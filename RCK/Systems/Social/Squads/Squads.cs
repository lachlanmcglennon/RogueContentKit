#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static RCK.AgentText;

namespace RCK.Social
{
    /// <summary>
    ///   Squads for the faction war: raids (<see cref="FactionRaids"/>), backup (<see cref="FactionBackup"/>), turf
    ///   guards (<see cref="TurfWar"/>) and respawned members (<see cref="FactionRespawn"/>). A squad is its faction's
    ///   designated units (see <see cref="SquadUnits"/>) or else copies of one of its own NPC members, spawned next to
    ///   a member out of the players' sight when there is one, or members a turf takes over. Squad members are never
    ///   street-innocent, and each pair set up with one of them (at spawn and later) is Hateful toward the squad's
    ///   foes: its target factions, or the one attacker it answers. Only the server acts, and the registry belongs to
    ///   one level load. Raids, backup and guards share one cap of living members, respawned members have their own.
    /// </summary>
    internal static class Squads
    {
        public const int MaxLiving = 12;
        public const string SpawnType = "RCKSquad";
        private const float OutOfSight = 8f;

        internal enum Kind { Raid, Backup, Guard, Respawn, Command }

        /// <summary>The custom character a spawn in progress gets (see <see cref="Agent_SetupAgentStats_SquadUnit_Patch"/>).</summary>
        internal static SaveCharacterData PendingData;

        internal sealed class Squad
        {
            public Kind Kind;
            /// <summary>The squad's faction.</summary>
            public int Key;
            /// <summary>The factions it fights (members of its own faction excepted).</summary>
            public ulong Foes;
            /// <summary>One agent it fights (and a player's followers, when that's a player).</summary>
            public Agent Foe;
            public readonly List<Agent> Members = new List<Agent>();
        }

        private static readonly Dictionary<Agent, Squad> members = new Dictionary<Agent, Squad>();

        static Squads() => LevelScope.ResetAtBoth(ClearMembers);

        private static void ClearMembers() => members.Clear();

        private static bool Check() => FactionEvents.Server() != null;

        public static Squad Of(Agent agent)
        {
            if (agent == null || members.Count == 0 || !Check()) return null;
            return members.TryGetValue(agent, out Squad s) ? s : null;
        }

        public static bool IsMember(Agent agent) => Of(agent) != null;

        // Raids, backup and guards share one cap; respawned members and the commander's recruits have their own.
        private static int Pool(Kind kind) => kind == Kind.Respawn ? 1 : kind == Kind.Command ? 2 : 0;

        /// <summary>Squad members alive now that share <paramref name="kind"/>'s cap.</summary>
        public static int Living(Kind kind)
        {
            if (members.Count == 0 || !Check()) return 0;
            int pool = Pool(kind), n = 0;
            foreach (KeyValuePair<Agent, Squad> kv in members)
                if (kv.Key != null && !kv.Key.dead && !kv.Key.disappeared && Pool(kv.Value.Kind) == pool) n++;
            return n;
        }

        /// <summary>How many more members a squad of <paramref name="kind"/> may spawn under its cap.</summary>
        public static int Room(Kind kind)
        {
            int cap = kind == Kind.Respawn ? RespawnRules.MaxLiving : kind == Kind.Command ? Command.Cap : MaxLiving;
            return cap - Living(kind);
        }

        /// <summary>The living squads of faction <paramref name="key"/> of <paramref name="kind"/>, oldest first.</summary>
        public static List<Squad> All(Kind kind, int key)
        {
            var list = new List<Squad>();
            if (members.Count == 0 || !Check()) return list;
            foreach (Squad s in members.Values)
                if (s.Kind == kind && s.Key == key && !list.Contains(s)) list.Add(s);
            return list;
        }

        /// <summary>Drops the fallen, so they neither count toward the cap nor bear the squad's hate if raised.</summary>
        public static void Prune()
        {
            if (members.Count == 0 || !Check()) return;
            List<Agent> gone = null;
            foreach (KeyValuePair<Agent, Squad> kv in members)
                if (kv.Key == null || kv.Key.dead || kv.Key.disappeared) (gone = gone ?? new List<Agent>()).Add(kv.Key);
            if (gone == null) return;
            foreach (Agent a in gone) Forget(a);
        }

