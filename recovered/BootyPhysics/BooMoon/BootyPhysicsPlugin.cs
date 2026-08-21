using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using SimpleJSON;
using UnityEngine;

namespace BooMoon;

[BepInPlugin("com.boomoon.bootyphysics", "BootyPhysics", "2.0.0")]
public class BootyPhysicsPlugin : BaseUnityPlugin
{
	private ConfigEntry<string> cfgPreset;

	private ConfigEntry<string> cfgPersonUID;

	private ConfigEntry<bool> cfgAutoApply;

	private ConfigEntry<float> cfgDelaySec;

	private void Awake()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Expected O, but got Unknown
		cfgPreset = ((BaseUnityPlugin)this).Config.Bind<string>("General", "Preset", "Normal", new ConfigDescription("Which BooMoon Booty Physics preset to apply (Small = firmer, Big = softer/jiggly).", (AcceptableValueBase)(object)new AcceptableValueList<string>(new string[3] { "Small", "Normal", "Big" }), new object[0]));
		cfgPersonUID = ((BaseUnityPlugin)this).Config.Bind<string>("General", "PersonUID", "All", "UID of the Person atom to apply to. \"All\" = every Person in the scene, \"Auto\" = first Person with GluteControl.");
		cfgAutoApply = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "AutoApplyOnSceneLoad", true, "Apply preset automatically after each scene finishes loading.");
		cfgDelaySec = ((BaseUnityPlugin)this).Config.Bind<float>("General", "DelaySeconds", 1f, new ConfigDescription("Seconds to wait after scene load before applying (give atoms time to initialise).", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 10f), new object[0]));
		((MonoBehaviour)this).StartCoroutine(StartupCoroutine());
		Logger.LogInfo((object)"BootyPhysics loaded.");
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
		if (cfgAutoApply.Value)
		{
			ApplyToTargets();
		}
	}

	private void OnSceneLoaded()
	{
		if (cfgAutoApply.Value)
		{
			((MonoBehaviour)this).StartCoroutine(DelayedApply());
		}
	}

	private IEnumerator DelayedApply()
	{
		float value = cfgDelaySec.Value;
		if (value > 0f)
		{
			yield return (object)new WaitForSeconds(value);
		}
		ApplyToTargets();
	}

	private void ApplyToTargets()
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		string text = cfgPersonUID.Value ?? "All";
		string text2 = cfgPreset.Value ?? "Normal";
		int num = 0;
		foreach (Atom atom in SuperController.singleton.GetAtoms())
		{
			if (!((Object)(object)atom == (Object)null) && !(atom.type != "Person") && (text == "All" || text == atom.uid || (text == "Auto" && num == 0)) && ApplyPreset(atom, text2))
			{
				num++;
				Logger.LogInfo((object)("BootyPhysics: Applied '" + text2 + "' to '" + atom.uid + "'."));
				if (text == "Auto")
				{
					break;
				}
			}
		}
		if (num == 0)
		{
			Logger.LogWarning((object)"BootyPhysics: No matching Person atoms with GluteControl found.");
		}
		else
		{
			Logger.LogInfo((object)("BootyPhysics: Done — " + num + " atom(s) updated."));
		}
	}

	private bool ApplyPreset(Atom person, string preset)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Expected O, but got Unknown
		try
		{
			JSONStorable storableByID = person.GetStorableByID("GluteControl");
			JSONStorable storableByID2 = person.GetStorableByID("LowerPhysicsMesh");
			if ((Object)(object)storableByID == (Object)null || (Object)(object)storableByID2 == (Object)null)
			{
				return false;
			}
			JSONClass val = new JSONClass();
			((JSONNode)val)["mass"] = "0.75";
			((JSONNode)val)["centerOfGravityPercent"] = "0.5";
			((JSONNode)val)["spring"] = "35.95115";
			((JSONNode)val)["damper"] = "0.2250392";
			((JSONNode)val)["positionSpringX"] = "0";
			((JSONNode)val)["positionSpringY"] = "0";
			((JSONNode)val)["positionSpringZ"] = "140";
			((JSONNode)val)["positionDamperX"] = "0";
			((JSONNode)val)["positionDamperY"] = "0";
			((JSONNode)val)["positionDamperZ"] = "1";
			((JSONNode)val)["targetRotationX"] = "-10";
			((JSONNode)val)["targetRotationY"] = "0";
			((JSONNode)val)["targetRotationZ"] = "0";
			string text;
			string text2;
			if (!(preset == "Big"))
			{
				if (preset == "Small")
				{
					text = "400";
					text2 = "0.0251076";
				}
				else
				{
					text = "300";
					text2 = "0.02505973";
				}
			}
			else
			{
				text = "200";
				text2 = "0.02536305";
			}
			JSONClass val2 = new JSONClass();
			((JSONNode)val2)["on"] = "true";
			((JSONNode)val2)["allowSelfCollision"] = "false";
			((JSONNode)val2)["softVerticesUseAutoColliderRadius"] = "false";
			((JSONNode)val2)["softVerticesCombinedSpring"] = text;
			((JSONNode)val2)["softVerticesCombinedDamper"] = "0.2";
			((JSONNode)val2)["softVerticesMass"] = "0.1995402";
			((JSONNode)val2)["softVerticesBackForce"] = "25.2032";
			((JSONNode)val2)["softVerticesBackForceThresholdDistance"] = "0.002";
			((JSONNode)val2)["softVerticesBackForceMaxForce"] = "50";
			((JSONNode)val2)["softVerticesColliderRadius"] = text2;
			((JSONNode)val2)["softVerticesColliderAdditionalNormalOffset"] = "-0.004";
			((JSONNode)val2)["softVerticesDistanceLimit"] = "0.1";
			storableByID.RestoreFromJSON(val, true, true, (JSONArray)null, false);
			storableByID2.RestoreFromJSON(val2, true, true, (JSONArray)null, false);
			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("BootyPhysics: Error on '" + person.uid + "': " + ex));
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
