using System;
using System.Collections.Generic;

namespace UnityEngine
{
	// ============================================================================ layout engine (port of Unity's IMGUI auto-layout)
	internal partial class LEntry
	{
		public float minW, maxW, minH, maxH;
		public int stretchW, stretchH;
		public Rect rect;
		public GUIStyle style = GUIStyle.none;
		public RectOffset margin = new RectOffset();
		public bool isSpace;
		public virtual float marginLeft => margin.left;
		public virtual float marginRight => margin.right;
		public virtual float marginTop => margin.top;
		public virtual float marginBottom => margin.bottom;
		public virtual void CalcWidth() { }
		public virtual void CalcHeight() { }
		public virtual void SetHorizontal(float x, float w) { rect.x = x; rect.width = w; }
		public virtual void SetVertical(float y, float h) { rect.y = y; rect.height = h; }

		public void ApplyStyle(GUIStyle s)
		{
			style = s;
			margin = s.margin;
			stretchW = (s.fixedWidth == 0 && s.stretchWidth) ? 1 : 0;
			stretchH = (s.fixedHeight == 0 && s.stretchHeight) ? 1 : 0;
		}
		public void ApplyOptions(GUILayoutOption[] opts)
		{
			if (opts == null) return;
			foreach (var o in opts)
			{
				switch (o.kind)
				{
					case "w": minW = maxW = o.value; stretchW = 0; break;
					case "h": minH = maxH = o.value; stretchH = 0; break;
					case "minw": minW = o.value; if (maxW < minW) maxW = minW; break;
					case "maxw": maxW = o.value; if (minW > maxW) minW = maxW; stretchW = 0; break;
					case "minh": minH = o.value; if (maxH < minH) maxH = minH; break;
					case "maxh": maxH = o.value; if (minH > maxH) minH = maxH; stretchH = 0; break;
					case "ew": stretchW = (int)o.value; break;
					case "eh": stretchH = (int)o.value; break;
				}
			}
		}
	}

	internal partial class LGroup : LEntry
	{
		public bool isVertical;
		public List<LEntry> entries = new List<LEntry>();
		public int cursor;
		public float spacing;
		public bool userW, userH;
		public bool isScroll;
		public bool resetCoords;
		private float childMinW, childMaxW, childMinH, childMaxH;
		private int mL, mR, mT, mB;
		public override float marginLeft => mL;
		public override float marginRight => mR;
		public override float marginTop => mT;
		public override float marginBottom => mB;

		public override void CalcWidth()
		{
			if (entries.Count == 0) { maxW = minW = style.padding.horizontal; return; }
			int leftMost = 0, rightMost = 0; childMinW = childMaxW = 0; int stretch = 0; bool first = true;
			if (isVertical)
			{
				foreach (var i in entries)
				{
					i.CalcWidth();
					var m = i.margin; float ml = i.marginLeft, mr = i.marginRight;
					if (!i.isSpace)
					{
						if (!first) { leftMost = (int)Math.Min(ml, leftMost); rightMost = (int)Math.Min(mr, rightMost); }
						else { leftMost = (int)ml; rightMost = (int)mr; first = false; }
						childMinW = Math.Max(i.minW + ml + mr, childMinW);
						childMaxW = Math.Max(i.maxW + ml + mr, childMaxW);
					}
					else { childMinW = Math.Max(i.minW, childMinW); childMaxW = Math.Max(i.maxW, childMaxW); }
					stretch += i.stretchW;
				}
			}
			else
			{
				float lastMargin = 0;
				foreach (var i in entries)
				{
					i.CalcWidth();
					float margins;
					if (!i.isSpace)
					{
						if (!first) margins = Math.Max(lastMargin, i.marginLeft); else { margins = 0; first = false; leftMost = (int)i.marginLeft; }
						lastMargin = i.marginRight;
					}
					else margins = 0;
					childMinW += i.minW + spacing + margins; childMaxW += i.maxW + spacing + margins; stretch += i.stretchW;
				}
				childMinW -= spacing; childMaxW -= spacing;
				rightMost = (int)lastMargin;
			}
			float lp = 0, rp = 0;
			if (style != GUIStyle.none || userW) { lp = Math.Max(style.padding.left, leftMost); rp = Math.Max(style.padding.right, rightMost); }
			else { mL = leftMost; mR = rightMost; }
			minW = Math.Max(minW, childMinW + lp + rp);
			if (maxW == 0) { stretchW += stretch + 1; maxW = childMaxW + lp + rp; }
			else stretchW = 0;
			maxW = Math.Max(maxW, minW);
			if (style.fixedWidth != 0) { maxW = minW = style.fixedWidth; stretchW = 0; }
		}

