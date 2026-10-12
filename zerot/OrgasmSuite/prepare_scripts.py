#!/usr/bin/env python3
"""Builds build/stage/** and build/OrgasmSuite.scripts.bin for the OrgasmSuite plugin.

The plugin embeds the ORIGINAL VaM scripts (SexyFluids, BodyLanguage, BodyLanguage Lite + ArousalHUD, StimulationHUD, FluidBridge,
AutoTremble, ZajeShuffle). They are read from the user's own .var files at build time - third-party source is not stored in git -
and a handful of anchored patches is applied (each anchor is asserted, so a changed source fails loudly instead of silently).

    python prepare_scripts.py [VAM_DIR]
"""
import os, sys, zipfile, struct, zlib, io, shutil

VAM = sys.argv[1] if len(sys.argv) > 1 else os.environ.get('VAM_DIR', r'T:\New folder')
KEEP = os.path.join(VAM, 'AddonPackages', 'keep')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'build')
STAGE = os.path.join(OUT, 'stage')
ROOT = 'Custom/Scripts/ZeroT/OrgasmSuite/'

PKG = {
    'sexyfluids': 'Foost.SexyFluids.20.var',
    'bllite': 'CheesyFX.BodyLanguage_Lite.38.var',
    'bl': 'CheesyFX.BodyLanguage.73.var',
    'fluidbridge': 'ZeroT.FluidBridge.27.var',
    'autotremble': 'DBP.AutoTremble.2.var',
    'zaje': 'ZeroT.ZajeShuffle.6.var',
    'stimhud': 'ZeroT.StimulationHUD.2.var',
}

SOFT_FLAG = 0x40000000   # in the length field: write only when the file does not exist yet (presets / user data)

files = {}         # path relative to the VaM root -> bytes
ifmissing = set()


def zopen(key):
    p = os.path.join(KEEP, PKG[key])
    if not os.path.exists(p):
        sys.exit('missing package: ' + p)
    return zipfile.ZipFile(p)


def put(path, data, soft=False):
    files[path] = data
    if soft:
        ifmissing.add(path)


def patch(text, anchor, repl, count=1, what=''):
    n = text.count(anchor)
    if n != count:
        sys.exit('PATCH ANCHOR (%s) found %d times, expected %d:\n%s' % (what, n, count, anchor[:200]))
    return text.replace(anchor, repl)


def cslist_files(z, base, cslist):
    out = []
    for line in z.read(base + cslist).decode('utf-8-sig').splitlines():
        line = line.strip().replace('\\', '/')
        if not line or line.startswith('//') or line.startswith('#'):
            continue
        out.append(line)
    return out


def add_script_tree(key, base, dest, cslists, extra=(), patches=None):
    """copy every file a cslist names (+ the cslists themselves + extra) from the package into ROOT+dest"""
    z = zopen(key)
    names = set(cslists) | set(extra)
    for c in cslists:
        for f in cslist_files(z, base, c):
            if f.endswith('.cs'):
                names.add(os.path.normpath(f).replace('\\', '/'))
    for n in sorted(names):
        data = z.read(base + n)
        if patches and n in patches:
            data = patches[n](data.decode('utf-8-sig')).encode('utf-8')
        put(ROOT + dest + n, data)
    return z


# ----------------------------------------------------------------------------------------------------------------- patches

def patch_sexyfluids_bundle(t):
    # the shader bundle sits next to the cslist under a fixed name; deriving it from the script name depends on how VaM names a loose plugin
    return patch(t, 'bundleFileName = $"{metaData.pluginPath}/data/{metaData.scriptName}.fluid_shaders.assetbundle";',
                 'bundleFileName = $"{metaData.pluginPath}/data/Foost.SexyFluids.fluid_shaders.assetbundle";', what='bundle name')


