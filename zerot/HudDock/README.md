# HUD Dock

One vertical window (`ZeroT.HudDock.dll`) with a switch for every other plugin's HUD / window, in the UI Future Pack look of the Orgasm Suite window.

- a **toggle per HUD** shows / hides it; the **ON / OFF** button beside it is the plugin's own on / off (its `General.Active` / `General.Enabled` setting; Thigh Compressor uses the ParticlePinnacle UI Skin's; a plugin with no switch is turned on / off as a Unity component, which stops its Update / OnGUI but not its Harmony patches); the small **- / +** next to it collapses / expands that HUD; **Hide all** hides everything and **Show again** brings back exactly the ones that were showing (a HUD you had hidden before stays hidden); **All on** shows every loaded HUD
- the same window features as the other plugins: `S- S+ - x`, drag by the title, resize arrow, DPI scale, collapsed bar (shows `n of m shown`), saved layout, experimental external window. **F7** shows / hides the dock (`Window.Hotkey`), it has no other way back once closed
- the list scrolls by the mouse wheel when the window is shorter than the list; a plugin that is not loaded is listed as `N/A`; one that loads after the dock is picked up later

It does not patch anything: every plugin already reads a bool from its own BepInEx config each frame (`Window.Show`, `Window.Visible`, `GUI.Visible`, `GUI.VisibleLowImpact`, `Touch Filters.ShowControlWindow` ...), the dock flips that entry (Penetration Counter keeps its window in a private flag, which the dock sets, plus `ShowOnStart` so the next start matches).

Listed: Scene Performance, SmartWardrobe, Anus Physics, CounterThrust, Penis Path Alignment, Move Controls, Thigh Compressor, Penetration Counter, Procedural Slaps, Orifice Dynamics, FastMode, FluidBridge Helper, GiveMeFPS, iamjoi Garments, XRay + Slap, Deeper Plugin, Detached UI Panel, Orgasm Suite, VPB Random Look. More with `Window.ExtraHuds` (`Label|plugin guid|show section|show key|collapsed section|collapsed key;...`).

Build / test: `python zerot/tools/vamdev.py test huddock` (31 logic checks against stand-in plugins registered like BepInEx registers them + the UI check). Not run in VaM.
