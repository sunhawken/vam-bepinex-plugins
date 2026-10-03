using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralSlaps;

public sealed class SkinRenderer
{
	public readonly List<SkinAtlas> Atlases = new List<SkinAtlas>();

	public readonly int[] AtlasByMaterial;

	private DecalCompositor compositor;

	private readonly DAZSkinV2 skin;

	private int renderCursor;

	private int cameraDepth;

	private bool disposed;

	public SkinRenderer(DAZSkinV2 selected)
	{
		skin = selected;
		string[] materialNames = skin.dazMesh.materialNames;
		AtlasByMaterial = new int[materialNames.Length];
		try
		{
			for (int i = 0; i < materialNames.Length; i++)
			{
				AtlasByMaterial[i] = -1;
				string text = SkinGroup(materialNames[i]);
				if (text == null)
				{
					continue;
				}
				int num = -1;
				for (int j = 0; j < Atlases.Count; j++)
				{
					if (Atlases[j].Group == text)
					{
						num = j;
						break;
					}
				}
				if (num < 0)
				{
					num = Atlases.Count;
					Atlases.Add(new SkinAtlas
					{
						Group = text
					});
				}
				AtlasByMaterial[i] = num;
				Atlases[num].Slots.Add(new MaterialSlot
				{
					Index = i,
					Gpu = true
				});
				Atlases[num].Slots.Add(new MaterialSlot
				{
					Index = i,
					Gpu = false
				});
			}
			if (Atlases.Count == 0)
			{
				throw new InvalidOperationException("No supported skin materials.");
			}
			compositor = new DecalCompositor();
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Combine((Delegate)(object)Camera.onPreRender, (Delegate)new Camera.CameraCallback(BeforeCamera));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Combine((Delegate)(object)Camera.onPostRender, (Delegate)new Camera.CameraCallback(AfterCamera));
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public static string SkinGroup(string name)
	{
		string text = name.ToLowerInvariant();
		if (text.Contains("eye") || text.Contains("cornea") || text.Contains("tear") || text.Contains("lacrimal") || text.Contains("teeth") || text.Contains("tongue") || text.Contains("mouth") || text.Contains("nail") || text.Contains("lash"))
		{
			return null;
		}
		if (text.Contains("face") || text.Contains("lip") || text.Contains("nostril"))
		{
			return "face";
		}
		if (text.Contains("torso") || text.Contains("nipple") || text.Contains("neck") || text.Contains("hip") || text.Contains("pelvis") || text.Contains("glute") || text.Contains("head"))
		{
			return "torso";
		}
		if (text.Contains("arm") || text.Contains("leg") || text.Contains("hand") || text.Contains("foot") || text.Contains("feet") || text.Contains("limb") || text.Contains("shoulder"))
		{
			return "limbs";
		}
		if (text.Contains("ear"))
		{
			return "torso";
		}
		if (text.Contains("genital") || text.Contains("penis") || text.Contains("scrot") || text.Contains("labia") || text.Contains("anus") || text == "defaultmat")
		{
			return "genitals";
		}
		return (!text.Contains("skin")) ? null : text;
	}

	public bool Intact()
	{
		return (Object)(object)skin != (Object)null && (Object)(object)skin.dazMesh != (Object)null;
	}

	public bool RenderNext(float now, float unscaledNow, Settings settings)
	{
		if (!Intact())
		{
			return false;
		}
		for (int i = 0; i < Atlases.Count; i++)
		{
			int index = renderCursor;
			renderCursor = (renderCursor + 1) % Atlases.Count;
			SkinAtlas skinAtlas = Atlases[index];
			if (!(unscaledNow < skinAtlas.NextRender))
			{
				skinAtlas.RefreshSlots(skin.GPUmaterials, skin.dazMesh.materials);
				skinAtlas.DecayTo(now, settings);
				skinAtlas.Render(compositor, settings);
				skinAtlas.NextRender = unscaledNow + 0.1f;
				break;
			}
		}
		return true;
	}

	public bool PrepareOne(Settings settings)
	{
		if (compositor.Error != null)
		{
			throw new InvalidOperationException(compositor.Error);
		}
		if ((Object)(object)compositor.Material == (Object)null)
		{
			return false;
		}
		for (int i = 0; i < Atlases.Count; i++)
		{
			if (!Atlases[i].Prepared)
			{
				Atlases[i].RefreshSlots(skin.GPUmaterials, skin.dazMesh.materials);
				Atlases[i].Prepare(compositor, settings);
				return i == Atlases.Count - 1;
			}
		}
		return true;
	}

	private void BeforeCamera(Camera camera)
	{
		if (disposed || cameraDepth++ > 0)
		{
			return;
		}
		try
		{
			for (int i = 0; i < Atlases.Count; i++)
			{
				Atlases[i].BindForCamera();
			}
		}
		catch
		{
			RestoreBindings();
			throw;
		}
	}

	private void AfterCamera(Camera camera)
	{
		if (cameraDepth > 0 && --cameraDepth == 0)
		{
			RestoreBindings();
		}
	}

	public void RestoreBindings()
	{
		cameraDepth = 0;
		for (int i = 0; i < Atlases.Count; i++)
		{
			Atlases[i].Restore();
		}
	}

	public void Clear(Settings settings)
	{
		for (int i = 0; i < Atlases.Count; i++)
		{
			Atlases[i].Map.Clear();
			Atlases[i].Render(compositor, settings);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Remove((Delegate)(object)Camera.onPreRender, (Delegate)new Camera.CameraCallback(BeforeCamera));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Remove((Delegate)(object)Camera.onPostRender, (Delegate)new Camera.CameraCallback(AfterCamera));
			RestoreBindings();
			for (int i = 0; i < Atlases.Count; i++)
			{
				Atlases[i].ReleaseTextures();
			}
			if (compositor != null)
			{
				compositor.Dispose();
			}
			compositor = null;
		}
	}
}
