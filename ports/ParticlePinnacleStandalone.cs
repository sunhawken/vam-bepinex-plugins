using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

namespace ParticlePinnacle
{
    // Runs the original ParticlePinnacle script from loose Custom files.  The
    // package archive is an embedded resource, so the original .var is never
    // queried by VaM and does not need to remain installed after this DLL is built.
    [BepInPlugin("com.particlepinnacle.thighcompressorandvibrations", "ParticlePinnacle Thigh Compressor & Vibrations", "52.0.0")]
    public sealed class ParticlePinnacleStandalonePlugin : BaseUnityPlugin
    {
        private const string ScriptUrl = "Custom/Scripts/particlepinnacle/thighcompressorandvibrations.cs";
        private const string ResourceName = "ParticlePinnacle.ppthighcompressorandvibrations.52.var";
        private const string ScriptTypeName = "particlepinnacle.thighcompressorandvibrations";
        private const string PatchedScriptMarker = "[ParticlePinnacleStandalone] safe optional-morph patch";
        private const string StartupSafetyMarker = "[ParticlePinnacleStandalone] startup-safety patch v2";
        private const string TouchFilterMarker = "[ParticlePinnacleStandalone] target-type filters";
        private const string TouchFilterRuntimeMarker = "[ParticlePinnacleStandalone] target filters enforced at runtime";
        private const string TouchFilterHardStopMarker = "[ParticlePinnacleStandalone] all-targets-disabled hard stop";
        private const string TouchFilterPreserveTargetMarker = "[ParticlePinnacleStandalone] preserve target while filtered v2";
        private const string TouchFilterReliableRuntimeMarker = "[ParticlePinnacleStandalone] reliable target-filter runtime v3";

        private ConfigEntry<bool> _installAssets;
        private ConfigEntry<bool> _attachToFemalePersons;
        private ConfigEntry<bool> _attachToMalePersons;
        private ConfigEntry<bool> _activateWhenTouchingPersons;
        private ConfigEntry<bool> _activateWhenTouchingCua;
        private ConfigEntry<bool> _showControlWindow;
        private ConfigEntry<float> _controlWindowScale;
        private ConfigEntry<float> _controlWindowX;
        private ConfigEntry<float> _controlWindowY;
        private ConfigEntry<float> _controlWindowWidth;
        private ConfigEntry<float> _controlWindowHeight;
        private Rect _controlWindow = new Rect(24f, 90f, 280f, 118f);
        private bool _resizingControlWindow;
        private bool _draggingControlWindow;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
        private Vector2 _dragStartMouse;
        private Vector2 _dragStartPosition;
        private bool _installed;
        // VaM compiles a newly selected .cs plugin asynchronously.  During that
        // gap it is not yet visible in pluginContainer, so a polling loader
        // must retain a claim for the atom rather than trying again every pass.
        // Native controllers live on this BepInEx plugin GameObject, never in
        // a Person's PluginManager.  The key is the receiving Person uid.
        private readonly Dictionary<string, thighcompressorandvibrations> _nativeControllers =
            new Dictionary<string, thighcompressorandvibrations>();

