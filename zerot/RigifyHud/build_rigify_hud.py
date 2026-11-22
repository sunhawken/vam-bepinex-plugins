#!/usr/bin/env python
"""Builds LightningBunny.RigifyVRPlus.<N>.var from the user's installed packages (third-party source is never committed; it is read from the .var at build time):

  - LightningBunny.RigifyVRPlus.3.var   the VR+ script (RigifyVRPlus.cs) and its bundled Rigify.cs
  - Skynet.Rigify.7.var                 newer Rigify: its changes are ported into RigifyVRPlus.cs, and its Rigify.cs replaces the bundled one
  - hud_block.cs (this folder)          the ZeroT node HUD (modelled on zerot.ToggleHUD), inserted into the same script

  python build_rigify_hud.py [--out <dir>] [--version 4] [--force] [--merged <file.cs>]
"""
import hashlib, json, os, re, sys, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
VAM = os.environ.get('VAM_DIR', 'T:\\New folder')
SRC_RVP = os.path.join(VAM, 'AddonPackages', 'resources', 'LightningBunny.RigifyVRPlus.3.var')
SRC_SKY = os.path.join(VAM, 'AddonPackages', 'extra', 'Skynet.Rigify.7.var')
VRPLUS = 'Custom/Scripts/Skynet/Rigify/RigifyVRPlus.cs'
RIGIFY = 'Custom/Scripts/Skynet/Rigify/Rigify.cs'

args = sys.argv[1:]
version = '4'
out_dir = os.path.join(VAM, 'AddonPackages', 'resources')
if '--version' in args:
    version = args[args.index('--version') + 1]
if '--out' in args:
    out_dir = args[args.index('--out') + 1]


def die(msg):
    sys.exit('build_rigify_hud: ' + msg)


def rep(t, a, b, count=1, what=''):
    n = t.count(a)
    if n != count:
        die('anchor found %d time(s), expected %d: %s %s' % (n, count, what, a[:80].replace('\n', '\\n')))
    return t.replace(a, b)


z_rvp = zipfile.ZipFile(SRC_RVP)
z_sky = zipfile.ZipFile(SRC_SKY)
text = z_rvp.read(VRPLUS).decode('utf-8-sig').replace('\r\n', '\n')
hud = open(os.path.join(HERE, 'hud_block.cs'), encoding='utf-8').read().replace('\r\n', '\n')

# ---------------------------------------------------------------- Skynet.Rigify.7 changes, ported (each one anchored on VRPlus's own text)
# the VR+ script already has the grabbing / collider code in an older form; these are the 7 changes: interaction controllers are held while a node is dragged
# (so VaM's own grab and physics do not fight the drag), the open VaM UI is kept when a node is clicked, and scene colliders are only touched when they
# are real controller colliders and only restored if this script disabled them.
text = rep(text, '        private List<FreeControllerV3> _currentDragControllers = new List<FreeControllerV3>(); // Controllers being moved in current drag\n',
           '        private List<FreeControllerV3> _currentDragControllers = new List<FreeControllerV3>(); // Controllers being moved in current drag\n'
           '        private List<FreeControllerV3> _rigifyGrabbedControllers = new List<FreeControllerV3>();\n', what='fields')
text = rep(text, '        private Quaternion _dragStartCameraRot;\n',
           '        private Quaternion _dragStartCameraRot;\n\n'
           '        private bool _preserveInteractionUI;\n'
           '        private bool _savedMainHUDVisible;\n'
           '        private bool _savedMainHUDAnchoredOnMonitor;\n'
           '        private SuperController.ActiveUI _savedActiveUI;\n'
           '        private FreeControllerV3 _savedSelectedController;\n'
           '        private int _savedUIFrame = -1;\n', what='ui fields')
text = rep(text, '        private List<Collider> _sceneAtomLayer8Colliders = new List<Collider>();\n',
           '        private List<Collider> _sceneAtomLayer8Colliders = new List<Collider>();\n'
           '        private List<Collider> _disabledSceneAtomColliders = new List<Collider>();\n', what='collider field')
text = rep(text, '            // When disabling, hide all hover visuals and restore layer 8 colliders\n            if (!val)\n            {\n',
           '            // When disabling, hide all hover visuals and restore layer 8 colliders\n            if (!val)\n            {\n                ReleaseInteractionControllers();\n', what='OnEnabledChanged')

