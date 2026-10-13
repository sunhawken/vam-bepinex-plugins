using System;

namespace UnityEngine
{
	// real vector / quaternion maths (the simulator needs correct angles and rotations for gaze, pose and gesture code)
	public partial struct Quaternion
	{
		public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
		public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
			a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
			a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
		public static Vector3 operator *(Quaternion q, Vector3 v)
		{
			float x2 = q.x * 2f, y2 = q.y * 2f, z2 = q.z * 2f, xx = q.x * x2, yy = q.y * y2, zz = q.z * z2, xy = q.x * y2, xz = q.x * z2, yz = q.y * z2, wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;
			return new Vector3((1f - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z, (xy + wz) * v.x + (1f - (xx + zz)) * v.y + (yz - wx) * v.z, (xz - wy) * v.x + (yz + wx) * v.y + (1f - (xx + yy)) * v.z);
		}
		public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
		public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
		public override bool Equals(object o) => o is Quaternion q && this == q;
		public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
		public static Quaternion Lerp(Quaternion a, Quaternion b, float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; float s = Dot(a, b) < 0f ? -1f : 1f; float x = a.x + (b.x * s - a.x) * t, y = a.y + (b.y * s - a.y) * t, z = a.z + (b.z * s - a.z) * t, w = a.w + (b.w * s - a.w) * t; float m = (float)Math.Sqrt(x * x + y * y + z * z + w * w); return m < 1e-8f ? identity : new Quaternion(x / m, y / m, z / m, w / m); }
		public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
		public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
		public static float Angle(Quaternion a, Quaternion b) { float d = Math.Min(Math.Abs(Dot(a, b)), 1f); return (float)(Math.Acos(d) * 2.0 * 57.29578); }
		public static Quaternion AngleAxis(float angle, Vector3 axis)
		{
			axis = axis.normalized; float h = angle * 0.0174532924f * 0.5f, s = (float)Math.Sin(h);
			return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
		}
		public static Quaternion Euler(float xd, float yd, float zd)
		{
			float x = xd * 0.0174532924f * 0.5f, y = yd * 0.0174532924f * 0.5f, z = zd * 0.0174532924f * 0.5f;
			float sx = (float)Math.Sin(x), cx = (float)Math.Cos(x), sy = (float)Math.Sin(y), cy = (float)Math.Cos(y), sz = (float)Math.Sin(z), cz = (float)Math.Cos(z);
			// Unity order: Z, X, Y
			return new Quaternion(cy * sx * cz + sy * cx * sz, sy * cx * cz - cy * sx * sz, cy * cx * sz - sy * sx * cz, cy * cx * cz + sy * sx * sz);
		}
		public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
		public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);
		public static Quaternion LookRotation(Vector3 forward, Vector3 up)
		{
			forward = forward.normalized; if (forward.sqrMagnitude < 1e-10f) return identity;
			Vector3 right = Vector3.Cross(up, forward).normalized; if (right.sqrMagnitude < 1e-10f) right = Vector3.Cross(Vector3.forward, forward).normalized;
			Vector3 u = Vector3.Cross(forward, right);
			float m00 = right.x, m01 = right.y, m02 = right.z, m10 = u.x, m11 = u.y, m12 = u.z, m20 = forward.x, m21 = forward.y, m22 = forward.z;
			float num8 = (m00 + m11) + m22; Quaternion q = new Quaternion();
			if (num8 > 0f) { float n = (float)Math.Sqrt(num8 + 1f); q.w = n * 0.5f; n = 0.5f / n; q.x = (m12 - m21) * n; q.y = (m20 - m02) * n; q.z = (m01 - m10) * n; }
			else if (m00 >= m11 && m00 >= m22) { float n = (float)Math.Sqrt(1f + m00 - m11 - m22); float h = 0.5f / n; q.x = 0.5f * n; q.y = (m01 + m10) * h; q.z = (m02 + m20) * h; q.w = (m12 - m21) * h; }
			else if (m11 > m22) { float n = (float)Math.Sqrt(1f + m11 - m00 - m22); float h = 0.5f / n; q.x = (m10 + m01) * h; q.y = 0.5f * n; q.z = (m21 + m12) * h; q.w = (m20 - m02) * h; }
			else { float n = (float)Math.Sqrt(1f + m22 - m00 - m11); float h = 0.5f / n; q.x = (m20 + m02) * h; q.y = (m21 + m12) * h; q.z = 0.5f * n; q.w = (m01 - m10) * h; }
			return q;
		}
		public void SetLookRotation(Vector3 view) { this = LookRotation(view); }
		public void SetLookRotation(Vector3 view, Vector3 up) { this = LookRotation(view, up); }
		public Vector3 eulerAngles
		{
			get
			{
				// rotation matrix -> ZXY euler (Unity convention)
				float sinp = 2f * (w * x - y * z); float ex, ey, ez;
				if (Math.Abs(sinp) >= 0.9999f) { ex = (float)(Math.PI / 2 * Math.Sign(sinp)); ey = (float)Math.Atan2(-2f * (x * z - w * y), 1f - 2f * (y * y + z * z)); ez = 0f; }
				else { ex = (float)Math.Asin(sinp); ey = (float)Math.Atan2(2f * (x * z + w * y), 1f - 2f * (x * x + y * y)); ez = (float)Math.Atan2(2f * (x * y + w * z), 1f - 2f * (x * x + z * z)); }
				float f = 57.29578f; Func<float, float> n = a => { a *= f; a %= 360f; if (a < 0) a += 360f; return a; };
				return new Vector3(n(ex), n(ey), n(ez));
			}
			set { this = Euler(value.x, value.y, value.z); }
		}
	}

