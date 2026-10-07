using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ProceduralSlaps;

[BepInPlugin("ridgerock.proceduralslaps.standalone", "RidgeRock Procedural Slaps Standalone", "0.3.0")]
public sealed class Plugin : BaseUnityPlugin
{
	private readonly List<Atom> femaleAtoms = new List<Atom>();

	private readonly Dictionary<string, TargetRuntime> runtimes = new Dictionary<string, TargetRuntime>();

	private readonly Dictionary<string, TargetProfile> profiles = new Dictionary<string, TargetProfile>();

	private readonly List<string> staleUids = new List<string>();

	private readonly Dictionary<string, Atom> currentFemaleByUid = new Dictionary<string, Atom>();

	private readonly Settings fallbackSettings = new Settings();

	private TargetRuntime selectedRuntime;

	private TargetProfile selectedProfile;

	private float targetRefreshAt;

	private bool booted;

	private ConfigEntry<float> cfgX;

	private ConfigEntry<float> cfgY;

	private ConfigEntry<float> cfgW;

	private ConfigEntry<float> cfgH;

	private ConfigEntry<float> cfgScale;

	private ConfigEntry<bool> cfgShow;

	private ConfigEntry<bool> cfgActive;

	private ConfigEntry<bool> cfgCollapsed;

	private ConfigEntry<string> cfgTargetUid;

	private Rect windowRect;

	private Vector2 scroll;

	private bool collapsed;

	private string targetInput = string.Empty;

	private bool uiDirty;

	private bool resizing;

	private bool showLevels;

	private float userScale = 1f;

	private float lastDpiScale = -1f;

	private float styleScale = -1f;

	private GUIStyle windowStyle;

	private GUIStyle labelStyle;

	private GUIStyle smallLabelStyle;

	private GUIStyle buttonStyle;

	private GUIStyle textStyle;

	private GUIStyle boxStyle;

	private GUIStyle toggleStyle;

	private GUIStyle sliderStyle;

	private GUIStyle sliderThumbStyle;

	private const int WindowId = 1381126227;

	public Settings Configuration => (selectedProfile != null) ? selectedProfile.Settings : fallbackSettings;

