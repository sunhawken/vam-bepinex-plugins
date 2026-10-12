#!/usr/bin/env python3
"""Builds build/stage/** and build/OrgasmSuite.scripts.bin for the OrgasmSuite plugin.

The plugin embeds the ORIGINAL VaM scripts (SexyFluids, BodyLanguage, BodyLanguage Lite + ArousalHUD, StimulationHUD, FluidBridge,
AutoTremble, ZajeShuffle). They are read from the user's own .var files at build time - third-party source is not stored in git -
and a handful of anchored patches is applied (each anchor is asserted, so a changed source fails loudly instead of silently).

    python prepare_scripts.py [VAM_DIR]
"""
import os, sys, zipfile, struct, zlib, io, shutil, subprocess, tempfile

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


def collect_sources(key, base, cslist, patches=None):
    """the .cs files one cslist names (patched), as {relative path: text}"""
    z = zopen(key)
    out = {}
    for f in cslist_files(z, base, cslist):
        if not f.endswith('.cs'):
            continue
        n = os.path.normpath(f).replace('\\', '/')
        t = z.read(base + n).decode('utf-8-sig')
        if patches and n in patches:
            t = patches[n](t)
        out[n] = t
    return out


NOWARN = 'CS0108;CS0114;CS0168;CS0219;CS0649;CS0169;CS0414;CS0618;CS1998;CS0659;CS0661;CS0162;CS0105;CS1591;CS0109;CS0472;CS0642;CS0252;CS0253;CS0067;CS0164;CS0660;CS0665;CS0628;CS0436;CS0458'


