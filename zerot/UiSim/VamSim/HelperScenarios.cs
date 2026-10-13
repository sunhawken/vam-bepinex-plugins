using System.IO;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Scenarios for the Harmony helper plugins (DecalMaker / BodyLanguage / UIAssist helpers).
// The code they patch lives in VaM script packages, so `vamdev.py simtargets` compiles the real scripts into
// UiSim/in/cores/*Scripts.dll; the scenario loads that assembly into the simulator, starts the helper (real Harmony
// does the patching), then runs the PATCHED methods and checks what changed.
public static class HelperScenarios
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static Type T(Assembly a, string name) => a.GetType(name, true);
	private static FieldInfo Field(Type t, string n) { for (; t != null; t = t.BaseType) { var f = t.GetField(n, All | BindingFlags.DeclaredOnly); if (f != null) return f; } return null; }
	private static PropertyInfo Prop(Type t, string n) { for (; t != null; t = t.BaseType) { var p = t.GetProperty(n, All | BindingFlags.DeclaredOnly); if (p != null) return p; } return null; }
	private static MethodInfo Method(Type t, string n) { for (; t != null; t = t.BaseType) { var m = t.GetMethod(n, All | BindingFlags.DeclaredOnly); if (m != null) return m; } return null; }
	private static object Get(object o, string name) { var f = Field(o.GetType(), name); if (f != null) return f.GetValue(o); return Prop(o.GetType(), name).GetValue(o); }
	private static void Set(object o, string name, object v) { var f = Field(o.GetType(), name); if (f != null) { f.SetValue(o, v); return; } Prop(o.GetType(), name).SetValue(o, v); }
	private static object Call(object o, string method, params object[] a) { var m = Method(o.GetType(), method); try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; } }
	private static object CallStatic(Type t, string method, params object[] a) { var m = Method(t, method); try { return m.Invoke(null, a); } catch (TargetInvocationException e) { throw e.InnerException; } }

	[Scenario("decalmakerhelper", "ZeroT.DecalMakerHelper")]
	public static void DecalMaker(Harness h)
	{
		h.Unpatch("com.zerot.decalmaker.helper");
		var asm = h.LoadTarget("DecalMakerScripts");
		var settings = T(asm, "VAM_Decal_Maker_2.Helpers.PluginSettings");
		string slot = (string)T(asm, "VAM_Decal_Maker_2.Utility.MatSlotEnum").GetField("DecalTex").GetRawConstantValue();
		BepInEx.Configuration.ConfigFile.Overrides["Memory.MaxRenderResolution"] = "1024";

		// before the helper: DecalMaker asks for 8x MSAA temporaries and leaks the alpha-clip one
		RenderTexture.ResetCounters();
		CallStatic(settings, "GetRenderTextureFromMatSlot", slot, true);
		h.Check(RenderTexture.TemporaryAntiAliasing.Last() == 8, "baseline: DecalMaker requests 8x MSAA temporaries (" + RenderTexture.TemporaryAntiAliasing.Last() + ")");
		var go0 = new GameObject("decal0");
		var baseRender = T(asm, "VAM_Decal_Maker_2.Render.DecalRender");
		var dr0 = go0.AddComponent(baseRender);
		Set(dr0, "MaterialSlot", slot);
		RenderTexture.ResetCounters();
		Call(dr0, "AlphaClip", new object[] { null });
		h.Check(RenderTexture.Released == 1 && RenderTexture.TemporaryReleased == 0, "baseline: alpha-clip releases a pooled temporary the wrong way");

		h.Start();
		h.Step(3f);
		h.Check(h.LogHas("DecalMaker patched"), "helper reports the patches were applied");

		// 1. no MSAA on temporaries
		RenderTexture.ResetCounters();
		CallStatic(settings, "GetRenderTextureFromMatSlot", slot, true);
		h.Check(RenderTexture.TemporaryAntiAliasing.Last() == 1, "temporary render textures now use no MSAA (" + RenderTexture.TemporaryAntiAliasing.Last() + ")");
		// 2. alpha-clip leak
		var dr = new GameObject("decal1").AddComponent(baseRender);
		Set(dr, "MaterialSlot", slot);
		RenderTexture.ResetCounters();
		Call(dr, "AlphaClip", new object[] { null });
		h.Check(RenderTexture.TemporaryReleased == 1 && RenderTexture.Released == 0, "alpha-clip now releases its temporary correctly");
		// 5. resolution cap (config)
		h.Check((int)CallStatic(settings, "ToInt", "4096") == 1024, "MaxRenderResolution=1024 caps a 4096 setting");
		h.Check((int)CallStatic(settings, "ToInt", "512") == 512, "values below the cap are untouched");

		// frames list for the apply tests
		var dmFrames = new GameObject("frames").AddComponent(T(asm, "VAM_Decal_Maker_2.Data.DMFrames"));
		var frameType = T(asm, "VAM_Decal_Maker_2.Data.DMFrame");
		var dmListType = T(asm, "VAM_Decal_Maker_2.Data.DMList`1").MakeGenericType(frameType);
		var frames = Activator.CreateInstance(dmListType);
		Set(dmFrames, "frames", frames);
		Set(dr, "_DMFrames", dmFrames);
		// 3. throttle: a texture that keeps changing re-renders at most every 0.1 s
		Set(dr, "_isDirty", true);
		Call(dr, "Update");
		h.Check(!(bool)Get(dr, "_isDirty"), "first change renders immediately");
		Set(dr, "_isDirty", true);
		Call(dr, "Update");
		h.Check((bool)Get(dr, "_isDirty"), "a second change right after is held back (throttled)");
		h.Step(0.2f);
		Call(dr, "Update");
		h.Check(!(bool)Get(dr, "_isDirty"), "...and rendered once the interval has passed");
		// 4. an apply that throws no longer leaves the renderer stuck
		((IList)frames).Add(null);
		Set(dr, "_isDirty", true);
		Set(dr, "_Processing", false);
		h.Step(0.3f);
		try { Call(dr, "Update"); } catch (Exception) { }
		h.Check(!(bool)Get(dr, "_Processing"), "an exception inside apply no longer leaves the renderer 'processing' forever");
	}

	private static object HelperHooks(Harness h) => h.Asm.GetType("ZeroT.BodyLanguageHelper.Hooks", true);
	private static object HookField(Harness h, string name) => HelperHooks(h) is Type t ? t.GetField(name, All).GetValue(null) : null;

	[Scenario("bodylanguagehelper", "ZeroT.BodyLanguageHelper")]
	public static void BodyLanguage(Harness h)
	{
		h.Unpatch("com.zerot.bodylanguage.helper");
		var asm = h.LoadTarget("BodyLanguageScripts");
		var blType = T(asm, "CheesyFX.BodyLanguage_Lite");
		var f1 = h.Person("Female1");
		var bl = new GameObject("bl").AddComponent(blType);
		Set(bl, "containingAtom", f1);
		h.Start();
		h.Step(3f);
		h.Check(h.LogHas("BodyLanguage patched"), "helper reports the patches were applied");

		// 1. remembered name checks: same answers, but the second call comes from the cache
		float r1 = (float)Call(bl, "ReceiverRadius", "LNipple"); float r2 = (float)Call(bl, "ReceiverRadius", "LNipple");
		h.Near(r1, 0.055f, "ReceiverRadius(nipple) unchanged");
		h.Check(r1 == r2 && ((System.Collections.IDictionary)HookField(h, "radius")).Count == 1, "...and cached after the first call");
		h.Near((float)Call(bl, "ReceiverWeight", "LNipple"), 1.25f, "ReceiverWeight(nipple) unchanged");
		h.Check((bool)Call(bl, "IsSameLimbOrNearbyDefaultPose", "lHand", "lForearm"), "same-limb test still true for hand vs forearm");
		h.Check(!(bool)Call(bl, "IsSameLimbOrNearbyDefaultPose", "lHand", "rThigh"), "...and false for different limbs");
		int limbEntries = 0; foreach (System.Collections.DictionaryEntry de in (System.Collections.IDictionary)HookField(h, "limb")) limbEntries += ((System.Collections.IDictionary)de.Value).Count;
		h.Check(limbEntries == 2, "...both answers cached (" + limbEntries + " entries)");
		var tokens = (string[])blType.GetField("StimulatorNames", All).GetValue(null);
		bool m1 = (bool)Call(bl, "MatchesAny", tokens[0], tokens); bool m2 = (bool)Call(bl, "MatchesAny", tokens[0], tokens);
		h.Check(m1 && m2, "token match still finds a stimulator name");
		// 2. own-atom check cached
		var rb = SimWorld.Body(f1, "chest"); var other = h.Person("Female2"); var rb2 = SimWorld.Body(other, "chest");
		h.Check((bool)Call(bl, "IsRigidbodyFromContainingAtom", rb) && !(bool)Call(bl, "IsRigidbodyFromContainingAtom", rb2), "own-atom check gives the right answers");
		h.Check(((System.Collections.IDictionary)HookField(h, "contain")).Count == 1, "...and caches them per instance");
		// 3. far fewer automatic body-pair rebuilds, but an immediate one when atoms change
		h.Near((float)Call(bl, "EffectivePairRebuildInterval"), 60f, "automatic rebuild interval raised from 10 s to 60 s");
		Set(bl, "lastPairRebuild", 99f);
		h.Person("Female3");
		h.Step(3f);
		h.Check((float)Get(bl, "lastPairRebuild") == 0f, "adding a person triggers a body-pair rebuild right away");
		// 4. ArousalHUD update rate limit
		var hooks = (Type)HelperHooks(h);
		var hudPrefix = hooks.GetMethod("HudPrefix");
		var dummy = new object();
		bool a = (bool)hudPrefix.Invoke(null, new object[] { dummy }); bool b = (bool)hudPrefix.Invoke(null, new object[] { dummy });
		h.Step(0.06f);
		bool c = (bool)hudPrefix.Invoke(null, new object[] { dummy });
		h.Check(a && !b && c, "ArousalHUD update: first allowed, immediate repeat skipped, allowed again after 50 ms");
		var hudType = T(asm, "ArousalHUD");
		var info = HarmonyLib.Harmony.GetPatchInfo(hudType.GetMethod("Update", All));
		h.Check(info != null && info.Prefixes.Count == 1, "ArousalHUD.Update carries the rate-limit patch");
	}

	[Scenario("uiassisthelper", "ZeroT.UIAssistHelper")]
	public static void UIAssist(Harness h)
	{
		h.Unpatch("com.zerot.uiassist.helper");
		var asm = h.LoadTarget("UIAssistScripts");
		var gameUi = T(asm, "JayJayWon.GameControlUI");
		var clothing = T(asm, "JayJayWon.ActiveClothingList");
		var target = T(asm, "JayJayWon.TargetControl");
		var heel = T(asm, "JayJayWon.HeelAdjustTool");

		// baseline: errors escape the per-frame updates
		bool threw = false;
		try { CallStatic(gameUi, "Update"); } catch (Exception) { threw = true; }
		h.Check(threw, "baseline: GameControlUI.Update throws on a half-built HUD");
		var dict = (System.Collections.IDictionary)clothing.GetField("DAZCharSelectorsByAtom", All).GetValue(null);
		var person = h.Person("Female1");
		dict[person] = null;                         // a person whose character selector vanished
		((System.Collections.IDictionary)clothing.GetField("genderByAtom", All).GetValue(null))[person] = DAZCharacterSelector.Gender.Female;
		threw = false;
		try { CallStatic(clothing, "Update"); } catch (Exception) { threw = true; }
		h.Check(threw, "baseline: ActiveClothingList.Update throws for that person");

		h.Start();
		h.Step(3f);
		h.Check(h.LogHas("UIAssist patched"), "helper reports the patches were applied");
		// contained errors
		threw = false;
		try { CallStatic(gameUi, "Update"); } catch (Exception) { threw = true; }
		h.Check(!threw, "HUD update error is contained");
		threw = false;
		try { CallStatic(clothing, "Update"); } catch (Exception) { threw = true; }
		h.Check(!threw, "clothing-list update error is contained");
		h.Check(h.LogHas("contained (logged once)"), "...and logged once to the BepInEx log");
		// throttles
		var hooks = h.Asm.GetType("ZeroT.UIAssistHelper.Hooks", true);
		bool g1 = (bool)hooks.GetMethod("GazePrefix").Invoke(null, null), g2 = (bool)hooks.GetMethod("GazePrefix").Invoke(null, null);
		h.Step(0.4f);
		bool g3 = (bool)hooks.GetMethod("GazePrefix").Invoke(null, null);
		h.Check(g1 && !g2 && g3, "gaze-target rebuild: allowed, immediate repeat skipped, allowed after 0.35 s (desktop)");
		h.Sc.isOVR = true;
		h.Step(0.2f);
		bool v1 = (bool)hooks.GetMethod("GazePrefix").Invoke(null, null);
		h.Step(0.2f);
		bool v2 = (bool)hooks.GetMethod("GazePrefix").Invoke(null, null);
		h.Check(v1 && v2, "VR keeps the faster 0.15 s interval");
		bool e1 = (bool)hooks.GetMethod("HeelPrefix").Invoke(null, null), e2 = (bool)hooks.GetMethod("HeelPrefix").Invoke(null, null);
		h.Step(5.1f);
		bool e3 = (bool)hooks.GetMethod("HeelPrefix").Invoke(null, null);
		h.Check(e1 && !e2 && e3, "Heel Adjust folder listing: once, repeat skipped, again after 5 s");
		var gaze = HarmonyLib.Harmony.GetPatchInfo(target.GetMethod("UpdateGazeTargets", All));
		h.Check(gaze != null && gaze.Prefixes.Count == 1, "TargetControl.UpdateGazeTargets carries the throttle");
		var hf = HarmonyLib.Harmony.GetPatchInfo(heel.GetMethod("UpdateExistingHAFiles", All));
		h.Check(hf != null && hf.Prefixes.Count == 1, "HeelAdjustTool.UpdateExistingHAFiles carries the throttle");
	}

	public static int FindCalls;
	public static readonly System.Collections.Generic.List<string> Writes = new System.Collections.Generic.List<string>();
	public static bool CountFind() { FindCalls++; return false; }
	public static bool CountWrite(string subPath) { Writes.Add(subPath); return false; }

	[Scenario("uxplushelper", "ZeroT.UXPlusHelper")]
	public static void UXPlus(Harness h)
	{
		h.Unpatch("com.zerot.uxplus.helper");
		var asm = h.LoadTarget("UXPlusScripts");
		var hover = T(asm, "everlaster.UX.ShowTargetOnHover.ShowTargetOnHover");
		var f1 = h.Person("Female1"); var f2 = h.Person("Female2");
		UserPreferences.singleton = new UserPreferences();
		var mod = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(hover);   // only OnUpdate is exercised: no module Awake/UI needed
		hover.GetField("_sc", All).SetValue(mod, h.Sc);
		var onUpdate = hover.GetMethod("OnUpdate", All);
		int total = 0; foreach (var a in h.Sc.atoms) total += a.freeControllers.Length;
		f1.freeControllers[3].highlighted = true;           // one controller under the mouse

		// baseline: UXPlus's own loop asks for the list and writes every controller, every frame
		for (int i = 0; i < 3; i++) onUpdate.Invoke(mod, null);
		int writes = 0; foreach (var a in h.Sc.atoms) foreach (var c in a.freeControllers) writes += c.hiddenWrites;
		h.Check(writes == 3 * total, "baseline: 3 frames = " + (3 * total) + " controller writes (got " + writes + ")");
		h.Check(h.Sc.allControllersCalls == 3, "baseline: the controller list is rebuilt every frame (3 calls)");
		h.Check(f1.freeControllers[3].hidden == false && f1.freeControllers[4].hidden == true, "baseline result: highlighted target visible, the rest hidden");



		// ---- Preview Camera Helper: baseline bug, then the fix
		var camState = T(asm, "everlaster.UX.PreviewCamHelper.AtomCameraState");
		Func<Atom> camPerson = () =>
		{
			var a = new GameObject("CamPerson").AddComponent<Atom>(); a.type = "Person"; a.uid = a.name = "CamPerson"; a.containingAtom = a;
			var eye = new GameObject("EyelidControl").AddComponent<JSONStorable>(); eye.RegisterBool(new JSONStorableBool("blinkEnabled", true)); a.storables["EyelidControl"] = eye;
			var ae = new GameObject("AutoExpressions").AddComponent<JSONStorable>(); ae.RegisterBool(new JSONStorableBool("enabled", true)); a.storables["AutoExpressions"] = ae;
			return a;
		};
		Func<Atom, bool> blink = a => a.GetStorableByID("EyelidControl").GetBoolJSONParam("blinkEnabled").val;
		var ca = camPerson();
		var st1 = Activator.CreateInstance(camState, new object[] { ca });
		camState.GetMethod("Enable").Invoke(st1, new object[] { true, true });
		camState.GetMethod("Enable").Invoke(st1, new object[] { true, true });     // preview + hi-res camera both switch on
		camState.GetMethod("Disable").Invoke(st1, null);
		bool baseBlinkStuck = !blink(ca);
		var cb = camPerson();
		var st2 = Activator.CreateInstance(camState, new object[] { cb });
		camState.GetMethod("Disable").Invoke(st2, null);                           // a disable nobody paused
		bool baseForced = !blink(cb);
		var camHelper = T(asm, "everlaster.UX.PreviewCamHelper.PreviewCamHelper");
		var camMod = Activator.CreateInstance(camHelper, true);
		var barePerson = new GameObject("Bare").AddComponent<Atom>(); barePerson.type = "Person"; barePerson.uid = "Bare";
		bool baseThrew = false;
		try { camHelper.GetMethod("OnAtomAdded", All).Invoke(camMod, new object[] { barePerson }); } catch (Exception) { baseThrew = true; }
		// ---- Download latest on startup, autosave: baseline numbers
		var hub = new MVR.Hub.HubDownloader(); MVR.Hub.HubDownloader.singleton = hub;
		var harm = new HarmonyLib.Harmony("test.uxplus");
		harm.Patch(typeof(MVR.Hub.HubDownloader).GetMethod("FindPackage"), new HarmonyLib.HarmonyMethod(typeof(HelperScenarios).GetMethod("CountFind", BindingFlags.Public | BindingFlags.Static)));
		harm.Patch(T(asm, "everlaster.FileUtils").GetMethod("WriteDataJSON"), new HarmonyLib.HarmonyMethod(typeof(HelperScenarios).GetMethod("CountWrite", BindingFlags.Public | BindingFlags.Static)));
		FindCalls = 0; Writes.Clear();
		var pluginT = T(asm, "everlaster.UX.AlternatePluginsList.Plugin");
		var urlPaths = T(asm, "UXPlus.AlternatePluginsList.UrlPaths");
		Func<string, object> mkPlugin = url =>
		{
			var paths = urlPaths.GetMethod("Parse").Invoke(null, new object[] { url, false });
			var pl = Activator.CreateInstance(pluginT, new object[] { paths });
			pluginT.GetMethod("SetAsFavorite").Invoke(pl, new object[] { true, "SESSION" });
			return pl;
		};
		var plug = mkPlugin("Someone.Thing.4:/Custom/Scripts/Someone/Thing.cs");
		var dl = pluginT.GetMethod("DownloadLatest");
		var alt = T(asm, "everlaster.UX.AlternatePluginsList.AlternatePluginsList");
		var altMod = Activator.CreateInstance(alt, true);
		var uxmod = T(asm, "everlaster.UX.UXModule");
		Field(uxmod, "useDataFile").SetValue(altMod, true); Field(uxmod, "initialized").SetValue(altMod, true); Field(uxmod, "_dataFile").SetValue(altMod, "AlternatePluginsList.json");
		var dict = (System.Collections.IDictionary)alt.GetField("pluginsDict").GetValue(altMod);


		// reset counters/state, start the helper
		foreach (var a in h.Sc.atoms) foreach (var c in a.freeControllers) { c.hiddenWrites = 0; c.hidden = false; c.hiddenWrites = 0; }
		h.Sc.allControllersCalls = 0;
		h.Start();
		h.Step(3f);
		h.Check(h.LogHas("UXPlus patched"), "helper reports the patches were applied");
		var info = HarmonyLib.Harmony.GetPatchInfo(onUpdate);
		h.Check(info != null && info.Prefixes.Count == 1, "ShowTargetOnHover.OnUpdate carries the optimisation");
		for (int i = 0; i < 3; i++) onUpdate.Invoke(mod, null);
		writes = 0; foreach (var a in h.Sc.atoms) foreach (var c in a.freeControllers) writes += c.hiddenWrites;
		h.Check(writes == total - 1, "patched: 3 frames write only what changed (" + (total - 1) + " controllers once, got " + writes + ")");
		h.Check(h.Sc.allControllersCalls == 1, "patched: the controller list is asked for once, not every frame (got " + h.Sc.allControllersCalls + ")");
		h.Check(f1.freeControllers[3].hidden == false && f1.freeControllers[4].hidden == true && f2.freeControllers[5].hidden == true, "patched result identical: highlighted visible, the rest hidden");

		// hover moves: the new target shows, the old one hides again, same frame
		f1.freeControllers[3].highlighted = false; f2.freeControllers[5].highlighted = true;
		onUpdate.Invoke(mod, null);
		h.Check(f1.freeControllers[3].hidden == true && f2.freeControllers[5].hidden == false, "hover change is applied immediately");
		// play mode / targets shown: untouched
		h.Sc.gameMode = SuperController.GameMode.Play;
		f2.freeControllers[5].highlighted = false;
		onUpdate.Invoke(mod, null);
		h.Check(f2.freeControllers[5].hidden == false, "Play mode: nothing is touched");
		h.Sc.gameMode = SuperController.GameMode.Edit;
		// hide-inactive-targets preference
		UserPreferences.singleton.hideInactiveTargets = true;
		f1.freeControllers[7].currentPositionState = FreeControllerV3.PositionState.Off; f1.freeControllers[7].currentRotationState = FreeControllerV3.RotationState.Off;
		onUpdate.Invoke(mod, null);
		h.Check(f1.freeControllers[7].hidden == true, "inactive target stays hidden when the preference says so");
		// a person removed: a stale controller in the cached list is skipped, no exception, list refreshed next frame
		h.Sc.RemoveAtom(f2);
		foreach (var c in f2.freeControllers) UnityEngine.Object.Destroy(c.gameObject);
		bool threw = false;
		try { onUpdate.Invoke(mod, null); onUpdate.Invoke(mod, null); } catch (Exception) { threw = true; }
		h.Check(!threw, "a removed person does not break the update");
		// ---- the helper is already running (started above); exercise it
		Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
		var checksFile = Path.Combine(BepInEx.Paths.ConfigPath, "ZeroT.UXPlusHelper.hubchecks.txt");
		var hooks2 = h.Asm.GetType("ZeroT.UXPlusHelper.Hooks", true);

		// camera fix
		var cc = camPerson();
		var st3 = Activator.CreateInstance(camState, new object[] { cc });
		camState.GetMethod("Enable").Invoke(st3, new object[] { true, true });
		camState.GetMethod("Enable").Invoke(st3, new object[] { true, true });
		camState.GetMethod("Disable").Invoke(st3, null);
		var cd = camPerson();
		var st4 = Activator.CreateInstance(camState, new object[] { cd });
		camState.GetMethod("Disable").Invoke(st4, null);
		h.Check(baseBlinkStuck, "baseline: after two camera enables, blink stays OFF after the screenshot (UXPlus bug)");
		h.Check(baseForced, "baseline: a disable nobody paused forces blink OFF");
		h.Check(blink(cc), "fixed: blink restored to its real value after two enables + disable");
		h.Check(blink(cd), "fixed: a disable without a pause leaves blink alone");
		h.Check(cc.GetStorableByID("AutoExpressions").GetBoolJSONParam("enabled").val, "fixed: auto expressions restored too");
		// contained handler errors
		bool threw2 = false;
		try { camHelper.GetMethod("OnAtomAdded", All).Invoke(camMod, new object[] { barePerson }); } catch (Exception) { threw2 = true; }
		h.Check(baseThrew && !threw2, "OnAtomAdded: baseline throws on a person with missing storables, helper contains it");
		h.Check(h.LogHas("error contained (logged once)"), "...and says so once in the log");

		// hub check throttle: startup check once, then skipped; a favorite click always goes through
		if (File.Exists(checksFile)) File.Delete(checksFile);
		hooks2.GetMethod("InitPrefix").Invoke(null, null);
		dl.Invoke(plug, null); dl.Invoke(plug, null);
		var other = mkPlugin("Else.Where.2:/Custom/Scripts/Else/Where.cs");
		dl.Invoke(other, null);
		hooks2.GetMethod("InitPostfix").Invoke(null, null);
		dl.Invoke(plug, null);
		h.Check(FindCalls == 3, "hub: startup asks once per plugin (2 plugins), repeat skipped, a favorite click still asks (got " + FindCalls + " calls)");
		h.Check(File.Exists(checksFile) && File.ReadAllText(checksFile).Contains("Someone.Thing.latest"), "hub: last-check times are remembered between runs");
		FindCalls = 0;
		hooks2.GetMethod("InitPrefix").Invoke(null, null);
		dl.Invoke(plug, null);
		hooks2.GetMethod("InitPostfix").Invoke(null, null);
		h.Check(FindCalls == 0, "hub: next startup within the window asks nobody");

		// autosave: first look only remembers; a change is written on the next look; no change = no write
		hooks2.GetMethod("Track").Invoke(null, new object[] { altMod });
		var tick = hooks2.GetMethod("AutosaveTick");
		Writes.Clear();
		tick.Invoke(null, null);
		h.Check(Writes.Count == 0, "autosave: nothing written for data that was just loaded");
		dict["Someone.Thing.latest:/Custom/Scripts/Someone/Thing.cs"] = plug;   // a favorite added during the session
		tick.Invoke(null, null);
		h.Check(Writes.Count == 1 && Writes[0] == "AlternatePluginsList.json", "autosave: a new favorite is written to AlternatePluginsList.json without waiting for exit (" + Writes.Count + " writes)");
		tick.Invoke(null, null); tick.Invoke(null, null);
		h.Check(Writes.Count == 1, "autosave: no further writes while nothing changes");
		pluginT.GetMethod("SetAsFavorite").Invoke(plug, new object[] { false, "SESSION" });
		tick.Invoke(null, null);
		h.Check(Writes.Count == 2, "autosave: un-favoriting is saved too");
	}

	private static System.Collections.IEnumerator FakeLoad(JSONStorableStringChooser c, System.Collections.Generic.List<string> seen) { seen.Add(c.val); yield return null; seen.Add(c.val); yield return null; }
	private static System.Collections.IEnumerator ThrowingLoad(JSONStorableStringChooser c) { yield return null; throw new InvalidOperationException("boom"); }

	[Scenario("appearanceloaderhelper", "ZeroT.AppearanceLoaderHelper")]
	public static void AppearanceLoaderHelper(Harness h)
	{
		h.Unpatch("com.zerot.appearanceloader.helper");
		var asm = h.LoadTarget("AppearanceLoaderScripts");
		var al = T(asm, "ky1001.AppearanceLoader");
		Func<object> makeLoader = () =>
		{
			var l = Activator.CreateInstance(al, true);
			Field(al, "missingItem").SetValue(l, new JSONStorableString("", ""));
			Field(al, "message").SetValue(l, new JSONStorableString("", ""));
			Field(al, "_searchMissingTextures_Bool").SetValue(l, new JSONStorableBool("search", false));
			Field(al, "loadDir").SetValue(l, "Saves/scene/");
			return l;
		};
		Func<object, string, object> call = (l, name) => al.GetMethod(name, All).Invoke(l, null);
		Func<object, string, string, object> call1 = (l, name, arg) => al.GetMethod(name, All).Invoke(l, new object[] { arg });

		// a small library: package Pack.Tex is installed at version 5 only; Other.Thing at 2
		MVR.FileManagementSecure.FileManagerSecure.SimReset();
		MVR.FileManagement.FileManager.SimPackageUids.Clear();
		MVR.FileManagement.FileManager.SimPackageUids.Add("Pack.Tex.5"); MVR.FileManagement.FileManager.SimPackageUids.Add("Other.Thing.2");
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Pack.Tex.5:/Textures/a.png");
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Custom/Atom/Person/Textures/a/x.png");
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Custom/Atom/Person/Textures/a/other.png");
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Custom/Atom/Person/Textures/b/c/x.png");
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Custom/Atom/Person/Textures/b/y.png");
		var fm = typeof(MVR.FileManagementSecure.FileManagerSecure);
		Action resetCounters = () => { MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls = 0; MVR.FileManagementSecure.FileManagerSecure.DirExistsCalls = 0; MVR.FileManagementSecure.FileManagerSecure.GetFilesCalls = 0; MVR.FileManagementSecure.FileManagerSecure.GetDirectoriesCalls = 0; };

		// ---- baseline (the loader as it is)
		var l0 = makeLoader();
		resetCounters();
		string v0 = (string)call1(l0, "checkVarDir", "Pack.Tex.latest:/Textures/");
		int dirCalls0 = MVR.FileManagementSecure.FileManagerSecure.DirExistsCalls;
		resetCounters();
		string p0 = (string)call1(l0, "checkPath", "Pack.Tex.3:/Textures/a.png");
		int fileCalls0 = MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls;
		resetCounters();
		string m0 = (string)call1(l0, "checkPath", "Gone.Pkg.9:/Textures/zz.png");
		int missCalls0 = MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls;
		((JSONStorableBool)Field(al, "_searchMissingTextures_Bool").GetValue(l0)).val = true;
		resetCounters();
		string s0a = (string)call1(l0, "checkPath", "Gone.Pkg.9:/Textures/x.png");
		string s0b = (string)call1(l0, "checkPath", "Gone.Pkg.9:/Textures/y.png");
		string s0c = (string)call1(l0, "checkPath", "Gone.Pkg.9:/Textures/nowhere.png");
		int listCalls0 = MVR.FileManagementSecure.FileManagerSecure.GetFilesCalls;
		h.Check(v0 == "Pack.Tex.5:/Textures/" && p0 == "Pack.Tex.5:/Textures/a.png" && m0 == "", "baseline results: newest installed version found, missing path -> empty");
		h.Check(dirCalls0 > 900 && fileCalls0 > 900 && missCalls0 > 900, "baseline: " + dirCalls0 + " / " + fileCalls0 + " / " + missCalls0 + " file-system questions for three paths");
		h.Check(s0a == "Custom/Atom/Person/Textures/a/x.png" && s0b == "Custom/Atom/Person/Textures/b/y.png" && s0c == "", "baseline search results: first match in folder order, empty when absent");

		// ---- an error in the middle of a load: sim stays off
		Func<Atom, JSONStorableBool> simOf = a => a.GetStorableByID("ClothA").GetBoolJSONParam("simEnabled");
		Func<object, Atom> prepareFailing = l =>
		{
			var person = h.Person("Person" + Guid.NewGuid().ToString("N").Substring(0, 4));
			var st = new GameObject("ClothA").AddComponent<JSONStorable>(); st.RegisterBool(new JSONStorableBool("simEnabled", true)); person.storables["ClothA"] = st;
			Field(al, "TargetAtom").SetValue(l, person);
			((System.Collections.Generic.List<string>)Field(al, "simClothingList").GetValue(l)).Add("ClothA");
			((System.Collections.Generic.Dictionary<string, bool>)Field(al, "simClothingDic").GetValue(l))["ClothA"] = true;
			simOf(person).val = false;   // what applyData does first
			return person;
		};
		var failer0 = makeLoader(); var pf0 = prepareFailing(failer0);
		var en0 = (System.Collections.IEnumerator)call(failer0, "applyData");
		bool threw0 = false;
		try { en0.MoveNext(); } catch (Exception) { threw0 = true; }
		h.Check(threw0 && !simOf(pf0).val, "baseline: an error escapes the load and the clothing sim stays OFF");

		// ---- waits as they are
		var m1 = makeLoader();
		var person1 = h.Person("MorphPerson");
		Field(al, "TargetAtom").SetValue(m1, person1);
		Field(al, "scale_Bool").SetValue(m1, new JSONStorableBool("scale", false));
		Field(al, "morphs_Bool").SetValue(m1, new JSONStorableBool("morphs", true));
		Field(al, "asArray").SetValue(m1, new SimpleJSON.JSONArray());
		Field(al, "idListVap").SetValue(m1, new System.Collections.Generic.List<string>());
		Func<object, float> firstWait = l =>
		{
			var en = (System.Collections.IEnumerator)call(l, "changeMorph");
			en.MoveNext();
			return ((WaitForSeconds)en.Current).seconds;
		};
		float morphWait0 = firstWait(m1);
		h.Near(morphWait0, 1f, "baseline: morph apply waits a fixed 1 s");

		// ---- start the helper
		h.Start();
		h.Step(3f);
		h.Check(h.LogHas("AppearanceLoader patched"), "helper reports the patches were applied");
		var hooks = h.Asm.GetType("ZeroT.AppearanceLoaderHelper.Hooks", true);
		int transpiled = (int)hooks.GetField("Transpiled").GetValue(null);
		h.Check(transpiled == 3, "wait sites rewritten inside the loader's coroutines: " + transpiled + " (2 morph pauses + 1 texture timeout)");

		// speed: same answers, a handful of questions
		var l1 = makeLoader();
		resetCounters();
		string v1 = (string)call1(l1, "checkVarDir", "Pack.Tex.latest:/Textures/");
		int dirCalls1 = MVR.FileManagementSecure.FileManagerSecure.DirExistsCalls;
		resetCounters();
		string p1 = (string)call1(l1, "checkPath", "Pack.Tex.3:/Textures/a.png");
		int fileCalls1 = MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls;
		resetCounters();
		string m1r = (string)call1(l1, "checkPath", "Gone.Pkg.9:/Textures/zz.png");
		int missCalls1 = MVR.FileManagementSecure.FileManagerSecure.FileExistsCalls;
		h.Check(v1 == v0 && p1 == p0 && m1r == m0, "same results with the helper");
		h.Check(dirCalls1 <= 4 && fileCalls1 <= 6 && missCalls1 <= 6, "package versions: " + dirCalls1 + " / " + fileCalls1 + " / " + missCalls1 + " file-system questions instead of " + dirCalls0 + " / " + fileCalls0 + " / " + missCalls0);
		// a package that appears later is noticed (refresh time changes)
		MVR.FileManagement.FileManager.SimPackageUids.Add("Pack.Tex.7"); MVR.FileManagement.FileManager.SimRefreshTime = MVR.FileManagement.FileManager.SimRefreshTime.AddMinutes(1);
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Pack.Tex.7:/Textures/a.png");
		string p7 = (string)call1(l1, "checkPath", "Pack.Tex.3:/Textures/a.png");
		h.Check(p7 == "Pack.Tex.7:/Textures/a.png", "a newly installed newer version is picked up after a package refresh");

		// a package that is only there as a switched-off archive (.DISABLED): VPB registers it when VaM is asked for a path inside it (simulated by the file
		// being answerable), but only if the fast lookup lets the question through
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile("Dis.Pack.7:/Textures/d.png");
		VPB.VamOnDemandLoader.Disabled.Clear();
		string dis0 = (string)call1(l1, "checkPath", "Dis.Pack.7:/Textures/d.png");
		h.Check(dis0 == "", "a .DISABLED package VPB does not know of is still answered 'not installed' without asking VaM");
		VPB.VamOnDemandLoader.Disabled.Add("Dis.Pack.7");
		resetCounters();
		string dis1 = (string)call1(l1, "checkPath", "Dis.Pack.7:/Textures/d.png");
		h.Check(dis1 == "Dis.Pack.7:/Textures/d.png", "a .DISABLED package VPB knows is let through to VaM / VPB and found (" + dis1 + ")");
		resetCounters();
		string dis2 = (string)call1(l1, "checkVarDir", "Dis.Pack.latest:/Textures/");
		int disDirCalls = MVR.FileManagementSecure.FileManagerSecure.DirExistsCalls;
		h.Check(dis2 == "Dis.Pack.7:/Textures/" && disDirCalls <= 6, "'.latest' of a package that only exists switched off resolves to its version (" + dis2 + "), still with a handful of questions (" + disDirCalls + ")");
		h.Check(h.LogHas("exists switched off (.DISABLED)") && (int)hooks.GetField("DisabledHits").GetValue(null) >= 2, "...and the log says a switched-off package was asked for");
		h.Set("Speed", "UseDisabledPackages", "false");
		string dis3 = (string)call1(l1, "checkPath", "Dis.Pack.7:/Textures/d.png");
		h.Check(dis3 == "", "UseDisabledPackages = false: switched-off packages do not count");
		h.Set("Speed", "UseDisabledPackages", "true");

		// missing-texture search: one listing, same files
		((JSONStorableBool)Field(al, "_searchMissingTextures_Bool").GetValue(l1)).val = true;
		resetCounters();
		string s1a = (string)call1(l1, "checkPath", "Gone.Pkg.9:/Textures/x.png");
		string s1b = (string)call1(l1, "checkPath", "Gone.Pkg.9:/Textures/y.png");
		string s1c = (string)call1(l1, "checkPath", "Gone.Pkg.9:/Textures/nowhere.png");
		int listCalls1 = MVR.FileManagementSecure.FileManagerSecure.GetFilesCalls;
		h.Check(s1a == s0a && s1b == s0b && s1c == s0c, "texture search: identical results and order (" + s1a + ", " + s1b + ")");
		h.Check(listCalls1 < listCalls0, "texture search: " + listCalls1 + " folder listings for 3 missing textures instead of " + listCalls0);
		hooks.GetMethod("HandlePrefix").Invoke(null, null);
		resetCounters();
		call1(l1, "checkPath", "Gone.Pkg.9:/Textures/x.png");
		h.Check(MVR.FileManagementSecure.FileManagerSecure.GetFilesCalls > 0, "a new load starts with a fresh listing");

		// failed load: sim restored, loader keeps working
		var failer1 = makeLoader(); var pf1 = prepareFailing(failer1);
		var en1 = (System.Collections.IEnumerator)call(failer1, "applyData");
		bool threw1 = false; bool more = true;
		try { more = en1.MoveNext(); } catch (Exception) { threw1 = true; }
		h.Check(!threw1 && !more, "helper: the same error no longer escapes");
		h.Check(simOf(pf1).val, "helper: the clothing sim is switched back ON");
		h.Check((int)hooks.GetField("Recovered").GetValue(null) == 1, "helper counted the recovered load");
		h.Check(h.LogHas("stopped by an error"), "...and logged what happened");

		// the waits
		float morphWait1 = firstWait(m1);
		h.Near(morphWait1, 0.5f, "morph apply now waits 0.5 s");
		h.Near((float)hooks.GetMethod("TextureTimeout").Invoke(null, null), 120f, "skin preset timeout 120 s instead of 300");
		// item waits: the setting is only lowered while a load runs, and always given back
		var chooser = new JSONStorableStringChooser("Select Wait Time", new System.Collections.Generic.List<string> { "1", "2", "5", "10", "15", "20" }, "10", "Select Wait Time");
		var wl = makeLoader(); Field(al, "selectWaitTime").SetValue(wl, chooser);
		var seen = new System.Collections.Generic.List<string>();
		Func<System.Collections.IEnumerator> fake = null;
		fake = () => FakeLoad(chooser, seen);
		System.Collections.IEnumerator guarded = fake();
		hooks.GetMethod("ApplyPostfix").Invoke(null, new object[] { wl, guarded });
		// ApplyPostfix takes the result by ref: call Guard directly for a handle on it
		var g = (System.Collections.IEnumerator)hooks.GetMethod("Guard").Invoke(null, new object[] { fake(), wl });
		g.MoveNext(); string whileRunning = chooser.val; while (g.MoveNext()) { }
		h.Check(whileRunning == "10" && chooser.val == "10", "item waits: 'Select Wait Time' 10 untouched by default (cap off by default)");
		h.Set("Speed", "MaxItemWaitSeconds", "3");
		seen.Clear();
		g = (System.Collections.IEnumerator)hooks.GetMethod("Guard").Invoke(null, new object[] { fake(), wl });
		g.MoveNext(); whileRunning = chooser.val; while (g.MoveNext()) { }
		h.Check(whileRunning == "2" && chooser.val == "10", "item waits: capped to the largest offered value <= 3 s (" + whileRunning + ") during the load, the user's 10 s restored after");
		g = (System.Collections.IEnumerator)hooks.GetMethod("Guard").Invoke(null, new object[] { ThrowingLoad(chooser), wl });
		while (g.MoveNext()) { }
		h.Check(chooser.val == "10", "...and also restored when the load fails");
	}
	public static readonly System.Collections.Generic.List<string> SaveNotes = new System.Collections.Generic.List<string>();
	public static void NoteSave(SimpleJSON.JSONClass jc) { SaveNotes.Add(jc.ToString()); }

	// stand-ins with the exact member shapes of UIAssist's grid classes (shapes are checked against the real UIAssist at the end)
	public sealed class FakeUiaButton
	{
		public int Index; public int Invoked; public bool Busy;
		public string buttonRef { get { return "R" + (Index + 1); } }
		public bool actionInProgress { get { return Busy; } set { Busy = value; } }
		public void ActionInit(int hand, bool leap) { Invoked++; }
	}
	public sealed class FakeUiaGrid
	{
		public JSONStorableString gridLabelJSS;
		public System.Collections.Generic.List<FakeUiaButton> buttonList = new System.Collections.Generic.List<FakeUiaButton>();
		public FakeUiaGrid(string label, int n) { gridLabelJSS = new JSONStorableString("gridLabel", label); for (int i = 0; i < n; i++) buttonList.Add(new FakeUiaButton { Index = i }); }
	}

	[Scenario("zajeshufflehelper", "ZeroT.ZajeShuffleHelper")]
	public static void ZajeShuffleHelper(Harness h)
	{
		h.Unpatch("none");
		// ---- UIAssist (fake grids) and the CoreControl atom
		var other = new FakeUiaGrid("Other", 3); var zaje = new FakeUiaGrid("Zaje", 30);
		UIAStorables.buttonGridsList = new System.Collections.Generic.List<FakeUiaGrid> { other, zaje };
		Func<Atom> makeCore = () => { var a = new GameObject("CoreControl").AddComponent<Atom>(); a.type = "CoreControl"; a.uid = a.name = "CoreControl"; a.containingAtom = a; h.Sc.AddAtom(a); return a; };
		var core = makeCore();
		Func<int> invokedAll = () => zaje.buttonList.Sum(b => b.Invoked);
		var settingsFile = "Saves/PluginData/ZeroT/ZajeShuffle/settings.json";
		// the .var's own saved settings, to be imported on first run
		MVR.FileManagementSecure.FileManagerSecure.SimReset();
		MVR.FileManagementSecure.FileManagerSecure.SimAddFile(settingsFile);
		SuperController.SimSaved[settingsFile] = "{\"gridLabel\":\"Zaje\",\"skipButtons\":\"24,25\",\"hotkey\":\"F6\",\"cooldown\":\"0.2\",\"autoShuffle\":\"False\",\"autoInterval\":\"45\"}";
		var harm = new HarmonyLib.Harmony("test.zaje");

		h.Start();
		h.Step(2.5f);
		Func<string, string, string> cfg = (sec, key) => h.Plugin.Config.Find(sec, key).BoxedValue.ToString();
		h.Check(cfg("Hotkey", "Key") == "F6" && cfg("Shuffle", "CooldownSeconds") == "0.2" && cfg("Auto", "AutoIntervalSeconds") == "45", "first run imports the .var's settings.json (hotkey F6, cooldown 0.2, interval 45)");
		h.Check(cfg("Import", "ImportedPluginSettings") == "True", "...and remembers it did");
		var host = core.GetStorableByID("ZeroT.ZajeShuffle");
		h.Check(host != null, "storable 'ZeroT.ZajeShuffle' is on CoreControl");
		h.Check(host.GetAction("Shuffle") != null && host.GetAction("SetHotkey") != null && host.GetAction("ClearHotkey") != null && host.GetAction("SaveSettings") != null, "actions Shuffle, SetHotkey, ClearHotkey, SaveSettings exist");
		h.Check(host.GetStringJSONParam("Grid Label") != null && host.GetStringJSONParam("Skip Buttons") != null && host.GetStringJSONParam("Hotkey") != null && host.GetFloatJSONParam("Cooldown (sec)") != null && host.GetBoolJSONParam("Auto Shuffle") != null && host.GetFloatJSONParam("Auto Interval (sec)") != null && host.GetStringJSONParam("Status") != null && host.GetStringJSONParam("History") != null, "the .var's eight parameters are there under the same names");
		h.Check(host.GetStringJSONParam("Hotkey").val == "F6" && Math.Abs(host.GetFloatJSONParam("Cooldown (sec)").val - 0.2f) < 1e-4f, "storables show the imported values");

		// ---- Shuffle action
		h.Set("Shuffle", "CooldownSeconds", "0.1");
		int before = invokedAll();
		host.CallAction("Shuffle");
		h.Check(invokedAll() == before + 1 && other.buttonList.Sum(b => b.Invoked) == 0, "Shuffle action activates exactly one button of the grid named Zaje (not of Other)");
		var status = host.GetStringJSONParam("Status").val; var hist = host.GetStringJSONParam("History").val;
		int lastIdx = zaje.buttonList.FindIndex(b => b.Invoked > 0);
		h.Check(status == "Now: R" + (lastIdx + 1), "status shows the button's real reference (" + status + "), not '#n'");
		h.Check(hist.EndsWith("- R" + (lastIdx + 1)) && hist.Split((char)10).Length == 1, "history has the entry (" + hist + ")");

		// ---- cooldown
		h.Set("Shuffle", "CooldownSeconds", "3"); h.Step(0.05f);
		before = invokedAll();
		h.Step(3.2f); host.CallAction("Shuffle"); int afterFirst = invokedAll();
		host.CallAction("Shuffle");
		h.Check(afterFirst == before + 1 && invokedAll() == afterFirst, "cooldown 3 s: the second shuffle inside it is ignored");
		h.Step(3.1f); host.CallAction("Shuffle");
		h.Check(invokedAll() == afterFirst + 1, "...and works after the cooldown");

		// ---- skip list, no repeat, coverage
		h.Set("Shuffle", "CooldownSeconds", "0.1");
		foreach (var b in zaje.buttonList) b.Invoked = 0;
		int prev = -1; bool repeated = false; var picks = new System.Collections.Generic.List<int>();
		for (int i = 0; i < 400; i++)
		{
			h.Step(0.12f);
			var snap = zaje.buttonList.Select(b => b.Invoked).ToArray();
			host.CallAction("Shuffle");
			int idx = -1; for (int k = 0; k < snap.Length; k++) if (zaje.buttonList[k].Invoked != snap[k]) idx = k;
			if (idx == prev) repeated = true;
			prev = idx; picks.Add(idx);
		}
		h.Check(picks.All(p => p >= 0), "400 shuffles: every one activated a button");
		h.Check(!picks.Contains(23) && !picks.Contains(24), "skip list '24,25' (1-based): buttons 24 and 25 never picked");
		h.Check(picks.Distinct().Count() == 28, "all 28 eligible buttons get picked (" + picks.Distinct().Count() + ")");
		h.Check(!repeated, "never the same button twice in a row");
		h.Set("Shuffle", "SkipButtons", "1, 2 ,x,3");
		foreach (var b in zaje.buttonList) b.Invoked = 0;
		for (int i = 0; i < 120; i++) { h.Step(0.12f); host.CallAction("Shuffle"); }
		h.Check(zaje.buttonList.Take(3).All(b => b.Invoked == 0) && zaje.buttonList.Skip(3).Count(b => b.Invoked > 0) > 20, "skip list with spaces and junk ('1, 2 ,x,3') skips 1-3 and ignores the junk");
		h.Set("Shuffle", "SkipButtons", "24,25");

		// ---- history keeps 20, newest first
		string h20 = host.GetStringJSONParam("History").val;
		h.Check(h20.Split((char)10).Length == 20, "history keeps the last 20 entries");
		h.Set("History", "Entries", "5"); h.Step(0.2f); host.CallAction("Shuffle");
		h.Check(host.GetStringJSONParam("History").val.Split((char)10).Length == 5, "History.Entries can shorten it");
		h.Set("History", "Entries", "20");

		// ---- busy buttons are not picked
		foreach (var b in zaje.buttonList) b.Busy = true;
		zaje.buttonList[7].Busy = false;
		foreach (var b in zaje.buttonList) b.Invoked = 0;
		for (int i = 0; i < 6; i++) { h.Step(0.12f); host.CallAction("Shuffle"); }
		h.Check(zaje.buttonList[7].Invoked >= 1 && zaje.buttonList.Where((b, i) => i != 7).All(b => b.Invoked == 0), "a button whose action is still running is never picked");
		zaje.buttonList[7].Busy = true; h.Step(0.2f); host.CallAction("Shuffle");
		h.Check(host.GetStringJSONParam("Status").val == "No eligible buttons", "every button busy -> 'No eligible buttons'");
		foreach (var b in zaje.buttonList) b.Busy = false;

		// ---- messages for a missing grid / UIAssist
		h.Set("Shuffle", "GridLabel", "Nope"); h.Step(0.2f); host.CallAction("Shuffle");
		h.Check(host.GetStringJSONParam("Status").val == "Grid 'Nope' not found", "unknown grid label -> status says so");
		h.Set("Shuffle", "GridLabel", "Zaje");
		UIAStorables.buttonGridsList = null; h.Step(0.2f); host.CallAction("Shuffle");
		h.Check(host.GetStringJSONParam("Status").val == "UIAssist not loaded yet", "UIAssist not loaded -> 'UIAssist not loaded yet'");
		UIAStorables.buttonGridsList = new System.Collections.Generic.List<FakeUiaGrid> { other, zaje };
		var swapped = new FakeUiaGrid("Zaje", 5); UIAStorables.buttonGridsList = new System.Collections.Generic.List<FakeUiaGrid> { swapped };
		h.Step(0.2f); host.CallAction("Shuffle");
		h.Check(swapped.buttonList.Sum(b => b.Invoked) == 1, "UIAssist rebuilding its grid list is followed (the new list is used)");
		UIAStorables.buttonGridsList = new System.Collections.Generic.List<FakeUiaGrid> { other, zaje };

		// ---- hotkey
		h.Set("Hotkey", "Key", "F5"); foreach (var b in zaje.buttonList) b.Invoked = 0; h.Step(0.2f);
		Input.SimDown.Add(KeyCode.F5); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == 1, "hotkey F5 shuffles");
		Input.SimDown.Add(KeyCode.F6); h.Step(0.2f); Input.SimDown.Clear();
		h.Check(invokedAll() == 1, "other keys do nothing");
		h.Set("Hotkey", "Key", "f5"); h.Step(0.2f); Input.SimDown.Add(KeyCode.F5); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == 2, "key names are accepted in any case ('f5')");
		var es = new UnityEngine.EventSystems.EventSystem(); var field = new GameObject("Field"); var inp = field.AddComponent<UnityEngine.UI.InputField>(); inp.isFocused = true;
		UnityEngine.EventSystems.EventSystem.current = es; es.currentSelectedGameObject = field;
		h.Step(0.2f); Input.SimDown.Add(KeyCode.F5); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == 2, "typing in a text field: the hotkey is ignored");
		inp.isFocused = false; h.Step(0.2f); Input.SimDown.Add(KeyCode.F5); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == 3, "...and works again when the field lets go");
		UnityEngine.EventSystems.EventSystem.current = null;

		// ---- set / clear hotkey like the buttons
		harm.Patch(typeof(SuperController).GetMethod("SaveJSON"), new HarmonyLib.HarmonyMethod(typeof(HelperScenarios).GetMethod("NoteSave", BindingFlags.Public | BindingFlags.Static)));
		SaveNotes.Clear();
		host.CallAction("SetHotkey"); h.Step(0.05f);
		h.Check(host.GetStringJSONParam("Status").val == "Press any key to set hotkey...", "SetHotkey: status asks for a key");
		Input.SimDown.Add(KeyCode.Mouse0); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(cfg("Hotkey", "Key").ToLower() == "f5", "SetHotkey: the left mouse button is not taken as a key (" + cfg("Hotkey", "Key") + ")");
		Input.SimDown.Add(KeyCode.G); h.Step(0.02f); Input.SimDown.Clear(); h.Step(0.04f);
		h.Check(cfg("Hotkey", "Key") == "G" && host.GetStringJSONParam("Status").val == "Hotkey set to: G" && host.GetStringJSONParam("Hotkey").val == "G", "SetHotkey: the next key becomes the hotkey (G) and is shown (" + cfg("Hotkey", "Key") + " / " + host.GetStringJSONParam("Status").val + " / " + host.GetStringJSONParam("Hotkey").val + ")");
		h.Check(SaveNotes.Count >= 1 && SaveNotes[SaveNotes.Count - 1].Contains("\"hotkey\":\"G\""), "...and is saved to the .var's settings.json too");
		int c0 = invokedAll(); h.Step(0.2f); Input.SimDown.Add(KeyCode.G); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == c0 + 1, "the newly set hotkey works at once");
		host.CallAction("ClearHotkey"); h.Step(0.05f);
		h.Check(cfg("Hotkey", "Key") == "None" && host.GetStringJSONParam("Status").val == "Hotkey cleared", "ClearHotkey: hotkey cleared and status says so");
		c0 = invokedAll(); h.Step(0.2f); Input.SimDown.Add(KeyCode.G); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(invokedAll() == c0, "...and the old key no longer shuffles");

		// ---- auto shuffle
		h.Set("Auto", "AutoIntervalSeconds", "5"); h.Step(0.2f);
		c0 = invokedAll();
		h.Set("Auto", "AutoShuffle", "true"); h.Step(0.3f);
		h.Check(invokedAll() == c0 + 1, "Auto Shuffle switched on: shuffles at once (like the .var)");
		h.Step(4.5f); h.Check(invokedAll() == c0 + 1, "...nothing before the interval is up");
		h.Step(1.0f); h.Check(invokedAll() == c0 + 2, "...one more after 5 s");
		h.Step(5.3f); h.Check(invokedAll() == c0 + 3, "...and so on every interval");
		h.Set("Auto", "AutoShuffle", "false"); h.Step(6f); h.Check(invokedAll() == c0 + 3, "Auto Shuffle off: stops");
		h.Set("Auto", "ShuffleWhenSwitchedOn", "false"); h.Set("Auto", "AutoShuffle", "true"); h.Step(0.3f);
		h.Check(invokedAll() == c0 + 3, "ShuffleWhenSwitchedOn=false: waits a full interval first");
		h.Step(5.3f); h.Check(invokedAll() == c0 + 4, "...then shuffles");
		h.Set("Auto", "AutoShuffle", "false"); h.Set("Auto", "ShuffleWhenSwitchedOn", "true"); h.Step(0.3f);
		host.GetBoolJSONParam("Auto Shuffle").val = true; h.Step(0.3f);
		h.Check(cfg("Auto", "AutoShuffle") == "True" && invokedAll() == c0 + 5, "switching Auto Shuffle on from the storable (a trigger) works the same");
		host.GetBoolJSONParam("Auto Shuffle").val = false;

		// ---- storables <-> settings
		host.GetFloatJSONParam("Cooldown (sec)").val = 2.5f; host.GetFloatJSONParam("Auto Interval (sec)").val = 60f;
		host.GetStringJSONParam("Grid Label").val = "Other"; host.GetStringJSONParam("Skip Buttons").val = "2";
		h.Check(cfg("Shuffle", "CooldownSeconds") == "2.5" && cfg("Auto", "AutoIntervalSeconds") == "60" && cfg("Shuffle", "GridLabel") == "Other" && cfg("Shuffle", "SkipButtons") == "2", "changing a storable (UI, trigger, other plugin) changes the setting");
		h.Set("Shuffle", "GridLabel", "Zaje"); h.Set("Shuffle", "SkipButtons", "24,25"); h.Set("Shuffle", "CooldownSeconds", "0.1"); h.Set("Auto", "AutoIntervalSeconds", "30");
		h.Check(host.GetStringJSONParam("Grid Label").val == "Zaje" && Math.Abs(host.GetFloatJSONParam("Cooldown (sec)").val - 0.1f) < 1e-4f && host.GetStringJSONParam("Skip Buttons").val == "24,25", "changing a setting (config file, ConfigurationManager) updates the storables");
		host.GetFloatJSONParam("Cooldown (sec)").val = 99f;
		h.Check(cfg("Shuffle", "CooldownSeconds") == "10", "cooldown is limited to 0.1 - 10 (99 -> 10)");
		h.Set("Shuffle", "CooldownSeconds", "0.1");

		// ---- Save Settings action
		SaveNotes.Clear();
		h.Set("Shuffle", "SkipButtons", "3,4"); h.Set("Hotkey", "Key", "F7");
		host.CallAction("SaveSettings");
		h.Check(host.GetStringJSONParam("Status").val == "Settings saved" && SaveNotes.Count == 1 && SaveNotes[0].Contains("\"skipButtons\":\"3,4\"") && SaveNotes[0].Contains("\"hotkey\":\"F7\"") && SaveNotes[0].Contains("\"cooldown\":\"0.1\"") && SaveNotes[0].Contains("\"autoInterval\":\"30\""), "SaveSettings writes every setting to settings.json in the .var's format");
		h.Set("Shuffle", "SkipButtons", "24,25"); h.Set("Hotkey", "Key", "None");

		// ---- scene load: CoreControl is replaced, the actions come back
		h.Load(() => { core = makeCore(); });
		h.Step(2.5f);
		var host2 = core.GetStorableByID("ZeroT.ZajeShuffle");
		h.Check(host2 != null && host2.GetAction("Shuffle") != null, "after a scene load the actions are back on the new CoreControl");
		int c1 = invokedAll(); host2.CallAction("Shuffle"); h.Check(invokedAll() == c1 + 1, "...and work");

		// ---- master switch
		h.Set("General", "Enabled", "false"); h.Step(1.5f);
		h.Check(core.GetStorableByID("ZeroT.ZajeShuffle") == null, "Enabled=false removes the actions");
		h.Set("General", "Enabled", "true"); h.Step(1.5f);
		h.Check(core.GetStorableByID("ZeroT.ZajeShuffle") != null, "...and Enabled=true brings them back");

		// ---- the real UIAssist: do the members this helper reflects on exist with these shapes?
		var ua = h.LoadTarget("UIAssistScripts");
		var st = ua.GetType("JayJayWon.UIAStorables", true);
		var gf = st.GetField("buttonGridsList", BindingFlags.Public | BindingFlags.Static);
		h.Check(gf != null && gf.FieldType.IsGenericType, "real UIAssist: UIAStorables.buttonGridsList is a public static List field");
		var gridT = gf.FieldType.GetGenericArguments()[0];
		var lf = gridT.GetField("gridLabelJSS", BindingFlags.Public | BindingFlags.Instance);
		h.Check(lf != null && lf.FieldType.GetProperty("val") != null, "real UIAssist: grid.gridLabelJSS is a field with a 'val'");
		var blf = gridT.GetField("buttonList", BindingFlags.Public | BindingFlags.Instance);
		var btnT = blf.FieldType.GetGenericArguments()[0];
		h.Check(blf != null && btnT.GetMethod("ActionInit", new[] { typeof(int), typeof(bool) }) != null, "real UIAssist: grid.buttonList (field) of buttons with ActionInit(int, bool)");
		h.Check(btnT.GetProperty("buttonRef") != null && btnT.GetField("buttonRef") == null, "real UIAssist: buttonRef is a PROPERTY (the .var looked for a field, so it always showed '#n')");
		h.Check(btnT.GetProperty("actionInProgress") != null, "real UIAssist: button.actionInProgress exists");
	}


	[Scenario("zajeshufflehelper_original", "ZeroT.ZajeShuffleHelper")]
	public static void ZajeShuffleHelperOriginal(Harness h)
	{
		var zaje = new FakeUiaGrid("Zaje", 30);
		UIAStorables.buttonGridsList = new System.Collections.Generic.List<FakeUiaGrid> { zaje };
		var core = new GameObject("CoreControl").AddComponent<Atom>(); core.type = "CoreControl"; core.uid = core.name = "CoreControl"; core.containingAtom = core; h.Sc.AddAtom(core);
		// the .var itself, running as a session plugin
		var asm = h.LoadTarget("ZajeShuffleScripts");
		var ot = asm.GetType("ZajeShuffle", true);
		var go = new GameObject("plugin#0_ZajeShuffle"); go.transform.SetParent(core.transform, false);
		var orig = (MVRScript)go.AddComponent(ot);
		orig.containingAtom = core;
		orig.Init();
		h.Step(1.5f);
		ot.GetMethod("DoShuffle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(orig, null);
		var status = ((JSONStorableString)ot.GetField("statusJSON", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(orig)).val;
		h.Check(zaje.buttonList.Sum(b => b.Invoked) == 1, "the .var shuffles: one button activated");
		h.Check(status.StartsWith("Now: #"), "the .var's own status says '" + status + "' - it never shows UIAssist's button reference (it looks for a field, UIAssist has a property)");

		h.Start();
		h.Step(2.5f);
		h.Check(core.GetStorableByID("ZeroT.ZajeShuffle") == null, "with the .var loaded the helper puts nothing on CoreControl");
		h.Check(h.LogHas("steps aside"), "...and says why in the log");
		h.Set("Hotkey", "Key", "F5"); h.Step(0.2f);
		int c0 = zaje.buttonList.Sum(b => b.Invoked);
		Input.SimDown.Add(KeyCode.F5); h.Step(0.02f); Input.SimDown.Clear();
		h.Check(zaje.buttonList.Sum(b => b.Invoked) == c0, "...and its hotkey does nothing, so there is only one shuffle");
	}
}

// UIAssist's static entry point, same name and shape: the helper finds it by name
public static class UIAStorables
{
	public static System.Collections.IList buttonGridsList;
}

public static class ToggleHudScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static UnityEngine.UI.Button Row(string label)
	{
		foreach (var b in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>()) if (b.gameObject.name == "Row_" + label) return b;
		return null;
	}

	private static string State(UnityEngine.UI.Button row) { return row.transform.Find("State").GetComponent<UnityEngine.UI.Text>().text; }
	private static Color Back(UnityEngine.UI.Button row) { return row.GetComponent<UnityEngine.UI.Image>().color; }

	[Scenario("togglehud", "ZeroT.ZajeShuffleHelper")]
	public static void ToggleHud(Harness h)
	{
		var prefs = new GameObject("prefs").AddComponent<UserPreferences>();
		UserPreferences.singleton = prefs;
		prefs.softPhysics = true; prefs.physicsHighQuality = false;
		Camera.main = new GameObject("Main Camera").AddComponent<Camera>();
		var core = new GameObject("CoreControl").AddComponent<Atom>(); core.type = "CoreControl"; core.uid = core.name = "CoreControl"; core.containingAtom = core; h.Sc.AddAtom(core);
		var asm = h.LoadTarget("ToggleHUDScripts");
		var t = asm.GetType("zerot.ToggleHUD", true);
		var go = new GameObject("plugin#0_ToggleHUD"); go.transform.SetParent(core.transform, false);
		var hud = (MVRScript)go.AddComponent(t);
		hud.containingAtom = core;
		hud.Init();
		h.Step(1f);

		var hp = Row("HIGH PHYSICS"); var soft = Row("SOFT BODY"); var gt = Row("GIVEMEFPS (GT)"); var ac = Row("ADV COLLIDERS");
		h.Check(hp != null && soft != null && gt != null && ac != null, "four toggle rows: Soft Body, GiveMeFPS, Adv Colliders, High Physics");
		var on = Back(soft); var off = Back(hp);
		h.Check(State(hp) == "OFF" && State(soft) == "ON", "status text from the real settings at start: High Physics OFF, Soft Body ON");
		h.Check(Back(hp).r > Back(hp).g && Back(soft).g > Back(soft).r, "red when off, green when on - like the other rows");
		h.Check(hp.transform.Find("Dot") != null && hp.transform.Find("State") != null, "the same parts as the other rows: indicator dot, label, ON/OFF text");
		var yHp = hp.GetComponent<RectTransform>().anchoredPosition.y; var yAc = ac.GetComponent<RectTransform>().anchoredPosition.y;
		h.Check(yHp < yAc && Math.Abs((yAc - yHp) - 62f) < 0.5f, "placed under the others with the same 62 spacing (" + yAc + " -> " + yHp + ")");
		var panel = hp.transform.parent.GetComponent<RectTransform>();
		h.Check(panel.sizeDelta.y >= 446f, "the panel grew to fit (" + panel.sizeDelta.y + ")");

		hp.onClick.Invoke();
		h.Check(prefs.physicsHighQuality, "clicking sets the real High Quality Physics preference");
		h.Check(State(hp) == "ON" && Back(hp).g > Back(hp).r, "...and the row turns green: ON");
		h.Check(prefs.softPhysics && State(soft) == "ON", "the other rows are untouched");
		hp.onClick.Invoke();
		h.Check(!prefs.physicsHighQuality && State(hp) == "OFF", "clicking again switches it back off");

		prefs.physicsHighQuality = true;      // changed in VaM's own settings
		h.Step(0.8f);
		h.Check(State(hp) == "ON", "the row follows a change made in VaM's settings (status is the truth, not a remembered click)");
		prefs.physicsHighQuality = false; h.Step(0.8f);
		h.Check(State(hp) == "OFF", "...and back");

		soft.onClick.Invoke();
		h.Check(!prefs.softPhysics && State(soft) == "OFF", "Soft Body still works");
		var st = (JSONStorableBool)hud.GetBoolJSONParam("High Physics On");
		h.Check(st != null && !st.val, "the state is a scene-stored storable 'High Physics On'");
		st.val = true;
		h.Check(State(hp) == "ON", "setting the storable (scene load / trigger) repaints the row");
	}
}
