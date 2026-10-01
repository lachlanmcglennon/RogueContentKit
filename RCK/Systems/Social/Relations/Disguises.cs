#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static RCK.AgentText;

namespace RCK.Social
{
    /// <summary>
    ///   Faction disguises (mutator <c>RCK_Faction_Disguises</c>, or any <c>[RCK]Disguise::</c> map). A player wearing
    ///   a faction's headpiece (a HatBlue passes for a Crepe) counts as a member of that faction for its private rooms,
    ///   turf and recruiting, and its members stop being Annoyed or Hateful toward the player. The disguise is blown for
    ///   the rest of the level, for every player, when a member of that faction turns on the wearer in play (the
    ///   player was seen stealing, trespassing, breaking in...), when the wearer attacks a member, or when a member sees
    ///   the wearer attack anyone. Then the calmed members go back to how they felt. A headpiece the character started
    ///   with is its look, not a disguise, and neither is one for a faction the player already belongs to. Cameras,
    ///   turrets and other factions aren't fooled. Only the server acts.
    /// </summary>
    internal static class Disguises
    {
        private const float TickSeconds = 0.5f;
        private const int MaxDropsPerFaction = 3;
        private const int MaxLogsPerLevel = 30;

        private sealed class Calmed
        {
            public Agent Member;
            public int MemberId;
            public string Rel;
            public float Hate;
            public int Strikes;
        }

        private sealed class Wearer
        {
            public int Key = -1;
            public readonly List<Calmed> Calmed = new List<Calmed>();
        }

        private static bool on;
        private static Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
        private static ulong blown;
        private static readonly Dictionary<int, int> drops = new Dictionary<int, int>();
        private static readonly Dictionary<Agent, Wearer> wearers = new Dictionary<Agent, Wearer>();
        private static float nextTick;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        /// <summary>Our own relationship changes and pair setups, which never blow a disguise.</summary>
        internal static int Quiet;

        /// <summary>True while disguises are on in this level (read cheaply by the hooks).</summary>
        internal static bool Active { get; private set; }

        public static void Initialize()
        {
            LevelMutators.Require(DisguiseRules.Mutator, "Disguises: mutator");
            foreach (KeyValuePair<string, string> d in DisguiseRules.Defaults)
                if (Factions.KeyIndex(d.Value) < 0) Rck.Log.LogError($"Disguises: default {d.Key} names unknown faction {d.Value}.");
        }

        // ---- Level state ----

        // Initialize runs at startup, before any level; a level already seen is replayed in case it doesn't.
        static Disguises()
        {
            LevelScope.Ended += OnEnded;
            LevelScope.Loaded += OnLoaded;
            if (LevelScope.Id > 0) OnEnded();
            if (LevelScope.Id > 0 && !LevelScope.Loading) OnLoaded();
        }

        private static void OnEnded()
        {
            blown = 0;
            drops.Clear();
            wearers.Clear();
            capped.Reset();
            GameController gc = GameController.gameController;
            if (gc != null) ReadMutators(gc, log: false);
        }

        // The level's own mutators are in place once it has loaded.
        private static void OnLoaded()
        {
            GameController gc = GameController.gameController;
            if (gc != null) ReadMutators(gc, log: true);
        }

        private static void ReadMutators(GameController gc, bool log)
        {
            List<string> bodies = LevelMutators.Bodies(gc, DisguiseRules.Prefix);
            on = LevelMutators.Has(gc, DisguiseRules.Mutator) || bodies.Count > 0;
            Active = on && Factions.Keys.Count > 0;
            map = DisguiseRules.Build(bodies, Factions.KeyIndex,
                (entry, error) => Rck.Log.LogWarning($"Disguises: ignored {DisguiseRules.Prefix} entry \"{entry}\": {error}."));
            if (Active && log) Rck.Log.LogInfo($"Disguises: on, {map.Count} headpiece(s) pass for a faction.");
        }

        private static GameController Server()
        {
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer) return null;
            LevelScope.Check(gc);
            return gc;
        }

        /// <summary>For the private-access checks, which clients run too: the level's map without the server's blown state.</summary>
        private static bool EnsureLevel()
        {
            GameController gc = GameController.gameController;
            if (gc == null) return false;
            LevelScope.Check(gc);
            return Active;
        }

        // ---- Who is disguised ----

