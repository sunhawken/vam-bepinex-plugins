using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.ControllersAppearanceStandalone
{
	// Headless standalone port of Datel's ControllersAppearance (.var, "controllers scale, custom icons, lines from controllers
	// to rigidbody"). Nothing to add to a session: it applies itself to every atom, and the icon assetbundle is inside this DLL.
	//
	// What the original does, and what is different here
	//  - scales: icon scale (controllers with a custom icon), cube scale (the rest), selected scale        (same, same defaults)
	//  - custom icons: meshes from the plugin's assetbundle, matched by "<AtomType>_<controller name>"       (same)
	//  - lines from each controller to its rigidbody, one colour for everything                               (colour now follows the person:
	//    pink for female, blue for male, light blue for everything else; all three are settings)
	//  - the lines are drawn from three batched meshes (one per colour) instead of one mesh and one draw call per line, and the
	//    list of controllers is not rebuilt every frame
	// If Datel's own plugin is loaded in the session this one steps aside (two of them would draw every line twice).
	[BepInPlugin("com.zerot.controllersappearance", "ControllersAppearance Standalone", "1.0.0")]
	public sealed class ControllersAppearanceStandalone : BaseUnityPlugin
	{
		private const int DrawLayer = 8;

		private ConfigEntry<bool> cfgActive;
		private ConfigEntry<float> cfgIconScale, cfgCubeScale, cfgSelectedScale;
		private ConfigEntry<bool> cfgIcons;
		private ConfigEntry<bool> cfgLines, cfgByGender;
		private ConfigEntry<float> cfgAlpha;
		private ConfigEntry<string> cfgFemale, cfgMale, cfgOther;

		private SuperController sc;
		private bool hooked;
		private bool scalesApplied;
		private bool dirtyScales, dirtyColors, dirtyIcons;
		private bool yielded;
		private float nextOriginalCheck;
		private AssetBundle bundle;
		private Dictionary<string, Mesh> icons;
		private Mesh defaultSquareMesh;
		private Material lineSource;

		// lines
		private sealed class Batch
		{
			public readonly Mesh mesh = new Mesh();
			public Material material;
			public Vector3[] verts = new Vector3[0];
			public int[] indices = new int[0];
			public int used;
			public Color color;

			public void Begin(int pairs)
			{
				if (verts.Length < pairs * 2)
				{
					verts = new Vector3[pairs * 2 + 64];
				}
				used = 0;
			}

			public void Add(Vector3 a, Vector3 b)
			{
				verts[used++] = a;
				verts[used++] = b;
			}
		}

		private readonly Batch[] batches = new Batch[3];   // 0 female, 1 male, 2 other
		private readonly List<FreeControllerV3> controllers = new List<FreeControllerV3>();
		private readonly Dictionary<Atom, int> groupOf = new Dictionary<Atom, int>();
		private float nextList;

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn everything off (restores VaM's own controller look).");
			cfgIconScale = Config.Bind<float>("Scale", "IconScale", 0.5f, "Controllers with an icon (0.0001 - 2).");
			cfgCubeScale = Config.Bind<float>("Scale", "CubeScale", 0.6f, "Controllers without an icon, like a person's spine or elbow (0.1 - 2).");
			cfgSelectedScale = Config.Bind<float>("Scale", "SelectedScale", 0.5f, "The white/blue mesh of the selected controller (0.1 - 2).");
			cfgIcons = Config.Bind<bool>("Icons", "UseCustomIcons", true, "Replace the controller meshes with the plugin's icons.");
			cfgLines = Config.Bind<bool>("Lines", "Enabled", true, "Draw a line from each controller to the rigidbody it belongs to.");
			cfgAlpha = Config.Bind<float>("Lines", "Alpha", 0.7f, "Line opacity, 0 - 1. 0 skips drawing.");
			cfgByGender = Config.Bind<bool>("Lines", "ColorByGender", true, "Female persons pink, male persons blue. Off: every line uses OtherColor.");
			cfgFemale = Config.Bind<string>("Lines", "FemaleColor", "#FF73C2", "Line colour for the controllers of a female person (hex RRGGBB).");
			cfgMale = Config.Bind<string>("Lines", "MaleColor", "#4080FF", "Line colour for the controllers of a male person (hex RRGGBB).");
			cfgOther = Config.Bind<string>("Lines", "OtherColor", "#66B3D9", "Line colour for every other atom (the original plugin's default).");
			Config.SettingChanged += OnSettingChanged;
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			ConfigEntryBase c = e.ChangedSetting;
			if (c == cfgIconScale || c == cfgCubeScale || c == cfgSelectedScale)
			{
				dirtyScales = true;
			}
			else if (c == cfgIcons)
			{
				dirtyIcons = true;
			}
			else if (c == cfgFemale || c == cfgMale || c == cfgOther || c == cfgAlpha || c == cfgByGender)
			{
				dirtyColors = true;
			}
			else if (c == cfgActive)
			{
				dirtyScales = dirtyIcons = dirtyColors = true;
			}
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			Unhook();
			ResetAll();
			for (int i = 0; i < batches.Length; i++)
			{
				if (batches[i] != null)
				{
					Destroy(batches[i].mesh);
					if (batches[i].material != null)
					{
						Destroy(batches[i].material);
					}
				}
			}
			if (bundle != null)
			{
				bundle.Unload(false);
			}
		}

		// ------------------------------------------------------------------ lifecycle

		private bool OriginalLoaded()
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					if (a.GetType("Datel.ControllersAppearance", false) != null)
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private void Update()
		{
			if (Time.unscaledTime >= nextOriginalCheck)
			{
				nextOriginalCheck = Time.unscaledTime + 2f;
				bool orig = OriginalLoaded();
				if (orig && !yielded)
				{
					yielded = true;
					Logger.LogWarning("Datel's ControllersAppearance is loaded in this session: this standalone steps aside so lines are not drawn twice.");
					Unhook();
					ResetAll();
				}
				else if (!orig && yielded)
				{
					yielded = false;
					dirtyScales = dirtyIcons = dirtyColors = true;
				}
			}
			if (yielded)
			{
				return;
			}
			sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			if (!cfgActive.Value)
			{
				if (hooked)
				{
					Unhook();
					ResetAll();
				}
				return;
			}
			if (!hooked)
			{
				Hook();
			}
			if (!sc.isLoading && icons == null && cfgIcons.Value)
			{
				LoadIcons();
				dirtyIcons = true;
			}
			if (!sc.isLoading && (!scalesApplied || dirtyScales || dirtyIcons))
			{
				ApplyAll(dirtyIcons || !scalesApplied, true);
				scalesApplied = true;
				dirtyScales = dirtyIcons = false;
			}
		}

		private void Hook()
		{
			hooked = true;
			sc.onAtomAddedHandlers += OnAtomAdded;
			sc.onSceneLoadedHandlers += OnSceneLoaded;
		}

		private void Unhook()
		{
			if (!hooked || sc == null)
			{
				hooked = false;
				return;
			}
			sc.onAtomAddedHandlers -= OnAtomAdded;
			sc.onSceneLoadedHandlers -= OnSceneLoaded;
			hooked = false;
		}

		private void OnSceneLoaded()
		{
			try
			{
				ApplyAll(true, true);
				nextList = 0f;
			}
			catch (Exception e)
			{
				Logger.LogWarning("Scene load: " + e.Message);
			}
		}

		private void OnAtomAdded(Atom atom)
		{
			try
			{
				if (sc != null && !sc.isLoading && atom != null)
				{
					SetupIcons(atom);
					SetupScale(atom);
					nextList = 0f;
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Atom added: " + e.Message);
			}
		}

		// ------------------------------------------------------------------ scales and icons (as in the original)

		private void ApplyAll(bool icons, bool scale)
		{
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null)
				{
					continue;
				}
				if (icons)
				{
					SetupIcons(a);
				}
				if (scale)
				{
					SetupScale(a);
				}
			}
		}

		private void ResetAll()
		{
			if (sc == null)
			{
				sc = SuperController.singleton;
			}
			if (sc == null || sc.isLoading)
			{
				scalesApplied = false;
				return;
			}
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				if (atoms[i] != null)
				{
					ResetIcons(atoms[i]);
					ResetScale(atoms[i]);
				}
			}
			scalesApplied = false;
		}

		private static float BaseScale(Atom atom)
		{
			return atom.type == "AnimationStep" ? 0.01f : 0.02f;
		}

		private void SetupScale(Atom atom)
		{
			float baseScale = BaseScale(atom);
			float icon = Mathf.Clamp(cfgIconScale.Value, 0.0001f, 2f), cube = Mathf.Clamp(cfgCubeScale.Value, 0.1f, 2f), sel = Mathf.Clamp(cfgSelectedScale.Value, 0.1f, 2f);
			FreeControllerV3[] cs = atom.freeControllers;
			for (int i = 0; i < cs.Length; i++)
			{
				FreeControllerV3 c = cs[i];
				if (c == null)
				{
					continue;
				}
				c.deselectedMeshScale = baseScale * icon;
				c.unhighlightedScale = 0.25f * cube;
				c.highlightedScale = c.unhighlightedScale;
				c.selectedScale = sel;
			}
		}

		private static void ResetScale(Atom atom)
		{
			float baseScale = BaseScale(atom);
			FreeControllerV3[] cs = atom.freeControllers;
			for (int i = 0; i < cs.Length; i++)
			{
				FreeControllerV3 c = cs[i];
				if (c == null)
				{
					continue;
				}
				c.deselectedMeshScale = baseScale;
				c.unhighlightedScale = 0.25f;
				c.highlightedScale = 0.25f;
				c.selectedScale = 1f;
			}
		}

		private void LoadIcons()
		{
			icons = new Dictionary<string, Mesh>();
			try
			{
				using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ControllersAppearance.assetbundle"))
				{
					if (s == null)
					{
						Logger.LogWarning("Icon assetbundle is missing from the DLL; custom icons are off.");
						return;
					}
					byte[] data = new byte[s.Length];
					int read = 0;
					while (read < data.Length)
					{
						int n = s.Read(data, read, data.Length - read);
						if (n <= 0)
						{
							break;
						}
						read += n;
					}
					bundle = AssetBundle.LoadFromMemory(data);
				}
				if (bundle == null)
				{
					Logger.LogWarning("The icon assetbundle could not be opened; custom icons are off.");
					return;
				}
				Mesh[] meshes = bundle.LoadAllAssets<Mesh>();
				for (int i = 0; i < meshes.Length; i++)
				{
					icons[meshes[i].name] = meshes[i];
				}
				Logger.LogInfo("Loaded " + icons.Count + " controller icons.");
			}
			catch (Exception e)
			{
				Logger.LogWarning("Loading icons failed: " + e.Message);
			}
		}

		private void SetupIcons(Atom atom)
		{
			if (atom.mainController != null && lineSource == null)
			{
				lineSource = atom.mainController.linkLineMaterial;
			}
			if (icons == null || !cfgIcons.Value)
			{
				return;
			}
			if (defaultSquareMesh == null && atom.mainController != null && (atom.type == "Empty" || atom.type == "CustomUnityAsset"))
			{
				defaultSquareMesh = atom.mainController.deselectedMesh;
			}
			FreeControllerV3[] cs = atom.freeControllers;
			for (int i = 0; i < cs.Length; i++)
			{
				Mesh mesh;
				if (cs[i] != null && icons.TryGetValue(atom.type + "_" + cs[i].name, out mesh))
				{
					cs[i].deselectedMesh = mesh;
				}
			}
		}

		private void ResetIcons(Atom atom)
		{
			if (icons == null)
			{
				return;
			}
			if (defaultSquareMesh != null && (atom.type == "Empty" || atom.type == "CustomUnityAsset") && atom.mainController != null)
			{
				atom.mainController.deselectedMesh = defaultSquareMesh;
				return;
			}
			FreeControllerV3[] cs = atom.freeControllers;
			for (int i = 0; i < cs.Length; i++)
			{
				if (cs[i] != null && icons.ContainsKey(atom.type + "_" + cs[i].name))
				{
					cs[i].deselectedMesh = null;
				}
			}
		}

		// ------------------------------------------------------------------ lines

		internal static Color ParseColor(string hex, Color fallback)
		{
			try
			{
				string h = (hex ?? "").Trim().TrimStart('#');
				if (h.Length != 6)
				{
					return fallback;
				}
				int r = Convert.ToInt32(h.Substring(0, 2), 16), g = Convert.ToInt32(h.Substring(2, 2), 16), b = Convert.ToInt32(h.Substring(4, 2), 16);
				return new Color(r / 255f, g / 255f, b / 255f, 1f);
			}
			catch
			{
				return fallback;
			}
		}

		private static bool IsPerson(Atom atom)
		{
			return atom != null && atom.type == "Person";
		}

		// 0 female, 1 male, 2 everything else
		private int GroupFor(Atom atom)
		{
			if (!cfgByGender.Value || !IsPerson(atom))
			{
				return 2;
			}
			try
			{
				DAZCharacterSelector cs = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				if (cs == null)
				{
					return 2;
				}
				return cs.gender == DAZCharacterSelector.Gender.Male ? 1 : 0;
			}
			catch
			{
				return 2;
			}
		}

		private bool EnsureBatches()
		{
			if (batches[0] != null)
			{
				return true;
			}
			if (lineSource == null)
			{
				return false;
			}
			for (int i = 0; i < batches.Length; i++)
			{
				batches[i] = new Batch();
				batches[i].material = (Material)Instantiate(lineSource);
			}
			dirtyColors = true;
			return true;
		}

		private void SyncColors()
		{
			batches[0].color = ParseColor(cfgFemale.Value, new Color(1f, 0.45f, 0.76f, 1f));
			batches[1].color = ParseColor(cfgMale.Value, new Color(0.25f, 0.5f, 1f, 1f));
			batches[2].color = ParseColor(cfgOther.Value, new Color(0.4f, 0.7f, 0.85f, 1f));
			float alpha = Mathf.Clamp01(cfgAlpha.Value);
			for (int i = 0; i < batches.Length; i++)
			{
				batches[i].material.color = batches[i].color;
				batches[i].material.SetFloat("_Alpha", alpha);
			}
			dirtyColors = false;
		}

		private void LateUpdate()
		{
			if (yielded || !cfgActive.Value || sc == null || sc.isLoading || !cfgLines.Value || cfgAlpha.Value <= 0f)
			{
				return;
			}
			if (!EnsureBatches())
			{
				return;
			}
			if (dirtyColors)
			{
				groupOf.Clear();
				SyncColors();
			}
			if (Time.unscaledTime >= nextList)
			{
				nextList = Time.unscaledTime + 0.5f;
				List<FreeControllerV3> all = sc.GetAllFreeControllers();
				controllers.Clear();
				groupOf.Clear();
				if (all != null)
				{
					controllers.AddRange(all);
				}
			}
			for (int i = 0; i < batches.Length; i++)
			{
				batches[i].Begin(controllers.Count);
			}
			for (int i = 0; i < controllers.Count; i++)
			{
				FreeControllerV3 c = controllers[i];
				if (c == null || !c.isActiveAndEnabled || c.hidden)
				{
					continue;
				}
				Rigidbody rb = c.followWhenOffRB;
				if (rb == null)
				{
					continue;
				}
				Vector3 a = c.transform.position, b = rb.transform.position;
				if ((a - b).sqrMagnitude < 0.0000005f)
				{
					continue;   // controller sits on its rigidbody: no line worth drawing
				}
				Atom atom = c.containingAtom;
				int g;
				if (atom == null)
				{
					g = 2;
				}
				else if (!groupOf.TryGetValue(atom, out g))
				{
					g = GroupFor(atom);
					groupOf[atom] = g;
				}
				batches[g].Add(a, b);
			}
			for (int i = 0; i < batches.Length; i++)
			{
				Batch bt = batches[i];
				if (bt.used == 0)
				{
					continue;
				}
				for (int k = bt.used; k < bt.verts.Length; k++)
				{
					bt.verts[k] = bt.verts[bt.used - 1];   // unused slots collapse onto the last point
				}
				bool indicesChanged = bt.indices.Length != bt.used;
				if (indicesChanged)
				{
					bt.indices = new int[bt.used];
					for (int k = 0; k < bt.used; k++)
					{
						bt.indices[k] = k;
					}
				}
				bt.mesh.Clear();
				bt.mesh.vertices = bt.verts;
				bt.mesh.SetIndices(bt.indices, MeshTopology.Lines, 0);
				bt.mesh.RecalculateBounds();
				Graphics.DrawMesh(bt.mesh, Matrix4x4.identity, bt.material, DrawLayer, null, 0, null, false, false);
			}
		}
	}
}
