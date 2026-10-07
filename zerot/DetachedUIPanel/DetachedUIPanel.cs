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

namespace DetachedUIPanelStandalone
{
	// Window chrome deliberately mirrors VpbRandomLook (IMGUI default-skin look, same title row
	// "S- S+ - x", same margins/row heights, resize arrow bottom-right, same config keys).
	// The body still has to be uGUI because the point of the plugin is reparenting a native
	// uGUI hierarchy into the window.
	[BepInPlugin("zero.vam.detached-ui-panel", "Detached UI Panel Standalone", "1.1.0")]
	public sealed class DetachedUIPanelPlugin : BaseUnityPlugin
	{
		private const float MinW = 330f;
		private const float MaxW = 1400f;
		private const float MinH = 240f;
		private const float MaxH = 1200f;
		private const float CollapsedH = 34f;
		private const float Margin = 8f;
		private const float BodyTop = 140f;
		private const float LabelW = 62f;

		private static readonly Color PanelCol = new Color(0.17f, 0.17f, 0.17f, 0.94f);
		private static readonly Color BorderCol = new Color(0.40f, 0.40f, 0.40f, 1f);
		private static readonly Color ButtonN = new Color(0.31f, 0.31f, 0.31f, 1f);
		private static readonly Color ButtonH = new Color(0.40f, 0.40f, 0.40f, 1f);
		private static readonly Color ButtonP = new Color(0.22f, 0.22f, 0.22f, 1f);
		private static readonly Color TextCol = new Color(0.90f, 0.90f, 0.90f, 1f);
		private static readonly Color ViewCol = new Color(0.09f, 0.09f, 0.09f, 0.96f);

		private static readonly string[] Categories = new string[] { "Whole", "Plugins", "Others" };

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

		[DllImport("Shcore.dll")]
		private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

		private ConfigEntry<bool> showWindow;
		private ConfigEntry<bool> savedCollapsed;
		private ConfigEntry<bool> savedActive;
		private Text powerLabel;
		private ConfigEntry<float> savedScale;
		private ConfigEntry<float> savedX;
		private ConfigEntry<float> savedY;
		private ConfigEntry<float> savedWidth;
		private ConfigEntry<float> savedHeight;
		private ConfigEntry<bool> dpiAware;

		// Same convention as VpbRandomLook: position is screen pixels, size is pre-scale units.
		private float winX = 40f;
		private float winY = 120f;
		private float winW = 530f;
		private float winH = 630f;
		private float userScale = 1f;
		private bool collapsed;

		private float cachedDpiScale = -1f;
		private double nextDpiProbe;
		private float lastScale = -1f;
		private int screenWidth;
		private int screenHeight;

		private Canvas canvas;
		private RectTransform window;
		private RectTransform body;
		private RectTransform viewport;
		private RectTransform scrollContent;
		private RectTransform nativeHost;
		private RectTransform popup;
		private ScrollRect nativeScroll;
		private Text status;
		private Text atomLabel;
		private Text catLabel;
		private Text itemLabel;
		private Button atomButton;
		private Button refreshButton;
		private Button itemButton;
		private Button restoreButton;
		private readonly Button[] catButtons = new Button[3];
		private RectTransform grip;
		private Text collapseLabel;

		private readonly List<string> items = new List<string>();
		private string atomUid = "";
		private string category = "Whole";
		private string item = "";
		private Atom atom;
		private int selectionToken;

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
		private float detachedWidth;
		private float detachedHeight;

		private void Awake()
		{
			showWindow = Config.Bind<bool>("Window", "Show", true, "Show the in-game Detached UI Panel window.");
			savedActive = Config.Bind<bool>("General", "Active", true, "Turn the panel off completely: restores any detached UI and stops all work.");
			savedCollapsed = Config.Bind<bool>("Window", "Collapsed", false, "Collapse the window to its title bar.");
			savedScale = Config.Bind<float>("Window", "Scale", 1f, "Extra scale multiplier applied on top of DPI (S- / S+).");
			savedX = Config.Bind<float>("Window", "X", 40f, "Saved horizontal position.");
			savedY = Config.Bind<float>("Window", "Y", 120f, "Saved vertical position.");
			savedWidth = Config.Bind<float>("Window", "Width", 530f, "Saved width.");
			savedHeight = Config.Bind<float>("Window", "Height", 630f, "Saved height.");
			dpiAware = Config.Bind<bool>("Window", "DpiAware", true, "Scale with the monitor's DPI. Turn off if the window is the wrong size on a mixed-DPI setup.");
			winX = savedX.Value;
			winY = savedY.Value;
			winW = Mathf.Clamp(savedWidth.Value, MinW, MaxW);
			winH = Mathf.Clamp(savedHeight.Value, MinH, MaxH);
			userScale = Mathf.Clamp(savedScale.Value, 0.3f, 2.5f);
			collapsed = savedCollapsed.Value;
			showWindow.SettingChanged += OnShowChanged;
		}

