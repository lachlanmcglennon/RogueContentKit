#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using static RCK.AgentText;

namespace RCK.Social
{
    /// <summary>
    ///   "Innocent until caught": the <c>RCK_Innocent_Until_Caught</c> trait, and the <c>RCK_Street_Innocence</c> mutator
    ///   for every NPC that starts on the street. While an NPC is innocent, its hostile faction results (Hateful,
    ///   Territorial, Annoyed) and its <c>Guilty</c> trait are held back in both directions, so street gangs don't brawl
    ///   across the whole map. It is caught when another NPC sees it strike first: then the held-back rules are applied
    ///   to all its pairs, only ever making a relationship worse. Relationships vanilla sets, or that formed in play,
    ///   are left alone. Drug dealers (the <c>Drug_Dealer</c> trait or the vanilla DrugDealer) stay Guilty.
    /// </summary>
    internal static class StreetInnocence
    {
        public const string Trait = "RCK_Innocent_Until_Caught";
        public const string Mutator = "RCK_Street_Innocence";
        private const int MaxLogsPerLevel = 40;

        private sealed class State
        {
            public int Level = int.MinValue;
            public int AgentId = -1;
            public bool Known, Street, Caught;
            public readonly List<Agent> Victims = new List<Agent>();

            public void Reset(int level, int agentId)
            {
                Level = level;
                AgentId = agentId;
                Known = Street = Caught = false;
                Victims.Clear();
            }
        }

        private static readonly ConditionalWeakTable<Agent, State> states = new ConditionalWeakTable<Agent, State>();
        private static List<string> seenChallenges;
        private static int seenCount = -1;
        private static bool mutatorOn;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        /// <summary>True once any agent in this level has counted as innocent, so the attack hook costs one static read otherwise.</summary>
        public static bool Active;

        static StreetInnocence() => LevelScope.Ended += Reset;

        public static void Initialize()
        {
            if (!Rck.IsRckTrait(Trait)) Rck.Log.LogError($"Factions: street innocence trait {Trait} is not registered.");
            LevelMutators.Require(Mutator, "Factions: street innocence mutator");
        }

        private static void Reset()
        {
            Active = false;
            capped.Reset();
            seenChallenges = null;
        }

        private static void Refresh()
        {
            LevelScope.Check();
            List<string> challenges = GameController.gameController?.challenges;
            if (challenges != seenChallenges || (challenges != null && challenges.Count != seenCount))
            {
                seenChallenges = challenges;
                seenCount = challenges?.Count ?? -1;
                mutatorOn = challenges != null && challenges.Contains(Mutator);
            }
        }

        // Per-agent state is keyed to LevelScope.Id, which stays the same from a level's start to its end.
        private static State StateOf(Agent agent)
        {
            State s = states.GetOrCreateValue(agent);
            if (s.Level != LevelScope.Id || s.AgentId != agent.agentID) s.Reset(LevelScope.Id, agent.agentID);
            return s;
        }

        /// <summary>Makes <paramref name="agent"/> guilty for the rest of the level (a raider or a squad sent to a fight).</summary>
        public static void MarkCaught(Agent agent)
        {
            if (agent == null) return;
            Refresh();
            StateOf(agent).Caught = true;
        }

        /// <summary>True while <paramref name="agent"/>'s hostile faction rules and Guilty trait are held back.</summary>
        public static bool IsInnocent(Agent agent)
        {
            if (agent == null || agent.isPlayer != 0 || agent.objectAgent) return false;
            Refresh();
            bool holder = SocialRules.Has(agent, Trait);
            if (!holder && !mutatorOn) return false;
            State s = StateOf(agent);
            if (s.Caught) return false;
            if (!holder)
            {
                if (!s.Known)
                {
                    s.Street = StreetSide(agent);
                    s.Known = true;
                }
                if (!s.Street) return false;
            }
            Active = true;
            return true;
        }

        /// <summary>Drug dealers are always Guilty, innocent or not.</summary>
        public static bool AlwaysGuilty(Agent agent)
            => agent != null && (SocialRules.Has(agent, "Drug_Dealer") || agent.agentName == "DrugDealer");

        /// <summary>
        ///   Street-side for the mutator: a wandering default goal (WanderFar, Random Patrol (Map) or a Random Teleport goal), or no owner
        ///   and a start on a tile nobody owns.
        /// </summary>
        private static bool StreetSide(Agent agent)
        {
            string goal = agent.defaultGoal;
            if (goal == "WanderFar" || goal == "Random Patrol (Map)" || (goal != null && goal.StartsWith("Random Teleport", StringComparison.Ordinal))) return true;
            if (agent.ownerID != 0) return false;
            GameController gc = GameController.gameController;
            if (gc == null || gc.tileInfo == null) return false;
            Vector2 pos = agent.startingPosition;
            if (pos == Vector2.zero) pos = agent.transform.position;
            TileData tile = gc.tileInfo.GetTileData(pos);
            return tile == null || tile.owner == 0;
        }

