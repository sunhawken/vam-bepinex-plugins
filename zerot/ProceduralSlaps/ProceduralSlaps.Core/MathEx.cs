using System;

namespace ProceduralSlaps.Core;

public static class MathEx
{
	public static bool Finite(float x)
	{
		return !float.IsNaN(x) && !float.IsInfinity(x);
	}

	public static float Clamp(float x, float min, float max)
	{
		return Math.Max(min, Math.Min(max, x));
	}

	public static float Saturate(float x)
	{
		return Clamp(x, 0f, 1f);
	}

	public static V3 ClosestWeights(V3 p, V3 a, V3 b, V3 c)
	{
		V3 a2 = b - a;
		V3 v = c - a;
		V3 b2 = p - a;
		if (V3.Cross(a2, v).LengthSquared < 1E-20f)
		{
			return new V3(1f, 0f, 0f);
		}
		float num = V3.Dot(a2, b2);
		float num2 = V3.Dot(v, b2);
		if (num <= 0f && num2 <= 0f)
		{
			return new V3(1f, 0f, 0f);
		}
		V3 b3 = p - b;
		float num3 = V3.Dot(a2, b3);
		float num4 = V3.Dot(v, b3);
		if (num3 >= 0f && num4 <= num3)
		{
			return new V3(0f, 1f, 0f);
		}
		float num5 = num * num4 - num3 * num2;
		if (num5 <= 0f && num >= 0f && num3 <= 0f)
		{
			float num6 = num / (num - num3);
			return new V3(1f - num6, num6, 0f);
		}
		V3 b4 = p - c;
		float num7 = V3.Dot(a2, b4);
		float num8 = V3.Dot(v, b4);
		if (num8 >= 0f && num7 <= num8)
		{
			return new V3(0f, 0f, 1f);
		}
		float num9 = num7 * num2 - num * num8;
		if (num9 <= 0f && num2 >= 0f && num8 <= 0f)
		{
			float num10 = num2 / (num2 - num8);
			return new V3(1f - num10, 0f, num10);
		}
		float num11 = num3 * num8 - num7 * num4;
		if (num11 <= 0f && num4 >= num3 && num7 >= num8)
		{
			float num12 = (num4 - num3) / (num4 - num3 + num7 - num8);
			return new V3(0f, 1f - num12, num12);
		}
		float num13 = 1f / (num11 + num9 + num5);
		float num14 = num9 * num13;
		float num15 = num5 * num13;
		return new V3(1f - num14 - num15, num14, num15);
	}
}
