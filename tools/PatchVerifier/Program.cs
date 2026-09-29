using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace PatchVerifier;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PatchVerifier <plugin.dll> <game Assembly-CSharp.dll> [--refdir <dir>] [--original <unpublicized Assembly-CSharp.dll>] [--dump-il Type::Method]");
            return 2;
        }

        string pluginPath = Path.GetFullPath(args[0]);
        string gamePath = Path.GetFullPath(args[1]);
        var refDirs = new List<string>();
        string? originalPath = null;
        string? dumpIl = null;

        for (int i = 2; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--refdir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                refDirs.Add(Path.GetFullPath(args[++i]));
            else if (arg.Equals("--original", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                originalPath = Path.GetFullPath(args[++i]);
            else if (arg.Equals("--dump-il", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                dumpIl = args[++i];
            else
            {
                Console.Error.WriteLine($"unknown or incomplete argument: {arg}");
                return 2;
            }
        }

        var resolver = new DefaultAssemblyResolver();
        AddDirectory(resolver, Path.GetDirectoryName(pluginPath));
        AddDirectory(resolver, Path.GetDirectoryName(gamePath));
        foreach (string dir in refDirs) AddDirectory(resolver, dir);
        if (originalPath is not null) AddDirectory(resolver, Path.GetDirectoryName(originalPath));

        var reader = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false, InMemory = true };
        using AssemblyDefinition gameAssembly = AssemblyDefinition.ReadAssembly(gamePath, reader);

        if (dumpIl is not null)
        {
            using AssemblyDefinition pluginForDump = AssemblyDefinition.ReadAssembly(pluginPath, reader);
            string dump = DumpIl(gameAssembly.MainModule, dumpIl);
            if (dump.StartsWith("type not found:", StringComparison.Ordinal))
                dump = DumpIl(pluginForDump.MainModule, dumpIl);
            Console.WriteLine(dump);
            return 0;
        }

        using AssemblyDefinition pluginAssembly = AssemblyDefinition.ReadAssembly(pluginPath, reader);
        AssemblyDefinition? originalAssembly = null;
        try
        {
            if (originalPath is not null && File.Exists(originalPath))
                originalAssembly = AssemblyDefinition.ReadAssembly(originalPath, reader);

            var context = new VerifyContext(pluginPath, gamePath, refDirs, pluginAssembly, gameAssembly, originalAssembly, resolver);
            var analyzer = new IlAnalyzer(context);
            analyzer.Analyze();
            HarmonyAttributeScanner.Scan(context);
            Verifier.VerifyAll(context);

            string report = ReportWriter.Build(context);
            Console.WriteLine(report);

            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "PatchVerifier", "last-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));
            return context.Errors.Count == 0 ? 0 : 1;
        }
        finally
        {
            originalAssembly?.Dispose();
        }
    }

    private static void AddDirectory(DefaultAssemblyResolver resolver, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            resolver.AddSearchDirectory(path);
    }

    private static string DumpIl(ModuleDefinition module, string spec)
    {
        int sep = spec.IndexOf("::", StringComparison.Ordinal);
        if (sep < 0) return "dump-il spec must be Type::Method";

        string typeName = spec[..sep];
        string methodName = spec[(sep + 2)..];
        var ctx = new SimpleTypeIndex(module);
        TypeDefinition? type = ctx.ResolveByName(typeName);
        if (type is null) return $"type not found: {typeName}";

        var methods = Verifier.GetCandidateMethods(type, methodName, declaredOnly: false).ToList();
        if (methods.Count == 0) return $"method not found: {type.FullName}::{methodName}";
        MethodDefinition method = Verifier.ChooseMostParameters(methods);

        var sb = new StringBuilder();
        sb.AppendLine($"{Display.Type(type)}::{method.Name}{Display.Parameters(method)}");
        if (!method.HasBody)
        {
            sb.AppendLine("  <no body>");
            return sb.ToString();
        }
        foreach (Instruction instr in method.Body.Instructions)
            sb.AppendLine($"  IL_{instr.Offset:x4}: {instr.OpCode,-12} {Display.Operand(instr.Operand)}");
        return sb.ToString();
    }
}

internal sealed class VerifyContext
{
    public VerifyContext(
        string pluginPath,
        string gamePath,
        IReadOnlyList<string> refDirs,
        AssemblyDefinition pluginAssembly,
        AssemblyDefinition gameAssembly,
        AssemblyDefinition? originalAssembly,
        IAssemblyResolver resolver)
    {
        PluginPath = pluginPath;
        GamePath = gamePath;
        RefDirs = refDirs;
        PluginAssembly = pluginAssembly;
        GameAssembly = gameAssembly;
        OriginalAssembly = originalAssembly;
        Resolver = resolver;
        GameTypes = new SimpleTypeIndex(gameAssembly.MainModule);
        PluginTypes = new SimpleTypeIndex(pluginAssembly.MainModule);
        OriginalTypes = originalAssembly is null ? null : new SimpleTypeIndex(originalAssembly.MainModule);
        RefAssemblies = LoadRefAssemblies(refDirs, resolver, gamePath, pluginPath, originalAssembly?.MainModule.FileName);
        RefTypes = RefAssemblies.Select(a => new SimpleTypeIndex(a.MainModule)).ToList();
        DefaultRoguePatcherPatchType = pluginAssembly.MainModule.GetTypes()
            .FirstOrDefault(t => t.CustomAttributes.Any(a => a.AttributeType.Name is "BepInPlugin" or "BepInPluginAttribute"))
            ?? pluginAssembly.MainModule.GetTypes().FirstOrDefault(t => t.Name.EndsWith("Plugin", StringComparison.Ordinal));
    }

    public string PluginPath { get; }
    public string GamePath { get; }
    public IReadOnlyList<string> RefDirs { get; }
    public AssemblyDefinition PluginAssembly { get; }
    public AssemblyDefinition GameAssembly { get; }
    public AssemblyDefinition? OriginalAssembly { get; }
    public IAssemblyResolver Resolver { get; }
    public SimpleTypeIndex GameTypes { get; }
    public SimpleTypeIndex PluginTypes { get; }
    public SimpleTypeIndex? OriginalTypes { get; }
    public List<AssemblyDefinition> RefAssemblies { get; }
    public List<SimpleTypeIndex> RefTypes { get; }
    public TypeReference? DefaultRoguePatcherPatchType { get; }

    public List<PatchSite> PatchSites { get; } = new();
    public List<DirectPatchSite> DirectPatchSites { get; } = new();
    public List<AttributePatchSite> AttributePatchSites { get; } = new();
    public List<LookupRecord> Lookups { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Infos { get; } = new();
    public List<string> OverloadChoices { get; } = new();
    public List<string> Transpilers { get; } = new();
    public HashSet<string> ImportedMembers { get; } = new(StringComparer.Ordinal);

    private static List<AssemblyDefinition> LoadRefAssemblies(IReadOnlyList<string> refDirs, IAssemblyResolver resolver, params string?[] skipPaths)
    {
        var assemblies = new List<AssemblyDefinition>();
        var skip = new HashSet<string>(skipPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p!)), StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in refDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string dll in Directory.EnumerateFiles(dir, "*.dll"))
            {
                string full = Path.GetFullPath(dll);
                if (skip.Contains(full)) continue;
                try
                {
                    var asm = AssemblyDefinition.ReadAssembly(full, new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false, InMemory = true });
                    if (!seenNames.Add(asm.Name.Name))
                    {
                        asm.Dispose();
                        continue;
                    }
                    assemblies.Add(asm);
                }
                catch
                {
                    // Some native/helper DLLs are not managed assemblies.
                }
            }
        }
        return assemblies;
    }
}

internal sealed class SimpleTypeIndex
{
    private readonly Dictionary<string, TypeDefinition> byFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TypeDefinition>> byShortName = new(StringComparer.Ordinal);

    public SimpleTypeIndex(ModuleDefinition module)
    {
        foreach (TypeDefinition type in module.GetTypes())
        {
            Add(type.FullName, type);
            Add(type.FullName.Replace('/', '+'), type);
            Add(type.FullName.Replace('/', '.'), type);
            AddShort(type.Name, type);
            AddShort(type.Name.Split('`')[0], type);
        }
    }

