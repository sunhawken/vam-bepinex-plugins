using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("com.14mhz.penetrationcounter.bepinex", "PenetrationCounter BepInEx", "0.8.3")]
public sealed class PenetrationCounterBepInExLoader : BaseUnityPlugin
{
	private sealed class CounterHost
	{
		public Atom atom;

		public MVRPluginManager manager;

		public GameObject hostObject;

		public PenetrationCounter script;

		public Transform hiddenUI;

		public Canvas canvas;
	}

	private const string Version = "0.8.3";

	private const int WindowId = 14087031;

	private readonly Dictionary<int, CounterHost> counters = new Dictionary<int, CounterHost>();

	private SuperController hookedController;

	private float nextReconcileAt;

	private bool shuttingDown;

	private Rect windowRect;

	private bool guiVisible;

	private bool resizing;

	private bool collapsed;

	private float expandedHeight;

	private float expandedWidth;

	private Vector2 resizeStartMouse;

	private Vector2 resizeStartSize;

	private int selectedTracker;

	private int selectedPartner;

	private Vector2 tableScroll;

	private float lastKnownScreenW;

	private float lastKnownScreenH;

	private bool layoutDirty;

	private float saveLayoutAt;

	private bool stylesReady;

	private float lastStyleScale = -1f;

	private ConfigEntry<bool> cfgShowOnStart;

	private ConfigEntry<bool> cfgActive;

	private ConfigEntry<bool> cfgCollapsed;

	private ConfigEntry<float> cfgWindowX;

	private ConfigEntry<float> cfgWindowY;

	private ConfigEntry<float> cfgWindowW;

	private ConfigEntry<float> cfgWindowH;

	private ConfigEntry<float> cfgUserScale;

	private ConfigEntry<bool> cfgDebugLog;

	private ConfigEntry<bool> cfgDebugGizmos;

	private GUIStyle titleStyle;

	private GUIStyle headerStyle;

	private GUIStyle cellStyle;

	private GUIStyle labelStyle;

	private GUIStyle smallStyle;

	private GUIStyle resizeStyle;

	private GUIStyle rlButtonStyle;

	private Rect restoreRect;

	private bool maximized;