        /// <summary>Vanilla reports an attack on <paramref name="victim"/> by <paramref name="criminal"/> (EnforcerAlertAttack).</summary>
        internal static void OnAttack(Agent criminal, Agent victim)
        {
            if (criminal == null || victim == null || criminal == victim) return;
            // The attacks vanilla's own EnforcerAlertAttack ignores: finished bodies, zombies, arena victims and duels.
            if ((victim.dead && !victim.justDied2 && !criminal.hasBitingAgent) || victim.zombified || victim.noEnforcerAlert) return;
            if ((criminal.UID == victim.challengedToFightAgentID && victim.challengedToFight == 2)
                || (victim.UID == criminal.challengedToFightAgentID && criminal.challengedToFight == 2)) return;
            if (!IsInnocent(criminal)) return;
            State s = StateOf(criminal);
            if (!s.Victims.Contains(victim))
            {
                // Hitting back at someone who struck first, or who is already fighting it, is self-defence.
                if (criminal.lastHitByAgent == victim || victim.opponent == criminal) return;
                s.Victims.Add(victim);
            }
            Agent witness = FindWitness(criminal, victim);
            if (witness != null) Catch(criminal, s, "attacked " + Describe(victim), witness);
        }

        /// <summary>The first NPC other than the victim that can see <paramref name="criminal"/> now, with vanilla's sight test.</summary>
        private static Agent FindWitness(Agent criminal, Agent victim)
        {
            if (criminal.invisible || criminal.ghost) return null;
            GameController gc = GameController.gameController;
            if (gc == null || gc.agentList == null) return null;
            float hard = criminal.hardToSeeFromDistance > 0f ? criminal.hardToSeeFromDistance : 1f;
            Vector2 pos = criminal.curPosition;
            List<Agent> agents = gc.agentList;
            for (int i = 0; i < agents.Count; i++)
            {
                Agent w = agents[i];
                if (w == null || w == criminal || w == victim || w.isPlayer != 0 || w.dead || w.objectAgent || w.mechEmpty || w.zombified || w.ghost) continue;
                if (w.relationships == null || w.movement == null || w.sleeping) continue;
                if (Vector2.Distance(w.curPosition, pos) >= w.LOSRange / hard) continue;
                relStatus rel = w.relationships.GetRelCode(criminal);
                if (rel == relStatus.Aligned || rel == relStatus.Loyal) continue;
                if (w.movement.InVisionBoundsAgent(criminal) && w.movement.HasLOSAgent(criminal)) return w;
            }
            return null;
        }

        private static void Catch(Agent agent, State s, string how, Agent witness)
        {
            s.Caught = true;
            capped.Info($"Street innocence: {Describe(agent)} was caught ({how}, seen by {Describe(witness)}).");
            GameController gc = GameController.gameController;
            if (gc == null || gc.agentList == null) return;
            // Applying rules runs vanilla relationship code, so the loop is over a copy. The catch line above explains
            // the new hostility, so the hostility diagnostics stay quiet.
            using (RelOps.Quietly())
            {
                foreach (Agent other in new List<Agent>(gc.agentList))
                {
                    if (other == null || other == agent || other.dead || other.objectAgent || other.relationships == null) continue;
                    try { SocialRules.ApplyCaught(agent, other); }
                    catch (Exception e) { SocialRules.LogOnce(agent, "innocence-caught", e); }
                }
            }
        }

        internal static void Forget(Agent agent)
        {
            if (agent != null) states.Remove(agent);
        }
    }

    // FindDamage, bullets, melee and biting all report attacks here. Only the 4-argument overload is patched; the
    // 3-argument one forwards to it.
    [HarmonyPatch(typeof(GameController), nameof(GameController.EnforcerAlertAttack), typeof(Agent), typeof(Agent), typeof(float), typeof(Vector2))]
    internal static class GameController_EnforcerAlertAttack_Innocence_Patch
    {
        private static void Prefix(Agent criminal, Agent victim)
        {
            if (!StreetInnocence.Active) return;
            try { StreetInnocence.OnAttack(criminal, victim); }
            catch (Exception e) { SocialRules.LogOnce(criminal, "innocence-attack", e); }
        }
    }

    // A pooled agent comes back with a new agentID that a restarted level may have used before, so its state goes.
    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleAwake))]
    internal static class Agent_RecycleAwake_Innocence_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { StreetInnocence.Forget(__instance); }
            catch (Exception e) { SocialRules.LogOnce(__instance, "innocence-recycle", e); }
        }
    }
}
