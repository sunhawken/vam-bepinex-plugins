using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroT.AutoFlutterHipStandalone
{
	// One morph slot of the hips or legs tab (AutoFlutterHip.Morpher).
	public sealed class MorphSlot
	{
		public string Key;            // "Bending Waist ::: Morph-Morph #0" in the original
		public string Tab;            // "Hips" / "Legs"
		public int Index;
		public bool IsLeg;
		public bool Act = true;       // Enable Morph
		public bool Dbg;              // Test Morph
		public float Min = 0.5f, Max = 0.5f, Mul = 0.7f;
		public float Val;             // current value / test value
		public string Name = "None";  // Select Morph
		public JSONStorableFloat Monitor;

		public string Label { get { return Tab + " Morph #" + Index; } }

		public void CopyFrom(MorphSlot m)
		{
			Act = m.Act; Dbg = m.Dbg; Min = m.Min; Max = m.Max; Mul = m.Mul; Name = m.Name;
		}
	}

	// The hip / leg angle morphing of one female Person (AutoFlutterHip + UtilMorph), per person instead of the .var's shared statics.
	public sealed class MorphRig
	{
		public const int HipSlots = 4, LegSlots = 4;
		public static readonly string[] DefaultHips = { "AutoFlutterHip - Doggy Style Shape (TenStrip)", "AutoFlutterHip - Hourglass Waist (TenStrip)", "AutoFlutterHip - Waist ErectorSpinae (TenStrip)", "None" };
		public static readonly string[] DefaultLegs = { "AutoFlutterHip - Thights Indent (kemenate)", "AutoFlutterHip - Gracilis Stretch (kemenate)", "None", "None" };
		private const float Epsilon = 0.003f;

		public readonly Atom Atom;
		public readonly List<MorphSlot> Slots = new List<MorphSlot>();
		private readonly Action<string> log;
		private readonly Dictionary<string, DAZMorph> morphs = new Dictionary<string, DAZMorph>();
		private readonly Dictionary<string, float> lerpmap = new Dictionary<string, float>();
		private readonly HashSet<string> reported = new HashSet<string>();
		private Rigidbody head, neck, chst, hips, lkne, rkne;
		private bool rigidsMissingLogged;
		public bool Debug;
		private readonly Dictionary<string, GameObject> points = new Dictionary<string, GameObject>();
		private readonly Dictionary<string, LineRenderer> lines = new Dictionary<string, LineRenderer>();

		public MorphRig(Atom atom, Action<string> log)
		{
			Atom = atom;
			this.log = log;
		}

		// ---- morph names of this person: every morph that is not a pose control and does not start with "!" (AutoFlutterHip.getMorph)
		public List<string> MorphNames()
		{
			List<string> o = new List<string>();
			try
			{
				DAZCharacterSelector dcs = Atom.GetStorableByID("geometry") as DAZCharacterSelector;
				GenerateDAZMorphsControlUI m = dcs == null ? null : dcs.morphsControlUI;
				if (m == null)
				{
					return o;
				}
				List<string> l = m.GetMorphDisplayNames();
				for (int i = 0; i < l.Count; i++)
				{
					string nme = l[i];
					DAZMorph mor = m.GetMorphByDisplayName(nme);
					if (mor == null || mor.isPoseControl)
					{
						continue;
					}
					if (nme.StartsWith("!") || (mor.displayName != null && mor.displayName.StartsWith("!")))
					{
						continue;
					}
					o.Add(mor.displayName);
				}
			}
			catch (Exception e)
			{
				log("Morph list failed: " + e.Message);
			}
			return o;
		}

		// ---- UtilMorph
		private DAZMorph LoadMorph(string morph)
		{
			DAZMorph cached;
			if (morphs.TryGetValue(morph, out cached) && cached != null)
			{
				return cached;
			}
			DAZCharacterSelector dcs = Atom.GetStorableByID("geometry") as DAZCharacterSelector;
			GenerateDAZMorphsControlUI mui = dcs == null ? null : dcs.morphsControlUI;
			DAZMorph mor = mui == null ? null : mui.GetMorphByDisplayName(morph);
			if (mor == null)
			{
				if (reported.Add(morph))
				{
					log("morph [" + morph + "] was not found on " + Atom.uid);
				}
				return null;
			}
			morphs[morph] = mor;
			return mor;
		}

		public void ApplyMorph(string morph, float value)
		{
			DAZMorph m = LoadMorph(morph);
			if (m == null)
			{
				return;
			}
			if (m.morphValue == value)
			{
				return;
			}
			if (value == 0f || Epsilon < Mathf.Abs(m.morphValue - value))
			{
				m.morphValue = value;
			}
		}

		private float Lerp(string name, float v, float t)
		{
			float bef;
			lerpmap.TryGetValue(name, out bef);
			float cur = Mathf.Lerp(bef, v, t);
			lerpmap[name] = cur;
			return cur;
		}

		// every morph this person has used goes back to 0 (UtilMorph.restoreMorph)
		public void RestoreMorphs()
		{
			foreach (KeyValuePair<string, DAZMorph> kv in morphs)
			{
				if (kv.Value != null)
				{
					kv.Value.morphValue = 0f;
				}
			}
		}

		// ---- the six body parts
		private bool Prepare()
		{
			if (hips != null && head != null)
			{
				return true;
			}
			Rigidbody[] rb = Atom.rigidbodies;
			head = Find(rb, "head"); neck = Find(rb, "neck"); hips = Find(rb, "hip"); chst = Find(rb, "chest");
			lkne = Find(rb, "lKneeLink"); rkne = Find(rb, "rKneeLink");
			if (head == null || neck == null || hips == null || chst == null || lkne == null || rkne == null)
			{
				if (!rigidsMissingLogged)
				{
					rigidsMissingLogged = true;
					log("The body parts head, neck, hip, chest, lKneeLink and rKneeLink were not all found on " + Atom.uid + "; the morphs wait for them.");
				}
				head = null;
				return false;
			}
			return true;
		}

		private static Rigidbody Find(Rigidbody[] all, string name)
		{
			for (int i = 0; all != null && i < all.Length; i++)
			{
				if (all[i] != null && all[i].name == name)
				{
					return all[i];
				}
			}
			return null;
		}

		public static float Angle(Vector3 one, Vector3 two, Vector3 thr)
		{
			Vector3 a = thr - two;
			Vector3 b = one - two;
			return Vector3.Angle(a, b);
		}

		private Vector3 headpos, neckpos, chstpos, hipspos, lknepos, rknepos;

		// rate_hips / rate_legs: how far the hips are bent / the legs spread, 0..1 between the two angle offsets
		public float RateHips(float min, float max)
		{
			float angle_stt = 180f - (160f - 40f * min);
			float angle_end = 180f - (50f + 40f * max);
			float angle_dif = angle_end - angle_stt;
			float lkne2hips2chst = 180f - Angle(lknepos, hipspos, chstpos);
			float rkne2hips2chst = 180f - Angle(rknepos, hipspos, chstpos);
			float knee2hips2chst = Mathf.Max(lkne2hips2chst, rkne2hips2chst);
			knee2hips2chst = Mathf.Min(Mathf.Max(knee2hips2chst, angle_stt), angle_end);
			return (knee2hips2chst - angle_stt) / angle_dif;
		}

		public float RateLegs(float min, float max)
		{
			float angle_stt = 20f + 40f * min;
			float angle_end = 90f + 40f * max;
			float angle_dif = angle_end - angle_stt;
			float lkne2hips2rkne = Angle(lknepos, hipspos, rknepos);
			lkne2hips2rkne = Mathf.Min(Mathf.Max(lkne2hips2rkne, angle_stt), angle_end);
			return (lkne2hips2rkne - angle_stt) / angle_dif;
		}

		// FixedUpdate: process() + apply()
		public void Process()
		{
			if (!Prepare())
			{
				return;
			}
			headpos = head.transform.position; neckpos = neck.transform.position; chstpos = chst.transform.position;
			hipspos = hips.transform.position; lknepos = lkne.transform.position; rknepos = rkne.transform.position;
			if (Debug)
			{
				DrawDebug();
			}
			for (int i = 0; i < Slots.Count; i++)
			{
				MorphSlot mor = Slots[i];
				if (!mor.Act || mor.Name == "None" || string.IsNullOrEmpty(mor.Name))
				{
					continue;
				}
				if (mor.Dbg)
				{
					Lerp(mor.Name, mor.Val, 0.1f);
					ApplyMorph(mor.Name, mor.Val);
				}
				else
				{
					float r = mor.IsLeg ? RateLegs(mor.Min, mor.Max) : RateHips(mor.Min, mor.Max);
					float v = Lerp(mor.Name, r * mor.Mul, 0.1f);
					ApplyMorph(mor.Name, v);
					mor.Val = v;
					if (mor.Monitor != null)
					{
						mor.Monitor.valNoCallback = v;
					}
				}
			}
		}

		// ---- "Show Debug": the six points and the five lines the original draws
		private static readonly Color PointColor = new Color(0.8f, 0.1f, 0.1f, 0.8f);
		private static readonly Color LineColor = new Color(0f, 1f, 0f, 0.3f);

		private void Point(string n, Vector3 v)
		{
			GameObject g;
			if (!points.TryGetValue(n, out g) || g == null)
			{
				g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				g.name = "AutoFlutterHip debug " + n;
				g.transform.localScale = new Vector3(0.008f, 0.008f, 0.008f);
				Collider c = g.GetComponent<Collider>();
				if (c != null)
				{
					UnityEngine.Object.Destroy(c);
				}
				points[n] = g;
			}
			g.transform.position = v;
			Renderer r = g.GetComponent<Renderer>();
			if (r != null && r.material != null)
			{
				r.material.color = PointColor;
			}
		}

		private void Line(string n, Vector3 a, Vector3 b)
		{
			LineRenderer l;
			if (!lines.TryGetValue(n, out l) || l == null)
			{
				GameObject g = new GameObject("AutoFlutterHip debug line " + n);
				l = g.AddComponent<LineRenderer>();
				l.SetVertexCount(2);
				l.SetWidth(0.004f, 0.004f);
				Shader sh = Shader.Find("Sprites/Default");
				if (sh != null)
				{
					l.material = new Material(sh);
				}
				lines[n] = l;
			}
			l.SetPosition(0, a);
			l.SetPosition(1, b);
			if (l.material != null)
			{
				l.material.color = LineColor;
			}
		}

		private void DrawDebug()
		{
			Point("head", headpos); Point("neck", neckpos); Point("chst", chstpos); Point("hips", hipspos); Point("lkne", lknepos); Point("rkne", rknepos);
			Line("neck2head", neckpos, headpos); Line("chst2neck", chstpos, neckpos); Line("hips2chst", hipspos, chstpos);
			Line("hips2lkne", hipspos, lknepos); Line("hips2rkne", hipspos, rknepos);
		}

		public int DebugObjectCount { get { return points.Count + lines.Count; } }

		public void ClosePoints()
		{
			foreach (KeyValuePair<string, GameObject> kv in points)
			{
				if (kv.Value != null)
				{
					UnityEngine.Object.Destroy(kv.Value);
				}
			}
			foreach (KeyValuePair<string, LineRenderer> kv in lines)
			{
				if (kv.Value != null)
				{
					UnityEngine.Object.Destroy(kv.Value.gameObject);
				}
			}
			points.Clear();
			lines.Clear();
		}

		public void Close()
		{
			ClosePoints();
			RestoreMorphs();
		}
	}
}
