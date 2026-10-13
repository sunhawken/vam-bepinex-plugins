using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UnityEngine;

// vamsim --scene <scene.json> [--vam "T:/New folder"]
//   Loads a whole VaM scene into the simulator without VaM:
//   1. every package the scene references is resolved against AddonPackages (exact version, "latest", case-sensitive paths like VaM) -> missing files are listed
//   2. every atom is instantiated (people with a bone hierarchy, plugin-driven CUA atoms with their reParent object + controller)
//   3. the CUAClothingAlt positioning logic (Skynet.CUAClothingAlt.8 LateUpdate / SyncLocalPosition, ported line by line) runs for 10 s against moving
//      nipple/head bones, once as the scene is stored and once with every VaM link/Hold ignored, and the result is checked.
//   Never throws: every problem is reported as a line and the exit code says whether anything was wrong.
public static class SceneSim
{
	static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
	static int problems;
	static void Bad(string msg) { problems++; Console.WriteLine("  PROBLEM " + msg); }
	static void Ok(string msg) { Console.WriteLine("  ok      " + msg); }
	static void Info(string msg) { Console.WriteLine("  info    " + msg); }

	static float F(JsonNode n, float def = 0f)
	{
		try { if (n == null) return def; float v; return float.TryParse(n.ToString().Trim('"'), NumberStyles.Float, Inv, out v) ? v : def; } catch { return def; }
	}
	static string S(JsonNode n) { try { return n == null ? null : n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToString(); } catch { return null; } }
	static Vector3 V(JsonNode n) => n == null ? Vector3.zero : new Vector3(F(n["x"]), F(n["y"]), F(n["z"]));

