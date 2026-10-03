using System;
using System.Collections.Generic;
using ProceduralSlaps.Core;
using UnityEngine;

namespace ProceduralSlaps;

public sealed class SkinAtlas
{
	public readonly HeatMap Map = new HeatMap(256);

	public readonly List<MaterialSlot> Slots = new List<MaterialSlot>();

	public string Group;

	private readonly List<DecalLayer> layers = new List<DecalLayer>();

	private readonly List<DecalLayer> nextLayers = new List<DecalLayer>();

	private Texture2D overlay;

	private Color32[] pixels;

	private bool wasActive;

	private int lastRed = -1;

	private int lastGreen = -1;

	private int lastBlue = -1;

	private float lastRedness = -1f;

	private readonly DirtyRegions dirty = new DirtyRegions(256);

	private readonly List<PixelRegion> regions = new List<PixelRegion>(1024);

	private float lastDecay;

	public float NextRender;

	public int LastPixelChecks { get; private set; }

	public bool Prepared { get; private set; }

	public bool Owns(MaterialSlot slot)
	{
		return (Object)(object)slot.Material != (Object)null && slot.Layer != null && (Object)(object)slot.Layer.Output != (Object)null && (Object)(object)slot.Material.GetTexture("_DecalTex") == (Object)(object)slot.Layer.Output;
	}

	public void RefreshSlots(Material[] gpu, Material[] cpu)
	{
		bool flag = layers.Count == 0;
		for (int i = 0; i < Slots.Count; i++)
		{
			MaterialSlot materialSlot = Slots[i];
			Material[] array = ((!materialSlot.Gpu) ? cpu : gpu);
			Material val = ((array != null && materialSlot.Index < array.Length) ? array[materialSlot.Index] : null);
			if ((Object)(object)val != (Object)null && !val.HasProperty("_DecalTex"))
			{
				val = null;
			}
			if ((Object)(object)materialSlot.Material != (Object)(object)val)
			{
				materialSlot.Material = val;
				flag = true;
			}
			if (!((Object)(object)val == (Object)null))
			{
				Texture texture = val.GetTexture("_DecalTex");
				Vector2 textureScale = val.GetTextureScale("_DecalTex");
				Vector2 textureOffset = val.GetTextureOffset("_DecalTex");
				if (!object.ReferenceEquals(texture, materialSlot.Original) || textureScale != materialSlot.Scale || textureOffset != materialSlot.Offset)
				{
					flag = true;
				}
				materialSlot.Original = texture;
				materialSlot.Scale = textureScale;
				materialSlot.Offset = textureOffset;
			}
		}
		if (!flag)
		{
			return;
		}
		nextLayers.Clear();
		bool flag2 = layers.Count == 1;
		MaterialSlot materialSlot2 = null;
		for (int j = 0; j < Slots.Count; j++)
		{
			MaterialSlot materialSlot3 = Slots[j];
			if (!((Object)(object)materialSlot3.Material == (Object)null))
			{
				if (materialSlot2 == null)
				{
					materialSlot2 = materialSlot3;
				}
				else if (!object.ReferenceEquals(materialSlot3.Original, materialSlot2.Original) || materialSlot3.Scale != materialSlot2.Scale || materialSlot3.Offset != materialSlot2.Offset)
				{
					flag2 = false;
				}
			}
		}
		if (flag2 && materialSlot2 != null)
		{
			layers[0].Adopt(materialSlot2);
		}
		for (int k = 0; k < Slots.Count; k++)
		{
			MaterialSlot materialSlot4 = Slots[k];
			materialSlot4.Layer = null;
			if ((Object)(object)materialSlot4.Material == (Object)null)
			{
				continue;
			}
			for (int l = 0; l < nextLayers.Count; l++)
			{
				if (nextLayers[l].Matches(materialSlot4))
				{
					materialSlot4.Layer = nextLayers[l];
					break;
				}
			}
			if (materialSlot4.Layer != null)
			{
				continue;
			}
			for (int m = 0; m < layers.Count; m++)
			{
				if (layers[m].Matches(materialSlot4))
				{
					materialSlot4.Layer = layers[m];
					break;
				}
			}
			if (materialSlot4.Layer == null)
			{
				materialSlot4.Layer = new DecalLayer();
				materialSlot4.Layer.Adopt(materialSlot4);
			}
			nextLayers.Add(materialSlot4.Layer);
		}
		for (int n = 0; n < layers.Count; n++)
		{
			if (!nextLayers.Contains(layers[n]))
			{
				layers[n].Dispose();
			}
		}
		layers.Clear();
		layers.AddRange(nextLayers);
		for (int num = 0; num < layers.Count; num++)
		{
			layers[num].Prepare();
		}
	}

