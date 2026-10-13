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

	private static FakeWindowPlugin Fake(string guid, string showSection, string showKey, string collapseSection, string collapseKey, bool show, bool collapsed, string activeKey = "Active", bool active = true)
	{
		var go = new GameObject(guid);
		var p = go.AddComponent<FakeWindowPlugin>();
		if (showSection != null) p.Config.Bind(showSection, showKey, show);
		if (collapseSection != null) p.Config.Bind(collapseSection, collapseKey, collapsed);
		if (activeKey != null) p.Config.Bind("General", activeKey, active);
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

		// XRay + Slap and Deeper Plugin (the shared in-game window: Window.Show / Window.Collapsed)
		Fake("zerot.xrayslapstandalone", "Window", "Show", "Window", "Collapsed", true, true);
		Fake("com.zerot.deeperplugin.standalone", "Window", "Show", "Window", "Collapsed", true, false);
		resolve();
		h.Check(found("XRay + Slap") && found("Deeper Plugin") && collapsed("XRay + Slap") && !collapsed("Deeper Plugin"), "XRay + Slap and Deeper Plugin are listed and read");
		setVis("XRay + Slap", false); setVis("Deeper Plugin", false);
		h.Check(!visible("XRay + Slap") && !visible("Deeper Plugin"), "both can be hidden");
		setVis("XRay + Slap", true); setVis("Deeper Plugin", true);

		// the plugin's own on / off: General.Active, General.Enabled, another plugin's switch, or the component itself
		var ctp = Fake("hasno.counterthrust.standalone", "GUI", "VisibleLowImpact", "GUI", "Collapsed", true, false, "Enabled", true);
		var thigh = Fake("com.particlepinnacle.thighcompressorandvibrations", "Touch Filters", "ShowControlWindow", "Touch Filters", "ControlWindowCollapsed", true, false, null);
		var skin = Fake("zerot.particlepinnacle.uiskin", null, null, null, null, false, false, "Active", true);
		var vpb = Fake("com.zerot.vpbrandomlook", "Window", "Show", "Window", "Collapsed", true, false, null);
		resolve();
		Func<string, bool> canActive = l => (bool)prop(find(l), "CanActive");
		Func<string, bool> isActive = l => (bool)prop(find(l), "IsActive");
		Action<string, bool> setAct = (l, v) => dt.GetMethod("SetActive", All).Invoke(dock, new object[] { find(l), v });
		Func<BaseUnityPlugin, string, bool> entry = (pl, k) => ((ConfigEntry<bool>)pl.Config.Find("General", k)).Value;
		h.Check(canActive("FastMode") && isActive("FastMode"), "FastMode: its General.Active switch is found (on)");
		setAct("FastMode", false);
		h.Check(!entry(fast, "Active") && !isActive("FastMode") && fast.enabled, "...switching it off sets General.Active and leaves the component alone");
		setAct("FastMode", true);
		h.Check(entry(fast, "Active") && isActive("FastMode"), "...and on again");
		setAct("CounterThrust", false);
		h.Check(!entry(ctp, "Enabled") && !isActive("CounterThrust"), "CounterThrust: General.Enabled is its switch");
		setAct("CounterThrust", true);
		setAct("Thigh Compressor", false);
		h.Check(!entry(skin, "Active") && !isActive("Thigh Compressor") && thigh.enabled, "Thigh Compressor: its on / off lives in the ParticlePinnacle UI Skin plugin");
		setAct("Thigh Compressor", true);
		h.Check(canActive("VPB Random Look") && isActive("VPB Random Look"), "a plugin without a switch can still be turned on / off");
		setAct("VPB Random Look", false);
		h.Check(!vpb.enabled && !isActive("VPB Random Look"), "...as a component (its Update / OnGUI stop)");
		setAct("VPB Random Look", true);
		h.Check(vpb.enabled, "...and back");
		((ConfigEntry<bool>)BepInEx.Bootstrap.Chainloader.PluginInfos["zerot.xrayslapstandalone"].Instance.Config.Find("General", "Active")).Value = false;   // switched off in the plugin's own config
		h.Check(!isActive("XRay + Slap"), "a plugin that is switched off in its own config shows as off (read live)");

		// a plugin that loads later is picked up
		var late = Fake("zerot.fluidbridge.helper", "Window", "Show", "Window", "Collapsed", true, false);
		resolve();
		h.Check(found("FluidBridge Helper") && visible("FluidBridge Helper"), "a plugin that loads after the dock is found later");
	}
}
