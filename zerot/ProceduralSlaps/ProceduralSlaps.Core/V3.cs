using System;

namespace ProceduralSlaps.Core;

public struct V3(float x, float y, float z)
{
	public float X = x;

	public float Y = y;

	public float Z = z;

	public float LengthSquared => Dot(this, this);

	public V3 Normalized
	{
		get
		{
			float num = (float)Math.Sqrt(LengthSquared);
			return (!(num > 1E-12f)) ? default(V3) : (this * (1f / num));
		}
	}

	public bool Finite => MathEx.Finite(X) && MathEx.Finite(Y) && MathEx.Finite(Z);

	public static V3 operator +(V3 a, V3 b)
	{
		return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
	}

	public static V3 operator -(V3 a, V3 b)
	{
		return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
	}

	public static V3 operator *(V3 a, float b)
	{
		return new V3(a.X * b, a.Y * b, a.Z * b);
	}

	public static float Dot(V3 a, V3 b)
	{
		return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
	}

	public static V3 Cross(V3 a, V3 b)
	{
		return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
	}
}
