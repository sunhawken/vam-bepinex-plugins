using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Charlestone.AnusPhysicsControlStandalone;

[BepInPlugin("charlestone.anusphysicscontrol.standalone", "Anus Physics Control Standalone", "1.2.0")]
public sealed class AnusPhysicsControlPlugin : BaseUnityPlugin
{
	private sealed class Profile
	{
		public bool AnalFirm;

		public bool DisableAnal;

		public bool DisableJointB;

		public bool VulvaFirm;

		public float AnalSpring;

		public float AnalDamper;

		public float VulvaSpring;

		public float VulvaDamper;

		public Profile Copy()
		{
			return (Profile)MemberwiseClone();
		}
	}

	private sealed class GroupState
	{
		private readonly DAZPhysicsMeshSoftVerticesGroup _group;

		private float _baselineSpring;

		private float _baselineDamper;

		private float _lastSpring;

		private float _lastDamper;

		private bool _owned;

		public DAZPhysicsMeshSoftVerticesGroup Group => _group;

		public GroupState(DAZPhysicsMeshSoftVerticesGroup group)
		{
			_group = group;
		}

		public void Apply(bool active, float spring, float damper)
		{
			if (!active)
			{
				Restore();
				return;
			}
			if (!_owned)
			{
				_baselineSpring = _group.jointSpringNormal;
				_baselineDamper = _group.jointDamperNormal;
			}
			_group.jointSpringNormal = (_lastSpring = spring);
			_group.jointDamperNormal = (_lastDamper = damper);
			_owned = true;
		}

		public void Restore()
		{
			if (_owned)
			{
				if (Mathf.Abs(_group.jointSpringNormal - _lastSpring) < 0.001f)
				{
					_group.jointSpringNormal = _baselineSpring;
				}
				if (Mathf.Abs(_group.jointDamperNormal - _lastDamper) < 0.001f)
				{
					_group.jointDamperNormal = _baselineDamper;
				}
				_owned = false;
			}
		}
	}

	private sealed class CollisionState
	{
		public readonly Rigidbody Body;

		private readonly Collider _collider;

		private bool _originalBody;

		private bool _originalCollider;

		private bool _owned;

		public CollisionState(Rigidbody body)
		{
			Body = body;
			if ((Object)(object)body != (Object)null)
			{
				Collider[] components = ((Component)body).GetComponents<Collider>();
				if (components.Length != 0)
				{
					_collider = components[0];
				}
			}
		}

		public void Apply(bool disable)
		{
			if (!disable)
			{
				Restore();
				return;
			}
			if (!_owned)
			{
				if ((Object)(object)Body != (Object)null)
				{
					_originalBody = Body.detectCollisions;
				}
				if ((Object)(object)_collider != (Object)null)
				{
					_originalCollider = _collider.enabled;
				}
			}
			if ((Object)(object)Body != (Object)null)
			{
				Body.detectCollisions = false;
			}
			if ((Object)(object)_collider != (Object)null)
			{
				_collider.enabled = false;
			}
			_owned = true;
		}

		public void Restore()
		{
			if (_owned)
			{
				if ((Object)(object)Body != (Object)null && !Body.detectCollisions)
				{
					Body.detectCollisions = _originalBody;
				}
				if ((Object)(object)_collider != (Object)null && !_collider.enabled)
				{
					_collider.enabled = _originalCollider;
				}
				_owned = false;
			}
		}
	}

	private sealed class TargetState
	{
		private readonly Atom _atom;

		private readonly DAZPhysicsMesh _mesh;

		public readonly bool IsFemale;

		private readonly List<GroupState> _anal = new List<GroupState>();

		private readonly List<GroupState> _vulva = new List<GroupState>();

		private readonly CollisionState _analCollision;

		private readonly CollisionState _jointBCollision;

		public TargetState(Atom atom, bool isFemale)
		{
			_atom = atom;
			IsFemale = isFemale;
			JSONStorable storableByID = atom.GetStorableByID("LowerPhysicsMesh");
			_mesh = (DAZPhysicsMesh)(object)((storableByID is DAZPhysicsMesh) ? storableByID : null);
			if ((Object)(object)_mesh != (Object)null && _mesh.softVerticesGroups != null)
			{
				foreach (DAZPhysicsMeshSoftVerticesGroup softVerticesGroup in _mesh.softVerticesGroups)
				{
					if (softVerticesGroup != null)
					{
						if (IsAnal(softVerticesGroup.name))
						{
							_anal.Add(new GroupState(softVerticesGroup));
						}
						else if (IsVulva(softVerticesGroup.name))
						{
							_vulva.Add(new GroupState(softVerticesGroup));
						}
					}
				}
			}
			FindBodies(atom, out var anal, out var jointB);
			_analCollision = new CollisionState(anal);
			_jointBCollision = new CollisionState(jointB);
		}

