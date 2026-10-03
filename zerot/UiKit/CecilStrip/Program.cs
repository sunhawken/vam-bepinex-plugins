using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

// usage: CecilStrip <in.dll> <out.dll> [--strip Type]... [--noplugin Type]...
//   --strip     removes a top-level type (and its nested types) from the assembly
//   --noplugin  keeps the type but drops its [BepInPlugin] attribute so BepInEx stops loading it as a plugin
internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length < 4)
		{
			Console.Error.WriteLine("usage: CecilStrip in.dll out.dll [--strip Type]... [--noplugin Type]...");
			return 2;
		}
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
		resolver.AddSearchDirectory(@"T:/New folder/VaM_Data/Managed");
		resolver.AddSearchDirectory(@"T:/New folder/BepInEx/core");
		var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
		for (int i = 2; i + 1 < args.Length; i += 2)
		{
			string mode = args[i];
			string name = args[i + 1];
			var t = mode == "--rename" ? null : module.Types.FirstOrDefault(x => x.FullName == name);
			if (t == null && mode != "--rename")
			{
				Console.Error.WriteLine("type not found: " + name);
				return 3;
			}
			if (mode == "--strip")
			{
				module.Types.Remove(t);
				Console.WriteLine("removed " + name);
			}
			else if (mode == "--noplugin")
			{
				var attrs = t.CustomAttributes.Where(a => a.AttributeType.Name == "BepInPlugin").ToList();
				foreach (var a in attrs)
				{
					t.CustomAttributes.Remove(a);
				}
				Console.WriteLine("dropped " + attrs.Count + " BepInPlugin attribute(s) from " + name);
			}
			else if (mode == "--rename")
			{
				module.Assembly.Name.Name = name;
				module.Name = name + ".dll";
				Console.WriteLine("renamed assembly to " + name);
			}
			else
			{
				Console.Error.WriteLine("unknown mode " + mode);
				return 2;
			}
		}
		module.Write(args[1]);
		return 0;
	}
}