ui_state = '''        private void SaveOpenUIState()
        {
            SuperController controller = SuperController.singleton;
            if (controller == null) return;

            _savedMainHUDVisible = controller.MainHUDVisible;
            _savedMainHUDAnchoredOnMonitor = controller.MainHUDAnchoredOnMonitor;
            _savedActiveUI = controller.activeUI;
            _savedSelectedController = controller.GetSelectedController();
            _savedUIFrame = Time.frameCount;
        }

        private void PreserveOpenUIForInteraction()
        {
            SuperController controller = SuperController.singleton;
            if (controller == null) return;

            if (controller.MainHUDVisible && controller.activeUI != SuperController.ActiveUI.None)
                SaveOpenUIState();

            _preserveInteractionUI = _savedMainHUDVisible &&
                _savedActiveUI != SuperController.ActiveUI.None &&
                _savedUIFrame >= Time.frameCount - 1;
        }

        private void LateUpdate()
        {
            HudLateUpdate();

            SuperController controller = SuperController.singleton;
            if (controller == null || _enabledJSON == null || !_enabledJSON.val)
            {
                _preserveInteractionUI = false;
                _savedUIFrame = -1;
                return;
            }

            if (_preserveInteractionUI)
            {
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
                {
                    if (_savedSelectedController != null && controller.GetSelectedController() != _savedSelectedController)
                        controller.SelectController(_savedSelectedController, false, true, true, false);

                    if (!controller.MainHUDVisible)
                        controller.ShowMainHUD(false, _savedMainHUDAnchoredOnMonitor);

                    controller.activeUI = _savedActiveUI;
                }

                if (!Input.GetMouseButton(0))
                    _preserveInteractionUI = false;
            }

            if (!_preserveInteractionUI)
                SaveOpenUIState();
        }

'''
text = rep(text, '        private void HandleKeybinds()\n', ui_state + '        private void HandleKeybinds()\n', what='HandleKeybinds')

text = rep(text, '''        private void FindSceneAtomLayer8Colliders()
        {
            _sceneAtomLayer8Colliders.Clear();
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom == null || atom == _personAtom) continue;
                // Include scene atoms like Empty, CustomUnityAsset, etc. that have controllers
                Collider[] colliders = atom.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in colliders)
                {
                    if (col != null && col.gameObject.layer == 8)
                        _sceneAtomLayer8Colliders.Add(col);
                }
            }
        }
''', '''        private void FindSceneAtomLayer8Colliders()
        {
            _sceneAtomLayer8Colliders.Clear();
            int controllerMask = SuperController.singleton.targetColliderMask;
            foreach (Atom atom in SuperController.singleton.GetAtoms())
            {
                if (atom == null || atom == _personAtom) continue;
                // Include scene atoms like Empty, CustomUnityAsset, etc. that have controllers
                Collider[] colliders = atom.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in colliders)
                {
                    if (col == null || (controllerMask & (1 << col.gameObject.layer)) == 0) continue;
                    if (col.GetComponent<FreeControllerV3>() != null || col.GetComponent<FreeControllerV3Link>() != null)
                        _sceneAtomLayer8Colliders.Add(col);
                }
            }
        }
''', what='FindSceneAtomLayer8Colliders')
text = rep(text, '''            if (_layer8DisableRefCount != 1) return;
            foreach (Collider col in _layer8Colliders)
            {
                if (col != null)
                    col.enabled = false;
            }
            foreach (Collider col in _sceneAtomLayer8Colliders)
            {
                if (col != null)
                    col.enabled = false;
            }
''', '''            if (_layer8DisableRefCount != 1) return;
            FindSceneAtomLayer8Colliders();
            _disabledSceneAtomColliders.Clear();
            foreach (Collider col in _layer8Colliders)
            {
                if (col != null)
                    col.enabled = false;
            }
            foreach (Collider col in _sceneAtomLayer8Colliders)
            {
                if (col != null && col.enabled)
                {
                    _disabledSceneAtomColliders.Add(col);
                    col.enabled = false;
                }
            }
''', what='DisableLayer8Colliders')
text = rep(text, '''            foreach (Collider col in _sceneAtomLayer8Colliders)
            {
                if (col != null)
                    col.enabled = true;
            }
''', '''            foreach (Collider col in _disabledSceneAtomColliders)
            {
                if (col != null)
                    col.enabled = true;
            }
            _disabledSceneAtomColliders.Clear();
''', what='EnableLayer8Colliders')

# mouse node click keeps the open UI; the mouse pole grab too
text = rep(text, '''                ControlNode node = GetNodeUnderMouse(mousePos);
                if (node != null)
                {
                    _activeNode = node;
                    _isInteractionLocked = true;
                    hasAltControllers =''', '''                ControlNode node = GetNodeUnderMouse(mousePos);
                if (node != null)
                {
                    PreserveOpenUIForInteraction();
                    _activeNode = node;
                    _isInteractionLocked = true;
                    hasAltControllers =''', what='mouse node')
