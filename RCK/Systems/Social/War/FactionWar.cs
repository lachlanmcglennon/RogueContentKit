#nullable disable
using System;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The faction war's clock: once a second on the server, after the level loads, it prunes squads, checks turfs
    ///   (see <see cref="TurfWar"/>), launches raids that are due (see <see cref="FactionRaids"/>), follows up squad
    ///   orders (see <see cref="SquadOrders"/>), sends expansion parties (see <see cref="TurfExpansion"/>), pays
    ///   control points (see <see cref="ControlPoints"/>), runs the player's command (see <see cref="Command"/>),
    ///   respawns factions (see <see cref="FactionRespawn"/>) and lets medics heal (see <see cref="FactionMedic"/>).
    ///   Each part stops on its own if it throws, so one failure doesn't take the others down.
    /// </summary>
    internal static class FactionWar
    {
        private const float TickSeconds = 1f;

        private static float nextTick;
        private static int stamp = int.MinValue;
        private static float startedAt;
        private static bool squadsBroken, turfBroken, raidsBroken, respawnBroken, ordersBroken, expansionBroken, pointsBroken, commandBroken, medicBroken;

        internal static void Tick()
        {
            float now = Time.time;
            if (!Interval.Due(ref nextTick, now, TickSeconds)) return;
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete) return;
            if (LevelScope.IsNew(ref stamp)) startedAt = now;
            float levelTime = now - startedAt;

            if (!squadsBroken)
            {
                try { Squads.Prune(); }
                catch (Exception e) { squadsBroken = Fail("squads", e); }
            }
            if (!turfBroken)
            {
                try { TurfWar.Tick(now, levelTime); }
                catch (Exception e) { turfBroken = Fail("turf capture", e); }
            }
            if (!raidsBroken)
            {
                try { FactionRaids.Tick(gc, levelTime); }
                catch (Exception e) { raidsBroken = Fail("raids", e); }
            }
            if (!ordersBroken && !turfBroken)
            {
                try { SquadOrders.Tick(gc, now, levelTime); }
                catch (Exception e) { ordersBroken = Fail("squad orders", e); }
            }
            if (!expansionBroken && !turfBroken)
            {
                try { TurfExpansion.Tick(gc, levelTime); }
                catch (Exception e) { expansionBroken = Fail("turf expansion", e); }
            }
            if (!pointsBroken && !turfBroken)
            {
                try { ControlPoints.Tick(gc, levelTime); }
                catch (Exception e) { pointsBroken = Fail("control points", e); }
            }
            if (!commandBroken && !turfBroken)
            {
                try { Command.Tick(gc, levelTime); }
                catch (Exception e) { commandBroken = Fail("commander", e); }
            }
            if (!respawnBroken && !turfBroken)
            {
                try { FactionRespawn.Tick(gc, now, levelTime); }
                catch (Exception e) { respawnBroken = Fail("respawn", e); }
            }
            if (!medicBroken)
            {
                try { FactionMedic.Tick(gc, now); }
                catch (Exception e) { medicBroken = Fail("medics", e); }
            }
        }

        private static bool Fail(string what, Exception e)
        {
            Rck.Log.LogError($"Factions: {what} tick failed and stops until restart: {e}");
            return true;
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_FactionWar_Patch
    {
        private static bool broken;

        private static void Postfix()
        {
            if (broken) return;
            try { FactionWar.Tick(); }
            catch (Exception e)
            {
                broken = true;
                Rck.Log.LogError($"Factions: faction war tick failed, it stops until restart: {e}");
            }
        }
    }
}
