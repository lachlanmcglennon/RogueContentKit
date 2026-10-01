using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RogueLibsCore;

namespace RCK.Items
{
    /// <summary>
    ///   Gives the cut vanilla items that have art (a sprite, a name and usually a description) something to do. The game
    ///   defines them and campaigns can hand them out, but using them did nothing. Each fix turns itself off if a game
    ///   update starts handling the item.
    /// </summary>
    public sealed class ItemsModule : IRckModule
    {
        public string Name => "Items";

        public void Initialize()
        {
            Bfg.Initialize();
            GrapplingHook.Initialize();
            TripMine.Initialize();
            UsableItems.Initialize();
            VoodooDoll.Initialize();
            AugmentationCanister.Initialize();
            Clovers.Initialize();
            MoodRing.Initialize();
            Rag.Initialize();
        }
    }

    internal static class ItemText
    {
        internal static void Name(string id, string type, string english, string russian, string chinese)
        {
            try
            {
                RogueLibs.CreateCustomName(id, type, new CustomNameInfo { English = english, Russian = russian, Chinese = chinese });
            }
            catch (ArgumentException e) { Rck.Log.LogDebug($"{type} name '{id}' was already registered: {e.Message}"); }
        }

        internal static void Description(string item, string english, string russian, string chinese)
            => Name(item, NameTypes.Description, english, russian, chinese);
    }

    internal static class Vanilla
    {
        /// <summary>
        ///   True when the game's own <paramref name="method"/> compares against <paramref name="itemName"/>, i.e. a game
        ///   update now handles the item and RCK's fix should stay off.
        /// </summary>
        internal static bool Mentions(MethodBase? method, string itemName, string fixName)
        {
            if (method == null)
            {
                Rck.Log.LogWarning($"Could not find the game method RCK's {fixName} checks, so it stays on.");
                return false;
            }
            try
            {
                bool found = PatchProcessor.GetOriginalInstructions(method)
                    .Any(i => i.opcode == OpCodes.Ldstr && itemName.Equals(i.operand as string, StringComparison.Ordinal));
                if (found) Rck.Log.LogInfo($"The game now handles {itemName} in {method.DeclaringType?.Name}.{method.Name}; RCK's {fixName} is off.");
                return found;
            }
            catch (Exception e)
            {
                Rck.Log.LogWarning($"Could not read {method.DeclaringType?.Name}.{method.Name}, so RCK's {fixName} stays on: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>Shared checks for guns the vanilla <c>Gun.Shoot</c> "fires" without spawning anything.</summary>
    internal static class CutGun
    {
        /// <summary>The <paramref name="itemName"/> the vanilla body is about to "fire", or null. Mirrors Shoot's own checks.</summary>
        internal static InvItem? Pending(Gun gun, bool specialAbility, string itemName)
        {
            Agent agent = gun.agent;
            if (agent == null || agent.inventory == null) return null;
            if (GameController.gameController.testMe2 && agent.isPlayer == 0) return null;
            InvItem item = specialAbility ? agent.inventory.equippedSpecialAbility : agent.inventory.equippedWeapon;
            if (item == null || item.invItemName != itemName) return null;
            return item.invItemCount > 0 || (!agent.localPlayer && agent.isPlayer != 0) ? item : null;
        }

        /// <summary>True for the machine that moves <paramref name="agent"/>: its own client for players, the host for NPCs.</summary>
        internal static bool OwnsPhysics(Agent agent)
            => agent.isPlayer > 0 ? agent.localPlayer : GameController.gameController.serverPlayer;

        internal static bool IsFriendly(Agent a, Agent b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            try
            {
                relStatus rel = a.relationships.GetRelCode(b);
                return rel == relStatus.Aligned || rel == relStatus.Loyal || rel == relStatus.Submissive;
            }
            catch { return false; }
        }
    }

    /// <summary>Item-definition tweaks that make the cut items usable. Runs after the game's own setup.</summary>
    [HarmonyPatch(typeof(InvItem), nameof(InvItem.SetupDetails), typeof(bool))]
    internal static class InvItem_SetupDetails_CutItems
    {
        private static void Postfix(InvItem __instance)
        {
            switch (__instance.invItemName)
            {
                case GrapplingHook.ItemName:
                    if (GrapplingHook.Enabled) __instance.noCountText = true;
                    break;
                case TripMine.ItemName:
                    if (TripMine.Enabled) TripMine.Setup(__instance);
                    break;
                case VoodooDoll.ItemName:
                    if (VoodooDoll.Enabled)
                    {
                        if (!__instance.Categories.Contains("Usable")) __instance.Categories.Add("Usable");
                        if (__instance.itemValue < VoodooDoll.Value) __instance.itemValue = VoodooDoll.Value;
                    }
                    break;
                case Rag.ItemName:
                    if (Rag.Enabled) __instance.itemType = "Combine";
                    break;
            }
        }
    }
}
