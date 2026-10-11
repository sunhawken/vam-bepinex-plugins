using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UnityEngine;

// Runs the REAL scripts a scene loads (compiled by `vamdev.py scenescripts` against VaM's own DLLs, retargeted onto the shim):
// each plugin dictionary in the scene -> an MVRScript component on the right atom / clothing item -> Init() -> RestoreFromJSON(scene state) -> frames.
public static class SceneHost
{
	public static string ScriptDir;   // UiSim/in/scene
	public static readonly List<string> Report = new List<string>();
	static string work;

	public sealed class Entry { public JsonObject Atom; public string AtomId; public string Clothing; public string Key; public string Package, Path; public JsonObject State; }

	static string S(JsonNode n) { try { return n == null ? null : n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToString(); } catch { return null; } }

	public static List<Entry> Collect(List<JsonObject> atoms)
	{
		var list = new List<Entry>();
		foreach (var a in atoms)
		{
			string id = S(a["id"]);
			var stor = (a["storables"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
			foreach (var s in stor)
			{
				var plugins = s["plugins"] as JsonObject;
				if (plugins == null) continue;
				string sid = S(s["id"]);
				var nested = (s["storables"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
				foreach (var kv in plugins.OrderBy(k => int.Parse(Regex.Match(k.Key, "\\d+").Value)))
				{
					string spec = S(kv.Value); int c = spec.IndexOf(":/");
					if (c < 0) continue;
					var pool = sid == "PluginManager" ? stor : nested;
					var state = pool.FirstOrDefault(x => (S(x["id"]) ?? "").StartsWith(kv.Key + "_"));
					list.Add(new Entry { Atom = a, AtomId = id, Clothing = sid == "PluginManager" ? null : sid, Key = kv.Key, Package = spec.Substring(0, c), Path = spec.Substring(c + 2), State = state });
				}
			}
		}
		return list;
	}

	static string DllName(Entry e) => Regex.Replace(e.Package + "__" + e.Path, "[^A-Za-z0-9_.]", "_") + ".dll";

	public static void Prepare()
	{
		work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vamsim_scene_" + Environment.ProcessId);
		if (Directory.Exists(work)) Directory.Delete(work, true);
		Directory.CreateDirectory(work);
		File.Copy(typeof(GameObject).Assembly.Location, System.IO.Path.Combine(work, "UnityShim.dll"), true);
		AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
		{
			string p = System.IO.Path.Combine(work, new AssemblyName(e.Name).Name + ".dll");
			return File.Exists(p) ? Assembly.LoadFrom(p) : null;
		};
	}

	static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();
	static Assembly Load(Entry e)
	{
		string dll = DllName(e);
		Assembly asm;
		if (loaded.TryGetValue(dll, out asm)) return asm;
		string src = System.IO.Path.Combine(ScriptDir, dll);
		if (!File.Exists(src)) return null;
		asm = Assembly.LoadFrom(Program.Retarget(src, work));
		loaded[dll] = asm;
		return asm;
	}

	public sealed class Result { public Entry E; public MVRScript Script; public string Error; }

	public static List<Result> Start(SuperController sc, List<Entry> entries, Func<string, Atom> atomOf, Func<string, string, GameObject> holderOf)
	{
		var results = new List<Result>();
		foreach (var e in entries)
		{
			var r = new Result { E = e };
			results.Add(r);
			try
			{
				var asm = Load(e);
				if (asm == null) { r.Error = "script not compiled: " + DllName(e) + " (run vamdev.py scenescripts)"; continue; }
				Type[] types; try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); r.Error = "load: " + string.Join("; ", ex.LoaderExceptions.Take(2).Select(x => x.Message)); }
				var type = types.FirstOrDefault(t => typeof(MVRScript).IsAssignableFrom(t) && !t.IsAbstract);
				if (type == null) { r.Error = r.Error ?? "no MVRScript class in " + e.Package; continue; }
				var atom = atomOf(e.AtomId);
				var parent = holderOf(e.AtomId, e.Clothing);
				var go = new GameObject(e.Key + "_" + type.FullName);
				go.transform.SetParent(parent.transform, false);
				var script = (MVRScript)go.AddComponent(type);
				script.containingAtom = atom;
				script.storeId = e.Key + "_" + type.FullName;
				script.manager = parent.GetComponent<MVRPluginManager>() ?? parent.AddComponent<MVRPluginManager>();
				script.manager.pluginSpecs[e.Key] = e.Package + ":/" + e.Path;
				script.enabledJSON = new JSONStorableBool("enabled", true);
				script.pluginLabelJSON = new JSONStorableString("pluginLabel", "");
				script.RegisterBool(script.enabledJSON); script.RegisterString(script.pluginLabelJSON);
				// the script's UI panel as the plugin manager builds it: MVRScriptUI with left/right content rects
				var ui = new GameObject("scriptUI " + script.storeId); ui.transform.SetParent(go.transform, false);
				var msu = ui.AddComponent<MVRScriptUI>();
				var left = new GameObject("left"); left.transform.SetParent(ui.transform, false); msu.leftUIContent = left.AddComponent<RectTransform>();
				var right = new GameObject("right"); right.transform.SetParent(ui.transform, false); msu.rightUIContent = right.AddComponent<RectTransform>();
				script.UITransform = ui.transform;
				foreach (var fn in new[] { "leftUIContent", "rightUIContent" })
				{
					var fld = typeof(MVRScript).GetField(fn);
					if (fld != null) fld.SetValue(script, fn.StartsWith("left") ? msu.leftUIContent : msu.rightUIContent);
				}
				atom.RegisterAdditionalStorable(script);
				r.Script = script;
			}
			catch (Exception ex) { r.Error = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message; }
		}
		// Init everything, then restore the scene state (VaM: plugins init first, the scene JSON is applied afterwards)
		foreach (var r in results.Where(x => x.Script != null))
		{
			try { r.Script.Init(); }
			catch (Exception ex) { r.Error = "Init: " + Where(ex); }
		}
		foreach (var r in results.Where(x => x.Script != null && x.E.State != null))
		{
			try
			{
				var jc = SimpleJSON.JSON.Parse(r.E.State.ToJsonString()) as SimpleJSON.JSONClass;
				r.Script.RestoreFromJSON(jc);
				r.Script.LateRestoreFromJSON(jc);
			}
			catch (Exception ex) { r.Error = (r.Error == null ? "" : r.Error + " | ") + "Restore: " + Where(ex); }
		}
		return results;
	}

	public static string Where(Exception ex)
	{
		var e = ex.InnerException ?? ex;
		var st = new System.Diagnostics.StackTrace(e, true).GetFrames();
		string at = st != null && st.Length > 0 ? " at " + string.Join(" < ", st.Take(3).Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name)) : "";
		return e.GetType().Name + ": " + e.Message + at;
	}
}
