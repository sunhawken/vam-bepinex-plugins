using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx;
using UnityEngine;

[BepInPlugin("com.vam.contentlistfixer", "VaM ContentList Fixer", "1.0.0")]
public class ContentListFixer : BaseUnityPlugin
{
	private void Awake()
	{
		((MonoBehaviour)this).StartCoroutine(RunAfterLoad());
	}

	private IEnumerator RunAfterLoad()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		while (SuperController.singleton.isLoading)
		{
			yield return null;
		}
		yield return null;
		string path = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
		if (!Directory.Exists(path))
		{
			yield break;
		}
		string cacheDir = Path.Combine(Paths.BepInExRootPath, "cache");
		string flagPath = Path.Combine(cacheDir, "contentlistfixer.txt");
		DateTime lastRun = DateTime.MinValue;
		if (File.Exists(flagPath) && long.TryParse(File.ReadAllText(flagPath).Trim(), out var result))
		{
			lastRun = new DateTime(result, DateTimeKind.Utc);
		}
		string[] files = Directory.GetFiles(path, "*.var", SearchOption.AllDirectories);
		int scanned = 0;
		int rebuilt = 0;
		int errors = 0;
		string[] array = files;
		foreach (string text in array)
		{
			if (new FileInfo(text).LastWriteTimeUtc <= lastRun)
			{
				continue;
			}
			scanned++;
			try
			{
				if (ProcessVAR(text))
				{
					rebuilt++;
				}
			}
			catch (Exception ex)
			{
				errors++;
				Logger.LogError((object)("[ContentListFixer] " + Path.GetFileName(text) + ": " + ex.Message));
			}
			if (scanned % 20 == 0)
			{
				yield return null;
			}
		}
		if (scanned > 0)
		{
			Logger.LogInfo((object)$"[ContentListFixer] Scanned {scanned}, rebuilt {rebuilt}, errors {errors}.");
		}
		else
		{
			Logger.LogInfo((object)"[ContentListFixer] Nothing new to fix.");
		}
		Directory.CreateDirectory(cacheDir);
		File.WriteAllText(flagPath, DateTime.UtcNow.Ticks.ToString());
	}

	private bool ProcessVAR(string varPath)
	{
		List<string> list = new List<string>();
		string json;
		using (ZipArchive zipArchive = ZipFile.OpenRead(varPath))
		{
			ZipArchiveEntry entry = zipArchive.GetEntry("meta.json");
			if (entry == null)
			{
				return false;
			}
			using (StreamReader streamReader = new StreamReader(entry.Open(), Encoding.UTF8))
			{
				json = streamReader.ReadToEnd();
			}
			foreach (ZipArchiveEntry entry2 in zipArchive.Entries)
			{
				string text = entry2.FullName.Replace('\\', '/');
				if (!text.Equals("meta.json", StringComparison.OrdinalIgnoreCase) && !text.EndsWith("/"))
				{
					list.Add(text);
				}
			}
		}
		list.Sort(StringComparer.OrdinalIgnoreCase);
		List<string> list2 = ParseContentList(json);
		list2.Sort(StringComparer.OrdinalIgnoreCase);
		if (ListsEqual(list, list2))
		{
			return false;
		}
		string value = RebuildContentList(json, list);
		using (ZipArchive zipArchive2 = ZipFile.Open(varPath, ZipArchiveMode.Update))
		{
			zipArchive2.GetEntry("meta.json")?.Delete();
			using StreamWriter streamWriter = new StreamWriter(zipArchive2.CreateEntry("meta.json").Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			streamWriter.Write(value);
		}
		Logger.LogInfo((object)("[ContentListFixer] Rebuilt " + Path.GetFileName(varPath) + " (" + list.Count + " entries)"));
		return true;
	}

	private string RebuildContentList(string json, List<string> entries)
	{
		int num = json.IndexOf("\"contentList\"", StringComparison.Ordinal);
		if (num < 0)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine(",");
			stringBuilder.AppendLine("   \"contentList\" : [");
			for (int i = 0; i < entries.Count; i++)
			{
				stringBuilder.Append("      \"").Append(entries[i].Replace("/", "\\\\")).Append('"');
				if (i < entries.Count - 1)
				{
					stringBuilder.Append(',');
				}
				stringBuilder.AppendLine();
			}
			stringBuilder.Append("   ]");
			int num2 = json.LastIndexOf('}');
			if (num2 < 0)
			{
				return json;
			}
			return json.Substring(0, num2) + stringBuilder?.ToString() + "\n}";
		}
		int num3 = json.IndexOf('[', num);
		if (num3 < 0)
		{
			return json;
		}
		int num4 = FindMatchingBracket(json, num3);
		if (num4 < 0)
		{
			return json;
		}
		StringBuilder stringBuilder2 = new StringBuilder();
		stringBuilder2.AppendLine("[");
		for (int j = 0; j < entries.Count; j++)
		{
			stringBuilder2.Append("      \"").Append(entries[j].Replace("/", "\\\\")).Append('"');
			if (j < entries.Count - 1)
			{
				stringBuilder2.Append(',');
			}
			stringBuilder2.AppendLine();
		}
		stringBuilder2.Append("   ]");
		return json.Substring(0, num3) + stringBuilder2?.ToString() + json.Substring(num4 + 1);
	}

	private List<string> ParseContentList(string json)
	{
		List<string> list = new List<string>();
		int num = json.IndexOf("\"contentList\"", StringComparison.Ordinal);
		if (num < 0)
		{
			return list;
		}
		int num2 = json.IndexOf('[', num);
		if (num2 < 0)
		{
			return list;
		}
		int num3 = FindMatchingBracket(json, num2);
		if (num3 < 0)
		{
			return list;
		}
		string text = json.Substring(num2 + 1, num3 - num2 - 1);
		int num4 = 0;
		while (num4 < text.Length)
		{
			int num5 = text.IndexOf('"', num4);
			if (num5 < 0)
			{
				break;
			}
			int num6 = text.IndexOf('"', num5 + 1);
			if (num6 < 0)
			{
				break;
			}
			string text2 = text.Substring(num5 + 1, num6 - num5 - 1).Replace("\\\\", "/").Replace("\\", "/");
			if (text2.Length > 0)
			{
				list.Add(text2);
			}
			num4 = num6 + 1;
		}
		return list;
	}

	private int FindMatchingBracket(string json, int openIdx)
	{
		int num = 0;
		for (int i = openIdx; i < json.Length; i++)
		{
			if (json[i] == '[')
			{
				num++;
			}
			else if (json[i] == ']' && --num == 0)
			{
				return i;
			}
		}
		return -1;
	}

	private bool ListsEqual(List<string> a, List<string> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}
}
