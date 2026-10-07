using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroT.UiKit;

namespace ZeroT.GiveMeFPSStandalone
{
	// Standalone port of Redeyes' GiveMeFPS (v27 session plugin). No .var and no session plugin: this DLL applies the
	// "Full" preset (the original's "Give me ALL the FPS!" button) by itself after VaM loads, after every scene load and
	// whenever a person is added or renamed. The window matches the other restyled plugin windows.
	[BepInPlugin("com.zerot.givemefps.standalone", "GiveMeFPS Standalone", "1.0.0")]
	public sealed class GiveMeFPSStandalone : BaseUnityPlugin
	{
		private sealed class Preset
		{
			public string Name;
			public bool HairSim;
			public float CurveDensity;
			public float HairMultiplier;
			public float HairWidth;
			public float HairWeight;
			public float HairFriction;
			public float HairIterations;
			public bool Breast;
			public bool Lower;
			public bool Tongue;
			public string Shader;
			public bool DisablePixelLights;
			public string AntiAliasing;
			public string TextureSize;
			public bool HairOnly;
		}

		private static readonly Preset Full = new Preset { Name = "Full", HairSim = false, CurveDensity = 10f, HairMultiplier = 2f, HairWidth = 0.00065f, HairWeight = 1f, HairFriction = 0.2f, HairIterations = 1f, Breast = false, Lower = false, Tongue = false, Shader = "Fast", DisablePixelLights = true, AntiAliasing = "1", TextureSize = "512", HairOnly = false };
		private static readonly Preset Recommend = new Preset { Name = "Recommend", HairSim = true, CurveDensity = 16f, HairMultiplier = 3f, HairWidth = 0.00045f, HairWeight = 1f, HairFriction = 0.2f, HairIterations = 1f, Breast = true, Lower = false, Tongue = false, Shader = "Quality", DisablePixelLights = true, AntiAliasing = "1", TextureSize = "1024", HairOnly = false };
		private static readonly Preset HairOnly = new Preset { Name = "Hair Only", HairSim = true, CurveDensity = 16f, HairMultiplier = 3f, HairWidth = 0.00045f, HairWeight = 1f, HairFriction = 0.2f, HairIterations = 1f, Breast = true, Lower = true, Tongue = true, Shader = "Quality", DisablePixelLights = false, AntiAliasing = "8", TextureSize = "1024", HairOnly = true };
		private static readonly Preset VamDefaults = new Preset { Name = "VaM Defaults", HairSim = true, CurveDensity = 16f, HairMultiplier = 16f, HairWidth = 0.0001f, HairWeight = 1.5f, HairFriction = 0.2f, HairIterations = 2f, Breast = true, Lower = true, Tongue = true, Shader = "Quality", DisablePixelLights = false, AntiAliasing = "8", TextureSize = "1024", HairOnly = false };

		private ConfigEntry<bool> cfgAuto;
		private ConfigEntry<string> cfgPreset;
		private ConfigEntry<float> cfgDelay;
		private RlUguiWindow win;
		private Text status;
		private Button autoButton;
		private Button[] presetButtons = new Button[4];
		private Preset[] presets;
		private bool handlersHooked;
		private bool startupDone;
		private float applyAt = -1f;
		private int attemptsLeft;
		private string lastApplied = "none";

		private void Awake()
		{
			cfgAuto = Config.Bind<bool>("General", "AutoApply", true, "Apply the preset automatically at startup, after scene loads and when a person is added.");
			cfgPreset = Config.Bind<string>("General", "AutoPreset", "Full", "Preset applied automatically: Full, Recommend, Hair Only or VaM Defaults.");
			cfgDelay = Config.Bind<float>("General", "StartupDelaySeconds", 4f, "Wait this long after VaM finishes loading before the first automatic apply.");
			presets = new Preset[] { Full, Recommend, HairOnly, VamDefaults };
			win = new RlUguiWindow(this, "GiveMeFPS", "Window", 40f, 60f, 300f, 178f, 240f, 150f, 800f, 600f);
		}

		private System.Collections.IEnumerator Start()
		{
			while (SuperController.singleton == null || EventSystem.current == null)
			{
				yield return null;
			}
			BuildWindow();
		}

