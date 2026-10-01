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
    ///   mutator sets directed relationships between factions and beats the traits. The role traits
    ///   <c>&lt;key&gt;_Vengeful</c>, <c>&lt;key&gt;_Leader</c>, <c>&lt;key&gt;_Calls_Backup</c> and <c>&lt;key&gt;_Reinforcement</c>
    ///   set nothing here; they act on faction events (see <see cref="FactionEvents"/>, <see cref="FactionBackup"/> and
    ///   <see cref="SquadUnits"/>).
    /// </summary>
    internal static class Factions
    {
        private const int SlotAligned = 0, SlotHostile = 1, SlotAnnoyed = 2, SlotFriendly = 3, SlotNeutral = 4, SlotMember = 5, SlotTerritorial = 6,
            SlotVengeful = 7, SlotLeader = 8, SlotBackup = 9, SlotReinforcement = 10;

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
        private static int commonFolk = -1;

        public static IReadOnlyList<string> Keys => keys;

        /// <summary>The <c>Common_Folk</c> key's bit, 0 if it has none.</summary>
        public static ulong CommonFolkBit => commonFolk >= 0 ? 1UL << commonFolk : 0;

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
            commonFolk = Array.IndexOf(k, "Common_Folk");
        }

        private static IEnumerable<string> AllGrades()
        {
            foreach (string grade in RckData.FactionGrades) yield return grade;
            yield return RckData.FactionMemberSuffix;
            foreach (string role in RckData.FactionRoles) yield return role;
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
                case "Vengeful": return SlotVengeful;
                case "Leader": return SlotLeader;
                case "Calls_Backup": return SlotBackup;
                case "Reinforcement": return SlotReinforcement;
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
            ulong shared = Standing(npc, player, out npcKeys, out FactionGrade grade);
            friendly = shared == 0 && grade == FactionGrade.Friendly;
            return shared;
        }

        /// <summary>
        ///   For private areas: true when <paramref name="visitor"/> joined factions through faction traits
        ///   (<c>&lt;key&gt;_Member</c> or <c>&lt;key&gt;_Aligned</c>) and <paramref name="owner"/> (by traits or
        ///   vanilla membership) shares one of them, or the owner's factions are Friendly or Aligned toward the visitor's
        ///   (by the matrix, else by faction traits). Vanilla membership alone doesn't count for the visitor, so a
        ///   vanilla Scientist player isn't welcome in every lab and ordinary runs keep vanilla trespassing.
        /// </summary>
        public static bool WelcomesInPrivate(Agent owner, Agent visitor)
        {
            ulong shared = Standing(owner, visitor, out ulong ownerKeys, out FactionGrade grade, otherByTraitsOnly: true);
            return shared != 0 || (ownerKeys != 0 && (grade == FactionGrade.Friendly || grade == FactionGrade.Aligned));
        }

        /// <summary>
        ///   The factions <paramref name="npc"/> and <paramref name="other"/> share (0 if none), the NPC's own factions,
        ///   and, when they share none but both belong to factions, the grade the NPC's factions give the other's. With
        ///   <paramref name="otherByTraitsOnly"/>, only the other's faction traits make it a member.
        /// </summary>
        private static ulong Standing(Agent npc, Agent other, out ulong npcKeys, out FactionGrade grade, bool otherByTraitsOnly = false)
        {
            npcKeys = 0;
            grade = FactionGrade.None;
            if (keys.Length == 0) return 0;
            ulong all = keys.Length >= 64 ? ulong.MaxValue : (1UL << keys.Length) - 1;
            FactionProfile po = ProfileOf(other);
            // A player's disguise passes for a member of its faction (see Disguises).
            ulong disguise = Disguises.MaskOf(other);
            // Checked first: in ordinary runs players hold no faction traits, so this skips the vanilla member tests.
            if (otherByTraitsOnly && (((po.Aligned | po.Member) & all) | disguise) == 0) return 0;
            FactionProfile pn = ProfileOf(npc);
            npcKeys = MembersOf(npc, pn, all);
            if (npcKeys == 0) return 0;
            ulong otherKeys = (otherByTraitsOnly ? (po.Aligned | po.Member) & all : MembersOf(other, po, all)) | disguise;
            if (otherKeys == 0) return 0;
            ulong shared = npcKeys & otherKeys;
            if (shared != 0) return shared;
            FactionMatrix matrix = FactionMatrix.Current();
            grade = matrix == null ? FactionGrade.None : matrix.Resolve(npcKeys, npc.isPlayer > 0, otherKeys, other.isPlayer > 0);
            if (grade == FactionGrade.None) grade = FactionRules.Resolve(pn, npcKeys, po, otherKeys);
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

        // Agents sworn to one faction for the level (see Swear).
        private static readonly Dictionary<Agent, int> sworn = new Dictionary<Agent, int>();

        static Factions() => LevelScope.ResetAtBoth(ClearSworn);

        private static void ClearSworn() => sworn.Clear();

        /// <summary>
        ///   <paramref name="agent"/> belongs to faction <paramref name="key"/> alone for the rest of the level: a unit of
        ///   another faction recruited by a commander (see <see cref="Command"/>). Its own faction traits and vanilla
        ///   membership stop counting, as do its role traits and its rules toward <paramref name="key"/>; its rules
        ///   toward other factions stay. Server only; set it before the agent's relationships are set up.
        /// </summary>
        internal static void Swear(Agent agent, int key)
        {
            if (agent == null || key < 0 || key >= keys.Length) return;
            LevelScope.Check();
            sworn[agent] = key;
        }

        internal static void Unswear(Agent agent)
        {
            if (agent != null && sworn.Count != 0) sworn.Remove(agent);
        }

        /// <summary>The faction <paramref name="agent"/> is sworn to, or -1.</summary>
        internal static int SwornKey(Agent agent)
            => agent != null && sworn.Count != 0 && sworn.TryGetValue(agent, out int key) ? key : -1;

        /// <summary>
        ///   Before the rules decide a pair: a sworn agent drops the vanilla Aligned tie its type gave it with anyone
        ///   outside its sworn faction (a recruited Cop with the other cops), both ways, so the rules and its squad's
        ///   orders decide the pair. Party-mates are never passed here.
        /// </summary>
        internal static void ResetSwornTies(Agent a, Agent b)
        {
            if (sworn.Count == 0 || keys.Length == 0) return;
            Reset(a, b);
            Reset(b, a);
        }

        private static void Reset(Agent one, Agent other)
        {
            int key = SwornKey(one);
            if (key < 0 || MemberKeys(other, 1UL << key) != 0) return;
            Unalign(one, other);
            Unalign(other, one);
        }

        private static void Unalign(Agent source, Agent target)
        {
            if (source.relationships == null || source.relationships.GetRelCode(target) != relStatus.Aligned) return;
            source.relationships.SetRelInitial(target, "Neutral");
            source.relationships.SetRelHate(target, 0);
        }

        private static FactionProfile ProfileOf(Agent agent)
        {
            FactionProfile p = TraitProfileOf(agent);
            int oath = SwornKey(agent);
            if (oath < 0) return p;
            ulong bit = 1UL << oath;
            p.Aligned = bit;
            p.Member = 0;
            p.Hostile &= ~bit;
            p.Territorial &= ~bit;
            p.Annoyed &= ~bit;
            p.Friendly &= ~bit;
            p.Neutral &= ~bit;
            p.Vengeful = p.Leader = p.Backup = p.Reinforcement = 0;
            p.Sworn = true;
            return p;
        }

        private static FactionProfile TraitProfileOf(Agent agent)
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
                    case SlotVengeful: p.Vengeful |= bit; break;
                    case SlotLeader: p.Leader |= bit; break;
                    case SlotBackup: p.Backup |= bit; break;
                    case SlotReinforcement: p.Reinforcement |= bit; break;
                }
            }
            return p;
        }

        /// <summary>The factions <paramref name="agent"/> belongs to, checking vanilla membership only for <paramref name="wanted"/>.</summary>
        private static ulong MembersOf(Agent agent, FactionProfile p, ulong wanted)
        {
            ulong members = p.Aligned | p.Member;
            if (p.Sworn) return members;
            ulong check = wanted & ~members;
            for (int i = 0; check != 0 && i < vanillaMembers.Length; i++, check >>= 1)
            {
                if ((check & 1UL) == 0 || vanillaMembers[i] == null) continue;
                if (vanillaMembers[i](agent)) members |= 1UL << i;
            }
            return members;
        }

        private static ulong AllKeys => keys.Length >= 64 ? ulong.MaxValue : (1UL << keys.Length) - 1;

        /// <summary>Every faction key's bit.</summary>
        public static ulong All => AllKeys;

        /// <summary>
        ///   The factions <paramref name="agent"/> belongs to, by traits or vanilla membership, with <c>Common_Folk</c>
        ///   only through its traits (every plain citizen is common folk).
        /// </summary>
        public static ulong KeysOf(Agent agent)
        {
            if (agent == null || keys.Length == 0) return 0;
            FactionProfile p = ProfileOf(agent);
            ulong wanted = AllKeys;
            if (commonFolk >= 0) wanted &= ~(1UL << commonFolk);
            return MembersOf(agent, p, wanted) | ((p.Aligned | p.Member) & AllKeys);
        }

        /// <summary>The factions of the living players together.</summary>
        public static ulong PlayerKeys(GameController gc)
        {
            ulong players = 0;
            if (gc != null && gc.playerAgentList != null)
                foreach (Agent p in gc.playerAgentList)
                    if (p != null && !p.dead) players |= KeysOf(p);
            return players;
        }

        /// <summary>The factions <paramref name="agent"/>'s faction traits make it Hostile, Territorial or Annoyed toward.</summary>
        public static void GradesOf(Agent agent, out ulong hostile, out ulong territorial, out ulong annoyed)
        {
            FactionProfile p = agent == null || keys.Length == 0 ? new FactionProfile() : ProfileOf(agent);
            hostile = p.Hostile;
            territorial = p.Territorial;
            annoyed = p.Annoyed;
        }

        /// <summary>Which of the factions in <paramref name="wanted"/> <paramref name="agent"/> belongs to (by traits or vanilla membership).</summary>
        public static ulong MemberKeys(Agent agent, ulong wanted)
            => agent == null || wanted == 0 || keys.Length == 0 ? 0 : MembersOf(agent, ProfileOf(agent), wanted) & wanted;

        /// <summary>The factions of <paramref name="agent"/>'s <c>&lt;key&gt;_Vengeful</c> and <c>&lt;key&gt;_Leader</c> traits.</summary>
        public static void RolesOf(Agent agent, out ulong vengeful, out ulong leader)
        {
            FactionProfile p = agent == null || keys.Length == 0 ? new FactionProfile() : ProfileOf(agent);
            vengeful = p.Vengeful;
            leader = p.Leader;
        }

        /// <summary>The factions of <paramref name="agent"/>'s <c>&lt;key&gt;_Calls_Backup</c> traits.</summary>
        public static ulong BackupOf(Agent agent) => agent == null || keys.Length == 0 ? 0 : ProfileOf(agent).Backup;

        /// <summary>The factions of <paramref name="agent"/>'s <c>&lt;key&gt;_Reinforcement</c> traits (see <see cref="SquadUnits"/>).</summary>
        public static ulong ReinforcementOf(Agent agent) => agent == null || keys.Length == 0 ? 0 : ProfileOf(agent).Reinforcement;

        /// <summary>The factions <paramref name="agent"/> joined through <c>&lt;key&gt;_Aligned</c> or <c>&lt;key&gt;_Member</c>.</summary>
        public static ulong TraitKeys(Agent agent)
        {
            if (agent == null || keys.Length == 0) return 0;
            FactionProfile p = ProfileOf(agent);
            return (p.Aligned | p.Member) & AllKeys;
        }

        /// <summary>
        ///   One faction of <paramref name="agent"/> in <paramref name="among"/>: the lowest one it joined through
        ///   traits, else the lowest by vanilla membership; -1 if none.
        /// </summary>
        public static int PrimaryKey(Agent agent, ulong among)
        {
            if (among == 0) return -1;
            ulong own = KeysOf(agent) & among;
            if (own == 0) return -1;
            ulong traits = TraitKeys(agent) & own;
            return LowestBit(traits != 0 ? traits : own);
        }

        public static int LowestBit(ulong mask)
        {
            if (mask == 0) return -1;
            int i = 0;
            while ((mask & 1UL) == 0) { mask >>= 1; i++; }
            return i;
        }

        /// <summary>
        ///   The factions a defector leaves when it joins <paramref name="employer"/>'s party: its own (<c>&lt;key&gt;_Aligned</c>
        ///   traits and vanilla membership, with <c>Common_Folk</c> only through <c>Common_Folk_Aligned</c>) that the
        ///   employer doesn't belong to.
        /// </summary>
        public static ulong DefectorKeys(Agent defector, Agent employer)
        {
            if (defector == null || keys.Length == 0) return 0;
            FactionProfile p = ProfileOf(defector);
            ulong wanted = AllKeys;
            if (commonFolk >= 0) wanted &= ~(1UL << commonFolk);
            ulong own = MembersOf(defector, p, wanted);
            return own == 0 ? 0 : own & ~MemberKeys(employer, own);
        }

        /// <summary>The faction keys in <paramref name="mask"/>, for log lines.</summary>
        public static string Describe(ulong mask)
        {
            var names = new List<string>();
            for (int i = 0; i < keys.Length && mask != 0; i++, mask >>= 1)
                if ((mask & 1UL) != 0) names.Add(keys[i]);
            return string.Join(", ", names.ToArray());
        }

        /// <summary>Living NPC members per faction in <paramref name="wanted"/>, not counting the dead, zombies, ghosts and players' hires.</summary>
        internal static int[] CountLiving(GameController gc, ulong wanted)
        {
            var counts = new int[keys.Length];
            if (gc == null || gc.agentList == null || wanted == 0) return counts;
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost || a.hologram || a.disappeared) continue;
                if (a.employer != null && a.employer.isPlayer > 0) continue;
                ulong k = KeysOf(a) & wanted;
                for (int i = 0; k != 0 && i < counts.Length; i++, k >>= 1)
                    if ((k & 1UL) != 0) counts[i]++;
            }
            return counts;
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
