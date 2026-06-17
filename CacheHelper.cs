using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("com.zerot.cachehelper", "CacheHelper", "1.0.0")]
public class CacheHelper : BaseUnityPlugin
{
    private ConfigEntry<string>  cfgCacheFolder;
    private ConfigEntry<int>     cfgMaxSizeGB;
    private ConfigEntry<int>     cfgMaxAgeDays;
    private ConfigEntry<bool>    cfgAutoCleanOnStart;
    private ConfigEntry<bool>    cfgAutoCleanOnSceneLoad;
    private ConfigEntry<bool>    cfgUnloadUnusedOnScene;
    private ConfigEntry<long>    cfgUnityCacheMaxMB;
    private ConfigEntry<int>     cfgUnityCacheExpireDays;
    private ConfigEntry<KeyCode> cfgHotkeyClean;
    private ConfigEntry<KeyCode> cfgHotkeyUnload;
    private ConfigEntry<KeyCode> cfgHotkeyStats;

    private static ManualLogSource Log;
    private string _resolvedCacheFolder;

    private void Awake()
    {
        Log = Logger;
        string vamRoot  = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
        string autoPath = Path.Combine(Path.GetPathRoot(vamRoot).TrimEnd('\\'), "Cache");

        cfgCacheFolder         = Config.Bind("Cache", "CacheFolder", autoPath,
            "Path to VaM cache folder. Leave blank to auto-detect from prefs.json.");
        cfgMaxSizeGB           = Config.Bind("Cache", "MaxSizeGB", 20,
            "Auto-evict oldest files when cache exceeds this size in GB. 0 = disabled.");
        cfgMaxAgeDays          = Config.Bind("Cache", "MaxAgeDays", 30,
            "Delete cache files not accessed in this many days. 0 = disabled.");
        cfgAutoCleanOnStart    = Config.Bind("Cache", "AutoCleanOnStart", true,
            "Run cache cleanup when VaM starts.");
        cfgAutoCleanOnSceneLoad = Config.Bind("Cache", "AutoCleanOnSceneLoad", false,
            "Run cache cleanup on every scene load (heavier, but keeps cache tidy).");
        cfgUnloadUnusedOnScene = Config.Bind("Cache", "UnloadUnusedAssetsOnSceneLoad", true,
            "Call Resources.UnloadUnusedAssets() after each scene finishes loading.");
        cfgUnityCacheMaxMB     = Config.Bind("Unity Cache", "MaxDiskSpaceMB", 8192L,
            "Maximum MB Unity may use for its asset-bundle cache (default ~unlimited).");
        cfgUnityCacheExpireDays = Config.Bind("Unity Cache", "ExpirationDays", 30,
            "Days before Unity considers a cached asset bundle stale.");
        cfgHotkeyClean  = Config.Bind("Hotkeys", "CleanCache",    KeyCode.F1, "Manually trigger cache cleanup.");
        cfgHotkeyUnload = Config.Bind("Hotkeys", "UnloadAssets",  KeyCode.F2, "Manually call UnloadUnusedAssets.");
        cfgHotkeyStats  = Config.Bind("Hotkeys", "PrintStats",    KeyCode.F3, "Print cache stats to BepInEx log.");

        _resolvedCacheFolder = ResolveCacheFolder(vamRoot);
        ApplyUnityCache();

        if (cfgAutoCleanOnStart.Value)
            StartCoroutine(CleanCacheCoroutine("startup"));

        SceneManager.sceneLoaded += OnSceneLoaded;
        Log.LogInfo("CacheHelper v1.0.0 active  |  cache=" + _resolvedCacheFolder);
        LogCacheStats();
    }

    private string ResolveCacheFolder(string vamRoot)
    {
        string cfg = cfgCacheFolder.Value.Trim();
        if (!string.IsNullOrEmpty(cfg) && Directory.Exists(cfg)) return cfg;

        string prefs = Path.Combine(vamRoot, "prefs.json");
        if (File.Exists(prefs))
        {
            string text = File.ReadAllText(prefs);
            string key  = "\"cacheFolder\"";
            int idx = text.IndexOf(key);
            if (idx >= 0)
            {
                int q1 = text.IndexOf('"', idx + key.Length + 1);
                int q2 = text.IndexOf('"', q1 + 1);
                if (q1 >= 0 && q2 > q1)
                {
                    string found = text.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\\", "\\");
                    if (Directory.Exists(found)) return found;
                }
            }
        }
        return Path.Combine(Path.GetPathRoot(vamRoot).TrimEnd('\\'), "Cache");
    }

