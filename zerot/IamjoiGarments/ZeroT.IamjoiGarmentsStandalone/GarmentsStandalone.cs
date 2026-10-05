using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.IamjoiGarmentsStandalone;

[BepInPlugin("zerot.iamjoi.garments.standalone", "iamjoi Garments Standalone", "1.1.0")]
public sealed class GarmentsStandalone : BaseUnityPlugin
{
	private struct ClothingItemInfo
	{
		public string Raw;

		public string Label;

		public string[] Tokens;

		public string Category;
	}

	private struct AlphaTrack
	{
		public JSONStorable Storable;

		public JSONStorableFloat Param;

		public float Start;
	}

	private const int WindowId = 742193;

	private const float MinWidth = 500f;

	private const float MinHeight = 360f;

	private const float CollapsedWidth = 330f;

	private const float CollapsedHeight = 34f;

	private const float AlphaFadeDuration = 0.8f;

	private const string CategoryTop = "Top";

	private const string CategoryBottom = "Bottom";

	private const string CategoryUnderTop = "UnderTop";

	private const string CategoryUnderBottom = "UnderBottom";

	private static readonly string[] NonClothingKeepKeywords = new string[28]
	{
		"creator", "eye", "eyes", "iris", "lips", "makeup", "freckles", "brows", "lashes", "piercing",
		"sticker", "bracelet", "nails", "glasses", "necklace", "choker", "tongue", "creole", "nose", "pussy",
		"nostril", "teeth", "ring", "lacrimal", "detail", "pupil", "hair", "earring"
	};

	private static readonly string[] BottomKeywords = new string[30]
	{
		"bottom", "bottoms", "pant", "pants", "jean", "jeans", "trouser", "trousers", "legging", "leggings",
		"skirt", "shorts", "capri", "culotte", "culottes", "shoe", "boots", "boot", "socks", "short",
		"sandals", "sneaker", "marten", "heels", "alette", "alet", "belt", "soulcalibur", "chaps", "shinguard"
	};

	private static readonly string[] UnderTopKeywords = new string[9] { "undertop", "under_top", "under-top", "bra", "bralette", "bikinitop", "bikini top", "bustier", "tape" };

	private static readonly string[] UnderBottomKeywords = new string[15]
	{
		"underbottom", "under_bottom", "under-bottom", "panty", "pantie", "thong", "gstring", "g-string", "brief", "briefs",
		"underwear", "underpant", "underpants", "bikinibottom", "harness"
	};

	private static readonly string[] TopPriorityKeywords = new string[12]
	{
		"shirt", "tshirt", "t-shirt", "blouse", "top", "tank", "crop", "sweater", "hoodie", "jacket",
		"coat", "corset"
	};

	private ConfigEntry<bool> cfgShow;

	private ConfigEntry<bool> cfgActive;

	private ConfigEntry<bool> cfgCollapsed;

	private ConfigEntry<bool> cfgAutoDpi;

	private ConfigEntry<bool> cfgShowItems;

	private ConfigEntry<bool> cfgAutoBot;

	private float nextAutoBot;

	private MVRScript autoBotScript;

	private Atom autoBotScriptAtom;

	private float autoBotNextLookup;

	private float autoBotLastSeen = -100f;

	private bool autoBotOff;

	private string autoBotInfo = "Auto Bot: waiting for PenetrationCounter";

	private readonly Dictionary<string, string> autoBotTimes = new Dictionary<string, string>();

	private ConfigEntry<float> cfgScale;

	private ConfigEntry<float> cfgX;

	private ConfigEntry<float> cfgY;

	private ConfigEntry<float> cfgWidth;

	private ConfigEntry<float> cfgHeight;

	private ConfigEntry<string> cfgTargetUid;

	private Rect window;

	private Vector2 mainScroll;

	private Vector2 itemScroll;

	private bool resizing;

	private bool dirty;

	private Vector2 resizeMouse;

	private Vector2 resizeSize;

	private float effectiveScale = 1f;

	private float saveAt;

	private float nextPeopleScan;

	private float nextRescan;

	private int lastStorableCount = -1;

	private string status = "Waiting for a female Person atom.";

	private string exposedJson = "";

	private bool showBigJson;

	private readonly List<Atom> females = new List<Atom>();

	private readonly List<ClothingItemInfo> scannedItems = new List<ClothingItemInfo>();

	private readonly Dictionary<string, Coroutine> fadeRoutines = new Dictionary<string, Coroutine>();

	private JSONNode savedClothing;

	private int savedClothingCount;

	private GUIStyle windowStyle;

	private GUIStyle titleStyle;

	private GUIStyle sectionStyle;

	private GUIStyle textStyle;

	private GUIStyle wrapStyle;

	private GUIStyle buttonStyle;

	private GUIStyle smallButtonStyle;

	private GUIStyle textAreaStyle;

