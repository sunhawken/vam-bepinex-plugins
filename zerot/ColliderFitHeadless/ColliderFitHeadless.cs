using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.ColliderFitHeadless
{
	// Keeps the fixed colliders on every female Person snug to the skin as the body morphs change, without adding a plugin to any atom.
	//
	// VaM rescales its AutoColliders (torso, breasts, belly, hips, thighs) with the body morphs, but the fixed capsules on the arms, hands, feet,
	// neck and jaw keep the size stored in the character prefab, so a slimmed forearm still collides as if it were thick. This plugin measures the skin
	// around each of those colliders a moment after the non-pose morphs stop changing and fits the radius and centre to it (and updates the GPU
	// colliders hair and cloth use). Everything it changed is put back when it is switched off.
	//
	// It works with Collider Editor: colliders adjusted in the Collider Editor scene plugin, or listed in the preset that Collider Editor (Headless)
	// (ColliderEditor.Headless.dll) applies, keep their values and are skipped here; when that changes the colliders are taken over / handed back
	// by themselves.
	//
	// Idea and method follow mutoe's ColliderFit scene plugin (CC BY, https://hub.virtamate.com/members/mutoe.99301/). This is an independent
	// implementation; none of its code is used.
	[BepInPlugin(PluginGuid, "ColliderFit Headless", "1.0.0")]
	public sealed class ColliderFitHeadlessPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.zerot.colliderfit.headless";

		private static ManualLogSource log;

		private ConfigEntry<float> cfgMargin;
		private ConfigEntry<string> cfgExcluded;
		private ConfigEntry<bool> cfgMales;
		private ConfigEntry<bool> cfgSkipScene;
		private ConfigEntry<float> cfgScan;
		private ConfigEntry<float> cfgSettle;
		private ConfigEntry<float> cfgQuiet;
		private ConfigEntry<bool> cfgLog;

		internal readonly CeLink Ce = new CeLink();
		private readonly Dictionary<Atom, FitHost> hosts = new Dictionary<Atom, FitHost>();
		private readonly List<FitHost> ordered = new List<FitHost>();
		private readonly List<Atom> stale = new List<Atom>();
		private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private float nextScan;

		// window
		private RlUguiWindow win;
		private Text status;
		private Text table;
		private Text atomLabel;
		private Text marginLabel;
		private Button fitNow, rebind, dump, atomPrev, atomNext, atomToggle, marginMinus, marginPlus, marginZero;
		private int selected;
		private float nextUi;
		private readonly FitHost.Summary sum = new FitHost.Summary();

		internal float MarginMm
		{
			get { return cfgMargin.Value; }
		}

		internal float SettleSeconds
		{
			get { return Mathf.Max(0.2f, cfgSettle.Value); }
		}

		internal float QuietSeconds
		{
			get { return Mathf.Max(0.1f, cfgQuiet.Value); }
		}

		private bool On
		{
			get { return win == null || win.Active; }
		}

		internal void Log(string msg)
		{
			if (cfgLog != null && cfgLog.Value && log != null)
			{
				log.LogInfo(msg);
			}
		}

		internal static void Warn(string msg)
		{
			if (log != null)
			{
				log.LogWarning(msg);
			}
		}

		private void Awake()
		{
			log = Logger;
			cfgMargin = Config.Bind<float>("Fit", "MarginMm", 0f, new ConfigDescription("How much thicker (+) or thinner (-) than the skin the colliders sit, in millimetres. 0 puts the collider surface on the average skin position.", new AcceptableValueRange<float>(-10f, 10f)));
			cfgExcluded = Config.Bind<string>("Targets", "ExcludedAtoms", "", "Comma-separated atom UIDs to leave alone.");
			cfgMales = Config.Bind<bool>("Targets", "IncludeMales", false, "Also fit male Persons (only the female body was tested by the original plugin).");
			cfgSkipScene = Config.Bind<bool>("Targets", "SkipAtomsWithColliderFitPlugin", true, "Leave an atom alone if it already carries the ColliderFit scene plugin (mutoe.ColliderFit), so the two do not fight.");
			cfgScan = Config.Bind<float>("Timing", "ScanInterval", 2f, new ConfigDescription("Seconds between scans for new or changed Person atoms.", new AcceptableValueRange<float>(0.25f, 30f)));
			cfgSettle = Config.Bind<float>("Timing", "SettleSeconds", 2f, new ConfigDescription("Seconds to wait after a scene load, a new atom or a look change before the first fit (lets Collider Editor build its values first).", new AcceptableValueRange<float>(0.2f, 15f)));
			cfgQuiet = Config.Bind<float>("Timing", "QuietSeconds", 0.4f, new ConfigDescription("Seconds the body morphs must stay still before the colliders are refitted.", new AcceptableValueRange<float>(0.1f, 5f)));
			cfgLog = Config.Bind<bool>("General", "LogEvents", true, "Log when an atom's colliders are fitted or put back.");
			cfgExcluded.SettingChanged += delegate
			{
				ParseExcluded();
				nextScan = 0f;
			};
			cfgMargin.SettingChanged += delegate
			{
				for (int i = 0; i < ordered.Count; i++)
				{
					ordered[i].MarkDirty();
				}
				RefreshMargin();
			};
			cfgMales.SettingChanged += delegate { nextScan = 0f; };
			cfgSkipScene.SettingChanged += delegate { nextScan = 0f; };
			ParseExcluded();
			win = new RlUguiWindow(this, "ColliderFit", "Window", 40f, 60f, 380f, 470f, 300f, 330f, 900f, 900f);
			Logger.LogInfo("ColliderFit Headless 1.0.0 loaded. Auto-fitting the fixed colliders on female Person atoms.");
		}

		private IEnumerator Start()
		{
			while (SuperController.singleton == null || EventSystem.current == null)
			{
				yield return null;
			}
			BuildWindow();
		}

		private void OnEnable()
		{
			if (win != null)
			{
				win.SetActive(true);
			}
			nextScan = 0f;
		}

		private void OnDisable()
		{
			RemoveAll();
			if (win != null)
			{
				win.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			RemoveAll();
			if (win != null)
			{
				win.Dispose();
			}
		}

		private void ParseExcluded()
		{
			excluded.Clear();
			string[] parts = (cfgExcluded.Value ?? "").Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i].Trim();
				if (p.Length > 0)
				{
					excluded.Add(p);
				}
			}
		}

		// ---------------------------------------------------------------- finding the atoms

		private void Update()
		{
			if (win != null && win.Window != null)
			{
				win.Tick();
				if (Time.unscaledTime >= nextUi)
				{
					nextUi = Time.unscaledTime + 0.4f;
					RefreshUi();
				}
			}
			if (!On)
			{
				if (hosts.Count > 0)
				{
					RemoveAll();    // switched off (the window, the HUD Dock or the config manager): every collider goes back
				}
				return;
			}
			float now = Time.unscaledTime;
			if (now < nextScan)
			{
				return;
			}
			nextScan = now + cfgScan.Value;
			try
			{
				Scan();
			}
			catch (Exception e)
			{
				Logger.LogError("Scan failed: " + e);
			}
		}

		private void Scan()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			stale.Clear();
			foreach (KeyValuePair<Atom, FitHost> kv in hosts)
			{
				if (kv.Key == null || kv.Value == null || !IsTarget(kv.Key))
				{
					stale.Add(kv.Key);
				}
			}
			for (int i = 0; i < stale.Count; i++)
			{
				Detach(stale[i]);
			}
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null || hosts.ContainsKey(a) || !IsTarget(a))
				{
					continue;
				}
				FitHost h = a.gameObject.GetComponent<FitHost>();
				if (h == null)
				{
					h = a.gameObject.AddComponent<FitHost>();
				}
				h.Init(this, a);
				hosts[a] = h;
				ordered.Add(h);
				Log("Watching " + a.uid + ": its fixed colliders are fitted to the skin " + SettleSeconds.ToString("0.#") + " s from now.");
			}
			ordered.RemoveAll(delegate(FitHost h) { return h == null; });
			ordered.Sort(delegate(FitHost x, FitHost y) { return string.Compare(x.atom.uid, y.atom.uid, StringComparison.OrdinalIgnoreCase); });
		}

		private bool IsTarget(Atom atom)
		{
			if (atom == null || atom.type != "Person" || excluded.Contains(atom.uid))
			{
				return false;
			}
			if (!cfgMales.Value && !IsFemale(atom))
			{
				return false;
			}
			if (cfgSkipScene.Value && HasColliderFitPlugin(atom))
			{
				return false;
			}
			return true;
		}

		internal static bool IsFemale(Atom atom)
		{
			DAZCharacterSelector sel = atom.GetComponentInChildren<DAZCharacterSelector>(true);
			if (sel == null)
			{
				return false;
			}
			if (sel.selectedCharacter != null)
			{
				return !sel.selectedCharacter.isMale;
			}
			return sel.gender == DAZCharacterSelector.Gender.Female;
		}

		private static bool HasColliderFitPlugin(Atom atom)
		{
			List<string> ids = atom.GetStorableIDs();
			for (int i = 0; i < ids.Count; i++)
			{
				string id = ids[i];
				if (id.StartsWith("plugin#") && id.IndexOf("ColliderFit") >= 0 && id.IndexOf("Headless") < 0)
				{
					return true;
				}
			}
			return false;
		}

		private void Detach(Atom atom)
		{
			FitHost h;
			if (hosts.TryGetValue(atom, out h))
			{
				if (h != null)
				{
					ordered.Remove(h);
					Destroy(h);
				}
				hosts.Remove(atom);
			}
		}

		private void RemoveAll()
		{
			foreach (FitHost h in hosts.Values)
			{
				if (h != null)
				{
					Destroy(h);
				}
			}
			hosts.Clear();
			ordered.Clear();
		}

		// ---------------------------------------------------------------- window

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h) { LayoutBody(w, h); };
			win.OnActiveChanged = delegate(bool on)
			{
				if (on)
				{
					nextScan = 0f;
				}
				else
				{
					RemoveAll();
				}
				RefreshUi();
			};
			win.Build("ColliderFit Headless Desktop");
			Transform body = win.Body;
			status = RlUguiWindow.NewText("Status", body, "", TextAnchor.UpperLeft);
			table = RlUguiWindow.NewText("Table", body, "", TextAnchor.UpperLeft);
			fitNow = RlUguiWindow.NewButton("Fit now", body, "Fit now", delegate { ForEachSelectedOrAll(true); });
			rebind = RlUguiWindow.NewButton("Rebind", body, "Rebind all", delegate { for (int i = 0; i < ordered.Count; i++) { ordered[i].Rebind(); } });
			dump = RlUguiWindow.NewButton("Dump", body, "Log details", delegate { LogDump(); });
			marginMinus = RlUguiWindow.NewButton("Margin -", body, "-", delegate { SetMargin(cfgMargin.Value - 0.5f); });
			marginPlus = RlUguiWindow.NewButton("Margin +", body, "+", delegate { SetMargin(cfgMargin.Value + 0.5f); });
			marginZero = RlUguiWindow.NewButton("Margin 0", body, "0", delegate { SetMargin(0f); });
			marginLabel = RlUguiWindow.NewText("Margin", body, "", TextAnchor.MiddleLeft);
			atomPrev = RlUguiWindow.NewButton("Atom prev", body, "<", delegate { Step(-1); });
			atomNext = RlUguiWindow.NewButton("Atom next", body, ">", delegate { Step(1); });
			atomToggle = RlUguiWindow.NewButton("Atom toggle", body, "Skip this atom", delegate { ToggleSelectedExcluded(); });
			atomLabel = RlUguiWindow.NewText("Atom", body, "", TextAnchor.MiddleCenter);
			win.AddResizeHandle();
			win.Apply();
			RefreshUi();
		}

		private void LayoutBody(float w, float h)
		{
			if (status == null)
			{
				return;
			}
			float m = RlUguiWindow.Margin;
			float inner = Mathf.Max(60f, w - 2f * m);
			float y = 30f;
			RlUguiWindow.SetRect(status.rectTransform, m, y, inner, 62f);
			y += 66f;
			float third = (inner - 8f) / 3f;
			RlUguiWindow.SetRect(RectOf(fitNow), m, y, third, 26f);
			RlUguiWindow.SetRect(RectOf(rebind), m + third + 4f, y, third, 26f);
			RlUguiWindow.SetRect(RectOf(dump), m + 2f * (third + 4f), y, third, 26f);
			y += 32f;
			float b = 26f;
			RlUguiWindow.SetRect(RectOf(marginMinus), m, y, b, 24f);
			RlUguiWindow.SetRect(marginLabel.rectTransform, m + b + 4f, y, inner - 3f * b - 12f, 24f);
			RlUguiWindow.SetRect(RectOf(marginPlus), m + inner - 2f * b - 4f, y, b, 24f);
			RlUguiWindow.SetRect(RectOf(marginZero), m + inner - b, y, b, 24f);
			y += 30f;
			RlUguiWindow.SetRect(RectOf(atomPrev), m, y, b, 24f);
			RlUguiWindow.SetRect(atomLabel.rectTransform, m + b + 4f, y, inner - 2f * b - 8f - 96f, 24f);
			RlUguiWindow.SetRect(RectOf(atomNext), m + inner - b - 96f - 4f, y, b, 24f);
			RlUguiWindow.SetRect(RectOf(atomToggle), m + inner - 96f, y, 96f, 24f);
			y += 30f;
			RlUguiWindow.SetRect(table.rectTransform, m, y, inner, Mathf.Max(40f, h - y - 22f));
		}

		private static RectTransform RectOf(Button b)
		{
			return b.GetComponent<RectTransform>();
		}

		private void SetMargin(float v)
		{
			cfgMargin.Value = Mathf.Clamp((float)Math.Round(v, 1), -10f, 10f);
			Config.Save();
		}

		private void Step(int d)
		{
			if (ordered.Count == 0)
			{
				return;
			}
			selected = ((selected + d) % ordered.Count + ordered.Count) % ordered.Count;
			RefreshUi();
		}

		private FitHost Selected()
		{
			if (ordered.Count == 0)
			{
				return null;
			}
			if (selected >= ordered.Count)
			{
				selected = 0;
			}
			return ordered[selected];
		}

		private void ForEachSelectedOrAll(bool fit)
		{
			for (int i = 0; i < ordered.Count; i++)
			{
				ordered[i].RefitNow();
			}
		}

		private void ToggleSelectedExcluded()
		{
			FitHost h = Selected();
			if (h == null)
			{
				return;
			}
			string uid = h.atom.uid;
			List<string> list = new List<string>();
			string[] parts = (cfgExcluded.Value ?? "").Split(',');
			bool had = false;
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i].Trim();
				if (p.Length == 0)
				{
					continue;
				}
				if (string.Equals(p, uid, StringComparison.OrdinalIgnoreCase))
				{
					had = true;
				}
				else
				{
					list.Add(p);
				}
			}
			if (!had)
			{
				list.Add(uid);
			}
			cfgExcluded.Value = string.Join(",", list.ToArray());
			Config.Save();
		}

		private void LogDump()
		{
			for (int i = 0; i < ordered.Count; i++)
			{
				Logger.LogInfo(ordered[i].Dump());
			}
			if (ordered.Count == 0)
			{
				Logger.LogInfo("ColliderFit: no female Person atoms are being fitted.");
			}
		}

		private void RefreshMargin()
		{
			if (marginLabel != null)
			{
				marginLabel.text = "Margin: " + cfgMargin.Value.ToString("+0.0;-0.0;0.0") + " mm";
			}
		}

		private string CeLine()
		{
			StringBuilder sb = new StringBuilder("Collider Editor (Headless): ");
			if (!Ce.HeadlessFound)
			{
				sb.Append("not installed");
			}
			else if (!Ce.HeadlessActive)
			{
				sb.Append("switched off");
			}
			else
			{
				sb.Append("active, ").Append(Ce.PresetCount).Append(Ce.PresetCount == 1 ? " collider kept" : " colliders kept");
			}
			if (Ce.HeadlessError != null)
			{
				sb.Append(" (").Append(Ce.HeadlessError).Append(')');
			}
			return sb.ToString();
		}

		private void RefreshUi()
		{
			if (status == null)
			{
				return;
			}
			Ce.Poll();
			RefreshMargin();
			int bound = 0;
			for (int i = 0; i < ordered.Count; i++)
			{
				if (ordered[i].IsBound)
				{
					bound++;
				}
			}
			StringBuilder sb = new StringBuilder();
			if (!On)
			{
				sb.Append("Plugin is Off - every collider is back to its original size.");
			}
			else
			{
				sb.Append("Female atoms: ").Append(ordered.Count).Append("   fitted: ").Append(bound).Append('\n');
				sb.Append(CeLine());
			}
			status.text = sb.ToString();
			FitHost h = Selected();
			if (h != null)
			{
				h.Summarize(sum);
			}
			atomLabel.text = h == null ? "(no female atom)" : h.atom.uid + "  " + (selected + 1) + "/" + ordered.Count;
			RlUguiWindow.SetLabel(atomToggle, h != null && excluded.Contains(h.atom.uid) ? "Fit this atom" : "Skip this atom");
			StringBuilder t = new StringBuilder();
			if (h == null)
			{
				t.Append("Nothing to show yet. A female Person atom shows up here a few seconds after the scene loads.");
			}
			else
			{
				t.Append(h.IsBound ? h.StateText : (h.BindError != null ? "Cannot bind: " + h.BindError : h.StateText)).Append("   (").Append(h.AdvancedSet ? "advanced" : "standard").Append(" colliders)\n");
				t.Append("following ").Append(sum.Following).Append(" / ").Append(sum.Total).Append("   Collider Editor ").Append(sum.Owned).Append("   others ").Append(sum.Foreign).Append("   few vertices ").Append(sum.TooFew).Append("   out of range ").Append(sum.Odd).Append('\n');
				for (int g = 0; g < FitBinder.GroupCount; g++)
				{
					if (sum.GroupTotal[g] == 0)
					{
						continue;
					}
					t.Append(FitBinder.GroupNames[g]).Append(": ").Append(sum.GroupFollow[g]).Append(" / ").Append(sum.GroupTotal[g]).Append('\n');
				}
				for (int i = 0; i < sum.Exceptions.Count; i++)
				{
					t.Append("  ").Append(sum.Exceptions[i]).Append('\n');
				}
				t.Append("bind ").Append(h.BindMs.ToString("0")).Append(" ms   refit ").Append(h.RefitMs.ToString("0.0")).Append(" ms   refits ").Append(h.Refits);
				if (h.MorphsSkipped > 0)
				{
					t.Append("   morphs without data ").Append(h.MorphsSkipped);
				}
			}
			table.text = t.ToString();
		}
	}
}
