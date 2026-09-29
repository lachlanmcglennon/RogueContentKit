using System;
using System.Collections.Generic;
using System.Linq;

namespace RCK
{
    public enum RckTraitKind
    {
        /// <summary>A trait a level designer puts on an NPC in the character creator.</summary>
        Designer,
        /// <summary>A trait a player can take for their own character.</summary>
        Player,
        /// <summary>One of our own additions (<see cref="Rck.ExtensionPrefix"/>); not part of the CCU interface.</summary>
        Extension,
    }

    public enum RckAvailability
    {
        Always,
        /// <summary>Shown in the character creator only when the DesignerEdition config option is on.</summary>
        DesignerEdition,
        Never,
    }

    /// <summary>Registration data for one trait. The ID, display name and costs are the CCU interface.</summary>
    public sealed class RckTraitInfo
    {
        public RckTraitInfo(Type type, string id, RckTraitKind kind, string group, string folder, string? display,
            bool inHead, string firstVersion, int ccCost, int unlockCost, RckAvailability availableInCC, bool isAvailable,
            bool cantLose, bool cantSwap, string? upgrade, string[] cancellations, string[] rolls, string[] bases)
        {
            Type = type;
            Id = id;
            Kind = kind;
            Group = group;
            Folder = folder;
            Display = display ?? id.Replace('_', ' ');
            InHead = inHead;
            FirstVersion = firstVersion;
            CharacterCreationCost = ccCost;
            UnlockCost = unlockCost;
            AvailableInCC = availableInCC;
            IsAvailable = isAvailable;
            CantLose = cantLose;
            CantSwap = cantSwap;
            Upgrade = upgrade;
            Cancellations = cancellations;
            Rolls = rolls;
            Bases = bases;
        }

        public Type Type { get; }
        /// <summary>The trait name the game stores in characters and campaigns.</summary>
        public string Id { get; }
        public RckTraitKind Kind { get; }
        /// <summary>CCU's namespace group, e.g. "Loadout_Gun_Nut".</summary>
        public string Group { get; }
        /// <summary>System folder, e.g. "Hiring/Hire Type" or "Player Traits/Guns/Gun Nut".</summary>
        public string Folder { get; }
        public string Display { get; }
        /// <summary>False for legacy traits that newer CCU versions removed; they are registered hidden for old saves.</summary>
        public bool InHead { get; }
        public string FirstVersion { get; }
        public int CharacterCreationCost { get; }
        public int UnlockCost { get; }
        public RckAvailability AvailableInCC { get; }
        public bool IsAvailable { get; }
        public bool CantLose { get; }
        public bool CantSwap { get; }
        public string? Upgrade { get; }
        public string[] Cancellations { get; }
        /// <summary>Appearance roll values (e.g. hair or colour names), where CCU defined them.</summary>
        public string[] Rolls { get; }
        /// <summary>CCU's class chain, most specific first (e.g. T_GunNut, T_Loadout). Useful for grouping.</summary>
        public string[] Bases { get; }

        public bool HasBase(string baseName) => Array.IndexOf(Bases, baseName) >= 0;
        public override string ToString() => Id;
    }

    public sealed class RckGoalInfo
    {
        public RckGoalInfo(string goal, string? constName, string[] lists)
        {
            Goal = goal;
            ConstName = constName;
            Lists = lists;
        }

        /// <summary>The exact string stored in a spawner's defaultGoal.</summary>
        public string Goal { get; }
        public string? ConstName { get; }
        public string[] Lists { get; }
        public override string ToString() => Goal;
    }

    public sealed class RckNamedInfo
    {
        public RckNamedInfo(string name, string? display, bool inHead, string? description = null)
        {
            Name = name;
            Display = display ?? name;
            InHead = inHead;
            Description = description;
        }

        public string Name { get; }
        public string Display { get; }
        public bool InHead { get; }
        /// <summary>English description of an RCK addition; null for CCU names, which get a generic one.</summary>
        public string? Description { get; }
        public override string ToString() => Name;
    }

    public static partial class RckData
    {
        private static Dictionary<string, RckTraitInfo>? byId;
        private static readonly List<RckTraitInfo> extensions = new List<RckTraitInfo>();

        /// <summary>Every registered trait: the CCU interface plus our <see cref="Rck.ExtensionPrefix"/> additions.</summary>
        public static IReadOnlyDictionary<string, RckTraitInfo> TraitsById => ById;

        private static Dictionary<string, RckTraitInfo> ById
            => byId ??= Traits.ToDictionary(static t => t.Id, StringComparer.Ordinal);

        public static IReadOnlyList<RckTraitInfo> Extensions => extensions;

        public static IEnumerable<RckTraitInfo> AllTraits => Traits.Concat(extensions);

        internal static void AddExtension(RckTraitInfo info)
        {
            ById.Add(info.Id, info);
            extensions.Add(info);
        }

        public static RckTraitInfo? FindTrait(string id) => ById.TryGetValue(id, out RckTraitInfo t) ? t : null;

        /// <summary>Traits whose folder is <paramref name="folder"/> or below it, e.g. "Appearance" or "Combat/Gun Skill".</summary>
        public static IEnumerable<RckTraitInfo> TraitsIn(string folder)
            => AllTraits.Where(t => t.Folder == folder || t.Folder.StartsWith(folder + "/", StringComparison.Ordinal));

        public static IEnumerable<RckTraitInfo> TraitsWithBase(string baseName) => AllTraits.Where(t => t.HasBase(baseName));

        public static bool IsGoal(string? goal) => goal != null && Array.Exists(Goals, g => g.Goal == goal);
    }
}
