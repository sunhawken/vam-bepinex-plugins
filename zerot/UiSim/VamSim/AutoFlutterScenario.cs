using System;
using System.Linq;
using System.Reflection;
using MVR.FileManagementSecure;
using UnityEngine;

// a stand-in for the .var's own plugin class: same name, no namespace
public class AutoFlutterHip : MonoBehaviour { }
public class AutoFlutterHipTechnic : MonoBehaviour { }

public static class AutoFlutterScenario
{
	private const string Girl = "Saves/PluginData/14mhz/AutoFlutterHip/Girl";

	private static string Slot(string rot1, string move1, float len, float rep, float lenMove = 0.3f)
	{
		return "{\"Repetition\":\"" + rep + "\",\"Length (sec)\":\"" + len + "\",\"Sync Offset\":\"0\",\"1st Rot, Axis\":\"" + rot1 + "\",\"1st Move, Axis\":\"" + move1 +
			"\",\"2nd Rot, Axis\":\"None\",\"2nd Move, Axis\":\"None\",\"1st Rotation\":\"0.5\",\"1st Rot, Offset\":\"0\",\"1st Movement\":\"" + lenMove + "\",\"1st Move, Offset\":\"0.25\"," +
			"\"2nd Rot, with Radial\":\"false\",\"2nd Rotation\":\"0.3\",\"2nd Rot, Offset\":\"0\",\"2nd Movement\":\"0.3\",\"2nd Move, Offset\":\"0\",\"1st Rot, Lock\":\"false\",\"1st Move, Lock\":\"false\",\"2st Rot, Lock\":\"false\",\"2st Move, Lock\":\"false\",\"isTourchedSlot\":\"true\"}";
	}

	private static string Preset(string name, bool sync, params string[] slots)
	{
		return "{\"type\":\"Technic\",\"name\":\"" + name + "\",\"image\":\"\",\"Repetition\":\"1\",\"Speed x sec\":\"1\",\"Endless Technic\":\"false\",\"Smooth Transition\":\"false\",\"Sync Other Atoms\":\"" + (sync ? "true" : "false") + "\",\"slotlist\":[" + string.Join(",", slots) + "]}";
	}

	private static Atom Woman(Harness h, string uid, string[] morphs)
	{
		var f = h.Person(uid);
		var geo = (DAZCharacterSelector)f.GetStorableByID("geometry");
		foreach (var m in morphs) SimWorld.AddMorph(geo, m, -1f, 1f);
		// standing straight: chest over the hip, knees under it
		Pose(f, 0f, 0.9f, 0.15f, 0.5f);
		return f;
	}

	// chestForward: how far the chest leans forward (m); kneeSpread: half distance between the knees
	private static void Pose(Atom a, float chestForward, float chestUp, float kneeSpread, float kneeDown)
	{
		SimWorld.Body(a, "hip").transform.position = new Vector3(0, 1f, 0);
		SimWorld.Body(a, "chest").transform.position = new Vector3(0, 1f + chestUp, chestForward);
		SimWorld.Body(a, "head").transform.position = new Vector3(0, 2f, chestForward);
		SimWorld.Body(a, "neck").transform.position = new Vector3(0, 1.9f, chestForward);
		SimWorld.Body(a, "lKneeLink").transform.position = new Vector3(-kneeSpread, 1f - kneeDown, 0);
		SimWorld.Body(a, "rKneeLink").transform.position = new Vector3(kneeSpread, 1f - kneeDown, 0);
	}

