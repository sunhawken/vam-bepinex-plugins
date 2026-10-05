using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Hasno.CounterThrustStandalone;

[BepInPlugin("hasno.counterthrust.standalone", "CounterThrust Standalone", "4.2.4")]
public sealed class CounterThrustStandalonePlugin : BaseUnityPlugin
{
	internal struct MotionRef
	{
		public Rigidbody Rb;

		public Transform Tr;

		public bool IsValid
		{
			get
			{
				if (!((Object)(object)Rb != (Object)null))
				{
					return (Object)(object)Tr != (Object)null;
				}
				return true;
			}
		}

		public Vector3 Position
		{
			get
			{
				if ((Object)(object)Rb != (Object)null)
				{
					return Rb.worldCenterOfMass;
				}
				if ((Object)(object)Tr != (Object)null)
				{
					return Tr.position;
				}
				return Vector3.zero;
			}
		}

		public Vector3 Forward
		{
			get
			{
				if ((Object)(object)Rb != (Object)null)
				{
					return ((Component)Rb).transform.forward;
				}
				if ((Object)(object)Tr != (Object)null)
				{
					return Tr.forward;
				}
				return Vector3.forward;
			}
		}
	}

	private sealed class PersonController
	{
		private readonly CounterThrustStandalonePlugin owner;

		private Atom receiverAtom;

		private Atom sourceAtom;

		private string receiverUid = string.Empty;

		private string sourceUid = string.Empty;

		private MotionRef sourceRef;

		private Rigidbody receiverRB;

		private string resolvedSourcePart = string.Empty;

		private string resolvedReceiverPart = string.Empty;

		private bool hasLast;

		private Vector3 lastSourcePos;

		private Vector3 lastReceiverPos;

		private float smoothedThrustVel;

		private float smoothedSpeedMag;

		private float lastOutputAccel;

		private float softTripUntil;

		private bool hardTripped;

		private int nudgeFramesRemaining;

		private float lastDistance;

		private string status = "Waiting";

		private string cachedGuiStatus = "Waiting";

		public Atom ReceiverAtom => receiverAtom;

		public Atom SourceAtom => sourceAtom;

		public string ReceiverUid => receiverUid;

		public string CachedGuiStatus => cachedGuiStatus;

		public bool HardTripped => hardTripped;

		public PersonController(CounterThrustStandalonePlugin ownerPlugin, Atom receiver)
		{
			owner = ownerPlugin;
			receiverAtom = receiver;
			receiverUid = SafeUid(receiver);
			ResolveReferences();
			RefreshGuiStatus();
		}

		public void SetReceiver(Atom atom)
		{
			if (receiverAtom != atom)
			{
				receiverAtom = atom;
				receiverUid = SafeUid(atom);
				ResolveReferences();
			}
		}

		public void SetSource(Atom atom)
		{
			if ((Object)(object)atom != (Object)null && (Object)(object)receiverAtom != (Object)null)
			{
				if (atom == receiverAtom)
				{
					atom = null;
				}
				else
				{
					string text = SafeUid(atom);
					string text2 = receiverUid;
					if (text.Length > 0 && text2.Length > 0 && string.Equals(text, text2, StringComparison.Ordinal))
					{
						atom = null;
					}
				}
			}
			if (sourceAtom != atom)
			{
				sourceAtom = atom;
				sourceUid = SafeUid(atom);
				ResolveReferences();
			}
		}

		public void InvalidateReferences()
		{
			resolvedSourcePart = string.Empty;
			resolvedReceiverPart = string.Empty;
			ResolveReferences();
		}

		public void ResolveReferences()
		{
			resolvedSourcePart = owner.SourcePart;
			resolvedReceiverPart = owner.ReceiverPart;
			sourceRef = FindMotionRef(sourceAtom, resolvedSourcePart);
			receiverRB = FindReceiverRigidbody(receiverAtom, resolvedReceiverPart);
			ResetTransient(clearHardTrip: false);
		}

		public Vector3 GetTargetAnchor()
		{
			if ((Object)(object)receiverRB == (Object)null || !string.Equals(resolvedReceiverPart, owner.ReceiverPart, StringComparison.Ordinal))
			{
				receiverRB = FindReceiverRigidbody(receiverAtom, owner.ReceiverPart);
				resolvedReceiverPart = owner.ReceiverPart;
			}
			if ((Object)(object)receiverRB != (Object)null)
			{
				return receiverRB.worldCenterOfMass;
			}
			if ((Object)(object)receiverAtom != (Object)null)
			{
				try
				{
					return ((Component)receiverAtom).transform.position;
				}
				catch
				{
				}
			}
			return Vector3.zero;
		}

		public void BeginNudge()
		{
			nudgeFramesRemaining = 3;
		}

		public void ResetTransient(bool clearHardTrip)
		{
			hasLast = false;
			smoothedThrustVel = 0f;
			smoothedSpeedMag = 0f;
			lastOutputAccel = 0f;
			softTripUntil = 0f;
			nudgeFramesRemaining = 0;
			if (clearHardTrip)
			{
				hardTripped = false;
			}
		}

		private void EnsureReferencesCurrent()
		{
			if (!string.Equals(resolvedSourcePart, owner.SourcePart, StringComparison.Ordinal) || !string.Equals(resolvedReceiverPart, owner.ReceiverPart, StringComparison.Ordinal))
			{
				ResolveReferences();
			}
		}

