using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using VPB.Api;

namespace ZeroT.VpbRandomLook
{
    /// <summary>
    /// One-button random look: roll a random scene by creator, take a random female person from it,
    /// and import the ticked resource types onto the chosen live female.
    /// <para>
    /// All the heavy lifting lives in VPB behind <see cref="VpbCompanion"/>; this plugin is the HUD.
    /// The reference is compile-time rather than reflection precisely so a VPB rename breaks the
    /// build instead of silently doing nothing at runtime — but every call is still guarded, because
    /// VPB may not be loaded at all.
    /// </para>
    /// <para>
    /// NOTE: this project targets net40, where <c>lock</c> compiles to a two-argument
    /// <c>Monitor.Enter</c> that VaM's Mono lacks — it throws MissingMethodException on every hit.
    /// There is deliberately no locking here; all state is touched from the Unity main thread only.
    /// </para>
    /// </summary>
    [BepInPlugin(PluginGuid, "VPB Random Look", "1.0.0")]
    public sealed class VpbRandomLookPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.zerot.vpbrandomlook";

        /// <summary>VPB API contract this build was compiled against.</summary>
        private const int RequiredApiVersion = 1;

        private static readonly string[] TypeNames = { "Appearance", "Clothing", "Hair", "Skin", "Morphs" };

        // ---- window layout (persisted) ----
        private ConfigEntry<bool> _showWindow;
        private ConfigEntry<bool> _collapsed;
        private ConfigEntry<float> _scale;
        private ConfigEntry<float> _winX, _winY, _winW, _winH;
        private ConfigEntry<bool> _dpiAware;
        private ConfigEntry<string> _lastTargetUid;

        private Rect _win = new Rect(40f, 120f, 330f, 300f);
        private bool _dragging, _resizing;
        private Vector2 _dragStartMouse, _dragStartPos, _resizeStartMouse, _resizeStartSize;

        private const float MinW = 280f, MaxW = 640f, MinH = 150f, MaxH = 720f;
        private const float CollapsedH = 34f;

        // ---- data ----
        private string[] _creators = new string[0];
        private string[] _targets = new string[0];
        private double _nextRefreshRealtime;
        private string _creatorQuery = "";
        private bool _creatorListOpen;
        private Vector2 _creatorScroll;
        private string _status = "";
        private double _statusUntil;
        private bool _apiOk;
        private string _apiError = "";

        /// <summary>Per-target settings, keyed by target atom uid. Saved on every change.</summary>
        private readonly Dictionary<string, TargetPrefs> _prefs =
            new Dictionary<string, TargetPrefs>(StringComparer.Ordinal);

        private string _targetUid = "";

        private sealed class TargetPrefs
        {
            public string Creator = "";
            public readonly HashSet<string> Types =
                new HashSet<string>(new[] { "Appearance", "Clothing", "Hair", "Skin", "Morphs" }, StringComparer.OrdinalIgnoreCase);
        }

        private string PrefsPath
        {
            get
            {
                string root = Path.Combine(Paths.GameRootPath, "Saves");
                return Path.Combine(Path.Combine(Path.Combine(root, "PluginData"), "zerot"),
                    Path.Combine("VpbRandomLook", "targets.json"));
            }
        }

