using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ZeroT.ForcePersonCacheStandalone
{
	// Headless standalone port of JayJayWon's ForcePersonCache (.var): "speeds up the time it takes to create an initial Person atom.
	// The first Person atom created in a scene is much quicker; the down side is that VAM start up takes a little longer."
	//
	// What the plugin does, all of it
	//  - when it starts (VaM first loads, and again on a Hard Reset - that is why it was meant to be a User Default session plugin):
	//    if the scene has no atom of category "People" and none named "Person", it creates a Person atom with
	//    SuperController.AddAtomByType("Person", "Person") and, when that has finished, removes it again (Atom.Remove)
	//  - if there already is a person (or an atom called Person), it does nothing
	//  - the .var also hooks atom added / removed / renamed / scene loaded with empty handlers and shows a help text; there is nothing
	//    to port in those, and no session plugin is needed here
	// Here, with no session plugin to start it, the same run happens when VaM has finished loading, and after every Hard Reset
	// (SuperController.HardReset). If the .var is loaded in the session this one steps aside.
	[BepInPlugin("com.zerot.forcepersoncache", "ForcePersonCache Standalone", "1.0.0")]
	public sealed class ForcePersonCacheStandalone : BaseUnityPlugin
	{
		private static ForcePersonCacheStandalone instance;
		private ConfigEntry<bool> cfgEnabled, cfgOnStart, cfgOnHardReset;
		private ConfigEntry<float> cfgDelay;
		private Harmony harmony;

		private bool startDone;
		private bool pending;
		private float runAt;
		private bool running;
		private bool yielded;
		private float nextOriginalCheck;
		public int Runs;

		private void Awake()
		{
			instance = this;
			cfgEnabled = Config.Bind<bool>("General", "Enabled", true, "Turn the warm-up off.");
			cfgOnStart = Config.Bind<bool>("General", "RunWhenVaMStarts", true, "Warm up once VaM has finished loading (what the session plugin did on first load).");
			cfgOnHardReset = Config.Bind<bool>("General", "RunAfterHardReset", true, "Warm up again after a Hard Reset (the session plugin was started again then).");
			cfgDelay = Config.Bind<float>("General", "DelaySeconds", 1f, "Seconds to wait after VaM is ready (or a Hard Reset) before the Person atom is created.");
			try
			{
				harmony = new Harmony("com.zerot.forcepersoncache");
				MethodInfo hard = typeof(SuperController).GetMethod("HardReset", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (hard != null)
				{
					harmony.Patch(hard, null, new HarmonyMethod(typeof(ForcePersonCacheStandalone).GetMethod("HardResetPostfix")));
				}
				else
				{
					Logger.LogWarning("SuperController.HardReset was not found; the warm-up will only run at start.");
				}
			}
			catch (Exception e)
			{
				Logger.LogWarning("Could not watch Hard Reset: " + e.Message);
			}
		}

		private void OnDestroy()
		{
			if (harmony != null)
			{
				harmony.UnpatchAll("com.zerot.forcepersoncache");
			}
			instance = null;
		}

		public static void HardResetPostfix()
		{
			if (instance != null && instance.cfgOnHardReset.Value)
			{
				instance.Schedule("Hard Reset");
			}
		}

		private void Schedule(string why)
		{
			pending = true;
			runAt = Time.unscaledTime + Mathf.Max(0f, cfgDelay.Value);
			Logger.LogInfo("Person warm-up scheduled (" + why + ").");
		}

		private bool OriginalLoaded()
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					Type t = a.GetType("JayJayWon.ForcePersonCache", false);
					if (t != null && typeof(MVRScript).IsAssignableFrom(t))
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}

		private void Update()
		{
			if (Time.unscaledTime >= nextOriginalCheck)
			{
				nextOriginalCheck = Time.unscaledTime + 2f;
				bool orig = OriginalLoaded();
				if (orig && !yielded)
				{
					yielded = true;
					pending = false;
					Logger.LogWarning("The ForcePersonCache .var is loaded in this session: this one steps aside.");
				}
				else if (!orig && yielded)
				{
					yielded = false;
				}
			}
			if (yielded || !cfgEnabled.Value)
			{
				return;
			}
			SuperController sc = SuperController.singleton;
			if (sc == null || sc.isLoading)
			{
				return;
			}
			if (!startDone)
			{
				startDone = true;
				if (cfgOnStart.Value)
				{
					Schedule("VaM started");
				}
			}
			if (pending && !running && Time.unscaledTime >= runAt)
			{
				pending = false;
				StartCoroutine(WarmUp());
			}
		}

		// Same test as the .var's Start()
		private static bool HasPerson(SuperController sc)
		{
			foreach (Atom atom in sc.GetAtoms())
			{
				if (atom != null && (atom.category == "People" || atom.name == "Person"))
				{
					return true;
				}
			}
			return false;
		}

		private IEnumerator WarmUp()
		{
			running = true;
			SuperController sc = SuperController.singleton;
			if (sc == null)
			{
				running = false;
				yield break;
			}
			if (HasPerson(sc))
			{
				Logger.LogInfo("A Person is already in the scene: nothing to warm up.");
				running = false;
				yield break;
			}
			Runs++;
			Logger.LogInfo("Creating a Person atom to warm VaM's caches...");
			IEnumerator add = sc.AddAtomByType("Person", "Person");
			bool failed = false;
			while (true)
			{
				bool more;
				try
				{
					more = add.MoveNext();
				}
				catch (Exception e)
				{
					Logger.LogWarning("Creating the Person failed: " + e.Message);
					failed = true;
					break;
				}
				if (!more)
				{
					break;
				}
				yield return add.Current;
			}
			if (!failed)
			{
				try
				{
					Atom atom = sc.GetAtomByUid("Person");
					if (atom != null)
					{
						atom.Remove();
						Logger.LogInfo("Person atom removed again; the next one will be quicker.");
					}
					else
					{
						Logger.LogWarning("The Person atom was not found after creating it.");
					}
				}
				catch (Exception e)
				{
					Logger.LogWarning("Removing the Person failed: " + e.Message);
				}
			}
			running = false;
		}
	}
}
