#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The <c>&lt;key&gt;_Calls_Backup</c> role: when an outsider really attacks a holder, or the holder (or an NPC of its
    ///   owner group) presses an Alarm Button against a criminal, the faction sends a squad of 3 (see
    ///   <see cref="Squads"/>) that hates the attacker. At most 2 calls per faction per level, 30 s apart, none once
    ///   the faction is routed. Server only.
    /// </summary>
    internal static class FactionBackup
    {
        public const string Role = "Calls_Backup";
        public const int SquadSize = 3, CallsPerLevel = 2;
        public const float CooldownSeconds = 30f;

        private static readonly Dictionary<int, int> calls = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> lastCall = new Dictionary<int, float>();

        static FactionBackup() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            calls.Clear();
            lastCall.Clear();
        }

        public static void Initialize()
        {
            if (Array.IndexOf(RckData.FactionRoles, Role) < 0) Rck.Log.LogError($"Factions: the {Role} role is not in the generated data.");
        }

        /// <summary>A real attack on <paramref name="victim"/> by <paramref name="criminal"/> (see <see cref="FactionEvents.OnAttack"/>).</summary>
        internal static void OnAttack(Agent criminal, Agent victim)
        {
            if (victim == null || criminal == null || victim.isPlayer != 0 || victim.objectAgent || victim.dead) return;
            ulong keys = Factions.BackupOf(victim);
            if (keys == 0 || Squads.IsMember(victim)) return;
            keys &= ~Factions.MemberKeys(criminal, keys);
            if (keys != 0) Call(keys, criminal, victim.tr.position, $"{AgentText.Describe(criminal)} attacked {AgentText.Describe(victim)}");
        }

        /// <summary><paramref name="causer"/> pressed <paramref name="alarm"/> against <paramref name="criminal"/>.</summary>
        internal static void OnAlarm(AlarmButton alarm, Agent causer, Agent criminal)
        {
            if (alarm == null || causer == null || criminal == null || causer.isPlayer != 0 || causer.objectAgent) return;
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete) return;
            ulong keys = Squads.IsMember(causer) ? 0 : Factions.BackupOf(causer);
            if (keys == 0 && alarm.owner != 0 && gc.agentList != null)
                foreach (Agent a in gc.agentList)
                {
                    if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || Squads.IsMember(a)) continue;
                    if (a.ownerID == alarm.owner && a.startingChunk == alarm.startingChunk) keys |= Factions.BackupOf(a);
                }
            if (keys == 0) return;
            keys &= ~Factions.MemberKeys(criminal, keys);
            if (keys != 0) Call(keys, criminal, alarm.tr.position, $"{AgentText.Describe(causer)} raised the alarm on {AgentText.Describe(criminal)}");
        }

        private static void Call(ulong keys, Agent attacker, Vector2 where, string how)
        {
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete || attacker.dead || attacker.objectAgent) return;
            ulong open = keys & ~FactionEvents.Routed & ~Command.Bit;
            float now = Time.time;
            for (int i = 0; open != 0 && i < 64; i++, open >>= 1)
            {
                if ((open & 1UL) == 0) continue;
                calls.TryGetValue(i, out int n);
                if (n >= CallsPerLevel) continue;
                if (lastCall.TryGetValue(i, out float at) && now - at < CooldownSeconds) continue;
                Agent template = Squads.FindTemplate(gc, i, where);
                if (template == null) continue;
                var squad = new Squads.Squad { Kind = Squads.Kind.Backup, Key = i, Foe = attacker };
                Vector2? from = Squads.Origin(gc, i, where, 0, out TurfWar.Turf home);
                int made = Squads.Spawn(gc, template, SquadSize, squad, from);
                if (made == 0) continue;
                calls[i] = n + 1;
                lastCall[i] = now;
                Squads.Send(squad, where);
                string origin = home != null ? $" from its turf (owner {home.Owner}, chunk {home.Chunk})" : "";
                Rck.Log.LogInfo($"Factions: {how}; {Factions.Keys[i]} sent {Squads.Roster(squad)} as backup{origin} (call {n + 1} of {CallsPerLevel}).");
                if (FactionEvents.IsPlayerSide(attacker))
                    Squads.Announce(gc, "Debuff", $"{Squads.Capital(Squads.Plural(i))} called for backup!");
                return;
            }
        }
    }

    [HarmonyPatch(typeof(AlarmButton), nameof(AlarmButton.ToggleSwitch), typeof(Agent), typeof(Agent))]
    internal static class AlarmButton_ToggleSwitch_Backup_Patch
    {
        // An NPC's press only counts when it turns the alarm on (as vanilla, which spawns nothing while it's on).
        private static void Prefix(AlarmButton __instance, out bool __state) => __state = __instance.switchOn;

        private static void Postfix(AlarmButton __instance, Agent causerAgent, Agent criminal, bool __state)
        {
            if (__state) return;
            try { FactionBackup.OnAlarm(__instance, causerAgent, criminal); }
            catch (Exception e) { SocialRules.LogOnce(causerAgent, "faction-backup-alarm", e); }
        }
    }
}