		private IEnumerator Start()
		{
			while (SuperController.singleton == null || EventSystem.current == null)
			{
				yield return null;
			}
			BuildWindow();
			SuperController.singleton.onSceneLoadedHandlers += SceneLoaded;
		}

		private void Update()
		{
			if (window == null || !savedActive.Value)
			{
				return;
			}
			float s = CurrentScale();
			if (screenWidth != Screen.width || screenHeight != Screen.height || Mathf.Abs(s - lastScale) > 0.001f)
			{
				ApplyGeometry();
			}
			if (detached != null && (originalParent == null || atom == null))
			{
				Restore();
				SetStatus("Selection was removed; choose another atom.");
			}
		}

		private void OnDestroy()
		{
			selectionToken++;
			if (showWindow != null)
			{
				showWindow.SettingChanged -= OnShowChanged;
			}
			if (SuperController.singleton != null)
			{
				SuperController.singleton.onSceneLoadedHandlers -= SceneLoaded;
			}
			Restore();
			if (canvas != null)
			{
				Destroy(canvas.gameObject);
			}
		}

		private void OnDisable()
		{
			selectionToken++;
			Restore();
			if (canvas != null)
			{
				canvas.gameObject.SetActive(false);
			}
		}

		private void OnEnable()
		{
			if (canvas != null)
			{
				canvas.gameObject.SetActive(showWindow == null || showWindow.Value);
			}
		}

		private void OnShowChanged(object sender, EventArgs e)
		{
			if (!showWindow.Value)
			{
				selectionToken++;
				Restore();
			}
			if (canvas != null)
			{
				canvas.gameObject.SetActive(showWindow.Value);
			}
		}

		private void SceneLoaded()
		{
			selectionToken++;
			Restore();
			atom = null;
			atomUid = "";
			item = "";
			items.Clear();
			HidePopup();
			RefreshLabels();
			SetStatus("Scene loaded. Select an atom.");
		}

		// ---------------------------------------------------------------- scale / DPI

		private float DpiScale()
		{
			if (dpiAware != null && !dpiAware.Value)
			{
				return 1f;
			}
			double now = Time.realtimeSinceStartup;
			if (cachedDpiScale > 0f && now < nextDpiProbe)
			{
				return cachedDpiScale;
			}
			nextDpiProbe = now + 5.0;
			float result = 1f;
			bool ok = false;
			try
			{
				POINT pt = default(POINT);
				pt.X = (int)winX;
				pt.Y = (int)winY;
				IntPtr monitor = MonitorFromPoint(pt, 2u);
				uint dpiX;
				uint dpiY;
				if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 && dpiX != 0)
				{
					result = (float)dpiX / 96f;
					ok = true;
				}
			}
			catch
			{
				ok = false;
			}
			if (!ok)
			{
				try
				{
					float dpi = Screen.dpi;
					if (dpi > 0f)
					{
						result = dpi / 96f;
						ok = true;
					}
				}
				catch
				{
				}
			}
			cachedDpiScale = Mathf.Clamp(ok ? result : 1f, 1f, 2.5f);
			return cachedDpiScale;
		}

		private float CurrentScale()
		{
			return DpiScale() * Mathf.Clamp(userScale, 0.3f, 2.5f);
		}

		private void SetScale(float v)
		{
			userScale = Mathf.Clamp(v, 0.3f, 2.5f);
			cachedDpiScale = -1f;
			ApplyGeometry();
			Persist();
		}

		// ---------------------------------------------------------------- building

