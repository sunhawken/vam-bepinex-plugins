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
	private ConfigEntry<string> cfgCacheFolder;

	private ConfigEntry<int> cfgMaxSizeGB;

	private ConfigEntry<int> cfgMaxAgeDays;

	private ConfigEntry<bool> cfgAutoCleanOnStart;

	private ConfigEntry<bool> cfgAutoCleanOnSceneLoad;

	private ConfigEntry<bool> cfgUnloadUnusedOnScene;

	private ConfigEntry<long> cfgUnityCacheMaxMB;

	private ConfigEntry<int> cfgUnityCacheExpireDays;

	private ConfigEntry<KeyCode> cfgHotkeyClean;

	private ConfigEntry<KeyCode> cfgHotkeyUnload;

	private ConfigEntry<KeyCode> cfgHotkeyStats;

	private static ManualLogSource Log;

	private string _resolvedCacheFolder;

	private void Awake()
	{
		Log = Logger;
		string fullPath = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
		string text = Path.Combine(Path.GetPathRoot(fullPath).TrimEnd('\\'), "Cache");
		cfgCacheFolder = ((BaseUnityPlugin)this).Config.Bind<string>("Cache", "CacheFolder", text, "Path to VaM cache folder. Leave blank to auto-detect from prefs.json.");
		cfgMaxSizeGB = ((BaseUnityPlugin)this).Config.Bind<int>("Cache", "MaxSizeGB", 20, "Auto-evict oldest files when cache exceeds this size in GB. 0 = disabled.");
		cfgMaxAgeDays = ((BaseUnityPlugin)this).Config.Bind<int>("Cache", "MaxAgeDays", 30, "Delete cache files not accessed in this many days. 0 = disabled.");
		cfgAutoCleanOnStart = ((BaseUnityPlugin)this).Config.Bind<bool>("Cache", "AutoCleanOnStart", true, "Run cache cleanup when VaM starts.");
		cfgAutoCleanOnSceneLoad = ((BaseUnityPlugin)this).Config.Bind<bool>("Cache", "AutoCleanOnSceneLoad", false, "Run cache cleanup on every scene load (heavier, but keeps cache tidy).");
		cfgUnloadUnusedOnScene = ((BaseUnityPlugin)this).Config.Bind<bool>("Cache", "UnloadUnusedAssetsOnSceneLoad", true, "Call Resources.UnloadUnusedAssets() after each scene finishes loading.");
		cfgUnityCacheMaxMB = ((BaseUnityPlugin)this).Config.Bind<long>("Unity Cache", "MaxDiskSpaceMB", 8192L, "Maximum MB Unity may use for its asset-bundle cache (default ~unlimited).");
		cfgUnityCacheExpireDays = ((BaseUnityPlugin)this).Config.Bind<int>("Unity Cache", "ExpirationDays", 30, "Days before Unity considers a cached asset bundle stale.");
		cfgHotkeyClean = ((BaseUnityPlugin)this).Config.Bind<KeyCode>("Hotkeys", "CleanCache", (KeyCode)289, "Manually trigger cache cleanup.");
		cfgHotkeyUnload = ((BaseUnityPlugin)this).Config.Bind<KeyCode>("Hotkeys", "UnloadAssets", (KeyCode)290, "Manually call UnloadUnusedAssets.");
		cfgHotkeyStats = ((BaseUnityPlugin)this).Config.Bind<KeyCode>("Hotkeys", "PrintStats", (KeyCode)291, "Print cache stats to BepInEx log.");
		_resolvedCacheFolder = ResolveCacheFolder(fullPath);
		ApplyUnityCache();
		if (cfgAutoCleanOnStart.Value)
		{
			((MonoBehaviour)this).StartCoroutine(CleanCacheCoroutine("startup"));
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
		Log.LogInfo((object)("CacheHelper v1.0.0 active  |  cache=" + _resolvedCacheFolder));
		LogCacheStats();
	}

	private string ResolveCacheFolder(string vamRoot)
	{
		string text = cfgCacheFolder.Value.Trim();
		if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
		{
			return text;
		}
		string path = Path.Combine(vamRoot, "prefs.json");
		if (File.Exists(path))
		{
			string text2 = File.ReadAllText(path);
			string text3 = "\"cacheFolder\"";
			int num = text2.IndexOf(text3);
			if (num >= 0)
			{
				int num2 = text2.IndexOf('"', num + text3.Length + 1);
				int num3 = text2.IndexOf('"', num2 + 1);
				if (num2 >= 0 && num3 > num2)
				{
					string text4 = text2.Substring(num2 + 1, num3 - num2 - 1).Replace("\\\\", "\\");
					if (Directory.Exists(text4))
					{
						return text4;
					}
				}
			}
		}
		return Path.Combine(Path.GetPathRoot(vamRoot).TrimEnd('\\'), "Cache");
	}

	private void ApplyUnityCache()
	{
		if (cfgUnityCacheMaxMB.Value > 0)
		{
			Caching.maximumAvailableDiskSpace = cfgUnityCacheMaxMB.Value * 1024 * 1024;
		}
		if (cfgUnityCacheExpireDays.Value > 0)
		{
			Caching.expirationDelay = cfgUnityCacheExpireDays.Value * 86400;
		}
		Log.LogDebug((object)("Unity cache: max=" + cfgUnityCacheMaxMB.Value + "MB  expire=" + cfgUnityCacheExpireDays.Value + "d  ready=" + Caching.ready));
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (cfgUnloadUnusedOnScene.Value)
		{
			((MonoBehaviour)this).StartCoroutine(UnloadUnusedCoroutine());
		}
		if (cfgAutoCleanOnSceneLoad.Value)
		{
			((MonoBehaviour)this).StartCoroutine(CleanCacheCoroutine("scene-load"));
		}
	}

	private IEnumerator UnloadUnusedCoroutine()
	{
		yield return (object)new WaitForSeconds(2f);
		yield return Resources.UnloadUnusedAssets();
		Log.LogDebug((object)"UnloadUnusedAssets complete");
	}

	private void Update()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (Input.GetKeyDown(cfgHotkeyClean.Value))
		{
			((MonoBehaviour)this).StartCoroutine(CleanCacheCoroutine("hotkey"));
		}
		if (Input.GetKeyDown(cfgHotkeyUnload.Value))
		{
			((MonoBehaviour)this).StartCoroutine(UnloadUnusedCoroutine());
		}
		if (Input.GetKeyDown(cfgHotkeyStats.Value))
		{
			LogCacheStats();
		}
	}

	private IEnumerator CleanCacheCoroutine(string reason)
	{
		yield return null;
		Log.LogInfo((object)("[CacheHelper] Starting cleanup  (" + reason + ")  folder=" + _resolvedCacheFolder));
		if (!Directory.Exists(_resolvedCacheFolder))
		{
			Log.LogWarning((object)("[CacheHelper] Cache folder not found: " + _resolvedCacheFolder));
			yield break;
		}
		long removedBytes = 0L;
		int removedCount = 0;
		DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(cfgMaxAgeDays.Value, 0));
		if (cfgMaxAgeDays.Value > 0)
		{
			FileInfo[] array = (from f in Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories)
				select new FileInfo(f) into f
				where f.LastAccessTimeUtc < cutoff
				select f).ToArray();
			FileInfo[] array2 = array;
			foreach (FileInfo fileInfo in array2)
			{
				try
				{
					long length = fileInfo.Length;
					fileInfo.Delete();
					removedBytes += length;
					removedCount++;
				}
				catch
				{
				}
			}
			Log.LogInfo((object)("[CacheHelper] Age pass: removed " + removedCount + " files  (" + FormatBytes(removedBytes) + ")"));
		}
		yield return null;
		if (cfgMaxSizeGB.Value > 0)
		{
			long num2 = (long)cfgMaxSizeGB.Value * 1024L * 1024 * 1024;
			long num3 = GetDirectorySize(_resolvedCacheFolder);
			if (num3 > num2)
			{
				Log.LogInfo((object)("[CacheHelper] Cache " + FormatBytes(num3) + " > limit " + cfgMaxSizeGB.Value + "GB -- evicting oldest..."));
				FileInfo[] array3 = (from f in Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories)
					select new FileInfo(f) into f
					orderby f.LastAccessTimeUtc
					select f).ToArray();
				FileInfo[] array4 = array3;
				foreach (FileInfo fileInfo2 in array4)
				{
					if (num3 <= num2)
					{
						break;
					}
					try
					{
						long length2 = fileInfo2.Length;
						fileInfo2.Delete();
						num3 -= length2;
						removedBytes += length2;
						removedCount++;
					}
					catch
					{
					}
				}
				Log.LogInfo((object)("[CacheHelper] Size pass: removed " + removedCount + " files total  (" + FormatBytes(removedBytes) + ")  remaining=" + FormatBytes(num3)));
			}
			else
			{
				Log.LogInfo((object)("[CacheHelper] Cache size " + FormatBytes(num3) + " within limit -- no size eviction."));
			}
		}
		RemoveEmptyDirectories(_resolvedCacheFolder);
		Log.LogInfo((object)"[CacheHelper] Cleanup done.");
	}

	private void LogCacheStats()
	{
		if (!Directory.Exists(_resolvedCacheFolder))
		{
			Log.LogInfo((object)("[CacheHelper] Cache folder not found: " + _resolvedCacheFolder));
			return;
		}
		string[] files = Directory.GetFiles(_resolvedCacheFolder, "*", SearchOption.AllDirectories);
		long bytes = files.Select((string f) => new FileInfo(f)).Sum((FileInfo f) => f.Length);
		Log.LogInfo((object)"[CacheHelper] ===== CACHE STATS =====");
		Log.LogInfo((object)("  Folder : " + _resolvedCacheFolder));
		Log.LogInfo((object)("  Files  : " + files.Length));
		Log.LogInfo((object)("  Size   : " + FormatBytes(bytes)));
		Log.LogInfo((object)("  Limit  : " + ((cfgMaxSizeGB.Value > 0) ? (cfgMaxSizeGB.Value + " GB") : "none")));
		Log.LogInfo((object)("  MaxAge : " + ((cfgMaxAgeDays.Value > 0) ? (cfgMaxAgeDays.Value + " days") : "none")));
		Log.LogInfo((object)("  Unity  : ready=" + Caching.ready + "  spaceOccupied=" + FormatBytes(Caching.spaceOccupied)));
		Log.LogInfo((object)"[CacheHelper] =======================");
	}

	private static long GetDirectorySize(string path)
	{
		return (from f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)
			select new FileInfo(f).Length).Sum();
	}

	private static void RemoveEmptyDirectories(string root)
	{
		foreach (string item in from d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
			orderby d.Length descending
			select d)
		{
			try
			{
				if (Directory.GetFiles(item).Length == 0 && Directory.GetDirectories(item).Length == 0)
				{
					Directory.Delete(item);
				}
			}
			catch
			{
			}
		}
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes >= 1073741824)
		{
			return Math.Round((double)bytes / 1073741824.0, 2) + " GB";
		}
		if (bytes >= 1048576)
		{
			return Math.Round((double)bytes / 1048576.0, 1) + " MB";
		}
		if (bytes >= 1024)
		{
			return Math.Round((double)bytes / 1024.0, 1) + " KB";
		}
		return bytes + " B";
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}
}
