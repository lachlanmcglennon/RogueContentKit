#nullable disable
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Turf expansion: every <c>Expand</c> seconds each faction that holds turf sends a party of up to <c>Party</c>
    ///   members to the nearest empty turf, or free racket (see <see cref="WarConfig.RacketsOn"/>), within <c>Reach</c>
    ///   tiles of its own. The party is made of its free roamers (members owning nothing, in no squad and under no
    ///   order) and the spare holders of its turfs (two on one post); it walks in and holds what it finds undefended
    ///   (see <see cref="SquadOrders"/>). The player's commanded faction (see <see cref="Command"/>) never expands on
    ///   its own. On with <c>RCK_Turf_Expansion</c> or a <c>[RCK]TurfWar::</c> entry. Server only.
    /// </summary>
    internal static class TurfExpansion
    {
        private const float TileSize = 0.64f;
        private const int MaxLogsPerLevel = 30;

        private static bool started, on;
        private static float nextAt;
        private static TurfWarSettings settings;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        static TurfExpansion() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            started = false;
            on = false;
            nextAt = 0f;
            settings = null;
            capped.Reset();
        }

        private static bool Check(GameController gc) => gc != null;

        /// <summary>Every second from <see cref="FactionWar"/>: sends the parties that are due.</summary>
        internal static void Tick(GameController gc, float levelTime)
        {
            if (!Check(gc) || levelTime < 1f || !TurfWar.Built) return;
            if (!started)
            {
                started = true;
                on = WarConfig.ExpansionOn(gc);
                if (!on) return;
                settings = WarConfig.Turf(gc);
                nextAt = levelTime + settings.Expand;
                Log($"Factions: turf expansion on: every {settings.Expand} s, parties of up to {settings.Party} within {settings.Reach} tiles{(WarConfig.RacketsOn(gc) ? ", rackets too" : "")}.");
            }
            if (!on || levelTime < nextAt) return;
            nextAt = levelTime + settings.Expand;
            Expand(gc, levelTime);
        }

        private static void Expand(GameController gc, float levelTime)
        {
            ulong routed = FactionEvents.Routed;
            int commanded = Command.Key;
            bool rackets = WarConfig.RacketsOn(gc);
            float reach = settings.Reach * TileSize;
            List<Agent> roamers = null;
            for (int key = 0; key < Factions.Keys.Count; key++)
            {
                ulong bit = 1UL << key;
                if (key == commanded || (routed & bit) != 0 || TurfWar.HeldCount(key) == 0) continue;
                TurfWar.Turf target = Target(key, reach, rackets);
                if (target == null) continue;
                if (roamers == null) roamers = Roamers(gc);
                List<Agent> party = Party(key, roamers, target);
                if (party.Count == 0) continue;
                foreach (Agent m in party)
                {
                    roamers.Remove(m);
                    SquadOrders.Attack(m, key, target, levelTime, manual: false);
                }
                Log($"Factions: {Factions.Keys[key]} sends {party.Count} member(s) to take the {SquadOrders.Describe(target)}.");
            }
        }

        /// <summary>The nearest empty turf or free racket within reach of <paramref name="key"/>'s turf that nobody of it is marching on.</summary>
        private static TurfWar.Turf Target(int key, float reach, bool rackets)
        {
            ulong bit = 1UL << key;
            var own = new List<Vector2>();
            foreach (TurfWar.Turf t in TurfWar.Turfs)
                if (!t.Ruined && (t.Current & bit) != 0) own.AddRange(t.Posts);
            if (own.Count == 0) return null;
            TurfWar.Turf best = null;
            float bestDistance = float.MaxValue;
            foreach (TurfWar.Turf t in TurfWar.Turfs)
            {
                if (t.Current != 0 || !SquadOrders.Attackable(t, key, 0, rackets) || SquadOrders.Targeted(t, key)) continue;
                foreach (Vector2 from in own)
                {
                    float d = Vector2.Distance(from, SquadOrders.NearestPost(t, from));
                    if (d > reach || d >= bestDistance) continue;
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>NPC faction members free to go: owning nothing, in no raid, backup or command squad, under no order.</summary>
        private static List<Agent> Roamers(GameController gc)
        {
            var list = new List<Agent>();
            if (gc.agentList == null) return list;
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.ownerID != 0 || a.isPlayer != 0 || a.dead || a.objectAgent) continue;
                if (SquadOrders.Of(a) != null || Factions.SwornKey(a) >= 0) continue;
                Squads.Squad s = Squads.Of(a);
                if (s != null && s.Kind != Squads.Kind.Guard && s.Kind != Squads.Kind.Respawn) continue;
                list.Add(a);
            }
            return list;
        }

        /// <summary>Up to <c>Party</c> of <paramref name="key"/>'s roamers and spare holders, nearest to <paramref name="target"/> first.</summary>
        private static List<Agent> Party(int key, List<Agent> roamers, TurfWar.Turf target)
        {
            ulong bit = 1UL << key;
            var candidates = new List<Agent>();
            foreach (Agent a in roamers)
                if (Squads.Eligible(a, bit, squadOk: true)) candidates.Add(a);
            foreach (TurfWar.Turf t in TurfWar.Turfs)
            {
                if ((t.Current & bit) == 0) continue;
                var kept = new List<Vector2>();
                foreach (Agent h in t.Holders)
                {
                    if (TurfWar.IsResolved(h)) continue;
                    Vector2 post = h.startingPosition;
                    bool spare = false;
                    foreach (Vector2 k in kept)
                        if (Vector2.Distance(k, post) <= SquadOrders.PostRadius) { spare = true; break; }
                    if (!spare) kept.Add(post);
                    else if (!h.inCombat && Squads.Eligible(h, bit, squadOk: true) && !candidates.Contains(h)) candidates.Add(h);
                }
            }
            Vector2 at = target.Posts[0];
            candidates.Sort((a, b) => Vector2.Distance(a.tr.position, at).CompareTo(Vector2.Distance(b.tr.position, at)));
            if (candidates.Count > settings.Party) candidates.RemoveRange(settings.Party, candidates.Count - settings.Party);
            return candidates;
        }

        private static void Log(string text)
        {
            capped.Info(text);
        }
    }
}