        private void Awake()
        {
            _showWindow = Config.Bind("Window", "Show", true, "Show the in-game Random Look window.");
            _collapsed = Config.Bind("Window", "Collapsed", false, "Collapse the window to its title bar.");
            _scale = Config.Bind("Window", "Scale", 1.0f, "Extra scale multiplier applied on top of DPI (S- / S+).");
            _winX = Config.Bind("Window", "X", 40f, "Saved horizontal position.");
            _winY = Config.Bind("Window", "Y", 120f, "Saved vertical position.");
            _winW = Config.Bind("Window", "Width", 330f, "Saved width.");
            _winH = Config.Bind("Window", "Height", 300f, "Saved height.");
            _dpiAware = Config.Bind("Window", "DpiAware", true,
                "Scale with the monitor's DPI. Turn off if the window is the wrong size on a mixed-DPI setup.");
            _lastTargetUid = Config.Bind("State", "LastTargetUid", "",
                "Target person remembered across sessions.");

            _win = new Rect(_winX.Value, _winY.Value,
                Mathf.Clamp(_winW.Value, MinW, MaxW),
                Mathf.Clamp(_winH.Value, MinH, MaxH));
            _targetUid = _lastTargetUid.Value ?? "";

            LoadPrefs();
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            while (SuperController.singleton == null) yield return null;
            while (SuperController.singleton.isLoading) yield return null;

            // Probe the API once. A missing/incompatible VPB must degrade to a visible message in the
            // HUD, never an exception storm from OnGUI.
            try
            {
                int v = VpbCompanion.ApiVersion;
                if (v != RequiredApiVersion)
                {
                    _apiOk = false;
                    _apiError = "VPB API v" + v + ", this plugin needs v" + RequiredApiVersion + ".";
                }
                else
                {
                    _apiOk = true;
                    Logger.LogInfo("VPB Random Look ready (VPB " + VpbCompanion.VpbVersion + ", API v" + v + ").");
                }
            }
            catch (Exception ex)
            {
                _apiOk = false;
                _apiError = "VPB not loaded (" + ex.GetType().Name + ").";
                Logger.LogWarning("VPB Random Look: " + _apiError);
            }

            RefreshData(true);
        }

        private void RefreshData(bool force)
        {
            if (!_apiOk) return;
            double now = Time.realtimeSinceStartup;
            if (!force && now < _nextRefreshRealtime) return;
            _nextRefreshRealtime = now + 2.0;

            try { _creators = VpbCompanion.GetSceneCreators() ?? new string[0]; }
            catch { _creators = new string[0]; }
            try { _targets = VpbCompanion.GetFemaleTargetUids() ?? new string[0]; }
            catch { _targets = new string[0]; }

            // Remembered target gone (renamed / new scene): fall back to the first live female and
            // remember that instead, so the button keeps working without a trip to the dropdown.
            if (_targets.Length > 0 && Array.IndexOf(_targets, _targetUid) < 0)
            {
                SetTarget(_targets[0]);
                SetStatus("Target fell back to " + _targets[0]);
            }
        }

        // ------------------------------------------------------------------ prefs

        private TargetPrefs Current
        {
            get
            {
                if (string.IsNullOrEmpty(_targetUid)) return null;
                TargetPrefs p;
                if (!_prefs.TryGetValue(_targetUid, out p))
                {
                    p = new TargetPrefs();
                    _prefs[_targetUid] = p;
                }
                return p;
            }
        }

        private void SetTarget(string uid)
        {
            _targetUid = uid ?? "";
            _lastTargetUid.Value = _targetUid;
            Config.Save();
            SavePrefs();
        }

