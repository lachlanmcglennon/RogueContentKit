#nullable disable

// Pure faction rule logic with no game or BepInEx references, so tools\FactionTests can compile and test it offline.
namespace RCK.Social
{
    /// <summary>
    ///   The relationship a faction rule asks for. When several rules match one direction, the highest value wins, so
    ///   the numeric order is the precedence order (Hateful, Territorial, Annoyed, Friendly, Aligned, Neutral).
    /// </summary>
    internal enum FactionGrade
    {
        None = 0,
        Neutral = 1,
        Aligned = 2,
        Friendly = 3,
        Annoyed = 4,
        /// <summary>Starts Annoyed and turns Hateful toward a member standing on the holder's turf (see Territorial).</summary>
        Territorial = 5,
        Hateful = 6,
    }

    /// <summary>One agent's faction traits as bitmasks over the faction key indices.</summary>
    internal struct FactionProfile
    {
        public ulong Aligned, Hostile, Territorial, Annoyed, Friendly, Neutral, Member;

        /// <summary>
        ///   The role traits (<c>&lt;key&gt;_Vengeful</c>, <c>&lt;key&gt;_Leader</c>, <c>&lt;key&gt;_Calls_Backup</c>,
        ///   <c>&lt;key&gt;_Reinforcement</c>). They aren't rules and don't make the holder a member.
        /// </summary>
        public ulong Vengeful, Leader, Backup, Reinforcement;

        /// <summary>The agent is sworn to the one faction in <see cref="Aligned"/> (a commanded recruit): no vanilla membership counts.</summary>
        public bool Sworn;

        public ulong Rules => Aligned | Hostile | Territorial | Annoyed | Friendly | Neutral;
    }

    internal static class FactionRules
    {
        /// <summary>The relationship names a matrix accepts, strongest first, for messages.</summary>
        public const string GradeNames = "Hateful, Territorial, Annoyed, Friendly, Aligned or Neutral";

        /// <summary>
        ///   The strongest faction trait rule between two agents: <paramref name="ma"/> and <paramref name="mb"/> are
        ///   the factions each belongs to. A rule on either side applies both ways.
        /// </summary>
        public static FactionGrade Resolve(FactionProfile pa, ulong ma, FactionProfile pb, ulong mb)
        {
            if ((pa.Rules | pb.Rules) == 0) return FactionGrade.None;
            if (((pa.Hostile & mb) | (pb.Hostile & ma)) != 0) return FactionGrade.Hateful;
            if (((pa.Territorial & mb) | (pb.Territorial & ma)) != 0) return FactionGrade.Territorial;
            if (((pa.Annoyed & mb) | (pb.Annoyed & ma)) != 0) return FactionGrade.Annoyed;
            if (((pa.Friendly & mb) | (pb.Friendly & ma)) != 0) return FactionGrade.Friendly;
            if (((pa.Aligned & mb) | (pb.Aligned & ma)) != 0) return FactionGrade.Aligned;
            if (((pa.Neutral & mb) | (pb.Neutral & ma)) != 0) return FactionGrade.Neutral;
            return FactionGrade.None;
        }

        /// <summary>A matrix relationship name; <c>Hostile</c> means <c>Hateful</c>. Case doesn't matter.</summary>
        public static bool TryParseGrade(string text, out FactionGrade grade)
        {
            switch ((text ?? "").ToLowerInvariant())
            {
                case "hateful":
                case "hostile": grade = FactionGrade.Hateful; return true;
                case "territorial": grade = FactionGrade.Territorial; return true;
                case "annoyed": grade = FactionGrade.Annoyed; return true;
                case "friendly": grade = FactionGrade.Friendly; return true;
                case "aligned": grade = FactionGrade.Aligned; return true;
                case "neutral": grade = FactionGrade.Neutral; return true;
                default: grade = FactionGrade.None; return false;
            }
        }

        /// <summary>The vanilla relationship a grade starts as. Territorial starts Annoyed.</summary>
        public static string RelOf(FactionGrade grade)
        {
            switch (grade)
            {
                case FactionGrade.Hateful: return "Hateful";
                case FactionGrade.Territorial:
                case FactionGrade.Annoyed: return "Annoyed";
                case FactionGrade.Friendly: return "Friendly";
                case FactionGrade.Aligned: return "Aligned";
                default: return "Neutral";
            }
        }

        /// <summary>The starting hate for a grade: 5 (hostile) for Hateful, otherwise 0.</summary>
        public static int HateOf(FactionGrade grade) => grade == FactionGrade.Hateful ? 5 : 0;

        /// <summary>Annoyed, Territorial or Hateful: the grades street innocence holds back until the agent is caught.</summary>
        public static bool IsHostile(FactionGrade grade) => grade >= FactionGrade.Annoyed;
    }
}
