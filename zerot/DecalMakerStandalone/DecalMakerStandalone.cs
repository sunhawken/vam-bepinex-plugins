using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.DecalMakerStandalone
{
	// Standalone helper for Chokaphi's DecalMaker 2 (.var). DecalMaker is a PERSON plugin, so this DLL carries the package,
	// installs it when missing, shows the panel of the DecalMaker you attached to a person (it does not add a second instance unless
	// AutoAddToPersons is switched on) and shows the selected person's DecalMaker panel in a desktop window that matches the other restyled plugin windows.
	[BepInPlugin("com.zerot.decalmaker.standalone", "DecalMaker Standalone", "1.0.0")]
	public sealed class DecalMakerStandalone : BaseUnityPlugin
	{
		private const string PackageName = "Chokaphi.DecalMaker";
		private const string PackageVersion = "90";
		private const string ScriptPath = "Custom/Scripts/Chokaphi/VAM_Decal_Maker/load.cslist";
		private const string ResourceName = "DecalMaker.var";

		private ConfigEntry<bool> cfgAutoAdd;
		private ConfigEntry<bool> cfgFemalesOnly;
		private ConfigEntry<float> cfgDelay;

		private RlUguiWindow win;
		private Text status;
		private Button personButton;
		private RectTransform viewport;
		private RectTransform scrollContent;
		private RectTransform nativeHost;
		private ScrollRect nativeScroll;

		private int selected;
		private string selectedUid = "";
		private float nextTick;
		private readonly Dictionary<string, float> firstSeen = new Dictionary<string, float>();
		private readonly Dictionary<string, int> attempts = new Dictionary<string, int>();

		private MVRScript hostedScript;
		private RectTransform detached;
		private Transform originalParent;
		private int originalSibling;
		private Vector3 originalPosition;
		private Vector3 originalScale;
		private Quaternion originalRotation;
		private Vector2 originalAnchorsMin;
		private Vector2 originalAnchorsMax;
		private Vector2 originalPivot;
		private Vector2 originalSize;
		private Vector2 originalAnchoredPosition;
		private bool originalActive;
		private float detachedWidth = 700f;
		private float detachedHeight = 800f;

		private void Awake()
		{
			cfgAutoAdd = Config.Bind<bool>("General", "AutoAddToPersons", false, "Off (default): only show the DecalMaker you attached to a person yourself. On: also add a new DecalMaker to every Person that has none.");
			cfgFemalesOnly = Config.Bind<bool>("General", "FemalesOnly", false, "Only add DecalMaker to female Person atoms.");
			cfgDelay = Config.Bind<float>("General", "AddDelaySeconds", 3f, "Wait this long after a Person appears before adding the plugin (lets VaM finish building it).");
			win = new RlUguiWindow(this, "Decal Maker", "Window", 40f, 60f, 620f, 700f, 380f, 280f, 1600f, 1400f);
			try
			{
				EnsurePackageInstalled();
			}
			catch (Exception e)
			{
				Logger.LogError("Could not install the bundled DecalMaker package: " + e);
			}
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
			Restore();
			if (win != null)
			{
				win.Dispose();
			}
		}

		private void OnDisable()
		{
			Restore();
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

		// ------------------------------------------------------------------ package

		private void EnsurePackageInstalled()
		{
			string root = Path.GetFullPath(Paths.GameRootPath);
			string packages = Path.Combine(root, "AddonPackages");
			if (!Directory.Exists(packages))
			{
				return;
			}
			foreach (string f in Directory.GetFiles(packages, PackageName + ".*.var", SearchOption.AllDirectories))
			{
				return;   // some version of the package is already installed
			}
			Assembly assembly = Assembly.GetExecutingAssembly();
			using (Stream source = assembly.GetManifestResourceStream(ResourceName))
			{
				if (source == null)
				{
					throw new InvalidOperationException("The embedded DecalMaker package was not found.");
				}
				string dest = Path.Combine(packages, PackageName + "." + PackageVersion + ".var");
				using (FileStream output = File.Create(dest))
				{
					byte[] buffer = new byte[81920];
					int count;
					while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
					{
						output.Write(buffer, 0, count);
					}
				}
			}
			Logger.LogInfo("Installed " + PackageName + "." + PackageVersion + ".var into AddonPackages (restart VaM once if it does not appear).");
		}

		private string PackageUrl()
		{
			string root = Path.GetFullPath(Paths.GameRootPath);
			string packages = Path.Combine(root, "AddonPackages");
			string version = PackageVersion;
			try
			{
				int best = -1;
				foreach (string f in Directory.GetFiles(packages, PackageName + ".*.var", SearchOption.AllDirectories))
				{
					string name = Path.GetFileNameWithoutExtension(f);
					int dot = name.LastIndexOf('.');
					int v;
					if (dot > 0 && int.TryParse(name.Substring(dot + 1), out v) && v > best)
					{
						best = v;
						version = v.ToString();
					}
				}
			}
			catch
			{
			}
			return PackageName + "." + version + ":/" + ScriptPath;
		}

		// ------------------------------------------------------------------ persons / plugin

		private static bool IsFemale(Atom atom)
		{
			try
			{
				DAZCharacterSelector character = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return character == null || character.selectedCharacter == null || !character.selectedCharacter.isMale;
			}
			catch
			{
				return true;
			}
		}

		private List<Atom> Persons()
		{
			List<Atom> list = new List<Atom>();
			foreach (Atom atom in SuperController.singleton.GetAtoms())
			{
				if (atom != null && atom.type == "Person")
				{
					list.Add(atom);
				}
			}
			return list;
		}

		private static MVRPluginManager ManagerOf(Atom atom)
		{
			return atom.GetStorableByID("PluginManager") as MVRPluginManager;
		}

		// The main DecalMaker script (load.cslist); DecalMaker also autoloads a small helper script we must not host.
		private static MVRScript FindScript(MVRPluginManager m)
		{
			if (m == null || m.pluginContainer == null)
			{
				return null;
			}
			Transform pc = m.pluginContainer;
			MVRScript fallback = null;
			for (int i = 0; i < pc.childCount; i++)
			{
				Transform child = pc.GetChild(i);
				MVRScript s = child.GetComponent<MVRScript>();
				if (s == null)
				{
					continue;
				}
				string n = child.name;
				if (n.IndexOf("load.cslist", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return s;
				}
				if (fallback == null && n.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					fallback = s;
				}
			}
			return fallback;
		}

		private void Update()
		{
			if (win == null || win.Window == null)
			{
				return;
			}
			win.Tick();
			if (!win.Active || Time.unscaledTime < nextTick)
			{
				return;
			}
			nextTick = Time.unscaledTime + 1f;
			if (detached != null && (originalParent == null || hostedScript == null))
			{
				Restore();
			}
			Tick();
		}

		private void Tick()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			List<Atom> persons = Persons();
			if (persons.Count == 0)
			{
				Restore();
				SetStatus("Add a Person to the scene.");
				SetPersonLabel("Person: none");
				return;
			}

			float now = Time.unscaledTime;
			for (int i = 0; i < persons.Count; i++)
			{
				Atom p = persons[i];
				if (!firstSeen.ContainsKey(p.uid))
				{
					firstSeen[p.uid] = now;
				}
				if (cfgAutoAdd.Value && now >= firstSeen[p.uid] + cfgDelay.Value && (!cfgFemalesOnly.Value || IsFemale(p)))
				{
					EnsurePlugin(p);
				}
			}

			int index = persons.FindIndex(delegate(Atom a) { return a.uid == selectedUid; });
			if (index < 0)
			{
				index = Mathf.Clamp(selected, 0, persons.Count - 1);
			}
			selected = index;
			Atom target = persons[index];
			selectedUid = target.uid;
			SetPersonLabel("Person: " + target.uid);

			MVRScript script = FindScript(ManagerOf(target));
			if (script == null)
			{
				Restore();
				SetStatus(cfgAutoAdd.Value ? "Adding DecalMaker to " + target.uid + "..." : "No DecalMaker on " + target.uid + " - attach it in VaM, or pick another person.");
				return;
			}
			RectTransform ui = script.UITransform as RectTransform;
			if (ui == null)
			{
				SetStatus("DecalMaker loaded; waiting for its UI...");
				return;
			}
			if (detached != ui)
			{
				hostedScript = script;
				Attach(ui);
			}
			SetStatus("DecalMaker on " + target.uid + " (" + persons.Count + (persons.Count == 1 ? " person" : " persons") + ").");
		}

		private void EnsurePlugin(Atom person)
		{
			MVRPluginManager m = ManagerOf(person);
			if (m == null || FindScript(m) != null)
			{
				return;
			}
			int n;
			attempts.TryGetValue(person.uid, out n);
			if (n >= 3)
			{
				return;
			}
			attempts[person.uid] = n + 1;
			try
			{
				MVRPlugin plugin = m.CreatePlugin();
				plugin.pluginURLJSON.val = PackageUrl();
				Logger.LogInfo("Added DecalMaker to " + person.uid + " (" + PackageUrl() + ").");
			}
			catch (Exception e)
			{
				Logger.LogError("Adding DecalMaker to " + person.uid + " failed: " + e);
			}
		}

		private void CycleSelection()
		{
			List<Atom> persons = Persons();
			if (persons.Count == 0)
			{
				return;
			}
			selected = (selected + 1) % persons.Count;
			selectedUid = persons[selected].uid;
			Restore();
			nextTick = 0f;
		}

		private void SetActive(bool on)
		{
			if (!on)
			{
				Restore();
				SetStatus("DecalMaker helper is off (plugins already on persons keep working).");
			}
			else
			{
				nextTick = 0f;
			}
		}

		// ------------------------------------------------------------------ window

		private void SetStatus(string text)
		{
			if (status != null && status.text != text)
			{
				status.text = text;
			}
		}

		private void SetPersonLabel(string text)
		{
			if (personButton != null)
			{
				RlUguiWindow.SetLabel(personButton, text);
			}
		}

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h)
			{
				LayoutBody(w, h);
			};
			win.OnActiveChanged = SetActive;
			win.Build("DecalMaker Standalone Desktop");
			personButton = RlUguiWindow.NewButton("Person", win.Body, "Person: select", CycleSelection);
			status = RlUguiWindow.NewText("Status", win.Body, "Starting...", TextAnchor.MiddleLeft);

			viewport = RlUguiWindow.NewRect("Native UI viewport", win.Body);
			viewport.gameObject.AddComponent<Image>().color = RlUguiWindow.ViewCol;
			viewport.gameObject.AddComponent<RectMask2D>();
			nativeScroll = viewport.gameObject.AddComponent<ScrollRect>();
			nativeScroll.viewport = viewport;
			nativeScroll.horizontal = false;
			nativeScroll.vertical = true;
			nativeScroll.movementType = ScrollRect.MovementType.Clamped;
			nativeScroll.scrollSensitivity = 40f;
			Vector2 corner = new Vector2(0f, 1f);
			scrollContent = RlUguiWindow.NewRect("Scroll content", viewport);
			scrollContent.pivot = corner;
			scrollContent.anchorMin = corner;
			scrollContent.anchorMax = corner;
			nativeScroll.content = scrollContent;
			nativeHost = RlUguiWindow.NewRect("Native UI host", scrollContent);
			nativeHost.pivot = corner;
			nativeHost.anchorMin = corner;
			nativeHost.anchorMax = corner;
			nativeHost.anchoredPosition = Vector2.zero;
			win.AddResizeHandle();
			win.Apply();
		}

		private void LayoutBody(float w, float h)
		{
			float m = RlUguiWindow.Margin;
			float inner = Mathf.Max(60f, w - 2f * m);
			float buttonW = Mathf.Min(220f, inner * 0.5f);
			if (personButton != null)
			{
				RlUguiWindow.SetRect(personButton.GetComponent<RectTransform>(), m, 30f, buttonW, 24f);
			}
			if (status != null)
			{
				RlUguiWindow.SetRect(status.rectTransform, m + buttonW + 8f, 30f, Mathf.Max(30f, inner - buttonW - 8f), 24f);
			}
			if (viewport != null)
			{
				viewport.anchorMin = Vector2.zero;
				viewport.anchorMax = Vector2.one;
				viewport.offsetMin = new Vector2(m, m);
				viewport.offsetMax = new Vector2(-m, -60f);
			}
			UpdateNativePresentation(w, h);
		}

		private void UpdateNativePresentation(float w, float h)
		{
			if (detached == null || viewport == null)
			{
				return;
			}
			float vw = Mathf.Max(1f, w - 2f * RlUguiWindow.Margin);
			float vh = Mathf.Max(1f, h - 60f - RlUguiWindow.Margin);
			float fit = Mathf.Min(1f, vw / detachedWidth);
			nativeHost.sizeDelta = new Vector2(detachedWidth * fit, detachedHeight * fit);
			scrollContent.sizeDelta = new Vector2(Mathf.Max(vw, detachedWidth * fit), Mathf.Max(vh, detachedHeight * fit));
			detached.localScale = Vector3.one * fit;
		}

		// Moves the plugin's own UI panel into the window; Restore puts it back exactly where VaM had it.
		private void Attach(RectTransform target)
		{
			if (target == null || nativeHost == null)
			{
				return;
			}
			Restore();
			originalParent = target.parent;
			originalSibling = target.GetSiblingIndex();
			originalPosition = target.localPosition;
			originalScale = target.localScale;
			originalRotation = target.localRotation;
			originalAnchorsMin = target.anchorMin;
			originalAnchorsMax = target.anchorMax;
			originalPivot = target.pivot;
			originalSize = target.sizeDelta;
			originalAnchoredPosition = target.anchoredPosition;
			originalActive = target.gameObject.activeSelf;
			detachedWidth = Mathf.Max(300f, target.rect.width);
			detachedHeight = Mathf.Max(300f, target.rect.height);
			detached = target;
			target.SetParent(nativeHost, false);
			Vector2 corner = new Vector2(0f, 1f);
			target.pivot = corner;
			target.anchorMin = corner;
			target.anchorMax = corner;
			target.anchoredPosition = Vector2.zero;
			target.sizeDelta = new Vector2(detachedWidth, detachedHeight);
			target.localRotation = Quaternion.identity;
			target.gameObject.SetActive(true);
			nativeScroll.verticalNormalizedPosition = 1f;
			UpdateNativePresentation(win.W, win.H);
		}

		private void Restore()
		{
			if (detached != null && originalParent != null)
			{
				detached.SetParent(originalParent, false);
				detached.SetSiblingIndex(Mathf.Clamp(originalSibling, 0, originalParent.childCount - 1));
				detached.anchorMin = originalAnchorsMin;
				detached.anchorMax = originalAnchorsMax;
				detached.pivot = originalPivot;
				detached.sizeDelta = originalSize;
				detached.anchoredPosition = originalAnchoredPosition;
				detached.localPosition = originalPosition;
				detached.localRotation = originalRotation;
				detached.localScale = originalScale;
				detached.gameObject.SetActive(originalActive);
			}
			detached = null;
			originalParent = null;
		}
	}
}
