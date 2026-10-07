using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.UiKit
{
	internal delegate void RlDraw(float width, float height);

	// External (separate Windows window) mode for IMGUI plugins. The plugin keeps its normal in-game drawing; when the
	// ExternalWindow setting is on it calls Run() first from OnGUI. Run() then drives the plugin's own window drawing
	// itself - with synthetic Layout / mouse / Repaint events - into a render texture, copies the finished picture to a
	// native window (RlExternalWindow) and feeds that window's mouse input back as IMGUI events.
	internal sealed class RlImguiExternal
	{
		private readonly ConfigEntry<bool> enabled;
		private readonly ConfigEntry<string> rectCfg;
		private readonly ConfigEntry<bool> flipCfg;
		private readonly BaseUnityPlugin owner;
		private readonly string title;
		private RlExternalWindow ext;
		private RenderTexture rt;
		private Texture2D tex;
		private int rtW;
		private int rtH;
		private float nextFrame;
		private float nextSave;
		private string lastRect = "";
		private bool failed;

		internal RlImguiExternal(BaseUnityPlugin owner, string section, string title)
		{
			this.owner = owner;
			this.title = title;
			ConfigFile c = owner.Config;
			enabled = c.Bind<bool>(section, "ExternalWindow", false, "EXPERIMENTAL: show this window as a separate Windows window outside the game instead of over the game view.");
			rectCfg = c.Bind<string>(section, "ExternalRect", "", "Last position and size of the external window (x,y,width,height).");
			flipCfg = c.Bind<bool>(section, "ExternalFlipY", false, "Turn on if the external window shows the picture upside down.");
		}

		// Returns true while the external window is in use (the plugin must then skip its in-game drawing).
		// draw(width, height) must draw the plugin window at (0,0) with that size, using GUI.Window or plain GUI calls.
		internal bool Run(RlDraw draw)
		{
			if (failed)
			{
				return false;
			}
			try
			{
				if (enabled.Value && ext == null)
				{
					Open();
				}
				else if (!enabled.Value && ext != null)
				{
					Close();
				}
				if (ext == null)
				{
					return false;
				}
				if (ext.Closed)
				{
					enabled.Value = false;
					owner.Config.Save();
					Close();
					return false;
				}
				if (!ext.Created)
				{
					return true;
				}
				Event real = Event.current;
				if (real == null || real.type != EventType.Repaint)
				{
					return true;
				}
				if (Time.unscaledTime < nextFrame || ext.Minimized)
				{
					return true;
				}
				nextFrame = Time.unscaledTime + 0.04f;
				Frame(draw);
				return true;
			}
			catch (Exception e)
			{
				failed = true;
				UnityEngine.Debug.LogWarning("External window disabled after an error: " + e);
				try
				{
					Close();
				}
				catch
				{
				}
				return false;
			}
		}

		private void Open()
		{
			int x = 140, y = 140, w = 436, h = 559;
			string[] parts = (rectCfg.Value ?? "").Split(',');
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
			nextFrame = 0f;
		}

		private void Close()
		{
			if (ext != null)
			{
				Save();
				ext.Close();
				ext = null;
			}
			if (rt != null)
			{
				rt.Release();
				UnityEngine.Object.Destroy(rt);
				rt = null;
			}
			if (tex != null)
			{
				UnityEngine.Object.Destroy(tex);
				tex = null;
			}
		}

		private void Save()
		{
			if (ext == null)
			{
				return;
			}
			string r = ext.RectString();
			if (r.Length > 0 && r != lastRect && !ext.Minimized)
			{
				lastRect = r;
				rectCfg.Value = r;
			}
		}

		private static Event Make(EventType type)
		{
			Event e = new Event();
			e.type = type;
			return e;
		}

		private void Frame(RlDraw draw)
		{
			// The GUI pipeline draws for a screen-sized target, so the render texture is screen-sized and the window
			// picture is the top-left corner of it.
			int sw = Screen.width;
			int sh = Screen.height;
			int cw = Mathf.Min(ext.ClientW, sw);
			int ch = Mathf.Min(ext.ClientH, sh);
			if (rt == null || rtW != sw || rtH != sh)
			{
				if (rt != null)
				{
					rt.Release();
					UnityEngine.Object.Destroy(rt);
				}
				rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32);
				rtW = sw;
				rtH = sh;
			}
			if (tex == null || tex.width != cw || tex.height != ch)
			{
				if (tex != null)
				{
					UnityEngine.Object.Destroy(tex);
				}
				tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
			}

			Event real = Event.current;
			Matrix4x4 matrix = GUI.matrix;
			GUI.matrix = Matrix4x4.identity;
			try
			{
				// Mouse input: every event is preceded by a Layout pass, like Unity does.
				RlExternalWindow.Mouse m;
				while (ext.TryDequeue(out m))
				{
					Event e;
					switch (m.Kind)
					{
						case 1:
							e = Make(EventType.MouseDown);
							e.button = 0;
							e.clickCount = 1;
							break;
						case 2:
							e = Make(EventType.MouseUp);
							e.button = 0;
							break;
						case 3:
							e = Make(EventType.ScrollWheel);
							e.delta = new Vector2(0f, -m.Wheel / 120f * 3f);
							break;
						default:
							e = Make(EventType.MouseMove);
							break;
					}
					e.mousePosition = new Vector2(m.X, m.Y);
					Event.current = Make(EventType.Layout);
					draw(cw, ch);
					Event.current = e;
					draw(cw, ch);
				}

				Event.current = Make(EventType.Layout);
				draw(cw, ch);

				RenderTexture prev = RenderTexture.active;
				RenderTexture.active = rt;
				GL.Clear(true, true, new Color(0.08f, 0.08f, 0.08f, 1f));
				Event.current = Make(EventType.Repaint);
				draw(cw, ch);
				bool flip = flipCfg.Value;
				tex.ReadPixels(new Rect(0f, flip ? 0f : sh - ch, cw, ch), 0, 0, false);
				RenderTexture.active = prev;
			}
			finally
			{
				GUI.matrix = matrix;
				Event.current = real;
			}

			byte[] raw = tex.GetRawTextureData();
			if (flipCfg.Value)
			{
				int stride = cw * 4;
				byte[] row = new byte[stride];
				for (int a = 0, b = ch - 1; a < b; a++, b--)
				{
					Buffer.BlockCopy(raw, a * stride, row, 0, stride);
					Buffer.BlockCopy(raw, b * stride, raw, a * stride, stride);
					Buffer.BlockCopy(row, 0, raw, b * stride, stride);
				}
			}
			ext.Submit(raw, cw, ch);

			if (Time.unscaledTime >= nextSave)
			{
				nextSave = Time.unscaledTime + 3f;
				string before = rectCfg.Value;
				Save();
				if (rectCfg.Value != before)
				{
					owner.Config.Save();
				}
			}
		}
	}
}
