using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

// HUD Dock against the REAL plugin DLLs (the ones in artifacts/Release), not stand-ins: every plugin the dock lists is loaded, registered the way
// BepInEx registers it, and the dock's On / Off switch for it is flipped. The label the dock draws (IsActive) must follow, and the plugin's own
// setting must really change, also after some frames (a plugin that puts it back would show up here).
public static class HudDockRealScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[Scenario("huddock_real", "ZeroT.HudDock")]
	public static void Real(Harness h)
	{
		BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
		string art = Path.GetFullPath(Path.Combine(Harness.CoresDir, "..", "..", "..", "..", "artifacts", "Release"));
		var want = new HashSet<string>
		{
			"com.zerot.sceneperformance.standalone", "bplus.smartwardrobe.standalone", "charlestone.anusphysicscontrol.standalone", "hasno.counterthrust.standalone",
			"helicalpink.penispalignment.standalone", "ky1001.movecontrolstobody.standalone", "com.particlepinnacle.thighcompressorandvibrations", "zerot.particlepinnacle.uiskin",
			"com.14mhz.penetrationcounter.bepinex", "ridgerock.proceduralslaps.standalone", "skynet.orificedynamics.standalone", "zerot.vamfastmode", "zerot.fluidbridge.helper",
			"com.zerot.givemefps.standalone", "zerot.iamjoi.garments.standalone", "zerot.xrayslapstandalone", "com.zerot.deeperplugin.standalone", "zero.vam.detached-ui-panel",
			"com.zerot.orgasmsuite", "com.zerot.vpbrandomlook"
		};
		var loaded = new Dictionary<string, BaseUnityPlugin>();
		var all = Directory.GetFiles(art, "*.dll").Where(d => !Path.GetFileName(d).StartsWith("Mono.") && !Path.GetFileName(d).Contains("HudDock")).ToArray();
		foreach (var dll in all) { try { Program.Retarget(dll, h.Work); } catch (Exception) { } }   // sister assemblies (cores) resolve from the work folder
		foreach (var dll in all)
		{
			if (Path.GetFileName(dll).Contains("HudDock") || Path.GetFileName(dll).StartsWith("Mono.")) continue;
			Type[] types;
			try { types = Assembly.LoadFrom(Path.Combine(h.Work, Path.GetFileName(dll))).GetTypes(); }
			catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
			catch (Exception) { continue; }
			foreach (var t in types)
			{
				if (t.IsAbstract || !typeof(BaseUnityPlugin).IsAssignableFrom(t)) continue;
				var at = t.GetCustomAttributes(typeof(BepInPlugin), false).FirstOrDefault() as BepInPlugin;
				if (at == null || !want.Contains(at.GUID) || loaded.ContainsKey(at.GUID)) continue;
				try
				{
					var p = (BaseUnityPlugin)new GameObject("plugin " + at.GUID).AddComponent(t);
					loaded[at.GUID] = p;
					BepInEx.Bootstrap.Chainloader.PluginInfos[at.GUID] = new BepInEx.PluginInfo { Instance = p };
				}
				catch (Exception e) { Console.WriteLine("  note    " + at.GUID + " did not start in the simulator: " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message); }
			}
		}
		h.Step(3f);
		Console.WriteLine("  info    " + loaded.Count + " of " + want.Count + " real plugins running in the simulator");
		h.Start();
		var dock = h.Plugin; var dt = dock.GetType();
		var targets = (IList)dt.GetField("_targets", All).GetValue(dock);
		Func<object, string, object> prop = (o, n) => o.GetType().GetProperty(n).GetValue(o, null);
		Func<object, string, object> fld = (o, n) => o.GetType().GetField(n).GetValue(o);
		foreach (var o in targets) { o.GetType().GetField("NextResolve").SetValue(o, 0f); dt.GetMethod("Resolve", All).Invoke(dock, new object[] { o }); }
		int bad = 0;
		foreach (var o in targets)
		{
			string label = (string)fld(o, "Label");
			bool found = (bool)prop(o, "Found"), can = (bool)prop(o, "CanActive");
			if (!found) { Console.WriteLine("  info    " + label + ": not loaded in the simulator (skipped)"); continue; }
			var entry = fld(o, "ActiveEntry");
			string how = entry != null ? "own setting" : "plugin component enabled";
			if (!can) { bad++; Console.WriteLine("  FAIL    " + label + ": no On / Off switch at all"); h.Check(false, label + " has an On / Off switch"); continue; }
			bool before = (bool)prop(o, "IsActive");
			dt.GetMethod("SetActive", All).Invoke(dock, new object[] { o, !before });
			bool now = (bool)prop(o, "IsActive");
			h.Step(2f);
			bool later = (bool)prop(o, "IsActive");
			dt.GetMethod("SetActive", All).Invoke(dock, new object[] { o, before });
			h.Step(1f);
			bool back = (bool)prop(o, "IsActive");
			h.Check(now == !before && later == !before && back == before, label + " [" + how + "]: label " + (before ? "On" : "Off") + " -> " + (now ? "On" : "Off") + " -> after 2 s " + (later ? "On" : "Off") + " -> restored " + (back ? "On" : "Off"));
		}

		// ---- the real clicks: press + release on the drawn On / Off buttons through the dock's own OnGUI (hit-testing included)
		h.Step(1f);
		var winF = dt.GetField("_win", All);
		Action<EventType, Vector2> fire = (typ, pos) => { Event.current = new Event { type = typ, mousePosition = pos, button = 0 }; dt.GetMethod("OnGUI", All).Invoke(dock, null); };
		Func<Dictionary<string, bool>> snap = () => targets.Cast<object>().Where(o => (bool)prop(o, "Found") && (bool)prop(o, "CanActive")).ToDictionary(o => (string)fld(o, "Label"), o => (bool)prop(o, "IsActive"));
		var win = (Rect)winF.GetValue(dock);
		var flipped = new HashSet<string>(); Vector2 first = Vector2.zero;
		for (float y = win.y; y < win.yMax; y += 3f)
			for (float x = win.xMax - 120f; x < win.xMax - 2f; x += 3f)
			{
				var before = snap();
				fire(EventType.Repaint, new Vector2(x, y));
				fire(EventType.MouseDown, new Vector2(x, y)); fire(EventType.MouseUp, new Vector2(x, y));
				var after = snap();
				foreach (var kv in before)
					if (after[kv.Key] != kv.Value)
					{
						if (flipped.Count == 0) first = new Vector2(x, y);
						flipped.Add(kv.Key);
						foreach (var o in targets) if ((string)fld(o, "Label") == kv.Key) dt.GetMethod("SetActive", All).Invoke(dock, new object[] { o, kv.Value });
					}
			}
		var expected = snap().Keys.Count;
		Console.WriteLine("  info    clicks flipped: " + string.Join(", ", flipped) + " (first at " + first.x + "," + first.y + ")");
		h.Check(flipped.Count > 0, "a real click on a drawn On / Off button flips a plugin's switch (" + flipped.Count + " of the listed rows were reachable)");

		// ---- what is DRAWN: the "ON" / "OFF" captions of the rows follow the click on the next repaint
		var guiT = typeof(GameObject).Assembly.GetTypes().First(x => x.Name == "Gui" && x.GetField("Cmds") != null);
		Func<Dictionary<string, int>> captions = () =>
		{
			((IList)guiT.GetField("Cmds").GetValue(null)).Clear();
			fire(EventType.Repaint, new Vector2(-1000f, -1000f));
			var cmds = (IEnumerable)guiT.GetField("Cmds").GetValue(null);
			var d = new Dictionary<string, int> { { "ON", 0 }, { "OFF", 0 } };
			foreach (var c in cmds) { string tx = (string)c.GetType().GetField("text").GetValue(c); if (tx == "ON" || tx == "OFF") d[tx]++; }
			return d;
		};
		var cap0 = captions();
		object row = targets.Cast<object>().First(o => (string)fld(o, "Label") == "Anus Physics");
		dt.GetMethod("SetActive", All).Invoke(dock, new object[] { row, false });
		var cap1 = captions();
		dt.GetMethod("SetActive", All).Invoke(dock, new object[] { row, true });
		var cap2 = captions();
		Console.WriteLine("  info    drawn captions: ON/OFF " + cap0["ON"] + "/" + cap0["OFF"] + " -> (Anus Physics off) " + cap1["ON"] + "/" + cap1["OFF"] + " -> (on again) " + cap2["ON"] + "/" + cap2["OFF"]);
		h.Check(cap1["OFF"] == cap0["OFF"] + 1 && cap1["ON"] == cap0["ON"] - 1 && cap2["ON"] == cap0["ON"], "the drawn caption of that row changes ON -> OFF -> ON on the next repaint");

		// the real plugins whose sister assemblies VaM loads from embedded resources cannot run fully here: those errors are the simulator's, not the dock's
		int sim = UnityEngine.Sim.Errors.RemoveAll(e => e.Contains("FileNotFoundException") && e.Contains("Could not load file or assembly"));
		Console.WriteLine("  info    " + sim + " simulator-only load errors set aside (sister assemblies of SmartWardrobe, PPA, Penetration Counter, VPB Random Look)");
	}
}
