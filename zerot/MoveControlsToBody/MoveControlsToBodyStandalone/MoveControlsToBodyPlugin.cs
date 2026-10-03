using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MoveControlsToBodyStandalone;

[BepInPlugin("ky1001.movecontrolstobody.standalone", "Move Controls to Body Standalone", "1.0.0")]
public sealed class MoveControlsToBodyPlugin : BaseUnityPlugin
{
	private const float MinWidth = 280f;

	private const float MinHeight = 185f;

	private const float MinBarWidth = 56f;

	private const float MinBarHeight = 16f;

	private const float ContentWidth = 400f;

	private const float ContentHeight = 365f;

	private static readonly string[] ControlNames = new string[23]
	{
		"headControl", "chestControl", "hipControl", "rHandControl", "lHandControl", "rFootControl", "lFootControl", "rElbowControl", "lElbowControl", "rKneeControl",
		"lKneeControl", "neckControl", "rArmControl", "lArmControl", "pelvisControl", "abdomenControl", "abdomen2Control", "rThighControl", "lThighControl", "rToeControl",
		"lToeControl", "rShoulderControl", "lShoulderControl"
	};

	private static readonly string[] BodyNames = new string[23]
	{
		"head", "chest", "hip", "rHand", "lHand", "rFoot", "lFoot", "rForeArm", "lForeArm", "rShin",
		"lShin", "neck", "rShldr", "lShldr", "pelvis", "abdomen", "abdomen2", "rThigh", "lThigh", "rToe",
		"lToe", "rCollar", "lCollar"
	};

	private ConfigEntry<bool> cfgMoveBottom;

	private ConfigEntry<bool> cfgThreshold;

	private ConfigEntry<bool> cfgMoveGround;

	private ConfigEntry<float> cfgDistance;

	private ConfigEntry<float> cfgThresholdValue;

	private ZeroT.UiKit.RlUguiWindow win;

	private Canvas canvas;

	private RectTransform window;

	private RectTransform viewport;

	private RectTransform scrollContent;

	private RectTransform nativeRoot;

	private RectTransform personMenu;

	private ScrollRect scroll;

	private Text status;

	private Button personButton;

	private GameObject expandedArea;

	private Atom selectedPerson;

	private void Awake()
	{
		win = new ZeroT.UiKit.RlUguiWindow(this, "Move Controls to Body", "Window", 40f, 60f, 420f, 490f, 330f, 260f, 1000f, 1200f);
		cfgMoveBottom = ((BaseUnityPlugin)this).Config.Bind<bool>("Options", "MoveBottomToAtom", false, "Align the lowest body control with the Atom control.");
		cfgDistance = ((BaseUnityPlugin)this).Config.Bind<float>("Options", "BottomDistance", 0.05f, "Distance between bottom control and Atom control (-0.2 to 0.2).");
		cfgThreshold = ((BaseUnityPlugin)this).Config.Bind<bool>("Options", "UseThreshold", true, "Leave nearby controls in place.");
		cfgThresholdValue = ((BaseUnityPlugin)this).Config.Bind<float>("Options", "ThresholdDistance", 0.04f, "Distance threshold in meters (0 to 0.5).");
		cfgMoveGround = ((BaseUnityPlugin)this).Config.Bind<bool>("Options", "MoveAtomToGround", false, "Move the Atom control vertically to world Y=0.");
	}