def build_dll(asm, sources):
    """Roslyn-compile a script set into an assembly for VaM's Mono. net35 reference assemblies: the compiler then emits none of the newer
    runtime attributes (IteratorStateMachineAttribute ...) that VaM's Mono cannot load. Returns the DLL bytes."""
    work = tempfile.mkdtemp(prefix='suite_' + asm + '_')
    src = os.path.join(work, 'src')
    for n, t in sources.items():
        p = os.path.join(src, n)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'w', encoding='utf-8').write(t)
    managed = os.path.join(VAM, 'VaM_Data', 'Managed')
    refs = []
    for f in sorted(os.listdir(managed)):
        if f.endswith('.dll') and (f.startswith('UnityEngine') or f in ('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'SimpleJSON.dll')):
            refs.append('<Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference>' % (f[:-4], os.path.join(managed, f)))
    open(os.path.join(work, 'p.csproj'), 'w').write(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Library</OutputType><AssemblyName>' + asm + '</AssemblyName>'
        '<RootNamespace></RootNamespace><TargetFramework>net35</TargetFramework><LangVersion>latest</LangVersion><GenerateAssemblyInfo>false</GenerateAssemblyInfo>'
        '<DefineConstants>VAM_GT_1_20;VAM_GT_1_20_77_13;VAM_GT_1_21;VAM_1_22</DefineConstants><DebugType>none</DebugType><Optimize>true</Optimize>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>' + NOWARN + '</NoWarn></PropertyGroup>'
        '<ItemGroup><Compile Include="src/**/*.cs" /><Reference Include="System.Core" />' + ''.join(refs) + '</ItemGroup></Project>')
    r = subprocess.run(['dotnet', 'build', os.path.join(work, 'p.csproj'), '-c', 'Release', '--nologo', '-v', 'q', '-o', os.path.join(work, 'out')],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    text = (r.stdout or '') + (r.stderr or '')
    errs = sorted(set(l.strip() for l in text.splitlines() if ' error ' in l))
    dll = os.path.join(work, 'out', asm + '.dll')
    if r.returncode != 0 or not os.path.exists(dll):
        print('COMPILE FAILED: ' + asm)
        for e in errs[:30]:
            print('  ' + e.replace(src, '')[:240])
        sys.exit(1)
    data = open(dll, 'rb').read()
    shutil.rmtree(work, ignore_errors=True)
    return data


# ----------------------------------------------------------------------------------------------------------------- patches

def patch_sexyfluids_bundle(t):
    # the shader bundle sits next to the cslist under a fixed name; deriving it from the script name depends on how VaM names a loose plugin
    return patch(t, 'bundleFileName = $"{metaData.pluginPath}/data/{metaData.scriptName}.fluid_shaders.assetbundle";',
                 'bundleFileName = $"{metaData.pluginPath}/data/Foost.SexyFluids.fluid_shaders.assetbundle";', what='bundle name')


def patch_bl_fillmeup(t):
    t = t.replace('\r\n', '\n')
    t = patch(t, """				orificeForceGroup.Update();
				handForceGroup.Update();
				UpdateBulge();
""", """				// [OrgasmSuite] before everything is built (or when the person it was built for is gone) these are still null: that threw here every frame
				if (orificeForceGroup != null) orificeForceGroup.Update();
				if (handForceGroup != null) handForceGroup.Update();
				if (anus != null && vagina != null && throat != null && bellyBulgeMorphs != null && throatBulgeMorphs != null) UpdateBulge();
""", what='fillmeup update')
    return t


def patch_bl_orifice(t):
    t = t.replace('\r\n', '\n')
    t = patch(t, """		public void GetDepthAndSpeed()
		{
			if (penetrator.type == 0)
			{
				GetDepthAndSpeedSimple();
				return;
			}
""", """		public void GetDepthAndSpeed()
		{
			// [OrgasmSuite] an orifice that is enabled with no (or a half-built) penetrator threw here on every physics step
			if (penetrator == null || enterPointTF == null) return;
			if (penetrator.type == 0)
			{
				if (depthMeter == null) return;
				GetDepthAndSpeedSimple();
				return;
			}
			if (penetrator.tip == null || penetrator.rigidbodies == null || penetrator.rigidbodies.Count < 3) return;
""", what='orifice depth')
    t = patch(t, """			base.FixedUpdate();
			GetDepthAndSpeed();
""", """			base.FixedUpdate();
			if (penetrator == null) return;   // [OrgasmSuite] nothing in the orifice: the depth / stretch / stimulation below all need the penetrator
			GetDepthAndSpeed();
""", what='orifice fixedupdate')
    return t


def patch_lite_collision(t):
    """BodyLanguage Lite: it can already switch self stimulation; add the other half (other Persons / CUA atoms) so the suite can offer None / Self / Person+CUA / Both"""
    t = t.replace('\r\n', '\n')
    t = patch(t, "        JSONStorableBool selfStimulationJ;\n", "        JSONStorableBool selfStimulationJ;\n        JSONStorableBool otherStimulationJ;   // [OrgasmSuite]\n", what='lite field')
    t = patch(t, """            selfStimulationJ.setCallbackFunction += delegate(bool value) { ForceRebuildPairs(); };
""", """            selfStimulationJ.setCallbackFunction += delegate(bool value) { ForceRebuildPairs(); };
            otherStimulationJ = NewBool("Allow Other Stimulation", true);   // [OrgasmSuite] other Persons and CUA atoms
            otherStimulationJ.setCallbackFunction += delegate(bool value) { ForceRebuildPairs(); };
""", what='lite create')
    t = patch(t, """            TT(CreateToggle(selfStimulationJ, false), "Allows the character's own hands, feet, and genitals to stimulate themselves.");
""", """            TT(CreateToggle(selfStimulationJ, false), "Allows the character's own hands, feet, and genitals to stimulate themselves.");
            TT(CreateToggle(otherStimulationJ, false), "Allows other Persons and CUA atoms (toys, props) to stimulate this character. Off = only self stimulation (if allowed) counts.");
""", what='lite ui')
    t = patch(t, """            if (atom == containingAtom) return AllowSelfStimulation();
""", """            if (atom == containingAtom) return AllowSelfStimulation();
            if (!AllowOtherStimulation()) return false;   // [OrgasmSuite]
""", what='lite atoms')
    t = patch(t, """                    if (!AllowSelfStimulation() && isSelfHit) continue;
""", """                    if (!AllowSelfStimulation() && isSelfHit) continue;
                    if (!isSelfHit && !AllowOtherStimulation()) continue;   // [OrgasmSuite]
""", what='lite overlap')
    t = patch(t, """        bool AllowSelfStimulation()
        {""", """        bool AllowOtherStimulation()
        {
            return otherStimulationJ == null || otherStimulationJ.val;
        }

        bool AllowSelfStimulation()
        {""", what='lite helper')
    return t


FILTER_CS = '''using UnityEngine;

// [OrgasmSuite] one switch for where touch-based arousal may come from; the suite sets Mode through reflection
// 0 = nothing, 1 = other Persons / CUA atoms, 2 = the Person itself, 3 = both
public static class OrgasmSuiteFilter
{
    public static int Mode = 1;

    public static bool Allows(Atom self, Atom other)
    {
        if (other == null) return Mode != 0;
        bool isSelf = other == self;
        switch (Mode)
        {
            case 0: return false;
            case 1: return !isSelf;
            case 2: return isSelf;
            default: return true;
        }
    }
}
'''


def patch_bl_trigger(t):
    """touch arousal: only from the sources the window allows (none / self / other Person+CUA / both)"""
    t = t.replace(chr(13) + chr(10), chr(10))
    t = patch(t, """		void RegisterCollision(Collision collision){
			if(TouchMe.excludedAtoms.Contains(collision.rigidbody.GetAtom())) return;
""", """		void RegisterCollision(Collision collision){
			if(TouchMe.excludedAtoms.Contains(collision.rigidbody.GetAtom())) return;
			if(!OrgasmSuiteFilter.Allows(FillMeUp.atom, collision.rigidbody.GetAtom())) return;   // [OrgasmSuite]
""", what='bl trigger')
    return t


def patch_bl_orificetrigger(t):
    """penetration arousal goes through the same switch"""
    t = t.replace(chr(13) + chr(10), chr(10))
    t = patch(t, """            lastColliding = collider;
            numCollisions++;
            if (!enabled)""", """            if (!OrgasmSuiteFilter.Allows(FillMeUp.atom, collider.GetAtom())) return;   // [OrgasmSuite] collision arousal mode
            lastColliding = collider;
            numCollisions++;
            if (!enabled)""", what='bl orifice trigger')
    return t


def patch_bl_resources(t):
    """BodyLanguage decides 'am I in a package' from its own plugin path; attached from a DLL it is not, so it looked for the morphs, expression sets,
    audio bundles and textures as loose files that do not exist (Anus / ReadMyLips / TouchMe / WatchMe init errors). Use the resources package whenever it is installed."""
    t = t.replace(chr(13) + chr(10), chr(10))
    t = patch(t, """			isVar = Utils.GetPackagePath(this) != string.Empty;
			packageUid = isVar? GetLatestPackageUid("CheesyFX.BodyLanguage_Resources")+":/" : string.Empty;""",
              """			string suiteRes = GetLatestPackageUid("CheesyFX.BodyLanguage_Resources");   // [OrgasmSuite]
			isVar = Utils.GetPackagePath(this) != string.Empty || suiteRes != null;
			packageUid = suiteRes != null ? suiteRes + ":/" : string.Empty;""", what='bl resources')
    return t


def patch_bl_orgasm(t):
    """DoOrgasm sets isOrgasming / isOrgasmPleasure first and the orgasm timer in the MIDDLE; a null reference in one of the cosmetic steps (voice, ahegao morphs,
    stimulation spread) left the timer unset, so the orgasm lasted a frame or two and 'Orgasm Now' threw. Every step is guarded, the timer is always set."""
    t = t.replace(chr(13) + chr(10), chr(10))
    t = patch(t, """			isOrgasming = true;
			isOrgasmPleasure = true;
			orgasmCount.val += 1f;
			Stimulate(3f, true);
			onOrgasmStartTrigger.Trigger();
			// float eyeRoll = Random.Range(-22, -10f);
			ahegao.Run();
			orgasmTimer = orgasmTime.val + orgasmFadeTime.val;
			PlayRandomMoan(5);
			multiOrgasmCount.val += 1;
			if(FillMeUp.isFuta) altFutaStim.Orgasm();
			foreach (var fuckable in FillMeUp.fuckables)
			{
				if(fuckable.penetrator != null && fuckable.penetrator.stimReceiver) fuckable.penetrator.stimReceiver.ForceOrgasm(true);
			}""", """			isOrgasming = true;
			isOrgasmPleasure = true;
			orgasmCount.val += 1f;
			orgasmTimer = orgasmTime.val + orgasmFadeTime.val;   // [OrgasmSuite] set first: the steps below may throw
			try { Stimulate(3f, true); } catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (stimulate): " + e.Message); }
			try { onOrgasmStartTrigger.Trigger(); } catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (start trigger): " + e.Message); }
			try { ahegao.Run(); } catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (ahegao): " + e.Message); }
			orgasmTimer = orgasmTime.val + orgasmFadeTime.val;
			try { PlayRandomMoan(5); } catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (moan): " + e.Message); }
			multiOrgasmCount.val += 1;
			try { if(FillMeUp.isFuta) altFutaStim.Orgasm(); } catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (futa): " + e.Message); }
			try
			{
				foreach (var fuckable in FillMeUp.fuckables)
				{
					if(fuckable.penetrator != null && fuckable.penetrator.stimReceiver) fuckable.penetrator.stimReceiver.ForceOrgasm(true);
				}
			}
			catch (System.Exception e) { SuperController.LogError("BodyLanguage orgasm (penetrators): " + e.Message); }""", what='bl doorgasm')
    return t


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
              '    JSONStorableFloat shuffleDelayJSON;\n'
              '    MVRScript trembleScript, zajeScript;\n'
              '    bool trembleStartedByBridge;\n'
              '    string orgasmHookStatus = "no orgasm yet";\n', what='fields')
    t = patch(t, '        if (owEnabledJSON != null) owEnabledJSON.setCallbackFunction += v => { if (!v) ClearOrgasmWardrobe(); };\n',
              '        if (owEnabledJSON != null) owEnabledJSON.setCallbackFunction += v => { if (!v) ClearOrgasmWardrobe(); };\n'
              '\n'
              '        // [OrgasmSuite] orgasm hooks\n'
              '        CreateHeader("Orgasm Hooks", false, new Color(0.78f, 0.5f, 1f));\n'
              '        trembleOnOrgasmJSON = RegisterToggle("AutoTremble on orgasm", true, false, "Starts AutoTremble on this Person when an orgasm fires and stops it when the fluids stop.");\n'
              '        shuffleOnOrgasmJSON = RegisterToggle("ZajeShuffle on orgasm", true, true, "Fires the ZajeShuffle Shuffle action (random UIAssist button) on every orgasm.");\n'
              '        shuffleDelayJSON = RegisterSlider("ZajeShuffle delay (sec)", 0f, 0f, 60f, true, "F1", "How long after the orgasm the shuffle runs. 0 = at once.");\n',
              what='ui')
    t = patch(t, '        StoreFloat(jc, wetClothesDurationJSON);\n',
              '        StoreFloat(jc, wetClothesDurationJSON);\n        StoreBool(jc, trembleOnOrgasmJSON);\n        StoreBool(jc, shuffleOnOrgasmJSON);\n        StoreFloat(jc, shuffleDelayJSON);\n', what='store')
    t = patch(t, '            RestoreFloat(jc, wetClothesDurationJSON);\n',
              '            RestoreFloat(jc, wetClothesDurationJSON);\n            RestoreBool(jc, trembleOnOrgasmJSON);\n            RestoreBool(jc, shuffleOnOrgasmJSON);\n            RestoreFloat(jc, shuffleDelayJSON);\n', what='restore')
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
                float shuffleDelay = shuffleDelayJSON == null ? 0f : shuffleDelayJSON.val;
                if (zajeScript == null) note += "shuffle: ZajeShuffle not loaded; ";
                else if (shuffleDelay > 0.01f)
                {
                    StartCoroutine(DelayedShuffle(shuffleDelay));
                    note += "shuffle in " + shuffleDelay.ToString("0.#") + " s; ";
                }
                else note += CallAction(zajeScript, "Shuffle") ? "shuffled; " : "shuffle: action missing; ";
            }
        }
        catch (Exception e) { note += "hook error: " + e.Message; }
        orgasmHookStatus = note.Length == 0 ? "hooks off" : note.TrimEnd(' ', ';');
    }

    IEnumerator DelayedShuffle(float delay)
    {
        yield return new WaitForSeconds(delay);
        try
        {
            if (shuffleOnOrgasmJSON != null && shuffleOnOrgasmJSON.val && zajeScript != null && CallAction(zajeScript, "Shuffle"))
                orgasmHookStatus = "shuffled after " + delay.ToString("0.#") + " s";
        }
        catch { }
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
    # tremble is only ever started by an orgasm (FluidBridge) or by hand: a scene saved while it ran must not restart it on load
    t = patch(t, "            RegisterBool(active);\n", "            RegisterBool(active);\n            active.isStorable = false;   // [OrgasmSuite]\n", what='active not stored')
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
    dlldir = os.path.join(OUT, 'dll')
    if os.path.isdir(dlldir):
        shutil.rmtree(dlldir)
    os.makedirs(dlldir)

    def add_dll(folder, asm, sources):
        data = build_dll(asm, sources)
        put(ROOT + folder + '/' + asm + '.dll', data)
        open(os.path.join(dlldir, asm + '.dll'), 'wb').write(data)
        print('  built %-40s %7d bytes' % (asm, len(data)))

    # SexyFluids (+ its shader bundle next to the DLL, + shipped presets: those only when missing)
    z = zopen('sexyfluids')
    add_dll('SexyFluids', 'ZeroT.OrgasmSuite.SexyFluids',
            collect_sources('sexyfluids', 'Custom/Scripts/Foost/SexyFluids/', 'SexyFluids.cslist', {'src/utils/DataResourceProvider.cs': patch_sexyfluids_bundle}))
    put(ROOT + 'SexyFluids/data/Foost.SexyFluids.fluid_shaders.assetbundle', z.read('Custom/Scripts/Foost/SexyFluids/data/Foost.SexyFluids.fluid_shaders.assetbundle'))
    for n in z.namelist():
        if n.startswith('Saves/PluginData/Foost/SexyFluids/') and not n.endswith('/'):
            put(n, z.read(n), soft=True)
    # BodyLanguage Lite and ArousalHUD: two scripts, two assemblies (VaM runs every MVRScript class it finds in one)
    add_dll('BodyLanguage_Lite', 'ZeroT.OrgasmSuite.BodyLanguageLite', collect_sources('bllite', 'Custom/Scripts/CheesyFX/BodyLanguage_Lite/', 'BodyLanguage_Lite.cslist', {'BodyLanguage_Lite.cs': patch_lite_collision}))
    add_dll('BodyLanguage_Lite', 'ZeroT.OrgasmSuite.ArousalHUD', collect_sources('bllite', 'Custom/Scripts/CheesyFX/BodyLanguage_Lite/', 'ArousalHUD.cslist'))
    # BodyLanguage (full)
    bl_sources = collect_sources('bl', 'Custom/Scripts/CheesyFX/BodyLanguage/', 'BodyLanguage.cslist', {'src/FillMeUp/Orifice.cs': patch_bl_orifice, 'src/shared/TouchCollisionTrigger.cs': patch_bl_trigger, 'src/FillMeUp/OrificeTriggerHandler.cs': patch_bl_orificetrigger, 'src/FillMeUp/FillMeUp.cs': lambda s: patch_bl_resources(patch_bl_fillmeup(s)), 'src/ReadMyLips/ReadMyLips.cs': patch_bl_orgasm})
    bl_sources['OrgasmSuiteFilter.cs'] = FILTER_CS
    add_dll('BodyLanguage', 'ZeroT.OrgasmSuite.BodyLanguage', bl_sources)
    # FluidBridge (patched)
    add_dll('FluidBridge', 'ZeroT.OrgasmSuite.FluidBridge', collect_sources('fluidbridge', 'Custom/Scripts/ZeroT/FluidBridge/', 'FluidBridge.cslist', {'FluidBridge.cs': patch_fluidbridge}))
    # AutoTremble (patched), plus the user's default preset when it is missing
    z = zopen('autotremble')
    add_dll('AutoTremble', 'ZeroT.OrgasmSuite.AutoTremble',
            {'AutoTremble.cs': patch_autotremble(z.read('Custom/Scripts/DBP/DBP.AutoTremble/AutoTremble.cs').decode('utf-8-sig'))})
    preset = os.path.join(VAM, 'Saves', 'PluginPresets', 'AutoTremble', 'AutoTremblePreset.json')
    if os.path.exists(preset):
        put('Saves/PluginPresets/AutoTremble/AutoTremblePreset.json', open(preset, 'rb').read(), soft=True)
    # ZajeShuffle, StimulationHUD
    add_dll('ZajeShuffle', 'ZeroT.OrgasmSuite.ZajeShuffle', collect_sources('zaje', 'Custom/Scripts/ZeroT/ZajeShuffle/', 'ZajeShuffle.cslist'))
    add_dll('StimulationHUD', 'ZeroT.OrgasmSuite.StimulationHUD', collect_sources('stimhud', 'Custom/Scripts/ZeroT/StimulationHUD/', 'StimulationHUD.cslist'))

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
