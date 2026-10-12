using System;
using System.Collections.Generic;
using System.Drawing;
using SDColor = System.Drawing.Color;

namespace UnityEngine
{
	public enum HorizontalWrapMode { Wrap = 0, Overflow = 1 }
	public enum VerticalWrapMode { Truncate = 0, Overflow = 1 }
	public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

	public partial class Canvas : Behaviour
	{
		public RenderMode renderMode { get; set; }
		public bool overrideSorting { get; set; }
		public int sortingOrder { get; set; }
		public float scaleFactor { get; set; } = 1f;
		public bool isRootCanvas => true;
	}

	public partial class RectTransform : Transform
	{
		public Vector2 anchorMin { get; set; } = new Vector2(0.5f, 0.5f);
		public Vector2 anchorMax { get; set; } = new Vector2(0.5f, 0.5f);
		public Vector2 pivot { get; set; } = new Vector2(0.5f, 0.5f);
		public Vector2 sizeDelta { get; set; }
		public Vector2 anchoredPosition { get; set; }
		public Vector2 offsetMin
		{
			get => anchoredPosition - new Vector2(sizeDelta.x * pivot.x, sizeDelta.y * pivot.y);
			set { var cur = offsetMin; var off = value - cur; sizeDelta = sizeDelta - off; anchoredPosition = anchoredPosition + new Vector2(off.x * (1 - pivot.x), off.y * (1 - pivot.y)); }
		}
		public Vector2 offsetMax
		{
			get => anchoredPosition + new Vector2(sizeDelta.x * (1 - pivot.x), sizeDelta.y * (1 - pivot.y));
			set { var cur = offsetMax; var off = value - cur; sizeDelta = sizeDelta + off; anchoredPosition = anchoredPosition + new Vector2(off.x * pivot.x, off.y * pivot.y); }
		}
		public Rect rect
		{
			get { var w = WorldRect(); return new Rect(-pivot.x * w.width, -pivot.y * w.height, w.width, w.height); }
		}
		internal Rect CanvasRect()
		{
			var c = FindCanvas();
			float s = c != null ? c.scaleFactor : 1f;
			return new Rect(0, 0, Screen.width / s, Screen.height / s);
		}
		internal Canvas FindCanvas() { for (Transform t = this; t != null; t = t.parent) { var c = t.gameObject.GetComponent<Canvas>(); if (c != null) return c; } return null; }
		// Rect in canvas space (y up, origin bottom-left).
		internal Rect WorldRect()
		{
			if (parent == null) return CanvasRect();
			Rect p = parent is RectTransform pr ? pr.WorldRect() : CanvasRect();
			float aw = (anchorMax.x - anchorMin.x) * p.width, ah = (anchorMax.y - anchorMin.y) * p.height;
			float w = aw + sizeDelta.x, h = ah + sizeDelta.y;
			float cx = p.x + (anchorMin.x + (anchorMax.x - anchorMin.x) * pivot.x) * p.width + anchoredPosition.x;
			float cy = p.y + (anchorMin.y + (anchorMax.y - anchorMin.y) * pivot.y) * p.height + anchoredPosition.y;
			return new Rect(cx - pivot.x * w, cy - pivot.y * h, w, h);
		}
	}
}

namespace UnityEngine.EventSystems
{
	public partial class UIBehaviour : MonoBehaviour { }
}

namespace UnityEngine.UI
{
	public partial class Graphic : UnityEngine.EventSystems.UIBehaviour
	{
		public Color color { get; set; } = Color.white;
		public bool raycastTarget { get; set; } = true;
		public RectTransform rectTransform => (RectTransform)transform;
	}
	public partial class MaskableGraphic : Graphic { }
	public partial class Image : MaskableGraphic { }
	public partial class Text : MaskableGraphic
	{
		public Font font { get; set; }
		public int fontSize { get; set; } = 14;
		public FontStyle fontStyle { get; set; }
		public TextAnchor alignment { get; set; } = TextAnchor.UpperLeft;
		public string text { get; set; } = "";
		public bool resizeTextForBestFit { get; set; }
		public int resizeTextMinSize { get; set; } = 10;
		public int resizeTextMaxSize { get; set; } = 40;
		public HorizontalWrapMode horizontalOverflow { get; set; }
		public VerticalWrapMode verticalOverflow { get; set; }
		public bool supportRichText { get; set; } = true;
		public float lineSpacing { get; set; } = 1f;
	}
	public partial class BaseMeshEffect : UnityEngine.EventSystems.UIBehaviour { }
	public partial class Shadow : BaseMeshEffect { public Color effectColor { get; set; } = new Color(0, 0, 0, 0.5f); public Vector2 effectDistance { get; set; } = new Vector2(1, -1); }
	public partial class Outline : Shadow { }
	public partial class RectMask2D : UnityEngine.EventSystems.UIBehaviour { }

