using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// The Rigify VR+ node HUD: the REAL merged script (RigifyVRPlus.cs with the ZeroT HUD and the Skynet.Rigify.7 changes, built by zerot/RigifyHud/build_rigify_hud.py
// and compiled against VaM's own assemblies into in/targets/RigifyVRPlusHud.dll) runs on a female Person. The HUD's rows are clicked like the laser would,
// and the node groups' real storables must follow; snapshots of the canvas are written next to the outputs of the other HUD scenarios.
public static class RigifyHudScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static Button Row(string label)
	{
		foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>()) if (b.gameObject.name == "Row_" + label) return b;
		return null;
	}
	private static Button Named(string name)
	{
		foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>()) if (b.gameObject.name == name) return b;
		return null;
	}
	private static string State(Button row) { return row.transform.Find("State").GetComponent<Text>().text; }
	private static Color Back(Button row) { return row.GetComponent<Image>().color; }

	// the headset camera rig the script looks up (centerCameraTarget.targetCamera)
	private static CameraTarget Rig(Harness h)
	{
		Camera.main = new GameObject("Main Camera").AddComponent<Camera>();
		var ct = new GameObject("CenterCameraTarget").AddComponent<CameraTarget>();
		ct.targetCamera = Camera.main;
		h.Sc.centerCameraTarget = ct;
		return ct;
	}

	private static MVRScript Load(Harness h, out Atom w)
	{
		var asm = h.LoadTarget("RigifyVRPlusHud");
		var t = asm.GetType("Skynet.Rigify", true);
		w = h.Person("Woman");
		var s = SceneHost.Spawn(t, w, w.gameObject, "plugin#0", "LightningBunny.RigifyVRPlus.4:/Custom/Scripts/Skynet/Rigify/RigifyVRPlus.cs");
		s.Init();
		return s;
	}

	private static bool Have()
	{
		string dll = Path.Combine(Harness.CoresDir, "RigifyVRPlusHud.dll");
		if (File.Exists(dll)) return true;
		Console.WriteLine("  (skipped: build it first: python zerot/RigifyHud/build_rigify_hud.py, then compile the merged script into in/targets/RigifyVRPlusHud.dll)");
		return false;
	}

	[Scenario("rigifyhud_vr", "ZeroT.ZajeShuffleHelper")]
	public static void HudVr(Harness h)
	{
		if (!Have()) return;
		var ct = Rig(h);
		ct.transform.position = new Vector3(0f, 1.6f, 0f);
		h.Sc.isOVR = true;
		Atom w; var s = Load(h, out w);
		h.Step(1f);
		var canvas = UnityEngine.Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.gameObject.name == "RigifyHUD_Canvas");
		h.Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "in VR the HUD is a world-space canvas");
		h.Check(canvas.transform.parent == null, "...free-floating, not stuck to the head");
		float d = (canvas.transform.position - ct.transform.position).magnitude;
		h.Check(d > 0.5f && d < 1.6f, "...about a metre in front of the headset (" + d.ToString("0.00") + " m)");
		h.Check(Named("Btn_BRING") != null && Named("Btn_PIN") != null, "the VR header has BRING and PIN buttons");
		var pin = Named("Btn_PIN"); var pinTxt = pin.transform.Find("Text").GetComponent<Text>();
		pin.onClick.Invoke();
		h.Check(s.GetBoolJSONParam("HUD Pin To View (VR)").val && pinTxt.text == "PINNED", "PIN pins it to the view (label PINNED)");
		var before = canvas.transform.position - ct.transform.position;
		ct.transform.position = new Vector3(2f, 1.6f, 1f); h.Step(0.3f);
		var after = canvas.transform.position - ct.transform.position;
		h.Check((after - before).magnitude < 0.05f, "pinned: the HUD travels with the head (offset " + before.ToString("0.00") + " -> " + after.ToString("0.00") + ")");
		pin.onClick.Invoke();
		var rest = canvas.transform.position;
		ct.transform.position = new Vector3(-1f, 1.6f, 3f); h.Step(0.3f);
		h.Check(!s.GetBoolJSONParam("HUD Pin To View (VR)").val && (canvas.transform.position - rest).magnitude < 0.01f && pinTxt.text == "PIN", "unpinned: it stays where it is in the room");
		Named("Btn_BRING").onClick.Invoke();
		h.Check((canvas.transform.position - ct.transform.position).magnitude < 1.6f && (canvas.transform.position - ct.transform.position).magnitude > 0.5f, "BRING puts it back in front of the head");
		// laser drag: the panel follows the ray
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_vr_default.png"), Screen.width, Screen.height);
		var rows = Row("LEFT ARM"); rows.onClick.Invoke();
		h.Check(State(rows) == "IK", "a laser click on a row works in VR too");
		Row("LEFT LEG").onClick.Invoke(); Row("LEFT LEG").onClick.Invoke(); Row("LOWER BODY").onClick.Invoke(); Row("SYMMETRY").onClick.Invoke();
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_vr_mixed.png"), Screen.width, Screen.height);
		Named("Btn_ALL OFF").onClick.Invoke();
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_vr_alloff.png"), Screen.width, Screen.height);
		h.Check(!Sim.Errors.Any(e => e.Contains("Rigify")), "no exceptions from the script (" + string.Join(" | ", Sim.Errors.Where(e => e.Contains("Rigify")).Take(2)) + ")");
	}

	[Scenario("rigifyhud", "ZeroT.ZajeShuffleHelper")]
	public static void Hud(Harness h)
	{
		if (!Have()) return;
		Rig(h);
		Atom w;
		var s = Load(h, out w);
		var desk = s.GetBoolJSONParam("HUD Show On Desktop"); if (desk != null) desk.val = true;   // no VR headset here: the desktop version of the same panel
		h.Step(1f);
		h.Check(!Sim.Errors.Any(e => e.Contains("Rigify")), "the real script initialises without errors (" + string.Join(" | ", Sim.Errors.Where(e => e.Contains("Rigify")).Take(2)) + ")");
		string[] labels = { "NODES", "LOWER BODY", "UPPER BODY", "LEFT ARM", "RIGHT ARM", "LEFT LEG", "RIGHT LEG", "SYMMETRY" };
		var rows = labels.Select(Row).ToArray();
		h.Check(rows.All(r => r != null), "the HUD has its eight rows: " + string.Join(", ", labels));
		if (rows.Any(r => r == null)) return;
		Func<string, JSONStorableBool> b = n => s.GetBoolJSONParam(n);
		Func<int, string> st = i => State(rows[i]);
		h.Check(st(0) == "ON" && st(1) == "ON" && st(2) == "ON" && st(3) == "FK" && st(6) == "FK" && st(7) == "OFF", "at start: nodes ON, every group ON, limbs FK, symmetry OFF (" + string.Join(" ", Enumerable.Range(0, 8).Select(st)) + ")");
		h.Check(Back(rows[0]).g > Back(rows[0]).r, "on rows are green");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_default.png"), Screen.width, Screen.height);

		// a limb row cycles OFF -> FK -> IK; IK also switches the FK nodes off for that limb, as the screen buttons do
		rows[3].onClick.Invoke();
		h.Check(st(3) == "IK" && b("Left Arm IK").val && b("Left Arm Node").val, "LEFT ARM: FK -> IK (the plugin's own Left Arm IK switch is on) (" + st(3) + ")");
		h.Check(Back(rows[3]).r > Back(rows[3]).b && Back(rows[3]).g > 0.4f, "...and the row turns amber");
		rows[3].onClick.Invoke();
		h.Check(st(3) == "OFF" && !b("Left Arm Node").val && !b("Left Arm IK").val, "...IK -> OFF (hidden)");
		rows[3].onClick.Invoke();
		h.Check(st(3) == "FK" && b("Left Arm Node").val && !b("Left Arm IK").val, "...OFF -> FK");
		rows[5].onClick.Invoke(); rows[5].onClick.Invoke();
		h.Check(st(5) == "OFF", "LEFT LEG cycles the same way (" + st(5) + ")");

		// groups
		rows[1].onClick.Invoke();
		h.Check(st(1) == "OFF" && !b("Torso Node").val, "LOWER BODY switches the torso / hip nodes off");
		rows[2].onClick.Invoke();
		h.Check(st(2) == "OFF" && !b("Upper Body Node").val, "UPPER BODY switches the chest / head nodes off");
		rows[7].onClick.Invoke();
		h.Check(st(7) == "ON" && b("Real-time Symmetry").val, "SYMMETRY switches real-time symmetry");
		rows[7].onClick.Invoke();

		// master
		rows[0].onClick.Invoke();
		h.Check(st(0) == "OFF" && !b("Enabled").val, "NODES off = the plugin's Enabled switch (Shift+R) is off");
		h.Step(0.8f);
		h.Check(Row("NODES") != null && st(0) == "OFF", "the HUD stays on screen while the node visuals are off");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_mixed.png"), Screen.width, Screen.height);
		rows[0].onClick.Invoke();

		// the HUD follows changes made elsewhere (Shift+E, the screen buttons, the plugin UI, a scene load)
		b("Right Arm IK").val = true; b("Right Leg Node").val = false;
		h.Step(0.8f);
		h.Check(st(4) == "IK" && st(6) == "OFF", "a change made elsewhere shows up on the HUD (R ARM " + st(4) + ", R LEG " + st(6) + ")");

		// ALL ON / ALL OFF
		Named("Btn_ALL OFF").onClick.Invoke();
		h.Check(Enumerable.Range(1, 6).All(i => st(i) == "OFF") && st(0) == "ON", "ALL OFF hides every node group and leaves the master switch alone");
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_alloff.png"), Screen.width, Screen.height);
		Named("Btn_ALL ON").onClick.Invoke();
		h.Check(Enumerable.Range(1, 6).All(i => st(i) == (i >= 3 ? "FK" : "ON")), "ALL ON puts them all back (limbs as FK)");
		var size = s.GetFloatJSONParam("HUD Scale"); float s0 = size.val;
		Named("Btn_SIZE +").onClick.Invoke();
		h.Check(Math.Abs(size.val - (s0 + 0.1f)) < 1e-4f, "SIZE + grows the HUD by a tenth (" + size.val + ")");
		Named("Btn_SIZE -").onClick.Invoke();

		// storables for scenes / triggers / the voice bridge
		h.Check(s.GetBoolJSONParam("HUD Show") != null && s.GetBoolJSONParam("HUD Pin To View (VR)") != null && s.GetFloatJSONParam("HUD Scale") != null && s.GetStringChooserJSONParam("HUD Attach To (VR)") != null, "the HUD settings are scene storables (show, pin, scale, attach)");
		s.GetBoolJSONParam("HUD Show").val = false;
		h.Step(0.3f);
		h.Check(!Row("NODES").gameObject.activeInHierarchy, "'HUD Show' off hides the panel");
		s.GetBoolJSONParam("HUD Show").val = true;
		h.Step(0.3f);
		SimSnapshot.Png(Path.Combine(Harness.CoresDir, "..", "..", "out", "RigifyHud_final.png"), Screen.width, Screen.height);
		h.Check(!Sim.Errors.Any(e => e.Contains("Rigify")), "no exceptions from the script (" + string.Join(" | ", Sim.Errors.Where(e => e.Contains("Rigify")).Take(2)) + ")");
	}
}
