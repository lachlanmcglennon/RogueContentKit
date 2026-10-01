#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   Control points (CP): each faction in the turf war earns them over time for the turf and rackets it holds, and
    ///   for every capture and enemy kill; the player's commanded faction (see <see cref="Command"/>) spends them on
    ///   recruits. They also weigh into each faction's share of the city's power on the war panel (see
    ///   <see cref="WarPanel"/>). On with a <c>[RCK]ControlPoints::</c> entry or the commander console. Server only,
    ///   and the points belong to one level load.
    /// </summary>
    internal static class ControlPoints
    {
        private const float StandingsSeconds = 1f;

        /// <summary>One faction's place in the war.</summary>
        internal sealed class Standing
        {
            public int Key, Turfs, Rackets, Living, Power;
            /// <summary>Its control points, or -1 with points off.</summary>
            public int Points;
        }

        private static bool started, on;
        private static int[] points = new int[0];
        private static ulong members;
        private static float nextPay;
        private static ControlPointSettings settings;
        private static readonly List<Standing> standings = new List<Standing>();
        private static float standingsAt = float.MinValue;

        static ControlPoints()
        {
            Reset();
            LevelScope.ResetAtBoth(Reset);
        }

        private static void Reset()
        {
            started = false;
            on = false;
            points = new int[Factions.Keys.Count];
            members = 0;
            nextPay = 0f;
            settings = null;
            standings.Clear();
            standingsAt = float.MinValue;
        }

        private static bool Check(GameController gc) => gc != null;

        /// <summary>True once this level's points are running.</summary>
        internal static bool On => on;

        internal static ControlPointSettings Settings => settings ?? new ControlPointSettings();

        /// <summary>Every second from <see cref="FactionWar"/>: pays income when it's due.</summary>
        internal static void Tick(GameController gc, float levelTime)
        {
            if (!Check(gc) || levelTime < 1f) return;
            if (!started) Start(gc, levelTime);
            members |= Held() | Command.Bit;
            if (!on || levelTime < nextPay) return;
            nextPay = levelTime + settings.Interval;
            Pay();
        }

        private static void Start(GameController gc, float levelTime)
        {
            started = true;
            members = TurfWar.StartKeys;
            if (!WarConfig.PointsOn(gc)) return;
            settings = WarConfig.Points(gc);
            on = true;
            for (int i = 0; i < points.Length; i++) points[i] = Math.Min(settings.Start, settings.Max);
            nextPay = levelTime + settings.Interval;
            Rck.Log.LogInfo($"Factions: control points on for {(members == 0 ? "no faction yet" : Factions.Describe(members))}: start {settings.Start}, {settings.Base} + {settings.PerTurf}/turf + {settings.PerCommon}/racket every {settings.Interval} s, +{settings.Capture} a capture, +{settings.Kill} a kill, at most {settings.Max}.");
        }

        private static ulong Held()
        {
            ulong held = 0;
            foreach (TurfWar.Turf t in TurfWar.Turfs)
                if (!t.Ruined) held |= t.Current;
            return held;
        }

        private static void Pay()
        {
            ulong routed = FactionEvents.Routed;
            int commanded = Command.Key;
            for (int key = 0; key < points.Length; key++)
            {
                ulong bit = 1UL << key;
                if ((members & bit) == 0 || (routed & bit) != 0) continue;
                int turfs = TurfWar.HeldCount(key), rackets = TurfWar.RacketCount(key);
                // A faction with nothing left earns nothing, except the player's, which keeps its base income.
                if (turfs + rackets == 0 && key != commanded) continue;
                Add(key, settings.Income(turfs, rackets));
            }
        }

        private static void Add(int key, int n)
        {
            if (!On || key < 0 || key >= points.Length || n == 0) return;
            points[key] = Mathf.Clamp(points[key] + n, 0, settings.Max);
        }

        /// <summary>Faction <paramref name="key"/>'s control points (0 with points off).</summary>
        internal static int Get(int key) => On && key >= 0 && key < points.Length ? points[key] : 0;

        /// <summary>What faction <paramref name="key"/> earns each <see cref="ControlPointSettings.Interval"/> from what it holds now.</summary>
        internal static int IncomeOf(int key) => On ? Settings.Income(TurfWar.HeldCount(key), TurfWar.RacketCount(key)) : 0;

        /// <summary>Takes <paramref name="n"/> points from <paramref name="key"/> if it has them.</summary>
        internal static bool Spend(int key, int n)
        {
            if (!On || key < 0 || key >= points.Length || n < 0 || points[key] < n) return false;
            points[key] -= n;
            return true;
        }

        internal static void Refund(int key, int n) => Add(key, Math.Max(0, n));

        /// <summary>Faction <paramref name="key"/> took <paramref name="t"/>: the capture bonus, half for a racket.</summary>
        internal static void Captured(TurfWar.Turf t, int key)
        {
            if (!On || t == null || key < 0) return;
            members |= 1UL << key;
            Add(key, t.Common ? settings.Capture / 2 : settings.Capture);
        }

        /// <summary><paramref name="killer"/>'s faction downed a member of another faction: the kill bonus.</summary>
        internal static void OnKill(Agent victim, Agent killer)
        {
            if (!On || victim == null || killer == null || victim == killer) return;
            ulong lost = Factions.KeysOf(victim);
            if (lost == 0 || (victim.employer != null && victim.employer.isPlayer > 0)) return;
            Agent side = killer.isPlayer == 0 && killer.employer != null && killer.employer.isPlayer > 0 ? killer.employer : killer;
            int commanded = side.isPlayer != 0 ? Command.Key : -1;
            if (commanded >= 0 && (lost & (1UL << commanded)) == 0)
            {
                Add(commanded, settings.Kill);
                return;
            }
            ulong keys = Factions.KeysOf(side);
            if (keys == 0 && side.isPlayer == 0)
            {
                Squads.Squad s = Squads.Of(side);
                if (s != null) keys = 1UL << s.Key;
            }
            keys &= members & ~lost;
            if (keys == 0) return;
            int key = Factions.PrimaryKey(side, keys);
            Add(key >= 0 ? key : Factions.LowestBit(keys), settings.Kill);
        }

        /// <summary>
        ///   The factions in the war, strongest first: turf, rackets, living members, points and power, the share of
        ///   10 per turf + 4 per racket + 1 per living member + 1 per 5 points (in percent). Refreshed once a second.
        /// </summary>
        internal static List<Standing> Standings(GameController gc)
        {
            if (!Check(gc)) return standings;
            float now = Time.unscaledTime;
            if (now - standingsAt < StandingsSeconds && now >= standingsAt) return standings;
            standingsAt = now;
            standings.Clear();
            ulong show = members | TurfWar.StartKeys | Held() | Command.Bit;
            if (show == 0) return standings;
            int[] living = Factions.CountLiving(gc, show);
            int total = 0;
            for (int key = 0; key < Factions.Keys.Count; key++)
            {
                if ((show & (1UL << key)) == 0) continue;
                var s = new Standing
                {
                    Key = key,
                    Turfs = TurfWar.HeldCount(key),
                    Rackets = TurfWar.RacketCount(key),
                    Living = key < living.Length ? living[key] : 0,
                    Points = On ? Get(key) : -1,
                };
                s.Power = 10 * s.Turfs + 4 * s.Rackets + s.Living + Math.Max(0, s.Points) / 5;
                total += s.Power;
                standings.Add(s);
            }
            foreach (Standing s in standings)
                s.Power = total > 0 ? Mathf.RoundToInt(100f * s.Power / total) : 0;
            standings.Sort((a, b) => b.Power != a.Power ? b.Power.CompareTo(a.Power) : a.Key.CompareTo(b.Key));
            return standings;
        }
    }
}
