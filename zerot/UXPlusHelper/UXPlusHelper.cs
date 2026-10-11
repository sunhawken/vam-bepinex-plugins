using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.UXPlusHelper
{
	// Headless helper for everlaster's UXPlus session plugin. No window. It patches the running script in memory:
	//  1. the "Show Target On Hover" module ran every frame in Edit mode: it asked the scene for a fresh list of every
	//     free controller (about 30 per person) and wrote controller.hidden for each of them, changed or not. It now keeps
	//     that list for a moment and only writes the flag when it really changes. The result is identical, the work is not.
	//  2. errors in that per-frame update are contained and logged once, so a half-loaded atom cannot flood the log.
	[BepInPlugin("com.zerot.uxplus.helper", "UXPlus Helper", "1.0.0")]
	public sealed class UXPlusHelperPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgShowTarget;
		internal static ConfigEntry<float> CfgListSeconds;
		internal static ConfigEntry<bool> CfgContain;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;

		private void Awake()
		{
			Log = Logger;
			CfgShowTarget = Config.Bind<bool>("Speed", "OptimizeShowTargetOnHover", true, "Make UXPlus's Show Target On Hover cheaper (same behaviour).");
			CfgListSeconds = Config.Bind<float>("Speed", "ControllerListSeconds", 0.5f, "How long the list of controllers is reused before the scene is asked again.");
			CfgContain = Config.Bind<bool>("Stability", "ContainUpdateErrors", true, "Catch errors in that update so they cannot flood the log.");
			harmony = new Harmony("com.zerot.uxplus.helper");
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
			if (Time.unscaledTime < nextScan)
			{
				return;
			}
			nextScan = Time.unscaledTime + 1f;
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

		private void TryPatch(Assembly asm)
		{
			Type hover = asm.GetType("everlaster.UX.ShowTargetOnHover.ShowTargetOnHover", false);
			if (hover == null)
			{
				return;
			}
			MethodInfo update = hover.GetMethod("OnUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
			if (update == null)
			{
				Logger.LogWarning("UXPlus found, but ShowTargetOnHover.OnUpdate is not where this helper expects it (UXPlus version changed?). Not patched.");
				return;
			}
			if (CfgShowTarget.Value)
			{
				harmony.Patch(update, new HarmonyMethod(typeof(Hooks).GetMethod("HoverPrefix")), null, null, CfgContain.Value ? new HarmonyMethod(typeof(Hooks).GetMethod("HoverFinalizer")) : null);
				Logger.LogInfo("UXPlus Show Target On Hover patched.");
			}
		}
	}

	public static class Hooks
	{
		private static List<FreeControllerV3> cache;
		private static float nextList = -100f;
		private static bool failed;
		private static bool logged;

		// Same decisions as UXPlus's own loop, without the per-frame list and without redundant writes.
		public static bool HoverPrefix()
		{
			if (failed)
			{
				return true;
			}
			try
			{
				SuperController sc = SuperController.singleton;
				if (sc == null)
				{
					return false;
				}
				if (sc.gameMode == SuperController.GameMode.Play || sc.GetTargetShow())
				{
					return false;
				}
				float now = Time.unscaledTime;
				if (cache == null || now >= nextList)
				{
					cache = sc.GetAllFreeControllers();
					nextList = now + Mathf.Max(0f, UXPlusHelperPlugin.CfgListSeconds.Value);
				}
				if (cache == null)
				{
					return true;
				}
				bool hideInactive = UserPreferences.singleton.hideInactiveTargets;
				int count = cache.Count;
				for (int i = 0; i < count; i++)
				{
					FreeControllerV3 c = cache[i];
					if (c == null)
					{
						nextList = -100f;   // something was removed: ask the scene again on the next frame
						continue;
					}
					bool want;
					if (hideInactive && c.currentPositionState == FreeControllerV3.PositionState.Off && c.currentRotationState == FreeControllerV3.RotationState.Off)
					{
						want = true;
					}
					else
					{
						want = !c.highlighted;
					}
					if (c.hidden != want)
					{
						c.hidden = want;
					}
				}
				return false;
			}
			catch (Exception e)
			{
				failed = true;   // anything unexpected: hand control back to UXPlus's own code for good
				UXPlusHelperPlugin.Log.LogWarning("Show Target On Hover optimisation turned off: " + e.Message);
				return true;
			}
		}

		public static Exception HoverFinalizer(Exception __exception)
		{
			if (__exception != null && !logged)
			{
				logged = true;
				UXPlusHelperPlugin.Log.LogWarning("UXPlus Show Target On Hover update error contained (logged once): " + __exception.Message);
			}
			return null;
		}
	}
}
