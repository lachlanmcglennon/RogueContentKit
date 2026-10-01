#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{
    /// <summary>
    ///   Faction names for on-screen messages. A campaign can name its factions with a
    ///   <c>[RCK]FactionName::1=The Contractor;2=The Summit</c> mutator; otherwise a key reads as itself without
    ///   underscores (<c>Faction 3</c>, <c>Upper Cruster</c>).
    /// </summary>
    internal static class FactionDisplay
    {
        private static readonly MutatorCache<Dictionary<int, string>> names = new MutatorCache<Dictionary<int, string>>(
            c => c.IndexOf(FactionNameRules.Prefix, StringComparison.Ordinal) >= 0,
            raws =>
            {
                var bodies = new List<string>(raws.Count);
                foreach (string c in raws)
                    bodies.Add(c.Substring(c.IndexOf(FactionNameRules.Prefix, StringComparison.Ordinal) + FactionNameRules.Prefix.Length));
                return FactionNameRules.Build(bodies, Factions.KeyIndex,
                    (entry, error) => Rck.Log.LogWarning($"Factions: ignored {FactionNameRules.Prefix} entry \"{entry}\": {error}."));
            },
            new Dictionary<int, string>());

        private static Dictionary<int, string> Names() => names.Current();

        /// <summary>The faction's designer name, else its key without underscores.</summary>
        public static string Short(string key)
        {
            int i = Factions.KeyIndex(key);
            return i >= 0 && Names().TryGetValue(i, out string name) ? name : FactionNameRules.Short(key);
        }

        /// <summary>The faction's designer name, else its members (<c>the Crepes</c>), for sentences.</summary>
        public static string Plural(string key)
        {
            int i = Factions.KeyIndex(key);
            return i >= 0 && Names().TryGetValue(i, out string name) ? name : FactionNameRules.Plural(key);
        }

        internal static bool IsNameMutator(string mutator) => mutator.IndexOf(FactionNameRules.Prefix, StringComparison.Ordinal) >= 0;
    }
}
