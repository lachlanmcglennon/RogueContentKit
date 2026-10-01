#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{
    /// <summary>The faction services RCK.Social offers the other modules through <see cref="RckWorld.Factions"/>.</summary>
    internal sealed class SocialWorld : IRckFactions
    {
        public string Resolve(string name)
        {
            int i = Factions.KeyIndex(name);
            return i >= 0 ? Factions.Keys[i] : null;
        }

        public string Display(string key) => FactionDisplay.Short(key);

        public string Plural(string key) => FactionDisplay.Plural(key);

        private static ulong Bit(string key)
        {
            int i = Factions.KeyIndex(key);
            return i >= 0 ? 1UL << i : 0;
        }

        public bool IsMember(Agent agent, string key)
        {
            ulong bit = Bit(key);
            return bit != 0 && agent != null && (Factions.MemberKeys(agent, bit) != 0 || (Disguises.MaskOf(agent) & bit) != 0);
        }

        public bool IsLeader(Agent agent, string key)
        {
            ulong bit = Bit(key);
            if (bit == 0 || agent == null) return false;
            Factions.RolesOf(agent, out _, out ulong leader);
            return (leader & bit) != 0;
        }

        public bool IsRouted(string key)
        {
            ulong bit = Bit(key);
            return bit != 0 && (FactionEvents.Routed & bit) != 0;
        }

        public IList<string> FactionsOf(Agent agent) => Names(Factions.KeysOf(agent));

        private static IList<string> Names(ulong mask)
        {
            var names = new List<string>();
            IReadOnlyList<string> keys = Factions.Keys;
            for (int i = 0; i < keys.Count && mask != 0; i++, mask >>= 1)
                if ((mask & 1UL) != 0) names.Add(keys[i]);
            return names;
        }

        public IList<Agent> MembersOf(string key)
        {
            var members = new List<Agent>();
            ulong bit = Bit(key);
            List<Agent> agents = GameController.gameController != null ? GameController.gameController.agentList : null;
            if (bit == 0 || agents == null) return members;
            foreach (Agent a in agents)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.ghost) continue;
                if (a.employer != null && a.employer.isPlayer > 0) continue;
                if (Factions.MemberKeys(a, bit) != 0) members.Add(a);
            }
            return members;
        }

        /// <summary>
        ///   Scores every faction with living members against <paramref name="key"/>: a Hateful rule counts 3, Territorial
        ///   2 and Annoyed 1, per member holding the trait either way, and a matrix entry counts ten times as much.
        /// </summary>
        public IList<string> RivalsOf(string key)
        {
            int k = Factions.KeyIndex(key);
            List<Agent> agents = GameController.gameController != null ? GameController.gameController.agentList : null;
            if (k < 0 || agents == null) return new List<string>();
            ulong bit = 1UL << k;
            int count = Factions.Keys.Count;
            var score = new int[count];
            ulong present = 0;
            foreach (Agent a in agents)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.ghost) continue;
                ulong own = Factions.KeysOf(a);
                present |= own;
                Factions.GradesOf(a, out ulong hostile, out ulong territorial, out ulong annoyed);
                if ((own & bit) != 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        ulong b = 1UL << i;
                        score[i] += (hostile & b) != 0 ? 3 : (territorial & b) != 0 ? 2 : (annoyed & b) != 0 ? 1 : 0;
                    }
                }
                else if (((hostile | territorial | annoyed) & bit) != 0)
                {
                    int w = (hostile & bit) != 0 ? 3 : (territorial & bit) != 0 ? 2 : 1;
                    for (int i = 0; i < count; i++)
                        if ((own & (1UL << i)) != 0) score[i] += w;
                }
            }
            FactionMatrix matrix = FactionMatrix.Current();
            var rivals = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < count; i++)
            {
                if (i == k || (present & (1UL << i)) == 0) continue;
                if (matrix != null)
                {
                    score[i] += 10 * Weight(matrix.Resolve(bit, false, 1UL << i, false));
                    score[i] += 10 * Weight(matrix.Resolve(1UL << i, false, bit, false));
                }
                if (score[i] > 0) rivals.Add(new KeyValuePair<string, int>(Factions.Keys[i], score[i]));
            }
            rivals.Sort((x, y) => y.Value.CompareTo(x.Value));
            var result = new List<string>(rivals.Count);
            foreach (KeyValuePair<string, int> r in rivals) result.Add(r.Key);
            return result;
        }

        private static int Weight(FactionGrade g) => g == FactionGrade.Hateful ? 3 : g == FactionGrade.Territorial ? 2 : g == FactionGrade.Annoyed ? 1 : 0;

        public int Befriend(Agent player, string key)
        {
            int i = Factions.KeyIndex(key);
            return i < 0 ? 0 : LevelRelations.SetTowardPlayers(i, "Friendly");
        }

        public int SetLevelRelation(string a, string b, string rel, bool bothWays)
            => LevelRelations.Set(Factions.KeyIndex(a), Factions.KeyIndex(b), rel, bothWays);
    }
}