        /// <summary>The faction <paramref name="player"/>'s headpiece passes for, whether or not it's blown; -1 if none.</summary>
        private static int KeyOf(Agent player)
        {
            InvDatabase inv = player.inventory;
            InvItem head = inv != null ? inv.equippedArmorHead : null;
            if (head == null || string.IsNullOrEmpty(head.invItemName) || head == inv.defaultArmorHead) return -1;
            if (head.invItemName == inv.startingHeadPiece || !map.TryGetValue(head.invItemName, out int k)) return -1;
            // A member already wears its colours as itself.
            return Factions.MemberKeys(player, 1UL << k) != 0 ? -1 : k;
        }

        /// <summary>The faction bit <paramref name="agent"/> passes as a member of through a disguise, or 0.</summary>
        internal static ulong MaskOf(Agent agent)
        {
            if (agent == null || agent.isPlayer == 0 || agent.dead || !EnsureLevel()) return 0;
            int k = KeyOf(agent);
            if (k < 0) return 0;
            ulong bit = 1UL << k;
            return (blown & bit) != 0 ? 0 : bit;
        }

        /// <summary>True when <paramref name="visitor"/>'s disguise passes for one of <paramref name="holder"/>'s factions.</summary>
        internal static bool Covers(Agent holder, Agent visitor)
        {
            ulong mask = MaskOf(visitor);
            return mask != 0 && Factions.MemberKeys(holder, mask) != 0;
        }

        // ---- Calming and restoring ----

        /// <summary>
        ///   Twice a second (from any NPC's brain update, so also on the first ticks of a level): reads the level's
        ///   mutators and notices headpieces put on or taken off.
        /// </summary>
        internal static void Tick()
        {
            float now = Time.time;
            if (!Interval.Due(ref nextTick, now, TickSeconds)) return;
            GameController gc = Server();
            if (gc == null || !Active || !gc.loadComplete || gc.playerAgentList == null) return;
            foreach (Agent p in gc.playerAgentList)
            {
                if (p == null || p.isPlayer == 0) continue;
                int k = p.dead ? -1 : KeyOf(p);
                if (k >= 0 && (blown & (1UL << k)) != 0) k = -1;
                if (!wearers.TryGetValue(p, out Wearer w))
                {
                    if (k < 0) continue;
                    wearers[p] = w = new Wearer();
                }
                if (w.Key == k) continue;
                Restore(p, w);
                w.Key = k;
                if (k < 0) continue;
                int calmed = CalmAll(gc, p, w);
                Text(p, "Buff", $"Disguised as {FactionDisplay.Short(Factions.Keys[k])}");
                Log($"Disguises: {Describe(p)} passes for {Factions.Keys[k]}; {calmed} member(s) calmed.");
            }
        }

        private static int CalmAll(GameController gc, Agent player, Wearer w)
        {
            int count = 0;
            ulong bit = 1UL << w.Key;
            List<Agent> agents = gc.agentList;
            for (int i = 0; agents != null && i < agents.Count; i++)
                if (Calm(agents[i], player, w, bit)) count++;
            return count;
        }

        private static bool Calm(Agent m, Agent player, Wearer w, ulong bit)
        {
            if (m == null || m == player || m.dead || m.isPlayer != 0 || m.objectAgent || m.relationships == null) return false;
            if (m.opponent == player || SocialRules.Has(m, "Relationless") || PartyPeace.ArePartyMates(m, player)) return false;
            if (Factions.MemberKeys(m, bit) == 0 || FactionEvents.HasGrudge(player, bit)) return false;
            Relationship rel = RelOps.Of(m, player);
            if (rel == null) return false;
            if (rel.relTypeCode != relStatus.Hostile && rel.relTypeCode != relStatus.Annoyed) return false;
            foreach (Calmed c in w.Calmed)
                if (c.Member == m && c.MemberId == m.agentID) return false;
            w.Calmed.Add(new Calmed { Member = m, MemberId = m.agentID, Rel = rel.relType, Hate = rel.relHate, Strikes = rel.relStrikes });
            using (RelOps.Quietly(disguises: true))
            {
                rel.relHate = 0f;
                rel.relStrikes = 0;
                m.relationships.SetRel(player, "Neutral");
            }
            return true;
        }

