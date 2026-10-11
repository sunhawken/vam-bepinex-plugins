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
}
