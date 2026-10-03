namespace ProceduralSlaps.Core;

public sealed class ContactHistory
{
	private float began;

	private float last;

	private float nextImpact;

	private float quietSince = -1f;

	private bool armed = true;

	private bool seen;

	public bool Sustained { get; private set; }

	public bool Observe(float now, float speed, float threshold, bool entered)
	{
		if (!seen || entered || now - last > 0.1f)
		{
			began = now;
		}
		if (!seen || now - last >= 0.15f)
		{
			armed = true;
		}
		seen = true;
		last = now;
		Sustained = now - began >= 0.15f;
		if (speed < threshold * 0.5f)
		{
			if (quietSince < 0f)
			{
				quietSince = now;
			}
			if (now - quietSince >= 0.15f)
			{
				armed = true;
			}
		}
		else
		{
			quietSince = -1f;
		}
		bool flag = armed && speed >= threshold && now >= nextImpact;
		if (speed >= threshold)
		{
			armed = false;
		}
		if (flag)
		{
			nextImpact = now + 0.18f;
		}
		return flag;
	}
}
