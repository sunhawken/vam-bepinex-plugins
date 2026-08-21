using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInDependency("com.zerot.assetloaderhelper", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin("com.zerot.scenespeedloader", "SceneSpeedLoader", "1.0.0")]
public class SceneSpeedLoader : BaseUnityPlugin
{
	private ConfigEntry<bool> cfgDisableShadowsDuringLoad;

	private ConfigEntry<bool> cfgDisableCamerasDuringLoad;

	private ConfigEntry<bool> cfgDisablePhysicsDuringLoad;

	private ConfigEntry<bool> cfgDisableReflectionsDuringLoad;

	private ConfigEntry<bool> cfgMinLODDuringLoad;

	private ConfigEntry<bool> cfgHighProcessPriority;

	private ConfigEntry<bool> cfgWarmShadersOnStart;

	private ConfigEntry<bool> cfgExtraGCAfterLoad;

	private ConfigEntry<bool> cfgLogTiming;

	private ShadowQuality _savedShadows;

	private bool _savedReflections;

	private float _savedLODBias;

	private int _savedParticleRaycast;

	private bool _savedPhysics;

	private Camera[] _disabledCameras = (Camera[])(object)new Camera[0];

	private bool _loadActive;

	private Stopwatch _loadTimer;

	public static SceneSpeedLoader Instance;

	private static ManualLogSource Log;

