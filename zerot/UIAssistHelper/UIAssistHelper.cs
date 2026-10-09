using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.UIAssistHelper
{
	// Headless helper for JayJayWon's UIAssist (Patron) session plugin. No window. It patches the running scripts in
	// memory so UIAssist costs less and cannot spam errors:
	//  1. every 0.1 s UIAssist rebuilt its gaze-target lists (about 25 new lists/dictionaries, a pass over every atom and
	//     every controller); this now runs at a lower rate (shorter in VR where gaze selection needs to feel immediate)
	//  2. while Heel Adjust is on it re-listed its folder every second; now every few seconds
	//  3. exceptions inside its per-frame updates (a destroyed person, a half-loaded atom...) were logged every frame and
	//     stopped the rest of the update from running, freezing the HUD; they are now contained and logged once
	[BepInPlugin("com.zerot.uiassist.helper", "UIAssist Helper", "1.0.0")]
	public sealed class UIAssistHelperPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgThrottle;
		internal static ConfigEntry<float> CfgDesktopInterval;
		internal static ConfigEntry<float> CfgVrInterval;
		internal static ConfigEntry<bool> CfgContain;
		internal static ConfigEntry<float> CfgHeelInterval;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;

		private void Awake()
		{
			Log = Logger;
			CfgThrottle = Config.Bind<bool>("Speed", "ThrottleGazeTargets", true, "Rebuild UIAssist's gaze-target lists less often.");
			CfgDesktopInterval = Config.Bind<float>("Speed", "DesktopIntervalSeconds", 0.35f, "Seconds between gaze-target rebuilds on desktop (UIAssist's own value is 0.1).");
			CfgVrInterval = Config.Bind<float>("Speed", "VrIntervalSeconds", 0.15f, "Seconds between gaze-target rebuilds in VR.");
			CfgHeelInterval = Config.Bind<float>("Speed", "HeelAdjustFileListSeconds", 5f, "Seconds between re-reading the Heel Adjust folder (UIAssist lists it every second while Heel Adjust is on). 0 = leave as is.");
			CfgContain = Config.Bind<bool>("Stability", "ContainUpdateErrors", true, "Catch errors in UIAssist's per-frame updates so they cannot stop the HUD or flood the log.");
			harmony = new Harmony("com.zerot.uiassist.helper");
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
			Type target = asm.GetType("JayJayWon.TargetControl", false);
			Type grid = asm.GetType("JayJayWon.GameControlUI", false);
			Type clothing = asm.GetType("JayJayWon.ActiveClothingList", false);
			if (target == null || grid == null)
			{
				return;
			}
			BindingFlags st = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
			int count = 0;
			MethodInfo gaze = target.GetMethod("UpdateGazeTargets", st, null, Type.EmptyTypes, null);
			if (CfgThrottle.Value && gaze != null)
			{
				harmony.Patch(gaze, new HarmonyMethod(typeof(Hooks).GetMethod("GazePrefix")));
				count++;
			}
			Type heel = asm.GetType("JayJayWon.HeelAdjustTool", false);
			MethodInfo heelFiles = heel != null ? heel.GetMethod("UpdateExistingHAFiles", st, null, Type.EmptyTypes, null) : null;
			if (CfgHeelInterval.Value > 0f && heelFiles != null)
			{
				harmony.Patch(heelFiles, new HarmonyMethod(typeof(Hooks).GetMethod("HeelPrefix")));
				count++;
			}
			if (CfgContain.Value)
			{
				MethodInfo gridUpdate = grid.GetMethod("Update", st, null, Type.EmptyTypes, null);
				if (gridUpdate != null)
				{
					harmony.Patch(gridUpdate, null, null, null, new HarmonyMethod(typeof(Hooks).GetMethod("GridFinalizer")));
					count++;
				}
				MethodInfo clothingUpdate = clothing != null ? clothing.GetMethod("Update", st, null, Type.EmptyTypes, null) : null;
				if (clothingUpdate != null)
				{
					harmony.Patch(clothingUpdate, null, null, null, new HarmonyMethod(typeof(Hooks).GetMethod("ClothingFinalizer")));
					count++;
				}
			}
			Logger.LogInfo("UIAssist patched (" + count + " methods).");
		}
	}

	public static class Hooks
	{
		private static float lastGaze = -100f;
		private static bool gridLogged;
		private static bool clothingLogged;

		// Skip a gaze-target rebuild that comes too soon after the previous one.
		public static bool GazePrefix()
		{
			float now = Time.unscaledTime;
			bool vr = false;
			try
			{
				SuperController sc = SuperController.singleton;
				vr = sc != null && (sc.isOVR || sc.isOpenVR);
			}
			catch
			{
			}
			float interval = vr ? UIAssistHelperPlugin.CfgVrInterval.Value : UIAssistHelperPlugin.CfgDesktopInterval.Value;
			if (now - lastGaze < interval)
			{
				return false;
			}
			lastGaze = now;
			return true;
		}

		private static float lastHeel = -100f;

		// Skip a Heel Adjust folder listing that comes too soon after the previous one.
		public static bool HeelPrefix()
		{
			float now = Time.unscaledTime;
			if (now - lastHeel < UIAssistHelperPlugin.CfgHeelInterval.Value)
			{
				return false;
			}
			lastHeel = now;
			return true;
		}

		public static Exception GridFinalizer(Exception __exception)
		{
			if (__exception != null && !gridLogged)
			{
				gridLogged = true;
				UIAssistHelperPlugin.Log.LogWarning("UIAssist HUD update error contained (logged once): " + __exception.Message);
			}
			return null;
		}

		public static Exception ClothingFinalizer(Exception __exception)
		{
			if (__exception != null && !clothingLogged)
			{
				clothingLogged = true;
				UIAssistHelperPlugin.Log.LogWarning("UIAssist clothing-list update error contained (logged once): " + __exception.Message);
			}
			return null;
		}
	}
}
