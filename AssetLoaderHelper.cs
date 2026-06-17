using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("com.zerot.assetloaderhelper", "AssetLoaderHelper", "1.0.0")]
public class AssetLoaderHelper : BaseUnityPlugin
{
    private ConfigEntry<int>   cfgMaxParallelBundles;
    private ConfigEntry<int>   cfgMaxParallelScenes;
    private ConfigEntry<int>   cfgTextureWorkerCount;
    private ConfigEntry<bool>  cfgLogSlowLoads;
    private ConfigEntry<float> cfgSlowLoadThresholdMs;
    private ConfigEntry<bool>  cfgLogStatsOnStart;
    private ConfigEntry<bool>  cfgPeriodicStats;
    private ConfigEntry<float> cfgPeriodicStatsInterval;
    private ConfigEntry<KeyCode> cfgHotkeyStats;

    private static ManualLogSource Log;
    private static AssetLoaderHelper Instance;

    private Type   _assetLoaderType;
    private Type   _imageLoaderType;
    private object _assetLoaderSingleton;
    private object _imageLoaderSingleton;

    private FieldInfo _fiMaxAbLoads;
    private FieldInfo _fiMaxSceneLoads;
    private FieldInfo _fiWorkerCount;
    private FieldInfo _fiActiveWorkers;
    private FieldInfo _fiAbQueue;
    private FieldInfo _fiSceneQueue;
    private FieldInfo _fiStatDispatched;
    private FieldInfo _fiStatMemHit;
    private FieldInfo _fiStatDiskHit;
    private FieldInfo _fiStatNew;
    private FieldInfo _fiStatErrors;
    private FieldInfo _fiTextureCache;
    private FieldInfo _fiThumbnailCache;

    private float _nextStatTime;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        int cpu = Environment.ProcessorCount;

        cfgMaxParallelBundles  = Config.Bind("AssetLoader", "MaxParallelBundleLoads", Math.Max(4, cpu / 2),
            "Max concurrent asset-bundle loads (VaM default: 2). Higher = faster load on fast drives.");
        cfgMaxParallelScenes   = Config.Bind("AssetLoader", "MaxParallelSceneLoads", Math.Max(2, cpu / 4),
            "Max concurrent scene-into-transform loads (VaM default: 1).");
        cfgTextureWorkerCount  = Config.Bind("ImageLoader", "TextureWorkerCount", Math.Max(4, cpu / 2),
            "Texture loading worker threads (VaM default: 2). More = faster texture streaming.");
        cfgLogSlowLoads        = Config.Bind("Logging", "LogSlowLoads", true,
            "Log asset loads that exceed SlowLoadThresholdMs.");
        cfgSlowLoadThresholdMs = Config.Bind("Logging", "SlowLoadThresholdMs", 500f,
            "Threshold in ms above which a load is considered slow.");
        cfgLogStatsOnStart     = Config.Bind("Stats", "LogStatsOnStart", true,
            "Print current AssetLoader configuration on startup.");
        cfgPeriodicStats       = Config.Bind("Stats", "PeriodicStats", false,
            "Print texture cache stats periodically.");
        cfgPeriodicStatsInterval = Config.Bind("Stats", "PeriodicStatsIntervalSeconds", 60f,
            "How often to print stats when PeriodicStats is enabled.");
        cfgHotkeyStats         = Config.Bind("Hotkeys", "PrintStats", KeyCode.F7,
            "Print asset loader stats to log.");

