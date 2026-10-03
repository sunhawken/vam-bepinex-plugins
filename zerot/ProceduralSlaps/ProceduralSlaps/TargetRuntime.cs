using System;
using System.Collections.Generic;
using ProceduralSlaps.Core;
using UnityEngine;

namespace ProceduralSlaps;

internal sealed class TargetRuntime : IDisposable
{
	private readonly Plugin host;

	private readonly TargetProfile profile;

	private readonly ContactBuffer contacts = new ContactBuffer();

	private readonly PainState pain = new PainState();

	private readonly List<ContactSample> batch = new List<ContactSample>(64);

	private readonly List<ContactSensor> sensors = new List<ContactSensor>();

	private readonly List<Collider> colliderBuffer = new List<Collider>();

	private readonly List<AutoCollider> autoBuffer = new List<AutoCollider>();

	private readonly List<DAZPhysicsMesh> meshBuffer = new List<DAZPhysicsMesh>();

	private readonly HashSet<Rigidbody> bodyBuffer = new HashSet<Rigidbody>();

	private readonly HashSet<Rigidbody> sensorBodies = new HashSet<Rigidbody>();

	private readonly HashSet<Collider> detailColliders = new HashSet<Collider>();

	private readonly Dictionary<Collider, int> ownColliders = new Dictionary<Collider, int>();

	private readonly float[] impactLevels = new float[SkinSurface.RegionNames.Length];

	private readonly float[] irritationLevels = new float[SkinSurface.RegionNames.Length];

	private DAZCharacterSelector selector;

	private SkinSurface surface;

	private SkinRenderer renderer;

	private float statusAt;

	private float surfaceAt;

	private float lastStatus;

	private int batchOffset;

	private int lastReceived;

	private int lastMapped;

	private int candidateSensors;

	private string error;

	private string runtimeStatus;

	private int received;

	private int mapped;

	private int dropped;

	private bool sensorsBound;

	private bool disposed;

	public Atom TargetAtom { get; private set; }

	public Settings Configuration => profile.Settings;

	public bool Ready { get; private set; }

	public bool Enabled => !disposed && (Object)(object)host != (Object)null && ((Behaviour)host).isActiveAndEnabled;

	public string StatusText { get; private set; }

	public string LevelsText { get; private set; }

	public TargetRuntime(Plugin owner, Atom atom, TargetProfile targetProfile)
	{
		host = owner;
		TargetAtom = atom;
		profile = targetProfile;
		object obj;
		if ((Object)(object)atom == (Object)null)
		{
			obj = null;
		}
		else
		{
			JSONStorable storableByID = atom.GetStorableByID("geometry");
			obj = ((storableByID is DAZCharacterSelector) ? storableByID : null);
		}
		selector = (DAZCharacterSelector)obj;
		runtimeStatus = ((!((Object)(object)atom == (Object)null)) ? ("Binding to " + atom.uid + "...") : "Waiting for target...");
		RefreshStatus();
	}

	public void Tick()
	{
		if (disposed || (Object)(object)TargetAtom == (Object)null)
		{
			return;
		}
		try
		{
			if (renderer != null)
			{
				renderer.RestoreBindings();
			}
			pain.Decay(Time.deltaTime * Configuration.PainDecay.Value);
			if (error != null || (Object)(object)SuperController.singleton == (Object)null)
			{
				ReleaseRuntime(fullRelease: false);
			}
			else if (SuperController.singleton.isLoading)
			{
				runtimeStatus = "VaM is loading...";
				ReleaseRuntime(fullRelease: false);
			}
			else if (!TargetAtom.on)
			{
				runtimeStatus = "Target atom is disabled.";
				ReleaseRuntime(fullRelease: false);
			}
			else if (BindRuntime())
			{
				if (surface.PollReadback() && batch.Count > 0)
				{
					int num = Mathf.Min(batch.Count, batchOffset + 2);
					while (batchOffset < num)
					{
						ContactSample sample = batch[batchOffset];
						if (Time.time - sample.Time <= 2f && surface.Stamp(sample, sample.Response, renderer.Atlases, Configuration))
						{
							mapped++;
							pain.Record(sample.Response, Configuration.PainSensitivity.Value);
						}
						else
						{
							dropped++;
						}
						batchOffset++;
					}
					if (batchOffset >= batch.Count)
					{
						batch.Clear();
						batchOffset = 0;
					}
				}
				if (batch.Count == 0 && contacts.HasReady(Time.time) && Time.unscaledTime >= surfaceAt)
				{
					if (!renderer.Intact())
					{
						ReleaseRuntime(fullRelease: false);
						return;
					}
					if (surface.BeginReadback())
					{
						contacts.TakeReady(batch, Time.time);
						batchOffset = 0;
						for (int i = 0; i < batch.Count; i++)
						{
							ContactSample value = batch[i];
							value.ResolvePose();
							batch[i] = value;
						}
						surfaceAt = Time.unscaledTime + 0.2f;
					}
				}
				if (!renderer.RenderNext(Time.time, Time.unscaledTime, Configuration))
				{
					ReleaseRuntime(fullRelease: false);
				}
			}
			if (Time.unscaledTime >= statusAt)
			{
				statusAt = Time.unscaledTime + 0.25f;
				RefreshStatus();
			}
		}
		catch (Exception e)
		{
			Fail(e);
		}
	}

