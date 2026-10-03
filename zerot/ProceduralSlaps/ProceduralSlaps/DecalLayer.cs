using System;
using UnityEngine;

namespace ProceduralSlaps;

public sealed class DecalLayer
{
	public Texture Source;

	public Vector2 Scale;

	public Vector2 Offset;

	public RenderTexture Output;

	public bool Dirty = true;

	public bool Matches(MaterialSlot slot)
	{
		return object.ReferenceEquals(Source, slot.Original) && Scale == slot.Scale && Offset == slot.Offset;
	}

	public void Adopt(MaterialSlot slot)
	{
		Source = slot.Original;
		Scale = slot.Scale;
		Offset = slot.Offset;
		Dirty = true;
	}

	public void Prepare()
	{
		int num = ((!((Object)(object)Source == (Object)null)) ? Math.Max(256, Source.width) : 256);
		int num2 = ((!((Object)(object)Source == (Object)null)) ? Math.Max(256, Source.height) : 256);
		if (!((Object)(object)Output != (Object)null) || ((Texture)Output).width != num || ((Texture)Output).height != num2)
		{
			Dispose();
			Output = new RenderTexture(num, num2, 0, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
			((Object)Output).name = "ProceduralSlaps decal";
			((Texture)Output).wrapMode = (TextureWrapMode)1;
			((Texture)Output).filterMode = (FilterMode)2;
			((Texture)Output).anisoLevel = (((Object)(object)Source == (Object)null) ? 1 : Source.anisoLevel);
			Output.useMipMap = true;
			Output.autoGenerateMips = false;
			if (!Output.Create())
			{
				throw new InvalidOperationException("Cannot allocate skin decal render texture.");
			}
			Dirty = true;
		}
	}

	public void Dispose()
	{
		if ((Object)(object)Output != (Object)null)
		{
			Output.Release();
			Object.Destroy((Object)(object)Output);
			Output = null;
		}
	}
}
