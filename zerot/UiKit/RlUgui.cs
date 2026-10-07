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

	internal delegate void RlBool(bool value);

	// uGUI twin of RlChrome: a screen-space window that looks and behaves like the VpbRandomLook IMGUI window
	// (default dark box, "Title ... S- S+ - x" row, resize arrow bottom-right, 34px collapsed bar, monitor DPI,
	// config keys Show/Collapsed/Scale/X/Y/Width/Height/DpiAware). The plugin builds its own body under Body.
	internal sealed class RlUguiWindow
	{
		internal static readonly Color PanelCol = new Color(0.04f, 0.04f, 0.04f, 0.72f);
		internal static readonly Color BorderCol = new Color(0.40f, 0.40f, 0.40f, 1f);
		internal static readonly Color ButtonN = new Color(0.31f, 0.31f, 0.31f, 1f);
		internal static readonly Color ButtonH = new Color(0.40f, 0.40f, 0.40f, 1f);
		internal static readonly Color ButtonP = new Color(0.22f, 0.22f, 0.22f, 1f);
		internal static readonly Color TextCol = new Color(0.90f, 0.90f, 0.90f, 1f);
		internal static readonly Color ViewCol = new Color(0f, 0f, 0f, 0.45f);

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
		private ConfigEntry<bool> savedActive;
		private Text powerLabel;
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
		// Active == plugin switched on. When off the window shrinks to its title bar and the plugin is told to stop everything.
		internal bool Active = true;
		internal RlBool OnActiveChanged;

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
		private ConfigEntry<bool> external;
		private ConfigEntry<string> extRect;
		private bool onExternal;
		private bool lastExternal;
		private RlExternalWindow ext;
		private GameObject extCamObj;
		private Camera extCam;
		private RenderTexture extRt;
		private Texture2D extTex;
		private int extW;
		private int extH;
		private float nextFrame;
		private float nextRectSave;
		private string lastRect = "";
		private bool extShown = true;
		private GameObject resizeGrip;
		private PointerEventData pressed;
		private GameObject hovered;
		private Vector2 lastPos;
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
			savedActive = c.Bind<bool>("General", "Active", true, "Turn the plugin off completely (the window shrinks to its title bar).");
			savedScale = c.Bind<float>(section, "Scale", 1f, "Extra scale multiplier applied on top of DPI (S- / S+).");
			savedX = c.Bind<float>(section, "X", defX, "Saved horizontal position.");
			savedY = c.Bind<float>(section, "Y", defY, "Saved vertical position.");
			savedW = c.Bind<float>(section, "Width", defW, "Saved width.");
			savedH = c.Bind<float>(section, "Height", defH, "Saved height.");
			external = c.Bind<bool>(section, "ExternalWindow", false, "EXPERIMENTAL: show this window as a separate Windows window outside the game instead of over the game view.");
			extRect = c.Bind<string>(section, "ExternalRect", "", "Last position and size of the external window (x,y,width,height).");
			dpiAware = c.Bind<bool>(section, "DpiAware", true, "Scale with the monitor's DPI. Turn off if the window is the wrong size on a mixed-DPI setup.");
			X = savedX.Value;
			Y = savedY.Value;
			W = Mathf.Clamp(savedW.Value, minW, maxW);
			H = Mathf.Clamp(savedH.Value, minH, maxH);
			UserScale = Mathf.Clamp(savedScale.Value, 0.3f, 2.5f);
			Collapsed = savedCollapsed.Value;
			Active = savedActive.Value;
			show.SettingChanged += OnShowChanged;
		}

		internal bool Visible
		{
			get { return show.Value; }
		}

		internal void Dispose()
		{
			show.SettingChanged -= OnShowChanged;
			if (ext != null)
			{
				ext.Close();
				ext = null;
			}
			if (extCamObj != null)
			{
				UnityEngine.Object.Destroy(extCamObj);
			}
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
			ApplyDisplay();
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
			t.rectTransform.offsetMax = new Vector2(-150f, -3f);

			Button power = HeaderButton("Power", "On", delegate { SetPlugin(!Active); });
			powerLabel = power.GetComponentInChildren<Text>();
			PinTopRight(power.GetComponent<RectTransform>(), 115f, 3f, 31f, 18f);
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
			resizeGrip = grip.gameObject;
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
			if (onExternal)
			{
				return 1f;
			}
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

		private int ScreenW
		{
			get { return onExternal && ext != null ? ext.ClientW : Screen.width; }
		}

		private int ScreenH
		{
			get { return onExternal && ext != null ? ext.ClientH : Screen.height; }
		}

		// ---------------------------------------------------------------- external (separate Windows window)

		private void ApplyDisplay()
		{
			lastExternal = external.Value;
			if (Canvas == null)
			{
				return;
			}
			if (external.Value)
			{
				OpenExternal();
			}
			else
			{
				CloseExternal();
			}
		}

		private void OpenExternal()
		{
			if (ext != null)
			{
				return;
			}
			try
			{
				int x = 120, y = 120, w = 436, h = 559;
				string[] parts = (extRect.Value ?? "").Split(',');
				if (parts.Length == 4)
				{
					int px, py, pw, ph;
					if (int.TryParse(parts[0], out px) && int.TryParse(parts[1], out py) && int.TryParse(parts[2], out pw) && int.TryParse(parts[3], out ph) && pw >= 120 && ph >= 120)
					{
						x = px;
						y = py;
						w = pw;
						h = ph;
					}
				}
				ext = new RlExternalWindow(title, x, y, w - 16, h - 39);
				extCamObj = new GameObject("RlExternal camera");
				UnityEngine.Object.DontDestroyOnLoad(extCamObj);
				extCamObj.transform.position = new Vector3(2000f, 2000f, 2000f);
				extCam = extCamObj.AddComponent<Camera>();
				extCam.orthographic = true;
				extCam.clearFlags = CameraClearFlags.SolidColor;
				extCam.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
				extCam.nearClipPlane = 0.1f;
				extCam.farClipPlane = 100f;
				extCam.depth = -100f;
				extCam.useOcclusionCulling = false;
				extCam.enabled = false;
				Canvas.renderMode = RenderMode.ScreenSpaceCamera;
				Canvas.worldCamera = extCam;
				Canvas.planeDistance = 10f;
				// VaM's own input module must not hit-test this hidden canvas against the game mouse.
				GraphicRaycaster gr = Canvas.GetComponent<GraphicRaycaster>();
				if (gr != null)
				{
					gr.enabled = false;
				}
				onExternal = true;
				cachedDpi = -1f;
				extW = 0;
				extH = 0;
				nextFrame = 0f;
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogWarning("ExternalWindow could not start, staying in the game view: " + e.Message);
				CloseExternal();
			}
		}

		private void CloseExternal()
		{
			if (ext != null)
			{
				SaveExtRect();
				ext.Close();
				ext = null;
			}
			if (extCamObj != null)
			{
				UnityEngine.Object.Destroy(extCamObj);
				extCamObj = null;
				extCam = null;
			}
			if (extRt != null)
			{
				extRt.Release();
				UnityEngine.Object.Destroy(extRt);
				extRt = null;
			}
			if (extTex != null)
			{
				UnityEngine.Object.Destroy(extTex);
				extTex = null;
			}
			pressed = null;
			hovered = null;
			bool was = onExternal;
			onExternal = false;
			if (Canvas != null)
			{
				Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				Canvas.worldCamera = null;
				GraphicRaycaster gr = Canvas.GetComponent<GraphicRaycaster>();
				if (gr != null)
				{
					gr.enabled = true;
				}
			}
			if (was)
			{
				cachedDpi = -1f;
				Apply();
			}
		}

		private void SaveExtRect()
		{
			if (ext == null)
			{
				return;
			}
			string r = ext.RectString();
			if (r.Length > 0 && r != lastRect && !ext.Minimized)
			{
				lastRect = r;
				extRect.Value = r;
			}
		}

		private void ApplyExternalLayout()
		{
			float s = Scale;
			lastScale = s;
			Canvas.scaleFactor = s;
			float w = Mathf.Max(100f, (extW > 0 ? extW : ext.ClientW) / s);
			float h = Mathf.Max(60f, (extH > 0 ? extH : ext.ClientH) / s);
			Window.anchoredPosition = Vector2.zero;
			Window.sizeDelta = new Vector2(w, h);
			Body.gameObject.SetActive(Active);
			if (resizeGrip != null)
			{
				resizeGrip.SetActive(false);
			}
			if (powerLabel != null)
			{
				powerLabel.text = Active ? "On" : "Off";
			}
			if (collapseLabel != null)
			{
				collapseLabel.text = "—";
			}
			if (Active)
			{
				if (OnLayout != null)
				{
					OnLayout(w, h);
				}
			}
			else if (OnHidden != null)
			{
				OnHidden();
			}
		}

		private void TickExternal()
		{
			if (ext == null)
			{
				return;
			}
			if (ext.Closed)
			{
				external.Value = false;
				lastExternal = false;
				owner.Config.Save();
				CloseExternal();
				return;
			}
			if (!ext.Created)
			{
				return;
			}
			bool vis = show.Value;
			if (vis != extShown)
			{
				extShown = vis;
				ext.SetVisible(vis);
			}
			if (!vis)
			{
				return;
			}
			int cw = ext.ClientW;
			int ch = ext.ClientH;
			if (extRt == null || cw != extW || ch != extH)
			{
				if (extRt != null)
				{
					extCam.targetTexture = null;
					extRt.Release();
					UnityEngine.Object.Destroy(extRt);
				}
				if (extTex != null)
				{
					UnityEngine.Object.Destroy(extTex);
				}
				extRt = new RenderTexture(cw, ch, 24, RenderTextureFormat.ARGB32);
				extTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
				extCam.targetTexture = extRt;
				extW = cw;
				extH = ch;
				Apply();
				nextFrame = 0f;
			}
			RlExternalWindow.Mouse m;
			while (ext.TryDequeue(out m))
			{
				HandleMouse(m);
			}
			if (Time.unscaledTime >= nextFrame && !ext.Minimized)
			{
				nextFrame = Time.unscaledTime + 0.05f;
				Canvas.ForceUpdateCanvases();
				extCam.Render();
				RenderTexture prev = RenderTexture.active;
				RenderTexture.active = extRt;
				extTex.ReadPixels(new Rect(0f, 0f, cw, ch), 0, 0, false);
				RenderTexture.active = prev;
				ext.Submit(extTex.GetRawTextureData(), cw, ch);
			}
			if (Time.unscaledTime >= nextRectSave)
			{
				nextRectSave = Time.unscaledTime + 3f;
				string before = extRect.Value;
				SaveExtRect();
				if (extRect.Value != before)
				{
					owner.Config.Save();
				}
			}
		}

		// Finds the topmost raycast-able graphic under a point of the external canvas.
		private GameObject Hit(Vector2 pos)
		{
			Graphic best = null;
			int bestDepth = -1;
			Graphic[] all = Canvas.GetComponentsInChildren<Graphic>(false);
			for (int i = 0; i < all.Length; i++)
			{
				Graphic g = all[i];
				if (g == null || !g.raycastTarget || !g.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pos, extCam))
				{
					continue;
				}
				ICanvasRaycastFilter f = g as ICanvasRaycastFilter;
				if (f != null && !f.IsRaycastLocationValid(pos, extCam))
				{
					continue;
				}
				if (g.depth >= bestDepth)
				{
					bestDepth = g.depth;
					best = g;
				}
			}
			return best != null ? best.gameObject : null;
		}

		private PointerEventData NewPointer(Vector2 pos, GameObject go)
		{
			PointerEventData pe = new PointerEventData(EventSystem.current);
			pe.position = pos;
			pe.button = PointerEventData.InputButton.Left;
			RaycastResult rr = new RaycastResult();
			rr.gameObject = go;
			rr.module = Canvas.GetComponent<GraphicRaycaster>();
			rr.screenPosition = pos;
			pe.pointerCurrentRaycast = rr;
			pe.pointerPressRaycast = rr;
			return pe;
		}

		private void HandleMouse(RlExternalWindow.Mouse m)
		{
			if (EventSystem.current == null || extCam == null)
			{
				return;
			}
			Vector2 pos = new Vector2(m.X, extH - m.Y);
			try
			{
				if (m.Kind == 0)
				{
					Vector2 delta = pos - lastPos;
					lastPos = pos;
					if (pressed != null)
					{
						pressed.position = pos;
						pressed.delta = delta;
						if (!pressed.dragging && pressed.pointerDrag != null && (pos - pressed.pressPosition).sqrMagnitude > 25f)
						{
							pressed.dragging = true;
							pressed.eligibleForClick = false;
							ExecuteEvents.Execute(pressed.pointerDrag, pressed, ExecuteEvents.beginDragHandler);
						}
						if (pressed.dragging && pressed.pointerDrag != null)
						{
							ExecuteEvents.Execute(pressed.pointerDrag, pressed, ExecuteEvents.dragHandler);
						}
					}
					else
					{
						GameObject go = Hit(pos);
						if (go != hovered)
						{
							if (hovered != null)
							{
								ExecuteEvents.ExecuteHierarchy(hovered, NewPointer(pos, hovered), ExecuteEvents.pointerExitHandler);
							}
							hovered = go;
							if (go != null)
							{
								ExecuteEvents.ExecuteHierarchy(go, NewPointer(pos, go), ExecuteEvents.pointerEnterHandler);
							}
						}
					}
				}
				else if (m.Kind == 1)
				{
					lastPos = pos;
					GameObject go = Hit(pos);
					if (go == null)
					{
						pressed = null;
						return;
					}
					PointerEventData pe = NewPointer(pos, go);
					pe.pressPosition = pos;
					pe.clickCount = 1;
					pe.eligibleForClick = true;
					pe.useDragThreshold = true;
					GameObject press = ExecuteEvents.ExecuteHierarchy(go, pe, ExecuteEvents.pointerDownHandler);
					if (press == null)
					{
						press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
					}
					pe.pointerPress = press;
					pe.rawPointerPress = go;
					pe.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
					if (pe.pointerDrag != null)
					{
						ExecuteEvents.Execute(pe.pointerDrag, pe, ExecuteEvents.initializePotentialDrag);
					}
					pressed = pe;
				}
				else if (m.Kind == 2)
				{
					lastPos = pos;
					PointerEventData pe = pressed;
					pressed = null;
					if (pe == null)
					{
						return;
					}
					pe.position = pos;
					if (pe.pointerPress != null)
					{
						ExecuteEvents.Execute(pe.pointerPress, pe, ExecuteEvents.pointerUpHandler);
					}
					GameObject under = Hit(pos);
					GameObject click = under != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(under) : null;
					if (pe.pointerPress != null && click == pe.pointerPress && pe.eligibleForClick && !pe.dragging)
					{
						ExecuteEvents.Execute(pe.pointerPress, pe, ExecuteEvents.pointerClickHandler);
					}
					if (pe.dragging && pe.pointerDrag != null)
					{
						ExecuteEvents.Execute(pe.pointerDrag, pe, ExecuteEvents.endDragHandler);
					}
				}
				else if (m.Kind == 3)
				{
					GameObject go = Hit(lastPos);
					if (go != null)
					{
						PointerEventData pe = NewPointer(lastPos, go);
						pe.scrollDelta = new Vector2(0f, m.Wheel / 120f * 2f);
						ExecuteEvents.ExecuteHierarchy(go, pe, ExecuteEvents.scrollHandler);
					}
				}
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogWarning("External window input failed: " + e.Message);
			}
		}

		internal float Scale
		{
			get { return Dpi() * Mathf.Clamp(UserScale, 0.3f, 2.5f); }
		}

		internal void SetScale(float v)
		{
			UserScale = Mathf.Clamp(v, 0.3f, 2.5f);
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
			if (external.Value != lastExternal)
			{
				ApplyDisplay();
				Apply();
			}
			if (onExternal)
			{
				TickExternal();
				float se = Scale;
				if (Mathf.Abs(se - lastScale) > 0.001f)
				{
					Apply();
				}
				return;
			}
			float s = Scale;
			if (screenW != ScreenW || screenH != ScreenH || Mathf.Abs(s - lastScale) > 0.001f)
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
			if (onExternal)
			{
				ApplyExternalLayout();
				return;
			}
			if (resizeGrip != null)
			{
				resizeGrip.SetActive(true);
			}
			screenW = ScreenW;
			screenH = ScreenH;
			float s = Scale;
			lastScale = s;
			Canvas.scaleFactor = s;
			// X/Y stay the remembered position; only the displayed copy is kept on screen, so shrinking or moving the
			// VaM window never permanently pushes the plugin window away from its spot.
			float dispX, dispY;
			VisiblePosition(s, out dispX, out dispY);
			bool shrunk = Collapsed || !Active;
			float h = shrunk ? CollapsedH : H;
			Window.sizeDelta = new Vector2(W, h);
			Window.anchoredPosition = new Vector2(dispX / s, -dispY / s);
			Body.gameObject.SetActive(!shrunk);
			if (powerLabel != null)
			{
				powerLabel.text = Active ? "On" : "Off";
			}
			if (collapseLabel != null)
			{
				collapseLabel.text = Collapsed ? "+" : "—";
			}
			if (shrunk)
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

		private void VisiblePosition(float s, out float x, out float y)
		{
			float w = W * s;
			x = Mathf.Clamp(X, -(w - 60f), Mathf.Max(0f, (float)ScreenW - 60f));
			y = Mathf.Clamp(Y, 0f, Mathf.Max(0f, (float)ScreenH - 24f));
		}

		internal void Drag(Vector2 pixelDelta, bool resize)
		{
			if (Window == null)
			{
				return;
			}
			if (onExternal)
			{
				return;
			}
			float s = Scale;
			if (!resize)
			{
				VisiblePosition(s, out X, out Y);
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

		internal void SetPlugin(bool on)
		{
			Active = on;
			savedActive.Value = on;
			owner.Config.Save();
			if (OnActiveChanged != null)
			{
				OnActiveChanged(on);
			}
			Apply();
		}

		internal void ToggleCollapse()
		{
			if (onExternal)
			{
				return;
			}
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
			SaveExtRect();
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
