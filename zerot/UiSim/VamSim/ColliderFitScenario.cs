using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using GPUTools.Physics.Scripts.Behaviours;
using SimpleJSON;
using UnityEngine;

// ColliderFit Headless on a built female: a forearm with a fixed capsule collider, a ring of skin vertices around it and one body morph that
// pulls the skin in. The plugin has to fit the collider to the skin (and again after the morph), keep out of the way of anything Collider
// Editor (Headless) owns, leave a collider alone that something else changed, honour the margin, and put everything back when switched off.
public static class ColliderFitScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	// stands in for ColliderEditor.Headless.dll: the plugin object other plugins find by guid, with the preset it applies
	public sealed class FakeHeadlessCe : BaseUnityPlugin
	{
		public JSONClass Preset { get; set; }
	}

	private sealed class Rig
	{
		public Atom atom;
		public DAZCharacterSelector geo;
		public DAZBone bone;
		public CapsuleCollider cap;
		public CapsuleLineSphereCollider gpu;
		public DAZMorph morph;
		public DAZSkinV2 skin;
		public string uuid;
	}

	// radius 0.04, height 0.25 along x; skin ring radius 0.05 around it; the morph pulls every skin vertex 0.015 towards the axis
	private static Rig Build(Harness h, string uid, bool male = false)
	{
		var a = SimWorld.NewPerson(h.Sc, uid, male);
		var rig = new Rig { atom = a };
		rig.geo = (DAZCharacterSelector)a.GetStorableByID("geometry");
		rig.geo.useAdvancedColliders = true;
		var run = rig.geo.gameObject.AddComponent<DAZCharacterRun>();
		run.morphBank1 = rig.geo.morphBank1;

		var rootGo = new GameObject("Genesis2Female"); rootGo.transform.SetParent(a.transform, false);
		var bones = rootGo.AddComponent<DAZBones>();
		var boneGo = new GameObject("lForeArm"); boneGo.transform.SetParent(rootGo.transform, false);
		rig.bone = boneGo.AddComponent<DAZBone>();
		rig.bone.morphedWorldToLocalMatrix = boneGo.transform.worldToLocalMatrix;
		boneGo.AddComponent<Rigidbody>();
		var colGo = new GameObject("_Collider1"); colGo.transform.SetParent(boneGo.transform, false);
		rig.cap = colGo.AddComponent<CapsuleCollider>();
		rig.cap.direction = 0; rig.cap.radius = 0.04f; rig.cap.height = 0.25f; rig.cap.center = Vector3.zero;
		rig.gpu = colGo.AddComponent<CapsuleLineSphereCollider>();
		rig.uuid = "CapsuleCollider:lForeArm._Collider1._Collider1";

		// skin: 16 around x 24 along the axis
		const int ring = 16, rows = 24;
		var rest = new Vector3[ring * rows];
		var strong = new DAZBone[ring * rows];
		var deltas = new DAZMorphVertex[ring * rows];
		for (int j = 0; j < rows; j++)
		{
			float x = -0.09f + 0.18f * j / (rows - 1);
			for (int i = 0; i < ring; i++)
			{
				float ang = 2f * Mathf.PI * (i + 0.5f) / ring;
				var dir = new Vector3(0f, Mathf.Cos(ang), Mathf.Sin(ang));
				int k = j * ring + i;
				rest[k] = new Vector3(x, 0f, 0f) + dir * 0.05f;
				strong[k] = rig.bone;
				deltas[k] = new DAZMorphVertex { vertex = k, delta = dir * -0.015f };
			}
		}
		var mesh = new GameObject("skinMesh").AddComponent<DAZMesh>();
		mesh.transform.SetParent(a.transform, false);
		mesh.UVVertices = rest; mesh.numBaseVertices = rest.Length;
		rig.skin = new GameObject("skin2").AddComponent<DAZSkinV2>();
		rig.skin.transform.SetParent(a.transform, false);
		rig.skin.root = bones; rig.skin.dazMesh = mesh; rig.skin.strongestDAZBone = strong;
		rig.geo.selectedCharacter.skin = rig.skin;

		rig.morph = new DAZMorph("Forearm Size", -1f, 1f, 0f);
		rig.morph.visible = true; rig.morph.isPoseControl = false; rig.morph.isRuntime = false; rig.morph.deltasLoaded = true; rig.morph.deltas = deltas;
		rig.geo.morphBank1.morphs.Add(rig.morph);
		return rig;
	}

	private static void SetMorph(Rig r, float v) { r.morph.appliedValue = v; }

	private static FakeHeadlessCe InstallFakeCe(Harness h, params string[] keys)
	{
		var go = new GameObject("fake collider editor headless");
		var p = go.AddComponent<FakeHeadlessCe>();
		var preset = new JSONClass();
		var ed = new JSONClass();
		foreach (var k in keys) ed.Add(k, new JSONClass());
		preset.Add("editables", ed);
		p.Preset = preset;
		var info = new PluginInfo { Instance = p };
		Chainloader.PluginInfos["acidbubbles.collidereditor.headless"] = info;
		return p;
	}

	[Scenario("colliderfit", "ZeroT.ColliderFitHeadless")]
	public static void Fit(Harness h)
	{
		Chainloader.PluginInfos.Clear();
		var w = Build(h, "Woman");
		var m = Build(h, "Man", true);
		float r0 = w.cap.radius, h0 = w.cap.height;
		h.Start();
		h.Step(1f);
		h.Near(w.cap.radius, r0, "before the settle time nothing is touched", 1e-6f);
		h.Step(4f);

		// first fit: the skin is at 0.05, the collider at 0.04 -> collider follows the skin
		h.Near(w.cap.radius, 0.05f, "the forearm collider is fitted to the skin (50 mm)", 0.0015f);
		h.Check(Mathf.Abs(w.cap.height - h0) < 1e-5f || w.cap.height >= 2f * w.cap.radius, "the capsule is still a capsule (height " + w.cap.height.ToString("0.###") + ")");
		h.Near(m.cap.radius, r0, "a male atom is left alone", 1e-6f);

		// a morph pulls the skin in: refit after the morphs stop moving
		SetMorph(w, 1f);
		h.Step(1.5f);
		h.Near(w.cap.radius, 0.035f, "the morph pulls the skin in: the collider shrinks to 35 mm", 0.0015f);
		SetMorph(w, 0.5f);
		h.Step(1.5f);
		h.Near(w.cap.radius, 0.0425f, "half the morph: 42.5 mm", 0.0015f);
		SetMorph(w, 1f);
		h.Step(1.5f);

		// interval refit: the morph data changes without any appliedValue changing, so only the timer can notice
		h.Set("Timing", "RefitIntervalSeconds", "1");
		foreach (var d in w.morph.deltas) d.delta = d.delta * 2f;
		h.Step(2.5f);
		h.Near(w.cap.radius, 0.020f, "interval refit picks up a change no morph check can see (20 mm)", 0.0015f);
		foreach (var d in w.morph.deltas) d.delta = d.delta * 0.5f;
		h.Set("Timing", "RefitIntervalSeconds", "0");
		h.Step(2.5f);
		h.Near(w.cap.radius, 0.020f, "interval 0: no timed refit", 0.0015f);
		h.Set("Timing", "RefitIntervalSeconds", "1");
		h.Step(2.5f);
		h.Near(w.cap.radius, 0.035f, "interval back on: fitted again (35 mm)", 0.0015f);

		// margin
		h.Set("Fit", "MarginMm", "2");
		h.Step(1.5f);
		h.Near(w.cap.radius, 0.037f, "margin +2 mm: collider 2 mm thicker than the skin", 0.0015f);
		h.Set("Fit", "MarginMm", "0");
		h.Step(1.5f);
		h.Near(w.cap.radius, 0.035f, "margin back to 0", 0.0015f);

		// something else changes the collider: hands off from then on
		w.cap.radius = 0.06f;
		h.Step(2.5f);
		SetMorph(w, 0f);
		h.Step(2f);
		h.Near(w.cap.radius, 0.06f, "a collider someone else changed is left alone", 1e-6f);
		SetMorph(w, 1f);

		// switched off: every value back to what VaM gave it (the foreign 0.06 stays: it is not ours to undo)
		var other = Build(h, "Woman2");
		h.Step(5f);
		h.Near(other.cap.radius, 0.05f, "a second woman is picked up by the scan", 0.0015f);
		h.Set("General", "Active", "false");
		h.Step(1.5f);
		h.Near(other.cap.radius, 0.04f, "Active=false: the collider is back to its original radius", 1e-6f);
		h.Near(other.cap.height, h0, "...and its original height", 1e-6f);
		h.Check((other.cap.center - Vector3.zero).sqrMagnitude < 1e-10f, "...and its original centre");
		h.Set("General", "Active", "true");
		h.Step(5f);
		h.Near(other.cap.radius, 0.05f, "Active=true: fitted again", 0.0015f);
		h.Check(!Sim.Errors.Any(e => e.Contains("ColliderFit") || e.Contains("FitHost")), "the plugin threw nothing");
	}

	[Scenario("colliderfit_ce", "ZeroT.ColliderFitHeadless")]
	public static void WithColliderEditor(Harness h)
	{
		Chainloader.PluginInfos.Clear();
		// Collider Editor (Headless) is installed and its preset keeps this forearm collider
		var fake = InstallFakeCe(h, "CapsuleCollider:lForeArm._Collider1._Collider1");
		var w = Build(h, "Woman");
		h.Start();
		h.Step(6f);
		h.Near(w.cap.radius, 0.04f, "the collider Collider Editor (Headless) keeps is not touched", 1e-6f);

		// its preset changes to something that does not cover it: taken over by itself
		var preset = new JSONClass(); var ed = new JSONClass(); ed.Add("CapsuleCollider:something.else", new JSONClass()); preset.Add("editables", ed);
		fake.Preset = preset;
		h.Step(6f);
		h.Near(w.cap.radius, 0.05f, "the preset no longer covers it: fitted to the skin", 0.0015f);

		// the preset takes it again: handed back to the original value
		var preset2 = new JSONClass(); var ed2 = new JSONClass(); ed2.Add("CapsuleCollider:lForeArm._Collider1._Collider1", new JSONClass()); preset2.Add("editables", ed2);
		fake.Preset = preset2;
		h.Step(1.5f);
		w.cap.radius = 0.033f;   // what Collider Editor does when it applies its adjustment
		h.Step(4f);
		SetMorph(w, 1f);
		h.Step(2f);
		h.Near(w.cap.radius, 0.033f, "Collider Editor's value stays when the morph changes", 1e-6f);

		// Collider Editor (Headless) switched off: it hands the colliders back, so they are taken over
		h.Check(!Sim.Errors.Any(e => e.Contains("ColliderFit") || e.Contains("FitHost")), "the plugin threw nothing");
	}

	[Scenario("colliderfit_scene_plugin", "ZeroT.ColliderFitHeadless")]
	public static void WithScenePlugin(Harness h)
	{
		Chainloader.PluginInfos.Clear();
		var w = Build(h, "Woman");
		// the mutoe.ColliderFit scene plugin is on this atom: this plugin leaves the atom alone
		var st = new GameObject("plugin#0_mutoe.ColliderFit.1:/Custom/Scripts/mutoe/ColliderFit/ColliderFit.cslist").AddComponent<JSONStorable>();
		st.storeId = "plugin#0_mutoe.ColliderFit.1:/Custom/Scripts/mutoe/ColliderFit/ColliderFit.cslist"; st.containingAtom = w.atom;
		w.atom.storables[st.storeId] = st;
		h.Start();
		h.Step(6f);
		h.Near(w.cap.radius, 0.04f, "an atom that carries the ColliderFit scene plugin is left alone", 1e-6f);
		h.Check(!Sim.Errors.Any(e => e.Contains("ColliderFit") || e.Contains("FitHost")), "the plugin threw nothing");
	}
}
