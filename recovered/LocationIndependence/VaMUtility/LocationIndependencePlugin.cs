using System;
using System.Collections;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VaMUtility;

[BepInPlugin("com.vam.locationindependence", "LocationIndependence", "1.0.0")]
public class LocationIndependencePlugin : BaseUnityPlugin
{
	private ConfigEntry<bool> cfgEnabled;

	private ConfigEntry<bool> cfgFixConfigFiles;

	private ConfigEntry<bool> cfgFixSceneFiles;

	private ConfigEntry<bool> cfgBackup;

	private ConfigEntry<bool> cfgDryRun;

	private static readonly string[] SceneScanExtensions = new string[3] { "*.json", "*.vap", "*.vam" };

	private string CachePath => Path.Combine(Path.Combine(Paths.BepInExRootPath, "cache"), "lastroot.txt");

	private void Awake()
	{
		cfgEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Enabled", true, "Detect drive/folder moves and rewrite stale absolute paths.");
		cfgFixConfigFiles = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "FixConfigFiles", true, "Rewrite stale absolute paths inside BepInEx/config/*.cfg files.");
		cfgFixSceneFiles = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "FixSceneFiles", true, "Rewrite stale absolute paths inside Saves/**/*.json, *.vap, *.vam files.");
		cfgBackup = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "BackupBeforeFix", true, "Create a .bak copy of each file before modifying it.");
		cfgDryRun = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "DryRun", false, "Log what would change without writing any files.");
		((MonoBehaviour)this).StartCoroutine(StartupCoroutine());
		Logger.LogInfo((object)"LocationIndependence BepInEx plugin loaded.");
	}

	private IEnumerator StartupCoroutine()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		yield return null;
		if (cfgEnabled.Value)
		{
			CheckForDrift();
		}
	}

	private void CheckForDrift()
	{
		string currentRoot = Directory.GetCurrentDirectory().TrimEnd('\\', '/');
		string cachePath = CachePath;
		string lastRoot = null;
		try
		{
			if (File.Exists(cachePath))
			{
				lastRoot = File.ReadAllText(cachePath).Trim();
			}
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("LocationIndependence: failed reading cache: " + ex));
		}
		if (string.IsNullOrEmpty(lastRoot))
		{
			Logger.LogInfo((object)"LocationIndependence: no previous root recorded — storing current root, nothing to fix.");
			WriteCurrentRoot(currentRoot, cachePath);
		}
		else
		{
			if (string.Equals(lastRoot, currentRoot, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			Logger.LogInfo((object)("LocationIndependence: install moved from '" + lastRoot + "' to '" + currentRoot + "'. Scanning for stale paths..."));
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					int num = (cfgFixConfigFiles.Value ? FixConfigFiles(lastRoot, currentRoot) : 0);
					int num2 = (cfgFixSceneFiles.Value ? FixSceneFiles(lastRoot, currentRoot) : 0);
					Logger.LogInfo((object)$"LocationIndependence: done. {num} config file(s), {num2} scene/preset file(s) updated.");
				}
				catch (Exception ex2)
				{
					Logger.LogError((object)("LocationIndependence: scan failed: " + ex2));
				}
				if (!cfgDryRun.Value)
				{
					WriteCurrentRoot(currentRoot, cachePath);
				}
			});
		}
	}

	private void WriteCurrentRoot(string root, string cachePath)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
			File.WriteAllText(cachePath, root);
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("LocationIndependence: failed writing cache: " + ex));
		}
	}

	private int FixConfigFiles(string oldRoot, string newRoot)
	{
		string path = Path.Combine(Paths.BepInExRootPath, "config");
		if (!Directory.Exists(path))
		{
			return 0;
		}
		int num = 0;
		string[] files = Directory.GetFiles(path, "*.cfg", SearchOption.TopDirectoryOnly);
		foreach (string path2 in files)
		{
			if (TryRewriteFile(path2, oldRoot, newRoot))
			{
				num++;
			}
		}
		return num;
	}

	private int FixSceneFiles(string oldRoot, string newRoot)
	{
		string path = Path.Combine(newRoot, "Saves");
		if (!Directory.Exists(path))
		{
			return 0;
		}
		int num = 0;
		string[] sceneScanExtensions = SceneScanExtensions;
		foreach (string searchPattern in sceneScanExtensions)
		{
			string[] files = Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories);
			foreach (string path2 in files)
			{
				if (TryRewriteFile(path2, oldRoot, newRoot))
				{
					num++;
				}
			}
		}
		return num;
	}

	private bool TryRewriteFile(string path, string oldRoot, string newRoot)
	{
		try
		{
			string text = File.ReadAllText(path);
			string oldValue = oldRoot.Replace("\\", "\\\\");
			string newValue = newRoot.Replace("\\", "\\\\");
			string oldValue2 = oldRoot.Replace("\\", "/");
			string newValue2 = newRoot.Replace("\\", "/");
			string text2 = text.Replace(oldValue, newValue).Replace(oldRoot, newRoot).Replace(oldValue2, newValue2);
			if (text2 == text)
			{
				return false;
			}
			if (cfgDryRun.Value)
			{
				Logger.LogInfo((object)("LocationIndependence: [dry run] would update " + path));
				return true;
			}
			if (cfgBackup.Value)
			{
				string text3 = path + ".bak";
				if (!File.Exists(text3))
				{
					File.Copy(path, text3);
				}
			}
			File.WriteAllText(path, text2);
			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("LocationIndependence: failed rewriting " + path + ": " + ex));
			return false;
		}
	}
}
