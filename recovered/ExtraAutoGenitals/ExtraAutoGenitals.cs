using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

[BepInPlugin("com.lfe.extraautogenitals", "LFE Extra Auto Genitals", "0.3.0")]
public class ExtraAutoGenitals : BaseUnityPlugin
{
	private class MorphCfg
	{
		public string Name;

		public ConfigEntry<bool> Enabled;

		public ConfigEntry<float> Friction;

		public ConfigEntry<float> InwardMax;

		public ConfigEntry<float> OutwardMax;

		public ConfigEntry<float> InwardExaggeration;

		public ConfigEntry<float> OutwardExaggeration;

		public ConfigEntry<bool> Reverse;
	}

	private struct MorphDefault(string n, bool e, bool r, float i, float o)
	{
		public string Name = n;

		public bool Enabled = e;

		public bool Reverse = r;

		public float InwardMax = i;

		public float OutwardMax = o;
	}

	private class RuntimeMorph
	{
		public MorphCfg Cfg;

		public LabiaAnimator Animator;
	}

	private ConfigEntry<string> cfgPersonUID;

	private static readonly MorphDefault[] Defaults = new MorphDefault[6]
	{
		new MorphDefault("Labia minora-size", e: true, r: false, 0.7f, 2f),
		new MorphDefault("Labia minora-style1", e: true, r: false, 0.7f, 2f),
		new MorphDefault("Labia minora-exstrophy", e: true, r: true, 0.1f, 1f),
		new MorphDefault("Labia majora-relaxation", e: true, r: false, 1f, 0f),
		new MorphDefault("Gen_Innie", e: true, r: true, 0.1f, 0.25f),
		new MorphDefault("Gens In - Out", e: false, r: true, 1f, 0f)
	};

	private MorphCfg[] _cfgs;

	private List<RuntimeMorph> _morphs = new List<RuntimeMorph>();

	private CollisionTriggerEventHandler _labiaHandler;

	private FreeControllerV3 _abdomen;

	private float? _prevDist;

	private float? _prevVel;

	private bool _ready;

