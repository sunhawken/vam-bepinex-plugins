using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using MeshVR;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.StockingPresetKeeper
{
	// Headless: nothing to add to a person, no window. Keeps one clothing preset applied.
	//
	// What it does
	//  - every Person atom that is FEMALE is watched. When the clothing item  Skynet.Body_Stocking_V2.1 / "Skynet:Body Stocking V2"  is worn
	//    on it, the item preset  Custom/Clothing/Female/Skynet/Body Stocking V2/Body Stocking V2_Default.vap  is checked every few seconds.
	//  - the check compares every parameter the preset lists (the item's wrap / sim / item-control / material storables and the plugin
	//    manager's plugin list with each plugin's own values) with what is live. If it is all the same nothing is touched - no reload,
	//    no reset. Only when something is different is the preset loaded, exactly like the preset's Load button on the item.
	//  - the Crotchless variant ("Skynet:Body Stocking V2 Crotchless") is a different item and is left alone.
	//
	// Why it is careful (read from VaM's own code)
	//  - Loading an item preset (not merge) removes and re-creates the item's plugins (MVRPluginManager.PreRestore / LateRestoreFromJSON) and
	//    sets every unlisted parameter to default, so it must never run when nothing differs.
	//  - Stopper's ClothingPluginManager loads the item's own default.clothingplugins when the item is added (its PostLoad) and the scene's
	//    saved copy when a scene loads, so this runs again after that and puts the preset back.
	//  - the item must be settled first (scene not loading, a few seconds since it became active / since the last load), and after a load
	//    the same check runs again; a parameter that still differs after two checks (the plugin rewrites it) is left out of the check, so it
	//    cannot cause a reload loop. At most MaxLoadsPerActivation loads per time the item is put on.
	[BepInPlugin("com.zerot.stockingpresetkeeper", "Stocking Preset Keeper", "1.0.0")]
	public sealed class StockingPresetKeeper : BaseUnityPlugin
	{
		private ConfigEntry<bool> cfgActive, cfgFemaleOnly, cfgStrictPlugins, cfgVerbose;
		private ConfigEntry<float> cfgInterval, cfgSettle, cfgAfterLoadSettle;
		private ConfigEntry<int> cfgMaxLoads;
		private ConfigEntry<string> cfgPackage, cfgInternalUid, cfgPresetFile;

		private sealed class ItemState
		{
			public bool wasActive;
			public float settleUntil;
			public int loads;
			public bool verifying;      // a load happened and has not been confirmed yet
			public int verifyPasses;
			public readonly HashSet<string> ignored = new HashSet<string>();
			public bool reportedOk, reportedGiveUp;
			public string lastDiffSignature = "";
			public float nextAllowedLoad;
		}

		private readonly Dictionary<string, ItemState> states = new Dictionary<string, ItemState>();
		private float nextTick;
		private JSONClass preset;
		private DateTime presetStamp = DateTime.MinValue;
		private long presetSize = -1;
		private string presetLoadedFrom = "";
		private bool presetErrorLogged;

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn the keeper on / off. Off = nothing is checked or loaded.");
			cfgInterval = Config.Bind<float>("General", "IntervalSeconds", 5f, "How often the item is checked against the preset (1 - 120). A check that finds no difference changes nothing.");
			cfgFemaleOnly = Config.Bind<bool>("General", "FemaleOnly", true, "Only Person atoms that are female.");
			cfgSettle = Config.Bind<float>("Safety", "SettleSeconds", 6f, "Seconds the item must have been worn (and no scene loading) before it is checked, so VaM's and Stopper's own start-up finishes first.");
			cfgAfterLoadSettle = Config.Bind<float>("Safety", "AfterLoadSettleSeconds", 8f, "Seconds to wait after loading the preset before checking again (its plugins are re-created and need a moment).");
			cfgMaxLoads = Config.Bind<int>("Safety", "MaxLoadsPerActivation", 3, "Most times the preset is loaded each time the item is put on. A parameter that stays different after a load is left out of the check.");
			cfgStrictPlugins = Config.Bind<bool>("Safety", "StrictPluginList", true, "The item's plugin list must be exactly the preset's (an extra plugin counts as a difference).");
			cfgVerbose = Config.Bind<bool>("Safety", "Verbose", false, "Log every check, not only loads and problems.");
			cfgPackage = Config.Bind<string>("Item", "PackageUid", "Skynet.Body_Stocking_V2.1", "The .var the item comes from (package id with version).");
			cfgInternalUid = Config.Bind<string>("Item", "ItemUid", "Skynet:Body Stocking V2", "The clothing item's own uid (from its .vam).");
			cfgPresetFile = Config.Bind<string>("Item", "PresetFile", "Custom/Clothing/Female/Skynet/Body Stocking V2/Body Stocking V2_Default.vap", "The item preset to keep applied (relative to the VaM folder).");
		}

		private void Update()
		{
			if (!cfgActive.Value) return;
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + Mathf.Clamp(cfgInterval.Value, 1f, 120f);
			try { Tick(); }
			catch (Exception ex) { Logger.LogWarning("Stocking Preset Keeper: " + ex.Message); }
		}

		// ------------------------------------------------------------------ one check of every person
		private void Tick()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null) return;
			bool loading = sc.isLoading;
			if (!ReadPreset()) return;

			List<Atom> atoms = sc.GetAtoms();
			HashSet<string> seen = new HashSet<string>();
			bool didLoad = false;   // one preset load per tick, so the frame hitch is never doubled
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom atom = atoms[i];
				if (atom == null || atom.type != "Person" || !atom.on) continue;
				DAZCharacterSelector cs = null;
				try { cs = atom.GetStorableByID("geometry") as DAZCharacterSelector; } catch { }
				if (cs == null) continue;
				if (cfgFemaleOnly.Value && cs.gender != DAZCharacterSelector.Gender.Female) continue;
				DAZClothingItem[] items = cs.clothingItems;
				if (items == null) continue;
				for (int k = 0; k < items.Length; k++)
				{
					DAZClothingItem item = items[k];
					if (item == null || !IsTarget(item)) continue;
					string key = atom.uid + "|" + item.uid;
					seen.Add(key);
					ItemState st;
					if (!states.TryGetValue(key, out st)) { st = new ItemState(); states[key] = st; }
					try { didLoad |= Check(atom, item, st, key, loading, didLoad); }
					catch (Exception ex) { Logger.LogWarning("Stocking Preset Keeper: " + atom.uid + ": " + ex.Message); }
				}
			}
			// forget items that went away
			List<string> gone = null;
			foreach (string k in states.Keys) if (!seen.Contains(k)) { if (gone == null) gone = new List<string>(); gone.Add(k); }
			if (gone != null) foreach (string k in gone) states.Remove(k);
		}

		private bool IsTarget(DAZClothingItem item)
		{
			string pkg = (cfgPackage.Value ?? "").Trim();
			string uid = (cfgInternalUid.Value ?? "").Trim();
			if (uid.Length == 0) return false;
			bool uidOk = item.internalUid == uid;
			if (!uidOk && item.uid != null) uidOk = item.uid.EndsWith(":" + uid, StringComparison.Ordinal) || item.uid.EndsWith("/" + uid, StringComparison.Ordinal);
			if (!uidOk) return false;
			if (pkg.Length == 0) return true;
			if (item.packageUid == pkg) return true;
			return item.uid != null && item.uid.StartsWith(pkg + ":", StringComparison.Ordinal);
		}

		private bool Check(Atom atom, DAZClothingItem item, ItemState st, string key, bool sceneLoading, bool alreadyLoadedThisTick)
		{
			float now = Time.unscaledTime;
			if (!item.active)
			{
				if (st.wasActive && cfgVerbose.Value) Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": item taken off.");
				st.wasActive = false;
				return false;
			}
			if (!st.wasActive)
			{
				// just put on (or the scene just loaded): VaM / Stopper load the item's own defaults now, so wait
				st.wasActive = true;
				st.settleUntil = now + Mathf.Max(0f, cfgSettle.Value);
				st.loads = 0; st.verifying = false; st.verifyPasses = 0; st.reportedOk = false; st.reportedGiveUp = false;
				st.ignored.Clear(); st.lastDiffSignature = "";
				if (cfgVerbose.Value) Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": item is on, first check in " + cfgSettle.Value.ToString("0.#") + " s.");
				return false;
			}
			if (sceneLoading)
			{
				st.settleUntil = Mathf.Max(st.settleUntil, now + Mathf.Max(0f, cfgSettle.Value));
				return false;
			}
			if (now < st.settleUntil) return false;

			PresetManager pm = FindPresetManager(item);
			if (pm == null) { LogOnce(st, "no preset manager on the item"); return false; }

			List<PresetDiff> diffs = new List<PresetDiff>();
			int skipped = 0;
			CompareAll(atom, item, diffs, st.ignored, ref skipped);

			if (diffs.Count == 0)
			{
				if (st.verifying) { Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": preset is on now (confirmed after loading it)."); st.verifying = false; st.verifyPasses = 0; }
				else if (!st.reportedOk) { Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": preset already applied" + (skipped > 0 ? " (" + skipped + " parameters not readable, skipped)" : "") + " - left alone."); }
				else if (cfgVerbose.Value) Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": same as the preset.");
				st.reportedOk = true;
				st.lastDiffSignature = "";
				return false;
			}
			st.reportedOk = false;

			// a load happened and things still differ: after two checks the parameters that are still different are given up on (the plugin rewrites them)
			if (st.verifying)
			{
				st.verifyPasses++;
				if (st.verifyPasses >= 2)
				{
					foreach (PresetDiff d in diffs) st.ignored.Add(d.Key);
					Logger.LogWarning("Stocking Preset Keeper: " + atom.uid + ": " + diffs.Count + " parameter(s) stay different after loading the preset, not checked any more: " + Describe(diffs, 6));
					st.verifying = false; st.verifyPasses = 0;
					return false;
				}
				st.settleUntil = now + Mathf.Max(0f, cfgAfterLoadSettle.Value) * 0.5f;
				if (cfgVerbose.Value) Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": still " + diffs.Count + " different after the load, checking again.");
				return false;
			}

			string sig = Signature(diffs);
			if (st.loads >= Mathf.Max(1, cfgMaxLoads.Value))
			{
				if (!st.reportedGiveUp)
				{
					st.reportedGiveUp = true;
					Logger.LogWarning("Stocking Preset Keeper: " + atom.uid + ": loaded the preset " + st.loads + " times and it still differs (" + Describe(diffs, 6) + "). Not loading it again until the item is put on again or the preset file changes.");
				}
				return false;
			}
			if (alreadyLoadedThisTick || now < st.nextAllowedLoad) return false;   // next tick

			Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": " + diffs.Count + " difference(s) from the preset: " + Describe(diffs, 8));
			bool ok = LoadPreset(atom, pm);
			st.loads++;
			st.lastDiffSignature = sig;
			if (ok)
			{
				st.verifying = true; st.verifyPasses = 0;
				st.settleUntil = now + Mathf.Max(0f, cfgAfterLoadSettle.Value);
				st.nextAllowedLoad = st.settleUntil;
			}
			else st.nextAllowedLoad = now + 30f;
			return ok;
		}

		// ------------------------------------------------------------------ the preset file
		private bool ReadPreset()
		{
			string rel = (cfgPresetFile.Value ?? "").Trim().Replace('\\', '/');
			if (rel.Length == 0) return false;
			string full;
			try { full = Path.GetFullPath(rel); } catch { return false; }
			try
			{
				FileInfo fi = new FileInfo(full);
				if (!fi.Exists)
				{
					if (!presetErrorLogged) { Logger.LogWarning("Stocking Preset Keeper: preset file not found: " + rel); presetErrorLogged = true; }
					preset = null;
					return false;
				}
				if (preset != null && presetLoadedFrom == full && fi.LastWriteTimeUtc == presetStamp && fi.Length == presetSize) return true;
				JSONClass jc = JSON.Parse(File.ReadAllText(full)) as JSONClass;
				if (jc == null || jc["storables"] == null || jc["storables"].AsArray == null) { if (!presetErrorLogged) { Logger.LogWarning("Stocking Preset Keeper: not a preset file: " + rel); presetErrorLogged = true; } preset = null; return false; }
				bool changed = presetStamp != DateTime.MinValue;
				preset = jc; presetStamp = fi.LastWriteTimeUtc; presetSize = fi.Length; presetLoadedFrom = full; presetErrorLogged = false;
				Logger.LogInfo("Stocking Preset Keeper: preset " + (changed ? "changed, re-read: " : "read: ") + rel + " (" + jc["storables"].AsArray.Count + " storables)");
				if (changed) foreach (ItemState st in states.Values) { st.loads = 0; st.reportedGiveUp = false; st.ignored.Clear(); st.verifying = false; }
				return true;
			}
			catch (Exception ex)
			{
				if (!presetErrorLogged) { Logger.LogWarning("Stocking Preset Keeper: cannot read the preset: " + ex.Message); presetErrorLogged = true; }
				return false;
			}
		}

		// ------------------------------------------------------------------ compare live against the preset
		private void CompareAll(Atom atom, DAZClothingItem item, List<PresetDiff> diffs, ICollection<string> skip, ref int skipped)
		{
			JSONStorable[] live = item.GetComponentsInChildren<JSONStorable>(true);
			Dictionary<string, JSONStorable> byId = new Dictionary<string, JSONStorable>();
			for (int i = 0; i < live.Length; i++)
			{
				JSONStorable s = live[i];
				if (s == null || s is PresetManagerControl) continue;
				string id = null;
				try { id = s.storeId; } catch { }
				if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) byId.Add(id, s);
			}

			JSONArray storables = preset["storables"].AsArray;
			for (int e = 0; e < storables.Count; e++)
			{
				JSONClass entry = storables[e] as JSONClass;
				if (entry == null || !entry.HasKey("id")) continue;
				string id = entry["id"];
				JSONStorable target;
				if (!byId.TryGetValue(id, out target)) target = FindOnAtom(atom, id);
				if (target == null)
				{
					diffs.Add(new PresetDiff { Id = id, Path = "(storable)", Want = "present", Have = null });
					continue;
				}
				JSONClass have = SafeJson(target);
				bool isPluginHost = entry.HasKey("plugins");
				if (isPluginHost)
				{
					ComparePluginHost(id, entry, have, byId, target, diffs, skip, ref skipped);
				}
				else
				{
					PresetCompare.CompareStorable(id, entry, have, skip, diffs, ref skipped);
				}
			}
		}

		// the Stopper plugin manager entry: "plugins" {slot: url} and "storables" [ {id, ...} of each inner plugin ]
		private void ComparePluginHost(string id, JSONClass entry, JSONClass have, Dictionary<string, JSONStorable> byId, JSONStorable host, List<PresetDiff> diffs, ICollection<string> skip, ref int skipped)
		{
			JSONClass wantPlugins = entry["plugins"].AsObject;
			JSONClass havePlugins = have != null && have.HasKey("plugins") ? have["plugins"].AsObject : null;
			if (wantPlugins != null)
			{
				PresetCompare.CompareNode(id, "plugins", wantPlugins, havePlugins ?? new JSONClass(), skip, diffs, ref skipped);
				if (cfgStrictPlugins.Value && havePlugins != null)
				{
					foreach (string slot in havePlugins.Keys)
					{
						if (!wantPlugins.HasKey(slot) && !skip.Contains(id + "/plugins/" + slot)) diffs.Add(new PresetDiff { Id = id, Path = "plugins/" + slot, Want = null, Have = havePlugins[slot].Value });
					}
				}
			}
			// every other plain key of the entry
			foreach (string k in entry.Keys)
			{
				if (k == "id" || k == "plugins" || k == "storables") continue;
				PresetCompare.CompareNode(id, k, entry[k], have != null && have.HasKey(k) ? have[k] : null, skip, diffs, ref skipped);
			}
			JSONArray inner = entry.HasKey("storables") ? entry["storables"].AsArray : null;
			if (inner == null) return;
			// the plugins the manager hosts: every MVRScript below the same plugin manager, found by storeId
			Dictionary<string, JSONStorable> scripts = new Dictionary<string, JSONStorable>();
			MVRPluginManager mgr = host.GetComponentInParent<MVRPluginManager>();
			MVRScript[] all = (mgr != null ? mgr.GetComponentsInChildren<MVRScript>(true) : host.GetComponentsInChildren<MVRScript>(true));
			for (int i = 0; i < all.Length; i++)
			{
				MVRScript s = all[i];
				if (s == null || s == host) continue;
				string sid = null;
				try { sid = s.storeId; } catch { }
				if (!string.IsNullOrEmpty(sid) && !scripts.ContainsKey(sid)) scripts.Add(sid, s);
			}
			for (int n = 0; n < inner.Count; n++)
			{
				JSONClass ie = inner[n] as JSONClass;
				if (ie == null || !ie.HasKey("id")) continue;
				string iid = ie["id"];
				string fullId = id + "#" + iid;   // key used for ignore bookkeeping
				JSONStorable script;
				if (!scripts.TryGetValue(iid, out script))
				{
					diffs.Add(new PresetDiff { Id = fullId, Path = "(plugin)", Want = "loaded", Have = null });
					continue;
				}
				PresetCompare.CompareStorable(fullId, ie, SafeJson(script), skip, diffs, ref skipped);
			}
		}

		private static JSONStorable FindOnAtom(Atom atom, string id)
		{
			try { return atom.GetStorableByID(id); } catch { return null; }
		}

		private static JSONClass SafeJson(JSONStorable s)
		{
			try { return s.GetJSON(true, true, true); }
			catch { return null; }
		}

		// ------------------------------------------------------------------ load it (what the item's preset Load button does)
		private bool LoadPreset(Atom atom, PresetManager pm)
		{
			string rel = (cfgPresetFile.Value ?? "").Trim().Replace('\\', '/');
			try
			{
				string name = pm.GetPresetNameFromFilePath(rel);
				if (name == null) { Logger.LogWarning("Stocking Preset Keeper: the preset path does not belong to this item's preset folder: " + rel); return false; }
				pm.presetName = name;
				if (!pm.CheckPresetExistance()) { Logger.LogWarning("Stocking Preset Keeper: VaM cannot find the preset '" + name + "' for this item."); return false; }
				bool ok = pm.LoadPreset();
				Logger.LogInfo("Stocking Preset Keeper: " + atom.uid + ": loaded preset '" + name + "' " + (ok ? "(done)" : "(failed)"));
				return ok;
			}
			catch (Exception ex)
			{
				Logger.LogWarning("Stocking Preset Keeper: loading the preset failed: " + ex.Message);
				return false;
			}
		}

		private static PresetManager FindPresetManager(DAZClothingItem item)
		{
			PresetManager pm = item.GetComponent<PresetManager>();
			if (pm == null) pm = item.GetComponentInChildren<PresetManager>(true);
			if (pm == null) pm = item.GetComponentInParent<PresetManager>();
			return pm;
		}

		// ------------------------------------------------------------------ small helpers
		private static string Signature(List<PresetDiff> diffs)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < diffs.Count; i++) sb.Append(diffs[i].Key).Append('|');
			return sb.ToString();
		}

		private static string Describe(List<PresetDiff> diffs, int max)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < diffs.Count && i < max; i++) { if (i > 0) sb.Append("; "); sb.Append(diffs[i].ToString()); }
			if (diffs.Count > max) sb.Append("; ... +").Append(diffs.Count - max).Append(" more");
			return sb.ToString();
		}

		private void LogOnce(ItemState st, string msg)
		{
			if (st.lastDiffSignature == msg) return;
			st.lastDiffSignature = msg;
			Logger.LogWarning("Stocking Preset Keeper: " + msg);
		}
	}
}
