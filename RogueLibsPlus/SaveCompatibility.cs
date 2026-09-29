using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RogueLibsCore;

namespace RogueLibsPlus
{
    // "Continue" restores a player's items, traits and effects from SessionData, but those objects were saved without
    // their RogueLibs hooks (or with hooks that point at the old agent). Re-create or re-parent the hooks when the
    // game hands them back to the new agent, so custom items, traits and effects keep working after a Continue.
    internal static class SaveCompatibility
    {
        private static Action<Trait, StatusEffects> setupTraitHook = null!;
        private static Action<StatusEffect, StatusEffects> setupEffectHook = null!;
        // Without the preloader's fields, RogueLibs keeps each trait's and effect's parent in this table, and its
        // AssociateExtra only Adds, so re-parenting a hook would throw. Remove the old entry first.
        private static ConditionalWeakTable<object, object> extraLookup = null!;

        private static readonly FieldInfo[] invItemSnapshotFields = typeof(InvItem).GetFields(BindingFlags.Instance | BindingFlags.Public);
        private static readonly HashSet<string> invItemContextFields = new HashSet<string>
        {
            "__RogueLibsHooks",
            "gc",
            "gr",
            "database",
            "agent",
            "objectReal",
            "belongsToInventory",
            "particleSystem",
            "invInterface",
            "rnd",
            "itemModel",
        };

        public static void Resolve()
        {
            Type plugin = Fixes.Need(AccessTools.TypeByName("RogueLibsCore.RogueLibsPlugin"), "RogueLibsCore.RogueLibsPlugin");
            setupTraitHook = AccessTools.MethodDelegate<Action<Trait, StatusEffects>>(
                Fixes.Need(AccessTools.Method(plugin, "SetupTraitHook", new Type[] { typeof(Trait), typeof(StatusEffects) }), "RogueLibsPlugin.SetupTraitHook"));
            setupEffectHook = AccessTools.MethodDelegate<Action<StatusEffect, StatusEffects>>(
                Fixes.Need(AccessTools.Method(plugin, "SetupEffectHook", new Type[] { typeof(StatusEffect), typeof(StatusEffects) }), "RogueLibsPlugin.SetupEffectHook"));
            FieldInfo lookup = Fixes.Need(AccessTools.Field(typeof(HookSystem), "extraLookup"), "HookSystem.extraLookup");
            extraLookup = Fixes.Need(lookup.GetValue(null) as ConditionalWeakTable<object, object>, "HookSystem.extraLookup table");
        }

        public static void Patch(Harmony harmony)
        {
            Type t = typeof(SaveCompatibility);
            harmony.Patch(AccessTools.Method(typeof(SessionData), nameof(SessionData.RetrieveInventory3), new Type[] { typeof(Agent), typeof(int), typeof(Agent) }),
                          prefix: new HarmonyMethod(t, nameof(RetrieveInventory3)));
            harmony.Patch(AccessTools.Method(typeof(SessionData), nameof(SessionData.RetrieveStatusEffects3), new Type[] { typeof(Agent), typeof(int), typeof(Agent) }),
                          prefix: new HarmonyMethod(t, nameof(RetrieveStatusEffects3)));
        }

        public static void RetrieveInventory3(SessionData __instance, Agent myAgent, int playerNum)
        {
            if (myAgent?.inventory is null || __instance.invItemList is null || playerNum < 0 || playerNum >= __instance.invItemList.Length)
                return;

            List<InvItem> items = __instance.invItemList[playerNum];
            if (items is null) return;

            for (int i = 0; i < items.Count; i++)
                RehydrateItemHook(items[i], myAgent.inventory);
        }

        public static void RetrieveStatusEffects3(SessionData __instance, Agent myAgent, int playerNum)
        {
            if (myAgent?.statusEffects is null || playerNum < 0) return;

            if (__instance.traitList is not null && playerNum < __instance.traitList.Length)
            {
                List<Trait> traits = __instance.traitList[playerNum];
                if (traits is not null)
                    for (int i = 0; i < traits.Count; i++)
                        RehydrateTraitHook(traits[i], myAgent.statusEffects);
            }

            if (__instance.statusEffectList is not null && playerNum < __instance.statusEffectList.Length)
            {
                List<StatusEffect> effects = __instance.statusEffectList[playerNum];
                if (effects is not null)
                    for (int i = 0; i < effects.Count; i++)
                        RehydrateEffectHook(effects[i], myAgent.statusEffects);
            }
        }

