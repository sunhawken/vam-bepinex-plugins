using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
    throw new ArgumentException("Usage: VPBPerformancePatcher <input.dll> <output.dll>");

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var gameRoot = Directory.GetParent(Directory.GetParent(Directory.GetParent(input)!.FullName)!.FullName)!.FullName;
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);
resolver.AddSearchDirectory(Path.Combine(gameRoot, "VaM_Data", "Managed"));
resolver.AddSearchDirectory(Path.Combine(gameRoot, "BepInEx", "core"));
resolver.AddSearchDirectory(Path.Combine(gameRoot, "BepInEx", "plugins"));
var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var module = assembly.MainModule;

static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes.SelectMany(AllTypes)) yield return nested;
}

TypeDefinition FindType(string fullName) =>
    module.Types.SelectMany(AllTypes).Single(t => t.FullName == fullName);

// The gallery policy is applied when this component is attached. Repeating its two
// recursive hierarchy scans in every LateUpdate burns CPU without changing the result.
var chromeType = FindType("VPB.GalleryPaneChromeEnforcer");
var lateUpdate = chromeType.Methods.Single(m => m.Name == "LateUpdate" && !m.HasParameters);
lateUpdate.Body.Instructions.Clear();
lateUpdate.Body.ExceptionHandlers.Clear();
lateUpdate.Body.Variables.Clear();
lateUpdate.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

var hookType = FindType("VPB.VamHookPlugin");
var update = hookType.Methods.Single(m => m.Name == "Update" && !m.HasParameters);
var originalRefresh = hookType.Methods.Single(m => m.Name == "QuickMenuRefreshSlotVisual" && m.Parameters.Count == 1);
var nextRefresh = new FieldDefinition("_perfNextIdleQuickMenuRefresh", FieldAttributes.Private, module.TypeSystem.Single);
var refreshFrame = new FieldDefinition("_perfIdleQuickMenuRefreshFrame", FieldAttributes.Private, module.TypeSystem.Single);
hookType.Fields.Add(nextRefresh);
hookType.Fields.Add(refreshFrame);

var wrapper = new MethodDefinition(
    "QuickMenuRefreshSlotVisualIdleThrottled",
    MethodAttributes.Private | MethodAttributes.HideBySig,
    module.TypeSystem.Void);
wrapper.Parameters.Add(new ParameterDefinition("idx", ParameterAttributes.None, module.TypeSystem.Int32));
hookType.Methods.Add(wrapper);

var getUnscaledTime = module.Types.SelectMany(AllTypes)
    .SelectMany(t => t.Methods).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>()
    .First(m => m.DeclaringType.FullName == "UnityEngine.Time" && m.Name == "get_unscaledTime");
var il = wrapper.Body.GetILProcessor();
var refresh = Instruction.Create(OpCodes.Nop);
var reject = Instruction.Create(OpCodes.Ret);
il.Append(Instruction.Create(OpCodes.Call, getUnscaledTime));
il.Append(Instruction.Create(OpCodes.Ldarg_0));
il.Append(Instruction.Create(OpCodes.Ldfld, refreshFrame));
il.Append(Instruction.Create(OpCodes.Beq, refresh));
il.Append(Instruction.Create(OpCodes.Call, getUnscaledTime));
il.Append(Instruction.Create(OpCodes.Ldarg_0));
il.Append(Instruction.Create(OpCodes.Ldfld, nextRefresh));
il.Append(Instruction.Create(OpCodes.Blt, reject));
il.Append(Instruction.Create(OpCodes.Ldarg_0));
il.Append(Instruction.Create(OpCodes.Call, getUnscaledTime));
il.Append(Instruction.Create(OpCodes.Stfld, refreshFrame));
il.Append(Instruction.Create(OpCodes.Ldarg_0));
il.Append(Instruction.Create(OpCodes.Call, getUnscaledTime));
il.Append(Instruction.Create(OpCodes.Ldc_R4, 0.25f));
il.Append(Instruction.Create(OpCodes.Add));
il.Append(Instruction.Create(OpCodes.Stfld, nextRefresh));
il.Append(refresh);
il.Append(Instruction.Create(OpCodes.Ldarg_0));
il.Append(Instruction.Create(OpCodes.Ldarg_1));
il.Append(Instruction.Create(OpCodes.Call, originalRefresh));
il.Append(reject);

var replaced = 0;
foreach (var instruction in update.Body.Instructions)
{
    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
        instruction.Operand is MethodReference called && called.FullName == originalRefresh.FullName)
    {
        instruction.OpCode = OpCodes.Call;
        instruction.Operand = wrapper;
        replaced++;
    }
}
if (replaced != 1)
    throw new InvalidOperationException($"Expected one idle quick-menu refresh call in Update; found {replaced}.");

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
assembly.Write(output);
Console.WriteLine("Patched VPB: removed the redundant pane scan and throttled idle quick-menu refresh to 4 Hz.");
