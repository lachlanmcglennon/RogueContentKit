using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;
using UnityEngine.UI;

namespace RogueLibsPlus
{
    // A DisplayedUnlock keeps only the last menu and button it was set up in, but the same unlock can be on screen in
    // two menus at once (e.g. both co-op players picking a level-up trait, or two character creation screens). Each
    // button remembers the menu it was set up for, and the unlock is re-bound to that menu and button before it
    // handles a press or shows its details.
    internal static class UnlockMenuBindings
    {
        private static readonly ConditionalWeakTable<ButtonData, UnlocksMenu> menus = new ConditionalWeakTable<ButtonData, UnlocksMenu>();

        internal static Action<DisplayedUnlock, UnlocksMenu?> setMenu = null!;
        internal static Action<DisplayedUnlock, ButtonData?> setButtonData = null!;

        public static void Record(ButtonData buttonData, UnlocksMenu? menu)
        {
            menus.Remove(buttonData);
            if (menu is not null) menus.Add(buttonData, menu);
        }
        public static UnlocksMenu? GetMenu(ButtonData buttonData)
            => menus.TryGetValue(buttonData, out UnlocksMenu menu) ? menu : null;

        public static DisplayedUnlock? Bind(ButtonData? buttonData)
        {
            if (buttonData?.GetHook() is not DisplayedUnlock du) return null;
            UnlocksMenu? menu = GetMenu(buttonData);
            if (menu is not null) setMenu(du, menu);
            setButtonData(du, buttonData);
            return du;
        }
        public static DisplayedUnlock? BindFor(IList<ButtonData>? buttons, int index, Unlock? unlock)
        {
            if (buttons is not null && index >= 0 && index < buttons.Count)
            {
                ButtonData buttonData = buttons[index];
                if (buttonData is not null && ReferenceEquals(buttonData.scrollingButtonUnlock, unlock))
                {
                    DisplayedUnlock? bound = Bind(buttonData);
                    if (bound is not null) return bound;
                }
            }
            return Resolve(unlock);
        }
        public static void BindAll(IEnumerable<ButtonData>? buttons)
        {
            if (buttons is null) return;
            foreach (ButtonData buttonData in buttons)
                if (buttonData is not null && GetMenu(buttonData) is not null)
                    Bind(buttonData);
        }

        public static DisplayedUnlock? Resolve(Unlock? unlock)
        {
            if (unlock is null) return null;
            if (unlock.GetHook() is DisplayedUnlock du) return du;
            // Copies of unlocks (e.g. loadout lists) don't carry hooks. Use the registered original.
            Unlock? original = GameController.gameController?.sessionDataBig?.unlocks?
                .Find(u => u.unlockName == unlock.unlockName && u.unlockType == unlock.unlockType);
            if (original?.GetHook() is DisplayedUnlock originalHook)
            {
                HookSystem.SetHook(unlock, originalHook);
                return originalHook;
            }
            return null;
        }

        private static readonly HashSet<string> warned = new HashSet<string>();
        public static IEnumerable<DisplayedUnlock> ResolveAll(IEnumerable<Unlock> unlocks)
        {
            foreach (Unlock unlock in unlocks)
            {
                DisplayedUnlock? du = Resolve(unlock);
                if (du is not null) yield return du;
                else if (warned.Add($"{unlock?.unlockType}:{unlock?.unlockName}"))
                    RogueLibsPlusPlugin.Log.LogWarning($"Unlock \"{unlock?.unlockName}\" ({unlock?.unlockType}) has no RogueLibs hook; it's left to vanilla.");
            }
        }
    }

    // RogueLibs' menus cast every unlock's hook to DisplayedUnlock, which fails on hookless copies (loadouts), on
    // Twitch reward and arcade buttons, and when two menus show the same unlock. These patches wrap (or replace)
    // RogueLibs' own menu patches so that those cases are resolved, bound to the right menu or left to vanilla.
    internal static class MenuFixes
    {
        private static Func<ButtonData, Unlock, bool> setupUnlocks = null!;

