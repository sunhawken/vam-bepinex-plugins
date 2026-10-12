using System;
using System.Reflection;
using UnityEngine;

// Error Silencer: an error must never arm VaM's pop-up, never open the HUD, and never switch the splash object on, not even for one frame.
public static class ErrorSilencerScenario
{
	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[Scenario("errorsilencer", "ZeroT.ErrorSilencer")]
	public static void Silencer(Harness h)
	{
		h.Start();
		var sc = h.Sc;
		var t = typeof(SuperController);
		Func<string, object> get = n => t.GetField(n, All).GetValue(sc);
		Action<string, object> set = (n, v) => t.GetField(n, All).SetValue(sc, v);
		Action queue = () => t.GetMethod("CheckMessageAndErrorQueue", All).Invoke(sc, null);
		var errT = new GameObject("errorSplash"); errT.SetActive(false); set("errorSplashTransform", errT.transform);
		var msgT = new GameObject("msgSplash"); msgT.SetActive(false); set("msgSplashTransform", msgT.transform);
		sc.SimMainHudVisible = false;

		h.Check(h.LogHas("Error Silencer: 3/3 hooks installed"), "all three hooks installed (Error, Message, CheckMessageAndErrorQueue)");

		sc.Error("something broke");
		h.Check((float)get("errorSplashTimeRemaining") == 0f, "Error(): the pop-up timer is never armed");
		h.Check(sc.SimHudOpened == 0 && !sc.SimMainHudVisible, "Error(): the main HUD is not opened because of an error");
		h.Check(sc.errorCount == 1 && sc.SimErrorLog.Contains("something broke"), "the error is still counted and kept in the error log text");
		for (int i = 0; i < 10; i++) { queue(); Time.frameCount++; h.Check(!errT.activeSelf, "frame " + i + ": the splash object is never switched on"); if (errT.activeSelf) break; }
		SuperController.Errors.Clear();
		sc.Error("second", false, true);
		sc.Error("third", true, false);
		h.Check(sc.errorCount == 3 && (float)get("errorSplashTimeRemaining") == 0f, "every overload / flag combination stays silent");

		// something else arms the fields directly (or VaM was already showing it): cleared before the queue looks at it
		set("errorSplashTimeRemaining", 4f); set("hasPendingErrorSplash", true); errT.SetActive(true);
		queue();
		h.Check(!errT.activeSelf && (float)get("errorSplashTimeRemaining") == 0f && !(bool)get("hasPendingErrorSplash") && sc.SimHudOpened == 0,
			"a pop-up that was already armed is cleared before it can be drawn, and the pending flag cannot open the HUD");

		// world UI (VR) path: the pending flag is the route to ShowMainHUDAuto
		set("worldUIActivated", true); sc.Error("in vr");
		set("worldUIActivated", false); queue();
		h.Check(sc.SimHudOpened == 0, "VR: an error while the world UI is up does not open the HUD afterwards either");

		// messages are left alone unless asked
		sc.Message("hello");
		h.Check((float)get("msgSplashTimeRemaining") > 0f, "messages still pop up by default");
		queue(); h.Check(msgT.activeSelf, "...and the message splash shows");
		set("msgSplashTimeRemaining", 0f); msgT.SetActive(false);
		h.Set("Silencer", "BlockMessageSplash", "true");
		sc.Message("hello again");
		h.Check((float)get("msgSplashTimeRemaining") == 0f, "BlockMessageSplash=true: message pop-up is silenced too");
		queue(); h.Check(!msgT.activeSelf, "...and never shows");

		// off switch
		h.Set("Silencer", "BlockErrorSplash", "false");
		sc.Error("visible again");
		h.Check((float)get("errorSplashTimeRemaining") > 0f && sc.SimHudOpened == 1, "BlockErrorSplash=false: VaM behaves as before (pop-up armed, HUD opened)");
	}
}
