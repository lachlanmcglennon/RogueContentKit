using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RCK.Loadout
{
    public sealed class LoadoutModule : IRckModule
    {
        public string Name => "Loadout";

        public void Initialize()
        {
        }
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.FillAgent))]
    internal static class InvDatabase_FillAgent_LoadoutPatch
    {
        private static readonly ConditionalWeakTable<Agent, AppliedMarker> Applied = new ConditionalWeakTable<Agent, AppliedMarker>();
        private static readonly HashSet<int> LoggedFailures = new HashSet<int>();

        private static void Postfix(InvDatabase __instance)
        {
            Agent agent = __instance.agent;
            if (agent == null || agent.isPlayer != 0)
            {
                return;
            }
            // Pooled agents are recycled across levels with fresh agentID/UID, so the marker is stamped
            // rather than a plain presence flag: a recycled NPC re-rolls, a repeat FillAgent does not.
            AppliedMarker marker = Applied.GetOrCreateValue(agent);
            if (marker.Matches(agent))
            {
                return;
            }

            try
            {
                if (LoadoutRuntime.Apply(__instance, agent))
                {
                    marker.Stamp(agent);
                }
            }
            catch (Exception ex)
            {
                if (LoggedFailures.Add(agent.agentID))
                {
                    Rck.Log.LogError($"Loadout generation failed for {agent}: {ex}");
                }
            }
        }

        private sealed class AppliedMarker
        {
            private bool set;
            private int level;
            private int agentId;
            private int uid;

            public bool Matches(Agent agent)
            {
                return set && level == CurrentLevel() && agentId == agent.agentID && uid == agent.UID;
            }

            public void Stamp(Agent agent)
            {
                set = true;
                level = CurrentLevel();
                agentId = agent.agentID;
                uid = agent.UID;
            }

            private static int CurrentLevel()
            {
                GameController gc = GameController.gameController;
                return gc != null && gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 0;
            }
        }
    }

    internal static class LoadoutRuntime
    {
        private static readonly string[] LoaderTraits = { "Flat_Distribution", "Scaled_Distribution", "Upscaled_Distribution" };
        private static readonly string[] MoneyTraits = { "Broke", "Desperate", "Poor", "Prosperous", "Rich", "Wealthy", "Zillionaire" };

        public static bool Apply(InvDatabase inventory, Agent agent)
        {
            IReadOnlyCollection<string> allTraits = AgentTraits.Get(agent);
            if (allTraits.Count == 0)
            {
                return false;
            }

            var names = (HashSet<string>)allTraits;
            bool hasLoadoutTrait = false;
            foreach (RckTraitInfo info in AgentTraits.InFolder(agent, "Loadout"))
            {
                if (info.InHead)
                {
                    hasLoadoutTrait = true;
                    break;
                }
            }
            if (!hasLoadoutTrait)
            {
                return false;
            }

            var rng = new StableRng(agent, "Loadout");
            ApplyChunkItems(inventory, agent, names);
            ApplyMoney(inventory, names, ref rng);

            string loader = ChooseLoader(names, ref rng);
            // Pooled agents can keep a stale customCharacterData; the game only reads it for "Custom" agents.
            if (loader != "" && agent.agentName == "Custom" && agent.customCharacterData != null
                && agent.customCharacterData.items.Count > 0)
            {
                RemoveItemsPageGrants(inventory, agent, agent.customCharacterData.items);
                GenerateFromCustomItems(inventory, agent, agent.customCharacterData.items, names, loader, ref rng);
            }
            return true;
        }

        // Vanilla SetupAgentStats feeds the Items page through AddItemPlayerStart, which only grants to
        // players and the character-select dummy, so NPCs normally arrive here without those items and
        // this is a no-op. If anything did grant them (they carry startingItem), the loader owns the pool:
        // drop the grants so the roll is a subset of the page rather than an addition to it.
        private static void RemoveItemsPageGrants(InvDatabase inventory, Agent agent, List<string> itemNames)
        {
            var pool = new HashSet<string>(itemNames);
            var grants = new List<InvItem>();
            foreach (InvItem item in inventory.InvItemList)
            {
                if (item != null && item.startingItem && item.invItemName != null && pool.Contains(item.invItemName))
                {
                    grants.Add(item);
                }
            }
            foreach (InvItem item in grants)
            {
                inventory.DestroyItem(item);
            }
            if (grants.Count > 0)
            {
                Rck.Log.LogDebug($"Loadout: removed {grants.Count} Items-page grant(s) from {agent} before rolling its pool.");
            }
        }

        private static string ChooseLoader(HashSet<string> traits, ref StableRng rng)
        {
            var loaders = new List<string>();
            foreach (string loader in LoaderTraits)
            {
                if (traits.Contains(loader))
                {
                    loaders.Add(loader);
                }
            }
            return loaders.Count == 0 ? "" : rng.Pick(loaders);
        }

        private static void GenerateFromCustomItems(InvDatabase inventory, Agent agent, List<string> itemNames,
            HashSet<string> traits, string loader, ref StableRng rng)
        {
            var bySlot = new Dictionary<LoadoutSlot, List<ItemCandidate>>
            {
                { LoadoutSlot.Headgear, new List<ItemCandidate>() },
                { LoadoutSlot.BodyArmor, new List<ItemCandidate>() },
                { LoadoutSlot.RangedWeapon, new List<ItemCandidate>() },
                { LoadoutSlot.MeleeWeapon, new List<ItemCandidate>() },
                { LoadoutSlot.ThrownWeapon, new List<ItemCandidate>() },
                { LoadoutSlot.Pocket, new List<ItemCandidate>() },
            };

            foreach (string itemName in itemNames)
            {
                ItemCandidate? candidate = CreateCandidate(agent, itemName);
                if (candidate != null)
                {
                    bySlot[candidate.Slot].Add(candidate);
                }
            }

            int equipmentLimit = EquipmentLimit(traits);
            int pocketLimit = PocketLimit(traits);
            foreach (KeyValuePair<LoadoutSlot, List<ItemCandidate>> pair in bySlot)
            {
                int limit = pair.Key == LoadoutSlot.Pocket ? pocketLimit : equipmentLimit;
                if (limit <= 0 || pair.Value.Count == 0)
                {
                    continue;
                }

                List<ItemCandidate> chosen = loader == "Flat_Distribution"
                    ? RollFlat(pair.Value, pair.Key, limit, traits, ref rng)
                    : RollScaled(pair.Value, pair.Key, limit, traits, loader == "Upscaled_Distribution", ref rng);
                foreach (ItemCandidate item in chosen)
                {
                    AddLoadoutItem(inventory, item.Name);
                }
            }
        }

        private static ItemCandidate? CreateCandidate(Agent agent, string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || itemName == "Empty" || itemName == "None")
            {
                return null;
            }
            var item = new InvItem
            {
                invItemName = itemName,
                agent = agent,
                itemNetID = -1,
            };
            item.ItemSetup(notNew: false);
            // An NPC can't carry a Nugget: AddItem("Nugget") would add one to the player's Nugget count every spawn.
            if (item.itemType == "Nugget")
            {
                return null;
            }
            LoadoutSlot slot;
            if (item.isArmorHead)
            {
                slot = LoadoutSlot.Headgear;
            }
            else if (item.isArmor)
            {
                slot = LoadoutSlot.BodyArmor;
            }
            else if (item.itemType == "WeaponProjectile")
            {
                slot = LoadoutSlot.RangedWeapon;
            }
            else if (item.itemType == "WeaponMelee")
            {
                slot = LoadoutSlot.MeleeWeapon;
            }
            else if (item.itemType == "WeaponThrown")
            {
                slot = LoadoutSlot.ThrownWeapon;
            }
            else
            {
                slot = LoadoutSlot.Pocket;
            }
            return new ItemCandidate(itemName, slot, Math.Max(1, item.itemValue));
        }

        private static List<ItemCandidate> RollFlat(List<ItemCandidate> pool, LoadoutSlot slot, int limit,
            HashSet<string> traits, ref StableRng rng)
        {
            var result = new List<ItemCandidate>();
            if (SlotSkipped(slot, traits, ref rng))
            {
                return result;
            }

            bool forceItem = ForceItem(slot, traits);
            var remaining = new List<ItemCandidate>(pool);
            int max = Math.Min(limit, remaining.Count);
            for (int i = 0; i < max; i++)
            {
                int choices = remaining.Count + (forceItem ? 0 : 1);
                int pick = rng.Next(choices);
                if (pick >= remaining.Count)
                {
                    break;
                }
                result.Add(remaining[pick]);
                remaining.RemoveAt(pick);
            }
            return result;
        }

        private static List<ItemCandidate> RollScaled(List<ItemCandidate> pool, LoadoutSlot slot, int limit,
            HashSet<string> traits, bool upscale, ref StableRng rng)
        {
            var result = new List<ItemCandidate>();
            if (SlotSkipped(slot, traits, ref rng))
            {
                return result;
            }

            int min = int.MaxValue;
            int max = 1;
            foreach (ItemCandidate item in pool)
            {
                min = Math.Min(min, item.Value);
                max = Math.Max(max, item.Value);
            }

            foreach (ItemCandidate item in pool)
            {
                int chance = upscale
                    ? Math.Max(1, Math.Min(100, item.Value * 100 / max))
                    : Math.Max(1, Math.Min(100, min * 100 / item.Value));
                if (rng.Chance(chance))
                {
                    result.Add(item);
                }
            }

            if (result.Count == 0 && ForceItem(slot, traits))
            {
                result.Add(PickWeighted(pool, upscale, min, max, ref rng));
            }

            Shuffle(result, ref rng);
            if (result.Count > limit)
            {
                result.RemoveRange(limit, result.Count - limit);
            }
            return result;
        }

        private static ItemCandidate PickWeighted(List<ItemCandidate> pool, bool upscale, int min, int max, ref StableRng rng)
        {
            int total = 0;
            foreach (ItemCandidate item in pool)
            {
                total += upscale ? item.Value : Math.Max(1, max + min - item.Value);
            }
            int pick = rng.Next(total);
            foreach (ItemCandidate item in pool)
            {
                pick -= upscale ? item.Value : Math.Max(1, max + min - item.Value);
                if (pick < 0)
                {
                    return item;
                }
            }
            return pool[0];
        }

        private static void Shuffle(List<ItemCandidate> items, ref StableRng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                ItemCandidate temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
        }

        private static bool SlotSkipped(LoadoutSlot slot, HashSet<string> traits, ref StableRng rng)
        {
            if (slot == LoadoutSlot.Pocket)
            {
                if (traits.Contains("Have")) return false;
                if (traits.Contains("Have_Not")) return rng.Chance(50);
                if (traits.Contains("Have_Mostly")) return rng.Chance(25);
                return false;
            }

            if (traits.Contains("Equipment_Chad")) return false;
            if (traits.Contains("Equipment_Virgin")) return rng.Chance(50);
            if (traits.Contains("Equipment_Enjoyer")) return rng.Chance(25);
            return false;
        }

        private static bool ForceItem(LoadoutSlot slot, HashSet<string> traits)
        {
            return slot == LoadoutSlot.Pocket ? traits.Contains("Have") : traits.Contains("Equipment_Chad");
        }

        private static int EquipmentLimit(HashSet<string> traits)
        {
            if (traits.Contains("Sidearmed_to_the_Teeth")) return 99;
            if (traits.Contains("Sidearmed_but_on_Both_Sides")) return 3;
            if (traits.Contains("Sidearmed")) return 2;
            return 1;
        }

        private static int PocketLimit(HashSet<string> traits)
        {
            if (traits.Contains("FunnyPack_Pro")) return 100;
            if (traits.Contains("FunnyPack_Extreme")) return 3;
            if (traits.Contains("FunnyPack")) return 2;
            return 1;
        }

        private static void AddLoadoutItem(InvDatabase inventory, string itemName)
        {
            InvItem item = inventory.AddItem(itemName, 1);
            if (item == null)
            {
                return;
            }
            if (item.invItemName == "Money")
            {
                item.invItemCount = 1;
            }
            else if (item.rewardCount > 0)
            {
                item.invItemCount = item.rewardCount;
            }
            else if (item.itemType == "WeaponProjectile" && item.initCountAI > 0)
            {
                item.invItemCount = item.initCountAI;
            }
            else
            {
                item.invItemCount = Math.Max(1, item.initCount);
            }
        }

        private static void ApplyMoney(InvDatabase inventory, HashSet<string> traits, ref StableRng rng)
        {
            bool hasAmountTrait = false;
            foreach (string trait in MoneyTraits)
            {
                if (traits.Contains(trait))
                {
                    hasAmountTrait = true;
                    break;
                }
            }

            int bankruptChance = 0;
            if (traits.Contains("Bankrupt_75")) bankruptChance = 75;
            else if (traits.Contains("Bankrupt_50")) bankruptChance = 50;
            else if (traits.Contains("Bankrupt_25")) bankruptChance = 25;

            bool bankrupt = bankruptChance > 0 && rng.Chance(bankruptChance);
            if (bankrupt || hasAmountTrait)
            {
                RemoveMoney(inventory);
            }
            if (bankrupt || !hasAmountTrait)
            {
                return;
            }

            var available = new List<string>();
            foreach (string trait in MoneyTraits)
            {
                if (traits.Contains(trait))
                {
                    available.Add(trait);
                }
            }
            string chosen = rng.Pick(available);
            int amount = chosen switch
            {
                "Broke" => rng.RangeInclusive(1, 6),
                "Desperate" => rng.RangeInclusive(6, 11),
                "Poor" => rng.RangeInclusive(11, 26),
                "Prosperous" => rng.RangeInclusive(26, 41),
                "Rich" => rng.RangeInclusive(41, 61),
                "Wealthy" => rng.RangeInclusive(81, 100),
                "Zillionaire" => 1000,
                _ => 0,
            };
            if (amount > 0)
            {
                InvItem money = inventory.AddItem("Money", amount);
                if (money != null)
                {
                    money.invItemCount = amount;
                }
            }
        }

        private static void RemoveMoney(InvDatabase inventory)
        {
            foreach (InvItem item in inventory.InvItemList)
            {
                if (item != null && item.invItemName == "Money")
                {
                    item.RevertInvItem();
                }
            }
        }

        private static void ApplyChunkItems(InvDatabase inventory, Agent agent, HashSet<string> traits)
        {
            if (traits.Contains("Chunk_Key"))
            {
                AddChunkKey(inventory, agent);
            }
            if (traits.Contains("Chunk_Safe_Combo"))
            {
                AddChunkSafeCombo(inventory, agent);
            }
            if (traits.Contains("Chunk_Mayor_Badge") && !inventory.HasItem("MayorBadge"))
            {
                inventory.AddItem("MayorBadge", 1);
                agent.oma.hasMayorBadge = true;
            }
        }

        private static void AddChunkKey(InvDatabase inventory, Agent agent)
        {
            if (inventory.HasItem("Key"))
            {
                agent.oma.hasKey = true;
                return;
            }

            Door? target = null;
            foreach (ObjectReal obj in agent.gc.objectRealList)
            {
                if (obj is Door door && door.locked && SameScope(agent, obj))
                {
                    target = door;
                    break;
                }
            }

            InvItem key = inventory.AddItem("Key", 1);
            if (key == null)
            {
                return;
            }
            int chunk = target != null ? target.startingChunk : agent.startingChunk;
            int sector = target != null ? target.startingSector : agent.startingSector;
            string description = target != null ? target.startingChunkRealDescription : agent.startingChunkRealDescription;
            key.specificChunk = chunk;
            key.specificSector = sector;
            key.chunks.Add(chunk);
            key.sectors.Add(sector);
            if (!string.IsNullOrEmpty(description))
            {
                key.contents.Add(description == "Generic" ? "GuardPost" : description);
            }
            if (target != null && target.distributedKey == null)
            {
                target.distributedKey = agent;
            }
            agent.oma.hasKey = true;
        }

        private static void AddChunkSafeCombo(InvDatabase inventory, Agent agent)
        {
            if (inventory.HasItem("SafeCombination"))
            {
                agent.oma.hasSafeCombination = true;
                return;
            }

            Safe? target = null;
            foreach (ObjectReal obj in agent.gc.objectRealList)
            {
                if (obj is Safe safe && SameScope(agent, obj))
                {
                    target = safe;
                    break;
                }
            }

            InvItem combo = inventory.AddItem("SafeCombination", 1);
            if (combo == null)
            {
                return;
            }
            int chunk = target != null ? target.startingChunk : agent.startingChunk;
            int sector = target != null ? target.startingSector : agent.startingSector;
            string description = target != null ? target.startingChunkRealDescription : agent.startingChunkRealDescription;
            combo.specificChunk = chunk;
            combo.specificSector = sector;
            combo.chunks.Add(chunk);
            combo.sectors.Add(sector);
            if (!string.IsNullOrEmpty(description))
            {
                combo.contents.Add(description);
            }
            if (target != null && target.distributedKey == null)
            {
                target.distributedKey = agent;
            }
            agent.oma.hasSafeCombination = true;
        }

        private static bool SameScope(Agent agent, ObjectReal obj)
        {
            return agent.gc.levelShape == 2
                ? obj.startingSector == agent.startingSector
                : obj.startingChunk == agent.startingChunk;
        }

        private enum LoadoutSlot
        {
            Headgear,
            BodyArmor,
            RangedWeapon,
            MeleeWeapon,
            ThrownWeapon,
            Pocket,
        }

        private sealed class ItemCandidate
        {
            public ItemCandidate(string name, LoadoutSlot slot, int value)
            {
                Name = name;
                Slot = slot;
                Value = value;
            }

            public string Name { get; }
            public LoadoutSlot Slot { get; }
            public int Value { get; }
        }
    }

    internal struct StableRng
    {
        private uint state;

        public StableRng(Agent agent, string salt)
        {
            state = 2166136261u;
            Add(salt);
            Add(agent.gc?.loadLevel?.randomSeedNum ?? 0);
            Add(agent.gc?.sessionDataBig?.curLevelEndless ?? 0);
            Add(agent.agentID);
            Add(agent.streamingChunkObjectID);
            Add(agent.startingChunk);
            Add(agent.startingSector);
            Add((int)Math.Round(agent.originalPosReal.x * 100f));
            Add((int)Math.Round(agent.originalPosReal.y * 100f));
            Add(agent.agentName);
            Add(agent.agentRealName);
            IReadOnlyCollection<string> traitNames = AgentTraits.Get(agent);
            var sorted = new List<string>(traitNames);
            sorted.Sort(StringComparer.Ordinal);
            foreach (string trait in sorted)
            {
                Add(trait);
            }
        }

        public string Pick(List<string> values) => values[Next(values.Count)];

        public bool Chance(int percent) => percent >= 100 || (percent > 0 && Next(100) < percent);

        public int RangeInclusive(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }
            return min + Next(max - min + 1);
        }

        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 1)
            {
                return 0;
            }
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)exclusiveMax);
        }

        private void Add(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Add(0);
                return;
            }
            for (int i = 0; i < text!.Length; i++)
            {
                state ^= text[i];
                state *= 16777619u;
            }
        }

        private void Add(int value)
        {
            unchecked
            {
                state ^= (uint)value;
                state *= 16777619u;
            }
        }
    }
}
