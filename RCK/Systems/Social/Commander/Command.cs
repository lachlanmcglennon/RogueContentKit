#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The commander console's orders: the host leads one faction in the turf war. That faction's automatic respawn,
    ///   raids and backup stop; instead the player spends its control points (see <see cref="ControlPoints"/>) on squads
    ///   of recruits, any faction's units sworn to it (see <see cref="Factions.Swear"/>), and sends them to capture,
    ///   hold or leave turf, or declares war on a faction. The other factions keep fighting on their own. On with the
    ///   <c>RCK_Commander</c> mutator or a <c>[RCK]Command::</c> entry (see <see cref="CommandRules"/>); the console
    ///   itself is <see cref="WarPanel"/>. Server only, and the squads belong to one level load.
    /// </summary>
    internal static class Command
    {
        private const float ConfirmSeconds = 4f, StatusSeconds = 6f;

        /// <summary>One unit the console offers and what it costs.</summary>
        internal sealed class Offer
        {
            public SquadUnits.Unit Unit;
            public string Name;
            public int Cost;
        }

        /// <summary>One of the player's squads, numbered in the order they were raised.</summary>
        internal sealed class Team
        {
            public int Number;
            public Squads.Squad Squad;
            public string Label => "Squad " + Number;
        }

        private static bool started;
        private static int key = -1;
        private static CommandSettings settings;
        private static readonly List<Offer> roster = new List<Offer>();
        private static readonly List<Team> teams = new List<Team>();
        private static readonly Dictionary<Agent, int> paid = new Dictionary<Agent, int>();
        private static int nextNumber = 1;
        private static float levelTime;
        private static string status = "";
        private static bool statusOk;
        private static float statusAt = float.MinValue;
        private static int confirmKey = -1;
        private static float confirmAt = float.MinValue;

        static Command() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            started = false;
            key = -1;
            settings = null;
            roster.Clear();
            teams.Clear();
            paid.Clear();
            nextNumber = 1;
            levelTime = 0f;
            status = "";
            statusAt = float.MinValue;
            confirmKey = -1;
            confirmAt = float.MinValue;
        }

        private static bool Check() => FactionEvents.Server() != null;

        /// <summary>The faction the player commands, or -1. Decided once the level's turfs are listed.</summary>
        internal static int Key
        {
            get
            {
                if (!Check()) return -1;
                if (!started && TurfWar.Built) Start(GameController.gameController);
                return key;
            }
        }

        internal static ulong Bit
        {
            get
            {
                int k = Key;
                return k >= 0 ? 1UL << k : 0;
            }
        }

        /// <summary>Recruits alive at once (it doesn't count them: <see cref="Squads.Room"/> calls this).</summary>
        internal static int Cap => settings != null ? settings.Cap : CommandRules.MaxUnits;

        internal static CommandSettings Settings => settings ?? new CommandSettings();

        internal static IReadOnlyList<Offer> Roster => Key >= 0 ? roster : (IReadOnlyList<Offer>)new Offer[0];

        internal static IReadOnlyList<Team> Teams => Key >= 0 ? teams : (IReadOnlyList<Team>)new Team[0];

        /// <summary>The squad raised last, or null.</summary>
        internal static Team Newest => Key >= 0 && teams.Count > 0 ? teams[teams.Count - 1] : null;

        /// <summary>The last order's outcome, for a few seconds.</summary>
        internal static string Status => Time.unscaledTime - statusAt < StatusSeconds ? status : "";

        /// <summary>True if the last order went through (the console shows refusals in red).</summary>
        internal static bool StatusOk => statusOk;

        /// <summary>Every second from <see cref="FactionWar"/>: drops wiped-out squads.</summary>
        internal static void Tick(GameController gc, float time)
        {
            if (!Check()) return;
            levelTime = time;
            if (Key < 0) return;
            for (int i = teams.Count - 1; i >= 0; i--)
            {
                Team t = teams[i];
                List<Agent> members = t.Squad.Members;
                for (int j = members.Count - 1; j >= 0; j--)
                {
                    Agent m = members[j];
                    if (m == null || m.dead || m.disappeared)
                    {
                        members.RemoveAt(j);
                        continue;
                    }
                    if (!SquadOrders.Lost(m)) continue;
                    // Hired by a player, or raised as a zombie or ghost: it's no recruit of the commander's any more.
                    members.RemoveAt(j);
                    Squads.Forget(m);
                    SquadOrders.Forget(m);
                    TurfWar.Leave(m);
                    paid.Remove(m);
                    Rck.Log.LogInfo($"Factions: commander's {t.Label} lost {AgentText.Describe(m)} ({(m.employer != null && m.employer.isPlayer > 0 ? "now a player's follower" : "raised")}).");
                }
                if (members.Count > 0) continue;
                teams.RemoveAt(i);
                Rck.Log.LogInfo($"Factions: commander's {t.Label} is gone.");
                Squads.Announce(gc, "Debuff", $"{t.Label} wiped out");
            }
            if (paid.Count > 0)
            {
                List<Agent> gone = null;
                foreach (Agent a in paid.Keys)
                    if (a == null || a.dead || a.disappeared) (gone = gone ?? new List<Agent>()).Add(a);
                if (gone != null) foreach (Agent a in gone) paid.Remove(a);
            }
        }

        private static void Start(GameController gc)
        {
            started = true;
            if (gc == null || !WarConfig.CommandWanted(gc)) return;
            settings = WarConfig.Command(gc);
            ulong common = Factions.CommonFolkBit;
            int k = settings.Faction;
            string how = "the [RCK]Command:: entry";
            if (k < 0 && gc.playerAgent != null)
            {
                k = Factions.PrimaryKey(gc.playerAgent, TurfWar.StartKeys & ~common);
                how = "the host's faction that holds turf";
                if (k < 0)
                {
                    k = Factions.PrimaryKey(gc.playerAgent, Factions.All & ~common);
                    how = "the host's faction";
                }
            }
            if (k < 0 || k >= Factions.Keys.Count)
            {
                Rck.Log.LogWarning($"Factions: {CommandRules.Mutator} is on, but there's no faction to command: name one with {CommandRules.Prefix}Faction=<key>; or give the player a faction trait.");
                return;
            }
            key = k;
            SquadUnits.Prepare(gc);
            BuildRoster(gc);
            if (gc.playerAgent != null && Factions.MemberKeys(gc.playerAgent, 1UL << key) == 0)
                Rck.Log.LogWarning($"Factions: the host commands {Factions.Keys[key]} but isn't a member; its recruits may not treat the player as one of their own.");
            var names = new List<string>();
            foreach (Offer o in roster) names.Add($"{o.Name} ({o.Cost} CP)");
            Rck.Log.LogInfo($"Factions: the player commands {Factions.Keys[key]} (from {how}): its respawn, raids and backup are off; recruits {(names.Count == 0 ? "none" : string.Join(", ", names.ToArray()))}; squads of {settings.Size}, {settings.Cap} alive at most, {settings.Refund}% back on disband.");
            KeyCode console = Rck.Config != null ? Rck.Config.CommandConsoleKey.Value : KeyCode.None;
            Squads.Announce(gc, "BuffSpecial", $"You command {Squads.Plural(key)}{(console != KeyCode.None ? $" ({HotKeys.Name(console)})" : "")}");
        }

        private static void BuildRoster(GameController gc)
        {
            roster.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (settings.Units.Count > 0)
            {
                foreach (CommandUnit u in settings.Units)
                {
                    SquadUnits.Unit unit = SquadUnits.Resolve(gc, u.Name, out string why);
                    if (unit == null)
                    {
                        Rck.Log.LogWarning($"Factions: {CommandRules.Prefix} unit \"{u.Name}\" left out: {why}.");
                        continue;
                    }
                    if (seen.Add(unit.Name)) roster.Add(new Offer { Unit = unit, Name = unit.Name, Cost = settings.CostOf(u) });
                }
                if (roster.Count > 0) return;
                Rck.Log.LogWarning($"Factions: none of the {CommandRules.Prefix} units resolve; the console offers {Factions.Keys[key]}'s own units.");
            }
            IList<SquadUnits.Unit> own = SquadUnits.ForRespawn(gc, key);
            if (own != null)
                foreach (SquadUnits.Unit unit in own)
                    if (seen.Add(unit.Name)) roster.Add(new Offer { Unit = unit, Name = unit.Name, Cost = settings.Cost });
            if (roster.Count > 0) return;
            Agent template = Squads.FindTemplate(gc, key, null);
            if (template == null) return;
            var copy = new SquadUnits.Unit
            {
                Type = template.agentName,
                Data = template.agentName == SquadUnits.Custom ? template.customCharacterData : null,
                Name = SquadUnits.NameOf(template),
                Loadout = SpawnedAgents.LoadoutOf(template),
            };
            roster.Add(new Offer { Unit = copy, Name = copy.Name, Cost = settings.Cost });
        }

        // ---- Orders (from the console, on the main thread) ----

        private static bool Report(string text, bool ok)
        {
            status = text;
            statusOk = ok;
            statusAt = Time.unscaledTime;
            Rck.Log.LogInfo($"Factions: commander: {text}");
            return ok;
        }

        private static ulong Foes(GameController gc)
            => FactionRaids.FoesOf(key, FactionRaids.HostileBy(gc, out _), FactionMatrix.Current());

        /// <summary>Raises a squad of <paramref name="offer"/>: as many as the cap, the squad size and the points allow.</summary>
        internal static bool Recruit(Offer offer)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0 || offer == null) return false;
            if ((FactionEvents.Routed & Bit) != 0) return Report($"{Squads.Capital(Squads.Plural(key))} are routed.", false);
            int room = Squads.Room(Squads.Kind.Command);
            if (room <= 0) return Report($"At the cap: {Cap} recruits alive.", false);
            int n = Math.Min(Settings.Size, room);
            if (offer.Cost > 0) n = Math.Min(n, ControlPoints.Get(key) / offer.Cost);
            if (n <= 0) return Report($"Not enough CP: {offer.Name} costs {offer.Cost}.", false);
            if (!ControlPoints.Spend(key, offer.Cost * n)) return Report("Not enough CP.", false);

            ulong foes = Foes(gc);
            Vector2 at;
            Agent sight = null;
            string where;
            Agent me = gc.playerAgent != null && !gc.playerAgent.dead ? gc.playerAgent : null;
            // The faction's turf nearest the commander, even in sight of the players.
            if (FactionRespawn.SpawnPoint(gc, key, foes, out TurfWar.Turf home, out int post, playerClear: 0f, near: me != null ? (Vector2)me.tr.position : (Vector2?)null))
            {
                at = home.Posts[post];
                foreach (Agent h in home.Holders)
                    if (!TurfWar.IsResolved(h)) { sight = h; break; }
                where = Place(gc, home);
            }
            else if (me != null)
            {
                at = me.tr.position;
                sight = me;
                where = "you";
            }
            else
            {
                ControlPoints.Refund(key, offer.Cost * n);
                return Report("Nowhere safe to raise a squad.", false);
            }

            var squad = new Squads.Squad { Kind = Squads.Kind.Command, Key = key, Foes = foes };
            int made = Squads.SpawnUnits(gc, new[] { offer.Unit }, at, sight, n, squad);
            if (made < n) ControlPoints.Refund(key, offer.Cost * (n - made));
            if (made == 0) return Report($"No room to raise {offer.Name} near {where}.", false);
            var team = new Team { Number = nextNumber++, Squad = squad };
            teams.Add(team);
            foreach (Agent m in squad.Members)
            {
                if (m == null) continue;
                paid[m] = offer.Cost;
                SquadOrders.HoldNearest(m, key, true);
            }
            return Report($"{team.Label}: {made} x {offer.Name} raised at {where} for {offer.Cost * made} CP.", true);
        }

        /// <summary>The squads an order goes to: <paramref name="team"/>, or every squad when it's null.</summary>
        private static List<Team> Targets(Team team)
        {
            var list = new List<Team>();
            if (team != null) { if (teams.Contains(team)) list.Add(team); }
            else list.AddRange(teams);
            return list;
        }

        internal static IEnumerable<Agent> Living(Team team)
        {
            if (team == null) yield break;
            foreach (Agent m in team.Squad.Members)
                if (!SquadOrders.Lost(m)) yield return m;
        }

        private static string Who(Team team) => team != null ? team.Label : "All squads";

        /// <summary>Sends <paramref name="team"/> (null: every squad) to take <paramref name="t"/>; to one of the faction's own turfs it's a defend order.</summary>
        internal static bool Capture(Team team, TurfWar.Turf t)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0 || t == null) return false;
            ulong bit = 1UL << key;
            if ((t.Current & bit) != 0) return Defend(team, t);
            List<Team> list = Targets(team);
            if (list.Count == 0) return Report("No squads to send.", false);
            if (t.Posts.Count == 0) return Report("That turf has nowhere to stand.", false);
            bool rackets = WarConfig.RacketsOn(gc);
            if (t.Common && t.Ruined) return Report("That racket is ruined: its owners are gone.", false);
            if (t.Common && !rackets) return Report("Rackets are off in this level.", false);
            ulong foes = Foes(gc);
            if (!SquadOrders.Attackable(t, key, foes, rackets))
            {
                ulong friends = t.Current & ~foes;
                return Report($"Declare war on {Squads.Plural(Factions.LowestBit(friends))} first.", false);
            }
            int sent = 0;
            foreach (Team tm in list)
            {
                tm.Squad.Foes |= foes;
                foreach (Agent m in Living(tm))
                {
                    SquadOrders.Attack(m, key, t, levelTime, true);
                    sent++;
                }
            }
            return Report($"{Who(team)}: {sent} marching on {Place(gc, t)}.", sent > 0);
        }

        /// <summary>What an order to a turf would do.</summary>
        internal enum Aim { None, Attack, Take, Defend }

        /// <summary>
        ///   What sending a squad to <paramref name="t"/> would do, by the same rules as <see cref="Capture"/> and
        ///   <see cref="Defend"/>, with <paramref name="text"/> for the map's hover tip (<c>Click: attack the Blahds</c>,
        ///   or why it can't).
        /// </summary>
        internal static Aim Intent(GameController gc, TurfWar.Turf t, out string text)
        {
            text = "";
            if (gc == null || Key < 0 || t == null) return Aim.None;
            if (t.Posts.Count == 0)
            {
                text = "Nowhere to stand on it.";
                return Aim.None;
            }
            if ((t.Current & (1UL << key)) != 0)
            {
                text = "Click: defend it";
                return Aim.Defend;
            }
            bool rackets = WarConfig.RacketsOn(gc);
            if (t.Common && t.Ruined)
            {
                text = "Ruined: its owners are gone.";
                return Aim.None;
            }
            if (t.Common && !rackets)
            {
                text = "Rackets are off in this level.";
                return Aim.None;
            }
            ulong foes = Foes(gc);
            if (SquadOrders.Attackable(t, key, foes, rackets))
            {
                if (t.Current == 0)
                {
                    text = t.Common ? "Click: take over the racket" : "Click: take it";
                    return Aim.Take;
                }
                text = $"Click: attack {Squads.Plural(Factions.LowestBit(t.Current & foes))}";
                return Aim.Attack;
            }
            text = $"At peace with {Squads.Plural(Factions.LowestBit(t.Current & ~foes))}: declare war first";
            return Aim.None;
        }

        /// <summary><paramref name="team"/> (null: every squad) holds the faction's turf <paramref name="t"/>, empty posts first.</summary>
        internal static bool Defend(Team team, TurfWar.Turf t)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0 || t == null) return false;
            if ((t.Current & (1UL << key)) == 0) return Report("That isn't your turf.", false);
            if (t.Posts.Count == 0) return Report("That turf has nowhere to stand.", false);
            List<Team> list = Targets(team);
            if (list.Count == 0) return Report("No squads to send.", false);
            int sent = 0;
            foreach (Team tm in list)
                foreach (Agent m in Living(tm))
                {
                    int post = -1;
                    for (int i = 0; i < t.Posts.Count && post < 0; i++)
                        if (TurfWar.PostEmpty(t, i, SquadOrders.PostRadius)) post = i;
                    if (post < 0) post = UnityEngine.Random.Range(0, t.Posts.Count);
                    SquadOrders.Hold(m, key, t, post, true);
                    sent++;
                }
            return Report($"{Who(team)}: {sent} holding {Place(gc, t)}.", sent > 0);
        }

        /// <summary><paramref name="team"/> (null: every squad) walks to the player and waits there.</summary>
        internal static bool Recall(Team team)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0) return false;
            Agent p = gc.playerAgent;
            if (p == null || p.dead) return Report("You're down: nobody to rally to.", false);
            List<Team> list = Targets(team);
            if (list.Count == 0) return Report("No squads to recall.", false);
            int sent = 0;
            foreach (Team tm in list)
                foreach (Agent m in Living(tm))
                {
                    SquadOrders.MoveTo(m, key, p.tr.position);
                    sent++;
                }
            return Report($"{Who(team)}: {sent} on their way to you.", sent > 0);
        }

        /// <summary><paramref name="team"/> (null: every squad) walks to <paramref name="to"/> and waits there, holding nothing.</summary>
        internal static bool Move(Team team, Vector2 to)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0) return false;
            List<Team> list = Targets(team);
            if (list.Count == 0) return Report("No squads to send.", false);
            int sent = 0;
            foreach (Team tm in list)
                foreach (Agent m in Living(tm))
                {
                    SquadOrders.MoveTo(m, key, to);
                    sent++;
                }
            Agent p = gc.playerAgent;
            string where = p != null ? Bearing(p.tr.position, to) : "there";
            return Report($"{Who(team)}: {sent} moving to {(where == "here" ? "you" : where)}.", sent > 0);
        }

        /// <summary>Shows <paramref name="text"/> as the console's status line (a refused order the console itself caught).</summary>
        internal static void Note(string text) => Report(text, false);

        /// <summary>Shows <paramref name="text"/> as the console's status line, not as a refusal (a prompt such as "click again").</summary>
        internal static void Hint(string text) => Report(text, true);

        /// <summary>The control points disbanding <paramref name="team"/> (null: every squad) would give back.</summary>
        internal static int RefundOf(Team team)
        {
            int back = 0;
            foreach (Team tm in Targets(team))
                foreach (Agent m in Living(tm))
                    if (paid.TryGetValue(m, out int cost)) back += cost * Settings.Refund / 100;
            return back;
        }

        /// <summary>Sends <paramref name="team"/> (null: every squad) home for part of its cost back.</summary>
        internal static bool Disband(Team team)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0) return false;
            List<Team> list = Targets(team);
            if (list.Count == 0) return Report("No squads to disband.", false);
            int gone = 0, back = 0;
            foreach (Team tm in list)
            {
                foreach (Agent m in new List<Agent>(Living(tm)))
                {
                    if (paid.TryGetValue(m, out int cost)) back += cost * Settings.Refund / 100;
                    paid.Remove(m);
                    SquadOrders.Forget(m);
                    TurfWar.Leave(m);
                    Squads.Forget(m);
                    try
                    {
                        m.statusEffects.Vanish();
                        m.objectMult.Disappear();
                    }
                    catch (Exception e) { SocialRules.LogOnce(m, "command-disband", e); }
                    Factions.Unswear(m);
                    gone++;
                }
                teams.Remove(tm);
            }
            ControlPoints.Refund(key, back);
            return Report($"{Who(team)} disbanded: {gone} sent home, {back} CP back.", true);
        }

        /// <summary>The factions the player could declare war on: in the war, not the commanded one, not foes yet.</summary>
        internal static List<int> Neutrals(GameController gc)
        {
            var list = new List<int>();
            if (gc == null || Key < 0) return list;
            ulong foes = Foes(gc), routed = FactionEvents.Routed, common = Factions.CommonFolkBit;
            foreach (ControlPoints.Standing s in ControlPoints.Standings(gc))
            {
                ulong bit = 1UL << s.Key;
                if (s.Key == key || (foes & bit) != 0 || (routed & bit) != 0 || (common & bit) != 0) continue;
                list.Add(s.Key);
            }
            return list;
        }

        /// <summary>True while a war declaration on <paramref name="other"/> waits for its confirming second click.</summary>
        internal static bool Confirming(int other) => confirmKey == other && Time.unscaledTime - confirmAt < ConfirmSeconds;

        /// <summary>Declares war on <paramref name="other"/>: the first click asks, a second within 4 s confirms.</summary>
        internal static bool DeclareWar(int other)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || Key < 0 || other < 0 || other == key || other >= Factions.Keys.Count) return false;
            if (!Confirming(other))
            {
                confirmKey = other;
                confirmAt = Time.unscaledTime;
                Hint($"Click again to declare war on {Squads.Plural(other)}.");
                return false;
            }
            confirmKey = -1;
            LevelRelations.Set(key, other, "Hateful", true);
            ulong bit = 1UL << other;
            foreach (Team tm in teams) tm.Squad.Foes |= bit;
            Squads.Announce(gc, "Debuff", $"War on {Squads.Plural(other)}!");
            return Report($"War declared on {Squads.Plural(other)}.", true);
        }

        // ---- Text for the console ----

        /// <summary>What <paramref name="team"/> is doing, by its first living member's order.</summary>
        internal static string Doing(GameController gc, Team team)
        {
            foreach (Agent m in Living(team))
            {
                SquadOrders.Order o = SquadOrders.Of(m);
                if (o == null) return "waiting";
                if (o.Target != null) return "marching on " + Place(gc, o.Target);
                if (o.Holding != null) return "holding " + Place(gc, o.Holding);
                if (o.HasGoal && Vector2.Distance(m.tr.position, o.Goal) > 1.5f)
                {
                    Agent p = gc != null ? gc.playerAgent : null;
                    return p != null ? "moving, " + Bearing(p.tr.position, o.Goal) : "on the move";
                }
                return "waiting";
            }
            return "gone";
        }

        internal static string Roll(Team team)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Agent m in Living(team))
            {
                string n = SquadUnits.NameOf(m);
                if (!counts.ContainsKey(n)) { counts[n] = 0; order.Add(n); }
                counts[n]++;
            }
            var parts = new List<string>();
            foreach (string n in order) parts.Add(counts[n] > 1 ? $"{counts[n]} {n}" : n);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary><c>Blahd turf 3, 24 tiles NE</c>: who holds <paramref name="t"/>, its number and where it lies from the player.</summary>
        internal static string Place(GameController gc, TurfWar.Turf t)
        {
            int number = 0;
            IReadOnlyList<TurfWar.Turf> turfs = TurfWar.Turfs;
            for (int i = 0; i < turfs.Count; i++)
                if (turfs[i] == t) { number = i + 1; break; }
            string who;
            if (t.Common) who = t.Ruined ? "ruined racket" : t.Current == 0 ? "free racket" : $"{Squads.Short(Factions.LowestBit(t.Current))} racket";
            else who = t.Current == 0 ? "empty turf" : $"{Squads.Short(Factions.LowestBit(t.Current))} turf";
            string text = $"{who} {number}";
            Agent p = gc != null ? gc.playerAgent : null;
            if (p == null || t.Posts.Count == 0) return text;
            Vector2 from = p.tr.position;
            Vector2 to = SquadOrders.NearestPost(t, from);
            return $"{text}, {Bearing(from, to)}";
        }

        private static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary><c>24 tiles NE</c>: a tile is 0.64 world units, and north is up the map.</summary>
        internal static string Bearing(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            int tiles = Mathf.RoundToInt(d.magnitude / 0.64f);
            if (tiles <= 1) return "here";
            float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return $"{tiles} tiles {Compass[Mathf.RoundToInt(angle / 45f) % 8]}";
        }
    }
}