	public void Prepare(DecalCompositor compositor, Settings settings)
	{
		if (!Prepared)
		{
			overlay = new Texture2D(256, 256, (TextureFormat)4, false);
			((Object)overlay).name = "ProceduralSlaps mask";
			((Texture)overlay).wrapMode = (TextureWrapMode)1;
			((Texture)overlay).filterMode = (FilterMode)1;
			pixels = new Color32[65536];
			UpdatePixels(settings);
			Compose(compositor, changed: true);
			Prepared = true;
			lastDecay = Time.time;
		}
	}

	public void DecayTo(float now, Settings settings)
	{
		if (Map.Active)
		{
			Map.Decay(Mathf.Max(0f, now - lastDecay), settings);
		}
		lastDecay = now;
	}

	public void Render(DecalCompositor compositor, Settings settings)
	{
		if (Prepared)
		{
			bool flag = (Map.Active || wasActive) && UpdatePixels(settings);
			bool flag2 = flag;
			int num = 0;
			while (!flag2 && num < layers.Count)
			{
				flag2 = layers[num].Dirty || (Map.Active && layers[num].Source is RenderTexture);
				num++;
			}
			if (flag2)
			{
				Compose(compositor, flag);
			}
		}
	}

	private bool UpdatePixels(Settings settings)
	{
		wasActive = Map.Active;
		byte b = (byte)(settings.Red.Value * 255f);
		byte b2 = (byte)(settings.Green.Value * 255f);
		byte b3 = (byte)(settings.Blue.Value * 255f);
		bool flag = b != lastRed || b2 != lastGreen || b3 != lastBlue;
		if (settings.Redness.Value != lastRedness)
		{
			Map.MarkActiveChanged();
		}
		LastPixelChecks = ((!flag) ? Map.ChangedCount : pixels.Length);
		dirty.Clear();
		bool flag2 = false;
		for (int i = 0; i < LastPixelChecks; i++)
		{
			int num = ((!flag) ? Map.ChangedAt(i) : i);
			byte b4 = (byte)(Map.VisibleRednessAt(num, settings.Redness.Value) * 255f);
			Color32 val = pixels[num];
			if (val.a != b4 || val.r != b || val.g != b2 || val.b != b3)
			{
				dirty.Include(num % Map.Size, num / Map.Size);
				flag2 = true;
			}
			ref Color32 reference = ref pixels[num];
			reference = new Color32(b, b2, b3, b4);
		}
		Map.ClearChanges();
		lastRed = b;
		lastGreen = b2;
		lastBlue = b3;
		lastRedness = settings.Redness.Value;
		if (flag && flag2)
		{
			dirty.All();
		}
		return flag2;
	}

	private void Compose(DecalCompositor compositor, bool changed)
	{
		if (changed)
		{
			overlay.SetPixels32(pixels);
			overlay.Apply(false, false);
		}
		RenderTexture active = RenderTexture.active;
		bool sRGBWrite = GL.sRGBWrite;
		try
		{
			GL.sRGBWrite = (int)QualitySettings.activeColorSpace == 1;
			for (int i = 0; i < layers.Count; i++)
			{
				DecalLayer decalLayer = layers[i];
				bool flag = decalLayer.Dirty || (Map.Active && decalLayer.Source is RenderTexture);
				if (changed || flag)
				{
					if (flag)
					{
						regions.Clear();
						regions.Add(new PixelRegion
						{
							X0 = 0,
							Y0 = 0,
							X1 = ((Texture)decalLayer.Output).width,
							Y1 = ((Texture)decalLayer.Output).height
						});
					}
					else
					{
						dirty.Build(((Texture)decalLayer.Output).width, ((Texture)decalLayer.Output).height, regions);
					}
					RenderTexture.active = decalLayer.Output;
					Material material = compositor.Material;
					material.SetTexture("_DecalTex0", (Texture)((!((Object)(object)decalLayer.Source == (Object)null)) ? ((object)decalLayer.Source) : ((object)compositor.Transparent)));
					material.SetTextureScale("_DecalTex0", new Vector2(1f / decalLayer.Scale.x, 1f / decalLayer.Scale.y));
					material.SetTextureOffset("_DecalTex0", decalLayer.Offset + (decalLayer.Scale - Vector2.one) * 0.5f);
					material.SetTexture("_DecalTex1", (Texture)(object)overlay);
					GL.PushMatrix();
					try
					{
						GL.LoadOrtho();
						DrawRegions(material, ((Texture)decalLayer.Output).width, ((Texture)decalLayer.Output).height);
					}
					finally
					{
						GL.PopMatrix();
					}
					decalLayer.Output.GenerateMips();
					decalLayer.Dirty = false;
				}
			}
		}
		finally
		{
			RenderTexture.active = active;
			GL.sRGBWrite = sRGBWrite;
		}
	}