	public static int Run(string scenePath, string vamDir)
	{
		problems = 0;
		var sw = Stopwatch.StartNew();
		Console.WriteLine("== scene " + Path.GetFileName(scenePath));
		string text;
		JsonNode root;
		try { text = File.ReadAllText(scenePath); root = JsonNode.Parse(text); }
		catch (Exception e) { Bad("scene is not readable JSON: " + e.Message); return 1; }
		var atoms = (root?["atoms"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
		Info(atoms.Count + " atoms: " + string.Join(", ", atoms.GroupBy(a => S(a["type"])).Select(g => g.Key + " x" + g.Count())));

		ResolvePackages(text, vamDir);
		BuildWorldAndRun(atoms, root);
		Console.WriteLine(problems == 0 ? "PASS scene (" + sw.ElapsedMilliseconds + " ms)" : "FAIL scene: " + problems + " problem(s) (" + sw.ElapsedMilliseconds + " ms)");
		return problems == 0 ? 0 : 1;
	}

	// ------------------------------------------------------------------ packages

	static Dictionary<string, string> indexCache;
	static Dictionary<string, string> PackageIndex(string vamDir)
	{
		if (indexCache != null) return indexCache;
		indexCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var sub in new[] { "AddonPackages", "AllPackages" })
		{
			string dir = Path.Combine(vamDir, sub);
			if (!Directory.Exists(dir)) continue;
			foreach (var f in Directory.EnumerateFiles(dir, "*.var", SearchOption.AllDirectories))
			{
				string n = Path.GetFileNameWithoutExtension(f);
				if (!indexCache.ContainsKey(n)) indexCache[n] = f;
			}
		}
		return indexCache;
	}

	// packages that exist only as a switched-off archive (Name.N.DISABLED / Name.N.var.disabled): VPB registers them when VaM is asked for a path inside
	static Dictionary<string, string> offCache;
	static Dictionary<string, string> SwitchedOffIndex(string vamDir)
	{
		if (offCache != null) return offCache;
		offCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var on = PackageIndex(vamDir);
		foreach (var sub in new[] { "AddonPackages", "AllPackages" })
		{
			string dir = Path.Combine(vamDir, sub);
			if (!Directory.Exists(dir)) continue;
			foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
			{
				string n = Path.GetFileName(f), low = n.ToLowerInvariant();
				if (low.EndsWith(".par2")) continue;
				string uid = low.EndsWith(".var.disabled") ? n.Substring(0, n.Length - 13) : low.EndsWith(".disabled") ? n.Substring(0, n.Length - 9) : null;
				if (uid != null && !on.ContainsKey(uid) && !offCache.ContainsKey(uid)) offCache[uid] = f;
			}
		}
		return offCache;
	}

	static void ResolvePackages(string text, string vamDir)
	{
		var refs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		foreach (Match m in Regex.Matches(text, "\"([A-Za-z0-9_\\-]+\\.[^\"\\\\/:]+?\\.(?:\\d+|latest|min\\d+)):/([^\"]+)\""))
		{
			HashSet<string> set;
			if (!refs.TryGetValue(m.Groups[1].Value, out set)) refs[m.Groups[1].Value] = set = new HashSet<string>(StringComparer.Ordinal);
			set.Add(m.Groups[2].Value);
		}
		var idx = PackageIndex(vamDir);
		Info(refs.Count + " packages referenced, " + idx.Count + " installed");
		int missingPk = 0, missingFiles = 0, caseBad = 0, filesChecked = 0, switchedOff = 0;
		var off = SwitchedOffIndex(vamDir);
		foreach (var kv in refs.OrderBy(k => k.Key))
		{
			string pkg = kv.Key, file;
			int dot = pkg.LastIndexOf('.');
			string stem = pkg.Substring(0, dot), ver = pkg.Substring(dot + 1);
			if (ver == "latest" || ver.StartsWith("min"))
			{
				int minv = ver.StartsWith("min") ? int.Parse(ver.Substring(3)) : 0;
				var best = idx.Keys.Where(k => k.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(stem.Length + 1), out int nv) && nv >= minv)
					.OrderByDescending(k => int.Parse(k.Substring(stem.Length + 1))).FirstOrDefault();
				if (best != null) file = idx[best];
				else
				{
					var offBest = off.Keys.Where(k => k.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(stem.Length + 1), out int nv) && nv >= minv)
						.OrderByDescending(k => int.Parse(k.Substring(stem.Length + 1))).FirstOrDefault();
					if (offBest == null) { missingPk++; Bad("package missing: " + pkg); continue; }
					switchedOff++; Info("package switched off (.DISABLED), VPB brings it in on demand: " + pkg + " -> " + offBest);
					file = off[offBest];
				}
			}
			else if (!idx.TryGetValue(pkg, out file) && off.TryGetValue(pkg, out file))
			{
				switchedOff++; Info("package switched off (.DISABLED), VPB brings it in on demand: " + pkg);
			}
			else if (file == null)
			{
				var newer = idx.Keys.Where(k => k.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
				missingPk++;
				Bad("package missing: " + pkg + (newer.Count > 0 ? "  (installed: " + string.Join(", ", newer.Select(k => k.Substring(stem.Length + 1))) + ")" : ""));
				continue;
			}
			try
			{
				using (var zip = ZipFile.OpenRead(file))
				{
					var exact = new HashSet<string>(zip.Entries.Select(e => e.FullName), StringComparer.Ordinal);
					Dictionary<string, string> lower = null;
					foreach (var path in kv.Value)
					{
						filesChecked++;
						if (exact.Contains(path)) continue;
						lower = lower ?? zip.Entries.GroupBy(e => e.FullName.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().FullName);
						string real;
						if (lower.TryGetValue(path.ToLowerInvariant(), out real)) { caseBad++; Bad("case mismatch in " + pkg + ": scene asks '" + path + "' package has '" + real + "' (VaM is case-sensitive)"); }
						else { missingFiles++; if (missingFiles <= 12) Bad("file not in " + pkg + ": " + path); }
					}
				}
			}
			catch (Exception e) { Bad("cannot read " + Path.GetFileName(file) + ": " + e.Message); }
		}
		string offNote = switchedOff > 0 ? " (" + switchedOff + " of them switched off: .DISABLED, found through VPB)" : "";
		if (missingPk == 0 && missingFiles == 0 && caseBad == 0) Ok("all " + refs.Count + " packages present" + offNote + ", all " + filesChecked + " referenced files exist");
		else Info("packages missing " + missingPk + ", files missing " + missingFiles + ", case mismatches " + caseBad + offNote);
	}

	// ------------------------------------------------------------------ world

	sealed class Stud
	{
		public string Id, Person, Bone, Link; public bool LinkHold;
		public float Xo, Yo, Zo; public float Xo0, Yo0, Zo0;
		public GameObject Reparent, Controller;
		public Vector3 LinkOffset;
		public Vector3 SavedPos; public Vector3 SavedRot;
		public bool Calibrated; public string CalibNote;
		public bool HasPrevPose; public Vector3 PrevPos; public Quaternion PrevRot;
		public bool HasPrevC; public Vector3 PrevCLP;
	}

