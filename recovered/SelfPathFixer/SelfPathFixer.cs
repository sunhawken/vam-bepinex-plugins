using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

[BepInPlugin("com.zerot.selfpathfixer", "SelfPathFixer", "1.0.0")]
public class SelfPathFixer : BaseUnityPlugin
{
	private ConfigEntry<bool> cfgRunOnStartup;

	private ConfigEntry<bool> cfgBackupVars;

	private ConfigEntry<bool> cfgFixScenes;

	private ConfigEntry<KeyCode> cfgHotkey;

	private ConfigEntry<bool> cfgDryRun;

	private static ManualLogSource Log;

	private readonly Queue<string> _logQueue = new Queue<string>();

	private readonly object _logLock = new object();

	private bool _running;

	private void Awake()
	{
		Log = Logger;
		cfgRunOnStartup = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "RunOnStartup", true, "Automatically scan and fix SELF:/ references when VaM starts.");
		cfgBackupVars = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "BackupVarFiles", true, "Create a .bak copy of each .var before modifying it.");
		cfgFixScenes = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "FixSceneFiles", true, "Also scan and fix SELF:/ references in Saves/scene JSON files.");
		cfgHotkey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>("General", "Hotkey", (KeyCode)0, "Press to trigger a manual scan. None = disabled.");
		cfgDryRun = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "DryRun", false, "Log what would change without writing any files.");
		if (cfgRunOnStartup.Value)
		{
			((MonoBehaviour)this).StartCoroutine(StartAfterDelay(5f));
		}
		Log.LogInfo((object)"SelfPathFixer v1.0.0 ready.");
	}

	private void Update()
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		// Explicit single-argument Monitor.Enter. This project targets net40, where `lock`
		// compiles to Monitor.Enter(object, ref bool) - an overload VaM's Mono mscorlib does
		// not have. Sitting in Update(), that threw MissingMethodException every frame
		// (~49k per session) and cost roughly 40% of the frame rate.
		Monitor.Enter(_logLock);
		try
		{
			if (_logQueue.Count > 0)
			{
				Log.LogInfo((object)_logQueue.Dequeue());
			}
		}
		finally
		{
			Monitor.Exit(_logLock);
		}
		if ((int)cfgHotkey.Value != 0 && Input.GetKeyDown(cfgHotkey.Value))
		{
			((MonoBehaviour)this).StartCoroutine(StartAfterDelay(0f));
		}
	}

	private IEnumerator StartAfterDelay(float delay)
	{
		if (delay > 0f)
		{
			yield return (object)new WaitForSeconds(delay);
		}
		if (_running)
		{
			Log.LogWarning((object)"SelfPathFixer: Already running, skipping.");
			yield break;
		}
		_running = true;
		string vamRoot = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
		string addonDir = Path.Combine(vamRoot, "AddonPackages");
		string sceneDir = Path.Combine(vamRoot, "Saves", "scene");
		bool backup = cfgBackupVars.Value;
		bool scenes = cfgFixScenes.Value;
		bool dryRun = cfgDryRun.Value;
		Queue<string> logQ = _logQueue;
		object logL = _logLock;
		bool done = false;
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				WorkerRun(addonDir, sceneDir, backup, scenes, dryRun, logQ, logL);
			}
			catch (Exception ex)
			{
				Enqueue(logQ, logL, "SelfPathFixer ERROR: " + ex);
			}
			finally
			{
				done = true;
			}
		});
		while (!done)
		{
			yield return null;
		}
		_running = false;
	}

	private static void WorkerRun(string addonDir, string sceneDir, bool backup, bool fixScenes, bool dryRun, Queue<string> logQ, object logL)
	{
		string text = (dryRun ? "[DRY RUN] " : "");
		Enqueue(logQ, logL, text + "SelfPathFixer: Starting scan...");
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		if (Directory.Exists(addonDir))
		{
			string[] files = Directory.GetFiles(addonDir, "*.var", SearchOption.AllDirectories);
			string[] array = files;
			foreach (string text2 in array)
			{
				num2++;
				string text3 = Path.GetFileNameWithoutExtension(text2);
				if (text3.EndsWith(".disabled"))
				{
					text3 = text3.Substring(0, text3.Length - ".disabled".Length);
				}
				try
				{
					if (FixVarFile(text2, text3, backup, dryRun))
					{
						num++;
						Enqueue(logQ, logL, text + "Fixed var: " + text3);
					}
				}
				catch (Exception ex)
				{
					Enqueue(logQ, logL, "SelfPathFixer: " + text3 + " -- " + ex.Message);
				}
			}
		}
		else
		{
			Enqueue(logQ, logL, "SelfPathFixer: AddonPackages not found: " + addonDir);
		}
		if (fixScenes && Directory.Exists(sceneDir))
		{
			string[] files2 = Directory.GetFiles(sceneDir, "*.json", SearchOption.AllDirectories);
			string[] array2 = files2;
			foreach (string text4 in array2)
			{
				num4++;
				try
				{
					if (FixSceneFile(text4, dryRun))
					{
						num3++;
						Enqueue(logQ, logL, text + "Fixed scene: " + Path.GetFileName(text4));
					}
				}
				catch (Exception ex2)
				{
					Enqueue(logQ, logL, "SelfPathFixer: scene " + Path.GetFileName(text4) + " -- " + ex2.Message);
				}
			}
		}
		Enqueue(logQ, logL, text + "SelfPathFixer: Done.  vars=" + num2 + " fixed=" + num + "  scenes=" + num4 + " fixed=" + num3);
	}

	private static bool FixVarFile(string varPath, string pkgName, bool backup, bool dryRun)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected O, but got Unknown
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Expected O, but got Unknown
		if (!FileContainsBytes(varPath, Encoding.UTF8.GetBytes("SELF:/")))
		{
			return false;
		}
		if (dryRun)
		{
			return true;
		}
		if (backup)
		{
			string text = varPath + ".bak";
			if (!File.Exists(text))
			{
				File.Copy(varPath, text);
			}
		}
		string newValue = pkgName + ":/";
		string text2 = varPath + ".tmp";
		bool flag = false;
		ZipFile val = new ZipFile(varPath);
		try
		{
			ZipOutputStream val2 = new ZipOutputStream((Stream)File.Create(text2));
			try
			{
				val2.SetLevel(6);
				foreach (ZipEntry item in val)
				{
					ZipEntry val3 = item;
					byte[] array;
					using (Stream stream = val.GetInputStream(val3))
					{
						array = ReadAllBytes(stream);
					}
					if (val3.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
					{
						string text3 = Encoding.UTF8.GetString(array);
						if (text3.Contains("SELF:/"))
						{
							array = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text3.Replace("SELF:/", newValue));
							flag = true;
						}
					}
					ZipEntry val4 = new ZipEntry(val3.Name);
					val4.DateTime = val3.DateTime;
					val4.Size = array.Length;
					val2.PutNextEntry(val4);
					((Stream)(object)val2).Write(array, 0, array.Length);
					val2.CloseEntry();
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		if (flag)
		{
			File.Delete(varPath);
			File.Move(text2, varPath);
		}
		else
		{
			File.Delete(text2);
		}
		return flag;
	}

	private static bool FixSceneFile(string jsonPath, bool dryRun)
	{
		string text = File.ReadAllText(jsonPath, Encoding.UTF8);
		if (!text.Contains("SELF:/"))
		{
			return false;
		}
		if (dryRun)
		{
			return true;
		}
		File.WriteAllText(jsonPath, text.Replace("SELF:/", ""), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return true;
	}

	private static bool FileContainsBytes(string path, byte[] needle)
	{
		byte[] array = new byte[65536];
		byte[] array2 = new byte[needle.Length - 1];
		bool flag = true;
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			int num;
			while ((num = fileStream.Read(array, 0, array.Length)) > 0)
			{
				byte[] array3;
				if (flag)
				{
					array3 = new byte[num];
					Buffer.BlockCopy(array, 0, array3, 0, num);
					flag = false;
				}
				else
				{
					array3 = new byte[array2.Length + num];
					Buffer.BlockCopy(array2, 0, array3, 0, array2.Length);
					Buffer.BlockCopy(array, 0, array3, array2.Length, num);
				}
				if (IndexOf(array3, needle) >= 0)
				{
					return true;
				}
				int num2 = Math.Min(needle.Length - 1, num);
				Buffer.BlockCopy(array, num - num2, array2, 0, num2);
			}
		}
		return false;
	}

	private static int IndexOf(byte[] haystack, byte[] needle)
	{
		int num = haystack.Length - needle.Length;
		for (int i = 0; i <= num; i++)
		{
			bool flag = true;
			for (int j = 0; j < needle.Length; j++)
			{
				if (haystack[i + j] != needle[j])
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return i;
			}
		}
		return -1;
	}

	private static byte[] ReadAllBytes(Stream stream)
	{
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[4096];
		int count;
		while ((count = stream.Read(array, 0, array.Length)) > 0)
		{
			memoryStream.Write(array, 0, count);
		}
		return memoryStream.ToArray();
	}

	private static void Enqueue(Queue<string> q, object lk, string msg)
	{
		// Same reason as Update(): avoid the net40 two-argument Monitor.Enter overload.
		Monitor.Enter(lk);
		try
		{
			q.Enqueue(msg);
		}
		finally
		{
			Monitor.Exit(lk);
		}
	}
}
