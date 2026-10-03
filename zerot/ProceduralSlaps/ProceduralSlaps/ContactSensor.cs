using System;
using System.Collections.Generic;
using ProceduralSlaps.Core;
using UnityEngine;

namespace ProceduralSlaps;

public sealed class ContactSensor : MonoBehaviour
{
	internal TargetRuntime Owner;

	public Rigidbody Body;

	private readonly Dictionary<int, SensorPair> histories = new Dictionary<int, SensorPair>();

	private readonly List<int> expired = new List<int>();

	private void OnCollisionEnter(Collision collision)
	{
		Receive(collision, entered: true);
	}

	private void OnCollisionStay(Collision collision)
	{
		Receive(collision, entered: false);
	}

	private void OnCollisionExit(Collision collision)
	{
	}

	private void OnDisable()
	{
		histories.Clear();
	}

	private void Receive(Collision collision, bool entered)
	{
		if (Owner == null || !Owner.Ready || !Owner.Enabled || (Object)(object)Body == (Object)null)
		{
			return;
		}
		Atom targetAtom = Owner.TargetAtom;
		if ((Object)(object)targetAtom == (Object)null)
		{
			return;
		}
		try
		{
			Collider collider = collision.collider;
			if ((Object)(object)collider == (Object)null || collider.isTrigger || !collider.enabled)
			{
				return;
			}
			bool flag = Owner.OwnsCollider(collider);
			if (flag && !Owner.Configuration.SelfContacts)
			{
				return;
			}
			Rigidbody attachedRigidbody = collider.attachedRigidbody;
			if ((Object)(object)attachedRigidbody == (Object)null || !targetAtom.collisionEnabled || !targetAtom.on)
			{
				return;
			}
			float time = Time.time;
			if (histories.Count > 64)
			{
				expired.Clear();
				foreach (KeyValuePair<int, SensorPair> history in histories)
				{
					if (time - history.Value.LastSeen > 1f)
					{
						expired.Add(history.Key);
					}
				}
				for (int i = 0; i < expired.Count; i++)
				{
					histories.Remove(expired[i]);
				}
			}
			int instanceID = ((Object)collider).GetInstanceID();
			if (!histories.TryGetValue(instanceID, out var value))
			{
				value = new SensorPair();
				histories.Add(instanceID, value);
				entered = true;
			}
			if (entered)
			{
				value.SourceAtom = ((Component)collider).GetComponentInParent<Atom>();
				value.SourceGroupId = ((!((Object)(object)value.SourceAtom != (Object)null)) ? ((Object)((Component)attachedRigidbody).transform.root).GetInstanceID() : ((Object)value.SourceAtom).GetInstanceID());
			}
			Atom sourceAtom = value.SourceAtom;
			if (((flag || (Object)(object)sourceAtom == (Object)(object)targetAtom) && !Owner.Configuration.SelfContacts) || ((Object)(object)sourceAtom != (Object)null && (!sourceAtom.on || !sourceAtom.collisionEnabled)))
			{
				return;
			}
			ContactPoint[] contacts = collision.contacts;
			int num = contacts.Length;
			if (num == 0)
			{
				return;
			}
			Vector3 val = Vector3.zero;
			Vector3 val2 = Vector3.zero;
			int num2 = 0;
			Collider val3 = null;
			for (int j = 0; j < num; j++)
			{
				ContactPoint val4 = contacts[j];
				if (!((Object)(object)val4.thisCollider == (Object)null) && !((Object)(object)val4.thisCollider.attachedRigidbody != (Object)(object)Body) && Owner.OwnsCollider(val4.thisCollider))
				{
					val += val4.point;
					val2 += val4.normal;
					num2++;
					val3 = val4.thisCollider;
				}
			}
			if (num2 == 0 || val2.sqrMagnitude < 0.01f)
			{
				return;
			}
			val /= (float)num2;
			val2.Normalize();
			float num3 = Mathf.Abs(Vector3.Dot(collision.impulse, val2));
			if (num3 <= 0f)
			{
				return;
			}
			int num4 = Owner.SurfaceVertex(val3);
			num3 = ContactResponse.SkinImpulse(num3, Body.mass, num4 >= 0);
			if ((Object)(object)value.Own != (Object)(object)val3 || time - value.LastSeen > 0.25f)
			{
				value.Own = val3;
				value.OwnPoint = ((Component)val3).transform.InverseTransformPoint(val);
				value.OtherPoint = ((Component)collider).transform.InverseTransformPoint(val);
				value.Motion.Reset();
			}
			value.LastSeen = time;
			Vector3 val5 = ((Component)collider).transform.TransformPoint(value.OtherPoint);
			Vector3 p = val5 - ((Component)val3).transform.TransformPoint(value.OwnPoint);
			value.Motion.Observe(time, SkinSurface.Core(val5), SkinSurface.Core(p), SkinSurface.Core(val2));
			Vector3 pointVelocity = attachedRigidbody.GetPointVelocity(val);
			bool flag2 = pointVelocity.sqrMagnitude >= 0.0009f || value.Motion.SourceMoving;
			float num5 = ((!flag2) ? 0f : ContactResponse.NormalSpeed(SkinSurface.Core(collision.relativeVelocity), SkinSurface.Core(pointVelocity - Body.GetPointVelocity(val)), SkinSurface.Core(val2)));
			bool flag3 = value.History.Observe(time, num5, Owner.Configuration.ImpactThreshold.Value, entered);
			V3 tangentVelocity = value.Motion.TangentVelocity;
			Vector3 velocity = val2 * num5 + new Vector3(tangentVelocity.X, tangentVelocity.Y, tangentVelocity.Z);
			if (!flag2 || (!flag3 && tangentVelocity.LengthSquared == 0f))
			{
				return;
			}
			float num6 = EstimateRadius(collider, val2);
			for (int k = 0; k < num; k++)
			{
				ContactPoint val6 = contacts[k];
				if ((Object)(object)val6.thisCollider != (Object)null && (Object)(object)val6.thisCollider.attachedRigidbody == (Object)(object)Body)
				{
					float num7 = num6;
					Vector3 val7 = Vector3.ProjectOnPlane(val6.point - val, val2);
					num6 = Mathf.Max(num7, val7.magnitude);
				}
			}
			PhysicMaterial sharedMaterial = val3.sharedMaterial;
			PhysicMaterial sharedMaterial2 = collider.sharedMaterial;
			float a = ((!((Object)(object)sharedMaterial == (Object)null)) ? sharedMaterial.dynamicFriction : 0.6f);
			float b = ((!((Object)(object)sharedMaterial2 == (Object)null)) ? sharedMaterial2.dynamicFriction : 0.6f);
			float friction = CombineFriction(a, b, (PhysicMaterialCombine)((!((Object)(object)sharedMaterial == (Object)null)) ? ((int)sharedMaterial.frictionCombine) : 0), (PhysicMaterialCombine)((!((Object)(object)sharedMaterial2 == (Object)null)) ? ((int)sharedMaterial2.frictionCombine) : 0));
			ContactSample sample = new ContactSample
			{
				Target = ((Component)val3).transform,
				Point = val,
				Normal = val2,
				Velocity = velocity,
				Impulse = num3,
				Area = (float)Math.PI * num6 * num6,
				Friction = friction,
				DeltaTime = Time.fixedDeltaTime,
				ImpactEdge = flag3,
				Sustained = value.History.Sustained,
				Time = time,
				SurfaceVertex = num4,
				SourceId = ((Object)attachedRigidbody).GetInstanceID(),
				SourceGroupId = value.SourceGroupId
			};
			Owner.Enqueue(sample);
		}
		catch (Exception e)
		{
			Owner.Fail(e);
		}
	}

