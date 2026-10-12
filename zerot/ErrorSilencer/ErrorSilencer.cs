using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.ErrorSilencer
{
	// VaM shows an error the moment SuperController.Error runs: with `splash` true it opens the main HUD (when it is hidden) and arms errorSplashTimeRemaining,
	// and CheckMessageAndErrorQueue then switches errorSplashTransform on for five seconds. Hiding the panel afterwards would still flash it for a frame,
	// so the splash is refused BEFORE it starts: Error() runs with splash = false (nothing opens, nothing is armed), and the splash state is also
	// cleared before CheckMessageAndErrorQueue looks at it. The error is still counted, written to the log file and kept in the error log text
	// (the panel you open yourself still lists it); only the pop-up is gone. Every error path (LogError, singleton.Error, game code) ends in Error().
	[BepInPlugin(PluginGuid, "Error Silencer", "1.0.0")]
	public sealed class ErrorSilencerPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.zerot.errorsilencer";

		private static ConfigEntry<bool> _errors, _messages;
		private static FieldInfo fSplashT, fSplashLeft, fPending, fMsgT, fMsgLeft;
		public static int Suppressed;

		private void Awake()
		{
			_errors = Config.Bind("Silencer", "BlockErrorSplash", true, "Never show the error pop-up (and never open the HUD because of an error).");
			_messages = Config.Bind("Silencer", "BlockMessageSplash", false, "Also never show the green message pop-up.");
			try
			{
				Type sc = AccessTools.TypeByName("SuperController");
				if (sc == null) { Logger.LogWarning("SuperController not found: nothing patched"); return; }
				fSplashT = AccessTools.Field(sc, "errorSplashTransform");
				fSplashLeft = AccessTools.Field(sc, "errorSplashTimeRemaining");
				fPending = AccessTools.Field(sc, "hasPendingErrorSplash");
				fMsgT = AccessTools.Field(sc, "msgSplashTransform");
				fMsgLeft = AccessTools.Field(sc, "msgSplashTimeRemaining");
				Harmony h = new Harmony(PluginGuid);
				int ok = 0, total = 0;
				total++; ok += Patch(h, sc, "Error", "ErrorPrefix", "ErrorPostfix");
				total++; ok += Patch(h, sc, "Message", "MessagePrefix", null);
				total++; ok += Patch(h, sc, "CheckMessageAndErrorQueue", "QueuePrefix", null);
				Logger.LogInfo("Error Silencer: " + ok + "/" + total + " hooks installed");
			}
			catch (Exception e) { Logger.LogWarning("Error Silencer failed to install: " + e.Message); }
		}

		private int Patch(Harmony h, Type t, string name, string pre, string post)
		{
			try
			{
				MethodInfo m = AccessTools.Method(t, name);
				if (m == null) { Logger.LogWarning(name + " not found"); return 0; }
				h.Patch(m,
					pre == null ? null : new HarmonyMethod(typeof(ErrorSilencerPlugin).GetMethod(pre, BindingFlags.Public | BindingFlags.Static)),
					post == null ? null : new HarmonyMethod(typeof(ErrorSilencerPlugin).GetMethod(post, BindingFlags.Public | BindingFlags.Static)));
				return 1;
			}
			catch (Exception e) { Logger.LogWarning("patch " + name + ": " + e.Message); return 0; }
		}

		// Error(string err, bool logToFile = true, bool splash = true)
		public static void ErrorPrefix(ref bool splash)
		{
			try { if (_errors != null && _errors.Value && splash) { splash = false; Suppressed++; } } catch { }
		}

		// belt and braces: whatever armed the splash (another plugin poking the fields) is cleared right away
		public static void ErrorPostfix(object __instance) { Clear(__instance); }

		public static void QueuePrefix(object __instance) { Clear(__instance); }

		// Message(string msg, bool logToFile = true, bool splash = true)
		public static void MessagePrefix(object __instance, ref bool splash)
		{
			try
			{
				if (_messages == null || !_messages.Value || !splash) return;
				splash = false;
				if (fMsgLeft != null) fMsgLeft.SetValue(__instance, 0f);
				Transform t = fMsgT == null ? null : fMsgT.GetValue(__instance) as Transform;
				if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
			}
			catch { }
		}

		private static void Clear(object sc)
		{
			try
			{
				if (_errors == null || !_errors.Value || sc == null) return;
				if (fSplashLeft != null && (float)fSplashLeft.GetValue(sc) > 0f) fSplashLeft.SetValue(sc, 0f);
				if (fPending != null && (bool)fPending.GetValue(sc)) fPending.SetValue(sc, false);
				Transform t = fSplashT == null ? null : fSplashT.GetValue(sc) as Transform;
				if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
			}
			catch { }
		}
	}
}
