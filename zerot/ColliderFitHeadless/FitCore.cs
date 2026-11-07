using System;
using System.Collections.Generic;
using System.Text;
using GPUTools.Physics.Scripts.Behaviours;
using UnityEngine;

namespace ZeroT.ColliderFitHeadless
{
	// The fitting core: which fixed colliders are managed, which skin vertices belong to each, and the maths that turns those vertices into a
	// radius / centre. Written for this plugin from the idea of mutoe's ColliderFit (CC BY, https://hub.virtamate.com/members/mutoe.99301/):
	// VaM only rescales its AutoColliders with the body morphs, the fixed capsules on arms, hands, feet, neck and jaw keep the prefab size, so
	// they are refitted to the morphed skin. None of mutoe's code is in this file.

	internal static class FitState
	{
		public const int Idle = 0;        // bound, not fitted yet
		public const int Following = 1;   // being fitted to the skin
		public const int Owned = 2;       // Collider Editor (scene plugin or headless preset) has adjusted it: hands off
		public const int Foreign = 3;     // its values were changed by something else: hands off
		public const int TooFew = 4;      // too few skin vertices near it (or no direction): left alone
		public const int Anomaly = 5;     // the fitted result is outside the allowed change: not written

		public static string Label(int s)
		{
			switch (s)
			{
				case Idle: return "idle";
				case Following: return "following";
				case Owned: return "collider-editor";
				case Foreign: return "changed-by-others";
				case TooFew: return "too-few-vertices";
				case Anomaly: return "out-of-range";
			}
			return "unknown";
		}
	}

	internal static class FitMode
	{
		public const int Wrap = 0;       // skin all round the collider: centre = mean of the angular bins, radius = mean distance to it
		public const int Sandwich = 1;   // skin on two opposite sides only (flat part): same formula
		public const int Face = 2;       // skin on one side only: slide the collider along the outward normal, radius unchanged

		public static string Label(int m)
		{
			return m == Wrap ? "wrap" : (m == Sandwich ? "sandwich" : "face");
		}
	}

	internal sealed class FitTarget
	{
		public Collider col;
		public CapsuleCollider cap;               // null for a sphere
		public SphereCollider sph;                // null for a capsule
		public CapsuleLineSphereCollider gpu;     // the copy hair and cloth use; null if none
		public DAZBone bone;
		public Rigidbody body;
		public Matrix4x4 colFromBone;             // bone local -> collider local (fixed chain, independent of pose)
		public int axis;                          // capsule axis 0/1/2, -1 for a sphere
		public float r0, h0;                      // radius / height VaM gave it
		public Vector3 c0;                        // centre VaM gave it
		public float tLimit;                      // how far along the axis a vertex may sit (collider local)
		public int[] verts;                       // skin vertices assigned at bind time
		public int mode;
		public Vector3 faceN;                     // outward normal for Face mode
		public int state;
		public string uuid;                       // same key rule as Collider Editor
		public string label;
		public int group;
		public float lastR, lastH;                // what this plugin wrote last (read back)
		public Vector3 lastC;
		public bool wrote;                        // something of ours is currently on the collider
		public float skinMean;
		public int usedVerts;
		public string note;
	}

	internal static class FitBinder
	{
		public const int GroupCount = 10;
		public static readonly string[] GroupNames =
		{
			"Upper arm", "Forearm", "Hand", "Palm", "Fingers", "Foot", "Toes", "Neck", "Jaw", "Torso / legs"
		};

		private static readonly HashSet<string> Bones = BuildBones();
		private static readonly HashSet<string> SelfBones = new HashSet<string> { "lShldr", "rShldr", "lForeArm", "rForeArm" };

		private static HashSet<string> BuildBones()
		{
			HashSet<string> set = new HashSet<string>();
			string[] sides = { "l", "r" };
			string[] parts =
			{
				"Shldr", "ForeArm", "Hand", "Carpal1", "Carpal2",
				"Thumb1", "Thumb2", "Thumb3", "Index1", "Index2", "Index3", "Mid1", "Mid2", "Mid3",
				"Ring1", "Ring2", "Ring3", "Pinky1", "Pinky2", "Pinky3",
				"Foot", "Toe", "BigToe", "SmallToe1", "SmallToe2", "SmallToe3", "SmallToe4",
				"Thigh", "Shin"
			};
			for (int s = 0; s < sides.Length; s++)
			{
				for (int p = 0; p < parts.Length; p++)
				{
					set.Add(sides[s] + parts[p]);
				}
			}
			string[] centre = { "neck", "lowerJaw", "pelvis", "abdomen", "abdomen2", "chest", "LGlute", "RGlute" };
			for (int i = 0; i < centre.Length; i++)
			{
				set.Add(centre[i]);
			}
			return set;
		}

