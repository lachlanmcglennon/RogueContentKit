using System;
using System.Collections.Generic;

namespace RCK.Campaign
{
    internal enum GateLogic
    {
        And,
        Nand,
        Nor,
        Or,
        Xnor,
        Xor
    }

    /// <summary>
    ///   One parsed level gate. The exit opens when the label agents pass (by <see cref="Logic"/>, or at least
    ///   <see cref="Count"/> of them resolved) and every condition holds. Either part may be empty, not both.
    /// </summary>
    internal sealed class LevelGateRule
    {
        public HashSet<int> Labels { get; } = new HashSet<int>();
        public GateLogic Logic { get; set; } = GateLogic.And;
        /// <summary>At least this many label agents resolved; 0 uses <see cref="Logic"/>.</summary>
        public int Count { get; set; }
        /// <summary><c>Name=arg</c> conditions in written order, all ANDed.</summary>
        public List<KeyValuePair<string, string>> Conditions { get; } = new List<KeyValuePair<string, string>>();
        /// <summary>Text the host's players see once, the first time the whole gate holds; null for none.</summary>
        public string? Open { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    ///   The level-gate data after <c>[CCU]LevelGate::</c> or <c>[RCK]LevelGate::</c>: <c>;</c>-separated
    ///   <c>Key=value</c> pairs. Pure (no game types) so tools\FactionTests can run it.
    /// </summary>
    internal static class LevelGateRules
    {
        /// <summary>Conditions RCK.Campaign answers itself; the other modules register theirs through RckWorld.</summary>
        public const string Destroyed = "Destroyed";
        public const string Holding = "Holding";
        public static readonly string[] BuiltIn = { Destroyed, Holding };
        /// <summary>Conditions other modules register: Social (Routed, TurfTaken) and Quests (Quest).</summary>
        public static readonly string[] Registered = { "Routed", "TurfTaken", "Quest" };

        public const int MaxCount = 99;
        public const int MaxOpen = 120;
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Type", "Label", "Labels", "Switch", "Switches", "Logic", "Count", "Open",
        };

        /// <summary>False when the data isn't an Entry gate with at least one label or condition.</summary>
        public static bool TryParse(string? data, out LevelGateRule? rule)
        {
            rule = null;
            if (data == null) return false;
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>> conditions = new List<KeyValuePair<string, string>>();
            foreach (string part in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;
                if (Reserved.Contains(key)) fields[key] = value;
                else conditions.Add(new KeyValuePair<string, string>(key, value));
            }

            string type = fields.TryGetValue("Type", out string? typeValue) && typeValue != null ? typeValue : "Entry";
            if (!string.Equals(type, "Entry", StringComparison.OrdinalIgnoreCase)) return false;

            LevelGateRule r = new LevelGateRule();
            if ((fields.TryGetValue("Label", out string? labelValue) || fields.TryGetValue("Labels", out labelValue)) && labelValue != null)
            {
                foreach (string token in SplitLabels(labelValue))
                {
                    if (TryParseLabel(token, out int label)) r.Labels.Add(label);
                    else r.Warnings.Add($"label '{token}' isn't 1 to 4 or A to D");
                }
            }

            if ((fields.TryGetValue("Switch", out string? switchValue) || fields.TryGetValue("Switches", out switchValue)) && switchValue != null)
            {
                bool agent = false;
                foreach (string token in SplitLabels(switchValue))
                    if (string.Equals(token, "Agent", StringComparison.OrdinalIgnoreCase)) agent = true;
                // Only Agent switches exist for labels; a gate naming other kinds only was never ours to read.
                if (!agent && r.Labels.Count > 0)
                {
                    if (conditions.Count == 0) return false;
                    r.Warnings.Add("labels ignored: Switch doesn't list Agent");
                    r.Labels.Clear();
                }
            }

            r.Logic = ParseLogic(fields.TryGetValue("Logic", out string? logicValue) ? logicValue : "AND");
            if (fields.TryGetValue("Count", out string? countValue) && countValue != null)
            {
                if (!int.TryParse(countValue, out int count) || count < 1 || count > MaxCount)
                    r.Warnings.Add($"Count '{countValue}' isn't 1 to {MaxCount}");
                else if (r.Labels.Count == 0) r.Warnings.Add("Count ignored: no labels");
                else r.Count = count;
            }

            if (fields.TryGetValue("Open", out string? openValue) && openValue != null)
            {
                if (openValue.Length == 0) r.Warnings.Add("Open= has no text");
                else if (openValue.Length > MaxOpen)
                {
                    r.Warnings.Add($"Open text cut to {MaxOpen} characters");
                    r.Open = openValue.Substring(0, MaxOpen).TrimEnd();
                }
                else r.Open = openValue;
            }

            foreach (KeyValuePair<string, string> c in conditions)
            {
                if (c.Value.Length == 0) r.Warnings.Add($"{c.Key}= has no value");
                r.Conditions.Add(new KeyValuePair<string, string>(Canonical(c.Key), c.Value));
            }

            if (r.Labels.Count == 0 && r.Conditions.Count == 0) return false;
            rule = r;
            return true;
        }

        /// <summary>The known spelling of a condition name, else the name as written.</summary>
        public static string Canonical(string name)
        {
            foreach (string known in BuiltIn)
                if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return known;
            foreach (string known in Registered)
                if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return known;
            return name;
        }

        public static bool IsKnown(string name) => Array.IndexOf(BuiltIn, Canonical(name)) >= 0 || Array.IndexOf(Registered, Canonical(name)) >= 0;

        /// <summary>The label agents' verdict: at least <paramref name="count"/> resolved, else by <paramref name="logic"/>.</summary>
        public static bool Combine(IList<bool> values, GateLogic logic, int count)
        {
            if (values.Count == 0) return false;
            int trueCount = 0;
            foreach (bool v in values)
                if (v) trueCount++;
            if (count > 0) return trueCount >= count;
            bool any = trueCount > 0;
            bool all = trueCount == values.Count;
            return logic switch
            {
                GateLogic.Nand => !all,
                GateLogic.Nor => !any,
                GateLogic.Or => any,
                GateLogic.Xnor => trueCount != 1,
                GateLogic.Xor => trueCount == 1,
                _ => all
            };
        }

        /// <summary>Entries of a condition's value: <c>A+B</c> or <c>A,B</c>.</summary>
        public static List<string> SplitArg(string? value)
        {
            List<string> list = new List<string>();
            if (value == null) return list;
            foreach (string token in value.Split('+', ','))
            {
                string t = token.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        /// <summary><c>Briefcase</c>, <c>Briefcase*2</c> or <c>Briefcase x2</c>.</summary>
        public static bool TryParseItem(string? token, out string name, out int count)
        {
            name = (token ?? "").Trim();
            count = 1;
            int star = name.LastIndexOf('*');
            int x = name.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            int cut = star >= 0 ? star : x;
            if (cut >= 0)
            {
                string n = name.Substring(cut + (star >= 0 ? 1 : 2)).Trim();
                if (!int.TryParse(n, out count) || count < 1 || count > MaxCount) return false;
                name = name.Substring(0, cut).Trim();
            }
            return name.Length > 0 && name.IndexOf(' ') < 0;
        }

        public static bool TryParseLabel(string token, out int label)
        {
            if (int.TryParse(token, out label)) return label >= 1;
            if (token.Length == 1)
            {
                switch (char.ToUpperInvariant(token[0]))
                {
                    case 'A': label = 1; return true;
                    case 'B': label = 2; return true;
                    case 'C': label = 3; return true;
                    case 'D': label = 4; return true;
                }
            }
            label = 0;
            return false;
        }

        public static GateLogic ParseLogic(string? logic)
        {
            return (logic ?? "").Trim().ToUpperInvariant() switch
            {
                "NAND" => GateLogic.Nand,
                "NOR" => GateLogic.Nor,
                "OR" => GateLogic.Or,
                "XNOR" => GateLogic.Xnor,
                "XOR" => GateLogic.Xor,
                _ => GateLogic.And
            };
        }

        private static IEnumerable<string> SplitLabels(string value)
        {
            foreach (string token in value.Split(new[] { ',', '|', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }
    }
}
