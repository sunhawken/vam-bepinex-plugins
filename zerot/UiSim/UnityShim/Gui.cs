using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SDColor = System.Drawing.Color;

namespace UnityEngine
{
	public partial class DrawCmd
	{
		public string kind;            // "box", "text", "check", "scrollbar"
		public RectangleF r;           // screen pixels
		public RectangleF clip;        // screen pixels
		public string text = "";
		public string styleKind = "";
		public float px; public bool bold, wrap; public TextAnchor align; public bool clipText;
		public float padL, padR, padT, padB;
		public UnityEngine.Color color = UnityEngine.Color.white;
		public bool on;
	}

	public partial class Issue
	{
		public string where, what, text; public RectangleF screen;
		public override string ToString() => "[" + where + "] " + what + (text != null ? "  \"" + (text.Length > 60 ? text.Substring(0, 57) + "..." : text) + "\"" : "");
	}

	public delegate void WindowFunction(int id);

	internal static partial class Gui
	{
		public static List<DrawCmd> Cmds = new List<DrawCmd>();
		public static List<Issue> Issues = new List<Issue>();
		public static Matrix4x4 Matrix = Matrix4x4.identity;
		public static GUISkin Skin = new GUISkin();
		public static Dictionary<string, LayoutCtx> Ctxs = new Dictionary<string, LayoutCtx>();
		public static LayoutCtx Ctx;
		public static string WindowKey = "screen";
		public static string WindowLabel = "";
		public static int AreaCounter;
		public static Stack<Vector2> OffsetStack = new Stack<Vector2>();
		public static Vector2 Offset;
		public static Stack<RectangleF> ClipStack = new Stack<RectangleF>();
		public static RectangleF Clip = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);   // in absolute (pre-matrix) coordinates
		public static bool InScroll;
		public static Stack<bool> ScrollStack = new Stack<bool>();
		public static bool Repaint => Event.current.type == EventType.Repaint;

