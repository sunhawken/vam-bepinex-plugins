using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ZeroT.XRaySlapStandalone;

internal static class WavResource
{
	internal static AudioClip Load(string resourceName, string clipName)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			throw new Exception("missing resource");
		}
		return Decode(XRaySlapStandalone.ReadAll(stream), clipName);
	}

	private static AudioClip Decode(byte[] data, string name)
	{
		if (data.Length < 44 || data[0] != 82 || data[1] != 73 || data[2] != 70 || data[3] != 70)
		{
			throw new Exception("invalid WAV");
		}
		int num = 12;
		int num2 = 1;
		int num3 = 44100;
		int num4 = 16;
		int num5 = 1;
		int num6 = -1;
		int num7 = 0;
		while (num + 8 <= data.Length)
		{
			string text = new string(new char[4]
			{
				(char)data[num],
				(char)data[num + 1],
				(char)data[num + 2],
				(char)data[num + 3]
			});
			int num8 = ReadI32(data, num + 4);
			int num9 = num + 8;
			if (text == "fmt " && num8 >= 16)
			{
				num5 = ReadI16(data, num9);
				num2 = ReadI16(data, num9 + 2);
				num3 = ReadI32(data, num9 + 4);
				num4 = ReadI16(data, num9 + 14);
			}
			else if (text == "data")
			{
				num6 = num9;
				num7 = Math.Min(num8, data.Length - num9);
				break;
			}
			num = num9 + num8 + (num8 & 1);
		}
		if (num5 != 1 || num4 != 16 || num6 < 0)
		{
			throw new Exception("only PCM16 WAV is supported");
		}
		int num10 = num7 / 2;
		float[] array = new float[num10];
		for (int i = 0; i < num10; i++)
		{
			short num11 = (short)(data[num6 + i * 2] | (data[num6 + i * 2 + 1] << 8));
			array[i] = (float)num11 / 32768f;
		}
		int num12 = num10 / Math.Max(1, num2);
		AudioClip val = AudioClip.Create(name, num12, num2, num3, false);
		val.SetData(array, 0);
		return val;
	}

	private static int ReadI16(byte[] b, int p)
	{
		return b[p] | (b[p + 1] << 8);
	}

	private static int ReadI32(byte[] b, int p)
	{
		return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
	}
}
