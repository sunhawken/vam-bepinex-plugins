using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;

// a stand-in for the .var's own plugin class: same namespace and name
namespace CheesyFX
{
	public class FlexIt : MonoBehaviour { }
}

public static class ShakeItScenario
{
	private const string Pkg = "CheesyFX.ShakeIt.42:/";
	private const string ShakeDir = "Saves/PluginData/CheesyFX/ShakeIt/";
	private const string FlexDir = "Saves/PluginData/CheesyFX/FlexIt/";
	private const string Host = "ZeroT.ShakeIt";
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static readonly string[] Parts = { "", "Top", "Mid", "Low" };

	private static IEnumerable<string> ShakeHandlerNames()
	{
		var two = new[] { "Glutes", "Thigh", "Calf", "UArm", "Breasts" };
		var one = new[] { "Abs", "AbsSide", "Cheeks" };
		foreach (var r in two) foreach (var side in new[] { "l", "r" }) foreach (var p in Parts) yield return side + r + p;
		foreach (var r in one) foreach (var p in Parts) yield return r + p;
	}

	private static IEnumerable<string> FlexMorphNames()
	{
		foreach (var r in new[] { "Ankle", "Knee", "Elbow", "Index", "Mid", "Ring", "Pinky" }) foreach (var i in new[] { 0, 1 }) foreach (var s in new[] { "L", "R" }) yield return "FI_" + r + i + s;
		foreach (var r in new[] { "Thigh", "Shoulder" }) foreach (var i in new[] { 0, 1, 2, 3 }) foreach (var s in new[] { "L", "R" }) yield return "FI_" + r + i + s;
		foreach (var r in new[] { "Abdomen", "Neck" }) foreach (var i in new[] { 0, 1, 2, 3 }) yield return "FI_" + r + i;
	}

	private static Atom Woman(Harness h, string uid, bool male = false)
	{
		var a = h.Person(uid, male);
		var geo = (DAZCharacterSelector)a.GetStorableByID("geometry");
		string g = male ? "male" : "female";
		foreach (var n in ShakeHandlerNames()) foreach (var ax in new[] { "X", "Y", "Z", "R" })
			SimWorld.AddMorphUid(geo, Pkg + "Custom/Atom/Person/Morphs/" + g + "/CheesyFX/ShakeIt/SI_" + n + "_" + ax + ".vmi", -3f, 3f);
		foreach (var n in FlexMorphNames())
			SimWorld.AddMorphUid(geo, Pkg + "Custom/Atom/Person/Morphs/" + g + "/CheesyFX/FlexIt/" + n + ".vmi", -3f, 3f);
		return a;
	}

	private static DAZMorph Morph(Atom a, string name, bool flex = false)
	{
		var geo = (DAZCharacterSelector)a.GetStorableByID("geometry");
		return geo.morphsControlUI.GetMorphByUid(Pkg + "Custom/Atom/Person/Morphs/female/CheesyFX/" + (flex ? "FlexIt/" : "ShakeIt/") + name + ".vmi");
	}

	private static object Prop(object o, string n)
	{
		for (var t = o.GetType(); t != null; t = t.BaseType)
		{
			var f = t.GetField(n, All); if (f != null) return f.GetValue(o);
			var p = t.GetProperty(n, All); if (p != null) return p.GetValue(o, null);
		}
		throw new Exception("no member " + n + " on " + o.GetType().Name);
	}

	private static object RigOf(Harness h, Atom a)
	{
		var d = (IDictionary)Prop(h.Plugin, "rigs");
		return d.Contains(a) ? d[a] : null;
	}

