using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZeroT.UiKit
{
	// Shared IMGUI chrome that reproduces the VpbRandomLook window: default skin, a GUI.Box backdrop,
	// "Title ... S- S+ - x" row at 8px margin, a "↘" resize handle, and monitor-DPI scaling.
	// All rects are window-local (they are used inside a GUI.Window function or after offsetting).
	internal static class RlChrome
	{
		internal const int Smaller = 1;
		internal const int Larger = 2;
		internal const int Collapse = 4;
		internal const int Close = 8;
		internal const int Power = 16;
		internal const float TitleRight = 150f;   // space the buttons take from the right edge (title label stops here)
		internal const float TitleH = 28f;
		internal const float Margin = 8f;
		internal const float CollapsedH = 34f;

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

		private static float cachedDpi = -1f;
		private static double nextProbe;

		internal static void ResetDpi()
		{
			cachedDpi = -1f;
		}

		// DPI of the monitor under (x, y) (screen pixels, top-left origin); cached for 5 seconds.
		internal static float Dpi(float x, float y)
		{
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
				pt.X = (int)x;
				pt.Y = (int)y;
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

		internal static void Backdrop(float w, float h)
		{
			GUI.Box(new Rect(0f, 0f, w, h), "");
		}

		// Returns a bitmask of the buttons clicked this frame.
		internal static int TitleRow(float w, string title, bool collapsed, bool active = true)
		{
			int result = 0;
			GUI.Label(new Rect(Margin, 3f, w - TitleRight, 22f), title);
			float x = w - 112f;
			if (GUI.Button(new Rect(x - 34f, 3f, 31f, 18f), active ? "On" : "Off"))
			{
				result |= Power;
			}
			if (GUI.Button(new Rect(x, 3f, 26f, 18f), "S-"))
			{
				result |= Smaller;
			}
			if (GUI.Button(new Rect(x + 29f, 3f, 26f, 18f), "S+"))
			{
				result |= Larger;
			}
			if (GUI.Button(new Rect(x + 58f, 3f, 22f, 18f), collapsed ? "+" : "\u2014"))
			{
				result |= Collapse;
			}
			if (GUI.Button(new Rect(x + 83f, 3f, 22f, 18f), "x"))
			{
				result |= Close;
			}
			return result;
		}

		// Draws the arrow and returns true on the frame the mouse goes down on it.
		internal static bool ResizeHandle(float w, float h)
		{
			Rect r = new Rect(w - 20f, h - 20f, 18f, 18f);
			GUI.Label(r, "\u2198");
			Event e = Event.current;
			if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
			{
				e.Use();
				return true;
			}
			return false;
		}

		// Area for the body below the title row.
		internal static Rect Body(float w, float h)
		{
			return new Rect(Margin, TitleH, Mathf.Max(0f, w - 2f * Margin), Mathf.Max(0f, h - TitleH - Margin));
		}

		// Pixel-scaled variants for plugins that scale rects/fonts themselves instead of using GUI.matrix.
		internal static int TitleRow(float w, string title, bool collapsed, float s, GUIStyle label, GUIStyle button, bool active = true)
		{
			int result = 0;
			GUI.Label(new Rect(Margin * s, 3f * s, w - TitleRight * s, 22f * s), title, label);
			float x = w - 112f * s;
			if (GUI.Button(new Rect(x - 34f * s, 3f * s, 31f * s, 18f * s), active ? "On" : "Off", button))
			{
				result |= Power;
			}
			if (GUI.Button(new Rect(x, 3f * s, 26f * s, 18f * s), "S-", button))
			{
				result |= Smaller;
			}
			if (GUI.Button(new Rect(x + 29f * s, 3f * s, 26f * s, 18f * s), "S+", button))
			{
				result |= Larger;
			}
			if (GUI.Button(new Rect(x + 58f * s, 3f * s, 22f * s, 18f * s), collapsed ? "+" : "—", button))
			{
				result |= Collapse;
			}
			if (GUI.Button(new Rect(x + 83f * s, 3f * s, 22f * s, 18f * s), "x", button))
			{
				result |= Close;
			}
			return result;
		}

		internal static Rect Body(float w, float h, float s)
		{
			return new Rect(Margin * s, TitleH * s, Mathf.Max(0f, w - 2f * Margin * s), Mathf.Max(0f, h - (TitleH + Margin) * s));
		}

		internal static Rect HandleRect(float w, float h, float s)
		{
			return new Rect(w - 20f * s, h - 20f * s, 18f * s, 18f * s);
		}
	}
}
