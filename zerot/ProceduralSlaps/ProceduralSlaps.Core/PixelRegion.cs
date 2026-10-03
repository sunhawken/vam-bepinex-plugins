using System;

namespace ProceduralSlaps.Core;

public struct PixelRegion
{
	public int X0;

	public int Y0;

	public int X1;

	public int Y1;

	public bool Empty => X1 <= X0 || Y1 <= Y0;

	public void Include(int x, int y)
	{
		if (Empty)
		{
			X0 = x;
			Y0 = y;
			X1 = x + 1;
			Y1 = y + 1;
		}
		else
		{
			X0 = Math.Min(X0, x);
			Y0 = Math.Min(Y0, y);
			X1 = Math.Max(X1, x + 1);
			Y1 = Math.Max(Y1, y + 1);
		}
	}

	public PixelRegion Project(int maskSize, int width, int height)
	{
		if (Empty)
		{
			return default;
		}
		return new PixelRegion
		{
			X0 = Math.Max(0, X0 - 1) * width / maskSize,
			Y0 = Math.Max(0, Y0 - 1) * height / maskSize,
			X1 = (Math.Min(maskSize, X1 + 1) * width + maskSize - 1) / maskSize,
			Y1 = (Math.Min(maskSize, Y1 + 1) * height + maskSize - 1) / maskSize
		};
	}
}