	private IEnumerator Start()
	{
		while ((Object)(object)SuperController.singleton == (Object)null || (Object)(object)EventSystem.current == (Object)null)
		{
			yield return null;
		}
		BuildWindow();
		SuperController singleton = SuperController.singleton;
		singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Combine((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		yield return null;
		AutoSelectOnlyPerson();
	}

	private void Update()
	{
		if (win != null)
		{
			win.Tick();
		}
	}

	private void OnDisable()
	{
		if ((Object)(object)canvas != (Object)null)
		{
			win.SetActive(false);
		}
	}

	private void OnEnable()
	{
		if ((Object)(object)canvas != (Object)null)
		{
			win.SetActive(true);
		}
	}

	private void OnDestroy()
	{
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController singleton = SuperController.singleton;
			singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Remove((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		}
		if ((Object)(object)canvas != (Object)null)
		{
			win.Dispose();
		}
	}

	private void SceneLoaded()
	{
		selectedPerson = null;
		HidePeopleMenu();
		Label(personButton, "Person: select");
		SetStatus("Select a Person atom.");
		((MonoBehaviour)this).StartCoroutine(SelectAfterScene());
	}

	private IEnumerator SelectAfterScene()
	{
		yield return null;
		AutoSelectOnlyPerson();
	}

	private void AutoSelectOnlyPerson()
	{
		List<Atom> people = GetPeople();
		if (people.Count == 1)
		{
			SelectPerson(people[0]);
		}
	}

	private static List<Atom> GetPeople()
	{
		List<Atom> list = new List<Atom>();
		SuperController singleton = SuperController.singleton;
		if ((Object)(object)singleton == (Object)null)
		{
			return list;
		}
		foreach (string atomUID in singleton.GetAtomUIDs())
		{
			Atom atomByUid = singleton.GetAtomByUid(atomUID);
			if ((Object)(object)atomByUid != (Object)null && atomByUid.type == "Person")
			{
				list.Add(atomByUid);
			}
		}
		list.Sort((Atom a, Atom b) => StringComparer.OrdinalIgnoreCase.Compare(a.uid, b.uid));
		return list;
	}

	private void SelectPerson(Atom person)
	{
		selectedPerson = person;
		Label(personButton, "Person: " + person.uid);
		SetStatus("Ready");
		ApplyGeometry();
	}

	private void BuildWindow()
	{
		win.OnLayout = delegate(float w, float h)
		{
			LayoutBody();
		};
		win.OnHidden = HidePeopleMenu;
		win.OnClosed = HidePeopleMenu;
		win.Build("Move Controls to Body desktop");
		canvas = win.Canvas;
		window = win.Window;
		RectTransform val4 = win.Body;
		Vector2 val3 = new Vector2(0f, 1f);
		expandedArea = ((Component)val4).gameObject;
		RectTransform val5 = NewRect("Person row", expandedArea.transform);
		val5.anchorMin = new Vector2(0f, 1f);
		val5.anchorMax = new Vector2(1f, 1f);
		val5.pivot = new Vector2(0f, 1f);
		val5.offsetMin = new Vector2(5f, -68f);
		val5.offsetMax = new Vector2(-5f, -34f);
		personButton = NewButton("Person selector", (Transform)(object)val5, "Person: select", ShowPeopleMenu);
		SetRect(((Component)personButton).GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0.65f, 1f), new Vector2(0f, 0f), new Vector2(-3f, 0f));
		status = NewText("Status", (Transform)(object)val5, "Select a Person atom.", 12, (TextAnchor)3);
		SetRect(((Graphic)status).rectTransform, new Vector2(0.65f, 0f), Vector2.one, new Vector2(5f, 0f), Vector2.zero);
		Button val6 = NewButton("Move controls", expandedArea.transform, "Move Controls to Body", MoveControls);
		SetRect(((Component)val6).GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -105f), new Vector2(-7f, -73f));
		viewport = NewRect("Settings viewport", expandedArea.transform);
		SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(7f, 7f), new Vector2(-7f, -111f));
		((Graphic)((Component)viewport).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.ViewCol;
		((Component)viewport).gameObject.AddComponent<RectMask2D>();
		scroll = ((Component)viewport).gameObject.AddComponent<ScrollRect>();
		scroll.viewport = viewport;
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = (ScrollRect.MovementType)2;
		scroll.scrollSensitivity = 42f;
		scrollContent = NewRect("Settings content", (Transform)(object)viewport);
		RectTransform val7 = scrollContent;
		val3 = new Vector2(0f, 1f);
		scrollContent.pivot = val3;
		val3 = val3;
		scrollContent.anchorMax = val3;
		val7.anchorMin = val3;
		scroll.content = scrollContent;
		nativeRoot = NewRect("Scalable settings", (Transform)(object)scrollContent);
		RectTransform val8 = nativeRoot;
		val3 = new Vector2(0f, 1f);
		nativeRoot.pivot = val3;
		val3 = val3;
		nativeRoot.anchorMax = val3;
		val8.anchorMin = val3;
		nativeRoot.sizeDelta = new Vector2(400f, 365f);
		BuildSettings();
		win.AddResizeHandle();
		ApplyGeometry();
	}

	private void BuildSettings()
	{
		Text val = NewText("Instructions", (Transform)(object)nativeRoot, "One click moves controls onto their matching body parts.", 14, (TextAnchor)3);
		SetTop(((Graphic)val).rectTransform, 5f, 28f);
		MakeToggle("Bottom alignment", 39f, "Move bottom control to Atom control", cfgMoveBottom);
		MakeSlider("Bottom distance", 87f, "Distance between bottom control and Atom control", cfgDistance, -0.2f, 0.2f);
		MakeToggle("Threshold switch", 148f, "Use threshold", cfgThreshold);
		MakeSlider("Threshold distance", 196f, "Threshold distance (m)", cfgThresholdValue, 0f, 0.5f);
		MakeToggle("Move Atom to ground", 257f, "Move Atom control to ground", cfgMoveGround);
		Text val2 = NewText("Note", (Transform)(object)nativeRoot, "Drag the title to move. Use the arrow to resize\nand S- / S+ to scale.", 13, (TextAnchor)0);
		SetTop(((Graphic)val2).rectTransform, 310f, 49f);
	}

	private void MakeToggle(string name, float top, string label, ConfigEntry<bool> entry)
	{
		Button val = NewButton(name, (Transform)(object)nativeRoot, "", () =>
		{
			entry.Value = !entry.Value;
			((BaseUnityPlugin)this).Config.Save();
		});
				SetTop(((Component)val).GetComponent<RectTransform>(), top, 37f);
		Text text = ((Component)val).GetComponentInChildren<Text>(true);
		text.alignment = (TextAnchor)3;
		((Graphic)text).rectTransform.offsetMin = new Vector2(10f, 0f);
		((UnityEvent)val.onClick).AddListener((UnityAction)(() =>
		{
			text.text = ((!entry.Value) ? "[ ]  " : "[x]  ") + label;
		}));
		text.text = ((!entry.Value) ? "[ ]  " : "[x]  ") + label;
	}

	private void MakeSlider(string name, float top, string caption, ConfigEntry<float> entry, float min, float max)
	{
		Text val = NewText(name + " label", (Transform)(object)nativeRoot, caption, 13, (TextAnchor)3);
		SetTop(((Graphic)val).rectTransform, top, 22f);
		RectTransform val2 = NewRect(name, (Transform)(object)nativeRoot);
		SetTop(val2, top + 27f, 30f);
		RectTransform val3 = NewRect("Background", (Transform)(object)val2);
		SetRect(val3, Vector2.zero, Vector2.one, new Vector2(7f, 10f), new Vector2(-78f, -9f));
		((Graphic)((Component)val3).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.ViewCol;
		RectTransform val4 = NewRect("Fill Area", (Transform)(object)val2);
		SetRect(val4, Vector2.zero, Vector2.one, new Vector2(7f, 10f), new Vector2(-78f, -9f));
		RectTransform val5 = NewRect("Fill", (Transform)(object)val4);
		SetRect(val5, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		((Graphic)((Component)val5).gameObject.AddComponent<Image>()).color = new Color(0.55f, 0.55f, 0.55f, 1f);
		RectTransform val6 = NewRect("Handle Area", (Transform)(object)val2);
		SetRect(val6, Vector2.zero, Vector2.one, new Vector2(7f, 0f), new Vector2(-78f, 0f));
		RectTransform val7 = NewRect("Handle", (Transform)(object)val6);
		val7.sizeDelta = new Vector2(15f, 26f);
		Image val8 = ((Component)val7).gameObject.AddComponent<Image>();
		((Graphic)val8).color = new Color(0.82f, 0.82f, 0.82f, 1f);
		Slider val9 = ((Component)val2).gameObject.AddComponent<Slider>();
		val9.fillRect = val5;
		val9.handleRect = val7;
		((Selectable)val9).targetGraphic = (Graphic)(object)val8;
		val9.minValue = min;
		val9.maxValue = max;
		val9.value = Mathf.Clamp(entry.Value, min, max);
		Text value = NewText("Value", (Transform)(object)val2, val9.value.ToString("0.000"), 13, (TextAnchor)4);
		SetRect(((Graphic)value).rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-72f, 0f), new Vector2(-4f, 0f));
		((UnityEvent<float>)(object)val9.onValueChanged).AddListener((UnityAction<float>)((float v) =>
		{
			entry.Value = (float)Math.Round(v, 3);
			value.text = entry.Value.ToString("0.000");
			((BaseUnityPlugin)this).Config.Save();
		}));
	}

	private void MoveControls()
	{
		Atom val = selectedPerson;
		if ((Object)(object)val == (Object)null || val.type != "Person")
		{
			SetStatus("Select a Person atom first.");
			return;
		}
		try
		{
			FreeControllerV3 val2 = FindController(val, "control");
			FreeControllerV3[] array = new FreeControllerV3[ControlNames.Length];
			Rigidbody[] array2 = new Rigidbody[BodyNames.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = FindController(val, ControlNames[i]);
				array2[i] = FindBody(val, BodyNames[i]);
			}
			float num = ((!cfgThreshold.Value) ? 0f : Mathf.Clamp(cfgThresholdValue.Value, 0f, 0.5f));
			float num2 = num * num;
			int num3 = 0;
			for (int j = 0; j < array.Length; j++)
			{
				Vector3 position = ((Component)array2[j]).transform.position;
				Vector3 val3 = ((Component)array[j]).transform.position - position;
				if (val3.sqrMagnitude > num2)
				{
					((Component)array[j]).transform.position = position;
					num3++;
				}
			}
			if (cfgMoveBottom.Value)
			{
				float num4 = float.PositiveInfinity;
				for (int k = 0; k < 21; k++)
				{
					num4 = Mathf.Min(num4, ((Component)array[k]).transform.position.y);
				}
				float num5 = num4 - Mathf.Clamp(cfgDistance.Value, -0.2f, 0.2f);
				float num6 = num5 - ((Component)val2).transform.position.y;
				Vector3 val4 = new Vector3(0f, num6, 0f);
				for (int l = 0; l < array.Length; l++)
				{
					Transform transform = ((Component)array[l]).transform;
					transform.position -= val4;
				}
			}
			if (cfgMoveGround.Value)
			{
				Vector3 position2 = ((Component)val2).transform.position;
				Transform transform2 = ((Component)val2).transform;
				transform2.position -= new Vector3(0f, position2.y, 0f);
			}
			SetStatus("Moved " + num3 + "/23 controls.");
		}
		catch (Exception ex)
		{
			SetStatus("Could not move controls. See log.");
			Logger.LogError((object)("Move Controls to Body failed on " + val.uid + ": " + ex));
		}
	}

	private static FreeControllerV3 FindController(Atom atom, string name)
	{
		FreeControllerV3[] freeControllers = atom.freeControllers;
		foreach (FreeControllerV3 val in freeControllers)
		{
			if ((Object)(object)val != (Object)null && ((Object)val).name == name)
			{
				return val;
			}
		}
		throw new InvalidOperationException("Missing control: " + name);
	}

	private static Rigidbody FindBody(Atom atom, string name)
	{
		Rigidbody[] rigidbodies = atom.rigidbodies;
		foreach (Rigidbody val in rigidbodies)
		{
			if ((Object)(object)val != (Object)null && ((Object)val).name == name)
			{
				return val;
			}
		}
		throw new InvalidOperationException("Missing body: " + name);
	}

	private void ShowPeopleMenu()
	{
		HidePeopleMenu();
		List<Atom> people = GetPeople();
		if (people.Count == 0)
		{
			SetStatus("No Person atoms in scene.");
			return;
		}
		personMenu = NewRect("Person choices", (Transform)(object)window);
		RectTransform val = personMenu;
		Vector2 val2 = new Vector2(0f, 1f);
		personMenu.pivot = val2;
		val2 = val2;
		personMenu.anchorMax = val2;
		val.anchorMin = val2;
		personMenu.anchoredPosition = new Vector2(6f, -69f);
		float num = Mathf.Max(32f, window.sizeDelta.y - 78f);
		personMenu.sizeDelta = new Vector2(Mathf.Max(40f, window.sizeDelta.x - 12f), Mathf.Min((float)people.Count * 30f + 8f, num));
		((Graphic)((Component)personMenu).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.PanelCol;
		RectTransform val3 = NewRect("Mask", (Transform)(object)personMenu);
		SetRect(val3, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
		((Graphic)((Component)val3).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.ViewCol;
		((Component)val3).gameObject.AddComponent<RectMask2D>();
		ScrollRect val4 = ((Component)val3).gameObject.AddComponent<ScrollRect>();
		val4.horizontal = false;
		val4.vertical = true;
		val4.viewport = val3;
		val4.scrollSensitivity = 35f;
		RectTransform val5 = NewRect("Choices", (Transform)(object)val3);
		val5.anchorMin = new Vector2(0f, 1f);
		val5.anchorMax = new Vector2(1f, 1f);
		val5.pivot = new Vector2(0.5f, 1f);
		val5.sizeDelta = new Vector2(0f, (float)people.Count * 30f);
		val4.content = val5;
		for (int i = 0; i < people.Count; i++)
		{
			Atom person = people[i];
			Button val6 = NewButton("Person " + i, (Transform)(object)val5, person.uid, () =>
			{
				HidePeopleMenu();
				SelectPerson(person);
			});
			RectTransform component = ((Component)val6).GetComponent<RectTransform>();
			component.anchorMin = new Vector2(0f, 1f);
			component.anchorMax = new Vector2(1f, 1f);
			component.pivot = new Vector2(0.5f, 1f);
			component.anchoredPosition = new Vector2(0f, (float)(-i) * 30f);
			component.sizeDelta = new Vector2(0f, 29f);
		}
		((Transform)personMenu).SetAsLastSibling();
	}

	private void HidePeopleMenu()
	{
		if ((Object)(object)personMenu != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)personMenu).gameObject);
		}
		personMenu = null;
	}

	private void ApplyGeometry()
	{
		if (win != null)
		{
			win.Apply();
		}
	}

	private void LayoutBody()
	{
		UpdateContentLayout();
		if ((Object)(object)personMenu != (Object)null)
		{
			personMenu.sizeDelta = new Vector2(Mathf.Max(40f, window.sizeDelta.x - 12f), Mathf.Min(personMenu.sizeDelta.y, Mathf.Max(32f, window.sizeDelta.y - 78f)));
		}
	}

	private void UpdateContentLayout()
	{
		float num = Mathf.Max(1f, window.sizeDelta.x - 14f);
		float num2 = Mathf.Max(1f, window.sizeDelta.y - 118f);
		float num3 = Mathf.Min(2f, num / 400f);
		((Transform)nativeRoot).localScale = Vector3.one * num3;
		scrollContent.sizeDelta = new Vector2(Mathf.Max(num, 400f * num3), Mathf.Max(num2, 365f * num3));
	}

	private void SetStatus(string value)
	{
		if ((Object)(object)status != (Object)null)
		{
			status.text = value;
		}
	}

	private static RectTransform NewRect(string name, Transform parent)
	{
		GameObject val = new GameObject(name, new Type[1] { typeof(RectTransform) });
		val.transform.SetParent(parent, false);
		return (RectTransform)val.transform;
	}

	private static Text NewText(string name, Transform parent, string value, int size, TextAnchor alignment)
	{
		RectTransform val = NewRect(name, parent);
		Text val2 = ((Component)val).gameObject.AddComponent<Text>();
		val2.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		val2.fontSize = size;
		val2.alignment = alignment;
		val2.text = value;
		((Graphic)val2).color = ZeroT.UiKit.RlUguiWindow.TextCol;
		((Graphic)val2).raycastTarget = false;
		val2.resizeTextForBestFit = true;
		val2.resizeTextMinSize = 9;
		val2.resizeTextMaxSize = size;
		return val2;
	}

	private static Button NewButton(string name, Transform parent, string label, UnityAction action)
	{
		RectTransform val = NewRect(name, parent);
		Image val2 = ((Component)val).gameObject.AddComponent<Image>();
		((Graphic)val2).color = Color.white;
		Button val3 = ((Component)val).gameObject.AddComponent<Button>();
		((Selectable)val3).targetGraphic = (Graphic)(object)val2;
		ColorBlock rlColors = val3.colors;
		rlColors.normalColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		rlColors.highlightedColor = ZeroT.UiKit.RlUguiWindow.ButtonH;
		rlColors.pressedColor = ZeroT.UiKit.RlUguiWindow.ButtonP;
		rlColors.disabledColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		val3.colors = rlColors;
		Navigation rlNav = new Navigation();
		rlNav.mode = Navigation.Mode.None;
		val3.navigation = rlNav;
		((UnityEvent)val3.onClick).AddListener(action);
		Text val4 = NewText("Label", (Transform)(object)val, label, 13, (TextAnchor)4);
		SetRect(((Graphic)val4).rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
		return val3;
	}

	private static void Label(Button button, string value)
	{
		if (!((Object)(object)button == (Object)null))
		{
			Text componentInChildren = ((Component)button).GetComponentInChildren<Text>(true);
			if ((Object)(object)componentInChildren != (Object)null)
			{
				componentInChildren.text = value;
			}
		}
	}

	private static void SetRect(RectTransform r, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
	{
		r.anchorMin = aMin;
		r.anchorMax = aMax;
		r.offsetMin = oMin;
		r.offsetMax = oMax;
	}

	private static void SetTop(RectTransform r, float top, float h)
	{
		SetRect(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, 0f - top - h), new Vector2(-7f, 0f - top));
	}

	private static void PinRight(RectTransform r, float right, float top, float w, float h)
	{
		Vector2 val = (r.pivot = new Vector2(1f, 1f));
		val = (r.anchorMax = val);
		r.anchorMin = val;
		r.anchoredPosition = new Vector2(0f - right, 0f - top);
		r.sizeDelta = new Vector2(w, h);
	}

}