	public static float CombineFriction(float a, float b, PhysicMaterialCombine ma, PhysicMaterialCombine mb)
	{
		if ((int)ma == 3 || (int)mb == 3)
		{
			return Mathf.Max(a, b);
		}
		if ((int)ma == 1 || (int)mb == 1)
		{
			return a * b;
		}
		if ((int)ma == 2 || (int)mb == 2)
		{
			return Mathf.Min(a, b);
		}
		return (a + b) * 0.5f;
	}

	private static float EstimateRadius(Collider c, Vector3 normal)
	{
		SphereCollider val = (SphereCollider)(object)((c is SphereCollider) ? c : null);
		CapsuleCollider val2 = (CapsuleCollider)(object)((c is CapsuleCollider) ? c : null);
		Vector3 lossyScale = ((Component)c).transform.lossyScale;
		float num;
		if ((Object)(object)val != (Object)null)
		{
			num = val.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Max(Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z))) * 0.6f;
		}
		else if ((Object)(object)val2 != (Object)null)
		{
			float num2;
			if (val2.direction != 0)
			{
				num2 = ((val2.direction != 1) ? Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y)) : Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z)));
			}
			else
			{
				num2 = Mathf.Max(Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
			}
			num = val2.radius * num2 * 0.65f;
		}
		else
		{
			Bounds bounds = c.bounds;
			Vector3 extents = bounds.extents;
			float num3 = 4f * (Mathf.Abs(normal.x) * extents.y * extents.z + Mathf.Abs(normal.y) * extents.x * extents.z + Mathf.Abs(normal.z) * extents.x * extents.y);
			num = Mathf.Sqrt(num3 / (float)Math.PI) * 0.5f;
		}
		return Mathf.Clamp(num, 0.008f, 0.12f);
	}
}
