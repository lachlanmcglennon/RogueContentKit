using System;
using System.Collections.Generic;

namespace RCK
{
    /// <summary>
    ///   A small random source seeded from an agent (level seed, level, ID, spawn chunk and position, name and traits),
    ///   so the same NPC rolls the same way every time a level is built, on every machine. It's a mutable struct: pass it
    ///   with <c>ref</c>, or the callee's rolls don't move the caller's on and later rolls repeat them.
    /// </summary>
    public struct StableRng
    {
        private uint state;

        /// <param name="agent">The agent to seed from.</param>
        /// <param name="salt">Keeps one system's rolls apart from another's (<c>"Appearance"</c>, <c>"Loadout"</c>).</param>
        /// <param name="playersBySlot">
        ///   Seed players by their player number only, not by level, ID or position, so a player rolls the same on every level.
        /// </param>
        public StableRng(Agent agent, string salt, bool playersBySlot = false)
        {
            state = 2166136261u;
            Add(salt);
            Add(agent.gc?.loadLevel?.randomSeedNum ?? 0);
            if (playersBySlot && agent.isPlayer != 0)
            {
                Add(agent.isPlayer);
            }
            else
            {
                Add(agent.gc?.sessionDataBig?.curLevelEndless ?? 0);
                Add(agent.agentID);
                Add(agent.streamingChunkObjectID);
                Add(agent.startingChunk);
                Add(agent.startingSector);
                Add((int)Math.Round(agent.originalPosReal.x * 100f));
                Add((int)Math.Round(agent.originalPosReal.y * 100f));
            }
            Add(agent.agentName);
            Add(agent.agentRealName);
            var sorted = new List<string>(AgentTraits.Get(agent));
            sorted.Sort(StringComparer.Ordinal);
            foreach (string trait in sorted)
            {
                Add(trait);
            }
        }

        public string Pick(List<string> values) => values[Next(values.Count)];

        public bool Chance(int percent) => percent >= 100 || (percent > 0 && Next(100) < percent);

        public int RangeInclusive(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }
            return min + Next(max - min + 1);
        }

        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 1)
            {
                return 0;
            }
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMax);
        }

        private void Add(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Add(0);
                return;
            }
            unchecked
            {
                for (int i = 0; i < text!.Length; i++)
                {
                    state ^= text[i];
                    state *= 16777619u;
                }
            }
        }

        private void Add(int value)
        {
            unchecked
            {
                state ^= (uint)value;
                state *= 16777619u;
            }
        }
    }
}