        private void Awake()
        {
            _installAssets = Config.Bind("General", "InstallBundledAssets", true,
                "Extract the embedded ParticlePinnacle morphs and script into VaM's Custom directory when missing.");
            _attachToFemalePersons = Config.Bind("General", "AttachToFemalePersons", true,
                "Automatically add the original compressor/vibration script to female Person atoms.");
            _attachToMalePersons = Config.Bind("General", "AttachToMalePersons", false,
                "Automatically add the original compressor/vibration script to male Person atoms.");
            _activateWhenTouchingPersons = Config.Bind("Touch Filters", "ActivateWhenTouchingPersonAtoms", true,
                "Allow the compressor to activate for a selected Person atom.");
            _activateWhenTouchingCua = Config.Bind("Touch Filters", "ActivateWhenTouchingCUAAtoms", true,
                "Allow the compressor to activate for a selected Custom Unity Asset (CUA) atom.");
            _showControlWindow = Config.Bind("Touch Filters", "ShowControlWindow", true,
                "Show the in-game DPI-aware touch-filter control window.");
            _controlWindowScale = Config.Bind("Touch Filters", "ControlWindowScale", 1.0f,
                "Extra scale multiplier for the touch-filter control window.");
            _controlWindowX = Config.Bind("Touch Filters", "ControlWindowX", 24f,
                "Saved horizontal position of the touch-filter window.");
            _controlWindowY = Config.Bind("Touch Filters", "ControlWindowY", 90f,
                "Saved vertical position of the touch-filter window.");
            _controlWindowWidth = Config.Bind("Touch Filters", "ControlWindowWidth", 280f,
                "Saved width of the touch-filter window.");
            _controlWindowHeight = Config.Bind("Touch Filters", "ControlWindowHeight", 118f,
                "Saved height of the touch-filter window.");
            _controlWindow = new Rect(_controlWindowX.Value, _controlWindowY.Value,
                Mathf.Clamp(_controlWindowWidth.Value, 210f, 560f),
                Mathf.Clamp(_controlWindowHeight.Value, 96f, 360f));
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            while (SuperController.singleton == null) yield return null;
            while (SuperController.singleton.isLoading) yield return null;

            Exception setupError = null;
            try
            {
                EnsureAssetsInstalled();
                _installed = true;
                ApplyTouchFiltersToNativeControllers();
                SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;
                Logger.LogInfo("ParticlePinnacle standalone loader is ready. The .var does not need to be active.");
            }
            catch (Exception ex)
            {
                setupError = ex;
            }
            if (setupError != null)
            {
                Logger.LogError("ParticlePinnacle setup failed: " + setupError);
                yield break;
            }
            yield return AttachAfterSceneLoad();
            StartCoroutine(AttachmentWatchdog());
        }

        private void OnSceneLoaded()
        {
            StartCoroutine(AttachAfterSceneLoad());
        }

        private IEnumerator AttachAfterSceneLoad()
        {
            while (SuperController.singleton != null && SuperController.singleton.isLoading) yield return null;
            // The scene-loaded callback happens before every Person finishes
            // building its geometry and PluginManager.  Loading the script too
            // early makes the script's Start method run against half-built data.
            yield return new WaitForSeconds(2f);
            if (!_installed) yield break;

            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                RemoveAllLegacyParticlePinnacleScripts(atom);
                if (!ShouldAttach(atom)) continue;
                EnsureNativeController(atom);
            }
        }

        // Atoms can be created after VaM signals the end of a scene load (and
        // also from presets/sub-scenes), so keep a light-weight safety pass.
        private IEnumerator AttachmentWatchdog()
        {
            while (_installed && SuperController.singleton != null)
            {
                if (!SuperController.singleton.isLoading)
                    yield return AttachAfterSceneLoad();
                ApplyTouchFiltersToNativeControllers();
                yield return new WaitForSeconds(3f);
            }
        }

        private bool ShouldAttach(Atom atom)
        {
            if (atom == null || atom.type != "Person") return false;
            // DAZCharacterSelector is registered as the atom's "geometry"
            // storable; it is not necessarily a component below the Atom.
            DAZCharacterSelector selector = atom.GetStorableByID("geometry") as DAZCharacterSelector;
            if (selector == null) return false;
            return selector.gender == DAZCharacterSelector.Gender.Female
                ? _attachToFemalePersons.Value
                : _attachToMalePersons.Value;
        }

