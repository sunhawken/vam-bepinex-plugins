using System.Collections.Generic;
using UnityEngine;

namespace ZeroT.OrgasmSuite
{
	// The window's look, after the "UI Future Pack" sheet: a navy ground, hairline white frames with corner brackets and node dots, spaced upper-case
	// section captions between thin rules, outlined square switches, a segmented loading bar, amber bracket buttons.
	//
	// Built for few IMGUI calls (every call is paid for on every frame, and every other IMGUI window shares the same budget):
	//  - frames, corner brackets and bracket buttons are ONE 9-sliced style draw each (a tiny hollow texture with a border) instead of 4 / 8 / 6 solid fills
	//  - the segmented bar is two tiled texture draws, the section rule with its end dots one 3-sliced draw
	//  - buttons / toggles are hit-tested by hand on mouse events (no IMGUI control per button)
	//  - the event type and mouse position are read once per pass, drawing is skipped on every event but Repaint
	//  - captions and style keys are cached, so a frame allocates nothing
	internal static class Fx
	{
		internal static readonly Color Bg = new Color(0.067f, 0.090f, 0.133f, 0.985f);     // #111722
		internal static readonly Color Ink = new Color(0.90f, 0.93f, 0.97f, 1f);           // line-art white
		internal static readonly Color Dim = new Color(0.52f, 0.57f, 0.66f, 1f);           // captions
		internal static readonly Color Faint = new Color(0.20f, 0.24f, 0.31f, 1f);         // inner rules, empty segments
		internal static readonly Color Amber = new Color(0.93f, 0.66f, 0.22f, 1f);         // the bracket accent of the sheet
		internal static readonly Color Lite = new Color(0.22f, 0.88f, 0.40f, 1f);          // Lite side
		internal static readonly Color Full = new Color(0.28f, 0.58f, 1.00f, 1f);          // BodyLanguage side

		// ------------------------------------------------------------------ per pass state
		private static Event _e;
		private static bool _paint;
		private static Vector2 _mouse;
		private static int _pressed;

		// every drawing pass (OnGUI, or the external window's synthetic one) starts with Window(), which calls this
		private static void Begin()
		{
			_e = Event.current;
			_paint = _e != null && _e.type == EventType.Repaint;
			_mouse = _e != null ? _e.mousePosition : Vector2.zero;
		}

		// ------------------------------------------------------------------ textures and styles (made once)
		private static readonly Dictionary<int, Texture2D> tex = new Dictionary<int, Texture2D>();
		private static readonly Dictionary<long, Texture2D> shapeTex = new Dictionary<long, Texture2D>();
		private static readonly Dictionary<long, GUIStyle> nineStyle = new Dictionary<long, GUIStyle>();
		private static readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>();
		private static readonly Dictionary<string, string> spacedCache = new Dictionary<string, string>();
		private static readonly Dictionary<string, string> upperCache = new Dictionary<string, string>();

		private static int Key(Color c)
		{
			return ((int)(c.r * 255f) << 24) | ((int)(c.g * 255f) << 16) | ((int)(c.b * 255f) << 8) | (int)(c.a * 255f);
		}

		private static Texture2D NewTex(int w, int h, bool repeat)
		{
			Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
			t.filterMode = FilterMode.Point;
			t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
			t.hideFlags = HideFlags.HideAndDontSave;
			return t;
		}

		private static Texture2D T(Color c)
		{
			int key = Key(c);
			Texture2D t;
			if (tex.TryGetValue(key, out t) && t != null) return t;
			t = NewTex(1, 1, false);
			t.SetPixel(0, 0, c);
			t.Apply();
			tex[key] = t;
			return t;
		}

