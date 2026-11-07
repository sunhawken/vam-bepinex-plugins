using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GPUTools.Physics.Scripts.Behaviours;
using UnityEngine;

namespace ZeroT.ColliderFitHeadless
{
	// One per female Person: binds the fixed colliders to skin vertices, refits them a moment after the non-pose body morphs stop changing, steps
	// aside for anything Collider Editor has adjusted, and puts every value back when switched off, when the atom goes away, or when it is unbound.
	internal sealed class FitHost : MonoBehaviour
	{
		private const float AdvancedSettle = 0.6f;     // after the Use Advanced Colliders switch: let VaM finish swapping the two sets
		private const float SigSec = 0.2f;             // how often the morph signature is computed
		private const float MaxWaitSec = 2f;           // morphs that never stop changing are still refitted this often
		private const float OwnerCheckSec = 1f;        // how often collider values are compared with what was written
		private const float OwnerReadSec = 1f;         // how often Collider Editor's ownership is re-read
		private const float ComTolerance = 0.002f;     // centre of mass drift beyond this (m) is pulled back

		internal sealed class Summary
		{
			public int Total, Following, Owned, Foreign, TooFew, Odd;
			public readonly int[] GroupTotal = new int[FitBinder.GroupCount];
			public readonly int[] GroupFollow = new int[FitBinder.GroupCount];
			public readonly List<string> Exceptions = new List<string>();
		}

		private ColliderFitHeadlessPlugin plugin;
		internal Atom atom;
		private DAZCharacterSelector selector;
		private DAZSkinV2 skin;
		private bool adv;
		private bool bound;
		private readonly List<FitTarget> targets = new List<FitTarget>();
		private readonly CeLink.AtomCe ce = new CeLink.AtomCe();
		private readonly HashSet<string> owned = new HashSet<string>();
		private readonly HashSet<string> scratch = new HashSet<string>();
		private readonly MorphVerts mv = new MorphVerts();
		private readonly float[] sx = new float[FitMath.Bins];
		private readonly float[] sy = new float[FitMath.Bins];
		private readonly int[] cnt = new int[FitMath.Bins];

		private readonly List<Rigidbody> bodies = new List<Rigidbody>();
		private readonly List<Vector3> comBase = new List<Vector3>();
		private readonly List<bool> comAuto = new List<bool>();
		private readonly List<bool> comFixed = new List<bool>();

		private float readyAt, dirtyFirst, dirtyLast, nextOwn, nextRead, nextSig, comUntil, nextCom;
		private int sigN;
		private double sigH;
		private bool dirty;

		internal string StateText = "Waiting for the scene to settle";
		internal string BindError;
		internal float BindMs, RefitMs;
		internal int Refits, Writes, ComFixes;

		internal bool IsBound
		{
			get { return bound; }
		}

		internal bool AdvancedSet
		{
			get { return adv; }
		}

		internal int MorphsSkipped
		{
			get { return mv.skipped; }
		}

		internal string CeStatusText
		{
			get { return ce.status == CeLink.Readable ? "scene Collider Editor found" : (ce.status == CeLink.Unreadable ? "scene Collider Editor unreadable" : "no scene Collider Editor"); }
		}

		internal void Init(ColliderFitHeadlessPlugin owner, Atom target)
		{
			plugin = owner;
			atom = target;
			readyAt = Time.realtimeSinceStartup + plugin.SettleSeconds;
		}

		// ---------------------------------------------------------------- requests from the window

		internal void MarkDirty()
		{
			if (!bound)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (!dirty)
			{
				dirty = true;
				dirtyFirst = now;
			}
			dirtyLast = now;
		}

		internal void RefitNow()
		{
			if (bound)
			{
				Refit();
			}
			else
			{
				readyAt = 0f;
			}
		}

		internal void Rebind()
		{
			Unbind();
			readyAt = Time.realtimeSinceStartup + 0.2f;
			ResetCe();
		}

		private void ResetCe()
		{
			ce.Reset();
			owned.Clear();
		}

		// ---------------------------------------------------------------- per frame

