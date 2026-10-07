using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.BodyLanguageHelper
{
	// Headless helper for CheesyFX's BodyLanguage_Lite + ArousalHUD (the .var plugins). No window. It patches the running
	// scripts in memory to cut their CPU cost and garbage:
	//  1. the contact scan called string-heavy name checks (IndexOf / ToLowerInvariant / Contains) for every touching
	//     collider several times a second; the results only depend on the names, so they are now remembered
	//  2. "is this rigidbody part of my atom" looped over every rigidbody of the atom for each touching collider; cached
	//  3. the body-pair rebuild (GetComponentsInChildren over every atom, 8-20 s apart) caused a hitch each time; it now runs
	//     far less often and is triggered immediately when atoms are added/removed/renamed instead
	//  4. ArousalHUD updated its text/bar every frame and built a string each time; limited to a steady rate
	[BepInPlugin("com.zerot.bodylanguage.helper", "BodyLanguage Helper", "1.0.0")]
	public sealed class BodyLanguageHelperPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgMemo;
		internal static ConfigEntry<bool> CfgContainCache;
		internal static ConfigEntry<bool> CfgRebuild;
		internal static ConfigEntry<float> CfgRebuildSeconds;
		internal static ConfigEntry<bool> CfgHud;
		internal static ConfigEntry<float> CfgHudRate;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;
		private float nextAtomCheck;
		private int atomSignature = int.MinValue;
		private float rebuildAt = -1f;

		private void Awake()
		{
			Log = Logger;
			CfgMemo = Config.Bind<bool>("Speed", "RememberNameChecks", true, "Remember the results of BodyLanguage's name checks (receiver radius/weight, same-limb test, stimulator match).");
			CfgContainCache = Config.Bind<bool>("Speed", "CacheOwnAtomCheck", true, "Cache 'is this rigidbody part of my atom' instead of searching the atom's rigidbodies for every contact.");
			CfgRebuild = Config.Bind<bool>("Speed", "ReduceBodyPairRebuilds", true, "Rebuild body pairs far less often (removes the hitch every 8-20 s). Rebuilds immediately when atoms change.");
			CfgRebuildSeconds = Config.Bind<float>("Speed", "BodyPairRebuildSeconds", 60f, "Minimum seconds between automatic body-pair rebuilds (only used when ReduceBodyPairRebuilds is on).");
			CfgHud = Config.Bind<bool>("Speed", "LimitHudUpdates", true, "Limit how often ArousalHUD updates its bar and text.");
			CfgHudRate = Config.Bind<float>("Speed", "HudUpdatesPerSecond", 20f, "ArousalHUD update rate when LimitHudUpdates is on.");
			harmony = new Harmony("com.zerot.bodylanguage.helper");
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				pending.Enqueue(a);
			}
		}

		private void OnDestroy()
		{
			AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
		}

		private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			lock (pending)
			{
				pending.Enqueue(args.LoadedAssembly);
			}
		}

		private void Update()
		{
			float now = Time.unscaledTime;
			if (now >= nextScan)
			{
				nextScan = now + 1f;
				ProcessPending();
			}
			if (CfgRebuild.Value && now >= nextAtomCheck)
			{
				nextAtomCheck = now + 1f;
				WatchAtoms(now);
			}
		}

		private void ProcessPending()
		{
			while (true)
			{
				Assembly a;
				lock (pending)
				{
					if (pending.Count == 0)
					{
						return;
					}
					a = pending.Dequeue();
				}
				if (a == null || !seen.Add(a))
				{
					continue;
				}
				try
				{
					TryPatch(a);
				}
				catch (Exception e)
				{
					Logger.LogWarning("Patching " + a.FullName + " failed: " + e.Message);
				}
			}
		}

		// When the set of atoms changes, rebuild pairs once the scene has settled (the plugin itself only rebuilds on a timer).
		private void WatchAtoms(float now)
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			int sig = 17;
			List<Atom> atoms = sc.GetAtoms();
			sig = sig * 31 + atoms.Count;
			for (int i = 0; i < atoms.Count; i++)
			{
				if (atoms[i] != null && atoms[i].uid != null)
				{
					sig = sig * 31 + atoms[i].uid.GetHashCode();
				}
			}
			if (atomSignature == int.MinValue)
			{
				atomSignature = sig;
				return;
			}
			if (sig != atomSignature)
			{
				atomSignature = sig;
				rebuildAt = now + 1.5f;
			}
			if (rebuildAt >= 0f && now >= rebuildAt)
			{
				rebuildAt = -1f;
				Hooks.RebuildAll();
			}
		}

		private void TryPatch(Assembly asm)
		{
			Type bl = asm.GetType("CheesyFX.BodyLanguage_Lite", false);
			Type hud = asm.GetType("ArousalHUD", false);
			BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			int count = 0;
			if (bl != null)
			{
				Hooks.BindBl(bl);
				MethodInfo update = bl.GetMethod("Update", all);
				if (update != null)
				{
					harmony.Patch(update, new HarmonyMethod(typeof(Hooks).GetMethod("TrackInstance")));
					count++;
				}
				if (CfgMemo.Value)
				{
					count += Pre(bl, "ReceiverRadius", "RadiusPrefix", "RadiusPostfix", all);
					count += Pre(bl, "ReceiverWeight", "WeightPrefix", "WeightPostfix", all);
					count += Pre(bl, "IsSameLimbOrNearbyDefaultPose", "LimbPrefix", "LimbPostfix", all);
					count += Pre(bl, "MatchesAny", "MatchPrefix", "MatchPostfix", all);
				}
				if (CfgContainCache.Value)
				{
					count += Pre(bl, "IsRigidbodyFromContainingAtom", "ContainPrefix", "ContainPostfix", all);
				}
				if (CfgRebuild.Value)
				{
					MethodInfo interval = bl.GetMethod("EffectivePairRebuildInterval", all);
					if (interval != null)
					{
						harmony.Patch(interval, null, new HarmonyMethod(typeof(Hooks).GetMethod("IntervalPostfix")));
						count++;
					}
				}
				MethodInfo rebuild = bl.GetMethod("RebuildBodyPairs", all);
				if (rebuild != null)
				{
					harmony.Patch(rebuild, null, new HarmonyMethod(typeof(Hooks).GetMethod("RebuiltPostfix")));
					count++;
				}
			}
			if (hud != null && CfgHud.Value)
			{
				MethodInfo update = hud.GetMethod("Update", all);
				if (update != null)
				{
					harmony.Patch(update, new HarmonyMethod(typeof(Hooks).GetMethod("HudPrefix")));
					count++;
				}
			}
			if (count > 0)
			{
				Logger.LogInfo("BodyLanguage patched (" + count + " methods in " + asm.GetName().Name + ").");
			}
		}

		private int Pre(Type t, string method, string prefix, string postfix, BindingFlags flags)
		{
			MethodInfo m = t.GetMethod(method, flags);
			if (m == null)
			{
				return 0;
			}
			harmony.Patch(m, new HarmonyMethod(typeof(Hooks).GetMethod(prefix)), new HarmonyMethod(typeof(Hooks).GetMethod(postfix)));
			return 1;
		}
	}

	public static class Hooks
	{
		private static readonly Dictionary<string, float> radius = new Dictionary<string, float>();
		private static readonly Dictionary<string, float> weight = new Dictionary<string, float>();
		private static readonly Dictionary<string, Dictionary<string, bool>> limb = new Dictionary<string, Dictionary<string, bool>>();
		private static readonly Dictionary<string[], Dictionary<string, bool>> match = new Dictionary<string[], Dictionary<string, bool>>();
		private static readonly Dictionary<object, Dictionary<Rigidbody, bool>> contain = new Dictionary<object, Dictionary<Rigidbody, bool>>();
		private static readonly Dictionary<object, bool> instances = new Dictionary<object, bool>();
		private static readonly Dictionary<object, float> hudLast = new Dictionary<object, float>();
		private static MethodInfo forceRebuild;

		internal static void BindBl(Type bl)
		{
			forceRebuild = bl.GetMethod("ForceRebuildPairs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		}

		// ---- instance tracking (so a rebuild can be requested for every BodyLanguage_Lite in the scene)
		public static void TrackInstance(object __instance)
		{
			if (__instance != null && !instances.ContainsKey(__instance))
			{
				instances[__instance] = true;
			}
		}

		internal static void RebuildAll()
		{
			if (forceRebuild == null)
			{
				return;
			}
			List<object> dead = new List<object>();
			foreach (object o in instances.Keys)
			{
				UnityEngine.Object u = o as UnityEngine.Object;
				if (u == null)
				{
					dead.Add(o);
					continue;
				}
				try
				{
					forceRebuild.Invoke(o, null);
				}
				catch
				{
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				instances.Remove(dead[i]);
				contain.Remove(dead[i]);
				hudLast.Remove(dead[i]);
			}
		}

		// ---- receiver radius / weight
		public static bool RadiusPrefix(string name, ref float __result)
		{
			if (name == null)
			{
				return true;
			}
			return !radius.TryGetValue(name, out __result);
		}

		public static void RadiusPostfix(string name, float __result)
		{
			if (name != null)
			{
				radius[name] = __result;
			}
		}

		public static bool WeightPrefix(string name, ref float __result)
		{
			if (name == null)
			{
				return true;
			}
			return !weight.TryGetValue(name, out __result);
		}

		public static void WeightPostfix(string name, float __result)
		{
			if (name != null)
			{
				weight[name] = __result;
			}
		}

		// ---- same-limb test
		public static bool LimbPrefix(string stimulatorName, string receiverName, ref bool __result)
		{
			if (stimulatorName == null || receiverName == null)
			{
				return true;
			}
			Dictionary<string, bool> inner;
			if (limb.TryGetValue(stimulatorName, out inner) && inner.TryGetValue(receiverName, out __result))
			{
				return false;
			}
			return true;
		}

		public static void LimbPostfix(string stimulatorName, string receiverName, bool __result)
		{
			if (stimulatorName == null || receiverName == null)
			{
				return;
			}
			Dictionary<string, bool> inner;
			if (!limb.TryGetValue(stimulatorName, out inner))
			{
				inner = new Dictionary<string, bool>();
				limb[stimulatorName] = inner;
			}
			inner[receiverName] = __result;
		}

		// ---- token match (name contains any token), keyed by the token array
		public static bool MatchPrefix(string name, string[] tokens, ref bool __result)
		{
			if (name == null || tokens == null)
			{
				return true;
			}
			Dictionary<string, bool> inner;
			if (match.TryGetValue(tokens, out inner) && inner.TryGetValue(name, out __result))
			{
				return false;
			}
			return true;
		}

		public static void MatchPostfix(string name, string[] tokens, bool __result)
		{
			if (name == null || tokens == null)
			{
				return;
			}
			Dictionary<string, bool> inner;
			if (!match.TryGetValue(tokens, out inner))
			{
				inner = new Dictionary<string, bool>();
				match[tokens] = inner;
			}
			if (inner.Count < 4096)
			{
				inner[name] = __result;
			}
		}

		// ---- "rigidbody belongs to my atom"
		public static bool ContainPrefix(object __instance, Rigidbody rb, ref bool __result)
		{
			if (rb == null)
			{
				return true;
			}
			Dictionary<Rigidbody, bool> inner;
			if (contain.TryGetValue(__instance, out inner) && inner.TryGetValue(rb, out __result))
			{
				return false;
			}
			return true;
		}

		public static void ContainPostfix(object __instance, Rigidbody rb, bool __result)
		{
			if (rb == null)
			{
				return;
			}
			Dictionary<Rigidbody, bool> inner;
			if (!contain.TryGetValue(__instance, out inner))
			{
				inner = new Dictionary<Rigidbody, bool>();
				contain[__instance] = inner;
			}
			if (inner.Count < 2048)
			{
				inner[rb] = __result;
			}
		}

		// The atom's rigidbodies can change when the look changes; start the cache over after every rebuild.
		public static void RebuiltPostfix(object __instance)
		{
			contain.Remove(__instance);
		}

		public static void IntervalPostfix(ref float __result)
		{
			float min = BodyLanguageHelperPlugin.CfgRebuildSeconds.Value;
			if (__result < min)
			{
				__result = min;
			}
		}

		// ---- ArousalHUD
		public static bool HudPrefix(object __instance)
		{
			float rate = Mathf.Max(1f, BodyLanguageHelperPlugin.CfgHudRate.Value);
			float now = Time.unscaledTime;
			float prev;
			if (hudLast.TryGetValue(__instance, out prev) && now - prev < 1f / rate)
			{
				return false;
			}
			hudLast[__instance] = now;
			return true;
		}
	}
}