		private static int GroupOf(string bone)
		{
			if (bone.EndsWith("Shldr")) return 0;
			if (bone.EndsWith("ForeArm")) return 1;
			if (bone.EndsWith("Hand")) return 2;
			if (bone.Contains("Carpal")) return 3;
			if (bone.Contains("Thumb") || bone.Contains("Index") || bone.Contains("Mid") || bone.Contains("Ring") || bone.Contains("Pinky")) return 4;
			if (bone.EndsWith("Foot")) return 5;
			if (bone.Contains("Toe")) return 6;
			if (bone == "neck") return 7;
			if (bone == "lowerJaw") return 8;
			return 9;
		}

		// The key Collider Editor uses for a collider: component type + ":" + the names from just under Genesis2Female / Genesis2Male down to the
		// collider's node, '.'-joined, with the node's own name repeated at the end.
		public static string Uuid(Collider c)
		{
			Stack<string> stack = new Stack<string>();
			stack.Push(c.name);
			for (Transform t = c.transform; t != null; t = t.parent)
			{
				string n = t.name;
				if (n.Equals("geometry", StringComparison.InvariantCultureIgnoreCase) || n.Equals("Genesis2Female", StringComparison.InvariantCultureIgnoreCase) || n.Equals("Genesis2Male", StringComparison.InvariantCultureIgnoreCase))
				{
					break;
				}
				stack.Push(n);
			}
			StringBuilder sb = new StringBuilder(c is CapsuleCollider ? "CapsuleCollider:" : (c is SphereCollider ? "SphereCollider:" : "BoxCollider:"));
			sb.Append(string.Join(".", stack.ToArray()));
			return sb.ToString();
		}

		// The active fixed colliders (only the active ones, like Collider Editor: the Advanced set or the standard set).
		public static List<FitTarget> Collect(DAZSkinV2 skin, HashSet<string> owned)
		{
			List<FitTarget> list = new List<FitTarget>();
			Transform root = skin.root.transform;
			Collider[] cols = root.GetComponentsInChildren<Collider>(false);
			for (int i = 0; i < cols.Length; i++)
			{
				Collider c = cols[i];
				CapsuleCollider cap = c as CapsuleCollider;
				SphereCollider sph = c as SphereCollider;
				if (cap == null && sph == null)
				{
					continue;
				}
				if (c.isTrigger || c.GetComponent<AutoCollider>() != null)
				{
					continue;
				}
				DAZBone bone = null;
				bool underTongue = false;
				for (Transform t = c.transform; t != null && t != root.parent; t = t.parent)
				{
					bone = t.GetComponent<DAZBone>();
					if (bone != null)
					{
						break;
					}
					if (t.name.Contains("Tongue"))
					{
						underTongue = true;
					}
				}
				if (bone == null || underTongue || !Bones.Contains(bone.name))
				{
					continue;
				}
				bool ownNode = c.transform == bone.transform;
				string nm = c.name;
				if (ownNode)
				{
					if (!SelfBones.Contains(bone.name))
					{
						continue;
					}
				}
				else if (!nm.StartsWith("_Collider") || nm == "_ColliderHair" || nm.Contains("Joint") || nm.Contains("Trigger"))
				{
					continue;
				}
				FitTarget t2 = new FitTarget();
				t2.col = c;
				t2.cap = cap;
				t2.sph = sph;
				t2.bone = bone;
				t2.group = GroupOf(bone.name);
				t2.body = c.attachedRigidbody != null ? c.attachedRigidbody : bone.GetComponent<Rigidbody>();
				t2.colFromBone = (bone.transform.worldToLocalMatrix * c.transform.localToWorldMatrix).inverse;
				t2.uuid = Uuid(c);
				t2.label = ownNode ? bone.name + "(self)" : bone.name + "/" + nm;
				if (cap != null)
				{
					t2.gpu = c.GetComponent<CapsuleLineSphereCollider>();
					t2.axis = cap.direction;
					t2.r0 = cap.radius;
					t2.h0 = cap.height;
					t2.c0 = cap.center;
					float sh = Mathf.Max(0f, t2.h0 * 0.5f - t2.r0);
					t2.tLimit = Mathf.Max(sh * 0.9f, 0.5f * t2.r0);
				}
				else
				{
					t2.axis = -1;
					t2.r0 = sph.radius;
					t2.c0 = sph.center;
					t2.tLimit = t2.r0;
				}
				t2.lastR = t2.r0;
				t2.lastH = t2.h0;
				t2.lastC = t2.c0;
				t2.state = owned != null && owned.Contains(t2.uuid) ? FitState.Owned : FitState.Idle;
				list.Add(t2);
			}
			return list;
		}

