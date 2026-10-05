using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.PpUiSkin
{
	// Re-skins the ParticlePinnacle touch-filter window to the VpbRandomLook look without touching the host DLL:
	// the host's OnGUI is replaced by a Harmony prefix that drives the host's own config entries and methods.
	[BepInPlugin("zerot.particlepinnacle.uiskin", "ParticlePinnacle UI Skin", "1.0.0")]
	[BepInDependency("com.particlepinnacle.thighcompressorandvibrations", BepInDependency.DependencyFlags.SoftDependency)]
	public sealed class PpUiSkinPlugin : BaseUnityPlugin
	{
		private object hostInstance;

		private void Awake()
		{
			Type host = AccessTools.TypeByName("ParticlePinnacle.ParticlePinnacleStandalonePlugin");
			if (host == null)
			{
				Logger.LogWarning("ParticlePinnacle host plugin not found; UI skin not applied.");
				return;
			}
			MethodInfo onGui = AccessTools.Method(host, "OnGUI");
			if (onGui == null || !Hooks.Bind(host, Logger))
			{
				Logger.LogWarning("ParticlePinnacle host layout changed; UI skin not applied.");
				return;
			}
			Hooks.ActiveCfg = Config.Bind<bool>("General", "Active", true, "Turn ParticlePinnacle off completely (disables its scripts).");
			new Harmony("zerot.particlepinnacle.uiskin").Patch(onGui, new HarmonyMethod(typeof(Hooks).GetMethod("OnGuiPrefix")), null, null, null);
			Logger.LogInfo("ParticlePinnacle window re-skinned.");
		}

		private void OnGUI()
		{
			if (hostInstance == null)
			{
				PluginInfo info;
				if (Chainloader.PluginInfos.TryGetValue("com.particlepinnacle.thighcompressorandvibrations", out info) && info.Instance != null)
				{
					hostInstance = info.Instance;
					Hooks.ApplyActive(hostInstance, Hooks.ActiveCfg.Value);
				}
				return;
			}
			Hooks.DrawSafe(hostInstance);
		}
	}

	public static class Hooks
	{
		private static FieldInfo fShow;
		private static FieldInfo fScale;
		private static FieldInfo fWin;
		private static FieldInfo fCollapsed;
		private static FieldInfo fPersons;
		private static FieldInfo fCua;
		private static MethodInfo mApply;
		private static MethodInfo mSave;
		private static BepInEx.Logging.ManualLogSource log;
		internal static ConfigEntry<bool> ActiveCfg;
		private static FieldInfo fControllers;
		private static bool failed;
		private static bool dragging;
		private static bool resizing;
		private static Vector2 startMouse;
		private static Vector2 startPos;
		private static Vector2 startSize;

		internal static bool Bind(Type host, BepInEx.Logging.ManualLogSource logger)
		{
			log = logger;
			BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			fShow = host.GetField("_showControlWindow", f);
			fScale = host.GetField("_controlWindowScale", f);
			fWin = host.GetField("_controlWindow", f);
			fCollapsed = host.GetField("_controlWindowCollapsed", f);
			fPersons = host.GetField("_activateWhenTouchingPersons", f);
			fCua = host.GetField("_activateWhenTouchingCua", f);
			mApply = host.GetMethod("ApplyTouchFiltersToNativeControllers", f);
			mSave = host.GetMethod("SaveControlWindowLayout", f);
			fControllers = host.GetField("_nativeControllers", f);
			return fShow != null && fScale != null && fWin != null && fCollapsed != null && fPersons != null && fCua != null && mApply != null && mSave != null;
		}

		// The skin plugin draws the window itself; the host's own OnGUI is simply suppressed.
		public static bool OnGuiPrefix(object __instance)
		{
			return failed;
		}

		internal static void DrawSafe(object host)
		{
			if (failed)
			{
				return;
			}
			try
			{
				Draw(host);
			}
			catch (Exception e)
			{
				failed = true;
				log.LogWarning("UI skin failed: " + e.Message);
			}
		}

		// Off = the host plugin and every thigh-compressor controller it created stop updating.
		internal static void ApplyActive(object host, bool on)
		{
			UnityEngine.Behaviour hb = host as UnityEngine.Behaviour;
			if (hb != null)
			{
				hb.enabled = on;
			}
			if (fControllers != null)
			{
				System.Collections.IDictionary d = fControllers.GetValue(host) as System.Collections.IDictionary;
				if (d != null)
				{
					foreach (object c in d.Values)
					{
						UnityEngine.Behaviour cb = c as UnityEngine.Behaviour;
						if (cb != null)
						{
							cb.enabled = on;
						}
					}
				}
			}
		}

		private static void Draw(object host)
		{
			ConfigEntry<bool> show = (ConfigEntry<bool>)fShow.GetValue(host);
			if (show == null || !show.Value)
			{
				return;
			}
			ConfigEntry<float> scaleCfg = (ConfigEntry<float>)fScale.GetValue(host);
			ConfigEntry<bool> collapsedCfg = (ConfigEntry<bool>)fCollapsed.GetValue(host);
			ConfigEntry<bool> persons = (ConfigEntry<bool>)fPersons.GetValue(host);
			ConfigEntry<bool> cua = (ConfigEntry<bool>)fCua.GetValue(host);
			Rect win = (Rect)fWin.GetValue(host);
			bool collapsed = collapsedCfg.Value;
			float s = ZeroT.UiKit.RlChrome.Dpi(win.x, win.y) * Mathf.Clamp(scaleCfg.Value, 0.65f, 2.5f);
			float w = collapsed ? 340f : win.width;
			float h = collapsed ? ZeroT.UiKit.RlChrome.CollapsedH : win.height;
			Rect panel = new Rect(win.x, win.y, w * s, h * s);
			GUI.Box(panel, "");

			GUI.BeginGroup(panel);
			int b = ZeroT.UiKit.RlChrome.TitleRow(panel.width, collapsed ? "ParticlePinnacle  52.0.1" : "ParticlePinnacle Filters", collapsed, s, GUI.skin.label, GUI.skin.button, ActiveCfg.Value);
			GUI.EndGroup();
			if ((b & 1) != 0)
			{
				scaleCfg.Value = Mathf.Clamp(scaleCfg.Value - 0.1f, 0.65f, 2.5f);
				ZeroT.UiKit.RlChrome.ResetDpi();
				mSave.Invoke(host, null);
			}
			if ((b & 2) != 0)
			{
				scaleCfg.Value = Mathf.Clamp(scaleCfg.Value + 0.1f, 0.65f, 2.5f);
				ZeroT.UiKit.RlChrome.ResetDpi();
				mSave.Invoke(host, null);
			}
			if ((b & 16) != 0)
			{
				ActiveCfg.Value = !ActiveCfg.Value;
				ActiveCfg.ConfigFile.Save();
				ApplyActive(host, ActiveCfg.Value);
				return;
			}
			if ((b & 4) != 0)
			{
				collapsedCfg.Value = !collapsedCfg.Value;
				dragging = false;
				resizing = false;
				collapsedCfg.ConfigFile.Save();
				return;
			}
			if ((b & 8) != 0)
			{
				show.Value = false;
				show.ConfigFile.Save();
				return;
			}

			float pad = ZeroT.UiKit.RlChrome.Margin * s;
			if (!collapsed && ActiveCfg.Value)
			{
				if (FilterButton(new Rect(panel.x + pad, panel.y + 31f * s, panel.width - 2f * pad, 28f * s), "Person targets", persons))
				{
					mApply.Invoke(host, null);
				}
				if (FilterButton(new Rect(panel.x + pad, panel.y + 64f * s, panel.width - 2f * pad, 28f * s), "CUA targets", cua))
				{
					mApply.Invoke(host, null);
				}
			}

			Rect drag = new Rect(panel.x, panel.y, panel.width - 150f * s, 26f * s);
			Rect grip = new Rect(panel.xMax - 20f * s, panel.yMax - 20f * s, 18f * s, 18f * s);
			if (!collapsed)
			{
				GUI.Label(grip, "↘");
			}
			Event e = Event.current;
			bool changed = false;
			if (e.type == EventType.MouseDown && e.button == 0 && !collapsed && grip.Contains(e.mousePosition))
			{
				resizing = true;
				startMouse = e.mousePosition;
				startSize = new Vector2(win.width, win.height);
				e.Use();
			}
			else if (e.type == EventType.MouseDown && e.button == 0 && drag.Contains(e.mousePosition))
			{
				dragging = true;
				startMouse = e.mousePosition;
				startPos = new Vector2(win.x, win.y);
				e.Use();
			}
			else if (resizing && e.type == EventType.MouseDrag)
			{
				win.width = Mathf.Clamp(startSize.x + (e.mousePosition.x - startMouse.x) / s, 250f, 560f);
				win.height = Mathf.Clamp(startSize.y + (e.mousePosition.y - startMouse.y) / s, 96f, 360f);
				changed = true;
				e.Use();
			}
			else if (dragging && e.type == EventType.MouseDrag)
			{
				win.x = startPos.x + e.mousePosition.x - startMouse.x;
				win.y = startPos.y + e.mousePosition.y - startMouse.y;
				changed = true;
				e.Use();
			}
			else if ((resizing || dragging) && e.type == EventType.MouseUp)
			{
				resizing = false;
				dragging = false;
				fWin.SetValue(host, win);
				mSave.Invoke(host, null);
				e.Use();
			}
			if (changed)
			{
				fWin.SetValue(host, win);
			}
		}

		private static bool FilterButton(Rect area, string label, ConfigEntry<bool> setting)
		{
			if (GUI.Button(area, label + "  [" + (setting.Value ? "ON" : "OFF") + "]"))
			{
				setting.Value = !setting.Value;
				return true;
			}
			return false;
		}
	}
}