        private void OnGUI()
        {
            if (_showControlWindow == null || !_showControlWindow.Value) return;
            float dpi = Screen.dpi;
            float dpiScale = dpi > 0f ? Mathf.Clamp(dpi / 96f, 1f, 2.5f) : 1f;
            float scale = dpiScale * Mathf.Clamp(_controlWindowScale.Value, 0.65f, 2.5f);
            // Do not use GUI.matrix here.  VaM's old IMGUI pipeline does not
            // always transform mouse coordinates with that matrix, so scaled
            // buttons can look clickable while receiving clicks elsewhere.
            Rect panel = new Rect(_controlWindow.x, _controlWindow.y,
                _controlWindow.width * scale, _controlWindow.height * scale);
            GUI.Box(panel, "");
            DrawControlWindow(panel, scale);
        }

        private void DrawControlWindow(Rect panel, float scale)
        {
            float pad = 8f * scale;
            float titleHeight = 24f * scale;
            GUI.Label(new Rect(panel.x + pad, panel.y + 2f * scale, panel.width - 90f * scale, titleHeight),
                "ParticlePinnacle Touch Filters");
            if (GUI.Button(new Rect(panel.xMax - 58f * scale, panel.y + 3f * scale, 24f * scale, 18f * scale), "−"))
                SetControlWindowScale(_controlWindowScale.Value - 0.1f);
            if (GUI.Button(new Rect(panel.xMax - 30f * scale, panel.y + 3f * scale, 24f * scale, 18f * scale), "+"))
                SetControlWindowScale(_controlWindowScale.Value + 0.1f);
            DrawFilterButton(new Rect(panel.x + pad, panel.y + 31f * scale, panel.width - 2f * pad, 28f * scale),
                "Person targets", _activateWhenTouchingPersons);
            DrawFilterButton(new Rect(panel.x + pad, panel.y + 64f * scale, panel.width - 2f * pad, 28f * scale),
                "CUA targets", _activateWhenTouchingCua);
            HandleWindowDragAndResize(panel, scale);
        }

        private void DrawFilterButton(Rect area, string label, ConfigEntry<bool> setting)
        {
            string state = setting.Value ? "ON" : "OFF";
            if (GUI.Button(area, label + "  [" + state + "]"))
            {
                setting.Value = !setting.Value;
                ApplyTouchFiltersToNativeControllers();
                Logger.LogInfo(label + " filter: " + (setting.Value ? "enabled" : "disabled") + ".");
            }
        }

        private void SetControlWindowScale(float value)
        {
            _controlWindowScale.Value = Mathf.Clamp(value, 0.65f, 2.5f);
            SaveControlWindowLayout();
        }

