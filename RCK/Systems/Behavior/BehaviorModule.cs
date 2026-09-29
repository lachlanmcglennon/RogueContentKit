#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RCK.Behavior
{
    public sealed class BehaviorModule : IRckModule
    {
        public string Name => "Behavior";

        public void Initialize()
        {
            Rck.TraitAdded += static (agent, trait, _) => BehaviorRuntime.OnTraitAdded(agent, trait);
        }
    }

    /// <summary>
    ///   Concealed_Carrier: the NPC holds its fists until it fights, draws its best weapon as soon as it does, and
    ///   puts the weapon away again once it has been calm for a while. The hidden weapon is tracked here because
    ///   vanilla EquipWeapon overwrites inventory.lastEquippedWeapon with the fist.
    /// </summary>
    internal static class ConcealedCarry
    {
        private const float CalmSecondsBeforeHiding = 3f;

        private sealed class State
        {
            public bool Hidden;
            public InvItem Weapon;
            public float CalmSince = -1f;
        }

        private static readonly ConditionalWeakTable<Agent, State> states = new ConditionalWeakTable<Agent, State>();

        public static void Update(Agent agent, bool immediate)
        {
            if (!AgentTraits.Has(agent, "Concealed_Carrier")) return;
            InvDatabase inv = agent.inventory;
            if (inv == null || inv.fist == null) return;
            State state = states.GetOrCreateValue(agent);

            if (IsFighting(agent))
            {
                state.CalmSince = -1f;
                if (state.Hidden) Draw(inv, state);
                return;
            }

            if (state.Hidden) return;
            InvItem current = inv.equippedWeapon;
            if (current == null || current == inv.fist || !current.isWeapon) return;

            float now = Time.time;
            if (!immediate)
            {
                if (state.CalmSince < 0f) { state.CalmSince = now; return; }
                if (now - state.CalmSince < CalmSecondsBeforeHiding) return;
            }

            state.Hidden = true;
            state.Weapon = current;
            inv.EquipWeapon(inv.fist, false);
        }

        private static void Draw(InvDatabase inv, State state)
        {
            InvItem weapon = state.Weapon;
            state.Hidden = false;
            state.Weapon = null;
            if (inv.equippedWeapon != null && inv.equippedWeapon != inv.fist) return;
            if (weapon != null && weapon.invItemCount > 0 && inv.InvItemList.Contains(weapon)) inv.EquipWeapon(weapon, false);
            if (inv.equippedWeapon == null || inv.equippedWeapon == inv.fist) inv.ChooseWeapon();
        }

        private static bool IsFighting(Agent agent)
        {
            return agent.hasOpponent || agent.opponent != null || agent.inCombat || agent.inFleeCombat || agent.isBattling
                || agent.mostRecentGoalCode == goalType.Battle || agent.mostRecentGoalCode == goalType.Flee;
        }
    }

    internal static class BehaviorRuntime
    {
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);

        public static void LogOnce(Agent agent, string key, Exception e)
        {
            string id = key + ":" + (agent != null ? agent.agentID.ToString() : "null");
            if (logged.Add(id)) Rck.Log.LogError($"Behavior {key} failed for {agent}: {e}");
        }

        private static bool Has(Agent agent, string trait) => agent != null && AgentTraits.Has(agent, trait);
        private static bool IsNpc(Agent agent) => agent != null && agent.isPlayer == 0 && !agent.objectAgent;

        public static void OnTraitAdded(Agent agent, string trait)
        {
            if (!IsSetupTrait(trait)) return;
            try { ApplyAgentSetup(agent); }
            catch (Exception e) { LogOnce(agent, "trait-added", e); }
        }

        private static bool IsSetupTrait(string trait)
        {
            switch (trait)
            {
                case "Accident_Prone":
                case "Brainless":
                case "Concealed_Carrier":
                case "Seek_and_Destroy":
                case "Eat_Corpses":
                case "Pick_Pockets":
                case "Suck_Blood":
                case "Grab_Alcohol":
                case "Grab_Drugs":
                case "Grab_Everything":
                case "Grab_Food":
                case "Grab_Money":
                case "Vigilant":
                case "Vigilanter":
                case "Vigilantest":
                case "Deaf":
                case "Sharp_Hearing":
                case "Dolphin_Ears":
                case "Hack_Sensor":
                case "Owl_Ears":
                case "Snake_Ears":
                case "Eight_Beamed":
                case "Vision_Beam_Normal":
                case "Cyclops_Eye":
                case "Falcon_Eyes":
                case "Horse_Eyes":
                case "Mantis_Eyes":
                case "Xenops_Eyes":
                case "Visually_Blind":
                case "Visually_Disabled":
                case "Visually_Impaired":
                case "Visually_Sharp":
                case "Visually_Vigilant":
                case "Visually_Zenithal":
                    return true;
                default:
                    return false;
            }
        }

        public static void ApplyAgentSetup(Agent agent)
        {
            if (!IsNpc(agent)) return;

            if (Has(agent, "Brainless"))
            {
                agent.modVigilant = 0;
                agent.losCheckAtIntervals = false;
                agent.hearingRange = 0f;
                agent.LOSRange = 0f;
            }

            if (Has(agent, "Vigilant")) agent.modVigilant = Math.Max(agent.modVigilant, 1);
            if (Has(agent, "Vigilanter")) agent.modVigilant = Math.Max(agent.modVigilant, 2);
            if (Has(agent, "Vigilantest")) agent.modVigilant = Math.Max(agent.modVigilant, 3);

            if (Has(agent, "Eat_Corpses") || Has(agent, "Pick_Pockets") || Has(agent, "Suck_Blood") || HasItemGrabTrait(agent))
                agent.losCheckAtIntervals = true;

            ApplySenses(agent);
            ConcealedCarry.Update(agent, immediate: true);
        }

        private static bool HasItemGrabTrait(Agent agent)
        {
            return Has(agent, "Grab_Alcohol") || Has(agent, "Grab_Drugs") || Has(agent, "Grab_Everything")
                || Has(agent, "Grab_Food") || Has(agent, "Grab_Money");
        }

        private static void ApplySenses(Agent agent)
        {
            if (Has(agent, "Deaf") || Has(agent, "Snake_Ears")) agent.hearingRange = 0f;
            else if (Has(agent, "Owl_Ears")) agent.hearingRange = 50f;
            else if (Has(agent, "Sharp_Hearing") || Has(agent, "Hack_Sensor")) agent.hearingRange = 150f;
            else if (Has(agent, "Dolphin_Ears")) agent.hearingRange = 250f;

            if (Has(agent, "Visually_Blind")) agent.LOSRange = 0f;
            else if (Has(agent, "Visually_Disabled")) agent.LOSRange = 4.48f;
            else if (Has(agent, "Visually_Impaired")) agent.LOSRange = 8f;
            else if (Has(agent, "Visually_Sharp")) agent.LOSRange = 18f;
            else if (Has(agent, "Visually_Vigilant")) agent.LOSRange = 27f;
            else if (Has(agent, "Visually_Zenithal")) agent.LOSRange = 40f;

            if (Has(agent, "Cyclops_Eye")) agent.LOSCone = 30f;
            else if (Has(agent, "Falcon_Eyes")) agent.LOSCone = 60f;
            else if (Has(agent, "Vision_Beam_Normal")) agent.LOSCone = 96f;
            else if (Has(agent, "Horse_Eyes")) agent.LOSCone = 180f;
            else if (Has(agent, "Mantis_Eyes")) agent.LOSCone = 270f;
            else if (Has(agent, "Xenops_Eyes") || Has(agent, "Eight_Beamed")) agent.LOSCone = 360f;
        }

        public static void Tick(Agent agent)
        {
            if (!IsNpc(agent) || agent.dead || agent.ghost) return;
            ConcealedCarry.Update(agent, immediate: false);
            if ((Time.frameCount + agent.agentID) % 30 != 0) return;
            SeekDestroy(agent);
            RunLosActions(agent);
        }

        private static void SeekDestroy(Agent agent)
        {
            if (!Has(agent, "Seek_and_Destroy") || agent.hasEmployer || agent.inCombat || agent.opponent != null || agent.gc == null) return;
            List<Agent> players = agent.gc.playerAgentList;
            Agent best = null;
            float bestDist = 999999f;
            for (int i = 0; i < players.Count; i++)
            {
                Agent player = players[i];
                if (player == null || player.dead || player.ghost || player.invisible || player.objectAgent) continue;
                float d = Vector2.Distance(agent.curPosition, player.curPosition);
                if (d < bestDist && d < 30f)
                {
                    best = player;
                    bestDist = d;
                }
            }
            if (best == null) return;
            agent.relationships.SetRel(best, "Hateful");
            agent.relationships.SetRelHate(best, 5);
            if (bestDist < agent.LOSRange / Math.Max(1f, best.hardToSeeFromDistance) && agent.movement.HasLOSAgent(best))
                agent.SetOpponent(best);
        }

        private static void RunLosActions(Agent agent)
        {
            if (HasItemGrabTrait(agent)) TryGrabItem(agent);
            if (Has(agent, "Eat_Corpses")) TryEatCorpse(agent);
            if (Has(agent, "Pick_Pockets")) TryPickPocket(agent);
            if (Has(agent, "Suck_Blood")) TrySuckBlood(agent);
        }

        private static bool CanAct(Agent agent)
        {
            return !agent.hasEmployer && agent.slaveOwners.Count == 0 && !agent.inCombat && agent.mostRecentGoalCode != goalType.Battle
                && agent.mostRecentGoalCode != goalType.Flee && agent.mostRecentGoalCode != goalType.FleeDanger;
        }

        private static void TryGrabItem(Agent agent)
        {
            if (!CanAct(agent) || agent.gc == null) return;
            List<Item> items = Has(agent, "Grab_Money") && !Has(agent, "Grab_Everything") ? agent.gc.moneyList : agent.gc.itemList;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (!ValidItemTarget(agent, item) || !WantsItem(agent, item)) continue;
                agent.SetPreviousDefaultGoal(agent.defaultGoal);
                agent.SetDefaultGoal("GoGet");
                agent.SetGoGettingTarget(item);
                agent.stoleStuff = true;
                return;
            }
        }

        private static bool ValidItemTarget(Agent agent, Item item)
        {
            if (item == null || item.invItem == null || item.fellInHole || item.dontStealFromGround) return false;
            if (item.curTileData.prison != agent.curTileData.prison) return false;
            if (agent.curTileData.prison > 0 && agent.curTileData.chunkID != item.curTileData.chunkID) return false;
            if (agent.gc.tileInfo.DifferentLockdownZones(agent.curTileData, item.curTileData)) return false;
            if (Math.Abs(agent.curPosX - item.curPosition.x) > 5f || Math.Abs(agent.curPosY - item.curPosition.y) > 5f) return false;
            return agent.movement.HasLOSObjectNormal(item);
        }

        private static bool WantsItem(Agent agent, Item item)
        {
            InvItem inv = item.invItem;
            if (Has(agent, "Grab_Everything"))
            {
                bool dangerous = item.dangerPenalty || item.hasDanger || DangerousItems.Contains(inv.invItemName) || inv.invItemName.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0;
                return !dangerous || Has(agent, "Accident_Prone");
            }
            if (Has(agent, "Grab_Money") && inv.invItemName == "Money") return true;
            if (Has(agent, "Grab_Food") && (inv.itemType == "Food" || inv.Categories.Contains("Food") || inv.Categories.Contains("Health"))) return true;
            if (Has(agent, "Grab_Alcohol") && inv.Categories.Contains("Alcohol")) return true;
            if (Has(agent, "Grab_Drugs") && (inv.Categories.Contains("Drugs") || inv.invItemName == "Syringe" || inv.invItemName == "Cocktail")) return true;
            return false;
        }

        // Vanilla has no trap item category; these are the placeable hazards whose names lack "Trap".
        private static readonly HashSet<string> DangerousItems = new HashSet<string>(StringComparer.Ordinal)
        {
            "LandMine", "StickyMine", "TripMine", "RemoteBomb", "TimeBomb"
        };

        private static void TryEatCorpse(Agent agent)
        {
            if (!CanAct(agent) || agent.health > 15f || agent.gc == null) return;
            List<Agent> dead = agent.gc.deadAgentList;
            for (int i = 0; i < dead.Count; i++)
            {
                Agent corpse = dead[i];
                if (corpse == null || !corpse.dead || corpse.resurrect || corpse.ghost || corpse.disappeared || corpse.inhuman || corpse.cantCannibalize || corpse.hasGettingBitByAgent || corpse.arrested || corpse.invisible || corpse.fire != null) continue;
                if (agent.prisoner != corpse.prisoner) continue;
                if (agent.prisoner > 0 && agent.curTileData.chunkID != corpse.curTileData.chunkID) continue;
                if (Math.Abs(agent.curPosX - corpse.curPosX) > 5f || Math.Abs(agent.curPosY - corpse.curPosY) > 5f) continue;
                if (agent.gc.tileInfo.DifferentLockdownZones(agent.curTileData, corpse.curTileData) || !agent.movement.HasLOSAgent(corpse)) continue;
                agent.SetPreviousDefaultGoal(agent.defaultGoal);
                agent.SetDefaultGoal("Cannibalize");
                agent.SetCannibalizingTarget(corpse);
                agent.losCheckAtIntervals = false;
                return;
            }
        }

        private static void TryPickPocket(Agent agent)
        {
            if (!CanAct(agent) || !agent.inventory.HasItem("StealingGlove")) return;
            for (int i = 0; i < agent.losCheckAtIntervalsList.Count; i++)
            {
                Agent target = agent.losCheckAtIntervalsList[i];
                if (target == null || target.dead || target.invisible || target.disappeared || target.mechEmpty || target.killerRobot || target.butlerBot || target.objectAgent || target.hasGettingArrestedByAgent) continue;
                Relationship rel = agent.relationships.GetRelationship(target);
                if (rel.distance >= 4f || rel.relTypeCode == relStatus.Aligned || rel.relTypeCode == relStatus.Loyal || rel.relTypeCode == relStatus.Friendly || rel.relTypeCode == relStatus.Hostile) continue;
                if (AgentTraits.Has(target, "HonorAmongThieves") || AgentTraits.Has(target, "HonorAmongThieves2")) continue;
                if (agent.prisoner != target.prisoner || (agent.prisoner > 0 && agent.curTileData.chunkID != target.curTileData.chunkID)) continue;
                if (agent.gc.tileInfo.DifferentLockdownZones(agent.curTileData, target.curTileData)) continue;
                agent.SetDefaultGoal("Steal");
                agent.SetStealingFromAgent(target);
                agent.hectoredAgents.Add(target.agentID);
                agent.losCheckAtIntervals = false;
                agent.noEnforcerAlert = true;
                agent.oma.mustBeGuilty = true;
                return;
            }
        }

        private static void TrySuckBlood(Agent agent)
        {
            if (!CanAct(agent) || agent.health > 15f) return;
            for (int i = 0; i < agent.losCheckAtIntervalsList.Count; i++)
            {
                Agent target = agent.losCheckAtIntervalsList[i];
                if (target == null || target.dead || target.ghost || target.hologram || target.disappeared || target.inhuman || target.beast || target.zombified || target.invisible || target.hasGettingBitByAgent || target.hasGettingArrestedByAgent || target.mechEmpty || target.mechFilled || target.objectAgent || target.dizzy) continue;
                if (target.agentName == "Vampire" || (!target.localPlayer && target.isPlayer != 0)) continue;
                Relationship rel = agent.relationships.GetRelationship(target);
                if (rel.distance >= 5f || rel.relTypeCode == relStatus.Aligned || rel.relTypeCode == relStatus.Loyal || rel.relTypeCode == relStatus.Friendly || rel.relTypeCode == relStatus.Hostile) continue;
                if (agent.prisoner != target.prisoner || (agent.prisoner > 0 && agent.curTileData.chunkID != target.curTileData.chunkID)) continue;
                if (agent.gc.tileInfo.DifferentLockdownZones(agent.curTileData, target.curTileData)) continue;
                agent.SetPreviousDefaultGoal(agent.defaultGoal);
                agent.SetDefaultGoal("Bite");
                agent.SetBitingTarget(target);
                agent.hectoredAgents.Add(target.agentID);
                agent.losCheckAtIntervals = false;
                agent.noEnforcerAlert = true;
                agent.oma.mustBeGuilty = true;
                agent.oma.hasAttacked = true;
                return;
            }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetupAgentStats), typeof(string))]
    internal static class Agent_SetupAgentStats_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { BehaviorRuntime.ApplyAgentSetup(__instance); }
            catch (Exception e) { BehaviorRuntime.LogOnce(__instance, "setup", e); }
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_Patch
    {
        // BrainUpdate.agent is private and Mono enforces field access, so it must come in through Harmony's ___agent.
        private static void Postfix(Agent ___agent)
        {
            try { BehaviorRuntime.Tick(___agent); }
            catch (Exception e) { BehaviorRuntime.LogOnce(___agent, "tick", e); }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetTraversable), typeof(string))]
    internal static class Agent_SetTraversable_Patch
    {
        private static void Prefix(Agent __instance, ref string type)
        {
            if (AgentTraits.Has(__instance, "Accident_Prone") && (type == "" || type == "Normal" || type == "AvoidFireSpewer" || type == "NoLasers"))
                type = "TraverseAll";
        }
    }
}