    private void ApplyUnityCache()
    {
        if (cfgUnityCacheMaxMB.Value > 0)
            Caching.maximumAvailableDiskSpace = cfgUnityCacheMaxMB.Value * 1024 * 1024;
        if (cfgUnityCacheExpireDays.Value > 0)
            Caching.expirationDelay = cfgUnityCacheExpireDays.Value * 86400;
        Log.LogDebug($"Unity cache: max={cfgUnityCacheMaxMB.Value}MB  expire={cfgUnityCacheExpireDays.Value}d  ready={Caching.ready}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (cfgUnloadUnusedOnScene.Value)    StartCoroutine(UnloadUnusedCoroutine());
        if (cfgAutoCleanOnSceneLoad.Value)   StartCoroutine(CleanCacheCoroutine("scene-load"));
    }

    private IEnumerator UnloadUnusedCoroutine()
    {
        yield return new WaitForSeconds(2f);
        yield return Resources.UnloadUnusedAssets();
        Log.LogDebug("UnloadUnusedAssets complete");
    }

    private void Update()
    {
        if (Input.GetKeyDown(cfgHotkeyClean.Value))  StartCoroutine(CleanCacheCoroutine("hotkey"));
        if (Input.GetKeyDown(cfgHotkeyUnload.Value)) StartCoroutine(UnloadUnusedCoroutine());
        if (Input.GetKeyDown(cfgHotkeyStats.Value))  LogCacheStats();
    }

    private IEnumerator CleanCacheCoroutine(string reason)
    {
        yield return null;
        Log.LogInfo($"[CacheHelper] Starting cleanup  ({reason})  folder={_resolvedCacheFolder}");
        if (!Directory.Exists(_resolvedCacheFolder))
        {
            Log.LogWarning("[CacheHelper] Cache folder not found: " + _resolvedCacheFolder);
            yield break;
        }

        long removedBytes = 0;
        int  removedCount = 0;

        if (cfgMaxAgeDays.Value > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-cfgMaxAgeDays.Value);
            foreach (var fi in Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories)
                                        .Select(f => new FileInfo(f))
                                        .Where(f => f.LastAccessTimeUtc < cutoff))
            {
                try { long len = fi.Length; fi.Delete(); removedBytes += len; removedCount++; }
                catch { }
            }
            Log.LogInfo($"[CacheHelper] Age pass: removed {removedCount} files  ({FormatBytes(removedBytes)})");
        }

        yield return null;

        if (cfgMaxSizeGB.Value > 0)
        {
            long limit = (long)cfgMaxSizeGB.Value * 1024L * 1024 * 1024;
            long size  = GetDirectorySize(_resolvedCacheFolder);
            if (size > limit)
            {
                Log.LogInfo($"[CacheHelper] Cache {FormatBytes(size)} > limit {cfgMaxSizeGB.Value}GB -- evicting oldest...");
                foreach (var fi in Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories)
                                            .Select(f => new FileInfo(f))
                                            .OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (size <= limit) break;
                    try { long len = fi.Length; fi.Delete(); size -= len; removedBytes += len; removedCount++; }
                    catch { }
                }
                Log.LogInfo($"[CacheHelper] Size pass: removed {removedCount} files total  ({FormatBytes(removedBytes)})  remaining={FormatBytes(size)}");
            }
            else Log.LogInfo($"[CacheHelper] Cache size {FormatBytes(size)} within limit -- no size eviction.");
        }

        RemoveEmptyDirectories(_resolvedCacheFolder);
        Log.LogInfo("[CacheHelper] Cleanup done.");
    }

    private void LogCacheStats()
    {
        if (!Directory.Exists(_resolvedCacheFolder))
        {
            Log.LogInfo("[CacheHelper] Cache folder not found: " + _resolvedCacheFolder);
            return;
        }
        var files = Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories);
        long total = files.Select(f => new FileInfo(f)).Sum(f => f.Length);
        Log.LogInfo("[CacheHelper] ===== CACHE STATS =====");
        Log.LogInfo("  Folder : " + _resolvedCacheFolder);
        Log.LogInfo("  Files  : " + files.Length);
        Log.LogInfo("  Size   : " + FormatBytes(total));
        Log.LogInfo("  Limit  : " + (cfgMaxSizeGB.Value > 0 ? cfgMaxSizeGB.Value + " GB" : "none"));
        Log.LogInfo("  MaxAge : " + (cfgMaxAgeDays.Value > 0 ? cfgMaxAgeDays.Value + " days" : "none"));
        Log.LogInfo($"  Unity  : ready={Caching.ready}  spaceOccupied={FormatBytes(Caching.spaceOccupied)}");
        Log.LogInfo("[CacheHelper] =======================");
    }

    private static long GetDirectorySize(string path) =>
        Directory.GetFiles(path, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f).Length).Sum();

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                                        .OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch { }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return Math.Round(bytes / 1_073_741_824.0, 2) + " GB";
        if (bytes >= 1_048_576)     return Math.Round(bytes / 1_048_576.0, 1)     + " MB";
        if (bytes >= 1_024)         return Math.Round(bytes / 1_024.0, 1)          + " KB";
        return bytes + " B";
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
