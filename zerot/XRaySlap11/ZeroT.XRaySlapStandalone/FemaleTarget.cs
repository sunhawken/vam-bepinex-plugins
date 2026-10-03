using System.Linq;
using UnityEngine;

namespace ZeroT.XRaySlapStandalone;

internal sealed class FemaleTarget
{
	internal readonly Atom atom;

	private Transform labia;

	private Transform anus;

	private Transform lips;

	private Transform pelvis;

	internal FemaleTarget(Atom a)
	{
		atom = a;
		Rigidbody[] componentsInChildren = ((Component)a).GetComponentsInChildren<Rigidbody>(true);
		labia = (((Object)(object)componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "LabiaTrigger") != (Object)null) ? ((Component)componentsInChildren.First((Rigidbody r) => ((Object)r).name == "LabiaTrigger")).transform : null);
		anus = (((Object)(object)componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "_JointAr") != (Object)null) ? ((Component)componentsInChildren.First((Rigidbody r) => ((Object)r).name == "_JointAr")).transform : null);
		lips = (((Object)(object)componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "LipTrigger") != (Object)null) ? ((Component)componentsInChildren.First((Rigidbody r) => ((Object)r).name == "LipTrigger")).transform : null);
		pelvis = (((Object)(object)componentsInChildren.FirstOrDefault((Rigidbody r) => ((Object)r).name == "pelvis") != (Object)null) ? ((Component)componentsInChildren.First((Rigidbody r) => ((Object)r).name == "pelvis")).transform : null);
	}

	internal float ClosestDistance(Vector3 p)
	{
		float num = float.MaxValue;
		if ((Object)(object)labia != (Object)null)
		{
			Vector3 val = labia.position - labia.up * 0.008f + labia.forward * 0.01f;
			num = Mathf.Min(num, Vector3.Distance(p, val));
		}
		if ((Object)(object)anus != (Object)null)
		{
			Vector3 val2 = anus.position + (((Object)(object)pelvis != (Object)null) ? pelvis.up : anus.up) * 0.01f;
			num = Mathf.Min(num, Vector3.Distance(p, val2));
		}
		if ((Object)(object)lips != (Object)null)
		{
			Vector3 val3 = lips.position - lips.up * 0.01f;
			num = Mathf.Min(num, Vector3.Distance(p, val3));
		}
		return num;
	}
}
