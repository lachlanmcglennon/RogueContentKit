#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Faction members are welcome in the private areas of factions friendly to theirs. A player who joined a
    ///   faction through a faction trait (<c>&lt;key&gt;_Member</c> or <c>&lt;key&gt;_Aligned</c>) can enter, open
    ///   doors in and talk in the owned rooms of an NPC whose factions share one of the player's or are Friendly or
    ///   Aligned toward them (see <see cref="Factions.WelcomesInPrivate"/>). The player's followers come in with the player.
    ///   Only the trespass responses are skipped: <c>ProtectOwned</c>, <c>ProtectOwnedLight</c> ("get out") and the
    ///   no-entry door check. Stealing, breaking, hacking, fires, bombs, opening prison cells and hurting anyone still
    ///   count, and once the owner is Annoyed or worse the visitor is a trespasser again. Security cameras, turrets and
    ///   laser emitters keep vanilla's ownership rules, as they do for Friendly visitors in vanilla.
    /// </summary>
    internal static class PrivateAccess
    {
        public static bool Welcomes(Agent owner, Agent visitor, Relationship rel)
        {
            if (owner == null || visitor == null || owner == visitor || owner.isPlayer != 0 || owner.dead || owner.objectAgent) return false;
            if (rel == null || (rel.relTypeCode != relStatus.Neutral && rel.relTypeCode != relStatus.Friendly) || rel.relHate >= 5f) return false;
            Agent member = visitor.isPlayer > 0 ? visitor : visitor.employer;
            if (member == null || member.isPlayer <= 0 || member.dead || visitor.objectAgent) return false;
            if (member != visitor)
            {
                // A follower comes in only with its player, who must be welcome too.
                Relationship memberRel = RelOps.Of(owner, member);
                if (memberRel == null || (memberRel.relTypeCode != relStatus.Neutral && memberRel.relTypeCode != relStatus.Friendly
                    && memberRel.relTypeCode != relStatus.Aligned && memberRel.relTypeCode != relStatus.Loyal && memberRel.relTypeCode != relStatus.Submissive)) return false;
            }
            return Factions.WelcomesInPrivate(owner, member);
        }

        /// <summary>The trespass tests below all need the visitor on owned floor; street checks (owner 0) stay vanilla.</summary>
        public static bool OnPrivateFloor(Agent visitor) => visitor.curTileData != null && visitor.curTileData.owner != 0;
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.ProtectOwned), typeof(Agent), typeof(Relationship))]
    internal static class Relationships_ProtectOwned_PrivateAccess_Patch
    {
        // Relationships.agent is private and Mono enforces field access, so it comes in through Harmony's ___agent.
        private static bool Prefix(Agent ___agent, Agent otherAgent, Relationship myRelationship)
        {
            try
            {
                if (otherAgent == null || !PrivateAccess.OnPrivateFloor(otherAgent)) return true;
                // A police lockdown is not a private area.
                if (___agent.enforcer && GameController.gameController.policeLockdown) return true;
                return !PrivateAccess.Welcomes(___agent, otherAgent, myRelationship);
            }
            catch (Exception e)
            {
                SocialRules.LogOnce(___agent, "private-access", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.ProtectOwnedLight), typeof(Agent), typeof(Relationship))]
    internal static class Relationships_ProtectOwnedLight_PrivateAccess_Patch
    {
        private static bool Prefix(Agent ___agent, Agent otherAgent, Relationship myRelationship)
        {
            try
            {
                if (otherAgent == null || !PrivateAccess.OnPrivateFloor(otherAgent)) return true;
                // Gang muggings, Cop Bot checks and the Mayor's guards are not about private areas.
                if (___agent.gangLeader || ___agent.copBot || ___agent.guardingMayor) return true;
                return !PrivateAccess.Welcomes(___agent, otherAgent, myRelationship);
            }
            catch (Exception e)
            {
                SocialRules.LogOnce(___agent, "private-access", e);
                return true;
            }
        }
    }

    // gc.OwnCheck runs this for every active NPC each time a door is opened or unlocked ("Door"), so the type test
    // comes first. Every other check type (theft, damage, hacking, operating, security, fire) stays vanilla, and so do
    // prison cell doors: opening one is a jailbreak, not a visit.
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.OwnCheck), typeof(Agent), typeof(GameObject), typeof(int), typeof(string), typeof(bool), typeof(int), typeof(Fire))]
    internal static class Relationships_OwnCheck_PrivateAccess_Patch
    {
        private static bool Prefix(Agent ___agent, Agent otherAgent, GameObject affectedGameObject, string ownCheckType)
        {
            if (ownCheckType != "Door" || otherAgent == null) return true;
            try
            {
                ObjectReal door = affectedGameObject != null ? affectedGameObject.GetComponent<ObjectReal>() : null;
                if (door == null || door.prisonObject > 0) return true;
                return !PrivateAccess.Welcomes(___agent, otherAgent, RelOps.Of(___agent, otherAgent));
            }
            catch (Exception e)
            {
                SocialRules.LogOnce(___agent, "private-access", e);
                return true;
            }
        }
    }
}
