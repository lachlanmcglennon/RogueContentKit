#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Faction relationships changed in play for the rest of the level (a brokered truce, a frame job, a quest's
    ///   standing reward). A rule applies at once to every living pair it covers and to pairs set up later (agents that
    ///   spawn), after the faction traits and matrix but before Vengeful grudges and routs. Party-mates, Relationless
    ///   agents and ties stronger than Friendly (Aligned, Loyal, Submissive) are left alone, and a hostile rule holds
    ///   back from street innocents until they are caught, like the faction rules. Only the server acts.
    /// </summary>
    internal static class LevelRelations
    {
        private sealed class Rule
        {
            public ulong Source;
            public ulong Target;
            /// <summary>The target is the players' side (players and their followers) rather than a faction.</summary>
            public bool Players;
            public string Rel;
        }

        private static readonly List<Rule> rules = new List<Rule>();

        static LevelRelations() => LevelScope.ResetAtBoth(ClearRules);

        private static void ClearRules() => rules.Clear();

        private static GameController Server()
        {
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer) return null;
            LevelScope.Check(gc);
            return gc;
        }

        public static bool IsRel(string rel) => rel == "Hateful" || rel == "Annoyed" || rel == "Neutral" || rel == "Friendly";

        /// <summary>
        ///   <paramref name="a"/>'s members feel <paramref name="rel"/> toward <paramref name="b"/>'s (and back when
        ///   <paramref name="bothWays"/>) for the rest of the level. Returns how many directed pairs changed now.
        /// </summary>
        public static int Set(int a, int b, string rel, bool bothWays)
        {
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || a < 0 || b < 0 || a == b || !IsRel(rel)) return 0;
            int changed = Add(new Rule { Source = 1UL << a, Target = 1UL << b, Rel = rel }, gc);
            if (bothWays) changed += Add(new Rule { Source = 1UL << b, Target = 1UL << a, Rel = rel }, gc);
            Rck.Log.LogInfo($"Factions: {Factions.Keys[a]} {(bothWays ? "and" : "→")} {Factions.Keys[b]} are {rel} for the rest of the level; {changed} pair(s) changed.");
            return changed;
        }

        /// <summary>Living members of <paramref name="key"/> outside a party feel <paramref name="rel"/> toward the players' side for the level.</summary>
        public static int SetTowardPlayers(int key, string rel)
        {
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || key < 0 || !IsRel(rel)) return 0;
            int changed = Add(new Rule { Source = 1UL << key, Players = true, Rel = rel }, gc);
            Rck.Log.LogInfo($"Factions: {Factions.Keys[key]} are {rel} toward the players for the rest of the level; {changed} pair(s) changed.");
            return changed;
        }

        private static int Add(Rule rule, GameController gc)
        {
            rules.RemoveAll(r => r.Source == rule.Source && r.Target == rule.Target && r.Players == rule.Players);
            rules.Add(rule);
            List<Agent> agents = gc.agentList;
            if (agents == null) return 0;
            var sources = new List<Agent>();
            var targets = new List<Agent>();
            foreach (Agent a in agents)
            {
                if (a == null || a.dead || a.objectAgent || a.relationships == null) continue;
                if (a.isPlayer == 0 && Factions.MemberKeys(a, rule.Source) != 0) sources.Add(a);
                if (rule.Players ? IsPlayerSide(a) : Factions.MemberKeys(a, rule.Target) != 0) targets.Add(a);
            }
            if (rule.Players && gc.playerAgentList != null)
                foreach (Agent p in gc.playerAgentList)
                    if (p != null && !p.dead && p.relationships != null && !targets.Contains(p)) targets.Add(p);
            int changed = 0;
            using (RelOps.Quietly(disguises: true))
            {
                foreach (Agent s in sources)
                    foreach (Agent t in targets)
                    {
                        try { if (Apply(s, t, rule.Rel)) changed++; }
                        catch (Exception e) { SocialRules.LogOnce(s, "level-relation", e); }
                    }
            }
            return changed;
        }

        /// <summary>A pair was just decided by the rules (not party-mates, not Relationless): this level's rules win.</summary>
        internal static void AfterPairSetup(Agent a, Agent b)
        {
            if (rules.Count == 0) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || rules.Count == 0) return;
            Direction(a, b);
            Direction(b, a);
        }

        private static void Direction(Agent s, Agent t)
        {
            if (s.isPlayer != 0 || s.dead || t.dead) return;
            string rel = null;
            foreach (Rule r in rules)
            {
                if (Factions.MemberKeys(s, r.Source) == 0) continue;
                if (r.Players ? IsPlayerSide(t) : Factions.MemberKeys(t, r.Target) != 0) rel = r.Rel;
            }
            if (rel != null) Apply(s, t, rel);
        }

        private static bool Apply(Agent s, Agent t, string want)
        {
            if (s == t || s.relationships == null || t.objectAgent) return false;
            if (SocialRules.Has(s, "Relationless") || SocialRules.Has(t, "Relationless") || PartyPeace.ArePartyMates(s, t)) return false;
            Relationship rel = RelOps.Of(s, t);
            if (rel == null) return false;
            // Aligned, Loyal and Submissive ties are stronger than anything a rule here sets.
            if (SocialRules.EscalationRank(rel.relTypeCode) == 0) return false;
            bool hostile = want == "Hateful" || want == "Annoyed";
            if (hostile)
            {
                if (StreetInnocence.IsInnocent(s) || StreetInnocence.IsInnocent(t)) return false;
                if (rel.relType == want) return false;
                s.relationships.SetRel(t, want);
                if (want == "Hateful") rel.relHate = Mathf.Max(rel.relHate, 5f);
                return true;
            }
            Territorial.UnregisterDirected(s, t);
            bool changed = rel.relType != want || rel.relHate > 0f || rel.relStrikes > 0;
            rel.relHate = 0f;
            rel.relStrikes = 0;
            if (rel.relType != want) s.relationships.SetRel(t, want);
            return changed;
        }

        /// <summary>True if this level's rules make <paramref name="key"/>'s members Neutral or Friendly toward <paramref name="target"/>.</summary>
        internal static bool Peace(int key, Agent target)
        {
            if (rules.Count == 0 || key < 0 || target == null || Server() == null) return false;
            ulong source = 1UL << key;
            string rel = null;
            foreach (Rule r in rules)
            {
                if ((r.Source & source) == 0) continue;
                if (r.Players ? IsPlayerSide(target) : Factions.MemberKeys(target, r.Target) != 0) rel = r.Rel;
            }
            return rel == "Neutral" || rel == "Friendly";
        }

        /// <summary>True if this level's rules make faction <paramref name="a"/> Neutral or Friendly toward faction <paramref name="b"/>.</summary>
        internal static bool Peace(int a, int b)
        {
            if (rules.Count == 0 || a < 0 || b < 0 || Server() == null) return false;
            string rel = null;
            foreach (Rule r in rules)
                if (!r.Players && (r.Source & (1UL << a)) != 0 && (r.Target & (1UL << b)) != 0) rel = r.Rel;
            return rel == "Neutral" || rel == "Friendly";
        }

        /// <summary>True if this level's rules make faction <paramref name="a"/> Hateful toward faction <paramref name="b"/> (a declared war).</summary>
        internal static bool War(int a, int b)
        {
            if (rules.Count == 0 || a < 0 || b < 0 || Server() == null) return false;
            string rel = null;
            foreach (Rule r in rules)
                if (!r.Players && (r.Source & (1UL << a)) != 0 && (r.Target & (1UL << b)) != 0) rel = r.Rel;
            return rel == "Hateful";
        }

        private static bool IsPlayerSide(Agent a)
            => a != null && !a.dead && !a.objectAgent && (a.isPlayer > 0 || (a.employer != null && a.employer.isPlayer > 0));
    }
}