        public static void Resolve()
        {
            Type plugin = Fixes.Need(AccessTools.TypeByName("RogueLibsCore.RogueLibsPlugin"), "RogueLibsCore.RogueLibsPlugin");
            UnlockMenuBindings.setMenu = AccessTools.MethodDelegate<Action<DisplayedUnlock, UnlocksMenu?>>(
                Fixes.Need(AccessTools.PropertySetter(typeof(DisplayedUnlock), nameof(DisplayedUnlock.Menu)), "DisplayedUnlock.Menu setter"));
            UnlockMenuBindings.setButtonData = AccessTools.MethodDelegate<Action<DisplayedUnlock, ButtonData?>>(
                Fixes.Need(AccessTools.PropertySetter(typeof(DisplayedUnlock), nameof(DisplayedUnlock.ButtonData)), "DisplayedUnlock.ButtonData setter"));
            setupUnlocks = AccessTools.MethodDelegate<Func<ButtonData, Unlock, bool>>(
                Fixes.Need(AccessTools.Method(plugin, "SetupUnlocks", new Type[] { typeof(ButtonData), typeof(Unlock) }), "RogueLibsPlugin.SetupUnlocks"));
        }

        public static void Patch(Harmony harmony)
        {
            Type plugin = Fixes.Need(AccessTools.TypeByName("RogueLibsCore.RogueLibsPlugin"), "RogueLibsCore.RogueLibsPlugin");
            Type t = typeof(MenuFixes);

            harmony.Patch(AccessTools.Method(plugin, "SetupUnlocks", new Type[] { typeof(ButtonData), typeof(Unlock) }),
                          prefix: new HarmonyMethod(t, nameof(SetupUnlocks_Prefix)),
                          postfix: new HarmonyMethod(t, nameof(SetupUnlocks_Postfix)));

            harmony.Patch(AccessTools.Method(plugin, "ScrollingMenu_OpenScrollingMenu"),
                          prefix: new HarmonyMethod(t, nameof(ScrollingMenu_OpenScrollingMenu)));
            harmony.Patch(AccessTools.Method(plugin, "ScrollingMenu_SortUnlocks"),
                          prefix: new HarmonyMethod(t, nameof(ScrollingMenu_SortUnlocks)));
            harmony.Patch(AccessTools.Method(plugin, "ScrollingMenu_PushedButton"),
                          prefix: new HarmonyMethod(t, nameof(ScrollingMenu_PushedButton)));
            harmony.Patch(AccessTools.Method(plugin, "ScrollingMenu_ShowDetails"),
                          prefix: new HarmonyMethod(t, nameof(ScrollingMenu_ShowDetails)));

            harmony.Patch(AccessTools.Method(plugin, "CharacterCreation_SortUnlocks"),
                          prefix: new HarmonyMethod(t, nameof(CharacterCreation_SortUnlocks)));
            harmony.Patch(AccessTools.Method(plugin, "CharacterCreation_PushedButton"),
                          prefix: new HarmonyMethod(t, nameof(CharacterCreation_PushedButton)));
            harmony.Patch(AccessTools.Method(plugin, "CharacterCreation_ShowDetails"),
                          prefix: new HarmonyMethod(t, nameof(CharacterCreation_ShowDetails)));
        }

        // RogueLibsPlugin.SetupUnlocks(ButtonData myButtonData, Unlock myUnlock): returns false once RogueLibs has set
        // the button up, or true to let vanilla do it. Hookless unlocks are resolved first, or left to vanilla.
        public static bool SetupUnlocks_Prefix(Unlock __1, ref bool __result)
        {
            if (UnlockMenuBindings.Resolve(__1) is not null) return true;
            __result = true;
            return false;
        }
        public static void SetupUnlocks_Postfix(ButtonData __0, bool __result)
        {
            if (!__result && __0 is not null && __0.GetHook() is DisplayedUnlock du)
                UnlockMenuBindings.Record(__0, du.Menu);
        }

