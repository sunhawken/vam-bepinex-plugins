using HarmonyLib;

namespace MicAlwaysOn;

[HarmonyPatch(typeof(OVRLipSyncMicInput), "Start")]
internal static class Patch_MicStart
{
	private static void Postfix(OVRLipSyncMicInput __instance)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		__instance.micControl = (micActivation)2;
		__instance.StartMicrophone();
	}
}
