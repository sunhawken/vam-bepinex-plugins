using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using UnityEngine;

// uisim <plugin.dll> [--w 1600] [--h 900] [--frames 6] [--out dir] [--set Section.Key=value]... [--dpi 96] [--deps dir]
// Loads a BepInEx plugin DLL against UnityShim (no Unity, no VaM), runs a few frames and renders every window it draws.
internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length < 1) { Console.Error.WriteLine("usage: uisim <plugin.dll> [--w N] [--h N] [--frames N] [--out dir] [--set Section.Key=value]..."); return 2; }
		string dll = Path.GetFullPath(args[0]);
		int w = 1600, h = 900, frames = 6; string outDir = Path.Combine(AppContext.BaseDirectory, "out"); string deps = Path.GetDirectoryName(dll);
		float dpi = 96f; string tag = null;
		for (int i = 1; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--w": w = int.Parse(args[++i]); break;
				case "--h": h = int.Parse(args[++i]); break;
				case "--frames": frames = int.Parse(args[++i]); break;
				case "--out": outDir = Path.GetFullPath(args[++i]); break;
				case "--deps": deps = Path.GetFullPath(args[++i]); break;
				case "--dpi": dpi = float.Parse(args[++i]); break;
				case "--tag": tag = args[++i]; break;
				case "--set":
					{
						string kv = args[++i]; int eq = kv.IndexOf('=');
						BepInEx.Configuration.ConfigFile.Overrides[kv.Substring(0, eq)] = kv.Substring(eq + 1);
						break;
					}
			}
		}
		Directory.CreateDirectory(outDir);
		string work = Path.Combine(Path.GetTempPath(), "uisim_work_" + Path.GetFileNameWithoutExtension(dll));
		if (Directory.Exists(work)) Directory.Delete(work, true);
		Directory.CreateDirectory(work);
		string shimSrc = typeof(GameObject).Assembly.Location;
		File.Copy(shimSrc, Path.Combine(work, "UnityShim.dll"), true);
		string patched = Retarget(dll, work);
		// sibling plugin DLLs (core assemblies the host references)
		var depFiles = Directory.GetFiles(deps, "*.dll").ToList();
		string coresDir = Path.Combine(deps, "cores");
		if (Directory.Exists(coresDir)) depFiles.AddRange(Directory.GetFiles(coresDir, "*.dll"));
		foreach (var f in depFiles)
		{
			string n = Path.GetFileName(f);
			if (string.Equals(f, dll, StringComparison.OrdinalIgnoreCase)) continue;
			try { Retarget(f, work); } catch { }
		}
		AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
		{
			string p = Path.Combine(work, new AssemblyName(e.Name).Name + ".dll");
			return File.Exists(p) ? Assembly.LoadFrom(p) : null;
		};

		Screen.width = w; Screen.height = h; Screen.dpi = dpi;
		Assembly asm = Assembly.LoadFrom(patched);
		Type[] types;
		try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); foreach (var le in ex.LoaderExceptions.Take(3)) Console.WriteLine("load: " + le.Message); }
		var plugins = types.Where(t => typeof(BepInEx.BaseUnityPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttributes(typeof(BepInEx.BepInPlugin), false).Length > 0).ToList();
		Console.WriteLine("plugin types: " + (plugins.Count == 0 ? "none" : string.Join(", ", plugins.Select(p => p.Name))));
		// the uGUI hosts wait for SuperController.singleton and EventSystem.current before building their window
		var shimAsm = typeof(GameObject).Assembly;
		var scType = shimAsm.GetType("SuperController");
		if (scType != null)
		{
			var scGo = new GameObject("SuperController");
			try { var sc = scGo.AddComponent(scType); scType.GetProperty("singleton")?.SetValue(null, sc); } catch (Exception e) { Sim.Error("SuperController", e); }
		}
		var esType = shimAsm.GetType("UnityEngine.EventSystems.EventSystem");
		if (esType != null) { try { var es = Activator.CreateInstance(esType); esType.GetProperty("current")?.SetValue(null, es); } catch (Exception e) { Sim.Error("EventSystem", e); } }
		foreach (var t in plugins)
		{
			var go = new GameObject("plugin");
			try { go.AddComponent(t); } catch (Exception e) { Sim.Error("AddComponent " + t.Name, e.InnerException ?? e); }
		}

		var onGui = new List<(MonoBehaviour, MethodInfo)>();
		for (int f = 0; f < frames; f++)
		{
			Sim.Step(0.25f);
			onGui.Clear();
			foreach (var go in Sim.AllObjects.ToArray())
				foreach (var c in go.GetType().GetField("comps", BindingFlags.NonPublic | BindingFlags.Instance) != null ? (List<Component>)go.GetType().GetField("comps", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(go) : new List<Component>())
					if (c is MonoBehaviour m && m.isActiveAndEnabled)
					{
						var mi = m.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (mi != null) onGui.Add((m, mi));
					}
			foreach (var pass in new[] { EventType.Layout, EventType.Repaint })
			{
				GuiBegin(pass);
				foreach (var (m, mi) in onGui)
				{
					try { mi.Invoke(m, null); }
					catch (TargetInvocationException ex) { Sim.Error(m.GetType().Name + ".OnGUI", ex.InnerException); }
				}
			}
		}

		string name = Path.GetFileNameWithoutExtension(dll) + (tag != null ? "_" + tag : "");
		var bmp = new Bitmap(w, h);
		GuiRender(bmp);
		// crop to the drawn area so the PNG is readable
		var cmds = GuiCmds();
		string png = Path.Combine(outDir, name + ".png");
		{
			var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			var buf = new byte[data.Stride * h];
			System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
			bmp.UnlockBits(data);
			int x0 = w, y0 = h, x1 = -1, y1 = -1;
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					int i = y * data.Stride + x * 4;
					if (buf[i] != 58 || buf[i + 1] != 50 || buf[i + 2] != 44) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
				}
			if (x1 >= 0)
			{
				var crop = Rectangle.Intersect(new Rectangle(0, 0, w, h), new Rectangle(x0 - 16, y0 - 16, x1 - x0 + 33, y1 - y0 + 33));
				using (var c = bmp.Clone(crop, bmp.PixelFormat)) c.Save(png);
			}
			else bmp.Save(png);
		}
		Console.WriteLine("image: " + png + "  (" + cmds.Count + " draw commands)");
		var issues = GuiIssues();
		var seen = new HashSet<string>();
		int shown = 0;
		foreach (var iss in issues) { string s = iss.ToString(); if (seen.Add(s)) { Console.WriteLine("CLIP  " + s); shown++; } }
		if (shown == 0) Console.WriteLine("no clipped text detected");
		foreach (var e in Sim.Errors) Console.WriteLine("ERR   " + e);
		foreach (var l in BepInEx.Logging.ManualLogSource.Lines.Take(15)) Console.WriteLine("LOG   " + l);
		return issues.Count == 0 ? 0 : 1;
	}

	// UnityShim.Gui is internal; reach it by reflection
	private static Type GuiType => typeof(GameObject).Assembly.GetType("UnityEngine.Gui");
	private static void GuiBegin(EventType t) => GuiType.GetMethod("BeginFrame").Invoke(null, new object[] { t });
	private static void GuiRender(Bitmap b) => GuiType.GetMethod("Render").Invoke(null, new object[] { b });
	private static List<DrawCmd> GuiCmds() => (List<DrawCmd>)GuiType.GetField("Cmds").GetValue(null);
	private static List<Issue> GuiIssues() => (List<Issue>)GuiType.GetField("Issues").GetValue(null);

	private static string Retarget(string path, string work)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(work);
		var mod = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
		AssemblyNameReference shim = null;
		var toRemove = new List<AssemblyNameReference>();
		foreach (var r in mod.AssemblyReferences)
		{
			bool unity = r.Name.StartsWith("UnityEngine") || r.Name == "BepInEx" || r.Name == "Assembly-CSharp" || r.Name == "0Harmony";
			if (!unity) continue;
			if (shim == null) { r.Name = "UnityShim"; r.Version = new Version(1, 0, 0, 0); r.PublicKeyToken = null; r.PublicKey = null; shim = r; }
			else toRemove.Add(r);
		}
		// repoint type references of the removed refs at the surviving shim reference
		foreach (var tr in mod.GetTypeReferences())
			if (tr.Scope is AssemblyNameReference an && an != shim && (an.Name.StartsWith("UnityEngine") || an.Name == "BepInEx" || an.Name == "Assembly-CSharp" || an.Name == "0Harmony") && shim != null)
				tr.Scope = shim;
		foreach (var r in toRemove) mod.AssemblyReferences.Remove(r);
		string outp = Path.Combine(work, Path.GetFileName(path));
		mod.Write(outp);
		return outp;
	}
}
