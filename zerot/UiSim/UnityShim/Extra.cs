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
	public partial class Texture { public int width { get; set; } public int height { get; set; } public int anisoLevel { get; set; } public FilterMode filterMode { get; set; } public TextureWrapMode wrapMode { get; set; } }
	public partial class Texture2D { public void SetPixels32(object c) { } }
	public partial class Camera { public static CameraCallback onPostRender; }
	public partial class Component
	{
		public T[] GetComponents<T>() where T : class => gameObject.GetComponentsInChildren<T>(true);
		public T GetComponentInParent<T>() where T : class { for (var t = transform; t != null; t = t.parent) { var c = t.gameObject.GetComponent<T>(); if (c != null) return c; } return null; }
	}
	public partial class Transform
	{
		public Transform root { get { var t = this; while (t.parent != null) t = t.parent; return t; } }
	}
	public partial class Camera
	{
		public CameraClearFlags clearFlags { get; set; } public int cullingMask { get; set; } public float depth { get; set; } public float fieldOfView { get; set; } = 60f;
		public float nearClipPlane { get; set; } = 0.3f; public float farClipPlane { get; set; } = 1000f;
		public void CopyFrom(Camera c) { }
	}
	public partial class AudioClip { public static AudioClip Create(string n, int len, int ch, int freq, bool stream) => new AudioClip(); public bool SetData(float[] d, int off) => true; }
	public partial class GameObject { public int layer { get; set; } }
	public partial struct Vector3 { public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude; }
	public static partial class Mathf
	{
		public static float Sign(float f) => f >= 0 ? 1f : -1f;
		public static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));
		// Unity's SmoothDamp (critically damped spring), same formula
		public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime) { return SmoothDamp(current, target, ref currentVelocity, smoothTime, float.PositiveInfinity, Time.deltaTime); }
		public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
		{
			smoothTime = Max(0.0001f, smoothTime);
			float omega = 2f / smoothTime, x = omega * deltaTime;
			float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
			float change = current - target, original = target;
			float maxChange = maxSpeed * smoothTime;
			change = Clamp(change, -maxChange, maxChange);
			target = current - change;
			float temp = (currentVelocity + omega * change) * deltaTime;
			currentVelocity = (currentVelocity - omega * temp) * exp;
			float output = target + (change + temp) * exp;
			if (original - current > 0f == output > original) { output = original; currentVelocity = (output - original) / deltaTime; }
			return output;
		}
	}
	public partial struct Vector2 { public float sqrMagnitude => x * x + y * y; }
	public static partial class Time { public static float fixedDeltaTime { get; set; } = 0.02f; }
	public static partial class GUI { public static Color backgroundColor2 => Color.white; }
	public static partial class GUIUtility { }
}

namespace UnityEngine { public partial class GameObject { public T GetComponentInChildren<T>() where T : class => GetComponentInChildren<T>(true); } }

namespace UnityEngine
{
	public partial struct Vector2
	{
		public float magnitude => (float)System.Math.Sqrt(x * x + y * y);
		public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : new Vector2(0, 0); } }
		public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
	}
	public partial struct Color
	{
		public static Color grey => new Color(.5f, .5f, .5f, 1);
		public static Color cyan => new Color(0, 1, 1, 1);
		public static Color magenta => new Color(1, 0, 1, 1);
		public static Color HSVToRGB(float h, float s, float v)
		{
			if (s <= 0f) return new Color(v, v, v, 1);
			h = (h - (float)System.Math.Floor(h)) * 6f; int i = (int)h; float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
			switch (i) { case 0: return new Color(v, t, p, 1); case 1: return new Color(q, v, p, 1); case 2: return new Color(p, v, t, 1); case 3: return new Color(p, q, v, 1); case 4: return new Color(t, p, v, 1); default: return new Color(v, p, q, 1); }
		}
		public static void RGBToHSV(Color c, out float h, out float s, out float v)
		{
			float max = System.Math.Max(c.r, System.Math.Max(c.g, c.b)), min = System.Math.Min(c.r, System.Math.Min(c.g, c.b)), d = max - min;
			v = max; s = max <= 0 ? 0 : d / max; h = 0;
			if (d > 0) { if (max == c.r) h = ((c.g - c.b) / d) % 6f; else if (max == c.g) h = (c.b - c.r) / d + 2f; else h = (c.r - c.g) / d + 4f; h /= 6f; if (h < 0) h += 1f; }
		}
	}
	public static partial class Mathf
	{
		public static float MoveTowards(float c, float t, float d) => System.Math.Abs(t - c) <= d ? t : c + Sign(t - c) * d;
		public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);
	}
	public static partial class Time { public static int frameCount { get; set; } }
}
namespace UnityEngine { public partial struct Color { public static Color red => new Color(1, 0, 0, 1); public static Color green => new Color(0, 1, 0, 1); public static Color blue => new Color(0, 0, 1, 1); public static Color yellow => new Color(1, .92f, .016f, 1); } }
