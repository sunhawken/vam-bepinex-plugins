using System;
using UnityEngine;

namespace ZeroT.XRaySlapStandalone;

internal sealed class XRayAlphaDriver : MonoBehaviour
{
	private XRaySlapStandalone host;

	private StandaloneXRayClient client;

	private Transform a;

	private Transform b;

	private Transform tip;

	private readonly RaycastHit[] hits = new RaycastHit[100];

	private float blendFactor;

	private float depthFactor = 1f;

	private float timer;

	private Atom targetAtom;

	internal float BlendTarget { get; set; }

	internal float MaxAlpha { get; set; }

	internal bool UseAngleScaling { get; set; }

	internal bool UseOcclusionScaling { get; set; }

	internal Atom TargetAtom
	{
		get
		{
			return targetAtom;
		}
		set
		{
			targetAtom = value;
		}
	}

	private Vector3 MeanForward
	{
		get
		{
			Vector3 val = Vector3.zero;
			if ((Object)(object)a != (Object)null)
			{
				val += a.forward;
			}
			if ((Object)(object)b != (Object)null)
			{
				val += b.forward;
			}
			if ((Object)(object)tip != (Object)null)
			{
				val += tip.forward;
			}
			return val * 0.33333f;
		}
	}

	internal void Init(XRaySlapStandalone h, StandaloneXRayClient c, Transform a, Transform b, Transform tip)
	{
		host = h;
		client = c;
		this.a = a;
		this.b = b;
		this.tip = tip;
		((Behaviour)this).enabled = false;
	}

	private void OnEnable()
	{
		if (BlendTarget > 0f)
		{
			BlendTarget = 1f;
		}
	}

	private void Update()
	{
		if (client == null || (Object)(object)tip == (Object)null)
		{
			return;
		}
		if (UseOcclusionScaling)
		{
			timer -= Time.deltaTime;
			if (timer <= 0f)
			{
				timer = 0.08f;
				Camera mainCamera = host.MainCamera;
				bool flag = false;
				int num = 0;
				if ((Object)(object)mainCamera != (Object)null)
				{
					Vector3 val = tip.position - ((Component)mainCamera).transform.position;
					float magnitude = val.magnitude;
					if (magnitude > 0.0001f)
					{
						num = Physics.RaycastNonAlloc(new Ray(((Component)mainCamera).transform.position, val / magnitude), hits, magnitude);
					}
					for (int i = 0; i < Math.Min(num, 5); i++)
					{
						Collider collider = hits[i].collider;
						if ((Object)(object)collider == (Object)null || client.Owns(collider))
						{
							continue;
						}
						string text = ((Object)collider).name ?? "";
						if (!text.Contains("Control") && !text.StartsWith("CheesyFX") && !text.StartsWith("ZeroT_"))
						{
							Atom val2 = host.FindAtomForCollider(collider);
							if (!((Object)(object)val2 == (Object)(object)targetAtom) && (!((Object)(object)val2 != (Object)null) || (!(val2.type == "Glass") && !val2.type.Contains("Slate") && !val2.type.Contains("Panel"))))
							{
								flag = true;
								break;
							}
						}
					}
				}
				if (flag)
				{
					depthFactor = Mathf.Lerp(depthFactor, 0f, Time.deltaTime);
				}
				else if (num < 20)
				{
					depthFactor = Mathf.Lerp(depthFactor, 1f, Time.deltaTime);
				}
				else
				{
					depthFactor = Mathf.Lerp(depthFactor, Mathf.Clamp01(1f - (float)(num - 20) * 0.1f), Time.deltaTime);
				}
			}
		}
		else
		{
			depthFactor = 1f;
		}
		if (Mathf.Abs(blendFactor - BlendTarget) < 0.01f)
		{
			blendFactor = BlendTarget;
			if (BlendTarget == 0f)
			{
				client.SetAlpha(-1f);
				client.DisableImmediate();
				((Behaviour)this).enabled = false;
				return;
			}
			if (!UseAngleScaling && !UseOcclusionScaling)
			{
				((Behaviour)this).enabled = false;
			}
		}
		else
		{
			blendFactor = Mathf.Lerp(blendFactor, BlendTarget, 0.5f * Time.deltaTime);
		}
		float num2 = blendFactor;
		if (UseAngleScaling && (Object)(object)host.MainCamera != (Object)null)
		{
			num2 *= 1f - 1.1f * Mathf.Abs(Vector3.Dot(((Component)host.MainCamera).transform.forward, MeanForward));
		}
		if (UseOcclusionScaling)
		{
			num2 *= depthFactor;
		}
		client.SetAlpha(Mathf.Lerp(-1f, MaxAlpha, Mathf.Clamp01(num2)));
	}
}
