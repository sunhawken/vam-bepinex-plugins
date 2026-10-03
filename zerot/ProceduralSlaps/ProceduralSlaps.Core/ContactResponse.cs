using System;

namespace ProceduralSlaps.Core;

public static class ContactResponse
{
	public static float SkinImpulse(float impulse, float colliderMass, bool anchoredSkin)
	{
		if (!anchoredSkin || colliderMass <= 0f || !MathEx.Finite(colliderMass))
		{
			return impulse;
		}
		return impulse * MathEx.Clamp(0.1f / colliderMass, 1f, 20f);
	}

	public static float NormalSpeed(V3 pairVelocity, V3 pointVelocity, V3 normal)
	{
		normal = normal.Normalized;
		return Math.Max(Math.Abs(V3.Dot(pairVelocity, normal)), Math.Abs(V3.Dot(pointVelocity, normal)));
	}

	public static Response Evaluate(Settings s, V3 velocity, V3 normal, float impulse, float area, float friction, float dt, bool impactEdge, bool sustained)
	{
		Response result = default;
		if (!velocity.Finite || !normal.Finite || normal.LengthSquared < 0.5f || !MathEx.Finite(impulse) || impulse <= 0f || !MathEx.Finite(area) || area <= 0f || !MathEx.Finite(friction) || friction < 0f || !MathEx.Finite(dt) || dt <= 0f)
		{
			return result;
		}
		normal = normal.Normalized;
		float num = Math.Abs(V3.Dot(velocity, normal));
		float num2 = (float)Math.Sqrt(Math.Max(0f, (velocity - normal * V3.Dot(velocity, normal)).LengthSquared));
		result.Radius = MathEx.Clamp((float)Math.Sqrt((double)area / Math.PI) * s.ImpactRadius.Value, 0.008f, 0.12f);
		if (impactEdge && num >= s.ImpactThreshold.Value && impulse > s.ImpulseThreshold.Value)
		{
			result.Impact = MathEx.Saturate((impulse - s.ImpulseThreshold.Value) * s.ImpactStrength.Value * s.ImpactSensitivity.Value * (float)Math.Sqrt(0.003f / area));
		}
		if (sustained && num2 > 0.03f)
		{
			float num3 = impulse / (dt * area);
			result.Irritation = Math.Min(s.MaxIrritation.Value, num3 / 5000f * s.PressureSensitivity.Value * friction * s.FrictionSensitivity.Value * (num2 - 0.03f) * dt * s.AccumulationRate.Value * s.IrritationSensitivity.Value);
		}
		return result;
	}
}
