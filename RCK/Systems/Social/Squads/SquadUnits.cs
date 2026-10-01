#nullable disable
using System;
using System.Collections.Generic;
using Google2u;

namespace RCK.Social
{
    /// <summary>
    ///   The units a faction's squads are made of. A faction's designated units come from its placed NPCs holding
    ///   <c>&lt;key&gt;_Reinforcement</c> (each counts once, so place two to double a unit's weight) and the unit names
    ///   of its <c>[RCK]FactionRespawn::</c> entry (see <see cref="RespawnRules"/>); raids, backup, garrison copies and
    ///   respawns all spawn them. A faction without designated units copies its members as before, and respawns from
    ///   the plain members it had at load. Each unit is snapshotted when the level loads, so it keeps coming after its
    ///   source dies. Server only; one level load.
    /// </summary>
    internal static class SquadUnits
    {
        public const string Custom = "Custom";

        internal sealed class Unit
        {
            /// <summary>The agent type to spawn: a vanilla type, or <see cref="Custom"/> with <see cref="Data"/>.</summary>
            public string Type;
            public SaveCharacterData Data;
            public string Name;
            /// <summary>The items its source was placed with (see <see cref="SpawnedAgents.LoadoutOf"/>); null rolls them.</summary>
            public string[] Loadout;
        }

        // Group names, player-only and editor-only rows of the agent table, which don't spawn one NPC.
        private static readonly HashSet<string> notUnits = new HashSet<string>(StringComparer.Ordinal)
        {
            "Playerr", "Custom", "Ghost", "Hologram", "Nature", "Self", "RobotPlayer", "MechEmpty", "MechFilled", "MechPilot",
            "PlayerChooses", "None", "WallSpawnSometimes", "testagent",
        };

        private static readonly Dictionary<int, List<Unit>> designated = new Dictionary<int, List<Unit>>();
        private static readonly Dictionary<int, List<Unit>> plain = new Dictionary<int, List<Unit>>();
        private static readonly List<Agent> sources = new List<Agent>();
        private static int stamp = int.MinValue;

        private static void Ensure(GameController gc)
        {
            if (!LevelScope.IsNew(ref stamp)) return;
            designated.Clear();
            plain.Clear();
            sources.Clear();
            try { Build(gc); }
            catch (Exception e) { Rck.Log.LogError($"Factions: squad units failed to load, squads copy members instead: {e}"); }
        }

        /// <summary>
        ///   A unit by name, as <c>[RCK]FactionRespawn::</c> and <c>[RCK]Command::</c> name them: a custom character
        ///   placed in the level, a placed NPC's type or a vanilla agent type. Null (with <paramref name="why"/>) if none.
        /// </summary>
        public static Unit Resolve(GameController gc, string name, out string why)
        {
            why = "there's no level";
            if (gc == null || string.IsNullOrEmpty(name)) return null;
            Ensure(gc);
            sources.RemoveAll(a => a == null);
            return Resolve(gc, sources, name.Trim(), out why);
        }

        /// <summary>Snapshots the level's units now (as it loads), before any of their sources can die.</summary>
        public static void Prepare(GameController gc)
        {
            if (gc != null) Ensure(gc);
        }

        /// <summary>Faction <paramref name="key"/>'s designated units, or null if it has none.</summary>
        public static IList<Unit> Designated(GameController gc, int key)
        {
            if (gc == null || key < 0) return null;
            Ensure(gc);
            return designated.TryGetValue(key, out List<Unit> list) && list.Count > 0 ? list : null;
        }

        /// <summary>What faction <paramref name="key"/> respawns: its designated units, else its plain members at load; null if neither.</summary>
        public static IList<Unit> ForRespawn(GameController gc, int key)
        {
            IList<Unit> units = Designated(gc, key);
            if (units != null) return units;
            return plain.TryGetValue(key, out List<Unit> list) && list.Count > 0 ? list : null;
        }

        /// <summary>An agent's unit name: its custom character's name, else its type.</summary>
        public static string NameOf(Agent agent)
        {
            if (agent == null) return "nobody";
            if (agent.agentName == Custom && agent.customCharacterData != null && !string.IsNullOrEmpty(agent.customCharacterData.characterName))
                return agent.customCharacterData.characterName;
            return agent.agentName;
        }

        private static void Build(GameController gc)
        {
            if (gc.agentList == null || Factions.Keys.Count == 0) return;
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.isPlayer != 0 || a.objectAgent || a.ghost || a.hologram) continue;
                if ((a.employer != null && a.employer.isPlayer > 0) || Squads.IsMember(a)) continue;
                sources.Add(a);
            }

            foreach (Agent a in sources)
            {
                ulong keys = Factions.ReinforcementOf(a);
                if (keys == 0) continue;
                Unit u = FromAgent(a, out string why);
                if (u == null)
                {
                    Rck.Log.LogWarning($"Factions: {AgentText.Describe(a)} holds a _{RespawnRules.Role} trait but isn't used as a unit: {why}.");
                    continue;
                }
                for (int i = 0; keys != 0 && i < 64; i++, keys >>= 1)
                    if ((keys & 1UL) != 0) Add(designated, i, u);
            }

            RespawnConfig config = FactionRespawn.Config(gc);
            if (config != null)
                foreach (RespawnFaction f in config.Factions.Values)
                    foreach (string name in f.Units)
                    {
                        Unit u = Resolve(gc, sources, name, out string why);
                        if (u == null) Rck.Log.LogWarning($"Factions: {RespawnRules.Prefix} unit \"{name}\" for {Factions.Keys[f.Key]} ignored: {why}.");
                        else Add(designated, f.Key, u);
                    }

