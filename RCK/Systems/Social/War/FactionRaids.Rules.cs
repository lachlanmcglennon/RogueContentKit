#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RCK.Social
{
    /// <summary>One scheduled raid: <c>A&gt;B@T</c> or <c>A&gt;B@TxN</c>.</summary>
    internal sealed class RaidEntry
    {
        public int Raider, Target, At, Size;
        public string Text;
    }

    /// <summary>
    ///   The raid schedule format, pure so it can be tested outside the game: <c>[RCK]FactionRaid::</c> followed by
    ///   <c>;</c>-separated entries <c>A&gt;B@T</c> or <c>A&gt;B@TxN</c>, where A and B are faction numbers or keys, T is
    ///   seconds after the level loads and N the squad size.
    /// </summary>
    internal static class RaidRules
    {
        public const string Prefix = "[RCK]FactionRaid::";
        public const string Mutator = "RCK_Faction_Raids";
        public const int DefaultSize = 3, MaxSize = 6, MaxSeconds = 3600;
        public const int FirstMin = 90, FirstMax = 150, GapMin = 150, GapMax = 240, MaxAuto = 3;

        /// <summary>Parses mutator bodies (the text after the prefix); bad entries go to <paramref name="onError"/> and are skipped.</summary>
        public static List<RaidEntry> Parse(IEnumerable<string> bodies, Func<string, int> keyIndex, Action<string, string> onError)
        {
            var list = new List<RaidEntry>();
            foreach (string body in bodies)
            {
                if (body == null) continue;
                foreach (string raw in body.Split(';'))
                {
                    string text = raw.Trim();
                    if (text.Length == 0) continue;
                    if (TryParse(text, keyIndex, out RaidEntry entry, out string error)) list.Add(entry);
                    else onError?.Invoke(text, error);
                }
            }
            return list;
        }

        public static bool TryParse(string text, Func<string, int> keyIndex, out RaidEntry entry, out string error)
        {
            entry = null;
            int gt = text.IndexOf('>');
            if (gt <= 0)
            {
                error = "expected A>B@seconds";
                return false;
            }
            int at = text.IndexOf('@', gt + 1);
            if (at < 0)
            {
                error = "missing @seconds";
                return false;
            }
            string a = text.Substring(0, gt).Trim();
            string b = text.Substring(gt + 1, at - gt - 1).Trim();
            string when = text.Substring(at + 1).Trim();
            int size = DefaultSize;
            int x = when.IndexOfAny(new[] { 'x', 'X' });
            if (x >= 0)
            {
                if (!int.TryParse(when.Substring(x + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 1 || size > MaxSize)
                {
                    error = $"squad size must be 1 to {MaxSize}";
                    return false;
                }
                when = when.Substring(0, x).Trim();
            }
            if (!int.TryParse(when, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds > MaxSeconds)
            {
                error = $"seconds must be 0 to {MaxSeconds}";
                return false;
            }
            int raider = a.Length == 0 ? -1 : keyIndex(a);
            if (raider < 0)
            {
                error = $"unknown faction \"{a}\"";
                return false;
            }
            int target = b.Length == 0 ? -1 : keyIndex(b);
            if (target < 0)
            {
                error = $"unknown faction \"{b}\"";
                return false;
            }
            if (raider == target)
            {
                error = "a faction can't raid itself";
                return false;
            }
            entry = new RaidEntry { Raider = raider, Target = target, At = seconds, Size = size, Text = text };
            error = null;
            return true;
        }
    }
}
