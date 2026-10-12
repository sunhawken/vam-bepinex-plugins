using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

// ZeroT.FastMode - one-key performance mode for heavy Person scenes, with the same HUD window chrome as the other
// ZeroT standalone plugins (Title row with On/Off, S-, S+, collapse, x; drag by the title; resize arrow; remembered
// position/size/scale/collapsed state; monitor-DPI scaling; optional separate Windows window).
//
// What "On" does (all reversible, measured by in-process A/B on a 3-Person scene):
//   * Person mesh/skin pipeline updates every Nth frame  (N=2: about -3 ms, N=3: about -4.5 ms)
//   * Rigidbody interpolation off                          (about -2 ms)
//   * Soft-body (jiggle) physics off  [optional]           (about -4 ms)
[BepInPlugin("zerot.vamfastmode", "ZeroT VaM FastMode", "3.0.0")]
public class FastMode : BaseUnityPlugin
{
	private ConfigEntry<KeyCode> cfgHotkey, cfgWindowKey;
	private ConfigEntry<bool> cfgActive, cfgStartOn, cfgSoftOff, cfgNoInterp, cfgShow, cfgCollapsed, cfgDpiAware;
	private ConfigEntry<int> cfgEvery;
	private ConfigEntry<float> cfgX, cfgY, cfgW, cfgH, cfgScale;

	private bool active;
	private float nextSweep;
	private readonly List<Behaviour> softDisabled = new List<Behaviour>();
	private readonly Dictionary<Rigidbody, RigidbodyInterpolation> interp = new Dictionary<Rigidbody, RigidbodyInterpolation>();
	private DAZCharacterRun[] runs;

	// window state
	private Rect windowRect;
	private bool collapsed, resizing, uiDirty;
	private float userScale = 1f, styleScale = -1f;
	private GUIStyle windowStyle, labelStyle, smallLabelStyle, buttonStyle, toggleStyle, boxStyle;
	private ZeroT.UiKit.RlImguiExternal rlExt;
	private readonly ZeroT.UiKit.RlChrome.WindowHome home = new ZeroT.UiKit.RlChrome.WindowHome();
	private const int WindowId = 1381126301;

	// live readout
	private float smoothDt = 0.02f;
	private int personCount;
	private float nextPersonScan;

	private void Awake()
	{
		cfgHotkey = Config.Bind("General", "Hotkey", KeyCode.F9, "Toggles Fast Mode on/off.");
		cfgWindowKey = Config.Bind("General", "WindowHotkey", KeyCode.F10, "Shows/hides the FastMode window.");
		cfgStartOn = Config.Bind("General", "StartEnabled", false, "Turn Fast Mode on automatically once a scene has loaded.");
		cfgActive = Config.Bind("General", "Active", false, "Current on/off state (also toggled by the window's On/Off button).");
		cfgEvery = Config.Bind("Fast", "UpdatePersonsEveryNthFrame", 2, "Update Person mesh/skin pipeline every Nth frame (1 = off).");
		cfgNoInterp = Config.Bind("Fast", "DisableRigidbodyInterpolation", true, "Disable interpolation on dynamic rigidbodies.");
		cfgSoftOff = Config.Bind("Fast", "DisableSoftBodyPhysics", false, "Also turn off soft-body (jiggle) physics components.");
		cfgShow = Config.Bind("Window", "Show", true, "Show the FastMode window.");
		cfgCollapsed = Config.Bind("Window", "Collapsed", false, "Whether the window is collapsed.");
		cfgX = Config.Bind("Window", "X", 24f, "Window X position in VaM display pixels.");
		cfgY = Config.Bind("Window", "Y", 120f, "Window Y position in VaM display pixels.");
		cfgW = Config.Bind("Window", "Width", 340f, "Expanded window width.");
		cfgH = Config.Bind("Window", "Height", 300f, "Expanded window height.");
		cfgScale = Config.Bind("Window", "UserScale", 1f, "Manual UI scale multiplied by the display DPI scale.");
		cfgDpiAware = Config.Bind("Window", "DpiAware", true, "Scale the window by the monitor DPI.");
		windowRect = new Rect(cfgX.Value, cfgY.Value, Mathf.Max(260f, cfgW.Value), Mathf.Max(150f, cfgH.Value));
		userScale = Mathf.Clamp(cfgScale.Value, 0.3f, 2.25f);
		collapsed = cfgCollapsed.Value;
		active = cfgStartOn.Value;
		cfgActive.Value = active;
	}

