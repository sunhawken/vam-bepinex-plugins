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

The scripts are **not stored in git**: `prepare_scripts.py` reads them from your own `.var` files (`AddonPackages/keep`), applies the anchored patches (each anchor is asserted) and packs them into `build/OrgasmSuite.scripts.bin`, which the project embeds.

    python zerot/tools/vamdev.py test orgasmsuite     # builds, compiles every script set against VaM's DLLs, runs the scenarios + UI check
    python zerot/tools/vamdev.py deploy orgasmsuite

Patches: SexyFluids' shader bundle gets a fixed file name; FluidBridge gets the orgasm hooks, per-Person settings files and a gender check; AutoTremble gets "Target Self" and the default-preset load.

## What the simulator covers

`vamdev.py suitetargets` compiles the staged scripts into `UiSim/in/suite`; the `orgasmsuite` scenario runs the host against the REAL Lite, ArousalHUD, FluidBridge, AutoTremble and ZajeShuffle scripts (attach, engine switch, parts on/off, orgasm -> tremble + shuffle, scene reload, external scripts). `orgasmsuite_hud` renders the real HUD scripts to `UiSim/out/hud` (copies in `preview/`).

Not covered: SexyFluids (needs the Person's skinned mesh and the shader bundle) and BodyLanguage (full) are attached/detached for real but their own Init/frames cannot run in the simulator. Nothing here has been run in VaM itself.