	// -------------------------------------------------------------------- renderer
	internal static class UguiRender
	{
		public static void Draw(Bitmap bmp, List<Issue> issues)
		{
			var canvases = new List<GameObject>();
			foreach (var go in Sim.AllObjects) { var c = go.GetComponent<Canvas>(); if (c != null && go.activeInHierarchy && go.transform.parent == null) canvases.Add(go); }
			canvases.Sort((a, b) => a.GetComponent<Canvas>().sortingOrder.CompareTo(b.GetComponent<Canvas>().sortingOrder));
			using (var g = System.Drawing.Graphics.FromImage(bmp))
			{
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
				foreach (var cgo in canvases)
				{
					var cv = cgo.GetComponent<Canvas>();
					float s = cv.scaleFactor;
					DrawNode(g, cgo.transform, s, bmp.Height, new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f), issues, 0);
				}
			}
		}

		private static void DrawNode(System.Drawing.Graphics g, Transform t, float s, int screenH, RectangleF clip, List<Issue> issues, int depth)
		{
			var go = t.gameObject;
			if (!go.activeSelf) return;
			var rt = t as RectTransform;
			RectangleF screen = new RectangleF();
			if (rt != null)
			{
				Rect w = rt.WorldRect();
				screen = new RectangleF(w.x * s, screenH - (w.y + w.height) * s, w.width * s, w.height * s);
			}
			var img = go.GetComponent<Image>();
			if (img != null && rt != null && img.enabled)
			{
				var c = img.color;
				foreach (var comp in go.comps)
				{
					var cp = comp.GetType().GetProperty("colors");
					if (cp == null || comp.GetType().GetProperty("targetGraphic") == null) continue;
					var cb = cp.GetValue(comp);
					var nc = cb?.GetType().GetProperty("normalColor")?.GetValue(cb);
					if (nc is Color ncc && (ncc.r != 0 || ncc.g != 0 || ncc.b != 0 || ncc.a != 0)) c = c * ncc;
				}
				g.SetClip(RectangleF.Intersect(clip, new RectangleF(0, 0, 100000, 100000)));
				// Image.Type.Filled / Horizontal: only fillAmount of the width is drawn (the HUD bars)
				var drawRect = screen;
				try
				{
					var tp = img.GetType().GetProperty("type"); var fa = img.GetType().GetProperty("fillAmount"); var fo = img.GetType().GetProperty("fillOrigin");
					if (tp != null && fa != null && tp.GetValue(img) != null && tp.GetValue(img).ToString() == "Filled")
					{
						float amt = Math.Max(0f, Math.Min(1f, Convert.ToSingle(fa.GetValue(img))));
						int origin = fo == null ? 0 : Convert.ToInt32(fo.GetValue(img));
						float fw = screen.Width * amt;
						drawRect = origin == 0 ? new RectangleF(screen.X, screen.Y, fw, screen.Height) : new RectangleF(screen.Right - fw, screen.Y, fw, screen.Height);
					}
				}
				catch { }
				using (var b = new SolidBrush(SDColor.FromArgb((int)(Math.Min(1f, c.a * 1f) * 255), (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255)))) g.FillRectangle(b, drawRect);
				var ol = go.GetComponent<Outline>();
				if (ol != null && ol.enabled) using (var p = new Pen(SDColor.FromArgb((int)(ol.effectColor.a * 255), (int)(ol.effectColor.r * 255), (int)(ol.effectColor.g * 255), (int)(ol.effectColor.b * 255)))) g.DrawRectangle(p, screen.X, screen.Y, screen.Width - 1, screen.Height - 1);
				g.ResetClip();
			}
			var txt = go.GetComponent<Text>();
			if (txt != null && rt != null && txt.enabled && !string.IsNullOrEmpty(txt.text))
				DrawText(g, txt, screen, s, clip, issues);
			RectangleF childClip = clip;
			if (go.GetComponent<RectMask2D>() != null && rt != null) childClip = RectangleF.Intersect(clip, screen);
			foreach (var ch in t.children.ToArray()) DrawNode(g, ch, s, screenH, childClip, issues, depth + 1);
		}

		// Unity's rich text: <b> <i> <size=n> <color=#rrggbb> ... are formatting, not characters
		private static string StripRich(string s)
		{
			if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
			return System.Text.RegularExpressions.Regex.Replace(s, @"</?(b|i|size|color|material|quad)(=[^>]*)?>", "");
		}

