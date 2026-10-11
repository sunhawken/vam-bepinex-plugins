using System;
using System.Collections.Generic;
using System.Globalization;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ShakeItStandalone
{
	// JSONStorableBool semantics without VaM: setting the same value does nothing, otherwise the callback runs.
	public sealed class Flag
	{
		public bool val;
		public bool def;
		public Action<bool> changed;
		public Flag(bool v) { val = def = v; }
		public bool Set(bool v)
		{
			if (v == val)
			{
				return false;
			}
			val = v;
			if (changed != null)
			{
				changed(v);
			}
			return true;
		}
	}

	// the original's JSONStorableVector4 as plain data: value, default, limits; the callback runs on change.
	public sealed class V4Param
	{
		public readonly string Key;
		public Vector4 val, def, min, max;
		public Action<Vector4> changed;

		public V4Param(string key, Vector4 def, Vector4 max)
		{
			Key = key;
			this.def = def;
			val = def;
			min = new Vector4(0f, 0f, 0f, 0f);
			this.max = max;
		}

		public static bool Same(Vector4 a, Vector4 b)
		{
			return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
		}

		public void Set(Vector4 v)
		{
			if (Same(v, val))
			{
				return;
			}
			val = v;
			if (changed != null)
			{
				changed(v);
			}
		}

		public void Store(JSONClass jc)
		{
			jc[Key] = J.V4(val);
		}

		public void Load(JSONClass jc)
		{
			Vector4 v = val;
			if (J.TryV4(jc, Key, ref v))
			{
				Set(v);
			}
		}
	}

	public static class J
	{
		public static string F(float f)
		{
			return f.ToString("0.######", CultureInfo.InvariantCulture);
		}

		public static JSONArray V4(Vector4 v)
		{
			JSONArray a = new JSONArray();
			a.Add(new JSONData(F(v.x)));
			a.Add(new JSONData(F(v.y)));
			a.Add(new JSONData(F(v.z)));
			a.Add(new JSONData(F(v.w)));
			return a;
		}

		public static bool Float(JSONNode n, out float f)
		{
			f = 0f;
			if (n == null)
			{
				return false;
			}
			return float.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
		}

		public static bool TryV4(JSONClass jc, string key, ref Vector4 v)
		{
			if (jc == null || !jc.HasKey(key))
			{
				return false;
			}
			JSONArray a = jc[key] as JSONArray;
			if (a == null || a.Count < 4)
			{
				return false;
			}
			float x, y, z, w;
			if (!Float(a[0], out x) || !Float(a[1], out y) || !Float(a[2], out z) || !Float(a[3], out w))
			{
				return false;
			}
			v = new Vector4(x, y, z, w);
			return true;
		}

		public static bool TryBool(JSONClass jc, string key, out bool b)
		{
			b = false;
			if (jc == null || !jc.HasKey(key))
			{
				return false;
			}
			string s = jc[key].Value;
			if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) { b = true; return true; }
			if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) { b = false; return true; }
			return false;
		}

		public static bool TryFloat(JSONClass jc, string key, out float f)
		{
			f = 0f;
			if (jc == null || !jc.HasKey(key))
			{
				return false;
			}
			return Float(jc[key], out f);
		}

		public static JSONClass Obj(JSONClass jc, string key)
		{
			if (jc == null || !jc.HasKey(key))
			{
				return null;
			}
			return jc[key] as JSONClass;
		}

		public static string Bool(bool b)
		{
			return b ? "true" : "false";
		}
	}

	// Physics bookkeeping of one rigidbody (RBDynamics): velocity, acceleration, tension. One per body and person, shared by all modules.
	public sealed class Dyn
	{
		public readonly Rigidbody rigidbody;
		public readonly Transform transform;
		public int uses;
		public Vector3 velocity, angularVelocity;
		private Vector3 lastVelocity, lastAngularVel;
		private Vector3 _acc, _localAcc, _angAcc;
		private bool accSet, localAccSet, angAccSet, activitySet, tensionSet;
		private float _tension, _activity;
		private Dyn parent;
		private Func<float> getTension;
		private readonly DynSet set;
		private bool relativesDone;

		public Dyn(DynSet set, Rigidbody rb)
		{
			this.set = set;
			rigidbody = rb;
			transform = rb.transform;
			getTension = delegate { return transform.localRotation.x; };
		}

		public void Tick()
		{
			lastVelocity = velocity;
			lastAngularVel = angularVelocity;
			velocity = rigidbody.velocity;
			angularVelocity = transform.InverseTransformDirection(rigidbody.angularVelocity);
			accSet = localAccSet = angAccSet = activitySet = tensionSet = false;
		}

		public Vector3 acceleration
		{
			get
			{
				if (accSet)
				{
					return _acc;
				}
				for (int i = 0; i < 3; i++)
				{
					if (Mathf.Sign(velocity[i]) + Mathf.Sign(lastVelocity[i]) == 0f)
					{
						_acc = Vector3.zero;
						break;
					}
					_acc[i] = (velocity[i] - lastVelocity[i]) / Time.fixedDeltaTime;
				}
				accSet = true;
				return _acc;
			}
		}

		public Vector3 localAcceleration
		{
			get
			{
				if (localAccSet)
				{
					return _localAcc;
				}
				_localAcc = transform.InverseTransformDirection(acceleration);
				localAccSet = true;
				return _localAcc;
			}
		}

		public Vector3 angularAcceleration
		{
			get
			{
				if (angAccSet)
				{
					return _angAcc;
				}
				for (int i = 0; i < 3; i++)
				{
					if (Mathf.Sign(angularVelocity[i]) + Mathf.Sign(lastAngularVel[i]) == 0f)
					{
						_angAcc = Vector3.zero;
						break;
					}
					_angAcc[i] = (angularVelocity[i] - lastAngularVel[i]) / Time.fixedDeltaTime;
				}
				angAccSet = true;
				return _angAcc;
			}
		}

		public float tension
		{
			get
			{
				if (tensionSet)
				{
					return _tension;
				}
				_tension = getTension();
				tensionSet = true;
				return _tension;
			}
		}

		public float activity
		{
			get
			{
				if (activitySet)
				{
					return _activity;
				}
				float act = rigidbody.mass * Math.Abs(Vector3.Dot(acceleration, velocity));
				Vector3 aa = angularAcceleration;
				Vector3 it = rigidbody.inertiaTensor;
				for (int i = 0; i < 3; i++)
				{
					act += Mathf.Abs(aa[i] * it[i] * angularVelocity[i]);
				}
				_activity = act;
				activitySet = true;
				return act;
			}
		}

		// SetTensionFunction: how bent this body is, 1 = tense; knows its parent body for the abdomen
		public void SetTensionFunction()
		{
			if (!relativesDone)
			{
				relativesDone = true;
				Rigidbody prb = transform.parent == null ? null : transform.parent.GetComponent<Rigidbody>();
				if (prb != null)
				{
					parent = set.Get(prb);
				}
			}
			getTension = delegate { return 1f - Mathf.Clamp(1.2f * Mathf.Abs(transform.localRotation.x), 0f, 1f); };
			string n = rigidbody.name;
			if (n.Contains("abdomen"))
			{
				getTension = delegate { return Mathf.Clamp(.3f + (parent == null ? 0f : parent.tension) - transform.localRotation.x, 0f, 1f); };
			}
			else if (n.Contains("Glute") || n.Contains("Thigh"))
			{
				getTension = delegate { return Mathf.Clamp(Mathf.Abs(.3f + 1.6f * transform.localRotation.x), 0f, 1f); };
			}
		}
	}

	public sealed class DynSet
	{
		private readonly Dictionary<Rigidbody, Dyn> map = new Dictionary<Rigidbody, Dyn>();
		private readonly List<Dyn> list = new List<Dyn>();

		public Dyn Get(Rigidbody rb)
		{
			Dyn d;
			if (!map.TryGetValue(rb, out d))
			{
				d = new Dyn(this, rb);
				map[rb] = d;
				list.Add(d);
			}
			d.uses++;
			return d;
		}

		public void Release(Dyn d)
		{
			if (d == null)
			{
				return;
			}
			d.uses--;
			if (d.uses <= 0)
			{
				map.Remove(d.rigidbody);
				list.Remove(d);
			}
		}

		public int Count { get { return list.Count; } }

		public void Tick()
		{
			for (int i = list.Count - 1; i >= 0; i--)
			{
				Dyn d = list[i];
				if (d.rigidbody == null)
				{
					list.RemoveAt(i);
					continue;
				}
				d.Tick();
			}
		}
	}

	// frame timing of the Renderer Fps option (FlexIt statics): Realtime follows the physics step, a number pins the output frame time
	public sealed class Timing
	{
		public bool isRealtime = true;
		public float _targetFrametime = 1f / 60f;
		private float _realFrametime;
		public float physicsRateFactor = 1f;
		public float flexRegressionSpeed = 10f * 0.02f;
		private const float Speed = 10f;
		private System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

		public float realFrametime { get { return isRealtime ? Time.fixedDeltaTime : _realFrametime; } }
		public float targetFrametime { get { return isRealtime ? Time.fixedDeltaTime : _targetFrametime; } }

		public void Tick()
		{
			if (isRealtime)
			{
				flexRegressionSpeed = Speed * Time.fixedDeltaTime;
				physicsRateFactor = 60f * Time.fixedDeltaTime;
			}
			else
			{
				flexRegressionSpeed = Speed * targetFrametime;
				_realFrametime = (float)watch.Elapsed.TotalSeconds;
				watch.Reset();
				watch.Start();
			}
		}

		public void SetOption(string val)
		{
			float fps;
			if (val == "Realtime" || !float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fps) || fps <= 0f)
			{
				isRealtime = true;
			}
			else
			{
				isRealtime = false;
				_targetFrametime = 1f / fps;
			}
		}
	}
}
