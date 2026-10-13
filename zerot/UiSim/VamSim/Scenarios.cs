using System.IO;
using System.Reflection;
using System.Text;
using System;
using System.Linq;
using UnityEngine;

// One scenario per headless plugin: build a fake scene, start the real plugin DLL, step time, assert.
// Add a method with [Scenario("name", "<plugin dll name without .dll>")] to cover a new plugin.
// stands in for VPB's package index (an internal static class in the real VPB.dll): the plugin finds it by name through reflection
namespace VPB
{
	internal static class VpbLocalDatabase
	{
		public static string Latest, ArchivePath;
		internal static bool TryResolveLatestUidFromIndex(string packageGroup, out string resolvedUid) { resolvedUid = packageGroup == "Datel.ControllersAppearance" ? Latest : null; return resolvedUid != null; }
		internal static bool TryResolveIndexedVarPathForUid(string uid, out string varPath) { varPath = uid == Latest ? ArchivePath : null; return varPath != null; }
	}
}

public static class Scenarios
{
	// Any plugin: one female + one male, ten seconds. Fails on plugin exceptions.
	public static void Smoke(Harness h)
	{
		h.Person("Female1"); h.Person("Male1", true);
		h.Start();
		h.Step(10f);
		h.Check(h.Plugins.Count > 0, "plugin type found and created");
	}

	[Scenario("bodyweight", "ZeroT.BodyWeightStandalone")]
	public static void BodyWeight(Harness h)
	{
		var f = h.Person("Female1"); var m = h.Person("Male1", true);
		h.Start();
		h.Step(6f);
		h.Near(SimWorld.Body(f, "head").mass, 4.7f, "female head = plugin default 4.7");
		h.Near(SimWorld.Body(f, "chest").mass, 10f, "female chest = 10");
		h.Near(SimWorld.Body(f, "hip").mass, 8f, "female hip = 8");
		h.Near(SimWorld.Body(f, "lThigh").mass, 6.4f, "left thigh = 6.4");
		h.Near(SimWorld.Body(f, "rThigh").mass, 6.4f, "right thigh mirrors left");
		h.Near(SimWorld.Body(f, "lHand").mass, 0.2f, "hand = 0.2");
		h.Near(SimWorld.Body(m, "chest").mass, 2f, "male untouched (VaM default 2)");
		h.Set("Masses", "MassMultiplier", "2");
		h.Step(2f);
		h.Near(SimWorld.Body(f, "chest").mass, 20f, "multiplier 2 -> chest 20");
		h.Near(SimWorld.Body(f, "head").mass, 4.7f, "head ignores the multiplier");
		h.Set("Masses", "LeftRightSymmetric", "false");
		h.Set("Masses", "RightThigh", "3");
		h.Step(2f);
		h.Near(SimWorld.Body(f, "rThigh").mass, 6f, "asymmetric right thigh = 3 x 2");
		h.Set("General", "Active", "false");
		h.Step(1f);
		h.Near(SimWorld.Body(f, "chest").mass, 2f, "Active=false restores VaM mass");
		h.Set("General", "Active", "true");
		h.Set("Masses", "MassMultiplier", "1");
		h.Step(3f);
		// a scene load replaces every person
		Atom f2 = null;
		h.Load(() => { f2 = h.Person("Female2"); });
		h.Step(5f);
		h.Near(SimWorld.Body(f2, "chest").mass, 20f * 0.5f, "new scene: applied again to the new person");
	}

	[Scenario("microphysics", "ZeroT.MicroPhysicsFixerStandalone")]
	public static void MicroPhysics(Harness h)
	{
		var f = h.Person("Female1");
		var hand = SimWorld.Body(f, "lHand"); hand.mass = 0.01f; hand.drag = 0f; hand.angularDrag = 0f;
		var chest = SimWorld.Body(f, "chest"); chest.mass = 30f;
		var gen = SimWorld.Body(f, "labia"); gen.mass = 1f; gen.drag = 0.5f; gen.angularDrag = 0.5f;
		h.Start();
		h.Step(6f);
		h.Near(hand.mass, 0.2f, "hand mass clamped up to MinMass 0.2");
		h.Check(hand.drag >= 0.08f - 1e-4f, "hand drag raised to >= 0.08 (" + hand.drag + ")");
		h.Check(chest.mass <= 2f + 1e-4f, "chest mass limited by the stabilizer (" + chest.mass + ")");
		h.Check(gen.mass < 1f, "genital part softened (" + gen.mass + ")");
		h.Check(gen.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic, "genital part uses continuous collision");
		hand.mass = 0.0001f;
		h.Step(2f);
		h.Check(hand.mass >= 0.05f - 1e-4f, "monitor corrects an unstable mass (" + hand.mass + ")");
		h.Set("General", "Active", "false");
		h.Step(1f);
		h.Near(chest.mass, 30f, "Active=false restores the original chest mass");
		h.Near(gen.collisionDetectionMode == CollisionDetectionMode.Discrete ? 1f : 0f, 1f, "genital collision mode restored");
	}

