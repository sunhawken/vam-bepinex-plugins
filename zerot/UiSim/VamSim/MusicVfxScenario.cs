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
	}

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

		// _Music box
		Spawn(p, "music"); h.Step(1f);
		var music = at("_Music");
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

		// _Audio box (partner _Music is there)
		var spawnsBefore = h.Sc.SimAddLog.Count;
		Spawn(p, "slider"); h.Step(1f);
		var slider = at("_AudioS");
		h.Check(slider != null && slider.type == "UISlider", "the _Audio box spawns the UISlider _AudioS");
		h.Check(Loaded.Contains("_AudioS: Custom/Atom/UISlider/Preset_Audio.vap"), "...with Custom/Atom/UISlider/Preset_Audio.vap");
		h.Check(h.Sc.SimAddLog.Count - spawnsBefore == 1, "...and only that one atom, _Music is already there");

		// _Audio without _Music: the partner comes first
		h.Sc.RemoveAtom(slider); h.Sc.RemoveAtom(music); h.Step(0.2f);
		h.Sc.SimAddLog.Clear();
		Spawn(p, "slider"); h.Step(2f);
		h.Check(h.Sc.SimAddLog.SequenceEqual(new[] { "add AudioSource _Music", "add UISlider _AudioS" }), "_Audio with no _Music spawns _Music first, then _AudioS (" + string.Join(", ", h.Sc.SimAddLog) + ")");

		// _LightVFX box
		Spawn(p, "light"); h.Step(1f);
		var light = at("_LightVFX");
		h.Check(light != null && light.type == "InvisibleLight", "the _LightVFX box spawns the InvisibleLight _LightVFX");
		h.Check(Loaded.Contains("_LightVFX: Custom/Atom/InvisibleLight/Preset_SpiralLight.vap"), "...with Preset_SpiralLight.vap");
		h.Check(Plugins(light).SequenceEqual(new[] { "Dragontales.Light_Atom_Excluder.12:/Custom/Scripts/Light Atom Excluder/Light Atom Excluder.cs" }), "...and adds Light Atom Excluder after the preset (" + string.Join(", ", Plugins(light)) + ")");

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
		h.Check(!Sim.Errors.Any(e => e.Contains("MusicVfx")), "the plugin threw nothing");
	}
}