		private static DAZBone ParentBone(DAZBone b)
		{
			for (Transform p = b.transform.parent; p != null; p = p.parent)
			{
				DAZBone d = p.GetComponent<DAZBone>();
				if (d != null)
				{
					return d;
				}
			}
			return null;
		}

		// Give every skin vertex to the nearest collider (by distance to its surface) among the colliders on its own bone, the parent bone and the
		// child bones. Decided once, with the shape VaM gave the collider: morphs move vertices, they do not reshuffle the assignment.
		public static string Assign(List<FitTarget> targets, DAZSkinV2 skin, MorphVerts mv, float[] sx, float[] sy, int[] cnt)
		{
			if (!mv.Bound)
			{
				return "vertex data unavailable";
			}
			Vector3[] V = mv.cur;
			DAZBone[] strong = skin.strongestDAZBone;
			int nb = mv.count;
			if (strong == null || strong.Length < nb)
			{
				return "vertex data unavailable";
			}
			Dictionary<DAZBone, List<int>> buckets = new Dictionary<DAZBone, List<int>>();
			List<DAZBone> keys = new List<DAZBone>();
			for (int i = 0; i < nb; i++)
			{
				DAZBone b = strong[i];
				if (b == null)
				{
					continue;
				}
				List<int> l;
				if (!buckets.TryGetValue(b, out l))
				{
					l = new List<int>();
					buckets[b] = l;
					keys.Add(b);
				}
				l.Add(i);
			}
			DAZBone[] parentOf = new DAZBone[keys.Count];
			for (int k = 0; k < keys.Count; k++)
			{
				parentOf[k] = ParentBone(keys[k]);
			}
			float[] bestD = new float[nb];
			int[] bestT = new int[nb];
			for (int i = 0; i < nb; i++)
			{
				bestD[i] = float.MaxValue;
				bestT[i] = -1;
			}
			for (int ti = 0; ti < targets.Count; ti++)
			{
				FitTarget t = targets[ti];
				if (t.state == FitState.Owned)
				{
					continue;
				}
				Matrix4x4 m = FitMath.ToCollider(t);
				float sh = t.axis >= 0 ? Mathf.Max(0f, t.h0 * 0.5f - t.r0) : 0f;
				float dLimit = Mathf.Max(0.015f, 0.8f * t.r0);
				DAZBone self = t.bone;
				DAZBone parent = ParentBone(self);
				for (int k = 0; k < keys.Count; k++)
				{
					DAZBone kb = keys[k];
					if (kb != self && kb != parent && parentOf[k] != self)
					{
						continue;
					}
					List<int> l = buckets[kb];
					for (int j = 0; j < l.Count; j++)
					{
						int vi = l[j];
						Vector3 q = m.MultiplyPoint3x4(V[vi]) - t.c0;
						if (t.axis >= 0)
						{
							float a = FitMath.C(q, t.axis);
							if (a > t.tLimit || a < -t.tLimit)
							{
								continue;
							}
							q = FitMath.SetC(q, t.axis, a - Mathf.Clamp(a, -sh, sh));
						}
						float ad = Mathf.Abs(q.magnitude - t.r0);
						if (ad > dLimit)
						{
							continue;
						}
						if (ad < bestD[vi])
						{
							bestD[vi] = ad;
							bestT[vi] = ti;
						}
					}
				}
			}
			int[] counts = new int[targets.Count];
			for (int i = 0; i < nb; i++)
			{
				if (bestT[i] >= 0)
				{
					counts[bestT[i]]++;
				}
			}
			for (int ti = 0; ti < targets.Count; ti++)
			{
				targets[ti].verts = counts[ti] > 0 ? new int[counts[ti]] : null;
			}
			int[] fill = new int[targets.Count];
			for (int i = 0; i < nb; i++)
			{
				int ti = bestT[i];
				if (ti >= 0)
				{
					targets[ti].verts[fill[ti]++] = i;
				}
			}
			for (int ti = 0; ti < targets.Count; ti++)
			{
				FitTarget t = targets[ti];
				if (t.state == FitState.Owned)
				{
					continue;
				}
				t.usedVerts = counts[ti];
				if (counts[ti] < FitMath.MinVerts)
				{
					t.state = FitState.TooFew;
					t.note = counts[ti] + " vertices";
					continue;
				}
				if (!FitMath.Analyze(t, V, sx, sy, cnt))
				{
					t.state = FitState.TooFew;
					t.note = "no direction";
				}
			}
			return null;
		}
	}

