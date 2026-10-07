using MeshVR;
using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System.Linq;

/// <summary>
/// Give Me FPS Version v27
/// By Redeyes
/// Session plugin to quickly set ALL person options to give more frames per second at diffently levels to user requirements
/// </summary>

namespace Redeyes{

    public class GiveMeFPS : MVRScript
    {
        private UIDynamicButton btn;

        private JSONStorableBool DynamicAdjustState;
        private JSONStorableFloat NumberDynamicPersonsValue;
        private UIDynamicSlider NumberDynamicPersonsSlider;

        private JSONStorableBool clothSimState;
        private JSONStorableBool AdvancedCollidersState;

        private JSONStorableBool HairSimulationState;
        private UIDynamicToggle onToggleHairSimulationState;
        private JSONStorableBool DisableHairRenderersState;
        private UIDynamicToggle onToggleDisableHairRenderersState;

        private JSONStorableBool BreastPhysicsState;
        private UIDynamicToggle onToggleBreastPhysicsState;

        private JSONStorableBool LowerPhysicsState;
        private UIDynamicToggle onToggleLowerPhysicsState;

        private JSONStorableBool TonguePhysicsState;
        private UIDynamicToggle onToggleTonguePhysicsState;

        private JSONStorableBool disablePixelLightsState;
        private UIDynamicToggle onToggledisablePixelLightState;

        private JSONStorableFloat HairMultiplierValue;
        private UIDynamicSlider HairMultiplierSlider;
        
        private JSONStorableFloat CurveDensityValue;
        private UIDynamicSlider CurveDensitySlider;

        private JSONStorableFloat HairWidthValue;
        private UIDynamicSlider HairWidthValueSlider;

        private JSONStorableFloat HairWeightValue;
        private UIDynamicSlider HairWeightValueSlider;

        private JSONStorableFloat HairFrictionValue;
        private UIDynamicSlider HairFrictionValueSlider;

        private JSONStorableFloat HairIterationsValue;
        private UIDynamicSlider HairIterationsSlider;

        private JSONStorableFloat ClothStiffnessValue;
        private UIDynamicSlider ClothStiffnessValueSlider;

        private JSONStorableFloat ClothWeightValue;
        private UIDynamicSlider ClothWeightValueSlider;

        private JSONStorableFloat ClothIterationsValue;
        private UIDynamicSlider ClothIterationsValueSlider;

        private JSONStorableFloat ClothCollisonPowerValue;
        private UIDynamicSlider ClothCollisonPowerValueSlider;

        private JSONStorableFloat ClothCollisonRadiusValue;
        private UIDynamicSlider ClothCollisonRadiusValueSlider;

        private JSONStorableStringChooser shaderChooser;
        private List<string> shaderChoices = new List<string>();

        private JSONStorableStringChooser reflectionTextureSizeChooser;
        private List<string> reflectionTextureChoices = new List<string>();

        private JSONStorableFloat pixelLightCountValue;
        private UIDynamicSlider pixelLightCountSlider;

        private JSONStorableStringChooser msaaLevelchooser;
        private List<string> msaaLevelChoices = new List<string>();

        private JSONStorableStringChooser ShaderLODChooser;
        private List<string> ShaderLODChoices = new List<string>();

        private JSONStorableFloat renderScaleValue;
        private UIDynamicSlider renderScaleSlider;

        private JSONStorableFloat smoothPassesValue;
        private UIDynamicSlider smoothPassesSlider;

        private JSONStorableStringChooser GlowEffectsLevelChooser;
        private List<string> GlowEffectsLevelChoices = new List<string>();

        private JSONStorableBool mirrorReflectionsState;
        private UIDynamicToggle onTogglemirrorReflectionsState;

        private JSONStorableBool realtimeReflectionProbesState;
        private UIDynamicToggle onTogglerealtimeReflectionProbesState;

        private JSONStorableBool physicsHighQualityState;
        private UIDynamicToggle onTogglephysicsHighQualityState;

        private JSONStorableBool softPhysicsState;
        private UIDynamicToggle onTogglesoftPhysicsState;

        private JSONStorableFloat physicsUpdateCapValue;
        private UIDynamicSlider physicsUpdateCapSlider;

        private JSONStorableStringChooser physicsRateChooser;
        private List<string> physicsRateChoices = new List<string>();

        private JSONStorableBool clothSimState_orginal;
        private JSONStorableBool AdvancedCollidersState_orginal;
        private JSONStorableBool softPhysicsState_orginal;
        private JSONStorableStringChooser msaaLevelchooser_orginal;
        private JSONStorableFloat pixelLightCountValue_orginal;
        private JSONStorableBool mirrorReflectionsState_orginal;
        private JSONStorableBool realtimeReflectionProbesState_orginal;
        private JSONStorableBool physicsHighQualityState_orginal;        
        private JSONStorableFloat smoothPassesValue_orginal;
        private JSONStorableStringChooser ShaderLODChooser_orginal;

        private JSONStorableBool overrideScenePreferencesState;
        private JSONStorableBool overrideClothParametersState;

        private string msaa_popup;
        private string atom_name = "";
        private float count=0;
        private float cloth_ready_count=0;
        private bool first_time_bench = true;
        private bool _ready = false;
        private bool cloth_ready = false;
        private bool hair_ready = false;
        protected PresetManager pm;

