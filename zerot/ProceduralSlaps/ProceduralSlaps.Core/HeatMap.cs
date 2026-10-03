using System;
using System.Collections.Generic;

namespace ProceduralSlaps.Core;

public sealed class HeatMap
{
	public readonly int Size;

	public readonly float[] Impact;

	public readonly float[] Irritation;

	public readonly int[] Regions;

	private readonly float[] weights;

	private readonly float[] hiddenImpact;

	private readonly float[] hiddenIrritation;

	private readonly List<int> touched;

	private readonly List<int> active;

	private readonly List<int> changed;

	private readonly bool[] inActive;

	private readonly bool[] inChanged;

	public int ChangedCount => changed.Count;

	public int PendingTexels => touched.Count;

	public int ActiveTexels => active.Count;

	public bool Active { get; private set; }

	public HeatMap(int size)
	{
		if (size < 8 || size > 1024)
		{
			throw new ArgumentOutOfRangeException("size");
		}
		Size = size;
		Impact = new float[size * size];
		Irritation = new float[size * size];
		hiddenImpact = new float[size * size];
		hiddenIrritation = new float[size * size];
		weights = new float[size * size];
		Regions = new int[size * size];
		inActive = new bool[size * size];
		inChanged = new bool[size * size];
		touched = new List<int>(size * size);
		active = new List<int>(size * size);
		changed = new List<int>(size * size);
	}

	public int ChangedAt(int index)
	{
		return changed[index];
	}

	private void Changed(int index)
	{
		if (!inChanged[index])
		{
			inChanged[index] = true;
			changed.Add(index);
		}
	}

	public void MarkActiveChanged()
	{
		for (int i = 0; i < active.Count; i++)
		{
			Changed(active[i]);
		}
	}

	public void ClearChanges()
	{
		for (int i = 0; i < changed.Count; i++)
		{
			inChanged[changed[i]] = false;
		}
		changed.Clear();
	}

	public void Triangle(V3 a, V3 b, V3 c, V3 ua, V3 ub, V3 uc, V3 center, float radius, int region)
	{
		if (!a.Finite || !b.Finite || !c.Finite || !ua.Finite || !ub.Finite || !uc.Finite || !center.Finite || !MathEx.Finite(radius) || radius <= 0f)
		{
			return;
		}
		float num = (ub.Y - uc.Y) * (ua.X - uc.X) + (uc.X - ub.X) * (ua.Y - uc.Y);
		if (Math.Abs(num) < 1E-12f)
		{
			return;
		}
		int num2 = (int)MathEx.Clamp((float)Math.Floor(Math.Min(ua.X, Math.Min(ub.X, uc.X)) * (float)Size), 0f, Size - 1);
		int num3 = (int)MathEx.Clamp((float)Math.Ceiling(Math.Max(ua.X, Math.Max(ub.X, uc.X)) * (float)Size), 0f, Size - 1);
		int num4 = (int)MathEx.Clamp((float)Math.Floor(Math.Min(ua.Y, Math.Min(ub.Y, uc.Y)) * (float)Size), 0f, Size - 1);
		int num5 = (int)MathEx.Clamp((float)Math.Ceiling(Math.Max(ua.Y, Math.Max(ub.Y, uc.Y)) * (float)Size), 0f, Size - 1);
		float num6 = 1f / num;
		float num7 = radius * radius;
		for (int i = num4; i <= num5; i++)
		{
			for (int j = num2; j <= num3; j++)
			{
				float num8 = ((float)j + 0.5f) / (float)Size;
				float num9 = ((float)i + 0.5f) / (float)Size;
				float num10 = ((ub.Y - uc.Y) * (num8 - uc.X) + (uc.X - ub.X) * (num9 - uc.Y)) * num6;
				float num11 = ((uc.Y - ua.Y) * (num8 - uc.X) + (ua.X - uc.X) * (num9 - uc.Y)) * num6;
				float num12 = 1f - num10 - num11;
				if (num10 < -1E-05f || num11 < -1E-05f || num12 < -1E-05f)
				{
					continue;
				}
				float lengthSquared = (a * num10 + b * num11 + c * num12 - center).LengthSquared;
				if (!(lengthSquared >= num7))
				{
					float num13 = 1f - lengthSquared / num7;
					num13 *= num13;
					int num14 = i * Size + j;
					if (weights[num14] == 0f)
					{
						touched.Add(num14);
					}
					if (num13 > weights[num14])
					{
						weights[num14] = num13;
						Regions[num14] = region;
					}
				}
			}
		}
	}

