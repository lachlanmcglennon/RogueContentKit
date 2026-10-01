using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK.Campaign
{
    public sealed class CampaignModule : IRckModule
    {
        private static readonly HashSet<string> SceneGoals = new HashSet<string>(
            RckData.Goals.Where(static g => g.Lists.Contains("SceneSetters_Active")).Select(static g => g.Goal),
            StringComparer.Ordinal);

        // Pooled agents come back on later levels (Agent.RecycleStart2 re-runs Start) with a fresh agentID/UID, so the
        // marker is a stamp, not a presence flag: a recycled NPC gets its new scene setter, a repeat call doesn't.
        private static readonly ConditionalWeakTable<Agent, SceneStamp> AppliedSceneGoals = new ConditionalWeakTable<Agent, SceneStamp>();

        public string Name => "Campaign";

        public void Initialize()
        {
            RegisterMutators();
            RegisterItemsAndEffects();
            RegisterLevelGateSwitchTraits();
            Rck.TraitAdded += static (agent, trait, _) =>
            {
                if (agent != null && trait == "Quest_Giver") ApplyQuestGiverMarker(agent);
            };
        }

        internal static void ApplySceneSetter(Agent agent)
        {
            if (agent == null || agent.defaultGoal == null || !SceneGoals.Contains(agent.defaultGoal)) return;
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer) return;
            SceneStamp stamp = AppliedSceneGoals.GetOrCreateValue(agent);
            int level = LevelScope.CurrentId();
            if (stamp.Set && stamp.Level == level && stamp.AgentId == agent.agentID && stamp.Uid == agent.UID) return;
            stamp.Set = true;
            stamp.Level = level;
            stamp.AgentId = agent.agentID;
            stamp.Uid = agent.UID;

            try
            {
                switch (agent.defaultGoal)
                {
                    case "Arrested":
                        agent.arrested = true;
                        agent.deathMethod = "Tranquilized";
                        agent.statusEffects.ChangeHealth(-9999f);
                        break;
                    case "Burned":
                        agent.statusEffects.AddStatusEffect("OnFire", showText: false, dontPrevent: true);
                        agent.deathMethod = "Fire";
                        agent.statusEffects.ChangeHealth(-9999f);
                        break;
                    case "Dead":
                        agent.deathMethod = "Falling";
                        agent.statusEffects.ChangeHealth(-9999f);
                        break;
                    case "Electrocuted":
                        agent.statusEffects.AddStatusEffect("Electrocuted", showText: false, dontPrevent: true, specificTime: 9999);
                        break;
                    case "Electrocuted (Permanent)":
                        agent.statusEffects.AddStatusEffect("Electrocuted_Permanent", showText: false, dontPrevent: true, specificTime: 999999);
                        break;
                    case "Frozen":
                        agent.statusEffects.AddStatusEffect("Frozen", showText: false, dontPrevent: true, specificTime: 9999);
                        break;
                    case "Frozen (Fragile)":
                        agent.statusEffects.AddStatusEffect("Frozen_Fragile", showText: false, dontPrevent: true, specificTime: 999999);
                        break;
                    case "Frozen (Permanent)":
                        agent.statusEffects.AddStatusEffect("Frozen_Permanent", showText: false, dontPrevent: true, specificTime: 999999);
                        break;
                    case "Gibbed":
                        agent.deathMethod = "Gibbed";
                        agent.statusEffects.ChangeHealth(-9999f);
                        agent.statusEffects.NormalGib();
                        break;
                    case "Knocked Out":
                        agent.deathMethod = "Tranquilized";
                        agent.statusEffects.ChangeHealth(-9999f);
                        break;
                    case "Random Teleport (Duo)":
                        TeleportRandomPublic(agent);
                        SpawnFollowers(agent, 1);
                        break;
                    case "Random Teleport (Gang)":
                        TeleportRandomPublic(agent);
                        SpawnFollowers(agent, 3);
                        break;
                    case "Random Teleport (Public)":
                        TeleportRandomPublic(agent);
                        break;
                    case "Zombified":
                        agent.customZombified = true;
                        agent.statusEffects.AddTrait("Zombify");
                        agent.deathMethod = "ZombieSpit";
                        agent.statusEffects.ChangeHealth(-9999f);
                        break;
                }
            }
            catch (Exception ex)
            {
                Rck.Log.LogWarning($"Could not apply default goal '{agent.defaultGoal}' to {agent.agentName}: {ex.Message}");
            }
        }

        private sealed class SceneStamp
        {
            internal bool Set;
            internal int Level;
            internal int AgentId;
            internal int Uid;
        }

        internal static void NormalizeCharacter(SaveCharacterData? data)
        {
            if (data?.traits == null) return;
            data.traits = NormalizeList(data.traits, RckData.TraitConversions);
        }

        internal static void NormalizeMutators(IList<string>? mutators)
        {
            if (mutators == null) return;
            List<string> normalized = NormalizeList(mutators.ToList(), RckData.MutatorConversions);
            mutators.Clear();
            foreach (string item in normalized) mutators.Add(item);
        }

        internal static void ApplyQuestGiverMarker(Agent agent)
        {
            if (!AgentTraits.Has(agent, "Quest_Giver")) return;
            agent.important = true;
            agent.importantToClient = true;
        }

        private static List<string> NormalizeList(List<string> original, IReadOnlyDictionary<string, string[]> conversions)
        {
            List<string> result = new List<string>();
            foreach (string item in original)
            {
                if (conversions.TryGetValue(item, out string[] replacements))
                {
                    foreach (string replacement in replacements)
                    {
                        if (!result.Contains(replacement)) result.Add(replacement);
                    }
                }
                else if (!result.Contains(item))
                {
                    result.Add(item);
                }
            }
            return result;
        }

        // Vanilla keeps its random events this far from the elevators; CCU's Random Teleport also avoids the start.
        private const float MinStartDistance = 20f;
        private const int TeleportTries = 50;

        private static void TeleportRandomPublic(Agent agent)
        {
            GameController gc = GameController.gameController;
            Vector2? start = StartPosition(gc);
            Vector2 best = Vector2.zero;
            float bestDistance = -1f;
            for (int i = 0; i < TeleportTries; i++)
            {
                Vector2 pos = gc.tileInfo.FindRandLocation(agent, includeOwned: false);
                if (pos == Vector2.zero) continue;
                float distance = start.HasValue ? Vector2.Distance(pos, start.Value) : float.MaxValue;
                if (distance > bestDistance)
                {
                    best = pos;
                    bestDistance = distance;
                }
                if (distance >= MinStartDistance) break;
            }
            if (best == Vector2.zero) return;
            agent.Teleport(new Vector3(best.x, best.y, agent.tr.position.z), bringOthers: false, immediate: true);
        }

        private static Vector2? StartPosition(GameController gc)
        {
            if (gc.startingPoint != null) return gc.startingPoint.transform.position;
            if (gc.elevatorUp != null) return gc.elevatorUp.transform.position;
            return null;
        }

        /// <summary>
        ///   Spawns followers of the leader's own kind. For a custom leader, SpawnAgent copies its character data (look,
        ///   traits, items), so faction traits carry over. <see cref="FollowerLinks"/> sets the relationships once the
        ///   game has set up the new agent's.
        /// </summary>
        private static void SpawnFollowers(Agent leader, int count)
        {
            GameController gc = GameController.gameController;
            string type = leader.agentName;
            if (string.IsNullOrEmpty(type) || (type == "Custom" && leader.customCharacterData == null)) type = "Gangbanger";
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = gc.tileInfo.FindLocationNearLocation(leader.tr.position, leader, 0.32f, 1.28f, accountForObstacles: true, notInside: false);
                if (pos == Vector2.zero) continue;
                Agent follower = gc.spawnerMain.SpawnAgent(new Vector3(pos.x, pos.y, leader.tr.position.z), leader, type, "RCKTeleport", leader);
                if (follower == null) continue;
                SpawnedAgents.Arm(follower, SpawnedAgents.LoadoutOf(leader));
                FollowerLinks.Add(follower, leader);
                follower.SetDefaultGoal("Follow");
                follower.employer = leader;
            }
        }

        private static void RegisterMutators()
        {
            foreach (RckNamedInfo info in RckData.Mutators.Concat(RckData.ExtensionMutators))
            {
                try
                {
                    RogueLibs.CreateCustomUnlock(new MutatorUnlock(info.Name, true))
                        .WithName(new CustomNameInfo(info.Display))
                        .WithDescription(new CustomNameInfo(info.Description ?? $"{info.Display} campaign option."));
                }
                catch (Exception ex)
                {
                    Rck.Log.LogDebug($"Mutator '{info.Name}' already registered or unavailable: {ex.Message}");
                }
            }

            SafeRegister(static () => RogueLibs.CreateCustomUnlock(new MutatorUnlock(LegacyCcu.LevelGateMenuHead, true))
                .WithName(new CustomNameInfo(LevelGateRuntime.DisplayName))
                .WithDescription(new CustomNameInfo("Configures level-gate data mutators.")));
        }

        private static void RegisterItemsAndEffects()
        {
            SafeRegister(static () => RogueLibs.CreateCustomItem<ClassAWare>().WithName(new CustomNameInfo("Class-A-Ware")).WithUnlock());
            SafeRegister(static () => RogueLibs.CreateCustomItem<Debugulizer>().WithName(new CustomNameInfo("Debugulizer")).WithUnlock());
            SafeRegister(static () => RogueLibs.CreateCustomItem<RubberBulletsMod>().WithName(new CustomNameInfo("Rubber Bullets Mod")).WithUnlock());
            SafeRegister(static () => RogueLibs.CreateCustomEffect<Electrocuted_Permanent>().WithName(new CustomNameInfo("Electrocuted (Permanent)")));
            SafeRegister(static () => RogueLibs.CreateCustomEffect<Frozen_Fragile>().WithName(new CustomNameInfo("Frozen (Fragile)")));
            SafeRegister(static () => RogueLibs.CreateCustomEffect<Frozen_Permanent>().WithName(new CustomNameInfo("Frozen (Permanent)")));
        }

        private static void RegisterLevelGateSwitchTraits()
        {
            SafeRegister(static () => RogueLibs.CreateCustomTrait<Agent_Switch_A>()
                .WithName(new CustomNameInfo(Rck.Tag + " Agent Switch A"))
                .WithDescription(new CustomNameInfo("Campaign branching switch for label 1."))
                .WithUnlock(new TraitUnlock("Agent_Switch_A", true) { IsAvailable = false, IsAvailableInCC = false }));
            SafeRegister(static () => RogueLibs.CreateCustomTrait<Agent_Switch_B>()
                .WithName(new CustomNameInfo(Rck.Tag + " Agent Switch B"))
                .WithDescription(new CustomNameInfo("Campaign branching switch for label 2."))
                .WithUnlock(new TraitUnlock("Agent_Switch_B", true) { IsAvailable = false, IsAvailableInCC = false }));
            SafeRegister(static () => RogueLibs.CreateCustomTrait<Agent_Switch_C>()
                .WithName(new CustomNameInfo(Rck.Tag + " Agent Switch C"))
                .WithDescription(new CustomNameInfo("Campaign branching switch for label 3."))
                .WithUnlock(new TraitUnlock("Agent_Switch_C", true) { IsAvailable = false, IsAvailableInCC = false }));
            SafeRegister(static () => RogueLibs.CreateCustomTrait<Agent_Switch_D>()
                .WithName(new CustomNameInfo(Rck.Tag + " Agent Switch D"))
                .WithDescription(new CustomNameInfo("Campaign branching switch for label 4."))
                .WithUnlock(new TraitUnlock("Agent_Switch_D", true) { IsAvailable = false, IsAvailableInCC = false }));
        }

        private static void SafeRegister(Action register)
        {
            try { register(); }
            catch (Exception ex) { Rck.Log.LogDebug($"Campaign registration skipped: {ex.Message}"); }
        }

        internal static void PopulateGoalList(LevelEditor editor)
        {
            List<string> list = new List<string> { "None" };
            List<string> list2 = new List<string>
            {
                "Idle", "Guard", "Patrol", "Dance", "IceSkate", "Swim", "ListenToJokeNPC", "Joke", "Sit", "Sleep",
                "CuriousObject", "Wander", "WanderInOwnedProperty", "WanderFar"
            };
            foreach (RckGoalInfo goal in RckData.Goals)
            {
                if (!list2.Contains(goal.Goal)) list2.Add(goal.Goal);
            }

            editor.ActivateLoadMenu();
            AccessTools.Field(typeof(LevelEditor), "numButtonsLoad").SetValue(editor, (float)(list.Count + list2.Count));
            editor.OpenObjectLoad(list, list2);
            IEnumerator routine = (IEnumerator)AccessTools.Method(typeof(LevelEditor), "SetScrollBarPlacement").Invoke(editor, null);
            editor.StartCoroutine(routine);
        }
    }

    internal static class LevelGateRuntime
    {
        internal const string DisplayName = Rck.Tag + " Level Gate";
        // RCK's spelling first, then the stored legacy one.
        private static readonly string[] Prefixes = { Rck.LevelGatePrefix, LegacyCcu.LevelGatePrefix };

        /// <summary>Finds a level-gate prefix in a mutator name; returns its index, or -1.</summary>
        internal static int FindPrefix(string mutator, out int prefixLength)
        {
            foreach (string prefix in Prefixes)
            {
                int start = mutator.IndexOf(prefix, StringComparison.Ordinal);
                if (start >= 0)
                {
                    prefixLength = prefix.Length;
                    return start;
                }
            }
            prefixLength = 0;
            return -1;
        }

        internal static bool CanExit(Agent player)
        {
            GameController gc = GameController.gameController;
            if (gc == null) return true;

            foreach (LevelGateRule rule in ActiveRules(gc))
            {
                if (!Evaluate(rule, gc, player, null)) return false;
            }
            return true;
        }

        private static IEnumerable<LevelGateRule> ActiveRules(GameController gc)
        {
            foreach (KeyValuePair<string, LevelGateRule> gate in ActiveGates(gc)) yield return gate.Value;
        }

        /// <summary>Each active gate's data string and parsed rule.</summary>
        private static IEnumerable<KeyValuePair<string, LevelGateRule>> ActiveGates(GameController gc)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in EnumerateGateStrings(gc.challenges, seen))
            {
                if (TryParse(raw, out LevelGateRule? rule) && rule != null) yield return new KeyValuePair<string, LevelGateRule>(raw, rule);
            }
            foreach (string raw in EnumerateGateStrings(gc.originalChallenges, seen))
            {
                if (TryParse(raw, out LevelGateRule? rule) && rule != null) yield return new KeyValuePair<string, LevelGateRule>(raw, rule);
            }
        }

        private const float OpenCheckSeconds = 1f;
        private static readonly HashSet<string> opened = new HashSet<string>(StringComparer.Ordinal);
        private static float nextOpenCheck;

        static LevelGateRuntime() => LevelScope.Ended += ClearOpened;

        private static void ClearOpened() => opened.Clear();

        /// <summary>
        ///   Host, every second: the first time a gate with <c>Open=</c> text holds this level (for any living player,
        ///   as Holding= depends on who's asking), the host's local players see the text once.
        /// </summary>
        internal static void AnnounceOpenings()
        {
            float now = Time.time;
            if (!Interval.Due(ref nextOpenCheck, now, OpenCheckSeconds)) return;
            GameController gc = GameController.gameController;
            if (gc == null || !gc.serverPlayer || !gc.loadComplete || gc.agentList == null) return;
            LevelScope.Check(gc);
            foreach (KeyValuePair<string, LevelGateRule> gate in ActiveGates(gc))
            {
                LevelGateRule rule = gate.Value;
                if (string.IsNullOrEmpty(rule.Open) || opened.Contains(gate.Key) || !HoldsForAnyPlayer(rule, gc)) continue;
                opened.Add(gate.Key);
                int shown = ShowToLocalPlayers(gc, rule.Open!);
                Rck.Log.LogInfo($"Level gate '{gate.Key}': open; showed its text to {shown} player(s).");
            }
        }

        private static bool HoldsForAnyPlayer(LevelGateRule rule, GameController gc)
        {
            bool any = false;
            if (gc.playerAgentList != null)
            {
                foreach (Agent p in gc.playerAgentList)
                {
                    if (p == null || p.dead) continue;
                    any = true;
                    if (Evaluate(rule, gc, p, null)) return true;
                }
            }
            return !any && gc.playerAgent != null && Evaluate(rule, gc, gc.playerAgent, null);
        }

        private static int ShowToLocalPlayers(GameController gc, string text)
        {
            int shown = 0;
            if (gc.playerAgentList == null) return 0;
            foreach (Agent p in gc.playerAgentList)
            {
                if (p == null || !p.localPlayer || p.dead) continue;
                try
                {
                    gc.spawnerMain.SpawnStatusText(p, "BuffSpecial", text);
                    shown++;
                }
                catch (Exception e) { Rck.Log.LogWarning($"Level gate: open text failed: {e.Message}"); }
            }
            return shown;
        }

        /// <summary>Yields the data after the prefix, once per distinct gate whichever prefix spells it.</summary>
        private static IEnumerable<string> EnumerateGateStrings(IEnumerable<string>? mutators, HashSet<string> seen)
        {
            if (mutators == null) yield break;
            foreach (string mutator in mutators)
            {
                if (string.IsNullOrEmpty(mutator)) continue;
                int start = FindPrefix(mutator, out int prefixLength);
                if (start < 0) continue;
                string data = mutator.Substring(start + prefixLength);
                if (seen.Add(data)) yield return data;
            }
        }

        private static bool TryParse(string data, out LevelGateRule? rule)
        {
            if (!LevelGateRules.TryParse(data, out rule) || rule == null) return false;
            if (warned.Add(data))
            {
                foreach (string w in rule.Warnings) Rck.Log.LogWarning($"Level gate '{data}': {w}.");
                foreach (KeyValuePair<string, string> c in rule.Conditions)
                {
                    if (Array.IndexOf(LevelGateRules.BuiltIn, c.Key) < 0 && !RckWorld.HasGateSwitch(c.Key))
                        Rck.Log.LogWarning($"Level gate '{data}': no RCK module answers {c.Key}=, so the exit stays shut.");
                }
            }
            return true;
        }

        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private static float lastRefusalLog = -10f;

        private static bool Evaluate(LevelGateRule rule, GameController gc, Agent player, List<string>? unmet)
        {
            bool open = true;
            if (rule.Labels.Count > 0)
            {
                List<bool> values = new List<bool>();
                foreach (Agent agent in gc.agentList)
                {
                    if (agent == null) continue;
                    foreach (int label in rule.Labels)
                    {
                        if (HasSwitchForLabel(agent, label))
                        {
                            values.Add(IsAgentSwitchTrue(agent));
                            break;
                        }
                    }
                }
                if (!LevelGateRules.Combine(values, rule.Logic, rule.Count))
                {
                    open = false;
                    unmet?.Add(rule.Count > 0 ? $"{values.Count(static v => v)} of {rule.Count} label agents" : $"label agents ({rule.Logic})");
                }
            }
            foreach (KeyValuePair<string, string> c in rule.Conditions)
            {
                if (Condition(c.Key, c.Value, gc, player)) continue;
                open = false;
                unmet?.Add($"{c.Key}={c.Value}");
            }
            return open;
        }

        private static bool Condition(string name, string arg, GameController gc, Agent player)
        {
            switch (name)
            {
                case LevelGateRules.Destroyed:
                    List<string> objects = LevelGateRules.SplitArg(arg);
                    if (objects.Count == 0) return false;
                    foreach (string obj in objects)
                        if (!DestroyedObjects.AllDestroyed(gc, obj)) return false;
                    return true;
                case LevelGateRules.Holding:
                    List<string> items = LevelGateRules.SplitArg(arg);
                    if (items.Count == 0) return false;
                    foreach (string token in items)
                    {
                        if (!LevelGateRules.TryParseItem(token, out string item, out int count) || Held(player, item) < count) return false;
                    }
                    return true;
                default:
                    return RckWorld.TryGateSwitch(name, arg, out bool holds) && holds;
            }
        }

        private static int Held(Agent player, string item)
        {
            if (player == null || player.inventory == null) return 0;
            int n = 0;
            foreach (InvItem inv in player.inventory.InvItemList)
            {
                if (inv != null && string.Equals(inv.invItemName, item, StringComparison.OrdinalIgnoreCase))
                    n += inv.stackable ? inv.invItemCount : 1;
            }
            return n;
        }

        /// <summary>Logs why the exit refused, at most every 3 seconds.</summary>
        internal static void LogRefusal(Agent player)
        {
            GameController gc = GameController.gameController;
            if (gc == null || Time.unscaledTime - lastRefusalLog < 3f) return;
            lastRefusalLog = Time.unscaledTime;
            List<string> unmet = new List<string>();
            foreach (LevelGateRule rule in ActiveRules(gc)) Evaluate(rule, gc, player, unmet);
            Rck.Log.LogInfo($"Level gate: the exit stays shut; not yet: {string.Join(", ", unmet.ToArray())}.");
        }
        private static bool HasSwitchForLabel(Agent agent, int label)
        {
            foreach (string trait in AgentTraits.Get(agent))
            {
                if (MatchesSwitchLabel(trait, label)) return true;
            }
            return false;
        }

        private static bool MatchesSwitchLabel(string trait, int label)
        {
            if (string.Equals(trait, "Agent_Switch_" + label, StringComparison.Ordinal)) return true;
            if (string.Equals(trait, "Switch_" + label, StringComparison.Ordinal)) return true;
            if (string.Equals(trait, "Switch" + label, StringComparison.Ordinal)) return true;
            if (label >= 1 && label <= 4)
            {
                char letter = (char)('A' + label - 1);
                if (string.Equals(trait, "Agent_Switch_" + letter, StringComparison.Ordinal)) return true;
                if (string.Equals(trait, "Switch_" + letter, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsAgentSwitchTrue(Agent agent)
        {
            return agent.dead || agent.zombified || agent.finishedLevel || agent.dismissed ||
                   agent.arrested || agent.hasEmployer || agent.KnockedOut();
        }
    }

    [HarmonyPatch(typeof(BrainUpdate), nameof(BrainUpdate.MyUpdate))]
    internal static class BrainUpdate_MyUpdate_LevelGateOpen_Patch
    {
        private static bool broken;

        private static void Postfix()
        {
            if (broken) return;
            try { LevelGateRuntime.AnnounceOpenings(); }
            catch (Exception e)
            {
                broken = true;
                Rck.Log.LogError($"Level gate: open-text check failed, it stops until restart: {e}");
            }
        }
    }

    public abstract class AgentSwitchTrait : CustomTrait
    {
        public override void OnAdded()
        {
            AgentTraits.Invalidate(SafeOwner());
        }

        public override void OnRemoved()
        {
            AgentTraits.Invalidate(SafeOwner());
        }

        private Agent? SafeOwner()
        {
            try { return Owner; }
            catch { return null; }
        }
    }

    public sealed class Agent_Switch_A : AgentSwitchTrait { }
    public sealed class Agent_Switch_B : AgentSwitchTrait { }
    public sealed class Agent_Switch_C : AgentSwitchTrait { }
    public sealed class Agent_Switch_D : AgentSwitchTrait { }

    /// <summary>
    ///   Scene-setter followers and their leader. The link is only trusted on the level it was made, while the
    ///   follower's agentID (fresh on every Awake/RecycleAwake) and employer still match, so pooled agents drop it.
    /// </summary>
    internal static class FollowerLinks
    {
        private sealed class Link
        {
            internal Agent Leader = null!;
            internal int LeaderId;
            internal int FollowerId;
            internal int Level;
        }

        private static readonly ConditionalWeakTable<Agent, Link> Links = new ConditionalWeakTable<Agent, Link>();
        private static bool any;

        internal static void Add(Agent follower, Agent leader)
        {
            Link link = Links.GetOrCreateValue(follower);
            link.Leader = leader;
            link.LeaderId = leader.agentID;
            link.FollowerId = follower.agentID;
            link.Level = CurrentLevel();
            any = true;
        }

        internal static Agent? LeaderOf(Agent agent)
        {
            if (!Links.TryGetValue(agent, out Link link)) return null;
            Agent leader = link.Leader;
            if (leader == null || link.Level != CurrentLevel() || link.FollowerId != agent.agentID
                || link.LeaderId != leader.agentID || agent.employer != leader) return null;
            return leader;
        }

        /// <summary>Runs after the game (and Social) set up a pair, so the follower relationships win.</summary>
        internal static void Apply(Agent agent, Agent other)
        {
            if (!any || agent == null || other == null || agent == other) return;
            Agent? agentLeader = LeaderOf(agent);
            Agent? otherLeader = LeaderOf(other);
            if (agentLeader == null && otherLeader == null) return;

            if (agentLeader == other)
            {
                agent.relationships.SetRelInitial(other, "Loyal");
                other.relationships.SetRelInitial(agent, "Aligned");
            }
            else if (otherLeader == agent)
            {
                other.relationships.SetRelInitial(agent, "Loyal");
                agent.relationships.SetRelInitial(other, "Aligned");
            }
            else if (agentLeader != null && agentLeader == otherLeader)
            {
                agent.relationships.SetRelInitial(other, "Aligned");
                other.relationships.SetRelInitial(agent, "Aligned");
            }
        }

        private static int CurrentLevel() => LevelScope.CurrentId();
    }

    [ItemCategories(RogueCategories.Technology)]
    public sealed class ClassAWare : CustomItem
    {
        public override void SetupDetails()
        {
            Item.itemType = ItemTypes.Tool;
            Item.stackable = false;
            Item.initCount = 1;
            Item.noCountText = true;
            Item.hierarchy = 8;
        }
    }

    [ItemCategories(RogueCategories.Technology)]
    public sealed class Debugulizer : CustomItem
    {
        public override void SetupDetails()
        {
            Item.itemType = ItemTypes.Tool;
            Item.stackable = false;
            Item.initCount = 1;
            Item.noCountText = true;
            Item.hierarchy = 8;
        }
    }

    [ItemCategories("GunMod")]
    public sealed class RubberBulletsMod : CustomItem
    {
        public override void SetupDetails()
        {
            Item.itemType = ItemTypes.Combine;
            Item.stackable = false;
            Item.initCount = 1;
            Item.noCountText = true;
            Item.hierarchy = 8;
        }
    }

    public sealed class Electrocuted_Permanent : PermanentRelayEffect
    {
        protected override string VanillaEffect => "Electrocuted";
    }

    public sealed class Frozen_Permanent : PermanentRelayEffect
    {
        protected override string VanillaEffect => "Frozen";
    }

    public sealed class Frozen_Fragile : PermanentRelayEffect
    {
        protected override string VanillaEffect => "Frozen";
    }

    [EffectParameters(EffectLimitations.RemoveOnDeath)]
    public abstract class PermanentRelayEffect : CustomEffect
    {
        protected abstract string VanillaEffect { get; }
        public override int GetEffectTime() => 999999;
        public override int GetEffectHate() => 5;
        public override void OnAdded()
        {
            if (!StatusEffects.hasStatusEffect(VanillaEffect))
                StatusEffects.AddStatusEffect(VanillaEffect, showText: false, dontPrevent: true, specificTime: GetEffectTime());
        }

        public override void OnRemoved() { }
        public override void OnUpdated(EffectUpdatedArgs e)
        {
            CurrentTime = GetEffectTime();
        }
    }

    [HarmonyPatch(typeof(Agent), "Start")]
    internal static class Agent_Start_Campaign
    {
        private static void Prefix(Agent __instance)
        {
            CampaignModule.NormalizeCharacter(__instance.customCharacterData);
            CampaignModule.NormalizeMutators(GameController.gameController?.challenges);
            CampaignModule.NormalizeMutators(GameController.gameController?.originalChallenges);
        }

        private static void Postfix(Agent __instance)
        {
            CampaignModule.ApplySceneSetter(__instance);
            CampaignModule.ApplyQuestGiverMarker(__instance);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetDefaultGoal))]
    internal static class Agent_SetDefaultGoal_Campaign
    {
        private static void Postfix(Agent __instance)
        {
            if (GameController.gameController?.loadComplete == true)
                CampaignModule.ApplySceneSetter(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.CreateGoalList))]
    internal static class LevelEditor_CreateGoalList_Campaign
    {
        private static bool Prefix(LevelEditor __instance)
        {
            CampaignModule.PopulateGoalList(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(BasicAgent), nameof(BasicAgent.Spawn))]
    internal static class BasicAgent_Spawn_Campaign
    {
        private static void Prefix(SpawnerBasic spawner, ref string agentName, Chunk startingChunkReal)
        {
            if (startingChunkReal?.customCharacterList == null) return;
            foreach (SaveCharacterData data in startingChunkReal.customCharacterList)
            {
                CampaignModule.NormalizeCharacter(data);
            }
        }
    }

    [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.AddStatusEffect), new[] { typeof(string), typeof(bool), typeof(bool), typeof(int) })]
    internal static class StatusEffects_AddStatusEffect_Campaign
    {
        private static void Prefix(ref string statusEffectName)
        {
            statusEffectName = statusEffectName switch
            {
                "Electrocuted (Permanent)" => "Electrocuted_Permanent",
                "Frozen (Fragile)" => "Frozen_Fragile",
                "Frozen (Permanent)" => "Frozen_Permanent",
                _ => statusEffectName
            };
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.Damage), new[] { typeof(PlayfieldObject), typeof(bool) })]
    internal static class Agent_Damage_FrozenFragile
    {
        private static bool Prefix(Agent __instance, PlayfieldObject damagerObject)
        {
            if (!__instance.dead && __instance.statusEffects.hasStatusEffect("Frozen_Fragile"))
            {
                __instance.deathMethod = "Frozen";
                __instance.statusEffects.ChangeHealth(-9999f, damagerObject);
                __instance.statusEffects.IceGib();
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ExitPoint), nameof(ExitPoint.TryToExit))]
    internal static class ExitPoint_TryToExit_LevelGate
    {
        private static bool Prefix(ExitPoint __instance, Agent myAgent)
        {
            if (myAgent == null || myAgent.isPlayer <= 0 || myAgent.dead) return true;
            if (!__instance.DetermineIfCanExit() || !__instance.DetermineIfCanBodyguardExit(myAgent)) return true;
            if (LevelGateRuntime.CanExit(myAgent)) return true;

            LevelGateRuntime.LogRefusal(myAgent);
            myAgent.SayDialogue("ElevatorWontGoUp");
            return false;
        }
    }

    /// <summary>The mutator list would otherwise show the raw level-gate string as a mutator name.</summary>
    [HarmonyPatch(typeof(Unlocks), nameof(Unlocks.GetChallengeName))]
    internal static class Unlocks_GetChallengeName_LevelGate
    {
        private static void Postfix(string unlockName, ref string __result)
        {
            if (unlockName != null && LevelGateRuntime.FindPrefix(unlockName, out _) >= 0)
                __result = LevelGateRuntime.DisplayName;
        }
    }

    /// <summary>
    ///   A new agent's relationship list is only filled a frame after it spawns, so follower relationships are set
    ///   here, after the game's and Social's rules for the pair.
    /// </summary>
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.SetupRelationshipOriginal), typeof(Agent))]
    internal static class Relationships_SetupRelationshipOriginal_Followers
    {
        // Relationships.agent is private and Mono enforces field access, so it comes in through Harmony's ___agent.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Agent ___agent, Agent otherAgent)
        {
            try { FollowerLinks.Apply(___agent, otherAgent); }
            catch (Exception e)
            {
                if (logged) return;
                logged = true;
                Rck.Log.LogWarning($"Could not set follower relationships: {e}");
            }
        }

        private static bool logged;
    }
}