	private void Awake()
	{
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected O, but got Unknown
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Expected O, but got Unknown
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Expected O, but got Unknown
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Expected O, but got Unknown
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Expected O, but got Unknown
		cfgPersonUID = ((BaseUnityPlugin)this).Config.Bind<string>("General", "PersonUID", "Auto", "UID of the Person atom to drive. 'Auto' = first Person in scene.");
		_cfgs = new MorphCfg[Defaults.Length];
		for (int i = 0; i < Defaults.Length; i++)
		{
			MorphDefault morphDefault = Defaults[i];
			string name = morphDefault.Name;
			bool enabled = morphDefault.Enabled;
			bool reverse = morphDefault.Reverse;
			float inwardMax = morphDefault.InwardMax;
			float outwardMax = morphDefault.OutwardMax;
			string text = name;
			_cfgs[i] = new MorphCfg
			{
				Name = name,
				Enabled = ((BaseUnityPlugin)this).Config.Bind<bool>(text, "Enabled", enabled, "Drive this morph"),
				Friction = ((BaseUnityPlugin)this).Config.Bind<float>(text, "Friction", 1f, new ConfigDescription("Response speed", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), Array.Empty<object>())),
				InwardMax = ((BaseUnityPlugin)this).Config.Bind<float>(text, "InwardMax", inwardMax, new ConfigDescription("Max inward morph value", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-5f, 5f), Array.Empty<object>())),
				OutwardMax = ((BaseUnityPlugin)this).Config.Bind<float>(text, "OutwardMax", outwardMax, new ConfigDescription("Max outward morph value", (AcceptableValueBase)(object)new AcceptableValueRange<float>(-5f, 5f), Array.Empty<object>())),
				InwardExaggeration = ((BaseUnityPlugin)this).Config.Bind<float>(text, "InwardExaggeration", 0f, new ConfigDescription("Exaggeration applied on inward motion", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 5f), Array.Empty<object>())),
				OutwardExaggeration = ((BaseUnityPlugin)this).Config.Bind<float>(text, "OutwardExaggeration", 0f, new ConfigDescription("Exaggeration applied on outward motion", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 5f), Array.Empty<object>())),
				Reverse = ((BaseUnityPlugin)this).Config.Bind<bool>(text, "Reverse", reverse, "Flip inward/outward direction")
			};
		}
		((MonoBehaviour)this).StartCoroutine(StartupCoroutine());
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
		yield return ((MonoBehaviour)this).StartCoroutine(SelectPersonCoroutine());
	}

	private void OnSceneLoaded()
	{
		((MonoBehaviour)this).StartCoroutine(SelectPersonCoroutine());
	}

	private IEnumerator SelectPersonCoroutine()
	{
		_ready = false;
		ResetMorphs();
		_morphs.Clear();
		_labiaHandler = null;
		_abdomen = null;
		_prevDist = null;
		_prevVel = null;
		yield return null;
		yield return null;
		Atom val = FindPerson();
		if ((Object)(object)val == (Object)null)
		{
			Logger.LogWarning((object)"[ExtraAutoGenitals] No Person atom found.");
			yield break;
		}
		CollisionTrigger val2 = ((Component)val).GetComponentsInChildren<CollisionTrigger>().FirstOrDefault((CollisionTrigger t) => ((Object)t).name == "LabiaTrigger");
		if ((Object)(object)val2 == (Object)null)
		{
			Logger.LogWarning((object)("[ExtraAutoGenitals] LabiaTrigger not found on " + val.uid));
			yield break;
		}
		_labiaHandler = ((Component)val2).gameObject.GetComponentInChildren<CollisionTriggerEventHandler>();
		_abdomen = val.freeControllers.FirstOrDefault((FreeControllerV3 fc) => ((Object)fc).name == "abdomen2Control");
		if ((Object)(object)_labiaHandler == (Object)null || (Object)(object)_abdomen == (Object)null)
		{
			Logger.LogWarning((object)"[ExtraAutoGenitals] Missing handler or abdomen2Control.");
			yield break;
		}
		GenerateDAZMorphsControlUI morphsControlUI = ((DAZCharacterSelector)val.GetStorableByID("geometry")).morphsControlUI;
		MorphCfg[] cfgs = _cfgs;
		foreach (MorphCfg morphCfg in cfgs)
		{
			DAZMorph morphByDisplayName = morphsControlUI.GetMorphByDisplayName(morphCfg.Name);
			if (morphByDisplayName == null)
			{
				Logger.LogWarning((object)("[ExtraAutoGenitals] Morph not found: " + morphCfg.Name));
				continue;
			}
			LabiaAnimator animator = new LabiaAnimator(morphByDisplayName, morphCfg.Reverse.Value, morphCfg.InwardMax.Value, morphCfg.OutwardMax.Value, morphCfg.InwardExaggeration.Value, morphCfg.OutwardExaggeration.Value);
			_morphs.Add(new RuntimeMorph
			{
				Cfg = morphCfg,
				Animator = animator
			});
		}
		Logger.LogInfo((object)("[ExtraAutoGenitals] Ready on " + val.uid + " (" + _morphs.Count + " morphs)"));
		_ready = true;
	}

	private Atom FindPerson()
	{
		string value = cfgPersonUID.Value;
		if (!string.IsNullOrEmpty(value) && value != "Auto")
		{
			Atom atomByUid = SuperController.singleton.GetAtomByUid(value);
			if ((Object)(object)atomByUid != (Object)null && atomByUid.type == "Person")
			{
				return atomByUid;
			}
		}
		return SuperController.singleton.GetAtoms().FirstOrDefault((Atom a) => a.type == "Person");
	}

	private void Update()
	{
		if (!_ready || SuperController.singleton.freezeAnimation)
		{
			return;
		}
		try
		{
			List<Collider> list = IEnumerableExtension.ToList<Collider>((IEnumerable<Collider>)_labiaHandler.collidingWithDictionary.Keys);
			float num = ((list.Count > 0) ? list.Min((Collider col) => Vector3.Distance(((Component)col).transform.position, ((Component)_abdomen).transform.position)) : 0f);
			float num2 = (_prevDist.GetValueOrDefault() - num) / Time.deltaTime;
			if (_prevDist.HasValue && _prevVel.HasValue)
			{
				foreach (RuntimeMorph morph in _morphs)
				{
					if (!morph.Cfg.Enabled.Value)
					{
						continue;
					}
					float value = morph.Cfg.Friction.Value;
					if (!(value <= 0f))
					{
						morph.Animator.IsInwardMorph = morph.Cfg.Reverse.Value;
						morph.Animator.InwardMax = morph.Cfg.InwardMax.Value;
						morph.Animator.OutwardMax = morph.Cfg.OutwardMax.Value;
						morph.Animator.InwardExaggeration = morph.Cfg.InwardExaggeration.Value;
						morph.Animator.OutwardExaggeration = morph.Cfg.OutwardExaggeration.Value;
						float? num3 = morph.Animator.NextMorphValue((list.Count > 0) ? new float?(num2) : ((float?)null), value);
						if (num3.HasValue)
						{
							morph.Animator.Morph.morphValueAdjustLimits = num3.Value;
						}
					}
				}
			}
			if (Mathf.Approximately(num2, 0f))
			{
				num2 = _prevVel.GetValueOrDefault();
			}
			_prevDist = num;
			_prevVel = num2;
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("[ExtraAutoGenitals] Update: " + ex.Message));
		}
	}

	private void ResetMorphs()
	{
		foreach (RuntimeMorph morph2 in _morphs)
		{
			LabiaAnimator animator = morph2.Animator;
			if (animator != null)
			{
				DAZMorph morph = animator.Morph;
				if (morph != null)
				{
					morph.SetDefaultValue();
				}
			}
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
		ResetMorphs();
	}
}
