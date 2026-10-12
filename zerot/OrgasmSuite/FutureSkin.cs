using System.Collections.Generic;
using UnityEngine;

namespace ZeroT.OrgasmSuite
{
	// The window's look, after the "UI Future Pack" sheet: a navy ground, hairline white frames with corner brackets and node dots, spaced upper-case
	// section captions between thin rules, outlined square switches, a segmented loading bar, amber bracket buttons.
	// Everything is drawn from 1x1 textures and plain styles (the IMGUI default font is the only font there is), so it works at any scale.
	internal static class Fx
	{
		internal static readonly Color Bg = new Color(0.067f, 0.090f, 0.133f, 0.985f);     // #111722
		internal static readonly Color Ink = new Color(0.90f, 0.93f, 0.97f, 1f);           // line-art white
		internal static readonly Color Dim = new Color(0.52f, 0.57f, 0.66f, 1f);           // captions
		internal static readonly Color Faint = new Color(0.20f, 0.24f, 0.31f, 1f);         // inner rules, empty segments
		internal static readonly Color Amber = new Color(0.93f, 0.66f, 0.22f, 1f);         // the bracket accent of the sheet
		internal static readonly Color Lite = new Color(0.22f, 0.88f, 0.40f, 1f);          // Lite side
		internal static readonly Color Full = new Color(0.28f, 0.58f, 1.00f, 1f);          // BodyLanguage side

		private static readonly Dictionary<int, Texture2D> tex = new Dictionary<int, Texture2D>();
		private static readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();
		private static GUIStyle click;

		private static Texture2D T(Color c)
		{
			int key = ((int)(c.r * 255f) << 24) | ((int)(c.g * 255f) << 16) | ((int)(c.b * 255f) << 8) | (int)(c.a * 255f);
			Texture2D t;
			if (tex.TryGetValue(key, out t) && t != null) return t;
			t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			t.SetPixel(0, 0, c);
			t.Apply();
			t.hideFlags = HideFlags.HideAndDontSave;
			tex[key] = t;
			return t;
		}

		// dimmed while the GUI is disabled (no Person selected, suite off)
		internal static Color A(Color c) { return GUI.enabled ? c : new Color(c.r, c.g, c.b, c.a * 0.38f); }

		internal static void Fill(Rect r, Color c) { if (r.width > 0f && r.height > 0f) GUI.DrawTexture(r, T(A(c))); }
		internal static void HLine(float x, float y, float w, Color c, float th) { Fill(new Rect(x, y, w, th), c); }
		internal static void VLine(float x, float y, float h, Color c, float th) { Fill(new Rect(x, y, th, h), c); }

		internal static void Outline(Rect r, Color c, float th)
		{
			Fill(new Rect(r.x, r.y, r.width, th), c);
			Fill(new Rect(r.x, r.yMax - th, r.width, th), c);
			Fill(new Rect(r.x, r.y, th, r.height), c);
			Fill(new Rect(r.xMax - th, r.y, th, r.height), c);
		}

		// the four "L" corner brackets of the sheet's frames
		internal static void Corners(Rect r, Color c, float len, float th)
		{
			Fill(new Rect(r.x, r.y, len, th), c); Fill(new Rect(r.x, r.y, th, len), c);
			Fill(new Rect(r.xMax - len, r.y, len, th), c); Fill(new Rect(r.xMax - th, r.y, th, len), c);
			Fill(new Rect(r.x, r.yMax - th, len, th), c); Fill(new Rect(r.x, r.yMax - len, th, len), c);
			Fill(new Rect(r.xMax - len, r.yMax - th, len, th), c); Fill(new Rect(r.xMax - th, r.yMax - len, th, len), c);
		}

		internal static void Dot(float cx, float cy, float d, Color c) { Fill(new Rect(cx - d / 2f, cy - d / 2f, d, d), c); }

		// "[" and "]" brackets with the little serifs of the "[ 150 ELEMENT ]" tag
		internal static void Brackets(Rect r, Color c, float th, float serif)
		{
			VLine(r.x, r.y, r.height, c, th); HLine(r.x, r.y, serif, c, th); HLine(r.x, r.yMax - th, serif, c, th);
			VLine(r.xMax - th, r.y, r.height, c, th); HLine(r.xMax - serif, r.y, serif, c, th); HLine(r.xMax - serif, r.yMax - th, serif, c, th);
		}

