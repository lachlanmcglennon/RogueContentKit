#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RCK.Social
{
    /// <summary>
    ///   A relationship matrix between factions, from campaign or level mutators such as
    ///   <c>[RCK]FactionRel::1&gt;2=Hateful;3&lt;&gt;Blahd=Annoyed;7&gt;*=Friendly;</c>. <c>A&gt;B</c> is how members of A
    ///   treat members of B, <c>A&lt;&gt;B</c> sets both ways and <c>A&lt;B</c> is <c>B&gt;A</c>. A side is a faction number
    ///   or key, a comma list of them, <c>Player</c>, or <c>*</c> (anyone who isn't on the other side). Entries naming
    ///   both sides beat <c>*</c> entries; within each kind the strongest relationship wins. <c>=Territorial</c> starts
    ///   the direction Annoyed and turns it Hateful on the source's turf (see <see cref="Territorial"/>). The legacy
    ///   <c>[CCU]FactionRel::</c> spelling (<see cref="RckData.FactionMatrixLegacyPrefixes"/>) is read too.
    /// </summary>
    internal sealed partial class FactionMatrix
    {
        private static readonly MutatorCache<FactionMatrix> cache = new MutatorCache<FactionMatrix>(c => FindPrefix(c, out _) >= 0, Parse, null);

        /// <summary>Finds a matrix prefix (RCK's, then the legacy spellings) in a mutator name; returns its index, or -1.</summary>
        internal static int FindPrefix(string mutator, out int prefixLength)
        {
            int start = mutator.IndexOf(RckData.FactionMatrixPrefix, StringComparison.Ordinal);
            if (start >= 0)
            {
                prefixLength = RckData.FactionMatrixPrefix.Length;
                return start;
            }
            foreach (string legacy in RckData.FactionMatrixLegacyPrefixes)
            {
                start = mutator.IndexOf(legacy, StringComparison.Ordinal);
                if (start >= 0)
                {
                    prefixLength = legacy.Length;
                    return start;
                }
            }
            prefixLength = 0;
            return -1;
        }

        /// <summary>The matrix for the running level, or null if no mutator sets one. Parsed again only when the mutators change.</summary>
        public static FactionMatrix Current() => cache.Current();

        private static FactionMatrix Parse(List<string> raws)
        {
            if (raws.Count == 0) return null;
            var bodies = new List<string>(raws.Count);
            foreach (string raw in raws)
            {
                int start = FindPrefix(raw, out int prefixLength);
                bodies.Add(raw.Substring(start + prefixLength));
            }
            FactionMatrix matrix = Build(bodies, Factions.KeyIndex,
                (text, error) => Rck.Log.LogWarning($"Factions: ignored {RckData.FactionMatrixPrefix} entry \"{text}\": {error}."));
            if (matrix != null) Rck.Log.LogInfo($"Factions: relationship matrix with {matrix.EntryCount} directed entries.");
            return matrix;
        }
    }
    /// <summary>The mutator list would otherwise show the raw matrix strings as mutator names.</summary>
    [HarmonyPatch(typeof(Unlocks), nameof(Unlocks.GetChallengeName))]
    internal static class Unlocks_GetChallengeName_FactionMatrix_Patch
    {
        private static void Postfix(string unlockName, ref string __result)
        {
            if (unlockName == null) return;
            if (FactionMatrix.FindPrefix(unlockName, out _) >= 0) __result = Rck.ExtensionTag + " Faction Relationships";
            else if (RecruitMatrix.IsRecruitMutator(unlockName)) __result = Rck.ExtensionTag + " Faction Recruiting (per faction)";
            else if (unlockName.IndexOf(DisguiseRules.Prefix, System.StringComparison.Ordinal) >= 0) __result = Rck.ExtensionTag + " Faction Disguises (custom)";
            else if (unlockName.IndexOf(RaidRules.Prefix, System.StringComparison.Ordinal) >= 0) __result = Rck.ExtensionTag + " Faction Raids (scheduled)";
            else if (FactionDisplay.IsNameMutator(unlockName)) __result = Rck.ExtensionTag + " Faction Names";
        }
    }
}
