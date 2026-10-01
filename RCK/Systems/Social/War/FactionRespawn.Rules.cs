#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RCK.Social
{
    /// <summary>A faction's respawn tuning (see <see cref="RespawnRules"/>).</summary>
    internal sealed class RespawnSettings
    {
        public int Free = RespawnRules.DefaultFree;
        public int PerTurf = RespawnRules.DefaultPerTurf;
        public int Max = RespawnRules.DefaultMax;
        public int Cooldown = RespawnRules.DefaultCooldown;
        public int Size = RespawnRules.DefaultSize;

        public RespawnSettings Copy() => (RespawnSettings)MemberwiseClone();

        /// <summary>The living members a faction with <paramref name="turfs"/> turfs keeps up: none without turf.</summary>
        public int Target(int turfs) => turfs <= 0 ? 0 : Math.Min(Max, Free + PerTurf * turfs);

        internal bool Set(string name, int value)
        {
            switch (name)
            {
                case "free": Free = value; return true;
                case "perturf": PerTurf = value; return true;
                case "max": Max = value; return true;
                case "cooldown": Cooldown = value; return true;
                case "size": Size = value; return true;
                default: return false;
            }
        }
    }

    /// <summary>One faction's respawn entry.</summary>
    internal sealed class RespawnFaction
    {
        public int Key;
        /// <summary>True when an entry gave it a pool (<c>Crepe=inf</c>, <c>Crepe=40</c>, <c>Crepe=off</c>).</summary>
        public bool Listed;
        /// <summary>Members it may respawn this level: <see cref="RespawnRules.Unlimited"/> until its turf is gone, 0 for none.</summary>
        public int Pool = RespawnRules.Unlimited;
        /// <summary>Unit names, vanilla agent types or custom character names; repeat a name to weight it.</summary>
        public readonly List<string> Units = new List<string>();
        /// <summary>Per-faction settings (<c>Crepe.Cooldown=30</c>) to apply over the level's defaults.</summary>
        public readonly List<KeyValuePair<string, int>> Overrides = new List<KeyValuePair<string, int>>();
        /// <summary>The level's defaults with <see cref="Overrides"/> applied (set by <see cref="RespawnRules.Parse"/>).</summary>
        public RespawnSettings Settings;
    }

    /// <summary>The respawn rules of one level.</summary>
    internal sealed class RespawnConfig
    {
        public readonly RespawnSettings Defaults = new RespawnSettings();
        public readonly Dictionary<int, RespawnFaction> Factions = new Dictionary<int, RespawnFaction>();

        public RespawnSettings For(int key) => Factions.TryGetValue(key, out RespawnFaction f) && f.Settings != null ? f.Settings : Defaults;

        public RespawnFaction Entry(int key) => Factions.TryGetValue(key, out RespawnFaction f) ? f : null;
    }

    /// <summary>
    ///   The respawn string format, pure so it can be tested outside the game: <c>[RCK]FactionRespawn::</c> followed by
    ///   <c>;</c>-separated entries. <c>Faction=pool</c> or <c>Faction=pool:Unit+Unit</c> turns respawn on for a faction
    ///   (pool <c>inf</c>, <c>off</c> or 0 to 999 members); <c>Setting=N</c> sets a default for the level and
    ///   <c>Faction.Setting=N</c> one faction's own. The settings are <c>Free</c>, <c>PerTurf</c>, <c>Max</c>,
    ///   <c>Cooldown</c> and <c>Size</c>. Later entries win.
    /// </summary>
    internal static class RespawnRules
    {
        public const string Prefix = "[RCK]FactionRespawn::";
        public const string Mutator = "RCK_Faction_Respawn";
        public const string Role = "Reinforcement";
        public const int Unlimited = -1;
        public const int DefaultFree = 2, DefaultPerTurf = 2, DefaultMax = 12, DefaultCooldown = 60, DefaultSize = 3;
        public const int MaxPool = 999, MaxUnits = 12, MaxUnitName = 64;
        /// <summary>Respawned members alive at once, all factions together.</summary>
        public const int MaxLiving = 24;

        /// <summary>Setting name (lower case) → its display name, lowest and highest value.</summary>
        public static readonly Dictionary<string, (string Name, int Min, int Max)> Ranges =
            new Dictionary<string, (string, int, int)>(StringComparer.Ordinal)
            {
                { "free", ("Free", 0, 30) },
                { "perturf", ("PerTurf", 0, 10) },
                { "max", ("Max", 1, 30) },
                { "cooldown", ("Cooldown", 10, 600) },
                { "size", ("Size", 1, 6) },
            };

        /// <summary>Parses mutator bodies (the text after the prefix); bad entries go to <paramref name="onError"/> and are skipped.</summary>
        public static RespawnConfig Parse(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var config = new RespawnConfig();
            foreach (string body in bodies)
            {
                if (body == null) continue;
                foreach (string raw in body.Split(';'))
                {
                    string text = raw.Trim();
                    if (text.Length == 0) continue;
                    if (!Apply(config, text, keyIndex, out string error)) onError?.Invoke(text, error);
                }
            }
            foreach (RespawnFaction f in config.Factions.Values)
            {
                f.Settings = config.Defaults.Copy();
                foreach (KeyValuePair<string, int> o in f.Overrides) f.Settings.Set(o.Key, o.Value);
            }
            return config;
        }

        private static bool Apply(RespawnConfig config, string text, Func<string, int> keyIndex, out string error)
        {
            int eq = text.IndexOf('=');
            if (eq <= 0)
            {
                error = "expected Faction=pool or Setting=number";
                return false;
            }
            string left = text.Substring(0, eq).Trim();
            string value = text.Substring(eq + 1).Trim();
            int dot = left.IndexOf('.');
            if (dot >= 0)
            {
                string faction = left.Substring(0, dot).Trim();
                int key = faction.Length == 0 ? -1 : keyIndex(faction);
                if (key < 0)
                {
                    error = $"unknown faction \"{faction}\"";
                    return false;
                }
                if (!Setting(left.Substring(dot + 1).Trim(), value, out string name, out int n, out error)) return false;
                Of(config, key).Overrides.Add(new KeyValuePair<string, int>(name, n));
                return true;
            }
            if (Ranges.ContainsKey(left.ToLowerInvariant()))
            {
                if (!Setting(left, value, out string name, out int n, out error)) return false;
                config.Defaults.Set(name, n);
                return true;
            }
            int k = keyIndex(left);
            if (k < 0)
            {
                error = $"unknown faction or setting \"{left}\"";
                return false;
            }
            if (!Pool(value, out int pool, out List<string> units, out error)) return false;
            RespawnFaction f = Of(config, k);
            f.Listed = true;
            f.Pool = pool;
            f.Units.Clear();
            f.Units.AddRange(units);
            return true;
        }

        private static RespawnFaction Of(RespawnConfig config, int key)
        {
            if (!config.Factions.TryGetValue(key, out RespawnFaction f)) config.Factions[key] = f = new RespawnFaction { Key = key };
            return f;
        }

        private static bool Setting(string setting, string value, out string name, out int n, out string error)
        {
            name = setting.ToLowerInvariant();
            n = 0;
            if (!Ranges.TryGetValue(name, out (string Name, int Min, int Max) r))
            {
                error = $"unknown setting \"{setting}\" (Free, PerTurf, Max, Cooldown or Size)";
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

        /// <summary><c>inf</c>, <c>off</c> or a count, then optionally <c>:Unit+Unit</c>.</summary>
        private static bool Pool(string value, out int pool, out List<string> units, out string error)
        {
            pool = Unlimited;
            units = new List<string>();
            string count = value;
            int colon = value.IndexOf(':');
            if (colon >= 0)
            {
                count = value.Substring(0, colon).Trim();
                foreach (string raw in value.Substring(colon + 1).Split('+'))
                {
                    string unit = raw.Trim();
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
                    units.Add(unit);
                }
                if (units.Count > MaxUnits)
                {
                    error = $"at most {MaxUnits} units";
                    return false;
                }
            }
            string c = count.ToLowerInvariant();
            if (c == "inf") pool = Unlimited;
            else if (c == "off") pool = 0;
            else if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out pool) || pool > MaxPool)
            {
                error = $"pool must be inf, off or 0 to {MaxPool}";
                return false;
            }
            error = null;
            return true;
        }
    }
}
