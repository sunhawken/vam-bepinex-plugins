using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// the storable the person carries: its parameters mirror the three modules' controls; the scene file keeps the settings
	public sealed class ShakeHost : JSONStorable
	{
		public Rig rig;
		public JSONClass PendingState;      // restored from the scene before the person's rig existed

		public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
		{
			JSONClass jc = base.GetJSON(includePhysical, includeAppearance, true);
			if (rig != null)
			{
				try
				{
					rig.StoreState(jc);
				}
				catch (Exception e)
				{
					SuperController.LogError("ShakeIt state could not be stored: " + e.Message);
				}
			}
			else if (PendingState != null)
			{
				foreach (string k in new string[] { "ShakeIt", "FlexIt", "WorkOut" })
				{
					if (PendingState.HasKey(k)) jc[k] = PendingState[k];
				}
			}
			return jc;
		}

		public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical, bool restoreAppearance, bool setMissingToDefault)
		{
			base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
			if (jc == null || !(jc.HasKey("ShakeIt") || jc.HasKey("FlexIt") || jc.HasKey("WorkOut")))
			{
				return;
			}
			if (rig != null)
			{
				rig.ApplyState(jc);
			}
			else
			{
				PendingState = jc;
			}
		}
	}

	public sealed class Settings
	{
		public bool ShakeOn = true, FlexOn = true, WorkOn = true;
		public string RendererFps = "Realtime";
		public bool UsePresetDefaults = true;
		public string DefaultsFile = "Custom/Atom/Person/Plugins/Preset_Butt.vap";
		public bool AutoApplyPresets = true;
		public bool ShowDebug;
		public int ErrorsBeforeRebuild = 20;
	}

	// everything of one person: the three modules, their controls, presets, error handling
	public sealed class Rig
	{
		public const string ShakeDir = "Saves/PluginData/CheesyFX/ShakeIt/";
		public const string FlexDir = "Saves/PluginData/CheesyFX/FlexIt/";
		public static readonly string[] ShakeLabels = { "Glutes", "Thighs", "Calves", "Abs", "SideAbs", "UArms", "Breasts", "Cheeks" };
		public static readonly string[] FlexLabels = { "Ankle", "Knee", "Thigh", "Abdomen", "Shoulder", "Elbow", "Hand", "Neck" };
		private static readonly string[][] ShakeBodies =
		{
			new[] { "Glutes", "LGlute", "RGlute" },
			new[] { "Thigh", "lThigh", "rThigh" },
			new[] { "Calf", "lShin", "rShin" },
			new[] { "Abs", "abdomen" },
			new[] { "AbsSide", "abdomen" },
			new[] { "UArm", "lShldr", "rShldr" },
			new[] { "Breasts", "lPectoral", "rPectoral" },
			new[] { "Cheeks", "head" },
		};
		private static readonly string[][] FlexBodies =
		{
			new[] { "Foot" }, new[] { "Shin" }, new[] { "Thigh" }, new[] { "pelvis", "chest" }, new[] { "Shldr" }, new[] { "ForeArm" }, null, new[] { "head" }
		};
		private static readonly string[] Kinds = { "Strength", "Frequency", "Damping", "Min Accel", "Max Accel", "Max Morph" };
		private static readonly string[] Axes = { "X", "Y", "Z", "R" };
		private static readonly string[] FlexKinds = { "Amount", "S-Ratio", "Dynamic" };
		public static readonly string[] FpsChoices = { "Realtime", "30", "36", "40", "45", "60", "72", "80", "90", "120", "144", "165", "240", "288" };

		public readonly Atom atom;
		public readonly Ctx ctx;
		public readonly List<JRegion> shake = new List<JRegion>();
		public readonly List<FRegion> flex = new List<FRegion>();
		public WorkOutModule work;
		public ShakeHost host;
		public readonly Settings cfg;
		private readonly Action<string> log;
		public readonly Flag shakeOn = new Flag(true), flexOn = new Flag(true), workOn = new Flag(true);
		private string factoryShake, factoryFlex, baselineState;
		private string genderSeen;
		public bool pushing;
		private int selShake, selPart, selFlex;
		private bool tensedState, syncShake, syncFlex;
		private int errShake, errFlex, errWork, okShake, okFlex, okWork;

		// errors are forgiven only after a long quiet stretch, so a module that fails every other step still gets rebuilt
		private static void Ok(ref int errors, ref int streak)
		{
			streak++;
			if (streak >= 100)
			{
				errors = 0;
				streak = 0;
			}
		}
		public bool NeedsRebuild;
		public string LastError = "";
		public int TotalErrors;
		private float nextSlow, nextMorphProbe;
		private readonly HashSet<string> errorsSeen = new HashSet<string>();
		public string Status = "";

		private JSONStorableStringChooser chShakeRegion, chPart, chFlexRegion, chFps, chShakePreset, chFlexPreset;
		private JSONStorableBool bShakeRegionOn, bCombined, bTension, bTensed, bMonitor, bSyncShake, bFlexRegionOn, bSyncFlex;
		private readonly JSONStorableFloat[,] shakeFloats = new JSONStorableFloat[6, 4];
		private readonly JSONStorableFloat[,] flexFloats = new JSONStorableFloat[3, 4];
		private JSONStorableString sTension, sWorkInfo, sStatus;
		private JSONStorableFloat fPower, fWork, fExhaustion;
		private JSONStorableBool bAdjust;

		public Rig(Atom atom, Settings cfg, Action<string> log, Action<IEnumerator> startCo, ShakeHost existingHost)
		{
			this.atom = atom;
			this.cfg = cfg;
			this.log = log;
			ctx = new Ctx();
			ctx.Atom = atom;
			ctx.Log = log;
			ctx.StartCo = startCo;
			ctx.Morphs = new MorphLib(atom, log);
			host = existingHost;
		}

		public bool Female
		{
			get
			{
				DAZCharacterSelector dcs = ctx.Morphs.Selector;
				return dcs != null && dcs.gender == DAZCharacterSelector.Gender.Female;
			}
		}

		// ------------------------------------------------------------------ build

		public void Build()
		{
			if (ctx.Morphs.Selector == null || ctx.Morphs.UI == null)
			{
				throw new InvalidOperationException("the person has no morph interface yet");
			}
			genderSeen = ctx.Morphs.Gender;
			ctx.Timing.SetOption(cfg.RendererFps);
			for (int i = 0; i < ShakeBodies.Length; i++)
			{
				string[] b = ShakeBodies[i];
				if (i == 0 && genderSeen == "male") b = new[] { "Glutes", "lThigh", "rThigh" };
				string[] rbs = new string[b.Length - 1];
				Array.Copy(b, 1, rbs, 0, rbs.Length);
				shake.Add(new JRegion(ctx, i, b[0], rbs));
			}
			flex.Add(new AnkleRegion(ctx, 0));
			flex.Add(new KneeRegion(ctx, 1));
			flex.Add(new ThighRegion(ctx, 2));
			flex.Add(new AbdomenRegion(ctx, 3));
			flex.Add(new ShoulderRegion(ctx, 4));
			flex.Add(new ElbowRegion(ctx, 5));
			flex.Add(new HandRegion(ctx, 6));
			flex.Add(new NeckRegion(ctx, 7));
			for (int i = 0; i < flex.Count; i++) flex[i].Init(FlexBodies[i]);
			flex[1].children = new FRegion[] { flex[0] };
			flex[2].children = new FRegion[] { flex[1], flex[0] };
			work = new WorkOutModule(ctx, CollectFlexMorphs);
			work.ResolveMorphs();
			work.FetchSkin();
			factoryShake = StoreShake().ToString();
			factoryFlex = StoreFlex().ToString();
			ctx.Morphs.ResetAll();
			shakeOn.changed = delegate (bool v) { if (!v) ResetShake(); };
			flexOn.changed = delegate (bool v)
			{
				ctx.FlexOn = v;
				for (int i = 0; i < shake.Count; i++) shake[i].SetupTension(v);
				if (!v) ResetFlex();
			};
			workOn.changed = delegate (bool v) { work.Active = v; };
			shakeOn.Set(cfg.ShakeOn);
			flexOn.Set(cfg.FlexOn);
			if (!cfg.ShakeOn) shakeOn.val = false;
			if (!cfg.FlexOn) flexOn.val = false;
			workOn.val = cfg.WorkOn;
			work.Active = cfg.WorkOn;
			ApplyConfiguredDefaults();
			if (cfg.AutoApplyPresets)
			{
				ApplyLatestMatchingPreset(false);
				ApplyLatestMatchingPreset(true);
			}
			BuildHost();
			baselineState = StateString();
			if (host != null && host.PendingState != null)
			{
				ApplyState(host.PendingState);
				host.PendingState = null;
			}
			Status = "running";
			if (MorphCount == 0)
			{
				Status = "no ShakeIt / FlexIt morphs found";
				log("No CheesyFX ShakeIt / FlexIt morphs were found on " + atom.uid + " - is the package CheesyFX.ShakeIt installed? It will look again when the person's morphs change.");
			}
		}

		public int MorphCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < shake.Count; i++) n += shake[i].MorphCount;
				for (int i = 0; i < flex.Count; i++) n += flex[i].MorphCount;
				return n;
			}
		}

		private List<DAZMorph> CollectFlexMorphs()
		{
			List<DAZMorph> l = new List<DAZMorph>();
			HashSet<DAZMorph> seen = new HashSet<DAZMorph>();
			for (int i = 0; i < flex.Count; i++)
			{
				HandRegion hand = flex[i] as HandRegion;
				if (hand != null)
				{
					for (int f = 0; f < hand.fingers.Count; f++) AddAll(l, seen, hand.fingers[f].morphs);
				}
				else
				{
					AddAll(l, seen, flex[i].morphs);
				}
			}
			try
			{
				string prefix = ctx.Morphs.BestPrefix();
				string dir = prefix + ctx.Morphs.Dir(true);
				string[] files = FileManagerSecure.GetFiles(dir);
				for (int i = 0; files != null && i < files.Length; i++)
				{
					if (!files[i].EndsWith(".vmi")) continue;
					DAZMorph m = ctx.Morphs.UI.GetMorphByUid(FileManagerSecure.NormalizePath(files[i]));
					if (m != null && seen.Add(m)) l.Add(m);
				}
			}
			catch
			{
			}
			return l;
		}

		private static void AddAll(List<DAZMorph> l, HashSet<DAZMorph> seen, List<List<DAZMorph>> src)
		{
			for (int i = 0; i < src.Count; i++)
			{
				for (int j = 0; j < src[i].Count; j++)
				{
					if (src[i][j] != null && seen.Add(src[i][j])) l.Add(src[i][j]);
				}
			}
		}

		public void Dispose()
		{
			try
			{
				for (int i = 0; i < shake.Count; i++) shake[i].Dispose();
				for (int i = 0; i < flex.Count; i++) flex[i].Dispose();
				if (work != null) work.Dispose();
			}
			catch (Exception e)
			{
				log("Cleaning up " + atom.uid + ": " + e.Message);
			}
			shake.Clear();
			flex.Clear();
		}

		// ------------------------------------------------------------------ the physics step

		private void Fail(string module, Exception e, ref int counter)
		{
			counter++;
			TotalErrors++;
			LastError = module + ": " + e.Message;
			string key = module + e.GetType().Name + e.Message;
			if (errorsSeen.Add(key) || counter == cfg.ErrorsBeforeRebuild)
			{
				log(module + " on " + atom.uid + " failed: " + e.Message + (cfg.ShowDebug ? "\n" + e.StackTrace : ""));
			}
			if (counter >= cfg.ErrorsBeforeRebuild)
			{
				NeedsRebuild = true;
			}
		}

		public void FixedTick()
		{
			if (NeedsRebuild)
			{
				return;
			}
			try
			{
				ctx.Dyns.Tick();
				ctx.Timing.Tick();
			}
			catch (Exception e)
			{
				Fail("Physics", e, ref errShake);
				return;
			}
			if (flexOn.val)
			{
				try
				{
					for (int i = 0; i < flex.Count; i++) flex[i].Tick();
					Ok(ref errFlex, ref okFlex);
				}
				catch (Exception e)
				{
					okFlex = 0; Fail("FlexIt", e, ref errFlex);
				}
			}
			if (shakeOn.val)
			{
				try
				{
					for (int i = 0; i < shake.Count; i++) shake[i].Tick();
					Ok(ref errShake, ref okShake);
				}
				catch (Exception e)
				{
					okShake = 0; Fail("ShakeIt", e, ref errShake);
				}
			}
			if (workOn.val)
			{
				try
				{
					work.Tick(AllDyns());
					Ok(ref errWork, ref okWork);
				}
				catch (Exception e)
				{
					okWork = 0; Fail("WorkOut", e, ref errWork);
				}
			}
			if (bMonitor != null && bMonitor.val)
			{
				UpdateMonitor();
			}
			if (fPower != null)
			{
				fPower.valNoCallback = work.power.val;
				fWork.valNoCallback = work.workDone.val;
				fExhaustion.valNoCallback = work.exhaustion.val;
			}
		}

		private List<Dyn> dynCache;

		private List<Dyn> AllDyns()
		{
			if (dynCache == null)
			{
				dynCache = new List<Dyn>();
				ForceReceiver[] fr = atom.forceReceivers;
				for (int i = 0; fr != null && i < fr.Length; i++)
				{
					Rigidbody rb = fr[i] == null ? null : fr[i].GetComponent<Rigidbody>();
					if (rb != null) dynCache.Add(ctx.Dyns.Get(rb));
				}
			}
			return dynCache;
		}

		public void Update()
		{
			if (NeedsRebuild || !workOn.val) return;
			work.TickTriggers();
		}

		// once a second: morphs rebuilt by VaM, skin changes, gender changes
		public void Slow()
		{
			if (Time.unscaledTime < nextSlow || NeedsRebuild) return;
			nextSlow = Time.unscaledTime + 1f;
			try
			{
				string g = ctx.Morphs.Gender;
				if (g != genderSeen)
				{
					genderSeen = g;
					RefreshMorphs();
				}
				else if (ctx.Morphs.SentinelChanged())
				{
					RefreshMorphs();
				}
				else if (Time.unscaledTime >= nextMorphProbe)
				{
					nextMorphProbe = Time.unscaledTime + 5f;
					if (ctx.Morphs.AnyMissingNowFound())
					{
						RefreshMorphs();
						log("More morphs appeared on " + atom.uid + " (" + MorphCount + " now).");
					}
				}
				work.FetchSkin();
				if (sWorkInfo != null) { string wi = work.BuildInfo(); if (wi != null && sWorkInfo.val != wi) sWorkInfo.valNoCallback = wi; }
				if (sStatus != null)
				{
					string s = Status + (TotalErrors > 0 ? "; " + TotalErrors + " error(s), last: " + LastError : "") + "; " + MorphCount + " morph(s)";
					if (sStatus.val != s) sStatus.valNoCallback = s;
				}
			}
			catch (Exception e)
			{
				log("Housekeeping on " + atom.uid + ": " + e.Message);
			}
		}

		public void RefreshMorphs()
		{
			ctx.Morphs.Invalidate();
			for (int i = 0; i < shake.Count; i++) shake[i].GetMorphs();
			for (int i = 0; i < flex.Count; i++) flex[i].GetMorphs();
			work.ResolveMorphs();
			ctx.Morphs.ResetAll();
			errorsSeen.Clear();
		}

		private void ResetShake() { for (int i = 0; i < shake.Count; i++) shake[i].Reset(); }
		private void ResetFlex() { for (int i = 0; i < flex.Count; i++) flex[i].Reset(); }

		// ------------------------------------------------------------------ settings in and out

		public JSONClass StoreShake()
		{
			JSONClass jc = new JSONClass();
			for (int i = 0; i < shake.Count; i++)
			{
				shake[i].Reset();
				shake[i].Store(jc);
			}
			return jc;
		}

		public JSONClass StoreFlex()
		{
			JSONClass jc = new JSONClass();
			for (int i = 0; i < flex.Count; i++) flex[i].Store(jc);
			return jc;
		}

		public void LoadShake(JSONClass jc)
		{
			if (jc == null) return;
			for (int i = 0; i < shake.Count; i++) shake[i].Load(jc);
			PushShakeProxies();
		}

		public void LoadFlex(JSONClass jc)
		{
			if (jc == null) return;
			for (int i = 0; i < flex.Count; i++) flex[i].Load(jc);
			PushFlexProxies();
		}

		private string StateString()
		{
			JSONClass jc = new JSONClass();
			jc["ShakeIt"] = StoreShake();
			jc["FlexIt"] = StoreFlex();
			JSONClass w = new JSONClass();
			w["enabled"] = J.Bool(workOn.val);
			w["shakeEnabled"] = J.Bool(shakeOn.val);
			w["flexEnabled"] = J.Bool(flexOn.val);
			work.Store(w, "");
			jc["WorkOut"] = w;
			return jc.ToString();
		}

		// the scene keeps only what differs from the defaults this person started with
		public void StoreState(JSONClass jc)
		{
			string now = StateString();
			if (now == baselineState) return;
			JSONClass cur = JSON.Parse(now) as JSONClass;
			jc["ShakeIt"] = cur["ShakeIt"];
			jc["FlexIt"] = cur["FlexIt"];
			jc["WorkOut"] = cur["WorkOut"];
		}

		public void ApplyState(JSONClass jc)
		{
			if (jc == null) return;
			LoadShake(J.Obj(jc, "ShakeIt"));
			LoadFlex(J.Obj(jc, "FlexIt"));
			JSONClass w = J.Obj(jc, "WorkOut");
			if (w != null)
			{
				bool b;
				if (J.TryBool(w, "enabled", out b)) workOn.Set(b);
				if (J.TryBool(w, "shakeEnabled", out b)) shakeOn.Set(b);
				if (J.TryBool(w, "flexEnabled", out b)) flexOn.Set(b);
				work.Load(w, "", true);
				PushWorkProxies();
			}
		}

		private static JSONClass defaultsCache;
		private static string defaultsFor;

		public static JSONClass ReadDefaults(Settings cfg, Action<string> log)
		{
			string key = cfg.DefaultsFile;
			if (defaultsCache != null && defaultsFor == key) return defaultsCache;
			JSONClass root = null;
			try
			{
				if (!string.IsNullOrEmpty(key) && FileManagerSecure.FileExists(key))
				{
					JSONClass vap = JSON.Parse(FileManagerSecure.ReadAllText(key)) as JSONClass;
					root = FromVap(vap);
					if (root != null) log("Defaults read from " + key);
				}
			}
			catch (Exception e)
			{
				log("Defaults file " + key + " could not be read (" + e.Message + "); using the built-in copy of Preset_Butt.");
			}
			if (root == null)
			{
				root = JSON.Parse(DefaultsData.Json) as JSONClass;
			}
			defaultsCache = root;
			defaultsFor = key;
			return root;
		}

		public static void ForgetDefaults() { defaultsCache = null; }

		// a .vap holds the three plugins as storables "plugin#N_CheesyFX.ShakeIt" ...
		private static JSONClass FromVap(JSONClass vap)
		{
			JSONArray st = vap == null ? null : vap["storables"] as JSONArray;
			if (st == null) return null;
			JSONClass o = new JSONClass();
			bool any = false;
			for (int i = 0; i < st.Count; i++)
			{
				JSONClass s = st[i] as JSONClass;
				if (s == null || !s.HasKey("id")) continue;
				string id = s["id"].Value;
				if (id.EndsWith("CheesyFX.ShakeIt") && s.HasKey("ShakeIt")) { o["ShakeIt"] = s["ShakeIt"]; o["ShakeItEnabled"] = s.HasKey("enabled") ? s["enabled"].Value : "true"; any = true; }
				else if (id.EndsWith("CheesyFX.FlexIt") && s.HasKey("FlexIt")) { o["FlexIt"] = s["FlexIt"]; o["FlexItEnabled"] = s.HasKey("enabled") ? s["enabled"].Value : "true"; any = true; }
				else if (id.EndsWith("CheesyFX.WorkOut")) { o["WorkOut"] = s; any = true; }
			}
			return any ? o : null;
		}

		// Preset_Butt.vap: the settings every woman starts with
		public void ApplyConfiguredDefaults()
		{
			if (!cfg.UsePresetDefaults) return;
			JSONClass d = ReadDefaults(cfg, log);
			if (d == null) return;
			bool b;
			if (J.TryBool(d, "ShakeItEnabled", out b)) shakeOn.Set(b);
			if (J.TryBool(d, "FlexItEnabled", out b)) flexOn.Set(b);
			LoadShake(J.Obj(d, "ShakeIt"));
			LoadFlex(J.Obj(d, "FlexIt"));
			JSONClass w = J.Obj(d, "WorkOut");
			if (w != null)
			{
				if (J.TryBool(w, "enabled", out b)) workOn.Set(b);
				work.Load(w, "", true);
				PushWorkProxies();
			}
		}

		public static string FileName(string path)
		{
			int i = Math.Max(path.LastIndexOf('/'), path.LastIndexOf((char)92));
			return i >= 0 ? path.Substring(i + 1) : path;
		}

		private List<string> PresetFiles(string dir)
		{
			List<string> l = new List<string>();
			try
			{
				string[] paths = FileManagerSecure.GetFiles(dir);
				for (int i = 0; paths != null && i < paths.Length; i++)
				{
					if (paths[i].EndsWith(".json")) l.Add(FileName(paths[i]));
				}
			}
			catch
			{
			}
			return l;
		}

		// ApplyLatestMatchingPreset: UserDefaults first, then the last preset whose name starts with the person's name
		public void ApplyLatestMatchingPreset(bool isFlex)
		{
			string dir = isFlex ? FlexDir : ShakeDir;
			List<string> matching = new List<string>();
			string atomName = atom.name;
			bool male = ctx.Morphs.Gender == "male";
			List<string> files = PresetFiles(dir);
			for (int i = 0; i < files.Count; i++)
			{
				string n = files[i];
				if (n.StartsWith(atomName)) matching.Add(n);
				else if (male && n.StartsWith("UserDefaultsMale")) matching.Insert(0, n);
				else if (n.StartsWith("UserDefaults") && !n.StartsWith("UserDefaultsMale")) matching.Insert(0, n);
			}
			if (matching.Count > 0)
			{
				string name = matching[matching.Count - 1];
				if (ApplyFromFile(dir + name, isFlex)) log("Applied '" + dir + name + "' to " + atom.uid);
			}
		}

		public bool ApplyFromFile(string path, bool isFlex)
		{
			try
			{
				if (!FileManagerSecure.FileExists(path)) return false;
				string p = FileManagerSecure.NormalizePath(path);
				JSONClass jc = SuperController.singleton.LoadJSON(p).AsObject;
				if (jc == null) return false;
				if (isFlex) LoadFlex(jc); else LoadShake(jc);
				return true;
			}
			catch (Exception e)
			{
				log("Preset " + path + ": " + e.Message);
				return false;
			}
		}

		public string SuggestedName(string dir)
		{
			List<string> names = new List<string>();
			List<string> files = PresetFiles(dir);
			for (int i = 0; i < files.Count; i++) names.Add(files[i].Substring(0, files[i].Length - 5));
			for (int i = 1; i < 20; i++)
			{
				string s = atom.name + "." + i.ToString("00");
				if (!names.Contains(s)) return s;
			}
			return atom.name;
		}

		public string SavePreset(bool isFlex, string name)
		{
			string dir = isFlex ? FlexDir : ShakeDir;
			try
			{
				FileManagerSecure.CreateDirectory(dir);
				string path = dir + (string.IsNullOrEmpty(name) ? SuggestedName(dir) : name) + ".json";
				SuperController.singleton.SaveJSON(isFlex ? StoreFlex() : StoreShake(), path);
				log("Saved " + path);
				RefreshPresetLists();
				return path;
			}
			catch (Exception e)
			{
				log("Saving a preset: " + e.Message);
				return null;
			}
		}

		private void RefreshPresetLists()
		{
			if (chShakePreset != null) chShakePreset.choices = PresetFiles(ShakeDir);
			if (chFlexPreset != null) chFlexPreset.choices = PresetFiles(FlexDir);
		}

		// ------------------------------------------------------------------ the controls on the storable

		private JSONStorableFloat AddFloat(string name, float v, float min, float max, Action<float> cb)
		{
			JSONStorableFloat f = new JSONStorableFloat(name, v, delegate (float x) { if (!pushing) cb(x); }, min, max, false);
			f.storeType = JSONStorableParam.StoreType.Any;
			f.isStorable = false;
			host.RegisterFloat(f);
			return f;
		}

		private JSONStorableBool AddBool(string name, bool v, Action<bool> cb)
		{
			JSONStorableBool b = new JSONStorableBool(name, v, delegate (bool x) { if (!pushing) cb(x); });
			b.isStorable = false;
			host.RegisterBool(b);
			return b;
		}

		private JSONStorableStringChooser AddChooser(string name, string[] choices, string v, Action<string> cb)
		{
			JSONStorableStringChooser c = new JSONStorableStringChooser(name, new List<string>(choices), v, name, delegate (string x) { if (!pushing) cb(x); });
			c.isStorable = false;
			host.RegisterStringChooser(c);
			return c;
		}

		private void AddAction(string name, Action a)
		{
			host.RegisterAction(new JSONStorableAction(name, delegate { try { a(); } catch (Exception e) { log(name + ": " + e.Message); } }));
		}

		private void BuildHost()
		{
			if (host == null)
			{
				GameObject go = new GameObject(Plugin.StoreId);
				go.transform.SetParent(atom.transform, false);
				host = go.AddComponent<ShakeHost>();
				host.overrideId = Plugin.StoreId;
				host.exclude = false;
				host.containingAtom = atom;
				if (!atom.RegisterAdditionalStorable(host))
				{
					log("VaM did not accept the storable on " + atom.uid + "; the effects still run.");
				}
			}
			host.rig = this;

			sStatus = new JSONStorableString("Status", "");
			sStatus.isStorable = false;
			host.RegisterString(sStatus);
			AddBool("ShakeIt Enabled", shakeOn.val, delegate (bool v) { shakeOn.Set(v); });
			AddBool("FlexIt Enabled", flexOn.val, delegate (bool v) { flexOn.Set(v); });
			AddBool("WorkOut Enabled", workOn.val, delegate (bool v) { workOn.Set(v); });
			chFps = AddChooser("Renderer Fps", FpsChoices, cfg.RendererFps, delegate (string v) { ctx.Timing.SetOption(v); });

			// ---- ShakeIt editor
			chShakeRegion = AddChooser("ShakeIt Region", ShakeLabels, ShakeLabels[0], delegate (string v) { selShake = Array.IndexOf(ShakeLabels, v); if (selShake < 0) selShake = 0; ctx.SelectedRegion = selShake; PushShakeProxies(); });
			chPart = AddChooser("ShakeIt Part", new[] { "Upper", "Mid", "Lower" }, "Upper", delegate (string v) { selPart = v == "Mid" ? 1 : v == "Lower" ? 2 : 0; shake[selShake].selectedSubregion = selPart; PushShakeProxies(); });
			bShakeRegionOn = AddBool("ShakeIt Region Enabled", true, delegate (bool v) { shake[selShake].enabledJ.Set(v); });
			bCombined = AddBool("ShakeIt Combined", true, delegate (bool v) { shake[selShake].isCombined.Set(v); PushShakeProxies(); });
			bTension = AddBool("ShakeIt Tension Enabled", true, delegate (bool v) { shake[selShake].tensionEnabled.Set(v); });
			bTensed = AddBool("ShakeIt Configure Tensed State", false, delegate (bool v) { tensedState = v; PushShakeProxies(); });
			bMonitor = AddBool("ShakeIt Show Tension", false, delegate (bool v) { ctx.Monitor = v; if (!v) sTension.valNoCallback = ""; });
			bSyncShake = AddBool("ShakeIt Sync Axes", false, delegate (bool v) { syncShake = v; });
			sTension = new JSONStorableString("ShakeIt Tension Info", "");
			sTension.isStorable = false;
			host.RegisterString(sTension);
			for (int k = 0; k < 6; k++)
			{
				for (int c = 0; c < 4; c++)
				{
					int kind = k, comp = c;
					float max = kind == 0 ? 10f : kind == 1 ? 25f : kind == 2 ? 10f : kind == 5 ? 3f : 5f;
					shakeFloats[k, c] = AddFloat("ShakeIt " + Kinds[k] + " " + Axes[c], 0f, 0f, max, delegate (float v) { SetShakeComp(kind, comp, v); });
				}
			}

			// ---- FlexIt editor
			chFlexRegion = AddChooser("FlexIt Region", FlexLabels, FlexLabels[0], delegate (string v) { selFlex = Array.IndexOf(FlexLabels, v); if (selFlex < 0) selFlex = 0; PushFlexProxies(); });
			bFlexRegionOn = AddBool("FlexIt Region Enabled", true, delegate (bool v) { flex[selFlex].enabledJ.Set(v); });
			bSyncFlex = AddBool("FlexIt Sync Axes", false, delegate (bool v) { syncFlex = v; });
			for (int k = 0; k < 3; k++)
			{
				for (int c = 0; c < 4; c++)
				{
					int kind = k, comp = c;
					flexFloats[k, c] = AddFloat("FlexIt " + FlexKinds[k] + " " + Axes[c], 0f, 0f, kind == 1 ? 1f : kind == 0 ? 5f : 10f, delegate (float v) { SetFlexComp(kind, comp, v); });
				}
			}

			// ---- WorkOut
			bAdjust = AddBool("Adjust Gloss Texture Offset", work.adjustOffset.val, delegate (bool v) { work.adjustOffset.Set(v); });
			sWorkInfo = new JSONStorableString("glossInfo", "");
			sWorkInfo.isStorable = false;
			host.RegisterString(sWorkInfo);
			fPower = new JSONStorableFloat("Current Power", 0f, 0f, 2f, false, false);
			fWork = new JSONStorableFloat("Work Done Recently", 0f, 0f, 10000f, false, false);
			fExhaustion = new JSONStorableFloat("Exhaustion", 0f, 0f, 10f, true, false);
			fPower.isStorable = fWork.isStorable = fExhaustion.isStorable = false;
			host.RegisterFloat(fPower); host.RegisterFloat(fWork); host.RegisterFloat(fExhaustion);
			for (int i = 0; i < work.tweak.Length; i++)
			{
				Num n = work.tweak[i];
				JSONStorableFloat f = AddFloat(n.name, n.val, n.min, n.max, delegate (float v) { n.Set(v); });
				Num nn = n;
				JSONStorableFloat ff = f;
				n.changed += delegate (float v) { PushNum(ff, nn); };
			}
			WatchControls(work.currentPowerTrigger);
			WatchControls(work.workDoneTrigger);
			WatchControls(work.exhaustionTrigger);
			WatchControls(work.glossTrigger);

			// ---- presets and actions
			chShakePreset = AddChooser("ShakeIt Preset", PresetFiles(ShakeDir).ToArray(), "", delegate (string v) { });
			chFlexPreset = AddChooser("FlexIt Preset", PresetFiles(FlexDir).ToArray(), "", delegate (string v) { });
			AddAction("ShakeIt Save Preset", delegate { SavePreset(false, null); });
			AddAction("ShakeIt Load Selected Preset", delegate { if (!string.IsNullOrEmpty(chShakePreset.val)) ApplyFromFile(ShakeDir + chShakePreset.val, false); });
			AddAction("ShakeIt Refresh Preset List", delegate { RefreshPresetLists(); });
			AddAction("ShakeIt Apply Latest Matching Preset", delegate { ApplyLatestMatchingPreset(false); });
			AddAction("ShakeIt UserDefaults", delegate { ApplyFromFile(ShakeDir + "UserDefaults.json", false); });
			AddAction("ShakeIt Save As UserDefaults", delegate { SavePreset(false, "UserDefaults"); });
			AddAction("ShakeIt FactoryDefaults", delegate { LoadShake(JSON.Parse(factoryShake) as JSONClass); });
			AddAction("FlexIt Save Preset", delegate { SavePreset(true, null); });
			AddAction("FlexIt Load Selected Preset", delegate { if (!string.IsNullOrEmpty(chFlexPreset.val)) ApplyFromFile(FlexDir + chFlexPreset.val, true); });
			AddAction("FlexIt Refresh Preset List", delegate { RefreshPresetLists(); });
			AddAction("FlexIt Apply Latest Matching Preset", delegate { ApplyLatestMatchingPreset(true); });
			AddAction("FlexIt UserDefaults", delegate { ApplyFromFile(FlexDir + "UserDefaults.json", true); });
			AddAction("FlexIt Save As UserDefaults", delegate { SavePreset(true, "UserDefaults"); });
			AddAction("FlexIt FactoryDefaults", delegate { LoadFlex(JSON.Parse(factoryFlex) as JSONClass); });
			AddAction("FlexIt Update Support Trackers", delegate { flex[0].UpdateSupportTrackers(); });
			AddAction("Apply Preset_Butt Defaults", delegate { Plugin.ForgetDefaultsCache(); ApplyConfiguredDefaults(); });
			AddAction("Refresh Morphs", delegate { RefreshMorphs(); });
			AddAction("Reset Morphs", delegate { ctx.Morphs.ResetAll(); });
			AddAction("WorkOut Reset", delegate { work.Reset(); fPower.valNoCallback = work.power.val; fWork.valNoCallback = work.workDone.val; fExhaustion.valNoCallback = work.exhaustion.val; });

			pushing = true;
			ctx.SelectedRegion = 0;
			pushing = false;
			PushShakeProxies();
			PushFlexProxies();
			PushWorkProxies();
		}

		private void WatchControls(FloatWatch w)
		{
			string n = w.Name.Substring(0, w.Name.Length - "Trigger".Length);
			Num[] nums = { w.threshold, w.inputFrom, w.inputTo };
			string[] labels = { " Trigger Threshold", " Trigger Input From", " Trigger Input To" };
			for (int i = 0; i < nums.Length; i++)
			{
				Num num = nums[i];
				AddFloat(n + labels[i], num.val, 0f, 10000f, delegate (float v) { num.Set(v); });
			}
			AddBool(n + " Trigger Absolute Value", w.absolute, delegate (bool v) { w.absolute = v; });
		}

		private void PushNum(JSONStorableFloat f, Num n)
		{
			pushing = true;
			try { f.min = n.min; f.max = n.max; f.valNoCallback = n.val; }
			finally { pushing = false; }
		}

		private Vector4 Get(V4Param p) { return p.val; }

		private V4Param PickShake(JHandler h, int kind)
		{
			switch (kind)
			{
				case 0: return tensedState ? h.strengthTenseJ : h.strengthJ;
				case 1: return tensedState ? h.frequencyTenseJ : h.frequencyJ;
				case 2: return tensedState ? h.dampingTenseJ : h.dampingJ;
				case 3: return h.minAccelJ;
				case 4: return h.maxAccelJ;
				default: return h.maxMorphJ;
			}
		}

		private void SetShakeComp(int kind, int comp, float v)
		{
			List<JHandler> sel = shake[selShake].Selected(selPart);
			for (int i = 0; i < sel.Count; i++)
			{
				V4Param p = PickShake(sel[i], kind);
				Vector4 vec = p.val;
				if (syncShake) vec = new Vector4(v, v, v, v); else vec[comp] = v;
				p.Set(vec);
			}
			if (syncShake) PushShakeProxies();
		}

		private V4Param PickFlex(FRegion r, int kind)
		{
			return kind == 0 ? r.amountJ : kind == 1 ? r.sRatioJ : r.dynamicStrengthJ;
		}

		private void SetFlexComp(int kind, int comp, float v)
		{
			V4Param p = PickFlex(flex[selFlex], kind);
			Vector4 vec = p.val;
			if (syncFlex) vec = new Vector4(v, v, v, v); else vec[comp] = v;
			p.Set(vec);
			if (syncFlex) PushFlexProxies();
		}

		public void PushShakeProxies()
		{
			if (host == null || shakeFloats[0, 0] == null) return;
			pushing = true;
			try
			{
				JRegion r = shake[selShake];
				bShakeRegionOn.valNoCallback = r.enabledJ.val;
				bCombined.valNoCallback = r.isCombined.val;
				bTension.valNoCallback = r.tensionEnabled.val;
				List<JHandler> sel = r.Selected(selPart);
				if (sel.Count > 0)
				{
					for (int k = 0; k < 6; k++)
					{
						Vector4 v = PickShake(sel[0], k).val;
						for (int c = 0; c < 4; c++) shakeFloats[k, c].valNoCallback = v[c];
					}
				}
			}
			finally
			{
				pushing = false;
			}
		}

		public void PushFlexProxies()
		{
			if (host == null || flexFloats[0, 0] == null) return;
			pushing = true;
			try
			{
				FRegion r = flex[selFlex];
				bFlexRegionOn.valNoCallback = r.enabledJ.val;
				for (int k = 0; k < 3; k++)
				{
					Vector4 v = PickFlex(r, k).val;
					for (int c = 0; c < 4; c++) flexFloats[k, c].valNoCallback = v[c];
				}
			}
			finally
			{
				pushing = false;
			}
		}

		public void PushWorkProxies()
		{
			if (host == null) return;
			pushing = true;
			try
			{
				for (int i = 0; i < work.tweak.Length; i++)
				{
					JSONStorableFloat f = host.GetFloatJSONParam(work.tweak[i].name);
					if (f != null) { f.min = work.tweak[i].min; f.max = work.tweak[i].max; f.valNoCallback = work.tweak[i].val; }
				}
				FloatWatch[] ws = { work.currentPowerTrigger, work.workDoneTrigger, work.exhaustionTrigger, work.glossTrigger };
				for (int w = 0; w < ws.Length; w++)
				{
					string n = ws[w].Name.Substring(0, ws[w].Name.Length - "Trigger".Length);
					JSONStorableFloat f = host.GetFloatJSONParam(n + " Trigger Threshold");
					if (f != null) f.valNoCallback = ws[w].threshold.val;
					f = host.GetFloatJSONParam(n + " Trigger Input From");
					if (f != null) f.valNoCallback = ws[w].inputFrom.val;
					f = host.GetFloatJSONParam(n + " Trigger Input To");
					if (f != null) f.valNoCallback = ws[w].inputTo.val;
					JSONStorableBool b = host.GetBoolJSONParam(n + " Trigger Absolute Value");
					if (b != null) b.valNoCallback = ws[w].absolute;
				}
				JSONStorableBool e = host.GetBoolJSONParam("WorkOut Enabled");
				if (e != null) e.valNoCallback = workOn.val;
				e = host.GetBoolJSONParam("ShakeIt Enabled");
				if (e != null) e.valNoCallback = shakeOn.val;
				e = host.GetBoolJSONParam("FlexIt Enabled");
				if (e != null) e.valNoCallback = flexOn.val;
			}
			finally
			{
				pushing = false;
			}
		}

		private void UpdateMonitor()
		{
			JRegion r = shake[selShake];
			List<JHandler> sel = r.Selected(selPart);
			if (sel.Count == 0) return;
			JHandler h = sel[0];
			sTension.valNoCallback = "Tension: " + h.monTension.ToString("0.00") + "  Strength " + h.monStrength.x.ToString("0.0") + "/" + h.monStrength.y.ToString("0.0") + "/" + h.monStrength.z.ToString("0.0") + "/" + h.monStrength.w.ToString("0.0");
		}
	}
}
