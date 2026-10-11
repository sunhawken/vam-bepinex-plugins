using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MVR.FileManagement;
using MVR.FileManagementSecure;
using UnityEngine;

namespace ZeroT.AppearanceLoaderHelper
{
	// Headless helper for ky1001's AppearanceLoader (appearance .vap / .json / scene loader). No window. It patches the running
	// script in memory. What reading all of it (5900 lines) turned up, feature by feature:
	//
	//  Speed
	//  1. Every ".latest" reference and every texture / asset path that is not found as written is "repaired" by asking the file
	//     system about version 1000, 999, 998 ... 1 of that package, one call each (checkVarDir, checkPath). A preset that points
	//     at an old package version, or at a package that is not installed, costs about 1000 file-system questions per path.
	//     Now: only versions that are really installed are asked about; the answer is the same.
	//  2. "Search Missing Textures" walks every folder under four roots for EACH missing texture. Now: the folder tree is listed
	//     once per load and every search is a lookup; same files, same order.
	//  3. Applying morphs waits a fixed 1 s twice ("insurance for collision problems", the author says 0.5 s is fine). Configurable,
	//     default 0.5 s.
	//  4. Waiting for hair / clothing to appear uses the "Select Wait Time" setting for EVERY item, so every missing item costs
	//     the full time. Optional cap on that time during a load (default: off; the setting is restored afterwards).
	//  5. After a skin preset is sent the loader waits for the loading screen to go away for up to 300 s. Now 120 s by default.
	//
	//  Safety
	//  6. Any error in the middle of a load (a person that changed under it, a half-loaded character...) killed the load
	//     coroutine for good. The clothing sim had been switched off at the start and was never switched back on, and the "waiting
	//     for the menu" state stayed set. Now: the error is caught, the sim state is restored, the menu is closed, and the loader's
	//     message says what happened.
	[BepInPlugin("com.zerot.appearanceloader.helper", "AppearanceLoader Helper", "1.0.0")]
	public sealed class AppearanceLoaderHelperPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> CfgPackages;
		internal static ConfigEntry<bool> CfgSearch;
		internal static ConfigEntry<float> CfgMorphWait;
		internal static ConfigEntry<float> CfgMaxItemWait;
		internal static ConfigEntry<float> CfgTextureTimeout;
		internal static ConfigEntry<bool> CfgRecover;
		internal static BepInEx.Logging.ManualLogSource Log;

		private Harmony harmony;
		private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
		private readonly Queue<Assembly> pending = new Queue<Assembly>();
		private float nextScan;

