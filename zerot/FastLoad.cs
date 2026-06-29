using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using System;
using System.Threading;

[BepInPlugin("com.zerot.fastload", "FastLoad", "1.2.0")]
public class FastLoad : BaseUnityPlugin
{
    ConfigEntry<int>  cfgUploadTimeSlice;
    ConfigEntry<int>  cfgUploadBufferSize;
    ConfigEntry<bool> cfgPrewarmThreadPool;

    static ManualLogSource Log;

    void Awake()
    {
        Log = Logger;

        cfgUploadTimeSlice  = Config.Bind("Async Upload", "TimeSliceMsPerFrame", 16,
            new ConfigDescription("ms per frame Unity may spend uploading textures/meshes to GPU (default 2, range 1-33)",
                new AcceptableValueRange<int>(1, 33)));
        cfgUploadBufferSize = Config.Bind("Async Upload", "BufferSizeMB", 64,
            new ConfigDescription("RAM buffer for async GPU uploads in MB (default 4, range 4-512)",
                new AcceptableValueRange<int>(4, 512)));
        cfgPrewarmThreadPool = Config.Bind("Loading", "PrewarmThreadPool", true,
            "Spin up .NET thread-pool threads on startup so they are ready when VaM needs them.");

        QualitySettings.asyncUploadTimeSlice  = cfgUploadTimeSlice.Value;
        QualitySettings.asyncUploadBufferSize = cfgUploadBufferSize.Value;

        if (cfgPrewarmThreadPool.Value)
            PrewarmThreadPool();

        Log.LogInfo("FastLoad v1.2.0  uploadSlice=" + cfgUploadTimeSlice.Value
                    + "ms  buf=" + cfgUploadBufferSize.Value + "MB");
    }

    void PrewarmThreadPool()
    {
        int workers, io;
        ThreadPool.GetMinThreads(out workers, out io);
        int target = Math.Max(workers, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(target, io);
        Log.LogDebug("Thread-pool min workers set to " + target);
    }
}
