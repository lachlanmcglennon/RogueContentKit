#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RCK.Social
{
    /// <summary>
    ///   Shared parsing for the faction war's rule strings: a prefix followed by <c>;</c>-separated <c>Name=Value</c>
    ///   entries. Names are case-insensitive, later entries win, and bad entries are reported and skipped. Pure, so
    ///   it can be tested outside the game.
    /// </summary>
    internal static class WarRuleText
    {
        /// <summary>The <c>Name=Value</c> entries of <paramref name="bodies"/> (the text after the prefix), names lower case.</summary>
        public static List<KeyValuePair<string, string>> Entries(IEnumerable<string> bodies, Action<string, string> onError)
        {
            var entries = new List<KeyValuePair<string, string>>();
            if (bodies == null) return entries;
            foreach (string body in bodies)
            {
                if (body == null) continue;
                foreach (string raw in body.Split(';'))
                {
                    string text = raw.Trim();
                    if (text.Length == 0) continue;
                    int eq = text.IndexOf('=');
                    if (eq <= 0)
                    {
                        onError?.Invoke(text, "expected Name=Value");
                        continue;
                    }
                    entries.Add(new KeyValuePair<string, string>(text.Substring(0, eq).Trim().ToLowerInvariant(), text.Substring(eq + 1).Trim()));
                }
            }
            return entries;
        }

        /// <summary>A whole number setting in its range.</summary>
        public static bool Int(Dictionary<string, (string Name, int Min, int Max)> ranges, string name, string value, out int n, out string error)
        {
            n = 0;
            if (!ranges.TryGetValue(name, out (string Name, int Min, int Max) r))
            {
                var names = new List<string>();
                foreach ((string Name, int Min, int Max) v in ranges.Values) names.Add(v.Name);
                error = $"unknown setting \"{name}\" ({string.Join(", ", names.ToArray())})";
                return false;
            }
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < r.Min || n > r.Max)
            {
                error = $"{r.Name} must be {r.Min} to {r.Max}";
                return false;
            }
            error = null;
            return true;
        }
    }

    // ---- [RCK]TurfWar:: ----

    /// <summary>Turf expansion and rackets (see <see cref="TurfExpansion"/>).</summary>
    internal sealed class TurfWarSettings
    {
        /// <summary>True when the level has a <c>[RCK]TurfWar::</c> entry.</summary>
        public bool Listed;
        /// <summary>Seconds between a faction's expansion moves; 0 for none.</summary>
        public int Expand = TurfWarRules.DefaultExpand;
        /// <summary>1 if factions racket commoner turf, 0 if not.</summary>
        public int Racket = 1;
        /// <summary>How far (in tiles) from its own posts a faction reaches for new turf.</summary>
        public int Reach = TurfWarRules.DefaultReach;
        /// <summary>Members a faction sends per move.</summary>
        public int Party = TurfWarRules.DefaultParty;
        /// <summary>How many times a ruined racket gets a new owner this level; 0 for never.</summary>
        public int Reopen = TurfWarRules.DefaultReopen;

        internal bool Set(string name, int value)
        {
            switch (name)
            {
                case "expand": Expand = value; return true;
                case "racket": Racket = value; return true;
                case "reach": Reach = value; return true;
                case "party": Party = value; return true;
                case "reopen": Reopen = value; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    ///   <c>[RCK]TurfWar::Expand=45;Racket=1;Reach=30;Party=3;</c>: factions holding turf send parties to empty turf
    ///   and commoner turf (rackets) near their own. <c>Expand=0</c> stops expansion and <c>Racket=0</c> rackets.
    ///   <c>Reopen=N</c> is how many times a ruined racket gets a new owner. An entry turns expansion on, as does the
    ///   <c>RCK_Turf_Expansion</c> mutator.
    /// </summary>
    internal static class TurfWarRules
    {
        public const string Prefix = "[RCK]TurfWar::";
        public const string Mutator = "RCK_Turf_Expansion";
        public const int DefaultExpand = 45, DefaultReach = 30, DefaultParty = 3, DefaultReopen = 3, MinExpand = 15;

        public static readonly Dictionary<string, (string Name, int Min, int Max)> Ranges =
            new Dictionary<string, (string, int, int)>(StringComparer.Ordinal)
            {
                { "expand", ("Expand", 0, 600) },
                { "racket", ("Racket", 0, 1) },
                { "reach", ("Reach", 5, 200) },
                { "party", ("Party", 1, 6) },
                { "reopen", ("Reopen", 0, 9) },
            };

        public static TurfWarSettings Parse(IEnumerable<string> bodies, Action<string, string> onError)
        {
            var s = new TurfWarSettings();
            foreach (KeyValuePair<string, string> e in WarRuleText.Entries(bodies, onError))
            {
                s.Listed = true;
                if (WarRuleText.Int(Ranges, e.Key, e.Value, out int n, out string error)) s.Set(e.Key, n);
                else onError?.Invoke($"{e.Key}={e.Value}", error);
            }
            if (s.Expand > 0 && s.Expand < MinExpand) s.Expand = MinExpand;
            return s;
        }
    }

    // ---- [RCK]ControlPoints:: ----

    /// <summary>Control point income and bonuses (see <see cref="ControlPoints"/>).</summary>
    internal sealed class ControlPointSettings
    {
        public bool Listed;
        public int Base = 1, PerTurf = 2, PerCommon = 1, Capture = 10, Kill = 1, Max = 200, Interval = 10, Start = 30;

        /// <summary>The income per interval of a faction holding <paramref name="turfs"/> turfs and <paramref name="rackets"/> rackets.</summary>
        public int Income(int turfs, int rackets) => Base + PerTurf * Math.Max(0, turfs) + PerCommon * Math.Max(0, rackets);

        internal bool Set(string name, int value)
        {
            switch (name)
            {
                case "base": Base = value; return true;
                case "perturf": PerTurf = value; return true;
                case "percommon": PerCommon = value; return true;
                case "capture": Capture = value; return true;
                case "kill": Kill = value; return true;
                case "max": Max = value; return true;
                case "interval": Interval = value; return true;
                case "start": Start = value; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    ///   <c>[RCK]ControlPoints::Base=1;PerTurf=2;PerCommon=1;Capture=10;Kill=1;Max=200;Interval=10;Start=30;</c>.
    ///   Every <c>Interval</c> seconds each faction in the war earns <c>Base + PerTurf × turfs + PerCommon × rackets</c>
    ///   control points, up to <c>Max</c>, plus <c>Capture</c> for each turf it takes (half for a racket) and
    ///   <c>Kill</c> for each enemy faction member its side takes down.
    /// </summary>
    internal static class ControlPointRules
    {
        public const string Prefix = "[RCK]ControlPoints::";

        public static readonly Dictionary<string, (string Name, int Min, int Max)> Ranges =
            new Dictionary<string, (string, int, int)>(StringComparer.Ordinal)
            {
                { "base", ("Base", 0, 50) },
                { "perturf", ("PerTurf", 0, 50) },
                { "percommon", ("PerCommon", 0, 50) },
                { "capture", ("Capture", 0, 500) },
                { "kill", ("Kill", 0, 100) },
                { "max", ("Max", 10, 9999) },
                { "interval", ("Interval", 5, 120) },
                { "start", ("Start", 0, 9999) },
            };

        public static ControlPointSettings Parse(IEnumerable<string> bodies, Action<string, string> onError)
        {
            var s = new ControlPointSettings();
            foreach (KeyValuePair<string, string> e in WarRuleText.Entries(bodies, onError))
            {
                s.Listed = true;
                if (WarRuleText.Int(Ranges, e.Key, e.Value, out int n, out string error)) s.Set(e.Key, n);
                else onError?.Invoke($"{e.Key}={e.Value}", error);
            }
            if (s.Start > s.Max) s.Start = s.Max;
            return s;
        }
    }

    // ---- [RCK]Command:: ----

    /// <summary>The commander console's rules (see <see cref="Command"/>).</summary>
    internal sealed class CommandSettings
    {
        public bool Listed;
        /// <summary>The faction the player commands; -1 for the player's own faction that holds turf.</summary>
        public int Faction = -1;
        /// <summary>The units to recruit, each with its cost (-1 for <see cref="Cost"/>); empty for the faction's respawn units.</summary>
        public readonly List<CommandUnit> Units = new List<CommandUnit>();
        /// <summary>Control points per recruit.</summary>
        public int Cost = 10;
        /// <summary>Recruits per squad.</summary>
        public int Size = 3;
        /// <summary>Recruits alive at once.</summary>
        public int Cap = 12;
        /// <summary>Percent of the cost a disbanded recruit gives back.</summary>
        public int Refund = 50;

        /// <summary>What recruiting <paramref name="unit"/> costs.</summary>
        public int CostOf(CommandUnit unit) => unit.Cost >= 0 ? unit.Cost : Cost;

        internal bool Set(string name, int value)
        {
            switch (name)
            {
                case "cost": Cost = value; return true;
                case "size": Size = value; return true;
                case "cap": Cap = value; return true;
                case "refund": Refund = value; return true;
                default: return false;
            }
        }
    }

    /// <summary>One recruitable unit: a custom character's name or an agent type, of any faction, and its own cost or -1.</summary>
    internal struct CommandUnit
    {
        public string Name;
        public int Cost;

        public CommandUnit(string name, int cost)
        {
            Name = name;
            Cost = cost;
        }

        public override string ToString() => Cost >= 0 ? $"{Name}:{Cost}" : Name;
    }

    /// <summary>
    ///   <c>[RCK]Command::Faction=Crepe;Units=Crepe_Grunt+Blahd Heavy:25+Cop;Cost=10;Size=3;Cap=12;Refund=50;</c>: the
    ///   player commands a faction from the commander console (see <see cref="Command"/>). <c>Units</c> may name any faction's
    ///   units; each recruit is sworn to the commanded faction alone, and <c>Name:Cost</c> overrides <c>Cost</c> for one
    ///   unit. A unit's role (a medic, a dealer) comes from its own character's traits. An entry turns the console on,
    ///   as does the <c>RCK_Commander</c> mutator.
    /// </summary>
    internal static class CommandRules
    {
        public const string Prefix = "[RCK]Command::";
        public const string Mutator = "RCK_Commander";
        public const int MaxUnits = 12, MaxUnitName = 64;

        public static readonly Dictionary<string, (string Name, int Min, int Max)> Ranges =
            new Dictionary<string, (string, int, int)>(StringComparer.Ordinal)
            {
                { "cost", ("Cost", 0, 500) },
                { "size", ("Size", 1, 6) },
                { "cap", ("Cap", 1, 24) },
                { "refund", ("Refund", 0, 100) },
            };

        public static CommandSettings Parse(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var s = new CommandSettings();
            foreach (KeyValuePair<string, string> e in WarRuleText.Entries(bodies, onError))
            {
                s.Listed = true;
                string text = $"{e.Key}={e.Value}";
                if (e.Key == "faction")
                {
                    int key = keyIndex(e.Value);
                    if (key < 0) onError?.Invoke(text, $"unknown faction \"{e.Value}\"");
                    else s.Faction = key;
                    continue;
                }
                if (e.Key == "units")
                {
                    if (!Units(e.Value, out List<CommandUnit> units, out string why)) onError?.Invoke(text, why);
                    else
                    {
                        s.Units.Clear();
                        s.Units.AddRange(units);
                    }
                    continue;
                }
                if (!Ranges.ContainsKey(e.Key))
                {
                    onError?.Invoke(text, $"unknown setting \"{e.Key}\" (Faction, Units, Cost, Size, Cap or Refund)");
                    continue;
                }
                if (WarRuleText.Int(Ranges, e.Key, e.Value, out int n, out string error)) s.Set(e.Key, n);
                else onError?.Invoke(text, error);
            }
            return s;
        }

        /// <summary><c>A+B:25+C</c>: names joined by <c>+</c>, each with an optional <c>:Cost</c> (0-500); a name keeps any colon not followed by digits only.</summary>
        internal static bool Units(string value, out List<CommandUnit> units, out string error)
        {
            units = new List<CommandUnit>();
            (int min, int max) = (Ranges["cost"].Min, Ranges["cost"].Max);
            foreach (string raw in value.Split('+'))
            {
                string unit = raw.Trim();
                int cost = -1;
                int colon = unit.LastIndexOf(':');
                if (colon >= 0 && IsDigits(unit, colon + 1))
                {
                    if (!int.TryParse(unit.Substring(colon + 1), out cost) || cost < min || cost > max)
                    {
                        error = $"\"{unit}\": a unit's cost is {min}-{max}";
                        return false;
                    }
                    unit = unit.Substring(0, colon).Trim();
                }
                if (unit.Length == 0)
                {
                    error = "empty unit name";
                    return false;
                }
                if (unit.Length > MaxUnitName)
                {
                    error = $"unit names are at most {MaxUnitName} characters";
                    return false;
                }
                units.Add(new CommandUnit(unit, cost));
            }
            if (units.Count > MaxUnits)
            {
                error = $"at most {MaxUnits} units";
                return false;
            }
            error = null;
            return true;
        }

        private static bool IsDigits(string s, int from)
        {
            if (from >= s.Length) return false;
            for (int i = from; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }
    }
}
