using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RogueLibsCore;

namespace RCK
{
    /// <summary>
    ///   An RCK system module (a <c>RCK.&lt;System&gt;.dll</c> next to <c>RCK.dll</c>). The loader applies the module's
    ///   Harmony patches (each patch class isolated), runs its <c>[RLSetup]</c> methods, then calls
    ///   <see cref="Initialize"/> on every public non-abstract implementation with a parameterless constructor.
    /// </summary>
    public interface IRckModule
    {
        string Name { get; }
        void Initialize();
    }

    public delegate void TraitChangedHandler(Agent agent, string traitName, RckTrait trait);

    /// <summary>The shared API every RCK module uses.</summary>
    public static class Rck
    {
        /// <summary>
        ///   Prefix for trait IDs we add that CCU content never had. Never use a bare CCU-style name for an addition.
        ///   The one exception is the faction family (<c>Faction_N_Grade</c>, <c>Group_Grade</c>), which is generated
        ///   from <c>tools/ccu-interface/ccux_extensions.py</c> and checked against CCU's IDs there.
        /// </summary>
        public const string ExtensionPrefix = "RCK_";
        /// <summary>Display-name tag for designer traits and mutators that CCU content also uses.</summary>
        public const string Tag = "[RCK]";
        /// <summary>Display-name tag for our additions, which CCU content never uses.</summary>
        public const string ExtensionTag = "[RCK+]";
        /// <summary>Prefix for extra-var strings we add, e.g. "rck-trigger:::...".</summary>
        public const string ExtraVarPrefix = "rck-";
        /// <summary>Investigate-text extra-var prefix, stored in spawner extraVarString (<see cref="LegacyCcu"/>).</summary>
        public const string InvestigatePrefix = LegacyCcu.InvestigatePrefix;
        /// <summary>RCK's spelling of the Level Gate data-mutator prefix. <see cref="LegacyCcu.LevelGatePrefix"/> is read too.</summary>
        public const string LevelGatePrefix = "[RCK]LevelGate::";
        /// <summary>
        ///   Start of the text of a campaign's "this campaign needs RCK" sign. With RCK loaded, every vanilla Sign whose
        ///   text starts with it is removed when its level starts; without RCK the sign stays and tells the player.
        /// </summary>
        public const string MissingRckSignTag = "[RCK REQUIRED]";

        private static ManualLogSource? log;
        public static ManualLogSource Log => log ?? throw new InvalidOperationException("RCK is not initialized yet.");
        internal static void SetLog(ManualLogSource l) => log = l;

        public static RckConfig Config { get; internal set; } = null!;

        /// <summary>Fired after any RCK trait (including extensions) is added to an agent.</summary>
        public static event TraitChangedHandler? TraitAdded;
        /// <summary>Fired after any RCK trait (including extensions) is removed from an agent.</summary>
        public static event TraitChangedHandler? TraitRemoved;

        internal static void RaiseTraitAdded(Agent agent, string name, RckTrait trait) => Raise(TraitAdded, agent, name, trait, "TraitAdded");
        internal static void RaiseTraitRemoved(Agent agent, string name, RckTrait trait) => Raise(TraitRemoved, agent, name, trait, "TraitRemoved");

        private static void Raise(TraitChangedHandler? handlers, Agent agent, string name, RckTrait trait, string what)
        {
            if (handlers is null) return;
            foreach (TraitChangedHandler h in handlers.GetInvocationList())
            {
                try { h(agent, name, trait); }
                catch (Exception e) { Log.LogError($"{what} handler {h.Method.DeclaringType?.Name}.{h.Method.Name} failed for {name}: {e}"); }
            }
        }

        public static bool IsRckTrait(string? name) => name != null && RckData.TraitsById.ContainsKey(name);

        /// <summary>Sets the English description shown in the character creator. Write your own text; never copy CCU's.</summary>
        public static void Describe(string traitId, string text) => TraitRegistry.SetDescription(traitId, text);

        /// <summary>Describes every trait in <paramref name="ids"/> with the same text.</summary>
        public static void Describe(IEnumerable<string> traitIds, string text)
        {
            foreach (string id in traitIds) Describe(id, text);
        }