		private static RectTransform NewRect(string name, Transform parent)
		{
			GameObject go = new GameObject(name, new Type[] { typeof(RectTransform) });
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		private static Text NewText(string name, Transform parent, string value, TextAnchor alignment)
		{
			RectTransform rt = NewRect(name, parent);
			Text t = rt.gameObject.AddComponent<Text>();
			t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			t.fontSize = 12;
			t.alignment = alignment;
			t.color = TextCol;
			t.text = value;
			t.raycastTarget = false;
			t.horizontalOverflow = HorizontalWrapMode.Wrap;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			return t;
		}

		private static Button NewButton(string name, Transform parent, string label, UnityAction onClick)
		{
			RectTransform rt = NewRect(name, parent);
			Image img = rt.gameObject.AddComponent<Image>();
			img.color = Color.white;
			Button b = rt.gameObject.AddComponent<Button>();
			b.targetGraphic = img;
			ColorBlock cb = b.colors;
			cb.normalColor = ButtonN;
			cb.highlightedColor = ButtonH;
			cb.pressedColor = ButtonP;
			cb.disabledColor = ButtonN;
			cb.colorMultiplier = 1f;
			cb.fadeDuration = 0.04f;
			b.colors = cb;
			Navigation nav = new Navigation();
			nav.mode = Navigation.Mode.None;
			b.navigation = nav;
			if (onClick != null)
			{
				b.onClick.AddListener(onClick);
			}
			Text t = NewText("Label", rt, label, TextAnchor.MiddleCenter);
			t.rectTransform.anchorMin = Vector2.zero;
			t.rectTransform.anchorMax = Vector2.one;
			t.rectTransform.offsetMin = new Vector2(3f, 0f);
			t.rectTransform.offsetMax = new Vector2(-3f, 0f);
			t.resizeTextForBestFit = true;
			t.resizeTextMinSize = 9;
			t.resizeTextMaxSize = 12;
			return b;
		}

		private static void SetRect(RectTransform rect, float left, float top, float w, float h)
		{
			Vector2 corner = new Vector2(0f, 1f);
			rect.pivot = corner;
			rect.anchorMin = corner;
			rect.anchorMax = corner;
			rect.anchoredPosition = new Vector2(left, -top);
			rect.sizeDelta = new Vector2(w, h);
		}

		private static void PinTopRight(RectTransform rect, float right, float top, float w, float h)
		{
			Vector2 corner = new Vector2(1f, 1f);
			rect.pivot = corner;
			rect.anchorMin = corner;
			rect.anchorMax = corner;
			rect.anchoredPosition = new Vector2(-right, -top);
			rect.sizeDelta = new Vector2(w, h);
		}

		private static void SetLabel(Button button, string value)
		{
			Text t = button.GetComponentInChildren<Text>();
			if (t != null)
			{
				t.text = value;
			}
		}

		private Button HeaderButton(string name, string label, Action action)
		{
			Button b = NewButton(name, window, label, null);
			PointerDownAction pd = b.gameObject.AddComponent<PointerDownAction>();
			pd.action = action;
			return b;
		}

		private void BuildWindow()
		{
			GameObject root = new GameObject("Detached UI Panel Standalone", new Type[] { typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster) });
			DontDestroyOnLoad(root);
			canvas = root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.overrideSorting = true;
			canvas.sortingOrder = 30000;
			root.SetActive(showWindow.Value);

			window = NewRect("Window", root.transform);
			Vector2 corner = new Vector2(0f, 1f);
			window.pivot = corner;
			window.anchorMin = corner;
			window.anchorMax = corner;
			Image panel = window.gameObject.AddComponent<Image>();
			panel.color = PanelCol;
			Outline edge = window.gameObject.AddComponent<Outline>();
			edge.effectColor = BorderCol;
			edge.effectDistance = new Vector2(1f, -1f);

			// Title row: the whole top strip drags the window (like VpbRandomLook's 24px drag rect).
			RectTransform dragStrip = NewRect("Drag title bar", window);
			dragStrip.anchorMin = new Vector2(0f, 1f);
			dragStrip.anchorMax = new Vector2(1f, 1f);
			dragStrip.pivot = new Vector2(0f, 1f);
			dragStrip.offsetMin = new Vector2(0f, -26f);
			dragStrip.offsetMax = Vector2.zero;
			Image dragImg = dragStrip.gameObject.AddComponent<Image>();
			dragImg.color = new Color(0f, 0f, 0f, 0f);
			WindowPointer move = dragStrip.gameObject.AddComponent<WindowPointer>();
			move.owner = this;
			move.axis = ResizeAxis.Move;

			Text title = NewText("Title", window, "Detached UI Panel", TextAnchor.MiddleLeft);
			title.rectTransform.anchorMin = new Vector2(0f, 1f);
			title.rectTransform.anchorMax = new Vector2(1f, 1f);
			title.rectTransform.offsetMin = new Vector2(Margin, -25f);
			title.rectTransform.offsetMax = new Vector2(-150f, -3f);

			Button power = HeaderButton("Power", "On", delegate
			{
				savedActive.Value = !savedActive.Value;
				if (!savedActive.Value)
				{
					selectionToken++;
					Restore();
					HidePopup();
				}
				ApplyGeometry();
				Config.Save();
			});
			powerLabel = power.GetComponentInChildren<Text>();
			PinTopRight(power.GetComponent<RectTransform>(), 115f, 3f, 31f, 18f);
			Button smaller = HeaderButton("Scale down", "S-", delegate { SetScale(userScale - 0.1f); });
			Button larger = HeaderButton("Scale up", "S+", delegate { SetScale(userScale + 0.1f); });
			Button collapse = HeaderButton("Collapse or expand", "—", ToggleCollapse);
			Button close = HeaderButton("Close", "x", delegate { showWindow.Value = false; Config.Save(); });
			collapseLabel = collapse.GetComponentInChildren<Text>();
			PinTopRight(smaller.GetComponent<RectTransform>(), 86f, 3f, 26f, 18f);
			PinTopRight(larger.GetComponent<RectTransform>(), 57f, 3f, 26f, 18f);
			PinTopRight(collapse.GetComponent<RectTransform>(), 32f, 3f, 22f, 18f);
			PinTopRight(close.GetComponent<RectTransform>(), 7f, 3f, 22f, 18f);

			body = NewRect("Body", window);
			body.anchorMin = Vector2.zero;
			body.anchorMax = Vector2.one;
			body.offsetMin = Vector2.zero;
			body.offsetMax = Vector2.zero;

			atomLabel = NewText("Atom label", body, "Atom:", TextAnchor.MiddleLeft);
			atomButton = NewButton("Atom", body, "(select atom)", ShowAtomOptions);
			refreshButton = NewButton("Refresh", body, "Refresh", RefreshSelection);
			catLabel = NewText("Show label", body, "Show:", TextAnchor.MiddleLeft);
			for (int i = 0; i < Categories.Length; i++)
			{
				string c = Categories[i];
				catButtons[i] = NewButton("Category " + c, body, c, delegate { SetCategory(c); });
			}
			itemLabel = NewText("Item label", body, "Item:", TextAnchor.MiddleLeft);
			itemButton = NewButton("Item", body, "Whole UI", ShowItemOptions);
			restoreButton = NewButton("Restore", body, "↺  Restore UI", delegate
			{
				selectionToken++;
				Restore();
				SetStatus("UI restored.");
			});
			status = NewText("Status", body, "Select an atom.", TextAnchor.MiddleLeft);

			viewport = NewRect("Native UI viewport", body);
			viewport.gameObject.AddComponent<Image>().color = ViewCol;
			viewport.gameObject.AddComponent<RectMask2D>();
			nativeScroll = viewport.gameObject.AddComponent<ScrollRect>();
			nativeScroll.viewport = viewport;
			nativeScroll.horizontal = false;
			nativeScroll.vertical = true;
			nativeScroll.movementType = ScrollRect.MovementType.Clamped;
			nativeScroll.scrollSensitivity = 36f;
			scrollContent = NewRect("Scroll content", viewport);
			scrollContent.pivot = corner;
			scrollContent.anchorMin = corner;
			scrollContent.anchorMax = corner;
			nativeScroll.content = scrollContent;
			nativeHost = NewRect("Native UI host", scrollContent);
			nativeHost.pivot = corner;
			nativeHost.anchorMin = corner;
			nativeHost.anchorMax = corner;
			nativeHost.anchoredPosition = Vector2.zero;

			Text arrow = NewText("Resize", body, "↘", TextAnchor.MiddleCenter);
			arrow.raycastTarget = true;
			grip = arrow.rectTransform;
			Vector2 br = new Vector2(1f, 0f);
			grip.pivot = br;
			grip.anchorMin = br;
			grip.anchorMax = br;
			grip.anchoredPosition = new Vector2(-2f, 2f);
			grip.sizeDelta = new Vector2(18f, 18f);
			WindowPointer resize = grip.gameObject.AddComponent<WindowPointer>();
			resize.owner = this;
			resize.axis = ResizeAxis.XY;

			ApplyGeometry();
			RefreshLabels();
		}

