using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using ZeroT.OrgasmSuite;   // the shared "UI Future Pack" skin (FutureSkin.cs is compiled into this assembly too)

namespace ZeroT.HudDock
{
	// One vertical window with a switch for every other plugin's HUD / window: show or hide each one, collapse each one, hide them all and bring
	// back exactly the ones that were showing. The other plugins already read a bool from their own BepInEx config every frame (Window.Show, GUI.Visible ...),
	// so this plugin only flips those entries; nothing is patched and a plugin that is not loaded is simply listed as such.
	[BepInPlugin(PluginGuid, "HUD Dock", "1.0.0")]
	public sealed class HudDockPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.zerot.huddock";

		// ------------------------------------------------------------------ what can be switched
		private sealed class Target
		{
			public string Label, Guid, ShowSection, ShowKey, CollapseSection, CollapseKey, Field;
			// the plugin's own on / off switch (General.Active / General.Enabled): 'Turn the plugin off completely'. A plugin without one is switched as a
			// Unity component instead (its Update / OnGUI stop), and ActiveGuid points at another plugin that owns the switch (Thigh Compressor -> its UI skin)
			public string ActiveGuid, ActiveSection = "General", ActiveKey = "Active";
			public ConfigEntry<bool> Show, Collapse, ActiveEntry;
			public bool FakeActive = true;
			public BaseUnityPlugin Owner;
			public FieldInfo VisibleField;
			public float NextResolve;
			public bool Fake, FakeVisible, FakeCollapsed;

			public bool Found { get { return Fake || Show != null || VisibleField != null; } }
			public bool CanCollapse { get { return Fake || Collapse != null; } }
			public bool CanActive { get { return Fake || ActiveEntry != null || Owner != null; } }
			public bool IsActive
			{
				get
				{
					if (Fake) return FakeActive;
					if (ActiveEntry != null) return ActiveEntry.Value;
					return Owner != null && Owner.enabled;
				}
			}
			public bool Visible
			{
				get
				{
					if (Fake) return FakeVisible;
					if (Show != null) return Show.Value;
					if (VisibleField != null && Owner != null) { try { object o = VisibleField.GetValue(Owner); return o is bool && (bool)o; } catch { } }
					return false;
				}
			}
			public bool Collapsed { get { return Fake ? FakeCollapsed : (Collapse != null && Collapse.Value); } }
		}

		private static Target T(string label, string guid, string section, string key, string cSection, string cKey)
		{
			Target t = new Target();
			t.Label = label; t.Guid = guid; t.ShowSection = section; t.ShowKey = key; t.CollapseSection = cSection; t.CollapseKey = cKey;
			return t;
		}

		private readonly List<Target> _targets = new List<Target>();

		private static Target Native(Target t, string key) { t.ActiveKey = key; return t; }
		private static Target Native(Target t, string guid, string section, string key) { t.ActiveGuid = guid; t.ActiveSection = section; t.ActiveKey = key; return t; }

