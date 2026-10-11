#!/usr/bin/env python
"""vamdev - fast build / sim-test / deploy / pack loop for the VaM BepInEx plugins and .var packages.

  python zerot/tools/vamdev.py list
  python zerot/tools/vamdev.py test  <name|all>      build, then run the logic scenario (vamsim) and the UI check (uisim)
  python zerot/tools/vamdev.py build <name|all>
  python zerot/tools/vamdev.py ship  <name>          build + test + deploy (stops if a test fails)
  python zerot/tools/vamdev.py deploy <name> [--force]   copy to T:\\New folder\\BepInEx\\plugins (keeps one .bak)
  python zerot/tools/vamdev.py simbuild              rebuild the simulator (UnityShim, vamsim, uisim) and regenerate stubs
  python zerot/tools/vamdev.py unpack <file.var> [dest]
  python zerot/tools/vamdev.py pack <folder> <out.var>   zip with forward slashes + a fresh meta.json check
  python zerot/tools/vamdev.py cslcheck <file.var|folder>  Roslyn compile check of VaM .cs/.cslist scripts against the real VaM assemblies
VaM install is read from VAM_DIR (default T:\\New folder).
"""
import json, os, re, shutil, subprocess, sys, tempfile, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ZEROT = os.path.dirname(HERE)
REPO = os.path.dirname(ZEROT)
ART = os.path.join(REPO, 'artifacts', 'Release')
VAM = os.environ.get('VAM_DIR', 'T:\\New folder')
PLUGINS = os.path.join(VAM, 'BepInEx', 'plugins')
SIM = os.path.join(ZEROT, 'UiSim')
VAMSIM = os.path.join(SIM, 'VamSim', 'bin', 'Release', 'net8.0-windows', 'vamsim.exe')
UISIM = os.path.join(SIM, 'bin', 'uisim.exe')