        private static void SetContext(InvItem item, InvDatabase inventory)
        {
            item.agent = inventory.agent;
            item.objectReal = inventory.objectReal;
            item.database = inventory;
            item.invInterface = inventory.agent?.mainGUI?.invInterface ?? inventory.objectReal?.mainGUI?.invInterface;
        }

        private static void RehydrateItemHook(InvItem item, InvDatabase inventory)
        {
            if (item?.invItemName is null) return;

            SetContext(item, inventory);
            if (item.GetHookControllerIfExists()?.GetHook<CustomItem>() is not null) return;

            List<IHook> hooks = new List<IHook>();
            try
            {
                foreach (IHookFactory factory in RogueFramework.ItemFactories)
                {
                    IHook? hook = factory.TryCreateHook(item);
                    if (hook is not null) hooks.Add(hook);
                }
            }
            catch (Exception e)
            {
                LogError(e, "creating the hooks of", item.invItemName);
                return;
            }
            if (hooks.Count == 0) return;

            // a hook's Initialize can reset the item (counts, contents) to its defaults: keep the saved values
            object?[] snapshot = CaptureInvItemSnapshot(item);
            try
            {
                IHookController controller = item.GetHookController();
                for (int i = 0; i < hooks.Count; i++)
                    controller.AddHook(hooks[i]);
            }
            catch (Exception e)
            {
                LogError(e, "attaching the hooks of", item.invItemName);
            }
            finally
            {
                RestoreInvItemSnapshot(item, snapshot);
                SetContext(item, inventory);
            }
        }

        private static void RehydrateTraitHook(Trait trait, StatusEffects parent)
        {
            if (trait?.traitName is null) return;

            CustomTrait? existing = trait.GetHookControllerIfExists()?.GetHook<CustomTrait>();
            if (existing is not null)
            {
                if (trait.GetStatusEffects() != parent)
                {
                    try
                    {
                        if (!HookSystem.PatcherOptimizedGen1) extraLookup.Remove(trait);
                        HookSystem.SetStatusEffects(trait, parent);
                        existing.OnAdded();
                    }
                    catch (Exception e) { LogError(e, "re-adding trait", trait.traitName); }
                }
                return;
            }

            try { setupTraitHook(trait, parent); }
            catch (Exception e) { LogError(e, "restoring the hook of trait", trait.traitName); }
        }

        private static void RehydrateEffectHook(StatusEffect effect, StatusEffects parent)
        {
            if (effect?.statusEffectName is null) return;

            CustomEffect? existing = effect.GetHookControllerIfExists()?.GetHook<CustomEffect>();
            if (existing is not null)
            {
                if (effect.GetStatusEffects() != parent)
                {
                    try
                    {
                        if (!HookSystem.PatcherOptimizedGen1) extraLookup.Remove(effect);
                        HookSystem.SetStatusEffects(effect, parent);
                        existing.OnAdded();
                    }
                    catch (Exception e) { LogError(e, "re-adding effect", effect.statusEffectName); }
                }
                return;
            }

            try { setupEffectHook(effect, parent); }
            catch (Exception e) { LogError(e, "restoring the hook of effect", effect.statusEffectName); }
        }

        private static void LogError(Exception e, string action, string name)
            => RogueLibsPlusPlugin.Log.LogError($"Continue: error {action} '{name}': {e}");

        private static object?[] CaptureInvItemSnapshot(InvItem item)
        {
            object?[] values = new object?[invItemSnapshotFields.Length];
            for (int i = 0; i < invItemSnapshotFields.Length; i++)
            {
                FieldInfo field = invItemSnapshotFields[i];
                if (invItemContextFields.Contains(field.Name)) continue;

                object? value = field.GetValue(item);
                if (value is List<string> strings) value = new List<string>(strings);
                else if (value is List<int> ints) value = new List<int>(ints);
                values[i] = value;
            }
            return values;
        }

        private static void RestoreInvItemSnapshot(InvItem item, object?[] values)
        {
            for (int i = 0; i < invItemSnapshotFields.Length; i++)
            {
                FieldInfo field = invItemSnapshotFields[i];
                if (!invItemContextFields.Contains(field.Name)) field.SetValue(item, values[i]);
            }
        }
    }
}
