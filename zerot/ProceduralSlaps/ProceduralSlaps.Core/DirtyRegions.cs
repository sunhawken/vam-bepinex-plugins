using System;
using System.Collections.Generic;

namespace ProceduralSlaps.Core;

public sealed class DirtyRegions
{
	private readonly int size;

	private readonly int side;

	private readonly bool[] tiles;

	private bool full;

	public DirtyRegions(int maskSize)
	{
		size = maskSize;
		side = (size + 8 - 1) / 8;
		tiles = new bool[side * side];
	}

	public void Clear()
	{
		full = false;
		Array.Clear(tiles, 0, tiles.Length);
	}

	public void All()
	{
		full = true;
	}

	public void Include(int x, int y)
	{
		if (full)
		{
			return;
		}
		int num = Math.Max(0, x - 1) / 8;
		int num2 = Math.Min(size - 1, x + 1) / 8;
		int num3 = Math.Max(0, y - 1) / 8;
		int num4 = Math.Min(size - 1, y + 1) / 8;
		for (int i = num3; i <= num4; i++)
		{
			for (int j = num; j <= num2; j++)
			{
				tiles[i * side + j] = true;
			}
		}
	}

	public void Build(int width, int height, List<PixelRegion> result)
	{
		result.Clear();
		if (full)
		{
			result.Add(new PixelRegion
			{
				X1 = width,
				Y1 = height
			});
			return;
		}
		for (int i = 0; i < side; i++)
		{
			for (int j = 0; j < side; j++)
			{
				if (tiles[i * side + j])
				{
					int num = j;
					for (; j + 1 < side && tiles[i * side + j + 1]; j++)
					{
					}
					result.Add(new PixelRegion
					{
						X0 = num * 8 * width / size,
						X1 = Math.Min(size, (j + 1) * 8) * width / size,
						Y0 = i * 8 * height / size,
						Y1 = Math.Min(size, (i + 1) * 8) * height / size
					});
				}
			}
		}
	}
}
