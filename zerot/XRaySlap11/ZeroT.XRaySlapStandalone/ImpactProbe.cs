using UnityEngine;

namespace ZeroT.XRaySlapStandalone;

public sealed class ImpactProbe : MonoBehaviour
{
	internal XRaySlapStandalone owner;

	internal Atom atom;

	internal string region;

	private float nextImpact;

	private void OnCollisionEnter(Collision c)
	{
		Try(c);
	}

	private void OnCollisionStay(Collision c)
	{
		Try(c);
	}

	private void Try(Collision c)
	{
		if (!((Object)(object)owner == (Object)null) && !(Time.time < nextImpact))
		{
			nextImpact = Time.time + 0.045f;
			owner.HandleImpact(this, c);
		}
	}
}
