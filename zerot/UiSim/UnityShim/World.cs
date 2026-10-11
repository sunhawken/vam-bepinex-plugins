using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
	public enum Space { World = 0, Self = 1 }

	// real transform hierarchy: world position/rotation come from the parent chain, like Unity
	public partial class Transform
	{
		public Vector3 lossyScale { get { var s = localScale; for (var p = parent; p != null; p = p.parent) s = Vector3.Scale(s, p.localScale); return s; } }
		public Vector3 position
		{
			get => parent == null ? localPosition : parent.TransformPoint(localPosition);
			set => localPosition = parent == null ? value : parent.InverseTransformPoint(value);
		}
		public Quaternion rotation
		{
			get => parent == null ? localRotation : parent.rotation * localRotation;
			set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
		}
		public Vector3 eulerAngles { get => rotation.eulerAngles; set => rotation = Quaternion.Euler(value.x, value.y, value.z); }
		public Vector3 localEulerAngles { get => localRotation.eulerAngles; set => localRotation = Quaternion.Euler(value.x, value.y, value.z); }
		public Vector3 forward => rotation * new Vector3(0, 0, 1);
		public Vector3 up => rotation * new Vector3(0, 1, 0);
		public Vector3 right => rotation * new Vector3(1, 0, 0);
		public Vector3 TransformPoint(Vector3 p) => position0() + rotation * Vector3.Scale(lossyScale, p);
		public Vector3 InverseTransformPoint(Vector3 p) { var s = lossyScale; return Vector3.Scale(new Vector3(Inv(s.x), Inv(s.y), Inv(s.z)), Quaternion.Inverse(rotation) * (p - position0())); }
		public Vector3 TransformDirection(Vector3 d) => rotation * d;
		public Vector3 InverseTransformDirection(Vector3 d) => Quaternion.Inverse(rotation) * d;
		private Vector3 position0() => position;
		private static float Inv(float f) => Math.Abs(f) < 1e-8f ? 0f : 1f / f;
		public bool IsChildOf(Transform p) { for (var t = this; t != null; t = t.parent) if (t == p) return true; return false; }
		public void SetAsFirstSibling() { SetSiblingIndex(0); }
		public void Translate(Vector3 d) { Translate(d, Space.Self); }
		public void Translate(Vector3 d, Space s) { position = position + (s == Space.Self ? TransformDirection(d) : d); }
		public void Rotate(Vector3 euler) { Rotate(euler, Space.Self); }
		public void Rotate(Vector3 euler, Space s) { var q = Quaternion.Euler(euler.x, euler.y, euler.z); if (s == Space.Self) localRotation = localRotation * q; else rotation = q * rotation; }
		public void RotateAround(Vector3 point, Vector3 axis, float angle) { var q = Quaternion.AngleAxis(angle, axis); position = point + q * (position - point); rotation = q * rotation; }
		public void LookAt(Transform t) { LookAt(t.position); }
		public void LookAt(Vector3 target) { rotation = Quaternion.LookRotation(target - position); }
	}

	public partial struct Vector3
	{
		public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
		public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = t < 0 ? 0 : (t > 1 ? 1 : t); return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
		public static Vector3 MoveTowards(Vector3 c, Vector3 t, float d) { var v = t - c; float m = v.magnitude; return m <= d || m < 1e-6f ? t : c + v / m * d; }
	}

	public partial class GameObject
	{
		// "A/B/C" path lookup over every live object (VaM code uses GameObject.Find("SceneAtoms/CoreControl/..."))
		public static GameObject Find(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			var parts = path.Split('/');
			foreach (var go in Sim.AllObjects)
			{
				if (go.destroyed || go.name != parts[parts.Length - 1]) continue;
				var t = go.transform; bool ok = true;
				for (int i = parts.Length - 2; i >= 0; i--) { t = t.parent; if (t == null || t.gameObject.name != parts[i]) { ok = false; break; } }
				if (ok && (parts.Length == 1 ? t.parent == null : true)) return go;
			}
			return null;
		}
	}

	public partial class Object
	{
		public static T[] FindObjectsOfType<T>() where T : Object
		{
			var l = new List<T>();
			foreach (var go in Sim.AllObjects.ToArray()) { if (go.destroyed) continue; foreach (var c in go.comps) if (c is T t) l.Add(t); }
			return l.ToArray();
		}
		public static T FindObjectOfType<T>() where T : Object { var a = FindObjectsOfType<T>(); return a.Length > 0 ? a[0] : null; }
	}
}
