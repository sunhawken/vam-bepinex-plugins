using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VamPar2;

internal static class Par2Writer
{
	private const int BLOCK = 524288;

	private static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("PAR2\0PKT");

	private static readonly byte[] T_MAIN = Type16("PAR 2.0\0Main\0\0\0\0");

	private static readonly byte[] T_FDESC = Type16("PAR 2.0\0FileDesc");

	private static readonly byte[] T_IFSC = Type16("PAR 2.0\0IFSC\0\0\0\0");

	private static readonly byte[] T_RECV = Type16("PAR 2.0\0RecvSlic");

	private static readonly byte[] T_CREATE = Type16("PAR 2.0\0Creator\0");

	private static byte[] Type16(string s)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(s);
		if (bytes.Length != 16)
		{
			throw new Exception("type must be 16 bytes");
		}
		return bytes;
	}

	public static void Create(string path, int redundancyPct)
	{
		long length = new FileInfo(path).Length;
		string fileName = Path.GetFileName(path);
		byte[] bytes = Encoding.UTF8.GetBytes(fileName);
		int num = (int)Math.Max(1L, (length + 524288 - 1) / 524288);
		int num2 = Math.Max(1, Math.Min(65534, (int)Math.Ceiling((double)(num * redundancyPct) / 100.0)));
		byte[][] array = new byte[num][];
		uint[] array2 = new uint[num];
		byte[][] array3 = new byte[num2][];
		for (int i = 0; i < num2; i++)
		{
			array3[i] = new byte[524288];
		}
		byte[] array4 = new byte[524288];
		byte[] first16k;
		byte[] array6;
		using (MD5 mD = MD5.Create())
		{
			using FileStream fileStream = File.OpenRead(path);
			int num3 = (int)Math.Min(16384L, length);
			byte[] array5 = new byte[num3];
			ReadFull(fileStream, array5, num3);
			first16k = mD.ComputeHash(array5);
			fileStream.Seek(0L, SeekOrigin.Begin);
			array6 = mD.ComputeHash(fileStream);
			fileStream.Seek(0L, SeekOrigin.Begin);
			for (int j = 0; j < num; j++)
			{
				int num4 = ReadFull(fileStream, array4, 524288);
				if (num4 < 524288)
				{
					Array.Clear(array4, num4, 524288 - num4);
				}
				array[j] = mD.ComputeHash(array4);
				array2[j] = Crc32.Compute(array4, 524288);
				for (int k = 0; k < num2; k++)
				{
					GF16.MulXor(GF16.Pow((int)((long)(k + 1) * (long)j % 65535)), array4, array3[k]);
				}
			}
		}
		byte[] fileId;
		using (MD5 mD2 = MD5.Create())
		{
			MemoryStream memoryStream = new MemoryStream(24 + bytes.Length);
			memoryStream.Write(LE64((ulong)length), 0, 8);
			memoryStream.Write(array6, 0, 16);
			memoryStream.Write(bytes, 0, bytes.Length);
			fileId = mD2.ComputeHash(memoryStream.ToArray());
		}
		byte[] setId = Guid.NewGuid().ToByteArray();
		using (MD5 md = MD5.Create())
		{
			using FileStream s = File.Create(path + ".par2");
			WritePacket(s, md, setId, T_MAIN, MainBody(num, num2, fileId));
			WritePacket(s, md, setId, T_FDESC, FdescBody(fileId, array6, first16k, length, bytes));
			WritePacket(s, md, setId, T_IFSC, IfscBody(fileId, array, array2));
			WritePacket(s, md, setId, T_CREATE, Pad4(Encoding.ASCII.GetBytes("VaM-Par2Creator")));
		}
		using MD5 md2 = MD5.Create();
		using FileStream s2 = File.Create(path + $".vol0000+{num2:D2}.par2");
		byte[] array7 = new byte[524292];
		for (int l = 0; l < num2; l++)
		{
			Buffer.BlockCopy(LE32((uint)(l + 1)), 0, array7, 0, 4);
			Buffer.BlockCopy(array3[l], 0, array7, 4, 524288);
			WritePacket(s2, md2, setId, T_RECV, array7);
		}
	}

	private static byte[] MainBody(int k, int nr, byte[] fileId)
	{
		MemoryStream memoryStream = new MemoryStream(28);
		memoryStream.Write(LE64(524288L), 0, 8);
		memoryStream.Write(LE32((uint)nr), 0, 4);
		memoryStream.Write(fileId, 0, 16);
		return memoryStream.ToArray();
	}

	private static byte[] FdescBody(byte[] fileId, byte[] fileHash, byte[] first16k, long len, byte[] nameBytes)
	{
		MemoryStream memoryStream = new MemoryStream(56 + nameBytes.Length + 4);
		memoryStream.Write(fileId, 0, 16);
		memoryStream.Write(fileHash, 0, 16);
		memoryStream.Write(first16k, 0, 16);
		memoryStream.Write(LE64((ulong)len), 0, 8);
		byte[] array = Pad4(nameBytes);
		memoryStream.Write(array, 0, array.Length);
		return memoryStream.ToArray();
	}

	private static byte[] IfscBody(byte[] fileId, byte[][] hashes, uint[] crcs)
	{
		MemoryStream memoryStream = new MemoryStream(16 + hashes.Length * 20);
		memoryStream.Write(fileId, 0, 16);
		for (int i = 0; i < hashes.Length; i++)
		{
			memoryStream.Write(hashes[i], 0, 16);
			memoryStream.Write(LE32(crcs[i]), 0, 4);
		}
		return memoryStream.ToArray();
	}

	private static void WritePacket(Stream s, MD5 md5, byte[] setId, byte[] type, byte[] body)
	{
		byte[] array = new byte[32 + body.Length];
		Buffer.BlockCopy(setId, 0, array, 0, 16);
		Buffer.BlockCopy(type, 0, array, 16, 16);
		Buffer.BlockCopy(body, 0, array, 32, body.Length);
		byte[] buffer = md5.ComputeHash(array);
		s.Write(MAGIC, 0, 8);
		s.Write(LE64((ulong)(64 + body.Length)), 0, 8);
		s.Write(buffer, 0, 16);
		s.Write(setId, 0, 16);
		s.Write(type, 0, 16);
		s.Write(body, 0, body.Length);
	}

	private static int ReadFull(Stream s, byte[] buf, int count)
	{
		int i;
		int num;
		for (i = 0; i < count; i += num)
		{
			if ((num = s.Read(buf, i, count - i)) <= 0)
			{
				break;
			}
		}
		return i;
	}

	private static byte[] LE64(ulong v)
	{
		return BitConverter.GetBytes(v);
	}

	private static byte[] LE64(long v)
	{
		return BitConverter.GetBytes(v);
	}

	private static byte[] LE32(uint v)
	{
		return BitConverter.GetBytes(v);
	}

	private static byte[] Pad4(byte[] b)
	{
		int num = (4 - b.Length % 4) % 4;
		if (num == 0)
		{
			return b;
		}
		byte[] array = new byte[b.Length + num];
		Buffer.BlockCopy(b, 0, array, 0, b.Length);
		return array;
	}
}
