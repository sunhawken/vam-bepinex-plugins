using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;

// Monica.json (a real scene of the user's): every package it names, the real archives on disk (.var or .DISABLED), the real entries inside them.
// VaM only knows the installed .var packages; a .DISABLED package answers only through VPB, which registers it when VaM is asked for a path
// inside it (FileManagerSecure.SimMissHook). The AppearanceLoader script's own path checks run against that, without the helper, with the
// helper as it was (fast lookup, no switched-off support) and with the helper as it is.
public static class AppearanceMonicaScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
	private static Type T(Assembly a, string name) { return a.GetType(name, true); }
	private static FieldInfo Field(Type t, string n) { for (; t != null; t = t.BaseType) { var f = t.GetField(n, All | BindingFlags.DeclaredOnly); if (f != null) return f; } return null; }

	[Scenario("appearanceloader_monica", "ZeroT.AppearanceLoaderHelper")]
	public static void Monica(Harness h)
	{
		string scene = Environment.GetEnvironmentVariable("MONICA_SCENE") ?? "T:/New folder/Saves/scene/Z/Monica.json";
		string vam = Environment.GetEnvironmentVariable("VAM_DIR") ?? "T:/New folder";
		if (!File.Exists(scene)) { Console.WriteLine("  SKIP no " + scene); return; }
		h.Unpatch("com.zerot.appearanceloader.helper");
		var asm = h.LoadTarget("AppearanceLoaderScripts");
		var al = T(asm, "ky1001.AppearanceLoader");
		Func<object> makeLoader = () =>
		{
			var l = Activator.CreateInstance(al, true);
			Field(al, "missingItem").SetValue(l, new JSONStorableString("", ""));
			Field(al, "message").SetValue(l, new JSONStorableString("", ""));
			Field(al, "_searchMissingTextures_Bool").SetValue(l, new JSONStorableBool("search", false));
			Field(al, "loadDir").SetValue(l, "Saves/scene/");
			return l;
		};
		var checkPath = al.GetMethod("checkPath", All, null, new Type[] { typeof(string) }, null);
		Func<object, string, string> check = (l, arg) => (string)checkPath.Invoke(l, new object[] { arg });

		// what the scene asks for: "Creator.Package.N:/entry" (files only)
		string text = File.ReadAllText(scene);
		var refs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "([A-Za-z0-9_\\-\\.]+?\\.\\d+):/([^\"\\\\]+)"))
		{
			string entry = m.Groups[2].Value;
			if (entry.EndsWith("/") || entry.IndexOf('.') < 0) continue;
			HashSet<string> set;
			if (!refs.TryGetValue(m.Groups[1].Value, out set)) refs[m.Groups[1].Value] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			set.Add(entry);
		}
		// the archives on disk by package uid: an enabled .var wins over a switched-off one
		var archive = new Dictionary<string, KeyValuePair<string, bool>>(StringComparer.OrdinalIgnoreCase);   // uid -> path, switched off
		foreach (var root in new[] { Path.Combine(vam, "AddonPackages"), Path.Combine(vam, "AllPackages") })
		{
			if (!Directory.Exists(root)) continue;
			foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(f), low = name.ToLowerInvariant(), uid; bool off;
				if (low.EndsWith(".var.disabled")) { uid = name.Substring(0, name.Length - 13); off = true; }
				else if (low.EndsWith(".disabled")) { uid = name.Substring(0, name.Length - 9); off = true; }
				else if (low.EndsWith(".var")) { uid = name.Substring(0, name.Length - 4); off = false; }
				else continue;
				if (!refs.ContainsKey(uid)) continue;
				KeyValuePair<string, bool> have;
				if (!archive.TryGetValue(uid, out have) || (have.Value && !off)) archive[uid] = new KeyValuePair<string, bool>(f, off);
			}
		}
		// which of the asked-for entries really are inside their archive
		var real = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var kv in archive)
		{
			var inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try { using (var z = System.IO.Compression.ZipFile.OpenRead(kv.Value.Key)) foreach (var e in z.Entries) inside.Add(e.FullName.Replace((char)92, '/')); } catch { }
			real[kv.Key] = inside;
		}
		int nInstalled = archive.Count(k => !k.Value.Value), nOff = archive.Count(k => k.Value.Value), nGone = refs.Keys.Count(u => !archive.ContainsKey(u));
		Console.WriteLine("  info    " + Path.GetFileName(scene) + ": " + refs.Count + " packages named: " + nInstalled + " installed (.var), " + nOff + " only switched off (.DISABLED), " + nGone + " not on disk");
		h.Check(refs.Count > 5 && nOff > 0, "the scene names switched-off packages (" + nOff + ") - the case under test");

		// VaM's view: installed .var packages are registered and their files answerable; a switched-off package answers only through VPB
		MVR.FileManagementSecure.FileManagerSecure.SimReset();
		MVR.FileManagement.FileManager.SimPackageUids.Clear();
		MVR.FileManagement.FileManager.SimPackageUids.Add("Sim.Installed.1");   // the helper's package index is never empty on a real install, even when none of this scene's packages is installed
		var offUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var asked = new List<string>();   // every uid:/entry the scene names that is really in its archive
		foreach (var kv in archive)
		{
			if (!kv.Value.Value) MVR.FileManagement.FileManager.SimPackageUids.Add(kv.Key); else offUids.Add(kv.Key);
			foreach (var e in refs[kv.Key])
				if (real[kv.Key].Contains(e)) { asked.Add(kv.Key + ":/" + e); if (!kv.Value.Value) MVR.FileManagementSecure.FileManagerSecure.SimAddFile(kv.Key + ":/" + e); }
		}
		bool vpbOn = true;
		VPB.VamOnDemandLoader.Disabled.Clear();
		foreach (var u in offUids) VPB.VamOnDemandLoader.Disabled.Add(u);
		MVR.FileManagementSecure.FileManagerSecure.SimMissHook = p =>
		{
			if (!vpbOn) return false;
			int sep = p.IndexOf(":/", StringComparison.Ordinal);
			if (sep <= 0) return false;
			string uid = p.Substring(0, sep), entry = p.Substring(sep + 2);
			HashSet<string> inside;
			if (!offUids.Contains(uid) || !real.TryGetValue(uid, out inside)) return false;
			return entry.Length == 0 || inside.Contains(entry) || inside.Any(x => x.StartsWith(entry.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
		};
		Func<List<string>> resolveAll = () =>
		{
			var l = makeLoader(); var res = new List<string>();
			foreach (var a in asked) res.Add(check(l, a));
			return res;
		};
		Func<List<string>, int> found = r => r.Count(x => !string.IsNullOrEmpty(x));
		Func<string, bool> isOff = a => offUids.Contains(a.Substring(0, a.IndexOf(":/", StringComparison.Ordinal)));
		int askedOff = asked.Count(isOff), askedOn = asked.Count - askedOff;
		Console.WriteLine("  info    " + asked.Count + " files the scene names exist in their archives: " + askedOn + " in installed packages, " + askedOff + " in switched-off ones");
		h.Check(askedOff > 0, "...some of them inside switched-off packages");

		// 1. the loader as it is (no helper): its own checks ask VaM, which VPB answers
		var r0 = resolveAll();
		h.Check(found(r0) == asked.Count, "without the helper: the loader finds all " + asked.Count + " files (" + found(r0) + "); switched-off packages work because VPB answers VaM's questions");

		// 2. the helper as it was: fast lookup, no switched-off support: 'not installed' before VaM / VPB are asked
		BepInEx.Configuration.ConfigFile.Overrides["Speed.UseDisabledPackages"] = "false";   // the helper as it was, until switched on below
		h.Start(); h.Step(3f);
		h.Check(h.LogHas("AppearanceLoader patched"), "the helper is active");
		var r1 = resolveAll();
		int off1 = r1.Where((x, i) => isOff(asked[i]) && !string.IsNullOrEmpty(x)).Count();
		h.Check(found(r1) - off1 == askedOn && off1 == 0, "the helper as it was: installed packages fine (" + (found(r1) - off1) + "/" + askedOn + "), switched-off ones all answered 'not installed' (" + off1 + "/" + askedOff + " found)");

		// 3. the helper now: switched-off packages are let through to VaM / VPB
		h.Set("Speed", "UseDisabledPackages", "true");
		var r2 = resolveAll();
		h.Check(found(r2) == asked.Count && r2.SequenceEqual(r0), "the helper now: all " + asked.Count + " files found (" + found(r2) + "), identical answers to the loader without the helper");

		// 4. without VPB nothing can bring a switched-off package in: they stay 'not installed', nothing else changes
		VPB.VamOnDemandLoader.Disabled.Clear(); vpbOn = false;
		var r3 = resolveAll();
		h.Check(found(r3) == askedOn, "without VPB: installed packages still found (" + found(r3) + "/" + askedOn + "), switched-off ones are not");
		MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls = 0;
		string gone = check(makeLoader(), "Gone.Nowhere.3:/Custom/Clothing/x.vam");
		h.Check(gone == "" && MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls <= 6, "a package that is not on disk at all: still a handful of questions (" + MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls + ")");
		foreach (var u in offUids) VPB.VamOnDemandLoader.Disabled.Add(u);
		MVR.FileManagementSecure.FileManagerSecure.SimMissHook = null;
		VPB.VamOnDemandLoader.Disabled.Clear();
		// the helper keeps its package index and counters in statics: leave them as a fresh process would, for the scenarios that run after this one
		var hooks = h.Asm.GetType("ZeroT.AppearanceLoaderHelper.Hooks", false);
		if (hooks != null)
			foreach (var f in hooks.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (f.IsInitOnly || f.IsLiteral) continue;
				if (f.FieldType == typeof(int)) f.SetValue(null, 0);
				else if (f.FieldType == typeof(bool)) f.SetValue(null, false);
				else if (f.FieldType == typeof(float)) f.SetValue(null, 0f);
				else if (!f.FieldType.IsValueType) f.SetValue(null, null);
			}
		MVR.FileManagement.FileManager.SimPackageUids.Clear();
		MVR.FileManagement.FileManager.SimRefreshTime = MVR.FileManagement.FileManager.SimRefreshTime.AddMinutes(5);
		MVR.FileManagementSecure.FileManagerSecure.SimReset();
	}
}
