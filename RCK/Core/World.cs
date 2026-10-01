using System;
using System.Collections.Generic;

namespace RCK
{
    /// <summary>
    ///   Faction services the Social module offers the other modules, which can't reference each other. Keys are faction
    ///   keys from <see cref="RckData.FactionKeys"/> (<c>Blahd</c>, <c>Faction_3</c>). Everything is per level and read
    ///   on the server.
    /// </summary>
    public interface IRckFactions
    {
        /// <summary>The key a name stands for (<c>3</c>, <c>Faction_3</c>, <c>blahd</c>, <c>CommonFolk</c>), or null.</summary>
        string? Resolve(string name);
        /// <summary>A short name for messages: <c>Blahd</c>, <c>Upper Cruster</c>, or a numbered faction's designer name.</summary>
        string Display(string key);
        /// <summary>The members as a group, for messages: <c>Blahds</c>, <c>Upper Crusters</c>, <c>Cops</c>.</summary>
        string Plural(string key);
        /// <summary>A member of <paramref name="key"/> through traits or vanilla membership (players: traits and disguises).</summary>
        bool IsMember(Agent agent, string key);
        /// <summary>Holds <c>&lt;key&gt;_Leader</c>.</summary>
        bool IsLeader(Agent agent, string key);
        /// <summary>The faction's last leader fell this level.</summary>
        bool IsRouted(string key);
        /// <summary>The factions <paramref name="agent"/> belongs to; <c>Common_Folk</c> only through its traits.</summary>
        IList<string> FactionsOf(Agent agent);
        /// <summary>
        ///   The factions present this level that <paramref name="key"/> is at odds with: a Hateful, Territorial or
        ///   Annoyed rule either way (faction traits or the matrix). Most bitter first.
        /// </summary>
        IList<string> RivalsOf(string key);
        /// <summary>Living NPC members of <paramref name="key"/> this level.</summary>
        IList<Agent> MembersOf(string key);
        /// <summary>Every living NPC member of <paramref name="key"/> outside a party turns Friendly toward the player's side.</summary>
        int Befriend(Agent player, string key);
        /// <summary>
        ///   Sets how <paramref name="a"/>'s members feel about <paramref name="b"/>'s (and back when
        ///   <paramref name="bothWays"/>) for the rest of the level. <paramref name="rel"/> is Hateful, Annoyed, Neutral
        ///   or Friendly. Returns how many directed pairs changed.
        /// </summary>
        int SetLevelRelation(string a, string b, string rel, bool bothWays);
    }

    /// <summary>Cross-module hub: services one module offers and the others use, all optional.</summary>
    public static class RckWorld
    {
        /// <summary>Set by RCK.Social when it loads; null without it.</summary>
        public static IRckFactions? Factions { get; set; }

        private static readonly Dictionary<string, Func<string, bool>> gateSwitches = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///   Registers a level-gate condition <c>Name=arg</c> (see RCK.Campaign's level gates). The function gets the text
        ///   after <c>=</c> and says whether the condition holds now.
        /// </summary>
        public static void RegisterGateSwitch(string name, Func<string, bool> holds)
        {
            if (string.IsNullOrEmpty(name) || holds == null) return;
            gateSwitches[name] = holds;
        }

        public static bool HasGateSwitch(string name) => name != null && gateSwitches.ContainsKey(name);

        /// <summary>False when no module registered <paramref name="name"/>; else <paramref name="holds"/> is its answer.</summary>
        public static bool TryGateSwitch(string name, string arg, out bool holds)
        {
            holds = false;
            if (name == null || !gateSwitches.TryGetValue(name, out Func<string, bool> f)) return false;
            try { holds = f(arg ?? ""); }
            catch (Exception e)
            {
                Rck.Log.LogError($"Level gate switch {name}={arg} failed: {e}");
                holds = false;
            }
            return true;
        }
    }
}