		private static void DrawText(System.Drawing.Graphics g, Text t, RectangleF screen, float s, RectangleF clip, List<Issue> issues)
		{
			string shown = t.supportRichText ? StripRich(t.text) : t.text;
			var style = new GUIStyle { fontSize = t.fontSize, fontStyle = t.fontStyle, wordWrap = t.horizontalOverflow == HorizontalWrapMode.Wrap, richText = t.supportRichText };
			float rw = screen.Width / s, rh = screen.Height / s;   // logical units: text is laid out before canvas scaling
			int size = t.fontSize;
			bool fits(int sz)
			{
				var st = new GUIStyle { fontSize = sz, fontStyle = t.fontStyle };
				var m = TextMetrics.Measure(st, shown, t.horizontalOverflow == HorizontalWrapMode.Wrap ? rw : 0);
				bool wOk = t.horizontalOverflow == HorizontalWrapMode.Wrap || m.x <= rw + 0.5f;
				bool hOk = m.y <= rh + 0.5f;
				return wOk && hOk;
			}
			if (t.resizeTextForBestFit)
			{
				size = t.resizeTextMinSize;
				for (int sz = t.resizeTextMaxSize; sz >= t.resizeTextMinSize; sz--) if (fits(sz)) { size = sz; break; }
			}
			var st2 = new GUIStyle { fontSize = size, fontStyle = t.fontStyle, wordWrap = t.horizontalOverflow == HorizontalWrapMode.Wrap, richText = t.supportRichText };
			var meas = TextMetrics.Measure(st2, shown, st2.wordWrap ? rw : 0);
			string what = null;
			if (t.horizontalOverflow == HorizontalWrapMode.Overflow && meas.x > rw + 0.5f) what = "text wider than its box: needs " + (int)Math.Ceiling(meas.x) + "px, has " + (int)Math.Floor(rw) + "px";
			else if (meas.y > rh + 0.5f && t.verticalOverflow == VerticalWrapMode.Truncate) what = "text taller than its box (lines cut): needs " + (int)Math.Ceiling(meas.y) + "px, has " + (int)Math.Floor(rh) + "px";
			if (what != null) issues.Add(new Issue { where = "uGUI " + t.gameObject.name, what = what + (t.resizeTextForBestFit ? " even at min size " + size : "") , text = t.text, screen = screen });
			g.SetClip(RectangleF.Intersect(clip, new RectangleF(0, 0, 100000, 100000)));
			using (var f = new System.Drawing.Font("Arial", Math.Max(1f, size * s), (t.fontStyle == FontStyle.Bold) ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
			using (var br = new SolidBrush(SDColor.FromArgb(255, (int)(t.color.r * 255), (int)(t.color.g * 255), (int)(t.color.b * 255))))
			{
				var fmt = new StringFormat(StringFormat.GenericTypographic);
				if (!st2.wordWrap) fmt.FormatFlags |= StringFormatFlags.NoWrap;
				int a = (int)t.alignment;
				fmt.Alignment = a % 3 == 0 ? StringAlignment.Near : a % 3 == 1 ? StringAlignment.Center : StringAlignment.Far;
				fmt.LineAlignment = a / 3 == 0 ? StringAlignment.Near : a / 3 == 1 ? StringAlignment.Center : StringAlignment.Far;
				fmt.FormatFlags |= StringFormatFlags.NoClip;
				g.SetClip(RectangleF.Intersect(clip, t.verticalOverflow == VerticalWrapMode.Truncate ? screen : new RectangleF(-1e5f, -1e5f, 2e5f, 2e5f)));
				g.DrawString(shown, f, br, screen, fmt);
			}
			g.ResetClip();
		}
	}
}

namespace UnityEngine
{
	// a picture of every uGUI canvas the scripts built (what the HUD scripts make): a size, a background, a PNG
	public static class SimSnapshot
	{
		public static List<string> Png(string path, int w, int h, int bgR = 44, int bgG = 50, int bgB = 58)
		{
			var issues = new List<Issue>();
			using (var bmp = new Bitmap(w, h))
			{
				using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(SDColor.FromArgb(bgR, bgG, bgB));
				UnityEngine.UI.UguiRender.Draw(bmp, issues);
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
				// crop to what was drawn (the HUDs are small panels pinned somewhere on a big screen)
				int x0 = w, y0 = h, x1 = -1, y1 = -1;
				var bg = SDColor.FromArgb(bgR, bgG, bgB);
				for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var c = bmp.GetPixel(x, y); if (c.R != bg.R || c.G != bg.G || c.B != bg.B) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; } }
				if (x1 < 0) bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
				else
				{
					int m = 12; x0 = Math.Max(0, x0 - m); y0 = Math.Max(0, y0 - m); x1 = Math.Min(w - 1, x1 + m); y1 = Math.Min(h - 1, y1 + m);
					using (var crop = bmp.Clone(new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1), bmp.PixelFormat)) crop.Save(path, System.Drawing.Imaging.ImageFormat.Png);
				}
			}
			var o = new List<string>();
			foreach (var i in issues) o.Add(i.where + ": " + i.what + "  \"" + i.text + "\"");
			return o;
		}
	}
}