		private void Update()
		{
			if (plugin == null || atom == null)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				readyAt = now + plugin.SettleSeconds;
				if (bound)
				{
					Unbind();      // the scene is about to replace the colliders: put them back first
				}
				return;
			}
			if (!atom.on)
			{
				if (bound)
				{
					Unbind();
				}
				SetState("Atom is switched off");
				return;
			}
			if (selector == null)
			{
				selector = atom.GetComponentInChildren<DAZCharacterSelector>(true);
			}
			if (selector == null)
			{
				return;
			}
			DAZCharacter ch = selector.selectedCharacter;
			DAZSkinV2 s = ch != null ? ch.skin : null;
			bool a = selector.useAdvancedColliders;
			if (s != skin || a != adv)
			{
				bool was = bound;
				if (was)
				{
					Unbind();
				}
				readyAt = now + (s != skin ? plugin.SettleSeconds : AdvancedSettle);
				skin = s;
				adv = a;
				if (!was)
				{
					BindError = null;
				}
			}
			if (!bound)
			{
				if (skin == null || now < readyAt)
				{
					SetState("Waiting for the scene to settle");
					return;
				}
				TryBind(now);
				return;
			}
			if (now >= nextSig)
			{
				nextSig = now + SigSec;
				int n;
				double h;
				mv.Sign(out n, out h);
				if (n != sigN || h != sigH)
				{
					sigN = n;
					sigH = h;
					MarkDirty();
				}
			}
			if (dirty && (now - dirtyLast >= plugin.QuietSeconds || now - dirtyFirst >= MaxWaitSec))
			{
				dirty = false;
				Refit();
			}
			if (now >= nextOwn)
			{
				nextOwn = now + OwnerCheckSec;
				OwnershipCheck();
			}
			if (now >= nextRead)
			{
				nextRead = now + OwnerReadSec;
				ReadOwners();
			}
			if (now < comUntil && now >= nextCom)
			{
				nextCom = now + 0.25f;
				CheckCom();
			}
		}

		private void SetState(string s)
		{
			StateText = s;
		}

		// ---------------------------------------------------------------- bind / unbind

		// the colliders Collider Editor (scene plugin or headless preset) owns on this atom; true when that changed
		private bool ReadOwners()
		{
			plugin.Ce.Poll();
			bool sceneChanged = ce.Read(atom);
			scratch.Clear();
			plugin.Ce.AddHeadless(scratch);
			foreach (string k in ce.owned)
			{
				scratch.Add(k);
			}
			bool changed = sceneChanged || !scratch.SetEquals(owned);
			if (!scratch.SetEquals(owned))
			{
				owned.Clear();
				foreach (string k in scratch)
				{
					owned.Add(k);
				}
			}
			if (changed && bound)
			{
				ApplyOwned();
				if (NeedRebind())
				{
					Unbind();
					readyAt = Time.realtimeSinceStartup + 0.2f;
				}
			}
			return changed;
		}

