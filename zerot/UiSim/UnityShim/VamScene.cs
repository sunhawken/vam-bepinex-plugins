using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Behavioural model of the parts of VaM that headless plugins touch: storables with callbacks, atoms with rigidbodies,
// free controllers, morph banks, collision triggers. Hand-written (StubGen skips every type that exists here); extend
// it when a plugin needs more. Scene building helpers live in SimWorld below.

namespace UnityEngine
{
	public partial struct Bounds { public Vector3 center { get; set; } public Vector3 size { get; set; } public Vector3 extents => size * 0.5f; public Bounds(Vector3 c, Vector3 s) { center = c; size = s; } }
	public enum RigidbodyInterpolation { None = 0, Interpolate = 1, Extrapolate = 2 }
	public enum CollisionDetectionMode { Discrete = 0, Continuous = 1, ContinuousDynamic = 2 }
	public enum PhysicMaterialCombine { Average = 0, Minimum = 1, Multiply = 2, Maximum = 3 }
	public enum ForceMode { Force = 0, Impulse = 1, VelocityChange = 2, Acceleration = 5 }

	public partial class PhysicMaterial : Object
	{
		public float dynamicFriction { get; set; }
		public float staticFriction { get; set; }
		public float bounciness { get; set; }
		public PhysicMaterialCombine frictionCombine { get; set; }
		public PhysicMaterialCombine bounceCombine { get; set; }
		public PhysicMaterial() { }
		public PhysicMaterial(string n) { name = n; }
	}

	public partial class Collider : Component
	{
		public bool enabled { get; set; } = true;
		public bool isTrigger { get; set; }
		public PhysicMaterial sharedMaterial { get; set; }
		public PhysicMaterial material { get => sharedMaterial; set => sharedMaterial = value; }
		public Rigidbody attachedRigidbody => gameObject.GetComponent<Rigidbody>();
		public Vector3 ClosestPoint(Vector3 p) => transform.position;
		public Bounds bounds { get; set; }
	}

	public sealed partial class Rigidbody : Component
	{
		public float mass { get; set; } = 1f;
		public float drag { get; set; }
		public float angularDrag { get; set; } = 0.05f;
		public Vector3 velocity { get; set; }
		public Vector3 angularVelocity { get; set; }
		public Vector3 position { get => transform.position; set => transform.position = value; }
		public Quaternion rotation { get; set; } = Quaternion.identity;
		public bool isKinematic { get; set; }
		public bool detectCollisions { get; set; } = true;
		public int solverIterations { get; set; } = 6;
		public int solverVelocityIterations { get; set; } = 1;
		public RigidbodyInterpolation interpolation { get; set; }
		public float maxAngularVelocity { get; set; } = 7f;
		public CollisionDetectionMode collisionDetectionMode { get; set; }
		public float maxDepenetrationVelocity { get; set; } = 10f;
		public Vector3 worldCenterOfMass => transform.position;
		public Vector3 inertiaTensor { get; set; } = new Vector3(0.02f, 0.02f, 0.02f);
		public Vector3 SimForceSum, SimTorqueSum; public int SimForceCalls, SimTorqueCalls;
		public void AddForce(Vector3 force, ForceMode mode) { SimForceSum = SimForceSum + force; SimForceCalls++; velocity = velocity + force * (1f / Mathf.Max(0.0001f, mass)); }
		public void AddTorque(Vector3 torque, ForceMode mode) { SimTorqueSum = SimTorqueSum + torque; SimTorqueCalls++; angularVelocity = angularVelocity + torque; }
		public Vector3 GetPointVelocity(Vector3 p) => velocity;
	}
}

namespace SimpleJSON { }

public abstract partial class JSONStorableParam
{
	public enum StoreType { Appearance = 0, Physical = 1, Any = 2, Full = 3 }
	public string name;
	public StoreType storeType = StoreType.Any;
	public bool isStorable = true;
	public bool isRestorable = true;
	public JSONStorable storable;
	protected JSONStorableParam() { }
}