		private static bool IsAnal(string name)
		{
			return name == "an" || name == "antop" || name == "antopsides";
		}

		private static bool IsVulva(string name)
		{
			return name == "lab" || name == "gen" || name == "geninner";
		}

		private static void FindBodies(Atom atom, out Rigidbody anal, out Rigidbody jointB)
		{
			anal = null;
			jointB = null;
			Rigidbody[] componentsInChildren = ((Component)atom).GetComponentsInChildren<Rigidbody>(true);
			foreach (Rigidbody val in componentsInChildren)
			{
				if (((Object)((Component)val).gameObject).name == "_JointAl")
				{
					anal = val;
				}
				else if (((Object)((Component)val).gameObject).name == "_JointB")
				{
					jointB = val;
				}
			}
		}

		public bool Matches(Atom atom, bool female)
		{
			if (IsFemale != female)
			{
				return false;
			}
			if ((Object)(object)_atom == (Object)null || !object.ReferenceEquals(_atom, atom))
			{
				return false;
			}
			JSONStorable storableByID = atom.GetStorableByID("LowerPhysicsMesh");
			DAZPhysicsMesh val = (DAZPhysicsMesh)(object)((storableByID is DAZPhysicsMesh) ? storableByID : null);
			if (!object.ReferenceEquals(_mesh, val))
			{
				return false;
			}
			if ((Object)(object)val == (Object)null && (_anal.Count != 0 || _vulva.Count != 0))
			{
				return false;
			}
			int num = 0;
			int num2 = 0;
			if ((Object)(object)val != (Object)null && val.softVerticesGroups != null)
			{
				foreach (DAZPhysicsMeshSoftVerticesGroup softVerticesGroup in val.softVerticesGroups)
				{
					if (softVerticesGroup == null)
					{
						continue;
					}
					if (IsAnal(softVerticesGroup.name))
					{
						if (num >= _anal.Count || !object.ReferenceEquals(softVerticesGroup, _anal[num].Group))
						{
							return false;
						}
						num++;
					}
					else if (IsVulva(softVerticesGroup.name))
					{
						if (num2 >= _vulva.Count || !object.ReferenceEquals(softVerticesGroup, _vulva[num2].Group))
						{
							return false;
						}
						num2++;
					}
				}
			}
			if (num != _anal.Count || num2 != _vulva.Count)
			{
				return false;
			}
			FindBodies(atom, out var anal, out var jointB);
			return object.ReferenceEquals(anal, _analCollision.Body) && object.ReferenceEquals(jointB, _jointBCollision.Body);
		}

		public void Apply(Profile profile)
		{
			if (profile == null)
			{
				Restore();
				return;
			}
			foreach (GroupState item in _anal)
			{
				item.Apply(profile.AnalFirm, profile.AnalSpring, profile.AnalDamper);
			}
			foreach (GroupState item2 in _vulva)
			{
				item2.Apply(profile.VulvaFirm, profile.VulvaSpring, profile.VulvaDamper);
			}
			_analCollision.Apply(profile.DisableAnal);
			_jointBCollision.Apply(profile.DisableJointB);
		}

		public void Restore()
		{
			foreach (GroupState item in _anal)
			{
				item.Restore();
			}
			foreach (GroupState item2 in _vulva)
			{
				item2.Restore();
			}
			_analCollision.Restore();
			_jointBCollision.Restore();
		}
	}

	private const float ScanSeconds = 3f;

	private const float DefaultWindowWidth = 455f;

	private const float DefaultWindowHeight = 535f;

	private const float MinWindowWidth = 320f;

	private const float MinWindowHeight = 210f;

	private const float MaxWindowWidth = 900f;

	private const float MaxWindowHeight = 1000f;

	private const float CollapsedWidth = 240f;

	private const float CollapsedHeight = 34f;

	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	private readonly Dictionary<string, TargetState> _targets = new Dictionary<string, TargetState>();

	private readonly Dictionary<string, Profile> _overrides = new Dictionary<string, Profile>();

