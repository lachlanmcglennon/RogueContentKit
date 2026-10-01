#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Pure quest-script logic with no game or BepInEx references, so tools\FactionTests can compile and test it offline.
namespace RCK.Quests
{
    internal enum QuestType { Kill, Retrieve, Destroy, Deliver, Talk }

    internal enum TargetKind { None, Label, Agent, AgentId, Leader, Faction, Object }

    internal enum RewardKind { Money, Item, XP, Recruit, Standing }

    /// <summary>What a quest map marker stands for. When one person is several, the higher value wins.</summary>
    internal enum MarkerKind { Running, Offer, Recipient, Target, Report }

    /// <summary>Who or what a stage is about: <c>Label:A</c>, <c>Agent:Big Tony</c>, <c>Leader:Rival</c>, <c>Object:Generator@Blahd</c>.</summary>
    internal sealed class QuestTarget
    {
        public TargetKind Kind;
        /// <summary>The label letter, agent name, faction key (or <c>Rival</c>) or object name.</summary>
        public string Value;
        /// <summary>An <c>Agent:#id</c> target's agent ID.</summary>
        public int Id;
        /// <summary>An <c>Object:</c> target's owner: a faction key, <c>Rival</c>, <c>Giver</c>, or null for anyone's.</summary>
        public string Owner;

        public bool IsAgentKind => Kind == TargetKind.Label || Kind == TargetKind.Agent || Kind == TargetKind.AgentId
            || Kind == TargetKind.Leader || Kind == TargetKind.Faction;

        public override string ToString()
        {
            switch (Kind)
            {
                case TargetKind.AgentId: return "Agent:#" + Id.ToString(CultureInfo.InvariantCulture);
                case TargetKind.Object: return "Object:" + Value + (Owner != null ? "@" + Owner : "");
                case TargetKind.None: return "";
                default: return Kind + ":" + Value;
            }
        }
    }

    internal sealed class QuestReward
    {
        public RewardKind Kind;
        public int Amount;
        public string Item;
    }

