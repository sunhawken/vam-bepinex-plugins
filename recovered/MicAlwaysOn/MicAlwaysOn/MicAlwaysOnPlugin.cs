using BepInEx;
using HarmonyLib;

namespace MicAlwaysOn;

[BepInPlugin("vam.micalwayson", "MicAlwaysOn", "1.1.0")]
public class MicAlwaysOnPlugin : BaseUnityPlugin
{
	private void Awake()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		new Harmony("vam.micalwayson").PatchAll();
	}
}