	sealed class Bone { public GameObject Go; public Vector3 Base; public Quaternion Rot; public GameObject Ctl; public Vector3 PrevPos; }

	static void BuildWorldAndRun(List<JsonObject> atoms, JsonNode root)
	{
		Sim.AllObjects.Clear(); Sim.Routines.Clear(); Sim.Lifecycle.Pending.Clear(); Sim.Lifecycle.Started.Clear(); Sim.Now = 0f; Sim.FixedAccum = 0f;
		var sc = SimWorld.NewSuperController();
		var people = new Dictionary<string, Atom>();
		foreach (var a in atoms)
		{
			string id = S(a["id"]), type = S(a["type"]);
			try
			{
				if (type == "Person") people[id] = SimWorld.NewPerson(sc, id, S(a["gender"]) == "Male" || (id.Contains("Male") && !id.Contains("Female")), 0);
			}
			catch (Exception e) { Bad("atom " + id + " could not be created: " + e.Message); }
		}
		Info(people.Count + " person atom(s) built with bones/controllers/morph banks");

		// plugin storables of each person (clothing item plugin settings) -> studs
		var studs = new List<Stud>();
		foreach (var a in atoms.Where(x => S(x["type"]) == "CustomUnityAsset" && (S(x["id"]) ?? "").Contains(".CUAClothing")))
		{
			string id = S(a["id"]);
			var st = new Stud { Id = id, Person = S(a["parentAtom"]) };
			var ctl = (a["storables"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(s => S(s["id"]) == "control");
			if (ctl != null)
			{
				st.SavedPos = V(ctl["position"]); st.SavedRot = V(ctl["rotation"]);
				st.Link = S(ctl["linkTo"]); st.LinkHold = S(ctl["positionState"]) == "Hold" || st.Link != null;
			}
			// atom id = Person#<clothing internalId>#plugin#N_<script>
			string[] parts = id.Split('#');
			string pluginKey = parts.Length >= 4 ? "plugin#" + parts[parts.Length - 1] : null;
			var person = atoms.FirstOrDefault(p => S(p["id"]) == st.Person);
			JsonObject cfg = null;
			if (person != null && parts.Length >= 4)
			{
				string internalId = string.Join("#", parts.Skip(1).Take(parts.Length - 3));
				foreach (var s1 in (person["storables"] as JsonArray).OfType<JsonObject>().Where(s => S(s["id"]) == internalId))
					cfg = (s1["storables"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(s => S(s["id"]) == pluginKey);
			}
			if (cfg == null) { Bad("no plugin settings found in the scene for CUA atom " + id); continue; }
			st.Bone = S(cfg["Parent Bone"]);
			st.Xo0 = st.Xo = F(cfg["xOffset"]); st.Yo0 = st.Yo = F(cfg["yOffset"]); st.Zo0 = st.Zo = F(cfg["zOffset"]);
			studs.Add(st);
		}
		Info(studs.Count + " plugin-driven CUA atom(s): " + string.Join(", ", studs.Select(s => (s.Bone ?? "?") + (s.Link != null ? "[link " + s.Link + "]" : ""))));
		if (studs.Count == 0) return;

		foreach (bool useLinks in new[] { true, false })
		{
			Console.WriteLine("  -- run: VaM links " + (useLinks ? "as stored in the scene" : "ignored (plugin only)"));
			Simulate(studs, useLinks);
		}
		RealRun(atoms, people, sc, studs);
	}

	static float Mag(Vector3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
	static Quaternion Mirror(Quaternion q) { var e = q.eulerAngles; return Quaternion.Euler(e.x, -e.y, -e.z); }

	static void Simulate(List<Stud> studs, bool useLinks)
	{
		var bones = CalibrateBones(studs, true);

		// ---- create the CUA objects the way CUAClothingAlt.CreateCUA does for an atom that already exists (scene load)
		foreach (var s in studs)
		{
			var b = bones[s.Person + "/" + s.Bone];
			s.Reparent = new GameObject("reParent " + s.Id); s.Controller = new GameObject("control " + s.Id);
			s.Controller.transform.SetParent(s.Reparent.transform, false);
			s.Controller.transform.position = s.SavedPos; s.Controller.transform.rotation = Quaternion.Euler(s.SavedRot.x, s.SavedRot.y, s.SavedRot.z);
			// SyncLocalPosition()
			s.Controller.transform.localPosition = new Vector3(s.Xo, s.Yo, s.Zo) * 1f;
			s.PrevCLP = s.Controller.transform.localPosition; s.HasPrevC = true;
			// the VaM link is restored with the offset the controller has to its link target at that moment (person not settled yet)
			if (useLinks && s.Link != null && s.LinkHold) s.LinkOffset = s.SavedPos - SettledBase(b, 0f);
		}

		// ---- run 10 s at 60 Hz
		float dt = 1f / 60f;
		float worstDrift = 0f, worstFar = 0f; string worstId = "";
		for (int f = 0; f < 600; f++)
		{
			float t = f * dt;
			foreach (var kv in bones) MoveBone(kv.Value, t);
			foreach (var s in studs)
			{
				var b = bones[s.Person + "/" + s.Bone];
				// nipple controller follows its bone with one physics step of lag
				if (b.Ctl != null) { b.Ctl.transform.position = b.PrevPos; }
				b.PrevPos = b.Go.transform.position;
				// VaM link / Hold: the controller is pulled to (link target + stored offset) every physics step
				if (useLinks && s.Link != null && s.LinkHold && b.Ctl != null) s.Controller.transform.position = b.Ctl.transform.position + s.LinkOffset;
				LateUpdate(s, b);
				var c = s.Controller.transform;
				float far = Mag(c.position - b.Go.transform.position);
				float drift = Mag(new Vector3(s.Xo - s.Xo0, s.Yo - s.Yo0, s.Zo - s.Zo0));
				if (far > worstFar) { worstFar = far; worstId = s.Id; }
				if (drift > worstDrift) worstDrift = drift;
			}
		}
		foreach (var s in studs)
		{
			var b = bones[s.Person + "/" + s.Bone];
			var ideal = b.Go.transform.position + b.Go.transform.rotation * new Vector3(s.Xo0, s.Yo0, s.Zo0);
			float err = Mag(s.Controller.transform.position - ideal);
			string tag = (s.Bone ?? "?") + " stud";
			Console.WriteLine("      " + tag + ": final offset (" + s.Xo.ToString("0.0000", Inv) + "," + s.Yo.ToString("0.0000", Inv) + "," + s.Zo.ToString("0.0000", Inv) + ") from stored (" + s.Xo0.ToString("0.0000", Inv) + "," + s.Yo0.ToString("0.0000", Inv) + "," + s.Zo0.ToString("0.0000", Inv) + "), " + (Mag(s.Controller.transform.position - b.Go.transform.position) * 100f).ToString("0.0", Inv) + " cm from its bone, " + (err * 100f).ToString("0.0", Inv) + " cm from where the stored offsets put it");
		}
		if (worstFar > 0.15f) Bad("a stud ends up " + (worstFar * 100f).ToString("0", Inv) + " cm from its bone (" + ShortId(worstId) + ")");
		else Ok("every stud stays within " + (worstFar * 100f).ToString("0.0", Inv) + " cm of its bone");
		if (worstDrift > 0.005f) Bad("offsets drift by up to " + (worstDrift * 100f).ToString("0.0", Inv) + " cm from what is stored in the scene");
		else Ok("stored offsets stay put (max change " + (worstDrift * 1000f).ToString("0.00", Inv) + " mm)");
		// L/R symmetry of the final world positions around the person's midline (x = 0)
		foreach (var grp in studs.Where(s => s.Bone != null && s.Bone.Length > 1 && (s.Bone[0] == 'l' || s.Bone[0] == 'r')).GroupBy(s => s.Person + "/" + s.Bone.Substring(1)))
		{
			var l = grp.FirstOrDefault(s => s.Bone[0] == 'l'); var r = grp.FirstOrDefault(s => s.Bone[0] == 'r');
			if (l == null || r == null) continue;
			var lp = l.Controller.transform.position; var rp = r.Controller.transform.position;
			var d = new Vector3(lp.x + rp.x, lp.y - rp.y, lp.z - rp.z);
			if (Mag(d) > 0.003f) Bad(grp.Key.Substring(grp.Key.IndexOf('/') + 1) + " L/R studs not symmetric: " + (Mag(d) * 1000f).ToString("0.0", Inv) + " mm apart (mirrored)");
			else Ok(grp.Key.Substring(grp.Key.IndexOf('/') + 1) + " L/R studs symmetric to " + (Mag(d) * 1000f).ToString("0.00", Inv) + " mm");
		}
		foreach (var s in studs) { UnityEngine.Object.Destroy(s.Reparent); UnityEngine.Object.Destroy(s.Controller); }
	}

	static Dictionary<string, Bone> CalibrateBones(List<Stud> studs, bool verbose)
	{
		// ---- bones: calibrate each (person, bone) pose from a stud whose offsets are sane (|offset| within 5x the slider range); the mirror side is derived
		var bones = new Dictionary<string, Bone>();
		foreach (var s in studs) { s.Xo = s.Xo0; s.Yo = s.Yo0; s.Zo = s.Zo0; s.HasPrevPose = false; s.HasPrevC = false; s.Calibrated = false; }
		Func<Stud, bool> sane = s => Math.Abs(s.Xo0) <= 0.5f && Math.Abs(s.Yo0) <= 0.5f && Math.Abs(s.Zo0) <= 0.5f;
		foreach (var s in studs.Where(sane))
		{
			string key = s.Person + "/" + s.Bone;
			if (bones.ContainsKey(key)) continue;
			var rot = Quaternion.Euler(s.SavedRot.x, s.SavedRot.y, s.SavedRot.z);
			var pos = s.SavedPos - rot * new Vector3(s.Xo0, s.Yo0, s.Zo0);
			bones[key] = MakeBone(key, pos, rot);
			s.Calibrated = true;
		}
		// L/R pairs: the bones are mirror images, so the left (reference) bone defines the right one. If the right stud was calibrated on its own, say how far off the mirror it was.
		foreach (var s in studs.Where(x => x.Bone != null && x.Bone.Length > 1 && x.Bone[0] == 'r'))
		{
			string lk = s.Person + "/l" + s.Bone.Substring(1), rk = s.Person + "/" + s.Bone;
			if (bones.ContainsKey(lk) && bones.ContainsKey(rk))
			{
				var lb = bones[lk]; var rb = bones[rk];
				var want = new Vector3(-lb.Base.x, lb.Base.y, lb.Base.z);
				if (verbose) Info(s.Bone + " saved pose vs mirrored " + lb.Go.name.Substring(5).Split('/')[1] + ": " + (Mag(rb.Base - want) * 1000f).ToString("0.0", Inv) + " mm apart (the stored right stud was not where a mirrored left would be)");
				bones[rk] = MakeBone(rk, want, Mirror(lb.Rot));
			}
		}
		foreach (var s in studs)
		{
			string key = s.Person + "/" + s.Bone;
			if (bones.ContainsKey(key)) continue;
			string other = s.Bone != null && s.Bone.Length > 1 ? (s.Bone[0] == 'l' ? "r" + s.Bone.Substring(1) : s.Bone[0] == 'r' ? "l" + s.Bone.Substring(1) : null) : null;
			Bone ob;
			if (other != null && bones.TryGetValue(s.Person + "/" + other, out ob))
			{
				bones[key] = MakeBone(key, new Vector3(-ob.Base.x, ob.Base.y, ob.Base.z), Mirror(ob.Rot));   // the L/R bones of a person are mirror images (saved yaw/roll mirror each other)
				if (verbose) Info("bone " + s.Bone + " derived by mirroring " + other + " (its own saved offsets are not usable)");
			}
			else
			{
				var rot = Quaternion.Euler(s.SavedRot.x, s.SavedRot.y, s.SavedRot.z);
				bones[key] = MakeBone(key, s.SavedPos, rot);
				if (verbose) Info("bone " + s.Bone + " has no calibration source: placed at the stud's saved pose");
			}
		}

		return bones;
	}

	// ------------------------------------------------------------------ the real scripts

	static void RealRun(List<JsonObject> atoms, Dictionary<string, Atom> people, SuperController sc, List<Stud> studs)
	{
		Console.WriteLine("  -- real scripts (compiled from the scene's packages, run on the shim)");
		SceneHost.ScriptDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(SceneSim).Assembly.Location), "..", "..", "..", "..", "in", "scene"));
		SceneHost.Prepare();
		var entries = SceneHost.Collect(atoms);
		Info(entries.Count + " plugin instance(s): " + string.Join(", ", entries.Select(e => e.Key + "=" + e.Package.Split('.').Last() + "/" + System.IO.Path.GetFileNameWithoutExtension(e.Path))));
		var bones = CalibrateBones(studs, false);
		foreach (var s in studs) { s.Xo = s.Xo0; s.Yo = s.Yo0; s.Zo = s.Zo0; }

		var root = new GameObject("SceneAtoms");   // VaM keeps every atom under SceneAtoms; scripts walk up to it
		var byUid = new Dictionary<string, Atom>(people);
		foreach (var pa in people.Values) pa.transform.SetParent(root.transform, false);
		// person extras the scripts look up
		foreach (var kv in people)
		{
			var person = kv.Value;
			var pm = new GameObject("PhysicsModel"); pm.transform.SetParent(person.transform, false);
			var pmc = pm.AddComponent<PhysicsMaterialControl>(); pmc.storeId = "PhysicsModel"; pmc.containingAtom = person; person.storables["PhysicsModel"] = pmc;
			foreach (var b in bones.Where(x => x.Key.StartsWith(kv.Key + "/")))
			{
				b.Value.Go.name = b.Key.Substring(kv.Key.Length + 1);
				b.Value.Go.transform.SetParent(pm.transform, true);
			}
			var rs = new GameObject("rescaleObject"); rs.transform.SetParent(person.transform, false);
			var rsj = rs.AddComponent<JSONStorable>(); rsj.storeId = "rescaleObject"; rsj.containingAtom = person; rsj.RegisterFloat(new JSONStorableFloat("scale", 1f, 0.01f, 10f)); person.storables["rescaleObject"] = rsj;
		}
		// every other atom; plugin-driven CUA atoms get the structure CUAClothingAlt expects (reParentObject > control, an asset loader, AtomControl)
		foreach (var a in atoms)
		{
			string id = S(a["id"]), type = S(a["type"]);
			if (byUid.ContainsKey(id)) continue;
			var go = new GameObject(id); go.transform.SetParent(root.transform, false);
			var atom = go.AddComponent<Atom>(); atom.type = type; atom.uid = id; atom.name = id; atom.containingAtom = atom;
			var ctl = (a["storables"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => S(x["id"]) == "control");
			var rp = new GameObject("reParent"); rp.transform.SetParent(go.transform, false);
			atom.reParentObject = rp.transform;
			var mc = new GameObject("control"); mc.transform.SetParent(rp.transform, false);
			var fc = mc.AddComponent<FreeControllerV3>(); fc.name = "control"; fc.control = mc.transform; fc.containingAtom = atom;
			atom.mainController = fc; atom.freeControllers = new[] { fc };
			if (ctl != null) { mc.transform.position = V(ctl["position"]); mc.transform.rotation = Quaternion.Euler(V(ctl["rotation"]).x, V(ctl["rotation"]).y, V(ctl["rotation"]).z); }
			var ac = new GameObject("AtomControl"); ac.transform.SetParent(go.transform, false);
			var acj = ac.AddComponent<JSONStorable>(); acj.storeId = "AtomControl"; acj.containingAtom = atom; acj.RegisterBool(new JSONStorableBool("collisionEnabled", S(a["collisionEnabled"]) != "false")); atom.storables["AtomControl"] = acj;
			if (id.Contains(".CUAClothing"))
			{
				var loader = rp.AddComponent<CustomUnityAssetLoader>(); loader.storeId = "CUALoader"; loader.containingAtom = atom;
				var assetName = new JSONStorableStringChooser("assetName", new List<string> { "None" }, "None", "Asset");
				var url = new JSONStorableUrl("assetUrl", "");
				url.setCallbackFunction = v => { assetName.choices = new List<string> { "None", "asset" }; };   // a real loader lists the bundle's prefabs; the bundle is not opened here
				loader.RegisterUrl(url); loader.RegisterStringChooser(assetName);
			}
			sc.AddAtom(atom);
			byUid[id] = atom;
		}
		sc.loadingIcon = new GameObject("loadingIcon").transform; sc.loadingIcon.gameObject.SetActive(false);
		sc.loadingUI = new GameObject("loadingUI").transform; sc.loadingUI.gameObject.SetActive(false);
		var holders = new Dictionary<string, GameObject>();
		Func<GameObject, MVRPluginManager> addManager = parentGo =>
		{
			var mg = new GameObject("PluginManager"); mg.transform.SetParent(parentGo.transform, false);
			var m = mg.AddComponent<MVRPluginManager>(); m.storeId = "PluginManager";
			foreach (var f in typeof(MVRPluginManager).GetFields().Where(x => x.FieldType == typeof(Transform) || x.FieldType == typeof(RectTransform)))
				f.SetValue(m, f.FieldType == typeof(RectTransform) ? new GameObject("prefab " + f.Name).AddComponent<RectTransform>() : new GameObject("prefab " + f.Name).transform);
			return m;
		};
		foreach (var kv in byUid.Where(x => x.Value.type == "Person" || x.Value.type == "CoreControl" || x.Value.type == "WindowCamera" || x.Value.type == "CustomUnityAsset" || true).ToList())
		{
			var m = addManager(kv.Value.gameObject); m.containingAtom = kv.Value; kv.Value.storables["PluginManager"] = m;
			holders[kv.Key + "|"] = m.gameObject;
		}
		var clothingGo = new Dictionary<string, GameObject>();
		Func<string, string, int, GameObject> makeClothing = (atomId, id, nMat) =>
		{
			string key = atomId + "|" + id;
			GameObject item;
			if (clothingGo.TryGetValue(key, out item)) return item;
			item = new GameObject(id); item.transform.SetParent(byUid[atomId].gameObject.transform, false);
			var dit = item.AddComponent<DAZClothingItem>(); dit.uid = id; dit.internalUid = id; dit.displayName = id; dit.packageUid = "";
			var dyn = item.AddComponent<MeshVR.DAZDynamic>(); dyn.uid = id;
			var bin = new GameObject("binaryStorableBucket"); bin.transform.SetParent(item.transform, false); dyn.binaryStorableBucket = bin.transform;
			dyn.specificStorables = new MeshVR.PresetManager.SpecificStorable[0];
			var mesh = item.AddComponent<DAZMesh>(); mesh.materials = Enumerable.Range(0, Math.Max(1, nMat)).Select(k => new Material((Shader)null)).ToArray();
			item.AddComponent<DAZSkinWrap>(); item.AddComponent<DAZSkinWrapMaterialOptions>();
			return clothingGo[key] = item;
		};
		foreach (var kv in people)
		{
			var pj = atoms.First(a => S(a["id"]) == kv.Key);
			var stor = (pj["storables"] as JsonArray).OfType<JsonObject>().ToList();
			var geo = stor.FirstOrDefault(x => S(x["id"]) == "geometry");
			var items = new List<DAZClothingItem>();
			foreach (var c in (geo?["clothing"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
			{
				string iid = S(c["internalId"]); if (iid == null) continue;
				int nm = stor.Count(x => (S(x["id"]) ?? "").StartsWith(iid + "Material"));
				items.Add(makeClothing(kv.Key, iid, nm).GetComponent<DAZClothingItem>());
			}
			var sel = kv.Value.GetStorableByID("geometry") as DAZCharacterSelector;
			if (sel != null) sel.clothingItems = items.ToArray();
		}
		Func<string, string, GameObject> holderOf = (atomId, clothing) =>
		{
			string key = atomId + "|" + clothing;
			GameObject h;
			if (holders.TryGetValue(key, out h)) return h;
			var item = makeClothing(atomId, clothing, 1);
			var mgr = addManager(item); mgr.containingAtom = byUid[atomId];
			return holders[key] = mgr.gameObject;
		};

		float t0 = 0f;
		Sim.BeforeFrame = () => { t0 += 0.02f; foreach (var b in bones.Values) MoveBone(b, t0); };
		var results = SceneHost.Start(sc, entries, id => byUid[id], holderOf);
		foreach (var g in results.Where(x => x.Error != null).GroupBy(x => x.E.Package.Split((char)46)[1] + ": " + x.Error)) Bad((g.Count() > 1 ? g.Count() + "x " : "") + g.Key);
		Info(results.Count(r => r.Script != null && r.Error == null) + " of " + results.Count + " plugin instances initialised cleanly");
		for (int f = 0; f < 500; f++) Sim.Step(0.02f);
		var errs = Sim.Errors.Distinct().ToList();
		foreach (var g in errs.GroupBy(e => e.ToString().Split((char)10)[0]).Take(25)) Bad("runtime: " + (g.Count() > 1 ? g.Count() + "x " : "") + g.Key);
		if (errs.Count > 25) Info((errs.Count - 25) + " more runtime errors");
		if (errs.Count == 0) Ok("no exception in 10 simulated seconds");
		foreach (var r in results.Where(x => x.Script != null && x.Script.GetType().Name.StartsWith("CUAClothing")))
		{
			var t = r.Script.GetType();
			Func<string, object> g = n => { var fi = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); return fi == null ? "?" : fi.GetValue(r.Script) ?? "null"; };
			var bc = g("parentBoneChooser") as JSONStorableStringChooser;
			Info("debug " + r.E.Key + " " + t.FullName + ": connected=" + g("connected") + " parentTransform=" + (g("parentTransform") is Transform ? "set" : g("parentTransform").ToString()) + " creating=" + g("_isCreatingCUA") + " cuaAtom=" + (g("cuaAtom") is Atom ? "set" : g("cuaAtom").ToString()) + " bone='" + (bc == null ? "?" : bc.val) + "' choices=" + (bc == null || bc.choices == null ? "?" : bc.choices.Count.ToString()) + " cuaManager=" + (g("cuaManager") is Component ? "set" : g("cuaManager")) + " atomScale=" + (g("atomScale") is JSONStorableFloat ? "set" : g("atomScale")) + " prevPose=" + g("prevParentPose") + " main=" + ((g("cuaAtom") as Atom) != null && ((Atom)g("cuaAtom")).mainController != null ? "ok" : "null"));
		}
		Sim.BeforeFrame = null;
		// the actual CUA atoms: where did the plugin put them?
		foreach (var st in studs)
		{
			Atom cua;
			if (!byUid.TryGetValue(st.Id, out cua) || cua.mainController == null) continue;
			var boneT = bones.ContainsKey(st.Person + "/" + st.Bone) ? bones[st.Person + "/" + st.Bone].Go.transform : null;
			if (boneT == null) continue;
			var rpT = cua.reParentObject;
			Func<Vector3, string> f3 = v => "(" + v.x.ToString("0.000", Inv) + "," + v.y.ToString("0.000", Inv) + "," + v.z.ToString("0.000", Inv) + ")";
			Console.WriteLine("      debug " + st.Bone + ": bone " + f3(boneT.position) + " reParent " + (rpT == null ? "-" : f3(rpT.position)) + " controller " + f3(cua.mainController.transform.position) + " local " + f3(cua.mainController.transform.localPosition));
			float d = Mag(cua.mainController.transform.position - boneT.position);
			Console.WriteLine("      " + st.Bone + " CUA atom: " + (d * 100f).ToString("0.0", Inv) + " cm from its bone after the real plugin ran");
			if (d > 0.15f) Bad(st.Bone + " piercing is " + (d * 100f).ToString("0", Inv) + " cm from its bone");
		}
	}

	static string ShortId(string id) { int i = id.LastIndexOf('#'); return i >= 0 ? id.Substring(i + 1) : id; }

	static Bone MakeBone(string name, Vector3 pos, Quaternion rot)
	{
		var go = new GameObject("bone " + name); go.transform.position = pos; go.transform.rotation = rot;
		var ctl = new GameObject("ctl " + name); ctl.transform.position = pos;
		return new Bone { Go = go, Base = pos, Rot = rot, Ctl = ctl, PrevPos = pos };
	}

	// the person is not in its stored pose yet when the scene loads: it settles over ~1 s, then breathes/jiggles a little
	static Vector3 SettledBase(Bone b, float t) => b.Base + new Vector3(0f, 0.03f, 0.02f) * (float)Math.Exp(-t / 0.3f);
	static void MoveBone(Bone b, float t)
	{
		float w = 2f * (float)Math.PI;
		var sway = new Vector3(0.003f * (float)Math.Sin(w * 1.2f * t), 0.004f * (float)Math.Sin(w * 0.9f * t), 0.002f * (float)Math.Sin(w * 1.7f * t));
		b.Go.transform.position = SettledBase(b, t) + sway;
		b.Go.transform.rotation = b.Rot;
	}

	// ---- CUAClothingAlt.LateUpdate (Skynet.CUAClothingAlt.8), same order and thresholds
	static void LateUpdate(Stud s, Bone b)
	{
		var parent = b.Go.transform;
		var thisPos = parent.position; var thisRot = parent.rotation;
		if (s.HasPrevPose) { s.Reparent.transform.position = s.PrevPos; s.Reparent.transform.rotation = s.PrevRot; }
		else { s.Reparent.transform.position = thisPos; s.Reparent.transform.rotation = thisRot; }
		s.PrevPos = thisPos; s.PrevRot = thisRot; s.HasPrevPose = true;
		var cLP = s.Controller.transform.localPosition;
		float cAS = 1f;   // atomScale = the person's rescale (1)
		if (s.HasPrevC && Mag(cLP - s.PrevCLP) > 0.0001f * cAS)
		{ s.Xo = cLP.x / cAS; s.Yo = cLP.y / cAS; s.Zo = cLP.z / cAS; }   // gizmo moved -> offsets follow (valNoCallback, so the controller is not re-placed)
		s.PrevCLP = cLP; s.HasPrevC = true;
	}
}
