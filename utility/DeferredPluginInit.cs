using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[BepInPlugin("com.vam.deferredplugininit", "VaM Deferred Plugin Init", "1.0.0")]
public class DeferredPluginInit : BaseUnityPlugin
{
    static readonly Queue<Action> _queue = new Queue<Action>();
    static bool _sceneReady;
    static bool _bypassing;
    static DeferredPluginInit _inst;

    void Awake()
    {
        Logger.LogWarning("[DeferredPluginInit] Awake start");
        try
        {
            _inst = this;

            var cfgEnabled = Config.Bind("General", "Enabled", true,
                "Queue MVRScript.Init() during scene loading and flush after");

            Logger.LogInfo("[DeferredPluginInit] Config bound, enabled=" + cfgEnabled.Value);

            if (!cfgEnabled.Value)
            {
                Logger.LogInfo("[DeferredPluginInit] Disabled by config.");
                return;
            }

            PatchHarmony();
            StartCoroutine(FlushAfterLoad());
        }
        catch (Exception ex)
        {
            Logger.LogError("[DeferredPluginInit] Awake exception: " + ex);
        }
    }

    void PatchHarmony()
    {
        try
        {
            var harmony = new Harmony("com.vam.deferredplugininit");

            // Use string lookup to avoid any compile-time type resolution issues
            Type mvr = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                mvr = asm.GetType("MVRScript");
                if (mvr != null) break;
            }

            if (mvr == null)
            {
                Logger.LogWarning("[DeferredPluginInit] MVRScript type not found — deferral inactive.");
                return;
            }

            MethodInfo target = mvr.GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (target == null)
            {
                Logger.LogWarning("[DeferredPluginInit] MVRScript.Init method not found — deferral inactive.");
                return;
            }

            var prefix = new HarmonyMethod(typeof(DeferredPluginInit).GetMethod("PrefixInit",
                BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(target, prefix: prefix);
            Logger.LogInfo("[DeferredPluginInit] MVRScript.Init patched successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogError("[DeferredPluginInit] Harmony patch failed: " + ex.Message);
        }
    }

    static bool PrefixInit(object __instance)
    {
        if (_sceneReady || _bypassing) return true;
        if (SuperController.singleton == null || !SuperController.singleton.isLoading) return true;

        var inst = __instance as MonoBehaviour;
        if (inst == null) return true;

        _queue.Enqueue(() =>
        {
            if (inst == null) return;
            _bypassing = true;
            try
            {
                inst.GetType().GetMethod("Init",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(inst, null);
            }
            catch (Exception ex)
            {
                _inst?.Logger.LogError("[DeferredPluginInit] Deferred init error: " + ex.Message);
            }
            finally { _bypassing = false; }
        });
        return false;
    }

    IEnumerator FlushAfterLoad()
    {
        while (SuperController.singleton == null) yield return null;
        while (SuperController.singleton.isLoading) yield return null;

        _sceneReady = true;
        int count = _queue.Count;
        if (count > 0)
            Logger.LogInfo(string.Format("[DeferredPluginInit] Flushing {0} deferred inits.", count));

        while (_queue.Count > 0)
        {
            _queue.Dequeue()?.Invoke();
            yield return null;
        }

        if (count > 0)
            Logger.LogInfo("[DeferredPluginInit] All deferred inits complete.");
    }
}
