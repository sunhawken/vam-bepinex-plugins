using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;

[BepInPlugin("com.vam.parallelvarscan", "VaM Parallel VAR Scan", "1.0.0")]
public class ParallelVARScan : BaseUnityPlugin
{
	private void Awake()
	{
		ConfigEntry<int> val = ((BaseUnityPlugin)this).Config.Bind<int>("General", "ThreadCount", 0, "Parallel scan threads (0 = CPU core count)");
		string path = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
		if (!Directory.Exists(path))
		{
			return;
		}
		string[] files = Directory.GetFiles(path, "*.var", SearchOption.AllDirectories);
		if (files.Length == 0)
		{
			return;
		}
		int val2 = ((val.Value > 0) ? val.Value : Environment.ProcessorCount);
		val2 = Math.Min(val2, files.Length);
		Logger.LogInfo((object)$"[ParallelVARScan] Pre-warming {files.Length} packages on {val2} threads.");
		int num = (files.Length + val2 - 1) / val2;
		for (int i = 0; i < val2; i++)
		{
			int num2 = i * num;
			int num3 = Math.Min(num2 + num, files.Length);
			if (num2 >= files.Length)
			{
				break;
			}
			string[] slice = new string[num3 - num2];
			Array.Copy(files, num2, slice, 0, slice.Length);
			ThreadPool.QueueUserWorkItem(delegate
			{
				string[] array = slice;
				foreach (string archiveFileName in array)
				{
					try
					{
						using ZipArchive zipArchive = ZipFile.OpenRead(archiveFileName);
						_ = zipArchive.Entries.Count;
					}
					catch
					{
					}
				}
			});
		}
	}
}