		public static void BeginFrame(EventType t)
		{
			Event.current.type = t;
			AreaCounter = 0;
			Matrix = Matrix4x4.identity;
			Offset = Vector2.zero; OffsetStack.Clear(); ClipStack.Clear();
			Clip = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);
			Ctx = null; WindowKey = "screen";
			if (t == EventType.Repaint) { Cmds.Clear(); }
		}

		public static LayoutCtx EnsureScreenCtx()
		{
			string key = "screen";
			if (Event.current.type == EventType.Layout || !Ctxs.ContainsKey(key))
			{
				var c = new LayoutCtx { root = new LGroup { isVertical = true }, area = new Rect(0, 0, Screen.width, Screen.height) };
				c.root.ApplyStyle(GUIStyle.none);
				c.stack.Push(c.root);
				Ctxs[key] = c;
			}
			Ctx = Ctxs[key];
			if (!Repaint) return Ctx;
			ResetCursors(Ctx.root); Ctx.stack.Clear(); Ctx.stack.Push(Ctx.root);
			return Ctx;
		}

		public static void ResetCursors(LGroup g) { g.cursor = 0; foreach (var e in g.entries) if (e is LGroup c) ResetCursors(c); }

		public static void DoLayout(LayoutCtx c, float w, float h)
		{
			var g = c.root;
			for (int pass = 0; pass < 3; pass++)
			{
				g.CalcWidth(); g.SetHorizontal(0, w); g.CalcHeight(); g.SetVertical(0, h);
				if (!ScrollChanged) break;
				ScrollChanged = false;
			}
		}
		public static bool ScrollChanged;

		// --------------------------------------------------------------------------- coordinates
		public static RectangleF Abs(Rect r) => new RectangleF(r.x + Offset.x, r.y + Offset.y, r.width, r.height);
		public static RectangleF Screen_(RectangleF a) => new RectangleF(a.X * Matrix.sx + Matrix.tx, a.Y * Matrix.sy + Matrix.ty, a.Width * Matrix.sx, a.Height * Matrix.sy);

		public static void PushGroup(Rect r, bool clip)
		{
			OffsetStack.Push(Offset); ClipStack.Push(Clip);
			var a = Abs(r);
			Offset = new Vector2(a.X, a.Y);
			if (clip) Clip = RectangleF.Intersect(Clip, a);
		}
		public static void PopGroup() { Offset = OffsetStack.Pop(); Clip = ClipStack.Pop(); }

		public static void PushScroll(Rect viewport, LGroup g)
		{
			ScrollStack.Push(InScroll); InScroll = true;
			if (!Repaint) return;
			OffsetStack.Push(Offset); ClipStack.Push(Clip);
			var a = Abs(g.rect);
			Offset = new Vector2(a.X, a.Y);
			Clip = RectangleF.Intersect(Clip, a);
			if (g is LScroll s && s.needsV)
			{
				Cmds.Add(new DrawCmd { kind = "scrollbar", r = Screen_(new RectangleF(a.X + a.Width - 15, a.Y, 15, a.Height)), clip = Screen_(Clip) });
			}
		}
		public static void PopScroll()
		{
			InScroll = ScrollStack.Count > 0 ? ScrollStack.Pop() : false;
			if (!Repaint) return;
			Offset = OffsetStack.Pop(); Clip = ClipStack.Pop();
		}

		// --------------------------------------------------------------------------- windows / areas
		public static Rect RunWindow(int id, Rect r, WindowFunction f, string title, GUIStyle style)
		{
			string prevKey = WindowKey, prevLabel = WindowLabel; var prevCtx = Ctx;
			WindowKey = "win" + id; WindowLabel = "window " + id; AreaCounter = 0;
			if (Event.current.type == EventType.Layout)
			{
				var c = new LayoutCtx { root = new LGroup { isVertical = true }, area = r };
				c.root.ApplyStyle(style);
				c.root.margin = new RectOffset();
				c.stack.Push(c.root);
				Ctxs[WindowKey] = c; Ctx = c;
				try { f(id); } catch (Exception e) { Sim.Error("window " + id, e); }
				while (c.stack.Count > 1) c.stack.Pop();
				DoLayout(c, r.width, r.height);
			}
			else
			{
				if (!Ctxs.TryGetValue(WindowKey, out var c)) { Ctx = prevCtx; return r; }
				ResetCursors(c.root); c.stack.Clear(); c.stack.Push(c.root); Ctx = c;
				PushGroup(r, true);
				if (style != GUIStyle.none) DrawStyled(new Rect(0, 0, r.width, r.height), title, style, false, false, false);
				try { f(id); } catch (Exception e) { Sim.Error("window " + id, e); }
				PopGroup();
			}
			WindowKey = prevKey; WindowLabel = prevLabel; Ctx = prevCtx;
			return r;
		}

		private static Stack<LayoutCtx> areaCtx = new Stack<LayoutCtx>();
		private static Stack<string> areaKeys = new Stack<string>();
		public static void BeginArea(Rect r, GUIStyle s)
		{
			string key = WindowKey + "/area" + (AreaCounter++);
			areaCtx.Push(Ctx); areaKeys.Push(WindowKey);
			LayoutCtx c;
			if (Event.current.type == EventType.Layout)
			{
				c = new LayoutCtx { root = new LGroup { isVertical = true }, area = r };
				c.root.ApplyStyle(s); c.root.margin = new RectOffset();
				c.stack.Push(c.root);
				Ctxs[key] = c;
			}
			else
			{
				if (!Ctxs.TryGetValue(key, out c)) { c = new LayoutCtx { root = new LGroup { isVertical = true }, area = r }; c.stack.Push(c.root); }
				ResetCursors(c.root); c.stack.Clear(); c.stack.Push(c.root);
				PushGroup(r, true);
				if (s != GUIStyle.none) DrawStyled(new Rect(0, 0, r.width, r.height), "", s, false, false, false);
			}
			Ctx = c;
		}
		public static void EndArea()
		{
			var c = Ctx;
			if (Event.current.type == EventType.Layout) { while (c.stack.Count > 1) c.stack.Pop(); DoLayout(c, c.area.width, c.area.height); }
			else PopGroup();
			Ctx = areaCtx.Pop(); areaKeys.Pop();
		}

		// --------------------------------------------------------------------------- drawing + clipping checks
		public static void DrawStyled(Rect r, string text, GUIStyle s, bool hover, bool active, bool on)
		{
			if (!Repaint) return;
			var a = Abs(r);
			if (a.Width <= 0 || a.Height <= 0) return;
			text = text ?? "";
			string kind = s.kind;
			var cmd = new DrawCmd { kind = "box", r = Screen_(a), clip = Screen_(Clip), styleKind = kind, on = on };
			if (kind != "label" && kind != "none" && kind != "toggle") Cmds.Add(cmd);
			float sc = Matrix.sx;
			if (kind == "slider") return;
			float padL = s.padding.left, padR = s.padding.right, padT = s.padding.top, padB = s.padding.bottom;
			if (kind == "toggle")
			{
				Cmds.Add(new DrawCmd { kind = "check", r = Screen_(new RectangleF(a.X + 2, a.Y + 2, 12, 12)), clip = Screen_(Clip), on = on });
			}
			if (text.Length == 0) return;
			var t = new DrawCmd
			{
				kind = "text", r = Screen_(a), clip = Screen_(Clip), text = text, styleKind = kind, px = TextMetrics.Px(s) * sc, bold = s.fontStyle == FontStyle.Bold || s.fontStyle == FontStyle.BoldAndItalic,
				wrap = s.wordWrap, align = s.alignment, clipText = s.clipping == TextClipping.Clip,
				padL = padL * sc, padR = padR * sc, padT = padT * sc, padB = padB * sc, color = s.normal.textColor
			};
			Cmds.Add(t);
			CheckText(a, text, s);
		}

		public static void CheckText(RectangleF a, string text, GUIStyle s)
		{
			bool clipStyle = s.clipping == TextClipping.Clip;
			// Unity draws centred text across the padding and only cuts it at the rect edge (Clip); Overflow text just spills out.
			float availW = clipStyle ? a.Width - 2 : a.Width - s.padding.horizontal;
			if (s.kind == "toggle") availW = a.Width - s.padding.left;
			float availH = a.Height;
			var size = TextMetrics.Measure(s, text, s.wordWrap ? Math.Max(1, a.Width - s.padding.horizontal) : 0);
			string what = null;
			if (!s.wordWrap && size.x > availW + 0.5f) what = (clipStyle ? "text cut off: needs " : "text overflows its box (spills into neighbours): needs ") + (int)Math.Ceiling(size.x) + "px, has " + (int)Math.Floor(availW) + "px";
			else if (size.y > availH + 1f) what = "text taller than its box: needs " + (int)Math.Ceiling(size.y) + "px, has " + (int)Math.Floor(availH) + "px";
			if (what == null)
			{
				var cl = Clip;
				if (!InScroll && (a.Left < cl.Left - 0.5f || a.Right > cl.Right + 0.5f || a.Top < cl.Top - 0.5f || a.Bottom > cl.Bottom + 0.5f))
					what = "extends outside its window/area";
			}
			if (what != null) Issues.Add(new Issue { where = WindowLabel, what = what, text = text, screen = Screen_(a) });
		}

		// --------------------------------------------------------------------------- rendering
		public static void Render(Bitmap bmp)
		{
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.None;
				g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
				g.Clear(SDColor.FromArgb(44, 50, 58));
				UnityEngine.UI.UguiRender.Draw(bmp, Issues);
				foreach (var c in Cmds)
				{
					g.SetClip(Rectangle.Intersect(Rectangle.Round(c.clip), new Rectangle(0, 0, bmp.Width, bmp.Height)));
					switch (c.kind)
					{
						case "box":
							{
								SDColor fill = SDColor.FromArgb(230, 52, 52, 52), edge = SDColor.FromArgb(255, 105, 105, 105);
								if (c.styleKind == "button") { fill = SDColor.FromArgb(255, 80, 80, 80); edge = SDColor.FromArgb(255, 125, 125, 125); }
								if (c.styleKind == "field") { fill = SDColor.FromArgb(255, 30, 30, 30); edge = SDColor.FromArgb(255, 120, 120, 120); }
								if (c.styleKind == "window") { fill = SDColor.FromArgb(235, 55, 55, 55); }
								if (c.styleKind == "slider") { fill = SDColor.FromArgb(255, 25, 25, 25); }
								using (var b = new SolidBrush(fill)) g.FillRectangle(b, c.r);
								using (var p = new Pen(edge)) g.DrawRectangle(p, c.r.X, c.r.Y, c.r.Width - 1, c.r.Height - 1);
								break;
							}
						case "scrollbar":
							using (var b = new SolidBrush(SDColor.FromArgb(255, 70, 70, 70))) g.FillRectangle(b, c.r);
							break;
						case "check":
							using (var b = new SolidBrush(SDColor.FromArgb(255, 30, 30, 30))) g.FillRectangle(b, c.r);
							using (var p = new Pen(SDColor.FromArgb(255, 150, 150, 150))) g.DrawRectangle(p, c.r.X, c.r.Y, c.r.Width, c.r.Height);
							if (c.on) using (var b = new SolidBrush(SDColor.FromArgb(255, 235, 235, 235))) g.FillRectangle(b, c.r.X + 3, c.r.Y + 3, c.r.Width - 5, c.r.Height - 5);
							break;
						case "text":
							{
								var fs = c.bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
								using (var f = new System.Drawing.Font("Arial", Math.Max(1f, c.px), fs, GraphicsUnit.Pixel))
								using (var br = new SolidBrush(SDColor.FromArgb(255, (int)(c.color.r * 255), (int)(c.color.g * 255), (int)(c.color.b * 255))))
								{
									var inner = new RectangleF(c.r.X + c.padL, c.r.Y + c.padT, Math.Max(1, c.r.Width - c.padL - c.padR), Math.Max(1, c.r.Height - c.padT - c.padB));
									var fmt = new StringFormat(StringFormat.GenericTypographic);
									if (!c.wrap) fmt.FormatFlags |= StringFormatFlags.NoWrap;
									fmt.Alignment = ((int)c.align % 3) == 0 ? StringAlignment.Near : ((int)c.align % 3) == 1 ? StringAlignment.Center : StringAlignment.Far;
									fmt.LineAlignment = ((int)c.align / 3) == 0 ? StringAlignment.Near : ((int)c.align / 3) == 1 ? StringAlignment.Center : StringAlignment.Far;
									if (!c.clipText) fmt.FormatFlags |= StringFormatFlags.NoClip;
									if (c.styleKind == "toggle") inner = new RectangleF(inner.X, inner.Y, inner.Width, inner.Height);
									g.DrawString(c.text, f, br, inner, fmt);
								}
								break;
							}
					}
					g.ResetClip();
				}
			}
		}
	}

	internal partial class LScroll : LGroup
	{
		public bool needsV;
		private float contentH;
		public override void CalcWidth()
		{
			base.CalcWidth();
			if (minW > 20) minW = 20;
		}
		public override void SetHorizontal(float x, float width)
		{
			float w = needsV ? width - 15 : width;
			base.SetHorizontal(x, w);
			rect.width = width;
		}
		public override void CalcHeight()
		{
			base.CalcHeight();
			contentH = minH;
			minH = Math.Min(minH, 32);
		}
		public override void SetVertical(float y, float height)
		{
			bool nv = contentH > height + 0.5f;
			if (nv != needsV) { needsV = nv; Gui.ScrollChanged = true; }
			base.SetVertical(y, Math.Max(height, contentH));
			rect.height = height;
		}
	}

	public static partial class GUI
	{
		public static GUISkin skin { get => Gui.Skin; set => Gui.Skin = value; }
		public static Matrix4x4 matrix { get => Gui.Matrix; set => Gui.Matrix = value; }
		public static Color color { get; set; } = Color.white; public static Color contentColor { get; set; } = Color.white; public static Color backgroundColor { get; set; } = Color.white;
		public static bool enabled { get; set; } = true;
		public static int depth { get; set; }
		public static string tooltip { get; set; } = "";
		public delegate void WindowFunction(int id);

		public static Rect Window(int id, Rect r, WindowFunction f, string title) => Window(id, r, f, title, skin.window);
		public static Rect Window(int id, Rect r, WindowFunction f, string title, GUIStyle style) => Gui.RunWindow(id, r, i => f(i), title, style);
		public static Rect Window(int id, Rect r, WindowFunction f, GUIContent title) => Window(id, r, f, title.text, skin.window);
		public static Rect Window(int id, Rect r, WindowFunction f, GUIContent title, GUIStyle style) => Gui.RunWindow(id, r, i => f(i), title.text, style);
		public static void DragWindow(Rect r) { }
		public static void DragWindow() { }
		public static void BringWindowToFront(int id) { }
		public static void FocusWindow(int id) { }

		public static void Box(Rect r, string t) => Gui.DrawStyled(r, t, skin.box, false, false, false);
		public static void Box(Rect r, GUIContent c) => Gui.DrawStyled(r, c.text, skin.box, false, false, false);
		public static void Box(Rect r, string t, GUIStyle s) => Gui.DrawStyled(r, t, s, false, false, false);
		public static void Box(Rect r, GUIContent c, GUIStyle s) => Gui.DrawStyled(r, c.text, s, false, false, false);
		public static void Label(Rect r, string t) => Gui.DrawStyled(r, t, skin.label, false, false, false);
		public static void Label(Rect r, GUIContent c) => Gui.DrawStyled(r, c.text, skin.label, false, false, false);
		public static void Label(Rect r, string t, GUIStyle s) => Gui.DrawStyled(r, t, s, false, false, false);
		public static void Label(Rect r, GUIContent c, GUIStyle s) => Gui.DrawStyled(r, c.text, s, false, false, false);
		public static bool Button(Rect r, string t) => Button(r, new GUIContent(t), skin.button);
		public static bool Button(Rect r, GUIContent c) => Button(r, c, skin.button);
		public static bool Button(Rect r, string t, GUIStyle s) => Button(r, new GUIContent(t), s);
		public static bool Button(Rect r, GUIContent c, GUIStyle s) { Gui.DrawStyled(r, c.text, s, false, false, false); return false; }
		public static bool Toggle(Rect r, bool v, string t) => Toggle(r, v, new GUIContent(t), skin.toggle);
		public static bool Toggle(Rect r, bool v, string t, GUIStyle s) => Toggle(r, v, new GUIContent(t), s);
		public static bool Toggle(Rect r, bool v, GUIContent c, GUIStyle s) { Gui.DrawStyled(r, c.text, s, false, false, v); return v; }
		public static string TextField(Rect r, string t) => TextField(r, t, skin.textField);
		public static string TextField(Rect r, string t, GUIStyle s) { Gui.DrawStyled(r, t, s, false, false, false); return t; }
		public static string TextArea(Rect r, string t) => TextField(r, t, skin.textArea);
		public static float HorizontalSlider(Rect r, float v, float min, float max) { Gui.DrawStyled(r, "", skin.horizontalSlider, false, false, false); return v; }
		public static void DrawTexture(Rect r, Texture t) { }
		public static void SetNextControlName(string n) { }
		public static string GetNameOfFocusedControl() => "";
		public static void FocusControl(string n) { }
		public static void BeginGroup(Rect r) { if (Gui.Repaint) Gui.PushGroup(r, true); }
		public static void BeginGroup(Rect r, string t) => BeginGroup(r);
		public static void BeginGroup(Rect r, GUIStyle s) { BeginGroup(r); }
		public static void EndGroup() { if (Gui.Repaint) Gui.PopGroup(); }
		public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view) { if (Gui.Repaint) Gui.PushGroup(pos, true); return scroll; }
		public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view, bool h, bool v) => BeginScrollView(pos, scroll, view);
		public static void EndScrollView() { if (Gui.Repaint) Gui.PopGroup(); }
	}
}
