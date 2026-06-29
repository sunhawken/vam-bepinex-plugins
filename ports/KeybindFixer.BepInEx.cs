using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

// KeybindFixer v5.0 — BepInEx edition
// Fixes [unloaded] keybindings by calling Keybindings.AcquireAllAvailableBroadcastingPlugins
// then reloading saved mappings from disk. Settings live in BepInEx/config/com.zerot.keybindfixer.cfg
// Runs automatically at startup; no session plugin required.

namespace ZeroT
{
    [BepInPlugin("com.zerot.keybindfixer", "KeybindFixer", "5.0.0")]
    public class KeybindFixerPlugin : BaseUnityPlugin
    {
        ConfigEntry<bool> cfgAutoFix;
        ConfigEntry<float> cfgDelay;
        ConfigEntry<float> cfgRepeat;
        ConfigEntry<float> cfgAutoReload;

        float nextRepeat;
        float nextAutoReload;
        bool initialDone;

        new ManualLogSource Logger => base.Logger;

        void Awake()
        {
            cfgAutoFix = Config.Bind("General", "AutoFixOnLoad", true,
                "Automatically rewire keybindings after VaM finishes loading.");

            cfgDelay = Config.Bind("General", "InitialDelaySec", 8f,
                new ConfigDescription("Seconds to wait after startup before first rewire.", new AcceptableValueRange<float>(1f, 30f)));

            cfgRepeat = Config.Bind("General", "RewireEverySec", 60f,
                new ConfigDescription("Repeat soft rewire on this interval (0 = disabled).", new AcceptableValueRange<float>(0f, 300f)));

            cfgAutoReload = Config.Bind("General", "FullReloadEverySec", 0f,
                new ConfigDescription("Fully reload the Keybindings plugin on this interval (0 = disabled).", new AcceptableValueRange<float>(0f, 600f)));

            if (cfgAutoFix.Value)
                StartCoroutine(RewireCoroutine(cfgDelay.Value));

            Logger.LogInfo("KeybindFixer BepInEx plugin loaded.");
        }

        void Update()
        {
            if (!initialDone) return;

            if (cfgRepeat.Value > 0f && Time.time >= nextRepeat)
            {
                nextRepeat = Time.time + cfgRepeat.Value;
                DoRewire();
            }

            if (cfgAutoReload.Value > 0f && Time.time >= nextAutoReload)
            {
                nextAutoReload = Time.time + cfgAutoReload.Value;
                StartCoroutine(FullReloadCoroutine());
            }
        }

        IEnumerator RewireCoroutine(float delay)
        {
            Logger.LogInfo($"Waiting {delay:F1}s before rewiring keybindings...");
            yield return new WaitForSeconds(delay);

            int waits = 0;
            while (SuperController.singleton != null && SuperController.singleton.isLoading && waits < 30)
            {
                Logger.LogInfo("Scene still loading, waiting...");
                yield return new WaitForSeconds(1f);
                waits++;
            }

            yield return null;
            yield return null;

            DoRewire();
            initialDone = true;
            nextRepeat = Time.time + cfgRepeat.Value;
            nextAutoReload = Time.time + cfgAutoReload.Value;
        }

        IEnumerator FullReloadCoroutine()
        {
            Logger.LogInfo("Full reloading Keybindings plugin...");

            MVRScript kbPlugin = FindKeybindingsPlugin();
            if (kbPlugin == null)
            {
                Logger.LogWarning("Keybindings plugin not found for full reload.");
                yield break;
            }

            MethodInfo reloadMethod = kbPlugin.GetType().GetMethod("ReloadPlugin",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            if (reloadMethod != null)
            {
                try
                {
                    reloadMethod.Invoke(kbPlugin, null);
                    Logger.LogInfo("Called ReloadPlugin on Keybindings.");
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"ReloadPlugin failed: {e.Message}");
                    TogglePlugin(kbPlugin);
                }
            }
            else
            {
                TogglePlugin(kbPlugin);
            }

            yield return new WaitForSeconds(3f);
            Logger.LogInfo("Re-wiring after full reload...");
            DoRewire();
            initialDone = true;
            nextRepeat = Time.time + cfgRepeat.Value;
            nextAutoReload = Time.time + cfgAutoReload.Value;
        }

