using BepInEx;
using HarmonyLib;

namespace ForceHardDelete;

[BepInPlugin("vam.forceharddelete", "ForceHardDelete", "1.0.0")]
public class ForceHardDeletePlugin : BaseUnityPlugin
{
	private void Awake()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		new Harmony("vam.forceharddelete").PatchAll();
	}
}
