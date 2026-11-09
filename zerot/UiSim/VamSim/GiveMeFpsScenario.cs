using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// GiveMeFPS replays its preset every few seconds while it is On: something else changes a setting back, the plugin puts it right again.
public static class GiveMeFpsScenario
{
	[Scenario("givemefps", "ZeroT.GiveMeFPSStandalone")]
	public static void Replay(Harness h)
	{
		var w = h.Person("Woman");
		var mr = new GameObject("MirrorRender"); mr.transform.SetParent(w.transform, false);
		var s = mr.AddComponent<JSONStorable>(); s.storeId = "MirrorRender"; s.containingAtom = w;
		var px = new JSONStorableBool("disablePixelLights", false); s.RegisterBool(px);
		var aa = new JSONStorableStringChooser("antiAliasing", new List<string> { "1", "2", "4", "8" }, "8", "AA"); s.RegisterStringChooser(aa);
		w.storables["MirrorRender"] = s;
		h.Start();
		h.Step(8f);
		h.Check(px.val && aa.val == "1", "the Full preset is applied after startup (pixel lights off, AA " + aa.val + ")");
		px.val = false; aa.val = "8";
		h.Step(7f);
		h.Check(px.val && aa.val == "1", "something changed the settings back: the plugin replays the preset on its own (" + px.val + ", " + aa.val + ")");
		h.Set("General", "ReapplyIntervalSeconds", "0");
		px.val = false;
		h.Step(10f);
		h.Check(!px.val, "ReapplyIntervalSeconds = 0: no replay");
		h.Set("General", "ReapplyIntervalSeconds", "2");
		h.Step(4f);
		h.Check(px.val, "set to 2 s: replaying again");
		h.Set("General", "Active", "false");
		px.val = false;
		h.Step(6f);
		h.Check(!px.val, "switched Off: it leaves the settings alone");
		h.Check(!Sim.Errors.Any(e => e.Contains("GiveMeFPS")), "the plugin threw nothing");
	}
}