public partial class JSONStorableFloat : JSONStorableParam
{
	public delegate void SetFloatCallback(float val);
	public delegate void SetJSONFloatCallback(JSONStorableFloat jf);
	public SetFloatCallback setCallbackFunction;
	public SetJSONFloatCallback setJSONCallbackFunction;
	public float defaultVal { get; set; }
	public float min { get; set; }
	public float max { get; set; }
	public bool constrained { get; set; }
	private float _val;
	public JSONStorableFloat() { }
	public JSONStorableFloat(string paramName, float startingValue, SetFloatCallback callback, float minValue, float maxValue, bool constrain = true, bool interactable = true)
	{ name = paramName; _val = startingValue; defaultVal = startingValue; setCallbackFunction = callback; min = minValue; max = maxValue; constrained = constrain; }
	public JSONStorableFloat(string paramName, float startingValue, SetJSONFloatCallback callback, float minValue, float maxValue, bool constrain = true, bool interactable = true)
	{ name = paramName; _val = startingValue; defaultVal = startingValue; setJSONCallbackFunction = callback; min = minValue; max = maxValue; constrained = constrain; }
	public JSONStorableFloat(string paramName, float startingValue, float minValue, float maxValue, bool constrain = true, bool interactable = true)
	{ name = paramName; _val = startingValue; defaultVal = startingValue; min = minValue; max = maxValue; constrained = constrain; }
	public float val
	{
		get => _val;
		set
		{
			float v = constrained ? Mathf.Clamp(value, min, max) : value;
			if (v == _val) return;
			_val = v;
			setCallbackFunction?.Invoke(v);
			setJSONCallbackFunction?.Invoke(this);
		}
	}
	public float valNoCallback { get => _val; set { _val = constrained ? Mathf.Clamp(value, min, max) : value; } }
	public void SetValToDefault() { val = defaultVal; }
}

public partial class JSONStorableBool : JSONStorableParam
{
	public delegate void SetBoolCallback(bool val);
	public delegate void SetJSONBoolCallback(JSONStorableBool jb);
	public SetBoolCallback setCallbackFunction;
	public SetJSONBoolCallback setJSONCallbackFunction;
	public bool defaultVal { get; set; }
	private bool _val;
	public JSONStorableBool() { }
	public JSONStorableBool(string paramName, bool startingValue) { name = paramName; _val = startingValue; defaultVal = startingValue; }
	public JSONStorableBool(string paramName, bool startingValue, SetBoolCallback callback) { name = paramName; _val = startingValue; defaultVal = startingValue; setCallbackFunction = callback; }
	public JSONStorableBool(string paramName, bool startingValue, SetJSONBoolCallback callback) { name = paramName; _val = startingValue; defaultVal = startingValue; setJSONCallbackFunction = callback; }
	public bool val
	{
		get => _val;
		set { if (value == _val) return; _val = value; setCallbackFunction?.Invoke(value); setJSONCallbackFunction?.Invoke(this); }
	}
	public bool valNoCallback { get => _val; set { _val = value; } }
	public void SetValToDefault() { val = defaultVal; }
}

public partial class JSONStorableString : JSONStorableParam
{
	public delegate void SetStringCallback(string val);
	public delegate void SetJSONStringCallback(JSONStorableString js);
	public SetStringCallback setCallbackFunction;
	public SetJSONStringCallback setJSONCallbackFunction;
	public string defaultVal;
	private string _val;
	public JSONStorableString() { }
	public JSONStorableString(string paramName, string startingValue) { name = paramName; _val = startingValue; defaultVal = startingValue; }
	public JSONStorableString(string paramName, string startingValue, SetStringCallback callback) { name = paramName; _val = startingValue; defaultVal = startingValue; setCallbackFunction = callback; }
	public string val
	{
		get => _val;
		set { if (value == _val) return; _val = value; setCallbackFunction?.Invoke(value); setJSONCallbackFunction?.Invoke(this); }
	}
	public string valNoCallback { get => _val; set { _val = value; } }
	public void SetValToDefault() { val = defaultVal; }
}

public partial class JSONStorableUrl : JSONStorableString
{
	public JSONStorableUrl() { }
	public JSONStorableUrl(string paramName, string startingValue) : base(paramName, startingValue) { }
	public JSONStorableUrl(string paramName, string startingValue, SetStringCallback callback) : base(paramName, startingValue, callback) { }
	public JSONStorableUrl(string paramName, string startingValue, SetStringCallback callback, string filter, bool forceCallbackOnSet = false) : base(paramName, startingValue, callback) { }
	public JSONStorableUrl(string paramName, string startingValue, string filter) : base(paramName, startingValue) { }
	public JSONStorableUrl(string paramName, string startingValue, string filter, string path) : base(paramName, startingValue) { }
	public JSONStorableUrl(string paramName, string startingValue, SetStringCallback callback, string filter, string path) : base(paramName, startingValue, callback) { }
}