	private Texture2D background;

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr handle);

	private void Awake()
	{
		cfgActive = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Active", true, "Turn Garments off completely: no scanning, no auto bot, controls hidden.");
		cfgShow = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Show", true, "Show the in-game Garments window.");
		cfgCollapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Collapsed", false, "Remember collapsed state.");
		cfgAutoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "AutoDPI", true, "Scale from display DPI.");
		cfgScale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "UIScale", 1f, "Additional UI scale from 0.65 to 2.5.");
		cfgX = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "X", 55f, "Saved logical X position.");
		cfgY = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Y", 75f, "Saved logical Y position.");
		cfgWidth = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Width", 610f, "Saved expanded logical width.");
		cfgHeight = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Height", 690f, "Saved expanded logical height.");
		cfgShowItems = ((BaseUnityPlugin)this).Config.Bind<bool>("Options", "ShowIndividualItems", false, "Show per-clothing-item On/Off controls.");
		cfgAutoBot = ((BaseUnityPlugin)this).Config.Bind<bool>("Options", "AutoBotFromPenetrationCounter", false, "Drive the Bot and UBot (bottom / under-bottom) HUD from PenetrationCounter: Off while the target is penetrated (vagina or anus), back On a few seconds after it stops.");
		cfgTargetUid = ((BaseUnityPlugin)this).Config.Bind<string>("Target", "FemalePersonUID", "", "Remembered female Person target UID.");
		window = new Rect(cfgX.Value, cfgY.Value, cfgCollapsed.Value ? 340f : Mathf.Max(290f, cfgWidth.Value), cfgCollapsed.Value ? 34f : Mathf.Max(140f, cfgHeight.Value));
		nextPeopleScan = Time.realtimeSinceStartup + 1f;
	}

	private IEnumerator Start()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		SuperController singleton = SuperController.singleton;
		singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Combine((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		yield return (object)new WaitForSeconds(0.6f);
		ScanFemalePeople();
		EnsureTarget();
		if ((Object)(object)Target() != (Object)null)
		{
			ScanCurrentClothing();
		}
	}

	private void OnDestroy()
	{
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController singleton = SuperController.singleton;
			singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Remove((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		}
		SaveLayout();
	}

	private void SceneLoaded()
	{
		scannedItems.Clear();
		savedClothing = null;
		savedClothingCount = 0;
		lastStorableCount = -1;
		status = "Scene loaded. Scanning female Person atoms...";
		nextPeopleScan = 0f;
		nextRescan = Time.realtimeSinceStartup + 1f;
	}

	private void Update()
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (!cfgActive.Value)
		{
			return;
		}
		if (realtimeSinceStartup >= nextPeopleScan)
		{
			nextPeopleScan = realtimeSinceStartup + 2f;
			ScanFemalePeople();
			EnsureTarget();
		}
		if (cfgAutoBot.Value && realtimeSinceStartup >= nextAutoBot)
		{
			nextAutoBot = realtimeSinceStartup + 0.5f;
			AutoBotTick(realtimeSinceStartup);
		}
		if (realtimeSinceStartup >= nextRescan)
		{
			nextRescan = realtimeSinceStartup + 2f;
			AutoRescanIfNeeded();
		}
		if (resizing)
		{
			if (Input.GetMouseButton(0))
			{
				Vector2 val = LogicalMouse();
				window.width = resizeSize.x + val.x - resizeMouse.x;
				window.height = resizeSize.y + val.y - resizeMouse.y;
				ClampWindow();
				MarkDirty();
			}
			else
			{
				resizing = false;
			}
		}
		if (dirty && !resizing && !Input.GetMouseButton(0) && realtimeSinceStartup >= saveAt)
		{
			SaveLayout();
		}
	}

	private static DAZCharacterSelector FemaleSelector(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || atom.type != "Person")
		{
			return null;
		}
		JSONStorable storableByID = atom.GetStorableByID("character");
		object obj = ((storableByID is DAZCharacterSelector) ? storableByID : null);
		if (obj == null)
		{
			JSONStorable storableByID2 = atom.GetStorableByID("geometry");
			obj = ((storableByID2 is DAZCharacterSelector) ? storableByID2 : null);
		}
		DAZCharacterSelector val = (DAZCharacterSelector)obj;
		if (!((Object)(object)val != (Object)null) || (int)val.gender != 2)
		{
			return null;
		}
		return val;
	}

	private void ScanFemalePeople()
	{
		try
		{
			females.Clear();
			SuperController singleton = SuperController.singleton;
			if ((Object)(object)singleton == (Object)null)
			{
				return;
			}
			List<Atom> atoms = singleton.GetAtoms();
			if (atoms == null)
			{
				return;
			}
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom val = atoms[i];
				if ((Object)(object)FemaleSelector(val) != (Object)null && !string.IsNullOrEmpty(val.uid))
				{
					females.Add(val);
				}
			}
			females.Sort((Atom a, Atom b) => StringComparer.OrdinalIgnoreCase.Compare(a.uid, b.uid));
		}
		catch (Exception e)
		{
			Report("Female atom scan failed", e);
		}
	}

	private Atom Target()
	{
		string value = cfgTargetUid.Value;
		for (int i = 0; i < females.Count; i++)
		{
			if ((Object)(object)females[i] != (Object)null && females[i].uid == value)
			{
				return females[i];
			}
		}
		return null;
	}

	private void EnsureTarget()
	{
		if (!((Object)(object)Target() != (Object)null))
		{
			Atom val = (((Object)(object)SuperController.singleton == (Object)null) ? null : SuperController.singleton.GetSelectedAtom());
			if ((Object)(object)FemaleSelector(val) != (Object)null)
			{
				SetTarget(val);
			}
			else if (females.Count > 0)
			{
				SetTarget(females[0]);
			}
			else
			{
				status = "No female Person atoms are available.";
			}
		}
	}

	private void SetTarget(Atom atom)
	{
		if (!((Object)(object)FemaleSelector(atom) == (Object)null) && (!(cfgTargetUid.Value == atom.uid) || !((Object)(object)Target() == (Object)(object)atom)))
		{
			cfgTargetUid.Value = atom.uid;
			((BaseUnityPlugin)this).Config.Save();
			lastStorableCount = -1;
			mainScroll = Vector2.zero;
			itemScroll = Vector2.zero;
			exposedJson = "";
			savedClothing = null;
			savedClothingCount = 0;
			status = "Target: " + atom.uid;
			ScanCurrentClothing();
		}
	}

	private void UseSelectedFemale()
	{
		Atom val = (((Object)(object)SuperController.singleton == (Object)null) ? null : SuperController.singleton.GetSelectedAtom());
		if ((Object)(object)FemaleSelector(val) == (Object)null)
		{
			status = "Select a female Person atom in VaM first.";
		}
		else
		{
			SetTarget(val);
		}
	}

	private void ShiftTarget(int step)
	{
		if (females.Count != 0)
		{
			Atom val = Target();
			int num = ((!((Object)(object)val == (Object)null)) ? females.IndexOf(val) : 0);
			if (num < 0)
			{
				num = 0;
			}
			num = (num + step + females.Count) % females.Count;
			SetTarget(females[num]);
		}
	}

	private void AutoRescanIfNeeded()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		try
		{
			List<string> storableIDs = val.GetStorableIDs();
			if (storableIDs != null)
			{
				if (lastStorableCount < 0)
				{
					lastStorableCount = storableIDs.Count;
				}
				else if (storableIDs.Count != lastStorableCount)
				{
					lastStorableCount = storableIDs.Count;
					ScanCurrentClothing();
				}
			}
		}
		catch
		{
		}
	}

	private void OnGUI()
	{
		if (Screen.width <= 0 || Screen.height <= 0 || !cfgShow.Value)
		{
			return;
		}
		effectiveScale = EffectiveScale();
		EnsureStyles();
		Rect a = window;
		ClampWindow();
		if (!RectNearlyEqual(a, window))
		{
			MarkDirty();
		}
		Matrix4x4 matrix = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(effectiveScale, effectiveScale, 1f)) * matrix;
		Rect a2 = window;
		bool value = cfgCollapsed.Value;
		try
		{
			window = GUI.Window(742193, window, (GUI.WindowFunction)DrawWindow, "", windowStyle);
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Garments overlay: " + ex.Message));
		}
		finally
		{
			GUI.matrix = matrix;
		}
		if (value != cfgCollapsed.Value)
		{
			window.width = (cfgCollapsed.Value ? 340f : Mathf.Max(290f, cfgWidth.Value));
			window.height = (cfgCollapsed.Value ? 34f : Mathf.Max(140f, cfgHeight.Value));
		}
		ClampWindow();
		if (!RectNearlyEqual(a2, window))
		{
			MarkDirty();
		}
	}

	private void DrawWindow(int id)
	{
		float num = Mathf.Max(1f, window.width);
		ZeroT.UiKit.RlChrome.Backdrop(num, window.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(num, "Iamjoi Garments", cfgCollapsed.Value, cfgActive.Value);
		if ((rlButtons & 1) != 0)
		{
			ChangeScale(0.85f);
		}
		if ((rlButtons & 2) != 0)
		{
			ChangeScale(1.1764706f);
		}
		if ((rlButtons & 16) != 0)
		{
			cfgActive.Value = !cfgActive.Value;
			nextPeopleScan = 0f;
			((BaseUnityPlugin)this).Config.Save();
		}
		if ((rlButtons & 4) != 0)
		{
			ToggleCollapse();
		}
		if ((rlButtons & 8) != 0)
		{
			cfgShow.Value = false;
			((BaseUnityPlugin)this).Config.Save();
		}
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, num - 150f), 26f));
		if (!cfgCollapsed.Value && cfgActive.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(window.width, window.height));
			mainScroll = GUILayout.BeginScrollView(mainScroll, false, true, new GUILayoutOption[1] { GUILayout.ExpandHeight(true) });
			DrawTargetSection();
			DrawVarHudControls();
			DrawMainControls();
			DrawCategoryControls();
			DrawItems();
			DrawJsonSection();
			GUILayout.Space(14f);
			GUILayout.EndScrollView();
			GUILayout.EndArea();
			if (ZeroT.UiKit.RlChrome.ResizeHandle(window.width, window.height))
			{
				resizing = true;
				resizeMouse = LogicalMouse();
				resizeSize = new Vector2(window.width, window.height);
			}
		}
	}

	private void DrawTargetSection()
	{
		GUILayout.Label("Female Person target", sectionStyle, new GUILayoutOption[0]);
		Atom val = Target();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("◀", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(44f),
			GUILayout.Height(30f)
		}))
		{
			ShiftTarget(-1);
		}
		string text = (((Object)(object)val == (Object)null) ? "No female Person" : val.uid);
		float num = Mathf.Max(30f, wrapStyle.CalcHeight(new GUIContent(text), Mathf.Max(180f, window.width - 150f)) + 6f);
		GUILayout.Label(text, wrapStyle, new GUILayoutOption[2]
		{
			GUILayout.ExpandWidth(true),
			GUILayout.Height(num)
		});
		if (GUILayout.Button("▶", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.Width(44f),
			GUILayout.Height(30f)
		}))
		{
			ShiftTarget(1);
		}
		GUILayout.EndHorizontal();
		if (GUILayout.Button("Use selected female Person", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(32f) }))
		{
			UseSelectedFemale();
		}
		GUILayout.Label(status, wrapStyle, new GUILayoutOption[0]);
		GUILayout.Space(6f);
	}

	private MVRScript FindPenetrationCounter(Atom atom)
	{
		MVRScript[] scripts = UnityEngine.Object.FindObjectsOfType<MVRScript>();
		for (int i = 0; i < scripts.Length; i++)
		{
			MVRScript s = scripts[i];
			if ((Object)(object)s != (Object)null && ((object)s).GetType().Name == "PenetrationCounter" && (Object)(object)((JSONStorable)s).containingAtom == (Object)(object)atom)
			{
				return s;
			}
		}
		return null;
	}

	// PenetrationCounter keeps a running "Count/<partner>/<Vagina|Anus|Mouth>/Time" string per partner; it only changes while
	// that opening is being penetrated, so a changing value means "active".
	private void AutoBotTick(float now)
	{
		try
		{
			Atom atom = Target();
			if ((Object)(object)atom == (Object)null)
			{
				autoBotInfo = "Auto Bot: no target female";
				return;
			}
			MVRScript script = autoBotScript;
			if ((Object)(object)script == (Object)null || (Object)(object)autoBotScriptAtom != (Object)(object)atom)
			{
				if (now < autoBotNextLookup && (Object)(object)autoBotScriptAtom == (Object)(object)atom)
				{
					return;
				}
				autoBotNextLookup = now + 3f;
				script = FindPenetrationCounter(atom);
				autoBotScript = script;
				autoBotScriptAtom = atom;
				autoBotTimes.Clear();
			}
			if ((Object)(object)script == (Object)null)
			{
				autoBotInfo = "Auto Bot: PenetrationCounter not found on " + atom.uid;
				return;
			}
			bool changed = false;
			List<string> names = ((JSONStorable)script).GetStringParamNames();
			for (int i = 0; i < names.Count; i++)
			{
				string n = names[i];
				if (!n.StartsWith("Count/") || !(n.EndsWith("/Vagina/Time") || n.EndsWith("/Anus/Time")))
				{
					continue;
				}
				JSONStorableString p = ((JSONStorable)script).GetStringJSONParam(n);
				string v = ((p != null) ? p.val : null) ?? "";
				string old;
				if (autoBotTimes.TryGetValue(n, out old) && old != v)
				{
					changed = true;
				}
				autoBotTimes[n] = v;
			}
			if (changed)
			{
				autoBotLastSeen = now;
			}
			bool active = now - autoBotLastSeen < 1.5f;
			if (active && !autoBotOff)
			{
				StartCategoryFade("Bottom", -1f);
				StartCategoryFade("UnderBottom", -1f);
				autoBotOff = true;
			}
			else if (!active && autoBotOff && now - autoBotLastSeen > 3f)
			{
				StartCategoryFade("Bottom", 0f);
				StartCategoryFade("UnderBottom", 0f);
				autoBotOff = false;
			}
			autoBotInfo = "Auto Bot: " + (active ? "penetration detected" : "idle") + " (" + atom.uid + ")";
		}
		catch (Exception ex)
		{
			autoBotInfo = "Auto Bot error: " + ex.Message;
		}
	}

	private void DrawAutoBotStatus()
	{
		GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.MinHeight(44f) });
		GUILayout.Box("Bot\n" + (autoBotOff ? "Off" : "On") + "  (auto)", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.ExpandWidth(true),
			GUILayout.MinHeight(44f)
		});
		GUILayout.Box("UBot\n" + (autoBotOff ? "Off" : "On") + "  (auto)", buttonStyle, new GUILayoutOption[2]
		{
			GUILayout.ExpandWidth(true),
			GUILayout.MinHeight(44f)
		});
		GUILayout.EndHorizontal();
		GUILayout.Label(autoBotInfo, wrapStyle, new GUILayoutOption[0]);
	}

	private void DrawVarHudControls()
	{
		GUILayout.Label("Garments HUD", sectionStyle, new GUILayoutOption[0]);
		GUILayout.Label("VAR-style quick controls", wrapStyle, new GUILayoutOption[0]);
		DrawHudPair("Top\nOff", "Top\nOn", new Color(0.75f, 0.18f, 0.18f), new Color(0.18f, 0.25f, 0.18f), "Top");
		DrawHudPair("UTop\nOff", "UTop\nOn", new Color(0.7f, 0.35f, 0.1f), new Color(0.1f, 0.55f, 0.25f), "UnderTop");
		bool autoBot = GUILayout.Toggle(cfgAutoBot.Value, "Auto Bot (PenCounter)", new GUILayoutOption[1] { GUILayout.MinHeight(24f) });
		if (autoBot != cfgAutoBot.Value)
		{
			cfgAutoBot.Value = autoBot;
			autoBotOff = false;
			autoBotLastSeen = -100f;
			autoBotTimes.Clear();
			((BaseUnityPlugin)this).Config.Save();
		}
		if (cfgAutoBot.Value)
		{
			DrawAutoBotStatus();
		}
		else
		{
			DrawHudPair("Bot\nOff", "Bot\nOn", new Color(0.82f, 0.32f, 0.1f), new Color(0.1f, 0.68f, 0.4f), "Bottom");
			DrawHudPair("UBot\nOff", "UBot\nOn", new Color(0.55f, 0.1f, 0.55f), new Color(0.3f, 0.45f, 0.82f), "UnderBottom");
		}
		Color backgroundColor = GUI.backgroundColor;
		try
		{
			GUI.backgroundColor = new Color(0.1f, 0.58f, 0.68f);
			if (GUILayout.Button("Reload\nClothing", buttonStyle, new GUILayoutOption[2]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.MinHeight(44f)
			}))
			{
				ScanCurrentClothing();
			}
		}
		finally
		{
			GUI.backgroundColor = backgroundColor;
		}
		GUILayout.Space(8f);
	}

	private void DrawHudPair(string offText, string onText, Color offColor, Color onColor, string category)
	{
		GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.MinHeight(44f) });
		Color backgroundColor = GUI.backgroundColor;
		try
		{
			GUI.backgroundColor = offColor;
			if (GUILayout.Button(offText, buttonStyle, new GUILayoutOption[3]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.MinWidth(105f),
				GUILayout.MinHeight(44f)
			}))
			{
				StartCategoryFade(category, -1f);
			}
			GUI.backgroundColor = onColor;
			if (GUILayout.Button(onText, buttonStyle, new GUILayoutOption[3]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.MinWidth(105f),
				GUILayout.MinHeight(44f)
			}))
			{
				StartCategoryFade(category, 0f);
			}
		}
		finally
		{
			GUI.backgroundColor = backgroundColor;
			GUILayout.EndHorizontal();
		}
	}

	private void DrawMainControls()
	{
		GUILayout.Label("Clothing", sectionStyle, new GUILayoutOption[0]);
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Scan / Reload", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(34f) }))
		{
			ScanCurrentClothing();
		}
		if (GUILayout.Button("Remove All Clothing", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(34f) }))
		{
			ClearClothingViaGeometry();
		}
		GUILayout.EndHorizontal();
		if (savedClothing != (object)null && GUILayout.Button("Restore Last Removed Snapshot (" + savedClothingCount + ")", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(34f) }))
		{
			RestoreClothingViaGeometry();
		}
		bool flag = GUILayout.Toggle(cfgShowItems.Value, "Show individual item controls", new GUILayoutOption[1] { GUILayout.MinHeight(24f) });
		if (flag != cfgShowItems.Value)
		{
			cfgShowItems.Value = flag;
			((BaseUnityPlugin)this).Config.Save();
		}
		GUILayout.Space(6f);
	}

	private void DrawCategoryControls()
	{
		GUILayout.Label("Categories", sectionStyle, new GUILayoutOption[0]);
		DrawCategoryRow("Top");
		DrawCategoryRow("Bottom");
		DrawCategoryRow("UnderTop");
		DrawCategoryRow("UnderBottom");
		GUILayout.Space(6f);
	}

	private void DrawCategoryRow(string category)
	{
		int categoryCount = GetCategoryCount(category);
		if (categoryCount > 0)
		{
			float num = Mathf.Max(160f, window.width - 210f);
			float num2 = Mathf.Max(32f, wrapStyle.CalcHeight(new GUIContent(category + " (" + categoryCount + ")"), num) + 8f);
			GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.MinHeight(num2) });
			GUILayout.Label(category + " (" + categoryCount + ")", wrapStyle, new GUILayoutOption[2]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.Height(num2)
			});
			if (GUILayout.Button("Off", buttonStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(74f),
				GUILayout.Height(num2)
			}))
			{
				StartCategoryFade(category, -1f);
			}
			if (GUILayout.Button("On", buttonStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(74f),
				GUILayout.Height(num2)
			}))
			{
				StartCategoryFade(category, 0f);
			}
			GUILayout.EndHorizontal();
		}
	}

	private void DrawItems()
	{
		if (!cfgShowItems.Value)
		{
			return;
		}
		GUILayout.Label("Individual items", sectionStyle, new GUILayoutOption[0]);
		if (scannedItems.Count == 0)
		{
			GUILayout.Label("No removable clothing is currently scanned.", wrapStyle, new GUILayoutOption[0]);
			return;
		}
		float num = Mathf.Clamp((float)scannedItems.Count * 42f + 8f, 90f, 300f);
		itemScroll = GUILayout.BeginScrollView(itemScroll, new GUILayoutOption[1] { GUILayout.Height(num) });
		float num2 = Mathf.Max(180f, window.width - 220f);
		for (int i = 0; i < scannedItems.Count; i++)
		{
			ClothingItemInfo clothingItemInfo = scannedItems[i];
			float num3 = Mathf.Max(34f, wrapStyle.CalcHeight(new GUIContent(clothingItemInfo.Label), num2) + 8f);
			GUILayout.BeginHorizontal(new GUILayoutOption[1] { GUILayout.MinHeight(num3) });
			GUILayout.Label(clothingItemInfo.Label, wrapStyle, new GUILayoutOption[2]
			{
				GUILayout.ExpandWidth(true),
				GUILayout.Height(num3)
			});
			ClothingItemInfo item = clothingItemInfo;
			if (GUILayout.Button("Off", buttonStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(74f),
				GUILayout.Height(num3)
			}))
			{
				StartAlphaFade(item, -1f);
			}
			if (GUILayout.Button("On", buttonStyle, new GUILayoutOption[2]
			{
				GUILayout.Width(74f),
				GUILayout.Height(num3)
			}))
			{
				StartAlphaFade(item, 0f);
			}
			GUILayout.EndHorizontal();
		}
		GUILayout.EndScrollView();
		GUILayout.Space(6f);
	}

	private void DrawJsonSection()
	{
		GUILayout.Label("Clothing array JSON", sectionStyle, new GUILayoutOption[0]);
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Expose Current", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(32f) }))
		{
			ExposeCurrentClothingText();
		}
		if (GUILayout.Button("Apply JSON", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(32f) }))
		{
			ApplyJsonClothingFromExposedText();
		}
		GUILayout.EndHorizontal();
		if (string.IsNullOrEmpty(exposedJson))
		{
			exposedJson = "(Click Expose Current)";
		}
		if (exposedJson.Length > 6000 && !showBigJson)
		{
			GUILayout.Label("Clothing JSON is large (" + exposedJson.Length + " characters), so the editor is hidden to keep the game responsive.", wrapStyle, new GUILayoutOption[0]);
			if (GUILayout.Button("Show editor (may lag)", buttonStyle, new GUILayoutOption[1] { GUILayout.MinHeight(28f) }))
			{
				showBigJson = true;
			}
		}
		else
		{
			exposedJson = GUILayout.TextArea(exposedJson, textAreaStyle, new GUILayoutOption[2]
			{
				GUILayout.MinHeight(150f),
				GUILayout.ExpandHeight(false)
			});
		}
		GUILayout.Label("Edit the JSON directly, then press Apply JSON. Content scrolls with the window instead of overlapping other controls.", wrapStyle, new GUILayoutOption[0]);
	}

	private void EnsureStyles()
	{
		if (titleStyle == null)
		{
			windowStyle = GUIStyle.none;
			titleStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 14,
				alignment = (TextAnchor)3,
				clipping = (TextClipping)1
			};
			sectionStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 13,
				alignment = (TextAnchor)3,
				wordWrap = true,
				padding = new RectOffset(8, 8, 5, 5),
				margin = new RectOffset(0, 0, 4, 4)
			};
			textStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 12,
				wordWrap = true,
				clipping = (TextClipping)0
			};
			wrapStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 12,
				wordWrap = true,
				alignment = (TextAnchor)3,
				clipping = (TextClipping)1,
				padding = new RectOffset(5, 5, 3, 3)
			};
			buttonStyle = new GUIStyle(GUI.skin.button)
			{
				fontSize = 12,
				wordWrap = true,
				alignment = (TextAnchor)4,
				clipping = (TextClipping)1,
				padding = new RectOffset(8, 8, 5, 5)
			};
			smallButtonStyle = new GUIStyle(buttonStyle)
			{
				fontSize = 11,
				padding = new RectOffset(3, 3, 2, 2)
			};
			textAreaStyle = new GUIStyle(GUI.skin.textArea)
			{
				fontSize = 11,
				wordWrap = true,
				clipping = (TextClipping)1,
				padding = new RectOffset(6, 6, 6, 6)
			};
		}
	}

	private float EffectiveScale()
	{
		float num = 1f;
		if (cfgAutoDpi.Value)
		{
			num = Mathf.Clamp(ZeroT.UiKit.RlChrome.Dpi(window.x * effectiveScale, window.y * effectiveScale), 0.75f, 2.5f);
		}
		float num3 = num * Mathf.Clamp(cfgScale.Value, 0.25f, 2.5f);
		float num4 = (cfgCollapsed.Value ? 340f : 290f);
		float num5 = (cfgCollapsed.Value ? 34f : 140f);
		return Mathf.Max(0.1f, Mathf.Min(new float[3]
		{
			num3,
			(float)Screen.width / num4,
			(float)Screen.height / num5
		}));
	}

	private Vector2 LogicalMouse()
	{
		return new Vector2(Input.mousePosition.x / effectiveScale, ((float)Screen.height - Input.mousePosition.y) / effectiveScale);
	}

	private void ClampWindow()
	{
		float num = (float)Screen.width / Mathf.Max(0.01f, effectiveScale);
		float num2 = (float)Screen.height / Mathf.Max(0.01f, effectiveScale);
		if (!(num <= 0f) && !(num2 <= 0f))
		{
			if (cfgCollapsed.Value)
			{
				window.width = Mathf.Min(340f, num);
				window.height = Mathf.Min(34f, num2);
			}
			else
			{
				window.width = Mathf.Clamp(window.width, Mathf.Min(290f, num), num);
				window.height = Mathf.Clamp(window.height, Mathf.Min(140f, num2), num2);
			}
			window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, num - window.width));
			window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, num2 - window.height));
		}
	}

	private void ChangeScale(float factor)
	{
		cfgScale.Value = Mathf.Clamp(cfgScale.Value * factor, 0.25f, 2.5f);
		((BaseUnityPlugin)this).Config.Save();
		effectiveScale = EffectiveScale();
		ClampWindow();
		MarkDirty();
	}

	private void ToggleCollapse()
	{
		if (!cfgCollapsed.Value)
		{
			cfgWidth.Value = Mathf.Max(290f, window.width);
			cfgHeight.Value = Mathf.Max(140f, window.height);
		}
		cfgCollapsed.Value = !cfgCollapsed.Value;
		((BaseUnityPlugin)this).Config.Save();
		MarkDirty();
	}

	private void MarkDirty()
	{
		dirty = true;
		saveAt = Time.realtimeSinceStartup + 0.35f;
	}

	private void SaveLayout()
	{
		if (!cfgCollapsed.Value)
		{
			cfgWidth.Value = Mathf.Max(290f, window.width);
			cfgHeight.Value = Mathf.Max(140f, window.height);
		}
		cfgX.Value = window.x;
		cfgY.Value = window.y;
		((BaseUnityPlugin)this).Config.Save();
		dirty = false;
	}

	private static bool RectNearlyEqual(Rect a, Rect b)
	{
		if (Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f && Mathf.Abs(a.width - b.width) < 0.01f)
		{
			return Mathf.Abs(a.height - b.height) < 0.01f;
		}
		return false;
	}

	private List<JSONStorable> GetAtomStorables()
	{
		Atom val = Target();
		try
		{
			if ((Object)(object)val == (Object)null)
			{
				return null;
			}
			List<string> storableIDs = val.GetStorableIDs();
			if (storableIDs == null)
			{
				return null;
			}
			List<JSONStorable> list = new List<JSONStorable>();
			for (int i = 0; i < storableIDs.Count; i++)
			{
				if (!string.IsNullOrEmpty(storableIDs[i]))
				{
					JSONStorable storableByID = val.GetStorableByID(storableIDs[i]);
					if ((Object)(object)storableByID != (Object)null)
					{
						list.Add(storableByID);
					}
				}
			}
			return list;
		}
		catch
		{
			return null;
		}
	}

	private JSONClass SafeGetStorableJson(JSONStorable storable)
	{
		try
		{
			return ((Object)(object)storable == (Object)null) ? null : storable.GetJSON(true, true, false);
		}
		catch
		{
			return null;
		}
	}

	private void ScanCurrentClothing()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			status = "No female Person target.";
			scannedItems.Clear();
			return;
		}
		try
		{
			JSONStorable storableByID = val.GetStorableByID("geometry");
			JSONClass val2 = SafeGetStorableJson(storableByID);
			if ((Object)(object)storableByID == (Object)null || (JSONNode)(object)val2 == (object)null)
			{
				status = "Geometry is not ready on " + val.uid + ".";
				return;
			}
			JSONNode val3 = ((JSONNode)val2)["clothing"];
			scanSelector = storableByID as DAZCharacterSelector;
			List<ClothingItemInfo> list = new List<ClothingItemInfo>();
			JSONArray val4 = (JSONArray)(object)((val3 is JSONArray) ? val3 : null);
			if ((JSONNode)(object)val4 != (object)null)
			{
				for (int i = 0; i < ((JSONNode)val4).Count; i++)
				{
					if (((JSONNode)val4)[i] != (object)null && !ShouldKeepClothingItem(((JSONNode)val4)[i]))
					{
						list.Add(BuildClothingItemInfo(((JSONNode)val4)[i]));
					}
				}
			}
			else if (val3 != (object)null && !ShouldKeepClothingItem(val3))
			{
				list.Add(BuildClothingItemInfo(val3));
			}
			scanSelector = null;
			scannedItems.Clear();
			scannedItems.AddRange(list);
			lastStorableCount = val.GetStorableIDs()?.Count ?? (-1);
			status = "Scanned " + list.Count + " clothing item(s) on " + val.uid + ".";
		}
		catch (Exception e)
		{
			Report("Scan Current Clothing failed", e);
		}
	}

	private void ClearClothingViaGeometry()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			status = "No female Person target.";
			return;
		}
		try
		{
			JSONStorable storableByID = val.GetStorableByID("geometry");
			JSONClass val2 = SafeGetStorableJson(storableByID);
			if ((Object)(object)storableByID == (Object)null || (JSONNode)(object)val2 == (object)null)
			{
				status = "Geometry is not ready.";
				return;
			}
			JSONNode val3 = ((JSONNode)val2)["clothing"];
			if (val3 == (object)null)
			{
				status = "No clothing node found.";
				return;
			}
			SaveClothingSnapshot(val3);
			int num = 0;
			JSONArray val4 = (JSONArray)(object)((val3 is JSONArray) ? val3 : null);
			if ((JSONNode)(object)val4 != (object)null)
			{
				JSONArray val5 = new JSONArray();
				for (int i = 0; i < ((JSONNode)val4).Count; i++)
				{
					JSONNode val6 = ((JSONNode)val4)[i];
					if (ShouldKeepClothingItem(val6))
					{
						((JSONNode)val5).Add(val6);
					}
					else
					{
						num++;
					}
				}
				((JSONNode)val2)["clothing"] = (JSONNode)(object)val5;
			}
			else if (!ShouldKeepClothingItem(val3))
			{
				((JSONNode)val2)["clothing"] = (JSONNode)new JSONArray();
				num = 1;
			}
			try
			{
				storableByID.RestoreFromJSON(val2, true, true, (JSONArray)null, true);
			}
			catch
			{
			}
			try
			{
				storableByID.LateRestoreFromJSON(val2, true, true, true);
			}
			catch
			{
			}
			status = "Removed " + num + " clothing item(s).";
			ScanCurrentClothing();
		}
		catch (Exception e)
		{
			Report("Remove All Clothing failed", e);
		}
	}

	private void RestoreClothingViaGeometry()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null || savedClothing == (object)null)
		{
			status = "No saved clothing snapshot to restore.";
			return;
		}
		try
		{
			JSONStorable storableByID = val.GetStorableByID("geometry");
			JSONClass val2 = SafeGetStorableJson(storableByID);
			JSONNode val3 = CloneJsonNode(savedClothing);
			if ((Object)(object)storableByID == (Object)null || (JSONNode)(object)val2 == (object)null || val3 == (object)null)
			{
				status = "Could not restore clothing snapshot.";
				return;
			}
			((JSONNode)val2)["clothing"] = val3;
			try
			{
				storableByID.RestoreFromJSON(val2, true, true, (JSONArray)null, true);
			}
			catch
			{
			}
			try
			{
				storableByID.LateRestoreFromJSON(val2, true, true, true);
			}
			catch
			{
			}
			status = "Restored " + savedClothingCount + " clothing item(s).";
			ScanCurrentClothing();
		}
		catch (Exception e)
		{
			Report("Restore clothing failed", e);
		}
	}

	private void SaveClothingSnapshot(JSONNode node)
	{
		savedClothing = CloneJsonNode(node);
		JSONNode val = savedClothing;
		JSONArray val2 = (JSONArray)(object)((val is JSONArray) ? val : null);
		savedClothingCount = (((JSONNode)(object)val2 != (object)null) ? ((JSONNode)val2).Count : ((savedClothing != (object)null) ? 1 : 0));
	}

	private void ExposeCurrentClothingText()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			exposedJson = "(no female target)";
			return;
		}
		try
		{
			JSONStorable storableByID = val.GetStorableByID("geometry");
			JSONClass val2 = SafeGetStorableJson(storableByID);
			JSONNode val3 = (((JSONNode)(object)val2 == (object)null) ? null : ((JSONNode)val2)["clothing"]);
			if (val3 == (object)null)
			{
				exposedJson = "[]";
				return;
			}
			exposedJson = BuildRealClothingText(val3);
			status = "Exposed current clothing JSON.";
		}
		catch (Exception e)
		{
			exposedJson = "[]";
			Report("Expose clothing JSON failed", e);
		}
	}

	private string BuildRealClothingText(JSONNode node)
	{
		JSONArray val = new JSONArray();
		JSONArray val2 = (JSONArray)(object)((node is JSONArray) ? node : null);
		if ((JSONNode)(object)val2 != (object)null)
		{
			for (int i = 0; i < ((JSONNode)val2).Count; i++)
			{
				if (((JSONNode)val2)[i] != (object)null && !ShouldKeepClothingItem(((JSONNode)val2)[i]))
				{
					((JSONNode)val).Add((JSONNode)(object)BuildExposedClothingEntry(((JSONNode)val2)[i]));
				}
			}
		}
		else if (!ShouldKeepClothingItem(node))
		{
			((JSONNode)val).Add((JSONNode)(object)BuildExposedClothingEntry(node));
		}
		return ((object)val).ToString();
	}

	private JSONClass BuildExposedClothingEntry(JSONNode item)
	{
		JSONClass val = new JSONClass();
		JSONClass obj = (JSONClass)(object)((item is JSONClass) ? item : null);
		string text = GetJsonString(obj, "id");
		if (string.IsNullOrEmpty(text))
		{
			text = GetClothingItemLabel(item);
		}
		string text2 = GetJsonString(obj, "internalId");
		if (string.IsNullOrEmpty(text2))
		{
			text2 = DeriveInternalId(text);
		}
		string text3 = GetJsonString(obj, "enabled");
		if (string.IsNullOrEmpty(text3))
		{
			text3 = "true";
		}
		((JSONNode)val)["id"] = (JSONNode)new JSONData(text ?? "");
		((JSONNode)val)["internalId"] = (JSONNode)new JSONData(text2 ?? "");
		((JSONNode)val)["enabled"] = (JSONNode)new JSONData(text3);
		return val;
	}

	private string DeriveInternalId(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return "";
		}
		string text = ExtractPackageName(id);
		string fileBaseName = GetFileBaseName(id);
		if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(fileBaseName))
		{
			return text + ":" + fileBaseName;
		}
		if (string.IsNullOrEmpty(fileBaseName))
		{
			return id;
		}
		return fileBaseName;
	}

	private void ApplyJsonClothingFromExposedText()
	{
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			status = "No female Person target.";
			return;
		}
		if (string.IsNullOrEmpty(exposedJson) || exposedJson.StartsWith("("))
		{
			status = "Expose or enter valid clothing JSON first.";
			return;
		}
		try
		{
			JSONNode val2 = null;
			try
			{
				val2 = JSON.Parse(exposedJson);
			}
			catch
			{
			}
			if (val2 == (object)null)
			{
				status = "Clothing text is not valid JSON.";
				return;
			}
			JSONArray val3 = new JSONArray();
			JSONArray val4 = (JSONArray)(object)((val2 is JSONArray) ? val2 : null);
			if ((JSONNode)(object)val4 != (object)null)
			{
				for (int i = 0; i < ((JSONNode)val4).Count; i++)
				{
					if (((JSONNode)val4)[i] != (object)null)
					{
						((JSONNode)val3).Add(((JSONNode)val4)[i]);
					}
				}
			}
			else
			{
				if (!(val2 is JSONClass))
				{
					status = "JSON must be an object or array.";
					return;
				}
				((JSONNode)val3).Add(val2);
			}
			JSONStorable storableByID = val.GetStorableByID("geometry");
			JSONClass val5 = SafeGetStorableJson(storableByID);
			if ((Object)(object)storableByID == (Object)null || (JSONNode)(object)val5 == (object)null)
			{
				status = "Geometry is not ready.";
				return;
			}
			JSONNode val6 = ((JSONNode)val5)["clothing"];
			JSONArray val7 = (JSONArray)(object)((val6 is JSONArray) ? val6 : null);
			if ((JSONNode)(object)val7 == (object)null)
			{
				val7 = new JSONArray();
				if (((JSONNode)val5)["clothing"] != (object)null)
				{
					((JSONNode)val7).Add(((JSONNode)val5)["clothing"]);
				}
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			for (int j = 0; j < ((JSONNode)val3).Count; j++)
			{
				JSONNode val8 = ((JSONNode)val3)[j];
				JSONClass val9 = (JSONClass)(object)((val8 is JSONClass) ? val8 : null);
				if ((JSONNode)(object)val9 == (object)null || ShouldKeepClothingItem((JSONNode)(object)val9))
				{
					num3++;
					continue;
				}
				string jsonString = GetJsonString(val9, "id");
				JSONNode val10 = CloneJsonNode((JSONNode)(object)val9);
				if (val10 == (object)null)
				{
					num3++;
					continue;
				}
				bool flag = false;
				if (!string.IsNullOrEmpty(jsonString))
				{
					for (int k = 0; k < ((JSONNode)val7).Count; k++)
					{
						JSONNode val11 = ((JSONNode)val7)[k];
						JSONClass val12 = (JSONClass)(object)((val11 is JSONClass) ? val11 : null);
						if (!((JSONNode)(object)val12 == (object)null))
						{
							string jsonString2 = GetJsonString(val12, "id");
							if (!string.IsNullOrEmpty(jsonString2) && string.Equals(jsonString2, jsonString, StringComparison.OrdinalIgnoreCase))
							{
								((JSONNode)val7)[k] = val10;
								num2++;
								flag = true;
								break;
							}
						}
					}
				}
				if (!flag)
				{
					((JSONNode)val7).Add(val10);
					num++;
				}
			}
			((JSONNode)val5)["clothing"] = (JSONNode)(object)val7;
			try
			{
				storableByID.RestoreFromJSON(val5, true, true, (JSONArray)null, true);
			}
			catch
			{
			}
			try
			{
				storableByID.LateRestoreFromJSON(val5, true, true, true);
			}
			catch
			{
			}
			status = "Applied JSON: added " + num + ", replaced " + num2 + ", skipped " + num3 + ".";
			ScanCurrentClothing();
		}
		catch (Exception e)
		{
			Report("Apply clothing JSON failed", e);
		}
	}

	private JSONNode CloneJsonNode(JSONNode node)
	{
		if (node == (object)null)
		{
			return null;
		}
		try
		{
			return JSON.Parse(((object)node).ToString());
		}
		catch
		{
			return null;
		}
	}

	private bool ShouldKeepClothingItem(JSONNode node)
	{
		if (node == (object)null)
		{
			return false;
		}
		if (!NodeContainsClothingPath(node))
		{
			return true;
		}
		if (NodeContainsKeepToken(node))
		{
			return true;
		}
		return false;
	}

	private bool NodeContainsClothingPath(JSONNode node)
	{
		JSONClass val = (JSONClass)(object)((node is JSONClass) ? node : null);
		if ((JSONNode)(object)val != (object)null)
		{
			foreach (KeyValuePair<string, JSONNode> item in val)
			{
				if (NodeContainsClothingPath(item.Value))
				{
					return true;
				}
			}
			return false;
		}
		JSONArray val2 = (JSONArray)(object)((node is JSONArray) ? node : null);
		if ((JSONNode)(object)val2 != (object)null)
		{
			for (int i = 0; i < ((JSONNode)val2).Count; i++)
			{
				if (NodeContainsClothingPath(((JSONNode)val2)[i]))
				{
					return true;
				}
			}
			return false;
		}
		string text = ((node == (object)null) ? "" : (node.Value ?? "")).ToLowerInvariant();
		if (!text.Contains("/custom/clothing/"))
		{
			return text.Contains("custom/clothing/");
		}
		return true;
	}

	private bool NodeContainsKeepToken(JSONNode node)
	{
		JSONClass val = (JSONClass)(object)((node is JSONClass) ? node : null);
		if ((JSONNode)(object)val != (object)null)
		{
			foreach (KeyValuePair<string, JSONNode> item in val)
			{
				if (NodeContainsKeepToken(item.Value))
				{
					return true;
				}
			}
			return false;
		}
		JSONArray val2 = (JSONArray)(object)((node is JSONArray) ? node : null);
		if ((JSONNode)(object)val2 != (object)null)
		{
			for (int i = 0; i < ((JSONNode)val2).Count; i++)
			{
				if (NodeContainsKeepToken(((JSONNode)val2)[i]))
				{
					return true;
				}
			}
			return false;
		}
		string text = (node.Value ?? "").ToLowerInvariant();
		return ContainsAnyKeyword(text, NonClothingKeepKeywords);
	}

	private ClothingItemInfo BuildClothingItemInfo(JSONNode node)
	{
		string clothingItemLabel = GetClothingItemLabel(node);
		string fileBaseName = GetFileBaseName(clothingItemLabel);
		List<string> list = BuildTokenList(clothingItemLabel, fileBaseName, (JSONClass)(object)((node is JSONClass) ? node : null));
		ClothingItemInfo result = default;
		result.Raw = clothingItemLabel;
		result.Label = fileBaseName;
		result.Tokens = list.ToArray();
		string[] itemTags = null;
		string itemDisplay = null;
		try
		{
			if ((Object)(object)scanSelector != (Object)null && !string.IsNullOrEmpty(clothingItemLabel))
			{
				DAZClothingItem clothingItem = scanSelector.GetClothingItem(clothingItemLabel);
				if ((Object)(object)clothingItem != (Object)null)
				{
					itemTags = ((DAZDynamicItem)clothingItem).tagsArray;
					itemDisplay = ((DAZDynamicItem)clothingItem).displayName;
				}
			}
		}
		catch
		{
		}
		result.Category = ClassifyDetailed(fileBaseName, clothingItemLabel, itemTags, itemDisplay) ?? "Top";
		return result;
	}

	private string GetClothingItemLabel(JSONNode node)
	{
		if (node == (object)null)
		{
			return "<null>";
		}
		JSONClass val = (JSONClass)(object)((node is JSONClass) ? node : null);
		if ((JSONNode)(object)val != (object)null)
		{
			string jsonString = GetJsonString(val, "id");
			if (!string.IsNullOrEmpty(jsonString))
			{
				return jsonString;
			}
			jsonString = GetJsonString(val, "name");
			if (!string.IsNullOrEmpty(jsonString))
			{
				return jsonString;
			}
			jsonString = GetJsonString(val, "uid");
			if (!string.IsNullOrEmpty(jsonString))
			{
				return jsonString;
			}
		}
		string value = node.Value;
		if (!string.IsNullOrEmpty(value))
		{
			return value;
		}
		return ((object)node).ToString();
	}

	private string GetFileBaseName(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return "<unknown>";
		}
		string text = raw;
		int num = text.LastIndexOf('/');
		if (num >= 0 && num < text.Length - 1)
		{
			text = text.Substring(num + 1);
		}
		int num2 = text.LastIndexOf('.');
		if (num2 > 0)
		{
			text = text.Substring(0, num2);
		}
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return raw;
	}

	private List<string> BuildTokenList(string raw, string label, JSONClass obj)
	{
		List<string> list = new List<string>();
		AddToken(list, raw);
		if (!string.IsNullOrEmpty(raw))
		{
			int num = raw.IndexOf(":/", StringComparison.Ordinal);
			if (num > 0)
			{
				AddToken(list, raw.Substring(0, num));
			}
			string text = ((num > 0) ? raw.Substring(num + 2) : raw);
			AddToken(list, GetFileBaseName(text));
			AddToken(list, GetLastDirectoryName(text));
		}
		AddToken(list, label);
		if ((JSONNode)(object)obj != (object)null)
		{
			AddToken(list, GetJsonString(obj, "id"));
			AddToken(list, GetJsonString(obj, "name"));
			AddToken(list, GetJsonString(obj, "uid"));
		}
		AddToken(list, NormalizeToken(label));
		AddToken(list, NormalizeToken(raw));
		return list;
	}

	private DAZCharacterSelector scanSelector;

	private sealed class WordInfo
	{
		public int Cat;

		public float W;
	}

	// 0 = Top, 1 = Bottom, 2 = UnderTop, 3 = UnderBottom
	private static readonly string[] CategoryNames = new string[4] { "Top", "Bottom", "UnderTop", "UnderBottom" };

	private static readonly int[] TieOrder = new int[4] { 3, 2, 0, 1 };

	private static readonly Dictionary<string, WordInfo> WordTable = BuildWordTable();

	// Long, unambiguous stems that may sit inside a longer name ("MiniSkirt01", "thighhighstockings").
	private static readonly string[] SubstringKeys = new string[28]
	{
		"skirt", "legging", "trouser", "underwear", "bralette", "hoodie", "sweater", "blouse", "jacket", "corset",
		"sandal", "sneaker", "stocking", "pantyhose", "bodysuit", "leotard", "swimsuit", "jumpsuit", "cardigan", "camisole",
		"tshirt", "croptop", "tanktop", "bikinitop", "bikinibottom", "nipplecover", "gstring", "thighhigh"
	};

	private static readonly int[] SubstringCats = new int[28]
	{
		1, 1, 1, 3, 2, 0, 0, 0, 0, 0,
		1, 1, 1, 1, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 2, 3, 2, 3, 1
	};

	private static void AddWords(Dictionary<string, WordInfo> table, int cat, float weight, string words)
	{
		string[] array = words.Split(' ');
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].Length > 0 && !table.ContainsKey(array[i]))
			{
				table[array[i]] = new WordInfo { Cat = cat, W = weight };
			}
		}
	}

	private static Dictionary<string, WordInfo> BuildWordTable()
	{
		Dictionary<string, WordInfo> dictionary = new Dictionary<string, WordInfo>();
		// explicit under-garments first so they win over the generic words they contain
		AddWords(dictionary, 3, 3f, "panty panties pantie thong thongs gstring briefs brief underwear underpants underpant boxer boxers knicker knickers bikinibottom bikinibottoms lingeriebottom cheeky bloomers hipster garterbelt");
		AddWords(dictionary, 3, 2f, "harness garter");
		AddWords(dictionary, 2, 3f, "bra bras bralette brassiere bustier bikinitop sportsbra lingerietop pasties pastie nipplecover nipplecovers bandeau");
		AddWords(dictionary, 2, 1f, "tape");
		AddWords(dictionary, 0, 2f, "shirt shirts tshirt tee blouse top tank tanktop camisole cami crop croptop sweater hoodie jacket coat corset vest cardigan tunic polo jersey bodice blazer shrug poncho cape cloak robe kimono dress gown bodysuit leotard swimsuit jumpsuit romper catsuit onepiece overalls apron uniform halter tubetop babydoll");
		AddWords(dictionary, 0, 1f, "sleeve sleeves gloves glove scarf tie necktie bowtie collar");
		AddWords(dictionary, 1, 3f, "pantyhose thighhigh thighhighs overknee");
		AddWords(dictionary, 1, 2f, "pants pant jeans jean trousers trouser leggings legging tights skirt miniskirt shorts hotpants capri culottes culotte kilt sarong chaps bottom bottoms stockings stocking socks sock hose shoe shoes boot boots heel heels sandal sandals sneaker sneakers slipper slippers loafers flats pumps footwear legwarmer legwarmers anklet shinguard marten alette soulcalibur");
		AddWords(dictionary, 1, 1f, "short belt");
		// body-region tags VaM clothing items carry
		AddWords(dictionary, 0, 0.8f, "torso chest shoulders arms");
		AddWords(dictionary, 1, 0.8f, "hips legs thighs feet ankles");
		return dictionary;
	}

	private static List<string> SplitWords(string text)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		int start = 0;
		for (int i = 0; i <= text.Length; i++)
		{
			bool boundary = i == text.Length;
			if (!boundary)
			{
				char c = text[i];
				if (!char.IsLetterOrDigit(c))
				{
					boundary = true;
				}
				else if (i > start)
				{
					char p = text[i - 1];
					bool lowerToUpper = char.IsLower(p) && char.IsUpper(c);
					bool letterDigit = char.IsLetter(p) != char.IsLetter(c);
					bool acronym = char.IsUpper(p) && char.IsUpper(c) && i + 1 < text.Length && char.IsLower(text[i + 1]);
					if (lowerToUpper || letterDigit || acronym)
					{
						string w = text.Substring(start, i - start).ToLowerInvariant();
						if (w.Length > 0)
						{
							list.Add(w);
						}
						start = i;
					}
				}
			}
			if (boundary)
			{
				if (i > start)
				{
					string w2 = text.Substring(start, i - start).ToLowerInvariant();
					if (w2.Length > 0)
					{
						list.Add(w2);
					}
				}
				start = i + 1;
			}
		}
		return list;
	}

	private static void AddWordScore(string word, float weight, float[] score, bool compound)
	{
		WordInfo info;
		if (WordTable.TryGetValue(word, out info) || (word.Length > 3 && word.EndsWith("s") && WordTable.TryGetValue(word.Substring(0, word.Length - 1), out info)))
		{
			score[info.Cat] += info.W * weight;
		}
		else if (!compound)
		{
			for (int i = 0; i < SubstringKeys.Length; i++)
			{
				if (word.Length > SubstringKeys[i].Length && word.Contains(SubstringKeys[i]))
				{
					score[SubstringCats[i]] += 1.5f * weight;
					break;
				}
			}
		}
	}

	private static void ScoreText(string text, float weight, float[] score)
	{
		List<string> list = SplitWords(text);
		for (int i = 0; i < list.Count; i++)
		{
			AddWordScore(list[i], weight, score, compound: false);
			if (i + 1 < list.Count)
			{
				AddWordScore(list[i] + list[i + 1], weight, score, compound: true);
			}
		}
	}

	// Scores whole words (and adjacent-word compounds such as "bikini"+"top") from the item's file name, its package path, and
	// the clothing item's own VaM tags / display name when available. Returns null when nothing matched (caller defaults to Top, as before).
	private string ClassifyDetailed(string fileBaseName, string raw, string[] tags, string displayName)
	{
		float[] score = new float[4];
		ScoreText(fileBaseName, 1f, score);
		if (!string.IsNullOrEmpty(raw) && raw != fileBaseName)
		{
			ScoreText(raw, 0.6f, score);
		}
		ScoreText(displayName, 1.2f, score);
		if (tags != null)
		{
			for (int i = 0; i < tags.Length; i++)
			{
				ScoreText(tags[i], 2f, score);
			}
		}
		int best = -1;
		float bestScore = 0f;
		for (int j = 0; j < TieOrder.Length; j++)
		{
			int cat = TieOrder[j];
			if (score[cat] > bestScore + 0.001f)
			{
				bestScore = score[cat];
				best = cat;
			}
		}
		return (best < 0) ? null : CategoryNames[best];
	}

	private string ClassifyCategory(string[] tokens)
	{
		if (TokenListContainsKeyword(tokens, UnderBottomKeywords))
		{
			return "UnderBottom";
		}
		if (TokenListContainsKeyword(tokens, UnderTopKeywords))
		{
			return "UnderTop";
		}
		if (TokenListContainsKeyword(tokens, TopPriorityKeywords))
		{
			return "Top";
		}
		if (TokenListContainsKeyword(tokens, BottomKeywords))
		{
			return "Bottom";
		}
		return "Top";
	}

	private int GetCategoryCount(string category)
	{
		int num = 0;
		for (int i = 0; i < scannedItems.Count; i++)
		{
			if (scannedItems[i].Category == category)
			{
				num++;
			}
		}
		return num;
	}

	private void StartCategoryFade(string category, float target)
	{
		int num = 0;
		for (int i = 0; i < scannedItems.Count; i++)
		{
			if (scannedItems[i].Category == category)
			{
				StartAlphaFade(scannedItems[i], target);
				num++;
			}
		}
		status = category + " " + ((target < 0f) ? "Off" : "On") + " triggered for " + num + " item(s).";
	}

	private void StartAlphaFade(ClothingItemInfo item, float target)
	{
		if ((Object)(object)Target() == (Object)null)
		{
			return;
		}
		try
		{
			if (target < 0f)
			{
				ApplyJointAdjustOnClothingOff(item);
			}
			else
			{
				ApplyJointAdjustOnClothingOn(item);
			}
			List<JSONStorable> list = ResolveMaterialStorables(item);
			if (list.Count == 0)
			{
				status = "No material matched " + item.Label + ".";
				return;
			}
			string key = item.Raw ?? item.Label ?? "item";
			if (fadeRoutines.TryGetValue(key, out var value) && value != null)
			{
				((MonoBehaviour)this).StopCoroutine(value);
			}
			fadeRoutines[key] = ((MonoBehaviour)this).StartCoroutine(FadeAlphaCoroutine(list, target, item.Label));
		}
		catch (Exception e)
		{
			Report("Garment fade failed", e);
		}
	}

	private void ApplyJointAdjustOnClothingOff(ClothingItemInfo item)
	{
		if (item.Category == "Top" || item.Category == "UnderTop")
		{
			SetJointAdjustForItem(item, "enableBreastJointAdjust", value: false);
		}
		else if (item.Category == "Bottom" || item.Category == "UnderBottom")
		{
			SetJointAdjustForItem(item, "enableGluteJointAdjust", value: false);
			SetJointAdjustForItem(item, "disableAnatomy", value: false);
		}
	}

	private void ApplyJointAdjustOnClothingOn(ClothingItemInfo item)
	{
		if (item.Category == "Bottom" || item.Category == "UnderBottom")
		{
			SetJointAdjustForItem(item, "disableAnatomy", value: true);
		}
	}

	private void SetJointAdjustForItem(ClothingItemInfo item, string key, bool value)
	{
		List<JSONStorable> list = ResolveItemControlStorables(item);
		for (int i = 0; i < list.Count; i++)
		{
			TrySetBoolLikeParam(list[i], key, value);
		}
	}

	private List<JSONStorable> ResolveItemControlStorables(ClothingItemInfo item)
	{
		List<JSONStorable> list = new List<JSONStorable>();
		List<JSONStorable> atomStorables = GetAtomStorables();
		if (atomStorables == null)
		{
			return list;
		}
		string value = NormalizeToken(item.Label ?? "");
		string[] tokens = item.Tokens;
		for (int i = 0; i < atomStorables.Count; i++)
		{
			JSONStorable val = atomStorables[i];
			string storableIdSafe = GetStorableIdSafe(val);
			if (!string.IsNullOrEmpty(storableIdSafe) && storableIdSafe.EndsWith("ItemControl", StringComparison.OrdinalIgnoreCase))
			{
				bool flag = !string.IsNullOrEmpty(value) && NormalizeToken(storableIdSafe).Contains(value);
				if (!flag && IdMatchesTokens(storableIdSafe, tokens))
				{
					flag = true;
				}
				if (flag && !list.Contains(val))
				{
					list.Add(val);
				}
			}
		}
		return list;
	}

	private bool TrySetBoolLikeParam(JSONStorable s, string key, bool value)
	{
		if ((Object)(object)s == (Object)null || string.IsNullOrEmpty(key))
		{
			return false;
		}
		try
		{
			JSONStorableBool boolJSONParam = s.GetBoolJSONParam(key);
			if (boolJSONParam != null)
			{
				boolJSONParam.val = value;
				return true;
			}
		}
		catch
		{
		}
		try
		{
			JSONStorableFloat floatJSONParam = s.GetFloatJSONParam(key);
			if (floatJSONParam != null)
			{
				floatJSONParam.val = (value ? 1f : 0f);
				return true;
			}
		}
		catch
		{
		}
		try
		{
			JSONStorableString stringJSONParam = s.GetStringJSONParam(key);
			if (stringJSONParam != null)
			{
				stringJSONParam.val = (value ? "true" : "false");
				return true;
			}
		}
		catch
		{
		}
		try
		{
			JSONClass val = SafeGetStorableJson(s);
			string text = FindKeyIgnoreCase(val, key);
			if (!string.IsNullOrEmpty(text))
			{
				((JSONNode)val)[text] = (JSONNode)new JSONData(value);
				try
				{
					s.RestoreFromJSON(val, true, true, (JSONArray)null, true);
				}
				catch
				{
				}
				try
				{
					s.LateRestoreFromJSON(val, true, true, true);
				}
				catch
				{
				}
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private IEnumerator FadeAlphaCoroutine(List<JSONStorable> storables, float target, string label)
	{
		List<string> keys = BuildAlphaKeyCandidates();
		List<AlphaTrack> tracks = new List<AlphaTrack>();
		for (int i = 0; i < storables.Count; i++)
		{
			if (TryGetAlphaParam(storables[i], keys, out var param))
			{
				tracks.Add(new AlphaTrack
				{
					Storable = storables[i],
					Param = param,
					Start = param.val
				});
			}
			else
			{
				TrySetAlphaAdjust(storables[i], target);
			}
		}
		if (tracks.Count == 0)
		{
			yield break;
		}
		float elapsed = 0f;
		while (elapsed < 0.8f)
		{
			float num = elapsed / 0.8f;
			for (int j = 0; j < tracks.Count; j++)
			{
				if (tracks[j].Param != null)
				{
					tracks[j].Param.val = Mathf.Lerp(tracks[j].Start, target, num);
				}
			}
			elapsed += Time.deltaTime;
			yield return null;
		}
		for (int k = 0; k < tracks.Count; k++)
		{
			if (tracks[k].Param != null)
			{
				tracks[k].Param.val = target;
			}
		}
		status = "Faded " + label + " to " + target.ToString("0.0") + ".";
	}

	private bool TryGetAlphaParam(JSONStorable s, List<string> keys, out JSONStorableFloat param)
	{
		param = null;
		if ((Object)(object)s == (Object)null)
		{
			return false;
		}
		for (int i = 0; i < keys.Count; i++)
		{
			try
			{
				JSONStorableFloat floatJSONParam = s.GetFloatJSONParam(keys[i]);
				if (floatJSONParam != null)
				{
					param = floatJSONParam;
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private List<JSONStorable> ResolveMaterialStorables(ClothingItemInfo item)
	{
		List<JSONStorable> list = new List<JSONStorable>();
		Atom val = Target();
		if ((Object)(object)val == (Object)null)
		{
			return list;
		}
		List<JSONStorable> atomStorables = GetAtomStorables();
		if (atomStorables == null)
		{
			return list;
		}
		List<string> list2 = BuildMaterialCandidateIds(item);
		for (int i = 0; i < list2.Count; i++)
		{
			JSONStorable storableByID = val.GetStorableByID(list2[i]);
			if ((Object)(object)storableByID != (Object)null && !list.Contains(storableByID))
			{
				list.Add(storableByID);
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		List<string> prefixes = FindItemControlPrefixesByLabel(item.Label, atomStorables);
		List<string> list3 = BuildMaterialCandidateIdsFromPrefixes(item.Label, prefixes);
		for (int j = 0; j < list3.Count; j++)
		{
			JSONStorable storableByID2 = val.GetStorableByID(list3[j]);
			if ((Object)(object)storableByID2 != (Object)null && !list.Contains(storableByID2))
			{
				list.Add(storableByID2);
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		list3 = BuildMaterialCandidateIdsFromItemControl(item.Label, atomStorables);
		for (int k = 0; k < list3.Count; k++)
		{
			JSONStorable storableByID3 = val.GetStorableByID(list3[k]);
			if ((Object)(object)storableByID3 != (Object)null && !list.Contains(storableByID3))
			{
				list.Add(storableByID3);
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		List<JSONStorable> list4 = FindMaterialStorablesByItemControl(item.Label, atomStorables);
		for (int l = 0; l < list4.Count; l++)
		{
			if (!list.Contains(list4[l]))
			{
				list.Add(list4[l]);
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		for (int m = 0; m < atomStorables.Count; m++)
		{
			JSONStorable val2 = atomStorables[m];
			string storableIdSafe = GetStorableIdSafe(val2);
			if (!string.IsNullOrEmpty(storableIdSafe) && storableIdSafe.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 && IdMatchesPackageBase(storableIdSafe, item) && !list.Contains(val2))
			{
				list.Add(val2);
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		string label = item.Label;
		if (!string.IsNullOrEmpty(label))
		{
			string text = NormalizeToken(label);
			if (text.Length >= 4)
			{
				for (int n = 0; n < atomStorables.Count; n++)
				{
					JSONStorable val3 = atomStorables[n];
					string storableIdSafe2 = GetStorableIdSafe(val3);
					if (!string.IsNullOrEmpty(storableIdSafe2) && storableIdSafe2.IndexOf("MaterialCombined", StringComparison.OrdinalIgnoreCase) >= 0 && NormalizeToken(storableIdSafe2).Contains(text) && !list.Contains(val3))
					{
						list.Add(val3);
					}
				}
			}
		}
		return list;
	}

	private List<string> BuildMaterialCandidateIds(ClothingItemInfo item)
	{
		List<string> list = new List<string>();
		List<string> list2 = ExtractPackagePrefixes(item.Raw);
		if (list2.Count == 0 || string.IsNullOrEmpty(item.Label))
		{
			return list;
		}
		for (int i = 0; i < list2.Count; i++)
		{
			AddMaterialId(list, list2[i], item.Label);
			AddMaterialId(list, list2[i], item.Label.Replace(" ", "_"));
			AddMaterialId(list, list2[i], item.Label.Replace(" ", ""));
			AddMaterialId(list, list2[i], NormalizeToken(item.Label));
		}
		return list;
	}

	private void AddMaterialId(List<string> ids, string package, string baseName)
	{
		if (!string.IsNullOrEmpty(package) && !string.IsNullOrEmpty(baseName))
		{
			string item = package + ":" + baseName + "MaterialCombined";
			if (!ids.Contains(item))
			{
				ids.Add(item);
			}
		}
	}

	private void AddRawMaterialId(List<string> ids, string id)
	{
		if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
		{
			ids.Add(id);
		}
	}

	private List<string> FindItemControlPrefixesByLabel(string label, List<JSONStorable> storables)
	{
		List<string> list = new List<string>();
		string value = NormalizeToken(label);
		if (string.IsNullOrEmpty(value) || storables == null)
		{
			return list;
		}
		for (int i = 0; i < storables.Count; i++)
		{
			string storableIdSafe = GetStorableIdSafe(storables[i]);
			if (string.IsNullOrEmpty(storableIdSafe) || !storableIdSafe.EndsWith("ItemControl", StringComparison.OrdinalIgnoreCase) || !NormalizeToken(storableIdSafe).Contains(value))
			{
				continue;
			}
			int num = storableIdSafe.IndexOf(':');
			if (num > 0)
			{
				string item = storableIdSafe.Substring(0, num);
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	private List<string> BuildMaterialCandidateIdsFromPrefixes(string label, List<string> prefixes)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(label))
		{
			return list;
		}
		if (prefixes != null && prefixes.Count > 0)
		{
			for (int i = 0; i < prefixes.Count; i++)
			{
				AddMaterialId(list, prefixes[i], label);
				AddMaterialId(list, prefixes[i], label.Replace(" ", "_"));
				AddMaterialId(list, prefixes[i], label.Replace(" ", ""));
				AddMaterialId(list, prefixes[i], NormalizeToken(label));
			}
		}
		else
		{
			AddRawMaterialId(list, label + "MaterialCombined");
			AddRawMaterialId(list, label.Replace(" ", "_") + "MaterialCombined");
			AddRawMaterialId(list, label.Replace(" ", "") + "MaterialCombined");
			AddRawMaterialId(list, NormalizeToken(label) + "MaterialCombined");
		}
		return list;
	}

	private List<string> BuildMaterialCandidateIdsFromItemControl(string label, List<JSONStorable> storables)
	{
		List<string> list = new List<string>();
		string value = NormalizeToken(label);
		if (string.IsNullOrEmpty(value) || storables == null)
		{
			return list;
		}
		for (int i = 0; i < storables.Count; i++)
		{
			string storableIdSafe = GetStorableIdSafe(storables[i]);
			if (!string.IsNullOrEmpty(storableIdSafe) && storableIdSafe.EndsWith("ItemControl", StringComparison.OrdinalIgnoreCase) && NormalizeToken(storableIdSafe).Contains(value))
			{
				string item = storableIdSafe.Substring(0, storableIdSafe.Length - "ItemControl".Length) + "MaterialCombined";
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	private List<JSONStorable> FindMaterialStorablesByItemControl(string label, List<JSONStorable> storables)
	{
		List<JSONStorable> list = new List<JSONStorable>();
		string value = NormalizeToken(label);
		if (string.IsNullOrEmpty(value) || storables == null)
		{
			return list;
		}
		for (int i = 0; i < storables.Count; i++)
		{
			string storableIdSafe = GetStorableIdSafe(storables[i]);
			if (string.IsNullOrEmpty(storableIdSafe) || !storableIdSafe.EndsWith("ItemControl", StringComparison.OrdinalIgnoreCase) || !NormalizeToken(storableIdSafe).Contains(value))
			{
				continue;
			}
			string token = storableIdSafe.Substring(0, storableIdSafe.Length - "ItemControl".Length);
			string value2 = NormalizeToken(token);
			for (int j = 0; j < storables.Count; j++)
			{
				JSONStorable val = storables[j];
				string storableIdSafe2 = GetStorableIdSafe(val);
				if (!string.IsNullOrEmpty(storableIdSafe2) && storableIdSafe2.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(value2) && NormalizeToken(storableIdSafe2).Contains(value2) && !list.Contains(val))
				{
					list.Add(val);
				}
			}
		}
		return list;
	}

	private bool TrySetAlphaAdjust(JSONStorable s, float value)
	{
		List<string> list = BuildAlphaKeyCandidates();
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				JSONStorableFloat floatJSONParam = s.GetFloatJSONParam(list[i]);
				if (floatJSONParam != null)
				{
					floatJSONParam.val = value;
					return true;
				}
			}
			catch
			{
			}
		}
		return TrySetAlphaAdjustFromJson(s, value, list);
	}

	private bool TrySetAlphaAdjustFromJson(JSONStorable s, float value, List<string> keys)
	{
		try
		{
			JSONClass val = SafeGetStorableJson(s);
			if ((JSONNode)(object)val == (object)null)
			{
				return false;
			}
			for (int i = 0; i < keys.Count; i++)
			{
				string text = FindKeyIgnoreCase(val, keys[i]);
				if (!string.IsNullOrEmpty(text))
				{
					((JSONNode)val)[text] = (JSONNode)new JSONData(value);
					try
					{
						s.RestoreFromJSON(val, true, true, (JSONArray)null, true);
					}
					catch
					{
					}
					try
					{
						s.LateRestoreFromJSON(val, true, true, true);
					}
					catch
					{
					}
					return true;
				}
			}
			foreach (KeyValuePair<string, JSONNode> item in val)
			{
				if (!string.IsNullOrEmpty(item.Key) && item.Key.ToLowerInvariant().Contains("alpha"))
				{
					((JSONNode)val)[item.Key] = (JSONNode)new JSONData(value);
					try
					{
						s.RestoreFromJSON(val, true, true, (JSONArray)null, true);
					}
					catch
					{
					}
					try
					{
						s.LateRestoreFromJSON(val, true, true, true);
					}
					catch
					{
					}
					return true;
				}
			}
			if (SetAlphaAdjustRecursive((JSONNode)(object)val, value) > 0)
			{
				try
				{
					s.RestoreFromJSON(val, true, true, (JSONArray)null, true);
				}
				catch
				{
				}
				try
				{
					s.LateRestoreFromJSON(val, true, true, true);
				}
				catch
				{
				}
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private int SetAlphaAdjustRecursive(JSONNode node, float value)
	{
		if (node == (object)null)
		{
			return 0;
		}
		int num = 0;
		JSONClass val = (JSONClass)(object)((node is JSONClass) ? node : null);
		if ((JSONNode)(object)val != (object)null)
		{
			foreach (KeyValuePair<string, JSONNode> item in val)
			{
				if (!string.IsNullOrEmpty(item.Key) && item.Key.ToLowerInvariant().Contains("alpha"))
				{
					((JSONNode)val)[item.Key] = (JSONNode)new JSONData(value);
					num++;
				}
				num += SetAlphaAdjustRecursive(item.Value, value);
			}
			return num;
		}
		JSONArray val2 = (JSONArray)(object)((node is JSONArray) ? node : null);
		if ((JSONNode)(object)val2 != (object)null)
		{
			for (int i = 0; i < ((JSONNode)val2).Count; i++)
			{
				num += SetAlphaAdjustRecursive(((JSONNode)val2)[i], value);
			}
		}
		return num;
	}

	private List<string> BuildAlphaKeyCandidates()
	{
		string[] array = new string[10] { "alphaAdjust", "AlphaAdjust", "Alpha Adjust", "alpha", "opacity", "alphaOffset", "alphaMult", "alphaMultiplier", "alphaCutoff", "cutoff" };
		string[] array2 = new string[15]
		{
			"", "mat", "material", "mat1", "mat2", "mat3", "material1", "material2", "material3", "slot1",
			"slot2", "slot3", "subMesh1", "subMesh2", "subMesh3"
		};
		List<string> list = new List<string>();
		for (int i = 0; i < array2.Length; i++)
		{
			for (int j = 0; j < array.Length; j++)
			{
				AddAlphaKey(list, array2[i], array[j]);
				for (int k = 0; k <= 8; k++)
				{
					AddAlphaKey(list, array2[i], array[j] + k);
					AddAlphaKey(list, array2[i], array[j] + " " + k);
				}
			}
		}
		return list;
	}

	private void AddAlphaKey(List<string> keys, string prefix, string baseKey)
	{
		if (string.IsNullOrEmpty(prefix))
		{
			if (!keys.Contains(baseKey))
			{
				keys.Add(baseKey);
			}
			return;
		}
		string item = prefix + baseKey;
		string item2 = prefix + "_" + baseKey;
		string item3 = prefix + " " + baseKey;
		if (!keys.Contains(item))
		{
			keys.Add(item);
		}
		if (!keys.Contains(item2))
		{
			keys.Add(item2);
		}
		if (!keys.Contains(item3))
		{
			keys.Add(item3);
		}
	}

	private string ExtractPackageName(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return null;
		}
		int num = raw.IndexOf(":/", StringComparison.Ordinal);
		if (num <= 0)
		{
			return null;
		}
		string text = raw.Substring(0, num);
		int num2 = text.IndexOf('.');
		if (num2 <= 0)
		{
			return text;
		}
		return text.Substring(0, num2);
	}

	private List<string> ExtractPackagePrefixes(string raw)
	{
		List<string> list = new List<string>();
		string text = ExtractPackageName(raw);
		if (!string.IsNullOrEmpty(text))
		{
			list.Add(text);
		}
		string text2 = ExtractCreatorFromPath(raw);
		if (!string.IsNullOrEmpty(text2) && !list.Contains(text2))
		{
			list.Add(text2);
		}
		return list;
	}

	private string ExtractCreatorFromPath(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return null;
		}
		string text = raw;
		int num = raw.IndexOf(":/", StringComparison.Ordinal);
		if (num >= 0 && num + 2 < raw.Length)
		{
			text = raw.Substring(num + 2);
		}
		string text2 = text.Replace("\\", "/");
		string text3 = text2.ToLowerInvariant();
		int num2 = text3.IndexOf("/custom/clothing/");
		int length = "/custom/clothing/".Length;
		if (num2 < 0)
		{
			num2 = text3.IndexOf("custom/clothing/");
			length = "custom/clothing/".Length;
		}
		if (num2 < 0)
		{
			return null;
		}
		int num3 = num2 + length;
		if (num3 >= text2.Length)
		{
			return null;
		}
		int num4 = text2.IndexOf('/', num3);
		if (num4 < 0)
		{
			return null;
		}
		string text4 = text2.Substring(num3, num4 - num3).ToLowerInvariant();
		if (text4 == "female" || text4 == "male")
		{
			num3 = num4 + 1;
		}
		int num5 = text2.IndexOf('/', num3);
		if (num5 < 0 || num5 <= num3)
		{
			return null;
		}
		return text2.Substring(num3, num5 - num3);
	}

	private bool IdMatchesTokens(string id, string[] tokens)
	{
		if (string.IsNullOrEmpty(id) || tokens == null)
		{
			return false;
		}
		string text = NormalizeToken(id);
		for (int i = 0; i < tokens.Length; i++)
		{
			if (!string.IsNullOrEmpty(tokens[i]))
			{
				if (id.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
				string value = NormalizeToken(tokens[i]);
				if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(value) && text.Contains(value))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool IdMatchesPackageBase(string id, ClothingItemInfo item)
	{
		if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(item.Raw))
		{
			return false;
		}
		List<string> list = ExtractPackagePrefixes(item.Raw);
		bool flag = false;
		for (int i = 0; i < list.Count; i++)
		{
			if (id.StartsWith(list[i] + ":", StringComparison.OrdinalIgnoreCase))
			{
				flag = true;
				break;
			}
		}
		if (!flag || id.IndexOf("material", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return false;
		}
		string value = NormalizeToken(item.Label);
		if (!string.IsNullOrEmpty(value))
		{
			return NormalizeToken(id).Contains(value);
		}
		return false;
	}

	private bool TokenListContainsKeyword(string[] tokens, string[] keywords)
	{
		if (tokens == null)
		{
			return false;
		}
		foreach (string text in tokens)
		{
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			string text2 = text.ToLowerInvariant();
			string text3 = NormalizeToken(text);
			foreach (string text4 in keywords)
			{
				if (text2.Contains(text4.ToLowerInvariant()))
				{
					return true;
				}
				string value = NormalizeToken(text4);
				if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(text3) && text3.Contains(value))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool ContainsAnyKeyword(string text, string[] keys)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < keys.Length; i++)
		{
			if (text.Contains(keys[i].ToLowerInvariant()))
			{
				return true;
			}
		}
		return false;
	}

	private void AddToken(List<string> tokens, string token)
	{
		if (!string.IsNullOrEmpty(token) && !tokens.Contains(token))
		{
			tokens.Add(token);
		}
	}

	private string NormalizeToken(string token)
	{
		if (string.IsNullOrEmpty(token))
		{
			return "";
		}
		return token.ToLowerInvariant().Replace(" ", "").Replace("_", "")
			.Replace("-", "");
	}

	private string GetLastDirectoryName(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		string text = path.Replace("\\", "/");
		int num = text.LastIndexOf('/');
		if (num <= 0)
		{
			return null;
		}
		string text2 = text.Substring(0, num);
		int num2 = text2.LastIndexOf('/');
		if (num2 < 0 || num2 >= text2.Length - 1)
		{
			return null;
		}
		return text2.Substring(num2 + 1);
	}

	private string GetJsonString(JSONClass obj, string key)
	{
		if ((JSONNode)(object)obj == (object)null || string.IsNullOrEmpty(key))
		{
			return null;
		}
		try
		{
			if (obj.HasKey(key) && ((JSONNode)obj)[key] != (object)null)
			{
				return ((JSONNode)obj)[key].Value;
			}
		}
		catch
		{
		}
		return null;
	}

	private string FindKeyIgnoreCase(JSONClass jc, string key)
	{
		if ((JSONNode)(object)jc == (object)null)
		{
			return null;
		}
		foreach (KeyValuePair<string, JSONNode> item in jc)
		{
			if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				return item.Key;
			}
		}
		return null;
	}

	private string GetStorableIdSafe(JSONStorable s)
	{
		try
		{
			return ((Object)(object)s == (Object)null) ? null : s.storeId;
		}
		catch
		{
			return null;
		}
	}

	private void Report(string context, Exception e)
	{
		status = context + ": " + e.Message;
		Logger.LogError((object)(context + ": " + e));
	}
}