		// shape: 0 hollow frame, 1 corner brackets, 2 "[ ]" brackets, 3 rule with end dots; a = border thickness / bracket length, b = line thickness / serif
		private static Texture2D Shape(int shape, Color c, int a, int b)
		{
			long key = ((long)shape << 56) ^ ((long)(uint)Key(c) << 20) ^ ((long)a << 10) ^ (long)b;
			Texture2D t;
			if (shapeTex.TryGetValue(key, out t) && t != null) return t;
			Color clear = new Color(c.r, c.g, c.b, 0f);
			int n = 2 * a + 1;
			if (shape == 3)
			{
				// 3-sliced rule: (cap | stretch column | cap) x a rows; dot in both caps, the hairline through the middle row. c = dot colour, line colour is Faint
				int h = a;
				t = NewTex(n, h, false);
				Color line = new Color(Faint.r, Faint.g, Faint.b, c.a);
				for (int y = 0; y < h; y++)
					for (int x = 0; x < n; x++)
					{
						bool cap = x < a || x > a;
						t.SetPixel(x, y, cap ? c : (y == h / 2 ? line : clear));
					}
			}
			else
			{
				t = NewTex(n, n, false);
				for (int y = 0; y < n; y++)
					for (int x = 0; x < n; x++)
					{
						bool on;
						if (shape == 0) on = x < b || x >= n - b || y < b || y >= n - b;
						else if (shape == 1) on = ((x < b || x >= n - b) && (y < a || y >= n - a)) || ((y < b || y >= n - b) && (x < a || x >= n - a));
						else on = (x < b || x >= n - b) || ((y < b || y >= n - b) && (x < a || x >= n - a));
						t.SetPixel(x, y, on ? c : clear);
					}
			}
			t.Apply();
			shapeTex[key] = t;
			return t;
		}

		private static Texture2D SegTex(Color c, int segPx)
		{
			long key = (1L << 58) ^ ((long)(uint)Key(c) << 8) ^ (long)segPx;
			Texture2D t;
			if (shapeTex.TryGetValue(key, out t) && t != null) return t;
			t = NewTex(segPx, 1, true);
			int gap = Mathf.Max(1, segPx / 5);
			for (int x = 0; x < segPx; x++) t.SetPixel(x, 0, x < segPx - gap ? c : new Color(c.r, c.g, c.b, 0f));
			t.Apply();
			shapeTex[key] = t;
			return t;
		}

		private static GUIStyle Nine(Texture2D t, int left, int right)
		{
			long key = ((long)t.GetInstanceID() << 16) ^ ((long)left << 8) ^ (long)right;
			GUIStyle st;
			if (nineStyle.TryGetValue(key, out st)) return st;
			st = new GUIStyle();
			st.normal.background = t; st.hover.background = t; st.active.background = t; st.focused.background = t;
			st.onNormal.background = t; st.onHover.background = t; st.onActive.background = t; st.onFocused.background = t;
			int v = left == right && t.height == 2 * left + 1 ? left : 0;
			st.border = new RectOffset(left, right, v, v);
			st.margin = new RectOffset(); st.padding = new RectOffset(); st.overflow = new RectOffset();
			nineStyle[key] = st;
			return st;
		}

		// dimmed while the GUI is disabled (no Person selected, suite off)
		internal static Color A(Color c) { return GUI.enabled ? c : new Color(c.r, c.g, c.b, c.a * 0.38f); }

		// ------------------------------------------------------------------ primitives
		internal static void Fill(Rect r, Color c) { if (_paint && r.width > 0f && r.height > 0f) GUI.DrawTexture(r, T(A(c))); }
		internal static void HLine(float x, float y, float w, Color c, float th) { Fill(new Rect(x, y, w, th), c); }
		internal static void VLine(float x, float y, float h, Color c, float th) { Fill(new Rect(x, y, th, h), c); }

		// a hollow rectangle in ONE draw
		internal static void Outline(Rect r, Color c, float th)
		{
			if (!_paint) return;
			int t = Mathf.Max(1, Mathf.RoundToInt(th));
			if (r.width < 2 * t + 1 || r.height < 2 * t + 1) { Fill(r, c); return; }
			GUI.Box(r, "", Nine(Shape(0, A(c), t, t), t, t));
		}