		public void FixedStep()
		{
			if ((Object)(object)receiverAtom == (Object)null || (Object)(object)sourceAtom == (Object)null || receiverAtom == sourceAtom)
			{
				status = (((Object)(object)sourceAtom == (Object)null) ? "No other Person target" : "Self target blocked");
				ResetTransient(clearHardTrip: false);
				return;
			}
			if (receiverUid.Length > 0 && sourceUid.Length > 0 && string.Equals(receiverUid, sourceUid, StringComparison.Ordinal))
			{
				sourceAtom = null;
				sourceUid = string.Empty;
				sourceRef = default;
				status = "Self target blocked by UID";
				ResetTransient(clearHardTrip: false);
				return;
			}
			EnsureReferencesCurrent();
			if (hardTripped)
			{
				status = "HARD DISABLED (trip)";
				return;
			}
			if (Time.time < softTripUntil)
			{
				status = "SOFT TRIP cooldown";
				return;
			}
			if (!sourceRef.IsValid || (Object)(object)receiverRB == (Object)null)
			{
				ResolveReferences();
				if (!sourceRef.IsValid || (Object)(object)receiverRB == (Object)null)
				{
					status = "Waiting for source/receiver body";
					return;
				}
			}
			if (receiverRB.isKinematic)
			{
				status = "Receiver rigidbody is kinematic";
				ResetTransient(clearHardTrip: false);
				return;
			}
			Vector3 position = sourceRef.Position;
			Vector3 worldCenterOfMass = receiverRB.worldCenterOfMass;
			Vector3 val = position - worldCenterOfMass;
			float sqrMagnitude = val.sqrMagnitude;
			if (owner.UseProximity && sqrMagnitude > owner.ProximityActivation * owner.ProximityActivation)
			{
				ResetSampleState();
				status = "Proximity gate";
				return;
			}
			if (nudgeFramesRemaining > 0)
			{
				Vector3 val2 = ComputeAxis();
				if (val2.sqrMagnitude < 1E-06f)
				{
					val2 = Vector3.forward;
				}
				val2.Normalize();
				receiverRB.AddForce(val2 * 2f, (ForceMode)2);
				nudgeFramesRemaining--;
			}
			if (!hasLast)
			{
				lastSourcePos = position;
				lastReceiverPos = worldCenterOfMass;
				hasLast = true;
				status = "Primed";
				return;
			}
			float num = Mathf.Max(Time.fixedDeltaTime, 1E-06f);
			Vector3 val3 = (position - lastSourcePos) / num;
			Vector3 val4 = (worldCenterOfMass - lastReceiverPos) / num;
			if (owner.RequireSourceMotion && val3.sqrMagnitude < owner.MinSourceSpeed * owner.MinSourceSpeed)
			{
				smoothedThrustVel = 0f;
				smoothedSpeedMag = 0f;
				lastOutputAccel = 0f;
				CommitSample(position, worldCenterOfMass);
				status = "Source-motion gate";
				return;
			}
			Vector3 val5 = ComputeAxis();
			if (val5.sqrMagnitude < 1E-06f)
			{
				CommitSample(position, worldCenterOfMass);
				status = "Axis degenerate";
				return;
			}
			val5.Normalize();
			float num2 = Vector3.Dot(val3 - val4, val5);
			num2 = Mathf.Clamp(num2, 0f - owner.MaxRelVel, owner.MaxRelVel);
			if (Mathf.Abs(num2) <= owner.Deadband)
			{
				smoothedThrustVel = 0f;
				smoothedSpeedMag = 0f;
				lastOutputAccel = 0f;
				CommitSample(position, worldCenterOfMass);
				status = "OK / deadband";
				return;
			}
			float num3 = 1f - Mathf.Exp((0f - num) / Mathf.Max(owner.SmoothingSeconds, 1E-05f));
			smoothedThrustVel = Mathf.Lerp(smoothedThrustVel, num2, num3);
			val = val3 - val4;
			float magnitude = val.magnitude;
			smoothedSpeedMag = Mathf.Lerp(smoothedSpeedMag, magnitude, num3 * 0.5f);
			if (DetectAnomaly(worldCenterOfMass, val5, magnitude))
			{
				CommitSample(position, worldCenterOfMass);
				Trip("Anomaly detected");
				return;
			}
			float num4 = (0f - smoothedThrustVel) * owner.ResponseStrength;
			num4 = Mathf.Clamp(num4, 0f - owner.MaxAccel, owner.MaxAccel);
			if (Mathf.Abs(smoothedThrustVel) > 0.1f && Mathf.Abs(num4) < 0.6f)
			{
				num4 = Mathf.Sign(num4) * 0.6f;
			}
			float num5 = Mathf.Max(0f, owner.MaxSlewPerStep);
			float num6 = Mathf.Clamp(num4 - lastOutputAccel, 0f - num5, num5);
			float num7 = (lastOutputAccel += num6);
			if (owner.UseVelocityChange)
			{
				float num8 = num7 * num;
				num8 *= owner.ResponseStrength * owner.VelChangeGain;
				num8 = Mathf.Clamp(num8, 0f - owner.MaxVelChangePerStep, owner.MaxVelChangePerStep);
				receiverRB.AddForce(val5 * num8, (ForceMode)2);
			}
			else
			{
				receiverRB.AddForce(val5 * num7, (ForceMode)5);
			}
			CommitSample(position, worldCenterOfMass);
			status = "OK";
		}

		private void ResetSampleState()
		{
			hasLast = false;
			smoothedThrustVel = 0f;
			smoothedSpeedMag = 0f;
			lastOutputAccel = 0f;
		}

		private void CommitSample(Vector3 sourcePos, Vector3 receiverPos)
		{
			lastSourcePos = sourcePos;
			lastReceiverPos = receiverPos;
			hasLast = true;
		}

		private Vector3 ComputeAxis()
		{
			if (!sourceRef.IsValid || (Object)(object)receiverRB == (Object)null)
			{
				return Vector3.forward;
			}
			if (string.Equals(owner.AxisMode, "Orientation Only", StringComparison.Ordinal))
			{
				return sourceRef.Forward;
			}
			return receiverRB.worldCenterOfMass - sourceRef.Position;
		}

		private bool DetectAnomaly(Vector3 receiverPos, Vector3 axis, float relSpeedMag)
		{
			float num = Mathf.Max(Time.fixedDeltaTime, 1E-06f);
			float num2 = Mathf.Max(0.01f, owner.ExpectedSpeed);
			float num3 = Mathf.Max(num2, smoothedSpeedMag);
			bool flag = relSpeedMag > 2f * num3 + 0.1f;
			Vector3 val = receiverPos - lastReceiverPos;
			float magnitude = val.magnitude;
			float num4 = num2 * num * 4f;
			bool flag2 = magnitude > num4 && magnitude > 0.06f;
			Vector3 val2 = receiverPos - lastReceiverPos;
			float num5 = Vector3.Dot(val2, axis);
			val = val2 - axis * num5;
			if (!(val.magnitude > owner.CorridorRadius))
			{
				return flag & flag2;
			}
			return true;
		}

		private void Trip(string reason)
		{
			softTripUntil = Time.time + owner.SoftTripCooldown;
			ResetSampleState();
			if (owner.HardDisableOnTrip)
			{
				hardTripped = true;
				status = "HARD TRIP: " + reason;
			}
			else
			{
				status = "SOFT TRIP: " + reason;
			}
		}

