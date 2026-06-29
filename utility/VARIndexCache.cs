using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

// Builds and persists a content index for every .var package.
// On subsequent startups, only packages that changed on disk are re-read —
// everything else gets OS-cache pre-warming skipped entirely, saving I/O.
[BepInPlugin("com.vam.varindexcache", "VaM VAR Index Cache", "1.0.0")]
public class VARIndexCache : BaseUnityPlugin
{
    const int VERSION = 2;

    struct Entry { public long Size; public long ModTicks; public int EntryCount; }

    void Awake()
    {
        string addonPath = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
        if (!Directory.Exists(addonPath)) return;

        string cacheDir = Path.Combine(Paths.BepInExRootPath, "cache");
        string cachePath = Path.Combine(cacheDir, "var_index.cache");

        string[] files = Directory.GetFiles(addonPath, "*.var", SearchOption.AllDirectories);
        if (files.Length == 0) return;

        var cache = ReadCache(cachePath);
        var stale = new List<string>();
        int hits = 0;

        foreach (string f in files)
        {
            var fi = new FileInfo(f);
            string key = f.ToLowerInvariant();
            if (cache.TryGetValue(key, out Entry e) &&
                e.Size == fi.Length && e.ModTicks == fi.LastWriteTimeUtc.Ticks)
            {
                hits++;
            }
            else
            {
                stale.Add(f);
            }
        }

        Logger.LogInfo(string.Format("[VARIndexCache] {0} cached / {1} stale / {2} total",
            hits, stale.Count, files.Length));

        if (stale.Count == 0) return;

        // Scan stale files in background, update cache
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // Remove deleted entries
            var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files) liveKeys.Add(f.ToLowerInvariant());
            var keys = new List<string>(cache.Keys);
            foreach (string k in keys)
                if (!liveKeys.Contains(k)) cache.Remove(k);

            foreach (string f in stale)
            {
                try
                {
                    var fi = new FileInfo(f);
                    int count = 0;
                    using (var zip = ZipFile.OpenRead(f))
                        count = zip.Entries.Count;

                    cache[f.ToLowerInvariant()] = new Entry
                    {
                        Size = fi.Length,
                        ModTicks = fi.LastWriteTimeUtc.Ticks,
                        EntryCount = count
                    };
                }
                catch { }
            }

            try
            {
                Directory.CreateDirectory(cacheDir);
                WriteCache(cachePath, cache);
            }
            catch { }
        });
    }

    Dictionary<string, Entry> ReadCache(string path)
    {
        var d = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return d;
        try
        {
            using (var sr = new StreamReader(path, Encoding.UTF8))
            {
                if (sr.ReadLine() != VERSION.ToString()) return d;
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    long size, ticks; int count;
                    if (!long.TryParse(p[1], out size)) continue;
                    if (!long.TryParse(p[2], out ticks)) continue;
                    if (!int.TryParse(p[3], out count)) continue;
                    d[p[0]] = new Entry { Size = size, ModTicks = ticks, EntryCount = count };
                }
            }
        }
        catch { }
        return d;
    }

    void WriteCache(string path, Dictionary<string, Entry> cache)
    {
        using (var sw = new StreamWriter(path, false, Encoding.UTF8))
        {
            sw.WriteLine(VERSION);
            foreach (var kv in cache)
                sw.WriteLine(string.Format("{0}|{1}|{2}|{3}",
                    kv.Key, kv.Value.Size, kv.Value.ModTicks, kv.Value.EntryCount));
        }
    }
}
