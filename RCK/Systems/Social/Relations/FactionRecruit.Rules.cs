#nullable disable
using System;
using System.Collections.Generic;

// The pure half of faction recruiting: policies, the FactionRecruit matrix and how they combine, with no game or
// BepInEx references, so tools\FactionTests can compile and test it offline. FactionRecruit.cs holds the game side.
namespace RCK.Social
{
    /// <summary>How an NPC can be recruited. The order is generosity: where several apply, the highest wins.</summary>
    internal enum RecruitPolicy
    {
        /// <summary>Nothing says; a lower-priority source decides.</summary>
        None = 0,
        Off = 1,
        /// <summary>Hire as protection at the vanilla gang-hire price.</summary>
        Paid = 2,
        /// <summary>"Join me" at no cost.</summary>
        Free = 3,
    }

    /// <summary>
    ///   Per-faction recruit policies from mutators such as <c>[RCK]FactionRecruit::Cop=Free;Crepe,Blahd=Paid;*=Off;</c>.
    ///   Each entry is <c>A=Policy</c>: A is a faction number or key, a comma list of them, or <c>*</c> (any faction no
    ///   other entry names). Where several entries match, the most generous policy wins.
    /// </summary>
    internal sealed partial class RecruitMatrix
    {
        private readonly ulong free, paid, off;
        private readonly RecruitPolicy any;

        private RecruitMatrix(ulong free, ulong paid, ulong off, RecruitPolicy any, int entryCount)
        {
            this.free = free;
            this.paid = paid;
            this.off = off;
            this.any = any;
            EntryCount = entryCount;
        }

        public int EntryCount { get; }

        /// <summary>
        ///   The policy for an NPC in the factions <paramref name="keys"/>: the most generous of the entries naming those
        ///   factions and, if any of them is named by none, the <c>*</c> entry. None if nothing matches.
        /// </summary>
        public RecruitPolicy Resolve(ulong keys)
        {
            if ((keys & free) != 0) return RecruitPolicy.Free;
            RecruitPolicy best = (keys & paid) != 0 ? RecruitPolicy.Paid : (keys & off) != 0 ? RecruitPolicy.Off : RecruitPolicy.None;
            if ((keys & ~(free | paid | off)) != 0 && any > best) best = any;
            return best;
        }

        internal static bool TryParsePolicy(string text, out RecruitPolicy policy)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "free": policy = RecruitPolicy.Free; return true;
                case "paid": policy = RecruitPolicy.Paid; return true;
                case "off": policy = RecruitPolicy.Off; return true;
                default: policy = RecruitPolicy.None; return false;
            }
        }

        /// <summary>
        ///   Builds the matrix from mutator bodies (the text after the prefix). <paramref name="keyIndex"/> maps a faction
        ///   name to its key index (-1 if unknown); each bad entry is passed to <paramref name="onError"/> and skipped.
        ///   Null if no entry is valid.
        /// </summary>
        internal static RecruitMatrix Build(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            ulong free = 0, paid = 0, off = 0;
            RecruitPolicy any = RecruitPolicy.None;
            int count = 0;
            foreach (string body in bodies)
            {
                foreach (string part in body.Split(';'))
                {
                    string text = part.Trim();
                    if (text.Length == 0) continue;
                    int eq = text.LastIndexOf('=');
                    if (eq <= 0) { onError?.Invoke(text, "expected Faction=Free, Paid or Off"); continue; }
                    if (!TryParsePolicy(text.Substring(eq + 1), out RecruitPolicy policy)) { onError?.Invoke(text, "unknown policy (use Free, Paid or Off)"); continue; }

                    string side = text.Substring(0, eq).Trim();
                    if (side == "*")
                    {
                        if (policy > any) any = policy;
                        count++;
                        continue;
                    }
                    ulong keys = 0;
                    string error = null;
                    foreach (string token in side.Split(','))
                    {
                        string t = token.Trim();
                        if (t.Length == 0) continue;
                        int index = keyIndex(t);
                        if (index < 0 || index > 63) { error = $"unknown faction \"{t}\""; break; }
                        keys |= 1UL << index;
                    }
                    if (error == null && keys == 0) error = "no faction before =";
                    if (error != null) { onError?.Invoke(text, error); continue; }
                    switch (policy)
                    {
                        case RecruitPolicy.Free: free |= keys; break;
                        case RecruitPolicy.Paid: paid |= keys; break;
                        default: off |= keys; break;
                    }
                    count++;
                }
            }
            return count == 0 ? null : new RecruitMatrix(free, paid, off, any, count);
        }
    }

    internal static class RecruitRules
    {
        /// <summary>
        ///   The policy for recruiting an NPC in the factions <paramref name="keys"/>: the NPC's own recruit trait, else
        ///   the matrix, else the global mutators, else Off. Never None. <paramref name="keys"/> 0 (no faction) is Off.
        /// </summary>
        public static RecruitPolicy Decide(RecruitPolicy trait, RecruitMatrix matrix, ulong keys, RecruitPolicy global)
        {
            if (keys == 0 || trait == RecruitPolicy.Off) return RecruitPolicy.Off;
            if (trait != RecruitPolicy.None) return trait;
            RecruitPolicy fromMatrix = matrix == null ? RecruitPolicy.None : matrix.Resolve(keys);
            if (fromMatrix != RecruitPolicy.None) return fromMatrix;
            return global == RecruitPolicy.None ? RecruitPolicy.Off : global;
        }

        /// <summary>
        ///   The button a player gets: members of a shared faction (<paramref name="shared"/>) get that faction's policy.
        ///   A player in a faction the NPC's factions are Friendly with (<paramref name="friendly"/>) may hire at the paid
        ///   price wherever the NPC's own factions (<paramref name="npcKeys"/>) allow recruiting at all.
        /// </summary>
        public static RecruitPolicy ForPlayer(RecruitPolicy trait, RecruitMatrix matrix, RecruitPolicy global, ulong shared, ulong npcKeys, bool friendly)
        {
            RecruitPolicy member = Decide(trait, matrix, shared, global);
            if (member != RecruitPolicy.Off || !friendly) return member;
            return Decide(trait, matrix, npcKeys, global) == RecruitPolicy.Off ? RecruitPolicy.Off : RecruitPolicy.Paid;
        }
    }
}