        /// <summary>Calmed members that are still calm feel as they did before the disguise.</summary>
        private static void Restore(Agent player, Wearer w)
        {
            if (w.Calmed.Count == 0) return;
            try
            {
                using (RelOps.Quietly(disguises: true))
                {
                    foreach (Calmed c in w.Calmed)
                    {
                        Agent m = c.Member;
                        if (m == null || m.agentID != c.MemberId || m.dead || m.relationships == null || player == null) continue;
                        Relationship rel = RelOps.Of(m, player);
                        if (rel == null) continue;
                        if (rel.relTypeCode != relStatus.Neutral || PartyPeace.ArePartyMates(m, player)) continue;
                        try
                        {
                            m.relationships.SetRel(player, c.Rel);
                            rel.relHate = Mathf.Max(rel.relHate, c.Hate);
                            rel.relStrikes = Mathf.Max(rel.relStrikes, c.Strikes);
                        }
                        catch (Exception e) { SocialRules.LogOnce(m, "disguise-restore", e); }
                    }
                }
            }
            finally { w.Calmed.Clear(); }
        }

        /// <summary>A pair set up in play (a new agent): a member calms toward a disguised player.</summary>
        internal static void AfterPairSetup(Agent a, Agent b)
        {
            if (!Active || wearers.Count == 0) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete) return;
            Agent player = a.isPlayer > 0 ? a : b.isPlayer > 0 ? b : null;
            Agent other = player == a ? b : a;
            if (player == null || other.isPlayer != 0 || !wearers.TryGetValue(player, out Wearer w) || w.Key < 0) return;
            Calm(other, player, w, 1UL << w.Key);
        }

        // ---- Blowing a disguise ----

        private static void Blow(Agent player, Wearer w, string how)
        {
            int k = w.Key;
            if (k < 0) return;
            blown |= 1UL << k;
            Restore(player, w);
            w.Key = -1;
            Text(player, "Debuff", $"{FactionDisplay.Short(Factions.Keys[k])} disguise blown!");
            Log($"Disguises: {Describe(player)}'s {Factions.Keys[k]} disguise is blown for the level ({how}).");
        }

        /// <summary><paramref name="member"/> just turned Annoyed or Hateful toward <paramref name="player"/> in play.</summary>
        internal static void OnWorsened(Agent member, Agent player)
        {
            if (Quiet > 0 || HostilityDiagnostics.Suppress || member == null || member.isPlayer != 0) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || !wearers.TryGetValue(player, out Wearer w) || w.Key < 0) return;
            if (Factions.MemberKeys(member, 1UL << w.Key) == 0) return;
            Blow(player, w, $"{Describe(member)} turned on the wearer");
        }

        /// <summary>Vanilla reports <paramref name="criminal"/> attacking <paramref name="victim"/>.</summary>
        internal static void OnAttack(Agent criminal, Agent victim)
        {
            if (!FactionEvents.IsRealAttack(criminal, victim) || criminal.isPlayer == 0) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete || !wearers.TryGetValue(criminal, out Wearer w) || w.Key < 0) return;
            ulong bit = 1UL << w.Key;
            if (victim.isPlayer == 0 && Factions.MemberKeys(victim, bit) != 0)
            {
                Blow(criminal, w, $"attacked member {Describe(victim)}");
                return;
            }
            List<Agent> agents = gc.agentList;
            for (int i = 0; agents != null && i < agents.Count; i++)
            {
                Agent m = agents[i];
                if (m == null || m == victim || m.dead || m.isPlayer != 0 || m.objectAgent || m.brain == null || !m.brain.active) continue;
                if (Factions.MemberKeys(m, bit) == 0 || !Sees(m, criminal)) continue;
                Blow(criminal, w, $"{Describe(m)} saw it attack {Describe(victim)}");
                return;
            }
        }

        private static bool Sees(Agent watcher, Agent other)
        {
            if (other.invisible || other.ghost || watcher.movement == null) return false;
            if (!watcher.movement.InVisionBoundsAgent(other)) return false;
            if (Vector2.Distance(watcher.curPosition, other.curPosition) >= watcher.LOSRange / other.hardToSeeFromDistance) return false;
            return watcher.movement.HasLOSAgent(other);
        }

        // ---- Headpieces from the fallen ----

        /// <summary>
        ///   A faction member whose look includes its faction's headpiece, felled by a player's side, leaves one behind
        ///   (vanilla never drops a look headpiece), at most a few per faction per level.
        /// </summary>
        internal static void OnFell(Agent victim)
        {
            if (victim == null || victim.isPlayer != 0 || victim.objectAgent || victim.zombified || victim.inventory == null) return;
            GameController gc = Server();
            if (gc == null || !Active || !gc.loadComplete) return;
            InvItem head = victim.inventory.equippedArmorHead ?? victim.inventory.defaultArmorHead;
            if (head == null || string.IsNullOrEmpty(head.invItemName) || !map.TryGetValue(head.invItemName, out int k)) return;
            if (Factions.MemberKeys(victim, 1UL << k) == 0) return;
            Agent killer = victim.justHitByAgent2 != null ? victim.justHitByAgent2 : victim.killedByAgentIndirect;
            if (killer == null || !(killer.isPlayer > 0 || (killer.employer != null && killer.employer.isPlayer > 0))) return;
            drops.TryGetValue(k, out int n);
            if (n >= MaxDropsPerFaction) return;
            drops[k] = n + 1;
            gc.spawnerMain.SpawnItem(victim.tr.position, head.invItemName);
        }

        // ---- Helpers ----

        private static void Text(Agent player, string type, string text)
        {
            try { GameController.gameController.spawnerMain.SpawnStatusText(player, type, text); }
            catch (Exception e) { SocialRules.LogOnce(player, "disguise-text", e); }
        }

        private static void Log(string line) => capped.Info(line);

        internal static void Forget(Agent agent)
        {
            if (agent != null) wearers.Remove(agent);
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_Disguises_Patch
    {
        private static void Postfix()
        {
            try { Disguises.Tick(); }
            catch (Exception e) { SocialRules.LogOnce(null, "disguise-tick", e); }
        }
    }

    // Pair setups (at load, and for agents that spawn later) are the rules at work, not the NPC noticing anything.
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetupRelationshipOriginal), typeof(Agent))]
    internal static class Relationships_SetupRelationshipOriginal_Disguises_Patch
    {
        private static void Prefix() => Disguises.Quiet++;
        private static Exception Finalizer(Exception __exception)
        {
            Disguises.Quiet--;
            return __exception;
        }
    }

    // The 2-argument SetRel forwards here; vanilla's strike and hate logic (DetermineRel) turns relationships through it.
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetRel), typeof(Agent), typeof(string), typeof(bool))]
    internal static class Relationships_SetRel_Disguises_Patch
    {
        private static void Prefix(Agent ___agent, Agent otherAgent, string newRel, out bool __state)
        {
            __state = false;
            if (!Disguises.Active || otherAgent == null || otherAgent.isPlayer == 0 || ___agent == null || ___agent.isPlayer != 0) return;
            if (newRel != "Hateful" && newRel != "Annoyed") return;
            Relationship rel = RelOps.Of(___agent, otherAgent);
            if (rel == null) return;
            relStatus code = rel.relTypeCode;
            __state = code != relStatus.Hostile && code != relStatus.Annoyed;
        }

        private static void Postfix(Agent ___agent, Agent otherAgent, bool __state)
        {
            if (!__state) return;
            try
            {
                Relationship rel = RelOps.Of(___agent, otherAgent);
                if (rel != null && (rel.relTypeCode == relStatus.Hostile || rel.relTypeCode == relStatus.Annoyed)) Disguises.OnWorsened(___agent, otherAgent);
            }
            catch (Exception e) { SocialRules.LogOnce(___agent, "disguise-rel", e); }
        }
    }

    [HarmonyPatch(typeof(GameController), nameof(GameController.EnforcerAlertAttack), typeof(Agent), typeof(Agent), typeof(float), typeof(Vector2))]
    internal static class GameController_EnforcerAlertAttack_Disguises_Patch
    {
        private static void Prefix(Agent criminal, Agent victim)
        {
            if (!Disguises.Active) return;
            try { Disguises.OnAttack(criminal, victim); }
            catch (Exception e) { SocialRules.LogOnce(criminal, "disguise-attack", e); }
        }
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.SetupDeath), typeof(PlayfieldObject), typeof(bool), typeof(bool))]
    internal static class StatusEffects_SetupDeath_Disguises_Patch
    {
        private static void Postfix(StatusEffects __instance)
        {
            if (!Disguises.Active) return;
            try { Disguises.OnFell(__instance.agent); }
            catch (Exception e) { SocialRules.LogOnce(__instance.agent, "disguise-drop", e); }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleAwake))]
    internal static class Agent_RecycleAwake_Disguises_Patch
    {
        private static void Postfix(Agent __instance) => Disguises.Forget(__instance);
    }
}
