using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RogueLibsCore;

namespace RCK
{
    /// <summary>Loads the <c>RCK.*.dll</c> system modules that sit next to <c>RCK.dll</c>.</summary>
    internal static class ModuleLoader
    {
        internal sealed class Loaded
        {
            public Loaded(string file) => File = file;
            public string File { get; }
            public int Patches { get; set; }
            public int FailedPatches { get; set; }
            public List<string> Modules { get; } = new List<string>();
            public string? Error { get; set; }
        }

        internal static readonly List<Loaded> Results = new List<Loaded>();

        internal static void LoadAll(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "RCK.*.dll").OrderBy(static f => f, StringComparer.OrdinalIgnoreCase))
            {
                var r = new Loaded(Path.GetFileName(file));
                Results.Add(r);
                try { Load(Assembly.LoadFrom(file), r); }
                catch (Exception e)
                {
                    r.Error = e is ReflectionTypeLoadException rtle
                        ? string.Join("; ", rtle.LoaderExceptions.Select(static x => x?.Message).Distinct())
                        : e.Message;
                    Rck.Log.LogError($"Module {r.File} failed to load: {r.Error}");
                }
            }
        }

        private static void Load(Assembly asm, Loaded r)
        {
            Type[] types = asm.GetTypes();
            var harmony = new Harmony(RckPlugin.PluginGuid + "." + asm.GetName().Name);

            foreach (Type t in types)
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                if (Rck.TryPatch(harmony, t)) r.Patches++;
                else r.FailedPatches++;
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type t in types)
            foreach (MethodInfo m in t.GetMethods(flags))
            {
                if (m.GetCustomAttribute<RLSetupAttribute>() is null || m.GetParameters().Length != 0) continue;
                Guarded($"{t.Name}.{m.Name}", () => m.Invoke(null, null));
            }

            foreach (Type t in types)
            {
                if (!typeof(IRckModule).IsAssignableFrom(t) || t.IsAbstract || t.IsInterface || t.GetConstructor(Type.EmptyTypes) is null)
                    continue;
                Guarded(t.Name, () =>
                {
                    var module = (IRckModule)Activator.CreateInstance(t);
                    module.Initialize();
                    r.Modules.Add(module.Name);
                });
            }
        }

        private static void Guarded(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Rck.Log.LogError($"{what} failed: {(e is TargetInvocationException tie ? tie.InnerException : e)}"); }
        }
    }
}
