"""Restyle a decompiled GUI.Window-based plugin to the VpbRandomLook chrome (see RlChrome.cs).

usage: restyle_imgui.py <plugin.cs> <collapsedLiteral> [title override]

Every step asserts what it found so a plugin that deviates from the template fails loudly and gets a hand edit.
"""
import re
import sys

path = sys.argv[1]
literal = sys.argv[2]
title_override = sys.argv[3] if len(sys.argv) > 3 else None
t = open(path, encoding='utf-8').read()
notes = []


def need(m, what):
    if not m:
        raise SystemExit('FAIL: ' + what)
    return m


# ---- names from the template
coll = need(re.search(r'private ConfigEntry<bool> (_\w+);\s*\n\s*private ConfigEntry<bool> _autoDpi;', t) or
            re.search(r'private ConfigEntry<bool> (_collapsed);', t), 'collapsed field').group(1)
dpi_cfg = need(re.search(r'\(\(!(_\w+)\.Value \|\| !\(Screen\.dpi > 0f\)\)', t), 'dpi cfg').group(1)
scale_cfg = need(re.search(r'Mathf\.Clamp\((_\w+)\.Value, 0\.65f, 2\.5f\)', t), 'scale cfg').group(1)
eff = need(re.search(r'GUI\.matrix = Matrix4x4\.Scale\(new Vector3\((_\w+), ', t), 'effective scale').group(1)
mark = need(re.search(r'private void (Mark\w+)\(\)', t), 'mark dirty').group(1)
win = need(re.search(r'GUI\.Window\(\d+, (_\w+),', t), 'window field').group(1)
draw = need(re.search(r'GUI\.Window\(\d+, _\w+, \(GUI\.WindowFunction\)(\w+),', t), 'draw fn').group(1)
dw = re.search(r'private void ' + draw + r'\(int id\)\n\t\{\n(.*?)\n\t\}\n', t, re.S)
need(dw, 'DrawWindow body')
body = dw.group(1)
expanded = need(re.search(r'if \(!' + coll + r'\.Value\)\n\t\t\{\n\t\t\t(\w+)\(\);', body), 'expanded fn').group(1)
title = title_override or need(re.search(r'GUILayout\.Label\("([^"]+)"', body), 'title').group(1)
rs = need(re.search(r'(_\w+) = true;\n\s*(_\w+) = LogicalMouse\(\);\n\s*(_\w+) = new Vector2\(' + win + r'\.width, ' + win + r'\.height\);', body), 'resize vars')
resizing, rmouse, rsize = rs.group(1), rs.group(2), rs.group(3)
toggle = need(re.search(r'(ToggleCollapse\w*)\(\);', body), 'toggle').group(1)
print('names:', coll, dpi_cfg, scale_cfg, eff, mark, win, draw, expanded, repr(title), resizing, rmouse, rsize, toggle)

# ---- Show config entry
show_decl = 'private ConfigEntry<bool> _rlShow;\n\n\tprivate ConfigEntry<bool> ' + coll + ';'
t = t.replace('private ConfigEntry<bool> ' + coll + ';', show_decl, 1)
m = need(re.search(r'\t\t' + coll + r' = \(\(BaseUnityPlugin\)this\)\.Config\.Bind<bool>\("Window", "Collapsed"', t) or
         re.search(r'\t\t' + coll + r' = Config\.Bind<bool>\("Window", "Collapsed"', t), 'collapsed bind')
bind_prefix = 'Config' if 'Config.Bind<bool>("Window", "Collapsed"' in m.group(0) and '((BaseUnityPlugin)this).' not in m.group(0) else '((BaseUnityPlugin)this).Config'
ins = '\t\t_rlShow = %s.Bind<bool>("Window", "Show", true, "Show the in-game window.");\n' % bind_prefix
t = t[:m.start()] + ins + t[m.start():]

# ---- DPI
t = t.replace('((!%s.Value || !(Screen.dpi > 0f)) ? 1f : Mathf.Clamp(Screen.dpi / 96f, 0.85f, 2.5f))' % dpi_cfg,
              '((!%s.Value) ? 1f : ZeroT.UiKit.RlChrome.Dpi(%s.x * %s, %s.y * %s))' % (dpi_cfg, win, eff, win, eff))

# ---- OnGUI show check + style-less window
t, n = re.subn(r'(private void OnGUI\(\)\n\t\{\n)', r'\1\t\tif (!_rlShow.Value)\n\t\t{\n\t\t\treturn;\n\t\t}\n', t, count=1)
need(n, 'OnGUI')
t, n = re.subn(r'(GUI\.Window\(\d+, ' + win + r', \(GUI\.WindowFunction\)' + draw + r', "")(?:, GUI\.skin\.window)?\)', r'\1, GUIStyle.none)', t)
need(n, 'GUI.Window call')

# ---- EnsureStyles: default skin colors / weights, no background texture
es = need(re.search(r'private void EnsureStyles\(\)\n\t\{\n(.*?)\n\t\}\n', t, re.S), 'EnsureStyles')
eb = es.group(1)
lines = []
for ln in eb.split('\n'):
    s = ln.strip()
    if re.match(r'_\w+\.normal\.textColor = ', s) or re.match(r'_\w+\.(fontSize|fontStyle) = ', s):
        continue
    if re.search(r'new Texture2D\(', s) or re.search(r'\.SetPixel\(', s) or re.match(r'_\w+\.Apply\(\);', s):
        continue
    lines.append(ln)
t = t.replace(eb, '\n'.join(lines), 1)

# ---- DrawWindow
new_draw = '''ZeroT.UiKit.RlChrome.Backdrop(%(w)s.width, %(w)s.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(%(w)s.width, "%(title)s", %(c)s.Value);
		if ((rlButtons & 1) != 0)
		{
			RlSetScale(%(s)s.Value - 0.1f);
		}
		if ((rlButtons & 2) != 0)
		{
			RlSetScale(%(s)s.Value + 0.1f);
		}
		if ((rlButtons & 4) != 0)
		{
			%(toggle)s();
		}
		if ((rlButtons & 8) != 0)
		{
			_rlShow.Value = false;
			Config.Save();
		}
		if (!%(c)s.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(%(w)s.width, %(w)s.height));
			%(expanded)s();
			GUILayout.EndArea();
			if (ZeroT.UiKit.RlChrome.ResizeHandle(%(w)s.width, %(w)s.height))
			{
				%(resizing)s = true;
				%(rmouse)s = LogicalMouse();
				%(rsize)s = new Vector2(%(w)s.width, %(w)s.height);
			}
		}
		if (!%(resizing)s)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, %(w)s.width - 118f), 26f));
		}
	}

	private void RlSetScale(float value)
	{
		%(s)s.Value = Mathf.Clamp(value, 0.65f, 2.5f);
		ZeroT.UiKit.RlChrome.ResetDpi();
		%(mark)s();''' % dict(w=win, title=title, c=coll, s=scale_cfg, toggle=toggle, expanded=expanded,
                             resizing=resizing, rmouse=rmouse, rsize=rsize, mark=mark)
t = t.replace(body, new_draw, 1)

# ---- collapsed height
t = re.sub(r'(?<![0-9.])' + re.escape(literal), '34f', t)

open(path, 'w', encoding='utf-8').write(t)
print('ok', path)
