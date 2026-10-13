using System;
using System.Collections.Generic;
using UnityEngine;

// What a real VaM script (MVRScript) needs from its host: storables, UI element factories that return live objects, plugin label/enabled params.
// The generated VamStubs.g.cs adds every other member; this file makes the commonly used ones actually work.

public partial class MVRPluginManager : JSONStorable
{
	// plugin id ("plugin#2") -> "Package.N:/path/script.cs", what the scene's PluginManager storable lists
	public readonly Dictionary<string, string> pluginSpecs = new Dictionary<string, string>();

	// the manager's own slot list (protected in VaM; plugins find it by reflection) and the loader a scenario installs to turn a URL into a running script
	public readonly List<MVRPlugin> plugins = new List<MVRPlugin>();
	public static Func<MVRPluginManager, MVRPlugin, string, GameObject> SimLoader;
	private readonly Dictionary<string, GameObject> spawned = new Dictionary<string, GameObject>();

	public MVRPlugin CreatePlugin()
	{
		var p = new MVRPlugin();
		for (int i = 0; i < 1000; i++)
		{
			string u = "plugin#" + i;
			if (!plugins.Exists(x => x.uid == u) && !pluginSpecs.ContainsKey(u)) { p.uid = u; break; }
		}
		var url = new JSONStorableUrl(p.uid, "");
		url.setCallbackFunction = v => LoadPlugin(p, v);
		p.pluginURLJSON = url;
		plugins.Add(p);
		return p;
	}

	private void LoadPlugin(MVRPlugin p, string url)
	{
		GameObject old;
		if (spawned.TryGetValue(p.uid, out old)) { DropScript(old); spawned.Remove(p.uid); }
		pluginSpecs[p.uid] = url;
		if (SimLoader != null && !string.IsNullOrEmpty(url))
		{
			var go = SimLoader(this, p, url);
			if (go != null) spawned[p.uid] = go;
		}
	}

	private void DropScript(GameObject go)
	{
		if (go == null) return;
		foreach (var sc in go.GetComponents<MVRScript>()) { try { if (containingAtom != null) containingAtom.UnregisterAdditionalStorable(sc); } catch { } }
		UnityEngine.Object.Destroy(go);
	}

	public void RemovePluginWithUID(string uid)
	{
		GameObject go;
		if (spawned.TryGetValue(uid, out go)) { DropScript(go); spawned.Remove(uid); }
		plugins.RemoveAll(x => x.uid == uid);
		pluginSpecs.Remove(uid);
	}

	public void RemoveAllPlugins() { foreach (var p in plugins.ToArray()) RemovePluginWithUID(p.uid); }

	public void ReloadPluginWithUID(string uid)
	{
		var p = plugins.Find(x => x.uid == uid);
		if (p != null && p.pluginURLJSON != null) { string u = p.pluginURLJSON.val; p.pluginURLJSON.valNoCallback = ""; p.pluginURLJSON.val = u; }
	}
	public override SimpleJSON.JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
	{
		var jc = new SimpleJSON.JSONClass();
		var pl = new SimpleJSON.JSONClass();
		foreach (var kv in pluginSpecs) pl[kv.Key] = kv.Value;
		jc["plugins"] = pl;
		return jc;
	}
}

public partial class MVRScript : JSONStorable
{
	public JSONStorableBool enabledJSON;
	public JSONStorableString pluginLabelJSON;
	public MVRPluginManager manager;
	public readonly List<UIDynamic> uiLeft = new List<UIDynamic>(), uiRight = new List<UIDynamic>();

	public virtual void Init() { }
	public SimpleJSON.JSONNode LoadJSON(string saveName) { return SuperController.singleton == null ? null : SuperController.singleton.LoadJSON(saveName); }
	public void SaveJSON(SimpleJSON.JSONClass jc, string saveName) { if (SuperController.singleton != null) SuperController.singleton.SaveJSON(jc, saveName); }
	public override void InitUI() { }
	public void ForceAwake() { }
	public Atom GetContainingAtom() { return containingAtom; }

