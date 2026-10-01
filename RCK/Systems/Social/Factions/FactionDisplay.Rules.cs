#nullable disable
using System;
using System.Collections.Generic;

// Pure faction-name logic with no game or BepInEx references, so tools\FactionTests can compile and test it offline.
namespace RCK.Social
{
    internal static class FactionNameRules
    {
        public const string Prefix = "[RCK]FactionName::";

        private static readonly Dictionary<string, string> plurals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Blahd", "the Blahds" }, { "Crepe", "the Crepes" }, { "Cannibal", "the Cannibals" }, { "Gorilla", "the Gorillas" },
            { "Soldier", "the Soldiers" }, { "Firefighter", "the Firefighters" }, { "Scientist", "the Scientists" },
            { "Mafia", "the Mafia" }, { "Upper_Cruster", "the Upper Crusters" }, { "Vampire", "the Vampires" },
            { "Werewolf", "the Werewolves" }, { "Common_Folk", "the common folk" }, { "Slavemaster", "the Slavemasters" },
            { "Cop", "the Cops" }, { "Zombie", "the Zombies" }, { "Thief", "the Thieves" }, { "Hacker", "the Hackers" },
        };

        /// <summary>
        ///   Parses <c>[RCK]FactionName::</c> bodies: <c>;</c>-separated <c>faction=Name</c> entries (<c>1=The Contractor</c>,
        ///   <c>Blahd=Blahd Nation</c>). Later entries win. Returns faction index → name.
        /// </summary>
        public static Dictionary<int, string> Build(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var names = new Dictionary<int, string>();
            if (bodies == null) return names;
            foreach (string body in bodies)
            {
                if (string.IsNullOrEmpty(body)) continue;
                foreach (string raw in body.Split(';'))
                {
                    string entry = raw.Trim();
                    if (entry.Length == 0) continue;
                    int eq = entry.IndexOf('=');
                    string key = eq > 0 ? entry.Substring(0, eq).Trim() : "";
                    string name = eq > 0 ? entry.Substring(eq + 1).Trim() : "";
                    if (key.Length == 0 || name.Length == 0)
                    {
                        onError?.Invoke(entry, "expected faction=Name");
                        continue;
                    }
                    int index = keyIndex(key);
                    if (index < 0)
                    {
                        onError?.Invoke(entry, $"unknown faction {key}");
                        continue;
                    }
                    names[index] = name;
                }
            }
            return names;
        }

        /// <summary>A faction key as a short name: <c>Upper_Cruster</c> → <c>Upper Cruster</c>, <c>Faction_3</c> → <c>Faction 3</c>.</summary>
        public static string Short(string key) => string.IsNullOrEmpty(key) ? "" : key.Replace('_', ' ');

        /// <summary>A faction key as its members, for sentences: <c>the Crepes</c>, <c>the Mafia</c>, <c>Faction 3</c>.</summary>
        public static string Plural(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return plurals.TryGetValue(key, out string p) ? p : Short(key);
        }
    }
}