		private void OnDestroy()
		{
			Unhook();
			if (win != null)
			{
				win.Dispose();
			}
		}

		private void OnDisable()
		{
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

		private void Hook()
		{
			if (handlersHooked)
			{
				return;
			}
			SuperController sc = SuperController.singleton;
			sc.onSceneLoadedHandlers += OnSceneLoaded;
			sc.onAtomAddedHandlers += OnAtomAdded;
			sc.onAtomUIDsChangedHandlers += OnAtomUIDsChanged;
			handlersHooked = true;
		}

		private void Unhook()
		{
			if (!handlersHooked || SuperController.singleton == null)
			{
				return;
			}
			SuperController sc = SuperController.singleton;
			sc.onSceneLoadedHandlers -= OnSceneLoaded;
			sc.onAtomAddedHandlers -= OnAtomAdded;
			sc.onAtomUIDsChangedHandlers -= OnAtomUIDsChanged;
			handlersHooked = false;
		}

		private void OnSceneLoaded()
		{
			Schedule(0.8f);
		}

		private void OnAtomAdded(Atom atom)
		{
			if (atom != null && atom.type == "Person")
			{
				Schedule(0.8f);
			}
		}

		private void OnAtomUIDsChanged(List<string> uids)
		{
			Schedule(0.8f);
		}

		private void Schedule(float delay)
		{
			if (!cfgAuto.Value || (win != null && !win.Active))
			{
				return;
			}
			applyAt = Time.unscaledTime + delay;
			attemptsLeft = 5;
		}

		private Preset AutoPreset()
		{
			for (int i = 0; i < presets.Length; i++)
			{
				if (string.Equals(presets[i].Name, cfgPreset.Value, StringComparison.OrdinalIgnoreCase))
				{
					return presets[i];
				}
			}
			return Full;
		}

		private void Update()
		{
			if (win == null || win.Window == null)
			{
				return;
			}
			win.Tick();
			SuperController sc = SuperController.singleton;
			if (!win.Active || sc == null)
			{
				return;
			}
			if (!startupDone)
			{
				if (sc.isLoading)
				{
					return;
				}
				startupDone = true;
				Hook();
				Schedule(Mathf.Max(0f, cfgDelay.Value));
			}
			if (applyAt < 0f || Time.unscaledTime < applyAt || sc.isLoading)
			{
				return;
			}
			// Hair/cloth controls appear a moment after a person loads, so retry a few times like the original.
			Preset p = AutoPreset();
			bool complete = Apply(p);
			attemptsLeft--;
			if (complete || attemptsLeft <= 0)
			{
				applyAt = -1f;
			}
			else
			{
				applyAt = Time.unscaledTime + 0.6f;
			}
		}

		// Returns true when every hair group already had its simulation control (nothing is still loading).
		private bool Apply(Preset p)
		{
			bool complete = true;
			int persons = 0;
			try
			{
				foreach (Atom atom in SuperController.singleton.GetAtoms())
				{
					if (atom == null)
					{
						continue;
					}
					if (atom.type == "Person")
					{
						persons++;
						foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>())
						{
							if (hairGroup == null)
							{
								continue;
							}
							HairSimControl hc = hairGroup.GetComponentInChildren<HairSimControl>();
							if (hc == null)
							{
								complete = false;
								continue;
							}
							SetHair(hc, p);
						}
						if (!p.HairOnly)
						{
							SetBool(atom, "BreastPhysicsMesh", "on", p.Breast);
							SetBool(atom, "LowerPhysicsMesh", "on", p.Lower);
							SetBool(atom, "TongueControl", "tongueCollision", p.Tongue);
						}
					}
					if (!p.HairOnly)
					{
						JSONStorable mirror = atom.GetStorableByID("MirrorRender");
						if (mirror != null)
						{
							JSONStorableBool b = mirror.GetBoolJSONParam("disablePixelLights");
							if (b != null)
							{
								b.val = p.DisablePixelLights;
							}
							JSONStorableStringChooser aa = mirror.GetStringChooserJSONParam("antiAliasing");
							if (aa != null)
							{
								aa.val = p.AntiAliasing;
							}
							JSONStorableStringChooser ts = mirror.GetStringChooserJSONParam("textureSize");
							if (ts != null)
							{
								ts.val = p.TextureSize;
							}
						}
					}
				}
				lastApplied = p.Name + " (" + persons + (persons == 1 ? " person)" : " persons)");
			}
			catch (Exception e)
			{
				Logger.LogWarning("Apply " + p.Name + " failed: " + e.Message);
				lastApplied = p.Name + " failed";
				return true;
			}
			return complete;
		}

