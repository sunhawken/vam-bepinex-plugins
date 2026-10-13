using System.Collections.Generic;

// stands in for the internal VPB.VamOnDemandLoader of the real VPB.dll: the one lookup the AppearanceLoader helper asks (found by name through reflection)
namespace VPB
{
	internal static class VamOnDemandLoader
	{
		public static readonly HashSet<string> Disabled = new HashSet<string>();
		internal static bool IsKnownDisabledPackageUid(string uid) { return uid != null && Disabled.Contains(uid); }
	}
}
