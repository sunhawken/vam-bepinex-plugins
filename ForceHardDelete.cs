using BepInEx;
using HarmonyLib;

namespace ForceHardDelete;

[BepInPlugin("vam.forceharddelete", "ForceHardDelete", "1.0.0")]
public class ForceHardDeletePlugin : BaseUnityPlugin
{
    private void Awake()
    {
        new Harmony("vam.forceharddelete").PatchAll();
    }
}

[HarmonyPatch(typeof(Atom), "Awake")]
internal static class Patch_AtomAwake
{
    private static void Postfix(Atom __instance)
    {
        __instance.isPoolable = false;
    }
}