            foreach (Agent a in sources)
            {
                if (a.dead) continue;
                ulong keys = Factions.KeysOf(a);
                for (int i = 0; keys != 0 && i < 64; i++, keys >>= 1)
                {
                    if ((keys & 1UL) == 0 || !Squads.Eligible(a, 1UL << i, squadOk: false)) continue;
                    Unit u = FromAgent(a, out _);
                    if (u != null) Add(plain, i, u);
                }
            }

            foreach (KeyValuePair<int, List<Unit>> kv in designated)
                Rck.Log.LogInfo($"Factions: {Factions.Keys[kv.Key]} squads are made of {Describe(kv.Value)}.");
        }

        private static void Add(Dictionary<int, List<Unit>> map, int key, Unit u)
        {
            if (!map.TryGetValue(key, out List<Unit> list)) map[key] = list = new List<Unit>();
            list.Add(u);
        }

        private static string Describe(List<Unit> units)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Unit u in units)
            {
                if (!counts.ContainsKey(u.Name))
                {
                    counts[u.Name] = 0;
                    order.Add(u.Name);
                }
                counts[u.Name]++;
            }
            var parts = new List<string>();
            foreach (string name in order) parts.Add(counts[name] > 1 ? $"{name} ×{counts[name]}" : name);
            return string.Join(", ", parts.ToArray());
        }

        private static Unit FromAgent(Agent a, out string why)
        {
            string type = a.agentName;
            if (string.IsNullOrEmpty(type))
            {
                why = "it has no type";
                return null;
            }
            if (!Allowed(AgentTraits.Get(a), out why)) return null;
            if (type == Custom)
            {
                SaveCharacterData data = a.customCharacterData;
                if (data == null)
                {
                    why = "it's a custom character without data";
                    return null;
                }
                return new Unit { Type = Custom, Data = data, Name = string.IsNullOrEmpty(data.characterName) ? Custom : data.characterName, Loadout = SpawnedAgents.LoadoutOf(a) };
            }
            return new Unit { Type = type, Name = type, Loadout = SpawnedAgents.LoadoutOf(a) };
        }

        /// <summary>
        ///   A unit name: a placed custom character's name, a placed NPC's type or a vanilla agent type (any case).
        ///   Custom characters must be placed somewhere in the level, as their data lives in the level.
        /// </summary>
        private static Unit Resolve(GameController gc, List<Agent> sources, string name, out string why)
        {
            foreach (Agent a in sources)
            {
                SaveCharacterData data = a.agentName == Custom ? a.customCharacterData : null;
                if (data == null || !string.Equals(data.characterName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Allowed(data.traits, out why)) return null;
                return new Unit { Type = Custom, Data = data, Name = data.characterName, Loadout = SpawnedAgents.LoadoutOf(a) };
            }
            if (string.Equals(name, Custom, StringComparison.OrdinalIgnoreCase))
            {
                why = "name the custom character instead";
                return null;
            }
            foreach (Agent a in sources)
                if (a.agentName != Custom && string.Equals(a.agentName, name, StringComparison.OrdinalIgnoreCase))
                {
                    why = null;
                    return new Unit { Type = a.agentName, Name = a.agentName, Loadout = SpawnedAgents.LoadoutOf(a) };
                }
            if (VanillaType(name, out string type))
            {
                why = null;
                return new Unit { Type = type, Name = type };
            }
            why = "no custom character in the level has that name and it isn't an agent type";
            return null;
        }

        /// <summary>A vanilla agent type that spawns one NPC, in its own spelling.</summary>
        internal static bool VanillaType(string name, out string type)
        {
            type = null;
            if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0]) || name.IndexOf(',') >= 0) return false;
            AgentNameDB.rowIds id;
            try { id = (AgentNameDB.rowIds)Enum.Parse(typeof(AgentNameDB.rowIds), name, ignoreCase: true); }
            catch (ArgumentException) { return false; }
            if (!Enum.IsDefined(typeof(AgentNameDB.rowIds), id) || id >= AgentNameDB.rowIds.BlueCollars) return false;
            string canonical = id.ToString();
            if (!string.Equals(canonical, name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (notUnits.Contains(canonical) || canonical.EndsWith("_Chinese", StringComparison.Ordinal) || canonical.EndsWith("_N", StringComparison.Ordinal)) return false;
            type = canonical;
            return true;
        }

        /// <summary>No copies of leaders, gate agents, quest NPCs, brokers or Relationless NPCs.</summary>
        private static bool Allowed(IEnumerable<string> traits, out string why)
        {
            why = null;
            if (traits == null) return true;
            foreach (string t in traits)
            {
                if (t == null) continue;
                if (t.EndsWith("_Leader", StringComparison.Ordinal)) why = "it's a faction leader";
                else if (t == "Relationless") why = "it's Relationless";
                else if (t == Broker.Trait) why = "it's a broker";
                else if (t == "Quest_Giver" || t == "RCK_Radiant_Quest_Giver" || t.StartsWith("RCK_Quest_Target", StringComparison.Ordinal)) why = "it's in a quest";
                else if (t.StartsWith("Agent_Switch_", StringComparison.Ordinal) || t.StartsWith("Switch_", StringComparison.Ordinal)) why = "it's a level gate agent";
                if (why != null) return false;
            }
            return true;
        }
    }
}