		public override void SetHorizontal(float x, float width)
		{
			base.SetHorizontal(x, width);
			if (resetCoords) x = 0;
			var pad = style.padding;
			if (isVertical)
			{
				if (style != GUIStyle.none)
				{
					foreach (var i in entries)
					{
						float leftMar = Math.Max(i.marginLeft, pad.left), thisX = x + leftMar, thisW = width - Math.Max(i.marginRight, pad.right) - leftMar;
						if (i.stretchW != 0) i.SetHorizontal(thisX, thisW); else i.SetHorizontal(thisX, Mathf.Clamp(thisW, i.minW, i.maxW));
					}
				}
				else
				{
					float thisX = x - mL, thisW = width + mL + mR;
					foreach (var i in entries)
					{
						float lm = i.marginLeft, rm = i.marginRight, w = thisW - lm - rm;
						if (i.stretchW != 0) i.SetHorizontal(thisX + lm, w); else i.SetHorizontal(thisX + lm, Mathf.Clamp(w, i.minW, i.maxW));
					}
				}
			}
			else
			{
				if (style != GUIStyle.none)
				{
					float lead = pad.left, trail = pad.right;
					if (entries.Count > 0) { lead = Math.Max(lead, entries[0].marginLeft); trail = Math.Max(trail, entries[entries.Count - 1].marginRight); }
					x += lead; width -= lead + trail;
				}
				float minMaxScale = 0;
				if (childMinW != childMaxW) minMaxScale = Mathf.Clamp((width - childMinW) / (childMaxW - childMinW), 0, 1);
				float perStretch = 0;
				if (width > childMaxW) { int st = 0; foreach (var i in entries) st += i.stretchW; if (st > 0) perStretch = (width - childMaxW) / st; }
				float lastMargin = 0; bool first = true;
				foreach (var i in entries)
				{
					float tw = Mathf.Lerp(i.minW, i.maxW, minMaxScale) + perStretch * i.stretchW;
					if (!i.isSpace)
					{
						float m = first ? 0 : Math.Max(lastMargin, i.marginLeft); first = false;
						x += m; lastMargin = i.marginRight;
					}
					i.SetHorizontal(x, tw);
					x += tw + spacing;
				}
			}
		}

		public override void CalcHeight()
		{
			if (entries.Count == 0) { maxH = minH = style.padding.vertical; return; }
			int topMost = 0, bottomMost = 0; childMinH = childMaxH = 0; int stretch = 0; bool first = true;
			if (!isVertical)
			{
				foreach (var i in entries)
				{
					i.CalcHeight();
					float mt = i.marginTop, mb = i.marginBottom;
					if (!i.isSpace)
					{
						if (!first) { topMost = (int)Math.Min(mt, topMost); bottomMost = (int)Math.Min(mb, bottomMost); }
						else { topMost = (int)mt; bottomMost = (int)mb; first = false; }
						childMinH = Math.Max(i.minH + mt + mb, childMinH);
						childMaxH = Math.Max(i.maxH + mt + mb, childMaxH);
					}
					else { childMinH = Math.Max(i.minH, childMinH); childMaxH = Math.Max(i.maxH, childMaxH); }
					stretch += i.stretchH;
				}
			}
			else
			{
				float lastMargin = 0;
				foreach (var i in entries)
				{
					i.CalcHeight();
					float margins;
					if (!i.isSpace)
					{
						if (!first) margins = Math.Max(lastMargin, i.marginTop); else { margins = 0; first = false; topMost = (int)i.marginTop; }
						lastMargin = i.marginBottom;
					}
					else margins = 0;
					childMinH += i.minH + spacing + margins; childMaxH += i.maxH + spacing + margins; stretch += i.stretchH;
				}
				childMinH -= spacing; childMaxH -= spacing;
				bottomMost = (int)lastMargin;
			}
			float tp = 0, bp = 0;
			if (style != GUIStyle.none || userH) { tp = Math.Max(style.padding.top, topMost); bp = Math.Max(style.padding.bottom, bottomMost); }
			else { mT = topMost; mB = bottomMost; }
			minH = Math.Max(minH, childMinH + tp + bp);
			if (maxH == 0) { stretchH += stretch + 1; maxH = childMaxH + tp + bp; }
			else stretchH = 0;
			maxH = Math.Max(maxH, minH);
			if (style.fixedHeight != 0) { maxH = minH = style.fixedHeight; stretchH = 0; }
		}

