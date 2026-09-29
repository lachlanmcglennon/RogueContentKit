using Mono.Cecil;
using Mono.Cecil.Cil;

// Checks RCK's interaction button names against the current game.
// VanillaButtons: every public const needs an AgentInteractions.PressedButton case and a vanilla Interface label.
// CustomButtons: every public const needs a [ButtonLabel] or must reuse a vanilla Interface label.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ButtonCheck <game Assembly-CSharp.dll> <RCK dll>...");
    return 2;
}

var reader = new ReaderParameters { ReadWrite = false, InMemory = true };
using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0]), reader);

TypeDefinition? interfaceDb = game.MainModule.GetType("Google2u.InterfaceNameDB");
TypeDefinition? rowIds = interfaceDb?.NestedTypes.FirstOrDefault(t => t.Name == "rowIds");
TypeDefinition? agentInteractions = game.MainModule.GetType("AgentInteractions");
if (rowIds is null || agentInteractions is null)
{
    Console.WriteLine("ERROR game assembly has no Google2u.InterfaceNameDB/rowIds or AgentInteractions");
    Console.WriteLine("ERRORs: 1");
    return 1;
}

var labels = new HashSet<string>(rowIds.Fields.Where(f => f.IsLiteral && !f.IsSpecialName).Select(f => f.Name), StringComparer.Ordinal);
var cases = new HashSet<string>(StringComparer.Ordinal);
foreach (MethodDefinition method in agentInteractions.Methods.Where(m => m.Name == "PressedButton" && m.HasBody))
{
    foreach (Instruction ins in method.Body.Instructions)
    {
        if (ins.OpCode != OpCodes.Ldstr || ins.Next is not { } next) continue;
        if ((next.OpCode == OpCodes.Call || next.OpCode == OpCodes.Callvirt) && next.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "System.String" && (mr.Name == "op_Equality" || mr.Name == "Equals"))
            cases.Add((string)ins.Operand);
    }
}
Console.WriteLine($"game: {labels.Count} Interface labels, {cases.Count} PressedButton cases");
if (cases.Count < 50) Console.WriteLine("WARN  found suspiciously few PressedButton cases; the string switch may have compiled differently");

int errors = 0, warnings = 0, vanilla = 0, custom = 0;
foreach (string path in args.Skip(1))
{
    using AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(Path.GetFullPath(path), reader);
    string dll = Path.GetFileName(path);
    foreach (TypeDefinition type in asm.MainModule.GetTypes())
    {
        bool isVanilla = type.Name == "VanillaButtons";
        if (!isVanilla && type.Name != "CustomButtons") continue;
        foreach (FieldDefinition field in type.Fields)
        {
            if (!field.IsPublic || !field.HasConstant || field.Constant is not string name) continue;
            CustomAttribute? attr = field.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "ButtonLabelAttribute");
            string where = $"{dll} {type.FullName}.{field.Name} \"{name}\"";
            if (isVanilla)
            {
                vanilla++;
                if (!cases.Contains(name)) { errors++; Console.WriteLine($"ERROR {where}: no AgentInteractions.PressedButton case"); }
                if (!labels.Contains(name)) { errors++; Console.WriteLine($"ERROR {where}: no vanilla Interface label"); }
                if (attr is not null) { warnings++; Console.WriteLine($"WARN  {where}: vanilla buttons must not carry [ButtonLabel]"); }
                continue;
            }
            custom++;
            if (attr is null)
            {
                if (!labels.Contains(name)) { errors++; Console.WriteLine($"ERROR {where}: no [ButtonLabel] and no vanilla Interface label (shows as E_{name})"); }
            }
            else
            {
                string? english = attr.ConstructorArguments.Count > 0 ? attr.ConstructorArguments[0].Value as string : null;
                if (string.IsNullOrWhiteSpace(english)) { errors++; Console.WriteLine($"ERROR {where}: empty [ButtonLabel]"); }
                if (labels.Contains(name)) { warnings++; Console.WriteLine($"WARN  {where}: [ButtonLabel] overrides the vanilla label and its translations"); }
            }
        }
    }
}

Console.WriteLine($"VanillaButtons: {vanilla}; CustomButtons: {custom}");
Console.WriteLine($"ERRORs: {errors}");
Console.WriteLine($"WARNs: {warnings}");
return errors > 0 ? 1 : 0;