    private void Add(string key, TypeDefinition type) => byFullName.TryAdd(key, type);
    private void AddShort(string key, TypeDefinition type)
    {
        if (!byShortName.TryGetValue(key, out var list)) byShortName[key] = list = new List<TypeDefinition>();
        list.Add(type);
    }

    public TypeDefinition? Resolve(TypeReference? reference)
    {
        if (reference is null) return null;
        TypeReference element = TypeHelpers.DefinitionElement(reference);
        return ResolveByName(element.FullName) ?? ResolveByName(element.FullName.Replace('/', '+')) ?? ResolveByName(element.Name);
    }

    public TypeDefinition? ResolveByName(string name)
    {
        string cecilName = name.Replace('+', '/');
        if (byFullName.TryGetValue(cecilName, out TypeDefinition? full)) return full;
        if (byFullName.TryGetValue(name, out full)) return full;
        if (byFullName.TryGetValue(name.Replace('/', '.'), out full)) return full;
        if (byShortName.TryGetValue(name, out var list) && list.Count == 1) return list[0];
        return null;
    }
}

internal enum LookupKind
{
    Method,
    DeclaredMethod,
    Field,
    DeclaredField,
    Property,
    DeclaredProperty,
    PropertyGetter,
    PropertySetter,
    NestedType,
    TypeByName,
}

internal sealed record LookupRecord(
    LookupKind Kind,
    TypeReference? TargetType,
    string? TargetName,
    IReadOnlyList<TypeReference>? ParameterTypes,
    string Source,
    string Api)
{
    public MethodDefinition? ResolvedMethod { get; set; }
    public FieldDefinition? ResolvedField { get; set; }
    public PropertyDefinition? ResolvedProperty { get; set; }
    public TypeDefinition? ResolvedType { get; set; }

    public string Describe()
    {
        string type = TargetType is null ? "<unknown>" : Display.Type(TargetType);
        string name = TargetName ?? "<unknown>";
        string parameters = ParameterTypes is null ? "" : $"({string.Join(", ", ParameterTypes.Select(Display.Type))})";
        return Kind == LookupKind.TypeByName ? $"{Api}({name}) in {Source}" : $"{Api}({type}::{name}{parameters}) in {Source}";
    }
}

internal sealed record PatchSite(
    string Kind,
    TypeReference? TargetType,
    string? TargetMethod,
    string? PatchMethod,
    TypeReference? PatchType,
    IReadOnlyList<TypeReference>? TargetParameterTypes,
    string Source);

internal sealed record DirectPatchSite(
    string Kind,
    LookupRecord? Original,
    LookupRecord? Patch,
    string Source);

internal sealed record AttributePatchSite(
    string Kind,
    TypeReference? TargetType,
    string? TargetMethod,
    IReadOnlyList<TypeReference>? TargetParameterTypes,
    MethodDefinition PatchMethod,
    string Source);

internal abstract record SymbolValue
{
    public static readonly UnknownValue Unknown = new();
    public static readonly NullValue Null = new();
}

internal sealed record UnknownValue : SymbolValue;
internal sealed record NullValue : SymbolValue;
internal sealed record IntValue(int Value) : SymbolValue;
internal sealed record StringValue(string Value) : SymbolValue;
internal sealed record RuntimeTypeHandleValue(TypeReference Type) : SymbolValue;
internal sealed record TypeValue(TypeReference Type) : SymbolValue;
internal sealed record ThisValue(TypeReference Type) : SymbolValue;

internal sealed record TypeArrayValue : SymbolValue
{
    public TypeArrayValue(int length)
    {
        // typed as SymbolValue: an UnknownValue[] would reject storing a TypeValue (array covariance)
        Elements = Enumerable.Repeat<SymbolValue>(SymbolValue.Unknown, Math.Max(0, length)).ToArray();
    }

    public SymbolValue[] Elements { get; }

    public IReadOnlyList<TypeReference>? ToTypeList()
    {
        var types = new List<TypeReference>();
        foreach (SymbolValue value in Elements)
        {
            if (value is not TypeValue typeValue) return null;
            types.Add(typeValue.Type);
        }
        return types;
    }
}

internal sealed record LookupValue(LookupRecord Record) : SymbolValue;
internal sealed record HarmonyMethodValue(LookupRecord? Method) : SymbolValue;

internal sealed class IlAnalyzer
{
    private static readonly HashSet<string> RoguePatchNames = new(StringComparer.Ordinal)
    {
        "Prefix", "Postfix", "Transpiler", "Finalizer",
    };

    private readonly VerifyContext context;

    public IlAnalyzer(VerifyContext context) => this.context = context;