	[Scenario("extraautogenitals", "ZeroT.ExtraAutoGenitalsStandalone")]
	public static void ExtraAutoGenitals(Harness h)
	{
		var f = h.Person("Female1"); var m = h.Person("Male1", true);
		h.Start();
		h.Step(4f);
		var geo = (DAZCharacterSelector)f.GetStorableByID("geometry");
		var morph = geo.morphsControlUI.GetMorphByDisplayName("Labia minora-size");
		var handler = SimWorld.LabiaHandler(f);
		var toy = new GameObject("toy"); var col = toy.AddComponent<Collider>();
		handler.collidingWithDictionary[col] = true;
		var abd = f.freeControllers.First(c => c.name == "abdomen2Control").transform.position;
		float lowest = 0f;
		for (int i = 0; i < 100; i++)
		{
			toy.transform.position = new Vector3(0f, 0f, 0.5f - i * 0.004f) + abd;   // moving toward the abdomen
			h.Step(0.02f);
			lowest = Math.Min(lowest, morph.morphValue);
		}
		h.Check(lowest < -0.1f, "morph moves while something approaches (lowest " + lowest.ToString("0.###") + ")");
		h.Check(lowest >= -0.7f - 1e-3f, "morph stays inside InwardMax 0.7");
		handler.collidingWithDictionary.Clear();
		h.Step(2f);
		h.Check(Math.Abs(morph.morphValue) < 0.05f, "morph relaxes back after contact ends (" + morph.morphValue.ToString("0.###") + ")");
		var maleGeo = (DAZCharacterSelector)m.GetStorableByID("geometry");
		h.Check(maleGeo.morphsControlUI.GetMorphByDisplayName("Labia minora-size").morphValue == 0f, "male untouched");
		h.Set("General", "Active", "false");
		h.Step(0.5f);
		h.Check(morph.morphValue == 0f, "Active=false returns morphs to default");
	}

	[Scenario("personguard", "ZeroT.PersonGuard")]
	public static void PersonGuard(Harness h)
	{
		var f = h.Person("Female1");
		h.Start();
		h.Step(5f);
		var hand = SimWorld.Body(f, "lHand");
		h.Check(f.rigidbodies.All(rb => rb.maxDepenetrationVelocity <= 4f + 1e-4f), "depenetration speed lowered on every body part");
		hand.velocity = new Vector3(100f, 0f, 0f);
		h.Step(0.1f);
		h.Check(hand.velocity.sqrMagnitude <= 40.01f * 40.01f, "limb speed capped at 40 m/s (" + Math.Sqrt(hand.velocity.sqrMagnitude).ToString("0.#") + ")");
		// morph repair
		var geo = (DAZCharacterSelector)f.GetStorableByID("geometry");
		var m5 = geo.morphsControlUI.GetMorphByDisplayName("Morph5");
		var m6 = geo.morphsControlUI.GetMorphByDisplayName("Morph6");
		m5.morphValue = float.NaN; m6.morphValue = 50f;
		h.Step(6f);
		h.Check(m5.morphValue == 0f, "NaN morph reset to 0");
		h.Near(m6.morphValue, 6f, "absurd morph clamped to +/-6");
		// explosion recovery
		var chest = SimWorld.Body(f, "chest");
		chest.position = new Vector3(float.NaN, 0f, 0f);
		h.Step(0.2f);
		h.Check(f.physicsResets.Count >= 1, "NaN body part triggers a physics reset (" + f.physicsResets.Count + ")");
		h.Step(10f);
		h.Check(f.physicsResets.Count <= 3, "recovery gives up after 3 tries instead of looping (" + f.physicsResets.Count + ")");
		// morph lock
		chest.position = Vector3.zero;
		h.Step(65f);   // let the pause expire
		h.Set("Morphs", "LockMorphs", "true");
		h.Step(2f);
		var m7 = geo.morphsControlUI.GetMorphByDisplayName("Morph7");
		m7.morphValue = 0.7f;
		h.Step(2f);
		h.Check(m7.morphValue == 0f, "locked morphs revert a change (" + m7.morphValue + ")");
	}

