namespace ProceduralSlaps.Core;

public sealed class ContactMotion
{
	private V3 originSource;

	private V3 originRelative;

	private V3 lastSource;

	private V3 lastRelative;

	private float began;

	private float last;

	private bool initialized;

	public V3 TangentVelocity { get; private set; }

	public bool SourceMoving { get; private set; }

	public void Reset()
	{
		initialized = false;
		TangentVelocity = default;
		SourceMoving = false;
	}

	public void Observe(float now, V3 sourcePoint, V3 relativePoint, V3 normal)
	{
		TangentVelocity = default;
		SourceMoving = false;
		if (!initialized || now - last > 0.25f || now < last)
		{
			initialized = true;
			began = (last = now);
			originSource = (lastSource = sourcePoint);
			originRelative = (lastRelative = relativePoint);
			return;
		}
		float num = now - last;
		float num2 = now - began;
		if (!(num <= 0f))
		{
			V3 v = sourcePoint - lastSource;
			V3 v2 = relativePoint - lastRelative;
			V3 v3 = relativePoint - originRelative;
			V3 v4 = v3 - normal * V3.Dot(v3, normal);
			V3 v5 = v2 - normal * V3.Dot(v2, normal);
			float num3 = 0.03f * num;
			SourceMoving = v.LengthSquared >= num3 * num3 && (sourcePoint - originSource).LengthSquared >= 9E-06f;
			if (SourceMoving && num2 >= 0.05f && v4.LengthSquared >= 9E-06f && v5.LengthSquared >= 0.0009f * num * num)
			{
				TangentVelocity = v4 * (1f / num2);
			}
			if (num2 >= 0.1f)
			{
				originSource = sourcePoint;
				originRelative = relativePoint;
				began = now;
			}
			lastSource = sourcePoint;
			lastRelative = relativePoint;
			last = now;
		}
	}
}