	private void Awake()
	{
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Expected O, but got Unknown
		Instance = this;
		Log = Logger;
		cfgDisableShadowsDuringLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "DisableShadows", true, "Turn shadows off during scene load (big GPU save).");
		cfgDisableCamerasDuringLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "DisableCameras", true, "Disable all cameras during load (stops GPU rendering entirely).");
		cfgDisablePhysicsDuringLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "DisablePhysics", true, "Pause physics simulation during load.");
		cfgDisableReflectionsDuringLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "DisableReflections", true, "Disable realtime reflection probes during load.");
		cfgMinLODDuringLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "MinimiseLOD", true, "Set LOD bias to minimum during load (skip high-poly LOD selection).");
		cfgHighProcessPriority = ((BaseUnityPlugin)this).Config.Bind<bool>("Load Optimise", "HighProcessPriority", true, "Boost VaM process priority to High during load.");
		cfgWarmShadersOnStart = ((BaseUnityPlugin)this).Config.Bind<bool>("Startup", "WarmShaders", true, "Pre-compile all shaders on startup (one-time cost, faster first scene).");
		cfgExtraGCAfterLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Post-Load", "ExtraGCAfterLoad", true, "Force full GC after scene finishes loading.");
		cfgLogTiming = ((BaseUnityPlugin)this).Config.Bind<bool>("Debug", "LogTiming", true, "Log scene load time to BepInEx console.");
		Harmony harmony = new Harmony("com.zerot.scenespeedloader");
		PatchSuperController(harmony);
		if (cfgWarmShadersOnStart.Value)
		{
			((MonoBehaviour)this).StartCoroutine(WarmShadersCoroutine());
		}
		Log.LogInfo((object)"SceneSpeedLoader v1.0.0 active");
	}

	private void PatchSuperController(Harmony harmony)
	{
		Type type = Type.GetType("SuperController, Assembly-CSharp");
		if (type == null)
		{
			Log.LogWarning((object)"SuperController not found -- patches skipped.");
			return;
		}
		TryPatch(harmony, type, "Load", typeof(SceneSpeedLoader).GetMethod("Prefix_Load", BindingFlags.Static | BindingFlags.Public), null);
		TryPatch(harmony, type, "ClearScene", typeof(SceneSpeedLoader).GetMethod("Prefix_ClearScene", BindingFlags.Static | BindingFlags.Public), null);
		TryPatch(harmony, type, "UpdateLoadingStatus", typeof(SceneSpeedLoader).GetMethod("Prefix_UpdateLoadingStatus", BindingFlags.Static | BindingFlags.Public), null);
		TryPatch(harmony, type, "UnloadUnusedResources", null, typeof(SceneSpeedLoader).GetMethod("Postfix_UnloadUnused", BindingFlags.Static | BindingFlags.Public));
	}

	private static void TryPatch(Harmony h, Type type, string methodName, MethodInfo prefix, MethodInfo postfix)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Log.LogWarning((object)("Method not found: " + methodName));
				return;
			}
			h.Patch((MethodBase)method, (!(prefix != null)) ? ((HarmonyMethod)null) : new HarmonyMethod(prefix), (!(postfix != null)) ? ((HarmonyMethod)null) : new HarmonyMethod(postfix), (HarmonyMethod)null, (HarmonyMethod)null);
			Log.LogDebug((object)("Patched: SuperController." + methodName));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("Patch failed for " + methodName + ": " + ex.Message));
		}
	}

	public static void Prefix_Load()
	{
		if ((Object)(object)Instance != (Object)null)
		{
			Instance.OnLoadStart("Load");
		}
	}

	public static void Prefix_ClearScene()
	{
		if ((Object)(object)Instance != (Object)null)
		{
			Instance.OnLoadStart("ClearScene");
		}
	}

	public static void Prefix_UpdateLoadingStatus(string status)
	{
		if ((Object)(object)Instance != (Object)null && Instance.cfgLogTiming.Value)
		{
			Log.LogDebug((object)("[Load] " + ((Instance._loadTimer != null) ? (Instance._loadTimer.ElapsedMilliseconds + "ms") : "?") + "  " + status));
		}
	}

	public static void Postfix_UnloadUnused()
	{
		if ((Object)(object)Instance != (Object)null && Instance.cfgExtraGCAfterLoad.Value)
		{
			((MonoBehaviour)Instance).StartCoroutine(Instance.PostUnloadGC());
		}
	}

	private void OnLoadStart(string trigger)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		if (_loadActive)
		{
			return;
		}
		_loadActive = true;
		_loadTimer = Stopwatch.StartNew();
		if (cfgLogTiming.Value)
		{
			Log.LogInfo((object)("[SceneSpeedLoader] Load started (" + trigger + ")"));
		}
		_savedShadows = QualitySettings.shadows;
		_savedReflections = QualitySettings.realtimeReflectionProbes;
		_savedLODBias = QualitySettings.lodBias;
		_savedParticleRaycast = QualitySettings.particleRaycastBudget;
		_savedPhysics = Physics.autoSimulation;
		if (cfgDisableShadowsDuringLoad.Value)
		{
			QualitySettings.shadows = (ShadowQuality)0;
		}
		if (cfgDisableReflectionsDuringLoad.Value)
		{
			QualitySettings.realtimeReflectionProbes = false;
		}
		if (cfgMinLODDuringLoad.Value)
		{
			QualitySettings.lodBias = 0.1f;
			QualitySettings.particleRaycastBudget = 0;
		}
		if (cfgDisablePhysicsDuringLoad.Value)
		{
			Physics.autoSimulation = false;
		}
		if (cfgDisableCamerasDuringLoad.Value)
		{
			DisableCameras();
		}
		if (cfgHighProcessPriority.Value)
		{
			try
			{
				Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
			}
			catch
			{
			}
		}
		Application.backgroundLoadingPriority = (ThreadPriority)4;
		QualitySettings.asyncUploadTimeSlice = 33;
		QualitySettings.asyncUploadBufferSize = 512;
		((MonoBehaviour)this).StartCoroutine(WaitForLoadComplete());
	}

	private IEnumerator WaitForLoadComplete()
	{
		yield return null;
		yield return null;
		Type scType = Type.GetType("SuperController, Assembly-CSharp");
		PropertyInfo isLoadingProp = ((scType != null) ? scType.GetProperty("isLoading", BindingFlags.Instance | BindingFlags.Public) : null);
		object sc = null;
		if (scType != null)
		{
			FieldInfo field = scType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
			if (field != null)
			{
				sc = field.GetValue(null);
			}
		}
		float timeout = 300f;
		while (timeout > 0f)
		{
			yield return (object)new WaitForSeconds(0.25f);
			timeout -= 0.25f;
			bool still = false;
			if (sc != null && isLoadingProp != null)
			{
				still = (bool)isLoadingProp.GetValue(sc, null);
			}
			if (!still)
			{
				break;
			}
		}
		OnLoadComplete();
	}

	private void OnLoadComplete()
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		long num = ((_loadTimer != null) ? _loadTimer.ElapsedMilliseconds : 0);
		_loadActive = false;
		QualitySettings.shadows = _savedShadows;
		QualitySettings.realtimeReflectionProbes = _savedReflections;
		QualitySettings.lodBias = _savedLODBias;
		QualitySettings.particleRaycastBudget = _savedParticleRaycast;
		Physics.autoSimulation = _savedPhysics;
		if (_disabledCameras.Length > 0)
		{
			Camera[] disabledCameras = _disabledCameras;
			foreach (Camera val in disabledCameras)
			{
				if ((Object)(object)val != (Object)null)
				{
					((Behaviour)val).enabled = true;
				}
			}
			_disabledCameras = (Camera[])(object)new Camera[0];
		}
		try
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
		}
		catch
		{
		}
		Application.backgroundLoadingPriority = (ThreadPriority)2;
		QualitySettings.asyncUploadTimeSlice = 4;
		QualitySettings.asyncUploadBufferSize = 64;
		if (cfgLogTiming.Value)
		{
			Log.LogInfo((object)("[SceneSpeedLoader] Load complete in " + num + " ms  (" + Math.Round((double)num / 1000.0, 1) + "s)"));
		}
		if (cfgExtraGCAfterLoad.Value)
		{
			((MonoBehaviour)this).StartCoroutine(PostLoadGC());
		}
	}

	private void DisableCameras()
	{
		Camera[] array = Object.FindObjectsOfType<Camera>();
		List<Camera> list = new List<Camera>();
		Camera[] array2 = array;
		foreach (Camera val in array2)
		{
			if (((Behaviour)val).enabled && ((Object)val).name.IndexOf("UI") < 0 && ((Object)val).name.IndexOf("ui") < 0 && ((Object)val).name.IndexOf("Loading") < 0)
			{
				((Behaviour)val).enabled = false;
				list.Add(val);
			}
		}
		_disabledCameras = list.ToArray();
		Log.LogDebug((object)("Disabled " + _disabledCameras.Length + " cameras for load."));
	}

	private IEnumerator WarmShadersCoroutine()
	{
		yield return (object)new WaitForSeconds(3f);
		Log.LogInfo((object)"[SceneSpeedLoader] Warming shaders...");
		Stopwatch sw = Stopwatch.StartNew();
		Shader.WarmupAllShaders();
		Log.LogInfo((object)("[SceneSpeedLoader] Shader warmup done in " + sw.ElapsedMilliseconds + "ms"));
	}

	private IEnumerator PostLoadGC()
	{
		yield return (object)new WaitForSeconds(1f);
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
		GC.WaitForPendingFinalizers();
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
		Log.LogDebug((object)"Post-load GC complete.");
	}

	private IEnumerator PostUnloadGC()
	{
		yield return null;
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
		GC.WaitForPendingFinalizers();
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
		Log.LogDebug((object)"Post-unload GC complete.");
	}

	private void OnDestroy()
	{
		if (_loadActive)
		{
			OnLoadComplete();
		}
	}
}