        // Direct pixel-space drag/resize keeps input accurate at every DPI.
        private void HandleWindowDragAndResize(Rect panel, float scale)
        {
            Rect title = new Rect(panel.x, panel.y, panel.width - 64f * scale, 25f * scale);
            Rect grip = new Rect(panel.xMax - 20f * scale, panel.yMax - 20f * scale, 18f * scale, 18f * scale);
            GUI.Label(grip, "↘");
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && grip.Contains(evt.mousePosition))
            {
                _resizingControlWindow = true;
                _resizeStartMouse = evt.mousePosition;
                _resizeStartSize = _controlWindow.size;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && title.Contains(evt.mousePosition))
            {
                _draggingControlWindow = true;
                _dragStartMouse = evt.mousePosition;
                _dragStartPosition = _controlWindow.position;
                evt.Use();
            }
            else if (_resizingControlWindow && evt.type == EventType.MouseDrag)
            {
                _controlWindow.width = Mathf.Clamp(_resizeStartSize.x + (evt.mousePosition.x - _resizeStartMouse.x) / scale, 210f, 560f);
                _controlWindow.height = Mathf.Clamp(_resizeStartSize.y + (evt.mousePosition.y - _resizeStartMouse.y) / scale, 96f, 360f);
                evt.Use();
            }
            else if (_draggingControlWindow && evt.type == EventType.MouseDrag)
            {
                _controlWindow.position = _dragStartPosition + evt.mousePosition - _dragStartMouse;
                evt.Use();
            }
            else if (_resizingControlWindow && evt.type == EventType.MouseUp)
            {
                _resizingControlWindow = false;
                SaveControlWindowLayout();
                evt.Use();
            }
            else if (_draggingControlWindow && evt.type == EventType.MouseUp)
            {
                _draggingControlWindow = false;
                SaveControlWindowLayout();
                evt.Use();
            }
        }

        private void SaveControlWindowLayout()
        {
            _controlWindowX.Value = _controlWindow.x;
            _controlWindowY.Value = _controlWindow.y;
            _controlWindowWidth.Value = _controlWindow.width;
            _controlWindowHeight.Value = _controlWindow.height;
            // ConfigEntry values are changed immediately, but BepInEx does not
            // guarantee a disk write until shutdown.  Persist each completed
            // drag/resize so a crash or forced close cannot lose the layout.
            Config.Save();
        }

        private void ApplyTouchFiltersToNativeControllers()
        {
            if (!_installed || SuperController.singleton == null) return;
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, thighcompressorandvibrations> pair in _nativeControllers)
            {
                thighcompressorandvibrations controller = pair.Value;
                Atom atom = SuperController.singleton.GetAtomByUid(pair.Key);
                if (controller == null || atom == null)
                {
                    if (controller != null) Destroy(controller);
                    stale.Add(pair.Key);
                    continue;
                }
                controller.SetRuntimeFilters(_activateWhenTouchingPersons.Value, _activateWhenTouchingCua.Value);
            }
            foreach (string uid in stale) _nativeControllers.Remove(uid);
        }

        private void EnsureNativeController(Atom atom)
        {
            thighcompressorandvibrations existing;
            if (_nativeControllers.TryGetValue(atom.uid, out existing) && existing != null) return;
            Atom target = FindFallbackPersonTarget(atom);
            if (target == null)
            {
                Logger.LogWarning("ParticlePinnacle native controller is waiting for a second Person atom.");
                return;
            }
            try
            {
                thighcompressorandvibrations controller = gameObject.AddComponent<thighcompressorandvibrations>();
                controller.enabled = false;
                controller.Initialize(atom, target);
                controller.SetRuntimeFilters(_activateWhenTouchingPersons.Value, _activateWhenTouchingCua.Value);
                controller.enabled = true;
                _nativeControllers[atom.uid] = controller;
                Logger.LogInfo("Started native ParticlePinnacle controller for " + atom.uid + " (target " + target.uid + ").");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not start native ParticlePinnacle controller for " + atom.uid + ": " + ex);
            }
        }

        private static Atom FindFallbackPersonTarget(Atom owner)
        {
            Atom firstPerson = null;
            foreach (Atom candidate in SuperController.singleton.GetAtoms())
            {
                if (candidate == null || candidate == owner || candidate.type != "Person") continue;
                if (firstPerson == null) firstPerson = candidate;
                DAZCharacterSelector selector = candidate.GetStorableByID("geometry") as DAZCharacterSelector;
                if (selector != null && selector.gender == DAZCharacterSelector.Gender.Male) return candidate;
            }
            return firstPerson;
        }

        private static bool HasParticlePinnacleScript(Atom atom)
        {
            MVRPluginManager manager = atom.GetStorableByID("PluginManager") as MVRPluginManager;
            if (manager == null || manager.pluginContainer == null) return false;
            foreach (Transform child in manager.pluginContainer)
            {
                MVRPlugin plugin = child.GetComponentInChildren<MVRPlugin>();
                if (plugin != null && plugin.pluginURLJSON != null &&
                    string.Equals(plugin.pluginURLJSON.val, ScriptUrl, StringComparison.OrdinalIgnoreCase)) return true;
                MVRScript script = child.GetComponentInChildren<MVRScript>();
                if (script != null && script.GetType().FullName == ScriptTypeName) return true;
            }
            return false;
        }

        private void AttachOriginalScript(Atom atom)
        {
            // Kept only for binary/source compatibility with older builds.
            // Native controllers are created by EnsureNativeController instead.
            EnsureNativeController(atom);
        }

        // Older builds could have inserted several identical script entries
        // into a scene.  Keep the first and destroy each later plugin object;
        // this also removes those entries from a subsequently saved scene.
        private void RemoveAllLegacyParticlePinnacleScripts(Atom atom)
        {
            MVRPluginManager manager = atom != null ? atom.GetStorableByID("PluginManager") as MVRPluginManager : null;
            if (manager == null || manager.pluginContainer == null) return;
            List<GameObject> legacyScripts = new List<GameObject>();
            foreach (Transform child in manager.pluginContainer)
            {
                MVRPlugin plugin = child.GetComponentInChildren<MVRPlugin>();
                MVRScript script = child.GetComponentInChildren<MVRScript>();
                bool isParticlePinnacle = (plugin != null && plugin.pluginURLJSON != null &&
                    string.Equals(plugin.pluginURLJSON.val, ScriptUrl, StringComparison.OrdinalIgnoreCase)) ||
                    (script != null && script.GetType().FullName == ScriptTypeName);
                if (isParticlePinnacle) legacyScripts.Add(child.gameObject);
            }
            foreach (GameObject duplicate in legacyScripts)
            {
                Logger.LogInfo("Removed legacy attached ParticlePinnacle .cs from " + atom.uid + ".");
                Destroy(duplicate);
            }
        }

        private IEnumerator ApplyTouchFiltersAfterScriptStartup()
        {
            // Sync once the dynamically compiled MVRScript has registered its
            // JSON parameters; this also preserves disabled defaults immediately.
            yield return new WaitForSeconds(1f);
            ApplyTouchFiltersToNativeControllers();
        }

        private void EnsureAssetsInstalled()
        {
            string vamRoot = Path.GetFullPath(Paths.GameRootPath);
            string morphProbe = Path.Combine(vamRoot,
                "Custom" + Path.DirectorySeparatorChar + "Atom" + Path.DirectorySeparatorChar + "Person" +
                Path.DirectorySeparatorChar + "Morphs" + Path.DirectorySeparatorChar + "female" +
                Path.DirectorySeparatorChar + "particlepinnacle" + Path.DirectorySeparatorChar + "for compressions" +
                Path.DirectorySeparatorChar + "pp butt thigh comp inner 1.vmb");
            if (File.Exists(morphProbe)) return;
            if (!_installAssets.Value)
                throw new InvalidOperationException("Bundled assets are missing and InstallBundledAssets is disabled.");

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream source = assembly.GetManifestResourceStream(ResourceName))
            {
                if (source == null) throw new InvalidOperationException("The embedded ParticlePinnacle archive was not found.");
                using (ZipInputStream zip = new ZipInputStream(source))
                {
                    ZipEntry entry;
                    byte[] buffer = new byte[81920];
                    while ((entry = zip.GetNextEntry()) != null)
                    {
                        if (!entry.IsFile || !entry.Name.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals(ScriptUrl, StringComparison.OrdinalIgnoreCase)) continue;
                        string relative = entry.Name.Replace('/', Path.DirectorySeparatorChar);
                        string destination = Path.GetFullPath(Path.Combine(vamRoot, relative));
                        if (!destination.StartsWith(vamRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Archive entry has an invalid path: " + entry.Name);
                        if (File.Exists(destination)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (FileStream output = File.Create(destination))
                        {
                            int count;
                            while ((count = zip.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, count);
                        }
                    }
                }
            }
            Logger.LogInfo("Installed bundled ParticlePinnacle morph assets to Custom.");
        }

        // This package's script assumes several optional third-party morphs are
        // installed, then repeatedly re-enters Start when one is absent.  Make
        // those resets null-safe and keep its recovery watchdog off by default.
        // The patch is intentionally applied to the loose runtime copy, not the
        // user's original .var archive.
        private void PatchInstalledScript()
        {
            string path = Path.Combine(Path.GetFullPath(Paths.GameRootPath), ScriptUrl.Replace('/', Path.DirectorySeparatorChar));
            // The loose script may already contain an older generated patch.
            // Restore the embedded original first so every DLL update can apply
            // its current patch set instead of silently retaining stale code.
            RestoreOriginalRuntimeScript(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Bundled ParticlePinnacle script was not installed.", path);
            string source = File.ReadAllText(path);
            if (source.IndexOf(PatchedScriptMarker, StringComparison.Ordinal) < 0)
            {
                int resetStart = source.IndexOf("private void ResetMorphs()", StringComparison.Ordinal);
                int resetEnd = source.IndexOf("void FixedUpdate()", resetStart, StringComparison.Ordinal);
                if (resetStart < 0 || resetEnd < 0) throw new InvalidOperationException("Unexpected ParticlePinnacle script layout.");
                string resetBlock = source.Substring(resetStart, resetEnd - resetStart);
                resetBlock = Regex.Replace(resetBlock, @"(?m)^(\s*)([A-Za-z_][A-Za-z0-9_]*)\.Reset\(\);", "$1SafeReset($2);");
                string helper = "// " + PatchedScriptMarker + "\r\n\t\tprivate static void SafeReset(DAZMorph morph) { if (morph != null) morph.Reset(); }\r\n\t\t";
                source = source.Substring(0, resetStart) + helper + resetBlock + source.Substring(resetEnd);
            }
            if (source.IndexOf(StartupSafetyMarker, StringComparison.Ordinal) < 0)
            {
                source = source.Replace("DAZCharacterSelector dcs = js as DAZCharacterSelector;\r\n\t\t\tGenerateDAZMorphsControlUI morphUI = dcs.morphsControlUI;",
                    "DAZCharacterSelector dcs = js as DAZCharacterSelector;\r\n\t\t\tif (dcs == null) return;\r\n\t\t\tGenerateDAZMorphsControlUI morphUI = dcs.morphsControlUI;");
                source = source.Replace("if (person2 != null)\r\n\t\t\t\t\t\t{\r\n\t\t\t\t\t\t\t//playerPelvisController",
                    "if (person2 != null && pelvisController != null && playerTipController != null)\r\n\t\t\t\t\t\t{\r\n\t\t\t\t\t\t\t//playerPelvisController");
                source = "// " + StartupSafetyMarker + "\r\n" + source;
            }
            if (source.IndexOf(TouchFilterMarker, StringComparison.Ordinal) < 0)
            {
                const string fields = "\t\t// [ParticlePinnacleStandalone] target-type filters\r\n\t\tprivate JSONStorableBool activateWhenTouchingPersons;\r\n\t\tprivate JSONStorableBool activateWhenTouchingCUA;\r\n\t\tprivate bool IsCUAAtom(Atom atom) { return atom != null && atom.type != null && (atom.type.Equals(\"CUA\", StringComparison.OrdinalIgnoreCase) || atom.type.IndexOf(\"CustomUnityAsset\", StringComparison.OrdinalIgnoreCase) >= 0); }\r\n\t\tprivate bool IsAllowedTouchTarget(Atom atom) { if (atom == null) return false; if (atom.type == \"Person\") return activateWhenTouchingPersons != null && activateWhenTouchingPersons.val; if (IsCUAAtom(atom)) return activateWhenTouchingCUA != null && activateWhenTouchingCUA.val; return false; }\r\n\t\t";
                source = source.Replace("\t\tpublic JSONStorableStringChooser uiFocusTarget;", fields + "public JSONStorableStringChooser uiFocusTarget;");
                source = source.Replace("\t\t\t\tCreateToggle(autoReload).label = \"Auto-Reload When Stuck\";", "\t\t\t\tCreateToggle(autoReload).label = \"Auto-Reload When Stuck\";\r\n\t\t\t\tactivateWhenTouchingPersons = new JSONStorableBool(\"Activate when touching Person atoms\", true);\r\n\t\t\t\tRegisterBool(activateWhenTouchingPersons);\r\n\t\t\t\tCreateToggle(activateWhenTouchingPersons).label = \"Activate when touching Person atoms\";\r\n\t\t\t\tactivateWhenTouchingCUA = new JSONStorableBool(\"Activate when touching CUA atoms\", true);\r\n\t\t\t\tRegisterBool(activateWhenTouchingCUA);\r\n\t\t\t\tCreateToggle(activateWhenTouchingCUA).label = \"Activate when touching CUA atoms\";");
                source = source.Replace("if (currentAtom != containingAtom && atomUID != null)", "if (currentAtom != containingAtom && atomUID != null && IsAllowedTouchTarget(currentAtom))");
                source = source.Replace("if (person2 != null && pelvisController != null && playerTipController != null)", "if (person2 != null && IsAllowedTouchTarget(person2) && pelvisController != null && playerTipController != null)");
                source = source.Replace("else\r\n\t\t\t\t\t\t{\r\n\t\t\t\t\t\t\t//playerPelvisController", "else if (!IsAllowedTouchTarget(person2))\r\n\t\t\t\t\t\t{\r\n\t\t\t\t\t\t\tuiFocusTarget.val = \"none\";\r\n\t\t\t\t\t\t\tcurrentAtomName = \"none\";\r\n\t\t\t\t\t\t}\r\n\t\t\t\t\t\telse\r\n\t\t\t\t\t\t{\r\n\t\t\t\t\t\t\t//playerPelvisController");
            }
            // The chooser is populated only during Init, but the target is
            // resolved every FixedUpdate.  Enforce the filter at that point so
            // turning a button off immediately stops an already-selected
            // Person/CUA instead of merely affecting future choices.
            if (source.IndexOf(TouchFilterRuntimeMarker, StringComparison.Ordinal) < 0)
            {
                source = source.Replace("person2 = SuperController.singleton.GetAtomByUid(uiFocusTarget.val);\r\n\t\t\t\t\t\tif (person2 == null)",
                    "person2 = SuperController.singleton.GetAtomByUid(uiFocusTarget.val);\r\n\t\t\t\t\t\t// " + TouchFilterRuntimeMarker + "\r\n\t\t\t\t\t\tif (person2 == null || !IsAllowedTouchTarget(person2))");
            }
            // A target might be an unusual atom type, or this controller might
            // already be animating when a filter is switched off.  The filters
            // are therefore also a master runtime stop: with both OFF, reset
            // every morph and leave FixedUpdate before any vibration code runs.
            if (source.IndexOf(TouchFilterHardStopMarker, StringComparison.Ordinal) < 0)
            {
                // The nullable isActive guard was added by an older patch after
                // this block previously ran, so accept either original shape.
                source = source.Replace("void FixedUpdate() {\r\n\t\t\tif (isActive == null || uiFocusTarget == null) return;",
                    "void FixedUpdate() {\r\n\t\t\t// " + TouchFilterHardStopMarker + "\r\n\t\t\tif (activateWhenTouchingPersons != null && activateWhenTouchingCUA != null && !activateWhenTouchingPersons.val && !activateWhenTouchingCUA.val) { ResetMorphs(); return; }\r\n\t\t\tif (isActive == null || uiFocusTarget == null) return;");
                source = source.Replace("void FixedUpdate() {\r\n\t\t\tif (isActive.val == true)",
                    "void FixedUpdate() {\r\n\t\t\t// " + TouchFilterHardStopMarker + "\r\n\t\t\tif (activateWhenTouchingPersons != null && activateWhenTouchingCUA != null && !activateWhenTouchingPersons.val && !activateWhenTouchingCUA.val) { ResetMorphs(); return; }\r\n\t\t\tif (isActive.val == true)");
            }
            source = source.Replace("new JSONStorableBool(\"Auto-Reload When Stuck\", true)", "new JSONStorableBool(\"Auto-Reload When Stuck\", false)");
            source = source.Replace("void Start() {\r\n\t\t\ttry {", "void Start() {\r\n\t\t\tif (uiFocusTarget == null) return;\r\n\t\t\ttry {");
            source = source.Replace("void FixedUpdate() {\r\n\t\t\tif (isActive.val == true)", "void FixedUpdate() {\r\n\t\t\tif (isActive == null || uiFocusTarget == null) return;\r\n\t\t\tif (isActive.val == true)");
            source = source.Replace("uiFocusTarget.val = auto.uid;\r\n        SuperController.LogMessage", "if (uiFocusTarget != null) uiFocusTarget.val = auto.uid;\r\n        SuperController.LogMessage");

            // Apply this final transformation after every compatibility edit.
            // Earlier builds tried to match one exact intermediate form; as a
            // result a stale script could pass through with the target-clearing
            // code intact.  This form accepts either original or prior-patched
            // indentation/newlines and leaves the Sex Giver selection in place.
            if (source.IndexOf(TouchFilterReliableRuntimeMarker, StringComparison.Ordinal) < 0)
            {
                source = Regex.Replace(source,
                    @"(?m)^(\s*)if \(person2 == null \|\| !IsAllowedTouchTarget\(person2\)\)\s*\{\s*uiFocusTarget\.val = ""none"";\s*currentAtomName = ""none"";\s*\}",
                    "$1// " + TouchFilterReliableRuntimeMarker + "\r\n$1if (person2 == null)\r\n$1{\r\n$1\tuiFocusTarget.val = \"none\";\r\n$1\tcurrentAtomName = \"none\";\r\n$1}\r\n$1else if (!IsAllowedTouchTarget(person2))\r\n$1{\r\n$1\tResetMorphs();\r\n$1\treturn;\r\n$1}");
                source = Regex.Replace(source,
                    @"void FixedUpdate\(\)\s*\{\s*(?!// \[ParticlePinnacleStandalone\] reliable target-filter runtime v3)",
                    "void FixedUpdate() {\r\n\t\t\t// " + TouchFilterReliableRuntimeMarker + "\r\n\t\t\tif (activateWhenTouchingPersons != null && activateWhenTouchingCUA != null && !activateWhenTouchingPersons.val && !activateWhenTouchingCUA.val) { ResetMorphs(); return; }\r\n\t\t\t");
                if (source.IndexOf(TouchFilterReliableRuntimeMarker, StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Could not apply the reliable ParticlePinnacle target-filter runtime patch.");
            }
            File.WriteAllText(path, source);
            Logger.LogInfo("Applied ParticlePinnacle runtime safety patches.");
        }

        private void RestoreOriginalRuntimeScript(string destination)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream source = assembly.GetManifestResourceStream(ResourceName))
            {
                if (source == null) throw new InvalidOperationException("The embedded ParticlePinnacle archive was not found.");
                using (ZipInputStream zip = new ZipInputStream(source))
                {
                    ZipEntry entry;
                    while ((entry = zip.GetNextEntry()) != null)
                    {
                        if (!entry.IsFile || !entry.Name.Equals(ScriptUrl, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (FileStream output = File.Create(destination))
                        {
                            byte[] buffer = new byte[81920];
                            int count;
                            while ((count = zip.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, count);
                        }
                        return;
                    }
                }
            }
            throw new FileNotFoundException("The embedded archive did not contain the ParticlePinnacle script.", ScriptUrl);
        }

        private void OnDestroy()
        {
            if (_controlWindowX != null) SaveControlWindowLayout();
            if (SuperController.singleton != null)
                SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
        }
    }
}