    public void Analyze()
    {
        foreach (TypeDefinition type in context.PluginAssembly.MainModule.GetTypes())
        {
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.HasBody) AnalyzeMethod(method);
            }
        }
    }

    private void AnalyzeMethod(MethodDefinition source)
    {
        var stack = new List<SymbolValue>();
        var locals = new Dictionary<int, SymbolValue>();
        TypeReference currentPatchType = context.DefaultRoguePatcherPatchType ?? source.DeclaringType;

        foreach (Instruction instr in source.Body.Instructions)
        {
            try
            {
                switch (instr.OpCode.Code)
                {
                    case Code.Nop:
                    case Code.Break:
                        continue;
                    case Code.Ldstr:
                        stack.Add(new StringValue((string)instr.Operand));
                        continue;
                    case Code.Ldnull:
                        stack.Add(SymbolValue.Null);
                        continue;
                    case Code.Ldc_I4_M1:
                    case Code.Ldc_I4_0:
                    case Code.Ldc_I4_1:
                    case Code.Ldc_I4_2:
                    case Code.Ldc_I4_3:
                    case Code.Ldc_I4_4:
                    case Code.Ldc_I4_5:
                    case Code.Ldc_I4_6:
                    case Code.Ldc_I4_7:
                    case Code.Ldc_I4_8:
                    case Code.Ldc_I4_S:
                    case Code.Ldc_I4:
                        stack.Add(new IntValue(GetInt(instr)));
                        continue;
                    case Code.Ldarg_0 when source.HasThis:
                        stack.Add(new ThisValue(source.DeclaringType));
                        continue;
                    case Code.Ldtoken when instr.Operand is TypeReference typeReference:
                        stack.Add(new RuntimeTypeHandleValue(typeReference));
                        continue;
                    case Code.Newarr:
                    {
                        SymbolValue len = Pop(stack);
                        stack.Add(len is IntValue intValue ? new TypeArrayValue(intValue.Value) : SymbolValue.Unknown);
                        continue;
                    }
                    case Code.Dup:
                    {
                        if (stack.Count == 0) stack.Clear();
                        else stack.Add(stack[^1]);
                        continue;
                    }
                    case Code.Stelem_Ref:
                    {
                        SymbolValue value = Pop(stack);
                        SymbolValue index = Pop(stack);
                        SymbolValue array = Pop(stack);
                        if (array is TypeArrayValue arr && index is IntValue { Value: >= 0 } i && i.Value < arr.Elements.Length)
                            arr.Elements[i.Value] = value;
                        continue;
                    }
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                    case Code.Stloc_S:
                    case Code.Stloc:
                        locals[GetLocalIndex(instr)] = Pop(stack);
                        continue;
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                        stack.Add(locals.TryGetValue(GetLocalIndex(instr), out SymbolValue? local) ? local : SymbolValue.Unknown);
                        continue;
                    case Code.Ldsfld when instr.Operand is FieldReference field && field.DeclaringType.FullName == "System.Type" && field.Name == "EmptyTypes":
                        stack.Add(new TypeArrayValue(0));
                        continue;
                    case Code.Call:
                    case Code.Callvirt:
                    {
                        if (instr.Operand is not MethodReference called)
                        {
                            GenericStackEffect(instr, stack);
                            continue;
                        }

                        if (called.Name == "GetTypeFromHandle")
                        {
                            SymbolValue handleArg = Pop(stack);
                            stack.Add(handleArg is RuntimeTypeHandleValue handle ? new TypeValue(NormalizeRuntimeType(handle.Type)) : SymbolValue.Unknown);
                            continue;
                        }

                        List<SymbolValue> args = PopCallArguments(stack, called, isNewObj: false, instr.OpCode.Code);
                        SymbolValue? pushed = HandleCall(source, instr, called, args, ref currentPatchType);
                        if (pushed is not null)
                            stack.Add(pushed);
                        else if (ReturnsValue(called))
                            stack.Add(SymbolValue.Unknown);
                        continue;
                    }
                    case Code.Newobj:
                    {
                        if (instr.Operand is not MethodReference ctor)
                        {
                            GenericStackEffect(instr, stack);
                            continue;
                        }

                        List<SymbolValue> args = PopCallArguments(stack, ctor, isNewObj: true, instr.OpCode.Code);
                        // new RoguePatcher(plugin, typeof(Patches)) sets TypeWithPatches
                        if (TypeHelpers.DefinitionElement(ctor.DeclaringType).FullName == "RogueLibsCore.RoguePatcher"
                            && args.Count == 2 && args[1] is TypeValue patchTypeValue)
                            currentPatchType = patchTypeValue.Type;
                        SymbolValue? pushed = HandleNewObj(source, ctor, args);
                        stack.Add(pushed ?? SymbolValue.Unknown);
                        continue;
                    }
                    default:
                        GenericStackEffect(instr, stack);
                        continue;
                }
            }
            catch
            {
                stack.Clear();
            }
        }
    }

    private SymbolValue? HandleCall(MethodDefinition source, Instruction instr, MethodReference called, List<SymbolValue> args, ref TypeReference currentPatchType)
    {
        string declaringType = TypeHelpers.DefinitionElement(called.DeclaringType).FullName;
        string name = called.Name;

        if (declaringType == "System.Type" && name == "GetTypeFromHandle")
        {
            if (args.Count > 0 && args[0] is RuntimeTypeHandleValue handle)
                return new TypeValue(NormalizeRuntimeType(handle.Type));
            return SymbolValue.Unknown;
        }

        // this.GetType(), e.g. new RoguePatcher(plugin, GetType())
        if (declaringType == "System.Object" && name == "GetType")
            return args.Count > 0 && args[0] is ThisValue thisValue ? new TypeValue(thisValue.Type) : SymbolValue.Unknown;

        if (declaringType == "RogueLibsCore.RoguePatcher" && name == "set_TypeWithPatches")
        {
            if (args.Count > 1 && args[1] is TypeValue typeValue)
                currentPatchType = typeValue.Type;
            return null;
        }

        if (declaringType == "RogueLibsCore.RoguePatcher" && RoguePatchNames.Contains(name))
        {
            RecordRoguePatch(source, name, args, currentPatchType, instr);
            return new IntValue(1);
        }

        if (declaringType == "HarmonyLib.AccessTools")
        {
            SymbolValue? lookup = HandleAccessTools(source, called, args);
            if (lookup is not null) return lookup;
        }

        if (declaringType == "System.Type")
        {
            SymbolValue? lookup = HandleSystemTypeLookup(source, called, args);
            if (lookup is not null) return lookup;
        }

        if (declaringType == "HarmonyLib.Harmony" && name == "Patch")
        {
            RecordHarmonyPatch(source, args);
            return SymbolValue.Unknown;
        }

        // a plugin's own null-check helper (e.g. RogueLibsPlus' Fixes.Need(member, description)) passes the lookup through
        if (name == "Need" && args.Count >= 1 && args[0] is LookupValue or TypeValue
            && called.DeclaringType.Scope == source.Module)
            return args[0];

        return null;
    }

    private SymbolValue? HandleNewObj(MethodDefinition source, MethodReference ctor, List<SymbolValue> args)
    {
        string declaringType = TypeHelpers.DefinitionElement(ctor.DeclaringType).FullName;
        if (declaringType != "HarmonyLib.HarmonyMethod") return null;

        if (args.Count == 1 && args[0] is LookupValue methodLookup)
            return new HarmonyMethodValue(methodLookup.Record);
        if (args.Count >= 2 && args[0] is TypeValue typeValue && args[1] is StringValue name)
        {
            var record = AddLookup(source, LookupKind.Method, typeValue.Type, name.Value, null, "HarmonyMethod::.ctor");
            return new HarmonyMethodValue(record);
        }
        return new HarmonyMethodValue(null);
    }

    private SymbolValue? HandleAccessTools(MethodDefinition source, MethodReference called, List<SymbolValue> args)
    {
        string name = called.Name;
        string api = "AccessTools." + name;

        if (name is "Method" or "DeclaredMethod")
        {
            if (TryTypeAndString(args, 0, 1, out TypeReference? type, out string? memberName))
            {
                IReadOnlyList<TypeReference>? parameters = FirstTypeArray(args.Skip(2));
                var kind = name == "DeclaredMethod" ? LookupKind.DeclaredMethod : LookupKind.Method;
                return new LookupValue(AddLookup(source, kind, type, memberName, parameters, api));
            }
            return new LookupValue(AddLookup(source, name == "DeclaredMethod" ? LookupKind.DeclaredMethod : LookupKind.Method, null, null, null, api));
        }

        if (name is "Field" or "DeclaredField")
        {
            if (TryTypeAndString(args, 0, 1, out TypeReference? type, out string? memberName))
            {
                var kind = name == "DeclaredField" ? LookupKind.DeclaredField : LookupKind.Field;
                return new LookupValue(AddLookup(source, kind, type, memberName, null, api));
            }
            return new LookupValue(AddLookup(source, name == "DeclaredField" ? LookupKind.DeclaredField : LookupKind.Field, null, null, null, api));
        }

        if (name is "Property" or "DeclaredProperty" or "PropertyGetter" or "PropertySetter")
        {
            if (TryTypeAndString(args, 0, 1, out TypeReference? type, out string? memberName))
            {
                LookupKind kind = name switch
                {
                    "DeclaredProperty" => LookupKind.DeclaredProperty,
                    "PropertyGetter" => LookupKind.PropertyGetter,
                    "PropertySetter" => LookupKind.PropertySetter,
                    _ => LookupKind.Property,
                };
                return new LookupValue(AddLookup(source, kind, type, memberName, null, api));
            }
            return new LookupValue(AddLookup(source, LookupKind.Property, null, null, null, api));
        }

        if (name == "Inner")
        {
            if (TryTypeAndString(args, 0, 1, out TypeReference? type, out string? memberName))
            {
                var record = AddLookup(source, LookupKind.NestedType, type, memberName, null, api);
                TypeDefinition? nested = Verifier.ResolveNestedType(ResolveAny(type!), memberName!);
                return nested is null ? SymbolValue.Unknown : new TypeValue(nested);
            }
            return SymbolValue.Unknown;
        }

        if (name == "TypeByName")
        {
            string? typeName = args.Count > 0 && args[0] is StringValue str ? str.Value : null;
            var record = AddLookup(source, LookupKind.TypeByName, null, typeName, null, api);
            TypeDefinition? resolved = typeName is null ? null : ResolveAny(typeName);
            return resolved is null ? SymbolValue.Unknown : new TypeValue(resolved);
        }

        return null;
    }

    private SymbolValue? HandleSystemTypeLookup(MethodDefinition source, MethodReference called, List<SymbolValue> args)
    {
        if (args.Count == 0 || args[0] is not TypeValue receiver) return null;
        string name = called.Name;
        if (name is not ("GetMethod" or "GetField" or "GetProperty" or "GetNestedType")) return null;

        string? memberName = args.Count > 1 && args[1] is StringValue str ? str.Value : null;
        IReadOnlyList<TypeReference>? parameters = name is "GetMethod" or "GetProperty" ? FirstTypeArray(args.Skip(2)) : null;
        LookupKind kind = name switch
        {
            "GetMethod" => LookupKind.Method,
            "GetField" => LookupKind.Field,
            "GetProperty" => LookupKind.Property,
            "GetNestedType" => LookupKind.NestedType,
            _ => LookupKind.Method,
        };
        return new LookupValue(AddLookup(source, kind, receiver.Type, memberName, parameters, "Type." + name));
    }

    private void RecordRoguePatch(MethodDefinition source, string kind, List<SymbolValue> args, TypeReference currentPatchType, Instruction instr)
    {
        int start = -1;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] is TypeValue && args[i + 1] is StringValue)
            {
                start = i;
                break;
            }
        }

        TypeReference? targetType = start >= 0 && args[start] is TypeValue type ? type.Type : null;
        string? targetMethod = start >= 0 && args[start + 1] is StringValue method ? method.Value : null;
        string? patchMethod = null;
        IReadOnlyList<TypeReference>? parameterTypes = null;

        int next = start + 2;
        if (start >= 0 && args.Count > next && args[next] is StringValue patch)
        {
            patchMethod = patch.Value;
            parameterTypes = args.Count > next + 1 && args[next + 1] is TypeArrayValue explicitArray ? explicitArray.ToTypeList() : null;
        }
        else
        {
            if (targetType is not null && targetMethod is not null)
                patchMethod = $"{targetType.Name}_{targetMethod}";
            parameterTypes = start >= 0 && args.Count > next && args[next] is TypeArrayValue defaultArray ? defaultArray.ToTypeList() : null;
        }

        if ((targetType is null || targetMethod is null) && TryDecodeRoguePatchFromIl(source, instr, out TypeReference? ilTargetType, out string? ilTargetMethod, out string? ilPatchMethod, out IReadOnlyList<TypeReference>? ilParameterTypes))
        {
            targetType ??= ilTargetType;
            targetMethod ??= ilTargetMethod;
            patchMethod ??= ilPatchMethod;
            parameterTypes ??= ilParameterTypes;
        }

        context.PatchSites.Add(new PatchSite(kind, targetType, targetMethod, patchMethod, currentPatchType, parameterTypes, Display.Method(source)));
    }

    private static bool TryDecodeRoguePatchFromIl(MethodDefinition source, Instruction call, out TypeReference? targetType, out string? targetMethod, out string? patchMethod, out IReadOnlyList<TypeReference>? parameterTypes)
    {
        targetType = null;
        targetMethod = null;
        patchMethod = null;
        parameterTypes = null;

        var instructions = source.Body.Instructions;
        int index = instructions.IndexOf(call);
        if (index < 0) return false;

        int start = Math.Max(0, index - 80);
        for (int i = index - 1; i >= start; i--)
        {
            if (instructions[i].Operand is FieldReference field && (field.Name == "Patcher" || field.Name == "patcher"))
            {
                start = i;
                break;
            }
            if (instructions[i].OpCode.Code == Code.Pop)
            {
                start = i + 1;
                break;
            }
        }

        var strings = new List<string>();
        var tokens = new List<(int Index, TypeReference Type)>();
        int newarrIndex = -1;
        for (int i = start; i < index; i++)
        {
            Instruction current = instructions[i];
            if (current.OpCode.Code == Code.Ldstr && current.Operand is string s)
                strings.Add(s);
            else if (current.OpCode.Code == Code.Ldtoken && current.Operand is TypeReference type)
                tokens.Add((i, NormalizeRuntimeType(type)));
            else if (current.OpCode.Code == Code.Newarr && current.Operand is TypeReference arrType && arrType.FullName == "System.Type")
                newarrIndex = i;
        }

        targetType = tokens.FirstOrDefault(t => newarrIndex < 0 || t.Index < newarrIndex).Type;
        if (strings.Count > 0) targetMethod = strings[0];
        if (strings.Count > 1) patchMethod = strings[1];
        if (targetType is not null && targetMethod is not null && patchMethod is null)
            patchMethod = $"{targetType.Name}_{targetMethod}";
        if (newarrIndex >= 0)
            parameterTypes = tokens.Where(t => t.Index > newarrIndex).Select(t => t.Type).ToList();

        return targetType is not null && targetMethod is not null;
    }

    private static string DebugValue(SymbolValue value) => value switch
    {
        TypeValue t => "Type:" + Display.Type(t.Type),
        StringValue s => "String:" + s.Value,
        TypeArrayValue a => "TypeArray[" + a.Elements.Length + "]",
        IntValue i => "Int:" + i.Value,
        NullValue => "null",
        UnknownValue => "unknown",
        _ => value.GetType().Name,
    };

    private static TypeReference NormalizeRuntimeType(TypeReference type)
    {
        if (type is GenericParameter gp && gp.Constraints.Count > 0)
            return gp.Constraints[0].ConstraintType;
        return type;
    }

    private void RecordHarmonyPatch(MethodDefinition source, List<SymbolValue> args)
    {
        LookupRecord? original = args.Count > 1 && args[1] is LookupValue originalLookup ? originalLookup.Record : null;
        string[] kinds = { "Prefix", "Postfix", "Transpiler", "Finalizer", "ILManipulator" };
        for (int i = 0; i < kinds.Length; i++)
        {
            int argIndex = i + 2;
            if (argIndex >= args.Count) break;
            if (args[argIndex] is HarmonyMethodValue harmonyMethod && harmonyMethod.Method is not null)
                context.DirectPatchSites.Add(new DirectPatchSite(kinds[i], original, harmonyMethod.Method, Display.Method(source)));
        }
    }

    private LookupRecord AddLookup(MethodDefinition source, LookupKind kind, TypeReference? type, string? name, IReadOnlyList<TypeReference>? parameters, string api)
    {
        var record = new LookupRecord(kind, type, name, parameters, Display.Method(source), api);
        context.Lookups.Add(record);
        return record;
    }

    private TypeDefinition? ResolveAny(TypeReference reference) => Verifier.ResolveType(context, reference);
    private TypeDefinition? ResolveAny(string name) => Verifier.ResolveTypeByName(context, name);

    private static bool TryTypeAndString(List<SymbolValue> args, int typeIndex, int stringIndex, out TypeReference? type, out string? name)
    {
        type = args.Count > typeIndex && args[typeIndex] is TypeValue typeValue ? typeValue.Type : null;
        name = args.Count > stringIndex && args[stringIndex] is StringValue stringValue ? stringValue.Value : null;
        return type is not null && name is not null;
    }

    private static IReadOnlyList<TypeReference>? FirstTypeArray(IEnumerable<SymbolValue> values)
    {
        foreach (SymbolValue value in values)
        {
            if (value is TypeArrayValue arr)
                return arr.ToTypeList();
        }
        return null;
    }

    private static List<SymbolValue> PopCallArguments(List<SymbolValue> stack, MethodReference method, bool isNewObj, Code code)
    {
        int count = method.Parameters.Count + (MethodHasInstance(method, isNewObj, code) ? 1 : 0);
        var args = new SymbolValue[count];
        for (int i = count - 1; i >= 0; i--)
            args[i] = Pop(stack);
        return args.ToList();
    }

    private static bool MethodHasInstance(MethodReference method, bool isNewObj, Code code)
    {
        if (isNewObj) return false;
        string declaringType = TypeHelpers.DefinitionElement(method.DeclaringType).FullName;
        if (method.Name == "GetTypeFromHandle") return false;
        if (declaringType == "HarmonyLib.AccessTools") return false;
        try
        {
            MethodDefinition? resolved = method.Resolve();
            if (resolved is not null) return !resolved.IsStatic;
        }
        catch { }
        return code == Code.Callvirt || method.HasThis;
    }

    private static bool ReturnsValue(MethodReference method) => method.ReturnType.FullName != "System.Void";

    private static SymbolValue Pop(List<SymbolValue> stack)
    {
        if (stack.Count == 0) return SymbolValue.Unknown;
        SymbolValue value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private static void GenericStackEffect(Instruction instr, List<SymbolValue> stack)
    {
        int pops = PopCount(instr);
        if (pops > stack.Count)
        {
            stack.Clear();
        }
        else
        {
            stack.RemoveRange(stack.Count - pops, pops);
        }

        int pushes = PushCount(instr);
        for (int i = 0; i < pushes; i++) stack.Add(SymbolValue.Unknown);
    }

    private static int PopCount(Instruction instr)
    {
        if (instr.OpCode.StackBehaviourPop == StackBehaviour.Varpop)
        {
            if (instr.Operand is MethodReference method)
                return method.Parameters.Count + (MethodHasInstance(method, instr.OpCode.Code == Code.Newobj, instr.OpCode.Code) ? 1 : 0);
            return 0;
        }

        return instr.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            _ => 0,
        };
    }

    private static int PushCount(Instruction instr)
    {
        if (instr.OpCode.StackBehaviourPush == StackBehaviour.Varpush)
        {
            if (instr.OpCode.Code == Code.Newobj) return 1;
            if (instr.Operand is MethodReference method) return ReturnsValue(method) ? 1 : 0;
            return 0;
        }

        return instr.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => 0,
        };
    }

    private static int GetInt(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1,
        Code.Ldc_I4_0 => 0,
        Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5,
        Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)instruction.Operand,
        _ => (int)instruction.Operand,
    };

    private static int GetLocalIndex(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Stloc_0 or Code.Ldloc_0 => 0,
        Code.Stloc_1 or Code.Ldloc_1 => 1,
        Code.Stloc_2 or Code.Ldloc_2 => 2,
        Code.Stloc_3 or Code.Ldloc_3 => 3,
        _ => ((VariableDefinition)instruction.Operand).Index,
    };
}

