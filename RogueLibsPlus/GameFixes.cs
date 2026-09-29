using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RogueLibsCore;
using UnityEngine;

namespace RogueLibsPlus
{
    // Lists custom items in the level editor, and skips item names from mods that aren't loaded (and non-item strings
    // that level data keeps in item slots) instead of spawning broken items.
    internal static class LevelEditorFixes
    {
        public static void Resolve() { }

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(LevelEditor), nameof(LevelEditor.CreateItemList), Type.EmptyTypes),
                          prefix: new HarmonyMethod(typeof(LevelEditorFixes), nameof(BeginItemList)),
                          finalizer: new HarmonyMethod(typeof(LevelEditorFixes), nameof(EndItemList)));
            harmony.Patch(AccessTools.Method(typeof(LevelEditor), nameof(LevelEditor.CreateExtraVarsStringChestList), Type.EmptyTypes),
                          prefix: new HarmonyMethod(typeof(LevelEditorFixes), nameof(BeginItemList)),
                          finalizer: new HarmonyMethod(typeof(LevelEditorFixes), nameof(EndItemList)));
            harmony.Patch(AccessTools.Method(typeof(LevelEditor), nameof(LevelEditor.OpenObjectLoad),
                                             new Type[] { typeof(List<string>), typeof(List<string>), typeof(List<string>) }),
                          prefix: new HarmonyMethod(typeof(LevelEditorFixes), nameof(OpenObjectLoad)));

            harmony.Patch(AccessTools.Method(typeof(BasicItem), nameof(BasicItem.Spawn),
                                             new Type[] { typeof(SpawnerBasic), typeof(string), typeof(Vector2), typeof(Vector2), typeof(Chunk) }),
                          prefix: new HarmonyMethod(typeof(LevelEditorFixes), nameof(BasicItem_Spawn)));
            harmony.Patch(AccessTools.Method(typeof(InvDatabase), "AddItemReal", new Type[] { typeof(string) }),
                          prefix: new HarmonyMethod(typeof(LevelEditorFixes), nameof(InvDatabase_AddItemReal)));
        }

        private static bool listingEditorItems;
        public static void BeginItemList() => listingEditorItems = true;
        public static void EndItemList() => listingEditorItems = false;

        // numButtonsLoad is private, and Mono enforces field access at runtime, so it comes in through Harmony
        public static void OpenObjectLoad(List<string> dataList, List<string> dataList2, List<string> dataList3, ref float ___numButtonsLoad)
        {
            if (!listingEditorItems) return;
            listingEditorItems = false;
            try
            {
                List<string> target = dataList2.Contains(VanillaItems.Money) ? dataList2 : dataList;
                HashSet<string> present = new HashSet<string>(dataList.Concat(dataList2).Concat(dataList3));
                List<string> custom = EditorItems.GetCustomItems().Where(present.Add).ToList();
                if (custom.Count == 0) return;

                int index = target.LastIndexOf(VanillaItems.Money);
                target.InsertRange(index == -1 ? target.Count : index, custom);
                ___numButtonsLoad = dataList.Count + dataList2.Count + dataList3.Count;
            }
            catch (Exception e)
            {
                RogueLibsPlusPlugin.Log.LogError($"Could not add custom items to the level editor's list: {e}");
            }
        }

        public static bool BasicItem_Spawn(SpawnerBasic spawner, string itemName)
        {
            if (EditorItems.IsNonItemName(itemName))
            {
                spawner.spawned = true;
                return false;
            }
            if (EditorItems.IsKnownItem(itemName)) return true;
            EditorItems.WarnUnknown(itemName, "a chunk's item spawner");
            spawner.spawned = true;
            return false;
        }

        public static bool InvDatabase_AddItemReal(string randItem, ref InvItem __result)
        {
            bool nonItem = EditorItems.IsNonItemName(randItem);
            bool nugget = randItem == "Nugget";
            if (!nonItem && !nugget && EditorItems.IsKnownItem(randItem)) return true;
            // AddItem("Nugget") adds to the player's Nugget count and returns null, so vanilla would throw here and
            // stop the level loading. A Nugget can't be held in contents; it belongs on the floor.
            if (nugget) EditorItems.WarnSkipped(randItem, "Skipped a Nugget in a chest's or agent's contents: contents can't hold Nuggets. Place it on the floor instead.");
            else if (!nonItem) EditorItems.WarnUnknown(randItem, "a chest's or agent's contents");
            // the callers set a few fields on the result, so hand them an item that isn't in any inventory
            __result = new InvItem { invItemName = randItem, invItemCount = 0 };
            return false;
        }
    }

    // Object buttons with no interacting agent: vanilla (and every interaction provider) assumes there is one.
    internal static class ObjectButtonFixes
    {
        public static void Resolve() { }

        public static void Patch(Harmony harmony)
        {
            // with no interacting agent vanilla would throw on interactingAgent.worldSpaceGUI
            harmony.Patch(AccessTools.Method(typeof(PlayfieldObject), nameof(PlayfieldObject.ShowObjectButtons), Type.EmptyTypes),
                          prefix: new HarmonyMethod(typeof(ObjectButtonFixes), nameof(ShowObjectButtons)));
            harmony.Patch(AccessTools.Method(typeof(WorldSpaceGUI), nameof(WorldSpaceGUI.RefreshObjectButtons), new Type[] { typeof(PlayfieldObject) }),
                          prefix: new HarmonyMethod(typeof(ObjectButtonFixes), nameof(RefreshObjectButtons)));
        }

        public static bool ShowObjectButtons(PlayfieldObject __instance) => __instance.interactingAgent != null;

        // Vanilla RefreshObjectButtons2 waits a frame, then rebuilds and re-shows the object's buttons. If the interaction
        // ended during that frame, it would run DetermineButtons and ShowObjectButtons with no interacting agent (every
        // provider throws, ShowObjectButtons throws) or StopInteraction on an object that already stopped. Run vanilla's own
        // coroutine, but only continue past the frame wait while the object still has an interacting agent.
        public static bool RefreshObjectButtons(WorldSpaceGUI __instance, PlayfieldObject myObject)
        {
            __instance.StartCoroutine(GuardedRefreshObjectButtons(__instance.RefreshObjectButtons2(myObject), myObject));
            return false;
        }
        private static IEnumerator GuardedRefreshObjectButtons(IEnumerator vanilla, PlayfieldObject myObject)
        {
            if (!vanilla.MoveNext()) yield break;
            yield return vanilla.Current;
            if (myObject == null || myObject.interactingAgent == null) yield break;
            while (vanilla.MoveNext()) yield return vanilla.Current;
        }
    }
}
