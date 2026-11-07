using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using SimpleJSON;
using UnityEngine;

namespace ZeroT.ColliderFitHeadless
{
	// Who else is adjusting colliders. A collider Collider Editor has adjusted keeps its values and is never fitted here.
	//  1. Collider Editor (Headless) (ColliderEditor.Headless.dll, BepInEx): applies a preset to every female Person. Its plugin object is found by guid and
	//     the keys of the preset it has loaded (its "editables") are the colliders it owns. Read by reflection, so it is optional and a version change
	//     degrades to "not found" instead of an error.
	//  2. The regular Collider Editor scene plugin on a Person: its storable's JSON "editables" lists the colliders adjusted in it.
	//  3. The scene plugin version of ColliderFit (mutoe.ColliderFit): an atom that carries it is left alone entirely, so the two never fight.
	internal sealed class CeLink
	{
		public const string HeadlessGuid = "acidbubbles.collidereditor.headless";

		public const int NotFound = 0, Readable = 1, Unreadable = 2;

		private BaseUnityPlugin headless;
		private PropertyInfo presetProp;
		private ConfigEntry<bool> headlessEnabled;
		private float nextFind;
		private object lastPreset;
		private readonly HashSet<string> presetKeys = new HashSet<string>();
		public string HeadlessError;

		public bool HeadlessFound
		{
			get { return headless != null; }
		}

		// Collider Editor (Headless) is loaded and switched on
		public bool HeadlessActive
		{
			get { return headless != null && (headlessEnabled == null || headlessEnabled.Value); }
		}

		public int PresetCount
		{
			get { return presetKeys.Count; }
		}

		private void FindHeadless()
		{
			float now = Time.realtimeSinceStartup;
			if (now < nextFind)
			{
				return;
			}
			nextFind = now + 3f;
			try
			{
				PluginInfo info;
				if (!Chainloader.PluginInfos.TryGetValue(HeadlessGuid, out info) || info == null || info.Instance == null)
				{
					headless = null;
					presetProp = null;
					headlessEnabled = null;
					presetKeys.Clear();
					lastPreset = null;
					return;
				}
				if (info.Instance == headless)
				{
					return;
				}
				headless = info.Instance;
				presetProp = headless.GetType().GetProperty("Preset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				ConfigEntry<bool> e;
				headlessEnabled = headless.Config.TryGetEntry<bool>("General", "Enabled", out e) ? e : null;
				lastPreset = null;
				HeadlessError = presetProp == null ? "its Preset property was not found" : null;
			}
			catch (Exception ex)
			{
				HeadlessError = ex.Message;
			}
		}

		// keys of the preset Collider Editor (Headless) has loaded (re-read whenever it swaps in a new preset object)
		public void Poll()
		{
			FindHeadless();
			if (headless == null || presetProp == null)
			{
				return;
			}
			try
			{
				object p = presetProp.GetValue(headless, null);
				if (ReferenceEquals(p, lastPreset) && lastPreset != null)
				{
					return;
				}
				lastPreset = p;
				presetKeys.Clear();
				JSONClass preset = p as JSONClass;
				if (preset == null)
				{
					return;
				}
				JSONClass ed = preset["editables"].AsObject;
				if (ed == null)
				{
					return;
				}
				foreach (string k in ed.Keys)
				{
					presetKeys.Add(k);
				}
				HeadlessError = null;
			}
			catch (Exception ex)
			{
				HeadlessError = ex.Message;
			}
		}

		public void AddHeadless(HashSet<string> into)
		{
			if (!HeadlessActive)
			{
				return;
			}
			foreach (string k in presetKeys)
			{
				into.Add(k);
			}
		}

		// ---- per atom: the Collider Editor scene plugin
		public sealed class AtomCe
		{
			public JSONStorable storable;
			public int status = NotFound;
			public string error;
			public readonly HashSet<string> owned = new HashSet<string>();
			public JSONStorable colliderFitPlugin;     // the scene plugin version of ColliderFit, if the atom carries it
			public float nextFind;

			public void Reset()
			{
				storable = null;
				colliderFitPlugin = null;
				status = NotFound;
				error = null;
				owned.Clear();
				nextFind = 0f;
			}

			private void Find(Atom atom)
			{
				float now = Time.realtimeSinceStartup;
				if (now < nextFind)
				{
					return;
				}
				nextFind = now + 5f;
				List<string> ids = atom.GetStorableIDs();
				JSONStorable ce = null, cf = null;
				for (int i = 0; i < ids.Count; i++)
				{
					string id = ids[i];
					if (!id.StartsWith("plugin#"))
					{
						continue;
					}
					if (ce == null && id.IndexOf("ColliderEditor") >= 0 && id.IndexOf("Headless") < 0)
					{
						ce = atom.GetStorableByID(id);
					}
					else if (cf == null && id.IndexOf("ColliderFit") >= 0 && id.IndexOf("Headless") < 0)
					{
						cf = atom.GetStorableByID(id);
					}
				}
				storable = ce;
				colliderFitPlugin = cf;
			}

			// re-read; true when the set of owned keys or the status changed
			public bool Read(Atom atom)
			{
				int oldStatus = status;
				if (storable == null)
				{
					Find(atom);
				}
				if (storable == null)
				{
					bool had = owned.Count > 0;
					owned.Clear();
					status = NotFound;
					error = null;
					return had || oldStatus != status;
				}
				try
				{
					JSONClass j = storable.GetJSON(true, true, false);
					if (j == null || !j.HasKey("editables"))
					{
						status = Unreadable;
						error = "no editables key";
						return oldStatus != status;
					}
					JSONClass ed = j["editables"].AsObject;
					HashSet<string> now = new HashSet<string>();
					if (ed != null)
					{
						foreach (string k in ed.Keys)
						{
							now.Add(k);
						}
					}
					bool changed = !now.SetEquals(owned);
					if (changed)
					{
						owned.Clear();
						foreach (string k in now)
						{
							owned.Add(k);
						}
					}
					status = Readable;
					error = null;
					return changed || oldStatus != status;
				}
				catch (Exception e)
				{
					status = Unreadable;
					error = e.Message;
					return oldStatus != status;
				}
			}

			// after colliders change, make the scene plugin's preview wireframe follow: switch its preview off and on once (only if it is on)
			public void RefreshPreview()
			{
				if (storable == null)
				{
					return;
				}
				JSONStorableBool b = storable.GetBoolJSONParam("showPreviews");
				if (b == null || !b.val)
				{
					return;
				}
				b.val = false;
				b.val = true;
			}
		}
	}
}