		// ---------------------------------------------------------------- layout

		private void ClampPosition(float s)
		{
			float w = winW * s;
			float maxX = Mathf.Max(0f, (float)Screen.width - 60f);
			float maxY = Mathf.Max(0f, (float)Screen.height - 24f);
			winX = Mathf.Clamp(winX, -(w - 60f), maxX);
			winY = Mathf.Clamp(winY, 0f, maxY);
		}

		private void ApplyGeometry()
		{
			if (window == null)
			{
				return;
			}
			screenWidth = Screen.width;
			screenHeight = Screen.height;
			float s = CurrentScale();
			lastScale = s;
			canvas.scaleFactor = s;
			ClampPosition(s);
			bool shrunk = collapsed || !savedActive.Value;
			float h = shrunk ? CollapsedH : winH;
			window.sizeDelta = new Vector2(winW, h);
			window.anchoredPosition = new Vector2(winX / s, -winY / s);
			body.gameObject.SetActive(!shrunk);
			if (powerLabel != null)
			{
				powerLabel.text = savedActive.Value ? "On" : "Off";
			}
			if (collapseLabel != null)
			{
				collapseLabel.text = collapsed ? "+" : "—";
			}
			if (shrunk)
			{
				HidePopup();
				return;
			}

			float inner = winW - 2f * Margin;
			float rightOfLabel = Margin + LabelW + 2f;
			float fieldW = winW - rightOfLabel - Margin;

			SetRect(atomLabel.rectTransform, Margin, 30f, LabelW, 22f);
			SetRect(atomButton.GetComponent<RectTransform>(), rightOfLabel, 30f, fieldW - 58f, 22f);
			SetRect(refreshButton.GetComponent<RectTransform>(), winW - Margin - 54f, 30f, 54f, 22f);

			SetRect(catLabel.rectTransform, Margin, 56f, LabelW, 22f);
			float cw = fieldW / 3f;
			for (int i = 0; i < catButtons.Length; i++)
			{
				SetRect(catButtons[i].GetComponent<RectTransform>(), rightOfLabel + i * cw, 56f, cw - 3f, 22f);
			}

			SetRect(itemLabel.rectTransform, Margin, 82f, LabelW, 22f);
			SetRect(itemButton.GetComponent<RectTransform>(), rightOfLabel, 82f, fieldW, 22f);

			SetRect(restoreButton.GetComponent<RectTransform>(), Margin, 108f, 112f, 26f);
			SetRect(status.rectTransform, Margin + 118f, 108f, Mathf.Max(30f, inner - 118f), 26f);

			viewport.anchorMin = Vector2.zero;
			viewport.anchorMax = Vector2.one;
			viewport.offsetMin = new Vector2(Margin, Margin);
			viewport.offsetMax = new Vector2(-Margin, -BodyTop);

			UpdateNativePresentation();
		}