public partial class JSONStorableStringChooser : JSONStorableParam
{
	public delegate void SetStringCallback(string val);
	public delegate void SetJSONStringCallback(JSONStorableStringChooser jsc);
	public SetStringCallback setCallbackFunction;
	public SetJSONStringCallback setJSONCallbackFunction;
	public List<string> choices { get; set; } = new List<string>();
	public string defaultVal;
	public string label { get; set; }
	private string _val;
	public JSONStorableStringChooser() { }
	public JSONStorableStringChooser(string paramName, List<string> options, string startingValue, string label) : this(paramName, options, startingValue, label, (SetStringCallback)null) { }
	public JSONStorableStringChooser(string paramName, List<string> options, string startingValue, string label, SetStringCallback callback)
	{ name = paramName; choices = options ?? new List<string>(); _val = startingValue; defaultVal = startingValue; this.label = label; setCallbackFunction = callback; }
	public JSONStorableStringChooser(string paramName, List<string> options, string startingValue, string label, SetJSONStringCallback callback)
	{ name = paramName; choices = options ?? new List<string>(); _val = startingValue; defaultVal = startingValue; this.label = label; setJSONCallbackFunction = callback; }
	public string val
	{
		get => _val;
		set { if (value == _val) return; _val = value; setCallbackFunction?.Invoke(value); setJSONCallbackFunction?.Invoke(this); }
	}
	public string valNoCallback { get => _val; set { _val = value; } }
	public void SetValToDefault() { val = defaultVal; }
}

public partial class JSONStorableAction : JSONStorableParam
{
	public delegate void ActionCallback();
	public ActionCallback actionCallback;
	public JSONStorableAction() { }
	public JSONStorableAction(string paramName, ActionCallback callback) { name = paramName; actionCallback = callback; }
}

public partial class JSONStorable : MonoBehaviour
{
	public Atom containingAtom;
	public string storeId { get; set; }
	public string overrideId;
	public bool exclude;
	public bool onlyStoreIfActive;
	public Transform UITransform;
	private readonly Dictionary<string, JSONStorableFloat> floats = new Dictionary<string, JSONStorableFloat>();
	private readonly Dictionary<string, JSONStorableBool> bools = new Dictionary<string, JSONStorableBool>();
	private readonly Dictionary<string, JSONStorableString> strings = new Dictionary<string, JSONStorableString>();
	private readonly Dictionary<string, JSONStorableStringChooser> choosers = new Dictionary<string, JSONStorableStringChooser>();
	private readonly Dictionary<string, JSONStorableAction> actions = new Dictionary<string, JSONStorableAction>();

