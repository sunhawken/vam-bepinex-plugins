using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("com.zerot.assetloaderhelper", "AssetLoaderHelper", "1.0.0")]
public class AssetLoaderHelper : BaseUnityPlugin
{
	private ConfigEntry<int> cfgMaxParallelBundles;

	private ConfigEntry<int> cfgMaxParallelScenes;

	private ConfigEntry<int> cfgTextureWorkerCount;

	private ConfigEntry<bool> cfgLogSlowLoads;

	private ConfigEntry<float> cfgSlowLoadThresholdMs;

	private ConfigEntry<bool> cfgLogStatsOnStart;

	private ConfigEntry<bool> cfgPeriodicStats;

	private ConfigEntry<float> cfgPeriodicStatsInterval;

	private ConfigEntry<KeyCode> cfgHotkeyStats;

	private static ManualLogSource Log;

	private static AssetLoaderHelper Instance;

	private Type _assetLoaderType;

	private Type _imageLoaderType;

	private object _assetLoaderSingleton;

	private object _imageLoaderSingleton;

	private FieldInfo _fiMaxAbLoads;

	private FieldInfo _fiMaxSceneLoads;

	private FieldInfo _fiWorkerCount;

	private FieldInfo _fiActiveWorkers;

	private FieldInfo _fiAbQueue;

	private FieldInfo _fiSceneQueue;

	private FieldInfo _fiStatDispatched;

	private FieldInfo _fiStatMemHit;

	private FieldInfo _fiStatDiskHit;

	private FieldInfo _fiStatNew;

	private FieldInfo _fiStatErrors;

	private FieldInfo _fiTextureCache;

	private FieldInfo _fiThumbnailCache;

	private float _nextStatTime;

	private void Awake()
	{
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Expected O, but got Unknown
		Instance = this;
		Log = Logger;
		int processorCount = Environment.ProcessorCount;
		cfgMaxParallelBundles = ((BaseUnityPlugin)this).Config.Bind<int>("AssetLoader", "MaxParallelBundleLoads", Math.Max(4, processorCount / 2), "Max concurrent asset-bundle loads (VaM default: 2). Higher = faster load on fast drives.");
		cfgMaxParallelScenes = ((BaseUnityPlugin)this).Config.Bind<int>("AssetLoader", "MaxParallelSceneLoads", Math.Max(2, processorCount / 4), "Max concurrent scene-into-transform loads (VaM default: 1).");
		cfgTextureWorkerCount = ((BaseUnityPlugin)this).Config.Bind<int>("ImageLoader", "TextureWorkerCount", Math.Max(4, processorCount / 2), "Texture loading worker threads (VaM default: 2). More = faster texture streaming.");
		cfgLogSlowLoads = ((BaseUnityPlugin)this).Config.Bind<bool>("Logging", "LogSlowLoads", true, "Log asset loads that exceed SlowLoadThresholdMs.");
		cfgSlowLoadThresholdMs = ((BaseUnityPlugin)this).Config.Bind<float>("Logging", "SlowLoadThresholdMs", 500f, "Threshold in ms above which a load is considered slow.");
		cfgLogStatsOnStart = ((BaseUnityPlugin)this).Config.Bind<bool>("Stats", "LogStatsOnStart", true, "Print current AssetLoader configuration on startup.");
		cfgPeriodicStats = ((BaseUnityPlugin)this).Config.Bind<bool>("Stats", "PeriodicStats", false, "Print texture cache stats periodically.");
		cfgPeriodicStatsInterval = ((BaseUnityPlugin)this).Config.Bind<float>("Stats", "PeriodicStatsIntervalSeconds", 60f, "How often to print stats when PeriodicStats is enabled.");
		cfgHotkeyStats = ((BaseUnityPlugin)this).Config.Bind<KeyCode>("Hotkeys", "PrintStats", (KeyCode)292, "Print asset loader stats to log.");
		Harmony h = new Harmony("com.zerot.assetloaderhelper");
		PatchAssetLoader(h);
		PatchImageLoader(h);
		PatchBundleManager(h);
		((MonoBehaviour)this).StartCoroutine(ResolveAndApplyDelayed());
	}

