#nullable disable
using System;
using System.Collections.Generic;

// Pure disguise map logic with no game or BepInEx references, so tools\FactionTests can compile and test it offline.
namespace RCK.Social
{
    internal static class DisguiseRules
    {
        public const string Mutator = "RCK_Faction_Disguises";
        public const string Prefix = "[RCK]Disguise::";

        /// <summary>The headpieces that pass for a faction's colours out of the box: item name, faction key.</summary>
        public static readonly KeyValuePair<string, string>[] Defaults =
        {
            new KeyValuePair<string, string>("CopHat", "Cop"),
            new KeyValuePair<string, string>("Cop2Hat", "Cop"),
            new KeyValuePair<string, string>("HatBlue", "Crepe"),
            new KeyValuePair<string, string>("HatRed", "Blahd"),
            new KeyValuePair<string, string>("SoldierHelmet", "Soldier"),
            new KeyValuePair<string, string>("ThiefHat", "Thief"),
            new KeyValuePair<string, string>("HackerGlasses", "Hacker"),
            new KeyValuePair<string, string>("FireHelmet", "Firefighter"),
            new KeyValuePair<string, string>("Fedora", "Mafia"),
            new KeyValuePair<string, string>("DoctorHeadLamp", "Scientist"),
        };

        /// <summary>
        ///   Builds the item → faction index map: the defaults, then each <c>[RCK]Disguise::</c> body in order.
        ///   A body is <c>;</c>-separated <c>Item=key</c> entries; <c>Item=None</c> takes an item off the map.
        /// </summary>
        public static Dictionary<string, int> Build(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> d in Defaults)
            {
                int k = keyIndex(d.Value);
                if (k >= 0) map[d.Key] = k;
            }
            if (bodies == null) return map;
            foreach (string body in bodies)
            {
                if (string.IsNullOrEmpty(body)) continue;
                foreach (string raw in body.Split(';'))
                {
                    string entry = raw.Trim();
                    if (entry.Length == 0) continue;
                    int eq = entry.IndexOf('=');
                    string item = eq > 0 ? entry.Substring(0, eq).Trim() : "";
                    string key = eq > 0 ? entry.Substring(eq + 1).Trim() : "";
                    if (item.Length == 0 || key.Length == 0)
                    {
                        onError?.Invoke(entry, "expected Item=faction");
                        continue;
                    }
                    if (string.Equals(key, "None", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "Off", StringComparison.OrdinalIgnoreCase))
                    {
                        map.Remove(item);
                        continue;
                    }
                    int index = keyIndex(key);
                    if (index < 0)
                    {
                        onError?.Invoke(entry, $"unknown faction {key}");
                        continue;
                    }
                    map[item] = index;
                }
            }
            return map;
        }
    }
}