	internal static class FitMath
	{
		public const int Bins = 16;
		public const int MinVerts = 12;
		public const float MaxScaleChange = 0.5f;     // radius may change by at most +-50%
		public const float MaxCenterMove = 0.015f;    // centre may move by at most 15 mm

		public static float C(Vector3 v, int i)
		{
			return i == 0 ? v.x : (i == 1 ? v.y : v.z);
		}

		public static Vector3 SetC(Vector3 v, int i, float x)
		{
			if (i == 0) v.x = x; else if (i == 1) v.y = x; else v.z = x;
			return v;
		}

		// morphed skin vertex (rest space) -> collider local space
		public static Matrix4x4 ToCollider(FitTarget t)
		{
			return t.colFromBone * t.bone.morphedWorldToLocalMatrix;
		}

		private static int Bin(float u, float v)
		{
			int k = (int)((Mathf.Atan2(v, u) + Mathf.PI) * (Bins / (2f * Mathf.PI)));
			return k >= Bins ? Bins - 1 : (k < 0 ? 0 : k);
		}

		// Once, at bind time: wrap / sandwich / face from where the skin sits around the collider. False when no direction can be told.
		public static bool Analyze(FitTarget t, Vector3[] V, float[] sx, float[] sy, int[] cnt)
		{
			Matrix4x4 m = ToCollider(t);
			int n = t.verts.Length;
			if (t.axis < 0)
			{
				Vector3 sum = Vector3.zero;
				for (int i = 0; i < n; i++)
				{
					sum += m.MultiplyPoint3x4(V[t.verts[i]]) - t.c0;
				}
				sum /= n;
				if (sum.magnitude < 1e-4f)
				{
					return false;
				}
				t.faceN = sum.normalized;
				t.mode = FitMode.Face;
				return true;
			}
			int ua = (t.axis + 1) % 3, va = (t.axis + 2) % 3;
			float cu = C(t.c0, ua), cv = C(t.c0, va);
			Array.Clear(sx, 0, Bins);
			Array.Clear(sy, 0, Bins);
			Array.Clear(cnt, 0, Bins);
			float mu = 0f, mv = 0f;
			for (int i = 0; i < n; i++)
			{
				Vector3 p = m.MultiplyPoint3x4(V[t.verts[i]]);
				float u = C(p, ua) - cu, v = C(p, va) - cv;
				int b = Bin(u, v);
				sx[b] += u;
				sy[b] += v;
				cnt[b]++;
				mu += u;
				mv += v;
			}
			int nonEmpty = 0;
			for (int k = 0; k < Bins; k++)
			{
				if (cnt[k] > 0)
				{
					nonEmpty++;
				}
			}
			if (nonEmpty >= 12)
			{
				t.mode = FitMode.Wrap;
				return true;
			}
			int opp = 0;
			for (int k = 0; k < Bins; k++)
			{
				if (cnt[k] == 0)
				{
					continue;
				}
				if (cnt[(k + 7) % Bins] > 0 || cnt[(k + 8) % Bins] > 0 || cnt[(k + 9) % Bins] > 0)
				{
					opp++;
				}
			}
			if (nonEmpty > 0 && opp >= 0.8f * nonEmpty)
			{
				t.mode = FitMode.Sandwich;
				return true;
			}
			mu /= n;
			mv /= n;
			float len = Mathf.Sqrt(mu * mu + mv * mv);
			if (len < 1e-4f)
			{
				return false;
			}
			t.faceN = new Vector3(mu / len, mv / len, 0f);
			t.mode = FitMode.Face;
			return true;
		}

