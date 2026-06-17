using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ICSharpCode.SharpZipLib.Zip;

// Scans all .var files in AddonPackages and scene JSON files in Saves/scene
// for "SELF:/" references and replaces them with the correct package name prefix.
// ALL file I/O runs on a background thread — zero main-thread blocking.

[BepInPlugin("com.zerot.selfpathfixer", "SelfPathFixer", "1.0.0")]
public class SelfPathFixer : BaseUnityPlugin
{
    ConfigEntry<bool>    cfgRunOnStartup;
    ConfigEntry<bool>    cfgBackupVars;
    ConfigEntry<bool>    cfgFixScenes;
    ConfigEntry<KeyCode> cfgHotkey;
    ConfigEntry<bool>    cfgDryRun;

    static ManualLogSource Log;

    // Thread-safe log queue: background thread enqueues, Update() drains on main thread
    readonly Queue<string> _logQueue = new Queue<string>();
    readonly object        _logLock  = new object();
    bool _running = false;

    void Awake()
    {
        Log = Logger;

        cfgRunOnStartup = Config.Bind("General", "RunOnStartup", true,
            "Automatically scan and fix SELF:/ references when VaM starts.");
        cfgBackupVars   = Config.Bind("General", "BackupVarFiles", true,
            "Create a .bak copy of each .var before modifying it.");
        cfgFixScenes    = Config.Bind("General", "FixSceneFiles", true,
            "Also scan and fix SELF:/ references in Saves/scene JSON files.");
        cfgHotkey       = Config.Bind("General", "Hotkey", KeyCode.None,
            "Press to trigger a manual scan. None = disabled.");
        cfgDryRun       = Config.Bind("General", "DryRun", false,
            "Log what would change without writing any files.");

        if (cfgRunOnStartup.Value)
            StartCoroutine(StartAfterDelay(5f));

        Log.LogInfo("SelfPathFixer v1.0.0 ready.");
    }

    void Update()
    {
        // Drain the log queue — one message per frame to stay cheap
        lock (_logLock)
        {
            if (_logQueue.Count > 0)
                Log.LogInfo(_logQueue.Dequeue());
        }

        if (cfgHotkey.Value != KeyCode.None && Input.GetKeyDown(cfgHotkey.Value))
            StartCoroutine(StartAfterDelay(0f));
    }

    IEnumerator StartAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        if (_running)
        {
            Log.LogWarning("SelfPathFixer: Already running, skipping.");
            yield break;
        }

        _running = true;

        string vamRoot  = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
        string addonDir = Path.Combine(vamRoot, "AddonPackages");
        string sceneDir = Path.Combine(vamRoot, "Saves", "scene");
        bool   backup   = cfgBackupVars.Value;
        bool   scenes   = cfgFixScenes.Value;
        bool   dryRun   = cfgDryRun.Value;
        Queue<string> logQ = _logQueue;
        object        logL = _logLock;

