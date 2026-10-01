#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{
    /// <summary>
    ///   The game side of the <c>[RCK]FactionRecruit::</c> mutator (see FactionRecruit.Rules.cs for the format). Several
    ///   such mutators combine as if they were one.
    /// </summary>
    internal sealed partial class RecruitMatrix
    {
        private static readonly MutatorCache<RecruitMatrix> cache = new MutatorCache<RecruitMatrix>(IsRecruitMutator, Parse, null);

        internal static bool IsRecruitMutator(string mutator)
            => mutator != null && mutator.IndexOf(RckData.FactionRecruitPrefix, StringComparison.Ordinal) >= 0;

        /// <summary>The recruit matrix for the running level, or null if no mutator sets one. Parsed again only when the mutators change.</summary>
        public static RecruitMatrix Current() => cache.Current();

        private static RecruitMatrix Parse(List<string> raws)
        {
            if (raws.Count == 0) return null;
            var bodies = new List<string>(raws.Count);
            foreach (string raw in raws)
            {
                int start = raw.IndexOf(RckData.FactionRecruitPrefix, StringComparison.Ordinal);
                bodies.Add(raw.Substring(start + RckData.FactionRecruitPrefix.Length));
            }
            RecruitMatrix matrix = Build(bodies, Factions.KeyIndex,
                (text, error) => Rck.Log.LogWarning($"Factions: ignored {RckData.FactionRecruitPrefix} entry \"{text}\": {error}."));
            if (matrix != null) Rck.Log.LogInfo($"Factions: recruit policies with {matrix.EntryCount} entries.");
            return matrix;
        }
    }

    /// <summary>
    ///   Faction recruiting: a player can recruit NPCs from their own factions, free ("Join me") or paid ("Hire as
    ///   protection"), as the NPC's recruit trait, the <c>[RCK]FactionRecruit::</c> matrix or the Faction Recruiting
    ///   mutators decide (see <see cref="RecruitRules"/>). Joining goes through the vanilla buttons, so the vanilla
    ///   follower cap, refusals and hire payment apply.
    /// </summary>
    internal static class FactionRecruit
    {
        public const string TraitFree = "RCK_Recruit_Free";
        public const string TraitPaid = "RCK_Recruit_Paid";
        public const string TraitNever = "RCK_Not_Recruitable";
        public const string MutatorFree = "RCK_Faction_Recruit_Free";
        public const string MutatorPaid = "RCK_Faction_Recruit_Paid";

        public static void Initialize()
        {
            foreach (string trait in new[] { TraitFree, TraitPaid, TraitNever })
                if (!Rck.IsRckTrait(trait)) Rck.Log.LogError($"Factions: recruit trait {trait} is not registered.");
            foreach (string mutator in new[] { MutatorFree, MutatorPaid })
                LevelMutators.Require(mutator, "Factions: recruit mutator");
        }

        public static void AddButtons(AgentInteractions interactions, Agent agent, Agent player, List<string> buttons)
        {
            if (!SocialRules.IsNpc(agent) || player == null || player.isPlayer <= 0 || buttons == null || agent.gc == null) return;
            if (buttons.Contains(VanillaButtons.JoinMe)) return;
            RecruitPolicy trait = TraitPolicy(agent);
            if (trait == RecruitPolicy.Off) return;
            RecruitPolicy global = GlobalPolicy(agent.gc.challenges);
            RecruitMatrix matrix = RecruitMatrix.Current();
            // Recruiting is off unless something turns it on; don't pay for the membership checks then.
            if (trait == RecruitPolicy.None && global == RecruitPolicy.None && matrix == null) return;
            if (!CanRecruit(agent, player)) return;

            ulong shared = Factions.RecruitStanding(agent, player, out ulong npcKeys, out bool friendly);
            switch (RecruitRules.ForPlayer(trait, matrix, global, shared, npcKeys, friendly))
            {
                case RecruitPolicy.Free:
                    interactions.AddButton(VanillaButtons.JoinMe);
                    break;
                case RecruitPolicy.Paid:
                    if (buttons.Contains(VanillaButtons.HireAsProtection)) break;
                    if (player.inventory != null && player.inventory.HasItem("HiringVoucher")) interactions.AddButton(VanillaButtons.HireAsProtection, 6666);
                    interactions.AddButton(VanillaButtons.HireAsProtection, HiringRules.Price(agent, "GangbangerHire", HiringRules.IsPermanent(agent) ? 8 : 1));
                    break;
            }
        }

        private static RecruitPolicy TraitPolicy(Agent agent)
        {
            if (SocialRules.Has(agent, TraitNever)) return RecruitPolicy.Off;
            if (SocialRules.Has(agent, TraitFree)) return RecruitPolicy.Free;
            if (SocialRules.Has(agent, TraitPaid)) return RecruitPolicy.Paid;
            return RecruitPolicy.None;
        }

        private static RecruitPolicy GlobalPolicy(List<string> challenges)
        {
            if (challenges == null) return RecruitPolicy.None;
            if (challenges.Contains(MutatorFree)) return RecruitPolicy.Free;
            if (challenges.Contains(MutatorPaid)) return RecruitPolicy.Paid;
            return RecruitPolicy.None;
        }

        /// <summary>The same situations in which the game offers its own gang "Join me": a free, living NPC, talked to up close outside the home base.</summary>
        private static bool CanRecruit(Agent agent, Agent player)
        {
            GameController gc = agent.gc;
            if (gc == null || gc.levelType == "HomeBase" || gc.levelType == "Tutorial") return false;
            if (agent.dead || agent.ghost || agent.mechEmpty || agent.employer != null || agent.arrested || agent.prisoner > 0) return false;
            if (agent.oma.mindControlled || agent.oma.rescuingForQuest || agent.oma.bodyGuarded || agent.slaveOwners.Count > 0) return false;
            if (player.interactionHelper != null && player.interactionHelper.interactingFar) return false;
            if (SocialRules.Has(agent, "Relationless") || agent.relationships == null) return false;
            relStatus rel = agent.relationships.GetRelCode(player);
            if (rel == relStatus.Annoyed || rel == relStatus.Hostile) return false;
            // The game shows a zombie player no buttons at all unless the NPC likes them.
            if (rel == relStatus.Neutral && player.statusEffects != null && player.statusEffects.hasTrait("EveryoneHatesZombie")) return false;
            return true;
        }
    }
}
