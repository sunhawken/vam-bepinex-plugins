import sys, re, glob, os, subprocess

BS = chr(92)
d = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
cs = glob.glob(os.path.join(d, '*.csproj'))[0]
t = open(cs, encoding='utf-8').read()
core = {'BepInEx', 'Assembly-CSharp', 'UnityEngine', 'UnityEngine.CoreModule', 'UnityEngine.PhysicsModule',
        'System.Core', 'System', 'mscorlib', '0Harmony', '0Harmony20'}


def repl(m):
    n = m.group(1)
    if n in core:
        return m.group(0)
    return ('<Reference Include="%s">\n      <HintPath>$(VaMManagedDir)%s%s.dll</HintPath>\n'
            '      <Private>false</Private>\n    </Reference>') % (n, BS, n)


t = re.sub(r'<Reference Include="([^"]+)" />', repl, t)
t = re.sub(r'\s*<LangVersion>[^<]*</LangVersion>', '', t)
if 'RlChrome' not in t:
    inc = ('  <ItemGroup>\n    <Compile Include="..%sUiKit%sRlChrome.cs" Link="UiKit%sRlChrome.cs" />\n'
           '  </ItemGroup>\n</Project>') % (BS, BS, BS)
    t = t.replace('</Project>', inc)
open(cs, 'w', encoding='utf-8').write(t)
for p in glob.glob(os.path.join(d, '**', '*.cs'), recursive=True):
    subprocess.call([sys.executable, os.path.join(here, 'fix_decompiled.py'), p])
