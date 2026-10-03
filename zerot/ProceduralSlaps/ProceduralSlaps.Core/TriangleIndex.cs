using System;

namespace ProceduralSlaps.Core;

public sealed class TriangleIndex
{
	private const int BlockSize = 32;

	private readonly int[] triangles;

	private readonly V3[] low;

	private readonly V3[] high;

	private V3[] vertices;

	private int updateBlock;

	public int LastTriangleTests { get; private set; }

	public TriangleIndex(int[] indices)
	{
		triangles = indices;
		low = new V3[(indices.Length / 3 + 32 - 1) / 32];
		high = new V3[low.Length];
	}

	public void Update(V3[] positions)
	{
		BeginUpdate(positions);
		ContinueUpdate(low.Length);
	}

	public void BeginUpdate(V3[] positions)
	{
		vertices = positions;
		updateBlock = 0;
	}

	public bool ContinueUpdate(int maxBlocks)
	{
		int num = Math.Min(low.Length, updateBlock + maxBlocks);
		while (updateBlock < num)
		{
			int num2 = updateBlock;
			V3 v = new V3(float.MaxValue, float.MaxValue, float.MaxValue);
			V3 v2 = new V3(float.MinValue, float.MinValue, float.MinValue);
			int num3 = Math.Min(triangles.Length, (num2 + 1) * 32 * 3);
			for (int i = num2 * 32 * 3; i < num3; i++)
			{
				V3 v3 = vertices[triangles[i]];
				v.X = Math.Min(v.X, v3.X);
				v.Y = Math.Min(v.Y, v3.Y);
				v.Z = Math.Min(v.Z, v3.Z);
				v2.X = Math.Max(v2.X, v3.X);
				v2.Y = Math.Max(v2.Y, v3.Y);
				v2.Z = Math.Max(v2.Z, v3.Z);
			}
			low[num2] = v;
			high[num2] = v2;
			updateBlock++;
		}
		return updateBlock == low.Length;
	}

	public int Closest(V3 p, V3 normal, float distance, int anchor, out V3 point, out V3 direction)
	{
		direction = default;
		point = direction;
		LastTriangleTests = 0;
		int result = -1;
		float num = distance * distance;
		bool flag = anchor >= 0 && anchor < vertices.Length;
		for (int i = 0; i < low.Length; i++)
		{
			if (BoxDistance(p, low[i], high[i]) > num || (flag && BoxDistance(vertices[anchor], low[i], high[i]) > distance * distance))
			{
				continue;
			}
			int num2 = Math.Min(triangles.Length, (i + 1) * 32 * 3);
			for (int j = i * 32 * 3; j < num2; j += 3)
			{
				V3 v = vertices[triangles[j]];
				V3 v2 = vertices[triangles[j + 1]];
				V3 v3 = vertices[triangles[j + 2]];
				if (flag && (v - vertices[anchor]).LengthSquared > distance * distance && (v2 - vertices[anchor]).LengthSquared > distance * distance && (v3 - vertices[anchor]).LengthSquared > distance * distance)
				{
					continue;
				}
				LastTriangleTests++;
				V3 normalized = V3.Cross(v2 - v, v3 - v).Normalized;
				if (!(normalized.LengthSquared < 0.5f) && (flag || !(Math.Abs(V3.Dot(normalized, normal)) < 0.2f)))
				{
					V3 v4 = MathEx.ClosestWeights(p, v, v2, v3);
					V3 v5 = v * v4.X + v2 * v4.Y + v3 * v4.Z;
					float lengthSquared = (p - v5).LengthSquared;
					if (!(lengthSquared >= num))
					{
						result = j / 3;
						num = lengthSquared;
						point = v5;
						direction = normalized;
					}
				}
			}
		}
		return result;
	}

	private static float BoxDistance(V3 p, V3 min, V3 max)
	{
		float num = Math.Max(min.X - p.X, Math.Max(0f, p.X - max.X));
		float num2 = Math.Max(min.Y - p.Y, Math.Max(0f, p.Y - max.Y));
		float num3 = Math.Max(min.Z - p.Z, Math.Max(0f, p.Z - max.Z));
		return num * num + num2 * num2 + num3 * num3;
	}
}