# name: (project folder/csproj, built dll name, live dll name or None = same, scenario or None)
P = {
    'givemefps': ('GiveMeFPSStandalone/ZeroT.GiveMeFPSStandalone.csproj', 'ZeroT.GiveMeFPSStandalone.dll', None),
    'sceneperformance': ('ScenePerformanceStandalone/ZeroT.ScenePerformanceStandalone.csproj', 'ZeroT.ScenePerformanceStandalone.dll', None),
    'deeper': ('DeeperPluginStandalone/DeeperPluginStandalone.csproj', 'ZeroT.DeeperPluginStandalone.dll', None),
    'movecontrols': ('MoveControlsToBody/MoveControlsToBodyStandalone.csproj', 'MoveControlsToBodyStandalone.dll', None),
    'smartwardrobe': ('SmartWardrobeUi91/SmartWardrobeUi91.csproj', 'Bplus.SmartWardrobeStandalone.9.1.0.dll', None),
    'xrayslap': ('XRaySlap10/ZeroT.XRaySlapStandalone.1.0.0.csproj', 'ZeroT.XRaySlapStandalone.dll', 'ZeroT.XRaySlapStandalone.1.0.0.dll'),
    'counterthrust': ('CounterThrust/Hasno.CounterThrustStandalone.4.2.4.csproj', 'Hasno.CounterThrustStandalone.4.2.4.dll', None),
    'iamjoi': ('IamjoiGarments/ZeroT.IamjoiGarmentsStandalone.1.1.0.SmallUI.csproj', 'ZeroT.IamjoiGarmentsStandalone.dll', 'ZeroT.IamjoiGarmentsStandalone.1.1.0.SmallUI.dll'),
    'orifice': ('OrificeDynamicsStandalone/OrificeDynamicsStandalone.csproj', 'Skynet.OrificeDynamicsStandalone.1.0.0.dll', None),
    'fluidbridge': ('FluidBridgeHelper/ZeroT.FluidBridgeHelper.1.1.0.csproj', 'ZeroT.FluidBridgeHelper.1.1.0.dll', None),
    'anus': ('AnusPhysicsControl/Charlestone.AnusPhysicsControlStandalone.1.2.0.csproj', 'Charlestone.AnusPhysicsControlStandalone.1.2.0.dll', None),
    'penetrationcounter': ('PenetrationCounterUi/PenetrationCounterUi.csproj', 'PenetrationCounter.BepInEx.0.8.3.dll', None),
    'ppa': ('PenisPathAlignmentUi/PenisPathAlignmentUi.csproj', 'HelicalPink.PenisPathAlignmentStandalone.10.1.2.dll', None),
    'slaps': ('ProceduralSlaps/RidgeRock.ProceduralSlaps.Standalone.csproj', 'RidgeRock.ProceduralSlaps.Standalone.dll', None),
    'vpbrandomlook': ('VpbRandomLook/VpbRandomLook.csproj', 'VpbRandomLook.dll', None),
    'ppuiskin': ('PpUiSkin/PpUiSkin.csproj', 'ZeroT.ParticlePinnacleUiSkin.dll', None),
    'decalmakerhelper': ('DecalMakerHelper/ZeroT.DecalMakerHelper.csproj', 'ZeroT.DecalMakerHelper.dll', None),
    'bodylanguagehelper': ('BodyLanguageHelper/ZeroT.BodyLanguageHelper.csproj', 'ZeroT.BodyLanguageHelper.dll', None),
    'uiassisthelper': ('UIAssistHelper/ZeroT.UIAssistHelper.csproj', 'ZeroT.UIAssistHelper.dll', None),
    'controllersappearance': ('ControllersAppearanceStandalone/ZeroT.ControllersAppearanceStandalone.csproj', 'ZeroT.ControllersAppearanceStandalone.dll', None),
    'appearanceloaderhelper': ('AppearanceLoaderHelper/ZeroT.AppearanceLoaderHelper.csproj', 'ZeroT.AppearanceLoaderHelper.dll', None),
    'zajeshufflehelper': ('ZajeShuffleHelper/ZeroT.ZajeShuffleHelper.csproj', 'ZeroT.ZajeShuffleHelper.dll', None),
    'shakeit': ('ShakeItStandalone/ZeroT.ShakeItStandalone.csproj', 'ZeroT.ShakeItStandalone.dll', None),
    'autoflutterhip': ('AutoFlutterHipStandalone/ZeroT.AutoFlutterHipStandalone.csproj', 'ZeroT.AutoFlutterHipStandalone.dll', None),
    'clothingbackface': ('ClothingBackfaceStandalone/ZeroT.ClothingBackfaceStandalone.csproj', 'ZeroT.ClothingBackfaceStandalone.dll', None),
    'forcepersoncache': ('ForcePersonCacheStandalone/ZeroT.ForcePersonCacheStandalone.csproj', 'ZeroT.ForcePersonCacheStandalone.dll', None),
    'uxplushelper': ('UXPlusHelper/ZeroT.UXPlusHelper.csproj', 'ZeroT.UXPlusHelper.dll', None),
    'personguard': ('PersonGuard/ZeroT.PersonGuard.csproj', 'ZeroT.PersonGuard.dll', None),
    'microphysics': ('MicroPhysicsFixerStandalone/ZeroT.MicroPhysicsFixerStandalone.csproj', 'ZeroT.MicroPhysicsFixerStandalone.dll', None),
    'bodyweight': ('BodyWeightStandalone/ZeroT.BodyWeightStandalone.csproj', 'ZeroT.BodyWeightStandalone.dll', None),
    'extraautogenitals': ('ExtraAutoGenitalsStandalone/ZeroT.ExtraAutoGenitalsStandalone.csproj', 'ZeroT.ExtraAutoGenitalsStandalone.dll', None),
}
# plugins whose logic scenario exists in VamSim/Scenarios.cs (others get the generic smoke run only if they are headless)
HEADLESS = {'shakeit', 'autoflutterhip', 'forcepersoncache', 'clothingbackface', 'zajeshufflehelper', 'appearanceloaderhelper', 'controllersappearance', 'uxplushelper', 'personguard', 'microphysics', 'bodyweight', 'extraautogenitals', 'decalmakerhelper', 'bodylanguagehelper', 'uiassisthelper'}
HELPERS = {'decalmakerhelper', 'bodylanguagehelper', 'uiassisthelper', 'uxplushelper', 'appearanceloaderhelper', 'zajeshufflehelper'}   # Harmony helpers: need the script assemblies from `simtargets`
UI_PLUGINS = {'givemefps', 'sceneperformance', 'deeper', 'movecontrols', 'smartwardrobe', 'xrayslap', 'counterthrust', 'iamjoi', 'orifice',
              'fluidbridge', 'anus', 'penetrationcounter', 'ppa', 'slaps', 'vpbrandomlook'}


def run(cmd, cwd=None, quiet=False):
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if not quiet:
        out = (r.stdout or '') + (r.stderr or '')
        sys.stdout.write(out)
    return r


def names(arg):
    if arg == 'all':
        return list(P)
    if arg not in P:
        sys.exit('unknown plugin %r; try: list' % arg)
    return [arg]


