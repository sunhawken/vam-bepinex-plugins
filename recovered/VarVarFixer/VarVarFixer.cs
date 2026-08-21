using System;
using System.IO;
using BepInEx;

[BepInPlugin("com.vam.varvarcleaner", "VaM VarVar Cleaner", "1.0.0")]
public class VarVarFixer : BaseUnityPlugin
{
	private void Awake()
	{
		string path = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
		if (!Directory.Exists(path))
		{
			return;
		}
		string[] files = Directory.GetFiles(path, "*.var.var", SearchOption.AllDirectories);
		if (files.Length == 0)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		string[] array = files;
		foreach (string text in array)
		{
			string text2 = text.Substring(0, text.Length - 4);
			if (File.Exists(text2))
			{
				num2++;
				Logger.LogWarning((object)$"[VarVarFixer] Conflict — {Path.GetFileNameWithoutExtension(text2)} already exists as .var; .var.var left in place");
				continue;
			}
			try
			{
				File.Move(text, text2);
				num++;
				Logger.LogInfo((object)("[VarVarFixer] Fixed: " + Path.GetFileName(text2)));
			}
			catch (Exception ex)
			{
				num3++;
				Logger.LogError((object)("[VarVarFixer] " + Path.GetFileName(text) + ": " + ex.Message));
			}
		}
		Logger.LogInfo((object)$"[VarVarFixer] Renamed {num} files ({num2} conflicts left alone, {num3} errors).");
	}
}