    /// <summary>One job in a giver's chain.</summary>
    internal sealed class QuestStage
    {
        public int Number;
        public string Id;
        public string Title;
        public QuestType Type;
        public QuestTarget Target = new QuestTarget();
        public string Item;
        /// <summary>0 when the script didn't say (the whole target set, 3 of a faction, 1 item).</summary>
        public int Count;
        public readonly List<QuestReward> Rewards = new List<QuestReward>();
        public bool AutoReport;
        public readonly List<string> After = new List<string>();
        public readonly Dictionary<string, string> Texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Text(string key) => Texts.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v) ? v : null;
    }

    internal sealed class QuestScript
    {
        public readonly List<QuestStage> Stages = new List<QuestStage>();
        /// <summary>What the giver says once the chain is over.</summary>
        public string Idle;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    ///   The quest script grammar. An NPC's Talk text starting with <c>rck-quest:::</c> is a chain of jobs, one per
    ///   stage, stages separated by a <c>---</c> line. Each stage is <c>Key: value</c> lines; a line whose text before
    ///   its first <c>:</c> isn't a key continues the previous value.
    /// </summary>
    internal static class QuestRules
    {
        public const string Prefix = "rck-quest:::";
        public const string RadiantMutator = "RCK_Radiant_Quests";
        public const string RadiantGiverTrait = "RCK_Radiant_Quest_Giver";
        public const string NoQuestsTrait = "RCK_No_Quests";
        public const string TargetTraitPrefix = "RCK_Quest_Target_";
        public const string GateSwitch = "Quest";
        public const int MaxText = 600;
        public const int MaxCount = 99;
        public const int MaxMoney = 100000;
        public const int DefaultFactionCount = 3;
        public const int MaxRadiantGivers = 4;
        public const int MaxMarkerTitle = 40;
        public const string DefaultDeliverItem = "Briefcase";

        public static readonly string[] Labels = { "A", "B", "C", "D" };

        /// <summary>Keys in the order the docs list them. <c>Rewards</c> is also accepted for <c>Reward</c>.</summary>
        public static readonly string[] Keys =
        {
            "Id", "Title", "Type", "Target", "Item", "Count", "Reward", "Report", "After",
            "Offer", "Accept", "Remind", "Done", "Fail", "Target text", "Wait", "Idle",
        };

        /// <summary>The keys holding text the player reads (normalized).</summary>
        public static readonly string[] TextKeys = { "offer", "accept", "remind", "done", "fail", "targettext", "wait", "idle" };

        public static readonly string[] Placeholders = { "giver", "faction", "rival", "target", "item", "count", "reward", "objective" };

        public static readonly Dictionary<string, QuestType> TypeNames = new Dictionary<string, QuestType>(StringComparer.OrdinalIgnoreCase)
        {
            { "Kill", QuestType.Kill }, { "Neutralize", QuestType.Kill }, { "Hit", QuestType.Kill },
            { "Retrieve", QuestType.Retrieve }, { "Fetch", QuestType.Retrieve }, { "Steal", QuestType.Retrieve },
            { "Destroy", QuestType.Destroy }, { "Sabotage", QuestType.Destroy },
            { "Deliver", QuestType.Deliver },
            { "Talk", QuestType.Talk }, { "Message", QuestType.Talk },
        };

        /// <summary>Items a radiant Recover, Pickpocket, Courier or Parcel job uses; all vanilla quest items.</summary>
        public static readonly string[] QuestItems =
        {
            "Blueprints", "Briefcase", "CircuitBoard", "Evidence", "HardDrive", "IncriminatingPhoto", "MacguffinMuffin",
            "SignedBaseball", "Tooth", "Will",
        };

        private static readonly Dictionary<string, string> keyMap = BuildKeyMap();

        private static Dictionary<string, string> BuildKeyMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string k in Keys) map[Normalize(k)] = Normalize(k);
            map["rewards"] = "reward";
            return map;
        }

        /// <summary>Lowercase, without spaces, underscores and hyphens: <c>Target_Text</c> → <c>targettext</c>.</summary>
        public static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (c != ' ' && c != '_' && c != '-' && c != '\t') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static string KeyName(string normalized)
        {
            foreach (string k in Keys)
                if (Normalize(k) == normalized) return k;
            return normalized;
        }

        public static bool IsScript(string text)
            => text != null && text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        // ---- Parsing ----

        /// <summary>
        ///   Parses a quest script (with or without the prefix). Invalid stages are dropped and reported in
        ///   <see cref="QuestScript.Errors"/>. <paramref name="resolveFaction"/> turns a faction name into its key (null
        ///   when unknown); without it faction names are kept as written.
        /// </summary>
        public static QuestScript Parse(string text, Func<string, string> resolveFaction = null)
        {
            var script = new QuestScript();
            if (text == null) return script;
            string body = text.TrimStart();
            if (body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) body = body.Substring(Prefix.Length);
            string[] lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var values = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            StringBuilder idle = null;
            StringBuilder last = null;
            int stageNo = 1, stageLine = 1;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Finish()
            {
                if (values.Count > 0)
                {
                    QuestStage stage = BuildStage(values, stageNo, stageLine, ids, script, resolveFaction);
                    if (stage != null) script.Stages.Add(stage);
                    stageNo++;
                }
                values.Clear();
                last = null;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length >= 3 && trimmed.Trim('-').Length == 0)
                {
                    Finish();
                    continue;
                }
                int colon = line.IndexOf(':');
                string key = null;
                if (colon > 0 && keyMap.TryGetValue(Normalize(line.Substring(0, colon)), out string k)) key = k;
                if (key != null)
                {
                    var value = new StringBuilder(line.Substring(colon + 1).Trim());
                    if (key == "idle")
                    {
                        idle = value;
                    }
                    else
                    {
                        if (values.ContainsKey(key))
                            script.Warnings.Add($"stage {stageNo} line {i + 1}: {KeyName(key)} is given twice; the later one wins");
                        if (values.Count == 0) stageLine = i + 1;
                        values[key] = value;
                    }
                    last = value;
                }
                else if (last != null)
                {
                    last.Append('\n').Append(line.TrimEnd());
                }
                else if (trimmed.Length > 0)
                {
                    script.Warnings.Add($"stage {stageNo} line {i + 1}: text before the first key is ignored");
                }
            }
            Finish();

            if (idle != null)
            {
                script.Idle = idle.ToString().Trim();
                CheckText(script.Idle, "Idle", "script", script.Warnings);
            }
            foreach (QuestStage s in script.Stages)
                foreach (string after in s.After)
                    if (!ids.Contains(after))
                        script.Warnings.Add($"stage {s.Number}: After names {after}, which isn't in this script (fine if another giver's job has that Id)");
            return script;
        }

        private static QuestStage BuildStage(Dictionary<string, StringBuilder> raw, int number, int line, HashSet<string> ids,
            QuestScript script, Func<string, string> resolveFaction)
        {
            string where = $"stage {number} (line {line})";
            var errors = new List<string>();
            var stage = new QuestStage { Number = number };
            string Get(string key) => raw.TryGetValue(key, out StringBuilder sb) ? sb.ToString().Trim() : null;
            string OneLine(string key)
            {
                string v = Get(key);
                if (v != null && v.IndexOf('\n') >= 0) errors.Add($"{KeyName(key)} must fit on one line");
                return v;
            }

            string type = OneLine("type");
            bool typeOk = false;
            if (string.IsNullOrEmpty(type)) errors.Add("no Type (Kill, Retrieve, Destroy, Deliver or Talk)");
            else if (!TypeNames.TryGetValue(type, out stage.Type)) errors.Add($"unknown Type {type} (Kill, Retrieve, Destroy, Deliver or Talk)");
            else typeOk = true;

            stage.Title = OneLine("title");
            if (string.IsNullOrEmpty(stage.Title)) stage.Title = DefaultTitle(stage.Type);

            string target = OneLine("target");
            bool targetOk = true;
            if (!string.IsNullOrEmpty(target))
            {
                if (TryParseTarget(target, resolveFaction, out QuestTarget t, out string error)) stage.Target = t;
                else
                {
                    errors.Add(error);
                    targetOk = false;
                }
            }

            stage.Item = OneLine("item");
            if (stage.Item != null && stage.Item.Length == 0) stage.Item = null;
            if (stage.Item != null && stage.Item.IndexOf(' ') >= 0) errors.Add($"Item {stage.Item} is not an item name (no spaces: Briefcase, CircuitBoard)");

            string count = OneLine("count");
            if (!string.IsNullOrEmpty(count))
            {
                if (!int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > MaxCount)
                    errors.Add($"Count {count} is not a whole number from 1 to {MaxCount}");
                else stage.Count = n;
            }

            string rewards = OneLine("reward");
            if (!string.IsNullOrEmpty(rewards) && !TryParseRewards(rewards, stage.Rewards, out string rewardError)) errors.Add(rewardError);

            string report = OneLine("report");
            if (!string.IsNullOrEmpty(report))
            {
                if (string.Equals(report, "Auto", StringComparison.OrdinalIgnoreCase)) stage.AutoReport = true;
                else if (!string.Equals(report, "Giver", StringComparison.OrdinalIgnoreCase)) errors.Add($"Report {report} is not Giver or Auto");
            }

            string after = OneLine("after");
            if (!string.IsNullOrEmpty(after))
                foreach (string id in after.Split(',', '+'))
                    if (id.Trim().Length > 0) stage.After.Add(id.Trim());

            foreach (string key in TextKeys)
            {
                if (key == "idle") continue;
                string v = Get(key);
                if (v == null) continue;
                stage.Texts[key] = v;
                CheckText(v, KeyName(key), where, script.Warnings);
            }

            // What each type needs (once the type and target themselves are valid).
            QuestTarget tg = stage.Target;
            switch (typeOk && targetOk ? stage.Type : (QuestType)(-1))
            {
                case QuestType.Kill:
                    if (tg.Kind == TargetKind.None) errors.Add("a Kill needs a Target (Label, Agent, Leader or Faction)");
                    else if (!tg.IsAgentKind) errors.Add("a Kill's Target must be people (Label, Agent, Leader or Faction), not an Object");
                    break;
                case QuestType.Destroy:
                    if (tg.Kind != TargetKind.Object) errors.Add("a Destroy needs an Object Target (Object:Generator@Blahd)");
                    break;
                case QuestType.Retrieve:
                    if (stage.Item == null) errors.Add("a Retrieve needs an Item");
                    if (stage.AutoReport) errors.Add("a Retrieve is always reported to the giver (Report: Auto isn't allowed)");
                    break;
                case QuestType.Deliver:
                case QuestType.Talk:
                    if (tg.Kind == TargetKind.None) errors.Add($"a {stage.Type} needs a Target (Label, Agent, Leader or Faction)");
                    else if (!tg.IsAgentKind) errors.Add($"a {stage.Type}'s Target must be a person, not an Object");
                    if (stage.Type == QuestType.Deliver && stage.Item == null) stage.Item = DefaultDeliverItem;
                    break;
            }
            if (stage.Item != null && (stage.Type == QuestType.Kill || stage.Type == QuestType.Destroy || stage.Type == QuestType.Talk))
                script.Warnings.Add($"{where}: Item is ignored for a {stage.Type}");
            if (stage.Count > 0 && stage.Type == QuestType.Talk)
                script.Warnings.Add($"{where}: Count is ignored for a Talk");
            if (stage.Text("targettext") != null && stage.Type != QuestType.Deliver && stage.Type != QuestType.Talk)
                script.Warnings.Add($"{where}: Target text is only shown for a Deliver or a Talk");

            string explicitId = OneLine("id");
            if (!string.IsNullOrEmpty(explicitId))
            {
                if (explicitId.IndexOfAny(new[] { ',', '+', ' ', '\t', '=' }) >= 0) errors.Add($"Id {explicitId} may not contain spaces, commas, + or =");
                else if (ids.Contains(explicitId)) errors.Add($"Id {explicitId} is already used by an earlier stage");
                stage.Id = explicitId;
            }
            else
            {
                string baseId = Slug(stage.Title);
                stage.Id = baseId;
                for (int n = 2; ids.Contains(stage.Id); n++) stage.Id = baseId + "-" + n.ToString(CultureInfo.InvariantCulture);
            }

            if (errors.Count > 0)
            {
                foreach (string e in errors) script.Errors.Add($"{where}: {e}; stage dropped");
                return null;
            }
            ids.Add(stage.Id);
            return stage;
        }

        private static void CheckText(string text, string key, string where, List<string> warnings)
        {
            if (text.Length > MaxText) warnings.Add($"{where}: {key} is {text.Length} characters; only {MaxText} fit and the rest is cut");
            foreach (string p in PlaceholdersIn(text))
                if (Array.IndexOf(Placeholders, p.ToLowerInvariant()) < 0)
                    warnings.Add($"{where}: {key} uses unknown placeholder {{{p}}}");
        }

        /// <summary>The <c>{name}</c> placeholders in <paramref name="text"/> (letters only between the braces).</summary>
        public static IEnumerable<string> PlaceholdersIn(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            int i = 0;
            while ((i = text.IndexOf('{', i)) >= 0)
            {
                int end = text.IndexOf('}', i + 1);
                if (end < 0) yield break;
                string name = text.Substring(i + 1, end - i - 1);
                bool letters = name.Length > 0 && IsLetters(name);
                if (letters) yield return name;
                i = letters ? end + 1 : i + 1;
            }
        }

        public static bool TryParseTarget(string text, Func<string, string> resolveFaction, out QuestTarget target, out string error)
        {
            target = new QuestTarget();
            error = null;
            const string help = "(Label:A, Agent:Name, Leader:Blahd, Faction:Rival or Object:Generator@Blahd)";
            int colon = text.IndexOf(':');
            string value = colon > 0 ? text.Substring(colon + 1).Trim() : "";
            if (colon <= 0 || value.Length == 0)
            {
                error = $"Target {text} is not Kind:value {help}";
                return false;
            }
            string kind = Normalize(text.Substring(0, colon));
            switch (kind)
            {
                case "label":
                    string letter = value.ToUpperInvariant();
                    if (Array.IndexOf(Labels, letter) < 0) { error = $"Target label {value} is not A, B, C or D"; return false; }
                    target.Kind = TargetKind.Label;
                    target.Value = letter;
                    return true;
                case "agent":
                    if (value[0] == '#')
                    {
                        if (!int.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                        {
                            error = $"Target {text} has a bad agent ID";
                            return false;
                        }
                        target.Kind = TargetKind.AgentId;
                        target.Id = id;
                        return true;
                    }
                    target.Kind = TargetKind.Agent;
                    target.Value = value;
                    return true;
                case "leader":
                case "faction":
                    target.Kind = kind == "leader" ? TargetKind.Leader : TargetKind.Faction;
                    return TryFaction(value, false, resolveFaction, out target.Value, out error);
                case "object":
                    int at = value.IndexOf('@');
                    target.Kind = TargetKind.Object;
                    target.Value = (at >= 0 ? value.Substring(0, at) : value).Trim();
                    if (target.Value.Length == 0) { error = $"Target {text} names no object"; return false; }
                    if (at < 0) return true;
                    return TryFaction(value.Substring(at + 1).Trim(), true, resolveFaction, out target.Owner, out error);
                default:
                    error = $"Target {text} has an unknown kind {help}";
                    return false;
            }
        }

        private static bool TryFaction(string name, bool allowGiver, Func<string, string> resolveFaction, out string key, out string error)
        {
            key = null;
            error = null;
            if (name.Length == 0) { error = "a faction is missing in the Target"; return false; }
            if (string.Equals(name, "Rival", StringComparison.OrdinalIgnoreCase)) { key = "Rival"; return true; }
            if (allowGiver && string.Equals(name, "Giver", StringComparison.OrdinalIgnoreCase)) { key = "Giver"; return true; }
            if (resolveFaction == null) { key = name; return true; }
            key = resolveFaction(name);
            if (key == null) error = $"Target names unknown faction {name}";
            return key != null;
        }

        private static readonly Regex itemCount = new Regex(@"^(?<name>.*?\S)\s*(?:\*\s*|\s[xX×]\s*)(?<n>\d+)$", RegexOptions.CultureInvariant);

        /// <summary>
        ///   Comma-separated rewards: <c>$150</c> or <c>Money:150</c>, <c>Item:Revolver</c> (<c>Item:Banana x3</c>,
        ///   <c>Item:Banana*3</c>), <c>XP</c>, <c>Recruit</c>, <c>Standing</c>.
        /// </summary>
        public static bool TryParseRewards(string text, List<QuestReward> rewards, out string error)
        {
            error = null;
            foreach (string raw in text.Split(','))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                int colon = p.IndexOf(':');
                string head = Normalize(colon > 0 ? p.Substring(0, colon) : p);
                string arg = colon > 0 ? p.Substring(colon + 1).Trim() : "";
                if (p[0] == '$')
                {
                    head = "money";
                    arg = p.Substring(1).Trim();
                }
                switch (head)
                {
                    case "money":
                        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int money) || money < 1 || money > MaxMoney)
                        {
                            error = $"reward {p}: money must be a whole number from 1 to {MaxMoney}";
                            return false;
                        }
                        rewards.Add(new QuestReward { Kind = RewardKind.Money, Amount = money });
                        break;
                    case "item":
                        int n = 0;
                        string name = arg;
                        Match m = itemCount.Match(arg);
                        if (m.Success)
                        {
                            name = m.Groups["name"].Value;
                            if (!int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1 || n > MaxCount)
                            {
                                error = $"reward {p}: the count must be from 1 to {MaxCount}";
                                return false;
                            }
                        }
                        if (name.Length == 0 || name.IndexOf(' ') >= 0)
                        {
                            error = $"reward {p}: expected Item:<item name>[ xN], e.g. Item:Revolver or Item:Banana x3";
                            return false;
                        }
                        rewards.Add(new QuestReward { Kind = RewardKind.Item, Item = name, Amount = n });
                        break;
                    case "xp":
                    case "experience":
                        rewards.Add(new QuestReward { Kind = RewardKind.XP });
                        break;
                    case "recruit":
                        rewards.Add(new QuestReward { Kind = RewardKind.Recruit });
                        break;
                    case "standing":
                        rewards.Add(new QuestReward { Kind = RewardKind.Standing });
                        break;
                    default:
                        error = $"unknown reward {p} ($N, Item:Name, XP, Recruit or Standing)";
                        return false;
                }
            }
            return true;
        }

        // ---- Text ----

        public static string DefaultTitle(QuestType type)
        {
            switch (type)
            {
                case QuestType.Kill: return "A hit";
                case QuestType.Retrieve: return "A recovery";
                case QuestType.Destroy: return "Sabotage";
                case QuestType.Deliver: return "A delivery";
                default: return "A message";
            }
        }

        /// <summary>What a giver says when the script gives no text for <paramref name="key"/>; null for none.</summary>
        public static string DefaultText(QuestType type, string key, bool autoReport)
        {
            switch (key)
            {
                case "offer":
                    switch (type)
                    {
                        case QuestType.Kill: return "I need {target} dealt with, and I'd rather not do it myself.";
                        case QuestType.Retrieve: return "I want the {item} back, and I can't go and get it myself.";
                        case QuestType.Destroy: return "Some things around here work a little too well. Break them for me.";
                        case QuestType.Deliver: return "The {item} has to reach {target}. Don't open it and don't lose it.";
                        default: return "Go and see {target} for me. Say I sent you; they'll know what it's about.";
                    }
                case "accept": return autoReport ? "Good. I'll hear about it when it's done." : "Good. Come back when it's done.";
                case "remind": return "Still waiting on you.";
                case "done": return "Nice work. As promised.";
                case "targettext":
                    return type == QuestType.Deliver
                        ? "{Target} takes the {item}. \"Tell {giver} it got here.\""
                        : "\"{Giver} sent you? Fine. Tell them I got the message.\"";
                default: return null;
            }
        }

        /// <summary>
        ///   Replaces <c>{name}</c> placeholders with <paramref name="value"/>'s answer (null keeps the placeholder). A
        ///   capitalised placeholder (<c>{Target}</c>) capitalises the value's first letter.
        /// </summary>
        public static string Fill(string text, Func<string, string> value)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
                if (open < 0 || close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, open - i);
                string name = text.Substring(open + 1, close - open - 1);
                string v = name.Length > 0 && IsLetters(name) ? value(name.ToLowerInvariant()) : null;
                if (v == null)
                {
                    sb.Append('{');
                    i = open + 1;
                    continue;
                }
                sb.Append(char.IsUpper(name[0]) ? Capitalize(v) : v);
                i = close + 1;
            }
            return sb.ToString();
        }

        private static bool IsLetters(string s)
        {
            foreach (char c in s)
                if (!char.IsLetter(c)) return false;
            return true;
        }

        public static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s ?? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Cuts text to <paramref name="max"/> characters, ending it with <c>...</c>.</summary>
        public static string Clamp(string text, int max)
        {
            if (text == null) return "";
            if (text.Length <= max) return text;
            return text.Substring(0, Math.Max(0, max - 3)).TrimEnd() + "...";
        }

        /// <summary>A quest map marker's hover text: <c>Big Tony - job: Collect the debt</c>.</summary>
        public static string MarkerLabel(MarkerKind kind, string name, string title)
        {
            name = Capitalize(string.IsNullOrEmpty(name) ? "someone" : name);
            title = Clamp((title ?? "").Trim(), MaxMarkerTitle);
            string what;
            switch (kind)
            {
                case MarkerKind.Offer: what = "job"; break;
                case MarkerKind.Running: what = "job in progress"; break;
                case MarkerKind.Report: what = "report back"; break;
                case MarkerKind.Recipient: what = "recipient"; break;
                default: what = "target"; break;
            }
            return title.Length == 0 ? name + " - " + what : name + " - " + what + ": " + title;
        }

        /// <summary>A plain English plural: <c>Thief</c> → <c>Thieves</c>, <c>Box</c> → <c>Boxes</c>, <c>Mafia</c> stays.</summary>
        public static string Plural(string word)
        {
            if (string.IsNullOrEmpty(word)) return word ?? "";
            string lower = word.ToLowerInvariant();
            if (lower.EndsWith("mafia") || (lower.EndsWith("s") && !lower.EndsWith("ss"))) return word;
            if (lower.EndsWith("man")) return word.Substring(0, word.Length - 2) + "en";
            if (lower.EndsWith("thief") || lower.EndsWith("wolf") || lower.EndsWith("elf")) return word.Substring(0, word.Length - 1) + "ves";
            if (lower.EndsWith("ss") || lower.EndsWith("x") || lower.EndsWith("ch") || lower.EndsWith("sh")) return word + "es";
            if (lower.Length > 1 && lower.EndsWith("y") && "aeiou".IndexOf(lower[lower.Length - 2]) < 0) return word.Substring(0, word.Length - 1) + "ies";
            return word + "s";
        }

        /// <summary>
        ///   A group of names for a sentence. Generic names (a vanilla type such as Blahd, or an object) take "the":
        ///   <c>the Blahd</c>, <c>Big Tony</c>, <c>3 Blahds</c>, <c>Big Tony and the Blahd</c>,
        ///   <c>Big Tony, Sal, the Blahd and 2 others</c>.
        /// </summary>
        public static string Names(IList<KeyValuePair<string, bool>> names)
        {
            if (names == null || names.Count == 0) return "";
            var groups = new List<KeyValuePair<string, bool>>();
            var counts = new List<int>();
            foreach (KeyValuePair<string, bool> n in names)
            {
                int g = groups.FindIndex(x => x.Key == n.Key && x.Value == n.Value);
                if (g >= 0) counts[g]++;
                else
                {
                    groups.Add(n);
                    counts.Add(1);
                }
            }
            var parts = new List<string>();
            int shown = Math.Min(groups.Count, 3);
            for (int i = 0; i < shown; i++)
                parts.Add(counts[i] == 1 ? (groups[i].Value ? "the " + groups[i].Key : groups[i].Key)
                    : counts[i].ToString(CultureInfo.InvariantCulture) + " " + Plural(groups[i].Key));
            if (groups.Count > shown)
            {
                int rest = 0;
                for (int i = shown; i < groups.Count; i++) rest += counts[i];
                parts.Add(rest == 1 ? "1 other" : rest.ToString(CultureInfo.InvariantCulture) + " others");
            }
            return JoinAnd(parts);
        }

        public static string JoinAnd(IList<string> parts)
        {
            if (parts == null || parts.Count == 0) return "";
            if (parts.Count == 1) return parts[0];
            var head = new string[parts.Count - 1];
            for (int i = 0; i < head.Length; i++) head[i] = parts[i];
            return string.Join(", ", head) + " and " + parts[parts.Count - 1];
        }

        /// <summary>
        ///   What the job asks, for <c>{objective}</c> and progress lines: <c>take down 2 of 3 Blahds</c>,
        ///   <c>bring back the Briefcase (Big Tony has it)</c>, <c>wreck the Generator</c>.
        /// </summary>
        /// <param name="needed">How many must fall or break (Kill, Destroy) or how many items (Retrieve, Deliver).</param>
        /// <param name="setSize">How many targets the set holds (Kill, Destroy); ignored for a faction.</param>
        /// <param name="holder">Who or what holds a Retrieve's item, already a name for a sentence; null if unknown.</param>
        public static string Objective(QuestType type, TargetKind kind, int needed, int setSize, string target, string item,
            string holder, bool holderIsObject)
        {
            string n = needed.ToString(CultureInfo.InvariantCulture);
            switch (type)
            {
                case QuestType.Kill:
                    if (kind == TargetKind.Faction) return $"take down {n} of {target}";
                    return needed >= setSize ? $"take down {target}" : $"take down {n} of {target}";
                case QuestType.Destroy:
                    return needed >= setSize ? $"wreck {target}" : $"wreck {n} of {target}";
                case QuestType.Retrieve:
                    string what = needed <= 1 ? $"bring back the {item}" : $"bring back {n} {Plural(item)}";
                    if (string.IsNullOrEmpty(holder)) return what;
                    if (holderIsObject) return needed <= 1 ? $"{what} (it's in {holder})" : $"{what} (they're in {holder})";
                    return needed <= 1 ? $"{what} ({holder} has it)" : $"{what} ({holder} has them)";
                case QuestType.Deliver:
                    return needed <= 1 ? $"take the {item} to {target}" : $"take {n} {Plural(item)} to {target}";
                default:
                    return $"have a word with {target}";
            }
        }

        /// <summary><c>Reward: $150, Revolver and experience.</c>, or empty with no rewards.</summary>
        public static string RewardText(IList<QuestReward> rewards, Func<string, string> itemName, string factionPlural)
        {
            string list = RewardList(rewards, itemName, factionPlural);
            return list.Length == 0 ? "" : "Reward: " + list + ".";
        }

        /// <summary><c>$150, Revolver and experience</c> (the <c>{reward}</c> placeholder), or empty with no rewards.</summary>
        public static string RewardList(IList<QuestReward> rewards, Func<string, string> itemName, string factionPlural)
        {
            if (rewards == null || rewards.Count == 0) return "";
            int money = 0;
            var parts = new List<string>();
            bool xp = false, recruit = false, standing = false;
            foreach (QuestReward r in rewards)
            {
                switch (r.Kind)
                {
                    case RewardKind.Money: money += r.Amount; break;
                    case RewardKind.Item:
                        string name = itemName != null ? itemName(r.Item) ?? r.Item : r.Item;
                        if (!parts.Contains(name)) parts.Add(r.Amount > 1 ? r.Amount.ToString(CultureInfo.InvariantCulture) + " " + Plural(name) : name);
                        break;
                    case RewardKind.XP: xp = true; break;
                    case RewardKind.Recruit: recruit = true; break;
                    case RewardKind.Standing: standing = true; break;
                }
            }
            if (money > 0) parts.Insert(0, "$" + money.ToString(CultureInfo.InvariantCulture));
            if (recruit) parts.Add("a recruit");
            if (standing) parts.Add(string.IsNullOrEmpty(factionPlural) ? "their trust" : "the trust of " + factionPlural);
            if (xp) parts.Add("experience");
            return JoinAnd(parts);
        }

        /// <summary><c>Cut off the head!</c> → <c>cut-off-the-head</c>.</summary>
        public static string Slug(string title)
        {
            var sb = new StringBuilder();
            foreach (char c in (title ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            return s.Length == 0 ? "job" : s;
        }

        /// <summary>The job ids of a <c>Quest=</c> gate switch: <c>tony-hit+the-ledger</c>.</summary>
        public static List<string> GateIds(string arg)
        {
            var ids = new List<string>();
            foreach (string id in (arg ?? "").Split('+', ','))
                if (id.Trim().Length > 0) ids.Add(id.Trim());
            return ids;
        }

        /// <summary>Radiant job pay: 60 + 20 per level, times the job's multiplier, rounded to $5.</summary>
        public static int RadiantPay(int level, double multiplier)
        {
            double pay = (60 + 20 * Math.Max(1, level)) * multiplier;
            return Math.Max(5, (int)Math.Round(pay / 5.0, MidpointRounding.AwayFromZero) * 5);
        }

        // ---- Radiant jobs ----

        internal sealed class RadiantText
        {
            public string Title, Offer, Accept, Remind, Done, TargetText;
        }

        internal sealed class RadiantTemplate
        {
            public string Name;
            /// <summary>For a giver in a faction (else for anyone outside one).</summary>
            public bool Faction;
            /// <summary>Needs a rival faction.</summary>
            public bool NeedsRival;
            public QuestType Type;
            public double Pay;
            public bool Standing;
            public bool Auto;
            public RadiantText[] Variants;
        }

        // {faction} and {rival} read as groups ("the Crepes", "the Mafia", or a designer name such as "The Contractor"),
        // so the texts use them where any of those fit. Items take "the", "my" or "our", never "a".
        public static readonly RadiantTemplate[] Radiant =
        {
            new RadiantTemplate
            {
                Name = "Hit", Faction = true, NeedsRival = true, Type = QuestType.Kill, Pay = 2, Standing = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Cut off the head",
                        Offer = "{Target} runs things for {rival}. Take {target} out of the picture and the rest of them fall apart.",
                        Accept = "Make it count. {Faction} won't forget who did this.",
                        Remind = "{Target} is still up and about. That's the opposite of what I asked for.",
                        Done = "{Target} is out of the picture? Ha! {Rival} won't know what hit them.",
                    },
                    new RadiantText
                    {
                        Title = "Leadership change",
                        Offer = "Word is {target} gives the orders for {rival}. I'd like that to stop being true.",
                        Accept = "Don't make it look like {faction} sent you. Or do. Your call.",
                        Remind = "{Target} is still giving orders. Why is that?",
                        Done = "Somebody finally dealt with {target}. We pay our debts.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Thin", Faction = true, NeedsRival = true, Type = QuestType.Kill, Pay = 1.5, Standing = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Thin the herd",
                        Offer = "Our streets are crawling with {rival}. Knock {count} of them down a peg.",
                        Accept = "Knocked out, locked up or worse. I'm not fussy.",
                        Remind = "I still see far too many of {rival} out there.",
                        Done = "That'll teach {rival} some manners.",
                    },
                    new RadiantText
                    {
                        Title = "Send a message",
                        Offer = "These streets aren't for {rival}, and they need reminding. Put {count} of them on the pavement.",
                        Accept = "Make it loud.",
                        Remind = "That's not {count} yet. I can count.",
                        Done = "Message received, I bet. Here's your cut.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Recover", Faction = true, NeedsRival = true, Type = QuestType.Retrieve, Pay = 1, Standing = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Stolen goods",
                        Offer = "Somebody from {rival} walked off with my {item}. {Target} has it now. Get it back, any way you like.",
                        Accept = "Pick their pocket or pick them up off the floor. Just bring it here.",
                        Remind = "No {item}, no money.",
                        Done = "My {item}! You're alright, you know that?",
                    },
                    new RadiantText
                    {
                        Title = "Repossession",
                        Offer = "{Target} is sitting on our {item}. I'd like it back.",
                        Accept = "Bring it straight to me. Don't get curious.",
                        Remind = "Still no {item}. {Target} isn't that tough.",
                        Done = "Back where it belongs. Nice.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Sabotage", Faction = true, NeedsRival = true, Type = QuestType.Destroy, Pay = 1.25, Standing = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Lights out",
                        Offer = "{Rival} can't do without their gear. Go and {objective}, then watch them squirm.",
                        Accept = "Bring a crowbar. Or a bomb. I don't judge.",
                        Remind = "Their stuff still works. I'd like it not to.",
                        Done = "I heard the crash from here. Lovely.",
                    },
                    new RadiantText
                    {
                        Title = "Property damage",
                        Offer = "I want {rival} to wake up to bad news. {Objective}, and make it look like an accident.",
                        Accept = "Nobody saw you. Right?",
                        Remind = "It's still standing. It shouldn't be.",
                        Done = "An accident. Tragic. Here's something for your trouble.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Courier", Faction = true, Type = QuestType.Deliver, Pay = 1, Auto = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Special delivery",
                        Offer = "Take the {item} to {target}. It's our business, so don't make it yours.",
                        Accept = "{Target} will pay you when it arrives.",
                        Remind = "{Target} is still waiting on the {item}.",
                        TargetText = "{Target} takes the {item} and checks inside. \"From {giver}? Good. Here's for your trouble.\"",
                    },
                    new RadiantText
                    {
                        Title = "Package run",
                        Offer = "{Target} is expecting the {item} from me. Walk it over, and keep your hands off the contents.",
                        Accept = "{Target} will sort out your pay.",
                        Remind = "The {item} won't deliver itself.",
                        TargetText = "\"From {giver}? About time.\" {Target} tucks the {item} away and hands you some cash.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Word", Faction = true, Type = QuestType.Talk, Pay = 1, Auto = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "A quiet word",
                        Offer = "Find {target} and say the meeting is off. Don't ask which meeting.",
                        Accept = "{Target} will see you right.",
                        Remind = "Did you tell {target} yet?",
                        TargetText = "\"Meeting's off? Figures.\" {Target} slips you a few bills for the trouble.",
                    },
                    new RadiantText
                    {
                        Title = "Checking in",
                        Offer = "Haven't heard from {target} in a while. Go and make sure they're still with us.",
                        Accept = "Tell {target} I asked after them.",
                        Remind = "Any word from {target}?",
                        TargetText = "\"Tell {giver} I'm fine, and to stop worrying.\" {Target} presses some cash into your hand.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Pickpocket", Type = QuestType.Retrieve, Pay = 0.75,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Pickpocketed",
                        Offer = "Somebody lifted my {item}! It was {target}, I'm sure of it. Please, get it back.",
                        Accept = "Thank you! I'll be right here.",
                        Remind = "Any luck with my {item}?",
                        Done = "My {item}! I thought I'd never see it again.",
                    },
                    new RadiantText
                    {
                        Title = "Sticky fingers",
                        Offer = "{Target} swiped my {item} and laughed about it. I can't fight. Can you?",
                        Accept = "Be careful!",
                        Remind = "Still no sign of my {item}?",
                        Done = "You got it back! Here, take this.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Parcel", Type = QuestType.Deliver, Pay = 0.75, Auto = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Care package",
                        Offer = "Could you take the {item} to {target}? My legs aren't what they were.",
                        Accept = "{Target} will be so pleased.",
                        Remind = "Did {target} get the {item}?",
                        TargetText = "\"Oh, from {giver}? How sweet.\" {Target} gives you a little something for your trouble.",
                    },
                    new RadiantText
                    {
                        Title = "Returned property",
                        Offer = "I borrowed the {item} from {target} ages ago. Could you give it back? I'm too embarrassed.",
                        Accept = "Tell {target} I'm sorry it took so long.",
                        Remind = "Please don't keep the {item}.",
                        TargetText = "\"My {item}! Finally.\" {Target} tips you for bringing it.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "CheckIn", Type = QuestType.Talk, Pay = 0.75, Auto = true,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Worried sick",
                        Offer = "I haven't heard from {target} in days. Could you check they're alright?",
                        Accept = "Thank you. It's a scary city.",
                        Remind = "Have you seen {target}?",
                        TargetText = "\"{Giver} is worried about me? Tell them I'm fine.\" {Target} gives you a few coins.",
                    },
                    new RadiantText
                    {
                        Title = "Neighbourly",
                        Offer = "Tell {target} the music was too loud last night. Politely! Or not. I'm past caring.",
                        Accept = "Don't let {target} talk you round.",
                        Remind = "Well? Did you tell {target}?",
                        TargetText = "\"Too loud? Fine, fine.\" {Target} hands you some money, apparently to make you go away.",
                    },
                },
            },
            new RadiantTemplate
            {
                Name = "Pest", Type = QuestType.Kill, Pay = 0.75,
                Variants = new[]
                {
                    new RadiantText
                    {
                        Title = "Pest control",
                        Offer = "{Target} has been making life miserable round here. Somebody should do something.",
                        Accept = "I didn't ask you for anything. Understand?",
                        Remind = "{Target} is still out there.",
                        Done = "The street's a little safer. Here, you earned this.",
                    },
                    new RadiantText
                    {
                        Title = "Neighbourhood watch",
                        Offer = "Everyone's scared of {target}. Everyone but you, maybe?",
                        Accept = "Be careful out there.",
                        Remind = "{Target} is still around. I saw them this morning.",
                        Done = "Is it true? About {target}? Here. Thank you.",
                    },
                },
            },
        };

        public static RadiantTemplate FindRadiant(string name)
            => Array.Find(Radiant, t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>A radiant job as a one-stage quest script, which then goes through <see cref="Parse"/>.</summary>
        public static string RadiantScript(RadiantTemplate t, int variant, string id, string target, string item, int count, int money)
        {
            RadiantText v = t.Variants[((variant % t.Variants.Length) + t.Variants.Length) % t.Variants.Length];
            var sb = new StringBuilder(Prefix);
            void Line(string key, string value)
            {
                if (!string.IsNullOrEmpty(value)) sb.Append(key).Append(": ").Append(value).Append('\n');
            }
            Line("Id", id);
            Line("Title", v.Title);
            Line("Type", t.Type.ToString());
            Line("Target", target);
            if (t.Type == QuestType.Retrieve || t.Type == QuestType.Deliver) Line("Item", item);
            if (count > 0 && t.Type != QuestType.Talk) Line("Count", count.ToString(CultureInfo.InvariantCulture));
            Line("Reward", "$" + money.ToString(CultureInfo.InvariantCulture) + ", XP" + (t.Standing ? ", Standing" : ""));
            if (t.Auto) Line("Report", "Auto");
            Line("Offer", v.Offer);
            Line("Accept", v.Accept);
            Line("Remind", v.Remind);
            Line("Done", v.Done);
            Line("Target text", v.TargetText);
            return sb.ToString();
        }
    }
}
