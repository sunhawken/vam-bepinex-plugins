using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Misiv;

[BepInPlugin("com.misiv.malemoanoff", "MaleMoanOff", "1.0.0")]
public class MaleMoanOffPlugin : BaseUnityPlugin
{
	private ConfigEntry<bool> cfgAutoDisable;

	private ConfigEntry<bool> cfgSilentMode;

	private void Awake()
	{
		cfgAutoDisable = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "AutoDisableOnSceneLoad", true, "Automatically disable VAMMoan on male Person atoms when a scene loads.");
		cfgSilentMode = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "SilentMode", false, "Suppress per-instance log messages during auto-disable (summary still logged).");
		((MonoBehaviour)this).StartCoroutine(StartupCoroutine());
		Logger.LogInfo((object)"MaleMoanOff loaded.");
	}

	private IEnumerator StartupCoroutine()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		while (SuperController.singleton.isLoading)
		{
			yield return null;
		}
		SuperController singleton = SuperController.singleton;
		singleton.onSceneLoadedHandlers += OnSceneLoaded;
		if (cfgAutoDisable.Value)
		{
			DisableVAMMoanOnMales(!cfgSilentMode.Value);
		}
	}

	private void OnSceneLoaded()
	{
		if (cfgAutoDisable.Value)
		{
			((MonoBehaviour)this).StartCoroutine(DelayedDisable());
		}
	}

	private IEnumerator DelayedDisable()
	{
		yield return (object)new WaitForSeconds(1f);
		DisableVAMMoanOnMales(!cfgSilentMode.Value);
	}

	private void DisableVAMMoanOnMales(bool verbose)
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		int num = 0;
		try
		{
			foreach (Atom atom in SuperController.singleton.GetAtoms())
			{
				if ((Object)(object)atom == (Object)null || atom.type != "Person" || !IsMale(atom))
				{
					continue;
				}
				foreach (MVRScript vAMMoanPlugin in GetVAMMoanPlugins(atom))
				{
					try
					{
						if (vAMMoanPlugin.enabledJSON.val)
						{
							vAMMoanPlugin.enabledJSON.val = false;
							num++;
							if (verbose)
							{
								Logger.LogInfo((object)("MaleMoanOff: Disabled '" + ((Object)vAMMoanPlugin).name + "' on '" + atom.uid + "'."));
							}
						}
					}
					catch (Exception ex)
					{
						Logger.LogError((object)("MaleMoanOff: Error disabling plugin on '" + atom.uid + "': " + ex));
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.LogError((object)("MaleMoanOff: " + ex2));
		}
		Logger.LogInfo((object)("MaleMoanOff: Done — " + num + " VAMMoan instance(s) disabled on male atom(s)."));
	}

	private List<MVRScript> GetVAMMoanPlugins(Atom atom)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected O, but got Unknown
		List<MVRScript> list = new List<MVRScript>();
		try
		{
			JSONStorable storableByID = atom.GetStorableByID("PluginManager");
			MVRPluginManager val = (MVRPluginManager)(object)((storableByID is MVRPluginManager) ? storableByID : null);
			if ((Object)(object)val == (Object)null)
			{
				return list;
			}
			Transform val2 = ((Component)val).gameObject.transform.Find("Plugins");
			if ((Object)(object)val2 == (Object)null)
			{
				return list;
			}
			foreach (Transform item in val2)
			{
				Transform val3 = item;
				if (!((Object)(object)val3 == (Object)null))
				{
					MVRScript component = ((Component)val3).gameObject.GetComponent<MVRScript>();
					if (!((Object)(object)component == (Object)null) && (((Object)component).name ?? "").IndexOf("VAMMoan", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						list.Add(component);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("MaleMoanOff: GetVAMMoanPlugins: " + ex));
		}
		return list;
	}

	private bool IsMale(Atom atom)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Invalid comparison between Unknown and I4
		try
		{
			DAZCharacterSelector componentInChildren = ((Component)atom).GetComponentInChildren<DAZCharacterSelector>();
			return (Object)(object)componentInChildren != (Object)null && (int)componentInChildren.gender == 1;
		}
		catch
		{
			return false;
		}
	}

	private void OnDestroy()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController singleton = SuperController.singleton;
			singleton.onSceneLoadedHandlers -= OnSceneLoaded;
		}
	}
}
