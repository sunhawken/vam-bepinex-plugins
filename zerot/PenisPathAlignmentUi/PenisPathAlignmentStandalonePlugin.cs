using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace HelicalPink.Standalone;

[BepInPlugin("helicalpink.penispalignment.standalone", "Penis Path Alignment Standalone", "10.1.2")]
public sealed class PenisPathAlignmentSkinnedPlugin : BaseUnityPlugin
{
	private sealed class SourceState
	{
		public Atom Atom;

		public GameObject Host;

		public PenisPathAlignment Ppa;

		public Atom Target;

		public string RequestedHole = "";

		public string AppliedHole = "none";

		public SourceState(Atom atom, GameObject host, PenisPathAlignment ppa)
		{
			Atom = atom;
			Host = host;
			Ppa = ppa;
		}
	}

	public const string PluginGuid = "helicalpink.penispalignment.standalone";

	public const string PluginName = "Penis Path Alignment Standalone";

	public const string PluginVersion = "10.1.2";

	private static readonly string[] HoleChoices = new string[3] { "Mouth", "Anus", "Vagina" };

	private ConfigEntry<bool> cfgEnabled;

	private ConfigEntry<bool> cfgAutoMode;

	private ConfigEntry<float> cfgRetargetInterval;

	private ConfigEntry<string> cfgTargetHole;

	private ConfigEntry<string> cfgManualSourceUid;

	private ConfigEntry<string> cfgManualTargetUid;

	private ConfigEntry<bool> cfgGuiVisible;

	private ConfigEntry<bool> cfgCollapsed;

	private ConfigEntry<bool> cfgAutoDpi;

	private ConfigEntry<float> cfgUiScale;

	private ConfigEntry<float> cfgWindowX;

	private ConfigEntry<float> cfgWindowY;

	private ConfigEntry<float> cfgWindowW;

	private ConfigEntry<float> cfgWindowH;

	private readonly Dictionary<string, SourceState> sources = new Dictionary<string, SourceState>(StringComparer.Ordinal);

	private readonly List<Atom> personScratch = new List<Atom>();

	private readonly List<Atom> eligibleSourceScratch = new List<Atom>();

	private readonly List<string> removeScratch = new List<string>();

	private readonly List<string> sourceUidScratch = new List<string>();

	private readonly List<string> targetUidScratch = new List<string>();

	private readonly HashSet<string> seenSourceUids = new HashSet<string>(StringComparer.Ordinal);

	private float nextSyncTime;

	private bool forceSync = true;

	private bool lastMasterEnabled;

	private bool lastAutoMode;

	private string lastHole = "";

	private string lastManualSource = "";

	private string lastManualTarget = "";

	private Rect windowRect;

	private Rect expandedRectBeforeCollapse;

	private Vector2 scrollPosition = Vector2.zero;

	private bool resizing;

	private Vector2 resizeStartMouse;

	private Vector2 resizeStartSize;

	private bool layoutDirty;

	private float layoutSaveAfter;

	private int windowId;

	private float guiScaleCached = 1f;

	private GUIStyle windowStyle;

	private GUIStyle titleStyle;

	private GUIStyle subtitleStyle;

	private GUIStyle sectionStyle;

	private GUIStyle statusStyle;

	private GUIStyle activeStatusStyle;

	private GUIStyle smallStyle;

	private Texture2D windowTexture;

	private Texture2D sectionTexture;

	private Texture2D statusTexture;

	private bool stylesReady;

	private void Awake()
	{
		BindConfig();
		windowRect = new Rect(cfgWindowX.Value, cfgWindowY.Value, cfgCollapsed.Value ? 210f : cfgWindowW.Value, cfgCollapsed.Value ? 24f : cfgWindowH.Value);
		expandedRectBeforeCollapse = new Rect(windowRect.x, windowRect.y, windowRect.width, cfgWindowH.Value);
		windowId = "helicalpink.penispalignment.standalone".GetHashCode();
		lastMasterEnabled = cfgEnabled.Value;
		lastAutoMode = cfgAutoMode.Value;
		lastHole = NormalizeHole(cfgTargetHole.Value);
		lastManualSource = cfgManualSourceUid.Value ?? "";
		lastManualTarget = cfgManualTargetUid.Value ?? "";
		Logger.LogInfo((object)"Penis Path Alignment Standalone 10.1.2 loaded. F8 toggles the GUI.");
	}

