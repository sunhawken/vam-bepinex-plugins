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

namespace Bplus.Standalone;

[BepInPlugin("bplus.smartwardrobe.standalone", "SmartWardrobe Standalone", "9.0.0")]
public sealed class SmartWardrobeSkinnedHost : BaseUnityPlugin
{
	private const float MinWidth = 340f;

	private const float MinHeight = 200f;

	private const float MinBarWidth = 64f;

	private const float MinBarHeight = 18f;

	private const float NativeWidth = 1080f;

	private ZeroT.UiKit.RlUguiWindow win;

	private Canvas canvas;

	private RectTransform window;

	private RectTransform viewport;

	private RectTransform scrollContent;

	private RectTransform nativeRoot;

	private RectTransform leftColumn;

	private RectTransform rightColumn;

	private RectTransform patchLeft;

	private RectTransform patchRight;

	private RectTransform menu;

	private Text status;

	private Button atomButton;

	private Button patchButton;

	private GameObject toolbar;

	private GameObject nativeArea;

	private ScrollRect scroll;

	private SmartWardrobe wardrobe;

	private ScenePatcher patcher;

	private Atom selectedPerson;

	private Atom registeredPerson;

	private bool wardrobeRegistered;

	private bool patchMode;

	private int generation;

	private float layoutNext;

	private void Awake()
	{
		win = new ZeroT.UiKit.RlUguiWindow(this, "SmartWardrobe", "Window", 40f, 60f, 650f, 720f, 400f, 280f, 1600f, 1400f);
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
		if (win == null || (Object)(object)window == (Object)null)
		{
			return;
		}
		win.Tick();
		if (((Object)(object)wardrobe != (Object)null || (Object)(object)patcher != (Object)null) && Time.unscaledTime >= layoutNext)
		{
			layoutNext = Time.unscaledTime + 0.5f;
			UpdateNativeLayout();
		}
	}