        public override void Init()
        {
            try {
                SuperController.singleton.onAtomAddedHandlers += OnAtomAdded;
                SuperController.singleton.onSceneLoadedHandlers += OnSceneLoaded;
                SuperController.singleton.onAtomUIDsChangedHandlers += OnAtomUIDsChanged;

                foreach (var storable in SuperController.singleton
                    .GetComponentInChildren<MVRPluginManager>()
                    .GetComponentsInChildren<MVRScript>()
                    .Where(s => s.name.Contains("Redeyes.GiveMeFPS")))
                {
                    if(containingAtom.type == "CoreControl")
                    {
                        SuperController.LogMessage("Redeyes.GiveMeFPS already running as a session plugin - exiting as scene plugin");
                        DestroyImmediate(this);
                        return;
                    }
                }

                if (containingAtom.type != "CoreControl" && containingAtom.type != "SessionPluginManager")
                {
                    SuperController.LogError("Please load GiveMeFPS as a Session or Scene Plugin, not with a '" + containingAtom.type + "' atom.");
                    DestroyImmediate(this);
                    return;
                }
                //start if user fine tuning
                SetupInfoText(this, "<color=#606060><size=40><b>Quick Buttons</b></size></color>", 20.0f, false);
                
                //Quick buttons
                btn = CreateButton("Give me some FPS - Recommend");
                btn.button.onClick.AddListener(() => { ExecuteGiveMeFPS(true, 16, 3, 0.00045000f, 1, 1, 0.5F, 1.0F, 1,true, false, false, "Quality", true, "1", "1024", false, 0.010F, 0.008F, 0.2F); });
                JSONStorableAction GivemesomeFPSRecommendAction = new JSONStorableAction("Give me some FPS - Recommend", () => {
                    ExecuteGiveMeFPS(true, 16, 3, 0.00045000f, 1, 1, 0.5F, 1.0F, 1, true, false, false, "Quality", true, "1", "1024", false, 0.010F, 0.008F, 0.2F);
                });
                RegisterAction(GivemesomeFPSRecommendAction);

                btn = CreateButton("Give me some FPS - Hair Only");
                btn.button.onClick.AddListener(() => { ExecuteGiveMeFPS(true, 16, 3, 0.00045000f, 1, 1, 0.5F, 1.0F, 1, true, true, true, "Quality", false, "8", "1024", true, 0.010F, 0.008F, 0.2F); });
                JSONStorableAction GivemesomeFPSHairOnlyAction = new JSONStorableAction("Give me some FPS - Hair Only", () => {
                    ExecuteGiveMeFPS(true, 16, 3, 0.00045000f, 1, 1, 0.5F, 1.0F, 1, true, true, true, "Quality", false, "8", "1024", true, 0.010F, 0.008F, 0.2F);
                });
                RegisterAction(GivemesomeFPSHairOnlyAction);

                btn = CreateButton("Hair FPS - Safe");
                btn.button.onClick.AddListener(() => { ApplyHairPreset(true, 16, 3, 0.00045f, 1.0f, 0.2f, 1, "Quality", false); });
                JSONStorableAction HairFPSSafeAction = new JSONStorableAction("Hair FPS - Safe", () => {
                    ApplyHairPreset(true, 16, 3, 0.00045f, 1.0f, 0.2f, 1, "Quality", false);
                });
                RegisterAction(HairFPSSafeAction);

                btn = CreateButton("Hair FPS - Strong");
                btn.button.onClick.AddListener(() => { ApplyHairPreset(true, 10, 2, 0.00055f, 0.8f, 0.35f, 1, "Fast", false); });
                JSONStorableAction HairFPSStrongAction = new JSONStorableAction("Hair FPS - Strong", () => {
                    ApplyHairPreset(true, 10, 2, 0.00055f, 0.8f, 0.35f, 1, "Fast", false);
                });
                RegisterAction(HairFPSStrongAction);

                btn = CreateButton("Hair FPS - Max");
                btn.button.onClick.AddListener(() => { ApplyHairPreset(false, 6, 1, 0.00065f, 0.5f, 0.5f, 1, "Fast", true); });
                JSONStorableAction HairFPSMaxAction = new JSONStorableAction("Hair FPS - Max", () => {
                    ApplyHairPreset(false, 6, 1, 0.00065f, 0.5f, 0.5f, 1, "Fast", true);
                });
                RegisterAction(HairFPSMaxAction);

                btn = CreateButton("Give me ALL the FPS!");
                btn.button.onClick.AddListener(() => { ExecuteGiveMeFPS(false, 10, 2, 0.00065000f, 1, 1, 0.5F, 1.0F, 1, false, false, false, "Fast", true, "1", "512", false, 0.010F, 0.008F, 0.2F); });
                JSONStorableAction GivemeALLtheFPSAction = new JSONStorableAction("Give me ALL the FPS!", () => {
                    ExecuteGiveMeFPS(false, 10, 2, 0.00065000f, 1, 1, 0.5F, 1.0F, 1, false, false, false, "Fast", true, "1", "512", false, 0.010F, 0.008F, 0.2F);
                });
                RegisterAction(GivemeALLtheFPSAction);

                btn = CreateButton("VAM Defaults");
                btn.button.onClick.AddListener(() => { ExecuteGiveMeFPS(true, 16, 16, 0.00010000f, 1.5f, 2, 0.5F, 1.0F, 1, true, true, true, "Quality", false , "8", "1024", false, 0.010F, 0.008F, 0.2F); });
                JSONStorableAction VAMDefaultsAction = new JSONStorableAction("VAM Defaults", () => {
                    ExecuteGiveMeFPS(true, 16, 16, 0.00010000f, 1.5f, 2, 0.5F, 1.0F, 1, true, true, true, "Quality", false , "8", "1024", false, 0.010F, 0.008F, 0.2F);
                });
                RegisterAction(VAMDefaultsAction);

                //Override scene preferences
                SetupInfoText(this,
                    "<color=#606060><size=40><b>Override Atom Settings On Scene Load</b></size>\n" +
                    "Tick this to apply these settings when loading any scene. " +
                    "Make sure to save 'Session Plugin Presets -> Change User Defaults -> Set Current As User Defaults' to make this permanent. Does not include performance preferences </color>\n\n",
                    380.0f, false
                );
                overrideScenePreferencesState = new JSONStorableBool("Override On Scene Load ", false);
                RegisterBool(overrideScenePreferencesState);
                UIDynamicToggle onToggleoverrideScenePreferencesState = CreateToggle(overrideScenePreferencesState, false);
                onToggleoverrideScenePreferencesState.toggle.onValueChanged.AddListener((on) =>
                {});

                //Preferences - Performance
                SetupInfoText(this, "<color=#606060><size=40><b>Performance Preferences</b></size></color>", 20.0f, false);

                //renderscale
                renderScaleValue = new JSONStorableFloat("Render Scale", UserPreferences.singleton.renderScale, renderScaleCallback, 0.5f, 2.0f, true);
                RegisterFloat(renderScaleValue);
                renderScaleSlider = CreateSlider(renderScaleValue, false);
                renderScaleSlider.rangeAdjustEnabled = false;
                renderScaleSlider.autoSetQuickButtons = false;
                renderScaleSlider.ConfigureQuickButtons(-0.01f, -0.05f, -0.10f, -0.25f, 0.01f, 0.05f, 0.10f, 0.25f);
                //renderScaleSlider.quickButtonsEnabled  = false;

                //Soft physics
                softPhysicsState = new JSONStorableBool("Soft Body Physics", UserPreferences.singleton.softPhysics);
                softPhysicsState_orginal = new JSONStorableBool("Soft Body Physics", UserPreferences.singleton.softPhysics);
                RegisterBool(softPhysicsState);
                onTogglesoftPhysicsState = CreateToggle(softPhysicsState, false);
                onTogglesoftPhysicsState.toggle.onValueChanged.AddListener((on) =>
                {
                    TogglesoftPhysicsFPS(on);
                });

                //physicsHighQuality
                physicsHighQualityState = new JSONStorableBool("High Quality Physics", UserPreferences.singleton.physicsHighQuality);
                physicsHighQualityState_orginal = new JSONStorableBool("High Quality Physics", UserPreferences.singleton.physicsHighQuality);
                RegisterBool(physicsHighQualityState);
                onTogglephysicsHighQualityState = CreateToggle(physicsHighQualityState, false);
                onTogglephysicsHighQualityState.toggle.onValueChanged.AddListener((on) =>
                {
                    TogglephysicsHighQualityFPS(on);
                });

                //mirrorReflections
                mirrorReflectionsState = new JSONStorableBool("Mirror Reflection", UserPreferences.singleton.mirrorReflections);
                mirrorReflectionsState_orginal = new JSONStorableBool("Mirror Reflection", UserPreferences.singleton.mirrorReflections);
                RegisterBool(mirrorReflectionsState);
                onTogglemirrorReflectionsState = CreateToggle(mirrorReflectionsState, false);
                onTogglemirrorReflectionsState.toggle.onValueChanged.AddListener((on) =>
                {
                    TogglemirrorReflectionsFPS(on);
                });

                //realtimeReflectionProbes
                realtimeReflectionProbesState = new JSONStorableBool("Realtime Reflection Probes", UserPreferences.singleton.realtimeReflectionProbes);
                realtimeReflectionProbesState_orginal = new JSONStorableBool("Realtime Reflection Probes", UserPreferences.singleton.realtimeReflectionProbes);
                RegisterBool(realtimeReflectionProbesState);
                onTogglerealtimeReflectionProbesState = CreateToggle(realtimeReflectionProbesState, false);
                onTogglerealtimeReflectionProbesState.toggle.onValueChanged.AddListener((on) =>
                {
                    TogglerealtimeReflectionProbesFPS(on);
                });

                //SHADER
                ShaderLODChoices.Add("Low");
                ShaderLODChoices.Add("Medium");
                ShaderLODChoices.Add("High");
                ShaderLODChooser = new JSONStorableStringChooser("Shader Quality", ShaderLODChoices, UserPreferences.singleton.shaderLOD.ToString(), "Shader Quality", doShaderLODChoice);
                ShaderLODChooser_orginal = new JSONStorableStringChooser("Shader Quality", ShaderLODChoices, UserPreferences.singleton.shaderLOD.ToString(), "Shader Quality", doShaderLODChoice);
                RegisterStringChooser(ShaderLODChooser);
                CreatePopup(ShaderLODChooser, false);

                //msaa
                msaaLevelChoices.Add("Off");
                msaaLevelChoices.Add("2x");
                msaaLevelChoices.Add("4x");
                msaaLevelChoices.Add("8x");
                switch( UserPreferences.singleton.msaaLevel)
                {
                  case 0:
                    msaa_popup = "Off";
                    break;
                  case 2:
                    msaa_popup = "2x";
                    break;
                  case 4:
                    msaa_popup = "4x";
                    break;
                  case 8:
                    msaa_popup = "8x";
                    break;
                  default:
                    msaa_popup = "8x";
                    break;
                }
                msaaLevelchooser = new JSONStorableStringChooser("MSAA Level", msaaLevelChoices, msaa_popup, "MSAA Level", doMSAAlevelChoice);
                msaaLevelchooser_orginal = new JSONStorableStringChooser("MSAA Level", msaaLevelChoices, msaa_popup, "MSAA Level", doMSAAlevelChoice);
                RegisterStringChooser(msaaLevelchooser);
                CreatePopup(msaaLevelchooser, false);

                //pixelLightCount
                pixelLightCountValue = new JSONStorableFloat("Pixel Light Count", UserPreferences.singleton.pixelLightCount, pixelLightCountCallback, 0f, 6, true);
                pixelLightCountValue_orginal = new JSONStorableFloat("Pixel Light Count", UserPreferences.singleton.pixelLightCount, pixelLightCountCallback, 0f, 6, true);
                RegisterFloat(pixelLightCountValue);
                pixelLightCountSlider = CreateSlider(pixelLightCountValue, false);
                pixelLightCountSlider.slider.wholeNumbers = true;
                pixelLightCountSlider.rangeAdjustEnabled = false;
                pixelLightCountSlider.quickButtonsEnabled  = false;

                //smooth passes
                smoothPassesValue = new JSONStorableFloat("Smooth Passes", UserPreferences.singleton.smoothPasses, smoothPassesCallback, 0, 4, true);
                smoothPassesValue_orginal = new JSONStorableFloat("Smooth Passes", UserPreferences.singleton.smoothPasses, smoothPassesCallback, 0, 4, true);
                RegisterFloat(smoothPassesValue);
                smoothPassesSlider = CreateSlider(smoothPassesValue, false);
                smoothPassesSlider.slider.wholeNumbers = true;
                smoothPassesSlider.rangeAdjustEnabled = false;
                smoothPassesSlider.quickButtonsEnabled  = false;

                //Glow
                GlowEffectsLevelChoices.Add("Off");
                GlowEffectsLevelChoices.Add("Low");
                GlowEffectsLevelChoices.Add("High");
                GlowEffectsLevelChooser = new JSONStorableStringChooser("Glow Effects Level", GlowEffectsLevelChoices, UserPreferences.singleton.glowEffects.ToString(), "Glow Effects Level", doGlowEffectsLevelChoice);
                RegisterStringChooser(GlowEffectsLevelChooser);
                CreatePopup(GlowEffectsLevelChooser, false);

                //PhysicsRat
                physicsRateChoices.Add("Auto");
                physicsRateChoices.Add("45");
                physicsRateChoices.Add("60");
                physicsRateChoices.Add("72");
                physicsRateChoices.Add("80");
                physicsRateChoices.Add("90");
                physicsRateChoices.Add("120");
                physicsRateChoices.Add("144");
                physicsRateChoices.Add("240");
                physicsRateChoices.Add("288");
                physicsRateChooser = new JSONStorableStringChooser("Physics Rate", physicsRateChoices, UserPreferences.singleton.physicsRate.ToString(), "Physics Rate", dophysicsRateChoice);
                RegisterStringChooser(physicsRateChooser);
                CreatePopup(physicsRateChooser, false);

                //physicsUpdateCap
                physicsUpdateCapValue = new JSONStorableFloat("Physics Update Cap", UserPreferences.singleton.physicsUpdateCap, physicsUpdateCapCallback, 1, 3, true);
                RegisterFloat(physicsUpdateCapValue);
                physicsUpdateCapSlider = CreateSlider(physicsUpdateCapValue, false);
                physicsUpdateCapSlider.slider.wholeNumbers = true;
                physicsUpdateCapSlider.rangeAdjustEnabled = false;
                physicsUpdateCapSlider.quickButtonsEnabled  = false;

                //start if user fine tuning
                SetupInfoText(this, "<color=#606060><size=40><b>All Person Settings</b></size></color>", 20.0f, true);

                //Softbody physics
                BreastPhysicsState = new JSONStorableBool("Disable Breast softbody Sim", false);
                RegisterBool(BreastPhysicsState);
                onToggleBreastPhysicsState = CreateToggle(BreastPhysicsState, true);
                onToggleBreastPhysicsState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleBreastPhysicsFPS(on);
                });

