using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.ExtraAutoGenitalsStandalone
{
	// Headless standalone port of LFE's ExtraAutoGenitals 0.3 (.var, "move extra female genital morphs automatically").
	// It runs by itself on EVERY female Person atom (each with its own state - the original shared its settings between
	// persons): the labia / genital morphs follow the movement of whatever touches the LabiaTrigger. No window and
	// nothing to add to the atom; settings live in BepInEx\config\com.zerot.extraautogenitals.cfg.
	[BepInPlugin("com.zerot.extraautogenitals", "ExtraAutoGenitals Standalone", "1.0.0")]
	public sealed class ExtraAutoGenitalsStandalone : BaseUnityPlugin
	{
		private sealed class MorphDefault
		{
			public string Name;
			public bool Enabled;
			public bool Reverse;
			public float InwardMax;
			public float OutwardMax;
			public MorphDefault(string n, bool e, bool r, float i, float o)
			{
				Name = n; Enabled = e; Reverse = r; InwardMax = i; OutwardMax = o;
			}
		}

		private sealed class MorphConfig
		{
			public string Name;
			public ConfigEntry<bool> Enabled;
			public ConfigEntry<bool> Reverse;
			public ConfigEntry<float> InwardMax;
			public ConfigEntry<float> OutwardMax;
			public ConfigEntry<float> InwardExaggeration;
			public ConfigEntry<float> OutwardExaggeration;
			public ConfigEntry<float> Friction;
		}

		private static readonly MorphDefault[] Defaults =
		{
			new MorphDefault("Labia minora-size", true, false, 0.7f, 2f),
			new MorphDefault("Labia minora-style1", true, false, 0.7f, 2f),
			new MorphDefault("Labia minora-exstrophy", true, true, 0.1f, 1f),
			new MorphDefault("Labia majora-relaxation", true, false, 1f, 0f),
			new MorphDefault("Gen_Innie", true, true, 0.10f, 0.25f),
			new MorphDefault("Gens In - Out", false, true, 1f, 0f),
		};

		private sealed class PersonState
		{
			public Atom atom;
			public CollisionTriggerEventHandler labiaHandler;
			public FreeControllerV3 abdomen;
			public readonly List<Animator> animators = new List<Animator>();
			public float? prevDistance;
			public float? prevVelocity;
			public bool failed;
		}

		private ConfigEntry<bool> cfgActive;
		private ConfigEntry<float> cfgDelay;
		private ConfigEntry<string> cfgExtra;
		private readonly List<MorphConfig> morphConfigs = new List<MorphConfig>();
		private readonly Dictionary<Atom, PersonState> states = new Dictionary<Atom, PersonState>();
		private readonly Dictionary<Atom, float> firstSeen = new Dictionary<Atom, float>();
		private readonly HashSet<Atom> skipped = new HashSet<Atom>();
		private readonly List<Collider> colliderBuffer = new List<Collider>();
		private float nextScan;
		private float rebuildAt = -1f;
		private bool wasActive = true;
		private bool wasLoading;

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn it off: the genital morphs go back to their default values.");
			cfgDelay = Config.Bind<float>("General", "ApplyDelaySeconds", 2f, "Wait this long after a scene loads or a female Person appears before starting.");
			cfgExtra = Config.Bind<string>("General", "AdditionalMorphs", "", "Extra morphs to drive, comma separated display names (each gets inward/outward max 0.5, not reversed).");
			for (int i = 0; i < Defaults.Length; i++)
			{
				MorphDefault d = Defaults[i];
				MorphConfig c = new MorphConfig();
				c.Name = d.Name;
				string sec = d.Name;
				c.Enabled = Config.Bind<bool>(sec, "Enabled", d.Enabled, "Drive this morph.");
				c.Reverse = Config.Bind<bool>(sec, "Reverse", d.Reverse, "Treat this as an inward morph (reverse direction).");
				c.InwardMax = Config.Bind<float>(sec, "InwardMax", d.InwardMax, "Largest inward movement (-5 to 5).");
				c.OutwardMax = Config.Bind<float>(sec, "OutwardMax", d.OutwardMax, "Largest outward movement (-5 to 5).");
				c.InwardExaggeration = Config.Bind<float>(sec, "InwardExaggeration", 0f, "Extra inward movement (0 to 5).");
				c.OutwardExaggeration = Config.Bind<float>(sec, "OutwardExaggeration", 0f, "Extra outward movement (0 to 5).");
				c.Friction = Config.Bind<float>(sec, "Friction", 1f, "Response amount (0 to 1, 0 = off).");
				morphConfigs.Add(c);
			}
			Config.SettingChanged += OnSettingChanged;
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			ResetAll();
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			if (e.ChangedSetting == cfgActive)
			{
				return;
			}
			rebuildAt = Time.unscaledTime + 0.5f;
		}

		// ------------------------------------------------------------------ animation (ported from LabiaAnimator)

		private sealed class Animator
		{
			private const int Lookback = 64;
			private const float StdDevMax = 2f;
			private const float SpeedMin = 0.08f;

			public DAZMorph Morph;
			public float RestingValue;
			public bool IsInward;
			public float InwardMax;
			public float InwardExaggeration;
			public float OutwardMax;
			public float OutwardExaggeration;
			public float Friction = 1f;
			public bool Enabled = true;

			private int iteration;
			private readonly float[] history = new float[Lookback];
			private float smooth1;
			private float smooth2;
			private float smooth3;

			public float Current
			{
				get { return Morph != null ? Morph.morphValue : 0f; }
			}

			public float? Next(float? velocityRaw)
			{
				float friction = Mathf.Clamp(Friction, 0f, 1f);
				float targetMin = RestingValue - (IsInward ? OutwardMax + OutwardExaggeration : InwardMax + InwardExaggeration);
				float targetMax = RestingValue + (IsInward ? InwardMax + InwardExaggeration : OutwardMax + OutwardExaggeration);
				float velocity = Mathf.Clamp(velocityRaw ?? 0f, -1f, 1f);
				if (!Enabled || friction <= 0f)
				{
					return null;
				}
				if (!velocityRaw.HasValue)
				{
					return Mathf.Clamp(Mathf.SmoothDamp(Current, RestingValue, ref smooth2, SpeedMin), targetMin, targetMax);
				}
				if (LooksLikeMistake(velocity))
				{
					return Current;
				}
				if (Mathf.Approximately(velocity, 0f))
				{
					return Mathf.Clamp(Mathf.SmoothDamp(Current, RestingValue, ref smooth3, 100f), targetMin, targetMax);
				}
				float pct = Mathf.InverseLerp(targetMin, targetMax, Current);
				float delta = velocity * friction * (IsInward ? 1f : -1f) * 1f * 10f;
				float target = Mathf.Clamp(RestingValue + delta, targetMin, targetMax);
				return Mathf.SmoothDamp(Current, target, ref smooth1, SpeedMin);
			}

			private bool LooksLikeMistake(float velocity)
			{
				int idx = iteration % (Lookback - 1);
				history[idx] = velocity;
				iteration++;
				if (iteration >= Lookback || iteration < 0)
				{
					iteration = 0;
				}
				float sum = 0f;
				for (int i = 0; i < history.Length; i++)
				{
					sum += history[i];
				}
				float average = sum / history.Length;
				double var = 0.0;
				for (int i = 0; i < history.Length; i++)
				{
					double d = history[i] - average;
					var += d * d;
				}
				double stddev = Math.Sqrt(var / history.Length);
				if (stddev > 0.0)
				{
					float off = Mathf.Abs((velocity - average) / (float)stddev);
					if (off > StdDevMax)
					{
						return true;
					}
				}
				return false;
			}
		}

		private Animator MakeAnimator(DAZCharacterSelector cs, string morphName, bool enabled, bool reverse, float inMax, float outMax, float inEx, float outEx, float friction)
		{
			DAZMorph morph = cs.morphsControlUI.GetMorphByDisplayName(morphName);
			if (morph == null)
			{
				return null;
			}
			Animator a = new Animator();
			a.Morph = morph;
			a.RestingValue = morph.jsonFloat != null ? morph.jsonFloat.defaultVal : 0f;
			a.IsInward = reverse;
			a.InwardMax = inMax;
			a.OutwardMax = outMax;
			a.InwardExaggeration = inEx;
			a.OutwardExaggeration = outEx;
			a.Friction = friction;
			a.Enabled = enabled;
			morph.SetDefaultValue();
			return a;
		}

		// ------------------------------------------------------------------ persons

		private static bool IsFemale(Atom atom)
		{
			try
			{
				DAZCharacterSelector cs = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return cs != null && cs.gender == DAZCharacterSelector.Gender.Female;
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
					if (id.IndexOf("ExtraAutoGenitals", StringComparison.OrdinalIgnoreCase) >= 0)
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

		private void Setup(Atom atom)
		{
			PersonState s = new PersonState();
			s.atom = atom;
			CollisionTrigger trigger = atom.GetComponentsInChildren<CollisionTrigger>().FirstOrDefault(t => t.name == "LabiaTrigger");
			if (trigger == null)
			{
				skipped.Add(atom);
				Logger.LogInfo(atom.uid + " has no LabiaTrigger; skipped.");
				return;
			}
			s.labiaHandler = trigger.gameObject.GetComponentInChildren<CollisionTriggerEventHandler>();
			s.abdomen = atom.freeControllers.FirstOrDefault(fc => fc.name == "abdomen2Control");
			if (s.labiaHandler == null || s.abdomen == null)
			{
				skipped.Add(atom);
				Logger.LogInfo(atom.uid + " is missing the labia handler or abdomen control; skipped.");
				return;
			}
			BuildAnimators(s);
			states[atom] = s;
			Logger.LogInfo("ExtraAutoGenitals running on " + atom.uid + " (" + s.animators.Count + " morphs).");
		}

		private void BuildAnimators(PersonState s)
		{
			s.animators.Clear();
			DAZCharacterSelector cs = (DAZCharacterSelector)s.atom.GetStorableByID("geometry");
			for (int i = 0; i < morphConfigs.Count; i++)
			{
				MorphConfig c = morphConfigs[i];
				Animator a = MakeAnimator(cs, c.Name, c.Enabled.Value, c.Reverse.Value, c.InwardMax.Value, c.OutwardMax.Value, c.InwardExaggeration.Value, c.OutwardExaggeration.Value, c.Friction.Value);
				if (a != null)
				{
					s.animators.Add(a);
				}
			}
			string extra = cfgExtra.Value;
			if (!string.IsNullOrEmpty(extra))
			{
				string[] names = extra.Split(',');
				for (int i = 0; i < names.Length; i++)
				{
					string n = names[i].Trim();
					if (n.Length == 0)
					{
						continue;
					}
					Animator a = MakeAnimator(cs, n, true, false, 0.5f, 0.5f, 0f, 0f, 1f);
					if (a != null)
					{
						s.animators.Add(a);
					}
				}
			}
			s.prevDistance = null;
			s.prevVelocity = null;
		}

		private void ResetAll()
		{
			foreach (PersonState s in states.Values)
			{
				for (int i = 0; i < s.animators.Count; i++)
				{
					try
					{
						if (s.animators[i].Morph != null)
						{
							s.animators[i].Morph.SetDefaultValue();
						}
					}
					catch
					{
					}
				}
			}
			states.Clear();
		}

		// ------------------------------------------------------------------ update

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
					ResetAll();
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
				states.Clear();
				firstSeen.Clear();
				skipped.Clear();
			}
			if (rebuildAt >= 0f && now >= rebuildAt)
			{
				rebuildAt = -1f;
				foreach (PersonState ps in states.Values)
				{
					try
					{
						BuildAnimators(ps);
					}
					catch (Exception e)
					{
						Logger.LogWarning("Rebuilding " + ps.atom.uid + " failed: " + e.Message);
					}
				}
			}
			if (now >= nextScan)
			{
				nextScan = now + 1f;
				ScanAtoms(now, sc);
			}
			if (sc.freezeAnimation)
			{
				return;
			}
			foreach (PersonState s in states.Values)
			{
				if (s.failed || s.atom == null || !s.atom.on)
				{
					continue;
				}
				try
				{
					Drive(s);
				}
				catch (Exception e)
				{
					s.failed = true;
					Logger.LogWarning("ExtraAutoGenitals stopped on " + s.atom.uid + ": " + e.Message);
				}
			}
		}

		private void ScanAtoms(float now, SuperController sc)
		{
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null || a.type != "Person" || states.ContainsKey(a) || skipped.Contains(a))
				{
					continue;
				}
				if (!IsFemale(a))
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
					Logger.LogInfo(a.uid + " already has the ExtraAutoGenitals plugin attached; leaving it to that.");
					continue;
				}
				try
				{
					Setup(a);
				}
				catch (Exception e)
				{
					skipped.Add(a);
					Logger.LogWarning("ExtraAutoGenitals on " + a.uid + " failed: " + e.Message);
				}
			}
		}

		private void Drive(PersonState s)
		{
			colliderBuffer.Clear();
			foreach (Collider c in s.labiaHandler.collidingWithDictionary.Keys)
			{
				colliderBuffer.Add(c);
			}
			Vector3 abdomenPos = s.abdomen.transform.position;
			float shortest = 0f;
			if (colliderBuffer.Count > 0)
			{
				shortest = float.MaxValue;
				for (int i = 0; i < colliderBuffer.Count; i++)
				{
					Collider c = colliderBuffer[i];
					if (c == null)
					{
						continue;
					}
					float d = Vector3.Distance(c.transform.position, abdomenPos);
					if (d < shortest)
					{
						shortest = d;
					}
				}
				if (shortest == float.MaxValue)
				{
					shortest = 0f;
				}
			}
			float velocity = ((s.prevDistance ?? 0f) - shortest) / Mathf.Max(0.0001f, Time.deltaTime);
			if (s.prevDistance.HasValue && s.prevVelocity.HasValue)
			{
				for (int i = 0; i < s.animators.Count; i++)
				{
					Animator a = s.animators[i];
					if (!a.Enabled || a.Friction <= 0f)
					{
						continue;
					}
					float? v = a.Next(colliderBuffer.Count > 0 ? (float?)velocity : null);
					if (v.HasValue)
					{
						a.Morph.morphValueAdjustLimits = v.Value;
					}
				}
			}
			if (Mathf.Approximately(velocity, 0f))
			{
				velocity = s.prevVelocity ?? 0f;
			}
			s.prevDistance = shortest;
			s.prevVelocity = velocity;
		}
	}
}