	private DAZSkinV2 SelectedSkin()
	{
		if ((Object)(object)selector == (Object)null && (Object)(object)TargetAtom != (Object)null)
		{
			JSONStorable storableByID = TargetAtom.GetStorableByID("geometry");
			selector = (DAZCharacterSelector)(object)((storableByID is DAZCharacterSelector) ? storableByID : null);
		}
		return (!((Object)(object)selector == (Object)null) && !((Object)(object)selector.selectedCharacter == (Object)null)) ? selector.selectedCharacter.skin : null;
	}

	private bool BindRuntime()
	{
		DAZSkinV2 val = SelectedSkin();
		if (surface != null && ((Object)(object)surface.Skin != (Object)(object)val || !surface.Valid))
		{
			ReleaseRuntime(fullRelease: false);
		}
		if ((Object)(object)val == (Object)null || !((Behaviour)val).isActiveAndEnabled || !val.wasInit || (Object)(object)val.dazMesh == (Object)null || !val.dazMesh.wasInit || (Object)(object)val.dazMesh.uvMappedMesh == (Object)null)
		{
			Ready = false;
			contacts.Clear();
			runtimeStatus = "Waiting for target skin...";
			return false;
		}
		if (surface == null)
		{
			renderer = new SkinRenderer(val);
			surface = new SkinSurface(val, renderer.AtlasByMaterial);
		}
		if (sensorsBound)
		{
			Ready = true;
			return true;
		}
		if (!renderer.PrepareOne(Configuration))
		{
			Ready = false;
			runtimeStatus = "Preparing skin textures...";
			return false;
		}
		RefreshSensors();
		sensorsBound = true;
		Ready = true;
		return true;
	}