def patch_fluidbridge(t):
    t = patch(t, 'const string Version = "27";', 'const string Version = "28";', what='version')
    t = patch(t, '// FluidBridge v13', '// FluidBridge v28 (OrgasmSuite build: orgasm hooks for AutoTremble + ZajeShuffle, per-person settings, male emitters)', what='header')
    # per-person settings file (the old shared file is still read once for females so tuned values survive)
    t = patch(t, '    const string AutoSavePath = "Custom/Scripts/ZeroT/FluidBridge/FluidBridge_AutoSave.json";\n',
              '    const string LegacySavePath = "Custom/Scripts/ZeroT/FluidBridge/FluidBridge_AutoSave.json";\n'
              '    string AutoSavePath { get { return "Saves/PluginData/ZeroT/OrgasmSuite/FluidBridge_" + (containingAtom != null ? containingAtom.uid : "atom") + ".json"; } }\n',
              what='save path')
    t = patch(t, '    Coroutine _wetClothesRemoveCoroutine;\n',
              '    Coroutine _wetClothesRemoveCoroutine;\n'
              '    JSONStorableBool trembleOnOrgasmJSON, shuffleOnOrgasmJSON;\n'
              '    MVRScript trembleScript, zajeScript;\n'
              '    bool trembleStartedByBridge;\n'
              '    string orgasmHookStatus = "no orgasm yet";\n', what='fields')
    t = patch(t, '        if (owEnabledJSON != null) owEnabledJSON.setCallbackFunction += v => { if (!v) ClearOrgasmWardrobe(); };\n',
              '        if (owEnabledJSON != null) owEnabledJSON.setCallbackFunction += v => { if (!v) ClearOrgasmWardrobe(); };\n'
              '\n'
              '        // [OrgasmSuite] orgasm hooks\n'
              '        CreateHeader("Orgasm Hooks", false, new Color(0.78f, 0.5f, 1f));\n'
              '        trembleOnOrgasmJSON = RegisterToggle("AutoTremble on orgasm", true, false, "Starts AutoTremble on this Person when an orgasm fires and stops it when the fluids stop.");\n'
              '        shuffleOnOrgasmJSON = RegisterToggle("ZajeShuffle on orgasm", true, true, "Fires the ZajeShuffle Shuffle action (random UIAssist button) on every orgasm.");\n',
              what='ui')
    t = patch(t, '        StoreFloat(jc, wetClothesDurationJSON);\n',
              '        StoreFloat(jc, wetClothesDurationJSON);\n        StoreBool(jc, trembleOnOrgasmJSON);\n        StoreBool(jc, shuffleOnOrgasmJSON);\n', what='store')
    t = patch(t, '            RestoreFloat(jc, wetClothesDurationJSON);\n',
              '            RestoreFloat(jc, wetClothesDurationJSON);\n            RestoreBool(jc, trembleOnOrgasmJSON);\n            RestoreBool(jc, shuffleOnOrgasmJSON);\n', what='restore')
    t = patch(t, '''        if (IsBodyLanguageOrLiteReason(reason))
        {
            ApplyWetClothesPreset();
            OnOrgasmWardrobe();
        }
    }''', '''        if (IsBodyLanguageOrLiteReason(reason))
        {
            ApplyWetClothesPreset();
            OnOrgasmWardrobe();
        }
        if (IsBodyLanguageOrLiteReason(reason) || reason == "manual test")
            OrgasmHooks(reason);
    }

    // ===== [OrgasmSuite] orgasm hooks: AutoTremble on this Person, ZajeShuffle anywhere =====
    void OrgasmHooks(string reason)
    {
        string note = "";
        try
        {
            if (trembleOnOrgasmJSON != null && trembleOnOrgasmJSON.val)
            {
                if (trembleScript == null) trembleScript = FindScriptOnAtom(new string[] { "AutoTremble" }, containingAtom);
                if (trembleScript != null)
                {
                    JSONStorableBool act = trembleScript.GetBoolJSONParam("Active");
                    if (act == null || !act.val)
                    {
                        if (CallAction(trembleScript, "Start")) { trembleStartedByBridge = true; note += "tremble started; "; }
                        else note += "tremble: Start action missing; ";
                    }
                    else { trembleStartedByBridge = true; note += "tremble already running; "; }
                }
                else note += "tremble: AutoTremble not on this Person; ";
            }
            if (shuffleOnOrgasmJSON != null && shuffleOnOrgasmJSON.val)
            {
                if (zajeScript == null) zajeScript = FindScript(new string[] { "ZajeShuffle" }, containingAtom, true);
                if (zajeScript != null)
                    note += CallAction(zajeScript, "Shuffle") ? "shuffled; " : "shuffle: action missing; ";
                else note += "shuffle: ZajeShuffle not loaded; ";
            }
        }
        catch (Exception e) { note += "hook error: " + e.Message; }
        orgasmHookStatus = note.Length == 0 ? "hooks off" : note.TrimEnd(' ', ';');
    }

    void StopOrgasmTremble()
    {
        if (!trembleStartedByBridge) return;
        trembleStartedByBridge = false;
        try
        {
            if (trembleScript != null) CallAction(trembleScript, "Stop");
        }
        catch { }
    }''', what='trigger')
    t = patch(t, '        ScheduleWetClothesRemoval();\n    }\n\n    void ClearFluidDynamics()',
              '        StopOrgasmTremble();\n        ScheduleWetClothesRemoval();\n    }\n\n    void ClearFluidDynamics()', what='stop')
    # male: no nipple/squirt presets
    t = patch(t, '    void ApplyConfiguredSexyFluidPresets()\n    {\n        if (sexyFluids == null) return;\n',
              '    void ApplyConfiguredSexyFluidPresets()\n    {\n        if (sexyFluids == null) return;\n'
              '        if (!IsFemaleAtom()) { sexyFluidPresetsApplied = true; sexyFluidPresetStatus = "Male: nipple/squirt presets not used"; return; }\n',
              what='male presets')
    # settings load: per-person file first, the legacy shared file only for females
    t = patch(t, '''            if (!AutoSaveFileExists())
            {
                if (manual) statusJSON.val = BuildDiagnostics("No saved settings found");
                return;
            }
            JSONNode node = SuperController.singleton.LoadJSON(AutoSavePath);''', '''            string loadPath = AutoSavePath;
            if (!AutoSaveFileExists(loadPath))
            {
                loadPath = IsFemaleAtom() ? LegacySavePath : null;
                if (loadPath == null || !AutoSaveFileExists(loadPath))
                {
                    if (manual) statusJSON.val = BuildDiagnostics("No saved settings found");
                    return;
                }
            }
            JSONNode node = SuperController.singleton.LoadJSON(loadPath);''', what='load')
    t = patch(t, '    bool AutoSaveFileExists()\n    {\n        try\n        {\n            if (File.Exists(AutoSavePath)) return true;',
              '    bool AutoSaveFileExists(string AutoSavePath)\n    {\n        try\n        {\n            if (File.Exists(AutoSavePath)) return true;', what='exists')
    t = patch(t, '''        yield return new WaitForSeconds(2f);
        if (autoLoadJSON.val)
            LoadSettings(false);''', '''        yield return new WaitForSeconds(2f);
        ApplyGenderDefaults();
        if (autoLoadJSON.val)
            LoadSettings(false);''', what='delayed scan')
    t = patch(t, '    IEnumerator DelayedScan()', '''    // a man has no nipples/squirt emitter: start on "Penis" (a saved per-person file still wins)
    void ApplyGenderDefaults()
    {
        restoringSettings = true;
        try
        {
            if (!IsFemaleAtom() && sexyEmitterJSON != null) sexyEmitterJSON.val = "Penis";
        }
        finally { restoringSettings = false; }
    }

    IEnumerator DelayedScan()''', what='gender defaults')
    # a Person's gender is the reliable answer; the LabiaTrigger collider search stays as the fallback
    t = patch(t, '        if (isFemaleAtomCache.HasValue) return isFemaleAtomCache.Value;\n',
              '        if (isFemaleAtomCache.HasValue) return isFemaleAtomCache.Value;\n'
              '        try\n        {\n'
              '            DAZCharacterSelector g0 = containingAtom == null ? null : containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;\n'
              '            if (g0 != null) { isFemaleAtomCache = g0.gender == DAZCharacterSelector.Gender.Female; return isFemaleAtomCache.Value; }\n'
              '        }\n        catch { }\n', what='female')
    t = patch(t, '"<color=#b8fff5>BodyLanguage -> SexyFluids + Fluid_Dynamics</color>\\n" +',
              '"<color=#b8fff5>BodyLanguage / Lite -> SexyFluids + Fluid_Dynamics + AutoTremble + ZajeShuffle</color>\\n" +', what='help')
    return t