	public void Seed(float u, float v, int region)
	{
		if (MathEx.Finite(u) && MathEx.Finite(v) && !(u < 0f) && !(u > 1f) && !(v < 0f) && !(v > 1f))
		{
			int num = Math.Min(Size - 1, (int)(v * (float)Size)) * Size + Math.Min(Size - 1, (int)(u * (float)Size));
			if (!(weights[num] > 0f))
			{
				touched.Add(num);
				weights[num] = 1f;
				Regions[num] = region;
			}
		}
	}

	public void Commit(float impact, float irritation, float maxIrritation)
	{
		for (int i = 0; i < touched.Count; i++)
		{
			int num = touched[i];
			float num2 = Impact[num];
			float num3 = Irritation[num];
			Impact[num] = MathEx.Saturate(Impact[num] + impact * weights[num]);
			Irritation[num] = MathEx.Clamp(Irritation[num] + irritation * weights[num], 0f, maxIrritation);
			hiddenImpact[num] = MathEx.Clamp(hiddenImpact[num] + Impact[num] - num2, 0f, Impact[num]);
			hiddenIrritation[num] = MathEx.Clamp(hiddenIrritation[num] + Irritation[num] - num3, 0f, Irritation[num]);
			Changed(num);
			if ((Impact[num] > 0f || Irritation[num] > 0f) && !inActive[num])
			{
				inActive[num] = true;
				active.Add(num);
			}
			weights[num] = 0f;
		}
		touched.Clear();
		Active = active.Count > 0;
	}

	public void Decay(float dt, Settings s)
	{
		if (!Active || dt <= 0f || !MathEx.Finite(dt))
		{
			return;
		}
		float num = (float)Math.Exp(-4.605170186 * (double)dt / (double)s.ImpactDecay.Value);
		float num2 = (float)Math.Exp(-4.605170186 * (double)dt / (double)s.IrritationDecay.Value);
		float num3 = (float)Math.Exp(-4.605170186 * (double)dt / 1.0);
		float num4 = num * num3;
		float num5 = num2 * num3;
		for (int num6 = active.Count - 1; num6 >= 0; num6--)
		{
			int num7 = active[num6];
			Changed(num7);
			Impact[num7] *= num;
			float num8 = Irritation[num7] * num2;
			Irritation[num7] = Math.Min(s.MaxIrritation.Value, num8);
			if (Impact[num7] < 0.001f)
			{
				Impact[num7] = 0f;
			}
			if (Irritation[num7] < 1E-05f)
			{
				Irritation[num7] = 0f;
			}
			hiddenImpact[num7] = Math.Min(Impact[num7], hiddenImpact[num7] * num4);
			hiddenIrritation[num7] = MathEx.Clamp(hiddenIrritation[num7] * num5 + Irritation[num7] - num8, 0f, Irritation[num7]);
			if (hiddenImpact[num7] < 1E-05f)
			{
				hiddenImpact[num7] = 0f;
			}
			if (hiddenIrritation[num7] < 1E-05f)
			{
				hiddenIrritation[num7] = 0f;
			}
			if (Impact[num7] == 0f && Irritation[num7] == 0f)
			{
				inActive[num7] = false;
				active[num6] = active[active.Count - 1];
				active.RemoveAt(active.Count - 1);
			}
		}
		Active = active.Count > 0;
	}

	public float RednessAt(int i, float strength)
	{
		return MathEx.Saturate((Impact[i] + Irritation[i]) * strength);
	}

	public float VisibleRednessAt(int i, float strength)
	{
		return MathEx.Saturate((Impact[i] - hiddenImpact[i] + Irritation[i] - hiddenIrritation[i]) * strength);
	}

	public void Levels(float[] impacts, float[] irritations)
	{
		for (int i = 0; i < active.Count; i++)
		{
			int num = active[i];
			int num2 = Regions[num];
			if (num2 >= 0 && num2 < impacts.Length && num2 < irritations.Length)
			{
				impacts[num2] = Math.Max(impacts[num2], Impact[num]);
				irritations[num2] = Math.Max(irritations[num2], Irritation[num]);
			}
		}
	}

	public void Clear()
	{
		for (int i = 0; i < active.Count; i++)
		{
			int num = active[i];
			Changed(num);
			Impact[num] = (Irritation[num] = (hiddenImpact[num] = (hiddenIrritation[num] = 0f)));
			inActive[num] = false;
		}
		for (int j = 0; j < touched.Count; j++)
		{
			weights[touched[j]] = 0f;
		}
		touched.Clear();
		Active = false;
		active.Clear();
	}
}