	private void RefreshSensors()
	{
		for (int num = sensors.Count - 1; num >= 0; num--)
		{
			if ((Object)(object)sensors[num] == (Object)null)
			{
				sensors.RemoveAt(num);
			}
		}
		((Component)TargetAtom).GetComponentsInChildren<Collider>(true, colliderBuffer);
		bodyBuffer.Clear();
		sensorBodies.Clear();
		detailColliders.Clear();
		ownColliders.Clear();
		for (int i = 0; i < colliderBuffer.Count; i++)
		{
			Collider val = colliderBuffer[i];
			if (!((Object)(object)val == (Object)null) && !val.isTrigger && !((Object)(object)val.attachedRigidbody == (Object)null) && !((Object)(object)((Component)val).GetComponentInParent<Atom>() != (Object)(object)TargetAtom) && !((Object)(object)((Component)val).GetComponentInParent<DAZClothingItem>() != (Object)null) && !((Object)(object)((Component)val).GetComponentInParent<DAZHairGroup>() != (Object)null))
			{
				AddCollider(val, -1, bodyBuffer);
			}
		}
		((Component)TargetAtom).GetComponentsInChildren<AutoCollider>(true, autoBuffer);
		for (int j = 0; j < autoBuffer.Count; j++)
		{
			AutoCollider val2 = autoBuffer[j];
			if (!((Object)(object)val2.skin != (Object)(object)surface.Skin))
			{
				int vertex = surface.ResolveVertex(val2.targetVertex);
				AddCollider(val2.jointCollider, vertex, bodyBuffer);
				AddCollider(val2.hardCollider, vertex, bodyBuffer);
				if (val2.createSoftCollider && (Object)(object)val2.jointCollider != (Object)null)
				{
					detailColliders.Add(val2.jointCollider);
				}
			}
		}
		((Component)TargetAtom).GetComponentsInChildren<DAZPhysicsMesh>(true, meshBuffer);
		for (int k = 0; k < meshBuffer.Count; k++)
		{
			DAZPhysicsMesh val3 = meshBuffer[k];
			if ((Object)(object)val3.skin != (Object)(object)surface.Skin)
			{
				continue;
			}
			if (val3.softVerticesGroups != null)
			{
				for (int l = 0; l < val3.softVerticesGroups.Count; l++)
				{
					List<DAZPhysicsMeshSoftVerticesSet> softVerticesSets = val3.softVerticesGroups[l].softVerticesSets;
					if (softVerticesSets == null)
					{
						continue;
					}
					for (int m = 0; m < softVerticesSets.Count; m++)
					{
						int vertex2 = surface.ResolveVertex(softVerticesSets[m].targetVertex);
						AddCollider(softVerticesSets[m].jointCollider, vertex2, bodyBuffer);
						AddCollider(softVerticesSets[m].jointCollider2, vertex2, bodyBuffer);
						if ((Object)(object)softVerticesSets[m].jointCollider != (Object)null)
						{
							detailColliders.Add(softVerticesSets[m].jointCollider);
						}
						if ((Object)(object)softVerticesSets[m].jointCollider2 != (Object)null)
						{
							detailColliders.Add(softVerticesSets[m].jointCollider2);
						}
					}
				}
			}
			if (val3.colliderGroups == null)
			{
				continue;
			}
			for (int n = 0; n < val3.colliderGroups.Count; n++)
			{
				DAZPhysicsMeshCapsuleCollider[] colliders = val3.colliderGroups[n].colliders;
				if (colliders != null)
				{
					for (int num2 = 0; num2 < colliders.Length; num2++)
					{
						AddCollider((Collider)(object)colliders[num2].collider, surface.ResolveVertex(colliders[num2].frontVertex), bodyBuffer);
					}
				}
			}
		}
		candidateSensors = bodyBuffer.Count;
		SensorDensity sensorDensity = new SensorDensity();
		foreach (KeyValuePair<Collider, int> ownCollider in ownColliders)
		{
			if (!detailColliders.Contains(ownCollider.Key))
			{
				sensorBodies.Add(ownCollider.Key.attachedRigidbody);
			}
		}
		foreach (KeyValuePair<Collider, int> ownCollider2 in ownColliders)
		{
			Collider key = ownCollider2.Key;
			Rigidbody attachedRigidbody = key.attachedRigidbody;
			if (!sensorBodies.Contains(attachedRigidbody))
			{
				int value = ownCollider2.Value;
				DAZBone[] strongestDAZBone = surface.Skin.strongestDAZBone;
				Transform val4 = ((strongestDAZBone == null || value < 0 || value >= strongestDAZBone.Length || !((Object)(object)strongestDAZBone[value] != (Object)null)) ? ((Component)TargetAtom).transform : ((Component)strongestDAZBone[value]).transform);
				int region = surface.SensorRegion(value);
				int instanceID = ((Object)val4).GetInstanceID();
				Bounds bounds = key.bounds;
				if (sensorDensity.Keep(region, instanceID, SkinSurface.Core(val4.InverseTransformPoint(bounds.center))))
				{
					sensorBodies.Add(attachedRigidbody);
				}
			}
		}
		foreach (Rigidbody sensorBody in sensorBodies)
		{
			ContactSensor contactSensor = ((Component)sensorBody).gameObject.AddComponent<ContactSensor>();
			contactSensor.Owner = this;
			contactSensor.Body = sensorBody;
			sensors.Add(contactSensor);
		}
	}

	private void AddCollider(Collider collider, int vertex, HashSet<Rigidbody> bodies)
	{
		if (!((Object)(object)collider == (Object)null) && !collider.isTrigger && !((Object)(object)collider.attachedRigidbody == (Object)null))
		{
			ownColliders[collider] = vertex;
			bodies.Add(collider.attachedRigidbody);
		}
	}

