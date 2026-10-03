using UnityEngine;

namespace ProceduralSlaps;

public sealed class MaterialSlot
{
	public Material Material;

	public Texture Original;

	public Vector2 Scale;

	public Vector2 Offset;

	public int Index;

	public bool Gpu;

	public bool Bound;

	public DecalLayer Layer;
}
