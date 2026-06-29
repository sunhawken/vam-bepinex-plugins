// AutoBulger v7 — BepInEx edition
// Combined: AutoBellyBulger + AutoThroatBulger + AutoGenExpansion
// Original plugins by Saking55 (modified from Captain Varghoss / VRAdultFun originals).
// Auto-selects every female Person atom in the scene. Settings: BepInEx/config/com.saking55.autobulger.cfg

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Saking55
{
    [BepInPlugin("com.saking55.autobulger", "AutoBulger", "7.0.0")]
    public class AutoBulgerPlugin : BaseUnityPlugin
    {
        // ── Per-person runtime state ────────────────────────────────────────────
        class PersonState
        {
            public Atom person;

            // Belly
            public FreeControllerV3 bellyChest;
            public DAZMorph b1, b2, b3, b4, b5, b6, b7, b8;
            public CollisionTrigger bTrigVag, bTrigVagD, bTrigVagDD;
            public CollisionTriggerEventHandler bEvt1, bEvt2, bEvt3;
            public Dictionary<Collider, bool> bCol = new Dictionary<Collider, bool>();
            public long bLastCol;
            public float bFloat = 1f, bVel;
            public Collider bTemp, bDistTarget;
            public bool bNotReset = true, bLoaded = true;
            public bool bPrevExtreme;

            // Throat
            public FreeControllerV3 throatNeck;
            public DAZMorph t1, t2, t3, t4;
            public CollisionTrigger tTrigMouth, tTrigThroat;
            public CollisionTriggerEventHandler tEvt1, tEvt2;
            public Dictionary<Collider, bool> tCol = new Dictionary<Collider, bool>();
            public long tLastCol;
            public float tFloat = 1f, tVel;
            public Collider tTemp, tDistTarget;
            public bool tNotReset = true, tLoaded = true;

            // Gen Expansion
            public FreeControllerV3 genChest;
            public DAZMorph gAnal1, gAnal2, gAnal3, gVag1, gVag2, gVag3;
            public CollisionTrigger gTrigVag, gTrigVagD, gTrigVagDD;
            public CollisionTriggerEventHandler gEvt1, gEvt2, gEvt3;
            public Dictionary<Collider, bool> gCol = new Dictionary<Collider, bool>();
            public long gLastCol;
            public float gFloat = 1f, gFloatA = 1f, gFloatV = 1f;
            public float gVel, gVelA, gVelV;
            public Collider gTemp, gDistTarget;
            public bool gNotReset = true, gLoaded = true;
        }

        // ── Person selection ──────────────────────────────────────────────────
        ConfigEntry<string> cfgPersonUID;
        readonly List<PersonState> _people = new List<PersonState>();

        // ── Belly Bulger config ───────────────────────────────────────────────
        ConfigEntry<bool>   cfgBellyEnabled;
        ConfigEntry<bool>   cfgBellyClamp;
        ConfigEntry<bool>   cfgBellyExtreme;
        ConfigEntry<bool>   cfgBellyDebug;
        ConfigEntry<float>  cfgBellyDebugDepth;
        ConfigEntry<float>  cfgBellyMult;
        ConfigEntry<float>  cfgBellyMinDistMult;
        ConfigEntry<float>  cfgBellySmoothing;
        ConfigEntry<float>  cfgBellyOfsX, cfgBellyOfsY, cfgBellyOfsZ;
        ConfigEntry<bool>   cfgBellyFilterActive;
        ConfigEntry<string> cfgBellyFilter;
        ConfigEntry<float>  cfgB1, cfgB2, cfgB3, cfgB4, cfgB5, cfgB6, cfgB7, cfgB8;

        // ── Throat Bulger config ──────────────────────────────────────────────
        ConfigEntry<bool>   cfgThroatEnabled;
        ConfigEntry<bool>   cfgThroatClamp;
        ConfigEntry<bool>   cfgThroatDebug;
        ConfigEntry<float>  cfgThroatDebugDepth;
        ConfigEntry<float>  cfgThroatMult;
        ConfigEntry<float>  cfgThroatMinDistMult;
        ConfigEntry<float>  cfgThroatSmoothing;
        ConfigEntry<float>  cfgThroatOfsX, cfgThroatOfsY, cfgThroatOfsZ;
        ConfigEntry<bool>   cfgThroatFilterActive;
        ConfigEntry<string> cfgThroatFilter;
        ConfigEntry<float>  cfgT1, cfgT2, cfgT3, cfgT4;

        // ── Gen Expansion config ──────────────────────────────────────────────
        ConfigEntry<bool>   cfgGenEnabled;
        ConfigEntry<bool>   cfgGenClamp;
        ConfigEntry<bool>   cfgGenAnal;
        ConfigEntry<bool>   cfgGenVag;
        ConfigEntry<bool>   cfgGenDebug;
        ConfigEntry<float>  cfgGenDebugDepth;
        ConfigEntry<float>  cfgGenMult;
        ConfigEntry<float>  cfgGenMinDistMult;
        ConfigEntry<float>  cfgGenSmoothing;
        ConfigEntry<float>  cfgGenOfsX, cfgGenOfsY, cfgGenOfsZ;
        ConfigEntry<bool>   cfgGenFilterActive;
        ConfigEntry<string> cfgGenFilter;
        ConfigEntry<float>  cfgGAnalMult, cfgGVagMult, cfgGVagOpen;

        bool _initialized;
        new ManualLogSource Logger => base.Logger;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            BindConfig();
            StartCoroutine(StartupCoroutine());
            Logger.LogInfo("AutoBulger BepInEx plugin loaded.");
        }

        void BindConfig()
        {
            const string G = "General";
            const string BL = "BellyBulger";
            const string TH = "ThroatBulger";
            const string GE = "GenExpansion";

            cfgPersonUID = Config.Bind(G, "PersonUID", "Auto",
                "UID of Person atom to apply morphs to. \"Auto\" = every female Person atom in the scene.");

            // Belly
            cfgBellyEnabled     = Config.Bind(BL, "Enabled",          true);
            cfgBellyClamp       = Config.Bind(BL, "LimitMorphs",       true);
            cfgBellyExtreme     = Config.Bind(BL, "ExtremeMode",       false, "Use X-morph set instead of standard.");
            cfgBellyDebug       = Config.Bind(BL, "ManualDepth",       false);
            cfgBellyDebugDepth  = Config.Bind(BL, "ManualDepthValue",  0f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgBellyMult        = Config.Bind(BL, "BellyBulgeMult",    1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));
            cfgBellyMinDistMult = Config.Bind(BL, "MinBulgeDistMult",  5f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 5f)));
            cfgBellySmoothing   = Config.Bind(BL, "MorphSmoothing",    0.02f,new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgBellyOfsX        = Config.Bind(BL, "TargetOffsetX",     0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgBellyOfsY        = Config.Bind(BL, "TargetOffsetY",     0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgBellyOfsZ        = Config.Bind(BL, "TargetOffsetZ",     0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgBellyFilterActive= Config.Bind(BL, "FilterAtomName",    false);
            cfgBellyFilter      = Config.Bind(BL, "FilterString",      "Person");
            cfgB1 = Config.Bind(BL, "Bulge1Mult", 0.32f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB2 = Config.Bind(BL, "Bulge2Mult", 0.67f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB3 = Config.Bind(BL, "Bulge3Mult", 0.9f,  new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB4 = Config.Bind(BL, "Bulge4Mult", 1.0f,  new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB5 = Config.Bind(BL, "Bulge5Mult", 0.86f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB6 = Config.Bind(BL, "Bulge6Mult", 0.7f,  new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB7 = Config.Bind(BL, "Bulge7Mult", 0.39f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgB8 = Config.Bind(BL, "Bulge8Mult", 0.18f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));

            // Throat
            cfgThroatEnabled     = Config.Bind(TH, "Enabled",          true);
            cfgThroatClamp       = Config.Bind(TH, "LimitMorphs",      true);
            cfgThroatDebug       = Config.Bind(TH, "ManualDepth",      false);
            cfgThroatDebugDepth  = Config.Bind(TH, "ManualDepthValue", 1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgThroatMult        = Config.Bind(TH, "ThroatBulgeMult",  1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgThroatMinDistMult = Config.Bind(TH, "MinBulgeDistMult", 2f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 5f)));
            cfgThroatSmoothing   = Config.Bind(TH, "MorphSmoothing",   0.1f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgThroatOfsX        = Config.Bind(TH, "TargetOffsetX",    0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgThroatOfsY        = Config.Bind(TH, "TargetOffsetY",    0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgThroatOfsZ        = Config.Bind(TH, "TargetOffsetZ",    0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgThroatFilterActive= Config.Bind(TH, "FilterAtomName",   false);
            cfgThroatFilter      = Config.Bind(TH, "FilterString",     "Person");
            cfgT1 = Config.Bind(TH, "Bulge1Mult", 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgT2 = Config.Bind(TH, "Bulge2Mult", 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgT3 = Config.Bind(TH, "Bulge3Mult", 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgT4 = Config.Bind(TH, "Bulge4Mult", 0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));

            // Gen expansion
            cfgGenEnabled      = Config.Bind(GE, "Enabled",            true);
            cfgGenClamp        = Config.Bind(GE, "LimitMorphs",        true);
            cfgGenAnal         = Config.Bind(GE, "AnalExpEnabled",     false);
            cfgGenVag          = Config.Bind(GE, "VaginalExpEnabled",  true);
            cfgGenDebug        = Config.Bind(GE, "ManualDepth",        false);
            cfgGenDebugDepth   = Config.Bind(GE, "ManualDepthValue",   1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgGenMult         = Config.Bind(GE, "GenExpMult",         1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));
            cfgGenMinDistMult  = Config.Bind(GE, "MinExpDistMult",     5f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 5f)));
            cfgGenSmoothing    = Config.Bind(GE, "MorphSmoothing",     0.05f,new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            cfgGenOfsX         = Config.Bind(GE, "TargetOffsetX",      0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgGenOfsY         = Config.Bind(GE, "TargetOffsetY",      0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgGenOfsZ         = Config.Bind(GE, "TargetOffsetZ",      0f,   new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f)));
            cfgGenFilterActive = Config.Bind(GE, "FilterAtomName",     false);
            cfgGenFilter       = Config.Bind(GE, "FilterString",       "Person");
            cfgGAnalMult       = Config.Bind(GE, "AnalMorphMult",      1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));
            cfgGVagMult        = Config.Bind(GE, "VaginalMorphMult",   1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));
            cfgGVagOpen        = Config.Bind(GE, "VagOpenMult",        1f,   new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));

            // Extreme mode change needs morph swap on every tracked person
            cfgBellyExtreme.SettingChanged += (_, __) =>
            {
                foreach (var p in _people) { p.bNotReset = true; p.bPrevExtreme = !cfgBellyExtreme.Value; }
            };
            cfgPersonUID.SettingChanged += (_, __) =>
            {
                if (_initialized) StartCoroutine(SelectAllPersonsCoroutine());
            };
        }

        IEnumerator StartupCoroutine()
        {
            while (SuperController.singleton == null) yield return null;
            while (SuperController.singleton.isLoading) yield return new WaitForSeconds(0.5f);
            yield return null;

            SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;
            _initialized = true;
            yield return SelectAllPersonsCoroutine();
        }

        void OnDestroy()
        {
            if (SuperController.singleton != null)
                SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
            ResetAllPeople();
        }

        void OnSceneLoaded()
        {
            StartCoroutine(SelectAllPersonsCoroutine());
        }

        List<string> ResolvePersonUIDs()
        {
            if (cfgPersonUID.Value == "Auto" || string.IsNullOrEmpty(cfgPersonUID.Value))
            {
                var ps = SuperController.singleton.GetAtoms().FindAll(a => {
                    if (a.type != "Person") return false;
                    var cs = a.GetStorableByID("geometry") as DAZCharacterSelector;
                    return cs != null && cs.gender == DAZCharacterSelector.Gender.Female;
                });
                return ps.ConvertAll(a => a.uid);
            }
            return new List<string> { cfgPersonUID.Value };
        }

        IEnumerator SelectAllPersonsCoroutine()
        {
            ResetAllPeople();
            _people.Clear();
            yield return null;
            yield return null;

            foreach (var uid in ResolvePersonUIDs())
            {
                if (string.IsNullOrEmpty(uid)) continue;
                var atom = SuperController.singleton.GetAtomByUid(uid);
                if (atom == null) { Logger.LogWarning("AutoBulger: Person '" + uid + "' not found."); continue; }

                var p = new PersonState { person = atom, bPrevExtreme = !cfgBellyExtreme.Value };
                SetupBelly(p);
                SetupThroat(p);
                SetupGen(p);
                _people.Add(p);
                Logger.LogInfo("AutoBulger: Attached to '" + uid + "'.");
            }
        }

        void ResetAllPeople()
        {
            foreach (var p in _people)
            {
                p.bNotReset = true; BellyResetOnce(p);
                p.tNotReset = true; ThroatResetOnce(p);
                p.gNotReset = true; GenResetOnce(p);
            }
        }

        // ── Setup helpers ─────────────────────────────────────────────────────

        void SetupBelly(PersonState p)
        {
            try
            {
                p.bTrigVag   = p.person.GetStorableByID("VaginaTrigger")      as CollisionTrigger;
                p.bTrigVagD  = p.person.GetStorableByID("DeepVaginaTrigger")  as CollisionTrigger;
                p.bTrigVagDD = p.person.GetStorableByID("DeeperVaginaTrigger")as CollisionTrigger;
                EnableTrigger(p.bTrigVag,   out p.bEvt1);
                EnableTrigger(p.bTrigVagD,  out p.bEvt2);
                EnableTrigger(p.bTrigVagDD, out p.bEvt3);
                p.bellyChest = p.person.GetStorableByID("chestControl") as FreeControllerV3;
                LoadBellyMorphs(p);
            }
            catch (Exception e) { Logger.LogError("AutoBulger SetupBelly: " + e); }
        }

        void LoadBellyMorphs(PersonState p)
        {
            if (p.person == null) return;
            var morphUI = MorphUI(p.person);
            if (morphUI == null) return;
            string pfx = cfgBellyExtreme.Value ? "AutoBulger Belly X " : "AutoBulger Belly ";
            try
            {
                ResetBellyMorphs(p);
                p.b1 = morphUI.GetMorphByDisplayName(pfx + "1");
                p.b2 = morphUI.GetMorphByDisplayName(pfx + "2");
                p.b3 = morphUI.GetMorphByDisplayName(pfx + "3");
                p.b4 = morphUI.GetMorphByDisplayName(pfx + "4");
                p.b5 = morphUI.GetMorphByDisplayName(pfx + "5");
                p.b6 = morphUI.GetMorphByDisplayName(pfx + "6");
                p.b7 = morphUI.GetMorphByDisplayName(pfx + "7");
                p.b8 = morphUI.GetMorphByDisplayName(pfx + "8");
                p.b1.Reset(); p.b2.Reset(); p.b3.Reset(); p.b4.Reset();
                p.b5.Reset(); p.b6.Reset(); p.b7.Reset(); p.b8.Reset();
                p.bLoaded = true;
                p.bPrevExtreme = cfgBellyExtreme.Value;
            }
            catch (Exception)
            {
                Logger.LogError("AutoBulger: Belly morphs not found for '" + p.person.name + "' — belly disabled.");
                p.bLoaded = false;
            }
        }

        void SetupThroat(PersonState p)
        {
            try
            {
                p.tTrigMouth  = p.person.GetStorableByID("MouthTrigger")  as CollisionTrigger;
                p.tTrigThroat = p.person.GetStorableByID("ThroatTrigger") as CollisionTrigger;
                EnableTrigger(p.tTrigMouth,  out p.tEvt1);
                EnableTrigger(p.tTrigThroat, out p.tEvt2);
                p.throatNeck = p.person.GetStorableByID("neckControl") as FreeControllerV3;
                var morphUI = MorphUI(p.person);
                if (morphUI == null) return;
                try
                {
                    p.t1 = morphUI.GetMorphByDisplayName("AutoBulger Throat 1"); p.t1.Reset();
                    p.t2 = morphUI.GetMorphByDisplayName("AutoBulger Throat 2"); p.t2.Reset();
                    p.t3 = morphUI.GetMorphByDisplayName("AutoBulger Throat 3"); p.t3.Reset();
                    p.t4 = morphUI.GetMorphByDisplayName("AutoBulger Throat 4"); p.t4.Reset();
                    p.tLoaded = true;
                }
                catch (Exception)
                {
                    Logger.LogError("AutoBulger: Throat morphs not found for '" + p.person.name + "' — throat disabled.");
                    p.tLoaded = false;
                }
            }
            catch (Exception e) { Logger.LogError("AutoBulger SetupThroat: " + e); }
        }

        void SetupGen(PersonState p)
        {
            try
            {
                p.gTrigVag   = p.person.GetStorableByID("VaginaTrigger")      as CollisionTrigger;
                p.gTrigVagD  = p.person.GetStorableByID("DeepVaginaTrigger")  as CollisionTrigger;
                p.gTrigVagDD = p.person.GetStorableByID("DeeperVaginaTrigger")as CollisionTrigger;
                EnableTrigger(p.gTrigVag,   out p.gEvt1);
                EnableTrigger(p.gTrigVagD,  out p.gEvt2);
                EnableTrigger(p.gTrigVagDD, out p.gEvt3);
                p.genChest = p.person.GetStorableByID("chestControl") as FreeControllerV3;
                var genUI   = GenMorphUI(p.person);
                var morphUI = MorphUI(p.person);
                if (genUI == null && morphUI == null) return;
                try
                {
                    // Anal 1-3 and Vag 1-2 are in female_genitalia/ → genUI
                    // Vag 3 is in female/ → morphUI (misplaced in the .var)
                    var g = genUI ?? morphUI;
                    var f = morphUI ?? genUI;
                    p.gAnal1 = g.GetMorphByDisplayName("AutoGenExp Anal 1"); p.gAnal1.Reset();
                    p.gAnal2 = g.GetMorphByDisplayName("AutoGenExp Anal 2"); p.gAnal2.Reset();
                    p.gAnal3 = g.GetMorphByDisplayName("AutoGenExp Anal 3"); p.gAnal3.Reset();
                    p.gVag1  = g.GetMorphByDisplayName("AutoGenExp Vag 1");  p.gVag1.Reset();
                    p.gVag2  = g.GetMorphByDisplayName("AutoGenExp Vag 2");  p.gVag2.Reset();
                    p.gVag3  = f.GetMorphByDisplayName("AutoGenExp Vag 3");  p.gVag3.Reset();
                    p.gLoaded = true;
                }
                catch (Exception)
                {
                    Logger.LogError("AutoBulger: Gen expansion morphs not found for '" + p.person.name + "' — gen disabled.");
                    p.gLoaded = false;
                }
            }
            catch (Exception e) { Logger.LogError("AutoBulger SetupGen: " + e); }
        }

        static void EnableTrigger(CollisionTrigger t, out CollisionTriggerEventHandler evt)
        {
            evt = null;
            if (t == null) return;
            t.triggerEnabledJSON.val = true;
            evt = t.GetComponent<CollisionTriggerEventHandler>();
        }

        static GenerateDAZMorphsControlUI MorphUI(Atom person)
        {
            return (person.GetStorableByID("geometry") as DAZCharacterSelector)?.morphsControlUI;
        }

        static GenerateDAZMorphsControlUI GenMorphUI(Atom person)
        {
            // female_genitalia/ morphs live in a separate bank; cast through object to
            // bypass the compile-time JSONStorable→GenerateDAZMorphsControlUI restriction
            var raw = person.GetStorableByID("morphsControlFemaleGenitalia");
            if (raw != null)
            {
                try { return (GenerateDAZMorphsControlUI)(object)raw; }
                catch { }
            }
            return MorphUI(person);
        }

        // ── FixedUpdate ───────────────────────────────────────────────────────

        void FixedUpdate()
        {
            if (!_initialized) return;

            foreach (var p in _people)
            {
                if (p.person == null) continue;

                if (cfgBellyEnabled.Value && p.bLoaded) FixedUpdateBelly(p);
                else BellyResetOnce(p);

                if (cfgThroatEnabled.Value && p.tLoaded) FixedUpdateThroat(p);
                else ThroatResetOnce(p);

                if (cfgGenEnabled.Value && p.gLoaded) FixedUpdateGen(p);
                else GenResetOnce(p);
            }
        }

        // ── Belly ─────────────────────────────────────────────────────────────

        void FixedUpdateBelly(PersonState p)
        {
            if (cfgBellyExtreme.Value != p.bPrevExtreme)
            {
                p.bNotReset = true;
                LoadBellyMorphs(p);
                return;
            }

            try
            {
                UpdateCollisions(p.bTrigVag, p.bTrigVagD, p.bTrigVagDD,
                    p.bEvt1, p.bEvt2, p.bEvt3, p.bCol, ref p.bLastCol, 2000);

                string targetName = "None";
                float  minDist    = 10f;

                foreach (var pair in p.bCol.Reverse())
                {
                    var atom = GetPairAtom(pair.Key);
                    if (atom == null || atom.name == p.person.name) continue;
                    if (!PassesFilter(atom.name, cfgBellyFilterActive.Value, cfgBellyFilter.Value)) continue;
                    float d = Vector3.Distance(p.bellyChest.followWhenOff.position, pair.Key.attachedRigidbody.position);
                    if (d < minDist) { p.bTemp = pair.Key; minDist = d; targetName = atom.name; }
                }

                p.bDistTarget = p.bTemp;
                if (targetName == "None")
                {
                    minDist = 100f;
                }
                else
                {
                    var ofs = new Vector3(cfgBellyOfsX.Value, cfgBellyOfsY.Value, cfgBellyOfsZ.Value);
                    float od = Vector3.Distance(p.bellyChest.followWhenOff.position, p.bDistTarget.attachedRigidbody.position + p.bDistTarget.attachedRigidbody.rotation * ofs);
                    float td = Vector3.Distance(p.bellyChest.followWhenOff.position, p.bTemp.attachedRigidbody.position      + p.bTemp.attachedRigidbody.rotation * ofs);
                    minDist = Mathf.Min(td, od);
                }

                p.bNotReset = true;

                p.bFloat = targetName == "None"
                    ? Mathf.SmoothDamp(p.bFloat, 1f, ref p.bVel, cfgBellySmoothing.Value)
                    : Mathf.SmoothDamp(p.bFloat, Remapp(minDist, 0.23f, 0.43f, 0f, 0.7f), ref p.bVel, cfgBellySmoothing.Value);

                if (cfgBellyDebug.Value) p.bFloat = cfgBellyDebugDepth.Value;

                BCalc(p.b1, p.bFloat, 0.150f, cfgB1.Value);
                BCalc(p.b2, p.bFloat, 0.130f, cfgB2.Value);
                BCalc(p.b3, p.bFloat, 0.110f, cfgB3.Value);
                BCalc(p.b4, p.bFloat, 0.090f, cfgB4.Value);
                BCalc(p.b5, p.bFloat, 0.070f, cfgB5.Value);
                BCalc(p.b6, p.bFloat, 0.050f, cfgB6.Value);
                BCalc(p.b7, p.bFloat, 0.030f, cfgB7.Value);
                BCalc(p.b8, p.bFloat, 0.025f, cfgB8.Value);
            }
            catch (Exception e) { Logger.LogError("AutoBulger Belly: " + e); }
        }

        void BCalc(DAZMorph m, float d, float dist0, float bMult)
        {
            if (m == null) return;
            m.morphValue = Mathf.Max((1f - d / (dist0 * cfgBellyMinDistMult.Value)) * cfgBellyMult.Value * bMult, 0f);
            if (cfgBellyClamp.Value) m.morphValue = Mathf.Clamp(m.morphValue, 0f, 1f);
        }

        void ResetBellyMorphs(PersonState p)
        {
            if (p.b1 != null) p.b1.Reset(); if (p.b2 != null) p.b2.Reset();
            if (p.b3 != null) p.b3.Reset(); if (p.b4 != null) p.b4.Reset();
            if (p.b5 != null) p.b5.Reset(); if (p.b6 != null) p.b6.Reset();
            if (p.b7 != null) p.b7.Reset(); if (p.b8 != null) p.b8.Reset();
        }

        void BellyResetOnce(PersonState p) { if (!p.bNotReset) return; ResetBellyMorphs(p); p.bNotReset = false; }

        // ── Throat ────────────────────────────────────────────────────────────

        void FixedUpdateThroat(PersonState p)
        {
            try
            {
                UpdateCollisions(p.tTrigMouth, p.tTrigThroat, null,
                    p.tEvt1, p.tEvt2, null, p.tCol, ref p.tLastCol, 500);

                string targetName = "None";
                float  minDist    = 10f;

                foreach (var pair in p.tCol)
                {
                    var atom = GetPairAtom(pair.Key);
                    if (atom == null || atom.name == p.person.name) continue;
                    if (!PassesFilter(atom.name, cfgThroatFilterActive.Value, cfgThroatFilter.Value)) continue;
                    float d = Vector3.Distance(p.throatNeck.followWhenOff.position, pair.Key.attachedRigidbody.position);
                    if (d < minDist) { p.tTemp = pair.Key; minDist = d; targetName = atom.name; }
                }

                if (targetName == "None")
                {
                    p.tDistTarget = null;
                    minDist = 10f;
                }

                if (p.tDistTarget == null)
                    p.tDistTarget = p.tTemp;
                else if (p.tTemp != null)
                {
                    var ofs = new Vector3(cfgThroatOfsX.Value, cfgThroatOfsY.Value, cfgThroatOfsZ.Value);
                    float od = Vector3.Distance(p.throatNeck.followWhenOff.position, p.tDistTarget.attachedRigidbody.position + p.tDistTarget.attachedRigidbody.rotation * ofs);
                    float td = Vector3.Distance(p.throatNeck.followWhenOff.position, p.tTemp.attachedRigidbody.position       + p.tTemp.attachedRigidbody.rotation * ofs);
                    minDist = td < od ? td : od;
                }

                p.tNotReset = true;

                p.tFloat = targetName == "None"
                    ? Mathf.SmoothDamp(p.tFloat, 1f, ref p.tVel, cfgThroatSmoothing.Value)
                    : Mathf.SmoothDamp(p.tFloat, Remapp(minDist, 0.07f, 0.19f, 0f, 0.8f), ref p.tVel, cfgThroatSmoothing.Value);

                if (cfgThroatDebug.Value) p.tFloat = cfgThroatDebugDepth.Value;

                TCalc(p.t1, p.tFloat, 0.40f, cfgT1.Value);
                TCalc(p.t2, p.tFloat, 0.20f, cfgT2.Value);
                TCalc(p.t3, p.tFloat, 0.15f, cfgT3.Value);
                TCalc(p.t4, p.tFloat, 0.40f, cfgT4.Value);
            }
            catch (Exception e) { Logger.LogError("AutoBulger Throat: " + e); }
        }

        void TCalc(DAZMorph m, float d, float dist0, float bMult)
        {
            if (m == null) return;
            m.morphValue = Mathf.Max((1f - d / (dist0 * cfgThroatMinDistMult.Value)) * cfgThroatMult.Value * bMult, 0f);
            if (cfgThroatClamp.Value) m.morphValue = Mathf.Clamp(m.morphValue, 0f, 1f);
        }

        void ThroatResetOnce(PersonState p)
        {
            if (!p.tNotReset) return;
            if (p.t1 != null) p.t1.Reset(); if (p.t2 != null) p.t2.Reset();
            if (p.t3 != null) p.t3.Reset(); if (p.t4 != null) p.t4.Reset();
            p.tNotReset = false;
        }

        // ── Gen Expansion ─────────────────────────────────────────────────────

        void FixedUpdateGen(PersonState p)
        {
            try
            {
                UpdateCollisions(p.gTrigVag, p.gTrigVagD, p.gTrigVagDD,
                    p.gEvt1, p.gEvt2, p.gEvt3, p.gCol, ref p.gLastCol, 2000);

                string targetName = "None";
                float  minDist    = 10f;

                foreach (var pair in p.gCol)
                {
                    var atom = GetPairAtom(pair.Key);
                    if (atom == null || atom.name == p.person.name) continue;
                    if (!PassesFilter(atom.name, cfgGenFilterActive.Value, cfgGenFilter.Value)) continue;
                    float d = Vector3.Distance(p.genChest.followWhenOff.position, pair.Key.attachedRigidbody.position);
                    if (d < minDist) { p.gTemp = pair.Key; minDist = d; targetName = atom.name; }
                }

                if (targetName == "None")
                {
                    p.gDistTarget = null;
                    minDist = 10f;
                }

                if (p.gDistTarget == null)
                    p.gDistTarget = p.gTemp;
                else if (p.gTemp != null)
                {
                    var ofs = new Vector3(cfgGenOfsX.Value, cfgGenOfsY.Value, cfgGenOfsZ.Value);
                    float od = Vector3.Distance(p.genChest.followWhenOff.position, p.gDistTarget.attachedRigidbody.position + p.gDistTarget.attachedRigidbody.rotation * ofs);
                    float td = Vector3.Distance(p.genChest.followWhenOff.position, p.gTemp.attachedRigidbody.position       + p.gTemp.attachedRigidbody.rotation * ofs);
                    minDist = td < od ? td : od;
                }

                p.gNotReset = true;

                if (targetName == "None")
                {
                    p.gFloatV = Mathf.SmoothDamp(p.gFloatV, 1f, ref p.gVelV, cfgGenSmoothing.Value);
                    p.gFloatA = Mathf.SmoothDamp(p.gFloatA, 1f, ref p.gVelA, cfgGenSmoothing.Value);
                    p.gFloat  = Mathf.SmoothDamp(p.gFloat,  1f, ref p.gVel,  cfgGenSmoothing.Value);
                }
                else
                {
                    float mapped = Remapp(minDist, 0.2f, 0.35f, 0f, 0.7f);
                    p.gFloatA = Mathf.SmoothDamp(p.gFloatA, cfgGenAnal.Value ? mapped : 1f, ref p.gVelA, cfgGenSmoothing.Value);
                    p.gFloatV = Mathf.SmoothDamp(p.gFloatV, cfgGenVag.Value  ? mapped : 1f, ref p.gVelV, cfgGenSmoothing.Value);
                    p.gFloat  = Mathf.SmoothDamp(p.gFloat,  mapped,                          ref p.gVel,  cfgGenSmoothing.Value);
                }

                if (cfgGenDebug.Value)
                {
                    p.gFloat = cfgGenDebugDepth.Value;
                    if (cfgGenAnal.Value) p.gFloatA = cfgGenDebugDepth.Value;
                    if (cfgGenVag.Value)  p.gFloatV = cfgGenDebugDepth.Value;
                }

                GCalc(p.gAnal3, p.gFloatA, 0.2f, cfgGAnalMult.Value, 3f);
                GCalc(p.gAnal1, p.gFloatA, 0.2f, cfgGAnalMult.Value, 3f);
                GCalc(p.gAnal2, p.gFloatA, 0.2f, cfgGAnalMult.Value, 0.3f);
                GCalc(p.gVag1,  p.gFloatV, 0.1f, cfgGVagOpen.Value,  0.3f);
                GCalc(p.gVag2,  p.gFloatV, 0.2f, cfgGVagMult.Value,  1f);
                GCalc(p.gVag3,  p.gFloatV, 0.2f, cfgGVagMult.Value,  2f);
            }
            catch (Exception e) { Logger.LogError("AutoBulger Gen: " + e); }
        }

        void GCalc(DAZMorph m, float d, float dist0, float bMult, float maxVal = 1f)
        {
            if (m == null) return;
            m.morphValue = Mathf.Max((1f - d / (dist0 * cfgGenMinDistMult.Value)) * cfgGenMult.Value * bMult, 0f);
            if (cfgGenClamp.Value) m.morphValue = Mathf.Clamp(m.morphValue, 0f, maxVal);
        }

        void GenResetOnce(PersonState p)
        {
            if (!p.gNotReset) return;
            if (p.gAnal1 != null) p.gAnal1.Reset(); if (p.gAnal2 != null) p.gAnal2.Reset(); if (p.gAnal3 != null) p.gAnal3.Reset();
            if (p.gVag1  != null) p.gVag1.Reset();  if (p.gVag2  != null) p.gVag2.Reset();  if (p.gVag3  != null) p.gVag3.Reset();
            p.gNotReset = false;
        }

        // ── Shared collision tracking ─────────────────────────────────────────

        static void UpdateCollisions(
            CollisionTrigger t1, CollisionTrigger t2, CollisionTrigger t3,
            CollisionTriggerEventHandler e1, CollisionTriggerEventHandler e2, CollisionTriggerEventHandler e3,
            Dictionary<Collider, bool> dict, ref long lastUpdate, long timeout)
        {
            long now    = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            bool active = (t1 != null && t1.trigger.active)
                       || (t2 != null && t2.trigger.active)
                       || (t3 != null && t3.trigger.active);

            if (active)
            {
                IEnumerable<KeyValuePair<Collider, bool>> combined = new Dictionary<Collider, bool>();
                if (e1 != null) combined = combined.Concat(e1.collidingWithDictionary);
                if (e2 != null) combined = combined.Concat(e2.collidingWithDictionary);
                if (e3 != null) combined = combined.Concat(e3.collidingWithDictionary);

                var merged = combined.Concat(dict)
                    .GroupBy(kv => kv.Key)
                    .ToDictionary(g => g.Key, g => g.First().Value);
                dict.Clear();
                foreach (var kv in merged) dict[kv.Key] = kv.Value;
                lastUpdate = now;
            }
            else if (now > lastUpdate + timeout)
            {
                dict.Clear();
            }

            foreach (var item in dict.Where(f => f.Key == null || f.Key.attachedRigidbody == null).ToArray())
                dict.Remove(item.Key);
        }

        static Atom GetPairAtom(Collider col)
        {
            var rb = col?.attachedRigidbody;
            return rb == null ? null : rb.GetComponentInParent<Atom>();
        }

        static bool PassesFilter(string name, bool filterActive, string filter)
        {
            if (!filterActive) return true;
            return name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static float Remapp(float val, float f1, float t1, float f2, float t2)
        {
            return (val - f1) / (t1 - f1) * (t2 - f2) + f2;
        }
    }
}