def build(name):
    proj, dll, _ = P[name]
    r = run(['dotnet', 'build', os.path.join(ZEROT, proj), '-c', 'Release', '--nologo', '-v', 'q'], quiet=True)
    text = (r.stdout or '') + (r.stderr or '')
    errs = sorted(set(l.strip() for l in text.splitlines() if ' error ' in l))
    if r.returncode != 0 or errs:
        print('BUILD FAILED  ' + name)
        for e in errs[:12]:
            print('  ' + e[:240])
        return False
    print('built  %-20s %s' % (name, dll))
    return True


def sim_logic(name):
    if name in HELPERS and not all(os.path.exists(os.path.join(SIM, 'in', 'targets', t + '.dll')) for t in TARGETS):
        print('building the script assemblies the helpers patch (simtargets)...')
        cmd_simtargets()
        run(['powershell', '-NoProfile', '-File', os.path.join(SIM, 'regen.ps1')], cwd=SIM, quiet=True)
        run(['dotnet', 'build', os.path.join(SIM, 'VamSim', 'VamSim.csproj'), '-c', 'Release', '--nologo', '-v', 'q'], quiet=True)
    dll = os.path.join(ART, P[name][1])
    if not os.path.exists(VAMSIM):
        print('vamsim not built: run  vamdev.py simbuild')
        return False
    r = run([VAMSIM, dll], quiet=True)
    out = r.stdout or ''
    print(out.rstrip())
    return r.returncode == 0


def sim_ui(name):
    if not os.path.exists(os.path.join(SIM, 'check.ps1')):
        return True
    inp = os.path.join(SIM, 'in')
    os.makedirs(inp, exist_ok=True)
    shutil.copy(os.path.join(ART, P[name][1]), inp)
    base = os.path.splitext(P[name][1])[0]
    r = run(['powershell', '-NoProfile', '-File', os.path.join(SIM, 'check.ps1'), '-Only', base.replace('.', '\\.')], quiet=True)
    lines = [l for l in (r.stdout or '').splitlines() if l.strip()]
    bad = [l for l in lines if 'CLIPPED' in l or 'CLIP ' in l or 'ERR' in l]
    print('ui check  %-18s %s' % (name, 'clean' if not bad else '%d issue line(s)' % len(bad)))
    for l in bad[:8]:
        print('  ' + l[:200])
    return not bad


def cmd_test(arg):
    ok = True
    for n in names(arg):
        if not build(n):
            ok = False
            continue
        if n in HEADLESS:
            ok = sim_logic(n) and ok
        elif n in UI_PLUGINS:
            ok = sim_ui(n) and ok
        else:
            print('(no sim for %s; build only)' % n)
    print('TESTS PASSED' if ok else 'TESTS FAILED')
    return ok


def live_path(name):
    _, dll, live = P[name]
    live = live or dll
    for sub in ('New folder', ''):
        p = os.path.join(PLUGINS, sub, live)
        if os.path.exists(p):
            return p
    return os.path.join(PLUGINS, 'New folder', live)


def vam_running():
    r = run(['tasklist'], quiet=True)
    return 'VaM.exe' in (r.stdout or '')


def cmd_deploy(name, force=False):
    src = os.path.join(ART, P[name][1])
    if not os.path.exists(src):
        sys.exit('not built: ' + src)
    dst = live_path(name)
    if os.path.exists(dst) and not os.path.exists(dst + '.bak'):
        shutil.copy2(dst, dst + '.bak')
    try:
        shutil.copy2(src, dst)
    except PermissionError:
        sys.exit('locked (VaM running?): ' + dst + '  -> close VaM, or stage it in zerot/UiKit/staged and run install_staged.ps1')
    print('deployed ' + dst + ('   (VaM is running: takes effect next launch)' if vam_running() else ''))


def cmd_ship(name):
    if not build(name):
        sys.exit(1)
    if name in HEADLESS and not sim_logic(name):
        sys.exit('sim failed - not deployed')
    if name in UI_PLUGINS and not sim_ui(name):
        sys.exit('ui check failed - not deployed')
    cmd_deploy(name)


def cmd_simbuild():
    for p in ('UnityShim/UnityShim.csproj',):
        pass
    r = run(['powershell', '-NoProfile', '-File', os.path.join(SIM, 'regen.ps1')], cwd=SIM)
    r2 = run(['dotnet', 'build', os.path.join(SIM, 'VamSim', 'VamSim.csproj'), '-c', 'Release', '--nologo', '-v', 'q'], quiet=True)
    print('vamsim build', 'ok' if r2.returncode == 0 else 'FAILED')
    if r2.returncode != 0:
        print((r2.stdout or '')[-1500:])


