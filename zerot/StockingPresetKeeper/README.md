# ZeroT.StockingPresetKeeper (headless BepInEx plugin)

Keeps the item preset `Custom/Clothing/Female/Skynet/Body Stocking V2/Body Stocking V2_Default.vap` applied to the
clothing item `Skynet:Body Stocking V2` from `Skynet.Body_Stocking_V2.1.var` on every female Person atom.

* Every `IntervalSeconds` (5) the item's live parameters (wrap / sim / item control / material, the Stopper plugin list and each
  plugin's own values) are compared with what the preset lists. Same = nothing happens. Different = the preset is loaded the
  way the item's preset Load button does it (`PresetManager.LoadPreset`).
* A non-merge preset load re-creates the item's plugins and defaults unlisted parameters, so it is only done when something differs,
  never while a scene loads, only after the item has settled, at most 3 times per time the item is put on, and a parameter that
  still differs after loading (a plugin rewriting it) is dropped from the check instead of looping.
* Config: `BepInEx\config\com.zerot.stockingpresetkeeper.cfg` (Active, IntervalSeconds, FemaleOnly, Safety.*, Item.*).
* `PresetCompare.cs` is plain logic (no Unity types) tested against the real `.vap`; the VaM side is not run in VaM yet.
