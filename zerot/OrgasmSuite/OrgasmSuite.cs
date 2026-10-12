using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using MVR.FileManagement;
using UnityEngine;

namespace ZeroT.OrgasmSuite
{
	// One BepInEx plugin that carries the original VaM scripts (SexyFluids, BodyLanguage + BodyLanguage Lite with their HUDs, FluidBridge,
	// AutoTremble, ZajeShuffle) as embedded files, puts them under Custom/Scripts/ZeroT/OrgasmSuite and attaches them to every Person
	// through VaM's own plugin manager. The window switches each Person between BodyLanguage Lite and BodyLanguage and turns the parts on/off.
	[BepInPlugin(PluginGuid, "Orgasm Suite", "1.0.0")]
	public sealed class OrgasmSuitePlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.zerot.orgasmsuite";
		public const string Root = "Custom/Scripts/ZeroT/OrgasmSuite/";
		private const int SoftFlag = 0x40000000;

		// ------------------------------------------------------------------ the scripts the suite carries
		public sealed class Comp
		{
			public readonly string Key, Label, Url, TypeName;
			public readonly string[] Ext;   // file names a scene's own .var plugin for the same script goes by
			public Comp(string key, string label, string url, string typeName, params string[] ext)
			{
				Key = key; Label = label; Url = url; TypeName = typeName; Ext = ext;
			}
		}

		// precompiled assemblies (built from the original scripts, see prepare_scripts.py): VaM loads them like any plugin DLL and its own
		// script compiler / compile cache is not involved
		public static readonly Comp Lite = new Comp("lite", "BodyLanguage Lite", Root + "BodyLanguage_Lite/ZeroT.OrgasmSuite.BodyLanguageLite.dll", "BodyLanguage_Lite", "BodyLanguage_Lite.cslist");
		public static readonly Comp LiteHud = new Comp("litehud", "Arousal HUD", Root + "BodyLanguage_Lite/ZeroT.OrgasmSuite.ArousalHUD.dll", "ArousalHUD", "ArousalHUD.cslist");
		public static readonly Comp Full = new Comp("bl", "BodyLanguage", Root + "BodyLanguage/ZeroT.OrgasmSuite.BodyLanguage.dll", "ReadMyLips", "BodyLanguage.cslist");
		public static readonly Comp FullHud = new Comp("blhud", "Stimulation HUD", Root + "StimulationHUD/ZeroT.OrgasmSuite.StimulationHUD.dll", "StimulationHUD", "StimulationHUD.cslist");
		public static readonly Comp Sexy = new Comp("sexy", "SexyFluids", Root + "SexyFluids/ZeroT.OrgasmSuite.SexyFluids.dll", "SexyFluids", "SexyFluids.cslist");
		public static readonly Comp Bridge = new Comp("bridge", "FluidBridge", Root + "FluidBridge/ZeroT.OrgasmSuite.FluidBridge.dll", "FluidBridge", "FluidBridge.cslist");
		public static readonly Comp Tremble = new Comp("tremble", "AutoTremble", Root + "AutoTremble/ZeroT.OrgasmSuite.AutoTremble.dll", "AutoTremble", "AutoTremble.cs");
		public static readonly Comp Zaje = new Comp("zaje", "ZajeShuffle", Root + "ZajeShuffle/ZeroT.OrgasmSuite.ZajeShuffle.dll", "ZajeShuffle", "ZajeShuffle.cslist");
		public static readonly Comp[] All = { Lite, LiteHud, Full, FullHud, Sexy, Bridge, Tremble, Zaje };

		// ------------------------------------------------------------------ per Person settings
		public sealed class PersonCfg
		{
			public bool On = true;
			public string Engine = "Lite";   // Lite | BodyLanguage
			// what happens during an orgasm on this Person (each one is a part of the suite that can be switched off) + the HUD
			public bool Fluids = true, Tremble = true, Shuffle = true, Wet = true, Wardrobe = true, Hud = true;
		}

		private readonly Dictionary<string, PersonCfg> _cfg = new Dictionary<string, PersonCfg>(StringComparer.Ordinal);

		// ------------------------------------------------------------------ config
		private ConfigEntry<bool> _active, _females, _males, _autoAttach;
		private ConfigEntry<float> _settle, _quiet;
		private ConfigEntry<bool> _showWindow, _collapsed, _dpiAware;
		private ConfigEntry<float> _scale, _winX, _winY, _winW, _winH;
		private ConfigEntry<string> _lastUid;

		private Rect _win = new Rect(60f, 140f, 380f, 450f);
		private bool _dragging, _resizing;
		private Vector2 _dragStartMouse, _dragStartPos, _resizeStartMouse, _resizeStartSize;
		private const float MinW = 320f, MaxW = 640f, MinH = 260f, MaxH = 760f, CollapsedH = 34f;