        private void LoadPrefs()
        {
            try
            {
                string path = PrefsPath;
                if (!File.Exists(path)) return;
                string text = File.ReadAllText(path);
                // Hand-rolled minimal parse: one target per line as
                // uid<TAB>creator<TAB>Type,Type,Type — avoids taking a JSON dependency for 3 fields.
                foreach (string raw in text.Split('\n'))
                {
                    string line = (raw ?? "").Trim('\r', ' ', '\t');
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var p = new TargetPrefs();
                    p.Creator = parts[1];
                    p.Types.Clear();
                    foreach (string t in parts[2].Split(','))
                    {
                        string tt = t.Trim();
                        if (tt.Length > 0) p.Types.Add(tt);
                    }
                    _prefs[parts[0]] = p;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("VPB Random Look: could not read saved target settings: " + ex.Message);
            }
        }

        private void SavePrefs()
        {
            try
            {
                string path = PrefsPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder(256);
                sb.Append("# VPB Random Look — per-target settings\n");
                sb.Append("# uid<TAB>creator<TAB>types\n");
                foreach (var kv in _prefs)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.Creator ?? "").Append('\t');
                    bool first = true;
                    foreach (string t in kv.Value.Types)
                    {
                        if (!first) sb.Append(',');
                        sb.Append(t);
                        first = false;
                    }
                    sb.Append('\n');
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                Logger.LogWarning("VPB Random Look: could not save target settings: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ DPI

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private float _cachedDpiScale = -1f;
        private double _nextDpiProbe;

        /// <summary>
        /// Per-monitor DPI where Windows can tell us, falling back to Unity's <see cref="Screen.dpi"/>,
        /// then to 1.0. Shcore is absent before Windows 8.1 and the call fails on some drivers, which
        /// is why both fallbacks exist rather than assuming the P/Invoke succeeds.
        /// </summary>
        private float DpiScale()
        {
            if (_dpiAware != null && !_dpiAware.Value) return 1f;

            double now = Time.realtimeSinceStartup;
            if (_cachedDpiScale > 0f && now < _nextDpiProbe) return _cachedDpiScale;
            _nextDpiProbe = now + 5.0;

            float result = 1f;
            bool got = false;
            try
            {
                POINT pt;
                pt.X = (int)_win.x;
                pt.Y = (int)_win.y;
                IntPtr mon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
                uint dx, dy;
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out dx, out dy) == 0 && dx > 0)
                {
                    result = dx / 96f;
                    got = true;
                }
            }
            catch { got = false; }

            if (!got)
            {
                try
                {
                    float d = Screen.dpi;
                    if (d > 0f) { result = d / 96f; got = true; }
                }
                catch { }
            }

            _cachedDpiScale = Mathf.Clamp(got ? result : 1f, 1f, 2.5f);
            return _cachedDpiScale;
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (_showWindow == null || !_showWindow.Value) return;

            float scale = DpiScale() * Mathf.Clamp(_scale.Value, 0.65f, 2.5f);
            bool collapsed = _collapsed.Value;

            float w = _win.width * scale;
            float h = (collapsed ? CollapsedH : _win.height) * scale;

            ClampToDisplay(w, h);

            Rect panel = new Rect(_win.x, _win.y, w, h);
            GUI.Box(panel, "");
            DrawWindow(panel, scale, collapsed);
        }

        /// <summary>Keep at least a title bar's worth of window on screen so it can never be lost.</summary>
        private void ClampToDisplay(float w, float h)
        {
            float maxX = Mathf.Max(0f, Screen.width - 60f);
            float maxY = Mathf.Max(0f, Screen.height - 24f);
            _win.x = Mathf.Clamp(_win.x, -(w - 60f), maxX);
            _win.y = Mathf.Clamp(_win.y, 0f, maxY);
        }

        private void DrawWindow(Rect panel, float s, bool collapsed)
        {
            float pad = 8f * s;
            float lh = 20f * s;

            GUI.Label(new Rect(panel.x + pad, panel.y + 3f * s, panel.width - 118f * s, 22f * s), "VPB Random Look");

            float bx = panel.xMax - 112f * s;
            if (GUI.Button(new Rect(bx, panel.y + 3f * s, 26f * s, 18f * s), "S-")) SetScale(_scale.Value - 0.1f);
            if (GUI.Button(new Rect(bx + 29f * s, panel.y + 3f * s, 26f * s, 18f * s), "S+")) SetScale(_scale.Value + 0.1f);
            if (GUI.Button(new Rect(bx + 58f * s, panel.y + 3f * s, 22f * s, 18f * s), collapsed ? "+" : "—"))
            {
                _collapsed.Value = !collapsed;
                Config.Save();
            }
            if (GUI.Button(new Rect(bx + 83f * s, panel.y + 3f * s, 22f * s, 18f * s), "x"))
            {
                _showWindow.Value = false;
                Config.Save();
            }

            HandleDragResize(panel, s, collapsed);
            if (collapsed) return;

            float y = panel.y + 28f * s;

            if (!_apiOk)
            {
                GUI.Label(new Rect(panel.x + pad, y, panel.width - 2f * pad, 44f * s),
                    "VPB companion API unavailable.\n" + _apiError);
                return;
            }

            RefreshData(false);

            // ---- target ----
            GUI.Label(new Rect(panel.x + pad, y, panel.width - 2f * pad, lh),
                "Target (live female): " + (_targets.Length == 0 ? "none in scene" : _targetUid));
            y += lh + 2f * s;

            if (_targets.Length > 0)
            {
                float tw = (panel.width - 2f * pad) / Mathf.Max(1, Mathf.Min(_targets.Length, 3));
                for (int i = 0; i < _targets.Length && i < 3; i++)
                {
                    bool sel = _targets[i] == _targetUid;
                    string label = sel ? "[" + _targets[i] + "]" : _targets[i];
                    if (GUI.Button(new Rect(panel.x + pad + i * tw, y, tw - 2f * s, 22f * s), label))
                        SetTarget(_targets[i]);
                }
                y += 26f * s;
            }

            // ---- creator (typable) ----
            TargetPrefs prefs = Current;
            string creatorShown = prefs != null && !string.IsNullOrEmpty(prefs.Creator) ? prefs.Creator : "(Any creator)";
            GUI.Label(new Rect(panel.x + pad, y, 62f * s, lh), "Creator:");
            string typed = GUI.TextField(new Rect(panel.x + pad + 64f * s, y, panel.width - 2f * pad - 130f * s, lh), _creatorQuery);
            if (typed != _creatorQuery)
            {
                _creatorQuery = typed;
                _creatorListOpen = true;
            }
            if (GUI.Button(new Rect(panel.xMax - pad - 62f * s, y, 28f * s, lh), _creatorListOpen ? "^" : "v"))
                _creatorListOpen = !_creatorListOpen;
            if (GUI.Button(new Rect(panel.xMax - pad - 31f * s, y, 31f * s, lh), "Any"))
            {
                if (prefs != null) { prefs.Creator = ""; SavePrefs(); }
                _creatorQuery = "";
                _creatorListOpen = false;
            }
            y += lh + 2f * s;

            GUI.Label(new Rect(panel.x + pad, y, panel.width - 2f * pad, lh), "  using: " + creatorShown);
            y += lh;

            if (_creatorListOpen)
            {
                float listH = Mathf.Min(120f * s, Mathf.Max(40f * s, panel.yMax - y - 80f * s));
                var matches = new List<string>(16);
                for (int i = 0; i < _creators.Length && matches.Count < 200; i++)
                {
                    if (_creatorQuery.Length == 0
                        || _creators[i].IndexOf(_creatorQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                        matches.Add(_creators[i]);
                }

                Rect listRect = new Rect(panel.x + pad, y, panel.width - 2f * pad, listH);
                Rect viewRect = new Rect(0f, 0f, listRect.width - 18f * s, matches.Count * 20f * s);
                _creatorScroll = GUI.BeginScrollView(listRect, _creatorScroll, viewRect);
                for (int i = 0; i < matches.Count; i++)
                {
                    if (GUI.Button(new Rect(0f, i * 20f * s, viewRect.width, 19f * s), matches[i]))
                    {
                        if (prefs != null) { prefs.Creator = matches[i]; SavePrefs(); }
                        _creatorQuery = "";
                        _creatorListOpen = false;
                    }
                }
                GUI.EndScrollView();
                y += listH + 4f * s;
            }

            // ---- type toggles ----
            GUI.Label(new Rect(panel.x + pad, y, panel.width - 2f * pad, lh), "Apply:");
            y += lh;
            float colW = (panel.width - 2f * pad) / 3f;
            for (int i = 0; i < TypeNames.Length; i++)
            {
                int col = i % 3, row = i / 3;
                bool on = prefs != null && prefs.Types.Contains(TypeNames[i]);
                Rect r = new Rect(panel.x + pad + col * colW, y + row * 24f * s, colW - 3f * s, 22f * s);
                if (GUI.Button(r, (on ? "[x] " : "[ ] ") + TypeNames[i]) && prefs != null)
                {
                    if (on) prefs.Types.Remove(TypeNames[i]); else prefs.Types.Add(TypeNames[i]);
                    SavePrefs();
                }
            }
            y += 24f * s * 2f + 4f * s;

            // ---- go ----
            if (GUI.Button(new Rect(panel.x + pad, y, panel.width - 2f * pad, 30f * s), "↺  Random Look"))
                FireRandom();
            y += 34f * s;

            if (Time.realtimeSinceStartup < _statusUntil && !string.IsNullOrEmpty(_status))
                GUI.Label(new Rect(panel.x + pad, y, panel.width - 2f * pad, 34f * s), _status);
        }

        private void FireRandom()
        {
            TargetPrefs prefs = Current;
            if (prefs == null)
            {
                SetStatus("No target selected.");
                return;
            }
            if (prefs.Types.Count == 0)
            {
                SetStatus("Tick at least one type to apply.");
                return;
            }

            var types = new List<string>(prefs.Types.Count);
            foreach (string t in prefs.Types) types.Add(t);

            try
            {
                string error;
                if (VpbCompanion.TryRandomFemaleImport(prefs.Creator, _targetUid, types.ToArray(), out error))
                    SetStatus("Rolling " + (string.IsNullOrEmpty(prefs.Creator) ? "(any creator)" : prefs.Creator) + "…");
                else
                    SetStatus("Failed: " + (error ?? "unknown"));
            }
            catch (Exception ex)
            {
                SetStatus("Failed: " + ex.Message);
                Logger.LogWarning("VPB Random Look: import call failed: " + ex);
            }
        }

        private void SetStatus(string msg)
        {
            _status = msg ?? "";
            _statusUntil = Time.realtimeSinceStartup + 4.0;
        }

        private void SetScale(float v)
        {
            _scale.Value = Mathf.Clamp(v, 0.65f, 2.5f);
            _cachedDpiScale = -1f;
            Config.Save();
        }

        /// <summary>Pixel-space drag/resize: accurate at any DPI, unlike scaling through GUI.matrix.</summary>
        private void HandleDragResize(Rect panel, float s, bool collapsed)
        {
            Rect title = new Rect(panel.x, panel.y, panel.width - 118f * s, 24f * s);
            Event e = Event.current;

            if (!collapsed)
            {
                Rect grip = new Rect(panel.xMax - 20f * s, panel.yMax - 20f * s, 18f * s, 18f * s);
                GUI.Label(grip, "↘");
                if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
                {
                    _resizing = true;
                    _resizeStartMouse = e.mousePosition;
                    _resizeStartSize = new Vector2(_win.width, _win.height);
                    e.Use();
                    return;
                }
            }

            if (e.type == EventType.MouseDown && title.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragStartMouse = e.mousePosition;
                _dragStartPos = _win.position;
                e.Use();
            }
            else if (_resizing && e.type == EventType.MouseDrag)
            {
                _win.width = Mathf.Clamp(_resizeStartSize.x + (e.mousePosition.x - _resizeStartMouse.x) / s, MinW, MaxW);
                _win.height = Mathf.Clamp(_resizeStartSize.y + (e.mousePosition.y - _resizeStartMouse.y) / s, MinH, MaxH);
                e.Use();
            }
            else if (_dragging && e.type == EventType.MouseDrag)
            {
                _win.position = _dragStartPos + (e.mousePosition - _dragStartMouse);
                e.Use();
            }
            else if ((_resizing || _dragging) && e.type == EventType.MouseUp)
            {
                _resizing = false;
                _dragging = false;
                SaveLayout();
                e.Use();
            }
        }

        private void SaveLayout()
        {
            _winX.Value = _win.x;
            _winY.Value = _win.y;
            _winW.Value = _win.width;
            _winH.Value = _win.height;
            // BepInEx only guarantees a disk write at shutdown; persist each completed gesture so a
            // crash cannot lose the layout.
            Config.Save();
            _cachedDpiScale = -1f;
        }
    }
}
