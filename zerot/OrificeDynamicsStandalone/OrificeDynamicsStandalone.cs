using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Skynet.OrificeDynamicsStandalone;

[BepInPlugin("skynet.orificedynamics.standalone", "Orifice Dynamics Standalone", "1.0.0")]
public sealed class OrificeDynamicsStandalone : BaseUnityPlugin
{
	private const string CoreUrl = "Custom/Scripts/Skynet/OrificeDynamicsStandalone34/OrificeDynamics.Core.dll";

	private const string ResourceName = "OrificeDynamics.Resources";

	private const int WindowId = 347127;

	private const float MinWidth = 350f;

	private const float MinHeight = 355f;

	private const float CollapsedHeight = 34f;

	private ConfigEntry<bool> _collapsed;

	private ConfigEntry<bool> _show;

	private ConfigEntry<bool> _active;

	private ConfigEntry<bool> _autoDpi;

	private ConfigEntry<bool> _autoAttach;

	private ConfigEntry<bool> _globalCollision;

	private ConfigEntry<float> _scale;

	private ConfigEntry<float> _x;

	private ConfigEntry<float> _y;

	private ConfigEntry<float> _width;

	private ConfigEntry<float> _height;

	private readonly List<Atom> _people = new List<Atom>();

	private readonly Dictionary<string, MVRScript> _instances = new Dictionary<string, MVRScript>();

	private readonly Dictionary<string, float> _pending = new Dictionary<string, float>();

	private readonly HashSet<int> _morphRefreshes = new HashSet<int>();

	private readonly HashSet<int> _collisionApplied = new HashSet<int>();

	private string _selectedUid = "";

	private string _status = "Waiting for a female Person atom.";

	private bool _installed;

	private bool _dirty;

	private bool _resizing;

	private float _nextScan;

	private float _saveAt;

	private float _effectiveScale = 1f;

	private Rect _window;

	private Vector2 _scroll;

	private Vector2 _resizeMouse;

	private Vector2 _resizeSize;

	private GUIStyle _title;

	private GUIStyle _text;

	private GUIStyle _section;

	private Texture2D _background;

