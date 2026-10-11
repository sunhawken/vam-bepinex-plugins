using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// what every module of one person shares
	public sealed class Ctx
	{
		public Atom Atom;
		public MorphLib Morphs;
		public DynSet Dyns = new DynSet();
		public Timing Timing = new Timing();
		public Action<string> Log;
		public Action<IEnumerator> StartCo;
		public bool FlexOn = true;      // FlexIt enabled: its morphs also drive the tension of the jiggle
		public bool Monitor;            // "Show tension"
		public int SelectedRegion;
		public static readonly float[] Sin = MakeSin();

		private static float[] MakeSin()
		{
			float[] s = new float[361];
			const float twoPi = 2f * Mathf.PI;
			for (int i = 0; i < 360; i++)
			{
				s[i] = Mathf.Sin(i / 360f * twoPi);
			}
			s[360] = s[0];
			return s;
		}
	}

	// One jiggle handler (JiggleHandler): the four morph axes X Y Z R of a body part, driven by its local acceleration.
	public sealed class JHandler
	{
		public readonly string region;
		private readonly int regionId;
		private readonly int sideId;
		private readonly int subId;
		private readonly Ctx ctx;
		private readonly Dyn dyn;
		private readonly bool[] isJiggling = new bool[4];
		private readonly float[] amp = new float[4];
		private readonly float[] phase = new float[4];
		public DAZMorph[] morphs = new DAZMorph[4];
		private DAZMorph[] tensionMorphs;
		private Func<float> GetTension;

		private Vector4 strength, frequency, damping, minAccel, maxAccel, maxMorph, strengthTense, frequencyTense, dampingTense;
		public bool tensionEnabled = true;

		public readonly V4Param strengthJ = new V4Param("strength", new Vector4(3f, 2f, 3f, 2f), new Vector4(10f, 10f, 10f, 10f));
		public readonly V4Param strengthTenseJ = new V4Param("strTense", new Vector4(2f, 1f, 2f, 2f), new Vector4(10f, 10f, 10f, 10f));
		public readonly V4Param frequencyJ = new V4Param("frequency", new Vector4(16f, 16f, 16f, 16f), new Vector4(25f, 25f, 25f, 25f));
		public readonly V4Param frequencyTenseJ = new V4Param("freqTense", new Vector4(18f, 18f, 18f, 18f), new Vector4(25f, 25f, 25f, 25f));
		public readonly V4Param dampingJ = new V4Param("damping", new Vector4(5f, 5f, 5f, 5f), new Vector4(10f, 10f, 10f, 10f));
		public readonly V4Param dampingTenseJ = new V4Param("dampTense", new Vector4(7f, 7f, 7f, 7f), new Vector4(10f, 10f, 10f, 10f));
		public readonly V4Param minAccelJ = new V4Param("minAccel", new Vector4(1f, 1f, 1f, 1f), new Vector4(5f, 5f, 5f, 5f));
		public readonly V4Param maxAccelJ = new V4Param("maxAccel", new Vector4(3f, 3f, 3f, 2.2f), new Vector4(5f, 5f, 5f, 5f));
		public readonly V4Param maxMorphJ = new V4Param("maxMorph", new Vector4(1f, 1f, 1f, 1f), new Vector4(3f, 3f, 3f, 3f));

		// "Show tension": what the last step used, for the region being watched
		public Vector4 monStrength, monFrequency, monDamping;
		public float monTension;

		private const float TwoPi = 2f * Mathf.PI;
		private const float PhaseToId = 360f / (2f * Mathf.PI);

		public JHandler(Ctx ctx, int id, string region, Dyn dyn)
		{
			this.ctx = ctx;
			this.region = region;
			regionId = id;
			if (region.StartsWith("l")) sideId = -1;
			else if (region.StartsWith("r")) sideId = 1;
			if (region.Contains("Top")) subId = 0;
			else if (region.Contains("Mid")) subId = 1;
			else if (region.Contains("Low")) subId = 2;
			this.dyn = dyn;
			GetMorphs();
			ApplyDefaults();
			strengthJ.changed = delegate (Vector4 v) { strength = v * .01f; };
			strengthTenseJ.changed = delegate (Vector4 v) { strengthTense = v * .01f; };
			frequencyJ.changed = delegate (Vector4 v) { frequency = v; };
			frequencyTenseJ.changed = delegate (Vector4 v) { frequencyTense = v; };
			dampingJ.changed = delegate (Vector4 v) { damping = v; };
			dampingTenseJ.changed = delegate (Vector4 v) { dampingTense = v; };
			minAccelJ.changed = delegate (Vector4 v) { minAccel = v; };
			maxAccelJ.changed = delegate (Vector4 v) { maxAccel = v; };
			maxMorphJ.changed = delegate (Vector4 v) { maxMorph = v; };
		}

		public int Side { get { return sideId; } }

		public void GetMorphs()
		{
			MorphLib m = ctx.Morphs;
			if (region.Contains("UArm"))
			{
				morphs = new DAZMorph[] { m.Get("SI_" + region + "_Y", false), m.Get("SI_" + region + "_X", false), m.Get("SI_" + region + "_Z", false), m.Get("SI_" + region + "_R", false) };
			}
			else
			{
				morphs = new DAZMorph[] { m.Get("SI_" + region + "_X", false), m.Get("SI_" + region + "_Y", false), m.Get("SI_" + region + "_Z", false), m.Get("SI_" + region + "_R", false) };
			}
			string s = region.Substring(0, 1).ToUpper();
			switch (regionId)
			{
				case 0:
					tensionMorphs = new DAZMorph[] { m.Get("FI_Thigh1" + s, true) };
					break;
				case 1:
					tensionMorphs = new DAZMorph[] { m.Get("FI_Thigh0" + s, true), m.Get("FI_Thigh1" + s, true), m.Get("FI_Knee0" + s, true), m.Get("FI_Knee1" + s, true) };
					break;
				case 2:
					tensionMorphs = new DAZMorph[] { m.Get("FI_Ankle0" + s, true), m.Get("FI_Ankle1" + s, true) };
					break;
				case 3:
				case 4:
					tensionMorphs = new DAZMorph[] { m.Get("FI_Abdomen0", true), m.Get("FI_Abdomen2", true), m.Get("FI_Abdomen3", true) };
					break;
				case 5:
					tensionMorphs = new DAZMorph[] { m.Get("FI_Elbow0" + s, true), m.Get("FI_Elbow1" + s, true) };
					break;
			}
			SetupTension(ctx.FlexOn);
		}

		public int MorphCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < morphs.Length; i++)
				{
					if (morphs[i] != null)
					{
						n++;
					}
				}
				return n;
			}
		}

		// FlexIt's own morphs describe the pose best; without them (module off, morph missing) the body's bend angle does
		public void SetupTension(bool flexEnabled)
		{
			DAZMorph[] tm = tensionMorphs;
			bool any = false;
			for (int i = 0; tm != null && i < tm.Length; i++)
			{
				if (tm[i] != null)
				{
					any = true;
				}
			}
			if (flexEnabled && any)
			{
				GetTension = delegate
				{
					float mVal = 0f;
					int idx = -1;
					for (int i = 0; i < tm.Length; i++)
					{
						if (tm[i] == null)
						{
							continue;
						}
						if (idx < 0 || tm[i].morphValue > mVal)
						{
							mVal = tm[i].morphValue;
							idx = i;
						}
					}
					if (idx < 0 || tm[idx].max == 0f)
					{
						return dyn.tension;
					}
					return mVal / tm[idx].max;
				};
			}
			else
			{
				GetTension = delegate { return dyn.tension; };
			}
		}

		public void ApplyDefaults()
		{
			Vector4 strengthDefault = strengthJ.def;
			Vector4 strengthTenseDefault = strengthTenseJ.def;
			Vector4 frequencyDefault = frequencyJ.def;
			Vector4 frequencyTenseDefault = frequencyTenseJ.def;
			Vector4 dampingDefault = dampingJ.def;
			Vector4 dampingTenseDefault = dampingTenseJ.def;
			Vector4 minAccelDefault = minAccelJ.def;
			Vector4 maxAccelDefault = maxAccelJ.def;
			Vector4 maxMorphDefault = maxMorphJ.def;
			if (region.Contains("Calf"))
			{
				strengthDefault.z = 2f;
				strengthDefault.y = 1.5f;
				strengthTenseDefault.z = 1f;
				dampingDefault = new Vector4(4f, 4f, 3f, 4f);
				minAccelDefault.w = 2f;
				maxAccelDefault.w = 2.5f;
			}
			else if (region.Contains("Glutes"))
			{
				strengthDefault = new Vector4(2f, 4f, 2f, 0f);
				strengthTenseDefault = new Vector4(1f, 2f, 1f, 0f);
			}
			else if (region.Contains("Abs"))
			{
				strengthDefault = new Vector4(1f, 2f, 2f, 1f);
				frequencyDefault = new Vector4(18f, 18f, 18f, 18f);
			}
			else if (region.Contains("Thigh"))
			{
				Vector4 delta = new Vector4(.02f, .02f, .02f, .02f) * subId;
				strengthDefault = new Vector4(3f, 3f, 3f, 2f);
				frequencyDefault += delta;
				frequencyTenseDefault += delta;
			}
			// (the original tests "UArms" here, which no handler is called, so the upper arms keep the general defaults)
			strengthJ.def = strengthDefault; strengthJ.val = strengthDefault; strength = strengthDefault * .01f;
			strengthTenseJ.def = strengthTenseDefault; strengthTenseJ.val = strengthTenseDefault; strengthTense = strengthTenseDefault * .01f;
			frequencyJ.def = frequencyDefault; frequencyJ.val = frequencyDefault; frequency = frequencyDefault;
			frequencyTenseJ.def = frequencyTenseDefault; frequencyTenseJ.val = frequencyTenseDefault; frequencyTense = frequencyTenseDefault;
			dampingJ.def = dampingDefault; dampingJ.val = dampingDefault; damping = dampingDefault;
			dampingTenseJ.def = dampingTenseDefault; dampingTenseJ.val = dampingTenseDefault; dampingTense = dampingTenseDefault;
			minAccelJ.def = minAccelDefault; minAccelJ.val = minAccelDefault; minAccel = minAccelDefault;
			maxAccelJ.def = maxAccelDefault; maxAccelJ.val = maxAccelDefault; maxAccel = maxAccelDefault;
			maxMorphJ.def = maxMorphDefault; maxMorphJ.val = maxMorphDefault; maxMorph = maxMorphDefault;
		}

		public V4Param[] All
		{
			get { return new V4Param[] { strengthJ, strengthTenseJ, frequencyJ, frequencyTenseJ, dampingJ, dampingTenseJ, minAccelJ, maxAccelJ, maxMorphJ }; }
		}

		public void Reset()
		{
			for (int i = 0; i < morphs.Length; i++)
			{
				if (morphs[i] != null)
				{
					morphs[i].morphValue = 0f;
				}
				amp[i] = 0f;
				phase[i] = 0f;
				isJiggling[i] = false;
			}
		}

		public void Jiggle()
		{
			Vector3 acc = dyn.localAcceleration;
			float tension = tensionEnabled ? GetTension() : 0f;
			Timing t = ctx.Timing;
			for (int i = 0; i < 4; i++)
			{
				if (morphs[i] == null)
				{
					continue;
				}
				float mag;
				if (i == 3)
				{
					mag = Mathf.Sqrt(acc.x * acc.x + acc.z * acc.z);
				}
				else
				{
					mag = acc[i];
				}
				if (Mathf.Abs(mag) > maxAccel[i])
				{
					mag = maxAccel[i] * Mathf.Sign(mag);
				}
				if (!t.isRealtime)
				{
					mag *= t.realFrametime / t.targetFrametime;
				}
				else
				{
					mag *= t.physicsRateFactor;
				}
				JiggleDir(i, mag, tension);
			}
		}

		private void JiggleDir(int dir, float mag, float tension)
		{
			float str = strength[dir];
			float freq = frequency[dir];
			float damp = damping[dir];
			if (tensionEnabled)
			{
				str = Mathf.Lerp(str, strengthTense[dir], tension);
				freq = Mathf.Lerp(freq, frequencyTense[dir], tension);
				damp = Mathf.Lerp(damp, dampingTense[dir], tension);
				if (ctx.Monitor && regionId == ctx.SelectedRegion && sideId <= 0)
				{
					monStrength[dir] = str * 100f;
					monFrequency[dir] = freq;
					monDamping[dir] = damp;
					monTension = tension;
				}
			}
			float a = amp[dir];
			if (Math.Abs(mag) > minAccel[dir])
			{
				mag *= str;
				if (!isJiggling[dir])
				{
					if (dir == 1 || dir == 2)
					{
						mag *= -1f;
					}
					a = Clamp(mag, dir);
					isJiggling[dir] = true;
				}
				else
				{
					a = Clamp(a + Mathf.Abs(mag) * Mathf.Sign(a), dir);
				}
			}
			if (Mathf.Abs(a) > .005f)
			{
				a = Mathf.Lerp(a, 0f, damp * ctx.Timing.targetFrametime);
				float p = phase[dir];
				p += freq * ctx.Timing.targetFrametime;
				if (p > TwoPi)
				{
					p = 0f;
				}
				int idx = (int)(p * PhaseToId);
				if (idx < 0) idx = 0;
				if (idx > 360) idx = 360;
				morphs[dir].morphValue = a * Ctx.Sin[idx];
				amp[dir] = a;
				phase[dir] = p;
			}
			else if (isJiggling[dir])
			{
				phase[dir] = 0f;
				isJiggling[dir] = false;
			}
		}

		private float Clamp(float val, int dir)
		{
			float mVal = maxMorph[dir];
			if (val > mVal) return mVal;
			if (val < -mVal) return -mVal;
			return val;
		}

		// the stored name: left handlers are stored under the name without the side letter, right handlers are not stored
		public bool Store(JSONClass jc)
		{
			if (sideId == 1)
			{
				return false;
			}
			string storeName = sideId == -1 ? region.Substring(1) : region;
			JSONClass t = new JSONClass();
			V4Param[] all = All;
			for (int i = 0; i < all.Length; i++)
			{
				all[i].Store(t);
			}
			jc[storeName] = t;
			return true;
		}

		public void Load(JSONClass jc)
		{
			string loadName = sideId != 0 ? region.Substring(1) : region;
			JSONClass t = J.Obj(jc, loadName);
			if (t == null)
			{
				return;
			}
			V4Param[] all = All;
			for (int i = 0; i < all.Length; i++)
			{
				all[i].Load(t);
			}
		}
	}

	// A body part with its handlers (JiggleRegion): one handler for the whole part ("combined") or Upper / Mid / Lower.
	public sealed class JRegion
	{
		public readonly string Name;
		public readonly int Id;
		private readonly Ctx ctx;
		private readonly List<Dyn> dyns = new List<Dyn>();
		private readonly List<JHandler> subHandlers = new List<JHandler>();
		private readonly List<JHandler> combinedHandlers = new List<JHandler>();
		public List<JHandler> handlers = new List<JHandler>();
		public int selectedSubregion;
		public readonly Flag enabledJ = new Flag(true);
		public readonly Flag isCombined = new Flag(true);
		public readonly Flag tensionEnabled = new Flag(true);
		public bool Active = true;
		public bool Missing;

		public JRegion(Ctx ctx, int id, string name, string[] rbNames)
		{
			this.ctx = ctx;
			Id = id;
			Name = name;
			enabledJ.changed = delegate (bool val)
			{
				Active = val;
				if (!val)
				{
					Reset();
				}
			};
			isCombined.changed = delegate (bool val)
			{
				for (int i = 0; i < handlers.Count; i++)
				{
					handlers[i].Reset();
				}
				handlers = val ? combinedHandlers : subHandlers;
			};
			tensionEnabled.changed = ToggleTension;
			string[] sides = { "l", "r" };
			for (int i = 0; i < rbNames.Length; i++)
			{
				string regionName = name;
				if (rbNames.Length == 2)
				{
					regionName = sides[i] + regionName;
				}
				Rigidbody rb = Bodies.Force(ctx.Atom, rbNames[i]);
				if (rb == null)
				{
					Missing = true;
					ctx.Log("Body " + rbNames[i] + " was not found on " + ctx.Atom.uid + ": " + name + " is skipped.");
					continue;
				}
				Dyn d = ctx.Dyns.Get(rb);
				d.SetTensionFunction();
				dyns.Add(d);
				subHandlers.Add(new JHandler(ctx, id, regionName + "Top", d));
				subHandlers.Add(new JHandler(ctx, id, regionName + "Mid", d));
				subHandlers.Add(new JHandler(ctx, id, regionName + "Low", d));
				combinedHandlers.Add(new JHandler(ctx, id, regionName, d));
			}
			handlers = combinedHandlers;
			if (id > 5) tensionEnabled.Set(false);
			if (id > 5) enabledJ.Set(false);
			if (id == 1) isCombined.Set(false);
		}

		public void GetMorphs()
		{
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].GetMorphs();
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].GetMorphs();
		}

		public void SetupTension(bool val)
		{
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].SetupTension(val);
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].SetupTension(val);
		}

		private void ToggleTension(bool val)
		{
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].tensionEnabled = val;
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].tensionEnabled = val;
		}

		public void Tick()
		{
			if (!Active)
			{
				return;
			}
			for (int i = 0; i < handlers.Count; i++)
			{
				handlers[i].Jiggle();
			}
		}

		public void Reset()
		{
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].Reset();
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].Reset();
		}

		public int MorphCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < combinedHandlers.Count; i++) n += combinedHandlers[i].MorphCount;
				for (int i = 0; i < subHandlers.Count; i++) n += subHandlers[i].MorphCount;
				return n;
			}
		}

		// the handlers the editor shows: all of them when combined, else the chosen part (left and right)
		public List<JHandler> Selected(int sub)
		{
			if (isCombined.val)
			{
				return handlers;
			}
			List<JHandler> l = new List<JHandler>();
			if (subHandlers.Count == 3)
			{
				l.Add(subHandlers[sub]);
			}
			else if (subHandlers.Count >= 6)
			{
				l.Add(subHandlers[sub]);
				l.Add(subHandlers[sub + 3]);
			}
			else if (subHandlers.Count > sub)
			{
				l.Add(subHandlers[sub]);
			}
			return l;
		}

		public List<JHandler> Combined { get { return combinedHandlers; } }
		public List<JHandler> Sub { get { return subHandlers; } }

		public void Dispose()
		{
			Reset();
			for (int i = 0; i < dyns.Count; i++)
			{
				ctx.Dyns.Release(dyns[i]);
			}
			dyns.Clear();
		}

		public bool Store(JSONClass jc)
		{
			JSONClass t = new JSONClass();
			t["Enabled"] = J.Bool(enabledJ.val);
			t["isCombined"] = J.Bool(isCombined.val);
			t["Tension enabled"] = J.Bool(tensionEnabled.val);
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].Store(t);
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].Store(t);
			jc[Name] = t;
			return true;
		}

		public void Load(JSONClass jc)
		{
			JSONClass t = J.Obj(jc, Name);
			if (t == null)
			{
				return;
			}
			bool b;
			if (J.TryBool(t, "Enabled", out b)) enabledJ.Set(b);
			if (J.TryBool(t, "isCombined", out b)) isCombined.Set(b);
			if (J.TryBool(t, "Tension enabled", out b)) tensionEnabled.Set(b);
			for (int i = 0; i < combinedHandlers.Count; i++) combinedHandlers[i].Load(t);
			for (int i = 0; i < subHandlers.Count; i++) subHandlers[i].Load(t);
		}
	}
}
