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
    private bool          _savedReflections;
    private float         _savedLODBias;
    private int           _savedParticleRaycast;
    private bool          _savedPhysics;
    private Camera[]      _disabledCameras = new Camera[0];
    private bool          _loadActive;
    private Stopwatch     _loadTimer;

    public static SceneSpeedLoader Instance;
    private static ManualLogSource Log;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        cfgDisableShadowsDuringLoad     = Config.Bind("Load Optimise", "DisableShadows",      true, "Turn shadows off during scene load (big GPU save).");
        cfgDisableCamerasDuringLoad     = Config.Bind("Load Optimise", "DisableCameras",       true, "Disable all cameras during load (stops GPU rendering entirely).");
        cfgDisablePhysicsDuringLoad     = Config.Bind("Load Optimise", "DisablePhysics",       true, "Pause physics simulation during load.");
        cfgDisableReflectionsDuringLoad = Config.Bind("Load Optimise", "DisableReflections",   true, "Disable realtime reflection probes during load.");
        cfgMinLODDuringLoad             = Config.Bind("Load Optimise", "MinimiseLOD",          true, "Set LOD bias to minimum during load (skip high-poly LOD selection).");
        cfgHighProcessPriority          = Config.Bind("Load Optimise", "HighProcessPriority",  true, "Boost VaM process priority to High during load.");
        cfgWarmShadersOnStart           = Config.Bind("Startup",       "WarmShaders",          true, "Pre-compile all shaders on startup (one-time cost, faster first scene).");
        cfgExtraGCAfterLoad             = Config.Bind("Post-Load",     "ExtraGCAfterLoad",     true, "Force full GC after scene finishes loading.");
        cfgLogTiming                    = Config.Bind("Debug",         "LogTiming",            true, "Log scene load time to BepInEx console.");

        var harmony = new Harmony("com.zerot.scenespeedloader");
        PatchSuperController(harmony);

        if (cfgWarmShadersOnStart.Value)
            StartCoroutine(WarmShadersCoroutine());

        Log.LogInfo("SceneSpeedLoader v1.0.0 active");
    }

    private void PatchSuperController(Harmony harmony)
    {
        var t = Type.GetType("SuperController, Assembly-CSharp");
        if (t == null) { Log.LogWarning("SuperController not found -- patches skipped."); return; }

        TryPatch(harmony, t, "Load",                typeof(SceneSpeedLoader).GetMethod("Prefix_Load",                BindingFlags.Static | BindingFlags.Public), null);
        TryPatch(harmony, t, "ClearScene",          typeof(SceneSpeedLoader).GetMethod("Prefix_ClearScene",          BindingFlags.Static | BindingFlags.Public), null);
        TryPatch(harmony, t, "UpdateLoadingStatus", typeof(SceneSpeedLoader).GetMethod("Prefix_UpdateLoadingStatus", BindingFlags.Static | BindingFlags.Public), null);
        TryPatch(harmony, t, "UnloadUnusedResources", null, typeof(SceneSpeedLoader).GetMethod("Postfix_UnloadUnused", BindingFlags.Static | BindingFlags.Public));
    }

    private static void TryPatch(Harmony h, Type type, string methodName, MethodInfo prefix, MethodInfo postfix)
    {
        try
        {
            var m = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Log.LogWarning("Method not found: " + methodName); return; }
            h.Patch(m,
                prefix  != null ? new HarmonyMethod(prefix)  : null,
                postfix != null ? new HarmonyMethod(postfix) : null);
            Log.LogDebug("Patched: SuperController." + methodName);
        }
        catch (Exception ex) { Log.LogWarning("Patch failed for " + methodName + ": " + ex.Message); }
    }

    public static void Prefix_Load()                      { Instance?.OnLoadStart("Load"); }
    public static void Prefix_ClearScene()                { Instance?.OnLoadStart("ClearScene"); }
    public static void Prefix_UpdateLoadingStatus(string status)
    {
        if (Instance != null && Instance.cfgLogTiming.Value)
            Log.LogDebug("[Load] " + (Instance._loadTimer != null ? Instance._loadTimer.ElapsedMilliseconds + "ms" : "?") + "  " + status);
    }
    public static void Postfix_UnloadUnused()
    {
        if (Instance != null && Instance.cfgExtraGCAfterLoad.Value)
            Instance.StartCoroutine(Instance.PostUnloadGC());
    }

    private void OnLoadStart(string trigger)
    {
        if (_loadActive) return;
        _loadActive = true;
        _loadTimer  = Stopwatch.StartNew();
        if (cfgLogTiming.Value) Log.LogInfo("[SceneSpeedLoader] Load started (" + trigger + ")");

        _savedShadows         = QualitySettings.shadows;
        _savedReflections     = QualitySettings.realtimeReflectionProbes;
        _savedLODBias         = QualitySettings.lodBias;
        _savedParticleRaycast = QualitySettings.particleRaycastBudget;
        _savedPhysics         = Physics.autoSimulation;

        if (cfgDisableShadowsDuringLoad.Value)     QualitySettings.shadows = ShadowQuality.Disable;
        if (cfgDisableReflectionsDuringLoad.Value) QualitySettings.realtimeReflectionProbes = false;
        if (cfgMinLODDuringLoad.Value)             { QualitySettings.lodBias = 0.1f; QualitySettings.particleRaycastBudget = 0; }
        if (cfgDisablePhysicsDuringLoad.Value)     Physics.autoSimulation = false;
        if (cfgDisableCamerasDuringLoad.Value)     DisableCameras();
        if (cfgHighProcessPriority.Value)
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
        }
        Application.backgroundLoadingPriority = ThreadPriority.VeryHigh;
        QualitySettings.asyncUploadTimeSlice  = 33;
        QualitySettings.asyncUploadBufferSize = 512;
        StartCoroutine(WaitForLoadComplete());
    }

    private IEnumerator WaitForLoadComplete()
    {
        yield return null;
        yield return null;
        var scType       = Type.GetType("SuperController, Assembly-CSharp");
        var isLoadingProp = scType?.GetProperty("isLoading", BindingFlags.Instance | BindingFlags.Public);
        object sc = null;
        if (scType != null)
        {
            var fi = scType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
            if (fi != null) sc = fi.GetValue(null);
        }
        float timeout = 300f;
        while (timeout > 0f)
        {
            yield return new WaitForSeconds(0.25f);
            timeout -= 0.25f;
            if (sc != null && isLoadingProp != null && !(bool)isLoadingProp.GetValue(sc, null)) break;
        }
        OnLoadComplete();
    }

    private void OnLoadComplete()
    {
        long ms = _loadTimer?.ElapsedMilliseconds ?? 0;
        _loadActive = false;
        QualitySettings.shadows               = _savedShadows;
        QualitySettings.realtimeReflectionProbes = _savedReflections;
        QualitySettings.lodBias               = _savedLODBias;
        QualitySettings.particleRaycastBudget = _savedParticleRaycast;
        Physics.autoSimulation                = _savedPhysics;

        foreach (Camera cam in _disabledCameras)
            if (cam != null) cam.enabled = true;
        _disabledCameras = new Camera[0];

        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
        QualitySettings.asyncUploadTimeSlice  = 4;
        QualitySettings.asyncUploadBufferSize = 64;

        if (cfgLogTiming.Value)
            Log.LogInfo($"[SceneSpeedLoader] Load complete in {ms} ms  ({Math.Round(ms / 1000.0, 1)}s)");

        if (cfgExtraGCAfterLoad.Value)
            StartCoroutine(PostLoadGC());
    }

    private void DisableCameras()
    {
        var all = FindObjectsOfType<Camera>();
        var list = new List<Camera>();
        foreach (Camera cam in all)
        {
            if (cam.enabled && cam.name.IndexOf("UI") < 0 && cam.name.IndexOf("ui") < 0 && cam.name.IndexOf("Loading") < 0)
            {
                cam.enabled = false;
                list.Add(cam);
            }
        }
        _disabledCameras = list.ToArray();
        Log.LogDebug("Disabled " + _disabledCameras.Length + " cameras for load.");
    }

    private IEnumerator WarmShadersCoroutine()
    {
        yield return new WaitForSeconds(3f);
        Log.LogInfo("[SceneSpeedLoader] Warming shaders...");
        var sw = Stopwatch.StartNew();
        Shader.WarmupAllShaders();
        Log.LogInfo("[SceneSpeedLoader] Shader warmup done in " + sw.ElapsedMilliseconds + "ms");
    }

    private IEnumerator PostLoadGC()
    {
        yield return new WaitForSeconds(1f);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        Log.LogDebug("Post-load GC complete.");
    }

    private IEnumerator PostUnloadGC()
    {
        yield return null;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        Log.LogDebug("Post-unload GC complete.");
    }

    private void OnDestroy()
    {
        if (_loadActive) OnLoadComplete();
    }
}
