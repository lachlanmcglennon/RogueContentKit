#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static RCK.AgentText;

namespace RCK.Social
{
    /// <summary>
    ///   The faction role traits, which act on events in play rather than when a pair is set up:
    ///   <list type="bullet">
    ///     <item><c>&lt;key&gt;_Vengeful</c>: when someone attacks or kills a member of the faction, every living holder
    ///     turns Hateful toward the attacker for the rest of the level. Holders that spawn later bear the same grudge.</item>
    ///     <item><c>&lt;key&gt;_Leader</c>: when the last living holder falls, the faction is routed for the rest of the
    ///     level. Its NPC members make peace with the players and their followers and flee fights.</item>
    ///     <item><c>RCK_Faction_Defector</c>: while the holder is in a player's party, its old factions and it hate each
    ///     other. This is stateless, so it also holds on later levels.</item>
    ///   </list>
    ///   Only the server acts; relationships reach clients the usual way. Grudges and routs belong to one level load.
    /// </summary>
    internal static class FactionEvents
    {
        private const int MaxLogsPerLevel = 40;
        private const float RescanSeconds = 5f;

        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        /// <summary>Attacker → the factions whose Vengeful holders hold a grudge against it.</summary>
        private static readonly Dictionary<Agent, ulong> grudges = new Dictionary<Agent, ulong>();
        private static ulong routed;

        private static readonly List<Agent> holders = new List<Agent>();
        private static ulong vengefulKeys;
        private static int scannedCount = -1;
        private static float scannedAt;

        static FactionEvents() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            grudges.Clear();
            routed = 0;
            holders.Clear();
            vengefulKeys = 0;
            scannedCount = -1;
            capped.Reset();
        }

        public static void Initialize()
        {
            if (!Rck.IsRckTrait(RckData.FactionDefectorTrait)) Rck.Log.LogError($"Factions: defector trait {RckData.FactionDefectorTrait} is not registered.");
            if (Array.IndexOf(RckData.FactionRoles, "Vengeful") < 0 || Array.IndexOf(RckData.FactionRoles, "Leader") < 0)
                Rck.Log.LogError("Factions: the Vengeful and Leader roles are not in the generated data.");
        }