                LowerPhysicsState = new JSONStorableBool("Disable Glute softbody Sim", false);
                RegisterBool(LowerPhysicsState);
                onToggleLowerPhysicsState = CreateToggle(LowerPhysicsState, true);
                onToggleLowerPhysicsState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleLowerPhysicsFPS(on);
                });

                TonguePhysicsState = new JSONStorableBool("Disable Tongue softbody Sim", false);
                RegisterBool(TonguePhysicsState);
                onToggleTonguePhysicsState = CreateToggle(TonguePhysicsState, true);
                onToggleTonguePhysicsState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleTonguePhysicsFPS(on);
                });

                //AdvancedColliders toggle
                AdvancedCollidersState = new JSONStorableBool("Disable All Advanced Colliders ", false);
                AdvancedCollidersState_orginal = new JSONStorableBool("Disable All Advanced Colliders ", false);
                RegisterBool(AdvancedCollidersState);
                UIDynamicToggle onToggleAdvancedCollidersState = CreateToggle(AdvancedCollidersState, true);
                onToggleAdvancedCollidersState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleAdvancedCollidersFPS(on);
                });

                //reflection controls
                disablePixelLightsState = new JSONStorableBool("Disable Reflection pixel shader", false);
                RegisterBool(disablePixelLightsState);
                onToggledisablePixelLightState = CreateToggle(disablePixelLightsState, true);
                onToggledisablePixelLightState.toggle.onValueChanged.AddListener((on) =>
                {
                    onToggledisablePixelLightStateFPS(on);
                });

                reflectionTextureChoices.Add("512");
                reflectionTextureChoices.Add("1024");
                reflectionTextureChoices.Add("2048");
                reflectionTextureChoices.Add("4096");
                reflectionTextureSizeChooser = new JSONStorableStringChooser("Reflection Texture Size", reflectionTextureChoices, "1024", "Reflection Texture Size", doReflectionTextureChoice);
                RegisterStringChooser(reflectionTextureSizeChooser);
                CreatePopup(reflectionTextureSizeChooser, true);

                //Hair sim
                HairSimulationState = new JSONStorableBool("Disable all Hair Sim", false);
                RegisterBool(HairSimulationState);
                onToggleHairSimulationState = CreateToggle(HairSimulationState, true);
                onToggleHairSimulationState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleHairSimulationFPS(on);
                });

                DisableHairRenderersState = new JSONStorableBool("Hide Hair Renderers", false);
                RegisterBool(DisableHairRenderersState);
                onToggleDisableHairRenderersState = CreateToggle(DisableHairRenderersState, true);
                onToggleDisableHairRenderersState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleHairRenderersFPS(on);
                });

                //hair sliders, sim iterations & shader control
                HairMultiplierValue = new JSONStorableFloat("Hair Multiplier", 3f, HairMultiplierCallback, 1f, 64f, false);
                RegisterFloat(HairMultiplierValue);
                HairMultiplierSlider = CreateSlider(HairMultiplierValue, true);
                HairMultiplierSlider.slider.wholeNumbers = true;
                HairMultiplierSlider.rangeAdjustEnabled = false;
                HairMultiplierSlider.autoSetQuickButtons = false;
                HairMultiplierSlider.ConfigureQuickButtons(-1.0f, -2.0f, -5.0f, -10.0f, 1.0f, 2.0f, 5.0f, 10.0f);

                CurveDensityValue = new JSONStorableFloat("Curve Density", 16f, CurveDensityCallback, 2f, 64f, false);
                RegisterFloat(CurveDensityValue);
                CurveDensitySlider = CreateSlider(CurveDensityValue, true);
                CurveDensitySlider.slider.wholeNumbers = true;
                CurveDensitySlider.rangeAdjustEnabled = false;
                CurveDensitySlider.autoSetQuickButtons = false;
                CurveDensitySlider.ConfigureQuickButtons(-1.0f, -2.0f, -5.0f, -10.0f, 1.0f, 2.0f, 5.0f, 10.0f);

                HairWidthValue = new JSONStorableFloat("Hair Width", 0.00045f, HairWidthValueCallback, 0.00000f, 0.00100f, false);
                RegisterFloat(HairWidthValue);
                HairWidthValueSlider = CreateSlider(HairWidthValue, true);
                HairWidthValueSlider.rangeAdjustEnabled = false;
                HairWidthValueSlider.valueFormat = "F5";

                HairWeightValue = new JSONStorableFloat("Hair Weight", 1.0f, HairWeightValueCallback, 0.00000f, 2.0f, false);
                RegisterFloat(HairWeightValue);
                HairWeightValueSlider = CreateSlider(HairWeightValue, true);
                HairWeightValueSlider.rangeAdjustEnabled = false;
                HairWeightValueSlider.valueFormat = "F5";

                HairFrictionValue = new JSONStorableFloat("Hair Friction", 0.2f, HairFrictionValueCallback, 0.00000f, 1.0f, false);
                RegisterFloat(HairFrictionValue);
                HairFrictionValueSlider = CreateSlider(HairFrictionValue, true);
                HairFrictionValueSlider.rangeAdjustEnabled = false;
                HairFrictionValueSlider.valueFormat = "F5";

                HairIterationsValue = new JSONStorableFloat("Sim iterations", 1f, HairIterationsCallback, 1f, 5f, false);
                RegisterFloat(HairIterationsValue);
                HairIterationsSlider = CreateSlider(HairIterationsValue, true);
                HairIterationsSlider.slider.wholeNumbers = true;
                HairIterationsSlider.rangeAdjustEnabled = false;
                HairIterationsSlider.quickButtonsEnabled  = false;

                shaderChoices.Add("Fast");
                shaderChoices.Add("Quality");
                shaderChoices.Add("QualityThicken");
                shaderChoices.Add("QualityThickenMore");
                shaderChooser = new JSONStorableStringChooser("Hair shader", shaderChoices, "Quality", "Hair shader", doShaderChoice);
                RegisterStringChooser(shaderChooser);
                CreatePopup(shaderChooser, true);

                //cloth sim toggle
                clothSimState = new JSONStorableBool("Disable all cloth Sim ", false);
                clothSimState_orginal = new JSONStorableBool("Disable all cloth Sim ", false);
                RegisterBool(clothSimState);
                UIDynamicToggle onToggleClothSimState = CreateToggle(clothSimState, true);
                onToggleClothSimState.toggle.onValueChanged.AddListener((on) =>
                {
                    ToggleSimClothFPS(on);
                });

                overrideClothParametersState = new JSONStorableBool("Enable Cloth override", false);
                RegisterBool(overrideClothParametersState);
                UIDynamicToggle onToggleoverrideClothParametersState = CreateToggle(overrideClothParametersState, true);
                onToggleoverrideClothParametersState.toggle.onValueChanged.AddListener((on) =>
                {});

                ClothStiffnessValue = new JSONStorableFloat("Cloth Stiffness", 1.0f, ClothStiffnessValueCallback, 0.00000f, 1.0f, false);
                RegisterFloat(ClothStiffnessValue);
                ClothStiffnessValueSlider = CreateSlider(ClothStiffnessValue, true);
                ClothStiffnessValueSlider.rangeAdjustEnabled = false;
                ClothStiffnessValueSlider.valueFormat = "F3";

                ClothWeightValue = new JSONStorableFloat("Cloth Weight", 0.5f, ClothWeightValueCallback, 0.00000f, 2.0f, false);
                RegisterFloat(ClothWeightValue);
                ClothWeightValueSlider = CreateSlider(ClothWeightValue, true);
                ClothWeightValueSlider.rangeAdjustEnabled = false;
                ClothWeightValueSlider.valueFormat = "F3";

                ClothIterationsValue = new JSONStorableFloat("Cloth Sim iterations", 1f, ClothIterationsValueCallback, 1f, 7f, false);
                RegisterFloat(ClothIterationsValue);
                ClothIterationsValueSlider = CreateSlider(ClothIterationsValue, true);
                ClothIterationsValueSlider.slider.wholeNumbers = true;
                ClothIterationsValueSlider.rangeAdjustEnabled = false;
                ClothIterationsValueSlider.quickButtonsEnabled  = false;

                ClothCollisonPowerValue = new JSONStorableFloat("Cloth Collison Power", 0.500F, ClothCollisonPowerValueCallback, 0.00000f, 1.0f, false);
                RegisterFloat(ClothCollisonPowerValue);
                ClothCollisonPowerValueSlider = CreateSlider(ClothCollisonPowerValue, true);
                ClothCollisonPowerValueSlider.rangeAdjustEnabled = false;
                ClothCollisonPowerValueSlider.valueFormat = "F3";

                ClothCollisonRadiusValue = new JSONStorableFloat("Cloth Collison Radius", 0.010F, ClothCollisonRadiusValueCallback, 0.00000f, 0.1f, false);
                RegisterFloat(ClothCollisonRadiusValue);
                ClothCollisonRadiusValueSlider = CreateSlider(ClothCollisonRadiusValue, true);
                ClothCollisonRadiusValueSlider.rangeAdjustEnabled = false;
                ClothCollisonRadiusValueSlider.valueFormat = "F3";


                SetupInfoText(this,
                    "<color=#606060><size=40><b>Dynamic Adjust on load</b></size>\n" +
                    "This will set glute softbody physics off & disable all advanced colliders when scene contains more than the number of girls (males not counted)</color>\n\n",
                    237.0f, true
                );

                //DynamicAdjustState toggle
                DynamicAdjustState = new JSONStorableBool("Dynamic Adjust on load ", false);
                RegisterBool(DynamicAdjustState);
                UIDynamicToggle onToggleDynamicAdjustStateState = CreateToggle(DynamicAdjustState, true);
                onToggleDynamicAdjustStateState.toggle.onValueChanged.AddListener((on) =>
                {
                    DynamicAdjustState.val = on;
                });

                NumberDynamicPersonsValue = new JSONStorableFloat("Number of persons ", 1f, 1f, 5f, false);
                RegisterFloat(NumberDynamicPersonsValue);
                NumberDynamicPersonsSlider = CreateSlider(NumberDynamicPersonsValue, true);
                NumberDynamicPersonsSlider.slider.wholeNumbers = true;
                NumberDynamicPersonsSlider.rangeAdjustEnabled = false;
                NumberDynamicPersonsSlider.quickButtonsEnabled  = false;

                SetupInfoText(this,
                    "<color=#606060><size=40><b>CPU Bench FPS</b></size>\n" +
                    "WARNING - These buttons will adjust performance parameters to reduce as much load on the GPU as possible - this should show how many FPS a good GPU should be able to give - suggest use 2 or more persons in a scene to test\n\n" +
                    "The real killer of CPU you'll find is the softbody physics - this is why I recommend switching of tongue and glute softbody physics as the best compromise</color>\n\n",
                    530.0f, false
                );

                btn = CreateButton("CPU Bench - Soft Body physics On", false);
                btn.button.onClick.AddListener(() => { CPUbench(); });

                btn = CreateButton("CPU Bench - Soft Body physics Off", false);
                btn.button.onClick.AddListener(() => { CPUbenchNoSoftPhysics(); });

                btn = CreateButton("Restore Performance Parameters", false);
                btn.button.onClick.AddListener(() => { restore_vam_performance_parameters(); });

                SetupInfoText(this, 
                    "<color=#606060><size=40><b>Give Me FPS v3.5.2</b></size>\nA Session Plugin.\n" +
                    //"These will set softbody physics for Tongue, breast & glute on/off to gain fps\n\n" +
                    "4 Quick buttons and cloth sim - with user fine tuning of the options the 4 buttons use + performance preferences for easy access</color>\n\n" +
                    "<b>Give me some FPS - Recommend:</b> Turns off Tongue & Glute softbody physics, breasts on, Hair Curve Density 16 - Multiplier 3 - strand width 0.00045 - iterations 1, Quality hair shader, disable pixel lights reflections and anti aliasing 1\n\n" +
                    "<b>Give me some FPS - Hair Only:</b> Hair same as recommend but all other options VAM default\n\n" +
                    "<b>Give me ALL the FPS!:</b> In addition to recommended - Turns off breast softbody physics, Hair sim off, Hair Curve Density 10 - Multiplier 2, reflections texture size 512.\n\n" +
                    "<b>Default:</b> Switch all settings back to VAM defaults (this isn't the same as the scene loaded with)\n\n" +
                    "<b>Toggle all clothes Sim On/Off:</b> Toggles all clothing simulation Off / On \n\n" +
                    "<color=#606060><size=40><b>Adjust Person On Load Look</b></size></color>\n" +
                    "The plugin will attempt to adjust a person on loading a look\n\n" +
                    "However it's only possible to do this if the name of the look changes (OnAtomUIDsChanged)\n\n" +
                    "If the name of the loaded look is the same as the current person, it won't apply the settings\n\n",
                    1580.0f, false
                );

                StartCoroutine(WaitForLoadingComplete());

            }
            catch (Exception e)
            {
                SuperController.LogError("Failed to initialize plugin: " + e);
            }
        }

        void OnAtomAdded(Atom atom)
        {
            if(atom.type == "Person")
            {
                JSONStorable appearancePresets = atom.GetStorableByID("AppearancePresets");
                if (appearancePresets != null)
                {
                    pm = appearancePresets.GetComponent<PresetManager>();
                    if (pm != null)
                    {
                        pm.postLoadEvent.AddListener(OnPresetLoaded);
                    }
                }
            }
        }

        void OnDestroy()
        {
            SuperController.singleton.onAtomAddedHandlers -= OnAtomAdded;
            SuperController.singleton.onSceneLoadedHandlers -= OnSceneLoaded;
            SuperController.singleton.onAtomUIDsChangedHandlers -= OnAtomUIDsChanged;
        }

        private void OnPresetLoaded() {
            UpdateScene();
        }

        private IEnumerator WaitForLoadingComplete()
        {
            while (SuperController.singleton.isLoading)
            {
                yield return null;
            }

            while (SuperController.singleton.freezeAnimation)
            {
                yield return null;
            }

            yield return null;

            _ready = true;
        }

        private void OnSceneLoaded()
        {
            try {
                if (overrideScenePreferencesState.val) {
                    count=0;
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom.type == "Person")
                        {
                            JSONStorable geometry = atom.GetStorableByID("geometry");
                            DAZCharacterSelector character = geometry as DAZCharacterSelector;
                            if (!character.selectedCharacter.isMale) {
                                count++;
                            }
                        }
                    }
                    SuperController.LogMessage("GiveMeFPS - updated scene with user preferences");
                    ToggleHairSimulationFPS(HairSimulationState.val);
                    HairMultiplierCallback(HairMultiplierValue);
                    CurveDensityCallback(CurveDensityValue);
                    HairWidthValueCallback(HairWidthValue);
                    HairWeightValueCallback(HairWeightValue);
                    HairFrictionValueCallback(HairFrictionValue);
                    HairIterationsCallback(HairIterationsValue);
                    ToggleHairRenderersFPS(DisableHairRenderersState.val);
                    if (overrideClothParametersState.val) {
                        ClothWeightValueCallback(ClothWeightValue);
                        ClothStiffnessValueCallback(ClothStiffnessValue);
                        ClothIterationsValueCallback(ClothIterationsValue);
                        ClothCollisonPowerValueCallback(ClothCollisonPowerValue);
                        ClothCollisonRadiusValueCallback(ClothCollisonRadiusValue);
                    }
                    doShaderChoice(shaderChooser.val);
                    ToggleBreastPhysicsFPS(BreastPhysicsState.val);

                    if (DynamicAdjustState.val) {
                        if (count > NumberDynamicPersonsValue.val) {
                            LowerPhysicsState.val=true;
                            AdvancedCollidersState.val=true;
                        } else {
                            LowerPhysicsState.val=false;
                            AdvancedCollidersState.val=false;
                        }
                    }
                    ToggleLowerPhysicsFPS(LowerPhysicsState.val);
                    ToggleTonguePhysicsFPS(TonguePhysicsState.val);
                    doReflectionTextureChoice(reflectionTextureSizeChooser.val);
                    ToggleAdvancedCollidersFPS(AdvancedCollidersState.val);
                    onToggledisablePixelLightStateFPS(disablePixelLightsState.val);
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught in OnSceneLoaded: " + e);
            }
        }

        private void OnAtomUIDsChanged(List<string> atomUIDs)
        {
            if (!_ready) return;
            var sctrl = SuperController.singleton;
            if (sctrl.isLoading) return;
            UpdateScene();
        }

        private void UpdateScene()
        {
            try {
                if (overrideScenePreferencesState.val) {
                    StartCoroutine(DoClothAndHairCallbacks());

                    doShaderChoice(shaderChooser.val);
                    ToggleBreastPhysicsFPS(BreastPhysicsState.val);
                    ToggleLowerPhysicsFPS(LowerPhysicsState.val);
                    ToggleTonguePhysicsFPS(TonguePhysicsState.val);
                    doReflectionTextureChoice(reflectionTextureSizeChooser.val);
                    ToggleAdvancedCollidersFPS(AdvancedCollidersState.val);
                    onToggledisablePixelLightStateFPS(disablePixelLightsState.val);
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught in OnAtomUIDsChanged: " + e);
            }
        }

        private IEnumerator DoClothAndHairCallbacks()
        {
            cloth_ready_count=0;
            while (true)
            {
                cloth_ready_count++;
                yield return new WaitForSeconds(0.51f);
                hair_ready=true;
                cloth_ready=true;
                foreach (Atom atom in SuperController.singleton.GetAtoms())
                {
                    if (atom.type == "Person")
                    {
                        atom_name = atom.name;
                        foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                        {
                            HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                            if (hairControl==null) {
                                hair_ready=false;
                            }
                        }
                        foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                        {
                            ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                            if (clothControl==null) {
                                cloth_ready=false;
                            }
                        }
                    }
                }

                if(hair_ready || cloth_ready_count>=5){
                    ToggleHairSimulationFPS(HairSimulationState.val);
                    HairMultiplierCallback(HairMultiplierValue);
                    CurveDensityCallback(CurveDensityValue);
                    HairWidthValueCallback(HairWidthValue);
                    HairWeightValueCallback(HairWeightValue);
                    HairFrictionValueCallback(HairFrictionValue);
                    HairIterationsCallback(HairIterationsValue);
                    ToggleHairRenderersFPS(DisableHairRenderersState.val);
                    if (overrideClothParametersState.val && (cloth_ready || cloth_ready_count>=5)) {
                        ClothStiffnessValueCallback(ClothStiffnessValue);
                        ClothWeightValueCallback(ClothWeightValue);
                        ClothIterationsValueCallback(ClothIterationsValue);
                        ClothCollisonPowerValueCallback(ClothCollisonPowerValue);
                        ClothCollisonRadiusValueCallback(ClothCollisonRadiusValue);
                    }
                }
                if(hair_ready && cloth_ready){
                    if (overrideClothParametersState.val ){
                        SuperController.LogMessage("GiveMeFPS - updated hair and cloth preferences");
                    }else{
                        SuperController.LogMessage("GiveMeFPS - updated hair preferences");
                    }
                    yield break;
                }

                if(cloth_ready_count>=5){
                    if (overrideClothParametersState.val ){
                        SuperController.LogMessage("GiveMeFPS - updated hair and cloth preferences");
                    }else{
                        SuperController.LogMessage("GiveMeFPS - updated hair preferences");
                    }
                    yield break;
                }
            }
        }

        //CPU Bench
        public void CPUbench()
        {
            if (first_time_bench){
                store_vam_performance_parameters();
                first_time_bench = false;
            }
            clothSimState.val = true;
            AdvancedCollidersState.val = false;
            softPhysicsState.val = true;
            msaaLevelchooser.val = "Off";
            pixelLightCountValue.val = 0;
            mirrorReflectionsState.val = false;
            smoothPassesValue.val = 0 ;
            ShaderLODChooser.val ="Low";
            ExecuteGiveMeFPS(false, 10, 2, 0.00065000f, 1, 1, 0.5F, 1.0F, 1, true, true, true, "Fast", true, "1", "512", false, 0.010F, 0.008F, 0.2F);
        }

        //CPU Bench
        public void CPUbenchNoSoftPhysics()
        {
            if (first_time_bench){
                store_vam_performance_parameters();
                first_time_bench = false;
            }
            clothSimState.val = true;
            AdvancedCollidersState.val = true;
            softPhysicsState.val = false;
            msaaLevelchooser.val = "Off";
            pixelLightCountValue.val = 0;
            mirrorReflectionsState.val = false;
            smoothPassesValue.val = 0 ;
            ShaderLODChooser.val ="Low";
            ExecuteGiveMeFPS(false, 10, 2, 0.00065000f, 1, 1, 0.5F, 1.0F, 1, false, false, false, "Fast", true, "1", "512", false, 0.010F, 0.008F, 0.2F);
        }

        //CPU Bench
        public void store_vam_performance_parameters()
        {
            clothSimState_orginal.val = clothSimState.val;
            AdvancedCollidersState_orginal.val = AdvancedCollidersState.val;
            softPhysicsState_orginal.val = softPhysicsState.val;
            msaaLevelchooser_orginal.val = msaaLevelchooser.val;
            pixelLightCountValue_orginal.val = pixelLightCountValue.val;
            mirrorReflectionsState_orginal.val = mirrorReflectionsState.val;
            smoothPassesValue_orginal.val = smoothPassesValue.val;
            ShaderLODChooser_orginal.val = ShaderLODChooser.val;
        }

        //CPU Bench
        public void restore_vam_performance_parameters()
        {
            clothSimState.val = clothSimState_orginal.val;
            AdvancedCollidersState.val = AdvancedCollidersState_orginal.val;
            softPhysicsState.val = softPhysicsState_orginal.val;
            msaaLevelchooser.val = msaaLevelchooser_orginal.val;
            pixelLightCountValue.val = pixelLightCountValue_orginal.val;
            mirrorReflectionsState.val = mirrorReflectionsState_orginal.val;
            smoothPassesValue.val = smoothPassesValue_orginal.val;
            ShaderLODChooser.val = ShaderLODChooser_orginal.val;
            ExecuteGiveMeFPS(true, 16, 16, 0.00010000f, 1, 2, 0.5F, 1.0F, 1, true, true, true, "Quality", false , "8", "1024", false, 0.010F, 0.008F, 0.2F);
        }

        //Preferences
        public void TogglemirrorReflectionsFPS(bool mirrorReflectionsState)
        {
            UserPreferences.singleton.mirrorReflections = mirrorReflectionsState;
        }

        public void TogglerealtimeReflectionProbesFPS(bool realtimeReflectionProbesState)
        {
            UserPreferences.singleton.realtimeReflectionProbes = realtimeReflectionProbesState;
        }

        public void TogglephysicsHighQualityFPS(bool physicsHighQualityState)
        {
            UserPreferences.singleton.physicsHighQuality = physicsHighQualityState;
        }

        public void TogglesoftPhysicsFPS(bool softPhysicsState)
        {
            UserPreferences.singleton.softPhysics = softPhysicsState;
        }

        public void pixelLightCountCallback(JSONStorableFloat pixelLightCountValue)
        {
            if ( pixelLightCountValue.val >= 0f && pixelLightCountValue.val <= 6f)
            {
            UserPreferences.singleton.pixelLightCount = (int)pixelLightCountValue.val;
            }
        }

        public void physicsUpdateCapCallback(JSONStorableFloat physicsUpdateCapValue)
        {
            if ( physicsUpdateCapValue.val >= 1f && physicsUpdateCapValue.val <= 3f)
            {
            UserPreferences.singleton.physicsUpdateCap = (int)physicsUpdateCapValue.val;
            }
        }

        public void renderScaleCallback(JSONStorableFloat renderScaleValue)
        {
            if ( renderScaleValue.val >= 0.5f && renderScaleValue.val <= 2.0f)
            {
            UserPreferences.singleton.renderScale = renderScaleValue.val;
            }
        }

        public void smoothPassesCallback(JSONStorableFloat smoothPassesValue)
        {
            if ( smoothPassesValue.val >= 0f && smoothPassesValue.val <= 4f)
            {
            UserPreferences.singleton.smoothPasses = (int)smoothPassesValue.val;
            }
        }

        public void doShaderLODChoice(string ShaderChoice)
        {
            switch(ShaderChoice)
            {
              case "Low":
                UserPreferences.singleton.shaderLOD = UserPreferences.ShaderLOD.Low;
                break;
              case "Medium":
                UserPreferences.singleton.shaderLOD = UserPreferences.ShaderLOD.Medium;
                break;
              case "High":
                UserPreferences.singleton.shaderLOD = UserPreferences.ShaderLOD.High;
                break;
              default:
                UserPreferences.singleton.shaderLOD = UserPreferences.ShaderLOD.Medium;
                break;
            }
        }

        public void doGlowEffectsLevelChoice(string GlowEffectsLevel)
        {
            switch(GlowEffectsLevel)
            {
              case "Off":
                UserPreferences.singleton.glowEffects = UserPreferences.GlowEffectsLevel.Off;
                break;
              case "Low":
                UserPreferences.singleton.glowEffects = UserPreferences.GlowEffectsLevel.Low;
                break;
              case "High":
                UserPreferences.singleton.glowEffects = UserPreferences.GlowEffectsLevel.High;
                break;
              default:
                UserPreferences.singleton.glowEffects = UserPreferences.GlowEffectsLevel.High;
                break;
            }
        }

        public void dophysicsRateChoice(string physicsRate)
        {
            switch(physicsRate)
            {
              case "Auto":
                UserPreferences.singleton.SetPhysicsRateFromString("Auto");
                break;
              case "45":
                UserPreferences.singleton.SetPhysicsRateFromString("_45");
                break;
              case "60":
                UserPreferences.singleton.SetPhysicsRateFromString("_60");
                break;
              case "72":
                UserPreferences.singleton.SetPhysicsRateFromString("_72");
                break;
              case "80":
                UserPreferences.singleton.SetPhysicsRateFromString("_80");
                break;
              case "90":
                UserPreferences.singleton.SetPhysicsRateFromString("_90");
                break;
              case "120":
                UserPreferences.singleton.SetPhysicsRateFromString("_120");
                break;
              case "144":
                UserPreferences.singleton.SetPhysicsRateFromString("_144");
                break;
              case "240":
                UserPreferences.singleton.SetPhysicsRateFromString("_240");
                break;
              case "288":
                UserPreferences.singleton.SetPhysicsRateFromString("_288");
                break;
              default:
                UserPreferences.singleton.SetPhysicsRateFromString("Auto");
                break;
            }
        }

        public void doMSAAlevelChoice(string msaaChoice)
        {
            switch(msaaChoice)
            {
              case "Off":
                UserPreferences.singleton.msaaLevel = 0;
                break;
              case "2x":
                UserPreferences.singleton.msaaLevel = 2;
                break;
              case "4x":
                UserPreferences.singleton.msaaLevel = 4;
                break;
              case "8x":
                UserPreferences.singleton.msaaLevel = 8;
                break;
              default:
                UserPreferences.singleton.msaaLevel = 8;
                break;
            }
        }

        private void SetHairBool(HairSimControl hairControl, string paramName, bool value)
        {
            try
            {
                hairControl.SetBoolParamValue(paramName, value);
            }
            catch
            {
            }
        }

        private void SetHairFloat(HairSimControl hairControl, string paramName, float value)
        {
            try
            {
                hairControl.SetFloatParamValue(paramName, value);
            }
            catch
            {
            }
        }

        private void SetHairString(HairSimControl hairControl, string paramName, string value)
        {
            try
            {
                hairControl.SetStringChooserParamValue(paramName, value);
            }
            catch
            {
            }
        }

        private void ApplyHairParametersToControl(HairSimControl hairControl, bool simulationEnabled, float curveDensity, float hairMultiplier, float width, float weight, float friction, float iterations, string shaderType)
        {
            if (hairControl == null) return;

            SetHairBool(hairControl, "simulationEnabled", simulationEnabled);
            SetHairFloat(hairControl, "curveDensity", curveDensity);
            SetHairFloat(hairControl, "hairMultiplier", hairMultiplier);
            SetHairFloat(hairControl, "width", width);
            SetHairFloat(hairControl, "weight", weight);
            SetHairFloat(hairControl, "friction", friction);
            SetHairFloat(hairControl, "iterations", iterations);
            SetHairString(hairControl, "shaderType", shaderType);
        }

        private void ApplyHairParametersToAll(bool simulationEnabled, float curveDensity, float hairMultiplier, float width, float weight, float friction, float iterations, string shaderType)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom == null || atom.type != "Person") continue;

                foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                {
                    if (hairGroup == null) continue;
                    ApplyHairParametersToControl(hairGroup.GetComponentInChildren<HairSimControl>(), simulationEnabled, curveDensity, hairMultiplier, width, weight, friction, iterations, shaderType);
                }
            }
        }

        private void ApplyHairPreset(bool simulationEnabled, float curveDensity, float hairMultiplier, float width, float weight, float friction, float iterations, string shaderType, bool hideRenderers)
        {
            if (onToggleHairSimulationState != null)
            {
                onToggleHairSimulationState.toggle.isOn = !simulationEnabled;
            }
            else
            {
                HairSimulationState.val = !simulationEnabled;
            }

            HairIterationsSlider.slider.value = iterations;
            HairMultiplierSlider.slider.value = hairMultiplier;
            CurveDensitySlider.slider.value = curveDensity;
            HairWidthValueSlider.slider.value = width;
            HairWeightValueSlider.slider.value = weight;
            HairFrictionValueSlider.slider.value = friction;
            shaderChooser.val = shaderType;

            if (DisableHairRenderersState != null)
            {
                DisableHairRenderersState.val = hideRenderers;
            }

            ApplyHairParametersToAll(simulationEnabled, curveDensity, hairMultiplier, width, weight, friction, iterations, shaderType);
            ToggleHairRenderersFPS(hideRenderers);
        }

        //Quick buttons
        public void ExecuteGiveMeFPS(bool HairSimulationState, float CurveDensityValue, float HairMultiplierValue, float HairWidthValue, float HairWeightValue, float HairIterationsValue, float ClothWeightValue, float ClothStiffnessValue, float ClothIterationsValue,bool BreastPhysicsState, bool LowerPhysicsState, bool TonguePhysicsState, string shaderTypeValue, bool disablePixelLightsState, string antiAliasingValue, string textureSizeValue, bool hairOnly, float ClothCollisonPowerValue, float ClothCollisonRadiusValue, float HairFrictionValue)
        {
            onToggleHairSimulationState.toggle.isOn = !HairSimulationState;
            HairIterationsSlider.slider.value = HairIterationsValue;
            HairMultiplierSlider.slider.value = HairMultiplierValue;
            CurveDensitySlider.slider.value = CurveDensityValue;
            HairWidthValueSlider.slider.value = HairWidthValue;
            HairWeightValueSlider.slider.value = HairWeightValue;
            HairFrictionValueSlider.slider.value = HairFrictionValue;
            ClothWeightValueSlider.slider.value = ClothWeightValue;
            ClothStiffnessValueSlider.slider.value = ClothStiffnessValue;
            ClothIterationsValueSlider.slider.value = ClothIterationsValue;
            ClothCollisonPowerValueSlider.slider.value = ClothCollisonPowerValue;
            ClothCollisonRadiusValueSlider.slider.value = ClothCollisonRadiusValue;

            shaderChooser.val = shaderTypeValue;

            if (!hairOnly) {
                onToggleBreastPhysicsState.toggle.isOn = !BreastPhysicsState;
                onToggleLowerPhysicsState.toggle.isOn = !LowerPhysicsState;
                onToggleTonguePhysicsState.toggle.isOn = !TonguePhysicsState;
                onToggledisablePixelLightState.toggle.isOn = disablePixelLightsState;
                reflectionTextureSizeChooser.val = textureSizeValue;
            }

            ApplyHairParametersToAll(HairSimulationState, CurveDensityValue, HairMultiplierValue, HairWidthValue, HairWeightValue, HairFrictionValue, HairIterationsValue, shaderTypeValue);

            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    if (!hairOnly) {
                        atom.GetStorableByID("BreastPhysicsMesh").GetBoolJSONParam("on").val=BreastPhysicsState;
                        atom.GetStorableByID("LowerPhysicsMesh").GetBoolJSONParam("on").val=LowerPhysicsState;
                        atom.GetStorableByID("TongueControl").GetBoolJSONParam("tongueCollision").val=TonguePhysicsState;
                    }
                    if (overrideClothParametersState.val) {
                        foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                        {
                            ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                            if (clothControl != null)
                            {
                                clothControl.SetFloatParamValue("stiffness", ClothStiffnessValue);
                                clothControl.SetFloatParamValue("weight", ClothWeightValue);
                                clothControl.SetFloatParamValue("iterations", ClothIterationsValue);
                                clothControl.SetFloatParamValue("Collision Power", ClothCollisonPowerValue);
                                clothControl.SetFloatParamValue("Collision Radius", ClothCollisonRadiusValue);
                            }
                        }
                    }
                }

                if (!hairOnly) {
                    if (atom.GetStorableByID("MirrorRender"))
                    {
                        atom.GetStorableByID("MirrorRender").GetBoolJSONParam("disablePixelLights").val=disablePixelLightsState;
                        atom.GetStorableByID("MirrorRender").GetStringChooserJSONParam("antiAliasing").val = antiAliasingValue;
                        atom.GetStorableByID("MirrorRender").GetStringChooserJSONParam("textureSize").val = textureSizeValue;
                    }
                }
            }
        }

        public void doReflectionTextureChoice(string textureSizeValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.GetStorableByID("MirrorRender"))
                {
                    atom.GetStorableByID("MirrorRender").GetStringChooserJSONParam("textureSize").val = textureSizeValue;
                }
            }
        }

        public void doShaderChoice(string shaderTypeValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetStringChooserParamValue("shaderType", shaderTypeValue);
                        }
                    }
                }
            }
        }

        public void ToggleBreastPhysicsFPS(bool BreastPhysicsState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    atom.GetStorableByID("BreastPhysicsMesh").GetBoolJSONParam("on").val=!BreastPhysicsState;

                }
            }
        }
        public void ToggleLowerPhysicsFPS(bool LowerPhysicsState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    atom.GetStorableByID("LowerPhysicsMesh").GetBoolJSONParam("on").val=!LowerPhysicsState;

                }
            }
        }
        public void ToggleTonguePhysicsFPS(bool TonguePhysicsState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    atom.GetStorableByID("TongueControl").GetBoolJSONParam("tongueCollision").val=!TonguePhysicsState;

                }
            }
        }

        
        public void onToggledisablePixelLightStateFPS(bool disablePixelLightsState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.GetStorableByID("MirrorRender"))
                {
                    atom.GetStorableByID("MirrorRender").GetBoolJSONParam("disablePixelLights").val=disablePixelLightsState;
                }
            }
        }


        public void ToggleAdvancedCollidersFPS(bool AdvancedCollidersState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    JSONStorable geometry = atom.GetStorableByID("geometry");
                    DAZCharacterSelector character = geometry as DAZCharacterSelector;
                    character.SetBoolParamValue("useAdvancedColliders", !AdvancedCollidersState);
                }
            }
        }

        public void ToggleSimClothFPS(bool ClothSimState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                    {
                        ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                        clothControl.SetBoolParamValue("simEnabled", !ClothSimState);
                    }
                }
            }
        }

        public void ToggleHairSimulationFPS(bool HairSimulationState)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetBoolParamValue("simulationEnabled", !HairSimulationState);
                        }
                    }
                }
            }
        }

        public void ToggleHairRenderersFPS(bool hideHairRenderers)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom == null || atom.type != "Person") continue;

                foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                {
                    if (hairGroup == null) continue;

                    foreach (Renderer renderer in hairGroup.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer != null)
                        {
                            renderer.enabled = !hideHairRenderers;
                        }
                    }
                }
            }
        }

        protected void HairMultiplierCallback(JSONStorableFloat HairMultiplierValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("hairMultiplier", HairMultiplierValue.val);
                        }
                    }
                }
            }
        }

        protected void CurveDensityCallback(JSONStorableFloat CurveDensityValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("curveDensity", CurveDensityValue.val);
                        }
                    }
                }
            }
        }

        protected void HairIterationsCallback(JSONStorableFloat HairIterationsValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("iterations", HairIterationsValue.val);
                        }
                    }
                }
            }
        }

        protected void HairWidthValueCallback(JSONStorableFloat HairWidthValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("width", HairWidthValue.val);
                        }
                    }
                }
            }
        }

        protected void HairWeightValueCallback(JSONStorableFloat HairWeightValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("weight", HairWeightValue.val);
                        }
                    }
                }
            }
        }

        protected void HairFrictionValueCallback(JSONStorableFloat HairFrictionValue)
        {
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "Person")
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
                    {
                        HairSimControl hairControl = hairGroup.GetComponentInChildren<HairSimControl>();
                        if (hairControl!=null) {
                            hairControl.SetFloatParamValue("friction", HairFrictionValue.val);
                        }
                    }
                }
            }
        }


        protected void ClothIterationsValueCallback(JSONStorableFloat ClothIterationsValue)
        {
            if (overrideClothParametersState.val) {
                foreach (Atom atom in SuperController.singleton.GetAtoms())
                {
                    if (atom.type == "Person")
                    {
                        foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                        {
                            ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                            if (clothControl!=null) {
                                clothControl.SetFloatParamValue("iterations", ClothIterationsValue.val);
                            }
                        }
                    }
                }
            }
        }

        protected void ClothStiffnessValueCallback(JSONStorableFloat ClothStiffnessValue)
        {
            try {
                if (overrideClothParametersState.val) {
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom.type == "Person")
                        {
                            foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                            {
                                ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                                if (clothControl!=null) {
                                    clothControl.SetFloatParamValue("stiffness", ClothStiffnessValue.val);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught3: " + e);
            }
        }

        protected void ClothWeightValueCallback(JSONStorableFloat ClothWeightValue)
        {
            try {
                if (overrideClothParametersState.val) {
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom.type == "Person")
                        {
                            foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                            {
                                ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                                if (clothControl!=null) {
                                    clothControl.SetFloatParamValue("weight", ClothWeightValue.val);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught3: " + e);
            }
        }

        protected void ClothCollisonPowerValueCallback(JSONStorableFloat ClothCollisonPowerValue)
        {
            try {
                if (overrideClothParametersState.val) {
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom.type == "Person")
                        {
                            foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                            {
                                ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                                if (clothControl!=null) {
                                    clothControl.SetFloatParamValue("collisionPower", ClothCollisonPowerValue.val);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught3: " + e);
            }
        }

        protected void ClothCollisonRadiusValueCallback(JSONStorableFloat ClothCollisonRadiusValue)
        {
            try {
                if (overrideClothParametersState.val) {
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom.type == "Person")
                        {
                            foreach (DAZClothingItem clothGroup in atom.GetComponentsInChildren<DAZClothingItem>())
                            {
                                ClothSimControl clothControl = clothGroup.GetComponentInChildren<ClothSimControl>();
                                if (clothControl!=null) {
                                    clothControl.SetFloatParamValue("collisionRadius", ClothCollisonRadiusValue.val);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) {
                SuperController.LogError("Exception caught3: " + e);
            }
        }

        private static JSONStorableString SetupInfoText(MVRScript script, string text, float height, bool rightSide)
        {
            JSONStorableString storable = new JSONStorableString("Info", text);
            UIDynamic textfield = script.CreateTextField(storable, rightSide);
            textfield.height = height;
            return storable;
        }

    }
}