	private void OnDisable()
	{
		generation++;
		RemoveWardrobe();
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
			if ((Object)(object)selectedPerson != (Object)null && (Object)(object)wardrobe == (Object)null)
			{
				((MonoBehaviour)this).StartCoroutine(AttachPerson(selectedPerson, ++generation));
			}
		}
	}

	private void OnDestroy()
	{
		generation++;
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController singleton = SuperController.singleton;
			singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Remove((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		}
		RemoveWardrobe();
		if ((Object)(object)canvas != (Object)null)
		{
			win.Dispose();
		}
	}

	private void SceneLoaded()
	{
		generation++;
		RemoveWardrobe();
		selectedPerson = null;
		ClearColumns();
		HideMenu();
		SetStatus("Scene loaded. Select a Person atom.");
		if ((Object)(object)atomButton != (Object)null)
		{
			Label(atomButton, "Person: select");
		}
		((MonoBehaviour)this).StartCoroutine(AutoSelectAfterScene(generation));
	}

	private IEnumerator AutoSelectAfterScene(int token)
	{
		yield return null;
		if (token == generation)
		{
			AutoSelectOnlyPerson();
		}
	}

	private void AutoSelectOnlyPerson()
	{
		List<Atom> people = GetPeople();
		if (people.Count == 1)
		{
			((MonoBehaviour)this).StartCoroutine(AttachPerson(people[0], ++generation));
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

	private void BuildWindow()
	{
		win.OnLayout = delegate(float w, float h)
		{
			UpdateNativeLayout();
		};
		win.OnHidden = HideMenu;
		win.OnClosed = HideMenu;
		win.Build("SmartWardrobe Standalone Desktop");
		canvas = win.Canvas;
		window = win.Window;
		Vector2 val3 = new Vector2(0f, 1f);
		RectTransform val4 = NewRect("Toolbar", (Transform)(object)win.Body);
		val4.anchorMin = new Vector2(0f, 1f);
		val4.anchorMax = new Vector2(1f, 1f);
		val4.pivot = new Vector2(0f, 1f);
		val4.offsetMin = new Vector2(0f, -66f);
		val4.offsetMax = new Vector2(0f, -33f);
		toolbar = ((Component)val4).gameObject;
		atomButton = NewButton("Person selector", toolbar.transform, "Person: select", ShowPeopleMenu);
		patchButton = NewButton("Scene patcher tab", toolbar.transform, "Scene Patcher", TogglePatchMode);
		status = NewText("Status", toolbar.transform, "Select a Person atom.", 12, (TextAnchor)3);
		((Component)atomButton).GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
		((Component)atomButton).GetComponent<RectTransform>().anchorMax = new Vector2(0.42f, 1f);
		((Component)atomButton).GetComponent<RectTransform>().offsetMin = new Vector2(5f, -32f);
		((Component)atomButton).GetComponent<RectTransform>().offsetMax = new Vector2(-3f, -3f);
		((Component)patchButton).GetComponent<RectTransform>().anchorMin = new Vector2(0.42f, 1f);
		((Component)patchButton).GetComponent<RectTransform>().anchorMax = new Vector2(0.65f, 1f);
		((Component)patchButton).GetComponent<RectTransform>().offsetMin = new Vector2(1f, -32f);
		((Component)patchButton).GetComponent<RectTransform>().offsetMax = new Vector2(-3f, -3f);
		((Graphic)status).rectTransform.anchorMin = new Vector2(0.65f, 1f);
		((Graphic)status).rectTransform.anchorMax = new Vector2(1f, 1f);
		((Graphic)status).rectTransform.offsetMin = new Vector2(4f, -32f);
		((Graphic)status).rectTransform.offsetMax = new Vector2(-6f, -3f);
		nativeArea = ((Component)NewRect("Native controls", (Transform)(object)win.Body)).gameObject;
		viewport = nativeArea.GetComponent<RectTransform>();
		viewport.anchorMin = Vector2.zero;
		viewport.anchorMax = Vector2.one;
		viewport.offsetMin = new Vector2(6f, 6f);
		viewport.offsetMax = new Vector2(-6f, -68f);
		((Graphic)((Component)viewport).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.ViewCol;
		((Component)viewport).gameObject.AddComponent<RectMask2D>();
		scroll = ((Component)viewport).gameObject.AddComponent<ScrollRect>();
		scroll.viewport = viewport;
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = (ScrollRect.MovementType)2;
		scroll.scrollSensitivity = 45f;
		scrollContent = NewRect("Scrolled SmartWardrobe content", (Transform)(object)viewport);
		RectTransform val5 = scrollContent;
		val3 = new Vector2(0f, 1f);
		scrollContent.pivot = val3;
		val3 = val3;
		scrollContent.anchorMax = val3;
		val5.anchorMin = val3;
		scroll.content = scrollContent;
		nativeRoot = NewRect("Native UI columns", (Transform)(object)scrollContent);
		RectTransform val6 = nativeRoot;
		val3 = new Vector2(0f, 1f);
		nativeRoot.pivot = val3;
		val3 = val3;
		nativeRoot.anchorMax = val3;
		val6.anchorMin = val3;
		leftColumn = MakeColumn("Left column", nativeRoot, 0f);
		rightColumn = MakeColumn("Right column", nativeRoot, 550f);
		patchLeft = MakeColumn("Patcher left column", nativeRoot, 0f);
		patchRight = MakeColumn("Patcher right column", nativeRoot, 550f);
		((Component)patchLeft).gameObject.SetActive(false);
		((Component)patchRight).gameObject.SetActive(false);
		win.AddResizeHandle();
		ApplyGeometry();
	}

	private static RectTransform MakeColumn(string name, RectTransform parent, float x)
	{
		RectTransform val = NewRect(name, (Transform)(object)parent);
		Vector2 val2 = (val.pivot = new Vector2(0f, 1f));
		val2 = (val.anchorMax = val2);
		val.anchorMin = val2;
		val.anchoredPosition = new Vector2(x, 0f);
		val.sizeDelta = new Vector2(530f, 500f);
		VerticalLayoutGroup val5 = ((Component)val).gameObject.AddComponent<VerticalLayoutGroup>();
		((HorizontalOrVerticalLayoutGroup)val5).spacing = 3f;
		((LayoutGroup)val5).padding = new RectOffset(3, 3, 3, 3);
		((HorizontalOrVerticalLayoutGroup)val5).childControlWidth = true;
		((HorizontalOrVerticalLayoutGroup)val5).childControlHeight = true;
		((HorizontalOrVerticalLayoutGroup)val5).childForceExpandWidth = true;
		((HorizontalOrVerticalLayoutGroup)val5).childForceExpandHeight = false;
		return val;
	}

	private void ApplyGeometry()
	{
		if (win != null)
		{
			if (win.TitleLabel != null)
			{
				win.TitleLabel.text = ((Object)(object)selectedPerson != (Object)null) ? ("SmartWardrobe: " + selectedPerson.uid) : "SmartWardrobe";
			}
			win.Apply();
		}
	}

	private void UpdateNativeLayout()
	{
		if (!((Object)(object)nativeRoot == (Object)null) && !win.Collapsed)
		{
			RectTransform val = ((!patchMode) ? leftColumn : patchLeft);
			RectTransform val2 = ((!patchMode) ? rightColumn : patchRight);
			float num = Mathf.Max(200f, LayoutUtility.GetPreferredHeight(val));
			float num2 = Mathf.Max(200f, LayoutUtility.GetPreferredHeight(val2));
			val.sizeDelta = new Vector2(530f, num);
			val2.sizeDelta = new Vector2(530f, num2);
			float num3 = Mathf.Max(num, num2) + 12f;
			nativeRoot.sizeDelta = new Vector2(1080f, num3);
			float num4 = Mathf.Max(1f, window.sizeDelta.x - 12f);
			float num5 = Mathf.Max(1f, window.sizeDelta.y - 74f);
			float num6 = Mathf.Min(2f, num4 / 1080f);
			((Transform)nativeRoot).localScale = Vector3.one * num6;
			scrollContent.sizeDelta = new Vector2(Mathf.Max(num4, 1080f * num6), Mathf.Max(num5, num3 * num6));
		}
	}

	private void TogglePatchMode()
	{
		patchMode = !patchMode;
		((Component)leftColumn).gameObject.SetActive(!patchMode);
		((Component)rightColumn).gameObject.SetActive(!patchMode);
		((Component)patchLeft).gameObject.SetActive(patchMode);
		((Component)patchRight).gameObject.SetActive(patchMode);
		Label(patchButton, (!patchMode) ? "Scene Patcher" : "Wardrobe");
		scroll.verticalNormalizedPosition = 1f;
		UpdateNativeLayout();
	}

	private void ShowPeopleMenu()
	{
		HideMenu();
		List<Atom> people = GetPeople();
		if (people.Count == 0)
		{
			SetStatus("No Person atoms in this scene.");
			return;
		}
		menu = NewRect("Person choices", (Transform)(object)window);
		RectTransform val = menu;
		Vector2 val2 = new Vector2(0f, 1f);
		menu.pivot = val2;
		val2 = val2;
		menu.anchorMax = val2;
		val.anchorMin = val2;
		menu.anchoredPosition = new Vector2(5f, -67f);
		menu.sizeDelta = new Vector2(Mathf.Max(1f, window.sizeDelta.x - 10f), Mathf.Min((float)people.Count * 30f + 8f, Mathf.Max(40f, window.sizeDelta.y - 78f)));
		((Graphic)((Component)menu).gameObject.AddComponent<Image>()).color = ZeroT.UiKit.RlUguiWindow.PanelCol;
		RectTransform val3 = NewRect("Mask", (Transform)(object)menu);
		val3.anchorMin = Vector2.zero;
		val3.anchorMax = Vector2.one;
		val3.offsetMin = new Vector2(4f, 4f);
		val3.offsetMax = new Vector2(-4f, -4f);
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
			Button val6 = NewButton("Choice " + i, (Transform)(object)val5, person.uid, () =>
			{
				HideMenu();
				((MonoBehaviour)this).StartCoroutine(AttachPerson(person, ++generation));
			});
			RectTransform component = ((Component)val6).GetComponent<RectTransform>();
			component.anchorMin = new Vector2(0f, 1f);
			component.anchorMax = new Vector2(1f, 1f);
			component.pivot = new Vector2(0.5f, 1f);
			component.anchoredPosition = new Vector2(0f, (float)(-i) * 30f);
			component.sizeDelta = new Vector2(0f, 29f);
		}
		((Transform)menu).SetAsLastSibling();
	}

	private void HideMenu()
	{
		if ((Object)(object)menu != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)menu).gameObject);
		}
		menu = null;
	}

	private IEnumerator AttachPerson(Atom person, int token)
	{
		RemoveWardrobe();
		ClearColumns();
		selectedPerson = person;
		Label(atomButton, "Person: " + person.uid);
		SetStatus("Preparing SmartWardrobe...");
		yield return null;
		float deadline = Time.realtimeSinceStartup + 5f;
		MVRPluginManager manager = null;
		while (token == generation && (Object)(object)person != (Object)null && Time.realtimeSinceStartup < deadline)
		{
			manager = ((Component)person).GetComponentInChildren<MVRPluginManager>(true);
			if (ManagerReady(manager))
			{
				break;
			}
			yield return null;
		}
		if (token != generation || (Object)(object)person == (Object)null)
		{
			yield break;
		}
		if (!ManagerReady(manager))
		{
			SetStatus("VaM's Person plugin UI is unavailable. Try again after it loads.");
			yield break;
		}
		MVRScript[] componentsInChildren = ((Component)person).GetComponentsInChildren<MVRScript>(true);
		foreach (MVRScript val in componentsInChildren)
		{
			if ((Object)(object)val != (Object)null && ((object)val).GetType().FullName == "Bplus.SmartWardrobe" && ((object)val).GetType().Assembly != typeof(SmartWardrobe).Assembly)
			{
				SetStatus("Remove the original VAR script from this Person to avoid duplicate effects.");
				yield break;
			}
		}
		try
		{
			wardrobe = ((Component)person).gameObject.AddComponent<SmartWardrobe>();
			((JSONStorable)wardrobe).overrideId = "Bplus.SmartWardrobe";
			wardrobe.InitializeStandalone(person, manager, leftColumn, rightColumn);
			registeredPerson = person;
			wardrobeRegistered = person.RegisterAdditionalStorable((JSONStorable)(object)wardrobe);
			if (!wardrobeRegistered)
			{
				Logger.LogWarning((object)("Could not register Bplus.SmartWardrobe actions on " + person.uid));
			}
			patcher = ((Component)person).gameObject.AddComponent<ScenePatcher>();
			patcher.InitializeStandalone(person, manager, patchLeft, patchRight);
			if (win.TitleLabel != null)
			{
				win.TitleLabel.text = "SmartWardrobe: " + person.uid;
			}
			SetStatus("Closet, physics, presets, and other tabs are ready.");
			UpdateNativeLayout();
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("SmartWardrobe initialization failed: " + ex));
			SetStatus("Initialization failed. Check the BepInEx log.");
			RemoveWardrobe();
			ClearColumns();
		}
	}

	private static bool ManagerReady(MVRPluginManager manager)
	{
		return (Object)(object)manager != (Object)null && (Object)(object)manager.configurableButtonPrefab != (Object)null && (Object)(object)manager.configurableSliderPrefab != (Object)null && (Object)(object)manager.configurableTogglePrefab != (Object)null && (Object)(object)manager.configurableColorPickerPrefab != (Object)null && (Object)(object)manager.configurableTextFieldPrefab != (Object)null && (Object)(object)manager.configurableScrollablePopupPrefab != (Object)null && (Object)(object)manager.configurableSpacerPrefab != (Object)null;
	}

	private void RemoveWardrobe()
	{
		if ((Object)(object)wardrobe != (Object)null)
		{
			try
			{
				wardrobe.RestoreOriginalFileBrowser();
			}
			catch (Exception ex)
			{
				Logger.LogWarning((object)("Could not restore file browser: " + ex.Message));
			}
			try
			{
				wardrobe.PrepareForStandaloneRemoval();
			}
			catch (Exception ex2)
			{
				Logger.LogWarning((object)("Could not restore wardrobe values: " + ex2.Message));
			}
			if (wardrobeRegistered && (Object)(object)registeredPerson != (Object)null)
			{
				registeredPerson.UnregisterAdditionalStorable((JSONStorable)(object)wardrobe);
			}
			Object.Destroy((Object)(object)wardrobe);
		}
		wardrobe = null;
		wardrobeRegistered = false;
		registeredPerson = null;
		if ((Object)(object)patcher != (Object)null)
		{
			Object.Destroy((Object)(object)patcher);
		}
		patcher = null;
	}

	private void ClearColumns()
	{
		if (!((Object)(object)leftColumn == (Object)null))
		{
			for (int num = ((Transform)leftColumn).childCount - 1; num >= 0; num--)
			{
				Object.Destroy((Object)(object)((Component)((Transform)leftColumn).GetChild(num)).gameObject);
			}
			for (int num2 = ((Transform)rightColumn).childCount - 1; num2 >= 0; num2--)
			{
				Object.Destroy((Object)(object)((Component)((Transform)rightColumn).GetChild(num2)).gameObject);
			}
			for (int num3 = ((Transform)patchLeft).childCount - 1; num3 >= 0; num3--)
			{
				Object.Destroy((Object)(object)((Component)((Transform)patchLeft).GetChild(num3)).gameObject);
			}
			for (int num4 = ((Transform)patchRight).childCount - 1; num4 >= 0; num4--)
			{
				Object.Destroy((Object)(object)((Component)((Transform)patchRight).GetChild(num4)).gameObject);
			}
		}
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

	private static Text NewText(string name, Transform parent, string value, int size, TextAnchor anchor)
	{
		RectTransform val = NewRect(name, parent);
		Text val2 = ((Component)val).gameObject.AddComponent<Text>();
		val2.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		val2.fontSize = size;
		val2.alignment = anchor;
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
		((Graphic)val4).rectTransform.anchorMin = Vector2.zero;
		((Graphic)val4).rectTransform.anchorMax = Vector2.one;
		((Graphic)val4).rectTransform.offsetMin = new Vector2(2f, 0f);
		((Graphic)val4).rectTransform.offsetMax = new Vector2(-2f, 0f);
		return val3;
	}

	private static void Label(Button button, string label)
	{
		if ((Object)(object)button != (Object)null)
		{
			Text componentInChildren = ((Component)button).GetComponentInChildren<Text>(true);
			if ((Object)(object)componentInChildren != (Object)null)
			{
				componentInChildren.text = label;
			}
		}
	}

	private static void PinRight(RectTransform rect, float right, float top, float w, float h)
	{
		Vector2 val = (rect.pivot = new Vector2(1f, 1f));
		val = (rect.anchorMax = val);
		rect.anchorMin = val;
		rect.anchoredPosition = new Vector2(0f - right, 0f - top);
		rect.sizeDelta = new Vector2(w, h);
	}

}
