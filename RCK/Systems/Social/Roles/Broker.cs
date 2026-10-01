#nullable disable
using System;
using System.Collections.Generic;
using RogueLibsCore;

namespace RCK.Social
{
    /// <summary>
    ///   The <c>RCK_Broker</c> trait: an NPC that sells faction deals for the rest of the level. <c>Broker a truce</c>
    ///   makes two chosen factions Neutral toward each other, both ways; <c>Frame a faction</c> makes the second
    ///   Hateful toward the first. Both are <see cref="LevelRelations"/> rules. The choices cycle through the factions
    ///   with NPC members here and stay with the broker for the level. Server only (the buttons need the host).
    /// </summary>
    internal static class Broker
    {
        public const string Trait = "RCK_Broker";
        public const int TruceBase = 100, FrameBase = 75, PerLevel = 25;

        private sealed class Pick
        {
            public int A = -1, B = -1;
        }

        private static readonly Dictionary<Agent, Pick> picks = new Dictionary<Agent, Pick>();

        static Broker() => LevelScope.ResetAtBoth(ClearPicks);

        private static void ClearPicks() => picks.Clear();

        public static void Initialize()
        {
            if (!Rck.IsRckTrait(Trait)) Rck.Log.LogError($"Factions: broker trait {Trait} is not registered.");
            ButtonLabels.Register(typeof(CustomButtons));
            RogueInteractions.CreateProvider<Agent>(Provide);
        }

        private static void Provide(SimpleInteractionProvider<Agent> h)
        {
            Agent npc = h.Object;
            Agent player = h.Agent;
            if (npc == null || player == null || npc == player || npc.isPlayer > 0 || npc.dead || !SocialRules.Has(npc, Trait)) return;
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete || h.Helper.interactingFar || npc.relationships == null) return;
            relStatus rel = npc.relationships.GetRelCode(player);
            if (rel == relStatus.Hostile || rel == relStatus.Annoyed) return;
            List<int> present = Present(gc);
            if (present.Count < 2) return;
            Pick p = PickOf(npc, present);
            string a = Squads.Plural(p.A), b = Squads.Plural(p.B);
            int truce = Price(gc, TruceBase), frame = Price(gc, FrameBase);
            if (!AtPeace(p)) h.AddButton(CustomButtons.Truce, truce, $" ({a} & {b})", m => Truce(m, truce));
            h.AddButton(CustomButtons.Frame, frame, $" ({a} for hitting {b})", m => Frame(m, frame));
            h.AddButton(CustomButtons.First, m => Cycle(m, first: true));
            h.AddButton(CustomButtons.Second, m => Cycle(m, first: false));
        }

        private static int Price(GameController gc, int basePrice)
        {
            int level = gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 1;
            return basePrice + PerLevel * Math.Max(1, level);
        }

        /// <summary>A truce this level already covers the pair, both ways (a later frame breaks it).</summary>
        private static bool AtPeace(Pick p) => LevelRelations.Peace(p.A, p.B) && LevelRelations.Peace(p.B, p.A);

        /// <summary>Factions with living NPC members here that aren't routed or in a party, in key order.</summary>
        private static List<int> Present(GameController gc)
        {
            ulong mask = 0;
            if (gc.agentList != null)
                foreach (Agent a in gc.agentList)
                {
                    if (a == null || a.dead || a.isPlayer != 0 || a.objectAgent || a.zombified || a.ghost) continue;
                    if (a.employer != null && a.employer.isPlayer > 0) continue;
                    mask |= Factions.KeysOf(a);
                }
            mask &= ~FactionEvents.Routed;
            var list = new List<int>();
            for (int i = 0; i < Factions.Keys.Count; i++)
                if ((mask & (1UL << i)) != 0) list.Add(i);
            return list;
        }

        private static Pick PickOf(Agent npc, List<int> present)
        {
            if (!picks.TryGetValue(npc, out Pick p)) picks[npc] = p = new Pick();
            if (!present.Contains(p.A)) p.A = present[0];
            if (!present.Contains(p.B) || p.B == p.A) p.B = Next(present, p.A, p.A);
            return p;
        }

        /// <summary>The faction after <paramref name="from"/> in <paramref name="present"/>, skipping <paramref name="skip"/>.</summary>
        private static int Next(List<int> present, int from, int skip)
        {
            int i = present.IndexOf(from);
            for (int step = 1; step <= present.Count; step++)
            {
                int k = present[(i + step + present.Count) % present.Count];
                if (k != skip) return k;
            }
            return from;
        }

        /// <summary>The current pick, still valid for this level; null (and the menu closed) otherwise.</summary>
        private static Pick Current(InteractionModel<Agent> m, out List<int> present)
        {
            present = null;
            GameController gc = FactionEvents.Server();
            if (gc == null || !gc.loadComplete || m.Object == null || m.Object.dead) return null;
            present = Present(gc);
            if (present.Count < 2) return null;
            return PickOf(m.Object, present);
        }

        private static void Cycle(InteractionModel<Agent> m, bool first)
        {
            Pick p = Current(m, out List<int> present);
            if (p == null)
            {
                m.StopInteraction();
                return;
            }
            if (first) p.A = Next(present, p.A, p.B);
            else p.B = Next(present, p.B, p.A);
        }

        private static void Truce(InteractionModel<Agent> m, int price)
        {
            Pick p = Current(m, out _);
            if (p == null || AtPeace(p) || !m.Object.moneySuccess(price))
            {
                m.StopInteraction();
                return;
            }
            int changed = LevelRelations.Set(p.A, p.B, "Neutral", true);
            Rck.Log.LogInfo($"Factions: {AgentText.Describe(m.Agent)} paid {AgentText.Describe(m.Object)} ${price} for a truce between {Factions.Keys[p.A]} and {Factions.Keys[p.B]} ({changed} pair(s) changed).");
            Show(m, $"Done. {Squads.Capital(Squads.Plural(p.A))} and {Squads.Plural(p.B)} will keep the peace for the rest of the day.");
        }

        private static void Frame(InteractionModel<Agent> m, int price)
        {
            Pick p = Current(m, out _);
            if (p == null || !m.Object.moneySuccess(price))
            {
                m.StopInteraction();
                return;
            }
            int changed = LevelRelations.Set(p.B, p.A, "Hateful", false);
            Rck.Log.LogInfo($"Factions: {AgentText.Describe(m.Agent)} paid {AgentText.Describe(m.Object)} ${price} to frame {Factions.Keys[p.A]} for hitting {Factions.Keys[p.B]} ({changed} pair(s) changed).");
            Show(m, $"Done. {Squads.Capital(Squads.Plural(p.B))} think {Squads.Plural(p.A)} hit them. Expect fireworks.");
        }

        private static void Show(InteractionModel<Agent> m, string text)
        {
            m.Object.ShowBigImage(text, string.Empty, null);
            m.Agent.worldSpaceGUI?.HideObjectButtons();
        }
    }

    /// <summary>Broker buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
    internal static class CustomButtons
    {
        [ButtonLabel("Broker a truce")] public const string Truce = "RCK_BrokerTruce";
        [ButtonLabel("Frame a faction")] public const string Frame = "RCK_BrokerFrame";
        [ButtonLabel("Change first faction")] public const string First = "RCK_BrokerFirst";
        [ButtonLabel("Change second faction")] public const string Second = "RCK_BrokerSecond";
    }
}
