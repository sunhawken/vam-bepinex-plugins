using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using MVR.FileManagement;
using UnityEngine;

namespace ZeroT.FluidBridgeHelper;

[BepInPlugin("zerot.fluidbridge.helper", "FluidBridge v27 Helper", "1.1.0")]
public sealed class FluidBridgeHelperPlugin : BaseUnityPlugin
{
	private const string DefaultScript = "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist";

	private const string LooseScript = "Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist";

	private const int WindowId = 270411;

	private const float CollapsedHeight = 34f;

	private const float MinWidth = 340f;

	private const float MinHeight = 315f;

	private ConfigEntry<bool> _rlShow;

	private ConfigEntry<bool> _rlActive;

	private ConfigEntry<bool> _collapsed;

	private ConfigEntry<bool> _autoDpi;

	private ConfigEntry<float> _uiScale;

	private ConfigEntry<float> _windowX;

	private ConfigEntry<float> _windowY;

	private ConfigEntry<float> _windowW;

	private ConfigEntry<float> _windowH;

	private ConfigEntry<string> _scriptPath;

	private ConfigEntry<bool> _assistLoaded;

	private readonly List<Atom> _people = new List<Atom>();

	private readonly Dictionary<string, MVRScript> _bridges = new Dictionary<string, MVRScript>(StringComparer.Ordinal);

	private readonly Dictionary<string, float> _pending = new Dictionary<string, float>(StringComparer.Ordinal);

	private readonly Dictionary<string, float> _assistAfter = new Dictionary<string, float>(StringComparer.Ordinal);

	private string _dependencySignature = "";

	private string _lastAssist = "Waiting for a loaded FluidBridge.";

	private bool _hasDependencySignature;

	private Rect _window;

	private Vector2 _scroll;

	private Vector2 _resizeStartMouse;

	private Vector2 _resizeStartSize;

	private bool _resizing;

	private bool _layoutDirty;

	private float _nextScan;

	private float _saveAfter;

	private float _effectiveScale;

	private string _selectedUid = "";

	private string _message = "Choose a Person to manage FluidBridge.";

	private GUIStyle _titleStyle;

	private GUIStyle _statusStyle;

	private GUIStyle _smallStyle;

	private Texture2D _windowBackground;