text = rep(text, '''                LimbIKChain chain = GetPoleChainUnderMouse(mousePos);
                if (chain != null)
                {
                    _activePoleChain = chain;''', '''                LimbIKChain chain = GetPoleChainUnderMouse(mousePos);
                if (chain != null)
                {
                    GrabInteractionChain(chain);
                    PreserveOpenUIForInteraction();
                    _activePoleChain = chain;''', what='mouse pole')
text = rep(text, '''                            _activePoleChain = poleChain;
                            _poleStartRotation = poleChain.poleNode.transform.rotation;''', '''                            GrabInteractionChain(poleChain);
                            _activePoleChain = poleChain;
                            _poleStartRotation = poleChain.poleNode.transform.rotation;''', count=2, what='VR pole')

# grabbing helpers + every Start* / EndAllModes
text = rep(text, '        private void EndAllModes()\n        {\n', '        private void EndAllModes()\n        {\n            ReleaseInteractionControllers();\n', what='EndAllModes')
helpers = '''        private void GrabInteractionController(FreeControllerV3 controller)
        {
            if (controller == null || controller.isGrabbing) return;
            _rigifyGrabbedControllers.Add(controller);
            controller.isGrabbing = true;
        }

        private void GrabInteractionChain(LimbIKChain chain)
        {
            GrabInteractionController(chain.rootCtrl);
            GrabInteractionController(chain.middleCtrl);
            GrabInteractionController(chain.endCtrl);
            GrabInteractionController(chain.tipCtrl);
        }

        private void GrabInteractionControllers(List<FreeControllerV3> controllers)
        {
            for (int index = 0; index < controllers.Count; index++)
                GrabInteractionController(controllers[index]);

            for (int chainIndex = 0; chainIndex < _ikChains.Count; chainIndex++)
            {
                LimbIKChain chain = _ikChains[chainIndex];
                if (!IsIKChainEnabled(chain) || !controllers.Contains(chain.endCtrl)) continue;
                GrabInteractionChain(chain);
            }
        }

        private void ReleaseInteractionControllers()
        {
            for (int index = 0; index < _rigifyGrabbedControllers.Count; index++)
            {
                FreeControllerV3 controller = _rigifyGrabbedControllers[index];
                if (controller != null)
                    controller.isGrabbing = false;
            }
            _rigifyGrabbedControllers.Clear();
        }

'''
text = rep(text, '        private void StartDrag(Vector3 mousePos)\n', helpers + '        private void StartDrag(Vector3 mousePos)\n', what='StartDrag sig')
text = rep(text, '        private void StartDrag(Vector3 mousePos)\n        {\n            if (_activeNode == null) return;\n            _isDragging = true;',
           '        private void StartDrag(Vector3 mousePos)\n        {\n            if (_activeNode == null) return;\n            GrabInteractionControllers(_activeNode.linkedControllers);\n            _isDragging = true;', what='StartDrag')
for meth, flag, lst, guard in [
        ('StartAltDrag', '_isAltDragging', 'altLinkedControllers', 'if (_activeNode == null || _activeNode.altLinkedControllers == null) return;'),
        ('StartAltRotate', '_isAltRotating', 'altLinkedControllers', 'if (_activeNode == null || _activeNode.altLinkedControllers == null) return;'),
        ('StartAltRoll', '_isAltRolling', 'altLinkedControllers', 'if (_activeNode == null || _activeNode.altLinkedControllers == null) return;'),
        ('StartRotate', '_isRotating', 'linkedControllers', 'if (_activeNode == null) return;'),
        ('StartRoll', '_isRolling', 'linkedControllers', 'if (_activeNode == null) return;')]:
    a = '        private void %s(Vector3 mousePos)\n        {\n            %s\n            %s = true;' % (meth, guard, flag)
    b = '        private void %s(Vector3 mousePos)\n        {\n            %s\n            GrabInteractionControllers(_activeNode.%s);\n            %s = true;' % (meth, guard, lst, flag)
    text = rep(text, a, b, what=meth)
text = rep(text, '        private void OnDisable()\n        {\n', '        private void OnDisable()\n        {\n            ReleaseInteractionControllers();\n            _preserveInteractionUI = false;\n            _savedUIFrame = -1;\n            if (_hudPanel != null) _hudPanel.SetActive(false);\n', what='OnDisable')
text = rep(text, '        private void OnDestroy()\n        {\n', '        private void OnDestroy()\n        {\n            ReleaseInteractionControllers();\n            HudDestroy();\n', what='OnDestroy')
text = rep(text, '            bool isGrabbed = chain.tipCtrl.isGrabbing;\n', '            bool isGrabbed = chain.tipCtrl.isGrabbing && !_rigifyGrabbedControllers.Contains(chain.tipCtrl);\n', what='toe')

