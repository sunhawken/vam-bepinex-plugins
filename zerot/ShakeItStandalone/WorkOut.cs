using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// a float setting (JSONStorableFloat without VaM): limits clamp only when constrained
	public sealed class Num
	{
		public readonly string name;
		public float val, def, min, max;
		public bool constrained;
		public Action<float> changed;

		public Num(string name, float v, float min, float max, bool constrained)
		{
			this.name = name;
			val = def = v;
			this.min = min;
			this.max = max;
			this.constrained = constrained;
		}

		public void Set(float v)
		{
			if (constrained) v = Mathf.Clamp(v, min, max);
			if (v == val) return;
			val = v;
			if (changed != null) changed(v);
		}

		public void SetLimits(float lo, float hi)
		{
			min = lo;
			max = hi;
			if (constrained && (val < min || val > max)) Set(val);
		}

		public void Store(JSONClass jc) { jc[name] = J.F(val); }

		public void Load(JSONClass jc)
		{
			float f;
			if (J.TryFloat(jc, name, out f)) Set(f);
		}
	}

	// VaM's trigger system has no UI here: the actions are read from and written to JSON, nothing is ever drawn
	public sealed class NullTriggerHandler : TriggerHandler
	{
		public static readonly NullTriggerHandler Instance = new NullTriggerHandler();
		void TriggerHandler.RemoveTrigger(Trigger t) { }
		void TriggerHandler.DuplicateTrigger(Trigger t) { }
		RectTransform TriggerHandler.CreateTriggerActionsUI() { return null; }
		RectTransform TriggerHandler.CreateTriggerActionMiniUI() { return null; }
		RectTransform TriggerHandler.CreateTriggerActionDiscreteUI() { return null; }
		RectTransform TriggerHandler.CreateTriggerActionTransitionUI() { return null; }
		void TriggerHandler.RemoveTriggerActionUI(RectTransform rt) { }
	}

	public abstract class HTrigger : Trigger
	{
		public readonly string Name;
		public int Fired;           // how often the trigger was fired (diagnostics)

		protected HTrigger(string name)
		{
			Name = name;
			handler = NullTriggerHandler.Instance;
		}

		public void Restore(JSONClass jc, string subScenePrefix)
		{
			if (jc != null && jc.HasKey(Name))
			{
				JSONClass tc = jc[Name].AsObject;
				if (tc != null)
				{
					base.RestoreFromJSON(tc, subScenePrefix, false);
				}
			}
			else
			{
				base.RestoreFromJSON(new JSONClass());
			}
		}
	}

	public sealed class EventHTrigger : HTrigger
	{
		public EventHTrigger(string name) : base(name) { }

		public void Fire()
		{
			Fired++;
			active = true;
			active = false;
		}
	}

	public sealed class FloatHTrigger : HTrigger
	{
		public float LastValue = -1f;

		public FloatHTrigger(string name) : base(name) { }

		public void Fire(float v)
		{
			Fired++;
			LastValue = Mathf.Clamp01(v);
			_transitionInterpValue = LastValue;
			for (int i = 0; i < transitionActions.Count; ++i)
			{
				transitionActions[i].TriggerInterp(_transitionInterpValue, true);
			}
		}
	}

	// CustomFloatTrigger: watches one number and fires "exceeded" / "undershoot" events around a threshold and a remapped value
	public sealed class FloatWatch
	{
		public readonly string Name;
		public Func<float> Driver;
		public readonly Num inputFrom, inputTo, threshold;
		public bool absolute;
		public readonly EventHTrigger exceeded = new EventHTrigger("OnExceeded");
		public readonly EventHTrigger undershoot = new EventHTrigger("OnUndershoot");
		public readonly FloatHTrigger value = new FloatHTrigger("OnValueChanged");
		private float lastValue;
		public bool Broken;
		private readonly Action<string> log;

		public FloatWatch(string name, bool absoluteDefault, float thresholdDefault, float fromDefault, float toDefault, Action<string> log)
		{
			Name = name + "Trigger";
			absolute = absoluteDefault;
			inputFrom = new Num("Input From", fromDefault, 0f, 1f, false);
			inputTo = new Num("Input To", toDefault, 0f, 1f, false);
			threshold = new Num("Threshold", thresholdDefault, 0f, 1f, false);
			this.log = log;
		}

		public bool HasActions(HTrigger t)
		{
			try { return t.HasActions(); }
			catch { return false; }
		}

		public void Trigger(float v)
		{
			if (HasActions(value))
			{
				float delta = inputTo.val - inputFrom.val;
				if (delta > 0f)
				{
					value.Fire((v - inputFrom.val) / delta);
				}
			}
			else
			{
				// no actions to run: still note the crossing for the diagnostics
			}
			bool ex = HasActions(exceeded), un = HasActions(undershoot);
			if (ex || un)
			{
				if (lastValue < threshold.val)
				{
					if (v >= threshold.val) exceeded.Fire();
				}
				else if (v < threshold.val) undershoot.Fire();
				lastValue = v;
			}
		}

		public void Tick()
		{
			if (Broken || Driver == null) return;
			try
			{
				exceeded.Update();
				undershoot.Update();
				value.Update();
				float d = Driver();
				if (absolute) d = Mathf.Abs(d);
				Trigger(d);
			}
			catch (Exception e)
			{
				Broken = true;
				log("Trigger " + Name + " stopped: " + e.Message);
			}
		}

		public void Dispose()
		{
			try { exceeded.Remove(); undershoot.Remove(); value.Remove(); } catch { }
		}

		public JSONClass Store(string subScenePrefix)
		{
			JSONClass jc = new JSONClass();
			inputFrom.Store(jc);
			inputTo.Store(jc);
			threshold.Store(jc);
			try
			{
				jc[exceeded.Name] = exceeded.GetJSON(subScenePrefix);
				jc[undershoot.Name] = undershoot.GetJSON(subScenePrefix);
				jc[value.Name] = value.GetJSON(subScenePrefix);
			}
			catch (Exception e)
			{
				log("Trigger " + Name + " could not be stored: " + e.Message);
			}
			return jc;
		}

		public void Load(JSONClass jc, string subScenePrefix)
		{
			JSONClass tc = J.Obj(jc, Name);
			if (tc == null) return;
			inputFrom.Load(tc);
			inputTo.Load(tc);
			threshold.Load(tc);
			try
			{
				exceeded.Restore(tc, subScenePrefix);
				undershoot.Restore(tc, subScenePrefix);
				value.Restore(tc, subScenePrefix);
			}
			catch (Exception e)
			{
				log("Trigger " + Name + " could not be restored: " + e.Message);
			}
		}
	}

	// WorkOut: movement builds up "work", work builds up exhaustion, exhaustion and work make the skin glossy (sweat)
	public sealed class WorkOutModule
	{
		private readonly Ctx ctx;
		public readonly Flag adjustOffset = new Flag(true);
		public JSONStorable skin;
		public JSONStorableFloat gloss, glossTextureOffset;

		public readonly Num power = new Num("Current Power", 0f, 0f, 2f, false);
		public readonly Num workDone = new Num("Work Done Recently", 0f, 0f, 10000f, false);
		public readonly Num exhaustion = new Num("Exhaustion", 0f, 0f, 10f, true);
		public readonly Num powerToWork = new Num("Power To Work Factor", 1f, 0f, 10f, false);
		public readonly Num regenerationRate = new Num("Regeneration Rate", 2f, 0f, 10f, false);
		public readonly Num sweatBuildUp = new Num("Sweat Gain", 1f, 0f, 5f, false);
		public readonly Num sweatRegression = new Num("Sweat Regression", 1f, 0f, 2f, false);
		public readonly Num maxGloss = new Num("Max gloss", 8f, 5f, 8f, true);
		public readonly Num minGloss = new Num("Min gloss", 5f, 0f, 7.5f, true);
		public readonly Num maxGlossTextureOffset = new Num("Max Gloss Texture Offset", .6f, -1f, 1f, true);
		public readonly Num minGlossTextureOffset = new Num("Min Gloss Texture Offset", .45f, -1f, 1f, true);
		public readonly Num glossTextureOffsetThreshold = new Num("Gloss Texture Offset Threshold", 6.6f, 4.7f, 8f, true);
		public readonly Num exhaustionGain = new Num("Exhaustion Gain", .5f, 0f, 10f, false);
		public readonly Num maxExhaustion = new Num("Max Exhaustion", 10f, 0f, 20f, false);
		public readonly Num[] tweak;

		public FloatWatch currentPowerTrigger, workDoneTrigger, exhaustionTrigger, glossTrigger;
		public bool initialized;
		public bool Active;
		private float alpha2;
		private List<DAZMorph> flexMorphs = new List<DAZMorph>();
		private Func<List<DAZMorph>> flexSource;
		public string Info = "";
		public bool Missing;

		public WorkOutModule(Ctx ctx, Func<List<DAZMorph>> flexSource)
		{
			this.ctx = ctx;
			this.flexSource = flexSource;
			tweak = new Num[] { powerToWork, regenerationRate, sweatBuildUp, sweatRegression, maxGloss, minGloss, maxGlossTextureOffset, minGlossTextureOffset, exhaustionGain, maxExhaustion, glossTextureOffsetThreshold };
			currentPowerTrigger = new FloatWatch(power.name, false, 1f, 0f, 2f, ctx.Log);
			workDoneTrigger = new FloatWatch(workDone.name, false, 5000f, 0f, 10000f, ctx.Log);
			exhaustionTrigger = new FloatWatch(exhaustion.name, false, 2f, 0f, 10f, ctx.Log);
			glossTrigger = new FloatWatch("Gloss", false, 7f, 5f, 8f, ctx.Log);
			currentPowerTrigger.Driver = delegate { return power.val; };
			workDoneTrigger.Driver = delegate { return workDone.val; };
			exhaustionTrigger.Driver = delegate { return exhaustion.val; };
			glossTrigger.Driver = delegate { return gloss == null ? 0f : gloss.val; };
			maxGloss.changed = delegate { SetAlpha(); };
			minGloss.changed = delegate { SetAlpha(); };
			maxGlossTextureOffset.changed = delegate { SetAlpha(); };
			minGlossTextureOffset.changed = delegate { SetAlpha(); };
			glossTextureOffsetThreshold.changed = delegate { SetAlpha(); };
			maxExhaustion.changed = delegate (float v) { exhaustion.max = v; exhaustion.SetLimits(exhaustion.min, v); };
			SetAlpha();
		}

		private void SetAlpha()
		{
			glossTextureOffsetThreshold.SetLimits(minGloss.val, maxGloss.val);
			float diff = minGloss.val - glossTextureOffsetThreshold.val;
			alpha2 = (minGlossTextureOffset.val - maxGlossTextureOffset.val) / (diff * diff * diff);
		}

		public void ResolveMorphs()
		{
			flexMorphs = flexSource == null ? new List<DAZMorph>() : flexSource();
		}

		// the person's skin storable holds Gloss and Gloss Texture Offset; it is a different object after a skin change
		public bool FetchSkin()
		{
			JSONStorable s = ctx.Atom.GetStorableByID("skin");
			if (s == null)
			{
				initialized = false;
				return false;
			}
			if (s == skin && gloss != null && glossTextureOffset != null)
			{
				return true;
			}
			skin = s;
			gloss = s.GetFloatJSONParam("Gloss");
			glossTextureOffset = s.GetFloatJSONParam("Gloss Texture Offset");
			initialized = gloss != null && glossTextureOffset != null;
			return initialized;
		}

		public void Reset()
		{
			exhaustion.Set(0f);
			workDone.Set(0f);
			if (gloss != null) gloss.val = minGloss.val;
		}

		public float GetActivity(List<Dyn> dyns)
		{
			float act = 0f;
			for (int i = 0; i < dyns.Count; i++) act += dyns[i].activity;
			for (int i = 0; i < flexMorphs.Count; i++)
			{
				DAZMorph m = flexMorphs[i];
				if (m == null) continue;
				float mVal = m.morphValue;
				if (mVal > .05f && m.max != 0f) act += mVal * 2f / m.max;
			}
			return act;
		}

		public void Tick(List<Dyn> dyns)
		{
			if (!Active || !initialized || gloss == null || glossTextureOffset == null) return;
			if (SuperController.singleton.freezeAnimation) return;
			power.Set(GetActivity(dyns) * Time.fixedDeltaTime);
			if (power.val > .1f)
			{
				workDone.Set(workDone.val + power.val * powerToWork.val);
				if (exhaustionGain.val > 0f)
				{
					if (workDone.val > 1000f) exhaustion.Set(Mathf.Lerp(exhaustion.val, maxExhaustion.val, Time.fixedDeltaTime * exhaustionGain.val * workDone.val * 10e-7f));
				}
			}
			else if (exhaustion.val > 0f)
			{
				exhaustion.Set(Mathf.Lerp(exhaustion.val, 0f, .005f * Time.fixedDeltaTime));
			}
			if (workDone.val > 0f)
			{
				float diff = maxGloss.val - gloss.val;
				gloss.val += 10e-9f * sweatBuildUp.val * exhaustion.val * workDone.val * diff * diff;
				workDone.Set(Mathf.Lerp(workDone.val, 0f, .01f * regenerationRate.val * Time.fixedDeltaTime));
				gloss.val = Mathf.Lerp(gloss.val, minGloss.val, .01f * sweatRegression.val * Time.fixedDeltaTime);
			}
			if (adjustOffset.val)
			{
				if (gloss.val <= glossTextureOffsetThreshold.val)
				{
					float diff = minGloss.val - gloss.val;
					glossTextureOffset.val = maxGlossTextureOffset.val + alpha2 * diff * diff * diff;
				}
				else if (glossTextureOffset.val != minGlossTextureOffset.val)
				{
					glossTextureOffset.val = minGlossTextureOffset.val;
				}
			}
		}

		// built on demand (once a second from the housekeeping), not every physics step
		public string BuildInfo()
		{
			if (gloss == null || glossTextureOffset == null) return Info;
			Info = "Gloss: " + gloss.val.ToString("0.000") + "  Offset: " + glossTextureOffset.val.ToString("0.000") + "  Exh: " + exhaustion.val.ToString("0.00");
			return Info;
		}

		public void TickTriggers()
		{
			currentPowerTrigger.Tick();
			workDoneTrigger.Tick();
			exhaustionTrigger.Tick();
			glossTrigger.Tick();
		}

		public void Dispose()
		{
			currentPowerTrigger.Dispose();
			workDoneTrigger.Dispose();
			exhaustionTrigger.Dispose();
			glossTrigger.Dispose();
		}

		public void Store(JSONClass jc, string subScenePrefix)
		{
			for (int i = 0; i < tweak.Length; i++) tweak[i].Store(jc);
			jc[currentPowerTrigger.Name] = currentPowerTrigger.Store(subScenePrefix);
			jc[workDoneTrigger.Name] = workDoneTrigger.Store(subScenePrefix);
			jc[exhaustionTrigger.Name] = exhaustionTrigger.Store(subScenePrefix);
			jc[glossTrigger.Name] = glossTrigger.Store(subScenePrefix);
		}

		public void Load(JSONClass jc, string subScenePrefix, bool withTriggers)
		{
			for (int i = 0; i < tweak.Length; i++) tweak[i].Load(jc);
			if (withTriggers)
			{
				currentPowerTrigger.Load(jc, subScenePrefix);
				workDoneTrigger.Load(jc, subScenePrefix);
				exhaustionTrigger.Load(jc, subScenePrefix);
				glossTrigger.Load(jc, subScenePrefix);
			}
		}
	}
}
