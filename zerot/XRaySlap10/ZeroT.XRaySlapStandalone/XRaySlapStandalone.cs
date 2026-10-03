using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ZeroT.XRaySlapStandalone;

[BepInPlugin("zerot.xrayslapstandalone", "ZeroT XRay + Slap Standalone", "1.0.0")]
public sealed class XRaySlapStandalone : BaseUnityPlugin
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static Func<StandaloneXRayClient, bool> _003C_003E9__76_0;

		public static UnityAction _003C_003E9__110_10;

		public static Func<AudioClip, bool> _003C_003E9__119_0;

		internal bool _003CUpdateOverlayAvailability_003Eb__76_0(StandaloneXRayClient c)
		{
			return c?.IsVisible ?? false;
		}

		internal void _003CBuildWindow_003Eb__110_10()
		{
		}

		internal bool _003CUpdateStatusCounts_003Eb__119_0(AudioClip c)
		{
			return (Object)(object)c != (Object)null;
		}
	}

	public const string PluginGuid = "zerot.xrayslapstandalone";

	public const string PluginName = "ZeroT XRay + Slap Standalone";

	public const string PluginVersion = "1.0.0";

	internal const int XRayLayer = 18;

	internal static readonly int XRayMask = 262144;

	private readonly Dictionary<Atom, StandaloneXRayClient> xrayClients = new Dictionary<Atom, StandaloneXRayClient>();

	private readonly Dictionary<Rigidbody, Atom> rbOwners = new Dictionary<Rigidbody, Atom>();

	private readonly List<FemaleTarget> femaleTargets = new List<FemaleTarget>();

	private readonly Dictionary<Atom, Atom> activeTargets = new Dictionary<Atom, Atom>();

	private readonly List<ImpactProbe> probes = new List<ImpactProbe>();

	private readonly AudioClip[] slapClips = new AudioClip[16];

	private int slapPrev1 = -1;

	private int slapPrev2 = -1;

	private float nextScanTime;

	private float nextPenetrationCheck;

	private float globalImpactCooldown;

	private AudioSource slapAudio;

	private GameObject audioObject;

	private Camera overlayCamera;

	private Camera linkedCamera;

	private GameObject cameraContainer;

	private int linkedCameraOriginalMask;

	private bool linkedCameraMaskSaved;

	private Texture2D texFull;

	private Texture2D texHalf;

	private Texture2D texTip;

	private Texture2D texBalls;

	private bool shuttingDown;

	private ConfigEntry<bool> cfgXrayEnabled;

	private ConfigEntry<bool> cfgSlapEnabled;

	private ConfigEntry<bool> cfgAngle;

	private ConfigEntry<bool> cfgOcclusion;

	private ConfigEntry<float> cfgTransparency;

	private ConfigEntry<float> cfgRadius;

	private ConfigEntry<float> cfgSlapThreshold;

	private ConfigEntry<float> cfgSlapVolume;

	private ConfigEntry<string> cfgAlpha;

	private ZeroT.UiKit.RlUguiWindow win;

	private Canvas canvas;

	private RectTransform window;

	private Button xrayButton;

	private Button slapButton;

	private Button alphaButton;

	private Button angleButton;

	private Button occlusionButton;

	private Text status;

	private Text transparencyText;

	private Text radiusText;

	private Text thresholdText;

	private Text volumeText;

	private const float HeaderHeight = 31f;

	private const float CollapsedWidth = 365f;

	private const float MinWidth = 390f;

	private const float MinHeight = 330f;

	internal Camera OverlayCamera => overlayCamera;

	internal Camera MainCamera
	{
		get
		{
			if (!((Object)(object)linkedCamera != (Object)null))
			{
				return ResolveMainCamera();
			}
			return linkedCamera;
		}
	}

	internal float Transparency => Mathf.Clamp01(cfgTransparency.Value);

	internal bool AngleScaling => cfgAngle.Value;

	internal bool OcclusionScaling => cfgOcclusion.Value;

	private void Awake()
	{
		win = new ZeroT.UiKit.RlUguiWindow(this, "XRay + Slap Standalone", "Window", 40f, 80f, 475f, 420f, 390f, 330f, 1000f, 1000f);
		cfgXrayEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("XRay", "Enabled", true, "Enable BodyLanguage-style XRay rendering.");
		cfgAlpha = ((BaseUnityPlugin)this).Config.Bind<string>("XRay", "AlphaMask", "half", "tip, half, full, or withBalls.");
		cfgTransparency = ((BaseUnityPlugin)this).Config.Bind<float>("XRay", "Transparency", 0.4f, "XRay transparency from 0 to 1.");
		cfgAngle = ((BaseUnityPlugin)this).Config.Bind<bool>("XRay", "AngleScaling", true, "Match BodyLanguage angle scaling.");
		cfgOcclusion = ((BaseUnityPlugin)this).Config.Bind<bool>("XRay", "OcclusionScaling", true, "Match BodyLanguage occlusion scaling.");
		cfgRadius = ((BaseUnityPlugin)this).Config.Bind<float>("XRay", "PenetrationRadius", 0.06f, "Distance in meters used to detect penetration near female orifices.");
		cfgSlapEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>("Slaps", "Enabled", true, "Enable embedded SlapFX sounds on BodyLanguage-style impacts.");
		cfgSlapThreshold = ((BaseUnityPlugin)this).Config.Bind<float>("Slaps", "ImpactThreshold", 1f, "Minimum collision intensity.");
		cfgSlapVolume = ((BaseUnityPlugin)this).Config.Bind<float>("Slaps", "Volume", 1f, "Slap volume multiplier.");
	}

	private IEnumerator Start()
	{
		while ((Object)(object)SuperController.singleton == (Object)null || (Object)(object)EventSystem.current == (Object)null)
		{
			yield return null;
		}
		LoadEmbeddedAssets();
		CreateAudio();
		ConnectOverlayCamera();
		BuildWindow();
		SuperController singleton = SuperController.singleton;
		singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Combine((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		yield return null;
		RescanScene();
		SetStatus("Ready. XRay and SlapFX sounds are standalone.");
	}

	private void SceneLoaded()
	{
		((MonoBehaviour)this).StartCoroutine(SceneLoadedDelayed());
	}

	private IEnumerator SceneLoadedDelayed()
	{
		yield return null;
		yield return null;
		RescanScene();
	}

	private void Update()
	{
		if (shuttingDown)
		{
			return;
		}
		if (win != null)
		{
			win.Tick();
		}
		if ((Object)(object)overlayCamera != (Object)null && (Object)(object)linkedCamera != (Object)null)
		{
			overlayCamera.fieldOfView = linkedCamera.fieldOfView;
			overlayCamera.nearClipPlane = linkedCamera.nearClipPlane;
			overlayCamera.farClipPlane = linkedCamera.farClipPlane;
		}
		if (Time.unscaledTime >= nextScanTime)
		{
			nextScanTime = Time.unscaledTime + 2f;
			if ((Object)(object)SuperController.singleton != (Object)null && !SuperController.singleton.isLoading)
			{
				RefreshAtomsIncremental();
			}
		}
		if (Time.time >= nextPenetrationCheck)
		{
			nextPenetrationCheck = Time.time + 0.06f;
			UpdatePenetrationPairs();
		}
		UpdateOverlayAvailability();
	}

	private void UpdateOverlayAvailability()
	{
		if (!((Object)(object)overlayCamera == (Object)null))
		{
			bool flag = false;
			try
			{
				flag = ((Object)(object)SuperController.singleton.screenshotPreview != (Object)null && ((Component)SuperController.singleton.screenshotPreview).gameObject.activeInHierarchy) || ((Object)(object)SuperController.singleton.hiResScreenshotPreview != (Object)null && ((Component)SuperController.singleton.hiResScreenshotPreview).gameObject.activeInHierarchy);
			}
			catch
			{
			}
			bool flag2 = xrayClients.Values.Any((StandaloneXRayClient c) => c?.IsVisible ?? false);
			((Behaviour)overlayCamera).enabled = (cfgXrayEnabled.Value & flag2) && !flag;
		}
	}

	private void ConnectOverlayCamera()
	{
		Camera val = ResolveMainCamera();
		if (!((Object)(object)val == (Object)null) && (!((Object)(object)linkedCamera == (Object)(object)val) || !((Object)(object)overlayCamera != (Object)null)))
		{
			DisconnectOverlayCamera();
			linkedCamera = val;
			cameraContainer = new GameObject("ZeroT_XRayCameraContainer");
			cameraContainer.transform.SetParent(((Component)val).transform, false);
			overlayCamera = cameraContainer.AddComponent<Camera>();
			overlayCamera.CopyFrom(val);
			((Behaviour)overlayCamera).enabled = false;
			overlayCamera.clearFlags = (CameraClearFlags)3;
			overlayCamera.depth = val.depth + 1f;
			overlayCamera.cullingMask = XRayMask;
			linkedCameraOriginalMask = val.cullingMask;
			linkedCameraMaskSaved = true;
			val.cullingMask &= ~XRayMask;
		}
	}

	private Camera ResolveMainCamera()
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return Camera.main;
		}
		if (SuperController.singleton.isOpenVR)
		{
			return SuperController.singleton.ViveCenterCamera;
		}
		if (SuperController.singleton.isOVR)
		{
			return SuperController.singleton.OVRCenterCamera;
		}
		if (!((Object)(object)SuperController.singleton.MonitorCenterCamera != (Object)null))
		{
			return Camera.main;
		}
		return SuperController.singleton.MonitorCenterCamera;
	}

	private void DisconnectOverlayCamera()
	{
		if ((Object)(object)linkedCamera != (Object)null && linkedCameraMaskSaved)
		{
			linkedCamera.cullingMask = linkedCameraOriginalMask;
		}
		linkedCameraMaskSaved = false;
		linkedCamera = null;
		if ((Object)(object)cameraContainer != (Object)null)
		{
			Object.Destroy((Object)(object)cameraContainer);
		}
		cameraContainer = null;
		overlayCamera = null;
	}

	private void LoadEmbeddedAssets()
	{
		texFull = LoadTextureResource("ZeroT.XRaySlap.Resources.full.png");
		texHalf = LoadTextureResource("ZeroT.XRaySlap.Resources.half.png");
		texTip = LoadTextureResource("ZeroT.XRaySlap.Resources.tip.png");
		texBalls = LoadTextureResource("ZeroT.XRaySlap.Resources.withBalls.png");
		for (int i = 0; i < slapClips.Length; i++)
		{
			string text = $"ZeroT.XRaySlap.Resources.SexSlap{i + 1:D2}.wav";
			try
			{
				slapClips[i] = WavResource.Load(text, $"SexSlap{i + 1:D2}");
			}
			catch (Exception ex)
			{
				Logger.LogError((object)("Failed loading " + text + ": " + ex.Message));
			}
		}
	}

	private Texture2D LoadTextureResource(string name)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
		if (stream == null)
		{
			throw new Exception("Missing embedded resource " + name);
		}
		byte[] array = ReadAll(stream);
		Texture2D val = new Texture2D(2, 2, (TextureFormat)5, false);
		if (!ImageConversion.LoadImage(val, array))
		{
			throw new Exception("Could not decode " + name);
		}
		val.Apply();
		((Object)val).name = name;
		return val;
	}

	internal static byte[] ReadAll(Stream stream)
	{
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[8192];
		int count;
		while ((count = stream.Read(array, 0, array.Length)) > 0)
		{
			memoryStream.Write(array, 0, count);
		}
		return memoryStream.ToArray();
	}

	internal Texture2D CurrentAlphaTexture()
	{
		return (cfgAlpha.Value ?? "half").ToLowerInvariant() switch
		{
			"full" => texFull, 
			"tip" => texTip, 
			"withballs" => texBalls, 
			_ => texHalf, 
		};
	}

	private void CreateAudio()
	{
		audioObject = new GameObject("ZeroT_SlapAudio");
		Object.DontDestroyOnLoad((Object)(object)audioObject);
		slapAudio = audioObject.AddComponent<AudioSource>();
		slapAudio.playOnAwake = false;
		slapAudio.spatialBlend = 1f;
		slapAudio.spatialize = true;
		slapAudio.dopplerLevel = 0f;
		slapAudio.minDistance = 0.3f;
		slapAudio.maxDistance = 15f;
	}

	private void RescanScene()
	{
		ConnectOverlayCamera();
		foreach (ImpactProbe probe in probes)
		{
			if ((Object)(object)probe != (Object)null)
			{
				Object.Destroy((Object)(object)probe);
			}
		}
		probes.Clear();
		StandaloneXRayClient[] array = xrayClients.Values.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			array[i]?.DestroyClient();
		}
		xrayClients.Clear();
		rbOwners.Clear();
		femaleTargets.Clear();
		activeTargets.Clear();
		RefreshAtomsIncremental();
	}

	private void RefreshAtomsIncremental()
	{
		if ((Object)(object)SuperController.singleton == (Object)null)
		{
			return;
		}
		List<Atom> atoms = SuperController.singleton.GetAtoms();
		HashSet<Atom> live = new HashSet<Atom>();
		foreach (Atom atom in atoms)
		{
			if ((Object)(object)atom == (Object)null || atom.type != "Person")
			{
				continue;
			}
			live.Add(atom);
			JSONStorable storableByID = atom.GetStorableByID("geometry");
			DAZCharacterSelector val = (DAZCharacterSelector)(object)((storableByID is DAZCharacterSelector) ? storableByID : null);
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			Rigidbody[] componentsInChildren = ((Component)atom).GetComponentsInChildren<Rigidbody>(true);
			foreach (Rigidbody val2 in componentsInChildren)
			{
				if (!((Object)(object)val2 == (Object)null))
				{
					rbOwners[val2] = atom;
					if (ShouldProbeRegion(((Object)val2).name) && (Object)(object)((Component)val2).GetComponent<ImpactProbe>() == (Object)null)
					{
						ImpactProbe impactProbe = ((Component)val2).gameObject.AddComponent<ImpactProbe>();
						impactProbe.owner = this;
						impactProbe.atom = atom;
						impactProbe.region = ((Object)val2).name;
						probes.Add(impactProbe);
					}
				}
			}
			if ((int)val.gender == 1)
			{
				femaleTargets.RemoveAll((FemaleTarget t) => t != null && (Object)(object)t.atom == (Object)(object)atom);
				if (!xrayClients.ContainsKey(atom))
				{
					StandaloneXRayClient standaloneXRayClient = new StandaloneXRayClient(this, atom);
					xrayClients[atom] = standaloneXRayClient;
					((MonoBehaviour)this).StartCoroutine(standaloneXRayClient.Initialize());
				}
				else
				{
					xrayClients[atom].EnsureSkin();
				}
			}
			else if ((int)val.gender == 2)
			{
				if (xrayClients.TryGetValue(atom, out var value))
				{
					value?.DestroyClient();
					xrayClients.Remove(atom);
					activeTargets.Remove(atom);
				}
				if (!femaleTargets.Any((FemaleTarget t) => (Object)(object)t.atom == (Object)(object)atom))
				{
					femaleTargets.Add(new FemaleTarget(atom));
				}
			}
		}
		Atom[] array = xrayClients.Keys.ToArray();
		foreach (Atom val3 in array)
		{
			if (!live.Contains(val3) || (Object)(object)val3 == (Object)null)
			{
				xrayClients[val3]?.DestroyClient();
				xrayClients.Remove(val3);
				activeTargets.Remove(val3);
			}
		}
		femaleTargets.RemoveAll((FemaleTarget t) => t == null || (Object)(object)t.atom == (Object)null || !live.Contains(t.atom));
		UpdateStatusCounts();
	}

	private static bool ShouldProbeRegion(string n)
	{
		if (string.IsNullOrEmpty(n))
		{
			return false;
		}
		string text = n.ToLowerInvariant();
		if (!text.Contains("hand") && !text.Contains("forearm") && !text.Contains("carpal") && !text.Contains("pelvis") && !text.Contains("abdomen") && !text.Contains("thigh") && !text.Contains("glute") && !text.Contains("breast") && !text.Contains("chest") && !text.Contains("gen") && !text.Contains("test") && !text.Contains("labia") && !text.Contains("vagina") && !text.Contains("anus"))
		{
			return text.Contains("jointar");
		}
		return true;
	}

	private readonly List<KeyValuePair<Atom, StandaloneXRayClient>> pairBuffer = new List<KeyValuePair<Atom, StandaloneXRayClient>>();

	private void UpdatePenetrationPairs()
	{
		if (!cfgXrayEnabled.Value)
		{
			foreach (StandaloneXRayClient value3 in xrayClients.Values)
			{
				value3?.ShutDown();
			}
			activeTargets.Clear();
			return;
		}
		float num = Mathf.Clamp(cfgRadius.Value, 0.015f, 0.2f);
		float num2 = num * 1.55f;
		pairBuffer.Clear();
		foreach (KeyValuePair<Atom, StandaloneXRayClient> pairItem in xrayClients)
		{
			pairBuffer.Add(pairItem);
		}
		List<KeyValuePair<Atom, StandaloneXRayClient>> array = pairBuffer;
		for (int i = 0; i < array.Count; i++)
		{
			KeyValuePair<Atom, StandaloneXRayClient> keyValuePair = array[i];
			Atom key = keyValuePair.Key;
			StandaloneXRayClient value = keyValuePair.Value;
			if ((Object)(object)key == (Object)null || value == null || !value.Ready)
			{
				continue;
			}
			if (!value.TryGetTip(out var p))
			{
				value.ShutDown();
				continue;
			}
			FemaleTarget femaleTarget = null;
			float num3 = float.MaxValue;
			foreach (FemaleTarget femaleTarget2 in femaleTargets)
			{
				if (femaleTarget2 != null && !((Object)(object)femaleTarget2.atom == (Object)null) && !((Object)(object)femaleTarget2.atom == (Object)(object)key))
				{
					float num4 = femaleTarget2.ClosestDistance(p);
					if (num4 < num3)
					{
						num3 = num4;
						femaleTarget = femaleTarget2;
					}
				}
			}
			float num5 = (activeTargets.TryGetValue(key, out var _) ? num2 : num);
			if (femaleTarget != null && num3 <= num5)
			{
				activeTargets[key] = femaleTarget.atom;
				value.Enable(femaleTarget.atom, CurrentAlphaTexture(), Mathf.Clamp01(cfgTransparency.Value), cfgAngle.Value, cfgOcclusion.Value);
			}
			else
			{
				activeTargets.Remove(key);
				value.ShutDown();
			}
		}
	}

	internal Atom FindAtomForCollider(Collider col)
	{
		if ((Object)(object)col == (Object)null)
		{
			return null;
		}
		Rigidbody val = col.attachedRigidbody;
		if ((Object)(object)val == (Object)null)
		{
			val = ((Component)col).GetComponentInParent<Rigidbody>();
		}
		if ((Object)(object)val == (Object)null)
		{
			return null;
		}
		if (!rbOwners.TryGetValue(val, out var value))
		{
			return null;
		}
		return value;
	}

	internal bool IsActivePenetration(Atom a, Atom b)
	{
		if ((Object)(object)a == (Object)null || (Object)(object)b == (Object)null)
		{
			return false;
		}
		if (!activeTargets.TryGetValue(a, out var value) || !((Object)(object)value == (Object)(object)b))
		{
			if (activeTargets.TryGetValue(b, out value))
			{
				return (Object)(object)value == (Object)(object)a;
			}
			return false;
		}
		return true;
	}

	internal void HandleImpact(ImpactProbe probe, Collision collision)
	{
		if (!cfgSlapEnabled.Value || collision == null || (Object)(object)probe == (Object)null || (Object)(object)probe.atom == (Object)null || Time.time < globalImpactCooldown || (Object)(object)collision.rigidbody == (Object)null || collision.contacts == null || collision.contacts.Length == 0 || !rbOwners.TryGetValue(collision.rigidbody, out var value) || (Object)(object)value == (Object)null)
		{
			return;
		}
		ContactPoint val = collision.contacts[0];
		float num = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, val.normal));
		float num2 = Mathf.Clamp(cfgSlapThreshold.Value, 0.25f, 10f);
		if (!(num < num2))
		{
			string s = (probe.region ?? "").ToLowerInvariant();
			string s2 = (((Object)collision.rigidbody).name ?? "").ToLowerInvariant();
			bool flag = IsHandRegion(s);
			bool flag2 = IsHandRegion(s2);
			bool flag3 = IsSlapBodyRegion(s);
			bool flag4 = IsSlapBodyRegion(s2);
			bool num3 = (flag & flag4) || (flag2 & flag3);
			bool flag5 = IsActivePenetration(probe.atom, value) && (IsSexRegion(s) || IsSexRegion(s2));
			if (num3 || flag5)
			{
				globalImpactCooldown = Time.time + 0.07f;
				PlaySlap(val.point, num, num2);
			}
		}
	}

	private static bool IsHandRegion(string s)
	{
		if (!s.Contains("hand") && !s.Contains("forearm"))
		{
			return s.Contains("carpal");
		}
		return true;
	}

	private static bool IsSlapBodyRegion(string s)
	{
		if (!s.Contains("glute") && !s.Contains("thigh") && !s.Contains("pelvis") && !s.Contains("abdomen") && !s.Contains("breast") && !s.Contains("chest") && !s.Contains("labia") && !s.Contains("vagina") && !s.Contains("anus") && !s.Contains("gen"))
		{
			return s.Contains("test");
		}
		return true;
	}

	private static bool IsSexRegion(string s)
	{
		if (!s.Contains("pelvis") && !s.Contains("abdomen") && !s.Contains("thigh") && !s.Contains("glute") && !s.Contains("gen") && !s.Contains("test") && !s.Contains("labia") && !s.Contains("vagina"))
		{
			return s.Contains("anus");
		}
		return true;
	}

	private void PlaySlap(Vector3 position, float intensity, float threshold)
	{
		if ((Object)(object)slapAudio == (Object)null)
		{
			return;
		}
		List<int> list = new List<int>();
		for (int i = 0; i < slapClips.Length; i++)
		{
			if ((Object)(object)slapClips[i] != (Object)null && i != slapPrev1 && i != slapPrev2)
			{
				list.Add(i);
			}
		}
		if (list.Count == 0)
		{
			for (int j = 0; j < slapClips.Length; j++)
			{
				if ((Object)(object)slapClips[j] != (Object)null)
				{
					list.Add(j);
				}
			}
		}
		if (list.Count != 0)
		{
			int num = list[UnityEngine.Random.Range(0, list.Count)];
			slapPrev2 = slapPrev1;
			slapPrev1 = num;
			((Component)slapAudio).transform.position = position;
			float num2 = Mathf.Max(0f, intensity + 0.1f - threshold);
			float num3 = Mathf.Clamp01(num2 * num2 * 0.6f) * Mathf.Clamp(cfgSlapVolume.Value, 0f, 3f);
			slapAudio.pitch = Mathf.Lerp(0.75f, 1.2f, Mathf.Clamp01(intensity / 3f));
			slapAudio.PlayOneShot(slapClips[num], num3);
		}
	}

	internal void LogWarn(string message)
	{
		Logger.LogWarning((object)message);
	}

	private void OnDestroy()
	{
		shuttingDown = true;
		if ((Object)(object)SuperController.singleton != (Object)null)
		{
			SuperController singleton = SuperController.singleton;
			singleton.onSceneLoadedHandlers = (SuperController.OnSceneLoaded)Delegate.Remove((Delegate)(object)singleton.onSceneLoadedHandlers, (Delegate)new SuperController.OnSceneLoaded(SceneLoaded));
		}
		StandaloneXRayClient[] array = xrayClients.Values.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			array[i]?.DestroyClient();
		}
		xrayClients.Clear();
		foreach (ImpactProbe probe in probes)
		{
			if ((Object)(object)probe != (Object)null)
			{
				Object.Destroy((Object)(object)probe);
			}
		}
		probes.Clear();
		DisconnectOverlayCamera();
		if ((Object)(object)audioObject != (Object)null)
		{
			Object.Destroy((Object)(object)audioObject);
		}
		if (win != null)
		{
			win.Dispose();
		}
		AudioClip[] array2 = slapClips;
		foreach (AudioClip val in array2)
		{
			if ((Object)(object)val != (Object)null)
			{
				Object.Destroy((Object)(object)val);
			}
		}
		if ((Object)(object)texFull != (Object)null)
		{
			Object.Destroy((Object)(object)texFull);
		}
		if ((Object)(object)texHalf != (Object)null)
		{
			Object.Destroy((Object)(object)texHalf);
		}
		if ((Object)(object)texTip != (Object)null)
		{
			Object.Destroy((Object)(object)texTip);
		}
		if ((Object)(object)texBalls != (Object)null)
		{
			Object.Destroy((Object)(object)texBalls);
		}
	}

	private void OnDisable()
	{
		foreach (StandaloneXRayClient value in xrayClients.Values)
		{
			value?.ShutDown();
		}
		if (win != null)
		{
			win.SetActive(false);
		}
	}

	private void OnEnable()
	{
		if (win != null)
		{
			win.SetActive(true);
		}
	}

	private void BuildWindow()
	{
		win.OnLayout = delegate(float w, float h)
		{
			RefreshLabels();
		};
		win.Build("ZeroT XRay Slap Standalone UI");
		canvas = win.Canvas;
		canvas.sortingOrder = 30010;
		window = win.Body;
		xrayButton = NewButton("XRay Toggle", (Transform)(object)window, "", ToggleXRay);
		slapButton = NewButton("Slap Toggle", (Transform)(object)window, "", ToggleSlap);
		alphaButton = NewButton("Alpha", (Transform)(object)window, "", CycleAlpha);
		angleButton = NewButton("Angle", (Transform)(object)window, "", ToggleAngle);
		occlusionButton = NewButton("Occlusion", (Transform)(object)window, "", ToggleOcclusion);
		Button val11 = NewButton("Rescan", (Transform)(object)window, "Rescan atoms", RescanScene);
		SetRect(((Component)xrayButton).GetComponent<RectTransform>(), 8f, -42f, 145f, 30f);
		SetRect(((Component)slapButton).GetComponent<RectTransform>(), 160f, -42f, 145f, 30f);
		SetRect(((Component)val11).GetComponent<RectTransform>(), 312f, -42f, 145f, 30f);
		SetRect(((Component)alphaButton).GetComponent<RectTransform>(), 8f, -80f, 145f, 30f);
		SetRect(((Component)angleButton).GetComponent<RectTransform>(), 160f, -80f, 145f, 30f);
		SetRect(((Component)occlusionButton).GetComponent<RectTransform>(), 312f, -80f, 145f, 30f);
		MakeStepper("Transparency", 118f, () =>
		{
			cfgTransparency.Value = Mathf.Clamp01(cfgTransparency.Value - 0.05f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
			ApplySettingsNow();
		}, () =>
		{
			cfgTransparency.Value = Mathf.Clamp01(cfgTransparency.Value + 0.05f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
			ApplySettingsNow();
		}, out transparencyText);
		MakeStepper("XRay trigger radius", 158f, () =>
		{
			cfgRadius.Value = Mathf.Clamp(cfgRadius.Value - 0.005f, 0.015f, 0.2f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, () =>
		{
			cfgRadius.Value = Mathf.Clamp(cfgRadius.Value + 0.005f, 0.015f, 0.2f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, out radiusText);
		MakeStepper("Slap threshold", 198f, () =>
		{
			cfgSlapThreshold.Value = Mathf.Clamp(cfgSlapThreshold.Value - 0.1f, 0.25f, 10f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, () =>
		{
			cfgSlapThreshold.Value = Mathf.Clamp(cfgSlapThreshold.Value + 0.1f, 0.25f, 10f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, out thresholdText);
		MakeStepper("Slap volume", 238f, () =>
		{
			cfgSlapVolume.Value = Mathf.Clamp(cfgSlapVolume.Value - 0.1f, 0f, 3f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, () =>
		{
			cfgSlapVolume.Value = Mathf.Clamp(cfgSlapVolume.Value + 0.1f, 0f, 3f);
			((BaseUnityPlugin)this).Config.Save();
			RefreshLabels();
		}, out volumeText);
		status = NewText("Status", (Transform)(object)window, "Starting...", 12, (TextAnchor)0);
		SetRect(((Graphic)status).rectTransform, 10f, -282f, 440f, 72f);
		win.AddResizeHandle();
		ApplyGeometry();
		RefreshLabels();
	}

	private void ApplyGeometry()
	{
		if (win != null)
		{
			win.Apply();
		}
	}

	private void MakeStepper(string label, float top, UnityAction minus, UnityAction plus, out Text value)
	{
		SetRect(((Graphic)NewText(label + " label", (Transform)(object)window, label, 13, (TextAnchor)3)).rectTransform, 12f, 0f - top, 185f, 30f);
		SetRect(((Component)NewButton(label + " minus", (Transform)(object)window, "−", minus)).GetComponent<RectTransform>(), 204f, 0f - top, 42f, 30f);
		value = NewText(label + " value", (Transform)(object)window, "", 13, (TextAnchor)4);
		SetRect(((Graphic)value).rectTransform, 252f, 0f - top, 118f, 30f);
		SetRect(((Component)NewButton(label + " plus", (Transform)(object)window, "+", plus)).GetComponent<RectTransform>(), 376f, 0f - top, 42f, 30f);
	}

	private void ToggleXRay()
	{
		cfgXrayEnabled.Value = !cfgXrayEnabled.Value;
		((BaseUnityPlugin)this).Config.Save();
		if (!cfgXrayEnabled.Value)
		{
			foreach (StandaloneXRayClient value in xrayClients.Values)
			{
				value.ShutDown();
			}
		}
		RefreshLabels();
	}

	private void ToggleSlap()
	{
		cfgSlapEnabled.Value = !cfgSlapEnabled.Value;
		((BaseUnityPlugin)this).Config.Save();
		RefreshLabels();
	}

	private void ToggleAngle()
	{
		cfgAngle.Value = !cfgAngle.Value;
		((BaseUnityPlugin)this).Config.Save();
		RefreshLabels();
		ApplySettingsNow();
	}

	private void ToggleOcclusion()
	{
		cfgOcclusion.Value = !cfgOcclusion.Value;
		((BaseUnityPlugin)this).Config.Save();
		RefreshLabels();
		ApplySettingsNow();
	}

	private void CycleAlpha()
	{
		string text = (cfgAlpha.Value ?? "half").ToLowerInvariant();
		cfgAlpha.Value = text switch
		{
			"full" => "withBalls", 
			"half" => "full", 
			"tip" => "half", 
			_ => "tip", 
		};
		((BaseUnityPlugin)this).Config.Save();
		RefreshLabels();
		ApplySettingsNow();
	}

	private void ApplySettingsNow()
	{
		foreach (KeyValuePair<Atom, StandaloneXRayClient> xrayClient in xrayClients)
		{
			if (activeTargets.TryGetValue(xrayClient.Key, out var value))
			{
				xrayClient.Value.Enable(value, CurrentAlphaTexture(), cfgTransparency.Value, cfgAngle.Value, cfgOcclusion.Value);
			}
		}
	}

	private void RefreshLabels()
	{
		if (!((Object)(object)xrayButton == (Object)null))
		{
			SetLabel(xrayButton, "XRay: " + (cfgXrayEnabled.Value ? "ON" : "OFF"));
			SetLabel(slapButton, "Slaps: " + (cfgSlapEnabled.Value ? "ON" : "OFF"));
			SetLabel(alphaButton, "Mask: " + cfgAlpha.Value);
			SetLabel(angleButton, "Angle: " + (cfgAngle.Value ? "ON" : "OFF"));
			SetLabel(occlusionButton, "Occlusion: " + (cfgOcclusion.Value ? "ON" : "OFF"));
			if ((Object)(object)transparencyText != (Object)null)
			{
				transparencyText.text = cfgTransparency.Value.ToString("0.00");
			}
			if ((Object)(object)radiusText != (Object)null)
			{
				radiusText.text = cfgRadius.Value.ToString("0.000") + " m";
			}
			if ((Object)(object)thresholdText != (Object)null)
			{
				thresholdText.text = cfgSlapThreshold.Value.ToString("0.00");
			}
			if ((Object)(object)volumeText != (Object)null)
			{
				volumeText.text = cfgSlapVolume.Value.ToString("0.00");
			}
		}
	}

	private void UpdateStatusCounts()
	{
		int num = slapClips.Count((AudioClip c) => (Object)(object)c != (Object)null);
		SetStatus("Male XRay sources: " + xrayClients.Count + "   Female targets: " + femaleTargets.Count + "\nEmbedded SlapFX clips: " + num + "/16   Active pairs: " + activeTargets.Count + "\nDrag title bar • resize bottom-right • S-/S+ changes DPI-aware scale");
	}

	private void SetStatus(string s)
	{
		if ((Object)(object)status != (Object)null)
		{
			status.text = s;
		}
	}

	private static RectTransform NewRect(string name, Transform parent)
	{
		GameObject val = new GameObject(name, new Type[1] { typeof(RectTransform) });
		val.transform.SetParent(parent, false);
		return (RectTransform)val.transform;
	}

	private static Text NewText(string name, Transform parent, string value, int size, TextAnchor align)
	{
		Text val = ((Component)NewRect(name, parent)).gameObject.AddComponent<Text>();
		val.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		val.fontSize = size;
		val.alignment = align;
		((Graphic)val).color = ZeroT.UiKit.RlUguiWindow.TextCol;
		val.text = value;
		((Graphic)val).raycastTarget = false;
		return val;
	}

	private static Button NewButton(string name, Transform parent, string label, UnityAction action)
	{
		RectTransform val = NewRect(name, parent);
		Image val2 = ((Component)val).gameObject.AddComponent<Image>();
		((Graphic)val2).color = Color.white;
		Button val3 = ((Component)val).gameObject.AddComponent<Button>();
		((Selectable)val3).targetGraphic = (Graphic)(object)val2;
		ColorBlock rlColors = val3.colors;
		rlColors.normalColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		rlColors.highlightedColor = ZeroT.UiKit.RlUguiWindow.ButtonH;
		rlColors.pressedColor = ZeroT.UiKit.RlUguiWindow.ButtonP;
		rlColors.disabledColor = ZeroT.UiKit.RlUguiWindow.ButtonN;
		val3.colors = rlColors;
		Navigation rlNav = new Navigation();
		rlNav.mode = Navigation.Mode.None;
		val3.navigation = rlNav;
		((UnityEvent)val3.onClick).AddListener(action);
		Text val4 = NewText("Label", (Transform)(object)val, label, 13, (TextAnchor)4);
		((Graphic)val4).rectTransform.anchorMin = Vector2.zero;
		((Graphic)val4).rectTransform.anchorMax = Vector2.one;
		((Graphic)val4).rectTransform.offsetMin = new Vector2(2f, 0f);
		((Graphic)val4).rectTransform.offsetMax = new Vector2(-2f, 0f);
		return val3;
	}

	private static void SetLabel(Button b, string value)
	{
		Text componentInChildren = ((Component)b).GetComponentInChildren<Text>();
		if ((Object)(object)componentInChildren != (Object)null)
		{
			componentInChildren.text = value;
		}
	}

	private static void SetRect(RectTransform r, float left, float top, float w, float h)
	{
		if (!((Object)(object)r == (Object)null))
		{
			Vector2 val = (r.pivot = new Vector2(0f, 1f));
			Vector2 anchorMin = (r.anchorMax = val);
			r.anchorMin = anchorMin;
			r.anchoredPosition = new Vector2(left, top);
			r.sizeDelta = new Vector2(w, h);
		}
	}

	private static void PinTopRight(RectTransform r, float right, float top, float w, float h)
	{
		Vector2 val = (r.pivot = new Vector2(1f, 1f));
		Vector2 anchorMin = (r.anchorMax = val);
		r.anchorMin = anchorMin;
		r.anchoredPosition = new Vector2(0f - right, 0f - top);
		r.sizeDelta = new Vector2(w, h);
	}

}