# ---------------------------------------------------------------- the ZeroT node HUD
text = rep(text, 'using UnityEngine.UI;\n', 'using UnityEngine.UI;\nusing UnityEngine.EventSystems;\nusing SimpleJSON;\n', what='usings')
text = rep(text, '        private void InitializeJSONStorables()\n', hud + '        private void InitializeJSONStorables()\n', what='insert HUD')
text = rep(text, '            RegisterAction(new JSONStorableAction("Toggle Visuals", () => { _enabledJSON.val = !_enabledJSON.val; }));\n',
           '            RegisterAction(new JSONStorableAction("Toggle Visuals", () => { _enabledJSON.val = !_enabledJSON.val; }));\n            HudInitStorables();\n', what='storables hook')
text = rep(text, '                _mainSettingsControls.Add(CreateToggle(_rightArmNodeEnabledJSON, false));\n',
           '                _mainSettingsControls.Add(CreateToggle(_rightArmNodeEnabledJSON, false));\n                HudAddPluginControls();\n', what='plugin UI hook')
text = rep(text, '                StartCoroutine(DelayedHeavyInit());\n', '                StartCoroutine(DelayedHeavyInit());\n\n                HudBuild();\n', what='build hook')
text = rep(text, '            HandleKeybinds();\n            PollVoiceBridge();\n', '            HandleKeybinds();\n            PollVoiceBridge();\n            HudUpdate();\n', what='update hook')

# ---------------------------------------------------------------- the package
meta = json.loads(z_rvp.read('meta.json').decode('utf-8-sig'))
meta['description'] = ('Rigify VR+ - VR dual-controller posing plugin. Version ' + version + ' adds a VR node HUD: a world-space panel (drag it with the laser, BRING, PIN, '
                       'attach to a hand, remembered layout) with one switch per Rigify node group - nodes on/off, lower body, upper body, and OFF/FK/IK for each arm and leg - '
                       'plus symmetry and ALL ON / ALL OFF. Also ports Skynet.Rigify.7: dragged controllers are held while a node is used, the open VaM UI is kept when a node is clicked, '
                       'and scene colliders are only touched when they are controller colliders. Version 3 added ZeroT VoiceBridge control. Original by Skynet.')
meta['credits'] = ('Original Rigify by Skynet; VR control system and dual-handle cooperation by LightningBunny; node HUD by ZeroT, structure modelled on zerot.ToggleHUD '
                   '(itself modelled on VamEssentials.DockedUI, CC BY); interaction changes from Skynet.Rigify.7.')
meta['instructions'] = ('Load on a character\'s Plugin panel. VR mode auto-detects controllers. Shift+E enables and Shift+R disables node visuals in desktop and VR modes. '
                        'In VR the node HUD appears in front of you: point the laser at a row and press the trigger to switch that node group (arms and legs cycle OFF, FK, IK). '
                        'Drag the HUD by its header or body, BRING it back, PIN it to your view, or attach it to a hand from the plugin UI (Node HUD section). '
                        'With ZeroT PossessSex Voice Commander 3.4+, say rigify on, rigify off, or rigify toggle.')
meta_text = json.dumps(meta, indent=3, ensure_ascii=False)

name = 'LightningBunny.RigifyVRPlus.%s.var' % version
os.makedirs(out_dir, exist_ok=True)
dest = os.path.join(out_dir, name)
if os.path.exists(dest) and '--force' not in args:
    die(dest + ' already exists (use --force to replace it)')
tmp = dest + '.tmp'
with zipfile.ZipFile(tmp, 'w', zipfile.ZIP_DEFLATED) as zo:
    zo.writestr('meta.json', meta_text.encode('utf-8'))
    zo.writestr(RIGIFY, z_sky.read(RIGIFY))
    zo.writestr(VRPLUS, text.encode('utf-8'))
os.replace(tmp, dest)
print('wrote', dest, os.path.getsize(dest), 'bytes;', RIGIFY, '=', hashlib.md5(z_sky.read(RIGIFY)).hexdigest()[:8], '(Skynet.Rigify.7)')
# the patched script, for the compile check / simulator (third-party source: only written when asked for, never into the repo)
if '--merged' in args:
    with open(args[args.index('--merged') + 1], 'w', encoding='utf-8') as f:
        f.write(text)
