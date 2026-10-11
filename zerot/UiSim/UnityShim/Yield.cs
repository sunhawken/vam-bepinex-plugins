using System;
using System.Collections;

namespace UnityEngine
{
	public partial class YieldInstruction { }

	// IEnumerator, like Unity's: a coroutine yielding one of these waits while keepWaiting is true
	public abstract partial class CustomYieldInstruction : IEnumerator
	{
		public abstract bool keepWaiting { get; }
		public object Current { get { return null; } }
		public bool MoveNext() { return keepWaiting; }
		public void Reset() { }
	}

	public sealed partial class WaitUntil : CustomYieldInstruction
	{
		private readonly Func<bool> pred;
		public WaitUntil(Func<bool> predicate) { pred = predicate; }
		public override bool keepWaiting { get { return !pred(); } }
	}

	public sealed partial class WaitWhile : CustomYieldInstruction
	{
		private readonly Func<bool> pred;
		public WaitWhile(Func<bool> predicate) { pred = predicate; }
		public override bool keepWaiting { get { return pred(); } }
	}

	public partial class WaitForSecondsRealtime : CustomYieldInstruction
	{
		private readonly float until;
		public WaitForSecondsRealtime(float time) { until = Time.unscaledTime + time; }
		public override bool keepWaiting { get { return Time.unscaledTime < until; } }
	}

	public sealed partial class WaitForFixedUpdate : YieldInstruction { }
}

namespace AssetBundles
{
	public abstract partial class AssetBundleLoadOperation : IEnumerator
	{
		public abstract bool Update();
		public abstract bool IsDone();
		public object Current { get { return null; } }
		public bool MoveNext() { return !IsDone(); }
		public void Reset() { }
	}

	public abstract partial class AssetBundleLoadAssetOperation : AssetBundleLoadOperation
	{
		public virtual T GetAsset<T>() { return default(T); }
	}

	// asset bundles are not opened by the simulator: every request completes at once with a placeholder prefab
	public sealed class SimAssetOperation : AssetBundleLoadAssetOperation
	{
		private readonly object asset;
		public SimAssetOperation(object a) { asset = a; }
		public override bool Update() { return false; }
		public override bool IsDone() { return true; }
		public override T GetAsset<T>() { return asset is T ? (T)asset : default(T); }
	}

	public partial class AssetBundleManager : UnityEngine.MonoBehaviour
	{
		public static AssetBundleLoadAssetOperation LoadAssetAsync(string assetBundleName, string assetName, Type type)
		{
			return new SimAssetOperation(type == typeof(UnityEngine.GameObject) || type == null ? new UnityEngine.GameObject(assetName) : null);
		}
	}
}
