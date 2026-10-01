#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;

namespace RCK.Social
{
    /// <summary>
    ///   The <c>RCK_Faction_Racketeer</c> trait: a player who leans on a free racket's commoner (a Mobster-style
    ///   shakedown, with vanilla's odds) makes the racket join their faction, the one they command or else their own,
    ///   as if a squad had taken it. No money; nobody is posted there. A vanilla Mobster's own shakedown of such a
    ///   commoner does the same (see <see cref="AgentInteractions_Shakedown_Racketeer_Patch"/>). Players only, host only.
    /// </summary>
    internal static class Racketeer
    {
        public const string Trait = "RCK_Faction_Racketeer";

        public static void Initialize()
        {
            if (!Rck.IsRckTrait(Trait)) Rck.Log.LogError($"Factions: racketeer trait {Trait} is not registered.");
            ButtonLabels.Register(typeof(RacketeerButtons));
            RogueInteractions.CreateProvider<Agent>(Provide);
        }

        private static void Provide(SimpleInteractionProvider<Agent> h)
        {
            Agent npc = h.Object;
            Agent player = h.Agent;
            if (h.Helper.interactingFar || !Able(npc, player, out TurfWar.Turf t, out int key)) return;
            // A Mobster already has vanilla's Shakedown here, which claims the racket too (see the patch below).
            if (VanillaShakedown(npc, player)) return;
            h.AddButton(RacketeerButtons.Shakedown, $" {Squads.Plural(key)} ({Chance(npc, player)}%)", m => Press(m));
        }

        /// <summary>
        ///   <paramref name="player"/> is a racketeer with a faction, and <paramref name="npc"/> is one of the commoners
        ///   of free racket <paramref name="t"/>, which it could make join faction <paramref name="key"/>.
        /// </summary>
        private static bool Able(Agent npc, Agent player, out TurfWar.Turf t, out int key)
        {
            t = null;
            key = -1;
            if (npc == null || player == null || npc == player || npc.isPlayer != 0 || player.isPlayer <= 0) return false;
            if (npc.dead || npc.zombified || npc.ghost || npc.objectAgent || npc.relationships == null) return false;
            if (!SocialRules.Has(player, Trait)) return false;
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete || !WarConfig.RacketsOn(gc)) return false;
            relStatus rel = npc.relationships.GetRelCode(player);
            if (rel == relStatus.Hostile) return false;
            key = KeyOf(player);
            if (key < 0) return false;
            t = TurfWar.FreeRacketOf(npc);
            return t != null;
        }

        /// <summary>The faction a racketeer works for: the one the player commands, else their own (never common folk or a routed one); -1 if none.</summary>
        internal static int KeyOf(Agent player)
        {
            int commanded = Command.Key;
            if (commanded >= 0) return commanded;
            ulong among = Factions.All & ~Factions.CommonFolkBit & ~FactionEvents.Routed;
            return Factions.PrimaryKey(player, among);
        }

        /// <summary>Vanilla's shakedown chance, as its button shows it.</summary>
        private static int Chance(Agent npc, Agent player)
            => Certain(npc, player) ? 100 : npc.relationships.FindThreat(player, false);

        private static bool Certain(Agent npc, Agent player)
            => npc.health <= npc.healthMax * 0.4f || npc.relationships.GetRelCode(player) == relStatus.Aligned || npc.slaveOwners.Contains(player);

        private static bool VanillaShakedown(Agent npc, Agent player)
            => (player.statusEffects.hasTrait("Shakedowner") || player.statusEffects.hasTrait("Shakedowner2")) && npc.CanShakeDown();

        private static void Press(InteractionModel<Agent> m)
        {
            Agent npc = m.Object, player = m.Agent;
            GameController gc = FactionEvents.Server();
            if (gc == null || !Able(npc, player, out TurfWar.Turf t, out int key))
            {
                m.StopInteraction();
                return;
            }
            m.StopInteraction();
            int chance = npc.relationships.FindThreat(player, true);
            if (Certain(npc, player) || gc.percentChance(chance))
            {
                npc.SayDialogue("ThreatenedShakedown", true);
                relStatus rel = npc.relationships.GetRelCode(player);
                if (rel != relStatus.Aligned && rel != relStatus.Loyal) npc.relationships.SetRel(player, "Submissive");
                Take(gc, t, key, npc, player);
                return;
            }
            npc.SayDialogue("ThreatenedAngry", true);
            npc.relationships.SetRel(player, "Hateful");
            npc.relationships.SetRelHate(player, 5);
            npc.melee.Attack(player);
            gc.EnforcerAlertAttack(player, npc, 7.4f);
            Rck.Log.LogInfo($"Factions: {AgentText.Describe(npc)} refused {AgentText.Describe(player)}'s shakedown for {Factions.Keys[key]} (racket owner {t.Owner}, chunk {t.Chunk}).");
        }

        private static void Take(GameController gc, TurfWar.Turf t, int key, Agent npc, Agent player)
        {
            Rck.Log.LogInfo($"Factions: {AgentText.Describe(player)} shook down {AgentText.Describe(npc)}; the racket (owner {t.Owner}, chunk {t.Chunk}) joins {Factions.Keys[key]}.");
            TurfWar.Claim(gc, t, key, new List<Agent>());
        }

        /// <summary>A vanilla shakedown of <paramref name="npc"/> by <paramref name="player"/> just succeeded.</summary>
        internal static void AfterVanilla(Agent npc, Agent player)
        {
            if (!Able(npc, player, out TurfWar.Turf t, out int key)) return;
            Take(FactionEvents.Server(), t, key, npc, player);
        }
    }

    /// <summary>Racketeer buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
    internal static class RacketeerButtons
    {
        [ButtonLabel("Shake down for")] public const string Shakedown = "RCK_RacketeerShakedown";
    }

    [HarmonyPatch(typeof(AgentInteractions), nameof(AgentInteractions.Shakedown), typeof(Agent), typeof(Agent))]
    internal static class AgentInteractions_Shakedown_Racketeer_Patch
    {
        private static void Prefix(Agent agent, out bool __state) => __state = agent != null && agent.oma != null && agent.oma.shookDown;

        private static void Postfix(Agent agent, Agent interactingAgent, bool __state)
        {
            if (__state || agent == null || agent.oma == null || !agent.oma.shookDown || agent.shookDownAgent != interactingAgent) return;
            try { Racketeer.AfterVanilla(agent, interactingAgent); }
            catch (Exception e) { SocialRules.LogOnce(agent, "racketeer-shakedown", e); }
        }
    }
}
