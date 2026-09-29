using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Combat
{
    public sealed class CombatModule : IRckModule
    {
        public string Name => "Combat";

        public void Initialize()
        {
            Rck.TraitAdded += static (agent, _, __) => CombatRuntime.ApplyAgentTraits(agent);
        }
    }

    internal static class CombatRuntime
    {
        internal sealed class LootState
        {
            public readonly List<Tuple<InvItem, bool>> Items = new List<Tuple<InvItem, bool>>();
        }

        private static bool redirectingGib;

        private static readonly string[] DrugWarriorOrder =
        {
            "An_Inimitable_Bulk", "Armor_Plated", "Berserker", "Colognier", "Confusionist", "Electrocutioner",
            "Fainting_Goat_Warrior", "Fireproofer", "Flasher", "Gambler", "Harshmellow", "Immortalish",
            "Invincibilist", "Invisibilist", "Maimer", "Numb_to_Pain", "Number_to_Pain", "Numbest_to_Pain",
            "Numbestest_to_Pain", "Recoverist", "Some_Bark", "Stimpacker", "Stimpackerer", "Sure_I_Can",
            "The_Impermanent_Hunk", "The_Last_Whiff", "Wildcard", "Suicide_Bomber_Normal", "Suicide_Bomber_Big",
            "Suicide_Bomber_Huge"
        };

        private static readonly Dictionary<string, string> DrugStatuses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["An_Inimitable_Bulk"] = "Giant",
            ["Armor_Plated"] = "ResistBullets",
            ["Berserker"] = "Enraged",
            ["Colognier"] = "NiceSmelling",
            ["Confusionist"] = "Confused",
            ["Electrocutioner"] = "ElectroTouch",
            ["Fainting_Goat_Warrior"] = "Tranquilized",
            ["Fireproofer"] = "ResistFire",
            ["Flasher"] = "Fast",
            ["Gambler"] = "FeelingLucky",
            ["Harshmellow"] = "Withdrawal",
            ["Immortalish"] = "Resurrection",
            ["Invincibilist"] = "Invincible",
            ["Invisibilist"] = "Invisible",
            ["Maimer"] = "AlwaysCrit",
            ["Numb_to_Pain"] = "ResistDamageSmall",
            ["Number_to_Pain"] = "ResistDamageMed",
            ["Numbest_to_Pain"] = "ResistDamageLarge",
            ["Numbestest_to_Pain"] = "NumbToPain",
            ["Recoverist"] = "BlockDebuffs",
            ["Some_Bark"] = "Loud",
            ["Stimpacker"] = "RegenerateHealthWhenLow",
            ["Stimpackerer"] = "RegenerateHealthWhenLow2",
            ["Sure_I_Can"] = "KillerThrower",
            ["The_Impermanent_Hunk"] = "Strength",
            ["The_Last_Whiff"] = "Nicotine",
        };

        private static readonly string[] DeathExplosionOrder =
        {
            "Normal", "Big", "Huge", "Ridiculous", "EMP", "Dizzy_EOD", "Firebomb", "Warp", "Water", "Slime",
            "Ooze", "Stomp", "Noise_Only", "Oil_Spill"
        };

        private static readonly Dictionary<string, string> DeathExplosions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Normal"] = "Normal",
            ["Big"] = "Big",
            ["Huge"] = "Huge",
            ["Ridiculous"] = "Ridiculous",
            ["EMP"] = "EMP",
            ["Dizzy_EOD"] = "Dizzy",
            ["Firebomb"] = "FireBomb",
            ["Warp"] = "Warp",
            ["Water"] = "Water",
            ["Slime"] = "Slime",
            ["Ooze"] = "Ooze",
            ["Stomp"] = "Stomp",
            ["Noise_Only"] = "Noise",
            ["Oil_Spill"] = "Oil",
        };

        private static readonly Dictionary<string, string> PermanentStatuses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Above_the_Laws_d"] = "AboveTheLaw",
            ["Conductive_d"] = "ElectroTouch",
            ["Critter_Hitter_d"] = "AlwaysCrit",
            ["Desecondive_d"] = "DecreaseAllStats",
            ["Dying_d"] = "Cyanide",
            ["Enfastened_d"] = "Fast",
            ["Enstrongened_d"] = "Strength",
            ["Gigantic_d"] = "Giant",
            ["Invisibility_Enjoyer_d"] = "Invisible",
            ["Killer_Throwerer_d"] = "KillerThrower",
            ["Lucky_Duck_d"] = "FeelingLucky",
            ["LyCANthrope_d"] = "WerewolfEffect",
            ["Ragestart_d"] = "Enraged",
            ["Regenerationist_d"] = "RegenerateHealth",
            ["Slothful_d"] = "Slow",
            ["Strong_Immune_System_d"] = "BlockDebuffs",
            ["The_Invincibility_Gambit_d"] = "Invincible",
            ["Thick_Skin_d"] = "ResistDamageSmall",
            ["Thicker_Skin_d"] = "ResistDamageMed",
            ["Thickest_Skin_d"] = "ResistDamageLarge",
            ["Thickester_Skin_d"] = "NumbToPain",
            ["Undying_d"] = "Resurrection",
            ["Unlucky_Duck_d"] = "FeelingUnlucky",
        };

        // Permanent designer traits that map to vanilla traits rather than status effects. Adding them as a
        // 9999 s status effect would work (AddStatusEffect calls AddTrait) but they would expire, be purged
        // with other effects, and do nothing if StatusEffectList is not ready yet.
        private static readonly Dictionary<string, string> PermanentTraits = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Crusty"] = "UpperCrusty",
            ["Bulletproofish_d"] = "ResistBulletsSmall",
        };

        public static void ApplyAgentTraits(Agent agent)
        {
            if (agent?.statusEffects == null) return;

            if (AgentTraits.Has(agent, "Melee_Adept")) agent.modMeleeSkill = 2;
            else if (AgentTraits.Has(agent, "Melee_Competent")) agent.modMeleeSkill = 1;
            else if (AgentTraits.Has(agent, "Melee_Shy")) agent.modMeleeSkill = 0;

            if (AgentTraits.Has(agent, "Gun_Adept")) agent.modGunSkill = 2;
            else if (AgentTraits.Has(agent, "Gun_Competent")) agent.modGunSkill = 1;
            else if (AgentTraits.Has(agent, "Gun_Shy")) agent.modGunSkill = 0;

            if (AgentTraits.Has(agent, "Toughest")) agent.modToughness = 3;
            else if (AgentTraits.Has(agent, "Tougher")) agent.modToughness = 2;
            else if (AgentTraits.Has(agent, "Tough")) agent.modToughness = 1;

            if (AgentTraits.Has(agent, "Coward")) agent.mustFlee = true;
            if (AgentTraits.Has(agent, "Fearless"))
            {
                agent.wontFlee = true;
                agent.mustFlee = false;
            }

            foreach (KeyValuePair<string, string> pair in PermanentTraits)
            {
                if (AgentTraits.Has(agent, pair.Key)) agent.statusEffects.AddTrait(pair.Value);
            }
            if (AgentTraits.Has(agent, "Guilty"))
            {
                agent.oma.mustBeGuilty = true;
                agent.oma.mustBeInnocent = false;
            }
            if (AgentTraits.Has(agent, "Innocent"))
            {
                agent.oma.mustBeInnocent = true;
                agent.oma.mustBeGuilty = false;
            }
            if (AgentTraits.Has(agent, "Indomitable")) agent.preventsMindControl = true;
            if (AgentTraits.Has(agent, "Status_Effect_Immune")) agent.preventStatusEffects = true;
            if (AgentTraits.Has(agent, "Possessed")) agent.shapeShifter = true;
            if (AgentTraits.Has(agent, "Z_Infected")) agent.zombieWhenDead = true;
            if (AgentTraits.Has(agent, "Not_Vincible")) AddPermanentStatus(agent, "Invincible");
            if (AgentTraits.Has(agent, "Ghost")) MakeGhost(agent);

            ApplyPermanentStatuses(agent);
        }

        public static void ApplyPermanentStatuses(Agent agent)
        {
            if (agent?.statusEffects == null || agent.dead) return;
            foreach (KeyValuePair<string, string> pair in PermanentStatuses)
            {
                if (AgentTraits.Has(agent, pair.Key)) AddPermanentStatus(agent, pair.Value);
            }
        }

        private static void AddPermanentStatus(Agent agent, string status)
        {
            if (agent.statusEffects.hasStatusEffect(status)) return;
            agent.statusEffects.AddStatusEffect(status, showText: false, dontPrevent: true, specificTime: 9999);
        }

        public static bool IsProtectedPermanentStatus(Agent agent, string status)
        {
            if (agent == null || agent.dead) return false;
            foreach (KeyValuePair<string, string> pair in PermanentStatuses)
                if (pair.Value == status && AgentTraits.Has(agent, pair.Key))
                    return true;
            return status == "Invincible" && AgentTraits.Has(agent, "Not_Vincible");
        }

        private static void MakeGhost(Agent agent)
        {
            agent.ghost = true;
            agent.oma.ghost = true;
            agent.preventsMindControl = true;
            agent.cantChallengeToFight = true;
            if (agent.agentItemColliderTr != null) agent.agentItemColliderTr.gameObject.SetActive(false);
            if (agent.objectSprite != null) agent.objectSprite.agentColorDirty = true;
        }

        public static void TryApplyDrugWarrior(GoalBattle goal)
        {
            Agent agent = goal.agent;
            if (agent?.statusEffects == null || agent.dead || agent.oma.combatTookDrugs) return;
            if (goal.battlingAgent != null && goal.battlingAgent.objectAgent) return;

            string? trait = FirstTrait(agent, DrugWarriorOrder);
            if (trait == null) return;

            agent.oma.combatTookDrugs = true;
            bool quiet = AgentTraits.Has(agent, "Suppress_Syringe_AV");

            if (trait == "Suicide_Bomber_Normal" || trait == "Suicide_Bomber_Big" || trait == "Suicide_Bomber_Huge")
            {
                string explosion = trait == "Suicide_Bomber_Big" ? "Big" : trait == "Suicide_Bomber_Huge" ? "Huge" : "Normal";
                agent.StartCoroutine(SuicideBomb(agent, explosion));
            }
            else
            {
                string status = trait == "Wildcard" ? agent.statusEffects.ChooseRandomDrugDealerStatusEffect() : DrugStatuses[trait];
                int specificTime = AgentTraits.Has(agent, "Eternal_Release") ? 9999 : AgentTraits.Has(agent, "Extended_Release") ? 90 : -1;
                agent.statusEffects.AddStatusEffect(status, !quiet, dontPrevent: true, specificTime);
            }

            if (!quiet)
            {
                Rck.gc.spawnerMain.SpawnStatusText(agent, "UseItem", "Syringe", "Item");
                Rck.gc.audioHandler.Play(agent, "UseSyringe");
            }
        }

        private static IEnumerator SuicideBomb(Agent agent, string explosionType)
        {
            if (agent.objectSprite != null) agent.objectSprite.flashingRepeatedly = true;
            yield return new WaitForSeconds(ComputeFuse(agent));
            if (agent == null || agent.dead || agent.disappeared) yield break;
            Rck.gc.spawnerMain.SpawnExplosion(agent, agent.tr.position, explosionType, immediateHit: true, -1, hitMultPlayer: false, mustSpawnOnClients: true);
            if (!agent.dead) agent.statusEffects.ChangeHealth(-9999f, agent);
        }

        public static void FlashIfExplosive(Agent agent)
        {
            if (GetDeathExplosion(agent) == null) return;
            if (agent.objectSprite != null) agent.objectSprite.flashingRepeatedly = true;
        }

        public static void StartDeathExplosion(StatusEffects effects, PlayfieldObject damagerObject)
        {
            Agent agent = effects.agent;
            string? explosionType = GetDeathExplosion(agent);
            if (explosionType == null || agent.resurrect || agent.FellInHole()) return;
            if (!Rck.gc.serverPlayer && !agent.localPlayer) return;
            if (!PendingDeathExplosions.Add(agent)) return;
            // Resolve the killer now: bullets and other damagers are pooled and may be reused before the fuse ends.
            Agent? killer = damagerObject != null ? Rck.gc.spawnerMain.FindSourceAgent(damagerObject) : null;
            RckPlugin.Instance.StartCoroutine(DeathExplosion(agent, killer, explosionType, ComputeFuse(agent)));
        }

        // Bodies with a death explosion still to come. One explosion per death, however often the body is hit.
        private static readonly HashSet<Agent> PendingDeathExplosions = new HashSet<Agent>();

        // The fuse runs on the plugin object, not on the victim. A coroutine on the victim's StatusEffects stops when the
        // body is reset, pooled or switched off, and a long fuse (Cinematic_Fuse is 9 s) then never went off.
        // Vanilla gibs a body killed with 20+ overkill damage (StatusEffects.NormalGib -> Disappear), which sets
        // agent.disappeared. The explosion still happens, at the last place the body was seen.
        private static IEnumerator DeathExplosion(Agent agent, Agent? killer, string explosionType, float fuse)
        {
            GameController gc = Rck.gc;
            int level = gc.sessionDataBig.curLevelEndless;
            int uid = agent.UID;
            Vector3 pos = agent.tr.position;
            try
            {
                // Game time, like WaitForSeconds: the fuse pauses with the game and slows with slow motion.
                float elapsed = 0f;
                while (true)
                {
                    if (gc == null || gc.levelEnded || gc.sessionDataBig.curLevelEndless != level) yield break;
                    if (BodyStillThere(agent, uid)) pos = agent.tr.position;
                    if (elapsed >= fuse) break;
                    yield return null;
                    elapsed += Time.deltaTime;
                }

                Agent? body = agent != null && agent.UID == uid ? agent : null;
                if (explosionType == "Noise")
                {
                    gc.spawnerMain.SpawnNoise(pos, 1f, body, "Explosion", body);
                    if (body != null) gc.audioHandler.Play(body, "ObjectDestroy");
                }
                else if (explosionType == "Oil")
                {
                    gc.tileInfo.SpillLiquidLarge(pos, "Oil", sendToClients: true, expandLevel: 0, canSpillIndoors: true);
                    gc.spawnerMain.SpawnParticleEffect("ObjectDestroyedSmoke", pos, 0f);
                    if (body != null) gc.audioHandler.Play(body, "ObjectDestroy");
                }
                else
                {
                    PlayfieldObject? source = killer != null ? killer : body;
                    gc.spawnerMain.SpawnExplosion(source, pos, explosionType, immediateHit: false, -1, hitMultPlayer: false, mustSpawnOnClients: true);
                }
            }
            finally
            {
                PendingDeathExplosions.Remove(agent);
            }
        }

        private static bool BodyStillThere(Agent agent, int uid)
            => agent != null && agent.UID == uid && !agent.disappeared && agent.tr != null && agent.gameObject.activeInHierarchy;

        private static string? GetDeathExplosion(Agent agent)
        {
            if (agent == null) return null;
            foreach (string trait in DeathExplosionOrder)
                if (AgentTraits.Has(agent, trait))
                    return DeathExplosions[trait];
            return null;
        }

        private static float ComputeFuse(Agent agent)
        {
            float fuse = 1.5f;
            if (AgentTraits.Has(agent, "Long_Fuse")) fuse *= 2f;
            if (AgentTraits.Has(agent, "Longer_Fuse")) fuse *= 3f;
            if (AgentTraits.Has(agent, "Longest_Fuse")) fuse *= 4f;
            if (AgentTraits.Has(agent, "Cinematic_Fuse")) fuse *= 6f;
            if (AgentTraits.Has(agent, "Short_Fuse")) fuse *= 0.66f;
            if (AgentTraits.Has(agent, "Shorter_Fuse")) fuse *= 0.33f;
            if (AgentTraits.Has(agent, "Shortest_Fuse")) fuse = 0f;
            return Mathf.Max(0f, fuse);
        }

        public static bool HandleGib(StatusEffects effects, string vanillaKind)
        {
            if (redirectingGib) return true;
            Agent agent = effects.agent;
            if (agent == null) return true;
            if (effects.slaveHelmetGonnaBlow) return true;

            if (AgentTraits.Has(agent, "Indestructible")) return false;
            if (AgentTraits.Has(agent, "Gibless"))
            {
                if ((Rck.gc.serverPlayer && !agent.disappeared) || (!Rck.gc.serverPlayer && !agent.gibbed && !agent.fellInHole))
                {
                    agent.gibbed = true;
                    effects.Disappear();
                }
                return false;
            }

            string target = GetGibKind(agent);
            if (target == vanillaKind || target == "Default") return true;

            redirectingGib = true;
            try
            {
                switch (target)
                {
                    case "Meat": effects.NormalGib(); break;
                    case "Ice": effects.IceGib(); break;
                    case "Ghost": effects.GhostGib(); break;
                    case "Rock": CustomRockGib(effects); break;
                    case "Leaves": CustomLeafGib(effects); break;
                    default: return true;
                }
            }
            finally
            {
                redirectingGib = false;
            }
            return false;
        }

        private static string GetGibKind(Agent agent)
        {
            if (AgentTraits.Has(agent, "Meat_Chunks")) return "Meat";
            if (AgentTraits.Has(agent, "Ice_Shards") || AgentTraits.Has(agent, "Glass_Shards")) return "Ice";
            if (AgentTraits.Has(agent, "Ectoplasm")) return "Ghost";
            if (AgentTraits.Has(agent, "Golemite")) return "Rock";
            if (AgentTraits.Has(agent, "Leaves")) return "Leaves";
            return "Default";
        }

        private static bool CanCustomGib(Agent agent)
            => (Rck.gc.serverPlayer && !agent.disappeared) || (!Rck.gc.serverPlayer && !agent.gibbed && !agent.fellInHole);

        private static void CustomRockGib(StatusEffects effects)
        {
            Agent agent = effects.agent;
            if (!CanCustomGib(agent)) return;
            agent.gibbed = true;
            effects.Disappear();
            InvItem rock = new InvItem { invItemName = "Rock" };
            rock.SetupDetails(notNew: false);
            for (int i = 0; i < 5; i++) Rck.gc.spawnerMain.SpawnWreckage(agent.tr.position, rock, agent, null, burntWall: false);
            Rck.gc.spawnerMain.SpawnParticleEffect("ObjectDestroyedSmoke", agent.tr.position, 0f);
            Rck.gc.audioHandler.Play(agent, "WallDestroy");
        }

        private static void CustomLeafGib(StatusEffects effects)
        {
            Agent agent = effects.agent;
            if (!CanCustomGib(agent)) return;
            agent.gibbed = true;
            effects.Disappear();
            Rck.gc.spawnerMain.SpawnParticleEffect("ObjectDestroyedSmoke", agent.tr.position, 0f);
            Rck.gc.audioHandler.Play(agent, "BushDestroy");
        }

        public static LootState? SuppressLoot(Agent agent)
        {
            if (agent?.inventory?.InvItemList == null) return null;
            if (!AgentTraits.HasAny(agent, "Blurse_of_Midas", "Blurse_of_Softlock", "Blurse_of_the_Pharoah", "Blurse_of_Valhalla")) return null;

            var state = new LootState();
            foreach (InvItem item in agent.inventory.InvItemList)
            {
                if (item?.invItemName == null || !item.doSpill || !ShouldSuppressLoot(agent, item)) continue;
                state.Items.Add(Tuple.Create(item, item.doSpill));
                item.doSpill = false;
            }
            return state;
        }

        public static void RestoreLoot(LootState? state)
        {
            if (state == null) return;
            foreach (Tuple<InvItem, bool> entry in state.Items) entry.Item1.doSpill = entry.Item2;
        }

        private static bool ShouldSuppressLoot(Agent agent, InvItem item)
        {
            if (AgentTraits.Has(agent, "Blurse_of_Midas") && item.invItemName == "Money") return true;
            if (AgentTraits.Has(agent, "Blurse_of_Softlock") && IsImportantItem(item)) return true;
            bool equippable = item.isWeapon || item.isArmor || item.isArmorHead || item.itemType == "WeaponMelee" || item.itemType == "WeaponProjectile";
            if (AgentTraits.Has(agent, "Blurse_of_the_Pharoah") && !equippable) return true;
            if (AgentTraits.Has(agent, "Blurse_of_Valhalla") && equippable) return true;
            return false;
        }

        private static bool IsImportantItem(InvItem item)
        {
            string name = item.invItemName ?? "";
            return item.questItem
                || name.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Evidence", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Mayor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void AdjustKnockoutDamage(StatusEffects effects, ref float healthNum, PlayfieldObject damagerObject, ref byte extraVar)
        {
            if (effects?.agent == null || healthNum >= 0f) return;
            Agent target = effects.agent;

            if (damagerObject?.isBullet == true && damagerObject.playfieldObjectBullet != null && damagerObject.playfieldObjectBullet.rubber)
            {
                if (target.electronic)
                {
                    damagerObject.playfieldObjectBullet.rubber = false;
                    return;
                }

                float finalHealth = target.health + healthNum;
                if (finalHealth <= -target.healthMax * 0.1f)
                {
                    damagerObject.playfieldObjectBullet.rubber = false;
                }
                else if (finalHealth <= target.healthMax * 0.1f)
                {
                    extraVar = 2;
                    healthNum = -Mathf.Max(target.health, 1f);
                }
                return;
            }

            Agent attacker = damagerObject as Agent ?? target.justHitByAgent2;
            if (attacker == null || target.electronic || attacker.inventory?.equippedWeapon == null) return;
            string weapon = attacker.inventory.equippedWeapon.invItemName ?? "";
            if (weapon.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) < 0) return;

            float after = target.health + healthNum;
            bool plus = AgentTraits.Has(attacker, "Always_Baton_Red_2");
            bool normal = AgentTraits.Has(attacker, "Always_Baton_Red");
            if ((plus && after <= target.healthMax * 0.1f) || (normal && after <= 0f))
            {
                extraVar = 2;
                healthNum = -Mathf.Max(target.health, 1f);
            }
        }

        public static void ApplyDamageSideEffects(StatusEffects effects, float healthNum, PlayfieldObject damagerObject)
        {
            if (effects?.agent == null || healthNum >= 0f) return;
            Agent attacker = damagerObject as Agent ?? effects.agent.justHitByAgent2;
            if (attacker != null && AgentTraits.Has(attacker, "Z_Infectious") && !effects.agent.inhuman && !effects.agent.electronic)
                effects.agent.zombieWhenDead = true;
        }

        public static void TriggerPlotCritical(Agent agent)
        {
            if (agent == null || !AgentTraits.Has(agent, "Plot_Critical") || !Rck.gc.serverPlayer) return;
            foreach (Agent player in Rck.gc.playerAgentList)
            {
                if (player == null || player.dead || player.ghost) continue;
                Rck.gc.spawnerMain.SpawnExplosion(agent, player.tr.position, "Ridiculous", immediateHit: true, -1, hitMultPlayer: false, mustSpawnOnClients: true);
            }
        }

        public static void KnockOutIfSceneSetter(Agent agent)
        {
            if (agent == null) return;
            if (agent.defaultGoal == "KnockedOut" || agent.defaultGoal == "Knocked Out")
                agent.StartCoroutine(KnockOutNextTick(agent));
        }

        private static IEnumerator KnockOutNextTick(Agent agent)
        {
            yield return null;
            if (agent == null || agent.dead || agent.KnockedOut() || agent.electronic) yield break;
            agent.statusEffects.ChangeHealth(-Mathf.Max(agent.health, 1f), agent, 0u, -999f, "KnockedOut", 2);
        }

        public static bool HasInfiniteAmmoFor(InvDatabase db, InvItem item)
        {
            if (db?.agent == null || item == null || !AgentTraits.Has(db.agent, "Infinite_Ammo")) return false;
            return item.itemType == "WeaponProjectile" || item.weaponCode == weaponType.WeaponProjectile || item.invItemName.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? FirstTrait(Agent agent, IEnumerable<string> traits)
        {
            foreach (string trait in traits)
                if (AgentTraits.Has(agent, trait))
                    return trait;
            return null;
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetupAgentStats), typeof(string))]
    internal static class Agent_SetupAgentStats_CombatPatch
    {
        private static void Postfix(Agent __instance)
        {
            CombatRuntime.ApplyAgentTraits(__instance);
            CombatRuntime.KnockOutIfSceneSetter(__instance);
        }
    }

    [HarmonyPatch(typeof(GoalBattle), nameof(GoalBattle.Activate))]
    internal static class GoalBattle_Activate_DrugWarriorPatch
    {
        private static void Postfix(GoalBattle __instance) => CombatRuntime.TryApplyDrugWarrior(__instance);
    }

    [HarmonyPatch(typeof(global::Combat), nameof(global::Combat.DoRapidFire))]
    internal static class Combat_DoRapidFire_MagDumperPatch
    {
        private static void Postfix(global::Combat __instance, Agent ___agent)
        {
            if (AgentTraits.Has(___agent, "Mag_Dumper"))
                __instance.rapidFireTime = Mathf.Max(__instance.rapidFireTime * 6f, 3f);
        }
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.SetupDeath), typeof(PlayfieldObject), typeof(bool), typeof(bool))]
    internal static class StatusEffects_SetupDeath_CombatPatch
    {
        private static void Prefix(StatusEffects __instance, out CombatRuntime.LootState? __state)
        {
            CombatRuntime.FlashIfExplosive(__instance.agent);
            __state = CombatRuntime.SuppressLoot(__instance.agent);
        }

        private static void Postfix(StatusEffects __instance, PlayfieldObject damagerObject, CombatRuntime.LootState? __state)
        {
            CombatRuntime.RestoreLoot(__state);
            CombatRuntime.StartDeathExplosion(__instance, damagerObject);
            CombatRuntime.TriggerPlotCritical(__instance.agent);
        }
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.NormalGib))]
    internal static class StatusEffects_NormalGib_CombatPatch
    {
        private static bool Prefix(StatusEffects __instance) => CombatRuntime.HandleGib(__instance, "Normal");
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.IceGib))]
    internal static class StatusEffects_IceGib_CombatPatch
    {
        private static bool Prefix(StatusEffects __instance) => CombatRuntime.HandleGib(__instance, "Ice");
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.GhostGib))]
    internal static class StatusEffects_GhostGib_CombatPatch
    {
        private static bool Prefix(StatusEffects __instance) => CombatRuntime.HandleGib(__instance, "Ghost");
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.RemoveStatusEffect), typeof(string), typeof(bool), typeof(uint), typeof(bool))]
    internal static class StatusEffects_RemoveStatusEffect_PermanentPatch
    {
        private static bool Prefix(StatusEffects __instance, string statusEffectName)
            => __instance.removingAllStatusEffects || !CombatRuntime.IsProtectedPermanentStatus(__instance.agent, statusEffectName);
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.RemoveAllStatusEffects), typeof(bool))]
    internal static class StatusEffects_RemoveAllStatusEffects_PermanentPatch
    {
        private static void Postfix(StatusEffects __instance) => CombatRuntime.ApplyPermanentStatuses(__instance.agent);
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.RemoveAllStatusEffectsNotBetweenLevels))]
    internal static class StatusEffects_RemoveAllStatusEffectsNotBetweenLevels_PermanentPatch
    {
        private static void Postfix(StatusEffects __instance) => CombatRuntime.ApplyPermanentStatuses(__instance.agent);
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.ChangeHealth), typeof(float), typeof(PlayfieldObject), typeof(uint), typeof(float), typeof(string), typeof(byte))]
    internal static class StatusEffects_ChangeHealth_KnockoutPatch
    {
        private static void Prefix(StatusEffects __instance, ref float healthNum, PlayfieldObject damagerObject, ref byte extraVar)
            => CombatRuntime.AdjustKnockoutDamage(__instance, ref healthNum, damagerObject, ref extraVar);

        private static void Postfix(StatusEffects __instance, float healthNum, PlayfieldObject damagerObject)
            => CombatRuntime.ApplyDamageSideEffects(__instance, healthNum, damagerObject);
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.SubtractFromItemCount), typeof(InvItem), typeof(int), typeof(bool))]
    internal static class InvDatabase_SubtractFromItemCount_InfiniteAmmoPatch
    {
        private static void Prefix(InvDatabase __instance, InvItem invItem, ref int amount)
        {
            if (CombatRuntime.HasInfiniteAmmoFor(__instance, invItem)) amount = 0;
        }
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.DepleteArmor), typeof(string), typeof(int))]
    internal static class InvDatabase_DepleteArmor_InfiniteArmorPatch
    {
        private static bool Prefix(InvDatabase __instance) => !AgentTraits.Has(__instance.agent, "Infinite_Armor");
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.DepleteMelee), typeof(int), typeof(InvItem))]
    internal static class InvDatabase_DepleteMelee_InfiniteMeleePatch
    {
        private static bool Prefix(InvDatabase __instance) => !AgentTraits.Has(__instance.agent, "Infinite_Melee");
    }

    [HarmonyPatch(typeof(Gun), nameof(Gun.Shoot), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string))]
    internal static class Gun_Shoot_RubberBulleteerPatch
    {
        private static void Prefix(Gun __instance, ref bool rubber)
        {
            if (AgentTraits.Has(__instance.agent, "Rubber_Bulleteer")) rubber = true;
        }
    }

    [HarmonyPatch(typeof(SpawnerMain), nameof(SpawnerMain.SpawnStatusText), typeof(PlayfieldObject), typeof(string), typeof(string), typeof(string), typeof(uint), typeof(string), typeof(string))]
    internal static class SpawnerMain_SpawnStatusText_SuppressPatch
    {
        private static bool Prefix(PlayfieldObject myPlayfieldObject, ref StatusText __result)
        {
            Agent? agent = myPlayfieldObject as Agent;
            if (agent != null && AgentTraits.Has(agent, "Suppress_Status_Text"))
            {
                __result = default!;
                return false;
            }
            return true;
        }
    }
}
