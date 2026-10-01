#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Faction respawn: a faction that holds turf (see <see cref="TurfWar"/>) keeps its numbers up. When its living NPC
    ///   members drop below its target (<c>Free + PerTurf × turfs held</c>, at most <c>Max</c>), and its cooldown has
    ///   passed, it respawns a squad (see <see cref="Squads"/>) of its units (see <see cref="SquadUnits"/>) at one of its
    ///   turfs that no player is near, no holder is fighting at and no foe stands at. The squad first restaffs the
    ///   empty posts of its faction's turfs; with <c>RCK_Turf_Capture</c> the rest march on the nearest turf that is
    ///   empty or held by a foe, fight for it, and hold it once it's theirs. Each faction's pool is unlimited or a count
    ///   per level. A faction that has lost all its turf (and has no attackers left out) is finished for the level.
    ///   The <c>RCK_Faction_Respawn</c> mutator turns it on for every faction holding turf at load, and
    ///   <c>[RCK]FactionRespawn::</c> for the ones it lists, with their settings (see <see cref="RespawnRules"/>).
    ///   Server only.
    /// </summary>
    internal static class FactionRespawn
    {
        private const float TickSeconds = 2f, RetrySeconds = 10f;
        private const float PlayerClear = 13f, FoeClear = 5f;
        private const int MaxLogsPerLevel = 40;

        private sealed class State
        {
            public int Key;
            public RespawnSettings Settings;
            /// <summary>Members left to respawn this level; <see cref="RespawnRules.Unlimited"/> for no limit.</summary>
            public int PoolLeft;
            public float NextAt;
            public bool Finished, NoUnitsLogged;
            public int Spawned;
        }

        private static readonly List<State> states = new List<State>();
        private static RespawnConfig config;
        private static bool started;
        private static float nextTick;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        static FactionRespawn() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            states.Clear();
            config = null;
            started = false;
            nextTick = 0f;
            capped.Reset();
        }

        public static void Initialize()
        {
            LevelMutators.Require(RespawnRules.Mutator, "Factions: respawn mutator");
            if (Array.IndexOf(RckData.FactionRoles, RespawnRules.Role) < 0)
                Rck.Log.LogError($"Factions: the {RespawnRules.Role} role is not in the generated data.");
        }

        private static bool Check(GameController gc) => gc != null;

        /// <summary>This level's respawn rules, parsed once.</summary>
        internal static RespawnConfig Config(GameController gc)
        {
            if (!Check(gc)) return null;
            if (config != null) return config;
            List<string> bodies = LevelMutators.Bodies(gc, RespawnRules.Prefix);
            config = RespawnRules.Parse(bodies, Factions.KeyIndex,
                (text, error) => Rck.Log.LogWarning($"Factions: ignored {RespawnRules.Prefix} entry \"{text}\": {error}."));
            return config;
        }

        /// <summary>Once a second from <see cref="FactionWar"/>, after the turfs.</summary>
        internal static void Tick(GameController gc, float now, float levelTime)
        {
            if (!Check(gc) || levelTime < 1f) return;
            if (!started) Start(gc, levelTime);
            if (states.Count == 0) return;
            if (!Interval.Due(ref nextTick, now, TickSeconds)) return;

            FactionMatrix matrix = FactionMatrix.Current();
            ulong[] hostileBy = FactionRaids.HostileBy(gc, out _);
            CheckFinished(gc);
            Respawn(gc, levelTime, hostileBy, matrix);
        }

        private static void Start(GameController gc, float levelTime)
        {
            started = true;
            RespawnConfig rules = Config(gc);
            SquadUnits.Prepare(gc);
            bool mutator = gc.challenges != null && gc.challenges.Contains(RespawnRules.Mutator);
            ulong held = TurfWar.StartKeys;
            if (mutator && held == 0) Log($"Factions: {RespawnRules.Mutator} is on, but no faction holds turf to respawn at.");
            ulong wanted = mutator ? held : 0;
            foreach (RespawnFaction f in rules.Factions.Values)
                if (f.Listed) wanted |= 1UL << f.Key;
            var on = new List<string>();
            int commanded = Command.Key;
            for (int key = 0; key < Factions.Keys.Count && wanted != 0; key++)
            {
                ulong bit = 1UL << key;
                if ((wanted & bit) == 0) continue;
                wanted &= ~bit;
                if (key == commanded)
                {
                    Log($"Factions: {Factions.Keys[key]} respawn is off: the player commands it.");
                    continue;
                }
                RespawnFaction entry = rules.Entry(key);
                if (entry != null && entry.Listed && entry.Pool == 0) continue;
                if ((held & bit) == 0)
                {
                    Log($"Factions: {Factions.Keys[key]} respawn is on, but it holds no turf to respawn at.");
                    continue;
                }
                RespawnSettings s = rules.For(key);
                int pool = entry != null && entry.Listed ? entry.Pool : RespawnRules.Unlimited;
                states.Add(new State { Key = key, Settings = s, PoolLeft = pool, NextAt = levelTime + s.Cooldown });
                on.Add($"{Factions.Keys[key]} (pool {(pool == RespawnRules.Unlimited ? "inf" : pool.ToString())}, {s.Free}+{s.PerTurf}/turf up to {s.Max}, every {s.Cooldown} s, squads of {s.Size})");
            }
            if (on.Count > 0) Log($"Factions: respawn on for {string.Join("; ", on.ToArray())}{(TurfWar.CaptureOn(gc) ? "" : "; without " + TurfWar.Mutator + " squads only hold turf")}.");
        }

        // ---- Respawning ----

        private static void Respawn(GameController gc, float levelTime, ulong[] hostileBy, FactionMatrix matrix)
        {
            if (states.Count == 0) return;
            int room = Squads.Room(Squads.Kind.Respawn);
            if (room <= 0) return;
            ulong due = 0;
            foreach (State s in states)
                if (!s.Finished && s.PoolLeft != 0 && levelTime >= s.NextAt) due |= 1UL << s.Key;
            if (due == 0) return;
            ulong routed = FactionEvents.Routed;
            int[] living = Factions.CountLiving(gc, due);
            foreach (State s in states)
            {
                ulong bit = 1UL << s.Key;
                if ((due & bit) == 0 || (routed & bit) != 0) continue;
                int target = s.Settings.Target(TurfWar.HeldCount(s.Key));
                int missing = target - living[s.Key];
                if (missing <= 0) continue;
                int n = Math.Min(Math.Min(s.Settings.Size, missing), room);
                if (s.PoolLeft != RespawnRules.Unlimited) n = Math.Min(n, s.PoolLeft);
                if (n <= 0) continue;
                IList<SquadUnits.Unit> units = SquadUnits.ForRespawn(gc, s.Key);
                if (units == null)
                {
                    if (!s.NoUnitsLogged) Log($"Factions: {Factions.Keys[s.Key]} can't respawn: it has no units and had no plain members at load.");
                    s.NoUnitsLogged = true;
                    s.NextAt = levelTime + s.Settings.Cooldown;
                    continue;
                }
                ulong foes = FactionRaids.FoesOf(s.Key, hostileBy, matrix);
                if (!SpawnPoint(gc, s.Key, foes, out TurfWar.Turf home, out int post))
                {
                    s.NextAt = levelTime + RetrySeconds;
                    continue;
                }
                var squad = new Squads.Squad { Kind = Squads.Kind.Respawn, Key = s.Key, Foes = foes };
                Agent sight = null;
                foreach (Agent h in home.Holders)
                    if (!TurfWar.IsResolved(h)) { sight = h; break; }
                int made = Squads.SpawnUnits(gc, units, home.Posts[post], sight, n, squad);
                if (made == 0)
                {
                    s.NextAt = levelTime + RetrySeconds;
                    continue;
                }
                room -= made;
                s.Spawned += made;
                if (s.PoolLeft != RespawnRules.Unlimited) s.PoolLeft = Math.Max(0, s.PoolLeft - made);
                s.NextAt = levelTime + s.Settings.Cooldown;
                string orders = SquadOrders.Deploy(gc, squad, home, levelTime, foes);
                Log($"Factions: {Factions.Keys[s.Key]} respawned {Squads.Roster(squad)} at its turf (owner {home.Owner}, chunk {home.Chunk}) with {living[s.Key]} of {target} member(s) alive; {orders}; pool {(s.PoolLeft == RespawnRules.Unlimited ? "inf" : s.PoolLeft.ToString())}.");
                if (room <= 0) return;
            }
        }

        /// <summary>
        ///   A post of one of <paramref name="key"/>'s turfs to spawn at: no player within <paramref name="playerClear"/>
        ///   (sight range by default), no holder of that turf in a fight and no foe near the post. The post nearest
        ///   <paramref name="near"/> when it's given; else turfs with empty posts come first, picked at random.
        /// </summary>
        internal static bool SpawnPoint(GameController gc, int key, ulong foes, out TurfWar.Turf turf, out int post, float playerClear = PlayerClear, Vector2? near = null)
        {
            turf = null;
            post = -1;
            ulong bit = 1UL << key;
            List<Vector2> foeSpots = foes == 0 ? null : FoeSpots(gc, foes);
            var best = new List<KeyValuePair<TurfWar.Turf, int>>();
            bool bestHasEmpty = false;
            float nearest = float.MaxValue;
            foreach (TurfWar.Turf t in TurfWar.Turfs)
            {
                if (t.Common || (t.Current & bit) == 0 || t.Posts.Count == 0 || Fighting(t)) continue;
                if (near.HasValue)
                {
                    for (int i = 0; i < t.Posts.Count; i++)
                    {
                        Vector2 p = t.Posts[i];
                        if ((playerClear > 0f && Squads.PlayerDistance(gc, p) < playerClear) || Near(foeSpots, p, FoeClear)) continue;
                        float d = Vector2.Distance(p, near.Value);
                        if (d >= nearest) continue;
                        nearest = d;
                        turf = t;
                        post = i;
                    }
                    continue;
                }
                int pick = -1;
                bool empty = false;
                for (int i = 0; i < t.Posts.Count; i++)
                {
                    Vector2 p = t.Posts[i];
                    if ((playerClear > 0f && Squads.PlayerDistance(gc, p) < playerClear) || Near(foeSpots, p, FoeClear)) continue;
                    bool e = TurfWar.PostEmpty(t, i, SquadOrders.PostRadius);
                    if (pick < 0 || (e && !empty))
                    {
                        pick = i;
                        empty = e;
                    }
                    if (empty) break;
                }
                if (pick < 0) continue;
                if (empty && !bestHasEmpty)
                {
                    best.Clear();
                    bestHasEmpty = true;
                }
                if (empty == bestHasEmpty) best.Add(new KeyValuePair<TurfWar.Turf, int>(t, pick));
            }
            if (near.HasValue) return turf != null;
            if (best.Count == 0) return false;
            KeyValuePair<TurfWar.Turf, int> chosen = best[UnityEngine.Random.Range(0, best.Count)];
            turf = chosen.Key;
            post = chosen.Value;
            return true;
        }

        private static bool Fighting(TurfWar.Turf t)
        {
            foreach (Agent h in t.Holders)
                if (!TurfWar.IsResolved(h) && h.inCombat) return true;
            return false;
        }

        private static List<Vector2> FoeSpots(GameController gc, ulong foes)
        {
            var spots = new List<Vector2>();
            if (gc.agentList == null) return spots;
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost) continue;
                if (Factions.MemberKeys(a, foes) != 0) spots.Add(a.tr.position);
            }
            return spots;
        }

        private static bool Near(List<Vector2> spots, Vector2 p, float radius)
        {
            if (spots == null) return false;
            foreach (Vector2 s in spots)
                if (Vector2.Distance(s, p) <= radius) return true;
            return false;
        }

        // ---- A faction's end ----

        /// <summary>A respawning faction with no turf left and no one out to take one is finished for the level.</summary>
        private static void CheckFinished(GameController gc)
        {
            foreach (State s in states)
            {
                if (s.Finished || TurfWar.HeldCount(s.Key) > 0 || SquadOrders.Marching(s.Key)) continue;
                s.Finished = true;
                Log($"Factions: {Factions.Keys[s.Key]} has lost all its turf; no more respawns this level ({s.Spawned} respawned).");
                Squads.Announce(gc, Squads.Tone(gc, 0, 1UL << s.Key), $"{Squads.Capital(Squads.Plural(s.Key))} have lost all their turf!");
            }
        }

        private static void Log(string line) => capped.Info(line);
    }
}
