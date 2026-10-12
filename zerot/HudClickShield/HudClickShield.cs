using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroT.HudClickShield
{
	// A click on a HUD must be the HUD's click only. VaM decides "the mouse is over the UI" (SuperController.GUIhitMouse, set in ProcessUI from its own
	// uGUI raycast) before it selects, grabs or drags what is under the pointer. IMGUI windows (every BepInEx plugin window) and screen-pinned overlay
	// canvases (the Arousal / Stimulation / Toggle HUDs) are invisible to that raycast, so a click on them also reached the scene behind.
	// This plugin tells VaM the mouse is over the UI whenever it is over one of those:
	//   - IMGUI: every control drawn on a Repaint (GUIStyle / button / texture draws) is recorded as a screen rectangle; the previous frame's rectangles are the "covered" area
	//   - overlay canvases: root canvases in ScreenSpaceOverlay mode whose name matches the filter ("HUD"), with a GraphicRaycaster, hit-tested graphic by graphic
	[BepInPlugin(PluginGuid, "HUD Click Shield", "1.0.0")]
	public sealed class HudClickShieldPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.zerot.hudclickshield";

		private static ConfigEntry<bool> _enabled, _imgui, _overlay, _debug;
		private static ConfigEntry<string> _filter;
		private static ManualLogSourceProxy log;

		private sealed class ManualLogSourceProxy
		{
			public BepInEx.Logging.ManualLogSource Src;
			public void Info(string s) { Src.LogInfo(s); }
			public void Warn(string s) { Src.LogWarning(s); }
		}

		// ------------------------------------------------------------------ the covered area
		private static List<Rect> cur = new List<Rect>(64), prev = new List<Rect>(64);
		private static int recordFrame = -1;
		private static int lastDrawFrame = -100;
		private static int overFrame = -1;
		private static bool overResult;
		private static Vector3 lastMouse;
		private static bool pressStartedOnHud;

		public static int RectCount { get { return prev.Count; } }

		// IMGUI draw hooks call this with the control's rectangle
		public static void Record(Rect position)
		{
			try
			{
				Event e = Event.current;
				if (e == null || e.type != EventType.Repaint) return;
				if (position.width < 2f || position.height < 2f) return;
				// a full-screen draw is a backdrop, not a HUD: never let it swallow every click
				if (position.width >= Screen.width * 0.9f && position.height >= Screen.height * 0.9f) return;
				int f = Time.frameCount;
				if (f != recordFrame)
				{
					List<Rect> t = prev; prev = cur; cur = t;
					cur.Clear();
					recordFrame = f;
				}
				lastDrawFrame = f;
				Vector2 p = GUIUtility.GUIToScreenPoint(position.position);
				Rect sr = new Rect(p.x, p.y, position.width, position.height);
				if (cur.Count > 0) { Rect lr = cur[cur.Count - 1]; if (lr.x == sr.x && lr.y == sr.y && lr.width == sr.width && lr.height == sr.height) return; }   // the public GUI call and the internal draw it ends in report the same rectangle
				cur.Add(sr);
			}
			catch { }
		}

		public static bool ImguiCovers(Vector3 mouse)
		{
			// nothing drawn for a few frames (windows hidden): forget the old rectangles
			if (Time.frameCount - lastDrawFrame > 3) return false;
			float y = Screen.height - mouse.y;
			// the list being filled by the current frame's Repaint is as good as the finished one
			for (int pass = 0; pass < 2; pass++)
			{
				List<Rect> l = pass == 0 ? prev : cur;
				for (int i = 0; i < l.Count; i++)
				{
					Rect r = l[i];
					if (mouse.x >= r.x && mouse.x <= r.x + r.width && y >= r.y && y <= r.y + r.height) return true;
				}
			}
			return false;
		}

		// ------------------------------------------------------------------ overlay canvases
		private sealed class Overlay
		{
			public Canvas Canvas;
			public Graphic[] Graphics;
		}

		private static readonly List<Overlay> overlays = new List<Overlay>();
		private static float nextOverlayScan;

		private static void RescanOverlays()
		{
			overlays.Clear();
			string[] names = (_filter.Value ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			Canvas[] all = UnityEngine.Object.FindObjectsOfType<Canvas>();
			for (int i = 0; i < all.Length; i++)
			{
				Canvas c = all[i];
				if (c == null || !c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
				if (c.GetComponent<GraphicRaycaster>() == null) continue;
				bool match = names.Length == 0;
				for (int n = 0; n < names.Length && !match; n++)
					if (c.gameObject.name.IndexOf(names[n].Trim(), StringComparison.OrdinalIgnoreCase) >= 0) match = true;
				if (!match) continue;
				Overlay o = new Overlay();
				o.Canvas = c;
				o.Graphics = c.GetComponentsInChildren<Graphic>(true);
				overlays.Add(o);
			}
		}

		public static bool OverlayCovers(Vector3 mouse)
		{
			if (Time.realtimeSinceStartup >= nextOverlayScan)
			{
				nextOverlayScan = Time.realtimeSinceStartup + 3f;
				try { RescanOverlays(); } catch { }
			}
			for (int i = 0; i < overlays.Count; i++)
			{
				Overlay o = overlays[i];
				if (o.Canvas == null || !o.Canvas.enabled || !o.Canvas.gameObject.activeInHierarchy) continue;
				for (int g = 0; g < o.Graphics.Length; g++)
				{
					Graphic gr = o.Graphics[g];
					if (gr == null || !gr.raycastTarget || !gr.enabled || !gr.gameObject.activeInHierarchy || gr.color.a < 0.05f) continue;
					if (RectTransformUtility.RectangleContainsScreenPoint(gr.rectTransform, new Vector2(mouse.x, mouse.y), null)) return true;
				}
			}
			return false;
		}

		// ------------------------------------------------------------------ the answer VaM gets
		public static bool MouseOverHud()
		{
			if (_enabled == null || !_enabled.Value) return false;
			int f = Time.frameCount;
			if (f == overFrame) return overResult;
			overFrame = f;
			Vector3 m = Input.mousePosition;
			bool over = false;
			if (m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height)
			{
				if (_imgui.Value && ImguiCovers(m)) over = true;
				else if (_overlay.Value && OverlayCovers(m)) over = true;
			}
			// a press belongs to wherever it started: begun on a HUD it stays the HUD's even when dragged off; begun elsewhere (a VaM slider) it is not stolen on the way over a HUD
			bool down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
			bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
			if (down) pressStartedOnHud = over;
			if (held) over = pressStartedOnHud; else pressStartedOnHud = false;
			if (over != overResult && _debug.Value) log.Info("mouse " + (over ? "over a HUD" : "left the HUD") + " at " + m.x + "," + m.y + " (" + RectCount + " IMGUI rects)");
			lastMouse = m;
			overResult = over;
			return over;
		}

		// ------------------------------------------------------------------ Harmony
		private static FieldInfo guiHitMouse;

		// GUIStyle / GUI draw hooks: the rectangle parameter is always called "position"
		public static void DrawPrefix(Rect position) { Record(position); }

		// VaM's own uGUI (the menus, sliders, buttons) takes the click through LookInputModule: while the mouse is on a HUD it is simply not asked
		public static bool UiInputPrefix() { return !MouseOverHud(); }

		public static void ProcessUIPostfix(object __instance)
		{
			try
			{
				if (guiHitMouse == null) return;
				if (MouseOverHud()) guiHitMouse.SetValue(__instance, true);
			}
			catch { }
		}

		private void Awake()
		{
			_enabled = Config.Bind("Shield", "Enabled", true, "A click on a HUD no longer reaches the scene behind it.");
			_imgui = Config.Bind("Shield", "ImguiWindows", true, "Protect IMGUI plugin windows (every BepInEx plugin window).");
			_overlay = Config.Bind("Shield", "OverlayHuds", true, "Protect screen-pinned overlay HUD canvases (Arousal / Stimulation / Toggle HUD).");
			_filter = Config.Bind("Shield", "OverlayNameContains", "HUD", "Only overlay canvases whose name contains one of these words (comma separated) are protected; empty = every overlay canvas that has a GraphicRaycaster.");
			_debug = Config.Bind("Shield", "Debug", false, "Log when the mouse enters / leaves a protected area.");
			log = new ManualLogSourceProxy();
			log.Src = Logger;

			Harmony h = new Harmony(PluginGuid);
			int ok = 0, total = 0;
			const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
			HarmonyMethod pre = new HarmonyMethod(typeof(HudClickShieldPlugin).GetMethod("DrawPrefix", BindingFlags.Public | BindingFlags.Static));

			// every IMGUI control ends in one of these (buttons / toggles / fields draw natively and come through DoButton or a GUIStyle draw)
			total++; ok += TryPatch(h, typeof(GUIStyle), "Internal_Draw2", pre, null);
			total++; ok += TryPatch(h, typeof(GUIStyle), "Internal_Draw", pre, null);
			total++; ok += TryPatch(h, typeof(GUI), "DoButton", pre, null);
			total++; ok += TryPatch(h, typeof(GUI), "DoToggle", pre, null);
			total++; ok += TryPatch(h, typeof(GUI), "DrawTexture", pre, null, 8);

			// the public GUI entry points too (Button, Toggle, Box, Label, fields, sliders, DrawTexture ...): a small internal helper can be inlined
			// into its caller by the JIT before it is patched, the public methods the plugins call are not
			try
			{
				int n = 0;
				MethodInfo[] gm = typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
				for (int i = 0; i < gm.Length; i++)
				{
					if (gm[i].IsGenericMethod) continue;
					ParameterInfo[] ps = gm[i].GetParameters();
					bool has = false;
					for (int q = 0; q < ps.Length; q++) if (ps[q].Name == "position" && ps[q].ParameterType == typeof(Rect)) has = true;
					if (!has) continue;
					try { h.Patch(gm[i], pre, null); n++; } catch { }
				}
				log.Info("  hooked " + n + " public GUI.* draw methods");
				total++; if (n > 0) ok++;
			}
			catch (Exception e) { log.Warn("public GUI hooks: " + e.Message); }

			// VaM's own "mouse is over the UI" flag
			guiHitMouse = typeof(SuperController).GetField("GUIhitMouse", All);
			total++;
			if (guiHitMouse == null) log.Warn("SuperController.GUIhitMouse not found: this VaM version cannot be shielded.");
			else ok += TryPatch(h, typeof(SuperController), "ProcessUI", null, new HarmonyMethod(typeof(HudClickShieldPlugin).GetMethod("ProcessUIPostfix", BindingFlags.Public | BindingFlags.Static)));

			// the click on the VaM menu behind a HUD: LookInputModule.ProcessMouseAlt is what hands the mouse to VaM's uGUI (and the standalone Process path in case it is on)
			HarmonyMethod skip = new HarmonyMethod(typeof(HudClickShieldPlugin).GetMethod("UiInputPrefix", BindingFlags.Public | BindingFlags.Static));
			total++; ok += TryPatch(h, typeof(LookInputModule), "ProcessMouseAlt", skip, null, -1, false);
			total++; ok += TryPatch(h, typeof(LookInputModule), "Process", skip, null, -1, false);

			// the scene itself: select / grab / drag are already covered by GUIhitMouse; right-drag orbit, middle-drag pan and the wheel zoom of the monitor camera go through ProcessMouseControl
			total++; ok += TryPatch(h, typeof(SuperController), "ProcessMouseControl", skip, null, -1, false);

			log.Info("HUD Click Shield ready: " + ok + " of " + total + " hooks in place.");
		}

		private static int TryPatch(Harmony h, Type type, string name, HarmonyMethod pre, HarmonyMethod post, int paramCount = -1, bool needRect = true)
		{
			try
			{
				MethodBase target = null;
				MethodInfo[] ms = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				for (int i = 0; i < ms.Length; i++)
				{
					if (ms[i].Name != name) continue;
					ParameterInfo[] ps = ms[i].GetParameters();
					if (paramCount >= 0 && ps.Length != paramCount) continue;
					if (pre != null && needRect)
					{
						// needs a Rect parameter called "position"
						bool has = false;
						for (int p = 0; p < ps.Length; p++) if (ps[p].Name == "position" && ps[p].ParameterType == typeof(Rect)) has = true;
						if (!has) continue;
					}
					target = ms[i];
					// patch every overload that matches (Internal_Draw, DrawTexture ...), not only the first
					h.Patch(target, pre, post);
					log.Info("  hooked " + type.Name + "." + name + "(" + ps.Length + ")");
				}
				return target != null ? 1 : 0;
			}
			catch (Exception e)
			{
				log.Warn("could not hook " + type.Name + "." + name + ": " + e.Message);
				return 0;
			}
		}
	}
}
