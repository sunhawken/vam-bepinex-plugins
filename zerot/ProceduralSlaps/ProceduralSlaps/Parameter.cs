using System;

namespace ProceduralSlaps;

public sealed class Parameter
{
	public readonly string Id;

	public readonly string Label;

	public readonly float Default;

	public readonly float Min;

	public readonly float Max;

	public float Value;

	public Parameter(string id, string label, float value, float min, float max)
	{
		Id = id;
		Label = label;
		Default = (Value = value);
		Min = min;
		Max = max;
	}

	public void Set(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value) || value < Min || value > Max)
		{
			throw new ArgumentException("Invalid setting: " + Id);
		}
		Value = value;
	}
}
