using System;
using System.Collections.Generic;
using System.Reflection;
using RogueLibsCore;

namespace RCK
{
    /// <summary>Registers every RCK trait with RogueLibs so the names load in characters, the editor and saves.</summary>
    public static class TraitRegistry
    {
        private static readonly Dictionary<string, CustomName> descriptions = new Dictionary<string, CustomName>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TraitUnlock> unlocks = new Dictionary<string, TraitUnlock>(StringComparer.Ordinal);
        private static readonly MethodInfo createTrait = typeof(RogueLibs).GetMethod(nameof(RogueLibs.CreateCustomTrait))!;

        public static int Registered => unlocks.Count;
        public static int Failed { get; private set; }

        public static TraitUnlock? GetUnlock(string traitId) => unlocks.TryGetValue(traitId, out TraitUnlock u) ? u : null;

        internal static void RegisterAll()
        {
            foreach (RckTraitInfo info in RckData.Traits)
            {
                try { Register(info, DefaultDescription(info)); }
                catch (Exception e)
                {
                    Failed++;
                    Rck.Log.LogError($"Could not register trait {info.Id}: {e.InnerException?.Message ?? e.Message}");
                }
            }
        }

        internal static RckTraitInfo RegisterExtension(Type type, string folder, string display, string description,
            RckTraitKind kind, int ccCost, string[] cancellations)
        {
            if (!type.Name.StartsWith(Rck.ExtensionPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"Extension trait {type.Name} must start with {Rck.ExtensionPrefix}.");
            bool designer = kind != RckTraitKind.Player;
            var info = new RckTraitInfo(type, type.Name, kind, "Extension", folder, $"{Rck.ExtensionTag} {display}",
                true, "RCK", ccCost, 0, designer ? RckAvailability.DesignerEdition : RckAvailability.Always,
                !designer, designer, designer, null, cancellations, new string[0], new[] { "RckTrait" });
            RckData.AddExtension(info);
            Register(info, TraitDescriptions.Get(info.Id) ?? description);
            return info;
        }

        private static void Register(RckTraitInfo info, string description)
        {
            var builder = (TraitBuilder)createTrait.MakeGenericMethod(info.Type).Invoke(null, null);
            builder.WithName(new CustomNameInfo(info.Display));
            builder.WithDescription(new CustomNameInfo(description));

            var unlock = new TraitUnlock(info.Id, true);
            builder.WithUnlock(unlock);
            unlock.UnlockCost = info.UnlockCost;
            unlock.CharacterCreationCost = info.CharacterCreationCost;
            unlock.IsAvailable = info.InHead && info.IsAvailable;
            unlock.IsAvailableInCC = IsInCC(info, Rck.Config.DesignerEdition.Value);
            unlock.Unlock.cantLose = info.CantLose || info.Kind != RckTraitKind.Player;
            unlock.Unlock.cantSwap = info.CantSwap || info.Kind != RckTraitKind.Player;
            if (info.Upgrade != null) unlock.Upgrade = info.Upgrade;
            if (info.Cancellations.Length > 0) unlock.Cancellations.AddRange(info.Cancellations);

            descriptions[info.Id] = builder.Description!;
            unlocks[info.Id] = unlock;
        }

        private static bool IsInCC(RckTraitInfo info, bool designerMode) => info.InHead && info.AvailableInCC switch
        {
            RckAvailability.Always => true,
            RckAvailability.DesignerEdition => designerMode,
            _ => false,
        };

        /// <summary>
        ///   Shows or hides the designer-mode traits in the character creator. RogueLibs updates the game's character
        ///   creator list at once; an open character creator shows the change the next time it is opened.
        /// </summary>
        internal static int ApplyDesignerMode(bool designerMode)
        {
            int changed = 0;
            foreach (RckTraitInfo info in RckData.AllTraits)
            {
                if (info.AvailableInCC != RckAvailability.DesignerEdition || !unlocks.TryGetValue(info.Id, out TraitUnlock unlock)) continue;
                bool inCC = IsInCC(info, designerMode);
                if (unlock.IsAvailableInCC == inCC) continue;
                unlock.IsAvailableInCC = inCC;
                changed++;
            }
            return changed;
        }

        internal static void SetDescription(string traitId, string text)
        {
            if (descriptions.TryGetValue(traitId, out CustomName name)) name.English = text;
            else Rck.Log.LogWarning($"Describe: unknown trait {traitId}");
        }

        private static string DefaultDescription(RckTraitInfo info)
        {
            string? text = TraitDescriptions.Get(info.Id);
            if (text != null) return text;
            Rck.Log.LogWarning($"Trait {info.Id} has no description in RCK/Core/Data/trait-descriptions.json; using a generic one.");
            if (!info.InHead)
                return $"Legacy trait ({info.Folder}). Kept so older characters still load; it is converted on load.";
            return info.Kind == RckTraitKind.Player
                ? $"RCK player trait ({info.Folder})."
                : $"RCK designer trait ({info.Folder}). Give it to an NPC character; it has no effect on players.";
        }
    }
}