	private readonly Dictionary<string, string> _editBuffers = new Dictionary<string, string>();

	private readonly List<string> _personIds = new List<string>();

	private ConfigEntry<bool> _applyAll;

	private ConfigEntry<bool> _visible;

	private ConfigEntry<bool> _collapsed;

	private ConfigEntry<bool> _autoDpi;

	private ConfigEntry<float> _windowX;

	private ConfigEntry<float> _windowY;

	private ConfigEntry<float> _windowWidth;

	private ConfigEntry<float> _windowHeight;

	private ConfigEntry<float> _scale;

	private ConfigEntry<float> _positionScale;

	private ConfigEntry<string> _serializedOverrides;

	private ConfigEntry<bool> _analFirm;

	private ConfigEntry<bool> _analCollision;

	private ConfigEntry<bool> _jointBCollision;

	private ConfigEntry<bool> _vulvaFirm;

	private ConfigEntry<float> _analSpring;

	private ConfigEntry<float> _analDamper;

	private ConfigEntry<float> _vulvaSpring;

	private ConfigEntry<float> _vulvaDamper;

	private Profile _global;

	private string _selectedId;

	private float _scanTimer;

	private Rect _window;

	private Vector2 _scroll;

	private float _lastEffectiveScale = 1f;

	private float _desktopDpi = 96f;

	private float _nextDpiCheck;

	private bool _dpiDetected;

	private bool _resetWindowRequested;

	private bool _resizingWindow;

	private Vector2 _resizeStartMouse;

	private float _resizeStartWidth;

	private float _resizeStartHeight;

	private float _resizeScale;

	private float _expandedWidth;

	private float _expandedHeight;

