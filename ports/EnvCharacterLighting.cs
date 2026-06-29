// ===================================================
// EnvCharacterLighting — BepInEx edition
// Original author: dafalconer  v1.25
// BepInEx port: auto-selects first Person atom on scene load.
// Settings: BepInEx/config/com.dafalconer.envcharacterlighting.cfg
// No session plugin required.
// ===================================================

using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace dafalconer
{
    [BepInPlugin("com.dafalconer.envcharacterlighting", "EnvCharacterLighting", "1.25.0")]
    public class EnvCharacterLightingPlugin : BaseUnityPlugin
    {
        // ── static config constants (unchanged from original) ────────
        private static class Cfg
        {
            public const float RootMoveThreshold = 0.01f;
            public const float StillDelay = 0.5f;
            public const float MaxRayDistance = 25f;
            public const int   RayMask = ~(1 << 2);
            public const int   ScanRes = 32;

            public const float KeyDistBodySizeMultiplier = 2.0f;
            public const float KeyDistMinimum = 3.0f;
            public const float KeyMinDistMultiplier = 1.5f;
            public const float KeyMinDistMinimum = 2.0f;
            public const float KeyMinHeightAboveFloor = 1.0f;
            public const float WallClearance = 0.3f;
            public const float WallClearanceMin = 0.5f;
            public const int   LOSMaxSteps = 30;
            public const float LOSStepSize = 0.1f;
            public const int   LOSMinVisiblePoints = 2;
            public const float KeyYOffsetMin = -0.3f;
            public const float KeyYOffsetMax = 0.6f;

            public const float KeyIntensityBase = 1.2f;
            public const float SpotAnglePadding = 1.2f;
            public const float SpotAngleMin = 90f;
            public const float SpotAngleMax = 120f;
            public const float KeyShadowStrength = 0.175f;
            public const float KeypointBias = 0.014f;
            public const float MinLuminanceFloor = 0.50f;

            public const float AmbientStartHeightAboveFloor = 1.3f;
            public const float AmbientCeilingClearance = 1.0f;
            public const float AmbientPositionMaxAdjust = 2.0f;
            public const float AmbientPositionStepSize = 0.2f;
            public const int   AmbientHorizClearRequired = 4;
            public const float AmbientRangeMultiplier = 1.2f;
            public const float AmbientShadowStrength = 0.3f;
            public const float AmbientpointBias = 0.014f;
            public const float AmbientHorizClearDist = 0.8f;
            public const int   AmbientVertStepsBeforeHoriz = 1;
            public const float AmbientHorizClearMinDist = 1.5f;
            public const float AmbientHorizStepSize = 0.4f;
            public const float MinAmbientIntensity = 0.8f;
        }

        private struct BodyGeometry
        {
            public Vector3   Center;
            public float     BodySize;
            public Vector3   BodyForward;
            public Vector3   BodyRight;
            public float     FloorY;
            public Vector3   RaisedCenter;
            public Transform Head;
            public Transform Chest;
            public Transform Pelvis;
        }

        private struct RoomContext
        {
            public Color[] DirectionalColors;
            public float[] HorizHitDistances;
        }

        // ── BepInEx config ───────────────────────────────────────────
        ConfigEntry<string> cfgPersonUID;       // "Auto" = first person found
        ConfigEntry<float>  cfgIntensityBoost;
        ConfigEntry<float>  cfgColorSaturation;
        ConfigEntry<string> cfgLightCount;
        ConfigEntry<bool>   cfgFreezeRig;

        // ── runtime state ────────────────────────────────────────────
        Atom      _person;
        Transform _rootControl;

        Atom      _keyFrontAtom, _keyBackAtom, _keyLeftAtom, _keyRightAtom, _ambientAtom;
        JSONStorable _keyFrontStor, _keyBackStor, _keyLeftStor, _keyRightStor, _ambientStor;

        Vector3 _lastRootPos;
        float   _stillTimer;

        readonly RaycastHit[] _rayBuffer = new RaycastHit[16];

        GameObject    _camGO;
        Camera        _samplingCam;
        RenderTexture _rt;
        Texture2D     _readTex;

        bool _initialized;

        new ManualLogSource Logger => base.Logger;

        // ── lifecycle ────────────────────────────────────────────────

        void Awake()
        {
            cfgPersonUID = Config.Bind("General", "PersonUID", "Auto",
                "UID of the Person atom to light. Set to \"Auto\" to always use the first person found.");

            cfgIntensityBoost = Config.Bind("General", "IntensityBoost", 0f,
                new ConfigDescription("Added to base light intensity.", new AcceptableValueRange<float>(0f, 2f)));

            cfgColorSaturation = Config.Bind("General", "ColorSaturation", 0.5f,
                new ConfigDescription("Saturation multiplier applied to sampled colors.", new AcceptableValueRange<float>(0f, 2f)));

            cfgLightCount = Config.Bind("General", "LightCount", "4 Lights",
                new ConfigDescription("", new AcceptableValueList<string>("4 Lights", "3 Lights")));

            cfgFreezeRig = Config.Bind("General", "FreezeRig", false,
                "When true the lighting rig stops updating even if the person moves.");

            // Trigger recalc when any visual setting changes at runtime.
            cfgIntensityBoost.SettingChanged  += (_, __) => RecalculateRig();
            cfgColorSaturation.SettingChanged += (_, __) => RecalculateRig();
            cfgLightCount.SettingChanged      += (_, __) => RecalculateRig();
            cfgFreezeRig.SettingChanged       += (s, e) =>
            {
                if (!((SettingChangedEventArgs)e).ChangedSetting.BoxedValue.Equals(true))
                    RecalculateRig();
            };
            cfgPersonUID.SettingChanged += (_, __) =>
            {
                if (_initialized) StartCoroutine(SelectPersonCoroutine(ResolvePersonUID()));
            };

            StartCoroutine(StartupCoroutine());
            Logger.LogInfo("EnvCharacterLighting BepInEx plugin loaded.");
        }

        void OnDestroy()
        {
            if (SuperController.singleton != null)
            {
                SuperController.singleton.onAtomAddedHandlers   -= OnAtomAdded;
                SuperController.singleton.onAtomRemovedHandlers -= OnAtomRemoved;
                SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
            }
            DestroyLights();
            CleanupSamplingObjects();
        }

        IEnumerator StartupCoroutine()
        {
            while (SuperController.singleton == null) yield return null;

            SuperController.singleton.onAtomAddedHandlers   += OnAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers += OnAtomRemoved;
            SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;

            while (SuperController.singleton.isLoading) yield return new WaitForSeconds(0.5f);
            yield return null;

            CreateSamplingCamera();
            _initialized = true;
            yield return SelectPersonCoroutine(ResolvePersonUID());
        }

        void Update()
        {
            if (_person == null || _rootControl == null) return;
            if (cfgFreezeRig.Value) return;

            Vector3 pos      = _rootControl.position;
            float   movedSqr = (pos - _lastRootPos).sqrMagnitude;

            if (movedSqr > Cfg.RootMoveThreshold * Cfg.RootMoveThreshold)
            {
                _lastRootPos = pos;
                _stillTimer  = Cfg.StillDelay;
            }
            else if (_stillTimer > 0f)
            {
                _stillTimer -= Time.deltaTime;
                if (_stillTimer <= 0f)
                    RecalculateRig();
            }
        }

        // ── scene event handlers ─────────────────────────────────────

        void OnAtomAdded(Atom atom)
        {
            if (atom == null || atom.type != "Person") return;
            // Auto-mode: if we have no person yet, grab this one.
            if (_person == null && cfgPersonUID.Value == "Auto")
                StartCoroutine(SelectPersonCoroutine(atom.uid));
        }

        void OnAtomRemoved(Atom atom)
        {
            if (atom == null || atom.type != "Person") return;
            if (_person != null && atom.uid == _person.uid)
            {
                DestroyLights();
                _person      = null;
                _rootControl = null;

                // Auto-mode: fall back to another person if one exists.
                if (cfgPersonUID.Value == "Auto")
                {
                    string next = ResolvePersonUID();
                    if (!string.IsNullOrEmpty(next))
                        StartCoroutine(SelectPersonCoroutine(next));
                }
            }
        }

        void OnSceneLoaded()
        {
            if (_initialized)
                StartCoroutine(SelectPersonCoroutine(ResolvePersonUID()));
        }

        // ── person selection ─────────────────────────────────────────

        string ResolvePersonUID()
        {
            if (cfgPersonUID.Value == "Auto" || string.IsNullOrEmpty(cfgPersonUID.Value))
            {
                var persons = SuperController.singleton.GetAtoms().FindAll(a => a.type == "Person");
                return persons.Count > 0 ? persons[0].uid : "";
            }
            return cfgPersonUID.Value;
        }

        IEnumerator SelectPersonCoroutine(string uid)
        {
            DestroyLights();
            yield return null;
            yield return null;

            if (string.IsNullOrEmpty(uid)) yield break;

            _person = SuperController.singleton.GetAtomByUid(uid);
            if (_person == null) { Logger.LogWarning($"EnvCharacterLighting: Person '{uid}' not found."); yield break; }

            var mainCtrl = _person.mainController;
            if (mainCtrl == null) yield break;

            _rootControl = mainCtrl.transform;
            _lastRootPos = _rootControl.position;

            yield return CreateAllLights();
            RecalculateRig();
            Logger.LogInfo($"EnvCharacterLighting: Lighting rig attached to '{uid}'.");
        }

        // ── rig recalculation ────────────────────────────────────────

        void RecalculateRig()
        {
            if (_person == null || _rootControl == null) return;
            if (_keyFrontAtom == null || _keyBackAtom  == null ||
                _keyLeftAtom  == null || _keyRightAtom == null || _ambientAtom == null) return;

            BodyGeometry geo;
            if (!TryComputeBodyGeometry(out geo)) return;

            SetAllLightsToZero();
            RoomContext ctx = SampleRoomContext(geo);

            PlaceKeyLights(geo, ctx);
            ConfigureLightInternal(_ambientStor, geo);
        }

        bool TryComputeBodyGeometry(out BodyGeometry geo)
        {
            geo = new BodyGeometry();

            Transform head   = GetControl("headControl");
            Transform chest  = GetControl("chestControl");
            Transform pelvis = GetControl("pelvisControl");

            if (head == null || chest == null || pelvis == null) return false;

            Vector3 min = Vector3.Min(Vector3.Min(head.position, chest.position), pelvis.position);
            Vector3 max = Vector3.Max(Vector3.Max(head.position, chest.position), pelvis.position);

            if (!IsFinite(min.x) || !IsFinite(max.x)) return false;

            Vector3 center   = (min + max) * 0.5f;
            float   bodySize = (max - min).magnitude;
            if (bodySize < 0.1f) bodySize = 0.1f;

            Vector3 projected = Vector3.ProjectOnPlane(chest.forward, Vector3.up);
            if (projected.sqrMagnitude < 1e-6f)
                projected = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (projected.sqrMagnitude < 1e-6f)
                projected = Vector3.forward;

            Vector3 bodyForward = projected.normalized;
            Vector3 bodyRight   = Vector3.Cross(Vector3.up, bodyForward).normalized;

            float   floorY      = GetFloorY(center);
            float   lightHeight = Mathf.Max(chest.position.y, floorY + Cfg.KeyMinHeightAboveFloor);
            Vector3 raisedCenter = new Vector3(center.x, lightHeight, center.z);

            geo.Center       = center;
            geo.BodySize     = bodySize;
            geo.BodyForward  = bodyForward;
            geo.BodyRight    = bodyRight;
            geo.FloorY       = floorY;
            geo.RaisedCenter = raisedCenter;
            geo.Head         = head;
            geo.Chest        = chest;
            geo.Pelvis       = pelvis;
            return true;
        }

        RoomContext SampleRoomContext(BodyGeometry geo)
        {
            Vector3[] dirs = BuildLightDirections(geo);

            Color[] directionalColors  = new Color[dirs.Length];
            float[] horizHitDistances  = new float[dirs.Length];
            float   defaultKeyDist     = Mathf.Max(geo.BodySize * Cfg.KeyDistBodySizeMultiplier, Cfg.KeyDistMinimum);

            for (int i = 0; i < dirs.Length; i++)
            {
                RaycastHit hit;
                float dist = defaultKeyDist;
                if (Physics.Raycast(geo.RaisedCenter, dirs[i], out hit, Cfg.MaxRayDistance, Cfg.RayMask))
                    if (hit.distance < defaultKeyDist)
                        dist = Mathf.Max(hit.distance - Cfg.WallClearance, Cfg.WallClearanceMin);

                horizHitDistances[i]  = dist;
                directionalColors[i]  = SampleEnvironmentColor(geo.RaisedCenter, dirs[i]);
            }

            RaiseLuminanceFloor(directionalColors);

            RoomContext ctx;
            ctx.DirectionalColors = directionalColors;
            ctx.HorizHitDistances = horizHitDistances;
            return ctx;
        }

        void PlaceKeyLights(BodyGeometry geo, RoomContext ctx)
        {
            bool     fourLights  = cfgLightCount.Value == "4 Lights";
            int      lightCount  = fourLights ? 4 : 3;
            Vector3[] dirs       = BuildLightDirections(geo);

            Atom[]        keyAtoms = { _keyFrontAtom, _keyBackAtom, _keyLeftAtom, _keyRightAtom };
            JSONStorable[] keyStors = { _keyFrontStor, _keyBackStor, _keyLeftStor, _keyRightStor };

            float      minLightDist = Mathf.Max(geo.BodySize * Cfg.KeyMinDistMultiplier, Cfg.KeyMinDistMinimum);
            Transform[] losPoints   = { geo.Head, geo.Chest, geo.Pelvis };

            for (int i = 0; i < lightCount; i++)
            {
                Vector3 dir        = dirs[i];
                Vector3 initialPos = geo.RaisedCenter + dir * ctx.HorizHitDistances[i];

                float   distToCenter;
                Vector3 finalPos = AdjustLightForLineOfSight(initialPos, geo.RaisedCenter, dir, losPoints, geo.Center, minLightDist, out distToCenter);

                if (distToCenter < minLightDist)
                {
                    float deltaY        = geo.RaisedCenter.y - geo.Center.y;
                    float requiredHoriz = Mathf.Sqrt(minLightDist * minLightDist - deltaY * deltaY);
                    finalPos            = requiredHoriz > 0 ? geo.RaisedCenter + dir * requiredHoriz : geo.RaisedCenter;
                    distToCenter        = minLightDist;
                }

                float yOffset = UnityEngine.Random.Range(Cfg.KeyYOffsetMin, Cfg.KeyYOffsetMax);
                keyAtoms[i].mainController.transform.position = finalPos - new Vector3(0, yOffset, 0);
                keyAtoms[i].mainController.transform.LookAt(geo.Chest.position);

                Color finalColor = ApplySaturation(ctx.DirectionalColors[i], cfgColorSaturation.Value);
                float intensity  = Cfg.KeyIntensityBase + cfgIntensityBoost.Value;
                float coneAngle  = 2f * Mathf.Atan(geo.BodySize / (2f * distToCenter)) * Mathf.Rad2Deg;
                float spotAngle  = Mathf.Clamp(coneAngle * Cfg.SpotAnglePadding, Cfg.SpotAngleMin, Cfg.SpotAngleMax);
                float range      = distToCenter * 3f;

                SetLight(keyStors[i], intensity, spotAngle, range, ToHSV(finalColor), true, Cfg.KeyShadowStrength, Cfg.KeypointBias);
            }

            if (!fourLights) _keyRightStor.SetFloatParamValue("intensity", 0f);
        }

        void ConfigureLightInternal(JSONStorable ambientStor, BodyGeometry geo)
        {
            Vector3 startPos   = new Vector3(geo.Center.x, geo.FloorY + Cfg.AmbientStartHeightAboveFloor, geo.Center.z);
            Vector3 ambientPos = AdjustAmbientLightPosition(startPos);
            _ambientAtom.mainController.transform.position = ambientPos;

            Color[] ambientColors = { SampleAmbientDominantColor(ambientPos, geo.BodyForward, geo.BodyRight) };
            bool    wasLifted     = RaiseLuminanceFloor(ambientColors);
            Color   ambientColor  = ambientColors[0];

            float distToHead   = Vector3.Distance(ambientPos, geo.Head.position);
            float distToChest  = Vector3.Distance(ambientPos, geo.Chest.position);
            float distToPelvis = Vector3.Distance(ambientPos, geo.Pelvis.position);
            float ambientRange = Mathf.Max(distToHead, Mathf.Max(distToChest, distToPelvis)) * Cfg.AmbientRangeMultiplier;

            float ambientIntensity = Luminance(ambientColor) + cfgIntensityBoost.Value;
            if (wasLifted) ambientIntensity = Mathf.Max(ambientIntensity, Cfg.MinAmbientIntensity);

            SetLight(ambientStor, ambientIntensity, 80f, ambientRange, ToHSV(ambientColor), true, Cfg.AmbientShadowStrength, Cfg.AmbientpointBias);
        }

        // ── light geometry helpers ───────────────────────────────────

        Vector3[] BuildLightDirections(BodyGeometry geo)
        {
            if (cfgLightCount.Value == "4 Lights")
                return new[] { geo.BodyForward, -geo.BodyForward, -geo.BodyRight, geo.BodyRight };

            Vector3[] dirs = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f * Mathf.Deg2Rad;
                dirs[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }
            return dirs;
        }

        float GetFloorY(Vector3 origin)
        {
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, 10f, Cfg.RayMask))
                return hit.point.y;
            return _rootControl != null ? _rootControl.position.y : origin.y - 0.5f;
        }

        Vector3 AdjustLightForLineOfSight(Vector3 startPos, Vector3 raisedCenter, Vector3 outwardDir,
            Transform[] samplePoints, Vector3 characterCenter, float minLightDist, out float newDistance)
        {
            float   horizDist  = Vector3.ProjectOnPlane(startPos - raisedCenter, Vector3.up).magnitude;
            Vector3 currentPos = startPos;

            for (int step = 0; step < Cfg.LOSMaxSteps; step++)
            {
                int visibleCount = 0;
                foreach (Transform t in samplePoints)
                {
                    if (t == null) continue;
                    Vector3 toPoint = t.position - currentPos;
                    float   dist    = toPoint.magnitude;
                    if (dist < 0.001f) continue;

                    RaycastHit hit;
                    if (Physics.Raycast(currentPos, toPoint / dist, out hit, dist, Cfg.RayMask))
                    {
                        if (hit.collider.transform.IsChildOf(_person.transform)) visibleCount++;
                    }
                    else visibleCount++;
                }

                if (visibleCount >= Cfg.LOSMinVisiblePoints)
                {
                    newDistance = Vector3.Distance(currentPos, characterCenter);
                    return currentPos;
                }

                horizDist  = Mathf.Max(horizDist - Cfg.LOSStepSize, 0.01f);
                currentPos = raisedCenter + outwardDir * horizDist;
                currentPos.y = raisedCenter.y;
            }

            newDistance = Vector3.Distance(currentPos, characterCenter);
            return currentPos;
        }

        Vector3 AdjustAmbientLightPosition(Vector3 startPos)
        {
            Vector3 currentPos = startPos;
            int     maxSteps   = Mathf.RoundToInt(Cfg.AmbientPositionMaxAdjust / Cfg.AmbientPositionStepSize);
            float   maxY       = currentPos.y + Cfg.AmbientPositionMaxAdjust;

            RaycastHit ceilingHit;
            if (RaycastIgnoreSelf(currentPos, Vector3.up, 10f, out ceilingHit))
                maxY = ceilingHit.point.y - 0.1f;
            if (RaycastIgnoreSelf(currentPos, Vector3.up, Cfg.AmbientCeilingClearance, out ceilingHit))
                currentPos.y -= (Cfg.AmbientCeilingClearance - ceilingHit.distance) * 0.5f;

            Vector3[] horizDirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            Vector3   blockedDir = Vector3.zero;
            int       vertStepsTaken = 0;

            for (int step = 0; step < maxSteps; step++)
            {
                int clearCount = 0;
                blockedDir = Vector3.zero;
                foreach (Vector3 dir in horizDirs)
                {
                    RaycastHit hit;
                    if (!RaycastIgnoreSelf(currentPos, dir, Cfg.AmbientHorizClearDist, out hit)) clearCount++;
                    else if (blockedDir == Vector3.zero) blockedDir = dir;
                }

                if (clearCount >= Cfg.AmbientHorizClearRequired) break;

                if (vertStepsTaken < Cfg.AmbientVertStepsBeforeHoriz)
                {
                    currentPos.y = Mathf.Min(currentPos.y + Cfg.AmbientPositionStepSize, maxY);
                    vertStepsTaken++;
                    if (currentPos.y >= maxY) break;
                }
                else if (blockedDir != Vector3.zero)
                {
                    Vector3    oppositeDir = -blockedDir;
                    RaycastHit oppositeHit;
                    if (!RaycastIgnoreSelf(currentPos, oppositeDir, Cfg.AmbientHorizClearMinDist, out oppositeHit))
                        currentPos += oppositeDir * Cfg.AmbientHorizStepSize;
                    break;
                }
                else break;
            }

            return currentPos;
        }

        bool RaycastIgnoreSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit result)
        {
            int  hitCount    = Physics.RaycastNonAlloc(origin, dir, _rayBuffer, maxDist, Cfg.RayMask);
            float closestDist = maxDist;
            result = new RaycastHit();
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _rayBuffer[i];
                if (_person != null && hit.collider.transform.IsChildOf(_person.transform)) continue;
                if (hit.distance < closestDist) { closestDist = hit.distance; result = hit; found = true; }
            }
            return found;
        }

        // ── color sampling ───────────────────────────────────────────

        void CreateSamplingCamera()
        {
            _camGO = new GameObject("EnvColorSampler");
            _camGO.hideFlags = HideFlags.HideAndDontSave;

            _samplingCam                       = _camGO.AddComponent<Camera>();
            _samplingCam.clearFlags            = CameraClearFlags.Skybox;
            _samplingCam.allowHDR              = false;
            _samplingCam.allowMSAA             = false;
            _samplingCam.forceIntoRenderTexture = true;
            _samplingCam.enabled               = false;
            _samplingCam.fieldOfView           = 90f;
            _samplingCam.cullingMask           = ~((1 << 2) | (1 << 5));
            _samplingCam.stereoTargetEye       = StereoTargetEyeMask.None;

            _rt = new RenderTexture(Cfg.ScanRes, Cfg.ScanRes, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _rt.Create();
            _readTex = new Texture2D(Cfg.ScanRes, Cfg.ScanRes, TextureFormat.RGBA32, false, false);
        }

        Color SampleAmbientDominantColor(Vector3 origin, Vector3 forward, Vector3 right)
        {
            Vector3[] dirs =
            {
                forward,
                -forward,
                -right,
                right,
                Vector3.up,
                (forward  + Vector3.up).normalized,
                (-forward + Vector3.up).normalized,
            };

            Color weightedSum = Color.black;
            float totalWeight = 0f;

            for (int i = 0; i < dirs.Length; i++)
            {
                Color c   = SampleEnvironmentColorFull(origin, dirs[i]);
                float lum = Luminance(c);
                float w   = lum * lum;
                weightedSum += c * w;
                totalWeight += w;
            }

            Color dominant = totalWeight > 0.001f ? weightedSum / totalWeight : Color.grey;
            dominant = Color.Lerp(dominant, Color.white, Luminance(dominant) * 0.5f);
            return dominant;
        }

        Color SampleEnvironmentColorFull(Vector3 origin, Vector3 dir)
        {
            if (_samplingCam == null || _rt == null || _readTex == null) return Color.white;
            _camGO.transform.position = origin;
            _camGO.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            return RenderAndAverage();
        }

        Color SampleEnvironmentColor(Vector3 origin, Vector3 dir)
        {
            if (_samplingCam == null || _rt == null || _readTex == null) return Color.white;
            _camGO.transform.position = origin;
            Vector3 flatDir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            _camGO.transform.rotation = Quaternion.LookRotation(flatDir, Vector3.up);
            return RenderAndAverage();
        }

        Color RenderAndAverage()
        {
            _samplingCam.targetTexture = _rt;
            _samplingCam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _readTex.ReadPixels(new Rect(0, 0, Cfg.ScanRes, Cfg.ScanRes), 0, 0);
            _readTex.Apply();
            RenderTexture.active = prev;

            Color   sum    = Color.black;
            Color[] pixels = _readTex.GetPixels();
            foreach (Color p in pixels) sum += p;
            return sum / pixels.Length;
        }

        bool RaiseLuminanceFloor(Color[] colors)
        {
            float minLum = float.MaxValue;
            foreach (Color c in colors) { float l = Luminance(c); if (l < minLum) minLum = l; }
            if (minLum >= Cfg.MinLuminanceFloor) return false;

            float deficit = Cfg.MinLuminanceFloor - minLum;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i].r = Mathf.Clamp01(colors[i].r + deficit);
                colors[i].g = Mathf.Clamp01(colors[i].g + deficit);
                colors[i].b = Mathf.Clamp01(colors[i].b + deficit);
            }
            return true;
        }

        // ── light atom management ────────────────────────────────────

        IEnumerator CreateLightAtom(string uid, Action<Atom> assign)
        {
            Atom existing = SuperController.singleton.GetAtomByUid(uid);
            if (existing != null) { assign(existing); yield break; }

            yield return SuperController.singleton.AddAtomByType("InvisibleLight", uid);
            yield return null;

            Atom a = SuperController.singleton.GetAtomByUid(uid);
            if (a == null) { Logger.LogError($"EnvCharacterLighting: Failed to create light atom '{uid}'."); yield break; }

            HideLightVisuals(a);
            assign(a);
        }

        IEnumerator CreateAllLights()
        {
            yield return CreateLightAtom("EnvKeyFront",    a => _keyFrontAtom  = a);
            yield return CreateLightAtom("EnvKeyBack",     a => _keyBackAtom   = a);
            yield return CreateLightAtom("EnvKeyLeft",     a => _keyLeftAtom   = a);
            yield return CreateLightAtom("EnvKeyRight",    a => _keyRightAtom  = a);
            yield return CreateLightAtom("EnvAmbientLight",a => _ambientAtom   = a);

            if (_keyFrontAtom  != null) _keyFrontStor  = _keyFrontAtom.GetStorableByID("Light");
            if (_keyBackAtom   != null) _keyBackStor   = _keyBackAtom.GetStorableByID("Light");
            if (_keyLeftAtom   != null) _keyLeftStor   = _keyLeftAtom.GetStorableByID("Light");
            if (_keyRightAtom  != null) _keyRightStor  = _keyRightAtom.GetStorableByID("Light");
            if (_ambientAtom   != null) _ambientStor   = _ambientAtom.GetStorableByID("Light");

            SetLightType(_keyFrontStor,  "Spot");
            SetLightType(_keyBackStor,   "Spot");
            SetLightType(_keyLeftStor,   "Spot");
            SetLightType(_keyRightStor,  "Spot");
            SetLightType(_ambientStor,   "Point");
        }

        void DestroyLights()
        {
            if (SuperController.singleton == null) return;
            if (_keyFrontAtom  != null) SuperController.singleton.RemoveAtom(_keyFrontAtom);
            if (_keyBackAtom   != null) SuperController.singleton.RemoveAtom(_keyBackAtom);
            if (_keyLeftAtom   != null) SuperController.singleton.RemoveAtom(_keyLeftAtom);
            if (_keyRightAtom  != null) SuperController.singleton.RemoveAtom(_keyRightAtom);
            if (_ambientAtom   != null) SuperController.singleton.RemoveAtom(_ambientAtom);
            _keyFrontAtom = _keyBackAtom = _keyLeftAtom = _keyRightAtom = _ambientAtom = null;
            _keyFrontStor = _keyBackStor = _keyLeftStor = _keyRightStor = _ambientStor = null;
        }

        void SetAllLightsToZero()
        {
            foreach (JSONStorable s in new[] { _keyFrontStor, _keyBackStor, _keyLeftStor, _keyRightStor, _ambientStor })
                if (s != null) s.SetFloatParamValue("intensity", 0f);
        }

        void HideLightVisuals(Atom atom)
        {
            var ctrl = atom.mainController;
            if (ctrl != null) { ctrl.canGrabPosition = false; ctrl.canGrabRotation = false; }
            atom.hidden = true;
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            if (layer >= 0) SetLayerRecursively(atom.gameObject, layer);
        }

        void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform) SetLayerRecursively(child.gameObject, layer);
        }

        void SetLight(JSONStorable stor, float intensity, float spotAngle, float range,
            HSVColor color, bool shadows, float shadowStrength, float pointBias)
        {
            if (stor == null) return;
            stor.SetFloatParamValue("intensity",      intensity);
            stor.SetFloatParamValue("spotAngle",      spotAngle);
            stor.SetFloatParamValue("range",          range);
            stor.SetColorParamValue("color",          color);
            stor.SetBoolParamValue ("shadowsOn",      shadows);
            if (shadows)
            {
                stor.SetFloatParamValue("shadowStrength", shadowStrength);
                stor.SetFloatParamValue("pointBias",      pointBias);
            }
        }

        void SetLightType(JSONStorable stor, string type)
        {
            if (stor != null) stor.SetStringChooserParamValue("type", type);
        }

        void CleanupSamplingObjects()
        {
            if (_rt      != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_readTex != null) { Destroy(_readTex); _readTex = null; }
            if (_camGO   != null) { Destroy(_camGO); _camGO = null; _samplingCam = null; }
        }

        // ── small utilities ──────────────────────────────────────────

        Transform GetControl(string id)
            => (_person?.GetStorableByID(id) as FreeControllerV3)?.transform;

        static float Luminance(Color c)
            => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;

        static Color ApplySaturation(Color c, float satScale)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            return Color.HSVToRGB(h, s * satScale, v);
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static HSVColor ToHSV(Color c)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            return new HSVColor { H = h, S = s, V = v };
        }
    }
}
