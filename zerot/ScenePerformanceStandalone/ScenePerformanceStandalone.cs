using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.ScenePerformanceStandalone
{
	// Standalone loader for vaan20's ScenePerformance (.var). The DLL carries the package, drops it into AddonPackages when it
	// is missing, creates the SESSION plugin by itself at startup (no manual "add plugin"), and shows the plugin's native
	// VaM UI in a desktop window that looks and behaves like the other restyled plugin windows.
	[BepInPlugin("com.zerot.sceneperformance.standalone", "Scene Performance Standalone", "1.0.0")]
	public sealed class ScenePerformanceStandalone : BaseUnityPlugin
	{
		private const string PackageName = "vaan20.ScenePerformance";
		private const string PackageVersion = "1";
		private const string ScriptPath = "Custom/Scripts/vaan20/ScenePerformance/ScenePerformance.dll";
		private const string ResourceName = "ScenePerformance.var";
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

		// saved settings (so they do not have to be set again after every scene load)
		private string settingsPath;
		private MVRScript persistScript;
		private float restoreAt = -1f;
		private bool persistReady;
		private bool loadingSeen;
		private string lastSaved = "";
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
			cfgAutoLoad = Config.Bind<bool>("General", "AutoLoadSessionPlugin", true, "Create the ScenePerformance session plugin automatically at startup.");
			cfgStartupDelay = Config.Bind<float>("General", "StartupDelaySeconds", 6f, "Wait this long after VaM finishes loading its own session plugins before adding ScenePerformance (so it is not added twice).");
			win = new RlUguiWindow(this, "Scene Performance", "Window", 40f, 60f, 560f, 640f, 360f, 260f, 1600f, 1400f);
			try
			{
				EnsurePackageInstalled();
			}
			catch (Exception e)
			{
				Logger.LogError("Could not install the bundled ScenePerformance package: " + e);
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
					throw new InvalidOperationException("The embedded ScenePerformance package was not found.");
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
				if (child.name.IndexOf("ScenePerformance", StringComparison.OrdinalIgnoreCase) >= 0)
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
			if (sc == null)
			{
				return;
			}
			if (sc.isLoading)
			{
				loadingSeen = true;
				return;
			}
			if (loadingSeen)
			{
				// a scene just finished loading: put the saved settings back once the plugin has settled
				loadingSeen = false;
				persistReady = false;
				restoreAt = Time.unscaledTime + 2f;
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
					SetStatus("ScenePerformance is not loaded (auto-load is off).");
					return;
				}
				if (Time.unscaledTime < readyAt)
				{
					SetStatus("Starting ScenePerformance shortly...");
					return;
				}
				if (creationAttempts >= 3)
				{
					SetStatus("Could not load ScenePerformance - see BepInEx/LogOutput.log.");
					return;
				}
				creationAttempts++;
				try
				{
					MVRPlugin plugin = manager.CreatePlugin();
					plugin.pluginURLJSON.val = PackageUrl();
					pluginUid = plugin.uid;
					created = true;
					Logger.LogInfo("Created ScenePerformance session plugin (" + PackageUrl() + ").");
				}
				catch (Exception e)
				{
					Logger.LogError("Creating the ScenePerformance session plugin failed: " + e);
				}
				return;
			}
			RectTransform ui = script.UITransform as RectTransform;
			if (ui == null)
			{
				SetStatus("ScenePerformance loaded; waiting for its UI...");
				return;
			}
			if (detached == null)
			{
				Attach(ui);
			}
			PersistTick();
			SetStatus("ScenePerformance is running" + (created ? " (loaded by this plugin)" : " (already loaded by VaM)") + ".");
		}

		// ------------------------------------------------------------------ saved settings

		private static readonly string[] Transient = { "Status", "Never Pause List", "Character" };

		private string SettingsPath()
		{
			if (settingsPath == null)
			{
				settingsPath = Path.Combine(Paths.ConfigPath, "ZeroT.ScenePerformance.settings.json");
			}
			return settingsPath;
		}

		private static bool IsTransient(string name)
		{
			if (name.StartsWith("header"))
			{
				return true;
			}
			for (int i = 0; i < Transient.Length; i++)
			{
				if (Transient[i] == name)
				{
					return true;
				}
			}
			return false;
		}

		private string Capture()
		{
			JSONClass root = new JSONClass();
			JSONClass bools = new JSONClass();
			JSONClass floats = new JSONClass();
			JSONClass strings = new JSONClass();
			JSONClass choosers = new JSONClass();
			foreach (string n in script.GetBoolParamNames())
			{
				JSONStorableBool p = script.GetBoolJSONParam(n);
				if (p != null && p.isStorable && !IsTransient(n)) bools[n] = p.val ? "true" : "false";
			}
			foreach (string n in script.GetFloatParamNames())
			{
				JSONStorableFloat p = script.GetFloatJSONParam(n);
				if (p != null && p.isStorable && !IsTransient(n)) floats[n] = p.val.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
			}
			foreach (string n in script.GetStringParamNames())
			{
				JSONStorableString p = script.GetStringJSONParam(n);
				if (p != null && p.isStorable && !IsTransient(n)) strings[n] = p.val;
			}
			foreach (string n in script.GetStringChooserParamNames())
			{
				JSONStorableStringChooser p = script.GetStringChooserJSONParam(n);
				if (p != null && p.isStorable && !IsTransient(n)) choosers[n] = p.val;
			}
			root["bools"] = bools;
			root["floats"] = floats;
			root["strings"] = strings;
			root["choosers"] = choosers;
			return root.ToString();
		}

		private void RestoreSettings()
		{
			string path = SettingsPath();
			if (!File.Exists(path))
			{
				return;
			}
			try
			{
				JSONClass root = JSON.Parse(File.ReadAllText(path)) as JSONClass;
				if (root == null)
				{
					return;
				}
				JSONClass bools = root["bools"] as JSONClass;
				JSONClass floats = root["floats"] as JSONClass;
				JSONClass strings = root["strings"] as JSONClass;
				JSONClass choosers = root["choosers"] as JSONClass;
				int applied = 0;
				if (bools != null)
				{
					foreach (string n in new List<string>(bools.Keys))
					{
						JSONStorableBool p = script.GetBoolJSONParam(n);
						if (p != null && !IsTransient(n) && p.val != bools[n].AsBool) { p.val = bools[n].AsBool; applied++; }
					}
				}
				if (floats != null)
				{
					foreach (string n in new List<string>(floats.Keys))
					{
						JSONStorableFloat p = script.GetFloatJSONParam(n);
						float v = floats[n].AsFloat;
						if (p != null && !IsTransient(n) && Mathf.Abs(p.val - v) > 1e-6f) { p.val = v; applied++; }
					}
				}
				if (strings != null)
				{
					foreach (string n in new List<string>(strings.Keys))
					{
						JSONStorableString p = script.GetStringJSONParam(n);
						if (p != null && !IsTransient(n) && p.val != strings[n].Value) { p.val = strings[n].Value; applied++; }
					}
				}
				if (choosers != null)
				{
					foreach (string n in new List<string>(choosers.Keys))
					{
						JSONStorableStringChooser p = script.GetStringChooserJSONParam(n);
						if (p != null && !IsTransient(n) && p.val != choosers[n].Value) { p.val = choosers[n].Value; applied++; }
					}
				}
				Logger.LogInfo("Restored " + applied + " saved ScenePerformance setting(s).");
			}
			catch (Exception e)
			{
				Logger.LogWarning("Restoring ScenePerformance settings failed: " + e.Message);
			}
		}

		private void PersistTick()
		{
			float now = Time.unscaledTime;
			if (persistScript != script)
			{
				persistScript = script;
				persistReady = false;
				restoreAt = now + 1.5f;
			}
			try
			{
				if (!persistReady)
				{
					if (now < restoreAt)
					{
						return;
					}
					RestoreSettings();
					persistReady = true;
					lastSaved = Capture();
					return;
				}
				string cur = Capture();
				if (cur != lastSaved)
				{
					File.WriteAllText(SettingsPath(), cur);
					lastSaved = cur;
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Saving ScenePerformance settings failed: " + e.Message);
			}
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
						Logger.LogWarning("Removing ScenePerformance failed: " + e.Message);
					}
				}
				script = null;
				pluginUid = "";
				created = false;
				SetStatus("ScenePerformance is off.");
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
			win.Build("Scene Performance Standalone Desktop");
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
