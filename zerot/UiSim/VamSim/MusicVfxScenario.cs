using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

// The seven UIAssist boxes as one plugin, on a fake scene: atoms are spawned the way UIAssist does (free name, AddAtomByType, preset through the atom's
// "Preset" storable, extra plugin through its plugin manager). The fake atoms give the spawned atom the storables VaM would: "Preset" (loads a .vap:
// registers its plugins), a plugin manager, a main controller; an AudioRouter storable with the real action names drives an AudioSource.
// BeatIt keeps its light links in a "LightLinks" JSON array and rebuilds its "LightLink n Light / Band" choosers from it on RestoreFromJSON
public sealed class FakeBeat : JSONStorable
{
	public readonly List<string[]> Links = new List<string[]>();   // light, band
	public int Sliders;
	public override SimpleJSON.JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
	{
		var jc = new SimpleJSON.JSONClass(); var arr = new SimpleJSON.JSONArray();
		foreach (var l in Links) { var o = new SimpleJSON.JSONClass(); o["light"] = l[0]; o["band"] = l[1]; o["enabled"] = "True"; o["intensity"] = "2.5"; arr.Add(o); }
		jc["LightLinks"] = arr;
		var sarr = new SimpleJSON.JSONArray();
		for (int i = 0; i < Sliders; i++) { var o = new SimpleJSON.JSONClass(); o["atom"] = "None"; o["band"] = "None"; sarr.Add(o); }
		jc["SliderLinks"] = sarr;
		var en = GetBoolJSONParam("enabled"); if (en != null) jc["enabled"] = en.val ? "true" : "false";
		return jc;
	}
	public override void RestoreFromJSON(SimpleJSON.JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, SimpleJSON.JSONArray presetAtoms = null, bool setMissingToDefault = true)
	{
		if (jc == null) return;
		if (jc.HasKey("enabled")) { var en = GetBoolJSONParam("enabled"); if (en != null) en.val = jc["enabled"].Value == "true"; }
		if (jc.HasKey("SliderLinks"))
		{
			Sliders = jc["SliderLinks"].AsArray.Count;
			for (int i = 1; i <= Sliders; i++) { RegisterStringChooser(new JSONStorableStringChooser("SliderLink " + i + " Atom", new List<string> { "None" }, "None", "Atom")); RegisterBool(new JSONStorableBool("SliderLink " + i + " Enabled", true)); }
		}
		if (!jc.HasKey("LightLinks")) return;
		Links.Clear();
		var arr = jc["LightLinks"].AsArray;
		var lights = new List<string> { "None" };
		foreach (var a in SuperController.singleton.GetAtoms()) if (a.type == "InvisibleLight") lights.Add(a.uid);
		var bands = new List<string> { "None" }; for (int i = 1; i <= 8; i++) bands.Add("Band " + i);
		for (int i = 0; i < arr.Count; i++)
		{
			Links.Add(new[] { arr[i]["light"].Value, arr[i]["band"].Value });
			RegisterStringChooser(new JSONStorableStringChooser("LightLink " + (i + 1) + " Light", lights, lights.Contains(arr[i]["light"].Value) ? arr[i]["light"].Value : "None", "Light"));
			RegisterStringChooser(new JSONStorableStringChooser("LightLink " + (i + 1) + " Band", bands, arr[i]["band"].Value, "Band"));
		}
	}
}

