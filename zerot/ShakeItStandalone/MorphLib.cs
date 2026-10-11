using System;
using System.Collections.Generic;
using MVR.FileManagement;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// Finds the package's morphs (SI_* for ShakeIt, FI_* for FlexIt) on one person. The .var's plugin knew its own package prefix; this
	// looks for the newest installed CheesyFX.ShakeIt.<n> and falls back to the other versions and to an unpackaged install.
	public sealed class MorphLib
	{
		private readonly Atom atom;
		private readonly Action<string> log;
		private readonly Dictionary<string, DAZMorph> cache = new Dictionary<string, DAZMorph>();
		private readonly Dictionary<string, float> missingUntil = new Dictionary<string, float>();
		private readonly HashSet<string> reported = new HashSet<string>();
		private string workingPrefix;
		public int Misses;

		public MorphLib(Atom atom, Action<string> log)
		{
			this.atom = atom;
			this.log = log;
		}

		public DAZCharacterSelector Selector
		{
			get { return atom.GetStorableByID("geometry") as DAZCharacterSelector; }
		}

		public GenerateDAZMorphsControlUI UI
		{
			get
			{
				DAZCharacterSelector dcs = Selector;
				return dcs == null ? null : dcs.morphsControlUI;
			}
		}

		public string Gender
		{
			get
			{
				DAZCharacterSelector dcs = Selector;
				return dcs == null ? "female" : dcs.gender.ToString().ToLower();
			}
		}

		public string Dir(bool flex)
		{
			return "Custom/Atom/Person/Morphs/" + Gender + "/CheesyFX/" + (flex ? "FlexIt/" : "ShakeIt/");
		}

		// package uids of the ShakeIt package, newest first
		public static List<string> PackagePrefixes()
		{
			List<KeyValuePair<int, string>> found = new List<KeyValuePair<int, string>>();
			try
			{
				List<string> all = FileManager.GetPackageUids();
				for (int i = 0; all != null && i < all.Count; i++)
				{
					string u = all[i];
					if (u != null && u.StartsWith("CheesyFX.ShakeIt."))
					{
						int v;
						string tail = u.Substring("CheesyFX.ShakeIt.".Length);
						found.Add(new KeyValuePair<int, string>(int.TryParse(tail, out v) ? v : -1, u));
					}
				}
			}
			catch
			{
			}
			found.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return b.Key.CompareTo(a.Key); });
			List<string> o = new List<string>();
			for (int i = 0; i < found.Count; i++)
			{
				o.Add(found[i].Value + ":/");
			}
			return o;
		}

		public string BestPrefix()
		{
			if (workingPrefix != null)
			{
				return workingPrefix;
			}
			List<string> p = PackagePrefixes();
			return p.Count > 0 ? p[0] : "";
		}

		public void Invalidate()
		{
			cache.Clear();
			missingUntil.Clear();
		}

		// uid is cached; a cached morph that VaM destroyed (the person's look was rebuilt) is looked up again
		public DAZMorph Get(string name, bool flex)
		{
			string key = (flex ? "F:" : "S:") + name;
			DAZMorph m;
			if (cache.TryGetValue(key, out m))
			{
				if (m != null)
				{
					return m;
				}
				cache.Remove(key);
			}
			float until;
			if (missingUntil.TryGetValue(key, out until) && Time.unscaledTime < until)
			{
				return null;
			}
			GenerateDAZMorphsControlUI ui = UI;
			if (ui == null)
			{
				return null;
			}
			string rel = Dir(flex) + name + ".vmi";
			List<string> tries = new List<string>();
			if (workingPrefix != null)
			{
				tries.Add(workingPrefix + rel);
			}
			List<string> pk = PackagePrefixes();
			for (int i = 0; i < pk.Count; i++)
			{
				if (pk[i] != workingPrefix)
				{
					tries.Add(pk[i] + rel);
				}
			}
			tries.Add(rel);
			for (int i = 0; i < tries.Count; i++)
			{
				try
				{
					m = ui.GetMorphByUid(tries[i]);
				}
				catch
				{
					m = null;
				}
				if (m != null)
				{
					int c = tries[i].IndexOf(":/");
					workingPrefix = c > 0 ? tries[i].Substring(0, c + 2) : "";
					cache[key] = m;
					return m;
				}
			}
			missingUntil[key] = Time.unscaledTime + 5f;
			Misses++;
			return null;
		}

		// VaM rebuilds a person's morphs when the look changes; the old DAZMorph objects then stop doing anything
		public string SentinelUid;
		public DAZMorph Sentinel;

		public bool SentinelChanged()
		{
			if (Sentinel == null)
			{
				foreach (KeyValuePair<string, DAZMorph> kv in cache)
				{
					if (kv.Value != null)
					{
						Sentinel = kv.Value;
						SentinelUid = kv.Value.uid;
						break;
					}
				}
				return false;
			}
			GenerateDAZMorphsControlUI ui = UI;
			if (ui == null || string.IsNullOrEmpty(SentinelUid)) return false;
			DAZMorph now;
			try { now = ui.GetMorphByUid(SentinelUid); } catch { return false; }
			if (now != Sentinel)
			{
				Sentinel = null;
				SentinelUid = null;
				return true;
			}
			return false;
		}

		// morphs that were not there at first (the package's morphs are added by VaM a little later): is one of them there now?
		public bool AnyMissingNowFound()
		{
			if (missingUntil.Count == 0) return false;
			GenerateDAZMorphsControlUI ui = UI;
			if (ui == null) return false;
			foreach (string key in new List<string>(missingUntil.Keys))
			{
				bool flex = key.StartsWith("F:");
				string rel = Dir(flex) + key.Substring(2) + ".vmi";
				List<string> pk = PackagePrefixes();
				pk.Add("");
				for (int i = 0; i < pk.Count; i++)
				{
					try
					{
						if (ui.GetMorphByUid(pk[i] + rel) != null) return true;
					}
					catch
					{
					}
				}
			}
			return false;
		}

		public bool Stale(string anyName, bool flex)
		{
			string key = (flex ? "F:" : "S:") + anyName;
			DAZMorph m;
			return cache.TryGetValue(key, out m) && m == null;
		}

		// every SI_/FI_ morph back to 0 (the plugins' ResetMorphs)
		public int ResetAll()
		{
			int n = 0;
			try
			{
				DAZMesh[] meshes = atom.GetComponentsInChildren<DAZMesh>();
				for (int i = 0; i < meshes.Length; i++)
				{
					if (meshes[i].morphBank == null)
					{
						continue;
					}
					List<DAZMorph> l = meshes[i].morphBank.morphs;
					for (int j = 0; j < l.Count; j++)
					{
						DAZMorph mo = l[j];
						if (mo != null && mo.uid != null && (mo.uid.Contains("/SI_") || mo.uid.Contains("/FI_") || mo.uid.StartsWith("SI_") || mo.uid.StartsWith("FI_")))
						{
							mo.morphValue = 0f;
							n++;
						}
					}
				}
			}
			catch (Exception e)
			{
				log("Reset morphs on " + atom.uid + ": " + e.Message);
			}
			return n;
		}
	}

	public static class Bodies
	{
		// atom.forceReceivers first (what the .var uses), the rigidbody list as a fallback
		public static Rigidbody Force(Atom atom, string name)
		{
			ForceReceiver[] fr = atom.forceReceivers;
			for (int i = 0; fr != null && i < fr.Length; i++)
			{
				if (fr[i] != null && fr[i].name == name)
				{
					Rigidbody rb = fr[i].GetComponent<Rigidbody>();
					if (rb != null)
					{
						return rb;
					}
				}
			}
			return Rb(atom, name);
		}

		public static Rigidbody Rb(Atom atom, string name)
		{
			Rigidbody[] all = atom.rigidbodies;
			for (int i = 0; all != null && i < all.Length; i++)
			{
				if (all[i] != null && all[i].name == name)
				{
					return all[i];
				}
			}
			return null;
		}
	}
}
