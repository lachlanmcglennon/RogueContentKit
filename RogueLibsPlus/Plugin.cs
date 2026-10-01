using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RogueLibsCore;

namespace RogueLibsPlus
{
    /// <summary>
    ///   <para>RogueLibsPlus: fixes and small APIs on top of an unmodified RogueLibs. Fixes to RogueLibs' own code only
    ///   apply to the RogueLibs build they were written for (<see cref="SupportedRogueLibs"/>); fixes to the game and
    ///   the public APIs work with any RogueLibs 4.0.</para>
    /// </summary>
    [BepInPlugin(GUID, Name, Version)]
    [BepInDependency(RogueLibs.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class RogueLibsPlusPlugin : BaseUnityPlugin
    {
        /// <summary>The plugin's GUID.</summary>
        public const string GUID = "streetsofrogue.roguelibsplus";
        /// <summary>The plugin's name.</summary>
        public const string Name = "RogueLibsPlus";
        /// <summary>The plugin's numeric version, X.Y.Z, for BepInEx. Generated from &lt;Version&gt; in RogueLibsPlus.csproj.</summary>
        public const string Version = BuildInfo.PluginVersion;
        /// <summary>
        ///   The version the main menu and the log show: exactly X.Y.Z[-pre] for a build of an RCK Pack release tag,
        ///   otherwise X.Y.Z[-pre]+N.g&lt;commit&gt; (N commits after the last release tag), with .dirty for uncommitted changes.
        /// </summary>
        public const string DisplayVersion = BuildInfo.DisplayVersion;
        /// <summary>The RogueLibs build whose internals the RogueLibs fixes were written against.</summary>
        public const string SupportedRogueLibs = "4.0.0-rc.3";

        /// <summary>The id of RogueLibsPlus' own <see cref="MenuLines"/> line.</summary>
        public const string MenuLineId = "RogueLibsPlusVersion";

        internal static ManualLogSource Log = null!;

        /// <summary>
        ///   <para>Gets whether the running RogueLibs is the build the RogueLibs fixes were written for.</para>
        /// </summary>
        public static bool RogueLibsSupported { get; private set; }
        /// <summary>
        ///   <para>Gets the running RogueLibs' semantic version, or <see langword="null"/> if it couldn't be read.</para>
        /// </summary>
        public static string? RunningRogueLibs { get; private set; }

        private void Awake()
        {
            Log = Logger;
            RunningRogueLibs = ReadRogueLibsVersion();
            RogueLibsSupported = RunningRogueLibs == SupportedRogueLibs;
            if (!RogueLibsSupported)
                Log.LogWarning($"RogueLibs {RunningRogueLibs ?? "(unknown version)"} is running, but {Name} {DisplayVersion} was written for "
                             + $"RogueLibs {SupportedRogueLibs}: its fixes to RogueLibs itself are skipped. The game fixes and the APIs still work.");

            Fixes.ApplyAll();
            Log.LogInfo($"{Name} {DisplayVersion} on RogueLibs {RunningRogueLibs}: {Fixes.Applied} fixes applied, {Fixes.Skipped} skipped.");
            if (Fixes.Skipped > 0) MenuLines.SetProblem(MenuLineId, MenuLineText + $" ({Fixes.Skipped} fixes skipped, see BepInEx\\LogOutput.log)");
            else MenuLines.Set(MenuLineId, MenuLineText);
        }

        /// <summary>The main-menu line: "RL+ v" and <see cref="DisplayVersion"/>.</summary>
        internal const string MenuLineText = "RL+ v" + DisplayVersion;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? ReadRogueLibsVersion()
        {
            try { return RogueLibs.SemanticVersion; }
            catch (Exception) { return null; }
        }
    }

    internal static class Fixes
    {
        public static int Applied;
        public static int Skipped;

        public static void ApplyAll()
        {
            // Game fixes: these only patch Streets of Rogue.
            Apply("LevelEditorItems", false, LevelEditorFixes.Resolve, LevelEditorFixes.Patch);
            Apply("ObjectButtons", false, ObjectButtonFixes.Resolve, ObjectButtonFixes.Patch);
            Apply("MenuLines", false, static () => { }, MenuLines.Patch);
            // RogueLibs fixes: these patch RogueLibsCore's own methods, so they need the exact build.
            Apply("Interactions", true, InteractionFixes.Resolve, InteractionFixes.Patch);
            Apply("Turntables", true, TurntablesFix.Resolve, TurntablesFix.Apply);
            Apply("DisplayedUnlock", true, DisplayedUnlockFixes.Resolve, DisplayedUnlockFixes.Patch);
            Apply("VanillaUnlocks", true, VanillaUnlockFixes.Resolve, VanillaUnlockFixes.Patch);
            Apply("UnlockMenus", true, MenuFixes.Resolve, MenuFixes.Patch);
            Apply("SaveContinue", true, SaveCompatibility.Resolve, SaveCompatibility.Patch);
            Apply("SpriteCollections", true, SpriteCollectionFixes.Resolve, SpriteCollectionFixes.Patch);
        }

        private static void Apply(string name, bool patchesRogueLibs, Action resolve, Action<Harmony> patch)
        {
            if (patchesRogueLibs && !RogueLibsPlusPlugin.RogueLibsSupported)
            {
                Skipped++;
                return;
            }
            Harmony harmony = new Harmony(RogueLibsPlusPlugin.GUID + "." + name);
            try
            {
                resolve();
                patch(harmony);
                Applied++;
            }
            catch (Exception e)
            {
                Skipped++;
                try { harmony.UnpatchSelf(); }
                catch (Exception) { /* nothing was patched, or unpatching failed too: the warning below covers it */ }
                Exception inner = e is TypeInitializationException { InnerException: { } ie } ? ie : e;
                RogueLibsPlusPlugin.Log.LogWarning($"Fix '{name}' skipped: {inner.GetType().Name}: {inner.Message}");
            }
        }

        /// <summary>
        ///   <para>Returns <paramref name="member"/>, or throws a <see cref="MissingMemberException"/> naming it.</para>
        /// </summary>
        public static T Need<T>(T? member, string description) where T : class
            => member ?? throw new MissingMemberException($"{description} not found");
    }
}
