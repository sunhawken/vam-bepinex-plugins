using System;
using System.Collections.Generic;

namespace UnityEngine
{
	public partial class RectOffset
	{
		public int left { get; set; }
		public int right { get; set; }
		public int top { get; set; }
		public int bottom { get; set; }
		public RectOffset() { }
		public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; }
		public int horizontal => left + right;
		public int vertical => top + bottom;
		public RectOffset Clone() => new RectOffset(left, right, top, bottom);
	}

	public partial class GUIStyleState
	{
		public Color textColor { get; set; } = new Color(0.9f, 0.9f, 0.9f, 1f);
		public Texture2D background { get; set; }
		public GUIStyleState Clone() => (GUIStyleState)MemberwiseClone();
	}

	public partial class GUIContent
	{
		public string text { get; set; } = "";
		public Texture image { get; set; }
		public string tooltip { get; set; } = "";
		public GUIContent() { }
		public GUIContent(string t) { text = t ?? ""; }
		public GUIContent(string t, string tip) { text = t ?? ""; tooltip = tip; }
		public static GUIContent none { get; set; } = new GUIContent("");
		public static implicit operator GUIContent(string s) => new GUIContent(s);
	}

	public partial class GUIStyle
	{
		public string name { get; set; } = "";
		public GUIStyleState normal { get; set; } = new GUIStyleState();
		public GUIStyleState hover { get; set; } = new GUIStyleState();
		public GUIStyleState active { get; set; } = new GUIStyleState();
		public GUIStyleState focused { get; set; } = new GUIStyleState();
		public GUIStyleState onNormal { get; set; } = new GUIStyleState();
		public GUIStyleState onHover { get; set; } = new GUIStyleState();
		public GUIStyleState onActive { get; set; } = new GUIStyleState();
		public GUIStyleState onFocused { get; set; } = new GUIStyleState();
		public RectOffset border { get; set; } = new RectOffset();
		public RectOffset margin { get; set; } = new RectOffset();
		public RectOffset padding { get; set; } = new RectOffset();
		public RectOffset overflow { get; set; } = new RectOffset();
		public Font font { get; set; }
		public int fontSize { get; set; }
		public FontStyle fontStyle { get; set; }
		public TextAnchor alignment { get; set; } = TextAnchor.UpperLeft;
		public bool wordWrap { get; set; }
		public bool richText { get; set; } = true;
		public bool stretchWidth { get; set; } = true;
		public bool stretchHeight { get; set; }
		public TextClipping clipping { get; set; } = TextClipping.Overflow;
		public ImagePosition imagePosition { get; set; } = ImagePosition.ImageLeft;
		public Vector2 contentOffset { get; set; }
		public float fixedWidth { get; set; }
		public float fixedHeight { get; set; }
		internal string kind = "label";

		public GUIStyle() { }
		public GUIStyle(GUIStyle o)
		{
			name = o.name; normal = o.normal.Clone(); hover = o.hover.Clone(); active = o.active.Clone(); focused = o.focused.Clone();
			onNormal = o.onNormal.Clone(); onHover = o.onHover.Clone(); onActive = o.onActive.Clone(); onFocused = o.onFocused.Clone();
			border = o.border.Clone(); margin = o.margin.Clone(); padding = o.padding.Clone(); overflow = o.overflow.Clone();
			font = o.font; fontSize = o.fontSize; fontStyle = o.fontStyle; alignment = o.alignment; wordWrap = o.wordWrap; richText = o.richText;
			stretchWidth = o.stretchWidth; stretchHeight = o.stretchHeight; clipping = o.clipping; imagePosition = o.imagePosition;
			contentOffset = o.contentOffset; fixedWidth = o.fixedWidth; fixedHeight = o.fixedHeight; kind = o.kind;
		}
		public static GUIStyle none { get; set; } = new GUIStyle { name = "none", kind = "none", stretchWidth = true };
		public bool isHeightDependantOnWidth => fixedWidth == 0 && wordWrap && imagePosition != ImagePosition.ImageOnly;
		public float lineHeight => TextMetrics.LineHeight(this);

		public Vector2 CalcSize(GUIContent c)
		{
			if (fixedWidth != 0 && fixedHeight != 0) return new Vector2(fixedWidth, fixedHeight);
			var s = TextMetrics.Measure(this, c.text, 0);
			float w = s.x + padding.horizontal, h = s.y + padding.vertical;
			if (fixedWidth != 0) w = fixedWidth;
			if (fixedHeight != 0) h = fixedHeight;
			return new Vector2(w, h);
		}
		public Vector2 CalcSizeWithConstraints(GUIContent c, Vector2 constraints) => CalcSize(c);
		public float CalcHeight(GUIContent c, float width)
		{
			if (fixedHeight != 0) return fixedHeight;
			var s = TextMetrics.Measure(this, c.text, wordWrap ? width - padding.horizontal : 0);
			return s.y + padding.vertical;
		}
		public void CalcMinMaxWidth(GUIContent c, out float minWidth, out float maxWidth)
		{
			maxWidth = TextMetrics.Measure(this, c.text, 0).x + padding.horizontal;
			minWidth = TextMetrics.MinWordWidth(this, c.text) + padding.horizontal;
		}
		public void Draw(Rect r, GUIContent c, bool isHover, bool isActive, bool on, bool focus) { Gui.DrawStyled(r, c.text, this, isHover, isActive, on); }
		public void Draw(Rect r, string c, bool isHover, bool isActive, bool on, bool focus) { Gui.DrawStyled(r, c, this, isHover, isActive, on); }
	}

	public partial class GUISkin : ScriptableObjectLike
	{
		public GUIStyle label { get; set; }
		public GUIStyle button { get; set; }
		public GUIStyle box { get; set; }
		public GUIStyle toggle { get; set; }
		public GUIStyle textField { get; set; }
		public GUIStyle textArea { get; set; }
		public GUIStyle window { get; set; }
		public GUIStyle horizontalSlider { get; set; }
		public GUIStyle horizontalSliderThumb { get; set; }
		public GUIStyle verticalSlider { get; set; }
		public GUIStyle verticalSliderThumb { get; set; }
		public GUIStyle scrollView { get; set; }
		public GUIStyle horizontalScrollbar { get; set; }
		public GUIStyle verticalScrollbar { get; set; }
		public Font font { get; set; }
		public GUISkin()
		{
			RectOffset R(int l, int r, int t, int b) => new RectOffset(l, r, t, b);
			label = new GUIStyle { name = "label", kind = "label", margin = R(4, 4, 2, 2), padding = R(3, 3, 1, 2), wordWrap = false };
			button = new GUIStyle { name = "button", kind = "button", margin = R(4, 4, 4, 4), padding = R(6, 6, 3, 3), border = R(6, 6, 6, 6), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
			box = new GUIStyle { name = "box", kind = "box", margin = R(4, 4, 4, 4), padding = R(3, 3, 3, 3), border = R(6, 6, 6, 6), alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
			toggle = new GUIStyle { name = "toggle", kind = "toggle", margin = R(4, 4, 4, 4), padding = R(15, 0, 3, 0), alignment = TextAnchor.UpperLeft };
			textField = new GUIStyle { name = "textfield", kind = "field", margin = R(4, 4, 4, 4), padding = R(3, 3, 3, 3), border = R(3, 3, 3, 3), clipping = TextClipping.Clip };
			textArea = new GUIStyle { name = "textarea", kind = "field", margin = R(4, 4, 4, 4), padding = R(3, 3, 3, 3), border = R(3, 3, 3, 3), wordWrap = true, clipping = TextClipping.Clip };
			window = new GUIStyle { name = "window", kind = "window", margin = R(0, 0, 0, 0), padding = R(10, 10, 20, 10), border = R(8, 8, 18, 8), alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
			horizontalSlider = new GUIStyle { name = "horizontalslider", kind = "slider", margin = R(4, 4, 4, 4), fixedHeight = 12 };
			horizontalSliderThumb = new GUIStyle { name = "horizontalsliderthumb", kind = "thumb", fixedWidth = 12, fixedHeight = 12 };
			verticalSlider = new GUIStyle { name = "verticalslider", kind = "slider", margin = R(4, 4, 4, 4), fixedWidth = 12, stretchWidth = false, stretchHeight = true };
			verticalSliderThumb = new GUIStyle { name = "verticalsliderthumb", kind = "thumb", fixedWidth = 12, fixedHeight = 12 };
			scrollView = new GUIStyle { name = "scrollview", kind = "none" };
			horizontalScrollbar = new GUIStyle { name = "horizontalscrollbar", kind = "scroll", fixedHeight = 15, margin = R(4, 4, 1, 4) };
			verticalScrollbar = new GUIStyle { name = "verticalscrollbar", kind = "scroll", fixedWidth = 15, stretchWidth = false, stretchHeight = true, margin = R(1, 4, 4, 4) };
		}
	}
	public partial class ScriptableObjectLike : Object { }

	public partial class Event
	{
		public EventType type { get; set; } = EventType.Repaint;
		public Vector2 mousePosition { get; set; }
		public Vector2 delta { get; set; }
		public int button { get; set; }
		public KeyCode keyCode { get; set; }
		public bool isKey => type == EventType.KeyDown || type == EventType.KeyUp;
		public bool isMouse => type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag || type == EventType.MouseMove;
		public bool shift { get; set; }
		public bool control { get; set; }
		public bool alt { get; set; }
		public bool command { get; set; }
		public char character { get; set; }
		public static Event current { get; set; } = new Event();
		public EventType GetTypeForControl(int id) => type;
		public void Use() { type = EventType.Used; }
	}

	public partial class GUILayoutOption { internal string kind; internal float value; }

	// ------------------------------------------------------------------ text metrics (Arial via GDI+)
	public static partial class TextMetrics
	{
		public static float DefaultPx = 13f;
		private static System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(new System.Drawing.Bitmap(1, 1));
		private static Dictionary<string, System.Drawing.Font> fonts = new Dictionary<string, System.Drawing.Font>();
		private static System.Drawing.StringFormat fmt = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic) { FormatFlags = System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
		static TextMetrics() { g.PageUnit = System.Drawing.GraphicsUnit.Pixel; }

		public static float Px(GUIStyle s) => s.fontSize > 0 ? s.fontSize : DefaultPx;
		public static System.Drawing.Font GetFont(GUIStyle s)
		{
			float px = Px(s);
			var fs = (s.fontStyle == FontStyle.Bold || s.fontStyle == FontStyle.BoldAndItalic) ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
			string key = px + "/" + fs;
			if (!fonts.TryGetValue(key, out var f)) fonts[key] = f = new System.Drawing.Font("Arial", px, fs, System.Drawing.GraphicsUnit.Pixel);
			return f;
		}
		public static float LineHeight(GUIStyle s) => (float)Math.Ceiling(GetFont(s).GetHeight(g) * 1.0);
		private static string Strip(GUIStyle s, string t)
		{
			if (t == null) return "";
			if (s.richText && t.IndexOf('<') >= 0) t = System.Text.RegularExpressions.Regex.Replace(t, "</?(b|i|color[^>]*|size[^>]*)>", "");
			return t;
		}
		// width <= 0: no wrapping. Returns text size without padding.
		public static Vector2 Measure(GUIStyle s, string text, float wrapWidth)
		{
			text = Strip(s, text);
			if (text.Length == 0) return new Vector2(0, LineHeight(s));
			var f = GetFont(s);
			if (wrapWidth > 0)
			{
				var sz = g.MeasureString(text, f, (int)Math.Max(1, wrapWidth), new System.Drawing.StringFormat(fmt) { FormatFlags = fmt.FormatFlags & ~System.Drawing.StringFormatFlags.NoWrap });
				return new Vector2((float)Math.Ceiling(sz.Width), (float)Math.Ceiling(sz.Height));
			}
			float w = 0; int lines = 0;
			foreach (var line in text.Split('\n')) { lines++; w = Math.Max(w, g.MeasureString(line, f, 100000, fmt).Width); }
			return new Vector2((float)Math.Ceiling(w), lines * LineHeight(s));
		}
		public static float MinWordWidth(GUIStyle s, string text)
		{
			float m = 0;
			foreach (var w in Strip(s, text).Split(' ', '\n')) m = Math.Max(m, Measure(s, w, 0).x);
			return m;
		}
	}
}
