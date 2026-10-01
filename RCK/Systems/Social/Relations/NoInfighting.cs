#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The <c>RCK_No_Infighting</c> mutator: every NPC gets the vanilla trait <c>DontHitAligned</c> ("No In-Fighting")
    ///   as it's set up, placed or spawned later (squads, alarm cops, summons). Vanilla's DontHitAlignedCheck needs it on
    ///   only one side of a hit, so aligned NPCs stop hurting each other in crossfire; a player's followers are NPCs,
    ///   so they're covered. Players don't get it: on them it only adds co-op immunity, and a trait added
    ///   to a player in play shows on the HUD and carries over to the next level. A sweep every 2 s catches NPCs whose
    ///   trait list was reset (possession, recycling). Runs on every peer, as NPC traits aren't networked.
    /// </summary>
    internal static class NoInfighting
    {
        public const string Mutator = "RCK_No_Infighting";
        public const string Trait = "DontHitAligned";
        private const float SweepSeconds = 2f;

        private static List<string> seenChallenges;
        private static int seenCount = -1;
        private static bool mutatorOn;
        private static int given;
        private static bool logged;
        private static float nextSweep;

        static NoInfighting() => LevelScope.Ended += Reset;

        private static void Reset()
        {
            given = 0;
            logged = false;
            seenChallenges = null;
        }

        public static void Initialize()
        {
            LevelMutators.Require(Mutator, "Factions: no in-fighting mutator");
        }

        private static bool On(GameController gc)
        {
            if (gc == null) return false;
            LevelScope.Check(gc);
            List<string> challenges = gc.challenges;
            if (challenges != seenChallenges || (challenges != null && challenges.Count != seenCount))
            {
                seenChallenges = challenges;
                seenCount = challenges?.Count ?? -1;
                mutatorOn = challenges != null && challenges.Contains(Mutator);
            }
            return mutatorOn;
        }

        /// <summary>Gives <paramref name="agent"/> the trait if it's an NPC that lacks it.</summary>
        internal static void Give(Agent agent)
        {
            if (agent == null || agent.objectAgent || agent.isDummy || agent.statusEffects == null || IsPlayer(agent)) return;
            if (agent.statusEffects.TraitList == null || !On(agent.gc ?? GameController.gameController)) return;
            if (AgentTraits.Has(agent, Trait)) return;
            agent.statusEffects.AddTrait(Trait);
            AgentTraits.Invalidate(agent);
            given++;
        }

        /// <summary>
        ///   A player, or a client's copy of another player that the game hasn't flagged yet (its isPlayer is set by a
        ///   coroutine after setup, Agent.cs:3600).
        /// </summary>
        private static bool IsPlayer(Agent agent)
        {
            if (agent.isPlayer != 0 || agent.localPlayer) return true;
            string name = agent.name;
            if (name != null && (name.StartsWith("Playerr", StringComparison.Ordinal) || name.Contains("AgentPlayer"))) return true;
            string id = agent.playerUniqueID;
            if (!string.IsNullOrEmpty(id) && id.Contains("Playerr")) return true;
            List<Agent> players = agent.gc?.playerAgentList;
            return players != null && players.Contains(agent);
        }

        /// <summary>Every 2 s: NPCs that lost the trait get it back, and the level's count is logged once it has loaded.</summary>
        internal static void Sweep()
        {
            float now = Time.time;
            if (!Interval.Due(ref nextSweep, now, SweepSeconds)) return;
            GameController gc = GameController.gameController;
            if (!On(gc) || !gc.loadComplete || gc.agentList == null) return;
            foreach (Agent a in gc.agentList.ToArray())
            {
                try { Give(a); }
                catch (Exception e) { SocialRules.LogOnce(a, "no-infighting", e); }
            }
            if (logged) return;
            logged = true;
            Rck.Log.LogInfo($"Factions: no in-fighting gave {Trait} to {given} NPC(s) this level.");
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetupAgentStats), typeof(string))]
    [HarmonyPriority(Priority.Last)]
    internal static class Agent_SetupAgentStats_NoInfighting_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { NoInfighting.Give(__instance); }
            catch (Exception e) { SocialRules.LogOnce(__instance, "no-infighting", e); }
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_NoInfighting_Patch
    {
        private static bool broken;

        private static void Postfix()
        {
            if (broken) return;
            try { NoInfighting.Sweep(); }
            catch (Exception e)
            {
                broken = true;
                Rck.Log.LogError($"Factions: no in-fighting sweep failed, it stops until restart: {e}");
            }
        }
    }
}
