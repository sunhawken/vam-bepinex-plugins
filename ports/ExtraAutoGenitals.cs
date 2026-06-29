using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port of LFE.ExtraAutoGenitals 0.3
// Drives labia/genital morphs based on collision velocity at the LabiaTrigger.
// Config lives in BepInEx/config/com.lfe.extraautogenitals.cfg.
[BepInPlugin("com.lfe.extraautogenitals", "LFE Extra Auto Genitals", "0.3.0")]
public class ExtraAutoGenitals : BaseUnityPlugin
{
    // ── general ──────────────────────────────────────────────────────────────
    ConfigEntry<string> cfgPersonUID;

    // ── per-morph config (6 defaults) ────────────────────────────────────────
    class MorphCfg
    {
        public string Name;
        public ConfigEntry<bool>  Enabled;
        public ConfigEntry<float> Friction;
        public ConfigEntry<float> InwardMax;
        public ConfigEntry<float> OutwardMax;
        public ConfigEntry<float> InwardExaggeration;
        public ConfigEntry<float> OutwardExaggeration;
        public ConfigEntry<bool>  Reverse;
    }

    struct MorphDefault
    {
        public string Name; public bool Enabled; public bool Reverse;
        public float InwardMax; public float OutwardMax;
        public MorphDefault(string n, bool e, bool r, float i, float o)
        { Name=n; Enabled=e; Reverse=r; InwardMax=i; OutwardMax=o; }
    }

    static readonly MorphDefault[] Defaults =
    {
        new MorphDefault("Labia minora-size",       true,  false, 0.7f,  2.0f ),
        new MorphDefault("Labia minora-style1",     true,  false, 0.7f,  2.0f ),
        new MorphDefault("Labia minora-exstrophy",  true,  true,  0.1f,  1.0f ),
        new MorphDefault("Labia majora-relaxation", true,  false, 1.0f,  0.0f ),
        new MorphDefault("Gen_Innie",               true,  true,  0.10f, 0.25f),
        new MorphDefault("Gens In - Out",           false, true,  1.0f,  0.0f ),
    };

    MorphCfg[] _cfgs;

    // ── runtime state ─────────────────────────────────────────────────────────
    List<RuntimeMorph>           _morphs  = new List<RuntimeMorph>();
    CollisionTriggerEventHandler _labiaHandler;
    FreeControllerV3             _abdomen;
    float?                       _prevDist;
    float?                       _prevVel;
    bool                         _ready;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        cfgPersonUID = Config.Bind("General", "PersonUID", "Auto",
            "UID of the Person atom to drive. 'Auto' = first Person in scene.");

        _cfgs = new MorphCfg[Defaults.Length];
        for (int i = 0; i < Defaults.Length; i++)
        {
            var d = Defaults[i];
            string name = d.Name; bool enabled = d.Enabled; bool reverse = d.Reverse;
            float inwardMax = d.InwardMax; float outwardMax = d.OutwardMax;
            string sec = name;
            _cfgs[i] = new MorphCfg
            {
                Name               = name,
                Enabled            = Config.Bind(sec, "Enabled",            enabled,   "Drive this morph"),
                Friction           = Config.Bind(sec, "Friction",           1f,        new ConfigDescription("Response speed", new AcceptableValueRange<float>(0f, 1f))),
                InwardMax          = Config.Bind(sec, "InwardMax",          inwardMax, new ConfigDescription("Max inward morph value", new AcceptableValueRange<float>(-5f, 5f))),
                OutwardMax         = Config.Bind(sec, "OutwardMax",         outwardMax,new ConfigDescription("Max outward morph value", new AcceptableValueRange<float>(-5f, 5f))),
                InwardExaggeration = Config.Bind(sec, "InwardExaggeration", 0f,        new ConfigDescription("Exaggeration applied on inward motion", new AcceptableValueRange<float>(0f, 5f))),
                OutwardExaggeration= Config.Bind(sec, "OutwardExaggeration",0f,        new ConfigDescription("Exaggeration applied on outward motion", new AcceptableValueRange<float>(0f, 5f))),
                Reverse            = Config.Bind(sec, "Reverse",            reverse,   "Flip inward/outward direction"),
            };
        }

