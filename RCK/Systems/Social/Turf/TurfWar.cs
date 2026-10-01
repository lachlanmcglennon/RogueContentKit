#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Turf capture. A turf is the owned floor one crew holds: the NPCs with the same owner ID and start chunk that
    ///   defend it (a <c>&lt;key&gt;_Territorial</c> trait or a <c>=Territorial</c> matrix entry) and belong to a faction,
    ///   listed once the level has loaded. When every holder is resolved (dead, knocked out, arrested, zombified, gone,
    ///   hired by a player or routed), the turf falls. With the <c>RCK_Turf_Capture</c> mutator it changes hands to the
    ///   faction of whoever took down the last holder, whose members move in as its guards (see <see cref="Squads"/>);
    ///   without it the turf just empties. Respawned squads (see <see cref="FactionRespawn"/>) restaff their faction's
    ///   empty posts and, with capture on, claim empty or undefended turfs by walking in. The <c>TurfTaken</c> gate
    ///   switch counts falls either way. With rackets on (see <see cref="WarConfig.RacketsOn"/>) each group of
    ///   commoners that owns a place is a racket too: a faction that walks in guards it, and its commoners side with
    ///   that faction until the racket is broken (its guards all resolved) or ruined (its commoners all gone). Rackets
    ///   never count as turf for respawn, raids, the gate or the legend's turf count. Server only.
    /// </summary>
    internal static class TurfWar
    {
        public const string Mutator = "RCK_Turf_Capture";
        private const float EvaluateSeconds = 2f;
        private const float GuardRadius = 8f;
        private const int MaxGuards = 3, GuardCopies = 2;
        private const int MaxLogsPerLevel = 30;

        internal sealed class Turf
        {
            public int Owner, Chunk;
            public Chunk ChunkReal;
            public ulong Original, Current;
            public readonly List<Agent> Holders = new List<Agent>();
            /// <summary>Where the original holders stood (and faced), one per holder listed at load.</summary>
            public readonly List<Vector2> Posts = new List<Vector2>();
            public readonly List<int> Angles = new List<int>();
            public Agent LastTaker;
            /// <summary>A racket: a place commoners own. Its <see cref="Folk"/> never hold it; the racketeers' guards do.</summary>
            public bool Common;
            /// <summary>A racket whose commoners are all gone: nobody can run it until a new owner moves in (see <see cref="Reopen"/>).</summary>
            public bool Ruined;
            /// <summary>The commoners who own a racket, listed at load.</summary>
            public readonly List<Agent> Folk = new List<Agent>();
            /// <summary>The faction a racket's commoners are tied to (Aligned both ways with its NPC members), 0 if none.</summary>
            public ulong Ties;
            /// <summary>A racket's first owner at load: its type, its place's kind, its sector and its goal, for a new owner.</summary>
            public string OwnerType, PlaceKind, OwnerGoal;
            public int OwnerSector;
            /// <summary>New owners a ruined racket has had this level, and the level time it was last ruined.</summary>
            public int Reopens;
            public float RuinedAt;
        }

        /// <summary>The vanilla types a Mobster can shake down (see <c>Agent.CanShakeDown</c>) that are common folk.</summary>
        private static readonly string[] Shakeable = { "Shopkeeper", "Bartender", "Clerk", "DrugDealer", "Athlete" };
        private const float ReopenSeconds = 30f, ReopenLatest = 90f, ReopenOutOfSight = 8f;

        private static readonly List<Turf> turfs = new List<Turf>();
        private static readonly List<Turf> none = new List<Turf>();
        private static bool built, tied;
        private static ulong startKeys;
        private static float nextEvaluate, clock;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        static TurfWar() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            turfs.Clear();
            built = false;
            tied = false;
            startKeys = 0;
            nextEvaluate = 0f;
            clock = 0f;
            capped.Reset();
        }

        public static void Initialize()
        {
            LevelMutators.Require(Mutator, "Factions: turf mutator");
        }

        private static GameController Server()
        {
            GameController gc = FactionEvents.Server();
            return gc != null && gc.loadComplete ? gc : null;
        }

        private static long Group(int owner, int chunk) => ((long)owner << 32) | (uint)chunk;

        private static void Build(GameController gc, Agent falling)
        {
            built = true;
            turfs.Clear();
            startKeys = 0;
            if (Factions.Keys.Count == 0 || gc.agentList == null) return;
            var byGroup = new Dictionary<long, Turf>();
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.isPlayer != 0 || a.objectAgent || a.ownerID == 0 || a.employer != null) continue;
                if ((a.dead && a != falling) || a.zombified || a.ghost || FactionEvents.PlacedDown(a)) continue;
                ulong keys = Factions.KeysOf(a);
                if (keys == 0) continue;
                Factions.GradesOf(a, out _, out ulong territorial, out _);
                if (territorial == 0 && !Territorial.HoldsTurf(a)) continue;
                long id = Group(a.ownerID, a.startingChunk);
                if (!byGroup.TryGetValue(id, out Turf t))
                {
                    byGroup[id] = t = new Turf { Owner = a.ownerID, Chunk = a.startingChunk };
                    turfs.Add(t);
                }
                if (t.ChunkReal == null && a.hasStartingChunkReal) t.ChunkReal = a.startingChunkReal;
                t.Original |= keys;
                t.Holders.Add(a);
                t.Posts.Add(a.startingPosition);
                t.Angles.Add(a.startingAngle);
            }
            foreach (Turf t in turfs)
            {
                t.Current = t.Original;
                startKeys |= t.Original;
            }
            int held = turfs.Count;
            int rackets = WarConfig.RacketsOn(gc) ? BuildRackets(gc, falling) : 0;
            if (held > 0) Log($"Factions: {held} turf(s) held by {Factions.Describe(startKeys)}{(rackets > 0 ? $"; {rackets} racket(s) free" : "")}.");
            else if (rackets > 0) Log($"Factions: no faction turf; {rackets} racket(s) free.");
        }

        /// <summary>
        ///   Each group of commoners that owns a place (same owner ID and start chunk), where no faction member owns
        ///   anything, becomes a free racket with the commoners' spots as its posts.
        /// </summary>
        private static int BuildRackets(GameController gc, Agent falling)
        {
            var keyed = new HashSet<long>();
            var byGroup = new Dictionary<long, Turf>();
            var found = new List<Turf>();
            ulong folkBit = Factions.CommonFolkBit;
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.isPlayer != 0 || a.objectAgent || a.ownerID == 0) continue;
                long id = Group(a.ownerID, a.startingChunk);
                if (Factions.KeysOf(a) != 0)
                {
                    keyed.Add(id);
                    continue;
                }
                if (a.employer != null || (a.dead && a != falling) || a.zombified || a.ghost || FactionEvents.PlacedDown(a)) continue;
                if (folkBit != 0 && Factions.MemberKeys(a, folkBit) == 0) continue;
                if (!byGroup.TryGetValue(id, out Turf t))
                {
                    byGroup[id] = t = new Turf
                    {
                        Owner = a.ownerID, Chunk = a.startingChunk, Common = true,
                        OwnerType = a.agentName, PlaceKind = a.startingChunkRealDescription, OwnerGoal = a.defaultGoal, OwnerSector = a.startingSector,
                    };
                    found.Add(t);
                }
                if (t.ChunkReal == null && a.hasStartingChunkReal) t.ChunkReal = a.startingChunkReal;
                t.Folk.Add(a);
                t.Posts.Add(a.startingPosition);
                t.Angles.Add(a.startingAngle);
            }
            int n = 0;
            foreach (Turf t in found)
            {
                if (keyed.Contains(Group(t.Owner, t.Chunk))) continue;
                turfs.Add(t);
                n++;
            }
            return n;
        }

        /// <summary>Once a second from <see cref="FactionWar"/>: lists the turfs, then checks them every 2 s.</summary>
        internal static void Tick(float now, float levelTime)
        {
            GameController gc = Server();
            if (gc == null) return;
            clock = levelTime;
            if (!built)
            {
                // Territorial registrations are made as the pairs are set up; give the last of them a moment.
                if (levelTime < 1f) return;
                Build(gc, null);
            }
            if (turfs.Count == 0) return;
            Reopen(gc);
            if (!Interval.Due(ref nextEvaluate, now, EvaluateSeconds)) return;
            foreach (Turf t in turfs.ToArray()) Evaluate(gc, t, null);
        }

        /// <summary><paramref name="victim"/> fell (SetupDeath); <paramref name="killer"/> is who vanilla is about to credit.</summary>
        internal static void OnFallen(Agent victim, Agent killer)
        {
            if (victim == null || victim.isPlayer != 0) return;
            GameController gc = Server();
            if (gc == null) return;
            if (!built) Build(gc, victim);
            Turf t = Find(victim);
            if (t == null) return;
            t.LastTaker = killer;
            Evaluate(gc, t, victim);
        }

        /// <summary>A holder hired by a player hands its turf over.</summary>
        internal static void OnEmployed(Agent agent)
        {
            if (agent == null || !built || agent.employer == null || agent.employer.isPlayer <= 0) return;
            GameController gc = Server();
            if (gc == null) return;
            Turf t = Find(agent);
            if (t == null) return;
            t.LastTaker = agent.employer;
            Evaluate(gc, t, null);
        }

        private static Turf Find(Agent holder)
        {
            foreach (Turf t in turfs)
            {
                if (t.Current != 0 && t.Holders.Contains(holder)) return t;
                if (t.Common && !t.Ruined && t.Folk.Contains(holder)) return t;
            }
            return null;
        }

        private static bool Resolved(Agent h)
        {
            if (h == null || h.dead || h.zombified || h.ghost || h.disappeared) return true;
            if (h.employer != null && h.employer.isPlayer > 0) return true;
            ulong routed = FactionEvents.Routed;
            return routed != 0 && Factions.MemberKeys(h, routed) != 0;
        }

        private static void Evaluate(GameController gc, Turf t, Agent falling)
        {
            if (t.Common)
            {
                EvaluateRacket(gc, t, falling);
                return;
            }
            if (t.Current == 0 || t.Holders.Count == 0) return;
            foreach (Agent h in t.Holders)
                if (h != falling && !Resolved(h)) return;
            Fall(gc, t);
        }

        private static void EvaluateRacket(GameController gc, Turf t, Agent falling)
        {
            if (t.Ruined) return;
            bool folk = false;
            foreach (Agent f in t.Folk)
                if (f != falling && !Resolved(f))
                {
                    folk = true;
                    break;
                }
            if (!folk)
            {
                Ruin(gc, t);
                return;
            }
            if (t.Current == 0 || t.Holders.Count == 0) return;
            foreach (Agent h in t.Holders)
                if (h != falling && !Resolved(h)) return;
            ulong lost = t.Current;
            Agent taker = t.LastTaker;
            t.LastTaker = null;
            t.Holders.Clear();
            Untie(gc, t);
            t.Current = 0;
            Log($"Factions: {Factions.Describe(lost)} racket (owner {t.Owner}, chunk {t.Chunk}) broken by {AgentText.Describe(taker)}.");
            Squads.Announce(gc, Squads.Tone(gc, 0, lost), $"{Squads.Short(Factions.LowestBit(lost))} racket broken");
        }

        /// <summary>A racket's commoners are all gone: nobody runs it now, and its guards go back to their faction's turf.</summary>
        private static void Ruin(GameController gc, Turf t)
        {
            t.Ruined = true;
            t.RuinedAt = clock;
            ulong lost = t.Current;
            Untie(gc, t);
            t.Current = 0;
            t.LastTaker = null;
            var guards = new List<Agent>(t.Holders);
            t.Holders.Clear();
            foreach (Agent g in guards)
            {
                if (Resolved(g)) continue;
                SquadOrders.Order o = SquadOrders.Of(g);
                if (o != null) SquadOrders.HoldNearest(g, o.Key, o.Manual);
            }
            Log($"Factions: {(lost == 0 ? "free" : Factions.Describe(lost))} racket (owner {t.Owner}, chunk {t.Chunk}) ruined: its owners are gone.");
            if (lost != 0) Squads.Announce(gc, Squads.Tone(gc, 0, lost), $"{Squads.Short(Factions.LowestBit(lost))} racket ruined");
        }

        /// <summary>
        ///   A ruined racket gets a new, neutral owner (up to <c>Reopen</c> times a level): 30 s after it was ruined,
        ///   once no player is near its first post (or after 90 s regardless). The owner is the first owner's type if
        ///   vanilla lets a Mobster shake it down, else a Shopkeeper; the racket is free again.
        /// </summary>
        private static void Reopen(GameController gc)
        {
            int limit = -1;
            foreach (Turf t in turfs)
            {
                if (!t.Common || !t.Ruined || t.Posts.Count == 0) continue;
                if (limit < 0) limit = WarConfig.Turf(gc).Reopen;
                if (t.Reopens >= limit) continue;
                float waited = clock - t.RuinedAt;
                if (waited < ReopenSeconds) continue;
                if (waited < ReopenLatest && Squads.PlayerDistance(gc, t.Posts[0]) < ReopenOutOfSight) continue;
                NewOwner(gc, t);
            }
        }

        private static void NewOwner(GameController gc, Turf t)
        {
            t.Reopens++;
            string type = Array.IndexOf(Shakeable, t.OwnerType) >= 0 ? t.OwnerType : "Shopkeeper";
            Vector2 post = t.Posts[0];
            Vector2 pos = gc.tileInfo.FindLocationNearLocation(post, null, 0.32f, 1.28f, accountForObstacles: true, notInside: false);
            if (pos == Vector2.zero) pos = post;
            Agent a = null;
            try { a = gc.spawnerMain.SpawnAgent(new Vector3(pos.x, pos.y, 0f), null, type, "", null); }
            catch (Exception e) { Rck.Log.LogError($"Factions: a new {type} for a ruined racket failed to spawn: {e}"); }
            if (a == null)
            {
                t.RuinedAt = clock;
                return;
            }
            // Owns the place as the old owner did; its relationships are set a frame later (SpawnerMain.SetRelsLate).
            a.ownerID = t.Owner;
            a.startingChunk = t.Chunk;
            a.initialStartingChunk = t.Chunk;
            if (t.ChunkReal != null)
            {
                a.startingChunkReal = t.ChunkReal;
                a.hasStartingChunkReal = true;
            }
            a.startingSector = t.OwnerSector;
            if (!string.IsNullOrEmpty(t.PlaceKind)) a.startingChunkRealDescription = t.PlaceKind;
            a.startingPosition = post;
            a.startingAngle = t.Angles[0];
            a.SetDefaultGoal(string.IsNullOrEmpty(t.OwnerGoal) || t.OwnerGoal == "None" ? "Guard" : t.OwnerGoal);
            SpawnedAgents.Arm(a, null);
            t.Folk.Clear();
            t.Folk.Add(a);
            t.Ruined = false;
            t.Current = 0;
            t.Ties = 0;
            t.LastTaker = null;
            t.Holders.Clear();
            Log($"Factions: a new {type} took over a ruined racket (owner {t.Owner}, chunk {t.Chunk}); it's free ({t.Reopens} reopen(s)).");
            Squads.Announce(gc, "Buff", "A new owner took over a ruined racket");
        }

        private static void Fall(GameController gc, Turf t)
        {
            ulong lost = t.Current;
            int lostKey = Factions.LowestBit(lost);
            Agent taker = t.LastTaker;
            t.LastTaker = null;
            t.Holders.Clear();
            bool on = CaptureOn(gc);
            int key = on ? TakerKey(t, taker) : -1;
            if (key < 0)
            {
                t.Current = 0;
                Log($"Factions: {Factions.Describe(lost)} turf (owner {t.Owner}, chunk {t.Chunk}) fell to {AgentText.Describe(taker)}; nobody holds it now.");
                if (on) Squads.Announce(gc, Squads.Tone(gc, 0, lost), $"{Squads.Short(lostKey)} turf cleared");
                return;
            }
            ulong won = 1UL << key;
            t.Current = won;
            List<Agent> guards = Garrison(gc, t, key, lost, taker);
            t.Holders.AddRange(guards);
            Log($"Factions: {Factions.Describe(lost)} turf (owner {t.Owner}, chunk {t.Chunk}) taken by {Factions.Describe(won)} ({AgentText.Describe(taker)}); {guards.Count} guard(s) moved in.");
            Squads.Announce(gc, Squads.Tone(gc, won, lost), $"{Squads.Short(lostKey)} turf taken by {Squads.Plural(key)}");
            ControlPoints.Captured(t, key);
        }

        /// <summary>
        ///   The faction that takes the turf from <paramref name="taker"/>: the players' faction for a player or a
        ///   follower, else the taker's own (or its squad's), minus the turf's holders; -1 if none.
        /// </summary>
        private static int TakerKey(Turf t, Agent taker)
        {
            if (taker == null) return -1;
            Agent side = taker.isPlayer == 0 && taker.employer != null && taker.employer.isPlayer > 0 ? taker.employer : taker;
            ulong keys = Factions.KeysOf(side);
            if (keys == 0 && side.isPlayer == 0)
            {
                Squads.Squad s = Squads.Of(side);
                if (s != null) keys = 1UL << s.Key;
            }
            keys &= ~t.Current;
            if (keys == 0) return -1;
            int primary = Factions.PrimaryKey(side, keys);
            return primary >= 0 ? primary : Factions.LowestBit(keys);
        }

        private static List<Agent> Garrison(GameController gc, Turf t, int key, ulong lost, Agent taker)
        {
            ulong bit = 1UL << key;
            var guards = new List<Agent>();
            var squad = new Squads.Squad { Kind = Squads.Kind.Guard, Key = key, Foes = lost & ~bit };
            if (Free(taker, bit) && !SquadOrders.Elsewhere(taker, t)) guards.Add(taker);
            if (gc.agentList != null)
                foreach (Agent a in gc.agentList)
                {
                    if (guards.Count >= MaxGuards) break;
                    if (guards.Contains(a) || !Free(a, bit) || !NearPosts(t, a.tr.position) || SquadOrders.Elsewhere(a, t)) continue;
                    guards.Add(a);
                }
            // The commanded faction raises its men with control points (see Command), so it gets no free guards.
            if (guards.Count == 0 && t.Posts.Count > 0 && key != Command.Key)
            {
                Agent template = Squads.FindTemplate(gc, key, t.Posts[0]);
                Vector2? from = Squads.Origin(gc, key, t.Posts[0], lost & ~bit, out _);
                if (template != null && Squads.Spawn(gc, template, GuardCopies, squad, from) > 0) guards.AddRange(squad.Members);
            }
            for (int i = 0; i < guards.Count; i++)
            {
                Agent g = guards[i];
                // A respawned member or a commander's recruit stays in its own squad, so it keeps counting toward its cap (see FactionRespawn, Command).
                Squads.Squad own = Squads.Of(g);
                if (own == null || (own.Kind != Squads.Kind.Respawn && own.Kind != Squads.Kind.Command) || own.Key != key) Squads.Enlist(g, squad);
                int post = t.Posts.Count > 0 ? i % t.Posts.Count : -1;
                Squads.Guard(g, t.Owner, t.Chunk, t.ChunkReal, post >= 0 ? t.Posts[post] : (Vector2)g.tr.position, post >= 0 ? t.Angles[post] : g.startingAngle);
                // Its order, if it has one, is to hold this turf now, so it no longer counts as marching.
                SquadOrders.Garrisoned(g, t);
            }
            return guards;
        }

        /// <summary>An NPC member of <paramref name="bit"/> that owns nothing and may join a squad.</summary>
        private static bool Free(Agent a, ulong bit)
            => a != null && a.isPlayer == 0 && a.ownerID == 0 && Squads.Eligible(a, bit, squadOk: true);

        private static bool NearPosts(Turf t, Vector2 p)
        {
            foreach (Vector2 post in t.Posts)
                if (Vector2.Distance(post, p) <= GuardRadius) return true;
            return false;
        }

        /// <summary>A post of one of <paramref name="key"/>'s turfs that still has a living holder, for raids to aim at.</summary>
        internal static bool TargetOf(int key, out Vector2 target)
        {
            target = Vector2.zero;
            if (Server() == null || !built || key < 0) return false;
            var choices = new List<Vector2>();
            foreach (Turf t in turfs)
            {
                if (t.Common || (t.Current & (1UL << key)) == 0) continue;
                foreach (Agent h in t.Holders)
                    if (!Resolved(h)) choices.Add(h.startingPosition);
            }
            if (choices.Count == 0) return false;
            target = choices[UnityEngine.Random.Range(0, choices.Count)];
            return true;
        }

        // ---- For respawn (see FactionRespawn) ----

        /// <summary>Turf changes hands when it falls: <c>RCK_Turf_Capture</c>, or the commander console (see <see cref="Command"/>).</summary>
        internal static bool CaptureOn(GameController gc)
            => gc != null && gc.challenges != null && (gc.challenges.Contains(Mutator) || WarConfig.CommandWanted(gc));

        /// <summary>True once the level's turfs are listed (a second after load).</summary>
        internal static bool Built => Server() != null && built;

        /// <summary>The level's turfs once they're listed (a second after load), else none.</summary>
        internal static IReadOnlyList<Turf> Turfs => Server() != null && built ? turfs : none;

        /// <summary>The factions that held turf when the level loaded.</summary>
        internal static ulong StartKeys => Server() != null && built ? startKeys : 0;

        internal static bool IsResolved(Agent holder) => Resolved(holder);

        /// <summary>The turfs faction <paramref name="key"/> holds now (not rackets).</summary>
        internal static int HeldCount(int key)
        {
            if (key < 0) return 0;
            ulong bit = 1UL << key;
            int n = 0;
            foreach (Turf t in Turfs)
                if (!t.Common && (t.Current & bit) != 0) n++;
            return n;
        }

        /// <summary>The rackets faction <paramref name="key"/> runs now.</summary>
        internal static int RacketCount(int key)
        {
            if (key < 0) return 0;
            ulong bit = 1UL << key;
            int n = 0;
            foreach (Turf t in Turfs)
                if (t.Common && !t.Ruined && (t.Current & bit) != 0) n++;
            return n;
        }

        /// <summary>The rackets nobody runs and that can still be run.</summary>
        internal static int FreeRackets()
        {
            int n = 0;
            foreach (Turf t in Turfs)
                if (t.Common && !t.Ruined && t.Current == 0) n++;
            return n;
        }

        /// <summary>The free racket (nobody runs it, not ruined) whose commoners include <paramref name="folk"/>, else null.</summary>
        internal static Turf FreeRacketOf(Agent folk)
        {
            if (folk == null) return null;
            foreach (Turf t in Turfs)
                if (t.Common && !t.Ruined && t.Current == 0 && t.Folk.Contains(folk)) return t;
            return null;
        }

        /// <summary><paramref name="m"/> stops holding any turf (it marches off or is recalled).</summary>
        internal static void Leave(Agent m)
        {
            if (m == null) return;
            foreach (Turf t in turfs) t.Holders.Remove(m);
        }

        /// <summary>True if no living, unresolved holder stands within <paramref name="radius"/> of post <paramref name="post"/>.</summary>
        internal static bool PostEmpty(Turf t, int post, float radius)
        {
            Vector2 p = t.Posts[post];
            foreach (Agent h in t.Holders)
                if (!Resolved(h) && Vector2.Distance(h.startingPosition, p) <= radius) return false;
            return true;
        }

        /// <summary>True if the turf has no unresolved holder.</summary>
        internal static bool Undefended(Turf t)
        {
            foreach (Agent h in t.Holders)
                if (!Resolved(h)) return false;
            return true;
        }

        /// <summary><paramref name="agent"/> joins the turf's holders at post <paramref name="post"/> (see <see cref="Squads.Guard"/>).</summary>
        internal static void Assign(Turf t, Agent agent, int post)
        {
            if (!t.Holders.Contains(agent)) t.Holders.Add(agent);
            bool has = post >= 0 && post < t.Posts.Count;
            Squads.Guard(agent, t.Owner, t.Chunk, t.ChunkReal, has ? t.Posts[post] : (Vector2)agent.tr.position, has ? t.Angles[post] : agent.startingAngle);
        }

        /// <summary>
        ///   Faction <paramref name="key"/> walks into an empty or undefended turf: it holds it now, with
        ///   <paramref name="claimers"/> as its holders.
        /// </summary>
        internal static void Claim(GameController gc, Turf t, int key, List<Agent> claimers)
        {
            ulong bit = 1UL << key;
            ulong before = t.Current;
            int shownKey = Factions.LowestBit(before != 0 ? before : t.Original);
            t.Current = bit;
            t.LastTaker = null;
            t.Holders.Clear();
            for (int i = 0; i < claimers.Count; i++) Assign(t, claimers[i], t.Posts.Count > 0 ? i % t.Posts.Count : -1);
            if (t.Common)
            {
                Tie(gc, t, key);
                Log($"Factions: {Factions.Keys[key]} took over a racket (owner {t.Owner}, chunk {t.Chunk}, {t.Folk.Count} owner(s)) with {claimers.Count} member(s).");
                Squads.Announce(gc, Squads.Tone(gc, bit, 0), $"{Squads.Capital(Squads.Plural(key))} now run a protection racket");
                ControlPoints.Captured(t, key);
                return;
            }
            string was = before == 0 ? "empty" : Factions.Describe(before);
            Log($"Factions: {Factions.Keys[key]} claimed {was} turf (owner {t.Owner}, chunk {t.Chunk}, first held by {Factions.Describe(t.Original)}) with {claimers.Count} member(s).");
            string text = (t.Original & bit) != 0 && before == 0
                ? $"{Squads.Capital(Squads.Plural(key))} took back their turf"
                : $"{Squads.Short(shownKey)} turf taken by {Squads.Plural(key)}";
            Squads.Announce(gc, Squads.Tone(gc, bit, before != 0 ? before : t.Original & ~bit), text);
            ControlPoints.Captured(t, key);
        }

        // ---- Racket ties ----

        /// <summary>A racket's commoners and <paramref name="key"/>'s NPC members side with each other (Aligned, both ways).</summary>
        private static void Tie(GameController gc, Turf t, int key)
        {
            Untie(gc, t);
            t.Ties = 1UL << key;
            tied = true;
            Ties(gc, t, true);
        }

        private static void Untie(GameController gc, Turf t)
        {
            if (t.Ties == 0) return;
            Ties(gc, t, false);
            t.Ties = 0;
        }

        private static void Ties(GameController gc, Turf t, bool on)
        {
            if (gc.agentList == null) return;
            var members = new List<Agent>();
            foreach (Agent a in gc.agentList)
                if (a != null && a.isPlayer == 0 && !a.dead && !a.objectAgent && !t.Folk.Contains(a) && (Factions.KeysOf(a) & t.Ties) != 0) members.Add(a);
            Quietly(() =>
            {
                foreach (Agent f in t.Folk)
                {
                    if (f == null || f.dead) continue;
                    foreach (Agent m in members)
                    {
                        SetTie(f, m, on);
                        SetTie(m, f, on);
                    }
                }
            });
        }

        /// <summary>A pair was just decided by the rules: a racket's commoners keep siding with its racketeers.</summary>
        internal static void AfterPairSetup(Agent a, Agent b)
        {
            if (!tied) return;
            if (a.isPlayer != 0 || b.isPlayer != 0 || a.dead || b.dead) return;
            foreach (Turf t in turfs)
            {
                if (t.Ties == 0 || t.Ruined) continue;
                Agent folk = t.Folk.Contains(a) ? a : t.Folk.Contains(b) ? b : null;
                if (folk == null) continue;
                Agent other = folk == a ? b : a;
                if (t.Folk.Contains(other) || (Factions.KeysOf(other) & t.Ties) == 0) continue;
                Quietly(() =>
                {
                    SetTie(folk, other, true);
                    SetTie(other, folk, true);
                });
            }
        }

        private static void Quietly(Action act)
        {
            using (RelOps.Quietly(disguises: true))
            {
                try { act(); }
                catch (Exception e) { Rck.Log.LogError($"Factions: racket ties failed: {e}"); }
            }
        }

        private static void SetTie(Agent s, Agent t, bool on)
        {
            if (s == t || s.relationships == null || t.relationships == null || t.objectAgent) return;
            if (SocialRules.Has(s, "Relationless") || SocialRules.Has(t, "Relationless") || PartyPeace.ArePartyMates(s, t)) return;
            Relationship rel = RelOps.Of(s, t);
            if (rel == null) return;
            if (on)
            {
                // Loyal and Submissive ties already go further.
                if (rel.relType == "Aligned" || SocialRules.EscalationRank(rel.relTypeCode) == 0) return;
                Territorial.UnregisterDirected(s, t);
                rel.relHate = 0f;
                rel.relStrikes = 0;
                s.relationships.SetRel(t, "Aligned");
                return;
            }
            if (rel.relType != "Aligned") return;
            rel.relHate = 0f;
            rel.relStrikes = 0;
            s.relationships.SetRel(t, "Neutral");
        }

        /// <summary>
        ///   Level gate <c>TurfTaken=Blahd</c> (or <c>Blahd+Crepe</c>, all of them): the faction held turf when the level
        ///   loaded and holds none now.
        /// </summary>
        internal static bool TakenGate(string arg)
        {
            GameController gc = Server();
            if (gc == null) return false;
            if (!built) Build(gc, null);
            ulong held = 0;
            foreach (Turf t in turfs)
                if (!t.Common) held |= t.Current;
            bool any = false;
            foreach (string raw in arg.Split('+', ','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                int i = Factions.KeyIndex(name);
                if (i < 0) return false;
                ulong bit = 1UL << i;
                if ((startKeys & bit) == 0 || (held & bit) != 0) return false;
                any = true;
            }
            return any;
        }

        private static void Log(string line) => capped.Info(line);
    }
}
