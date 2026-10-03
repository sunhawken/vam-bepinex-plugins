using System.Collections.Generic;

namespace ProceduralSlaps.Core;

public sealed class SensorDensity
{
	private struct Site
	{
		public int Region;

		public int Bone;

		public V3 Point;
	}

	private readonly List<Site> sites = new List<Site>();

	public static bool Dense(int region)
	{
		return region == 0 || region == 3 || region == 4;
	}

	public bool Keep(int region, int bone, V3 point)
	{
		if (Dense(region))
		{
			return true;
		}
		float num = 0.0036f;
		for (int i = 0; i < sites.Count; i++)
		{
			if (sites[i].Region == region && sites[i].Bone == bone && (sites[i].Point - point).LengthSquared < num)
			{
				return false;
			}
		}
		sites.Add(new Site
		{
			Region = region,
			Bone = bone,
			Point = point
		});
		return true;
	}
}