	public void RegisterFloat(JSONStorableFloat p) { floats[p.name] = p; p.storable = this; }
	public void RegisterBool(JSONStorableBool p) { bools[p.name] = p; p.storable = this; }
	public void RegisterString(JSONStorableString p) { strings[p.name] = p; p.storable = this; }
	public void RegisterStringChooser(JSONStorableStringChooser p) { choosers[p.name] = p; p.storable = this; }
	public void RegisterAction(JSONStorableAction p) { actions[p.name] = p; p.storable = this; }
	private readonly Dictionary<string, JSONStorableUrl> urls = new Dictionary<string, JSONStorableUrl>();
	public void RegisterUrl(JSONStorableUrl p) { urls[p.name] = p; strings[p.name] = p; p.storable = this; }
	public JSONStorableUrl GetUrlJSONParam(string n) { JSONStorableUrl p; return urls.TryGetValue(n, out p) ? p : null; }
	public void DeregisterFloat(JSONStorableFloat p) { floats.Remove(p.name); }
	public void DeregisterBool(JSONStorableBool p) { bools.Remove(p.name); }
	public void DeregisterString(JSONStorableString p) { strings.Remove(p.name); }
	public void DeregisterStringChooser(JSONStorableStringChooser p) { choosers.Remove(p.name); }
	public void DeregisterUrl(JSONStorableUrl p) { urls.Remove(p.name); strings.Remove(p.name); }
	public void DeregisterAction(JSONStorableAction p) { actions.Remove(p.name); }
	public List<string> GetUrlParamNames() => new List<string>(urls.Keys);
	public JSONStorableFloat GetFloatJSONParam(string n) { JSONStorableFloat p; return floats.TryGetValue(n, out p) ? p : null; }
	public JSONStorableBool GetBoolJSONParam(string n) { JSONStorableBool p; return bools.TryGetValue(n, out p) ? p : null; }
	public JSONStorableString GetStringJSONParam(string n) { JSONStorableString p; return strings.TryGetValue(n, out p) ? p : null; }
	public JSONStorableStringChooser GetStringChooserJSONParam(string n) { JSONStorableStringChooser p; return choosers.TryGetValue(n, out p) ? p : null; }
	public JSONStorableAction GetAction(string n) { JSONStorableAction p; return actions.TryGetValue(n, out p) ? p : null; }
	public List<string> GetFloatParamNames() => new List<string>(floats.Keys);
	public List<string> GetBoolParamNames() => new List<string>(bools.Keys);
	public List<string> GetStringParamNames() => new List<string>(strings.Keys);
	public List<string> GetStringChooserParamNames() => new List<string>(choosers.Keys);
	public virtual void SetFloatParamValue(string n, float v) { var p = GetFloatJSONParam(n); if (p != null) p.val = v; }
	public virtual void SetBoolParamValue(string n, bool v) { var p = GetBoolJSONParam(n); if (p != null) p.val = v; }
	public virtual void SetStringChooserParamValue(string n, string v) { var p = GetStringChooserJSONParam(n); if (p != null) p.val = v; }
	public virtual void CallAction(string n) { var a = GetAction(n); a?.actionCallback?.Invoke(); }
	public virtual void InitUI() { }
	public virtual SimpleJSON.JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false) => new SimpleJSON.JSONClass();
	// like VaM: every registered param present in the JSON is set (callbacks fire); missing ones go back to their default when asked
	public virtual void RestoreFromJSON(SimpleJSON.JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, SimpleJSON.JSONArray presetAtoms = null, bool setMissingToDefault = true)
	{
		if (jc == null) return;
		foreach (var kv in new List<KeyValuePair<string, JSONStorableFloat>>(floats)) { float v; if (jc[kv.Key] != null && jc[kv.Key].ToString().Trim('"') != "" && float.TryParse(jc[kv.Key].ToString().Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) kv.Value.val = v; else if (setMissingToDefault) kv.Value.val = kv.Value.defaultVal; }
		foreach (var kv in new List<KeyValuePair<string, JSONStorableBool>>(bools)) { if (jc[kv.Key] != null) { bool b; if (bool.TryParse(jc[kv.Key].ToString().Trim('"'), out b)) kv.Value.val = b; } else if (setMissingToDefault) kv.Value.val = kv.Value.defaultVal; }
		foreach (var kv in new List<KeyValuePair<string, JSONStorableString>>(strings)) { if (jc[kv.Key] != null) kv.Value.val = jc[kv.Key].ToString().Trim('"'); else if (setMissingToDefault) kv.Value.val = kv.Value.defaultVal; }
		foreach (var kv in new List<KeyValuePair<string, JSONStorableStringChooser>>(choosers)) { if (jc[kv.Key] != null) kv.Value.val = jc[kv.Key].ToString().Trim('"'); else if (setMissingToDefault) kv.Value.val = kv.Value.defaultVal; }
	}
	public virtual void LateRestoreFromJSON(SimpleJSON.JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true) { }
}

public partial class FreeControllerV3 : JSONStorable
{
	public Transform control;
	public Transform followWhenOff;
	public Rigidbody followWhenOffRB { get; set; }
	public bool globalCollisionEnabled { get; set; } = true;
	public enum PositionState { Off, On, Following, ParentLink, PhysicsLink, Hold, Lock }
	public enum RotationState { Off, On, Following, ParentLink, PhysicsLink, Hold, Lock }
	public PositionState currentPositionState { get; set; } = PositionState.On;
	public RotationState currentRotationState { get; set; } = RotationState.On;
	private bool _hidden;
	public int hiddenWrites;   // how often the scene's code assigned .hidden (changed or not): what a per-frame loop costs
	public bool hidden { get { return _hidden; } set { hiddenWrites++; _hidden = value; } }
	public bool highlighted { get; set; }
	public void ResetControl() { }
	public void SelectLinkToRigidbody(Rigidbody rb) { }
}

public partial class ForceReceiver : MonoBehaviour { }

