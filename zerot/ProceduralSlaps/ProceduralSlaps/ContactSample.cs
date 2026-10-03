using ProceduralSlaps.Core;
using UnityEngine;

namespace ProceduralSlaps;

public struct ContactSample
{
	public Vector3 Point;

	public Vector3 Normal;

	public Vector3 Velocity;

	public Transform Target;

	private Vector3 localPoint;

	private Vector3 localNormal;

	public float Impulse;

	public float Area;

	public float Friction;

	public float DeltaTime;

	public float Time;

	public float FirstTime;

	public bool ImpactEdge;

	public bool Sustained;

	public int SurfaceVertex;

	public int SourceId;

	public int SourceGroupId;

	public Response Response;

	public float LastIrritation;

	public void CapturePose()
	{
		if (!((Object)(object)Target == (Object)null))
		{
			localPoint = Target.InverseTransformPoint(Point);
			localNormal = Target.InverseTransformDirection(Normal);
		}
	}

	public void ResolvePose()
	{
		if (!((Object)(object)Target == (Object)null))
		{
			Point = Target.TransformPoint(localPoint);
			Vector3 val = Target.TransformDirection(localNormal);
			Normal = val.normalized;
		}
	}

	public static bool TryMerge(ContactSample previous, ContactSample next, out ContactSample merged)
	{
		merged = previous;
		if ((previous.SourceId != next.SourceId && (previous.SourceGroupId == 0 || previous.SourceGroupId != next.SourceGroupId)) || next.Time < previous.Time || next.Time - previous.FirstTime > 0.05f)
		{
			return false;
		}
		if (next.Time != previous.Time)
		{
			previous.ResolvePose();
		}
		merged = previous;
		Vector3 val = next.Point - previous.Point;
		if (Vector3.Dot(previous.Normal, next.Normal) < 0.7f || Mathf.Abs(Vector3.Dot(val, previous.Normal)) > 0.025f || val.sqrMagnitude > 0.0081f)
		{
			return false;
		}
		float magnitude = val.magnitude;
		float radius = previous.Response.Radius;
		float radius2 = next.Response.Radius;
		float num;
		Vector3 val2;
		if (radius >= magnitude + radius2)
		{
			num = radius;
			val2 = previous.Point;
		}
		else if (radius2 >= magnitude + radius)
		{
			num = radius2;
			val2 = next.Point;
		}
		else
		{
			num = (magnitude + radius + radius2) * 0.5f;
			val2 = previous.Point + val * ((num - radius) / magnitude);
		}
		if (num > 0.12f)
		{
			return false;
		}
		Response response = previous.Response;
		response.Impact = Mathf.Max(response.Impact, next.Response.Impact);
		response.Radius = num;
		if (next.Time == previous.Time)
		{
			response.Irritation += Mathf.Max(0f, next.Response.Irritation - previous.LastIrritation);
			merged.LastIrritation = Mathf.Max(previous.LastIrritation, next.Response.Irritation);
		}
		else
		{
			response.Irritation += next.Response.Irritation;
			merged.LastIrritation = next.Response.Irritation;
		}
		merged.Point = val2;
		Vector3 val3 = previous.Normal + next.Normal;
		merged.Normal = val3.normalized;
		merged.Time = next.Time;
		merged.Response = response;
		merged.CapturePose();
		if (previous.SurfaceVertex < 0)
		{
			goto IL_02d3;
		}
		if (next.SurfaceVertex >= 0)
		{
			Vector3 val4 = val2 - next.Point;
			float sqrMagnitude = val4.sqrMagnitude;
			Vector3 val5 = val2 - previous.Point;
			if (sqrMagnitude < val5.sqrMagnitude)
			{
				goto IL_02d3;
			}
		}
		goto IL_02e0;
		IL_02d3:
		merged.SurfaceVertex = next.SurfaceVertex;
		goto IL_02e0;
		IL_02e0:
		return true;
	}
}
