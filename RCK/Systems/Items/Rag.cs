using HarmonyLib;

namespace RCK.Items
{
    /// <summary>
    ///   The Rag was junk with no use. Combining it with a Beer or a Whiskey now makes a Molotov Cocktail.
    /// </summary>
    internal static class Rag
    {
        internal const string ItemName = "Rag";

        internal static bool Enabled { get; private set; }

        internal static void Initialize()
        {
            Enabled = !Vanilla.Mentions(AccessTools.Method(typeof(ItemFunctions), nameof(ItemFunctions.CombineItems)), ItemName, "Rag fix");
            if (!Enabled) return;
            ItemText.Description(ItemName,
                "Stuff it into a Beer or a Whiskey to make a Molotov Cocktail.",
                "Засуньте её в бутылку пива или виски, и получится коктейль Молотова.",
                "塞进一瓶啤酒或威士忌里，就能做成燃烧瓶。");
        }

        internal static bool IsBooze(InvItem item) => item.invItemName == "Beer" || item.invItemName == "Whiskey";
    }

    [HarmonyPatch(typeof(ItemFunctions), nameof(ItemFunctions.CombineItems))]
    internal static class ItemFunctions_CombineItems_Rag
    {
        private static bool Prefix(InvItem item, Agent agent, InvItem otherItem, string combineType, ref bool __result)
        {
            if (!Rag.Enabled || item == null || otherItem == null || agent == null) return true;
            InvItem drink;
            if (item.invItemName == Rag.ItemName && Rag.IsBooze(otherItem)) drink = otherItem;
            else if (otherItem.invItemName == Rag.ItemName && Rag.IsBooze(item)) drink = item;
            else return true;

            __result = true;
            if (combineType != "Combine") return false;

            InvDatabase inventory = agent.inventory;
            InvItem ownRag = inventory.FindItem(Rag.ItemName);
            InvItem ownDrink = inventory.FindItem(drink.invItemName);
            if (ownRag == null || ownDrink == null) return false;
            inventory.SubtractFromItemCount(ownDrink, 1);
            inventory.SubtractFromItemCount(ownRag, 1);
            agent.mainGUI.invInterface.HideDraggedItem();
            agent.mainGUI.invInterface.HideTarget();
            inventory.AddItemOrDrop("MolotovCocktail", 1);
            GameController.gameController.audioHandler.Play(agent, "CombineItem");
            return false;
        }
    }
}
