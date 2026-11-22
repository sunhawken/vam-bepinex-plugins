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

	// real data: scenes (with their thumbnails) read from the .var files of the user's VaM install; nothing is copied anywhere, the picture only lives in the simulator's memory
	public sealed class RealScene { public string Title, Package, Creator; public byte[] Jpg; }

	public static class RealData
	{
		public static List<RealScene> Scenes = new List<RealScene>();
		public static int Packages, SceneCount;

		public static void Load(string vamRoot, int want)
		{
			var files = new List<string>();
			foreach (var d in new[] { "AddonPackages", Path.Combine("AddonPackages", "keep"), Path.Combine("AddonPackages", "resources") })
			{
				var dir = Path.Combine(vamRoot, d);
				if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.var"));
			}
			Packages = files.Count;
			files = files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ToList();
			foreach (var f in files)
			{
				try
				{
					using (var z = System.IO.Compression.ZipFile.OpenRead(f))
					{
						var jsons = z.Entries.Where(e => e.FullName.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
						SceneCount += jsons.Count;
						if (Scenes.Count >= want) continue;
						foreach (var j in jsons)
						{
							var jpg = z.GetEntry(j.FullName.Substring(0, j.FullName.Length - 5) + ".jpg");
							if (jpg == null || jpg.Length > 400000) continue;
							var pkg = Path.GetFileNameWithoutExtension(f);
							byte[] bytes; using (var s = jpg.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
							Scenes.Add(new RealScene { Title = Path.GetFileNameWithoutExtension(j.Name), Package = pkg, Creator = pkg.Split('.')[0], Jpg = bytes });
							break;   // one scene per package: a more varied grid
						}
					}
				}
				catch { }
			}
		}
	}

	// drawing helpers for the stand-in's two screens (top-left origin inside the parent)
	internal static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color col, bool bottom = false)
	{
		var g = new GameObject(name); g.transform.SetParent(parent, false);
		var rt = g.AddComponent<RectTransform>();
		float ay = bottom ? 0f : 1f;
		rt.anchorMin = rt.anchorMax = new Vector2(0f, ay); rt.pivot = new Vector2(0f, ay);
		rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, bottom ? y : -y);
		if (col.a > 0f) g.AddComponent<Image>().color = col;
		return rt;
	}

	internal static Text Label(Transform parent, string s, float x, float y, float w, float h, int size, Color col, TextAnchor al = TextAnchor.MiddleLeft, bool bottom = false)
	{
		var rt = Box(parent, "T " + s, x, y, w, h, new Color(0, 0, 0, 0), bottom);
		var t = rt.gameObject.AddComponent<Text>();
		t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		t.fontSize = size; t.alignment = al; t.color = col; t.text = s; t.raycastTarget = false;
		t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
		return t;
	}

	internal static void Field(Transform parent, string label, string value, float x, float y, float w)
	{
		Label(parent, label, x, y, w, 14f, 11, new Color(0.15f, 0.15f, 0.2f));
		var f = Box(parent, "Field " + label, x, y + 15f, w, 24f, new Color(0.93f, 0.93f, 0.96f));
		Label(f, value, 6f, 0f, w - 24f, 24f, 12, new Color(0.1f, 0.1f, 0.15f));
		Label(f, "v", w - 20f, 0f, 16f, 24f, 11, new Color(0.3f, 0.3f, 0.4f), TextAnchor.MiddleCenter);
	}

	internal static void Btn(Transform parent, string label, float x, float y, float w, float h, Color col, bool bottom = false)
	{
		var b = Box(parent, "Btn " + label, x, y, w, h, col, bottom);
		Label(b, label, 0f, 0f, w, h, 12, Color.white, TextAnchor.MiddleCenter);
	}

	// like BrowserAssist's real one: a screen-space overlay canvas, the resource browser docked to the right (650 units) with the tag column beside it
	public sealed class FakeBrowserUi
	{
		public Canvas mainCanvas;
		public bool isActive { get; private set; }
		public float browserScreenWidth { get { return 650f; } }

		private static readonly string[] Names = { "Beach Day", "City Night", "Studio A", "Loft Party", "Forest Walk", "Poolside", "Penthouse", "Rooftop", "Desert Road", "Cabin", "Dojo", "Garage",
			"Hotel Suite", "Gallery", "Library", "Snow Lodge", "Harbor", "Neon Alley", "Greenhouse", "Subway", "Ballroom", "Lighthouse", "Casino", "Spa" };

		public FakeBrowserUi()
		{
			var go = new GameObject("BrowserAssistCanvas");
			mainCanvas = go.AddComponent<Canvas>();
			mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
			var crt = (RectTransform)go.transform;
			crt.sizeDelta = new Vector2(1440f, 810f);
			var panel = Dock(go.transform, "Panel", 650f, 0f, new Color(0.78f, 0.79f, 0.84f, 0.97f));
			var tags = Dock(go.transform, "SideTags", 150f, -655f, new Color(0.78f, 0.79f, 0.84f, 0.97f));
			var dark = new Color(0.12f, 0.13f, 0.2f);
			var chip = new Color(0.35f, 0.38f, 0.55f);
			Field(panel, "Resource Type", "Scenes", 8f, 6f, 200f); Field(panel, "Tags", "All", 216f, 6f, 200f); Field(panel, "Creator", "All", 424f, 6f, 218f);
			Field(panel, "Package License", "All", 8f, 50f, 200f); Field(panel, "Select Mode", "Single", 216f, 50f, 200f); Field(panel, "Order by", "Newest", 424f, 50f, 218f);
			Field(panel, "Search", "", 8f, 94f, 408f);
			Btn(panel, "Show Hidden", 424f, 109f, 104f, 24f, chip);
			Btn(panel, "Only Favorites", 536f, 109f, 106f, 24f, chip);
			Label(panel, RealData.Scenes.Count > 0 ? "1 - " + RealData.Scenes.Count + " of " + RealData.SceneCount : "1 - 400 of 725", 8f, 140f, 300f, 20f, 13, dark);
			Btn(panel, "<", 560f, 140f, 36f, 20f, chip); Btn(panel, ">", 600f, 140f, 36f, 20f, chip);
			for (int r = 0; r < 4; r++)
				for (int c = 0; c < 6; c++)
				{
					int i = r * 6 + c;
					float x = 8f + c * 106f, y = 166f + r * 138f;
					var tile = Box(panel, "Tile " + i, x, y, 100f, 100f, Color.HSVToRGB((i * 0.071f) % 1f, 0.45f, 0.75f));
					var real = i < RealData.Scenes.Count ? RealData.Scenes[i] : null;
					if (real != null) SimPictures.Set(tile.gameObject, real.Jpg);
					else Box(tile, "Shade", 0f, 62f, 100f, 38f, new Color(0f, 0f, 0f, 0.25f));
					if (i % 5 == 0) Label(tile, "*", 80f, 2f, 18f, 18f, 16, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
					Label(panel, real != null ? real.Title : Names[i % Names.Length], x, y + 102f, 100f, 28f, 11, dark, TextAnchor.UpperCenter);
				}
			Btn(panel, "Action", 8f, 100f, 100f, 26f, new Color(0.25f, 0.28f, 0.5f), true);
			Btn(panel, "Tags", 112f, 100f, 100f, 26f, new Color(0.45f, 0.47f, 0.6f), true);
			Btn(panel, "Load Scene", 8f, 66f, 200f, 28f, new Color(0.2f, 0.5f, 0.3f), true);
			Btn(panel, "Merge Load", 216f, 66f, 200f, 28f, chip, true);
			Label(panel, RealData.Scenes.Count > 0 ? RealData.Scenes[0].Title + "  -  " + RealData.Scenes[0].Package + "  -  Scene" : "Beach Day  -  Creator.BeachDay.3  -  Scene  -  Free", 8f, 30f, 630f, 28f, 11, dark, TextAnchor.UpperLeft, true);
			Label(tags, "Tags", 8f, 6f, 134f, 20f, 13, dark);
			string[] tg = { "outdoor", "indoor", "night", "party", "bedroom", "beach", "city", "studio", "dance", "water", "cinematic", "pose" };
			for (int i = 0; i < tg.Length; i++) Btn(tags, tg[i], 8f, 30f + i * 30f, 134f, 24f, new Color(0.4f + 0.02f * i, 0.42f, 0.58f));
			SetInactive();
		}

		private static RectTransform Dock(Transform parent, string name, float w, float x, Color col)
		{
			var g = new GameObject(name); g.transform.SetParent(parent, false);
			var rt = g.AddComponent<RectTransform>();
			rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f);
			rt.sizeDelta = new Vector2(w, 0f); rt.anchoredPosition = new Vector2(x, 0f);
			g.AddComponent<Image>().color = col;
			return rt;
		}

		public void ToggleActive() { if (isActive) SetInactive(); else SetActive(); }
		public void SetActive() { mainCanvas.enabled = true; isActive = true; }
		public void SetInactive() { mainCanvas.enabled = false; isActive = false; }
	}

	// BrowserAssist's own settings page (the plugin UI VaM shows in the session plugin's window): labelled rows, 700 x 900 units
	internal static void BuildSettingsUi(RectTransform ui)
	{
		var dark = new Color(0.1f, 0.1f, 0.15f);
		Box(ui, "Bg", 0f, 0f, 700f, 900f, new Color(0.82f, 0.83f, 0.88f));
		Label(ui, "BrowserAssist v1.43", 12f, 8f, 676f, 28f, 18, dark);
		string[][] rows =
		{
			new[] { "Enable Desktop Browser", "on" }, new[] { "Dock side", "Right" }, new[] { "Browser width", "650" }, new[] { "Thumbnail size", "100" },
			new[] { "Thumbnails per page", "400" }, new[] { "Show hidden packages", "off" }, new[] { "Only favorites", "off" }, new[] { "Remember last filters", "on" },
			new[] { "Auto-scan on startup", "on" }, new[] { "Lock import target", "off" }, new[] { "Default resource type", "Scenes" }, new[] { "Language", "English" },
		};
		for (int i = 0; i < rows.Length; i++)
		{
			float y = 48f + i * 40f;
			Label(ui, rows[i][0], 16f, y, 340f, 28f, 14, dark);
			var f = Box(ui, "Val " + i, 380f, y, 220f, 28f, new Color(0.93f, 0.93f, 0.96f));
			Label(f, rows[i][1], 8f, 0f, 200f, 28f, 13, dark);
		}
		Btn(ui, "Rescan packages", 16f, 548f, 200f, 32f, new Color(0.25f, 0.28f, 0.5f));
		Btn(ui, "Clear thumbnail cache", 230f, 548f, 200f, 32f, new Color(0.35f, 0.38f, 0.55f));
		Label(ui, RealData.Packages > 0 ? "Packages indexed: " + RealData.Packages + "   Scenes: " + RealData.SceneCount : "Packages indexed: 725   Cache: 1.2 GB", 16f, 596f, 600f, 24f, 12, dark);
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
		ui.anchorMin = ui.anchorMax = new Vector2(0.5f, 0.5f);
		BuildSettingsUi(ui);
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
		if (Environment.GetEnvironmentVariable("BA_REAL_DATA") != "0" && Directory.Exists(@"T:\New folder\AddonPackages")) RealData.Load(@"T:\New folder", 24);
		string root = Path.Combine(Path.GetTempPath(), "vamsim_browserassist_" + Environment.ProcessId);
		Packages(root, "JayJayWon.BrowserAssist.94.var", "JayJayWon.BrowserAssist.96.var", "JayJayWon.UIAssist(Patron).100.var");
		MVRPluginManager mgr; Transform pc;
		BuildSession(out mgr, out pc);
		string created = null;
		MVRPluginManager.SimLoader = (m, plugin, url) => { created = url; return SpawnFake(m, pc, plugin.uid).gameObject; };
		BepInEx.Configuration.ConfigFile.Overrides["General.StartupDelaySeconds"] = "2";
		BepInEx.Configuration.ConfigFile.Overrides["General.AutoLoadSessionPlugin"] = "true";
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
		h.Check(crt.localScale.x > 0.5f && crt.localScale.x <= 1.001f && Math.Abs(wW - (810f * crt.localScale.x + 2f * 8f + 4f)) < 40f, "shown as large as the screen allows (1:1 when it fits), window sized to it (" + wW + " x " + wH + ", scale " + crt.localScale.x + ")");
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
		BepInEx.Configuration.ConfigFile.Overrides["General.AutoLoadSessionPlugin"] = "true";
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
		BepInEx.Configuration.ConfigFile.Overrides["General.AutoLoadSessionPlugin"] = "true";
		h.Start();
		h.Step(8f);
		h.Check(created == 0 && StatusText(h).Contains("not installed"), "no BrowserAssist .var installed: nothing is created and the status says so (" + StatusText(h) + ")");
		h.Set("General", "AutoLoadSessionPlugin", "false");
		Packages(root, "JayJayWon.BrowserAssist.96.var");
		h.Step(5f);
		h.Check(created == 0, "AutoLoadSessionPlugin=false: never created");
		h.Check(!Sim.Errors.Any(e => e.Contains("BrowserAssist")), "the plugin threw nothing");
	}

	[Scenario("browserassist_ondemand", "ZeroT.BrowserAssistStandalone")]
	public static void OnDemand(Harness h)
	{
		if (UnityEngine.EventSystems.EventSystem.current == null) UnityEngine.EventSystems.EventSystem.current = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
		string root = Path.Combine(Path.GetTempPath(), "vamsim_browserassist5_" + Environment.ProcessId);
		Packages(root, "JayJayWon.BrowserAssist.96.var");
		MVRPluginManager mgr; Transform pc;
		BuildSession(out mgr, out pc);
		int created = 0;
		MVRPluginManager.SimLoader = (m, plugin, url) => { created++; return SpawnFake(m, pc, plugin.uid).gameObject; };
		BepInEx.Configuration.ConfigFile.Overrides["General.StartupDelaySeconds"] = "1";
		h.Start();
		h.Step(8f);
		h.Check(created == 0 && StatusText(h).Contains("auto-load is off"), "default: nothing is created at startup (" + StatusText(h) + ")");
		h.Plugin.GetType().GetMethod("ToggleBrowser", All).Invoke(h.Plugin, null);
		h.Step(5f);
		h.Check(created == 1 && BaCount(pc) == 1, "pressing the browser button loads BrowserAssist once, on demand");
		h.Check(!Sim.Errors.Any(e => e.Contains("BrowserAssist")), "the plugin threw nothing");
	}
}
