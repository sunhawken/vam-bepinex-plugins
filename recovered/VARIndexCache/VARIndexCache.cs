using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using BepInEx;

[BepInPlugin("com.vam.varindexcache", "VaM VAR Index Cache", "1.0.0")]
public class VARIndexCache : BaseUnityPlugin
{
	private struct Entry
	{
		public long Size;

		public long ModTicks;

		public int EntryCount;
	}

	private const int VERSION = 2;

	private void Awake()
	{
		string path = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
		if (!Directory.Exists(path))
		{
			return;
		}
		string cacheDir = Path.Combine(Paths.BepInExRootPath, "cache");
		string cachePath = Path.Combine(cacheDir, "var_index.cache");
		string[] files = Directory.GetFiles(path, "*.var", SearchOption.AllDirectories);
		if (files.Length == 0)
		{
			return;
		}
		Dictionary<string, Entry> cache = ReadCache(cachePath);
		List<string> stale = new List<string>();
		int num = 0;
		string[] array = files;
		foreach (string text in array)
		{
			FileInfo fileInfo = new FileInfo(text);
			string key = text.ToLowerInvariant();
			if (cache.TryGetValue(key, out var value) && value.Size == fileInfo.Length && value.ModTicks == fileInfo.LastWriteTimeUtc.Ticks)
			{
				num++;
			}
			else
			{
				stale.Add(text);
			}
		}
		Logger.LogInfo((object)$"[VARIndexCache] {num} cached / {stale.Count} stale / {files.Length} total");
		if (stale.Count == 0)
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] array2 = files;
			foreach (string text2 in array2)
			{
				hashSet.Add(text2.ToLowerInvariant());
			}
			foreach (string item in new List<string>(cache.Keys))
			{
				if (!hashSet.Contains(item))
				{
					cache.Remove(item);
				}
			}
			foreach (string item2 in stale)
			{
				try
				{
					FileInfo fileInfo2 = new FileInfo(item2);
					int entryCount = 0;
					using (ZipArchive zipArchive = ZipFile.OpenRead(item2))
					{
						entryCount = zipArchive.Entries.Count;
					}
					cache[item2.ToLowerInvariant()] = new Entry
					{
						Size = fileInfo2.Length,
						ModTicks = fileInfo2.LastWriteTimeUtc.Ticks,
						EntryCount = entryCount
					};
				}
				catch
				{
				}
			}
			try
			{
				Directory.CreateDirectory(cacheDir);
				WriteCache(cachePath, cache);
			}
			catch
			{
			}
		});
	}

	private Dictionary<string, Entry> ReadCache(string path)
	{
		Dictionary<string, Entry> dictionary = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(path))
		{
			return dictionary;
		}
		try
		{
			using StreamReader streamReader = new StreamReader(path, Encoding.UTF8);
			if (streamReader.ReadLine() != 2.ToString())
			{
				return dictionary;
			}
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				string[] array = text.Split('|');
				if (array.Length >= 4 && long.TryParse(array[1], out var result) && long.TryParse(array[2], out var result2) && int.TryParse(array[3], out var result3))
				{
					dictionary[array[0]] = new Entry
					{
						Size = result,
						ModTicks = result2,
						EntryCount = result3
					};
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private void WriteCache(string path, Dictionary<string, Entry> cache)
	{
		using StreamWriter streamWriter = new StreamWriter(path, append: false, Encoding.UTF8);
		streamWriter.WriteLine(2);
		foreach (KeyValuePair<string, Entry> item in cache)
		{
			streamWriter.WriteLine($"{item.Key}|{item.Value.Size}|{item.Value.ModTicks}|{item.Value.EntryCount}");
		}
	}
}