		public override void SetVertical(float y, float height)
		{
			base.SetVertical(y, height);
			if (entries.Count == 0) return;
			if (resetCoords) y = 0;
			var pad = style.padding;
			if (!isVertical)
			{
				if (style != GUIStyle.none)
				{
					foreach (var i in entries)
					{
						float topMar = Math.Max(i.marginTop, pad.top), thisY = y + topMar, thisH = height - Math.Max(i.marginBottom, pad.bottom) - topMar;
						if (i.stretchH != 0) i.SetVertical(thisY, thisH); else i.SetVertical(thisY, Mathf.Clamp(thisH, i.minH, i.maxH));
					}
				}
				else
				{
					float thisY = y - mT, thisH = height + mT + mB;
					foreach (var i in entries)
					{
						float tm = i.marginTop, bm = i.marginBottom, h = thisH - tm - bm;
						if (i.stretchH != 0) i.SetVertical(thisY + tm, h); else i.SetVertical(thisY + tm, Mathf.Clamp(h, i.minH, i.maxH));
					}
				}
			}
			else
			{
				if (style != GUIStyle.none)
				{
					float lead = pad.top, trail = pad.bottom;
					lead = Math.Max(lead, entries[0].marginTop); trail = Math.Max(trail, entries[entries.Count - 1].marginBottom);
					y += lead; height -= lead + trail;
				}
				float minMaxScale = 0;
				if (childMinH != childMaxH) minMaxScale = Mathf.Clamp((height - childMinH) / (childMaxH - childMinH), 0, 1);
				float perStretch = 0;
				if (height > childMaxH) { int st = 0; foreach (var i in entries) st += i.stretchH; if (st > 0) perStretch = (height - childMaxH) / st; }
				float lastMargin = 0; bool first = true;
				foreach (var i in entries)
				{
					float th = Mathf.Lerp(i.minH, i.maxH, minMaxScale) + perStretch * i.stretchH;
					if (!i.isSpace)
					{
						float m = first ? 0 : Math.Max(lastMargin, i.marginTop); first = false;
						y += m; lastMargin = i.marginBottom;
					}
					i.SetVertical(y, th);
					y += th + spacing;
				}
			}
		}
	}

	internal partial class LContent : LEntry
	{
		public GUIContent content;
		public bool wrapSized;
		public override void CalcWidth()
		{
			if (wrapSized)
			{
				style.CalcMinMaxWidth(content, out float mn, out float mx);
				float uMin = minW, uMax = maxW;
				minW = uMin != 0 ? uMin : mn; maxW = uMax != 0 ? uMax : mx;
				if (maxW < minW) maxW = minW;
				if (uMin != 0 && uMax == 0) maxW = Math.Max(mx, minW);
			}
		}
		public override void CalcHeight()
		{
			if (wrapSized) { float h = style.CalcHeight(content, rect.width); minH = maxH = h; }
		}
	}

	internal partial class LSpace : LEntry { public LSpace() { isSpace = true; } }

	// ============================================================================ GUI / GUILayout statics
	public static partial class GUILayoutUtility { }

	internal partial class LayoutCtx
	{
		public LGroup root;
		public Stack<LGroup> stack = new Stack<LGroup>();
		public Rect area;
		public Vector2 origin;
	}