internal static class HarmonyAttributeScanner
{
    public static void Scan(VerifyContext context)
    {
        foreach (TypeDefinition type in context.PluginAssembly.MainModule.GetTypes())
        {
            PatchAttributeData typeData = ReadPatchAttributes(type.CustomAttributes);
            foreach (MethodDefinition method in type.Methods)
            {
                string? kind = PatchKind(method);
                if (kind is null && !typeData.IsEmpty && method.Name is "Prefix" or "Postfix" or "Transpiler" or "Finalizer")
                    kind = method.Name;
                PatchAttributeData methodData = ReadPatchAttributes(method.CustomAttributes);
                if (kind is null && methodData.IsEmpty) continue;

                TypeReference? targetType = methodData.TargetType ?? typeData.TargetType;
                string? targetMethod = methodData.MethodName ?? typeData.MethodName;
                IReadOnlyList<TypeReference>? parameters = methodData.ArgumentTypes ?? typeData.ArgumentTypes;
                context.AttributePatchSites.Add(new AttributePatchSite(kind ?? "HarmonyPatch", targetType, targetMethod, parameters, method, Display.Method(method)));
            }
        }
    }

    private static string? PatchKind(MethodDefinition method)
    {
        foreach (CustomAttribute attr in method.CustomAttributes)
        {
            string name = attr.AttributeType.Name;
            if (name is "HarmonyPrefix" or "HarmonyPrefixAttribute") return "Prefix";
            if (name is "HarmonyPostfix" or "HarmonyPostfixAttribute") return "Postfix";
            if (name is "HarmonyTranspiler" or "HarmonyTranspilerAttribute") return "Transpiler";
            if (name is "HarmonyFinalizer" or "HarmonyFinalizerAttribute") return "Finalizer";
        }
        return null;
    }