public partial class Atom : JSONStorable
{
	public ForceReceiver[] forceReceivers { get; set; } = new ForceReceiver[0];
	public string type;
	public string category;
	public void Remove() { SuperController.singleton.RemoveAtom(this); }
	public string uid { get; set; }
	public bool on { get; set; } = true;
	public bool hidden { get; set; }
	public bool destroyed;
	public bool collisionEnabled { get; set; } = true;
	public bool tempFreezePhysics { get; set; }
	public bool isPhysicsFrozen;
	public FreeControllerV3[] freeControllers { get; set; } = new FreeControllerV3[0];
	public Rigidbody[] rigidbodies { get; set; } = new Rigidbody[0];
	public FreeControllerV3 mainController;
	public readonly Dictionary<string, JSONStorable> storables = new Dictionary<string, JSONStorable>();
	public readonly List<string> physicsResets = new List<string>();
	public JSONStorable GetStorableByID(string id) { JSONStorable s; return storables.TryGetValue(id, out s) ? s : null; }
	public List<string> GetStorableIDs() => new List<string>(storables.Keys);
	public bool RegisterAdditionalStorable(JSONStorable js) { storables[js.storeId ?? js.name] = js; return true; }
	public void UnregisterAdditionalStorable(JSONStorable js) { storables.Remove(js.storeId ?? js.name); }
	public void ResetPhysics(bool fullReset, bool fullResetControls) { physicsResets.Add("ResetPhysics(" + fullReset + "," + fullResetControls + ")"); foreach (var rb in rigidbodies) if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
	public void ResetRigidbodies() { physicsResets.Add("ResetRigidbodies"); }
	public void ResetPhysical() { physicsResets.Add("ResetPhysical"); }
}

public partial class SuperController : MonoBehaviour
{
	public delegate void OnAtomAdded(Atom atom);
	public delegate void OnAtomRemoved(Atom atom);
	public delegate void OnSceneLoaded();
	public delegate void OnAtomUIDsChanged(List<string> atomUIDs);
	public delegate void OnAtomUIDRename(string oldName, string newName);
	public static SuperController singleton { get; set; }
	public bool isLoading { get; set; }
	public bool freezeAnimation { get; set; }
	public bool isOVR, isOpenVR;
	public OnAtomAdded onAtomAddedHandlers;
	public OnAtomRemoved onAtomRemovedHandlers;
	public OnSceneLoaded onSceneLoadedHandlers;
	public OnAtomUIDsChanged onAtomUIDsChangedHandlers;
	public readonly List<Atom> atoms = new List<Atom>();
	public static List<string> Messages = new List<string>();
	public static List<string> Errors = new List<string>();
	public Atom selectedAtom;
	public static readonly Dictionary<string, string> SimSaved = new Dictionary<string, string>();   // what SaveJSON wrote / what LoadJSON reads
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public void SaveJSON(SimpleJSON.JSONClass jc, string saveName) { SimSaved[saveName] = jc == null ? "" : jc.ToString(); }
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public SimpleJSON.JSONNode LoadJSON(string saveName) { string t; return SimSaved.TryGetValue(saveName, out t) ? SimpleJSON.JSON.Parse(t) : null; }
	public int HardResetCalls;
	public bool SimAddFails;
	public readonly List<string> SimAddLog = new List<string>();   // "add Person Person", "remove Person" in the order VaM was asked
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public void HardReset() { HardResetCalls++; foreach (var a in atoms.ToArray()) if (a.uid != "CoreControl") RemoveAtom(a); }
	// like VaM: the atom exists a few frames after the call
	public System.Collections.IEnumerator AddAtomByType(string type, string useuid)
	{
		SimAddLog.Add("add " + type + " " + useuid);
		yield return null; yield return null; yield return null;
		if (SimAddFails) yield break;
		if (type == "Person") { SimWorld.NewPerson(this, useuid, false); yield break; }
		var go = new GameObject(useuid); var at = go.AddComponent<Atom>(); at.type = type; at.uid = at.name = useuid; at.containingAtom = at; AddAtom(at);
	}
	public bool targetShow;
	public int allControllersCalls;   // how often something asked for the full list (it is rebuilt on every call in VaM)
	public bool GetTargetShow() { return targetShow; }
	public List<FreeControllerV3> GetAllFreeControllers()
	{
		allControllersCalls++;
		var l = new List<FreeControllerV3>();
		foreach (var a in atoms) if (a.freeControllers != null) foreach (var c in a.freeControllers) l.Add(c);
		return l;
	}
	public List<Atom> GetAtoms() => atoms;
	public List<string> GetAtomUIDs() => atoms.ConvertAll(a => a.uid);
	public Atom GetAtomByUid(string uid) => atoms.Find(a => a.uid == uid);
	public Atom GetSelectedAtom() => selectedAtom;
	public void AddCanvas(Canvas c) { }
	public void RemoveCanvas(Canvas c) { }
	public static void LogMessage(string msg) { Messages.Add(msg); }
	public static void LogError(string err) { Errors.Add(err); }
	public void AddAtom(Atom a) { atoms.Add(a); onAtomAddedHandlers?.Invoke(a); onAtomUIDsChangedHandlers?.Invoke(GetAtomUIDs()); }
	public void RemoveAtom(Atom a) { SimAddLog.Add("remove " + a.uid); atoms.Remove(a); a.destroyed = true; onAtomRemovedHandlers?.Invoke(a); }
	// Scene load: atoms are replaced; plugins see isLoading = true meanwhile, then the scene-loaded event.
	public void BeginLoad() { isLoading = true; }
	public void EndLoad() { isLoading = false; onSceneLoadedHandlers?.Invoke(); }
}

public partial class DAZCharacter : MonoBehaviour { public bool isMale; }

public partial class DAZMorphBank : MonoBehaviour
{
	public List<DAZMorph> morphs { get; set; } = new List<DAZMorph>();
}

public partial class DAZMorph
{
	public string displayName, morphName;
	public string uid { get; set; }
	public float min = 0f, max = 1f, startValue;
	public JSONStorableFloat jsonFloat;
	public bool wasTouched;
	public DAZMorph() { }
	public DAZMorph(string name, float min, float max, float def = 0f)
	{
		displayName = morphName = uid = name; this.min = min; this.max = max; startValue = def;
		jsonFloat = new JSONStorableFloat(name, def, min, max, false);
	}
	public float morphValue { get => jsonFloat.val; set { wasTouched = true; jsonFloat.val = value; } }
	public float morphValueAdjustLimits
	{
		get => jsonFloat.val;
		set { if (value < min) min = value; if (value > max) max = value; wasTouched = true; jsonFloat.val = value; }
	}
	public void SetDefaultValue() { jsonFloat.val = jsonFloat.defaultVal; }
}

public partial class GenerateDAZMorphsControlUI : MonoBehaviour
{
	public List<DAZMorph> all = new List<DAZMorph>();
	public DAZMorph GetMorphByDisplayName(string name) => all.Find(m => m.displayName == name);
	public DAZMorph GetMorphByUid(string uid) => all.Find(m => m.uid == uid);
	public List<string> GetMorphDisplayNames() => all.ConvertAll(m => m.displayName);
}

public partial class DAZCharacterSelector : JSONStorable
{
	public enum Gender { None = 0, Male = 1, Female = 2, Both = 3 }
	public Gender gender { get; set; } = Gender.Female;
	public DAZCharacter selectedCharacter { get; set; }
	public GenerateDAZMorphsControlUI morphsControlUI { get; set; }
	public DAZMorphBank morphBank1 { get; set; }
	public DAZMorphBank morphBank2 { get; set; }
	public DAZMorphBank morphBank3 { get; set; }
	public bool RefreshRuntimeMorphs() { return true; }
}

public partial class CollisionTriggerEventHandler : MonoBehaviour
{
	public Dictionary<Collider, bool> collidingWithDictionary = new Dictionary<Collider, bool>();
}

public partial class CollisionTrigger : MonoBehaviour { }

// ---------------------------------------------------------------- scene building for scenarios

public static class SimWorld
{
	public static readonly string[] Bodies =
	{
		"hip", "pelvis", "abdomen", "abdomen2", "chest", "neck", "head", "lKneeLink", "rKneeLink", "lForeArm", "rForeArm",
		"lIndex1", "rIndex1", "lMid1", "rMid1", "lRing1", "rRing1", "lPinky1", "rPinky1", "lBigToe", "rBigToe", "lSmallToe1", "rSmallToe1",
		"lShldr", "rShldr", "lArm", "rArm", "lHand", "rHand", "lThigh", "rThigh", "lShin", "rShin", "lFoot", "rFoot", "lToe", "rToe",
		"LGlute", "RGlute", "lPectoral", "rPectoral", "Gen1", "Gen2", "Gen3", "labia", "Testes"
	};

