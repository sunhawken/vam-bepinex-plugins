# VaM BepInEx plugins

Source archive and reproducible build projects for BepInEx plugins used with Virt-A-Mate.

The `recovered/` directory contains 23 project trees reconstructed from the installed DLLs. The older `zerot/`, `ports/`, and `utility/` directories are retained for history and include hand-maintained source revisions.

## Build

Requirements:

- Windows with a Virt-A-Mate installation
- .NET SDK
- BepInEx installed in the VaM directory

From PowerShell:

```powershell
$env:VAM_DIR = 'T:\New folder'
.\build.ps1
```

You can instead pass the location directly:

```powershell
.\build.ps1 -VaMDir 'D:\Games\VaM'
```

Release DLLs are written beneath `artifacts/Release`. VaM and BepInEx binaries are referenced from the local installation and are not redistributed by this repository.

### VPB performance patcher

`utility/VPBPerformancePatcher` applies two targeted idle-cost fixes to VPB 0.30.55 while preserving immediate click and configuration-change updates: it removes a redundant every-frame gallery hierarchy scan and limits the idle quick-menu visual refresh to 4 Hz. The proprietary input DLL is not included.

```powershell
dotnet run --project utility/VPBPerformancePatcher -- `
  'T:\New folder\BepInEx - Disabled\group1\VPB.original.0.30.55.dll' `
  'T:\New folder\BepInEx\plugins\VPB.dll'
```

## What each plugin does

| Plugin | Purpose |
| --- | --- |
| **AddonPackagesOrganizer** | Moves top-level `.var` and `.DISABLED` packages into `AddonPackages/extra`, except for a built-in keep-at-top list. Existing destination conflicts and protected PAR2 files are left alone. |
| **AssetLoaderHelper** | Raises VaM's parallel asset-bundle, scene, and texture-worker limits. It also exposes load/cache statistics, slow-load logging, periodic reporting, and a stats hotkey through BepInEx configuration. |
| **AutoBulger** | Drives configurable belly, throat, vaginal, and anal morphs from collision depth, creating penetration bulge effects on a selected Person atom. Includes smoothing, filters, offsets, multipliers, clamping, and debug/manual-depth options. |
| **BootyPhysics** | Automatically applies Small, Normal, or Big glute physics presets to one or all Person atoms after scene load. It updates `GluteControl` and `LowerPhysicsMesh` settings. |
| **CacheHelper** | Manages VaM's disk cache: deletes old files, evicts the oldest files above a size limit, configures Unity's cache limits, optionally unloads unused assets after scene loads, and provides cleanup/stat hotkeys. |
| **ContentListFixer** | Scans changed `.var` archives and rebuilds an incorrect or missing `contentList` in `meta.json` from the archive's real entries. It keeps a timestamp so unchanged packages are skipped on later runs. |
| **ExtraAutoGenitals** | Animates a configurable set of genital/labia morphs from `LabiaTrigger` movement relative to `abdomen2Control`, with response speed, direction, inward/outward limits, and exaggeration controls. |
| **FastLoad** | Increases Unity's asynchronous GPU upload time slice and buffer size, then prewarms and raises the .NET thread-pool minimum to reduce loading stalls. |
| **ForceHardDelete** | Harmony-patches every `Atom` after `Awake` and sets `isPoolable = false`, forcing removed atoms to be destroyed instead of returned to VaM's object pool. |
| **KeybindFixer** | Repairs AcidBubbles Keybindings integration on the `CoreControl` atom after startup. It rediscovers action-provider plugins, reloads saved defaults, and can periodically rewire or fully reload Keybindings. |
| **KeyGenerator** | Restores two expected key/environment files and any embedded certificate files when they are missing. Existing files are never overwritten. |
| **LocationIndependence** | Detects when the VaM installation has moved to a different drive or folder and rewrites stale absolute paths in BepInEx configs and VaM scene/preset files. Supports backups and dry-run mode. |
| **MaleMoanOff** | Finds male Person atoms after scene load and disables enabled VAMMoan plugin instances on them. It can be disabled or made less verbose in its BepInEx config. |
| **MicAlwaysOn** | Patches `OVRLipSyncMicInput.Start`, sets microphone activation to the always-on mode, and immediately starts microphone capture for lip sync. |
| **ParallelVARScan** | Opens every `.var` archive across a configurable number of background threads to prewarm filesystem/archive metadata caches before VaM needs the packages. |
| **QvaroSync** | Reads Qvaro's `settings.json` keep-enabled list and re-enables matching `.DISABLED` packages. An off-by-default option can also disable packages not in the list; automatic and repeated synchronization are configurable. |
| **SceneSpeedLoader** | Optimizes scene transitions by temporarily disabling cameras, physics, reflections, and expensive LOD selection; raises process priority during loading; optionally warms shaders at startup and runs garbage collection afterward. Logs scene-load timing. |
| **SelfPathFixer** | Replaces broken `SELF:/` references inside `.var` archive text files with the package-qualified path, and removes `SELF:/` from saved scene JSON. Runs in the background and supports archive backups, dry-run mode, and a manual hotkey. |
| **StartupProfiler** | Records timestamps for BepInEx startup, `SuperController` availability, scene readiness, and a one-second settle period, then writes the results to `BepInEx/StartupProfile.txt`. |
| **VamPar2** | Creates PAR2 recovery files for every `.var` package that lacks them, using configurable redundancy. It also relocates recovery sets when their source packages have moved and cleans incomplete output after failures. |
| **VARIndexCache** | Maintains `BepInEx/cache/var_index.cache`, recording package size, modification time, and ZIP entry count. Only new or changed `.var` archives are rescanned, on a background worker. |
| **VarVarFixer** | Renames accidental `*.var.var` files to `*.var`. If the correct destination already exists, it reports the conflict and leaves both files untouched. |
| **ZeroT.PostMagicToggle** | Adds a configurable hotkey (default `Alt+P`) that finds every `PostMagicEnabled` parameter in the scene and toggles all MacGruber PostMagic instances together. |

### File-changing plugins

`AddonPackagesOrganizer`, `ContentListFixer`, `KeyGenerator`, `LocationIndependence`, `QvaroSync`, `SelfPathFixer`, `VamPar2`, and `VarVarFixer` can create, move, rename, or edit files. Read their configuration and keep backups before first use. `CacheHelper` deliberately deletes cache data according to its configured age and size limits.

## Recovered projects

The 23 matching source projects are stored under `recovered/`. The older `zerot/`, `ports/`, and `utility/` folders contain historical or hand-maintained revisions.

These projects are recovery snapshots. Review and test plugins in a disposable VaM setup before deployment.
