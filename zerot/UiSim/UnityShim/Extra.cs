using System;

namespace UnityEngine
{
	public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
	public partial struct Color
	{
		public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
		public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);
	}
	public partial struct Vector2
	{
		public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
		public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
		public override bool Equals(object o) => o is Vector2 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
	}
	public partial struct Vector3
	{
		public static Vector3 forward => new Vector3(0, 0, 1);
		public static Vector3 up => new Vector3(0, 1, 0);
		public float magnitude => Mathf.Sqrt(sqrMagnitude);
		public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this * (1f / m) : zero; } }
		public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
		public static Vector3 Normalize(Vector3 v) => v.normalized;
		public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * (Dot(v, n) / Dot(n, n));
		public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
	}
	public partial class Texture { public int width { get; set; } public int height { get; set; } public int anisoLevel { get; set; } public int filterMode { get; set; } public int wrapMode { get; set; } }
	public partial class Texture2D { public void SetPixels32(object c) { } }
	public partial class Camera { public static CameraCallback onPostRender; }
	public partial class Component
	{
		public T[] GetComponents<T>() where T : class => gameObject.GetComponentsInChildren<T>(true);
		public T GetComponentInParent<T>() where T : class { for (var t = transform; t != null; t = t.parent) { var c = t.gameObject.GetComponent<T>(); if (c != null) return c; } return null; }
	}
	public partial class Transform
	{
		public Vector3 forward => Vector3.forward;
		public Transform root { get { var t = this; while (t.parent != null) t = t.parent; return t; } }
		public Vector3 lossyScale => Vector3.one;
		public Vector3 InverseTransformPoint(Vector3 v) => v;
		public Vector3 InverseTransformDirection(Vector3 v) => v;
		public Vector3 TransformPoint(Vector3 v) => v;
		public Vector3 TransformDirection(Vector3 v) => v;
	}
	public partial class Camera
	{
		public CameraClearFlags clearFlags { get; set; } public int cullingMask { get; set; } public float depth { get; set; } public float fieldOfView { get; set; } = 60f;
		public float nearClipPlane { get; set; } = 0.3f; public float farClipPlane { get; set; } = 1000f;
		public void CopyFrom(Camera c) { }
	}
	public partial class AudioClip { public static AudioClip Create(string n, int len, int ch, int freq, bool stream) => new AudioClip(); public bool SetData(float[] d, int off) => true; }
	public partial struct Color { public static Color red => new Color(1, 0, 0, 1); public static Color green => new Color(0, 1, 0, 1); public static Color blue => new Color(0, 0, 1, 1); public static Color yellow => new Color(1, .92f, .016f, 1); }
	public partial class GameObject { public int layer { get; set; } }
	public partial struct Quaternion
	{
		public static Quaternion Euler(float x, float y, float z) => identity;
		public static Quaternion operator *(Quaternion a, Quaternion b) => identity;
		public static Vector3 operator *(Quaternion a, Vector3 b) => b;
	}
	public partial class Transform { public Vector3 eulerAngles { get; set; } public Vector3 up => Vector3.up; public Quaternion rotation { get; set; } = Quaternion.identity; }
	public partial struct Vector3 { public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude; }
	public partial class Object { public static T[] FindObjectsOfType<T>() where T : Object => new T[0]; }
	public static partial class Mathf { public static float Sign(float f) => f >= 0 ? 1f : -1f; }
	public static partial class Time { public static float fixedDeltaTime { get; set; } = 0.02f; }
	public static partial class GUI { public static Color backgroundColor2 => Color.white; }
	public static partial class GUIUtility { }
}
