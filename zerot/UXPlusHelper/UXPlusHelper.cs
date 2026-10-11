using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.UXPlusHelper
{
	// Headless helper for everlaster's UXPlus session plugin. No window. It patches the running script in memory.
	// Found by reading all of UXPlus (6 modules, ~15k lines):
	//  1. Show Target On Hover ran every frame in Edit mode: a fresh list of every free controller plus a write of
	//     controller.hidden for each, changed or not. Now: the list is reused briefly and only changes are written.
	//  2. Nothing UXPlus keeps is saved until VaM exits normally: the Alternate Plugins List (favorites, recently added
	//     plugins) and UXPlus's own settings are written only when the plugin is destroyed. After a crash or kill, a whole
	//     session of favorites / recents is gone. Now: changes are written within a short while.
	//  3. Preview Camera Helper pauses auto blink / auto expressions while the preview camera is on. When two cameras (preview
	//     and hi-res) enable it in a row, the "before" values are overwritten with the already-paused ones, and a disable
	//     without a matching enable resets them to false: blink / expressions stay off after the screenshot. Now: the first
	//     "before" values are kept and only a real enable is undone.
	//  4. VaM calls several UXPlus handlers (atom added/removed, scene loaded, camera on/off, plugin list scroll) with no
	//     error guard; an exception there escapes into VaM's own event call and can stop other plugins' handlers. Now each is
	//     contained and logged once.
	//  5. "Download Latest On Startup" asks the Hub about every favorited plugin on every start of VaM. Now it asks about
	//     each plugin at most once per N hours (the favorite-click check is untouched).
	[BepInPlugin("com.zerot.uxplus.helper", "UXPlus Helper", "1.1.0")]
	public sealed class UXPlusHelperPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgShowTarget;
		internal static ConfigEntry<float> CfgListSeconds;
		internal static ConfigEntry<bool> CfgAutosave;
		internal static ConfigEntry<float> CfgAutosaveSeconds;
		internal static ConfigEntry<bool> CfgCamFix;
		internal static ConfigEntry<bool> CfgContain;
		internal static ConfigEntry<bool> CfgThrottleHub;
		internal static ConfigEntry<float> CfgHubHours;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;
		private float nextAutosave;

		private void Awake()
		{
			Log = Logger;
			CfgShowTarget = Config.Bind<bool>("Speed", "OptimizeShowTargetOnHover", true, "Make UXPlus's Show Target On Hover cheaper (same behaviour).");
			CfgListSeconds = Config.Bind<float>("Speed", "ControllerListSeconds", 0.5f, "How long the list of controllers is reused before the scene is asked again.");
			CfgThrottleHub = Config.Bind<bool>("Speed", "ThrottleHubCheckOnStartup", true, "Check the Hub for newer versions of favorited plugins at most once per N hours instead of on every start.");
			CfgHubHours = Config.Bind<float>("Speed", "HubCheckHours", 12f, "Hours between Hub checks of the same plugin at startup.");
			CfgAutosave = Config.Bind<bool>("Safety", "AutosaveUXPlusData", true, "Save the Alternate Plugins List (favorites, recents) and UXPlus settings soon after they change, not only on exit.");
			CfgAutosaveSeconds = Config.Bind<float>("Safety", "AutosaveCheckSeconds", 20f, "How often to look for changes to save.");
			CfgCamFix = Config.Bind<bool>("Stability", "FixPreviewCameraBlinkRestore", true, "Keep the first remembered blink / expression values and only undo a real pause.");
			CfgContain = Config.Bind<bool>("Stability", "ContainHandlerErrors", true, "Catch errors in UXPlus's unguarded event handlers so they cannot reach VaM's event calls.");
			harmony = new Harmony("com.zerot.uxplus.helper");
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				pending.Enqueue(a);
			}
		}

		private void OnDestroy()
		{
			AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
		}

		private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			lock (pending)
			{
				pending.Enqueue(args.LoadedAssembly);
			}
		}

		private void Update()
		{
			if (Time.unscaledTime >= nextScan)
			{
				nextScan = Time.unscaledTime + 1f;
				while (true)
				{
					Assembly a;
					lock (pending)
					{
						if (pending.Count == 0)
						{
							break;
						}
						a = pending.Dequeue();
					}
					if (a == null || !seen.Add(a))
					{
						continue;
					}
					try
					{
						TryPatch(a);
					}
					catch (Exception e)
					{
						Logger.LogWarning("Patching " + a.FullName + " failed: " + e.Message);
					}
				}
			}
			if (CfgAutosave.Value && Time.unscaledTime >= nextAutosave)
			{
				nextAutosave = Time.unscaledTime + Mathf.Max(2f, CfgAutosaveSeconds.Value);
				Hooks.AutosaveTick();
			}
		}

		private static MethodInfo M(Type t, string name, params Type[] args)
		{
			return t == null ? null : t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
		}

		private HarmonyMethod H(string name)
		{
			return new HarmonyMethod(typeof(Hooks).GetMethod(name));
		}

		private void Contain(MethodInfo m)
		{
			if (m != null && CfgContain.Value)
			{
				harmony.Patch(m, null, null, null, H("ContainFinalizer"));
			}
		}

		private void TryPatch(Assembly asm)
		{
			Type hover = asm.GetType("everlaster.UX.ShowTargetOnHover.ShowTargetOnHover", false);
			Type camState = asm.GetType("everlaster.UX.PreviewCamHelper.AtomCameraState", false);
			Type camHelper = asm.GetType("everlaster.UX.PreviewCamHelper.PreviewCamHelper", false);
			Type altList = asm.GetType("everlaster.UX.AlternatePluginsList.AlternatePluginsList", false);
			Type plugin = asm.GetType("everlaster.UX.AlternatePluginsList.Plugin", false);
			Type pmHandler = asm.GetType("everlaster.UX.AlternatePluginsList.PluginManagerHandler", false);
			Type module = asm.GetType("everlaster.UX.UXModule", false);
			Type main = asm.GetType("everlaster.UXPlus", false);
			if (hover == null && altList == null && camHelper == null)
			{
				return;
			}
			Hooks.Asm = asm;
			Hooks.MainType = main;
			int n = 0;
			Type atomT = typeof(Atom);

			MethodInfo update = M(hover, "OnUpdate");
			if (CfgShowTarget.Value && update != null)
			{
				harmony.Patch(update, H("HoverPrefix"), null, null, CfgContain.Value ? H("ContainFinalizer") : null);
				n++;
			}
			else if (hover != null && update == null)
			{
				Logger.LogWarning("UXPlus found, but ShowTargetOnHover.OnUpdate is not where this helper expects it (UXPlus version changed?).");
			}

			if (CfgCamFix.Value && camState != null)
			{
				MethodInfo en = M(camState, "Enable", typeof(bool), typeof(bool)), dis = M(camState, "Disable");
				if (en != null && dis != null)
				{
					harmony.Patch(en, H("CamEnablePrefix"));
					harmony.Patch(dis, H("CamDisablePrefix"));
					n += 2;
				}
			}
			if (camHelper != null)
			{
				Contain(M(camHelper, "OnAtomAdded", atomT));
				Contain(M(camHelper, "OnAtomRemoved", atomT));
				Contain(M(camHelper, "OnSceneLoaded"));
				Contain(M(camHelper, "OnCameraEnabled"));
				Contain(M(camHelper, "OnCameraDisabled"));
			}
			if (altList != null)
			{
				Contain(M(altList, "OnAtomRemoved", atomT));
				MethodInfo init = M(altList, "OnInit");
				if (CfgThrottleHub.Value && init != null && plugin != null && M(plugin, "DownloadLatest") != null)
				{
					harmony.Patch(init, H("InitPrefix"), H("InitPostfix"));
					harmony.Patch(M(plugin, "DownloadLatest"), H("DownloadPrefix"));
					n += 2;
				}
			}
			if (pmHandler != null)
			{
				Contain(M(pmHandler, "ReadyAndModifyUI"));
				Contain(M(pmHandler, "OnScroll", typeof(Vector2)));
			}
			if (module != null)
			{
				MethodInfo init2 = module.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (init2 != null)
				{
					harmony.Patch(init2, null, H("ModuleInitPostfix"));   // remember the live modules so their data can be autosaved
					n++;
				}
			}
			Logger.LogInfo("UXPlus patched (" + n + " hooks).");
		}
	}

	public static class Hooks
	{
		internal static Assembly Asm;
		internal static Type MainType;

		// ---- 1. Show Target On Hover
		private static List<FreeControllerV3> cache;
		private static float nextList = -100f;
		private static bool failed;

		// Same decisions as UXPlus's own loop, without the per-frame list and without redundant writes.
		public static bool HoverPrefix()
		{
			if (failed)
			{
				return true;
			}
			try
			{
				SuperController sc = SuperController.singleton;
				if (sc == null)
				{
					return false;
				}
				if (sc.gameMode == SuperController.GameMode.Play || sc.GetTargetShow())
				{
					return false;
				}
				float now = Time.unscaledTime;
				if (cache == null || now >= nextList)
				{
					cache = sc.GetAllFreeControllers();
					nextList = now + Mathf.Max(0f, UXPlusHelperPlugin.CfgListSeconds.Value);
				}
				if (cache == null)
				{
					return true;
				}
				bool hideInactive = UserPreferences.singleton.hideInactiveTargets;
				int count = cache.Count;
				for (int i = 0; i < count; i++)
				{
					FreeControllerV3 c = cache[i];
					if (c == null)
					{
						nextList = -100f;   // something was removed: ask the scene again on the next frame
						continue;
					}
					bool want;
					if (hideInactive && c.currentPositionState == FreeControllerV3.PositionState.Off && c.currentRotationState == FreeControllerV3.RotationState.Off)
					{
						want = true;
					}
					else
					{
						want = !c.highlighted;
					}
					if (c.hidden != want)
					{
						c.hidden = want;
					}
				}
				return false;
			}
			catch (Exception e)
			{
				failed = true;   // anything unexpected: hand control back to UXPlus's own code for good
				UXPlusHelperPlugin.Log.LogWarning("Show Target On Hover optimisation turned off: " + e.Message);
				return true;
			}
		}

		// ---- 4. contain errors of handlers VaM calls
		private static readonly HashSet<string> logged = new HashSet<string>();

		public static Exception ContainFinalizer(Exception __exception, MethodBase __originalMethod)
		{
			if (__exception != null)
			{
				string key = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
				if (logged.Add(key))
				{
					UXPlusHelperPlugin.Log.LogWarning("UXPlus " + key + " error contained (logged once): " + __exception.Message);
				}
			}
			return null;
		}

		// ---- 3. preview camera: pair enable with disable
		private static readonly Dictionary<object, bool> camEnabled = new Dictionary<object, bool>(new RefEq());

		private sealed class RefEq : IEqualityComparer<object>
		{
			public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
			public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
		}

		public static bool CamEnablePrefix(object __instance)
		{
			bool on;
			if (camEnabled.TryGetValue(__instance, out on) && on)
			{
				return false;   // already paused: keep the first remembered values
			}
			if (camEnabled.Count > 500)
			{
				camEnabled.Clear();
			}
			camEnabled[__instance] = true;
			return true;
		}

		public static bool CamDisablePrefix(object __instance)
		{
			bool on;
			if (!camEnabled.TryGetValue(__instance, out on) || !on)
			{
				return false;   // this person was never paused by the camera: nothing to undo
			}
			camEnabled[__instance] = false;
			return true;
		}

		// ---- 5. Hub check on startup at most every N hours
		private static bool inInit;
		private static Dictionary<string, long> checks;
		private static string ChecksFile { get { return Path.Combine(Paths.ConfigPath, "ZeroT.UXPlusHelper.hubchecks.txt"); } }

		public static void InitPrefix() { inInit = true; }
		public static void InitPostfix() { inInit = false; }

		private static void LoadChecks()
		{
			checks = new Dictionary<string, long>();
			try
			{
				if (File.Exists(ChecksFile))
				{
					foreach (string line in File.ReadAllLines(ChecksFile))
					{
						int t = line.LastIndexOf('\t');
						long v;
						if (t > 0 && long.TryParse(line.Substring(t + 1), out v))
						{
							checks[line.Substring(0, t)] = v;
						}
					}
				}
			}
			catch
			{
			}
		}

		private static void SaveChecks()
		{
			try
			{
				List<string> lines = new List<string>();
				foreach (KeyValuePair<string, long> kv in checks)
				{
					lines.Add(kv.Key + "\t" + kv.Value);
				}
				File.WriteAllLines(ChecksFile, lines.ToArray());
			}
			catch
			{
			}
		}

		public static bool DownloadPrefix(object __instance)
		{
			if (!inInit)
			{
				return true;   // a favorite just clicked: always check
			}
			try
			{
				PropertyInfo pi = __instance.GetType().GetProperty("paths", BindingFlags.Public | BindingFlags.Instance);
				object paths = pi == null ? null : pi.GetValue(__instance, null);
				FieldInfo fi = paths == null ? null : paths.GetType().GetField("latestId");
				string id = fi == null ? null : fi.GetValue(paths) as string;
				if (string.IsNullOrEmpty(id))
				{
					return true;
				}
				if (checks == null)
				{
					LoadChecks();
				}
				long now = DateTime.UtcNow.Ticks, last;
				if (checks.TryGetValue(id, out last) && now - last < (long)(UXPlusHelperPlugin.CfgHubHours.Value * 36000000000.0) && now >= last)
				{
					return false;
				}
				checks[id] = now;
				SaveChecks();
				return true;
			}
			catch
			{
				return true;
			}
		}

		// ---- 2. autosave of what UXPlus only writes on exit
		private static readonly List<object> modules = new List<object>();
		private static readonly Dictionary<object, string> baseline = new Dictionary<object, string>(new RefEq());
		private static string mainBaseline;

		public static void ModuleInitPostfix(object __instance)
		{
			Track(__instance);
		}

		public static void Track(object module)
		{
			if (module != null && !modules.Contains(module))
			{
				modules.Add(module);
			}
		}

		private static object Call(object o, string name, params object[] args)
		{
			MethodInfo m = o.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
			return m == null ? null : m.Invoke(o, args);
		}

		private static bool Initialized(object module)
		{
			Type t = module.GetType();
			while (t != null)
			{
				FieldInfo f = t.GetField("initialized", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (f != null)
				{
					return (bool)f.GetValue(module);
				}
				t = t.BaseType;
			}
			return true;
		}

		private static bool UsesDataFile(object module)
		{
			Type t = module.GetType();
			while (t != null)
			{
				FieldInfo f = t.GetField("useDataFile", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (f != null)
				{
					return (bool)f.GetValue(module);
				}
				t = t.BaseType;
			}
			return false;
		}

		public static int AutosaveWrites;

		// Looks for changes since the last look; the first look only remembers what was loaded.
		public static void AutosaveTick()
		{
			try
			{
				SuperController sc = SuperController.singleton;
				if (sc != null && sc.isLoading)
				{
					return;
				}
				for (int i = modules.Count - 1; i >= 0; i--)
				{
					object m = modules[i];
					UnityEngine.Object uo = m as UnityEngine.Object;
					if (uo != null && !uo)
					{
						modules.RemoveAt(i);
						baseline.Remove(m);
						continue;
					}
					try
					{
						if (!Initialized(m) || !UsesDataFile(m))
						{
							continue;
						}
						object jc = Call(m, "GetDataJSON");
						if (jc == null)
						{
							continue;
						}
						string json = jc.ToString();
						string prev;
						if (!baseline.TryGetValue(m, out prev))
						{
							baseline[m] = json;
						}
						else if (prev != json)
						{
							Call(m, "SaveDataJSON");
							baseline[m] = json;
							AutosaveWrites++;
							UXPlusHelperPlugin.Log.LogInfo("UXPlus " + m.GetType().Name + " data autosaved.");
						}
					}
					catch (Exception e)
					{
						UXPlusHelperPlugin.Log.LogWarning("Autosave of " + m.GetType().Name + " skipped: " + (e.InnerException ?? e).Message);
					}
				}
				SaveMainSettings();
			}
			catch (Exception e)
			{
				UXPlusHelperPlugin.Log.LogWarning("Autosave tick failed: " + e.Message);
			}
		}

		private static void SaveMainSettings()
		{
			if (modules.Count == 0 || MainType == null || Asm == null)
			{
				return;
			}
			object main = null;
			for (int i = 0; i < modules.Count; i++)
			{
				PropertyInfo p = modules[i].GetType().GetProperty("script", BindingFlags.Public | BindingFlags.Instance);
				main = p == null ? null : p.GetValue(modules[i], null);
				if (main != null)
				{
					break;
				}
			}
			if (main == null)
			{
				return;
			}
			object jc = Call(main, "GetJSON", true, true, false);
			if (jc == null)
			{
				return;
			}
			string json = jc.ToString();
			if (mainBaseline == null)
			{
				mainBaseline = json;
				return;
			}
			if (mainBaseline == json)
			{
				return;
			}
			Type fu = Asm.GetType("everlaster.FileUtils", false);
			FieldInfo df = MainType.GetField("DATA_FILE", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
			MethodInfo w = fu == null ? null : fu.GetMethod("WriteDataJSON", BindingFlags.Public | BindingFlags.Static);
			if (w == null || df == null)
			{
				return;
			}
			w.Invoke(null, new object[] { df.GetValue(null), jc });
			mainBaseline = json;
			AutosaveWrites++;
			UXPlusHelperPlugin.Log.LogInfo("UXPlus settings autosaved.");
		}
	}
}