    private static PatchAttributeData ReadPatchAttributes(Collection<CustomAttribute> attrs)
    {
        var data = new PatchAttributeData();
        foreach (CustomAttribute attr in attrs)
        {
            string name = attr.AttributeType.Name;
            if (name is not ("HarmonyPatch" or "HarmonyPatchAttribute")) continue;

            foreach (CustomAttributeArgument arg in attr.ConstructorArguments)
            {
                if (arg.Value is TypeReference typeRef) data.TargetType ??= typeRef;
                else if (arg.Value is string str) data.MethodName ??= str;
                else if (arg.Value is CustomAttributeArgument[] arr)
                {
                    var types = arr.Select(a => a.Value).OfType<TypeReference>().ToList();
                    if (types.Count > 0) data.ArgumentTypes ??= types;
                }
            }

            foreach (CustomAttributeNamedArgument prop in attr.Properties.Concat(attr.Fields))
            {
                if (prop.Name is "methodName" or "MethodName" && prop.Argument.Value is string methodName)
                    data.MethodName = methodName;
                else if (prop.Name is "declaringType" or "DeclaringType" && prop.Argument.Value is TypeReference declaringType)
                    data.TargetType = declaringType;
            }
        }
        return data;
    }

    private sealed class PatchAttributeData
    {
        public TypeReference? TargetType { get; set; }
        public string? MethodName { get; set; }
        public IReadOnlyList<TypeReference>? ArgumentTypes { get; set; }
        public bool IsEmpty => TargetType is null && MethodName is null && ArgumentTypes is null;
    }
}

internal static class Verifier
{
    public static void VerifyAll(VerifyContext context)
    {
        foreach (LookupRecord lookup in context.Lookups)
            VerifyLookup(context, lookup, addOverloadChoice: true);

        foreach (PatchSite patch in context.PatchSites)
            VerifyPatchSite(context, patch);

        foreach (DirectPatchSite patch in context.DirectPatchSites)
            VerifyDirectPatchSite(context, patch);

        foreach (AttributePatchSite patch in context.AttributePatchSites)
            VerifyAttributePatchSite(context, patch);

        VerifyImportedAssemblyCSharpReferences(context);
    }

    private static void VerifyLookup(VerifyContext context, LookupRecord record, bool addOverloadChoice)
    {
        if (record.Kind == LookupKind.TypeByName)
        {
            if (record.TargetName is null)
                context.Errors.Add($"ERROR lookup has unknown TypeByName argument: {record.Describe()}");
            else if ((record.ResolvedType = ResolveTypeByName(context, record.TargetName)) is null)
                context.Errors.Add($"ERROR type lookup failed: {record.Describe()}");
            return;
        }

        if (record.TargetType is null || record.TargetName is null)
        {
            context.Warnings.Add($"WARN incomplete reflective lookup; verifier could not resolve operands: {record.Describe()}");
            return;
        }

        TypeDefinition? type = ResolveType(context, record.TargetType);
        if (type is null)
        {
            context.Errors.Add($"ERROR target type not found for lookup: {record.Describe()}");
            return;
        }

        bool declaredOnly = record.Kind is LookupKind.DeclaredMethod or LookupKind.DeclaredField or LookupKind.DeclaredProperty;
        switch (record.Kind)
        {
            case LookupKind.Method:
            case LookupKind.DeclaredMethod:
            {
                List<MethodDefinition> candidates = GetCandidateMethods(type, record.TargetName, declaredOnly).ToList();
                MethodDefinition? method = record.ParameterTypes is null
                    ? ChooseMostParametersOrNull(candidates)
                    : candidates.FirstOrDefault(m => ParametersMatch(context, m, record.ParameterTypes));
                if (method is null)
                {
                    context.Errors.Add($"ERROR method not found: {record.Describe()}");
                    return;
                }
                record.ResolvedMethod = method;
                if (record.ParameterTypes is null)
                    RecordOverloads(context, addOverloadChoice, record, candidates, method);
                break;
            }
            case LookupKind.Field:
            case LookupKind.DeclaredField:
            {
                FieldDefinition? field = GetCandidateFields(type, record.TargetName, declaredOnly).FirstOrDefault();
                if (field is null) context.Errors.Add($"ERROR field not found: {record.Describe()}");
                else record.ResolvedField = field;
                break;
            }
            case LookupKind.Property:
            case LookupKind.DeclaredProperty:
            case LookupKind.PropertyGetter:
            case LookupKind.PropertySetter:
            {
                PropertyDefinition? property = GetCandidateProperties(type, record.TargetName, declaredOnly).FirstOrDefault();
                if (property is null)
                {
                    context.Errors.Add($"ERROR property not found: {record.Describe()}");
                    return;
                }
                record.ResolvedProperty = property;
                if (record.Kind == LookupKind.PropertyGetter)
                {
                    if (property.GetMethod is null) context.Errors.Add($"ERROR property getter not found: {record.Describe()}");
                    else record.ResolvedMethod = property.GetMethod;
                }
                else if (record.Kind == LookupKind.PropertySetter)
                {
                    if (property.SetMethod is null) context.Errors.Add($"ERROR property setter not found: {record.Describe()}");
                    else record.ResolvedMethod = property.SetMethod;
                }
                break;
            }
            case LookupKind.NestedType:
            {
                TypeDefinition? nested = ResolveNestedType(type, record.TargetName);
                if (nested is null) context.Errors.Add($"ERROR nested type not found: {record.Describe()}");
                else record.ResolvedType = nested;
                break;
            }
        }
    }

