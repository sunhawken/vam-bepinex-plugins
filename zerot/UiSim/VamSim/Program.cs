using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using UnityEngine;

// vamsim - run headless BepInEx plugins against a fake VaM scene (no VaM, no Unity).
//   vamsim --list                          scenarios
//   vamsim <plugin.dll> [<scenario>]       run one scenario (default: "smoke" = 1 female + 1 male, 10 s)
//   vamsim --all <dir>                     run every scenario whose plugin DLL exists in <dir>
//   --set Section.Key=value                config override before the plugin starts
// Exit code 0 = every check passed and the plugin threw nothing.

[AttributeUsage(AttributeTargets.Method)]
public sealed class ScenarioAttribute : Attribute
{
	public string Name, Plugin;
	public ScenarioAttribute(string name, string plugin) { Name = name; Plugin = plugin; }
}

public sealed class Harness
{
	public SuperController Sc;
	public Assembly Asm;
	public List<BepInEx.BaseUnityPlugin> Plugins = new List<BepInEx.BaseUnityPlugin>();
	public int Checks, Failures;
	public string Work;
	public static string CoresDir;
	// loads a script-assembly built by `vamdev.py simtargets` (the code a Harmony helper patches) into the simulator
	public Assembly LoadTarget(string name)
	{
		string src = Path.Combine(CoresDir, name + ".dll");
		if (!File.Exists(src)) throw new FileNotFoundException("build it first: python zerot/tools/vamdev.py simtargets  (missing " + src + ")");
		return Assembly.LoadFrom(Program.Retarget(src, Work));
	}
	public void Unpatch(string harmonyId) { new HarmonyLib.Harmony(harmonyId).UnpatchAll(harmonyId); }
	public bool LogHas(string text) => BepInEx.Logging.ManualLogSource.Lines.Any(l => l.Contains(text));
	public BepInEx.BaseUnityPlugin Plugin => Plugins.Count > 0 ? Plugins[0] : null;