		internal static GUIStyle Style(int size, TextAnchor anchor, Color c, bool bold)
		{
			string key = size + "|" + (int)anchor + "|" + c.r + c.g + c.b + "|" + bold;
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

		internal static void Text(Rect r, string text, int size, TextAnchor anchor, Color c, bool bold)
		{
			GUIStyle st = Style(size, anchor, c, bold);
			Color old = GUI.contentColor;
			GUI.contentColor = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
			GUI.Label(r, text, st);
			GUI.contentColor = old;
		}

		// a caption of the sheet: letter-spaced capitals
		internal static string Spaced(string t)
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

		internal static bool Hover(Rect r)
		{
			Event e = Event.current;
			return e != null && GUI.enabled && r.Contains(e.mousePosition);
		}

		internal static bool Hit(Rect r)
		{
			if (click == null) click = new GUIStyle();
			return GUI.Button(r, "", click);
		}

		// a caption between two thin rules ("NAVIGATION", "LOADINGS" ...)
		internal static void Section(Rect r, string caption, float s)
		{
			string t = Spaced(caption);
			float tw = 7.2f * s * t.Length + 14f * s;
			float mid = r.x + r.width / 2f;
			float y = r.y + r.height / 2f;
			Text(new Rect(mid - tw / 2f, r.y, tw, r.height), t, Mathf.Max(8, Mathf.RoundToInt(10f * s)), TextAnchor.MiddleCenter, Dim, false);
			HLine(r.x, y, mid - tw / 2f - r.x, Faint, 1f);
			HLine(mid + tw / 2f, y, r.xMax - (mid + tw / 2f), Faint, 1f);
			Dot(r.x, y, 3f * s, Dim); Dot(r.xMax, y, 3f * s, Dim);
		}

		// flat outlined button; the selected one gets corner brackets and a tint of its accent
		internal static bool Button(Rect r, string text, Color accent, bool selected, float s, bool spaced = true)
		{
			bool hover = Hover(r);
			if (selected) Fill(r, new Color(accent.r, accent.g, accent.b, 0.16f));
			else if (hover) Fill(r, new Color(1f, 1f, 1f, 0.05f));
			Outline(r, selected ? accent : (hover ? Ink : Dim), 1f);
			if (selected) Corners(r, accent, 6f * s, 2f);
			Text(r, spaced ? Spaced(text) : text.ToUpperInvariant(), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleCenter, selected ? Ink : (hover ? Ink : new Color(0.75f, 0.79f, 0.86f, 1f)), false);
			return Hit(r);
		}

		// a small square header button (S- S+ - x)
		internal static bool Mini(Rect r, string text, float s)
		{
			bool hover = Hover(r);
			if (hover) Fill(r, new Color(1f, 1f, 1f, 0.07f));
			Outline(r, hover ? Ink : Dim, 1f);
			Text(r, text, Mathf.Max(8, Mathf.RoundToInt(10f * s)), TextAnchor.MiddleCenter, hover ? Ink : new Color(0.75f, 0.79f, 0.86f, 1f), false);
			return Hit(r);
		}

		// the amber "[ LABEL ]" action
		internal static bool Bracket(Rect r, string text, float s)
		{
			bool hover = Hover(r);
			if (hover) Fill(r, new Color(Amber.r, Amber.g, Amber.b, 0.10f));
			Brackets(r, Amber, 1f * s < 1f ? 1f : Mathf.Round(1.5f * s), 5f * s);
			Text(r, Spaced(text), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleCenter, hover ? Ink : Amber, false);
			return Hit(r);
		}

		// outlined square with a solid inner square when on, in the colour of the side (green Lite / blue BodyLanguage)
		internal static bool Toggle(Rect r, string label, bool on, Color accent, float s)
		{
			bool hover = Hover(r);
			if (hover) Fill(r, new Color(1f, 1f, 1f, 0.04f));
			HLine(r.x, r.yMax - 1f, r.width, Faint, 1f);
			float b = 12f * s;
			Rect box = new Rect(r.x + 4f * s, r.y + (r.height - b) / 2f, b, b);
			Outline(box, on ? accent : Dim, 1f);
			if (on) Fill(new Rect(box.x + 3f * s, box.y + 3f * s, box.width - 6f * s, box.height - 6f * s), accent);
			Text(new Rect(box.xMax + 8f * s, r.y, r.width - b - 16f * s, r.height), Spaced(label), Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, on ? Ink : new Color(0.68f, 0.73f, 0.81f, 1f), false);
			return Hit(r);
		}

		private static int dragging;

		// the segmented loading bar of the sheet, draggable. Returns the new value 0..1, or -1 when it was not touched.
		internal static float Bar(Rect r, float v, Color accent, float s, int id)
		{
			Outline(r, Dim, 1f);
			Corners(r, Ink, 5f * s, 2f);
			int n = 24;
			float pad = 4f * s;
			float segW = (r.width - 2f * pad) / n;
			int lit = Mathf.RoundToInt(Mathf.Clamp01(v) * n);
			for (int i = 0; i < n; i++)
				Fill(new Rect(r.x + pad + i * segW + 1f, r.y + pad, segW - 2f, r.height - 2f * pad), i < lit ? accent : Faint);
			float result = -1f;
			Event e = Event.current;
			if (e != null && GUI.enabled)
			{
				if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = id; e.Use(); }
				if (dragging == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
				{
					result = Mathf.Clamp01((e.mousePosition.x - r.x - pad) / (r.width - 2f * pad));
					e.Use();
				}
				if (e.type == EventType.MouseUp && dragging == id) { dragging = 0; e.Use(); }
			}
			return result;
		}

		// a framed panel (status box): hairline frame, node dots in the corners
		internal static void Frame(Rect r, float s)
		{
			Outline(r, Faint, 1f);
			Corners(r, Dim, 5f * s, 1f);
		}

		// the window itself: ground, double frame, bracket corners, node dots on the top and bottom edge
		internal static void Window(Rect r, float s, bool collapsed)
		{
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
