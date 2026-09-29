using System;
using System.Collections.Generic;
using HarmonyLib;
using RogueLibsCore;
using RogueLibsPlus;
using UnityEngine;
using UnityEngine.UI;

namespace RCK.Objects
{
    public sealed class ObjectsModule : IRckModule
    {
        private static readonly HashSet<string> Investigateable = new HashSet<string>(RckData.InvestigateableObjects, StringComparer.Ordinal);
        private static readonly HashSet<string> Containers = new HashSet<string>(RckData.ContainerObjects, StringComparer.Ordinal);
        private static readonly HashSet<string> FireContainers = new HashSet<string>(RckData.FireParticleObjects, StringComparer.Ordinal);

        public string Name => "Objects";

        public void Initialize()
        {
            ButtonLabels.Register(typeof(CustomButtons));
            // investigate text lives in the object's extraVarString, which vanilla also feeds to the object's inventory
            EditorItems.RegisterNonItemPrefix(Rck.InvestigatePrefix);

            RogueInteractions.CreateProvider(static h =>
            {
                if (h.Helper.interactingFar || h.Object is not ObjectReal obj) return;

                if (TryGetInvestigateText(obj, out string text))
                {
                    // As vanilla signs and TalkAgent: keep the interaction open. StopInteraction would close the text
                    // straight away; MainGUI.HideBigImage stops the interaction when the player closes it.
                    h.AddButton(CustomButtons.Investigate, m =>
                    {
                        obj.ShowBigImage(text, string.Empty, null);
                        m.Agent.worldSpaceGUI?.HideObjectButtons();
                    });
                }

                if (!HasExistingContainerInteraction(h) && TryGetLooseContainerItem(obj, out string itemName) && CanSearch(obj, h.Agent))
                {
                    h.AddButton(CustomButtons.Search, m =>
                    {
                        GiveContainerItem(obj, m.Agent, itemName);
                        m.StopInteraction();
                    });
                }
            });
        }

        internal static bool IsInvestigateable(ObjectReal obj)
        {
            return Investigateable.Contains(obj.objectName);
        }

        internal static bool IsInvestigateableName(string objectName)
        {
            return Investigateable.Contains(objectName);
        }

        internal static bool IsContainerName(string objectName)
        {
            return Containers.Contains(objectName);
        }

        internal static bool TryGetInvestigateText(ObjectReal obj, out string text)
        {
            text = string.Empty;
            if (!IsInvestigateable(obj)) return false;
            string extra = obj.extraVarString ?? string.Empty;
            if (!extra.StartsWith(Rck.InvestigatePrefix, StringComparison.Ordinal)) return false;
            text = extra.Substring(Rck.InvestigatePrefix.Length);
            return text.Length > 0;
        }

        internal static bool TryGetLooseContainerItem(ObjectReal obj, out string itemName)
        {
            itemName = string.Empty;
            if (!Containers.Contains(obj.objectName)) return false;
            if (obj.objectInvDatabase != null && obj.chestReal) return false;
            string extra = obj.extraVarString ?? string.Empty;
            if (extra.Length == 0 || extra.StartsWith(Rck.InvestigatePrefix, StringComparison.Ordinal)) return false;
            if (extra == "Randomized" || extra == "None") return false;
            if (!EditorItems.IsKnownItem(extra))
            {
                if (WarnedUnknown.Add(extra))
                    Rck.Log.LogWarning($"{obj.objectName} holds unknown item '{extra}', so it can't be searched. Check the chunk's container item name.");
                return false;
            }
            itemName = extra;
            return true;
        }

        private static readonly HashSet<string> WarnedUnknown = new HashSet<string>(StringComparer.Ordinal);

        private static bool HasExistingContainerInteraction(SimpleInteractionProvider h)
        {
            return h.HasButton("Open") || h.HasButton("Search");
        }

        private static bool CanSearch(ObjectReal obj, Agent agent)
        {
            if (obj is Tube tube && tube.functional && tube.state != 0) return false;
            if (!FireContainers.Contains(obj.objectName) || !obj.functional) return true;
            return agent.statusEffects.hasTrait("ResistFire") || agent.statusEffects.hasStatusEffect("ResistFire");
        }

