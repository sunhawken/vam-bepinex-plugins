using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// The REAL DiviningRod 5 and the REAL ForceForge 49 (its v8 script) on a male, built from the user's .var files by `vamdev.py simtargets`.
// The rod picks its own target as it always does (nearest allowed spot in front of the penis tip); the plugin has to put ForceForge
// on the same person and body part, and keep it there as the rod re-targets.
public static class ForceRodSyncScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	// the extra rigidbodies the two scripts look up by name (VaM persons have them; the simulator's person only has the body)
	private static Rigidbody Add(Atom a, string name, Vector3 pos, bool trigger)
	{
		var go = new GameObject(name); go.transform.SetParent(a.transform, false); go.transform.position = pos;
		var rb = go.AddComponent<Rigidbody>(); go.AddComponent<Collider>();
		if (trigger) go.AddComponent<CollisionTrigger>();
		var l = a.rigidbodies.ToList(); l.Add(rb); a.rigidbodies = l.ToArray();
		return rb;
	}

	private static void Dress(Atom a, Vector3 at, bool female)
	{
		foreach (var rb in a.rigidbodies) rb.transform.position = at;
		Add(a, "lCollar", at + new Vector3(0.1f, 1.4f, 0f), false); Add(a, "rCollar", at + new Vector3(-0.1f, 1.4f, 0f), false);
		Add(a, "LipTrigger", at + new Vector3(0f, 1.5f, 0.1f), false);
		Add(a, "MouthTrigger", at + new Vector3(0f, 1.5f, 0.1f), true);
		Add(a, "VaginaTrigger", at + new Vector3(0f, 0.9f, 0.1f), true);   // ForceForge looks it up on its own person too
		if (female) Add(a, "LabiaTrigger", at + new Vector3(0f, 0.9f, 0.1f), false);
	}

	private static MVRScript Attach(Harness h, Assembly asm, string typeName, Atom a)
	{
		Type t = null;
		try { t = asm.GetTypes().FirstOrDefault(x => x.FullName == typeName); }
		catch (ReflectionTypeLoadException ex) { throw new Exception(string.Join(" | ", ex.LoaderExceptions.Select(x => x.Message).Distinct().Take(6))); }
		if (t == null) throw new Exception(typeName + " not in " + string.Join(",", asm.GetTypes().Select(x => x.FullName)));
		var s = SceneHost.Spawn(t, a, a.gameObject, "plugin#" + a.storables.Count, "Pkg.1:/x.cs");
		s.Init();
		return s;
	}

	[Scenario("forcerodsync", "ZeroT.ForceRodSync")]
	public static void Sync(Harness h)
	{
		var man = h.Person("Man", true);
		var w1 = h.Person("Woman1"); var w2 = h.Person("Woman2");
		Dress(man, new Vector3(0f, 0f, 0f), false);
		Dress(w1, new Vector3(0f, 0f, 1f), true);
		Dress(w2, new Vector3(5f, 0f, 1f), true);

		var rodAsm = h.LoadTarget("DiviningRodScripts");
		var ffAsm = h.LoadTarget("ForceForgeScripts");
		var rod = Attach(h, rodAsm, "ToumeiHitsuji.DiviningRod", man);
		var ff = Attach(h, ffAsm, "ParticlePinnacle.ForceForge", man);
		h.Step(0.5f);
		var atomChooser = ff.GetStringChooserJSONParam("target to move to");
		var toThis = (JSONStorableStringChooser)ff.GetType().GetField("TargetSelector", All).GetValue(ff);
		var pdp = ff.GetBoolJSONParam("Auto-sync Target with PenisDiviningPath");
		var fTarget = rod.GetType().GetField("_penisTarget", All);
		h.Check(atomChooser != null && toThis != null && pdp != null && fTarget != null, "the real scripts started: ForceForge's choosers and the rod's pick field are there");
		Func<Atom> rodAtom = () => { var c = fTarget.GetValue(rod) as Component; return c == null ? null : c.GetComponentInParent<Atom>(); };
		var tip = man.freeControllers.First(f => f.name == "penisTipControl").transform;

		atomChooser.val = "Woman2"; toThis.val = "Hips";   // ForceForge starts somewhere else

		// the rod alone: it picks Woman1 (her vagina is at the tip, her mouth is further away)
		tip.position = new Vector3(0f, 0.9f, 0.7f);
		h.Step(2f);
		h.Check(rodAtom() == w1, "the rod picks Woman1 on its own (" + (rodAtom() == null ? "nobody" : rodAtom().uid) + ")");
		h.Check(atomChooser.val == "Woman2" && toThis.val == "Hips", "without the plugin ForceForge stays where it was (" + atomChooser.val + " / " + toThis.val + ")");

		h.Start();
		h.Step(1.5f);
		h.Check(atomChooser.val == "Woman1", "Woman1: ForceForge's target follows the rod (" + atomChooser.val + ")");
		h.Check(toThis.val == "Vagina", "...body part Vagina (" + toThis.val + ")");
		h.Check(!pdp.val, "ForceForge's own PenisDiviningPath sync is switched off so they do not fight");

		// the rod re-targets to Woman1's mouth: the pick moves, ForceForge follows
		tip.position = new Vector3(0f, 1.5f, 0.7f);
		h.Step(1.5f);
		h.Check(rodAtom() == w1, "the rod picks Woman1's mouth");
		h.Check(atomChooser.val == "Woman1" && toThis.val == "Head", "ForceForge follows to the head (" + atomChooser.val + " / " + toThis.val + ")");

		// the rod goes to the other woman
		tip.position = new Vector3(5f, 0.9f, 0.7f);
		h.Step(1.5f);
		h.Check(rodAtom() == w2, "the rod picks Woman2 (" + (rodAtom() == null ? "nobody" : rodAtom().uid) + ")");
		h.Check(atomChooser.val == "Woman2" && toThis.val == "Vagina", "ForceForge follows to Woman2 / Vagina (" + atomChooser.val + " / " + toThis.val + ")");

		// the rod has no target (every spot switched off): ForceForge keeps what it had
		rod.GetBoolJSONParam("Vagina Enabled").val = false; rod.GetBoolJSONParam("Mouth Enabled").val = false;
		h.Step(1.5f);
		h.Check(rodAtom() == null, "the rod has no target");
		h.Check(atomChooser.val == "Woman2" && toThis.val == "Vagina", "...and ForceForge is left alone (" + atomChooser.val + " / " + toThis.val + ")");

		// the body part can be left to the user
		rod.GetBoolJSONParam("Vagina Enabled").val = true; rod.GetBoolJSONParam("Mouth Enabled").val = true;
		toThis.val = "Hips";
		h.Set("Sync", "FollowBodyPart", "false");
		tip.position = new Vector3(0f, 0.9f, 0.7f);
		h.Step(1.5f);
		h.Check(atomChooser.val == "Woman1" && toThis.val == "Hips", "FollowBodyPart=false: only the person follows (" + atomChooser.val + " / " + toThis.val + ")");

		// switched off
		h.Set("Sync", "Enabled", "false");
		tip.position = new Vector3(5f, 0.9f, 0.7f);
		h.Step(1.5f);
		h.Check(atomChooser.val == "Woman1", "Enabled=false: ForceForge is left alone (" + atomChooser.val + ")");
	}
}
