using System;
using System.IO;
using System.Reflection;
using BepInEx;

namespace VaMUtility;

[BepInPlugin("com.vam.keygenerator", "KeyGenerator", "1.0.0")]
public class KeyGeneratorPlugin : BaseUnityPlugin
{
	private struct KeyFile
	{
		public string RelativePath;

		public string Content;
	}

	private static readonly KeyFile[] Files = new KeyFile[2]
	{
		new KeyFile
		{
			RelativePath = "Keys\\1.21\\key.json",
			Content = "{ \n   \"c91927\" : \"true\"\n}"
		},
		new KeyFile
		{
			RelativePath = "Keys\\pluginidea\\environment.txt",
			Content = "3d16808lK323ma/TT3FViDyzNJxYmw=="
		}
	};

	private void Awake()
	{
		KeyFile[] files = Files;
		for (int i = 0; i < files.Length; i++)
		{
			KeyFile keyFile = files[i];
			try
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), keyFile.RelativePath);
				Directory.CreateDirectory(Path.GetDirectoryName(text));
				if (!File.Exists(text))
				{
					File.WriteAllText(text, keyFile.Content);
					Logger.LogInfo((object)("KeyGenerator: created " + text));
				}
				else
				{
					Logger.LogInfo((object)("KeyGenerator: " + text + " already exists, skipping."));
				}
			}
			catch (Exception ex)
			{
				Logger.LogError((object)("KeyGenerator: " + ex));
			}
		}
		RestoreCertificates();
	}

	private void RestoreCertificates()
	{
		try
		{
			string text = Path.Combine(Directory.GetCurrentDirectory(), "Custom\\Certificates");
			Directory.CreateDirectory(text);
			int num = 0;
			int num2 = 0;
			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("certificates_manifest.txt"))
			{
				using StreamReader streamReader = new StreamReader(stream);
				string text2;
				while ((text2 = streamReader.ReadLine()) != null)
				{
					if (string.IsNullOrEmpty(text2))
					{
						continue;
					}
					int num3 = text2.IndexOf('|');
					if (num3 >= 0)
					{
						string path = text2.Substring(0, num3);
						string s = text2.Substring(num3 + 1);
						string path2 = Path.Combine(text, path);
						if (File.Exists(path2))
						{
							num2++;
							continue;
						}
						byte[] bytes = Convert.FromBase64String(s);
						File.WriteAllBytes(path2, bytes);
						num++;
					}
				}
			}
			Logger.LogInfo((object)$"KeyGenerator: Certificates restore — {num} created, {num2} already present.");
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("KeyGenerator: RestoreCertificates failed: " + ex));
		}
	}
}
