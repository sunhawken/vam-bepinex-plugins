using System;

namespace Unity.Collections
{
	public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }

	public partial struct NativeArray<T> : IDisposable
	{
		public T[] data;
		public NativeArray(int length, Allocator allocator) { data = new T[length]; }
		public NativeArray(T[] array, Allocator allocator) { data = (T[])array.Clone(); }
		public int Length { get { return data == null ? 0 : data.Length; } }
		public bool IsCreated { get { return data != null; } }
		public T this[int index] { get { return data[index]; } set { data[index] = value; } }
		public void CopyTo(T[] array) { if (data != null) Array.Copy(data, array, Math.Min(data.Length, array.Length)); }
		public void CopyFrom(T[] array) { if (data != null) Array.Copy(array, data, Math.Min(data.Length, array.Length)); }
		public T[] ToArray() { return data == null ? new T[0] : (T[])data.Clone(); }
		public void Dispose() { data = null; }
	}
}

namespace UnityEngine
{
	public partial struct Pose
	{
		public Vector3 position;
		public Quaternion rotation;
		public Pose(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
		public static Pose identity { get { return new Pose(Vector3.zero, Quaternion.identity); } }
	}
}