		private void BuildTargets()
		{
			_targets.Clear();
			_targets.Add(T("Scene Performance", "com.zerot.sceneperformance.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("SmartWardrobe", "bplus.smartwardrobe.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("Anus Physics", "charlestone.anusphysicscontrol.standalone", "Window", "Visible", "Window", "Collapsed"));
			_targets.Add(Native(T("CounterThrust", "hasno.counterthrust.standalone", "GUI", "VisibleLowImpact", "GUI", "Collapsed"), "Enabled"));
			_targets.Add(Native(T("Penis Path Alignment", "helicalpink.penispalignment.standalone", "GUI", "Visible", "GUI", "Collapsed"), "Enabled"));
			_targets.Add(T("Move Controls", "ky1001.movecontrolstobody.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(Native(T("Thigh Compressor", "com.particlepinnacle.thighcompressorandvibrations", "Touch Filters", "ShowControlWindow", "Touch Filters", "ControlWindowCollapsed"), "zerot.particlepinnacle.uiskin", "General", "Active"));
			Target pc = T("Penetration Counter", "com.14mhz.penetrationcounter.bepinex", "Desktop GUI", "ShowOnStart", "Desktop GUI", "Collapsed");
			pc.Field = "guiVisible";   // its window is a private flag (F-key toggled); ShowOnStart only decides the state at startup
			_targets.Add(pc);
			_targets.Add(T("Procedural Slaps", "ridgerock.proceduralslaps.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("Orifice Dynamics", "skynet.orificedynamics.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("FastMode", "zerot.vamfastmode", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("FluidBridge Helper", "zerot.fluidbridge.helper", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("GiveMeFPS", "com.zerot.givemefps.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("iamjoi Garments", "zerot.iamjoi.garments.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("XRay + Slap", "zerot.xrayslapstandalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("Deeper Plugin", "com.zerot.deeperplugin.standalone", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(T("Detached UI Panel", "zero.vam.detached-ui-panel", "Window", "Show", "Window", "Collapsed"));
			_targets.Add(Native(T("Orgasm Suite", "com.zerot.orgasmsuite", "Window", "Show", "Window", "Collapsed"), "com.zerot.orgasmsuite", "Suite", "Active"));
			_targets.Add(T("VPB Random Look", "com.zerot.vpbrandomlook", "Window", "Show", "Window", "Collapsed"));
			// more of your own: 'Label|plugin guid|show section|show key|collapsed section|collapsed key' separated by ;
			string extra = _extra == null ? "" : _extra.Value;
			string[] items = (extra ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < items.Length; i++)
			{
				string[] f = items[i].Split('|');
				if (f.Length < 4) continue;
				_targets.Add(T(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim(), f.Length > 5 ? f[4].Trim() : f[2].Trim(), f.Length > 5 ? f[5].Trim() : "Collapsed"));
			}
			// screenshots: 'Label:visible:collapsed,...' with 1 / 0
			string fake = _previewRows == null ? "" : _previewRows.Value;
			if (!string.IsNullOrEmpty(fake))
			{
				_targets.Clear();
				string[] parts = fake.Split(',');
				for (int i = 0; i < parts.Length; i++)
				{
					string[] f = parts[i].Split(':');
					if (f.Length < 2 || f[0].Trim().Length == 0) continue;
					Target t = T(f[0].Trim(), "", "", "", "", "");
					t.Fake = true; t.FakeVisible = f[1].Trim() == "1"; t.FakeCollapsed = f.Length > 2 && f[2].Trim() == "1"; t.FakeActive = f.Length <= 3 || f[3].Trim() == "1";
					_targets.Add(t);
				}
			}
		}

		// find the other plugin and its config entries (it may load after this one, so unfound rows are retried)
		private void Resolve(Target t)
		{
			if (t.Fake || t.Found && (t.Collapse != null || t.CollapseKey.Length == 0) && t.ActiveEntry != null) return;
			float now = Time.realtimeSinceStartup;
			if (now < t.NextResolve) return;
			t.NextResolve = now + 3f;
			try
			{
				PluginInfo info;
				if (!Chainloader.PluginInfos.TryGetValue(t.Guid, out info) || info == null || info.Instance == null) return;
				t.Owner = info.Instance;
				ConfigFile cfg = t.Owner.Config;
				ConfigEntry<bool> e;
				if (t.Show == null && t.VisibleField == null)
				{
					if (!string.IsNullOrEmpty(t.Field)) t.VisibleField = t.Owner.GetType().GetField(t.Field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					if (t.VisibleField == null && cfg.TryGetEntry<bool>(t.ShowSection, t.ShowKey, out e)) t.Show = e;
				}
				if (t.Collapse == null && t.CollapseKey.Length > 0 && cfg.TryGetEntry<bool>(t.CollapseSection, t.CollapseKey, out e)) t.Collapse = e;
				if (t.ActiveEntry == null)
				{
					BaseUnityPlugin owner = t.Owner;
					PluginInfo ainfo;
					if (!string.IsNullOrEmpty(t.ActiveGuid) && Chainloader.PluginInfos.TryGetValue(t.ActiveGuid, out ainfo) && ainfo != null && ainfo.Instance != null) owner = ainfo.Instance;
					else if (!string.IsNullOrEmpty(t.ActiveGuid) && t.ActiveGuid != t.Guid) owner = null;
					if (owner != null && owner.Config.TryGetEntry<bool>(t.ActiveSection, t.ActiveKey, out e)) t.ActiveEntry = e;
				}
			}
			catch (Exception ex) { Logger.LogWarning("HUD Dock: " + t.Label + ": " + ex.Message); }
		}

		private void SetVisible(Target t, bool v)
		{
			try
			{
				if (t.Fake) { t.FakeVisible = v; return; }
				if (t.VisibleField != null && t.Owner != null)
				{
					t.VisibleField.SetValue(t.Owner, v);
					ConfigEntry<bool> keep;
					if (t.Owner.Config.TryGetEntry<bool>(t.ShowSection, t.ShowKey, out keep)) keep.Value = v;   // remembered for the next start
				}
				else if (t.Show != null) t.Show.Value = v;
			}
			catch (Exception ex) { Logger.LogWarning("HUD Dock: could not switch " + t.Label + ": " + ex.Message); }
		}

		private void SetActive(Target t, bool v)
		{
			try
			{
				if (t.Fake) { t.FakeActive = v; return; }
				if (t.ActiveEntry != null) t.ActiveEntry.Value = v;
				else if (t.Owner != null) t.Owner.enabled = v;
				Logger.LogInfo("HUD Dock: " + t.Label + " switched " + (v ? "on" : "off") + (t.ActiveEntry != null ? " (its own " + t.ActiveEntry.Definition.Section + "." + t.ActiveEntry.Definition.Key + ")" : " (plugin component)") + ", now reads " + (t.IsActive ? "on" : "off"));
			}
			catch (Exception ex) { Logger.LogWarning("HUD Dock: could not switch " + t.Label + " on / off: " + ex.Message); }
		}

		private void SetCollapsed(Target t, bool v)
		{
			try { if (t.Fake) t.FakeCollapsed = v; else if (t.Collapse != null) t.Collapse.Value = v; }
			catch (Exception ex) { Logger.LogWarning("HUD Dock: could not collapse " + t.Label + ": " + ex.Message); }
		}

		// ------------------------------------------------------------------ hide all / bring back
		private string _saved = "";   // 'guid=1;guid=0' of what was showing when "Hide all" was pressed
		private bool _hidden;

		private void HideAll()
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			for (int i = 0; i < _targets.Count; i++)
			{
				Target t = _targets[i];
				if (!t.Found) continue;
				if (sb.Length > 0) sb.Append(';');
				sb.Append(t.Label).Append('=').Append(t.Visible ? '1' : '0');
				SetVisible(t, false);
			}
			_saved = sb.ToString();
			_hidden = true;
			_state.Value = _saved; _hiddenCfg.Value = true;
		}

		private void ShowAgain()
		{
			HashSet<string> was = new HashSet<string>();
			string[] items = (_saved ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < items.Length; i++) if (items[i].EndsWith("=1")) was.Add(items[i].Substring(0, items[i].Length - 2));
			for (int i = 0; i < _targets.Count; i++) if (_targets[i].Found && was.Contains(_targets[i].Label)) SetVisible(_targets[i], true);
			_hidden = false; _hiddenCfg.Value = false;
		}

		private void AllOn()
		{
			for (int i = 0; i < _targets.Count; i++) if (_targets[i].Found) SetVisible(_targets[i], true);
			_hidden = false; _hiddenCfg.Value = false;
		}

		// ------------------------------------------------------------------ config + window state
		private ConfigEntry<bool> _showWindow, _collapsed, _dpiAware, _hiddenCfg;
		private ConfigEntry<float> _scale, _winX, _winY, _winW, _winH;
		private ConfigEntry<string> _extra, _previewRows, _state;
		private ConfigEntry<KeyCode> _hotkey;

		private Rect _win = new Rect(20f, 60f, 290f, 660f);
		private bool _dragging, _resizing;
		private Vector2 _dragStartMouse, _dragStartPos, _resizeStartMouse, _resizeStartSize;
		private const float MinW = 260f, MaxW = 480f, MinH = 180f, MaxH = 900f;
		private int _first;          // first list row shown (the list scrolls by whole rows)
		private int _listRows;       // rows that fit, set while drawing

		private void Awake()
		{
			_showWindow = Config.Bind("Window", "Show", true, "Show the HUD Dock window.");
			_collapsed = Config.Bind("Window", "Collapsed", false, "Collapse the window to its title bar.");
			_scale = Config.Bind("Window", "Scale", 1.0f, "Extra scale multiplier applied on top of DPI (S- / S+).");
			_winX = Config.Bind("Window", "X", 20f, "Saved horizontal position.");
			_winY = Config.Bind("Window", "Y", 60f, "Saved vertical position.");
			_winW = Config.Bind("Window", "Width", 290f, "Saved width.");
			_winH = Config.Bind("Window", "Height", 660f, "Saved height.");
			_dpiAware = Config.Bind("Window", "DpiAware", true, "Scale with the monitor's DPI.");
			_hotkey = Config.Bind("Window", "Hotkey", KeyCode.F7, "Shows / hides this window (it has no other way back once closed).");
			_extra = Config.Bind("Window", "ExtraHuds", "", "More HUDs to list, ';' separated: 'Label|plugin guid|show section|show key|collapsed section|collapsed key' (the last two optional).");
			_previewRows = Config.Bind("Window", "PreviewRows", "", "For screenshots: fake rows 'Label:visible:collapsed,...' (1 / 0). Empty = the real plugins.");
			_state = Config.Bind("State", "Saved", "", "What was showing when Hide all was pressed.");
			_hiddenCfg = Config.Bind("State", "Hidden", false, "Hide all is active.");
			_saved = _state.Value ?? "";
			_hidden = _hiddenCfg.Value;
			_win = new Rect(_winX.Value, _winY.Value, Mathf.Clamp(_winW.Value, MinW, MaxW), Mathf.Clamp(_winH.Value, MinH, MaxH));
			BuildTargets();
		}

		private void Update()
		{
			if (_hotkey != null && _hotkey.Value != KeyCode.None && Input.GetKeyDown(_hotkey.Value)) { _showWindow.Value = !_showWindow.Value; Config.Save(); }
			if (Time.frameCount % 30 == 0) for (int i = 0; i < _targets.Count; i++) Resolve(_targets[i]);
		}

		// ------------------------------------------------------------------ DPI
		[DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
		[DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
		[StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

		private float _cachedDpi = -1f;
		private double _nextDpiProbe;

		private float DpiScale()
		{
			if (_dpiAware != null && !_dpiAware.Value) return 1f;
			double now = Time.realtimeSinceStartup;
			if (_cachedDpi > 0f && now < _nextDpiProbe) return _cachedDpi;
			_nextDpiProbe = now + 5.0;
			float r = 1f;
			bool got = false;
			try
			{
				POINT pt; pt.X = (int)_win.x; pt.Y = (int)_win.y;
				IntPtr mon = MonitorFromPoint(pt, 2);
				uint dx, dy;
				if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) { r = dx / 96f; got = true; }
			}
			catch { got = false; }
			if (!got)
			{
				try { float d = Screen.dpi; if (d > 0f) { r = d / 96f; got = true; } } catch { }
			}
			_cachedDpi = Mathf.Clamp(got ? r : 1f, 1f, 2.5f);
			return _cachedDpi;
		}

		private void SetScale(float v)
		{
			_scale.Value = Mathf.Clamp((float)Math.Round(v, 1), 0.5f, 2.5f);
			Config.Save();
		}

		// ------------------------------------------------------------------ window
		private ZeroT.UiKit.RlImguiExternal _ext;
		private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();

		private float CollapsedHeight() { return 60f; }

		private void DrawExternal(float w, float h)
		{
			Rect panel = new Rect(0f, 0f, w, h);
			float es = Mathf.Clamp(_scale.Value, 0.3f, 2.5f);
			Fx.Window(panel, es, false);
			DrawWindow(panel, es, false);
		}

		private void OnGUI()
		{
			if (_showWindow == null || !_showWindow.Value) return;
			// explicit rectangles only (no IMGUI layout): the Layout pass and key / move events have nothing to draw or answer
			EventType et = Event.current.type;
			if (et != EventType.Repaint && et != EventType.MouseDown && et != EventType.MouseUp && et != EventType.MouseDrag && et != EventType.ScrollWheel) return;
			if (_ext == null) _ext = new ZeroT.UiKit.RlImguiExternal(this, "Window", "HUD Dock");
			if (_ext.Run(DrawExternal)) return;
			float scale = DpiScale() * Mathf.Clamp(_scale.Value, 0.3f, 2.5f);
			bool collapsed = _collapsed.Value;
			float w = _win.width * scale;
			float h = (collapsed ? CollapsedHeight() : _win.height) * scale;
			_home.Begin(ref _win);
			_win.x = Mathf.Clamp(_win.x, -(w - 60f), Mathf.Max(0f, Screen.width - 60f));
			_win.y = Mathf.Clamp(_win.y, 0f, Mathf.Max(0f, Screen.height - 24f));
			_home.End(ref _win);
			Rect panel = new Rect(_win.x, _win.y, w, h);
			Fx.Window(panel, scale, collapsed);
			DrawWindow(panel, scale, collapsed);
		}

		private static readonly string[] CountText = BuildCounts();
		private static string[] BuildCounts() { string[] a = new string[64]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }
		private static string N(int n) { return n >= 0 && n < CountText.Length ? CountText[n] : n.ToString(); }

		private string _hotkeyText;
		private KeyCode _hotkeyFor;
		private string _countCaption = "", _countKey = "";
		private string Caption(int shown, int found)
		{
			string key = shown + "/" + found;
			if (key != _countKey) { _countKey = key; _countCaption = "HUDS   " + N(shown) + " / " + N(found); }
			return _countCaption;
		}

		private void DrawWindow(Rect panel, float s, bool collapsed)
		{
			float pad = 14f * s, lh = 22f * s, gap = 4f * s;
			int ts = Mathf.Max(9, Mathf.RoundToInt(13f * s));
			int small = Mathf.Max(8, Mathf.RoundToInt(10f * s));
			Fx.Text(new Rect(panel.x + pad, panel.y + 6f * s, panel.width - 150f * s, 22f * s), Fx.Spaced("HUD Dock"), ts, TextAnchor.MiddleLeft, Fx.Ink, true);
			float bw = 24f * s, bh = 18f * s, by = panel.y + 8f * s;
			float bx = panel.xMax - pad - (4f * bw + 3f * gap);
			if (Fx.Mini(new Rect(bx, by, bw, bh), "S-", s)) SetScale(_scale.Value - 0.1f);
			if (Fx.Mini(new Rect(bx + (bw + gap), by, bw, bh), "S+", s)) SetScale(_scale.Value + 0.1f);
			if (Fx.Mini(new Rect(bx + 2f * (bw + gap), by, bw, bh), collapsed ? "+" : "-", s)) { _collapsed.Value = !collapsed; Config.Save(); }
			if (Fx.Mini(new Rect(bx + 3f * (bw + gap), by, bw, bh), "x", s)) { _showWindow.Value = false; Config.Save(); }
			HandleDragResize(panel, s, collapsed);

			int found = 0, shown = 0;
			for (int i = 0; i < _targets.Count; i++) { if (_targets[i].Found) { found++; if (_targets[i].Visible) shown++; } }

			if (collapsed)
			{
				Fx.Text(new Rect(panel.x + pad, panel.y + 34f * s, panel.width - 2f * pad, 20f * s), Fx.Spaced(_hidden ? "All hidden" : N(shown) + " of " + N(found) + " shown"), small, TextAnchor.MiddleLeft, _hidden ? Fx.Amber : Fx.Dim, false);
				return;
			}

			float ty = panel.y + 34f * s;
			Fx.TitleRule(panel.x + pad, ty, panel.width - 2f * pad, s);
			float x = panel.x + pad, w = panel.width - 2f * pad, y = panel.y + 44f * s;

			// ---- hide all / bring back + all on
			float half = (w - gap) / 2f;
			if (Fx.Button(new Rect(x, y, half, lh), _hidden ? "Show again" : "Hide all", Fx.Amber, _hidden, s)) { if (_hidden) ShowAgain(); else HideAll(); }
			if (Fx.Button(new Rect(x + half + gap, y, half, lh), "All on", Fx.Lite, false, s)) AllOn();
			y += lh + 10f * s;

			Fx.Section(new Rect(x, y, w, 16f * s), Caption(shown, found), s);
			y += 22f * s;

			// ---- the list: one row per HUD, switch + collapse; scrolls by whole rows
			float rh = 24f * s;
			float footer = 26f * s;
			float listBottom = panel.yMax - footer - 8f * s;
			_listRows = Mathf.Max(1, Mathf.FloorToInt((listBottom - y) / rh));
			int n = _targets.Count;
			int maxFirst = Mathf.Max(0, n - _listRows);
			_first = Mathf.Clamp(_first, 0, maxFirst);
			Rect listRect = new Rect(x, y, w, _listRows * rh);
			Event ev = Event.current;
			if (ev != null && ev.type == EventType.ScrollWheel && listRect.Contains(ev.mousePosition) && maxFirst > 0)
			{
				_first = Mathf.Clamp(_first + (ev.delta.y > 0f ? 1 : -1), 0, maxFirst);
				ev.Use();
			}
			float barW = n > _listRows ? 6f * s : 0f;
			float cw = 24f * s;
			for (int r = 0; r < _listRows && _first + r < n; r++)
			{
				Target t = _targets[_first + r];
				float ry = y + r * rh;
				bool vis = t.Visible;
				if (!t.Found)
				{
					Fx.Text(new Rect(x + 6f * s, ry, w - 6f * s - barW - 44f * s, rh), t.Label.ToUpperInvariant(), small, TextAnchor.MiddleLeft, Fx.Faint, false);
					Fx.Text(new Rect(x + w - barW - 44f * s, ry, 42f * s, rh), "N/A", small, TextAnchor.MiddleRight, Fx.Faint, false);
					continue;
				}
				float aw = t.CanActive ? 34f * s : 0f;
				float tw = w - barW - (t.CanCollapse ? cw + gap : 0f) - (aw > 0f ? aw + gap : 0f);
				if (Fx.Toggle(new Rect(x, ry, tw, rh), t.Label, vis, Fx.Lite, s, false)) { SetVisible(t, !vis); if (!vis) { _hidden = false; _hiddenCfg.Value = false; } }
				float cx = x + tw + gap;
				if (aw > 0f)
				{
					// the plugin's own on / off ("Active" / "Enabled"): lit = running
					bool on = t.IsActive;
					if (Fx.Button(new Rect(cx, ry + 2f * s, aw, rh - 4f * s), on ? "On" : "Off", Fx.Lite, on, s, false)) SetActive(t, !on);
					cx += aw + gap;
				}
				if (t.CanCollapse)
				{
					bool was = GUI.enabled;
					GUI.enabled = was && vis;
					if (Fx.Mini(new Rect(cx, ry + 3f * s, cw, rh - 6f * s), t.Collapsed ? "+" : "-", s)) SetCollapsed(t, !t.Collapsed);
					GUI.enabled = was;
				}
			}
			if (barW > 0f)
			{
				float track = listRect.height;
				Fx.Fill(new Rect(listRect.xMax - barW + 2f * s, listRect.y, 2f * s, track), Fx.Faint);
				float th = Mathf.Max(14f * s, track * _listRows / n);
				float ty2 = listRect.y + (track - th) * (maxFirst == 0 ? 0f : (float)_first / maxFirst);
				Fx.Fill(new Rect(listRect.xMax - barW + 1f * s, ty2, 4f * s, th), Fx.Dim);
			}

			// ---- footer
			if (_hotkeyText == null || _hotkeyFor != _hotkey.Value) { _hotkeyFor = _hotkey.Value; _hotkeyText = _hotkeyFor == KeyCode.None ? "" : _hotkeyFor + " shows / hides this window"; }
			Fx.Text(new Rect(x, panel.yMax - footer - 2f * s, w - 14f * s, 18f * s), _hotkeyText, small, TextAnchor.MiddleLeft, Fx.Dim, false);
		}

		private void HandleDragResize(Rect panel, float s, bool collapsed)
		{
			Rect title = new Rect(panel.x, panel.y, panel.width - 130f * s, 30f * s);
			Event e = Event.current;
			if (!collapsed)
			{
				Rect grip = new Rect(panel.xMax - 20f * s, panel.yMax - 20f * s, 18f * s, 18f * s);
				GUI.Label(grip, "↘");
				if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
				{
					_resizing = true; _resizeStartMouse = e.mousePosition; _resizeStartSize = new Vector2(_win.width, _win.height); e.Use(); return;
				}
			}
			if (e.type == EventType.MouseDown && title.Contains(e.mousePosition))
			{
				_dragging = true; _dragStartMouse = e.mousePosition; _dragStartPos = _win.position; e.Use();
			}
			else if (_resizing && e.type == EventType.MouseDrag)
			{
				_win.width = Mathf.Clamp(_resizeStartSize.x + (e.mousePosition.x - _resizeStartMouse.x) / s, MinW, MaxW);
				_win.height = Mathf.Clamp(_resizeStartSize.y + (e.mousePosition.y - _resizeStartMouse.y) / s, MinH, MaxH);
				e.Use();
			}
			else if (_dragging && e.type == EventType.MouseDrag)
			{
				_win.position = _dragStartPos + (e.mousePosition - _dragStartMouse); e.Use();
			}
			else if ((_resizing || _dragging) && e.type == EventType.MouseUp)
			{
				_resizing = false; _dragging = false; SaveLayout(); e.Use();
			}
		}

		private void SaveLayout()
		{
			_winX.Value = _win.x; _winY.Value = _win.y; _winW.Value = _win.width; _winH.Value = _win.height;
			Config.Save();
			_cachedDpi = -1f;
		}
	}
}