	private void BindConfig()
	{
		cfgEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Enabled", true, "Master switch. Disabling immediately releases PPA controller states.");
		cfgAutoMode = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "AutoMode", true, "Automatically targets the nearest female Person for every eligible source and enables the v10 continuous auto controller.");
		cfgRetargetInterval = ((BaseUnityPlugin)this).Config.Bind<float>("Targeting", "RetargetIntervalSeconds", 0.5f, "How often the centralized nearest-female scan runs. Lower values react faster but scan more often.");
		cfgTargetHole = ((BaseUnityPlugin)this).Config.Bind<string>("Targeting", "TargetPoint", "Anus", "Requested target point: Mouth, Anus, or Vagina. Vagina safely falls back when unavailable.");
		cfgManualSourceUid = ((BaseUnityPlugin)this).Config.Bind<string>("Manual", "SourcePersonUid", "", "Male source used while Auto Mode is disabled.");
		cfgManualTargetUid = ((BaseUnityPlugin)this).Config.Bind<string>("Manual", "TargetPersonUid", "", "Female target used while Auto Mode is disabled. The selected source is never allowed here.");
		cfgGuiVisible = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "Visible", true, "Show the desktop overlay. F8 toggles it.");
		cfgCollapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "Collapsed", false, "Remember collapsed state.");
		cfgAutoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "AutoDPIScaling", true, "Scale the overlay using Screen.dpi when available.");
		cfgUiScale = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "ManualUIScale", 1f, "Additional UI scale multiplier.");
		cfgWindowX = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowX", 24f, "Remembered logical window X position.");
		cfgWindowY = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowY", 24f, "Remembered logical window Y position.");
		cfgWindowW = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowWidth", 540f, "Remembered logical window width.");
		cfgWindowH = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowHeight", 560f, "Remembered logical expanded window height.");
	}

	private void Update()
	{
		if (Input.GetKeyDown((KeyCode)289))
		{
			cfgGuiVisible.Value = !cfgGuiVisible.Value;
			((BaseUnityPlugin)this).Config.Save();
		}
		string text = NormalizeHole(cfgTargetHole.Value);
		if (!string.Equals(text, cfgTargetHole.Value, StringComparison.Ordinal))
		{
			cfgTargetHole.Value = text;
		}
		if (cfgEnabled.Value != lastMasterEnabled || cfgAutoMode.Value != lastAutoMode || !string.Equals(text, lastHole, StringComparison.Ordinal) || !string.Equals(cfgManualSourceUid.Value ?? "", lastManualSource, StringComparison.Ordinal) || !string.Equals(cfgManualTargetUid.Value ?? "", lastManualTarget, StringComparison.Ordinal))
		{
			forceSync = true;
			lastMasterEnabled = cfgEnabled.Value;
			lastAutoMode = cfgAutoMode.Value;
			lastHole = text;
			lastManualSource = cfgManualSourceUid.Value ?? "";
			lastManualTarget = cfgManualTargetUid.Value ?? "";
		}
		if (!((Object)(object)SuperController.singleton == (Object)null))
		{
			float unscaledTime = Time.unscaledTime;
			float num = Mathf.Clamp(cfgRetargetInterval.Value, 0.25f, 3f);
			if (forceSync || unscaledTime >= nextSyncTime)
			{
				SynchronizeAndTarget();
				forceSync = false;
				nextSyncTime = unscaledTime + num;
			}
		}
	}

	private void SynchronizeAndTarget()
	{
		List<Atom> list = null;
		try
		{
			list = SuperController.singleton.GetAtoms();
		}
		catch
		{
			list = null;
		}
		if (list == null)
		{
			return;
		}
		personScratch.Clear();
		eligibleSourceScratch.Clear();
		seenSourceUids.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			Atom val = list[i];
			if (IsPerson(val))
			{
				personScratch.Add(val);
				if (HasPpaSourceRig(val))
				{
					eligibleSourceScratch.Add(val);
				}
			}
		}
		for (int j = 0; j < eligibleSourceScratch.Count; j++)
		{
			Atom val2 = eligibleSourceScratch[j];
			string text = SafeUid(val2);
			if (text.Length == 0)
			{
				continue;
			}
			seenSourceUids.Add(text);
			if (!sources.TryGetValue(text, out var value) || value == null || (Object)(object)value.Ppa == (Object)null)
			{
				value = CreateSourceState(val2);
				if (value != null)
				{
					sources[text] = value;
				}
			}
			else
			{
				value.Atom = val2;
			}
		}
		removeScratch.Clear();
		foreach (KeyValuePair<string, SourceState> source in sources)
		{
			if (!seenSourceUids.Contains(source.Key) || source.Value == null || (Object)(object)source.Value.Atom == (Object)null)
			{
				removeScratch.Add(source.Key);
			}
		}
		for (int k = 0; k < removeScratch.Count; k++)
		{
			string key = removeScratch[k];
			if (sources.TryGetValue(key, out var value2))
			{
				DestroySourceState(value2);
			}
			sources.Remove(key);
		}
		RepairManualSelections();
		if (!cfgEnabled.Value)
		{
			foreach (KeyValuePair<string, SourceState> source2 in sources)
			{
				if (source2.Value != null && (Object)(object)source2.Value.Ppa != (Object)null)
				{
					source2.Value.Ppa.SetStandaloneEnabled(enabled: false);
				}
			}
			return;
		}
		if (cfgAutoMode.Value)
		{
			ApplyAutoTargets();
		}
		else
		{
			ApplyManualTarget();
		}
	}

	private SourceState CreateSourceState(Atom source)
	{
		GameObject val = null;
		try
		{
			val = new GameObject("_PPAStandalone_" + SafeUid(source));
			val.transform.SetParent(((Component)source).transform, false);
			PenisPathAlignment penisPathAlignment = val.AddComponent<PenisPathAlignment>();
			if (!penisPathAlignment.InitializeStandalone(source))
			{
				Object.Destroy((Object)(object)val);
				return null;
			}
			return new SourceState(source, val, penisPathAlignment);
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Could not initialize PPA source " + SafeUid(source) + ": " + ex.Message));
			if ((Object)(object)val != (Object)null)
			{
				Object.Destroy((Object)(object)val);
			}
			return null;
		}
	}

	private void DestroySourceState(SourceState state)
	{
		if (state == null)
		{
			return;
		}
		try
		{
			if ((Object)(object)state.Ppa != (Object)null)
			{
				state.Ppa.SetStandaloneEnabled(enabled: false);
			}
			if ((Object)(object)state.Host != (Object)null)
			{
				Object.Destroy((Object)(object)state.Host);
			}
		}
		catch
		{
		}
	}

	private void ApplyAutoTargets()
	{
		string text = NormalizeHole(cfgTargetHole.Value);
		foreach (KeyValuePair<string, SourceState> source in sources)
		{
			SourceState value = source.Value;
			if (value == null || (Object)(object)value.Ppa == (Object)null || (Object)(object)value.Atom == (Object)null)
			{
				continue;
			}
			value.Ppa.SetStandaloneEnabled(enabled: true);
			value.Ppa.SetStandaloneAutoController(enabled: true);
			Atom val = FindNearestOtherPerson(value.Atom);
			if ((Object)(object)val == (Object)null)
			{
				value.Ppa.ClearStandaloneTarget();
				value.Target = null;
				value.RequestedHole = "";
				value.AppliedHole = "none";
			}
			else if (!((Object)(object)value.Target == (Object)(object)val) || !value.Ppa.Attached || !string.Equals(value.RequestedHole, text, StringComparison.Ordinal))
			{
				if (value.Ppa.SetStandaloneTarget(val, text, out var appliedHole))
				{
					value.Target = val;
					value.RequestedHole = text;
					value.AppliedHole = appliedHole;
				}
				else
				{
					value.Target = null;
					value.RequestedHole = "";
					value.AppliedHole = "none";
				}
			}
		}
	}

	private void ApplyManualTarget()
	{
		string key = cfgManualSourceUid.Value ?? "";
		string uid = cfgManualTargetUid.Value ?? "";
		SourceState value = null;
		sources.TryGetValue(key, out value);
		Atom val = FindPersonByUid(uid);
		string text = NormalizeHole(cfgTargetHole.Value);
		foreach (KeyValuePair<string, SourceState> source in sources)
		{
			SourceState value2 = source.Value;
			if (value2 == null || (Object)(object)value2.Ppa == (Object)null)
			{
				continue;
			}
			bool flag = value != null && value2 == value;
			value2.Ppa.SetStandaloneAutoController(enabled: false);
			value2.Ppa.SetStandaloneEnabled(flag);
			if (!flag)
			{
				value2.Ppa.ClearStandaloneTarget();
				value2.Target = null;
				value2.RequestedHole = "";
				value2.AppliedHole = "none";
			}
			else if (!IsValidOtherPerson(value2.Atom, val))
			{
				value2.Ppa.ClearStandaloneTarget();
				value2.Target = null;
				value2.RequestedHole = "";
				value2.AppliedHole = "none";
			}
			else if (!((Object)(object)value2.Target == (Object)(object)val) || !value2.Ppa.Attached || !string.Equals(value2.RequestedHole, text, StringComparison.Ordinal))
			{
				if (value2.Ppa.SetStandaloneTarget(val, text, out var appliedHole))
				{
					value2.Target = val;
					value2.RequestedHole = text;
					value2.AppliedHole = appliedHole;
				}
				else
				{
					value2.Target = null;
					value2.RequestedHole = "";
					value2.AppliedHole = "none";
				}
			}
		}
	}

	private void RepairManualSelections()
	{
		sourceUidScratch.Clear();
		foreach (KeyValuePair<string, SourceState> source2 in sources)
		{
			if (source2.Value != null && (Object)(object)source2.Value.Atom != (Object)null)
			{
				sourceUidScratch.Add(source2.Key);
			}
		}
		sourceUidScratch.Sort(StringComparer.OrdinalIgnoreCase);
		string text = cfgManualSourceUid.Value ?? "";
		if (!sourceUidScratch.Contains(text))
		{
			text = ((sourceUidScratch.Count > 0) ? sourceUidScratch[0] : "");
			cfgManualSourceUid.Value = text;
		}
		BuildManualTargetUidList(text);
		string item = cfgManualTargetUid.Value ?? "";
		if (!targetUidScratch.Contains(item))
		{
			Atom source = FindPersonByUid(text);
			Atom val = FindNearestOtherPerson(source);
			if ((Object)(object)val != (Object)null)
			{
				item = SafeUid(val);
			}
			else
			{
				item = ((targetUidScratch.Count > 0) ? targetUidScratch[0] : "");
			}
			cfgManualTargetUid.Value = item;
		}
	}

	private void BuildManualTargetUidList(string sourceUid)
	{
		targetUidScratch.Clear();
		for (int i = 0; i < personScratch.Count; i++)
		{
			if (HasRequiredGender(personScratch[i], male: false))
			{
				string text = SafeUid(personScratch[i]);
				if (text.Length != 0 && !string.Equals(text, sourceUid, StringComparison.Ordinal))
				{
					targetUidScratch.Add(text);
				}
			}
		}
		targetUidScratch.Sort(StringComparer.OrdinalIgnoreCase);
	}

	private Atom FindNearestOtherPerson(Atom source)
	{
		if ((Object)(object)source == (Object)null)
		{
			return null;
		}
		Vector3 personAnchor = GetPersonAnchor(source);
		Atom result = null;
		float num = float.MaxValue;
		for (int i = 0; i < personScratch.Count; i++)
		{
			Atom val = personScratch[i];
			if (IsValidOtherPerson(source, val))
			{
				Vector3 val2 = GetPersonAnchor(val) - personAnchor;
				float sqrMagnitude = val2.sqrMagnitude;
				if (sqrMagnitude < num)
				{
					num = sqrMagnitude;
					result = val;
				}
			}
		}
		return result;
	}

	private static bool IsValidOtherPerson(Atom source, Atom candidate)
	{
		if (!HasRequiredGender(source, male: true) || !HasRequiredGender(candidate, male: false))
		{
			return false;
		}
		if ((Object)(object)source == (Object)null || (Object)(object)candidate == (Object)null || candidate.type != "Person")
		{
			return false;
		}
		if ((Object)(object)candidate == (Object)(object)source)
		{
			return false;
		}
		string text = SafeUid(source);
		string text2 = SafeUid(candidate);
		if (text.Length != 0 && text2.Length != 0)
		{
			return !string.Equals(text, text2, StringComparison.Ordinal);
		}
		return true;
	}

	private Atom FindPersonByUid(string uid)
	{
		if (string.IsNullOrEmpty(uid))
		{
			return null;
		}
		for (int i = 0; i < personScratch.Count; i++)
		{
			Atom val = personScratch[i];
			if ((Object)(object)val != (Object)null && string.Equals(SafeUid(val), uid, StringComparison.Ordinal))
			{
				return val;
			}
		}
		return null;
	}

	private static Vector3 GetPersonAnchor(Atom person)
	{
		if ((Object)(object)person == (Object)null)
		{
			return Vector3.zero;
		}
		if (person.rigidbodies != null)
		{
			for (int i = 0; i < person.rigidbodies.Length; i++)
			{
				Rigidbody val = person.rigidbodies[i];
				if ((Object)(object)val != (Object)null && ((Object)val).name == "pelvis")
				{
					return val.position;
				}
			}
		}
		return ((Component)person).transform.position;
	}

	private static bool HasPpaSourceRig(Atom person)
	{
		if (!HasRequiredGender(person, male: true))
		{
			return false;
		}
		if (!IsPerson(person) || person.freeControllers == null || person.rigidbodies == null)
		{
			return false;
		}
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		for (int i = 0; i < person.freeControllers.Length; i++)
		{
			FreeControllerV3 val = person.freeControllers[i];
			if (!((Object)(object)val == (Object)null))
			{
				if (((Object)val).name == "penisBaseControl")
				{
					flag = true;
				}
				else if (((Object)val).name == "penisMidControl")
				{
					flag2 = true;
				}
				else if (((Object)val).name == "penisTipControl")
				{
					flag3 = true;
				}
			}
		}
		if (!flag || !flag2 || !flag3)
		{
			return false;
		}
		bool flag4 = false;
		bool flag5 = false;
		bool flag6 = false;
		bool flag7 = false;
		for (int j = 0; j < person.rigidbodies.Length; j++)
		{
			Rigidbody val2 = person.rigidbodies[j];
			if (!((Object)(object)val2 == (Object)null))
			{
				if (((Object)val2).name == "Gen1")
				{
					flag4 = true;
				}
				else if (((Object)val2).name == "Gen2")
				{
					flag5 = true;
				}
				else if (((Object)val2).name == "Gen3")
				{
					flag6 = true;
				}
				else if (((Object)val2).name == "pelvis")
				{
					flag7 = true;
				}
			}
		}
		return flag4 & flag5 & flag6 & flag7;
	}

	private static bool IsPerson(Atom atom)
	{
		if ((Object)(object)atom != (Object)null)
		{
			return atom.type == "Person";
		}
		return false;
	}

	private static string SafeUid(Atom atom)
	{
		try
		{
			return ((Object)(object)atom != (Object)null && atom.uid != null) ? atom.uid : "";
		}
		catch
		{
			return "";
		}
	}

	private static string DisplayPerson(Atom atom)
	{
		if ((Object)(object)atom == (Object)null)
		{
			return "(none)";
		}
		string text = SafeUid(atom);
		string text2 = "";
		try
		{
			text2 = ((Object)atom).name ?? "";
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(text2) || string.Equals(text2, text, StringComparison.Ordinal))
		{
			return text;
		}
		return text2 + "  [" + text + "]";
	}

	private static string NormalizeHole(string value)
	{
		if (string.Equals(value, "Mouth", StringComparison.OrdinalIgnoreCase))
		{
			return "Mouth";
		}
		if (string.Equals(value, "Vagina", StringComparison.OrdinalIgnoreCase))
		{
			return "Vagina";
		}
		return "Anus";
	}

	private void OnGUI()
	{
		if (cfgGuiVisible != null && cfgGuiVisible.Value)
		{
			EnsureStyles();
			guiScaleCached = GetGuiScale();
			Matrix4x4 matrix = GUI.matrix;
			GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(guiScaleCached, guiScaleCached, 1f));
			float sw = (float)Screen.width / guiScaleCached;
			float sh = (float)Screen.height / guiScaleCached;
			ClampWindowToScreen(sw, sh);
			Rect val = windowRect;
			windowRect = GUI.Window(windowId, windowRect, (GUI.WindowFunction)DrawWindow, string.Empty, GUIStyle.none);
			if (Mathf.Abs(val.x - windowRect.x) > 0.01f || Mathf.Abs(val.y - windowRect.y) > 0.01f || Mathf.Abs(val.width - windowRect.width) > 0.01f || Mathf.Abs(val.height - windowRect.height) > 0.01f)
			{
				layoutDirty = true;
				layoutSaveAfter = Time.unscaledTime + 0.35f;
			}
			if (layoutDirty && !resizing && Time.unscaledTime >= layoutSaveAfter)
			{
				SaveWindowLayout();
			}
			GUI.matrix = matrix;
		}
	}

	private void DrawWindow(int id)
	{
		ZeroT.UiKit.RlChrome.Backdrop(windowRect.width, windowRect.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(windowRect.width, cfgCollapsed.Value ? "PPA Standalone  v10.1.2" : "Penis Path Alignment  v10.1.2", cfgCollapsed.Value, cfgEnabled.Value);
		if ((rlButtons & 1) != 0)
		{
			cfgUiScale.Value = Mathf.Clamp(cfgUiScale.Value - 0.1f, 0.3f, 1.75f);
			ZeroT.UiKit.RlChrome.ResetDpi();
		}
		if ((rlButtons & 2) != 0)
		{
			cfgUiScale.Value = Mathf.Clamp(cfgUiScale.Value + 0.1f, 0.3f, 1.75f);
			ZeroT.UiKit.RlChrome.ResetDpi();
		}
		if ((rlButtons & 16) != 0)
		{
			cfgEnabled.Value = !cfgEnabled.Value;
			forceSync = true;
			((BaseUnityPlugin)this).Config.Save();
		}
		if ((rlButtons & 4) != 0)
		{
			ToggleCollapsed();
		}
		if ((rlButtons & 8) != 0)
		{
			cfgGuiVisible.Value = false;
			((BaseUnityPlugin)this).Config.Save();
		}
		if (cfgCollapsed.Value)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 150f), 26f));
			return;
		}
		GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, windowRect.height));
		GUILayout.BeginVertical(new GUILayoutOption[0]);
		GUILayout.Label("Standalone BepInEx • no Person .cs needed", subtitleStyle, new GUILayoutOption[0]);
		scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, new GUILayoutOption[0]);
		DrawSection("Plugin Control");
		bool flag = GUILayout.Toggle(cfgEnabled.Value, "Enabled", new GUILayoutOption[0]);
		if (flag != cfgEnabled.Value)
		{
			cfgEnabled.Value = flag;
			forceSync = true;
		}
		bool flag2 = GUILayout.Toggle(cfgAutoMode.Value, "Auto Mode — nearest female Person + continuous v10 alignment", new GUILayoutOption[0]);
		if (flag2 != cfgAutoMode.Value)
		{
			cfgAutoMode.Value = flag2;
			forceSync = true;
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label("Retarget interval", new GUILayoutOption[1] { GUILayout.Width(145f) });
		float num3 = GUILayout.HorizontalSlider(cfgRetargetInterval.Value, 0.25f, 2f, new GUILayoutOption[0]);
		GUILayout.Label(num3.ToString("0.00") + " s", new GUILayoutOption[1] { GUILayout.Width(58f) });
		GUILayout.EndHorizontal();
		if (Mathf.Abs(num3 - cfgRetargetInterval.Value) > 0.001f)
		{
			cfgRetargetInterval.Value = num3;
		}
		DrawSection("Target Point");
		DrawHoleButtons();
		GUILayout.Label("Vagina automatically falls back to Anus/Mouth when the selected Person has no valid vagina target.", smallStyle, new GUILayoutOption[0]);
		DrawSection(cfgAutoMode.Value ? "Auto Targeting" : "Manual Targeting");
		if (cfgAutoMode.Value)
		{
			GUILayout.Box("Every eligible male source independently selects the nearest female Person. Self is excluded by both object identity and atom UID.", statusStyle, new GUILayoutOption[0]);
		}
		else
		{
			DrawManualSelectors();
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Refresh / Retarget Now", new GUILayoutOption[0]))
		{
			forceSync = true;
		}
		if (GUILayout.Button("Release All", new GUILayoutOption[0]))
		{
			cfgEnabled.Value = false;
			forceSync = true;
		}
		GUILayout.EndHorizontal();
		DrawSection("Current Sources");
		if (sources.Count == 0)
		{
			GUILayout.Box("No eligible male source found (requires penisBaseControl, penisMidControl, penisTipControl + Gen1/Gen2/Gen3).", statusStyle, new GUILayoutOption[0]);
		}
		else
		{
			foreach (KeyValuePair<string, SourceState> source in sources)
			{
				SourceState value = source.Value;
				if (value != null)
				{
					GUILayout.Box(DisplayPerson(value.Atom) + "\n→ " + DisplayPerson(value.Target) + "   [" + (value.AppliedHole ?? "none") + "]", ((Object)(object)value.Target != (Object)null) ? activeStatusStyle : statusStyle, new GUILayoutOption[0]);
				}
			}
		}
		DrawSection("GUI");
		bool flag3 = GUILayout.Toggle(cfgAutoDpi.Value, "Automatic DPI scaling", new GUILayoutOption[0]);
		if (flag3 != cfgAutoDpi.Value)
		{
			cfgAutoDpi.Value = flag3;
		}
		GUILayout.Label("UI scale (S- / S+): " + cfgUiScale.Value.ToString("0.00") + "x", new GUILayoutOption[0]);
		GUILayout.Label("F8 = show/hide. Window position, size, collapse state, target point, and manual selections are remembered.", smallStyle, new GUILayoutOption[0]);
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
		GUILayout.EndArea();
		HandleResize();
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(60f, windowRect.width - 150f), 26f));
	}

	private void DrawHoleButtons()
	{
		string a = NormalizeHole(cfgTargetHole.Value);
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		for (int i = 0; i < HoleChoices.Length; i++)
		{
			string text = HoleChoices[i];
			if (GUILayout.Button(string.Equals(a, text, StringComparison.Ordinal) ? ("● " + text) : text, new GUILayoutOption[0]))
			{
				cfgTargetHole.Value = text;
				forceSync = true;
				a = text;
			}
		}
		GUILayout.EndHorizontal();
	}

	private void DrawManualSelectors()
	{
		sourceUidScratch.Clear();
		foreach (KeyValuePair<string, SourceState> source in sources)
		{
			if (source.Value != null && (Object)(object)source.Value.Atom != (Object)null)
			{
				sourceUidScratch.Add(source.Key);
			}
		}
		sourceUidScratch.Sort(StringComparer.OrdinalIgnoreCase);
		string text = DrawUidChoice("Male Source", cfgManualSourceUid.Value ?? "", sourceUidScratch, sourceList: true);
		if (!string.Equals(text, cfgManualSourceUid.Value ?? "", StringComparison.Ordinal))
		{
			cfgManualSourceUid.Value = text;
			BuildManualTargetUidList(text);
			if (!targetUidScratch.Contains(cfgManualTargetUid.Value ?? ""))
			{
				cfgManualTargetUid.Value = ((targetUidScratch.Count > 0) ? targetUidScratch[0] : "");
			}
			forceSync = true;
		}
		BuildManualTargetUidList(cfgManualSourceUid.Value ?? "");
		string text2 = DrawUidChoice("Female Target", cfgManualTargetUid.Value ?? "", targetUidScratch, sourceList: false);
		if (!string.Equals(text2, cfgManualTargetUid.Value ?? "", StringComparison.Ordinal))
		{
			cfgManualTargetUid.Value = text2;
			forceSync = true;
		}
		GUILayout.Label("Only female Persons are shown. The male source is excluded and selections are checked again when applied.", smallStyle, new GUILayoutOption[0]);
	}

	private string DrawUidChoice(string label, string current, List<string> choices, bool sourceList)
	{
		int num = choices.IndexOf(current);
		if (num < 0 && choices.Count > 0)
		{
			num = 0;
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label(label, new GUILayoutOption[1] { GUILayout.Width(112f) });
		if (GUILayout.Button("<", new GUILayoutOption[1] { GUILayout.Width(28f) }) && choices.Count > 0)
		{
			num = (num - 1 + choices.Count) % choices.Count;
		}
		string uid = ((num >= 0 && num < choices.Count) ? choices[num] : "");
		GUILayout.Box(DisplayPerson(sourceList ? FindSourceAtomByUid(uid) : FindPersonByUid(uid)), new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		if (GUILayout.Button(">", new GUILayoutOption[1] { GUILayout.Width(28f) }) && choices.Count > 0)
		{
			num = (num + 1) % choices.Count;
		}
		GUILayout.EndHorizontal();
		if (num < 0 || num >= choices.Count)
		{
			return "";
		}
		return choices[num];
	}

	private Atom FindSourceAtomByUid(string uid)
	{
		if (string.IsNullOrEmpty(uid) || !sources.TryGetValue(uid, out var value) || value == null)
		{
			return null;
		}
		return value.Atom;
	}

	private void DrawSection(string text)
	{
		GUILayout.Space(5f);
		GUILayout.Box(text, sectionStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
	}

	private void ToggleCollapsed()
	{
		if (!cfgCollapsed.Value)
		{
			expandedRectBeforeCollapse = windowRect;
			cfgWindowH.Value = Mathf.Max(360f, windowRect.height);
			cfgWindowW.Value = windowRect.width;
			cfgCollapsed.Value = true;
			resizing = false;
			windowRect.width = 340f;
			windowRect.height = 34f;
		}
		else
		{
			cfgCollapsed.Value = false;
			windowRect.width = Mathf.Max(430f, cfgWindowW.Value);
			windowRect.height = Mathf.Max(360f, cfgWindowH.Value);
		}
		layoutDirty = true;
		SaveWindowLayout();
	}

	private void HandleResize()
	{
		Rect val = new Rect(windowRect.width - 20f, windowRect.height - 20f, 18f, 18f);
		GUI.Label(val, "↘");
		Event current = Event.current;
		if (current != null)
		{
			if ((int)current.type == 0 && current.button == 0 && val.Contains(current.mousePosition))
			{
				resizing = true;
				resizeStartMouse = current.mousePosition;
				resizeStartSize = new Vector2(windowRect.width, windowRect.height);
				current.Use();
			}
			else if (resizing && (int)current.type == 3 && current.button == 0)
			{
				Vector2 val2 = current.mousePosition - resizeStartMouse;
				float num = Mathf.Max(430f, (float)Screen.width / guiScaleCached - windowRect.x);
				float num2 = Mathf.Max(360f, (float)Screen.height / guiScaleCached - windowRect.y);
				windowRect.width = Mathf.Clamp(resizeStartSize.x + val2.x, 430f, num);
				windowRect.height = Mathf.Clamp(resizeStartSize.y + val2.y, 360f, num2);
				layoutDirty = true;
				current.Use();
			}
			else if (resizing && (int)current.type == 1)
			{
				resizing = false;
				SaveWindowLayout();
				current.Use();
			}
		}
	}

	private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();
	private void ClampWindowToScreen(float sw, float sh)
	{
		_home.Begin(ref windowRect);
		float num = (cfgCollapsed.Value ? 340f : 430f);
		float num2 = (cfgCollapsed.Value ? 34f : 360f);
		windowRect.width = (cfgCollapsed.Value ? 340f : Mathf.Clamp(windowRect.width, num, Mathf.Max(num, sw)));
		windowRect.height = (cfgCollapsed.Value ? 34f : Mathf.Clamp(windowRect.height, num2, Mathf.Max(num2, sh)));
		windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, sw - 90f));
		windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, sh - 30f));
		_home.End(ref windowRect);
	}

	private void SaveWindowLayout()
	{
		cfgWindowX.Value = windowRect.x;
		cfgWindowY.Value = windowRect.y;
		if (!cfgCollapsed.Value)
		{
			cfgWindowW.Value = windowRect.width;
			cfgWindowH.Value = windowRect.height;
		}
		layoutDirty = false;
		try
		{
			((BaseUnityPlugin)this).Config.Save();
		}
		catch
		{
		}
	}

	private float GetGuiScale()
	{
		float num = 1f;
		if (cfgAutoDpi.Value)
		{
			num = Mathf.Clamp(ZeroT.UiKit.RlChrome.Dpi(windowRect.x * guiScaleCached, windowRect.y * guiScaleCached), 0.85f, 2.25f);
		}
		return Mathf.Clamp(num * Mathf.Clamp(cfgUiScale.Value, 0.3f, 1.75f), 0.3f, 2.75f);
	}

	private void EnsureStyles()
	{
		if (!stylesReady)
		{
			windowStyle = GUIStyle.none;
			titleStyle = new GUIStyle(GUI.skin.label);
			subtitleStyle = new GUIStyle(GUI.skin.label);
			subtitleStyle.fontSize = 11;
			sectionStyle = new GUIStyle(GUI.skin.box);
			sectionStyle.alignment = (TextAnchor)3;
			sectionStyle.padding = new RectOffset(8, 8, 5, 5);
			statusStyle = new GUIStyle(GUI.skin.box);
			statusStyle.alignment = (TextAnchor)3;
			statusStyle.wordWrap = true;
			statusStyle.padding = new RectOffset(8, 8, 6, 6);
			activeStatusStyle = new GUIStyle(statusStyle);
			activeStatusStyle.normal.textColor = new Color(0.72f, 1f, 0.76f, 1f);
			smallStyle = new GUIStyle(GUI.skin.label);
			smallStyle.fontSize = 10;
			smallStyle.wordWrap = true;
			stylesReady = true;
		}
	}

	private static Texture2D MakeTexture(Color color)
	{
		Texture2D val = new Texture2D(1, 1, (TextureFormat)4, false)
		{
			hideFlags = (HideFlags)61
		};
		val.SetPixel(0, 0, color);
		val.Apply(false, true);
		return val;
	}

	private void OnDestroy()
	{
		foreach (KeyValuePair<string, SourceState> source in sources)
		{
			DestroySourceState(source.Value);
		}
		sources.Clear();
		SaveWindowLayout();
		if ((Object)(object)windowTexture != (Object)null)
		{
			Object.Destroy((Object)(object)windowTexture);
		}
		if ((Object)(object)sectionTexture != (Object)null)
		{
			Object.Destroy((Object)(object)sectionTexture);
		}
		if ((Object)(object)statusTexture != (Object)null)
		{
			Object.Destroy((Object)(object)statusTexture);
		}
	}

	public PenisPathAlignmentSkinnedPlugin()
	{
	}

	internal static bool HasRequiredGender(Atom person, bool male)
	{
		if (IsPerson(person))
		{
			DAZCharacter componentInChildren = ((Component)person).GetComponentInChildren<DAZCharacter>();
			if ((Object)(object)componentInChildren != (Object)null)
			{
				return componentInChildren.isMale == male;
			}
		}
		return false;
	}
}
