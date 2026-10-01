#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    public sealed class SocialModule : IRckModule
    {
        public string Name => "Social";

        public void Initialize()
        {
            Factions.Initialize();
            FactionRecruit.Initialize();
            StreetInnocence.Initialize();
            NoInfighting.Initialize();
            FactionEvents.Initialize();
            Disguises.Initialize();
            TurfWar.Initialize();
            WarConfig.Initialize();
            FactionRaids.Initialize();
            FactionRespawn.Initialize();
            FactionBackup.Initialize();
            Broker.Initialize();
            FactionMedic.Initialize();
            Racketeer.Initialize();
            RckWorld.Factions = new SocialWorld();
            RckWorld.RegisterGateSwitch("Routed", RoutedGate);
            RckWorld.RegisterGateSwitch("TurfTaken", TurfWar.TakenGate);
            WarPanel.Create();
        }

        /// <summary>Level gate <c>Routed=Crepe</c> (or <c>Crepe+Blahd</c>, all of them): the faction's last leader fell.</summary>
        private static bool RoutedGate(string arg)
        {
            bool any = false;
            ulong routed = FactionEvents.Routed;
            foreach (string raw in arg.Split('+', ','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                int i = Factions.KeyIndex(name);
                if (i < 0 || (routed & (1UL << i)) == 0) return false;
                any = true;
            }
            return any;
        }
    }

    internal static class SocialRules
    {
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);

        public static void LogOnce(Agent agent, string key, Exception e)
        {
            string id = key + ":" + (agent != null ? agent.agentID.ToString() : "null");
            if (logged.Add(id)) Rck.Log.LogError($"Social {key} failed for {agent}: {e}");
        }

        public static bool Has(Agent agent, string trait) => agent != null && AgentTraits.Has(agent, trait);

        public static bool HasAny(Agent agent, string a, string b)
            => Has(agent, a) || Has(agent, b);

        public static bool HasAny(Agent agent, string a, string b, string c)
            => Has(agent, a) || Has(agent, b) || Has(agent, c);

        public static bool IsNpc(Agent agent) => agent != null && agent.isPlayer == 0 && !agent.objectAgent;

        public static void ApplyRelationshipRules(Relationships relationships, Agent otherAgent)
        {
            Agent agent = relationships.GetComponent<Agent>();
            if (agent == null || otherAgent == null) return;
            // A pair set up again is decided again: forget any territorial registration from the last setup (or from
            // an earlier load of this level, whose agent IDs this pair may reuse).
            Territorial.Unregister(agent, otherAgent);
            if (agent == otherAgent || agent.objectAgent || otherAgent.objectAgent) return;

            if (Has(agent, "Relationless") || Has(otherAgent, "Relationless"))
            {
                if (!caughtMode) SetPair(agent, otherAgent, "Neutral", 0);
                return;
            }

            // Party-mates keep the Loyal, Aligned or Submissive links the game gave them: no own rules, faction rules,
            // Guilty or trait gates between them, and street innocence never escalates them (see PartyPeace).
            if (PartyPeace.ArePartyMates(agent, otherAgent)) return;

            // A unit sworn to a commanded faction leaves its type's vanilla ties behind (see Factions.Swear).
            if (!caughtMode) Factions.ResetSwornTies(agent, otherAgent);

            // The game sets up each pair from both sides, so both calls check the same fixed order and agree. Each
            // direction keeps the first rule that sets it: the lower-ID agent's own rules, then the other agent's own
            // rules, then faction traits.
            Agent first = agent.agentID <= otherAgent.agentID ? agent : otherAgent;
            Agent second = first == agent ? otherAgent : agent;
            ruleA = first;
            ruleB = second;
            setAB = setBA = false;
            try
            {
                if (ApplyOwnRules(first, second)) setAB = true;
                if (!(setAB && setBA) && ApplyOwnRules(second, first)) setBA = true;
                if (!(setAB && setBA)) Factions.Apply(first, second);
            }
            finally
            {
                ruleA = ruleB = null;
            }
            // Faction relationships changed in play this level (truces, frames, standing), then Vengeful grudges,
            // defectors and routs from this level, then raid, backup and turf-guard squads, outrank the rules above (see
            // LevelRelations, FactionEvents and Squads). A disguised player's calmed members stay calm (see Disguises).
            LevelRelations.AfterPairSetup(first, second);
            FactionEvents.AfterPairSetup(first, second);
            Squads.AfterPairSetup(first, second);
            TurfWar.AfterPairSetup(first, second);
            Disguises.AfterPairSetup(first, second);
        }

        private static Agent ruleA, ruleB;
        private static bool setAB, setBA;

        /// <summary>
        ///   Set while a pair is decided again because street innocence just ended for one side (see
        ///   <see cref="ApplyCaught"/>). Every rule claims its direction as at spawn, but only the rules innocence held
        ///   back (suppressible ones) set anything, and only to make the relationship worse.
        /// </summary>
        private static bool caughtMode;

        /// <summary>
        ///   <paramref name="caught"/> was innocent and has just been caught: applies the faction and Guilty rules its
        ///   innocence held back toward <paramref name="other"/>, both ways, without undoing anything that happened in play.
        /// </summary>
        public static void ApplyCaught(Agent caught, Agent other)
        {
            if (caught?.relationships == null || other?.relationships == null) return;
            caughtMode = true;
            // The rules catching up with the caught agent aren't anyone noticing a disguise.
            Disguises.Quiet++;
            try { ApplyRelationshipRules(caught.relationships, other); }
            finally
            {
                caughtMode = false;
                Disguises.Quiet--;
            }
        }

        private static bool ApplyOwnRules(Agent agent, Agent otherAgent)
            => ApplyPlayerRelationship(agent, otherAgent) || ApplyTraitGate(agent, otherAgent) || ApplyGeneral(agent, otherAgent);

        private static bool ApplyPlayerRelationship(Agent agent, Agent otherAgent)
        {
            if (!IsNpc(agent) || otherAgent.isPlayer <= 0) return false;
            if (Has(agent, "Player_Neutral"))
            {
                SetPair(agent, otherAgent, "Neutral", 0);
                return true;
            }
            if (Has(agent, "Player_Secret_Hate"))
            {
                if (!caughtMode) agent.relationships.SetSecretHate(otherAgent, true);
                return true;
            }
            if (Has(agent, "Player_Hostile")) { SetPair(agent, otherAgent, "Hateful", 5); return true; }
            if (Has(agent, "Player_Annoyed")) { SetDirected(agent, otherAgent, "Annoyed", 0); return true; }
            if (Has(agent, "Player_Submissive")) { SetPair(agent, otherAgent, "Submissive", 0); return true; }
            if (Has(agent, "Player_Loyal")) { SetPair(agent, otherAgent, "Loyal", 0); return true; }
            if (Has(agent, "Player_Aligned")) { SetPair(agent, otherAgent, "Aligned", 0); return true; }
            if (Has(agent, "Player_Friendly")) { SetPair(agent, otherAgent, "Friendly", 0); return true; }
            return false;
        }

        private static bool ApplyTraitGate(Agent agent, Agent otherAgent)
        {
            if (!IsNpc(agent)) return false;
            bool otherIsPlayer = otherAgent.isPlayer > 0;
            if (Has(agent, "Common_Folk") && otherIsPlayer && Has(otherAgent, "GenericAgentsAligned"))
            {
                SetDirected(agent, otherAgent, "Loyal", 0);
                return true;
            }
            if (Has(agent, "Cool_Cannibal") && otherIsPlayer && Has(otherAgent, "CannibalsNeutral"))
            {
                SetDirected(agent, otherAgent, "Neutral", 0);
                return true;
            }
            if (Has(agent, "Family_Friend") && (Has(otherAgent, "MafiaAligned") || otherAgent.agentName == "Mafia" || otherAgent.agentName == "Mobster"))
            {
                SetPair(agent, otherAgent, "Aligned", 0);
                return true;
            }
            if (Has(agent, "Scumbag") && (HasAny(otherAgent, "ScumbagSlaughterer", "Scumbag_Slaughterer") || Has(otherAgent, "MechHateTrait")))
            {
                SetPair(agent, otherAgent, "Hateful", 5);
                return true;
            }
            if (Has(agent, "Slayable") && HasAny(otherAgent, "ScientistSlaughterer", "Scientist_Slaughterer"))
            {
                SetPair(agent, otherAgent, "Hateful", 5);
                return true;
            }
            if (Has(agent, "Specistist") && (Has(otherAgent, "Specist") || Has(otherAgent, "HatesGorilla")))
            {
                SetPair(agent, otherAgent, "Hateful", 5);
                return true;
            }
            if (Has(agent, "Suspecter") && Has(otherAgent, "Suspicious"))
            {
                SetDirected(agent, otherAgent, "Annoyed", 0);
                return true;
            }
            return false;
        }

        private static bool ApplyGeneral(Agent agent, Agent otherAgent)
        {
            if (!IsNpc(agent)) return false;
            if (Has(agent, "Aligned_to_Innocent") && IsInnocent(otherAgent, agent))
            {
                SetPair(agent, otherAgent, "Aligned", 0);
                return true;
            }
            if (Has(agent, "Hostile_to_Guilty") && IsGuilty(otherAgent))
            {
                SetPair(agent, otherAgent, "Hateful", 5, suppressible: true);
                return true;
            }
            if (Has(agent, "Hostile_to_Scumbag") && Has(otherAgent, "Scumbag"))
            {
                SetPair(agent, otherAgent, "Hateful", 5);
                return true;
            }
            return false;
        }

        private static bool IsInnocent(Agent target, Agent viewer)
        {
            if (Has(target, "Innocent")) return true;
            try { return target.statusEffects != null && target.statusEffects.IsInnocent(viewer); }
            catch { return false; }
        }

        /// <summary>Guilty for <c>Hostile_to_Guilty</c>, unless street innocence hides it for now.</summary>
        private static bool IsGuilty(Agent agent)
            => (Has(agent, "Guilty") || agent.oma.mustBeGuilty) && !(StreetInnocence.IsInnocent(agent) && !StreetInnocence.AlwaysGuilty(agent));

        public static void SetPair(Agent a, Agent b, string rel, int hate, bool suppressible = false)
        {
            SetDirected(a, b, rel, hate, suppressible);
            SetDirected(b, a, rel, hate, suppressible);
        }

        /// <summary>
        ///   Claims the direction <paramref name="source"/>→<paramref name="target"/> for the rule being applied; false
        ///   if an earlier rule in this pair setup already claimed it. Outside a pair setup every claim succeeds.
        /// </summary>
        public static bool Claim(Agent source, Agent target)
        {
            if (source == null || target == null || source == target || source.relationships == null) return false;
            if (ruleA != null)
            {
                if (source == ruleA && target == ruleB) { if (setAB) return false; setAB = true; }
                else if (source == ruleB && target == ruleA) { if (setBA) return false; setBA = true; }
            }
            return true;
        }

        /// <summary>
        ///   Sets one direction; false if it was skipped because an earlier rule in this pair setup already set it.
        ///   <paramref name="suppressible"/> marks the rules street innocence holds back (hostile faction rules and
        ///   Guilty), which are the only ones applied again when an innocent agent is caught.
        /// </summary>
        public static bool SetDirected(Agent source, Agent target, string rel, int hate, bool suppressible = false)
        {
            if (!Claim(source, target)) return false;
            if (caughtMode)
            {
                if (suppressible) Escalate(source, target, rel, hate);
                return true;
            }
            source.relationships.SetRelInitial(target, rel);
            source.relationships.SetRelHate(target, hate);
            return true;
        }

        /// <summary>Sets Annoyed or Hateful only over Neutral, Friendly or (for Hateful) Annoyed; Aligned, Loyal and Submissive stay.</summary>
        private static void Escalate(Agent source, Agent target, string rel, int hate)
        {
            int want = rel == "Hateful" ? 3 : rel == "Annoyed" ? 2 : 0;
            int now = EscalationRank(source.relationships.GetRelCode(target));
            if (now == 0 || want <= now) return;
            source.relationships.SetRelInitial(target, rel);
            if (hate > 0) source.relationships.SetRelHate(target, hate);
        }

        /// <summary>1 for Neutral and Friendly, 2 for Annoyed, 3 for Hostile; 0 for Aligned, Loyal and Submissive, which escalation leaves alone.</summary>
        internal static int EscalationRank(relStatus rel)
        {
            switch (rel)
            {
                case relStatus.Neutral:
                case relStatus.Friendly: return 1;
                case relStatus.Annoyed: return 2;
                case relStatus.Hostile: return 3;
                default: return 0;
            }
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetupRelationshipOriginal), typeof(Agent))]
    internal static class Relationships_SetupRelationshipOriginal_Patch
    {
        private static void Postfix(Relationships __instance, Agent otherAgent)
        {
            try { SocialRules.ApplyRelationshipRules(__instance, otherAgent); }
            catch (Exception e) { SocialRules.LogOnce(__instance.GetComponent<Agent>(), "relationships", e); }
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRel), typeof(Agent), typeof(string))]
    internal static class Relationships_SetRel_Patch
    {
        private static bool relationlessGuard;

        public static void Postfix(Relationships __instance, Agent otherAgent)
        {
            try
            {
                if (relationlessGuard) return;
                Agent agent = __instance.GetComponent<Agent>();
                if (SocialRules.Has(agent, "Relationless") || SocialRules.Has(otherAgent, "Relationless"))
                {
                    relationlessGuard = true;
                    try { SocialRules.SetDirected(agent, otherAgent, "Neutral", 0); }
                    finally { relationlessGuard = false; }
                }
            }
            catch (Exception e) { SocialRules.LogOnce(__instance.GetComponent<Agent>(), "relationless", e); }
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRel), typeof(Agent), typeof(string), typeof(bool))]
    internal static class Relationships_SetRel_Server_Patch
    {
        private static void Postfix(Relationships __instance, Agent otherAgent) => Relationships_SetRel_Patch.Postfix(__instance, otherAgent);
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRelInitial), typeof(Agent), typeof(string))]
    internal static class Relationships_SetRelInitial_Patch
    {
        private static void Postfix(Relationships __instance, Agent otherAgent) => Relationships_SetRel_Patch.Postfix(__instance, otherAgent);
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRelInitial), typeof(Agent), typeof(string), typeof(bool))]
    internal static class Relationships_SetRelInitial_Server_Patch
    {
        private static void Postfix(Relationships __instance, Agent otherAgent) => Relationships_SetRel_Patch.Postfix(__instance, otherAgent);
    }

    internal static class HiringRules
    {
        private static readonly string[] hireTraits =
        {
            "Cyber_Intruder", "Decoy", "Intruder", "Muscle", "Pickpocket", "Poisoner", "Saboteur", "Safecracker", "Trapper"
        };

        public static bool HasHireTrait(Agent agent)
        {
            for (int i = 0; i < hireTraits.Length; i++)
                if (AgentTraits.Has(agent, hireTraits[i])) return true;
            return false;
        }

        public static void AddButtons(AgentInteractions interactions, Agent agent, Agent interactingAgent, List<string> buttons)
        {
            if (!SocialRules.IsNpc(agent) || interactingAgent == null || !HasHireTrait(agent)) return;
            if (agent.relationships.GetRelCode(interactingAgent) == relStatus.Annoyed) return;

            bool permanent = IsPermanent(agent);
            int mult = permanent ? 8 : 1;
            if (agent.employer == null)
            {
                if (SocialRules.Has(agent, "Muscle"))
                    AddButton(interactions, buttons, VanillaButtons.HireAsProtection, Price(agent, "GangbangerHire", mult));
                if (HasExpertHire(agent))
                {
                    if (interactingAgent.inventory.HasItem("HiringVoucher")) AddButton(interactions, buttons, VanillaButtons.AssistMe, 6666);
                    AddButton(interactions, buttons, VanillaButtons.AssistMe, Price(agent, HireCostType(agent), mult));
                }
                return;
            }

            if (agent.employer != interactingAgent) return;
            bool canDoTask = !agent.oma.cantDoMoreTasks || permanent;
            if (!canDoTask) return;
            if (SocialRules.HasAny(agent, "Cyber_Intruder", "Saboteur")) AddButton(interactions, buttons, VanillaButtons.HackSomething, 0);
            if (SocialRules.HasAny(agent, "Intruder", "Safecracker", "Trapper")) AddButton(interactions, buttons, VanillaButtons.LockpickDoor, 0);
            if (SocialRules.HasAny(agent, "Decoy", "Pickpocket", "Poisoner")) AddButton(interactions, buttons, VanillaButtons.CauseRuckus, 0);
        }

        public static void ApplyDuration(Agent agent)
        {
            if (agent == null || agent.employer == null) return;
            if (SocialRules.Has(agent, "Homesickly")) agent.canGoBetweenLevels = false;
            if (SocialRules.Has(agent, "Homesickless") || IsPermanent(agent)) agent.canGoBetweenLevels = true;
        }

        private static bool HasExpertHire(Agent agent)
        {
            return SocialRules.Has(agent, "Cyber_Intruder") || SocialRules.Has(agent, "Decoy") || SocialRules.Has(agent, "Intruder")
                || SocialRules.Has(agent, "Pickpocket") || SocialRules.Has(agent, "Poisoner") || SocialRules.Has(agent, "Saboteur")
                || SocialRules.Has(agent, "Safecracker") || SocialRules.Has(agent, "Trapper");
        }

        internal static bool IsPermanent(Agent agent) => SocialRules.HasAny(agent, "Permanent_Hire", "Permanent_Hire_Only");

        private static string HireCostType(Agent agent)
        {
            if (SocialRules.HasAny(agent, "Cyber_Intruder", "Saboteur")) return "HackerAssist";
            if (SocialRules.HasAny(agent, "Intruder", "Safecracker", "Trapper") || SocialRules.Has(agent, "Pickpocket")) return "ThiefAssist";
            return "HoboAssist";
        }

        internal static int Price(Agent agent, string type, int mult)
        {
            int price = agent.determineMoneyCost(type);
            // RCK.Merchants' currency traits make every price this NPC charges a code from -6604 to -6600, paid in
            // items or health and labelled in words. Keep the code; clamping it would make the hire free.
            if (price >= -6604 && price <= -6600) return price;
            return Mathf.Clamp(price * mult, 0, 9999);
        }

        private static void AddButton(AgentInteractions interactions, List<string> buttons, string button, int price)
        {
            if (buttons.Contains(button)) return;
            if (price == 0) interactions.AddButton(button);
            else interactions.AddButton(button, price);
        }
    }

    [HarmonyPatch(typeof(AgentInteractions), nameof(AgentInteractions.DetermineButtons), typeof(Agent), typeof(Agent), typeof(List<string>), typeof(List<string>), typeof(List<int>))]
    internal static class AgentInteractions_DetermineButtons_Patch
    {
        private static void Postfix(AgentInteractions __instance, Agent agent, Agent interactingAgent, List<string> buttons1)
        {
            try { HiringRules.AddButtons(__instance, agent, interactingAgent, buttons1); }
            catch (Exception e) { SocialRules.LogOnce(agent, "hire-buttons", e); }
            try { FactionRecruit.AddButtons(__instance, agent, interactingAgent, buttons1); }
            catch (Exception e) { SocialRules.LogOnce(agent, "recruit-buttons", e); }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetEmployer), typeof(Agent))]
    internal static class Agent_SetEmployer_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { HiringRules.ApplyDuration(__instance); }
            catch (Exception e) { SocialRules.LogOnce(__instance, "hire-duration", e); }
            try { FactionEvents.OnEmployed(__instance); }
            catch (Exception e) { SocialRules.LogOnce(__instance, "faction-defector", e); }
        }
    }
}
