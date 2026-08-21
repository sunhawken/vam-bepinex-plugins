using System;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

[BepInPlugin("com.zerot.fastload", "FastLoad", "1.2.0")]
public class FastLoad : BaseUnityPlugin
{
	private ConfigEntry<int> cfgUploadTimeSlice;

	private ConfigEntry<int> cfgUploadBufferSize;

	private ConfigEntry<bool> cfgPrewarmThreadPool;

	private static ManualLogSource Log;

	private void Awake()
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		Log = Logger;
		cfgUploadTimeSlice = ((BaseUnityPlugin)this).Config.Bind<int>("Async Upload", "TimeSliceMsPerFrame", 16, new ConfigDescription("ms per frame Unity may spend uploading textures/meshes to GPU (default 2, range 1-33)", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 33), new object[0]));
		cfgUploadBufferSize = ((BaseUnityPlugin)this).Config.Bind<int>("Async Upload", "BufferSizeMB", 64, new ConfigDescription("RAM buffer for async GPU uploads in MB (default 4, range 4-512)", (AcceptableValueBase)(object)new AcceptableValueRange<int>(4, 512), new object[0]));
		cfgPrewarmThreadPool = ((BaseUnityPlugin)this).Config.Bind<bool>("Loading", "PrewarmThreadPool", true, "Spin up .NET thread-pool threads on startup so they are ready when VaM needs them.");
		QualitySettings.asyncUploadTimeSlice = cfgUploadTimeSlice.Value;
		QualitySettings.asyncUploadBufferSize = cfgUploadBufferSize.Value;
		if (cfgPrewarmThreadPool.Value)
		{
			PrewarmThreadPool();
		}
		Log.LogInfo((object)("FastLoad v1.2.0  uploadSlice=" + cfgUploadTimeSlice.Value + "ms  buf=" + cfgUploadBufferSize.Value + "MB"));
	}

	private void PrewarmThreadPool()
	{
		ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
		int num = Math.Max(workerThreads, Environment.ProcessorCount);
		ThreadPool.SetMinThreads(num, completionPortThreads);
		Log.LogDebug((object)("Thread-pool min workers set to " + num));
	}
}
