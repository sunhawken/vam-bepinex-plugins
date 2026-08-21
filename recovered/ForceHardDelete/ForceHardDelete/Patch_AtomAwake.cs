using HarmonyLib;

namespace ForceHardDelete;

[HarmonyPatch(typeof(Atom), "Awake")]
internal static class Patch_AtomAwake
{
	private static void Postfix(Atom __instance)
	{
		__instance.isPoolable = false;
	}
}
