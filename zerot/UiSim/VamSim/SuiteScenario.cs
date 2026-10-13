using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagementSecure;
using UnityEngine;

// The OrgasmSuite host against the REAL scripts it carries: `vamdev.py suitetargets` compiles the staged (patched) SexyFluids, BodyLanguage,
// BodyLanguage Lite + ArousalHUD, StimulationHUD, FluidBridge, AutoTremble and ZajeShuffle against VaM's own DLLs into in/suite/*.dll; here the
// plugin manager shim turns a slot's URL into one of those scripts, so the host's attach / detach / switch logic and the scripts' own
// Init + frames run together.
public static class SuiteScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
	private const string Root = "Custom/Scripts/ZeroT/OrgasmSuite/";

	private static readonly Dictionary<string, Assembly> asms = new Dictionary<string, Assembly>();
	private static readonly List<string> loadErrors = new List<string>();
	private static readonly List<string> simNotes = new List<string>();

	// SexyFluids (screen-space fluid rendering, compute/shader bundle) and BodyLanguage (1.6 MB: PoseMe, FillMeUp, voices...) need far more of Unity than the
	// simulator has: their attach / detach is tested, their own Init is attempted, and what the simulator cannot do is listed, not hidden
	private static bool SimLimited(string set) { return set == "ZeroT.OrgasmSuite.SexyFluids" || set == "ZeroT.OrgasmSuite.BodyLanguage"; }

	private static string SuiteDir { get { return Path.GetFullPath(Path.Combine(Harness.CoresDir, "..", "suite")); } }

	private static Assembly Asm(Harness h, string set)
	{
		Assembly a;
		if (asms.TryGetValue(set, out a)) return a;
		string src = Path.Combine(SuiteDir, set + ".dll");
		if (!File.Exists(src)) throw new FileNotFoundException("run: python zerot/tools/vamdev.py suitetargets  (missing " + src + ")");
		a = Assembly.LoadFrom(Program.Retarget(src, h.Work));
		asms[set] = a;
		return a;
	}

	private static void Map(string url, out string set, out string type)
	{
		string rest = url.Replace('\\', '/');
		int i = rest.IndexOf("OrgasmSuite/");
		rest = i >= 0 ? rest.Substring(i + "OrgasmSuite/".Length) : rest;
		set = null; type = null;
		string file = rest.Substring(rest.LastIndexOf('/') + 1);
		if (!file.EndsWith(".dll")) return;
		set = file.Substring(0, file.Length - 4);
		switch (set)
		{
			case "ZeroT.OrgasmSuite.BodyLanguageLite": type = "BodyLanguage_Lite"; break;
			case "ZeroT.OrgasmSuite.ArousalHUD": type = "ArousalHUD"; break;
			case "ZeroT.OrgasmSuite.BodyLanguage": type = "ReadMyLips"; break;
			case "ZeroT.OrgasmSuite.StimulationHUD": type = "StimulationHUD"; break;
			case "ZeroT.OrgasmSuite.SexyFluids": type = "SexyFluids"; break;
			case "ZeroT.OrgasmSuite.FluidBridge": type = "FluidBridge"; break;
			case "ZeroT.OrgasmSuite.AutoTremble": type = "AutoTremble"; break;
			case "ZeroT.OrgasmSuite.ZajeShuffle": type = "ZajeShuffle"; break;
			default: set = null; break;
		}
	}

	private static Type TypeOf(Assembly a, string name)
	{
		Type[] ts;
		try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types.Where(t => t != null).ToArray(); }
		return ts.FirstOrDefault(t => typeof(MVRScript).IsAssignableFrom(t) && !t.IsAbstract && t.Name == name);
	}

	private static MVRPluginManager AddManager(Atom a)
	{
		var mg = new GameObject("PluginManager");
		mg.transform.SetParent(a.transform, false);
		var m = mg.AddComponent<MVRPluginManager>();
		m.storeId = "PluginManager"; m.containingAtom = a;
		a.storables["PluginManager"] = m;
		return m;
	}

	// SUITE_DEBUG=1: step in half seconds and call every FluidBridge.Update directly so an exception arrives with its full stack
	private static void St(Harness h, float seconds)
	{
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") == null) { h.Step(seconds); return; }
		for (float t = 0; t < seconds; t += 0.5f)
		{
			h.Step(0.5f);
			foreach (var a in h.Sc.atoms.ToArray())
			{
				var fb = a == null ? null : Script(a, "FluidBridge");
				if (fb == null) continue;
				try { fb.GetType().GetMethod("Update", All).Invoke(fb, null); }
				catch (Exception e) { Console.WriteLine("  DEBUG " + a.uid + " t=" + h.Sc.atoms.Count + " " + (e.InnerException ?? e)); return; }
			}
		}
	}

	private static int Triggers(MVRScript fb) { return (int)fb.GetType().GetField("triggerCount", All).GetValue(fb); }

	// a touch raises arousal gradually (FluidBridge samples it every 0.25 s); returns when FluidBridge has seen the orgasm
	private static bool Orgasm(Harness h, Atom a)
	{
		var lite = Script(a, "BodyLanguage_Lite"); var fb = Script(a, "FluidBridge");
		var ar = lite.GetFloatJSONParam("Arousal");
		int before = Triggers(fb);
		ar.val = 0.5f; St(h, 0.6f);
		ar.val = 0.97f;
		for (float t = 0; t < 6f && Triggers(fb) == before; t += 0.1f) St(h, 0.1f);
		St(h, 0.4f);
		return Triggers(fb) == before + 1;
	}

	private static Atom Person(Harness h, string uid, bool male)
	{
		var a = h.Person(uid, male);
		AddManager(a);
		return a;
	}

	private static Atom Core(Harness h)
	{
		var a = new GameObject("CoreControl").AddComponent<Atom>();
		a.type = "CoreControl"; a.uid = a.name = "CoreControl"; a.containingAtom = a;
		h.Sc.AddAtom(a);
		AddManager(a);
		return a;
	}

	private static void InstallLoader(Harness h)
	{
		loadErrors.Clear(); simNotes.Clear();
		MVRPluginManager.SimLoader = (mgr, plugin, url) =>
		{
			try
			{
				string set, type;
				Map(url, out set, out type);
				if (set == null) { if (url.Contains("OrgasmSuite/")) loadErrors.Add("unknown script url " + url); return null; }   // somebody else's slot: nothing to run here
				var t = TypeOf(Asm(h, set), type);
				if (t == null) { loadErrors.Add("no MVRScript class " + type + " in " + set); return null; }
				var script = SceneHost.Spawn(t, mgr.containingAtom, mgr.gameObject, plugin.uid, url);
				try { script.Init(); }
				catch (Exception ex)
				{
					string msg = type + " on " + mgr.containingAtom.uid + " Init: " + SceneHost.Where(ex);
					if (SimLimited(set)) { simNotes.Add(msg); script.enabled = false; }   // frames of a half-initialised script would only repeat the same gap
					else loadErrors.Add(msg);
				}
				return script.gameObject;
			}
			catch (Exception ex) { loadErrors.Add("loader: " + SceneHost.Where(ex)); return null; }
		};
	}

	private static List<string> SlotUrls(Atom a)
	{
		var m = (MVRPluginManager)a.GetStorableByID("PluginManager");
		return m.plugins.Select(p => p.pluginURLJSON.val).ToList();
	}

	private static bool Has(Atom a, string urlTail) { return SlotUrls(a).Any(u => u.EndsWith(urlTail, StringComparison.OrdinalIgnoreCase)); }

	private static MVRScript Script(Atom a, string typeName)
	{
		return a.gameObject.GetComponentsInChildren<MVRScript>(true).FirstOrDefault(s => s.GetType().Name == typeName);
	}

	private static object Cfg(Harness h, string uid)
	{
		return h.Plugin.GetType().GetMethod("CfgFor").Invoke(h.Plugin, new object[] { uid });
	}

	private static object CfgVal(Harness h, string uid, string field) { var c = Cfg(h, uid); return c.GetType().GetField(field).GetValue(c); }

	private static void SetCfg(Harness h, string uid, string field, object value)
	{
		var c = Cfg(h, uid);
		c.GetType().GetField(field).SetValue(c, value);
		// what the window's buttons do after a change: save + rescan now
		h.Plugin.GetType().GetMethod("SavePeople", All).Invoke(h.Plugin, null);
		h.Plugin.GetType().GetField("_nextScan", All).SetValue(h.Plugin, 0f);
		h.Plugin.GetType().GetField("_pushed", All).GetValue(h.Plugin).GetType().GetMethod("Clear").Invoke(h.Plugin.GetType().GetField("_pushed", All).GetValue(h.Plugin), null);
	}

	private const string TremblePreset = "{ \"PresetVersion\" : \"1\", \"TargetAtom\" : \"Woman\", \"BodyParts\" : { \"BodyPart1\" : \"hipControl\", \"BodyPart2\" : \"lThighControl\", \"BodyPart3\" : \"lKneeControl\", \"BodyPart4\" : \"lThighControl\", \"BodyPart5\" : \"rKneeControl\", \"BodyPart6\" : \"None\" }, \"Magnitude\" : \"0.03565005\", \"MagnitudeRandomPercent\" : \"30\", \"MagnitudeRandomTime\" : \"0.2\", \"Frequency\" : \"4.784571\", \"FrequencyRandomPercent\" : \"30\", \"FrequencyRandomTime\" : \"0.2\", \"DirectionSpace\" : \"Body Part\", \"DirectionX\" : \"0\", \"DirectionY\" : \"0\", \"DirectionZ\" : \"1\", \"MirrorMode\" : \"true\", \"Mode\" : \"Constant\", \"PhaseIn\" : \"1\", \"HoldAtMax\" : \"2\", \"PhaseOut\" : \"1\", \"ForcePositionOn\" : \"true\", \"ComplianceOverride\" : \"true\", \"ComplySpeed\" : \"100\", \"ComplyDamper\" : \"0\" }";

	private static float Wiggle(Atom a, string controller, Harness h, float seconds)
	{
		var fc = a.freeControllers.First(c => c.name == controller);
		float lo = float.MaxValue, hi = float.MinValue;
		for (float t = 0; t < seconds; t += 0.02f)
		{
			h.Step(0.02f);
			float z = fc.control.position.x + fc.control.position.y + fc.control.position.z;
			lo = Math.Min(lo, z); hi = Math.Max(hi, z);
		}
		return hi - lo;
	}

	[Scenario("orgasmsuite", "ZeroT.OrgasmSuite")]
	public static void OrgasmSuite(Harness h)
	{
		string game = Path.Combine(Path.GetTempPath(), "vamsim_suite_game_" + Environment.ProcessId);
		if (Directory.Exists(game)) Directory.Delete(game, true);
		Directory.CreateDirectory(Path.Combine(game, "Saves", "PluginPresets", "AutoTremble"));
		BepInEx.Paths.GameRootPath = game;
		// a leftover from the earlier source-based build must disappear
		Directory.CreateDirectory(Path.Combine(game, "Custom", "Scripts", "ZeroT", "OrgasmSuite", "old"));
		File.WriteAllText(Path.Combine(game, "Custom", "Scripts", "ZeroT", "OrgasmSuite", "old", "Leftover.cs"), "// old");
		// the user's own preset must survive: it is only written when missing
		File.WriteAllText(Path.Combine(game, "Saves", "PluginPresets", "AutoTremble", "AutoTremblePreset.json"), TremblePreset);

		MVR.FileManagementSecure.FileManagerSecure.SimReset();
		const string presetPath = "Saves/PluginPresets/AutoTremble/AutoTremblePreset.json";
		FileManagerSecure.SimAddFile(presetPath);
		SuperController.SimSaved[presetPath] = TremblePreset;
		var uia = new HelperScenarios.FakeUiaGrid("Zaje", 30);
		UIAStorables.buttonGridsList = new List<HelperScenarios.FakeUiaGrid> { uia };
		InstallLoader(h);

		var core = Core(h);
		var woman = Person(h, "Woman", false);
		var man = Person(h, "Man", true);
		// the real defaults (15 s settle, 6 s quiet) would only slow the test down; the gating itself is exercised below
		BepInEx.Configuration.ConfigFile.Overrides["Suite.AutoAddOnLoad"] = "true";   // the default is off; the rest of the run is about the suite doing its work
		BepInEx.Configuration.ConfigFile.Overrides["Suite.SettleSeconds"] = "1";
		BepInEx.Configuration.ConfigFile.Overrides["Suite.QuietSeconds"] = "1";
		h.Start();
		h.Check(h.Plugin != null, "the plugin starts");

		// ---- 1. the embedded script pack lands on disk
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 1 errors=" + Sim.Errors.Count);
		string sd = Path.Combine(game, "Custom", "Scripts", "ZeroT", "OrgasmSuite");
		foreach (var f in new[] { "FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll", "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll", "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll", "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll",
			"BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll", "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll", "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll", "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll", "SexyFluids/data/Foost.SexyFluids.fluid_shaders.assetbundle" })
			h.Check(File.Exists(Path.Combine(sd, f.Replace('/', Path.DirectorySeparatorChar))), "extracted " + f);
		int nfiles = Directory.GetFiles(sd, "*", SearchOption.AllDirectories).Length;
		h.Check(nfiles == 9, "the suite folder holds exactly the 8 assemblies + the shader bundle (" + nfiles + " files)");
		h.Check(File.ReadAllText(Path.Combine(game, "Saves", "PluginPresets", "AutoTremble", "AutoTremblePreset.json")) == TremblePreset, "the user's AutoTremble preset was not overwritten");
		h.Check(Directory.GetFiles(Path.Combine(game, "Saves", "PluginData", "Foost", "SexyFluids", "JizzSim"), "*.json").Length >= 5, "SexyFluids' shipped presets were added where missing");
		h.Check(File.Exists(Path.Combine(sd, "old", "Leftover.cs")) == false, "files left by the earlier source-based build are removed");
		var fbT0 = TypeOf(Asm(h, "ZeroT.OrgasmSuite.FluidBridge"), "FluidBridge");
		h.Check(fbT0.GetMethod("OrgasmHooks", All) != null, "FluidBridge is the patched v28 (orgasm hooks)");
		h.Check(TypeOf(Asm(h, "ZeroT.OrgasmSuite.AutoTremble"), "AutoTremble").GetField("targetSelf", All) != null, "AutoTremble is the self-targeting build");

		// ---- 2. everything attaches, one change per scan
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 2 errors=" + Sim.Errors.Count);
		St(h, 16f);
		string[] womanWants = { "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll", "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll", "FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll", "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll", "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll" };
		foreach (var u in womanWants)
		{
			h.Check(Has(woman, u), "Woman has " + u);
			// every HUD pins itself to the same desktop spot: only the first Person starts with one
			h.Check(u.Contains("ArousalHUD") ? !Has(man, u) : Has(man, u), u.Contains("ArousalHUD") ? "Man starts without a HUD (Woman has it)" : "Man has " + u);
		}
		h.Check(!Has(core, "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "ZajeShuffle is off by default: nothing on CoreControl");
		SetCfg(h, "Woman", "Shuffle", true);
		St(h, 8f);
		h.Check(Has(core, "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "CoreControl has ZajeShuffle (session wide) once a Person turns it on");
		h.Check(!Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll") && !Has(woman, "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll"), "default engine is Lite: no BodyLanguage, no Stimulation HUD");
		foreach (var e in loadErrors) Console.WriteLine("  load error: " + e);
		h.Check(loadErrors.Count == 0, "no script failed to load or Init (" + loadErrors.Count + " errors)");
		SetCfg(h, "Man", "Hud", true);
		St(h, 4f);
		h.Check(Has(man, "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll"), "...and gets its own when switched on");
		foreach (var set in new[] { "BodyLanguage_Lite", "SexyFluids", "FluidBridge", "AutoTremble", "ArousalHUD" })
			h.Check(Script(woman, set) != null, "a running " + set + " script is on Woman");

		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null)
		{
			var lw = Script(woman, "BodyLanguage_Lite");
			for (int k = 0; k < 8; k++) { St(h, 1f); Console.WriteLine("  DEBUG t+" + k + " arousal=" + lw.GetFloatJSONParam("Arousal").val + " status=" + lw.GetStringJSONParam("Status")?.val + " thr=" + lw.GetFloatJSONParam("Orgasm Threshold").val); }
		}
		// ---- 3. AutoTremble targets the Person it is on, whatever the preset says
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 3 errors=" + Sim.Errors.Count);
		var trW = Script(woman, "AutoTremble"); var trM = Script(man, "AutoTremble");
		h.Check(trW != null && trW.GetStringChooserJSONParam("Target Atom").val == "Woman", "Woman's AutoTremble targets Woman");
		h.Check(trM != null && trM.GetStringChooserJSONParam("Target Atom").val == "Man", "Man's AutoTremble targets Man although the default preset says 'Woman'");
		h.Check(trM.GetFloatJSONParam("Frequency") != null && Math.Abs(trM.GetFloatJSONParam("Frequency").val - 4.784571f) < 1e-3f, "the default preset was applied (Frequency " + (trM.GetFloatJSONParam("Frequency") == null ? "?" : trM.GetFloatJSONParam("Frequency").val.ToString("0.000")) + ")");
		h.Check(!trW.GetBoolJSONParam("Active").isStorable, "AutoTremble's Active flag is not saved in a scene: tremble only ever starts from an orgasm or by hand");
		var tsM = trM.GetBoolJSONParam("Target Self (added atom)");
		h.Check(tsM != null && tsM.val, "'Target Self' is on by default");
		// asking for another atom while Target Self is on is refused
		trM.GetStringChooserJSONParam("Target Atom").val = "Woman";
		h.Check(trM.GetStringChooserJSONParam("Target Atom").val == "Man", "...and choosing another atom is pulled back to the Person it is on");

		// ---- 4. FluidBridge carries the orgasm hooks and the man starts on 'Penis'
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 4 errors=" + Sim.Errors.Count);
		var fbW = Script(woman, "FluidBridge"); var fbM = Script(man, "FluidBridge");
		h.Check(fbW.GetBoolJSONParam("AutoTremble on orgasm").val && fbW.GetBoolJSONParam("ZajeShuffle on orgasm").val, "FluidBridge has the tremble + shuffle hooks on");
		h.Check(fbM.GetStringChooserJSONParam("SexyFluids Emitters").val == "Penis", "Man's SexyFluids emitter starts on 'Penis'");
		h.Check(fbW.GetStringChooserJSONParam("SexyFluids Emitters").val != "Penis", "Woman's stays on nipples + squirt");

		// ---- 5. an orgasm on Woman (BodyLanguage Lite): tremble starts, a shuffle fires, tremble stops with the fluids
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 5 errors=" + Sim.Errors.Count);
		int shuffles0 = uia.buttonList.Sum(b => b.Invoked);
		var liteW = Script(woman, "BodyLanguage_Lite");
		h.Check(liteW.GetFloatJSONParam("Arousal") != null, "BodyLanguage Lite exposes Arousal");
		// the liquid bar shows the value the arousal HUD shows
		var lvT = h.Plugin.GetType().GetMethod("LiquidValue");
		liteW.GetFloatJSONParam("Arousal").val = 0.40f;
		St(h, 2f);
		float lvNow = (float)lvT.Invoke(h.Plugin, new object[] { "Woman" });
		var arP = liteW.GetFloatJSONParam("Arousal");
		h.Check(Math.Abs(lvNow - Math.Max(0f, Math.Min(1f, (arP.val - arP.min) / (arP.max - arP.min)))) < 0.001f && lvNow > 0.3f, "the liquid level is the Lite arousal (" + lvNow.ToString("0.00") + ")");
		h.Check((float)lvT.Invoke(h.Plugin, new object[] { "NoSuchPerson" }) < 0f, "...and shows nothing for a Person without the engine");

		// ---- collision arousal: None / Person+CUA / Self / Both
		SetCfg(h, "Man", "Collision", 0);   // keep Man from drifting into an orgasm of his own while the long waits below run
		var selfSw = liteW.GetBoolJSONParam("Allow Self Stimulation");
		var otherSw = liteW.GetBoolJSONParam("Allow Other Stimulation");
		h.Check(selfSw != null && otherSw != null, "BodyLanguage Lite carries both collision switches (the suite added the 'other' one)");
		SetCfg(h, "Woman", "Collision", 0);
		St(h, 2.5f);
		h.Check(!selfSw.val && !otherSw.val, "None: self and other collisions are both off");
		arP.val = 0.10f;
		St(h, 6f);
		h.Check(arP.val < 0.13f, "None: arousal does not climb from collisions (0.10 -> " + arP.val.ToString("0.000") + ")");
		SetCfg(h, "Woman", "Collision", 1);
		St(h, 2.5f);
		h.Check(!selfSw.val && otherSw.val, "Person/CUA: only other atoms count");
		arP.val = 0.10f;
		St(h, 6f);
		h.Check(arP.val > 0.17f, "Person/CUA: the other Person's contact does raise it (0.10 -> " + arP.val.ToString("0.000") + ")");
		SetCfg(h, "Woman", "Collision", 2); St(h, 2.5f);
		h.Check(selfSw.val && !otherSw.val, "Self: only the Person's own parts count");
		SetCfg(h, "Woman", "Collision", 3); St(h, 2.5f);
		h.Check(selfSw.val && otherSw.val, "Both: self and other");
		SetCfg(h, "Woman", "Collision", 1); St(h, 2.5f);
		h.Check(Orgasm(h, woman), "FluidBridge saw Woman's orgasm exactly once (BodyLanguage Lite)");
		bool trembling = trW.GetBoolJSONParam("Active").val;
		h.Check(trembling, "AutoTremble on Woman started with the orgasm");
		h.Check(uia.buttonList.Sum(b => b.Invoked) == shuffles0 + 1, "ZajeShuffle fired once (one UIAssist button activated)");
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null)
		{
			var hip = woman.freeControllers.First(c => c.name == "hipControl");
			for (int k = 0; k < 6; k++)
			{
				h.Step(0.2f);
				Console.WriteLine("  DEBUG k=" + k + " mode=" + trW.GetStringChooserJSONParam("Mode")?.val + " active=" + trW.GetBoolJSONParam("Active").val + " status=" + trW.GetStringJSONParam("Status")?.val + " hipPos=" + hip.control.position.x + "," + hip.control.position.y + "," + hip.control.position.z + " fixedDt=" + Time.fixedDeltaTime);
			}
		}
		float wig = Wiggle(woman, "hipControl", h, 1.5f);
		h.Check(wig > 0.001f, "Woman's hip actually trembles (range " + wig.ToString("0.0000") + ")");
		bool manQuiet = !trM.GetBoolJSONParam("Active").val;
		h.Check(manQuiet, "Man is not trembling (the orgasm was Woman's)");
		St(h, 12f);
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null)
			foreach (var fn in new[] { "orgasmHookStatus", "lastEvent", "triggerCount", "fluidRunning", "stopAt", "trembleStartedByBridge" })
				Console.WriteLine("  DEBUG after12 fb." + fn + " = " + fbW.GetType().GetField(fn, All).GetValue(fbW) + "  now=" + Time.realtimeSinceStartup);
		h.Check(!trW.GetBoolJSONParam("Active").val, "AutoTremble stopped when the fluids stopped");
		h.Check(Math.Abs(Wiggle(woman, "hipControl", h, 0.6f)) < 1e-4f, "...and Woman's hip is still again");

		// ---- the shuffle cooldown (1 minute by default): an orgasm right after a shuffle does not shuffle again, one after the minute does
		int sh1 = uia.buttonList.Sum(b => b.Invoked);
		St(h, 20f);   // the engine needs a while after an orgasm before it can climax again
		arP.val = 0f;
		h.Check(Orgasm(h, woman), "a second orgasm, within the minute, is seen");

		h.Check(uia.buttonList.Sum(b => b.Invoked) == sh1, "cooldown: no second shuffle straight after the first");
		var fbWs = Script(woman, "FluidBridge");
		h.Check(fbWs.GetFloatJSONParam("ZajeShuffle cooldown (min)") != null && Math.Abs(fbWs.GetFloatJSONParam("ZajeShuffle cooldown (min)").val - 1f) < 0.01f, "FluidBridge carries the cooldown (1 min by default)");
		SetCfg(h, "Woman", "Collision", 0);   // no spontaneous orgasm while the minute runs out
		St(h, 70f);
		SetCfg(h, "Woman", "Collision", 1);
		arP.val = 0f;
		h.Check(Orgasm(h, woman), "an orgasm after the cooldown is seen");
		h.Check(uia.buttonList.Sum(b => b.Invoked) == sh1 + 1, "cooldown over: the shuffle fires again");
		SetCfg(h, "Woman", "ShuffleCooldown", 5f);
		St(h, 2.5f);
		h.Check(Math.Abs(Script(woman, "FluidBridge").GetFloatJSONParam("ZajeShuffle cooldown (min)").val - 5f) < 0.01f, "the window's cooldown (5 min) reaches FluidBridge");
		SetCfg(h, "Woman", "ShuffleCooldown", 1f);
		SetCfg(h, "Woman", "Collision", 1);
		St(h, 16f);   // let the tremble of those orgasms end

		// ---- 6. the same on the man: his AutoTremble moves HIS controllers
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 6 errors=" + Sim.Errors.Count);
		var liteM = Script(man, "BodyLanguage_Lite");
		SetCfg(h, "Man", "Collision", 1);   // his contact with Woman is what lifts him over the threshold in this simulation
		St(h, 2.5f);
		h.Check(Orgasm(h, man), "FluidBridge saw Man's orgasm exactly once");
		h.Check(trM.GetBoolJSONParam("Active").val, "AutoTremble on Man started with his orgasm");
		float wigM = Wiggle(man, "hipControl", h, 1.5f);
		h.Check(wigM > 0.001f, "Man's hip trembles (" + wigM.ToString("0.0000") + ")");
		h.Check(Math.Abs(Wiggle(woman, "hipControl", h, 0.4f)) < 1e-4f, "...while Woman stays still");
		St(h, 12f);

		// ---- 6b. the window's "Orgasm now" and "Test fluids" buttons (they act on the selected Person)
		var pt = h.Plugin.GetType();
		pt.GetField("_uid", All).SetValue(h.Plugin, "Woman");
		Func<string, string, string, bool> press = (comp, action, what) =>
		{
			var cf = pt.GetField(comp, BindingFlags.Public | BindingFlags.Static).GetValue(null);
			pt.GetMethod("TriggerAction", All).Invoke(h.Plugin, new object[] { cf, action, what });
			return true;
		};
		int t0 = Triggers(fbW);
		press("Lite", "Force Orgasm", "Orgasm"); press("Bridge", "Test Orgasm Fluids", "Orgasm"); St(h, 1.2f);   // what the button does
		h.Check(Triggers(fbW) == t0 + 1 && trW.GetBoolJSONParam("Active").val, "'Orgasm now' fires the engine's orgasm: FluidBridge triggers and AutoTremble starts");
		St(h, 12f);
		int t1 = Triggers(fbW);
		press("Bridge", "Test Orgasm Fluids", "Test fluids"); St(h, 0.5f);
		h.Check(Triggers(fbW) == t1 + 1, "'Test fluids' runs FluidBridge's test");
		St(h, 12f);
		// ---- 7. switch Woman to BodyLanguage: Lite + Arousal HUD go, BodyLanguage + Stimulation HUD come
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 7 errors=" + Sim.Errors.Count);
		SetCfg(h, "Woman", "Engine", "BodyLanguage");
		St(h, 10f);
		h.Check(!Has(woman, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll") && !Has(woman, "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll"), "Lite and the Arousal HUD were detached");
		h.Check(Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll"), "BodyLanguage was attached");
		h.Check(Has(woman, "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll"), "the Stimulation HUD was attached");
		h.Check(Has(man, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll") && !Has(man, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll"), "Man stays on Lite (own setting)");
		h.Check(Script(woman, "BodyLanguage_Lite") == null && Script(woman, "ArousalHUD") == null, "the old scripts are really gone from Woman");
		// the same switch for BodyLanguage: a static filter in its touch code
		var blAsm = Script(woman, "ReadMyLips").GetType().Assembly;
		var filterT = blAsm.GetType("OrgasmSuiteFilter");
		var modeF = filterT == null ? null : filterT.GetField("Mode");
		SetCfg(h, "Woman", "Collision", 2); St(h, 3f);
		h.Check(modeF != null && (int)modeF.GetValue(null) == 2, "BodyLanguage: Self reaches the touch filter (Mode " + (modeF == null ? "?" : modeF.GetValue(null).ToString()) + ")");
		SetCfg(h, "Woman", "Collision", 0); St(h, 3f);
		h.Check(modeF != null && (int)modeF.GetValue(null) == 0, "BodyLanguage: None reaches the touch filter");
		SetCfg(h, "Woman", "Collision", 1); St(h, 3f);
		foreach (var e in loadErrors) Console.WriteLine("  load error: " + e);

		// AutoTremble follows BodyLanguage's own orgasm state: starts with it, runs while it lasts, stops when it is over
		{
			var sf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			var rmlT = blAsm.GetType("CheesyFX.ReadMyLips");
			var pleasureF = rmlT == null ? null : rmlT.GetField("isOrgasmPleasure", sf);
			var orgasmingF = rmlT == null ? null : rmlT.GetField("isOrgasming", sf);
			var trem = Script(woman, "AutoTremble");
			var tact = trem == null ? null : trem.GetBoolJSONParam("Active");
			h.Check(pleasureF != null && orgasmingF != null && tact != null, "BodyLanguage orgasm state + AutoTremble 'Active' are reachable");
			if (pleasureF != null && orgasmingF != null && tact != null)
			{
				St(h, 2f);
				h.Check(!tact.val, "BodyLanguage: AutoTremble is idle before an orgasm");
				orgasmingF.SetValue(null, true); pleasureF.SetValue(null, true);
				St(h, 2f);
				h.Check(tact.val, "BodyLanguage orgasm started AutoTremble");
				St(h, 4f);
				h.Check(tact.val, "AutoTremble keeps running while the orgasm lasts");
				pleasureF.SetValue(null, false);
				St(h, 2f);
				h.Check(tact.val, "AutoTremble keeps running through the orgasm fade (isOrgasming still set)");
				orgasmingF.SetValue(null, false);
				St(h, 2f);
				h.Check(!tact.val, "AutoTremble stopped when the BodyLanguage orgasm was over");
			}
		}

		// BodyLanguage is a singleton: a second Person asking for it is held back
		SetCfg(h, "Man", "Engine", "BodyLanguage");
		St(h, 6f);
		h.Check(!Has(man, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll"), "a second Person cannot take BodyLanguage while Woman has it");

		// ---- 8. parts on/off
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 8 errors=" + Sim.Errors.Count);
		SetCfg(h, "Woman", "Tremble", false);
		St(h, 3f);
		h.Check(!Has(woman, "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll"), "Tremble off: AutoTremble detached");
		SetCfg(h, "Woman", "Fluids", false);
		St(h, 4f);
		h.Check(!Has(woman, "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll"), "Fluids off: SexyFluids detached");
		SetCfg(h, "Woman", "Hud", false);
		St(h, 3f);
		h.Check(!Has(woman, "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll"), "HUD off: Stimulation HUD detached");
		SetCfg(h, "Woman", "Fluids", true); SetCfg(h, "Woman", "Tremble", true); SetCfg(h, "Woman", "Hud", true);
		St(h, 10f);
		h.Check(Has(woman, "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll") && Has(woman, "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll") && Has(woman, "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll"), "...and they come back when switched on again");

		// the Suite switch and the mode switch both park the Person and bring back exactly what was set
		SetCfg(h, "Man", "Engine", "Lite");   // BodyLanguage is a single-instance script: a Person waiting for it would take it while Woman is parked
		St(h, 8f);
		SetCfg(h, "Woman", "On", false);
		St(h, 14f);
		h.Check(!Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll") && !Has(woman, "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll") && !Has(woman, "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll"), "Suite off: everything parked");
		SetCfg(h, "Woman", "On", true);
		St(h, 14f);
		h.Check(Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll") && Has(woman, "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll") && Has(woman, "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll") && (string)CfgVal(h, "Woman", "Engine") == "BodyLanguage", "Suite on again: same engine and parts as before");
		SetCfg(h, "Woman", "ModeOn", false);
		St(h, 14f);
		h.Check(!Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll") && (bool)CfgVal(h, "Woman", "Tremble") && (bool)CfgVal(h, "Woman", "Fluids"), "mode off: engine parked, the module choices are kept");
		SetCfg(h, "Woman", "ModeOn", true);
		St(h, 14f);
		h.Check(Has(woman, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll") && Has(woman, "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll"), "mode on again: back as it was");

		// ---- 8b. the "during orgasm" switches reach FluidBridge; ZajeShuffle leaves CoreControl when nobody wants it
		SetCfg(h, "Woman", "Wet", false); SetCfg(h, "Woman", "Wardrobe", false);
		St(h, 2f);
		var fbS = Script(woman, "FluidBridge");
		h.Check(!fbS.GetBoolJSONParam("Apply Wet Clothes preset on orgasm").val && !fbS.GetBoolJSONParam("Random clothing on orgasm").val, "Wet clothes / Wardrobe off: pushed into FluidBridge");
		SetCfg(h, "Woman", "Wet", true); SetCfg(h, "Woman", "Wardrobe", true);
		St(h, 2f);
		h.Check(fbS.GetBoolJSONParam("Apply Wet Clothes preset on orgasm").val && fbS.GetBoolJSONParam("Random clothing on orgasm").val, "...and back on");
		SetCfg(h, "Woman", "Shuffle", false); SetCfg(h, "Man", "Shuffle", false);
		St(h, 4f);
		h.Check(!Has(core, "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "no Person wants a shuffle: ZajeShuffle leaves CoreControl");
		h.Check(!fbS.GetBoolJSONParam("ZajeShuffle on orgasm").val, "...and FluidBridge's shuffle hook is off");
		SetCfg(h, "Woman", "Shuffle", true);
		St(h, 4f);
		h.Check(Has(core, "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "one Person wants it again: ZajeShuffle is back (session wide)");
		SetCfg(h, "Man", "Shuffle", true);

		// the shuffle can wait: ShuffleDelay seconds after the orgasm
		SetCfg(h, "Woman", "ShuffleDelay", 3f);
		St(h, 3f);
		h.Check(Math.Abs(fbS.GetFloatJSONParam("ZajeShuffle delay (sec)").val - 3f) < 0.01f, "the delay set in the window reaches FluidBridge");
		int sh0 = uia.buttonList.Sum(b => b.Invoked);
		press("Bridge", "Test Orgasm Fluids", "Test fluids"); St(h, 1.2f);
		h.Check(uia.buttonList.Sum(b => b.Invoked) == sh0, "with a 3 s delay the shuffle has not run 1.2 s after the orgasm");
		St(h, 3f);
		h.Check(uia.buttonList.Sum(b => b.Invoked) == sh0 + 1, "...and runs once, after the delay");
		SetCfg(h, "Woman", "ShuffleDelay", 0f);
		St(h, 14f);   // let the fluids / tremble of that test finish

		// ---- 9. Suite OFF removes only the suite's own scripts
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 9 errors=" + Sim.Errors.Count);
		SetCfg(h, "Man", "Engine", "Lite");
		SetCfg(h, "Man", "On", false);
		St(h, 10f);
		h.Check(SlotUrls(man).Count == 0, "Suite OFF: Man has no suite script left (" + SlotUrls(man).Count + ")");
		h.Check(Has(core, "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "ZajeShuffle on CoreControl is session wide and stays");
		SetCfg(h, "Man", "On", true);
		St(h, 10f);
		h.Check(Has(man, "FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll") && Has(man, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll"), "Suite ON again: Man's scripts are back");

		// ---- 10. a script somebody else loaded is left alone
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 10 errors=" + Sim.Errors.Count);
		var other = Person(h, "Third", false);
		var asmFb = Asm(h, "ZeroT.OrgasmSuite.FluidBridge");
		var fbType = TypeOf(asmFb, "FluidBridge");
		var ext = SceneHost.Spawn(fbType, other, ((MVRPluginManager)other.GetStorableByID("PluginManager")).gameObject, "plugin#9", "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist");
		ext.Init();
		St(h, 10f);
		h.Check(!Has(other, "OrgasmSuite/FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll"), "a FluidBridge loaded from a .var is not duplicated");
		h.Check(Has(other, "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll") && Has(other, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll"), "...the rest of the suite still attaches to that Person");

		// a scene's own .var plugin that is still compiling: its slot is there, its script is not yet
		var fourth = Person(h, "Fourth", false);
		var fm = (MVRPluginManager)fourth.GetStorableByID("PluginManager");
		fm.CreatePlugin().pluginURLJSON.val = "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist";
		St(h, 10f);
		h.Check(!Has(fourth, "OrgasmSuite/FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll") && fm.plugins.Count(p => p.pluginURLJSON.val.EndsWith("FluidBridge.cslist")) == 1, "a .var's FluidBridge slot that is still compiling is not duplicated either");

		// ---- 10b. VaM is still restoring a scene: slots keep changing, nothing may be attached until it is quiet (attaching mid-restore crashed VaM)
		var late = Person(h, "Late", false);
		var lm = (MVRPluginManager)late.GetStorableByID("PluginManager");
		for (int i = 0; i < 5; i++) { lm.CreatePlugin().pluginURLJSON.val = "SomePkg.1:/Custom/Scripts/x/Other" + i + ".cs"; St(h, 0.8f); }
		h.Check(!SlotUrls(late).Any(u => u.Contains("OrgasmSuite")), "while a Person's plugin slots keep changing the suite attaches nothing");
		St(h, 10f);
		h.Check(Has(late, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll"), "...and starts once the scene has been quiet");

		// ---- 11. a scene load: everything is replaced, the suite re-attaches from the saved per-Person settings
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 11 errors=" + Sim.Errors.Count);
		h.Load(() =>
		{
			Core(h);
			Person(h, "Woman", false);
			Person(h, "Man", true);
		});
		St(h, 0.5f);
		h.Check(SlotUrls(h.Sc.GetAtomByUid("Woman")).Count == 0, "right after a scene load nothing is attached yet (it waits for the scene to settle)");
		St(h, 24f);
		var woman2 = h.Sc.GetAtomByUid("Woman");
		var man2 = h.Sc.GetAtomByUid("Man");
		h.Check(Has(woman2, "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll"), "after a scene load Woman is on BodyLanguage again (saved per-Person choice)");
		h.Check(Has(man2, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll"), "...and Man on Lite");
		h.Check(Has(h.Sc.GetAtomByUid("CoreControl"), "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll"), "...and ZajeShuffle on the new CoreControl");
		string people = File.ReadAllText(Path.Combine(game, "Saves", "PluginData", "ZeroT", "OrgasmSuite", "people.tsv"));
		h.Check(people.Contains("Woman\t1\tBodyLanguage") && people.Contains("Man\t1\tLite"), "people.tsv holds the choices");

		// ---- 11b. "Auto-add on scene load" off: a loaded scene is left alone until a Person is added by hand
		h.Set("Suite", "AutoAddOnLoad", "false");
		h.Load(() =>
		{
			Core(h);
			Person(h, "Woman", false);
			Person(h, "Man", true);
		});
		St(h, 30f);
		var womanH = h.Sc.GetAtomByUid("Woman"); var manH = h.Sc.GetAtomByUid("Man");
		h.Check(SlotUrls(womanH).Count == 0 && SlotUrls(manH).Count == 0 && SlotUrls(h.Sc.GetAtomByUid("CoreControl")).Count == 0, "auto-add off: after a scene load nothing is attached to anybody");
		h.Plugin.GetType().GetMethod("Release", All).Invoke(h.Plugin, new object[] { "Woman", true });
		St(h, 20f);
		h.Check(Has(womanH, "FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll") && SlotUrls(manH).Count == 0, "'Add to this Person' adds the suite to Woman only, Man stays untouched");
		h.Set("Suite", "AutoAddOnLoad", "true");
		h.Plugin.GetType().GetMethod("SetAutoAddOnLoad", All).Invoke(h.Plugin, new object[] { true });
		St(h, 24f);
		h.Check(Has(manH, "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll"), "switching auto-add on brings the waiting Person in");

		// ---- 12. the master switch / auto attach off
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 12 errors=" + Sim.Errors.Count);
		h.Set("Suite", "Active", "false");
		int before = SlotUrls(woman2).Count;
		SetCfg(h, "Woman", "Engine", "Lite");
		St(h, 8f);
		h.Check(SlotUrls(woman2).Count == before, "Active=false: the suite changes nothing");
		h.Set("Suite", "Active", "true");
		h.Set("Suite", "AutoAttach", "false");
		SetCfg(h, "Woman", "Engine", "BodyLanguage");
		St(h, 8f);
		h.Check(SlotUrls(woman2).Count == before, "AutoAttach=false: the suite never touches a Person");
		h.Set("Suite", "AutoAttach", "true");
		foreach (var e in loadErrors) Console.WriteLine("  load error: " + e);
		h.Check(loadErrors.Count == 0, "no script failed to load or Init over the whole run (" + loadErrors.Count + " errors)");

		// what the simulator cannot run (SexyFluids / BodyLanguage frames): listed, and nothing else may hide in there
		var limited = Sim.Errors.Where(e => e.StartsWith("ReadMyLips.") || e.StartsWith("SexyFluids.") || e.Contains("SexyFluids") || e.Contains("ReadMyLips")).ToList();
		foreach (var e in limited) Sim.Errors.Remove(e);
		foreach (var n in simNotes.Distinct().Take(6)) Console.WriteLine("  note   sim cannot run: " + n);
		foreach (var n in limited.Select(x => x.Length > 160 ? x.Substring(0, 160) : x).Distinct().Take(6)) Console.WriteLine("  note   sim cannot run: " + n);
		Console.WriteLine("  note   (SexyFluids and BodyLanguage were attached and detached for real; their own rendering / voice / pose code is not exercised by the simulator)");
	}
}

// a stand-in for BodyLanguage's own script: same type name, the one param the Stimulation HUD binds to. BodyLanguage itself needs more of Unity
// than the simulator has, so this only supplies the VALUE the real StimulationHUD script displays.
namespace CheesyFX
{
	public class ReadMyLips : MVRScript
	{
		public JSONStorableFloat stimulation;
		public override void Init()
		{
			stimulation = new JSONStorableFloat("Stimulation", 0f, 0f, 1f);
			RegisterFloat(stimulation);
		}
	}
}

public static class SuiteInputScenario
{
	// the skin's hand-written input: a click is press + release inside one rectangle (no IMGUI control per button), the segmented bar drags, a disabled GUI ignores it
	[Scenario("orgasmsuite_input", "ZeroT.OrgasmSuite")]
	public static void Input(Harness h)
	{
		var asm = h.Asm;
		var fx = asm.GetType("ZeroT.OrgasmSuite.Fx");
		h.Check(fx != null, "the skin class is there");
		if (fx == null) return;
		const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
		var window = fx.GetMethod("Window", All); var hit = fx.GetMethod("Hit", All); var bar = fx.GetMethod("Bar", All);
		var panel = new Rect(0, 0, 400, 300);
		var btn = new Rect(20, 20, 100, 24);
		Func<EventType, Vector2, int, Event> ev = (t, pos, button) => { var e = new Event { type = t, mousePosition = pos, button = button }; Event.current = e; window.Invoke(null, new object[] { panel, 1f, false }); return e; };
		Func<bool> click = () => (bool)hit.Invoke(null, new object[] { btn });

		var inside = new Vector2(60, 30); var outside = new Vector2(300, 200);
		var e1 = ev(EventType.MouseDown, inside, 0);
		h.Check(!click() && e1.type == EventType.Used, "press on a button: no click yet, the press is taken");
		var e2 = ev(EventType.MouseUp, inside, 0);
		h.Check(click() && e2.type == EventType.Used, "release inside the same button: a click");
		ev(EventType.MouseUp, inside, 0);
		h.Check(!click(), "a second release does not click again");
		ev(EventType.MouseDown, inside, 0); click();
		ev(EventType.MouseUp, outside, 0);
		h.Check(!click(), "press on a button, release elsewhere: no click");
		ev(EventType.MouseDown, outside, 0);
		h.Check(!click(), "a press outside the button is ignored");
		ev(EventType.MouseDown, inside, 1);
		h.Check(!click(), "the right button is ignored");
		ev(EventType.MouseDown, inside, 0); click();
		UnityEngine.GUI.enabled = false;
		ev(EventType.MouseUp, inside, 0);
		h.Check(!click(), "a disabled GUI does not click");
		UnityEngine.GUI.enabled = true;
		ev(EventType.MouseUp, inside, 0); click();

		// the segmented bar: press sets the value from the mouse x, drag follows, release ends it
		var br = new Rect(20, 100, 240, 18);
		Func<EventType, float, float> drag = (t, x) => { ev(t, new Vector2(x, 108), 0); return (float)bar.Invoke(null, new object[] { br, 0.5f, Color.green, 1f, 9901 }); };
		float vL = drag(EventType.MouseDown, 24f);
		float vM = drag(EventType.MouseDrag, 140f);
		float vR = drag(EventType.MouseDrag, 400f);
		drag(EventType.MouseUp, 140f);
		float after = drag(EventType.MouseDrag, 100f);
		h.Check(vL >= 0f && vL < 0.05f, "bar: press at the left end = about 0 (" + vL.ToString("0.00") + ")");
		h.Check(vM > 0.4f && vM < 0.6f, "bar: drag to the middle = about 0.5 (" + vM.ToString("0.00") + ")");
		h.Check(vR > 0.99f, "bar: dragged past the right end = 1");
		h.Check(after < 0f, "bar: after the release a drag changes nothing");
	}
}

public static class SuiteHudScenario
{
	// the real ArousalHUD (BodyLanguage Lite) and the real StimulationHUD, rendered by the simulator's uGUI drawer into UiSim/out/hud/*.png
	[Scenario("orgasmsuite_hud", "ZeroT.OrgasmSuite")]
	public static void Huds(Harness h)
	{
		var outDir = Path.GetFullPath(Path.Combine(Harness.CoresDir, "..", "..", "out", "hud"));
		SuperController.SimSaved.Clear();
		var woman = new GameObject("Woman").AddComponent<Atom>();
		h.Sc.AddAtom(woman);   // placeholder replaced below
		h.Sc.RemoveAtom(woman);
		var a = h.Person("Woman", false);
		var mgrGo = new GameObject("PluginManager"); mgrGo.transform.SetParent(a.transform, false);
		var mgr = mgrGo.AddComponent<MVRPluginManager>(); mgr.storeId = "PluginManager"; mgr.containingAtom = a; a.storables["PluginManager"] = mgr;
		// the same loader the host scenario uses
		var asmCache = new Dictionary<string, Assembly>();
		MVRPluginManager.SimLoader = (m, plugin, url) =>
		{
			string set = url.Contains("StimulationHUD") ? "ZeroT.OrgasmSuite.StimulationHUD" : (url.Contains("ArousalHUD") ? "ZeroT.OrgasmSuite.ArousalHUD" : "ZeroT.OrgasmSuite.BodyLanguageLite");
			string type = url.EndsWith("ArousalHUD.dll") ? "ArousalHUD" : url.EndsWith("StimulationHUD.dll") ? "StimulationHUD" : "BodyLanguage_Lite";
			Assembly asm;
			if (!asmCache.TryGetValue(set, out asm)) { asm = Assembly.LoadFrom(Program.Retarget(Path.Combine(Path.GetFullPath(Path.Combine(Harness.CoresDir, "..", "suite")), set + ".dll"), h.Work)); asmCache[set] = asm; }
			var t = asm.GetTypes().First(x => typeof(MVRScript).IsAssignableFrom(x) && !x.IsAbstract && x.Name == type);
			var script = SceneHost.Spawn(t, m.containingAtom, m.gameObject, plugin.uid, url);
			script.Init();
			return script.gameObject;
		};
		Func<string, MVRScript> attach = url =>
		{
			var p = mgr.CreatePlugin(); p.pluginURLJSON.val = url;
			return a.gameObject.GetComponentsInChildren<MVRScript>(true).Last();
		};
		const string root = "Custom/Scripts/ZeroT/OrgasmSuite/";
		var lite = attach(root + "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll");
		var hud = attach(root + "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll");
		h.Step(3f);
		var arousal = lite.GetFloatJSONParam("Arousal");
		arousal.val = 0.62f;
		h.Step(1.5f);
		var issues = SimSnapshot.Png(Path.Combine(outDir, "ArousalHUD_Lite.png"), Screen.width, Screen.height);
		h.Check(File.Exists(Path.Combine(outDir, "ArousalHUD_Lite.png")), "ArousalHUD rendered (" + Path.Combine(outDir, "ArousalHUD_Lite.png") + ")");
		foreach (var i in issues) Console.WriteLine("  hud issue: " + i);

		// the Stimulation HUD for BodyLanguage: Lite and its HUD out, a stand-in carrying 'Stimulation', the real StimulationHUD in
		mgr.RemovePluginWithUID(hud.GetComponentInParent<Transform>() == null ? "" : mgr.plugins.Last().uid);
		foreach (var p in mgr.plugins.ToArray()) mgr.RemovePluginWithUID(p.uid);
		h.Step(0.5f);
		var go = new GameObject("plugin#7_CheesyFX.ReadMyLips"); go.transform.SetParent(mgrGo.transform, false);
		var bl = (CheesyFX.ReadMyLips)go.AddComponent(typeof(CheesyFX.ReadMyLips));
		bl.containingAtom = a; bl.storeId = "plugin#7_CheesyFX.ReadMyLips"; bl.enabledJSON = new JSONStorableBool("enabled", true); bl.pluginLabelJSON = new JSONStorableString("pluginLabel", "");
		a.RegisterAdditionalStorable(bl); bl.Init();
		var stim = attach(root + "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll");
		h.Step(3f);
		bl.stimulation.val = 0.45f;
		h.Step(1.5f);
		var issues2 = SimSnapshot.Png(Path.Combine(outDir, "StimulationHUD_BodyLanguage.png"), Screen.width, Screen.height);
		h.Check(File.Exists(Path.Combine(outDir, "StimulationHUD_BodyLanguage.png")), "StimulationHUD rendered");
		foreach (var i in issues2) Console.WriteLine("  hud issue: " + i);
		h.Check(SuperController.Messages.Any(m => m.Contains("BL Stimulation found")), "the Stimulation HUD found BodyLanguage's Stimulation value");
	}
}
