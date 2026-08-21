namespace VamPar2;

internal static class Crc32
{
	private static readonly uint[] Table;

	static Crc32()
	{
		Table = new uint[256];
		for (uint num = 0u; num < 256; num++)
		{
			uint num2 = num;
			for (int i = 0; i < 8; i++)
			{
				num2 = (((num2 & 1) != 0) ? ((num2 >> 1) ^ 0xEDB88320u) : (num2 >> 1));
			}
			Table[num] = num2;
		}
	}

	public static uint Compute(byte[] data, int length)
	{
		uint num = uint.MaxValue;
		for (int i = 0; i < length; i++)
		{
			num = (num >> 8) ^ Table[(num ^ data[i]) & 0xFF];
		}
		return ~num;
	}
}