        // Kick off on a background thread
        bool done = false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try   { WorkerRun(addonDir, sceneDir, backup, scenes, dryRun, logQ, logL); }
            catch (Exception ex) { Enqueue(logQ, logL, "SelfPathFixer ERROR: " + ex); }
            finally { done = true; }
        });

        // Yield on main thread until background work finishes
        while (!done) yield return null;

        _running = false;
    }

    // ── Background worker (never touches Unity API) ───────────────────────────
    static void WorkerRun(string addonDir, string sceneDir,
                          bool backup, bool fixScenes, bool dryRun,
                          Queue<string> logQ, object logL)
    {
        string mode = dryRun ? "[DRY RUN] " : "";
        Enqueue(logQ, logL, mode + "SelfPathFixer: Starting scan...");

        int varFixed = 0, varScanned = 0, sceneFixed = 0, sceneScanned = 0;

        // ── .var files ────────────────────────────────────────────────────────
        if (Directory.Exists(addonDir))
        {
            string[] varFiles = Directory.GetFiles(addonDir, "*.var", SearchOption.AllDirectories);

            foreach (string varPath in varFiles)
            {
                varScanned++;

                string pkgName = Path.GetFileNameWithoutExtension(varPath);
                if (pkgName.EndsWith(".disabled"))
                    pkgName = pkgName.Substring(0, pkgName.Length - ".disabled".Length);

                try
                {
                    if (FixVarFile(varPath, pkgName, backup, dryRun))
                    {
                        varFixed++;
                        Enqueue(logQ, logL, mode + "Fixed var: " + pkgName);
                    }
                }
                catch (Exception ex)
                {
                    Enqueue(logQ, logL, "SelfPathFixer: " + pkgName + " -- " + ex.Message);
                }
            }
        }
        else
        {
            Enqueue(logQ, logL, "SelfPathFixer: AddonPackages not found: " + addonDir);
        }

        // ── Scene JSON files ──────────────────────────────────────────────────
        if (fixScenes && Directory.Exists(sceneDir))
        {
            string[] sceneFiles = Directory.GetFiles(sceneDir, "*.json", SearchOption.AllDirectories);

            foreach (string scenePath in sceneFiles)
            {
                sceneScanned++;
                try
                {
                    if (FixSceneFile(scenePath, dryRun))
                    {
                        sceneFixed++;
                        Enqueue(logQ, logL, mode + "Fixed scene: " + Path.GetFileName(scenePath));
                    }
                }
                catch (Exception ex)
                {
                    Enqueue(logQ, logL, "SelfPathFixer: scene " + Path.GetFileName(scenePath) + " -- " + ex.Message);
                }
            }
        }

        Enqueue(logQ, logL, mode + "SelfPathFixer: Done."
            + "  vars=" + varScanned + " fixed=" + varFixed
            + "  scenes=" + sceneScanned + " fixed=" + sceneFixed);
    }

    static bool FixVarFile(string varPath, string pkgName, bool backup, bool dryRun)
    {
        if (!FileContainsBytes(varPath, Encoding.UTF8.GetBytes("SELF:/")))
            return false;

        if (dryRun) return true;

        if (backup)
        {
            string bak = varPath + ".bak";
            if (!File.Exists(bak)) File.Copy(varPath, bak);
        }

        string replacement = pkgName + ":/";
        string tmpPath = varPath + ".tmp";
        bool anyFixed = false;

        using (ZipFile src = new ZipFile(varPath))
        using (ZipOutputStream dst = new ZipOutputStream(File.Create(tmpPath)))
        {
            dst.SetLevel(6);

            foreach (ZipEntry entry in src)
            {
                byte[] data;
                using (Stream s = src.GetInputStream(entry))
                    data = ReadAllBytes(s);

                if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string text = Encoding.UTF8.GetString(data);
                    if (text.Contains("SELF:/"))
                    {
                        data     = new UTF8Encoding(false).GetBytes(text.Replace("SELF:/", replacement));
                        anyFixed = true;
                    }
                }

                ZipEntry newEntry = new ZipEntry(entry.Name);
                newEntry.DateTime = entry.DateTime;
                newEntry.Size     = data.Length;
                dst.PutNextEntry(newEntry);
                dst.Write(data, 0, data.Length);
                dst.CloseEntry();
            }
        }

        if (anyFixed)
        {
            File.Delete(varPath);
            File.Move(tmpPath, varPath);
        }
        else
        {
            File.Delete(tmpPath);
        }

        return anyFixed;
    }

    static bool FixSceneFile(string jsonPath, bool dryRun)
    {
        string content = File.ReadAllText(jsonPath, Encoding.UTF8);
        if (!content.Contains("SELF:/")) return false;
        if (dryRun) return true;
        File.WriteAllText(jsonPath, content.Replace("SELF:/", ""), new UTF8Encoding(false));
        return true;
    }

    // Fast raw-byte scan — avoids opening zips that don't need fixing.
    static bool FileContainsBytes(string path, byte[] needle)
    {
        byte[] buf   = new byte[65536];
        byte[] carry = new byte[needle.Length - 1];
        bool first   = true;

        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read;
            while ((read = fs.Read(buf, 0, buf.Length)) > 0)
            {
                byte[] window;
                if (first)
                {
                    window = new byte[read];
                    Buffer.BlockCopy(buf, 0, window, 0, read);
                    first = false;
                }
                else
                {
                    window = new byte[carry.Length + read];
                    Buffer.BlockCopy(carry, 0, window, 0, carry.Length);
                    Buffer.BlockCopy(buf, 0, window, carry.Length, read);
                }

                if (IndexOf(window, needle) >= 0) return true;

                int carryLen = Math.Min(needle.Length - 1, read);
                Buffer.BlockCopy(buf, read - carryLen, carry, 0, carryLen);
            }
        }
        return false;
    }

    static int IndexOf(byte[] haystack, byte[] needle)
    {
        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    static byte[] ReadAllBytes(Stream stream)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            byte[] buf = new byte[4096];
            int read;
            while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                ms.Write(buf, 0, read);
            return ms.ToArray();
        }
    }

    static void Enqueue(Queue<string> q, object lk, string msg)
    {
        lock (lk) q.Enqueue(msg);
    }
}
