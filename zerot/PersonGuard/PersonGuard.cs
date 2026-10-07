using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.PersonGuard
{
	// Headless safety net for Person atoms. It does not change your scenes or looks; it only steps in when a person is
	// clearly broken:
	//  * Exploding persons: limb speeds are capped and the depenetration "pop" speed is lowered (the usual cause of a body
	//    flying apart when colliders start inside each other). If a body part is NaN or ends up far from its person, the
	//    person is put back to its last healthy pose and its physics is reset.
	//  * Bad morphs: NaN/Infinity and absurd values are repaired. An optional morph LOCK (hotkey) freezes every morph of every
	//    person at its current value and silently reverts anything that changes them - handy against random morph changes
	//    while you are not editing looks.
	[BepInPlugin("com.zerot.personguard", "Person Guard", "1.0.0")]
	public sealed class PersonGuardPlugin : BaseUnityPlugin
	{
		private sealed class PersonState
		{
			public Atom atom;
			public Rigidbody[] bodies;
			public FreeControllerV3[] controllers;
			public Vector3[] goodPos;
			public Quaternion[] goodRot;
			public bool hasGood;
			public float lastGoodTime;
			public float lastAnomaly = -100f;
			public float pausedUntil;
			public readonly List<float> recoveries = new List<float>();
			public readonly HashSet<Rigidbody> tuned = new HashSet<Rigidbody>();
			public readonly Dictionary<DAZMorph, float> lockSnapshot = new Dictionary<DAZMorph, float>();
			public bool lockedOnce;
		}

		private ConfigEntry<bool> cfgPhysics;
		private ConfigEntry<float> cfgMaxVelocity;
		private ConfigEntry<float> cfgMaxAngular;
		private ConfigEntry<float> cfgDepenetration;
		private ConfigEntry<bool> cfgRecover;
		private ConfigEntry<float> cfgExplodeDistance;
		private ConfigEntry<bool> cfgMorphRepair;
		private ConfigEntry<float> cfgMorphCap;
		private ConfigEntry<bool> cfgLock;
		private ConfigEntry<KeyboardShortcut> cfgLockKey;
		private ConfigEntry<bool> cfgMessages;

		private readonly Dictionary<Atom, PersonState> states = new Dictionary<Atom, PersonState>();
		private readonly List<Atom> toRemove = new List<Atom>();
		private float nextRefresh;
		private float nextMorphTick;
		private int morphCursor;
		private float lastLoadingEnd;
		private bool wasLoading;
		private bool lockWasOn;

		private void Awake()
		{
			cfgPhysics = Config.Bind<bool>("Physics", "ClampLimbSpeeds", true, "Cap the speed of every person body part so a blown-up joint cannot throw a limb across the scene.");
			cfgMaxVelocity = Config.Bind<float>("Physics", "MaxVelocity", 40f, "Maximum body-part speed in metres per second.");
			cfgMaxAngular = Config.Bind<float>("Physics", "MaxAngularVelocity", 80f, "Maximum body-part spin in radians per second.");
			cfgDepenetration = Config.Bind<float>("Physics", "MaxDepenetrationVelocity", 4f, "How fast overlapping colliders may push apart (Unity default 10). Lower = fewer explosive pops. 0 = leave as is.");
			cfgRecover = Config.Bind<bool>("Physics", "AutoRecover", true, "If a body part is NaN or far away from its person, restore the last healthy pose and reset that person's physics.");
			cfgExplodeDistance = Config.Bind<float>("Physics", "ExplodeDistanceMetres", 30f, "A body part farther than this from its person's main control counts as exploded.");
			cfgMorphRepair = Config.Bind<bool>("Morphs", "RepairInvalidMorphs", true, "Reset NaN/Infinity morph values and clamp absurdly large ones.");
			cfgMorphCap = Config.Bind<float>("Morphs", "MorphAbsoluteLimit", 6f, "Morph values beyond +/- this are clamped back to it.");
			cfgLock = Config.Bind<bool>("Morphs", "LockMorphs", false, "While on, every person's morphs are frozen at their current values and any change is reverted (turn off to edit looks).");
			cfgLockKey = Config.Bind<KeyboardShortcut>("Morphs", "LockToggleKey", new KeyboardShortcut(KeyCode.M, KeyCode.LeftControl, KeyCode.LeftAlt), "Hotkey that switches LockMorphs on and off.");
			cfgMessages = Config.Bind<bool>("General", "ShowMessages", true, "Show a short message in VaM when the guard repairs something.");
		}

		// ------------------------------------------------------------------ persons

		private void RefreshPersons()
		{
			SuperController sc = SuperController.singleton;
			List<Atom> atoms = sc.GetAtoms();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null || a.type != "Person" || states.ContainsKey(a))
				{
					continue;
				}
				PersonState s = new PersonState();
				s.atom = a;
				states[a] = s;
			}
			toRemove.Clear();
			foreach (KeyValuePair<Atom, PersonState> kv in states)
			{
				if (kv.Key == null)
				{
					toRemove.Add(kv.Key);
				}
			}
			for (int i = 0; i < toRemove.Count; i++)
			{
				states.Remove(toRemove[i]);
			}
		}

		private static bool Healthy(Vector3 v)
		{
			return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
		}

		private void Message(string text)
		{
			Logger.LogInfo(text);
			if (cfgMessages.Value)
			{
				try
				{
					SuperController.LogMessage("PersonGuard: " + text);
				}
				catch
				{
				}
			}
		}

		private void Prepare(PersonState s)
		{
			if (s.bodies == null)
			{
				s.bodies = s.atom.rigidbodies;
				s.controllers = s.atom.freeControllers;
				if (s.controllers != null)
				{
					s.goodPos = new Vector3[s.controllers.Length];
					s.goodRot = new Quaternion[s.controllers.Length];
				}
			}
		}

		// ------------------------------------------------------------------ update

		private void Update()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				return;
			}
			if (sc.isLoading)
			{
				wasLoading = true;
				return;
			}
			if (wasLoading)
			{
				wasLoading = false;
				lastLoadingEnd = Time.unscaledTime;
			}

			if (cfgLockKey.Value.IsDown())
			{
				cfgLock.Value = !cfgLock.Value;
				Message(cfgLock.Value ? "morphs LOCKED (Ctrl+Alt+M to unlock)" : "morphs unlocked");
			}
			if (cfgLock.Value != lockWasOn)
			{
				lockWasOn = cfgLock.Value;
				foreach (PersonState ps in states.Values)
				{
					ps.lockSnapshot.Clear();
					ps.lockedOnce = false;
				}
			}

			float now = Time.unscaledTime;
			if (now >= nextRefresh)
			{
				nextRefresh = now + 1f;
				RefreshPersons();
				foreach (PersonState s in states.Values)
				{
					TakeSnapshot(s, now);
				}
			}
			if (now >= nextMorphTick && states.Count > 0 && now - lastLoadingEnd > 3f)
			{
				nextMorphTick = now + 0.25f;
				List<PersonState> list = new List<PersonState>(states.Values);
				morphCursor = (morphCursor + 1) % list.Count;
				MorphPass(list[morphCursor], now);
			}
		}

		private void FixedUpdate()
		{
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading || sc.freezeAnimation || states.Count == 0)
			{
				return;
			}
			float now = Time.unscaledTime;
			float maxV = Mathf.Max(1f, cfgMaxVelocity.Value);
			float maxW = Mathf.Max(1f, cfgMaxAngular.Value);
			float depen = cfgDepenetration.Value;
			float explode = Mathf.Max(2f, cfgExplodeDistance.Value);
			foreach (PersonState s in states.Values)
			{
				if (s.atom == null || !s.atom.on || now < s.pausedUntil)
				{
					continue;
				}
				Prepare(s);
				Rigidbody[] bodies = s.bodies;
				if (bodies == null)
				{
					continue;
				}
				Vector3 center = Vector3.zero;
				bool hasCenter = false;
				if (s.atom.mainController != null)
				{
					center = s.atom.mainController.transform.position;
					hasCenter = Healthy(center);
				}
				bool anomaly = false;
				for (int i = 0; i < bodies.Length; i++)
				{
					Rigidbody rb = bodies[i];
					if (rb == null)
					{
						continue;
					}
					Vector3 p = rb.position;
					if (!Healthy(p) || !Healthy(rb.velocity))
					{
						anomaly = true;
						break;
					}
					if (hasCenter && (p - center).sqrMagnitude > explode * explode)
					{
						anomaly = true;
						break;
					}
					if (rb.isKinematic)
					{
						continue;
					}
					if (depen > 0f && s.tuned.Add(rb))
					{
						rb.maxDepenetrationVelocity = depen;
					}
					if (cfgPhysics.Value)
					{
						Vector3 v = rb.velocity;
						float sq = v.sqrMagnitude;
						if (sq > maxV * maxV)
						{
							rb.velocity = v * (maxV / Mathf.Sqrt(sq));
						}
						Vector3 w = rb.angularVelocity;
						float wsq = w.sqrMagnitude;
						if (wsq > maxW * maxW)
						{
							rb.angularVelocity = w * (maxW / Mathf.Sqrt(wsq));
						}
					}
				}
				if (anomaly)
				{
					s.lastAnomaly = now;
					if (cfgRecover.Value)
					{
						Recover(s, now);
					}
				}
			}
		}

		// ------------------------------------------------------------------ explosion recovery

		private void TakeSnapshot(PersonState s, float now)
		{
			if (s.atom == null || !s.atom.on)
			{
				return;
			}
			Prepare(s);
			if (s.controllers == null || now - s.lastAnomaly < 3f || now < s.pausedUntil)
			{
				return;
			}
			for (int i = 0; i < s.controllers.Length; i++)
			{
				FreeControllerV3 c = s.controllers[i];
				if (c == null || c.control == null)
				{
					continue;
				}
				Vector3 p = c.control.position;
				if (!Healthy(p))
				{
					return;   // never store a broken pose
				}
			}
			for (int i = 0; i < s.controllers.Length; i++)
			{
				FreeControllerV3 c = s.controllers[i];
				if (c == null || c.control == null)
				{
					continue;
				}
				s.goodPos[i] = c.control.position;
				s.goodRot[i] = c.control.rotation;
			}
			s.hasGood = true;
			s.lastGoodTime = now;
		}

		private void Recover(PersonState s, float now)
		{
			s.recoveries.RemoveAll(delegate(float t) { return now - t > 60f; });
			if (s.recoveries.Count >= 3)
			{
				s.pausedUntil = now + 60f;
				Message(s.atom.uid + " kept breaking - guard paused for 60 s");
				return;
			}
			bool repeat = s.recoveries.Count > 0 && now - s.recoveries[s.recoveries.Count - 1] < 20f;
			s.recoveries.Add(now);
			try
			{
				for (int i = 0; i < s.bodies.Length; i++)
				{
					Rigidbody rb = s.bodies[i];
					if (rb != null && !rb.isKinematic)
					{
						if (Healthy(rb.position))
						{
							rb.velocity = Vector3.zero;
							rb.angularVelocity = Vector3.zero;
						}
					}
				}
				if (s.hasGood && s.controllers != null)
				{
					for (int i = 0; i < s.controllers.Length; i++)
					{
						FreeControllerV3 c = s.controllers[i];
						if (c == null || c.control == null)
						{
							continue;
						}
						c.control.position = s.goodPos[i];
						c.control.rotation = s.goodRot[i];
					}
				}
				s.atom.ResetPhysics(repeat, false);
				Message(s.atom.uid + " physics recovered" + (s.hasGood ? " (pose restored)" : ""));
			}
			catch (Exception e)
			{
				Logger.LogWarning("Recovering " + s.atom.uid + " failed: " + e.Message);
			}
		}

		// ------------------------------------------------------------------ morphs

		private void MorphPass(PersonState s, float now)
		{
			if (s.atom == null || !s.atom.on)
			{
				return;
			}
			DAZCharacterSelector cs = s.atom.GetStorableByID("geometry") as DAZCharacterSelector;
			if (cs == null)
			{
				return;
			}
			bool repair = cfgMorphRepair.Value;
			bool locked = cfgLock.Value;
			if (!repair && !locked)
			{
				return;
			}
			float cap = Mathf.Max(1f, cfgMorphCap.Value);
			int fixedCount = 0;
			int reverted = 0;
			bool firstLock = locked && !s.lockedOnce;
			DAZMorphBank[] banks = { cs.morphBank1, cs.morphBank2, cs.morphBank3 };
			for (int b = 0; b < banks.Length; b++)
			{
				DAZMorphBank bank = banks[b];
				if (bank == null || bank.morphs == null)
				{
					continue;
				}
				List<DAZMorph> list = bank.morphs;
				for (int i = 0; i < list.Count; i++)
				{
					DAZMorph m = list[i];
					if (m == null)
					{
						continue;
					}
					float v = m.morphValue;
					if (repair && v != 0f)
					{
						if (float.IsNaN(v) || float.IsInfinity(v))
						{
							m.morphValue = 0f;
							v = 0f;
							fixedCount++;
						}
						else if (v > cap || v < -cap)
						{
							v = Mathf.Clamp(v, -cap, cap);
							m.morphValue = v;
							fixedCount++;
						}
					}
					if (locked)
					{
						if (firstLock)
						{
							if (v != 0f)
							{
								s.lockSnapshot[m] = v;
							}
						}
						else if (v != 0f)
						{
							float want;
							if (!s.lockSnapshot.TryGetValue(m, out want))
							{
								want = 0f;
							}
							if (Mathf.Abs(v - want) > 0.0005f)
							{
								m.morphValue = want;
								reverted++;
							}
						}
					}
				}
			}
			if (locked)
			{
				if (firstLock)
				{
					s.lockedOnce = true;
				}
				else
				{
					// morphs that were non-zero at lock time and have been zeroed since
					List<DAZMorph> keys = new List<DAZMorph>(s.lockSnapshot.Keys);
					for (int i = 0; i < keys.Count; i++)
					{
						DAZMorph m = keys[i];
						if (m == null)
						{
							s.lockSnapshot.Remove(m);
							continue;
						}
						float want = s.lockSnapshot[m];
						if (Mathf.Abs(m.morphValue - want) > 0.0005f)
						{
							m.morphValue = want;
							reverted++;
						}
					}
				}
			}
			if (fixedCount > 0)
			{
				Message("repaired " + fixedCount + " invalid morph value(s) on " + s.atom.uid);
			}
			if (reverted > 0)
			{
				Logger.LogInfo("Morph lock reverted " + reverted + " change(s) on " + s.atom.uid);
			}
		}
	}
}