	[Scenario("controllersappearance", "ZeroT.ControllersAppearanceStandalone")]
	public static void ControllersAppearance(Harness h)
	{
		var f = h.Person("Female1"); var m = h.Person("Male1", true);
		var e = new GameObject("Empty1").AddComponent<Atom>(); e.type = "Empty"; e.uid = e.name = "Empty1"; e.containingAtom = e;
		var ec = new GameObject("control").AddComponent<FreeControllerV3>(); ec.name = "control"; ec.containingAtom = e; ec.transform.SetParent(e.transform, false);
		var erb = new GameObject("rb").AddComponent<Rigidbody>(); erb.transform.SetParent(e.transform, false); ec.followWhenOffRB = erb;
		e.freeControllers = new[] { ec }; e.mainController = ec; h.Sc.AddAtom(e);
		var lineMat = new Material((Shader)null); lineMat.SetFloat("_Alpha", 1f);
		foreach (var a in h.Sc.atoms)
		{
			a.mainController.linkLineMaterial = lineMat;
			foreach (var c in a.freeControllers)
			{
				c.deselectedMeshScale = a.type == "Empty" ? 0.02f : 0.02f; c.unhighlightedScale = 0.25f; c.highlightedScale = 0.25f; c.selectedScale = 1f;
				if (c.followWhenOffRB != null) c.transform.position = c.followWhenOffRB.transform.position + new Vector3(0.2f, 0f, 0f);   // controller away from its body part: a line to draw
			}
		}
		Func<Atom, int> lines = a => a.freeControllers.Count(c => c.followWhenOffRB != null);
		int nF = lines(f), nM = lines(m), nE = lines(e);

		// icon packages on disk: an old enabled .var, a NEWER one that is switched off (.DISABLED) in a sub folder, and junk next to them
		string caRoot = Path.Combine(Path.GetTempPath(), "vamsim_ca_" + Environment.ProcessId);
		if (Directory.Exists(caRoot)) Directory.Delete(caRoot, true);
		Directory.CreateDirectory(Path.Combine(caRoot, "AddonPackages", "keep")); Directory.CreateDirectory(Path.Combine(caRoot, "AddonPackages", "off", "deeper"));
		const string CaEntry = "Custom/Scripts/Datel/ControllersAppearance.assetbundle";
		Action<string, string, byte[]> makeZip = (path, name, bytes) =>
		{
			using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
			{
				var en = zip.CreateEntry("meta.json"); using (var w = new StreamWriter(en.Open())) w.Write("{}");
				var e2 = zip.CreateEntry(name, name.EndsWith("assetbundle") && bytes.Length > 100 ? System.IO.Compression.CompressionLevel.Optimal : System.IO.Compression.CompressionLevel.NoCompression);
				using (var s = e2.Open()) s.Write(bytes, 0, bytes.Length);
			}
		};
		Func<byte, int, byte[]> blob = (b, n) => Enumerable.Range(0, n).Select(i => (byte)(b + i % 7)).ToArray();
		var old4 = blob(1, 300); var new9 = blob(40, 700);
		makeZip(Path.Combine(caRoot, "AddonPackages", "keep", "Datel.ControllersAppearance.4.var"), CaEntry, old4);
		makeZip(Path.Combine(caRoot, "AddonPackages", "off", "deeper", "Datel.ControllersAppearance.9.DISABLED"), CaEntry, new9);
		File.WriteAllText(Path.Combine(caRoot, "AddonPackages", "keep", "Datel.ControllersAppearance.20.var.par2"), "junk");   // not a package
		BepInEx.Paths.GameRootPath = caRoot;

		h.Start();
		h.Step(1f);
		var ctl = f.freeControllers.First(c => c.name == "headControl");
		h.Near(ctl.deselectedMeshScale, 0.01f, "icon scale: 0.02 x 0.5");
		h.Near(ctl.unhighlightedScale, 0.15f, "cube scale: 0.25 x 0.6");
		h.Near(ctl.highlightedScale, 0.15f, "highlighted scale follows");
		h.Near(ctl.selectedScale, 0.5f, "selected scale 0.5");
		h.Near(m.freeControllers.First(c => c.name == "headControl").unhighlightedScale, 0.15f, "male person scaled too");
		h.Near(ec.deselectedMeshScale, 0.01f, "non-person atom scaled too");

		// lines: one batched draw per colour, not one per line
		Func<Graphics.DrawCall[]> frame = () => { Graphics.DrawLog.Clear(); h.Step(0.02f); return Graphics.DrawLog.ToArray(); };
		var draws = frame();
		h.Check(draws.Length == 3, "3 draw calls per frame for " + (nF + nM + nE) + " lines (the original makes one per line) - got " + draws.Length);
		Func<Graphics.DrawCall[], Color, Graphics.DrawCall> byColor = (ds, col) => ds.FirstOrDefault(d => Math.Abs(d.color.r - col.r) < 0.01f && Math.Abs(d.color.g - col.g) < 0.01f && Math.Abs(d.color.b - col.b) < 0.01f);
		var pink = byColor(draws, new Color(1f, 0.451f, 0.761f)); var blue = byColor(draws, new Color(0.251f, 0.502f, 1f)); var other = byColor(draws, new Color(0.4f, 0.702f, 0.851f));
		h.Check(pink != null && pink.indexCount == 2 * nF, "female controllers: pink lines (" + nF + " lines)");
		h.Check(blue != null && blue.indexCount == 2 * nM, "male controllers: blue lines (" + nM + " lines, includes the male parts)");
		h.Check(other != null && other.indexCount == 2 * nE, "other atoms: the original light blue (" + nE + " line)");
		h.Check(draws.All(d => d.layer == 8 && Math.Abs(d.alpha - 0.7f) < 1e-3f), "drawn on layer 8 with alpha 0.7 like the original");
		h.Check(pink != null && blue != null && pink.material != blue.material && pink.material != lineMat, "each colour has its own copy of VaM's line material");

		// a controller that is hidden gets no line
		f.freeControllers.First(c => c.followWhenOffRB != null && c.name != "headControl").hidden = true;
		h.Step(0.6f);
		draws = frame(); pink = byColor(draws, new Color(1f, 0.451f, 0.761f));
		h.Check(pink != null && pink.indexCount == 2 * (nF - 1), "a hidden controller loses its line");
		f.freeControllers.First(c => c.hidden).hidden = false;
		h.Step(0.6f);

		// settings
		h.Set("Lines", "FemaleColor", "#00FF00"); h.Step(0.1f);
		draws = frame();
		h.Check(byColor(draws, new Color(0f, 1f, 0f)) != null && byColor(draws, new Color(1f, 0.451f, 0.761f)) == null, "FemaleColor setting changes the pink live");
		h.Set("Lines", "FemaleColor", "#FF73C2");
		h.Set("Lines", "ColorByGender", "false"); h.Step(0.1f);
		draws = frame();
		h.Check(draws.Length == 1 && draws[0].indexCount == 2 * (nF + nM + nE), "ColorByGender off: every line in OtherColor in one batch");
		h.Set("Lines", "ColorByGender", "true");
		h.Set("Lines", "Alpha", "0"); h.Step(0.1f);
		h.Check(frame().Length == 0, "Alpha 0 skips drawing");
		h.Set("Lines", "Alpha", "0.7");
		h.Set("Scale", "IconScale", "1"); h.Step(0.6f);
		h.Near(ctl.deselectedMeshScale, 0.02f, "IconScale setting re-applies to existing controllers");
		h.Set("Scale", "IconScale", "0.5"); h.Step(0.6f);

		// a person added later gets everything
		var late = h.Person("Female2");
		foreach (var c in late.freeControllers) if (c.followWhenOffRB != null) c.transform.position = c.followWhenOffRB.transform.position + new Vector3(0.2f, 0f, 0f);
		late.mainController.linkLineMaterial = lineMat;
		h.Step(1f);
		h.Near(late.freeControllers.First(c => c.name == "headControl").unhighlightedScale, 0.15f, "person added later is scaled");
		draws = frame(); pink = byColor(draws, new Color(1f, 0.451f, 0.761f));
		h.Check(pink != null && pink.indexCount == 2 * (nF + lines(late)), "...and gets pink lines too");

		// the embedded icon bundle is the original's
		var res = h.Asm.GetManifestResourceStream("ControllersAppearance.assetbundle");
		h.Check(res != null && res.Length > 10000, "icon assetbundle is inside the DLL (" + (res == null ? 0 : res.Length) + " bytes)");
		// icons from a package, enabled or switched off
		Func<byte[], byte[], bool> same = (x, y) => x != null && y != null && x.Length == y.Length && x.SequenceEqual(y);
		h.Check(same(AssetBundle.SimLastBytes, new9), "Source = Auto: the newest Datel.ControllersAppearance package wins, even switched off (.DISABLED, v9 over the enabled v4); " + (AssetBundle.SimLastBytes == null ? 0 : AssetBundle.SimLastBytes.Length) + " bytes");
		h.Check(h.LogHas("Icon assetbundle read from") && h.LogHas("AddonPackages"), "...found by scanning AddonPackages when VPB is not there, and says so");
		string idxDir = Path.Combine(caRoot, "elsewhere"); Directory.CreateDirectory(idxDir);
		var new12 = blob(90, 520);
		makeZip(Path.Combine(idxDir, "Datel.ControllersAppearance.12.DISABLED"), CaEntry, new12);
		VPB.VpbLocalDatabase.Latest = "Datel.ControllersAppearance.12"; VPB.VpbLocalDatabase.ArchivePath = Path.Combine(idxDir, "Datel.ControllersAppearance.12.DISABLED");
		h.Set("Icons", "Source", "Embedded"); h.Step(1.5f);
		h.Check(AssetBundle.SimLastBytes != null && AssetBundle.SimLastBytes.Length > 10000, "Source = Embedded: the copy inside the DLL again");
		h.Set("Icons", "Source", "Auto"); h.Step(1.5f);
		h.Check(same(AssetBundle.SimLastBytes, new12), "VPB's index answers (latest uid, then its file): that archive is read, v12 from outside AddonPackages");
		VPB.VpbLocalDatabase.Latest = null;
		h.Set("Icons", "Package", "Nobody.NoSuchPackage"); h.Step(1.5f);
		h.Check(AssetBundle.SimLastBytes != null && AssetBundle.SimLastBytes.Length > 10000, "no such package: the embedded bundle");
		makeZip(Path.Combine(caRoot, "AddonPackages", "keep", "Empty.Pack.1.var"), CaEntry, new byte[0]);
		h.Set("Icons", "Package", "Empty.Pack"); h.Step(1.5f);
		h.Check(AssetBundle.SimLastBytes != null && AssetBundle.SimLastBytes.Length > 10000 && h.LogHas("could not be opened; using the one inside this DLL"), "a package bundle that will not open: the embedded one, with a log line");
		h.Set("Icons", "Package", "Datel.ControllersAppearance"); h.Step(1.5f);


		// off switch restores VaM's values
		h.Set("General", "Active", "false"); h.Step(0.6f);
		h.Near(ctl.unhighlightedScale, 0.25f, "Active off: VaM's own scale back");
		h.Near(ctl.selectedScale, 1f, "Active off: selected scale back");
		h.Check(frame().Length == 0, "Active off: no lines");
		h.Set("General", "Active", "true"); h.Step(1f);
		h.Near(ctl.unhighlightedScale, 0.15f, "Active on again: applied again");

		// Datel's own plugin appears in the session: this one steps aside
		h.LoadTarget("ControllersAppearanceScripts");
		h.Step(2.5f);
		h.Near(ctl.unhighlightedScale, 0.25f, "original plugin present: standalone restores VaM's values and steps aside");
		h.Check(frame().Length == 0, "original plugin present: no duplicate lines");
		h.Check(h.LogHas("steps aside"), "...and says why in the log");
	}