	private void Awake()
	{
		_active = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Active", true, "Turn Orifice Dynamics off completely: stops scanning/attaching and disables the attached scripts.");
		_show = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Show", true, "Show the in-game Orifice Dynamics window.");
		_collapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Collapsed", false, "Remember the collapsed state.");
		_autoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "AutoDPI", true, "Scale using the display DPI when available.");
		_scale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "UIScale", 1f, "Extra scale from 0.65 to 2.5.");
		_x = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "X", 60f, "Saved logical X coordinate.");
		_y = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Y", 80f, "Saved logical Y coordinate.");
		_width = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Width", 450f, "Saved logical width.");
		_height = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Height", 555f, "Saved expanded height.");
		_autoAttach = ((BaseUnityPlugin)this).Config.Bind<bool>("People", "AutoAttachFemale", true, "Attach the standalone core to female Person atoms without an OrificeDynamics slot.");
		_globalCollision = ((BaseUnityPlugin)this).Config.Bind<bool>("People", "EnableGlobalCollisionOnAttach", true, "Enable VaM global collision on the attached female Person controllers.");
		_window = new Rect(_x.Value, _y.Value, _width.Value, (!_collapsed.Value) ? _height.Value : 34f);
		try
		{
			InstallEmbeddedFiles();
			_installed = File.Exists(Path.Combine(Paths.GameRootPath, "Custom/Scripts/Skynet/OrificeDynamicsStandalone34/OrificeDynamics.Core.dll"));
			_status = ((!_installed) ? "Core DLL was not found after extraction." : "Standalone core ready. Waiting for a female Person.");
		}
		catch (Exception e)
		{
			_installed = false;
			Report("Could not extract embedded OrificeDynamics files", e);
		}
		_nextScan = Time.realtimeSinceStartup + 2f;
	}

	private static void InstallEmbeddedFiles()
	{
		Stream manifestResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OrificeDynamics.Resources");
		if (manifestResourceStream == null)
		{
			throw new InvalidDataException("Missing embedded OrificeDynamics core and morphs.");
		}
		using (manifestResourceStream)
		{
			using DeflateStream input = new DeflateStream(manifestResourceStream, CompressionMode.Decompress);
			using BinaryReader binaryReader = new BinaryReader(input);
			int num = binaryReader.ReadInt32();
			if (num < 1 || num > 100)
			{
				throw new InvalidDataException("Invalid embedded file count.");
			}
			string fullPath = Path.GetFullPath(Paths.GameRootPath);
			for (int i = 0; i < num; i++)
			{
				string text = binaryReader.ReadString().Replace('\\', '/');
				int num2 = binaryReader.ReadInt32();
				if ((!text.StartsWith("Custom/Scripts/Skynet/OrificeDynamicsStandalone34/", StringComparison.Ordinal) && !text.StartsWith("Custom/Atom/Person/Morphs/female/OrificeDynamicsStandalone34/", StringComparison.Ordinal) && !text.StartsWith("Custom/Atom/Person/Morphs/female_genitalia/OrificeDynamicsStandalone34/", StringComparison.Ordinal)) || text.Contains("../") || num2 < 1 || num2 > 8000000)
				{
					throw new InvalidDataException("Invalid embedded asset path or size: " + text);
				}
				byte[] array = binaryReader.ReadBytes(num2);
				if (array.Length != num2)
				{
					throw new EndOfStreamException("Incomplete embedded asset: " + text);
				}
				string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, text));
				if (!fullPath2.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Asset escapes the VaM folder.");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath2));
				if (File.Exists(fullPath2) && EqualHash(File.ReadAllBytes(fullPath2), array))
				{
					continue;
				}
				string text2 = fullPath2 + ".standalone.tmp";
				try
				{
					File.WriteAllBytes(text2, array);
					if (File.Exists(fullPath2))
					{
						File.Delete(fullPath2);
					}
					File.Move(text2, fullPath2);
				}
				finally
				{
					if (File.Exists(text2))
					{
						File.Delete(text2);
					}
				}
			}
		}
	}

	private static bool EqualHash(byte[] left, byte[] right)
	{
		if (left.Length != right.Length)
		{
			return false;
		}
		using (SHA256 sHA = SHA256.Create())
		{
			byte[] array = sHA.ComputeHash(left);
			byte[] array2 = sHA.ComputeHash(right);
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != array2[i])
				{
					return false;
				}
			}
		}
		return true;
	}

	private void Update()
	{
		if (_active.Value && Time.realtimeSinceStartup >= _nextScan)
		{
			_nextScan = Time.realtimeSinceStartup + 2.5f;
			ScanPeople();
		}
		if (_resizing)
		{
			if (Input.GetMouseButton(0) && !_collapsed.Value)
			{
				Vector2 val = LogicalMouse();
				_window.width = _resizeSize.x + val.x - _resizeMouse.x;
				_window.height = _resizeSize.y + val.y - _resizeMouse.y;
				ClampWindow();
				MarkDirty();
			}
			else
			{
				_resizing = false;
			}
		}
		if (_dirty && !_resizing && !Input.GetMouseButton(0) && Time.realtimeSinceStartup >= _saveAt)
		{
			SaveLayout();
		}
	}

	private static DAZCharacterSelector FemaleSelector(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || atom.type != "Person")
		{
			return null;
		}
		JSONStorable storableByID = atom.GetStorableByID("character");
		object obj = ((storableByID is DAZCharacterSelector) ? storableByID : null);
		if (obj == null)
		{
			JSONStorable storableByID2 = atom.GetStorableByID("geometry");
			obj = ((storableByID2 is DAZCharacterSelector) ? storableByID2 : null);
		}
		DAZCharacterSelector val = (DAZCharacterSelector)obj;
		return (!((Object)(object)val != (Object)null) || (int)val.gender != 2) ? null : val;
	}

	private void ScanPeople()
	{
		try
		{
			_people.Clear();
			_instances.Clear();
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
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom val = atoms[i];
				if ((Object)(object)FemaleSelector(val) == (Object)null || string.IsNullOrEmpty(val.uid))
				{
					continue;
				}
				_people.Add(val);
				MVRScript val2 = FindDynamics(val);
				if ((Object)(object)val2 != (Object)null)
				{
					_instances[val.uid] = val2;
					((Behaviour)val2).enabled = true;
					_pending.Remove(val.uid);
					if (_globalCollision.Value && IsStandalone(val2) && _collisionApplied.Add(((Object)val2).GetInstanceID()))
					{
						EnableGlobalCollision(val);
					}
				}
			}
			if ((Object)(object)SelectedPerson() == (Object)null)
			{
				Atom selectedAtom = singleton.GetSelectedAtom();
				if ((Object)(object)selectedAtom != (Object)null && (Object)(object)FemaleSelector(selectedAtom) != (Object)null)
				{
					_selectedUid = selectedAtom.uid;
				}
				if ((Object)(object)SelectedPerson() == (Object)null && _people.Count > 0)
				{
					_selectedUid = _people[0].uid;
				}
			}
			if (!_autoAttach.Value || !_installed)
			{
				return;
			}
			for (int j = 0; j < _people.Count; j++)
			{
				Atom val3 = _people[j];
				if (!_instances.ContainsKey(val3.uid) && !HasDynamicsSlot(FindManager(val3)))
				{
					AddToPerson(val3);
				}
			}
		}
		catch (Exception e)
		{
			Report("Female atom scan failed", e);
		}
	}

	private static MVRScript FindDynamics(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || (Object)(object)((Component)atom).gameObject == (Object)null)
		{
			return null;
		}
		MVRScript[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRScript>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if ((Object)(object)componentsInChildren[i] != (Object)null && (Object)(object)((JSONStorable)componentsInChildren[i]).containingAtom == (Object)(object)atom && ((object)componentsInChildren[i]).GetType().Name == "OrificeDynamics" && ((object)componentsInChildren[i]).GetType().Namespace == "Skynet")
			{
				return componentsInChildren[i];
			}
		}
		return null;
	}

	private static bool IsStandalone(MVRScript script)
	{
		return (Object)(object)script != (Object)null && ((object)script).GetType().Assembly.GetName().Name == "OrificeDynamics.Core";
	}

	private static MVRPluginManager FindManager(Atom atom)
	{
		if ((Object)(object)atom == (Object)null || (Object)(object)((Component)atom).gameObject == (Object)null)
		{
			return null;
		}
		JSONStorable storableByID = atom.GetStorableByID("plugin");
		MVRPluginManager val = (MVRPluginManager)(object)((storableByID is MVRPluginManager) ? storableByID : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		MVRPluginManager[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<MVRPluginManager>(true);
		return (componentsInChildren.Length <= 0) ? null : componentsInChildren[0];
	}

	private static bool HasDynamicsSlot(MVRPluginManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			return false;
		}
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
				if (!string.IsNullOrEmpty(text) && (text.IndexOf("OrificeDynamics", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Orifice Dynamics/", StringComparison.OrdinalIgnoreCase) >= 0))
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

	private void AddToPerson(Atom atom)
	{
		DAZCharacterSelector val = FemaleSelector(atom);
		if ((Object)(object)val == (Object)null)
		{
			_status = "Only female Person atoms are supported.";
			return;
		}
		if (!_installed)
		{
			_status = "Standalone core is unavailable. Check BepInEx/LogOutput.log.";
			return;
		}
		if ((Object)(object)FindDynamics(atom) != (Object)null)
		{
			_status = "Orifice Dynamics is already on " + atom.uid + ".";
			return;
		}
		MVRPluginManager val2 = FindManager(atom);
		if ((Object)(object)val2 == (Object)null)
		{
			_status = "VaM's plugin manager is not ready on " + atom.uid + ".";
		}
		else if (HasDynamicsSlot(val2))
		{
			_status = "An Orifice Dynamics slot is already on " + atom.uid + ".";
		}
		else
		{
			if (_pending.TryGetValue(atom.uid, out var value) && Time.realtimeSinceStartup - value < 30f)
			{
				return;
			}
			MVRPlugin val3 = null;
			try
			{
				if (_morphRefreshes.Add(((Object)val).GetInstanceID()))
				{
					val.RefreshRuntimeMorphs();
				}
				val3 = val2.CreatePlugin();
				if (val3 == null || val3.pluginURLJSON == null)
				{
					throw new InvalidOperationException("VaM could not create a plugin slot.");
				}
				_pending[atom.uid] = Time.realtimeSinceStartup;
				((JSONStorableString)val3.pluginURLJSON).val = "Custom/Scripts/Skynet/OrificeDynamicsStandalone34/OrificeDynamics.Core.dll";
				if (_globalCollision.Value)
				{
					EnableGlobalCollision(atom);
				}
				_status = "Attached Orifice Dynamics to " + atom.uid + ".";
				_nextScan = Time.realtimeSinceStartup + 0.6f;
			}
			catch (Exception e)
			{
				_pending.Remove(atom.uid);
				try
				{
					if (val3 != null)
					{
						val2.RemovePluginWithUID(val3.uid);
					}
				}
				catch
				{
				}
				Report("Could not attach Orifice Dynamics", e);
			}
		}
	}

	private void EnableGlobalCollision(Atom atom)
	{
		if ((Object)(object)FemaleSelector(atom) == (Object)null || (Object)(object)((Component)atom).gameObject == (Object)null)
		{
			return;
		}
		try
		{
			FreeControllerV3[] componentsInChildren = ((Component)atom).gameObject.GetComponentsInChildren<FreeControllerV3>(true);
			int num = 0;
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				if ((Object)(object)componentsInChildren[i] != (Object)null && !componentsInChildren[i].globalCollisionEnabled)
				{
					componentsInChildren[i].globalCollisionEnabled = true;
					num++;
				}
			}
			_status = "Global collision enabled on " + atom.uid + " (" + num + " controllers updated).";
		}
		catch (Exception e)
		{
			Report("Global collision setup failed", e);
		}
	}

	private void OnGUI()
	{
		if (Screen.width <= 0 || Screen.height <= 0)
		{
			return;
		}
		if (!_show.Value)
		{
			return;
		}
		_effectiveScale = EffectiveScale();
		EnsureStyles();
		Rect window = _window;
		ClampWindow();
		if (Mathf.Abs(_window.x - window.x) > 0.01f || Mathf.Abs(_window.y - window.y) > 0.01f || Mathf.Abs(_window.width - window.width) > 0.01f || Mathf.Abs(_window.height - window.height) > 0.01f)
		{
			MarkDirty();
		}
		Matrix4x4 matrix = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(_effectiveScale, _effectiveScale, 1f)) * matrix;
		Rect window2 = _window;
		try
		{
			_window = GUI.Window(347127, _window, (GUI.WindowFunction)DrawWindow, "", GUIStyle.none);
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Orifice Dynamics overlay: " + ex.Message));
		}
		finally
		{
			GUI.matrix = matrix;
		}
		ClampWindow();
		if (Mathf.Abs(_window.x - window2.x) > 0.01f || Mathf.Abs(_window.y - window2.y) > 0.01f || Mathf.Abs(_window.width - window2.width) > 0.01f || Mathf.Abs(_window.height - window2.height) > 0.01f)
		{
			MarkDirty();
		}
	}

	private float EffectiveScale()
	{
		float num = (!_autoDpi.Value) ? 1f : ZeroT.UiKit.RlChrome.Dpi(_window.x * _effectiveScale, _window.y * _effectiveScale);
		return num * Mathf.Clamp(_scale.Value, 0.3f, 2.5f);
	}

	private Vector2 LogicalMouse()
	{
		return new Vector2(Input.mousePosition.x / _effectiveScale, ((float)Screen.height - Input.mousePosition.y) / _effectiveScale);
	}

	private readonly ZeroT.UiKit.RlChrome.WindowHome _home = new ZeroT.UiKit.RlChrome.WindowHome();
	private void ClampWindow()
	{
		_home.Begin(ref _window);
		float num = (float)Screen.width / Mathf.Max(0.01f, _effectiveScale);
		float num2 = (float)Screen.height / Mathf.Max(0.01f, _effectiveScale);
		if (!(num <= 0f) && !(num2 <= 0f))
		{
			_window.width = Mathf.Clamp(_window.width, Mathf.Min(350f, num), num);
			_window.height = ((!_collapsed.Value) ? Mathf.Clamp(_window.height, Mathf.Min(355f, num2), num2) : Mathf.Min(34f, num2));
			_window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, num - _window.width));
			_window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, num2 - _window.height));
		}
		_home.End(ref _window);
	}

	private void EnsureStyles()
	{
		if (_title == null)
		{
			_title = new GUIStyle(GUI.skin.label);
			GUIStyle val = new GUIStyle(GUI.skin.label);
			val.wordWrap = true;
			_text = val;
			_section = new GUIStyle(GUI.skin.label);
		}
	}

	private void DrawWindow(int id)
	{
		ZeroT.UiKit.RlChrome.Backdrop(_window.width, _window.height);
		int num = ZeroT.UiKit.RlChrome.TitleRow(_window.width, "Orifice Dynamics", _collapsed.Value, _active.Value);
		if ((num & 1) != 0)
		{
			SetScale(_scale.Value - 0.1f);
		}
		if ((num & 2) != 0)
		{
			SetScale(_scale.Value + 0.1f);
		}
		if ((num & 16) != 0)
		{
			SetActive(!_active.Value);
		}
		if ((num & 4) != 0)
		{
			ToggleCollapse();
		}
		if ((num & 8) != 0)
		{
			_show.Value = false;
			((BaseUnityPlugin)this).Config.Save();
		}
		if (!_collapsed.Value)
		{
			GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(_window.width, _window.height));
			DrawExpanded();
			GUILayout.EndArea();
			if (ZeroT.UiKit.RlChrome.ResizeHandle(_window.width, _window.height))
			{
				_resizing = true;
				_resizeMouse = LogicalMouse();
				_resizeSize = new Vector2(_window.width, _window.height);
			}
		}
		if (!_resizing)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, _window.width - 118f), 26f));
		}
	}

	private void SetActive(bool on)
	{
		_active.Value = on;
		foreach (KeyValuePair<string, MVRScript> instance in _instances)
		{
			if ((Object)(object)instance.Value != (Object)null)
			{
				((Behaviour)instance.Value).enabled = on;
			}
		}
		if (on)
		{
			_nextScan = 0f;
		}
		((BaseUnityPlugin)this).Config.Save();
	}

	private void SetScale(float value)
	{
		_scale.Value = Mathf.Clamp(value, 0.3f, 2.5f);
		ZeroT.UiKit.RlChrome.ResetDpi();
		MarkDirty();
	}

	private void DrawExpanded()
	{
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Use selected female Person", new GUILayoutOption[1] { GUILayout.Height(26f) }))
		{
			SelectCurrent();
		}
		if (GUILayout.Button("Refresh", new GUILayoutOption[2]
		{
			GUILayout.Width(69f),
			GUILayout.Height(26f)
		}))
		{
			ScanPeople();
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("◀", new GUILayoutOption[1] { GUILayout.Width(30f) }))
		{
			ShiftSelection(-1);
		}
		Atom val = SelectedPerson();
		GUILayout.Label((!((Object)(object)val == (Object)null)) ? val.uid : "No female Person", _section, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		if (GUILayout.Button("▶", new GUILayoutOption[1] { GUILayout.Width(30f) }))
		{
			ShiftSelection(1);
		}
		GUILayout.EndHorizontal();
		GUILayout.Label(_status, _text, new GUILayoutOption[0]);
		_scroll = GUILayout.BeginScrollView(_scroll, new GUILayoutOption[1] { GUILayout.ExpandHeight(true) });
		MVRScript value;
		if ((Object)(object)val == (Object)null)
		{
			GUILayout.Label("Add a female Person atom. Auto attach is enabled by default.", _text, new GUILayoutOption[0]);
		}
		else if (!_instances.TryGetValue(val.uid, out value) || (Object)(object)value == (Object)null)
		{
			GUILayout.Label("Orifice Dynamics is not active on this female Person.", _text, new GUILayoutOption[0]);
			if (GUILayout.Button("Attach standalone core", new GUILayoutOption[1] { GUILayout.Height(29f) }))
			{
				AddToPerson(val);
			}
		}
		else
		{
			DrawControls(val, value);
		}
		GUILayout.EndScrollView();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		bool flag = GUILayout.Toggle(_autoAttach.Value, "Auto attach female", new GUILayoutOption[1] { GUILayout.Width(146f) });
		if (flag != _autoAttach.Value)
		{
			_autoAttach.Value = flag;
			((BaseUnityPlugin)this).Config.Save();
			_nextScan = 0f;
		}
		bool flag2 = GUILayout.Toggle(_globalCollision.Value, "Global collision on attach", new GUILayoutOption[0]);
		if (flag2 != _globalCollision.Value)
		{
			_globalCollision.Value = flag2;
			((BaseUnityPlugin)this).Config.Save();
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		bool flag3 = GUILayout.Toggle(_autoDpi.Value, "DPI aware", new GUILayoutOption[1] { GUILayout.Width(88f) });
		if (flag3 != _autoDpi.Value)
		{
			_autoDpi.Value = flag3;
			MarkDirty();
		}
		GUILayout.Label("Scale (S- / S+): " + _scale.Value.ToString("0.00") + "x", new GUILayoutOption[0]);
		GUILayout.EndHorizontal();
	}

	private void DrawControls(Atom atom, MVRScript script)
	{
		GUILayout.Label((!IsStandalone(script)) ? "Original VAR active; using its existing controls" : "Standalone DLL active", _text, new GUILayoutOption[0]);
		GUILayout.Label("Optimized physics", _section, new GUILayoutOption[0]);
		DrawToggle(script, "Enable Optimized Physics", "Optimized physics");
		GUILayout.Label("Auto open genital orifice", _section, new GUILayoutOption[0]);
		DrawToggle(script, "Enable Gape Module", "Auto gape system");
		DrawToggle(script, "Enable Anal Gape", "Anal gape");
		DrawToggle(script, "Enable Vaginal Gape", "Vaginal gape");
		GUILayout.Label("Auto bulging and in/out motion", _section, new GUILayoutOption[0]);
		DrawToggle(script, "Enable Bulge Module", "Bulging system");
		DrawToggle(script, "Enable Belly Bulge", "Belly bulge");
		DrawToggle(script, "Enable Anal Motion", "Anal in/out motion");
		DrawToggle(script, "Enable Vagina Motion", "Vaginal in/out motion");
		GUILayout.Label("Global collision", _section, new GUILayoutOption[0]);
		if (GUILayout.Button("Enable global collisions on this female Person", new GUILayoutOption[0]))
		{
			EnableGlobalCollision(atom);
		}
		GUILayout.Label("The complete Orifice Dynamics settings remain in this Person's VaM Plugins tab.", _text, new GUILayoutOption[0]);
	}

	private void DrawToggle(MVRScript script, string name, string label)
	{
		JSONStorableBool boolJSONParam = ((JSONStorable)script).GetBoolJSONParam(name);
		if (boolJSONParam == null)
		{
			GUILayout.Label(label + ": initializing", _text, new GUILayoutOption[0]);
			return;
		}
		bool flag = GUILayout.Toggle(boolJSONParam.val, label, new GUILayoutOption[0]);
		if (flag == boolJSONParam.val)
		{
			return;
		}
		try
		{
			boolJSONParam.val = flag;
		}
		catch (Exception e)
		{
			Report("Could not change " + name, e);
		}
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

	private void SelectCurrent()
	{
		Atom val = ((!((Object)(object)SuperController.singleton == (Object)null)) ? SuperController.singleton.GetSelectedAtom() : null);
		if ((Object)(object)FemaleSelector(val) == (Object)null)
		{
			_status = "Select a female Person in VaM first.";
			return;
		}
		_selectedUid = val.uid;
		_scroll = Vector2.zero;
		ScanPeople();
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

	private void ToggleCollapse()
	{
		if (!_collapsed.Value)
		{
			_height.Value = Mathf.Max(355f, _window.height);
		}
		_collapsed.Value = !_collapsed.Value;
		_window.height = ((!_collapsed.Value) ? _height.Value : 34f);
		_resizing = false;
		ClampWindow();
		MarkDirty();
	}

	private void MarkDirty()
	{
		_dirty = true;
		_saveAt = Time.realtimeSinceStartup + 0.5f;
	}

	private void SaveLayout()
	{
		ClampWindow();
		_x.Value = _window.x;
		_y.Value = _window.y;
		_width.Value = _window.width;
		if (!_collapsed.Value)
		{
			_height.Value = _window.height;
		}
		((BaseUnityPlugin)this).Config.Save();
		_dirty = false;
	}

	private void Report(string prefix, Exception e)
	{
		_status = prefix + ": " + e.Message;
		Logger.LogError((object)(prefix + ": " + e));
	}

	private void OnDestroy()
	{
		if (_dirty)
		{
			SaveLayout();
		}
		if ((Object)(object)_background != (Object)null)
		{
			Object.Destroy((Object)(object)_background);
		}
	}
}