	private void Awake()
	{
		_rlShow = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Show", true, "Show the in-game window.");
		_rlActive = Config.Bind<bool>("General", "Active", true, "Turn the helper off completely (no scanning or VAR assistance).");
		_collapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Collapsed", false, "Remember the collapsed state.");
		_autoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "AutoDPI", true, "Scale the overlay using Screen.dpi.");
		_uiScale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "UIScale", 1f, "Additional UI scale multiplier (0.65 to 2.5).");
		_windowX = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "X", 50f, "Logical X position.");
		_windowY = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Y", 85f, "Logical Y position.");
		_windowW = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Width", 450f, "Logical expanded width.");
		_windowH = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Height", 570f, "Logical expanded height.");
		_scriptPath = ((BaseUnityPlugin)this).Config.Bind<string>("FluidBridge", "ScriptPath", "ZeroT.FluidBridge.27:/Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist", "VAR path to the original FluidBridge v27 cslist. Used by Add to Person.");
		_assistLoaded = ((BaseUnityPlugin)this).Config.Bind<bool>("FluidBridge", "AssistLoadedVAR", true, "Rescan an existing FluidBridge v27 script when it appears or its BodyLanguage/fluid dependencies change.");
		_window = new Rect(_windowX.Value, _windowY.Value, Mathf.Max(340f, _windowW.Value), (!_collapsed.Value) ? Mathf.Max(315f, _windowH.Value) : 34f);
		ScanPeople();
	}

	private void Update()
	{
		if (!_rlActive.Value)
		{
			return;
		}
		if (Time.realtimeSinceStartup >= _nextScan)
		{
			_nextScan = Time.realtimeSinceStartup + 3f;
			ScanPeople();
		}
		ProcessAssistance();
		if (_resizing)
		{
			if (Input.GetMouseButton(0))
			{
				Vector2 val = LogicalMouse();
				float num = Mathf.Max(340f, (float)Screen.width / Mathf.Max(0.01f, _effectiveScale) - _window.x);
				float num2 = Mathf.Max(315f, (float)Screen.height / Mathf.Max(0.01f, _effectiveScale) - _window.y);
				_window.width = Mathf.Clamp(_resizeStartSize.x + val.x - _resizeStartMouse.x, 340f, num);
				_window.height = Mathf.Clamp(_resizeStartSize.y + val.y - _resizeStartMouse.y, 315f, num2);
				MarkLayoutDirty();
			}
			else
			{
				_resizing = false;
			}
		}
		if (_layoutDirty && !_resizing && !Input.GetMouseButton(0) && Time.realtimeSinceStartup >= _saveAfter)
		{
			SaveLayout();
		}
	}

	private Vector2 LogicalMouse()
	{
		float num = Mathf.Max(0.01f, _effectiveScale);
		return new Vector2(Input.mousePosition.x / num, ((float)Screen.height - Input.mousePosition.y) / num);
	}

	private float GetScale()
	{
		float num = ((!_autoDpi.Value) ? 1f : ZeroT.UiKit.RlChrome.Dpi(_window.x * _effectiveScale, _window.y * _effectiveScale));
		return num * Mathf.Clamp(_uiScale.Value, 0.65f, 2.5f);
	}

	private void OnGUI()
	{
		if (!_rlShow.Value)
		{
			return;
		}
		_effectiveScale = GetScale();
		EnsureStyles();
		ClampWindow();
		Matrix4x4 matrix = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(_effectiveScale, _effectiveScale, 1f)) * matrix;
		Rect window = _window;
		try
		{
			_window = GUI.Window(270411, _window, (GUI.WindowFunction)DrawWindow, "", GUIStyle.none);
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("FluidBridge helper GUI: " + ex.Message));
		}
		finally
		{
			GUI.matrix = matrix;
		}
		if (Mathf.Abs(window.x - _window.x) > 0.01f || Mathf.Abs(window.y - _window.y) > 0.01f || Mathf.Abs(window.width - _window.width) > 0.01f || Mathf.Abs(window.height - _window.height) > 0.01f)
		{
			MarkLayoutDirty();
		}
	}

	private void EnsureStyles()
	{
		if (_titleStyle == null)
		{
			_titleStyle = new GUIStyle(GUI.skin.label);
			_statusStyle = new GUIStyle(GUI.skin.label);
			_statusStyle.richText = true;
			_statusStyle.wordWrap = true;
			_smallStyle = new GUIStyle(GUI.skin.label);
			_smallStyle.wordWrap = true;
		}
	}

	private void DrawWindow(int id)
	{
ZeroT.UiKit.RlChrome.Backdrop(_window.width, _window.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(_window.width, "FluidBridge Helper", _collapsed.Value, _rlActive.Value);
		if ((rlButtons & 1) != 0)
		{
			RlSetScale(_uiScale.Value - 0.1f);
		}
		if ((rlButtons & 2) != 0)
		{
			RlSetScale(_uiScale.Value + 0.1f);
		}
		if ((rlButtons & 16) != 0)
		{
			_rlActive.Value = !_rlActive.Value;
			if (_rlActive.Value)
			{
				_nextScan = 0f;
			}
			Config.Save();
		}
		if ((rlButtons & 4) != 0)
		{
			ToggleCollapse();
		}
		if ((rlButtons & 8) != 0)
		{
			_rlShow.Value = false;
			Config.Save();
		}
		if (!_collapsed.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(_window.width, _window.height));
			DrawExpanded();
			GUILayout.EndArea();
			if (ZeroT.UiKit.RlChrome.ResizeHandle(_window.width, _window.height))
			{
				_resizing = true;
				_resizeStartMouse = LogicalMouse();
				_resizeStartSize = new Vector2(_window.width, _window.height);
			}
		}
		if (!_resizing)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, _window.width - 118f), 26f));
		}
	}

	private void RlSetScale(float value)
	{
		_uiScale.Value = Mathf.Clamp(value, 0.65f, 2.5f);
		ZeroT.UiKit.RlChrome.ResetDpi();
		MarkLayoutDirty();
	}

	private void DrawExpanded()
	{
		GUILayout.Space(4f);
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Use selected VaM Person", new GUILayoutOption[1] { GUILayout.Height(26f) }))
		{
			SelectCurrentAtom();
		}
		if (GUILayout.Button("Refresh", new GUILayoutOption[2]
		{
			GUILayout.Width(72f),
			GUILayout.Height(26f)
		}))
		{
			ScanPeople();
		}
		GUILayout.EndHorizontal();
		Atom val = SelectedPerson();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("◀", new GUILayoutOption[1] { GUILayout.Width(30f) }))
		{
			ShiftSelection(-1);
		}
		GUILayout.Label((!((Object)(object)val == (Object)null)) ? val.uid : "No Person found", _titleStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		if (GUILayout.Button("▶", new GUILayoutOption[1] { GUILayout.Width(30f) }))
		{
			ShiftSelection(1);
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		bool flag = GUILayout.Toggle(_assistLoaded.Value, "Assist loaded VAR", new GUILayoutOption[1] { GUILayout.Width(143f) });
		if (flag != _assistLoaded.Value)
		{
			_assistLoaded.Value = flag;
			if (flag)
			{
				_hasDependencySignature = false;
				foreach (string key in _bridges.Keys)
				{
					ScheduleAssistance(key, Time.realtimeSinceStartup + 1.5f);
				}
			}
			else
			{
				_assistAfter.Clear();
			}
			((BaseUnityPlugin)this).Config.Save();
		}
		GUILayout.Label(_bridges.Count + " loaded instance(s)", _smallStyle, new GUILayoutOption[0]);
		GUILayout.EndHorizontal();
		if ((Object)(object)val == (Object)null)
		{
			GUILayout.Label("Add a Person atom to the scene, then refresh.", _smallStyle, new GUILayoutOption[0]);
		}
		else
		{
			MVRScript val2 = FindBridge(val);
			if ((Object)(object)val2 == (Object)null)
			{
				GUILayout.Label("FluidBridge is not loaded on this Person.", _smallStyle, new GUILayoutOption[0]);
				if (GUILayout.Button("Add FluidBridge v27 to this Person", new GUILayoutOption[1] { GUILayout.Height(30f) }))
				{
					AddToPerson(val);
				}
			}
			else
			{
				GUILayout.Label("Connected: " + ((JSONStorable)val2).storeId, _smallStyle, new GUILayoutOption[0]);
				DrawToggle(val2, "Enabled", "Enabled");
				GUILayout.BeginHorizontal(new GUILayoutOption[0]);
				DrawToggle(val2, "Use SexyFluids", "SexyFluids");
				DrawToggle(val2, "Use Fluid Dynamics", "Fluid Dynamics");
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal(new GUILayoutOption[0]);
				DrawToggle(val2, "Apply Wet Clothes preset on orgasm", "Wet Clothes");
				DrawToggle(val2, "Random clothing on orgasm", "Orgasm Wardrobe");
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal(new GUILayoutOption[0]);
				DrawAction(val2, "Scan / Diagnostics", "Scan");
				DrawAction(val2, "Test Orgasm Fluids", "Test Fluids");
				DrawAction(val2, "Stop Fluids", "Stop Fluids");
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal(new GUILayoutOption[0]);
				DrawAction(val2, "Save FluidBridge Settings", "Save Settings");
				DrawAction(val2, "Load FluidBridge Settings", "Load Settings");
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.Space(5f);
		GUILayout.Label(_message, _smallStyle, new GUILayoutOption[0]);
		GUILayout.Label(_lastAssist, _smallStyle, new GUILayoutOption[0]);
		GUILayout.Label("FluidBridge status", _titleStyle, new GUILayoutOption[0]);
		_scroll = GUILayout.BeginScrollView(_scroll, new GUILayoutOption[1] { GUILayout.ExpandHeight(true) });
		string text = ((!((Object)(object)val == (Object)null)) ? GetStatus(FindBridge(val)) : "");
		GUILayout.Label((!string.IsNullOrEmpty(text)) ? text : "No status yet. Attach FluidBridge or press Scan.", _statusStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		GUILayout.EndScrollView();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		bool flag2 = GUILayout.Toggle(_autoDpi.Value, "Auto DPI", new GUILayoutOption[1] { GUILayout.Width(86f) });
		if (flag2 != _autoDpi.Value)
		{
			_autoDpi.Value = flag2;
			((BaseUnityPlugin)this).Config.Save();
		}
		GUILayout.Label("Scale", new GUILayoutOption[1] { GUILayout.Width(48f) });
		float num = GUILayout.HorizontalSlider(_uiScale.Value, 0.65f, 2.5f, new GUILayoutOption[0]);
		if (Mathf.Abs(num - _uiScale.Value) > 0.01f)
		{
			_uiScale.Value = num;
			MarkLayoutDirty();
		}
		GUILayout.Label(_uiScale.Value.ToString("0.00") + "×", new GUILayoutOption[1] { GUILayout.Width(45f) });
		GUILayout.EndHorizontal();
	}

	private void DrawToggle(MVRScript script, string name, string label)
	{
		JSONStorableBool boolJSONParam = ((JSONStorable)script).GetBoolJSONParam(name);
		if (boolJSONParam == null)
		{
			GUILayout.Label(label + ": unavailable", _smallStyle, new GUILayoutOption[0]);
			return;
		}
		bool flag = GUILayout.Toggle(boolJSONParam.val, label, new GUILayoutOption[0]);
		if (flag == boolJSONParam.val)
		{
			return;
		}
		try
		{
			if (!flag)
			{
				switch (name)
				{
				case "Enabled":
				case "Use SexyFluids":
				case "Use Fluid Dynamics":
					if (((JSONStorable)script).GetAction("Stop Fluids") != null)
					{
						((JSONStorable)script).CallAction("Stop Fluids");
					}
					break;
				}
			}
			boolJSONParam.val = flag;
			_message = label + ((!flag) ? " disabled." : " enabled.");
		}
		catch (Exception e)
		{
			Report("Could not change " + label, e);
		}
	}

	private void DrawAction(MVRScript script, string name, string label)
	{
		JSONStorableAction action = ((JSONStorable)script).GetAction(name);
		GUI.enabled = action != null;
		bool flag = GUILayout.Button(label, new GUILayoutOption[1] { GUILayout.Height(25f) });
		GUI.enabled = true;
		if (flag && action != null)
		{
			try
			{
				((JSONStorable)script).CallAction(name);
				_message = label + " sent to " + ((JSONStorable)script).containingAtom.uid + ".";
			}
			catch (Exception e)
			{
				Report("Could not run " + label, e);
			}
		}
	}

	private string GetStatus(MVRScript script)
	{
		if ((Object)(object)script == (Object)null)
		{
			return "";
		}
		try
		{
			JSONStorableString val = ((JSONStorable)script).GetStringJSONParam("Status");
			if (val == null)
			{
				FieldInfo field = ((object)script).GetType().GetField("statusJSON", BindingFlags.Instance | BindingFlags.NonPublic);
				if (field != null)
				{
					object value = field.GetValue(script);
					val = (JSONStorableString)((value is JSONStorableString) ? value : null);
				}
			}
			return (val != null) ? val.val : "FluidBridge is present. Press Scan for diagnostics.";
		}
		catch (Exception ex)
		{
			return "Status unavailable: " + ex.Message;
		}
	}

	private void ScanPeople()
	{
		Dictionary<string, MVRScript> dictionary = new Dictionary<string, MVRScript>(_bridges, StringComparer.Ordinal);
		_people.Clear();
		_bridges.Clear();
		try
		{
			SuperController singleton = SuperController.singleton;
			if ((Object)(object)singleton == (Object)null)
			{
				return;
			}
			List<Atom> atoms = singleton.GetAtoms();
			if (atoms == null)
			{
				return;
			}
			List<string> list = ((!_assistLoaded.Value) ? null : new List<string>());
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom val = atoms[i];
				if (list != null)
				{
					CollectDependencyTokens(val, list);
				}
				if (!((Object)(object)val == (Object)null) && !(val.type != "Person") && !string.IsNullOrEmpty(val.uid))
				{
					_people.Add(val);
					MVRScript val2 = FindBridgeOnAtom(val);
					if ((Object)(object)val2 != (Object)null)
					{
						_bridges[val.uid] = val2;
					}
				}
			}
			if ((Object)(object)SelectedPerson() == (Object)null)
			{
				Atom selectedAtom = singleton.GetSelectedAtom();
				if ((Object)(object)selectedAtom != (Object)null && selectedAtom.type == "Person" && _bridges.ContainsKey(selectedAtom.uid))
				{
					_selectedUid = selectedAtom.uid;
				}
				if ((Object)(object)SelectedPerson() == (Object)null)
				{
					for (int j = 0; j < _people.Count; j++)
					{
						if (_bridges.ContainsKey(_people[j].uid))
						{
							_selectedUid = _people[j].uid;
							break;
						}
					}
				}
				if ((Object)(object)SelectedPerson() == (Object)null && (Object)(object)selectedAtom != (Object)null && selectedAtom.type == "Person")
				{
					_selectedUid = selectedAtom.uid;
				}
				if ((Object)(object)SelectedPerson() == (Object)null && _people.Count > 0)
				{
					_selectedUid = _people[0].uid;
				}
			}
			if (list != null)
			{
				float realtimeSinceStartup = Time.realtimeSinceStartup;
				foreach (KeyValuePair<string, MVRScript> bridge in _bridges)
				{
					if (!dictionary.TryGetValue(bridge.Key, out var value) || (Object)(object)value != (Object)(object)bridge.Value)
					{
						ScheduleAssistance(bridge.Key, realtimeSinceStartup + 2.5f);
					}
				}
				list.Sort(StringComparer.Ordinal);
				string text = string.Join("|", list.ToArray());
				if (_hasDependencySignature && text != _dependencySignature)
				{
					foreach (string key in _bridges.Keys)
					{
						ScheduleAssistance(key, realtimeSinceStartup + 1.5f);
					}
				}
				_dependencySignature = text;
				_hasDependencySignature = true;
			}
			else
			{
				_assistAfter.Clear();
				_hasDependencySignature = false;
			}
			List<string> list2 = new List<string>();
			foreach (KeyValuePair<string, float> item in _pending)
			{
				if (_bridges.ContainsKey(item.Key) || Time.realtimeSinceStartup - item.Value > 30f)
				{
					list2.Add(item.Key);
				}
			}
			for (int k = 0; k < list2.Count; k++)
			{
				_pending.Remove(list2[k]);
			}
			list2.Clear();
			foreach (string key2 in _assistAfter.Keys)
			{
				if (!_bridges.ContainsKey(key2))
				{
					list2.Add(key2);
				}
			}
			for (int l = 0; l < list2.Count; l++)
			{
				_assistAfter.Remove(list2[l]);
			}
		}
		catch (Exception e)
		{
			Report("Scan failed", e);
		}
	}

	private static void CollectDependencyTokens(Atom atom, List<string> tokens)
	{
		if ((Object)(object)atom == (Object)null || (Object)(object)((Component)atom).gameObject == (Object)null)
		{
			return;
		}
		try
		{
			MVRScript[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRScript>(true);
			foreach (MVRScript val in componentsInChildren)
			{
				if (!((Object)(object)val == (Object)null) && !((Object)(object)((JSONStorable)val).containingAtom != (Object)(object)atom))
				{
					string text = ((object)val).GetType().Name + " " + ((JSONStorable)val).storeId;
					if (text.IndexOf("BodyLanguage", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("ReadMyLips", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("FuckingReach", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("SexMachine", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("SexyFluids", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Fluid_Dynamics", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("FluidDynamics", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("FluidEmitterSPH", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("SuddenBodyBridge", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						tokens.Add(atom.uid + ":" + ((JSONStorable)val).storeId + ":" + ((Object)val).GetInstanceID());
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ScheduleAssistance(string uid, float when)
	{
		if (!_assistAfter.TryGetValue(uid, out var value) || when > value)
		{
			_assistAfter[uid] = when;
		}
	}

	private void ProcessAssistance()
	{
		if (!_assistLoaded.Value || _assistAfter.Count == 0)
		{
			return;
		}
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, float> item in _assistAfter)
		{
			if (item.Value <= realtimeSinceStartup)
			{
				list.Add(item.Key);
			}
		}
		for (int i = 0; i < list.Count; i++)
		{
			string text = list[i];
			if (!_bridges.TryGetValue(text, out var value) || (Object)(object)value == (Object)null)
			{
				_assistAfter.Remove(text);
				continue;
			}
			try
			{
				JSONStorableBool boolJSONParam = ((JSONStorable)value).GetBoolJSONParam("Enabled");
				if (boolJSONParam == null || !boolJSONParam.val)
				{
					_assistAfter[text] = realtimeSinceStartup + 3f;
					continue;
				}
				if (((JSONStorable)value).GetAction("Scan / Diagnostics") != null)
				{
					((JSONStorable)value).CallAction("Scan / Diagnostics");
					_lastAssist = "Updated loaded VAR on " + text + " at " + DateTime.Now.ToString("HH:mm:ss") + ".";
				}
			}
			catch (Exception e)
			{
				Report("Loaded VAR rescan failed on " + text, e);
			}
			_assistAfter.Remove(text);
		}
	}

	private static MVRScript FindBridgeOnAtom(Atom atom)
	{
		try
		{
			List<string> storableIDs = atom.GetStorableIDs();
			if (storableIDs != null)
			{
				for (int i = 0; i < storableIDs.Count; i++)
				{
					JSONStorable storableByID = atom.GetStorableByID(storableIDs[i]);
					MVRScript val = (MVRScript)(object)((storableByID is MVRScript) ? storableByID : null);
					if (IsFluidBridge(val))
					{
						return val;
					}
				}
			}
			if ((Object)(object)((Component)atom).gameObject != (Object)null)
			{
				MVRScript[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRScript>(true);
				for (int j = 0; j < componentsInChildren.Length; j++)
				{
					if ((Object)(object)componentsInChildren[j] != (Object)null && (Object)(object)((JSONStorable)componentsInChildren[j]).containingAtom == (Object)(object)atom && IsFluidBridge(componentsInChildren[j]))
					{
						return componentsInChildren[j];
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool IsFluidBridge(MVRScript script)
	{
		if ((Object)(object)script == (Object)null || ((object)script).GetType().Name != "FluidBridge")
		{
			return false;
		}
		try
		{
			FieldInfo field = ((object)script).GetType().GetField("Version", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.IsLiteral && !"27".Equals(field.GetRawConstantValue() as string, StringComparison.Ordinal))
			{
				return false;
			}
			return ((JSONStorable)script).GetAction("Scan / Diagnostics") != null && ((JSONStorable)script).GetBoolJSONParam("Use SexyFluids") != null && ((JSONStorable)script).GetBoolJSONParam("Random clothing on orgasm") != null;
		}
		catch
		{
			return false;
		}
	}

	private MVRScript FindBridge(Atom atom)
	{
		if ((Object)(object)atom == (Object)null)
		{
			return null;
		}
		MVRScript value;
		return (!_bridges.TryGetValue(atom.uid, out value) || !((Object)(object)value != (Object)null)) ? null : value;
	}

	private Atom SelectedPerson()
	{
		for (int i = 0; i < _people.Count; i++)
		{
			if ((Object)(object)_people[i] != (Object)null && _people[i].uid == _selectedUid)
			{
				return _people[i];
			}
		}
		return null;
	}

	private void ShiftSelection(int step)
	{
		if (_people.Count != 0)
		{
			int num = _people.FindIndex((Atom a) => (Object)(object)a != (Object)null && a.uid == _selectedUid);
			if (num < 0)
			{
				num = 0;
			}
			_selectedUid = _people[(num + step + _people.Count) % _people.Count].uid;
			_scroll = Vector2.zero;
		}
	}

	private void SelectCurrentAtom()
	{
		try
		{
			Atom val = ((!((Object)(object)SuperController.singleton == (Object)null)) ? SuperController.singleton.GetSelectedAtom() : null);
			if ((Object)(object)val == (Object)null || val.type != "Person")
			{
				_message = "Select a Person atom in VaM first.";
				return;
			}
			_selectedUid = val.uid;
			_scroll = Vector2.zero;
			ScanPeople();
			_message = "Selected " + val.uid + ".";
		}
		catch (Exception e)
		{
			Report("Could not select Person", e);
		}
	}

	private static MVRPluginManager FindManager(Atom atom)
	{
		JSONStorable storableByID = atom.GetStorableByID("plugin");
		MVRPluginManager val = (MVRPluginManager)(object)((storableByID is MVRPluginManager) ? storableByID : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		MVRPluginManager[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRPluginManager>(true);
		return (componentsInChildren.Length <= 0) ? null : componentsInChildren[0];
	}

	private void AddToPerson(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || atom.type != "Person")
		{
			_message = "Select a Person atom first.";
			return;
		}
		if ((Object)(object)FindBridge(atom) != (Object)null || (Object)(object)FindBridgeOnAtom(atom) != (Object)null)
		{
			_message = "FluidBridge is already loaded on " + atom.uid + ".";
			return;
		}
		if (_pending.TryGetValue(atom.uid, out var value) && Time.realtimeSinceStartup - value < 30f)
		{
			_message = "FluidBridge is still loading on " + atom.uid + ".";
			return;
		}
		string text = _scriptPath.Value;
		MVRPluginManager val = null;
		MVRPlugin val2 = null;
		try
		{
			if (!FileManager.FileExists(text, false, false) && FileManager.FileExists("Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist", false, false))
			{
				text = "Custom/Scripts/ZeroT/FluidBridge/FluidBridge.cslist";
			}
			if (!FileManager.FileExists(text, false, false))
			{
				_message = "FluidBridge v27 VAR is missing. Install ZeroT.FluidBridge.27.var in AddonPackages.";
				return;
			}
			val = FindManager(atom);
			if ((Object)(object)val == (Object)null)
			{
				_message = "VaM plugin manager is not ready on " + atom.uid + ".";
				return;
			}
			if (HasInstalledPath(val, text))
			{
				_message = "A FluidBridge plugin slot is already present on " + atom.uid + ".";
				return;
			}
			val2 = val.CreatePlugin();
			if (val2 == null || val2.pluginURLJSON == null)
			{
				if (val2 != null)
				{
					val.RemovePluginWithUID(val2.uid);
				}
				_message = "Could not create a VaM plugin slot.";
			}
			else
			{
				_pending[atom.uid] = Time.realtimeSinceStartup;
				((JSONStorableString)val2.pluginURLJSON).val = text;
				_message = "Loading FluidBridge v27 on " + atom.uid + ". VaM may ask you to allow the VAR script.";
				_nextScan = Time.realtimeSinceStartup + 0.5f;
			}
		}
		catch (Exception e)
		{
			_pending.Remove(atom.uid);
			try
			{
				if ((Object)(object)val != (Object)null && val2 != null)
				{
					val.RemovePluginWithUID(val2.uid);
				}
			}
			catch
			{
			}
			Report("Could not attach FluidBridge", e);
		}
	}

	private static bool HasInstalledPath(MVRPluginManager manager, string path)
	{
		try
		{
			FieldInfo field = typeof(MVRPluginManager).GetField("plugins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			IEnumerable<MVRPlugin> enumerable = ((field != null) ? (field.GetValue(manager) as IEnumerable<MVRPlugin>) : null);
			if (enumerable == null)
			{
				return false;
			}
			foreach (MVRPlugin item in enumerable)
			{
				string text = ((item != null && item.pluginURLJSON != null) ? ((JSONStorableString)item.pluginURLJSON).val : null);
				if (!string.IsNullOrEmpty(text) && (text.Equals(path, StringComparison.OrdinalIgnoreCase) || text.IndexOf("ZeroT/FluidBridge/FluidBridge.cslist", StringComparison.OrdinalIgnoreCase) >= 0))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private void ToggleCollapse()
	{
		if (!_collapsed.Value)
		{
			_windowH.Value = Mathf.Max(315f, _window.height);
		}
		_collapsed.Value = !_collapsed.Value;
		_window.height = ((!_collapsed.Value) ? Mathf.Max(315f, _windowH.Value) : 34f);
		_resizing = false;
		MarkLayoutDirty();
	}

	private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();
	private void ClampWindow()
	{
		_home.Begin(ref _window);
		float num = (float)Screen.width / Mathf.Max(0.01f, _effectiveScale);
		float num2 = (float)Screen.height / Mathf.Max(0.01f, _effectiveScale);
		_window.width = Mathf.Min(Mathf.Max(340f, _window.width), Mathf.Max(340f, num));
		_window.height = ((!_collapsed.Value) ? Mathf.Min(Mathf.Max(315f, _window.height), Mathf.Max(315f, num2)) : 34f);
		_window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, num - Mathf.Min(80f, _window.width)));
		_window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, num2 - Mathf.Min(30f, _window.height)));
		_home.End(ref _window);
	}

	private void MarkLayoutDirty()
	{
		_layoutDirty = true;
		_saveAfter = Time.realtimeSinceStartup + 0.7f;
	}

	private void SaveLayout()
	{
		_windowX.Value = _window.x;
		_windowY.Value = _window.y;
		_windowW.Value = _window.width;
		if (!_collapsed.Value)
		{
			_windowH.Value = _window.height;
		}
		((BaseUnityPlugin)this).Config.Save();
		_layoutDirty = false;
	}

	private void Report(string context, Exception e)
	{
		_message = context + ": " + e.Message;
		Logger.LogWarning((object)_message);
	}

	private void OnDestroy()
	{
		if (_layoutDirty)
		{
			SaveLayout();
		}
		if ((Object)(object)_windowBackground != (Object)null)
		{
			Object.Destroy((Object)(object)_windowBackground);
		}
	}
}