		private void UpdateNativePresentation()
		{
			if (detached == null || viewport == null)
			{
				return;
			}
			float vw = Mathf.Max(1f, winW - 2f * Margin);
			float vh = Mathf.Max(1f, winH - BodyTop - Margin);
			float fit = Mathf.Min(1f, vw / detachedWidth);
			nativeHost.sizeDelta = new Vector2(detachedWidth * fit, detachedHeight * fit);
			scrollContent.sizeDelta = new Vector2(Mathf.Max(vw, detachedWidth * fit), Mathf.Max(vh, detachedHeight * fit));
			detached.localScale = Vector3.one * fit;
		}

		// ---------------------------------------------------------------- pointer input

		internal void PointerDrag(Vector2 pixelDelta, ResizeAxis axis)
		{
			if (window == null || canvas == null)
			{
				return;
			}
			float s = CurrentScale();
			if (axis == ResizeAxis.Move)
			{
				winX += pixelDelta.x;
				winY -= pixelDelta.y;
			}
			else if (axis == ResizeAxis.XY)
			{
				winW = Mathf.Clamp(winW + pixelDelta.x / s, MinW, MaxW);
				winH = Mathf.Clamp(winH - pixelDelta.y / s, MinH, MaxH);
			}
			ApplyGeometry();
		}