    private static void VerifyPatchSite(VerifyContext context, PatchSite patch)
    {
        if (patch.TargetType is null || patch.TargetMethod is null || patch.PatchType is null || patch.PatchMethod is null)
        {
            context.Errors.Add($"ERROR incomplete {patch.Kind} patch site in {patch.Source}: targetType={Display.MaybeType(patch.TargetType)}, targetMethod={patch.TargetMethod ?? "<unknown>"}, patchType={Display.MaybeType(patch.PatchType)}, patchMethod={patch.PatchMethod ?? "<unknown>"}");
            return;
        }

        TypeDefinition? targetType = ResolveType(context, patch.TargetType);
        if (targetType is null)
        {
            context.Errors.Add($"ERROR {patch.Kind} target type not found: {Display.Type(patch.TargetType)}::{patch.TargetMethod} in {patch.Source}");
            return;
        }

        List<MethodDefinition> candidates = GetCandidateMethods(targetType, patch.TargetMethod, declaredOnly: false).ToList();
        MethodDefinition? targetMethod = patch.TargetParameterTypes is null
            ? ChooseMostParametersOrNull(candidates)
            : candidates.FirstOrDefault(m => ParametersMatch(context, m, patch.TargetParameterTypes));

        if (targetMethod is null)
        {
            string sig = patch.TargetParameterTypes is null ? "" : $"({string.Join(", ", patch.TargetParameterTypes.Select(Display.Type))})";
            context.Errors.Add($"ERROR {patch.Kind} target method not found: {Display.Type(patch.TargetType)}::{patch.TargetMethod}{sig} in {patch.Source}");
            return;
        }
        if (patch.TargetParameterTypes is null)
            RecordOverloads(context, add: true, $"{patch.Kind} target {Display.Type(targetType)}::{patch.TargetMethod} in {patch.Source}", candidates, targetMethod);

        TypeDefinition? patchType = ResolveType(context, patch.PatchType);
        if (patchType is null)
        {
            context.Errors.Add($"ERROR {patch.Kind} patch type not found: {Display.Type(patch.PatchType)} in {patch.Source}");
            return;
        }

        MethodDefinition? patchMethod = ChooseMostParametersOrNull(GetCandidateMethods(patchType, patch.PatchMethod, declaredOnly: false).ToList());
        if (patchMethod is null)
        {
            context.Errors.Add($"ERROR {patch.Kind} patch method not found: {Display.Type(patch.PatchType)}::{patch.PatchMethod} for {Display.Method(targetMethod)} in {patch.Source}");
            return;
        }

        ValidatePatchBinding(context, patch.Kind, targetMethod, patchMethod, patch.Source);
    }

    private static void VerifyDirectPatchSite(VerifyContext context, DirectPatchSite patch)
    {
        if (patch.Original is null || patch.Patch is null)
        {
            context.Errors.Add($"ERROR incomplete direct Harmony {patch.Kind} patch in {patch.Source}");
            return;
        }

        VerifyLookup(context, patch.Original, addOverloadChoice: false);
        VerifyLookup(context, patch.Patch, addOverloadChoice: false);

        MethodDefinition? targetMethod = patch.Original.ResolvedMethod;
        if (targetMethod is null && patch.Original.ResolvedProperty is { } property)
            targetMethod = patch.Original.Kind == LookupKind.PropertySetter ? property.SetMethod : property.GetMethod;

        MethodDefinition? patchMethod = patch.Patch.ResolvedMethod;
        if (targetMethod is null || patchMethod is null) return;
        ValidatePatchBinding(context, patch.Kind, targetMethod, patchMethod, patch.Source);
    }

    private static void VerifyAttributePatchSite(VerifyContext context, AttributePatchSite patch)
    {
        if (patch.TargetType is null || patch.TargetMethod is null)
        {
            context.Warnings.Add($"WARN incomplete HarmonyPatch attribute target on {patch.Source}");
            return;
        }

        TypeDefinition? targetType = ResolveType(context, patch.TargetType);
        if (targetType is null)
        {
            context.Errors.Add($"ERROR HarmonyPatch target type not found: {Display.Type(patch.TargetType)} on {patch.Source}");
            return;
        }

        List<MethodDefinition> candidates = GetCandidateMethods(targetType, patch.TargetMethod, declaredOnly: false).ToList();
        MethodDefinition? targetMethod = patch.TargetParameterTypes is null
            ? ChooseMostParametersOrNull(candidates)
            : candidates.FirstOrDefault(m => ParametersMatch(context, m, patch.TargetParameterTypes));
        if (targetMethod is null)
        {
            context.Errors.Add($"ERROR HarmonyPatch target method not found: {Display.Type(targetType)}::{patch.TargetMethod} on {patch.Source}");
            return;
        }
        ValidatePatchBinding(context, patch.Kind, targetMethod, patch.PatchMethod, patch.Source);
    }

    private static void ValidatePatchBinding(VerifyContext context, string kind, MethodDefinition targetMethod, MethodDefinition patchMethod, string source)
    {
        if (kind == "Transpiler")
        {
            if (!IsIEnumerableCodeInstruction(patchMethod.ReturnType))
                context.Errors.Add($"ERROR transpiler {Display.Method(patchMethod)} does not return IEnumerable<CodeInstruction> for {Display.Method(targetMethod)} in {source}");
            context.Transpilers.Add($"{Display.Method(targetMethod)} <= {Display.Method(patchMethod)}");
            return;
        }

        if (kind == "Prefix" && patchMethod.ReturnType.FullName is not ("System.Void" or "System.Boolean"))
            context.Warnings.Add($"WARN prefix {Display.Method(patchMethod)} returns {Display.Type(patchMethod.ReturnType)}; Harmony prefixes should return void or bool.");

        var targetParams = targetMethod.Parameters.ToList();
        foreach (ParameterDefinition patchParam in patchMethod.Parameters)
        {
            string name = patchParam.Name ?? "";
            TypeReference patchParamType = TypeHelpers.StripByRef(patchParam.ParameterType);

            if (name == "__instance")
            {
                if (targetMethod.IsStatic)
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} uses __instance but target {Display.Method(targetMethod)} is static.");
                else if (!TypeHelpers.IsAssignableFrom(context, patchParamType, targetMethod.DeclaringType))
                    context.Warnings.Add($"WARN {Display.Method(patchMethod)} __instance type {Display.Type(patchParamType)} is not assignable from target {Display.Type(targetMethod.DeclaringType)}.");
                continue;
            }

            if (name == "__result")
            {
                if (targetMethod.ReturnType.FullName == "System.Void")
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} uses __result but target {Display.Method(targetMethod)} returns void.");
                else if (!TypeHelpers.SameType(context, patchParamType, targetMethod.ReturnType, ignoreByRef: true))
                    context.Warnings.Add($"WARN {Display.Method(patchMethod)} __result type {Display.Type(patchParamType)} differs from target return {Display.Type(targetMethod.ReturnType)}.");
                continue;
            }