		// After every morph change: the new radius and centre (the along-the-axis part stays). False if the cross-section is too sparse (< 3 bins).
		public static bool Fit(FitTarget t, Vector3[] V, float margin, float[] sx, float[] sy, int[] cnt, out float newR, out Vector3 newC)
		{
			Matrix4x4 m = ToCollider(t);
			int n = t.verts.Length;
			newR = t.r0;
			newC = t.c0;
			if (t.axis < 0)
			{
				float d = 0f;
				for (int i = 0; i < n; i++)
				{
					d += Vector3.Dot(m.MultiplyPoint3x4(V[t.verts[i]]) - t.c0, t.faceN);
				}
				d /= n;
				t.skinMean = d;
				newC = t.c0 + t.faceN * (d + margin - t.r0);
				return true;
			}
			int ua = (t.axis + 1) % 3, va = (t.axis + 2) % 3;
			float cu = C(t.c0, ua), cv = C(t.c0, va);
			if (t.mode == FitMode.Face)
			{
				float d = 0f;
				for (int i = 0; i < n; i++)
				{
					Vector3 p = m.MultiplyPoint3x4(V[t.verts[i]]);
					d += (C(p, ua) - cu) * t.faceN.x + (C(p, va) - cv) * t.faceN.y;
				}
				d /= n;
				t.skinMean = d;
				float shift = d + margin - t.r0;
				newC = SetC(newC, ua, cu + shift * t.faceN.x);
				newC = SetC(newC, va, cv + shift * t.faceN.y);
				return true;
			}
			Array.Clear(sx, 0, Bins);
			Array.Clear(sy, 0, Bins);
			Array.Clear(cnt, 0, Bins);
			for (int i = 0; i < n; i++)
			{
				Vector3 p = m.MultiplyPoint3x4(V[t.verts[i]]);
				float u = C(p, ua) - cu, v = C(p, va) - cv;
				int b = Bin(u, v);
				sx[b] += u;
				sy[b] += v;
				cnt[b]++;
			}
			int nonEmpty = 0;
			float mx = 0f, my = 0f;
			for (int k = 0; k < Bins; k++)
			{
				if (cnt[k] == 0)
				{
					continue;
				}
				sx[k] /= cnt[k];
				sy[k] /= cnt[k];
				mx += sx[k];
				my += sy[k];
				nonEmpty++;
			}
			if (nonEmpty < 3)
			{
				return false;
			}
			mx /= nonEmpty;
			my /= nonEmpty;
			float rr = 0f;
			for (int k = 0; k < Bins; k++)
			{
				if (cnt[k] == 0)
				{
					continue;
				}
				float dx = sx[k] - mx, dy = sy[k] - my;
				rr += Mathf.Sqrt(dx * dx + dy * dy);
			}
			rr /= nonEmpty;
			t.skinMean = rr;
			newR = rr + margin;
			newC = SetC(newC, ua, cu + mx);
			newC = SetC(newC, va, cv + my);
			return true;
		}

		// null when the result is acceptable, otherwise why it is not (shown in the window)
		public static string CheckLimits(FitTarget t, float newR, Vector3 newC)
		{
			if (newR <= 0.0005f)
			{
				return "radius " + (newR * 1000f).ToString("0.0") + " mm";
			}
			if (Mathf.Abs(newR - t.r0) > MaxScaleChange * t.r0)
			{
				return "radius " + (t.r0 * 1000f).ToString("0.0") + " -> " + (newR * 1000f).ToString("0.0") + " mm";
			}
			float move = (newC - t.c0).magnitude;
			if (move > MaxCenterMove)
			{
				return "centre moved " + (move * 1000f).ToString("0.0") + " mm";
			}
			return null;
		}
	}