		internal void Persist()
		{
			savedX.Value = winX;
			savedY.Value = winY;
			savedWidth.Value = winW;
			savedHeight.Value = winH;
			savedScale.Value = userScale;
			savedCollapsed.Value = collapsed;
			cachedDpiScale = -1f;
			Config.Save();
		}

		internal void ToggleCollapse()
		{
			collapsed = !collapsed;
			ApplyGeometry();
			Persist();
		}

		// ---------------------------------------------------------------- labels / status

		private void RefreshLabels()
		{
			if (atomButton == null)
			{
				return;
			}
			SetLabel(atomButton, string.IsNullOrEmpty(atomUid) ? "(select atom)" : atomUid);
			for (int i = 0; i < catButtons.Length; i++)
			{
				SetLabel(catButtons[i], (Categories[i] == category ? "[x] " : "[ ] ") + Categories[i]);
			}
			SetLabel(itemButton, category == "Whole" ? "Whole UI" : (string.IsNullOrEmpty(item) ? "(select item)" : item));
		}

		private void SetStatus(string value)
		{
			if (status != null)
			{
				status.text = value;
			}
		}

		// ---------------------------------------------------------------- selection logic

		private void StartAttachOrLoad()
		{
			if (category == "Whole")
			{
				StartCoroutine(AttachSelected(selectionToken));
			}
			else
			{
				StartCoroutine(LoadItems(selectionToken));
			}
		}