        void TogglePlugin(MVRScript plugin)
        {
            try
            {
                plugin.enabled = false;
                plugin.enabled = true;
                Logger.LogInfo("Toggled Keybindings enabled to force reinit.");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Toggle failed: {e.Message}");
            }
        }

        MVRScript FindKeybindingsPlugin()
        {
            Atom core = SuperController.singleton?.GetAtomByUid("CoreControl");
            if (core != null)
            {
                foreach (MVRScript s in core.gameObject.GetComponentsInChildren<MVRScript>(true))
                    if (s.GetType().Name == "Keybindings") return s;

                foreach (string id in core.GetStorableIDs())
                {
                    if (id.EndsWith("_Keybindings") || id.EndsWith("Keybindings"))
                    {
                        if (core.GetStorableByID(id) is MVRScript ms) return ms;
                    }
                }
            }

            if (SuperController.singleton == null) return null;
            foreach (Atom atom in SuperController.singleton.GetAtoms())
                foreach (MVRScript s in atom.gameObject.GetComponentsInChildren<MVRScript>(true))
                    if (s.GetType().Name == "Keybindings") return s;

            return null;
        }

        void DoRewire()
        {
            try
            {
                Atom core = SuperController.singleton?.GetAtomByUid("CoreControl");
                if (core == null) { Logger.LogWarning("CoreControl atom not found."); return; }

                MVRScript kbPlugin = FindKeybindingsPlugin();
                if (kbPlugin == null)
                {
                    Logger.LogWarning("Keybindings plugin not found. Plugins on CoreControl:");
                    foreach (string id in core.GetStorableIDs())
                        if (id.StartsWith("plugin#")) Logger.LogInfo("  " + id);
                    return;
                }

                Logger.LogInfo($"Found Keybindings: {kbPlugin.storeId} ({kbPlugin.GetType().Name})");
                Type kbType = kbPlugin.GetType();

                MethodInfo acquire = kbType.GetMethod("AcquireAllAvailableBroadcastingPlugins",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (acquire != null)
                {
                    acquire.Invoke(kbPlugin, null);
                    Logger.LogInfo("Called AcquireAllAvailableBroadcastingPlugins.");
                }
                else
                {
                    Logger.LogInfo("AcquireAll not found, trying OnActionsProviderAvailable fallback.");
                    MethodInfo onProvider = kbType.GetMethod("OnActionsProviderAvailable",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (onProvider != null)
                    {
                        int count = 0;
                        foreach (string id in core.GetStorableIDs())
                        {
                            if (!id.StartsWith("plugin#") || id.Contains("KeybindFixer")) continue;
                            JSONStorable storable = core.GetStorableByID(id);
                            if (storable == null) continue;
                            try { onProvider.Invoke(kbPlugin, new object[] { storable }); count++; } catch { }
                        }
                        Logger.LogInfo($"Registered {count} plugins via OnActionsProviderAvailable.");
                    }
                    else
                    {
                        Logger.LogWarning("OnActionsProviderAvailable not found.");
                    }
                }

                FieldInfo storageField = kbType.GetField("_storage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (storageField != null)
                {
                    object storage = storageField.GetValue(kbPlugin);
                    if (storage != null)
                    {
                        MethodInfo importDefaults = storage.GetType().GetMethod("ImportDefaults",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (importDefaults != null)
                        {
                            importDefaults.Invoke(storage, null);
                            Logger.LogInfo("Called ImportDefaults (reloaded .keybindings from disk).");
                        }
                        else
                        {
                            Logger.LogWarning("ImportDefaults not found on _storage.");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("_storage field not found on Keybindings.");
                }

                Logger.LogInfo($"Rewire done at {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception e)
            {
                Logger.LogError($"DoRewire error: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