	private void Awake()
	{
		BindConfig();
		LoadWindowLayout();
		guiVisible = cfgShowOnStart.Value;
		ApplyDebugSettings();
		Logger.LogInfo((object)"PenetrationCounter BepInEx 0.8.3 loaded. F8 toggles the desktop GUI.");
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void BindConfig()
	{
		cfgActive = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Active", true, "Turn PenetrationCounter off completely: removes the counter scripts from every Person and stops scanning.");
		cfgShowOnStart = ((BaseUnityPlugin)this).Config.Bind<bool>("Desktop GUI", "ShowOnStart", true, "Show the desktop-style overlay when VaM starts.");
		cfgCollapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Desktop GUI", "Collapsed", false, "Remember whether the desktop window is collapsed to its title bar.");
		cfgWindowX = ((BaseUnityPlugin)this).Config.Bind<float>("Desktop GUI", "WindowX", 40f, "Saved GUI X position in pixels.");
		cfgWindowY = ((BaseUnityPlugin)this).Config.Bind<float>("Desktop GUI", "WindowY", 40f, "Saved GUI Y position in pixels.");
		cfgWindowW = ((BaseUnityPlugin)this).Config.Bind<float>("Desktop GUI", "WindowWidth", 980f, "Saved GUI width in pixels.");
		cfgWindowH = ((BaseUnityPlugin)this).Config.Bind<float>("Desktop GUI", "WindowHeight", 430f, "Saved GUI height in pixels.");
		cfgUserScale = ((BaseUnityPlugin)this).Config.Bind<float>("Desktop GUI", "UIScale", 1f, "Manual UI scale multiplier. DPI scaling is applied automatically too.");
		cfgDebugLog = ((BaseUnityPlugin)this).Config.Bind<bool>("Debug", "DebugLogging", false, "Enable PenetrationCounter debug logging.");
		cfgDebugGizmos = ((BaseUnityPlugin)this).Config.Bind<bool>("Debug", "DebugGizmos", false, "Enable PenetrationCounter debug gizmos.");
	}

	private void LoadWindowLayout()
	{
		collapsed = cfgCollapsed != null && cfgCollapsed.Value;
		expandedHeight = Mathf.Max(130f, cfgWindowH.Value);
		expandedWidth = Mathf.Max(220f, cfgWindowW.Value);
		windowRect = new Rect(cfgWindowX.Value, cfgWindowY.Value, collapsed ? CollapsedWindowWidth() : expandedWidth, collapsed ? CollapsedWindowHeight() : expandedHeight);
		SanitizeWindowRect(recoverOffScreen: true);
		lastKnownScreenW = Screen.width;
		lastKnownScreenH = Screen.height;
	}

	private void ApplyDebugSettings()
	{
		PenetrationCounter.DBGLOG = cfgDebugLog != null && cfgDebugLog.Value;
		PenetrationCounter.DBGGIZ = cfgDebugGizmos != null && cfgDebugGizmos.Value;
		foreach (CounterHost value in counters.Values)
		{
			if (value != null && (Object)(object)value.script != (Object)null)
			{
				value.script.setDebugLog(PenetrationCounter.DBGLOG);
				value.script.setDebugGizmo(PenetrationCounter.DBGGIZ);
			}
		}
	}

	private void Start()
	{
		TryHookSuperController();
		nextReconcileAt = 0f;
	}

	private void Update()
	{
		if (!shuttingDown)
		{
			if (Input.GetKeyDown((KeyCode)289))
			{
				guiVisible = !guiVisible;
			}
			HandleLiveResize();
			HandleResolutionChange();
			SaveLayoutIfDue();
			TryHookSuperController();
			if (cfgActive.Value && !(Time.unscaledTime < nextReconcileAt))
			{
				nextReconcileAt = Time.unscaledTime + 0.75f;
				ReconcilePeople();
			}
		}
	}

	private void HandleResolutionChange()
	{
		if (!Mathf.Approximately(lastKnownScreenW, (float)Screen.width) || !Mathf.Approximately(lastKnownScreenH, (float)Screen.height))
		{
			lastKnownScreenW = Screen.width;
			lastKnownScreenH = Screen.height;
			expandedHeight = Mathf.Clamp(expandedHeight, 130f, Mathf.Max(130f, (float)Screen.height - 8f));
			expandedWidth = Mathf.Clamp(expandedWidth, 220f, Mathf.Max(220f, (float)Screen.width - 8f));
			if (collapsed)
			{
				windowRect.width = CollapsedWindowWidth();
				windowRect.height = CollapsedWindowHeight();
			}
			SanitizeWindowRect(recoverOffScreen: true);
			MarkLayoutDirty();
		}
	}

	private void HandleLiveResize()
	{
		if (collapsed)
		{
			resizing = false;
		}
		else if (resizing)
		{
			if (!Input.GetMouseButton(0))
			{
				resizing = false;
				MarkLayoutDirty();
				return;
			}
			Vector2 val = ScreenMousePosition() - resizeStartMouse;
			float num = 220f;
			float num2 = 130f;
			float num3 = Mathf.Max(num, (float)Screen.width - windowRect.x - 4f);
			float num4 = Mathf.Max(num2, (float)Screen.height - windowRect.y - 4f);
			windowRect.width = Mathf.Clamp(resizeStartSize.x + val.x, num, num3);
			windowRect.height = Mathf.Clamp(resizeStartSize.y + val.y, num2, num4);
			MarkLayoutDirty();
		}
	}

	private static Vector2 ScreenMousePosition()
	{
		return new Vector2(Input.mousePosition.x, (float)Screen.height - Input.mousePosition.y);
	}

	private void MarkLayoutDirty()
	{
		layoutDirty = true;
		saveLayoutAt = Time.unscaledTime + 0.65f;
	}

	private void SaveLayoutIfDue()
	{
		if (layoutDirty && !(Time.unscaledTime < saveLayoutAt))
		{
			layoutDirty = false;
			SanitizeWindowRect(recoverOffScreen: false);
			cfgWindowX.Value = windowRect.x;
			cfgWindowY.Value = windowRect.y;
			cfgWindowW.Value = (collapsed ? expandedWidth : windowRect.width);
			cfgWindowH.Value = (collapsed ? expandedHeight : windowRect.height);
			cfgCollapsed.Value = collapsed;
			((BaseUnityPlugin)this).Config.Save();
		}
	}

	private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();
	private void SanitizeWindowRect(bool recoverOffScreen)
	{
		_home.Begin(ref windowRect);
		float num = (collapsed ? CollapsedWindowWidth() : 220f);
		float num2 = (collapsed ? CollapsedWindowHeight() : 130f);
		float num3 = Mathf.Max(num, (float)Screen.width - 8f);
		float num4 = Mathf.Max(num2, (float)Screen.height - 8f);
		windowRect.width = (collapsed ? num : Mathf.Clamp(windowRect.width, num, num3));
		windowRect.height = (collapsed ? num2 : Mathf.Clamp(windowRect.height, num2, num4));
		if (!collapsed)
		{
			expandedWidth = windowRect.width;
			expandedHeight = windowRect.height;
		}
		if (recoverOffScreen)
		{
			float num5 = 64f;
			if (windowRect.x > (float)Screen.width - num5 || windowRect.x + windowRect.width < num5)
			{
				windowRect.x = Mathf.Max(4f, ((float)Screen.width - windowRect.width) * 0.5f);
			}
			if (windowRect.y > (float)Screen.height - 30f || windowRect.y < -4f)
			{
				windowRect.y = 4f;
			}
		}
		windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, (float)Screen.width - 64f));
		windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, (float)Screen.height - 28f));
		_home.End(ref windowRect);
	}

	private float CollapsedWindowWidth()
	{
		float num = AutomaticDpiScale() * Mathf.Clamp((cfgUserScale != null) ? cfgUserScale.Value : 1f, 0.5f, 2.5f);
		return Mathf.Clamp(300f * num, 240f, 600f);
	}

	private float CollapsedWindowHeight()
	{
		float num = AutomaticDpiScale() * Mathf.Clamp((cfgUserScale != null) ? cfgUserScale.Value : 1f, 0.5f, 2.5f);
		return Mathf.Clamp(34f * num, 28f, 76f);
	}

	private void ToggleCollapsed()
	{
		if (!collapsed)
		{
			expandedWidth = Mathf.Max(220f, windowRect.width);
			expandedHeight = Mathf.Max(130f, windowRect.height);
			collapsed = true;
			resizing = false;
			windowRect.width = CollapsedWindowWidth();
			windowRect.height = CollapsedWindowHeight();
		}
		else
		{
			collapsed = false;
			windowRect.width = Mathf.Clamp(expandedWidth, 220f, Mathf.Max(220f, (float)Screen.width - windowRect.x - 4f));
			windowRect.height = Mathf.Clamp(expandedHeight, 130f, Mathf.Max(130f, (float)Screen.height - windowRect.y - 4f));
		}
		cfgCollapsed.Value = collapsed;
		SanitizeWindowRect(recoverOffScreen: true);
		MarkLayoutDirty();
	}

	private float AutomaticDpiScale()
	{
		return Mathf.Clamp(ZeroT.UiKit.RlChrome.Dpi(windowRect.x, windowRect.y), 0.75f, 3f);
	}

	private float EffectiveUiScale()
	{
		float num = AutomaticDpiScale() * Mathf.Clamp(cfgUserScale.Value, 0.5f, 2.5f);
		if (collapsed)
		{
			return Mathf.Clamp(num, 0.55f, 3f);
		}
		float num2 = windowRect.width / 760f;
		float num3 = windowRect.height / 330f;
		float num4 = Mathf.Clamp(Mathf.Min(num2, num3), 0.45f, 1f);
		return Mathf.Clamp(num * num4, 0.45f, 3f);
	}

	private void EnsureStyles(float s)
	{
		if (!stylesReady || !(Mathf.Abs(lastStyleScale - s) < 0.03f))
		{
			lastStyleScale = s;
			stylesReady = true;
			titleStyle = new GUIStyle(GUI.skin.label)
			{
				fontStyle = (FontStyle)1,
				alignment = (TextAnchor)3,
				fontSize = Mathf.RoundToInt(17f * s)
			};
			headerStyle = new GUIStyle(GUI.skin.box)
			{
				fontStyle = (FontStyle)1,
				alignment = (TextAnchor)4,
				fontSize = Mathf.RoundToInt(12f * s)
			};
			cellStyle = new GUIStyle(GUI.skin.box)
			{
				alignment = (TextAnchor)4,
				fontSize = Mathf.RoundToInt(12f * s)
			};
			labelStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = (TextAnchor)3,
				fontSize = Mathf.RoundToInt(12f * s)
			};
			smallStyle = new GUIStyle(GUI.skin.label)
			{
				wordWrap = true,
				alignment = (TextAnchor)3,
				fontSize = Mathf.Max(8, Mathf.RoundToInt(10f * s))
			};
			rlButtonStyle = new GUIStyle(GUI.skin.button)
			{
				fontSize = Mathf.Max(8, Mathf.RoundToInt(12f * s))
			};
			resizeStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = (TextAnchor)8,
				fontStyle = (FontStyle)1,
				fontSize = Mathf.Max(10, Mathf.RoundToInt(18f * s))
			};
		}
	}

	private void OnGUI()
	{
		if (guiVisible && !shuttingDown)
		{
			SanitizeWindowRect(recoverOffScreen: false);
			Rect a = windowRect;
			windowRect = GUI.Window(14087031, windowRect, (GUI.WindowFunction)DrawDesktopWindow, string.Empty, GUIStyle.none);
			if (!Approximately(a, windowRect))
			{
				MarkLayoutDirty();
			}
		}
	}

	private static bool Approximately(Rect a, Rect b)
	{
		if (Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f && Mathf.Abs(a.width - b.width) < 0.01f)
		{
			return Mathf.Abs(a.height - b.height) < 0.01f;
		}
		return false;
	}

	private void DrawDesktopWindow(int id)
	{
		float num = EffectiveUiScale();
		EnsureStyles(num);
		float num2 = Mathf.Max(3f, 7f * num);
		float num3 = Mathf.Clamp(30f * num, 22f, 64f);
		float num4 = Mathf.Clamp(24f * num, 18f, 48f);
		ZeroT.UiKit.RlChrome.Backdrop(windowRect.width, windowRect.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(windowRect.width, collapsed ? "PenetrationCounter v0.8.3" : "PenetrationCounter v0.8.3", collapsed, num, labelStyle, rlButtonStyle, cfgActive.Value);
		if ((rlButtons & 1) != 0)
		{
			ChangeUiScale(-0.1f);
		}
		if ((rlButtons & 2) != 0)
		{
			ChangeUiScale(0.1f);
		}
		if ((rlButtons & 16) != 0)
		{
			SetActive(!cfgActive.Value);
		}
		if ((rlButtons & 4) != 0)
		{
			ToggleCollapsed();
		}
		if ((rlButtons & 8) != 0)
		{
			guiVisible = false;
		}
		if (collapsed)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 150f * num), 26f * num));
			return;
		}
		if (!cfgActive.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, windowRect.height, num));
			GUILayout.Label("PenetrationCounter is off. Press On to attach the counters again.", smallStyle, new GUILayoutOption[0]);
			GUILayout.EndArea();
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 150f * num), 26f * num));
			return;
		}
		GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, windowRect.height, num));
		GUILayout.BeginVertical(new GUILayoutOption[0]);
		List<CounterHost> list = GetCountersCached();
		if (list.Count == 0)
		{
			GUILayout.Space(num2);
			GUILayout.Label("Waiting for Person atoms...", labelStyle, new GUILayoutOption[0]);
			GUILayout.Label("The counter engine continues running even when this window is hidden. Press F8 to show/hide it.", smallStyle, new GUILayoutOption[0]);
			DrawFooter(num);
			GUILayout.EndVertical();
			GUILayout.EndArea();
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 150f * num), 26f * num));
			DrawResizeGrip(num);
			return;
		}
		selectedTracker = ClampIndex(selectedTracker, list.Count);
		CounterHost counterHost = list[selectedTracker];
		List<Atom> partners = GetPartnersCached(counterHost.atom);
		selectedPartner = ClampIndex(selectedPartner, Math.Max(1, partners.Count));
		GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.Height(num4) });
		if (GUILayout.Button("◀", new GUILayoutOption[1] { GUILayout.Width(num4) }))
		{
			selectedTracker = Wrap(selectedTracker - 1, list.Count);
			selectedPartner = 0;
		}
		GUILayout.Label("Tracker: " + ((Object)counterHost.atom).name, labelStyle, new GUILayoutOption[1] { GUILayout.MinWidth(80f) });
		if (GUILayout.Button("▶", new GUILayoutOption[1] { GUILayout.Width(num4) }))
		{
			selectedTracker = Wrap(selectedTracker + 1, list.Count);
			selectedPartner = 0;
		}
		GUILayout.Space(num2);
		if (partners.Count > 0)
		{
			if (GUILayout.Button("◀", new GUILayoutOption[1] { GUILayout.Width(num4) }))
			{
				selectedPartner = Wrap(selectedPartner - 1, partners.Count);
			}
			GUILayout.Label("Partner: " + ((Object)partners[selectedPartner]).name, labelStyle, new GUILayoutOption[1] { GUILayout.MinWidth(80f) });
			if (GUILayout.Button("▶", new GUILayoutOption[1] { GUILayout.Width(num4) }))
			{
				selectedPartner = Wrap(selectedPartner + 1, partners.Count);
			}
		}
		else
		{
			GUILayout.Label("Partner: none", labelStyle, new GUILayoutOption[0]);
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.Height(num4) });
		bool flag = GUILayout.Toggle(cfgDebugLog.Value, "Debug log", new GUILayoutOption[1] { GUILayout.Height(num4) });
		if (flag != cfgDebugLog.Value)
		{
			cfgDebugLog.Value = flag;
			ApplyDebugSettings();
			((BaseUnityPlugin)this).Config.Save();
		}
		bool flag2 = GUILayout.Toggle(cfgDebugGizmos.Value, "Debug gizmos", new GUILayoutOption[1] { GUILayout.Height(num4) });
		if (flag2 != cfgDebugGizmos.Value)
		{
			cfgDebugGizmos.Value = flag2;
			ApplyDebugSettings();
			((BaseUnityPlugin)this).Config.Save();
		}
		GUILayout.FlexibleSpace();
		if (GUILayout.Button(maximized ? "Restore size" : "Maximize", new GUILayoutOption[1] { GUILayout.Height(num4) }))
		{
			MaximizeOrRestore();
		}
		if (partners.Count > 0 && GUILayout.Button("Reset selected", new GUILayoutOption[1] { GUILayout.Height(num4) }))
		{
			ResetPartner(counterHost.script, ((Object)partners[selectedPartner]).name);
		}
		if (GUILayout.Button("Reset tracker", new GUILayoutOption[1] { GUILayout.Height(num4) }))
		{
			ResetTracker(counterHost);
		}
		GUILayout.EndHorizontal();
		if (partners.Count > 0)
		{
			tableScroll = GUILayout.BeginScrollView(tableScroll, false, true, new GUILayoutOption[1] { GUILayout.ExpandHeight(true) });
			DrawCounterTable(counterHost.script, ((Object)partners[selectedPartner]).name, num);
			GUILayout.EndScrollView();
		}
		else
		{
			GUILayout.Label("Add another Person to display partner statistics.", smallStyle, new GUILayoutOption[0]);
			GUILayout.FlexibleSpace();
		}
		DrawFooter(num);
		GUILayout.EndVertical();
		GUILayout.EndArea();
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 150f * num), 26f * num));
		DrawResizeGrip(num);
	}

	private void DrawFooter(float s)
	{
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label("F8 show/hide  •  S- / S+ scale  •  drag the title  •  arrow resizes  •  layout auto-save", smallStyle, new GUILayoutOption[0]);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private void DrawResizeGrip(float s)
	{
		Rect val = ZeroT.UiKit.RlChrome.HandleRect(windowRect.width, windowRect.height, s);
		GUI.Label(val, "↘", labelStyle);
		Event current = Event.current;
		if ((int)current.type == 0 && current.button == 0 && val.Contains(current.mousePosition))
		{
			resizing = true;
			resizeStartMouse = ScreenMousePosition();
			resizeStartSize = new Vector2(windowRect.width, windowRect.height);
			current.Use();
		}
	}

	private void ChangeUiScale(float amount)
	{
		cfgUserScale.Value = Mathf.Clamp(cfgUserScale.Value + amount, 0.5f, 2.5f);
		stylesReady = false;
		if (collapsed)
		{
			windowRect.width = CollapsedWindowWidth();
			windowRect.height = CollapsedWindowHeight();
		}
		SanitizeWindowRect(recoverOffScreen: true);
		MarkLayoutDirty();
		((BaseUnityPlugin)this).Config.Save();
	}

	private void MaximizeOrRestore()
	{
		if (!collapsed)
		{
			if (!maximized)
			{
				restoreRect = windowRect;
				windowRect = new Rect(4f, 4f, Mathf.Max(220f, (float)Screen.width - 8f), Mathf.Max(130f, (float)Screen.height - 8f));
				maximized = true;
			}
			else
			{
				windowRect = restoreRect;
				maximized = false;
				SanitizeWindowRect(recoverOffScreen: true);
			}
			MarkLayoutDirty();
		}
	}

	// The window runs its draw function twice per frame; rebuilding these LINQ lists (and scanning every atom) each time
	// allocated garbage and cost frame time. Refresh at most twice a second instead.
	private List<CounterHost> cachedCounters = new List<CounterHost>();

	private float cachedCountersUntil;

	private List<Atom> cachedPartners = new List<Atom>();

	private Atom cachedPartnersFor;

	private float cachedPartnersUntil;

	private List<CounterHost> GetCountersCached()
	{
		float now = Time.unscaledTime;
		if (now >= cachedCountersUntil)
		{
			cachedCountersUntil = now + 0.5f;
			cachedCounters = IEnumerableExtension.ToList<CounterHost>((IEnumerable<CounterHost>)(from h in counters.Values
				where h != null && (Object)(object)h.atom != (Object)null && (Object)(object)h.script != (Object)null
				orderby ((Object)h.atom).name
				select h));
		}
		return cachedCounters;
	}

	private List<Atom> GetPartnersCached(Atom tracker)
	{
		float now = Time.unscaledTime;
		if (now >= cachedPartnersUntil || (Object)(object)tracker != (Object)(object)cachedPartnersFor)
		{
			cachedPartnersUntil = now + 0.5f;
			cachedPartnersFor = tracker;
			cachedPartners = GetPartners(tracker);
		}
		return cachedPartners;
	}

	private List<Atom> GetPartners(Atom tracker)
	{
		SuperController singleton = SuperController.singleton;
		if ((Object)(object)singleton == (Object)null)
		{
			return new List<Atom>();
		}
		try
		{
			return IEnumerableExtension.ToList<Atom>((IEnumerable<Atom>)(from a in singleton.GetAtoms()
				where (Object)(object)a != (Object)null && a.type == "Person" && !a.destroyed && (Object)(object)a != (Object)(object)tracker
				orderby ((Object)a).name
				select a));
		}
		catch
		{
			return new List<Atom>();
		}
	}

	private void DrawCounterTable(PenetrationCounter script, string partnerName, float s)
	{
		bool flag = windowRect.width < 660f * Mathf.Clamp(s, 0.7f, 1.4f);
		string[] array = new string[6] { "Push", "Pull", "Soft", "Hard", "Deep", "Exit" };
		string[] array2 = ((!flag) ? array : new string[4] { "Push", "Pull", "Deep", "Exit" });
		float num = Mathf.Clamp(28f * s, 20f, 58f);
		GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.Height(num) });
		GUILayout.Box("Target", headerStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(Mathf.Clamp(82f * s, 58f, 160f)),
			GUILayout.Height(num)
		});
		string[] array3 = array2;
		for (int i = 0; i < array3.Length; i++)
		{
			GUILayout.Box(array3[i], headerStyle, new GUILayoutOption[2]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.Height(num)
			});
		}
		GUILayout.Box("Time", headerStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(Mathf.Clamp(112f * s, 72f, 210f)),
			GUILayout.Height(num)
		});
		GUILayout.EndHorizontal();
		array3 = new string[3] { "Vagina", "Anus", "Mouth" };
		foreach (string text in array3)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.Height(num) });
			GUILayout.Box(text, headerStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(Mathf.Clamp(82f * s, 58f, 160f)),
				GUILayout.Height(num)
			});
			string[] array4 = array2;
			foreach (string metric in array4)
			{
				GUILayout.Box(ReadCounter(script, partnerName, text, metric).ToString("0"), cellStyle, new GUILayoutOption[2]
				{
					GUILayout.ExpandWidth(true),
					GUILayout.Height(num)
				});
			}
			GUILayout.Box(ReadTime(script, partnerName, text), cellStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(Mathf.Clamp(112f * s, 72f, 210f)),
				GUILayout.Height(num)
			});
			GUILayout.EndHorizontal();
		}
		if (flag)
		{
			GUILayout.Label("Compact mode: Soft/Hard columns appear automatically when the window is wider.", smallStyle, new GUILayoutOption[0]);
		}
	}

	private static float ReadCounter(PenetrationCounter script, string partner, string kind, string metric)
	{
		try
		{
			JSONStorableFloat floatJSONParam = ((JSONStorable)script).GetFloatJSONParam("Count/" + partner + "/" + kind + "/" + metric);
			return (floatJSONParam != null) ? floatJSONParam.val : 0f;
		}
		catch
		{
			return 0f;
		}
	}

	private static string ReadTime(PenetrationCounter script, string partner, string kind)
	{
		try
		{
			JSONStorableString stringJSONParam = ((JSONStorable)script).GetStringJSONParam("Count/" + partner + "/" + kind + "/Time");
			return (stringJSONParam != null && !string.IsNullOrEmpty(stringJSONParam.val)) ? stringJSONParam.val : "00:00:00.00";
		}
		catch
		{
			return "00:00:00.00";
		}
	}

	private static void ResetPartner(PenetrationCounter script, string partner)
	{
		string[] array = new string[3] { "Vagina", "Anus", "Mouth" };
		foreach (string text in array)
		{
			string[] array2 = new string[6] { "Push", "Pull", "Soft", "Hard", "Deep", "Exit" };
			foreach (string text2 in array2)
			{
				try
				{
					JSONStorableFloat floatJSONParam = ((JSONStorable)script).GetFloatJSONParam("Count/" + partner + "/" + text + "/" + text2);
					if (floatJSONParam != null)
					{
						floatJSONParam.val = 0f;
					}
				}
				catch
				{
				}
			}
			try
			{
				string text3 = "Count/" + partner + "/" + text + "/Time";
				JSONStorableString stringJSONParam = ((JSONStorable)script).GetStringJSONParam(text3);
				if (stringJSONParam != null)
				{
					stringJSONParam.val = "00:00:00.00";
				}
				string text4 = text3 + "/(ms)";
				JSONStorableFloat floatJSONParam2 = ((JSONStorable)script).GetFloatJSONParam(text4);
				if (floatJSONParam2 != null)
				{
					floatJSONParam2.val = 0f;
				}
				if (script.instime != null && script.instime.ContainsKey(text4))
				{
					script.instime[text4].val = 0f;
				}
			}
			catch
			{
			}
		}
	}

	private void ResetTracker(CounterHost host)
	{
		if (host == null || (Object)(object)host.script == (Object)null || (Object)(object)host.atom == (Object)null)
		{
			return;
		}
		foreach (Atom partner in GetPartners(host.atom))
		{
			ResetPartner(host.script, ((Object)partner).name);
		}
	}

	private static int ClampIndex(int index, int count)
	{
		if (count <= 0)
		{
			return 0;
		}
		return Mathf.Clamp(index, 0, count - 1);
	}

	private static int Wrap(int value, int count)
	{
		if (count <= 0)
		{
			return 0;
		}
		value %= count;
		if (value < 0)
		{
			value += count;
		}
		return value;
	}

	private void TryHookSuperController()
	{
		SuperController singleton = SuperController.singleton;
		if (singleton != null && !((Object)(object)singleton == (Object)null) && singleton != hookedController)
		{
			UnhookSuperController();
			hookedController = singleton;
			SuperController val = hookedController;
			val.onAtomAddedHandlers = (SuperController.OnAtomAdded)Delegate.Combine((Delegate)(object)val.onAtomAddedHandlers, (Delegate)new SuperController.OnAtomAdded(OnAtomAdded));
			SuperController val2 = hookedController;
			val2.onAtomRemovedHandlers = (SuperController.OnAtomRemoved)Delegate.Combine((Delegate)(object)val2.onAtomRemovedHandlers, (Delegate)new SuperController.OnAtomRemoved(OnAtomRemoved));
			nextReconcileAt = 0f;
		}
	}

	private void UnhookSuperController()
	{
		if (hookedController == null || (Object)(object)hookedController == (Object)null)
		{
			hookedController = null;
			return;
		}
		try
		{
			SuperController val = hookedController;
			val.onAtomAddedHandlers = (SuperController.OnAtomAdded)Delegate.Remove((Delegate)(object)val.onAtomAddedHandlers, (Delegate)new SuperController.OnAtomAdded(OnAtomAdded));
		}
		catch
		{
		}
		try
		{
			SuperController val2 = hookedController;
			val2.onAtomRemovedHandlers = (SuperController.OnAtomRemoved)Delegate.Remove((Delegate)(object)val2.onAtomRemovedHandlers, (Delegate)new SuperController.OnAtomRemoved(OnAtomRemoved));
		}
		catch
		{
		}
		hookedController = null;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		nextReconcileAt = 0f;
		SanitizeWindowRect(recoverOffScreen: true);
	}

	private void OnAtomAdded(Atom atom)
	{
		if ((Object)(object)atom != (Object)null && atom.type == "Person")
		{
			nextReconcileAt = 0f;
		}
	}

	private void OnAtomRemoved(Atom atom)
	{
		if (!((Object)(object)atom == (Object)null))
		{
			int instanceID = ((Object)atom).GetInstanceID();
			if (counters.TryGetValue(instanceID, out var value))
			{
				counters.Remove(instanceID);
				DestroyCounter(value);
			}
		}
	}

	private void ReconcilePeople()
	{
		SuperController singleton = SuperController.singleton;
		if (singleton == null || (Object)(object)singleton == (Object)null || singleton.isLoading)
		{
			return;
		}
		List<Atom> list;
		try
		{
			list = IEnumerableExtension.ToList<Atom>(from a in singleton.GetAtoms()
				where (Object)(object)a != (Object)null && a.type == "Person" && !a.destroyed
				select a);
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Could not enumerate Person atoms: " + ex.Message));
			return;
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Atom item in list)
		{
			int instanceID = ((Object)item).GetInstanceID();
			hashSet.Add(instanceID);
			if (counters.TryGetValue(instanceID, out var value))
			{
				if ((Object)(object)value.script == (Object)null || (Object)(object)value.hostObject == (Object)null)
				{
					counters.Remove(instanceID);
					DestroyCounter(value);
				}
				else if (HasOtherPenetrationCounter(item, value.script))
				{
					counters.Remove(instanceID);
					DestroyCounter(value);
				}
			}
			else if (!HasOtherPenetrationCounter(item, null))
			{
				TryAttach(item);
			}
		}
		foreach (int item2 in IEnumerableExtension.ToList<int>((IEnumerable<int>)counters.Keys))
		{
			if (!hashSet.Contains(item2))
			{
				CounterHost host = counters[item2];
				counters.Remove(item2);
				DestroyCounter(host);
			}
		}
	}

	private bool HasOtherPenetrationCounter(Atom atom, PenetrationCounter except)
	{
		try
		{
			foreach (string storableID in atom.GetStorableIDs())
			{
				JSONStorable storableByID = atom.GetStorableByID(storableID);
				if (!((Object)(object)storableByID == (Object)null) && (object)storableByID != except)
				{
					Type type = ((object)storableByID).GetType();
					if (type != null && type.Name == "PenetrationCounter")
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private bool ManagerReady(MVRPluginManager manager)
	{
		if ((Object)(object)manager != (Object)null && (Object)(object)manager.pluginContainer != (Object)null && (Object)(object)manager.scriptUIPrefab != (Object)null && (Object)(object)manager.configurableSpacerPrefab != (Object)null && (Object)(object)manager.configurableSliderPrefab != (Object)null && (Object)(object)manager.configurableTogglePrefab != (Object)null && (Object)(object)manager.configurableColorPickerPrefab != (Object)null && (Object)(object)manager.configurableButtonPrefab != (Object)null && (Object)(object)manager.configurablePopupPrefab != (Object)null && (Object)(object)manager.configurableScrollablePopupPrefab != (Object)null && (Object)(object)manager.configurableFilterablePopupPrefab != (Object)null)
		{
			return (Object)(object)manager.configurableTextFieldPrefab != (Object)null;
		}
		return false;
	}

	private void TryAttach(Atom atom)
	{
		CounterHost counterHost = null;
		try
		{
			MVRPluginManager val = ((Component)atom).GetComponentsInChildren<MVRPluginManager>(true).FirstOrDefault();
			if (ManagerReady(val))
			{
				int instanceID = ((Object)atom).GetInstanceID();
				counterHost = new CounterHost
				{
					atom = atom,
					manager = val
				};
				GameObject val2 = (counterHost.hostObject = new GameObject("PenetrationCounter.BepInEx." + instanceID));
				val2.transform.SetParent(val.pluginContainer, false);
				val2.transform.localPosition = Vector3.zero;
				val2.transform.localRotation = Quaternion.identity;
				val2.transform.localScale = Vector3.one;
				PenetrationCounter penetrationCounter = (counterHost.script = val2.AddComponent<PenetrationCounter>());
				((MVRScript)penetrationCounter).ForceAwake();
				((JSONStorable)penetrationCounter).containingAtom = atom;
				((MVRScript)penetrationCounter).manager = val;
				((JSONStorable)penetrationCounter).exclude = true;
				((JSONStorable)penetrationCounter).onlyStoreIfActive = false;
				((JSONStorable)penetrationCounter).overrideId = "PenetrationCounter.BepInEx." + instanceID;
				if (((MVRScript)penetrationCounter).pluginLabelJSON != null)
				{
					((MVRScript)penetrationCounter).pluginLabelJSON.val = "PenetrationCounter BepInEx";
				}
				((MVRScript)penetrationCounter).Init();
				Transform val3 = (counterHost.hiddenUI = Object.Instantiate<Transform>(val.scriptUIPrefab));
				if ((Object)(object)val.scriptUIParent != (Object)null)
				{
					val3.SetParent((Transform)(object)val.scriptUIParent, false);
				}
				((Component)val3).gameObject.SetActive(false);
				((JSONStorable)penetrationCounter).UITransform = val3;
				((JSONStorable)penetrationCounter).InitUI();
				Canvas val4 = (counterHost.canvas = ((Component)val3).GetComponentInChildren<Canvas>(true));
				if ((Object)(object)val4 != (Object)null && (Object)(object)SuperController.singleton != (Object)null)
				{
					SuperController.singleton.AddCanvas(val4);
				}
				if (!atom.RegisterAdditionalStorable((JSONStorable)(object)penetrationCounter))
				{
					throw new InvalidOperationException("VaM rejected the additional PenetrationCounter storable.");
				}
				((Behaviour)penetrationCounter).enabled = false;
				((Behaviour)penetrationCounter).enabled = true;
				penetrationCounter.setDebugLog(cfgDebugLog.Value);
				penetrationCounter.setDebugGizmo(cfgDebugGizmos.Value);
				counters[instanceID] = counterHost;
				Logger.LogInfo((object)("PenetrationCounter attached automatically to Person '" + atom.uid + "'."));
			}
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("Failed to attach PenetrationCounter to Person '" + (((Object)(object)atom != (Object)null) ? atom.uid : "<null>") + "': " + ex));
			DestroyCounter(counterHost);
		}
	}

	private void SetActive(bool on)
	{
		cfgActive.Value = on;
		if (on)
		{
			nextReconcileAt = 0f;
		}
		else
		{
			foreach (CounterHost item in IEnumerableExtension.ToList<CounterHost>((IEnumerable<CounterHost>)counters.Values))
			{
				DestroyCounter(item);
			}
			counters.Clear();
			cachedCountersUntil = 0f;
		}
		((BaseUnityPlugin)this).Config.Save();
	}

	private void DestroyCounter(CounterHost host)
	{
		if (host == null)
		{
			return;
		}
		try
		{
			if ((Object)(object)host.atom != (Object)null && (Object)(object)host.script != (Object)null)
			{
				host.atom.UnregisterAdditionalStorable((JSONStorable)(object)host.script);
			}
		}
		catch
		{
		}
		try
		{
			if ((Object)(object)host.canvas != (Object)null && (Object)(object)SuperController.singleton != (Object)null)
			{
				SuperController.singleton.RemoveCanvas(host.canvas);
			}
		}
		catch
		{
		}
		try
		{
			if ((Object)(object)host.hiddenUI != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)host.hiddenUI).gameObject);
			}
		}
		catch
		{
		}
		try
		{
			if ((Object)(object)host.hostObject != (Object)null)
			{
				Object.Destroy((Object)(object)host.hostObject);
			}
		}
		catch
		{
		}
	}

	private void OnDestroy()
	{
		shuttingDown = true;
		SaveLayoutIfDue();
		if (layoutDirty)
		{
			layoutDirty = false;
			cfgWindowX.Value = windowRect.x;
			cfgWindowY.Value = windowRect.y;
			cfgWindowW.Value = (collapsed ? expandedWidth : windowRect.width);
			cfgWindowH.Value = (collapsed ? expandedHeight : windowRect.height);
			cfgCollapsed.Value = collapsed;
			((BaseUnityPlugin)this).Config.Save();
		}
		SceneManager.sceneLoaded -= OnSceneLoaded;
		UnhookSuperController();
		foreach (CounterHost item in IEnumerableExtension.ToList<CounterHost>((IEnumerable<CounterHost>)counters.Values))
		{
			DestroyCounter(item);
		}
		counters.Clear();
	}
}
