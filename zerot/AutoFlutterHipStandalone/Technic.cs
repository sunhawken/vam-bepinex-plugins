using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.AutoFlutterHipStandalone
{
	// ------------------------------------------------------------------ data (AutoFlutterHipTechnicUI.initHead / initSlot)

	public sealed class TechnicSlot
	{
		public float Repetition = 1f, Length = 2f, SyncOffset = 0f;
		public string Rot1Axis = "None", Move1Axis = "None", Rot2Axis = "None", Move2Axis = "None";
		public float Rot1 = 0.3f, Rot1Offset = 0f, Move1 = 0.3f, Move1Offset = 0f;
		public bool Rot2Radial;
		public float Rot2 = 0.3f, Rot2Offset = 0f, Move2 = 0.3f, Move2Offset = 0f;
		public bool Rot1Lock, Move1Lock, Rot2Lock, Move2Lock;   // the original calls the last two "2st ..."
		public bool Touched;
		public string Title = "";

		static float F(JSONNode n, float def) { return n == null ? def : n.AsFloat; }
		static bool B(JSONNode n, bool def) { return n == null ? def : n.AsBool; }
		static string S(JSONNode n, string def) { return n == null ? def : n.Value; }

		// json2slotdata
		public static TechnicSlot FromJson(JSONClass j)
		{
			TechnicSlot s = new TechnicSlot();
			s.Repetition = F(j["Repetition"], 1f);
			s.Length = F(j["Length (sec)"], 2f);
			s.SyncOffset = F(j["Sync Offset"], 0f);
			s.Rot1Axis = S(j["1st Rot, Axis"], "None");
			s.Move1Axis = S(j["1st Move, Axis"], "None");
			s.Rot2Axis = S(j["2nd Rot, Axis"], "None");
			s.Move2Axis = S(j["2nd Move, Axis"], "None");
			s.Rot1 = F(j["1st Rotation"], 0.3f);
			s.Rot1Offset = F(j["1st Rot, Offset"], 0f);
			s.Move1 = F(j["1st Movement"], 0.3f);
			s.Move1Offset = F(j["1st Move, Offset"], 0f);
			s.Rot2Radial = B(j["2nd Rot, with Radial"], false);
			s.Rot2 = F(j["2nd Rotation"], 0.3f);
			s.Rot2Offset = F(j["2nd Rot, Offset"], 0f);
			s.Move2 = F(j["2nd Movement"], 0.3f);
			s.Move2Offset = F(j["2nd Move, Offset"], 0f);
			s.Rot1Lock = B(j["1st Rot, Lock"], false);
			s.Move1Lock = B(j["1st Move, Lock"], false);
			s.Rot2Lock = B(j["2st Rot, Lock"], false);
			s.Move2Lock = B(j["2st Move, Lock"], false);
			s.Touched = B(j["isTourchedSlot"], false);
			return s;
		}

		// slot2json
		public JSONClass ToJson()
		{
			JSONClass j = new JSONClass();
			j["Repetition"].AsFloat = Repetition;
			j["Length (sec)"].AsFloat = Length;
			j["Sync Offset"].AsFloat = SyncOffset;
			j["1st Rot, Axis"] = Rot1Axis;
			j["1st Move, Axis"] = Move1Axis;
			j["2nd Rot, Axis"] = Rot2Axis;
			j["2nd Move, Axis"] = Move2Axis;
			j["1st Rotation"].AsFloat = Rot1;
			j["1st Rot, Offset"].AsFloat = Rot1Offset;
			j["1st Movement"].AsFloat = Move1;
			j["1st Move, Offset"].AsFloat = Move1Offset;
			j["2nd Rot, with Radial"].AsBool = Rot2Radial;
			j["2nd Rotation"].AsFloat = Rot2;
			j["2nd Rot, Offset"].AsFloat = Rot2Offset;
			j["2nd Movement"].AsFloat = Move2;
			j["2nd Move, Offset"].AsFloat = Move2Offset;
			j["1st Rot, Lock"].AsBool = Rot1Lock;
			j["1st Move, Lock"].AsBool = Move1Lock;
			j["2st Rot, Lock"].AsBool = Rot2Lock;
			j["2st Move, Lock"].AsBool = Move2Lock;
			j["isTourchedSlot"].AsBool = Touched;
			return j;
		}
	}

	public sealed class TechnicHead
	{
		public string Name = "Unknown";
		public string Image = "";
		public float Repetition = 1f, Speed = 1f;
		public bool Endless, Smooth, Sync;
		public readonly List<TechnicSlot> Slots = new List<TechnicSlot>();

		// json2headdata + the slotlist (loaddata)
		public static TechnicHead FromJson(JSONClass j)
		{
			TechnicHead h = new TechnicHead();
			h.Name = j["name"] == null ? "" : j["name"].Value;
			h.Image = j["image"] == null ? "" : j["image"].Value;
			h.Repetition = j["Repetition"] == null ? 1f : j["Repetition"].AsFloat;
			h.Speed = j["Speed x sec"] == null ? 1f : j["Speed x sec"].AsFloat;
			h.Endless = j["Endless Technic"] == null ? false : j["Endless Technic"].AsBool;
			h.Smooth = j["Smooth Transition"] == null ? false : j["Smooth Transition"].AsBool;
			h.Sync = j["Sync Other Atoms"] == null ? false : j["Sync Other Atoms"].AsBool;
			JSONArray arr = j["slotlist"] as JSONArray;
			if (arr != null)
			{
				for (int i = 0; i < arr.Count; i++)
				{
					TechnicSlot s = TechnicSlot.FromJson(arr[i].AsObject);
					s.Title = "Slot " + (i + 1);
					h.Slots.Add(s);
				}
			}
			return h;
		}

		// head2json + savedata
		public JSONClass ToJson()
		{
			JSONClass j = new JSONClass();
			j["type"] = "Technic";
			j["name"] = Name;
			j["image"] = Image ?? "";
			j["Repetition"].AsFloat = Repetition;
			j["Speed x sec"].AsFloat = Speed;
			j["Endless Technic"].AsBool = Endless;
			j["Smooth Transition"].AsBool = Smooth;
			j["Sync Other Atoms"].AsBool = Sync;
			JSONArray arr = new JSONArray();
			for (int i = 0; i < Slots.Count; i++)
			{
				arr.Add(Slots[i].ToJson());
			}
			j.Add("slotlist", arr);
			return j;
		}

		public float TotalSeconds()
		{
			float t = 0f;
			for (int i = 0; i < Slots.Count; i++)
			{
				t += Slots[i].Repetition * Slots[i].Length;
			}
			return t * (Endless ? 1f : Repetition);
		}
	}

	// ------------------------------------------------------------------ UtilRigidbodyForce: the force player

	public delegate IEnumerator SyncFn(string type, string head, int slot, long time, int span);

	public sealed class TechnicPlayer
	{
		public static Func<long> Ticks = delegate { return DateTime.Now.Ticks; };   // the .var reads the wall clock; the simulator swaps this
		public static bool Debug;

		private readonly Atom atom;
		private readonly Rigidbody body;
		private readonly Action<string> log;
		public bool islooping;
		private int instanceN;
		public long poll_time;
		private long temp_time;
		private long repeatcnt;
		private float angle;
		private readonly float r_val = 0f;
		private readonly SizeQueue smoothTorque = new SizeQueue(4), smoothForces = new SizeQueue(4);
		private Fader smoothfadeout = new Fader(1000);
		public string NowPlaying = "";
		public int LastSlotIndex = -1;
		public SyncFn Sync;   // (type, headName, slot, pollTime, spantree)
		public Action<IEnumerator> StartCo;                                // how the .var started it: not waited for

		public TechnicPlayer(Atom atom, Rigidbody hip, Action<string> log)
		{
			this.atom = atom;
			this.body = hip;
			this.log = log;
		}

		public Rigidbody Body { get { return body; } }

		// the original's BezierFader with its default (linear) table: a 0.001 -> 1 ramp over `duration` milliseconds
		private sealed class Fader
		{
			private float fade_val = 1f;
			private long fade_stt;
			private readonly float duration;
			private static float[] table;

			public Fader(int duration)
			{
				this.duration = duration;
				if (table == null)
				{
					table = new float[101];
					table[0] = 0.001f;
					for (int i = 1; i < 101; i++)
					{
						table[i] = i / 100f;
					}
				}
			}

			public void run() { fade_val = 0f; fade_stt = Ticks() / 10000; }
			public void end() { fade_val = 1f; fade_stt = 0; }

			private bool ing()
			{
				if (1f <= fade_val)
				{
					fade_val = 1f;
				}
				if (fade_val != 1f)
				{
					return true;
				}
				return fade_stt != 0;
			}

			public float get()
			{
				if (!ing())
				{
					return table[table.Length - 1];
				}
				long mil = Ticks() / 10000;
				float drawalpha;
				if (fade_val == 0f)
				{
					fade_stt = mil;
				}
				long gap = mil - fade_stt;
				if (duration < gap)
				{
					fade_stt = 0;
					fade_val = 1f;
					drawalpha = 1f;
				}
				else
				{
					int len = table.Length;
					int pos = (int)(len * (gap / duration));
					float val = pos == len ? 1f : table[pos % len];
					val = Mathf.Max(val, 0.001f);
					fade_val = val;
					drawalpha = fade_val;
				}
				return Mathf.Min(drawalpha, 1f);
			}
		}

		private sealed class SizeQueue
		{
			private readonly int size;
			private readonly List<Vector3> list = new List<Vector3>();
			public SizeQueue(int size) { this.size = size; }
			public void add(Vector3 v) { if (size <= list.Count) { list.RemoveAt(0); } list.Add(v); }
			public Vector3 get()
			{
				Vector3 v = Vector3.zero;
				if (list.Count == 0)
				{
					return v;
				}
				for (int i = 0; i < list.Count; i++)
				{
					v += list[i];
				}
				return v / (float)list.Count;
			}
		}

		// ---- axes
		public static int Axis(string axes)
		{
			switch (axes)
			{
				case "+X": return 1;
				case "+Y": return 2;
				case "+Z": return 3;
				case "-X": return 4;
				case "-Y": return 5;
				case "-Z": return 6;
				default: return 0;
			}
		}

		private static Vector3 Direction(int type, Rigidbody body)
		{
			switch (type)
			{
				case 1: return +1 * body.transform.right;
				case 2: return +1 * body.transform.up;
				case 3: return +1 * body.transform.forward;
				case 4: return -1 * body.transform.right;
				case 5: return -1 * body.transform.up;
				case 6: return -1 * body.transform.forward;
				default: return Vector3.zero;
			}
		}

		private static Vector3 Direction(int type, Rigidbody body, bool radial, Vector3 axes)
		{
			if (!radial)
			{
				return Direction(type, body);
			}
			switch (type)
			{
				case 1: return Vector3.Cross(axes, +1 * body.transform.right).normalized;
				case 2: return Vector3.Cross(axes, +1 * body.transform.up).normalized;
				case 3: return Vector3.Cross(axes, +1 * body.transform.forward).normalized;
				case 4: return Vector3.Cross(axes, -1 * body.transform.right).normalized;
				case 5: return Vector3.Cross(axes, -1 * body.transform.up).normalized;
				case 6: return Vector3.Cross(axes, -1 * body.transform.forward).normalized;
				default: return Vector3.zero;
			}
		}

		// generate(): torque and force of one fixed step for a slot at the current angle
		public Vector3[] Generate(TechnicSlot slot)
		{
			float synchronized_offset = slot.SyncOffset;
			int one_rotation_axetpe = Axis(slot.Rot1Axis), one_movement_axetpe = Axis(slot.Move1Axis);
			int two_rotation_axetpe = Axis(slot.Rot2Axis), two_movement_axetpe = Axis(slot.Move2Axis);

			Vector3 one_rotation_direct = Direction(one_rotation_axetpe, body);
			Vector3 one_movement_direct = Direction(one_movement_axetpe, body);
			Vector3 two_rotation_direct = Direction(two_rotation_axetpe, body, slot.Rot2Radial, one_rotation_direct);
			Vector3 two_movement_direct = Direction(two_movement_axetpe, body);

			float d2r = Mathf.Deg2Rad;
			float one_rotation_circular_sin = Mathf.Sin(synchronized_offset * 360f * d2r + slot.Rot1Offset * 360f * d2r + (slot.Rot1Lock ? 0 : angle));
			float one_movement_circular_sin = Mathf.Sin(synchronized_offset * 360f * d2r + slot.Move1Offset * 360f * d2r + (slot.Move1Lock ? 0 : angle));
			float two_rotation_circular_cos = Mathf.Cos(synchronized_offset * 360f * d2r + slot.Rot2Offset * 360f * d2r + (slot.Rot2Lock ? 0 : angle));
			float two_movement_circular_cos = Mathf.Cos(synchronized_offset * 360f * d2r + slot.Move2Offset * 360f * d2r + (slot.Move2Lock ? 0 : angle));

			Vector3 one_rotation_circular_vec = one_rotation_circular_sin * one_rotation_direct * slot.Rot1 * 80f;
			Vector3 one_movement_circular_vec = one_movement_circular_sin * one_movement_direct * slot.Move1 * 800f;
			Vector3 two_rotation_circular_vec = two_rotation_circular_cos * two_rotation_direct * slot.Rot2 * 80f;
			Vector3 two_movement_circular_vec = two_movement_circular_cos * two_movement_direct * slot.Move2 * 800f;

			Vector3 torque = (one_rotation_circular_vec + two_rotation_circular_vec) * Time.fixedDeltaTime;
			Vector3 forces = (one_movement_circular_vec + two_movement_circular_vec) * Time.fixedDeltaTime;
			return new Vector3[] { torque, forces };
		}

		public IEnumerator StopWait()
		{
			islooping = false;
			yield return new WaitForFixedUpdate();
		}

		// rate(): the progress 0..1 of the current repetition; ends a repetition and starts the blend-out of the last one
		private float Rate(long now, long loop_time, long repeatnum, float speed_mutipy)
		{
			long cut_time = (long)((float)loop_time / speed_mutipy);
			long dif_time = now - temp_time;
			if (cut_time < dif_time)
			{
				repeatcnt += 1;
				temp_time = now;
				if (repeatnum <= repeatcnt + 1)
				{
					smoothfadeout.end();
					smoothfadeout = new Fader((int)(loop_time) / 10000);
					smoothfadeout.run();
				}
			}
			return (float)(dif_time % cut_time) / (float)cut_time;
		}

		public IEnumerator Run(TechnicHead head, List<TechnicSlot> slotlist, int slot_num, long pollTime, int spantree)
		{
			for (int i = 0; i < 8; i++)
			{
				yield return new WaitForFixedUpdate();
				if (instanceN == 0)
				{
					break;
				}
			}
			if (islooping)
			{
				if (Debug) { log(atom.uid + ": '" + head.Name + "' did not start: another technic is running"); }
				yield break;
			}
			instanceN++;
			islooping = true;
			poll_time = pollTime;

			if (slot_num != -1)
			{
				if (slot_num < 0 || slot_num >= slotlist.Count)
				{
					instanceN--;
					islooping = false;
					yield break;
				}
				slotlist = new List<TechnicSlot> { slotlist[slot_num] };
			}
			NowPlaying = head.Name;
			if (Debug) { log(atom.uid + ": playing '" + head.Name + "', " + slotlist.Count + " slot(s)"); }

			float repeatof_all = head.Repetition, speed_mutipy = head.Speed;
			bool endless_move = head.Endless, smooth_trans = head.Smooth, sync_another = head.Sync;

			for (int i = 0; endless_move || i < (int)repeatof_all; i++)
			{
				if (!islooping)
				{
					break;
				}
				bool hasnextloop = endless_move || i + 1 < (int)repeatof_all;
				for (int j = 0; j < slotlist.Count; j++)
				{
					TechnicSlot slot = slotlist[j % slotlist.Count];
					TechnicSlot next = hasnextloop || (j + 1) < slotlist.Count ? slotlist[(j + 1) % slotlist.Count] : null;
					repeatcnt = 0;
					temp_time = Ticks() - poll_time;
					LastSlotIndex = j;
					if (!islooping)
					{
						break;
					}
					if (sync_another && spantree == 0 && Sync != null && StartCo != null)
					{
						StartCo(Sync("sync", head.Name, slot_num, poll_time, spantree));
					}
					for (int k = 0; ; k++)
					{
						if (!islooping)
						{
							break;
						}
						int loop_repetition_num = (int)slot.Repetition;
						long loop_time_len_ticks = (long)(Mathf.Max(slot.Length, 0.01f) * 10000000f);

						Vector3[] curval = Generate(slot);
						Vector3 torque = curval[0];
						Vector3 forces = curval[1];
						Vector3 random = UnityEngine.Random.insideUnitSphere * r_val;

						if (smooth_trans && loop_repetition_num <= repeatcnt + 1)
						{   // last repetition of the slot
							if (next == null)
							{
								body.AddTorque(torque + random, ForceMode.Impulse);
								body.AddForce(forces + random, ForceMode.Impulse);
							}
							else
							{   // blending into the next slot
								Vector3[] nxtval = Generate(next);
								float fo = smoothfadeout.get();
								torque = curval[0] * (1f - fo) + nxtval[0] * fo;
								forces = curval[1] * (1f - fo) + nxtval[1] * fo;
								smoothTorque.add(torque);
								smoothForces.add(forces);
								body.AddTorque(torque + random, ForceMode.Impulse);
								body.AddForce(forces + random, ForceMode.Impulse);
							}
						}
						else if (smooth_trans)
						{
							smoothTorque.add(torque);
							smoothForces.add(forces);
							body.AddTorque(smoothTorque.get() + random, ForceMode.Impulse);
							body.AddForce(smoothForces.get() + random, ForceMode.Impulse);
						}
						else
						{
							body.AddTorque(torque + random, ForceMode.Impulse);
							body.AddForce(forces + random, ForceMode.Impulse);
						}

						if (!SuperController.singleton.freezeAnimation)
						{
							float rate_val = Rate(Ticks() - poll_time, loop_time_len_ticks, loop_repetition_num, Mathf.Max(speed_mutipy, 0.01f));
							angle = 360f * Mathf.Min(rate_val, 1f) * Mathf.Deg2Rad;
						}

						yield return new WaitForFixedUpdate();

						if (!islooping || loop_repetition_num <= repeatcnt)
						{
							break;
						}
						// realtime apply control
						repeatof_all = head.Repetition;
						speed_mutipy = head.Speed;
						endless_move = head.Endless;
						smooth_trans = head.Smooth;
						sync_another = head.Sync;
					}
					if (!islooping)
					{
						break;
					}
				}
				if (!islooping)
				{
					break;
				}
				repeatof_all = head.Repetition;
				speed_mutipy = head.Speed;
				endless_move = head.Endless;
				smooth_trans = head.Smooth;
				sync_another = head.Sync;
			}
			instanceN--;
			islooping = false;
			NowPlaying = "";
			if (Debug) { log(atom.uid + ": '" + head.Name + "' finished"); }
		}
	}
}
