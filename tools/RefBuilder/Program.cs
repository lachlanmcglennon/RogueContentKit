using Mono.Cecil;

// Builds the compile-time reference Assembly-CSharp.dll:
// injects the same fields RogueLibsPatcher adds at preload, then optionally publicizes every member.
// Usage: RefBuilder <game Assembly-CSharp.dll> <output.dll> [--publicize]
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: RefBuilder <in.dll> <out.dll> [--publicize]");
    return 2;
}
string input = args[0], output = args[1];
bool publicize = args.Contains("--publicize");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
using var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var module = asm.MainModule;
var objRef = module.TypeSystem.Object;

void AddField(string typeName, string field)
{
    var t = module.GetType(typeName) ?? throw new Exception("missing type " + typeName);
    if (t.Fields.Any(f => f.Name == field)) return;
    t.Fields.Add(new FieldDefinition(field, FieldAttributes.Public | FieldAttributes.NotSerialized, objRef));
}
foreach (var t in new[] { "InvItem", "PlayfieldObject", "StatusEffect", "Trait", "MainGUI", "WorldSpaceGUI" }) AddField(t, "__RogueLibsHooks");
foreach (var t in new[] { "StatusEffect", "Trait" }) AddField(t, "__RogueLibsContainer");
foreach (var t in new[] { "ButtonData", "Unlock", "tk2dSpriteDefinition" }) AddField(t, "__RogueLibsCustom");

int nTypes = 0, nMembers = 0;
if (publicize)
{
    foreach (var type in module.GetTypes())
    {
        if (type.IsNested) { if (!type.IsNestedPublic) { type.IsNestedPublic = true; nTypes++; } }
        else if (!type.IsPublic) { type.IsPublic = true; nTypes++; }

        var eventNames = new HashSet<string>(type.Events.Select(e => e.Name));
        foreach (var m in type.Methods)
            if (!m.IsPublic) { m.IsPublic = true; nMembers++; }
        foreach (var f in type.Fields)
        {
            if (f.IsPublic || eventNames.Contains(f.Name)) continue;
            if (f.Name.Contains('<')) continue;
            f.IsPublic = true; nMembers++;
        }
    }
}
asm.Write(output);
Console.WriteLine($"wrote {output} (publicized {nTypes} types, {nMembers} members)");
return 0;