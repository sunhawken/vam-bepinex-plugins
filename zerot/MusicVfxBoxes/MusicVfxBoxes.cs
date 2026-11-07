using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.MusicVfxBoxes
{
	// The seven UIAssist boxes for the music / light-show kit (Saves/PluginData/JayJayWon/UIAssist/default.1.uiap), as one window:
	//
	//   Music On / Off   Plugin Action Toggle on the _Music atom: VamEssentials.AudioRouter "PlayRandom [UIList]" / "Stop [UIList]"
	//   Music            selects _Music
	//   _Audio           spawns the UISlider _AudioS (Custom/Atom/UISlider/Preset_Audio.vap): its text is the song name, bound by 14mhz UIBinder to _Music's AudioRouter "Audio File"
	//   _Music           spawns the AudioSource _Music (Custom/Atom/AudioSource/Preset_Audio.vap): AudioRouter (plays Custom/Sounds/me), AudioScrubber (drives the _AudioS slider), BeatIt, DopeLer
	//   _LightVFX        spawns the InvisibleLight _LightVFX (Preset_SpiralLight.vap) and adds Dragontales Light Atom Excluder
	//   FlyObject        deletes the old FlyObject, spawns an Empty "FlyObject" with Feel.AutoFlyObject: it makes the atom whose name contains "_LightVFX" orbit the first Person
	//   Delete Fly Objects
	//
	// The "M" in the UIAssist target names (M_Music, MFlyObject) is UIAssist's own prefix for the main choice of a multi-enum (T... top, M... main, B... bottom);
	// the atoms are really called _Music and FlyObject, so spawn and delete / select / toggle all talk about the same atoms.
	//
	// Spawning follows UIAssist step by step: a free atom name, AddAtomByType, move to the spawn point, wait a frame, load the preset through the atom's
	// "Preset" storable, then add the extra plugin(s) through the atom's plugin manager. Plugin versions are resolved to the newest installed .var
	// (UIAssist's "use latest VAR"). Optional wiring (on by default): a box whose partner atom is missing spawns the partner first.
	[BepInPlugin("com.zerot.musicvfxboxes", "Music and VFX Boxes", "1.0.0")]
	public sealed class MusicVfxBoxesPlugin : BaseUnityPlugin
	{
		private const string RouterType = "VamEssentials.AudioRouter";

		private sealed class Spec
		{
			public string Key;                     // music / slider / light / fly
			public string AtomType;
			public ConfigEntry<string> Name;
			public ConfigEntry<string> Preset;     // .vap, empty = none
			public string[] Plugins;               // extra plugins added after the preset
			public bool AtHead;                    // spawn point: VR head (else scene origin)
			public bool Select;
			public bool DeleteFirst;               // delete the old atom of this name first
			public bool OpenUi;
		}

		private ConfigEntry<bool> cfgPartners;
		private ConfigEntry<bool> cfgLatest;
		private ConfigEntry<string> cfgLightPlugin;
		private ConfigEntry<string> cfgFlyPlugin;
		private Spec music, slider, light, fly;

		private RlUguiWindow win;
		private Text status;
		private Button bMusicToggle, bMusicSelect, bSlider, bMusic, bLight, bFly, bDeleteFly;
		private bool busy;
		private string message = "";

		// the plugins on the four atoms: one row each, a window page at a time
		private const int RowCount = 8;
		private sealed class PluginRow
		{
			public JSONStorable Storable;
			public string Label;
			public string Kind;        // router / fly / other
		}
		private int viewAtom;          // 0 music, 1 slider, 2 light, 3 fly
		private int page;
		private List<PluginRow> rows = new List<PluginRow>();
		private readonly Button[] rowMain = new Button[RowCount];
		private readonly Button[] rowA = new Button[RowCount];
		private readonly Button[] rowB = new Button[RowCount];
		private readonly Button[] rowC = new Button[RowCount];
		private readonly Button[] rowD = new Button[RowCount];
		private Button bPagePrev5, bPageNext5;

		// detail view: every parameter of one plugin
		private sealed class ParamRow
		{
			public string Name;
			public string Kind;        // bool / float / choice / action / text
		}
		private PluginRow detailRow;
		private List<ParamRow> paramRows = new List<ParamRow>();
		private Button bAtomPrev, bAtomNext, bAllOn, bAllOff, bPagePrev, bPageNext;
		private Text atomLabel, pageLabel;
		private float nextUi;

		// installed packages (name -> newest version), refreshed now and then
		private Dictionary<string, int> packages;
		private float packagesAt = -999f;

		private void Awake()
		{
			cfgPartners = Config.Bind<bool>("Wiring", "SpawnMissingPartners", true, "A box whose partner atom is missing spawns it first: _Audio needs _Music (its text is bound to _Music's AudioRouter), FlyObject needs _LightVFX (AutoFlyObject flies the atom named _LightVFX).");
			cfgLatest = Config.Bind<bool>("Wiring", "UseLatestPackageVersion", true, "Plugin paths like Pkg.5:/... are replaced with the newest installed version of that package (UIAssist's 'use latest VAR').");
			cfgLightPlugin = Config.Bind<string>("Plugins", "LightExtra", "Dragontales.Light_Atom_Excluder.12:/Custom/Scripts/Light Atom Excluder/Light Atom Excluder.cs", "Plugin added to the spawned _LightVFX after its preset (empty = none).");
			cfgFlyPlugin = Config.Bind<string>("Plugins", "FlyObject", "Feel.AutoFlyObject.5:/Custom/Scripts/Feel/AutoFlyObject.cslist", "Plugin added to the spawned FlyObject.");
			music = new Spec { Key = "music", AtomType = "AudioSource", AtHead = true, Select = true };
			music.Name = Config.Bind<string>("Atoms", "Music", "_Music", "Name of the AudioSource atom.");
			music.Preset = Config.Bind<string>("Presets", "Music", "Custom/Atom/AudioSource/Preset_Audio.vap", "Atom preset loaded into the new music atom.");
			slider = new Spec { Key = "slider", AtomType = "UISlider" };
			slider.Name = Config.Bind<string>("Atoms", "Slider", "_AudioS", "Name of the UISlider that shows the song name.");
			slider.Preset = Config.Bind<string>("Presets", "Slider", "Custom/Atom/UISlider/Preset_Audio.vap", "Atom preset loaded into the new slider atom.");
			light = new Spec { Key = "light", AtomType = "InvisibleLight", Select = true };
			light.Name = Config.Bind<string>("Atoms", "Light", "_LightVFX", "Name of the light atom (AutoFlyObject looks for a name containing this).");
			light.Preset = Config.Bind<string>("Presets", "Light", "Custom/Atom/InvisibleLight/Preset_SpiralLight.vap", "Atom preset loaded into the new light atom.");
			fly = new Spec { Key = "fly", AtomType = "Empty", AtHead = true, Select = true, DeleteFirst = true, OpenUi = true };
			fly.Name = Config.Bind<string>("Atoms", "Fly", "FlyObject", "Name of the Empty atom that carries AutoFlyObject.");
			fly.Preset = Config.Bind<string>("Presets", "Fly", "", "Atom preset loaded into the new fly atom (UIAssist uses none).");
			win = new RlUguiWindow(this, "Music and VFX", "Window", 40f, 60f, 360f, 520f, 300f, 360f, 800f, 900f);
		}

		private IEnumerator Start()
		{
			while (SuperController.singleton == null || EventSystem.current == null)
			{
				yield return null;
			}
			BuildWindow();
		}

		private void OnDestroy()
		{
			if (win != null)
			{
				win.Dispose();
			}
		}

		private void OnDisable()
		{
			if (win != null)
			{
				win.SetActive(false);
			}
		}

		private void OnEnable()
		{
			if (win != null)
			{
				win.SetActive(true);
			}
		}

		private void Update()
		{
			if (win == null || win.Window == null)
			{
				return;
			}
			win.Tick();
			if (Time.unscaledTime >= nextUi)
			{
				nextUi = Time.unscaledTime + 0.5f;
				Refresh();
			}
		}

		// ---------------------------------------------------------------- atoms

		private static SuperController Sc
		{
			get { return SuperController.singleton; }
		}

		private Atom Find(Spec s)
		{
			return Sc == null ? null : Sc.GetAtomByUid(s.Name.Value);
		}

		private string Unique(string name)
		{
			if (Sc.GetAtomByUid(name) == null)
			{
				return name;
			}
			for (int i = 2; i < 1000; i++)
			{
				string n = name + "#" + i;
				if (Sc.GetAtomByUid(n) == null)
				{
					return n;
				}
			}
			return name + "#" + DateTime.Now.Ticks;
		}

		private static bool Named(Atom a, string name)
		{
			return a != null && (a.uid == name || a.uid.StartsWith(name + "#"));
		}

		private int DeleteAtoms(Spec s)
		{
			int n = 0;
			List<Atom> all = new List<Atom>(Sc.GetAtoms());
			for (int i = 0; i < all.Count; i++)
			{
				Atom a = all[i];
				if (Named(a, s.Name.Value) && a.type == s.AtomType)
				{
					try
					{
						Sc.RemoveAtom(a);
						n++;
					}
					catch (Exception e)
					{
						Logger.LogWarning("Could not delete " + a.uid + ": " + e.Message);
					}
				}
			}
			return n;
		}

		private void Select(Atom a)
		{
			try
			{
				if (a != null && a.mainController != null)
				{
					Sc.SelectController(a.mainController);
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not select " + a.uid + ": " + e.Message);
			}
		}

		// ---------------------------------------------------------------- music on / off

		private List<JSONStorable> Routers(Atom a)
		{
			List<JSONStorable> list = new List<JSONStorable>();
			if (a == null)
			{
				return list;
			}
			List<string> ids = a.GetStorableIDs();
			for (int i = 0; i < ids.Count; i++)
			{
				if (ids[i].StartsWith("plugin#") && ids[i].IndexOf(RouterType, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					JSONStorable s = a.GetStorableByID(ids[i]);
					if (s != null)
					{
						list.Add(s);
					}
				}
			}
			return list;
		}

		private bool MusicPlaying()
		{
			Atom a = Find(music);
			if (a == null)
			{
				return false;
			}
			AudioSource src = a.GetComponentInChildren<AudioSource>(true);
			return src != null && src.isPlaying;
		}

		private void MusicToggle()
		{
			Atom a = Find(music);
			if (a == null)
			{
				Say("There is no " + music.Name.Value + " atom: press the _Music box first.");
				return;
			}
			List<JSONStorable> routers = Routers(a);
			if (routers.Count == 0)
			{
				Say(a.uid + " has no AudioRouter plugin (is its preset loaded?).");
				return;
			}
			bool on = !MusicPlaying();
			for (int i = 0; i < routers.Count; i++)
			{
				JSONStorable r = routers[i];
				try
				{
					JSONStorableBool enabled = r.GetBoolJSONParam("enabled");
					if (enabled != null)
					{
						enabled.val = true;
					}
					if (on)
					{
						JSONStorableStringChooser mode = r.GetStringChooserJSONParam("Mode");
						if (mode != null)
						{
							mode.val = "PlayNow";
						}
						r.CallAction("PlayRandom [UIList]");
					}
					else
					{
						r.CallAction("Stop [UIList]");
					}
				}
				catch (Exception e)
				{
					Logger.LogWarning("AudioRouter action failed: " + e.Message);
				}
			}
			Say(on ? "Music: playing a random song from the UIList." : "Music: stopped.");
		}

		// ---------------------------------------------------------------- spawning

		private void Spawn(Spec s)
		{
			if (busy)
			{
				Say("Still spawning, one moment.");
				return;
			}
			StartCoroutine(SpawnAll(s));
		}

		private Spec PartnerOf(Spec s)
		{
			if (!cfgPartners.Value)
			{
				return null;
			}
			if (s == slider && Find(music) == null)
			{
				return music;
			}
			if (s == fly && Find(light) == null)
			{
				return light;
			}
			return null;
		}

		private IEnumerator SpawnAll(Spec s)
		{
			busy = true;
			Spec partner = PartnerOf(s);
			if (partner != null)
			{
				Say("Spawning " + partner.Name.Value + " first (" + s.Name.Value + " needs it).");
				yield return StartCoroutine(SpawnOne(partner));
			}
			yield return StartCoroutine(SpawnOne(s));
			busy = false;
		}

		// the UIAssist spawn: free name, AddAtomByType, spawn point, one frame, preset, plugins, select
		private IEnumerator SpawnOne(Spec s)
		{
			string wanted = s.Name.Value;
			if (s.DeleteFirst)
			{
				DeleteAtoms(s);
				yield return null;
			}
			string uid = Unique(wanted);
			IEnumerator add = Sc.AddAtomByType(s.AtomType, uid);
			bool failed = false;
			while (true)
			{
				bool more;
				try
				{
					more = add != null && add.MoveNext();
				}
				catch (Exception e)
				{
					Logger.LogWarning("Creating " + uid + " failed: " + e.Message);
					failed = true;
					break;
				}
				if (!more)
				{
					break;
				}
				yield return add.Current;
			}
			Atom atom = failed ? null : Sc.GetAtomByUid(uid);
			if (atom == null)
			{
				Say("Could not create the " + s.AtomType + " atom " + uid + ".");
				yield break;
			}
			try
			{
				Place(atom, s.AtHead);
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not move " + uid + ": " + e.Message);
			}
			yield return new WaitForEndOfFrame();
			string missing = "";
			try
			{
				string preset = (s.Preset.Value ?? "").Trim();
				if (preset.Length > 0)
				{
					missing = CheckPreset(preset);
					LoadPreset(atom, preset);
				}
				string[] plugins = s.Key == "light" ? new string[] { cfgLightPlugin.Value } : (s.Key == "fly" ? new string[] { cfgFlyPlugin.Value } : new string[0]);
				for (int i = 0; i < plugins.Length; i++)
				{
					AddPlugin(atom, plugins[i]);
				}
				if (s.Select)
				{
					Select(atom);
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Setting up " + uid + " failed: " + e);
				Say("Setting up " + uid + " failed: " + e.Message);
				yield break;
			}
			Say("Spawned " + uid + (missing.Length > 0 ? "  (missing packages: " + missing + ")" : "."));
			Logger.LogInfo("Spawned " + uid + " (" + s.AtomType + ")" + (missing.Length > 0 ? ", missing packages: " + missing : ""));
		}

		private void Place(Atom atom, bool atHead)
		{
			if (atom.mainController == null)
			{
				return;
			}
			Vector3 pos = Vector3.zero;
			Quaternion rot = Quaternion.identity;
			if (atHead && Sc.centerCameraTarget != null)
			{
				Transform t = Sc.centerCameraTarget.transform;
				pos = t.position;
				rot = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
			}
			atom.mainController.transform.position = pos;
			atom.mainController.transform.rotation = rot;
		}

		// loads a .vap into the atom through its "Preset" storable, like the browser does
		private void LoadPreset(Atom atom, string preset)
		{
			string full = Path.Combine(Paths.GameRootPath, preset.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(full))
			{
				Say("Preset not found: " + preset);
				return;
			}
			JSONStorable js = atom.GetStorableByID("Preset");
			if (js == null)
			{
				Logger.LogWarning(atom.uid + " has no Preset storable.");
				return;
			}
			JSONStorableBool loadOnSelect = js.GetBoolJSONParam("loadPresetOnSelect");
			bool preSelect = loadOnSelect != null && loadOnSelect.val;
			if (loadOnSelect != null)
			{
				loadOnSelect.val = false;
			}
			JSONStorableUrl url = js.GetUrlJSONParam("presetBrowsePath");
			string prePath = url != null ? url.val : null;
			if (url != null)
			{
				url.val = Sc.NormalizePath(preset);
			}
			js.CallAction("LoadPreset");
			if (url != null)
			{
				url.val = prePath;
			}
			if (loadOnSelect != null)
			{
				loadOnSelect.val = preSelect;
			}
		}

		private static MVRPluginManager ManagerOf(Atom atom)
		{
			MVRPluginManager m = atom.GetStorableByID("PluginManager") as MVRPluginManager;
			if (m == null)
			{
				m = atom.GetStorableByID("plugin") as MVRPluginManager;
			}
			if (m == null)
			{
				MVRPluginManager[] all = atom.gameObject.GetComponentsInChildren<MVRPluginManager>(true);
				m = all.Length > 0 ? all[0] : null;
			}
			return m;
		}

		private void AddPlugin(Atom atom, string path)
		{
			path = (path ?? "").Trim();
			if (path.Length == 0)
			{
				return;
			}
			if (cfgLatest.Value)
			{
				path = Latest(path);
			}
			MVRPluginManager mgr = ManagerOf(atom);
			if (mgr == null)
			{
				Say(atom.uid + " has no plugin manager yet.");
				return;
			}
			MVRPlugin p = mgr.CreatePlugin();
			if (p == null || p.pluginURLJSON == null)
			{
				Say("Could not create a plugin slot on " + atom.uid + ".");
				return;
			}
			((JSONStorableString)p.pluginURLJSON).val = path;
		}

		// ---------------------------------------------------------------- packages

		private static readonly Regex VarName = new Regex(@"^(.+)\.(\d+)\.var$", RegexOptions.IgnoreCase);
		private static readonly Regex PkgRef = new Regex(@"^(.+)\.(\d+|latest):/(.*)$", RegexOptions.IgnoreCase);

		private Dictionary<string, int> Packages()
		{
			if (packages != null && Time.realtimeSinceStartup - packagesAt < 30f)
			{
				return packages;
			}
			packagesAt = Time.realtimeSinceStartup;
			Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			try
			{
				string root = Path.Combine(Paths.GameRootPath, "AddonPackages");
				if (Directory.Exists(root))
				{
					string[] files = Directory.GetFiles(root, "*.var", SearchOption.AllDirectories);
					for (int i = 0; i < files.Length; i++)
					{
						Match m = VarName.Match(Path.GetFileName(files[i]));
						if (!m.Success)
						{
							continue;
						}
						int v;
						if (!int.TryParse(m.Groups[2].Value, out v))
						{
							continue;
						}
						int old;
						if (!d.TryGetValue(m.Groups[1].Value, out old) || v > old)
						{
							d[m.Groups[1].Value] = v;
						}
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not list AddonPackages: " + e.Message);
			}
			packages = d;
			return d;
		}

		// Pkg.5:/x -> Pkg.<newest installed>:/x
		private string Latest(string path)
		{
			Match m = PkgRef.Match(path);
			if (!m.Success)
			{
				return path;
			}
			int v;
			if (Packages().TryGetValue(m.Groups[1].Value, out v))
			{
				return m.Groups[1].Value + "." + v + ":/" + m.Groups[3].Value;
			}
			return path;
		}

		// the plugins a .vap loads: every package it names must be installed (any version); returns the missing ones, comma separated
		private string CheckPreset(string preset)
		{
			string full = Path.Combine(Paths.GameRootPath, preset.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(full))
			{
				return "";
			}
			StringBuilder sb = new StringBuilder();
			HashSet<string> seen = new HashSet<string>();
			try
			{
				string text = File.ReadAllText(full);
				foreach (Match m in Regex.Matches(text, "\"plugin#\\d+\"\\s*:\\s*\"([^\"]+)\""))
				{
					string path = m.Groups[1].Value;
					Match r = PkgRef.Match(path);
					if (r.Success)
					{
						int v;
						if (!Packages().TryGetValue(r.Groups[1].Value, out v) && seen.Add(r.Groups[1].Value))
						{
							sb.Append(sb.Length > 0 ? ", " : "").Append(r.Groups[1].Value);
						}
					}
					else if (!File.Exists(Path.Combine(Paths.GameRootPath, path.Replace('/', Path.DirectorySeparatorChar))) && seen.Add(path))
					{
						sb.Append(sb.Length > 0 ? ", " : "").Append(path);
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not read " + preset + ": " + e.Message);
			}
			return sb.ToString();
		}

		// ---------------------------------------------------------------- window

		private void Say(string m)
		{
			message = m;
			Refresh();
		}

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h) { LayoutBody(w, h); };
			win.Build("Music and VFX Boxes Desktop");
			status = RlUguiWindow.NewText("Status", win.Body, "", TextAnchor.UpperLeft);
			bMusicToggle = RlUguiWindow.NewButton("Music toggle", win.Body, "Music Off", delegate { MusicToggle(); });
			bMusicSelect = RlUguiWindow.NewButton("Music select", win.Body, "Music", delegate { Select(Find(music)); });
			bSlider = RlUguiWindow.NewButton("Audio slider", win.Body, "_Audio", delegate { Spawn(slider); });
			bMusic = RlUguiWindow.NewButton("Music atom", win.Body, "_Music", delegate { Spawn(music); });
			bLight = RlUguiWindow.NewButton("Light VFX", win.Body, "_LightVFX", delegate { Spawn(light); });
			bFly = RlUguiWindow.NewButton("FlyObject", win.Body, "FlyObject", delegate { Spawn(fly); });
			bDeleteFly = RlUguiWindow.NewButton("Delete fly objects", win.Body, "Delete Fly Objects", delegate
			{
				int n = DeleteAtoms(fly);
				Say(n > 0 ? "Deleted " + n + " fly object" + (n == 1 ? "." : "s.") : "No " + fly.Name.Value + " atom to delete.");
			});
			bAtomPrev = RlUguiWindow.NewButton("Atom prev", win.Body, "<", delegate { StepAtom(-1); });
			bAtomNext = RlUguiWindow.NewButton("Atom next", win.Body, ">", delegate { StepAtom(1); });
			atomLabel = RlUguiWindow.NewText("Atom", win.Body, "", TextAnchor.MiddleCenter);
			bAllOn = RlUguiWindow.NewButton("All on", win.Body, "All on", delegate { if (detailRow != null) { detailRow = null; page = 0; Refresh(); } else { SetAll(true); } });
			bAllOff = RlUguiWindow.NewButton("All off", win.Body, "All off", delegate { SetAll(false); });
			for (int i = 0; i < RowCount; i++)
			{
				int k = i;
				rowMain[i] = RlUguiWindow.NewButton("Plugin " + i, win.Body, "", delegate { CellMain(k); });
				rowA[i] = RlUguiWindow.NewButton("Plugin " + i + " A", win.Body, "", delegate { Cell(k, 0); });
				rowB[i] = RlUguiWindow.NewButton("Plugin " + i + " B", win.Body, "", delegate { Cell(k, 1); });
				rowC[i] = RlUguiWindow.NewButton("Plugin " + i + " C", win.Body, "", delegate { Cell(k, 2); });
				rowD[i] = RlUguiWindow.NewButton("Plugin " + i + " D", win.Body, "", delegate { Cell(k, 3); });
			}
			bPagePrev = RlUguiWindow.NewButton("Page prev", win.Body, "<", delegate { page = Mathf.Max(0, page - 1); Refresh(); });
			bPageNext = RlUguiWindow.NewButton("Page next", win.Body, ">", delegate { page++; Refresh(); });
			bPagePrev5 = RlUguiWindow.NewButton("Page prev 5", win.Body, "<<", delegate { page = Mathf.Max(0, page - 5); Refresh(); });
			bPageNext5 = RlUguiWindow.NewButton("Page next 5", win.Body, ">>", delegate { page += 5; Refresh(); });
			pageLabel = RlUguiWindow.NewText("Page", win.Body, "", TextAnchor.MiddleCenter);
			win.AddResizeHandle();
			win.Apply();
			Refresh();
		}

		private Spec ViewSpec()
		{
			return viewAtom == 0 ? music : (viewAtom == 1 ? slider : (viewAtom == 2 ? light : fly));
		}

		private void StepAtom(int d)
		{
			if (detailRow != null)
			{
				StepPlugin(d);
				return;
			}
			viewAtom = ((viewAtom + d) % 4 + 4) % 4;
			page = 0;
			Refresh();
		}

		private void StepPlugin(int d)
		{
			List<PluginRow> all = RowsFor(Find(ViewSpec()));
			int at = -1;
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].Storable == detailRow.Storable)
				{
					at = i;
				}
			}
			if (all.Count == 0)
			{
				return;
			}
			detailRow = all[((at + d) % all.Count + all.Count) % all.Count];
			page = 0;
			Refresh();
		}

		private static List<ParamRow> ParamsOf(JSONStorable s)
		{
			List<ParamRow> list = new List<ParamRow>();
			AddParams(list, s.GetBoolParamNames(), "bool");
			AddParams(list, s.GetFloatParamNames(), "float");
			AddParams(list, s.GetStringChooserParamNames(), "choice");
			AddParams(list, s.GetActionNames(), "action");
			List<string> texts = new List<string>();
			foreach (string n in s.GetStringParamNames())
			{
				if (s.GetStringChooserJSONParam(n) == null && s.GetUrlJSONParam(n) == null)
				{
					texts.Add(n);
				}
			}
			AddParams(list, texts, "text");
			return list;
		}

		private static void AddParams(List<ParamRow> list, List<string> names, string kind)
		{
			if (names == null)
			{
				return;
			}
			List<string> sorted = new List<string>(names);
			sorted.Sort(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < sorted.Count; i++)
			{
				ParamRow r = new ParamRow();
				r.Name = sorted[i];
				r.Kind = kind;
				list.Add(r);
			}
		}

		private ParamRow ParamAt(int slot)
		{
			int i = page * Mathf.Max(1, visibleRows) + slot;
			return i >= 0 && i < paramRows.Count ? paramRows[i] : null;
		}

		private static float Step(JSONStorableFloat f, bool coarse)
		{
			float range = f.max - f.min;
			if (range <= 0f || range > 1000000f)
			{
				range = 1f;
			}
			return range / (coarse ? 10f : 100f);
		}

		private static void Nudge(JSONStorableFloat f, float delta)
		{
			float v = f.val + delta;
			f.val = f.constrained ? Mathf.Clamp(v, f.min, f.max) : v;
		}

		private static void Cycle(JSONStorableStringChooser c, int d)
		{
			List<string> ch = c.choices;
			if (ch == null || ch.Count == 0)
			{
				return;
			}
			int at = ch.IndexOf(c.val);
			c.val = ch[((at + d) % ch.Count + ch.Count) % ch.Count];
		}

		private void CellMain(int slot)
		{
			if (detailRow == null)
			{
				Toggle(RowAt(slot));
				return;
			}
			ParamRow pr = ParamAt(slot);
			JSONStorable s = detailRow.Storable;
			if (pr == null || s == null)
			{
				return;
			}
			try
			{
				if (pr.Kind == "bool")
				{
					JSONStorableBool b = s.GetBoolJSONParam(pr.Name);
					if (b != null)
					{
						b.val = !b.val;
					}
				}
				else if (pr.Kind == "float")
				{
					JSONStorableFloat f = s.GetFloatJSONParam(pr.Name);
					if (f != null)
					{
						f.val = f.defaultVal;       // the label resets to the default
					}
				}
				else if (pr.Kind == "choice")
				{
					JSONStorableStringChooser c = s.GetStringChooserJSONParam(pr.Name);
					if (c != null)
					{
						Cycle(c, 1);
					}
				}
				else if (pr.Kind == "action")
				{
					s.CallAction(pr.Name);
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(detailRow.Label + " / " + pr.Name + ": " + ex.Message);
			}
			Refresh();
		}

		// the four small buttons of a row: plugin list = extras / Edit; detail = float steps (<< < > >>) or choice (< >)
		private void Cell(int slot, int which)
		{
			if (detailRow == null)
			{
				PluginRow r = RowAt(slot);
				if (which == 3)
				{
					if (r != null)
					{
						detailRow = r;
						page = 0;
						Refresh();
					}
				}
				else if (which < 2)
				{
					Extra(r, which);
				}
				return;
			}
			ParamRow pr = ParamAt(slot);
			JSONStorable s = detailRow.Storable;
			if (pr == null || s == null)
			{
				return;
			}
			try
			{
				if (pr.Kind == "float")
				{
					JSONStorableFloat f = s.GetFloatJSONParam(pr.Name);
					if (f != null)
					{
						float[] d = { -1f, -1f, 1f, 1f };
						Nudge(f, d[which] * Step(f, which == 0 || which == 3));
					}
				}
				else if (pr.Kind == "choice")
				{
					JSONStorableStringChooser c = s.GetStringChooserJSONParam(pr.Name);
					if (c != null)
					{
						Cycle(c, which == 1 ? -1 : 1);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(detailRow.Label + " / " + pr.Name + ": " + ex.Message);
			}
			Refresh();
		}

		private PluginRow RowAt(int slot)
		{
			int i = page * RowCount + slot;
			return i >= 0 && i < rows.Count ? rows[i] : null;
		}

		// ---------------------------------------------------------------- the plugins on an atom

		private static string Short(string id)
		{
			int us = id.IndexOf('_');
			string name = us >= 0 ? id.Substring(us + 1) : id;
			int dot = name.LastIndexOf('.');
			return dot >= 0 ? name.Substring(dot + 1) : name;
		}

		private static int PluginNumber(string id)
		{
			int us = id.IndexOf('_');
			string n = id.Substring(7, (us > 7 ? us : id.Length) - 7);
			int v;
			return int.TryParse(n, out v) ? v : 0;
		}

		private List<PluginRow> RowsFor(Atom a)
		{
			List<PluginRow> list = new List<PluginRow>();
			if (a == null)
			{
				return list;
			}
			List<string> ids = new List<string>();
			foreach (string id in a.GetStorableIDs())
			{
				if (id.StartsWith("plugin#"))
				{
					ids.Add(id);
				}
			}
			ids.Sort(delegate(string x, string y) { return PluginNumber(x).CompareTo(PluginNumber(y)); });
			Dictionary<string, int> seen = new Dictionary<string, int>();
			Dictionary<string, int> total = new Dictionary<string, int>();
			for (int i = 0; i < ids.Count; i++)
			{
				string s = Short(ids[i]);
				total[s] = total.ContainsKey(s) ? total[s] + 1 : 1;
			}
			for (int i = 0; i < ids.Count; i++)
			{
				JSONStorable js = a.GetStorableByID(ids[i]);
				if (js == null)
				{
					continue;
				}
				string s = Short(ids[i]);
				int n = seen.ContainsKey(s) ? seen[s] + 1 : 1;
				seen[s] = n;
				PluginRow r = new PluginRow();
				r.Storable = js;
				r.Label = total[s] > 1 ? s + " " + n : s;
				r.Kind = ids[i].IndexOf(RouterType, StringComparison.OrdinalIgnoreCase) >= 0 ? "router" : (ids[i].IndexOf("AutoFlyObject", StringComparison.OrdinalIgnoreCase) >= 0 ? "fly" : "other");
				list.Add(r);
			}
			return list;
		}

		private static bool IsOn(PluginRow r)
		{
			JSONStorableBool e = r.Storable.GetBoolJSONParam("enabled");
			return e == null || e.val;
		}

		private void Toggle(PluginRow r)
		{
			if (r == null)
			{
				return;
			}
			try
			{
				JSONStorableBool e = r.Storable.GetBoolJSONParam("enabled");
				if (e != null)
				{
					e.val = !e.val;
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning("Could not switch " + r.Label + ": " + ex.Message);
			}
			Refresh();
		}

		private void SetAll(bool on)
		{
			Atom a = Find(ViewSpec());
			List<PluginRow> all = RowsFor(a);
			for (int i = 0; i < all.Count; i++)
			{
				try
				{
					JSONStorableBool e = all[i].Storable.GetBoolJSONParam("enabled");
					if (e != null)
					{
						e.val = on;
					}
				}
				catch (Exception ex)
				{
					Logger.LogWarning("Could not switch " + all[i].Label + ": " + ex.Message);
				}
			}
			Say((on ? "All " : "All ") + (on ? "on" : "off") + ": " + (a != null ? a.uid : ViewSpec().Name.Value) + " (" + all.Count + " plugins).");
		}

		// the two small buttons next to a plugin: previous / next song for the AudioRouter, fly on-off / speed for AutoFlyObject
		private void Extra(PluginRow r, int which)
		{
			if (r == null)
			{
				return;
			}
			try
			{
				if (r.Kind == "router")
				{
					r.Storable.CallAction(which == 0 ? "PlayPrevious [UIList]" : "PlayNext [UIList]");
				}
				else if (r.Kind == "fly")
				{
					if (which == 0)
					{
						JSONStorableBool mv = r.Storable.GetBoolJSONParam("Move Toggle");
						if (mv != null)
						{
							mv.val = !mv.val;
						}
					}
					else
					{
						JSONStorableFloat sp = r.Storable.GetFloatJSONParam("Fly Speed");
						if (sp != null)
						{
							float v = sp.val + 0.5f;
							sp.val = v > sp.max + 0.001f ? sp.min : v;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning("Plugin control failed: " + ex.Message);
			}
			Refresh();
		}

		private void LayoutBody(float w, float h)
		{
			if (status == null)
			{
				return;
			}
			float m = RlUguiWindow.Margin;
			float inner = Mathf.Max(60f, w - 2f * m);
			float half = (inner - 6f) / 2f;
			float rowH = 28f;
			float y = 30f;
			Button[] left = { bMusicToggle, bSlider, bLight, bDeleteFly };
			Button[] right = { bMusicSelect, bMusic, bFly, null };
			for (int r = 0; r < 3; r++)
			{
				RlUguiWindow.SetRect(left[r].GetComponent<RectTransform>(), m, y, half, rowH);
				RlUguiWindow.SetRect(right[r].GetComponent<RectTransform>(), m + half + 6f, y, half, rowH);
				y += rowH + 4f;
			}
			RlUguiWindow.SetRect(bDeleteFly.GetComponent<RectTransform>(), m, y, inner, rowH);
			y += rowH + 8f;
			RlUguiWindow.SetRect(status.rectTransform, m, y, inner, 50f);
			y += 54f;
			float b = 26f;
			RlUguiWindow.SetRect(bAtomPrev.GetComponent<RectTransform>(), m, y, b, 24f);
			RlUguiWindow.SetRect(atomLabel.rectTransform, m + b + 4f, y, inner - 2f * b - 8f - 140f, 24f);
			RlUguiWindow.SetRect(bAtomNext.GetComponent<RectTransform>(), m + inner - b - 140f - 4f, y, b, 24f);
			RlUguiWindow.SetRect(bAllOn.GetComponent<RectTransform>(), m + inner - 140f, y, 68f, 24f);
			RlUguiWindow.SetRect(bAllOff.GetComponent<RectTransform>(), m + inner - 68f, y, 68f, 24f);
			y += 30f;
			float small = 30f, rh = 22f;
			float mainW = inner - 4f * (small + 3f);
			int fit = Mathf.Clamp(Mathf.FloorToInt((h - y - 50f) / (rh + 2f)), 1, RowCount);
			for (int i = 0; i < RowCount; i++)
			{
				bool show = i < fit;
				rowMain[i].gameObject.SetActive(show);
				rowA[i].gameObject.SetActive(show);
				rowB[i].gameObject.SetActive(show);
				rowC[i].gameObject.SetActive(show);
				rowD[i].gameObject.SetActive(show);
				if (!show)
				{
					continue;
				}
				RlUguiWindow.SetRect(rowMain[i].GetComponent<RectTransform>(), m, y, mainW, rh);
				RlUguiWindow.SetRect(rowA[i].GetComponent<RectTransform>(), m + mainW + 3f, y, small, rh);
				RlUguiWindow.SetRect(rowB[i].GetComponent<RectTransform>(), m + mainW + small + 6f, y, small, rh);
				RlUguiWindow.SetRect(rowC[i].GetComponent<RectTransform>(), m + mainW + 2f * small + 9f, y, small, rh);
				RlUguiWindow.SetRect(rowD[i].GetComponent<RectTransform>(), m + mainW + 3f * small + 12f, y, small, rh);
				y += rh + 2f;
			}
			visibleRows = fit;
			float pb = 28f;
			RlUguiWindow.SetRect(bPagePrev5.GetComponent<RectTransform>(), m, y + 2f, pb, 22f);
			RlUguiWindow.SetRect(bPagePrev.GetComponent<RectTransform>(), m + pb + 3f, y + 2f, pb, 22f);
			RlUguiWindow.SetRect(pageLabel.rectTransform, m + 2f * pb + 8f, y + 2f, inner - 4f * pb - 16f, 22f);
			RlUguiWindow.SetRect(bPageNext.GetComponent<RectTransform>(), m + inner - 2f * pb - 3f, y + 2f, pb, 22f);
			RlUguiWindow.SetRect(bPageNext5.GetComponent<RectTransform>(), m + inner - pb, y + 2f, pb, 22f);
		}

		private int visibleRows = RowCount;

		private string Mark(Spec s)
		{
			Atom a = Find(s);
			return (a != null ? "[x] " : "[ ] ") + s.Name.Value;
		}

		private void Refresh()
		{
			if (status == null)
			{
				return;
			}
			bool playing = MusicPlaying();
			RlUguiWindow.SetLabel(bMusicToggle, playing ? "Music On" : "Music Off");
			StringBuilder sb = new StringBuilder();
			sb.Append(Mark(music)).Append("   ").Append(Mark(slider)).Append('\n').Append(Mark(light)).Append("   ").Append(Mark(fly));
			Atom m = Find(music);
			if (m != null)
			{
				List<JSONStorable> routers = Routers(m);
				if (routers.Count > 0)
				{
					JSONStorableString file = routers[0].GetStringJSONParam("Audio File");
					if (file != null && !string.IsNullOrEmpty(file.val))
					{
						sb.Append("\nSong: ").Append(Path.GetFileNameWithoutExtension(file.val));
					}
				}
			}
			if (message.Length > 0)
			{
				sb.Append('\n').Append(message);
			}
			status.text = sb.ToString();

			// the plugins of the atom in view
			Spec vs = ViewSpec();
			Atom va = Find(vs);
			rows = RowsFor(va);
			if (detailRow != null && (detailRow.Storable == null || va == null))
			{
				detailRow = null;
			}
			RlUguiWindow.SetLabel(bAllOn, detailRow != null ? "Back" : "All on");
			bAllOff.gameObject.SetActive(detailRow == null);
			if (detailRow != null)
			{
				RefreshDetail(vs);
				return;
			}
			int per = Mathf.Max(1, visibleRows);
			int pages = Mathf.Max(1, (rows.Count + per - 1) / per);
			if (page >= pages)
			{
				page = pages - 1;
			}
			atomLabel.text = vs.Name.Value + (va == null ? " (none)" : " " + rows.Count);
			pageLabel.text = rows.Count == 0 ? "" : (page + 1) + " / " + pages;
			for (int i = 0; i < RowCount; i++)
			{
				int idx = page * per + i;
				PluginRow r = idx < rows.Count && i < per ? rows[idx] : null;
				SetRow(i, r);
			}
		}

		private void RefreshDetail(Spec vs)
		{
			paramRows = ParamsOf(detailRow.Storable);
			int per = Mathf.Max(1, visibleRows);
			int pages = Mathf.Max(1, (paramRows.Count + per - 1) / per);
			if (page >= pages)
			{
				page = pages - 1;
			}
			atomLabel.text = vs.Name.Value + " / " + detailRow.Label;
			pageLabel.text = paramRows.Count + " settings   " + (page + 1) + " / " + pages;
			JSONStorable s = detailRow.Storable;
			for (int i = 0; i < RowCount; i++)
			{
				int idx = page * per + i;
				ParamRow pr = idx < paramRows.Count && i < per ? paramRows[idx] : null;
				bool show = pr != null;
				rowMain[i].gameObject.SetActive(show);
				rowA[i].gameObject.SetActive(show && pr.Kind == "float");
				rowB[i].gameObject.SetActive(show && (pr.Kind == "float" || pr.Kind == "choice"));
				rowC[i].gameObject.SetActive(show && (pr.Kind == "float" || pr.Kind == "choice"));
				rowD[i].gameObject.SetActive(show && pr.Kind == "float");
				if (!show)
				{
					continue;
				}
				string text = pr.Name;
				rowMain[i].interactable = pr.Kind != "text";
				if (pr.Kind == "bool")
				{
					JSONStorableBool b = s.GetBoolJSONParam(pr.Name);
					text += ":  " + (b != null && b.val ? "On" : "Off");
				}
				else if (pr.Kind == "float")
				{
					JSONStorableFloat f = s.GetFloatJSONParam(pr.Name);
					text += ":  " + (f != null ? f.val.ToString("0.###") : "?");
					RlUguiWindow.SetLabel(rowA[i], "<<");
					RlUguiWindow.SetLabel(rowB[i], "<");
					RlUguiWindow.SetLabel(rowC[i], ">");
					RlUguiWindow.SetLabel(rowD[i], ">>");
				}
				else if (pr.Kind == "choice")
				{
					JSONStorableStringChooser c = s.GetStringChooserJSONParam(pr.Name);
					text += ":  " + (c != null ? c.val : "?");
					RlUguiWindow.SetLabel(rowB[i], "<");
					RlUguiWindow.SetLabel(rowC[i], ">");
				}
				else if (pr.Kind == "text")
				{
					JSONStorableString st = s.GetStringJSONParam(pr.Name);
					string v = st != null ? st.val : "";
					text += ":  " + (v != null && v.Length > 40 ? v.Substring(0, 40) + "..." : v);
				}
				RlUguiWindow.SetLabel(rowMain[i], text);
				rowA[i].interactable = rowB[i].interactable = rowC[i].interactable = rowD[i].interactable = true;
			}
		}

		private void SetRow(int i, PluginRow r)
		{
			bool show = r != null && i < visibleRows;
			rowMain[i].gameObject.SetActive(show);
			rowA[i].gameObject.SetActive(show);
			rowB[i].gameObject.SetActive(show);
			rowC[i].gameObject.SetActive(false);
			rowD[i].gameObject.SetActive(show);
			if (r == null)
			{
				RlUguiWindow.SetLabel(rowMain[i], "");
				RlUguiWindow.SetLabel(rowA[i], "");
				RlUguiWindow.SetLabel(rowB[i], "");
				rowMain[i].interactable = false;
				rowA[i].interactable = false;
				rowB[i].interactable = false;
				return;
			}
			rowMain[i].interactable = true;
			rowC[i].gameObject.SetActive(false);
			rowD[i].gameObject.SetActive(show);
			rowD[i].interactable = true;
			RlUguiWindow.SetLabel(rowD[i], "Edit");
			RlUguiWindow.SetLabel(rowMain[i], r.Label + ":  " + (IsOn(r) ? "On" : "Off"));
			bool router = r.Kind == "router", fly = r.Kind == "fly";
			rowA[i].interactable = router || fly;
			rowB[i].interactable = router || fly;
			if (router)
			{
				RlUguiWindow.SetLabel(rowA[i], "Prev");
				RlUguiWindow.SetLabel(rowB[i], "Next");
			}
			else if (fly)
			{
				JSONStorableBool mv = r.Storable.GetBoolJSONParam("Move Toggle");
				JSONStorableFloat sp = r.Storable.GetFloatJSONParam("Fly Speed");
				RlUguiWindow.SetLabel(rowA[i], mv != null && !mv.val ? "Still" : "Fly");
				RlUguiWindow.SetLabel(rowB[i], sp != null ? sp.val.ToString("0.0") : "Spd");
			}
			else
			{
				RlUguiWindow.SetLabel(rowA[i], "");
				RlUguiWindow.SetLabel(rowB[i], "");
			}
		}
	}
}