	public void Start()
	{
		var types = Asm.GetTypes().Where(t => typeof(BepInEx.BaseUnityPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttributes(typeof(BepInEx.BepInPlugin), false).Length > 0);
		foreach (var t in types)
		{
			var go = new GameObject("plugin " + t.Name);
			try { Plugins.Add((BepInEx.BaseUnityPlugin)go.AddComponent(t)); }
			catch (Exception e) { Sim.Error("AddComponent " + t.Name, e.InnerException ?? e); }
		}
		Sim.Step(0.02f);
	}

	public void Step(float seconds, float dt = 0.02f)
	{
		for (float t = 0; t < seconds - 1e-6f; t += dt) Sim.Step(dt);
	}

	public Atom Person(string uid, bool male = false) => SimWorld.NewPerson(Sc, uid, male);

	public void Set(string section, string key, string value)
	{
		var e = Plugin.Config.Find(section, key);
		if (e == null) { Failures++; Checks++; Console.WriteLine("  FAIL config entry not found: " + section + "." + key); return; }
		e.SetFromString(value);
	}

	public void Check(bool ok, string msg)
	{
		Checks++;
		if (!ok) { Failures++; Console.WriteLine("  FAIL " + msg); }
		else Console.WriteLine("  ok   " + msg);
	}

	public void Near(float actual, float expected, string msg, float tol = 1e-3f)
	{
		Check(Math.Abs(actual - expected) <= tol, msg + " (" + actual.ToString("0.####") + " vs " + expected.ToString("0.####") + ")");
	}

	// A scene load as VaM does it: everything is replaced, plugins see isLoading, then the loaded event.
	public void Load(Action buildScene)
	{
		Sc.BeginLoad();
		foreach (var a in Sc.atoms.ToArray()) Sc.RemoveAtom(a);
		Step(0.2f);
		buildScene();
		Sc.EndLoad();
	}
}

internal static class Program
{
	private static int runCounter;
	private static int Main(string[] args)
	{
		var scenarios = typeof(Program).Assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
			.Select(m => new { m, a = m.GetCustomAttribute<ScenarioAttribute>() }).Where(x => x.a != null).ToList();
		if (args.Length == 0 || args[0] == "--list")
		{
			foreach (var s in scenarios) Console.WriteLine(s.a.Name.PadRight(22) + s.a.Plugin);
			return 0;
		}
		var overrides = new Dictionary<string, string>();
		var rest = new List<string>();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--set") { string kv = args[++i]; int eq = kv.IndexOf('='); overrides[kv.Substring(0, eq)] = kv.Substring(eq + 1); }
			else rest.Add(args[i]);
		}
		if (rest[0] == "--linkcheck")
		{
			string lw = Path.Combine(Path.GetTempPath(), "vamsim_link_" + Environment.ProcessId);
			Directory.CreateDirectory(lw);
			File.Copy(typeof(GameObject).Assembly.Location, Path.Combine(lw, "UnityShim.dll"), true);
			return LinkCheck.Run(Path.GetFullPath(rest[1]), lw);
		}
		if (rest[0] == "--scene")
		{
			int vi = rest.IndexOf("--vam");
			string vamDir = vi >= 0 ? rest[vi + 1] : (Environment.GetEnvironmentVariable("VAM_DIR") ?? "T:/New folder");
			return SceneSim.Run(Path.GetFullPath(rest[1]), vamDir);
		}
		int failed = 0;
		if (rest[0] == "--all")
		{
			string dir = Path.GetFullPath(rest[1]);
			foreach (var s in scenarios)
			{
				string dll = Path.Combine(dir, s.a.Plugin + ".dll");
				if (!File.Exists(dll)) { Console.WriteLine("SKIP " + s.a.Name + " (no " + s.a.Plugin + ".dll in " + dir + ")"); continue; }
				failed += Run(dll, s.a.Name, s.m, overrides);
			}
			Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " scenario(s) FAILED");
			return failed == 0 ? 0 : 1;
		}
		string path = Path.GetFullPath(rest[0]);
		string pluginName = Path.GetFileNameWithoutExtension(path);
		string wanted = rest.Count > 1 ? rest[1] : null;
		var pick = scenarios.FirstOrDefault(s => s.a.Name == wanted) ?? (wanted == null ? scenarios.FirstOrDefault(s => s.a.Plugin == pluginName) : null);
		MethodInfo method = pick != null ? pick.m : typeof(Scenarios).GetMethod("Smoke");
		string name = pick != null ? pick.a.Name : "smoke";
		failed = Run(path, name, method, overrides);
		return failed == 0 ? 0 : 1;
	}

	private static int Run(string dll, string name, MethodInfo scenario, Dictionary<string, string> overrides)
	{
		Console.WriteLine("== " + name + "  (" + Path.GetFileName(dll) + ")");
		Sim.Errors.Clear();
		BepInEx.Logging.ManualLogSource.Lines.Clear();
		BepInEx.Configuration.ConfigFile.Overrides.Clear();
		foreach (var kv in overrides) BepInEx.Configuration.ConfigFile.Overrides[kv.Key] = kv.Value;
		string work = Path.Combine(Path.GetTempPath(), "vamsim_work_" + Path.GetFileNameWithoutExtension(dll) + "_" + Environment.ProcessId + "_" + (++runCounter));   // earlier scenarios keep their DLLs loaded (and locked)
		if (Directory.Exists(work)) Directory.Delete(work, true);
		Directory.CreateDirectory(work);
		File.Copy(typeof(GameObject).Assembly.Location, Path.Combine(work, "UnityShim.dll"), true);
		string patched = Retarget(dll, work);
		string deps = Path.GetDirectoryName(dll);
		foreach (var f in Directory.GetFiles(deps, "*.dll"))
			if (!string.Equals(f, dll, StringComparison.OrdinalIgnoreCase)) { try { Retarget(f, work); } catch { } }
		AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
		{
			string p = Path.Combine(work, new AssemblyName(e.Name).Name + ".dll");
			return File.Exists(p) ? Assembly.LoadFrom(p) : null;
		};
		// fresh world
		// objects of the previous scenario are simply dropped (no OnDestroy: their plugins belong to a world that no longer exists)
		Sim.AllObjects.Clear(); Sim.Routines.Clear(); Sim.Lifecycle.Pending.Clear(); Sim.Lifecycle.Started.Clear();
		Sim.Now = 0f; Sim.FixedAccum = 0f;
		var h = new Harness();
		h.Work = work;
		Harness.CoresDir = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "..", "..", "..", "..", "in", "targets");
		h.Asm = Assembly.LoadFrom(patched);
		h.Sc = SimWorld.NewSuperController();
		try { scenario.Invoke(null, new object[] { h }); }
		catch (TargetInvocationException e) { h.Checks++; h.Failures++; Console.WriteLine("  FAIL scenario threw: " + (e.InnerException ?? e)); }
		foreach (var e in Sim.Errors) { h.Checks++; h.Failures++; Console.WriteLine("  FAIL plugin error: " + e); }
		foreach (var l in BepInEx.Logging.ManualLogSource.Lines.Distinct().Take(40)) Console.WriteLine("  log  " + l);
		foreach (var m in SuperController.Messages.Take(6)) Console.WriteLine("  VaM message: " + m);
		foreach (var m in SuperController.Errors.Take(6)) Console.WriteLine("  VaM error: " + m);
		Console.WriteLine(h.Failures == 0 ? "PASS " + name + " (" + h.Checks + " checks)" : "FAIL " + name + " (" + h.Failures + " of " + h.Checks + " checks failed)");
		return h.Failures == 0 ? 0 : 1;
	}

	internal static string Retarget(string path, string work)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(work);
		var mod = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
		AssemblyNameReference shim = null;
		var toRemove = new List<AssemblyNameReference>();
		foreach (var r in mod.AssemblyReferences)
		{
			if (r.Name == "0Harmony" || r.Name == "0Harmony20") { r.Name = "0Harmony"; r.Version = new Version(2, 3, 3, 0); r.PublicKeyToken = null; r.PublicKey = null; continue; }   // real Harmony (Lib.Harmony)
			bool unity = r.Name.StartsWith("UnityEngine") || r.Name == "BepInEx" || r.Name.StartsWith("Assembly-CSharp") || r.Name == "SimpleJSON";
			if (!unity) continue;
			if (shim == null) { r.Name = "UnityShim"; r.Version = new Version(1, 0, 0, 0); r.PublicKeyToken = null; r.PublicKey = null; shim = r; }
			else toRemove.Add(r);
		}
		foreach (var tr in mod.GetTypeReferences())
			if (tr.Scope is AssemblyNameReference an && an != shim && (an.Name.StartsWith("UnityEngine") || an.Name == "BepInEx" || an.Name.StartsWith("Assembly-CSharp") || an.Name == "SimpleJSON") && shim != null)
				tr.Scope = shim;
		foreach (var r in toRemove) mod.AssemblyReferences.Remove(r);
		// the helpers were built against Harmony 2.0 (0Harmony20), whose Patch() returns DynamicMethod; Lib.Harmony returns MethodInfo
		foreach (var mr in mod.GetMemberReferences())
			if (mr is MethodReference m2 && m2.DeclaringType != null && m2.DeclaringType.FullName == "HarmonyLib.Harmony" && m2.Name == "Patch" && m2.ReturnType.FullName == "System.Reflection.Emit.DynamicMethod")
				m2.ReturnType = mod.ImportReference(typeof(System.Reflection.MethodInfo));
		string outp = Path.Combine(work, Path.GetFileName(path));
		mod.Write(outp);
		return outp;
	}
}