            if (name == "__state" || name == "__args")
                continue;
            if (name == "__originalMethod")
            {
                if (!TypeHelpers.IsAssignableFrom(context, patchParamType, ResolveTypeByName(context, "System.Reflection.MethodBase") ?? patchParamType))
                    context.Warnings.Add($"WARN {Display.Method(patchMethod)} __originalMethod type is {Display.Type(patchParamType)}.");
                continue;
            }
            if (name == "__runOriginal")
            {
                if (patchParamType.FullName != "System.Boolean")
                    context.Warnings.Add($"WARN {Display.Method(patchMethod)} __runOriginal type is {Display.Type(patchParamType)}, expected bool.");
                continue;
            }
            if (name == "__exception")
            {
                if (kind != "Finalizer")
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} uses __exception but patch kind is {kind}, not Finalizer.");
                continue;
            }

            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                string fieldName = name[3..];
                FieldDefinition? field = GetCandidateFields(targetMethod.DeclaringType, fieldName, declaredOnly: false).FirstOrDefault();
                if (field is null)
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} requests field {name}, but {Display.Type(targetMethod.DeclaringType)} and bases do not contain {fieldName}.");
                // Harmony doesn't convert injected fields: a ref must match exactly, a copy must be assignable
                else if (patchParam.ParameterType.IsByReference
                    ? !TypeHelpers.SameType(context, patchParamType, field.FieldType, ignoreByRef: true)
                    : !TypeHelpers.SameType(context, patchParamType, field.FieldType, ignoreByRef: true) && !TypeHelpers.IsAssignableFrom(context, patchParamType, field.FieldType))
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} requests field {name} as {Display.Type(patchParam.ParameterType)}, but {Display.Type(field.DeclaringType)}.{fieldName} is {Display.Type(field.FieldType)}.");
                continue;
            }

            if (name.Length >= 3 && name[0] == '_' && name[1] == '_' && int.TryParse(name[2..], out int index))
            {
                if (index < 0 || index >= targetParams.Count)
                    context.Errors.Add($"ERROR {Display.Method(patchMethod)} parameter {name} is out of range for target {Display.Method(targetMethod)}.");
                else if (!TypeHelpers.SameType(context, patchParamType, targetParams[index].ParameterType, ignoreByRef: true))
                    context.Warnings.Add($"WARN {Display.Method(patchMethod)} parameter {name} type {Display.Type(patchParamType)} differs from target argument {index} type {Display.Type(targetParams[index].ParameterType)}.");
                continue;
            }

            ParameterDefinition? targetParam = targetParams.FirstOrDefault(p => p.Name == name);
            if (targetParam is null)
            {
                context.Errors.Add($"ERROR {Display.Method(patchMethod)} parameter '{name}' cannot bind to {Display.Method(targetMethod)}.");
                continue;
            }

            if (!TypeHelpers.SameType(context, patchParamType, targetParam.ParameterType, ignoreByRef: true))
                context.Warnings.Add($"WARN {Display.Method(patchMethod)} parameter {name} type {Display.Type(patchParamType)} differs from target type {Display.Type(targetParam.ParameterType)}.");
        }
    }

    private static bool IsIEnumerableCodeInstruction(TypeReference returnType)
    {
        string full = returnType.FullName.Replace('/', '.');
        return full == "System.Collections.Generic.IEnumerable`1<HarmonyLib.CodeInstruction>"
            || full == "System.Collections.Generic.IEnumerable`1<CodeInstruction>"
            || (full.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal) && full.Contains("HarmonyLib.CodeInstruction", StringComparison.Ordinal));
    }

    private static void VerifyImportedAssemblyCSharpReferences(VerifyContext context)
    {
        foreach (TypeDefinition type in context.PluginAssembly.MainModule.GetTypes())
        {
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference mr && TypeHelpers.ScopeName(TypeHelpers.DefinitionElement(mr.DeclaringType)) == "Assembly-CSharp")
                        VerifyImportedMethod(context, mr, method);
                    else if (instruction.Operand is FieldReference fr && TypeHelpers.ScopeName(TypeHelpers.DefinitionElement(fr.DeclaringType)) == "Assembly-CSharp")
                        VerifyImportedField(context, fr, method);
                }
            }
        }
    }

    private static void VerifyImportedMethod(VerifyContext context, MethodReference reference, MethodDefinition source)
    {
        MethodReference elementMethod = reference.GetElementMethod();
        string key = "M:" + elementMethod.FullName;
        if (!context.ImportedMembers.Add(key)) return;

        TypeDefinition? type = ResolveType(context, elementMethod.DeclaringType);
        MethodDefinition? method = type is null
            ? null
            : GetCandidateMethods(type, elementMethod.Name, declaredOnly: false)
                .FirstOrDefault(m => ParametersMatch(context, m, elementMethod.Parameters.Select(p => p.ParameterType).ToList()));
        if (method is null)
        {
            context.Errors.Add($"ERROR imported Assembly-CSharp method reference does not resolve in game DLL: {Display.MethodReference(elementMethod)} used by {Display.Method(source)}");
            return;
        }
        CheckOriginalVisibility(context, method, isMethod: true);
    }

    private static void VerifyImportedField(VerifyContext context, FieldReference reference, MethodDefinition source)
    {
        string key = "F:" + reference.FullName;
        if (!context.ImportedMembers.Add(key)) return;

        TypeDefinition? type = ResolveType(context, reference.DeclaringType);
        FieldDefinition? field = type is null ? null : GetCandidateFields(type, reference.Name, declaredOnly: false).FirstOrDefault(f => TypeHelpers.SameType(context, f.FieldType, reference.FieldType, ignoreByRef: false));
        if (field is null)
        {
            context.Errors.Add($"ERROR imported Assembly-CSharp field reference does not resolve in game DLL: {Display.FieldReference(reference)} used by {Display.Method(source)}");
            return;
        }
        CheckOriginalVisibility(context, field, isMethod: false);
    }

    private static void CheckOriginalVisibility(VerifyContext context, IMemberDefinition member, bool isMethod)
    {
        if (context.OriginalTypes is null) return;

        TypeDefinition? originalType = context.OriginalTypes.Resolve(member.DeclaringType);
        if (originalType is null) return;

        if (isMethod && member is MethodDefinition method)
        {
            MethodDefinition? original = GetCandidateMethods(originalType, method.Name, declaredOnly: false)
                .FirstOrDefault(m => ParametersMatch(context, m, method.Parameters.Select(p => p.ParameterType).ToList()));
            if (original is not null && !original.IsPublic)
                context.Infos.Add($"INFO original member is non-public: {Display.Method(original)}");
        }
        else if (!isMethod && member is FieldDefinition field)
        {
            FieldDefinition? original = GetCandidateFields(originalType, field.Name, declaredOnly: false).FirstOrDefault();
            if (original is not null && !original.IsPublic)
                context.Infos.Add($"INFO original member is non-public: {Display.Type(original.DeclaringType)}::{original.Name}");
        }
    }

    public static TypeDefinition? ResolveType(VerifyContext context, TypeReference reference)
    {
        TypeReference element = TypeHelpers.DefinitionElement(reference);
        if (context.GameTypes.Resolve(element) is { } gameType) return gameType;
        if (context.PluginTypes.Resolve(element) is { } pluginType) return pluginType;
        foreach (SimpleTypeIndex refs in context.RefTypes)
            if (refs.Resolve(element) is { } refType) return refType;
        try { return element.Resolve(); }
        catch { return null; }
    }

    public static TypeDefinition? ResolveTypeByName(VerifyContext context, string name)
    {
        if (context.GameTypes.ResolveByName(name) is { } gameType) return gameType;
        if (context.PluginTypes.ResolveByName(name) is { } pluginType) return pluginType;
        foreach (SimpleTypeIndex refs in context.RefTypes)
            if (refs.ResolveByName(name) is { } refType) return refType;
        return null;
    }

    public static TypeDefinition? ResolveNestedType(TypeDefinition? parent, string nestedName)
    {
        if (parent is null) return null;
        return parent.NestedTypes.FirstOrDefault(t => t.Name == nestedName || t.FullName.EndsWith("/" + nestedName, StringComparison.Ordinal) || t.FullName.EndsWith("+" + nestedName, StringComparison.Ordinal));
    }

    public static IEnumerable<MethodDefinition> GetCandidateMethods(TypeDefinition type, string name, bool declaredOnly)
    {
        TypeDefinition? current = type;
        bool first = true;
        while (current is not null)
        {
            foreach (MethodDefinition method in current.Methods)
            {
                if (method.Name != name) continue;
                if (first || !method.IsPrivate) yield return method;
            }
            if (declaredOnly) yield break;
            first = false;
            current = ResolveBase(current);
        }
    }

    public static IEnumerable<FieldDefinition> GetCandidateFields(TypeDefinition type, string name, bool declaredOnly)
    {
        TypeDefinition? current = type;
        bool first = true;
        while (current is not null)
        {
            foreach (FieldDefinition field in current.Fields)
            {
                if (field.Name != name) continue;
                if (first || !field.IsPrivate) yield return field;
            }
            if (declaredOnly) yield break;
            first = false;
            current = ResolveBase(current);
        }
    }

    public static IEnumerable<PropertyDefinition> GetCandidateProperties(TypeDefinition type, string name, bool declaredOnly)
    {
        TypeDefinition? current = type;
        bool first = true;
        while (current is not null)
        {
            foreach (PropertyDefinition property in current.Properties)
            {
                if (property.Name != name) continue;
                MethodDefinition? accessor = property.GetMethod ?? property.SetMethod;
                if (first || accessor is null || !accessor.IsPrivate) yield return property;
            }
            if (declaredOnly) yield break;
            first = false;
            current = ResolveBase(current);
        }
    }

    private static TypeDefinition? ResolveBase(TypeDefinition type)
    {
        if (type.BaseType is null) return null;
        try { return type.BaseType.Resolve(); }
        catch { return null; }
    }

    public static MethodDefinition ChooseMostParameters(IReadOnlyList<MethodDefinition> methods)
    {
        MethodDefinition best = methods[0];
        int max = best.Parameters.Count;
        for (int i = 1; i < methods.Count; i++)
        {
            int count = methods[i].Parameters.Count;
            if (count > max)
            {
                best = methods[i];
                max = count;
            }
        }
        return best;
    }

    private static MethodDefinition? ChooseMostParametersOrNull(IReadOnlyList<MethodDefinition> methods)
        => methods.Count == 0 ? null : ChooseMostParameters(methods);

    private static bool ParametersMatch(VerifyContext context, MethodDefinition method, IReadOnlyList<TypeReference> types)
    {
        if (method.Parameters.Count != types.Count) return false;
        for (int i = 0; i < types.Count; i++)
        {
            if (!TypeHelpers.SameType(context, method.Parameters[i].ParameterType, types[i], ignoreByRef: false))
                return false;
        }
        return true;
    }

    private static void RecordOverloads(VerifyContext context, bool addOverloadChoice, LookupRecord record, List<MethodDefinition> candidates, MethodDefinition chosen)
        => RecordOverloads(context, addOverloadChoice, record.Describe(), candidates, chosen);

    private static void RecordOverloads(VerifyContext context, bool add, string label, List<MethodDefinition> candidates, MethodDefinition chosen)
    {
        if (!add || candidates.Count == 0) return;
        string choice = $"{label} -> {Display.Method(chosen)}";
        if (candidates.Count > 1)
        {
            context.Warnings.Add($"WARN name-only method lookup has {candidates.Count} overloads; verifier chose most parameters: {choice}");
            choice += Environment.NewLine + "    overloads: " + string.Join("; ", candidates.Select(Display.Method));
        }
        context.OverloadChoices.Add(choice);
    }
}

