using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

// MicroPhysicsFixer v1.5 — BepInEx edition
// Scans all Person atoms for rigidbodies with unstable physics values and clamps them
// to safe ranges. All settings live in BepInEx/config/com.kimowal.microphysicsfixer.cfg
// Runs automatically when a scene finishes loading; no session plugin required.

namespace Kimowal
{
    [BepInPlugin("com.kimowal.microphysicsfixer", "MicroPhysicsFixer", "1.5.0")]
    public class MicroPhysicsFixerPlugin : BaseUnityPlugin
    {
        // General
        ConfigEntry<string> cfgPreset;
        ConfigEntry<float>  cfgGlobalIntensity;
        ConfigEntry<string> cfgStabilizerMode;
        ConfigEntry<string> cfgJiggleCurve;

        // Thresholds (user config — never written at runtime)
        ConfigEntry<float> cfgMinMass;
        ConfigEntry<float> cfgMaxMass;
        ConfigEntry<float> cfgMinDrag;
        ConfigEntry<float> cfgMaxDrag;
        ConfigEntry<float> cfgMinAngularDrag;
        ConfigEntry<float> cfgMaxAngularDrag;

        // Region toggles
        ConfigEntry<bool> cfgFixHeadNeck;
        ConfigEntry<bool> cfgFixChest;
        ConfigEntry<bool> cfgFixAbdomenPelvis;
        ConfigEntry<bool> cfgFixBreastGlute;
        ConfigEntry<bool> cfgFixGenital;
        ConfigEntry<bool> cfgFixArmsHands;
        ConfigEntry<bool> cfgFixLegsFeet;
        ConfigEntry<bool> cfgFixOther;

        // Region intensities
        ConfigEntry<float> cfgIntensityHeadNeck;
        ConfigEntry<float> cfgIntensityChest;
        ConfigEntry<float> cfgIntensityAbdomenPelvis;
        ConfigEntry<float> cfgIntensityBreastGlute;
        ConfigEntry<float> cfgIntensityGenital;
        ConfigEntry<float> cfgIntensityArmsHands;
        ConfigEntry<float> cfgIntensityLegsFeet;
        ConfigEntry<float> cfgIntensityOther;

        // Advanced stability
        ConfigEntry<bool>  cfgEnableSolverBoost;
        ConfigEntry<float> cfgSolverBoostIntensity;
        ConfigEntry<bool>  cfgEnableInterpolation;
        ConfigEntry<bool>  cfgEnableMaxAngularVelocity;
        ConfigEntry<float> cfgMaxAngularVelocityValue;
        ConfigEntry<bool>  cfgEnableHighPrecisionCollisions;

        // Genital physics
        ConfigEntry<string> cfgGenitalPhysicsMode;
        ConfigEntry<bool>   cfgEnableGenitalPhysicMaterial;
        ConfigEntry<float>  cfgGenitalFriction;
        ConfigEntry<float>  cfgGenitalBounciness;
        ConfigEntry<bool>   cfgEnableGenitalRegionalPhysics;

        // Auto-fix & monitoring
        ConfigEntry<bool>   cfgAutoFixOnSceneLoad;
        ConfigEntry<float>  cfgAutoFixDelaySec;
        ConfigEntry<string> cfgPhysicsHz;
        ConfigEntry<bool>   cfgEnableRealtimeMonitoring;
        ConfigEntry<float>  cfgMonitoringInterval;

        // Runtime threshold state — computed from preset + config; never written back to cfg files.
        // Bug fix: writing back to ConfigEntry would clobber user's custom values on every scene load.
        float _tMinMass, _tMaxMass, _tMinDrag, _tMaxDrag, _tMinAngularDrag, _tMaxAngularDrag;

        // Runtime state
        bool  _initialized;  // true once SuperController is ready and _wasLoading is set
        bool  _wasLoading;
        float _monitoringTimer;
        List<Rigidbody> _monitoredRigidbodies = new List<Rigidbody>();
        Dictionary<Rigidbody, Vector3> _targetPhysicsValues = new Dictionary<Rigidbody, Vector3>();

        // Undo
        class RbBackup
        {
            public Rigidbody rb;
            public float mass, drag, angularDrag;
            public int solverIterations, solverVelocityIterations;
            public RigidbodyInterpolation interpolation;
            public float maxAngularVelocity;
            public CollisionDetectionMode collisionDetectionMode;
        }
        List<RbBackup> _backups = new List<RbBackup>();

