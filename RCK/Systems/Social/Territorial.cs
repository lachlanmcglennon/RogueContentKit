#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Territorial faction pairs, from <c>&lt;key&gt;_Territorial</c> traits or a matrix <c>=Territorial</c> entry. The
    ///   direction X→Y starts Annoyed and turns Hateful (hate 5) when X sees Y standing on X's turf: floor X owns
    ///   (<c>ownerID</c>, or any owned floor for owner 99) in X's starting chunk, not behind glass or on a wall, the same
    ///   test vanilla <c>ProtectOwned</c> uses. X with owner 0 (a street NPC) has no turf and stays Annoyed. Y is spared
    ///   when it lives there (same owner and chunk), is dead, an object agent, an empty mech, or holds a PropertyDeed.
    ///   Escalation is sticky for the level, like vanilla trespassing.
    /// </summary>
    internal static class Territorial
    {
        private const float SweepInterval = 0.5f;

        private sealed class Holder
        {
            public readonly HashSet<Agent> Targets = new HashSet<Agent>();
            public float NextSweep;
        }

        private static readonly HashSet<long> pairs = new HashSet<long>();
        private static readonly Dictionary<int, Holder> holders = new Dictionary<int, Holder>();
        private static readonly List<Agent> seen = new List<Agent>();
        private static int level = int.MinValue;

        /// <summary>True while any pair is registered, so the hooks cost one static read in levels without any.</summary>
        public static bool Active;

        private static long Key(Agent source, Agent target) => ((long)source.agentID << 32) | (uint)target.agentID;

        // Agent IDs restart each level, so the registry is per level. A pair set up again (the same level restarted,
        // or a new agent reusing an ID) is unregistered first by SocialRules.ApplyRelationshipRules, so an entry left
        // over from an earlier load never applies to a pair it wasn't decided for; clearing on a level change only
        // frees the memory.
        private static void CheckLevel()
        {
            GameController gc = GameController.gameController;
            int current = gc != null && gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 0;
            if (current == level) return;
            level = current;
            pairs.Clear();
            holders.Clear();
            Active = false;
        }

        /// <summary>Marks the direction <paramref name="source"/>→<paramref name="target"/> territorial (it was just set Annoyed).</summary>
        public static void Register(Agent source, Agent target)
        {
            CheckLevel();
            if (!pairs.Add(Key(source, target))) return;
            if (!holders.TryGetValue(source.agentID, out Holder h)) holders[source.agentID] = h = new Holder();
            h.Targets.Add(target);
            Active = true;
        }

        /// <summary>Forgets both directions of a pair, before its rules are applied again.</summary>
        public static void Unregister(Agent a, Agent b)
        {
            if (!Active) return;
            if (pairs.Remove(Key(a, b)) && holders.TryGetValue(a.agentID, out Holder ha)) ha.Targets.Remove(b);
            if (pairs.Remove(Key(b, a)) && holders.TryGetValue(b.agentID, out Holder hb)) hb.Targets.Remove(a);
        }

        public static bool IsTerritorial(Agent source, Agent target) => Active && pairs.Contains(Key(source, target));

        /// <summary>
        ///   The turf test from vanilla <c>ProtectOwned</c>, without its enforcer, firefighter and fire exceptions (a
        ///   Cop_Territorial hideout guard attacks a cop who walks in) and without its whole-map rule for levelShape 2.
        /// </summary>
        public static bool OnTurf(Agent holder, TileData tile)
            => tile != null && holder.ownerID != 0
            && (tile.owner == holder.ownerID || (holder.ownerID == 99 && tile.owner != 0))
            && tile.chunkID == holder.startingChunk
            && (tile.wallMaterial != wallMaterialType.Glass || tile.destroyedWall)
            && tile.wallSide == wallSideType.None;

        private static bool Exempt(Agent holder, Agent other)
            => (other.ownerID == holder.ownerID && other.startingChunk == holder.startingChunk)
            || other.dead || other.objectAgent || other.mechEmpty
            || (other.inventory != null && other.inventory.HasItem("PropertyDeed"));

        // The cached tile owner and chunk (updated every AI tick) rule out almost every pair before the tile is read.
        private static bool MaybeOnTurf(Agent holder, Agent other)
        {
            if (holder.ownerID == 0 || other.curChunk != holder.startingChunk) return false;
            int owner = other.curOwnerTile;
            return owner == holder.ownerID || (holder.ownerID == 99 && owner != 0);
        }

        /// <summary>Vanilla <c>LastSaw</c> just confirmed that <paramref name="holder"/> sees <paramref name="other"/>.</summary>
        internal static void OnSaw(Agent holder, Agent other, Relationship rel)
        {
            if (holder == null || other == null || rel == null || !rel.hasLOS || rel.relTypeCode != relStatus.Annoyed) return;
            if (!MaybeOnTurf(holder, other) || !pairs.Contains(Key(holder, other))) return;
            Escalate(holder, other, rel);
        }

        /// <summary>
        ///   Vanilla <c>LastSaw</c> skips its sight check while neither agent has moved or turned since the last AI tick,
        ///   so a trespasser standing still in front of a still holder would never be noticed. Twice a second, for such
        ///   pairs only, this does the same sight check itself (vanilla's vision cone, range and line of sight), with
        ///   the rest of <c>LastSaw</c>'s gates applied conservatively.
        /// </summary>
        internal static void Sweep(Agent holder)
        {
            if (holder == null || holder.ownerID == 0 || !holders.TryGetValue(holder.agentID, out Holder h)) return;
            float now = Time.time;
            if (now < h.NextSweep) return;
            h.NextSweep = now + SweepInterval;
            CheckLevel();
            if (!Active || h.Targets.Count == 0 || !holder.notMovedSinceLastAIUpdate || holder.dead || holder.brain == null || !holder.brain.active) return;
            if ((holder.oma.rioter && holder.isPlayer == 0) || holder.warZoneAgent || holder.zombified || holder.prisoner != 0) return;
            List<Relationship> rels = holder.relationships.RelList2;
            seen.Clear();
            foreach (Agent other in h.Targets)
            {
                if (other == null || !other.notMovedSinceLastAIUpdate || !MaybeOnTurf(holder, other)) continue;
                int id = other.agentID;
                if (id < 0 || id >= rels.Count || !pairs.Contains(Key(holder, other))) continue;
                Relationship rel = rels[id];
                if (rel != null && rel.relTypeCode == relStatus.Annoyed && CanSee(holder, other, rel)) seen.Add(other);
            }
            // Escalating runs vanilla relationship code, so it happens after the loop over Targets.
            foreach (Agent other in seen) Escalate(holder, other, rels[other.agentID]);
            seen.Clear();
        }

        private static bool CanSee(Agent holder, Agent other, Relationship rel)
        {
            if (other.dead || other.ghost || other.underBox || (other.invisible && !rel.sawBecomeHidden) || other.prisoner != 0) return false;
            if (other.warZoneAgent || (other.isPlayer == 0 && (other.oma.rioter || other.zombified || other.brain == null || !other.brain.active))) return false;
            if (!holder.movement.InVisionBoundsAgent(other)) return false;
            if (Vector2.Distance(holder.curPosition, other.curPosition) >= holder.LOSRange / other.hardToSeeFromDistance) return false;
            return holder.movement.HasLOSAgent(other);
        }

        private static void Escalate(Agent holder, Agent other, Relationship rel)
        {
            TileData tile = other.curTileData;
            if (!OnTurf(holder, tile) || Exempt(holder, other)) return;
            HostilityDiagnostics.ReportTerritorial(holder, other, rel, tile);
            HostilityDiagnostics.Suppress = true;
            try { holder.relationships.SetRelHate(other, 5); }
            finally { HostilityDiagnostics.Suppress = false; }
        }
    }

    // LastSaw is DeadBodyCheck's only caller. It calls it right after confirming line of sight and skipping rioters,
    // war-zone and zombified agents, so this postfix sees each sighting once and needs no LastSaw context (no prefix,
    // finalizer or thread-static on LastSaw, the hottest relationship method).
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.DeadBodyCheck), typeof(Agent), typeof(Relationship))]
    internal static class Relationships_DeadBodyCheck_Territorial_Patch
    {
        // Relationships.agent is private and Mono enforces field access, so it comes in through Harmony's ___agent.
        private static void Postfix(Agent ___agent, Agent otherAgent, Relationship myRelationship)
        {
            if (!Territorial.Active) return;
            try { Territorial.OnSaw(___agent, otherAgent, myRelationship); }
            catch (Exception e) { SocialRules.LogOnce(___agent, "territorial", e); }
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_Territorial_Patch
    {
        private static void Postfix(Agent ___agent)
        {
            if (!Territorial.Active) return;
            try { Territorial.Sweep(___agent); }
            catch (Exception e) { SocialRules.LogOnce(___agent, "territorial-sweep", e); }
        }
    }
}
