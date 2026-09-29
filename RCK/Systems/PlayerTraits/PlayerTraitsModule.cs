using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK.PlayerTraits
{
    public sealed class PlayerTraitsModule : IRckModule
    {
        private static readonly Dictionary<string, int> VanillaMaxAmmo = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<Agent, float> LastBlink = new Dictionary<Agent, float>();

        public string Name => "PlayerTraits";

        public void Initialize()
        {
            Rck.TraitAdded += static (agent, trait, _) =>
            {
                if (agent == null) return;
                ApplyAll(agent);
            };
            Rck.TraitRemoved += static (agent, trait, _) =>
            {
                if (agent == null) return;
                ApplyAll(agent);
            };
        }

        internal static void ApplyAll(Agent agent)
        {
            ApplyShadowTraits(agent);
            ApplyInventoryTraits(agent.inventory);
        }

        internal static void ApplyShadowTraits(Agent agent)
        {
            if (AgentTraits.Has(agent, "Immovable")) EnsureTrait(agent, "KnockbackLess2");
            if (AgentTraits.Has(agent, "Melee_Maniac_2")) EnsureTrait(agent, "FastMelee2");
            else if (AgentTraits.Has(agent, "Melee_Maniac")) EnsureTrait(agent, "FastMelee");
            if (AgentTraits.Has(agent, "Mapless")) EnsureTrait(agent, "CantTeleport");
        }

        internal static void ApplyInventoryTraits(InvDatabase inv)
        {
            if (inv?.agent == null || inv.InvItemList == null) return;
            foreach (InvItem item in inv.InvItemList)
            {
                ApplyWeaponTraits(inv.agent, item);
            }
        }

        internal static void ApplyWeaponTraits(Agent agent, InvItem item)
        {
            if (agent == null || item == null) return;

            if (item.itemType == ItemTypes.WeaponProjectile)
            {
                if (AgentTraits.Has(agent, "Accuracy_Modder")) AddContent(item, "AccuracyMod");
                if (AgentTraits.Has(agent, "Ammo_Stocker")) AddContent(item, "AmmoCapacityMod");
                if (AgentTraits.Has(agent, "Rate_of_Fire_Modder")) AddContent(item, "RateOfFireMod");
                if (AgentTraits.Has(agent, "Silencerist")) AddContent(item, "Silencer");
                if (AgentTraits.Has(agent, "Pants_on_Autofire")) item.rapidFire = true;
                ApplyAmmoCap(agent, item);
            }
            else if (item.itemType == ItemTypes.WeaponMelee && AgentTraits.Has(agent, "Remise_Beast"))
            {
                item.rapidFire = true;
            }
        }

        internal static void ApplyAmmoCap(Agent agent, InvItem item)
        {
            if (item.maxAmmo <= 0) return;
            float factor = 1f;
            if (AgentTraits.Has(agent, "Ammo_Auteur")) factor = 2f;
            else if (AgentTraits.Has(agent, "Ammo_Artiste")) factor = 1.5f;
            else if (AgentTraits.Has(agent, "Ammo_Amateur")) factor = 1.25f;
            if (factor <= 1f) return;

            int baseMax = GetVanillaMaxAmmo(item);
            int newMax = Mathf.Max(item.maxAmmo, Mathf.CeilToInt(baseMax * factor));
            int delta = newMax - item.maxAmmo;
            item.maxAmmo = newMax;
            if (delta > 0) item.invItemCount += delta;
        }

        private static int GetVanillaMaxAmmo(InvItem item)
        {
            if (VanillaMaxAmmo.TryGetValue(item.invItemName, out int cached)) return cached;
            int result = item.maxAmmo;
            try
            {
                InvItem probe = new InvItem { invItemName = item.invItemName };
                probe.SetupDetails(notNew: false);
                if (probe.maxAmmo > 0) result = probe.maxAmmo;
            }
            catch
            {
            }
            VanillaMaxAmmo[item.invItemName] = result;
            return result;
        }

        internal static bool TryBlink(Agent agent, float oldHealth)
        {
            if (!AgentTraits.Has(agent, "Blinker") || agent.dead || agent.health >= oldHealth) return false;
            float now = Time.time;
            if (LastBlink.TryGetValue(agent, out float last) && now - last < 1.5f) return false;
            LastBlink[agent] = now;

            Vector2 pos = GameController.gameController.tileInfo.FindLocationNearLocation(
                agent.tr.position, agent, 0.96f, 4f, accountForObstacles: true, notInside: false, dontCareAboutDanger: false, teleporting: true);
            agent.Teleport(new Vector3(pos.x, pos.y, agent.tr.position.z), bringOthers: false, immediate: true);
            return true;
        }

        internal static void ReduceArmorDamage(Agent agent, ref int amount)
        {
            if (amount <= 0) return;
            if (AgentTraits.Has(agent, "Myrmidon")) amount = Mathf.Max(1, Mathf.CeilToInt(amount * 0.25f));
            else if (AgentTraits.Has(agent, "Myrmicapo")) amount = Mathf.Max(1, Mathf.CeilToInt(amount * 0.5f));
        }

        internal static void ReduceGunCooldown(Gun gun)
        {
            Agent agent = gun.agent;
            if (agent == null) return;
            if (AgentTraits.Has(agent, "Trigger_Junkie")) agent.weaponCooldown = Mathf.Max(0.05f, agent.weaponCooldown * 0.6f);
            else if (AgentTraits.Has(agent, "Trigger_Happy")) agent.weaponCooldown = Mathf.Max(0.05f, agent.weaponCooldown * 0.8f);
        }

        internal static float AdjustKnockback(Agent agent, float value)
        {
            if (AgentTraits.Has(agent, "Immovable")) return 0f;
            if (AgentTraits.Has(agent, "Knockback_Peon")) return value * 0.5f;
            return value;
        }

        internal static bool BlocksMap()
        {
            GameController gc = GameController.gameController;
            foreach (Agent agent in gc.playerAgentList)
            {
                if (agent != null && agent.localPlayer && AgentTraits.Has(agent, "Mapless")) return true;
            }
            return false;
        }

        private static void EnsureTrait(Agent agent, string vanillaTrait)
        {
            if (!agent.statusEffects.hasTrait(vanillaTrait))
            {
                agent.statusEffects.AddTrait(vanillaTrait);
            }
        }

        private static void AddContent(InvItem item, string content)
        {
            item.contents ??= new List<string>();
            if (!item.contents.Contains(content)) item.contents.Add(content);
        }
    }

    [HarmonyPatch(typeof(Agent), "Start")]
    internal static class Agent_Start_PlayerTraits
    {
        private static void Postfix(Agent __instance)
        {
            PlayerTraitsModule.ApplyAll(__instance);
        }
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.DepleteArmor))]
    internal static class InvDatabase_DepleteArmor_PlayerTraits
    {
        private static void Prefix(InvDatabase __instance, ref int amount)
        {
            if (__instance.agent != null) PlayerTraitsModule.ReduceArmorDamage(__instance.agent, ref amount);
        }
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.AddItem), new[]
    {
        typeof(string), typeof(int), typeof(List<string>), typeof(List<int>), typeof(List<int>), typeof(int), typeof(bool),
        typeof(bool), typeof(int), typeof(int), typeof(bool), typeof(string), typeof(bool), typeof(int), typeof(bool),
        typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool)
    })]
    internal static class InvDatabase_AddItem_PlayerTraits
    {
        private static void Postfix(InvDatabase __instance, InvItem __result)
        {
            if (__instance.agent != null && __result != null)
                PlayerTraitsModule.ApplyWeaponTraits(__instance.agent, __result);
        }
    }

    [HarmonyPatch(typeof(Gun), nameof(Gun.SetWeaponCooldown))]
    internal static class Gun_SetWeaponCooldown_PlayerTraits
    {
        private static void Postfix(Gun __instance)
        {
            PlayerTraitsModule.ReduceGunCooldown(__instance);
        }
    }

    [HarmonyPatch(typeof(Movement), nameof(Movement.FindKnockBackStrength))]
    internal static class Movement_FindKnockBackStrength_PlayerTraits
    {
        private static void Postfix(Movement __instance, ref float __result)
        {
            Agent agent = (Agent)AccessTools.Field(typeof(Movement), "agent").GetValue(__instance);
            if (agent != null) __result = PlayerTraitsModule.AdjustKnockback(agent, __result);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Damage), new[] { typeof(PlayfieldObject), typeof(bool) })]
    internal static class Agent_Damage_PlayerTraits
    {
        private static void Prefix(Agent __instance, out float __state)
        {
            __state = __instance.health;
        }

        private static void Postfix(Agent __instance, float __state)
        {
            PlayerTraitsModule.TryBlink(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(MainGUI), nameof(MainGUI.ShowMinimap))]
    internal static class MainGUI_ShowMinimap_PlayerTraits
    {
        private static bool Prefix()
        {
            return !PlayerTraitsModule.BlocksMap();
        }
    }
}
