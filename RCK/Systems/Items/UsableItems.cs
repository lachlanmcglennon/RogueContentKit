using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;

namespace RCK.Items
{
    /// <summary>
    ///   Clicking a cut Tool item runs the game's <c>ItemFunctions.UseItem</c>, which has no case for it, so nothing
    ///   happened. Each item registers what clicking it does; RCK runs that after the game's own checks.
    /// </summary>
    internal static class UsableItems
    {
        private static readonly Dictionary<string, Action<InvItem, Agent>> Handlers = new Dictionary<string, Action<InvItem, Agent>>(StringComparer.Ordinal);

        internal static void Initialize()
        {
            ForceField.Initialize();
            MagicLamp.Initialize();
        }

        /// <summary>Registers a click handler unless the game's UseItem already handles <paramref name="itemName"/>.</summary>
        internal static bool Register(string itemName, string fixName, Action<InvItem, Agent> use)
        {
            if (Vanilla.Mentions(AccessTools.Method(typeof(ItemFunctions), nameof(ItemFunctions.UseItem)), itemName, fixName)) return false;
            Handlers[itemName] = use;
            return true;
        }

        internal static Action<InvItem, Agent>? Handler(string itemName)
            => itemName != null && Handlers.TryGetValue(itemName, out Action<InvItem, Agent> use) ? use : null;
    }

    [HarmonyPatch(typeof(ItemFunctions), nameof(ItemFunctions.UseItem))]
    internal static class ItemFunctions_UseItem_CutItems
    {
        private static bool Prefix(InvItem item, Agent agent)
        {
            if (item == null || agent == null) return true;
            Action<InvItem, Agent>? use = UsableItems.Handler(item.invItemName);
            if (use == null) return true;

            // The game's own opening checks; letting it run gives the usual "can't do" feedback.
            if (agent.ghost) return true;
            if (agent.statusEffects.hasTrait("CantInteract") && item.itemType != "Food") return true;
            if (agent.localPlayer)
            {
                InvItem special = agent.inventory.equippedSpecialAbility;
                if (!agent.inventory.HasItem(item.invItemName) && (special == null || special.invItemName != item.invItemName)) return true;
                if ((item.Categories.Contains("Usable") || item.itemType == "Consumable") && !item.used)
                {
                    item.used = true;
                    if (agent.isPlayer > 0) GameController.gameController.sessionData.endStats[agent.isPlayer].itemsUsed++;
                }
            }

            use(item, agent);
            return false;
        }
    }

    /// <summary>The Force Field makes you invincible for a few seconds.</summary>
    internal static class ForceField
    {
        internal const string ItemName = "ForceField";

        internal const int Seconds = 8;

        internal static void Initialize()
        {
            if (!UsableItems.Register(ItemName, "Force Field fix", Use)) return;
            ItemText.Description(ItemName,
                $"Surrounds you with a shield that blocks all damage for {Seconds} seconds.",
                $"Окружает вас щитом, который {Seconds} секунд блокирует любой урон.",
                $"用护盾包裹你，{Seconds} 秒内免疫一切伤害。");
        }

        private static void Use(InvItem item, Agent agent)
        {
            GameController gc = GameController.gameController;
            item.database.SubtractFromItemCount(item, 1);
            agent.statusEffects.AddStatusEffect("Invincible", true, true, Seconds);
            agent.SpawnParticleEffect("Spawn", agent.tr.position, false);
            gc.audioHandler.Play(agent, "Recharge");
            item.itemFunctions.UseItemAnim(item, agent);
        }
    }

    /// <summary>Rubbing the Magic Lamp grants one random wish, then the lamp is spent.</summary>
    internal static class MagicLamp
    {
        internal const string ItemName = "MagicLamp";

        internal const int Money = 250;

        private const string Riches = "RCK_Wish_Riches";
        private const string Health = "RCK_Wish_Health";
        private const string ExtraLife = "RCK_Wish_ExtraLife";
        private const string Power = "RCK_Wish_Power";

        internal static void Initialize()
        {
            if (!UsableItems.Register(ItemName, "Magic Lamp fix", Use)) return;
            ItemText.Name(Riches, NameTypes.Interface, "Wish granted: riches!", "Желание исполнено: богатство!", "愿望实现：财富！");
            ItemText.Name(Health, NameTypes.Interface, "Wish granted: health!", "Желание исполнено: здоровье!", "愿望实现：健康！");
            ItemText.Name(ExtraLife, NameTypes.Interface, "Wish granted: another life!", "Желание исполнено: ещё одна жизнь!", "愿望实现：再来一条命！");
            ItemText.Name(Power, NameTypes.Interface, "Wish granted: power!", "Желание исполнено: сила!", "愿望实现：力量！");
        }

        private static void Use(InvItem item, Agent agent)
        {
            GameController gc = GameController.gameController;
            List<string> wishes = new List<string> { Riches, Power };
            if (agent.health < agent.healthMax) wishes.Add(Health);
            if (!HasLastingResurrection(agent)) wishes.Add(ExtraLife);
            string wish = wishes[UnityEngine.Random.Range(0, wishes.Count)];

            item.database.SubtractFromItemCount(item, 1);
            switch (wish)
            {
                case Riches:
                    agent.inventory.AddItemOrDrop("Money", Money);
                    break;
                case Health:
                    agent.statusEffects.ChangeHealth(agent.healthMax - agent.health);
                    break;
                case ExtraLife:
                    agent.statusEffects.AddStatusEffect("Resurrection");
                    break;
                case Power:
                    agent.statusEffects.AddStatusEffect("IncreaseAllStats");
                    break;
            }
            agent.SpawnParticleEffect("Spawn", agent.tr.position, false);
            gc.audioHandler.Play(agent, "UseBooUrn");
            gc.spawnerMain.SpawnStatusText(agent, "Buff", wish, NameTypes.Interface);
            item.itemFunctions.UseItemAnim(item, agent);
        }

        private static bool HasLastingResurrection(Agent agent)
        {
            foreach (StatusEffect effect in agent.statusEffects.StatusEffectList)
                if (effect.statusEffectName == "Resurrection" && effect.infiniteTime) return true;
            return false;
        }
    }
}
