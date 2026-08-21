using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Saking55;

[BepInPlugin("com.saking55.autobulger", "AutoBulger", "7.0.0")]
public class AutoBulgerPlugin : BaseUnityPlugin
{
	private ConfigEntry<string> cfgPersonUID;

	private Atom _person;

	private ConfigEntry<bool> cfgBellyEnabled;

	private ConfigEntry<bool> cfgBellyClamp;

	private ConfigEntry<bool> cfgBellyExtreme;

	private ConfigEntry<bool> cfgBellyDebug;

	private ConfigEntry<float> cfgBellyDebugDepth;

	private ConfigEntry<float> cfgBellyMult;

	private ConfigEntry<float> cfgBellyMinDistMult;

	private ConfigEntry<float> cfgBellySmoothing;

	private ConfigEntry<float> cfgBellyOfsX;

	private ConfigEntry<float> cfgBellyOfsY;

	private ConfigEntry<float> cfgBellyOfsZ;

	private ConfigEntry<bool> cfgBellyFilterActive;

	private ConfigEntry<string> cfgBellyFilter;

	private ConfigEntry<float> cfgB1;

	private ConfigEntry<float> cfgB2;

	private ConfigEntry<float> cfgB3;

	private ConfigEntry<float> cfgB4;

	private ConfigEntry<float> cfgB5;

	private ConfigEntry<float> cfgB6;

	private ConfigEntry<float> cfgB7;

	private ConfigEntry<float> cfgB8;

	private FreeControllerV3 _bellyChest;

	private DAZMorph _b1;

	private DAZMorph _b2;

	private DAZMorph _b3;

	private DAZMorph _b4;

	private DAZMorph _b5;

	private DAZMorph _b6;

	private DAZMorph _b7;

	private DAZMorph _b8;

	private CollisionTrigger _bTrigVag;

	private CollisionTrigger _bTrigVagD;

	private CollisionTrigger _bTrigVagDD;

	private CollisionTriggerEventHandler _bEvt1;

	private CollisionTriggerEventHandler _bEvt2;

	private CollisionTriggerEventHandler _bEvt3;

	private Dictionary<Collider, bool> _bCol = new Dictionary<Collider, bool>();

	private long _bLastCol;

	private float _bFloat = 1f;

	private float _bVel;

	private Collider _bTemp;

	private Collider _bDistTarget;

	private bool _bNotReset = true;

	private bool _bLoaded = true;

	private bool _bPrevExtreme;

	private ConfigEntry<bool> cfgThroatEnabled;

	private ConfigEntry<bool> cfgThroatClamp;

	private ConfigEntry<bool> cfgThroatDebug;

	private ConfigEntry<float> cfgThroatDebugDepth;

	private ConfigEntry<float> cfgThroatMult;

	private ConfigEntry<float> cfgThroatMinDistMult;

	private ConfigEntry<float> cfgThroatSmoothing;

	private ConfigEntry<float> cfgThroatOfsX;

	private ConfigEntry<float> cfgThroatOfsY;

	private ConfigEntry<float> cfgThroatOfsZ;

	private ConfigEntry<bool> cfgThroatFilterActive;

	private ConfigEntry<string> cfgThroatFilter;

	private ConfigEntry<float> cfgT1;

	private ConfigEntry<float> cfgT2;

	private ConfigEntry<float> cfgT3;

	private ConfigEntry<float> cfgT4;

	private FreeControllerV3 _throatNeck;

	private DAZMorph _t1;

	private DAZMorph _t2;

	private DAZMorph _t3;

	private DAZMorph _t4;

	private CollisionTrigger _tTrigMouth;

	private CollisionTrigger _tTrigThroat;

	private CollisionTriggerEventHandler _tEvt1;

	private CollisionTriggerEventHandler _tEvt2;

	private Dictionary<Collider, bool> _tCol = new Dictionary<Collider, bool>();

	private long _tLastCol;

	private float _tFloat = 1f;

	private float _tVel;

	private Collider _tTemp;

	private Collider _tDistTarget;

	private bool _tNotReset = true;

	private bool _tLoaded = true;

	private ConfigEntry<bool> cfgGenEnabled;

	private ConfigEntry<bool> cfgGenClamp;

	private ConfigEntry<bool> cfgGenAnal;

	private ConfigEntry<bool> cfgGenVag;

	private ConfigEntry<bool> cfgGenDebug;

	private ConfigEntry<float> cfgGenDebugDepth;

	private ConfigEntry<float> cfgGenMult;

	private ConfigEntry<float> cfgGenMinDistMult;

	private ConfigEntry<float> cfgGenSmoothing;

	private ConfigEntry<float> cfgGenOfsX;

	private ConfigEntry<float> cfgGenOfsY;

	private ConfigEntry<float> cfgGenOfsZ;

	private ConfigEntry<bool> cfgGenFilterActive;

	private ConfigEntry<string> cfgGenFilter;

	private ConfigEntry<float> cfgGAnalMult;

	private ConfigEntry<float> cfgGVagMult;

	private ConfigEntry<float> cfgGVagOpen;

	private FreeControllerV3 _genChest;

	private DAZMorph _gAnal1;

