using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.AutoFlutterHipStandalone
{
	// Headless standalone port of 14mhz's AutoFlutterHip (.var, two plugins in one package) - it goes on female Person atoms by itself.
	//
	// 1. AutoFlutterHip: "morph the hips by how the body is bent". Per person, two groups of morph slots:
	//      Hips (Bending Waist): 4 slots, driven by the angle knee - hip - chest (unfolded .. bending)
	//      Legs (Spreading Legs): 4 slots, driven by the angle left knee - hip - right knee (closed .. spread)
	//    A slot = Select Morph, Enable Morph, Min Angle Offset, Max Angle Offset, Intensity Morph, Test Morph + Monitor & Test Morph,
	//    Set Default. Defaults are the .var's: hips = Doggy Style Shape, Hourglass Waist, Waist ErectorSpinae (TenStrip);
	//    legs = Thights Indent, Gracilis Stretch (kemenate). Each physics step the morph moves a tenth of the way to its target.
	//    Switching a slot off, or removing the plugin, puts the morphs back to 0. "Show Debug" draws the six points and five lines.
	// 2. AutoFlutterHipTechnic: "morph/animate the hips in precise time units" - a force sequencer on the hip. 8 tabs ("Technic 00-07"),
	//    each with Repetition, Speed x sec, Endless Technic, Smooth Transition, Sync Other Atoms and up to 10 slots; a slot is
	//    Repetition, Length (sec), Sync Offset, 1st/2nd Rotation and Movement (axis, strength, offset, lock) and 2nd Rot with Radial.
	//    The actions Stop If Playing, Play Forced [00..07], Play If Not Playing [00..07] play a tab; Sync Other Atoms makes other
	//    people play the same technic in time with this one.
	//
	// What it is without a window
	//    Each person gets a storable "ZeroT.AutoFlutterHip" with the .var's controls as parameters and actions (so triggers, UIAssist
	//    buttons and other plugins can drive them), and the defaults live in BepInEx\config\com.zerot.autoflutterhip.cfg.
	//    The .var's tabs were filled by hand in its UI and its Save / Load buttons; here a tab shows a preset file
	//    ("Technic - <name>.json" in Saves\\PluginData\\14mhz\\AutoFlutterHip\\Girl, \\Male or that folder itself - the same files the
	//    .var wrote), chosen with the Technic.TabNN settings or the "Tab NN Preset" parameter; empty tabs take the library in name order.
	//    Editing a technic means editing its JSON file; "Tab NN Save" writes the tab (with any live changes) back.
	// Where it differs on purpose: morphs are looked up and smoothed per person (the .var kept them in one static table, so two women
	// shared state), a tab with no slots does nothing (the .var spun forever), missing body parts wait instead of throwing every step.
	// If the .var's own plugins are on a person, that part is left to them.
	[BepInPlugin("com.zerot.autoflutterhip", "AutoFlutterHip Standalone", "1.0.0")]
	public sealed class AutoFlutterHipStandalone : BaseUnityPlugin
	{
		internal const string StoreId = "ZeroT.AutoFlutterHip";
		internal const string PresetBase = "Saves/PluginData/14mhz/AutoFlutterHip";
		internal const int Tabs = 8;

		private sealed class Rig
		{
			public Atom atom;
			public bool female;
			public MorphRig morph;                 // female only
			public TechnicPlayer player;
			public TechnicHead[] heads = new TechnicHead[Tabs];
			public JSONStorable host;
			public JSONStorableBool stDebug, stLog;
			public JSONStorableString stStatus;
			public readonly List<JSONStorableFloat> floats = new List<JSONStorableFloat>();
			public readonly List<JSONStorableBool> bools = new List<JSONStorableBool>();
			public readonly List<JSONStorableString> strings = new List<JSONStorableString>();
			public readonly List<JSONStorableStringChooser> choosers = new List<JSONStorableStringChooser>();
			public JSONStorableString[] stTabPreset = new JSONStorableString[Tabs];
			public JSONStorableFloat[] stRep = new JSONStorableFloat[Tabs], stSpeed = new JSONStorableFloat[Tabs];
			public JSONStorableBool[] stEndless = new JSONStorableBool[Tabs], stSmooth = new JSONStorableBool[Tabs], stSync = new JSONStorableBool[Tabs];
			public bool pushing;
			public bool morphOriginalOn, technicOriginalOn;
		}

		private ConfigEntry<bool> cfgActive, cfgMorph, cfgTechnic, cfgTechMales, cfgAutoFill, cfgDebug;
		private ConfigEntry<string> cfgFolder;
		private readonly ConfigEntry<string>[] cfgName = new ConfigEntry<string>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<bool>[] cfgAct = new ConfigEntry<bool>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<bool>[] cfgTest = new ConfigEntry<bool>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<float>[] cfgMin = new ConfigEntry<float>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<float>[] cfgMax = new ConfigEntry<float>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<float>[] cfgMul = new ConfigEntry<float>[MorphRig.HipSlots + MorphRig.LegSlots];
		private readonly ConfigEntry<string>[] cfgTab = new ConfigEntry<string>[Tabs];

		private readonly Dictionary<Atom, Rig> rigs = new Dictionary<Atom, Rig>();
		private float nextDiscover, nextOriginalCheck;
		private bool presetsDirty = true;
		private readonly List<KeyValuePair<string, TechnicHead>> library = new List<KeyValuePair<string, TechnicHead>>();   // preset name, data
		private bool wasActive = true;
		private readonly Dictionary<Atom, string> skipLogged = new Dictionary<Atom, string>();

		// ------------------------------------------------------------------ settings

		private static string SlotName(int i) { return (i < MorphRig.HipSlots ? "Hips Morph #" + i : "Legs Morph #" + (i - MorphRig.HipSlots)); }

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn everything off (morphs back to 0, technics stopped).");
			cfgMorph = Config.Bind<bool>("General", "HipAndLegMorphs", true, "AutoFlutterHip: morph the hips and legs by how the body is bent (female persons).");
			cfgTechnic = Config.Bind<bool>("General", "Technic", true, "AutoFlutterHipTechnic: the hip force sequencer.");
			cfgTechMales = Config.Bind<bool>("Technic", "IncludeMales", false, "Also give male persons the technic player (the .var allows it; the morphs are female only).");
			cfgAutoFill = Config.Bind<bool>("Technic", "AutoFillTabs", true, "Tabs whose setting is empty take the preset library in name order.");
			cfgFolder = Config.Bind<string>("Technic", "ExtraPresetFolder", "", "Another folder with 'Technic - *.json' presets (besides the .var's Girl / Male folders).");
			cfgDebug = Config.Bind<bool>("Debug", "ShowDebug", false, "Draw the six body points and five lines the morphs measure; verbose log.");
			for (int t = 0; t < Tabs; t++)
			{
				cfgTab[t] = Config.Bind<string>("Technic", "Tab" + t.ToString("00"), "", "Preset shown in technic tab " + t.ToString("00") + " (a 'Technic - <name>.json' name or a file path). Empty: next preset of the library.");
			}
			for (int i = 0; i < cfgName.Length; i++)
			{
				string sec = SlotName(i);
				string def = i < MorphRig.HipSlots ? MorphRig.DefaultHips[i] : MorphRig.DefaultLegs[i - MorphRig.HipSlots];
				cfgName[i] = Config.Bind<string>(sec, "Morph", def, "Select Morph: the display name of the morph, or None.");
				cfgAct[i] = Config.Bind<bool>(sec, "EnableMorph", true, "Enable Morph.");
				cfgMin[i] = Config.Bind<float>(sec, "MinAngleOffset", 0.5f, "Min Angle Offset (0 - 1).");
				cfgMax[i] = Config.Bind<float>(sec, "MaxAngleOffset", 0.5f, "Max Angle Offset (0 - 1).");
				cfgMul[i] = Config.Bind<float>(sec, "IntensityMorph", 0.7f, "Intensity Morph (0 - 1).");
				cfgTest[i] = Config.Bind<bool>(sec, "TestMorph", false, "Test Morph: drive the morph by the test value instead of the body angle (the value is the 'Monitor & Test Morph' parameter).");
			}
			Config.SettingChanged += OnSettingChanged;
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			foreach (Rig r in new List<Rig>(rigs.Values))
			{
				DropRig(r);
			}
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			ConfigEntryBase c = e.ChangedSetting;
			for (int t = 0; t < Tabs; t++)
			{
				if (c == cfgTab[t] || c == cfgAutoFill || c == cfgFolder)
				{
					presetsDirty = true;
					return;
				}
			}
			for (int i = 0; i < cfgName.Length; i++)
			{
				if (c == cfgName[i] || c == cfgAct[i] || c == cfgMin[i] || c == cfgMax[i] || c == cfgMul[i] || c == cfgTest[i])
				{
					foreach (Rig r in rigs.Values)
					{
						if (r.morph != null)
						{
							PullSlot(r, i, true);
						}
					}
					return;
				}
			}
			if (c == cfgDebug)
			{
				foreach (Rig r in rigs.Values)
				{
					SetDebug(r, cfgDebug.Value);
				}
			}
		}

		// ------------------------------------------------------------------ people

		private static bool IsFemale(Atom atom)
		{
			try
			{
				DAZCharacterSelector cs = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return cs != null && cs.gender == DAZCharacterSelector.Gender.Female;
			}
			catch
			{
				return false;
			}
		}

		private static bool HasOriginal(Atom atom, string typeName)
		{
			try
			{
				MonoBehaviour[] all = atom.GetComponentsInChildren<MonoBehaviour>(true);
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i] != null && all[i].GetType().Name == typeName && all[i].GetType().Namespace == null)
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

		private void Say(string s) { Logger.LogInfo(s); }

		private void Update()
		{
			if (!cfgActive.Value)
			{
				if (wasActive)
				{
					foreach (Rig r in new List<Rig>(rigs.Values))
					{
						DropRig(r);
					}
				}
				wasActive = false;
				return;
			}
			wasActive = true;
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			if (presetsDirty)
			{
				LoadLibrary();
				presetsDirty = false;
				foreach (Rig r in rigs.Values)
				{
					AssignTabs(r);
				}
			}
			if (Time.unscaledTime >= nextDiscover)
			{
				nextDiscover = Time.unscaledTime + 1f;
				bool checkOriginal = Time.unscaledTime >= nextOriginalCheck;
				if (checkOriginal)
				{
					nextOriginalCheck = Time.unscaledTime + 3f;
				}
				List<Atom> atoms = sc.GetAtoms();
				List<Atom> gone = new List<Atom>();
				foreach (Atom a in rigs.Keys)
				{
					if (a == null || !a || !atoms.Contains(a))
					{
						gone.Add(a);
					}
				}
				foreach (Atom a in gone)
				{
					DropRig(rigs[a]);
				}
				for (int i = 0; i < atoms.Count; i++)
				{
					Atom a = atoms[i];
					if (a == null || a.type != "Person")
					{
						continue;
					}
					bool female = IsFemale(a);
					bool wantMorph = cfgMorph.Value && female;
					bool wantTech = cfgTechnic.Value && (female || cfgTechMales.Value);
					Rig rig;
					bool has = rigs.TryGetValue(a, out rig);
					if (!has || checkOriginal)
					{
						bool origMorph = HasOriginal(a, "AutoFlutterHip"), origTech = HasOriginal(a, "AutoFlutterHipTechnic");
						if (has && (origMorph != rig.morphOriginalOn || origTech != rig.technicOriginalOn))
						{
							DropRig(rig);
							has = false;
						}
						if (!has)
						{
							if (origMorph)
							{
								wantMorph = false;
							}
							if (origTech)
							{
								wantTech = false;
							}
							string note = (origMorph ? "m" : "") + (origTech ? "t" : "");
							string was;
							if ((origMorph || origTech) && !(skipLogged.TryGetValue(a, out was) && was == note))
							{
								skipLogged[a] = note;
								Say("The .var's own " + (origMorph ? "AutoFlutterHip " : "") + (origTech ? "AutoFlutterHipTechnic " : "") + "is on " + a.uid + ": that part steps aside.");
							}
							if (wantMorph || wantTech)
							{
								rig = AddRig(a, female, wantMorph, wantTech);
								rig.morphOriginalOn = origMorph;
								rig.technicOriginalOn = origTech;
							}
							else
							{
								// remember that we looked, so the original check keeps working per person
								continue;
							}
						}
					}
				}
			}
		}

		private void FixedUpdate()
		{
			if (!cfgActive.Value)
			{
				return;
			}
			foreach (Rig r in rigs.Values)
			{
				if (r.morph != null)
				{
					try
					{
						r.morph.Process();
					}
					catch (Exception e)
					{
						Logger.LogWarning("Morphs on " + r.atom.uid + ": " + e.Message);
					}
				}
			}
		}

		// ------------------------------------------------------------------ a person

		private Rig AddRig(Atom atom, bool female, bool morph, bool technic)
		{
			Rig r = new Rig();
			r.atom = atom;
			r.female = female;
			rigs[atom] = r;
			GameObject go = new GameObject(StoreId);
			go.transform.SetParent(atom.transform, false);
			r.host = go.AddComponent<JSONStorable>();
			r.host.overrideId = StoreId;
			r.host.exclude = true;
			r.host.containingAtom = atom;
			Rig captured = r;
			r.stStatus = new JSONStorableString("Technic Status", "");
			r.host.RegisterString(r.stStatus);
			if (morph)
			{
				r.morph = new MorphRig(atom, Say);
				for (int i = 0; i < cfgName.Length; i++)
				{
					MorphSlot s = new MorphSlot();
					s.Tab = i < MorphRig.HipSlots ? "Hips" : "Legs";
					s.Index = i < MorphRig.HipSlots ? i : i - MorphRig.HipSlots;
					s.IsLeg = i >= MorphRig.HipSlots;
					s.Key = SlotName(i);
					r.morph.Slots.Add(s);
				}
				BuildMorphStorables(r);
				for (int i = 0; i < cfgName.Length; i++)
				{
					PullSlot(r, i, false);
				}
				r.stDebug = new JSONStorableBool("Show Debug", cfgDebug.Value, delegate (bool v) { if (!captured.pushing) { SetDebug(captured, v); } });
				r.host.RegisterBool(r.stDebug);
				SetDebug(r, cfgDebug.Value);
			}
			if (technic)
			{
				BuildTechnic(r);
			}
			if (!atom.RegisterAdditionalStorable(r.host))
			{
				Logger.LogWarning("VaM did not accept the storable on " + atom.uid + "; the effects still run.");
			}
			Logger.LogInfo("AutoFlutterHip on " + atom.uid + (morph ? " [morphs]" : "") + (technic ? " [technic]" : "") + ".");
			return r;
		}

		private void DropRig(Rig r)
		{
			if (r.player != null)
			{
				r.player.islooping = false;
			}
			if (r.morph != null)
			{
				r.morph.Close();
			}
			if (r.host != null)
			{
				UnityEngine.Object.Destroy(r.host.gameObject);
			}
			rigs.Remove(r.atom);
		}

		private void SetDebug(Rig r, bool v)
		{
			if (r.morph == null)
			{
				return;
			}
			r.morph.Debug = v;
			if (!v)
			{
				r.morph.ClosePoints();
			}
			TechnicPlayer.Debug = v;
			r.pushing = true;
			try
			{
				if (r.stDebug != null)
				{
					r.stDebug.val = v;
				}
			}
			finally
			{
				r.pushing = false;
			}
		}

		// ------------------------------------------------------------------ morph controls

		private static string P(MorphSlot s, string what) { return s.Label + " " + what; }

		private void BuildMorphStorables(Rig r)
		{
			List<string> names = r.morph.MorphNames();
			names.Insert(0, "None");
			for (int i = 0; i < r.morph.Slots.Count; i++)
			{
				MorphSlot s = r.morph.Slots[i];
				int idx = i;
				Rig rig = r;
				JSONStorableStringChooser sel = new JSONStorableStringChooser(P(s, "Select Morph"), new List<string>(names), s.Name, P(s, "Select Morph"), delegate (string v)
				{
					if (rig.pushing) { return; }
					rig.morph.RestoreMorphs();   // as the .var: changing a slot's morph puts every used morph back to 0 first
					s.Name = v;
				});
				JSONStorableBool act = new JSONStorableBool(P(s, "Enable Morph"), s.Act, delegate (bool v)
				{
					if (rig.pushing) { return; }
					s.Act = v;
					if (!v)
					{
						rig.morph.ApplyMorph(s.Name, 0f);
					}
				});
				JSONStorableFloat min = new JSONStorableFloat(P(s, "Min Angle Offset"), s.Min, delegate (float v) { if (!rig.pushing) { s.Min = v; } }, 0f, 1f, false);
				JSONStorableFloat max = new JSONStorableFloat(P(s, "Max Angle Offset"), s.Max, delegate (float v) { if (!rig.pushing) { s.Max = v; } }, 0f, 1f, false);
				JSONStorableFloat mul = new JSONStorableFloat(P(s, "Intensity Morph"), s.Mul, delegate (float v) { if (!rig.pushing) { s.Mul = v; } }, 0f, 1f, false);
				JSONStorableBool dbg = new JSONStorableBool(P(s, "Test Morph"), s.Dbg, delegate (bool v) { if (!rig.pushing) { s.Dbg = v; } });
				JSONStorableFloat mon = new JSONStorableFloat(P(s, "Monitor & Test Morph"), s.Val, delegate (float v) { if (!rig.pushing && s.Dbg) { s.Val = v; } }, 0f, 1f, false);
				s.Monitor = mon;
				r.host.RegisterStringChooser(sel); r.host.RegisterBool(act); r.host.RegisterFloat(min); r.host.RegisterFloat(max);
				r.host.RegisterFloat(mul); r.host.RegisterBool(dbg); r.host.RegisterFloat(mon);
				r.host.RegisterAction(new JSONStorableAction(P(s, "Set Default"), delegate { PullSlot(rig, idx, true); }));
				r.choosers.Add(sel); r.bools.Add(act); r.bools.Add(dbg); r.floats.Add(min); r.floats.Add(max); r.floats.Add(mul); r.floats.Add(mon);
			}
		}

		// "Set Default" / config -> the slot of one person (morpher2tui of the default Morpher)
		private void PullSlot(Rig r, int i, bool restoreFirst)
		{
			MorphSlot s = r.morph.Slots[i];
			if (restoreFirst && s.Name != cfgName[i].Value)
			{
				r.morph.RestoreMorphs();
			}
			s.Name = cfgName[i].Value;
			s.Act = cfgAct[i].Value;
			s.Min = Mathf.Clamp01(cfgMin[i].Value);
			s.Max = Mathf.Clamp01(cfgMax[i].Value);
			s.Mul = Mathf.Clamp01(cfgMul[i].Value);
			s.Dbg = cfgTest[i].Value;
			r.pushing = true;
			try
			{
				string p = s.Label;
				JSONStorableStringChooser sel = r.host.GetStringChooserJSONParam(p + " Select Morph");
				if (sel != null) { sel.val = s.Name; }
				JSONStorableBool act = r.host.GetBoolJSONParam(p + " Enable Morph");
				if (act != null) { act.val = s.Act; }
				JSONStorableBool dbg = r.host.GetBoolJSONParam(p + " Test Morph");
				if (dbg != null) { dbg.val = s.Dbg; }
				JSONStorableFloat f;
				if ((f = r.host.GetFloatJSONParam(p + " Min Angle Offset")) != null) { f.val = s.Min; }
				if ((f = r.host.GetFloatJSONParam(p + " Max Angle Offset")) != null) { f.val = s.Max; }
				if ((f = r.host.GetFloatJSONParam(p + " Intensity Morph")) != null) { f.val = s.Mul; }
			}
			finally
			{
				r.pushing = false;
			}
			if (!s.Act)
			{
				r.morph.ApplyMorph(s.Name, 0f);
			}
		}

		// ------------------------------------------------------------------ technic

		private static string TabId(int t) { return t.ToString("00"); }

		private void BuildTechnic(Rig r)
		{
			Rigidbody hip = null;
			Rigidbody[] rb = r.atom.rigidbodies;
			for (int i = 0; rb != null && i < rb.Length; i++)
			{
				if (rb[i] != null && rb[i].name == "hip")
				{
					hip = rb[i];
					break;
				}
			}
			Rig rig = r;
			if (hip != null)
			{
				r.player = new TechnicPlayer(r.atom, hip, Say);
				r.player.StartCo = delegate (IEnumerator e) { StartCoroutine(e); };
				r.player.Sync = delegate (string type, string head, int slot, long time, int span) { return SyncOthers(rig, type, head, slot, time, span); };
			}
			else
			{
				Logger.LogWarning("No hip body on " + r.atom.uid + ": the technic player cannot start there.");
			}
			r.stLog = new JSONStorableBool("Debug Log", false, delegate (bool v) { TechnicPlayer.Debug = v; });
			r.host.RegisterBool(r.stLog);
			r.host.RegisterAction(new JSONStorableAction("Stop If Playing", delegate { Action(rig, 0, "stop"); }));
			r.host.RegisterAction(new JSONStorableAction("Reload Presets", delegate { presetsDirty = true; }));
			for (int t = 0; t < Tabs; t++)
			{
				int tab = t;
				string id = TabId(t);
				r.host.RegisterAction(new JSONStorableAction("Play Forced [" + id + "]", delegate { Action(rig, tab, "forced"); }));
				r.host.RegisterAction(new JSONStorableAction("Play If Not Playing [" + id + "]", delegate { Action(rig, tab, "ifnot"); }));
				r.host.RegisterAction(new JSONStorableAction("Tab " + id + " Save", delegate { SaveTab(rig, tab); }));
				r.stTabPreset[t] = new JSONStorableString("Tab " + id + " Preset", "", delegate (string v) { if (!rig.pushing) { cfgTab[tab].Value = v; } });
				r.stRep[t] = new JSONStorableFloat("Tab " + id + " Repetition", 1f, delegate (float v) { if (!rig.pushing && rig.heads[tab] != null) { rig.heads[tab].Repetition = v; } }, 1f, 99f, false);
				r.stSpeed[t] = new JSONStorableFloat("Tab " + id + " Speed x sec", 1f, delegate (float v) { if (!rig.pushing && rig.heads[tab] != null) { rig.heads[tab].Speed = v; } }, 0.5f, 2f, false);
				r.stEndless[t] = new JSONStorableBool("Tab " + id + " Endless Technic", false, delegate (bool v) { if (!rig.pushing && rig.heads[tab] != null) { rig.heads[tab].Endless = v; } });
				r.stSmooth[t] = new JSONStorableBool("Tab " + id + " Smooth Transition", false, delegate (bool v) { if (!rig.pushing && rig.heads[tab] != null) { rig.heads[tab].Smooth = v; } });
				r.stSync[t] = new JSONStorableBool("Tab " + id + " Sync Other Atoms", false, delegate (bool v) { if (!rig.pushing && rig.heads[tab] != null) { rig.heads[tab].Sync = v; } });
				r.host.RegisterString(r.stTabPreset[t]); r.host.RegisterFloat(r.stRep[t]); r.host.RegisterFloat(r.stSpeed[t]);
				r.host.RegisterBool(r.stEndless[t]); r.host.RegisterBool(r.stSmooth[t]); r.host.RegisterBool(r.stSync[t]);
			}
			AssignTabs(r);
		}

		private static string FolderFor(bool female) { return PresetBase + "/" + (female ? "Girl" : "Male"); }

		private static string NameOf(string path)
		{
			int i = Math.Max(path.LastIndexOf('/'), path.LastIndexOf((char)92));
			string f = i >= 0 ? path.Substring(i + 1) : path;
			if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				f = f.Substring(0, f.Length - 5);
			}
			if (f.StartsWith("Technic - ", StringComparison.OrdinalIgnoreCase))
			{
				f = f.Substring(10);
			}
			return f;
		}

		private static bool IsCopyName(string n)
		{
			int o = n.LastIndexOf(" (", StringComparison.Ordinal);
			return o > 0 && n.EndsWith(")") && n.Substring(o + 2, n.Length - o - 3).Length > 0 && int.TryParse(n.Substring(o + 2, n.Length - o - 3), out o);
		}

		private TechnicHead ReadPreset(string path)
		{
			try
			{
				string text = FileManagerSecure.ReadAllText(path);
				if (string.IsNullOrEmpty(text))
				{
					return null;
				}
				JSONClass jc = JSON.Parse(text) as JSONClass;
				return jc == null ? null : TechnicHead.FromJson(jc);
			}
			catch (Exception e)
			{
				Logger.LogWarning("Preset " + path + ": " + e.Message);
				return null;
			}
		}

		private readonly Dictionary<string, string> libraryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private void LoadLibrary()
		{
			library.Clear();
			libraryPaths.Clear();
			List<string> folders = new List<string> { FolderFor(true), FolderFor(false), PresetBase };
			if (!string.IsNullOrEmpty(cfgFolder.Value))
			{
				folders.Add(cfgFolder.Value.Replace((char)92, '/'));
			}
			foreach (string folder in folders)
			{
				try
				{
					if (!FileManagerSecure.DirectoryExists(folder))
					{
						continue;
					}
					string[] files = FileManagerSecure.GetFiles(folder, "*.json");
					Array.Sort(files, StringComparer.OrdinalIgnoreCase);
					foreach (string f in files)
					{
						string n = NameOf(f);
						string fn = f.Substring(Math.Max(f.LastIndexOf('/'), f.LastIndexOf((char)92)) + 1);
						if (!fn.StartsWith("Technic - ", StringComparison.OrdinalIgnoreCase) || libraryPaths.ContainsKey(folder + "|" + n))
						{
							continue;
						}
						TechnicHead h = ReadPreset(f);
						if (h != null)
						{
							library.Add(new KeyValuePair<string, TechnicHead>(n, h));
							libraryPaths[folder + "|" + n] = f;
							if (!libraryPaths.ContainsKey(n))
							{
								libraryPaths[n] = f;
							}
						}
					}
				}
				catch (Exception e)
				{
					Logger.LogWarning("Preset folder " + folder + ": " + e.Message);
				}
			}
			Logger.LogInfo("AutoFlutterHip technic library: " + library.Count + " preset(s).");
		}

		private TechnicHead Resolve(string wanted, bool female)
		{
			if (string.IsNullOrEmpty(wanted))
			{
				return null;
			}
			string w = NameOf(wanted);
			// this person's own folder first, then everything else
			string own = FolderFor(female) + "|" + w, path;
			if (libraryPaths.TryGetValue(own, out path) || libraryPaths.TryGetValue(w, out path))
			{
				return ReadPreset(path);
			}
			foreach (KeyValuePair<string, TechnicHead> kv in library)
			{
				if (string.Equals(kv.Value.Name, wanted, StringComparison.OrdinalIgnoreCase))
				{
					return Clone(kv.Value);
				}
			}
			if (wanted.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				return ReadPreset(wanted.Replace((char)92, '/'));
			}
			return null;
		}

		private static TechnicHead Clone(TechnicHead h)
		{
			return TechnicHead.FromJson(h.ToJson());
		}

		private void AssignTabs(Rig r)
		{
			if (r.player == null && r.stTabPreset[0] == null)
			{
				return;
			}
			// tabs whose setting is empty take the library in order (copies like "Name (1)" are skipped)
			List<TechnicHead> pool = new List<TechnicHead>();
			if (cfgAutoFill.Value)
			{
				foreach (KeyValuePair<string, TechnicHead> kv in library)
				{
					if (!IsCopyName(kv.Key))
					{
						pool.Add(kv.Value);
					}
				}
			}
			int next = 0;
			for (int t = 0; t < Tabs; t++)
			{
				string wanted = cfgTab[t].Value;
				TechnicHead h = null;
				if (!string.IsNullOrEmpty(wanted))
				{
					h = Resolve(wanted, r.female);
					if (h == null)
					{
						Logger.LogWarning("Tab " + TabId(t) + ": preset '" + wanted + "' was not found.");
					}
				}
				else
				{
					while (h == null && next < pool.Count)
					{
						h = Clone(pool[next++]);
					}
				}
				if (r.player != null && r.player.islooping && r.heads[t] != null && h != null && r.heads[t].Name == h.Name)
				{
					continue;   // already playing this one: leave it be
				}
				r.heads[t] = h;
				PushTab(r, t);
			}
		}

		private void PushTab(Rig r, int t)
		{
			if (r.stTabPreset[t] == null)
			{
				return;
			}
			TechnicHead h = r.heads[t];
			r.pushing = true;
			try
			{
				r.stTabPreset[t].val = h == null ? "" : h.Name;
				r.stRep[t].val = h == null ? 1f : h.Repetition;
				r.stSpeed[t].val = h == null ? 1f : h.Speed;
				r.stEndless[t].val = h != null && h.Endless;
				r.stSmooth[t].val = h != null && h.Smooth;
				r.stSync[t].val = h != null && h.Sync;
			}
			finally
			{
				r.pushing = false;
			}
		}

		private void SaveTab(Rig r, int t)
		{
			TechnicHead h = r.heads[t];
			if (h == null)
			{
				return;
			}
			try
			{
				string dir = FolderFor(r.female);
				if (!FileManagerSecure.DirectoryExists(dir))
				{
					FileManagerSecure.CreateDirectory(dir);
				}
				string path = dir + "/Technic - " + h.Name.Trim() + ".json";
				SuperController.singleton.SaveJSON(h.ToJson(), path);
				Logger.LogInfo("Tab " + TabId(t) + " saved to " + path);
			}
			catch (Exception e)
			{
				Logger.LogWarning("Saving tab " + TabId(t) + ": " + e.Message);
			}
		}

		// ---- the actions (AutoFlutterHipTechnic.action / callback)

		private void Action(Rig r, int tab, string what)
		{
			if (r.player == null)
			{
				return;
			}
			TechnicHead head = r.heads[tab];
			if (what == "stop")
			{
				DoStop(r, head);
				return;
			}
			if (head == null || head.Slots.Count == 0)
			{
				SetStatus(r, "Tab " + TabId(tab) + " is empty");
				return;
			}
			if (what == "forced")
			{
				DoStop(r, head);
				DoRun(r, head);
			}
			else if (what == "ifnot")
			{
				if (r.player.islooping)
				{
					return;
				}
				DoRun(r, head);
			}
		}

		private void SetStatus(Rig r, string s)
		{
			r.stStatus.val = s;
			if (TechnicPlayer.Debug)
			{
				Logger.LogInfo(r.atom.uid + ": " + s);
			}
		}

		private void DoStop(Rig r, TechnicHead head)
		{
			StartCoroutine(r.player.StopWait());
			if (head != null && head.Sync)
			{
				StartCoroutine(SyncOthers(r, "stop", null, -1, -1, 0));
			}
			SetStatus(r, "Stopped ...");
		}

		private void DoRun(Rig r, TechnicHead head)
		{
			SetStatus(r, "Now Playing ... " + head.Name);
			StartCoroutine(RunAndReport(r, head, head.Slots, -1, TechnicPlayer.Ticks(), 0));
		}

		private IEnumerator RunAndReport(Rig r, TechnicHead head, List<TechnicSlot> slots, int slot, long poll, int span)
		{
			IEnumerator it = r.player.Run(head, slots, slot, poll, span);
			while (true)
			{
				bool more;
				try
				{
					more = it.MoveNext();
				}
				catch (Exception e)
				{
					Logger.LogWarning("Technic on " + r.atom.uid + " stopped by an error: " + e.Message);
					r.player.islooping = false;
					break;
				}
				if (!more)
				{
					break;
				}
				yield return it.Current;
			}
			if (!r.player.islooping)
			{
				SetStatus(r, "Stopped ...");
			}
		}

		// Sync Other Atoms: every other person that has the technic player gets the message
		private IEnumerator SyncOthers(Rig from, string type, string headName, int slot, long time, int span)
		{
			foreach (Rig o in new List<Rig>(rigs.Values))
			{
				if (o == from || o.player == null || o.atom == null || o.atom.category != "People")
				{
					continue;
				}
				Message(o, type, headName, slot, time, span + 1);
			}
			yield return new WaitForFixedUpdate();
		}

		private void Message(Rig o, string type, string headName, int slot, long time, int span)
		{
			if (type == "sync")
			{
				TechnicHead found = null;
				if (headName != null)
				{
					for (int i = 0; i < Tabs; i++)
					{
						if (o.heads[i] != null && o.heads[i].Name == headName)
						{
							found = o.heads[i];
							break;
						}
					}
				}
				if (found != null && found.Slots.Count > 0)
				{
					if (!o.player.islooping)
					{
						StartCoroutine(RunAndReport(o, found, found.Slots, slot, time, span));
					}
					else
					{
						o.player.poll_time = time;
					}
				}
			}
			else if (type == "stop")
			{
				StartCoroutine(o.player.StopWait());
			}
		}
	}
}