	public static partial class GUILayout
	{
		public static GUILayoutOption Width(float w) => new GUILayoutOption { kind = "w", value = w };
		public static GUILayoutOption Height(float h) => new GUILayoutOption { kind = "h", value = h };
		public static GUILayoutOption MinWidth(float w) => new GUILayoutOption { kind = "minw", value = w };
		public static GUILayoutOption MaxWidth(float w) => new GUILayoutOption { kind = "maxw", value = w };
		public static GUILayoutOption MinHeight(float h) => new GUILayoutOption { kind = "minh", value = h };
		public static GUILayoutOption MaxHeight(float h) => new GUILayoutOption { kind = "maxh", value = h };
		public static GUILayoutOption ExpandWidth(bool b) => new GUILayoutOption { kind = "ew", value = b ? 1 : 0 };
		public static GUILayoutOption ExpandHeight(bool b) => new GUILayoutOption { kind = "eh", value = b ? 1 : 0 };

		private static LEntry NextOrAdd(Func<LEntry> make, out bool layoutPass)
		{
			var ctx = Gui.Ctx;
			layoutPass = Event.current.type == EventType.Layout;
			if (ctx == null) { ctx = Gui.EnsureScreenCtx(); }
			var g = ctx.stack.Peek();
			if (layoutPass) { var e = make(); g.entries.Add(e); return e; }
			if (g.cursor < g.entries.Count) return g.entries[g.cursor++];
			return null;
		}

		private static Rect Place(GUIContent content, GUIStyle style, GUILayoutOption[] opts)
		{
			var e = NextOrAdd(() =>
			{
				var c = new LContent { content = content };
				c.ApplyStyle(style);
				if (style.isHeightDependantOnWidth) { c.wrapSized = true; }
				else { var sz = style.CalcSize(content); c.minW = c.maxW = sz.x; c.minH = c.maxH = sz.y; }
				c.ApplyOptions(opts);
				return c;
			}, out bool layout);
			return e == null ? new Rect(0, 0, 0, 0) : e.rect;
		}