def patch_autotremble(t):
    t = patch(t, '    private JSONStorableStringChooser targetAtomChooser;\n',
              '    private JSONStorableStringChooser targetAtomChooser;\n'
              '    private JSONStorableBool targetSelf;\n'
              '    private const string DefaultPresetFile = "Saves/PluginPresets/AutoTremble/AutoTremblePreset.json";\n', what='fields')
    t = patch(t, '''            targetAtomChooser =
                new JSONStorableStringChooser(
                    "Target Atom",
                    atomChoices,
                    atomChoices.Count > 0
                        ? atomChoices[0]
                        : "None",''', '''            // [OrgasmSuite] the target is the Person this plugin was added to (a preset's saved target is ignored while this is on)
            targetSelf = new JSONStorableBool("Target Self (added atom)", true);
            RegisterBool(targetSelf);
            CreateToggle(targetSelf, true).height = UiRowHeight;

            targetAtomChooser =
                new JSONStorableStringChooser(
                    "Target Atom",
                    atomChoices,
                    containingAtom != null && atomChoices.Contains(containingAtom.uid)
                        ? containingAtom.uid
                        : (atomChoices.Count > 0
                            ? atomChoices[0]
                            : "None"),''', what='chooser')
    t = patch(t, '''    private void OnTargetAtomChanged(
        string uid)
    {
        Stop();
''', '''    private void OnTargetAtomChanged(
        string uid)
    {
        if (targetSelf != null && targetSelf.val && containingAtom != null && uid != containingAtom.uid)
        {
            uid = containingAtom.uid;
            if (targetAtomChooser != null && targetAtomChooser.val != uid)
                targetAtomChooser.valNoCallback = uid;
        }

        Stop();
''', what='on target')
    t = patch(t, '''            string savedAtom =
                preset["TargetAtom"].Value;
''', '''            string savedAtom =
                preset["TargetAtom"].Value;

            if (targetSelf != null && targetSelf.val && containingAtom != null)
                savedAtom = containingAtom.uid;
''', what='preset target')
    t = patch(t, '''            OnTargetAtomChanged(
                targetAtomChooser.val
            );
        }
        catch (Exception e)
        {
            SuperController.LogError(
                "AutoTremble Init error: " +''', '''            OnTargetAtomChanged(
                targetAtomChooser.val
            );

            // [OrgasmSuite] the default preset (a scene's own saved state is restored on top of this afterwards)
            try
            {
                if (FileManagerSecure.FileExists(DefaultPresetFile))
                    LoadPresetFromPath(DefaultPresetFile);
            }
            catch (Exception pe)
            {
                SuperController.LogError("AutoTremble default preset: " + pe.Message);
            }
        }
        catch (Exception e)
        {
            SuperController.LogError(
                "AutoTremble Init error: " +''', what='init end')
    return t