	private IEnumerator ResolveAndApplyDelayed()
	{
		yield return (object)new WaitForSeconds(1f);
		ResolveTypes();
		ApplyWorkerCounts();
		if (cfgLogStatsOnStart.Value)
		{
			LogStats();
		}
	}

	private void ResolveTypes()
	{
		_assetLoaderType = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
		_imageLoaderType = Type.GetType("ImageLoaderThreaded, Assembly-CSharp");
		if (_assetLoaderType != null)
		{
			FieldInfo field = _assetLoaderType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
			if (field != null)
			{
				_assetLoaderSingleton = field.GetValue(null);
			}
			_fiMaxAbLoads = _assetLoaderType.GetField("MAX_PARALLEL_AB_LOADS", BindingFlags.Instance | BindingFlags.Public);
			_fiMaxSceneLoads = _assetLoaderType.GetField("MAX_PARALLEL_SCENE_LOADS", BindingFlags.Instance | BindingFlags.Public);
			_fiActiveWorkers = _assetLoaderType.GetField("_activeWorkerCount", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiAbQueue = _assetLoaderType.GetField("assetBundleFromFileQueue", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiSceneQueue = _assetLoaderType.GetField("sceneLoadIntoTransformQueue", BindingFlags.Instance | BindingFlags.NonPublic);
		}
		if (_imageLoaderType != null)
		{
			FieldInfo field2 = _imageLoaderType.GetField("singleton", BindingFlags.Static | BindingFlags.Public);
			if (field2 != null)
			{
				_imageLoaderSingleton = field2.GetValue(null);
			}
			_fiWorkerCount = _imageLoaderType.GetField("WORKER_COUNT", BindingFlags.Instance | BindingFlags.Public);
			_fiStatDispatched = _imageLoaderType.GetField("_statTotalDispatched", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiStatMemHit = _imageLoaderType.GetField("_statMemoryCacheHit", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiStatDiskHit = _imageLoaderType.GetField("_statDiskCacheHit", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiStatNew = _imageLoaderType.GetField("_statNewLoad", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiStatErrors = _imageLoaderType.GetField("_statErrors", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiTextureCache = _imageLoaderType.GetField("textureCache", BindingFlags.Instance | BindingFlags.NonPublic);
			_fiThumbnailCache = _imageLoaderType.GetField("thumbnailCache", BindingFlags.Instance | BindingFlags.NonPublic);
		}
		Log.LogInfo((object)("[AssetLoaderHelper] Types resolved  AssetLoader=" + ((_assetLoaderSingleton != null) ? "OK" : "MISSING") + "  ImageLoader=" + ((_imageLoaderSingleton != null) ? "OK" : "MISSING")));
	}

	private void PatchAssetLoader(Harmony h)
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Expected O, but got Unknown
		Type type = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
		if (type == null)
		{
			Log.LogWarning((object)"MeshVR.AssetLoader not found");
			return;
		}
		MethodInfo method = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			Log.LogWarning((object)"AssetLoader.Awake not found");
			return;
		}
		h.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Postfix_AssetLoaderAwake", BindingFlags.Static | BindingFlags.Public)), (HarmonyMethod)null, (HarmonyMethod)null);
		Log.LogDebug((object)"Patched MeshVR.AssetLoader.Awake");
	}

	private void PatchImageLoader(Harmony h)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		Type type = Type.GetType("ImageLoaderThreaded, Assembly-CSharp");
		if (type == null)
		{
			Log.LogWarning((object)"ImageLoaderThreaded not found");
			return;
		}
		MethodInfo method = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method != null)
		{
			h.Patch((MethodBase)method, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Prefix_ImageLoaderAwake", BindingFlags.Static | BindingFlags.Public)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		}
		Log.LogDebug((object)"Patched ImageLoaderThreaded.Awake");
	}

	private void PatchBundleManager(Harmony h)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		Type type = Type.GetType("MeshVR.AssetLoader, Assembly-CSharp");
		if (!(type == null))
		{
			MethodInfo method = type.GetMethod("LoadBundleFileAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Log.LogDebug((object)"LoadBundleFileAsync not found -- skipping timing patch");
				return;
			}
			h.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(typeof(AssetLoaderHelper).GetMethod("Postfix_BundleLoaded", BindingFlags.Static | BindingFlags.Public)), (HarmonyMethod)null, (HarmonyMethod)null);
			Log.LogDebug((object)"Patched MeshVR.AssetLoader.LoadBundleFileAsync");
		}
	}

	public static void Postfix_AssetLoaderAwake(object __instance)
	{
		if ((Object)(object)Instance == (Object)null)
		{
			return;
		}
		try
		{
			FieldInfo field = __instance.GetType().GetField("MAX_PARALLEL_AB_LOADS", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field2 = __instance.GetType().GetField("MAX_PARALLEL_SCENE_LOADS", BindingFlags.Instance | BindingFlags.Public);
			if (field != null)
			{
				field.SetValue(__instance, Instance.cfgMaxParallelBundles.Value);
			}
			if (field2 != null)
			{
				field2.SetValue(__instance, Instance.cfgMaxParallelScenes.Value);
			}
			Log.LogInfo((object)("[AssetLoaderHelper] AssetLoader limits set  AB=" + Instance.cfgMaxParallelBundles.Value + "  Scene=" + Instance.cfgMaxParallelScenes.Value));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("Postfix_AssetLoaderAwake: " + ex.Message));
		}
	}

	public static void Prefix_ImageLoaderAwake(object __instance)
	{
		if ((Object)(object)Instance == (Object)null)
		{
			return;
		}
		try
		{
			FieldInfo field = __instance.GetType().GetField("WORKER_COUNT", BindingFlags.Instance | BindingFlags.Public);
			if (field != null)
			{
				field.SetValue(__instance, Instance.cfgTextureWorkerCount.Value);
				Log.LogInfo((object)("[AssetLoaderHelper] ImageLoaderThreaded WORKER_COUNT set to " + Instance.cfgTextureWorkerCount.Value));
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("Prefix_ImageLoaderAwake: " + ex.Message));
		}
	}

	public static void Postfix_BundleLoaded(object __instance, object[] __args)
	{
		if ((Object)(object)Instance == (Object)null || !Instance.cfgLogSlowLoads.Value || __args == null || __args.Length == 0)
		{
			return;
		}
		try
		{
			object obj = __args[0];
			if (obj == null)
			{
				return;
			}
			FieldInfo field = obj.GetType().GetField("startTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo fieldInfo = obj.GetType().GetField("path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? obj.GetType().GetField("uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				float num = (float)field.GetValue(obj);
				float num2 = (Time.realtimeSinceStartup - num) * 1000f;
				string text = ((fieldInfo != null) ? ((string)fieldInfo.GetValue(obj)) : "?");
				if (num2 > Instance.cfgSlowLoadThresholdMs.Value)
				{
					Log.LogWarning((object)("[AssetLoaderHelper] SLOW BUNDLE " + Math.Round(num2) + "ms  " + text));
				}
				else
				{
					Log.LogDebug((object)("[AssetLoaderHelper] bundle " + Math.Round(num2) + "ms  " + text));
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyWorkerCounts()
	{
		if (_assetLoaderSingleton != null)
		{
			if (_fiMaxAbLoads != null)
			{
				_fiMaxAbLoads.SetValue(_assetLoaderSingleton, cfgMaxParallelBundles.Value);
			}
			if (_fiMaxSceneLoads != null)
			{
				_fiMaxSceneLoads.SetValue(_assetLoaderSingleton, cfgMaxParallelScenes.Value);
			}
		}
	}

	private void Update()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		if (Input.GetKeyDown(cfgHotkeyStats.Value))
		{
			LogStats();
		}
		if (cfgPeriodicStats.Value && Time.realtimeSinceStartup > _nextStatTime)
		{
			_nextStatTime = Time.realtimeSinceStartup + cfgPeriodicStatsInterval.Value;
			LogStats();
		}
	}

	private void LogStats()
	{
		Log.LogInfo((object)"[AssetLoaderHelper] ===== ASSET LOADER STATS =====");
		if (_assetLoaderSingleton != null)
		{
			int num = ((_fiMaxAbLoads != null) ? ((int)_fiMaxAbLoads.GetValue(_assetLoaderSingleton)) : (-1));
			int num2 = ((_fiMaxSceneLoads != null) ? ((int)_fiMaxSceneLoads.GetValue(_assetLoaderSingleton)) : (-1));
			int num3 = ((_fiActiveWorkers != null) ? ((int)_fiActiveWorkers.GetValue(_assetLoaderSingleton)) : (-1));
			int num4 = -1;
			int num5 = -1;
			if (_fiAbQueue != null)
			{
				object value = _fiAbQueue.GetValue(_assetLoaderSingleton);
				if (value != null)
				{
					PropertyInfo property = value.GetType().GetProperty("Count");
					if (property != null)
					{
						num4 = (int)property.GetValue(value, null);
					}
				}
			}
			if (_fiSceneQueue != null)
			{
				object value2 = _fiSceneQueue.GetValue(_assetLoaderSingleton);
				if (value2 != null)
				{
					PropertyInfo property2 = value2.GetType().GetProperty("Count");
					if (property2 != null)
					{
						num5 = (int)property2.GetValue(value2, null);
					}
				}
			}
			Log.LogInfo((object)"  [AssetLoader]");
			Log.LogInfo((object)("    MAX_PARALLEL_AB_LOADS    : " + num));
			Log.LogInfo((object)("    MAX_PARALLEL_SCENE_LOADS : " + num2));
			Log.LogInfo((object)("    Active workers           : " + num3));
			Log.LogInfo((object)("    Bundle queue depth       : " + num4));
			Log.LogInfo((object)("    Scene queue depth        : " + num5));
		}
		else
		{
			Log.LogInfo((object)"  [AssetLoader] singleton not found");
		}
		if (_imageLoaderSingleton != null)
		{
			int num6 = ((_fiWorkerCount != null) ? ((int)_fiWorkerCount.GetValue(_imageLoaderSingleton)) : (-1));
			int num7 = ((_fiStatDispatched != null) ? ((int)_fiStatDispatched.GetValue(_imageLoaderSingleton)) : (-1));
			int num8 = ((_fiStatMemHit != null) ? ((int)_fiStatMemHit.GetValue(_imageLoaderSingleton)) : (-1));
			int num9 = ((_fiStatDiskHit != null) ? ((int)_fiStatDiskHit.GetValue(_imageLoaderSingleton)) : (-1));
			int num10 = ((_fiStatNew != null) ? ((int)_fiStatNew.GetValue(_imageLoaderSingleton)) : (-1));
			int num11 = ((_fiStatErrors != null) ? ((int)_fiStatErrors.GetValue(_imageLoaderSingleton)) : (-1));
			int num12 = -1;
			int num13 = -1;
			if (_fiTextureCache != null)
			{
				object value3 = _fiTextureCache.GetValue(_imageLoaderSingleton);
				if (value3 != null)
				{
					PropertyInfo property3 = value3.GetType().GetProperty("Count");
					if (property3 != null)
					{
						num12 = (int)property3.GetValue(value3, null);
					}
				}
			}
			if (_fiThumbnailCache != null)
			{
				object value4 = _fiThumbnailCache.GetValue(_imageLoaderSingleton);
				if (value4 != null)
				{
					PropertyInfo property4 = value4.GetType().GetProperty("Count");
					if (property4 != null)
					{
						num13 = (int)property4.GetValue(value4, null);
					}
				}
			}
			float num14 = ((num7 > 0) ? ((float)Math.Round((float)(num8 + num9) * 100f / (float)num7, 1)) : 0f);
			Log.LogInfo((object)"  [ImageLoader]");
			Log.LogInfo((object)("    Worker threads    : " + num6));
			Log.LogInfo((object)("    Total dispatched  : " + num7));
			Log.LogInfo((object)("    Memory cache hits : " + num8));
			Log.LogInfo((object)("    Disk cache hits   : " + num9));
			Log.LogInfo((object)("    New loads         : " + num10));
			Log.LogInfo((object)("    Errors            : " + num11));
			Log.LogInfo((object)("    Cache hit rate    : " + num14 + "%"));
			Log.LogInfo((object)("    Texture cache     : " + num12 + " entries"));
			Log.LogInfo((object)("    Thumbnail cache   : " + num13 + " entries"));
		}
		else
		{
			Log.LogInfo((object)"  [ImageLoader] singleton not found");
		}
		Log.LogInfo((object)"[AssetLoaderHelper] ================================");
	}
}