        private static void GiveContainerItem(ObjectReal obj, Agent agent, string itemName)
        {
            try
            {
                InvItem? probe = itemName == "Money" ? null : Probe(itemName);
                // A Nugget goes straight to the Nugget count (AddItem returns null) and needs no slot.
                bool nugget = probe != null && probe.itemType == "Nugget";
                // AddItem into a full inventory places nothing and says nothing, and the item would be lost. Refuse as
                // vanilla does and leave it in the container for later. (hasEmptySlotForItem adds Money on the spot.)
                if (probe != null && !nugget && !agent.inventory.hasEmptySlotForItem(probe))
                {
                    agent.inventory.PlayerFullResponse(agent);
                    return;
                }
                // A full item, as a vanilla chest holds it (InvDatabase.AddItemReal): a loaded gun, full durability or
                // charges, a normal stack. Money is a mid-tier vanilla chest roll; a Nugget is one Nugget.
                int count = itemName == "Money"
                    ? agent.inventory.MoneyAdjustForPlayers(UnityEngine.Random.Range(11, 26))
                    : nugget ? 1 : Math.Max(1, probe?.initCount ?? 1);
                InvItem item = agent.inventory.AddItem(itemName, count);
                if (item != null)
                {
                    item.startingChunk = obj.startingChunk;
                    item.ownerID = obj.owner;
                    if (obj.owner > 0) item.stealable = true;
                }
                obj.extraVarString = string.Empty;
                obj.hasInteracted = true;
                if (agent.isPlayer > 0)
                    agent.gc.spawnerMain.SpawnStatusText(agent, "ItemPickup", "Found: " + ItemDisplayName(agent, itemName));
            }
            catch (Exception ex)
            {
                Rck.Log.LogWarning($"Could not give container item '{itemName}' from {obj.objectName}: {ex.Message}");
            }
        }

        private static InvItem? Probe(string itemName)
        {
            try
            {
                var probe = new InvItem { invItemName = itemName, itemNetID = -1 };
                probe.ItemSetup(false);
                return probe;
            }
            catch
            {
                return null;
            }
        }

        private static string ItemDisplayName(Agent agent, string itemName)
        {
            try
            {
                string name = agent.gc.nameDB.GetName(itemName, "Item");
                return string.IsNullOrEmpty(name) ? itemName : name;
            }
            catch
            {
                return itemName;
            }
        }
    }
    /// <summary>Custom object buttons. Each needs a [ButtonLabel] or a vanilla Interface label (checked by tools\ButtonCheck).</summary>
    internal static class CustomButtons
    {
        [ButtonLabel("Investigate")] public const string Investigate = "Investigate";
        [ButtonLabel("Search")] public const string Search = "Search";
    }

    [HarmonyPatch(typeof(ObjectReal), "Start")]
    internal static class ObjectReal_Start_Containers
    {
        private static void Postfix(ObjectReal __instance)
        {
            if (ObjectsModule.TryGetInvestigateText(__instance, out _) ||
                ObjectsModule.TryGetLooseContainerItem(__instance, out _))
            {
                __instance.interactable = true;
            }
        }
    }

    internal static class LevelEditorObjectExtras
    {
        private static readonly AccessTools.FieldRef<LevelEditor, string> CurrentInterface =
            AccessTools.FieldRefAccess<LevelEditor, string>("currentInterface");
        private static readonly AccessTools.FieldRef<LevelEditor, InputField> TileNameObject =
            AccessTools.FieldRefAccess<LevelEditor, InputField>("tileNameObject");
        private static readonly AccessTools.FieldRef<LevelEditor, InputField> ExtraVarObject =
            AccessTools.FieldRefAccess<LevelEditor, InputField>("extraVarObject");
        private static readonly AccessTools.FieldRef<LevelEditor, InputField> ExtraVarStringObject =
            AccessTools.FieldRefAccess<LevelEditor, InputField>("extraVarStringObject");
        private static readonly AccessTools.FieldRef<LevelEditor, InputField> ExtraVarString2Object =
            AccessTools.FieldRefAccess<LevelEditor, InputField>("extraVarString2Object");
        private static readonly AccessTools.FieldRef<LevelEditor, InputField> ExtraVarString3Object =
            AccessTools.FieldRefAccess<LevelEditor, InputField>("extraVarString3Object");
        private static readonly AccessTools.FieldRef<LevelEditor, string> ScrollingMenuType =
            AccessTools.FieldRefAccess<LevelEditor, string>("scrollingMenuType");
        private static readonly AccessTools.FieldRef<LevelEditor, string> CurLongDescriptionText =
            AccessTools.FieldRefAccess<LevelEditor, string>("curLongDescriptionText");

        internal static bool IsObjects(LevelEditor editor) => CurrentInterface(editor) == "Objects";

        internal static string CurrentObjectName(LevelEditor editor) => TileNameObject(editor)?.text ?? string.Empty;

        internal static bool UsesInvestigateText(LevelEditor editor)
        {
            return IsObjects(editor) && ObjectsModule.IsInvestigateableName(CurrentObjectName(editor));
        }

