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
	private static bool SimLimited(string set) { return set == "SexyFluids" || set == "BodyLanguage"; }

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
		switch (rest)
		{
			case "BodyLanguage_Lite/BodyLanguage_Lite.cslist": set = "BodyLanguage_Lite"; type = "BodyLanguage_Lite"; break;
			case "BodyLanguage_Lite/ArousalHUD.cslist": set = "BodyLanguage_Lite"; type = "ArousalHUD"; break;
			case "BodyLanguage/BodyLanguage.cslist": set = "BodyLanguage"; type = "ReadMyLips"; break;
			case "StimulationHUD/StimulationHUD.cslist": set = "StimulationHUD"; type = "StimulationHUD"; break;
			case "SexyFluids/SexyFluids.cslist": set = "SexyFluids"; type = "SexyFluids"; break;
			case "FluidBridge/FluidBridge.cslist": set = "FluidBridge"; type = "FluidBridge"; break;
			case "AutoTremble/AutoTremble.cs": set = "AutoTremble"; type = "AutoTremble"; break;
			case "ZajeShuffle/ZajeShuffle.cslist": set = "ZajeShuffle"; type = "ZajeShuffle"; break;
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
		h.Start();
		h.Check(h.Plugin != null, "the plugin starts");

		// ---- 1. the embedded script pack lands on disk
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 1 errors=" + Sim.Errors.Count);
		string sd = Path.Combine(game, "Custom", "Scripts", "ZeroT", "OrgasmSuite");
		foreach (var f in new[] { "FluidBridge/FluidBridge.cslist", "FluidBridge/FluidBridge.cs", "AutoTremble/AutoTremble.cs", "BodyLanguage_Lite/BodyLanguage_Lite.cslist", "BodyLanguage_Lite/ArousalHUD.cslist",
			"BodyLanguage/BodyLanguage.cslist", "StimulationHUD/StimulationHUD.cslist", "ZajeShuffle/ZajeShuffle.cslist", "SexyFluids/SexyFluids.cslist", "SexyFluids/data/Foost.SexyFluids.fluid_shaders.assetbundle" })
			h.Check(File.Exists(Path.Combine(sd, f.Replace('/', Path.DirectorySeparatorChar))), "extracted " + f);
		int nfiles = Directory.GetFiles(sd, "*", SearchOption.AllDirectories).Length;
		h.Check(nfiles >= 190, "all the scripts are there (" + nfiles + " files)");
		h.Check(File.ReadAllText(Path.Combine(game, "Saves", "PluginPresets", "AutoTremble", "AutoTremblePreset.json")) == TremblePreset, "the user's AutoTremble preset was not overwritten");
		h.Check(Directory.GetFiles(Path.Combine(game, "Saves", "PluginData", "Foost", "SexyFluids", "JizzSim"), "*.json").Length >= 5, "SexyFluids' shipped presets were added where missing");
		string fbText = File.ReadAllText(Path.Combine(sd, "FluidBridge", "FluidBridge.cs"));
		h.Check(fbText.Contains("OrgasmHooks") && fbText.Contains("const string Version = \"28\""), "FluidBridge is the patched v28");
		h.Check(File.ReadAllText(Path.Combine(sd, "AutoTremble", "AutoTremble.cs")).Contains("Target Self (added atom)"), "AutoTremble is the self-targeting build");

		// ---- 2. everything attaches, one change per scan
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 2 errors=" + Sim.Errors.Count);
		St(h, 16f);
		string[] womanWants = { "BodyLanguage_Lite/BodyLanguage_Lite.cslist", "SexyFluids/SexyFluids.cslist", "FluidBridge/FluidBridge.cslist", "AutoTremble/AutoTremble.cs", "BodyLanguage_Lite/ArousalHUD.cslist" };
		foreach (var u in womanWants)
		{
			h.Check(Has(woman, u), "Woman has " + u);
			// every HUD pins itself to the same desktop spot: only the first Person starts with one
			h.Check(u.Contains("ArousalHUD") ? !Has(man, u) : Has(man, u), u.Contains("ArousalHUD") ? "Man starts without a HUD (Woman has it)" : "Man has " + u);
		}
		h.Check(Has(core, "ZajeShuffle/ZajeShuffle.cslist"), "CoreControl has ZajeShuffle (session wide)");
		h.Check(!Has(woman, "BodyLanguage/BodyLanguage.cslist") && !Has(woman, "StimulationHUD/StimulationHUD.cslist"), "default engine is Lite: no BodyLanguage, no Stimulation HUD");
		foreach (var e in loadErrors) Console.WriteLine("  load error: " + e);
		h.Check(loadErrors.Count == 0, "no script failed to load or Init (" + loadErrors.Count + " errors)");
		SetCfg(h, "Man", "Hud", true);
		St(h, 4f);
		h.Check(Has(man, "BodyLanguage_Lite/ArousalHUD.cslist"), "...and gets its own when switched on");
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

		// ---- 6. the same on the man: his AutoTremble moves HIS controllers
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 6 errors=" + Sim.Errors.Count);
		var liteM = Script(man, "BodyLanguage_Lite");
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
		h.Check(!Has(woman, "BodyLanguage_Lite/BodyLanguage_Lite.cslist") && !Has(woman, "BodyLanguage_Lite/ArousalHUD.cslist"), "Lite and the Arousal HUD were detached");
		h.Check(Has(woman, "BodyLanguage/BodyLanguage.cslist"), "BodyLanguage was attached");
		h.Check(Has(woman, "StimulationHUD/StimulationHUD.cslist"), "the Stimulation HUD was attached");
		h.Check(Has(man, "BodyLanguage_Lite/BodyLanguage_Lite.cslist") && !Has(man, "BodyLanguage/BodyLanguage.cslist"), "Man stays on Lite (own setting)");
		h.Check(Script(woman, "BodyLanguage_Lite") == null && Script(woman, "ArousalHUD") == null, "the old scripts are really gone from Woman");
		foreach (var e in loadErrors) Console.WriteLine("  load error: " + e);

		// BodyLanguage is a singleton: a second Person asking for it is held back
		SetCfg(h, "Man", "Engine", "BodyLanguage");
		St(h, 6f);
		h.Check(!Has(man, "BodyLanguage/BodyLanguage.cslist"), "a second Person cannot take BodyLanguage while Woman has it");

		// ---- 8. parts on/off
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 8 errors=" + Sim.Errors.Count);
		SetCfg(h, "Woman", "Tremble", false);
		St(h, 3f);
		h.Check(!Has(woman, "AutoTremble/AutoTremble.cs"), "Tremble off: AutoTremble detached");
		SetCfg(h, "Woman", "Fluids", false);
		St(h, 4f);
		h.Check(!Has(woman, "SexyFluids/SexyFluids.cslist"), "Fluids off: SexyFluids detached");
		SetCfg(h, "Woman", "Hud", false);
		St(h, 3f);
		h.Check(!Has(woman, "StimulationHUD/StimulationHUD.cslist"), "HUD off: Stimulation HUD detached");
		SetCfg(h, "Woman", "Fluids", true); SetCfg(h, "Woman", "Tremble", true); SetCfg(h, "Woman", "Hud", true);
		St(h, 10f);
		h.Check(Has(woman, "SexyFluids/SexyFluids.cslist") && Has(woman, "AutoTremble/AutoTremble.cs") && Has(woman, "StimulationHUD/StimulationHUD.cslist"), "...and they come back when switched on again");

		// ---- 9. Suite OFF removes only the suite's own scripts
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 9 errors=" + Sim.Errors.Count);
		SetCfg(h, "Man", "Engine", "Lite");
		SetCfg(h, "Man", "On", false);
		St(h, 10f);
		h.Check(SlotUrls(man).Count == 0, "Suite OFF: Man has no suite script left (" + SlotUrls(man).Count + ")");
		h.Check(Has(core, "ZajeShuffle/ZajeShuffle.cslist"), "ZajeShuffle on CoreControl is session wide and stays");
		SetCfg(h, "Man", "On", true);
		St(h, 10f);
		h.Check(Has(man, "FluidBridge/FluidBridge.cslist") && Has(man, "BodyLanguage_Lite/BodyLanguage_Lite.cslist"), "Suite ON again: Man's scripts are back");

		// ---- 10. a script somebody else loaded is left alone
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 10 errors=" + Sim.Errors.Count);
		var other = Person(h, "Third", false);
		var asmFb = Asm(h, "FluidBridge");
		var fbType = TypeOf(asmFb, "FluidBridge");
		var ext = SceneHost.Spawn(fbType, other, ((MVRPluginManager)other.GetStorableByID("PluginManager")).gameObject, "plugin#9", "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist");
		ext.Init();
		St(h, 10f);
		h.Check(!Has(other, "FluidBridge/FluidBridge.cslist"), "a FluidBridge loaded from a .var is not duplicated");
		h.Check(Has(other, "SexyFluids/SexyFluids.cslist") && Has(other, "BodyLanguage_Lite/BodyLanguage_Lite.cslist"), "...the rest of the suite still attaches to that Person");

		// a scene's own .var plugin that is still compiling: its slot is there, its script is not yet
		var fourth = Person(h, "Fourth", false);
		var fm = (MVRPluginManager)fourth.GetStorableByID("PluginManager");
		fm.CreatePlugin().pluginURLJSON.val = "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist";
		St(h, 10f);
		h.Check(!Has(fourth, "OrgasmSuite/FluidBridge/FluidBridge.cslist") && fm.plugins.Count(p => p.pluginURLJSON.val.EndsWith("FluidBridge.cslist")) == 1, "a .var's FluidBridge slot that is still compiling is not duplicated either");

		// ---- 11. a scene load: everything is replaced, the suite re-attaches from the saved per-Person settings
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 11 errors=" + Sim.Errors.Count);
		h.Load(() =>
		{
			Core(h);
			Person(h, "Woman", false);
			Person(h, "Man", true);
		});
		St(h, 24f);
		var woman2 = h.Sc.GetAtomByUid("Woman");
		var man2 = h.Sc.GetAtomByUid("Man");
		h.Check(Has(woman2, "BodyLanguage/BodyLanguage.cslist"), "after a scene load Woman is on BodyLanguage again (saved per-Person choice)");
		h.Check(Has(man2, "BodyLanguage_Lite/BodyLanguage_Lite.cslist"), "...and Man on Lite");
		h.Check(Has(h.Sc.GetAtomByUid("CoreControl"), "ZajeShuffle/ZajeShuffle.cslist"), "...and ZajeShuffle on the new CoreControl");
		string people = File.ReadAllText(Path.Combine(game, "Saves", "PluginData", "ZeroT", "OrgasmSuite", "people.tsv"));
		h.Check(people.Contains("Woman\t1\tBodyLanguage") && people.Contains("Man\t1\tLite"), "people.tsv holds the choices");

		// ---- 12. the master switch
		if (Environment.GetEnvironmentVariable("SUITE_DEBUG") != null) Console.WriteLine("  DEBUG stage 12 errors=" + Sim.Errors.Count);
		h.Set("Suite", "Active", "false");
		int before = SlotUrls(woman2).Count;
		SetCfg(h, "Woman", "Engine", "Lite");
		St(h, 8f);
		h.Check(SlotUrls(woman2).Count == before, "Active=false: the suite changes nothing");
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
			string set = url.Contains("StimulationHUD") ? "StimulationHUD" : "BodyLanguage_Lite";
			string type = url.EndsWith("ArousalHUD.cslist") ? "ArousalHUD" : url.EndsWith("StimulationHUD.cslist") ? "StimulationHUD" : "BodyLanguage_Lite";
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
		var lite = attach(root + "BodyLanguage_Lite/BodyLanguage_Lite.cslist");
		var hud = attach(root + "BodyLanguage_Lite/ArousalHUD.cslist");
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
		var stim = attach(root + "StimulationHUD/StimulationHUD.cslist");
		h.Step(3f);
		bl.stimulation.val = 0.45f;
		h.Step(1.5f);
		var issues2 = SimSnapshot.Png(Path.Combine(outDir, "StimulationHUD_BodyLanguage.png"), Screen.width, Screen.height);
		h.Check(File.Exists(Path.Combine(outDir, "StimulationHUD_BodyLanguage.png")), "StimulationHUD rendered");
		foreach (var i in issues2) Console.WriteLine("  hud issue: " + i);
		h.Check(SuperController.Messages.Any(m => m.Contains("BL Stimulation found")), "the Stimulation HUD found BodyLanguage's Stimulation value");
	}
}
