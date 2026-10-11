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
}