	public void Enqueue(ContactSample sample)
	{
		Response response = ContactResponse.Evaluate(Configuration, SkinSurface.Core(sample.Velocity), SkinSurface.Core(sample.Normal), sample.Impulse, sample.Area, sample.Friction, sample.DeltaTime, sample.ImpactEdge, sample.Sustained);
		if (!(response.Impact <= 0f) || !(response.Irritation <= 0f))
		{
			received++;
			sample.Response = response;
			sample.CapturePose();
			if (!contacts.Add(sample))
			{
				dropped++;
			}
		}
	}

	public bool OwnsCollider(Collider collider)
	{
		return (Object)(object)collider != (Object)null && ownColliders.ContainsKey(collider);
	}

	public int SurfaceVertex(Collider collider)
	{
		int value;
		return (!((Object)(object)collider != (Object)null) || !ownColliders.TryGetValue(collider, out value)) ? (-1) : value;
	}

	public void ClearMarks()
	{
		contacts.Clear();
		batch.Clear();
		batchOffset = 0;
		pain.Clear();
		if (renderer != null)
		{
			renderer.Clear(Configuration);
		}
		RefreshStatus();
	}

	public void RetryRuntime()
	{
		error = null;
		ReleaseRuntime(fullRelease: false);
		runtimeStatus = "Retrying target skin...";
		RefreshStatus();
	}

	private void ReleaseRuntime(bool fullRelease)
	{
		Ready = false;
		sensorsBound = false;
		contacts.Clear();
		batch.Clear();
		batchOffset = 0;
		ownColliders.Clear();
		pain.Clear();
		for (int i = 0; i < sensors.Count; i++)
		{
			if ((Object)(object)sensors[i] != (Object)null)
			{
				sensors[i].Owner = null;
				Object.Destroy((Object)(object)sensors[i]);
			}
		}
		sensors.Clear();
		colliderBuffer.Clear();
		autoBuffer.Clear();
		meshBuffer.Clear();
		bodyBuffer.Clear();
		sensorBodies.Clear();
		detailColliders.Clear();
		if (renderer != null)
		{
			renderer.Dispose();
		}
		renderer = null;
		surface = null;
		if (fullRelease)
		{
			selector = null;
		}
	}

	public void Fail(Exception e)
	{
		error = "ProceduralSlaps [" + ((!((Object)(object)TargetAtom == (Object)null)) ? TargetAtom.uid : "?") + "]: " + e.Message;
		ReleaseRuntime(fullRelease: false);
		if ((Object)(object)host != (Object)null)
		{
			host.ReportRuntimeError(error, e);
		}
		RefreshStatus();
	}

	private void RefreshStatus()
	{
		if (Ready && renderer != null)
		{
			runtimeStatus = "Active: " + renderer.Atlases.Count + " skin atlases, " + sensors.Count + "/" + candidateSensors + " contact sensors";
		}
		float num = Mathf.Max(0.01f, Time.unscaledTime - lastStatus);
		StatusText = (error ?? runtimeStatus) + "\nAccepted contacts/s: " + ((float)(received - lastReceived) / num).ToString("0") + "; stamps/s: " + ((float)(mapped - lastMapped) / num).ToString("0") + "\nTotal stamps: " + mapped + "; queue drops: " + dropped + "\nPain: " + pain.Value.ToString("0.00") + "  (impact " + pain.LastImpact.ToString("0.00") + ", irritation " + pain.LastIrritation.ToString("0.00") + ")";
		lastStatus = Time.unscaledTime;
		lastReceived = received;
		lastMapped = mapped;
		Array.Clear(impactLevels, 0, impactLevels.Length);
		Array.Clear(irritationLevels, 0, irritationLevels.Length);
		if (renderer != null)
		{
			for (int i = 0; i < renderer.Atlases.Count; i++)
			{
				renderer.Atlases[i].Map.Levels(impactLevels, irritationLevels);
			}
		}
		string text = "Region                 Impact | Irritation\n";
		for (int j = 0; j < impactLevels.Length; j++)
		{
			string text2 = text;
			text = text2 + SkinSurface.RegionNames[j] + ": " + impactLevels[j].ToString("0.00") + " | " + irritationLevels[j].ToString("0.00") + "\n";
		}
		LevelsText = text;
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			ReleaseRuntime(fullRelease: true);
			TargetAtom = null;
		}
	}
}
