using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

// Browser Assist Standalone with a stand-in for JayJayWon's script: the real plugin's behaviour around it - find a BrowserAssist VaM already restored (never a second one),
// otherwise create the session plugin from the newest installed .var, mirror the plugin's own UI into the window and give it back, and the Controls page built from the
// actions the script registers (grouped by name). The stand-in registers the same action names BrowserAssist v1.43 registers (read from its source).
public static class BrowserAssistScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	public sealed class FakeBrowserUi
	{
		public Canvas mainCanvas;
		public bool isActive { get; private set; }
		public float browserScreenWidth { get { return 650f; } }

		public FakeBrowserUi()
		{
			var go = new GameObject("BrowserAssistCanvas");
			mainCanvas = go.AddComponent<Canvas>();
			mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
			var crt = (RectTransform)go.transform;
			crt.sizeDelta = new Vector2(1440f, 810f);
			Panel(go.transform, "Panel", 650f, 0f);
			Panel(go.transform, "SideTags", 150f, -655f);
			SetInactive();
		}

		private static void Panel(Transform parent, string name, float w, float x)
		{
			var g = new GameObject(name); g.transform.SetParent(parent, false);
			var rt = g.AddComponent<RectTransform>();
			rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
			rt.sizeDelta = new Vector2(w, 0f); rt.anchoredPosition = new Vector2(x, 0f);
			g.AddComponent<Image>().color = new Color(0.5f, 0.5f, 0.55f, 0.95f);
		}

		public void ToggleActive() { if (isActive) SetInactive(); else SetActive(); }
		public void SetActive() { mainCanvas.enabled = true; isActive = true; }
		public void SetInactive() { mainCanvas.enabled = false; isActive = false; }
	}

	public sealed class FakeBrowserAssist : MVRScript
	{
		public static FakeBrowserUi mainBrowserUI { get; set; }
		public static readonly List<string> Ran = new List<string>();
	}

	private static readonly string[] Actions =
	{
		"Open UI", "Close UI", "Toggle UI Open/Close", "Open UI: Scenes", "Open UI: Audio", "Open UI: Clothing (Female)", "Toggle 'Only Favorites' filter", "Toggle 'Show Hidden' filter",
		"First Results Page", "Next Results Page", "Next Selection", "Increase Thumbnail Size", "Select Tags Tab", "Select Info Tab", "Load Random Resource", "Toggle Lock Import Target", "ToggleLocalFolderFilter"
	};

	private static GameObject BuildSession(out MVRPluginManager mgr, out Transform pluginContainer)
	{
		var atoms = new GameObject("SceneAtoms"); var core = new GameObject("CoreControl"); core.transform.SetParent(atoms.transform, false);
		var cont = new GameObject("SessionPluginManagerContainer"); cont.transform.SetParent(core.transform, false);
		var mgo = new GameObject("PluginManager"); mgo.transform.SetParent(cont.transform, false);
		mgr = mgo.AddComponent<MVRPluginManager>();
		var pc = new GameObject("Plugins"); pc.transform.SetParent(mgo.transform, false);
		mgr.pluginContainer = pc.transform;
		pluginContainer = pc.transform;
		return cont;
	}

	private static MVRScript SpawnFake(MVRPluginManager mgr, Transform pluginContainer, string uid, bool withUi = true)
	{
		var go = new GameObject(uid + "_JayJayWon.BrowserAssist"); go.transform.SetParent(pluginContainer, false);
		var s = go.AddComponent<FakeBrowserAssist>();
		s.containingAtom = null;
		var uiGo = new GameObject("PluginUI"); var ui = uiGo.AddComponent<RectTransform>(); ui.SetParent(go.transform, false);
		ui.sizeDelta = new Vector2(700f, 900f);
		if (withUi) s.UITransform = ui;
		FakeBrowserAssist.mainBrowserUI = new FakeBrowserUi();
		foreach (var a in Actions) { var name = a; s.RegisterAction(new JSONStorableAction(name, () => FakeBrowserAssist.Ran.Add(name))); }
		s.RegisterAction(new JSONStorableAction("Toggle UI Open/Close", () => { FakeBrowserAssist.mainBrowserUI.ToggleActive(); FakeBrowserAssist.Ran.Add("Toggle UI Open/Close"); }));
		return s;
	}

	private static string StatusText(Harness h) => ((Text)h.Plugin.GetType().GetField("status", All).GetValue(h.Plugin)).text;
	private static int BaCount(Transform pc) { int n = 0; for (int i = 0; i < pc.childCount; i++) if (pc.GetChild(i).name.Contains("JayJayWon.BrowserAssist")) n++; return n; }

	private static void Packages(string root, params string[] vars)
	{
		if (Directory.Exists(root)) Directory.Delete(root, true);
		Directory.CreateDirectory(Path.Combine(root, "AddonPackages", "resources"));
		foreach (var v in vars) File.WriteAllBytes(Path.Combine(root, "AddonPackages", "resources", v), new byte[0]);
		Paths.GameRootPath = root;
	}

	[Scenario("browserassist", "ZeroT.BrowserAssistStandalone")]
	public static void Standalone(Harness h)
	{
		if (UnityEngine.EventSystems.EventSystem.current == null) UnityEngine.EventSystems.EventSystem.current = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
		string root = Path.Combine(Path.GetTempPath(), "vamsim_browserassist_" + Environment.ProcessId);
		Packages(root, "JayJayWon.BrowserAssist.94.var", "JayJayWon.BrowserAssist.96.var", "JayJayWon.UIAssist(Patron).100.var");
		MVRPluginManager mgr; Transform pc;
		BuildSession(out mgr, out pc);
		string created = null;
		MVRPluginManager.SimLoader = (m, plugin, url) => { created = url; return SpawnFake(m, pc, plugin.uid).gameObject; };
		BepInEx.Configuration.ConfigFile.Overrides["General.StartupDelaySeconds"] = "2";
		h.Start();
		h.Step(1f);
		h.Check(created == null, "before the start-up delay nothing is created");
		h.Step(8f);
		h.Check(created == "JayJayWon.BrowserAssist.96:/Custom/Scripts/JayJayWon/BrowserAssist/BrowserAssist.cslist", "the session plugin is created by itself from the newest installed .var (" + created + ")");
		h.Check(BaCount(pc) == 1, "...exactly one");
		h.Check(StatusText(h).Contains("loaded by this plugin"), "the status says it was loaded here (" + StatusText(h) + ")");
		h.Step(3f);
		var ui = FakeBrowserAssist.mainBrowserUI;
		var host = (RectTransform)h.Plugin.GetType().GetField("browserHost", All).GetValue(h.Plugin);
		var crt = (RectTransform)ui.mainCanvas.transform;
		h.Check(crt.parent == host, "BrowserAssist's own browser canvas was moved into the window");
		h.Check(ui.isActive && ui.mainCanvas.enabled, "...and opened");
		var winObj = h.Plugin.GetType().GetField("win", All).GetValue(h.Plugin);
		float wW = (float)winObj.GetType().GetField("W", All).GetValue(winObj), wH = (float)winObj.GetType().GetField("H", All).GetValue(winObj);
		h.Check(Math.Abs(crt.sizeDelta.x - 810f) < 0.5f && crt.sizeDelta.y >= 800f, "laid out in its own units (650 panel + 160 tag column x the canvas height it had) (" + crt.sizeDelta + ")");
		h.Check(crt.localScale.x > 0.5f && crt.localScale.x <= 1.001f && wW >= 820f, "shown as large as the screen allows (1:1 when it fits), window sized to it (" + wW + " x " + wH + ", scale " + crt.localScale.x + ")");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "BrowserAssist_browser.png"), Screen.width, Screen.height);
		winObj.GetType().GetField("W", All).SetValue(winObj, 415f); winObj.GetType().GetField("H", All).SetValue(winObj, 495f);
		winObj.GetType().GetMethod("Apply", All).Invoke(winObj, null);
		h.Step(0.3f);
		h.Check(Math.Abs(crt.localScale.x - crt.localScale.y) < 1e-4f && crt.localScale.x < 0.6f && crt.localScale.x > 0.3f, "a smaller window scales the browser as a whole (" + crt.localScale.x + ")");
		winObj.GetType().GetField("W", All).SetValue(winObj, wW); winObj.GetType().GetField("H", All).SetValue(winObj, wH);
		winObj.GetType().GetMethod("Apply", All).Invoke(winObj, null);
		h.Plugin.GetType().GetMethod("ToggleBrowser", All).Invoke(h.Plugin, null);
		h.Check(!ui.isActive, "the window's toggle button closes the browser");
		h.Plugin.GetType().GetMethod("ToggleBrowser", All).Invoke(h.Plugin, null);
		// Settings page: BrowserAssist's own settings UI; the browser goes back to VaM
		h.Plugin.GetType().GetMethod("SetMode", All).Invoke(h.Plugin, new object[] { 1 });
		h.Check(crt.parent == null, "leaving the Browser page hands the canvas back to VaM");
		var native = ((Component)h.Plugin.GetType().GetField("nativeHost", All).GetValue(h.Plugin)).transform;
		h.Check(native.childCount == 1 && native.GetChild(0).name == "PluginUI", "Settings page shows BrowserAssist's own settings UI");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "BrowserAssist_interface.png"), Screen.width, Screen.height);
		h.Plugin.GetType().GetMethod("SetMode", All).Invoke(h.Plugin, new object[] { 2 });
		h.Check(crt.parent == host, "back on the Browser page it is moved in again");

		// Controls: the actions the script registers, grouped by name
		h.Plugin.GetType().GetMethod("SetMode", All).Invoke(h.Plugin, new object[] { 0 });
		var totals = (int[])h.Plugin.GetType().GetField("catTotals", All).GetValue(h.Plugin);
		h.Check(totals.SequenceEqual(new[] { 3, 3, 2, 4, 2, 3 }), "group sizes Browser / Types / Filters / Browse / Tabs / Random = 3/3/2/4/2/3 (" + string.Join("/", totals) + ")");
		var cat = h.Plugin.GetType().GetField("cat", All);
		var runBtn = (Button[])h.Plugin.GetType().GetField("actButtons", All).GetValue(h.Plugin);
		Func<int, string[]> labels = c => { cat.SetValue(h.Plugin, c); h.Plugin.GetType().GetMethod("RefreshControls", All).Invoke(h.Plugin, null); return runBtn.Where(b => b.gameObject.activeSelf).Select(b => b.GetComponentInChildren<Text>().text).ToArray(); };
		h.Check(labels(1).SequenceEqual(new[] { "Scenes", "Audio", "Clothing (Female)" }), "Types buttons are labelled without the 'Open UI:' prefix (" + string.Join(", ", labels(1)) + ")");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "BrowserAssist_controls_types.png"), Screen.width, Screen.height);
		labels(3);
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "BrowserAssist_controls_browse.png"), Screen.width, Screen.height);
		labels(0);
		runBtn[2].onClick.Invoke();   // Toggle UI Open/Close
		h.Check(FakeBrowserAssist.Ran.Contains("Toggle UI Open/Close"), "a Controls button runs BrowserAssist's own action (Toggle UI Open/Close opened the browser)");
		h.Step(1.2f);
		labels(1); runBtn[0].onClick.Invoke();
		h.Check(FakeBrowserAssist.Ran.Contains("Open UI: Scenes"), "Types -> Scenes runs 'Open UI: Scenes'");
		labels(5);
		h.Check(runBtn.Count(b => b.gameObject.activeSelf) == 3, "Random group: Load Random Resource, Toggle Lock Import Target, ToggleLocalFolderFilter");

		// Settings page / window Off
		h.Plugin.GetType().GetMethod("SetMode", All).Invoke(h.Plugin, new object[] { 1 });
		var vp = (RectTransform)h.Plugin.GetType().GetField("viewport", All).GetValue(h.Plugin);
		h.Check(vp.gameObject.activeSelf && !runBtn[0].gameObject.activeSelf, "Settings shows BrowserAssist's own UI, Controls hides");
		h.Plugin.GetType().GetMethod("SetMode", All).Invoke(h.Plugin, new object[] { 2 });
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "BrowserAssist_settings.png"), Screen.width, Screen.height);
		h.Set("General", "Active", "false");
		h.Step(1.2f);
		h.Check(crt.parent == null, "switched Off: the browser canvas is handed back to VaM");
		var uiBack = pc.GetChild(0).Find("PluginUI");
		h.Check(uiBack != null, "switched Off: BrowserAssist's UI is handed back to VaM where it was");
		h.Check(BaCount(pc) == 1, "...and BrowserAssist keeps running (Off is only the window)");
		h.Set("General", "UnloadWhenSwitchedOff", "true");
		h.Set("General", "Active", "true"); h.Step(0.3f); h.Set("General", "Active", "false"); h.Step(1.2f);
		h.Check(BaCount(pc) == 0, "with UnloadWhenSwitchedOff on, Off also removes the session plugin");
		h.Check(!Sim.Errors.Any(e => e.Contains("BrowserAssist")), "the plugin threw nothing");
	}

	[Scenario("browserassist_existing", "ZeroT.BrowserAssistStandalone")]
	public static void Existing(Harness h)
	{
		if (UnityEngine.EventSystems.EventSystem.current == null) UnityEngine.EventSystems.EventSystem.current = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
		string root = Path.Combine(Path.GetTempPath(), "vamsim_browserassist2_" + Environment.ProcessId);
		Packages(root, "JayJayWon.BrowserAssist.96.var");
		MVRPluginManager mgr; Transform pc;
		BuildSession(out mgr, out pc);
		int created = 0;
		MVRPluginManager.SimLoader = (m, plugin, url) => { created++; return SpawnFake(m, pc, plugin.uid).gameObject; };
		SpawnFake(mgr, pc, "plugin#0");   // VaM restored its saved session plugins: BrowserAssist is already running
		BepInEx.Configuration.ConfigFile.Overrides["General.StartupDelaySeconds"] = "1";
		h.Start();
		h.Step(10f);
		h.Check(created == 0 && BaCount(pc) == 1, "a BrowserAssist VaM already loaded is used, no second one is created (created " + created + ")");
		h.Check(StatusText(h).Contains("already loaded by VaM"), "the status says so (" + StatusText(h) + ")");
		h.Set("General", "Active", "false"); h.Step(1.2f);
		h.Check(BaCount(pc) == 1, "switching the window Off leaves a BrowserAssist that VaM loaded alone");
		h.Check(!Sim.Errors.Any(e => e.Contains("BrowserAssist")), "the plugin threw nothing");
	}

	[Scenario("browserassist_missing", "ZeroT.BrowserAssistStandalone")]
	public static void NotInstalled(Harness h)
	{
		if (UnityEngine.EventSystems.EventSystem.current == null) UnityEngine.EventSystems.EventSystem.current = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
		string root = Path.Combine(Path.GetTempPath(), "vamsim_browserassist3_" + Environment.ProcessId);
		Packages(root, "Someone.Else.1.var");
		MVRPluginManager mgr; Transform pc;
		BuildSession(out mgr, out pc);
		int created = 0;
		MVRPluginManager.SimLoader = (m, plugin, url) => { created++; return SpawnFake(m, pc, plugin.uid).gameObject; };
		BepInEx.Configuration.ConfigFile.Overrides["General.StartupDelaySeconds"] = "1";
		h.Start();
		h.Step(8f);
		h.Check(created == 0 && StatusText(h).Contains("not installed"), "no BrowserAssist .var installed: nothing is created and the status says so (" + StatusText(h) + ")");
		h.Set("General", "AutoLoadSessionPlugin", "false");
		Packages(root, "JayJayWon.BrowserAssist.96.var");
		h.Step(5f);
		h.Check(created == 0, "AutoLoadSessionPlugin=false: never created");
		h.Check(!Sim.Errors.Any(e => e.Contains("BrowserAssist")), "the plugin threw nothing");
	}
}