	// ---------------------------------------------------------------- behaviour
	private void Update()
	{
		var sc = SuperController.singleton;
		if (sc == null) return;
		smoothDt = Mathf.Lerp(smoothDt, Time.unscaledDeltaTime, 0.05f);
		if (Input.GetKeyDown(cfgHotkey.Value)) SetActive(!active, true);
		if (Input.GetKeyDown(cfgWindowKey.Value)) { cfgShow.Value = !cfgShow.Value; Config.Save(); }
		if (cfgActive.Value != active) SetActive(cfgActive.Value, false);   // changed through the config file / manager
		if (sc.isLoading) { runs = null; return; }
		if (Time.realtimeSinceStartup > nextPersonScan)
		{
			nextPersonScan = Time.realtimeSinceStartup + 2f;
			int n = 0; foreach (var a in sc.GetAtoms()) if (a != null && a.type == "Person") n++;
			personCount = n;
		}
		if (!active) return;
		int every = Mathf.Max(1, cfgEvery.Value);
		if (every > 1)
		{
			if (runs == null) runs = FindObjectsOfType<DAZCharacterRun>();
			bool on = Time.frameCount % every == 0;
			foreach (var r in runs) if (r != null) r.doUpdate = on;
		}
		else if (runs != null) { foreach (var r in runs) if (r != null) r.doUpdate = true; runs = null; }
		if (Time.realtimeSinceStartup < nextSweep) return;
		nextSweep = Time.realtimeSinceStartup + 3f;
		if (cfgSoftOff.Value)
		{
			foreach (var mb in FindObjectsOfType<DAZPhysicsMesh>()) { if (mb.enabled) { mb.enabled = false; softDisabled.Add(mb); } }
		}
		else RestoreSoft();
		if (cfgNoInterp.Value)
		{
			foreach (var r in FindObjectsOfType<Rigidbody>())
			{
				if (!r.isKinematic && r.interpolation != RigidbodyInterpolation.None && r.gameObject.activeInHierarchy) { interp[r] = r.interpolation; r.interpolation = RigidbodyInterpolation.None; }
			}
		}
		else RestoreInterp();
	}

	private void RestoreSoft()
	{
		foreach (var b in softDisabled) if (b != null) b.enabled = true;
		softDisabled.Clear();
	}

	private void RestoreInterp()
	{
		foreach (var kv in interp) if (kv.Key != null) kv.Key.interpolation = kv.Value;
		interp.Clear();
	}

	private void SetActive(bool on, bool announce)
	{
		active = on; cfgActive.Value = on; nextSweep = 0; runs = null;
		if (!on)
		{
			RestoreSoft(); RestoreInterp();
			foreach (var r in FindObjectsOfType<DAZCharacterRun>()) r.doUpdate = true;
		}
		Config.Save();
		if (announce && SuperController.singleton != null) SuperController.LogMessage("FastMode " + (on ? "ON" : "OFF"));
	}

	// Applies a changed option immediately while Fast Mode is on.
	private void OptionChanged()
	{
		nextSweep = 0; runs = null;
		if (!cfgSoftOff.Value) RestoreSoft();
		if (!cfgNoInterp.Value) RestoreInterp();
		if (cfgEvery.Value <= 1) foreach (var r in FindObjectsOfType<DAZCharacterRun>()) r.doUpdate = true;
		Config.Save();
	}

	// ---------------------------------------------------------------- window
	private float DpiScale()
	{
		return cfgDpiAware.Value ? Mathf.Clamp(ZeroT.UiKit.RlChrome.Dpi(windowRect.x, windowRect.y), 0.75f, 2.5f) : 1f;
	}

	private float EffectiveScale()
	{
		return Mathf.Clamp(userScale * DpiScale(), 0.3f, 3f);
	}

	private float CollapsedHeight(float scale)
	{
		return 34f * scale;
	}

	private void DrawExternal(float w, float h)
	{
		Rect saved = windowRect;
		windowRect = new Rect(0f, 0f, w, h);
		try { GUI.Window(WindowId, windowRect, DrawWindow, "", GUIStyle.none); }
		finally { windowRect = saved; }
	}

