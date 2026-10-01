#nullable disable
using System;
using System.Collections.Generic;

// The pure half of FactionMatrix: entries, parsing and resolution, with no game or BepInEx references, so
// tools\FactionTests can compile and test it offline. FactionMatrix.cs holds the game side.
namespace RCK.Social
{
    internal sealed partial class FactionMatrix
    {
        private struct Side
        {
            public ulong Keys;
            public bool Player, Any;

            public bool Contains(ulong members, bool isPlayer) => (Keys & members) != 0 || (Player && isPlayer);
        }

        private struct Entry
        {
            public Side From, To;
            public FactionGrade Grade;
        }

        private readonly Entry[] entries;

        private FactionMatrix(Entry[] entries, ulong keyMask)
        {
            this.entries = entries;
            KeyMask = keyMask;
        }

        /// <summary>The factions any entry names, so membership is only checked for those.</summary>
        public ulong KeyMask { get; }

        /// <summary>The number of directed entries (<c>A&lt;&gt;B</c> counts twice).</summary>
        public int EntryCount => entries.Length;

        /// <summary>How a <paramref name="source"/> with these memberships treats a <paramref name="target"/>; None if no entry applies.</summary>
        public FactionGrade Resolve(ulong source, bool sourceIsPlayer, ulong target, bool targetIsPlayer)
        {
            FactionGrade named = FactionGrade.None, wildcard = FactionGrade.None;
            foreach (Entry e in entries)
            {
                if (e.To.Any)
                {
                    if (e.From.Contains(source, sourceIsPlayer) && !e.From.Contains(target, targetIsPlayer) && e.Grade > wildcard) wildcard = e.Grade;
                }
                else if (e.From.Any)
                {
                    if (e.To.Contains(target, targetIsPlayer) && !e.To.Contains(source, sourceIsPlayer) && e.Grade > wildcard) wildcard = e.Grade;
                }
                else if (e.From.Contains(source, sourceIsPlayer) && e.To.Contains(target, targetIsPlayer) && e.Grade > named)
                {
                    named = e.Grade;
                }
            }
            return named != FactionGrade.None ? named : wildcard;
        }

        /// <summary>
        ///   Builds a matrix from mutator bodies (the text after the prefix). <paramref name="keyIndex"/> maps a faction
        ///   name to its key index (-1 if unknown); each bad entry is passed to <paramref name="onError"/> with the
        ///   reason and skipped. Null if no entry is valid.
        /// </summary>
        internal static FactionMatrix Build(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var list = new List<Entry>();
            ulong mask = 0;
            foreach (string body in bodies)
            {
                foreach (string part in body.Split(';'))
                {
                    string text = part.Trim();
                    if (text.Length == 0) continue;
                    if (!TryParseEntry(text, keyIndex, out Side from, out Side to, out bool both, out FactionGrade grade, out string error))
                    {
                        onError?.Invoke(text, error);
                        continue;
                    }
                    list.Add(new Entry { From = from, To = to, Grade = grade });
                    if (both) list.Add(new Entry { From = to, To = from, Grade = grade });
                    mask |= from.Keys | to.Keys;
                }
            }
            return list.Count == 0 ? null : new FactionMatrix(list.ToArray(), mask);
        }

        private static bool TryParseEntry(string text, Func<string, int> keyIndex, out Side from, out Side to, out bool both, out FactionGrade grade, out string error)
        {
            from = to = default;
            both = false;
            grade = FactionGrade.None;
            int eq = text.LastIndexOf('=');
            if (eq <= 0) { error = "expected A>B=Relationship"; return false; }
            if (!FactionRules.TryParseGrade(text.Substring(eq + 1).Trim(), out grade)) { error = "unknown relationship (use " + FactionRules.GradeNames + ")"; return false; }

            string pair = text.Substring(0, eq);
            string left, right;
            bool reverse = false;
            int op = pair.IndexOf("<>", StringComparison.Ordinal);
            if (op >= 0)
            {
                both = true;
                left = pair.Substring(0, op);
                right = pair.Substring(op + 2);
            }
            else if ((op = pair.IndexOf('>')) >= 0)
            {
                left = pair.Substring(0, op);
                right = pair.Substring(op + 1);
            }
            else if ((op = pair.IndexOf('<')) >= 0)
            {
                reverse = true;
                left = pair.Substring(0, op);
                right = pair.Substring(op + 1);
            }
            else { error = "expected >, < or <> between the two sides"; return false; }

            if (!TryParseSide(left, keyIndex, out Side a, out error) || !TryParseSide(right, keyIndex, out Side b, out error)) return false;
            if (a.Any && b.Any) { error = "* on both sides is not supported"; return false; }
            from = reverse ? b : a;
            to = reverse ? a : b;
            error = null;
            return true;
        }

        private static bool TryParseSide(string text, Func<string, int> keyIndex, out Side side, out string error)
        {
            side = default;
            error = null;
            string trimmed = text.Trim();
            if (trimmed == "*")
            {
                side.Any = true;
                return true;
            }
            foreach (string token in trimmed.Split(','))
            {
                string t = token.Trim();
                if (t.Length == 0) continue;
                if (string.Equals(t, "Player", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Players", StringComparison.OrdinalIgnoreCase))
                {
                    side.Player = true;
                    continue;
                }
                int index = keyIndex(t);
                if (index < 0 || index > 63) { error = $"unknown faction \"{t}\""; return false; }
                side.Keys |= 1UL << index;
            }
            if (side.Keys == 0 && !side.Player) { error = "empty side"; return false; }
            return true;
        }
    }
}
