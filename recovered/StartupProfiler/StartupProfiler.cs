using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

[BepInPlugin("com.vam.startupprofiler", "VaM Startup Profiler", "1.0.0")]
public class StartupProfiler : BaseUnityPlugin
{
	private struct Entry
	{
		public string Phase;

		public float T;

		public float D;
	}

	private readonly List<Entry> _log = new List<Entry>();

	private float _t0;

	private void Awake()
	{
		_t0 = Time.realtimeSinceStartup;
		Mark("BepInEx Awake");
		((MonoBehaviour)this).StartCoroutine(Run());
	}

	private void Mark(string phase)
	{
		float num = Time.realtimeSinceStartup - _t0;
		float d = ((_log.Count > 0) ? (num - _log[_log.Count - 1].T) : num);
		_log.Add(new Entry
		{
			Phase = phase,
			T = num,
			D = d
		});
	}

	private IEnumerator Run()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		Mark("SuperController ready");
		bool wasLoading = false;
		while (SuperController.singleton.isLoading)
		{
			wasLoading = true;
			yield return null;
		}
		if (wasLoading)
		{
			Mark("isLoading → false  (scene ready)");
		}
		yield return (object)new WaitForSeconds(1f);
		Mark("+1 s post-load settle");
		Save();
	}

	private void Save()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("VaM Startup Profile  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine(new string('-', 56));
		foreach (Entry item in _log)
		{
			stringBuilder.AppendLine($"  {item.T,7:F3}s  +{item.D,6:F3}s  {item.Phase}");
		}
		string text = Path.Combine(Paths.BepInExRootPath, "StartupProfile.txt");
		File.WriteAllText(text, stringBuilder.ToString());
		Logger.LogInfo((object)("[StartupProfiler] Written to " + text));
	}
}