		// the four "L" corner brackets of the sheet's frames in ONE draw
		internal static void Corners(Rect r, Color c, float len, float th)
		{
			if (!_paint) return;
			int t = Mathf.Max(1, Mathf.RoundToInt(th));
			int l = Mathf.Max(t + 1, Mathf.RoundToInt(len));
			int max = (int)(Mathf.Min(r.width, r.height) / 2f) - 1;
			if (l > max) l = max;
			if (l <= t) return;
			GUI.Box(r, "", Nine(Shape(1, A(c), l, t), l, l));
		}

		internal static void Dot(float cx, float cy, float d, Color c) { Fill(new Rect(cx - d / 2f, cy - d / 2f, d, d), c); }

		// "[" and "]" brackets with the little serifs of the "[ 150 ELEMENT ]" tag, in ONE draw
		internal static void Brackets(Rect r, Color c, float th, float serif)
		{
			if (!_paint) return;
			int t = Mathf.Max(1, Mathf.RoundToInt(th));
			int sf = Mathf.Max(t + 1, Mathf.RoundToInt(serif));
			int max = (int)(Mathf.Min(r.width, r.height) / 2f) - 1;
			if (sf > max) sf = max;
			if (sf <= t) return;
			GUI.Box(r, "", Nine(Shape(2, A(c), sf, t), sf, sf));
		}

		// a hairline with a node dot at each end in ONE draw (the sheet's section rules)
		internal static void Rule(float x, float y, float w, float s, Color dot)
		{
			if (!_paint || w <= 0f) return;
			int d = Mathf.Max(3, Mathf.RoundToInt(3f * s));
			Rect r = new Rect(x - d / 2f, y - d / 2f, w + d, d);
			GUI.Box(r, "", Nine(Shape(3, A(dot), d, d), d, d));
		}

		// ------------------------------------------------------------------ text
		internal static GUIStyle Style(int size, TextAnchor anchor, Color c, bool bold)
		{
			long key = ((long)size << 40) | ((long)(int)anchor << 33) | ((long)(bold ? 1 : 0) << 32)
				| ((long)(int)(c.r * 255f) << 16) | ((long)(int)(c.g * 255f) << 8) | (long)(int)(c.b * 255f);
			GUIStyle st;
			if (styles.TryGetValue(key, out st)) return st;
			st = new GUIStyle(GUI.skin.label);
			st.fontSize = size;
			st.alignment = anchor;
			st.wordWrap = false;
			st.clipping = TextClipping.Clip;
			st.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
			st.normal.textColor = c;
			st.padding = new RectOffset(0, 0, 0, 0);
			st.margin = new RectOffset(0, 0, 0, 0);
			styles[key] = st;
			return st;
		}

		private static readonly Color Dimmed = new Color(1f, 1f, 1f, 0.4f);

		internal static void Text(Rect r, string text, int size, TextAnchor anchor, Color c, bool bold)
		{
			if (!_paint) return;
			GUIStyle st = Style(size, anchor, c, bold);
			Color old = GUI.contentColor;
			GUI.contentColor = GUI.enabled ? Color.white : Dimmed;
			GUI.Label(r, text, st);
			GUI.contentColor = old;
		}

		// a caption of the sheet: letter-spaced capitals
		internal static string Spaced(string t)
		{
			string cached;
			if (spacedCache.TryGetValue(t, out cached)) return cached;
			cached = BuildSpaced(t);
			spacedCache[t] = cached;
			return cached;
		}

		private static string Upper(string t)
		{
			string cached;
			if (upperCache.TryGetValue(t, out cached)) return cached;
			cached = t.ToUpperInvariant();
			upperCache[t] = cached;
			return cached;
		}

		private static string BuildSpaced(string t)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			string[] words = t.ToUpperInvariant().Split(' ');
			for (int w = 0; w < words.Length; w++)
			{
				if (w > 0) sb.Append("   ");               // a word gap is wider than a letter gap
				for (int i = 0; i < words[w].Length; i++)
				{
					if (i > 0) sb.Append(' ');
					sb.Append(words[w][i]);
				}
			}
			return sb.ToString();
		}

		// ------------------------------------------------------------------ input (by hand: no IMGUI control per button)
		internal static bool Hover(Rect r)
		{
			return _paint && GUI.enabled && r.Contains(_mouse);
		}