		public void RefreshGuiStatus()
		{
			string text = SafeDisplayName(receiverAtom);
			string text2 = SafeDisplayName(sourceAtom);
			string text3 = (((Object)(object)receiverRB != (Object)null) ? ((Object)receiverRB).name : "none");
			if (sourceRef.IsValid && (Object)(object)receiverRB != (Object)null)
			{
				Vector3 val = sourceRef.Position - receiverRB.worldCenterOfMass;
				lastDistance = Mathf.Sqrt(val.sqrMagnitude);
			}
			else
			{
				lastDistance = 0f;
			}
			cachedGuiStatus = text + "  ->  " + text2 + "  | distance " + lastDistance.ToString("0.000") + " m  | receiver " + text3 + "  | " + status;
		}

		private static string SafeDisplayName(Atom atom)
		{
			if ((Object)(object)atom == (Object)null)
			{
				return "(none)";
			}
			string text = SafeUid(atom);
			if (text.Length > 0)
			{
				return text;
			}
			try
			{
				return ((Object)atom).name ?? "(unnamed)";
			}
			catch
			{
				return "(unnamed)";
			}
		}
	}

	public const string PluginGuid = "hasno.counterthrust.standalone";

	public const string PluginName = "CounterThrust Standalone";

	public const string PluginVersion = "4.2.4";

	private static readonly string[] PartOptions = new string[12]
	{
		"hip", "pelvis", "abdomen", "chest", "head", "lHand", "rHand", "lFoot", "rFoot", "penisBase",
		"control", "object"
	};

	private static readonly string[] AxisOptions = new string[4] { "Orientation Only", "Hip-To-Hip", "Thrust Angle Dep", "Source-To-Receiver" };

	private ConfigEntry<bool> cfgEnabled;

	private ConfigEntry<bool> cfgAutoNearest;

	private ConfigEntry<float> cfgRetargetInterval;

	private ConfigEntry<string> cfgSourcePart;

	private ConfigEntry<string> cfgReceiverPart;

	private ConfigEntry<string> cfgAxisMode;

	private ConfigEntry<float> cfgResponseStrength;

	private ConfigEntry<float> cfgSmoothingSeconds;

	private ConfigEntry<float> cfgMaxAccel;

	private ConfigEntry<float> cfgMaxSlewPerStep;

	private ConfigEntry<float> cfgDeadband;

	private ConfigEntry<float> cfgCorridorRadius;

	private ConfigEntry<float> cfgExpectedSpeed;

	private ConfigEntry<float> cfgSoftTripCooldown;

	private ConfigEntry<bool> cfgHardDisableOnTrip;

	private ConfigEntry<bool> cfgUseProximity;

	private ConfigEntry<float> cfgProximityActivation;

	private ConfigEntry<float> cfgMaxRelVel;

	private ConfigEntry<bool> cfgRequireSourceMotion;

	private ConfigEntry<float> cfgMinSourceSpeed;

	private ConfigEntry<bool> cfgUseVelocityChange;

	private ConfigEntry<float> cfgMaxVelChangePerStep;

	private ConfigEntry<float> cfgVelChangeGain;

	private ConfigEntry<bool> cfgAutoDpi;

	private ConfigEntry<float> cfgUiScale;

	private ConfigEntry<bool> cfgGuiVisible;

	private ConfigEntry<bool> cfgCollapsed;

	private readonly Dictionary<string, PersonController> controllers = new Dictionary<string, PersonController>(StringComparer.Ordinal);

	private readonly List<Atom> personScratch = new List<Atom>();

	private readonly List<string> removeScratch = new List<string>();

	private readonly List<PersonController> controllerList = new List<PersonController>();

	private float nextRetargetTime;

	private float nextPersonScanTime;

	private float nextGuiStatusRefresh;

	private bool forceRetarget = true;

	private bool lastEnabled;

	private Rect windowRect = new Rect(20f, 20f, 560f, 760f);

	private Vector2 scrollPosition = Vector2.zero;

	private bool collapsed;

	private float expandedWidth = 560f;

	private float expandedHeight = 760f;

	private bool resizing;

	private Vector2 resizeStartMouse;

	private Vector2 resizeStartSize;

	private int windowId;

	private float guiScaleCached = 1f;

	private GUIStyle titleStyle;

	private GUIStyle smallStyle;

	private GUIStyle statusStyle;

	private GUIStyle sectionStyle;

	private bool stylesReady;

	private ConfigEntry<float> cfgWindowX;

	private ConfigEntry<float> cfgWindowY;

	internal string SourcePart => NormalizeChoice(cfgSourcePart.Value, PartOptions, "hip");

	internal string ReceiverPart => NormalizeChoice(cfgReceiverPart.Value, PartOptions, "hip");

	internal string AxisMode => NormalizeChoice(cfgAxisMode.Value, AxisOptions, "Hip-To-Hip");

	internal float ResponseStrength => Mathf.Clamp(cfgResponseStrength.Value, 0f, 15f);

	internal float SmoothingSeconds => Mathf.Clamp(cfgSmoothingSeconds.Value, 0.001f, 0.8f);

	internal float MaxAccel => Mathf.Clamp(cfgMaxAccel.Value, 0f, 150f);

	internal float MaxSlewPerStep => Mathf.Clamp(cfgMaxSlewPerStep.Value, 0f, 100f);

	internal float Deadband => Mathf.Clamp(cfgDeadband.Value, 0f, 0.25f);

	internal float CorridorRadius => Mathf.Clamp(cfgCorridorRadius.Value, 0.001f, 0.5f);

	internal float ExpectedSpeed => Mathf.Clamp(cfgExpectedSpeed.Value, 0.001f, 5f);

	internal float SoftTripCooldown => Mathf.Clamp(cfgSoftTripCooldown.Value, 0.01f, 10f);

	internal bool HardDisableOnTrip => cfgHardDisableOnTrip.Value;

	internal bool UseProximity => cfgUseProximity.Value;

	internal float ProximityActivation => Mathf.Clamp(cfgProximityActivation.Value, 0.01f, 5f);

	internal float MaxRelVel => Mathf.Clamp(cfgMaxRelVel.Value, 0.05f, 20f);

	internal bool RequireSourceMotion => cfgRequireSourceMotion.Value;

	internal float MinSourceSpeed => Mathf.Clamp(cfgMinSourceSpeed.Value, 0f, 2f);

	internal bool UseVelocityChange => cfgUseVelocityChange.Value;

	internal float MaxVelChangePerStep => Mathf.Clamp(cfgMaxVelChangePerStep.Value, 0f, 5f);

	internal float VelChangeGain => Mathf.Clamp(cfgVelChangeGain.Value, 0f, 80f);