	private static JSONStorable Store(Atom a) { return a.GetStorableByID(Host); }
	private static float Fl(Atom a, string n) { return Store(a).GetFloatJSONParam(n).val; }
	private static void SetFl(Atom a, string n, float v) { Store(a).GetFloatJSONParam(n).val = v; }
	private static void SetCh(Atom a, string n, string v) { Store(a).GetStringChooserJSONParam(n).val = v; }
	private static void SetB(Atom a, string n, bool v) { Store(a).GetBoolJSONParam(n).val = v; }
	private static bool Bl(Atom a, string n) { return Store(a).GetBoolJSONParam(n).val; }
	private static void Act(Atom a, string n) { Store(a).CallAction(n); }

	private const string TinyVap = "{\"setUnlistedParamsToDefault\":\"true\",\"storables\":[{\"id\":\"plugin#0_CheesyFX.ShakeIt\",\"enabled\":\"true\",\"ShakeIt\":{\"Glutes\":{\"Enabled\":\"true\",\"isCombined\":\"true\",\"Tension enabled\":\"true\",\"Glutes\":{\"strength\":[\"7\",\"7\",\"7\",\"7\"]}}}}]}";

	[Scenario("shakeit", "ZeroT.ShakeItStandalone")]
	public static void ShakeIt(Harness h)
	{
		FileManagerSecure.SimReset();
		SuperController.SimSaved.Clear();
		FileManager.SimPackageUids.Clear();
		FileManager.SimPackageUids.Add("CheesyFX.ShakeIt.40");
		FileManager.SimPackageUids.Add("CheesyFX.ShakeIt.42");
		var cfg = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.zerot.shakeit.cfg");
		if (System.IO.File.Exists(cfg)) System.IO.File.Delete(cfg);
		// a saved preset named after one of the women, and UserDefaults for FlexIt
		const string cheeksPreset = "{\"Cheeks\":{\"Enabled\":\"true\",\"isCombined\":\"true\",\"Tension enabled\":\"false\",\"Cheeks\":{\"strength\":[\"2\",\"2\",\"2\",\"2\"]}}}";
		SuperController.SimSaved[ShakeDir + "Female2.01.json"] = cheeksPreset; FileManagerSecure.SimAddFile(ShakeDir + "Female2.01.json");
		const string flexUser = "{\"Knee\":{\"Enabled\":\"true\",\"Amount\":[\"1\",\"1\",\"1\",\"1\"]}}";
		SuperController.SimSaved[FlexDir + "UserDefaults.json"] = flexUser; FileManagerSecure.SimAddFile(FlexDir + "UserDefaults.json");

		var f1 = Woman(h, "Female1"); var f2 = Woman(h, "Female2"); var m1 = Woman(h, "Male1", true);
		var orig = Woman(h, "Female3"); orig.gameObject.AddComponent<CheesyFX.FlexIt>();
		h.Start();
		h.Step(2f);

		// ---- who gets it
		h.Check(Store(f1) != null && Store(f2) != null, "every female Person gets the storable ZeroT.ShakeIt");
		h.Check(Store(m1) == null, "male Person: nothing (women only by default)");
		h.Check(Store(orig) == null && h.LogHas("is left to it"), "the .var's own plugin on a person: that person is left to it, with a log line");

		// ---- defaults: the built-in copy of Preset_Butt (the .vap is not there)
		SetCh(f1, "ShakeIt Region", "Glutes");
		h.Near(Fl(f1, "ShakeIt Strength X"), 10f, "Preset_Butt: Glutes strength 10 (the .var's own default is 2/4/2/0)");
		SetCh(f1, "ShakeIt Region", "Thighs"); SetCh(f1, "ShakeIt Part", "Upper");
		h.Check(!Bl(f1, "ShakeIt Combined"), "Preset_Butt: thighs are split into Upper / Mid / Lower");
		h.Near(Fl(f1, "ShakeIt Strength X"), 10f, "...Upper thigh strength 10");
		SetCh(f1, "ShakeIt Part", "Mid");
		h.Near(Fl(f1, "ShakeIt Strength X"), 3f, "...Mid thigh strength 3");
		SetCh(f1, "ShakeIt Region", "Breasts");
		h.Check(Bl(f1, "ShakeIt Region Enabled") && !Bl(f1, "ShakeIt Tension Enabled"), "Preset_Butt: breasts are on, without tension (off in the .var's own defaults)");
		SetCh(f1, "ShakeIt Region", "Cheeks"); h.Near(Fl(f1, "ShakeIt Strength X"), 10f, "Female1: cheeks strength 10 from the defaults");
		SetCh(f2, "ShakeIt Region", "Cheeks");
		h.Near(Fl(f2, "ShakeIt Strength X"), 2f, "Female2: cheeks strength 2 from her saved preset 'Female2.01' (applied after the defaults)");
		SetCh(f1, "FlexIt Region", "Ankle");
		h.Near(Fl(f1, "FlexIt Amount X"), 1.5f, "FlexIt ankle amount 1.5");
		h.Near(Fl(f1, "FlexIt Dynamic X"), 0.25f, "FlexIt ankle dynamic .25");
		SetCh(f1, "FlexIt Region", "Abdomen"); h.Near(Fl(f1, "FlexIt S-Ratio X"), 0.5f, "FlexIt abdomen S-Ratio .5");
		SetCh(f1, "FlexIt Region", "Knee"); h.Near(Fl(f1, "FlexIt Amount X"), 1f, "FlexIt UserDefaults.json applied: knee amount 1 (Preset_Butt says 2)");
		SetCh(f1, "FlexIt Region", "Thigh");
		h.Near(Fl(f1, "Max gloss"), 8f, "WorkOut max gloss 8"); h.Near(Fl(f1, "Exhaustion Gain"), 0.5f, "WorkOut exhaustion gain .5");
		h.Check(Bl(f1, "WorkOut Enabled") && Bl(f1, "ShakeIt Enabled") && Bl(f1, "FlexIt Enabled"), "all three modules are on (WorkOut is off in the .var, on in Preset_Butt)");

		// ---- the jiggle
		var lThigh = SimWorld.Body(f1, "lThigh");
		SetCh(f1, "ShakeIt Region", "Thighs");
		h.Step(0.1f);
		lThigh.velocity = new Vector3(0f, 2f, 0f);
		h.Step(0.02f);
		var top = Morph(f1, "SI_lThighTop_Y"); var mid = Morph(f1, "SI_lThighMid_Y"); var low = Morph(f1, "SI_lThighLow_Y");
		h.Check(top.morphValue < -0.05f && top.morphValue > -0.2f, "a thigh acceleration moves SI_lThighTop_Y (" + top.morphValue.ToString("0.###") + ")");
		h.Check(Math.Abs(top.morphValue) > 2.5f * Math.Abs(mid.morphValue) && Math.Abs(mid.morphValue) > 0.01f, "...Top (strength 10) more than Mid (strength 3): " + mid.morphValue.ToString("0.###"));
		h.Check(Math.Abs(Morph(f2, "SI_lThighTop_Y").morphValue) < 1e-6f, "the other woman is untouched");
		var seen = new List<float>(); for (int i = 0; i < 40; i++) { h.Step(0.02f); seen.Add(top.morphValue); }
		h.Check(seen.Any(v => v > 0.01f) && seen.Any(v => v < -0.01f), "it swings both ways while it dies out (frequency 16, damping 5)");
		h.Step(3f);
		h.Check(Math.Abs(top.morphValue) < 0.01f, "...and has settled after 3 s (" + top.morphValue.ToString("0.####") + ")");
		h.Check(Math.Abs(Morph(f1, "SI_rThighTop_Y").morphValue) < 0.05f, "the right thigh did not feel the left one's acceleration");
		// the other axes, the R morph
		lThigh.velocity = new Vector3(2f, 0f, 0f); h.Step(0.02f);
		h.Check(Math.Abs(Morph(f1, "SI_lThighTop_X").morphValue) > 0.01f, "X acceleration moves the X morph");
		h.Step(3f);

		// ---- tension (FlexIt's morphs feed it)
		var kneeMorph = Morph(f1, "FI_Knee0L", true);
		h.Check(kneeMorph != null, "FlexIt morphs found through the newest package prefix (" + Pkg + ")");

		// ---- region and module switches
		SetCh(f1, "ShakeIt Region", "Glutes");
		var gl = SimWorld.Body(f1, "LGlute");
		gl.velocity = new Vector3(0f, 3f, 0f); h.Step(0.02f);
		var glY = Morph(f1, "SI_lGlutes_Y");
		h.Check(Math.Abs(glY.morphValue) > 0.01f, "glutes jiggle too (" + glY.morphValue.ToString("0.###") + ")");
		SetB(f1, "ShakeIt Region Enabled", false);
		h.Near(glY.morphValue, 0f, "Region Enabled off: its morphs go back to 0");
		gl.velocity = new Vector3(0f, 0f, 0f); h.Step(0.04f); gl.velocity = new Vector3(0f, 3f, 0f); h.Step(0.02f);
		h.Near(glY.morphValue, 0f, "...and stay there");
		SetB(f1, "ShakeIt Region Enabled", true);
		SetB(f1, "ShakeIt Enabled", false);
		lThigh.velocity = Vector3.zero; h.Step(0.1f); lThigh.velocity = new Vector3(0f, 2f, 0f); h.Step(0.02f);
		h.Near(top.morphValue, 0f, "ShakeIt Enabled off: no jiggle");
		SetB(f1, "ShakeIt Enabled", true);
		h.Set("General", "ShakeIt", "false");
		h.Check(!Bl(f1, "ShakeIt Enabled") && !Bl(f2, "ShakeIt Enabled"), "the config setting switches the module off for everybody");
		h.Set("General", "ShakeIt", "true");
		h.Step(0.1f);

		// ---- editing a handler
		SetCh(f1, "ShakeIt Region", "Glutes");
		SetFl(f1, "ShakeIt Strength X", 5f);
		SetCh(f1, "ShakeIt Region", "Calves"); SetCh(f1, "ShakeIt Region", "Glutes");
		h.Near(Fl(f1, "ShakeIt Strength X"), 5f, "an edited value is kept per region");
		SetB(f1, "ShakeIt Configure Tensed State", true);
		h.Near(Fl(f1, "ShakeIt Strength X"), 1f, "Configure tensed state shows the tensed strength (Preset_Butt: 1/2/1/0)");
		SetB(f1, "ShakeIt Configure Tensed State", false);
		SetB(f1, "ShakeIt Sync Axes", true); SetFl(f1, "ShakeIt Strength Y", 3f);
		h.Check(Math.Abs(Fl(f1, "ShakeIt Strength X") - 3f) < 1e-4f && Math.Abs(Fl(f1, "ShakeIt Strength R") - 3f) < 1e-4f, "Sync: one axis sets all four");
		SetB(f1, "ShakeIt Sync Axes", false);
		SetFl(f1, "ShakeIt Strength X", 5f);

		// ---- FlexIt, static: the thigh raised in front
		var thigh = SimWorld.Body(f1, "lThigh").transform;
		thigh.localRotation = Quaternion.Euler(-30f, 0f, 0f);       // forward.y = .5
		h.Step(4f);
		var th0 = Morph(f1, "FI_Thigh0L", true);
		h.Near(th0.morphValue, (float)Math.Sin(45 * Math.PI / 180) * 0.75f * 1f, "a raised thigh pushes FI_Thigh0L to sin(45) x S-Ratio .75 x amount 1", 0.04f);
		SetCh(f1, "FlexIt Region", "Thigh"); SetB(f1, "FlexIt Region Enabled", false);
		h.Near(th0.morphValue, 0f, "FlexIt Region Enabled off: its morphs go back to 0");
		SetB(f1, "FlexIt Region Enabled", true);
		thigh.localRotation = Quaternion.identity; h.Step(4f);
		h.Near(th0.morphValue, 0f, "thigh down again: morph relaxes", 0.03f);
		// dynamic: angular acceleration fills a buffer that is poured into the morph
		SimWorld.Body(f1, "lThigh").angularVelocity = new Vector3(5f, 0f, 0f); h.Step(0.02f);
		SimWorld.Body(f1, "lThigh").angularVelocity = new Vector3(1f, 0f, 0f); h.Step(0.02f);
		h.Check(th0.morphValue > 0.5f, "a quick thigh swing adds a dynamic push (" + th0.morphValue.ToString("0.##") + ")");
		SimWorld.Body(f1, "lThigh").angularVelocity = Vector3.zero; h.Step(3f);

		// ---- support: a foot on the ground cancels the knee's pose morph
		var foot = SimWorld.Body(f1, "lFoot").transform; foot.localRotation = Quaternion.Euler(30f, 0f, 0f);
		var rig = RigOf(h, f1);
		var flexList = (IList)Prop(rig, "flex");
		var ankle = flexList[0]; var knee = flexList[1];
		var trackers = (IList)Prop(ankle, "supportTrackers");
		h.Check(trackers.Count == 2, "the ankle watches both feet for support (" + trackers.Count + " trackers)");
		var shin = SimWorld.Body(f1, "lShin").transform; shin.localRotation = Quaternion.Euler(-30f, 0f, 0f);
		h.Step(4f);
		var kn0 = Morph(f1, "FI_Knee0L", true);
		float kneeFree = kn0.morphValue;
		h.Check(kneeFree > 0.2f, "a bent knee: FI_Knee0L " + kneeFree.ToString("0.###"));
		var ground = new GameObject("ground").AddComponent<Rigidbody>();
		var reg = trackers[0].GetType().GetMethod("RegisterCollision");
		reg.Invoke(trackers[0], new object[] { ground, foot.position + new Vector3(0f, -1f, 0f) });
		h.Check((bool)Prop(ankle, "supported"), "a contact below the foot makes the ankle 'supported'");
		h.Step(4f);
		h.Near(kn0.morphValue, 0f, "...and the knee above it stops posing (the foot carries the weight)", 0.03f);
		trackers[0].GetType().GetMethod("DeregisterCollision").Invoke(trackers[0], new object[] { ground });
		h.Step(0.3f);
		h.Check(!(bool)Prop(ankle, "supported"), "the contact ends: not supported after the short timeout");
		h.Step(4f);
		h.Check(kn0.morphValue > 0.2f, "...and the knee poses again");
		shin.localRotation = Quaternion.identity; foot.localRotation = Quaternion.identity;
		h.Step(4f);

		// ---- Renderer Fps
		SetCh(f1, "Renderer Fps", "30"); h.Step(0.1f);
		var timing = Prop(Prop(rig, "ctx"), "Timing");
		h.Near((float)Prop(timing, "flexRegressionSpeed"), 10f / 30f, "Renderer Fps 30: FlexIt follows the 30 fps frame time", 0.01f);
		SetCh(f1, "Renderer Fps", "Realtime"); h.Step(0.1f);
		h.Near((float)Prop(timing, "flexRegressionSpeed"), 0.2f, "Realtime: back to the physics step", 0.001f);

		// ---- WorkOut
		var skin = f1.GetStorableByID("skin");
		SetFl(f1, "Sweat Gain", 5f);
		float g0 = skin.GetFloatJSONParam("Gloss").val;
		var hip = SimWorld.Body(f1, "chest");
		for (int i = 0; i < 200; i++) { hip.velocity = new Vector3(0f, (i % 2 == 0) ? 4f : 1f, 0f); h.Step(0.02f); }
		hip.velocity = Vector3.zero;
		h.Check(Fl(f1, "Work Done Recently") > 1000f, "movement builds work (" + Fl(f1, "Work Done Recently").ToString("0") + ")");
		h.Check(Fl(f1, "Exhaustion") > 0f, "...work builds exhaustion (" + Fl(f1, "Exhaustion").ToString("0.####") + ")");
		h.Check(skin.GetFloatJSONParam("Gloss").val > g0, "...and exhaustion makes the skin glossier (" + g0.ToString("0.###") + " -> " + skin.GetFloatJSONParam("Gloss").val.ToString("0.####") + ")");
		h.Near(skin.GetFloatJSONParam("Gloss Texture Offset").val, 0.6f, "...the gloss texture offset stays at its maximum while the gloss is near the minimum", 0.002f);
		skin.GetFloatJSONParam("Gloss").val = 6f; h.Step(0.04f);
		h.Near(skin.GetFloatJSONParam("Gloss Texture Offset").val, 0.6f + (0.45f - 0.6f) / (float)Math.Pow(5f - 6.6f, 3) * (float)Math.Pow(5f - 6f, 3), "...and follows a glossier skin along the cubic curve between the limits", 0.01f);
		skin.GetFloatJSONParam("Gloss").val = 7f; h.Step(0.04f);
		h.Near(skin.GetFloatJSONParam("Gloss Texture Offset").val, 0.45f, "...down to the minimum offset above the threshold gloss 6.6", 0.001f);
		h.Check(Store(f1).GetStringJSONParam("glossInfo").val.Contains("Gloss"), "a status line: " + Store(f1).GetStringJSONParam("glossInfo").val);
		hip.velocity = Vector3.zero;
		Act(f1, "WorkOut Reset");
		h.Near(Fl(f1, "Work Done Recently"), 0f, "WorkOut Reset empties the work");
		h.Step(2f);

		// ---- WorkOut triggers
		var work = Prop(rig, "work");
		// feed the trigger the way a scene would: threshold 1, one action on exceed, one value action
		var tj = (JSONClass)JSON.Parse("{\"Current PowerTrigger\":{\"Input From\":\"0\",\"Input To\":\"2\",\"Threshold\":\"1\",\"OnExceeded\":{\"startActions\":[{}],\"transitionActions\":[],\"endActions\":[]},\"OnUndershoot\":{\"startActions\":[{}],\"transitionActions\":[],\"endActions\":[]},\"OnValueChanged\":{\"startActions\":[],\"transitionActions\":[{}],\"endActions\":[]}}}");
		var state = new JSONClass(); state["WorkOut"] = tj;
		Store(f1).LateRestoreFromJSON(state, true, true, true);
		TriggerActionDiscrete.SimTriggered = 0; TriggerActionTransition.SimCalls = 0;
		SetCh(f1, "ShakeIt Region", "Glutes");
		h.Check(Math.Abs(Fl(f1, "Current Power Trigger Threshold") - 1f) < 1e-4f && Math.Abs(Fl(f1, "Current Power Trigger Input To") - 2f) < 1e-4f, "trigger settings (threshold, input range) are loaded and shown");
		hip.velocity = Vector3.zero;
		for (int i = 0; i < 60; i++) { hip.velocity = new Vector3(0f, (i % 2 == 0) ? 4f : 1f, 0f); h.Step(0.02f); }
		hip.velocity = Vector3.zero; h.Step(0.1f);
		h.Check(TriggerActionTransition.SimCalls > 5, "the value trigger runs its actions every frame (" + TriggerActionTransition.SimCalls + " calls)");
		h.Check(TriggerActionTransition.SimLast >= 0f && TriggerActionTransition.SimLast <= 1f, "...with the power remapped to 0..1 (" + TriggerActionTransition.SimLast.ToString("0.###") + ")");
		h.Check(TriggerActionDiscrete.SimTriggered >= 2, "threshold crossings run the event actions (" + TriggerActionDiscrete.SimTriggered + " runs: exceeded and undershoot)");

		// ---- presets
		SetCh(f1, "ShakeIt Region", "Glutes"); SetFl(f1, "ShakeIt Strength X", 6f);
		Act(f1, "ShakeIt Save Preset");
		var saved = SuperController.SimSaved.Keys.Where(k => k.StartsWith(ShakeDir + "Female1.")).ToList();
		h.Check(saved.Count == 1 && saved[0] == ShakeDir + "Female1.01.json" && SuperController.SimSaved[saved[0]].Contains("\"Glutes\"") && SuperController.SimSaved[saved[0]].Contains("\"strength\""), "Save Preset writes <name>.01.json in the .var's format (" + string.Join(",", saved.ToArray()) + ")");
		SetFl(f1, "ShakeIt Strength X", 9f);
		Act(f1, "ShakeIt Apply Latest Matching Preset");
		h.Near(Fl(f1, "ShakeIt Strength X"), 6f, "Apply Latest Matching Preset brings the saved value back");
		Act(f1, "ShakeIt FactoryDefaults");
		h.Near(Fl(f1, "ShakeIt Strength X"), 2f, "FactoryDefaults: the .var's own values (glutes 2/4/2/0)");
		Act(f1, "Apply Preset_Butt Defaults");
		h.Near(Fl(f1, "ShakeIt Strength X"), 10f, "Apply Preset_Butt Defaults: the starting point again");
		SetFl(f1, "ShakeIt Strength X", 4f); Act(f1, "ShakeIt Save As UserDefaults");
		h.Check(SuperController.SimSaved.ContainsKey(ShakeDir + "UserDefaults.json"), "Save As UserDefaults");

		// ---- the scene keeps what was changed
		var jc = Store(f1).GetJSON(true, true, true);
		h.Check(jc.HasKey("ShakeIt") && jc.HasKey("FlexIt") && jc.HasKey("WorkOut"), "the scene JSON of a changed person has ShakeIt / FlexIt / WorkOut");
		var keep = jc;
		h.Sc.BeginLoad();
		foreach (var a0 in h.Sc.atoms.ToArray()) h.Sc.RemoveAtom(a0);
		h.Step(0.2f);
		var ngA = Woman(h, "NewGirl"); var ngB = Woman(h, "NewGirl2");
		h.Step(1.5f);
		h.Check(Store(ngA) != null && RigOf(h, ngA) == null, "while a scene loads: the storable is there for the scene to fill, the rig waits");
		Store(ngA).LateRestoreFromJSON(keep, true, true, true);
		h.Sc.EndLoad();
		h.Step(2.5f);
		var ng = ngA;
		h.Check(RigOf(h, ngA) != null && RigOf(h, ngB) != null, "...and their rigs once loading is over");
		SetCh(ngA, "ShakeIt Region", "Glutes");
		h.Near(Fl(ngA, "ShakeIt Strength X"), 4f, "the scene's own settings were restored into the first one (the load order does not matter)");
		SetCh(ngB, "ShakeIt Region", "Glutes");
		h.Near(Fl(ngB, "ShakeIt Strength X"), 4f, "the second one starts from UserDefaults (strength 4)");

		// restoring the saved state into a freshly loaded person
		h.Load(delegate { Woman(h, "Restored"); });
		h.Step(1.5f);
		var rs = h.Sc.GetAtomByUid("Restored");
		Store(rs).LateRestoreFromJSON(keep, true, true, true);
		h.Step(2.5f);
		SetCh(rs, "ShakeIt Region", "Glutes");
		h.Near(Fl(rs, "ShakeIt Strength X"), 4f, "the scene's settings were restored into the new person");
		h.Step(0.2f);

		// ---- trouble
		// 1. the person's look is rebuilt: the morph objects are replaced
		var oldMorph = Morph(rs, "SI_lThighTop_Y");
		var geoR = (DAZCharacterSelector)rs.GetStorableByID("geometry");
		DAZMorph repl = null;
		foreach (var om in geoR.morphsControlUI.all.ToArray())
		{
			var nm = new DAZMorph(om.uid, om.min, om.max, 0f);
			geoR.morphsControlUI.all.Remove(om); geoR.morphsControlUI.all.Add(nm);
			if (om == oldMorph) repl = nm;
		}
		h.Step(1.6f);
		var lt = SimWorld.Body(rs, "lThigh");
		lt.velocity = Vector3.zero; h.Step(0.1f); lt.velocity = new Vector3(0f, 2f, 0f); h.Step(0.02f);
		h.Check(Math.Abs(repl.morphValue) > 0.01f && Math.Abs(oldMorph.morphValue) < 1e-6f, "morphs replaced by VaM are found again within a second or two");
		lt.velocity = Vector3.zero; h.Step(3f);

		// 2. a body part is missing
		var oddWoman = Woman(h, "NoShoulder");
		var sh = SimWorld.Body(oddWoman, "lShldr"); sh.gameObject.name = "gone1";
		foreach (var fr in oddWoman.forceReceivers) if (fr.name == "gone1") fr.gameObject.name = "gone1";
		h.Step(2.5f);
		h.Check(RigOf(h, oddWoman) != null && h.LogHas("was not found on NoShoulder"), "a missing body part: that region is skipped and logged, the rest runs");

		// 3. one module throws every step: it is reported, the others keep going, the person is rebuilt
		SetCh(rs, "ShakeIt Region", "Glutes"); SetFl(rs, "ShakeIt Strength X", 8f);
		var thrower = Morph(rs, "SI_lThighTop_Y");
		thrower.jsonFloat.setCallbackFunction = delegate (float v) { throw new InvalidOperationException("boom"); };
		var rigBefore = RigOf(h, rs);
		for (int i = 0; i < 90; i++) { lt.velocity = new Vector3(0f, (i % 2 == 0) ? 3f : 1f, 0f); h.Step(0.02f); }
		h.Check(h.LogHas("ShakeIt on Restored failed: boom"), "a throwing module is logged once, not every step");
		h.Check(Morph(rs, "FI_Thigh0L", true) != null, "(FlexIt keeps running meanwhile)");
		thrower.jsonFloat.setCallbackFunction = null;
		lt.velocity = Vector3.zero;
		h.Step(5f);
		h.Check(h.LogHas("Rebuilding Restored"), "after repeated failures the person is rebuilt");
		h.Check(RigOf(h, rs) != null && !ReferenceEquals(RigOf(h, rs), rigBefore), "...with a fresh rig");
		SetCh(rs, "ShakeIt Region", "Glutes");
		h.Near(Fl(rs, "ShakeIt Strength X"), 8f, "...that keeps the settings it had");

		// 4. an atom is removed in the middle of things
		var doomed = Woman(h, "Doomed"); h.Step(2.5f);
		h.Check(RigOf(h, doomed) != null, "(Doomed has a rig)");
		doomed.Remove(); h.Step(2f);
		h.Check(RigOf(h, doomed) == null && RigOf(h, rs) != null, "a removed person is dropped quietly, the others continue");

		// 5. gender change
		var flip = (DAZCharacterSelector)rs.GetStorableByID("geometry");
		flip.gender = DAZCharacterSelector.Gender.Male;
		h.Step(1.5f);
		h.Check(RigOf(h, rs) == null, "a person who turns male loses it (IncludeMales is off)");
		flip.gender = DAZCharacterSelector.Gender.Female;
		h.Step(3f);
		h.Check(RigOf(h, rs) != null, "...and gets it back when she is a woman again");

		// ---- switching off
		var anyMorph = Morph(rs, "SI_lGlutes_Y"); anyMorph.morphValue = 0.7f;
		h.Set("General", "Active", "false");
		h.Step(0.5f);
		h.Check(Store(rs) == null && RigOf(h, rs) == null, "Active = false: the controls are gone");
		h.Set("General", "Active", "true");
		h.Step(3f);
		h.Check(Store(rs) != null, "...and back on");
	}

}
