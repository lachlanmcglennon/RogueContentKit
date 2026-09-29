using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RCK.Merchants
{
    public sealed class MerchantModule : IRckModule
    {
        public string Name => "Merchants";

        public void Initialize()
        {
            RogueInteractions.CreateProvider<Agent>(static h =>
            {
                Agent vendor = h.Object;
                if (!MerchantInventory.HasMerchantType(vendor) || vendor.isPlayer > 0 || vendor.dead || vendor.zombified)
                    return;

                MerchantInventory.EnsureInventory(vendor);
                if (!h.HasButton("Buy"))
                    h.AddButton(VanillaButtons.Buy, static m => m.Object.agentInteractions.PressedButton(m.Object, m.Agent, VanillaButtons.Buy, 0));
                if (h.Agent.inventory != null && h.Agent.inventory.HasItem("FreeItemVoucher") && !h.HasButton("UseVoucher"))
                    h.AddButton(VanillaButtons.UseVoucher, static m => m.Object.agentInteractions.PressedButton(m.Object, m.Agent, VanillaButtons.UseVoucher, 0));
            });
        }
    }

    internal static class MerchantInventory
    {
        private static readonly string[] MerchantTypeIds = RckData.TraitsIn("Custom Merchants/Merchant Type")
            .Select(static t => t.Id).Concat(new[] { "Cyber_Intruder", "Muscle" }).Distinct().ToArray();

        private static readonly Dictionary<string, string[]> Pools = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["_Test_Inventory"] = new[] { "Banana", "Beer", "FirstAidKit", "Pistol", "Sword", "Key", "Translator", "RemoteBomb", "LaserGun", "BloodBag" },
            ["Anthropophagie"] = new[] { "Beer", "Whiskey", "Cocktail", "Axe", "BearTrap", "Cannibalize", "Knife" },
            ["Armorer"] = new[] { "BulletproofVest", "FireproofSuit", "GasMask", "HardHat", "SoldierHelmet", "ArmorDurabilityDoubler", "MeleeDurabilityDoubler" },
            ["Army_Quartermaster"] = new[] { "Pistol", "MachineGun", "Shotgun", "Grenade", "GrenadeEMP", "BulletproofVest", "SoldierHelmet", "FirstAidKit", "WalkieTalkie" },
            ["Assassineer"] = new[] { "Knife", "Sword", "Pistol", "Silencer", "ChloroformHankie", "TranquilizerGun", "Shuriken", "CardboardBox", "CyanidePill" },
            ["Banana_Boutique"] = new[] { "Banana", "Banana", "BananaPeel", "BananaPeel" },
            ["Barbarian_Merchant"] = new[] { "Beer", "Whiskey", "HamSandwich", "BaconCheeseburger", "Axe", "Sword", "Sledgehammer" },
            ["Bartender_Dive"] = new[] { "Beer", "Whiskey", "Cocktail", "Cigarettes", "Knife", "BaseballBat" },
            ["Bartender_Fancy"] = new[] { "Cocktail", "Whiskey", "Cologne", "MoodRing", "Cigarettes", "BaconCheeseburger" },
            ["Bartender_Vanilla"] = new[] { "Beer", "Whiskey", "Cocktail", "Cigarettes", "BaconCheeseburger" },
            ["Blacksmith"] = new[] { "Axe", "BaseballBat", "Knife", "Sledgehammer", "Sword", "FlamingSword", "Blowtorch", "MeleeDurabilityDoubler" },
            ["Bloodsuckers_Bazaar"] = new[] { "BloodBag", "BloodTransfusionKit", "Knife", "ChloroformHankie", "VoodooDoll", "InvisibleLimitedItem", "Antidote" },
            ["Burger_Joint"] = new[] { "Fud", "HotFud", "BaconCheeseburger", "HamSandwich", "Banana", "FoodProcessor" },
            ["Consumer_Electronics"] = new[] { "Laptop", "RemoteControl", "HackingTool", "Boombox", "MiniFridge", "WalkieTalkie", "MemoryEraser" },
            ["Convenience_Store"] = new[] { "Beer", "Whiskey", "Cigarettes", "CigaretteLighter", "Banana", "HamSandwich", "Fud", "Newspaper" },
            ["Cop_Contraband"] = new[] { "Handcuffs", "Pistol", "PoliceBaton", "Cocaine", "Syringe", "Knife", "Cigarettes", "MayorEvidence" },
            ["Cop_Patrol"] = new[] { "PoliceBaton", "Pistol", "Handcuffs", "BulletproofVest", "Taser", "KeyCard" },
            ["Cop_SWAT"] = new[] { "MachineGun", "Shotgun", "GrenadeKnocker", "GrenadeEMP", "BulletproofVest", "SoldierHelmet", "DoorDetonator" },
            ["Cyber_Intruder"] = new[] { "HackingTool", "Laptop", "RemoteControl", "WallBypasser", "DoorDetonator", "SafeCrackingTool", "GrenadeEMP", "Silencer" },
            ["Demolition_Depot"] = new[] { "Grenade", "GrenadeEMP", "GrenadeDizzy", "LandMine", "MolotovCocktail", "RemoteBomb", "TimeBomb", "DoorDetonator", "BigBomb" },
            ["Drug_Dealer"] = new[] { "Cocaine", "Steroids", "Syringe", "RagePoison", "ElectroPill", "Giantizer", "Antidote", "Cigarettes" },
            ["Fire_Sale"] = new[] { "Matches", "CigaretteLighter", "MolotovCocktail", "Flamethrower", "FireproofSuit", "FireExtinguisher", "Blowtorch" },
            ["Firefighter_Five_and_Dime"] = new[] { "FireExtinguisher", "FireproofSuit", "HardHat", "FirstAidKit", "WaterCannon", "Axe", "Wrench" },
            ["General_Store"] = new[] { "FirstAidKit", "HamSandwich", "Beer", "Cigarettes", "Lockpick", "Wrench", "Crowbar", "Shovel", "Map", "FreeItemVoucher" },
            ["Gun_Dealer"] = new[] { "Pistol", "Revolver", "MachineGun", "Shotgun", "SniperRifle", "AmmoCapacityMod", "RateOfFireMod" },
            ["Gun_Dealer_Heavy"] = new[] { "MachineGun", "Shotgun", "SniperRifle", "RocketLauncher", "Flamethrower", "BFG", "Grenade" },
            ["Gunsmith"] = new[] { "Pistol", "Revolver", "MachineGun", "Shotgun", "SniperRifle", "AccuracyMod", "Silencer", "AmmoCapacityMod", "RateOfFireMod", "RubberBulletsMod" },
            ["Hardware_Store"] = new[] { "Crowbar", "Wrench", "Shovel", "Saw", "PowerDrill", "Jackhammer", "Blowtorch", "HardHat", "DoorDetonator" },
            ["Home_Fortress_Outlet"] = new[] { "BearTrap", "LandMine", "TripMine", "StickyMine", "LaserBlazer", "ForceField", "HearingBlocker" },
            ["Hypnotist"] = new[] { "Hypnotizer", "Hypnotizer2", "Haterator", "FriendPhone", "MindControl", "Cologne", "MoodRing" },
            ["Insider_Key"] = new[] { "Key", "KeyCard", "SkeletonKey", "Lockpick" },
            ["Insider_Safe_Combo"] = new[] { "SafeCombination", "SafeCrackingTool", "SafeBuster" },
            ["Intruders_Outlet"] = new[] { "Lockpick", "SafeCrackingTool", "WindowCutter", "Crowbar", "CardboardBox", "WallBypasser", "GrapplingHook", "StealingGlove" },
            ["Junk_Dealer"] = new[] { "Rock", "Rag", "Newspaper", "Tooth", "CircuitBoard", "HardDrive", "FreeItemVoucher", "OilContainer", "Wrench" },
            ["McFuds"] = new[] { "Fud", "HotFud", "BaconCheeseburger", "HamSandwich", "Beer", "FoodProcessor" },
            ["Medical_Supplier"] = new[] { "FirstAidKit", "Syringe", "Antidote", "BloodBag", "BloodTransfusionKit", "Knife", "TranquilizerGun" },
            ["Mining_Gear"] = new[] { "HardHat", "Sledgehammer", "Shovel", "Jackhammer", "Crowbar", "LandMine", "RemoteBomb", "Wrench", "FireExtinguisher" },
            ["Monke_Mart"] = new[] { "Banana", "BananaPeel", "Rock", "MonkeyBarrel", "Translator", "Fud" },
            ["Movie_Theater"] = new[] { "Boombox", "Fud", "HotFud", "BaconCheeseburger", "Beer", "Whiskey", "Newspaper" },
            ["Muscle"] = new[] { "BaseballBat", "Sword", "Axe", "Sledgehammer", "BraceletStrength", "Steroids", "Beer", "BaconCheeseburger" },
            ["Occultist"] = new[] { "VoodooDoll", "Necronomicon", "GhostBlaster", "ResurrectionShampoo", "BooUrn", "ZombieReanimator", "WerewolfTransform" },
            ["Outdoor_Outfitter"] = new[] { "Axe", "Shovel", "BearTrap", "GrapplingHook", "Rope", "WaterPistol", "FirstAidKit", "HamSandwich" },
            ["Pacifist_Provisioner"] = new[] { "ChloroformHankie", "TranquilizerGun", "RubberBulletsMod", "CardboardBox", "Hypnotizer", "MemoryEraser", "Depossessor" },
            ["Pawn_Shop"] = new[] { "Pistol", "Knife", "Laptop", "BraceletStrength", "MoodRing", "Cologne", "VoodooDoll", "SafeCrackingTool", "FreeItemVoucher" },
            ["Pest_Control"] = new[] { "BearTrap", "KillerThrower", "FireExtinguisher", "Flamethrower", "RagePoison", "Axe", "Shotgun", "Antidote" },
            ["Pharmacy"] = new[] { "FirstAidKit", "Syringe", "Antidote", "Steroids", "ElectroPill", "Giantizer", "BloodBag", "TranquilizerGun" },
            ["Research_Materials"] = new[] { "ResearchGun", "ShrinkRay", "FreezeRay", "GhostBlaster", "LaserGun", "Syringe", "CircuitBoard", "HardDrive" },
            ["Resistance_Commissary"] = new[] { "QuickEscapeTeleporter", "FreeItemVoucher", "HiringVoucher", "WalkieTalkie", "FirstAidKit", "Pistol", "Lockpick" },
            ["Riot_Inc"] = new[] { "MolotovCocktail", "Grenade", "Rock", "BaseballBat", "CigaretteLighter", "Fireworks", "Haterator" },
            ["Slave_Shop"] = new[] { "SlaveHelmet", "SlaveHelmetRemote", "SlaveHelmetRemover", "Enslave", "Handcuffs", "HiringVoucher" },
            ["Slaves_Shop"] = new[] { "SlaveHelmet", "SlaveHelmetRemote", "SlaveHelmetRemover", "Enslave", "Handcuffs", "HiringVoucher" },
            ["Sporting_Goods"] = new[] { "BaseballBat", "BalletShoes", "GrapplingHook", "KillerThrower", "Shuriken", "WaterPistol", "Lunge", "Stomp" },
            ["Sugar_Shack"] = new[] { "Fud", "HotFud", "BaconCheeseburger", "Banana", "Cocktail", "Cigarettes" },
            ["Tech_Mart"] = new[] { "HackingTool", "Laptop", "RemoteControl", "LaserGun", "WallBypasser", "VisionDetector", "XRaySpecs", "CircuitBoard" },
            ["Teleportationist"] = new[] { "Teleporter", "ItemTeleporter", "QuickEscapeTeleporter", "WarpZoner", "GrenadeWarp", "WallBypasser" },
            ["Thief_Master"] = new[] { "SkeletonKey", "SafeBuster", "SafeCrackingTool", "WindowCutter", "StealingGlove", "WallBypasser", "GrapplingHook" },
            ["Throwcery_Store"] = new[] { "Rock", "Grenade", "GrenadeEMP", "GrenadeKnocker", "GrenadeDizzy", "MolotovCocktail", "Shuriken", "KillerThrower" },
            ["Toy_Store"] = new[] { "WaterPistol", "Boombox", "HologramItem", "Joke", "CardboardBox", "Fireworks", "MonkeyBarrel" },
            ["Upper_Cruster"] = new[] { "Cologne", "MoodRing", "BraceletStrength", "MayorHat", "TopHat", "Cocktail", "Cigarettes", "BooUrn" },
            ["Villains_Vault"] = new[] { "RocketLauncher", "BFG", "BigBomb", "MindControl", "RemoteBomb", "CyanidePill", "ExplosiveStimulator" },
            ["Shopkeeper"] = new[] { "FirstAidKit", "HamSandwich", "Beer", "Cigarettes", "Lockpick", "Wrench", "Crowbar", "Shovel", "Map" },
            ["Soldier"] = new[] { "Pistol", "MachineGun", "Grenade", "BulletproofVest", "SoldierHelmet", "FirstAidKit" },
            ["Thief"] = new[] { "Lockpick", "SafeCrackingTool", "WindowCutter", "Crowbar", "CardboardBox", "WallBypasser" },
            ["Vampire"] = new[] { "BloodBag", "BloodTransfusionKit", "Knife", "VoodooDoll" },
        };

        private static readonly string[] FallbackPool = { "FirstAidKit", "Beer", "HamSandwich", "Knife", "Pistol", "Lockpick" };

        public static bool HasMerchantType(Agent agent) => AgentTraits.HasAny(agent, MerchantTypeIds);

        public static void EnsureInventory(Agent agent)
        {
            if (!HasMerchantType(agent)) return;
            agent.SetupSpecialInvDatabase();
            if (agent.specialInvDatabase == null) return;
            agent.specialInvDatabase.agent = agent;
            if (!agent.specialInvDatabase.createdInventory) agent.specialInvDatabase.CreateInventory();
            if (!agent.specialInvDatabase.filledSpecialInv) FillCustom(agent.specialInvDatabase, agent);
        }

        public static bool FillCustom(InvDatabase db, Agent agent)
        {
            List<string> pools = AgentTraits.Which(agent, MerchantTypeIds).ToList();
            if (pools.Count == 0) return false;

            db.filledSpecialInv = true;
            db.agent = agent;
            if (!db.createdInventory) db.CreateInventory();
            db.ClearInventory(true);

            int stockCount = Mathf.Clamp(5 + Math.Max(0, pools.Count - 1), 3, 12);
            bool repeat = AgentTraits.Has(agent, "Clearancer");
            EnsureSlots(db, stockCount);

            List<string> candidates = new List<string>();
            foreach (string id in pools)
            {
                if (Pools.TryGetValue(id, out string[] pool)) candidates.AddRange(pool);
            }
            if (candidates.Count == 0) candidates.AddRange(FallbackPool);

            HashSet<string> chosen = new HashSet<string>(StringComparer.Ordinal);
            int guard = 0;
            while (chosen.Count < stockCount && guard++ < stockCount * 20)
            {
                string itemName = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                if (!repeat && chosen.Contains(itemName)) continue;
                InvItem item;
                try { item = db.AddItem(itemName, 1); }
                catch (Exception e)
                {
                    Rck.Log.LogWarning($"Merchant stock skipped invalid item '{itemName}' for {agent.agentName}: {e.Message}");
                    continue;
                }
                if (item == null || item.invItemName == "" || item.invItemName == "Empty") continue;
                // AddItem(name, 1) leaves a count of 1 (one bullet, one point of durability). Start from vanilla shop
                // stock instead (InvDatabase.AddItemReal on a SpecialInvDatabase): full melee durability, else initCount.
                item.invItemCount = item.itemType == "WeaponMelee" ? 200 : Math.Max(1, item.initCount);
                chosen.Add(itemName + (repeat ? ":" + guard : ""));
                AdjustStockItem(agent, item);
            }
            return true;
        }

        private static void EnsureSlots(InvDatabase db, int stockCount)
        {
            if (db.npcInvSlots < stockCount) db.npcInvSlots = stockCount;
            while (db.InvItemList.Count < stockCount) db.InvItemList.Add(new InvItem());
        }

        private static void AdjustStockItem(Agent vendor, InvItem item)
        {
            int quantityMultiplier = StockQuantityMultiplier(vendor);
            float durabilityMultiplier = StockDurabilityMultiplier(vendor);
            bool durable = (item.isWeapon && item.itemType != "WeaponThrown") || item.isArmor || item.isArmorHead || item.hasCharges;
            bool stackable = item.stackable || item.stackableContents || item.itemType == "Consumable" || item.itemType == "Food" || item.itemType == "WeaponThrown";

            if (durable && item.invItemCount > 0)
                item.invItemCount = Mathf.Max(1, Mathf.RoundToInt(item.invItemCount * durabilityMultiplier));
            else if (stackable)
                item.invItemCount = Mathf.Max(1, item.invItemCount * quantityMultiplier);
        }

        private static int StockQuantityMultiplier(Agent agent)
        {
            int mult = 1;
            if (AgentTraits.Has(agent, "Wholesaler")) mult *= 2;
            if (AgentTraits.Has(agent, "Wholesalerer")) mult *= 3;
            if (AgentTraits.Has(agent, "Wholesalerest")) mult *= 4;
            return mult;
        }

        private static float StockDurabilityMultiplier(Agent agent)
        {
            float mult = 1f;
            if (AgentTraits.Has(agent, "Masterworker")) mult *= 2f;
            if (AgentTraits.Has(agent, "Masterworkerer")) mult *= 3f;
            if (AgentTraits.Has(agent, "Masterworkerest")) mult *= 4f;
            if (AgentTraits.Has(agent, "Shoddy_Goods")) mult *= 2f / 3f;
            if (AgentTraits.Has(agent, "Shiddy_Goods")) mult *= 1f / 3f;
            return mult;
        }
    }

    [HarmonyPatch(typeof(Agent), "Start")]
    internal static class AgentStartPatch
    {
        private static void Postfix(Agent __instance) => MerchantInventory.EnsureInventory(__instance);
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.RecycleStart))]
    internal static class AgentRecycleStartPatch
    {
        private static void Postfix(Agent __instance) => MerchantInventory.EnsureInventory(__instance);
    }

    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.FillSpecialInv))]
    internal static class FillSpecialInvPatch
    {
        private static bool Prefix(InvDatabase __instance)
        {
            Agent agent = __instance.agent;
            if (agent == null || !MerchantInventory.HasMerchantType(agent)) return true;
            MerchantInventory.FillCustom(__instance, agent);
            return false;
        }
    }

    // Custom Currency. The price of anything the NPC charges for becomes a code below; MoneySuccessPatch settles it in
    // bananas, booze, health or a traded item, and the label patches show it in words instead of "$-6604".
    internal static class Currency
    {
        internal const int Banana = -6600;
        internal const int Booze = -6601;
        internal const int Blood = -6602;
        internal const int Flesh = -6603;
        internal const int Swap = -6604;

        internal static bool IsCode(int price) => price >= Swap && price <= Banana;

        internal static int CodeFor(Agent agent)
        {
            if (AgentTraits.Has(agent, "Banana_Barter")) return Banana;
            if (AgentTraits.Has(agent, "Booze_Bargain")) return Booze;
            if (AgentTraits.Has(agent, "Blood_Covenant")) return Blood;
            if (AgentTraits.Has(agent, "Pound_of_Flesh")) return Flesh;
            if (AgentTraits.Has(agent, "Swap_Meet")) return Swap;
            return 0;
        }

        internal static string Label(int code)
        {
            switch (code)
            {
                case Banana: return "1 " + ItemName("Banana");
                case Booze: return "1 Booze";
                case Blood: return "20 HP";
                case Flesh: return "50 HP";
                default: return "1 Item";
            }
        }

        internal static string ItemName(string itemName)
        {
            try
            {
                string name = GameController.gameController.nameDB.GetName(itemName, "Item");
                return string.IsNullOrEmpty(name) ? itemName : name;
            }
            catch
            {
                return itemName;
            }
        }
    }

    [HarmonyPatch(typeof(PlayfieldObject), nameof(PlayfieldObject.determineMoneyCost), typeof(int), typeof(string))]
    internal static class DetermineMoneyCostPatch
    {
        private static void Postfix(PlayfieldObject __instance, string transactionType, ref int __result)
        {
            if (!(__instance is Agent agent) || agent.isPlayer > 0) return;
            // Bribe and mug offers value the player's item in money; they aren't a price this NPC charges.
            if (transactionType == "BribeQuest" || transactionType == "BribeItem" || transactionType == "BribeQuestItem"
                || transactionType == "MugItem" || transactionType == "BribeDeportationItem") return;

            int currency = Currency.CodeFor(agent);
            if (currency != 0)
            {
                // A free-item voucher makes the purchase free (vanilla prices it at 0), so don't charge the currency too.
                bool voucher = (transactionType == "AgentItemSale" || transactionType == "ArtOfTheDealSale")
                    && agent.interactingAgent != null && agent.interactingAgent.usingVoucher;
                if (!voucher) __result = currency;
                return;
            }

            float mult = 1f;
            if (AgentTraits.Has(agent, "Less")) mult *= 0.5f;
            if (AgentTraits.Has(agent, "Less_ish")) mult *= 0.75f;
            if (AgentTraits.Has(agent, "Little_Steep")) mult *= 1.25f;
            if (AgentTraits.Has(agent, "More")) mult *= 1.5f;
            if (AgentTraits.Has(agent, "Much_More")) mult *= 2f;
            if (AgentTraits.Has(agent, "Zero")) mult = 0f;
            if (Math.Abs(mult - 1f) > 0.001f)
                __result = Mathf.Clamp(Mathf.RoundToInt(__result * mult), 0, 9999);
        }
    }

    // What a currency payment took, so a vanilla refund in the same frame can give it back (see CurrencyRefundPatch).
    internal sealed class CurrencyPayment
    {
        internal Agent Buyer = null!;
        internal int Code;
        internal int Frame;
        internal string? OneOf;
        internal InvItem? Whole;
        internal int WholeCount;
        internal float Health;
    }

    [HarmonyPatch(typeof(PlayfieldObject), nameof(PlayfieldObject.moneySuccess), typeof(int), typeof(bool))]
    internal static class MoneySuccessPatch
    {
        // Swap_Meet never takes these: DestroyItem clears the holder's key, combination, badge and deed flags, vouchers
        // pay for things themselves, and the phone is the player's link to the level.
        private static readonly HashSet<string> NeverTraded = new HashSet<string>(StringComparer.Ordinal)
        {
            "Money", "Key", "KeyCard", "SafeCombination", "MayorBadge", "PropertyDeed", "FriendPhone", "FreeItemVoucher",
            "HiringVoucher",
        };

        private static CurrencyPayment? last;

        private static bool Prefix(PlayfieldObject __instance, int moneyAmt, ref bool __result)
        {
            Agent buyer = __instance.interactingAgent;
            if (buyer == null || !Currency.IsCode(moneyAmt)) return true;
            last = null;
            var paid = new CurrencyPayment { Buyer = buyer, Code = moneyAmt, Frame = Time.frameCount };
            switch (moneyAmt)
            {
                case Currency.Banana:
                    __result = TakeOne(buyer, paid, "Banana");
                    if (!__result) buyer.SayDialogue("NeedBananas");
                    break;
                case Currency.Booze:
                    __result = TakeOne(buyer, paid, "Beer", "Whiskey", "Cocktail");
                    if (!__result) buyer.SayDialogue("RCK_NeedItem");
                    break;
                case Currency.Blood:
                    __result = TakeHealth(buyer, paid, 20f);
                    break;
                case Currency.Flesh:
                    __result = TakeHealth(buyer, paid, 50f);
                    break;
                default:
                    __result = TakeTradeItem(buyer, paid);
                    if (!__result) buyer.SayDialogue("RCK_NeedItem");
                    break;
            }
            if (__result) last = paid;
            return false;
        }

        // Gives back the payment made by this inventory's owner earlier in the same frame, if its code matches.
        internal static bool TryRefund(InvDatabase db, int code)
        {
            CurrencyPayment? p = last;
            last = null;
            if (p == null || p.Code != code || p.Frame != Time.frameCount || p.Buyer == null || db.agent != p.Buyer) return false;
            Agent buyer = p.Buyer;
            if (buyer.inventory != null)
            {
                if (p.OneOf != null) buyer.inventory.AddItem(p.OneOf, 1);
                if (p.Whole != null)
                {
                    p.Whole.invItemCount = p.WholeCount;
                    buyer.inventory.AddItem(p.Whole);
                }
            }
            if (p.Health > 0f && buyer.statusEffects != null) buyer.statusEffects.ChangeHealth(p.Health);
            return true;
        }

        private static bool TakeOne(Agent agent, CurrencyPayment paid, params string[] itemNames)
        {
            if (agent.inventory == null) return false;
            foreach (string name in itemNames)
            {
                InvItem item = agent.inventory.FindItem(name);
                if (item != null && item.invItemCount > 0)
                {
                    agent.inventory.SubtractFromItemCount(item, 1);
                    paid.OneOf = name;
                    Feedback(agent, "Paid: " + Currency.ItemName(name));
                    return true;
                }
            }
            return false;
        }

        private static bool TakeHealth(Agent buyer, CurrencyPayment paid, float hp)
        {
            if (buyer.statusEffects == null || buyer.health <= hp)
            {
                buyer.SayDialogue("CantGiveBlood");
                return false;
            }
            buyer.statusEffects.ChangeHealth(-hp);
            paid.Health = hp;
            return true;
        }

        // Swap_Meet takes the buyer's cheapest tradeable item: one from a stack, or a whole unstackable item (a gun
        // with its ammo, armour with its durability). Equipped, quest and undroppable items are never taken.
        private static bool TakeTradeItem(Agent agent, CurrencyPayment paid)
        {
            if (agent.inventory == null) return false;
            InvItem? item = PickTradeItem(agent);
            if (item == null) return false;
            string name = item.invItemName;
            if (item.stackable && item.invItemCount > 1)
            {
                agent.inventory.SubtractFromItemCount(item, 1);
                paid.OneOf = name;
            }
            else
            {
                paid.Whole = item;
                paid.WholeCount = item.invItemCount;
                agent.inventory.SubtractFromItemCount(item, item.invItemCount);
            }
            Feedback(agent, "Traded: " + Currency.ItemName(name));
            return true;
        }

        internal static InvItem? PickTradeItem(Agent agent)
        {
            InvItem? best = null;
            foreach (InvItem item in agent.inventory.InvItemList)
            {
                if (item == null || string.IsNullOrEmpty(item.invItemName) || NeverTraded.Contains(item.invItemName)) continue;
                if (item.equipped || item.questItem || item.itemValue <= 0 || item.invItemCount <= 0) continue;
                if (item.CantDrop(agent) || item.CharacterExclusiveSpecificCharacter(agent)) continue;
                if (best == null || item.itemValue < best.itemValue) best = item;
            }
            return best;
        }

        private static void Feedback(Agent agent, string text)
        {
            try
            {
                if (agent.isPlayer > 0) agent.gc.spawnerMain.SpawnStatusText(agent, "ItemPickup", text);
            }
            catch (Exception e)
            {
                Rck.Log.LogDebug("Currency feedback text failed: " + e.Message);
            }
        }
    }

    // InvSlot.BuyItem refunds a purchase won by ChanceFreeShopItem(2) as Money worth the price, and
    // AgentInteractions.MugMoney pays a mugger the same way. With a currency that "price" is a code, so give back what
    // was paid instead, and never store a negative Money item.
    [HarmonyPatch(typeof(InvDatabase), nameof(InvDatabase.AddItem), typeof(InvItem), typeof(int))]
    internal static class CurrencyRefundPatch
    {
        private static bool Prefix(InvDatabase __instance, InvItem item, ref InvItem __result)
        {
            if (item == null || !Currency.IsCode(item.invItemCount) || item.invItemName != "Money") return true;
            MoneySuccessPatch.TryRefund(__instance, item.invItemCount);
            item.invItemCount = 0;
            __result = item;
            return false;
        }
    }

    // The shop window prints "$" + price in each slot; show the currency instead.
    [HarmonyPatch(typeof(InvSlot), nameof(InvSlot.UpdateInvSlot))]
    internal static class CurrencySlotLabelPatch
    {
        private static void Postfix(InvSlot __instance)
        {
            int price = __instance.previousPrice;
            if (!Currency.IsCode(price) || __instance.slotType != "NPCChest") return;
            UnityEngine.UI.Text text = __instance.toolbarNumText;
            if (text == null || text.text != "$" + price) return;
            text.text = Currency.Label(price);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }

    // Interaction buttons print " - $" + price; show the currency instead.
    [HarmonyPatch(typeof(WorldSpaceGUI), nameof(WorldSpaceGUI.ShowObjectButtons), typeof(GameObject), typeof(List<string>), typeof(List<string>), typeof(List<int>))]
    internal static class CurrencyButtonLabelPatch
    {
        private static void Postfix(WorldSpaceGUI __instance, List<string> buttons, List<int> buttonPrices)
        {
            if (buttons == null || buttonPrices == null || __instance.objectButtons == null) return;
            for (int i = 0; i < buttonPrices.Count && i < buttons.Count; i++)
            {
                int price = buttonPrices[i];
                if (!Currency.IsCode(price)) continue;
                Transform list = __instance.objectButtons.transform.GetChild(0);
                if (i >= list.childCount) return;
                Transform t = list.GetChild(i).Find("Text");
                UnityEngine.UI.Text? label = t == null ? null : t.GetComponent<UnityEngine.UI.Text>();
                string suffix = " - $" + price;
                if (label != null && label.text != null && label.text.EndsWith(suffix, StringComparison.Ordinal))
                    label.text = label.text.Substring(0, label.text.Length - suffix.Length) + " - " + Currency.Label(price);
            }
        }
    }
}
