using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RogueLibsCore;

namespace RCK
{
    /// <summary>
    ///   Designer mode shows RCK's designer traits (for NPCs) and its player traits in the character creator. It is off by
    ///   default so players get a clean list. It is saved as <c>[General] DesignerEdition</c> in
    ///   <c>BepInEx\config\streetsofrogue.roguecontentkit.cfg</c>, and the "[RCK] Designer mode" button at the top of the
    ///   character creator's Traits list switches it.
    /// </summary>
    public static class DesignerMode
    {
        internal const string UnlockName = "RCK_DesignerMode";
        private static DesignerModeUnlock? button;

        /// <summary>True if designer mode is on.</summary>
        public static bool On => Rck.Config.DesignerEdition.Value;

        /// <summary>Switches designer mode, saves the setting and updates the character creator's trait list.</summary>
        public static void Set(bool on) => Rck.Config.DesignerEdition.Value = on;

        internal static void Initialize()
        {
            // Built before the game's session data exists, so the button's unlock never joins the game's trait lists.
            button = new DesignerModeUnlock();
            Rck.gc?.sessionDataBig?.traitUnlocksCharacterCreation?.Remove(button.Unlock);
            Rck.Config.DesignerEdition.SettingChanged += static (_, _) => Apply();
        }

        private static void Apply()
        {
            int changed = TraitRegistry.ApplyDesignerMode(On);
            Rck.Log.LogInfo($"Designer mode {(On ? "on" : "off")}: {changed} traits {(On ? "added to" : "removed from")} the character creator.");
            RckPlugin.UpdateMenuLine();
        }

        private static readonly MethodInfo? setMenu = AccessTools.PropertySetter(typeof(DisplayedUnlock), nameof(DisplayedUnlock.Menu));

        /// <summary>Puts the button after RogueLibs' "Clear All" at the top of the character creator's Traits list.</summary>
        internal static void AddButton(CharacterCreation cc)
        {
            if (button == null || setMenu == null) return;
            List<Unlock> list = cc.listUnlocksTraits;
            int removed = list.RemoveAll(u => ReferenceEquals(u, button.Unlock));
            if (list.Count == 0 || list[0].unlockName != "ClearAllTraits" || list[0].GetHook() is not DisplayedUnlock clearAll
                || clearAll.Menu == null)
            {
                cc.numButtonsTraits -= removed;
                return;
            }
            setMenu.Invoke(button, new object[] { clearAll.Menu });
            list.Insert(1, button.Unlock);
            cc.numButtonsTraits += 1 - removed;
        }
    }

    internal sealed class DesignerModeUnlock : TraitUnlock
    {
        public DesignerModeUnlock() : base(DesignerMode.UnlockName, true)
        {
            UnlockCost = 0;
            CharacterCreationCost = 0;
            LoadoutCost = 0;
        }

        public override bool IsAvailable { get; set; } = true;
        public override bool IsEnabled { get => false; set { } }
        public override bool IsUnlocked { get => true; set { } }

        public override string GetName() => "[RCK] Designer mode: " + (DesignerMode.On ? "<color=lime>On</color>" : "Off");

        public override string GetDescription()
            => "Designer mode lists RCK's designer traits (for NPCs in custom campaigns) and its player traits here. "
             + "Click to switch it " + (DesignerMode.On ? "off" : "on") + ". The list changes the next time you open the character creator.\n\n"
             + "Turn it on before you edit a character that uses designer traits: without it, the character creator drops them. "
             + "Playing campaigns doesn't need it.";

        // Never shown as selected: the character creator resets button highlights when it loads a character.
        public override void UpdateButton() => UpdateButton(false);

        public override void OnPushedButton()
        {
            DesignerMode.Set(!DesignerMode.On);
            PlaySound(VanillaAudio.ClickButton);
            UpdateButton();
            if (Menu is CustomCharacterCreation menu)
            {
                menu.CC.detailsTitleTraits.text = GetName();
                menu.CC.detailsTextTraits.text = GetFancyDescription();
            }
        }
    }

    [HarmonyPatch(typeof(CharacterCreation), nameof(CharacterCreation.SortUnlocks))]
    internal static class CharacterCreation_SortUnlocks_DesignerMode
    {
        // RogueLibs' prefix builds the list and skips the original; postfixes still run.
        private static void Postfix(CharacterCreation __instance, string unlockType)
        {
            if (unlockType != "Trait") return;
            try { DesignerMode.AddButton(__instance); }
            catch (Exception e) { Rck.Log.LogError($"Designer mode button: {e}"); }
        }
    }
}
