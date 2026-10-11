# vamdev + vamsim - test and ship VaM plugins without launching VaM

`python zerot/tools/vamdev.py help`

| command | does |
|---|---|
| `list` | every plugin project and which simulator covers it |
| `test <name\|all>` | build, then run the logic scenario (headless plugins) or the UI clip check (window plugins) |
| `ship <name>` | build + test + copy to `T:\New folder\BepInEx\plugins` (one `.bak` kept, tells you if VaM is running) |
| `deploy <name>` | copy only |
| `unpack` / `pack` | .var <-> folder (forward-slash entries, meta.json validated) |
| `cslcheck <var\|folder>` | compile the .cs/.cslist scripts with Roslyn against the real `VaM_Data\Managed` assemblies |
| `simbuild` | rebuild the simulator after changing it (regenerates the VaM API stubs) |
| `simtargets` | compile the real scripts of DecalMaker / BodyLanguage_Lite / UIAssist from your AddonPackages into `UiSim/in/targets` (needed by the Harmony-helper scenarios; never committed) |
| `cslbuild <var> <out.dll>` | same, for any package |

## What the simulator is
`zerot/UiSim` holds a small re-implementation of UnityEngine + the VaM classes plugins use (no Unity, no VaM).
Plugin DLLs are retargeted at it with Cecil and run for real.

* **UiSim / uisim.exe**: draws every IMGUI/uGUI window to a PNG and flags clipped text (`check.ps1`).
* **VamSim / vamsim.exe**: runs headless plugins against a fake scene (persons with rigidbodies, free controllers, morph banks,
  labia triggers, storables with callbacks, scene loads, config changes) and asserts what they do. Scenarios live in
  `zerot/UiSim/VamSim/Scenarios.cs`: one `[Scenario("name","<plugin dll>")]` method per plugin. Scene model:
  `zerot/UiSim/UnityShim/VamScene.cs` (`SimWorld.NewPerson`, `Harness.Step/Set/Load/Check`).
* `vamsim.exe --list`, `vamsim.exe <dll> [scenario] [--set Section.Key=value]`, `vamsim.exe --all <dir>`.

Covered today: BodyWeight, MicroPhysics Fixer, ExtraAutoGenitals, PersonGuard (logic) and every window plugin (UI).

## Adding something
1. Plugin uses a VaM/Unity API the shim lacks -> `vamdev.py simbuild` prints `MISSING MEMBER ...`; add it to
   `UnityShim/VamScene.cs` (behaviour) or `Extra.cs` (plain member). Real VaM exposes many members as *properties*; plugins call
   `get_x`, so a field in the shim is "missing".
2. New scenario -> add a method to `Scenarios.cs`, `vamdev.py simbuild`, `vamsim.exe <dll>`.
3. New plugin project -> add it to the table at the top of `vamdev.py`.

## Harmony helpers (DecalMaker / BodyLanguage / UIAssist)
These patch script code that only exists inside VaM, so `vamdev.py simtargets` compiles the REAL scripts from the newest matching
`.var` in AddonPackages into a script assembly. The scenario (`HelperScenarios.cs`) loads that assembly into the simulator, runs the
helper with real Harmony (Lib.Harmony, with a call-signature fix-up for the helpers' Harmony 2.0 build), and then runs the PATCHED
methods: MSAA argument 8 -> 1, the wrong `Release()` -> `ReleaseTemporary`, throttles, exception containment, name-check caches.
Each scenario first asserts the *unpatched* behaviour (e.g. "temporaries use 8x MSAA", "GameControlUI.Update throws") so a pass
shows the helper changed it. Harmony patching JITs the patched method, so every Unity/VaM member in those methods must exist in the
shim - that is why the structs (Vector3/Quaternion) now have real maths in `UnityShim/Math3D.cs`.

## Limits (be honest about what a pass means)
* Behaviour is simulated, not VaM: no physics, no rendering, no real DAZ morph pipeline. A pass proves the plugin/patch logic and its
  config/lifecycle handling, not that it looks or feels right in VaM.
* Helper scenarios exercise the patched methods in isolation (render textures are counters, not GPU textures).
* Text metrics for UiSim assume Arial 13 px.
