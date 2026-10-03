using System.Collections;
using System.Linq;
using GPUTools.Skinner.Scripts.Providers;
using UnityEngine;

namespace ZeroT.XRaySlapStandalone;

internal sealed class StandaloneXRayClient
{
	internal sealed class MaterialOptionsWrapper : MaterialOptions
	{
		internal void SetParams()
		{
			this.SetAllParameters();
		}

		internal void SetAlpha(float val)
		{
			if (base.renderers != null && base.renderers.Length != 0 && (Object)(object)base.renderers[0] != (Object)null && base.renderers[0].materials.Length > 29)
			{
				base.renderers[0].materials[29].SetFloat("_AlphaAdjust", val);
			}
		}

		internal void SetAlphaTexture(Texture2D tex)
		{
			if (!((Object)(object)tex == (Object)null))
			{
				base.customTexture4IsNull = false;
				base.customTexture4 = tex;
				this.SetTextureGroupSet(base.textureGroup1, base.currentTextureGroup1Set, 3, (Texture)(object)tex, base.customTexture4IsNull);
			}
		}
	}

	private readonly XRaySlapStandalone host;

	internal readonly Atom atom;

	private DAZSkinV2 skin;

	private GameObject meshContainer;

	private MeshFilter meshFilter;

	private MeshRenderer renderer;

	private Material material;

	private Material discard;

	private Material[] oldMaterials;

	private Material[] mats;

	private MaterialOptionsWrapper matOptions;

	private XRayAlphaDriver driver;

	private Rigidbody gen1;

	private Rigidbody gen2;

	private Rigidbody gen3;

	private Coroutine syncRoutine;

	private bool destroyed;

	private bool syncing;

	internal bool Ready { get; private set; }

	internal bool IsVisible
	{
		get
		{
			if ((Object)(object)meshContainer != (Object)null)
			{
				return meshContainer.activeSelf;
			}
			return false;
		}
	}

	internal StandaloneXRayClient(XRaySlapStandalone host, Atom atom)
	{
		this.host = host;
		this.atom = atom;
	}