	// Current skin vertex positions from the non-pose morphs. The mesh's own morphed array is stale in game (the morph thread writes its own
	// protected copy and only copies it back when doSetMorphedMeshVerts is on), and the merged mesh includes pose corrections, so the base
	// mesh's rest vertices are summed with delta * appliedValue over the visible, non-pose morphs here.
	internal sealed class MorphVerts
	{
		public Vector3[] cur;
		public int count;
		public string error;
		public int used, skipped;
		public float lastMs;

		private Vector3[] rest;
		private DAZCharacterRun run;
		private DAZMorphBank bank1, bank2;

		public bool Bound
		{
			get { return cur != null && rest != null && bank1 != null; }
		}

		public void Clear()
		{
			cur = null;
			rest = null;
			run = null;
			bank1 = null;
			bank2 = null;
			count = 0;
			error = null;
		}

		public bool Bind(DAZCharacterSelector sel, DAZSkinV2 skin)
		{
			Clear();
			if (sel == null || skin == null || skin.dazMesh == null)
			{
				error = "no skin mesh";
				return false;
			}
			run = sel.GetComponentInChildren<DAZCharacterRun>(true);
			if (run == null)
			{
				error = "no DAZCharacterRun";
				return false;
			}
			DAZMergedMesh merged = skin.dazMesh as DAZMergedMesh;
			DAZMesh baseMesh = merged != null ? merged.targetMesh : skin.dazMesh;
			if (baseMesh == null)
			{
				error = "no base mesh";
				return false;
			}
			rest = baseMesh.UVVertices;
			count = baseMesh.numBaseVertices;
			if (rest == null || count <= 0 || rest.Length < count)
			{
				error = "base mesh vertices unavailable";
				return false;
			}
			bank1 = run.morphBank1;
			bank2 = run.morphBank1OtherGender;
			if (bank1 == null)
			{
				error = "no morph bank";
				return false;
			}
			cur = new Vector3[count];
			Array.Copy(rest, cur, count);
			return true;
		}

		private bool UseBank2()
		{
			return run != null && run.useOtherGenderMorphs && bank2 != null;
		}

		// Cheap signature of the active non-pose morphs (how many, weighted sum of their values): changes whenever the body shape does.
		public void Sign(out int n, out double h)
		{
			n = 0;
			h = 0.0;
			SignBank(bank1, ref n, ref h);
			if (UseBank2())
			{
				n += 100000;
				SignBank(bank2, ref n, ref h);
			}
		}

		private static void SignBank(DAZMorphBank bank, ref int n, ref double h)
		{
			List<DAZMorph> ms = bank.morphs;
			int c = ms.Count;
			for (int i = 0; i < c; i++)
			{
				DAZMorph m = ms[i];
				float a = m.appliedValue;
				if (a == 0f || !m.visible || m.isPoseControl)
				{
					continue;
				}
				n++;
				h += (double)a * (i + 1) + (double)a * a * 0.37;
			}
		}

		public void Rebuild()
		{
			if (!Bound)
			{
				return;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Array.Copy(rest, cur, count);
			used = 0;
			skipped = 0;
			AddBank(bank1);
			if (UseBank2())
			{
				AddBank(bank2);
			}
			lastMs = (float)sw.Elapsed.TotalMilliseconds;
		}

		private void AddBank(DAZMorphBank bank)
		{
			List<DAZMorph> ms = bank.morphs;
			int c = ms.Count;
			for (int i = 0; i < c; i++)
			{
				DAZMorph m = ms[i];
				float a = m.appliedValue;
				if (a == 0f || !m.visible || m.isPoseControl)
				{
					continue;
				}
				if (m.isRuntime && !m.deltasLoaded)
				{
					m.LoadDeltas();
				}
				DAZMorphVertex[] d = m.deltas;
				if (d == null)
				{
					skipped++;
					continue;
				}
				if (d.Length == 0)
				{
					continue;
				}
				used++;
				for (int k = 0; k < d.Length; k++)
				{
					int v = d[k].vertex;
					if (v >= count)
					{
						continue;
					}
					Vector3 dl = d[k].delta;
					cur[v].x += dl.x * a;
					cur[v].y += dl.y * a;
					cur[v].z += dl.z * a;
				}
			}
		}
	}
}
