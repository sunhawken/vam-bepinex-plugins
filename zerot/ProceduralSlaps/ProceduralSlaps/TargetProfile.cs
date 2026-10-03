using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ProceduralSlaps;

internal sealed class TargetProfile
{
	public readonly Settings Settings = new Settings();

	private readonly ConfigEntry<float>[] values;

	private readonly ConfigEntry<bool> selfContacts;

	public TargetProfile(ConfigFile config, string uid)
	{
		string text = SectionForUid(uid);
		values = new ConfigEntry<float>[Settings.Parameters.Length];
		for (int i = 0; i < Settings.Parameters.Length; i++)
		{
			Parameter parameter = Settings.Parameters[i];
			values[i] = config.Bind<float>(text, parameter.Id, parameter.Default, parameter.Label + " [" + parameter.Min.ToString("0.###") + ".." + parameter.Max.ToString("0.###") + "]");
			float num = values[i].Value;
			if (float.IsNaN(num) || float.IsInfinity(num) || num < parameter.Min || num > parameter.Max)
			{
				num = parameter.Default;
				values[i].Value = num;
			}
			parameter.Set(num);
		}
		selfContacts = config.Bind<bool>(text, "selfContacts", false, "Include contact between colliders belonging to the target Person.");
		Settings.SelfContacts = selfContacts.Value;
	}

	public void SetParameter(int index, float value)
	{
		Parameter parameter = Settings.Parameters[index];
		value = Mathf.Clamp(value, parameter.Min, parameter.Max);
		parameter.Set(value);
		values[index].Value = value;
	}

	public void SetSelfContacts(bool value)
	{
		Settings.SelfContacts = value;
		selfContacts.Value = value;
	}

	private static string SectionForUid(string uid)
	{
		if (uid == null)
		{
			uid = string.Empty;
		}
		char[] array = new char[uid.Length];
		uint num = 2166136261u;
		for (int i = 0; i < uid.Length; i++)
		{
			char c = uid[i];
			num ^= c;
			num *= 16777619;
			array[i] = ((!char.IsLetterOrDigit(c) && c != '-' && c != '_') ? '_' : c);
		}
		int length = Math.Min(40, array.Length);
		return "Target." + new string(array, 0, length) + "." + num.ToString("X8");
	}
}