	private void Awake()
	{
		BindConfig();
		windowRect.x = cfgWindowX.Value;
		windowRect.y = cfgWindowY.Value;
		collapsed = cfgCollapsed.Value;
		if (collapsed)
		{
			windowRect.width = CollapsedWindowWidth();
			windowRect.height = CollapsedWindowHeight();
		}
		windowId = "hasno.counterthrust.standalone".GetHashCode();
		lastEnabled = cfgEnabled.Value;
		Logger.LogInfo((object)"CounterThrust Standalone 4.2.4 loaded.");
	}

	private void BindConfig()
	{
		cfgEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("General", "Enabled", true, "Master CounterThrust switch.");
		cfgAutoNearest = ((BaseUnityPlugin)this).Config.Bind<bool>("Targeting", "AutoSelectNearestPerson", true, "Each Person targets the closest OTHER Person. Self-targeting is always blocked.");
		cfgRetargetInterval = ((BaseUnityPlugin)this).Config.Bind<float>("Targeting", "RetargetIntervalSeconds", 0.75f, "How often nearest cached Person targets are re-evaluated. Full scene scans are independently throttled to reduce overhead.");
		cfgSourcePart = ((BaseUnityPlugin)this).Config.Bind<string>("Targeting", "SourcePart", "hip", "Source body part sampled for motion.");
		cfgReceiverPart = ((BaseUnityPlugin)this).Config.Bind<string>("Targeting", "ReceiverPart", "hip", "Receiver body part receiving CounterThrust.");
		cfgAxisMode = ((BaseUnityPlugin)this).Config.Bind<string>("Targeting", "AxisMode", "Hip-To-Hip", "Interaction axis mode.");
		cfgUseVelocityChange = ((BaseUnityPlugin)this).Config.Bind<bool>("Force", "UseVelocityChange", true, "Use ForceMode.VelocityChange instead of acceleration.");
		cfgMaxVelChangePerStep = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "MaxVelocityChangePerStep", 1f, "Maximum velocity change per physics step.");
		cfgVelChangeGain = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "ResponseVelocity", 12f, "Velocity-change response gain.");
		cfgResponseStrength = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "ResponseForce", 3f, "CounterThrust response strength.");
		cfgDeadband = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "ResponseDeadzone", 0.01f, "Relative-speed deadband.");
		cfgMaxAccel = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "MaxAcceleration", 22f, "Maximum acceleration magnitude.");
		cfgMaxSlewPerStep = ((BaseUnityPlugin)this).Config.Bind<float>("Force", "ResponseCurve", 6f, "Maximum acceleration change per fixed step.");
		cfgRequireSourceMotion = ((BaseUnityPlugin)this).Config.Bind<bool>("Motion", "RequireSourceMotion", true, "Do not react unless the source is actually moving.");
		cfgMinSourceSpeed = ((BaseUnityPlugin)this).Config.Bind<float>("Motion", "MinSourceSpeed", 0.05f, "Minimum source speed in m/s.");
		cfgMaxRelVel = ((BaseUnityPlugin)this).Config.Bind<float>("Motion", "MaxRelativeSpeed", 1.25f, "Single-frame relative velocity clamp.");
		cfgSmoothingSeconds = ((BaseUnityPlugin)this).Config.Bind<float>("Motion", "SmoothingSeconds", 0.12f, "Exponential smoothing time constant.");
		cfgUseProximity = ((BaseUnityPlugin)this).Config.Bind<bool>("Motion", "UseProximity", true, "Only react when source is near receiver.");
		cfgProximityActivation = ((BaseUnityPlugin)this).Config.Bind<float>("Motion", "ProximityActivation", 0.75f, "Maximum source/receiver distance for response.");
		cfgCorridorRadius = ((BaseUnityPlugin)this).Config.Bind<float>("Safety", "PathCorridorRadius", 0.04f, "Maximum off-axis movement before a safety trip.");
		cfgExpectedSpeed = ((BaseUnityPlugin)this).Config.Bind<float>("Safety", "ExpectedSpeed", 0.2f, "Expected speed baseline for anomaly detection.");
		cfgSoftTripCooldown = ((BaseUnityPlugin)this).Config.Bind<float>("Safety", "SoftTripCooldown", 0.5f, "Soft trip cooldown in seconds.");
		cfgHardDisableOnTrip = ((BaseUnityPlugin)this).Config.Bind<bool>("Safety", "HardDisableOnTrip", false, "Latch a controller disabled when it trips.");
		cfgAutoDpi = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "AutoDPIScaling", true, "Scale the GUI using desktop DPI when available.");
		cfgUiScale = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "ManualUIScale", 1f, "Additional GUI scale multiplier.");
		cfgGuiVisible = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "VisibleLowImpact", true, "Show the CounterThrust desktop GUI. Defaults ON; F8 toggles it.");
		cfgCollapsed = ((BaseUnityPlugin)this).Config.Bind<bool>("GUI", "Collapsed", false, "Remember whether the desktop window is collapsed to its title bar.");
		cfgWindowX = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowX", 20f, "Last screen position of the CounterThrust window (X).");
		cfgWindowY = ((BaseUnityPlugin)this).Config.Bind<float>("GUI", "WindowY", 20f, "Last screen position of the CounterThrust window (Y).");
	}

	private void Update()
	{
		if (!Input.GetMouseButton(0))
		{
			SaveWindowPositionIfChanged();
		}
		if (Input.GetKeyDown((KeyCode)289))
		{
			cfgGuiVisible.Value = !cfgGuiVisible.Value;
		}
		if (cfgEnabled.Value != lastEnabled)
		{
			if (cfgEnabled.Value)
			{
				ResetAllTransient(clearHardTrips: true);
				forceRetarget = true;
				nextPersonScanTime = 0f;
			}
			else
			{
				ResetAllTransient(clearHardTrips: false);
			}
			lastEnabled = cfgEnabled.Value;
		}
		if ((Object)(object)SuperController.singleton == (Object)null || !cfgEnabled.Value)
		{
			return;
		}

		float unscaledTime = Time.unscaledTime;
		if (forceRetarget || unscaledTime >= nextPersonScanTime)
		{
			SynchronizePeople();
			nextPersonScanTime = unscaledTime + 2f;
		}
		float num = Mathf.Clamp(cfgRetargetInterval.Value, 0.5f, 5f);
		if (forceRetarget || unscaledTime >= nextRetargetTime)
		{
			if (cfgAutoNearest.Value)
			{
				RetargetCachedPeople();
			}
			forceRetarget = false;
			nextRetargetTime = unscaledTime + num;
		}
		if (!cfgGuiVisible.Value || !(unscaledTime >= nextGuiStatusRefresh))
		{
			return;
		}
		for (int i = 0; i < controllerList.Count; i++)
		{
			if (controllerList[i] != null)
			{
				controllerList[i].RefreshGuiStatus();
			}
		}
		nextGuiStatusRefresh = unscaledTime + 0.25f;
	}

	private void FixedUpdate()
	{
		if (!cfgEnabled.Value || (Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		try
		{
			if ((Object)(object)SuperController.singleton.freezeAnimationToggle != (Object)null && SuperController.singleton.freezeAnimationToggle.isOn)
			{
				return;
			}
		}
		catch
		{
		}
		for (int i = 0; i < controllerList.Count; i++)
		{
			controllerList[i]?.FixedStep();
		}
	}

	private void SynchronizePeople()
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		personScratch.Clear();
		List<Atom> list = null;
		try
		{
			list = SuperController.singleton.GetAtoms();
		}
		catch
		{
			list = null;
		}
		if (list == null)
		{
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			Atom val = list[i];
			if (IsPerson(val))
			{
				personScratch.Add(val);
			}
		}
		for (int j = 0; j < personScratch.Count; j++)
		{
			Atom val2 = personScratch[j];
			string text = SafeUid(val2);
			if (text.Length != 0)
			{
				if (!controllers.TryGetValue(text, out var value) || value == null)
				{
					value = new PersonController(this, val2);
					controllers[text] = value;
				}
				else
				{
					value.SetReceiver(val2);
				}
			}
		}
		removeScratch.Clear();
		foreach (KeyValuePair<string, PersonController> controller in controllers)
		{
			bool flag = false;
			for (int k = 0; k < personScratch.Count; k++)
			{
				if (string.Equals(SafeUid(personScratch[k]), controller.Key, StringComparison.Ordinal))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				removeScratch.Add(controller.Key);
			}
		}
		for (int l = 0; l < removeScratch.Count; l++)
		{
			controllers.Remove(removeScratch[l]);
		}
		controllerList.Clear();
		foreach (KeyValuePair<string, PersonController> controller2 in controllers)
		{
			if (controller2.Value != null)
			{
				controllerList.Add(controller2.Value);
			}
		}
	}

	private void RetargetCachedPeople()
	{
		for (int i = 0; i < controllerList.Count; i++)
		{
			PersonController personController = controllerList[i];
			personController?.SetSource(FindNearestOther(personController)?.ReceiverAtom);
		}
	}

	private PersonController FindNearestOther(PersonController receiverState)
	{
		if (receiverState == null || (Object)(object)receiverState.ReceiverAtom == (Object)null)
		{
			return null;
		}
		string receiverUid = receiverState.ReceiverUid;
		Vector3 targetAnchor = receiverState.GetTargetAnchor();
		float num = float.PositiveInfinity;
		PersonController result = null;
		for (int i = 0; i < controllerList.Count; i++)
		{
			PersonController personController = controllerList[i];
			if (personController == null || (Object)(object)personController.ReceiverAtom == (Object)null || personController.ReceiverAtom == receiverState.ReceiverAtom)
			{
				continue;
			}
			string receiverUid2 = personController.ReceiverUid;
			if (receiverUid2.Length != 0 && !string.Equals(receiverUid2, receiverUid, StringComparison.Ordinal))
			{
				Vector3 val = personController.GetTargetAnchor() - targetAnchor;
				float sqrMagnitude = val.sqrMagnitude;
				if (sqrMagnitude < num)
				{
					num = sqrMagnitude;
					result = personController;
				}
			}
		}
		return result;
	}

	private bool IsPerson(Atom atom)
	{
		if ((Object)(object)atom == (Object)null)
		{
			return false;
		}
		try
		{
			return string.Equals(atom.type, "Person", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static string SafeUid(Atom atom)
	{
		if ((Object)(object)atom == (Object)null)
		{
			return string.Empty;
		}
		try
		{
			return atom.uid ?? string.Empty;
		}
		catch
		{
			try
			{
				return ((Object)atom).name ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}
	}

	private static string NormalizeChoice(string value, string[] choices, string fallback)
	{
		if (value != null)
		{
			for (int i = 0; i < choices.Length; i++)
			{
				if (string.Equals(value, choices[i], StringComparison.OrdinalIgnoreCase))
				{
					return choices[i];
				}
			}
		}
		return fallback;
	}

	internal static Rigidbody FindBodyRigidbody(Atom atom, string partKey)
	{
		if ((Object)(object)atom == (Object)null || string.IsNullOrEmpty(partKey))
		{
			return null;
		}
		string text = partKey.Trim().ToLowerInvariant();
		string[] array = text switch
		{
			"hip" => new string[5] { "hip", "hipjoint", "hip_j", "hipdrv", "hiprb" }, 
			"pelvis" => new string[3] { "pelvis", "pelvisjoint", "pelvis_j" }, 
			"abdomen" => new string[4] { "abdomen", "abd", "abdomenlower", "spine1" }, 
			"chest" => new string[4] { "chest", "torso", "spine2", "spine3" }, 
			"head" => new string[2] { "head", "neck" }, 
			"lhand" => new string[3] { "lhand", "lefthand", "l_hand" }, 
			"rhand" => new string[3] { "rhand", "righthand", "r_hand" }, 
			"lfoot" => new string[3] { "lfoot", "leftfoot", "l_foot" }, 
			"rfoot" => new string[3] { "rfoot", "rightfoot", "r_foot" }, 
			"penisbase" => new string[5] { "penisbase", "penis", "genital", "cock", "shaft" }, 
			_ => new string[1] { text }, 
		};
		Rigidbody[] array2 = null;
		try
		{
			array2 = ((Component)atom).GetComponentsInChildren<Rigidbody>(true);
		}
		catch
		{
			array2 = null;
		}
		if (array2 == null || array2.Length == 0)
		{
			return null;
		}
		Rigidbody result = null;
		int num = int.MinValue;
		Rigidbody[] array3 = array2;
		foreach (Rigidbody val in array3)
		{
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			string text2 = (((Object)val).name ?? string.Empty).ToLowerInvariant();
			int num2 = 0;
			string[] array4 = array;
			foreach (string text3 in array4)
			{
				if (text2 == text3)
				{
					num2 += 50;
				}
				if (text2.Contains(text3))
				{
					num2 += 20;
				}
			}
			if (text2.Contains("control"))
			{
				num2 -= 25;
			}
			if (text2.Contains("collider"))
			{
				num2 -= 5;
			}
			int num3 = 0;
			Transform val2 = ((Component)val).transform;
			while ((Object)(object)val2 != (Object)null && (Object)(object)val2 != (Object)(object)((Component)atom).transform && num3 < 50)
			{
				num3++;
				val2 = val2.parent;
			}
			num2 -= num3;
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		if (num < 10)
		{
			return null;
		}
		return result;
	}

	internal static MotionRef FindMotionRef(Atom atom, string keyRaw)
	{
		MotionRef result = default;
		if ((Object)(object)atom == (Object)null || string.IsNullOrEmpty(keyRaw))
		{
			return result;
		}
		string text = keyRaw.Trim().ToLowerInvariant();
		switch (text)
		{
		case "atomroot":
		case "object":
			result.Tr = ((Component)atom).transform;
			return result;
		case "control":
			try
			{
				FreeControllerV3 componentInChildren = ((Component)atom).GetComponentInChildren<FreeControllerV3>(true);
				if ((Object)(object)componentInChildren != (Object)null)
				{
					result.Tr = ((Component)componentInChildren).transform;
				}
			}
			catch
			{
			}
			return result;
		default:
			result.Rb = FindBodyRigidbody(atom, text);
			if ((Object)(object)result.Rb == (Object)null)
			{
				try
				{
					result.Tr = ((Component)atom).transform;
				}
				catch
				{
				}
			}
			return result;
		}
	}

	internal static Rigidbody FindReceiverRigidbody(Atom atom, string keyRaw)
	{
		if ((Object)(object)atom == (Object)null || string.IsNullOrEmpty(keyRaw))
		{
			return null;
		}
		string text = keyRaw.Trim().ToLowerInvariant();
		switch (text)
		{
		case "atomroot":
		case "object":
			return FindAnyDynamicRigidbody(atom);
		case "control":
			return FindAnyDynamicRigidbody(atom);
		default:
			return FindBodyRigidbody(atom, text);
		}
	}

	internal static Rigidbody FindAnyDynamicRigidbody(Atom atom)
	{
		if ((Object)(object)atom == (Object)null)
		{
			return null;
		}
		Rigidbody[] array = null;
		try
		{
			array = ((Component)atom).GetComponentsInChildren<Rigidbody>(true);
		}
		catch
		{
			array = null;
		}
		if (array == null || array.Length == 0)
		{
			return null;
		}
		for (int i = 0; i < array.Length; i++)
		{
			if ((Object)(object)array[i] != (Object)null && !array[i].isKinematic)
			{
				return array[i];
			}
		}
		for (int j = 0; j < array.Length; j++)
		{
			if ((Object)(object)array[j] != (Object)null)
			{
				return array[j];
			}
		}
		return null;
	}

	private void ResetAllTransient(bool clearHardTrips)
	{
		foreach (KeyValuePair<string, PersonController> controller in controllers)
		{
			if (controller.Value != null)
			{
				controller.Value.ResetTransient(clearHardTrips);
			}
		}
	}

	private void InvalidateAllReferences()
	{
		foreach (KeyValuePair<string, PersonController> controller in controllers)
		{
			if (controller.Value != null)
			{
				controller.Value.InvalidateReferences();
			}
		}
		forceRetarget = true;
	}

	private void NudgeAll()
	{
		foreach (KeyValuePair<string, PersonController> controller in controllers)
		{
			if (controller.Value != null)
			{
				controller.Value.BeginNudge();
			}
		}
	}

	private void ApplyDefaultPreset()
	{
		cfgEnabled.Value = true;
		cfgAxisMode.Value = "Hip-To-Hip";
		cfgUseVelocityChange.Value = true;
		cfgMaxVelChangePerStep.Value = 0.8f;
		cfgVelChangeGain.Value = 12f;
		cfgResponseStrength.Value = 3f;
		cfgSmoothingSeconds.Value = 0.06f;
		cfgDeadband.Value = 0.02f;
		cfgMaxAccel.Value = 80f;
		cfgMaxSlewPerStep.Value = 40f;
		cfgUseProximity.Value = true;
		cfgProximityActivation.Value = 0.75f;
		cfgMaxRelVel.Value = 1.25f;
		ResetAllTransient(clearHardTrips: true);
	}

	private void ApplySoftPreset()
	{
		cfgEnabled.Value = true;
		cfgAxisMode.Value = "Hip-To-Hip";
		cfgUseVelocityChange.Value = true;
		cfgMaxVelChangePerStep.Value = 1f;
		cfgVelChangeGain.Value = 6f;
		cfgResponseStrength.Value = 1.8f;
		cfgSmoothingSeconds.Value = 0.14f;
		cfgDeadband.Value = 0.03f;
		cfgMaxAccel.Value = 40f;
		cfgMaxSlewPerStep.Value = 20f;
		cfgUseProximity.Value = true;
		cfgProximityActivation.Value = 0.85f;
		cfgMaxRelVel.Value = 1f;
		ResetAllTransient(clearHardTrips: true);
	}

	private void ApplyStrongPreset()
	{
		cfgEnabled.Value = true;
		cfgAxisMode.Value = "Hip-To-Hip";
		cfgUseVelocityChange.Value = true;
		cfgMaxVelChangePerStep.Value = 1f;
		cfgVelChangeGain.Value = 18f;
		cfgResponseStrength.Value = 4f;
		cfgSmoothingSeconds.Value = 0.05f;
		cfgDeadband.Value = 0.01f;
		cfgMaxAccel.Value = 120f;
		cfgMaxSlewPerStep.Value = 60f;
		cfgUseProximity.Value = true;
		cfgProximityActivation.Value = 0.6f;
		cfgMaxRelVel.Value = 1.75f;
		ResetAllTransient(clearHardTrips: true);
	}

	private float GetGuiScale()
	{
		float num = Mathf.Clamp(cfgUiScale.Value, 0.6f, 2.5f);
		float num2 = 1f;
		if (cfgAutoDpi.Value)
		{
			num2 = ZeroT.UiKit.RlChrome.Dpi(windowRect.x * guiScaleCached, windowRect.y * guiScaleCached);
		}
		return Mathf.Clamp(num2 * num, 0.6f, 3f);
	}

	private void EnsureStyles()
	{
		if (!stylesReady)
		{
			stylesReady = true;
			titleStyle = new GUIStyle(GUI.skin.label);
			smallStyle = new GUIStyle(GUI.skin.label);
			smallStyle.fontSize = 11;
			smallStyle.wordWrap = true;
			statusStyle = new GUIStyle(GUI.skin.box);
			statusStyle.alignment = (TextAnchor)3;
			statusStyle.wordWrap = true;
			sectionStyle = new GUIStyle(GUI.skin.box);
			sectionStyle.alignment = (TextAnchor)3;
		}
	}

	private void OnGUI()
	{
		if (cfgGuiVisible != null && cfgGuiVisible.Value)
		{
			EnsureStyles();
			guiScaleCached = GetGuiScale();
			Matrix4x4 matrix = GUI.matrix;
			GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(guiScaleCached, guiScaleCached, 1f));
			float num = (float)Screen.width / guiScaleCached;
			float num2 = (float)Screen.height / guiScaleCached;
			if (collapsed)
			{
				windowRect.width = CollapsedWindowWidth();
				windowRect.height = CollapsedWindowHeight();
			}
			else
			{
				windowRect.width = Mathf.Clamp(windowRect.width, 360f, Mathf.Max(360f, num));
				windowRect.height = Mathf.Clamp(windowRect.height, 320f, Mathf.Max(320f, num2));
			}
			windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, num - 80f));
			windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, num2 - CollapsedWindowHeight()));
			windowRect = GUI.Window(windowId, windowRect, (GUI.WindowFunction)DrawWindow, string.Empty, GUIStyle.none);
			GUI.matrix = matrix;
		}
	}

	private float CollapsedWindowWidth()
	{
		return 340f;
	}

	private float CollapsedWindowHeight()
	{
		return 34f;
	}

	private void ToggleCollapsed()
	{
		if (!collapsed)
		{
			expandedWidth = Mathf.Max(360f, windowRect.width);
			expandedHeight = Mathf.Max(320f, windowRect.height);
			collapsed = true;
			resizing = false;
			windowRect.width = CollapsedWindowWidth();
			windowRect.height = CollapsedWindowHeight();
		}
		else
		{
			collapsed = false;
			windowRect.width = expandedWidth;
			windowRect.height = expandedHeight;
		}
		cfgCollapsed.Value = collapsed;
		((BaseUnityPlugin)this).Config.Save();
	}

	private void DrawWindow(int id)
	{
		ZeroT.UiKit.RlChrome.Backdrop(windowRect.width, windowRect.height);
		int rlButtons = ZeroT.UiKit.RlChrome.TitleRow(windowRect.width, "CounterThrust v4.2.4", collapsed, cfgEnabled.Value);
		if ((rlButtons & 1) != 0)
		{
			cfgUiScale.Value = Mathf.Clamp(cfgUiScale.Value - 0.1f, 0.65f, 2.5f);
			ZeroT.UiKit.RlChrome.ResetDpi();
		}
		if ((rlButtons & 2) != 0)
		{
			cfgUiScale.Value = Mathf.Clamp(cfgUiScale.Value + 0.1f, 0.65f, 2.5f);
			ZeroT.UiKit.RlChrome.ResetDpi();
		}
		if ((rlButtons & 16) != 0)
		{
			cfgEnabled.Value = !cfgEnabled.Value;
			((BaseUnityPlugin)this).Config.Save();
		}
		if ((rlButtons & 4) != 0)
		{
			ToggleCollapsed();
		}
		if ((rlButtons & 8) != 0)
		{
			cfgGuiVisible.Value = false;
			((BaseUnityPlugin)this).Config.Save();
		}
		if (collapsed)
		{
			GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(1f, windowRect.width - 118f), 26f));
			return;
		}
		GUILayout.BeginArea(ZeroT.UiKit.RlChrome.Body(windowRect.width, windowRect.height));
		GUILayout.BeginVertical(new GUILayoutOption[0]);
		scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, new GUILayoutOption[0]);
		DrawSectionHeader("Main / Targeting");
		cfgEnabled.Value = GUILayout.Toggle(cfgEnabled.Value, "Enabled", new GUILayoutOption[0]);
		bool value = cfgAutoNearest.Value;
		cfgAutoNearest.Value = GUILayout.Toggle(cfgAutoNearest.Value, "Auto select closest OTHER Person (self blocked)", new GUILayoutOption[0]);
		if (cfgAutoNearest.Value != value)
		{
			forceRetarget = true;
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Retarget Now", new GUILayoutOption[0]))
		{
			forceRetarget = true;
		}
		if (GUILayout.Button("Reset Trips", new GUILayoutOption[0]))
		{
			ResetAllTransient(clearHardTrips: true);
		}
		if (GUILayout.Button("Nudge All", new GUILayoutOption[0]))
		{
			NudgeAll();
		}
		GUILayout.EndHorizontal();

		float value2 = cfgRetargetInterval.Value;
		cfgRetargetInterval.Value = DrawSlider("Retarget interval (s)", value2, 0.5f, 2f, "0.00");
		string value3 = cfgSourcePart.Value;
		cfgSourcePart.Value = DrawChoice("Source part", value3, PartOptions);
		if (!string.Equals(value3, cfgSourcePart.Value, StringComparison.Ordinal))
		{
			InvalidateAllReferences();
		}
		string value4 = cfgReceiverPart.Value;
		cfgReceiverPart.Value = DrawChoice("Receiver part", value4, PartOptions);
		if (!string.Equals(value4, cfgReceiverPart.Value, StringComparison.Ordinal))
		{
			InvalidateAllReferences();
		}
		cfgAxisMode.Value = DrawChoice("Axis", cfgAxisMode.Value, AxisOptions);
		DrawSectionHeader("Presets");
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		if (GUILayout.Button("Default", new GUILayoutOption[0]))
		{
			ApplyDefaultPreset();
		}
		if (GUILayout.Button("Soft", new GUILayoutOption[0]))
		{
			ApplySoftPreset();
		}
		if (GUILayout.Button("Strong", new GUILayoutOption[0]))
		{
			ApplyStrongPreset();
		}
		GUILayout.EndHorizontal();
		DrawSectionHeader("Force Controls");
		cfgUseVelocityChange.Value = GUILayout.Toggle(cfgUseVelocityChange.Value, "Use velocity change", new GUILayoutOption[0]);
		cfgMaxVelChangePerStep.Value = DrawSlider("Max velocity change / step", cfgMaxVelChangePerStep.Value, 0f, 2f, "0.00");
		cfgVelChangeGain.Value = DrawSlider("Response velocity", cfgVelChangeGain.Value, 1f, 40f, "0.0");
		cfgResponseStrength.Value = DrawSlider("Response force", cfgResponseStrength.Value, 0f, 15f, "0.00");
		cfgDeadband.Value = DrawSlider("Response deadzone", cfgDeadband.Value, 0f, 0.1f, "0.000");
		cfgMaxAccel.Value = DrawSlider("Max acceleration", cfgMaxAccel.Value, 0.5f, 120f, "0.0");
		cfgMaxSlewPerStep.Value = DrawSlider("Response curve / jitter", cfgMaxSlewPerStep.Value, 0f, 60f, "0.0");
		DrawSectionHeader("Motion / Proximity");
		cfgRequireSourceMotion.Value = GUILayout.Toggle(cfgRequireSourceMotion.Value, "Require source motion", new GUILayoutOption[0]);
		cfgMinSourceSpeed.Value = DrawSlider("Min source speed", cfgMinSourceSpeed.Value, 0f, 0.5f, "0.000");
		cfgMaxRelVel.Value = DrawSlider("Max relative speed", cfgMaxRelVel.Value, 0.1f, 6f, "0.00");
		cfgSmoothingSeconds.Value = DrawSlider("Smoothing seconds", cfgSmoothingSeconds.Value, 0.02f, 0.8f, "0.000");
		cfgUseProximity.Value = GUILayout.Toggle(cfgUseProximity.Value, "Use proximity gate", new GUILayoutOption[0]);
		cfgProximityActivation.Value = DrawSlider("Proximity activation (m)", cfgProximityActivation.Value, 0.01f, 2f, "0.00");
		DrawSectionHeader("Trip Safety");
		cfgCorridorRadius.Value = DrawSlider("Path corridor radius", cfgCorridorRadius.Value, 0.005f, 0.15f, "0.000");
		cfgExpectedSpeed.Value = DrawSlider("Expected speed", cfgExpectedSpeed.Value, 0.01f, 2f, "0.00");
		cfgSoftTripCooldown.Value = DrawSlider("Soft trip cooldown", cfgSoftTripCooldown.Value, 0.05f, 3f, "0.00");
		cfgHardDisableOnTrip.Value = GUILayout.Toggle(cfgHardDisableOnTrip.Value, "Hard disable on trip", new GUILayoutOption[0]);
		DrawSectionHeader("Low-Impact GUI");
		GUILayout.Box("Low-impact mode: GUI stays on; performance monitoring is removed. CounterThrust physics still runs every FixedUpdate.", statusStyle, new GUILayoutOption[0]);
		cfgAutoDpi.Value = GUILayout.Toggle(cfgAutoDpi.Value, "Automatic DPI scaling", new GUILayoutOption[0]);
		cfgUiScale.Value = DrawSlider("Manual UI scale", cfgUiScale.Value, 0.75f, 2f, "0.00");
		DrawSectionHeader("Current Person Pairs");
		if (controllers.Count == 0)
		{
			GUILayout.Box("No Person atoms detected yet.", statusStyle, new GUILayoutOption[0]);
		}
		else
		{
			for (int i = 0; i < controllerList.Count; i++)
			{
				PersonController personController = controllerList[i];
				if (personController != null)
				{
					GUILayout.Box(personController.CachedGuiStatus, statusStyle, new GUILayoutOption[0]);
				}
			}
		}
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
		GUILayout.EndArea();
		HandleResize();
		GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(40f, windowRect.width - 118f), 26f));
	}

	private void DrawSectionHeader(string text)
	{
		GUILayout.Space(5f);
		GUILayout.Box(text, sectionStyle, new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
	}

	private float DrawSlider(string label, float value, float min, float max, string format)
	{
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label(label, new GUILayoutOption[1] { GUILayout.Width(Mathf.Max(170f, windowRect.width * 0.38f)) });
		float result = GUILayout.HorizontalSlider(value, min, max, new GUILayoutOption[1] { GUILayout.MinWidth(100f) });
		GUILayout.Label(result.ToString(format), new GUILayoutOption[1] { GUILayout.Width(58f) });
		GUILayout.EndHorizontal();
		return result;
	}

	private string DrawChoice(string label, string value, string[] choices)
	{
		int num = IndexOfChoice(value, choices);
		if (num < 0)
		{
			num = 0;
		}
		GUILayout.BeginHorizontal(new GUILayoutOption[0]);
		GUILayout.Label(label, new GUILayoutOption[1] { GUILayout.Width(Mathf.Max(120f, windowRect.width * 0.28f)) });
		if (GUILayout.Button("<", new GUILayoutOption[1] { GUILayout.Width(28f) }))
		{
			num = (num - 1 + choices.Length) % choices.Length;
		}
		GUILayout.Box(choices[num], new GUILayoutOption[1] { GUILayout.ExpandWidth(true) });
		if (GUILayout.Button(">", new GUILayoutOption[1] { GUILayout.Width(28f) }))
		{
			num = (num + 1) % choices.Length;
		}
		GUILayout.EndHorizontal();
		return choices[num];
	}

	private int IndexOfChoice(string value, string[] choices)
	{
		for (int i = 0; i < choices.Length; i++)
		{
			if (string.Equals(value, choices[i], StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private void HandleResize()
	{
		Rect val = new Rect(windowRect.width - 20f, windowRect.height - 20f, 18f, 18f);
		GUI.Label(val, "↘");
		Event current = Event.current;
		if (current != null)
		{
			if ((int)current.type == 0 && current.button == 0 && val.Contains(current.mousePosition))
			{
				resizing = true;
				resizeStartMouse = current.mousePosition;
				resizeStartSize = new Vector2(windowRect.width, windowRect.height);
				current.Use();
			}
			else if (resizing && (int)current.type == 3 && current.button == 0)
			{
				Vector2 val2 = current.mousePosition - resizeStartMouse;
				float num = Mathf.Max(360f, (float)Screen.width / guiScaleCached - windowRect.x);
				float num2 = Mathf.Max(320f, (float)Screen.height / guiScaleCached - windowRect.y);
				windowRect.width = Mathf.Clamp(resizeStartSize.x + val2.x, 360f, num);
				windowRect.height = Mathf.Clamp(resizeStartSize.y + val2.y, 320f, num2);
				current.Use();
			}
			else if (resizing && (int)current.type == 1)
			{
				resizing = false;
			}
		}
	}

	private void OnDestroy()
	{
		try
		{
			SaveWindowPositionIfChanged();
			((BaseUnityPlugin)this).Config.Save();
		}
		catch
		{
		}
		controllers.Clear();
		controllerList.Clear();
	}

	public CounterThrustStandalonePlugin()
	{
	}

	private void SaveWindowPositionIfChanged()
	{
		float x = windowRect.x;
		float y = windowRect.y;
		if (!((cfgWindowX.Value == x) & (cfgWindowY.Value == y)))
		{
			cfgWindowX.Value = x;
			cfgWindowY.Value = y;
			((BaseUnityPlugin)this).Config.Save();
		}
	}
}