	private void Awake()
	{
		cfgX = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "X", 24f, "Window X position in VaM display pixels.");
		cfgY = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Y", 60f, "Window Y position in VaM display pixels.");
		cfgW = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Width", 470f, "Expanded window width in VaM display pixels.");
		cfgH = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Height", 720f, "Expanded window height in VaM display pixels.");
		cfgScale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "UserScale", 1f, "Manual UI scale multiplied by detected display DPI scale.");
		cfgActive = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Active", true, "Turn Procedural Slaps off completely: removes all per-person sensors and renderers and stops updating.");
		cfgShow = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Show", true, "Show the in-game Procedural Slaps window.");
		cfgCollapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Collapsed", false, "Whether the standalone window is collapsed.");
		cfgTargetUid = ((BaseUnityPlugin)this).Config.Bind<string>("Target", "UID", string.Empty, "Female Person atom UID selected for editing in the UI. All female Persons are targeted automatically.");
		windowRect = new Rect(cfgX.Value, cfgY.Value, Mathf.Max(260f, cfgW.Value), Mathf.Max(180f, cfgH.Value));
		userScale = Mathf.Clamp(cfgScale.Value, 0.55f, 2.25f);
		collapsed = cfgCollapsed.Value;
		targetInput = cfgTargetUid.Value ?? string.Empty;
		fallbackSettings.Validate();
		Logger.LogInfo((object)"Procedural Slaps standalone loaded; automatic all-female scene targeting enabled.");
	}

	private void DisposeRuntimes()
	{
		foreach (TargetRuntime value in runtimes.Values)
		{
			value.Dispose();
		}
		runtimes.Clear();
		selectedRuntime = null;
		selectedProfile = null;
		targetRefreshAt = 0f;
	}

	private void Update()
	{
		if (!cfgActive.Value)
		{
			if (runtimes.Count > 0)
			{
				DisposeRuntimes();
			}
			return;
		}
		if (!((Object)(object)SuperController.singleton == (Object)null))
		{
			if (!booted)
			{
				booted = true;
				RefreshTargets(immediate: true);
			}
			if (Time.unscaledTime >= targetRefreshAt)
			{
				targetRefreshAt = Time.unscaledTime + 1f;
				RefreshTargets(immediate: false);
			}
		}
	}

	private void LateUpdate()
	{
		if (!booted || !cfgActive.Value)
		{
			return;
		}
		for (int i = 0; i < femaleAtoms.Count; i++)
		{
			Atom val = femaleAtoms[i];
			if (!((Object)(object)val == (Object)null) && runtimes.TryGetValue(val.uid, out var value))
			{
				value.Tick();
			}
		}
	}

	private void RefreshTargets(bool immediate)
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		femaleAtoms.Clear();
		currentFemaleByUid.Clear();
		List<Atom> atoms = SuperController.singleton.GetAtoms();
		for (int i = 0; i < atoms.Count; i++)
		{
			Atom val = atoms[i];
			if (IsFemalePerson(val) && !string.IsNullOrEmpty(val.uid))
			{
				femaleAtoms.Add(val);
				currentFemaleByUid[val.uid] = val;
			}
		}
		femaleAtoms.Sort((Atom a, Atom b) => string.Compare(a.uid, b.uid, StringComparison.OrdinalIgnoreCase));
		staleUids.Clear();
		foreach (KeyValuePair<string, TargetRuntime> runtime in runtimes)
		{
			if (!currentFemaleByUid.TryGetValue(runtime.Key, out var value) || (Object)(object)value != (Object)(object)runtime.Value.TargetAtom)
			{
				staleUids.Add(runtime.Key);
			}
		}
		for (int num = 0; num < staleUids.Count; num++)
		{
			string key = staleUids[num];
			if (runtimes.TryGetValue(key, out var value2))
			{
				value2.Dispose();
			}
			runtimes.Remove(key);
		}
		for (int num2 = 0; num2 < femaleAtoms.Count; num2++)
		{
			Atom val2 = femaleAtoms[num2];
			if (!runtimes.TryGetValue(val2.uid, out var value3))
			{
				value3 = new TargetRuntime(this, val2, GetProfile(val2.uid));
				runtimes.Add(val2.uid, value3);
				Logger.LogInfo((object)("Procedural Slaps auto-target added: " + val2.uid));
			}
		}
		string text = ((selectedRuntime == null || !((Object)(object)selectedRuntime.TargetAtom != (Object)null)) ? (cfgTargetUid.Value ?? string.Empty) : selectedRuntime.TargetAtom.uid);
		if (!string.IsNullOrEmpty(text) && runtimes.TryGetValue(text, out var value4))
		{
			SelectRuntime(value4, save: false);
		}
		else if (femaleAtoms.Count > 0 && runtimes.TryGetValue(femaleAtoms[0].uid, out value4))
		{
			SelectRuntime(value4, save: true);
		}
		else
		{
			selectedRuntime = null;
			selectedProfile = null;
			targetInput = string.Empty;
		}
		if (immediate)
		{
			targetRefreshAt = Time.unscaledTime + 1f;
		}
	}

	private static bool IsFemalePerson(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || atom.type != "Person")
		{
			return false;
		}
		JSONStorable storableByID = atom.GetStorableByID("geometry");
		DAZCharacterSelector val = (DAZCharacterSelector)(object)((storableByID is DAZCharacterSelector) ? storableByID : null);
		return (Object)(object)val != (Object)null && (int)val.gender == 2;
	}

	private TargetProfile GetProfile(string uid)
	{
		if (!profiles.TryGetValue(uid, out var value))
		{
			value = new TargetProfile(((BaseUnityPlugin)this).Config, uid);
			profiles.Add(uid, value);
		}
		return value;
	}

	private void SelectRuntime(TargetRuntime runtime, bool save)
	{
		if (runtime != null && !((Object)(object)runtime.TargetAtom == (Object)null))
		{
			selectedRuntime = runtime;
			selectedProfile = GetProfile(runtime.TargetAtom.uid);
			targetInput = runtime.TargetAtom.uid;
			if (save)
			{
				cfgTargetUid.Value = runtime.TargetAtom.uid;
				uiDirty = true;
				SaveUiState();
			}
		}
	}

	private bool SelectByUid(string uid, bool save)
	{
		if (string.IsNullOrEmpty(uid))
		{
			return false;
		}
		if (!runtimes.TryGetValue(uid, out var value))
		{
			return false;
		}
		SelectRuntime(value, save);
		return true;
	}

	private void SelectRelativeTarget(int delta)
	{
		RefreshTargets(immediate: false);
		if (femaleAtoms.Count == 0)
		{
			return;
		}
		int num = -1;
		Atom val = ((selectedRuntime != null) ? selectedRuntime.TargetAtom : null);
		for (int i = 0; i < femaleAtoms.Count; i++)
		{
			if ((Object)(object)femaleAtoms[i] == (Object)(object)val)
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			num = ((delta >= 0) ? (-1) : 0);
		}
		num = (num + delta + femaleAtoms.Count) % femaleAtoms.Count;
		if (runtimes.TryGetValue(femaleAtoms[num].uid, out var value))
		{
			SelectRuntime(value, save: true);
		}
	}

	private void ClearAllMarks()
	{
		foreach (TargetRuntime value in runtimes.Values)
		{
			value.ClearMarks();
		}
	}

	internal void ReportRuntimeError(string message, Exception e)
	{
		Logger.LogError((object)(message + "\n" + e));
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController.LogError(message);
		}
	}

	private float DpiScale()
	{
		float num = NativeDpi.TryGetScale();
		if (num > 0f)
		{
			return Mathf.Clamp(num, 0.75f, 2.5f);
		}
		float dpi = Screen.dpi;
		if (dpi <= 20f || float.IsNaN(dpi) || float.IsInfinity(dpi))
		{
			return 1f;
		}
		return Mathf.Clamp(dpi / 96f, 0.75f, 2.5f);
	}

	private float EffectiveScale()
	{
		return Mathf.Clamp(userScale * DpiScale(), 0.65f, 3f);
	}

	private void OnGUI()
	{
		if (Screen.width > 0 && Screen.height > 0 && cfgShow.Value)
		{
			float num = DpiScale();
			if (lastDpiScale < 0f)
			{
				lastDpiScale = num;
			}
			else if (Mathf.Abs(num - lastDpiScale) > 0.02f)
			{
				float num2 = num / lastDpiScale;
				ref Rect reference = ref windowRect;
				reference.width *= num2;
				ref Rect reference2 = ref windowRect;
				reference2.height *= num2;
				lastDpiScale = num;
				uiDirty = true;
			}
			float scale = EffectiveScale();
			EnsureStyles(scale);
			ClampWindow(scale);
			Rect val = (Rect)((!collapsed) ? windowRect : new Rect(windowRect.x, windowRect.y, windowRect.width, CollapsedHeight(scale)));
			float x = val.x;
			float y = val.y;
			Rect val2 = GUI.Window(1381126227, val, (GUI.WindowFunction)DrawWindow, string.Empty, GUIStyle.none);
			windowRect.x = val2.x;
			windowRect.y = val2.y;
			if (Mathf.Abs(x - val2.x) > 0.01f || Mathf.Abs(y - val2.y) > 0.01f)
			{
				uiDirty = true;
			}
			ClampWindow(scale);
			if ((int)Event.current.type == 1 && uiDirty)
			{
				SaveUiState();
			}
		}
	}

	private string WindowTitle()
	{
		return "Slaps: auto " + runtimes.Count;
	}

	private float CollapsedHeight(float scale)
	{
		return 34f * scale;
	}

	private void EnsureStyles(float scale)
	{
		if (windowStyle == null || !(Mathf.Abs(styleScale - scale) < 0.02f))
		{
			styleScale = scale;
			int fontSize = Mathf.Max(10, Mathf.RoundToInt(12f * scale));
			int fontSize2 = Mathf.Max(9, Mathf.RoundToInt(10f * scale));
			windowStyle = new GUIStyle(GUI.skin.window);
			windowStyle.fontSize = fontSize;
			labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.fontSize = fontSize;
			labelStyle.wordWrap = true;
			smallLabelStyle = new GUIStyle(GUI.skin.label);
			smallLabelStyle.fontSize = fontSize2;
			smallLabelStyle.wordWrap = true;
			buttonStyle = new GUIStyle(GUI.skin.button);
			buttonStyle.fontSize = fontSize;
			textStyle = new GUIStyle(GUI.skin.textField);
			textStyle.fontSize = fontSize;
			boxStyle = new GUIStyle(GUI.skin.box);
			boxStyle.fontSize = fontSize;
			boxStyle.alignment = (TextAnchor)0;
			boxStyle.wordWrap = true;
			toggleStyle = new GUIStyle(GUI.skin.toggle);
			toggleStyle.fontSize = fontSize;
			sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
			sliderStyle.fixedHeight = Mathf.Max(12f, 12f * scale);
			sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
			sliderThumbStyle.fixedWidth = Mathf.Max(10f, 14f * scale);
			sliderThumbStyle.fixedHeight = Mathf.Max(14f, 18f * scale);
		}
	}

	private void DrawWindow(int id)
	{
		float num = EffectiveScale();
		float num3 = 26f * num;
		float num5 = ((!collapsed) ? windowRect.height : CollapsedHeight(num));
		ZeroT.UiKit.RlChrome.Backdrop(windowRect.width, num5);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(windowRect.width, WindowTitle(), collapsed, num, labelStyle, buttonStyle, cfgActive.Value);
		if ((rlButtons & 1) != 0)
		{
			ChangeUserScale(-0.1f);
		}
		if ((rlButtons & 2) != 0)
		{
			ChangeUserScale(0.1f);
		}
		if ((rlButtons & 16) != 0)
		{
			cfgActive.Value = !cfgActive.Value;
			uiDirty = true;
			SaveUiState();
		}
		if ((rlButtons & 4) != 0)
		{
			collapsed = !collapsed;
			uiDirty = true;
			SaveUiState();
		}
		if ((rlButtons & 8) != 0)
		{
			cfgShow.Value = false;
			uiDirty = true;
			SaveUiState();
		}
		if (!collapsed && cfgActive.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, num5, num));
			DrawExpanded(num, num3);
			GUILayout.EndArea();
			DrawResizeHandle(num);
		}
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, windowRect.width - 118f * num), 26f * num));
	}

	private void DrawExpanded(float scale, float row)
	{
		GUILayout.Space(4f * scale);
		GUILayout.Label("AUTO mode: every detected female Person is active. The selector below only chooses which atom's settings/status you are editing.", smallLabelStyle, new GUILayoutOption[0]);
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("<", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(34f * scale),
			GUILayout.Height(row)
		}))
		{
			SelectRelativeTarget(-1);
		}
		targetInput = GUILayout.TextField(targetInput ?? string.Empty, textStyle, new GUILayoutOption[1] { GUILayout.Height(row) });
		if (GUILayout.Button("Edit", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(52f * scale),
			GUILayout.Height(row)
		}))
		{
			SelectByUid(targetInput, save: true);
		}
		if (GUILayout.Button(">", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(34f * scale),
			GUILayout.Height(row)
		}))
		{
			SelectRelativeTarget(1);
		}
		GUILayout.EndHorizontal();
		int num = 0;
		foreach (TargetRuntime value in runtimes.Values)
		{
			if (value.Ready)
			{
				num++;
			}
		}
		GUILayout.Label(femaleAtoms.Count + " female Person atom(s) detected; " + num + " runtime(s) ready.", smallLabelStyle, new GUILayoutOption[0]);
		GUILayout.Space(3f * scale);
		string text = ((selectedRuntime != null && !((Object)(object)selectedRuntime.TargetAtom == (Object)null)) ? selectedRuntime.TargetAtom.uid : "none");
		string text2 = ((selectedRuntime != null) ? selectedRuntime.StatusText : "No female Person is currently available.");
		GUILayout.Box("Editing: " + text + "\n" + (text2 ?? string.Empty), boxStyle, new GUILayoutOption[2]
		{
			GUILayout.ExpandWidth(true),
			GUILayout.MinHeight(104f * scale)
		});
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Clear selected", buttonStyle, new GUILayoutOption[1] { GUILayout.Height(row) }) && selectedRuntime != null)
		{
			selectedRuntime.ClearMarks();
		}
		if (GUILayout.Button("Retry selected", buttonStyle, new GUILayoutOption[1] { GUILayout.Height(row) }) && selectedRuntime != null)
		{
			selectedRuntime.RetryRuntime();
		}
		if (GUILayout.Button("Clear ALL", buttonStyle, new GUILayoutOption[1] { GUILayout.Height(row) }))
		{
			ClearAllMarks();
		}
		GUILayout.EndHorizontal();
		if (selectedProfile == null)
		{
			GUILayout.Label("Add a female Person atom to the scene; it will be targeted automatically.", labelStyle, new GUILayoutOption[0]);
			return;
		}
		scroll = GUILayout.BeginScrollView(scroll, false, true, new GUILayoutOption[0]);
		for (int i = 0; i < Configuration.Parameters.Length; i++)
		{
			DrawParameter(i, scale, row);
		}
		bool flag = GUILayout.Toggle(Configuration.SelfContacts, "Self contacts", toggleStyle, new GUILayoutOption[1] { GUILayout.Height(row) });
		if (flag != Configuration.SelfContacts)
		{
			selectedProfile.SetSelfContacts(flag);
		}
		GUILayout.Space(5f * scale);
		showLevels = GUILayout.Toggle(showLevels, "Show regional levels", toggleStyle, new GUILayoutOption[1] { GUILayout.Height(row) });
		if (showLevels)
		{
			GUILayout.Box((selectedRuntime != null) ? (selectedRuntime.LevelsText ?? string.Empty) : string.Empty, boxStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		}
		GUILayout.EndScrollView();
	}

	private void DrawParameter(int index, float scale, float row)
	{
		Parameter parameter = Configuration.Parameters[index];
		GUILayout.BeginVertical(new GUILayoutOption[1] { GUILayout.Height(row * 1.8f) });
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label(parameter.Label, smallLabelStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		GUILayout.Label(parameter.Value.ToString((!(parameter.Max <= 1f)) ? "0.00" : "0.000"), smallLabelStyle, new GUILayoutOption[1] { GUILayout.Width(62f * scale) });
		if (GUILayout.Button("R", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(28f * scale),
			GUILayout.Height(20f * scale)
		}))
		{
			selectedProfile.SetParameter(index, parameter.Default);
		}
		GUILayout.EndHorizontal();
		float num = GUILayout.HorizontalSlider(parameter.Value, parameter.Min, parameter.Max, sliderStyle, sliderThumbStyle, new GUILayoutOption[1] { GUILayout.Height(18f * scale) });
		if (Mathf.Abs(num - parameter.Value) > 1E-06f)
		{
			selectedProfile.SetParameter(index, num);
		}
		GUILayout.EndVertical();
	}

	private void DrawResizeHandle(float scale)
	{
		Rect val = ZeroT.UiKit.RlChrome.HandleRect(windowRect.width, windowRect.height, scale);
		GUI.Label(val, "↘", labelStyle);
		Event current = Event.current;
		int controlID = GUIUtility.GetControlID(1381127002, (FocusType)2);
		EventType typeForControl = current.GetTypeForControl(controlID);
		if ((int)typeForControl == 0 && val.Contains(current.mousePosition) && current.button == 0)
		{
			resizing = true;
			GUIUtility.hotControl = controlID;
			current.Use();
		}
		else if ((int)typeForControl == 3 && resizing && GUIUtility.hotControl == controlID)
		{
			float num2 = Mathf.Min((float)Screen.width, 330f * scale);
			float num3 = Mathf.Min((float)Screen.height, 260f * scale);
			float num4 = Mathf.Max(num2, (float)Screen.width - windowRect.x - 2f);
			float num5 = Mathf.Max(num3, (float)Screen.height - windowRect.y - 2f);
			windowRect.width = Mathf.Clamp(windowRect.width + current.delta.x, num2, num4);
			windowRect.height = Mathf.Clamp(windowRect.height + current.delta.y, num3, num5);
			uiDirty = true;
			current.Use();
		}
		else if ((int)typeForControl == 1 && resizing && GUIUtility.hotControl == controlID)
		{
			resizing = false;
			GUIUtility.hotControl = 0;
			uiDirty = true;
			current.Use();
			SaveUiState();
		}
	}

	private void ChangeUserScale(float delta)
	{
		float num = EffectiveScale();
		float num2 = Mathf.Clamp(userScale + delta, 0.55f, 2.25f);
		if (!(Mathf.Abs(num2 - userScale) < 0.001f))
		{
			userScale = num2;
			float num3 = EffectiveScale();
			float num4 = num3 / num;
			ref Rect reference = ref windowRect;
			reference.width *= num4;
			ref Rect reference2 = ref windowRect;
			reference2.height *= num4;
			styleScale = -1f;
			uiDirty = true;
			ClampWindow(num3);
			SaveUiState();
		}
	}

	private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();
	private void ClampWindow(float scale)
	{
		_home.Begin(ref windowRect);
		float num = 2f;
		float num2 = Mathf.Max(40f, (float)Screen.width - num * 2f);
		float num3 = Mathf.Max(40f, (float)Screen.height - num * 2f);
		float num4 = Mathf.Min(num2, 330f * scale);
		float num5 = Mathf.Min(num3, 260f * scale);
		windowRect.width = Mathf.Clamp(windowRect.width, num4, num2);
		windowRect.height = Mathf.Clamp(windowRect.height, num5, num3);
		float num6 = ((!collapsed) ? windowRect.height : Mathf.Min(CollapsedHeight(scale), num3));
		windowRect.x = Mathf.Clamp(windowRect.x, num, Mathf.Max(num, (float)Screen.width - windowRect.width - num));
		windowRect.y = Mathf.Clamp(windowRect.y, num, Mathf.Max(num, (float)Screen.height - num6 - num));
		_home.End(ref windowRect);
	}

	private void SaveUiState()
	{
		cfgX.Value = windowRect.x;
		cfgY.Value = windowRect.y;
		cfgW.Value = windowRect.width;
		cfgH.Value = windowRect.height;
		cfgScale.Value = userScale;
		cfgCollapsed.Value = collapsed;
		if (selectedRuntime != null && (Object)(object)selectedRuntime.TargetAtom != (Object)null)
		{
			cfgTargetUid.Value = selectedRuntime.TargetAtom.uid;
		}
		((BaseUnityPlugin)this).Config.Save();
		uiDirty = false;
	}

	private void OnDestroy()
	{
		try
		{
			SaveUiState();
		}
		catch
		{
		}
		foreach (TargetRuntime value in runtimes.Values)
		{
			value.Dispose();
		}
		runtimes.Clear();
	}
}