	// name of the controller -> VaM default mass of its rigidbody
	public static readonly Dictionary<string, float> ControllerMass = new Dictionary<string, float>
	{
		{ "headControl", 1f }, { "chestControl", 2f }, { "abdomen2Control", 2f }, { "abdomenControl", 2f }, { "pelvisControl", 2f }, { "hipControl", 4f },
		{ "lArmControl", 0.6f }, { "rArmControl", 0.6f }, { "lElbowControl", 0.3f }, { "rElbowControl", 0.3f }, { "lHandControl", 1f }, { "rHandControl", 1f },
		{ "lThighControl", 2f }, { "rThighControl", 2f }, { "lKneeControl", 1f }, { "rKneeControl", 1f }, { "lFootControl", 1f }, { "rFootControl", 1f },
		{ "lToeControl", 0.1f }, { "rToeControl", 0.1f }, { "penisBaseControl", 0.2f }, { "penisMidControl", 0.2f }, { "penisTipControl", 0.2f }, { "testesControl", 0.1f }
	};

	public static readonly string[] GenitalMorphs = { "Labia minora-size", "Labia minora-style1", "Labia minora-exstrophy", "Labia majora-relaxation", "Gen_Innie", "Gens In - Out" };

	public static SuperController NewSuperController()
	{
		var go = new GameObject("SuperController");
		var sc = go.AddComponent<SuperController>();
		SuperController.singleton = sc;
		SuperController.Messages.Clear();
		SuperController.Errors.Clear();
		return sc;
	}

