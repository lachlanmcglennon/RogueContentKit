#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Faction raids: a squad (see <see cref="Squads"/>) of one faction marches on a rival's turf (see
    ///   <see cref="TurfWar"/>) or members, fights, searches and goes home. Raids come from the
    ///   <c>[RCK]FactionRaid::</c> schedule (see <see cref="RaidRules"/>) and, with the <c>RCK_Faction_Raids</c>
    ///   mutator, on their own between factions that hate each other. Server only.
    /// </summary>
    internal static class FactionRaids
    {
        private const float RetrySeconds = 30f;
        private const int MaxLogsPerLevel = 20;

        private sealed class Pending
        {
            public RaidEntry Entry;
            public bool Done;
        }

        private static readonly List<Pending> scheduled = new List<Pending>();
        private static int stamp = int.MinValue;
        private static int autoCount;
        private static float nextAuto;
        private static readonly CappedLog capped = new CappedLog(MaxLogsPerLevel);

        public static void Initialize()
        {
            LevelMutators.Require(RaidRules.Mutator, "Factions: raid mutator");
        }

        private static void Reset(GameController gc)
        {
            scheduled.Clear();
            autoCount = 0;
            nextAuto = UnityEngine.Random.Range(RaidRules.FirstMin, RaidRules.FirstMax + 1);
            capped.Reset();
            List<string> bodies = LevelMutators.Bodies(gc, RaidRules.Prefix);
            if (bodies.Count == 0) return;
            foreach (RaidEntry e in RaidRules.Parse(bodies, Factions.KeyIndex,
                         (text, error) => Rck.Log.LogWarning($"Factions: ignored {RaidRules.Prefix} entry \"{text}\": {error}.")))
                scheduled.Add(new Pending { Entry = e });
            Log($"Factions: {scheduled.Count} raid(s) scheduled this level.");
        }

        /// <summary>Once a second from <see cref="FactionWar"/>.</summary>
        internal static void Tick(GameController gc, float levelTime)
        {
            if (LevelScope.IsNew(ref stamp)) Reset(gc);
            foreach (Pending p in scheduled)
            {
                if (p.Done || levelTime < p.Entry.At) continue;
                p.Done = true;
                Launch(gc, p.Entry.Raider, p.Entry.Target, p.Entry.Size, "scheduled");
            }
            if (autoCount >= RaidRules.MaxAuto || levelTime < nextAuto) return;
            if (gc.challenges == null || !gc.challenges.Contains(RaidRules.Mutator)) return;
            if (PickPair(gc, out int a, out int b) && Launch(gc, a, b, RaidRules.DefaultSize, "auto"))
            {
                autoCount++;
                nextAuto = levelTime + UnityEngine.Random.Range(RaidRules.GapMin, RaidRules.GapMax + 1);
            }
            else nextAuto = levelTime + RetrySeconds;
        }

        private static bool Launch(GameController gc, int raider, int target, int size, string why)
        {
            string pair = $"{Factions.Keys[raider]}>{Factions.Keys[target]}";
            ulong routed = FactionEvents.Routed;
            if (raider == Command.Key)
            {
                Log($"Factions: {why} raid {pair} skipped: the player commands {Factions.Keys[raider]}.");
                return false;
            }
            if ((routed & ((1UL << raider) | (1UL << target))) != 0)
            {
                Log($"Factions: {why} raid {pair} skipped: a side is routed.");
                return false;
            }
            if (LevelRelations.Peace(raider, target))
            {
                Log($"Factions: {why} raid {pair} skipped: they have a truce.");
                return false;
            }
            if (!TurfWar.TargetOf(target, out Vector2 spot) && !RandomMember(gc, target, out spot))
            {
                Log($"Factions: {why} raid {pair} skipped: no one to hit.");
                return false;
            }
            Agent template = Squads.FindTemplate(gc, raider, spot);
            if (template == null)
            {
                Log($"Factions: {why} raid {pair} skipped: no one to send.");
                return false;
            }
            var squad = new Squads.Squad { Kind = Squads.Kind.Raid, Key = raider, Foes = 1UL << target };
            Vector2? from = Squads.Origin(gc, raider, spot, 1UL << target, out TurfWar.Turf home);
            int made = Squads.Spawn(gc, template, size, squad, from);
            if (made == 0)
            {
                Log($"Factions: {why} raid {pair} skipped: too many squads out, or no room to spawn.");
                return false;
            }
            Squads.Send(squad, spot);
            string origin = home != null ? $"its turf (owner {home.Owner}, chunk {home.Chunk})" : $"near {AgentText.Describe(template)}";
            Log($"Factions: {why} raid {pair}: {Squads.Roster(squad)} from {origin} to {spot}.");
            Squads.Announce(gc, Squads.Tone(gc, 1UL << raider, 1UL << target),
                $"{Squads.Capital(Squads.Plural(raider))} are raiding {Squads.Plural(target)}!");
            return true;
        }

        private static bool RandomMember(GameController gc, int key, out Vector2 spot)
        {
            spot = Vector2.zero;
            if (gc.agentList == null) return false;
            ulong bit = 1UL << key;
            var choices = new List<Agent>();
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost || a.employer != null) continue;
                if (Factions.MemberKeys(a, bit) != 0) choices.Add(a);
            }
            if (choices.Count == 0) return false;
            spot = choices[UnityEngine.Random.Range(0, choices.Count)].tr.position;
            return true;
        }

        /// <summary>A random pair of factions present, neither routed nor at peace, where A is Hateful or Territorial toward B.</summary>
        private static bool PickPair(GameController gc, out int raider, out int target)
        {
            raider = target = -1;
            int n = Factions.Keys.Count;
            ulong[] hostileBy = HostileBy(gc, out ulong present);
            if (hostileBy == null) return false;
            present &= ~FactionEvents.Routed;
            FactionMatrix matrix = FactionMatrix.Current();
            var pairs = new List<KeyValuePair<int, int>>();
            int commanded = Command.Key;
            for (int i = 0; i < n; i++)
            {
                if ((present & (1UL << i)) == 0 || i == commanded) continue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || (present & (1UL << j)) == 0) continue;
                    if (Hates(i, j, hostileBy, matrix) && !LevelRelations.Peace(i, j)) pairs.Add(new KeyValuePair<int, int>(i, j));
                }
            }
            if (pairs.Count == 0) return false;
            KeyValuePair<int, int> pick = pairs[UnityEngine.Random.Range(0, pairs.Count)];
            raider = pick.Key;
            target = pick.Value;
            return true;
        }

        /// <summary>
        ///   Per faction, the factions its living, free NPC members' traits make them Hostile or Territorial toward;
        ///   <paramref name="present"/> is the factions with such members. Null without factions.
        /// </summary>
        internal static ulong[] HostileBy(GameController gc, out ulong present)
        {
            present = 0;
            int n = Factions.Keys.Count;
            if (n == 0 || gc.agentList == null) return null;
            var hostileBy = new ulong[n];
            foreach (Agent a in gc.agentList)
            {
                if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost || a.employer != null) continue;
                ulong keys = Factions.KeysOf(a);
                if (keys == 0) continue;
                present |= keys;
                Factions.GradesOf(a, out ulong hostile, out ulong territorial, out _);
                ulong grudge = hostile | territorial;
                if (grudge == 0) continue;
                for (int i = 0; i < n; i++)
                    if ((keys & (1UL << i)) != 0) hostileBy[i] |= grudge;
            }
            return hostileBy;
        }

        /// <summary>
        ///   True if faction <paramref name="i"/> hates <paramref name="j"/>: by its members' traits, a matrix rule of
        ///   Territorial or stronger, or a war declared in play (see <see cref="LevelRelations.War"/>).
        /// </summary>
        internal static bool Hates(int i, int j, ulong[] hostileBy, FactionMatrix matrix)
        {
            if ((hostileBy[i] & (1UL << j)) != 0 || LevelRelations.War(i, j)) return true;
            return matrix != null && matrix.Resolve(1UL << i, false, 1UL << j, false) >= FactionGrade.Territorial;
        }

        /// <summary>The factions <paramref name="key"/> hates (see <see cref="Hates"/>), without itself, routed factions and truces.</summary>
        internal static ulong FoesOf(int key, ulong[] hostileBy, FactionMatrix matrix)
        {
            if (hostileBy == null || key < 0 || key >= hostileBy.Length) return 0;
            ulong foes = 0, routed = FactionEvents.Routed;
            for (int j = 0; j < hostileBy.Length; j++)
            {
                ulong bit = 1UL << j;
                if (j == key || (routed & bit) != 0) continue;
                if (Hates(key, j, hostileBy, matrix) && !LevelRelations.Peace(key, j)) foes |= bit;
            }
            return foes;
        }

        private static void Log(string line) => capped.Info(line);
    }
}
