using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.BodyWeightStandalone
{
	// Headless standalone port of everlaster's BodyWeight (.var, "more realistic body mass, adjustable by region").
	// The plugin's own "plugin defaults" are applied automatically to every female Person atom: the rigidbody mass of each
	// body region (head, torso, arms, legs...) is set to a realistic value instead of VaM's default, scaled by one
	// Mass Multiplier. The VaM masses are remembered so switching the plugin off (or removing it) restores them.
	// No window, nothing to add to the atoms: settings live in BepInEx\config\com.zerot.bodyweight.cfg.
	[BepInPlugin("com.zerot.bodyweight", "BodyWeight Standalone", "1.0.0")]
	public sealed class BodyWeightStandalone : BaseUnityPlugin
	{
		private sealed class Receiver
		{
			public string key;
			public FreeControllerV3 control;
			public Rigidbody rb;
			public JSONStorableFloat vam;
			public JSONStorableFloat.SetFloatCallback callback;
			public bool ignoreMultiplier;
			public float vamMass;
			public float adopted = -1f;   // set when something else changed the controller's own mass after we started
		}

		private sealed class PersonState
		{
			public Atom atom;
			public readonly List<Receiver> receivers = new List<Receiver>();
		}

		// key, left controller, right controller (null = single), plugin default mass, ignores the multiplier
		private static readonly string[][] Table = new string[][]
		{
			new[] { "Head", "headControl", null, "4.70", "1" },
			new[] { "Chest", "chestControl", null, "10.00", "0" },
			new[] { "Abdomen2", "abdomen2Control", null, "1.50", "0" },
			new[] { "Abdomen", "abdomenControl", null, "3.00", "0" },
			new[] { "Pelvis", "pelvisControl", null, "4.00", "0" },
			new[] { "Hip", "hipControl", null, "8.00", "0" },
			new[] { "Shoulder", "lArmControl", "rArmControl", "2.00", "0" },
			new[] { "Arm", "lElbowControl", "rElbowControl", "2.00", "0" },
			new[] { "Hand", "lHandControl", "rHandControl", "0.20", "0" },
			new[] { "Thigh", "lThighControl", "rThighControl", "6.40", "0" },
			new[] { "Shin", "lKneeControl", "rKneeControl", "3.00", "0" },
			new[] { "Foot", "lFootControl", "rFootControl", "0.70", "0" },
			new[] { "Toes", "lToeControl", "rToeControl", "0.10", "0" },
		};

		private static readonly string[][] MaleTable = new string[][]
		{
			new[] { "PenisBase", "penisBaseControl", null, "0.10", "0" },
			new[] { "PenisMid", "penisMidControl", null, "0.10", "0" },
			new[] { "PenisTip", "penisTipControl", null, "0.10", "0" },
			new[] { "Testes", "testesControl", null, "0.10", "0" },
		};

		private ConfigEntry<bool> cfgActive;
		private ConfigEntry<bool> cfgMales;
		private ConfigEntry<float> cfgDelay;
		private ConfigEntry<float> cfgMultiplier;
		private ConfigEntry<bool> cfgSymmetric;
		private readonly Dictionary<string, ConfigEntry<float>> leftMass = new Dictionary<string, ConfigEntry<float>>();
		private readonly Dictionary<string, ConfigEntry<float>> rightMass = new Dictionary<string, ConfigEntry<float>>();

		private readonly Dictionary<Atom, PersonState> states = new Dictionary<Atom, PersonState>();
		private readonly Dictionary<Atom, float> firstSeen = new Dictionary<Atom, float>();
		private readonly HashSet<Atom> skipped = new HashSet<Atom>();
		private float nextScan;
		private float resyncAt = -1f;
		private bool wasActive = true;
		private bool wasLoading;

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn BodyWeight off: restores VaM's own masses.");
			cfgMales = Config.Bind<bool>("General", "IncludeMales", false, "Also apply to male Person atoms (adds the penis/testes masses). The original plugin is for female atoms.");
			cfgDelay = Config.Bind<float>("General", "ApplyDelaySeconds", 2f, "Wait this long after a scene loads or a Person appears before applying.");
			cfgMultiplier = Config.Bind<float>("Masses", "MassMultiplier", 1f, "Scales every mass except the head (0.1 - 5).");
			cfgSymmetric = Config.Bind<bool>("Masses", "LeftRightSymmetric", true, "Use the left value for both sides. Turn off to set the right side separately.");
			AddMasses(Table, "Masses");
			AddMasses(MaleTable, "Masses (male)");
			Config.SettingChanged += OnSettingChanged;
		}

		private void AddMasses(string[][] table, string section)
		{
			for (int i = 0; i < table.Length; i++)
			{
				string key = table[i][0];
				float def = float.Parse(table[i][3], System.Globalization.CultureInfo.InvariantCulture);
				leftMass[key] = Config.Bind<float>(section, table[i][2] != null ? "Left" + key : key, def, "Mass in kg for this body region (plugin default " + table[i][3] + ").");
				if (table[i][2] != null)
				{
					rightMass[key] = Config.Bind<float>(section, "Right" + key, def, "Used only when LeftRightSymmetric is off.");
				}
			}
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			RestoreAll();
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			if (e.ChangedSetting == cfgActive)
			{
				return;
			}
			resyncAt = Time.unscaledTime + 0.5f;
		}

		// ------------------------------------------------------------------ masses

		private float TargetFor(Receiver r, bool isRight, string key)
		{
			if (r.adopted >= 0f)
			{
				return r.adopted;
			}
			ConfigEntry<float> entry;
			if (isRight && !cfgSymmetric.Value && rightMass.TryGetValue(key, out entry))
			{
				return entry.Value;
			}
			return leftMass[key].Value;
		}

		private void Apply(Receiver r, bool isRight, string key)
		{
			if (r.rb == null)
			{
				return;
			}
			float mult = r.ignoreMultiplier ? 1f : Mathf.Clamp(cfgMultiplier.Value, 0.1f, 5f);
			r.rb.mass = Mathf.Max(0.1f, mult * TargetFor(r, isRight, key));
		}

		private static bool IsMale(Atom atom)
		{
			try
			{
				DAZCharacterSelector cs = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return cs != null && cs.gender == DAZCharacterSelector.Gender.Male;
			}
			catch
			{
				return false;
			}
		}

		private static bool HasOriginalPlugin(Atom atom)
		{
			try
			{
				foreach (string id in atom.GetStorableIDs())
				{
					if (id.IndexOf("BodyWeight", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return true;
					}
				}
			}
			catch
			{
			}
			return false;
		}

		private FreeControllerV3 Find(FreeControllerV3[] controllers, string name)
		{
			for (int i = 0; i < controllers.Length; i++)
			{
				if (controllers[i] != null && controllers[i].name == name)
				{
					return controllers[i];
				}
			}
			return null;
		}

		private void Setup(Atom atom)
		{
			PersonState s = new PersonState();
			s.atom = atom;
			FreeControllerV3[] controllers = atom.freeControllers;
			bool male = IsMale(atom);
			AddReceivers(s, controllers, Table, false);
			if (male)
			{
				AddReceivers(s, controllers, MaleTable, false);
			}
			states[atom] = s;
			Logger.LogInfo("BodyWeight applied to " + atom.uid + " (" + s.receivers.Count + " body parts).");
		}

		private void AddReceivers(PersonState s, FreeControllerV3[] controllers, string[][] table, bool unused)
		{
			for (int i = 0; i < table.Length; i++)
			{
				string key = table[i][0];
				bool ignore = table[i][4] == "1";
				for (int side = 0; side < 2; side++)
				{
					string controlName = table[i][side == 0 ? 1 : 2];
					if (controlName == null)
					{
						continue;
					}
					FreeControllerV3 control = Find(controllers, controlName);
					if (control == null || control.followWhenOffRB == null)
					{
						continue;
					}
					JSONStorableFloat vam = control.GetFloatJSONParam("mass");
					Receiver r = new Receiver();
					r.key = key;
					r.control = control;
					r.rb = control.followWhenOffRB;
					r.vam = vam;
					r.ignoreMultiplier = ignore;
					r.vamMass = vam != null ? vam.val : r.rb.mass;
					bool isRight = side == 1;
					if (vam != null)
					{
						Receiver captured = r;
						string capturedKey = key;
						bool capturedRight = isRight;
						r.callback = delegate(float val)
						{
							// the controller's own mass changed (UI, pose preset...): adopt it, like the original plugin does
							captured.vamMass = val;
							captured.adopted = val;
							Apply(captured, capturedRight, capturedKey);
						};
						vam.setCallbackFunction += r.callback;
					}
					Apply(r, isRight, key);
					s.receivers.Add(r);
				}
			}
		}

		private void ApplyAll()
		{
			foreach (PersonState s in states.Values)
			{
				for (int i = 0; i < s.receivers.Count; i++)
				{
					Receiver r = s.receivers[i];
					r.adopted = -1f;
					bool isRight = r.control != null && r.control.name.StartsWith("r") && r.control.name != "";
					// right-side controls of the paired regions
					isRight = IsRightControl(r.control != null ? r.control.name : "");
					Apply(r, isRight, r.key);
				}
			}
		}

		private static bool IsRightControl(string name)
		{
			return name == "rArmControl" || name == "rElbowControl" || name == "rHandControl" || name == "rThighControl" || name == "rKneeControl" || name == "rFootControl" || name == "rToeControl";
		}

		private void RestoreAll()
		{
			foreach (PersonState s in states.Values)
			{
				for (int i = 0; i < s.receivers.Count; i++)
				{
					Receiver r = s.receivers[i];
					if (r.vam != null && r.callback != null)
					{
						r.vam.setCallbackFunction -= r.callback;
					}
					if (r.rb != null)
					{
						r.rb.mass = r.vamMass;
					}
				}
			}
			states.Clear();
		}

		// ------------------------------------------------------------------ scheduling

		private void Update()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			float now = Time.unscaledTime;
			if (!cfgActive.Value)
			{
				if (wasActive)
				{
					wasActive = false;
					RestoreAll();
				}
				return;
			}
			wasActive = true;
			if (sc.isLoading)
			{
				wasLoading = true;
				return;
			}
			if (wasLoading)
			{
				wasLoading = false;
				states.Clear();   // the old scene's rigidbodies are gone
				firstSeen.Clear();
				skipped.Clear();
			}
			if (resyncAt >= 0f && now >= resyncAt)
			{
				resyncAt = -1f;
				ApplyAll();
			}
			if (now < nextScan)
			{
				return;
			}
			nextScan = now + 1f;
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null || a.type != "Person" || states.ContainsKey(a) || skipped.Contains(a))
				{
					continue;
				}
				if (IsMale(a) && !cfgMales.Value)
				{
					skipped.Add(a);
					continue;
				}
				float seen;
				if (!firstSeen.TryGetValue(a, out seen))
				{
					firstSeen[a] = now;
					continue;
				}
				if (now - seen < cfgDelay.Value)
				{
					continue;
				}
				if (HasOriginalPlugin(a))
				{
					skipped.Add(a);
					Logger.LogInfo(a.uid + " already has the BodyWeight plugin attached; leaving it to that.");
					continue;
				}
				try
				{
					Setup(a);
				}
				catch (Exception e)
				{
					skipped.Add(a);
					Logger.LogWarning("BodyWeight on " + a.uid + " failed: " + e.Message);
				}
			}
		}
	}
}
