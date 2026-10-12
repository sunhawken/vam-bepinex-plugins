using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// HUD Click Shield: the covered-area logic (IMGUI rectangles, overlay canvases) and the Harmony postfix that tells VaM "the mouse is over the UI".
// The IMGUI draw hooks themselves patch Unity-internal methods the simulator does not have; their targets were checked against the real
// UnityEngine.IMGUIModule (GUIStyle.Internal_Draw / Internal_Draw2, GUI.DoButton / DoToggle / DrawTexture all take a Rect called "position").
public static class HudShieldScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[Scenario("hudclickshield", "ZeroT.HudClickShield")]
	public static void Shield(Harness h)
	{
		h.Start();
		var t = h.Asm.GetType("ZeroT.HudClickShield.HudClickShieldPlugin", true);
		Func<string, object[], object> call = (n, a) => t.GetMethod(n, All).Invoke(null, a);
		Func<float, float, bool> imgui = (x, yTop) => (bool)call("ImguiCovers", new object[] { new Vector3(x, Screen.height - yTop, 0f) });
		Action<Rect> draw = r => { Event.current.type = EventType.Repaint; call("Record", new object[] { r }); };

		// ---- IMGUI windows
		h.Check(!imgui(150, 150), "nothing drawn: nothing is covered");
		Time.frameCount++;
		draw(new Rect(100, 100, 200, 150));          // a window backdrop
		draw(new Rect(110, 110, 40, 20));            // a button inside it
		h.Check(imgui(150, 150), "a point inside a drawn IMGUI rectangle is covered (this frame's list)");
		h.Check(!imgui(400, 400), "a point outside it is not");
		h.Check(!imgui(299, 99), "the row above the window is not");
		Time.frameCount++;
		draw(new Rect(100, 100, 200, 150));
		h.Check(imgui(150, 150), "still covered on the next frame (previous frame's rectangles + the new ones)");
		Event.current.type = EventType.MouseDown;
		Time.frameCount++;
		call("Record", new object[] { new Rect(500, 500, 100, 100) });
		h.Check(!imgui(520, 520), "only Repaint draws count (a MouseDown pass records nothing)");
		Event.current.type = EventType.Repaint;
		Time.frameCount++;
		draw(new Rect(0, 0, Screen.width, Screen.height));
		h.Check(!imgui(Screen.width - 5, Screen.height - 5), "a full-screen backdrop is ignored");
		h.Check((int)t.GetProperty("RectCount").GetValue(null, null) < 1000, "rectangle list stays small");
		for (int i = 0; i < 6; i++) Time.frameCount++;
		h.Check(!imgui(150, 150), "windows that stop drawing stop covering after a few frames");

		// ---- overlay HUD canvases
		var images = new System.Collections.Generic.Dictionary<string, Image>();
		Func<string, bool, float, float, float, float, GameObject> canvas = (name, raycaster, x, y, w, hgt) =>
		{
			var go = new GameObject(name);
			var cv = go.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay;
			var crt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
			crt.sizeDelta = new Vector2(Screen.width, Screen.height);
			if (raycaster) go.AddComponent<GraphicRaycaster>();
			var child = new GameObject("panel"); child.transform.SetParent(go.transform, false);
			var rt = child.AddComponent<RectTransform>();
			rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
			rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, hgt);
			var img = child.AddComponent<Image>(); img.color = Color.white; images[name] = img;
			return go;
		};
		var hud = canvas("ArousalHUD_Canvas", true, 200, 300, 100, 50);
		var other = canvas("SomethingElse_Canvas", true, 700, 300, 100, 50);
		var noRay = canvas("PlainHUD_Canvas", false, 900, 300, 100, 50);
		Func<float, float, bool> overlay = (x, y) => (bool)call("OverlayCovers", new object[] { new Vector3(x, y, 0f) });
		h.Check(overlay(250, 320), "a point on a HUD overlay canvas (name has 'HUD', has a GraphicRaycaster) is covered");
		h.Check(!overlay(250, 400), "just outside the HUD panel it is not");
		h.Check(!overlay(750, 320), "a canvas whose name does not match the filter is not protected");
		h.Check(!overlay(950, 320), "a canvas without a GraphicRaycaster is not protected");
		images["ArousalHUD_Canvas"].raycastTarget = false;
		h.Step(3.5f);
		h.Check(!overlay(250, 320), "a graphic that is not a raycast target does not count");

		// ---- VaM's own menus: the click must not reach them through LookInputModule
		Func<bool> vamGetsInput = () => (bool)call("UiInputPrefix", new object[0]);
		Action<float, float> frame = (x, yTop) =>
		{
			Time.frameCount++;
			Input.mousePosition = new Vector3(x, Screen.height - yTop, 0);
			Event.current.type = EventType.Repaint;
			call("Record", new object[] { new Rect(100, 100, 200, 150) });   // the HUD window is drawn every frame
		};
		Input.SimMouseHeld.Clear(); Input.SimMouseDown.Clear();
		frame(150, 150);
		h.Check(!vamGetsInput(), "mouse over the HUD (Random Look's Close All, say): VaM's menu behind it is not given the mouse");
		frame(500, 500);
		h.Check(vamGetsInput(), "mouse elsewhere: VaM gets it as usual");
		frame(150, 150); Input.SimMouseDown.Add(0); Input.SimMouseHeld.Add(0);
		h.Check(!vamGetsInput(), "the click lands on the HUD: still not VaM's");
		Input.SimMouseDown.Clear();
		frame(500, 500);
		h.Check(!vamGetsInput(), "dragged off the HUD with the button down: the press is still the HUD's");
		Input.SimMouseHeld.Clear();
		frame(500, 500);
		h.Check(vamGetsInput(), "released: VaM gets the mouse again");
		frame(500, 500); Input.SimMouseDown.Add(0); Input.SimMouseHeld.Add(0);
		h.Check(vamGetsInput(), "a press that starts on a VaM slider...");
		Input.SimMouseDown.Clear();
		frame(150, 150);
		h.Check(vamGetsInput(), "...is not stolen when the pointer slides over a HUD");
		Input.SimMouseHeld.Clear();
		frame(500, 500);

		// ---- the flag VaM reads
		images["ArousalHUD_Canvas"].raycastTarget = true;
		h.Step(3.5f);
		var sc = h.Sc;
		var process = typeof(SuperController).GetMethod("ProcessUI", All);
		var flag = typeof(SuperController).GetProperty("SimGuiHitMouse");
		Input.mousePosition = new Vector3(250, 320, 0);
		Time.frameCount++;
		process.Invoke(sc, null);
		h.Check((bool)flag.GetValue(sc, null), "VaM's GUIhitMouse becomes true while the mouse is over a HUD (Harmony postfix on SuperController.ProcessUI)");
		Input.mousePosition = new Vector3(640, 600, 0);
		Time.frameCount++;
		process.Invoke(sc, null);
		h.Check(!(bool)flag.GetValue(sc, null), "...and stays false elsewhere");
		h.Set("Shield", "Enabled", "false");
		Input.mousePosition = new Vector3(250, 320, 0);
		Time.frameCount++;
		process.Invoke(sc, null);
		h.Check(!(bool)flag.GetValue(sc, null), "Enabled=false: VaM's flag is left alone");
		h.Check(h.LogHas("HUD Click Shield ready"), "the plugin reports how many hooks it placed");
	}
}