	private void OnGUI()
	{
		if (Screen.width <= 0 || Screen.height <= 0 || !cfgShow.Value) return;
		float scale = EffectiveScale();
		EnsureStyles(scale);
		if (rlExt == null) rlExt = new ZeroT.UiKit.RlImguiExternal(this, "Window", "FastMode");
		if (rlExt.Run(DrawExternal)) return;
		ClampWindow(scale);
		Rect r = collapsed ? new Rect(windowRect.x, windowRect.y, windowRect.width, CollapsedHeight(scale)) : windowRect;
		float ox = r.x, oy = r.y;
		Rect r2 = GUI.Window(WindowId, r, DrawWindow, string.Empty, GUIStyle.none);
		windowRect.x = r2.x; windowRect.y = r2.y;
		if (Mathf.Abs(ox - r2.x) > 0.01f || Mathf.Abs(oy - r2.y) > 0.01f) uiDirty = true;
		ClampWindow(scale);
		if (Event.current.type == EventType.MouseUp && uiDirty) SaveUiState();
	}

	private void EnsureStyles(float scale)
	{
		if (windowStyle != null && Mathf.Abs(styleScale - scale) < 0.02f) return;
		styleScale = scale;
		int fs = Mathf.Max(10, Mathf.RoundToInt(12f * scale));
		int fs2 = Mathf.Max(9, Mathf.RoundToInt(10f * scale));
		windowStyle = new GUIStyle(GUI.skin.window); windowStyle.fontSize = fs;
		labelStyle = new GUIStyle(GUI.skin.label); labelStyle.fontSize = fs; labelStyle.wordWrap = true;
		smallLabelStyle = new GUIStyle(GUI.skin.label); smallLabelStyle.fontSize = fs2; smallLabelStyle.wordWrap = true;
		buttonStyle = new GUIStyle(GUI.skin.button); buttonStyle.fontSize = fs;
		toggleStyle = new GUIStyle(GUI.skin.toggle); toggleStyle.fontSize = fs;
		boxStyle = new GUIStyle(GUI.skin.box); boxStyle.fontSize = fs; boxStyle.alignment = TextAnchor.UpperLeft; boxStyle.wordWrap = true;
	}

