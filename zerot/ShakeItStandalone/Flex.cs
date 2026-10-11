using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// -------------------------------------------------------------------- support detection (FlexRegionSupportTracker)

	public class FlexCollisionTrigger : MonoBehaviour
	{
		public SupportTracker tracker;

		private static Vector3 Point(Collision c)
		{
			ContactPoint[] cp = c.contacts;
			return cp != null && cp.Length > 0 ? cp[0].point : c.transform.position;
		}

		public void OnCollisionEnter(Collision collision)
		{
			if (tracker != null) tracker.RegisterCollision(collision.rigidbody, Point(collision));
		}

		public void OnCollisionExit(Collision collision)
		{
			if (tracker != null) tracker.DeregisterCollision(collision.rigidbody);
		}

		public void OnCollisionStay(Collision collision)
		{
			if (tracker != null && !tracker.colliding) tracker.RegisterCollision(collision.rigidbody, Point(collision));
		}
	}

	public class HeelTrigger : MonoBehaviour
	{
		public bool isColliding;

		public void OnTriggerEnter(Collider other)
		{
			if (other.isTrigger) return;
			isColliding = true;
		}

		public void OnTriggerExit(Collider other)
		{
			if (other.isTrigger) return;
			isColliding = false;
		}
	}

	public sealed class SupportTracker
	{
		private readonly FRegion flexRegion;
		private readonly List<FlexCollisionTrigger> triggers;
		private readonly Action<IEnumerator> startCo;
		public HashSet<Rigidbody> stuffSupporting = new HashSet<Rigidbody>();
		private bool _supported;
		public bool colliding;
		public Rigidbody exclude;
		public Func<bool> SetReference;
		private Vector3 origin;
		private float down;
		private float onStayTimeout;
		private bool dead;

		public bool supported
		{
			get { return _supported; }
			set
			{
				if (_supported == value) return;
				_supported = flexRegion.supported = value;
			}
		}

		public SupportTracker(FRegion flexRegion, Rigidbody rigidbody, List<FlexCollisionTrigger> triggers, Action<IEnumerator> startCo, Rigidbody exclude = null)
		{
			this.flexRegion = flexRegion;
			this.triggers = triggers;
			this.startCo = startCo;
			if (exclude != null) this.exclude = exclude;
			for (int i = 0; i < triggers.Count; i++) triggers[i].tracker = this;
			Transform tr = rigidbody.transform;
			bool left = rigidbody.name.StartsWith("l");
			if (flexRegion.id == 0)
			{
				HeelTrigger heel = left ? flexRegion.lHeelTrigger : flexRegion.rHeelTrigger;
				HeelTrigger toe = left ? flexRegion.lToeTrigger : flexRegion.rToeTrigger;
				if (heel != null)
				{
					SetReference = delegate
					{
						if (heel.isColliding)
						{
							Transform c = heel.transform;
							Vector3 up = c.up;
							origin = c.position + .5f * c.localScale.y * up;
							down = -up.y;
						}
						else if (toe != null && toe.isColliding)
						{
							Transform c = toe.transform;
							Vector3 up = c.up;
							origin = c.position + .5f * c.localScale.y * up;
							down = -up.y;
						}
						else
						{
							origin = tr.position;
							down = tr.forward.y;
						}
						return true;
					};
				}
				else
				{
					SetReference = delegate { origin = tr.position; down = -tr.up.y; return true; };
				}
			}
			else
			{
				SetReference = delegate { origin = tr.position; down = -tr.up.y; return true; };
			}
		}

		public void RegisterCollision(Rigidbody other, Vector3 point)
		{
			if (dead || other == exclude) return;   // as the original: with no excluded body, contacts with bodiless colliders are ignored too
			if (!colliding) colliding = true;
			SetReference();
			if ((point - origin).normalized.y < down)
			{
				stuffSupporting.Add(other);
				if (!supported)
				{
					supported = true;
					if (startCo != null) startCo(OnStayRoutine());
				}
			}
		}

		public void DeregisterCollision(Rigidbody other)
		{
			stuffSupporting.Remove(other);
			if (stuffSupporting.Count == 0)
			{
				colliding = false;
				onStayTimeout = .05f;
			}
		}

		public IEnumerator OnStayRoutine()
		{
			while (!dead && (stuffSupporting.Count > 0 || onStayTimeout > 0f))
			{
				if (stuffSupporting.Count == 0) onStayTimeout -= Time.deltaTime;
				yield return new WaitForSeconds(Time.fixedDeltaTime);
			}
			supported = false;
		}

		public void Destroy()
		{
			dead = true;
			for (int i = 0; i < triggers.Count; i++)
			{
				if (triggers[i] != null) UnityEngine.Object.Destroy(triggers[i]);
			}
			supported = false;
		}

		public void SetActive(bool val)
		{
			for (int i = 0; i < triggers.Count; i++)
			{
				if (triggers[i] != null) triggers[i].enabled = val;
			}
		}
	}

	public sealed class MorphBuffer
	{
		public DAZMorph morph;
		private float buffer;
		public float max;
		private const float increment = .25f;

		public MorphBuffer(DAZMorph morph) { this.morph = morph; }
		public void Fill(float val) { buffer += val; }

		public void Pop()
		{
			if (buffer < .001f) return;
			float val = buffer * increment;
			buffer -= val;
			float newVal = morph.morphValue + val;
			morph.morphValue = newVal > max ? max : newVal;
		}
	}

	// -------------------------------------------------------------------- the flex regions (FlexRegion and its subclasses)

	public class FRegion
	{
		public Ctx ctx;
		public int id;
		public string name;
		public Dyn[] rbDynamics = new Dyn[0];
		public List<SupportTracker> supportTrackers = new List<SupportTracker>();
		public List<List<DAZMorph>> morphs = new List<List<DAZMorph>>();
		protected List<List<MorphBuffer>> morphBuffers = new List<List<MorphBuffer>>();
		protected bool[] doDyn = { true, true, true, true };
		protected bool[] doStatic = { true, true, true, true };
		public bool supported;
		public FRegion[] children;
		public HeelTrigger lHeelTrigger, rHeelTrigger, lToeTrigger, rToeTrigger;
		public bool Active = true;
		public bool Missing;
		protected Action<IEnumerator> startCo { get { return ctx.StartCo; } }
		protected Atom atom { get { return ctx.Atom; } }

		public int morphsCount { get { return morphs.Count; } }

		public readonly Flag enabledJ = new Flag(true);
		public readonly V4Param dynamicStrengthJ = new V4Param("Dynamic", new Vector4(1f, 1f, 1f, 1f), new Vector4(10f, 10f, 10f, 10f));
		public readonly V4Param amountJ = new V4Param("Amount", new Vector4(2f, 2f, 2f, 2f), new Vector4(5f, 5f, 5f, 5f));
		public readonly V4Param minAccelJ = new V4Param("minAcc", new Vector4(0f, 0f, 0f, 0f), new Vector4(20f, 20f, 20f, 20f));
		public readonly V4Param sRatioJ = new V4Param("S-Ratio", new Vector4(.75f, .75f, .75f, .75f), new Vector4(1f, 1f, 1f, 1f));

		public Vector4 sRatio, dynamicStrength, staticStrength, minAccel, amount;

		public FRegion(Ctx ctx, int id, string name)
		{
			this.ctx = ctx;
			this.id = id;
			this.name = name;
		}

		// FlexRegion.Init: morphs, callbacks, defaults (the subclass then finds its bodies)
		public virtual void Init(string[] rbNames)
		{
			GetMorphs();
			enabledJ.changed = OnEnabled;
			dynamicStrengthJ.changed = delegate (Vector4 v) { OnDynamicStrengthChanged(v); };
			minAccelJ.changed = delegate (Vector4 v) { minAccel = v; };
			sRatioJ.changed = OnSRatioChanged;
			amountJ.changed = OnAmountChanged;
			SetupDefaults();
		}

		protected void OnEnabled(bool val)
		{
			Active = val;
			if (val) OnEnable(); else OnDisable();
		}

		public virtual void OnEnable()
		{
			for (int i = 0; i < supportTrackers.Count; i++) supportTrackers[i].SetActive(true);
		}

		public virtual void OnDisable()
		{
			for (int i = 0; i < supportTrackers.Count; i++) supportTrackers[i].SetActive(false);
			Reset();
		}

		public virtual void GetMorphs()
		{
			morphs.Clear();
			morphBuffers.Clear();
			for (int i = 0; i < 4; i++)
			{
				bool morphsFound = false;
				List<DAZMorph> sideMorphs = new List<DAZMorph>();
				for (int j = 0; j < 2; j++)
				{
					string side = j == 0 ? "L" : "R";
					DAZMorph morph = ctx.Morphs.Get("FI_" + name + i + side, true);
					if (morph == null) continue;
					morph.jsonFloat.constrained = false;
					sideMorphs.Add(morph);
					morphsFound = true;
				}
				if (!morphsFound) continue;
				morphs.Add(sideMorphs);
				List<MorphBuffer> b = new List<MorphBuffer>();
				for (int k = 0; k < sideMorphs.Count; k++) b.Add(new MorphBuffer(sideMorphs[k]));
				morphBuffers.Add(b);
			}
			OnAmountChanged(amountJ.val);
			OnSRatioChanged(sRatioJ.val);
			OnDynamicStrengthChanged(dynamicStrengthJ.val);
		}

		// single-sided morphs: FI_<name><n> (abdomen, neck)
		protected void GetSingleMorphs()
		{
			morphs.Clear();
			morphBuffers.Clear();
			for (int i = 0; i < 4; i++)
			{
				List<DAZMorph> sideMorphs = new List<DAZMorph>();
				DAZMorph morph = ctx.Morphs.Get("FI_" + name + i, true);
				if (morph == null) continue;
				morph.jsonFloat.constrained = false;
				sideMorphs.Add(morph);
				morphs.Add(sideMorphs);
				List<MorphBuffer> b = new List<MorphBuffer>();
				b.Add(new MorphBuffer(morph));
				morphBuffers.Add(b);
			}
			OnAmountChanged(amountJ.val);
			OnSRatioChanged(sRatioJ.val);
			OnDynamicStrengthChanged(dynamicStrengthJ.val);
		}

		protected virtual void SetupSupportTrackers(Rigidbody rb) { }

		public virtual void UpdateSupportTrackers()
		{
			lToeTrigger = lHeelTrigger = rToeTrigger = rHeelTrigger = null;
			for (int i = 0; i < supportTrackers.Count; i++) supportTrackers[i].Destroy();
			supportTrackers.Clear();
			for (int i = 0; i < rbDynamics.Length; i++)
			{
				if (rbDynamics[i] != null) SetupSupportTrackers(rbDynamics[i].rigidbody);
			}
		}

		public void OnAmountChanged(Vector4 val)
		{
			amount = val;
			OnDynamicStrengthChanged(dynamicStrengthJ.val);
			staticStrength = Mul(sRatio, amount);
			for (int i = 0; i < morphBuffers.Count; i++)
			{
				for (int j = 0; j < morphBuffers[i].Count; j++) morphBuffers[i][j].max = amount[i];
			}
		}

		public void OnSRatioChanged(Vector4 val)
		{
			sRatio = val;
			doDyn = new[] { val[0] < 1f, val[1] < 1f, val[2] < 1f, val[3] < 1f };
			doStatic = new[] { val[0] > 0f, val[1] > 0f, val[2] > 0f, val[3] > 0f };
			staticStrength = Mul(sRatio, amount);
		}

		public virtual void OnDynamicStrengthChanged(Vector4 val)
		{
			dynamicStrength = Mul(val, amountJ.val);
		}

		protected static void SetD(V4Param p, Vector4 v)
		{
			p.def = v;
			p.Set(v);
		}

		protected virtual void SetupDefaults()
		{
			OnAmountChanged(amountJ.val);
			OnSRatioChanged(sRatioJ.val);
		}

		// the pattern of every region's defaults: assign default + value of the four vectors, then refresh what depends on them
		protected void ApplyDefaultSet(Vector4 amountD, Vector4 sRatioD, Vector4 dynamicD, Vector4 minAccelD)
		{
			SetD(amountJ, amountD);
			SetD(sRatioJ, sRatioD);
			SetD(dynamicStrengthJ, dynamicD);
			minAccel = minAccelD;
			SetD(minAccelJ, minAccelD);
			OnAmountChanged(amountJ.val);
			OnSRatioChanged(sRatioJ.val);
		}

		protected virtual float GetFlex(int direction, int side)
		{
			return rbDynamics[side].rigidbody.transform.forward.y;
		}

		protected virtual bool Flexed(float flex, int direction, int side)
		{
			return direction == 0 ? flex > 0f : flex < 0f;
		}

		protected virtual float GetAcceleration(int direction, int side)
		{
			float val = rbDynamics[side].angularAcceleration.x;
			val *= amount[direction];
			return val;
		}

		private bool SupportedAt(FRegion r, int side)
		{
			return side < r.supportTrackers.Count && r.supportTrackers[side].supported;
		}

		private bool AnySupported(int side)
		{
			if (SupportedAt(this, side)) return true;
			if (children != null)
			{
				for (int i = 0; i < children.Length; i++) if (SupportedAt(children[i], side)) return true;
			}
			return false;
		}

		public virtual void Tick()
		{
			if (!Active || Missing) return;
			float speed = ctx.Timing.flexRegressionSpeed;
			for (int direction = 0; direction < morphs.Count; direction++)
			{
				if (amount[direction] == 0f) continue;
				bool dyn = doDyn[direction];
				bool stat = doStatic[direction];
				for (int side = 0; side < morphs[direction].Count; side++)
				{
					if (side >= rbDynamics.Length || rbDynamics[side] == null) continue;
					morphs[direction][side].max = amount[direction];
					float mVal = morphs[direction][side].morphValue;
					float mTarget = 0f;
					float flex = 0f;
					if (stat)
					{
						if (!AnySupported(side))
						{
							flex = GetFlex(direction, side);
						}
						if (Flexed(flex, direction, side))
						{
							flex = Mathf.Abs(flex);
							int si = (int)(flex * 90);
							if (si > 360) si = 360;
							flex = Ctx.Sin[si];
							mTarget = flex * staticStrength[direction];
						}
					}
					if (Mathf.Abs(mVal - mTarget) > .02f)
					{
						morphs[direction][side].morphValue = Mathf.Lerp(mVal, mTarget, speed);
					}
					if (dyn)
					{
						float f = GetAcceleration(direction, side);
						bool inRange = (direction == 1 || direction == 3) ? f > minAccel[direction] : f < -minAccel[direction];
						if (inRange)
						{
							morphBuffers[direction][side].Fill(Mathf.Abs(f) * dynamicStrength[direction] * ctx.Timing.realFrametime);
						}
						morphBuffers[direction][side].Pop();
					}
				}
			}
		}

		public virtual void Reset()
		{
			for (int i = 0; i < morphs.Count; i++)
			{
				for (int j = 0; j < morphs[i].Count; j++) morphs[i][j].morphValue = 0f;
			}
		}

		public virtual void Dispose()
		{
			for (int i = 0; i < supportTrackers.Count; i++) supportTrackers[i].Destroy();
			supportTrackers.Clear();
			Reset();
			for (int i = 0; i < rbDynamics.Length; i++) ctx.Dyns.Release(rbDynamics[i]);
			rbDynamics = new Dyn[0];
		}

		protected static Vector4 Mul(Vector4 a, Vector4 b)
		{
			for (int i = 0; i < 4; i++) a[i] *= b[i];
			return a;
		}

		public virtual int MorphCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < morphs.Count; i++) n += morphs[i].Count;
				return n;
			}
		}

		public virtual bool Stale()
		{
			return false;
		}

		public virtual void Store(JSONClass jc)
		{
			JSONClass t = new JSONClass();
			t["Enabled"] = J.Bool(enabledJ.val);
			amountJ.Store(t);
			sRatioJ.Store(t);
			dynamicStrengthJ.Store(t);
			jc[name] = t;
		}

		public virtual void Load(JSONClass jc)
		{
			JSONClass t = J.Obj(jc, name);
			if (t == null) return;
			bool b;
			if (J.TryBool(t, "Enabled", out b)) enabledJ.Set(b);
			amountJ.Load(t);
			sRatioJ.Load(t);
			dynamicStrengthJ.Load(t);
		}

		public static float MapRange(float val, float zero, float fromMin, float fromMax, float toMin, float toMax)
		{
			float v;
			if (val >= zero) v = val < fromMax ? toMax * (val - zero) / (fromMax - zero) : toMax;
			else v = val > fromMin ? toMin * (val - zero) / (fromMin - zero) : toMin;
			return v;
		}

		// ---- shared helpers for the regions with a left and a right body
		protected void SidedBodies(string[] rbNames, bool useForce)
		{
			rbDynamics = new Dyn[2];
			for (int i = 0; i < 2; i++)
			{
				string side = i == 0 ? "l" : "r";
				Rigidbody rb = useForce ? Bodies.Force(atom, side + rbNames[0]) : Bodies.Rb(atom, side + rbNames[0]);
				if (rb == null)
				{
					Missing = true;
					ctx.Log("Body " + side + rbNames[0] + " was not found on " + atom.uid + ": " + name + " is skipped.");
					return;
				}
				rbDynamics[i] = ctx.Dyns.Get(rb);
			}
			for (int i = 0; i < 2; i++) SetupSupportTrackers(rbDynamics[i].rigidbody);
		}

		protected static List<Collider> LegColliders(Rigidbody rb)
		{
			List<Collider> cols = new List<Collider>();
			Transform t = rb.transform;
			if (t.childCount > 3)
			{
				Transform holder = t.GetChild(3);
				for (int i = 0; i < holder.childCount; i++)
				{
					Transform c = holder.GetChild(i);
					if (c.childCount > 0)
					{
						Collider col = c.GetChild(0).GetComponent<Collider>();
						if (col != null) cols.Add(col);
					}
				}
			}
			Collider own = rb.GetComponentInChildren<Collider>();
			if (own != null) cols.Add(own);
			return cols;
		}

		protected void LegTrackers(Rigidbody rb)
		{
			List<FlexCollisionTrigger> triggers = new List<FlexCollisionTrigger>();
			List<Collider> colliders = LegColliders(rb);
			for (int i = 0; i < colliders.Count; i++) triggers.Add(colliders[i].gameObject.AddComponent<FlexCollisionTrigger>());
			if (triggers.Count > 0) supportTrackers.Add(new SupportTracker(this, rb, triggers, startCo));
		}
	}

	public sealed class AnkleRegion : FRegion
	{
		public AnkleRegion(Ctx c, int id) : base(c, id, "Ankle") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			SidedBodies(rbNames, true);
		}

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			a.x = 1.5f; a.y = 2f; d.x = .25f; d.y = .2f;
			ApplyDefaultSet(a, s, d, m);
		}

		private static HeelTrigger Add(Transform root, string path)
		{
			Transform t = root.Find(path);
			return t == null ? null : t.gameObject.AddComponent<HeelTrigger>();
		}

		protected override void SetupSupportTrackers(Rigidbody rb)
		{
			List<FlexCollisionTrigger> triggers = new List<FlexCollisionTrigger>();
			string side = rb.name.Substring(0, 1);
			Transform t = rb.transform;
			if (side == "l")
			{
				lHeelTrigger = Add(t, "lHeelColliderBase/lHeelColTOffset/lHeelColROffset/lHeelCollider/lHeelColliderTrigger");
				lToeTrigger = Add(t, "lToe/lToeColliderBase/lToeColTOffset/lToeColROffset/lToeCollider/lToeColliderTrigger");
				if (lToeTrigger == null) lToeTrigger = Add(t, "lToeCollider/lToeColliderTrigger");
			}
			else
			{
				rHeelTrigger = Add(t, "rHeelColliderBase/rHeelColTOffset/rHeelColROffset/rHeelCollider/rHeelColliderTrigger");
				rToeTrigger = Add(t, "rToe/rToeColliderBase/rToeColTOffset/rToeColROffset/rToeCollider/rToeColliderTrigger");
				if (rToeTrigger == null) rToeTrigger = Add(t, "rToeCollider/rToeColliderTrigger");
			}
			triggers.Add(rb.gameObject.AddComponent<FlexCollisionTrigger>());
			Rigidbody toe = Bodies.Rb(atom, side + "Toe");
			Rigidbody bigToe = Bodies.Rb(atom, side + "BigToe");
			Rigidbody smallToe1 = Bodies.Rb(atom, side + "SmallToe1");
			if (bigToe != null) triggers.Add(bigToe.gameObject.AddComponent<FlexCollisionTrigger>());
			if (toe != null) triggers.Add(toe.gameObject.AddComponent<FlexCollisionTrigger>());
			supportTrackers.Add(new SupportTracker(this, rb, triggers, startCo, smallToe1));
		}

		protected override float GetFlex(int direction, int side)
		{
			return MapRange(rbDynamics[side].transform.localRotation.x, .25f, -.15f, .7f, 1f, -1f);
		}
	}

	public sealed class KneeRegion : FRegion
	{
		public KneeRegion(Ctx c, int id) : base(c, id, "Knee") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			SidedBodies(rbNames, true);
			SetupDefaults();
		}

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			s.x = s.y = .75f; d.y = .75f;
			ApplyDefaultSet(a, s, d, m);
		}

		protected override void SetupSupportTrackers(Rigidbody rb) { LegTrackers(rb); }
	}

	public sealed class ThighRegion : FRegion
	{
		public ThighRegion(Ctx c, int id) : base(c, id, "Thigh") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			SidedBodies(rbNames, true);
		}

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			a.x = 1f; d.x = 2f; d.y = .25f;
			ApplyDefaultSet(a, s, d, m);
		}

		protected override void SetupSupportTrackers(Rigidbody rb) { LegTrackers(rb); }

		protected override float GetFlex(int direction, int side)
		{
			return direction < 2 ? rbDynamics[side].transform.forward.y : rbDynamics[side].transform.right.y;
		}

		protected override bool Flexed(float flex, int direction, int side)
		{
			if (direction < 2) return direction == 0 ? flex > 0f : flex < 0f;
			bool flexed = direction == 2 ? flex > 0f : flex < 0f;
			if (side == 1) flexed = !flexed;
			return flexed;
		}

		protected override float GetAcceleration(int direction, int side)
		{
			float val = direction < 2 ? rbDynamics[side].angularAcceleration.x : rbDynamics[side].angularAcceleration.z;
			return val * amount[direction];
		}
	}

	public sealed class AbdomenRegion : FRegion
	{
		private Dyn alt;

		public AbdomenRegion(Ctx c, int id) : base(c, id, "Abdomen") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			rbDynamics = new Dyn[1];
			Rigidbody rb = Bodies.Force(atom, rbNames[0]);
			Rigidbody rb2 = Bodies.Force(atom, rbNames[1]);
			if (rb == null || rb2 == null)
			{
				Missing = true;
				ctx.Log("Body " + rbNames[0] + " or " + rbNames[1] + " was not found on " + atom.uid + ": Abdomen is skipped.");
				return;
			}
			rbDynamics[0] = ctx.Dyns.Get(rb);
			alt = ctx.Dyns.Get(rb2);
		}

		public override void Dispose()
		{
			base.Dispose();
			ctx.Dyns.Release(alt);
			alt = null;
		}

		public override void GetMorphs() { GetSingleMorphs(); }

		protected override void SetupDefaults()
		{
			Vector4 m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			Vector4 d = new Vector4(.35f, .125f, .5f, .5f);
			s.x = .5f;
			ApplyDefaultSet(a, s, d, m);
		}

		protected override float GetFlex(int direction, int side)
		{
			return direction < 2 ? rbDynamics[side].rigidbody.transform.forward.y : rbDynamics[side].rigidbody.transform.right.y;
		}

		protected override bool Flexed(float flex, int direction, int side)
		{
			return direction == 0 || direction == 2 ? flex > 0f : flex < 0f;
		}

		protected override float GetAcceleration(int direction, int side)
		{
			float val;
			if (direction < 2) val = rbDynamics[side].angularAcceleration.x - .75f * alt.angularAcceleration.x;
			else val = rbDynamics[side].angularAcceleration.z - .75f * alt.angularAcceleration.z;
			return val * amount[direction];
		}
	}

	public sealed class ShoulderRegion : FRegion
	{
		public ShoulderRegion(Ctx c, int id) : base(c, id, "Shoulder") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			SidedBodies(rbNames, true);
		}

		protected override void SetupDefaults()
		{
			Vector4 m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			a.w = 1f;
			Vector4 d = new Vector4(1.2f, 1.2f, 1.2f, 1.2f);
			ApplyDefaultSet(a, s, d, m);
		}

		protected override float GetFlex(int direction, int side)
		{
			return direction < 2 ? rbDynamics[side].transform.forward.y : rbDynamics[side].transform.right.y;
		}

		protected override bool Flexed(float flex, int direction, int side)
		{
			if (direction < 2) return direction == 0 ? flex > 0f : flex < 0f;
			bool flexed = direction == 2 ? flex > 0f : flex < 0f;
			if (side == 1) flexed = !flexed;
			return flexed;
		}

		protected override float GetAcceleration(int direction, int side)
		{
			float val;
			if (direction < 2) val = (side == 0 ? -1f : 1f) * rbDynamics[side].angularAcceleration.y;
			else val = (side == 0 ? 1f : -1f) * rbDynamics[side].angularAcceleration.z;
			return val * amount[direction];
		}
	}

	public sealed class ElbowRegion : FRegion
	{
		public ElbowRegion(Ctx c, int id) : base(c, id, "Elbow") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			SidedBodies(rbNames, true);
		}

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			a.x = 1f; a.y = .65f;
			ApplyDefaultSet(a, s, d, m);
		}
	}

	public sealed class NeckRegion : FRegion
	{
		public NeckRegion(Ctx c, int id) : base(c, id, "Neck") { }

		public override void Init(string[] rbNames)
		{
			base.Init(rbNames);
			rbDynamics = new Dyn[1];
			Rigidbody rb = Bodies.Force(atom, rbNames[0]);
			if (rb == null)
			{
				Missing = true;
				ctx.Log("Body " + rbNames[0] + " was not found on " + atom.uid + ": Neck is skipped.");
				return;
			}
			rbDynamics[0] = ctx.Dyns.Get(rb);
		}

		public override void GetMorphs() { GetSingleMorphs(); }

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def;
			ApplyDefaultSet(new Vector4(2f, 1.5f, 1f, 1f), s, d, m);
		}

		protected override float GetFlex(int direction, int side)
		{
			if (direction < 2) return rbDynamics[side].transform.forward.y;
			return rbDynamics[side].transform.localRotation.y * 10f;
		}

		protected override bool Flexed(float flex, int direction, int side)
		{
			if (direction < 2) return direction == 1 ? flex > 0f : flex < 0f;
			return direction == 2 ? flex > 0f : flex < 0f;
		}

		protected override float GetAcceleration(int direction, int side)
		{
			if (direction < 2) return -rbDynamics[side].angularAcceleration.x * amount[direction];
			return -rbDynamics[side].angularAcceleration.y * amount[direction];
		}
	}

	// one finger of the hand region: bends are read from the local rotation of the first joint
	public sealed class FingerRegion : FRegion
	{
		private Func<int, int, float> getFlex;

		public FingerRegion(Ctx c, int id, int fingerId) : base(c, id, FingerName(fingerId))
		{
			switch (fingerId)
			{
				case 0:
					getFlex = delegate (int direction, int side)
					{
						if (side == 0) return MapRange(rbDynamics[side].transform.localRotation.z, .2f, .1f, .2f, 1f, 0f);
						return MapRange(rbDynamics[side].transform.localRotation.z, -.2f, -.1f, -.2f, 0f, 1f);
					};
					break;
				case 1:
				case 2:
					getFlex = delegate (int direction, int side)
					{
						if (side == 0) return MapRange(rbDynamics[side].transform.localRotation.z, .1f, 0f, .1f, 1f, 0f);
						return MapRange(rbDynamics[side].transform.localRotation.z, -.1f, 0f, -.1f, 0f, 1f);
					};
					break;
				default:
					getFlex = delegate (int direction, int side)
					{
						if (side == 0) return MapRange(rbDynamics[side].transform.localRotation.z, -.1f, 0f, -.1f, 0f, 1f);
						return MapRange(rbDynamics[side].transform.localRotation.z, .1f, 0f, .1f, 1f, 0f);
					};
					break;
			}
		}

		public static string FingerName(int i)
		{
			return i == 0 ? "Index" : i == 1 ? "Mid" : i == 2 ? "Ring" : "Pinky";
		}

		public void InitFinger()
		{
			base.Init(null);
			rbDynamics = new Dyn[2];
			for (int i = 0; i < 2; i++)
			{
				string side = i == 0 ? "l" : "r";
				Rigidbody rb = Bodies.Rb(atom, side + name + "1");
				if (rb == null)
				{
					Missing = true;
					ctx.Log("Body " + side + name + "1 was not found on " + atom.uid + ": " + name + " is skipped.");
					return;
				}
				rbDynamics[i] = ctx.Dyns.Get(rb);
			}
		}

		protected override float GetFlex(int direction, int side) { return getFlex(direction, side); }

		protected override float GetAcceleration(int direction, int side)
		{
			float val = rbDynamics[side].angularAcceleration.z;
			return val * dynamicStrength[direction];
		}
	}

	// the hand: four fingers sharing one set of settings
	public sealed class HandRegion : FRegion
	{
		public readonly List<FingerRegion> fingers = new List<FingerRegion>();

		public HandRegion(Ctx c, int id) : base(c, id, "Hand") { }

		public override void Init(string[] rbNames)
		{
			for (int i = 0; i < 4; i++)
			{
				FingerRegion f = new FingerRegion(ctx, id, i);
				f.InitFinger();
				fingers.Add(f);
			}
			enabledJ.changed = OnEnabled;
			dynamicStrengthJ.changed = delegate (Vector4 v) { for (int i = 0; i < fingers.Count; i++) fingers[i].OnDynamicStrengthChanged(v); };
			minAccelJ.changed = delegate (Vector4 v) { for (int i = 0; i < fingers.Count; i++) fingers[i].minAccel = v; };
			sRatioJ.changed = delegate (Vector4 v) { for (int i = 0; i < fingers.Count; i++) fingers[i].OnSRatioChanged(v); };
			amountJ.changed = delegate (Vector4 v) { for (int i = 0; i < fingers.Count; i++) fingers[i].OnAmountChanged(v); };
			SetupDefaults();
		}

		public override void GetMorphs()
		{
			for (int i = 0; i < fingers.Count; i++) fingers[i].GetMorphs();
		}

		protected override void SetupDefaults()
		{
			Vector4 d = dynamicStrengthJ.def, m = minAccelJ.def, s = sRatioJ.def, a = amountJ.def;
			d.x = .5f;
			ApplyDefaultSet(a, s, d, m);
		}

		public override void OnEnable()
		{
			for (int i = 0; i < fingers.Count; i++) fingers[i].Active = true;
		}

		public override void OnDisable()
		{
			for (int i = 0; i < fingers.Count; i++) { fingers[i].Active = false; fingers[i].Reset(); }
		}

		public override void Reset()
		{
			for (int i = 0; i < fingers.Count; i++) fingers[i].Reset();
		}

		public override void Tick()
		{
			if (!Active) return;
			for (int i = 0; i < fingers.Count; i++) fingers[i].Tick();
		}

		public override void Dispose()
		{
			for (int i = 0; i < fingers.Count; i++) fingers[i].Dispose();
			fingers.Clear();
		}

		public override int MorphCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < fingers.Count; i++) n += fingers[i].MorphCount;
				return n;
			}
		}
	}
}
