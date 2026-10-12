using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

// HUD Dock against stand-in plugins registered the way BepInEx registers them: it must find each one by GUID, flip its own config entry (or a private
// flag), collapse it, hide all and bring back exactly the ones that were showing, and list a plugin that is not loaded as such.
public sealed class FakeWindowPlugin : BaseUnityPlugin { public bool guiVisible; }

public static class HudDockScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static FakeWindowPlugin Fake(string guid, string showSection, string showKey, string collapseSection, string collapseKey, bool show, bool collapsed)
	{
		var go = new GameObject(guid);
		var p = go.AddComponent<FakeWindowPlugin>();
		if (showSection != null) p.Config.Bind(showSection, showKey, show);
		if (collapseSection != null) p.Config.Bind(collapseSection, collapseKey, collapsed);
		BepInEx.Bootstrap.Chainloader.PluginInfos[guid] = new BepInEx.PluginInfo { Instance = p };
		return p;
	}

	[Scenario("huddock", "ZeroT.HudDock")]
	public static void Dock(Harness h)
	{
		BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
		var fast = Fake("zerot.vamfastmode", "Window", "Show", "Window", "Collapsed", true, false);
		var anus = Fake("charlestone.anusphysicscontrol.standalone", "Window", "Visible", "Window", "Collapsed", true, true);
		var pc = Fake("com.14mhz.penetrationcounter.bepinex", "Desktop GUI", "ShowOnStart", "Desktop GUI", "Collapsed", true, false);
		pc.guiVisible = true;
		var slaps = Fake("ridgerock.proceduralslaps.standalone", "Window", "Show", "Window", "Collapsed", false, false);   // already hidden by the user
		h.Start();
		var dock = h.Plugin;
		var dt = dock.GetType();
		var targets = (IList)dt.GetField("_targets", All).GetValue(dock);
		Func<string, object> find = label => targets.Cast<object>().First(o => (string)o.GetType().GetField("Label").GetValue(o) == label);
		Func<object, string, object> prop = (o, n) => o.GetType().GetProperty(n).GetValue(o, null);
		Action resolve = () =>
		{
			foreach (var o in targets) { o.GetType().GetField("NextResolve").SetValue(o, 0f); dt.GetMethod("Resolve", All).Invoke(dock, new object[] { o }); }
		};
		Func<string, bool> visible = l => (bool)prop(find(l), "Visible");
		Func<string, bool> found = l => (bool)prop(find(l), "Found");
		Func<string, bool> collapsed = l => (bool)prop(find(l), "Collapsed");
		Action<string, bool> setVis = (l, v) => dt.GetMethod("SetVisible", All).Invoke(dock, new object[] { find(l), v });
		Action<string, bool> setCol = (l, v) => dt.GetMethod("SetCollapsed", All).Invoke(dock, new object[] { find(l), v });

		resolve();
		h.Check(targets.Count >= 14, "the list carries the HUDs (" + targets.Count + ")");
		h.Check(found("FastMode") && found("Anus Physics") && found("Penetration Counter") && found("Procedural Slaps"), "plugins that are loaded are found by their GUID");
		h.Check(!found("GiveMeFPS") && !found("Orifice Dynamics"), "a plugin that is not loaded is listed as such");
		h.Check(visible("FastMode") && visible("Anus Physics") && visible("Penetration Counter") && !visible("Procedural Slaps"), "visibility is read from each plugin's own setting / flag");

		setVis("FastMode", false);
		h.Check(!fast.Config.Find("Window", "Show").GetType().GetProperty("Value").GetValue(fast.Config.Find("Window", "Show"), null).Equals(true), "FastMode: its own Window.Show entry was switched off");
		setVis("Anus Physics", false);
		h.Check(!visible("Anus Physics"), "Anus Physics: Window.Visible switched off");
		setVis("Penetration Counter", false);
		h.Check(!pc.guiVisible && !visible("Penetration Counter"), "Penetration Counter: its private flag was switched off");
		var showOnStart = (ConfigEntry<bool>)pc.Config.Find("Desktop GUI", "ShowOnStart");
		h.Check(!showOnStart.Value, "...and ShowOnStart follows, so the next start matches");
		setVis("FastMode", true); setVis("Anus Physics", true); setVis("Penetration Counter", true);
		h.Check(visible("FastMode") && visible("Anus Physics") && pc.guiVisible && showOnStart.Value, "switched back on again");

		h.Check(!collapsed("FastMode") && collapsed("Anus Physics"), "collapsed state is read from the plugin");
		setCol("FastMode", true); setCol("Anus Physics", false);
		h.Check(collapsed("FastMode") && !collapsed("Anus Physics"), "collapse / expand flips the plugin's Collapsed entry");

		// hide all, then bring back exactly what was showing (Procedural Slaps was hidden before and must stay hidden)
		dt.GetMethod("HideAll", All).Invoke(dock, null);
		h.Check(!visible("FastMode") && !visible("Anus Physics") && !visible("Penetration Counter") && !pc.guiVisible && !visible("Procedural Slaps"), "Hide all: every HUD is off");
		h.Check((bool)dt.GetField("_hidden", All).GetValue(dock), "Hide all is remembered as active");
		dt.GetMethod("ShowAgain", All).Invoke(dock, null);
		h.Check(visible("FastMode") && visible("Anus Physics") && visible("Penetration Counter") && pc.guiVisible, "Show again: the ones that were showing are back");
		h.Check(!visible("Procedural Slaps"), "...and the one that was hidden before stays hidden");
		h.Check(!(bool)dt.GetField("_hidden", All).GetValue(dock), "Hide all is no longer active");

		dt.GetMethod("AllOn", All).Invoke(dock, null);
		h.Check(visible("Procedural Slaps"), "All on switches every loaded HUD on");

		// Detached UI Panel (its window flag is Window.Show, like most)
		var dup = Fake("zero.vam.detached-ui-panel", "Window", "Show", "Window", "Collapsed", true, true);
		resolve();
		h.Check(found("Detached UI Panel") && visible("Detached UI Panel") && collapsed("Detached UI Panel"), "Detached UI Panel is listed and read");
		setVis("Detached UI Panel", false);
		h.Check(!visible("Detached UI Panel"), "Detached UI Panel can be hidden");
		setVis("Detached UI Panel", true);

		// a plugin that loads later is picked up
		var late = Fake("zerot.fluidbridge.helper", "Window", "Show", "Window", "Collapsed", true, false);
		resolve();
		h.Check(found("FluidBridge Helper") && visible("FluidBridge Helper"), "a plugin that loads after the dock is found later");
	}
}
