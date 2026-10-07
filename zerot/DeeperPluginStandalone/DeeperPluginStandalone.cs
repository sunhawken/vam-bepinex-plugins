using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.DeeperPluginStandalone
{
	// Standalone loader for Boris47's DeeperPlugin (.var). The DLL carries the package, drops it into AddonPackages when it
	// is missing, creates the SESSION plugin by itself at startup (no manual "add plugin"), and shows the plugin's native
	// VaM UI in a desktop window that looks and behaves like the other restyled plugin windows.
	[BepInPlugin("com.zerot.deeperplugin.standalone", "Deeper Plugin Standalone", "1.0.0")]
	public sealed class DeeperPluginStandalone : BaseUnityPlugin
	{
		private const string PackageName = "Boris47.DeeperPlugin";
		private const string PackageVersion = "16";
		private const string ScriptPath = "Custom/Scripts/Boris47/DeeperPlugin/out/DeeperPlugin.rls.cs";
		private const string ResourceName = "DeeperPlugin.var";
		private const string SessionContainerPath = "SceneAtoms/CoreControl/SessionPluginManagerContainer";

		private ConfigEntry<bool> cfgAutoLoad;
		private ConfigEntry<float> cfgStartupDelay;

		private RlUguiWindow win;
		private Text status;
		private RectTransform viewport;
		private RectTransform scrollContent;
		private RectTransform nativeHost;
		private ScrollRect nativeScroll;

		private MVRPluginManager manager;
		private MVRScript script;
		private string pluginUid = "";
		private float readyAt = -1f;
		private float nextTick;
		private bool created;
		private int creationAttempts;

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
		private float detachedWidth = 800f;
		private float detachedHeight = 800f;

		private void Awake()
		{
			cfgAutoLoad = Config.Bind<bool>("General", "AutoLoadSessionPlugin", true, "Create the DeeperPlugin session plugin automatically at startup.");
			cfgStartupDelay = Config.Bind<float>("General", "StartupDelaySeconds", 6f, "Wait this long after VaM finishes loading its own session plugins before adding DeeperPlugin (so it is not added twice).");
			win = new RlUguiWindow(this, "Deeper Plugin", "Window", 40f, 60f, 560f, 640f, 360f, 260f, 1600f, 1400f);
			try
			{
				EnsurePackageInstalled();
			}
			catch (Exception e)
			{
				Logger.LogError("Could not install the bundled DeeperPlugin package: " + e);
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
					throw new InvalidOperationException("The embedded DeeperPlugin package was not found.");
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

		// ------------------------------------------------------------------ session plugin

		private MVRPluginManager FindManager()
		{
			GameObject container = GameObject.Find(SessionContainerPath);
			if (container == null)
			{
				return null;
			}
			MVRPluginManager[] managers = container.GetComponentsInChildren<MVRPluginManager>(true);
			for (int i = 0; i < managers.Length; i++)
			{
				if (managers[i] != null && managers[i].pluginContainer != null)
				{
					return managers[i];
				}
			}
			return null;
		}

		private MVRScript FindExistingScript(MVRPluginManager m)
		{
			Transform pc = m.pluginContainer;
			for (int i = 0; i < pc.childCount; i++)
			{
				Transform child = pc.GetChild(i);
				if (child.name.IndexOf("DeeperPlugin", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					MVRScript s = child.GetComponent<MVRScript>();
					if (s != null)
					{
						return s;
					}
				}
			}
			return null;
		}

		private static string UidOf(MVRScript s)
		{
			string n = s.name;
			int us = n.IndexOf('_');
			return us > 0 ? n.Substring(0, us) : n;
		}

		private void Tick()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			if (readyAt < 0f)
			{
				readyAt = Time.unscaledTime + cfgStartupDelay.Value;
			}
			if (manager == null)
			{
				manager = FindManager();
				if (manager == null)
				{
					SetStatus("Waiting for VaM's session plugin manager...");
					return;
				}
			}
			if (script == null)
			{
				script = FindExistingScript(manager);
				if (script != null)
				{
					pluginUid = UidOf(script);
					created = false;
				}
			}
			if (script == null)
			{
				if (!cfgAutoLoad.Value)
				{
					SetStatus("DeeperPlugin is not loaded (auto-load is off).");
					return;
				}
				if (Time.unscaledTime < readyAt)
				{
					SetStatus("Starting DeeperPlugin shortly...");
					return;
				}
				if (creationAttempts >= 3)
				{
					SetStatus("Could not load DeeperPlugin - see BepInEx/LogOutput.log.");
					return;
				}
				creationAttempts++;
				try
				{
					MVRPlugin plugin = manager.CreatePlugin();
					plugin.pluginURLJSON.val = PackageUrl();
					pluginUid = plugin.uid;
					created = true;
					Logger.LogInfo("Created DeeperPlugin session plugin (" + PackageUrl() + ").");
				}
				catch (Exception e)
				{
					Logger.LogError("Creating the DeeperPlugin session plugin failed: " + e);
				}
				return;
			}
			RectTransform ui = script.UITransform as RectTransform;
			if (ui == null)
			{
				SetStatus("DeeperPlugin loaded; waiting for its UI...");
				return;
			}
			if (detached == null)
			{
				Attach(ui);
			}
			SetStatus("DeeperPlugin is running" + (created ? " (loaded by this plugin)" : " (already loaded by VaM)") + ".");
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
			if (detached != null && (originalParent == null || script == null))
			{
				Restore();
				script = null;
			}
			Tick();
		}

		private void SetActive(bool on)
		{
			if (!on)
			{
				Restore();
				if (manager != null && !string.IsNullOrEmpty(pluginUid))
				{
					try
					{
						manager.RemovePluginWithUID(pluginUid);
					}
					catch (Exception e)
					{
						Logger.LogWarning("Removing DeeperPlugin failed: " + e.Message);
					}
				}
				script = null;
				pluginUid = "";
				created = false;
				SetStatus("DeeperPlugin is off.");
			}
			else
			{
				creationAttempts = 0;
				readyAt = Time.unscaledTime;
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

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h)
			{
				LayoutBody(w, h);
			};
			win.OnActiveChanged = SetActive;
			win.Build("Deeper Plugin Standalone Desktop");
			status = RlUguiWindow.NewText("Status", win.Body, "Starting...", TextAnchor.MiddleLeft);
			RlUguiWindow.SetRect(status.rectTransform, RlUguiWindow.Margin, 30f, 400f, 22f);

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
			if (status != null)
			{
				RlUguiWindow.SetRect(status.rectTransform, RlUguiWindow.Margin, 30f, Mathf.Max(30f, w - 2f * RlUguiWindow.Margin), 22f);
			}
			if (viewport != null)
			{
				viewport.anchorMin = Vector2.zero;
				viewport.anchorMax = Vector2.one;
				viewport.offsetMin = new Vector2(RlUguiWindow.Margin, RlUguiWindow.Margin);
				viewport.offsetMax = new Vector2(-RlUguiWindow.Margin, -56f);
			}
			UpdateNativePresentation();
		}

		private void UpdateNativePresentation()
		{
			if (detached == null || viewport == null || win == null)
			{
				return;
			}
			float vw = Mathf.Max(1f, win.W - 2f * RlUguiWindow.Margin);
			float vh = Mathf.Max(1f, win.H - 56f - RlUguiWindow.Margin);
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
			UpdateNativePresentation();
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