	private void DrawRegions(Material material, int width, int height)
	{
		if (!material.SetPass(0))
		{
			throw new InvalidOperationException("Skin composite material is not supported.");
		}
		GL.Begin(7);
		GL.Color(Color.white);
		for (int i = 0; i < regions.Count; i++)
		{
			PixelRegion pixelRegion = regions[i];
			float num = (float)pixelRegion.X0 / (float)width;
			float num2 = (float)pixelRegion.Y0 / (float)height;
			float num3 = (float)pixelRegion.X1 / (float)width;
			float num4 = (float)pixelRegion.Y1 / (float)height;
			GL.TexCoord2(num, num2);
			GL.Vertex3(num, num2, 0f);
			GL.TexCoord2(num3, num2);
			GL.Vertex3(num3, num2, 0f);
			GL.TexCoord2(num3, num4);
			GL.Vertex3(num3, num4, 0f);
			GL.TexCoord2(num, num4);
			GL.Vertex3(num, num4, 0f);
		}
		GL.End();
	}

	public void BindForCamera()
	{
		if (!Prepared || (!Map.Active && !wasActive))
		{
			return;
		}
		for (int i = 0; i < Slots.Count; i++)
		{
			MaterialSlot materialSlot = Slots[i];
			if (!((Object)(object)materialSlot.Material == (Object)null) && materialSlot.Layer != null && !materialSlot.Layer.Dirty && !((Object)(object)materialSlot.Layer.Output == (Object)null) && object.ReferenceEquals(materialSlot.Material.GetTexture("_DecalTex"), materialSlot.Original) && !(materialSlot.Material.GetTextureScale("_DecalTex") != materialSlot.Scale) && !(materialSlot.Material.GetTextureOffset("_DecalTex") != materialSlot.Offset))
			{
				materialSlot.Material.SetTexture("_DecalTex", (Texture)(object)materialSlot.Layer.Output);
				materialSlot.Bound = true;
				if (materialSlot.Scale != Vector2.one)
				{
					materialSlot.Material.SetTextureScale("_DecalTex", Vector2.one);
				}
				if (materialSlot.Offset != Vector2.zero)
				{
					materialSlot.Material.SetTextureOffset("_DecalTex", Vector2.zero);
				}
			}
		}
	}

	public void Restore()
	{
		for (int i = 0; i < Slots.Count; i++)
		{
			MaterialSlot materialSlot = Slots[i];
			if (!materialSlot.Bound)
			{
				continue;
			}
			materialSlot.Bound = false;
			if (Owns(materialSlot))
			{
				materialSlot.Material.SetTexture("_DecalTex", materialSlot.Original);
				if (materialSlot.Scale != Vector2.one)
				{
					materialSlot.Material.SetTextureScale("_DecalTex", materialSlot.Scale);
				}
				if (materialSlot.Offset != Vector2.zero)
				{
					materialSlot.Material.SetTextureOffset("_DecalTex", materialSlot.Offset);
				}
			}
		}
	}

	public void ReleaseTextures()
	{
		Restore();
		for (int i = 0; i < layers.Count; i++)
		{
			layers[i].Dispose();
		}
		layers.Clear();
		nextLayers.Clear();
		if ((Object)(object)overlay != (Object)null)
		{
			Object.Destroy((Object)(object)overlay);
			overlay = null;
		}
		pixels = null;
		Prepared = false;
	}
}
