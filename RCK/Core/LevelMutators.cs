using System;
using System.Collections.Generic;

namespace RCK
{
    /// <summary>Reads the running level's mutator strings (<c>gc.challenges</c>), where campaigns put RCK's settings.</summary>
    public static class LevelMutators
    {
        /// <summary>The level has the mutator <paramref name="mutator"/> exactly.</summary>
        public static bool Has(GameController? gc, string mutator)
            => gc != null && gc.challenges != null && gc.challenges.Contains(mutator);

        /// <summary>
        ///   The text after <paramref name="prefix"/> in every mutator that contains it, in order:
        ///   <c>[RCK]TurfWar::Expand=2</c> gives <c>Expand=2</c>.
        /// </summary>
        public static List<string> Bodies(GameController? gc, string prefix)
        {
            var bodies = new List<string>();
            if (gc == null || gc.challenges == null) return bodies;
            foreach (string c in gc.challenges)
            {
                int start = c == null ? -1 : c.IndexOf(prefix, StringComparison.Ordinal);
                if (start >= 0) bodies.Add(c!.Substring(start + prefix.Length));
            }
            return bodies;
        }

        /// <summary>
        ///   Logs an error when <paramref name="mutator"/> isn't one of RCK's generated mutators, so a renamed mutator
        ///   shows up at startup: <c>{label} {mutator} is not in the generated data.</c>
        /// </summary>
        public static bool Require(string mutator, string label)
        {
            if (Array.FindIndex(RckData.ExtensionMutators, m => m.Name == mutator) >= 0) return true;
            Rck.Log.LogError($"{label} {mutator} is not in the generated data.");
            return false;
        }
    }

    /// <summary>
    ///   A value built from the level's matching mutator strings, built again only when those strings change (a new
    ///   level, or a mutator added or removed).
    /// </summary>
    public sealed class MutatorCache<T>
    {
        private readonly Func<string, bool> matches;
        private readonly Func<List<string>, T> build;
        private readonly List<string> raw = new List<string>();
        private T value;

        /// <param name="matches">Picks the mutators that feed the value.</param>
        /// <param name="build">Builds the value from the matching mutators, in order. It must not keep the list.</param>
        /// <param name="initial">The value before any mutator matches.</param>
        public MutatorCache(Func<string, bool> matches, Func<List<string>, T> build, T initial)
        {
            this.matches = matches;
            this.build = build;
            value = initial;
        }

        /// <summary>The value for the running level.</summary>
        public T Current()
        {
            GameController gc = GameController.gameController;
            return Get(gc == null ? null : gc.challenges);
        }

        public T Get(List<string>? challenges)
        {
            int found = 0;
            bool same = true;
            if (challenges != null)
            {
                foreach (string c in challenges)
                {
                    if (c == null || !matches(c)) continue;
                    if (found >= raw.Count || !string.Equals(raw[found], c, StringComparison.Ordinal)) same = false;
                    found++;
                }
            }
            if (same && found == raw.Count) return value;

            raw.Clear();
            if (challenges != null)
            {
                foreach (string c in challenges)
                    if (c != null && matches(c)) raw.Add(c);
            }
            value = build(raw);
            return value;
        }
    }
}