		// a click = press and release inside the same rectangle
		internal static bool Hit(Rect r)
		{
			Event e = _e;
			if (e == null || !GUI.enabled) return false;
			EventType t = e.type;
			if (t == EventType.MouseDown)
			{
				if (e.button == 0 && r.Contains(e.mousePosition)) { _pressed = RectKey(r); e.Use(); }
			}
			else if (t == EventType.MouseUp)
			{
				if (_pressed != 0 && _pressed == RectKey(r))
				{
					_pressed = 0;
					bool inside = r.Contains(e.mousePosition);
					e.Use();
					return inside;
				}
			}
			return false;
		}

		private static int RectKey(Rect r)
		{
			int h = 17;
			h = h * 31 + (int)(r.x * 4f); h = h * 31 + (int)(r.y * 4f); h = h * 31 + (int)(r.width * 4f); h = h * 31 + (int)(r.height * 4f);
			return h == 0 ? 1 : h;
		}

		// ------------------------------------------------------------------ widgets
		// a caption between two thin rules ("NAVIGATION", "LOADINGS" ...): one rule draw with its end dots, a ground-coloured knock-out, the caption
		internal static void Section(Rect r, string caption, float s)
		{
			if (!_paint) return;
			string t = Spaced(caption);
			float tw = 7.2f * s * t.Length + 14f * s;
			float mid = r.x + r.width / 2f;
			float y = r.y + r.height / 2f;
			Rule(r.x, y, r.width, s, Dim);
			Fill(new Rect(mid - tw / 2f, r.y, tw, r.height), Bg);
			Text(new Rect(mid - tw / 2f, r.y, tw, r.height), t, Mathf.Max(8, Mathf.RoundToInt(10f * s)), TextAnchor.MiddleCenter, Dim, false);
		}

		// the rule under the window title: hairline with node dots at both ends and a brighter stretch in the middle
		internal static void TitleRule(float x, float y, float w, float s)
		{
			Rule(x, y, w, s, Ink);
			HLine(x + w / 2f - 30f * s, y, 60f * s, Dim, 1f);
		}

		private static readonly Color BtnText = new Color(0.75f, 0.79f, 0.86f, 1f);
		private static readonly Color ToggleText = new Color(0.68f, 0.73f, 0.81f, 1f);

		// flat outlined button; the selected one gets corner brackets and a tint of its accent
		internal static bool Button(Rect r, string text, Color accent, bool selected, float s, bool spaced = true)
		{
			if (_paint)
			{
				bool hover = Hover(r);
				if (selected) Fill(r, new Color(accent.r, accent.g, accent.b, 0.16f));
				else if (hover) Fill(r, new Color(1f, 1f, 1f, 0.05f));
				Outline(r, selected ? accent : (hover ? Ink : Dim), 1f);
				if (selected) Corners(r, accent, 6f * s, 2f);
				Text(r, spaced ? Spaced(text) : Upper(text), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleCenter, selected || hover ? Ink : BtnText, false);
			}
			return Hit(r);
		}

		// a small square header button (S- S+ - x)
		internal static bool Mini(Rect r, string text, float s)
		{
			if (_paint)
			{
				bool hover = Hover(r);
				if (hover) Fill(r, new Color(1f, 1f, 1f, 0.07f));
				Outline(r, hover ? Ink : Dim, 1f);
				Text(r, text, Mathf.Max(8, Mathf.RoundToInt(10f * s)), TextAnchor.MiddleCenter, hover ? Ink : BtnText, false);
			}
			return Hit(r);
		}

		// the amber "[ LABEL ]" action
		internal static bool Bracket(Rect r, string text, float s)
		{
			if (_paint)
			{
				bool hover = Hover(r);
				if (hover) Fill(r, new Color(Amber.r, Amber.g, Amber.b, 0.10f));
				Brackets(r, Amber, 1f * s < 1f ? 1f : Mathf.Round(1.5f * s), 5f * s);
				Text(r, Spaced(text), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleCenter, hover ? Ink : Amber, false);
			}
			return Hit(r);
		}

