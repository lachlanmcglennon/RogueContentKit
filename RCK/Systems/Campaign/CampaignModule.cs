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
            int level = gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 0;
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
                if (!Evaluate(rule, gc)) return false;
            }
            return true;
        }

        private static IEnumerable<LevelGateRule> ActiveRules(GameController gc)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in EnumerateGateStrings(gc.challenges, seen))
            {
                if (TryParse(raw, out LevelGateRule? rule) && rule != null) yield return rule;
            }
            foreach (string raw in EnumerateGateStrings(gc.originalChallenges, seen))
            {
                if (TryParse(raw, out LevelGateRule? rule) && rule != null) yield return rule;
            }
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
            rule = null;
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();
                if (key.Length > 0) fields[key] = value;
            }

            string type = fields.TryGetValue("Type", out string typeValue) ? typeValue : "Entry";
            if (!string.Equals(type, "Entry", StringComparison.OrdinalIgnoreCase)) return false;

            HashSet<int> labels = new HashSet<int>();
            if (fields.TryGetValue("Label", out string labelValue) || fields.TryGetValue("Labels", out labelValue))
            {
                foreach (string token in SplitList(labelValue))
                {
                    if (TryParseLabel(token, out int label)) labels.Add(label);
                }
            }
            if (labels.Count == 0) return false;

            HashSet<string> switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fields.TryGetValue("Switch", out string switchValue) || fields.TryGetValue("Switches", out switchValue))
            {
                foreach (string token in SplitList(switchValue)) switches.Add(token);
            }
            if (switches.Count > 0 && !switches.Contains("Agent")) return false;

            string logic = fields.TryGetValue("Logic", out string logicValue) ? logicValue : "AND";
            rule = new LevelGateRule(labels, ParseLogic(logic));
            return true;
        }

        private static IEnumerable<string> SplitList(string value)
        {
            foreach (string token in value.Split(new[] { ',', '|', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static bool TryParseLabel(string token, out int label)
        {
            if (int.TryParse(token, out label)) return true;
            if (token.Length == 1)
            {
                switch (char.ToUpperInvariant(token[0]))
                {
                    case 'A': label = 1; return true;
                    case 'B': label = 2; return true;
                    case 'C': label = 3; return true;
                    case 'D': label = 4; return true;
                }
            }
            label = 0;
            return false;
        }

        private static GateLogic ParseLogic(string logic)
        {
            return logic.ToUpperInvariant() switch
            {
                "NAND" => GateLogic.Nand,
                "NOR" => GateLogic.Nor,
                "OR" => GateLogic.Or,
                "XNOR" => GateLogic.Xnor,
                "XOR" => GateLogic.Xor,
                _ => GateLogic.And
            };
        }

        private static bool Evaluate(LevelGateRule rule, GameController gc)
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
            if (values.Count == 0) return false;

            bool any = values.Any(static v => v);
            bool all = values.All(static v => v);
            int trueCount = values.Count(static v => v);
            return rule.Logic switch
            {
                GateLogic.Nand => !all,
                GateLogic.Nor => !any,
                GateLogic.Or => any,
                GateLogic.Xnor => trueCount != 1,
                GateLogic.Xor => trueCount == 1,
                _ => all
            };
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

    internal sealed class LevelGateRule
    {
        public LevelGateRule(HashSet<int> labels, GateLogic logic)
        {
            Labels = labels;
            Logic = logic;
        }

        public HashSet<int> Labels { get; }
        public GateLogic Logic { get; }
    }

    internal enum GateLogic
    {
        And,
        Nand,
        Nor,
        Or,
        Xnor,
        Xor
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

        private static int CurrentLevel()
        {
            GameController gc = GameController.gameController;
            return gc != null && gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 0;
        }
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