        // RogueLibsPlugin.ScrollingMenu_OpenScrollingMenu(ScrollingMenu __instance, ref float __state, List<Unlock> ___listUnlocks)
        public static bool ScrollingMenu_OpenScrollingMenu(ScrollingMenu __0, ref float __1, List<Unlock> __2)
        {
            ScrollingMenu __instance = __0;
            __instance.numButtons = __2.Count;
            float x = __1 / (__instance.numButtons - __instance.numButtonsOnScreen + 1f);
            __instance.StartCoroutine(EnsureScrollbarValue(__instance, Mathf.Clamp01(1f - x)));

            if (__instance.menuType is "Challenges" or "FreeItems")
            {
                __instance.nuggetSlot.gameObject.SetActive(true);
            }
            else if (__instance.menuType == "Floors")
            {
                List<DisplayedUnlock> displayedUnlocks = UnlockMenuBindings.ResolveAll(GameController.gameController.sessionDataBig.floorUnlocks)
                                                                           .OrderBy(static d => d).ToList();
                CustomScrollingMenu menu = new CustomScrollingMenu(__instance, displayedUnlocks);

                foreach (DisplayedUnlock du in displayedUnlocks.ToList())
                    if (!du.IsAvailable)
                    {
                        displayedUnlocks.Remove(du);
                        __instance.buttonsData.Remove(du.ButtonData!);
                        __instance.numButtons--;
                    }
                    else UnlockMenuBindings.setMenu(du, menu);

                SetupAll(__instance.buttonsData);
            }
            else if (__instance.menuType == "Traits")
            {
                __instance.numButtons = __instance.smallTraitList.Count;
                BindMenu(__instance, __instance.smallTraitList);
            }
            else if (__instance.menuType is "RemoveTrait" or "ChangeTraitRandom" or "UpgradeTrait")
            {
                __instance.numButtons = __instance.customTraitList.Count;
                BindMenu(__instance, __instance.customTraitList);
            }
            return false;
        }
        private static void BindMenu(ScrollingMenu scrollingMenu, List<Unlock> unlocks)
        {
            List<DisplayedUnlock> displayedUnlocks = UnlockMenuBindings.ResolveAll(unlocks).OrderBy(static d => d).ToList();
            CustomScrollingMenu menu = new CustomScrollingMenu(scrollingMenu, displayedUnlocks);
            foreach (DisplayedUnlock du in displayedUnlocks)
                UnlockMenuBindings.setMenu(du, menu);
            SetupAll(scrollingMenu.buttonsData);
        }
        private static void SetupAll(List<ButtonData> buttons)
        {
            foreach (ButtonData buttonData in buttons)
                setupUnlocks(buttonData, buttonData.scrollingButtonUnlock);
        }
        private static IEnumerator EnsureScrollbarValue(ScrollingMenu menu, float value)
        {
            menu.scrollBar.value = value;
            yield return null;
            menu.scrollBar.value = value;
            yield return null;
            menu.scrollBar.value = value;
            yield return null;
            menu.scrollBar.value = value;
        }

        // RogueLibsPlugin.ScrollingMenu_SortUnlocks(ScrollingMenu __instance, List<Unlock> myUnlockList, List<Unlock> ___listUnlocks)
        public static void ScrollingMenu_SortUnlocks(ref List<Unlock> __1) => __1 = ResolvedOnly(__1);

        private static List<Unlock> ResolvedOnly(List<Unlock> unlocks)
        {
            if (unlocks is null) return unlocks!;
            List<DisplayedUnlock> resolved = UnlockMenuBindings.ResolveAll(unlocks).ToList();
            // the list can be the game's own (e.g. sessionDataBig's), so filter a copy
            return resolved.Count == unlocks.Count ? unlocks : resolved.ConvertAll(static du => du.Unlock);
        }

        // RogueLibsPlugin.ScrollingMenu_PushedButton(ScrollingMenu __instance, ButtonHelper myButton)
        public static bool ScrollingMenu_PushedButton(ScrollingMenu __0, ButtonHelper __1, ref bool __result)
        {
            if (__0.menuType is not null && __0.menuType.EndsWith("Configs", StringComparison.Ordinal)) return true;

            int index = __1.scrollingButtonNum;
            // Twitch rewards/disasters and the arcade character select have no unlocks: leave them to vanilla.
            if (index < 0 || index >= __0.buttonsData.Count || __0.buttonsData[index].GetHook() is not DisplayedUnlock)
            {
                __result = true;
                return false;
            }
            // In co-op, the same unlocks can be in several players' menus. Re-bind them to this one.
            UnlockMenuBindings.BindAll(__0.buttonsData);
            UnlockMenuBindings.Bind(__0.buttonsData[index]);
            return true;
        }

