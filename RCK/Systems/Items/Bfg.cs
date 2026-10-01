using System;
using HarmonyLib;
using UnityEngine;

namespace RCK.Items
{
    /// <summary>
    ///   Makes the BFG fire. The game defines it (sprite, 3 rounds, heavy recoil) but <c>Gun.Shoot</c> has no case for
    ///   it. RCK fires a rocket that bursts in a Huge explosion, with a long wait between shots.
    /// </summary>
    internal static class Bfg
    {
        internal const string ItemName = "BFG";
        internal const float Cooldown = 1.2f;
        internal const float Recoil = 30f;
        internal const string ExplosionType = "Huge";

        internal static bool Enabled { get; private set; }
        private static bool reportedError;

        internal static void Initialize()
        {
            Enabled = !Vanilla.Mentions(AccessTools.Method(typeof(Gun), nameof(Gun.Shoot), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string) }), ItemName, "BFG fix");
            ItemText.Description(ItemName,
                "Fires a slow rocket that bursts in a huge explosion. Don't stand too close.",
                "Стреляет медленной ракетой, которая разрывается огромным взрывом. Не стойте слишком близко.",
                "发射一枚缓慢的火箭，爆炸威力巨大。别站得太近。");
        }

        internal static void Fire(Gun gun, InvItem item, bool silenced, int bulletNetID)
        {
            try
            {
                FireUnsafe(gun, item, silenced, bulletNetID);
            }
            catch (Exception e)
            {
                if (reportedError) return;
                reportedError = true;
                Rck.Log.LogError($"BFG shot failed for {gun.agent}: {e}");
            }
        }

        // The game's Rocket Launcher case with the BFG's numbers. The rocket's own explosion is upgraded to Huge below.
        private static void FireUnsafe(Gun gun, InvItem item, bool silenced, int bulletNetID)
        {
            Agent agent = gun.agent;
            GameController gc = GameController.gameController;
            Bullet bullet = gun.spawnBullet(bulletStatus.Rocket, item, bulletNetID, false);
            if (bullet == null) return;
            gc.spawnerMain.SpawnLightTemp(gun.tr.position, null, "GunFlash");
            if (gc.serverPlayer) gc.spawnerMain.SpawnDanger(agent, "Minor", "Huge");
            gun.SetWeaponCooldown(Cooldown, item);
            gun.SubtractBullets(1, item);
            bool quiet = silenced || item.contents.Contains("Silencer");
            if (!quiet && gc.serverPlayer) gc.spawnerMain.SpawnNoise(agent.tr.position, 2f, null, null, agent);
            if (agent.isPlayer > 0 && agent.localPlayer) gc.ScreenBump(3f, 40, agent);
            if (quiet)
            {
                gc.audioHandler.Play(agent, "SilencedGun");
                bullet.silenced = true;
            }
            else
            {
                gc.audioHandler.Play(agent, "RocketLauncherFire");
            }
            gc.alienFX.FireGun(agent);
            gc.playerControl.Vibrate(agent.isPlayer, 0.3f, 0.25f);

            if (!agent.statusEffects.hasTrait("KnockbackLess") && !agent.statusEffects.hasTrait("KnockbackLess2"))
            {
                Vector3 angles = bullet.tr.eulerAngles;
                agent.movement.KnockBackBullet(Quaternion.Euler(angles.x, angles.y, angles.z + 270f), Recoil, true, null);
            }
        }

        internal static bool IsBfgRocket(PlayfieldObject source)
            => Enabled && source is Bullet b && b.bulletType == bulletStatus.Rocket && b.gun != null && b.gun.invItemName == ItemName;
    }

    [HarmonyPatch(typeof(Gun), nameof(Gun.Shoot), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(string))]
    internal static class Gun_Shoot_BfgPatch
    {
        private static void Prefix(Gun __instance, bool specialAbility, out InvItem? __state)
            => __state = Bfg.Enabled ? CutGun.Pending(__instance, specialAbility, Bfg.ItemName) : null;

        private static void Postfix(Gun __instance, bool silenced, int bulletNetID, InvItem? __state)
        {
            if (__state != null) Bfg.Fire(__instance, __state, silenced, bulletNetID);
        }
    }

    // Every SpawnExplosion overload ends in this one. A BFG rocket's "Normal" burst becomes Huge on every machine.
    [HarmonyPatch(typeof(SpawnerMain), nameof(SpawnerMain.SpawnExplosion),
        typeof(PlayfieldObject), typeof(Vector3), typeof(string), typeof(bool), typeof(int), typeof(bool), typeof(bool),
        typeof(Agent), typeof(PlayfieldObject))]
    internal static class SpawnerMain_SpawnExplosion_BfgPatch
    {
        private static void Prefix(PlayfieldObject sourceObject, ref string explosionType)
        {
            if (explosionType == "Normal" && Bfg.IsBfgRocket(sourceObject)) explosionType = Bfg.ExplosionType;
        }
    }
}
