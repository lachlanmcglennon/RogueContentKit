#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RCK.Social
{
    /// <summary>
    ///   No infighting inside a party. Two agents are party-mates when one employs the other or both have the same
    ///   employer; the leader can be a player or an NPC (RCK Random Teleport squads count). Relationship rules skip
    ///   party-mates (see <see cref="SocialRules.ApplyRelationshipRules"/>), so a pair set up again, most often when
    ///   followers carry over to the next level, keeps the links the game gave it. When someone joins, this settles the
    ///   newcomer with the leader and every party-mate: no territorial registration, no hate, and no Annoyed or Hateful
    ///   left from rules applied before it joined.
    /// </summary>
    internal static class PartyPeace
    {
        public static bool ArePartyMates(Agent a, Agent b)
        {
            if (a == null || b == null || a == b) return false;
            Agent ea = a.employer, eb = b.employer;
            return ea == b || eb == a || (ea != null && ea == eb);
        }

        public static void OnJoined(Agent newcomer, Agent leader)
        {
            if (newcomer == null || leader == null || newcomer == leader || newcomer.objectAgent) return;
            Settle(newcomer, leader);
            GameController gc = GameController.gameController;
            List<Agent> agents = gc != null ? gc.agentList : null;
            if (agents == null) return;
            for (int i = 0; i < agents.Count; i++)
            {
                Agent mate = agents[i];
                if (mate == null || mate == newcomer || mate == leader || mate.employer != leader || mate.dead || mate.objectAgent) continue;
                Settle(newcomer, mate);
            }
        }

        private static void Settle(Agent a, Agent b)
        {
            Territorial.Unregister(a, b);
            if (SocialRules.Has(a, "Relationless") || SocialRules.Has(b, "Relationless")) return;
            Calm(a, b);
            Calm(b, a);
        }

        /// <summary>
        ///   Clears hate and strikes directly: vanilla <c>SetRelHate(0)</c> does nothing once hate has reached 5, and the
        ///   hate left behind would turn Loyal back into Hateful on the next hate change.
        /// </summary>
        private static void Calm(Agent source, Agent target) => RelOps.Calm(source, target, "Loyal");
    }

    // JoinParty(Agent) calls this overload. joinPartyDelay (hires, recruits, followers carried to the next level) sets
    // the employer first, then calls it, then sets the join type's own links in the same frame, after this postfix.
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.JoinParty), typeof(Agent), typeof(string))]
    internal static class Relationships_JoinParty_PartyPeace_Patch
    {
        // Relationships.agent is private and Mono enforces field access, so it comes in through Harmony's ___agent.
        private static void Postfix(Agent ___agent, Agent interactingAgent)
        {
            try { PartyPeace.OnJoined(___agent, interactingAgent); }
            catch (Exception e) { SocialRules.LogOnce(___agent, "party-peace", e); }
        }
    }
}