        internal static bool UsesContainerItem(LevelEditor editor)
        {
            return IsObjects(editor) && ObjectsModule.IsContainerName(CurrentObjectName(editor));
        }

        internal static bool TryOpenCustomExtraString(LevelEditor editor)
        {
            if (UsesInvestigateText(editor))
            {
                editor.OpenLongDescription("Sign");
                return true;
            }
            if (UsesContainerItem(editor))
            {
                ScrollingMenuType(editor) = "LoadExtraVarsStringChest";
                editor.CreateExtraVarsStringChestList();
                return true;
            }
            return false;
        }

        internal static bool TrySetCustomExtraString(LevelEditor editor)
        {
            if (!UsesInvestigateText(editor)) return false;

            InputField field = ExtraVarStringObject(editor);
            string text = field?.text ?? string.Empty;
            string stored = text.Length == 0 ? string.Empty :
                text.StartsWith(Rck.InvestigatePrefix, StringComparison.Ordinal) ? text : Rck.InvestigatePrefix + text;

            foreach (LevelEditorTile selectedTile in editor.selectedTiles)
            {
                selectedTile.extraVarString = stored;
            }
            return true;
        }

        internal static void SyncCustomWidgets(LevelEditor editor)
        {
            if (!IsObjects(editor)) return;

            string objectName = CurrentObjectName(editor);
            if (ObjectsModule.IsInvestigateableName(objectName))
            {
                SetActive(ExtraVarObject(editor), false);
                InputField field = ExtraVarStringObject(editor);
                SetActive(field, true);
                SetActive(ExtraVarString2Object(editor), false);
                SetActive(ExtraVarString3Object(editor), false);

                string stored = SharedExtraVarString(editor);
                string text = stored.StartsWith(Rck.InvestigatePrefix, StringComparison.Ordinal)
                    ? stored.Substring(Rck.InvestigatePrefix.Length)
                    : string.Empty;
                if (field != null) field.text = text;
                CurLongDescriptionText(editor) = stored;
                SetHasText(field, text.Length > 0);
            }
            else if (ObjectsModule.IsContainerName(objectName))
            {
                SetActive(ExtraVarObject(editor), false);
                InputField field = ExtraVarStringObject(editor);
                SetActive(field, true);
                SetActive(ExtraVarString2Object(editor), false);
                SetActive(ExtraVarString3Object(editor), false);
                string item = SharedExtraVarString(editor);
                if (field != null)
                {
                    field.text = item;
                    editor.SetNameText(field, item, "Item");
                }
            }
        }

        private static string SharedExtraVarString(LevelEditor editor)
        {
            string value = string.Empty;
            bool hasValue = false;
            foreach (LevelEditorTile selectedTile in editor.selectedTiles)
            {
                if (selectedTile.tileName != CurrentObjectName(editor)) continue;
                string current = selectedTile.extraVarString ?? string.Empty;
                if (!hasValue)
                {
                    value = current;
                    hasValue = true;
                }
                else if (!string.Equals(value, current, StringComparison.Ordinal))
                {
                    return string.Empty;
                }
            }
            return value;
        }

        private static void SetActive(InputField? field, bool active)
        {
            if (field != null) field.gameObject.SetActive(active);
        }

        private static void SetHasText(InputField? field, bool hasText)
        {
            if (field == null) return;
            Transform nameText = field.transform.Find("NameText");
            if (nameText == null) return;
            Text label = nameText.GetComponent<Text>();
            if (label == null) return;
            label.text = hasText
                ? GameController.gameController.nameDB.GetName("HasText", "Interface")
                : string.Empty;
        }
    }

    [HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.PressedLoadExtraVarStringList))]
    internal static class LevelEditor_PressedLoadExtraVarStringList_RckObjects
    {
        private static bool Prefix(LevelEditor __instance)
        {
            return !LevelEditorObjectExtras.TryOpenCustomExtraString(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.SetExtraVarString))]
    internal static class LevelEditor_SetExtraVarString_RckObjects
    {
        private static bool Prefix(LevelEditor __instance)
        {
            return !LevelEditorObjectExtras.TrySetCustomExtraString(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.CloseLongDescription))]
    internal static class LevelEditor_CloseLongDescription_RckObjects
    {
        private static void Postfix(LevelEditor __instance)
        {
            LevelEditorObjectExtras.TrySetCustomExtraString(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditor), "UpdateInterface")]
    internal static class LevelEditor_UpdateInterface_RckObjects
    {
        private static void Postfix(LevelEditor __instance)
        {
            LevelEditorObjectExtras.SyncCustomWidgets(__instance);
        }
    }
}
