using System;
using System.Linq;

// Debug aid: NRE_PROBE=<type name part> prints, for the first null references thrown inside such a type, the IL around the throw
// (which field / property was null), read from the retargeted assembly with Cecil. Scripts carry no pdb, so this is the line number.
public static class NreProbe
{
	public static void InstallFromEnvironment()
	{
		string filter = Environment.GetEnvironmentVariable("NRE_PROBE");
		if (string.IsNullOrEmpty(filter)) return;
		int shown = 0;
		AppDomain.CurrentDomain.FirstChanceException += (s, ev) =>
		{
			if (!(ev.Exception is NullReferenceException) || shown >= 6) return;
			try
			{
				var fr = new System.Diagnostics.StackTrace(ev.Exception, false).GetFrame(0);
				var m = fr == null ? null : fr.GetMethod();
				if (m == null || m.DeclaringType == null || m.DeclaringType.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
				shown++;
				int off = fr.GetILOffset();
				var mod = Mono.Cecil.ModuleDefinition.ReadModule(m.Module.FullyQualifiedName);
				var td = mod.GetType(m.DeclaringType.FullName.Replace('+', '/'));
				var md = td.Methods.First(x => x.Name == m.Name && x.Parameters.Count == m.GetParameters().Length);
				var ins = md.Body.Instructions.ToList();
				int k = ins.FindIndex(i => i.Offset >= off);
				Console.WriteLine("  nre     " + m.DeclaringType.Name + "." + m.Name + " IL " + off + ": " + string.Join(" ; ", ins.Skip(Math.Max(0, k - 5)).Take(12).Select(i => i.OpCode + " " + i.Operand)));
			}
			catch (Exception) { }
		};
	}
}