internal static class TypeHelpers
{
    public static TypeReference DefinitionElement(TypeReference type)
    {
        while (type is TypeSpecification spec)
            type = spec.ElementType;
        return type;
    }

    public static TypeReference StripByRef(TypeReference type)
        => type is ByReferenceType byRef ? byRef.ElementType : type;

    public static string ScopeName(TypeReference type)
    {
        TypeReference element = DefinitionElement(type);
        return element.Scope switch
        {
            AssemblyNameReference assemblyName => assemblyName.Name,
            ModuleDefinition module => module.Assembly.Name.Name,
            _ => element.Module?.Assembly?.Name.Name ?? "",
        };
    }

    public static bool SameType(VerifyContext context, TypeReference a, TypeReference b, bool ignoreByRef)
    {
        if (ignoreByRef)
        {
            a = StripByRef(a);
            b = StripByRef(b);
        }

        string keyA = TypeKey(context, a);
        string keyB = TypeKey(context, b);
        return keyA == keyB;
    }

    private static string TypeKey(VerifyContext context, TypeReference type)
    {
        if (type is ByReferenceType byRef) return TypeKey(context, byRef.ElementType) + "&";
        if (type is ArrayType arr) return TypeKey(context, arr.ElementType) + "[]";
        if (type is GenericInstanceType git)
            return TypeKey(context, git.ElementType) + "<" + string.Join(",", git.GenericArguments.Select(a => TypeKey(context, a))) + ">";
        if (type is GenericParameter gp) return "!" + gp.Name;

        TypeDefinition? resolved = Verifier.ResolveType(context, type);
        return resolved?.FullName ?? DefinitionElement(type).FullName;
    }

    public static bool IsAssignableFrom(VerifyContext context, TypeReference target, TypeReference source)
    {
        target = StripByRef(target);
        source = StripByRef(source);
        if (SameType(context, target, source, ignoreByRef: true)) return true;
        if (target.FullName == "System.Object") return true;

        TypeDefinition? sourceDef = Verifier.ResolveType(context, source);
        if (sourceDef is null) return false;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Walk(sourceDef);

        bool Walk(TypeDefinition type)
        {
            if (!visited.Add(type.FullName)) return false;
            if (type.BaseType is not null)
            {
                if (SameType(context, target, type.BaseType, ignoreByRef: true)) return true;
                try
                {
                    TypeDefinition? baseDef = type.BaseType.Resolve();
                    if (baseDef is not null && Walk(baseDef)) return true;
                }
                catch { }
            }

            foreach (InterfaceImplementation iface in type.Interfaces)
            {
                if (SameType(context, target, iface.InterfaceType, ignoreByRef: true)) return true;
                try
                {
                    TypeDefinition? ifaceDef = iface.InterfaceType.Resolve();
                    if (ifaceDef is not null && Walk(ifaceDef)) return true;
                }
                catch { }
            }
            return false;
        }
    }
}

internal static class Display
{
    public static string Type(TypeReference type)
    {
        string name = type.FullName.Replace('/', '.');
        return name;
    }

    public static string MaybeType(TypeReference? type) => type is null ? "<unknown>" : Type(type);

    public static string Type(TypeDefinition type) => type.FullName.Replace('/', '.');

    public static string Method(MethodDefinition method)
        => $"{Type(method.DeclaringType)}::{method.Name}{Parameters(method)}";

    public static string MethodReference(MethodReference method)
        => $"{Type(method.DeclaringType)}::{method.Name}({string.Join(", ", method.Parameters.Select(p => Type(p.ParameterType)))})";

    public static string FieldReference(FieldReference field)
        => $"{Type(field.DeclaringType)}::{field.Name}";

    public static string Parameters(MethodDefinition method)
        => $"({string.Join(", ", method.Parameters.Select(p => Type(p.ParameterType) + " " + p.Name))})";

    public static string Operand(object? operand) => operand switch
    {
        null => "",
        Instruction i => $"IL_{i.Offset:x4}",
        Instruction[] arr => string.Join(", ", arr.Select(i => $"IL_{i.Offset:x4}")),
        MethodReference mr => MethodReference(mr),
        FieldReference fr => FieldReference(fr),
        TypeReference tr => Type(tr),
        string s => "\"" + s + "\"",
        _ => operand.ToString() ?? "",
    };
}

internal static class ReportWriter
{
    public static string Build(VerifyContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PatchVerifier report");
        sb.AppendLine("====================");
        sb.AppendLine($"Plugin: {context.PluginPath}");
        sb.AppendLine($"Game:   {context.GamePath}");
        if (context.OriginalAssembly is not null) sb.AppendLine($"Original visibility baseline: {context.OriginalAssembly.MainModule.FileName}");
        sb.AppendLine();
        sb.AppendLine("Summary");
        sb.AppendLine("-------");
        sb.AppendLine($"RoguePatcher sites: {context.PatchSites.Count}");
        sb.AppendLine($"Direct Harmony patches: {context.DirectPatchSites.Count}");
        sb.AppendLine($"HarmonyPatch attribute patches: {context.AttributePatchSites.Count}");
        sb.AppendLine($"Reflective lookups recorded: {context.Lookups.Count}");
        sb.AppendLine($"Assembly-CSharp member refs checked: {context.ImportedMembers.Count}");
        sb.AppendLine($"Transpilers: {context.Transpilers.Count}");
        sb.AppendLine($"ERRORs: {context.Errors.Count}");
        sb.AppendLine($"WARNs: {context.Warnings.Count}");
        sb.AppendLine($"INFOs: {context.Infos.Count}");

        AppendSection(sb, "ERRORs", context.Errors);
        AppendSection(sb, "WARNs", context.Warnings);
        AppendSection(sb, "INFOs", context.Infos.Distinct(StringComparer.Ordinal).OrderBy(s => s));
        AppendSection(sb, "Overload choices", context.OverloadChoices.Distinct(StringComparer.Ordinal));
        AppendSection(sb, "Transpilers needing IL review", context.Transpilers.Distinct(StringComparer.Ordinal));
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        sb.AppendLine(new string('-', title.Length));
        bool any = false;
        foreach (string line in lines)
        {
            any = true;
            foreach (string part in line.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                sb.AppendLine(part);
        }
        if (!any) sb.AppendLine("<none>");
    }
}
