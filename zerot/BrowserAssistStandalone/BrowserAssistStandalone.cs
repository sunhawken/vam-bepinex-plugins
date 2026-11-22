using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.BrowserAssistStandalone
{
	// Standalone loader for JayJayWon's BrowserAssist (the installed .var; nothing of it is copied here). BrowserAssist only works as a SESSION plugin and
	// only one copy may run, so this plugin:
	//   - waits for VaM's session plugin manager, finds a BrowserAssist that is already loaded (VaM restores the saved session plugins itself) and never adds a second one;
	//   - otherwise creates the session plugin by itself at startup (newest installed JayJayWon.BrowserAssist.<n>.var) - no manual "add plugin";
	//   - shows BrowserAssist's own resource browser (the docked panel with the filters, thumbnails, tabs and actions) INSIDE the window: the plugin's live canvas is moved into the
	//     window (desktop only) at the layout size BrowserAssist built it for, so it looks and works exactly like the .var's own interface, and is handed back to VaM when the
	//     page is left, the window is switched Off or the plugin is removed. Its settings UI is the second page;
	//   - adds a Controls page: every action BrowserAssist registers on its plugin (Open / Close / Toggle UI, Open UI: Scenes / Clothing / Hair / Plugins ..., the Only Favorites /
	//     Show Hidden / Only New filters, results paging, selection, thumbnail size, the tabs, Load Random, Lock Import Target ...) as grouped buttons - found at run time, so a
	//     newer BrowserAssist with more actions shows them too.
	// BrowserAssist's browser, its launch buttons in VaM's UI, its tags / filters / VAR management and its interop with other plugins (UIAssist) are all left to the plugin itself.
	[BepInPlugin("com.zerot.browserassist.standalone", "Browser Assist Standalone", "1.0.0")]
	public sealed class BrowserAssistStandalone : BaseUnityPlugin
	{
		private const string PackageName = "JayJayWon.BrowserAssist";
		private const string ScriptPath = "Custom/Scripts/JayJayWon/BrowserAssist/BrowserAssist.cslist";
		private const string SessionContainerPath = "SceneAtoms/CoreControl/SessionPluginManagerContainer";

		private ConfigEntry<bool> cfgAutoLoad;
		private ConfigEntry<float> cfgStartupDelay;
		private ConfigEntry<bool> cfgUnloadOnOff;
		private ConfigEntry<float> cfgNativeScale;
		private ConfigEntry<bool> cfgAutoSize;
		private ConfigEntry<float> cfgBrowserScale;
		private ConfigEntry<bool> cfgMirrorBrowser;
		private RectTransform browserHost;
		private Canvas mirrorCanvas;
		private RectTransform mirrorRt;
		private Vector2 mirrorOriginal;       // the size (in BrowserAssist's own canvas units) it laid itself out in
		private object mirrorUi;
		private Button browserToggleButton, modeBrowser;
		private const float SideTagWidth = 160f;
		private bool sizedToNative;

		private RlUguiWindow win;
		private Text status;
		private Button reattachButton, modeControls, modeSettings, pagePrev, pageNext;
		private Text pageLabel;
		private Button[] catButtons = new Button[CatCount];
		private Button[] actButtons = new Button[ActSlots];
		private int mode;                  // 0 = Controls, 1 = Settings (BrowserAssist's own UI)
		private int cat;
		private int page;
		private List<string> catActions = new List<string>();
		private int[] catTotals = new int[CatCount];
		private RectTransform viewport;
		private RectTransform scrollContent;
		private RectTransform nativeHost;
		private ScrollRect nativeScroll;

		private MVRPluginManager manager;
		private MVRScript script;
		private string pluginUid = "";
		private float readyAt = -1f;
		private float nextTick;
		private bool created;
		private string createdUid = "";
		private int creationAttempts;
		private bool loadingSeen;

		private RectTransform detached;
		private Transform originalParent;
		private int originalSibling;
		private Vector3 originalPosition;
		private Vector3 originalScale;
		private Quaternion originalRotation;
		private Vector2 originalAnchorsMin;
		private Vector2 originalAnchorsMax;
		private Vector2 originalPivot;
		private Vector2 originalSize;
		private Vector2 originalAnchoredPosition;
		private bool originalActive;
		private float detachedWidth = 800f;
		private float detachedHeight = 800f;

		private const int CatCount = 6;
		private const int ActSlots = 16;
		private static readonly string[] CatNames = { "Browser", "Types", "Filters", "Browse", "Tabs", "Random" };
		private float HeaderH = 86f;
		private bool mirrorSized;

		private void Awake()
		{
			cfgAutoLoad = Config.Bind<bool>("General", "AutoLoadSessionPlugin", true, "Create the BrowserAssist session plugin automatically at startup when VaM has not loaded it already.");
			cfgStartupDelay = Config.Bind<float>("General", "StartupDelaySeconds", 8f, "Wait this long after VaM finishes loading before adding BrowserAssist (VaM restores its saved session plugins first, so it is not added twice).");
			cfgUnloadOnOff = Config.Bind<bool>("General", "UnloadWhenSwitchedOff", false, "Switching this plugin Off also removes the BrowserAssist session plugin (off = only the window and auto-load stop; BrowserAssist keeps running).");
			cfgNativeScale = Config.Bind<float>("Window", "NativeUiScale", 1f, new ConfigDescription("Size of BrowserAssist's own settings UI inside the window: 1 = exactly as it looks in VaM (1:1, the window scrolls if it is smaller); 0 = shrink it to fit the window width.", new AcceptableValueRange<float>(0f, 3f)));
			cfgAutoSize = Config.Bind<bool>("Window", "SizeToNativeUi", true, "When BrowserAssist's UI first appears, size the window to show it at its original size (as far as the screen allows).");
			cfgMirrorBrowser = Config.Bind<bool>("Window", "ShowBrowserInWindow", true, "Show BrowserAssist's resource browser (its docked panel) inside this window (desktop only; in VR it stays where BrowserAssist puts it). Off = the browser stays on VaM's screen and the window only has the settings and the controls.");
			cfgBrowserScale = Config.Bind<float>("Window", "BrowserScale", 0f, new ConfigDescription("Size of the browser inside the window: 0 = fit the window (resize the window to scale it, proportions stay the same); otherwise a fixed factor (1 = BrowserAssist's own canvas units).", new AcceptableValueRange<float>(0f, 3f)));
			win = new RlUguiWindow(this, "Browser Assist", "Window", 40f, 60f, 620f, 680f, 380f, 300f, 1700f, 1400f);
		}

		private IEnumerator Start()
		{
			while (SuperController.singleton == null || EventSystem.current == null)
			{
				yield return null;
			}
			BuildWindow();
		}

		private void OnDestroy()
		{
			Restore();
			if (win != null)
			{
				win.Dispose();
			}
		}

		private void OnDisable()
		{
			Restore();
			if (win != null)
			{
				win.SetActive(false);
			}
		}

		private void OnEnable()
		{
			if (win != null)
			{
				win.SetActive(true);
			}
		}

		// ------------------------------------------------------------------ the package

		// newest installed JayJayWon.BrowserAssist.<n>.var (any folder under AddonPackages); null when there is none
		private string PackageUrl()
		{
			int best = -1;
			try
			{
				string packages = Path.Combine(Path.GetFullPath(Paths.GameRootPath), "AddonPackages");
				if (Directory.Exists(packages))
				{
					foreach (string f in Directory.GetFiles(packages, PackageName + ".*.var", SearchOption.AllDirectories))
					{
						string name = Path.GetFileNameWithoutExtension(f);
						int dot = name.LastIndexOf('.');
						int v;
						if (dot > 0 && int.TryParse(name.Substring(dot + 1), out v) && v > best)
						{
							best = v;
						}
					}
				}
			}
			catch
			{
			}
			return best < 0 ? null : PackageName + "." + best + ":/" + ScriptPath;
		}

		// ------------------------------------------------------------------ session plugin

		private MVRPluginManager FindManager()
		{
			GameObject container = GameObject.Find(SessionContainerPath);
			if (container == null)
			{
				return null;
			}
			MVRPluginManager[] managers = container.GetComponentsInChildren<MVRPluginManager>(true);
			for (int i = 0; i < managers.Length; i++)
			{
				if (managers[i] != null && managers[i].pluginContainer != null)
				{
					return managers[i];
				}
			}
			return null;
		}

		// a BrowserAssist that is already running (BrowserAssist itself looks for names ending in _JayJayWon.BrowserAssist; the free edition counts as the same plugin)
		private static MVRScript FindExistingScript(MVRPluginManager m)
		{
			Transform pc = m.pluginContainer;
			for (int i = 0; i < pc.childCount; i++)
			{
				Transform child = pc.GetChild(i);
				if (child.name.IndexOf("JayJayWon.BrowserAssist", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					MVRScript s = child.GetComponent<MVRScript>();
					if (s != null)
					{
						return s;
					}
				}
			}
			return null;
		}

		private static string UidOf(MVRScript s)
		{
			string n = s.name;
			int us = n.IndexOf('_');
			return us > 0 ? n.Substring(0, us) : n;
		}

		private void Tick()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			if (sc.isLoading)
			{
				loadingSeen = true;
				return;
			}
			if (loadingSeen)
			{
				// a scene just finished loading: a session plugin stays, but give VaM a moment before anything is judged missing
				loadingSeen = false;
				if (script == null)
				{
					readyAt = Time.unscaledTime + 2f;
				}
			}
			if (readyAt < 0f)
			{
				readyAt = Time.unscaledTime + cfgStartupDelay.Value;
			}
			if (manager == null)
			{
				manager = FindManager();
				if (manager == null)
				{
					SetStatus("Waiting for VaM's session plugin manager...");
					return;
				}
			}
			if (script == null)
			{
				script = FindExistingScript(manager);
				if (script != null)
				{
					pluginUid = UidOf(script);
					created = createdUid.Length > 0 && pluginUid == createdUid;   // the one this plugin added, found again on the next tick
				}
			}
			if (script == null)
			{
				if (!cfgAutoLoad.Value)
				{
					SetStatus("BrowserAssist is not loaded (auto-load is off).");
					return;
				}
				if (Time.unscaledTime < readyAt)
				{
					SetStatus("Starting BrowserAssist shortly...");
					return;
				}
				string url = PackageUrl();
				if (url == null)
				{
					SetStatus("JayJayWon.BrowserAssist is not installed in AddonPackages.");
					return;
				}
				if (creationAttempts >= 3)
				{
					SetStatus("Could not load BrowserAssist - see BepInEx/LogOutput.log.");
					return;
				}
				creationAttempts++;
				try
				{
					MVRPlugin plugin = manager.CreatePlugin();
					plugin.pluginURLJSON.val = url;
					pluginUid = plugin.uid;
					createdUid = plugin.uid;
					created = true;
					Logger.LogInfo("Created the BrowserAssist session plugin (" + url + ").");
				}
				catch (Exception e)
				{
					Logger.LogError("Creating the BrowserAssist session plugin failed: " + e);
				}
				return;
			}
			RectTransform ui = script.UITransform as RectTransform;
			if (ui == null)
			{
				SetStatus("BrowserAssist loaded; waiting for its UI...");
				return;
			}
			if (detached == null)
			{
				Attach(ui);
			}
			if (mode == 2)
			{
				// BrowserAssist can rebuild its browser (rescan / restore): follow the new canvas
				if (mirrorCanvas != null && (mirrorCanvas == null || BrowserUi() != mirrorUi))
				{
					Unmirror();
				}
				Mirror();
				LayoutMirror();
			}
			SetStatus("BrowserAssist is running" + (created ? " (loaded by this plugin)" : " (already loaded by VaM)") + ".   Browser: " + (BrowserIsOpen() ? "open" : "closed") + (mirrorCanvas != null ? " (shown in this window)" : ""));
			RefreshModeButtons();
			RefreshControls();
		}

		// ------------------------------------------------------------------ the browser (BrowserAssist's own launch action)

		// BrowserAssist keeps its main browser in the static property BrowserAssist.mainBrowserUI (a BrowserUI with ToggleActive() and isActive)
		private object BrowserUi()
		{
			if (script == null)
			{
				return null;
			}
			try
			{
				BindingFlags f = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
				PropertyInfo p = script.GetType().GetProperty("mainBrowserUI", f);
				if (p != null)
				{
					return p.GetValue(null, null);
				}
				FieldInfo fi = script.GetType().GetField("mainBrowserUI", f);
				return fi != null ? fi.GetValue(null) : null;
			}
			catch
			{
				return null;
			}
		}

		private bool BrowserIsOpen()
		{
			object ui = BrowserUi();
			if (ui == null)
			{
				return false;
			}
			try
			{
				BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				PropertyInfo p = ui.GetType().GetProperty("isActive", f);
				if (p != null)
				{
					object v = p.GetValue(ui, null);
					return v is bool && (bool)v;
				}
				FieldInfo fi = ui.GetType().GetField("isActive", f);
				object w = fi != null ? fi.GetValue(ui) : null;
				return w is bool && (bool)w;
			}
			catch
			{
				return false;
			}
		}

		// ------------------------------------------------------------------ the actions BrowserAssist registers (its real interface)

		// which group an action belongs to (by its name; unknown names land in Random, so nothing is ever hidden)
		private static int CategoryOf(string name)
		{
			if (name == "Open UI" || name == "Close UI" || name == "Toggle UI Open/Close") return 0;
			if (name.StartsWith("Open UI:")) return 1;
			if (name.StartsWith("Toggle '")) return 2;
			if (name.IndexOf("Results Page", StringComparison.Ordinal) >= 0 || name.IndexOf("Selection", StringComparison.Ordinal) >= 0 || name.IndexOf("Thumbnail", StringComparison.Ordinal) >= 0) return 3;
			if (name.StartsWith("Select ") && name.EndsWith(" Tab")) return 4;
			return 5;
		}

		private static string Short(string name)
		{
			if (name.StartsWith("Open UI: ")) return name.Substring(9);
			if (name.StartsWith("Select ") && name.EndsWith(" Tab")) return name.Substring(7, name.Length - 11);
			return name;
		}

		private List<string> ActionsOf(int category)
		{
			List<string> list = new List<string>();
			if (script == null)
			{
				return list;
			}
			try
			{
				foreach (string n in script.GetActionNames())
				{
					if (CategoryOf(n) == category)
					{
						list.Add(n);
					}
				}
			}
			catch
			{
			}
			return list;
		}

		private void RefreshControls()
		{
			if (actButtons[0] == null)
			{
				return;
			}
			for (int c = 0; c < CatCount; c++)
			{
				int total = ActionsOf(c).Count;
				catTotals[c] = total;
				RlUguiWindow.SetLabel(catButtons[c], CatNames[c] + (script != null ? " " + total : ""));
				catButtons[c].interactable = total > 0;
			}
			catActions = ActionsOf(cat);
			int pages = Mathf.Max(1, (catActions.Count + ActSlots - 1) / ActSlots);
			if (page >= pages)
			{
				page = pages - 1;
			}
			pageLabel.text = catActions.Count == 0 ? (script == null ? "BrowserAssist is not running yet" : "no actions in this group") : (page + 1) + " / " + pages;
			for (int i = 0; i < ActSlots; i++)
			{
				int idx = page * ActSlots + i;
				bool show = mode == 0 && idx < catActions.Count;
				actButtons[i].gameObject.SetActive(show);
				if (show)
				{
					RlUguiWindow.SetLabel(actButtons[i], Short(catActions[idx]));
				}
			}
		}

		private void RunAction(int slot)
		{
			int idx = page * ActSlots + slot;
			if (script == null || idx < 0 || idx >= catActions.Count)
			{
				return;
			}
			try
			{
				script.CallAction(catActions[idx]);
				SetStatus("Ran \"" + catActions[idx] + "\".   Browser: " + (BrowserIsOpen() ? "open" : "closed"));
			}
			catch (Exception e)
			{
				Logger.LogWarning("BrowserAssist action \"" + catActions[idx] + "\" failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
			}
		}

		// the 1:1 view: the window is made as large as BrowserAssist's own UI (limited by the screen), once per start
		private void SizeWindowToNative()
		{
			if (sizedToNative || !cfgAutoSize.Value || win == null || detached == null)
			{
				return;
			}
			sizedToNative = true;
			float scale = cfgNativeScale.Value > 0f ? cfgNativeScale.Value : 1f;
			float sw = Mathf.Max(400f, Screen.width / Mathf.Max(0.5f, win.Scale) - 40f);
			float sh = Mathf.Max(300f, Screen.height / Mathf.Max(0.5f, win.Scale) - 60f);
			win.W = Mathf.Clamp(detachedWidth * scale + 2f * RlUguiWindow.Margin + 4f, 380f, Mathf.Min(1700f, sw));
			win.H = Mathf.Clamp(detachedHeight * scale + HeaderH + RlUguiWindow.Margin, 300f, Mathf.Min(1400f, sh));
			win.Apply();
		}

		// ------------------------------------------------------------------ BrowserAssist's own browser, inside the window

		private static bool IsVR()
		{
			SuperController sc = SuperController.singleton;
			return sc != null && (sc.isOVR || sc.isOpenVR);
		}

		private Canvas BrowserCanvas(object ui)
		{
			if (ui == null)
			{
				return null;
			}
			try
			{
				FieldInfo f = ui.GetType().GetField("mainCanvas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (f != null)
				{
					return f.GetValue(ui) as Canvas;
				}
				PropertyInfo p = ui.GetType().GetProperty("mainCanvas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				return p != null ? p.GetValue(ui, null) as Canvas : null;
			}
			catch
			{
				return null;
			}
		}

		// width of BrowserAssist's panel in its own units (650 docked, wider in full-width mode) plus the side panel that holds the tag list
		private float PanelUnitsWidth()
		{
			float w = 650f;
			try
			{
				PropertyInfo p = mirrorUi != null ? mirrorUi.GetType().GetProperty("browserScreenWidth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
				if (p != null)
				{
					object v = p.GetValue(mirrorUi, null);
					if (v is float && (float)v > 100f)
					{
						w = (float)v;
					}
				}
			}
			catch
			{
			}
			return w + SideTagWidth;
		}

		private void InvokeBrowser(string method)
		{
			object ui = mirrorUi ?? BrowserUi();
			if (ui == null)
			{
				return;
			}
			try
			{
				MethodInfo m = ui.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (m != null)
				{
					m.Invoke(ui, null);
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("BrowserAssist " + method + " failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
			}
		}

		private void ToggleBrowser()
		{
			if (BrowserUi() == null)
			{
				SetStatus("BrowserAssist's browser is not ready yet (it is still scanning, or it failed to start - see its settings).");
				return;
			}
			InvokeBrowser("ToggleActive");
			nextTick = 0f;
			RefreshModeButtons();
		}

		private void Mirror()
		{
			if (mirrorCanvas != null || browserHost == null || script == null || !cfgMirrorBrowser.Value || IsVR())
			{
				return;
			}
			object ui = BrowserUi();
			Canvas c = BrowserCanvas(ui);
			RectTransform rt = c != null ? c.transform as RectTransform : null;
			if (rt == null)
			{
				return;
			}
			mirrorUi = ui;
			mirrorCanvas = c;
			mirrorRt = rt;
			mirrorOriginal = rt.rect.size;      // an overlay canvas is the screen divided by its scale: the units the panel's stretched parts were sized in
			if (mirrorOriginal.y < 200f)
			{
				mirrorOriginal = new Vector2(1440f, 810f);
			}
			rt.SetParent(browserHost, false);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.zero;
			rt.pivot = Vector2.zero;
			rt.anchoredPosition = Vector2.zero;
			rt.localRotation = Quaternion.identity;
			browserHost.gameObject.SetActive(mode == 2);
			Logger.LogInfo("BrowserAssist's browser is shown inside the window.");
			LayoutMirror();
			SizeWindowToBrowser();
			if (!BrowserIsOpen())
			{
				InvokeBrowser("SetActive");     // the window is the browser's place now: open it
			}
		}

		private void Unmirror()
		{
			if (mirrorCanvas == null)
			{
				return;
			}
			try
			{
				if (mirrorRt != null)
				{
					mirrorRt.SetParent(null, false);     // a root overlay canvas again: VaM draws it on the screen as before
					mirrorRt.localScale = Vector3.one;
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Handing the browser back failed: " + e.Message);
			}
			mirrorCanvas = null;
			mirrorRt = null;
			mirrorUi = null;
			mirrorSized = false;
		}

		// the browser fills the window's content area: one factor for both directions, so the proportions never change
		private void LayoutMirror()
		{
			if (mirrorRt == null || browserHost == null || win == null)
			{
				return;
			}
			float w = PanelUnitsWidth(), h = mirrorOriginal.y;
			float availW = Mathf.Max(50f, win.W - 2f * RlUguiWindow.Margin), availH = Mathf.Max(50f, win.H - HeaderH - RlUguiWindow.Margin);
			float k = cfgBrowserScale.Value > 0f ? cfgBrowserScale.Value : Mathf.Clamp(Mathf.Min(availW / w, availH / h), 0.25f, 4f);
			mirrorRt.sizeDelta = new Vector2(w, h);
			mirrorRt.localScale = new Vector3(k, k, 1f);
			mirrorRt.anchoredPosition = Vector2.zero;
		}

		// once per mirror: make the window as large as the browser at 1:1 (as far as the screen allows)
		private void SizeWindowToBrowser()
		{
			if (mirrorSized || win == null || mirrorRt == null || !cfgAutoSize.Value)
			{
				return;
			}
			mirrorSized = true;
			float sw = Mathf.Max(400f, Screen.width / Mathf.Max(0.5f, win.Scale) - 40f);
			float sh = Mathf.Max(300f, Screen.height / Mathf.Max(0.5f, win.Scale) - 60f);
			float k = cfgBrowserScale.Value > 0f ? cfgBrowserScale.Value : 1f;
			win.W = Mathf.Clamp(PanelUnitsWidth() * k + 2f * RlUguiWindow.Margin + 4f, 380f, Mathf.Min(1700f, sw));
			win.H = Mathf.Clamp(mirrorOriginal.y * k + HeaderH + RlUguiWindow.Margin, 300f, Mathf.Min(1400f, sh));
			win.Apply();
		}

		private void RefreshModeButtons()
		{
			if (browserToggleButton != null)
			{
				RlUguiWindow.SetLabel(browserToggleButton, BrowserIsOpen() ? "Close browser" : "Open browser");
			}
		}

		private void SetMode(int m)
		{
			mode = m;
			if (m != 2)
			{
				Unmirror();     // the browser goes back to VaM's screen while another page is shown
			}
			if (browserHost != null)
			{
				browserHost.gameObject.SetActive(m == 2);
			}
			if (m == 2)
			{
				Mirror();
			}
			if (viewport != null)
			{
				viewport.gameObject.SetActive(m == 1);
			}
			bool c = m == 0;
			for (int i = 0; i < CatCount; i++) catButtons[i].gameObject.SetActive(c);
			pagePrev.gameObject.SetActive(c);
			pageNext.gameObject.SetActive(c);
			pageLabel.gameObject.SetActive(c);
			RefreshControls();
			if (win != null) win.Apply();
		}

		private void SetActive(bool on)
		{
			if (!on)
			{
				Restore();
				if (cfgUnloadOnOff.Value && manager != null && !string.IsNullOrEmpty(pluginUid))
				{
					try
					{
						manager.RemovePluginWithUID(pluginUid);
						script = null;
						pluginUid = "";
						created = false;
					}
					catch (Exception e)
					{
						Logger.LogWarning("Removing BrowserAssist failed: " + e.Message);
					}
				}
				SetStatus("Browser Assist window is off" + (script != null ? " (BrowserAssist itself keeps running)." : "."));
				RefreshControls();
			}
			else
			{
				creationAttempts = 0;
				readyAt = Time.unscaledTime;
				nextTick = 0f;
			}
		}

		private void Update()
		{
			if (win == null || win.Window == null)
			{
				return;
			}
			win.Tick();
			if (!win.Active || Time.unscaledTime < nextTick)
			{
				return;
			}
			nextTick = Time.unscaledTime + 1f;
			if (detached != null && (originalParent == null || script == null))
			{
				Restore();
				script = null;
			}
			Tick();
		}

		// ------------------------------------------------------------------ window

		private void SetStatus(string text)
		{
			if (status != null && status.text != text)
			{
				status.text = text;
			}
		}

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h)
			{
				LayoutBody(w, h);
			};
			win.OnActiveChanged = SetActive;
			win.Build("Browser Assist Standalone Desktop");
			status = RlUguiWindow.NewText("Status", win.Body, "Starting...", TextAnchor.MiddleLeft);
			modeBrowser = RlUguiWindow.NewButton("Mode browser", win.Body, "Browser", delegate { SetMode(2); });
			modeSettings = RlUguiWindow.NewButton("Mode settings", win.Body, "Settings", delegate { SetMode(1); });
			modeControls = RlUguiWindow.NewButton("Mode controls", win.Body, "Controls", delegate { SetMode(0); });
			browserToggleButton = RlUguiWindow.NewButton("Browser toggle", win.Body, "Open browser", delegate { ToggleBrowser(); });
			reattachButton = browserToggleButton;
			browserHost = RlUguiWindow.NewRect("Browser host", win.Body);
			browserHost.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.05f, 1f);
			browserHost.gameObject.AddComponent<RectMask2D>();
			browserHost.anchorMin = Vector2.zero;
			browserHost.anchorMax = Vector2.one;
			for (int i = 0; i < CatCount; i++)
			{
				int k = i;
				catButtons[i] = RlUguiWindow.NewButton("Group " + CatNames[i], win.Body, CatNames[i], delegate { cat = k; page = 0; RefreshControls(); });
			}
			for (int i = 0; i < ActSlots; i++)
			{
				int k = i;
				actButtons[i] = RlUguiWindow.NewButton("Action " + i, win.Body, "", delegate { RunAction(k); });
			}
			pagePrev = RlUguiWindow.NewButton("Page prev", win.Body, "<", delegate { page = Mathf.Max(0, page - 1); RefreshControls(); });
			pageNext = RlUguiWindow.NewButton("Page next", win.Body, ">", delegate { page++; RefreshControls(); });
			pageLabel = RlUguiWindow.NewText("Page", win.Body, "", TextAnchor.MiddleCenter);

			viewport = RlUguiWindow.NewRect("Native UI viewport", win.Body);
			viewport.gameObject.AddComponent<Image>().color = RlUguiWindow.ViewCol;
			viewport.gameObject.AddComponent<RectMask2D>();
			nativeScroll = viewport.gameObject.AddComponent<ScrollRect>();
			nativeScroll.viewport = viewport;
			nativeScroll.horizontal = true;
			nativeScroll.vertical = true;
			nativeScroll.movementType = ScrollRect.MovementType.Clamped;
			nativeScroll.scrollSensitivity = 40f;
			Vector2 corner = new Vector2(0f, 1f);
			scrollContent = RlUguiWindow.NewRect("Scroll content", viewport);
			scrollContent.pivot = corner;
			scrollContent.anchorMin = corner;
			scrollContent.anchorMax = corner;
			nativeScroll.content = scrollContent;
			nativeHost = RlUguiWindow.NewRect("Native UI host", scrollContent);
			nativeHost.pivot = corner;
			nativeHost.anchorMin = corner;
			nativeHost.anchorMax = corner;
			nativeHost.anchoredPosition = Vector2.zero;
			win.AddResizeHandle();
			win.Apply();
			SetMode(IsVR() || !cfgMirrorBrowser.Value ? 1 : 2);
		}

		private void LayoutBody(float w, float h)
		{
			if (status == null)
			{
				return;
			}
			float m = RlUguiWindow.Margin;
			float inner = Mathf.Max(60f, w - 2f * m);
			RlUguiWindow.SetRect(status.rectTransform, m, 30f, inner, 22f);
			float quarter = (inner - 12f) / 4f;
			RlUguiWindow.SetRect(modeBrowser.GetComponent<RectTransform>(), m, 54f, quarter, 26f);
			RlUguiWindow.SetRect(modeSettings.GetComponent<RectTransform>(), m + (quarter + 4f), 54f, quarter, 26f);
			RlUguiWindow.SetRect(modeControls.GetComponent<RectTransform>(), m + 2f * (quarter + 4f), 54f, quarter, 26f);
			RlUguiWindow.SetRect(browserToggleButton.GetComponent<RectTransform>(), m + 3f * (quarter + 4f), 54f, quarter, 26f);
			// Controls: two rows of group chips, then a grid of the group's actions, then the pager
			float cw = (inner - 8f) / 3f;
			for (int i = 0; i < CatCount; i++)
			{
				RlUguiWindow.SetRect(catButtons[i].GetComponent<RectTransform>(), m + (i % 3) * (cw + 4f), 86f + (i / 3) * 28f, cw, 24f);
			}
			float gy = 86f + 2f * 28f + 4f;
			float gw = (inner - 6f) / 2f;
			int rowsFit = Mathf.Clamp(Mathf.FloorToInt((h - gy - 40f) / 30f), 1, ActSlots / 2);
			for (int i = 0; i < ActSlots; i++)
			{
				bool fits = i / 2 < rowsFit;
				RlUguiWindow.SetRect(actButtons[i].GetComponent<RectTransform>(), m + (i % 2) * (gw + 6f), gy + (i / 2) * 30f, gw, 26f);
				if (!fits && actButtons[i].gameObject.activeSelf) actButtons[i].gameObject.SetActive(false);
			}
			float py = gy + rowsFit * 30f + 4f;
			RlUguiWindow.SetRect(pagePrev.GetComponent<RectTransform>(), m, py, 30f, 24f);
			RlUguiWindow.SetRect(pageLabel.rectTransform, m + 34f, py, inner - 68f, 24f);
			RlUguiWindow.SetRect(pageNext.GetComponent<RectTransform>(), m + inner - 30f, py, 30f, 24f);
			HeaderH = 86f;
			if (browserHost != null)
			{
				browserHost.anchorMin = Vector2.zero;
				browserHost.anchorMax = Vector2.one;
				browserHost.offsetMin = new Vector2(m, m);
				browserHost.offsetMax = new Vector2(-m, -HeaderH);
				LayoutMirror();
			}
			if (viewport != null)
			{
				viewport.anchorMin = Vector2.zero;
				viewport.anchorMax = Vector2.one;
				viewport.offsetMin = new Vector2(m, m);
				viewport.offsetMax = new Vector2(-m, -HeaderH);
			}
			UpdateNativePresentation();
		}

		private void UpdateNativePresentation()
		{
			if (detached == null || viewport == null || win == null)
			{
				return;
			}
			float vw = Mathf.Max(1f, win.W - 2f * RlUguiWindow.Margin);
			float vh = Mathf.Max(1f, win.H - HeaderH - RlUguiWindow.Margin);
			float fit = cfgNativeScale.Value > 0f ? cfgNativeScale.Value : Mathf.Min(1f, vw / detachedWidth);
			nativeHost.sizeDelta = new Vector2(detachedWidth * fit, detachedHeight * fit);
			scrollContent.sizeDelta = new Vector2(Mathf.Max(vw, detachedWidth * fit), Mathf.Max(vh, detachedHeight * fit));
			detached.localScale = Vector3.one * fit;
		}

		// Moves the plugin's own UI panel into the window; Restore puts it back exactly where VaM had it.
		private void Attach(RectTransform target)
		{
			if (target == null || nativeHost == null)
			{
				return;
			}
			Restore();
			originalParent = target.parent;
			originalSibling = target.GetSiblingIndex();
			originalPosition = target.localPosition;
			originalScale = target.localScale;
			originalRotation = target.localRotation;
			originalAnchorsMin = target.anchorMin;
			originalAnchorsMax = target.anchorMax;
			originalPivot = target.pivot;
			originalSize = target.sizeDelta;
			originalAnchoredPosition = target.anchoredPosition;
			originalActive = target.gameObject.activeSelf;
			detachedWidth = Mathf.Max(300f, target.rect.width);
			detachedHeight = Mathf.Max(300f, target.rect.height);
			detached = target;
			target.SetParent(nativeHost, false);
			Vector2 corner = new Vector2(0f, 1f);
			target.pivot = corner;
			target.anchorMin = corner;
			target.anchorMax = corner;
			target.anchoredPosition = Vector2.zero;
			target.sizeDelta = new Vector2(detachedWidth, detachedHeight);
			target.localRotation = Quaternion.identity;
			target.gameObject.SetActive(true);
			nativeScroll.verticalNormalizedPosition = 1f;
			UpdateNativePresentation();
			SizeWindowToNative();
		}

		private void Restore()
		{
			Unmirror();
			if (detached != null && originalParent != null)
			{
				detached.SetParent(originalParent, false);
				detached.SetSiblingIndex(Mathf.Clamp(originalSibling, 0, originalParent.childCount - 1));
				detached.anchorMin = originalAnchorsMin;
				detached.anchorMax = originalAnchorsMax;
				detached.pivot = originalPivot;
				detached.sizeDelta = originalSize;
				detached.anchoredPosition = originalAnchoredPosition;
				detached.localPosition = originalPosition;
				detached.localRotation = originalRotation;
				detached.localScale = originalScale;
				detached.gameObject.SetActive(originalActive);
			}
			detached = null;
			originalParent = null;
		}
	}
}
