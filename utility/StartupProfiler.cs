using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[BepInPlugin("com.vam.startupprofiler", "VaM Startup Profiler", "1.0.0")]
public class StartupProfiler : BaseUnityPlugin
{
    struct Entry { public string Phase; public float T; public float D; }
    readonly List<Entry> _log = new List<Entry>();
    float _t0;

    void Awake()
    {
        _t0 = Time.realtimeSinceStartup;
        Mark("BepInEx Awake");
        StartCoroutine(Run());
    }

    void Mark(string phase)
    {
        float t = Time.realtimeSinceStartup - _t0;
        float d = _log.Count > 0 ? t - _log[_log.Count - 1].T : t;
        _log.Add(new Entry { Phase = phase, T = t, D = d });
    }

    IEnumerator Run()
    {
        while (SuperController.singleton == null) yield return null;
        Mark("SuperController ready");

        bool wasLoading = false;
        while (SuperController.singleton.isLoading) { wasLoading = true; yield return null; }
        if (wasLoading) Mark("isLoading → false  (scene ready)");

        yield return new WaitForSeconds(1f);
        Mark("+1 s post-load settle");

        Save();
    }

    void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("VaM Startup Profile  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(new string('-', 56));
        foreach (var e in _log)
            sb.AppendLine(string.Format("  {0,7:F3}s  +{1,6:F3}s  {2}", e.T, e.D, e.Phase));

        string path = Path.Combine(Paths.BepInExRootPath, "StartupProfile.txt");
        File.WriteAllText(path, sb.ToString());
        Logger.LogInfo("[StartupProfiler] Written to " + path);
    }
}