        // RogueLibsPlugin.ScrollingMenu_ShowDetails(ScrollingMenu __instance, ButtonHelper myButton)
        public static bool ScrollingMenu_ShowDetails(ScrollingMenu __0, ButtonHelper __1, ref bool __result)
        {
            ScrollingMenu __instance = __0;
            ButtonHelper myButton = __1;
            __result = true;
            if (__instance.agent is not null && myButton.scrollingButtonUnlock?.unlockType == "Trait" && __instance.agent.addedEndLevelTrait
                || !string.IsNullOrEmpty(myButton.scrollingButtonLevelFeeling) || !string.IsNullOrEmpty(myButton.scrollingButtonConfigName)
                || !string.IsNullOrEmpty(myButton.scrollingButtonAgentName))
                return false;
            // Twitch reward buttons carry an item and no unlock.
            if (myButton.scrollingButtonUnlock is null || myButton.scrollingButtonItem is not null || myButton.scrollingButtonTrait is not null)
                return false;
            DisplayedUnlock? du = UnlockMenuBindings.BindFor(__instance.buttonsData, myButton.scrollingButtonNum, myButton.scrollingButtonUnlock);
            if (du is null) return false;
            __result = false;

            bool show = du.IsUnlocked || du.Unlock.nowAvailable || du.Menu?.ShowLockedUnlocks == true;
            __instance.detailsTitle.text = show ? du.GetName() : "?????";
            __instance.detailsText.text = du.GetFancyDescription();
            __instance.detailsImage.sprite = show ? du.GetImage() : null;
            __instance.detailsImage.gameObject.SetActive(__instance.detailsImage.sprite is not null);

            // Gamepad scrolling fix
            __instance.curSelectedButtonNum = myButton.scrollingButtonNum;
            if (__instance.menuType == "FreeItems" && __instance.setInitialSelectedChildFreeItems)
                __instance.curSelectedChildFreeItems = myButton.scrollingButtonNum;
            __instance.curSelectedButton = myButton;
            if (__instance.agent?.controllerType == "Gamepad")
            {
                if (!__instance.refreshing)
                {
                    __instance.scrollBar.value = Mathf.Clamp01(1f - __instance.yOffset / ((__instance.numButtons - __instance.numButtonsOnScreen + 1f) * __instance.yOffset)
                                                                    * (myButton.scrollingButtonNum - (__instance.numButtonsOnScreen / 2f - 1f)));
                }
                if (__instance.menuType is "TraitUnlocks" or "Items" && !__instance.isPersonal)
                {
                    __instance.instructionText2.text
                        = myButton.scrollingButtonUnlock.unlocked
                              ? __instance.gc.nameDB.GetName(myButton.scrollingButtonUnlock.notActive ? "AddToPool" : "RemoveFromPool", "Interface")
                              : __instance.gc.nameDB.GetName("ScrollingInstr4", "Interface");
                }
            }
            return false;
        }

        // RogueLibsPlugin.CharacterCreation_SortUnlocks(CharacterCreation __instance, List<Unlock> myUnlockList, string unlockType)
        public static bool CharacterCreation_SortUnlocks(CharacterCreation __0, ref List<Unlock> __1, string __2, ref bool __result)
        {
            if (GetCCList(__0, __2) is null)
            {
                RogueLibsPlusPlugin.Log.LogWarning($"Unknown character creation list \"{__2}\"; it's left to vanilla.");
                __result = true;
                return false;
            }
            // RogueLibs lists BigQuests from sessionDataBig's originals, which have hooks; the other lists can hold copies
            if (__2 != UnlockTypes.BigQuest) __1 = ResolvedOnly(__1);
            return true;
        }

        private static List<Unlock>? GetCCList(CharacterCreation cc, string? unlockType) => unlockType switch
        {
            UnlockTypes.Item => cc.listUnlocksItems,
            UnlockTypes.Trait => cc.listUnlocksTraits,
            UnlockTypes.Ability => cc.listUnlocksAbilities,
            UnlockTypes.BigQuest => cc.listUnlocksBigQuests,
            _ => null,
        };
        private static List<ButtonData>? GetCCButtons(CharacterCreation cc, string? unlockType) => unlockType switch
        {
            UnlockTypes.Item => cc.buttonsDataItems,
            UnlockTypes.Trait => cc.buttonsDataTraits,
            UnlockTypes.Ability => cc.buttonsDataAbilities,
            UnlockTypes.BigQuest => cc.buttonsDataBigQuests,
            _ => null,
        };