        /// <summary>
        ///   The game controller when this is the server, after <see cref="LevelScope.Check(GameController)"/> has dropped
        ///   every system's state from an earlier level load.
        /// </summary>
        internal static GameController Server()
        {
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer) return null;
            LevelScope.Check(gc);
            return gc;
        }

        /// <summary>Living NPCs holding a Vengeful role, rescanned when the agent count changes or every few seconds.</summary>
        private static void ScanHolders(GameController gc)
        {
            List<Agent> agents = gc.agentList;
            if (agents == null) return;
            float now = Time.time;
            if (agents.Count == scannedCount && now - scannedAt < RescanSeconds) return;
            scannedCount = agents.Count;
            scannedAt = now;
            holders.Clear();
            vengefulKeys = 0;
            if (Factions.Keys.Count == 0) return;
            for (int i = 0; i < agents.Count; i++)
            {
                Agent a = agents[i];
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent) continue;
                Factions.RolesOf(a, out ulong vengeful, out _);
                if (vengeful == 0) continue;
                holders.Add(a);
                vengefulKeys |= vengeful;
            }
        }

        // ---- Vengeful ----

        /// <summary>Vanilla reports an attack on <paramref name="victim"/> by <paramref name="criminal"/> (EnforcerAlertAttack).</summary>
        internal static void OnAttack(Agent criminal, Agent victim)
        {
            if (!IsRealAttack(criminal, victim)) return;
            Harm(criminal, victim, "attacked");
            FactionBackup.OnAttack(criminal, victim);
        }

        /// <summary>
        ///   True when an EnforcerAlertAttack report is a hit that landed: not the attacks vanilla's own
        ///   EnforcerAlertAttack ignores (finished bodies, zombies, arena victims, duels, fights between players), nor the
        ///   victim's side of a clash.
        /// </summary>
        internal static bool IsRealAttack(Agent criminal, Agent victim)
        {
            if (criminal == null || victim == null || criminal == victim) return false;
            if ((victim.dead && !victim.justDied2 && !criminal.hasBitingAgent) || victim.zombified || victim.noEnforcerAlert) return false;
            if ((criminal.UID == victim.challengedToFightAgentID && victim.challengedToFight == 2)
                || (victim.UID == criminal.challengedToFightAgentID && criminal.challengedToFight == 2)) return false;
            if (criminal.isPlayer > 0 && victim.isPlayer > 0) return false;
            return victim.lastHitByAgent == criminal;
        }

        /// <summary>True when Vengeful holders of one of <paramref name="keys"/> hold a grudge against <paramref name="attacker"/>.</summary>
        internal static bool HasGrudge(Agent attacker, ulong keys)
        {
            if (attacker == null || Server() == null) return false;
            return grudges.TryGetValue(attacker, out ulong had) && (had & keys & ~routed) != 0;
        }

        /// <summary>The factions routed this level (the last <c>_Leader</c> fell); 0 on clients.</summary>
        internal static ulong Routed => Server() != null ? routed : 0;

        /// <summary><paramref name="victim"/> has just died, been knocked out or been arrested (SetupDeath).</summary>
        internal static void OnDeath(Agent victim)
        {
            if (victim == null || victim.objectAgent) return;
            GameController gc = Server();
            // Roles act on falls in play: not while a level loads, nor on an NPC a scene setter puts down at the start.
            if (gc == null || !gc.loadComplete || PlacedDown(victim)) return;
            // SetupDeath runs before vanilla credits the kill, so this is the killer ChangeHealth is about to credit.
            Agent killer = victim.justHitByAgent2 != null ? victim.justHitByAgent2 : victim.killedByAgentIndirect;
            if (!victim.zombified && killer != null && killer != victim && !(killer.isPlayer > 0 && victim.isPlayer > 0))
                Harm(killer, victim, "killed");
            Fallen(victim, gc);
            TurfWar.OnFallen(victim, killer != victim ? killer : null);
            ControlPoints.OnKill(victim, killer);
        }

        private static void Harm(Agent attacker, Agent victim, string how)
        {
            if (attacker.dead || attacker.objectAgent) return;
            GameController gc = Server();
            if (gc == null || !gc.loadComplete) return;
            ScanHolders(gc);
            ulong open = vengefulKeys & ~routed;
            if (open == 0) return;
            ulong keys = Factions.MemberKeys(victim, open);
            if (keys == 0) return;
            // No revenge on a member, and each faction takes against an attacker once.
            keys &= ~Factions.MemberKeys(attacker, keys);
            grudges.TryGetValue(attacker, out ulong had);
            keys &= ~had;
            if (keys == 0) return;
            grudges[attacker] = had | keys;

            int turned = 0;
            using (RelOps.Quietly())
            {
                for (int i = 0; i < holders.Count; i++)
                {
                    Agent h = holders[i];
                    try { if (Avenge(h, attacker, keys)) turned++; }
                    catch (Exception e) { SocialRules.LogOnce(h, "faction-vengeful", e); }
                }
            }
            Log($"Factions: {Describe(attacker)} {how} {Describe(victim)}; {turned} {Factions.Describe(keys)} Vengeful holder(s) turned on it.");
        }

        /// <summary>Holder <paramref name="h"/> turns Hateful toward <paramref name="attacker"/> for a grudge of <paramref name="grudgeKeys"/>.</summary>
        private static bool Avenge(Agent h, Agent attacker, ulong grudgeKeys)
        {
            if (h == null || attacker == null || h == attacker || h.dead || h.isPlayer != 0 || h.objectAgent || h.zombified || h.relationships == null) return false;
            if (attacker.dead || attacker.objectAgent) return false;
            Factions.RolesOf(h, out ulong vengeful, out _);
            if ((vengeful & grudgeKeys & ~routed) == 0) return false;
            if (SocialRules.Has(h, "Relationless") || SocialRules.Has(attacker, "Relationless")) return false;
            if (PartyPeace.ArePartyMates(h, attacker)) return false;
            if (routed != 0 && Factions.MemberKeys(h, routed) != 0) return false;
            // Aligned, Loyal and Submissive links stay (hate would turn Loyal Hateful); Hateful already is.
            int rank = SocialRules.EscalationRank(h.relationships.GetRelCode(attacker));
            if (rank == 0 || rank == 3) return false;
            h.relationships.SetRelHate(attacker, 5);
            return true;
        }

        // ---- Leader ----

        private static void Fallen(Agent leader, GameController gc)
        {
            Factions.RolesOf(leader, out _, out ulong led);
            if (led == 0 || (leader.resurrect && !leader.zombified)) return;
            ulong lost = led & ~routed;
            List<Agent> agents = gc.agentList;
            if (lost == 0 || agents == null) return;
            for (int i = 0; i < agents.Count && lost != 0; i++)
            {
                Agent a = agents[i];
                if (a == null || a == leader || a.dead || a.objectAgent) continue;
                Factions.RolesOf(a, out _, out ulong other);
                lost &= ~other;
            }
            if (lost == 0) return;
            routed |= lost;

            List<Agent> side = PlayerSide(gc);
            int count = 0;
            using (RelOps.Quietly())
            {
                foreach (Agent m in new List<Agent>(agents))
                {
                    if (m == leader || !IsRouted(m) || Factions.MemberKeys(m, lost) == 0) continue;
                    try
                    {
                        for (int j = 0; j < side.Count; j++) Settle(m, side[j]);
                        Flee(m);
                        count++;
                    }
                    catch (Exception e) { SocialRules.LogOnce(m, "faction-rout", e); }
                }
            }
            Log($"Factions: the last {Factions.Describe(lost)} leader ({Describe(leader)}) fell; {count} member(s) give up on the players and flee from fights.");
        }

        /// <summary>A living NPC member of a routed faction outside a player's party.</summary>
        private static bool IsRouted(Agent m)
        {
            if (routed == 0 || m == null || m.dead || m.isPlayer != 0 || m.objectAgent || m.relationships == null) return false;
            if (m.employer != null && m.employer.isPlayer > 0) return false;
            return Factions.MemberKeys(m, routed) != 0;
        }

        internal static bool IsPlayerSide(Agent a)
            => a != null && !a.dead && !a.objectAgent && a.relationships != null && (a.isPlayer > 0 || (a.employer != null && a.employer.isPlayer > 0));

        private static List<Agent> PlayerSide(GameController gc)
        {
            var side = new List<Agent>();
            List<Agent> agents = gc.agentList;
            for (int i = 0; agents != null && i < agents.Count; i++)
                if (IsPlayerSide(agents[i])) side.Add(agents[i]);
            if (gc.playerAgentList != null)
                foreach (Agent p in gc.playerAgentList)
                    if (IsPlayerSide(p) && !side.Contains(p)) side.Add(p);
            return side;
        }

        private static void Settle(Agent member, Agent other)
        {
            if (member == other || SocialRules.Has(member, "Relationless") || SocialRules.Has(other, "Relationless")) return;
            Territorial.Unregister(member, other);
            Calm(member, other);
            Calm(other, member);
        }

        /// <summary>Clears hate and strikes directly (vanilla SetRelHate can't lower hate from 5), and ends Annoyed or Hateful.</summary>
        private static void Calm(Agent source, Agent target) => RelOps.Calm(source, target, "Neutral");

        private static void Flee(Agent m)
        {
            if (SocialRules.Has(m, "Fearless")) return;
            m.mustFlee = true;
            m.wontFlee = false;
        }

        /// <summary>A scene-setter goal that starts the level with the NPC down (see RCK.Campaign's scene setters).</summary>
        internal static bool PlacedDown(Agent a)
        {
            string g = a.defaultGoal;
            return g == "Dead" || g == "Knocked Out" || g == "Arrested" || g == "Burned" || g == "Gibbed" || g == "Zombified";
        }

        // ---- Defector ----

        private static bool Defecting(Agent d)
            => d != null && d.employer != null && d.employer.isPlayer > 0 && !d.dead && SocialRules.Has(d, RckData.FactionDefectorTrait);

        /// <summary>
        ///   <paramref name="agent"/>'s employer changed (SetEmployer): a routed member that joins a player stops fleeing
        ///   every fight, and a defector joining a player is disowned.
        /// </summary>
        internal static void OnEmployed(Agent agent)
        {
            if (agent == null || agent.employer == null || agent.employer.isPlayer <= 0) return;
            GameController gc = Server();
            if (gc == null) return;
            TurfWar.OnEmployed(agent);
            if (routed != 0 && agent.mustFlee && Factions.MemberKeys(agent, routed) != 0 && !SocialRules.Has(agent, "Coward"))
                agent.mustFlee = false;
            if (!Defecting(agent) || gc.agentList == null) return;
            ulong old = Factions.DefectorKeys(agent, agent.employer);
            if (old == 0) return;
            int count = 0;
            using (RelOps.Quietly())
            {
                foreach (Agent m in new List<Agent>(gc.agentList))
                {
                    try { if (Disown(agent, m, old)) count++; }
                    catch (Exception e) { SocialRules.LogOnce(m, "faction-defector", e); }
                }
            }
            if (count > 0) Log($"Factions: defector {Describe(agent)} joined {Describe(agent.employer)}; {count} {Factions.Describe(old)} member(s) turned on it.");
        }

        /// <summary>Defector <paramref name="d"/> and <paramref name="m"/>, a member of its old factions <paramref name="old"/>, turn on each other.</summary>
        private static bool Disown(Agent d, Agent m, ulong old)
        {
            if (m == null || m == d || m.dead || m.isPlayer != 0 || m.objectAgent || m.relationships == null || d.relationships == null) return false;
            if (PartyPeace.ArePartyMates(d, m) || IsRouted(m)) return false;
            if (SocialRules.Has(d, "Relationless") || SocialRules.Has(m, "Relationless")) return false;
            if (Factions.MemberKeys(m, old) == 0) return false;
            bool changed = Hate(d, m);
            changed |= Hate(m, d);
            return changed;
        }

        /// <summary>SetRel overrides Aligned (hate alone leaves it), then hate 5 keeps the link from cooling.</summary>
        private static bool Hate(Agent source, Agent target)
        {
            Relationship rel = RelOps.Of(source, target);
            if (rel == null) return false;
            return RelOps.ForceHate(source, target, rel);
        }

        // ---- Pair setup ----

        /// <summary>
        ///   Runs after the relationship rules decided a pair (not party-mates, not Relationless): grudges, then
        ///   defectors, then the rout, so a routed member's peace with the players wins.
        /// </summary>
        internal static void AfterPairSetup(Agent a, Agent b)
        {
            if (a == null || b == null || a == b) return;
            GameController gc = Server();
            if (gc == null) return;
            bool da = Defecting(a), db = Defecting(b);
            if (grudges.Count == 0 && routed == 0 && !da && !db) return;
            using (RelOps.Quietly())
            {
                if (grudges.Count != 0)
                {
                    if (grudges.TryGetValue(b, out ulong gb)) Avenge(a, b, gb);
                    if (grudges.TryGetValue(a, out ulong ga)) Avenge(b, a, ga);
                }
                if (da) { ulong old = Factions.DefectorKeys(a, a.employer); if (old != 0) Disown(a, b, old); }
                if (db) { ulong old = Factions.DefectorKeys(b, b.employer); if (old != 0) Disown(b, a, old); }
                if (routed != 0)
                {
                    if (IsRouted(a) && IsPlayerSide(b)) { Settle(a, b); Flee(a); }
                    if (IsRouted(b) && IsPlayerSide(a)) { Settle(b, a); Flee(b); }
                }
            }
        }

        private static void Log(string line) => capped.Info(line);

        internal static void Forget(Agent agent)
        {
            if (agent == null) return;
            grudges.Remove(agent);
            Squads.Forget(agent);
        }
    }

    // A separate prefix from street innocence's on the same method; only the 4-argument overload is patched, the
    // 3-argument one forwards to it.
    [HarmonyPatch(typeof(GameController), nameof(GameController.EnforcerAlertAttack), typeof(Agent), typeof(Agent), typeof(float), typeof(Vector2))]
    internal static class GameController_EnforcerAlertAttack_FactionEvents_Patch
    {
        private static void Prefix(Agent criminal, Agent victim)
        {
            try { FactionEvents.OnAttack(criminal, victim); }
            catch (Exception e) { SocialRules.LogOnce(criminal, "faction-attack", e); }
        }
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.SetupDeath), typeof(PlayfieldObject), typeof(bool), typeof(bool))]
    internal static class StatusEffects_SetupDeath_FactionEvents_Patch
    {
        private static void Postfix(StatusEffects __instance)
        {
            try { FactionEvents.OnDeath(__instance.agent); }
            catch (Exception e) { SocialRules.LogOnce(__instance.agent, "faction-death", e); }
        }
    }

    // A pooled agent comes back as someone new, so a grudge against it goes.
    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleAwake))]
    internal static class Agent_RecycleAwake_FactionEvents_Patch
    {
        private static void Postfix(Agent __instance)
        {
            try { FactionEvents.Forget(__instance); }
            catch (Exception e) { SocialRules.LogOnce(__instance, "faction-recycle", e); }
        }
    }
}
