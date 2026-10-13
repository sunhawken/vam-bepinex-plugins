using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.ForceRodSync
{
	// Makes ForceForge (ParticlePinnacle.ForceForge, v8 script) go after whatever DiviningRod (ToumeiHitsuji.DiviningRod) is aiming at.
	// Both plugins sit on the same male. DiviningRod keeps its pick in a protected field (_penisTarget, with _targetType); every
	// few moments this reads it and sets ForceForge's "target to move to" (the person) and "Mode" (which body part) to match, so
	// the pick follows the rod as it re-targets. When the rod has no target, ForceForge is left alone.
	// Nothing of either plugin is modified or replaced.
	[BepInPlugin("com.zerot.forcerodsync", "Force Rod Sync", "1.0.0")]
	public sealed class ForceRodSyncPlugin : BaseUnityPlugin
	{
		private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private sealed class Pair
		{
			public Atom atom;
			public JSONStorable rod;
			public JSONStorable force;
			public FieldInfo fTarget, fType, fMode;
		}

		private ConfigEntry<bool> cfgEnabled;
		private ConfigEntry<float> cfgInterval;
		private ConfigEntry<bool> cfgMode;
		private ConfigEntry<bool> cfgStopPdp;
		private readonly List<Pair> pairs = new List<Pair>();
		private float nextScan, nextTick;

		private void Awake()
		{
			cfgEnabled = Config.Bind<bool>("Sync", "Enabled", true, "ForceForge follows the target DiviningRod picks (person and body part).");
			cfgInterval = Config.Bind<float>("Sync", "Interval", 0.15f, "Seconds between checks of the rod's pick.");
			cfgMode = Config.Bind<bool>("Sync", "FollowBodyPart", true, "Also set ForceForge's body part (Mouth = Head, Vagina / Anus = Vagina, hands = Right / Left Hand); off keeps your own choice and follows only the person.");
			cfgStopPdp = Config.Bind<bool>("Sync", "StopPenisDiviningPathSync", true, "Switch off ForceForge's own 'Auto-sync Target with PenisDiviningPath' while the rod drives it, so the two do not fight.");
		}

		private void Update()
		{
			if (!cfgEnabled.Value) return;
			float now = Time.unscaledTime;
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading) return;
			if (now >= nextScan) { nextScan = now + 2f; Scan(sc); }
			if (now < nextTick) return;
			nextTick = now + Mathf.Max(0.05f, cfgInterval.Value);
			for (int i = 0; i < pairs.Count; i++)
			{
				try { Tick(sc, pairs[i]); }
				catch (Exception) { }
			}
		}

		private void Scan(SuperController sc)
		{
			pairs.Clear();
			foreach (Atom a in sc.GetAtoms())
			{
				if (a == null || a.type != "Person") continue;
				JSONStorable rod = null, force = null;
				foreach (string id in a.GetStorableIDs())
				{
					if (id == null || id.IndexOf("plugin#", StringComparison.Ordinal) < 0) continue;
					JSONStorable js = a.GetStorableByID(id);
					if (js == null) continue;
					string n = js.GetType().Name;
					if (rod == null && n == "DiviningRod") rod = js;
					else if (force == null && n == "ForceForge") force = js;
				}
				if (rod == null || force == null) continue;
				Type rt = rod.GetType();
				pairs.Add(new Pair { atom = a, rod = rod, force = force, fTarget = Field(rt, "_penisTarget"), fType = Field(rt, "_targetType"), fMode = Field(force.GetType(), "TargetSelector") });
			}
		}

		private static FieldInfo Field(Type t, string name)
		{
			for (; t != null; t = t.BaseType) { FieldInfo f = t.GetField(name, Inst | BindingFlags.DeclaredOnly); if (f != null) return f; }
			return null;
		}

		private void Tick(SuperController sc, Pair p)
		{
			if (p.atom == null || p.rod == null || p.force == null || p.fTarget == null) { nextScan = 0f; return; }
			Component target = p.fTarget.GetValue(p.rod) as Component;
			if (target == null) return;
			Atom ta = target.GetComponentInParent<Atom>();
			if (ta == null || ta == p.atom || ta.type != "Person") return;

			JSONStorableStringChooser atomChooser = p.force.GetStringChooserJSONParam("target to move to");
			if (atomChooser == null) return;
			if (cfgStopPdp.Value)
			{
				JSONStorableBool pdp = p.force.GetBoolJSONParam("Auto-sync Target with PenisDiviningPath");
				if (pdp != null && pdp.val) pdp.val = false;
			}
			if (atomChooser.val != ta.uid) atomChooser.val = ta.uid;

			if (!cfgMode.Value || p.fType == null || p.fMode == null) return;
			string type = p.fType.GetValue(p.rod) as string, mode = null;
			if (type == "Mouth") mode = "Head";
			else if (type == "Vagina" || type == "Anus") mode = HasVagina(ta) ? "Vagina" : "Hips";
			else if (type == "rHand") mode = "Right Hand";
			else if (type == "lHand") mode = "Left Hand";
			if (mode == null) return;
			// ForceForge registers two choosers under the name "Mode" ("Move This" and "To This"), so the right one is taken from its field
			JSONStorableStringChooser modeChooser = p.fMode != null ? p.fMode.GetValue(p.force) as JSONStorableStringChooser : null;
			if (modeChooser != null && modeChooser.val != mode && modeChooser.choices != null && modeChooser.choices.Contains(mode)) modeChooser.val = mode;
		}

		private static bool HasVagina(Atom a)
		{
			foreach (Rigidbody rb in a.rigidbodies) if (rb != null && rb.name == "VaginaTrigger") return true;
			return false;
		}
	}
}
