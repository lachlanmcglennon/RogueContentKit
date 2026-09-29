using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HarmonyLib;
using RogueLibsCore;

namespace RogueLibsPlus
{
    /// <summary>
    ///   <para>Lists the items registered in a <see cref="CustomItemFactory"/>, which RogueLibs doesn't expose.</para>
    /// </summary>
    public static class ItemFactoryExtensions
    {
        private static readonly FieldInfo? itemsDictField = AccessTools.Field(typeof(CustomItemFactory), "itemsDict");
        private static FieldInfo? metadataField;

        /// <summary>
        ///   <para>Gets whether the running RogueLibs lets this class list a factory's items. If it doesn't,
        ///   <see cref="Items"/> is empty and <see cref="Contains"/> asks the factory to create a hook instead.</para>
        /// </summary>
        public static bool CanListItems => itemsDictField is not null && typeof(IDictionary).IsAssignableFrom(itemsDictField.FieldType);

        private static IDictionary? GetDictionary(CustomItemFactory factory)
            => CanListItems ? itemsDictField!.GetValue(factory) as IDictionary : null;
        private static CustomItemMetadata? GetMetadata(object? entry)
        {
            if (entry is null) return null;
            FieldInfo? field = metadataField;
            if (field is null || field.DeclaringType != entry.GetType())
                metadataField = field = AccessTools.Field(entry.GetType(), "Metadata");
            return field?.GetValue(entry) as CustomItemMetadata;
        }

        /// <summary>
        ///   <para>Gets the metadata of every custom item (and ability) registered in the <paramref name="factory"/>.</para>
        /// </summary>
        /// <param name="factory">The item factory.</param>
        /// <returns>The metadata of the registered items.</returns>
        public static IEnumerable<CustomItemMetadata> Items(this CustomItemFactory factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            IDictionary? dict = GetDictionary(factory);
            if (dict is null) return Array.Empty<CustomItemMetadata>();
            List<CustomItemMetadata> list = new List<CustomItemMetadata>(dict.Count);
            foreach (object? entry in dict.Values)
                if (GetMetadata(entry) is { } metadata) list.Add(metadata);
            return list;
        }
        /// <summary>
        ///   <para>Determines whether an item with the specified <paramref name="name"/> is registered in the <paramref name="factory"/>.</para>
        /// </summary>
        /// <param name="factory">The item factory.</param>
        /// <param name="name">The item's name.</param>
        /// <returns><see langword="true"/>, if the item is registered; otherwise, <see langword="false"/>.</returns>
        public static bool Contains(this CustomItemFactory factory, string? name)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            if (name is null) return false;
            IDictionary? dict = GetDictionary(factory);
            if (dict is not null) return dict.Contains(name);
            return ((IHookFactory)factory).TryCreateHook(new InvItem { invItemName = name }) is not null;
        }
        /// <summary>
        ///   <para>Gets the metadata of the item with the specified <paramref name="name"/>.</para>
        /// </summary>
        /// <param name="factory">The item factory.</param>
        /// <param name="name">The item's name.</param>
        /// <param name="metadata">The item's metadata, if found; otherwise, <see langword="null"/>.</param>
        /// <returns><see langword="true"/>, if the item is registered; otherwise, <see langword="false"/>.</returns>
        public static bool TryGetMetadata(this CustomItemFactory factory, string? name, [NotNullWhen(true)] out CustomItemMetadata? metadata)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            IDictionary? dict = name is null ? null : GetDictionary(factory);
            metadata = dict is not null && dict.Contains(name!) ? GetMetadata(dict[name!]) : null;
            return metadata is not null;
        }

        /// <summary>
        ///   <para>Determines whether the custom item is left out of the level editor's item lists: abilities, and items
        ///   marked with <see cref="HideInLevelEditorAttribute"/>.</para>
        /// </summary>
        /// <param name="metadata">The item's metadata.</param>
        /// <returns><see langword="true"/>, if the item is hidden; otherwise, <see langword="false"/>.</returns>
        public static bool IsHiddenInLevelEditor(this CustomItemMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));
            return typeof(CustomAbility).IsAssignableFrom(metadata.Type)
                || metadata.Type.IsDefined(typeof(HideInLevelEditorAttribute), true);
        }
    }

    /// <summary>
    ///   <para>Leaves the custom item out of the level editor's item lists (items on the ground, and chest and agent
    ///   contents). Abilities are always left out.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HideInLevelEditorAttribute : Attribute { }
}
