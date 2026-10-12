using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// Headless standalone port of CheesyFX's ShakeIt package (ShakeIt + FlexIt + WorkOut, the three plugins of CheesyFX.ShakeIt.42.var):
	// it goes on female Person atoms by itself, with the settings of Custom\Atom\Person\Plugins\Preset_Butt.vap as the starting point.
	//
	//  ShakeIt  - jiggle: the acceleration of the glutes, thighs, calves, abs, side abs, upper arms, breasts and cheeks drives their SI_* morphs
	//             (4 axes each, per part Upper / Mid / Lower or combined, with a relaxed and a tensed set of strength / frequency / damping)
	//  FlexIt   - pose and motion morphs (FI_*) for ankle, knee, thigh, abdomen, shoulder, elbow, hand (4 fingers) and neck, with support
	//             detection through collisions; the Renderer Fps option for frame-by-frame video capture
	//  WorkOut  - movement builds work, work builds exhaustion, both raise the skin gloss (sweat); threshold / value triggers on the numbers
	//
	// Without a window: every control is a parameter or action of the storable "ZeroT.ShakeIt" on the person (so triggers, UIAssist and other
	// plugins can drive it), presets are the .var's own JSON files in Saves\PluginData\CheesyFX\ShakeIt and \FlexIt, and the scene file keeps
	// what differs from the defaults. Errors in one module never stop the others; a person whose modules keep failing is rebuilt.
	// If the .var's own plugins (ShakeIt / FlexIt / WorkOut) are on a person, that person is left to them.
	[BepInPlugin("com.zerot.shakeit", "ShakeIt Standalone", "1.0.0")]
	public sealed class Plugin : BaseUnityPlugin
	{
		public const string StoreId = "ZeroT.ShakeIt";
		private static Plugin instance;

		private ConfigEntry<bool> cfgActive, cfgShake, cfgFlex, cfgWork, cfgMales, cfgUseDefaults, cfgAutoPresets, cfgDebug;
		private ConfigEntry<string> cfgDefaultsFile, cfgFps;
		private ConfigEntry<int> cfgErrors;
		private readonly Settings settings = new Settings();
		private readonly Dictionary<Atom, Rig> rigs = new Dictionary<Atom, Rig>();
		private readonly Dictionary<Atom, ShakeHost> shells = new Dictionary<Atom, ShakeHost>();
		private readonly Dictionary<Atom, float> retryAt = new Dictionary<Atom, float>();
		private readonly Dictionary<Atom, int> retries = new Dictionary<Atom, int>();
		private readonly Dictionary<Atom, float> rebuildAt = new Dictionary<Atom, float>();
		private readonly HashSet<Atom> skipLogged = new HashSet<Atom>();
		private readonly HashSet<Atom> originals = new HashSet<Atom>();
		private float nextDiscover, nextOriginalCheck;
		private readonly List<Rig> updBuf = new List<Rig>();
		private readonly Dictionary<Atom, float> deepAt = new Dictionary<Atom, float>();
		private bool wasActive = true;

		public static void ForgetDefaultsCache() { Rig.ForgetDefaults(); }

		private void Awake()
		{
			instance = this;
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn everything off (morphs back to 0, controls removed).");
			cfgShake = Config.Bind<bool>("General", "ShakeIt", true, "Jiggle morphs (module on for new persons).");
			cfgFlex = Config.Bind<bool>("General", "FlexIt", true, "Pose and motion morphs (module on for new persons).");
			cfgWork = Config.Bind<bool>("General", "WorkOut", true, "Exhaustion and sweat gloss (module on for new persons).");
			cfgMales = Config.Bind<bool>("General", "IncludeMales", false, "Also run on male persons (the .var supports them; this is meant for women).");
			cfgUseDefaults = Config.Bind<bool>("Defaults", "UsePresetDefaults", true, "Start every person from the settings in the preset file below.");
			cfgDefaultsFile = Config.Bind<string>("Defaults", "PresetFile", "Custom/Atom/Person/Plugins/Preset_Butt.vap", "A person-plugin preset (.vap) with the ShakeIt / FlexIt / WorkOut settings to start from. If it cannot be read, a built-in copy of Preset_Butt is used.");
			cfgAutoPresets = Config.Bind<bool>("Defaults", "ApplySavedPresets", true, "Then apply UserDefaults.json and the latest preset named after the person from Saves\\PluginData\\CheesyFX\\ShakeIt and \\FlexIt (as the .var does).");
			cfgFps = Config.Bind<string>("Video", "RendererFps", "Realtime", "Realtime, or the output frame rate (30 36 40 45 60 72 80 90 120 144 165 240 288) when a frame renderer slows time down.");
			cfgErrors = Config.Bind<int>("Robustness", "ErrorsBeforeRebuild", 20, "A module that fails this many steps in a row gets its person rebuilt.");
			cfgDebug = Config.Bind<bool>("Robustness", "Debug", false, "Log stack traces.");
			Pull();
			Config.SettingChanged += OnSettingChanged;
		}

		private void Pull()
		{
			settings.ShakeOn = cfgShake.Value;
			settings.FlexOn = cfgFlex.Value;
			settings.WorkOn = cfgWork.Value;
			settings.RendererFps = cfgFps.Value;
			settings.UsePresetDefaults = cfgUseDefaults.Value;
			settings.DefaultsFile = cfgDefaultsFile.Value;
			settings.AutoApplyPresets = cfgAutoPresets.Value;
			settings.ShowDebug = cfgDebug.Value;
			settings.ErrorsBeforeRebuild = Math.Max(2, cfgErrors.Value);
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			ConfigEntryBase c = e.ChangedSetting;
			Pull();
			if (c == cfgDefaultsFile) Rig.ForgetDefaults();
			foreach (Rig r in rigs.Values)
			{
				if (c == cfgShake) r.shakeOn.Set(cfgShake.Value);
				else if (c == cfgFlex) r.flexOn.Set(cfgFlex.Value);
				else if (c == cfgWork) r.workOn.Set(cfgWork.Value);
				else if (c == cfgFps) r.ctx.Timing.SetOption(cfgFps.Value);
				r.PushWorkProxies();
			}
			if (c == cfgMales)
			{
				nextDiscover = 0f;
			}
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			foreach (Rig r in new List<Rig>(rigs.Values)) DropRig(r, true);
			foreach (ShakeHost h in new List<ShakeHost>(shells.Values)) KillHost(h);
			shells.Clear();
			if (instance == this) instance = null;
		}

		private void Say(string s) { Logger.LogInfo(s); }

		// ------------------------------------------------------------------ people

		private static bool IsPerson(Atom a)
		{
			return a != null && a.type == "Person";
		}

		private static DAZCharacterSelector Selector(Atom a)
		{
			try { return a.GetStorableByID("geometry") as DAZCharacterSelector; }
			catch { return null; }
		}

		private bool Wanted(Atom a)
		{
			DAZCharacterSelector dcs = Selector(a);
			if (dcs == null) return false;
			return dcs.gender == DAZCharacterSelector.Gender.Female || (cfgMales.Value && dcs.gender == DAZCharacterSelector.Gender.Male);
		}

		// the .var's own scripts, if loaded on this person: classes named ShakeIt / FlexIt / WorkOut in the CheesyFX namespace
		private static bool HasOriginal(Atom atom, bool deep)
		{
			try
			{
				// cheap: the plugin's storable id ("plugin#N_CheesyFX.ShakeIt")
				List<string> ids = atom.GetStorableIDs();
				for (int i = 0; ids != null && i < ids.Count; i++)
				{
					string s = ids[i];
					if (s != null && s.Contains("CheesyFX.") && (s.EndsWith(".ShakeIt") || s.EndsWith(".FlexIt") || s.EndsWith(".WorkOut"))) return true;
				}
				// the walk over every component of the person is the expensive part (a visible hitch): rarely
				if (!deep) return false;
				MonoBehaviour[] all = atom.GetComponentsInChildren<MonoBehaviour>(true);
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i] == null) continue;
					Type t = all[i].GetType();
					if (t.Namespace == "CheesyFX" && (t.Name == "ShakeIt" || t.Name == "FlexIt" || t.Name == "WorkOut"))
					{
						return true;
					}
				}
			}
			catch
			{
			}
			return false;
		}

		private ShakeHost MakeHost(Atom a)
		{
			GameObject go = new GameObject(StoreId);
			go.transform.SetParent(a.transform, false);
			ShakeHost h = go.AddComponent<ShakeHost>();
			h.overrideId = StoreId;
			h.exclude = false;
			h.containingAtom = a;
			a.RegisterAdditionalStorable(h);
			return h;
		}

		private void KillHost(ShakeHost h)
		{
			if (h == null) return;
			try
			{
				if (h.containingAtom != null) h.containingAtom.UnregisterAdditionalStorable(h);
			}
			catch
			{
			}
			try { UnityEngine.Object.Destroy(h.gameObject); } catch { }
		}

		private void DropRig(Rig r, bool killHost)
		{
			ShakeHost h = r.host;
			r.Dispose();
			rigs.Remove(r.atom);
			if (h != null) h.rig = null;
			if (killHost)
			{
				KillHost(h);
				shells.Remove(r.atom);
			}
		}

		private void Update()
		{
			if (!cfgActive.Value)
			{
				if (wasActive)
				{
					foreach (Rig r in new List<Rig>(rigs.Values)) DropRig(r, true);
					foreach (ShakeHost h in new List<ShakeHost>(shells.Values)) KillHost(h);
					shells.Clear();
				}
				wasActive = false;
				return;
			}
			wasActive = true;
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			if (Time.unscaledTime >= nextDiscover)
			{
				nextDiscover = Time.unscaledTime + 1f;
				try
				{
					Discover(sc);
				}
				catch (Exception e)
				{
					Logger.LogWarning("Looking for persons: " + e.Message);
				}
			}
			if (sc.isLoading)
			{
				return;
			}
			updBuf.Clear();
			updBuf.AddRange(rigs.Values);
			for (int ri = 0; ri < updBuf.Count; ri++)
			{
				Rig r = updBuf[ri];
				try
				{
					r.Update();
					r.Slow();
				}
				catch (Exception e)
				{
					Logger.LogWarning("Update on " + r.atom.uid + ": " + e.Message);
				}
			}
		}

		private void Discover(SuperController sc)
		{
			List<Atom> atoms = sc.GetAtoms();
			// atoms that are gone (a scene load, a delete): forget them without touching them
			List<Atom> gone = new List<Atom>();
			foreach (Atom a in rigs.Keys) if (a == null || !a || !atoms.Contains(a)) gone.Add(a);
			foreach (Atom a in shells.Keys) if (!gone.Contains(a) && (a == null || !a || !atoms.Contains(a))) gone.Add(a);
			foreach (Atom a in gone)
			{
				Rig r;
				if (a != null && rigs.TryGetValue(a, out r))
				{
					r.host = null;
					DropRigGone(r);
				}
				if (a != null) { shells.Remove(a); retryAt.Remove(a); retries.Remove(a); rebuildAt.Remove(a); skipLogged.Remove(a); originals.Remove(a); deepAt.Remove(a); }
			}
			bool checkOriginal = Time.unscaledTime >= nextOriginalCheck;
			if (checkOriginal) nextOriginalCheck = Time.unscaledTime + 3f;
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (!IsPerson(a)) continue;
				Rig rig;
				bool has = rigs.TryGetValue(a, out rig);
				if (!Wanted(a))
				{
					if (has) DropRig(rig, true);
					else if (shells.ContainsKey(a)) { KillHost(shells[a]); shells.Remove(a); }
					continue;
				}
				if (checkOriginal)
				{
					float dAt;
						bool deep = !deepAt.TryGetValue(a, out dAt) || Time.unscaledTime >= dAt;
						if (deep) deepAt[a] = Time.unscaledTime + 30f + (a.uid == null ? 0 : (a.uid.Length % 7));
						if (HasOriginal(a, deep)) originals.Add(a); else if (deep) originals.Remove(a);
				}
				if (originals.Contains(a))
				{
					if (has) DropRig(rig, true);
					else if (shells.ContainsKey(a)) { KillHost(shells[a]); shells.Remove(a); }
					if (skipLogged.Add(a))
					{
						Say("The .var's own ShakeIt / FlexIt / WorkOut is on " + a.uid + ": this person is left to it.");
					}
					continue;
				}
				skipLogged.Remove(a);
				if (sc.isLoading)
				{
					// a scene is loading: only make the storable, so the scene can hand its saved settings to it
					if (!has && !shells.ContainsKey(a))
					{
						try { shells[a] = MakeHost(a); } catch (Exception e) { Logger.LogWarning("Storable on " + a.uid + ": " + e.Message); }
					}
					continue;
				}
				if (has)
				{
					if (rig.NeedsRebuild) ScheduleRebuild(a, rig);
					else if (!rig.Female && !cfgMales.Value) DropRig(rig, true);
					continue;
				}
				float at;
				if (retryAt.TryGetValue(a, out at) && Time.unscaledTime < at) continue;
				TryBuild(a);
			}
			// rebuilds that were waiting
			foreach (Atom a in new List<Atom>(rebuildAt.Keys))
			{
				if (Time.unscaledTime < rebuildAt[a]) continue;
				rebuildAt.Remove(a);
				Rig r;
				if (rigs.TryGetValue(a, out r) && r.NeedsRebuild)
				{
					JSONClass keep = new JSONClass();
					try { r.StoreState(keep); } catch { }
					DropRig(r, true);
					ShakeHost h = null;
					try { h = MakeHost(a); h.PendingState = keep.Count > 0 ? keep : null; shells[a] = h; } catch { }
					Say("Rebuilding " + a.uid + " after repeated errors.");
					retryAt.Remove(a);
					TryBuild(a);
				}
			}
		}

		// an atom that no longer exists: free what we hold, never touch the atom
		private void DropRigGone(Rig r)
		{
			try { r.Dispose(); } catch { }
			rigs.Remove(r.atom);
			shells.Remove(r.atom);
		}

		private void ScheduleRebuild(Atom a, Rig r)
		{
			if (!rebuildAt.ContainsKey(a)) rebuildAt[a] = Time.unscaledTime + 3f;
		}

		private void TryBuild(Atom a)
		{
			Rig rig = null;
			ShakeHost shell;
			shells.TryGetValue(a, out shell);
			try
			{
				rig = new Rig(a, settings, Say, delegate (IEnumerator e) { StartCoroutine(e); }, shell);
				rig.Build();
				rigs[a] = rig;
				retries.Remove(a);
				retryAt.Remove(a);
				Say("ShakeIt on " + a.uid + " (" + rig.MorphCount + " morphs found).");
			}
			catch (Exception e)
			{
				if (rig != null)
				{
					try { rig.Dispose(); } catch { }
				}
				int n;
				retries.TryGetValue(a, out n);
				n++;
				retries[a] = n;
				retryAt[a] = Time.unscaledTime + (n < 5 ? 3f : 30f);
				if (n <= 2 || n % 10 == 0) Logger.LogWarning("ShakeIt could not start on " + a.uid + " yet: " + e.Message);
			}
		}

		private void FixedUpdate()
		{
			if (!cfgActive.Value) return;
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading) return;
			foreach (Rig r in rigs.Values)
			{
				try
				{
					r.FixedTick();
				}
				catch (Exception e)
				{
					Logger.LogWarning("Step on " + r.atom.uid + ": " + e.Message);
				}
			}
		}
	}
}