def varint(n):
    enc = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        enc.append(b | (0x80 if n else 0))
        if not n:
            break
    return bytes(enc)


def build():
    if os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)

    # SexyFluids (+ its shader bundle + preset jpg/json, the latter only when missing)
    z = add_script_tree('sexyfluids', 'Custom/Scripts/Foost/SexyFluids/', 'SexyFluids/', ['SexyFluids.cslist'],
                        extra=['data/Foost.SexyFluids.fluid_shaders.assetbundle'],
                        patches={'src/utils/DataResourceProvider.cs': patch_sexyfluids_bundle})
    for n in z.namelist():
        if n.startswith('Saves/PluginData/Foost/SexyFluids/') and not n.endswith('/'):
            put(n, z.read(n), soft=True)

    # BodyLanguage Lite + ArousalHUD (the Lite var ships both cslists)
    add_script_tree('bllite', 'Custom/Scripts/CheesyFX/BodyLanguage_Lite/', 'BodyLanguage_Lite/', ['BodyLanguage_Lite.cslist', 'ArousalHUD.cslist'])
    # BodyLanguage (full)
    add_script_tree('bl', 'Custom/Scripts/CheesyFX/BodyLanguage/', 'BodyLanguage/', ['BodyLanguage.cslist'])
    # FluidBridge (patched)
    add_script_tree('fluidbridge', 'Custom/Scripts/ZeroT/FluidBridge/', 'FluidBridge/', ['FluidBridge.cslist'],
                    patches={'FluidBridge.cs': patch_fluidbridge})
    # AutoTremble (patched), plus the user's default preset when it is missing
    z = zopen('autotremble')
    put(ROOT + 'AutoTremble/AutoTremble.cs',
        patch_autotremble(z.read('Custom/Scripts/DBP/DBP.AutoTremble/AutoTremble.cs').decode('utf-8-sig')).encode('utf-8'))
    preset = os.path.join(VAM, 'Saves', 'PluginPresets', 'AutoTremble', 'AutoTremblePreset.json')
    if os.path.exists(preset):
        put('Saves/PluginPresets/AutoTremble/AutoTremblePreset.json', open(preset, 'rb').read(), soft=True)
    # ZajeShuffle, StimulationHUD
    add_script_tree('zaje', 'Custom/Scripts/ZeroT/ZajeShuffle/', 'ZajeShuffle/', ['ZajeShuffle.cslist'])
    add_script_tree('stimhud', 'Custom/Scripts/ZeroT/StimulationHUD/', 'StimulationHUD/', ['StimulationHUD.cslist'])

    # stage on disk (for the compile check / sim) and pack
    for p, d in files.items():
        fp = os.path.join(STAGE, p)
        os.makedirs(os.path.dirname(fp), exist_ok=True)
        open(fp, 'wb').write(d)
    buf = io.BytesIO()
    buf.write(struct.pack('<i', len(files)))
    for p in sorted(files):
        name = p.encode('utf-8')
        buf.write(varint(len(name)))           # .NET BinaryWriter string: 7-bit length + utf-8
        buf.write(name)
        buf.write(struct.pack('<i', len(files[p]) | (SOFT_FLAG if p in ifmissing else 0)))
        buf.write(files[p])
    comp = zlib.compressobj(9, zlib.DEFLATED, -15)
    packed = comp.compress(buf.getvalue()) + comp.flush()
    open(os.path.join(OUT, 'OrgasmSuite.scripts.bin'), 'wb').write(packed)
    print('%d files, %.1f MB raw, %.2f MB packed -> %s' % (len(files), len(buf.getvalue()) / 1e6, len(packed) / 1e6, OUT))


if __name__ == '__main__':
    build()