		private void TryBind(float now)
		{
			Stopwatch sw = Stopwatch.StartNew();
			ReadOwners();
			List<FitTarget> list;
			string err = null;
			try
			{
				if (!mv.Bind(selector, skin))
				{
					throw new Exception(mv.error);
				}
				mv.Rebuild();
				list = FitBinder.Collect(skin, owned);
				err = FitBinder.Assign(list, skin, mv, sx, sy, cnt);
			}
			catch (Exception e)
			{
				list = null;
				err = e.Message;
			}
			if (err != null || list == null)
			{
				if (BindError != (err ?? "unknown"))
				{
					plugin.Log(atom.uid + ": cannot bind the colliders yet: " + (err ?? "unknown") + " (trying again)");
				}
				BindError = err ?? "unknown";
				readyAt = now + 2f;     // try again in two seconds
				SetState("Cannot bind colliders");
				return;
			}
			BindError = null;
			targets.Clear();
			targets.AddRange(list);
			bodies.Clear();
			comBase.Clear();
			comAuto.Clear();
			comFixed.Clear();
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				if (t.body != null && !bodies.Contains(t.body))
				{
					bodies.Add(t.body);
					comBase.Add(t.body.centerOfMass);
					comAuto.Add(ProbeAutoCom(t.body));
					comFixed.Add(false);
				}
			}
			bound = true;
			dirty = false;
			mv.Sign(out sigN, out sigH);
			nextSig = now + SigSec;
			nextOwn = now + OwnerCheckSec;
			nextRead = now + OwnerReadSec;
			BindMs = (float)sw.Elapsed.TotalMilliseconds;
			Refit();
			plugin.Log(atom.uid + ": " + CountState(FitState.Following) + " of " + targets.Count + " fixed colliders fitted to the skin (" + (adv ? "advanced" : "standard") + " set, " + BindMs.ToString("0") + " ms)");
		}

		private void Unbind()
		{
			if (targets.Count > 0)
			{
				int back = 0;
				for (int i = 0; i < targets.Count; i++)
				{
					if (targets[i].wrote)
					{
						back++;
					}
					Release(targets[i]);
				}
				if (back > 0 && plugin != null && atom != null)
				{
					plugin.Log(atom.uid + ": " + back + " colliders put back to their original size");
				}
				RestoreCom();      // colliders first, then the centre of mass
				ce.RefreshPreview();
			}
			targets.Clear();
			bodies.Clear();
			comBase.Clear();
			comAuto.Clear();
			comFixed.Clear();
			mv.Clear();
			bound = false;
			dirty = false;
		}

		private int CountState(int state)
		{
			int n = 0;
			for (int i = 0; i < targets.Count; i++)
			{
				if (targets[i].state == state)
				{
					n++;
				}
			}
			return n;
		}

		// ---------------------------------------------------------------- fitting

		private void Refit()
		{
			if (!bound || !mv.Bound)
			{
				return;
			}
			Stopwatch sw = Stopwatch.StartNew();
			mv.Rebuild();
			Vector3[] V = mv.cur;
			float margin = plugin.MarginMm * 0.001f;
			int wrote = 0;
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				if (t.state == FitState.Owned || t.state == FitState.Foreign || t.state == FitState.TooFew)
				{
					continue;
				}
				float r;
				Vector3 c;
				if (!FitMath.Fit(t, V, margin, sx, sy, cnt, out r, out c))
				{
					t.state = FitState.Anomaly;
					t.note = "sparse cross-section";
					continue;
				}
				string bad = FitMath.CheckLimits(t, r, c);
				if (bad != null)
				{
					t.state = FitState.Anomaly;
					t.note = bad;
					continue;
				}
				if (Write(t, r, c))
				{
					wrote++;
				}
				t.state = FitState.Following;
				t.note = null;
			}
			RefitMs = (float)sw.Elapsed.TotalMilliseconds;
			Refits++;
			Writes += wrote;
			if (wrote > 0)
			{
				float now = Time.realtimeSinceStartup;
				comUntil = now + 2f;
				nextCom = now + 0.05f;
				ce.RefreshPreview();
			}
			SetState("Fitting colliders to the skin");
		}

		// writes radius / centre / height; remembers what it wrote (read back) so other writers can be told apart. True if anything changed.
		private bool Write(FitTarget t, float r, Vector3 c)
		{
			bool changed = false;
			if (t.cap != null)
			{
				CapsuleCollider cap = t.cap;
				if (Mathf.Abs(r - t.lastR) > 1e-5f)
				{
					cap.radius = r;
					t.lastR = cap.radius;
					changed = true;
				}
				if ((c - t.lastC).sqrMagnitude > 1e-10f)
				{
					cap.center = c;
					t.lastC = cap.center;
					changed = true;
				}
				float wantH = 2f * r > t.h0 ? 2f * r : t.h0;    // the length stays; only a diameter beyond it lengthens the capsule (else Unity treats it as a sphere)
				if (Mathf.Abs(wantH - t.lastH) > 1e-5f)
				{
					cap.height = wantH;
					t.lastH = cap.height;
					changed = true;
				}
				if (changed && t.gpu != null)
				{
					t.gpu.UpdateData();
				}
			}
			else if (t.sph != null)
			{
				if ((c - t.lastC).sqrMagnitude > 1e-10f)
				{
					t.sph.center = c;
					t.lastC = t.sph.center;
					changed = true;
				}
			}
			if (changed)
			{
				t.wrote = true;
			}
			return changed;
		}

		// puts back what was written, but only the values that are still the ones written (anything else belongs to someone else now)
		private static void Release(FitTarget t)
		{
			if (!t.wrote || t.col == null)
			{
				t.wrote = false;
				return;
			}
			bool changed = false;
			if (t.cap != null)
			{
				if (t.cap.radius == t.lastR && t.lastR != t.r0)
				{
					t.cap.radius = t.r0;
					changed = true;
				}
				if (t.cap.center == t.lastC && t.lastC != t.c0)
				{
					t.cap.center = t.c0;
					changed = true;
				}
				if (t.cap.height == t.lastH && t.lastH != t.h0)
				{
					t.cap.height = t.h0;
					changed = true;
				}
				if (changed && t.gpu != null)
				{
					t.gpu.UpdateData();
				}
			}
			else if (t.sph != null && t.sph.center == t.lastC)
			{
				t.sph.center = t.c0;
			}
			t.wrote = false;
			t.lastR = t.r0;
			t.lastH = t.h0;
			t.lastC = t.c0;
		}

		// ---------------------------------------------------------------- centre of mass

		// Unity recomputes the centre of mass of a rigidbody whose centre was never set explicitly whenever a collider changes; pull it back to
		// where VaM had it (drift beyond 2 mm), and remember to hand it back at the end.
		private void CheckCom()
		{
			for (int i = 0; i < bodies.Count; i++)
			{
				Rigidbody rb = bodies[i];
				if (rb == null)
				{
					continue;
				}
				if ((rb.centerOfMass - comBase[i]).magnitude <= ComTolerance)
				{
					continue;
				}
				rb.centerOfMass = comBase[i];
				comFixed[i] = true;
				ComFixes++;
			}
		}

		// Unity cannot say whether the centre was set explicitly, so recompute once and compare: unchanged = automatic; changed = explicit, put back.
		private static bool ProbeAutoCom(Rigidbody rb)
		{
			Vector3 before = rb.centerOfMass;
			rb.ResetCenterOfMass();
			if ((rb.centerOfMass - before).sqrMagnitude <= 1e-10f)
			{
				return true;
			}
			rb.centerOfMass = before;
			return false;
		}

		private void RestoreCom()
		{
			for (int i = 0; i < bodies.Count && i < comFixed.Count; i++)
			{
				if (!comFixed[i])
				{
					continue;
				}
				comFixed[i] = false;
				Rigidbody rb = bodies[i];
				if (rb == null)
				{
					continue;
				}
				if (comAuto[i])
				{
					rb.ResetCenterOfMass();
				}
				else
				{
					rb.centerOfMass = comBase[i];
				}
			}
		}

		// ---------------------------------------------------------------- who is in charge of a collider

		private bool ValuesChanged(FitTarget t)
		{
			if (t.col == null)
			{
				return false;
			}
			if (t.cap != null)
			{
				return t.cap.radius != t.lastR || t.cap.center != t.lastC || t.cap.height != t.lastH;
			}
			return t.sph != null && t.sph.center != t.lastC;
		}

		private static void Snapshot(FitTarget t)
		{
			if (t.cap != null)
			{
				t.lastR = t.cap.radius;
				t.lastC = t.cap.center;
				t.lastH = t.cap.height;
			}
			else if (t.sph != null)
			{
				t.lastC = t.sph.center;
			}
		}

		// Something else changed the collider after this plugin wrote it. Hand back only the values that are still ours.
		private static void Drop(FitTarget t)
		{
			Release(t);
			Snapshot(t);
		}

		private void OwnershipCheck()
		{
			bool foreign = false, moved = false;
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				if (t.state == FitState.Following || t.state == FitState.Anomaly)
				{
					if (t.wrote && ValuesChanged(t))
					{
						Drop(t);
						t.state = FitState.Foreign;
						t.note = "changed by something else";
						foreign = true;
					}
				}
				else if (t.state == FitState.Foreign || t.state == FitState.Owned)
				{
					if (ValuesChanged(t))
					{
						moved = true;
					}
				}
			}
			if (!foreign && !moved)
			{
				return;
			}
			ReadOwners();
			ApplyOwned();
			if (NeedRebind())
			{
				Unbind();
				readyAt = Time.realtimeSinceStartup + 0.2f;
			}
		}

		private void ApplyOwned()
		{
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				if (owned.Contains(t.uuid) && t.state != FitState.Owned && t.state != FitState.TooFew)
				{
					if (t.wrote)
					{
						Drop(t);
					}
					else
					{
						Snapshot(t);
					}
					t.state = FitState.Owned;
					t.note = null;
				}
			}
		}

		// a collider that was Collider Editor's no longer is (it was reset there): bind again so it is taken over
		private bool NeedRebind()
		{
			if (ce.status == CeLink.Unreadable)
			{
				return false;
			}
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				if (t.state == FitState.Owned && !owned.Contains(t.uuid))
				{
					return true;
				}
			}
			return false;
		}

		// ---------------------------------------------------------------- numbers for the window

		internal void Summarize(Summary s)
		{
			s.Total = targets.Count;
			s.Following = s.Owned = s.Foreign = s.TooFew = s.Odd = 0;
			s.Exceptions.Clear();
			for (int g = 0; g < FitBinder.GroupCount; g++)
			{
				s.GroupTotal[g] = 0;
				s.GroupFollow[g] = 0;
			}
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				s.GroupTotal[t.group]++;
				switch (t.state)
				{
					case FitState.Following:
						s.Following++;
						s.GroupFollow[t.group]++;
						break;
					case FitState.Owned:
						s.Owned++;
						break;
					case FitState.Foreign:
						s.Foreign++;
						break;
					case FitState.TooFew:
						s.TooFew++;
						break;
					case FitState.Anomaly:
						s.Odd++;
						break;
				}
				if (t.state != FitState.Following && s.Exceptions.Count < 6)
				{
					s.Exceptions.Add(t.label + ": " + FitState.Label(t.state) + (string.IsNullOrEmpty(t.note) ? "" : " (" + t.note + ")"));
				}
			}
		}

		// the whole state, for the log line behind the "Dump" button
		internal string Dump()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(atom.uid).Append(": bound=").Append(bound).Append(" adv=").Append(adv).Append(" targets=").Append(targets.Count)
				.Append(" bindMs=").Append(BindMs.ToString("0.0")).Append(" refitMs=").Append(RefitMs.ToString("0.0")).Append(" morphs=").Append(mv.used).Append("/skipped ").Append(mv.skipped)
				.Append(" refits=").Append(Refits).Append(" writes=").Append(Writes).Append(" comFixes=").Append(ComFixes).Append(" owned=").Append(owned.Count).Append('\n');
			for (int i = 0; i < targets.Count; i++)
			{
				FitTarget t = targets[i];
				sb.Append(i).Append(' ').Append(t.label).Append(" | ").Append(FitState.Label(t.state)).Append(' ').Append(FitMode.Label(t.mode))
					.Append(" v=").Append(t.usedVerts).Append(" r0=").Append((t.r0 * 1000f).ToString("0.00")).Append(" r=").Append((t.cap != null ? t.cap.radius * 1000f : 0f).ToString("0.00"))
					.Append(" skin=").Append((t.skinMean * 1000f).ToString("0.00"));
				if (!string.IsNullOrEmpty(t.note))
				{
					sb.Append(" note=").Append(t.note);
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		// ---------------------------------------------------------------- lifecycle

		private void OnDisable()
		{
			try
			{
				Unbind();
			}
			catch (Exception e)
			{
				ColliderFitHeadlessPlugin.Warn("OnDisable: " + e.Message);
			}
		}

		private void OnDestroy()
		{
			try
			{
				Unbind();
			}
			catch (Exception e)
			{
				ColliderFitHeadlessPlugin.Warn("OnDestroy: " + e.Message);
			}
		}
	}
}
