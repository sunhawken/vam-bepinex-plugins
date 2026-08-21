using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VamPar2;

[BepInPlugin("vam.par2creator", "Par2Creator", "1.1.0")]
public class Par2CreatorPlugin : BaseUnityPlugin
{
	private ConfigEntry<int> _redundancy;

	private static readonly Regex VolRegex = new Regex("^(.+)\\.vol\\d{4}\\+\\d{2}\\.par2$", RegexOptions.IgnoreCase);

	private void Start()
	{
		_redundancy = ((BaseUnityPlugin)this).Config.Bind<int>("General", "RedundancyPercent", 5, "Recovery block redundancy percentage (1-50). Default 5 = 5%.");
		string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "AddonPackages");
		int redPct = Math.Max(1, Math.Min(50, _redundancy.Value));
		Logger.LogInfo((object)"[Par2] Starting — scanning AddonPackages on background thread");
		Thread thread = new Thread((ThreadStart)delegate
		{
			Run(root, redPct);
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void Run(string root, int redundancyPct)
	{
		ReconcileMovedFiles(root);
		Logger.LogInfo((object)"[Par2] Scanning AddonPackages...");
		string[] files = Directory.GetFiles(root, "*.var", SearchOption.AllDirectories);
		Logger.LogInfo((object)$"[Par2] Found {files.Length} .var packages  redundancy={redundancyPct}%");
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		string[] array = files;
		foreach (string text in array)
		{
			if (File.Exists(text + ".par2") && Directory.GetFiles(Path.GetDirectoryName(text), Path.GetFileName(text) + ".vol*.par2").Length != 0)
			{
				num2++;
				continue;
			}
			try
			{
				Logger.LogInfo((object)("[Par2] " + Path.GetFileName(text)));
				Par2Writer.Create(text, redundancyPct);
				num++;
			}
			catch (Exception ex)
			{
				Logger.LogError((object)("[Par2] FAILED " + Path.GetFileName(text) + ": " + ex.Message));
				num3++;
				TryDelete(text + ".par2");
				string[] files2 = Directory.GetFiles(Path.GetDirectoryName(text), Path.GetFileName(text) + ".vol*.par2");
				for (int j = 0; j < files2.Length; j++)
				{
					TryDelete(files2[j]);
				}
			}
		}
		Logger.LogInfo((object)$"[Par2] Finished — Created:{num}  Skipped:{num2}  Failed:{num3}");
	}

	private void ReconcileMovedFiles(string root)
	{
		string[] files;
		try
		{
			files = Directory.GetFiles(root, "*.par2", SearchOption.AllDirectories);
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("[Par2] Reconcile scan failed: " + ex.Message));
			return;
		}
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string[] array = files;
		foreach (string text in array)
		{
			string directoryName = Path.GetDirectoryName(text);
			string fileName = Path.GetFileName(text);
			Match match = VolRegex.Match(fileName);
			string text2;
			if (match.Success)
			{
				text2 = match.Groups[1].Value;
			}
			else
			{
				if (!fileName.EndsWith(".par2", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				text2 = fileName.Substring(0, fileName.Length - 5);
			}
			string key = directoryName + "|" + text2;
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new List<string>());
			}
			value.Add(text);
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (KeyValuePair<string, List<string>> item in dictionary)
		{
			int num4 = item.Key.IndexOf('|');
			string text3 = item.Key.Substring(0, num4);
			string text4 = item.Key.Substring(num4 + 1);
			string path = Path.Combine(text3, text4);
			string text5 = SwapVarDisabledExtension(text4);
			string text6 = ((text5 != null) ? Path.Combine(text3, text5) : null);
			if (File.Exists(path) || (text6 != null && File.Exists(text6)))
			{
				continue;
			}
			string[] array3;
			try
			{
				string[] files2 = Directory.GetFiles(root, text4, SearchOption.AllDirectories);
				string[] array2 = ((text5 != null) ? Directory.GetFiles(root, text5, SearchOption.AllDirectories) : new string[0]);
				array3 = new string[files2.Length + array2.Length];
				files2.CopyTo(array3, 0);
				array2.CopyTo(array3, files2.Length);
			}
			catch
			{
				continue;
			}
			if (array3.Length == 0)
			{
				num2++;
				continue;
			}
			if (array3.Length > 1)
			{
				Logger.LogWarning((object)("[Par2] Multiple candidates for moved file '" + text4 + "' — skipping auto-relocate (ambiguous)."));
				num3++;
				continue;
			}
			string directoryName2 = Path.GetDirectoryName(array3[0]);
			if (string.Equals(directoryName2, text3, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			bool flag = true;
			foreach (string item2 in item.Value)
			{
				string text7 = Path.Combine(directoryName2, Path.GetFileName(item2));
				try
				{
					if (File.Exists(text7))
					{
						File.Delete(text7);
					}
					File.Move(item2, text7);
				}
				catch (Exception ex2)
				{
					Logger.LogError((object)("[Par2] Failed moving " + item2 + " -> " + text7 + ": " + ex2.Message));
					flag = false;
				}
			}
			if (flag)
			{
				Logger.LogInfo((object)("[Par2] Relocated recovery set for '" + text4 + "' -> " + directoryName2));
				num++;
			}
		}
		if (num > 0 || num2 > 0 || num3 > 0)
		{
			Logger.LogInfo((object)$"[Par2] Reconcile: {num} relocated, {num2} orphaned (source deleted), {num3} ambiguous.");
		}
	}

	private static string SwapVarDisabledExtension(string name)
	{
		if (name.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
		{
			return name.Substring(0, name.Length - 4) + ".DISABLED";
		}
		if (name.EndsWith(".DISABLED", StringComparison.OrdinalIgnoreCase))
		{
			return name.Substring(0, name.Length - 9) + ".var";
		}
		return null;
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
