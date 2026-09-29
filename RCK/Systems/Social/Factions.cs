#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{

    /// <summary>
    ///   Numbered factions (<c>Faction_1</c> to <c>Faction_20</c>) and named ones (<c>Blahd</c>, <c>Cop</c>, ...). An
    ///   agent holding <c>&lt;key&gt;_Aligned</c> is a member; a named faction also counts the matching vanilla agents
    ///   as members. <c>&lt;key&gt;_Hostile/_Annoyed/_Friendly/_Neutral</c> set the holder and every member to that
    ///   relationship, both ways. <c>&lt;key&gt;_Territorial</c> starts them Annoyed, and each side turns Hateful toward
    ///   the other on its own turf (see <see cref="Territorial"/>). The strongest matching rule wins (see
    ///   <see cref="FactionGrade"/>) and is set once.
    ///   The player trait <c>&lt;key&gt;_Member</c> only makes the holder a member. A <see cref="FactionMatrix"/>
    ///   mutator sets directed relationships between factions and beats the traits.
    /// </summary>
    internal static class Factions
    {
        private const int SlotAligned = 0, SlotHostile = 1, SlotAnnoyed = 2, SlotFriendly = 3, SlotNeutral = 4, SlotMember = 5, SlotTerritorial = 6;

        private sealed class Named
        {
            public Named(string plural, Func<Agent, bool> isMember)
            {
                Plural = plural;
                IsMember = isMember;
            }

            public string Plural { get; }
            public Func<Agent, bool> IsMember { get; }
        }

        private static readonly Dictionary<string, Named> named = new Dictionary<string, Named>(StringComparer.Ordinal)
        {
            { "Blahd", new Named("Blahds", a => a.agentName == "GangbangerB") },
            { "Crepe", new Named("Crepes", a => a.agentName == "Gangbanger") },
            { "Cannibal", new Named("Cannibals", a => a.agentName == "Cannibal") },
            { "Gorilla", new Named("Gorillas", a => a.agentName == "Gorilla") },
            { "Soldier", new Named("Soldiers", a => a.agentName == "Soldier") },
            { "Firefighter", new Named("Firefighters", a => a.agentName == "Firefighter") },
            { "Scientist", new Named("Scientists", a => a.agentName == "Scientist") },
            { "Mafia", new Named("the Mafia", a => a.agentName == "Mafia" || SocialRules.Has(a, "MafiaAligned")) },
            { "Upper_Cruster", new Named("Upper Crusters", a => a.agentName == "UpperCruster" || SocialRules.Has(a, "UpperCrusty")) },
            { "Vampire", new Named("Vampires", a => a.agentName == "Vampire") },
            { "Werewolf", new Named("Werewolves", a => a.agentName == "WerewolfB" || a.agentName == "Werewolf") },
            { "Common_Folk", new Named("common folk", IsCommonFolk) },
            { "Slavemaster", new Named("Slavemasters", a => a.agentName == "Slavemaster") },
            { "Cop", new Named("the law (Cops, Supercops, Cop Bots)", a => a.enforcer || a.agentName == "Cop" || a.agentName == "Cop2" || a.agentName == "CopBot" || SocialRules.Has(a, "TheLaw")) },
            { "Zombie", new Named("Zombies", a => a.zombified || a.agentName == "Zombie") },
            { "Thief", new Named("Thieves", a => a.agentName == "Thief") },
            { "Hacker", new Named("Hackers", a => a.agentName == "Hacker") },
        };

        private static readonly Dictionary<string, KeyValuePair<int, int>> byTrait = new Dictionary<string, KeyValuePair<int, int>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> byToken = new Dictionary<string, int>(StringComparer.Ordinal);
        private static string[] keys = new string[0];
        private static Func<Agent, bool>[] vanillaMembers = new Func<Agent, bool>[0];

        public static IReadOnlyList<string> Keys => keys;

        public static void Initialize()
        {
            string[] k = RckData.FactionKeys;
            if (k.Length > 64)
            {
                Rck.Log.LogError($"Factions: {k.Length} faction keys, but at most 64 are supported; factions are off.");
                return;
            }
            var members = new Func<Agent, bool>[k.Length];
            for (int i = 0; i < k.Length; i++)
            {
                bool numbered = k[i].StartsWith("Faction_", StringComparison.Ordinal);
                if (named.TryGetValue(k[i], out Named n)) members[i] = n.IsMember;
                else if (!numbered) Rck.Log.LogError($"Factions: no member rule for named faction {k[i]}.");

                byToken[Token(k[i])] = i;
                foreach (string grade in AllGrades())
                {
                    int slot = SlotOf(grade);
                    string trait = k[i] + "_" + grade;
                    if (slot < 0) Rck.Log.LogError($"Factions: unknown grade {grade}.");
                    else if (!Rck.IsRckTrait(trait)) Rck.Log.LogError($"Factions: trait {trait} is not registered.");
                    else byTrait[trait] = new KeyValuePair<int, int>(i, slot);
                }
                // CCU's legacy Faction_<Group>_Aligned traits are converted on load, but count them in case one is left.
                string legacy = "Faction_" + k[i] + "_Aligned";
                if (!numbered && Rck.IsRckTrait(legacy)) byTrait[legacy] = new KeyValuePair<int, int>(i, SlotAligned);
            }
            keys = k;
            vanillaMembers = members;
        }

        private static IEnumerable<string> AllGrades()
        {
            foreach (string grade in RckData.FactionGrades) yield return grade;
            yield return RckData.FactionMemberSuffix;
        }

        /// <summary>A faction name as written in a matrix: case and underscores don't matter, so <c>CommonFolk</c> works.</summary>
        private static string Token(string name) => name.Replace("_", "").ToLowerInvariant();

        /// <summary>The index of a faction key, from <c>3</c>, <c>Faction_3</c>, <c>Blahd</c> or <c>common_folk</c>; -1 if unknown.</summary>
        public static int KeyIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (int.TryParse(name, out int n)) name = "Faction_" + n;
            return byToken.TryGetValue(Token(name), out int i) ? i : -1;
        }

        private static int SlotOf(string grade)
        {
            if (grade == RckData.FactionMemberSuffix) return SlotMember;
            switch (grade)
            {
                case "Aligned": return SlotAligned;
                case "Hostile": return SlotHostile;
                case "Annoyed": return SlotAnnoyed;
                case "Friendly": return SlotFriendly;
                case "Neutral": return SlotNeutral;
                case "Territorial": return SlotTerritorial;
                default: return -1;
            }
        }

        /// <summary>
        ///   Sets each direction of the pair from the faction matrix, then any direction still unset from the strongest
        ///   faction trait rule. Directions set earlier in the same pair setup are kept (see SocialRules.SetDirected).
        /// </summary>
        public static void Apply(Agent a, Agent b)
        {
            if (keys.Length == 0) return;
            FactionMatrix matrix = FactionMatrix.Current();
            FactionProfile pa = ProfileOf(a), pb = ProfileOf(b);
            ulong rules = pa.Rules | pb.Rules;
            if (rules == 0 && matrix == null) return;
            ulong wanted = rules | (matrix == null ? 0UL : matrix.KeyMask);
            ulong ma = MembersOf(a, pa, wanted), mb = MembersOf(b, pb, wanted);
            // Street innocence holds back hostile results for the pair, both ways, until the innocent side is caught.
            bool innocent = StreetInnocence.IsInnocent(a) || StreetInnocence.IsInnocent(b);
            if (matrix != null)
            {
                bool playerA = a.isPlayer > 0, playerB = b.isPlayer > 0;
                Set(a, b, matrix.Resolve(ma, playerA, mb, playerB), innocent);
                Set(b, a, matrix.Resolve(mb, playerB, ma, playerA), innocent);
            }
            FactionGrade grade = FactionRules.Resolve(pa, ma, pb, mb);
            Set(a, b, grade, innocent);
            Set(b, a, grade, innocent);
        }

        /// <summary>
        ///   For recruiting: the factions <paramref name="npc"/> and <paramref name="player"/> both belong to (0 if none),
        ///   the NPC's own factions, and whether the NPC's factions are Friendly toward the player's (by the matrix,
        ///   else by faction traits).
        /// </summary>
        public static ulong RecruitStanding(Agent npc, Agent player, out ulong npcKeys, out bool friendly)
        {
            npcKeys = 0;
            friendly = false;
            if (keys.Length == 0) return 0;
            ulong all = keys.Length >= 64 ? ulong.MaxValue : (1UL << keys.Length) - 1;
            FactionProfile pn = ProfileOf(npc), pp = ProfileOf(player);
            npcKeys = MembersOf(npc, pn, all);
            if (npcKeys == 0) return 0;
            ulong playerKeys = MembersOf(player, pp, all);
            if (playerKeys == 0) return 0;
            ulong shared = npcKeys & playerKeys;
            if (shared != 0) return shared;
            FactionMatrix matrix = FactionMatrix.Current();
            FactionGrade grade = matrix == null ? FactionGrade.None : matrix.Resolve(npcKeys, npc.isPlayer > 0, playerKeys, player.isPlayer > 0);
            if (grade == FactionGrade.None) grade = FactionRules.Resolve(pn, npcKeys, pp, playerKeys);
            friendly = grade == FactionGrade.Friendly;
            return 0;
        }

        /// <summary>
        ///   Sets one direction unless an earlier rule already did; a Territorial result also registers the pair. For an
        ///   <paramref name="innocent"/> pair a hostile result only claims the direction, so the pair keeps its vanilla
        ///   relationship and weaker faction rules don't take over.
        /// </summary>
        private static void Set(Agent source, Agent target, FactionGrade grade, bool innocent)
        {
            if (grade == FactionGrade.None) return;
            if (innocent && FactionRules.IsHostile(grade))
            {
                SocialRules.Claim(source, target);
                return;
            }
            if (SocialRules.SetDirected(source, target, FactionRules.RelOf(grade), FactionRules.HateOf(grade), suppressible: true) && grade == FactionGrade.Territorial)
                Territorial.Register(source, target);
        }

        private static FactionProfile ProfileOf(Agent agent)
        {
            var p = new FactionProfile();
            if (!(AgentTraits.Get(agent) is HashSet<string> names) || names.Count == 0) return p;
            foreach (string name in names)
            {
                if (!byTrait.TryGetValue(name, out KeyValuePair<int, int> e)) continue;
                ulong bit = 1UL << e.Key;
                switch (e.Value)
                {
                    case SlotAligned: p.Aligned |= bit; break;
                    case SlotHostile: p.Hostile |= bit; break;
                    case SlotAnnoyed: p.Annoyed |= bit; break;
                    case SlotFriendly: p.Friendly |= bit; break;
                    case SlotNeutral: p.Neutral |= bit; break;
                    case SlotTerritorial: p.Territorial |= bit; break;
                    case SlotMember: p.Member |= bit; break;
                }
            }
            return p;
        }

        /// <summary>The factions <paramref name="agent"/> belongs to, checking vanilla membership only for <paramref name="wanted"/>.</summary>
        private static ulong MembersOf(Agent agent, FactionProfile p, ulong wanted)
        {
            ulong members = p.Aligned | p.Member;
            ulong check = wanted & ~members;
            for (int i = 0; check != 0 && i < vanillaMembers.Length; i++, check >>= 1)
            {
                if ((check & 1UL) == 0 || vanillaMembers[i] == null) continue;
                if (vanillaMembers[i](agent)) members |= 1UL << i;
            }
            return members;
        }

        private static bool IsCommonFolk(Agent a)
        {
            if (SocialRules.Has(a, "Common_Folk")) return true;
            if (a.isPlayer != 0 || a.inhuman || a.zombified || a.mechEmpty || a.electronic || a.enforcer || a.gang != 0) return false;
            string n = a.agentName;
            return n != "Cannibal" && n != "Gorilla" && n != "Scientist" && n != "Soldier" && n != "Vampire" && n != "WerewolfB" && n != "Slavemaster" && n != "Mafia";
        }
    }
}
