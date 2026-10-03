using System;

namespace ProceduralSlaps.Core;

public sealed class PainState
{
	private float quietTime;

	public float LastImpact { get; private set; }

	public float LastIrritation { get; private set; }

	public float Value { get; private set; }

	public void Record(Response response, float sensitivity)
	{
		float num = ((!MathEx.Finite(response.Impact)) ? 0f : MathEx.Saturate(response.Impact));
		float num2 = ((!MathEx.Finite(response.Irritation)) ? 0f : MathEx.Saturate(response.Irritation));
		if (!(num <= 0f) || !(num2 <= 0f))
		{
			if (num > 0f)
			{
				LastImpact = num;
			}
			if (num2 > 0f)
			{
				LastIrritation = num2;
			}
			if (MathEx.Finite(sensitivity) && !(sensitivity <= 0f))
			{
				quietTime = 0f;
				Value = MathEx.Saturate(Value + (num + num2) * sensitivity);
			}
		}
	}

	public void Decay(float dt)
	{
		if (!(Value <= 0f) && !(dt <= 0f) && MathEx.Finite(dt))
		{
			quietTime += dt;
			Value *= (float)Math.Exp(-4.605170186 * (double)dt / 60.0);
			if (quietTime >= 60f && Value < 0.005f)
			{
				Value = 0f;
			}
		}
	}

	public void Clear()
	{
		float num = (Value = (quietTime = 0f));
		num = (LastIrritation = num);
		LastImpact = num;
	}
}