	internal IEnumerator Initialize()
	{
		while ((Object)(object)SuperController.singleton != (Object)null && SuperController.singleton.isLoading)
		{
			yield return null;
		}
		if (!destroyed && !((Object)(object)atom == (Object)null) && !((Object)(object)host.OverlayCamera == (Object)null))
		{
			Rigidbody[] componentsInChildren = ((Component)atom).GetComponentsInChildren<Rigidbody>(true);
			gen1 = componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "Gen1");
			gen2 = componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "Gen2");
			gen3 = componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "Gen3");
			if ((Object)(object)gen3 == (Object)null)
			{
				host.LogWarn("XRay: " + atom.uid + " has no Gen3 rigidbody.");
				yield break;
			}
			meshContainer = new GameObject("ZeroT_XRayMesh");
			meshContainer.transform.SetParent(((Component)atom).transform, false);
			meshContainer.layer = 18;
			renderer = meshContainer.AddComponent<MeshRenderer>();
			meshFilter = meshContainer.AddComponent<MeshFilter>();
			discard = new Material(Shader.Find("Custom/Discard"));
			matOptions = meshContainer.AddComponent<MaterialOptionsWrapper>();
			((MaterialOptions)matOptions).materialContainer = meshContainer.transform;
			((MaterialOptions)matOptions).textureGroup1 = new MaterialOptionTextureGroup();
			((MaterialOptions)matOptions).paramMaterialSlots = new int[1] { 29 };
			((MaterialOptions)matOptions).textureGroup1.materialSlots = ((MaterialOptions)matOptions).paramMaterialSlots;
			driver = meshContainer.AddComponent<XRayAlphaDriver>();
			driver.Init(host, this, ((Object)(object)gen1 != (Object)null) ? ((Component)gen1).transform : ((Component)gen3).transform, ((Object)(object)gen2 != (Object)null) ? ((Component)gen2).transform : ((Component)gen3).transform, ((Component)gen3).transform);
			meshContainer.SetActive(false);
			syncRoutine = ((MonoBehaviour)host).StartCoroutine(SyncSkin());
		}
	}

	private IEnumerator SyncSkin()
	{
		if (syncing || destroyed)
		{
			yield break;
		}
		syncing = true;
		Ready = false;
		while (!destroyed && (SuperController.singleton.isLoading || ((Component)SuperController.singleton.loadingIcon).gameObject.activeSelf || ((Component)SuperController.singleton.loadingUI).gameObject.activeSelf))
		{
			yield return null;
		}
		if (destroyed || (Object)(object)atom == (Object)null)
		{
			syncing = false;
			yield break;
		}
		JSONStorable storableByID = atom.GetStorableByID("skin");
		DAZCharacterMaterialOptions val = (DAZCharacterMaterialOptions)(object)((storableByID is DAZCharacterMaterialOptions) ? storableByID : null);
		if ((Object)(object)val == (Object)null || (Object)(object)val.skin == (Object)null)
		{
			host.LogWarn("XRay: skin unavailable for " + atom.uid);
			syncing = false;
			yield break;
		}
		skin = val.skin;
		if (skin.GPUmaterials == null || skin.GPUmaterials.Length <= 29 || (Object)(object)skin.GPUmaterials[29] == (Object)null)
		{
			host.LogWarn("XRay: material slot 29 unavailable for " + atom.uid);
			syncing = false;
			yield break;
		}
		bool matEnabled = skin.materialsEnabled == null || skin.materialsEnabled.Length <= 29 || skin.materialsEnabled[29];
		if (skin.materialsEnabled != null && skin.materialsEnabled.Length > 29)
		{
			skin.materialsEnabled[29] = true;
		}
		meshFilter.sharedMesh = ((PreCalcMeshProvider)skin).Mesh;
		oldMaterials = skin.GPUmaterials;
		Shader val2 = Shader.Find("Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff");
		if ((Object)(object)material != (Object)null)
		{
			Object.Destroy((Object)(object)material);
		}
		material = new Material(skin.GPUmaterials[29]);
		if ((Object)(object)val2 != (Object)null)
		{
			material.shader = val2;
		}
		mats = new Material[skin.GPUmaterials.Length];
		for (int i = 0; i < mats.Length; i++)
		{
			mats[i] = discard;
		}
		mats[29] = material;
		((Renderer)renderer).sharedMaterials = mats;
		((Renderer)renderer).materials = mats;
		((Renderer)renderer).enabled = true;
		skin.GPUmaterials = mats;
		skin.FlushBuffers();
		yield return (object)new WaitForEndOfFrame();
		int tries = 0;
		while (!destroyed && tries++ < 120 && (skin.GPUmaterials != mats || !material.HasProperty("verts")))
		{
			DAZMergedSkinV2 componentInChildren = ((Component)atom).GetComponentInChildren<DAZMergedSkinV2>();
			if ((Object)(object)componentInChildren != (Object)null && (Object)(object)componentInChildren != (Object)(object)skin)
			{
				skin.GPUmaterials = oldMaterials;
				skin.FlushBuffers();
				syncing = false;
				syncRoutine = ((MonoBehaviour)host).StartCoroutine(SyncSkin());
				yield break;
			}
			yield return (object)new WaitForEndOfFrame();
		}
		if (!destroyed)
		{
			skin.GPUmaterials = oldMaterials;
			skin.FlushBuffers();
			((MaterialOptions)matOptions).materialForDefaults = material;
			matOptions.SetParams();
			matOptions.SetAlphaTexture(host.CurrentAlphaTexture());
			SetAlpha(-1f);
			Ready = true;
			syncing = false;
			if (skin.materialsEnabled != null && skin.materialsEnabled.Length > 29)
			{
				skin.materialsEnabled[29] = matEnabled;
			}
		}
	}

	internal void EnsureSkin()
	{
		if (destroyed || (Object)(object)atom == (Object)null || syncing)
		{
			return;
		}
		JSONStorable storableByID = atom.GetStorableByID("skin");
		DAZCharacterMaterialOptions val = (DAZCharacterMaterialOptions)(object)((storableByID is DAZCharacterMaterialOptions) ? storableByID : null);
		if ((Object)(object)val != (Object)null && (Object)(object)val.skin != (Object)null && (Object)(object)val.skin != (Object)(object)skin)
		{
			Ready = false;
			if (syncRoutine != null)
			{
				((MonoBehaviour)host).StopCoroutine(syncRoutine);
			}
			syncRoutine = ((MonoBehaviour)host).StartCoroutine(SyncSkin());
		}
	}

	internal bool TryGetTip(out Vector3 p)
	{
		if ((Object)(object)gen3 != (Object)null)
		{
			p = gen3.position;
			return true;
		}
		p = Vector3.zero;
		return false;
	}

	internal void Enable(Atom target, Texture2D alphaTexture, float transparency, bool angle, bool occlusion)
	{
		if (Ready && !destroyed && !((Object)(object)meshContainer == (Object)null))
		{
			driver.TargetAtom = target;
			driver.MaxAlpha = 0f - Mathf.Clamp01(transparency);
			driver.UseAngleScaling = angle;
			driver.UseOcclusionScaling = occlusion;
			((Behaviour)driver).enabled = true;
			driver.BlendTarget = 1f;
			matOptions.SetAlphaTexture(alphaTexture);
			meshContainer.SetActive(true);
		}
	}

	internal void ShutDown()
	{
		if ((Object)(object)driver != (Object)null)
		{
			driver.BlendTarget = 0f;
			((Behaviour)driver).enabled = true;
		}
	}

	internal void DisableImmediate()
	{
		if ((Object)(object)meshContainer != (Object)null)
		{
			meshContainer.SetActive(false);
		}
	}

	internal void SetAlpha(float v)
	{
		if ((Object)(object)matOptions != (Object)null && (Object)(object)material != (Object)null)
		{
			matOptions.SetAlpha(v);
		}
	}

	internal bool Owns(Collider c)
	{
		if ((Object)(object)c == (Object)null)
		{
			return false;
		}
		Rigidbody attachedRigidbody = c.attachedRigidbody;
		if ((Object)(object)attachedRigidbody != (Object)null)
		{
			if (!((Object)(object)attachedRigidbody == (Object)(object)gen1) && !((Object)(object)attachedRigidbody == (Object)(object)gen2) && !((Object)(object)attachedRigidbody == (Object)(object)gen3))
			{
				return (Object)(object)host.FindAtomForCollider(c) == (Object)(object)atom;
			}
			return true;
		}
		return false;
	}

	internal void DestroyClient()
	{
		destroyed = true;
		Ready = false;
		if (syncRoutine != null)
		{
			((MonoBehaviour)host).StopCoroutine(syncRoutine);
		}
		try
		{
			if ((Object)(object)skin != (Object)null && oldMaterials != null && skin.GPUmaterials != oldMaterials)
			{
				skin.GPUmaterials = oldMaterials;
				skin.FlushBuffers();
			}
		}
		catch
		{
		}
		if ((Object)(object)meshContainer != (Object)null)
		{
			Object.Destroy((Object)(object)meshContainer);
		}
		if ((Object)(object)discard != (Object)null)
		{
			Object.Destroy((Object)(object)discard);
		}
		if ((Object)(object)material != (Object)null)
		{
			Object.Destroy((Object)(object)material);
		}
	}
}
