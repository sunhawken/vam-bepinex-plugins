using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// vamsim --linkcheck <script-or-plugin.dll>: which members does this assembly use that the shim does not provide?
// (the JIT only finds out when the method first runs - or, for a patched method, when it is patched)
public static class LinkCheck
{
	public static int Run(string dll, string work)
	{
		string shimPath = typeof(UnityEngine.GameObject).Assembly.Location;
		var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetDirectoryName(shimPath)); resolver.AddSearchDirectory(work);
		var shim = ModuleDefinition.ReadModule(shimPath, new ReaderParameters { AssemblyResolver = resolver });
		string patched = Program.Retarget(dll, work);
		var mod = ModuleDefinition.ReadModule(patched, new ReaderParameters { AssemblyResolver = resolver });
		var types = new Dictionary<string, TypeDefinition>();
		foreach (var t in AllTypes(shim.Types)) types[t.FullName] = t;
		var missing = new SortedDictionary<string, int>();
		Action<string> miss = s => { int c; missing.TryGetValue(s, out c); missing[s] = c + 1; };
		foreach (var type in AllTypes(mod.Types))
			foreach (var m in type.Methods)
			{
				if (!m.HasBody) continue;
				foreach (var ins in m.Body.Instructions)
				{
					var mr = ins.Operand as MemberReference;
					if (mr == null) continue;
					var scope = (mr.DeclaringType == null ? null : mr.DeclaringType.Scope) as AssemblyNameReference;
					if (scope == null || scope.Name != "UnityShim") continue;
					string tn = mr.DeclaringType is GenericInstanceType ? ((GenericInstanceType)mr.DeclaringType).ElementType.FullName : mr.DeclaringType.FullName;
					TypeDefinition td;
					if (!types.TryGetValue(tn, out td)) { miss("TYPE " + tn); continue; }
					var fr = mr as FieldReference;
					var mref = mr as MethodReference;
					if (fr != null)
					{
						if (!HasField(td, fr.Name, types)) miss("FIELD " + tn + "::" + fr.Name);
					}
					else if (mref != null && !HasMethod(td, mref, types))
					{
						miss("METHOD " + tn + "::" + mref.Name + "(" + string.Join(",", mref.Parameters.Select(p => ShortName(p.ParameterType))) + ")");
					}
				}
			}
		// the JIT is the real judge: compile every method of the assembly and see which ones it refuses
		var jitFailures = new List<string>();
		try
		{
			AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { string p = Path.Combine(work, new System.Reflection.AssemblyName(e.Name).Name + ".dll"); return File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null; };
			var asm = System.Reflection.Assembly.LoadFrom(patched);
			Type[] ts; try { ts = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException ex) { ts = ex.Types.Where(x => x != null).ToArray(); }
			foreach (var t in ts)
			{
				if (t.ContainsGenericParameters) continue;
				foreach (var mb in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).Cast<System.Reflection.MethodBase>().Concat(t.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)))
				{
					if (mb.IsAbstract || mb.ContainsGenericParameters) continue;
					try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(mb.MethodHandle); }
					catch (Exception ex) { jitFailures.Add(t.FullName + "::" + mb.Name + "  " + ex.GetType().Name + ": " + ex.Message); }
				}
			}
		}
		catch (Exception ex) { jitFailures.Add("could not load: " + ex.Message); }
		Console.WriteLine("== linkcheck " + Path.GetFileName(dll));
		foreach (var kv in missing) Console.WriteLine("  MISSING " + kv.Key + "  x" + kv.Value);
		// can Harmony rewrite every compiler-made coroutine body without producing a program the JIT rejects?
		if (Environment.GetEnvironmentVariable("LINKCHECK_ROUNDTRIP") == "1")
		{
			var asm2 = System.Reflection.Assembly.LoadFrom(patched);
			var hm = new HarmonyLib.Harmony("linkcheck.roundtrip");
			foreach (var t in asm2.GetTypes())
				foreach (var mb in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
				{
					if (mb.Name != "MoveNext") continue;
					try { hm.Patch(mb, null, null, new HarmonyLib.HarmonyMethod(typeof(LinkCheck).GetMethod("Identity"))); hm.Unpatch(mb, HarmonyLib.HarmonyPatchType.All, "linkcheck.roundtrip"); }
					catch (Exception ex) { jitFailures.Add("HARMONY ROUNDTRIP " + t.FullName + "::MoveNext  " + ex.GetType().Name + ": " + ex.Message); }
				}
		}
		foreach (var j in jitFailures) Console.WriteLine("  JIT REFUSES " + j);
		Console.WriteLine(missing.Count == 0 && jitFailures.Count == 0 ? "LINK OK" : missing.Count + " member(s) missing from the shim, " + jitFailures.Count + " method(s) the JIT refuses");
		return missing.Count == 0 && jitFailures.Count == 0 ? 0 : 1;
	}

	public static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Identity(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> i) { return i; }

	static string ShortName(TypeReference t) { return t.Name; }

	static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
	{
		foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; }
	}

	static IEnumerable<TypeDefinition> Chain(TypeDefinition td, Dictionary<string, TypeDefinition> types)
	{
		for (var t = td; t != null; )
		{
			yield return t;
			foreach (var i in t.Interfaces) { TypeDefinition it; if (types.TryGetValue(i.InterfaceType.FullName, out it)) yield return it; }
			TypeDefinition b = null;
			if (t.BaseType != null && types.TryGetValue(t.BaseType is GenericInstanceType ? ((GenericInstanceType)t.BaseType).ElementType.FullName : t.BaseType.FullName, out b)) t = b; else t = null;
		}
	}

	static bool HasField(TypeDefinition td, string name, Dictionary<string, TypeDefinition> types)
	{
		return Chain(td, types).Any(t => t.Fields.Any(f => f.Name == name));
	}

	static bool HasMethod(TypeDefinition td, MethodReference mr, Dictionary<string, TypeDefinition> types)
	{
		return Chain(td, types).Any(t => t.Methods.Any(m => m.Name == mr.Name && m.Parameters.Count == mr.Parameters.Count && SameParams(m, mr)));
	}

	static bool SameParams(MethodDefinition m, MethodReference r)
	{
		for (int i = 0; i < m.Parameters.Count; i++)
		{
			string a = Norm(m.Parameters[i].ParameterType), b = Norm(r.Parameters[i].ParameterType);
			if (a != b && !a.StartsWith("!") && !b.StartsWith("!") && !a.Contains("`") && !b.Contains("`")) return false;
		}
		return true;
	}

	static string Norm(TypeReference t)
	{
		if (t is GenericParameter) return "!" + t.Name;
		if (t is ByReferenceType) return Norm(((ByReferenceType)t).ElementType) + "&";
		if (t is ArrayType) return Norm(((ArrayType)t).ElementType) + "[]";
		if (t is GenericInstanceType) return ((GenericInstanceType)t).ElementType.FullName;
		return t.FullName;
	}
}
