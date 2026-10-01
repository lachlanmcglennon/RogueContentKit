#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Orders for faction members on the move between turfs (see <see cref="TurfWar"/>): hold a post, or march on a
    ///   turf, fight for it, walk in once it's undefended and hold it once it's theirs. Automatic orders (respawned
    ///   squads, expansion parties) move on to the next turf when one can't be taken; manual ones (the commander's, see
    ///   <see cref="Command"/>) hold the nearest turf instead. Server only, and the orders belong to one level load.
    /// </summary>
    internal static class SquadOrders
    {
        private const float TickSeconds = 2f, AttackSeconds = 300f, ManualAttackSeconds = 600f;
        internal const float PostRadius = 0.5f, ClaimRadius = 2f;

        internal sealed class Order
        {
            public int Key;
            /// <summary>The turf it's marching on; null once it holds one.</summary>
            public TurfWar.Turf Target;
            public float Deadline;
            /// <summary>Given by the commander: when its target can't be taken it holds the nearest turf instead of picking another.</summary>
            public bool Manual;
            /// <summary>Holding this turf (null when marching or holding nothing).</summary>
            public TurfWar.Turf Holding;
            /// <summary>Where it's headed or standing (the commander's map draws a line there).</summary>
            public Vector2 Goal;
            public bool HasGoal;
        }

        private static readonly Dictionary<Agent, Order> orders = new Dictionary<Agent, Order>();
        private static float nextTick;

        static SquadOrders() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            orders.Clear();
            nextTick = 0f;
        }

        private static bool Check() => FactionEvents.Server() != null;

        internal static int Count => Check() ? orders.Count : 0;

        /// <summary>The order <paramref name="m"/> follows, or null.</summary>
        internal static Order Of(Agent m) => m != null && Check() && orders.TryGetValue(m, out Order o) ? o : null;

        /// <summary>Every 2 s from <see cref="FactionWar"/>: follows up the marching members.</summary>
        internal static void Tick(GameController gc, float now, float levelTime)
        {
            if (!Check() || orders.Count == 0) return;
            if (!Interval.Due(ref nextTick, now, TickSeconds)) return;
            ulong[] hostileBy = FactionRaids.HostileBy(gc, out _);
            Retask(gc, levelTime, hostileBy, FactionMatrix.Current());
        }

        /// <summary>A new squad restaffs empty posts, then marches on a turf (with capture on) or holds its home turf.</summary>
        internal static string Deploy(GameController gc, Squads.Squad squad, TurfWar.Turf home, float levelTime, ulong foes)
        {
            int restaffed = 0, extra = 0;
            var rest = new List<Agent>();
            foreach (Agent m in squad.Members)
            {
                if (m == null || m.dead) continue;
                if (EmptyPost(squad.Key, home, m.tr.position, out TurfWar.Turf t, out int post))
                {
                    Hold(m, squad.Key, t, post, false);
                    restaffed++;
                }
                else rest.Add(m);
            }
            if (rest.Count == 0) return $"{restaffed} restaffed";
            TurfWar.Turf target = TurfWar.CaptureOn(gc) ? NearestTarget(rest[0].tr.position, squad.Key, foes, null) : null;
            if (target != null)
            {
                foreach (Agent m in rest) Attack(m, squad.Key, target, levelTime, false);
                return $"{restaffed} restaffed, {rest.Count} march on {Describe(target)}";
            }
            foreach (Agent m in rest)
            {
                Hold(m, squad.Key, home, home.Posts.Count > 0 ? extra % home.Posts.Count : -1, false);
                extra++;
            }
            return $"{restaffed} restaffed, {extra} extra holder(s)";
        }

        /// <summary>The nearest empty post of <paramref name="key"/>'s turfs (not rackets), <paramref name="first"/>'s before any other's.</summary>
        internal static bool EmptyPost(int key, TurfWar.Turf first, Vector2 from, out TurfWar.Turf turf, out int post)
        {
            turf = null;
            post = -1;
            ulong bit = 1UL << key;
            if (first != null && !first.Common && (first.Current & bit) != 0)
                for (int i = 0; i < first.Posts.Count; i++)
                    if (TurfWar.PostEmpty(first, i, PostRadius))
                    {
                        turf = first;
                        post = i;
                        return true;
                    }
            float best = float.MaxValue;
            foreach (TurfWar.Turf t in TurfWar.Turfs)
            {
                if (t == first || t.Common || (t.Current & bit) == 0) continue;
                for (int i = 0; i < t.Posts.Count; i++)
                {
                    float d = Vector2.Distance(from, t.Posts[i]);
                    if (d >= best || !TurfWar.PostEmpty(t, i, PostRadius)) continue;
                    best = d;
                    turf = t;
                    post = i;
                }
            }
            return turf != null;
        }

        /// <summary><paramref name="m"/> holds post <paramref name="post"/> of <paramref name="t"/> for <paramref name="key"/>.</summary>
        internal static void Hold(Agent m, int key, TurfWar.Turf t, int post, bool manual)
        {
            if (!Check()) return;
            // A holder moved to another turf stops holding its old one, so that one can fall or be claimed.
            TurfWar.Leave(m);
            TurfWar.Assign(t, m, post);
            var o = new Order { Key = key, Manual = manual, Holding = t };
            if (t != null && post >= 0 && post < t.Posts.Count)
            {
                o.Goal = t.Posts[post];
                o.HasGoal = true;
            }
            orders[m] = o;
        }

        /// <summary><paramref name="m"/> marches on <paramref name="target"/> for <paramref name="key"/>.</summary>
        internal static void Attack(Agent m, int key, TurfWar.Turf target, float levelTime, bool manual)
        {
            if (!Check()) return;
            TurfWar.Leave(m);
            Vector2 goal = NearestPost(target, m.tr.position);
            orders[m] = new Order { Key = key, Target = target, Deadline = levelTime + (manual ? ManualAttackSeconds : AttackSeconds), Manual = manual, Goal = goal, HasGoal = true };
            Squads.March(m, goal);
        }

        /// <summary><paramref name="m"/> walks to <paramref name="to"/> and waits there, holding nothing (the commander's recall).</summary>
        internal static void MoveTo(Agent m, int key, Vector2 to)
        {
            if (!Check()) return;
            TurfWar.Leave(m);
            orders[m] = new Order { Key = key, Manual = true, Goal = to, HasGoal = true };
            Squads.March(m, to);
        }

        internal static void Forget(Agent m)
        {
            if (m != null && Check()) orders.Remove(m);
        }

        /// <summary>
        ///   True if <paramref name="a"/> follows a commander's order anywhere but <paramref name="t"/> (marching on
        ///   another turf, or moving or waiting somewhere), so a turf falling as it passes doesn't take it.
        /// </summary>
        internal static bool Elsewhere(Agent a, TurfWar.Turf t)
            => a != null && Check() && orders.TryGetValue(a, out Order o) && o.Manual && o.Target != t;

        /// <summary><paramref name="m"/> moved in to hold <paramref name="t"/> after a fight (see TurfWar.Garrison): its order is to hold it now.</summary>
        internal static void Garrisoned(Agent m, TurfWar.Turf t)
        {
            if (m == null || !Check() || !orders.TryGetValue(m, out Order o)) return;
            o.Target = null;
            o.Holding = t;
            o.Goal = m.startingPosition;
            o.HasGoal = true;
        }

        /// <summary>
        ///   True if <paramref name="key"/> may march on <paramref name="t"/>: it's not its own, and it's empty or held
        ///   by a foe it has no truce with. A racket (commoner turf) only for <paramref name="rackets"/>, while its
        ///   commoners live.
        /// </summary>
        internal static bool Attackable(TurfWar.Turf t, int key, ulong foes, bool rackets = false)
        {
            ulong bit = 1UL << key;
            if ((t.Current & bit) != 0 || t.Posts.Count == 0) return false;
            if (t.Common && (!rackets || t.Ruined)) return false;
            if (t.Current == 0) return true;
            return (t.Current & foes) != 0;
        }

        private static TurfWar.Turf NearestTarget(Vector2 from, int key, ulong foes, TurfWar.Turf skip)
        {
            TurfWar.Turf best = null;
            float bestDistance = float.MaxValue;
            foreach (TurfWar.Turf t in TurfWar.Turfs)
            {
                if (t == skip || !Attackable(t, key, foes)) continue;
                float d = Vector2.Distance(from, NearestPost(t, from));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }

        internal static Vector2 NearestPost(TurfWar.Turf t, Vector2 from)
        {
            Vector2 best = t.Posts[0];
            foreach (Vector2 p in t.Posts)
                if (Vector2.Distance(from, p) < Vector2.Distance(from, best)) best = p;
            return best;
        }

        /// <summary>Follows up the marching members: they hold what their faction took, claim what's undefended and move on from the rest.</summary>
        private static void Retask(GameController gc, float levelTime, ulong[] hostileBy, FactionMatrix matrix)
        {
            var foes = new Dictionary<int, ulong>();
            foreach (KeyValuePair<Agent, Order> kv in new List<KeyValuePair<Agent, Order>>(orders))
            {
                Agent m = kv.Key;
                Order o = kv.Value;
                if (Lost(m))
                {
                    orders.Remove(m);
                    continue;
                }
                TurfWar.Turf t = o.Target;
                if (t == null) continue;
                ulong bit = 1UL << o.Key;
                if (t.Holders.Contains(m))
                {
                    // It took the turf in a fight and moved in (see TurfWar.Garrison).
                    o.Target = null;
                    o.Holding = t;
                    o.Goal = m.startingPosition;
                    o.HasGoal = true;
                    continue;
                }
                if ((t.Current & bit) != 0)
                {
                    if (t.Common)
                    {
                        Hold(m, o.Key, t, UnityEngine.Random.Range(0, t.Posts.Count), o.Manual);
                        continue;
                    }
                    EmptyPost(o.Key, t, m.tr.position, out TurfWar.Turf at, out int post);
                    Hold(m, o.Key, at ?? t, at != null ? post : UnityEngine.Random.Range(0, t.Posts.Count), o.Manual);
                    continue;
                }
                if (!foes.TryGetValue(o.Key, out ulong f)) foes[o.Key] = f = FactionRaids.FoesOf(o.Key, hostileBy, matrix);
                if (!Attackable(t, o.Key, f, rackets: true) || levelTime > o.Deadline)
                {
                    TurfWar.Turf next = !o.Manual && TurfWar.CaptureOn(gc) ? NearestTarget(m.tr.position, o.Key, f, t) : null;
                    if (next != null) Attack(m, o.Key, next, levelTime, false);
                    else HoldNearest(m, o.Key, o.Manual);
                    continue;
                }
                if (TurfWar.Undefended(t) && NearAnyPost(t, m.tr.position, ClaimRadius)) Claim(gc, t, o.Key);
            }
        }

        /// <summary><paramref name="m"/> holds the nearest empty post of its faction's turfs, else any post of the nearest one; with none it has no order.</summary>
        internal static void HoldNearest(Agent m, int key, bool manual)
        {
            if (EmptyPost(key, null, m.tr.position, out TurfWar.Turf t, out int post))
            {
                Hold(m, key, t, post, manual);
                return;
            }
            ulong bit = 1UL << key;
            TurfWar.Turf best = null;
            float bestDistance = float.MaxValue;
            foreach (TurfWar.Turf turf in TurfWar.Turfs)
            {
                if (turf.Common || (turf.Current & bit) == 0 || turf.Posts.Count == 0) continue;
                float d = Vector2.Distance(m.tr.position, NearestPost(turf, m.tr.position));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = turf;
                }
            }
            if (best != null) Hold(m, key, best, UnityEngine.Random.Range(0, best.Posts.Count), manual);
            else if (manual) orders[m] = new Order { Key = key, Manual = true };
            else orders.Remove(m);
        }

        private static bool NearAnyPost(TurfWar.Turf t, Vector2 p, float radius)
        {
            foreach (Vector2 post in t.Posts)
                if (Vector2.Distance(post, p) <= radius) return true;
            return false;
        }

        /// <summary>Every living member marching on <paramref name="t"/> for <paramref name="key"/> moves in.</summary>
        private static void Claim(GameController gc, TurfWar.Turf t, int key)
        {
            var claimers = new List<Agent>();
            var manual = new HashSet<Agent>();
            foreach (KeyValuePair<Agent, Order> kv in orders)
                if (kv.Value.Target == t && kv.Value.Key == key && !Lost(kv.Key))
                {
                    claimers.Add(kv.Key);
                    if (kv.Value.Manual) manual.Add(kv.Key);
                }
            if (claimers.Count == 0) return;
            TurfWar.Claim(gc, t, key, claimers);
            foreach (Agent m in claimers) orders[m] = new Order { Key = key, Manual = manual.Contains(m), Holding = t, Goal = m.startingPosition, HasGoal = true };
        }

        /// <summary>True if <paramref name="m"/> can't take orders any more: gone, dead, raised, or a player's follower.</summary>
        internal static bool Lost(Agent m)
            => m == null || m.dead || m.zombified || m.ghost || m.disappeared || (m.employer != null && m.employer.isPlayer > 0);

        /// <summary>True while a member of <paramref name="key"/> marches on a turf.</summary>
        internal static bool Marching(int key)
        {
            if (!Check()) return false;
            foreach (KeyValuePair<Agent, Order> kv in orders)
                if (kv.Value.Key == key && kv.Value.Target != null && kv.Key != null && !kv.Key.dead) return true;
            return false;
        }

        /// <summary>True if a member of <paramref name="key"/> marches on <paramref name="t"/>.</summary>
        internal static bool Targeted(TurfWar.Turf t, int key)
        {
            if (!Check()) return false;
            foreach (KeyValuePair<Agent, Order> kv in orders)
                if (kv.Value.Target == t && kv.Value.Key == key && kv.Key != null && !kv.Key.dead) return true;
            return false;
        }

        internal static string Describe(TurfWar.Turf t)
            => t.Common
                ? $"{(t.Current == 0 ? "free" : Factions.Describe(t.Current))} racket (owner {t.Owner}, chunk {t.Chunk})"
                : $"{(t.Current == 0 ? "empty" : Factions.Describe(t.Current))} turf (owner {t.Owner}, chunk {t.Chunk})";
    }
}