	private T Make<T>(string label, bool right) where T : UIDynamic
	{
		var go = new GameObject("ui " + label);
		if (transform != null) go.transform.SetParent(transform, false);
		var d = go.AddComponent<T>();
		(right ? uiRight : uiLeft).Add(d);
		return d;
	}

	public UIDynamicSlider CreateSlider(JSONStorableFloat jsf, bool rightSide = false)
	{
		var d = Make<UIDynamicSlider>(jsf == null ? "slider" : jsf.name, rightSide);
		d.slider = d.gameObject.AddComponent<UnityEngine.UI.Slider>();
		d.labelText = d.gameObject.AddComponent<UnityEngine.UI.Text>();
		d.label = jsf == null ? "" : jsf.name;
		if (jsf != null) { d.slider.minValue = jsf.min; d.slider.maxValue = jsf.max; d.slider.value = jsf.val; }
		return d;
	}
	public UIDynamicToggle CreateToggle(JSONStorableBool jsb, bool rightSide = false)
	{
		var d = Make<UIDynamicToggle>(jsb == null ? "toggle" : jsb.name, rightSide);
		d.toggle = d.gameObject.AddComponent<UnityEngine.UI.Toggle>();
		d.labelText = d.gameObject.AddComponent<UnityEngine.UI.Text>();
		d.label = jsb == null ? "" : jsb.name;
		return d;
	}
	public UIDynamicButton CreateButton(string label, bool rightSide = false)
	{
		var d = Make<UIDynamicButton>(label, rightSide);
		d.button = d.gameObject.AddComponent<UnityEngine.UI.Button>();
		d.buttonText = d.gameObject.AddComponent<UnityEngine.UI.Text>();
		d.label = label;
		return d;
	}
	public UIDynamicTextField CreateTextField(JSONStorableString jss, bool rightSide = false)
	{
		var d = Make<UIDynamicTextField>(jss == null ? "text" : jss.name, rightSide);
		d.UItext = d.gameObject.AddComponent<UnityEngine.UI.Text>();
		return d;
	}
	public UIDynamicPopup CreatePopup(JSONStorableStringChooser jsc, bool rightSide = false) { return MakePopup(jsc, rightSide); }
	public UIDynamicPopup CreateScrollablePopup(JSONStorableStringChooser jsc, bool rightSide = false) { return MakePopup(jsc, rightSide); }
	public UIDynamicPopup CreateFilterablePopup(JSONStorableStringChooser jsc, bool rightSide = false) { return MakePopup(jsc, rightSide); }
	private UIDynamicPopup MakePopup(JSONStorableStringChooser jsc, bool right)
	{
		var d = Make<UIDynamicPopup>(jsc == null ? "popup" : jsc.name, right);
		d.popup = d.gameObject.AddComponent<UIPopup>();
		return d;
	}
	public UIDynamic CreateSpacer(bool rightSide = false) { return Make<UIDynamic>("spacer", rightSide); }
	public void RemoveSlider(UIDynamicSlider d) { Drop(d); }
	public void RemoveToggle(UIDynamicToggle d) { Drop(d); }
	public void RemoveButton(UIDynamicButton d) { Drop(d); }
	public void RemoveTextField(UIDynamicTextField d) { Drop(d); }
	public void RemovePopup(UIDynamicPopup d) { Drop(d); }
	public void RemoveColorPicker(UIDynamicColorPicker d) { Drop(d); }
	public void RemoveSpacer(UIDynamic d) { Drop(d); }
	public void RemoveSlider(JSONStorableFloat j) { DropFor(j == null ? null : j.name); }
	public void RemoveToggle(JSONStorableBool j) { DropFor(j == null ? null : j.name); }
	public void RemoveColorPicker(JSONStorableColor j) { DropFor(j == null ? null : j.name); }
	public void RemovePopup(JSONStorableStringChooser j) { DropFor(j == null ? null : j.name); }
	public void RemoveTextField(JSONStorableString j) { DropFor(j == null ? null : j.name); }
	private void DropFor(string name) { if (name == null) return; foreach (var l in new[] { uiLeft, uiRight }) foreach (var d in l.ToArray()) if (d != null && d.name == "ui " + name) Drop(d); }
	private void Drop(UIDynamic d) { if (d == null) return; uiLeft.Remove(d); uiRight.Remove(d); if (d.gameObject != null) UnityEngine.Object.Destroy(d.gameObject); }
}

