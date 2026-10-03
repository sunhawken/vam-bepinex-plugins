import re
import sys
sys.path.insert(0, '../UiKit')
from pysurgery import del_method, rep

p = 'ZeroT.XRaySlapStandalone/XRaySlapStandalone.cs'
t = open(p, encoding='utf-8').read()

t = re.sub(r'\tprivate ConfigEntry<(?:float|bool)> saved(?:X|Y|Width|Height|Scale|Collapsed);\n\n', '', t)
t = re.sub(r'\tprivate (?:float|int|bool) (?:xFraction|yFraction|width|height|userScale|dpiScale|collapsed|screenWidth|screenHeight);\n\n', '', t)
t = rep(t, '\tprivate Button collapseButton;\n\n', '')
t = rep(t, '\tprivate Canvas canvas;', '\tprivate ZeroT.UiKit.RlUguiWindow win;\n\n\tprivate Canvas canvas;')
t = re.sub(r'\t\tsaved(?:X|Y|Width|Height|Scale|Collapsed) = [^\n]*\n', '', t)
t = re.sub(r'\t\t(?:xFraction|yFraction|width|height|userScale|collapsed) = [^\n]*\n', '', t)
t = rep(t, '\tprivate void Awake()\n\t{\n', '\tprivate void Awake()\n\t{\n\t\twin = new ZeroT.UiKit.RlUguiWindow(this, "XRay + Slap Standalone", "Window", 40f, 80f, 475f, 420f, 390f, 330f, 1000f, 1000f);\n')

# Update(): replace the window/DPI block
a = t.index('\t\tif ((Object)(object)window != (Object)null)\n\t\t{\n\t\t\tfloat num = DpiRatio();')
b = t.index('\t\tif ((Object)(object)overlayCamera != (Object)null && (Object)(object)linkedCamera != (Object)null)', a)
t = t[:a] + '\t\tif (win != null)\n\t\t{\n\t\t\twin.Tick();\n\t\t}\n' + t[b:]

t = rep(t, '\t\tif ((Object)(object)canvas != (Object)null)\n\t\t{\n\t\t\tObject.Destroy((Object)(object)((Component)canvas).gameObject);\n\t\t}\n', '\t\tif (win != null)\n\t\t{\n\t\t\twin.Dispose();\n\t\t}\n')
t = rep(t, '\t\tif ((Object)(object)canvas != (Object)null)\n\t\t{\n\t\t\t((Component)canvas).gameObject.SetActive(false);\n\t\t}\n', '\t\tif (win != null)\n\t\t{\n\t\t\twin.SetActive(false);\n\t\t}\n')
t = rep(t, '\t\tif ((Object)(object)canvas != (Object)null)\n\t\t{\n\t\t\t((Component)canvas).gameObject.SetActive(true);\n\t\t}\n', '\t\tif (win != null)\n\t\t{\n\t\t\twin.SetActive(true);\n\t\t}\n')

for name in ['DpiRatio', 'EffectiveScale', 'ApplyGeometry', 'PointerDrag', 'PersistWindow', 'ChangeScale', 'ToggleCollapse']:
    t = del_method(t, r'(?:private|internal) (?:static )?\w+ ' + name + r'\(')
t = re.sub(r'\t\[DllImport\("user32.dll"\)\]\n\tprivate static extern \w+ \w+\([^\n]*\);\n\n', '', t)

# BuildWindow chrome -> kit
a = t.index('\t\tGameObject val = new GameObject("ZeroT XRay Slap Standalone UI"')
b = t.index('\t\txrayButton = NewButton("XRay Toggle"')
t = t[:a] + '''		win.OnLayout = delegate(float w, float h)
		{
			RefreshLabels();
		};
		win.Build("ZeroT XRay Slap Standalone UI");
		canvas = win.Canvas;
		canvas.sortingOrder = 30010;
		window = win.Body;
''' + t[b:]
# resize button -> kit arrow
a = t.index('\t\tRectTransform parent = window;\n\t\tUnityAction val12')
b = t.index('\t\tApplyGeometry();\n\t\tRefreshLabels();\n\t}', a)
t = t[:a] + '\t\twin.AddResizeHandle();\n' + t[b:]
t = rep(t, '\t\t\tif ((Object)(object)collapseButton != (Object)null)\n\t\t\t{\n\t\t\t\tSetLabel(collapseButton, collapsed ? "+" : "\\u2212");\n\t\t\t}\n', '') if False else t
t = re.sub(r'\t\t\tif \(\(Object\)\(object\)collapseButton != \(Object\)null\)\n\t\t\t\{\n\t\t\t\tSetLabel\(collapseButton, [^\n]*\n\t\t\t\}\n', '', t)
t = rep(t, '\tprivate void MakeStepper(', '''	private void ApplyGeometry()
	{
		if (win != null)
		{
			win.Apply();
		}
	}

	private void MakeStepper(''')

# look
t = rep(t, '((Graphic)val).color = Color.white;\n\t\tval.text = value;', '((Graphic)val).color = ZeroT.UiKit.RlUguiWindow.TextCol;\n\t\tval.text = value;')
t = rep(t, '''		((Graphic)val2).color = new Color(0.21f, 0.29f, 0.39f, 1f);
		Button val3 = ((Component)val).gameObject.AddComponent<Button>();
		((Selectable)val3).targetGraphic = (Graphic)(object)val2;''', '''		((Graphic)val2).color = Color.white;
		Button val3 = ((Component)val).gameObject.AddComponent<Button>();
		((Selectable)val3).targetGraphic = (Graphic)(object)val2;
		ColorBlock rlColors = val3.colors;
		rlColors.normalColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		rlColors.highlightedColor = ZeroT.UiKit.RlUguiWindow.ButtonH;
		rlColors.pressedColor = ZeroT.UiKit.RlUguiWindow.ButtonP;
		rlColors.disabledColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		val3.colors = rlColors;
		Navigation rlNav = new Navigation();
		rlNav.mode = Navigation.Mode.None;
		val3.navigation = rlNav;''')
open(p, 'w', encoding='utf-8').write(t)
print('ok')
