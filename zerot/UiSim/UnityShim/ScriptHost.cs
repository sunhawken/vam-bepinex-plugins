using System;
using System.Collections.Generic;
using UnityEngine;

// What a real VaM script (MVRScript) needs from its host: storables, UI element factories that return live objects, plugin label/enabled params.
// The generated VamStubs.g.cs adds every other member; this file makes the commonly used ones actually work.

public partial class MVRPluginManager : JSONStorable
{
	// plugin id ("plugin#2") -> "Package.N:/path/script.cs", what the scene's PluginManager storable lists
	public readonly Dictionary<string, string> pluginSpecs = new Dictionary<string, string>();
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
		public void SetFloat(string name, float v) { floats[name] = v; }
		public float GetFloat(string name) { float v; return floats.TryGetValue(name, out v) ? v : 0f; }
		public Material() { }
		public Material(Shader shader) { }
		public Material(Material source) { color = source.color; foreach (var kv in source.floats) floats[kv.Key] = kv.Value; }
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
