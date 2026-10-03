using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
	public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
	public enum RuntimePlatform { WindowsEditor = 7, WindowsPlayer = 2 }
	public enum KeyCode { None = 0, F1 = 282, F8 = 289, F9 = 290, Escape = 27 }
	public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6, Repaint = 7, Layout = 8, DragUpdated = 9, DragPerform = 10, Ignore = 11, Used = 12, ValidateCommand = 13, ExecuteCommand = 14, ContextClick = 16 }
	public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
	public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
	public enum TextClipping { Overflow, Clip }
	public enum FocusType { Native, Keyboard, Passive }
	public enum ImagePosition { ImageLeft, ImageAbove, ImageOnly, TextOnly }
	public enum TextureFormat { RGBA32 = 4 }

	public partial struct Vector2
	{
		public float x, y;
		public Vector2(float x, float y) { this.x = x; this.y = y; }
		public static Vector2 zero => new Vector2(0, 0);
		public static Vector2 one => new Vector2(1, 1);
		public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
		public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
		public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
		public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
		public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
		public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
		public override string ToString() => "(" + x + ", " + y + ")";
	}

	public partial struct Vector3
	{
		public float x, y, z;
		public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
		public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
		public static Vector3 zero => new Vector3(0, 0, 0);
		public static Vector3 one => new Vector3(1, 1, 1);
		public float sqrMagnitude => x * x + y * y + z * z;
		public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
		public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
		public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
	}

	public partial struct Vector4
	{
		public float x, y, z, w;
		public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
	}

	public partial struct Quaternion
	{
		public float x, y, z, w;
		public static Quaternion identity => new Quaternion { w = 1 };
	}

	public partial struct Color
	{
		public float r, g, b, a;
		public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
		public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
		public static Color white => new Color(1, 1, 1, 1);
		public static Color black => new Color(0, 0, 0, 1);
		public static Color clear => new Color(0, 0, 0, 0);
		public static Color gray => new Color(.5f, .5f, .5f, 1);
		public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
	}

	public partial struct Rect
	{
		private float m_x, m_y, m_w, m_h;
		public Rect(float x, float y, float width, float height) { m_x = x; m_y = y; m_w = width; m_h = height; }
		public Rect(Vector2 position, Vector2 size) { m_x = position.x; m_y = position.y; m_w = size.x; m_h = size.y; }
		public float x { get => m_x; set => m_x = value; }
		public float y { get => m_y; set => m_y = value; }
		public float width { get => m_w; set => m_w = value; }
		public float height { get => m_h; set => m_h = value; }
		public float xMin { get => m_x; set { float r = xMax; m_x = value; m_w = r - m_x; } }
		public float yMin { get => m_y; set { float b = yMax; m_y = value; m_h = b - m_y; } }
		public float xMax { get => m_x + m_w; set => m_w = value - m_x; }
		public float yMax { get => m_y + m_h; set => m_h = value - m_y; }
		public Vector2 position { get => new Vector2(m_x, m_y); set { m_x = value.x; m_y = value.y; } }
		public Vector2 size { get => new Vector2(m_w, m_h); set { m_w = value.x; m_h = value.y; } }
		public Vector2 center => new Vector2(m_x + m_w / 2, m_y + m_h / 2);
		public bool Contains(Vector2 p) => p.x >= m_x && p.x < m_x + m_w && p.y >= m_y && p.y < m_y + m_h;
		public bool Contains(Vector3 p) => Contains(new Vector2(p.x, p.y));
		public static Rect zero => new Rect(0, 0, 0, 0);
		public override string ToString() => "(x:" + m_x + ", y:" + m_y + ", w:" + m_w + ", h:" + m_h + ")";
	}

	public partial struct Matrix4x4
	{
		public float sx, sy, tx, ty;   // enough for 2D scale+translate (what IMGUI plugins use)
		public static Matrix4x4 identity => new Matrix4x4 { sx = 1, sy = 1 };
		public static Matrix4x4 Scale(Vector3 v) => new Matrix4x4 { sx = v.x, sy = v.y };
		public static Matrix4x4 TRS(Vector3 t, Quaternion r, Vector3 s) => new Matrix4x4 { sx = s.x, sy = s.y, tx = t.x, ty = t.y };
		public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new Matrix4x4 { sx = a.sx * b.sx, sy = a.sy * b.sy, tx = a.sx * b.tx + a.tx, ty = a.sy * b.ty + a.ty };
	}

	public static partial class Mathf
	{
		public const float PI = 3.14159265f;
		public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
		public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
		public static float Clamp01(float v) => Clamp(v, 0, 1);
		public static float Max(float a, float b) => a > b ? a : b;
		public static float Max(params float[] v) { float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
		public static int Max(int a, int b) => a > b ? a : b;
		public static float Min(float a, float b) => a < b ? a : b;
		public static float Min(params float[] v) { float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
		public static int Min(int a, int b) => a < b ? a : b;
		public static float Abs(float a) => a < 0 ? -a : a;
		public static float Round(float a) => (float)Math.Round(a, MidpointRounding.ToEven);
		public static int RoundToInt(float a) => (int)Math.Round(a, MidpointRounding.ToEven);
		public static int FloorToInt(float a) => (int)Math.Floor(a);
		public static int CeilToInt(float a) => (int)Math.Ceiling(a);
		public static float Floor(float a) => (float)Math.Floor(a);
		public static float Ceil(float a) => (float)Math.Ceiling(a);
		public static float Sqrt(float a) => (float)Math.Sqrt(a);
		public static float Pow(float a, float b) => (float)Math.Pow(a, b);
		public static float Exp(float a) => (float)Math.Exp(a);
		public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
		public static bool Approximately(float a, float b) => Abs(a - b) < 1e-6f;
		public static float Sin(float a) => (float)Math.Sin(a);
		public static float Cos(float a) => (float)Math.Cos(a);
	}

	public static partial class Screen
	{
		public static int width { get; set; } = 1920;
		public static int height { get; set; } = 1080;
		public static float dpi { get; set; } = 96f;
	}

	public static partial class Time
	{
		public static float realtimeSinceStartup { get; set; }
		public static float unscaledTime { get; set; }
		public static float time { get; set; }
		public static float deltaTime { get; set; } = 1f / 60f;
		public static float unscaledDeltaTime { get; set; } = 1f / 60f;
	}

	public static partial class Application
	{
		public static RuntimePlatform platform { get; set; } = RuntimePlatform.WindowsPlayer;
	}

	public static partial class Input
	{
		public static Vector3 mousePosition { get; set; }
		public static bool GetKeyDown(KeyCode k) => false;
		public static bool GetKey(KeyCode k) => false;
		public static bool GetMouseButton(int b) => false;
		public static bool GetMouseButtonDown(int b) => false;
	}

	public static partial class Debug
	{
		public static void Log(object o) { }
		public static void LogWarning(object o) { }
		public static void LogError(object o) { }
	}

	public partial class Object
	{
		public string name { get; set; } = "";
		public HideFlags hideFlags { get; set; }
		internal bool destroyed;
		public static bool operator ==(Object a, Object b)
		{
			bool an = a is null || a.destroyed, bn = b is null || b.destroyed;
			if (an && bn) return true;
			if (an || bn) return false;
			return ReferenceEquals(a, b);
		}
		public static bool operator !=(Object a, Object b) => !(a == b);
		public override bool Equals(object o) => ReferenceEquals(this, o);
		public override int GetHashCode() => base.GetHashCode();
		public static implicit operator bool(Object o) => !(o is null) && !o.destroyed;
		public static void Destroy(Object o) { Destroy(o, 0f); }
		public static void Destroy(Object o, float t) { if (o != null) o.DestroyInternal(); }
		public static void DestroyImmediate(Object o) { Destroy(o); }
		public static void DontDestroyOnLoad(Object o) { }
		internal virtual void DestroyInternal() { destroyed = true; }
		public static T Instantiate<T>(T o) where T : Object => o;
		public T GetInstanceIDHolder<T>() => default;
		public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
		public override string ToString() => name;
	}

	public partial class Component : Object
	{
		public GameObject gameObject { get; set; }
		public Transform transform => gameObject.transform;
		public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
		public T GetComponentInChildren<T>() where T : class => gameObject.GetComponentInChildren<T>(true);
		public T GetComponentInChildren<T>(bool includeInactive) where T : class => gameObject.GetComponentInChildren<T>(includeInactive);
		public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
		public T[] GetComponentsInChildren<T>() where T : class => gameObject.GetComponentsInChildren<T>(false);
		internal virtual void OnAttached() { }
	}

	public partial class Behaviour : Component
	{
		public bool enabled { get; set; } = true;
		public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
	}

	public partial class WaitForSeconds { public float seconds; public WaitForSeconds(float s) { seconds = s; } }
	public partial class WaitForEndOfFrame { }

	public partial class MonoBehaviour : Behaviour
	{
		internal List<IEnumerator> routines = new List<IEnumerator>();
		public Coroutine StartCoroutine(IEnumerator e) { var c = new Coroutine(); c.it = e; routines.Add(e); Sim.Routines.Add(new Sim.Routine { owner = this, it = e }); return c; }
		public void StopAllCoroutines() { Sim.Routines.RemoveAll(r => r.owner == this); }
		public void StopCoroutine(Coroutine c) { }
		public void Invoke(string m, float t) { }
		public void CancelInvoke() { }
		public static void print(object o) { }
	}

	public partial class Coroutine { internal IEnumerator it; }

	public partial class GameObject : Object
	{
		private bool active = true;
		public Transform transform { get; set; }
		internal List<Component> comps = new List<Component>();
		public bool activeSelf => active;
		public bool activeInHierarchy { get { for (var t = transform; t != null; t = t.parent) if (!t.gameObject.active) return false; return true; } }
		public GameObject() : this("GameObject") { }
		public GameObject(string name) : this(name, new Type[0]) { }
		public GameObject(string name, params Type[] components)
		{
			this.name = name;
			Sim.AllObjects.Add(this);
			foreach (var t in components) AddComponent(t);
			if (transform == null) AddComponent(typeof(Transform));
		}
		public void SetActive(bool v)
		{
			bool was = activeInHierarchy;
			active = v;
			if (!was && activeInHierarchy) foreach (var c in comps.ToArray()) if (c is MonoBehaviour m) Sim.Lifecycle.OnEnable(m);
		}
		public Component AddComponent(Type t)
		{
			var c = (Component)Activator.CreateInstance(t, true);
			c.gameObject = this;
			c.name = name;
			comps.Add(c);
			if (c is Transform tr) transform = tr;
			c.OnAttached();
			if (c is MonoBehaviour mb) Sim.Lifecycle.Created(mb);
			return c;
		}
		public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
		public T GetComponent<T>() where T : class { foreach (var c in comps) if (c is T t) return t; return null; }
		public Component GetComponent(Type t) { foreach (var c in comps) if (t.IsInstanceOfType(c)) return c; return null; }
		public T GetComponentInChildren<T>(bool inc) where T : class
		{
			var r = GetComponent<T>(); if (r != null) return r;
			foreach (var ch in transform.children) { r = ch.gameObject.GetComponentInChildren<T>(inc); if (r != null) return r; }
			return null;
		}
		public T[] GetComponentsInChildren<T>(bool inc) where T : class
		{
			var l = new List<T>();
			foreach (var c in comps) if (c is T t) l.Add(t);
			foreach (var ch in transform.children) l.AddRange(ch.gameObject.GetComponentsInChildren<T>(inc));
			return l.ToArray();
		}
		internal override void DestroyInternal()
		{
			if (destroyed) return;
			foreach (var c in comps.ToArray()) { if (c is MonoBehaviour m) Sim.Lifecycle.OnDestroy(m); c.destroyed = true; }
			destroyed = true;
			Sim.AllObjects.Remove(this);
			foreach (var ch in transform.children.ToArray()) ch.gameObject.DestroyInternal();
			transform.SetParent(null, false);
		}
	}

	public partial class Transform : Component, IEnumerable
	{
		public Transform parent { get; set; }
		internal List<Transform> children = new List<Transform>();
		public Vector3 localPosition { get; set; }
		public Vector3 localScale { get; set; } = Vector3.one;
		public Quaternion localRotation { get; set; } = Quaternion.identity;
		public Vector3 position { get => localPosition; set => localPosition = value; }
		public int childCount => children.Count;
		public Transform GetChild(int i) => children[i];
		public int GetSiblingIndex() => parent == null ? 0 : parent.children.IndexOf(this);
		public void SetSiblingIndex(int i) { if (parent == null) return; parent.children.Remove(this); parent.children.Insert(Mathf.Clamp(i, 0, parent.children.Count), this); }
		public void SetAsLastSibling() { if (parent == null) return; parent.children.Remove(this); parent.children.Add(this); }
		public void SetParent(Transform p) { SetParent(p, true); }
		public void SetParent(Transform p, bool worldPositionStays)
		{
			if (parent != null) parent.children.Remove(this);
			parent = p;
			if (p != null) p.children.Add(this);
		}
		public Transform Find(string path)
		{
			Transform cur = this;
			foreach (var part in path.Split('/'))
			{
				Transform next = null;
				foreach (var c in cur.children) if (c.gameObject.name == part) { next = c; break; }
				if (next == null) return null;
				cur = next;
			}
			return cur;
		}
		public IEnumerator GetEnumerator() => children.ToArray().GetEnumerator();
	}

	public partial class Texture : Object { }
	public partial class Texture2D : Texture
	{
		public Texture2D(int w, int h) { }
		public Texture2D(int w, int h, TextureFormat f, bool mip) { }
		public void SetPixel(int x, int y, Color c) { }
		public void Apply() { }
		public void Apply(bool a, bool b) { }
	}

	public partial class Font : Object { }
	public static partial class Resources
	{
		public static T GetBuiltinResource<T>(string n) where T : Object => Activator.CreateInstance<T>();
	}

	public partial class WWWForm { }
	public partial class AudioClip : Object { }
	public partial class Camera : Component { public static Camera main { get; set; } public delegate void CameraCallback(Camera c); public static CameraCallback onPreRender; }

	// -------------------------------------------------------------------- simulation scheduler
	public static partial class Sim
	{
		public partial class Routine { public MonoBehaviour owner; public IEnumerator it; public float wait; }
		public static List<Routine> Routines = new List<Routine>();
		public static List<GameObject> AllObjects = new List<GameObject>();
		public static List<string> Errors = new List<string>();
		public static float Now;

		public static partial class Lifecycle
		{
			public static void Created(MonoBehaviour m) { Pending.Add(m); }
			public static List<MonoBehaviour> Pending = new List<MonoBehaviour>();
			public static HashSet<MonoBehaviour> Started = new HashSet<MonoBehaviour>();
			public static void OnEnable(MonoBehaviour m) { Call(m, "OnEnable"); }
			public static void OnDestroy(MonoBehaviour m) { Call(m, "OnDestroy"); }
			public static void Call(MonoBehaviour m, string name, params object[] args)
			{
				var mi = m.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy, null, args.Length == 0 ? Type.EmptyTypes : Array.ConvertAll(args, a => a.GetType()), null);
				if (mi == null) return;
				try { var r = mi.Invoke(m, args); if (r is IEnumerator e) m.StartCoroutine(e); }
				catch (TargetInvocationException ex) { Error(m.GetType().Name + "." + name, ex.InnerException); }
			}
			// Runs Awake / OnEnable / Start for freshly created behaviours, like Unity does at end of frame.
			public static void Flush()
			{
				while (Pending.Count > 0)
				{
					var list = Pending.ToArray(); Pending.Clear();
					foreach (var m in list)
					{
						if (m.destroyed) continue;
						Call(m, "Awake");
						if (m.isActiveAndEnabled) Call(m, "OnEnable");
					}
					foreach (var m in list)
					{
						if (m.destroyed || !m.isActiveAndEnabled || Started.Contains(m)) continue;
						Started.Add(m);
						Call(m, "Start");
					}
				}
			}
		}

		public static void Error(string where, Exception e)
		{
			string st = ""; if (e.StackTrace != null) { var ls = e.StackTrace.Split((char)10); for (int i = 0; i < Math.Min(3, ls.Length); i++) st += " | " + ls[i].Trim(); }
			string msg = where + ": " + e.GetType().Name + ": " + e.Message + st;
			if (!Errors.Contains(msg)) Errors.Add(msg);
		}

		public static void Step(float dt)
		{
			Now += dt;
			Time.realtimeSinceStartup = Time.unscaledTime = Time.time = Now;
			Lifecycle.Flush();
			foreach (var r in Routines.ToArray())
			{
				if (r.owner == null || r.owner.destroyed) { Routines.Remove(r); continue; }
				if (r.wait > 0) { r.wait -= dt; continue; }
				try
				{
					if (!r.it.MoveNext()) { Routines.Remove(r); continue; }
					if (r.it.Current is WaitForSeconds w) r.wait = w.seconds;
					else if (r.it.Current is IEnumerator inner) { Routines.Remove(r); var comb = Chain(inner, r.it); Routines.Add(new Routine { owner = r.owner, it = comb }); }
				}
				catch (Exception e) { Error(r.owner.GetType().Name + " coroutine", e); Routines.Remove(r); }
			}
			foreach (var go in AllObjects.ToArray())
				foreach (var c in go.comps.ToArray())
					if (c is MonoBehaviour m && m.isActiveAndEnabled && !m.destroyed && Lifecycle.Started.Contains(m))
						Lifecycle.Call(m, "Update");
		}

		private static IEnumerator Chain(IEnumerator inner, IEnumerator outer)
		{
			while (inner.MoveNext()) yield return inner.Current;
			while (outer.MoveNext()) yield return outer.Current;
		}
	}

	public static partial class GUIUtility
	{
		public static int hotControl { get; set; }
		public static int GetControlID(int hint, FocusType t) => hint;
		public static int GetControlID(FocusType t) => 0;
		public static Vector2 GUIToScreenPoint(Vector2 p) => p;
	}
}