		private static void SetBool(Atom atom, string storableId, string param, bool value)
		{
			JSONStorable s = atom.GetStorableByID(storableId);
			if (s == null)
			{
				return;
			}
			JSONStorableBool b = s.GetBoolJSONParam(param);
			if (b != null)
			{
				b.val = value;
			}
		}

		private static void SetHair(HairSimControl hc, Preset p)
		{
			try { hc.SetBoolParamValue("simulationEnabled", p.HairSim); } catch { }
			try { hc.SetFloatParamValue("curveDensity", p.CurveDensity); } catch { }
			try { hc.SetFloatParamValue("hairMultiplier", p.HairMultiplier); } catch { }
			try { hc.SetFloatParamValue("width", p.HairWidth); } catch { }
			try { hc.SetFloatParamValue("weight", p.HairWeight); } catch { }
			try { hc.SetFloatParamValue("friction", p.HairFriction); } catch { }
			try { hc.SetFloatParamValue("iterations", p.HairIterations); } catch { }
			try { hc.SetStringChooserParamValue("shaderType", p.Shader); } catch { }
		}

		// ------------------------------------------------------------------ window

		private void BuildWindow()
		{
			win.OnLayout = delegate(float w, float h)
			{
				LayoutBody(w, h);
			};
			win.OnActiveChanged = delegate(bool on)
			{
				if (on)
				{
					Schedule(0.2f);
				}
				else
				{
					applyAt = -1f;
				}
				RefreshStatus();
			};
			win.Build("GiveMeFPS Standalone Desktop");
			status = RlUguiWindow.NewText("Status", win.Body, "", TextAnchor.MiddleLeft);
			for (int i = 0; i < presets.Length; i++)
			{
				Preset p = presets[i];
				presetButtons[i] = RlUguiWindow.NewButton("Preset " + p.Name, win.Body, p.Name == "Full" ? "Full (ALL the FPS)" : p.Name, delegate
				{
					cfgPreset.Value = p.Name;
					Apply(p);
					RefreshStatus();
				});
			}
			autoButton = RlUguiWindow.NewButton("Auto", win.Body, "", delegate
			{
				cfgAuto.Value = !cfgAuto.Value;
				if (cfgAuto.Value)
				{
					Schedule(0.2f);
				}
				else
				{
					applyAt = -1f;
				}
				RefreshStatus();
			});
			win.AddResizeHandle();
			win.Apply();
			RefreshStatus();
		}

		private void LayoutBody(float w, float h)
		{
			float m = RlUguiWindow.Margin;
			float inner = Mathf.Max(60f, w - 2f * m);
			float half = (inner - 6f) / 2f;
			float rowH = 28f;
			RlUguiWindow.SetRect(status.rectTransform, m, 30f, inner, 36f);
			for (int i = 0; i < presetButtons.Length; i++)
			{
				RlUguiWindow.SetRect(((Component)presetButtons[i]).GetComponent<RectTransform>(), m + (i % 2) * (half + 6f), 70f + (i / 2) * (rowH + 4f), half, rowH);
			}
			RlUguiWindow.SetRect(((Component)autoButton).GetComponent<RectTransform>(), m, 70f + 2f * (rowH + 4f), inner, rowH);
		}

		private void RefreshStatus()
		{
			if (status == null)
			{
				return;
			}
			Preset p = AutoPreset();
			status.text = (win.Active ? "Auto preset: " + p.Name : "Plugin is Off") + "\nLast applied: " + lastApplied;
			if (autoButton != null)
			{
				RlUguiWindow.SetLabel(autoButton, "Auto-apply on load: " + (cfgAuto.Value ? "ON" : "OFF"));
			}
		}
	}
}