		// outlined square with a solid inner square when on, in the colour of the side (green Lite / blue BodyLanguage)
		internal static bool Toggle(Rect r, string label, bool on, Color accent, float s, bool spaced = true)
		{
			if (_paint)
			{
				bool hover = Hover(r);
				if (hover) Fill(r, new Color(1f, 1f, 1f, 0.04f));
				HLine(r.x, r.yMax - 1f, r.width, Faint, 1f);
				float b = 12f * s;
				Rect box = new Rect(r.x + 4f * s, r.y + (r.height - b) / 2f, b, b);
				Outline(box, on ? accent : Dim, 1f);
				if (on) Fill(new Rect(box.x + 3f * s, box.y + 3f * s, box.width - 6f * s, box.height - 6f * s), accent);
				Text(new Rect(box.xMax + 8f * s, r.y, r.width - b - 16f * s, r.height), spaced ? Spaced(label) : Upper(label), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, on ? Ink : ToggleText, false);
			}
			return Hit(r);
		}

		private static int dragging;

		// the segmented loading bar of the sheet, draggable: the lit and the empty segments are one tiled draw each.
		// Returns the new value 0..1, or -1 when it was not touched.
		internal static float Bar(Rect r, float v, Color accent, float s, int id)
		{
			int n = 24;
			float pad = 4f * s;
			int segPx = Mathf.Max(3, Mathf.FloorToInt((r.width - 2f * pad) / n));
			float inner = segPx * n;
			float x0 = r.x + (r.width - inner) / 2f;
			if (_paint)
			{
				Outline(r, Dim, 1f);
				Corners(r, Ink, 5f * s, 2f);
				int lit = Mathf.RoundToInt(Mathf.Clamp01(v) * n);
				float y = r.y + pad, h = r.height - 2f * pad;
				if (h > 0f)
				{
					if (lit > 0) GUI.DrawTextureWithTexCoords(new Rect(x0, y, lit * segPx, h), SegTex(A(accent), segPx), new Rect(0f, 0f, lit, 1f));
					if (lit < n) GUI.DrawTextureWithTexCoords(new Rect(x0 + lit * segPx, y, (n - lit) * segPx, h), SegTex(A(Faint), segPx), new Rect(0f, 0f, n - lit, 1f));
				}
			}
			float result = -1f;
			Event e = _e;
			if (e != null && GUI.enabled)
			{
				EventType t = e.type;
				if (t == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = id; e.Use(); }
				if (dragging == id && (t == EventType.MouseDown || t == EventType.MouseDrag))
				{
					result = Mathf.Clamp01((e.mousePosition.x - x0) / inner);
					e.Use();
				}
				if (t == EventType.MouseUp && dragging == id) { dragging = 0; e.Use(); }
			}
			return result;
		}

		// a framed panel (status box): hairline frame, corner brackets
		internal static void Frame(Rect r, float s)
		{
			Outline(r, Faint, 1f);
			Corners(r, Dim, 5f * s, 1f);
		}

		private static readonly Dictionary<int, float> shown = new Dictionary<int, float>();

		// a bar that fills with liquid: the level eases to the real value, the leading edge ripples, bubbles rise inside, a bright skin rides the surface.
		// v is 0..1 (the same number the arousal / stimulation HUD shows); full level pulses amber.
		internal static void Liquid(Rect r, float v, Color accent, float s, int id)
		{
			if (!_paint) return;
			float now = Time.realtimeSinceStartup;
			float cur;
			if (!shown.TryGetValue(id, out cur)) cur = v;
			cur += (v - cur) * 0.12f;                 // eases, so a jump reads as the liquid rising
			if (Mathf.Abs(v - cur) < 0.002f) cur = v;
			shown[id] = cur;

			Fill(r, new Color(0.03f, 0.045f, 0.07f, 1f));
			float pad = 3f * s;
			Rect tank = new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad);
			float full = Mathf.Clamp01(cur);
			if (full > 0.001f)
			{
				bool nearFull = full > 0.94f;
				float pulse = nearFull ? 0.5f + 0.5f * Mathf.Sin(now * 7f) : 0f;
				Color deep = Color.Lerp(new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 1f), new Color(Amber.r * 0.6f, Amber.g * 0.6f, Amber.b * 0.6f, 1f), pulse);
				Color skin = Color.Lerp(new Color(Mathf.Min(1f, accent.r + 0.25f), Mathf.Min(1f, accent.g + 0.25f), Mathf.Min(1f, accent.b + 0.25f), 1f), Amber, pulse);
				Color mid = Color.Lerp(accent, Amber, pulse);
				Color body = Color.Lerp(deep, mid, 0.7f);