        new ManualLogSource Logger => base.Logger;

        void Awake()
        {
            BindConfig();
            InitThresholdsFromConfig();
            Logger.LogInfo("MicroPhysicsFixer BepInEx plugin loaded.");
            StartCoroutine(StartupCoroutine());
        }

        void BindConfig()
        {
            const string GEN  = "1. General";
            const string THR  = "2. Thresholds";
            const string REG  = "3. Regions";
            const string INT  = "4. Region Intensities";
            const string ADV  = "5. Advanced Stability";
            const string GEN2 = "6. Genital Physics";
            const string AUTO = "7. Auto-Fix";

            cfgPreset          = Config.Bind(GEN, "Preset", "Realistic",
                new ConfigDescription("Physics preset. Acceptable: None, Realistic, Soft Anime, Firm Athletic, Hyper Jiggle",
                    new AcceptableValueList<string>("None","Realistic","Soft Anime","Firm Athletic","Hyper Jiggle")));
            cfgGlobalIntensity = Config.Bind(GEN, "GlobalJiggleIntensity", 1.0f,
                new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 1.5f)));
            cfgStabilizerMode  = Config.Bind(GEN, "StabilizerMode", "Normal",
                new ConfigDescription("Chest/glute stabilizer strength.", new AcceptableValueList<string>("Off","Normal","Strong")));
            cfgJiggleCurve     = Config.Bind(GEN, "JiggleCurve", "Linear",
                new ConfigDescription("", new AcceptableValueList<string>("Linear","Soft","Sharp")));

