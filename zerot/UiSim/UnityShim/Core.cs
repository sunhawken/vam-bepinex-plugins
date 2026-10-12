using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
	public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
	public enum RuntimePlatform { WindowsEditor = 7, WindowsPlayer = 2 }
	public enum KeyCode { None = 0, Backspace = 8, Delete = 127, Tab = 9, Clear = 12, Return = 13, Pause = 19, Escape = 27, Space = 32, Keypad0 = 256, Keypad1 = 257, Keypad2 = 258, Keypad3 = 259, Keypad4 = 260, Keypad5 = 261, Keypad6 = 262, Keypad7 = 263, Keypad8 = 264, Keypad9 = 265, KeypadPeriod = 266, KeypadDivide = 267, KeypadMultiply = 268, KeypadMinus = 269, KeypadPlus = 270, KeypadEnter = 271, KeypadEquals = 272, UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, Insert = 277, Home = 278, End = 279, PageUp = 280, PageDown = 281, F1 = 282, F2 = 283, F3 = 284, F4 = 285, F5 = 286, F6 = 287, F7 = 288, F8 = 289, F9 = 290, F10 = 291, F11 = 292, F12 = 293, F13 = 294, F14 = 295, F15 = 296, Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57, Exclaim = 33, DoubleQuote = 34, Hash = 35, Dollar = 36, Ampersand = 38, Quote = 39, LeftParen = 40, RightParen = 41, Asterisk = 42, Plus = 43, Comma = 44, Minus = 45, Period = 46, Slash = 47, Colon = 58, Semicolon = 59, Less = 60, Equals = 61, Greater = 62, Question = 63, At = 64, LeftBracket = 91, Backslash = 92, RightBracket = 93, Caret = 94, Underscore = 95, BackQuote = 96, A = 97, B = 98, C = 99, D = 100, E = 101, F = 102, G = 103, H = 104, I = 105, J = 106, K = 107, L = 108, M = 109, N = 110, O = 111, P = 112, Q = 113, R = 114, S = 115, T = 116, U = 117, V = 118, W = 119, X = 120, Y = 121, Z = 122, Numlock = 300, CapsLock = 301, ScrollLock = 302, RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306, RightAlt = 307, LeftAlt = 308, LeftCommand = 310, LeftApple = 310, LeftWindows = 311, RightCommand = 309, RightApple = 309, RightWindows = 312, AltGr = 313, Help = 315, Print = 316, SysReq = 317, Break = 318, Menu = 319, Mouse0 = 323, Mouse1 = 324, Mouse2 = 325, Mouse3 = 326, Mouse4 = 327, Mouse5 = 328, Mouse6 = 329, JoystickButton0 = 330, JoystickButton1 = 331, JoystickButton2 = 332, JoystickButton3 = 333, JoystickButton4 = 334, JoystickButton5 = 335, JoystickButton6 = 336, JoystickButton7 = 337, JoystickButton8 = 338, JoystickButton9 = 339, JoystickButton10 = 340, JoystickButton11 = 341, JoystickButton12 = 342, JoystickButton13 = 343, JoystickButton14 = 344, JoystickButton15 = 345, JoystickButton16 = 346, JoystickButton17 = 347, JoystickButton18 = 348, JoystickButton19 = 349, Joystick1Button0 = 350, Joystick1Button1 = 351, Joystick1Button2 = 352, Joystick1Button3 = 353, Joystick1Button4 = 354, Joystick1Button5 = 355, Joystick1Button6 = 356, Joystick1Button7 = 357, Joystick1Button8 = 358, Joystick1Button9 = 359, Joystick1Button10 = 360, Joystick1Button11 = 361, Joystick1Button12 = 362, Joystick1Button13 = 363, Joystick1Button14 = 364, Joystick1Button15 = 365, Joystick1Button16 = 366, Joystick1Button17 = 367, Joystick1Button18 = 368, Joystick1Button19 = 369, Joystick2Button0 = 370, Joystick2Button1 = 371, Joystick2Button2 = 372, Joystick2Button3 = 373, Joystick2Button4 = 374, Joystick2Button5 = 375, Joystick2Button6 = 376, Joystick2Button7 = 377, Joystick2Button8 = 378, Joystick2Button9 = 379, Joystick2Button10 = 380, Joystick2Button11 = 381, Joystick2Button12 = 382, Joystick2Button13 = 383, Joystick2Button14 = 384, Joystick2Button15 = 385, Joystick2Button16 = 386, Joystick2Button17 = 387, Joystick2Button18 = 388, Joystick2Button19 = 389, Joystick3Button0 = 390, Joystick3Button1 = 391, Joystick3Button2 = 392, Joystick3Button3 = 393, Joystick3Button4 = 394, Joystick3Button5 = 395, Joystick3Button6 = 396, Joystick3Button7 = 397, Joystick3Button8 = 398, Joystick3Button9 = 399, Joystick3Button10 = 400, Joystick3Button11 = 401, Joystick3Button12 = 402, Joystick3Button13 = 403, Joystick3Button14 = 404, Joystick3Button15 = 405, Joystick3Button16 = 406, Joystick3Button17 = 407, Joystick3Button18 = 408, Joystick3Button19 = 409, Joystick4Button0 = 410, Joystick4Button1 = 411, Joystick4Button2 = 412, Joystick4Button3 = 413, Joystick4Button4 = 414, Joystick4Button5 = 415, Joystick4Button6 = 416, Joystick4Button7 = 417, Joystick4Button8 = 418, Joystick4Button9 = 419, Joystick4Button10 = 420, Joystick4Button11 = 421, Joystick4Button12 = 422, Joystick4Button13 = 423, Joystick4Button14 = 424, Joystick4Button15 = 425, Joystick4Button16 = 426, Joystick4Button17 = 427, Joystick4Button18 = 428, Joystick4Button19 = 429, Joystick5Button0 = 430, Joystick5Button1 = 431, Joystick5Button2 = 432, Joystick5Button3 = 433, Joystick5Button4 = 434, Joystick5Button5 = 435, Joystick5Button6 = 436, Joystick5Button7 = 437, Joystick5Button8 = 438, Joystick5Button9 = 439, Joystick5Button10 = 440, Joystick5Button11 = 441, Joystick5Button12 = 442, Joystick5Button13 = 443, Joystick5Button14 = 444, Joystick5Button15 = 445, Joystick5Button16 = 446, Joystick5Button17 = 447, Joystick5Button18 = 448, Joystick5Button19 = 449, Joystick6Button0 = 450, Joystick6Button1 = 451, Joystick6Button2 = 452, Joystick6Button3 = 453, Joystick6Button4 = 454, Joystick6Button5 = 455, Joystick6Button6 = 456, Joystick6Button7 = 457, Joystick6Button8 = 458, Joystick6Button9 = 459, Joystick6Button10 = 460, Joystick6Button11 = 461, Joystick6Button12 = 462, Joystick6Button13 = 463, Joystick6Button14 = 464, Joystick6Button15 = 465, Joystick6Button16 = 466, Joystick6Button17 = 467, Joystick6Button18 = 468, Joystick6Button19 = 469, Joystick7Button0 = 470, Joystick7Button1 = 471, Joystick7Button2 = 472, Joystick7Button3 = 473, Joystick7Button4 = 474, Joystick7Button5 = 475, Joystick7Button6 = 476, Joystick7Button7 = 477, Joystick7Button8 = 478, Joystick7Button9 = 479, Joystick7Button10 = 480, Joystick7Button11 = 481, Joystick7Button12 = 482, Joystick7Button13 = 483, Joystick7Button14 = 484, Joystick7Button15 = 485, Joystick7Button16 = 486, Joystick7Button17 = 487, Joystick7Button18 = 488, Joystick7Button19 = 489, Joystick8Button0 = 490, Joystick8Button1 = 491, Joystick8Button2 = 492, Joystick8Button3 = 493, Joystick8Button4 = 494, Joystick8Button5 = 495, Joystick8Button6 = 496, Joystick8Button7 = 497, Joystick8Button8 = 498, Joystick8Button9 = 499, Joystick8Button10 = 500, Joystick8Button11 = 501, Joystick8Button12 = 502, Joystick8Button13 = 503, Joystick8Button14 = 504, Joystick8Button15 = 505, Joystick8Button16 = 506, Joystick8Button17 = 507, Joystick8Button18 = 508, Joystick8Button19 = 509 }
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
		public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
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
		public static readonly System.Collections.Generic.HashSet<KeyCode> SimDown = new System.Collections.Generic.HashSet<KeyCode>();   // keys "pressed this frame" for the simulator
		public static bool GetKeyDown(KeyCode k) => SimDown.Contains(k);
		public static bool GetKey(KeyCode k) => false;
		public static readonly System.Collections.Generic.HashSet<int> SimMouseHeld = new System.Collections.Generic.HashSet<int>();   // mouse buttons held / pressed this frame, for the simulator
		public static readonly System.Collections.Generic.HashSet<int> SimMouseDown = new System.Collections.Generic.HashSet<int>();
		public static bool GetMouseButton(int b) => SimMouseHeld.Contains(b);
		public static bool GetMouseButtonDown(int b) => SimMouseDown.Contains(b);
	}

	public static partial class Debug
	{
		public static void Log(object o) { }
		public static void LogWarning(object o) { }
		public static void LogError(object o) { }
	}

	public partial class Object
	{
		private string _name = "";
		public virtual string name { get { return _name; } set { _name = value; } }
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
		public static T Instantiate<T>(T o) where T : Object { return Cloner.Clone(o, null); }
		public static T Instantiate<T>(T o, Transform parent) where T : Object { return Cloner.Clone(o, parent); }
		public static T Instantiate<T>(T o, Transform parent, bool worldPositionStays) where T : Object { return Cloner.Clone(o, parent); }
		public static T Instantiate<T>(T o, Vector3 position, Quaternion rotation) where T : Object { var c = Cloner.Clone(o, null); var tr = c is GameObject g ? g.transform : (c as Component)?.transform; if (tr != null) { tr.position = position; tr.rotation = rotation; } return c; }
		public static Object Instantiate(Object o) { return Cloner.Clone(o, null); }
		public T GetInstanceIDHolder<T>() => default;
		public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
		public override string ToString() => name;
	}

	public partial class Component : Object
	{
		public override string name { get { return _go != null ? _go.name : base.name; } set { if (_go != null) _go.name = value; else base.name = value; } }
		private GameObject _go;
		public GameObject gameObject { get { return _go ?? GameObject.constructing; } set { _go = value; } }   // like Unity, a script constructor already sees its game object
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
		public Coroutine StartCoroutine(string methodName) { return StartCoroutine(methodName, null); }
		public Coroutine StartCoroutine(string methodName, object value)
		{
			var mi = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (mi == null) return null;
			var r = mi.Invoke(this, mi.GetParameters().Length == 0 ? null : new object[] { value });
			return r is IEnumerator it ? StartCoroutine(it) : null;
		}
		public Coroutine StartCoroutine(IEnumerator e)
		{
			if (e == null) return null;
			var c = new Coroutine(); c.it = e;
			try { routines.Add(e); Sim.Routines.Add(new Sim.Routine { owner = this, it = e }); }
			catch (ArrayTypeMismatchException ex) { throw new InvalidOperationException("StartCoroutine(" + e.GetType().FullName + ") from " + GetType().Name + ": " + ex.StackTrace, ex); }
			return c;
		}
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
		[ThreadStatic] internal static GameObject constructing;
		public Component AddComponent(Type t)
		{
			Component c;
			var prev = constructing; constructing = this;
			try { c = (Component)Activator.CreateInstance(t, true); }
			catch (TargetInvocationException tie) when (tie.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw(); throw; }
			finally { constructing = prev; }
			c.gameObject = this;
			c.name = name;
			comps.Add(c);
			if (c is Transform tr)
			{
				var old = transform;
				if (old != null && old != tr)
				{   // Unity swaps Transform for RectTransform in place: parent, children and local pose carry over
					tr.parent = old.parent;
					if (old.parent != null) { int idx = old.parent.children.IndexOf(old); if (idx >= 0) old.parent.children[idx] = tr; }
					tr.children = old.children; foreach (var ch in tr.children) ch.parent = tr;
					tr.localPosition = old.localPosition; tr.localRotation = old.localRotation; tr.localScale = old.localScale;
					comps.Remove(old); old.destroyed = true;
				}
				transform = tr;
			}
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
		public int childCount => children.Count;
		public Transform GetChild(int i) => children[i];
		public int GetSiblingIndex() => parent == null ? 0 : parent.children.IndexOf(this);
		public void SetSiblingIndex(int i) { if (parent == null) return; parent.children.Remove(this); parent.children.Insert(Mathf.Clamp(i, 0, parent.children.Count), this); }
		public void SetAsLastSibling() { if (parent == null) return; parent.children.Remove(this); parent.children.Add(this); }
		public void SetParent(Transform p) { SetParent(p, true); }
		public void SetParent(Transform p, bool worldPositionStays)
		{
			Vector3 wp = default(Vector3); Quaternion wr = default(Quaternion);
			if (worldPositionStays) { wp = position; wr = rotation; }
			if (parent != null) parent.children.Remove(this);
			parent = p;
			if (p != null) p.children.Add(this);
			if (worldPositionStays) { position = wp; rotation = wr; }
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
		public Color[] Px = new Color[] { new Color(1, 1, 1, 1) };
		public Texture2D(int w, int h) { Init(w, h); }
		public Texture2D(int w, int h, TextureFormat f, bool mip) { Init(w, h); }
		private void Init(int w, int h) { width = w; height = h; Px = new Color[Math.Max(1, w * h)]; for (int i = 0; i < Px.Length; i++) Px[i] = new Color(1, 1, 1, 1); }
		public Color Pixel0 { get { return Px[0]; } set { Px[0] = value; } }
		public static Texture2D blackTexture { get; } = new Texture2D(1, 1);
		public void SetPixel(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < width && y < height) Px[y * width + x] = c; else Px[0] = c; }
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
			string st = ""; if (e.StackTrace != null) { var ls = e.StackTrace.Split((char)10); int maxFrames = Environment.GetEnvironmentVariable("SIM_STACK") != null ? 16 : 3; for (int i = 0; i < Math.Min(maxFrames, ls.Length); i++) st += " | " + ls[i].Trim(); }
			string msg = where + ": " + e.GetType().Name + ": " + e.Message + st;
			if (!Errors.Contains(msg)) Errors.Add(msg);
		}

		public static void Step(float dt)
		{
			Now += dt;
			Time.realtimeSinceStartup = Time.unscaledTime = Time.time = Now;
			Time.deltaTime = Time.unscaledDeltaTime = dt;
			BeforeFrame?.Invoke();   // the scene host moves bones / physics here
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
			// physics ticks (FixedUpdate) at Time.fixedDeltaTime
			FixedAccum += dt;
			float fdt = Time.fixedDeltaTime <= 0f ? 0.02f : Time.fixedDeltaTime;
			while (FixedAccum >= fdt - 1e-6f)
			{
				FixedAccum -= fdt;
				foreach (var go in AllObjects.ToArray())
					foreach (var c in go.comps.ToArray())
						if (c is MonoBehaviour m && m.isActiveAndEnabled && !m.destroyed && Lifecycle.Started.Contains(m))
							Lifecycle.Call(m, "FixedUpdate");
			}
			foreach (var go in AllObjects.ToArray())
				foreach (var c in go.comps.ToArray())
					if (c is MonoBehaviour m && m.isActiveAndEnabled && !m.destroyed && Lifecycle.Started.Contains(m))
						Lifecycle.Call(m, "LateUpdate");
		}
		public static Action BeforeFrame;
		public static float FixedAccum;

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

	// Object.Instantiate: a deep copy of the game object (children, components with their field values)
	internal static class Cloner
	{
		public static T Clone<T>(T src, Transform parent) where T : Object
		{
			if (src == null) return null;
			var go = src as GameObject ?? (src as Component)?.gameObject;
			if (go == null)
			{   // assets (Material, Mesh...): a copy through the copy constructor when there is one
				var cc = src.GetType().GetConstructor(new[] { src.GetType() });
				return cc != null ? (T)cc.Invoke(new object[] { src }) : src;
			}
			var map = new Dictionary<Object, Object>();
			var copy = CloneGo(go, parent, map);
			if (src is GameObject) return (T)(Object)copy;
			var comp = (Component)map[src];
			return (T)(Object)comp;
		}

		static GameObject CloneGo(GameObject go, Transform parent, Dictionary<Object, Object> map)
		{
			var c = new GameObject(go.name + "(Clone)");
			map[go] = c;
			c.SetActive(go.activeSelf);
			if (parent != null) c.transform.SetParent(parent, false);
			var st = go.transform; var dt = c.transform;
			foreach (var comp in go.comps.ToArray())
			{
				Component n;
				if (comp is Transform) { n = c.transform; }
				else
				{
					if (comp.GetType().IsSubclassOf(typeof(Transform))) { n = c.AddComponent(comp.GetType()); }
					else n = c.AddComponent(comp.GetType());
				}
				map[comp] = n;
			}
			dt.localPosition = st.localPosition; dt.localRotation = st.localRotation; dt.localScale = st.localScale;
			foreach (var ch in st.children.ToArray()) CloneGo(ch.gameObject, dt, map);
			// copy field values; references into the copied hierarchy are remapped
			foreach (var kv in new List<KeyValuePair<Object, Object>>(map))
			{
				if (!(kv.Key is Component sc) || sc is Transform) continue;
				var dc = (Component)kv.Value;
				for (var t = sc.GetType(); t != null && t != typeof(Component) && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
					foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						if (f.IsInitOnly || f.Name == "_go") continue;
						object v; try { v = f.GetValue(sc); } catch { continue; }
						Object ov = v as Object;
						if (ov != null && map.ContainsKey(ov)) v = map[ov];
						try { f.SetValue(dc, v); } catch { }
					}
			}
			return c;
		}
	}
}