		private void Awake()
		{
			Log = Logger;
			CfgPackages = Config.Bind<bool>("Speed", "FastPackageVersionLookup", true, "Look up the newest installed version of a package directly instead of asking the file system about versions 1000..1.");
			CfgSearch = Config.Bind<bool>("Speed", "FastMissingTextureSearch", true, "List the texture folders once per load and answer every Search Missing Textures from that list.");
			CfgMorphWait = Config.Bind<float>("Speed", "MorphWaitSeconds", 0.5f, "The two pauses while morphs are applied (AppearanceLoader uses 1). 0 - 1.");
			CfgMaxItemWait = Config.Bind<float>("Speed", "MaxItemWaitSeconds", 0f, "Upper limit, for the length of a load, for the 'Select Wait Time' setting (how long each hair / clothing item may take to appear; a missing item waits the whole time). 0 = leave the setting alone. Your setting is put back afterwards.");
			CfgTextureTimeout = Config.Bind<float>("Speed", "TextureLoadTimeoutSeconds", 120f, "How long to wait for a skin preset to finish loading (AppearanceLoader uses 300).");
			CfgRecover = Config.Bind<bool>("Safety", "RecoverFromFailedLoad", true, "If a load fails half way: put the clothing sim back, close the item menu and say what happened.");
			harmony = new Harmony("com.zerot.appearanceloader.helper");
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				pending.Enqueue(a);
			}
		}

		private void OnDestroy()
		{
			AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
		}

		private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			lock (pending)
			{
				pending.Enqueue(args.LoadedAssembly);
			}
		}

		private void Update()
		{
			if (Time.unscaledTime < nextScan)
			{
				return;
			}
			nextScan = Time.unscaledTime + 1f;
			while (true)
			{
				Assembly a;
				lock (pending)
				{
					if (pending.Count == 0)
					{
						return;
					}
					a = pending.Dequeue();
				}
				if (a == null || !seen.Add(a))
				{
					continue;
				}
				try
				{
					TryPatch(a);
				}
				catch (Exception e)
				{
					Logger.LogWarning("Patching " + a.FullName + " failed: " + e.Message);
				}
			}
		}

		private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		private HarmonyMethod H(string name)
		{
			MethodInfo m = typeof(Hooks).GetMethod(name);
			if (m == null)
			{
				throw new InvalidOperationException("missing hook " + name);
			}
			return new HarmonyMethod(m);
		}

		private void TryPatch(Assembly asm)
		{
			Type t = asm.GetType("ky1001.AppearanceLoader", false);
			if (t == null || t.GetMethod("checkPath", All) == null)
			{
				return;
			}
			int n = 0;
			Hooks.LoaderType = t;
			MethodInfo checkVar = t.GetMethod("checkVarDir", All, null, new Type[] { typeof(string) }, null);
			MethodInfo checkPath = t.GetMethod("checkPath", All, null, new Type[] { typeof(string) }, null);
			MethodInfo handle = t.GetMethod("HandleLoadPreset", All, null, new Type[] { typeof(string) }, null);
			MethodInfo search = t.GetMethod("SearchFile", All);
			MethodInfo apply = t.GetMethod("applyData", All, null, Type.EmptyTypes, null);

			if (CfgPackages.Value && checkVar != null && checkPath != null)
			{
				harmony.Patch(checkVar, H("ScopePrefix"), null, null, H("ScopeFinalizer"));
				harmony.Patch(checkPath, H("ScopePrefix"), null, null, H("ScopeFinalizer"));
				MethodInfo fe = typeof(FileManagerSecure).GetMethod("FileExists", new Type[] { typeof(string), typeof(bool) });
				MethodInfo de = typeof(FileManagerSecure).GetMethod("DirectoryExists", new Type[] { typeof(string), typeof(bool) });
				if (fe != null && de != null)
				{
					harmony.Patch(fe, H("ExistsPrefix"));
					harmony.Patch(de, H("ExistsPrefix"));
					n += 4;
				}
			}
			if (handle != null)
			{
				harmony.Patch(handle, H("HandlePrefix"));
				n++;
			}
			if (CfgSearch.Value && search != null)
			{
				harmony.Patch(search, H("SearchPrefix"));
				n++;
			}
			if (CfgRecover.Value && apply != null && typeof(IEnumerator).IsAssignableFrom(apply.ReturnType))
			{
				harmony.Patch(apply, null, H("ApplyPostfix"));
				n++;
			}
			n += PatchIterator(t, "changeMorph", H("MorphWaitTranspiler"));
			n += PatchIterator(t, "loadTexturesPreset", H("TextureTimeoutTranspiler"));
			Logger.LogInfo("AppearanceLoader patched (" + n + " hooks).");
		}

		// the body of "IEnumerator Foo()" lives in a compiler-made nested class's MoveNext
		private int PatchIterator(Type owner, string method, HarmonyMethod transpiler)
		{
			foreach (Type nt in owner.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (nt.Name.StartsWith("<" + method + ">"))
				{
					MethodInfo move = nt.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					if (move != null)
					{
						try
						{
							harmony.Patch(move, null, null, transpiler);
							return 1;
						}
						catch (Exception e)
						{
							Logger.LogWarning("AppearanceLoader." + method + ": the wait rewrite was not applied (" + e.Message + "); that wait is left as it is.");
							return 0;
						}
					}
				}
			}
			Logger.LogWarning("AppearanceLoader." + method + " not found: that wait is left as it is.");
			return 0;
		}
	}

	public static class Hooks
	{
		internal static Type LoaderType;

		// ------------------------------------------------------------ 1. installed package versions
		private static int scopeDepth;
		private static Dictionary<string, HashSet<int>> versions;
		private static DateTime indexStamp;
		private static bool indexBroken;
		public static int ShortcutHits;

		public static void ScopePrefix() { scopeDepth++; }

		public static Exception ScopeFinalizer(Exception __exception)
		{
			if (scopeDepth > 0)
			{
				scopeDepth--;
			}
			return __exception;
		}

		private static bool EnsureIndex()
		{
			if (indexBroken)
			{
				return false;
			}
			try
			{
				DateTime stamp = FileManager.lastPackageRefreshTime;
				if (versions != null && stamp == indexStamp)
				{
					return true;
				}
				Dictionary<string, HashSet<int>> idx = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
				List<string> uids = FileManager.GetPackageUids();
				if (uids == null || uids.Count == 0)
				{
					return false;
				}
				for (int i = 0; i < uids.Count; i++)
				{
					string uid = uids[i];
					int dot = uid == null ? -1 : uid.LastIndexOf('.');
					int v;
					if (dot > 0 && int.TryParse(uid.Substring(dot + 1), out v))
					{
						HashSet<int> set;
						string group = uid.Substring(0, dot);
						if (!idx.TryGetValue(group, out set))
						{
							idx[group] = set = new HashSet<int>();
						}
						set.Add(v);
					}
				}
				versions = idx;
				indexStamp = stamp;
				return true;
			}
			catch (Exception e)
			{
				indexBroken = true;
				AppearanceLoaderHelperPlugin.Log.LogWarning("Package version index unavailable, using the original lookup: " + e.Message);
				return false;
			}
		}

		// Inside checkVarDir / checkPath: a "Creator.Package.<n>:/..." path whose version n is not installed cannot exist.
		public static bool ExistsPrefix(object[] __args, ref bool __result)
		{
			if (scopeDepth == 0)
			{
				return true;
			}
			string path = __args[0] as string;
			if (string.IsNullOrEmpty(path))
			{
				return true;
			}
			int sep = path.IndexOf(":/", StringComparison.Ordinal);
			if (sep <= 0)
			{
				return true;
			}
			string head = path.Substring(0, sep);
			int dot = head.LastIndexOf('.');
			int v;
			if (dot <= 0 || !int.TryParse(head.Substring(dot + 1), out v) || !EnsureIndex())
			{
				return true;
			}
			HashSet<int> set;
			if (versions.TryGetValue(head.Substring(0, dot), out set) && set.Contains(v))
			{
				return true;   // installed: the real file system answers
			}
			__result = false;
			ShortcutHits++;
			return false;
		}

		// ------------------------------------------------------------ 2. one folder listing per load
		private sealed class Dir
		{
			public string path;
			public string[] files;
		}

		private static readonly Dictionary<string, List<Dir>> trees = new Dictionary<string, List<Dir>>(StringComparer.OrdinalIgnoreCase);
		public static int TreesBuilt;

		public static void HandlePrefix()
		{
			trees.Clear();   // a new load: files may have been added since
		}

		private static void Walk(string folder, List<Dir> into, int depth)
		{
			if (depth > 40)
			{
				return;
			}
			Dir d = new Dir();
			d.path = folder;
			d.files = FileManagerSecure.GetFiles(folder, "*") ?? new string[0];
			into.Add(d);
			string[] subs = FileManagerSecure.GetDirectories(folder, null);
			if (subs != null)
			{
				for (int i = 0; i < subs.Length; i++)
				{
					Walk(subs[i], into, depth + 1);
				}
			}
		}

		private static string NameOf(string path)
		{
			int i = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
			return i >= 0 ? path.Substring(i + 1) : path;
		}

		public static bool SearchPrefix(string folder, string filter, ref ArrayList allFiles)
		{
			try
			{
				if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(filter) || filter.IndexOfAny(new char[] { '*', '?' }) >= 0 || allFiles == null)
				{
					return true;   // patterns are left to the real search
				}
				List<Dir> tree;
				if (!trees.TryGetValue(folder, out tree))
				{
					tree = new List<Dir>();
					Walk(folder, tree, 0);
					trees[folder] = tree;
					TreesBuilt++;
				}
				for (int i = 0; i < tree.Count; i++)
				{
					string[] files = tree[i].files;
					for (int k = 0; k < files.Length; k++)
					{
						if (string.Equals(NameOf(files[k]), filter, StringComparison.OrdinalIgnoreCase))
						{
							allFiles.Add(files[k]);
						}
					}
				}
				return false;
			}
			catch (Exception e)
			{
				trees.Clear();
				AppearanceLoaderHelperPlugin.Log.LogWarning("Texture search index failed, using the original search: " + e.Message);
				return true;
			}
		}

		// ------------------------------------------------------------ 6. a failed load puts things back
		public static void ApplyPostfix(object __instance, ref IEnumerator __result)
		{
			__result = Guard(__result, __instance);
		}

		// "Select Wait Time" is read at every wait of a load; for the length of the load it is lowered to the cap (a value the
		// chooser really offers) and put back afterwards, so the user's setting is untouched.
		private static JSONStorableStringChooser LowerWait(object loader, out string original)
		{
			original = null;
			JSONStorableStringChooser chooser = Field(loader, "selectWaitTime") as JSONStorableStringChooser;
			if (chooser == null)
			{
				return null;
			}
			float cap = AppearanceLoaderHelperPlugin.CfgMaxItemWait.Value, current;
			if (!float.TryParse(chooser.val, out current) || current <= cap || cap <= 0f)
			{
				return null;
			}
			string best = null;
			float bestVal = -1f;
			List<string> choices = chooser.choices;
			for (int i = 0; choices != null && i < choices.Count; i++)
			{
				float v;
				if (float.TryParse(choices[i], out v) && v <= cap && v > bestVal)
				{
					best = choices[i];
					bestVal = v;
				}
			}
			if (best == null)
			{
				return null;
			}
			original = chooser.val;
			chooser.valNoCallback = best;
			return chooser;
		}

		private static object Field(object o, string name)
		{
			for (Type t = o.GetType(); t != null; t = t.BaseType)
			{
				FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (f != null)
				{
					return f.GetValue(o);
				}
			}
			return null;
		}

		private static void SetField(object o, string name, object value)
		{
			for (Type t = o.GetType(); t != null; t = t.BaseType)
			{
				FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (f != null)
				{
					f.SetValue(o, value);
					return;
				}
			}
		}

		public static int Recovered;
		public static string LastFailure;

		// Runs the loader's coroutine the way Unity does (nested IEnumerators are run to the end first) and stops cleanly on an error.
		public static IEnumerator Guard(IEnumerator inner, object loader)
		{
			string originalWait;
			JSONStorableStringChooser waitChooser = LowerWait(loader, out originalWait);
			try
			{
				Stack<IEnumerator> stack = new Stack<IEnumerator>();
				stack.Push(inner);
				while (stack.Count > 0)
				{
					IEnumerator top = stack.Peek();
					bool more;
					object current = null;
					try
					{
						more = top.MoveNext();
						if (more)
						{
							current = top.Current;
						}
					}
					catch (Exception e)
					{
						Recover(loader, e);
						yield break;
					}
					if (!more)
					{
						stack.Pop();
						continue;
					}
					IEnumerator nested = current as IEnumerator;
					if (nested != null)
					{
						stack.Push(nested);
						continue;
					}
					yield return current;
				}
			}
			finally
			{
				if (waitChooser != null)
				{
					waitChooser.valNoCallback = originalWait;
				}
			}
		}

		private static void Recover(object loader, Exception e)
		{
			Recovered++;
			Exception root = e.InnerException ?? e;
			LastFailure = root.GetType().Name + ": " + root.Message;
			AppearanceLoaderHelperPlugin.Log.LogWarning("AppearanceLoader stopped by an error, putting things back: " + root);
			try
			{
				MethodInfo sim = loader.GetType().GetMethod("simEnable", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (sim != null)
				{
					sim.Invoke(loader, null);
				}
			}
			catch (Exception e2)
			{
				AppearanceLoaderHelperPlugin.Log.LogWarning("Could not restore the clothing sim: " + (e2.InnerException ?? e2).Message);
			}
			try
			{
				SetField(loader, "waitingForPlayerToSubmitCanvas", false);
				SetField(loader, "_loadByTrigger", false);
				GameObject canvas = Field(loader, "currentlyOpenCanvas") as GameObject;
				if (canvas != null)
				{
					UnityEngine.Object.Destroy(canvas);
				}
				JSONStorableString message = Field(loader, "message") as JSONStorableString;
				if (message != null)
				{
					message.val = message.val + "\nStopped by an error (clothing sim restored): " + root.Message + "\n";
				}
			}
			catch
			{
			}
		}

		// ------------------------------------------------------------ 3/4/5. waits
		public static float MorphWait()
		{
			return Mathf.Clamp(AppearanceLoaderHelperPlugin.CfgMorphWait.Value, 0f, 1f);
		}

		public static float TextureTimeout()
		{
			return Mathf.Max(5f, AppearanceLoaderHelperPlugin.CfgTextureTimeout.Value);
		}

		public static int Transpiled;

		// ldc.r4 1 ; newobj WaitForSeconds(float)  ->  ldc.r4 1 becomes a call
		public static IEnumerable<CodeInstruction> MorphWaitTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo repl = typeof(Hooks).GetMethod("MorphWait");
			for (int i = 0; i + 1 < list.Count; i++)
			{
				ConstructorInfo ci = list[i + 1].operand as ConstructorInfo;
				if (list[i].opcode == OpCodes.Ldc_R4 && list[i].operand is float && (float)list[i].operand == 1f && list[i + 1].opcode == OpCodes.Newobj && ci != null && ci.DeclaringType == typeof(WaitForSeconds))
				{
					list[i] = new CodeInstruction(OpCodes.Call, repl);
					Transpiled++;
				}
			}
			return list;
		}

		// ldc.r4 300 (the timeout added to Time.unscaledTime)
		public static IEnumerable<CodeInstruction> TextureTimeoutTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo repl = typeof(Hooks).GetMethod("TextureTimeout");
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].opcode == OpCodes.Ldc_R4 && list[i].operand is float && (float)list[i].operand == 300f)
				{
					list[i] = new CodeInstruction(OpCodes.Call, repl);
					Transpiled++;
				}
			}
			return list;
		}
	}
}
