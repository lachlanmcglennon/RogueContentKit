#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    // Playtest diagnostics for NPCs that turn on a nearby player with no visible cause (a Probable Cause CCU
    // vendor went hostile when talked to). Each prefix logs the first escalation per NPC, player and source with
    // the NPC's state and a trimmed call stack. Remove once the cause is known.
    internal static class HostilityDiagnostics
    {
        private const int MaxPerLevel = 40;
        private const float MaxDistance = 5f;
        private const int MaxFrames = 14;

        private const int MaxTerritorialPerLevel = 40;

        private static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<long> territorialSeen = new HashSet<long>();
        private static int logged;
        private static bool failed;

        static HostilityDiagnostics() => LevelScope.Ended += Reset;

        private static void Reset()
        {
            logged = 0;
            seen.Clear();
            territorialSeen.Clear();
        }

        /// <summary>Set while RCK itself escalates a relationship it has already reported (Territorial, street innocence), so the hate prefixes stay quiet.</summary>
        internal static bool Suppress;

        public static bool Relevant(Relationships rels, Agent other, out Agent npc, out Relationship rel)
        {
            npc = null;
            rel = null;
            if (failed || Suppress || other == null || other.isPlayer == 0 || rels == null) return false;
            GameController gc = GameController.gameController;
            if (gc == null || !gc.loadComplete) return false;
            npc = rels.GetComponent<Agent>();
            if (npc == null || npc.isPlayer != 0 || npc.dead || npc.objectAgent) return false;
            if (npc.interactingAgent != other && Vector2.Distance(npc.curPosition, other.curPosition) > MaxDistance) return false;
            rel = RelOps.Of(rels, other);
            return rel != null;
        }

        public static void Report(Agent npc, Agent other, Relationship rel, string source, string change)
        {
            try
            {
                GameController gc = GameController.gameController;
                LevelScope.Check(gc);
                if (logged >= MaxPerLevel || !seen.Add(npc.agentID + ">" + other.agentID + ":" + source)) return;
                logged++;

                StringBuilder sb = new StringBuilder(512);
                sb.Append("[diag hostility] ").Append(source).Append(' ').Append(change)
                    .Append(" | npc=").Append(npc.agentName).Append(" '").Append(npc.agentRealName).Append("' id=").Append(npc.agentID)
                    .Append(" owner=").Append(npc.ownerID).Append(" chunk=").Append(npc.startingChunk)
                    .Append(" chunkDesc=").Append(npc.startingChunkRealDescription)
                    .Append(" protects=").Append(npc.oma != null ? npc.oma.modProtectsProperty : -1)
                    .Append(" gang=").Append(npc.gang).Append(" goal=").Append(npc.mostRecentGoalCode)
                    .Append(" | rel=").Append(rel.relType).Append(" hate=").Append(rel.relHate).Append(" strikes=").Append(rel.relStrikes)
                    .Append(" | player=").Append(other.agentName).Append(" talking=").Append(npc.interactingAgent == other)
                    .Append(" weapon=").Append(other.inventory != null && other.inventory.equippedWeapon != null ? other.inventory.equippedWeapon.invItemName : "none")
                    .Append(" dist=").Append(Vector2.Distance(npc.curPosition, other.curPosition).ToString("0.00"));
                if (gc.tileInfo != null)
                {
                    TileData tile = gc.tileInfo.GetTileData(other.curPosition);
                    if (tile != null) sb.Append(" playerTileOwner=").Append(tile.owner).Append(" playerTileChunk=").Append(tile.chunkID);
                }
                AppendStack(sb);
                Rck.Log.LogWarning(sb.ToString());
            }
            catch (Exception e)
            {
                failed = true;
                Rck.Log.LogError("Hostility diagnostics disabled: " + e);
            }
        }

        /// <summary>One line per pair and level when a Territorial holder turns Hateful on someone on its turf (any agents, not just players).</summary>
        public static void ReportTerritorial(Agent holder, Agent other, Relationship rel, TileData tile)
        {
            if (failed) return;
            try
            {
                LevelScope.Check();
                if (territorialSeen.Count >= MaxTerritorialPerLevel || !territorialSeen.Add(((long)holder.agentID << 32) | (uint)other.agentID)) return;
                StringBuilder sb = new StringBuilder(256);
                sb.Append("[diag hostility] Territorial ").Append(rel.relType).Append("->Hateful")
                    .Append(" | holder=").Append(holder.agentName).Append(" '").Append(holder.agentRealName).Append("' id=").Append(holder.agentID)
                    .Append(" owner=").Append(holder.ownerID).Append(" chunk=").Append(holder.startingChunk)
                    .Append(" | other=").Append(other.agentName).Append(" '").Append(other.agentRealName).Append("' id=").Append(other.agentID)
                    .Append(" owner=").Append(other.ownerID).Append(" player=").Append(other.isPlayer != 0)
                    .Append(" | turf tileOwner=").Append(tile.owner).Append(" tileChunk=").Append(tile.chunkID);
                Rck.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                failed = true;
                Rck.Log.LogError("Hostility diagnostics disabled: " + e);
            }
        }

        private static void AppendStack(StringBuilder sb)
        {
            StackFrame[] frames = new StackTrace(2, false).GetFrames();
            if (frames == null) return;
            int count = 0;
            sb.Append(" | stack:");
            foreach (StackFrame frame in frames)
            {
                System.Reflection.MethodBase method = frame.GetMethod();
                if (method == null) continue;
                string type = method.DeclaringType != null ? method.DeclaringType.Name : "?";
                if (type.StartsWith("Relationships_", StringComparison.Ordinal) && type.EndsWith("_Diag", StringComparison.Ordinal)) continue;
                sb.Append(count == 0 ? " " : " < ").Append(type).Append('.').Append(method.Name);
                if (++count >= MaxFrames) break;
            }
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.AddRelHate), typeof(Agent), typeof(int))]
    internal static class Relationships_AddRelHate_Diag
    {
        private static void Prefix(Relationships __instance, Agent otherAgent, int addedHate)
        {
            if (addedHate <= 0 || !HostilityDiagnostics.Relevant(__instance, otherAgent, out Agent npc, out Relationship rel)) return;
            if (rel.relHate > 0f) return;
            HostilityDiagnostics.Report(npc, otherAgent, rel, "AddRelHate", "+" + addedHate);
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRelHate), typeof(Agent), typeof(int))]
    internal static class Relationships_SetRelHate_Diag
    {
        private static void Prefix(Relationships __instance, Agent otherAgent, int newHate)
        {
            if (newHate <= 0 || !HostilityDiagnostics.Relevant(__instance, otherAgent, out Agent npc, out Relationship rel)) return;
            if (rel.relHate > 0f) return;
            HostilityDiagnostics.Report(npc, otherAgent, rel, "SetRelHate", "=" + newHate);
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRel), typeof(Agent), typeof(string), typeof(bool))]
    internal static class Relationships_SetRel_Diag
    {
        private static void Prefix(Relationships __instance, Agent otherAgent, string newRel)
        {
            if (newRel != "Hateful" && newRel != "Annoyed") return;
            if (!HostilityDiagnostics.Relevant(__instance, otherAgent, out Agent npc, out Relationship rel)) return;
            if (rel.relType == newRel || rel.relType == "Hateful") return;
            HostilityDiagnostics.Report(npc, otherAgent, rel, "SetRel", rel.relType + "->" + newRel);
        }
    }

    [HarmonyPatch(typeof(Relationships), nameof(Relationships.AddStrikes), typeof(Agent), typeof(int))]
    internal static class Relationships_AddStrikes_Diag
    {
        private static void Prefix(Relationships __instance, Agent otherAgent, int numStrikes)
        {
            if (numStrikes <= 0 || !HostilityDiagnostics.Relevant(__instance, otherAgent, out Agent npc, out Relationship rel)) return;
            HostilityDiagnostics.Report(npc, otherAgent, rel, "AddStrikes", "+" + numStrikes);
        }
    }
}