	private static GameObject Child(GameObject parent, string name)
	{
		var go = new GameObject(name);
		go.transform.SetParent(parent.transform, false);
		return go;
	}

	public static Atom NewPerson(SuperController sc, string uid, bool male, int extraMorphs = 200)
	{
		var go = new GameObject(uid);
		var atom = go.AddComponent<Atom>();
		atom.type = "Person"; atom.category = "People"; atom.uid = uid; atom.name = uid;
		atom.containingAtom = atom;
		var bodies = new List<Rigidbody>();
		var rbByName = new Dictionary<string, Rigidbody>();
		foreach (var n in Bodies)
		{
			var b = Child(go, n);
			var rb = b.AddComponent<Rigidbody>();
			var col = b.AddComponent<Collider>();
			rb.mass = 1f; rb.drag = 0.1f; rb.angularDrag = 0.05f;
			bodies.Add(rb); rbByName[n] = rb;
		}
		var rbForController = new Dictionary<string, string>
		{
			{ "headControl", "head" }, { "chestControl", "chest" }, { "abdomen2Control", "abdomen2" }, { "abdomenControl", "abdomen" }, { "pelvisControl", "pelvis" }, { "hipControl", "hip" },
			{ "lArmControl", "lShldr" }, { "rArmControl", "rShldr" }, { "lElbowControl", "lArm" }, { "rElbowControl", "rArm" }, { "lHandControl", "lHand" }, { "rHandControl", "rHand" },
			{ "lThighControl", "lThigh" }, { "rThighControl", "rThigh" }, { "lKneeControl", "lShin" }, { "rKneeControl", "rShin" }, { "lFootControl", "lFoot" }, { "rFootControl", "rFoot" },
			{ "lToeControl", "lToe" }, { "rToeControl", "rToe" }, { "penisBaseControl", "Gen1" }, { "penisMidControl", "Gen2" }, { "penisTipControl", "Gen3" }, { "testesControl", "Testes" }
		};
		var controllers = new List<FreeControllerV3>();
		var main = Child(go, "control").AddComponent<FreeControllerV3>();
		main.name = "control"; main.control = main.transform; main.containingAtom = atom;
		controllers.Add(main);
		foreach (var kv in ControllerMass)
		{
			if (!male && (kv.Key.StartsWith("penis") || kv.Key == "testesControl")) continue;
			var fc = Child(go, kv.Key).AddComponent<FreeControllerV3>();
			fc.name = kv.Key; fc.control = fc.transform; fc.containingAtom = atom;
			fc.followWhenOffRB = rbByName[rbForController[kv.Key]];
			fc.followWhenOffRB.mass = kv.Value;
			fc.storeId = kv.Key; atom.storables[kv.Key] = fc;   // VaM registers every controller as a storable under its own name
			fc.RegisterFloat(new JSONStorableFloat("mass", kv.Value, (v) => { fc.followWhenOffRB.mass = v; }, 0.001f, 20f, false));
			controllers.Add(fc);
		}
		atom.freeControllers = controllers.ToArray();
		atom.mainController = main;
		atom.rigidbodies = bodies.ToArray();
		var frs = new List<ForceReceiver>();
		foreach (var rbx in bodies) frs.Add(rbx.gameObject.AddComponent<ForceReceiver>());
		atom.forceReceivers = frs.ToArray();
		var skin = Child(go, "skin").AddComponent<JSONStorable>();
		skin.storeId = "skin"; skin.containingAtom = atom;
		skin.RegisterFloat(new JSONStorableFloat("Gloss", 5f, 0f, 8f, true));
		skin.RegisterFloat(new JSONStorableFloat("Gloss Texture Offset", 0.6f, -1f, 1f, true));
		atom.storables["skin"] = skin;
		// geometry: gender + morphs
		var geo = Child(go, "geometry").AddComponent<DAZCharacterSelector>();
		geo.storeId = "geometry"; geo.containingAtom = atom;
		geo.gender = male ? DAZCharacterSelector.Gender.Male : DAZCharacterSelector.Gender.Female;
		geo.selectedCharacter = Child(go, "character").AddComponent<DAZCharacter>();
		geo.selectedCharacter.isMale = male;
		geo.morphsControlUI = Child(go, "morphsUI").AddComponent<GenerateDAZMorphsControlUI>();
		geo.morphBank1 = Child(go, "bank1").AddComponent<DAZMorphBank>();
		geo.morphBank2 = Child(go, "bank2").AddComponent<DAZMorphBank>();
		geo.morphBank3 = Child(go, "bank3").AddComponent<DAZMorphBank>();
		foreach (var m in GenitalMorphs) AddMorph(geo, m, -1f, 3f);
		for (int i = 0; i < extraMorphs; i++) AddMorph(geo, "Morph" + i, 0f, 1f);
		atom.storables["geometry"] = geo;
		// labia trigger
		var trig = Child(go, "LabiaTrigger");
		trig.AddComponent<CollisionTrigger>();
		Child(trig, "handler").AddComponent<CollisionTriggerEventHandler>();
		sc.AddAtom(atom);
		return atom;
	}

