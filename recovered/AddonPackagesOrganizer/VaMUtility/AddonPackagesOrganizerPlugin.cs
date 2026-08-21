using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace VaMUtility;

[BepInPlugin("com.vam.addonpackagesorganizer", "AddonPackagesOrganizer", "1.0.0")]
public class AddonPackagesOrganizerPlugin : BaseUnityPlugin
{
	private static readonly HashSet<string> KeepAtTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"AcidBubbles.Timeline.210", "DJ.NailPolish.1", "DJ.TanLines.1", "DJ.TanLines.2", "Jackaroo.SmartSuitJaR.1", "JayC_Re-animator.Hair_Curly_Bob.1", "MacGruber.Life.12", "MeshedVR.3PointLightSetup.1", "MeshedVR.AssetsPack.1", "MeshedVR.BonusScenes.9",
		"MeshedVR.DemoScenes.2", "MeshedVR.OlderContent.1", "MeshedVR.PresetsPack.2", "NoOC.Clothing_SailorLingerie.2", "NoStage3.Hair_Long_Upswept_Top_Bun.1", "NoStage3.UnityAssetVamifier.20", "Vince.Clothing_PleatedSkirtV2T.2", "Xstatic.MegaParticlePack.1"
	};

	private void Awake()
	{
		try
		{
			string text = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackages");
			if (!Directory.Exists(text))
			{
				Logger.LogWarning((object)"AddonPackagesOrganizer: AddonPackages folder not found.");
				return;
			}
			string text2 = Path.Combine(text, "extra");
			string[] files = Directory.GetFiles(text, "*.var", SearchOption.TopDirectoryOnly);
			string[] files2 = Directory.GetFiles(text, "*.DISABLED", SearchOption.TopDirectoryOnly);
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			foreach (string item2 in IterateBoth(files, files2))
			{
				string fileName = Path.GetFileName(item2);
				if (IsProtected(fileName))
				{
					num2++;
					continue;
				}
				string item = StripExtension(fileName);
				if (KeepAtTop.Contains(item))
				{
					num2++;
					continue;
				}
				string text3 = Path.Combine(text2, fileName);
				try
				{
					if (File.Exists(text3))
					{
						Logger.LogWarning((object)("AddonPackagesOrganizer: '" + fileName + "' already exists in extra\\, skipping."));
						num3++;
					}
					else
					{
						Directory.CreateDirectory(text2);
						File.Move(item2, text3);
						num++;
					}
				}
				catch (Exception ex)
				{
					Logger.LogError((object)("AddonPackagesOrganizer: failed moving " + fileName + ": " + ex.Message));
					num3++;
				}
			}
			Logger.LogInfo((object)$"AddonPackagesOrganizer: kept {num2} at top, moved {num} to extra\\, {num3} skipped.");
		}
		catch (Exception ex2)
		{
			Logger.LogError((object)("AddonPackagesOrganizer: " + ex2));
		}
	}

	private static IEnumerable<string> IterateBoth(string[] a, string[] b)
	{
		string[] array = a;
		for (int i = 0; i < array.Length; i++)
		{
			yield return array[i];
		}
		array = b;
		for (int i = 0; i < array.Length; i++)
		{
			yield return array[i];
		}
	}

	private static bool IsProtected(string fileName)
	{
		if (fileName.EndsWith(".par2", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (string.Equals(fileName, "VamPar2.dll", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	private static string StripExtension(string fileName)
	{
		if (fileName.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
		{
			return fileName.Substring(0, fileName.Length - 4);
		}
		if (fileName.EndsWith(".DISABLED", StringComparison.OrdinalIgnoreCase))
		{
			return fileName.Substring(0, fileName.Length - 9);
		}
		return fileName;
	}
}
