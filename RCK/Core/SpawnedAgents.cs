using System;
using System.Collections.Generic;

namespace RCK
{
    /// <summary>
    ///   Fills the inventory of an NPC that RCK spawns mid-level. <c>SpawnerMain.SpawnAgent</c> never does: only the
    ///   level loader calls <c>InvDatabase.FillAgent</c>, so a spawned NPC would otherwise carry nothing but fists.
    /// </summary>
    public static class SpawnedAgents
    {
        private const string Custom = "Custom";
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        ///   The items a placed agent was given in the level editor (its three <c>extraVarString</c>s), or null if it
        ///   has none, so a copy carries the same; a vanilla agent left on "Randomized" rolls its own.
        /// </summary>
        public static string[]? LoadoutOf(Agent? source)
        {
            if (source == null) return null;
            string[] loadout = { source.extraVarString ?? "", source.extraVarString2 ?? "", source.extraVarString3 ?? "" };
            foreach (string s in loadout)
                if (s.Length > 0 && s != "Randomized") return loadout;
            return null;
        }

        /// <summary>
        ///   Arms <paramref name="agent"/> as the level loader would: <paramref name="loadout"/> (see
        ///   <see cref="LoadoutOf"/>) if given, else its type's random weapon, items and money. A custom character
        ///   still holding no weapon then gets its character's starting items, as vanilla gives those only to players.
        ///   Host only; RCK's loadout traits apply through the same call.
        /// </summary>
        public static void Arm(Agent? agent, string[]? loadout)
        {
            if (agent == null || agent.inventory == null || agent.isPlayer != 0) return;
            try
            {
                // Pooled agents are recycled, so stale editor items are cleared too.
                agent.extraVarString = loadout != null && loadout.Length > 0 ? loadout[0] : "";
                agent.extraVarString2 = loadout != null && loadout.Length > 1 ? loadout[1] : "";
                agent.extraVarString3 = loadout != null && loadout.Length > 2 ? loadout[2] : "";
                agent.inventory.FillAgent();
                if (agent.agentName == Custom && agent.customCharacterData != null && !HasWeapon(agent))
                    foreach (string name in agent.customCharacterData.items)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        InvItem item = agent.inventory.AddItem(agent.inventory.SwapWeaponTypes(name), 1);
                        if (item != null && item.invItemName != "Money") item.invItemCount = Math.Max(1, item.initCount);
                    }
            }
            catch (Exception e)
            {
                if (logged.Add(agent.agentName ?? "")) Rck.Log.LogError($"Arming a spawned {agent.agentName} failed: {e}");
            }
        }

        /// <summary>Whether <paramref name="agent"/> holds a weapon other than its fists.</summary>
        public static bool HasWeapon(Agent? agent)
        {
            if (agent?.inventory?.InvItemList == null) return false;
            foreach (InvItem item in agent.inventory.InvItemList)
                if (item != null && item.invItemName != "Fist" && (item.itemType == "WeaponMelee" || item.itemType == "WeaponProjectile"))
                    return true;
            return false;
        }
    }
}
