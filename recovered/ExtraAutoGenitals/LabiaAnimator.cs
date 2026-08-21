using System;
using System.Linq;
using UnityEngine;

public class LabiaAnimator
{
	private const int VELOCITY_SMOOTH_LOOKBACK = 64;

	private const float VELOCITY_SMOOTH_STDDEV_MAX = 2f;

	private const float ANIMATION_SPEED_MIN = 0.08f;

	private int _iteration;

	private float[] _velHistory = new float[64];

	private float _sd1;

	private float _sd2;

	private float _sd3;

	public DAZMorph Morph { get; private set; }

	public float MorphDefault
	{
		get
		{
			DAZMorph morph = Morph;
			float? obj;
			if (morph == null)
			{
				obj = null;
			}
			else
			{
				JSONStorableFloat jsonFloat = morph.jsonFloat;
				obj = ((jsonFloat != null) ? new float?(jsonFloat.defaultVal) : ((float?)null));
			}
			float? num = obj;
			return num.GetValueOrDefault();
		}
	}

	public float MorphCurrent
	{
		get
		{
			DAZMorph morph = Morph;
			if (morph == null)
			{
				return 0f;
			}
			return morph.morphValue;
		}
	}

	public float MorphRestingValue { get; set; }

	public bool IsInwardMorph { get; set; }

	public float InwardMax { get; set; }

	public float InwardExaggeration { get; set; }

	public float OutwardMax { get; set; }

	public float OutwardExaggeration { get; set; }

	public LabiaAnimator(DAZMorph morph, bool isInwardMorph, float inwardMax, float outwardMax, float inwardExaggeration = 0f, float outwardExaggeration = 0f)
	{
		Morph = morph;
		IsInwardMorph = isInwardMorph;
		InwardMax = inwardMax;
		OutwardMax = outwardMax;
		InwardExaggeration = inwardExaggeration;
		OutwardExaggeration = outwardExaggeration;
		MorphRestingValue = MorphDefault;
	}

	public float? NextMorphValue(float? velocityRaw, float friction)
	{
		friction = Mathf.Clamp(friction, 0f, 1f);
		float num = MorphRestingValue - (IsInwardMorph ? (OutwardMax + OutwardExaggeration) : (InwardMax + InwardExaggeration));
		float num2 = MorphRestingValue + (IsInwardMorph ? (InwardMax + InwardExaggeration) : (OutwardMax + OutwardExaggeration));
		float num3 = Mathf.Clamp(velocityRaw.GetValueOrDefault(), -1f, 1f);
		if (friction <= 0f)
		{
			return null;
		}
		if (!velocityRaw.HasValue)
		{
			return Mathf.Clamp(Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _sd2, 0.08f), num, num2);
		}
		if (VelocityLooksLikeMistake(num3))
		{
			return MorphCurrent;
		}
		if (Mathf.Approximately(num3, 0f))
		{
			return Mathf.Clamp(Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _sd3, 100f), num, num2);
		}
		Mathf.InverseLerp(num, num2, MorphCurrent);
		float num4 = num3 * friction * (IsInwardMorph ? 1f : (-1f)) * 10f;
		float num5 = Mathf.Clamp(MorphRestingValue + num4, num, num2);
		return Mathf.SmoothDamp(MorphCurrent, num5, ref _sd1, 0.08f);
	}

	private bool VelocityLooksLikeMistake(float velocity)
	{
		int num = _iteration % 63;
		_velHistory[num] = velocity;
		_iteration = (_iteration + 1) % 64;
		float avg = _velHistory.Average();
		float num2 = (float)Math.Sqrt(_velHistory.Average((float v) => Math.Pow(v - avg, 2.0)));
		if (num2 > 0f && Mathf.Abs((velocity - avg) / num2) > 2f)
		{
			return true;
		}
		return false;
	}
}
