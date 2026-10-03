"""Restyle the decompiled SmartWardrobeHost uGUI window to the RlUguiWindow chrome (VpbRandomLook look).

usage: restyle_sw.py <SmartWardrobeHost.cs>
"""
import re
import sys

sys.path.insert(0, __file__.rsplit('\\', 1)[0] if '\\' in __file__ else __file__.rsplit('/', 1)[0])
from pysurgery import del_method, rep

p = sys.argv[1]
t = open(p, encoding='utf-8').read()

t = t.replace('SmartWardrobeHost', 'SmartWardrobeSkinnedHost')
t = re.sub(r'\tprivate ConfigEntry<(?:float|bool)> cfg(?:X|Y|Width|Height|BarWidth|BarHeight|Scale|Collapsed);\n\n', '', t)
t = re.sub(r'\tprivate (?:float|int|bool) (?:xFraction|yFraction|width|height|barWidth|barHeight|userScale|dpiScale|screenWidth|screenHeight|collapsed);\n\n', '', t)
t = re.sub(r'\tprivate (?:RectTransform header|Text title|Button collapseButton|Button scaleDown|Button scaleUp);\n\n', '', t)
t = rep(t, '\tprivate Canvas canvas;', '\tprivate ZeroT.UiKit.RlUguiWindow win;\n\n\tprivate Canvas canvas;')
t = re.sub(r'\t\t(?:cfgX|cfgY|cfgWidth|cfgHeight|cfgBarWidth|cfgBarHeight|cfgScale|cfgCollapsed) = [^\n]*\n', '', t)
t = re.sub(r'\t\t(?:xFraction|yFraction|width|height|barWidth|barHeight|userScale|collapsed) = [^\n]*\n', '', t)
t = rep(t, '\tprivate void Awake()\n\t{\n', '\tprivate void Awake()\n\t{\n\t\twin = new ZeroT.UiKit.RlUguiWindow(this, "SmartWardrobe", "Window", 40f, 60f, 650f, 720f, 400f, 280f, 1600f, 1400f);\n')

# Update(): keep the periodic native-layout refresh, drop the geometry/DPI watcher
a = t.index('\tprivate void Update()\n')
b = t.index('\tprivate void OnDisable()')
t = t[:a] + '''	private void Update()
	{
		if (win == null || (Object)(object)window == (Object)null)
		{
			return;
		}
		win.Tick();
		if (((Object)(object)wardrobe != (Object)null || (Object)(object)patcher != (Object)null) && Time.unscaledTime >= layoutNext)
		{
			layoutNext = Time.unscaledTime + 0.5f;
			UpdateNativeLayout();
		}
	}

''' + t[b:]
t = rep(t, '((Component)canvas).gameObject.SetActive(false);', 'win.SetActive(false);')
t = rep(t, '((Component)canvas).gameObject.SetActive(true);', 'win.SetActive(true);')
t = rep(t, 'Object.Destroy((Object)(object)((Component)canvas).gameObject);', 'win.Dispose();')

for name in ['GetDpiRatio', 'EffectiveScale', 'MakeGrip', 'PositionGrips', 'Drag', 'SaveGeometry', 'ChangeScale', 'ToggleCollapse', 'AddPointer', 'ApplyGeometry']:
    t = del_method(t, r'(?:private|internal) (?:static )?\w+ ' + name + r'\(')
t = re.sub(r'\t\[DllImport\("user32.dll"\)\]\n\tprivate static extern \w+ \w+\([^\n]*\);\n\n', '', t)

# BuildWindow chrome
a = t.index('\t\tGameObject val = new GameObject("SmartWardrobe Standalone Desktop"')
b = t.index('\t\tRectTransform val4 = NewRect("Toolbar"')
t = t[:a] + '''		win.OnLayout = delegate(float w, float h)
		{
			UpdateNativeLayout();
		};
		win.OnHidden = HideMenu;
		win.OnClosed = HideMenu;
		win.Build("SmartWardrobe Standalone Desktop");
		canvas = win.Canvas;
		window = win.Window;
		Vector2 val3 = new Vector2(0f, 1f);
''' + t[b:]
t = rep(t, 'RectTransform val4 = NewRect("Toolbar", (Transform)(object)window);', 'RectTransform val4 = NewRect("Toolbar", (Transform)(object)win.Body);')
t = rep(t, 'nativeArea = ((Component)NewRect("Native controls", (Transform)(object)window)).gameObject;', 'nativeArea = ((Component)NewRect("Native controls", (Transform)(object)win.Body)).gameObject;')
t = rep(t, '\t\tMakeGrip("Resize width", ResizeAxis.X);\n\t\tMakeGrip("Resize height", ResizeAxis.Y);\n\t\tMakeGrip("Resize both", ResizeAxis.XY);\n', '\t\twin.AddResizeHandle();\n')
t = rep(t, '\tprivate void UpdateNativeLayout()', '''	private void ApplyGeometry()
	{
		if (win != null)
		{
			if (win.TitleLabel != null)
			{
				win.TitleLabel.text = ((Object)(object)selectedPerson != (Object)null) ? ("SmartWardrobe: " + selectedPerson.uid) : "SmartWardrobe";
			}
			win.Apply();
		}
	}

	private void UpdateNativeLayout()''')
t = rep(t, 'title.text = "SmartWardrobe: " + person.uid;', 'if (win.TitleLabel != null)\n\t\t\t{\n\t\t\t\twin.TitleLabel.text = "SmartWardrobe: " + person.uid;\n\t\t\t}')

# colours / widgets
t = rep(t, 'new Color(0.075f, 0.09f, 0.13f, 1f)', 'ZeroT.UiKit.RlUguiWindow.ViewCol')
t = rep(t, 'new Color(0.12f, 0.17f, 0.23f, 1f)', 'ZeroT.UiKit.RlUguiWindow.PanelCol')
t = rep(t, 'new Color(0.1f, 0.13f, 0.18f, 1f)', 'ZeroT.UiKit.RlUguiWindow.ViewCol')
t = rep(t, '''		((Graphic)val2).color = new Color(0.22f, 0.3f, 0.4f, 1f);
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
print('ok', p)