public partial class DAZCharacterSelector
{
	public DAZClothingItem GetClothingItem(string itemId)
	{
		if (clothingItems != null) foreach (var c in clothingItems) if (c != null && c.uid == itemId) return c;
		return null;
	}
}

namespace UnityEngine
{
	// geometry the simulator can look at: what a plugin assigns to a mesh and what it asks the renderer to draw
	public sealed partial class Mesh : Object
	{
		private Vector3[] _vertices = new Vector3[0];
		public int[] indices = new int[0];
		public MeshTopology topology = MeshTopology.Triangles;
		public Vector3[] vertices { get { return _vertices; } set { _vertices = value ?? new Vector3[0]; } }
		public void SetIndices(int[] idx, MeshTopology t, int submesh) { indices = idx ?? new int[0]; topology = t; }
		public void Clear() { _vertices = new Vector3[0]; indices = new int[0]; }
		public void RecalculateBounds() { }
		public Mesh() { }
	}

	public partial class Material : Object
	{
		public Color color { get; set; }
		private readonly System.Collections.Generic.Dictionary<string, float> floats = new System.Collections.Generic.Dictionary<string, float>();
		public Shader shader { get; set; }
		public int renderQueue { get; set; }
		private readonly System.Collections.Generic.Dictionary<string, Texture> textures = new System.Collections.Generic.Dictionary<string, Texture>();
		public System.Collections.Generic.HashSet<string> SimProperties = new System.Collections.Generic.HashSet<string>();   // what the shader declares (the simulator has no shader to ask)
		public bool HasProperty(string name) { return SimProperties.Contains(name) || floats.ContainsKey(name) || textures.ContainsKey(name); }
		public void SetTexture(string name, Texture t) { textures[name] = t; }
		public Texture GetTexture(string name) { Texture t; return textures.TryGetValue(name, out t) ? t : null; }
		public void SetFloat(string name, float v) { floats[name] = v; }
		public float GetFloat(string name) { float v; return floats.TryGetValue(name, out v) ? v : 0f; }
		public Material() { }
		public Material(Shader shader) { this.shader = shader; }
		public Material(Material source) { color = source.color; shader = source.shader; renderQueue = source.renderQueue; foreach (var kv in source.floats) floats[kv.Key] = kv.Value; foreach (var kv in source.textures) textures[kv.Key] = kv.Value; foreach (var p in source.SimProperties) SimProperties.Add(p); }
	}

	public sealed partial class Graphics
	{
		public sealed class DrawCall { public Mesh mesh; public Material material; public int layer; public int vertexCount; public int indexCount; public Color color; public float alpha; }
		public static readonly System.Collections.Generic.List<DrawCall> DrawLog = new System.Collections.Generic.List<DrawCall>();
		public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer)
		{
			DrawLog.Add(new DrawCall { mesh = mesh, material = material, layer = layer, vertexCount = mesh.vertices.Length, indexCount = mesh.indices.Length, color = material == null ? default(Color) : material.color, alpha = material == null ? 0f : material.GetFloat("_Alpha") });
		}
		public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties, bool castShadows, bool receiveShadows)
		{
			DrawMesh(mesh, matrix, material, layer);
		}
	}
}