        StartCoroutine(StartupCoroutine());
    }

    IEnumerator StartupCoroutine()
    {
        while (SuperController.singleton == null) yield return null;
        while (SuperController.singleton.isLoading)  yield return null;

        SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;
        yield return StartCoroutine(SelectPersonCoroutine());
    }

    void OnSceneLoaded() => StartCoroutine(SelectPersonCoroutine());

    IEnumerator SelectPersonCoroutine()
    {
        _ready = false;
        ResetMorphs();
        _morphs.Clear();
        _labiaHandler = null;
        _abdomen      = null;
        _prevDist     = null;
        _prevVel      = null;

        yield return null;
        yield return null;

        Atom person = FindPerson();
        if (person == null)
        {
            Logger.LogWarning("[ExtraAutoGenitals] No Person atom found.");
            yield break;
        }

        var labiaTrigger = person.GetComponentsInChildren<CollisionTrigger>()
            .FirstOrDefault(t => t.name == "LabiaTrigger");
        if (labiaTrigger == null)
        {
            Logger.LogWarning("[ExtraAutoGenitals] LabiaTrigger not found on " + person.uid);
            yield break;
        }

        _labiaHandler = labiaTrigger.gameObject.GetComponentInChildren<CollisionTriggerEventHandler>();
        _abdomen      = person.freeControllers.FirstOrDefault(fc => fc.name == "abdomen2Control");

        if (_labiaHandler == null || _abdomen == null)
        {
            Logger.LogWarning("[ExtraAutoGenitals] Missing handler or abdomen2Control.");
            yield break;
        }

        var morphUI = ((DAZCharacterSelector)person.GetStorableByID("geometry")).morphsControlUI;

        foreach (var cfg in _cfgs)
        {
            DAZMorph morph = morphUI.GetMorphByDisplayName(cfg.Name);
            if (morph == null)
            {
                Logger.LogWarning("[ExtraAutoGenitals] Morph not found: " + cfg.Name);
                continue;
            }

            var anim = new LabiaAnimator(morph,
                isInwardMorph:      cfg.Reverse.Value,
                inwardMax:          cfg.InwardMax.Value,
                outwardMax:         cfg.OutwardMax.Value,
                inwardExaggeration: cfg.InwardExaggeration.Value,
                outwardExaggeration:cfg.OutwardExaggeration.Value);

            _morphs.Add(new RuntimeMorph { Cfg = cfg, Animator = anim });
        }

        Logger.LogInfo("[ExtraAutoGenitals] Ready on " + person.uid
            + " (" + _morphs.Count + " morphs)");
        _ready = true;
    }

    Atom FindPerson()
    {
        string uid = cfgPersonUID.Value;
        if (!string.IsNullOrEmpty(uid) && uid != "Auto")
        {
            var a = SuperController.singleton.GetAtomByUid(uid);
            if (a != null && IsFemale(a)) return a;
        }
        return SuperController.singleton.GetAtoms().FirstOrDefault(IsFemale);
    }

    static bool IsFemale(Atom a)
    {
        if (a.type != "Person") return false;
        return a.GetComponentsInChildren<CollisionTrigger>()
                .Any(t => t.name == "LabiaTrigger");
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_ready || SuperController.singleton.freezeAnimation) return;

        try
        {
            var colliders = _labiaHandler.collidingWithDictionary.Keys.ToList();

            float shortest = colliders.Count > 0
                ? colliders.Min(col => Vector3.Distance(
                    col.transform.position, _abdomen.transform.position))
                : 0f;

            float velocity = ((_prevDist ?? 0f) - shortest) / Time.deltaTime;

            if (_prevDist.HasValue && _prevVel.HasValue)
            {
                foreach (var m in _morphs)
                {
                    if (!m.Cfg.Enabled.Value) continue;

                    float friction = m.Cfg.Friction.Value;
                    if (friction <= 0f) continue;

                    // Sync config → animator each frame (cheap for 6 morphs)
                    m.Animator.IsInwardMorph         = m.Cfg.Reverse.Value;
                    m.Animator.InwardMax             = m.Cfg.InwardMax.Value;
                    m.Animator.OutwardMax            = m.Cfg.OutwardMax.Value;
                    m.Animator.InwardExaggeration    = m.Cfg.InwardExaggeration.Value;
                    m.Animator.OutwardExaggeration   = m.Cfg.OutwardExaggeration.Value;

                    float? next = m.Animator.NextMorphValue(
                        colliders.Count > 0 ? (float?)velocity : null, friction);
                    if (next.HasValue)
                        m.Animator.Morph.morphValueAdjustLimits = next.Value;
                }
            }

            if (Mathf.Approximately(velocity, 0f))
                velocity = _prevVel ?? 0f;
            _prevDist = shortest;
            _prevVel  = velocity;
        }
        catch (Exception e)
        {
            Logger.LogError("[ExtraAutoGenitals] Update: " + e.Message);
        }
    }

    // ── cleanup ───────────────────────────────────────────────────────────────
    void ResetMorphs()
    {
        foreach (var m in _morphs)
            m.Animator?.Morph?.SetDefaultValue();
    }

    void OnDestroy()
    {
        if (SuperController.singleton != null)
            SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
        ResetMorphs();
    }

    // ─────────────────────────────────────────────────────────────────────────
    class RuntimeMorph
    {
        public MorphCfg     Cfg;
        public LabiaAnimator Animator;
    }
}

