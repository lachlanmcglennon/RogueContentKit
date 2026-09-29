using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RogueLibsPlus
{
    // RogueLibs' DisplayedUnlock throws when it's used outside of a menu, and its Twitch vote numbers can read past the
    // vote array. Mods (and the menu fixes) use unlocks outside of their own menu, so make those members tolerate it.
    internal static class DisplayedUnlockFixes
    {
        internal delegate void AddToDescription(DisplayedUnlock unlock, ref string? description);

        internal static Action<DisplayedUnlock, bool> updateButton = null!;
        private static AddToDescription addCancellationsTo = null!;
        private static AddToDescription addRecommendationsTo = null!;
        private static AddToDescription addPrerequisitesTo = null!;

        public static void Resolve()
        {
            updateButton = AccessTools.MethodDelegate<Action<DisplayedUnlock, bool>>(
                Fixes.Need(AccessTools.Method(typeof(DisplayedUnlock), "UpdateButton", new Type[] { typeof(bool) }), "DisplayedUnlock.UpdateButton(bool)"));
            addCancellationsTo = AccessTools.MethodDelegate<AddToDescription>(
                Fixes.Need(AccessTools.Method(typeof(DisplayedUnlock), "AddCancellationsTo"), "DisplayedUnlock.AddCancellationsTo"));
            addRecommendationsTo = AccessTools.MethodDelegate<AddToDescription>(
                Fixes.Need(AccessTools.Method(typeof(DisplayedUnlock), "AddRecommendationsTo"), "DisplayedUnlock.AddRecommendationsTo"));
            addPrerequisitesTo = AccessTools.MethodDelegate<AddToDescription>(
                Fixes.Need(AccessTools.Method(typeof(DisplayedUnlock), "AddPrerequisitesTo"), "DisplayedUnlock.AddPrerequisitesTo"));
        }

        public static void Patch(Harmony harmony)
        {
            Type t = typeof(DisplayedUnlockFixes);
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), "UpdateButton", new Type[] { typeof(bool), typeof(UnlockButtonState), typeof(UnlockButtonState) }),
                          prefix: new HarmonyMethod(t, nameof(UpdateButton)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), nameof(DisplayedUnlock.GetFancyName), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(GetFancyName)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), nameof(DisplayedUnlock.GetFancyDescription), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(GetFancyDescription)));
            harmony.Patch(AccessTools.Method(typeof(BigQuestUnlock), nameof(BigQuestUnlock.GetFancyDescription), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(GetFancyDescription)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), "PlaySound", new Type[] { typeof(string) }),
                          prefix: new HarmonyMethod(t, nameof(PlaySound)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), "SendAnnouncementInChat", new Type[] { typeof(string), typeof(string), typeof(string) }),
                          prefix: new HarmonyMethod(t, nameof(SendAnnouncementInChat)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), nameof(DisplayedUnlock.UpdateMenu), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(UpdateMenu)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), nameof(DisplayedUnlock.UpdateAllUnlocks), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(UpdateAllUnlocks)));
            harmony.Patch(AccessTools.Method(typeof(DisplayedUnlock), nameof(DisplayedUnlock.EnumerateCancellations), Type.EmptyTypes),
                          prefix: new HarmonyMethod(t, nameof(EnumerateCancellations)));
        }

        private static GameController gc => GameController.gameController;

        // nothing to update when the unlock isn't on screen (e.g. a mod calls this outside of a menu)
        public static bool UpdateButton(DisplayedUnlock __instance)
            => __instance.Menu is not null && __instance.ButtonData is not null;

        public static bool GetFancyName(DisplayedUnlock __instance, ref string __result)
        {
            DisplayedUnlock du = __instance;
            UnlocksMenu? menu = du.Menu;
            Unlock unlock = du.Unlock;
            string name = du.GetName();
            // outside of a menu, the name is decorated like in a generic unlock list
            UnlocksMenuType menuType = menu?.Type ?? UnlocksMenuType.Unknown;
            if (menuType == UnlocksMenuType.NewLevelTraits)
            {
                if (unlock.specialAbilities.Count > 0 || unlock.leadingTraits.Count > 0)
                    name = $"<color=yellow>{name}</color>";
                if (unlock.isUpgrade)
                    name = $"<color=lime>{name}</color>";
                if (du.Name is "EnduranceTrait" or "StrengthTrait" or "AccuracyTrait" or "SpeedTrait")
                    name = $"<color=cyan>{name}</color>";
                if (gc.twitchMode || gc.sessionDataBig.twitchOn && gc.sessionDataBig.twitchTraits)
                {
                    int num = GetVoteIndex(du);
                    Agent? agent = menu!.Agent;
                    int player = Mathf.Clamp(agent != null ? agent.isPlayer : 1, 1, 4);
                    int votes = GetVotes(num + (player - 1) * 5);
                    name = $"{name} <color=yellow>#{num + 1 + (player - 1) * 5}</color> <color=cyan>({votes})</color>";
                }
            }
            else if (menuType == UnlocksMenuType.TwitchRewards)
            {
                if (gc.twitchMode || gc.sessionDataBig.twitchOn && gc.sessionDataBig.twitchRewards)
                {
                    int num = GetVoteIndex(du);
                    name = $"{name} <color=yellow>#{num + 1}</color> <color=cyan>({GetVotes(num)})</color>";
                }
            }
            else if (menuType == UnlocksMenuType.TwitchDisasters)
            {
                if (gc.twitchMode || gc.sessionDataBig.twitchOn && gc.sessionDataBig.twitchLevelFeelings)
                {
                    int num = GetVoteIndex(du);
                    name = $"{name} <color=yellow>#{num + 1}</color> <color=cyan>({GetVotes(num)})</color>";
                }
            }
            else if (menuType == UnlocksMenuType.Loadouts && du is ItemUnlock)
            {
                InvItem invItem = new InvItem { invItemName = du.Name };
                invItem.SetupDetails(false);
                if (invItem.rewardCount != 1 && !invItem.isArmor && !invItem.isArmorHead && invItem.itemType != ItemTypes.WeaponMelee)
                    name += $" ({invItem.rewardCount})";
                name += $" - ${du.LoadoutCost}";
            }
            else
            {
                if (du.IsUnlocked || unlock.nowAvailable || menu?.ShowLockedUnlocks == true)
                {
                    if (menuType == UnlocksMenuType.CharacterCreation)
                    {
                        if (du.CharacterCreationCost != 0)
                            name += $" | <color={(du.CharacterCreationCost < 0 ? "lime" : "orange")}>{du.CharacterCreationCost}</color>";
                    }
                    if (!du.IsUnlocked && unlock.nowAvailable && du.UnlockCost > 0)
                    {
                        name += $" - ${du.UnlockCost}";
                    }
                }
                else name = "?????";
            }
            __result = name;
            return false;
        }
        // Twitch vote numbers follow the on-screen button order
        private static int GetVoteIndex(DisplayedUnlock du)
        {
            if (du.ButtonData is not null) return du.ButtonData.scrollingButtonNum;
            return du.Menu is null ? 0 : Math.Max(0, du.Menu.Unlocks.IndexOf(du));
        }
        private static int GetVotes(int index)
        {
            int[]? votes = gc.twitchFunctions?.voteCount;
            return votes is not null && index >= 0 && index < votes.Length ? votes[index] : 0;
        }

        public static bool GetFancyDescription(DisplayedUnlock __instance, ref string __result)
        {
            if (__instance.Menu is not null) return true;
            string? text;
            if (__instance.IsUnlocked || __instance.Unlock.nowAvailable)
            {
                text = __instance.GetDescription();
                addCancellationsTo(__instance, ref text);
                addRecommendationsTo(__instance, ref text);
                if (!__instance.IsUnlocked)
                    addPrerequisitesTo(__instance, ref text);
            }
            else
            {
                text = "?????";
                addPrerequisitesTo(__instance, ref text);
            }
            __result = text ?? string.Empty;
            return false;
        }

        public static bool PlaySound(DisplayedUnlock __instance, string clipName)
        {
            if (__instance.Menu is not null) return true;
            Agent? player = gc.playerAgent;
            if (player != null) gc.audioHandler.Play(player, clipName);
            return false;
        }
        public static bool SendAnnouncementInChat(DisplayedUnlock __instance, string msg1, string? msg2, string? msg3)
        {
            if (__instance.Menu is not null) return true;
            if (!gc.serverPlayer || !gc.multiplayerMode) return false;
            Agent? agent = gc.playerAgent;
            if (agent != null)
                agent.objectMult.SendChatAnnouncement(msg1, msg2 ?? string.Empty, msg3 ?? string.Empty);
            return false;
        }
        public static bool UpdateMenu(DisplayedUnlock __instance) => __instance.Menu is not null;
        public static bool UpdateAllUnlocks(DisplayedUnlock __instance)
        {
            if (__instance.Menu is not null) return true;
            __instance.UpdateUnlock();
            return false;
        }
        public static bool EnumerateCancellations(DisplayedUnlock __instance, ref IEnumerable<DisplayedUnlock> __result)
        {
            if (__instance.Menu is not null) return true;
            __result = Array.Empty<DisplayedUnlock>();
            return false;
        }
    }

    // RogueLibs' vanilla unlock wrappers assume they're in the menu of their own kind. Let them show up in, and be
    // queried from, other menus (and no menu) without throwing.
    internal static class VanillaUnlockFixes
    {
        public static void Resolve()
        {
            // the fixes only use public members, and DisplayedUnlockFixes' UpdateButton(bool) delegate
            if (DisplayedUnlockFixes.updateButton is null)
                DisplayedUnlockFixes.Resolve();
        }

        public static void Patch(Harmony harmony)
        {
            Type t = typeof(VanillaUnlockFixes);
            // IsAddedToCC: false outside of the character creation menu
            harmony.Patch(AccessTools.PropertyGetter(typeof(AbilityUnlock), nameof(AbilityUnlock.IsAddedToCC)), prefix: new HarmonyMethod(t, nameof(IsAddedToCC)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(BigQuestUnlock), nameof(BigQuestUnlock.IsAddedToCC)), prefix: new HarmonyMethod(t, nameof(IsAddedToCC)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(ItemUnlock), nameof(ItemUnlock.IsAddedToCC)), prefix: new HarmonyMethod(t, nameof(IsAddedToCC)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(TraitUnlock), nameof(TraitUnlock.IsAddedToCC)), prefix: new HarmonyMethod(t, nameof(IsAddedToCC)));

            // loadouts: outside of a player's menu, use the first player's loadouts
            harmony.Patch(AccessTools.PropertyGetter(typeof(ItemUnlock), nameof(ItemUnlock.IsSelectedLoadout)), prefix: new HarmonyMethod(t, nameof(get_IsSelectedLoadout)));
            harmony.Patch(AccessTools.PropertySetter(typeof(ItemUnlock), nameof(ItemUnlock.IsSelectedLoadout)), prefix: new HarmonyMethod(t, nameof(set_IsSelectedLoadout)));
            harmony.Patch(AccessTools.Method(typeof(ItemUnlock), "GetMyLoadouts", Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(GetMyLoadouts)));

            // UpdateButton: nothing to update off screen; update the button in menus that the wrapper doesn't know about
            harmony.Patch(AccessTools.Method(typeof(AbilityUnlock), nameof(AbilityUnlock.UpdateButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(UpdateButton_Ability)));
            harmony.Patch(AccessTools.Method(typeof(BigQuestUnlock), nameof(BigQuestUnlock.UpdateButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(UpdateButton_Ability)));
            harmony.Patch(AccessTools.Method(typeof(FloorUnlock), nameof(FloorUnlock.UpdateButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(UpdateButton_Floor)));
            harmony.Patch(AccessTools.Method(typeof(ItemUnlock), nameof(ItemUnlock.UpdateButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(UpdateButton_Item)));
            harmony.Patch(AccessTools.Method(typeof(TraitUnlock), nameof(TraitUnlock.UpdateButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(UpdateButton_Trait)));

            // OnPushedButton: nothing to push off screen, or in a menu of a kind that the wrapper would cast wrongly
            harmony.Patch(AccessTools.Method(typeof(AbilityUnlock), nameof(AbilityUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton)));
            harmony.Patch(AccessTools.Method(typeof(BigQuestUnlock), nameof(BigQuestUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton)));
            harmony.Patch(AccessTools.Method(typeof(MutatorUnlock), nameof(MutatorUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton)));
            harmony.Patch(AccessTools.Method(typeof(FloorUnlock), nameof(FloorUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton_Floor)));
            harmony.Patch(AccessTools.Method(typeof(ItemUnlock), nameof(ItemUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton_Item)));
            harmony.Patch(AccessTools.Method(typeof(TraitUnlock), nameof(TraitUnlock.OnPushedButton), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(OnPushedButton_Trait)));

            // Augmentation Booth prices outside of the booth's menu
            harmony.Patch(AccessTools.Method(typeof(TraitUnlock), nameof(TraitUnlock.GetUpgradeCost), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(GetUpgradeCost)));
            harmony.Patch(AccessTools.Method(typeof(TraitUnlock), nameof(TraitUnlock.GetRemovalCost), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(GetRemovalCost)));
            harmony.Patch(AccessTools.Method(typeof(TraitUnlock), nameof(TraitUnlock.GetSwapCost), Type.EmptyTypes), prefix: new HarmonyMethod(t, nameof(GetSwapCost)));

            // vanilla swaps the character select slots to their "Super" versions when this mutator changes
            harmony.Patch(AccessTools.PropertySetter(typeof(MutatorUnlock), nameof(MutatorUnlock.IsEnabled)),
                          prefix: new HarmonyMethod(t, nameof(set_IsEnabled_Prefix)), postfix: new HarmonyMethod(t, nameof(set_IsEnabled_Postfix)));
        }

        private static GameController gc => GameController.gameController;

        public static bool IsAddedToCC(DisplayedUnlock __instance, ref bool __result)
        {
            if (__instance.Menu is CustomCharacterCreation) return true;
            __result = false;
            return false;
        }

        private static List<string> MyLoadouts(DisplayedUnlock unlock)
        {
            Agent? agent = unlock.Menu?.Agent;
            int player = agent != null ? agent.isPlayer : 1;
            return player == 2 ? gc.sessionDataBig.loadouts2
                : player == 3 ? gc.sessionDataBig.loadouts3
                : player == 4 ? gc.sessionDataBig.loadouts4
                : gc.sessionDataBig.loadouts1;
        }
        public static bool get_IsSelectedLoadout(ItemUnlock __instance, ref bool __result)
        {
            if (__instance.Menu is not null) return true;
            __result = MyLoadouts(__instance).Contains(__instance.Name);
            return false;
        }
        public static bool set_IsSelectedLoadout(ItemUnlock __instance, bool value)
        {
            if (__instance.Menu is not null) return true;
            List<string> myLoadouts = MyLoadouts(__instance);
            bool cur = myLoadouts.Contains(__instance.Name);
            if (cur && !value) myLoadouts.Remove(__instance.Name);
            else if (!cur && value) myLoadouts.Add(__instance.Name);
            return false;
        }
        public static bool GetMyLoadouts(ItemUnlock __instance, ref List<string> __result)
        {
            __result = MyLoadouts(__instance);
            return false;
        }

        private static bool OffScreen(DisplayedUnlock unlock) => unlock.Menu is null || unlock.ButtonData is null;
        public static bool UpdateButton_Ability(DisplayedUnlock __instance)
        {
            if (OffScreen(__instance)) return false;
            if (__instance.Menu!.Type == UnlocksMenuType.CharacterCreation) return true;
            DisplayedUnlockFixes.updateButton(__instance, false);
            return false;
        }
        public static bool UpdateButton_Floor(DisplayedUnlock __instance)
        {
            if (OffScreen(__instance)) return false;
            if (__instance.Menu!.Type == UnlocksMenuType.FloorsMenu) return true;
            DisplayedUnlockFixes.updateButton(__instance, false);
            return false;
        }
        public static bool UpdateButton_Item(DisplayedUnlock __instance)
        {
            if (OffScreen(__instance)) return false;
            if (__instance.Menu!.Type is UnlocksMenuType.RewardsMenu or UnlocksMenuType.ItemTeleporter
                                    or UnlocksMenuType.Loadouts or UnlocksMenuType.CharacterCreation) return true;
            DisplayedUnlockFixes.updateButton(__instance, false);
            return false;
        }
        public static bool UpdateButton_Trait(DisplayedUnlock __instance)
        {
            if (OffScreen(__instance)) return false;
            UnlocksMenuType type = __instance.Menu!.Type;
            if (type is UnlocksMenuType.TraitsMenu or UnlocksMenuType.CharacterCreation or UnlocksMenuType.AB_UpgradeTrait
                     or UnlocksMenuType.AB_RemoveTrait or UnlocksMenuType.AB_SwapTrait) return true;
            DisplayedUnlockFixes.updateButton(__instance, type != UnlocksMenuType.NewLevelTraits && __instance.IsEnabled);
            return false;
        }

        public static bool OnPushedButton(DisplayedUnlock __instance) => __instance.Menu is not null;
        public static bool OnPushedButton_Floor(DisplayedUnlock __instance)
            => __instance.Menu is { Type: UnlocksMenuType.FloorsMenu };
        public static bool OnPushedButton_Item(ItemUnlock __instance)
        {
            UnlocksMenu? menu = __instance.Menu;
            if (menu is null) return false;
            return !(menu.Type == UnlocksMenuType.RewardsMenu && menu is not CustomScrollingMenu && __instance.IsUnlocked);
        }
        public static bool OnPushedButton_Trait(TraitUnlock __instance)
        {
            UnlocksMenu? menu = __instance.Menu;
            if (menu is null) return false;
            if (menu is CustomScrollingMenu) return true;
            if (menu.Type is UnlocksMenuType.NewLevelTraits or UnlocksMenuType.AB_SwapTrait) return false;
            return !(menu.Type == UnlocksMenuType.TraitsMenu && __instance.IsUnlocked);
        }

        // the Augmentation Booth prices live on the ScrollingMenu; outside of that menu, use the main scrolling menu's
        // values (the defaults are the game's own)
        private static ScrollingMenu? GetCostMenu()
        {
            ScrollingMenu? main = gc.mainGUI?.scrollingMenuScript;
            return main != null ? main : null;
        }
        public static bool GetUpgradeCost(TraitUnlock __instance, ref int __result)
        {
            if (__instance.Menu is CustomScrollingMenu) return true;
            ScrollingMenu? menu = GetCostMenu();
            int adjust = menu != null ? menu.upgradeTraitAdjust : 75;
            __result = Mathf.Abs((gc.unlocks.GetUnlock(__instance.Unlock.upgrade, "Trait")?.cost3 ?? __instance.CharacterCreationCost) * adjust);
            return false;
        }
        public static bool GetRemovalCost(TraitUnlock __instance, ref int __result)
        {
            if (__instance.Menu is CustomScrollingMenu) return true;
            ScrollingMenu? menu = GetCostMenu();
            int adjust = menu != null ? menu.removeTraitAdjust : 150;
            __result = Mathf.Abs(__instance.CharacterCreationCost * adjust);
            return false;
        }
        public static bool GetSwapCost(TraitUnlock __instance, ref int __result)
        {
            if (__instance.Menu is CustomScrollingMenu) return true;
            ScrollingMenu? menu = GetCostMenu();
            int cost = __instance.CharacterCreationCost;
            int adjust = cost < 0
                ? menu != null ? menu.changeTraitRandomAdjustNegativeTrait : 75
                : menu != null ? menu.changeTraitRandomAdjustPositiveTrait : 35;
            __result = Mathf.Abs(cost * adjust);
            return false;
        }

        public static void set_IsEnabled_Prefix(MutatorUnlock __instance, out bool __state) => __state = __instance.IsEnabled;
        public static void set_IsEnabled_Postfix(MutatorUnlock __instance, bool __state)
        {
            if (__instance.Name != "SuperSpecialCharacters" || __instance.IsEnabled == __state) return;
            try
            {
                CharacterSelect? select = gc.mainGUI?.characterSelectScript;
                if (select != null && gc.playerAgent != null) select.RefreshSuperSpecials();
            }
            catch (Exception e)
            {
                RogueLibsPlusPlugin.Log.LogWarning($"Couldn't refresh the Super Special character slots: {e.Message}");
            }
        }
    }
}