	private DAZMorph _gAnal2;

	private DAZMorph _gAnal3;

	private DAZMorph _gVag1;

	private DAZMorph _gVag2;

	private DAZMorph _gVag3;

	private CollisionTrigger _gTrigVag;

	private CollisionTrigger _gTrigVagD;

	private CollisionTrigger _gTrigVagDD;

	private CollisionTriggerEventHandler _gEvt1;

	private CollisionTriggerEventHandler _gEvt2;

	private CollisionTriggerEventHandler _gEvt3;

	private Dictionary<Collider, bool> _gCol = new Dictionary<Collider, bool>();

	private long _gLastCol;

	private float _gFloat = 1f;

	private float _gFloatA = 1f;

	private float _gFloatV = 1f;

	private float _gVel;

	private float _gVelA;

	private float _gVelV;

	private Collider _gTemp;

	private Collider _gDistTarget;

	private bool _gNotReset = true;

	private bool _gLoaded = true;

	private bool _initialized;

	private ManualLogSource Logger => Logger;

	private void Awake()
	{
		BindConfig();
		((MonoBehaviour)this).StartCoroutine(StartupCoroutine());
		Logger.LogInfo((object)"AutoBulger BepInEx plugin loaded.");
	}

	private void BindConfig()
	{
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected O, but got Unknown
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Expected O, but got Unknown
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Expected O, but got Unknown
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Expected O, but got Unknown
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Expected O, but got Unknown
		//IL_0208: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Expected O, but got Unknown
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		//IL_0251: Expected O, but got Unknown
		//IL_02c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ce: Expected O, but got Unknown
		//IL_0303: Unknown result type (might be due to invalid IL or missing references)
		//IL_030d: Expected O, but got Unknown
		//IL_0342: Unknown result type (might be due to invalid IL or missing references)
		//IL_034c: Expected O, but got Unknown
		//IL_0381: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		//IL_03c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ca: Expected O, but got Unknown
		//IL_03ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0409: Expected O, but got Unknown
		//IL_043e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0448: Expected O, but got Unknown
		//IL_047d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0487: Expected O, but got Unknown
		//IL_0513: Unknown result type (might be due to invalid IL or missing references)
		//IL_051d: Expected O, but got Unknown
		//IL_0552: Unknown result type (might be due to invalid IL or missing references)
		//IL_055c: Expected O, but got Unknown
		//IL_0591: Unknown result type (might be due to invalid IL or missing references)
		//IL_059b: Expected O, but got Unknown
		//IL_05d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_05da: Expected O, but got Unknown
		//IL_060f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0619: Expected O, but got Unknown
		//IL_064e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0658: Expected O, but got Unknown
		//IL_068d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0697: Expected O, but got Unknown
		//IL_070a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0714: Expected O, but got Unknown
		//IL_0749: Unknown result type (might be due to invalid IL or missing references)
		//IL_0753: Expected O, but got Unknown
		//IL_0788: Unknown result type (might be due to invalid IL or missing references)
		//IL_0792: Expected O, but got Unknown
		//IL_07c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d1: Expected O, but got Unknown
		//IL_0897: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a1: Expected O, but got Unknown
		//IL_08d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_08e0: Expected O, but got Unknown
		//IL_0915: Unknown result type (might be due to invalid IL or missing references)
		//IL_091f: Expected O, but got Unknown
		//IL_0954: Unknown result type (might be due to invalid IL or missing references)
		//IL_095e: Expected O, but got Unknown
		//IL_0993: Unknown result type (might be due to invalid IL or missing references)
		//IL_099d: Expected O, but got Unknown
		//IL_09d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09dc: Expected O, but got Unknown
		//IL_0a11: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a1b: Expected O, but got Unknown
		//IL_0a8e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a98: Expected O, but got Unknown
		//IL_0acd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ad7: Expected O, but got Unknown
		//IL_0b0c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b16: Expected O, but got Unknown
		cfgPersonUID = ((BaseUnityPlugin)this).Config.Bind<string>("General", "PersonUID", "Auto", "UID of Person atom to apply morphs to. \"Auto\" = first Person found.");
		cfgBellyEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("BellyBulger", "Enabled", true, (ConfigDescription)null);
		cfgBellyClamp = ((BaseUnityPlugin)this).Config.Bind<bool>("BellyBulger", "LimitMorphs", true, (ConfigDescription)null);
		cfgBellyExtreme = ((BaseUnityPlugin)this).Config.Bind<bool>("BellyBulger", "ExtremeMode", false, "Use X-morph set instead of standard.");
		cfgBellyDebug = ((BaseUnityPlugin)this).Config.Bind<bool>("BellyBulger", "ManualDepth", false, (ConfigDescription)null);
		cfgBellyDebugDepth = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "ManualDepthValue", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgBellyMult = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "BellyBulgeMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 3f), new object[0]));
		cfgBellyMinDistMult = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "MinBulgeDistMult", 5f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 5f), new object[0]));
		cfgBellySmoothing = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "MorphSmoothing", 0.02f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgBellyOfsX = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "TargetOffsetX", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgBellyOfsY = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "TargetOffsetY", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgBellyOfsZ = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "TargetOffsetZ", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgBellyFilterActive = ((BaseUnityPlugin)this).Config.Bind<bool>("BellyBulger", "FilterAtomName", false, (ConfigDescription)null);
		cfgBellyFilter = ((BaseUnityPlugin)this).Config.Bind<string>("BellyBulger", "FilterString", "Person", (ConfigDescription)null);
		cfgB1 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge1Mult", 0.32f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB2 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge2Mult", 0.67f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB3 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge3Mult", 0.9f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB4 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge4Mult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB5 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge5Mult", 0.86f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB6 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge6Mult", 0.7f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB7 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge7Mult", 0.39f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgB8 = ((BaseUnityPlugin)this).Config.Bind<float>("BellyBulger", "Bulge8Mult", 0.18f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgThroatEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("ThroatBulger", "Enabled", true, (ConfigDescription)null);
		cfgThroatClamp = ((BaseUnityPlugin)this).Config.Bind<bool>("ThroatBulger", "LimitMorphs", true, (ConfigDescription)null);
		cfgThroatDebug = ((BaseUnityPlugin)this).Config.Bind<bool>("ThroatBulger", "ManualDepth", false, (ConfigDescription)null);
		cfgThroatDebugDepth = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "ManualDepthValue", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgThroatMult = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "ThroatBulgeMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgThroatMinDistMult = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "MinBulgeDistMult", 2f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 5f), new object[0]));
		cfgThroatSmoothing = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "MorphSmoothing", 0.1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgThroatOfsX = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "TargetOffsetX", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgThroatOfsY = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "TargetOffsetY", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgThroatOfsZ = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "TargetOffsetZ", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgThroatFilterActive = ((BaseUnityPlugin)this).Config.Bind<bool>("ThroatBulger", "FilterAtomName", false, (ConfigDescription)null);
		cfgThroatFilter = ((BaseUnityPlugin)this).Config.Bind<string>("ThroatBulger", "FilterString", "Person", (ConfigDescription)null);
		cfgT1 = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "Bulge1Mult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgT2 = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "Bulge2Mult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgT3 = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "Bulge3Mult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgT4 = ((BaseUnityPlugin)this).Config.Bind<float>("ThroatBulger", "Bulge4Mult", 0.5f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgGenEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "Enabled", true, (ConfigDescription)null);
		cfgGenClamp = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "LimitMorphs", true, (ConfigDescription)null);
		cfgGenAnal = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "AnalExpEnabled", false, (ConfigDescription)null);
		cfgGenVag = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "VaginalExpEnabled", true, (ConfigDescription)null);
		cfgGenDebug = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "ManualDepth", false, (ConfigDescription)null);
		cfgGenDebugDepth = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "ManualDepthValue", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgGenMult = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "GenExpMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 3f), new object[0]));
		cfgGenMinDistMult = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "MinExpDistMult", 5f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 5f), new object[0]));
		cfgGenSmoothing = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "MorphSmoothing", 0.05f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), new object[0]));
		cfgGenOfsX = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "TargetOffsetX", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgGenOfsY = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "TargetOffsetY", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgGenOfsZ = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "TargetOffsetZ", 0f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-1f, 1f), new object[0]));
		cfgGenFilterActive = ((BaseUnityPlugin)this).Config.Bind<bool>("GenExpansion", "FilterAtomName", false, (ConfigDescription)null);
		cfgGenFilter = ((BaseUnityPlugin)this).Config.Bind<string>("GenExpansion", "FilterString", "Person", (ConfigDescription)null);
		cfgGAnalMult = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "AnalMorphMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 3f), new object[0]));
		cfgGVagMult = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "VaginalMorphMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 3f), new object[0]));
		cfgGVagOpen = ((BaseUnityPlugin)this).Config.Bind<float>("GenExpansion", "VagOpenMult", 1f, new ConfigDescription("", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 3f), new object[0]));
		cfgBellyExtreme.SettingChanged += delegate
		{
			_bNotReset = true;
			_bPrevExtreme = !cfgBellyExtreme.Value;
		};
		cfgPersonUID.SettingChanged += delegate
		{
			if (_initialized)
			{
				((MonoBehaviour)this).StartCoroutine(SelectPersonCoroutine(ResolvePersonUID()));
			}
		};
	}

	private IEnumerator StartupCoroutine()
	{
		while ((Object)(object)SuperController.singleton == (Object)null)
		{
			yield return null;
		}
		while (SuperController.singleton.isLoading)
		{
			yield return (object)new WaitForSeconds(0.5f);
		}
		yield return null;
		SuperController singleton = SuperController.singleton;
		singleton.onSceneLoadedHandlers += OnSceneLoaded;
		_initialized = true;
		yield return SelectPersonCoroutine(ResolvePersonUID());
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
		ResetAll();
	}

	private void OnSceneLoaded()
	{
		((MonoBehaviour)this).StartCoroutine(SelectPersonCoroutine(ResolvePersonUID()));
	}

	private string ResolvePersonUID()
	{
		if (cfgPersonUID.Value == "Auto" || string.IsNullOrEmpty(cfgPersonUID.Value))
		{
			List<Atom> list = SuperController.singleton.GetAtoms().FindAll(delegate(Atom a)
			{
				//IL_002f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0035: Invalid comparison between Unknown and I4
				if (a.type != "Person")
				{
					return false;
				}
				JSONStorable storableByID = a.GetStorableByID("geometry");
				DAZCharacterSelector val = (DAZCharacterSelector)(object)((storableByID is DAZCharacterSelector) ? storableByID : null);
				return (Object)(object)val != (Object)null && (int)val.gender == 2;
			});
			if (list.Count <= 0)
			{
				return "";
			}
			return list[0].uid;
		}
		return cfgPersonUID.Value;
	}

	private IEnumerator SelectPersonCoroutine(string uid)
	{
		ResetAll();
		_person = null;
		yield return null;
		yield return null;
		if (!string.IsNullOrEmpty(uid))
		{
			_person = SuperController.singleton.GetAtomByUid(uid);
			if ((Object)(object)_person == (Object)null)
			{
				Logger.LogWarning((object)("AutoBulger: Person '" + uid + "' not found."));
				yield break;
			}
			_bCol.Clear();
			_bNotReset = true;
			_bLoaded = true;
			_bFloat = 1f;
			_bTemp = null;
			_bDistTarget = null;
			_bPrevExtreme = !cfgBellyExtreme.Value;
			_tCol.Clear();
			_tNotReset = true;
			_tLoaded = true;
			_tFloat = 1f;
			_tTemp = null;
			_tDistTarget = null;
			_gCol.Clear();
			_gNotReset = true;
			_gLoaded = true;
			_gFloat = (_gFloatA = (_gFloatV = 1f));
			_gTemp = null;
			_gDistTarget = null;
			SetupBelly();
			SetupThroat();
			SetupGen();
			Logger.LogInfo((object)("AutoBulger: Attached to '" + uid + "'."));
		}
	}

	private void SetupBelly()
	{
		try
		{
			JSONStorable storableByID = _person.GetStorableByID("VaginaTrigger");
			_bTrigVag = (CollisionTrigger)(object)((storableByID is CollisionTrigger) ? storableByID : null);
			JSONStorable storableByID2 = _person.GetStorableByID("DeepVaginaTrigger");
			_bTrigVagD = (CollisionTrigger)(object)((storableByID2 is CollisionTrigger) ? storableByID2 : null);
			JSONStorable storableByID3 = _person.GetStorableByID("DeeperVaginaTrigger");
			_bTrigVagDD = (CollisionTrigger)(object)((storableByID3 is CollisionTrigger) ? storableByID3 : null);
			EnableTrigger(_bTrigVag, out _bEvt1);
			EnableTrigger(_bTrigVagD, out _bEvt2);
			EnableTrigger(_bTrigVagDD, out _bEvt3);
			JSONStorable storableByID4 = _person.GetStorableByID("chestControl");
			_bellyChest = (FreeControllerV3)(object)((storableByID4 is FreeControllerV3) ? storableByID4 : null);
			LoadBellyMorphs();
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("AutoBulger SetupBelly: " + ex));
		}
	}

	private void LoadBellyMorphs()
	{
		if ((Object)(object)_person == (Object)null)
		{
			return;
		}
		GenerateDAZMorphsControlUI val = MorphUI(_person);
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		string text = (cfgBellyExtreme.Value ? "AutoBulger Belly X " : "AutoBulger Belly ");
		try
		{
			ResetBellyMorphs();
			_b1 = val.GetMorphByDisplayName(text + "1");
			_b2 = val.GetMorphByDisplayName(text + "2");
			_b3 = val.GetMorphByDisplayName(text + "3");
			_b4 = val.GetMorphByDisplayName(text + "4");
			_b5 = val.GetMorphByDisplayName(text + "5");
			_b6 = val.GetMorphByDisplayName(text + "6");
			_b7 = val.GetMorphByDisplayName(text + "7");
			_b8 = val.GetMorphByDisplayName(text + "8");
			_b1.Reset();
			_b2.Reset();
			_b3.Reset();
			_b4.Reset();
			_b5.Reset();
			_b6.Reset();
			_b7.Reset();
			_b8.Reset();
			_bLoaded = true;
			_bPrevExtreme = cfgBellyExtreme.Value;
		}
		catch (Exception)
		{
			Logger.LogError((object)"AutoBulger: Belly morphs not found — belly disabled.");
			_bLoaded = false;
		}
	}

	private void SetupThroat()
	{
		try
		{
			JSONStorable storableByID = _person.GetStorableByID("MouthTrigger");
			_tTrigMouth = (CollisionTrigger)(object)((storableByID is CollisionTrigger) ? storableByID : null);
			JSONStorable storableByID2 = _person.GetStorableByID("ThroatTrigger");
			_tTrigThroat = (CollisionTrigger)(object)((storableByID2 is CollisionTrigger) ? storableByID2 : null);
			EnableTrigger(_tTrigMouth, out _tEvt1);
			EnableTrigger(_tTrigThroat, out _tEvt2);
			JSONStorable storableByID3 = _person.GetStorableByID("neckControl");
			_throatNeck = (FreeControllerV3)(object)((storableByID3 is FreeControllerV3) ? storableByID3 : null);
			GenerateDAZMorphsControlUI val = MorphUI(_person);
			if ((Object)(object)val == (Object)null)
			{
				return;
			}
			try
			{
				_t1 = val.GetMorphByDisplayName("AutoBulger Throat 1");
				_t1.Reset();
				_t2 = val.GetMorphByDisplayName("AutoBulger Throat 2");
				_t2.Reset();
				_t3 = val.GetMorphByDisplayName("AutoBulger Throat 3");
				_t3.Reset();
				_t4 = val.GetMorphByDisplayName("AutoBulger Throat 4");
				_t4.Reset();
				_tLoaded = true;
			}
			catch (Exception)
			{
				Logger.LogError((object)"AutoBulger: Throat morphs not found — throat disabled.");
				_tLoaded = false;
			}
		}
		catch (Exception ex2)
		{
			Logger.LogError((object)("AutoBulger SetupThroat: " + ex2));
		}
	}

	private void SetupGen()
	{
		try
		{
			JSONStorable storableByID = _person.GetStorableByID("VaginaTrigger");
			_gTrigVag = (CollisionTrigger)(object)((storableByID is CollisionTrigger) ? storableByID : null);
			JSONStorable storableByID2 = _person.GetStorableByID("DeepVaginaTrigger");
			_gTrigVagD = (CollisionTrigger)(object)((storableByID2 is CollisionTrigger) ? storableByID2 : null);
			JSONStorable storableByID3 = _person.GetStorableByID("DeeperVaginaTrigger");
			_gTrigVagDD = (CollisionTrigger)(object)((storableByID3 is CollisionTrigger) ? storableByID3 : null);
			EnableTrigger(_gTrigVag, out _gEvt1);
			EnableTrigger(_gTrigVagD, out _gEvt2);
			EnableTrigger(_gTrigVagDD, out _gEvt3);
			JSONStorable storableByID4 = _person.GetStorableByID("chestControl");
			_genChest = (FreeControllerV3)(object)((storableByID4 is FreeControllerV3) ? storableByID4 : null);
			GenerateDAZMorphsControlUI val = GenMorphUI(_person);
			GenerateDAZMorphsControlUI val2 = MorphUI(_person);
			if ((Object)(object)val == (Object)null && (Object)(object)val2 == (Object)null)
			{
				return;
			}
			try
			{
				GenerateDAZMorphsControlUI val3 = val ?? val2;
				GenerateDAZMorphsControlUI val4 = val2 ?? val;
				_gAnal1 = val3.GetMorphByDisplayName("AutoGenExp Anal 1");
				_gAnal1.Reset();
				_gAnal2 = val3.GetMorphByDisplayName("AutoGenExp Anal 2");
				_gAnal2.Reset();
				_gAnal3 = val3.GetMorphByDisplayName("AutoGenExp Anal 3");
				_gAnal3.Reset();
				_gVag1 = val3.GetMorphByDisplayName("AutoGenExp Vag 1");
				_gVag1.Reset();
				_gVag2 = val3.GetMorphByDisplayName("AutoGenExp Vag 2");
				_gVag2.Reset();
				_gVag3 = val4.GetMorphByDisplayName("AutoGenExp Vag 3");
				_gVag3.Reset();
				_gLoaded = true;
			}
			catch (Exception)
			{
				Logger.LogError((object)"AutoBulger: Gen expansion morphs not found — gen disabled.");
				_gLoaded = false;
			}
		}
		catch (Exception ex2)
		{
			Logger.LogError((object)("AutoBulger SetupGen: " + ex2));
		}
	}

	private static void EnableTrigger(CollisionTrigger t, out CollisionTriggerEventHandler evt)
	{
		evt = null;
		if (!((Object)(object)t == (Object)null))
		{
			t.triggerEnabledJSON.val = true;
			evt = ((Component)t).GetComponent<CollisionTriggerEventHandler>();
		}
	}

	private static GenerateDAZMorphsControlUI MorphUI(Atom person)
	{
		JSONStorable storableByID = person.GetStorableByID("geometry");
		JSONStorable obj = ((storableByID is DAZCharacterSelector) ? storableByID : null);
		if (obj == null)
		{
			return null;
		}
		return ((DAZCharacterSelector)obj).morphsControlUI;
	}

	private static GenerateDAZMorphsControlUI GenMorphUI(Atom person)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		JSONStorable storableByID = person.GetStorableByID("morphsControlFemaleGenitalia");
		if ((Object)(object)storableByID != (Object)null)
		{
			try
			{
				return (GenerateDAZMorphsControlUI)storableByID;
			}
			catch
			{
			}
		}
		return MorphUI(person);
	}

	private void FixedUpdate()
	{
		if (_initialized && !((Object)(object)_person == (Object)null))
		{
			if (cfgBellyEnabled.Value && _bLoaded)
			{
				FixedUpdateBelly();
			}
			else
			{
				BellyResetOnce();
			}
			if (cfgThroatEnabled.Value && _tLoaded)
			{
				FixedUpdateThroat();
			}
			else
			{
				ThroatResetOnce();
			}
			if (cfgGenEnabled.Value && _gLoaded)
			{
				FixedUpdateGen();
			}
			else
			{
				GenResetOnce();
			}
		}
	}

	private void FixedUpdateBelly()
	{
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		if (cfgBellyExtreme.Value != _bPrevExtreme)
		{
			_bNotReset = true;
			LoadBellyMorphs();
			return;
		}
		try
		{
			UpdateCollisions(_bTrigVag, _bTrigVagD, _bTrigVagDD, _bEvt1, _bEvt2, _bEvt3, _bCol, ref _bLastCol, 2000L);
			string text = "None";
			float num = 10f;
			foreach (KeyValuePair<Collider, bool> item in _bCol.Reverse())
			{
				Atom pairAtom = GetPairAtom(item.Key);
				if (!((Object)(object)pairAtom == (Object)null) && !(((Object)pairAtom).name == ((Object)_person).name) && PassesFilter(((Object)pairAtom).name, cfgBellyFilterActive.Value, cfgBellyFilter.Value))
				{
					float num2 = Vector3.Distance(_bellyChest.followWhenOff.position, item.Key.attachedRigidbody.position);
					if (num2 < num)
					{
						_bTemp = item.Key;
						num = num2;
						text = ((Object)pairAtom).name;
					}
				}
			}
			_bDistTarget = _bTemp;
			if (text == "None")
			{
				num = 100f;
			}
			else
			{
				Vector3 val = new Vector3(cfgBellyOfsX.Value, cfgBellyOfsY.Value, cfgBellyOfsZ.Value);
				float num3 = Vector3.Distance(_bellyChest.followWhenOff.position, _bDistTarget.attachedRigidbody.position + _bDistTarget.attachedRigidbody.rotation * val);
				num = Mathf.Min(Vector3.Distance(_bellyChest.followWhenOff.position, _bTemp.attachedRigidbody.position + _bTemp.attachedRigidbody.rotation * val), num3);
			}
			_bNotReset = true;
			_bFloat = ((text == "None") ? Mathf.SmoothDamp(_bFloat, 1f, ref _bVel, cfgBellySmoothing.Value) : Mathf.SmoothDamp(_bFloat, Remapp(num, 0.23f, 0.43f, 0f, 0.7f), ref _bVel, cfgBellySmoothing.Value));
			if (cfgBellyDebug.Value)
			{
				_bFloat = cfgBellyDebugDepth.Value;
			}
			BCalc(_b1, _bFloat, 0.15f, cfgB1.Value);
			BCalc(_b2, _bFloat, 0.13f, cfgB2.Value);
			BCalc(_b3, _bFloat, 0.11f, cfgB3.Value);
			BCalc(_b4, _bFloat, 0.09f, cfgB4.Value);
			BCalc(_b5, _bFloat, 0.07f, cfgB5.Value);
			BCalc(_b6, _bFloat, 0.05f, cfgB6.Value);
			BCalc(_b7, _bFloat, 0.03f, cfgB7.Value);
			BCalc(_b8, _bFloat, 0.025f, cfgB8.Value);
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("AutoBulger Belly: " + ex));
		}
	}

	private void BCalc(DAZMorph m, float d, float dist0, float bMult)
	{
		if (m != null)
		{
			m.morphValue = Mathf.Max((1f - d / (dist0 * cfgBellyMinDistMult.Value)) * cfgBellyMult.Value * bMult, 0f);
			if (cfgBellyClamp.Value)
			{
				m.morphValue = Mathf.Clamp(m.morphValue, 0f, 1f);
			}
		}
	}

	private void ResetBellyMorphs()
	{
		if (_b1 != null)
		{
			_b1.Reset();
		}
		if (_b2 != null)
		{
			_b2.Reset();
		}
		if (_b3 != null)
		{
			_b3.Reset();
		}
		if (_b4 != null)
		{
			_b4.Reset();
		}
		if (_b5 != null)
		{
			_b5.Reset();
		}
		if (_b6 != null)
		{
			_b6.Reset();
		}
		if (_b7 != null)
		{
			_b7.Reset();
		}
		if (_b8 != null)
		{
			_b8.Reset();
		}
	}

	private void BellyResetOnce()
	{
		if (_bNotReset)
		{
			ResetBellyMorphs();
			_bNotReset = false;
		}
	}

	private void FixedUpdateThroat()
	{
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			UpdateCollisions(_tTrigMouth, _tTrigThroat, null, _tEvt1, _tEvt2, null, _tCol, ref _tLastCol, 500L);
			string text = "None";
			float num = 10f;
			foreach (KeyValuePair<Collider, bool> item in _tCol)
			{
				Atom pairAtom = GetPairAtom(item.Key);
				if (!((Object)(object)pairAtom == (Object)null) && !(((Object)pairAtom).name == ((Object)_person).name) && PassesFilter(((Object)pairAtom).name, cfgThroatFilterActive.Value, cfgThroatFilter.Value))
				{
					float num2 = Vector3.Distance(_throatNeck.followWhenOff.position, item.Key.attachedRigidbody.position);
					if (num2 < num)
					{
						_tTemp = item.Key;
						num = num2;
						text = ((Object)pairAtom).name;
					}
				}
			}
			if (text == "None")
			{
				_tDistTarget = null;
				num = 10f;
			}
			if ((Object)(object)_tDistTarget == (Object)null)
			{
				_tDistTarget = _tTemp;
			}
			else if ((Object)(object)_tTemp != (Object)null)
			{
				Vector3 val = new Vector3(cfgThroatOfsX.Value, cfgThroatOfsY.Value, cfgThroatOfsZ.Value);
				float num3 = Vector3.Distance(_throatNeck.followWhenOff.position, _tDistTarget.attachedRigidbody.position + _tDistTarget.attachedRigidbody.rotation * val);
				float num4 = Vector3.Distance(_throatNeck.followWhenOff.position, _tTemp.attachedRigidbody.position + _tTemp.attachedRigidbody.rotation * val);
				num = ((num4 < num3) ? num4 : num3);
			}
			_tNotReset = true;
			_tFloat = ((text == "None") ? Mathf.SmoothDamp(_tFloat, 1f, ref _tVel, cfgThroatSmoothing.Value) : Mathf.SmoothDamp(_tFloat, Remapp(num, 0.07f, 0.19f, 0f, 0.8f), ref _tVel, cfgThroatSmoothing.Value));
			if (cfgThroatDebug.Value)
			{
				_tFloat = cfgThroatDebugDepth.Value;
			}
			TCalc(_t1, _tFloat, 0.4f, cfgT1.Value);
			TCalc(_t2, _tFloat, 0.2f, cfgT2.Value);
			TCalc(_t3, _tFloat, 0.15f, cfgT3.Value);
			TCalc(_t4, _tFloat, 0.4f, cfgT4.Value);
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("AutoBulger Throat: " + ex));
		}
	}

	private void TCalc(DAZMorph m, float d, float dist0, float bMult)
	{
		if (m != null)
		{
			m.morphValue = Mathf.Max((1f - d / (dist0 * cfgThroatMinDistMult.Value)) * cfgThroatMult.Value * bMult, 0f);
			if (cfgThroatClamp.Value)
			{
				m.morphValue = Mathf.Clamp(m.morphValue, 0f, 1f);
			}
		}
	}

	private void ThroatResetOnce()
	{
		if (_tNotReset)
		{
			if (_t1 != null)
			{
				_t1.Reset();
			}
			if (_t2 != null)
			{
				_t2.Reset();
			}
			if (_t3 != null)
			{
				_t3.Reset();
			}
			if (_t4 != null)
			{
				_t4.Reset();
			}
			_tNotReset = false;
		}
	}

	private void FixedUpdateGen()
	{
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0205: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			UpdateCollisions(_gTrigVag, _gTrigVagD, _gTrigVagDD, _gEvt1, _gEvt2, _gEvt3, _gCol, ref _gLastCol, 2000L);
			string text = "None";
			float num = 10f;
			foreach (KeyValuePair<Collider, bool> item in _gCol)
			{
				Atom pairAtom = GetPairAtom(item.Key);
				if (!((Object)(object)pairAtom == (Object)null) && !(((Object)pairAtom).name == ((Object)_person).name) && PassesFilter(((Object)pairAtom).name, cfgGenFilterActive.Value, cfgGenFilter.Value))
				{
					float num2 = Vector3.Distance(_genChest.followWhenOff.position, item.Key.attachedRigidbody.position);
					if (num2 < num)
					{
						_gTemp = item.Key;
						num = num2;
						text = ((Object)pairAtom).name;
					}
				}
			}
			if (text == "None")
			{
				_gDistTarget = null;
				num = 10f;
			}
			if ((Object)(object)_gDistTarget == (Object)null)
			{
				_gDistTarget = _gTemp;
			}
			else if ((Object)(object)_gTemp != (Object)null)
			{
				Vector3 val = new Vector3(cfgGenOfsX.Value, cfgGenOfsY.Value, cfgGenOfsZ.Value);
				float num3 = Vector3.Distance(_genChest.followWhenOff.position, _gDistTarget.attachedRigidbody.position + _gDistTarget.attachedRigidbody.rotation * val);
				float num4 = Vector3.Distance(_genChest.followWhenOff.position, _gTemp.attachedRigidbody.position + _gTemp.attachedRigidbody.rotation * val);
				num = ((num4 < num3) ? num4 : num3);
			}
			_gNotReset = true;
			if (text == "None")
			{
				_gFloatV = Mathf.SmoothDamp(_gFloatV, 1f, ref _gVelV, cfgGenSmoothing.Value);
				_gFloatA = Mathf.SmoothDamp(_gFloatA, 1f, ref _gVelA, cfgGenSmoothing.Value);
				_gFloat = Mathf.SmoothDamp(_gFloat, 1f, ref _gVel, cfgGenSmoothing.Value);
			}
			else
			{
				float num5 = Remapp(num, 0.2f, 0.35f, 0f, 0.7f);
				_gFloatA = Mathf.SmoothDamp(_gFloatA, cfgGenAnal.Value ? num5 : 1f, ref _gVelA, cfgGenSmoothing.Value);
				_gFloatV = Mathf.SmoothDamp(_gFloatV, cfgGenVag.Value ? num5 : 1f, ref _gVelV, cfgGenSmoothing.Value);
				_gFloat = Mathf.SmoothDamp(_gFloat, num5, ref _gVel, cfgGenSmoothing.Value);
			}
			if (cfgGenDebug.Value)
			{
				_gFloat = cfgGenDebugDepth.Value;
				if (cfgGenAnal.Value)
				{
					_gFloatA = cfgGenDebugDepth.Value;
				}
				if (cfgGenVag.Value)
				{
					_gFloatV = cfgGenDebugDepth.Value;
				}
			}
			GCalc(_gAnal3, _gFloatA, 0.2f, cfgGAnalMult.Value, 3f);
			GCalc(_gAnal1, _gFloatA, 0.2f, cfgGAnalMult.Value, 3f);
			GCalc(_gAnal2, _gFloatA, 0.2f, cfgGAnalMult.Value, 0.3f);
			GCalc(_gVag1, _gFloatV, 0.1f, cfgGVagOpen.Value, 0.3f);
			GCalc(_gVag2, _gFloatV, 0.2f, cfgGVagMult.Value);
			GCalc(_gVag3, _gFloatV, 0.2f, cfgGVagMult.Value, 2f);
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("AutoBulger Gen: " + ex));
		}
	}

	private void GCalc(DAZMorph m, float d, float dist0, float bMult, float maxVal = 1f)
	{
		if (m != null)
		{
			m.morphValue = Mathf.Max((1f - d / (dist0 * cfgGenMinDistMult.Value)) * cfgGenMult.Value * bMult, 0f);
			if (cfgGenClamp.Value)
			{
				m.morphValue = Mathf.Clamp(m.morphValue, 0f, maxVal);
			}
		}
	}

	private void GenResetOnce()
	{
		if (_gNotReset)
		{
			if (_gAnal1 != null)
			{
				_gAnal1.Reset();
			}
			if (_gAnal2 != null)
			{
				_gAnal2.Reset();
			}
			if (_gAnal3 != null)
			{
				_gAnal3.Reset();
			}
			if (_gVag1 != null)
			{
				_gVag1.Reset();
			}
			if (_gVag2 != null)
			{
				_gVag2.Reset();
			}
			if (_gVag3 != null)
			{
				_gVag3.Reset();
			}
			_gNotReset = false;
		}
	}

	private void ResetAll()
	{
		_bNotReset = true;
		BellyResetOnce();
		_tNotReset = true;
		ThroatResetOnce();
		_gNotReset = true;
		GenResetOnce();
	}

	private static void UpdateCollisions(CollisionTrigger t1, CollisionTrigger t2, CollisionTrigger t3, CollisionTriggerEventHandler e1, CollisionTriggerEventHandler e2, CollisionTriggerEventHandler e3, Dictionary<Collider, bool> dict, ref long lastUpdate, long timeout)
	{
		long num = DateTime.Now.Ticks / 10000;
		if (((Object)(object)t1 != (Object)null && t1.trigger.active) || ((Object)(object)t2 != (Object)null && t2.trigger.active) || ((Object)(object)t3 != (Object)null && t3.trigger.active))
		{
			IEnumerable<KeyValuePair<Collider, bool>> first = new Dictionary<Collider, bool>();
			if ((Object)(object)e1 != (Object)null)
			{
				first = first.Concat(e1.collidingWithDictionary);
			}
			if ((Object)(object)e2 != (Object)null)
			{
				first = first.Concat(e2.collidingWithDictionary);
			}
			if ((Object)(object)e3 != (Object)null)
			{
				first = first.Concat(e3.collidingWithDictionary);
			}
			Dictionary<Collider, bool> dictionary = (from kv in first.Concat(dict)
				group kv by kv.Key).ToDictionary((IGrouping<Collider, KeyValuePair<Collider, bool>> g) => g.Key, (IGrouping<Collider, KeyValuePair<Collider, bool>> g) => g.First().Value);
			dict.Clear();
			foreach (KeyValuePair<Collider, bool> item in dictionary)
			{
				dict[item.Key] = item.Value;
			}
			lastUpdate = num;
		}
		else if (num > lastUpdate + timeout)
		{
			dict.Clear();
		}
		KeyValuePair<Collider, bool>[] array = dict.Where((KeyValuePair<Collider, bool> f) => (Object)(object)f.Key == (Object)null || (Object)(object)f.Key.attachedRigidbody == (Object)null).ToArray();
		foreach (KeyValuePair<Collider, bool> keyValuePair in array)
		{
			dict.Remove(keyValuePair.Key);
		}
	}

	private static Atom GetPairAtom(Collider col)
	{
		Rigidbody val = ((col != null) ? col.attachedRigidbody : null);
		if (!((Object)(object)val == (Object)null))
		{
			return ((Component)val).GetComponentInParent<Atom>();
		}
		return null;
	}

	private static bool PassesFilter(string name, bool filterActive, string filter)
	{
		if (!filterActive)
		{
			return true;
		}
		return name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static float Remapp(float val, float f1, float t1, float f2, float t2)
	{
		return (val - f1) / (t1 - f1) * (t2 - f2) + f2;
	}
}
