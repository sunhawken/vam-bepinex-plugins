using BepInEx;
using HarmonyLib;

namespace MicAlwaysOn;

[BepInPlugin("vam.micalwayson", "MicAlwaysOn", "1.1.0")]
public class MicAlwaysOnPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        new Harmony("vam.micalwayson").PatchAll();
    }
}

[HarmonyPatch(typeof(OVRLipSyncMicInput), "Start")]
internal static class Patch_MicStart
{
    private static void Postfix(OVRLipSyncMicInput __instance)
    {
        __instance.micControl = (micActivation)2;
        __instance.StartMicrophone();
    }
}
