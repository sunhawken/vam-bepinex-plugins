using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.MicroPhysicsFixerStandalone
{
	// Headless standalone port of Kimowal's MicroPhysics Fixer (.var). It does what the plugin's "Apply Basic Fix" does,
	// by itself, to every Person atom in the scene: clamps each body part's mass / drag / angular drag into safe ranges
	// (per body region, with presets, a chest/glute stabilizer, the soft-tissue genital physics and the optional solver /
	// interpolation boosts), keeps a backup of the original values so it can undo, and runs the real-time safety monitor.
	// No window and no scene plugin: settings live in BepInEx\config\com.zerot.microphysicsfixer.cfg.
	[BepInPlugin("com.zerot.microphysicsfixer", "MicroPhysics Fixer Standalone", "1.0.0")]
	public sealed class MicroPhysicsFixerStandalone : BaseUnityPlugin
	{
		private sealed class Backup
		{
			public Rigidbody rb;
			public float mass;
			public float drag;
			public float angularDrag;
			public int solverIterations;
			public int solverVelocityIterations;
			public RigidbodyInterpolation interpolation;
			public float maxAngularVelocity;
			public CollisionDetectionMode collisionDetectionMode;
			public float maxDepenetrationVelocity;
			public Collider collider;
			public PhysicMaterial originalMaterial;
			public bool materialChanged;
			public Vector3 applied;
		}

		private ConfigEntry<bool> cfgActive;
		private ConfigEntry<bool> cfgMales;
		private ConfigEntry<float> cfgDelay;
		private ConfigEntry<string> cfgPreset;
		private ConfigEntry<float> cfgPresetIntensity;
		private ConfigEntry<string> cfgStabilizer;
		private ConfigEntry<string> cfgCurve;
		private ConfigEntry<float> cfgMinMass, cfgMaxMass, cfgMinDrag, cfgMaxDrag, cfgMinAD, cfgMaxAD;
		private ConfigEntry<bool> fixHeadNeck, fixChest, fixAbdomenPelvis, fixBreastGlute, fixGenital, fixArmsHands, fixLegsFeet, fixOther;
		private ConfigEntry<float> intHeadNeck, intChest, intAbdomenPelvis, intBreastGlute, intGenital, intArmsHands, intLegsFeet, intOther;
		private ConfigEntry<bool> cfgSolver;
		private ConfigEntry<float> cfgSolverIntensity;
		private ConfigEntry<bool> cfgInterp;
		private ConfigEntry<bool> cfgMaxAng;
		private ConfigEntry<float> cfgMaxAngValue;
		private ConfigEntry<bool> cfgHighPrecision;
		private ConfigEntry<string> cfgGenMode;
		private ConfigEntry<bool> cfgGenMaterial;
		private ConfigEntry<float> cfgGenFriction;
		private ConfigEntry<float> cfgGenBounce;
		private ConfigEntry<bool> cfgGenRegional;
		private ConfigEntry<bool> cfgMonitor;
		private ConfigEntry<float> cfgMonitorInterval;
		private ConfigEntry<string> cfgMonitorMode;

		private const float AbsMinMass = 0.05f;
		private const float AbsMaxMass = 50f;
		private const float AbsMinDrag = 0.01f;
		private const float AbsMaxDrag = 50f;
		private const float AbsMinAD = 0.01f;
		private const float AbsMaxAD = 50f;

		private readonly Dictionary<Rigidbody, Backup> backups = new Dictionary<Rigidbody, Backup>();
		private readonly Dictionary<Atom, float> firstSeen = new Dictionary<Atom, float>();
		private readonly HashSet<Atom> applied = new HashSet<Atom>();
		private PhysicMaterial genitalMaterial;
		private float nextScan;
		private float nextMonitor;
		private float reapplyAt = -1f;
		private bool wasActive = true;
		private bool wasLoading;
		private int lastCount = -1;

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn the fixer off: restores every body part to the values it had before.");
			cfgMales = Config.Bind<bool>("General", "IncludeMales", true, "Also fix male Person atoms.");
			cfgDelay = Config.Bind<float>("General", "ApplyDelaySeconds", 3f, "Wait this long after a scene loads or a Person appears before fixing it.");
			cfgPreset = Config.Bind<string>("Thresholds", "Preset", "None", "None, Realistic, Soft Anime, Firm Athletic or Hyper Jiggle. None uses the thresholds below.");
			cfgPresetIntensity = Config.Bind<float>("Thresholds", "PresetIntensity", 1f, "0.5 - 1.5, scales the preset's drag limits.");
			cfgStabilizer = Config.Bind<string>("Thresholds", "ChestGluteStabilizer", "Normal", "Off, Normal or Strong - extra damping for chest, breast and glute.");
			cfgCurve = Config.Bind<string>("Thresholds", "JiggleCurve", "Linear", "Linear, Soft or Sharp - how region intensity blends the fix in.");
			cfgMinMass = Config.Bind<float>("Thresholds", "MinMass", 0.2f, "Smallest body-part mass allowed.");
			cfgMaxMass = Config.Bind<float>("Thresholds", "MaxMass", 2.5f, "Largest body-part mass allowed.");
			cfgMinDrag = Config.Bind<float>("Thresholds", "MinDrag", 0.08f, "Smallest drag allowed.");
			cfgMaxDrag = Config.Bind<float>("Thresholds", "MaxDrag", 3f, "Largest drag allowed.");
			cfgMinAD = Config.Bind<float>("Thresholds", "MinAngularDrag", 0.02f, "Smallest angular drag allowed.");
			cfgMaxAD = Config.Bind<float>("Thresholds", "MaxAngularDrag", 3f, "Largest angular drag allowed.");
			fixHeadNeck = Config.Bind<bool>("Regions", "FixHeadNeck", true, "");
			fixChest = Config.Bind<bool>("Regions", "FixChest", true, "");
			fixAbdomenPelvis = Config.Bind<bool>("Regions", "FixAbdomenPelvis", true, "");
			fixBreastGlute = Config.Bind<bool>("Regions", "FixBreastGlute", true, "");
			fixGenital = Config.Bind<bool>("Regions", "FixGenital", true, "");
			fixArmsHands = Config.Bind<bool>("Regions", "FixArmsHands", true, "");
			fixLegsFeet = Config.Bind<bool>("Regions", "FixLegsFeet", true, "");
			fixOther = Config.Bind<bool>("Regions", "FixOther", true, "");
			intHeadNeck = Config.Bind<float>("Regions", "IntensityHeadNeck", 1f, "0 = leave alone, 1 = full fix, up to 2 = extra jiggle.");
			intChest = Config.Bind<float>("Regions", "IntensityChest", 1f, "");
			intAbdomenPelvis = Config.Bind<float>("Regions", "IntensityAbdomenPelvis", 1f, "");
			intBreastGlute = Config.Bind<float>("Regions", "IntensityBreastGlute", 1f, "");
			intGenital = Config.Bind<float>("Regions", "IntensityGenital", 0.5f, "");
			intArmsHands = Config.Bind<float>("Regions", "IntensityArmsHands", 1f, "");
			intLegsFeet = Config.Bind<float>("Regions", "IntensityLegsFeet", 1f, "");
			intOther = Config.Bind<float>("Regions", "IntensityOther", 1f, "");
			cfgSolver = Config.Bind<bool>("Advanced", "SolverBoost", false, "Raise physics solver iterations on fixed body parts.");
			cfgSolverIntensity = Config.Bind<float>("Advanced", "SolverBoostIntensity", 1f, "0.5 - 2.");
			cfgInterp = Config.Bind<bool>("Advanced", "Interpolation", false, "Interpolate fixed body parts for smoother motion.");
			cfgMaxAng = Config.Bind<bool>("Advanced", "LimitMaxAngularVelocity", false, "Set the maximum angular velocity of fixed body parts.");
			cfgMaxAngValue = Config.Bind<float>("Advanced", "MaxAngularVelocity", 10f, "7 - 20.");
			cfgHighPrecision = Config.Bind<bool>("Advanced", "HighPrecisionCollisions", false, "Continuous collision detection on key bones.");
			cfgGenMode = Config.Bind<string>("Genital", "Mode", "Soft Tissue", "Standard, Soft Tissue, Ultra-Sensitive or Penetration-Ready.");
			cfgGenMaterial = Config.Bind<bool>("Genital", "PhysicMaterial", true, "Give genital colliders a friction/bounce physic material.");
			cfgGenFriction = Config.Bind<float>("Genital", "Friction", 0.4f, "0.15 - 1.");
			cfgGenBounce = Config.Bind<float>("Genital", "Bounciness", 0f, "0 - 0.3.");
			cfgGenRegional = Config.Bind<bool>("Genital", "RegionalPhysics", true, "Different softness for Gen1 / Gen2 / Gen3 / labia.");
			cfgMonitor = Config.Bind<bool>("Monitoring", "RealtimeMonitoring", true, "Watch the fixed body parts and correct drift.");
			cfgMonitorInterval = Config.Bind<float>("Monitoring", "IntervalSeconds", 0.5f, "How often the monitor checks.");
			cfgMonitorMode = Config.Bind<string>("Monitoring", "Mode", "Safety Limits", "Observe Only, Safety Limits (only fix near-zero / absurd values) or Lock Values (snap back to the fixed values).");
			Config.SettingChanged += OnSettingChanged;
		}

		private void OnDestroy()
		{
			Config.SettingChanged -= OnSettingChanged;
			Undo();
		}

		private void OnSettingChanged(object sender, SettingChangedEventArgs e)
		{
			if (e.ChangedSetting == cfgActive)
			{
				return;
			}
			if (e.ChangedSetting == cfgMonitor || e.ChangedSetting == cfgMonitorInterval || e.ChangedSetting == cfgMonitorMode)
			{
				return;
			}
			reapplyAt = Time.unscaledTime + 1f;
		}

		// ------------------------------------------------------------------ helpers (ported from the plugin)

		private static float Clamp(float v, float lo, float hi)
		{
			return v < lo ? lo : (v > hi ? hi : v);
		}

		private static string Category(string rbName)
		{
			string name = rbName.ToLower();
			if (name.Contains("labia") || name.Contains("vagina") || name.Contains("gens") || name.Contains("vulva") || name.Contains("clitoris") || name.Contains("genital"))
			{
				return "Genital";
			}
			if (name.Contains("chest") || name.Contains("thorax")) return "Chest";
			if (name.Contains("abdomen") || name.Contains("belly")) return "Abdomen";
			if (name.Contains("pelvis") || name.Contains("hip")) return "Pelvis";
			if (name.Contains("breast") || name.Contains("pectoral")) return "Breast";
			if (name.Contains("glute") || name.Contains("buttock")) return "Glute";
			if (name.Contains("head") || name.Contains("neck")) return "Head/Neck";
			if (name.Contains("thigh") || name.Contains("leg")) return "Leg";
			if (name.Contains("arm") || name.Contains("forearm") || name.Contains("shoulder")) return "Arm";
			if (name.Contains("hand") || name.Contains("finger")) return "Hand";
			if (name.Contains("foot") || name.Contains("toe")) return "Foot";
			return "Other";
		}

		private bool ShouldFix(string part)
		{
			if (part == "Head/Neck") return fixHeadNeck.Value;
			if (part == "Chest") return fixChest.Value;
			if (part == "Abdomen" || part == "Pelvis") return fixAbdomenPelvis.Value;
			if (part == "Breast" || part == "Glute") return fixBreastGlute.Value;
			if (part == "Genital") return fixGenital.Value;
			if (part == "Arm" || part == "Hand") return fixArmsHands.Value;
			if (part == "Leg" || part == "Foot") return fixLegsFeet.Value;
			return fixOther.Value;
		}

		private float RegionIntensity(string part)
		{
			if (part == "Head/Neck") return intHeadNeck.Value;
			if (part == "Chest") return intChest.Value;
			if (part == "Abdomen" || part == "Pelvis") return intAbdomenPelvis.Value;
			if (part == "Breast" || part == "Glute") return intBreastGlute.Value;
			if (part == "Genital") return intGenital.Value;
			if (part == "Arm" || part == "Hand") return intArmsHands.Value;
			if (part == "Leg" || part == "Foot") return intLegsFeet.Value;
			return intOther.Value;
		}

		private float JiggleCurve(float t)
		{
			string c = cfgCurve.Value;
			if (c == "Soft")
			{
				return Mathf.Sqrt(Mathf.Clamp01(t));
			}
			if (c == "Sharp")
			{
				t = Mathf.Clamp01(t);
				return t * t;
			}
			return Mathf.Clamp01(t);
		}

		private struct Limits
		{
			public float minMass, maxMass, minDrag, maxDrag, minAD, maxAD;
		}

		private Limits ComputeLimits()
		{
			Limits l = new Limits();
			l.minMass = cfgMinMass.Value;
			l.maxMass = cfgMaxMass.Value;
			l.minDrag = cfgMinDrag.Value;
			l.maxDrag = cfgMaxDrag.Value;
			l.minAD = cfgMinAD.Value;
			l.maxAD = cfgMaxAD.Value;
			string preset = cfgPreset.Value;
			if (string.IsNullOrEmpty(preset) || preset == "None")
			{
				return l;
			}
			float bMinM = 0.2f, bMaxM = 2.5f, bMinD = 0.05f, bMaxD = 3f, bMinA = 0.02f, bMaxA = 3f;
			if (preset == "Realistic")
			{
				bMinM = 0.2f; bMaxM = 2.8f; bMinD = 0.05f; bMaxD = 3.2f; bMinA = 0.02f; bMaxA = 3.2f;
			}
			else if (preset == "Soft Anime")
			{
				bMinM = 0.18f; bMaxM = 1.8f; bMinD = 0.08f; bMaxD = 2.5f; bMinA = 0.02f; bMaxA = 2.5f;
			}
			else if (preset == "Firm Athletic")
			{
				bMinM = 0.25f; bMaxM = 3.0f; bMinD = 0.07f; bMaxD = 3.5f; bMinA = 0.025f; bMaxA = 3.5f;
			}
			else if (preset == "Hyper Jiggle")
			{
				bMinM = 0.16f; bMaxM = 1.6f; bMinD = 0.06f; bMaxD = 2.2f; bMinA = 0.018f; bMaxA = 2.2f;
			}
			float intensity = Clamp(cfgPresetIntensity.Value, 0.5f, 1.5f);
			l.minMass = bMinM;
			l.maxMass = bMaxM;
			l.minDrag = Clamp(bMinD / intensity, 0f, 1f);
			l.maxDrag = Clamp(bMaxD / intensity, 0.5f, 10f);
			l.minAD = Clamp(bMinA / intensity, 0.001f, 1f);
			l.maxAD = Clamp(bMaxA / intensity, 0.5f, 10f);
			return l;
		}

		private static bool IsMale(Atom atom)
		{
			try
			{
				DAZCharacterSelector character = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return character != null && character.selectedCharacter != null && character.selectedCharacter.isMale;
			}
			catch
			{
				return false;
			}
		}

		// ------------------------------------------------------------------ the fix (port of ApplyBasicFix)

		private int FixPerson(Atom person, Limits lim)
		{
			Rigidbody[] bodies = person.GetComponentsInChildren<Rigidbody>(true);
			if (bodies == null)
			{
				return 0;
			}
			int changedCount = 0;
			string stabilizer = cfgStabilizer.Value;
			bool stabOff = stabilizer == "Off";
			bool stabStrong = stabilizer == "Strong";
			for (int i = 0; i < bodies.Length; i++)
			{
				Rigidbody rb = bodies[i];
				if (rb == null || backups.ContainsKey(rb))
				{
					continue;
				}
				string part = Category(rb.name);
				if (!ShouldFix(part))
				{
					continue;
				}
				float m = rb.mass;
				float d = rb.drag;
				float ad = rb.angularDrag;

				int origSolverIterations = rb.solverIterations;
				int origSolverVelocityIterations = rb.solverVelocityIterations;
				RigidbodyInterpolation origInterpolation = rb.interpolation;
				float origMaxAngularVelocity = rb.maxAngularVelocity;
				CollisionDetectionMode origCdm = rb.collisionDetectionMode;
				float origMaxDepen = rb.maxDepenetrationVelocity;
				Collider origCollider = rb.GetComponent<Collider>();
				PhysicMaterial origMaterial = origCollider != null ? origCollider.sharedMaterial : null;
				bool materialWillChange = false;

				float newM = m;
				float newD = d;
				float newAD = ad;

				bool chestOrGlute = part == "Chest" || part == "Glute" || part == "Breast";
				if (chestOrGlute && !stabOff)
				{
					if (stabStrong)
					{
						newM = Clamp(m, lim.minMass, Mathf.Min(lim.maxMass, 1.5f));
						newD = Clamp(d, Mathf.Max(lim.minDrag, 0.4f), lim.maxDrag);
						newAD = Clamp(ad, Mathf.Max(lim.minAD, 0.08f), lim.maxAD);
					}
					else
					{
						newM = Clamp(m, lim.minMass, Mathf.Min(lim.maxMass, 2f));
						newD = Clamp(d, Mathf.Max(lim.minDrag, 0.2f), lim.maxDrag);
						newAD = Clamp(ad, Mathf.Max(lim.minAD, 0.05f), lim.maxAD);
					}
				}
				else
				{
					newM = Clamp(m, lim.minMass, lim.maxMass);
					newD = Clamp(d, lim.minDrag, lim.maxDrag);
					newAD = Clamp(ad, lim.minAD, lim.maxAD);
				}

				float regionIntensity = RegionIntensity(part);
				float blend = JiggleCurve(Mathf.Clamp01(regionIntensity));
				newM = m + (newM - m) * blend;
				newD = d + (newD - d) * blend;
				newAD = ad + (newAD - ad) * blend;

				if (part == "Genital")
				{
					string genMode = cfgGenMode.Value;
					float regionalMult = 1f;
					if (cfgGenRegional.Value)
					{
						if (rb.name.Contains("Gen1")) regionalMult = 0.4f;
						else if (rb.name.Contains("Gen2")) regionalMult = 0.6f;
						else if (rb.name.Contains("Gen3")) regionalMult = 0.7f;
						else if (rb.name.Contains("labia")) regionalMult = 0.4f;
					}
					if (genMode == "Standard")
					{
						newM *= 0.8f * regionalMult;
						newD *= 0.6f;
						newAD *= 0.6f;
					}
					else if (genMode == "Soft Tissue")
					{
						newM *= 0.5f * regionalMult;
						newD *= 0.3f;
						newAD *= 0.4f;
					}
					else if (genMode == "Ultra-Sensitive")
					{
						newM *= 0.3f * regionalMult;
						newD *= 0.2f;
						newAD *= 0.3f;
					}
					else if (genMode == "Penetration-Ready")
					{
						newM = Mathf.Max(0.12f, newM * (0.3f * regionalMult));
						newD = Mathf.Max(0.03f, newD * 0.2f);
						newAD = Mathf.Max(0.01f, newAD * 0.3f);
					}

					if (cfgGenMaterial.Value && origCollider != null)
					{
						float friction = Mathf.Max(0.15f, cfgGenFriction.Value);
						float bounce = Mathf.Clamp(cfgGenBounce.Value, 0f, 0.3f);
						if (genitalMaterial == null)
						{
							genitalMaterial = new PhysicMaterial("MicroPhysicsFixer_GenitalMaterial");
						}
						genitalMaterial.dynamicFriction = friction;
						genitalMaterial.staticFriction = friction * 1.2f;
						genitalMaterial.bounciness = bounce;
						genitalMaterial.frictionCombine = PhysicMaterialCombine.Average;
						genitalMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
						origCollider.material = genitalMaterial;
						materialWillChange = true;
					}

					rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
					rb.maxDepenetrationVelocity = 0.5f;

					if (newM < 0.15f) newM = 0.15f;
					if (newD < 0.10f) newD = 0.10f;
					if (newAD < 0.02f) newAD = 0.02f;

					if (!cfgSolver.Value)
					{
						rb.solverIterations = 10;
						rb.solverVelocityIterations = 6;
					}
				}

				if (regionIntensity > 1f)
				{
					float extraCurve = JiggleCurve(Mathf.Clamp01(regionIntensity - 1f));
					float targetD = Mathf.Max(lim.minDrag * 1.5f, newD * 0.85f);
					float targetAD = Mathf.Max(lim.minAD * 1.5f, newAD * 0.85f);
					float targetM = Mathf.Max(lim.minMass * 1.2f, newM * 0.95f);
					newD = Mathf.Lerp(newD, targetD, extraCurve * 0.5f);
					newAD = Mathf.Lerp(newAD, targetAD, extraCurve * 0.5f);
					newM = Mathf.Lerp(newM, targetM, extraCurve * 0.3f);
				}

				if (newM < 0.15f) newM = 0.15f;
				if (newD < 0.08f) newD = 0.08f;
				if (newAD < 0.015f) newAD = 0.015f;

				bool valuesChanged = !Mathf.Approximately(m, newM) || !Mathf.Approximately(d, newD) || !Mathf.Approximately(ad, newAD);
				if (!valuesChanged && !materialWillChange)
				{
					// still remember the body part (and undo anything the genital block touched)
					rb.solverIterations = origSolverIterations;
					rb.solverVelocityIterations = origSolverVelocityIterations;
					rb.collisionDetectionMode = origCdm;
					rb.maxDepenetrationVelocity = origMaxDepen;
					continue;
				}

				Backup b = new Backup();
				b.rb = rb;
				b.mass = m;
				b.drag = d;
				b.angularDrag = ad;
				b.solverIterations = origSolverIterations;
				b.solverVelocityIterations = origSolverVelocityIterations;
				b.interpolation = origInterpolation;
				b.maxAngularVelocity = origMaxAngularVelocity;
				b.collisionDetectionMode = origCdm;
				b.maxDepenetrationVelocity = origMaxDepen;
				b.collider = origCollider;
				b.originalMaterial = origMaterial;
				b.materialChanged = materialWillChange;
				b.applied = new Vector3(newM, newD, newAD);
				backups[rb] = b;

				rb.mass = newM;
				rb.drag = newD;
				rb.angularDrag = newAD;

				if (cfgSolver.Value)
				{
					float intensity = Clamp(cfgSolverIntensity.Value, 0.5f, 2f);
					rb.solverIterations = Mathf.RoundToInt(6 + 6 * intensity);
					rb.solverVelocityIterations = Mathf.RoundToInt(4 + 4 * intensity);
				}
				if (cfgInterp.Value)
				{
					rb.interpolation = RigidbodyInterpolation.Interpolate;
				}
				if (cfgMaxAng.Value)
				{
					rb.maxAngularVelocity = cfgMaxAngValue.Value;
				}
				if (cfgHighPrecision.Value && (part == "Chest" || part == "Head" || part == "Pelvis"))
				{
					rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				}
				changedCount++;
			}
			return changedCount;
		}

		private void Undo()
		{
			int restored = 0;
			foreach (Backup b in backups.Values)
			{
				if (b == null || b.rb == null)
				{
					continue;
				}
				b.rb.mass = b.mass;
				b.rb.drag = b.drag;
				b.rb.angularDrag = b.angularDrag;
				b.rb.solverIterations = b.solverIterations;
				b.rb.solverVelocityIterations = b.solverVelocityIterations;
				b.rb.interpolation = b.interpolation;
				b.rb.maxAngularVelocity = b.maxAngularVelocity;
				b.rb.collisionDetectionMode = b.collisionDetectionMode;
				b.rb.maxDepenetrationVelocity = b.maxDepenetrationVelocity;
				if (b.materialChanged && b.collider != null)
				{
					b.collider.sharedMaterial = b.originalMaterial;
				}
				restored++;
			}
			backups.Clear();
			applied.Clear();
			if (restored > 0)
			{
				Logger.LogInfo("Restored " + restored + " body parts to their original physics.");
			}
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
					Undo();
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
				// a new scene replaced the atoms; start over (old rigidbodies are already gone)
				backups.Clear();
				applied.Clear();
				firstSeen.Clear();
			}
			if (reapplyAt >= 0f && now >= reapplyAt)
			{
				reapplyAt = -1f;
				Undo();
				firstSeen.Clear();
			}
			if (now < nextScan)
			{
				return;
			}
			nextScan = now + 1f;
			List<Atom> atoms = sc.GetAtoms();
			Limits lim = ComputeLimits();
			for (int i = 0; i < atoms.Count; i++)
			{
				Atom a = atoms[i];
				if (a == null || a.type != "Person" || applied.Contains(a))
				{
					continue;
				}
				if (!cfgMales.Value && IsMale(a))
				{
					applied.Add(a);
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
				try
				{
					int n = FixPerson(a, lim);
					applied.Add(a);
					Logger.LogInfo("Fixed " + n + " body parts on " + a.uid + ".");
				}
				catch (Exception e)
				{
					applied.Add(a);
					Logger.LogWarning("Fixing " + a.uid + " failed: " + e.Message);
				}
			}
		}

		// ------------------------------------------------------------------ monitor

		private void FixedUpdate()
		{
			if (!cfgActive.Value || !cfgMonitor.Value || backups.Count == 0)
			{
				return;
			}
			float now = Time.unscaledTime;
			if (now < nextMonitor)
			{
				return;
			}
			nextMonitor = now + Mathf.Max(0.1f, cfgMonitorInterval.Value);
			string mode = cfgMonitorMode.Value;
			if (mode == "Observe Only")
			{
				return;
			}
			List<Rigidbody> dead = null;
			foreach (KeyValuePair<Rigidbody, Backup> kv in backups)
			{
				Rigidbody rb = kv.Key;
				if (rb == null)
				{
					if (dead == null)
					{
						dead = new List<Rigidbody>();
					}
					dead.Add(rb);
					continue;
				}
				Backup b = kv.Value;
				if (mode == "Lock Values")
				{
					if (Mathf.Abs(rb.mass - b.applied.x) > 0.001f || Mathf.Abs(rb.drag - b.applied.y) > 0.001f || Mathf.Abs(rb.angularDrag - b.applied.z) > 0.001f)
					{
						rb.mass = b.applied.x;
						rb.drag = b.applied.y;
						rb.angularDrag = b.applied.z;
					}
					continue;
				}
				// Safety Limits: only values that are actually unstable
				float cm = Clamp(rb.mass, AbsMinMass, AbsMaxMass);
				if (!Mathf.Approximately(cm, rb.mass)) rb.mass = cm;
				float cd = Clamp(rb.drag, AbsMinDrag, AbsMaxDrag);
				if (!Mathf.Approximately(cd, rb.drag)) rb.drag = cd;
				float ca = Clamp(rb.angularDrag, AbsMinAD, AbsMaxAD);
				if (!Mathf.Approximately(ca, rb.angularDrag)) rb.angularDrag = ca;
			}
			if (dead != null)
			{
				for (int i = 0; i < dead.Count; i++)
				{
					backups.Remove(dead[i]);
				}
			}
		}
	}
}
