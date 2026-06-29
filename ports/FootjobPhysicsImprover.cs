// FootjobPhysicsImprover — BepInEx edition
// Original plugin by SoleStorm (Footjob_Physics_Improver.1.var).
// Auto-attaches to the first female Person atom. Settings:
// BepInEx/config/com.solestorm.footjobphysicsimprover.cfg

using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SoleStorm
{
    [BepInPlugin("com.solestorm.footjobphysicsimprover", "FootjobPhysicsImprover", "1.0.0")]
    public class FootjobPhysicsImproverPlugin : BaseUnityPlugin
    {
        // ── Person selection ──────────────────────────────────────────────────
        ConfigEntry<string> cfgPersonUID;
        Atom _person;

        // ── Friction config ───────────────────────────────────────────────────
        ConfigEntry<bool>   cfgFrictionEnabled;
        ConfigEntry<float>  cfgStaticFriction;
        ConfigEntry<float>  cfgDynamicFriction;
        ConfigEntry<float>  cfgBounciness;
        ConfigEntry<string> cfgFrictionCombine;
        ConfigEntry<string> cfgBounceCombine;

        // ── Mass config ───────────────────────────────────────────────────────
        ConfigEntry<bool>  cfgMassEnabled;
        ConfigEntry<float> cfgToeMassMult;
        ConfigEntry<float> cfgToeAngularDrag;
        ConfigEntry<float> cfgToeDrag;
        ConfigEntry<float> cfgPenisMassMult;

        // ── Friction state ────────────────────────────────────────────────────
        readonly Dictionary<Collider, PhysicMaterial> _originalMaterials = new Dictionary<Collider, PhysicMaterial>();
        readonly List<PhysicMaterial> _createdMaterials = new List<PhysicMaterial>();

        // ── Mass state ────────────────────────────────────────────────────────
        static readonly HashSet<string> ToeNames = new HashSet<string>
        {
            "rToe","rBigToe","rSmallToe1","rSmallToe2","rSmallToe3","rSmallToe4",
            "lToe","lBigToe","lSmallToe1","lSmallToe2","lSmallToe3","lSmallToe4"
        };

        // Friction override target: feet + toes only. The original .var script applied
        // this to every non-trigger collider on the whole atom, which weakens contact
        // resolution for hands/torso/etc. against other atoms and causes visible
        // inter-atom clipping/"phasing". Scoping to feet keeps the smoother-sliding
        // effect where it's actually relevant (footjob contact) without destabilizing
        // unrelated collisions.
        static readonly HashSet<string> FootColliderNames = new HashSet<string>
        {
            "rFoot","lFoot",
            "rToe","rBigToe","rSmallToe1","rSmallToe2","rSmallToe3","rSmallToe4",
            "lToe","lBigToe","lSmallToe1","lSmallToe2","lSmallToe3","lSmallToe4"
        };

        static readonly HashSet<string> PenisNames = new HashSet<string>
        {
            "Gen1","Gen2","Gen3","Gen4","Gen5","Gen6",
            "Testes","lTesticle","rTesticle"
        };

        struct Entry { public Rigidbody rb; public float baseMass, baseDrag, baseAngularDrag; }

        readonly List<Entry> _toes = new List<Entry>();
        readonly List<Entry> _penis = new List<Entry>();

        bool _initialized;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            BindConfig();
            StartCoroutine(StartupCoroutine());
            Logger.LogInfo("FootjobPhysicsImprover BepInEx plugin loaded.");
        }

        void BindConfig()
        {
            const string G = "General";
            const string FR = "Friction";
            const string MS = "Mass";

            cfgPersonUID = Config.Bind(G, "PersonUID", "Auto",
                "UID of Person atom to apply physics tweaks to. \"Auto\" = first female Person found.");

            cfgFrictionEnabled = Config.Bind(FR, "Enabled", true, "Override collider friction/bounciness for smoother sliding contact.");
            cfgStaticFriction  = Config.Bind(FR, "StaticFriction",  0.05f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgDynamicFriction = Config.Bind(FR, "DynamicFriction", 0.05f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgBounciness      = Config.Bind(FR, "Bounciness",      0.02f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgFrictionCombine = Config.Bind(FR, "FrictionCombine", "Minimum",
                new ConfigDescription("", new AcceptableValueList<string>("Average", "Minimum", "Maximum", "Multiply")));
            cfgBounceCombine   = Config.Bind(FR, "BounceCombine",   "Average",
                new ConfigDescription("", new AcceptableValueList<string>("Average", "Minimum", "Maximum", "Multiply")));

            cfgMassEnabled     = Config.Bind(MS, "Enabled", true, "Boost mass of toe and penis-group rigidbodies.");
            cfgToeMassMult     = Config.Bind(MS, "ToeMassMultiplier", 3f,  new ConfigDescription("", new AcceptableValueRange<float>(1f, 20f)));
            cfgToeAngularDrag  = Config.Bind(MS, "ToeAngularDrag",    0f,  new ConfigDescription("Reduces sway. 0 = keep original.", new AcceptableValueRange<float>(0f, 20f)));
            cfgToeDrag         = Config.Bind(MS, "ToeDrag",           0f,  new ConfigDescription("Linear drag. 0 = keep original.", new AcceptableValueRange<float>(0f, 20f)));
            cfgPenisMassMult   = Config.Bind(MS, "PenisMassMultiplier", 1f, new ConfigDescription("", new AcceptableValueRange<float>(1f, 20f)));

            cfgPersonUID.SettingChanged += (_, __) => { if (_initialized) StartCoroutine(SelectPersonCoroutine(ResolvePersonUID())); };
            cfgFrictionEnabled.SettingChanged += (_, __) => OnFrictionToggle();
            cfgStaticFriction.SettingChanged  += (_, __) => { if (cfgFrictionEnabled.Value) ApplyFriction(); };
            cfgDynamicFriction.SettingChanged += (_, __) => { if (cfgFrictionEnabled.Value) ApplyFriction(); };
            cfgBounciness.SettingChanged      += (_, __) => { if (cfgFrictionEnabled.Value) ApplyFriction(); };
            cfgFrictionCombine.SettingChanged += (_, __) => { if (cfgFrictionEnabled.Value) ApplyFriction(); };
            cfgBounceCombine.SettingChanged   += (_, __) => { if (cfgFrictionEnabled.Value) ApplyFriction(); };
            cfgMassEnabled.SettingChanged     += (_, __) => OnMassToggle();
        }

        IEnumerator StartupCoroutine()
        {
            while (SuperController.singleton == null) yield return null;
            while (SuperController.singleton.isLoading) yield return null;

            SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;
            yield return SelectPersonCoroutine(ResolvePersonUID());
            _initialized = true;
        }

        void OnSceneLoaded()
        {
            StartCoroutine(SelectPersonCoroutine(ResolvePersonUID()));
        }

        string ResolvePersonUID()
        {
            if (cfgPersonUID.Value == "Auto" || string.IsNullOrEmpty(cfgPersonUID.Value))
            {
                var ps = SuperController.singleton.GetAtoms().FindAll(a => {
                    if (a.type != "Person") return false;
                    var cs = a.GetStorableByID("geometry") as DAZCharacterSelector;
                    return cs != null && cs.gender == DAZCharacterSelector.Gender.Female;
                });
                return ps.Count > 0 ? ps[0].uid : "";
            }
            return cfgPersonUID.Value;
        }

        IEnumerator SelectPersonCoroutine(string uid)
        {
            ResetAll();
            _person = null;
            yield return null;
            yield return null;

            _person = SuperController.singleton.GetAtomByUid(uid);
            if (_person == null) { Logger.LogWarning("FootjobPhysicsImprover: Person '" + uid + "' not found."); yield break; }

            Gather(ToeNames, _toes);
            Gather(PenisNames, _penis);
            Logger.LogInfo($"FootjobPhysicsImprover: Attached to '{uid}'. Toes found: {_toes.Count}, Penis rigidbodies found: {_penis.Count}");

            if (cfgFrictionEnabled.Value) ApplyFriction();
        }

        void ResetAll()
        {
            RestoreList(_toes);
            RestoreList(_penis);
            RestoreFriction();
            _toes.Clear();
            _penis.Clear();
        }

        // ── Friction logic ────────────────────────────────────────────────────

        void ApplyFriction()
        {
            if (_person == null) return;
            Collider[] colliders = _person.GetComponentsInChildren<Collider>(true);

            foreach (Collider col in colliders)
            {
                if (col == null || col.isTrigger) continue;
                if (col.attachedRigidbody == null || !FootColliderNames.Contains(col.attachedRigidbody.name)) continue;

                if (!_originalMaterials.ContainsKey(col))
                    _originalMaterials[col] = col.sharedMaterial;

                PhysicMaterial mat = new PhysicMaterial("FootjobPhysicsImprover_" + col.name)
                {
                    staticFriction  = cfgStaticFriction.Value,
                    dynamicFriction = cfgDynamicFriction.Value,
                    bounciness      = cfgBounciness.Value,
                    frictionCombine = ParseCombine(cfgFrictionCombine.Value),
                    bounceCombine   = ParseCombine(cfgBounceCombine.Value)
                };

                col.material = mat;
                _createdMaterials.Add(mat);
            }
        }

        void RestoreFriction()
        {
            foreach (var kvp in _originalMaterials)
                if (kvp.Key != null) kvp.Key.sharedMaterial = kvp.Value;
            _originalMaterials.Clear();

            foreach (var mat in _createdMaterials)
                if (mat != null) Destroy(mat);
            _createdMaterials.Clear();
        }

        void OnFrictionToggle()
        {
            if (!_initialized) return;
            if (cfgFrictionEnabled.Value) ApplyFriction();
            else RestoreFriction();
        }

        static PhysicMaterialCombine ParseCombine(string val)
        {
            switch (val)
            {
                case "Minimum":  return PhysicMaterialCombine.Minimum;
                case "Maximum":  return PhysicMaterialCombine.Maximum;
                case "Multiply": return PhysicMaterialCombine.Multiply;
                default:         return PhysicMaterialCombine.Average;
            }
        }

        // ── Mass logic ────────────────────────────────────────────────────────

        void Gather(HashSet<string> names, List<Entry> target)
        {
            target.Clear();
            if (_person == null) return;
            foreach (Rigidbody rb in _person.GetComponentsInChildren<Rigidbody>())
            {
                if (names.Contains(rb.name))
                {
                    target.Add(new Entry { rb = rb, baseMass = rb.mass, baseDrag = rb.drag, baseAngularDrag = rb.angularDrag });
                }
            }
        }

        void ApplyToes()
        {
            for (int i = 0; i < _toes.Count; i++)
            {
                Rigidbody rb = _toes[i].rb;
                if (rb == null) continue;
                rb.mass = _toes[i].baseMass * cfgToeMassMult.Value;
                rb.angularDrag = cfgToeAngularDrag.Value > 0f ? cfgToeAngularDrag.Value : _toes[i].baseAngularDrag;
                rb.drag = cfgToeDrag.Value > 0f ? cfgToeDrag.Value : _toes[i].baseDrag;
            }
        }

        void ApplyPenis()
        {
            for (int i = 0; i < _penis.Count; i++)
            {
                Rigidbody rb = _penis[i].rb;
                if (rb != null) rb.mass = _penis[i].baseMass * cfgPenisMassMult.Value;
            }
        }

        static void RestoreList(List<Entry> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Rigidbody rb = list[i].rb;
                if (rb != null)
                {
                    rb.mass = list[i].baseMass;
                    rb.drag = list[i].baseDrag;
                    rb.angularDrag = list[i].baseAngularDrag;
                }
            }
        }

        void OnMassToggle()
        {
            if (!_initialized) return;
            if (!cfgMassEnabled.Value)
            {
                RestoreList(_toes);
                RestoreList(_penis);
            }
            // When turned back on, FixedUpdate re-applies on the next physics step.
        }

        void FixedUpdate()
        {
            if (!_initialized || _person == null) return;
            if (cfgMassEnabled.Value)
            {
                ApplyToes();
                ApplyPenis();
            }
        }

        void OnDestroy()
        {
            if (SuperController.singleton != null)
                SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
            RestoreList(_toes);
            RestoreList(_penis);
            RestoreFriction();
        }
    }
}