	private void DrawWindow(int id)
	{
		float s = EffectiveScale();
		float row = 24f * s;
		float h = collapsed ? CollapsedHeight(s) : windowRect.height;
		ZeroT.UiKit.RlChrome.Backdrop(windowRect.width, h);
		int b = ZeroT.UiKit.RlChrome.TitleRow(windowRect.width, "FastMode " + (active ? "(ON)" : "(off)"), collapsed, s, labelStyle, buttonStyle, active);
		if ((b & ZeroT.UiKit.RlChrome.Smaller) != 0) ChangeUserScale(-0.1f);
		if ((b & ZeroT.UiKit.RlChrome.Larger) != 0) ChangeUserScale(0.1f);
		if ((b & ZeroT.UiKit.RlChrome.Power) != 0) { SetActive(!active, false); uiDirty = true; }
		if ((b & ZeroT.UiKit.RlChrome.Collapse) != 0) { collapsed = !collapsed; uiDirty = true; SaveUiState(); }
		if ((b & ZeroT.UiKit.RlChrome.Close) != 0) { cfgShow.Value = false; uiDirty = true; SaveUiState(); }
		if (!collapsed)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, h, s));
			DrawBody(s, row);
			GUILayout.EndArea();
			DrawResizeHandle(s);
		}
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, windowRect.width - 118f * s), 26f * s));
	}

	private void DrawBody(float s, float row)
	{
		GUILayout.Space(4f * s);
		GUILayout.BeginHorizontal();
		GUILayout.Label("Persons update every", labelStyle, GUILayout.Width(150f * s));
		for (int n = 1; n <= 4; n++)
		{
			bool sel = cfgEvery.Value == n;
			GUI.backgroundColor = sel ? new Color(0.3f, 0.85f, 0.4f) : Color.white;
			if (GUILayout.Button(n == 1 ? "1 (off)" : n.ToString(), buttonStyle, GUILayout.Height(row)) && !sel) { cfgEvery.Value = n; OptionChanged(); }
		}
		GUI.backgroundColor = Color.white;
		GUILayout.Label("frame", labelStyle, GUILayout.Width(44f * s));
		GUILayout.EndHorizontal();
		GUILayout.Space(2f * s);
		bool ni = GUILayout.Toggle(cfgNoInterp.Value, "Rigidbody interpolation off  (about -2 ms)", toggleStyle, GUILayout.Height(row));
		if (ni != cfgNoInterp.Value) { cfgNoInterp.Value = ni; OptionChanged(); }
		bool so = GUILayout.Toggle(cfgSoftOff.Value, "Soft-body jiggle off  (about -4 ms)", toggleStyle, GUILayout.Height(row));
		if (so != cfgSoftOff.Value) { cfgSoftOff.Value = so; OptionChanged(); }
		bool st = GUILayout.Toggle(cfgStartOn.Value, "Turn on automatically after scene load", toggleStyle, GUILayout.Height(row));
		if (st != cfgStartOn.Value) { cfgStartOn.Value = st; Config.Save(); }
		GUILayout.Space(4f * s);
		float fps = smoothDt > 0.0001f ? 1f / smoothDt : 0f;
		GUILayout.Box("Now: " + fps.ToString("0") + " fps  (" + (smoothDt * 1000f).ToString("0.0") + " ms)    Persons: " + personCount
			+ "\n" + (active ? "ON" : "off") + "   keys: " + cfgHotkey.Value + " on/off, " + cfgWindowKey.Value + " window", boxStyle, GUILayout.ExpandWidth(true));
	}

	private void DrawResizeHandle(float scale)
	{
		Rect val = ZeroT.UiKit.RlChrome.HandleRect(windowRect.width, windowRect.height, scale);
		GUI.Label(val, "↘", labelStyle);
		Event current = Event.current;
		int controlID = GUIUtility.GetControlID(1381127101, FocusType.Passive);
		EventType t = current.GetTypeForControl(controlID);
		if (t == EventType.MouseDown && val.Contains(current.mousePosition) && current.button == 0)
		{
			resizing = true; GUIUtility.hotControl = controlID; current.Use();
		}
		else if (t == EventType.MouseDrag && resizing && GUIUtility.hotControl == controlID)
		{
			float minW = Mathf.Min((float)Screen.width, 300f * scale);
			float minH = Mathf.Min((float)Screen.height, 200f * scale);
			float maxW = Mathf.Max(minW, (float)Screen.width - windowRect.x - 2f);
			float maxH = Mathf.Max(minH, (float)Screen.height - windowRect.y - 2f);
			windowRect.width = Mathf.Clamp(windowRect.width + current.delta.x, minW, maxW);
			windowRect.height = Mathf.Clamp(windowRect.height + current.delta.y, minH, maxH);
			uiDirty = true; current.Use();
		}
		else if (t == EventType.MouseUp && resizing && GUIUtility.hotControl == controlID)
		{
			resizing = false; GUIUtility.hotControl = 0; uiDirty = true; current.Use(); SaveUiState();
		}
	}

	private void ChangeUserScale(float delta)
	{
		float before = EffectiveScale();
		float ns = Mathf.Clamp(userScale + delta, 0.3f, 2.25f);
		if (Mathf.Abs(ns - userScale) < 0.001f) return;
		userScale = ns;
		float k = EffectiveScale() / before;
		windowRect.width *= k; windowRect.height *= k;
		styleScale = -1f; uiDirty = true;
		ClampWindow(EffectiveScale());
		SaveUiState();
	}

	private void ClampWindow(float scale)
	{
		home.Begin(ref windowRect);
		float m = 2f;
		float maxW = Mathf.Max(40f, (float)Screen.width - m * 2f);
		float maxH = Mathf.Max(40f, (float)Screen.height - m * 2f);
		float minW = Mathf.Min(maxW, 300f * scale);
		float minH = Mathf.Min(maxH, 200f * scale);
		windowRect.width = Mathf.Clamp(windowRect.width, minW, maxW);
		windowRect.height = Mathf.Clamp(windowRect.height, minH, maxH);
		float hh = collapsed ? Mathf.Min(CollapsedHeight(scale), maxH) : windowRect.height;
		windowRect.x = Mathf.Clamp(windowRect.x, m, Mathf.Max(m, (float)Screen.width - windowRect.width - m));
		windowRect.y = Mathf.Clamp(windowRect.y, m, Mathf.Max(m, (float)Screen.height - hh - m));
		home.End(ref windowRect);
	}

	private void SaveUiState()
	{
		cfgX.Value = windowRect.x; cfgY.Value = windowRect.y;
		cfgW.Value = windowRect.width; cfgH.Value = windowRect.height;
		cfgScale.Value = userScale; cfgCollapsed.Value = collapsed;
		Config.Save();
		uiDirty = false;
	}

	private void OnDestroy()
	{
		try { SaveUiState(); } catch { }
	}
}