		// ------------------------------------------------------------------ runtime
		private bool _installed;
		private string _installNote = "";
		private string _message = "";
		private double _messageUntil;
		private string _uid = "";
		private int _page;
		private float _nextScan, _loadedAt = -99f;
		private bool _wasLoading = true;
		private readonly List<Atom> _people = new List<Atom>();
		private readonly Dictionary<string, float> _attachedAt = new Dictionary<string, float>();
		private readonly Dictionary<string, string> _pushed = new Dictionary<string, string>();
		private readonly Dictionary<string, string> _status = new Dictionary<string, string>();
		private Vector2 _statusScroll;

		// the plugin manager's own list of slots (protected field)
		private static readonly FieldInfo PluginsField = typeof(MVRPluginManager).GetField("plugins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		private string PeoplePath { get { return Path.Combine(Path.Combine(Path.Combine(Path.Combine(Paths.GameRootPath, "Saves"), "PluginData"), "ZeroT"), Path.Combine("OrgasmSuite", "people.tsv")); } }

		private void Awake()
		{
			_active = Config.Bind("Suite", "Active", true, "Master switch: when off the suite attaches nothing and leaves everything as it is.");
			_females = Config.Bind("Suite", "Females", true, "Manage female Person atoms.");
			_males = Config.Bind("Suite", "Males", true, "Manage male Person atoms.");
			_autoAttach = Config.Bind("Suite", "AutoAttach", true, "Attach / detach the scripts by itself. Off: the suite never touches a Person (use it only to switch the choices on and off in the window).");
			_settle = Config.Bind("Suite", "SettleSeconds", 15f, "After a scene load finishes, wait at least this long before attaching anything: VaM keeps restoring the scene's own plugins for a while after it says loading is done, and a script added in the middle of that crashed VaM.");
			_quiet = Config.Bind("Suite", "QuietSeconds", 6f, "...and until nothing about the atoms or their plugin slots has changed for this long (changes the suite itself makes do not count).");
			_showWindow = Config.Bind("Window", "Show", true, "Show the in-game Orgasm Suite window.");
			_collapsed = Config.Bind("Window", "Collapsed", false, "Collapse the window to its title bar.");
			_scale = Config.Bind("Window", "Scale", 1.0f, "Extra scale multiplier applied on top of DPI (S- / S+).");
			_winX = Config.Bind("Window", "X", 60f, "Saved horizontal position.");
			_winY = Config.Bind("Window", "Y", 140f, "Saved vertical position.");
			_winW = Config.Bind("Window", "Width", 380f, "Saved width.");
			_winH = Config.Bind("Window", "Height", 450f, "Saved height.");
			_dpiAware = Config.Bind("Window", "DpiAware", true, "Scale with the monitor's DPI.");
			_lastUid = Config.Bind("State", "LastPerson", "", "Person remembered across sessions.");
			_win = new Rect(_winX.Value, _winY.Value, Mathf.Clamp(_winW.Value, MinW, MaxW), Mathf.Clamp(_winH.Value, MinH, MaxH));
			_uid = _lastUid.Value ?? "";
			LoadPeople();
			try
			{
				int n = InstallEmbeddedFiles();
				_installed = true;
				_installNote = n + " file(s) written";
				Logger.LogInfo("Orgasm Suite: scripts ready (" + _installNote + ").");
			}
			catch (Exception e)
			{
				_installed = false;
				_installNote = "extract failed: " + e.Message;
				Logger.LogError("Orgasm Suite: could not extract the embedded scripts: " + e);
			}
			_nextScan = Time.realtimeSinceStartup + 3f;
		}

		// ------------------------------------------------------------------ embedded files
		private int InstallEmbeddedFiles()
		{
			Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("OrgasmSuite.scripts");
			if (rs == null) throw new InvalidDataException("the embedded script pack is missing");
			int written = 0;
			string root = Path.GetFullPath(Paths.GameRootPath);
			HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (rs)
			using (DeflateStream ds = new DeflateStream(rs, CompressionMode.Decompress))
			using (BinaryReader br = new BinaryReader(ds))
			{
				int count = br.ReadInt32();
				if (count < 1 || count > 5000) throw new InvalidDataException("bad file count " + count);
				for (int i = 0; i < count; i++)
				{
					string rel = br.ReadString().Replace('\\', '/');
					int lenField = br.ReadInt32();
					bool soft = (lenField & SoftFlag) != 0;
					int len = lenField & ~SoftFlag;
					if (len < 0 || len > 40000000) throw new InvalidDataException("bad size for " + rel);
					byte[] data = br.ReadBytes(len);
					if (data.Length != len) throw new EndOfStreamException(rel);
					bool allowed = rel.StartsWith(Root, StringComparison.Ordinal)
						|| rel.StartsWith("Saves/PluginData/Foost/SexyFluids/", StringComparison.Ordinal)
						|| rel.StartsWith("Saves/PluginPresets/AutoTremble/", StringComparison.Ordinal);
					if (!allowed || rel.Contains("..")) throw new InvalidDataException("path outside the suite folders: " + rel);
					keep.Add(rel);
					string full = Path.GetFullPath(Path.Combine(root, rel));
					if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("path escapes the VaM folder: " + rel);
					if (File.Exists(full))
					{
						if (soft) continue;                       // the user's own presets / data are never overwritten
						if (SameBytes(File.ReadAllBytes(full), data)) continue;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(full));
					string tmp = full + ".suite.tmp";
					File.WriteAllBytes(tmp, data);
					if (File.Exists(full)) File.Delete(full);
					File.Move(tmp, full);
					written++;
				}
			}
			RemoveStaleFiles(root, keep);
			return written;
		}

		// the earlier build extracted loose source; only files the current pack knows stay in the suite's own folder
		private void RemoveStaleFiles(string root, HashSet<string> keep)
		{
			try
			{
				string dir = Path.Combine(root, Root.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
				if (!Directory.Exists(dir)) return;
				string[] all = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
				for (int i = 0; i < all.Length; i++)
				{
					string rel = all[i].Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
					if (!keep.Contains(rel)) { try { File.Delete(all[i]); } catch { } }
				}
			}
			catch (Exception e) { Logger.LogWarning("Orgasm Suite: could not tidy the old files: " + e.Message); }
		}

		private static bool SameBytes(byte[] a, byte[] b)
		{
			if (a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
			return true;
		}

		// ------------------------------------------------------------------ saved per-person settings
		private void LoadPeople()
		{
			try
			{
				string p = PeoplePath;
				if (!File.Exists(p)) return;
				foreach (string raw in File.ReadAllText(p).Split('\n'))
				{
					string line = raw.Trim('\r', ' ', '\t');
					if (line.Length == 0 || line[0] == '#') continue;
					string[] f = line.Split('\t');
					if (f.Length < 6) continue;
					PersonCfg c = new PersonCfg();
					c.On = f[1] == "1";
					c.Engine = f[2] == "BodyLanguage" ? "BodyLanguage" : "Lite";
					c.Fluids = f[3] == "1";
					c.Tremble = f[4] == "1";
					c.Hud = f[5] == "1";
					if (f.Length >= 9) { c.Shuffle = f[6] == "1"; c.Wet = f[7] == "1"; c.Wardrobe = f[8] == "1"; }   // older files stop at the HUD column
					_cfg[f[0]] = c;
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Orgasm Suite: could not read " + PeoplePath + ": " + e.Message);
			}
		}

		private void SavePeople()
		{
			try
			{
				string p = PeoplePath;
				Directory.CreateDirectory(Path.GetDirectoryName(p));
				StringBuilder sb = new StringBuilder();
				sb.Append("# Orgasm Suite per Person: uid<TAB>on<TAB>engine<TAB>fluids<TAB>tremble<TAB>hud\n");
				foreach (KeyValuePair<string, PersonCfg> kv in _cfg)
				{
					PersonCfg c = kv.Value;
					sb.Append(kv.Key).Append('\t').Append(c.On ? "1" : "0").Append('\t').Append(c.Engine).Append('\t')
						.Append(c.Fluids ? "1" : "0").Append('\t').Append(c.Tremble ? "1" : "0").Append('\t').Append(c.Hud ? "1" : "0")
							.Append('\t').Append(c.Shuffle ? "1" : "0").Append('\t').Append(c.Wet ? "1" : "0").Append('\t').Append(c.Wardrobe ? "1" : "0").Append('\n');
				}
				File.WriteAllText(p, sb.ToString());
			}
			catch (Exception e)
			{
				Logger.LogWarning("Orgasm Suite: could not save " + PeoplePath + ": " + e.Message);
			}
		}

		public PersonCfg CfgFor(string uid)
		{
			PersonCfg c;
			if (!_cfg.TryGetValue(uid, out c))
			{
				c = new PersonCfg();
				// every HUD pins itself to the same spot on the desktop: only the first Person starts with one
				foreach (PersonCfg o in _cfg.Values) if (o.On && o.Hud) { c.Hud = false; break; }
				_cfg[uid] = c;
			}
			return c;
		}

		// ------------------------------------------------------------------ people / managers
		private static DAZCharacterSelector Selector(Atom a)
		{
			try { return a.GetStorableByID("geometry") as DAZCharacterSelector; }
			catch { return null; }
		}

		private readonly Dictionary<string, bool> _female = new Dictionary<string, bool>();

		public static bool IsFemale(Atom a)
		{
			DAZCharacterSelector s = Selector(a);
			return s != null && s.gender == DAZCharacterSelector.Gender.Female;
		}

		private bool Managed(Atom a)
		{
			if (a == null || a.type != "Person") return false;
			DAZCharacterSelector s = Selector(a);
			if (s == null) return false;
			return (s.gender == DAZCharacterSelector.Gender.Female && _females.Value) || (s.gender == DAZCharacterSelector.Gender.Male && _males.Value);
		}

		private static MVRPluginManager FindManager(Atom atom)
		{
			if (atom == null || atom.gameObject == null) return null;
			MVRPluginManager m = atom.GetStorableByID("PluginManager") as MVRPluginManager;
			if (m == null) m = atom.GetStorableByID("plugin") as MVRPluginManager;
			if (m != null) return m;
			MVRPluginManager[] all = atom.gameObject.GetComponentsInChildren<MVRPluginManager>(true);
			return all.Length > 0 ? all[0] : null;
		}

		private static List<MVRPlugin> SlotsOf(MVRPluginManager mgr)
		{
			List<MVRPlugin> l = new List<MVRPlugin>();
			try
			{
				IEnumerable<MVRPlugin> e = PluginsField == null || mgr == null ? null : PluginsField.GetValue(mgr) as IEnumerable<MVRPlugin>;
				if (e != null) foreach (MVRPlugin p in e) if (p != null) l.Add(p);
			}
			catch { }
			return l;
		}

		private static string UrlOf(MVRPlugin p)
		{
			try { return p.pluginURLJSON == null ? "" : (p.pluginURLJSON.val ?? ""); }
			catch { return ""; }
		}

		private static bool UrlIs(string url, Comp c)
		{
			return url.Replace('\\', '/').EndsWith(c.Url, StringComparison.OrdinalIgnoreCase);
		}

		// our slot for a component on this manager
		private static MVRPlugin OurSlot(MVRPluginManager mgr, Comp c)
		{
			List<MVRPlugin> l = SlotsOf(mgr);
			for (int i = 0; i < l.Count; i++) if (UrlIs(UrlOf(l[i]), c)) return l[i];
			return null;
		}

		// a slot from somebody else (a scene's own .var plugin) for the same script file: it may still be compiling, so the type is not there yet
		private static bool ExternalSlot(MVRPluginManager mgr, Comp c)
		{
			foreach (MVRPlugin p in SlotsOf(mgr))
			{
				string u = UrlOf(p).Replace('\\', '/');
				if (UrlIs(u, c)) continue;
				for (int e = 0; e < c.Ext.Length; e++)
					if (u.EndsWith("/" + c.Ext[e], StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}

		// the plugin scripts on an Atom, from its storable ids ("plugin#N_Type"): far cheaper than walking the whole hierarchy of a Person.
		// Looked up once per atom per scan.
		private readonly Dictionary<Atom, List<MVRScript>> _scripts = new Dictionary<Atom, List<MVRScript>>();

		private List<MVRScript> ScriptsOf(Atom atom)
		{
			List<MVRScript> l;
			if (_scripts.TryGetValue(atom, out l)) return l;
			l = new List<MVRScript>();
			try
			{
				List<string> ids = atom.GetStorableIDs();
				for (int i = 0; ids != null && i < ids.Count; i++)
				{
					if (!ids[i].StartsWith("plugin#", StringComparison.Ordinal)) continue;
					MVRScript m = atom.GetStorableByID(ids[i]) as MVRScript;
					if (m != null) l.Add(m);
				}
			}
			catch { }
			_scripts[atom] = l;
			return l;
		}

		// a running script of that type on the atom, from any source
		private MVRScript Instance(Atom atom, Comp c)
		{
			if (atom == null) return null;
			List<MVRScript> all = ScriptsOf(atom);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i] != null && all[i].GetType().Name == c.TypeName) return all[i];
			}
			return null;
		}

		// the same type loaded from somebody else's slot (a .var the scene brought along): the suite leaves that one alone

		// ------------------------------------------------------------------ the reconcile loop
		private void Update()
		{
			if (_active == null || !_active.Value) return;
			SuperController sc = SuperController.singleton;
			if (sc == null) return;
			if (sc.isLoading) { _wasLoading = true; return; }
			if (_wasLoading) { _wasLoading = false; _loadedAt = Time.realtimeSinceStartup; _attachedAt.Clear(); _pushed.Clear(); }
			if (!_autoAttach.Value) { _waitNote = "Auto attach is off."; return; }
			float now = Time.realtimeSinceStartup;
			if (now < _loadedAt + _settle.Value) { _waitNote = "Waiting for the scene to settle (" + Mathf.CeilToInt(_loadedAt + _settle.Value - now) + " s)..."; return; }
			if (now < _nextScan) return;
			_nextScan = now + 1.5f;
			try
			{
				// the scene is still restoring while atoms or their plugin slots keep changing: touching a Person then crashed VaM
				int sig = SceneSignature(sc);
				if (sig != _sig) { _sig = sig; _sigAt = now; }
				if (now - _sigAt < _quiet.Value) { _waitNote = "Waiting for the scene to settle (atoms / plugins still changing)..."; return; }
				_waitNote = "";
				Reconcile(sc);
				_sig = SceneSignature(sc);   // what the suite itself just did is not a change by the scene
			}
			catch (Exception e) { Logger.LogWarning("Orgasm Suite: " + e.Message); }
		}

		private int _sig;
		private float _sigAt;
		private string _waitNote = "";

		// atoms, their plugin slots and their registered storables: all of it moves while a scene is being restored
		private int SceneSignature(SuperController sc)
		{
			int h = 17;
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; atoms != null && i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null) continue;
				h = h * 31 + (a.uid == null ? 0 : a.uid.GetHashCode());
				MVRPluginManager m = FindManager(a);
				if (m != null)
				{
					List<MVRPlugin> sl = SlotsOf(m);
					h = h * 31 + sl.Count;
					for (int k = 0; k < sl.Count; k++) h = h * 31 + UrlOf(sl[k]).Length;
				}
				try { List<string> ids = a.GetStorableIDs(); h = h * 31 + (ids == null ? 0 : ids.Count); } catch { }
			}
			return h;
		}

		private void Reconcile(SuperController sc)
		{
			_scripts.Clear();
			_people.Clear();
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; atoms != null && i < atoms.Count; i++) if (Managed(atoms[i])) { _people.Add(atoms[i]); _female[atoms[i].uid] = IsFemale(atoms[i]); }
			if (!_people.Exists(delegate (Atom a) { return a.uid == _uid; })) _uid = _people.Count > 0 ? _people[0].uid : "";

			bool attachedOne = false;

			// the session-wide ZajeShuffle on CoreControl
			Atom core = sc.GetAtomByUid("CoreControl");
			if (core != null)
			{
				MVRPluginManager cm = FindManager(core);
				string zs = "CoreControl's plugin manager is not ready";
				if (cm != null) attachedOne = Apply(core, cm, Zaje, AnyWantsShuffle(), ref attachedOne, out zs);
				_status["Zaje"] = zs;
			}

			for (int i = 0; i < _people.Count; i++)
			{
				Atom a = _people[i];
				PersonCfg cfg = CfgFor(a.uid);
				MVRPluginManager mgr = FindManager(a);
				if (mgr == null) { _status[a.uid] = "VaM's plugin manager is not ready on " + a.uid; continue; }
				bool lite = cfg.Engine != "BodyLanguage";
				bool on = cfg.On && _installed;
				bool needBridge = on && (cfg.Fluids || cfg.Tremble || cfg.Shuffle || cfg.Wet || cfg.Wardrobe);
				StringBuilder st = new StringBuilder();
				string s;
				// engine first (everything else listens to it), then the hub, then the outputs
				bool wantFull = on && !lite && BodyLanguageFree(a);
				attachedOne = Apply(a, mgr, lite ? Lite : Full, on && (lite || wantFull), ref attachedOne, out s); st.AppendLine((lite ? Lite : Full).Label + ": " + s);
				if (!lite && on && !wantFull) st.AppendLine("  BodyLanguage is a single-instance script: another Person already runs it.");
				attachedOne = Apply(a, mgr, lite ? Full : Lite, false, ref attachedOne, out s);
				attachedOne = Apply(a, mgr, Sexy, on && cfg.Fluids, ref attachedOne, out s); st.AppendLine(Sexy.Label + ": " + s);
				attachedOne = Apply(a, mgr, Bridge, needBridge, ref attachedOne, out s); st.AppendLine(Bridge.Label + ": " + s);
				attachedOne = Apply(a, mgr, Tremble, on && cfg.Tremble, ref attachedOne, out s); st.AppendLine(Tremble.Label + ": " + s);
				Comp hud = lite ? LiteHud : FullHud;
				attachedOne = Apply(a, mgr, hud, on && cfg.Hud, ref attachedOne, out s); st.AppendLine(hud.Label + ": " + s);
				attachedOne = Apply(a, mgr, lite ? FullHud : LiteHud, false, ref attachedOne, out s);
				_status[a.uid] = st.ToString();
				PushBridgeSettings(a, cfg);
			}
			if (attachedOne) _nextScan = Time.realtimeSinceStartup + 0.8f;
		}

		// ZajeShuffle is one script for the whole session: it stays while any managed Person still wants a shuffle on orgasm
		private bool AnyWantsShuffle()
		{
			if (!_installed) return false;
			for (int i = 0; i < _people.Count; i++)
			{
				PersonCfg c = CfgFor(_people[i].uid);
				if (c.On && c.Shuffle) return true;
			}
			return false;
		}

		// BodyLanguage keeps a static singleton: one Person at a time. Whoever already has it keeps it; only when nobody does the
		// Person that sorts first among those asking gets it
		private bool BodyLanguageFree(Atom forAtom)
		{
			if (Instance(forAtom, Full) != null) return true;
			for (int i = 0; i < _people.Count; i++)
			{
				Atom o = _people[i];
				if (o != forAtom && Instance(o, Full) != null) return false;
			}
			for (int i = 0; i < _people.Count; i++)
			{
				Atom o = _people[i];
				if (o == forAtom) continue;
				PersonCfg c = CfgFor(o.uid);
				if (c.On && c.Engine == "BodyLanguage" && string.CompareOrdinal(o.uid, forAtom.uid) < 0) return false;
			}
			return true;
		}

		// attach / detach one component so it matches `want`; returns true when it changed something (one change per scan keeps load spikes apart)
		private bool Apply(Atom atom, MVRPluginManager mgr, Comp c, bool want, ref bool alreadyDidOne, out string status)
		{
			status = "";
			MVRPlugin slot = OurSlot(mgr, c);
			MVRScript inst = Instance(atom, c);
			bool ext = ExternalSlot(mgr, c);
			string key = atom.uid + "|" + c.Key;
			if (!want)
			{
				if (slot != null)
				{
					if (alreadyDidOne) { status = "waiting to detach"; return alreadyDidOne; }
					try
					{
						mgr.RemovePluginWithUID(slot.uid);
						_attachedAt.Remove(key);
						status = "detached";
						return true;
					}
					catch (Exception e) { status = "could not detach: " + e.Message; Logger.LogWarning("Orgasm Suite: detach " + c.Label + " on " + atom.uid + ": " + e.Message); }
				}
				else status = inst != null || ext ? "running (not ours, left alone)" : "off";
				return alreadyDidOne;
			}
			if (slot == null)
			{
				if (inst != null || ext) { status = "using the one already on " + atom.uid + " (not ours)"; return alreadyDidOne; }
				if (alreadyDidOne) { status = "queued"; return alreadyDidOne; }
				try
				{
					// logged BEFORE the call: if VaM dies inside it, this is the last line in the log
					Logger.LogInfo("Orgasm Suite: attaching " + c.Label + " to " + atom.uid + "...");
					MVRPlugin np = mgr.CreatePlugin();
					if (np == null || np.pluginURLJSON == null) throw new InvalidOperationException("VaM could not make a plugin slot");
					np.pluginURLJSON.val = c.Url;
					_attachedAt[key] = Time.realtimeSinceStartup;
					status = "attached, loading...";
					Logger.LogInfo("Orgasm Suite: attached " + c.Label + " to " + atom.uid);
					return true;
				}
				catch (Exception e)
				{
					status = "could not attach: " + e.Message;
					Logger.LogWarning("Orgasm Suite: attach " + c.Label + " on " + atom.uid + ": " + e.Message);
					return alreadyDidOne;
				}
			}
			if (inst == null)
			{
				float t;
				bool known = _attachedAt.TryGetValue(key, out t);
				if (!known) { _attachedAt[key] = Time.realtimeSinceStartup; t = Time.realtimeSinceStartup; }
				status = Time.realtimeSinceStartup - t > 25f ? "slot present but no script came up (compile error? see the VaM log)" : "loading...";
				return alreadyDidOne;
			}
			if (!inst.enabled) { try { inst.enabled = true; } catch { } }
			status = "running";
			return alreadyDidOne;
		}

		// the window's choices become FluidBridge's own switches (pushed once per change, so the plugin's own UI still works in between)
		private void PushBridgeSettings(Atom a, PersonCfg cfg)
		{
			try
			{
				MVRScript fb = Instance(a, Bridge);
				if (fb == null) return;
				string sig = (cfg.Fluids ? "1" : "0") + (cfg.Tremble ? "1" : "0") + (cfg.Shuffle ? "1" : "0") + (cfg.Wet ? "1" : "0") + (cfg.Wardrobe ? "1" : "0") + fb.GetInstanceID();
				string old;
				if (_pushed.TryGetValue(a.uid, out old) && old == sig) return;
				JSONStorableBool b = fb.GetBoolJSONParam("Use SexyFluids"); if (b != null) b.val = cfg.Fluids;
				b = fb.GetBoolJSONParam("AutoTremble on orgasm"); if (b != null) b.val = cfg.Tremble;
				b = fb.GetBoolJSONParam("ZajeShuffle on orgasm"); if (b != null) b.val = cfg.Shuffle;
				b = fb.GetBoolJSONParam("Apply Wet Clothes preset on orgasm"); if (b != null) b.val = cfg.Wet;
				b = fb.GetBoolJSONParam("Random clothing on orgasm"); if (b != null) b.val = cfg.Wardrobe;
				_pushed[a.uid] = sig;
			}
			catch { }
		}

		// ------------------------------------------------------------------ window
		private void SetMessage(string s) { _message = s; _messageUntil = Time.realtimeSinceStartup + 6.0; }

		private void SetScale(float v)
		{
			_scale.Value = Mathf.Clamp((float)Math.Round(v, 1), 0.5f, 2.5f);
			Config.Save();
		}

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

		private ZeroT.UiKit.RlImguiExternal _ext;
		private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();

		private void DrawExternal(float w, float h)
		{
			Rect panel = new Rect(0f, 0f, w, h);
			ZeroT.UiKit.RlChrome.Panel(panel);
			DrawWindow(panel, Mathf.Clamp(_scale.Value, 0.3f, 2.5f), false);
		}

		private void OnGUI()
		{
			if (_showWindow == null || !_showWindow.Value) return;
			if (_ext == null) _ext = new ZeroT.UiKit.RlImguiExternal(this, "Window", "Orgasm Suite");
			if (_ext.Run(DrawExternal)) return;
			float scale = DpiScale() * Mathf.Clamp(_scale.Value, 0.3f, 2.5f);
			bool collapsed = _collapsed.Value;
			float w = _win.width * scale;
			float h = (collapsed ? CollapsedH : _win.height) * scale;
			_home.Begin(ref _win);
			_win.x = Mathf.Clamp(_win.x, -(w - 60f), Mathf.Max(0f, Screen.width - 60f));
			_win.y = Mathf.Clamp(_win.y, 0f, Mathf.Max(0f, Screen.height - 24f));
			_home.End(ref _win);
			Rect panel = new Rect(_win.x, _win.y, w, h);
			ZeroT.UiKit.RlChrome.Panel(panel);
			DrawWindow(panel, scale, collapsed);
		}

		private static string Fit(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "~"; }

		private void DrawWindow(Rect panel, float s, bool collapsed)
		{
			float pad = 8f * s, lh = 22f * s;
			GUI.Label(new Rect(panel.x + pad, panel.y + 3f * s, panel.width - 118f * s, 22f * s), "Orgasm Suite");
			float bx = panel.xMax - 112f * s;
			if (GUI.Button(new Rect(bx, panel.y + 3f * s, 26f * s, 18f * s), "S-")) SetScale(_scale.Value - 0.1f);
			if (GUI.Button(new Rect(bx + 29f * s, panel.y + 3f * s, 26f * s, 18f * s), "S+")) SetScale(_scale.Value + 0.1f);
			if (GUI.Button(new Rect(bx + 58f * s, panel.y + 3f * s, 22f * s, 18f * s), collapsed ? "+" : "-")) { _collapsed.Value = !collapsed; Config.Save(); }
			if (GUI.Button(new Rect(bx + 83f * s, panel.y + 3f * s, 22f * s, 18f * s), "x")) { _showWindow.Value = false; Config.Save(); }
			HandleDragResize(panel, s, collapsed);
			if (collapsed) return;

			float x = panel.x + pad, w = panel.width - 2f * pad, y = panel.y + 30f * s;

			if (!_installed)
			{
				GUI.Label(new Rect(x, y, w, 60f * s), "The embedded scripts could not be written:\n" + _installNote);
				return;
			}

			// ---- person picker
			if (_people.Count == 0)
			{
				GUI.Label(new Rect(x, y, w, lh), "No Person atom yet.");
				y += lh + 4f * s;
			}
			else
			{
				int per = 3;
				int pages = (_people.Count + per - 1) / per;
				_page = Mathf.Clamp(_page, 0, Mathf.Max(0, pages - 1));
				float arrow = 24f * s;
				float tw = (w - (pages > 1 ? 2f * arrow + 4f * s : 0f)) / per;
				float tx = x;
				if (pages > 1)
				{
					if (GUI.Button(new Rect(tx, y, arrow, lh), "<")) _page = (_page + pages - 1) % pages;
					tx += arrow + 2f * s;
				}
				for (int i = 0; i < per; i++)
				{
					int idx = _page * per + i;
					if (idx >= _people.Count) break;
					Atom a = _people[idx];
					bool sel = a.uid == _uid;
					bool fem;
					string g = _female.TryGetValue(a.uid, out fem) && !fem ? "M " : "F ";
					if (GUI.Button(new Rect(tx + i * tw, y, tw - 2f * s, lh), (sel ? "[" : "") + g + Fit(a.uid, 9) + (sel ? "]" : "")))
					{
						_uid = a.uid; _lastUid.Value = _uid; Config.Save();
					}
				}
				if (pages > 1 && GUI.Button(new Rect(x + w - arrow, y, arrow, lh), ">")) _page = (_page + 1) % pages;
				y += lh + 6f * s;
			}

			PersonCfg cfg = string.IsNullOrEmpty(_uid) ? null : CfgFor(_uid);
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && cfg != null;
			bool changed = false;

			// ---- master
			float third = (w - 4f * s) / 3f;
			if (GUI.Button(new Rect(x, y, third, lh), "Suite: " + (cfg != null && cfg.On ? "ON" : "OFF"))) { cfg.On = !cfg.On; changed = true; }
			if (GUI.Button(new Rect(x + third + 2f * s, y, third, lh), "All ON")) { foreach (Atom a in _people) CfgFor(a.uid).On = true; changed = true; }
			if (GUI.Button(new Rect(x + 2f * (third + 2f * s), y, third, lh), "All OFF")) { foreach (Atom a in _people) CfgFor(a.uid).On = false; changed = true; }
			y += lh + 6f * s;

			// ---- mode
			GUI.Label(new Rect(x, y, w, lh), "Mode");
			y += lh;
			float ew = (w - 4f * s) / 2f;
			bool isLite = cfg == null || cfg.Engine != "BodyLanguage";
			if (GUI.Button(new Rect(x, y, ew, lh), isLite ? "[ Lite ]" : "Lite") && cfg != null && !isLite) { cfg.Engine = "Lite"; changed = true; }
			if (GUI.Button(new Rect(x + ew + 4f * s, y, ew, lh), !isLite ? "[ BodyLanguage ]" : "BodyLanguage") && cfg != null && isLite) { cfg.Engine = "BodyLanguage"; changed = true; }
			y += lh + 6f * s;

			// ---- what happens during an orgasm: one switch per module
			float hw = (w - 4f * s) / 2f;
			GUI.Label(new Rect(x, y, w, lh), "During orgasm");
			y += lh;
			if (GUI.Button(new Rect(x, y, hw, lh), Box(cfg != null && cfg.Tremble) + "AutoTremble") && cfg != null) { cfg.Tremble = !cfg.Tremble; changed = true; }
			if (GUI.Button(new Rect(x + hw + 4f * s, y, hw, lh), Box(cfg != null && cfg.Shuffle) + "ZajeShuffle") && cfg != null) { cfg.Shuffle = !cfg.Shuffle; changed = true; }
			y += lh + 2f * s;
			if (GUI.Button(new Rect(x, y, hw, lh), Box(cfg != null && cfg.Fluids) + "SexyFluids") && cfg != null) { cfg.Fluids = !cfg.Fluids; changed = true; }
			if (GUI.Button(new Rect(x + hw + 4f * s, y, hw, lh), Box(cfg != null && cfg.Wet) + "Wet clothes") && cfg != null) { cfg.Wet = !cfg.Wet; changed = true; }
			y += lh + 2f * s;
			if (GUI.Button(new Rect(x, y, hw, lh), Box(cfg != null && cfg.Wardrobe) + "Wardrobe") && cfg != null) { cfg.Wardrobe = !cfg.Wardrobe; changed = true; }
			string hudName = isLite ? "Arousal HUD" : "Stimulation HUD";
			if (GUI.Button(new Rect(x + hw + 4f * s, y, hw, lh), Box(cfg != null && cfg.Hud) + hudName) && cfg != null) { cfg.Hud = !cfg.Hud; changed = true; }
			y += lh + 4f * s;

			// ---- test buttons (act on the selected Person)
			GUI.enabled = enabled && cfg != null && cfg.On;
			if (GUI.Button(new Rect(x, y, hw, lh), "Orgasm now")) {
				// the engine's own orgasm (arousal reset, its triggers), then the bridge chain: a forced orgasm at low arousal is not a threshold crossing FluidBridge could see
				TriggerAction(isLite ? Lite : Full, isLite ? "Force Orgasm" : "Orgasm Now", "Orgasm");
				TriggerAction(Bridge, "Test Orgasm Fluids", "Orgasm");
			}
			if (GUI.Button(new Rect(x + hw + 4f * s, y, hw, lh), "Test fluids")) TriggerAction(Bridge, "Test Orgasm Fluids", "Test fluids");
			GUI.enabled = enabled;
			y += lh + 8f * s;

			if (changed) { SavePeople(); _nextScan = 0f; _pushed.Clear(); }

			// ---- status
			float footer = 40f * s;
			float sh = Mathf.Max(40f * s, panel.yMax - y - footer - 6f * s);
			string text = "";
			if (!string.IsNullOrEmpty(_uid))
			{
				string st;
				if (_status.TryGetValue(_uid, out st)) text = st;
			}
			string zs;
			if (_status.TryGetValue("Zaje", out zs)) text += "ZajeShuffle (CoreControl): " + zs + "\n";
			if (!isLite && cfg != null && !BodyLanguageResourcesPresent()) text += "BodyLanguage needs the CheesyFX.BodyLanguage_Resources package.\n";
			Rect view = new Rect(x, y, w, sh);
			GUI.Box(view, "");
			GUI.Label(new Rect(view.x + 4f * s, view.y + 2f * s, view.width - 8f * s, view.height - 4f * s), text.Length == 0 ? "Waiting for a Person atom..." : text.TrimEnd());
			y += sh + 4f * s;

			string msg = Time.realtimeSinceStartup < _messageUntil ? _message : "";
			GUI.Label(new Rect(x, y, w, 36f * s), msg.Length > 0 ? msg : (_waitNote.Length > 0 ? _waitNote : "Scripts: " + _installNote));
			// resize grip is drawn by HandleDragResize
		}

		// press a script's own action on the selected Person (the engine's orgasm, FluidBridge's test)
		private void TriggerAction(Comp c, string action, string what)
		{
			try
			{
				Atom a = SuperController.singleton == null ? null : SuperController.singleton.GetAtomByUid(_uid);
				MVRScript m = a == null ? null : Instance(a, c);
				if (m == null) { SetMessage(what + ": " + c.Label + " is not running on " + _uid + " yet."); return; }
				JSONStorableAction act = m.GetAction(action);
				if (act == null || act.actionCallback == null) { SetMessage(what + ": " + c.Label + " has no '" + action + "' action."); return; }
				act.actionCallback.Invoke();
				SetMessage(what + " fired on " + _uid + ".");
			}
			catch (Exception e) { SetMessage(what + " failed: " + e.Message); Logger.LogWarning("Orgasm Suite: " + what + ": " + e.Message); }
		}

		private static string Box(bool on) { return on ? "[x] " : "[ ] "; }

		private bool BodyLanguageResourcesPresent()
		{
			try
			{
				List<string> all = FileManager.GetPackageUids();
				for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].StartsWith("CheesyFX.BodyLanguage_Resources.")) return true;
			}
			catch { }
			return false;
		}

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
