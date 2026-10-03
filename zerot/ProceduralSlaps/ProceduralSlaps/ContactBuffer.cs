using System.Collections.Generic;

namespace ProceduralSlaps;

public sealed class ContactBuffer
{
	private readonly List<ContactSample> samples = new List<ContactSample>(64);

	public int Count => samples.Count;

	public bool Add(ContactSample sample)
	{
		sample.FirstTime = sample.Time;
		sample.LastIrritation = sample.Response.Irritation;
		for (int i = 0; i < samples.Count; i++)
		{
			if (ContactSample.TryMerge(samples[i], sample, out var merged))
			{
				samples[i] = merged;
				return true;
			}
		}
		if (samples.Count >= 64)
		{
			return false;
		}
		samples.Add(sample);
		return true;
	}

	public bool HasReady(float now)
	{
		return samples.Count > 0 && now - samples[0].FirstTime >= 0.05f;
	}

	public void TakeReady(List<ContactSample> destination, float now)
	{
		int num = 0;
		for (int i = 0; i < samples.Count; i++)
		{
			ContactSample contactSample = samples[i];
			if (now - contactSample.FirstTime >= 0.05f)
			{
				destination.Add(contactSample);
			}
			else
			{
				samples[num++] = contactSample;
			}
		}
		if (num < samples.Count)
		{
			samples.RemoveRange(num, samples.Count - num);
		}
	}

	public void Clear()
	{
		samples.Clear();
	}
}
