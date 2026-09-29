using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RCK
{
    /// <summary>
    ///   Fast trait lookups for agents. Vanilla <c>StatusEffects.hasTrait</c> walks the whole trait list; RCK systems
    ///   ask "does this agent have X" many times per frame, so the names are cached per agent and rebuilt when the
    ///   trait list changes.
    /// </summary>
    public static class AgentTraits
    {
        private sealed class Entry
        {
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
            public int Count = -1;
            public bool Dirty = true;
        }

        private static readonly ConditionalWeakTable<Agent, Entry> cache = new ConditionalWeakTable<Agent, Entry>();
        private static readonly IReadOnlyCollection<string> none = new string[0];

        public static void Invalidate(Agent? agent)
        {
            if (agent != null && cache.TryGetValue(agent, out Entry e)) e.Dirty = true;
        }

        /// <summary>All trait names the agent currently has (vanilla and custom).</summary>
        public static IReadOnlyCollection<string> Get(Agent? agent)
        {
            List<Trait>? list = agent?.statusEffects?.TraitList;
            if (list is null) return none;
            Entry e = cache.GetOrCreateValue(agent!);
            if (e.Dirty || e.Count != list.Count)
            {
                e.Names.Clear();
                foreach (Trait t in list)
                    if (t?.traitName != null) e.Names.Add(t.traitName);
                e.Count = list.Count;
                e.Dirty = false;
            }
            return e.Names;
        }

        public static bool Has(Agent? agent, string traitName)
            => Get(agent) is HashSet<string> set ? set.Contains(traitName) : false;

        public static bool HasAny(Agent? agent, params string[] traitNames)
        {
            IReadOnlyCollection<string> names = Get(agent);
            if (names.Count == 0) return false;
            var set = (HashSet<string>)names;
            foreach (string n in traitNames)
                if (set.Contains(n)) return true;
            return false;
        }

        /// <summary>The agent's traits that are one of <paramref name="candidates"/>, in the candidates' order.</summary>
        public static IEnumerable<string> Which(Agent? agent, IEnumerable<string> candidates)
        {
            IReadOnlyCollection<string> names = Get(agent);
            if (names.Count == 0) yield break;
            var set = (HashSet<string>)names;
            foreach (string n in candidates)
                if (set.Contains(n)) yield return n;
        }

        /// <summary>The agent's RCK traits whose folder is <paramref name="folder"/> or below it.</summary>
        public static IEnumerable<RckTraitInfo> InFolder(Agent? agent, string folder)
        {
            foreach (string n in Get(agent))
            {
                RckTraitInfo? info = RckData.FindTrait(n);
                if (info != null && (info.Folder == folder || info.Folder.StartsWith(folder + "/", StringComparison.Ordinal)))
                    yield return info;
            }
        }

        [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.AddTrait), typeof(string), typeof(bool), typeof(bool))]
        private static class AddTraitPatch
        {
            private static void Postfix(StatusEffects __instance) => Invalidate(__instance.agent);
        }

        [HarmonyPatch(typeof(StatusEffects), nameof(StatusEffects.RemoveTrait), typeof(string), typeof(bool))]
        private static class RemoveTraitPatch
        {
            private static void Postfix(StatusEffects __instance) => Invalidate(__instance.agent);
        }
    }
}