namespace MVR.FileManagementSecure
{
	// an in-memory file system, with call counters, standing in for VaM's package-aware one
	public partial class FileManagerSecure
	{
		public static readonly System.Collections.Generic.HashSet<string> SimFiles = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		public static readonly System.Collections.Generic.HashSet<string> SimDirs = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		public static int FileExistsCalls, DirExistsCalls, GetFilesCalls, GetDirectoriesCalls;
		private static string Norm(string p) { return (p ?? "").Replace((char)92, '/').TrimEnd('/'); }
		private static bool Glob(string name, string pat)
		{
			var re = "^" + System.Text.RegularExpressions.Regex.Escape(pat).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			return System.Text.RegularExpressions.Regex.IsMatch(name, re, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		}
		public static void SimReset() { SimText.Clear(); SimFiles.Clear(); SimDirs.Clear(); FileExistsCalls = DirExistsCalls = GetFilesCalls = GetDirectoriesCalls = 0; }
		public static void SimAddFile(string path)
		{
			path = Norm(path); SimFiles.Add(path);
			for (int i = path.LastIndexOf('/'); i > 0; i = path.LastIndexOf('/', i - 1)) SimDirs.Add(path.Substring(0, i));
		}
		public static readonly System.Collections.Generic.Dictionary<string, string> SimText = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
		public static void SimAddText(string path, string text) { SimAddFile(path); SimText[Norm(path)] = text; }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static string ReadAllText(string path)
		{
			string t, p = Norm(path);
			if (SimText.TryGetValue(p, out t)) return t;
			if (SuperController.SimSaved.TryGetValue(path, out t)) return t;
			throw new System.IO.FileNotFoundException(path);
		}
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static void CreateDirectory(string path) { SimDirs.Add(Norm(path)); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static string NormalizePath(string path) { return (path ?? "").Replace((char)92, '/'); }
		public static System.Func<string, bool> SimMissHook;   // asked when a path is not in the simulated file system (VPB registering a switched-off package on demand)
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static bool FileExists(string path, bool onlySystemFiles) { FileExistsCalls++; return SimFiles.Contains(Norm(path)) || SuperController.SimSaved.ContainsKey(Norm(path)) || (SimMissHook != null && SimMissHook(Norm(path))); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static bool DirectoryExists(string path, bool onlySystemDirectories) { DirExistsCalls++; return SimDirs.Contains(Norm(path)) || (SimMissHook != null && SimMissHook(Norm(path))); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static string[] GetFiles(string dir, string pattern)
		{
			GetFilesCalls++;
			var r = new System.Collections.Generic.List<string>(); string d = Norm(dir);
			var all = new System.Collections.Generic.HashSet<string>(SimFiles, System.StringComparer.OrdinalIgnoreCase);
			foreach (var k in SuperController.SimSaved.Keys) all.Add(Norm(k));   // files a scenario saved through SuperController.SaveJSON
			foreach (var f in all)
			{
				int i = f.LastIndexOf('/');
				if (i < 0 || !string.Equals(f.Substring(0, i), d, System.StringComparison.OrdinalIgnoreCase)) continue;
				string name = f.Substring(i + 1);
				if (string.IsNullOrEmpty(pattern) || Glob(name, pattern)) r.Add(f);
			}
			r.Sort(System.StringComparer.OrdinalIgnoreCase);
			return r.ToArray();
		}
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static string[] GetDirectories(string dir, string pattern)
		{
			GetDirectoriesCalls++;
			var r = new System.Collections.Generic.List<string>(); string d = Norm(dir);
			foreach (var x in SimDirs)
			{
				int i = x.LastIndexOf('/');
				if (i >= 0 && string.Equals(x.Substring(0, i), d, System.StringComparison.OrdinalIgnoreCase)) r.Add(x);
			}
			r.Sort(System.StringComparer.OrdinalIgnoreCase);
			return r.ToArray();
		}
	}
}

namespace MVR.FileManagement
{
	public partial class FileManager : UnityEngine.MonoBehaviour
	{
		public static readonly System.Collections.Generic.List<string> SimPackageUids = new System.Collections.Generic.List<string>();
		public static System.DateTime SimRefreshTime = new System.DateTime(2026, 1, 1);
		public static System.Collections.Generic.List<string> GetPackageUids() { return new System.Collections.Generic.List<string>(SimPackageUids); }
		public static System.DateTime lastPackageRefreshTime { get { return SimRefreshTime; } }
	}
}

namespace UnityEngine
{
	public sealed partial class Random
	{
		private static readonly System.Random Rnd = new System.Random(1234);
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static float Range(float min, float max) { return min + (float)Rnd.NextDouble() * (max - min); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static int Range(int min, int max) { return Rnd.Next(min, max); }
		public static float value { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return (float)Rnd.NextDouble(); } }
		public static Vector3 insideUnitSphere { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { Vector3 v; do { v = new Vector3((float)Rnd.NextDouble() * 2 - 1, (float)Rnd.NextDouble() * 2 - 1, (float)Rnd.NextDouble() * 2 - 1); } while (v.sqrMagnitude > 1f); return v; } }
	}

	public sealed partial class LineRenderer
	{
		public Vector3[] SimPositions = new Vector3[0]; public float SimWidth;
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void SetVertexCount(int n) { System.Array.Resize(ref SimPositions, n); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void SetWidth(float a, float b) { SimWidth = a; }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void SetPosition(int i, Vector3 v) { if (i >= 0 && i < SimPositions.Length) SimPositions[i] = v; }
	}

	public partial class GameObject
	{
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static GameObject CreatePrimitive(PrimitiveType type) { var g = new GameObject(type.ToString()); g.AddComponent<MeshRenderer>(); g.AddComponent<Collider>(); return g; }
	}

	public sealed partial class Shader : Object
	{
		public bool isSupported { get; set; }
		public Shader() { isSupported = true; }
	}

	// an asset bundle the simulator cannot open: it hands out whatever the test put in SimContents under the asset name
	public sealed partial class AssetBundle : Object
	{
		public static readonly System.Collections.Generic.Dictionary<string, Object> SimContents = new System.Collections.Generic.Dictionary<string, Object>();
		public static int SimLoadFromMemoryCalls;
		public static bool SimFailLoad;
		public static AssetBundle LoadFromMemory(byte[] binary) { SimLoadFromMemoryCalls++; return SimFailLoad || binary == null || binary.Length == 0 ? null : new AssetBundle(); }
		public T LoadAsset<T>(string name) where T : Object { Object o; return SimContents.TryGetValue(name, out o) && o is T ? (T)o : null; }
		public T[] LoadAllAssets<T>() where T : Object { var l = new System.Collections.Generic.List<T>(); foreach (var o in SimContents.Values) if (o is T) l.Add((T)o); return l.ToArray(); }
		public void Unload(bool unloadAllLoadedObjects) { }
	}

	public static partial class JsonUtility
	{
		private static readonly System.Text.Json.JsonSerializerOptions opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		public static T FromJson<T>(string json) { return System.Text.Json.JsonSerializer.Deserialize<T>(json, opts); }
		public static string ToJson(object obj) { return System.Text.Json.JsonSerializer.Serialize(obj, obj.GetType(), opts); }
	}
}


// ---- VaM's trigger system, without any UI: actions are only counted so a scenario can see what fired
public partial class TriggerActionDiscrete
{
	public static int SimTriggered;
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public void Trigger(bool reverse = false, bool force = false) { SimTriggered++; }
}

public partial class TriggerActionTransition
{
	public static int SimCalls;
	public static float SimLast;
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public void TriggerInterp(float interp, bool force = false) { SimCalls++; SimLast = interp; }
}

public partial class Trigger
{
	public TriggerHandler handler;
	public System.Collections.Generic.List<TriggerActionDiscrete> discreteActionsStart = new System.Collections.Generic.List<TriggerActionDiscrete>();
	public System.Collections.Generic.List<TriggerActionDiscrete> discreteActionsEnd = new System.Collections.Generic.List<TriggerActionDiscrete>();
	public System.Collections.Generic.List<TriggerActionTransition> transitionActions = new System.Collections.Generic.List<TriggerActionTransition>();
	protected float _transitionInterpValue;
	private bool _activeFlag;
	public bool active
	{
		get { return _activeFlag; }
		set
		{
			if (value == _activeFlag) return;
			_activeFlag = value;
			var l = value ? discreteActionsStart : discreteActionsEnd;
			for (int i = 0; i < l.Count; i++) l[i].Trigger();
		}
	}
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public bool HasActions() { return discreteActionsStart.Count > 0 || discreteActionsEnd.Count > 0 || transitionActions.Count > 0; }
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public virtual void RestoreFromJSON(SimpleJSON.JSONClass jc) { RestoreFromJSON(jc, "", false); }
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public virtual void RestoreFromJSON(SimpleJSON.JSONClass jc, string subScenePrefix, bool isMerge)
	{
		if (!isMerge) { discreteActionsStart.Clear(); discreteActionsEnd.Clear(); transitionActions.Clear(); }
		var a = jc == null ? null : jc["startActions"] as SimpleJSON.JSONArray; for (int i = 0; a != null && i < a.Count; i++) discreteActionsStart.Add(new TriggerActionDiscrete());
		a = jc == null ? null : jc["endActions"] as SimpleJSON.JSONArray; for (int i = 0; a != null && i < a.Count; i++) discreteActionsEnd.Add(new TriggerActionDiscrete());
		a = jc == null ? null : jc["transitionActions"] as SimpleJSON.JSONArray; for (int i = 0; a != null && i < a.Count; i++) transitionActions.Add(new TriggerActionTransition());
	}
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public virtual SimpleJSON.JSONClass GetJSON(string subScenePrefix)
	{
		var jc = new SimpleJSON.JSONClass();
		var s = new SimpleJSON.JSONArray(); for (int i = 0; i < discreteActionsStart.Count; i++) s.Add(new SimpleJSON.JSONClass()); jc["startActions"] = s;
		var t = new SimpleJSON.JSONArray(); for (int i = 0; i < transitionActions.Count; i++) t.Add(new SimpleJSON.JSONClass()); jc["transitionActions"] = t;
		var e = new SimpleJSON.JSONArray(); for (int i = 0; i < discreteActionsEnd.Count; i++) e.Add(new SimpleJSON.JSONClass()); jc["endActions"] = e;
		return jc;
	}
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public virtual void Update() { }
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public virtual void Remove() { }
}

// ---- UnityEvent that really calls its listeners (UI buttons, sliders), so a scenario can click things
namespace UnityEngine.Events
{
	public partial class UnityEventBase
	{
		public readonly System.Collections.Generic.List<System.Delegate> SimCalls = new System.Collections.Generic.List<System.Delegate>();
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void RemoveAllListeners() { SimCalls.Clear(); }
	}

	public partial class UnityEvent
	{
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void AddListener(UnityAction call) { SimCalls.Add(call); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void RemoveListener(UnityAction call) { SimCalls.Remove(call); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void Invoke() { foreach (var d in SimCalls.ToArray()) ((UnityAction)d)(); }
	}

	public abstract partial class UnityEvent<T0>
	{
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void AddListener(UnityAction<T0> call) { SimCalls.Add(call); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void RemoveListener(UnityAction<T0> call) { SimCalls.Remove(call); }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public void Invoke(T0 arg) { foreach (var d in SimCalls.ToArray()) ((UnityAction<T0>)d)(arg); }
	}
}

namespace UnityEngine
{
	public sealed partial class RectTransformUtility
	{
		// screen point inside a RectTransform of a ScreenSpaceOverlay canvas: the canvas' world rect scaled by its scale factor
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam)
		{
			if (rect == null) return false;
			float sc = 1f;
			for (Transform t = rect; t != null; t = t.parent) { var cv = t.gameObject.GetComponent<UnityEngine.Canvas>(); if (cv != null && t.parent == null) sc = cv.scaleFactor; }
			Rect w = rect.WorldRect();
			return screenPoint.x >= w.x * sc && screenPoint.x <= (w.x + w.width) * sc && screenPoint.y >= w.y * sc && screenPoint.y <= (w.y + w.height) * sc;
		}
	}
}
