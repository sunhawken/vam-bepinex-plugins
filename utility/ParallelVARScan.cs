using BepInEx;
using BepInEx.Configuration;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

[BepInPlugin("com.vam.parallelvarscan", "VaM Parallel VAR Scan", "1.0.0")]
public class ParallelVARScan : BaseUnityPlugin
{
    void Awake()
    {
        var cfgThreads = Config.Bind("General", "ThreadCount", 0,
            "Parallel scan threads (0 = CPU core count)");

        string addonPath = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
        if (!Directory.Exists(addonPath)) return;

        string[] files = Directory.GetFiles(addonPath, "*.var", SearchOption.AllDirectories);
        if (files.Length == 0) return;

        int threads = cfgThreads.Value > 0 ? cfgThreads.Value : Environment.ProcessorCount;
        threads = Math.Min(threads, files.Length);

        Logger.LogInfo(string.Format("[ParallelVARScan] Pre-warming {0} packages on {1} threads.", files.Length, threads));

        int perThread = (files.Length + threads - 1) / threads;

        for (int t = 0; t < threads; t++)
        {
            int start = t * perThread;
            int end = Math.Min(start + perThread, files.Length);
            if (start >= files.Length) break;

            string[] slice = new string[end - start];
            Array.Copy(files, start, slice, 0, slice.Length);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (string f in slice)
                {
                    try
                    {
                        // Reading Entries reads only the ZIP central directory (fast, end-of-file seek)
                        using (var zip = ZipFile.OpenRead(f))
                        {
                            int n = zip.Entries.Count;
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
