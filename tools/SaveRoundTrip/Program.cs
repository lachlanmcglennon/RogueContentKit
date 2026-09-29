using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

internal static class Program
{
    private static string repoRoot = "";
    private static Assembly gameAssembly = null!;

    private static int Main(string[] args)
    {
        try
        {
            repoRoot = FindRepoRoot();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            gameAssembly = Assembly.LoadFrom(Path.Combine(repoRoot, ".ref", "Assembly-CSharp.dll"));

            object save = args.Length == 0 ? CreateSyntheticSave() : LoadSave(args[0]);
            var before = Snapshot(save);
            object after = RoundTrip(save);
            var afterSnapshot = Snapshot(after);

            if (!before.SequenceEqual(afterSnapshot))
            {
                Console.Error.WriteLine("Round-trip mismatch.");
                Console.Error.WriteLine("Before: " + string.Join(" | ", before));
                Console.Error.WriteLine("After:  " + string.Join(" | ", afterSnapshot));
                return 2;
            }

            Console.WriteLine(args.Length == 0
                ? "Synthetic custom save payload round-tripped: trait/effect/item names, counts and contents survived."
                : $"Save copy round-tripped in memory: {before.Count} tracked trait/effect/item entries preserved.");
            foreach (string line in before.Where(static line => line.Contains("RCK")).Take(20))
                Console.WriteLine("  " + line);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static Assembly? ResolveAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string dir in new[] { Path.Combine(repoRoot, ".ref"), Path.Combine(repoRoot, ".ref", "static") })
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static string FindRepoRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, ".ref", "Assembly-CSharp.dll"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repo root containing .ref\\Assembly-CSharp.dll.");
    }

    private static object LoadSave(string path)
    {
        using FileStream stream = File.OpenRead(Path.GetFullPath(path));
        return new BinaryFormatter().Deserialize(stream);
    }

    private static object RoundTrip(object save)
    {
        using var stream = new MemoryStream();
        var formatter = new BinaryFormatter();
        formatter.Serialize(stream, save);
        stream.Position = 0;
        return formatter.Deserialize(stream);
    }

    private static object CreateSyntheticSave()
    {
        Type saveType = gameAssembly.GetType("SaveGameData", throwOnError: true)!;
        Type traitType = gameAssembly.GetType("Trait", throwOnError: true)!;
        Type effectType = gameAssembly.GetType("StatusEffectLight", throwOnError: true)!;
        Type itemType = gameAssembly.GetType("InvItemLight", throwOnError: true)!;

        object save = Activator.CreateInstance(saveType)!;
        Set(save, "invalidate", false);

        var filled = new bool[30];
        filled[1] = true;
        Set(save, "filled", filled);

        var agentNames = new string[30];
        agentNames[1] = "Custom";
        Set(save, "agentName", agentNames);

        Array traits = MakeListArray(traitType, 30);
        object trait = Activator.CreateInstance(traitType)!;
        Set(trait, "traitName", "RCK_SaveRoundTripTrait");
        Set(trait, "addedInGame", true);
        Set(trait, "requiresUpdates", true);
        AddToListArray(traits, 1, traitType, trait);
        Set(save, "traitList", traits);

        Array effects = MakeListArray(effectType, 30);
        object effect = Activator.CreateInstance(effectType)!;
        Set(effect, "statusEffectName", "RCK_SaveRoundTripEffect");
        Set(effect, "curTime", 37);
        Set(effect, "keepBetweenLevels", true);
        AddToListArray(effects, 1, effectType, effect);
        Set(save, "statusEffectList", effects);

        Array items = MakeListArray(itemType, 30);
        object item = Activator.CreateInstance(itemType)!;
        Set(item, "invItemName", "RCK_SaveRoundTripItem");
        Set(item, "invItemCount", 7);
        Set(item, "itemType", "Tool");
        Set(item, "contents", new List<string> { "charge=3", "flag=true" });
        Set(item, "Categories", new List<string> { "Technology", "Usable" });
        AddToListArray(items, 1, itemType, item);
        Set(save, "invItemList", items);

        return save;
    }

    private static Array MakeListArray(Type elementType, int length)
        => Array.CreateInstance(typeof(List<>).MakeGenericType(elementType), length);

    private static void AddToListArray(Array array, int index, Type elementType, object item)
    {
        Type listType = typeof(List<>).MakeGenericType(elementType);
        object list = Activator.CreateInstance(listType)!;
        listType.GetMethod("Add")!.Invoke(list, new[] { item });
        array.SetValue(list, index);
    }

    private static void Set(object obj, string field, object? value)
        => obj.GetType().GetField(field)!.SetValue(obj, value);

    private static List<string> Snapshot(object save)
    {
        var lines = new List<string>();
        AddNames(lines, save, "traitList", "traitName");
        AddNames(lines, save, "statusEffectList", "statusEffectName", "curTime");
        AddNames(lines, save, "invItemList", "invItemName", "invItemCount", "contents");
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void AddNames(List<string> lines, object save, string listField, string nameField, params string[] extraFields)
    {
        object? arrayObj = save.GetType().GetField(listField)?.GetValue(save);
        if (arrayObj is not Array array) return;

        for (int i = 0; i < array.Length; i++)
        {
            object? list = array.GetValue(i);
            if (list is null) continue;

            foreach (object entry in (System.Collections.IEnumerable)list)
            {
                Type type = entry.GetType();
                string? name = type.GetField(nameField)?.GetValue(entry) as string;
                if (string.IsNullOrEmpty(name)) continue;

                var parts = new List<string> { $"{listField}[{i}]={name}" };
                foreach (string extra in extraFields)
                {
                    object? value = type.GetField(extra)?.GetValue(entry);
                    if (value is System.Collections.IEnumerable enumerable && value is not string)
                        value = string.Join(",", enumerable.Cast<object>());
                    parts.Add($"{extra}:{value}");
                }
                lines.Add(string.Join(";", parts));
            }
        }
    }
}
