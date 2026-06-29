// LocationIndependence — BepInEx plugin
// Keeps the VaM install working after it's moved to a different drive letter
// or folder. Detects drift between the last known install root and the
// current one, then rewrites any stale absolute paths found in BepInEx
// config files and Saves/scene + preset JSON files.
//
// This is broader than SelfPathFixer (com.zerot.selfpathfixer), which only
// fixes SELF:/ references inside .var packages — this plugin fixes literal
// absolute-path strings (e.g. "J:\New folder\...") left behind anywhere
// that text-based config/scene data is stored.
//
// Settings: BepInEx/config/com.vam.locationindependence.cfg

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VaMUtility
{
    [BepInPlugin("com.vam.locationindependence", "LocationIndependence", "1.0.0")]
    public class LocationIndependencePlugin : BaseUnityPlugin
    {
        ConfigEntry<bool> cfgEnabled;
        ConfigEntry<bool> cfgFixConfigFiles;
        ConfigEntry<bool> cfgFixSceneFiles;
        ConfigEntry<bool> cfgBackup;
        ConfigEntry<bool> cfgDryRun;

        static readonly string[] SceneScanExtensions = { "*.json", "*.vap", "*.vam" };

        void Awake()
        {
            cfgEnabled        = Config.Bind("General", "Enabled", true,
                "Detect drive/folder moves and rewrite stale absolute paths.");
            cfgFixConfigFiles = Config.Bind("General", "FixConfigFiles", true,
                "Rewrite stale absolute paths inside BepInEx/config/*.cfg files.");
            cfgFixSceneFiles  = Config.Bind("General", "FixSceneFiles", true,
                "Rewrite stale absolute paths inside Saves/**/*.json, *.vap, *.vam files.");
            cfgBackup         = Config.Bind("General", "BackupBeforeFix", true,
                "Create a .bak copy of each file before modifying it.");
            cfgDryRun         = Config.Bind("General", "DryRun", false,
                "Log what would change without writing any files.");

            StartCoroutine(StartupCoroutine());
            Logger.LogInfo("LocationIndependence BepInEx plugin loaded.");
        }

        IEnumerator StartupCoroutine()
        {
            while (SuperController.singleton == null) yield return null;
            yield return null;

            if (!cfgEnabled.Value) yield break;
            CheckForDrift();
        }

        string CachePath => Path.Combine(Path.Combine(Paths.BepInExRootPath, "cache"), "lastroot.txt");

        void CheckForDrift()
        {
            string currentRoot = Directory.GetCurrentDirectory().TrimEnd('\\', '/');
            string cachePath = CachePath;

            string lastRoot = null;
            try { if (File.Exists(cachePath)) lastRoot = File.ReadAllText(cachePath).Trim(); }
            catch (Exception e) { Logger.LogError("LocationIndependence: failed reading cache: " + e); }

            if (string.IsNullOrEmpty(lastRoot))
            {
                Logger.LogInfo("LocationIndependence: no previous root recorded — storing current root, nothing to fix.");
                WriteCurrentRoot(currentRoot, cachePath);
                return;
            }

            if (string.Equals(lastRoot, currentRoot, StringComparison.OrdinalIgnoreCase))
            {
                return; // no drift
            }

            Logger.LogInfo($"LocationIndependence: install moved from '{lastRoot}' to '{currentRoot}'. Scanning for stale paths...");

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int configFixed = cfgFixConfigFiles.Value ? FixConfigFiles(lastRoot, currentRoot) : 0;
                    int sceneFixed  = cfgFixSceneFiles.Value  ? FixSceneFiles(lastRoot, currentRoot)  : 0;
                    Logger.LogInfo($"LocationIndependence: done. {configFixed} config file(s), {sceneFixed} scene/preset file(s) updated.");
                }
                catch (Exception e)
                {
                    Logger.LogError("LocationIndependence: scan failed: " + e);
                }

                if (!cfgDryRun.Value)
                    WriteCurrentRoot(currentRoot, cachePath);
            });
        }

        void WriteCurrentRoot(string root, string cachePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                File.WriteAllText(cachePath, root);
            }
            catch (Exception e) { Logger.LogError("LocationIndependence: failed writing cache: " + e); }
        }

        int FixConfigFiles(string oldRoot, string newRoot)
        {
            string configDir = Path.Combine(Paths.BepInExRootPath, "config");
            if (!Directory.Exists(configDir)) return 0;

            int fixedCount = 0;
            foreach (string file in Directory.GetFiles(configDir, "*.cfg", SearchOption.TopDirectoryOnly))
            {
                if (TryRewriteFile(file, oldRoot, newRoot)) fixedCount++;
            }
            return fixedCount;
        }

        int FixSceneFiles(string oldRoot, string newRoot)
        {
            string savesDir = Path.Combine(newRoot, "Saves");
            if (!Directory.Exists(savesDir)) return 0;

            int fixedCount = 0;
            foreach (string pattern in SceneScanExtensions)
            {
                foreach (string file in Directory.GetFiles(savesDir, pattern, SearchOption.AllDirectories))
                {
                    if (TryRewriteFile(file, oldRoot, newRoot)) fixedCount++;
                }
            }
            return fixedCount;
        }

        bool TryRewriteFile(string path, string oldRoot, string newRoot)
        {
            try
            {
                string text = File.ReadAllText(path);

                string oldEscaped = oldRoot.Replace("\\", "\\\\");
                string newEscaped = newRoot.Replace("\\", "\\\\");
                string oldForward = oldRoot.Replace("\\", "/");
                string newForward = newRoot.Replace("\\", "/");

                string updated = text
                    .Replace(oldEscaped, newEscaped)
                    .Replace(oldRoot, newRoot)
                    .Replace(oldForward, newForward);

                if (updated == text) return false;

                if (cfgDryRun.Value)
                {
                    Logger.LogInfo("LocationIndependence: [dry run] would update " + path);
                    return true;
                }

                if (cfgBackup.Value)
                {
                    string bak = path + ".bak";
                    if (!File.Exists(bak)) File.Copy(path, bak);
                }

                File.WriteAllText(path, updated);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError("LocationIndependence: failed rewriting " + path + ": " + e);
                return false;
            }
        }
    }
}