// ── LabiaAnimator ─────────────────────────────────────────────────────────────
// Pure velocity-to-morph logic.  Ported directly from the original.
public class LabiaAnimator
{
    const int   VELOCITY_SMOOTH_LOOKBACK  = 64;
    const float VELOCITY_SMOOTH_STDDEV_MAX = 2f;
    const float ANIMATION_SPEED_MIN       = 0.08f;

    public DAZMorph Morph              { get; private set; }
    public float    MorphDefault       => Morph?.jsonFloat?.defaultVal ?? 0f;
    public float    MorphCurrent       => Morph?.morphValue ?? 0f;
    public float    MorphRestingValue  { get; set; }
    public bool     IsInwardMorph      { get; set; }
    public float    InwardMax          { get; set; }
    public float    InwardExaggeration { get; set; }
    public float    OutwardMax         { get; set; }
    public float    OutwardExaggeration{ get; set; }

    int   _iteration;
    float[] _velHistory = new float[VELOCITY_SMOOTH_LOOKBACK];
    float _sd1, _sd2, _sd3;

    public LabiaAnimator(DAZMorph morph, bool isInwardMorph, float inwardMax, float outwardMax,
        float inwardExaggeration = 0f, float outwardExaggeration = 0f)
    {
        Morph               = morph;
        IsInwardMorph       = isInwardMorph;
        InwardMax           = inwardMax;
        OutwardMax          = outwardMax;
        InwardExaggeration  = inwardExaggeration;
        OutwardExaggeration = outwardExaggeration;
        MorphRestingValue   = MorphDefault;
    }

    public float? NextMorphValue(float? velocityRaw, float friction)
    {
        friction = Mathf.Clamp(friction, 0f, 1f);
        float morphMin = MorphRestingValue - (IsInwardMorph
            ? OutwardMax + OutwardExaggeration
            : InwardMax  + InwardExaggeration);
        float morphMax = MorphRestingValue + (IsInwardMorph
            ? InwardMax  + InwardExaggeration
            : OutwardMax + OutwardExaggeration);
        float velocity = Mathf.Clamp(velocityRaw ?? 0f, -1f, 1f);

        if (friction <= 0f) return null;

        if (!velocityRaw.HasValue)
        {
            return Mathf.Clamp(
                Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _sd2, ANIMATION_SPEED_MIN),
                morphMin, morphMax);
        }

        if (VelocityLooksLikeMistake(velocity))
            return MorphCurrent;

        if (Mathf.Approximately(velocity, 0f))
        {
            return Mathf.Clamp(
                Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _sd3, 100f),
                morphMin, morphMax);
        }

        float pct         = Mathf.InverseLerp(morphMin, morphMax, MorphCurrent);
        float morphDelta  = velocity * friction * (IsInwardMorph ? 1f : -1f) * 10f;
        float morphTarget = Mathf.Clamp(MorphRestingValue + morphDelta, morphMin, morphMax);
        return Mathf.SmoothDamp(MorphCurrent, morphTarget, ref _sd1, ANIMATION_SPEED_MIN);
    }

    bool VelocityLooksLikeMistake(float velocity)
    {
        int idx = _iteration % (VELOCITY_SMOOTH_LOOKBACK - 1);
        _velHistory[idx] = velocity;
        _iteration = (_iteration + 1) % VELOCITY_SMOOTH_LOOKBACK;

        float avg    = _velHistory.Average();
        float stddev = (float)Math.Sqrt(_velHistory.Average(v => Math.Pow(v - avg, 2)));
        if (stddev > 0f)
        {
            float zScore = Mathf.Abs((velocity - avg) / stddev);
            if (zScore > VELOCITY_SMOOTH_STDDEV_MAX) return true;
        }
        return false;
    }
}
