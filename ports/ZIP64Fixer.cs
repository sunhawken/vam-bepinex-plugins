using BepInEx;
using BepInEx.Configuration;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

// VaM's bundled SharpZipLib cannot read ZIP64 central directories produced by
// Python's zipfile module (files over ~2 GB, or Python packing quirks).
// This plugin detects affected .var files by actually trying to open them with
// SharpZipLib, then repacks them with 7-Zip which produces a standard ZIP that
// SharpZipLib can handle.  Results are cached so only new/untested files are
// checked on subsequent launches.
[BepInPlugin("com.vam.zip64fixer", "VaM ZIP64 Fixer", "1.0.0")]
public class ZIP64Fixer : BaseUnityPlugin
{
    void Awake() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        while (SuperController.singleton == null) yield return null;
        while (SuperController.singleton.isLoading) yield return null;
        yield return null;

        string sevenZip = FindSevenZip();
        if (sevenZip == null)
        {
            Logger.LogError("[ZIP64Fixer] 7z.exe not found. Install 7-Zip to enable repacking.");
            yield break;
        }
        Logger.LogInfo("[ZIP64Fixer] Using 7-Zip at: " + sevenZip);

        string addonPath = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
        if (!Directory.Exists(addonPath)) yield break;

        string cacheDir  = Path.Combine(Paths.BepInExRootPath, "cache");
        string cachePath = Path.Combine(cacheDir, "zip64fixer.txt");
        Directory.CreateDirectory(cacheDir);

        // Cache stores paths of files already verified good or already fixed
        var done = LoadCache(cachePath);

        string[] varFiles = Directory.GetFiles(addonPath, "*.var", SearchOption.AllDirectories);
        int scanned = 0, repacked = 0, errors = 0;

        foreach (string varPath in varFiles)
        {
            if (done.Contains(varPath)) continue;
            scanned++;

            // Test with SharpZipLib — same library VaM uses
            bool bad = false;
            try
            {
                using (var zf = new ZipFile(varPath))
                    foreach (ZipEntry e in zf) { } // enumerate to stress-test central dir
            }
            catch (ZipException)  { bad = true; }
            catch (Exception ex)
            {
                Logger.LogWarning("[ZIP64Fixer] Unexpected error reading "
                    + Path.GetFileName(varPath) + ": " + ex.Message);
            }

            if (!bad)
            {
                done.Add(varPath);
                if (scanned % 50 == 0) { SaveCache(cachePath, done); yield return null; }
                continue;
            }

            Logger.LogInfo("[ZIP64Fixer] Repacking " + Path.GetFileName(varPath));

            bool success = false;
            string errMsg = null;
            bool threadDone = false;

            string capture = varPath;
            string sz = sevenZip;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try   { RepackWith7Zip(capture, sz); success = true; }
                catch (Exception ex) { errMsg = ex.Message; }
                finally { threadDone = true; }
            });

            while (!threadDone) yield return null;

            if (success)
            {
                repacked++;
                done.Add(varPath);
                Logger.LogInfo("[ZIP64Fixer] Repacked " + Path.GetFileName(varPath));
            }
            else
            {
                errors++;
                Logger.LogError("[ZIP64Fixer] Failed on " + Path.GetFileName(varPath) + ": " + errMsg);
            }

            SaveCache(cachePath, done);
            yield return null;
        }

        SaveCache(cachePath, done);

        if (scanned > 0 || repacked > 0)
            Logger.LogInfo(string.Format(
                "[ZIP64Fixer] Scanned {0} new files, repacked {1}, errors {2}.",
                scanned, repacked, errors));
        else
            Logger.LogInfo("[ZIP64Fixer] All packages OK.");
    }

    void RepackWith7Zip(string varPath, string sevenZip)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vam_zip64fix_" + Guid.NewGuid().ToString("N"));
        string tempOut = varPath + ".repack";

        try
        {
            Directory.CreateDirectory(tempDir);

            // 1. Extract original
            Run7Z(sevenZip, "x \"" + varPath + "\" -o\"" + tempDir + "\" -y", null);

            // 2. Repack from the extracted directory so paths are relative
            Run7Z(sevenZip, "a -tzip \"" + tempOut + "\" * -y -r", tempDir);

            // 3. Atomic replace
            string backup = varPath + ".bak";
            File.Move(varPath, backup);
            try
            {
                File.Move(tempOut, varPath);
                File.Delete(backup);
            }
            catch
            {
                // Restore backup on failure
                if (File.Exists(backup) && !File.Exists(varPath))
                    File.Move(backup, varPath);
                throw;
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            try { if (File.Exists(tempOut))      File.Delete(tempOut); }            catch { }
        }
    }

    void Run7Z(string exe, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        if (workDir != null) psi.WorkingDirectory = workDir;

        using (var proc = Process.Start(psi))
        {
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new Exception("7z exited " + proc.ExitCode + ": " + stderr.Trim());
        }
    }

    string FindSevenZip()
    {
        string[] candidates =
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
        };
        foreach (string p in candidates)
            if (File.Exists(p)) return p;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "where",
                Arguments              = "7z.exe",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            };
            using (var proc = Process.Start(psi))
            {
                string line = proc.StandardOutput.ReadLine();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && line != null && File.Exists(line.Trim()))
                    return line.Trim();
            }
        }
        catch { }

        return null;
    }

    HashSet<string> LoadCache(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;
        try { foreach (var line in File.ReadAllLines(path)) if (line.Length > 0) set.Add(line); }
        catch { }
        return set;
    }

    void SaveCache(string path, HashSet<string> set)
    {
        try { File.WriteAllLines(path, new List<string>(set).ToArray()); }
        catch { }
    }
}
