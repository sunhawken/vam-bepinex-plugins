using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ZeroT.UiKit
{
	internal delegate void RlAction();

	internal delegate void RlLayout(float w, float h);

	// uGUI twin of RlChrome: a screen-space window that looks and behaves like the VpbRandomLook IMGUI window
	// (default dark box, "Title ... S- S+ - x" row, resize arrow bottom-right, 34px collapsed bar, monitor DPI,
	// config keys Show/Collapsed/Scale/X/Y/Width/Height/DpiAware). The plugin builds its own body under Body.
	internal sealed class RlUguiWindow
	{
		internal static readonly Color PanelCol = new Color(0.17f, 0.17f, 0.17f, 0.94f);
		internal static readonly Color BorderCol = new Color(0.40f, 0.40f, 0.40f, 1f);
		internal static readonly Color ButtonN = new Color(0.31f, 0.31f, 0.31f, 1f);
		internal static readonly Color ButtonH = new Color(0.40f, 0.40f, 0.40f, 1f);
		internal static readonly Color ButtonP = new Color(0.22f, 0.22f, 0.22f, 1f);
		internal static readonly Color TextCol = new Color(0.90f, 0.90f, 0.90f, 1f);
		internal static readonly Color ViewCol = new Color(0.09f, 0.09f, 0.09f, 0.96f);

		internal const float CollapsedH = 34f;
		internal const float Margin = 8f;
		internal const float TitleH = 28f;

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

		private readonly BaseUnityPlugin owner;
		private readonly string title;
		private readonly float minW;
		private readonly float minH;
		private readonly float maxW;
		private readonly float maxH;

		private ConfigEntry<bool> show;
		private ConfigEntry<bool> savedCollapsed;
		private ConfigEntry<float> savedScale;
		private ConfigEntry<float> savedX;
		private ConfigEntry<float> savedY;
		private ConfigEntry<float> savedW;
		private ConfigEntry<float> savedH;
		private ConfigEntry<bool> dpiAware;

		internal float X;
		internal float Y;
		internal float W;
		internal float H;
		internal float UserScale = 1f;
		internal bool Collapsed;

		internal Canvas Canvas;
		internal RectTransform Window;
		internal RectTransform Body;
		internal Text TitleLabel;

		// Called after every geometry change while expanded, with the window's logical size.
		internal RlLayout OnLayout;
		// Called when the window collapses or closes (so the plugin can drop popups etc.).
		internal RlAction OnHidden;
		// Called when the window is hidden via the x button or config (e.g. to restore reparented UI).
		internal RlAction OnClosed;

		private Text collapseLabel;
		private float cachedDpi = -1f;
		private double nextProbe;
		private float lastScale = -1f;
		private int screenW;
		private int screenH;

		internal RlUguiWindow(BaseUnityPlugin owner, string title, string section, float defX, float defY, float defW, float defH, float minW, float minH, float maxW, float maxH)
		{
			this.owner = owner;
			this.title = title;
			this.minW = minW;
			this.minH = minH;
			this.maxW = maxW;
			this.maxH = maxH;
			ConfigFile c = owner.Config;
			show = c.Bind<bool>(section, "Show", true, "Show the in-game window.");
			savedCollapsed = c.Bind<bool>(section, "Collapsed", false, "Collapse the window to its title bar.");
			savedScale = c.Bind<float>(section, "Scale", 1f, "Extra scale multiplier applied on top of DPI (S- / S+).");
			savedX = c.Bind<float>(section, "X", defX, "Saved horizontal position.");
			savedY = c.Bind<float>(section, "Y", defY, "Saved vertical position.");
			savedW = c.Bind<float>(section, "Width", defW, "Saved width.");
			savedH = c.Bind<float>(section, "Height", defH, "Saved height.");
			dpiAware = c.Bind<bool>(section, "DpiAware", true, "Scale with the monitor's DPI. Turn off if the window is the wrong size on a mixed-DPI setup.");
			X = savedX.Value;
			Y = savedY.Value;
			W = Mathf.Clamp(savedW.Value, minW, maxW);
			H = Mathf.Clamp(savedH.Value, minH, maxH);
			UserScale = Mathf.Clamp(savedScale.Value, 0.65f, 2.5f);
			Collapsed = savedCollapsed.Value;
			show.SettingChanged += OnShowChanged;
		}

		internal bool Visible
		{
			get { return show.Value; }
		}

		internal void Dispose()
		{
			show.SettingChanged -= OnShowChanged;
			if (Canvas != null)
			{
				UnityEngine.Object.Destroy(Canvas.gameObject);
			}
		}

		private void OnShowChanged(object sender, EventArgs e)
		{
			if (Canvas != null)
			{
				Canvas.gameObject.SetActive(show.Value);
			}
			if (!show.Value && OnClosed != null)
			{
				OnClosed();
			}
		}

		internal void SetActive(bool active)
		{
			if (Canvas != null)
			{
				Canvas.gameObject.SetActive(active && show.Value);
			}
		}

		// ---------------------------------------------------------------- build

		internal void Build(string canvasName)
		{
			GameObject root = new GameObject(canvasName, new Type[] { typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster) });
			UnityEngine.Object.DontDestroyOnLoad(root);
			Canvas = root.GetComponent<Canvas>();
			Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			Canvas.overrideSorting = true;
			Canvas.sortingOrder = 30000;
			root.SetActive(show.Value);

			Window = NewRect("Window", root.transform);
			Vector2 corner = new Vector2(0f, 1f);
			Window.pivot = corner;
			Window.anchorMin = corner;
			Window.anchorMax = corner;
			Window.gameObject.AddComponent<Image>().color = PanelCol;
			Outline edge = Window.gameObject.AddComponent<Outline>();
			edge.effectColor = BorderCol;
			edge.effectDistance = new Vector2(1f, -1f);

			RectTransform strip = NewRect("Drag title bar", Window);
			strip.anchorMin = new Vector2(0f, 1f);
			strip.anchorMax = new Vector2(1f, 1f);
			strip.pivot = new Vector2(0f, 1f);
			strip.offsetMin = new Vector2(0f, -26f);
			strip.offsetMax = Vector2.zero;
			strip.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
			RlPointer move = strip.gameObject.AddComponent<RlPointer>();
			move.owner = this;
			move.resize = false;

			Text t = NewText("Title", Window, title, TextAnchor.MiddleLeft);
			TitleLabel = t;
			t.rectTransform.anchorMin = new Vector2(0f, 1f);
			t.rectTransform.anchorMax = new Vector2(1f, 1f);
			t.rectTransform.offsetMin = new Vector2(Margin, -25f);
			t.rectTransform.offsetMax = new Vector2(-112f, -3f);

			Button smaller = HeaderButton("Scale down", "S-", delegate { SetScale(UserScale - 0.1f); });
			Button larger = HeaderButton("Scale up", "S+", delegate { SetScale(UserScale + 0.1f); });
			Button collapse = HeaderButton("Collapse or expand", "—", ToggleCollapse);
			Button close = HeaderButton("Close", "x", delegate { show.Value = false; owner.Config.Save(); });
			collapseLabel = collapse.GetComponentInChildren<Text>();
			PinTopRight(smaller.GetComponent<RectTransform>(), 86f, 3f, 26f, 18f);
			PinTopRight(larger.GetComponent<RectTransform>(), 57f, 3f, 26f, 18f);
			PinTopRight(collapse.GetComponent<RectTransform>(), 32f, 3f, 22f, 18f);
			PinTopRight(close.GetComponent<RectTransform>(), 7f, 3f, 22f, 18f);

			Body = NewRect("Body", Window);
			Body.anchorMin = Vector2.zero;
			Body.anchorMax = Vector2.one;
			Body.offsetMin = Vector2.zero;
			Body.offsetMax = Vector2.zero;
		}

		// Adds the bottom-right resize arrow; call after the plugin has built the rest of its body.
		internal void AddResizeHandle()
		{
			Text arrow = NewText("Resize", Body, "↘", TextAnchor.MiddleCenter);
			arrow.raycastTarget = true;
			RectTransform grip = arrow.rectTransform;
			Vector2 br = new Vector2(1f, 0f);
			grip.pivot = br;
			grip.anchorMin = br;
			grip.anchorMax = br;
			grip.anchoredPosition = new Vector2(-2f, 2f);
			grip.sizeDelta = new Vector2(18f, 18f);
			RlPointer p = grip.gameObject.AddComponent<RlPointer>();
			p.owner = this;
			p.resize = true;
		}

		private Button HeaderButton(string name, string label, RlAction action)
		{
			Button b = NewButton(name, Window, label, null);
			RlPointerDown pd = b.gameObject.AddComponent<RlPointerDown>();
			pd.action = action;
			return b;
		}

		// ---------------------------------------------------------------- scale / DPI

		private float Dpi()
		{
			if (!dpiAware.Value)
			{
				return 1f;
			}
			double now = Time.realtimeSinceStartup;
			if (cachedDpi > 0f && now < nextProbe)
			{
				return cachedDpi;
			}
			nextProbe = now + 5.0;
			float result = 1f;
			bool ok = false;
			try
			{
				POINT pt = default(POINT);
				pt.X = (int)X;
				pt.Y = (int)Y;
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
			cachedDpi = Mathf.Clamp(ok ? result : 1f, 1f, 2.5f);
			return cachedDpi;
		}

		internal float Scale
		{
			get { return Dpi() * Mathf.Clamp(UserScale, 0.65f, 2.5f); }
		}

		internal void SetScale(float v)
		{
			UserScale = Mathf.Clamp(v, 0.65f, 2.5f);
			cachedDpi = -1f;
			Apply();
			Persist();
		}

		// ---------------------------------------------------------------- geometry

		internal void Tick()
		{
			if (Window == null)
			{
				return;
			}
			float s = Scale;
			if (screenW != Screen.width || screenH != Screen.height || Mathf.Abs(s - lastScale) > 0.001f)
			{
				Apply();
			}
		}

		internal void Apply()
		{
			if (Window == null)
			{
				return;
			}
			screenW = Screen.width;
			screenH = Screen.height;
			float s = Scale;
			lastScale = s;
			Canvas.scaleFactor = s;
			float w = W * s;
			X = Mathf.Clamp(X, -(w - 60f), Mathf.Max(0f, (float)Screen.width - 60f));
			Y = Mathf.Clamp(Y, 0f, Mathf.Max(0f, (float)Screen.height - 24f));
			float h = Collapsed ? CollapsedH : H;
			Window.sizeDelta = new Vector2(W, h);
			Window.anchoredPosition = new Vector2(X / s, -Y / s);
			Body.gameObject.SetActive(!Collapsed);
			if (collapseLabel != null)
			{
				collapseLabel.text = Collapsed ? "+" : "—";
			}
			if (Collapsed)
			{
				if (OnHidden != null)
				{
					OnHidden();
				}
				return;
			}
			if (OnLayout != null)
			{
				OnLayout(W, H);
			}
		}

		internal void Drag(Vector2 pixelDelta, bool resize)
		{
			if (Window == null)
			{
				return;
			}
			float s = Scale;
			if (!resize)
			{
				X += pixelDelta.x;
				Y -= pixelDelta.y;
			}
			else
			{
				W = Mathf.Clamp(W + pixelDelta.x / s, minW, maxW);
				H = Mathf.Clamp(H - pixelDelta.y / s, minH, maxH);
			}
			Apply();
		}

		internal void ToggleCollapse()
		{
			Collapsed = !Collapsed;
			Apply();
			Persist();
		}

		internal void Persist()
		{
			savedX.Value = X;
			savedY.Value = Y;
			savedW.Value = W;
			savedH.Value = H;
			savedScale.Value = UserScale;
			savedCollapsed.Value = Collapsed;
			cachedDpi = -1f;
			owner.Config.Save();
		}

		// ---------------------------------------------------------------- widgets (shared look)

		internal static RectTransform NewRect(string name, Transform parent)
		{
			GameObject go = new GameObject(name, new Type[] { typeof(RectTransform) });
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		internal static Text NewText(string name, Transform parent, string value, TextAnchor alignment)
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

		internal static Button NewButton(string name, Transform parent, string label, UnityAction onClick)
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

		internal static void SetRect(RectTransform rect, float left, float top, float w, float h)
		{
			Vector2 corner = new Vector2(0f, 1f);
			rect.pivot = corner;
			rect.anchorMin = corner;
			rect.anchorMax = corner;
			rect.anchoredPosition = new Vector2(left, -top);
			rect.sizeDelta = new Vector2(w, h);
		}

		internal static void PinTopRight(RectTransform rect, float right, float top, float w, float h)
		{
			Vector2 corner = new Vector2(1f, 1f);
			rect.pivot = corner;
			rect.anchorMin = corner;
			rect.anchorMax = corner;
			rect.anchoredPosition = new Vector2(-right, -top);
			rect.sizeDelta = new Vector2(w, h);
		}

		internal static void SetLabel(Button button, string value)
		{
			Text t = button.GetComponentInChildren<Text>();
			if (t != null)
			{
				t.text = value;
			}
		}
	}

	internal sealed class RlPointerDown : MonoBehaviour, IPointerDownHandler
	{
		internal RlAction action;

		public void OnPointerDown(PointerEventData data)
		{
			if (action != null && data.button == PointerEventData.InputButton.Left)
			{
				action();
			}
		}
	}

	internal sealed class RlPointer : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		internal RlUguiWindow owner;
		internal bool resize;
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
				owner.Drag(eventData.position - previous, resize);
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