	public static int FlushCalls;
	public static bool CountFlush() { FlushCalls++; return true; }

	[Scenario("clothingbackface", "ZeroT.ClothingBackfaceStandalone")]
	public static void ClothingBackface(Harness h)
	{
		const string Src = "Custom/Subsurface/TransparentGlossNMNoCullSeparateAlphaComputeBuff";
		const string BackName = "Nial/VaM/Clothing Backface Native TankTop";
		var back = new Shader { name = BackName };
		var src = new Shader { name = Src };
		var other = new Shader { name = "Custom/Subsurface/GlossNM" };
		AssetBundle.SimContents.Clear();
		AssetBundle.SimContents["assets/nialclothingbackface/nialclothingbackface.shader"] = back;
		var harm = new HarmonyLib.Harmony("test.backface");
		harm.Patch(typeof(DAZSkinWrap).GetMethod("FlushBuffers"), new HarmonyLib.HarmonyMethod(typeof(Scenarios).GetMethod("CountFlush", BindingFlags.Public | BindingFlags.Static)));

		Func<Shader, int, bool, Material> mat = (sh, queue, specNull) =>
		{
			var m = new Material(sh); m.renderQueue = queue; m.SimProperties.Add("_SpecTex");
			if (!specNull) m.SetTexture("_SpecTex", new Texture2D(2, 2));
			return m;
		};
		Func<Atom, string, string, Material[], bool, DAZSkinWrap> addCloth = (person, goName, display, mats, active) =>
		{
			var go = new GameObject(goName); go.transform.SetParent(person.transform, false);
			var item = go.AddComponent<DAZClothingItem>(); item.containingAtom = person; item.displayName = display;
			var wg = new GameObject("wrap"); wg.transform.SetParent(go.transform, false);
			var wrap = wg.AddComponent<DAZSkinWrap>(); wrap.GPUuseSimpleMaterial = false; wrap.GPUmaterials = mats;
			go.SetActive(active);
			return wrap;
		};

		var f = h.Person("Female1"); var m1 = h.Person("Male1", true);
		var mA = mat(src, 2450, true); var mB = mat(src, 2460, false); var mC = mat(other, 2000, true); var mD = mat(src, 2470, true);
		var wrapDress = addCloth(f, "DressGO", "Dress", new[] { mA, mB, mC, mD }, true);
		wrapDress.materialsEnabled = new[] { true, true, true, false };           // slot 4 is switched off
		var mS = mat(src, 2500, true);
		var wrapSimple = addCloth(f, "TopGO", "Top", new Material[0], true); wrapSimple.GPUuseSimpleMaterial = true; wrapSimple.GPUsimpleMaterial = mS;
		var mHidden = mat(src, 2400, true); addCloth(f, "HiddenGO", "Hidden", new[] { mHidden }, false);
		var mMale = mat(src, 2300, true); addCloth(m1, "ShirtGO", "Shirt", new[] { mMale }, true);
		var mNull = mat(src, 2100, true); var wrapLazy = addCloth(f, "LazyGO", "Lazy", null, true);   // materials not built yet: the plugin asks the wrap to build them
		FlushCalls = 0;
		Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
		var savedFile = Path.Combine(BepInEx.Paths.ConfigPath, "ZeroT.ClothingBackface.profiles.txt");
		if (File.Exists(savedFile)) File.Delete(savedFile);

		// ---- the shader bundle fails to load first: 5 attempts, 1.5 s apart, then it stops
		AssetBundle.SimFailLoad = true; AssetBundle.SimLoadFromMemoryCalls = 0;
		h.Start();
		h.Step(0.3f);
		h.Check(AssetBundle.SimLoadFromMemoryCalls == 1, "shader load: first attempt at once");
		h.Step(1.5f); h.Check(AssetBundle.SimLoadFromMemoryCalls == 2, "...second after 1.5 s");
		h.Step(6.5f); h.Check(AssetBundle.SimLoadFromMemoryCalls == 5, "...five attempts in all (" + AssetBundle.SimLoadFromMemoryCalls + ")");
		h.Step(5f); h.Check(AssetBundle.SimLoadFromMemoryCalls == 5 && h.LogHas("Failed after 5 attempts"), "...then it gives up and says so");
		h.Check(mA.shader == src && mS.shader == src, "nothing is touched while the shader is missing");
		AssetBundle.SimFailLoad = false;
		var host = f.GetStorableByID("ZeroT.ClothingBackface");
		h.Check(host != null && m1.GetStorableByID("ZeroT.ClothingBackface") != null, "the controls are on every Person atom (female and male)");
		host.CallAction("Apply"); h.Step(0.3f);
		h.Check(mA.shader == back, "Apply starts the shader load over (attempts reset) and it works");

		// ---- what gets patched
		h.Check(mA.shader == back && mB.shader == back, "materials using VaM's no-cull clothing shader now use the backface shader");
		h.Check(mS.shader == back, "a wrap on the simple material is patched too");
		h.Check(mC.shader == other, "a material on any other shader is left alone");
		h.Check(mD.shader == src, "a material slot switched off in the wrap is left alone");
		h.Check(mHidden.shader == src, "a clothing item that is not active is left alone");
		h.Check(mMale.shader == back, "male Person atoms work the same");
		h.Check(mA.renderQueue == 2450 && mB.renderQueue == 2460 && mS.renderQueue == 2500, "render queues are kept");
		h.Near(mA.GetFloat("_BackBrightness"), 0.28f, "_BackBrightness starts at the original's default 0.28");
		h.Check(mA.GetTexture("_SpecTex") == Texture2D.blackTexture, "no specular map -> a black one is put in");
		h.Check(mB.GetTexture("_SpecTex") != null && mB.GetTexture("_SpecTex") != Texture2D.blackTexture, "a specular map that exists is not replaced");
		h.Check(FlushCalls >= 3, "the wraps are told to rebuild their buffers (" + FlushCalls + " flushes)");

		// ---- controls and the profile string
		var tDress = host.GetBoolJSONParam("Dress"); var sDress = host.GetFloatJSONParam("Dress Brightness");
		h.Check(tDress != null && sDress != null && tDress.val && Math.Abs(sDress.val - 0.28f) < 1e-4f, "per clothing item: a toggle 'Dress' and a slider 'Dress Brightness'");
		h.Check(host.GetBoolJSONParam("Top") != null && host.GetBoolJSONParam("Hidden") == null, "...for active items only");
		var prof = host.GetStringJSONParam("ClothingBackface.Profiles").val;
		string DecodeFirst(string p) { var e = p.Split(';')[0].Split(':'); return Encoding.UTF8.GetString(Convert.FromBase64String(e[0])) + ":" + e[1] + ":" + e[2]; }
		h.Check(prof.Split(';').Length == 3 && prof.Split(';').Select(x => Encoding.UTF8.GetString(Convert.FromBase64String(x.Split(':')[0]))).OrderBy(x => x).SequenceEqual(new[] { "DressGO", "LazyGO", "TopGO" }), "the original's profile string: base64(key):enabled:brightness x 10000 - one entry per active item (" + string.Join(",", prof.Split(';').Select(x => Encoding.UTF8.GetString(Convert.FromBase64String(x.Split(':')[0])))) + ")");
		h.Check(prof.Split(';').Any(x => x.EndsWith(":1:2800")), "...with 0.28 saved as 2800");

		// ---- brightness slider, toggle
		sDress.val = 0.6f;
		h.Near(mA.GetFloat("_BackBrightness"), 0.6f, "slider: that clothing's materials follow at once");
		h.Near(mS.GetFloat("_BackBrightness"), 0.28f, "...other clothing is untouched");
		h.Check(host.GetStringJSONParam("ClothingBackface.Profiles").val.Split(';').Any(x => x.EndsWith(":1:6000")), "...and the profile string says 6000");
		sDress.val = 7f; h.Near(mA.GetFloat("_BackBrightness"), 1f, "brightness is limited to 0 - 1");
		sDress.val = 0.6f;
		tDress.val = false;
		h.Check(mA.shader == src && mA.renderQueue == 2450 && mA.GetTexture("_SpecTex") == null && mB.shader == src, "toggle off: that item goes back to VaM's shader, queue and the black spec map is removed again");
		h.Check(mB.GetTexture("_SpecTex") != null && mS.shader == back, "...the specular map that was there stays; other items keep the effect");
		tDress.val = true;
		h.Check(mA.shader == back && Math.Abs(mA.GetFloat("_BackBrightness") - 0.6f) < 1e-4f, "toggle on: patched again with the saved brightness");

		// ---- Restore / Apply
		host.CallAction("Restore");
		h.Check(mA.shader == src && mS.shader == src && mMale.shader == back, "Restore: this person back to VaM's shaders (the other person keeps its effect)");
		var mNew = mat(src, 2600, true); addCloth(f, "NewGO", "New", new[] { mNew }, true); h.Step(2f);
		h.Check(mNew.shader == src, "...and a new item is not patched while in manual restore mode");
		host.CallAction("Apply"); h.Step(0.3f);
		h.Check(mA.shader == back && mNew.shader == back, "Apply: the effect is back on everything, new items included");

		// ---- scan: changes are noticed
		var mLate = mat(src, 2700, true); var wLate = addCloth(f, "LateGO", "Late", new[] { mLate }, true);
		h.Step(1.2f);
		h.Check(mLate.shader == back && f.GetStorableByID("ZeroT.ClothingBackface").GetBoolJSONParam("Late") != null, "a clothing item added later is patched within a second and gets its controls");
		wLate.gameObject.transform.parent.gameObject.SetActive(false); h.Step(1.2f);
		h.Check(mLate.shader == src && host.GetBoolJSONParam("Late") == null, "an item switched off is restored and its controls go");
		wLate.gameObject.transform.parent.gameObject.SetActive(true); h.Step(1.2f);
		h.Check(mLate.shader == back, "...and back on again");
		var lateMat2 = mat(src, 2710, true); wLate.GPUmaterials = new[] { lateMat2 }; h.Step(1.2f);
		h.Check(lateMat2.shader == back && mLate.shader == src, "a wrap that swaps its materials is followed");
		UnityEngine.Object.Destroy(wLate.gameObject.transform.parent.gameObject); h.Step(1.2f);
		h.Check(host.GetBoolJSONParam("Late") == null, "an item that disappears loses its controls");

		// ---- duplicate names
		var d1 = mat(src, 2800, true); var d2 = mat(src, 2801, true);
		addCloth(f, "Twin", "Twin", new[] { d1 }, true); addCloth(f, "Twin", "Twin", new[] { d2 }, true); h.Step(1.2f);
		var t1 = host.GetBoolJSONParam("Twin (1)"); var t2 = host.GetBoolJSONParam("Twin (2)");
		h.Check(t1 != null && t2 != null, "two items with the same name: numbered 'Twin (1)', 'Twin (2)'");
		host.GetFloatJSONParam("Twin (2) Brightness").val = 0.9f;
		h.Check(Math.Abs(d2.GetFloat("_BackBrightness") - 0.9f) < 1e-4f && Math.Abs(d1.GetFloat("_BackBrightness") - 0.28f) < 1e-4f, "...and each has its own profile");

		// ---- profiles: stored per person, base-name fallback, old JSON form
		h.Step(1.5f);
		var file = Path.Combine(BepInEx.Paths.ConfigPath, "ZeroT.ClothingBackface.profiles.txt");
		h.Check(File.Exists(file) && File.ReadAllText(file).Contains("Female1\t"), "profiles are written to ZeroT.ClothingBackface.profiles.txt (one line per person)");
		string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
		host.GetStringJSONParam("ClothingBackface.Profiles").val = B64("Dress") + ":1:5000;" + B64("TopGO") + ":0:2000";   // 'Dress' is the display name, not the path key
		h.Step(1.2f);
		h.Near(mA.GetFloat("_BackBrightness"), 0.5f, "an older profile saved under the item's plain name is still found (base-name fallback)");
		h.Check(mS.shader == src, "a profile with enabled=0 keeps that item off");
		host.GetStringJSONParam("ClothingBackface.Profiles").val = "{\"profiles\":[{\"id\":\"DressGO\",\"enabled\":false,\"brightness\":0.9},{\"id\":\"TopGO\",\"enabled\":true,\"brightness\":0.7}]}";
		h.Step(1.2f);
		h.Check(mA.shader == src && mS.shader == back && Math.Abs(mS.GetFloat("_BackBrightness") - 0.7f) < 1e-4f, "the old JSON profile form is read too");
		host.GetStringJSONParam("ClothingBackface.Profiles").val = "";
		h.Step(1.2f);
		h.Check(mA.shader == back && Math.Abs(mA.GetFloat("_BackBrightness") - 0.28f) < 1e-4f, "empty profiles -> every item back to the defaults");

		// ---- defaults in the settings
		h.Set("Defaults", "Brightness", "0.5");
		var mDef = mat(src, 2900, true); addCloth(f, "DefGO", "Def", new[] { mDef }, true); h.Step(1.2f);
		h.Near(mDef.GetFloat("_BackBrightness"), 0.5f, "Defaults.Brightness applies to items that have no profile yet");
		h.Set("Defaults", "Brightness", "0.28");

		// ---- settings
		h.Set("General", "IncludeMales", "false"); h.Step(1.5f);
		h.Check(mMale.shader == src && m1.GetStorableByID("ZeroT.ClothingBackface") == null && mA.shader == back, "IncludeMales=false: male persons are restored and left alone");
		h.Set("General", "IncludeMales", "true"); h.Step(1.5f);
		h.Check(mMale.shader == back, "...and back when switched on");
		h.Set("General", "Active", "false"); h.Step(1.5f);
		h.Check(mA.shader == src && mS.shader == src && mMale.shader == src && mDef.shader == src && f.GetStorableByID("ZeroT.ClothingBackface") == null, "Active=false: every material restored and the controls removed");
		h.Set("General", "Active", "true"); h.Step(2f);
		h.Check(mA.shader == back && mMale.shader == back && f.GetStorableByID("ZeroT.ClothingBackface") != null, "Active=true: everything comes back");

		// ---- Nial's own plugin on a person: this one steps aside there
		var asm = h.LoadTarget("ClothingBackfaceScripts");
		var ot = asm.GetType("ClothingBackface", true);
		m1.gameObject.AddComponent(ot);
		h.Step(3.5f);
		h.Check(mMale.shader == src && m1.GetStorableByID("ZeroT.ClothingBackface") == null && h.LogHas("steps aside"), "Nial's plugin on a person: this one restores it and steps aside there");
		h.Check(mA.shader == back, "...while the other person keeps the effect");

		// ---- scene load: new atoms, the saved profiles come back
		h.Load(() => { f = h.Person("Female1"); });
		var mL = mat(src, 3000, true); addCloth(f, "DressGO", "Dress", new[] { mL }, true);
		h.Step(3f);
		h.Check(mL.shader == back, "after a scene load the new person is patched");
		h.Check(f.GetStorableByID("ZeroT.ClothingBackface") != null, "...with its controls");
	}