	[Scenario("autoflutterhip", "ZeroT.AutoFlutterHipStandalone")]
	public static void AutoFlutterHip(Harness h)
	{
		string[] morphs = { "AutoFlutterHip - Doggy Style Shape (TenStrip)", "AutoFlutterHip - Hourglass Waist (TenStrip)", "AutoFlutterHip - Waist ErectorSpinae (TenStrip)",
			"AutoFlutterHip - Thights Indent (kemenate)", "AutoFlutterHip - Gracilis Stretch (kemenate)" };
		FileManagerSecure.SimReset();
		SuperController.SimSaved.Clear();
		FileManagerSecure.SimAddText(Girl + "/Technic - Alpha.json", Preset("Alpha", true, Slot("+X", "+Y", 1f, 2f), Slot("+Z", "None", 1f, 1f)));
		FileManagerSecure.SimAddText(Girl + "/Technic - Beta.json", Preset("Beta", false, Slot("None", "+Z", 1f, 1f)));
		FileManagerSecure.SimAddText(Girl + "/Technic - Beta (1).json", Preset("Beta", false, Slot("None", "+Z", 1f, 1f)));
		FileManagerSecure.SimAddText(Girl + "/Technic - Empty.json", Preset("Empty", false));
		FileManagerSecure.SimAddText(Girl + "/notes.txt", "x");
		foreach (var f in new[] { BepInEx.Paths.ConfigPath })
		{
			var cfg = System.IO.Path.Combine(f, "com.zerot.autoflutterhip.cfg");
			if (System.IO.File.Exists(cfg)) System.IO.File.Delete(cfg);
		}
		var asm = h.Asm;
		var player = asm.GetType("ZeroT.AutoFlutterHipStandalone.TechnicPlayer", true);
		player.GetField("Ticks").SetValue(null, (Func<long>)(() => (long)(Time.time * 1e7f)));

		var f1 = Woman(h, "Female1", morphs); var f2 = Woman(h, "Female2", morphs); var m1 = h.Person("Male1", true);
		var fake = Woman(h, "Female3", morphs); fake.gameObject.AddComponent<AutoFlutterHip>(); fake.gameObject.AddComponent<AutoFlutterHipTechnic>();
		h.Start();
		h.Step(1.5f);

		var host = f1.GetStorableByID("ZeroT.AutoFlutterHip");
		h.Check(host != null && f2.GetStorableByID("ZeroT.AutoFlutterHip") != null, "every female Person gets the storable ZeroT.AutoFlutterHip");
		h.Check(m1.GetStorableByID("ZeroT.AutoFlutterHip") == null, "male Person: nothing (female only by default)");
		h.Check(fake.GetStorableByID("ZeroT.AutoFlutterHip") == null && h.LogHas("steps aside"), "the .var's own AutoFlutterHip on a person: both parts step aside, and it says so");

		// ---- the morphs
		DAZMorph M(Atom a, int i) => ((DAZCharacterSelector)a.GetStorableByID("geometry")).morphsControlUI.GetMorphByDisplayName(morphs[i]);
		h.Near(M(f1, 0).morphValue, 0f, "standing straight: hips morph 0");
		Pose(f1, 0.5f, 0.0f, 0.15f, 0.5f);          // chest 90 degrees forward of the hip-to-knee line
		h.Step(5f);
		h.Near(M(f1, 0).morphValue, 0.5f, "bent hips: Doggy Style Shape reaches rate .714 x intensity .7 = .5", 0.02f);
		h.Near(M(f1, 1).morphValue, 0.5f, "...Hourglass Waist the same (same defaults)", 0.02f);
		var kneeRate = (float)(2 * Math.Atan(0.15 / 0.5) * 180 / Math.PI);   // 33.4 degrees: below the 40 start of the range
		h.Near(M(f1, 3).morphValue, 0f, "legs closed (" + kneeRate.ToString("0") + " deg, below the range): Thights Indent 0");
		Pose(f1, 0.5f, 0.0f, 0.3f, 0.5f);           // 61.9 degrees between the knees
		h.Step(5f);
		h.Near(M(f1, 3).morphValue, ((float)(2 * Math.Atan(0.6) * 180 / Math.PI) - 40f) / 70f * 0.7f, "legs spread 62 deg: Thights Indent = (62-40)/70 x .7", 0.02f);
		h.Near(M(f2, 0).morphValue, 0f, "the other woman is not touched by her pose (the .var shared its tables)");

		// ---- the slot controls
		var act = host.GetBoolJSONParam("Hips Morph #0 Enable Morph");
		h.Check(act != null && host.GetStringChooserJSONParam("Hips Morph #0 Select Morph").val == morphs[0] && host.GetFloatJSONParam("Hips Morph #0 Intensity Morph").val == 0.7f, "controls named like the .var's, with its defaults");
		act.val = false;
		h.Step(0.1f);
		h.Near(M(f1, 0).morphValue, 0f, "Enable Morph off: the morph goes back to 0");
		h.Step(1f);
		h.Near(M(f1, 0).morphValue, 0f, "...and stays there");
		act.val = true;
		host.GetFloatJSONParam("Hips Morph #0 Intensity Morph").val = 0.35f;
		h.Step(4f);
		h.Near(M(f1, 0).morphValue, 0.25f, "Intensity Morph .35 -> half the value", 0.02f);
		host.CallAction("Hips Morph #0 Set Default");
		h.Check(Math.Abs(host.GetFloatJSONParam("Hips Morph #0 Intensity Morph").val - 0.7f) < 1e-4f, "Set Default puts the setting's value back");
		var dbg = host.GetBoolJSONParam("Hips Morph #2 Test Morph"); var mon = host.GetFloatJSONParam("Hips Morph #2 Monitor & Test Morph");
		dbg.val = true; mon.val = 0.8f;
		h.Step(4f);
		h.Near(M(f1, 2).morphValue, 0.8f, "Test Morph drives the morph by the test value", 0.02f);
		dbg.val = false; h.Step(4f);
		h.Check(M(f1, 2).morphValue < 0.8f - 0.1f, "...and leaves it when switched off");
		var sel = host.GetStringChooserJSONParam("Legs Morph #2 Select Morph");
		sel.val = morphs[4];
		h.Step(5f);
		h.Check(M(f1, 4).morphValue > 0.05f, "a free slot can take another morph (" + M(f1, 4).morphValue.ToString("0.###") + ")");
		sel.val = "None";
		h.Near(M(f1, 4).morphValue, 0f, "...and going back to None restores the morphs it moved");
		h.Set("Hips Morph #0", "IntensityMorph", "0.2");
		h.Check(Math.Abs(host.GetFloatJSONParam("Hips Morph #0 Intensity Morph").val - 0.2f) < 1e-4f, "changing the setting in the config changes the live slot");
		h.Set("Hips Morph #0", "IntensityMorph", "0.7");
		h.Set("Debug", "ShowDebug", "true");
		h.Step(0.2f);
		h.Check(UnityEngine.Object.FindObjectsOfType<LineRenderer>().Length >= 5, "Show Debug draws the five lines");
		h.Check(UnityEngine.GameObject.Find("AutoFlutterHip debug hips") != null, "...and the points");
		h.Set("Debug", "ShowDebug", "false");
		h.Step(0.2f);
		h.Check(UnityEngine.Object.FindObjectsOfType<LineRenderer>().Length == 0, "...and removes them");

		// ---- the technic player
		h.Check(host.GetStringJSONParam("Tab 00 Preset").val == "Alpha" && host.GetStringJSONParam("Tab 01 Preset").val == "Beta" && host.GetStringJSONParam("Tab 02 Preset").val == "Empty" && host.GetStringJSONParam("Tab 03 Preset").val == "", "tabs fill from the library in name order ('Beta (1)' copies skipped)");
		h.Check(Math.Abs(host.GetFloatJSONParam("Tab 00 Repetition").val - 1f) < 1e-4f && host.GetBoolJSONParam("Tab 00 Sync Other Atoms").val, "the tab shows the preset's settings");
		var hip1 = SimWorld.Body(f1, "hip"); var hip2 = SimWorld.Body(f2, "hip");
		host.CallAction("Play If Not Playing [03]");
		h.Step(0.5f);
		h.Check(hip1.SimForceCalls == 0, "an empty tab does nothing (no spin forever)");
		host.GetBoolJSONParam("Debug Log").val = true;
		host.CallAction("Play Forced [01]");
		h.Step(0.3f);
		h.Check(hip1.SimForceCalls > 5 && hip1.SimForceSum.z != 0f && hip1.SimTorqueSum == Vector3.zero, "Beta: moves along +Z only (" + hip1.SimForceCalls + " pushes)");
		h.Check(host.GetStringJSONParam("Technic Status").val.Contains("Beta"), "status says what plays: " + host.GetStringJSONParam("Technic Status").val);
		h.Step(1.5f);
		int calls = hip1.SimForceCalls;
		h.Step(0.5f);
		h.Check(hip1.SimForceCalls == calls && host.GetStringJSONParam("Technic Status").val.StartsWith("Stopped"), "it ends after its 1 s and reports Stopped");
		h.Check(hip2.SimForceCalls == 0, "Beta has Sync off: the other woman is left alone");

		hip1.SimForceCalls = 0; hip1.SimForceSum = Vector3.zero; hip1.SimTorqueSum = Vector3.zero;
		host.CallAction("Play Forced [00]");
		h.Step(0.6f);
		h.Check(hip1.SimTorqueCalls > 5 && hip1.SimTorqueSum.x != 0f && hip1.SimForceSum.y != 0f, "Alpha: torque about X and force along Y");
		h.Check(hip2.SimForceCalls > 3, "Alpha has Sync on: the other woman plays it in time (" + hip2.SimForceCalls + " pushes)");
		host.CallAction("Play If Not Playing [01]");
		h.Step(0.2f);
		h.Check(host.GetStringJSONParam("Technic Status").val.Contains("Alpha"), "Play If Not Playing keeps the one that is running");
		host.CallAction("Stop If Playing");
		h.Step(0.2f);
		calls = hip1.SimForceCalls; int calls2 = hip2.SimForceCalls;
		h.Step(0.5f);
		h.Check(hip1.SimForceCalls == calls && hip2.SimForceCalls == calls2, "Stop If Playing stops both (sync)");
		h.Near(hip1.SimTorqueSum.y, 0f, "...no stray sideways torque");

		// the sequence: slot 1 (2 x 1 s) then slot 2 (1 s)
		hip1.SimForceCalls = 0; hip1.SimTorqueSum = Vector3.zero;
		host.CallAction("Play Forced [00]");
		h.Step(3.8f);
		h.Check(hip1.SimTorqueSum.z != 0f, "slot 2 of Alpha (torque about Z) comes after slot 1");
		h.Step(0.5f);
		calls = hip1.SimTorqueCalls; h.Step(0.4f);
		h.Check(hip1.SimTorqueCalls == calls, "Alpha ends after its 3 s of slots");
		host.GetBoolJSONParam("Tab 00 Endless Technic").val = true;
		host.CallAction("Play Forced [00]");
		h.Step(8f);
		calls = hip1.SimTorqueCalls; h.Step(0.3f);
		h.Check(hip1.SimTorqueCalls > calls, "Endless Technic keeps going");
		host.GetBoolJSONParam("Tab 00 Endless Technic").val = false;
		host.CallAction("Stop If Playing");
		h.Step(0.2f);

		// ---- save a tab, reload
		host.CallAction("Tab 01 Save");
		h.Check(SuperController.SimSaved.ContainsKey(Girl + "/Technic - Beta.json") && SuperController.SimSaved[Girl + "/Technic - Beta.json"].Contains("slotlist"), "Tab 01 Save writes 'Technic - Beta.json' in the .var's format");
		h.Set("Technic", "Tab03", "Beta");
		h.Step(0.3f);
		h.Check(host.GetStringJSONParam("Tab 03 Preset").val == "Beta", "a tab setting names a preset");
		h.Set("Technic", "Tab04", "nothing here");
		h.Step(0.3f);
		h.Check(h.LogHas("preset 'nothing here' was not found"), "an unknown preset is reported, not fatal");

		// ---- atoms coming and going, scene loads
		host.CallAction("Play Forced [00]");
		h.Step(0.3f);
		f1.Remove();
		h.Step(1.5f);
		h.Check(!h.Sc.atoms.Contains(f1), "(the woman who was playing was removed in mid-technic)");
		h.Check(f2.GetStorableByID("ZeroT.AutoFlutterHip") != null, "the other woman keeps working");
		h.Load(() => { Woman(h, "NewGirl", morphs); h.Person("NewMale", true); });
		h.Step(2f);
		var ng = h.Sc.GetAtomByUid("NewGirl");
		h.Check(ng != null && ng.GetStorableByID("ZeroT.AutoFlutterHip") != null, "after a scene load the new woman gets the controls");
		Pose(ng, 0.5f, 0.0f, 0.3f, 0.5f);
		h.Step(5f);
		h.Check(((DAZCharacterSelector)ng.GetStorableByID("geometry")).morphsControlUI.GetMorphByDisplayName(morphs[0]).morphValue > 0.3f, "...and her morphs work at once");
		var lone = h.Sc.GetAtomByUid("NewMale");
		h.Check(lone.GetStorableByID("ZeroT.AutoFlutterHip") == null, "the new man gets nothing");

		// a person whose body is not there yet (still loading) must not throw
		var late = h.Person("Late");
		var hipLate = SimWorld.Body(late, "chest"); hipLate.name = "chestX";
		h.Step(2f);
		h.Check(h.LogHas("were not all found"), "missing body parts: told once, then waits quietly");
		hipLate.name = "chest";
		h.Step(1f);

		// ---- switching off
		Pose(ng, 0.5f, 0.0f, 0.3f, 0.5f);
		h.Set("General", "Active", "false");
		h.Step(0.5f);
		h.Near(((DAZCharacterSelector)ng.GetStorableByID("geometry")).morphsControlUI.GetMorphByDisplayName(morphs[0]).morphValue, 0f, "Active = false: the morphs go back to 0");
		h.Check(ng.GetStorableByID("ZeroT.AutoFlutterHip") == null, "...and the controls are removed");
		h.Set("General", "Active", "true");
		h.Step(3f);
		h.Check(ng.GetStorableByID("ZeroT.AutoFlutterHip") != null, "...and back on again");
	}
}
