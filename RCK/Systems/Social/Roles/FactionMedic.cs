#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The <c>RCK_Faction_Medic</c> trait: every few seconds the holder patches up the most hurt friend within reach,
    ///   itself included. Friends are members of its factions, its employer and fellow followers, and the players while
    ///   it's sworn to the faction they command. Not while it's out of action, and never someone it's Hostile or
    ///   Annoyed toward. Server only.
    /// </summary>
    internal static class FactionMedic
    {
        public const string Trait = "RCK_Faction_Medic";
        public const float Range = 3.2f;
        public const float Cooldown = 5f;
        public const float Share = 0.15f;
        public const int Least = 8;

        private static readonly Dictionary<Agent, float> due = new Dictionary<Agent, float>();
        private static readonly List<Agent> medics = new List<Agent>();

        static FactionMedic() => LevelScope.ResetAtBoth(ClearDue);

        private static void ClearDue() => due.Clear();

        public static void Initialize()
        {
            if (!Rck.IsRckTrait(Trait)) Rck.Log.LogError($"Factions: medic trait {Trait} is not registered.");
        }

        internal static void Tick(GameController gc, float now)
        {
            medics.Clear();
            foreach (Agent a in gc.agentList)
                if (Able(a) && SocialRules.Has(a, Trait)) medics.Add(a);
            if (medics.Count == 0) return;

            ulong commanded = Command.Bit;
            foreach (Agent medic in medics)
            {
                if (due.TryGetValue(medic, out float at) && now < at) continue;
                Agent patient = Worst(gc, medic, commanded);
                if (patient == null) continue;
                int heal = Math.Max(Least, Mathf.RoundToInt(patient.healthMax * Share));
                heal = Math.Min(heal, Mathf.CeilToInt(patient.healthMax - patient.health));
                if (heal <= 0) continue;
                patient.statusEffects.ChangeHealth(heal);
                gc.audioHandler.Play(patient, "Heal");
                due[medic] = now + Cooldown;
            }
        }

        private static bool Able(Agent a)
            => a != null && !a.dead && !a.ghost && !a.objectAgent && a.isPlayer == 0 && !a.arrested && !a.zombified && !a.hologram
               && !a.butlerBot && a.health > 0f;

        /// <summary>The friend in reach with the least health left, as a share of its most.</summary>
        private static Agent Worst(GameController gc, Agent medic, ulong commanded)
        {
            ulong keys = Factions.KeysOf(medic);
            bool sworn = commanded != 0 && (keys & commanded) != 0;
            Vector2 at = medic.curPosition;
            Agent best = null;
            float bestShare = 1f;
            foreach (Agent p in gc.agentList)
            {
                if (p == null || p.dead || p.ghost || p.objectAgent || p.hologram || p.butlerBot || p.mechEmpty) continue;
                if (p.healthMax <= 0f || p.health <= 0f || p.health >= p.healthMax) continue;
                float share = p.health / p.healthMax;
                if (share >= bestShare) continue;
                if (Vector2.Distance(at, p.curPosition) > Range) continue;
                if (!Friend(medic, p, keys, sworn)) continue;
                best = p;
                bestShare = share;
            }
            return best;
        }

        private static bool Friend(Agent medic, Agent p, ulong keys, bool sworn)
        {
            if (p == medic) return true;
            if (medic.relationships != null)
            {
                relStatus rel = medic.relationships.GetRelCode(p);
                if (rel == relStatus.Hostile || rel == relStatus.Annoyed) return false;
            }
            if (medic.employer != null && (p == medic.employer || p.employer == medic.employer)) return true;
            if (p.employer == medic) return true;
            if (p.isPlayer > 0 && sworn) return true;
            return (keys & Factions.KeysOf(p)) != 0;
        }
    }
}
