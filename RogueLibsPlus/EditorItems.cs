using System;
using System.Collections.Generic;
using System.Linq;
using RogueLibsCore;

namespace RogueLibsPlus
{
    /// <summary>
    ///   <para>Provides the level editor's custom item lists and checks item names that chunks refer to.</para>
    /// </summary>
    public static class EditorItems
    {
        /// <summary>
        ///   <para>Returns the names of the custom items shown in the level editor's item lists, sorted by name.</para>
        /// </summary>
        /// <returns>The names of the custom items that aren't hidden from the level editor.</returns>
        public static IEnumerable<string> GetCustomItems()
            => RogueFramework.ItemFactories.OfType<CustomItemFactory>()
                             .SelectMany(static f => f.Items())
                             .Where(static m => !m.IsHiddenInLevelEditor())
                             .Select(static m => m.Name)
                             .Distinct()
                             .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase);

        private static readonly object nonItemLock = new object();
        private static string[] nonItemPrefixes = Array.Empty<string>();
        /// <summary>
        ///   <para>Registers a prefix for strings that level data keeps in an object's or agent's item slots (its extra
        ///   vars) but that aren't items, for example text that a mod reads back from the object. Names that start with a
        ///   registered prefix are never spawned or added as items, and never reported as unknown items.</para>
        /// </summary>
        /// <param name="prefix">The prefix, compared case-sensitively.</param>
        /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="prefix"/> is empty.</exception>
        public static void RegisterNonItemPrefix(string prefix)
        {
            if (prefix is null) throw new ArgumentNullException(nameof(prefix));
            if (prefix.Length == 0) throw new ArgumentException("The prefix must not be empty.", nameof(prefix));
            lock (nonItemLock)
            {
                if (Array.IndexOf(nonItemPrefixes, prefix) != -1) return;
                string[] updated = new string[nonItemPrefixes.Length + 1];
                nonItemPrefixes.CopyTo(updated, 0);
                updated[nonItemPrefixes.Length] = prefix;
                nonItemPrefixes = updated;
            }
        }
        /// <summary>
        ///   <para>Determines whether <paramref name="name"/> starts with a prefix registered with <see cref="RegisterNonItemPrefix"/>.</para>
        /// </summary>
        /// <param name="name">The name from an item slot.</param>
        /// <returns><see langword="true"/>, if the name isn't an item; otherwise, <see langword="false"/>.</returns>
        public static bool IsNonItemName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string[] prefixes = nonItemPrefixes;
            for (int i = 0; i < prefixes.Length; i++)
                if (name!.StartsWith(prefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        private static readonly Dictionary<string, bool> knownItems = new Dictionary<string, bool>();
        private static readonly HashSet<string> warned = new HashSet<string>();
        // Hats and armour that agents start with (ObjectMultAgent.convertArmorHeadToInt, convertArmorToInt). Several are
        // neither unlockable nor in the item sprite list, but they're real items.
        private static readonly HashSet<string> wearables = new HashSet<string>(StringComparer.Ordinal)
        {
            "CopHat", "Cop2Hat", "DoctorHeadLamp", "Fedora", "FireHelmet", "GasMask", "HackerGlasses", "HardHat", "HatBlue",
            "HatRed", "Headphones", "MayorHat", "SlaveHelmet", "SoldierHelmet", "Sunglasses", "ThiefHat",
            "BraceletStrength", "BulletproofVest", "CodPiece", "FireproofSuit", "MayorBadge", "MoodRing",
        };
        /// <summary>
        ///   <para>Determines whether an item named <paramref name="itemName"/> exists, either in vanilla or in a loaded mod.</para>
        /// </summary>
        /// <param name="itemName">The item's name.</param>
        /// <returns><see langword="true"/>, if the item exists; otherwise, <see langword="false"/>.</returns>
        public static bool IsKnownItem(string? itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            if (knownItems.TryGetValue(itemName!, out bool known)) return known;
            bool? result = CheckItem(itemName!);
            if (result is null) return true; // too early to tell, so don't block it or cache it
            return knownItems[itemName!] = result.Value;
        }
        private static bool? CheckItem(string itemName)
        {
            if (itemName == VanillaItems.Money || wearables.Contains(itemName)) return true;
            foreach (IHookFactory factory in RogueFramework.ItemFactories)
            {
                if (factory is CustomItemFactory customFactory && ItemFactoryExtensions.CanListItems)
                {
                    if (customFactory.Contains(itemName)) return true;
                }
                else
                {
                    try
                    {
                        if (factory.TryCreateHook(new InvItem { invItemName = itemName }) is not null) return true;
                    }
                    catch (Exception e)
                    {
                        RogueLibsPlusPlugin.Log.LogWarning($"Item factory {factory.GetType()} threw while checking '{itemName}': {e.Message}");
                    }
                }
            }
            GameController? gc = GameController.gameController;
            if (gc is null || gc.gameResources is null || gc.sessionDataBig is null) return null;
            if (gc.gameResources.itemDic?.ContainsKey(itemName) == true) return true;
            if (gc.sessionDataBig.itemUnlocks?.Exists(u => u?.unlockName == itemName) == true) return true;
            try
            {
                string? name = gc.nameDB?.GetName(itemName, NameTypes.Item);
                if (!string.IsNullOrEmpty(name) && !name!.StartsWith("E_", StringComparison.Ordinal)) return true;
            }
            catch { /* treat as unknown */ }
            return false;
        }
        internal static void WarnUnknown(string itemName, string source)
        {
            if (warned.Add(itemName))
                RogueLibsPlusPlugin.Log.LogWarning($"Skipped unknown item '{itemName}' in {source}. Is the mod that adds it missing?");
        }
        internal static void WarnSkipped(string itemName, string message)
        {
            if (warned.Add(itemName)) RogueLibsPlusPlugin.Log.LogWarning(message);
        }
    }
}
