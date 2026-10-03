using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ProceduralSlaps;

public sealed class DecalCompositor : IDisposable
{
	private AssetBundle bundle;

	private bool disposed;

	public Material Material { get; private set; }

	public Texture2D Transparent { get; private set; }

	public string Error { get; private set; }

	public DecalCompositor()
	{
		Transparent = new Texture2D(1, 1, (TextureFormat)4, false);
		Transparent.SetPixel(0, 0, Color.clear);
		Transparent.Apply(false, true);
		try
		{
			byte[] array = ReadEmbeddedBundle();
			bundle = AssetBundle.LoadFromMemory(array);
			if ((Object)(object)bundle == (Object)null)
			{
				throw new InvalidOperationException("Cannot load embedded skin compositor bundle.");
			}
			Shader val = bundle.LoadAsset<Shader>("assets/proceduralslaps/decal.shader");
			if ((Object)(object)val == (Object)null || !val.isSupported)
			{
				throw new InvalidOperationException("Skin compositor shader is unsupported.");
			}
			Material = new Material(val);
			Material.SetTexture("_MainTex", (Texture)(object)Transparent);
			for (int i = 0; i < 10; i++)
			{
				Material.SetTexture("_DecalTex" + i, (Texture)(object)Transparent);
				Material.SetVector("_DecalColor" + i, (Vector4)(Color.clear));
				Material.SetVector("_DecalData" + i, new Vector4(0.5f, 0.5f, 0f, 1f));
				Material.SetTextureScale("_DecalTex" + i, Vector2.one);
				Material.SetTextureOffset("_DecalTex" + i, Vector2.zero);
			}
			Material.SetVector("_DecalColor0", (Vector4)(Color.white));
			Material.SetVector("_DecalColor1", (Vector4)(Color.white));
			bundle.Unload(false);
			bundle = null;
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			if ((Object)(object)bundle != (Object)null)
			{
				bundle.Unload(true);
				bundle = null;
			}
		}
	}

	private static byte[] ReadEmbeddedBundle()
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("ProceduralSlaps.skin-composite.assetbundle");
		if (manifestResourceStream == null)
		{
			throw new InvalidOperationException("Embedded resource is missing: ProceduralSlaps.skin-composite.assetbundle");
		}
		try
		{
			if (manifestResourceStream.Length > int.MaxValue)
			{
				throw new InvalidOperationException("Embedded compositor bundle is too large.");
			}
			byte[] array = new byte[(int)manifestResourceStream.Length];
			int num;
			for (int i = 0; i < array.Length; i += num)
			{
				num = manifestResourceStream.Read(array, i, array.Length - i);
				if (num <= 0)
				{
					throw new EndOfStreamException("Unexpected end of embedded compositor bundle.");
				}
			}
			return array;
		}
		finally
		{
			manifestResourceStream.Dispose();
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			if ((Object)(object)Material != (Object)null)
			{
				Object.Destroy((Object)(object)Material);
			}
			if ((Object)(object)Transparent != (Object)null)
			{
				Object.Destroy((Object)(object)Transparent);
			}
			if ((Object)(object)bundle != (Object)null)
			{
				bundle.Unload(true);
			}
			bundle = null;
			Material = null;
			Transparent = null;
		}
	}
}
