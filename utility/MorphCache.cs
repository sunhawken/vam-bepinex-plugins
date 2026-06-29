using BepInEx;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Threading;

[BepInPlugin("com.vam.morphcache", "VaM Morph Cache", "1.0.0")]
public class MorphCache : BaseUnityPlugin
{
    void Awake()
    {
        Logger.LogWarning("[MorphCache] Awake start");
        try
        {
            var cfgVmb = Config.Bind("General", "PrewarmVMB", false,
                "Also pre-warm .vmb binary morph files");
            var cfgThreads = Config.Bind("General", "ThreadCount", 4,
                "Background threads (0 = CPU count)");

            Logger.LogInfo("[MorphCache] Config bound.");

            int threads = cfgThreads.Value > 0 ? cfgThreads.Value : Environment.ProcessorCount;
            bool prewarmVmb = cfgVmb.Value;
            string morphRoot = Path.Combine(Directory.GetCurrentDirectory(), "Custom", "Atom", "Person", "Morphs");

            Logger.LogInfo("[MorphCache] Morph root: " + morphRoot + " exists=" + Directory.Exists(morphRoot));

            if (!Directory.Exists(morphRoot))
            {
                Logger.LogWarning("[MorphCache] Morph directory not found, skipping.");
                return;
            }

            string[] vmiFiles = Directory.GetFiles(morphRoot, "*.vmi", SearchOption.AllDirectories);
            Logger.LogInfo(string.Format("[MorphCache] Found {0} .vmi files, queuing pre-warm on {1} threads.", vmiFiles.Length, threads));

            if (vmiFiles.Length == 0) return;

            int filesPerThread = (vmiFiles.Length + threads - 1) / threads;
            for (int t = 0; t < threads; t++)
            {
                int startIdx = t * filesPerThread;
                if (startIdx >= vmiFiles.Length) break;
                int endIdx = Math.Min(startIdx + filesPerThread, vmiFiles.Length);

                // copy slice to avoid closure capture of loop variable
                var slice = new string[endIdx - startIdx];
                Array.Copy(vmiFiles, startIdx, slice, 0, slice.Length);

                ThreadPool.QueueUserWorkItem(state =>
                {
                    var fileSlice = (string[])state;
                    var buf = new byte[65536];
                    foreach (string f in fileSlice)
                    {
                        try
                        {
                            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read,
                                FileShare.Read, 4096, FileOptions.SequentialScan))
                            {
                                while (fs.Read(buf, 0, buf.Length) > 0) { }
                            }
                        }
                        catch { }
                    }
                }, slice);
            }

            if (prewarmVmb)
            {
                string[] vmbFiles = Directory.GetFiles(morphRoot, "*.vmb", SearchOption.AllDirectories);
                if (vmbFiles.Length > 0)
                {
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        foreach (string f in (string[])state)
                        {
                            try { File.ReadAllBytes(f); } catch { }
                        }
                    }, vmbFiles);
                }
            }

            Logger.LogInfo("[MorphCache] Pre-warm queued.");
        }
        catch (Exception ex)
        {
            Logger.LogError("[MorphCache] Awake exception: " + ex);
        }
    }
}
