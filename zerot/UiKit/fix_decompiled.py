import sys
import re

# Mechanical fix-ups for ILSpy output so the decompiled sources rebuild against VaM's Managed DLLs.
for p in sys.argv[1:]:
    t = open(p, encoding='utf-8').read()
    t = t.replace('((BaseUnityPlugin)this).Logger', 'Logger')
    t = t.replace('(WindowFunction)', '(GUI.WindowFunction)')
    t = re.sub(r'(?<![\w.])CameraCallback', 'Camera.CameraCallback', t)
    t = re.sub(r'\(OnSceneLoaded\)', '(SuperController.OnSceneLoaded)', t)
    t = re.sub(r'new OnSceneLoaded\(', 'new SuperController.OnSceneLoaded(', t)
    t = re.sub(r'Color\.op_Implicit\(([^()]*(?:\([^()]*\))?[^()]*)\)', r'(Vector4)(\1)', t)
    t = re.sub(r'(?<![\w.])MovementType', 'ScrollRect.MovementType', t)
    t = re.sub(r'(?<![\w.])ActionCallback', 'JSONStorableAction.ActionCallback', t)
    t = re.sub(r'(?<![\w.])AssetBundleFromFileRequest', 'MeshVR.AssetLoader.AssetBundleFromFileRequest', t)
    t = re.sub(r'\((OnAtomAdded|OnAtomRemoved|OnAtomUIDRename)\)', r'(SuperController.\1)', t)
    t = re.sub(r'new (OnAtomAdded|OnAtomRemoved|OnAtomUIDRename)\(', r'new SuperController.\1(', t)
    t = re.sub(r'(new SuperController\.On\w+\()SuperController\.(On\w+)\)', r'\1\2)', t)
    t = re.sub(r'\n\t*//IL_[0-9a-f]+: [^\n]*', '', t)
    open(p, 'w', encoding='utf-8').write(t)
