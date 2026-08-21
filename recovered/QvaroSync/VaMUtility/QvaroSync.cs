using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VaMUtility;

[BepInPlugin("com.vam.qvarosync", "QvaroSync", "1.0.0")]
public class QvaroSync : BaseUnityPlugin
{
	private ConfigEntry<bool> _autoSyncOnStart;

	private ConfigEntry<float> _autoSyncDelay;

	private ConfigEntry<float> _repeatInterval;

	private ConfigEntry<bool> _autoDisableNonListed;

	private ConfigEntry<string> _qvaroDataPath;

	private ConfigEntry<string> _addonPackagesPath;

	private HashSet<string> _keepEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private HashSet<string> _keepCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private bool _syncing;

	private void Awake()
	{
		_qvaroDataPath = ((BaseUnityPlugin)this).Config.Bind<string>("Paths", "QvaroDataPath", Path.Combine(Directory.GetCurrentDirectory(), "Qvaro_Data"), "Path to the Qvaro_Data folder (absolute or relative to VaM root).");
		_addonPackagesPath = ((BaseUnityPlugin)this).Config.Bind<string>("Paths", "AddonPackagesPath", Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages"), "Path to AddonPackages folder.");
		_autoSyncOnStart = ((BaseUnityPlugin)this).Config.Bind<bool>("Sync", "AutoSyncOnStart", false, "Apply keepEnabled list automatically after VaM starts.");
		_autoSyncDelay = ((BaseUnityPlugin)this).Config.Bind<float>("Sync", "AutoSyncDelaySeconds", 5f, "Seconds to wait after startup before syncing.");
		_repeatInterval = ((BaseUnityPlugin)this).Config.Bind<float>("Sync", "RepeatIntervalSeconds", 0f, "How often (in seconds) to re-sync after the initial startup sync. 0 = disabled.");
		_autoDisableNonListed = ((BaseUnityPlugin)this).Config.Bind<bool>("Sync", "AutoDisableNonListed", false, "If true, packages NOT in Qvaro's keepEnabled list are renamed to .DISABLED. WARNING: irreversible without Qvaro. Leave false unless you know what you're doing.");
		Logger.LogInfo((object)("[QvaroSync] Loaded. DataPath=" + _qvaroDataPath.Value));
		if (_autoSyncOnStart.Value)
		{
			((MonoBehaviour)this).StartCoroutine(SyncAfterDelay(_autoSyncDelay.Value));
		}
		if (_repeatInterval.Value > 0f)
		{
			((MonoBehaviour)this).StartCoroutine(RepeatSync());
		}
	}

	private IEnumerator RepeatSync()
	{
		yield return (object)new WaitForSeconds(_autoSyncDelay.Value + 2f);
		while (true)
		{
			float value = _repeatInterval.Value;
			if (value <= 0f)
			{
				break;
			}
			yield return (object)new WaitForSeconds(value);
			if (!_syncing)
			{
				yield return RunSync();
			}
		}
	}

	private IEnumerator SyncAfterDelay(float delay)
	{
		yield return (object)new WaitForSeconds(delay);
		yield return RunSync();
	}

	private IEnumerator RunSync()
	{
		if (_syncing)
		{
			yield break;
		}
		_syncing = true;
		try
		{
			Logger.LogInfo((object)"[QvaroSync] Starting sync...");
			LoadQvaroSettings();
			if (_keepEnabled.Count == 0)
			{
				Logger.LogWarning((object)"[QvaroSync] keepEnabled list is empty — nothing to do.");
				Logger.LogWarning((object)"[QvaroSync] No keepEnabled packages found in Qvaro settings.");
				yield break;
			}
			Logger.LogInfo((object)$"[QvaroSync] keepEnabled count: {_keepEnabled.Count}, categories: {_keepCategories.Count}");
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			string text = ResolvePath(_addonPackagesPath.Value);
			if (!Directory.Exists(text))
			{
				Logger.LogError((object)("[QvaroSync] AddonPackages folder not found: " + text));
				yield break;
			}
			foreach (string item in SafeGetFiles(text, "*.DISABLED"))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item);
				if (ShouldKeepEnabled(fileNameWithoutExtension))
				{
					string text2 = Path.ChangeExtension(item, null) + ".var";
					try
					{
						File.Move(item, text2);
						num++;
						Logger.LogInfo((object)("[QvaroSync] Enabled: " + Path.GetFileName(text2)));
					}
					catch (Exception ex)
					{
						Logger.LogWarning((object)("[QvaroSync] Could not enable " + Path.GetFileName(item) + ": " + ex.Message));
					}
				}
			}
			if (_autoDisableNonListed.Value)
			{
				foreach (string item2 in SafeGetFiles(text, "*.var"))
				{
					string fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(item2);
					if (!ShouldKeepEnabled(fileNameWithoutExtension2))
					{
						_ = item2 + ".DISABLED".Replace(".var.DISABLED", ".DISABLED");
						string destFileName = Path.ChangeExtension(item2, null) + ".DISABLED";
						try
						{
							File.Move(item2, destFileName);
							num2++;
							Logger.LogInfo((object)("[QvaroSync] Disabled: " + Path.GetFileName(item2)));
						}
						catch (Exception ex2)
						{
							Logger.LogWarning((object)("[QvaroSync] Could not disable " + Path.GetFileName(item2) + ": " + ex2.Message));
						}
					}
					else
					{
						num3++;
					}
				}
			}
			string text3 = $"[QvaroSync] Done. Enabled: {num}";
			if (_autoDisableNonListed.Value)
			{
				text3 += $", Disabled: {num2}, Kept: {num3}";
			}
			Logger.LogInfo((object)text3);
			Logger.LogMessage((object)text3);
		}
		finally
		{
			_syncing = false;
		}
	}

	private void LoadQvaroSettings()
	{
		_keepEnabled.Clear();
		_keepCategories.Clear();
		string text = Path.Combine(ResolvePath(_qvaroDataPath.Value), "settings.json");
		if (!File.Exists(text))
		{
			Logger.LogWarning((object)("[QvaroSync] settings.json not found at: " + text));
			return;
		}
		try
		{
			string json = File.ReadAllText(text, Encoding.UTF8);
			string text2 = ExtractJsonStringField(json, "keepEnabled");
			if (!string.IsNullOrEmpty(text2))
			{
				string[] array = text2.Split('\n');
				for (int i = 0; i < array.Length; i++)
				{
					string text3 = array[i].Trim();
					if (!string.IsNullOrEmpty(text3))
					{
						_keepEnabled.Add(text3);
					}
				}
			}
			string text4 = ExtractJsonStringField(json, "keepEnabledCategories");
			if (!string.IsNullOrEmpty(text4))
			{
				string[] array = text4.Split(',', '\n', ';');
				for (int i = 0; i < array.Length; i++)
				{
					string text5 = array[i].Trim();
					if (!string.IsNullOrEmpty(text5))
					{
						_keepCategories.Add(text5);
					}
				}
			}
			Logger.LogInfo((object)$"[QvaroSync] Loaded {_keepEnabled.Count} keepEnabled UIDs, {_keepCategories.Count} categories from Qvaro settings.");
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("[QvaroSync] Failed to read settings.json: " + ex.Message));
		}
	}

	public List<string> QueryCatalogPackageUids()
	{
		List<string> result = new List<string>();
		string path = Path.Combine(ResolvePath(_qvaroDataPath.Value), "catalog.sqlite");
		if (!File.Exists(path))
		{
			Logger.LogWarning((object)"[QvaroSync] catalog.sqlite not found.");
			return result;
		}
		try
		{
			byte[] array = new byte[100];
			using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				fileStream.Read(array, 0, 100);
			}
			if (!Encoding.ASCII.GetString(array, 0, 16).StartsWith("SQLite format 3"))
			{
				Logger.LogWarning((object)"[QvaroSync] catalog.sqlite is not a valid SQLite database.");
				return result;
			}
			Logger.LogInfo((object)"[QvaroSync] catalog.sqlite found and valid. Use QueryCatalogPackageUids() for full integration — requires System.Data.SQLite driver.");
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("[QvaroSync] Could not read catalog.sqlite: " + ex.Message));
		}
		return result;
	}

	private bool ShouldKeepEnabled(string uid)
	{
		if (string.IsNullOrEmpty(uid))
		{
			return false;
		}
		if (_keepEnabled.Contains(uid))
		{
			return true;
		}
		if (_keepCategories.Count > 0)
		{
			int num = uid.IndexOf('.');
			if (num > 0)
			{
				string text = uid.Substring(0, num);
				if (_keepCategories.Contains(text))
				{
					return true;
				}
				foreach (string keepCategory in _keepCategories)
				{
					if (text.StartsWith(keepCategory, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static string ResolvePath(string path)
	{
		if (Path.IsPathRooted(path))
		{
			return path;
		}
		return Path.Combine(Directory.GetCurrentDirectory(), path);
	}

	private static List<string> SafeGetFiles(string dir, string pattern)
	{
		List<string> list = new List<string>();
		try
		{
			string[] files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
			list.AddRange(files);
			string[] directories = Directory.GetDirectories(dir);
			foreach (string path in directories)
			{
				try
				{
					list.AddRange(Directory.GetFiles(path, pattern, SearchOption.AllDirectories));
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static string ExtractJsonStringField(string json, string fieldName)
	{
		string text = "\"" + fieldName + "\"";
		int num = json.IndexOf(text, StringComparison.Ordinal);
		if (num < 0)
		{
			return null;
		}
		int num2 = json.IndexOf(':', num + text.Length);
		if (num2 < 0)
		{
			return null;
		}
		int i;
		for (i = num2 + 1; i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n'); i++)
		{
		}
		if (i >= json.Length || json[i] != '"')
		{
			return null;
		}
		i++;
		StringBuilder stringBuilder = new StringBuilder();
		for (int j = i; j < json.Length; j++)
		{
			char c = json[j];
			if (c == '\\' && j + 1 < json.Length)
			{
				switch (json[j + 1])
				{
				case 'n':
					stringBuilder.Append('\n');
					j++;
					break;
				case '\\':
					stringBuilder.Append('\\');
					j++;
					break;
				case '"':
					stringBuilder.Append('"');
					j++;
					break;
				default:
					stringBuilder.Append(c);
					break;
				}
			}
			else
			{
				if (c == '"')
				{
					break;
				}
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}
}
