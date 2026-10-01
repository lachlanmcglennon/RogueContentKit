using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RCK.Campaign
{
    /// <summary>
    ///   The patrol and wander goals CCU lists in the editor. The game runs them as WanderFar (so they flee, fight and
    ///   investigate like any wanderer); RCK picks each stop: a spot in the start chunk, a far spot on the map, an NPC
    ///   or an object. Host only, as vanilla's goals are.
    /// </summary>
    internal static class WanderGoals
    {
        internal enum Kind
        {
            PatrolChunk,
            PatrolMap,
            Agents,
            AgentsOwner,
            AgentsNonOwner,
            ObjectsOwner,
            ObjectsNonOwner,
        }

        internal static readonly Dictionary<string, Kind> Goals = new Dictionary<string, Kind>(StringComparer.Ordinal)
        {
            ["Random Patrol (Chunk)"] = Kind.PatrolChunk,
            ["Random Patrol (Map)"] = Kind.PatrolMap,
            ["Wander Between Agents"] = Kind.Agents,
            ["Wander Between Agents (Owner)"] = Kind.AgentsOwner,
            ["Wander Between Agents (Non-Owner)"] = Kind.AgentsNonOwner,
            ["Wander Between Objects (Owner)"] = Kind.ObjectsOwner,
            ["Wander Between Objects (Non-Owner)"] = Kind.ObjectsNonOwner,
        };

        // One chunk is 16 tiles of 0.64.
        private const float ChunkSize = 10.24f;
        private const float NearRange = 2f * ChunkSize;
        private const int Tries = 10;

        private static readonly HashSet<string> SkippedObjects = new HashSet<string>(StringComparer.Ordinal)
        {
            "Door", "Window", "Bars", "BarbedWire", "LockdownWall", "SecurityCam", "Turret", "LaserEmitter",
            "FireSpewer", "SawBlade", "KillerPlant", "Mine", "TrapDoor", "GasVent", "FlamingBarrel", "ExplodingBarrel",
            "SlimeBarrel", "PowerBox", "AlarmButton", "ExitPoint", "Elevator", "Manhole", "Bush", "Tree", "Plant",
            "Pipe", "Tube", "Train", "Boulder",
        };

        private sealed class State
        {
            internal int Level;
            internal int AgentId;
            internal PlayfieldObject? Last;
        }

        private static readonly ConditionalWeakTable<Agent, State> States = new ConditionalWeakTable<Agent, State>();
        private static int logged;

        internal static bool TryKind(Agent agent, out Kind kind)
        {
            kind = default;
            return agent != null && agent.defaultGoal != null && Goals.TryGetValue(agent.defaultGoal, out kind);
        }

        /// <summary>The next stop for <paramref name="agent"/>, or <see cref="Vector3.zero"/> to leave it to the game.</summary>
        internal static Vector3 NextStop(Agent agent, Kind kind)
        {
            GameController gc = GameController.gameController;
            if (gc == null || gc.tileInfo == null || !gc.serverPlayer) return Vector3.zero;
            State state = StateOf(agent, gc);
            Vector2 pos;
            try
            {
                pos = kind switch
                {
                    Kind.PatrolChunk => ChunkSpot(agent, gc),
                    Kind.PatrolMap => MapSpot(agent, gc),
                    Kind.Agents or Kind.AgentsOwner or Kind.AgentsNonOwner => AgentStop(agent, gc, kind, state),
                    _ => ObjectStop(agent, gc, kind, state),
                };
                // Nobody or nothing to visit: stay around the start chunk rather than roam the map.
                if (pos == Vector2.zero && kind != Kind.PatrolMap && kind != Kind.PatrolChunk) pos = ChunkSpot(agent, gc);
            }
            catch (Exception e)
            {
                if (logged++ < 5) Rck.Log.LogWarning($"Could not pick a stop for {agent.agentName}'s {agent.defaultGoal}: {e.Message}");
                return Vector3.zero;
            }
            return pos == Vector2.zero ? Vector3.zero : new Vector3(pos.x, pos.y, agent.tr.position.z);
        }

        /// <summary>Goals that stop at an NPC, an object or a patrol point linger there; a map patrol keeps moving.</summary>
        internal static float PauseFor(Kind kind) => kind == Kind.PatrolMap ? UnityEngine.Random.Range(1f, 2f) : UnityEngine.Random.Range(2f, 4.5f);

        private static State StateOf(Agent agent, GameController gc)
        {
            State state = States.GetOrCreateValue(agent);
            int level = LevelScope.CurrentId();
            if (state.Level != level || state.AgentId != agent.agentID)
            {
                state.Level = level;
                state.AgentId = agent.agentID;
                state.Last = null;
            }
            return state;
        }

        private static Vector2 Home(Agent agent)
        {
            Vector2 home = agent.startingPosition;
            return home != Vector2.zero ? home : (Vector2)agent.tr.position;
        }

        private static Vector2 ChunkSpot(Agent agent, GameController gc)
        {
            Vector2 home = Home(agent);
            int chunk = agent.startingChunk;
            Vector2 here = agent.tr.position;
            Vector2 fallback = Vector2.zero;
            for (int i = 0; i < Tries; i++)
            {
                Vector2 pos = gc.tileInfo.FindLocationNearLocation(home, agent, 0.64f, ChunkSize * 0.75f, accountForObstacles: true, notInside: false);
                if (pos == Vector2.zero) continue;
                TileData tile = gc.tileInfo.GetTileData(pos);
                if (chunk != 0 && tile != null && tile.chunkID != chunk) continue;
                if (fallback == Vector2.zero) fallback = pos;
                if (Vector2.Distance(pos, here) >= 1.92f) return pos;
            }
            return fallback;
        }

        private static Vector2 MapSpot(Agent agent, GameController gc)
        {
            Vector2 here = agent.tr.position;
            Vector2 best = Vector2.zero;
            float bestDistance = -1f;
            for (int i = 0; i < Tries; i++)
            {
                Vector2 pos = gc.tileInfo.FindRandLocation(agent, includeOwned: false);
                if (pos == Vector2.zero) continue;
                float d = Vector2.Distance(pos, here);
                if (d > bestDistance)
                {
                    best = pos;
                    bestDistance = d;
                }
                if (d >= ChunkSize) break;
            }
            return best;
        }

        private static bool SameOwner(int ownerA, int chunkA, int ownerB, int chunkB)
            => ownerA > 0 && ownerA == ownerB && chunkA == chunkB;

        private static Vector2 AgentStop(Agent agent, GameController gc, Kind kind, State state)
        {
            Vector2 home = Home(agent);
            List<Agent> picks = new List<Agent>();
            foreach (Agent other in gc.agentList)
            {
                if (other == null || other == agent || other.isPlayer > 0 || other.dead || other.ghost || other.objectAgent) continue;
                bool owner = SameOwner(agent.ownerID, agent.startingChunk, other.ownerID, other.startingChunk);
                if (kind == Kind.AgentsOwner && !owner) continue;
                if (kind == Kind.AgentsNonOwner && owner) continue;
                if (kind != Kind.AgentsOwner && Vector2.Distance(other.tr.position, home) > NearRange) continue;
                relStatus rel = agent.relationships.GetRelCode(other);
                if (rel == relStatus.Hostile || rel == relStatus.Annoyed) continue;
                picks.Add(other);
            }
            if (picks.Count > 1 && state.Last is Agent lastAgent) picks.Remove(lastAgent);
            return Visit(agent, gc, state, picks);
        }

        private static Vector2 ObjectStop(Agent agent, GameController gc, Kind kind, State state)
        {
            Vector2 home = Home(agent);
            List<ObjectReal> picks = new List<ObjectReal>();
            foreach (ObjectReal o in gc.objectRealList)
            {
                if (o == null || o.destroyed || o.objectName == null || SkippedObjects.Contains(o.objectName)) continue;
                bool owner = SameOwner(agent.ownerID, agent.startingChunk, o.owner, o.startingChunk);
                if (kind == Kind.ObjectsOwner && !owner) continue;
                if (kind == Kind.ObjectsNonOwner && (owner || Vector2.Distance(o.tr.position, home) > NearRange)) continue;
                picks.Add(o);
            }
            if (picks.Count > 1 && state.Last is ObjectReal lastObject) picks.Remove(lastObject);
            return Visit(agent, gc, state, picks);
        }

        private static Vector2 Visit<T>(Agent agent, GameController gc, State state, List<T> picks) where T : PlayfieldObject
        {
            while (picks.Count > 0)
            {
                int i = UnityEngine.Random.Range(0, picks.Count);
                T target = picks[i];
                picks.RemoveAt(i);
                Vector2 pos = gc.tileInfo.FindLocationNearLocation(target.tr.position, agent, 0.32f, 1.28f, accountForObstacles: true, notInside: false);
                if (pos == Vector2.zero) continue;
                state.Last = target;
                return pos;
            }
            return Vector2.zero;
        }
    }

    /// <summary>The game has no code for these names, so it would give them no goal at all; they run as WanderFar.</summary>
    [HarmonyPatch(typeof(Agent), nameof(Agent.GetGoalCode))]
    internal static class Agent_GetGoalCode_WanderGoals
    {
        private static void Postfix(string myGoal, ref goalType __result)
        {
            if (__result == goalType.None && myGoal != null && WanderGoals.Goals.ContainsKey(myGoal)) __result = goalType.WanderFar;
        }
    }

    [HarmonyPatch(typeof(GoalWanderFar), nameof(GoalWanderFar.Activate))]
    internal static class GoalWanderFar_Activate_WanderGoals
    {
        private static void Prefix(GoalWanderFar __instance, out bool __state)
        {
            __state = false;
            Agent agent = __instance.agent;
            if (!WanderGoals.TryKind(agent, out WanderGoals.Kind kind)) return;
            __state = true;
            if (agent.wanderFarDest == Vector3.zero) agent.wanderFarDest = WanderGoals.NextStop(agent, kind);
        }

        private static void Postfix(GoalWanderFar __instance, bool __state)
        {
            if (!__state || !WanderGoals.TryKind(__instance.agent, out WanderGoals.Kind kind)) return;
            foreach (Goal sub in __instance.SubGoals)
            {
                if (sub is GoalPause pause) pause.pauseTime = WanderGoals.PauseFor(kind);
            }
        }
    }

    /// <summary>When the path fails or the level asks wanderers to re-path, vanilla picks a random spot; RCK picks the next stop.</summary>
    [HarmonyPatch(typeof(GoalWanderFar), nameof(GoalWanderFar.Process))]
    internal static class GoalWanderFar_Process_WanderGoals
    {
        private static void Prefix(GoalWanderFar __instance, out Vector3 __state)
        {
            __state = __instance.agent != null ? __instance.agent.wanderFarDest : Vector3.zero;
        }

        private static void Postfix(GoalWanderFar __instance, Vector3 __state)
        {
            Agent agent = __instance.agent;
            if (agent == null || agent.wanderFarDest == __state || agent.wanderFarDest == Vector3.zero || !WanderGoals.TryKind(agent, out WanderGoals.Kind kind)) return;
            Vector3 next = WanderGoals.NextStop(agent, kind);
            if (next != Vector3.zero) agent.wanderFarDest = next;
        }
    }
}