        internal static void Forget(Agent agent)
        {
            if (!members.TryGetValue(agent, out Squad s)) return;
            s.Members.Remove(agent);
            members.Remove(agent);
        }

        // ---- Picking and spawning ----

        /// <summary>
        ///   True if <paramref name="a"/> may be copied into a squad of the faction <paramref name="bit"/>, or (with
        ///   <paramref name="squadOk"/>) join one: a living, free NPC member that isn't a leader, backup caller, broker,
        ///   quest giver or target, gate agent, Relationless or important NPC.
        /// </summary>
        public static bool Eligible(Agent a, ulong bit, bool squadOk)
        {
            if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost || a.hologram || a.disappeared) return false;
            if (a.mechEmpty || a.mechFilled || a.prisoner != 0 || a.important || a.employer != null || a.relationships == null) return false;
            string type = a.agentName;
            if (string.IsNullOrEmpty(type) || (type == "Custom" && a.customCharacterData == null)) return false;
            if (Factions.MemberKeys(a, bit) == 0 || (FactionEvents.Routed & bit) != 0) return false;
            Factions.RolesOf(a, out _, out ulong leader);
            if (leader != 0 || Factions.BackupOf(a) != 0) return false;
            if (!squadOk && members.Count != 0 && IsMember(a)) return false;
            foreach (string t in AgentTraits.Get(a))
            {
                if (t == "Relationless" || t == Broker.Trait || t == "Quest_Giver" || t == "RCK_Radiant_Quest_Giver") return false;
                if (t.StartsWith("Agent_Switch_", StringComparison.Ordinal) || t.StartsWith("Switch_", StringComparison.Ordinal)
                    || t.StartsWith("RCK_Quest_Target", StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        ///   A member of faction <paramref name="key"/> to copy: out of the players' sight if any is (the nearest to
        ///   <paramref name="near"/>, else a random one), otherwise the one farthest from them. Null if none qualifies.
        /// </summary>
        public static Agent FindTemplate(GameController gc, int key, Vector2? near)
        {
            if (gc == null || gc.agentList == null || key < 0 || !Check()) return null;
            ulong bit = 1UL << key;
            Agent hidden = null, seen = null;
            float hiddenScore = float.MaxValue, seenDistance = -1f;
            foreach (Agent a in gc.agentList)
            {
                if (!Eligible(a, bit, false)) continue;
                Vector2 p = a.tr.position;
                float d = PlayerDistance(gc, p);
                if (d >= OutOfSight)
                {
                    float score = near.HasValue ? Vector2.Distance(p, near.Value) : UnityEngine.Random.value;
                    if (score < hiddenScore) { hiddenScore = score; hidden = a; }
                }
                else if (d > seenDistance) { seenDistance = d; seen = a; }
            }
            return hidden != null ? hidden : seen;
        }

        internal static float PlayerDistance(GameController gc, Vector2 p)
        {
            float best = float.MaxValue;
            if (gc.playerAgentList != null)
                foreach (Agent pl in gc.playerAgentList)
                    if (pl != null && !pl.dead) best = Mathf.Min(best, Vector2.Distance(pl.tr.position, p));
            return best;
        }

        /// <summary>
        ///   Where a new squad of faction <paramref name="key"/> appears: the post of its turf nearest
        ///   <paramref name="toward"/> that's out of the players' sight and clear of <paramref name="foes"/> (see
        ///   <see cref="FactionRespawn.SpawnPoint"/>); null if it holds no such turf.
        /// </summary>
        internal static Vector2? Origin(GameController gc, int key, Vector2 toward, ulong foes, out TurfWar.Turf turf)
        {
            if (FactionRespawn.SpawnPoint(gc, key, foes, out turf, out int post, near: toward)) return turf.Posts[post];
            turf = null;
            return null;
        }

        /// <summary>
        ///   Spawns up to <paramref name="count"/> members into <paramref name="squad"/> next to <paramref name="template"/>,
        ///   or at <paramref name="at"/> when it's given (within the cap): the faction's designated units if it has any
        ///   (see <see cref="SquadUnits"/>), else copies of the template.
        /// </summary>
        public static int Spawn(GameController gc, Agent template, int count, Squad squad, Vector2? at = null)
        {
            if (gc == null || template == null || squad == null || !Check()) return 0;
            Vector2 from = at ?? (Vector2)template.tr.position;
            Agent sight = at.HasValue ? null : template;
            IList<SquadUnits.Unit> units = SquadUnits.Designated(gc, squad.Key);
            if (units != null) return SpawnUnits(gc, units, from, sight, count, squad);
            count = Math.Min(count, Room(squad.Kind));
            string type = template.agentName;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = gc.tileInfo.FindLocationNearLocation(from, sight, 0.32f, 1.28f, accountForObstacles: true, notInside: false);
                if (pos == Vector2.zero) continue;
                // The relationships are set up a frame later (SpawnerMain.SetRelsLate), after the squad is registered.
                Agent m = gc.spawnerMain.SpawnAgent(new Vector3(pos.x, pos.y, template.tr.position.z), template, type, SpawnType, template);
                if (m == null) continue;
                SpawnedAgents.Arm(m, SpawnedAgents.LoadoutOf(template));
                Enlist(m, squad);
                made++;
            }
            return made;
        }

        /// <summary>
        ///   Spawns up to <paramref name="count"/> of <paramref name="units"/> (picked at random) into
        ///   <paramref name="squad"/> near <paramref name="at"/>, within the cap. <paramref name="sight"/> (may be null)
        ///   is the agent whose line of sight the spots must share. A unit that isn't a member of the squad's faction
        ///   (a vanilla type for a numbered faction) gets its <c>&lt;key&gt;_Aligned</c> trait; the commander's recruits
        ///   are sworn to it instead (see <see cref="Factions.Swear"/>), so any faction's units serve it alone.
        /// </summary>
        public static int SpawnUnits(GameController gc, IList<SquadUnits.Unit> units, Vector2 at, Agent sight, int count, Squad squad)
        {
            if (gc == null || units == null || units.Count == 0 || squad == null || !Check()) return 0;
            count = Math.Min(count, Room(squad.Kind));
            ulong bit = 1UL << squad.Key;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                SquadUnits.Unit unit = units[UnityEngine.Random.Range(0, units.Count)];
                Vector2 pos = gc.tileInfo.FindLocationNearLocation(at, sight, 0.32f, 1.28f, accountForObstacles: true, notInside: false);
                if (pos == Vector2.zero) continue;
                Agent m;
                PendingData = unit.Type == SquadUnits.Custom ? unit.Data : null;
                try { m = gc.spawnerMain.SpawnAgent(new Vector3(pos.x, pos.y, 0f), null, unit.Type, SpawnType, null); }
                finally { PendingData = null; }
                if (m == null) continue;
                SpawnedAgents.Arm(m, unit.Loadout);
                // The relationships are set up a frame later (SpawnerMain.SetRelsLate), after the oath.
                if (squad.Kind == Kind.Command) Factions.Swear(m, squad.Key);
                else if (Factions.MemberKeys(m, bit) == 0)
                {
                    try { m.statusEffects.AddTrait(Factions.Keys[squad.Key] + "_Aligned"); }
                    catch (Exception e) { SocialRules.LogOnce(m, "squad-unit-aligned", e); }
                }
                Enlist(m, squad);
                made++;
            }
            return made;
        }

        /// <summary>Adds <paramref name="agent"/> to <paramref name="squad"/> (leaving any other one); it's guilty for the level.</summary>
        public static void Enlist(Agent agent, Squad squad)
        {
            if (agent == null || squad == null || !Check()) return;
            if (members.TryGetValue(agent, out Squad old) && old != squad) old.Members.Remove(agent);
            members[agent] = squad;
            if (!squad.Members.Contains(agent)) squad.Members.Add(agent);
            StreetInnocence.MarkCaught(agent);
            agent.wontFlee = true;
            agent.agentActive = true;
        }

        /// <summary>
        ///   The squad walks to <paramref name="target"/> and searches it (vanilla Investigate, as SpawnerMain sends the
        ///   enforcers an alarm calls): it fights what it finds, searches around, walks back to where it spawned and
        ///   vanishes. A squad with one foe tracks it.
        /// </summary>
        public static void Send(Squad squad, Vector2 target)
        {
            Agent foe = squad.Foe != null && !squad.Foe.dead ? squad.Foe : null;
            foreach (Agent m in squad.Members)
            {
                if (m == null || m.dead) continue;
                if (foe != null) m.investigationTarget = foe;
                m.investigatePosition = new Vector3(target.x, target.y, m.tr.position.z);
                m.investigation = 1;
                m.SetDefaultGoal("Investigate");
                m.alwaysActiveWanderFar = true;
                m.agentActive = true;
            }
        }

        /// <summary><paramref name="agent"/> guards a post of the turf owned by <paramref name="owner"/> in chunk <paramref name="chunk"/>.</summary>
        public static void Guard(Agent agent, int owner, int chunk, Chunk chunkReal, Vector2 post, int angle)
        {
            agent.ownerID = owner;
            agent.startingChunk = chunk;
            agent.initialStartingChunk = chunk;
            if (chunkReal != null)
            {
                agent.startingChunkReal = chunkReal;
                agent.hasStartingChunkReal = true;
            }
            agent.startingPosition = post;
            agent.startingAngle = angle;
            GuardAt(agent);
        }

        /// <summary>
        ///   <paramref name="agent"/> walks to <paramref name="to"/> and stands there (vanilla Guard, owning nothing so
        ///   the turf's holders fight it), fighting its foes on the way.
        /// </summary>
        public static void March(Agent agent, Vector2 to)
        {
            agent.ownerID = 0;
            agent.startingPosition = to;
            GuardAt(agent);
        }

        /// <summary>
        ///   <paramref name="agent"/> guards its (new) <see cref="Agent.startingPosition"/>. The brain only starts a
        ///   Guard goal when it switches to one, and only a starting Guard paths to its post, so a member already
        ///   guarding gets a fresh one, as the brain would give it. Otherwise it stood still until something disturbed
        ///   it, and with nothing to do far from the players its brain switched off.
        /// </summary>
        private static void GuardAt(Agent agent)
        {
            agent.investigation = 0;
            agent.SetDefaultGoal("Guard");
            agent.alwaysActiveWanderFar = true;
            agent.agentActive = true;
            Brain brain = agent.brain;
            if (brain == null || agent.brainUpdate == null || brain.Goals.Count == 0 || !(brain.Goals[0] is GoalGuard)) return;
            try { agent.brainUpdate.SwitchGoal(new GoalGuard()); }
            catch (Exception e) { SocialRules.LogOnce(agent, "squad-guard-restart", e); }
        }

        // ---- Pair setup ----

        /// <summary>A pair was just decided by the rules (not party-mates, not Relationless): squads hate their foes.</summary>
        internal static void AfterPairSetup(Agent a, Agent b)
        {
            if (members.Count == 0 || a == null || b == null || a == b) return;
            Squad sa = Of(a), sb = Of(b);
            if (sa == null && sb == null) return;
            using (RelOps.Quietly())
            {
                if (sa != null) Pair(a, sa, b);
                if (sb != null) Pair(b, sb, a);
            }
        }

        private static void Pair(Agent m, Squad squad, Agent other)
        {
            if (m.dead || other.dead || other.objectAgent || m.relationships == null || other.relationships == null) return;
            if (Of(other) == squad || !IsFoe(squad, other)) return;
            if (PartyPeace.ArePartyMates(m, other) || LevelRelations.Peace(squad.Key, other)) return;
            if ((FactionEvents.Routed & (1UL << squad.Key)) != 0 && FactionEvents.IsPlayerSide(other)) return;
            Hate(m, other);
            // Innocent targets are only attacked, so they don't start fights of their own.
            if (other.isPlayer == 0 && !StreetInnocence.IsInnocent(other)) Hate(other, m);
        }

        private static bool IsFoe(Squad squad, Agent other)
        {
            Agent foe = squad.Foe;
            if (foe != null && (other == foe || (foe.isPlayer > 0 && other.employer == foe))) return true;
            if (squad.Foes == 0 || Factions.MemberKeys(other, 1UL << squad.Key) != 0) return false;
            return Factions.MemberKeys(other, squad.Foes) != 0;
        }

        /// <summary>
        ///   <paramref name="source"/> turns Hateful (hate 5) toward <paramref name="target"/>. Aligned, Loyal and
        ///   Submissive ties stay, as do Relationless agents.
        /// </summary>
        internal static bool Hate(Agent source, Agent target)
        {
            if (source == target || source.relationships == null) return false;
            if (SocialRules.Has(source, "Relationless") || SocialRules.Has(target, "Relationless")) return false;
            Relationship rel = RelOps.Of(source, target);
            if (rel == null) return false;
            if (SocialRules.EscalationRank(rel.relTypeCode) == 0) return false;
            return RelOps.ForceHate(source, target, rel);
        }

        // ---- Messages ----

        /// <summary>A short status text over each local player.</summary>
        internal static void Announce(GameController gc, string type, string text)
        {
            if (gc == null || gc.playerAgentList == null) return;
            foreach (Agent p in gc.playerAgentList)
            {
                if (p == null || !p.localPlayer || p.dead) continue;
                try { gc.spawnerMain.SpawnStatusText(p, type, text); }
                catch (Exception e) { SocialRules.LogOnce(p, "faction-war-text", e); }
            }
        }

        /// <summary>The status text kind for news about <paramref name="winner"/> and <paramref name="loser"/>: good, bad or neutral for the players.</summary>
        internal static string Tone(GameController gc, ulong winner, ulong loser)
        {
            ulong players = Factions.PlayerKeys(gc);
            if ((players & winner) != 0) return "BuffSpecial";
            if ((players & loser) != 0) return "Debuff";
            return "ItemPickupSlow";
        }

        /// <summary><c>the Crepes</c> → <c>The Crepes</c>.</summary>
        internal static string Capital(string text)
            => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        internal static string Plural(int key) => key >= 0 && key < Factions.Keys.Count ? FactionDisplay.Plural(Factions.Keys[key]) : "someone";

        internal static string Short(int key) => key >= 0 && key < Factions.Keys.Count ? FactionDisplay.Short(Factions.Keys[key]) : "Someone's";

        /// <summary>The squad's living members by type, for log lines: <c>2 × Gangbanger, 1 × Crepe Heavy</c>.</summary>
        internal static string Roster(Squad squad)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Agent m in squad.Members)
            {
                if (m == null || m.dead) continue;
                string name = SquadUnits.NameOf(m);
                if (!counts.ContainsKey(name))
                {
                    counts[name] = 0;
                    order.Add(name);
                }
                counts[name]++;
            }
            var parts = new List<string>();
            foreach (string name in order) parts.Add($"{counts[name]} × {name}");
            return parts.Count == 0 ? "nobody" : string.Join(", ", parts.ToArray());
        }
    }

    /// <summary>
    ///   A custom unit spawned without a living source agent (see <see cref="Squads.SpawnUnits"/>) gets its character
    ///   before vanilla reads it: SpawnerMain only copies custom data from the agent a spawn came from, and pooled
    ///   agents keep the last one's.
    /// </summary>
    [HarmonyPatch(typeof(Agent), nameof(Agent.SetupAgentStats), typeof(string))]
    [HarmonyPriority(Priority.First)]
    internal static class Agent_SetupAgentStats_SquadUnit_Patch
    {
        private static void Prefix(Agent __instance)
        {
            if (Squads.PendingData != null && __instance.agentName == SquadUnits.Custom) __instance.customCharacterData = Squads.PendingData;
        }
    }
}