        var h = new Harmony("com.zerot.assetloaderhelper");
        PatchAssetLoader(h);
        PatchImageLoader(h);
        PatchBundleManager(h);
        StartCoroutine(ResolveAndApplyDelayed());
    }

    private IEnumerator ResolveAndApplyDelayed()
    {
        yield return new WaitForSeconds(1f);
        ResolveTypes();
        ApplyWorkerCounts();
        if (cfgLogStatsOnStart.Value) LogStats();
    }

    private void ResolveTypes()
    {
        _assetLoaderType  = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
        _imageLoaderType  = Type.GetType("ImageLoaderThreaded, Assembly-CSharp");

        if (_assetLoaderType != null)
        {
            var si = _assetLoaderType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
            if (si != null) _assetLoaderSingleton = si.GetValue(null);
            _fiMaxAbLoads    = _assetLoaderType.GetField("MAX_PARALLEL_AB_LOADS",    BindingFlags.Instance | BindingFlags.Public);
            _fiMaxSceneLoads = _assetLoaderType.GetField("MAX_PARALLEL_SCENE_LOADS", BindingFlags.Instance | BindingFlags.Public);
            _fiActiveWorkers = _assetLoaderType.GetField("_activeWorkerCount",       BindingFlags.Instance | BindingFlags.NonPublic);
            _fiAbQueue       = _assetLoaderType.GetField("assetBundleFromFileQueue",        BindingFlags.Instance | BindingFlags.NonPublic);
            _fiSceneQueue    = _assetLoaderType.GetField("sceneLoadIntoTransformQueue",     BindingFlags.Instance | BindingFlags.NonPublic);
        }
        if (_imageLoaderType != null)
        {
            var si = _imageLoaderType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
            if (si != null) _imageLoaderSingleton = si.GetValue(null);
            _fiWorkerCount    = _imageLoaderType.GetField("WORKER_COUNT",          BindingFlags.Instance | BindingFlags.Public);
            _fiStatDispatched = _imageLoaderType.GetField("_statTotalDispatched",  BindingFlags.Instance | BindingFlags.NonPublic);
            _fiStatMemHit     = _imageLoaderType.GetField("_statMemoryCacheHit",   BindingFlags.Instance | BindingFlags.NonPublic);
            _fiStatDiskHit    = _imageLoaderType.GetField("_statDiskCacheHit",     BindingFlags.Instance | BindingFlags.NonPublic);
            _fiStatNew        = _imageLoaderType.GetField("_statNewLoad",          BindingFlags.Instance | BindingFlags.NonPublic);
            _fiStatErrors     = _imageLoaderType.GetField("_statErrors",           BindingFlags.Instance | BindingFlags.NonPublic);
            _fiTextureCache   = _imageLoaderType.GetField("textureCache",          BindingFlags.Instance | BindingFlags.NonPublic);
            _fiThumbnailCache = _imageLoaderType.GetField("thumbnailCache",        BindingFlags.Instance | BindingFlags.NonPublic);
        }
        Log.LogInfo("[AssetLoaderHelper] Types resolved" +
            "  AssetLoader=" + (_assetLoaderSingleton != null ? "OK" : "MISSING") +
            "  ImageLoader=" + (_imageLoaderSingleton != null ? "OK" : "MISSING"));
    }

    private void PatchAssetLoader(Harmony h)
    {
        var t = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
        if (t == null) { Log.LogWarning("MeshVR.AssetLoader not found"); return; }
        var m = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) { Log.LogWarning("AssetLoader.Awake not found"); return; }
        h.Patch(m, null, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Postfix_AssetLoaderAwake",
            BindingFlags.Static | BindingFlags.Public)));
        Log.LogDebug("Patched MeshVR.AssetLoader.Awake");
    }

    private void PatchImageLoader(Harmony h)
    {
        var t = Type.GetType("ImageLoaderThreaded, Assembly-CSharp");
        if (t == null) { Log.LogWarning("ImageLoaderThreaded not found"); return; }
        var m = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m != null)
            h.Patch(m, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Prefix_ImageLoaderAwake",
                BindingFlags.Static | BindingFlags.Public)));
        Log.LogDebug("Patched ImageLoaderThreaded.Awake");
    }

    private void PatchBundleManager(Harmony h)
    {
        var t = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
        if (t == null) return;
        var m = t.GetMethod("LoadBundleFileAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) { Log.LogDebug("LoadBundleFileAsync not found -- skipping timing patch"); return; }
        h.Patch(m, null, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Postfix_BundleLoaded",
            BindingFlags.Static | BindingFlags.Public)));
        Log.LogDebug("Patched MeshVR.AssetLoader.LoadBundleFileAsync");
    }

    public static void Postfix_AssetLoaderAwake(object __instance)
    {
        if (Instance == null) return;
        try
        {
            var f1 = __instance.GetType().GetField("MAX_PARALLEL_AB_LOADS",    BindingFlags.Instance | BindingFlags.Public);
            var f2 = __instance.GetType().GetField("MAX_PARALLEL_SCENE_LOADS", BindingFlags.Instance | BindingFlags.Public);
            f1?.SetValue(__instance, Instance.cfgMaxParallelBundles.Value);
            f2?.SetValue(__instance, Instance.cfgMaxParallelScenes.Value);
            Log.LogInfo("[AssetLoaderHelper] AssetLoader limits set" +
                "  AB=" + Instance.cfgMaxParallelBundles.Value +
                "  Scene=" + Instance.cfgMaxParallelScenes.Value);
        }
        catch (Exception ex) { Log.LogWarning("Postfix_AssetLoaderAwake: " + ex.Message); }
    }

    public static void Prefix_ImageLoaderAwake(object __instance)
    {
        if (Instance == null) return;
        try
        {
            var f = __instance.GetType().GetField("WORKER_COUNT", BindingFlags.Instance | BindingFlags.Public);
            if (f != null)
            {
                f.SetValue(__instance, Instance.cfgTextureWorkerCount.Value);
                Log.LogInfo("[AssetLoaderHelper] ImageLoaderThreaded WORKER_COUNT set to " +
                    Instance.cfgTextureWorkerCount.Value);
            }
        }
        catch (Exception ex) { Log.LogWarning("Prefix_ImageLoaderAwake: " + ex.Message); }
    }

    public static void Postfix_BundleLoaded(object __instance, object[] __args)
    {
        if (Instance == null || !Instance.cfgLogSlowLoads.Value || __args == null || __args.Length == 0) return;
        try
        {
            var req = __args[0];
            if (req == null) return;
            var fTime = req.GetType().GetField("startTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fPath = req.GetType().GetField("path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? req.GetType().GetField("uid",  BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fTime == null) return;
            float elapsed = (Time.realtimeSinceStartup - (float)fTime.GetValue(req)) * 1000f;
            string label  = fPath != null ? (string)fPath.GetValue(req) : "?";
            if (elapsed > Instance.cfgSlowLoadThresholdMs.Value)
                Log.LogWarning($"[AssetLoaderHelper] SLOW BUNDLE {Math.Round(elapsed)}ms  {label}");
            else
                Log.LogDebug($"[AssetLoaderHelper] bundle {Math.Round(elapsed)}ms  {label}");
        }
        catch { }
    }

    private void ApplyWorkerCounts()
    {
        if (_assetLoaderSingleton != null)
        {
            _fiMaxAbLoads?.SetValue(_assetLoaderSingleton, cfgMaxParallelBundles.Value);
            _fiMaxSceneLoads?.SetValue(_assetLoaderSingleton, cfgMaxParallelScenes.Value);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(cfgHotkeyStats.Value)) LogStats();
        if (cfgPeriodicStats.Value && Time.realtimeSinceStartup > _nextStatTime)
        {
            _nextStatTime = Time.realtimeSinceStartup + cfgPeriodicStatsInterval.Value;
            LogStats();
        }
    }

    private void LogStats()
    {
        Log.LogInfo("[AssetLoaderHelper] ===== ASSET LOADER STATS =====");
        if (_assetLoaderSingleton != null)
        {
            int ab      = _fiMaxAbLoads    != null ? (int)_fiMaxAbLoads.GetValue(_assetLoaderSingleton)    : -1;
            int sc      = _fiMaxSceneLoads != null ? (int)_fiMaxSceneLoads.GetValue(_assetLoaderSingleton) : -1;
            int workers = _fiActiveWorkers != null ? (int)_fiActiveWorkers.GetValue(_assetLoaderSingleton) : -1;
            int abQ = -1, scQ = -1;
            if (_fiAbQueue != null)
            {
                var q = _fiAbQueue.GetValue(_assetLoaderSingleton);
                if (q != null) { var p = q.GetType().GetProperty("Count"); if (p != null) abQ = (int)p.GetValue(q, null); }
            }
            if (_fiSceneQueue != null)
            {
                var q = _fiSceneQueue.GetValue(_assetLoaderSingleton);
                if (q != null) { var p = q.GetType().GetProperty("Count"); if (p != null) scQ = (int)p.GetValue(q, null); }
            }
            Log.LogInfo("  [AssetLoader]");
            Log.LogInfo($"    MAX_PARALLEL_AB_LOADS    : {ab}");
            Log.LogInfo($"    MAX_PARALLEL_SCENE_LOADS : {sc}");
            Log.LogInfo($"    Active workers           : {workers}");
            Log.LogInfo($"    Bundle queue depth       : {abQ}");
            Log.LogInfo($"    Scene queue depth        : {scQ}");
        }
        else Log.LogInfo("  [AssetLoader] singleton not found");

        if (_imageLoaderSingleton != null)
        {
            int workers   = _fiWorkerCount    != null ? (int)_fiWorkerCount.GetValue(_imageLoaderSingleton)    : -1;
            int disp      = _fiStatDispatched != null ? (int)_fiStatDispatched.GetValue(_imageLoaderSingleton) : -1;
            int memHit    = _fiStatMemHit     != null ? (int)_fiStatMemHit.GetValue(_imageLoaderSingleton)     : -1;
            int diskHit   = _fiStatDiskHit    != null ? (int)_fiStatDiskHit.GetValue(_imageLoaderSingleton)    : -1;
            int newLoad   = _fiStatNew        != null ? (int)_fiStatNew.GetValue(_imageLoaderSingleton)        : -1;
            int errors    = _fiStatErrors     != null ? (int)_fiStatErrors.GetValue(_imageLoaderSingleton)     : -1;
            int texCount  = -1, thumbCount = -1;
            if (_fiTextureCache != null)
            {
                var c = _fiTextureCache.GetValue(_imageLoaderSingleton);
                if (c != null) { var p = c.GetType().GetProperty("Count"); if (p != null) texCount = (int)p.GetValue(c, null); }
            }
            if (_fiThumbnailCache != null)
            {
                var c = _fiThumbnailCache.GetValue(_imageLoaderSingleton);
                if (c != null) { var p = c.GetType().GetProperty("Count"); if (p != null) thumbCount = (int)p.GetValue(c, null); }
            }
            float hitRate = disp > 0 ? (float)Math.Round((float)(memHit + diskHit) * 100f / disp, 1) : 0f;
            Log.LogInfo("  [ImageLoader]");
            Log.LogInfo($"    Worker threads    : {workers}");
            Log.LogInfo($"    Total dispatched  : {disp}");
            Log.LogInfo($"    Memory cache hits : {memHit}");
            Log.LogInfo($"    Disk cache hits   : {diskHit}");
            Log.LogInfo($"    New loads         : {newLoad}");
            Log.LogInfo($"    Errors            : {errors}");
            Log.LogInfo($"    Cache hit rate    : {hitRate}%");
            Log.LogInfo($"    Texture cache     : {texCount} entries");
            Log.LogInfo($"    Thumbnail cache   : {thumbCount} entries");
        }
        else Log.LogInfo("  [ImageLoader] singleton not found");
        Log.LogInfo("[AssetLoaderHelper] ================================");
    }
}
