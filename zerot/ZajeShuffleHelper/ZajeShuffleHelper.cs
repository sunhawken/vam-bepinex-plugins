using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroT.ZajeShuffleHelper
{
	// Headless helper for ZeroT's ZajeShuffle (.var): "Picks a random button from a UIAssist grid and activates it.
	// Triggerable action, configurable hotkey, persistent settings." Nothing to add to a session and no window; it runs by itself.
	//
	// Every feature of the .var, one for one
	//   Shuffle action ........ the "Shuffle" action, here on a storable called "ZeroT.ZajeShuffle" on the CoreControl atom
	//                           (triggers, keybindings and other plugins can call it exactly as they called the plugin's)
	//   SHUFFLE button ........ the same action (nothing to click: bind it, or use the hotkey / timer)
	//   Grid Label ............ setting GridLabel (+ storable "Grid Label")
	//   Skip Buttons .......... setting SkipButtons, comma separated, 1-based (+ storable "Skip Buttons")
	//   Hotkey ................ setting Hotkey (a Unity KeyCode name or None); actions "SetHotkey" (press any key, like the
	//                           "Press to Set Hotkey..." button) and "ClearHotkey"
	//   Cooldown (sec) ........ setting CooldownSeconds 0.1 - 10
	//   Auto Shuffle / Auto Interval (sec) ... settings AutoShuffle, AutoIntervalSeconds 5 - 300
	//   Save Settings ......... the "SaveSettings" action (settings are also saved by BepInEx as soon as they change); it also writes
	//                           the .var's own Saves/PluginData/ZeroT/ZajeShuffle/settings.json so both stay in step
	//   Persistent settings ... BepInEx\config\com.zerot.zajeshuffle.helper.cfg; on first run the .var's settings.json is imported
	//   Status / History ...... storables "Status" and "History" (last 20 picks, newest first) and the BepInEx log
	//
	// Where it is deliberately more correct than the .var
	//   - the history shows the button's real reference (A1, B2...). The .var looked for a field that UIAssist declares as a property,
	//     so it always showed "#n".
	//   - it does not pick a button whose action is still running (UIAssist refuses it and the .var reported success anyway)
	//   - the previous pick is avoided whenever another button is eligible
	//   - UIAssist's grid list is looked up once, not by walking every type of every assembly on each shuffle
	//   - the hotkey is ignored while you are typing in a text field
	// If the .var itself is loaded in the session, this helper steps aside (two of them would shuffle twice).
	[BepInPlugin("com.zerot.zajeshuffle.helper", "ZajeShuffle Helper", "1.0.0")]
	public sealed class ZajeShuffleHelperPlugin : BaseUnityPlugin
	{
		private const string SettingsDir = "Saves/PluginData/ZeroT/ZajeShuffle";
		private const string SettingsFile = "Saves/PluginData/ZeroT/ZajeShuffle/settings.json";
		internal const string StoreId = "ZeroT.ZajeShuffle";

		private ConfigEntry<bool> cfgEnabled;
		private ConfigEntry<string> cfgGrid, cfgSkip, cfgHotkey;
		private ConfigEntry<float> cfgCooldown, cfgInterval;
		private ConfigEntry<bool> cfgAuto, cfgAutoNow, cfgAvoidRepeat, cfgIgnoreTyping, cfgImported;
		private ConfigEntry<int> cfgHistory;

		// storables on CoreControl
		private Atom hostAtom;
		private JSONStorable host;
		private JSONStorableString stGrid, stSkip, stHotkey, stStatus, stHistory;
		private JSONStorableFloat stCooldown, stInterval;
		private JSONStorableBool stAuto;
		private bool syncing;

		// runtime
		private readonly System.Random rng = new System.Random();
		private float lastShuffleTime;
		private float nextAutoShuffle;
		private KeyCode currentHotkey = KeyCode.None;
		private readonly HashSet<int> skipSet = new HashSet<int>();
		private readonly List<string> shuffleHistory = new List<string>();
		private int lastPick = -1;
		private bool waitingForHotkey;
		private bool yielded;
		private bool wasAuto;
		private float nextHostCheck, nextOriginalCheck;
		private FieldInfo gridsField;
		private string status = "Ready";
		private string history = "";

		// -------------------------------------------------------------------- setup

		private void Awake()
		{
			cfgEnabled = Config.Bind<bool>("General", "Enabled", true, "Turn the whole helper off.");
			cfgGrid = Config.Bind<string>("Shuffle", "GridLabel", "Zaje", "Name of the UIAssist grid to shuffle (its Grid Label).");
			cfgSkip = Config.Bind<string>("Shuffle", "SkipButtons", "24,25", "Button numbers to skip, comma separated, counting from 1.");
			cfgCooldown = Config.Bind<float>("Shuffle", "CooldownSeconds", 1f, "Minimum seconds between shuffles (0.1 - 10).");
			cfgAvoidRepeat = Config.Bind<bool>("Shuffle", "AvoidRepeat", true, "Do not pick the same button twice in a row when another one is eligible.");
			cfgHotkey = Config.Bind<string>("Hotkey", "Key", "None", "Keyboard key that shuffles: a Unity KeyCode name such as F5 or Keypad0, or None.");
			cfgIgnoreTyping = Config.Bind<bool>("Hotkey", "IgnoreWhileTyping", true, "Do not shuffle when the key is pressed while a text field has the keyboard.");
			cfgAuto = Config.Bind<bool>("Auto", "AutoShuffle", false, "Shuffle on a timer.");
			cfgInterval = Config.Bind<float>("Auto", "AutoIntervalSeconds", 30f, "Seconds between automatic shuffles (5 - 300).");
			cfgAutoNow = Config.Bind<bool>("Auto", "ShuffleWhenSwitchedOn", true, "Shuffle at once when Auto Shuffle is switched on (what the .var did), then every interval.");
			cfgHistory = Config.Bind<int>("History", "Entries", 20, "How many picks the History keeps.");
			cfgImported = Config.Bind<bool>("Import", "ImportedPluginSettings", false, "Set after the .var's settings.json was imported once. Set to false to import it again.");
			Config.SettingChanged += OnSettingChanged;
			ParseSkip();
			ParseHotkey();
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			RemoveHost();
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			ConfigEntryBase c = e.ChangedSetting;
			if (c == cfgSkip)
			{
				ParseSkip();
			}
			else if (c == cfgHotkey)
			{
				ParseHotkey();
			}
			PushToStorables();
		}

		// -------------------------------------------------------------------- settings parsing

		private void ParseSkip()
		{
			skipSet.Clear();
			string raw = cfgSkip.Value;
			if (string.IsNullOrEmpty(raw))
			{
				return;
			}
			foreach (string part in raw.Split(','))
			{
				int v;
				if (int.TryParse(part.Trim(), out v))
				{
					skipSet.Add(v - 1);   // 1-based in the setting
				}
			}
		}

		private void ParseHotkey()
		{
			currentHotkey = KeyCode.None;
			string s = cfgHotkey.Value;
			if (string.IsNullOrEmpty(s) || s == "None")
			{
				return;
			}
			try
			{
				currentHotkey = (KeyCode)Enum.Parse(typeof(KeyCode), s, true);
			}
			catch
			{
			}
		}

		internal KeyCode Hotkey { get { return currentHotkey; } }
		internal string Status { get { return status; } }
		internal string History { get { return history; } }

		private void SetStatus(string s)
		{
			status = s;
			if (stStatus != null)
			{
				stStatus.val = s;
			}
			Logger.LogInfo(s);
		}

		// -------------------------------------------------------------------- the storable on CoreControl

		private void RemoveHost()
		{
			if (host != null)
			{
				UnityEngine.Object.Destroy(host.gameObject);
			}
			host = null;
			hostAtom = null;
		}

		private void EnsureHost()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			Atom core = sc.GetAtomByUid("CoreControl");
			if (core == null)
			{
				return;
			}
			if (host != null && host && hostAtom == core && core.GetStorableByID(StoreId) == host)
			{
				return;
			}
			RemoveHost();
			BuildHost(core);
		}

		private void BuildHost(Atom atom)
		{
			GameObject go = new GameObject("ZeroT.ZajeShuffle");
			go.transform.SetParent(atom.transform, false);
			host = go.AddComponent<JSONStorable>();
			host.overrideId = StoreId;
			host.exclude = true;               // settings live in the BepInEx config, not in scenes
			host.containingAtom = atom;
			hostAtom = atom;

			host.RegisterAction(new JSONStorableAction("Shuffle", delegate { DoShuffle("action"); }));
			host.RegisterAction(new JSONStorableAction("SetHotkey", delegate { BeginHotkeyCapture(); }));
			host.RegisterAction(new JSONStorableAction("ClearHotkey", delegate { ClearHotkey(); }));
			host.RegisterAction(new JSONStorableAction("SaveSettings", delegate { SaveSettings(); }));

			stGrid = new JSONStorableString("Grid Label", cfgGrid.Value, delegate (string v) { if (!syncing) { cfgGrid.Value = v; } });
			stSkip = new JSONStorableString("Skip Buttons", cfgSkip.Value, delegate (string v) { if (!syncing) { cfgSkip.Value = v; } });
			stHotkey = new JSONStorableString("Hotkey", cfgHotkey.Value, delegate (string v) { if (!syncing) { cfgHotkey.Value = v; } });
			stCooldown = new JSONStorableFloat("Cooldown (sec)", cfgCooldown.Value, delegate (float v) { if (!syncing) { cfgCooldown.Value = Mathf.Clamp(v, 0.1f, 10f); } }, 0.1f, 10f);
			stAuto = new JSONStorableBool("Auto Shuffle", cfgAuto.Value, delegate (bool v) { if (!syncing) { cfgAuto.Value = v; } });
			stInterval = new JSONStorableFloat("Auto Interval (sec)", cfgInterval.Value, delegate (float v) { if (!syncing) { cfgInterval.Value = Mathf.Clamp(v, 5f, 300f); } }, 5f, 300f);
			stStatus = new JSONStorableString("Status", status);
			stHistory = new JSONStorableString("History", history);
			host.RegisterString(stGrid); host.RegisterString(stSkip); host.RegisterString(stHotkey);
			host.RegisterFloat(stCooldown); host.RegisterBool(stAuto); host.RegisterFloat(stInterval);
			host.RegisterString(stStatus); host.RegisterString(stHistory);
			if (!atom.RegisterAdditionalStorable(host))
			{
				Logger.LogWarning("VaM did not accept the ZajeShuffle storable on CoreControl; the hotkey and the timer still work.");
			}
			else
			{
				Logger.LogInfo("ZajeShuffle actions are on CoreControl as '" + StoreId + "' (Shuffle, SetHotkey, ClearHotkey, SaveSettings).");
			}
		}

		// config -> storables
		private void PushToStorables()
		{
			if (host == null)
			{
				return;
			}
			syncing = true;
			try
			{
				stGrid.val = cfgGrid.Value; stSkip.val = cfgSkip.Value; stHotkey.val = cfgHotkey.Value;
				stCooldown.val = cfgCooldown.Value; stAuto.val = cfgAuto.Value; stInterval.val = cfgInterval.Value;
			}
			finally
			{
				syncing = false;
			}
		}

		// -------------------------------------------------------------------- the .var's saved settings

		private void ImportPluginSettings()
		{
			cfgImported.Value = true;
			try
			{
				if (!FileManagerSecure.FileExists(SettingsFile))
				{
					return;
				}
				JSONNode jn = SuperController.singleton.LoadJSON(SettingsFile);
				if (jn == null)
				{
					return;
				}
				JSONClass jc = jn.AsObject;
				if (jc.HasKey("gridLabel")) { cfgGrid.Value = jc["gridLabel"]; }
				if (jc.HasKey("skipButtons")) { cfgSkip.Value = jc["skipButtons"]; }
				if (jc.HasKey("hotkey")) { cfgHotkey.Value = jc["hotkey"]; }
				float f;
				if (jc.HasKey("cooldown") && float.TryParse(jc["cooldown"], out f)) { cfgCooldown.Value = Mathf.Clamp(f, 0.1f, 10f); }
				if (jc.HasKey("autoShuffle")) { cfgAuto.Value = jc["autoShuffle"].Value.ToLower() == "true"; }
				if (jc.HasKey("autoInterval") && float.TryParse(jc["autoInterval"], out f)) { cfgInterval.Value = Mathf.Clamp(f, 5f, 300f); }
				SetStatus("Settings loaded");
			}
			catch (Exception e)
			{
				SetStatus("Load error: " + e.Message);
			}
		}

		internal void SaveSettings()
		{
			try
			{
				Config.Save();
				if (!FileManagerSecure.DirectoryExists(SettingsDir))
				{
					FileManagerSecure.CreateDirectory(SettingsDir);
				}
				JSONClass jc = new JSONClass();
				jc["gridLabel"] = cfgGrid.Value;
				jc["skipButtons"] = cfgSkip.Value;
				jc["hotkey"] = cfgHotkey.Value;
				jc["cooldown"] = cfgCooldown.Value.ToString("R");
				jc["autoShuffle"] = cfgAuto.Value.ToString();
				jc["autoInterval"] = cfgInterval.Value.ToString("R");
				SuperController.singleton.SaveJSON(jc, SettingsFile);
				SetStatus("Settings saved");
			}
			catch (Exception e)
			{
				SetStatus("Save error: " + e.Message);
			}
		}

		// -------------------------------------------------------------------- hotkey

		internal void BeginHotkeyCapture()
		{
			SetStatus("Press any key to set hotkey...");
			waitingForHotkey = true;
		}

		internal void ClearHotkey()
		{
			cfgHotkey.Value = "None";
			currentHotkey = KeyCode.None;
			waitingForHotkey = false;
			SetStatus("Hotkey cleared");
			SaveSettings();
			SetStatus("Hotkey cleared");
		}

		private static bool TypingInTextField()
		{
			try
			{
				EventSystem es = EventSystem.current;
				if (es == null || es.currentSelectedGameObject == null)
				{
					return false;
				}
				InputField f = es.currentSelectedGameObject.GetComponent<InputField>();
				return f != null && f.isFocused;
			}
			catch
			{
				return false;
			}
		}

		// -------------------------------------------------------------------- the loop

		private bool OriginalLoaded()
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					Type t = a.GetType("ZajeShuffle", false);
					if (t != null && typeof(MVRScript).IsAssignableFrom(t))
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private void Update()
		{
			if (Time.unscaledTime >= nextOriginalCheck)
			{
				nextOriginalCheck = Time.unscaledTime + 2f;
				bool orig = OriginalLoaded();
				if (orig && !yielded)
				{
					yielded = true;
					RemoveHost();
					Logger.LogWarning("The ZajeShuffle .var is loaded in this session: this helper steps aside so there is only one shuffle.");
				}
				else if (!orig && yielded)
				{
					yielded = false;
				}
			}
			if (yielded || !cfgEnabled.Value)
			{
				if (!cfgEnabled.Value && host != null)
				{
					RemoveHost();
				}
				return;
			}
			if (Time.unscaledTime >= nextHostCheck)
			{
				nextHostCheck = Time.unscaledTime + 1f;
				SuperController sc = SuperController.singleton;
				if (sc != null && !sc.isLoading && !cfgImported.Value)
				{
					ImportPluginSettings();
				}
				EnsureHost();
			}

			// hotkey capture
			if (waitingForHotkey)
			{
				foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
				{
					if (kc == KeyCode.None || kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1 || kc == KeyCode.Mouse2)
					{
						continue;
					}
					if (Input.GetKeyDown(kc))
					{
						cfgHotkey.Value = kc.ToString();
						currentHotkey = kc;
						waitingForHotkey = false;
						SetStatus("Hotkey set to: " + kc.ToString());
						SaveSettings();
						SetStatus("Hotkey set to: " + kc.ToString());
						return;
					}
				}
				return;
			}

			if (currentHotkey != KeyCode.None && Input.GetKeyDown(currentHotkey) && !(cfgIgnoreTyping.Value && TypingInTextField()))
			{
				DoShuffle("hotkey");
			}

			bool auto = cfgAuto.Value;
			if (auto && !wasAuto)
			{
				nextAutoShuffle = cfgAutoNow.Value ? 0f : Time.time + Mathf.Clamp(cfgInterval.Value, 5f, 300f);
			}
			wasAuto = auto;
			if (auto && Time.time >= nextAutoShuffle)
			{
				nextAutoShuffle = Time.time + Mathf.Clamp(cfgInterval.Value, 5f, 300f);
				DoShuffle("timer");
			}
		}

		// -------------------------------------------------------------------- shuffle

		private object FindButtonGridsList()
		{
			if (gridsField == null)
			{
				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					try
					{
						foreach (Type t in asm.GetTypes())
						{
							if (t.Name == "UIAStorables")
							{
								FieldInfo f = t.GetField("buttonGridsList", BindingFlags.Public | BindingFlags.Static);
								if (f != null)
								{
									gridsField = f;
									break;
								}
							}
						}
					}
					catch
					{
					}
					if (gridsField != null)
					{
						break;
					}
				}
			}
			return gridsField == null ? null : gridsField.GetValue(null);
		}

		private static object Prop(object o, string name, params object[] index)
		{
			return o.GetType().GetProperty(name).GetValue(o, index.Length == 0 ? null : index);
		}

		private int FindGridIndex(object grids, string label)
		{
			int count = (int)Prop(grids, "Count");
			for (int i = 0; i < count; i++)
			{
				object grid = Prop(grids, "Item", i);
				FieldInfo lf = grid.GetType().GetField("gridLabelJSS", BindingFlags.Public | BindingFlags.Instance);
				if (lf == null)
				{
					continue;
				}
				object jss = lf.GetValue(grid);
				if (jss == null)
				{
					continue;
				}
				if ((string)Prop(jss, "val") == label)
				{
					return i;
				}
			}
			return -1;
		}

		private static bool Busy(object button)
		{
			try
			{
				PropertyInfo p = button.GetType().GetProperty("actionInProgress", BindingFlags.Public | BindingFlags.Instance);
				return p != null && (bool)p.GetValue(button, null);
			}
			catch
			{
				return false;
			}
		}

		private static string LabelOf(object button, int pick)
		{
			string label = "#" + (pick + 1);
			try
			{
				Type bt = button.GetType();
				object v = null;
				PropertyInfo p = bt.GetProperty("buttonRef", BindingFlags.Public | BindingFlags.Instance);
				if (p != null)
				{
					v = p.GetValue(button, null);
				}
				else
				{
					FieldInfo f = bt.GetField("buttonRef", BindingFlags.Public | BindingFlags.Instance);
					if (f != null)
					{
						v = f.GetValue(button);
					}
				}
				string s = v as string;
				if (!string.IsNullOrEmpty(s))
				{
					label = s;
				}
			}
			catch
			{
			}
			return label;
		}

		internal void DoShuffle(string source)
		{
			if (yielded || !cfgEnabled.Value)
			{
				return;
			}
			if (Time.time - lastShuffleTime < Mathf.Clamp(cfgCooldown.Value, 0.1f, 10f))
			{
				return;
			}
			lastShuffleTime = Time.time;
			ParseSkip();
			try
			{
				object grids = FindButtonGridsList();
				if (grids == null)
				{
					SetStatus("UIAssist not loaded yet");
					return;
				}
				int gridIdx = FindGridIndex(grids, cfgGrid.Value);
				if (gridIdx < 0)
				{
					SetStatus("Grid '" + cfgGrid.Value + "' not found");
					return;
				}
				object grid = Prop(grids, "Item", gridIdx);
				FieldInfo blField = grid.GetType().GetField("buttonList", BindingFlags.Public | BindingFlags.Instance);
				if (blField == null)
				{
					SetStatus("buttonList not found");
					return;
				}
				object buttonList = blField.GetValue(grid);
				int buttonCount = (int)Prop(buttonList, "Count");

				List<int> eligible = new List<int>();
				for (int i = 0; i < buttonCount; i++)
				{
					if (skipSet.Contains(i))
					{
						continue;
					}
					if (Busy(Prop(buttonList, "Item", i)))
					{
						continue;
					}
					eligible.Add(i);
				}
				if (cfgAvoidRepeat.Value && eligible.Count > 1)
				{
					eligible.Remove(lastPick);
				}
				if (eligible.Count == 0)
				{
					SetStatus("No eligible buttons");
					return;
				}
				int pick = eligible[rng.Next(eligible.Count)];
				lastPick = pick;

				object button = Prop(buttonList, "Item", pick);
				MethodInfo actionInit = button.GetType().GetMethod("ActionInit", BindingFlags.Public | BindingFlags.Instance);
				if (actionInit == null)
				{
					SetStatus("ActionInit not found");
					return;
				}
				actionInit.Invoke(button, new object[] { 0, false });

				string label = LabelOf(button, pick);
				string entry = DateTime.Now.ToString("HH:mm:ss") + " - " + label;
				shuffleHistory.Insert(0, entry);
				int max = Mathf.Clamp(cfgHistory.Value, 1, 200);
				while (shuffleHistory.Count > max)
				{
					shuffleHistory.RemoveAt(shuffleHistory.Count - 1);
				}
				history = string.Join("\n", shuffleHistory.ToArray());
				if (stHistory != null)
				{
					stHistory.val = history;
				}
				SetStatus("Now: " + label);
			}
			catch (Exception e)
			{
				SetStatus("Error: " + (e.InnerException ?? e).Message);
			}
		}
	}
}
