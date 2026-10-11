using System;
using System.Linq;
using UnityEngine;

// One scenario per headless plugin: build a fake scene, start the real plugin DLL, step time, assert.
// Add a method with [Scenario("name", "<plugin dll name without .dll>")] to cover a new plugin.
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
}