	public partial struct Vector3
	{
		public static Vector3 right => new Vector3(1, 0, 0);
		public static Vector3 left => new Vector3(-1, 0, 0);
		public static Vector3 down => new Vector3(0, -1, 0);
		public static Vector3 back => new Vector3(0, 0, -1);
		public static Vector3 operator -(Vector3 v) => new Vector3(-v.x, -v.y, -v.z);
		public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
		public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
		public override bool Equals(object o) => o is Vector3 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
		public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
		public static float Angle(Vector3 from, Vector3 to)
		{
			float d = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude); if (d < 1e-15f) return 0f;
			return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Dot(from, to) / d))) * 57.29578);
		}
		public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
		{
			float u = Angle(from, to); float sign = Math.Sign(Dot(axis, Cross(from, to))); return u * (sign == 0 ? 1 : sign);
		}
	}

	public partial struct Vector3
	{
		public float this[int i]
		{
			get { switch (i) { case 0: return x; case 1: return y; case 2: return z; default: throw new System.IndexOutOfRangeException(); } }
			set { switch (i) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; default: throw new System.IndexOutOfRangeException(); } }
		}
	}

	public partial struct Vector4
	{
		public float this[int i]
		{
			get { switch (i) { case 0: return x; case 1: return y; case 2: return z; case 3: return w; default: throw new System.IndexOutOfRangeException(); } }
			set { switch (i) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; case 3: w = value; break; default: throw new System.IndexOutOfRangeException(); } }
		}
		public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
		public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
		public static Vector4 operator *(Vector4 a, float d) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
		public static Vector4 operator *(float d, Vector4 a) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
		public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
		public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
		public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
		public override bool Equals(object o) => o is Vector4 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode() ^ w.GetHashCode();
	}
}

// VaM's own ToList extension (global namespace, so it wins over System.Linq in a script): the generated stub returns an empty list
public static partial class IEnumerableExtension
{
	public static System.Collections.Generic.List<T> ToList<T>(this System.Collections.Generic.IEnumerable<T> collection) { return new System.Collections.Generic.List<T>(collection); }
}

namespace UnityEngine
{
	public partial struct Vector3
	{
		public static Vector3 ClampMagnitude(Vector3 v, float max) { float m = v.magnitude; return m > max && m > 0f ? v * (max / m) : v; }
	}
}