	[DllImport("user32.dll")]
	private static extern IntPtr GetActiveWindow();

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr window);

	[DllImport("user32.dll")]
	private static extern uint GetDpiForSystem();

	private void Awake()
	{
		_applyAll = ((BaseUnityPlugin)this).Config.Bind<bool>("Scope", "ApplyToAllFemalePersons", true, "Session variant: apply global settings to all female Person atoms. Individual overrides take precedence.");
		_serializedOverrides = ((BaseUnityPlugin)this).Config.Bind<string>("Scope", "IndividualOverrides", "", "Per-Person variant: saved settings indexed by Person UID. Edit these in the plugin window.");
		_analFirm = ((BaseUnityPlugin)this).Config.Bind<bool>("Global", "FirmAnalPhysics", true, "Original package default: true.");
		_analSpring = ((BaseUnityPlugin)this).Config.Bind<float>("Global", "AnalFirmnessSpring", 8000f, "Range: 0 to 20000.");
		_analDamper = ((BaseUnityPlugin)this).Config.Bind<float>("Global", "AnalFirmnessDamper", 200f, "Range: 0 to 1000.");
		_analCollision = ((BaseUnityPlugin)this).Config.Bind<bool>("Global", "DisableAnalCollisions", true, "Disable _JointAl rigidbody collisions and its first collider.");
		_jointBCollision = ((BaseUnityPlugin)this).Config.Bind<bool>("Global", "DisableJointBCollisions", true, "Disable _JointB rigidbody collisions and its first collider.");
		_vulvaFirm = ((BaseUnityPlugin)this).Config.Bind<bool>("Global", "FirmVulvaLabiaPhysics", false, "Original package default: false, preserving vulva/labia physics.");
		_vulvaSpring = ((BaseUnityPlugin)this).Config.Bind<float>("Global", "VulvaFirmnessSpring", 8000f, "Range: 0 to 20000.");
		_vulvaDamper = ((BaseUnityPlugin)this).Config.Bind<float>("Global", "VulvaFirmnessDamper", 200f, "Range: 0 to 1000.");
		_visible = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Visible", true, "F9 toggles the desktop window.");
		_collapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "Collapsed", false, "Remember whether the desktop window is collapsed to its title bar.");
		_windowX = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "WindowX", 30f, "Last desktop window position X.");
		_windowY = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "WindowY", 80f, "Last desktop window position Y.");
		_windowWidth = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "WindowWidth", 455f, "Expanded desktop window width, 320 to 900 GUI units.");
		_windowHeight = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "WindowHeight", 535f, "Expanded desktop window height, 210 to 1000 GUI units.");
		_autoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("Window", "AutoDPIScaling", true, "Scale the desktop window for Windows display DPI, or Unity DPI when Windows DPI is unavailable.");
		_scale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "Scale", 1f, "Manual UI scale, 0.65 to 1.75; multiplies automatic DPI scaling.");
		_positionScale = ((BaseUnityPlugin)this).Config.Bind<float>("Window", "LastEffectiveScale", 1f, "Scale used when WindowX and WindowY were saved; keeps the physical window position when DPI changes.");
		_global = ReadGlobal();
		LoadOverrides();
		_expandedWidth = Clamp(_windowWidth.Value, 320f, 900f);
		_expandedHeight = Clamp(_windowHeight.Value, 210f, 1000f);
		_window = new Rect(Clamp(_windowX.Value, 0f, 10000f), Clamp(_windowY.Value, 0f, 10000f), (!_collapsed.Value) ? _expandedWidth : 300f, (!_collapsed.Value) ? _expandedHeight : 34f);
		_lastEffectiveScale = Clamp(_positionScale.Value, 0.65f, 3f);
	}

	private void OnEnable()
	{
		if (_applyAll == null)
		{
			return;
		}
		_scanTimer = 0f;
		try
		{
			ScanPersons();
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Initial Person scan failed: " + ex.Message));
		}
	}

	private void Update()
	{
		if (_resizingWindow)
		{
			if (Input.GetMouseButton(0))
			{
				UpdateWindowResize();
			}
			else
			{
				UpdateWindowResize();
				FinishWindowResize();
			}
		}
		if (Input.GetKeyDown((KeyCode)290))
		{
			_visible.Value = !_visible.Value;
		}
		_scanTimer += Time.unscaledDeltaTime;
		if (_scanTimer < 3f)
		{
			return;
		}
		_scanTimer = 0f;
		if ((Object)(object)SuperController.singleton != (Object)null && SuperController.singleton.isLoading)
		{
			return;
		}
		try
		{
			ScanPersons();
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Person scan failed: " + ex.Message));
		}
	}

	private void ScanPersons()
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
		HashSet<string> hashSet = new HashSet<string>();
		for (int i = 0; i < atoms.Count; i++)
		{
			Atom val = atoms[i];
			if ((Object)(object)val == (Object)null || val.type != "Person")
			{
				continue;
			}
			bool flag = (Object)(object)val.GetStorableByID("FemaleAnatomy") != (Object)null;
			string uid = val.uid;
			if (!string.IsNullOrEmpty(uid))
			{
				hashSet.Add(uid);
				if (!_targets.TryGetValue(uid, out var value) || !value.Matches(val, flag))
				{
					value?.Restore();
					TargetState targetState = new TargetState(val, flag);
					_targets[uid] = targetState;
					targetState.Apply(EffectiveProfile(uid, flag));
				}
			}
		}
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, TargetState> target in _targets)
		{
			if (!hashSet.Contains(target.Key))
			{
				list.Add(target.Key);
			}
		}
		for (int j = 0; j < list.Count; j++)
		{
			_targets[list[j]].Restore();
			_targets.Remove(list[j]);
		}
		_personIds.Clear();
		foreach (string item in hashSet)
		{
			_personIds.Add(item);
		}
		_personIds.Sort(StringComparer.Ordinal);
		if (_selectedId == null || !_personIds.Contains(_selectedId))
		{
			_selectedId = ((_personIds.Count <= 0) ? null : _personIds[0]);
		}
	}

	private Profile EffectiveProfile(string id, bool female)
	{
		if (_overrides.TryGetValue(id, out var value))
		{
			return value;
		}
		return (!_applyAll.Value || !female) ? null : _global;
	}

	private void ApplyAll()
	{
		foreach (KeyValuePair<string, TargetState> target in _targets)
		{
			target.Value.Apply(EffectiveProfile(target.Key, target.Value.IsFemale));
		}
	}

	private Profile ReadGlobal()
	{
		Profile profile = new Profile();
		profile.AnalFirm = _analFirm.Value;
		profile.AnalSpring = Clamp(_analSpring.Value, 0f, 20000f);
		profile.AnalDamper = Clamp(_analDamper.Value, 0f, 1000f);
		profile.DisableAnal = _analCollision.Value;
		profile.DisableJointB = _jointBCollision.Value;
		profile.VulvaFirm = _vulvaFirm.Value;
		profile.VulvaSpring = Clamp(_vulvaSpring.Value, 0f, 20000f);
		profile.VulvaDamper = Clamp(_vulvaDamper.Value, 0f, 1000f);
		return profile;
	}

	private void SaveProfile(Profile profile, bool personal)
	{
		if (personal)
		{
			SaveOverrides();
			ApplySelected();
			return;
		}
		_analFirm.Value = profile.AnalFirm;
		_analSpring.Value = profile.AnalSpring;
		_analDamper.Value = profile.AnalDamper;
		_analCollision.Value = profile.DisableAnal;
		_jointBCollision.Value = profile.DisableJointB;
		_vulvaFirm.Value = profile.VulvaFirm;
		_vulvaSpring.Value = profile.VulvaSpring;
		_vulvaDamper.Value = profile.VulvaDamper;
		foreach (KeyValuePair<string, TargetState> target in _targets)
		{
			if (!_overrides.ContainsKey(target.Key))
			{
				target.Value.Apply(EffectiveProfile(target.Key, target.Value.IsFemale));
			}
		}
	}

	private void ApplySelected()
	{
		if (_selectedId != null && _targets.TryGetValue(_selectedId, out var value))
		{
			value.Apply(EffectiveProfile(_selectedId, value.IsFemale));
		}
	}

	private static float Clamp(float value, float low, float high)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			return low;
		}
		return Mathf.Clamp(value, low, high);
	}

	private void LoadOverrides()
	{
		string[] array = _serializedOverrides.Value.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				string[] array2 = array[i].Split('|');
				if (array2.Length == 9)
				{
					string text = Encoding.UTF8.GetString(Convert.FromBase64String(array2[0]));
					if (!string.IsNullOrEmpty(text))
					{
						Profile profile = new Profile();
						profile.AnalFirm = array2[1] == "1";
						profile.AnalSpring = Clamp(float.Parse(array2[2], Invariant), 0f, 20000f);
						profile.AnalDamper = Clamp(float.Parse(array2[3], Invariant), 0f, 1000f);
						profile.DisableAnal = array2[4] == "1";
						profile.DisableJointB = array2[5] == "1";
						profile.VulvaFirm = array2[6] == "1";
						profile.VulvaSpring = Clamp(float.Parse(array2[7], Invariant), 0f, 20000f);
						profile.VulvaDamper = Clamp(float.Parse(array2[8], Invariant), 0f, 1000f);
						Profile value = profile;
						_overrides[text] = value;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning((object)("Skipped invalid Person override: " + ex.Message));
			}
		}
	}

	private void SaveOverrides()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, Profile> @override in _overrides)
		{
			Profile value = @override.Value;
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(';');
			}
			stringBuilder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(@override.Key))).Append('|');
			stringBuilder.Append((!value.AnalFirm) ? '0' : '1').Append('|');
			stringBuilder.Append(value.AnalSpring.ToString("R", Invariant)).Append('|');
			stringBuilder.Append(value.AnalDamper.ToString("R", Invariant)).Append('|');
			stringBuilder.Append((!value.DisableAnal) ? '0' : '1').Append('|');
			stringBuilder.Append((!value.DisableJointB) ? '0' : '1').Append('|');
			stringBuilder.Append((!value.VulvaFirm) ? '0' : '1').Append('|');
			stringBuilder.Append(value.VulvaSpring.ToString("R", Invariant)).Append('|');
			stringBuilder.Append(value.VulvaDamper.ToString("R", Invariant));
		}
		_serializedOverrides.Value = stringBuilder.ToString();
	}

	private void OnGUI()
	{
		if (!_visible.Value)
		{
			return;
		}
		RefreshDesktopDpi();
		float num = EffectiveScale();
		if (Mathf.Abs(_lastEffectiveScale - num) > 0.001f)
		{
			ref Rect window = ref _window;
			window.x *= _lastEffectiveScale / num;
			ref Rect window2 = ref _window;
			window2.y *= _lastEffectiveScale / num;
			_lastEffectiveScale = num;
		}
		float num2 = (float)Screen.width / num;
		float num3 = (float)Screen.height / num;
		EventType type = Event.current.type;
		if (_resizingWindow && (int)type == 3)
		{
			UpdateWindowResize();
		}
		SetVisibleWindowSize(num2, num3);
		float x = _window.x;
		float y = _window.y;
		_window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, num2 - _window.width));
		_window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, num3 - _window.height));
		bool flag = Mathf.Abs(x - _window.x) > 0.1f || Mathf.Abs(y - _window.y) > 0.1f;
		Matrix4x4 matrix = GUI.matrix;
		try
		{
			GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(num, num, 1f));
			Rect window3 = GUI.Window(17821, _window, (GUI.WindowFunction)DrawWindow, "", GUIStyle.none);
			_window = window3;
			if (_resizingWindow && (int)type == 1)
			{
				UpdateWindowResize();
				FinishWindowResize();
			}
			if (_resetWindowRequested)
			{
				_resetWindowRequested = false;
				_resizingWindow = false;
				_expandedWidth = 455f;
				_expandedHeight = 535f;
				_windowWidth.Value = _expandedWidth;
				_windowHeight.Value = _expandedHeight;
				_scale.Value = 1f;
				_collapsed.Value = false;
				float num4 = EffectiveScale();
				_window.x = 30f / num4;
				_window.y = 80f / num4;
				_lastEffectiveScale = num4;
				SaveWindowPosition(num4);
			}
			SetVisibleWindowSize((float)Screen.width / _lastEffectiveScale, (float)Screen.height / _lastEffectiveScale);
			if ((int)type == 1 || Mathf.Abs(_positionScale.Value - num) > 0.001f || flag)
			{
				SaveWindowPosition(_lastEffectiveScale);
			}
		}
		finally
		{
			GUI.matrix = matrix;
		}
	}

	private void SetVisibleWindowSize(float virtualWidth, float virtualHeight)
	{
		float num = Mathf.Max(120f, virtualWidth - 12f);
		float num2 = Mathf.Max(100f, virtualHeight - 20f);
		_window.width = ((!_collapsed.Value) ? Mathf.Min(_expandedWidth, num) : Mathf.Min(300f, num));
		_window.height = ((!_collapsed.Value) ? Mathf.Min(_expandedHeight, num2) : 34f);
	}

	private static Vector2 MouseScreenPosition()
	{
		Vector3 mousePosition = Input.mousePosition;
		return new Vector2(mousePosition.x, (float)Screen.height - mousePosition.y);
	}

	private void BeginWindowResize()
	{
		_resizingWindow = true;
		_resizeStartMouse = MouseScreenPosition();
		_resizeStartWidth = _window.width;
		_resizeStartHeight = _window.height;
		_resizeScale = Mathf.Max(0.65f, EffectiveScale());
	}

	private void UpdateWindowResize()
	{
		Vector2 val = MouseScreenPosition();
		float num = (val.x - _resizeStartMouse.x) / _resizeScale;
		float num2 = (val.y - _resizeStartMouse.y) / _resizeScale;
		float num3 = EffectiveScale();
		float num4 = Mathf.Min(900f, Mathf.Max(120f, (float)Screen.width / num3 - _window.x - 8f));
		float num5 = Mathf.Min(1000f, Mathf.Max(100f, (float)Screen.height / num3 - _window.y - 8f));
		_expandedWidth = Clamp(_resizeStartWidth + num, Mathf.Min(320f, num4), num4);
		_expandedHeight = Clamp(_resizeStartHeight + num2, Mathf.Min(210f, num5), num5);
	}

	private void FinishWindowResize()
	{
		_resizingWindow = false;
		if (Mathf.Abs(_windowWidth.Value - _expandedWidth) > 0.1f)
		{
			_windowWidth.Value = _expandedWidth;
		}
		if (Mathf.Abs(_windowHeight.Value - _expandedHeight) > 0.1f)
		{
			_windowHeight.Value = _expandedHeight;
		}
	}

	private void SaveWindowPosition(float scale)
	{
		if (Mathf.Abs(_window.x - _windowX.Value) > 0.1f)
		{
			_windowX.Value = _window.x;
		}
		if (Mathf.Abs(_window.y - _windowY.Value) > 0.1f)
		{
			_windowY.Value = _window.y;
		}
		if (Mathf.Abs(scale - _positionScale.Value) > 0.001f)
		{
			_positionScale.Value = scale;
		}
	}

	private float EffectiveScale()
	{
		float num = ((!_autoDpi.Value) ? 1f : Mathf.Clamp(_desktopDpi / 96f, 0.75f, 2.5f));
		float num2 = Mathf.Max(0.65f, ((float)Screen.width - 12f) / 320f);
		return Clamp(Clamp(_scale.Value, 0.65f, 1.75f) * num, 0.65f, Mathf.Min(3f, num2));
	}

	private void RefreshDesktopDpi()
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (realtimeSinceStartup < _nextDpiCheck)
		{
			return;
		}
		_nextDpiCheck = realtimeSinceStartup + 1f;
		float num = 0f;
		if (Environment.OSVersion.Platform == PlatformID.Win32NT)
		{
			try
			{
				IntPtr activeWindow = GetActiveWindow();
				if (activeWindow != IntPtr.Zero)
				{
					num = GetDpiForWindow(activeWindow);
				}
			}
			catch (EntryPointNotFoundException)
			{
			}
			catch (DllNotFoundException)
			{
			}
			if (num < 72f)
			{
				try
				{
					num = GetDpiForSystem();
				}
				catch (EntryPointNotFoundException)
				{
				}
				catch (DllNotFoundException)
				{
				}
			}
		}
		if (num < 72f || num > 480f)
		{
			num = Screen.dpi;
		}
		_dpiDetected = num >= 72f && num <= 480f;
		_desktopDpi = ((!_dpiDetected) ? 96f : num);
	}

	private void DrawWindow(int id)
	{
		float width = _window.width;
		ZeroT.UiKit.RlChrome.Backdrop(width, _window.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(width, "Anus Physics Control", _collapsed.Value);
		if ((rlButtons & 1) != 0)
		{
			_scale.Value = Clamp(_scale.Value - 0.1f, 0.65f, 1.75f);
		}
		if ((rlButtons & 2) != 0)
		{
			_scale.Value = Clamp(_scale.Value + 0.1f, 0.65f, 1.75f);
		}
		if ((rlButtons & 4) != 0)
		{
			_collapsed.Value = !_collapsed.Value;
		}
		if ((rlButtons & 8) != 0)
		{
			_visible.Value = false;
		}
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, width - 118f), 26f));
		if (_collapsed.Value)
		{
			return;
		}
		Event current = Event.current;
		if (ZeroT.UiKit.RlChrome.ResizeHandle(width, _window.height))
		{
			BeginWindowResize();
		}
		else if (_resizingWindow && ((int)current.type == 3 || (int)current.type == 1))
		{
			current.Use();
		}
		GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(width, _window.height));
		_scroll = GUILayout.BeginScrollView(_scroll, new GUILayoutOption[1] { GUILayout.Height(Mathf.Max(80f, _window.height - 52f)) });
		GUILayout.Label("F9 show/hide  |  S-/S+ scale  |  arrow resizes", new GUILayoutOption[0]);
		bool flag = GUILayout.Toggle(_applyAll.Value, "Apply to all female Persons (session mode)", new GUILayoutOption[0]);
		if (flag != _applyAll.Value)
		{
			_applyAll.Value = flag;
			ApplyAll();
		}
		if (_personIds.Count > 0)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			if (GUILayout.Button("<", new GUILayoutOption[1] { GUILayout.Width(28f) }))
			{
				SelectPerson(-1);
			}
			GUILayout.Label("Person: " + _selectedId, new GUILayoutOption[1] { GUILayout.MinWidth(Mathf.Max(150f, width - 150f)) });
			if (GUILayout.Button(">", new GUILayoutOption[1] { GUILayout.Width(28f) }))
			{
				SelectPerson(1);
			}
			GUILayout.EndHorizontal();
			bool flag2 = _overrides.ContainsKey(_selectedId);
			bool flag3 = GUILayout.Toggle(flag2, "Use individual settings for selected Person", new GUILayoutOption[0]);
			if (flag2 != flag3)
			{
				if (flag3)
				{
					_overrides[_selectedId] = _global.Copy();
				}
				else
				{
					_overrides.Remove(_selectedId);
				}
				_editBuffers.Clear();
				SaveOverrides();
				ApplySelected();
			}
		}
		else
		{
			GUILayout.Label("No Person atom detected yet.", new GUILayoutOption[0]);
		}
		bool flag4 = _selectedId != null && _overrides.ContainsKey(_selectedId);
		Profile profile = ((!flag4) ? _global : _overrides[_selectedId]);
		GUILayout.Label((!flag4) ? "Global settings" : "Selected Person settings", new GUILayoutOption[0]);
		bool flag5 = false;
		flag5 |= Toggle("Firm Anal Physics", ref profile.AnalFirm);
		flag5 |= Slider("Anal Firmness Spring", "analSpring", ref profile.AnalSpring, 20000f);
		flag5 |= Slider("Anal Firmness Damper", "analDamper", ref profile.AnalDamper, 1000f);
		flag5 |= Toggle("Disable Anal Collisions (No Gape)", ref profile.DisableAnal);
		flag5 |= Toggle("Disable JointB / Pelvic Collisions", ref profile.DisableJointB);
		flag5 |= Toggle("Firm Vulva/Labia Physics", ref profile.VulvaFirm);
		flag5 |= Slider("Vulva Firmness Spring", "vulvaSpring", ref profile.VulvaSpring, 20000f);
		if (flag5 | Slider("Vulva Firmness Damper", "vulvaDamper", ref profile.VulvaDamper, 1000f))
		{
			SaveProfile(profile, flag4);
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label("Manual UI scale: " + Clamp(_scale.Value, 0.65f, 1.75f).ToString("0.00", Invariant), new GUILayoutOption[1] { GUILayout.Width(150f) });
		float num = GUILayout.HorizontalSlider(_scale.Value, 0.65f, 1.75f, new GUILayoutOption[0]);
		if (Mathf.Abs(num - _scale.Value) > 0.005f)
		{
			_scale.Value = num;
		}
		GUILayout.EndHorizontal();
		bool flag6 = GUILayout.Toggle(_autoDpi.Value, "Automatic DPI scaling (" + ((!_dpiDetected) ? "96 DPI fallback" : (_desktopDpi.ToString("0", Invariant) + " DPI")) + ")", new GUILayoutOption[0]);
		if (flag6 != _autoDpi.Value)
		{
			_autoDpi.Value = flag6;
		}
		GUILayout.Label("Effective UI scale: " + EffectiveScale().ToString("0.00", Invariant), new GUILayoutOption[0]);
		if (GUILayout.Button("Reset Desktop Window Layout", new GUILayoutOption[1] { GUILayout.Width(220f) }))
		{
			_resetWindowRequested = true;
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	private void SelectPerson(int delta)
	{
		int num = _personIds.IndexOf(_selectedId);
		if (num < 0)
		{
			num = 0;
		}
		_selectedId = _personIds[(num + delta + _personIds.Count) % _personIds.Count];
		_editBuffers.Clear();
	}

	private static bool Toggle(string label, ref bool value)
	{
		bool flag = GUILayout.Toggle(value, label, new GUILayoutOption[0]);
		if (flag == value)
		{
			return false;
		}
		value = flag;
		return true;
	}

	private bool Slider(string label, string key, ref float value, float maximum)
	{
		string text = (_selectedId ?? "global") + ":" + key + ":" + ((_selectedId == null || !_overrides.ContainsKey(_selectedId)) ? "global" : "personal");
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		float num = Mathf.Clamp(_window.width * 0.37f, 148f, 190f);
		float num2 = Mathf.Max(75f, _window.width - num - 116f);
		GUILayout.Label(label, new GUILayoutOption[1] { GUILayout.Width(num) });
		float num3 = GUILayout.HorizontalSlider(value, 0f, maximum, new GUILayoutOption[1] { GUILayout.Width(num2) });
		if (Mathf.Abs(num3 - value) > 0.01f)
		{
			value = num3;
			_editBuffers[text] = value.ToString("0.##", Invariant);
			GUILayout.TextField(_editBuffers[text], new GUILayoutOption[1] { GUILayout.Width(68f) });
			GUILayout.EndHorizontal();
			return true;
		}
		if (!_editBuffers.TryGetValue(text, out var value2))
		{
			value2 = value.ToString("0.##", Invariant);
		}
		string text2 = "number:" + text;
		GUI.SetNextControlName(text2);
		string text3 = GUILayout.TextField(value2, new GUILayoutOption[1] { GUILayout.Width(68f) });
		_editBuffers[text] = text3;
		GUILayout.EndHorizontal();
		if ((GUI.GetNameOfFocusedControl() != text2 || (Event.current.isKey && (int)Event.current.keyCode == 13)) && float.TryParse(text3, NumberStyles.Float, Invariant, out var result))
		{
			result = Clamp(result, 0f, maximum);
			if (Mathf.Abs(result - value) > 0.001f)
			{
				value = result;
				_editBuffers[text] = value.ToString("0.##", Invariant);
				return true;
			}
		}
		return false;
	}

	private void OnDisable()
	{
		RestoreAll();
	}

	private void OnDestroy()
	{
		RestoreAll();
	}

	private void RestoreAll()
	{
		foreach (TargetState value in _targets.Values)
		{
			value.Restore();
		}
		_targets.Clear();
	}
}
