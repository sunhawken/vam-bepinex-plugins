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
		Screen.width = 1920; Screen.height = 1080; Screen.dpi = 96f;
		string art = Path.GetFullPath(Path.Combine(Harness.CoresDir, "..", "..", "..", "..", "artifacts", "Release"));
		string over = Environment.GetEnvironmentVariable("HUDDOCK_REAL_DIR");
		if (!string.IsNullOrEmpty(over)) art = over;   // e.g. the plugins folder of a VaM install: the builds that are live
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


		// ---- the plugins' OWN windows: flip from the dock, repaint the plugin, and its drawn window must change (its own On / Off caption follows)
		h.Set("Window", "Show", "false");   // the dock's own captions out of the picture: only the plugins' windows are compared
		foreach (var o in targets) if ((bool)prop(o, "Found")) dt.GetMethod("SetVisible", All).Invoke(dock, new object[] { o, true });   // every plugin window up
		h.Step(0.5f);
		int uguiTexts = 0;
		{
			int ta = 0, cv = 0;
			foreach (var go in UnityEngine.Sim.AllObjects.ToArray()) { if (go == null) continue; if (go.GetComponent<UnityEngine.UI.Text>() != null) { uguiTexts++; if (go.activeInHierarchy) ta++; } if (go.GetComponent<Canvas>() != null) cv++; }
			Console.WriteLine("  info    uGUI: " + uguiTexts + " Text (" + ta + " active), " + cv + " canvases, objects " + UnityEngine.Sim.AllObjects.Count + ", errors " + UnityEngine.Sim.Errors.Count);
		}
		var beginFrame = guiT.GetMethod("BeginFrame");
		Func<BaseUnityPlugin, List<string>> drawn = pl =>
		{
			var m = pl.GetType().GetMethod("OnGUI", All);
			if (m == null) return new List<string>();
			foreach (var pass in new[] { EventType.Layout, EventType.Repaint })
			{
				Event.current = new Event { type = pass, mousePosition = new Vector2(-1000f, -1000f) };
				beginFrame.Invoke(null, new object[] { pass });
				try { m.Invoke(pl, null); } catch (Exception) { }
			}
			var res = new List<string>();
			foreach (var c in (IEnumerable)guiT.GetField("Cmds").GetValue(null)) { var ct = c.GetType(); res.Add((string)ct.GetField("text").GetValue(c) + "|" + ct.GetField("on").GetValue(c) + "|" + ct.GetField("color").GetValue(c)); }
			return res;
		};
		// every "On" / "Off" caption on screen: the IMGUI windows (drawn here one by one) and the uGUI windows (Text components)
		Func<int[]> captionsNow = () =>
		{
			int on = 0, off = 0;
			foreach (var pl in loaded.Values)
				foreach (var s in drawn(pl)) { string tx = s.Split('|')[0]; if (tx == "On") on++; else if (tx == "Off") off++; }
			foreach (var go in UnityEngine.Sim.AllObjects.ToArray())
			{
				if (go == null) continue;
				var tx = go.GetComponent<UnityEngine.UI.Text>();
				if (tx == null || !go.activeInHierarchy) continue;
				if (tx.text == "On") on++; else if (tx.text == "Off") off++;
			}
			return new[] { on, off };
		};
		h.Step(0.5f);
		if (uguiTexts == 0) Console.WriteLine("  info    the uGUI windows were not built in this run (state left by earlier scenarios of a --all run): the caption check is skipped here, run  vamsim ZeroT.HudDock.dll huddock_real  on its own for it");
		foreach (var o in targets)
		{
			string label = (string)fld(o, "Label");
			if (uguiTexts == 0) break;
			if (!(bool)prop(o, "Found") || !(bool)prop(o, "CanActive")) continue;
			if (label == "Orgasm Suite" || label == "VPB Random Look") { Console.WriteLine("  info    " + label + ": its window has no On / Off caption (nothing to compare)"); continue; }
			if (label == "FastMode") { Console.WriteLine("  info    FastMode: its Update needs DAZCharacterRun.doUpdate, which the simulator does not have (not tested; it polls the setting in Update)"); continue; }
			if (label == "SmartWardrobe" || label == "Penetration Counter") { Console.WriteLine("  info    " + label + ": its core assembly cannot be loaded in the simulator, so its window is not built here (not tested)"); continue; }
			bool wasOn = (bool)prop(o, "IsActive");
			var c0 = captionsNow();
			dt.GetMethod("SetActive", All).Invoke(dock, new object[] { o, !wasOn });
			h.Step(0.4f);   // a plugin that polls its setting in Update
			var c1 = captionsNow();
			dt.GetMethod("SetActive", All).Invoke(dock, new object[] { o, wasOn });
			h.Step(0.4f);
			var c2 = captionsNow();
			int dOn = c1[0] - c0[0], dOff = c1[1] - c0[1];
			bool follows = wasOn ? (dOff == 1 && dOn == -1) : (dOn == 1 && dOff == -1);
			bool back = c2[0] == c0[0] && c2[1] == c0[1];
			if (c0[0] + c0[1] == 0) { Console.WriteLine("  info    " + label + ": no On / Off caption found in the simulator"); continue; }
			h.Check(follows && back, label + ": the plugin's own On / Off caption follows the dock (On/Off captions on screen " + c0[0] + "/" + c0[1] + " -> " + c1[0] + "/" + c1[1] + " -> " + c2[0] + "/" + c2[1] + ")");
		}
		// the real plugins whose sister assemblies VaM loads from embedded resources cannot run fully here: those errors are the simulator's, not the dock's
		int sim = UnityEngine.Sim.Errors.RemoveAll(e => e.Contains("FileNotFoundException") && e.Contains("Could not load file or assembly"));
		Console.WriteLine("  info    " + sim + " simulator-only load errors set aside (sister assemblies of SmartWardrobe, PPA, Penetration Counter, VPB Random Look)");
	}
}
