using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZeroT.ClothingBackfaceStandalone
{
	// Headless standalone port of Nial's "Clothing Backface" (.var, a per-Person plugin). Nothing to add to a person and no window:
	// it finds every Person atom by itself.
	//
	// What it does (all of it from the original)
	//  - the plugin's assetbundle (inside this DLL) holds the shader "Nial/VaM/Clothing Backface Native TankTop". Every material of an
	//    active clothing item that uses VaM's "Custom/Subsurface/TransparentGlossNMNoCullSeparateAlphaComputeBuff" (the no-cull clothing
	//    shader) is switched to it: the BACK faces then draw darker instead of as bright as the front. _BackBrightness 0..1, default 0.28.
	//  - the material's render queue is kept; if the material had no specular map a black one is put in (and taken out again on restore)
	//  - one profile per clothing item: enabled (default on) and brightness (default 0.28). Items are told apart by their position under the
	//    person, duplicates numbered "(1)", "(2)"; an older profile saved under the item's plain name is still found
	//  - the shader is loaded up to 5 times, 1.5 s apart, and must be supported by the graphics card
	//  - the clothing is rescanned every 0.75 s; when items, their wraps or their materials change, the profiles are re-applied
	//  - controls: on every person a storable "ZeroT.ClothingBackface" with the original's controls - actions Apply (re-apply, leaves the
	//    manual restore mode) and Restore (back to VaM's shaders and stay there), a toggle "<clothing>" and a slider "<clothing> Brightness"
	//    per clothing item, and the string "ClothingBackface.Profiles" in the original's format ("base64 id:enabled:brightness x 10000;...",
	//    the old JSON form is read too)
	//  - everything is undone when a person is switched off, or when this plugin is
	//  - only one patcher per person: a person that has Nial's plugin on it is left to it
	// Profiles are saved to BepInEx\config\ZeroT.ClothingBackface.profiles.txt (one line per person atom) and come back with the scene.
	[BepInPlugin("com.zerot.clothingbackface", "ClothingBackface Standalone", "1.0.0")]
	public sealed class ClothingBackfaceStandalone : BaseUnityPlugin
	{
		private const string BundleResource = "nialclothingbackface.assetbundle";
		private const string ShaderAssetName = "assets/nialclothingbackface/nialclothingbackface.shader";
		private const string ShaderName = "Nial/VaM/Clothing Backface Native TankTop";
		private const string SupportedSourceShader = "Custom/Subsurface/TransparentGlossNMNoCullSeparateAlphaComputeBuff";
		private const string ProfilesKey = "ClothingBackface.Profiles";
		internal const string StoreId = "ZeroT.ClothingBackface";
		private const float MinBrightness = 0f;
		private const float MaxBrightness = 1f;
		private const float DefaultBrightness = 0.28f;
		private const int BrightnessScale = 10000;

		[Serializable]
		private class ClothingProfile
		{
			public string id;
			public bool enabled = true;
			public float brightness = DefaultBrightness;
		}

		[Serializable]
		private class ProfileList
		{
			public List<ClothingProfile> profiles = new List<ClothingProfile>();
		}

		private sealed class PatchedMaterial
		{
			public Material material;
			public Shader originalShader;
			public int originalRenderQueue;
			public string clothingId;
			public bool injectedBlackSpecTex;
		}

		private sealed class PersonState
		{
			public Atom atom;
			public JSONStorable host;
			public JSONStorableString profilesJSON;
			public readonly List<PatchedMaterial> patched = new List<PatchedMaterial>();
			public readonly List<DAZSkinWrap> patchedWraps = new List<DAZSkinWrap>();
			public readonly List<JSONStorableBool> dynBools = new List<JSONStorableBool>();
			public readonly List<JSONStorableFloat> dynFloats = new List<JSONStorableFloat>();
			public readonly Dictionary<string, bool> runtimeEnabled = new Dictionary<string, bool>();
			public readonly Dictionary<string, float> runtimeBrightness = new Dictionary<string, float>();
			public readonly Dictionary<string, string> runtimeBaseNames = new Dictionary<string, string>();
			public bool suppressProfileCallback;
			public bool buildingDynamicUI;
			public bool manualRestoreMode;
			public float nextScanTime = -1f;
			public string lastSignature = "";
			public float saveAt = -1f;
		}

		private ConfigEntry<bool> cfgActive, cfgMales;
		private ConfigEntry<float> cfgScan, cfgRetry;
		private ConfigEntry<int> cfgAttempts;
		private ConfigEntry<bool> cfgDefaultEnabled;
		private ConfigEntry<float> cfgDefaultBrightness;

		private readonly Dictionary<Atom, PersonState> states = new Dictionary<Atom, PersonState>();
		private Shader backfaceShader;
		private AssetBundle bundle;
		private int shaderLoadAttempts;
		private float nextShaderLoadRetryTime = -1f;
		private float nextDiscover, nextOriginalCheck;
		private Dictionary<string, string> savedProfiles;
		private bool wasActive = true;

		private string ProfilesFile { get { return Path.Combine(Paths.ConfigPath, "ZeroT.ClothingBackface.profiles.txt"); } }

		private void Awake()
		{
			cfgActive = Config.Bind<bool>("General", "Active", true, "Turn everything off (every material goes back to VaM's shader).");
			cfgMales = Config.Bind<bool>("General", "IncludeMales", true, "Also male Person atoms (the original works on any Person).");
			cfgScan = Config.Bind<float>("General", "ScanIntervalSeconds", 0.75f, "How often the clothing is checked for changes (0.2 - 5).");
			cfgDefaultEnabled = Config.Bind<bool>("Defaults", "ClothingEnabled", true, "A clothing item with no profile yet gets the effect.");
			cfgDefaultBrightness = Config.Bind<float>("Defaults", "Brightness", DefaultBrightness, "Back-face brightness for a clothing item with no profile yet (0 - 1).");
			cfgAttempts = Config.Bind<int>("Shader", "MaxLoadAttempts", 5, "How many times the shader is loaded before giving up.");
			cfgRetry = Config.Bind<float>("Shader", "RetryDelaySeconds", 1.5f, "Seconds between shader load attempts.");
		}

		private void OnDestroy()
		{
			RestoreEverything();
			if (bundle != null)
			{
				bundle.Unload(false);
				bundle = null;
			}
		}

		// ------------------------------------------------------------------ shader (as in the original)

		private void EnsureShader()
		{
			if (backfaceShader != null || Time.time < nextShaderLoadRetryTime || shaderLoadAttempts >= Mathf.Max(1, cfgAttempts.Value))
			{
				return;
			}
			try
			{
				if (bundle == null)
				{
					using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundleResource))
					{
						if (s == null)
						{
							ScheduleRetry("the shader bundle is missing from the DLL");
							return;
						}
						byte[] data = new byte[s.Length];
						int read = 0;
						while (read < data.Length)
						{
							int n = s.Read(data, read, data.Length - read);
							if (n <= 0)
							{
								break;
							}
							read += n;
						}
						bundle = AssetBundle.LoadFromMemory(data);
					}
				}
				if (bundle == null)
				{
					ScheduleRetry("AssetBundle was not valid");
					return;
				}
				Shader shader = bundle.LoadAsset<Shader>(ShaderAssetName);
				if (shader == null)
				{
					Shader[] shaders = bundle.LoadAllAssets<Shader>();
					for (int i = 0; shaders != null && i < shaders.Length; i++)
					{
						if (shaders[i] != null && shaders[i].name == ShaderName)
						{
							shader = shaders[i];
							break;
						}
					}
				}
				if (shader == null || !shader.isSupported)
				{
					bundle.Unload(false);
					bundle = null;
					ScheduleRetry("shader '" + ShaderName + "' is missing or unsupported");
					return;
				}
				backfaceShader = shader;
				shaderLoadAttempts = 0;
				nextShaderLoadRetryTime = -1f;
				Logger.LogInfo("Backface shader loaded.");
				foreach (PersonState st in states.Values)
				{
					RefreshClothingUI(st);
					ApplyAllFromProfiles(st);
					st.lastSignature = ComputeSignature(st);
					st.nextScanTime = Time.time + Mathf.Clamp(cfgScan.Value, 0.2f, 5f);
				}
			}
			catch (Exception e)
			{
				if (bundle != null)
				{
					bundle.Unload(false);
					bundle = null;
				}
				backfaceShader = null;
				ScheduleRetry("AssetBundle failed: " + e.Message);
			}
		}

		private void ScheduleRetry(string reason)
		{
			shaderLoadAttempts++;
			if (shaderLoadAttempts < Mathf.Max(1, cfgAttempts.Value))
			{
				nextShaderLoadRetryTime = Time.time + Mathf.Max(0.1f, cfgRetry.Value);
			}
			else
			{
				Logger.LogError("ClothingBackface: " + reason + ". Failed after " + shaderLoadAttempts + " attempts.");
				nextShaderLoadRetryTime = -1f;
			}
		}

		// ------------------------------------------------------------------ people

		private static bool IsMale(Atom atom)
		{
			try
			{
				DAZCharacterSelector cs = atom.GetStorableByID("geometry") as DAZCharacterSelector;
				return cs != null && cs.gender == DAZCharacterSelector.Gender.Male;
			}
			catch
			{
				return false;
			}
		}

		private static bool HasOriginalPlugin(Atom atom)
		{
			try
			{
				MonoBehaviour[] all = atom.GetComponentsInChildren<MonoBehaviour>(true);
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i] != null && all[i].GetType().Name == "ClothingBackface" && all[i].GetType().Namespace == null)
					{
						return true;
					}
				}
			}
			catch
			{
			}
			return false;
		}

		private void LoadSaved()
		{
			savedProfiles = new Dictionary<string, string>();
			try
			{
				if (File.Exists(ProfilesFile))
				{
					foreach (string line in File.ReadAllLines(ProfilesFile))
					{
						int t = line.IndexOf('\t');
						if (t > 0)
						{
							savedProfiles[line.Substring(0, t)] = line.Substring(t + 1);
						}
					}
				}
			}
			catch
			{
			}
		}

		private void WriteSaved()
		{
			try
			{
				List<string> lines = new List<string>();
				foreach (KeyValuePair<string, string> kv in savedProfiles)
				{
					if (!string.IsNullOrEmpty(kv.Value))
					{
						lines.Add(kv.Key + "\t" + kv.Value);
					}
				}
				Directory.CreateDirectory(Paths.ConfigPath);
				File.WriteAllLines(ProfilesFile, lines.ToArray());
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not save the profiles: " + e.Message);
			}
		}

		private void AddPerson(Atom atom)
		{
			PersonState st = new PersonState();
			st.atom = atom;
			states[atom] = st;
			GameObject go = new GameObject(StoreId);
			go.transform.SetParent(atom.transform, false);
			st.host = go.AddComponent<JSONStorable>();
			st.host.overrideId = StoreId;
			st.host.exclude = true;
			st.host.containingAtom = atom;
			string saved;
			if (savedProfiles == null)
			{
				LoadSaved();
			}
			savedProfiles.TryGetValue(atom.uid, out saved);
			PersonState captured = st;
			st.profilesJSON = new JSONStorableString(ProfilesKey, saved ?? "", delegate (string v)
			{
				if (!captured.suppressProfileCallback)
				{
					captured.lastSignature = "";
					captured.saveAt = Time.unscaledTime + 1f;
				}
			});
			st.host.RegisterString(st.profilesJSON);
			st.host.RegisterAction(new JSONStorableAction("Apply", delegate { ApplyAndRefresh(captured); }));
			st.host.RegisterAction(new JSONStorableAction("Restore", delegate { ManualRestore(captured); }));
			if (!atom.RegisterAdditionalStorable(st.host))
			{
				Logger.LogWarning("VaM did not accept the storable on " + atom.uid + "; the effect still works.");
			}
			Logger.LogInfo("ClothingBackface on " + atom.uid + ".");
			if (backfaceShader != null)
			{
				RefreshClothingUI(st);
				ApplyAllFromProfiles(st);
				st.lastSignature = ComputeSignature(st);
				st.nextScanTime = Time.time + Mathf.Clamp(cfgScan.Value, 0.2f, 5f);
			}
		}

		private void DropPerson(PersonState st, bool restore)
		{
			if (restore)
			{
				RestoreOriginalMaterials(st);
			}
			if (st.host != null)
			{
				UnityEngine.Object.Destroy(st.host.gameObject);
			}
			states.Remove(st.atom);
		}

		private void RestoreEverything()
		{
			foreach (PersonState st in new List<PersonState>(states.Values))
			{
				DropPerson(st, true);
			}
		}

		// ------------------------------------------------------------------ loop

		private void Update()
		{
			bool active = cfgActive.Value;
			if (!active)
			{
				if (wasActive)
				{
					RestoreEverything();
				}
				wasActive = false;
				return;
			}
			wasActive = true;
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			if (backfaceShader == null)
			{
				EnsureShader();
			}
			if (Time.unscaledTime >= nextDiscover)
			{
				nextDiscover = Time.unscaledTime + 1f;
				bool checkOriginal = Time.unscaledTime >= nextOriginalCheck;
				if (checkOriginal)
				{
					nextOriginalCheck = Time.unscaledTime + 3f;
				}
				List<Atom> atoms = sc.GetAtoms();
				List<Atom> gone = new List<Atom>();
				foreach (Atom a in states.Keys)
				{
					if (a == null || !a || !atoms.Contains(a))
					{
						gone.Add(a);
					}
				}
				foreach (Atom a in gone)
				{
					PersonState st = states[a];
					DropPerson(st, a != null && a);
				}
				for (int i = 0; i < atoms.Count; i++)
				{
					Atom a = atoms[i];
					if (a == null || a.type != "Person")
					{
						continue;
					}
					if (!cfgMales.Value && IsMale(a))
					{
						if (states.ContainsKey(a))
						{
							DropPerson(states[a], true);
						}
						continue;
					}
					bool has = states.ContainsKey(a);
					if (!has || checkOriginal)
					{
						if (HasOriginalPlugin(a))
						{
							if (has)
							{
								Logger.LogWarning("Nial's ClothingBackface was added to " + a.uid + ": this one steps aside there.");
								DropPerson(states[a], true);
							}
							continue;
						}
					}
					if (!has)
					{
						AddPerson(a);
					}
				}
			}
			if (backfaceShader == null)
			{
				return;
			}
			float now = Time.time;
			float interval = Mathf.Clamp(cfgScan.Value, 0.2f, 5f);
			foreach (PersonState st in new List<PersonState>(states.Values))
			{
				if (st.saveAt > 0f && Time.unscaledTime >= st.saveAt)
				{
					st.saveAt = -1f;
					if (savedProfiles == null)
					{
						LoadSaved();
					}
					savedProfiles[st.atom.uid] = st.profilesJSON.val;
					WriteSaved();
				}
				if (st.manualRestoreMode)
				{
					continue;
				}
				if (now >= st.nextScanTime && !st.buildingDynamicUI)
				{
					st.nextScanTime = now + interval;
					string sig = ComputeSignature(st);
					if (sig != st.lastSignature)
					{
						RefreshClothingUI(st);
						ApplyAllFromProfiles(st);
						st.lastSignature = ComputeSignature(st);
					}
				}
			}
		}

		private void ApplyAndRefresh(PersonState st)
		{
			st.manualRestoreMode = false;
			shaderLoadAttempts = 0;
			nextShaderLoadRetryTime = -1f;
			RefreshClothingUI(st);
			EnsureShader();
			if (backfaceShader != null)
			{
				ApplyAllFromProfiles(st);
			}
			st.lastSignature = ComputeSignature(st);
		}

		private void ManualRestore(PersonState st)
		{
			st.manualRestoreMode = true;
			RestoreOriginalMaterials(st);
			st.lastSignature = ComputeSignature(st);
		}

		// ------------------------------------------------------------------ clothing scan

		private static string ClothingPathKey(PersonState st, DAZClothingItem clothing)
		{
			try
			{
				if (clothing == null || clothing.transform == null)
				{
					return null;
				}
				Transform root = st.atom != null ? st.atom.transform : null;
				Transform t = clothing.transform;
				string path = t.name;
				while (t.parent != null && t.parent != root)
				{
					t = t.parent;
					path = t.name + "/" + path;
				}
				return path;
			}
			catch
			{
				return null;
			}
		}

		private static string DisplayName(DAZClothingItem clothing)
		{
			if (clothing == null)
			{
				return "Clothing";
			}
			if (!string.IsNullOrEmpty(clothing.displayName))
			{
				return clothing.displayName;
			}
			if (!string.IsNullOrEmpty(clothing.name))
			{
				return clothing.name;
			}
			return "Clothing";
		}

		private string ComputeSignature(PersonState st)
		{
			StringBuilder sb = new StringBuilder();
			if (st.atom == null)
			{
				return "";
			}
			DAZClothingItem[] items = st.atom.GetComponentsInChildren<DAZClothingItem>(true);
			foreach (DAZClothingItem c in items)
			{
				if (c == null || c.containingAtom != st.atom)
				{
					continue;
				}
				sb.Append(c.gameObject.activeInHierarchy ? "A" : "I");
				sb.Append(ClothingPathKey(st, c));
				DAZSkinWrap[] wraps = c.GetComponentsInChildren<DAZSkinWrap>(true);
				foreach (DAZSkinWrap w in wraps)
				{
					if (w == null)
					{
						continue;
					}
					sb.Append("|w");
					sb.Append(w.gameObject.activeInHierarchy ? "A" : "I");
					sb.Append(w.GPUuseSimpleMaterial ? "S" : "M");
					if (w.GPUuseSimpleMaterial)
					{
						if (w.GPUsimpleMaterial != null)
						{
							sb.Append("|m").Append(w.GPUsimpleMaterial.GetInstanceID());
							if (w.GPUsimpleMaterial.shader != null)
							{
								sb.Append("s").Append(w.GPUsimpleMaterial.shader.GetInstanceID());
							}
						}
					}
					else if (w.GPUmaterials != null)
					{
						for (int i = 0; i < w.GPUmaterials.Length; i++)
						{
							bool materialEnabled = w.materialsEnabled == null || i >= w.materialsEnabled.Length || w.materialsEnabled[i];
							sb.Append(materialEnabled ? "|E" : "|D");
							if (w.GPUmaterials[i] != null)
							{
								sb.Append("m").Append(w.GPUmaterials[i].GetInstanceID());
								if (w.GPUmaterials[i].shader != null)
								{
									sb.Append("s").Append(w.GPUmaterials[i].shader.GetInstanceID());
								}
							}
						}
					}
				}
				sb.Append(";");
			}
			return sb.ToString();
		}

		private void CollectClothing(PersonState st, List<DAZClothingItem> clothes, List<string> keys, List<string> labels, List<string> baseNames)
		{
			clothes.Clear();
			keys.Clear();
			labels.Clear();
			baseNames.Clear();
			if (st.atom == null)
			{
				return;
			}
			DAZClothingItem[] items = st.atom.GetComponentsInChildren<DAZClothingItem>(true);
			Dictionary<string, int> totalKeys = new Dictionary<string, int>();
			Dictionary<string, int> totalLabels = new Dictionary<string, int>();
			List<string> rawKeys = new List<string>();
			List<string> rawLabels = new List<string>();
			foreach (DAZClothingItem c in items)
			{
				if (c == null || c.containingAtom != st.atom)
				{
					continue;
				}
				string pathKey = ClothingPathKey(st, c);
				if (string.IsNullOrEmpty(pathKey))
				{
					pathKey = DisplayName(c);
				}
				string baseName = DisplayName(c);
				int n;
				totalKeys[pathKey] = totalKeys.TryGetValue(pathKey, out n) ? n + 1 : 1;
				totalLabels[baseName] = totalLabels.TryGetValue(baseName, out n) ? n + 1 : 1;
				clothes.Add(c);
				rawKeys.Add(pathKey);
				rawLabels.Add(baseName);
				baseNames.Add(baseName);
			}
			Dictionary<string, int> seenKeys = new Dictionary<string, int>();
			Dictionary<string, int> seenLabels = new Dictionary<string, int>();
			for (int i = 0; i < clothes.Count; i++)
			{
				string k = rawKeys[i];
				int s;
				if (totalKeys[k] > 1)
				{
					seenKeys[k] = seenKeys.TryGetValue(k, out s) ? s + 1 : 1;
					k = k + " (" + seenKeys[k] + ")";
				}
				keys.Add(k);
				string l = rawLabels[i];
				if (totalLabels[l] > 1)
				{
					seenLabels[l] = seenLabels.TryGetValue(l, out s) ? s + 1 : 1;
					l = l + " (" + seenLabels[l] + ")";
				}
				labels.Add(l);
			}
		}

		// ------------------------------------------------------------------ profiles (same format as the original)

		private ProfileList LoadProfiles(PersonState st)
		{
			ProfileList result = new ProfileList();
			string data = st.profilesJSON == null ? string.Empty : st.profilesJSON.val;
			if (string.IsNullOrEmpty(data))
			{
				return result;
			}
			if (data.StartsWith("{"))
			{
				try
				{
					ProfileList legacy = JsonUtility.FromJson<ProfileList>(data);
					if (legacy != null)
					{
						if (legacy.profiles == null)
						{
							legacy.profiles = new List<ClothingProfile>();
						}
						return legacy;
					}
				}
				catch
				{
				}
			}
			string[] entries = data.Split(';');
			for (int i = 0; i < entries.Length; i++)
			{
				string entry = entries[i];
				if (string.IsNullOrEmpty(entry))
				{
					continue;
				}
				string[] parts = entry.Split(':');
				if (parts.Length != 3)
				{
					continue;
				}
				string id = DecodeId(parts[0]);
				if (string.IsNullOrEmpty(id))
				{
					continue;
				}
				int enabledInt = 0, brightnessInt = 0;
				int.TryParse(parts[1], out enabledInt);
				int.TryParse(parts[2], out brightnessInt);
				ClothingProfile p = new ClothingProfile();
				p.id = id;
				p.enabled = enabledInt != 0;
				p.brightness = Mathf.Clamp(brightnessInt / (float)BrightnessScale, MinBrightness, MaxBrightness);
				AddOrReplace(result, p);
			}
			return result;
		}

		private void SaveProfiles(PersonState st, ProfileList list)
		{
			if (st.profilesJSON == null)
			{
				return;
			}
			if (list == null)
			{
				list = new ProfileList();
			}
			if (list.profiles == null)
			{
				list.profiles = new List<ClothingProfile>();
			}
			ProfileList unique = new ProfileList();
			for (int i = 0; i < list.profiles.Count; i++)
			{
				if (list.profiles[i] == null || string.IsNullOrEmpty(list.profiles[i].id))
				{
					continue;
				}
				AddOrReplace(unique, list.profiles[i]);
			}
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < unique.profiles.Count; i++)
			{
				ClothingProfile p = unique.profiles[i];
				if (sb.Length > 0)
				{
					sb.Append(";");
				}
				int scaled = Mathf.RoundToInt(Mathf.Clamp(p.brightness, MinBrightness, MaxBrightness) * BrightnessScale);
				sb.Append(EncodeId(p.id)).Append(":").Append(p.enabled ? 1 : 0).Append(":").Append(scaled);
			}
			st.suppressProfileCallback = true;
			try
			{
				st.profilesJSON.val = sb.ToString();
			}
			finally
			{
				st.suppressProfileCallback = false;
			}
			st.saveAt = Time.unscaledTime + 1f;
		}

		private static void AddOrReplace(ProfileList list, ClothingProfile p)
		{
			if (list == null || p == null || string.IsNullOrEmpty(p.id))
			{
				return;
			}
			if (list.profiles == null)
			{
				list.profiles = new List<ClothingProfile>();
			}
			for (int i = 0; i < list.profiles.Count; i++)
			{
				if (list.profiles[i] != null && list.profiles[i].id == p.id)
				{
					list.profiles[i] = p;
					return;
				}
			}
			list.profiles.Add(p);
		}

		private static string EncodeId(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return string.Empty;
			}
			try
			{
				return Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
			}
			catch
			{
				return id.Replace(";", "_").Replace(":", "_");
			}
		}

		private static string DecodeId(string encoded)
		{
			if (string.IsNullOrEmpty(encoded))
			{
				return string.Empty;
			}
			try
			{
				return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
			}
			catch
			{
				return encoded;
			}
		}

		private static ClothingProfile FindExact(ProfileList list, string key)
		{
			if (list == null || list.profiles == null || string.IsNullOrEmpty(key))
			{
				return null;
			}
			for (int i = 0; i < list.profiles.Count; i++)
			{
				if (list.profiles[i] != null && list.profiles[i].id == key)
				{
					return list.profiles[i];
				}
			}
			return null;
		}

		private static ClothingProfile FindProfile(ProfileList list, string key, string baseName)
		{
			ClothingProfile exact = FindExact(list, key);
			if (exact != null)
			{
				return exact;
			}
			if (!string.IsNullOrEmpty(baseName) && baseName != key)
			{
				return FindExact(list, baseName);
			}
			return null;
		}

		// ------------------------------------------------------------------ applying

		private void ApplyAllFromProfiles(PersonState st)
		{
			try
			{
				if (st.atom == null || backfaceShader == null)
				{
					return;
				}
				RestoreOriginalMaterials(st);
				ProfileList list = LoadProfiles(st);
				List<DAZClothingItem> clothes = new List<DAZClothingItem>();
				List<string> keys = new List<string>(), labels = new List<string>(), baseNames = new List<string>();
				CollectClothing(st, clothes, keys, labels, baseNames);
				for (int idx = 0; idx < clothes.Count; idx++)
				{
					DAZClothingItem clothing = clothes[idx];
					if (!clothing.gameObject.activeInHierarchy)
					{
						continue;
					}
					string key = keys[idx];
					ClothingProfile profile = FindProfile(list, key, baseNames[idx]);
					bool enabled;
					if (!st.runtimeEnabled.TryGetValue(key, out enabled))
					{
						enabled = profile == null ? cfgDefaultEnabled.Value : profile.enabled;
					}
					float brightness;
					if (!st.runtimeBrightness.TryGetValue(key, out brightness))
					{
						brightness = profile == null ? Mathf.Clamp(cfgDefaultBrightness.Value, MinBrightness, MaxBrightness) : Mathf.Clamp(profile.brightness, MinBrightness, MaxBrightness);
					}
					else
					{
						brightness = Mathf.Clamp(brightness, MinBrightness, MaxBrightness);
					}
					if (!enabled)
					{
						continue;
					}
					PatchClothing(st, clothing, key, brightness);
				}
			}
			catch (Exception e)
			{
				Logger.LogError("ClothingBackface Apply error: " + e);
				RestoreOriginalMaterials(st);
			}
		}

		private void PatchClothing(PersonState st, DAZClothingItem clothing, string clothingId, float brightness)
		{
			DAZSkinWrap[] wraps = clothing.GetComponentsInChildren<DAZSkinWrap>(true);
			foreach (DAZSkinWrap wrap in wraps)
			{
				if (wrap == null || !wrap.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (wrap.GPUmaterials == null || wrap.GPUmaterials.Length == 0)
				{
					wrap.InitMaterials();
				}
				bool wrapPatched = false;
				if (wrap.GPUuseSimpleMaterial)
				{
					if (EnsurePatched(st, wrap.GPUsimpleMaterial, clothingId, brightness))
					{
						wrapPatched = true;
					}
				}
				else if (wrap.GPUmaterials != null)
				{
					for (int i = 0; i < wrap.GPUmaterials.Length; i++)
					{
						if (wrap.materialsEnabled != null && i < wrap.materialsEnabled.Length && !wrap.materialsEnabled[i])
						{
							continue;
						}
						if (EnsurePatched(st, wrap.GPUmaterials[i], clothingId, brightness))
						{
							wrapPatched = true;
						}
					}
				}
				if (wrapPatched)
				{
					if (!st.patchedWraps.Contains(wrap))
					{
						st.patchedWraps.Add(wrap);
					}
					wrap.FlushBuffers();
				}
			}
		}

		private bool EnsurePatched(PersonState st, Material material, string clothingId, float brightness)
		{
			if (material == null || material.shader == null)
			{
				return false;
			}
			if (material.shader == backfaceShader)
			{
				material.SetFloat("_BackBrightness", brightness);
				return true;
			}
			if (material.shader.name != SupportedSourceShader)
			{
				return false;
			}
			bool originalSpecTexWasNull = material.HasProperty("_SpecTex") && material.GetTexture("_SpecTex") == null;
			PatchedMaterial record = new PatchedMaterial();
			record.material = material;
			record.originalShader = material.shader;
			record.originalRenderQueue = material.renderQueue;
			record.clothingId = clothingId;
			st.patched.Add(record);
			material.shader = backfaceShader;
			material.renderQueue = record.originalRenderQueue;
			material.SetFloat("_BackBrightness", brightness);
			if (originalSpecTexWasNull && material.HasProperty("_SpecTex"))
			{
				material.SetTexture("_SpecTex", Texture2D.blackTexture);
				record.injectedBlackSpecTex = true;
			}
			return true;
		}

		private void SetBrightnessForClothing(PersonState st, string clothingId, float value)
		{
			value = Mathf.Clamp(value, MinBrightness, MaxBrightness);
			for (int i = 0; i < st.patched.Count; i++)
			{
				PatchedMaterial r = st.patched[i];
				if (r.clothingId == clothingId && r.material != null)
				{
					r.material.SetFloat("_BackBrightness", value);
				}
			}
		}

		private void RestoreOriginalMaterials(PersonState st)
		{
			try
			{
				for (int i = 0; i < st.patched.Count; i++)
				{
					PatchedMaterial r = st.patched[i];
					if (r.material == null)
					{
						continue;
					}
					if (backfaceShader != null && r.material.shader != backfaceShader)
					{
						continue;
					}
					if (r.injectedBlackSpecTex && r.material.HasProperty("_SpecTex") && r.material.GetTexture("_SpecTex") == Texture2D.blackTexture)
					{
						r.material.SetTexture("_SpecTex", null);
					}
					r.material.shader = r.originalShader;
					r.material.renderQueue = r.originalRenderQueue;
				}
				for (int i = 0; i < st.patchedWraps.Count; i++)
				{
					if (st.patchedWraps[i] != null)
					{
						st.patchedWraps[i].FlushBuffers();
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogError("ClothingBackface Restore error: " + e);
			}
			st.patched.Clear();
			st.patchedWraps.Clear();
		}

		// ------------------------------------------------------------------ the per-clothing controls

		private void ClearDynamic(PersonState st)
		{
			foreach (JSONStorableBool b in st.dynBools)
			{
				st.host.DeregisterBool(b);
			}
			foreach (JSONStorableFloat f in st.dynFloats)
			{
				st.host.DeregisterFloat(f);
			}
			st.dynBools.Clear();
			st.dynFloats.Clear();
		}

		private void RefreshClothingUI(PersonState st)
		{
			st.buildingDynamicUI = true;
			try
			{
				ClearDynamic(st);
				st.runtimeEnabled.Clear();
				st.runtimeBrightness.Clear();
				st.runtimeBaseNames.Clear();
				if (st.atom == null)
				{
					return;
				}
				ProfileList list = LoadProfiles(st);
				List<DAZClothingItem> clothes = new List<DAZClothingItem>();
				List<string> keys = new List<string>(), labels = new List<string>(), baseNames = new List<string>();
				CollectClothing(st, clothes, keys, labels, baseNames);
				for (int idx = 0; idx < clothes.Count; idx++)
				{
					if (!clothes[idx].gameObject.activeInHierarchy)
					{
						continue;
					}
					string key = keys[idx], label = labels[idx], baseName = baseNames[idx];
					ClothingProfile profile = FindProfile(list, key, baseName);
					if (profile == null)
					{
						profile = new ClothingProfile();
						profile.id = key;
						profile.enabled = cfgDefaultEnabled.Value;
						profile.brightness = Mathf.Clamp(cfgDefaultBrightness.Value, MinBrightness, MaxBrightness);
						list.profiles.Add(profile);
					}
					else if (profile.id != key)
					{
						profile.id = key;
					}
					profile.brightness = Mathf.Clamp(profile.brightness, MinBrightness, MaxBrightness);
					st.runtimeEnabled[key] = profile.enabled;
					st.runtimeBrightness[key] = profile.brightness;
					st.runtimeBaseNames[key] = baseName;
					CreateClothingControls(st, key, label, profile);
				}
				SaveProfiles(st, list);
			}
			catch (Exception e)
			{
				Logger.LogError("ClothingBackface RefreshClothingUI error: " + e);
			}
			finally
			{
				st.buildingDynamicUI = false;
			}
		}

		private void CreateClothingControls(PersonState st, string clothingId, string label, ClothingProfile profile)
		{
			PersonState captured = st;
			string id = clothingId;
			JSONStorableBool enabledJSON = new JSONStorableBool(label, profile.enabled, delegate (bool value) { OnClothingEnabledChanged(captured, id, value); });
			JSONStorableFloat brightnessJSON = new JSONStorableFloat(label + " Brightness", profile.brightness, delegate (float value) { OnClothingBrightnessChanged(captured, id, value); }, MinBrightness, MaxBrightness, true, true);
			st.host.RegisterBool(enabledJSON);
			st.host.RegisterFloat(brightnessJSON);
			st.dynBools.Add(enabledJSON);
			st.dynFloats.Add(brightnessJSON);
		}

		private void WriteProfile(PersonState st, string clothingId, bool enabled, float brightness)
		{
			string baseName;
			st.runtimeBaseNames.TryGetValue(clothingId, out baseName);
			ProfileList list = LoadProfiles(st);
			ClothingProfile profile = FindProfile(list, clothingId, baseName);
			if (profile == null)
			{
				profile = new ClothingProfile();
				profile.id = clothingId;
				list.profiles.Add(profile);
			}
			else if (profile.id != clothingId)
			{
				profile.id = clothingId;
			}
			profile.enabled = enabled;
			profile.brightness = brightness;
			SaveProfiles(st, list);
		}

		private void OnClothingEnabledChanged(PersonState st, string clothingId, bool enabled)
		{
			if (st.buildingDynamicUI)
			{
				return;
			}
			try
			{
				float current;
				if (!st.runtimeBrightness.TryGetValue(clothingId, out current))
				{
					current = Mathf.Clamp(cfgDefaultBrightness.Value, MinBrightness, MaxBrightness);
				}
				current = Mathf.Clamp(current, MinBrightness, MaxBrightness);
				st.runtimeEnabled[clothingId] = enabled;
				st.runtimeBrightness[clothingId] = current;
				WriteProfile(st, clothingId, enabled, current);
				if (!st.manualRestoreMode)
				{
					if (enabled)
					{
						EnsureShader();
					}
					if (backfaceShader != null)
					{
						ApplyAllFromProfiles(st);
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogError("ClothingBackface OnClothingEnabledChanged error: " + e);
			}
		}

		private void OnClothingBrightnessChanged(PersonState st, string clothingId, float value)
		{
			if (st.buildingDynamicUI)
			{
				return;
			}
			try
			{
				float clamped = Mathf.Clamp(value, MinBrightness, MaxBrightness);
				st.runtimeBrightness[clothingId] = clamped;
				bool enabled;
				if (!st.runtimeEnabled.TryGetValue(clothingId, out enabled))
				{
					enabled = true;
				}
				WriteProfile(st, clothingId, enabled, clamped);
				if (!st.manualRestoreMode)
				{
					SetBrightnessForClothing(st, clothingId, clamped);
				}
			}
			catch (Exception e)
			{
				Logger.LogError("ClothingBackface OnClothingBrightnessChanged error: " + e);
			}
		}
	}
}
