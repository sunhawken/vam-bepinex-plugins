using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SimpleJSON;

namespace ZeroT.StockingPresetKeeper
{
	// Pure comparison logic (no Unity types, so it can be tested outside VaM): "is what is live the same as what this preset file says?"
	//
	// A VaM item preset (.vap) written with setUnlistedParamsToDefault=true lists EVERY parameter of every storable of the item, as strings
	// ("true", "1.975788", a path, {h,s,v}, [x,y,z]). The live side is the same storable's GetJSON(true, true, true): every parameter again.
	// Only the keys the preset lists are compared. Text that is written differently but means the same (case / slash / SELF: / ".latest"
	// package versions / 1 vs 1.0000001) is equal; a key the live side does not have at all is skipped, not counted as different.
	internal sealed class PresetDiff
	{
		public string Id;      // storable id
		public string Path;    // key inside it, '/' separated
		public string Want;    // what the preset says
		public string Have;    // what is live
		public string Key { get { return Id + "/" + Path; } }
		public override string ToString() { return Key + ": preset " + Short(Want) + ", live " + Short(Have); }
		private static string Short(string s) { s = s ?? "(none)"; return s.Length > 90 ? s.Substring(0, 87) + "..." : "'" + s + "'"; }
	}

	internal static class PresetCompare
	{
		private static readonly Regex Version = new Regex(@"^([^:/\\]+?)\.(?:latest|\d+):", RegexOptions.Compiled);

		// compares one preset storable entry against its live JSON; adds a PresetDiff for every listed key that differs.
		// skipKeys: keys (Id/Path) not to compare. skipped counts keys the live side did not have.
		public static void CompareStorable(string id, JSONClass want, JSONClass have, ICollection<string> skipKeys, List<PresetDiff> diffs, ref int skipped)
		{
			if (want == null) return;
			foreach (string k in want.Keys)
			{
				if (k == "id") continue;
				CompareNode(id, k, want[k], have != null && have.HasKey(k) ? have[k] : null, skipKeys, diffs, ref skipped);
			}
		}

		public static void CompareNode(string id, string path, JSONNode want, JSONNode have, ICollection<string> skipKeys, List<PresetDiff> diffs, ref int skipped)
		{
			if (want == null) return;
			if (skipKeys != null && skipKeys.Contains(id + "/" + path)) return;
			JSONClass wc = want as JSONClass;
			JSONArray wa = want as JSONArray;
			if (wc != null)
			{
				JSONClass hc = have as JSONClass;
				foreach (string k in wc.Keys)
				{
					JSONNode h = null;
					if (hc != null && hc.HasKey(k)) h = hc[k];
					else if (have != null && hc == null) { Add(diffs, id, path + "/" + k, Text(wc[k]), Text(have)); continue; }
					CompareNode(id, path + "/" + k, wc[k], h, skipKeys, diffs, ref skipped);
				}
				return;
			}
			if (have == null) { skipped++; return; }   // live side does not carry this key: cannot tell
			if (wa != null)
			{
				JSONArray ha = have as JSONArray;
				if (ha == null) { Add(diffs, id, path, Text(want), Text(have)); return; }
				if (wa.Count != ha.Count) { Add(diffs, id, path, Text(want), Text(have)); return; }
				for (int i = 0; i < wa.Count; i++) CompareNode(id, path + "/" + i, wa[i], ha[i], skipKeys, diffs, ref skipped);
				return;
			}
			if (!TextEqual(want.Value, have is JSONClass || have is JSONArray ? Text(have) : have.Value)) Add(diffs, id, path, want.Value, Text(have));
		}

		private static void Add(List<PresetDiff> diffs, string id, string path, string want, string have)
		{
			PresetDiff d = new PresetDiff(); d.Id = id; d.Path = path; d.Want = want; d.Have = have; diffs.Add(d);
		}

		private static string Text(JSONNode n)
		{
			if (n == null) return null;
			if (n is JSONClass || n is JSONArray) { StringBuilder sb = new StringBuilder(); n.ToString(string.Empty, sb); return sb.ToString(); }
			return n.Value;
		}

		// ---- scalar equality ---------------------------------------------------------------------------------------------------------
		public static bool TextEqual(string a, string b)
		{
			a = (a ?? string.Empty).Trim(); b = (b ?? string.Empty).Trim();
			if (a == b) return true;
			bool ba, bb;
			if (TryBool(a, out ba) && TryBool(b, out bb)) return ba == bb;
			double x, y;
			if (TryNum(a, out x) && TryNum(b, out y)) return Math.Abs(x - y) <= 1e-5 + 1e-4 * Math.Max(Math.Abs(x), Math.Abs(y));
			return NormPath(a) == NormPath(b) || SameIgnoringVersion(a, b);
		}

		private static bool TryBool(string s, out bool v)
		{
			v = false;
			if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) { v = true; return true; }
			if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) { v = false; return true; }
			return false;
		}

		private static bool TryNum(string s, out double v)
		{
			v = 0;
			if (s.Length == 0 || s.Length > 32) return false;
			char c = s[0];
			if (!(char.IsDigit(c) || c == '-' || c == '+' || c == '.')) return false;
			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
		}

		// package paths: slashes, case, SELF:, ".var:" -> ":"
		public static string NormPath(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			s = s.Replace('\\', '/').Trim();
			while (s.IndexOf("//", StringComparison.Ordinal) >= 0) s = s.Replace("//", "/");
			if (s.StartsWith("SELF:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
			s = s.Replace(".var:/", ":/");
			return s.ToLowerInvariant();
		}

		// a package written with ".latest" matches the same package with any version number
		private static bool SameIgnoringVersion(string a, string b)
		{
			if (a.IndexOf(".latest:", StringComparison.OrdinalIgnoreCase) < 0 && b.IndexOf(".latest:", StringComparison.OrdinalIgnoreCase) < 0) return false;
			return Version.Replace(NormPath(a), "$1:") == Version.Replace(NormPath(b), "$1:");
		}
	}
}