		public static void Label(string t, params GUILayoutOption[] o) => Label(new GUIContent(t), GUI.skin.label, o);
		public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) => Label(new GUIContent(t), s, o);
		public static void Label(GUIContent c, params GUILayoutOption[] o) => Label(c, GUI.skin.label, o);
		public static void Label(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { var r = Place(c, s, o); GUI.Label(r, c, s); }
		public static void Box(string t, params GUILayoutOption[] o) => Box(new GUIContent(t), GUI.skin.box, o);
		public static void Box(string t, GUIStyle s, params GUILayoutOption[] o) => Box(new GUIContent(t), s, o);
		public static void Box(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { var r = Place(c, s, o); GUI.Box(r, c, s); }
		public static bool Button(string t, params GUILayoutOption[] o) => Button(new GUIContent(t), GUI.skin.button, o);
		public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) => Button(new GUIContent(t), s, o);
		public static bool Button(GUIContent c, GUIStyle s, params GUILayoutOption[] o) { var r = Place(c, s, o); return GUI.Button(r, c, s); }
		public static bool Toggle(bool v, string t, params GUILayoutOption[] o) => Toggle(v, new GUIContent(t), GUI.skin.toggle, o);
		public static bool Toggle(bool v, string t, GUIStyle s, params GUILayoutOption[] o) => Toggle(v, new GUIContent(t), s, o);
		public static bool Toggle(bool v, GUIContent c, GUIStyle s, params GUILayoutOption[] o) { var r = Place(c, s, o); return GUI.Toggle(r, v, c, s); }
		public static string TextField(string t, params GUILayoutOption[] o) => TextField(t, GUI.skin.textField, o);
		public static string TextField(string t, GUIStyle s, params GUILayoutOption[] o) { var r = Place(new GUIContent(t), s, o); return GUI.TextField(r, t, s); }
		public static string TextArea(string t, params GUILayoutOption[] o) => TextArea(t, GUI.skin.textArea, o);
		public static string TextArea(string t, GUIStyle s, params GUILayoutOption[] o) { var r = Place(new GUIContent(t), s, o); return GUI.TextField(r, t, s); }
		public static float HorizontalSlider(float v, float min, float max, params GUILayoutOption[] o)
		{
			var s = GUI.skin.horizontalSlider;
			var e = NextOrAdd(() => { var c = new LEntry(); c.ApplyStyle(s); c.minW = 0; c.maxW = 100000; c.minH = c.maxH = s.fixedHeight; c.stretchW = 1; c.ApplyOptions(o); if (c.minW == 0) c.minW = 20; return c; }, out bool l);
			if (e != null) Gui.DrawStyled(e.rect, "", s, false, false, false);
			return v;
		}
		public static void Space(float px)
		{
			NextOrAdd(() =>
			{
				var sp = new LSpace(); var g = Gui.Ctx.stack.Peek();
				if (g.isVertical) { sp.minH = sp.maxH = px; sp.minW = sp.maxW = 0; } else { sp.minW = sp.maxW = px; sp.minH = sp.maxH = 0; }
				return sp;
			}, out bool l);
		}
		public static void FlexibleSpace()
		{
			NextOrAdd(() =>
			{
				var sp = new LSpace(); var g = Gui.Ctx.stack.Peek();
				if (g.isVertical) { sp.stretchH = 1; sp.maxH = 100000; } else { sp.stretchW = 1; sp.maxW = 100000; }
				return sp;
			}, out bool l);
		}

		private static void BeginGroup(bool vertical, GUIStyle style, GUILayoutOption[] opts, bool scroll = false)
		{
			var ctx = Gui.Ctx ?? Gui.EnsureScreenCtx();
			var e = NextOrAdd(() =>
			{
				var g = scroll ? new LScroll { isVertical = vertical, isScroll = true } : new LGroup { isVertical = vertical, isScroll = scroll };
				g.ApplyStyle(style);
				g.margin = style.margin;
				g.ApplyOptions(opts);
				if (opts != null) foreach (var o in opts) { if (o.kind == "w" || o.kind == "minw" || o.kind == "maxw") g.userW = true; if (o.kind == "h" || o.kind == "minh" || o.kind == "maxh") g.userH = true; }
				return g;
			}, out bool l);
			var grp = (LGroup)e;
			grp.cursor = 0;
			ctx.stack.Push(grp);
		}
		private static void EndGroup() { var ctx = Gui.Ctx; if (ctx != null && ctx.stack.Count > 1) ctx.stack.Pop(); }

		public static void BeginHorizontal(params GUILayoutOption[] o) => BeginGroup(false, GUIStyle.none, o);
		public static void BeginHorizontal(GUIStyle s, params GUILayoutOption[] o) => BeginGroup(false, s, o);
		public static void EndHorizontal() => EndGroup();
		public static void BeginVertical(params GUILayoutOption[] o) => BeginGroup(true, GUIStyle.none, o);
		public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) => BeginGroup(true, s, o);
		public static void EndVertical() => EndGroup();

		public static void BeginArea(Rect r) => BeginArea(r, GUIStyle.none);
		public static void BeginArea(Rect r, GUIStyle s) { Gui.BeginArea(r, s); }
		public static void EndArea() { Gui.EndArea(); }

		public static Vector2 BeginScrollView(Vector2 pos, params GUILayoutOption[] o) => BeginScrollView(pos, false, false, o);
		public static Vector2 BeginScrollView(Vector2 pos, bool alwaysH, bool alwaysV, params GUILayoutOption[] o)
		{
			BeginGroup(true, GUIStyle.none, o, true);
			var g = Gui.Ctx.stack.Peek();
			g.resetCoords = true;
			Gui.PushScroll(Event.current.type == EventType.Repaint ? g.rect : new Rect(), g);
			return pos;
		}
		public static Vector2 BeginScrollView(Vector2 pos, bool alwaysH, bool alwaysV, GUIStyle h, GUIStyle v, params GUILayoutOption[] o) => BeginScrollView(pos, alwaysH, alwaysV, o);
		public static void EndScrollView() { Gui.PopScroll(); EndGroup(); }
	}
}
