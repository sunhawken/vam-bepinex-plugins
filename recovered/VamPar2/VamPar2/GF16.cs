namespace VamPar2;

internal static class GF16
{
	public const int ORDER = 65535;

	private static readonly ushort[] Exp;

	private static readonly ushort[] Log;

	static GF16()
	{
		Exp = new ushort[131070];
		Log = new ushort[65536];
		int num = 1;
		for (int i = 0; i < 65535; i++)
		{
			Exp[i] = (Exp[i + 65535] = (ushort)num);
			Log[num] = (ushort)i;
			num <<= 1;
			if ((num & 0x10000) != 0)
			{
				num ^= 0x1100B;
			}
		}
	}

	public static ushort Pow(int e)
	{
		return Exp[(e % 65535 + 65535) % 65535];
	}

	public static void MulXor(ushort scalar, byte[] src, byte[] dst)
	{
		if (scalar == 0)
		{
			return;
		}
		int num = Log[scalar];
		for (int i = 0; i < src.Length; i += 2)
		{
			ushort num2 = (ushort)(src[i] | (src[i + 1] << 8));
			if (num2 != 0)
			{
				ushort num3 = Exp[num + Log[num2]];
				dst[i] ^= (byte)num3;
				dst[i + 1] ^= (byte)(num3 >> 8);
			}
		}
	}
}
