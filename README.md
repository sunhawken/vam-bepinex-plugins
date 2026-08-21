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

## Recovered projects

AddonPackagesOrganizer, AssetLoaderHelper, AutoBulger, BootyPhysics, CacheHelper, ContentListFixer, ExtraAutoGenitals, FastLoad, ForceHardDelete, KeybindFixer, KeyGenerator, LocationIndependence, MaleMoanOff, MicAlwaysOn, ParallelVARScan, QvaroSync, SceneSpeedLoader, SelfPathFixer, StartupProfiler, VamPar2, VARIndexCache, VarVarFixer, and ZeroT.PostMagicToggle.

These projects are recovery snapshots. Review and test plugins in a disposable VaM setup before deployment.