				// a handful of bands: each one is a rectangle whose right edge rides the wave
				int rows = Mathf.Clamp(Mathf.FloorToInt(tank.height / (3.5f * s)), 3, 6);
				float rowH = tank.height / rows;
				float edgeBase = tank.width * full;
				float amp = Mathf.Min(edgeBase, tank.width - edgeBase, 7f * s) * 0.5f + (full < 0.999f ? 1.2f * s : 0f);
				for (int i = 0; i < rows; i++)
				{
					float w = edgeBase + amp * Mathf.Sin(i * 1.7f - now * 3.2f) + 0.6f * amp * Mathf.Sin(i * 0.7f + now * 1.7f);
					w = Mathf.Clamp(w, 0f, tank.width);
					if (w <= 0f) continue;
					float y = tank.y + i * rowH;
					Fill(new Rect(tank.x, y, w, rowH + 0.5f), body);
					float skinW = Mathf.Min(w, 4f * s);
					Fill(new Rect(tank.x + w - skinW, y, skinW, rowH + 0.5f), skin);
				}
				// bubbles rising inside the liquid
				for (int b = 0; b < 3; b++)
				{
					float fx = (b * 0.37f + 0.09f) % 1f;
					float bx = tank.x + fx * Mathf.Max(0f, edgeBase - 6f * s) + 2f * s;
					float speed = 0.22f + (b % 3) * 0.09f;
					float t01 = (now * speed + b * 0.31f) % 1f;
					float by = tank.yMax - t01 * tank.height;
					float size = (2f + (b % 3)) * s;
					if (bx + size < tank.x + edgeBase - 5f * s && edgeBase > 10f * s)
						Fill(new Rect(bx, by, size, size), new Color(1f, 1f, 1f, 0.45f * (1f - t01)));
				}
			}
			// gauge ticks at 25 / 50 / 75 %
			for (int k = 1; k <= 3; k++) VLine(tank.x + tank.width * k * 0.25f, r.yMax - 5f * s, 3f * s, Dim, 1f);
			Outline(r, Dim, 1f);
			Corners(r, Ink, 5f * s, 2f);
		}

		// the window itself: ground, double frame, bracket corners, node dots on the top and bottom edge
		internal static void Window(Rect r, float s, bool collapsed)
		{
			Begin();
			if (!_paint) return;
			GUI.DrawTexture(r, T(Bg));
			Outline(r, new Color(Ink.r, Ink.g, Ink.b, 0.55f), 1f);
			if (!collapsed) Outline(new Rect(r.x + 5f * s, r.y + 5f * s, r.width - 10f * s, r.height - 10f * s), new Color(Faint.r, Faint.g, Faint.b, 0.8f), 1f);
			Corners(r, Ink, 14f * s, 2f);
			float mx = r.x + r.width / 2f;
			Dot(mx, r.y, 5f * s, Ink);
			Dot(mx, r.yMax, 5f * s, Ink);
		}

		// the resize mark: two short rules in the corner
		internal static void Grip(Rect r)
		{
			HLine(r.xMax - 12f, r.yMax - 4f, 10f, Dim, 1f);
			VLine(r.xMax - 4f, r.yMax - 12f, 10f, Dim, 1f);
			HLine(r.xMax - 8f, r.yMax - 8f, 6f, Dim, 1f);
			VLine(r.xMax - 8f, r.yMax - 8f, 6f, Dim, 1f);
		}
	}
}