def cmd_unpack(var, dest=None):
    dest = dest or os.path.splitext(os.path.basename(var))[0]
    with zipfile.ZipFile(var) as z:
        names = z.namelist()
        for info in z.infolist():
            n = info.filename
            if info.is_dir() or (info.file_size == 0 and any(o.startswith(n.rstrip('/') + '/') for o in names)):
                os.makedirs(os.path.join(dest, n), exist_ok=True)   # some packers list folders without the trailing slash
                continue
            z.extract(info, dest)
        print('%d entries -> %s' % (len(names), dest))


def cmd_pack(folder, out):
    if not os.path.exists(os.path.join(folder, 'meta.json')):
        sys.exit('meta.json missing in ' + folder)
    json.load(open(os.path.join(folder, 'meta.json'), encoding='utf-8-sig'))
    n = 0
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(folder):
            for f in sorted(files):
                full = os.path.join(root, f)
                rel = os.path.relpath(full, folder).replace(os.sep, '/')
                z.write(full, rel)
                n += 1
    with zipfile.ZipFile(out) as z:
        assert z.testzip() is None
    print('packed %d files -> %s' % (n, out))


def cmd_cslcheck(target, out=None):
    work = tempfile.mkdtemp(prefix='cslcheck_')
    if target.lower().endswith('.var'):
        src = os.path.join(work, 'var')
        cmd_unpack(target, src)
    else:
        src = target
    cs = []
    for root, _, files in os.walk(src):
        for f in files:
            if f.lower().endswith('.cslist'):
                base = root
                for line in open(os.path.join(root, f), encoding='utf-8-sig').read().splitlines():
                    line = line.strip()
                    if line and not line.startswith('//'):
                        p = os.path.normpath(os.path.join(base, line.replace('\\', '/')))
                        if os.path.exists(p):
                            cs.append(p)
    if not cs:
        for root, _, files in os.walk(src):
            cs += [os.path.join(root, f) for f in files if f.lower().endswith('.cs')]
    cs = sorted(set(cs))
    managed = os.path.join(VAM, 'VaM_Data', 'Managed')
    refs = []
    for f in sorted(os.listdir(managed)):
        if f.endswith('.dll') and (f.startswith('UnityEngine') or f in ('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'SimpleJSON.dll', 'Leap.dll', 'LeapCSharp.NET3.5.dll', 'LeapCSharp.dll')):
            refs.append('<Reference Include="%s"><HintPath>%s</HintPath></Reference>' % (f[:-4], os.path.join(managed, f)))
    items = '\n'.join('<Compile Include="%s" />' % c for c in cs)
    asm = os.path.splitext(os.path.basename(out))[0] if out else 'chk'
    open(os.path.join(work, 'Program.cs'), 'w').write('static class P{static void Main(){}}' if not out else '')
    open(os.path.join(work, 'chk.csproj'), 'w').write(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>' + ('Library' if out else 'Exe') + '</OutputType><AssemblyName>' + asm + '</AssemblyName><TargetFramework>net48</TargetFramework><LangVersion>latest</LangVersion><DefineConstants>VAM_GT_1_20;VAM_GT_1_20_77_13;VAM_GT_1_21;VAM_1_22</DefineConstants>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>CS0108;CS0114;CS0168;CS0219;CS0649;CS0169;CS0414;CS0618;CS1998;CS0659;CS0661;CS0162;CS0105;CS1591</NoWarn></PropertyGroup>'
        '<ItemGroup>%s<Compile Include="Program.cs" />%s<Reference Include="System.Core" /></ItemGroup></Project>' % (items, '\n'.join(refs)))
    r = run(['dotnet', 'build', os.path.join(work, 'chk.csproj'), '--nologo', '-v', 'q'], quiet=True)
    text = (r.stdout or '') + (r.stderr or '')
    errs = sorted(set(l.strip() for l in text.splitlines() if ' error ' in l))
    print('%d source files' % len(cs))
    if errs:
        print('COMPILE ERRORS (%d)' % len(errs))
        for e in errs[:40]:
            print('  ' + e.replace(src, '')[:230])
        print('(note: VaM compiles with mcs, which is stricter about newer C# syntax - keep to C# 6 style)')
    else:
        print('compiles clean against VaM_Data\\Managed')
        if out:
            built = None
            for root, _, files in os.walk(os.path.join(work, 'bin')):
                for f in files:
                    if f == asm + '.dll':
                        built = os.path.join(root, f)
            if built:
                os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
                shutil.copy(built, out)
                print('wrote ' + out)
    shutil.rmtree(work, ignore_errors=True)
    return not errs


# packages whose script code the Harmony helpers patch -> built into UiSim/in/targets so the simulator can load them
TARGETS = {
    'DecalMakerScripts': 'Chokaphi.DecalMaker.*.var',
    'BodyLanguageScripts': 'CheesyFX.BodyLanguage_Lite.*.var',
    'UIAssistScripts': 'JayJayWon.UIAssist(Patron).*.var',
    'UXPlusScripts': 'everlaster.UXPlus.*.var',
    'ControllersAppearanceScripts': 'Datel.ControllersAppearance.*.var',
    'AppearanceLoaderScripts': 'ky1001.AppearanceLoader.*.var',
    'ZajeShuffleScripts': 'ZeroT.ZajeShuffle.*.var',
    'ForcePersonCacheScripts': 'JayJayWon.ForcePersonCache.*.var',
    'ClothingBackfaceScripts': 'Nial.?Plugins?_Clothing_backface.*.var',
}


def newest_var(pattern):
    import fnmatch
    best = None
    for root, _, files in os.walk(os.path.join(VAM, 'AddonPackages')):
        for f in files:
            if fnmatch.fnmatch(f, pattern):
                n = int(re.findall(r'\.(\d+)\.var$', f)[-1]) if re.findall(r'\.(\d+)\.var$', f) else 0
                if best is None or n > best[0]:
                    best = (n, os.path.join(root, f))
    return best[1] if best else None


def cmd_simtargets():
    ok = True
    for name, pat in TARGETS.items():
        v = newest_var(pat)
        if not v:
            print('SKIP %s (no %s in AddonPackages)' % (name, pat))
            continue
        print('== %s <- %s' % (name, os.path.basename(v)))
        ok = cmd_cslcheck(v, os.path.join(SIM, 'in', 'targets', name + '.dll')) and ok
    return ok


def scene_plugins(scene):
    """{(package, path)} of every script a scene loads (PluginManager / clothing 'plugins' dictionaries)."""
    t = open(scene, encoding='utf-8').read()
    return sorted(set(re.findall(r'"plugin#\d+"\s*:\s*"([^":]+):/([^"]+\.(?:cs|cslist))"', t)))


def resolve_var(pkg):
    stem, ver = pkg.rsplit('.', 1)
    idx = {}
    for sub in ('AddonPackages', 'AllPackages'):
        for root, _, files in os.walk(os.path.join(VAM, sub)):
            for f in files:
                if f.lower().endswith('.var'):
                    idx.setdefault(f[:-4].lower(), os.path.join(root, f))
    if ver == 'latest' or ver.startswith('min'):
        best = sorted([(int(k.rsplit('.', 1)[1]), v) for k, v in idx.items() if k.startswith(stem.lower() + '.') and k.rsplit('.', 1)[1].isdigit()])
        return best[-1][1] if best else None
    return idx.get(pkg.lower())


def cmd_scenescripts(scene):
    """Compile every script the scene loads (from its package, against VaM's real Managed DLLs) into UiSim/in/scene/ so vamsim --scene can run them."""
    out = os.path.join(SIM, 'in', 'scene')
    os.makedirs(out, exist_ok=True)
    ok = True
    for pkg, path in scene_plugins(scene):
        var = resolve_var(pkg)
        name = re.sub(r'[^A-Za-z0-9_.]', '_', pkg + '__' + path) + '.dll'
        if not var:
            print('MISSING PACKAGE %s' % pkg); ok = False; continue
        print('== %s :/%s' % (pkg, path))
        ok = compile_one(var, path, os.path.join(out, name)) and ok
    return ok


def compile_one(var, entry, out):
    work = tempfile.mkdtemp(prefix='cslone_')
    src = os.path.join(work, 'var')
    cmd_unpack(var, src)
    full = os.path.normpath(os.path.join(src, entry.replace(chr(92), '/')))
    cs = []
    if entry.lower().endswith('.cslist'):
        for line in open(full, encoding='utf-8-sig').read().splitlines():
            line = line.strip()
            if line and not line.startswith('//'):
                q = os.path.normpath(os.path.join(os.path.dirname(full), line.replace(chr(92), '/')))
                if os.path.exists(q):
                    cs.append(q)
    else:
        cs = [full]
    managed = os.path.join(VAM, 'VaM_Data', 'Managed')
    refs = []
    for f in sorted(os.listdir(managed)):
        if f.endswith('.dll') and (f.startswith('UnityEngine') or f in ('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'SimpleJSON.dll', 'Leap.dll', 'LeapCSharp.NET3.5.dll', 'LeapCSharp.dll')):
            refs.append('<Reference Include="%s"><HintPath>%s</HintPath></Reference>' % (f[:-4], os.path.join(managed, f)))
    asm = os.path.splitext(os.path.basename(out))[0]
    open(os.path.join(work, 'chk.csproj'), 'w').write(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Library</OutputType><AssemblyName>' + asm + '</AssemblyName><TargetFramework>net48</TargetFramework><LangVersion>latest</LangVersion><DefineConstants>VAM_GT_1_20;VAM_GT_1_20_77_13;VAM_GT_1_21;VAM_1_22</DefineConstants>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>CS0108;CS0114;CS0168;CS0219;CS0649;CS0169;CS0414;CS0618;CS1998;CS0659;CS0661;CS0162;CS0105;CS1591</NoWarn></PropertyGroup>'
        '<ItemGroup>%s%s<Reference Include="System.Core" /></ItemGroup></Project>' % (chr(10).join('<Compile Include="%s" />' % c for c in cs), chr(10).join(refs)))
    r = run(['dotnet', 'build', os.path.join(work, 'chk.csproj'), '--nologo', '-v', 'q'], quiet=True)
    text = (r.stdout or '') + (r.stderr or '')
    errs = sorted(set(l.strip() for l in text.splitlines() if ' error ' in l))
    good = False
    if errs:
        print('  COMPILE ERRORS (%d)' % len(errs))
        for e in errs[:10]:
            print('   ' + e.replace(src, '')[:200])
    else:
        for root, _, files in os.walk(os.path.join(work, 'bin')):
            for f in files:
                if f == asm + '.dll':
                    shutil.copy(os.path.join(root, f), out); good = True
        print('  ok -> ' + os.path.basename(out) if good else '  no output')
    shutil.rmtree(work, ignore_errors=True)
    return good


def cmd_sheet(out=None):
    """Render every plugin window collapsed (uisim) from the BUILT dlls and montage them into one PNG."""
    from PIL import Image, ImageDraw
    out = out or os.path.join(SIM, 'out', 'collapsed_huds.png')
    ckeys = ['Window.Collapsed', 'GUI.Collapsed', 'Desktop GUI.Collapsed', 'Touch Filters.ControlWindowCollapsed']
    tiles = []
    wd = os.path.join(SIM, 'out', 'sheet')
    os.makedirs(wd, exist_ok=True)
    host = os.path.join(SIM, 'hosts', 'ParticlePinnacleThighCompressor.52.0.1.dll') + '::com.particlepinnacle.thighcompressorandvibrations'
    for n in sorted(UI_PLUGINS | {'ppuiskin'}):
        dll = os.path.join(ART, P[n][1])
        if not os.path.exists(dll):
            continue
        cmd = [UISIM, dll, '--out', wd, '--tag', 'sheet', '--w', '700', '--h', '420', '--deps', os.path.join(SIM, 'in')]
        for k in ckeys:
            cmd += ['--set', k + '=true']
        if n == 'ppuiskin':
            cmd += ['--host', host]
        run(cmd, quiet=True)
        png = os.path.join(wd, os.path.splitext(P[n][1])[0] + '_sheet.png')
        if os.path.exists(png):
            tiles.append((n, Image.open(png).convert('RGB')))
    tiles.append(('personguard', None))   # headless: no window
    cw, ch, cols = 360, 190, 4
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * cw, rows * ch), (24, 24, 28))
    d = ImageDraw.Draw(sheet)
    for i, (n, im) in enumerate(tiles):
        x, y = (i % cols) * cw, (i // cols) * ch
        d.text((x + 8, y + 4), n, fill=(200, 200, 200))
        if im is None:
            d.rectangle([x + 8, y + 24, x + cw - 8, y + 90], outline=(90, 90, 100))
            d.text((x + 20, y + 50), 'no window - headless (logs only)', fill=(140, 140, 150))
            continue
        bbox = im.point(lambda v: 255 if v > 14 else 0).convert('L').getbbox()
        if bbox:
            im = im.crop((max(0, bbox[0] - 4), max(0, bbox[1] - 4), min(im.width, bbox[2] + 4), min(im.height, bbox[3] + 4)))
        im.thumbnail((cw - 16, ch - 30))
        sheet.paste(im, (x + 8, y + 24))
    sheet.save(out)
    print('sheet', out, len(tiles), 'tiles')
    return out


def cmd_simall():
    """Whole-install check, parallel: every logic scenario (one vamsim process) + every UI plugin x 5 window states (uisim, all cores)."""
    import time
    from concurrent.futures import ThreadPoolExecutor
    t0 = time.time()
    ok = True
    ckeys = ['Window.Collapsed', 'GUI.Collapsed', 'Desktop GUI.Collapsed', 'Touch Filters.ControlWindowCollapsed']
    wk = ['Window.Width', 'Window.ExpandedWidth', 'Window.WindowWidth', 'GUI.WindowWidth', 'Desktop GUI.WindowWidth', 'Touch Filters.ControlWindowWidth']
    hk = [k.replace('Width', 'Height') for k in wk]
    sk = ['Window.Scale', 'Window.UIScale', 'Window.UserScale', 'GUI.ManualUIScale', 'Desktop GUI.UIScale', 'Touch Filters.ControlWindowScale']
    def sets(keys, v):
        return [x for k in keys for x in ('--set', '%s=%s' % (k, v))]
    variants = {'default': [], 'collapsed': sets(ckeys, 'true'), 'minsize': sets(wk, 1) + sets(hk, 1),
                'scale2': sets(sk, 2.0) + ['--w', '2560', '--h', '1440'], 'scalesmall': sets(sk, 0.65)}
    host = os.path.join(SIM, 'hosts', 'ParticlePinnacleThighCompressor.52.0.1.dll') + '::com.particlepinnacle.thighcompressorandvibrations'
    jobs = []
    for n in sorted(UI_PLUGINS | {'ppuiskin'}):
        dll = os.path.join(ART, P[n][1])
        if not os.path.exists(dll):
            continue
        for v, extra in variants.items():
            wd = os.path.join(SIM, 'out', 'simall', n + '_' + v)
            os.makedirs(wd, exist_ok=True)
            cmd = [UISIM, dll, '--deps', os.path.join(SIM, 'in'), '--out', wd, '--tag', v] + extra + (['--host', host] if n == 'ppuiskin' else [])
            jobs.append((n, v, cmd))
    def one(j):
        r = run(j[2], quiet=True)
        out = (r.stdout or '') + (r.stderr or '')
        clips = [l for l in out.splitlines() if l.startswith('CLIP')]
        errs = [l for l in out.splitlines() if l.startswith('ERR') and 'Update:' not in l and 'coroutine:' not in l]
        return j[0], j[1], clips, errs
    with ThreadPoolExecutor(max_workers=os.cpu_count() or 4) as ex:
        logic = ex.submit(run, [VAMSIM, '--all', ART], None, True)
        res = list(ex.map(one, jobs))
        lr = logic.result()
    lo = (lr.stdout or '').splitlines()
    for l in lo:
        if l.startswith(('PASS', 'FAIL', 'ALL PASSED')) or 'FAILED' in l:
            print(l)
    ok &= lr.returncode == 0
    clipped = 0
    for n, v, clips, errs in res:
        clipped += len(clips)
        for l in errs[:2]:
            ok = False
            print('UI ERR %s/%s %s' % (n, v, l[:200]))
    bad = sorted(set((n, v, len(c)) for n, v, c, _ in res if c))
    for n, v, c in bad:
        print('  clipped %-20s %-10s %d' % (n, v, c))
    print('ui: %d renders, %d clipped lines; logic: %s; %.1fs' % (len(res), clipped, 'ok' if lr.returncode == 0 else 'FAILED', time.time() - t0))
    return ok


def scene_lint(path):
    """Static scene check (what the sim can't load): CUA/plugin offsets out of their slider range, absurd values, L/R bone pairs that are not mirrored."""
    import json, re
    d = json.load(open(path, encoding='utf-8'))
    issues = []
    LIMITS = {'xOffset': 0.1, 'yOffset': 0.1, 'zOffset': 0.1}
    def walk(o, trail):
        if isinstance(o, dict):
            if 'Parent Bone' in o and any(k in o for k in LIMITS):
                for k, lim in LIMITS.items():
                    try:
                        v = float(o.get(k, 0))
                    except ValueError:
                        continue
                    if abs(v) > lim * 5:
                        issues.append((trail + [o.get('id', '?')], '%s=%s beyond 5x the +-%s slider range (plugin accepts it on load -> object ends up far away)' % (k, o.get(k), lim)))
                pairs.append((trail, o))
            for k, v in o.items():
                if isinstance(v, (dict, list)):
                    walk(v, trail + [k if not isinstance(o.get('id'), str) else o['id']])
        elif isinstance(o, list):
            for v in o:
                walk(v, trail)
    pairs = []
    walk(d, [])
    # CUAClothing atoms are positioned by the plugin (it re-parents them to a bone every LateUpdate and writes any controller drift back into the offsets).
    # A VaM link (linkTo / Hold) on top fights it: the offsets run away on load (e.g. zOffset -5000) and the piece ends up far from the body.
    for at in d.get('atoms', []) if isinstance(d, dict) else []:
        if '.CUAClothing' in str(at.get('id', '')):
            for st in at.get('storables', []):
                if st.get('id') == 'control' and (st.get('linkTo') or st.get('positionState') == 'Hold'):
                    issues.append(([at['id']], 'plugin-driven CUA atom also has a VaM link (%s, %s) - double-driven, offsets drift on load' % (st.get('linkTo'), st.get('positionState'))))
    # L/R mirror check: same plugin group, bones lX / rX, local offset should be (-x, y, z) of the other
    by = {}
    for trail, o in pairs:
        b = o.get('Parent Bone', '')
        m = re.match(r'^([lr])([A-Z]\w*)$', b)
        if m:
            by.setdefault((tuple(trail[:-1]) if trail else (), m.group(2)), {})[m.group(1)] = o
    for key, lr in by.items():
        if 'l' in lr and 'r' in lr:
            l, r = lr['l'], lr['r']
            try:
                lx, ly, lz = (float(l.get(k, 0)) for k in ('xOffset', 'yOffset', 'zOffset'))
                rx, ry, rz = (float(r.get(k, 0)) for k in ('xOffset', 'yOffset', 'zOffset'))
            except ValueError:
                continue
            if abs(abs(lx) - abs(rx)) > 0.003 or abs(ly - ry) > 0.003 or abs(lz - rz) > 0.003:
                issues.append((['%s/%s' % (l.get('id'), r.get('id'))], 'L/R %s offsets not mirrored: L(%.4f,%.4f,%.4f) R(%.4f,%.4f,%.4f)' % (key[1], lx, ly, lz, rx, ry, rz)))
    return issues


def cmd_scenelint(target):
    import glob
    files = [target] if target.lower().endswith('.json') else glob.glob(os.path.join(target, '**', '*.json'), recursive=True)
    bad = 0
    for f in files:
        try:
            iss = scene_lint(f)
        except Exception:
            continue
        for trail, msg in iss:
            bad += 1
            print('%s :: %s :: %s' % (os.path.basename(f), trail[-1] if trail else '', msg))
    print('scenelint: %d file(s), %d issue(s)' % (len(files), bad))
    return bad == 0


def main(argv):
    if len(argv) < 2 or argv[1] in ('-h', '--help', 'help'):
        print(__doc__)
        return 0
    c = argv[1]
    a = argv[2:]
    if c == 'list':
        for n in P:
            print('%-20s %s%s' % (n, P[n][1], '  [logic sim]' if n in HEADLESS else ('  [ui sim]' if n in UI_PLUGINS else '')))
    elif c == 'build':
        ok = all([build(n) for n in names(a[0])])
        return 0 if ok else 1
    elif c == 'test':
        return 0 if cmd_test(a[0]) else 1
    elif c == 'ship':
        cmd_ship(a[0])
    elif c == 'deploy':
        cmd_deploy(a[0], '--force' in a)
    elif c == 'simbuild':
        cmd_simbuild()
    elif c == 'unpack':
        cmd_unpack(a[0], a[1] if len(a) > 1 else None)
    elif c == 'pack':
        cmd_pack(a[0], a[1])
    elif c == 'cslcheck':
        return 0 if cmd_cslcheck(a[0]) else 1
    elif c == 'cslbuild':
        return 0 if cmd_cslcheck(a[0], a[1]) else 1
    elif c == 'simall':
        return 0 if cmd_simall() else 1
    elif c == 'scenescripts':
        return 0 if cmd_scenescripts(a[0]) else 1
    elif c == 'linkcheck':
        return run([VAMSIM, '--linkcheck', a[0]], None, False).returncode
    elif c == 'scene':
        return run([VAMSIM, '--scene', a[0]] + a[1:], None, False).returncode
    elif c == 'scenelint':
        return 0 if cmd_scenelint(a[0]) else 1
    elif c == 'sheet':
        cmd_sheet(a[0] if a else None)
    elif c == 'simtargets':
        return 0 if cmd_simtargets() else 1
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
