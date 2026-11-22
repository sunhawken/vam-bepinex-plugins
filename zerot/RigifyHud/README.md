# Rigify VR+ node HUD

`build_rigify_hud.py` builds **LightningBunny.RigifyVRPlus.4.var** from the installed packages (third-party source is read from the .var at build time and never committed):

- `LightningBunny.RigifyVRPlus.3.var`: the VR+ script. The ZeroT HUD (`hud_block.cs`, modelled on `zerot.ToggleHUD`) is inserted **into the same script** (RigifyVRPlus.cs).
- `Skynet.Rigify.7.var`: its changes are ported into RigifyVRPlus.cs (controllers held while a node is dragged, the open VaM UI kept when a node is clicked, scene colliders only touched when they are controller colliders), and its Rigify.cs replaces the bundled older one.

The HUD (VR: world-space panel, drag with the laser, BRING, PIN, attach to a hand, always on top, remembered layout; desktop: camera-parented):
NODES (master, = Shift+E/R), LOWER BODY, UPPER BODY, LEFT/RIGHT ARM and LEG (OFF -> FK -> IK), SYMMETRY, ALL ON / ALL OFF, SIZE -/+.
Settings are scene storables (`HUD Show`, `HUD Show On Desktop`, `HUD Pin To View (VR)`, `HUD Scale`, `HUD Attach To (VR)`, ...) plus actions (`HUD Bring`, `All Nodes On / Off`).

Tests: the real merged script is compiled against `VaM_Data\Managed` (`vamdev.py cslcheck`) and run in VamSim (`rigifyhud`, `rigifyhud_vr`); `preview.png` is rendered from that run.
