using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ZeroT;

[BepInPlugin("com.zerot.keybindfixer", "KeybindFixer", "5.0.0")]
public class KeybindFixerPlugin : BaseUnityPlugin
{
	private ConfigEntry<bool> cfgAutoFix;

	private ConfigEntry<float> cfgDelay;

	private ConfigEntry<float> cfgRepeat;

	private ConfigEntry<float> cfgAutoReload;

	private float nextRepeat;

	private float nextAutoReload;

	private bool initialDone;

	private ManualLogSource Logger => Logger;

	private void Awake()
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected O, but got Unknown
		cfgAutoFix = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "AutoFixOnLoad", true, "Automatically rewire keybindings after VaM finishes loading.");
		cfgDelay = ((BaseUnityPlugin)this).Config.Bind<float>("General", "InitialDelaySec", 8f, new ConfigDescription("Seconds to wait after startup before first rewire.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(1f, 30f), Array.Empty<object>()));
		cfgRepeat = ((BaseUnityPlugin)this).Config.Bind<float>("General", "RewireEverySec", 60f, new ConfigDescription("Repeat soft rewire on this interval (0 = disabled).", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 300f), Array.Empty<object>()));
		cfgAutoReload = ((BaseUnityPlugin)this).Config.Bind<float>("General", "FullReloadEverySec", 0f, new ConfigDescription("Fully reload the Keybindings plugin on this interval (0 = disabled).", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 600f), Array.Empty<object>()));
		if (cfgAutoFix.Value)
		{
			((MonoBehaviour)this).StartCoroutine(RewireCoroutine(cfgDelay.Value));
		}
		Logger.LogInfo((object)"KeybindFixer BepInEx plugin loaded.");
	}

	private void Update()
	{
		if (initialDone)
		{
			if (cfgRepeat.Value > 0f && Time.time >= nextRepeat)
			{
				nextRepeat = Time.time + cfgRepeat.Value;
				DoRewire();
			}
			if (cfgAutoReload.Value > 0f && Time.time >= nextAutoReload)
			{
				nextAutoReload = Time.time + cfgAutoReload.Value;
				((MonoBehaviour)this).StartCoroutine(FullReloadCoroutine());
			}
		}
	}

	private IEnumerator RewireCoroutine(float delay)
	{
		Logger.LogInfo((object)$"Waiting {delay:F1}s before rewiring keybindings...");
		yield return (object)new WaitForSeconds(delay);
		int waits = 0;
		while ((Object)(object)SuperController.singleton != (Object)null && SuperController.singleton.isLoading && waits < 30)
		{
			Logger.LogInfo((object)"Scene still loading, waiting...");
			yield return (object)new WaitForSeconds(1f);
			waits++;
		}
		yield return null;
		yield return null;
		DoRewire();
		initialDone = true;
		nextRepeat = Time.time + cfgRepeat.Value;
		nextAutoReload = Time.time + cfgAutoReload.Value;
	}

	private IEnumerator FullReloadCoroutine()
	{
		Logger.LogInfo((object)"Full reloading Keybindings plugin...");
		MVRScript val = FindKeybindingsPlugin();
		if ((Object)(object)val == (Object)null)
		{
			Logger.LogWarning((object)"Keybindings plugin not found for full reload.");
			yield break;
		}
		MethodInfo method = ((object)val).GetType().GetMethod("ReloadPlugin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method != null)
		{
			try
			{
				method.Invoke(val, null);
				Logger.LogInfo((object)"Called ReloadPlugin on Keybindings.");
			}
			catch (Exception ex)
			{
				Logger.LogWarning((object)("ReloadPlugin failed: " + ex.Message));
				TogglePlugin(val);
			}
		}
		else
		{
			TogglePlugin(val);
		}
		yield return (object)new WaitForSeconds(3f);
		Logger.LogInfo((object)"Re-wiring after full reload...");
		DoRewire();
		initialDone = true;
		nextRepeat = Time.time + cfgRepeat.Value;
		nextAutoReload = Time.time + cfgAutoReload.Value;
	}

	private void TogglePlugin(MVRScript plugin)
	{
		try
		{
			((Behaviour)plugin).enabled = false;
			((Behaviour)plugin).enabled = true;
			Logger.LogInfo((object)"Toggled Keybindings enabled to force reinit.");
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Toggle failed: " + ex.Message));
		}
	}

	private MVRScript FindKeybindingsPlugin()
	{
		SuperController singleton = SuperController.singleton;
		Atom val = ((singleton != null) ? singleton.GetAtomByUid("CoreControl") : null);
		if ((Object)(object)val != (Object)null)
		{
			MVRScript[] componentsInChildren = ((Component)val).gameObject.GetComponentsInChildren<MVRScript>(true);
			foreach (MVRScript val2 in componentsInChildren)
			{
				if (((object)val2).GetType().Name == "Keybindings")
				{
					return val2;
				}
			}
			foreach (string storableID in val.GetStorableIDs())
			{
				if (storableID.EndsWith("_Keybindings") || storableID.EndsWith("Keybindings"))
				{
					JSONStorable storableByID = val.GetStorableByID(storableID);
					MVRScript val3 = (MVRScript)(object)((storableByID is MVRScript) ? storableByID : null);
					if (val3 != null)
					{
						return val3;
					}
				}
			}
		}
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return null;
		}
		foreach (Atom atom in SuperController.singleton.GetAtoms())
		{
			MVRScript[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRScript>(true);
			foreach (MVRScript val4 in componentsInChildren)
			{
				if (((object)val4).GetType().Name == "Keybindings")
				{
					return val4;
				}
			}
		}
		return null;
	}

	private void DoRewire()
	{
		try
		{
			SuperController singleton = SuperController.singleton;
			Atom val = ((singleton != null) ? singleton.GetAtomByUid("CoreControl") : null);
			if ((Object)(object)val == (Object)null)
			{
				Logger.LogWarning((object)"CoreControl atom not found.");
				return;
			}
			MVRScript val2 = FindKeybindingsPlugin();
			if ((Object)(object)val2 == (Object)null)
			{
				Logger.LogWarning((object)"Keybindings plugin not found. Plugins on CoreControl:");
				{
					foreach (string storableID in val.GetStorableIDs())
					{
						if (storableID.StartsWith("plugin#"))
						{
							Logger.LogInfo((object)("  " + storableID));
						}
					}
					return;
				}
			}
			Logger.LogInfo((object)("Found Keybindings: " + ((JSONStorable)val2).storeId + " (" + ((object)val2).GetType().Name + ")"));
			Type type = ((object)val2).GetType();
			MethodInfo method = type.GetMethod("AcquireAllAvailableBroadcastingPlugins", BindingFlags.Instance | BindingFlags.NonPublic);
			if (method != null)
			{
				method.Invoke(val2, null);
				Logger.LogInfo((object)"Called AcquireAllAvailableBroadcastingPlugins.");
			}
			else
			{
				Logger.LogInfo((object)"AcquireAll not found, trying OnActionsProviderAvailable fallback.");
				MethodInfo method2 = type.GetMethod("OnActionsProviderAvailable", BindingFlags.Instance | BindingFlags.Public);
				if (method2 != null)
				{
					int num = 0;
					foreach (string storableID2 in val.GetStorableIDs())
					{
						if (!storableID2.StartsWith("plugin#") || storableID2.Contains("KeybindFixer"))
						{
							continue;
						}
						JSONStorable storableByID = val.GetStorableByID(storableID2);
						if (!((Object)(object)storableByID == (Object)null))
						{
							try
							{
								method2.Invoke(val2, new object[1] { storableByID });
								num++;
							}
							catch
							{
							}
						}
					}
					Logger.LogInfo((object)$"Registered {num} plugins via OnActionsProviderAvailable.");
				}
				else
				{
					Logger.LogWarning((object)"OnActionsProviderAvailable not found.");
				}
			}
			FieldInfo field = type.GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null)
			{
				object value = field.GetValue(val2);
				if (value != null)
				{
					MethodInfo method3 = value.GetType().GetMethod("ImportDefaults", BindingFlags.Instance | BindingFlags.Public);
					if (method3 != null)
					{
						method3.Invoke(value, null);
						Logger.LogInfo((object)"Called ImportDefaults (reloaded .keybindings from disk).");
					}
					else
					{
						Logger.LogWarning((object)"ImportDefaults not found on _storage.");
					}
				}
			}
			else
			{
				Logger.LogWarning((object)"_storage field not found on Keybindings.");
			}
			Logger.LogInfo((object)$"Rewire done at {DateTime.Now:HH:mm:ss}");
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("DoRewire error: " + ex.Message + "\n" + ex.StackTrace));
		}
	}
}