public static class MusicVfxScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static readonly List<string> Loaded = new List<string>();   // "<atom>: <preset path>" in load order

	private static void Dress(Atom a)
	{
		var go = a.gameObject;
		var ctl = new GameObject("control"); ctl.transform.SetParent(go.transform, false);
		var fc = ctl.AddComponent<FreeControllerV3>(); fc.name = "control"; fc.control = fc.transform; fc.containingAtom = a;
		a.mainController = fc; a.freeControllers = new[] { fc };
		var mgrGo = new GameObject("PluginManager"); mgrGo.transform.SetParent(go.transform, false);
		var mgr = mgrGo.AddComponent<MVRPluginManager>(); mgr.storeId = "PluginManager"; mgr.containingAtom = a;
		a.storables["PluginManager"] = mgr;
		var presetGo = new GameObject("Preset"); presetGo.transform.SetParent(go.transform, false);
		var preset = presetGo.AddComponent<JSONStorable>(); preset.storeId = "Preset"; preset.containingAtom = a;
		var url = new JSONStorableUrl("presetBrowsePath", ""); preset.RegisterUrl(url);
		preset.RegisterBool(new JSONStorableBool("loadPresetOnSelect", true));
		preset.RegisterAction(new JSONStorableAction("LoadPreset", () => OnLoadPreset(a, mgr, url.val)));
		a.storables["Preset"] = preset;
		if (a.type == "AudioSource") a.gameObject.AddComponent<AudioSource>();
	}

	private static void OnLoadPreset(Atom a, MVRPluginManager mgr, string path)
	{
		Loaded.Add(a.uid + ": " + path);
		if (a.type != "AudioSource") return;
		// what the preset's AudioRouter plugin registers
		var rgo = new GameObject("plugin#0_VamEssentials.AudioRouter"); rgo.transform.SetParent(a.transform, false);
		var r = rgo.AddComponent<JSONStorable>(); r.storeId = "plugin#0_VamEssentials.AudioRouter"; r.containingAtom = a;
		var en = new JSONStorableBool("enabled", false); r.RegisterBool(en);
		var mode = new JSONStorableStringChooser("Mode", new List<string> { "PlayNext", "PlayNow", "PlayIfClear" }, "PlayIfClear", "Mode"); r.RegisterStringChooser(mode);
		var file = new JSONStorableString("Audio File", ""); r.RegisterString(file);
		var src = a.GetComponentInChildren<AudioSource>(true);
		r.RegisterAction(new JSONStorableAction("PlayRandom [UIList]", () => { if (en.val && mode.val == "PlayNow") { src.isPlaying = true; file.val = "Custom/Sounds/me/STAY BACK.mp3"; } }));
		r.RegisterAction(new JSONStorableAction("Stop [UIList]", () => { src.isPlaying = false; }));
		a.storables[r.storeId] = r;
		r.RegisterAction(new JSONStorableAction("PlayPrevious [UIList]", () => Calls.Add("prev")));
		r.RegisterAction(new JSONStorableAction("PlayNext [UIList]", () => Calls.Add("next")));
		foreach (var id in new[] { "plugin#1_everlaster.AudioScrubber", "plugin#3_HAL9001.BeatIt", "plugin#2_HAL9001.BeatIt" })
		{
			var g = new GameObject(id); g.transform.SetParent(a.transform, false);
			JSONStorable s = id.Contains("BeatIt") ? (JSONStorable)g.AddComponent<FakeBeat>() : g.AddComponent<JSONStorable>();
			s.storeId = id; s.containingAtom = a; s.RegisterBool(new JSONStorableBool("enabled", true)); a.storables[id] = s;
		}
	}
	private static readonly List<string> Calls = new List<string>();

	private static JSONStorable FlyPlugin(Atom a)
	{
		var g = new GameObject("plugin#0_Feel.AutoFlyObject"); g.transform.SetParent(a.transform, false);
		var s = g.AddComponent<JSONStorable>(); s.storeId = "plugin#0_Feel.AutoFlyObject"; s.containingAtom = a;
		s.RegisterBool(new JSONStorableBool("enabled", true)); s.RegisterBool(new JSONStorableBool("Move Toggle", true)); s.RegisterFloat(new JSONStorableFloat("Fly Speed", 3f, 1f, 5f, true));
		a.storables[s.storeId] = s; return s;
	}

	private static void visibleRowsSet(object p, int n) { p.GetType().GetField("visibleRows", All).SetValue(p, n); }
	private static object Spec(BepInEx.BaseUnityPlugin p, string field) => p.GetType().GetField(field, All).GetValue(p);
	private static void Spawn(BepInEx.BaseUnityPlugin p, string field) => p.GetType().GetMethod("Spawn", All).Invoke(p, new[] { Spec(p, field) });
	private static int Delete(BepInEx.BaseUnityPlugin p, string field) => (int)p.GetType().GetMethod("DeleteAtoms", All).Invoke(p, new[] { Spec(p, field) });
	private static void Toggle(BepInEx.BaseUnityPlugin p) => p.GetType().GetMethod("MusicToggle", All).Invoke(p, null);
	private static string Msg(BepInEx.BaseUnityPlugin p) => (string)p.GetType().GetField("message", All).GetValue(p);
	private static List<string> Plugins(Atom a) => ((MVRPluginManager)a.GetStorableByID("PluginManager")).plugins.Select(x => ((JSONStorableString)x.pluginURLJSON).val).ToList();

	[Scenario("musicvfx", "ZeroT.MusicVfxBoxes")]
	public static void Boxes(Harness h)
	{
		Loaded.Clear();
		if (UnityEngine.EventSystems.EventSystem.current == null) UnityEngine.EventSystems.EventSystem.current = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
		string root = Path.Combine(Path.GetTempPath(), "vamsim_musicvfx_" + Environment.ProcessId);
		if (Directory.Exists(root)) Directory.Delete(root, true);
		foreach (var d in new[] { "Custom/Atom/AudioSource", "Custom/Atom/UISlider", "Custom/Atom/InvisibleLight", "AddonPackages/resources", "AddonPackages/keep" }) Directory.CreateDirectory(Path.Combine(root, d));
		File.WriteAllText(Path.Combine(root, "Custom/Atom/AudioSource/Preset_Audio.vap"), "{\"storables\":[{\"id\":\"PluginManager\",\"plugins\":{\"plugin#0\":\"VamEssentials.AudioRouter.15:/Custom/Scripts/a.cslist\",\"plugin#1\":\"Missing.Package.1:/Custom/Scripts/b.cs\"}}]}");
		File.WriteAllText(Path.Combine(root, "Custom/Atom/UISlider/Preset_Audio.vap"), "{\"storables\":[]}");
		File.WriteAllText(Path.Combine(root, "Custom/Atom/InvisibleLight/Preset_SpiralLight.vap"), "{\"storables\":[]}");
		foreach (var v in new[] { "resources/VamEssentials.AudioRouter.16.var", "resources/VamEssentials.AudioRouter.9.var", "keep/Feel.AutoFlyObject.6.var", "resources/Dragontales.Light_Atom_Excluder.12.var" })
			File.WriteAllBytes(Path.Combine(root, "AddonPackages", v), new byte[0]);
		Paths.GameRootPath = root;
		h.Sc.onAtomAddedHandlers += a => { if (a.uid != "CoreControl") Dress(a); };
		var person = h.Person("Woman");

		h.Start();
		h.Step(0.5f);
		var p = h.Plugin;
		Func<string, Atom> at = n => h.Sc.GetAtomByUid(n);

		// nothing spawned yet: the toggle explains instead of failing
		Toggle(p);
		h.Check(Msg(p).Contains("_Music"), "Music On/Off without a _Music atom says so (" + Msg(p) + ")");

		// _Music box: its AudioScrubber looks _AudioS up when it loads, so the slider comes first
		Spawn(p, "music"); h.Step(3f);
		var music = at("_Music");
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "add UISlider _AudioS", "add AudioSource _Music" }), "the _Music box spawns _AudioS first, then _Music (" + string.Join(", ", h.Sc.SimAddLog) + ")");
		h.Check(music != null && music.type == "AudioSource", "the _Music box spawns the AudioSource atom _Music");
		h.Check(Loaded.Contains("_Music: Custom/Atom/AudioSource/Preset_Audio.vap"), "...and loads Custom/Atom/AudioSource/Preset_Audio.vap through its Preset storable (" + string.Join(" | ", Loaded) + ")");
		h.Check(Msg(p).Contains("Missing.Package"), "...and reports the package its preset needs that is not installed (" + Msg(p) + ")");
		h.Check(!Msg(p).Contains("VamEssentials"), "...but not one that is installed in another version");

		// Music On / Off
		Toggle(p); h.Step(0.2f);
		var src = music.GetComponentInChildren<AudioSource>(true);
		var router = music.GetStorableByID("plugin#0_VamEssentials.AudioRouter");
		h.Check(src.isPlaying, "Music Off -> On: AudioRouter 'PlayRandom [UIList]' runs and the music plays");
		h.Check(router.GetStringChooserJSONParam("Mode").val == "PlayNow" && router.GetBoolJSONParam("enabled").val, "...with enabled = true and Mode = PlayNow, as the box sets them");
		Toggle(p); h.Step(0.2f);
		h.Check(!src.isPlaying, "Music On -> Off: 'Stop [UIList]' stops it");

		// _Audio box (the first one: it needs nothing)
		h.Sc.RemoveAtom(at("_AudioS")); h.Step(0.2f);
		var spawnsBefore = h.Sc.SimAddLog.Count;
		Spawn(p, "slider"); h.Step(1f);
		var slider = at("_AudioS");
		h.Check(slider != null && slider.type == "UISlider", "the _Audio box spawns the UISlider _AudioS");
		h.Check(Loaded.Contains("_AudioS: Custom/Atom/UISlider/Preset_Audio.vap"), "...with Custom/Atom/UISlider/Preset_Audio.vap");
		h.Check(h.Sc.SimAddLog.Count - spawnsBefore == 1, "...and only that one atom");

		// _Music with no _AudioS: the slider comes first (and the music atom is renewed, so its BeatIt starts unlinked again)
		h.Sc.RemoveAtom(slider); h.Sc.RemoveAtom(at("_Music")); h.Step(0.2f);
		h.Sc.SimAddLog.Clear();
		Spawn(p, "music"); h.Step(3f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "add UISlider _AudioS", "add AudioSource _Music" }), "_Music with no _AudioS spawns _AudioS first, then _Music (" + string.Join(", ", h.Sc.SimAddLog) + ")");

		// _LightVFX box
		Spawn(p, "light"); h.Step(1f);
		var light = at("_LightVFX");
		h.Check(light != null && light.type == "InvisibleLight", "the _LightVFX box spawns the InvisibleLight _LightVFX");
		h.Check(Loaded.Contains("_LightVFX: Custom/Atom/InvisibleLight/Preset_SpiralLight.vap"), "...with Preset_SpiralLight.vap");
		h.Check(Plugins(light).SequenceEqual(new[] { "Dragontales.Light_Atom_Excluder.12:/Custom/Scripts/Light Atom Excluder/Light Atom Excluder.cs" }), "...and adds Light Atom Excluder after the preset (" + string.Join(", ", Plugins(light)) + ")");

		// BeatIt <-> _LightVFX: every BeatIt plugin on _Music gets a light link to the light, once, and its band moves around
		var mus = at("_Music");
		var beats = new[] { "plugin#2_HAL9001.BeatIt", "plugin#3_HAL9001.BeatIt" }.Select(id => (FakeBeat)mus.GetStorableByID(id)).ToArray();
		h.Check(beats.All(b => b.Links.Count == 1 && b.Links[0][0] == "_LightVFX" && b.GetStringChooserJSONParam("LightLink 1 Light").val == "_LightVFX"), "spawning _LightVFX links both BeatIt plugins of _Music to it (" + string.Join(" | ", beats.Select(b => string.Join(",", b.Links.Select(l => l[0] + "/" + l[1])))) + ")");
		h.Check(Msg(p).Contains("BeatIt linked"), "...and says so (" + Msg(p) + ")");
		p.GetType().GetMethod("LinkBeat", All).Invoke(p, null);
		h.Check(beats.All(b => b.Links.Count == 1), "linking again adds nothing");
		h.Set("Wiring", "BeatRandomBandSeconds", "1");
		var seen = new HashSet<string>();
		for (int i = 0; i < 12; i++) { h.Step(1.05f); seen.Add(beats[1].GetStringChooserJSONParam("LightLink 1 Band").val); }
		h.Check(seen.Count >= 3 && seen.All(b => b.StartsWith("Band ")), "the link's band jumps about at random (" + string.Join(", ", seen) + ")");
		h.Set("Wiring", "BeatRandomBandSeconds", "0");
		var held = beats[1].GetStringChooserJSONParam("LightLink 1 Band").val;
		h.Step(4f);
		h.Check(beats[1].GetStringChooserJSONParam("LightLink 1 Band").val == held, "BeatRandomBandSeconds = 0 keeps the band");
		h.Set("Wiring", "BeatRandomBandSeconds", "5");

		// FlyObject box
		h.Sc.SimAddLog.Clear();
		Spawn(p, "fly"); h.Step(2f);
		var fly = at("FlyObject");
		h.Check(fly != null && fly.type == "Empty", "the FlyObject box spawns the Empty atom FlyObject");
		h.Check(Plugins(fly).SequenceEqual(new[] { "Feel.AutoFlyObject.6:/Custom/Scripts/Feel/AutoFlyObject.cslist" }), "...with AutoFlyObject, the .5 of the UIAssist box resolved to the newest installed .6 (" + string.Join(", ", Plugins(fly)) + ")");
		h.Check(!h.Sc.SimAddLog.Any(l => l.Contains("InvisibleLight")), "...and no second light, _LightVFX is already there");
		var firstFly = fly;
		h.Sc.SimAddLog.Clear();
		Spawn(p, "fly"); h.Step(2f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "remove FlyObject", "add Empty FlyObject" }), "pressing it again deletes the old FlyObject first and spawns a fresh one under the same name (" + string.Join(", ", h.Sc.SimAddLog) + ")");
		h.Check(at("FlyObject") != null && at("FlyObject") != firstFly, "...a new atom");

		// individual plugin controls
		music = at("_Music");   // the one respawned as partner above
		var rowsM = (System.Collections.IList)p.GetType().GetMethod("RowsFor", All).Invoke(p, new object[] { music });
		Func<object, string> label = r => (string)r.GetType().GetField("Label").GetValue(r);
		h.Check(rowsM.Count == 4 && label(rowsM[0]) == "AudioRouter" && label(rowsM[1]) == "AudioScrubber" && label(rowsM[2]) == "BeatIt 1" && label(rowsM[3]) == "BeatIt 2", "the plugin list of _Music: AudioRouter, AudioScrubber, BeatIt 1, BeatIt 2 in plugin order (" + string.Join(", ", rowsM.Cast<object>().Select(label)) + ")");
		var mt = p.GetType().GetMethod("Toggle", All, null, new[] { rowsM[2].GetType() }, null);
		var beat1 = music.GetStorableByID("plugin#2_HAL9001.BeatIt");
		mt.Invoke(p, new[] { rowsM[2] });
		h.Check(!beat1.GetBoolJSONParam("enabled").val, "a plugin's On / Off button switches that plugin's own 'enabled'");
		mt.Invoke(p, new[] { rowsM[2] });
		h.Check(beat1.GetBoolJSONParam("enabled").val, "...and back");
		p.GetType().GetField("viewAtom", All).SetValue(p, 1);   // 0 _AudioS, 1 _Music, 2 _LightVFX, 3 FlyObject
		p.GetType().GetMethod("SetAll", All).Invoke(p, new object[] { false });
		h.Check(new[] { "plugin#0_VamEssentials.AudioRouter", "plugin#1_everlaster.AudioScrubber", "plugin#2_HAL9001.BeatIt", "plugin#3_HAL9001.BeatIt" }.All(id => !music.GetStorableByID(id).GetBoolJSONParam("enabled").val), "All off switches every plugin on the atom in view");
		p.GetType().GetMethod("SetAll", All).Invoke(p, new object[] { true });
		var ex = p.GetType().GetMethod("Extra", All);
		Calls.Clear(); ex.Invoke(p, new[] { rowsM[0], (object)0 }); ex.Invoke(p, new[] { rowsM[0], (object)1 });
		h.Check(Calls.SequenceEqual(new[] { "prev", "next" }), "the AudioRouter row's Prev / Next play the previous / next song");
		var flyAtom = at("FlyObject"); var fp = FlyPlugin(flyAtom);
		var rowsF = (System.Collections.IList)p.GetType().GetMethod("RowsFor", All).Invoke(p, new object[] { flyAtom });
		ex.Invoke(p, new[] { rowsF[0], (object)0 });
		h.Check(!fp.GetBoolJSONParam("Move Toggle").val, "the AutoFlyObject row's first button switches Move Toggle");
		ex.Invoke(p, new[] { rowsF[0], (object)1 });
		h.Near(fp.GetFloatJSONParam("Fly Speed").val, 3.5f, "...and the second raises Fly Speed by 0.5");

		// settings editor of one plugin (the AutoFlyObject on FlyObject)
		var T = p.GetType();
		Func<string, object> pf = n => T.GetField(n, All).GetValue(p);
		var cellMain = T.GetMethod("CellMain", All); var cell = T.GetMethod("Cell", All);
		var detailF = T.GetField("detailRow", All); var viewF = T.GetField("viewAtom", All);
		viewF.SetValue(p, 3);
		T.GetMethod("Refresh", All).Invoke(p, null);
		var rowFly = ((System.Collections.IList)T.GetMethod("RowsFor", All).Invoke(p, new object[] { flyAtom }))[0];
		detailF.SetValue(p, rowFly);
		T.GetMethod("Refresh", All).Invoke(p, null);
		var prs = (System.Collections.IList)pf("paramRows");
		Func<string, int> slot = n => { var prs2 = (System.Collections.IList)pf("paramRows"); for (int i = 0; i < prs2.Count; i++) if ((string)prs2[i].GetType().GetField("Name").GetValue(prs2[i]) == n) return i; return -1; };
		h.Check(prs.Count == 3 && slot("Fly Speed") >= 0 && slot("Move Toggle") >= 0 && slot("enabled") >= 0, "the editor lists every setting of the plugin (" + prs.Count + ")");
		// the row slots depend on the page: put the wanted param on slot 0 by page index
		Action<string, int, bool> press = (name, which, main) =>
		{
			int idx = slot(name); T.GetField("page", All).SetValue(p, idx / 8); int s = idx % 8;
			visibleRowsSet(p, 8);
			if (main) cellMain.Invoke(p, new object[] { s }); else cell.Invoke(p, new object[] { s, which });
		};
		var speed = fp.GetFloatJSONParam("Fly Speed"); speed.val = 3f;
		press("Fly Speed", 3, false);   // > coarse: +10% of the 1..5 range
		h.Near(speed.val, 3.4f, "a float's '>>' adds a tenth of its range", 0.001f);
		press("Fly Speed", 2, false);
		h.Near(speed.val, 3.44f, "...'>' a hundredth", 0.001f);
		press("Fly Speed", 0, false); press("Fly Speed", 1, false);
		h.Near(speed.val, 3.0f, "...'<<' and '<' take them back", 0.001f);
		for (int i = 0; i < 20; i++) press("Fly Speed", 3, false);
		h.Near(speed.val, 5f, "...and a constrained float stops at its maximum", 0.001f);
		press("Fly Speed", 0, true);
		h.Near(speed.val, 3f, "the label resets a float to its default", 0.001f);
		fp.GetBoolJSONParam("Move Toggle").val = true;
		press("Move Toggle", 0, true);
		h.Check(!fp.GetBoolJSONParam("Move Toggle").val, "a switch's label toggles it");
		viewF.SetValue(p, 1); detailF.SetValue(p, null);
		var beat = ((System.Collections.IList)T.GetMethod("RowsFor", All).Invoke(p, new object[] { music }))[2];
		var bs = beat.GetType().GetField("Storable").GetValue(beat) as JSONStorable;
		var mode = new JSONStorableStringChooser("Preset", new List<string> { "a", "b", "c" }, "a", "Preset"); bs.RegisterStringChooser(mode);
		detailF.SetValue(p, beat); T.GetMethod("Refresh", All).Invoke(p, null);
		press("Preset", 2, false);
		h.Check(mode.val == "b", "a choice's '>' picks the next choice");
		press("Preset", 1, false); press("Preset", 1, false);
		h.Check(mode.val == "c", "...and '<' wraps round to the last");
		detailF.SetValue(p, null); viewF.SetValue(p, 1);

		// Delete Fly Objects
		h.Check(Delete(p, "fly") == 1 && at("FlyObject") == null, "Delete Fly Objects removes FlyObject");
		h.Check(at("_LightVFX") != null && at("_Music") != null, "...and nothing else");
		h.Check(Delete(p, "fly") == 0, "...and again with nothing to delete does nothing");

		// FlyObject with no light: the light is spawned first (AutoFlyObject looks for the name _LightVFX at start)
		h.Sc.RemoveAtom(at("_LightVFX")); h.Step(0.2f);
		h.Sc.SimAddLog.Clear();
		Spawn(p, "fly"); h.Step(2f);
		h.Check(h.Sc.SimAddLog.Contains("add InvisibleLight _LightVFX") && h.Sc.SimAddLog.Last() == "add Empty FlyObject", "FlyObject with no _LightVFX spawns the light first (" + string.Join(", ", h.Sc.SimAddLog) + ")");

		// partner spawning switched off
		h.Set("Wiring", "SpawnMissingPartners", "false");
		h.Sc.RemoveAtom(at("_LightVFX")); h.Step(0.2f);
		h.Sc.SimAddLog.Clear();
		Spawn(p, "fly"); h.Step(2f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "remove FlyObject", "add Empty FlyObject" }), "SpawnMissingPartners=false: only the FlyObject (" + string.Join(", ", h.Sc.SimAddLog) + ")");

		// Spawn all 1-4: in the working order, from nothing
		h.Set("Wiring", "SpawnMissingPartners", "true");
		foreach (var a in h.Sc.atoms.Where(a => a.type != "Person" && a.uid != "CoreControl").ToArray()) h.Sc.RemoveAtom(a);
		h.Step(0.3f);
		h.Sc.SimAddLog.Clear();
		p.GetType().GetMethod("SpawnEverything", All).Invoke(p, null); h.Step(6f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "add UISlider _AudioS", "add AudioSource _Music", "add InvisibleLight _LightVFX", "add Empty FlyObject" }), "Spawn all goes _Audio, _Music, _LightVFX, FlyObject (" + string.Join(", ", h.Sc.SimAddLog) + ")");
		var mus2 = at("_Music");
		h.Check(new[] { "plugin#2_HAL9001.BeatIt", "plugin#3_HAL9001.BeatIt" }.All(id => ((FakeBeat)mus2.GetStorableByID(id)).Links.Count == 1), "...and BeatIt is linked to the light at the end");

		// Delete all / Restore all: the atoms go, then come back in order with their plugin settings
		var beatA = (FakeBeat)at("_Music").GetStorableByID("plugin#2_HAL9001.BeatIt");
		beatA.GetBoolJSONParam("enabled").val = false;
		beatA.Links[0][1] = "Band 7";
		var T2 = p.GetType();
		T2.GetMethod("DeleteEverything", All).Invoke(p, null); h.Step(0.3f);
		h.Check(new[] { "_AudioS", "_Music", "_LightVFX", "FlyObject" }.All(n => at(n) == null), "Delete all removes all four atoms (" + Msg(p) + ")");
		h.Sc.SimAddLog.Clear();
		T2.GetMethod("RestoreEverything", All).Invoke(p, null); h.Step(12f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "add UISlider _AudioS", "add AudioSource _Music", "add InvisibleLight _LightVFX", "add Empty FlyObject" }), "Restore all brings them back in the working order (" + string.Join(", ", h.Sc.SimAddLog) + ")");
		var beatB = (FakeBeat)at("_Music").GetStorableByID("plugin#2_HAL9001.BeatIt");
		h.Check(beatB != beatA && !beatB.GetBoolJSONParam("enabled").val, "...with the plugin settings they had (BeatIt 1 still switched off)");
		h.Check(beatB.Links.Count == 1 && beatB.Links[0][1] == "Band 7", "...and the BeatIt light link as it was (" + string.Join(",", beatB.Links[0]) + ")");
		T2.GetMethod("RestoreEverything", All).Invoke(p, null);
		h.Check(Msg(p).Contains("Nothing to restore"), "a second Restore all has nothing to do");

		// the BeatIt panel: switch, what it links to, add / remove links
		var Tb = p.GetType();
		Tb.GetMethod("OpenBeatPanel", All).Invoke(p, null);
		var pr2 = (System.Collections.IList)Tb.GetField("paramRows", All).GetValue(p);
		Func<int, string> nm = i => (string)pr2[i].GetType().GetField("Name").GetValue(pr2[i]);
		var names2 = Enumerable.Range(0, pr2.Count).Select(nm).ToList();
		h.Check(names2.Take(5).All(n => n.StartsWith("+") || n.StartsWith("-") || n.StartsWith("Refresh")) && names2.Contains("enabled") && names2.Contains("LightLink 1 Light") && names2.Contains("LightLink 1 Band"), "the BeatIt panel lists the link actions, the switch and the light link settings (" + string.Join(", ", names2) + ")");
		Func<string, int, bool, bool> pressB = (name, which, main) =>
		{
			var pr3 = (System.Collections.IList)Tb.GetField("paramRows", All).GetValue(p);
			int idx = -1; for (int i = 0; i < pr3.Count; i++) if ((string)pr3[i].GetType().GetField("Name").GetValue(pr3[i]) == name) idx = i;
			if (idx < 0) return false;
			Tb.GetField("page", All).SetValue(p, idx / 8); visibleRowsSet(p, 8);
			if (main) Tb.GetMethod("CellMain", All).Invoke(p, new object[] { idx % 8 }); else Tb.GetMethod("Cell", All).Invoke(p, new object[] { idx % 8, which });
			return true;
		};
		var panelRow = Tb.GetField("detailRow", All).GetValue(p);
		var panelStore = (FakeBeat)panelRow.GetType().GetField("Storable").GetValue(panelRow);
		var wasOn = panelStore.GetBoolJSONParam("enabled").val;
		pressB("enabled", 0, true);
		h.Check(panelStore.GetBoolJSONParam("enabled").val != wasOn, "the panel's switch turns that BeatIt on / off");
		pressB("enabled", 0, true);
		var bandBefore = panelStore.GetStringChooserJSONParam("LightLink 1 Band").val;
		pressB("LightLink 1 Band", 2, false);
		h.Check(panelStore.GetStringChooserJSONParam("LightLink 1 Band").val != bandBefore, "'>' on a link's Band picks the next band (" + bandBefore + " -> " + panelStore.GetStringChooserJSONParam("LightLink 1 Band").val + ")");
		var linksBefore = panelStore.Links.Count;
		pressB("+ Add light link", 0, true);
		h.Check(panelStore.Links.Count == linksBefore + 1 && panelStore.Links[linksBefore][0] == "_LightVFX", "'+ Add light link' adds a link pointing at _LightVFX");
		pressB("- Remove last light link", 0, true);
		h.Check(panelStore.Links.Count == linksBefore, "'- Remove last light link' takes it away again");
		pressB("+ Add slider link", 0, true);
		h.Check(panelStore.Sliders == 1 && panelStore.GetStringChooserJSONParam("SliderLink 1 Atom") != null, "'+ Add slider link' adds a slider link with its settings");
		pressB("- Remove last slider link", 0, true);
		h.Check(panelStore.Sliders == 0, "...and removes it");
		Tb.GetMethod("StepAtom", All).Invoke(p, new object[] { 1 });
		var panelRow2 = Tb.GetField("detailRow", All).GetValue(p);
		h.Check(panelRow2 != panelRow && (string)panelRow2.GetType().GetField("Label").GetValue(panelRow2) != null && ((string)panelRow2.GetType().GetField("Label").GetValue(panelRow2)).StartsWith("BeatIt"), "'>' in the panel steps to the other BeatIt plugin");
		Tb.GetField("detailRow", All).SetValue(p, null); Tb.GetField("beatMode", All).SetValue(p, false);
		h.Check(!Sim.Errors.Any(e => e.Contains("MusicVfx")), "the plugin threw nothing");
	}
}
