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
			win = new RlUguiWindow(this, "Music and VFX", "Window", 40f, 60f, 320f, 270f, 260f, 230f, 700f, 600f);
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
			win.AddResizeHandle();
			win.Apply();
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
			RlUguiWindow.SetRect(status.rectTransform, m, y, inner, Mathf.Max(30f, h - y - 22f));
		}

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
		}
	}
}