		private void ShowAtomOptions()
		{
			if (popup != null)
			{
				HidePopup();
				return;
			}
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			List<string> list = new List<string>();
			foreach (string uid in sc.GetAtomUIDs())
			{
				Atom a = sc.GetAtomByUid(uid);
				if (a != null && a.type != "SimpleSign" && uid != "CoreControl" && uid != "[CameraRig]")
				{
					list.Add(uid);
				}
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			ShowOptions(list, 54f, delegate(string chosen)
			{
				selectionToken++;
				Restore();
				atomUid = chosen;
				atom = sc.GetAtomByUid(chosen);
				item = "";
				items.Clear();
				RefreshLabels();
				StartAttachOrLoad();
			});
		}

		private void SetCategory(string chosen)
		{
			if (chosen == category)
			{
				return;
			}
			selectionToken++;
			Restore();
			category = chosen;
			item = "";
			items.Clear();
			HidePopup();
			RefreshLabels();
			if (atom != null)
			{
				StartAttachOrLoad();
			}
		}

		private void RefreshSelection()
		{
			selectionToken++;
			Restore();
			HidePopup();
			atom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
			if (atom == null)
			{
				atomUid = "";
				item = "";
				items.Clear();
				RefreshLabels();
				SetStatus("Select an atom.");
			}
			else
			{
				StartAttachOrLoad();
			}
		}

		private void ShowItemOptions()
		{
			if (popup != null)
			{
				HidePopup();
				return;
			}
			if (category == "Whole")
			{
				if (atom != null)
				{
					StartCoroutine(AttachSelected(++selectionToken));
				}
				return;
			}
			if (atom == null)
			{
				SetStatus("Select an atom first.");
				return;
			}
			if (items.Count == 0)
			{
				StartCoroutine(LoadItems(++selectionToken));
				return;
			}
			ShowOptions(new List<string>(items), 80f, delegate(string chosen)
			{
				item = chosen;
				RefreshLabels();
				StartCoroutine(AttachSelected(++selectionToken));
			});
		}

		private IEnumerator FocusAtom(int token, string tab)
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || atom == null)
			{
				yield break;
			}
			sc.editModeToggle.isOn = true;
			sc.SelectController(atom.mainController, false, true, true, true);
			if (sc.isOpenVR)
			{
				sc.ShowMainHUD(true, false);
			}
			else
			{
				sc.ShowMainHUDMonitor();
			}
			float deadline = Time.realtimeSinceStartup + 3f;
			while (token == selectionToken && Time.realtimeSinceStartup < deadline)
			{
				if (atom != null && atom.UITransform != null && atom.UITransform.childCount > 0)
				{
					UITabSelector selector = atom.GetComponentInChildren<UITabSelector>(true);
					if (selector != null)
					{
						selector.SetActiveTab(tab);
						yield return null;
						break;
					}
				}
				yield return null;
			}
		}

		private IEnumerator LoadItems(int token)
		{
			Restore();
			SetStatus("Loading items...");
			yield return StartCoroutine(FocusAtom(token, category == "Plugins" ? "Plugins" : "Control"));
			if (token != selectionToken || atom == null)
			{
				yield break;
			}
			items.Clear();
			if (category == "Plugins")
			{
				Transform plugins = atom.reParentObject != null ? atom.reParentObject.Find("object/PluginManager/Plugins") : null;
				if (plugins != null)
				{
					foreach (Transform child in plugins)
					{
						int underscore = child.name.IndexOf('_');
						string text = underscore >= 0 ? child.name.Substring(underscore + 1) : child.name;
						if (!items.Contains(text))
						{
							items.Add(text);
						}
					}
				}
			}
			else
			{
				Transform content = UiContent();
				if (content != null)
				{
					foreach (Transform child in content)
					{
						if (child.name != "ToggleContainer")
						{
							items.Add(child.name);
						}
					}
				}
			}
			SetStatus(items.Count != 0 ? "Choose an item (" + items.Count + ")." : "No items found. Open the atom UI and refresh.");
			if (items.Count > 0)
			{
				ShowItemOptions();
			}
		}

		private Transform UiContent()
		{
			if (atom == null || atom.UITransform == null || atom.UITransform.childCount == 0)
			{
				return null;
			}
			return atom.UITransform.GetChild(0).Find("Canvas/Panel/Content");
		}

		private IEnumerator AttachSelected(int token)
		{
			HidePopup();
			Restore();
			if (atom == null)
			{
				yield break;
			}
			if (category != "Whole" && string.IsNullOrEmpty(item))
			{
				SetStatus("Select an item.");
				yield break;
			}
			SetStatus("Opening native UI...");
			yield return StartCoroutine(FocusAtom(token, category == "Plugins" ? "Plugins" : "Control"));
			if (token != selectionToken || atom == null)
			{
				yield break;
			}
			if (category == "Plugins")
			{
				OpenPluginItem(item);
			}
			float deadline = Time.realtimeSinceStartup + 3f;
			RectTransform target = null;
			while (token == selectionToken && Time.realtimeSinceStartup < deadline)
			{
				target = FindTarget();
				if (target != null)
				{
					break;
				}
				yield return null;
			}
			if (token == selectionToken)
			{
				if (target == null)
				{
					SetStatus("Native UI unavailable. Open its tab and press Refresh.");
					yield break;
				}
				Attach(target);
				SetStatus("Drag title bar • resize at bottom right • scroll content");
			}
		}

		private void OpenPluginItem(string chosen)
		{
			if (atom == null)
			{
				return;
			}
			UITabSelector selector = atom.GetComponentInChildren<UITabSelector>(true);
			if (selector == null)
			{
				return;
			}
			MVRScriptUI[] scriptUIs = selector.GetComponentsInChildren<MVRScriptUI>(true);
			foreach (MVRScriptUI ui in scriptUIs)
			{
				if (ui.closeButton != null)
				{
					ui.closeButton.onClick.Invoke();
				}
			}
			MVRScriptControllerUI[] controllers = selector.GetComponentsInChildren<MVRScriptControllerUI>(true);
			foreach (MVRScriptControllerUI c in controllers)
			{
				if (c.label == null || c.openUIButton == null)
				{
					continue;
				}
				string text = c.label.text ?? "";
				if (text.IndexOf("plugin#", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf(chosen, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					c.openUIButton.onClick.Invoke();
					break;
				}
			}
		}

		private RectTransform FindTarget()
		{
			if (atom == null || atom.UITransform == null || atom.UITransform.childCount == 0)
			{
				return null;
			}
			Transform ui = atom.UITransform.GetChild(0);
			if (category == "Whole")
			{
				return ui.Find("Canvas/Panel") as RectTransform;
			}
			Transform content = ui.Find("Canvas/Panel/Content");
			if (content == null)
			{
				return null;
			}
			if (category == "Others")
			{
				return content.Find(item) as RectTransform;
			}
			Transform plugins = content.Find("Plugins");
			if (plugins == null)
			{
				return null;
			}
			foreach (Transform child in plugins)
			{
				if (child.name == "ScriptUI(Clone)" && child.gameObject.activeSelf)
				{
					return child as RectTransform;
				}
			}
			return null;
		}

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
			RefreshLabels();
		}

		private void Restore()
		{
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
			else if (detached != null)
			{
				Destroy(detached.gameObject);
			}
			detached = null;
			originalParent = null;
			RefreshLabels();
		}

		// ---------------------------------------------------------------- dropdown list

		private void HidePopup()
		{
			if (popup != null)
			{
				Destroy(popup.gameObject);
			}
			popup = null;
		}

		private void ShowOptions(List<string> options, float top, Action<string> choose)
		{
			HidePopup();
			if (options.Count == 0)
			{
				SetStatus("No choices available.");
				return;
			}
			float avail = Mathf.Max(44f, winH - top - Margin);
			float h = Mathf.Min(Mathf.Min(options.Count * 20f + 8f, 200f), avail);
			popup = NewRect("Choices", body);
			SetRect(popup, Margin, top, winW - 2f * Margin, h);
			popup.gameObject.AddComponent<Image>().color = PanelCol;
			Outline edge = popup.gameObject.AddComponent<Outline>();
			edge.effectColor = BorderCol;
			edge.effectDistance = new Vector2(1f, -1f);

			RectTransform mask = NewRect("Scroll mask", popup);
			mask.anchorMin = Vector2.zero;
			mask.anchorMax = Vector2.one;
			mask.offsetMin = new Vector2(4f, 4f);
			mask.offsetMax = new Vector2(-4f, -4f);
			mask.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
			mask.gameObject.AddComponent<RectMask2D>();
			ScrollRect sr = mask.gameObject.AddComponent<ScrollRect>();
			sr.viewport = mask;
			sr.horizontal = false;
			sr.vertical = true;
			sr.movementType = ScrollRect.MovementType.Clamped;
			sr.scrollSensitivity = 24f;

			RectTransform content = NewRect("Choices content", mask);
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0.5f, 1f);
			content.anchoredPosition = Vector2.zero;
			content.sizeDelta = new Vector2(0f, options.Count * 20f);
			sr.content = content;
			for (int i = 0; i < options.Count; i++)
			{
				string selected = options[i];
				Button b = NewButton("Choice " + i, content, selected, delegate
				{
					HidePopup();
					choose(selected);
				});
				RectTransform rt = b.GetComponent<RectTransform>();
				rt.anchorMin = new Vector2(0f, 1f);
				rt.anchorMax = new Vector2(1f, 1f);
				rt.pivot = new Vector2(0.5f, 1f);
				rt.anchoredPosition = new Vector2(0f, -i * 20f);
				rt.sizeDelta = new Vector2(0f, 19f);
			}
			popup.SetAsLastSibling();
		}
	}

	public enum ResizeAxis
	{
		Move,
		XY
	}

	public sealed class PointerDownAction : MonoBehaviour, IPointerDownHandler
	{
		internal Action action;

		public void OnPointerDown(PointerEventData data)
		{
			if (action != null && data.button == PointerEventData.InputButton.Left)
			{
				action();
			}
		}
	}

	public sealed class WindowPointer : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		internal DetachedUIPanelPlugin owner;
		internal ResizeAxis axis;
		private Vector2 previous;

		public void OnPointerDown(PointerEventData eventData)
		{
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			previous = eventData.position;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (owner != null)
			{
				owner.PointerDrag(eventData.position - previous, axis);
			}
			previous = eventData.position;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (owner != null)
			{
				owner.Persist();
			}
		}
	}
}
