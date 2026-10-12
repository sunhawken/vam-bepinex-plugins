# OrgasmSuite

One BepInEx plugin (`ZeroT.OrgasmSuite.dll`) that carries the original VaM scripts and puts them on every Person atom:

| Script | Role |
|---|---|
| `CheesyFX.BodyLanguage_Lite` + `ArousalHUD` | the "Lite" arousal engine and its HUD |
| `CheesyFX.BodyLanguage` (full) + `ZeroT.StimulationHUD` | the full engine and its HUD (BodyLanguage keeps a static singleton: one Person at a time) |
| `Foost.SexyFluids` | fluid emitters (nipples / squirt for women, penis for men) |
| `ZeroT.FluidBridge` (v28) | listens to Lite or BodyLanguage, starts SexyFluids, wet clothes, orgasm wardrobe, **AutoTremble** and **ZajeShuffle** on every orgasm |
| `DBP.AutoTremble` | tremble on the Person it is added to; the default preset (`Saves/PluginPresets/AutoTremble/AutoTremblePreset.json`) is applied with its target replaced by that Person |
| `ZeroT.ZajeShuffle` | one instance on CoreControl; FluidBridge fires its `Shuffle` action |

The window (same chassis as the other plugins: `S- S+ - x`, resize arrow, DPI scale, collapsed bar, saved layout) switches each Person between **Lite** and **BodyLanguage** and turns Suite / Fluids / AutoTremble / HUD on and off; ZajeShuffle is session wide. A script already on a Person (a scene's own `.var` plugin, even one that is still compiling) is left alone and not duplicated. Choices are saved per Person in `Saves/PluginData/ZeroT/OrgasmSuite/people.tsv`.

## Build

The scripts are **not stored in git**: `prepare_scripts.py` reads them from your own `.var` files (`AddonPackages/keep`), applies the anchored patches (each anchor is asserted), compiles every script set with Roslyn against VaM's own DLLs (target net35, so the compiler emits none of the newer runtime attributes VaM's Mono cannot load) and packs the **assemblies** (plus the SexyFluids shader bundle and shipped presets) into `build/OrgasmSuite.scripts.bin`, which the project embeds. Eight assemblies: BodyLanguage Lite, Arousal HUD, BodyLanguage, Stimulation HUD, SexyFluids, FluidBridge, AutoTremble, ZajeShuffle.

Why assemblies and not loose `.cslist` source: the first build attached loose scripts and VaM (with LeanCore's plugin compile cache) crashed inside Mono reflection (`mono_method_body_get_object`) right after the first attach, on every scene load. Precompiled plugin DLLs are the path the other standalone plugins use, and VaM's script compiler / cache is not involved.

    python zerot/tools/vamdev.py test orgasmsuite     # builds, compiles every script set, runs the scenarios + UI check
    python zerot/tools/vamdev.py deploy orgasmsuite

Patches: SexyFluids' shader bundle gets a fixed file name; FluidBridge gets the orgasm hooks, per-Person settings files and a gender check; AutoTremble gets "Target Self" and the default-preset load.

## Safety while a scene loads

Nothing is attached until the scene has settled: `SettleSeconds` (15) after VaM says loading is done AND `QuietSeconds` (6) with no change to atoms, plugin slots or registered storables (the suite's own changes do not count). Each attach is logged *before* the call ("attaching X to Y...") so a crash log names the culprit. `AutoAttach=false` (config `Suite`) makes the suite never touch a Person.

## What the simulator covers

`vamdev.py suitetargets` builds the assemblies and copies them into `UiSim/in/suite`; the `orgasmsuite` scenario runs the host against the REAL Lite, ArousalHUD, FluidBridge, AutoTremble and ZajeShuffle scripts (attach, engine switch, parts on/off, orgasm -> tremble + shuffle, scene reload, external scripts). `orgasmsuite_hud` renders the real HUD scripts to `UiSim/out/hud` (copies in `preview/`).

Not covered: SexyFluids (needs the Person's skinned mesh and the shader bundle) and BodyLanguage (full) are attached/detached for real but their own Init/frames cannot run in the simulator. Nothing here has been run in VaM itself.