	[Scenario("forcepersoncache", "ZeroT.ForcePersonCacheStandalone")]
	public static void ForcePersonCache(Harness h)
	{
		var sc = h.Sc;
		var core = new GameObject("CoreControl").AddComponent<Atom>(); core.type = "SessionPluginManager"; core.uid = core.name = "CoreControl"; core.containingAtom = core; sc.AddAtom(core);
		h.Start();
		h.Step(0.5f);
		h.Check(sc.SimAddLog.Count == 0, "nothing happens straight away (a delay after VaM is ready)");
		h.Step(1.0f);
		h.Check(sc.SimAddLog.Count >= 1 && sc.SimAddLog[0] == "add Person Person", "scene without a person: AddAtomByType(\"Person\", \"Person\") is called");
		h.Check(sc.GetAtomByUid("Person") == null || sc.SimAddLog.Count == 1, "...the atom does not exist at the instant of the call (it takes a few frames)");
		h.Step(0.5f);
		h.Check(sc.SimAddLog.SequenceEqual(new[] { "add Person Person", "remove Person" }), "...and when it exists it is removed again: " + string.Join(" > ", sc.SimAddLog));
		h.Check(sc.GetAtoms().All(a => a.type != "Person"), "the scene is left as it was (no Person)");
		h.Step(5f);
		h.Check(sc.SimAddLog.Count == 2, "it runs once, not again and again");

		// Hard Reset: the session plugin used to start again
		sc.SimAddLog.Clear();
		sc.HardReset();
		h.Step(0.4f); h.Check(sc.SimAddLog.Count == 0, "Hard Reset: waits the delay first");
		h.Step(1.2f); h.Step(0.5f);
		h.Check(sc.SimAddLog.SequenceEqual(new[] { "add Person Person", "remove Person" }), "Hard Reset: warms up again");

		// a person (or an atom called Person) already there: nothing to do
		sc.SimAddLog.Clear();
		sc.HardReset(); h.Step(0.3f);
		var existing = h.Person("Female1");               // category People
		h.Step(2.5f);
		h.Check(sc.SimAddLog.Count == 0, "a Person atom (category People) is in the scene: nothing is created");
		sc.RemoveAtom(existing); sc.SimAddLog.Clear();
		sc.HardReset(); h.Step(0.3f);
		var lookalike = new GameObject("Person").AddComponent<Atom>(); lookalike.type = "Empty"; lookalike.uid = lookalike.name = "Person"; lookalike.containingAtom = lookalike; sc.AddAtom(lookalike);
		h.Step(2.5f);
		h.Check(sc.SimAddLog.Count == 0, "an atom named 'Person' (any type) counts too, like the .var");
		sc.RemoveAtom(lookalike);

		// a call that does not produce an atom
		sc.SimAddLog.Clear(); sc.SimAddFails = true;
		sc.HardReset(); h.Step(2.5f);
		h.Check(sc.SimAddLog.Count == 1 && sc.GetAtoms().Count == 1, "AddAtomByType that yields no atom: no exception, the scene is untouched");
		sc.SimAddFails = false;

		// no overlapping runs
		sc.SimAddLog.Clear();
		sc.HardReset(); h.Step(1.1f);                  // run starts
		sc.HardReset(); h.Step(2.5f);                  // second reset while it is busy / after
		int adds = sc.SimAddLog.Count(x => x.StartsWith("add"));
		h.Check(adds >= 1 && adds <= 2 && sc.SimAddLog.Count(x => x.StartsWith("add")) == sc.SimAddLog.Count(x => x.StartsWith("remove")), "two resets in a row never leave a Person behind (" + string.Join(" > ", sc.SimAddLog) + ")");
		h.Check(sc.GetAtoms().All(a => a.type != "Person"), "...and never leave a Person behind");

		// settings
		sc.SimAddLog.Clear();
		h.Set("General", "RunAfterHardReset", "false");
		sc.HardReset(); h.Step(3f);
		h.Check(sc.SimAddLog.Count == 0, "RunAfterHardReset=false: a Hard Reset does not start it");
		h.Set("General", "RunAfterHardReset", "true");
		h.Set("General", "Enabled", "false");
		sc.HardReset(); h.Step(3f);
		h.Check(sc.SimAddLog.Count == 0, "Enabled=false: nothing at all");
		h.Set("General", "Enabled", "true");

		// the .var itself loaded in the session: this one steps aside
		var asm = h.LoadTarget("ForcePersonCacheScripts");
		h.Check(asm.GetType("JayJayWon.ForcePersonCache") != null, "the original plugin's class is loaded");
		h.Step(2.5f);
		sc.SimAddLog.Clear();
		sc.HardReset(); h.Step(3f);
		h.Check(sc.SimAddLog.Count == 0 && h.LogHas("steps aside"), "with the .var loaded this one does nothing and says so");
	}
}
