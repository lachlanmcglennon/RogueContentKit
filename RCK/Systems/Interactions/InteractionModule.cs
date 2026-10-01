using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK.Interactions
{
    public sealed class InteractionModule : IRckModule
    {
        public string Name => "Interactions";

        public void Initialize()
        {
            ButtonLabels.Register(typeof(CustomButtons));
            foreach (string language in LanguageSupport.LanguageTraits)
                if (language != "Polyglot" && !ButtonLabels.HasLabel(CustomButtons.Learn(language)))
                    Rck.Log.LogError($"No CustomButtons label for {CustomButtons.Learn(language)}.");

            RogueInteractions.CreateProvider<Agent>(h =>
            {
                Agent target = h.Object;
                Agent actor = h.Agent;
                if (target == null || actor == null || actor == target || target.dead || target.zombified || target.isPlayer > 0)
                    return;

                AddVanillaButtons(h);
                AddTeacherButtons(h);
                AddHackButtons(h);
                ApplyGates(h);
            });

            Rck.TraitAdded += static (agent, traitName, trait) =>
            {
                if (agent == null) return;
                AmbientAudio.Start(agent);
                if (traitName == "MapMarker_Pilot") SafeMinimap(agent);
            };
        }

        private static void AddVanillaButtons(SimpleInteractionProvider<Agent> h)
        {
            Agent target = h.Object;
            Agent actor = h.Agent;
            bool actorHas(string item) => actor.inventory != null && actor.inventory.HasItem(item);

            if (AgentTraits.Has(target, "Administer_Blood_Bag"))
                AddPressed(h, VanillaButtons.AdministerBloodBag, 0, " - 20HP");
            if (AgentTraits.Has(target, "Use_Blood_Bag") && actorHas("BloodBag"))
                AddPressed(h, VanillaButtons.UseBloodBag);
            if (AgentTraits.Has(target, "Give_Blood"))
                AddPressed(h, VanillaButtons.GiveBlood, 0, target.gc.challenges.Contains("LowHealth") ? " - 10HP/$20" : " - 20HP/$20");
            if (AgentTraits.Has(target, "Heal_Player"))
                AddPressed(h, VanillaButtons.Heal, target.determineMoneyCost("Heal"));
            if (AgentTraits.Has(target, "Identify"))
                AddPressed(h, VanillaButtons.Identify, target.determineMoneyCost("IdentifySyringe"));
            if (AgentTraits.Has(target, "Borrow_Money") || AgentTraits.Has(target, "Borrow_Money_Moocher")
                || (AgentTraits.Has(actor, "Panhandler") && !AgentTraits.Has(target, "Borrow_Money_Moocher")))
                AddPressed(h, VanillaButtons.BorrowMoney);
            if (AgentTraits.Has(target, "Bribe_Cops"))
                AddPressed(h, VanillaButtons.BribeCops, target.determineMoneyCost("BribeCops"));
            if (AgentTraits.Has(target, "Bribe_for_Entry_Alcohol"))
            {
                if (actorHas("Beer")) AddPressed(h, VanillaButtons.BribeBeer);
                else if (actorHas("Whiskey")) AddPressed(h, VanillaButtons.BribeWhiskey);
            }
            if (AgentTraits.Has(target, "Buy_Round"))
                AddPressed(h, VanillaButtons.BuyRound, target.determineMoneyCost(CountChunkAgents(target), "BuyRound"));
            if (AgentTraits.Has(target, "Buy_Slave"))
                AddPressed(h, VanillaButtons.PurchaseSlave, OwnsQuestSlave(target)
                    ? target.determineMoneyCost("QuestSlavePurchase")
                    : target.determineMoneyCost("SlavePurchase"));
            if (AgentTraits.Has(target, "Influence_Election"))
                AddPressed(h, VanillaButtons.ElectionBribe, target.determineMoneyCost("ElectionBribe"));
            if (AgentTraits.Has(target, "Election_Signup"))
                AddPressed(h, VanillaButtons.RunForOffice);
            if (AgentTraits.Has(target, "Election_Results"))
                AddPressed(h, VanillaButtons.GetElectionResults);
            if (AgentTraits.Has(target, "Election_Badge"))
                AddPressed(h, VanillaButtons.GiveMeMayorBadge);
            if (AgentTraits.Has(target, "Leave_Weapons_Behind") && actor.inventory != null && actor.inventory.HasWeapons())
                AddPressed(h, VanillaButtons.LeaveWeaponsBehind);
            if (AgentTraits.Has(target, "Manage_Chunk"))
            {
                AddPressed(h, VanillaButtons.BuyKey, target.determineMoneyCost("BuyKey"));
                AddPressed(h, VanillaButtons.BuySafeCombination, target.determineMoneyCost("BuySafeCombination"));
                AddPressed(h, VanillaButtons.BuyKeyHotel, target.determineMoneyCost("BuyKey"));
            }
            if (AgentTraits.Has(target, "Pay_Big_Quest"))
                AddPressed(h, VanillaButtons.PutMoneyTowardHome, target.determineMoneyCost("PutMoneyTowardHome"));
            if (AgentTraits.Has(target, "Pay_Debt"))
                AddPressed(h, VanillaButtons.PayBackDebt, Math.Max(0, actor.CalculateDebt()));
            if (AgentTraits.Has(target, "Pay_Entrance_Fee"))
                AddPressed(h, VanillaButtons.PayEntranceFee, target.determineMoneyCost("Bribe"));
            if (AgentTraits.Has(target, "Play_Bad_Music"))
            {
                if (actorHas("MayorEvidence"))
                    AddPressed(h, VanillaButtons.PlayMayorEvidence, target.determineMoneyCost("PlayMayorEvidence"));
                AddPressed(h, VanillaButtons.PlayBadMusic, target.determineMoneyCost("PlayBadMusic"));
            }
            if (AgentTraits.Has(target, "Offer_Motivation") && HasMotivation(actor))
                AddCustom(h, CustomButtons.OfferMotivation, 0, OfferMotivation);
        }

        private static void AddTeacherButtons(SimpleInteractionProvider<Agent> h)
        {
            Agent target = h.Object;
            Agent actor = h.Agent;
            if (!AgentTraits.Has(target, "Teach_Languages")) return;

            if (actor.statusEffects != null && actor.statusEffects.hasTrait("CantSpeakEnglish"))
                AddCustom(h, CustomButtons.LearnEnglish, 600, m =>
                {
                    if (!m.Object.moneySuccess(600)) { m.StopInteraction(); return; }
                    if (m.Agent.statusEffects != null) m.Agent.statusEffects.RemoveTrait("CantSpeakEnglish", false);
                    m.Agent.SayDialogue("RCK_Thanks");
                    m.StopInteraction();
                });

            foreach (string language in LanguageSupport.LanguageTraits)
            {
                if (language == "Polyglot") continue;
                if (AgentTraits.Has(actor, language)) continue;
                if (!LanguageSupport.CanTeach(target, language)) continue;
                string captured = language;
                AddCustom(h, CustomButtons.Learn(captured), 200, m =>
                {
                    if (!m.Object.moneySuccess(200)) { m.StopInteraction(); return; }
                    if (m.Agent.statusEffects != null) m.Agent.statusEffects.AddTrait(captured);
                    m.Agent.SayDialogue("RCK_Thanks");
                    m.StopInteraction();
                });
            }
        }

        private static void AddHackButtons(SimpleInteractionProvider<Agent> h)
        {
            Agent target = h.Object;
            Agent actor = h.Agent;
            if (actor.interactionHelper == null || !actor.interactionHelper.interactingFar || actor.inventory == null || !actor.inventory.HasItem("HackingTool"))
                return;

            if (AgentTraits.Has(target, "Explode"))
                AddCustom(h, CustomButtons.HackExplode, 0, static m =>
                {
                    m.Object.gc.spawnerMain.SpawnExplosion(m.Agent, m.Object.tr.position, "Hack");
                    m.Object.StopInteraction();
                });
            if (AgentTraits.Has(target, "Go_Haywire"))
                AddPressed(h, VanillaButtons.RobotEnrage);
            if (AgentTraits.Has(target, "Tamper_with_Aim"))
                AddPressed(h, VanillaButtons.TamperRobotAim);
        }

        private static void ApplyGates(SimpleInteractionProvider<Agent> h)
        {
            Agent target = h.Object;
            Agent actor = h.Agent;
            if (!IsTrustedForInteraction(target, actor))
            {
                RemoveSensitive(h);
                return;
            }

            if (AgentTraits.Has(target, "Cop_Access") && !IsCop(actor))
            {
                h.RemoveButton(VanillaButtons.Buy);
                h.RemoveButton(VanillaButtons.UseVoucher);
            }
            if (AgentTraits.Has(target, "Honorable_Thief") && !AgentTraits.Has(actor, "HonorAmongThieves") && !AgentTraits.Has(actor, "HonorAmongThieves2") && !IsThiefLike(actor))
            {
                h.RemoveButton(VanillaButtons.Buy);
                h.RemoveButton(VanillaButtons.UseVoucher);
            }
        }

        private static bool IsTrustedForInteraction(Agent target, Agent actor)
        {
            int threshold = TrustThreshold(target);
            if (threshold == 0) return true;
            if (target.relationships == null) return false;
            Relationship rel = target.relationships.GetRelationship(actor);
            if (rel.relType == "Aligned" || rel.relType == "Loyal" || rel.relType == "Friendly") return true;
            if (target.employer == actor) return true;
            if (threshold <= 1 && (SameFamily(target, actor) || LanguageSupport.CanUnderstand(target, actor))) return true;
            if (threshold <= 2 && SameFamily(target, actor)) return true;
            return false;
        }

        private static int TrustThreshold(Agent target)
        {
            if (AgentTraits.Has(target, "Untrustingest") || AgentTraits.Has(target, "Insularest")) return 3;
            if (AgentTraits.Has(target, "Untrustinger") || AgentTraits.Has(target, "Insularer")) return 2;
            if (AgentTraits.Has(target, "Untrusting") || AgentTraits.Has(target, "Insular")) return 1;
            return 0;
        }

        private static void RemoveSensitive(SimpleInteractionProvider<Agent> h)
        {
            foreach (string name in SensitiveButtons)
                h.RemoveButton(name);
            h.RemoveButton(static i => i.ButtonName != null && i.ButtonName.StartsWith("RCK_", StringComparison.Ordinal));
        }

        private static readonly string[] SensitiveButtons =
        {
            VanillaButtons.Buy, VanillaButtons.UseVoucher, VanillaButtons.Heal, VanillaButtons.Identify, VanillaButtons.GiveBlood,
            VanillaButtons.UseBloodBag, VanillaButtons.AdministerBloodBag, VanillaButtons.BorrowMoney, VanillaButtons.BribeCops,
            VanillaButtons.BribeBeer, VanillaButtons.BribeWhiskey, VanillaButtons.BuyRound, VanillaButtons.PurchaseSlave,
            VanillaButtons.ElectionBribe, VanillaButtons.RunForOffice, VanillaButtons.GetElectionResults, VanillaButtons.GiveMeMayorBadge,
            VanillaButtons.LeaveWeaponsBehind, VanillaButtons.BuyKey, VanillaButtons.BuySafeCombination, VanillaButtons.BuyKeyHotel,
            VanillaButtons.PutMoneyTowardHome, VanillaButtons.PayBackDebt, VanillaButtons.PayEntranceFee, VanillaButtons.PlayBadMusic,
            VanillaButtons.PlayMayorEvidence, CustomButtons.HackExplode, VanillaButtons.RobotEnrage, VanillaButtons.TamperRobotAim
        };

        private static readonly HashSet<string> rejected = new HashSet<string>(StringComparer.Ordinal);

        private static void AddPressed(SimpleInteractionProvider<Agent> h, string buttonName, int price = 0, string? extra = null)
        {
            if (!VanillaButtons.IsKnown(buttonName))
            {
                if (rejected.Add(buttonName)) Rck.Log.LogError($"'{buttonName}' is not a known vanilla button; not adding it.");
                return;
            }
            if (h.HasButton(buttonName)) return;
            h.AddButton(buttonName, price > 0 ? price : (int?)null, extra, m => m.Object.agentInteractions.PressedButton(m.Object, m.Agent, buttonName, price));
        }

        private static void AddCustom(SimpleInteractionProvider<Agent> h, string buttonName, int price, Action<InteractionModel<Agent>> action)
        {
            if (h.HasButton(buttonName)) return;
            ButtonLabels.Ensure(buttonName);
            h.AddButton(buttonName, price > 0 ? price : (int?)null, action);
        }

        private static int CountChunkAgents(Agent target)
        {
            int count = 0;
            List<Agent> agents = target.gc.agentList;
            for (int i = 0; i < agents.Count; i++)
            {
                Agent a = agents[i];
                if (a != null && !a.dead && a.startingChunk != 0 && a.startingChunk == target.startingChunk) count++;
            }
            return Math.Max(1, count);
        }

        private static bool HasMotivation(Agent actor) => actor.inventory != null && MotivationItems.Any(item => actor.inventory.HasItem(item));

        private static void OfferMotivation(InteractionModel<Agent> m)
        {
            foreach (string itemName in MotivationItems)
            {
                if (m.Agent.inventory == null) break;
                InvItem item = m.Agent.inventory.FindItem(itemName);
                if (item == null || item.invItemCount <= 0) continue;
                m.Agent.inventory.SubtractFromItemCount(item, 1);
                m.Object.relationships.SetRel(m.Agent, "Friendly");
                m.Object.SayDialogue("RCK_Thanks");
                m.StopInteraction();
                return;
            }
            m.Agent.SayDialogue("RCK_NeedItem");
            m.StopInteraction();
        }

        private static readonly string[] MotivationItems = { "Beer", "Whiskey", "Cocktail", "Cigarettes", "BaconCheeseburger", "HamSandwich", "Fud", "HotFud" };

        private static bool SameFamily(Agent a, Agent b)
        {
            return a.agentName == b.agentName
                || (IsCop(a) && IsCop(b))
                || (IsThiefLike(a) && IsThiefLike(b))
                || (a.agentRealName != null && a.agentRealName == b.agentRealName);
        }

        private static bool IsCop(Agent a) => a.enforcer || a.agentName.Contains("Cop") || a.agentName.Contains("Soldier") || AgentTraits.Has(a, "TheLaw");
        private static bool IsThiefLike(Agent a) => a.agentName.Contains("Thief") || a.agentName.Contains("Gangbanger") || a.agentName.Contains("Mobster");

        // Vanilla prices a slave as a quest slave only while one of the owner's slaves is a rescue target.
        private static bool OwnsQuestSlave(Agent owner)
        {
            List<Agent> agents = owner.gc.agentList;
            for (int i = 0; i < agents.Count; i++)
            {
                Agent a = agents[i];
                if (a != null && a.agentName == "Slave" && a.slaveOwners.Contains(owner)
                    && (a.rescueForQuest != null || (!a.gc.serverPlayer && a.oma != null && a.oma.rescuingForQuest)))
                    return true;
            }
            return false;
        }

        internal static void SafeMinimap(Agent agent)
        {
            if (agent.nonQuestObjectMarker == null)
            {
                try { agent.MinimapDisplay(); }
                catch (Exception e) { Rck.Log.LogDebug($"MapMarker_Pilot failed for {agent.agentName}: {e.Message}"); return; }
            }
            PilotMarker.Watch(agent);
        }
    }

    /// <summary>
    ///   MapMarker_Pilot. Vanilla makes a map marker for any NPC but only shows it for shopkeepers, bartenders and the
    ///   like. Once a player has seen this NPC, its marker shows on the big map as a blue arrow with the NPC's name, the
    ///   way vanilla shows a shopkeeper. A marker something else has restyled (an RCK job) is left alone.
    /// </summary>
    internal static class PilotMarker
    {
        private const string Trait = "MapMarker_Pilot";
        private const float CheckSeconds = 0.25f;

        private sealed class Watcher { public float At; }

        // Unity stops an agent's coroutines without running finally blocks when the agent goes back to the pool, so a
        // watcher counts as gone once it hasn't checked in for a while.
        private static readonly Dictionary<Agent, Watcher> watching = new Dictionary<Agent, Watcher>();

        public static void Watch(Agent agent)
        {
            if (agent == null || !agent.isActiveAndEnabled) return;
            float now = Time.time;
            if (watching.TryGetValue(agent, out Watcher w) && now >= w.At && now - w.At < CheckSeconds * 4) return;
            if (watching.Count > 64)
                foreach (Agent gone in watching.Keys.Where(a => a == null).ToList()) watching.Remove(gone);
            w = new Watcher { At = now };
            watching[agent] = w;
            agent.StartCoroutine(Run(agent, agent.agentID, w));
        }

        private static IEnumerator Run(Agent agent, int id, Watcher w)
        {
            var wait = new WaitForSeconds(CheckSeconds);
            int remakes = 0;
            while (agent != null && agent.agentID == id && !agent.dead && AgentTraits.Has(agent, Trait)
                && watching.TryGetValue(agent, out Watcher current) && current == w)
            {
                w.At = Time.time;
                QuestMarker m = agent.nonQuestObjectMarker;
                if (m != null) remakes = 0;
                // Something (a finished RCK job, a recycle) dropped the marker: make a fresh one.
                if (m == null && remakes < 3 && agent.gameObject.activeInHierarchy)
                {
                    remakes++;
                    try { agent.MinimapDisplay(); }
                    catch (Exception e) { Rck.Log.LogDebug($"MapMarker_Pilot failed for {agent.agentName}: {e.Message}"); }
                }
                else if (m != null && m.reallyStarted && m.playerSeen && m.colorInvis && !m.isBigQuestMarker && m.questMarkerSmall2 != null)
                {
                    try { Show(m, agent); }
                    catch (Exception e) { Rck.Log.LogDebug($"MapMarker_Pilot couldn't show {agent.agentName}: {e.Message}"); break; }
                }
                yield return wait;
            }
            if (agent != null && watching.TryGetValue(agent, out Watcher last) && last == w) watching.Remove(agent);
        }

        private static void Show(QuestMarker m, Agent agent)
        {
            string name = string.IsNullOrEmpty(agent.agentRealName) ? agent.agentName : agent.agentRealName;
            if (m.smallImage2 != null)
            {
                if (m.targetSmallBlue != null) m.smallImage2.sprite = m.targetSmallBlue;
                m.smallImage2.color = m.vis;
            }
            m.colorVis = true;
            m.colorInvis = false;
            m.questMarkerSmall2.markerName = name;
            if (m.questMarkerSmall2.myText != null) m.questMarkerSmall2.myText.text = name;
        }
    }

    /// <summary>Custom agent buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
    internal static class CustomButtons
    {
        [ButtonLabel("Offer Motivation")] public const string OfferMotivation = "RCK_OfferMotivation";
        [ButtonLabel("Learn English")] public const string LearnEnglish = "RCK_LearnEnglish";
        [ButtonLabel("Learn Binary")] public const string LearnBinary = LearnPrefix + "Speaks_Binary";
        [ButtonLabel("Learn Chthonic")] public const string LearnChthonic = LearnPrefix + "Speaks_Chthonic";
        [ButtonLabel("Learn ErSdtAdt")] public const string LearnErSdtAdt = LearnPrefix + "Speaks_ErSdtAdt";
        [ButtonLabel("Learn Foreign")] public const string LearnForeign = LearnPrefix + "Speaks_Foreign";
        [ButtonLabel("Learn High Goryllian")] public const string LearnHighGoryllian = LearnPrefix + "Speaks_High_Goryllian";
        [ButtonLabel("Learn Undercant")] public const string LearnUndercant = LearnPrefix + "Speaks_Undercant";
        [ButtonLabel("Learn Werewelsh")] public const string LearnWerewelsh = LearnPrefix + "Speaks_Werewelsh";

        // Reuses the vanilla object-hack label; RCK handles the press itself for agents.
        public const string HackExplode = "HackExplode";

        private const string LearnPrefix = "RCK_Learn_";
        public static string Learn(string languageTrait) => LearnPrefix + languageTrait;
    }

    internal static class LanguageSupport
    {
        public static readonly string[] LanguageTraits =
        {
            "Polyglot", "Speaks_Binary", "Speaks_Chthonic", "Speaks_ErSdtAdt", "Speaks_Foreign",
            "Speaks_High_Goryllian", "Speaks_Undercant", "Speaks_Werewelsh"
        };

        public static bool CanTeach(Agent teacher, string language) => AgentTraits.Has(teacher, language) || SpeaksNative(teacher, language);

        public static bool CanUnderstand(Agent a, Agent b)
        {
            return AgentTraits.Has(a, "Polyglot") || AgentTraits.Has(b, "Polyglot")
                || CanUnderstandOneWay(a, b) || CanUnderstandOneWay(b, a);
        }

        private static bool CanUnderstandOneWay(Agent listener, Agent speaker)
        {
            foreach (string language in LanguageTraits)
            {
                if (language == "Polyglot") continue;
                if (AgentTraits.Has(listener, language) && SpeaksNative(speaker, language)) return true;
            }
            return false;
        }

        private static bool SpeaksNative(Agent speaker, string language)
        {
            string name = speaker.agentName ?? string.Empty;
            switch (language)
            {
                case "Speaks_Binary": return name.Contains("Robot") || name.Contains("CopBot") || name.Contains("Mech");
                case "Speaks_Chthonic": return speaker.zombified || name.Contains("Zombie") || name.Contains("Ghost") || name.Contains("Vampire") || name.Contains("ShapeShifter");
                case "Speaks_ErSdtAdt": return name.Contains("Alien") || name.Contains("Slavemaster");
                case "Speaks_Foreign": return (speaker.statusEffects != null && speaker.statusEffects.hasTrait("CantSpeakEnglish")) || name.Contains("Alien");
                case "Speaks_High_Goryllian": return name.Contains("Gorilla");
                case "Speaks_Undercant": return name.Contains("Thief") || name.Contains("Gangbanger") || name.Contains("Mobster") || name.Contains("Cannibal");
                case "Speaks_Werewelsh": return name.Contains("Werewolf");
                default: return false;
            }
        }
    }

    internal static class AmbientAudio
    {
        private static readonly Dictionary<string, string> ClipByTrait = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sawblade_Sound"] = "SawBladeRun",
            ["Generating_Sounds"] = "GeneratorAmbience",
            ["Generating_Overclocked_Sounds"] = "OverclockedGeneratorAmbience",
            ["Computation_Noises"] = "ComputerAmbience",
            ["Conveying_Noises"] = "ConveyorBelt",
            ["Fire_Noises"] = "FireConstant",
            ["Powering_Noises"] = "PowerBox",
            ["Movie_Screen_Sounds"] = "MovieScreen",
            ["Ventulations"] = "AirFiltrationAmbience",
            // No vanilla object owns these five sounds; each uses the closest-sounding vanilla loop.
            ["Squeakitations"] = "MineCart",
            ["Whhhhhhhh"] = "GasConstant",
            ["Wummmmmm"] = "LaserAmbience",
            ["Zzzzzzzzzzzz"] = "LampPostAmbience",
            ["Woof"] = "FlamingBarrelCrackle",
            ["Choochootations"] = "Train",
            ["Cop_Bot_Sound"] = "CopBotCam"
        };

        public static void Start(PlayfieldObject obj)
        {
            if (obj?.gc?.audioHandler == null) return;
            foreach (KeyValuePair<string, string> pair in ClipByTrait)
            {
                if (!(obj is Agent agent) || !AgentTraits.Has(agent, pair.Key)) continue;
                if (obj.gc.audioHandler.IsPlaying(obj, pair.Value)) continue;
                try { obj.gc.audioHandler.Play(obj, pair.Value); }
                catch (Exception e) { Rck.Log.LogDebug($"Ambient audio '{pair.Value}' failed for {pair.Key}: {e.Message}"); }
            }
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.CanUnderstandEachOther), typeof(Agent), typeof(bool), typeof(bool))]
    internal static class CanUnderstandEachOtherPatch
    {
        private static void Postfix(Agent __instance, Agent otherAgent, ref bool __result)
        {
            if (__result || __instance == null || otherAgent == null) return;
            if (LanguageSupport.CanUnderstand(__instance, otherAgent)) __result = true;
        }
    }

    [HarmonyPatch(typeof(Agent), "Start")]
    internal static class AgentStartPatch
    {
        private static void Postfix(Agent __instance)
        {
            AmbientAudio.Start(__instance);
            if (AgentTraits.Has(__instance, "MapMarker_Pilot")) InteractionModule.SafeMinimap(__instance);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleStart))]
    internal static class AgentRecycleStartPatch
    {
        private static void Postfix(Agent __instance)
        {
            AmbientAudio.Start(__instance);
            if (AgentTraits.Has(__instance, "MapMarker_Pilot")) InteractionModule.SafeMinimap(__instance);
        }
    }
}