        // RogueLibsPlugin.CharacterCreation_PushedButton(CharacterCreation __instance, ButtonHelper myButton)
        public static bool CharacterCreation_PushedButton(CharacterCreation __0, ButtonHelper __1, ref bool __result)
        {
            if (__0.selectedSpace == "Load") return true;

            List<ButtonData>? buttonsData = GetCCButtons(__0, __1.scrollingButtonUnlock?.unlockType);
            int index = __1.scrollingButtonNum;
            if (buttonsData is null || index < 0 || index >= buttonsData.Count || buttonsData[index].GetHook() is not DisplayedUnlock)
            {
                __result = true;
                return false;
            }
            UnlockMenuBindings.BindAll(buttonsData);
            UnlockMenuBindings.Bind(buttonsData[index]);
            return true;
        }

        // RogueLibsPlugin.CharacterCreation_ShowDetails(CharacterCreation __instance, ButtonHelper myButton)
        public static bool CharacterCreation_ShowDetails(CharacterCreation __0, ButtonHelper __1, ref bool __result)
        {
            CharacterCreation __instance = __0;
            ButtonHelper myButton = __1;
            if (__instance.loadMenu.gameObject.activeSelf || myButton.scrollingButtonUnlock is null)
            {
                __result = true;
                return false;
            }
            __result = false;
            string unlockType = myButton.scrollingButtonUnlock.unlockType;
            if (__instance.agent?.controllerType == "Gamepad" && !__instance.refreshing)
            {
                __instance.scrollBarLoad.value = Mathf.Clamp01(1f - __instance.yOffset / ((__instance.numButtonsLoad - __instance.numButtonsOnScreen + 1f) * __instance.yOffset)
                                                                    * (myButton.scrollingButtonNum - (__instance.numButtonsOnScreen / 2f - 1f)));

                Scrollbar? bar;
                float numButtons;
                if (unlockType == UnlockTypes.Item)
                { bar = __instance.scrollBarItems; numButtons = __instance.numButtonsItems; }
                else if (unlockType == UnlockTypes.Trait)
                { bar = __instance.scrollBarTraits; numButtons = __instance.numButtonsTraits; }
                else if (unlockType == UnlockTypes.Ability)
                { bar = __instance.scrollBarAbilities; numButtons = __instance.numButtonsAbilities; }
                else if (unlockType == UnlockTypes.BigQuest)
                { bar = __instance.scrollBarBigQuests; numButtons = __instance.numButtonsBigQuests; }
                else
                { bar = null; numButtons = 0f; }

                if (bar is not null)
                    bar.value = Mathf.Clamp01(1f - __instance.yOffset / ((numButtons - __instance.numButtonsOnScreen + 2f) * __instance.yOffset)
                                                   * (myButton.scrollingButtonNum - (__instance.numButtonsOnScreen / 2f - 1f)));
            }

            Image? image = null; Text? title = null; Text? text = null;
            if (unlockType == UnlockTypes.Item)
            { image = __instance.detailsImageItems; title = __instance.detailsTitleItems; text = __instance.detailsTextItems; }
            else if (unlockType == UnlockTypes.Trait)
            { image = __instance.detailsImageTraits; title = __instance.detailsTitleTraits; text = __instance.detailsTextTraits; }
            else if (unlockType == UnlockTypes.Ability)
            { image = __instance.detailsImageAbilities; title = __instance.detailsTitleAbilities; text = __instance.detailsTextAbilities; }
            else if (unlockType == UnlockTypes.BigQuest)
            { image = __instance.detailsImageBigQuests; title = __instance.detailsTitleBigQuests; text = __instance.detailsTextBigQuests; }

            DisplayedUnlock? du = UnlockMenuBindings.BindFor(GetCCButtons(__instance, unlockType), myButton.scrollingButtonNum, myButton.scrollingButtonUnlock);
            if (image is not null && du is not null)
            {
                bool show = du.IsUnlocked || du.Unlock.nowAvailable || du.Menu?.ShowLockedUnlocks == true;
                title!.text = show ? du.GetName() : "?????";
                text!.text = du.GetFancyDescription();
                image.sprite = show ? du.GetImage() : null;
                image.gameObject.SetActive(image.sprite is not null);
            }

            __instance.curSelectedButton = myButton;
            __instance.curSelectedButtonNum = myButton.scrollingButtonNum;
            return false;
        }
    }
}
