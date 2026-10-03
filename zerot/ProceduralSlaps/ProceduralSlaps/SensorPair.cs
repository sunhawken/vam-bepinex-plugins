using ProceduralSlaps.Core;
using UnityEngine;

namespace ProceduralSlaps;

internal sealed class SensorPair
{
	public readonly ContactHistory History = new ContactHistory();

	public readonly ContactMotion Motion = new ContactMotion();

	public Collider Own;

	public Atom SourceAtom;

	public int SourceGroupId;

	public Vector3 OwnPoint;

	public Vector3 OtherPoint;

	public float LastSeen;
}