        /// <summary>
        ///   Registers one of our own additions. The type's name is the trait ID and must start with
        ///   <see cref="ExtensionPrefix"/>.
        /// </summary>
        public static RckTraitInfo RegisterExtension<TTrait>(string folder, string display, string description,
            RckTraitKind kind = RckTraitKind.Extension, int ccCost = 0, params string[] cancellations)
            where TTrait : RckTrait, new()
            => TraitRegistry.RegisterExtension(typeof(TTrait), folder, display, description, kind, ccCost, cancellations);

        /// <summary>Applies one Harmony patch class, logging instead of throwing if its target is missing.</summary>
        public static bool TryPatch(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"Patch {patchClass.FullName} failed: {e.InnerException?.Message ?? e.Message}");
                return false;
            }
        }

        public static GameController gc => GameController.gameController;
    }

    public sealed class RckConfig
    {
        internal RckConfig(ConfigFile file)
        {
            DesignerEdition = file.Bind("General", "DesignerEdition", false,
                "Designer mode: list RCK's designer traits (for NPCs) and its player traits in the character creator. Off by default; "
                + "playing campaigns doesn't need it. The \"[RCK] Designer mode\" button at the top of the character creator's Traits "
                + "list switches it in game.");
            LogTraitCount = file.Bind("Debug", "LogRegistration", true,
                "Log a summary of registered traits and loaded modules at startup.");
            TurfOverlay = file.Bind("Map", "TurfOverlay", true,
                "Show who holds each turf on the big map (host only). The toggle key switches it in game.");
            TurfOverlayKey = file.Bind("Map", "TurfOverlayKey", UnityEngine.KeyCode.F8,
                "The key that hides and shows the turf overlay (a Unity KeyCode name; F7 is UnityExplorer's).");
            WarPanelKey = file.Bind("Map", "WarPanelKey", UnityEngine.KeyCode.G,
                "The key that hides and shows the turf-war panel, each faction's share of the city (host only; None turns it off). "
                + "G is free in SoR's default controls; RCK warns in the log and on the panel if your controls use the same key.");
            CommandConsoleKey = file.Bind("Map", "CommandConsoleKey", UnityEngine.KeyCode.T,
                "The key that hides and shows the commander console, in levels where you lead a faction (host only; None turns it "
                + "off). T is free in SoR's default controls; RCK warns in the log and on the console if your controls use the same key.");
            KeyDefaults = file.Bind("Map", "KeyDefaults", 0,
                "Which set of default keys RCK last moved this file to (RCK sets it; leave it alone).");
            FilePath = file.ConfigFilePath;
            MoveOldDefaultKeys();
        }

        // The war panel and the console were on F6 and F9 in earlier test builds. A config written then still says so,
        // and BepInEx's defaults only apply to settings a file doesn't have yet, so the old defaults move once.
        private void MoveOldDefaultKeys()
        {
            if (KeyDefaults.Value >= 1) return;
            var moved = new List<string>();
            if (WarPanelKey.Value == UnityEngine.KeyCode.F6)
            {
                WarPanelKey.Value = UnityEngine.KeyCode.G;
                moved.Add("WarPanelKey F6 to G");
            }
            if (CommandConsoleKey.Value == UnityEngine.KeyCode.F9)
            {
                CommandConsoleKey.Value = UnityEngine.KeyCode.T;
                moved.Add("CommandConsoleKey F9 to T");
            }
            KeyDefaults.Value = 1;
            if (moved.Count > 0)
                Rck.Log.LogInfo($"RCK: moved the old default keys to the new defaults ({string.Join(", ", moved.ToArray())}). "
                    + $"Change them in the [Map] section of {FilePath}.");
        }

        /// <summary>The config file's path, for messages that tell the player where to change a setting.</summary>
        public string FilePath { get; }
        public ConfigEntry<bool> DesignerEdition { get; }
        public ConfigEntry<bool> LogTraitCount { get; }
        public ConfigEntry<bool> TurfOverlay { get; }
        public ConfigEntry<UnityEngine.KeyCode> TurfOverlayKey { get; }
        public ConfigEntry<UnityEngine.KeyCode> WarPanelKey { get; }
        public ConfigEntry<UnityEngine.KeyCode> CommandConsoleKey { get; }
        public ConfigEntry<int> KeyDefaults { get; }
    }
}
