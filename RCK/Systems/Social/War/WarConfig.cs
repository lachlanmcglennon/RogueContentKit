#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{
    /// <summary>
    ///   This level's faction-war settings beyond capture and respawn, parsed once per load from the level's mutator
    ///   strings: <c>[RCK]TurfWar::</c> (expansion and rackets), <c>[RCK]ControlPoints::</c> and <c>[RCK]Command::</c>,
    ///   with the mutators that turn each part on.
    /// </summary>
    internal static class WarConfig
    {
        public const string PanelMutator = "RCK_War_Panel";

        private static TurfWarSettings turf;
        private static ControlPointSettings points;
        private static CommandSettings command;

        static WarConfig() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            turf = null;
            points = null;
            command = null;
        }

        private static bool Check(GameController gc) => gc != null;

        public static void Initialize()
        {
            foreach (string m in new[] { TurfWarRules.Mutator, CommandRules.Mutator, PanelMutator })
                LevelMutators.Require(m, "Factions: war mutator");
        }

        private static Action<string, string> Warn(string prefix)
            => (text, error) => Rck.Log.LogWarning($"Factions: ignored {prefix} entry \"{text}\": {error}.");

        internal static TurfWarSettings Turf(GameController gc)
        {
            if (!Check(gc)) return new TurfWarSettings();
            return turf ?? (turf = TurfWarRules.Parse(LevelMutators.Bodies(gc, TurfWarRules.Prefix), Warn(TurfWarRules.Prefix)));
        }

        internal static ControlPointSettings Points(GameController gc)
        {
            if (!Check(gc)) return new ControlPointSettings();
            return points ?? (points = ControlPointRules.Parse(LevelMutators.Bodies(gc, ControlPointRules.Prefix), Warn(ControlPointRules.Prefix)));
        }

        internal static CommandSettings Command(GameController gc)
        {
            if (!Check(gc)) return new CommandSettings();
            return command ?? (command = CommandRules.Parse(LevelMutators.Bodies(gc, CommandRules.Prefix), Factions.KeyIndex, Warn(CommandRules.Prefix)));
        }

        /// <summary>Factions send parties to free turf near their own: <c>RCK_Turf_Expansion</c> or a <c>[RCK]TurfWar::</c> entry, unless <c>Expand=0</c>.</summary>
        internal static bool ExpansionOn(GameController gc)
        {
            TurfWarSettings s = Turf(gc);
            return s.Expand > 0 && (s.Listed || LevelMutators.Has(gc, TurfWarRules.Mutator));
        }

        /// <summary>The level asks for the commander console (it may still find no faction to command).</summary>
        internal static bool CommandWanted(GameController gc) => Command(gc).Listed || LevelMutators.Has(gc, CommandRules.Mutator);

        /// <summary>Commoner turf can be racketed: expansion or the console is on and <c>Racket</c> isn't 0.</summary>
        internal static bool RacketsOn(GameController gc) => Turf(gc).Racket != 0 && (ExpansionOn(gc) || CommandWanted(gc));

        /// <summary>Factions earn control points: a <c>[RCK]ControlPoints::</c> entry or the console.</summary>
        internal static bool PointsOn(GameController gc) => Points(gc).Listed || CommandWanted(gc);
    }
}
