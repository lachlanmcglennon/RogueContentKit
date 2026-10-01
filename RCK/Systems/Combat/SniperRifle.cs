using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK.Combat
{
    /// <summary>
    ///   Makes the vanilla Sniper Rifle fire. The game defines the item (sprite, name, 10 rounds, the No Guns swap) and
    ///   campaigns, loot and RCK merchants hand it out, but <c>Gun.Shoot</c> has no case for it: it played the shoot
    ///   animation and fired nothing, for players and NPCs alike. RCK fires it after the vanilla body, the same way the
    ///   game fires its other bullet guns, with a heavy, slow round.
    /// </summary>
    internal static class SniperRifle
    {
        internal const string ItemName = "SniperRifle";

        /// <summary>Base damage before the shooter's accuracy scaling (the Revolver's is 16).</summary>
        internal const int Damage = 45;

        /// <summary>Seconds between shots before the vanilla accuracy and Rate of Fire Mod changes (the Revolver's is 0.79).</summary>
        internal const float Cooldown = 1.5f;

        /// <summary>The shot is aimed as if the shooter's Accuracy were this much higher (spread, NPC hit chance, auto-aim).</summary>
        internal const int AimBonus = 1;

        /// <summary>Recoil on the shooter, the same as the Revolver's.</summary>
        internal const float Recoil = 30f;

        private static bool vanillaFires;
        private static bool reportedError;

        internal static void Initialize()
        {
            vanillaFires = VanillaHandlesSniperRifle();
            if (vanillaFires)
                Rck.Log.LogInfo("The game now fires the Sniper Rifle itself; RCK's Sniper Rifle fix is off.");
            try
            {
                RogueLibs.CreateCustomName(ItemName, NameTypes.Description, new CustomNameInfo
                {
                    English = "A bolt-action rifle. Every shot hits very hard, but you have to wait a moment between shots.",
                    Russian = "Винтовка с продольно-скользящим затвором. Каждый выстрел бьёт очень сильно, но между выстрелами приходится немного подождать.",
                    Chinese = "栓动步枪。每一枪都威力巨大，但两次射击之间需要稍等片刻。",
                });
            }
            catch (ArgumentException e) { Rck.Log.LogDebug($"Sniper Rifle description was already registered: {e.Message}"); }
        }

        // A future game update that adds the missing case would compare the item name in Gun.Shoot's switch.
        private static bool VanillaHandlesSniperRifle()
        {
            try
            {
                MethodInfo shoot = AccessTools.Method(typeof(Gun), nameof(Gun.Shoot),
                    new[] { typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string) });
                return PatchProcessor.GetOriginalInstructions(shoot)
                    .Any(i => i.opcode == OpCodes.Ldstr && ItemName.Equals(i.operand as string, StringComparison.Ordinal));
            }
            catch (Exception e)
            {
                Rck.Log.LogWarning($"Could not read Gun.Shoot, so the Sniper Rifle fix stays on: {e.Message}");
                return false;
            }
        }

        /// <summary>The Sniper Rifle the vanilla body is about to "fire", or null. Mirrors Shoot's own checks.</summary>
        internal static InvItem? Pending(Gun gun, bool specialAbility)
        {
            if (vanillaFires) return null;
            Agent agent = gun.agent;
            if (agent == null || agent.inventory == null) return null;
            if (GameController.gameController.testMe2 && agent.isPlayer == 0) return null;
            InvItem item = specialAbility ? agent.inventory.equippedSpecialAbility : agent.inventory.equippedWeapon;
            if (item == null || item.invItemName != ItemName) return null;
            return item.invItemCount > 0 || (!agent.localPlayer && agent.isPlayer != 0) ? item : null;
        }

        internal static void Fire(Gun gun, InvItem item, bool silenced, bool rubber, int bulletNetID)
        {
            try
            {
                FireUnsafe(gun, item, silenced, rubber, bulletNetID);
            }
            catch (Exception e)
            {
                if (reportedError) return;
                reportedError = true;
                Rck.Log.LogError($"Sniper Rifle shot failed for {gun.agent}: {e}");
            }
        }

        // The same steps as the game's Revolver case, with the Sniper Rifle's numbers. Remote machines replay the shot
        // through Gun.Shoot with the bullet's net ID, so this runs there too.
        private static void FireUnsafe(Gun gun, InvItem item, bool silenced, bool rubber, int bulletNetID)
        {
            Agent agent = gun.agent;
            GameController gc = GameController.gameController;

            Bullet bullet;
            int accuracy = agent.accuracyStatMod;
            agent.accuracyStatMod = accuracy + AimBonus;
            try { bullet = gun.spawnBullet(bulletStatus.Revolver, item, bulletNetID, false); }
            finally { agent.accuracyStatMod = accuracy; }
            if (bullet == null) return;
            bullet.damage = Damage;

            bool quiet = silenced || item.contents.Contains("Silencer");
            if (gc.serverPlayer) gc.spawnerMain.SpawnDanger(agent, "Minor", "Huge");
            gun.SetWeaponCooldown(Cooldown, item);
            gun.SubtractBullets(1, item);
            if (!quiet && gc.serverPlayer) gc.spawnerMain.SpawnNoise(agent.tr.position, 2f, null, null, agent);
            if (agent.isPlayer > 0 && agent.localPlayer) gc.ScreenBump(2f, 20, agent);
            if (quiet)
            {
                gc.audioHandler.Play(agent, "SilencedGun");
                bullet.silenced = true;
            }
            else
            {
                gc.audioHandler.Play(agent, "RevolverFire");
            }
            if (rubber || item.contents.Contains("RubberBulletsMod")) bullet.rubber = true;
            gc.alienFX.FireGun(agent);
            gc.playerControl.Vibrate(agent.isPlayer, 0.15f, 0.2f);

            if (!agent.statusEffects.hasTrait("KnockbackLess") && !agent.statusEffects.hasTrait("KnockbackLess2"))
            {
                Vector3 angles = bullet.tr.eulerAngles;
                agent.movement.KnockBackBullet(Quaternion.Euler(angles.x, angles.y, angles.z + 270f), Recoil, true, null);
            }
        }
    }

    [HarmonyPatch(typeof(Gun), nameof(Gun.Shoot), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string))]
    internal static class Gun_Shoot_SniperRiflePatch
    {
        private static void Prefix(Gun __instance, bool specialAbility, out InvItem? __state)
            => __state = SniperRifle.Pending(__instance, specialAbility);

        private static void Postfix(Gun __instance, bool silenced, bool rubber, int bulletNetID, InvItem? __state)
        {
            if (__state != null) SniperRifle.Fire(__instance, __state, silenced, rubber, bulletNetID);
        }
    }
}
