using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;

namespace RCK.Items
{
    /// <summary>
    ///   The Augmentation Canister adds a button to Augmentation Booths that gives you one completely random trait,
    ///   good or bad, for free. The canister is used up.
    /// </summary>
    internal static class AugmentationCanister
    {
        internal const string ItemName = "AugmentationCanister";

        private const string Hint = "RCK_UseAtAugmentationBooth";
        private const string NoTrait = "RCK_NoRandomTrait";

        internal static bool Enabled { get; private set; }

        internal static void Initialize()
        {
            if (Vanilla.Mentions(AccessTools.Method(typeof(AugmentationBooth), nameof(AugmentationBooth.PressedButton), new[] { typeof(string), typeof(int) }), ItemName, "Augmentation Canister fix")) return;
            Enabled = UsableItems.Register(ItemName, "Augmentation Canister fix", static (_, agent) =>
            {
                GameController.gameController.spawnerMain.SpawnStatusText(agent, "Buff", Hint, NameTypes.Interface);
            });
            if (!Enabled) return;

            // The item keeps the game's own (translated) description; the booth button explains the random trait.
            ItemText.Name(Hint, NameTypes.Interface, "Use it at an Augmentation Booth", "Используйте в кабине аугментации", "请在改造亭使用");
            ItemText.Name(NoTrait, NameTypes.Interface, "No trait fits", "Нет подходящей черты", "没有合适的特性");
            ItemText.Name(CustomButtons.UseAugmentationCanister, NameTypes.Interface,
                "Use Augmentation Canister", "Использовать канистру аугментации", "使用改造罐");
            // The booth's tooltip reads the button's StatusEffect and Description names.
            ItemText.Name(CustomButtons.UseAugmentationCanister, NameTypes.StatusEffect,
                "Use Augmentation Canister", "Использовать канистру аугментации", "使用改造罐");
            ItemText.Description(CustomButtons.UseAugmentationCanister,
                "Uses up the canister to give you one completely random trait. It could be anything, good or bad.",
                "Расходует канистру и даёт одну совершенно случайную черту. Это может быть что угодно, хорошее или плохое.",
                "消耗改造罐，随机获得一个特性。可能是任何特性，有好有坏。");
            ButtonLabels.Register(typeof(CustomButtons));

            RogueInteractions.CreateProvider<AugmentationBooth>(static h =>
            {
                if (h.Helper.interactingFar || h.Agent.possessing || h.Agent.inventory == null) return;
                if (!h.Agent.inventory.HasItem(ItemName)) return;
                h.AddButton(CustomButtons.UseAugmentationCanister, static m =>
                {
                    Use(m.Agent);
                    m.StopInteraction();
                });
            });
        }

        private static void Use(Agent agent)
        {
            GameController gc = GameController.gameController;
            InvItem canister = agent.inventory.FindItem(ItemName);
            if (canister == null) return;
            List<Unlock> pool = Pool(agent);
            if (pool.Count == 0)
            {
                gc.spawnerMain.SpawnStatusText(agent, "Buff", NoTrait, NameTypes.Interface);
                gc.audioHandler.Play(agent, "CantDo");
                return;
            }

            Unlock trait = pool[UnityEngine.Random.Range(0, pool.Count)];
            agent.inventory.SubtractFromItemCount(canister, 1);
            agent.usingAugmentationBooth = true;
            try
            {
                agent.statusEffects.AddTrait(trait.unlockName);
            }
            finally
            {
                agent.usingAugmentationBooth = false;
            }
            gc.audioHandler.Play(agent, "AddTrait");
            Rck.Log.LogInfo($"Augmentation Canister gave {agent.agentName} the trait {trait.unlockName}.");
        }

        /// <summary>
        ///   Every trait, good or bad, the agent could take at a booth: the game's own swap rules (special-ability
        ///   gates, cancellations, conflicts), minus traits they have, upgrades, and traits that can't be swapped.
        /// </summary>
        private static List<Unlock> Pool(Agent agent)
        {
            GameController gc = GameController.gameController;
            ScrollingMenu menu = agent.mainGUI.scrollingMenuPersonalScript;
            menu.agent = agent;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<Unlock> pool = new List<Unlock>();
            foreach (List<Unlock> list in new[] { gc.sessionDataBig.traitUnlocks, gc.sessionDataBig.traitUnlocksCharacterCreation })
            {
                if (list == null) continue;
                foreach (Unlock u in list)
                {
                    if (u == null || string.IsNullOrEmpty(u.unlockName) || !seen.Add(u.unlockName)) continue;
                    if (u.isUpgrade || u.cantSwap || u.removal || u.notActive) continue;
                    if (agent.statusEffects.hasTrait(u.unlockName)) continue;
                    if (!string.IsNullOrEmpty(u.upgrade) && agent.statusEffects.hasTrait(u.upgrade)) continue;
                    if (!HasSpecialAbilityFor(agent, u)) continue;
                    if (!menu.CanHaveTrait(u) || !menu.HasNoCancellations(u) || !menu.CanHaveSpecialAbility(u)) continue;
                    pool.Add(u);
                }
            }
            return pool;
        }

        private static bool HasSpecialAbilityFor(Agent agent, Unlock u)
        {
            if (u.specialAbilities == null || u.specialAbilities.Count == 0) return true;
            foreach (string ability in u.specialAbilities)
                if (agent.statusEffects.hasSpecialAbility(ability)) return true;
            return false;
        }

        /// <summary>Custom booth buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
        internal static class CustomButtons
        {
            [ButtonLabel("Use Augmentation Canister")] public const string UseAugmentationCanister = "RCK_UseAugmentationCanister";
        }
    }
}
