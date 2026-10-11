using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.DecalMakerHelper
{
	// Headless helper for Chokaphi's DecalMaker 2 (the .var plugin you attach to a person). It has no window. It patches
	// the running DecalMaker scripts in memory so they use far less VRAM, apply faster and cannot get stuck:
	//  1. temporary render textures are made without 8x MSAA (a 4096 texture with 8x MSAA is ~0.5 GB each, and every
	//     apply asks for two or three of them; the decal shaders are plain full-screen blits, MSAA does nothing there)
	//  2. the alpha-clip step released a pooled temporary the wrong way (leaked one render texture per apply)
	//  3. while a slider is dragged DecalMaker re-rendered every frame; renders are now limited to a minimum interval
	//  4. an exception inside an apply left the renderer "processing" forever (decals stop updating); it now recovers
	//  5. optional resolution cap for all decal/normal/spec render textures (off by default)
	[BepInPlugin("com.zerot.decalmaker.helper", "DecalMaker Helper", "1.0.0")]
	public sealed class DecalMakerHelper : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgMsaa;
		internal static ConfigEntry<bool> CfgLeak;
		internal static ConfigEntry<bool> CfgThrottle;
		internal static ConfigEntry<float> CfgInterval;
		internal static ConfigEntry<bool> CfgRecover;
		internal static ConfigEntry<int> CfgMaxResolution;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;
		private int patchedAssemblies;

		private void Awake()
		{
			Log = Logger;
			CfgMsaa = Config.Bind<bool>("Memory", "NoMsaaOnTempTextures", true, "Create DecalMaker's temporary render textures without 8x MSAA. Big VRAM and speed saving, no visual change.");
			CfgLeak = Config.Bind<bool>("Memory", "FixAlphaClipLeak", true, "Release the alpha-clip temporary texture correctly (stops a render texture leaking on every apply).");
			CfgMaxResolution = Config.Bind<int>("Memory", "MaxRenderResolution", 0, "Cap DecalMaker's render texture size (0 = off, e.g. 2048 or 1024). Lower = less VRAM and faster, softer decals.");
			CfgThrottle = Config.Bind<bool>("Speed", "ThrottleRenders", true, "Limit how often a changing DecalMaker texture is re-rendered (slider drags, moving a decal).");
			CfgInterval = Config.Bind<float>("Speed", "MinRenderIntervalSeconds", 0.1f, "Minimum time between two renders of the same texture while it keeps changing.");
			CfgRecover = Config.Bind<bool>("Stability", "RecoverFromApplyErrors", true, "If an apply throws, reset the renderer so later changes still update instead of freezing.");
			harmony = new Harmony("com.zerot.decalmaker.helper");
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
						break;
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
			Throttle.Cleanup();
		}

		private void TryPatch(Assembly asm)
		{
			Type settings = asm.GetType("VAM_Decal_Maker_2.Helpers.PluginSettings", false);
			Type baseRender = asm.GetType("VAM_Decal_Maker_2.Render.BaseRender", false);
			if (settings == null || baseRender == null)
			{
				return;
			}
			BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			int count = 0;

			MethodInfo getRt = settings.GetMethod("GetRenderTextureFromMatSlot", all);
			if (CfgMsaa.Value && getRt != null)
			{
				harmony.Patch(getRt, null, null, new HarmonyMethod(typeof(Patches).GetMethod("MsaaTranspiler")));
				count++;
			}
			MethodInfo toInt = settings.GetMethod("ToInt", all);
			if (toInt != null)
			{
				harmony.Patch(toInt, null, new HarmonyMethod(typeof(Patches).GetMethod("CapResolution")));
				count++;
			}
			Type decal = asm.GetType("VAM_Decal_Maker_2.Render.DecalRender", false);
			if (CfgLeak.Value && decal != null)
			{
				MethodInfo clip = decal.GetMethod("AlphaClip", all);
				if (clip != null)
				{
					harmony.Patch(clip, null, null, new HarmonyMethod(typeof(Patches).GetMethod("ReleaseTranspiler")));
					count++;
				}
			}
			MethodInfo update = baseRender.GetMethod("Update", all);
			if (CfgThrottle.Value && update != null)
			{
				Throttle.Bind(baseRender);
				harmony.Patch(update, new HarmonyMethod(typeof(Throttle).GetMethod("Prefix")));
				count++;
			}
			if (CfgRecover.Value)
			{
				string[] names = { "DecalRender", "NormalRender", "SpecularRender" };
				foreach (string n in names)
				{
					Type t = asm.GetType("VAM_Decal_Maker_2.Render." + n, false);
					MethodInfo apply = t != null ? t.GetMethod("ApplyChanges", all) : null;
					if (apply != null)
					{
						harmony.Patch(apply, null, null, null, new HarmonyMethod(typeof(Throttle).GetMethod("Finalizer")));
						count++;
					}
				}
			}
			patchedAssemblies++;
			Logger.LogInfo("DecalMaker patched (" + count + " methods, assembly #" + patchedAssemblies + ").");
		}
	}

	public static class Patches
	{
		// GetTemporary(w, h, depth, format, readWrite, antiAliasing): the last argument is the literal 8.
		public static IEnumerable<CodeInstruction> MsaaTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 1; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && mi != null && mi.Name == "GetTemporary" && mi.DeclaringType == typeof(RenderTexture) && mi.GetParameters().Length == 6 && list[i - 1].opcode == OpCodes.Ldc_I4_8)
				{
					list[i - 1] = new CodeInstruction(OpCodes.Ldc_I4_1);
				}
			}
			return list;
		}

		// rt.Release() on a pooled temporary -> RenderTexture.ReleaseTemporary(rt)
		public static IEnumerable<CodeInstruction> ReleaseTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo release = typeof(RenderTexture).GetMethod("ReleaseTemporary", new Type[] { typeof(RenderTexture) });
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && mi != null && mi.Name == "Release" && mi.DeclaringType == typeof(RenderTexture) && mi.GetParameters().Length == 0)
				{
					list[i] = new CodeInstruction(OpCodes.Call, release);
				}
			}
			return list;
		}

		public static void CapResolution(ref int __result)
		{
			int cap = DecalMakerHelper.CfgMaxResolution.Value;
			if (cap >= 64 && __result > cap)
			{
				__result = cap;
			}
		}
	}

	public static class Throttle
	{
		private static readonly Dictionary<object, float> last = new Dictionary<object, float>();
		private static FieldInfo fDirty;
		private static FieldInfo fProcessing;
		private static float nextCleanup;
		private static bool errorLogged;

		internal static void Bind(Type baseRender)
		{
			BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			fDirty = baseRender.GetField("_isDirty", f);
			fProcessing = baseRender.GetField("_Processing", f);
		}

		internal static void Cleanup()
		{
			if (Time.unscaledTime < nextCleanup)
			{
				return;
			}
			nextCleanup = Time.unscaledTime + 30f;
			List<object> dead = new List<object>();
			foreach (object o in last.Keys)
			{
				UnityEngine.Object u = o as UnityEngine.Object;
				if (u == null)
				{
					dead.Add(o);
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				last.Remove(dead[i]);
			}
		}

		// Skip this frame's Update while the texture changed again too soon after its last render.
		public static bool Prefix(object __instance)
		{
			try
			{
				if (fDirty == null || fProcessing == null || !(bool)fDirty.GetValue(__instance) || (bool)fProcessing.GetValue(__instance))
				{
					return true;
				}
				float now = Time.unscaledTime;
				float prev;
				if (last.TryGetValue(__instance, out prev) && now - prev < DecalMakerHelper.CfgInterval.Value)
				{
					return false;
				}
				last[__instance] = now;
			}
			catch
			{
			}
			return true;
		}

		// An exception inside ApplyChanges leaves _Processing = true forever; reset it and swallow the error once logged.
		public static Exception Finalizer(Exception __exception, object __instance)
		{
			if (__exception == null)
			{
				return null;
			}
			try
			{
				if (fProcessing != null)
				{
					fProcessing.SetValue(__instance, false);
				}
				if (fDirty != null)
				{
					fDirty.SetValue(__instance, false);
				}
				if (!errorLogged)
				{
					errorLogged = true;
					DecalMakerHelper.Log.LogWarning("DecalMaker apply failed and was recovered (first occurrence logged): " + __exception.Message);
				}
			}
			catch
			{
			}
			return null;
		}
	}
}