            cfgMinMass        = Config.Bind(THR, "MinMass",        0.2f,  new ConfigDescription("", new AcceptableValueRange<float>(0.1f,  1f)));
            cfgMaxMass        = Config.Bind(THR, "MaxMass",        2.5f,  new ConfigDescription("", new AcceptableValueRange<float>(1f,   20f)));
            cfgMinDrag        = Config.Bind(THR, "MinDrag",        0.08f, new ConfigDescription("", new AcceptableValueRange<float>(0.01f, 1f)));
            cfgMaxDrag        = Config.Bind(THR, "MaxDrag",        3f,    new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 10f)));
            cfgMinAngularDrag = Config.Bind(THR, "MinAngularDrag", 0.02f, new ConfigDescription("", new AcceptableValueRange<float>(0.001f, 1f)));
            cfgMaxAngularDrag = Config.Bind(THR, "MaxAngularDrag", 3f,    new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 10f)));

            cfgFixHeadNeck      = Config.Bind(REG, "FixHeadNeck",      true, "");
            cfgFixChest         = Config.Bind(REG, "FixChest",         true, "");
            cfgFixAbdomenPelvis = Config.Bind(REG, "FixAbdomenPelvis", true, "");
            cfgFixBreastGlute   = Config.Bind(REG, "FixBreastGlute",   true, "");
            cfgFixGenital       = Config.Bind(REG, "FixGenital",       true, "");
            cfgFixArmsHands     = Config.Bind(REG, "FixArmsHands",     true, "");
            cfgFixLegsFeet      = Config.Bind(REG, "FixLegsFeet",      true, "");
            cfgFixOther         = Config.Bind(REG, "FixOther",         true, "");

            cfgIntensityHeadNeck      = Config.Bind(INT, "HeadNeck",      1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityChest         = Config.Bind(INT, "Chest",         1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityAbdomenPelvis = Config.Bind(INT, "AbdomenPelvis", 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityBreastGlute   = Config.Bind(INT, "BreastGlute",   1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityGenital       = Config.Bind(INT, "Genital",       0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityArmsHands     = Config.Bind(INT, "ArmsHands",     1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityLegsFeet      = Config.Bind(INT, "LegsFeet",      1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));
            cfgIntensityOther         = Config.Bind(INT, "Other",         1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 2f)));

            cfgEnableSolverBoost           = Config.Bind(ADV, "EnableSolverBoost",           false, "Increase solver iterations to reduce jitter.");
            cfgSolverBoostIntensity        = Config.Bind(ADV, "SolverBoostIntensity",        1.0f,  new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 2f)));
            cfgEnableInterpolation         = Config.Bind(ADV, "EnableInterpolation",         false, "Enable Rigidbody interpolation for smoother motion at low FPS.");
            cfgEnableMaxAngularVelocity    = Config.Bind(ADV, "EnableMaxAngularVelocity",    false, "");
            cfgMaxAngularVelocityValue     = Config.Bind(ADV, "MaxAngularVelocityValue",     10f,   new ConfigDescription("", new AcceptableValueRange<float>(7f, 20f)));
            cfgEnableHighPrecisionCollisions = Config.Bind(ADV, "EnableHighPrecisionCollisions", false, "ContinuousDynamic on chest/head/pelvis bones.");

            cfgGenitalPhysicsMode           = Config.Bind(GEN2, "GenitalPhysicsMode", "Soft Tissue",
                new ConfigDescription("", new AcceptableValueList<string>("Standard","Soft Tissue","Ultra-Sensitive","Penetration-Ready")));
            cfgEnableGenitalPhysicMaterial  = Config.Bind(GEN2, "EnableGenitalPhysicMaterial", true, "Apply friction/bounciness PhysicMaterial to genital colliders.");
            cfgGenitalFriction              = Config.Bind(GEN2, "GenitalFriction",    0.4f, new ConfigDescription("", new AcceptableValueRange<float>(0.15f, 1f)));
            cfgGenitalBounciness            = Config.Bind(GEN2, "GenitalBounciness",  0.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 0.3f)));
            cfgEnableGenitalRegionalPhysics = Config.Bind(GEN2, "EnableGenitalRegionalPhysics", true, "Different multipliers for Gen1/Gen2/Gen3 bones.");

            cfgAutoFixOnSceneLoad       = Config.Bind(AUTO, "AutoFixOnSceneLoad",    true,  "Apply fix automatically after each scene finishes loading.");
            cfgAutoFixDelaySec          = Config.Bind(AUTO, "AutoFixDelaySec",       2.0f,  new ConfigDescription("Seconds after scene load before applying fix.", new AcceptableValueRange<float>(0f, 15f)));
            cfgPhysicsHz                = Config.Bind(AUTO, "SetPhysicsHz",          "Off",
                new ConfigDescription("Set Time.fixedDeltaTime to match a target Hz after fix.", new AcceptableValueList<string>("Off","60 Hz","90 Hz","120 Hz","144 Hz")));
            cfgEnableRealtimeMonitoring = Config.Bind(AUTO, "EnableRealtimeMonitoring", true, "Continuously watch physics values and correct drift.");
            cfgMonitoringInterval       = Config.Bind(AUTO, "MonitoringIntervalSec",    0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 5f)));
        }

        // Load user's config values into the runtime threshold fields.
        // These are what FixPerson uses at runtime — the cfg* entries are never written back.
        void InitThresholdsFromConfig()
        {
            _tMinMass        = cfgMinMass.Value;
            _tMaxMass        = cfgMaxMass.Value;
            _tMinDrag        = cfgMinDrag.Value;
            _tMaxDrag        = cfgMaxDrag.Value;
            _tMinAngularDrag = cfgMinAngularDrag.Value;
            _tMaxAngularDrag = cfgMaxAngularDrag.Value;
        }

        IEnumerator StartupCoroutine()
        {
            while (SuperController.singleton == null)
                yield return null;

            _wasLoading  = SuperController.singleton.isLoading;
            _initialized = true;

            Logger.LogInfo("MicroPhysicsFixer: SuperController ready.");

            // Bug fix: if VaM was already in a fully loaded state when this plugin started,
            // the isLoading false→false transition is never detected by FixedUpdate, so the
            // initial fix would never fire. Trigger it explicitly here.
            if (!_wasLoading && cfgAutoFixOnSceneLoad.Value)
                StartCoroutine(SceneLoadFixCoroutine());
        }

        void FixedUpdate()
        {
            if (!_initialized) return;
            if (SuperController.singleton == null) return;

            bool isLoading = SuperController.singleton.isLoading;

            // Scene-load detection: trigger fix on the loading→loaded transition.
            if (_wasLoading && !isLoading)
            {
                _wasLoading = false;
                if (cfgAutoFixOnSceneLoad.Value)
                    StartCoroutine(SceneLoadFixCoroutine());
                return;
            }
            _wasLoading = isLoading;

            // Real-time monitoring only while a scene is actually running.
            if (isLoading || !cfgEnableRealtimeMonitoring.Value) return;
            _monitoringTimer += Time.fixedDeltaTime;
            if (_monitoringTimer >= cfgMonitoringInterval.Value)
            {
                _monitoringTimer = 0f;
                MonitorAndCorrectPhysics();
            }
        }

        IEnumerator SceneLoadFixCoroutine()
        {
            if (cfgAutoFixDelaySec.Value > 0f)
                yield return new WaitForSeconds(cfgAutoFixDelaySec.Value);

            yield return null;
            yield return null;

            // Compute active thresholds from preset + global intensity into runtime fields.
            // This does NOT touch any ConfigEntry value, so user config is preserved.
            ApplyPresetToThresholds();
            ApplyFixToAllPersons();

            if (cfgPhysicsHz.Value != "Off")
                SetPhysicsFrequency(cfgPhysicsHz.Value);

            RebuildMonitoringList();
        }

        // ─────────────────────────────────────────────────────────────
        // Physics fix core
        // ─────────────────────────────────────────────────────────────

        void ApplyFixToAllPersons()
        {
            try
            {
                List<Atom> persons = GetAllPersons();
                if (persons.Count == 0) { Logger.LogInfo("MicroPhysicsFixer: No Person atoms in scene."); return; }

                _backups.Clear();
                int totalFixed = 0;

                foreach (Atom person in persons)
                {
                    int changed = FixPerson(person);
                    totalFixed += changed;
                    if (changed > 0)
                        Logger.LogInfo($"MicroPhysicsFixer: {person.uid} — fixed {changed} rigidbodies.");
                    else
                        Logger.LogInfo($"MicroPhysicsFixer: {person.uid} — no changes needed.");
                }

                Logger.LogInfo($"MicroPhysicsFixer: Done. Total RBs modified: {totalFixed}");
            }
            catch (Exception e)
            {
                Logger.LogError($"MicroPhysicsFixer ApplyFixToAllPersons: {e}");
            }
        }

        int FixPerson(Atom person)
        {
            if (person == null) return 0;
            Rigidbody[] bodies = person.GetComponentsInChildren<Rigidbody>(true);
            if (bodies == null || bodies.Length == 0) return 0;

            int changed = 0;

            foreach (Rigidbody rb in bodies)
            {
                if (rb == null) continue;

                float m = rb.mass, d = rb.drag, ad = rb.angularDrag;
                string region = GetBodyPartCategory(rb.name);
                if (!ShouldFixBodyPart(region)) continue;

                float newM = m, newD = d, newAD = ad;
                bool isChestOrGlute = (region == "Chest" || region == "Glute" || region == "Breast");
                bool stabOff    = cfgStabilizerMode.Value == "Off";
                bool stabStrong = cfgStabilizerMode.Value == "Strong";

                if (isChestOrGlute && !stabOff)
                {
                    if (stabStrong)
                    {
                        newM  = Clamp(m,  _tMinMass,        Mathf.Min(_tMaxMass, 1.5f));
                        newD  = Clamp(d,  Mathf.Max(_tMinDrag, 0.4f),        _tMaxDrag);
                        newAD = Clamp(ad, Mathf.Max(_tMinAngularDrag, 0.08f), _tMaxAngularDrag);
                    }
                    else
                    {
                        newM  = Clamp(m,  _tMinMass,        Mathf.Min(_tMaxMass, 2f));
                        newD  = Clamp(d,  Mathf.Max(_tMinDrag, 0.2f),        _tMaxDrag);
                        newAD = Clamp(ad, Mathf.Max(_tMinAngularDrag, 0.05f), _tMaxAngularDrag);
                    }
                }
                else
                {
                    newM  = Clamp(m,  _tMinMass,        _tMaxMass);
                    newD  = Clamp(d,  _tMinDrag,        _tMaxDrag);
                    newAD = Clamp(ad, _tMinAngularDrag, _tMaxAngularDrag);
                }

                // Region intensity + jiggle curve blend
                float regionIntensity = GetRegionIntensity(region);
                float blend = ApplyJiggleCurve(Mathf.Clamp01(regionIntensity));

                newM  = m  + (newM  - m)  * blend;
                newD  = d  + (newD  - d)  * blend;
                newAD = ad + (newAD - ad) * blend;

                // Jiggle increase when intensity > 1 (applied before genital physics so
                // genital safety minimums are enforced on the final values, not an intermediate).
                if (regionIntensity > 1f)
                {
                    float extra = ApplyJiggleCurve(Mathf.Clamp01(regionIntensity - 1f));
                    newD  = Mathf.Lerp(newD,  Mathf.Max(_tMinDrag        * 1.5f, newD  * 0.85f), extra * 0.5f);
                    newAD = Mathf.Lerp(newAD, Mathf.Max(_tMinAngularDrag * 1.5f, newAD * 0.85f), extra * 0.5f);
                    newM  = Mathf.Lerp(newM,  Mathf.Max(_tMinMass        * 1.2f, newM  * 0.95f), extra * 0.3f);
                }

                // Genital enhanced physics — applied after jiggle increase so its safety
                // minimums are the last word on genital values.
                if (region == "Genital")
                    ApplyGenitalPhysics(rb, ref newM, ref newD, ref newAD);

                // Global absolute safety floor (lower than genital minimums, so genital
                // bones are already covered by ApplyGenitalPhysics above).
                if (newM  < 0.15f)  newM  = 0.15f;
                if (newD  < 0.08f)  newD  = 0.08f;
                if (newAD < 0.015f) newAD = 0.015f;

                if (!Mathf.Approximately(m, newM) || !Mathf.Approximately(d, newD) || !Mathf.Approximately(ad, newAD))
                {
                    var bk = new RbBackup
                    {
                        rb = rb, mass = m, drag = d, angularDrag = ad,
                        solverIterations         = rb.solverIterations,
                        solverVelocityIterations = rb.solverVelocityIterations,
                        interpolation            = rb.interpolation,
                        maxAngularVelocity       = rb.maxAngularVelocity,
                        collisionDetectionMode   = rb.collisionDetectionMode
                    };
                    _backups.Add(bk);

                    rb.mass = newM; rb.drag = newD; rb.angularDrag = newAD;

                    if (cfgEnableSolverBoost.Value)
                    {
                        float si = cfgSolverBoostIntensity.Value;
                        rb.solverIterations         = Mathf.RoundToInt(6 + 6 * si);
                        rb.solverVelocityIterations = Mathf.RoundToInt(4 + 4 * si);
                    }

                    if (cfgEnableInterpolation.Value)
                        rb.interpolation = RigidbodyInterpolation.Interpolate;

                    if (cfgEnableMaxAngularVelocity.Value)
                        rb.maxAngularVelocity = cfgMaxAngularVelocityValue.Value;

                    if (cfgEnableHighPrecisionCollisions.Value)
                    {
                        bool keyBone = (region == "Chest" || region == "Head/Neck" || region == "Pelvis");
                        if (keyBone) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    }

                    changed++;
                }
            }

            return changed;
        }

        // regionIntensity parameter removed — genital is now always called after jiggle increase
        // so it no longer needs to know the intensity to guard against its own floor being violated.
        void ApplyGenitalPhysics(Rigidbody rb, ref float newM, ref float newD, ref float newAD)
        {
            string genMode = cfgGenitalPhysicsMode.Value;

            float regionalMult = 1.0f;
            if (cfgEnableGenitalRegionalPhysics.Value)
            {
                if      (rb.name.Contains("Gen1") || rb.name.Contains("labia")) regionalMult = 0.4f;
                else if (rb.name.Contains("Gen2"))                               regionalMult = 0.6f;
                else if (rb.name.Contains("Gen3"))                               regionalMult = 0.7f;
            }

            switch (genMode)
            {
                case "Standard":
                    newM  *= (0.8f * regionalMult);
                    newD  *= 0.6f;
                    newAD *= 0.6f;
                    break;
                case "Soft Tissue":
                    newM  *= (0.5f * regionalMult);
                    newD  *= 0.3f;
                    newAD *= 0.4f;
                    break;
                case "Ultra-Sensitive":
                    newM  *= (0.3f * regionalMult);
                    newD  *= 0.2f;
                    newAD *= 0.3f;
                    break;
                case "Penetration-Ready":
                    newM  = Mathf.Max(0.12f, newM  * (0.3f * regionalMult));
                    newD  = Mathf.Max(0.03f, newD  * 0.2f);
                    newAD = Mathf.Max(0.01f, newAD * 0.3f);
                    break;
            }

            if (cfgEnableGenitalPhysicMaterial.Value)
            {
                Collider col = rb.GetComponent<Collider>();
                if (col != null)
                {
                    float friction = Mathf.Max(0.15f, cfgGenitalFriction.Value);
                    float bounce   = cfgGenitalBounciness.Value;
                    PhysicMaterial mat      = new PhysicMaterial();
                    mat.dynamicFriction     = friction;
                    mat.staticFriction      = friction * 1.2f;
                    mat.bounciness          = bounce;
                    mat.frictionCombine     = PhysicMaterialCombine.Average;
                    mat.bounceCombine       = PhysicMaterialCombine.Minimum;
                    col.material            = mat;
                }
            }

            rb.collisionDetectionMode   = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = 0.5f;

            if (!cfgEnableSolverBoost.Value)
            {
                rb.solverIterations         = 10;
                rb.solverVelocityIterations = 6;
            }

            // Genital safety minimums — these are higher than the global floor and are applied
            // last, so nothing that runs after this can push values below them.
            if (newM  < 0.15f) newM  = 0.15f;
            if (newD  < 0.10f) newD  = 0.10f;
            if (newAD < 0.02f) newAD = 0.02f;
        }

        // ─────────────────────────────────────────────────────────────
        // Preset → runtime thresholds
        // ─────────────────────────────────────────────────────────────

        // Computes active thresholds from the chosen preset + global intensity and stores
        // them in the _t* runtime fields. Never touches any ConfigEntry.
        void ApplyPresetToThresholds()
        {
            string preset = cfgPreset.Value;

            // Start from whatever the user has in their config file.
            float minM = cfgMinMass.Value,       maxM = cfgMaxMass.Value;
            float minD = cfgMinDrag.Value,       maxD = cfgMaxDrag.Value;
            float minAD = cfgMinAngularDrag.Value, maxAD = cfgMaxAngularDrag.Value;

            if (!string.IsNullOrEmpty(preset) && preset != "None")
            {
                switch (preset)
                {
                    case "Realistic":
                        minM = 0.2f; maxM = 2.8f; minD = 0.05f; maxD = 3.2f; minAD = 0.02f; maxAD = 3.2f;
                        break;
                    case "Soft Anime":
                        minM = 0.18f; maxM = 1.8f; minD = 0.08f; maxD = 2.5f; minAD = 0.02f; maxAD = 2.5f;
                        break;
                    case "Firm Athletic":
                        minM = 0.25f; maxM = 3.0f; minD = 0.07f; maxD = 3.5f; minAD = 0.025f; maxAD = 3.5f;
                        break;
                    case "Hyper Jiggle":
                        minM = 0.16f; maxM = 1.6f; minD = 0.06f; maxD = 2.2f; minAD = 0.018f; maxAD = 2.2f;
                        break;
                }

                float gi = Mathf.Clamp(cfgGlobalIntensity.Value, 0.5f, 1.5f);
                minD  = Clamp(minD  / gi, 0.001f, 1f);
                maxD  = Clamp(maxD  / gi, 0.5f,  10f);
                minAD = Clamp(minAD / gi, 0.001f, 1f);
                maxAD = Clamp(maxAD / gi, 0.5f,  10f);
            }

            _tMinMass        = minM;
            _tMaxMass        = maxM;
            _tMinDrag        = minD;
            _tMaxDrag        = maxD;
            _tMinAngularDrag = minAD;
            _tMaxAngularDrag = maxAD;
        }

        // ─────────────────────────────────────────────────────────────
        // Real-time monitoring
        // ─────────────────────────────────────────────────────────────

        void RebuildMonitoringList()
        {
            _monitoredRigidbodies.Clear();
            _targetPhysicsValues.Clear();

            foreach (Atom person in GetAllPersons())
            {
                if (person == null) continue;
                foreach (Rigidbody rb in person.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (rb == null) continue;
                    _monitoredRigidbodies.Add(rb);
                    _targetPhysicsValues[rb] = new Vector3(rb.mass, rb.drag, rb.angularDrag);
                }
            }
        }

        void MonitorAndCorrectPhysics()
        {
            if (_monitoredRigidbodies.Count == 0) { RebuildMonitoringList(); return; }

            const float tolerance = 0.001f;

            for (int i = _monitoredRigidbodies.Count - 1; i >= 0; i--)
            {
                Rigidbody rb = _monitoredRigidbodies[i];
                if (rb == null) { _monitoredRigidbodies.RemoveAt(i); continue; }

                Vector3 target;
                if (!_targetPhysicsValues.TryGetValue(rb, out target)) continue;

                if (Mathf.Abs(rb.mass        - target.x) > tolerance ||
                    Mathf.Abs(rb.drag        - target.y) > tolerance ||
                    Mathf.Abs(rb.angularDrag - target.z) > tolerance)
                {
                    rb.mass = target.x; rb.drag = target.y; rb.angularDrag = target.z;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        List<Atom> GetAllPersons()
        {
            var persons = new List<Atom>();
            if (SuperController.singleton == null) return persons;
            foreach (Atom a in SuperController.singleton.GetAtoms())
                if (a != null && a.type == "Person") persons.Add(a);
            return persons;
        }

        string GetBodyPartCategory(string rbName)
        {
            string n = rbName.ToLower();
            if (n.Contains("labia") || n.Contains("vagina") || n.Contains("gens") ||
                n.Contains("vulva") || n.Contains("clitoris") || n.Contains("genital")) return "Genital";
            if (n.Contains("chest") || n.Contains("thorax"))                            return "Chest";
            if (n.Contains("abdomen") || n.Contains("belly"))                           return "Abdomen";
            if (n.Contains("pelvis") || n.Contains("hip"))                              return "Pelvis";
            if (n.Contains("breast") || n.Contains("pectoral"))                         return "Breast";
            if (n.Contains("glute") || n.Contains("buttock"))                           return "Glute";
            if (n.Contains("head") || n.Contains("neck"))                               return "Head/Neck";
            if (n.Contains("thigh") || n.Contains("leg"))                               return "Leg";
            if (n.Contains("arm") || n.Contains("forearm") || n.Contains("shoulder"))   return "Arm";
            if (n.Contains("hand") || n.Contains("finger"))                             return "Hand";
            if (n.Contains("foot") || n.Contains("toe"))                                return "Foot";
            return "Other";
        }

        bool ShouldFixBodyPart(string region)
        {
            switch (region)
            {
                case "Head/Neck":              return cfgFixHeadNeck.Value;
                case "Chest":                  return cfgFixChest.Value;
                case "Abdomen": case "Pelvis": return cfgFixAbdomenPelvis.Value;
                case "Breast":  case "Glute":  return cfgFixBreastGlute.Value;
                case "Genital":                return cfgFixGenital.Value;
                case "Arm":     case "Hand":   return cfgFixArmsHands.Value;
                case "Leg":     case "Foot":   return cfgFixLegsFeet.Value;
                default:                       return cfgFixOther.Value;
            }
        }

        float GetRegionIntensity(string region)
        {
            switch (region)
            {
                case "Head/Neck":              return cfgIntensityHeadNeck.Value;
                case "Chest":                  return cfgIntensityChest.Value;
                case "Abdomen": case "Pelvis": return cfgIntensityAbdomenPelvis.Value;
                case "Breast":  case "Glute":  return cfgIntensityBreastGlute.Value;
                case "Genital":                return cfgIntensityGenital.Value;
                case "Arm":     case "Hand":   return cfgIntensityArmsHands.Value;
                case "Leg":     case "Foot":   return cfgIntensityLegsFeet.Value;
                default:                       return cfgIntensityOther.Value;
            }
        }

        float ApplyJiggleCurve(float t)
        {
            switch (cfgJiggleCurve.Value)
            {
                case "Soft":  return Mathf.Sqrt(Mathf.Clamp01(t));
                case "Sharp": t = Mathf.Clamp01(t); return t * t;
                default:      return Mathf.Clamp01(t);
            }
        }

        void SetPhysicsFrequency(string option)
        {
            try
            {
                float hz = float.Parse(option.Replace(" Hz", "").Trim());
                if (hz > 0) Time.fixedDeltaTime = 1f / hz;
                Logger.LogInfo($"MicroPhysicsFixer: Physics rate set to {hz} Hz.");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"MicroPhysicsFixer: Failed to set physics Hz: {e.Message}");
            }
        }

        static float Clamp(float v, float min, float max) =>
            v < min ? min : v > max ? max : v;
    }
}