	public static DAZMorph AddMorphUid(DAZCharacterSelector geo, string uid, float min, float max)
	{
		var m = new DAZMorph(uid, min, max, 0f);
		geo.morphsControlUI.all.Add(m);
		geo.morphBank1.morphs.Add(m);
		return m;
	}

	public static DAZMorph AddMorph(DAZCharacterSelector geo, string name, float min, float max, float def = 0f)
	{
		var m = new DAZMorph(name, min, max, def);
		geo.morphsControlUI.all.Add(m);
		geo.morphBank1.morphs.Add(m);
		return m;
	}

	public static CollisionTriggerEventHandler LabiaHandler(Atom a) => a.gameObject.GetComponentInChildren<CollisionTriggerEventHandler>(true);

	public static Rigidbody Body(Atom a, string name)
	{
		foreach (var rb in a.rigidbodies) if (rb.name == name) return rb;
		return null;
	}
}

namespace UnityEngine
{
	public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Shadowmap = 3, RGB565 = 4, ARGB4444 = 5, ARGB1555 = 6, Default = 7 }
	public enum RenderTextureReadWrite { Default = 0, Linear = 1, sRGB = 2 }
	public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
	public enum TextureWrapMode { Repeat = 0, Clamp = 1 }

	// Records how render textures are created and released so a scenario can assert on it.
	public partial class RenderTexture : Texture
	{
		public static int Created, TemporaryAcquired, TemporaryReleased, Released;
		public static List<int> TemporaryAntiAliasing = new List<int>();
		public static RenderTexture active { get; set; }
		public int antiAliasing { get; set; } = 1;
		public RenderTextureFormat format { get; set; }
		public bool IsTemporary;
		public RenderTexture(int width, int height, int depth) { this.width = width; this.height = height; Created++; }
		public RenderTexture(int width, int height, int depth, RenderTextureFormat format) : this(width, height, depth) { this.format = format; }
		public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite rw) : this(width, height, depth, format) { }
		public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite rw, int antiAliasing)
		{
			TemporaryAcquired++; TemporaryAntiAliasing.Add(antiAliasing);
			var rt = new RenderTexture(width, height, depth, format); Created--; rt.antiAliasing = antiAliasing; rt.IsTemporary = true; return rt;
		}
		public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format) => GetTemporary(width, height, depth, format, RenderTextureReadWrite.Default, 1);
		public static void ReleaseTemporary(RenderTexture rt) { TemporaryReleased++; }
		public void Release() { Released++; }
		public void GenerateMips() { }
		public static void ResetCounters() { Created = TemporaryAcquired = TemporaryReleased = Released = 0; TemporaryAntiAliasing.Clear(); }
	}
}
