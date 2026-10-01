// Offline tests for RCK's pure faction rules: grade precedence, parsing and the FactionRel matrix, plus a check that
// docs\ccu-interface.json (the modder-facing spec) lists the same grades and precedence the code uses.
// Usage: dotnet run --project tools\FactionTests [-- <repo root>]. Exits 1 on any failure.
using System.Text.Json;
using RCK.Campaign;
using RCK.Quests;
using RCK.Social;

int failures = 0, checks = 0;

void Check(bool ok, string what)
{
    checks++;
    if (ok) return;
    failures++;
    Console.WriteLine("FAIL " + what);
}

void Equal<T>(T actual, T expected, string what) => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: got {actual}, want {expected}");

var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
for (int n = 1; n <= 20; n++) keys["Faction_" + n] = n - 1;
keys["Crepe"] = 20;
keys["Blahd"] = 21;
keys["Cop"] = 22;
int KeyIndex(string name) => keys.TryGetValue(name, out int i) ? i : int.TryParse(name, out int n) && n >= 1 && n <= 20 ? n - 1 : -1;
ulong Bit(string name) => 1UL << KeyIndex(name);

// Grade order is precedence order.
FactionGrade[] strongestFirst = { FactionGrade.Hateful, FactionGrade.Territorial, FactionGrade.Annoyed, FactionGrade.Friendly, FactionGrade.Aligned, FactionGrade.Neutral, FactionGrade.None };
for (int i = 1; i < strongestFirst.Length; i++)
    Check(strongestFirst[i - 1] > strongestFirst[i], $"{strongestFirst[i - 1]} outranks {strongestFirst[i]}");

// Trait rules. Membership masks are passed in; a rule on either side applies both ways.
FactionProfile P(string? hostile = null, string? territorial = null, string? annoyed = null, string? friendly = null, string? aligned = null, string? neutral = null)
{
    var p = new FactionProfile();
    if (hostile != null) p.Hostile = Bit(hostile);
    if (territorial != null) p.Territorial = Bit(territorial);
    if (annoyed != null) p.Annoyed = Bit(annoyed);
    if (friendly != null) p.Friendly = Bit(friendly);
    if (aligned != null) p.Aligned = Bit(aligned);
    if (neutral != null) p.Neutral = Bit(neutral);
    return p;
}
ulong blahd = Bit("Blahd"), crepe = Bit("Crepe"), cop = Bit("Cop");
var none = new FactionProfile();
Equal(FactionRules.Resolve(P(territorial: "Blahd"), 0, none, blahd), FactionGrade.Territorial, "Blahd_Territorial vs a Blahd member");
Equal(FactionRules.Resolve(none, blahd, P(territorial: "Blahd"), 0), FactionGrade.Territorial, "a Blahd member vs Blahd_Territorial (other side)");
Equal(FactionRules.Resolve(P(territorial: "Blahd"), 0, none, crepe), FactionGrade.None, "Blahd_Territorial vs a non-member");
Equal(FactionRules.Resolve(P(hostile: "Blahd", territorial: "Blahd"), 0, none, blahd), FactionGrade.Hateful, "Hostile beats Territorial");
Equal(FactionRules.Resolve(P(territorial: "Blahd", annoyed: "Blahd"), 0, none, blahd), FactionGrade.Territorial, "Territorial beats Annoyed");
Equal(FactionRules.Resolve(P(territorial: "Blahd", aligned: "Crepe"), crepe, P(annoyed: "Crepe", aligned: "Blahd"), blahd), FactionGrade.Territorial, "Territorial beats the other side's Annoyed");
Equal(FactionRules.Resolve(P(territorial: "Blahd", friendly: "Blahd"), 0, none, blahd), FactionGrade.Territorial, "Territorial beats Friendly");
Equal(FactionRules.Resolve(P(aligned: "Blahd", neutral: "Blahd"), blahd, none, blahd), FactionGrade.Aligned, "Aligned beats Neutral");
Equal(FactionRules.Resolve(P(neutral: "Blahd"), 0, none, blahd), FactionGrade.Neutral, "Neutral");
Equal(FactionRules.Resolve(none, 0, none, blahd), FactionGrade.None, "no rules");

// Parsing and the vanilla relationship each grade starts as.
foreach (string text in new[] { "Territorial", "territorial", "TERRITORIAL", " Territorial " })
    Check(FactionRules.TryParseGrade(text.Trim(), out FactionGrade g) && g == FactionGrade.Territorial, $"parse '{text}'");
Check(FactionRules.TryParseGrade("Hostile", out FactionGrade h) && h == FactionGrade.Hateful, "Hostile parses as Hateful");
Check(!FactionRules.TryParseGrade("Furious", out _), "unknown grade rejected");
Equal(FactionRules.RelOf(FactionGrade.Territorial), "Annoyed", "Territorial starts Annoyed");
Equal(FactionRules.HateOf(FactionGrade.Territorial), 0, "Territorial starts with no hate");
Equal(FactionRules.RelOf(FactionGrade.Hateful), "Hateful", "RelOf Hateful");
Equal(FactionRules.HateOf(FactionGrade.Hateful), 5, "HateOf Hateful");
Check(FactionRules.GradeNames.Contains("Territorial"), "GradeNames lists Territorial");

// Street innocence holds back exactly the hostile grades.
foreach (FactionGrade g in new[] { FactionGrade.Hateful, FactionGrade.Territorial, FactionGrade.Annoyed })
    Check(FactionRules.IsHostile(g), $"innocence holds back {g}");
foreach (FactionGrade g in new[] { FactionGrade.Friendly, FactionGrade.Aligned, FactionGrade.Neutral, FactionGrade.None })
    Check(!FactionRules.IsHostile(g), $"innocence keeps {g}");

// The FactionRel matrix.
FactionMatrix? M(string body, List<string>? errors = null)
    => FactionMatrix.Build(new[] { body }, KeyIndex, (entry, error) => errors?.Add(entry + ": " + error));

FactionMatrix? m = M("Crepe<>Blahd=Territorial");
Check(m != null, "Crepe<>Blahd=Territorial parses");
if (m != null)
{
    Equal(m.EntryCount, 2, "<> makes two entries");
    Equal(m.KeyMask, crepe | blahd, "key mask");
    Equal(m.Resolve(crepe, false, blahd, false), FactionGrade.Territorial, "Crepe>Blahd from <>");
    Equal(m.Resolve(blahd, false, crepe, false), FactionGrade.Territorial, "Blahd>Crepe from <>");
    Equal(m.Resolve(crepe, false, cop, false), FactionGrade.None, "Crepe>Cop not named");
}
m = M("1>2=Territorial");
if (m != null)
{
    Equal(m.Resolve(Bit("Faction_1"), false, Bit("Faction_2"), false), FactionGrade.Territorial, "1>2 directed");
    Equal(m.Resolve(Bit("Faction_2"), false, Bit("Faction_1"), false), FactionGrade.None, "1>2 has no reverse");
}
else Check(false, "1>2=Territorial parses");
m = M("1<2=Territorial");
if (m != null)
{
    Equal(m.Resolve(Bit("Faction_2"), false, Bit("Faction_1"), false), FactionGrade.Territorial, "1<2 is 2>1");
    Equal(m.Resolve(Bit("Faction_1"), false, Bit("Faction_2"), false), FactionGrade.None, "1<2 has no 1>2");
}
else Check(false, "1<2=Territorial parses");
m = M("Crepe>*=Territorial;Crepe>Blahd=Friendly");
if (m != null)
{
    Equal(m.Resolve(crepe, false, blahd, false), FactionGrade.Friendly, "a named entry beats *");
    Equal(m.Resolve(crepe, false, cop, false), FactionGrade.Territorial, "Crepe>* covers Cop");
    Equal(m.Resolve(crepe, false, 0, true), FactionGrade.Territorial, "Crepe>* covers a player");
    Equal(m.Resolve(crepe, false, crepe, false), FactionGrade.None, "Crepe>* skips Crepe members");
}
else Check(false, "Crepe>*=Territorial parses");
Equal(M("1>2=Annoyed;1>2=Territorial;1>2=Hostile")?.Resolve(Bit("Faction_1"), false, Bit("Faction_2"), false), FactionGrade.Hateful, "strongest named entry wins (Hateful)");
Equal(M("1>2=Annoyed;1>2=Territorial")?.Resolve(Bit("Faction_1"), false, Bit("Faction_2"), false), FactionGrade.Territorial, "strongest named entry wins (Territorial)");
Equal(M("Player>Crepe=Territorial")?.Resolve(0, true, crepe, false), FactionGrade.Territorial, "Player side");
var errors = new List<string>();
Check(M("1>2=Furious", errors) == null, "bad grade gives no matrix");
Check(errors.Count == 1 && errors[0].Contains("Territorial"), "bad-grade error lists Territorial: " + string.Join(" | ", errors));
errors.Clear();
Check(M("Nobody>2=Territorial;1>2=Territorial", errors)?.EntryCount == 1 && errors.Count == 1 && errors[0].Contains("Nobody"), "unknown faction skipped, the rest kept");

// Recruit policies and the FactionRecruit matrix.
RecruitMatrix? R(string body, List<string>? errs = null)
    => RecruitMatrix.Build(new[] { body }, KeyIndex, (entry, error) => errs?.Add(entry + ": " + error));

RecruitMatrix? r = R("Cop=Free;Crepe,Blahd=Paid;*=Off;");
Check(r != null, "Cop=Free;Crepe,Blahd=Paid;*=Off parses");
if (r != null)
{
    Equal(r.EntryCount, 3, "recruit entry count");
    Equal(r.Resolve(cop), RecruitPolicy.Free, "Cop Free");
    Equal(r.Resolve(crepe), RecruitPolicy.Paid, "Crepe Paid from a comma list");
    Equal(r.Resolve(blahd), RecruitPolicy.Paid, "Blahd Paid from a comma list");
    Equal(r.Resolve(Bit("Faction_1")), RecruitPolicy.Off, "* covers unnamed factions");
    Equal(r.Resolve(crepe | cop), RecruitPolicy.Free, "most generous named entry wins");
    Equal(r.Resolve(crepe | Bit("Faction_1")), RecruitPolicy.Paid, "* never beats a more generous named entry");
    Equal(r.Resolve(0), RecruitPolicy.None, "no faction, no policy");
}
r = R("Crepe=Off;*=Free");
if (r != null)
{
    Equal(r.Resolve(crepe), RecruitPolicy.Off, "a named Off beats * for that faction alone");
    Equal(r.Resolve(crepe | cop), RecruitPolicy.Free, "* applies to the unnamed Cop");
}
else Check(false, "Crepe=Off;*=Free parses");
Equal(R("3=paid")?.Resolve(Bit("Faction_3")), RecruitPolicy.Paid, "faction numbers and lower-case policies");
Equal(R("Crepe=Free")?.Resolve(blahd), RecruitPolicy.None, "unnamed faction without * is None");
var rerrors = new List<string>();
Check(R("Crepe=Maybe", rerrors) == null && rerrors.Count == 1, "bad policy gives no matrix: " + string.Join(" | ", rerrors));
rerrors.Clear();
Check(R("Nobody=Free;Crepe,Nobody=Paid;Cop=Free", rerrors)?.EntryCount == 1 && rerrors.Count == 2, "unknown factions skip their entries: " + string.Join(" | ", rerrors));
rerrors.Clear();
Check(R("=Free;Crepe", rerrors) == null && rerrors.Count == 2, "entries without a faction or a policy are skipped: " + string.Join(" | ", rerrors));

RecruitMatrix? rm = R("Crepe=Paid;Cop=Off");
Equal(RecruitRules.Decide(RecruitPolicy.None, null, crepe, RecruitPolicy.None), RecruitPolicy.Off, "nothing on is Off");
Equal(RecruitRules.Decide(RecruitPolicy.None, null, crepe, RecruitPolicy.Free), RecruitPolicy.Free, "global mutator applies");
Equal(RecruitRules.Decide(RecruitPolicy.None, rm, crepe, RecruitPolicy.Free), RecruitPolicy.Paid, "matrix beats the global mutator");
Equal(RecruitRules.Decide(RecruitPolicy.None, rm, blahd, RecruitPolicy.Free), RecruitPolicy.Free, "global covers factions the matrix doesn't name");
Equal(RecruitRules.Decide(RecruitPolicy.Free, rm, cop, RecruitPolicy.None), RecruitPolicy.Free, "trait beats the matrix");
Equal(RecruitRules.Decide(RecruitPolicy.Off, rm, crepe, RecruitPolicy.Free), RecruitPolicy.Off, "Not Recruitable beats everything");
Equal(RecruitRules.Decide(RecruitPolicy.Free, null, 0, RecruitPolicy.Free), RecruitPolicy.Off, "no shared faction is Off even with a trait");
Equal(RecruitRules.ForPlayer(RecruitPolicy.None, rm, RecruitPolicy.None, crepe, crepe, false), RecruitPolicy.Paid, "member gets the faction policy");
Equal(RecruitRules.ForPlayer(RecruitPolicy.None, rm, RecruitPolicy.None, 0, crepe, false), RecruitPolicy.Off, "non-member gets nothing");
Equal(RecruitRules.ForPlayer(RecruitPolicy.None, null, RecruitPolicy.Free, 0, crepe, true), RecruitPolicy.Paid, "Friendly non-member hires at the paid price");
Equal(RecruitRules.ForPlayer(RecruitPolicy.None, rm, RecruitPolicy.None, 0, cop, true), RecruitPolicy.Off, "Friendly non-member can't hire where the faction is Off");
Equal(RecruitRules.ForPlayer(RecruitPolicy.Off, null, RecruitPolicy.Free, 0, crepe, true), RecruitPolicy.Off, "Not Recruitable also refuses Friendly non-members");

// Disguise maps: the defaults whose factions exist, then [RCK]Disguise:: entries in order.
var derrors = new List<string>();
Dictionary<string, int> D(params string[] bodies) => DisguiseRules.Build(bodies, KeyIndex, (entry, error) => derrors.Add(entry + ": " + error));
Dictionary<string, int> dm = D();
Equal(dm.GetValueOrDefault("HatBlue", -1), KeyIndex("Crepe"), "HatBlue passes for a Crepe by default");
Equal(dm.GetValueOrDefault("HatRed", -1), KeyIndex("Blahd"), "HatRed passes for a Blahd by default");
Equal(dm.GetValueOrDefault("CopHat", -1), KeyIndex("Cop"), "CopHat passes for a Cop by default");
Check(!dm.ContainsKey("SoldierHelmet"), "a default for a faction that doesn't exist is dropped");
Equal(derrors.Count, 0, "defaults give no errors");
dm = D("HatBlue=None; Fedora=3", "Fedora=Blahd;HatRed=off");
Check(!dm.ContainsKey("HatBlue") && !dm.ContainsKey("HatRed"), "=None and =Off take defaults off");
Equal(dm.GetValueOrDefault("Fedora", -1), KeyIndex("Blahd"), "later entries win");
Equal(derrors.Count, 0, "overrides give no errors");
dm = D("Beret=Nobody;Beret;=Crepe;Beanie=Faction_4");
Check(derrors.Count == 3 && derrors[0].Contains("Nobody"), "unknown faction and malformed entries are reported: " + string.Join(" | ", derrors));
Equal(dm.GetValueOrDefault("Beanie", -1), 3, "numbered faction keys work");
Check(!dm.ContainsKey("Beret"), "a bad entry adds nothing");
foreach (KeyValuePair<string, string> d in DisguiseRules.Defaults)
    Check(d.Key.Length > 0 && d.Value.Length > 0, $"default {d.Key}={d.Value} is complete");

// Faction names.
var nerrors = new List<string>();
Dictionary<int, string> names = FactionNameRules.Build(new[] { "1=The Contractor; 2 = The Summit ;Crepe=Crepe Nation", "Nobody=X;Blahd", "1=The Firm" }, KeyIndex, (entry, error) => nerrors.Add(entry + ": " + error));
Equal(names.GetValueOrDefault(0), "The Firm", "later faction names win");
Equal(names.GetValueOrDefault(1), "The Summit", "names are trimmed");
Equal(names.GetValueOrDefault(KeyIndex("Crepe")), "Crepe Nation", "named factions can be renamed");
Equal(nerrors.Count, 2, "unknown and malformed name entries are reported");
Equal(FactionNameRules.Short("Upper_Cruster"), "Upper Cruster", "short names drop underscores");
Equal(FactionNameRules.Plural("Crepe"), "the Crepes", "plural of a named faction");
Equal(FactionNameRules.Plural("Faction_3"), "Faction 3", "plural of a numbered faction");

// Raid schedules: [RCK]FactionRaid:: entries A>B@T[xN].
var raidErrs = new List<string>();
List<RaidEntry> raidList = RaidRules.Parse(new[] { "Crepe>Blahd@120; 1 > Blahd @ 60x5 ;", null!, "Blahd>1@0X1" }, KeyIndex, (entry, error) => raidErrs.Add(entry + ": " + error));
Equal(raidList.Count, 3, "three raid entries parse");
Equal(raidErrs.Count, 0, "valid raid entries give no errors");
if (raidList.Count == 3)
{
    Check(raidList[0].Raider == KeyIndex("Crepe") && raidList[0].Target == KeyIndex("Blahd") && raidList[0].At == 120 && raidList[0].Size == RaidRules.DefaultSize, "Crepe>Blahd@120 uses the default size");
    Check(raidList[1].Raider == 0 && raidList[1].Target == KeyIndex("Blahd") && raidList[1].At == 60 && raidList[1].Size == 5, "numbered raider, spaces and xN");
    Check(raidList[2].At == 0 && raidList[2].Size == 1 && raidList[2].Target == 0, "@0 and X1");
}
foreach ((string badEntry, string why) in new[]
{
    ("Crepe>Blahd", "@"), ("Crepe@120", "A>B"), (">Blahd@5", "A>B"), ("Nobody>Blahd@5", "Nobody"), ("Crepe>Nobody@5", "Nobody"),
    ("Crepe>Crepe@5", "itself"), ("Crepe>Blahd@5x0", "size"), ("Crepe>Blahd@5x7", "size"), ("Crepe>Blahd@3601", "seconds"),
    ("Crepe>Blahd@-5", "seconds"), ("Crepe>Blahd@soon", "seconds"), ("Crepe>Blahd@5x", "size"),
})
    Check(!RaidRules.TryParse(badEntry, KeyIndex, out RaidEntry? badRaid, out string? raidError) && badRaid == null && (raidError ?? "").Contains(why), $"raid entry {badEntry} is refused ({raidError})");
raidErrs.Clear();
raidList = RaidRules.Parse(new[] { "Crepe>Blahd@5;Crepe>Crepe@5;Blahd>Crepe@3600x6" }, KeyIndex, (entry, error) => raidErrs.Add(entry));
Check(raidList.Count == 2 && raidErrs.Count == 1 && raidErrs[0] == "Crepe>Crepe@5", "a bad raid entry is skipped and reported, the rest kept");

// Quest scripts.
string? ResolveFaction(string n) => KeyIndex(n) < 0 ? null : keys.Keys.FirstOrDefault(k => string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) ?? "Faction_" + n;
QuestScript qs = QuestRules.Parse(
    "rck-quest:::\r\nTitle: Cut off the head!\r\nType: Hit\r\nTarget: Leader:blahd\r\nReward: $150, Item:Banana x3, XP, Standing\r\n" +
    "Offer: Big Tony runs the Blahds.\r\n  He has to go.\r\nDone: Nice.\r\n---\r\nId: ledger\r\nType: Retrieve\r\nItem: Briefcase\r\n" +
    "Target: Agent:#12\r\nCount: 2\r\nAfter: cut-off-the-head\r\nWait: Not yet.\r\n---\r\nType: Deliver\r\nTarget: Label:b\r\n" +
    "Report: Auto\r\nTarget_Text: Thanks, {giver}.\r\n---\r\nType: Destroy\r\nTarget: Object:Generator@Rival\r\n" +
    "---\r\nType: Kill\r\nTarget: Faction:Rival\r\n---\r\nType: Talk\r\nTarget: Agent:Big Tony\r\nIdle: Go away.\r\n", ResolveFaction);
Equal(qs.Errors.Count, 0, "a good script has no errors: " + string.Join("; ", qs.Errors));
Equal(qs.Stages.Count, 6, "stages split on ---");
Equal(qs.Idle, "Go away.", "Idle is script-wide");
QuestStage q1 = qs.Stages[0];
Equal(q1.Id, "cut-off-the-head", "a stage id defaults to its slugged title");
Equal(q1.Type, QuestType.Kill, "Hit is a Kill");
Equal(q1.Target.Kind, TargetKind.Leader, "Leader target kind");
Equal(q1.Target.Value, "Blahd", "the target faction resolves to its key");
Equal(q1.Rewards.Count, 4, "four rewards");
Check(q1.Rewards[1].Kind == RewardKind.Item && q1.Rewards[1].Item == "Banana" && q1.Rewards[1].Amount == 3, "Item:Banana x3");
Equal(q1.Text("offer"), "Big Tony runs the Blahds.\n  He has to go.", "a line without a key continues the value");
QuestStage q2 = qs.Stages[1];
Check(q2.Id == "ledger" && q2.Target.Kind == TargetKind.AgentId && q2.Target.Id == 12 && q2.Count == 2, "Id, Agent:#id and Count");
Check(q2.After.SequenceEqual(new[] { "cut-off-the-head" }) && q2.Text("wait") == "Not yet.", "After and Wait");
QuestStage q3 = qs.Stages[2];
Check(q3.AutoReport && q3.Item == QuestRules.DefaultDeliverItem && q3.Target.Value == "B", "a Deliver defaults its item; Report: Auto; labels upper-case");
Equal(q3.Text("targettext"), "Thanks, {giver}.", "Target_Text normalises to Target text");
Check(qs.Stages[3].Target.Kind == TargetKind.Object && qs.Stages[3].Target.Value == "Generator" && qs.Stages[3].Target.Owner == "Rival", "Object:Name@Rival");
Check(qs.Stages[4].Target.Kind == TargetKind.Faction && qs.Stages[4].Target.Value == "Rival", "Faction:Rival");
Check(qs.Stages[5].Target.Kind == TargetKind.Agent && qs.Stages[5].Target.Value == "Big Tony", "Agent:Name keeps spaces");
Check(QuestRules.IsScript("  RCK-QUEST:::Type: Talk") && !QuestRules.IsScript("Hello"), "the prefix is case-insensitive");

QuestScript bad = QuestRules.Parse("rck-quest:::\nType: Kill\n---\nType: Destroy\nTarget: Agent:Bob\n---\nType: Retrieve\n---\nType: Dance\n" +
    "---\nType: Kill\nTarget: Leader:Nobody\n---\nType: Talk\nTarget: Label:E\n---\nType: Kill\nTarget: Agent:X\nCount: 0\n---\n" +
    "Type: Talk\nTarget: Agent:X\nReward: $0\n---\nType: Retrieve\nItem: Briefcase\nReport: Auto\n---\nId: a b\nType: Talk\nTarget: Agent:X\n" +
    "---\nType: Deliver\nTarget: Object:Safe\n---\nType: Talk\nTarget: Agent:X\nReward: Item:Big Gun\n", ResolveFaction);
Equal(bad.Stages.Count, 0, "every bad stage is dropped");
Equal(bad.Errors.Count, 12, "one error per bad stage: " + string.Join(" | ", bad.Errors));
QuestScript warn = QuestRules.Parse("rck-quest:::stray\nTitle: X\nType: Kill\nTarget: Label:A\nItem: Banana\nOffer: {nope} {target}\n" +
    "---\nTitle: X\nType: Talk\nTarget: Label:A\nAfter: elsewhere\nOffer: " + new string('x', 601), ResolveFaction);
Equal(warn.Errors.Count, 0, "warnings aren't errors");
Equal(warn.Stages[1].Id, "x-2", "a repeated title gets a numbered id");
foreach (string w in new[] { "before the first key", "Item is ignored", "unknown placeholder {nope}", "After names elsewhere", "601 characters" })
    Check(warn.Warnings.Any(x => x.Contains(w)), "warns: " + w + " in [" + string.Join(" | ", warn.Warnings) + "]");
Check(QuestRules.Parse("rck-quest:::\nId: j\nType: Talk\nTarget: Label:A\n---\nId: J\nType: Talk\nTarget: Label:A\n").Errors.Count == 1, "duplicate ids (any case) are errors");

// Quest text.
Func<string, string?> vals = n => n switch { "target" => "the Blahd leader", "rival" => "the Blahds", "count" => "3", _ => null };
Equal(QuestRules.Fill("{Target} runs things for {rival}. {Rival}! {nope} {count}{", n => vals(n)!), "The Blahd leader runs things for the Blahds. The Blahds! {nope} 3{", "Fill capitalises and keeps unknowns");
Equal(QuestRules.Names(new List<KeyValuePair<string, bool>> { new("Blahd", true) }), "the Blahd", "one generic name");
Equal(QuestRules.Names(new List<KeyValuePair<string, bool>> { new("Blahd", true), new("Blahd", true), new("Big Tony", false) }), "2 Blahds and Big Tony", "grouped names");
Equal(QuestRules.Names(new List<KeyValuePair<string, bool>> { new("A", false), new("B", false), new("C", false), new("D", false), new("E", false) }), "A, B, C and 2 others", "long name lists");
Equal(QuestRules.Plural("Thief"), "Thieves", "plural Thief");
Equal(QuestRules.Plural("Mafia"), "Mafia", "plural Mafia");
Equal(QuestRules.Plural("Box"), "Boxes", "plural Box");
Equal(QuestRules.Plural("Firefighter"), "Firefighters", "plural Firefighter");
Equal(QuestRules.Objective(QuestType.Kill, TargetKind.Faction, 3, 5, "the Blahds", "", null, false), "take down 3 of the Blahds", "faction objective");
Equal(QuestRules.Objective(QuestType.Kill, TargetKind.Label, 2, 2, "Big Tony and Sal", "", null, false), "take down Big Tony and Sal", "whole-set objective");
Equal(QuestRules.Objective(QuestType.Retrieve, TargetKind.AgentId, 1, 1, "Big Tony", "Briefcase", "Big Tony", false), "bring back the Briefcase (Big Tony has it)", "retrieve objective");
Equal(QuestRules.Objective(QuestType.Retrieve, TargetKind.Object, 2, 1, "the Safe", "Banana", "the Safe", true), "bring back 2 Bananas (they're in the Safe)", "retrieve from an object");
Equal(QuestRules.Objective(QuestType.Destroy, TargetKind.Object, 1, 2, "2 Generators", "", null, false), "wreck 1 of 2 Generators", "destroy objective");
Equal(QuestRules.RewardText(q1.Rewards, n => n, "the Crepes"), "Reward: $150, 3 Bananas, the trust of the Crepes and experience.", "reward text");
Equal(QuestRules.RewardText(new List<QuestReward>(), n => n, null), "", "no reward text");
Equal(QuestRules.Clamp("abcdefgh", 6), "abc...", "clamp");
Equal(QuestRules.MarkerLabel(MarkerKind.Offer, "Big Tony", "Collect the debt"), "Big Tony - job: Collect the debt", "offer marker label");
Equal(QuestRules.MarkerLabel(MarkerKind.Report, "the Bartender", ""), "The Bartender - report back", "report marker label, capitalised, no title");
Equal(QuestRules.MarkerLabel(MarkerKind.Target, null, new string('x', 60)).Length, "Someone - target: ".Length + QuestRules.MaxMarkerTitle, "target marker label clamps its title");
Check(MarkerKind.Report > MarkerKind.Target && MarkerKind.Target > MarkerKind.Offer && MarkerKind.Offer > MarkerKind.Running, "marker priority order");
Equal(QuestRules.Slug("  Cut off -- the Head! "), "cut-off-the-head", "slug");
Check(QuestRules.GateIds(" a + b,c ").SequenceEqual(new[] { "a", "b", "c" }), "gate ids");
Equal(QuestRules.RadiantPay(1, 1), 80, "radiant pay level 1");
Equal(QuestRules.RadiantPay(3, 1.25), 150, "radiant pay rounds to $5");

// Every radiant template, in every variant, parses cleanly and fits with long names filled in.
string longName = new string('N', 40);
Func<string, string?> longVals = n => n switch
{
    "giver" or "target" or "faction" or "rival" or "item" => longName,
    "count" => "99",
    "reward" => "$99999, " + longName + " and experience",
    "objective" => "take down 99 of " + longName,
    _ => null,
};
var radiantNames = new HashSet<string>();
foreach (var t in QuestRules.Radiant)
{
    Check(radiantNames.Add(t.Name), $"radiant template {t.Name} is unique");
    Check(!t.NeedsRival || t.Faction, $"radiant {t.Name}: only faction templates need a rival");
    for (int v = 0; v < t.Variants.Length; v++)
    {
        string target = t.Type == QuestType.Destroy ? "Object:Generator@Blahd" : t.Name == "Hit" ? "Leader:Blahd" : t.Name == "Thin" ? "Faction:Blahd" : "Agent:#7";
        QuestScript rs = QuestRules.Parse(QuestRules.RadiantScript(t, v, "radiant-7", target, "Briefcase", t.Type == QuestType.Kill && t.Name == "Thin" ? 3 : t.Type == QuestType.Destroy ? 1 : 0, 100), ResolveFaction);
        Check(rs.Errors.Count == 0 && rs.Warnings.Count == 0 && rs.Stages.Count == 1, $"radiant {t.Name}/{v} parses cleanly: {string.Join("; ", rs.Errors.Concat(rs.Warnings))}");
        if (rs.Stages.Count != 1) continue;
        QuestStage st = rs.Stages[0];
        Check(st.Id == "radiant-7" && st.Type == t.Type && st.AutoReport == t.Auto, $"radiant {t.Name}/{v} id, type and report");
        Check(st.Rewards.Any(x => x.Kind == RewardKind.Standing) == t.Standing, $"radiant {t.Name}/{v} standing");
        string[] needed = t.Type is QuestType.Deliver or QuestType.Talk ? new[] { "offer", "accept", "remind", "targettext" } : new[] { "offer", "accept", "remind", "done" };
        foreach (string key in needed)
        {
            string? text = st.Text(key);
            Check(text != null, $"radiant {t.Name}/{v} has {key}");
            if (text == null) continue;
            Check(!text.Contains("a {item}") && !text.Contains("this {item}") && !text.Contains("A {item}"), $"radiant {t.Name}/{v} {key} doesn't say \"a {{item}}\"");
            Check(QuestRules.Fill(text, n => longVals(n)!).Length <= 300, $"radiant {t.Name}/{v} {key} stays short with long names");
        }
    }
}
Check(QuestRules.QuestItems.Length >= 5 && QuestRules.QuestItems.All(i => !i.Contains(' ')), "quest items are item names");
Check(QuestRules.Keys.All(k => QuestRules.Normalize(k).Length > 0) && QuestRules.TextKeys.All(k => QuestRules.Keys.Any(x => QuestRules.Normalize(x) == k)), "text keys are keys");

// Level gates: CCU's Agent switches plus RCK's Count and conditions.
LevelGateRule? Gate(string data) => LevelGateRules.TryParse(data, out LevelGateRule? r) ? r : null;
LevelGateRule? gate = Gate("Type=Entry;Switches=Agent,Object;Labels=A,B,C,D;Logic=AND;");
Check(gate != null && gate.Labels.SetEquals(new[] { 1, 2, 3, 4 }) && gate.Logic == GateLogic.And && gate.Count == 0 && gate.Conditions.Count == 0 && gate.Warnings.Count == 0, "CCU's example gate parses");
gate = Gate("Label=1,3;Logic=xor");
Check(gate != null && gate.Labels.SetEquals(new[] { 1, 3 }) && gate.Logic == GateLogic.Xor, "numbered labels, lower-case logic, default type");
gate = Gate("Label=A;Label=B");
Check(gate != null && gate.Labels.SetEquals(new[] { 2 }), "a repeated key keeps the last value");
Check(Gate("Type=Exit;Label=A;") == null, "only Entry gates");
Check(Gate("Type=Entry;") == null && Gate("") == null && Gate("Label=;") == null, "a gate needs a label or a condition");
Check(Gate("Switch=Object;Label=A;") == null, "Object-only CCU switches aren't ours");
gate = Gate("Switch=Object;Label=A;Destroyed=Generator;");
Check(gate != null && gate.Labels.Count == 0 && gate.Conditions.Count == 1 && gate.Warnings.Count == 1, "Object switch with a condition drops its labels with a warning");
gate = Gate("Label=A,Z,0;");
Check(gate != null && gate.Labels.SetEquals(new[] { 1 }) && gate.Warnings.Count == 2, "bad labels warn");
gate = Gate("Label=A,B,C,D;Count=3;");
Equal(gate?.Count ?? -1, 3, "Count parses");
foreach (string badValue in new[] { "0", "100", "three", "-1" })
{
    gate = Gate($"Label=A;Count={badValue};");
    Check(gate != null && gate.Count == 0 && gate.Warnings.Count == 1, $"Count={badValue} warns and falls back to Logic");
}
gate = Gate("Count=2;Routed=Crepe;");
Check(gate != null && gate.Count == 0 && gate.Warnings.Count == 1, "Count without labels warns");
gate = Gate("destroyed=Generator+PowerBox;HOLDING=Briefcase*2;routed=Crepe;quest=a+b;Mystery=x;Label=B;");
Check(gate != null && gate.Conditions.Select(c => c.Key).SequenceEqual(new[] { "Destroyed", "Holding", "Routed", "Quest", "Mystery" }), "conditions keep written order and canonical names");
Check(gate != null && gate.Conditions[0].Value == "Generator+PowerBox", "condition values are kept");
Check(LevelGateRules.IsKnown("destroyed") && LevelGateRules.IsKnown("Quest") && !LevelGateRules.IsKnown("Mystery"), "IsKnown");
gate = Gate("Holding=;");
Check(gate != null && gate.Warnings.Count == 1, "an empty condition warns");
gate = Gate("Type=Entry;TurfTaken=Blahd+Mafia;Open=The Crepes run the city. The exit is open.;");
Check(gate != null && gate.Open == "The Crepes run the city. The exit is open." && gate.Conditions.Count == 1 && gate.Warnings.Count == 0, "Open= is text, not a condition");
gate = Gate("Label=A;open= Odds = evens ;");
Check(gate != null && gate.Open == "Odds = evens" && gate.Conditions.Count == 0, "Open= is case-blind and keeps later '='");
Check(Gate("Open=hi;") == null, "Open= alone isn't a gate");
gate = Gate("Label=A;Open=" + new string('x', LevelGateRules.MaxOpen + 30) + ";");
Check(gate != null && gate.Open!.Length == LevelGateRules.MaxOpen && gate.Warnings.Count == 1, "Open text over 120 characters is cut with a warning");
gate = Gate("Label=A;Open=;");
Check(gate != null && gate.Open == null && gate.Warnings.Count == 1, "an empty Open= warns");
Check(Gate("Label=A;")!.Open == null, "no Open= means no text");
Check(LevelGateRules.SplitArg(" A + B,C ,").SequenceEqual(new[] { "A", "B", "C" }) && LevelGateRules.SplitArg("").Count == 0, "SplitArg");
foreach ((string token, string name, int count) in new[] { ("Briefcase", "Briefcase", 1), ("Briefcase*2", "Briefcase", 2), ("Key x3", "Key", 3), (" Money * 5 ", "Money", 5) })
    Check(LevelGateRules.TryParseItem(token, out string n, out int c) && n == name && c == count, $"item '{token}'");
foreach (string badValue in new[] { "", "*2", "Briefcase*0", "Briefcase*x", "Brief case", "Key*100" })
    Check(!LevelGateRules.TryParseItem(badValue, out _, out _), $"item '{badValue}' is refused");
bool[] noAgents = { }, t0 = { false, false, false }, t1 = { true, false, false }, t2 = { true, true, false }, t3 = { true, true, true };
foreach (GateLogic logic in Enum.GetValues<GateLogic>())
    Check(!LevelGateRules.Combine(noAgents, logic, 0) && !LevelGateRules.Combine(noAgents, logic, 1), $"{logic} with no agents stays shut");
foreach ((GateLogic logic, bool[] want) in new[]
         {
             (GateLogic.And, new[] { false, false, false, true }), (GateLogic.Or, new[] { false, true, true, true }),
             (GateLogic.Nand, new[] { true, true, true, false }), (GateLogic.Nor, new[] { true, false, false, false }),
             (GateLogic.Xor, new[] { false, true, false, false }), (GateLogic.Xnor, new[] { true, false, true, true }),
         })
{
    bool[][] rows = { t0, t1, t2, t3 };
    for (int i = 0; i < rows.Length; i++)
        Equal(LevelGateRules.Combine(rows[i], logic, 0), want[i], $"{logic} with {i} of 3 resolved");
}
Check(!LevelGateRules.Combine(t1, GateLogic.And, 2) && LevelGateRules.Combine(t2, GateLogic.Nor, 2) && LevelGateRules.Combine(t3, GateLogic.Xor, 2), "Count overrides Logic");
Check(!LevelGateRules.Combine(t3, GateLogic.And, 4), "Count above the agent count stays shut");

// Faction respawn: [RCK]FactionRespawn:: entries.
var respawnErrors = new List<string>();
RespawnConfig Respawn(params string[] bodies)
{
    respawnErrors.Clear();
    return RespawnRules.Parse(bodies, KeyIndex, (text, err) => respawnErrors.Add(text + ": " + err));
}
RespawnConfig rc = Respawn();
Check(rc.Factions.Count == 0 && respawnErrors.Count == 0, "no entries, no factions");
RespawnSettings rd = rc.For(KeyIndex("Crepe"));
Check(rd.Free == 2 && rd.PerTurf == 2 && rd.Max == 12 && rd.Cooldown == 60 && rd.Size == 3, "respawn defaults");
Check(rc.Entry(KeyIndex("Crepe")) == null, "an unlisted faction has no entry");
Equal(rd.Target(0), 0, "no turf, no target");
Equal(rd.Target(1), 4, "target with 1 turf");
Equal(rd.Target(3), 8, "target with 3 turfs");
Equal(rd.Target(9), 12, "target is capped at Max");
rc = Respawn("Crepe=inf;Blahd=20;Cop=off;Faction_3=0");
Check(respawnErrors.Count == 0, "pools parse: " + string.Join(" | ", respawnErrors));
Check(rc.Entry(KeyIndex("Crepe")) is { Listed: true, Pool: RespawnRules.Unlimited }, "inf pool");
Check(rc.Entry(KeyIndex("Blahd")) is { Listed: true, Pool: 20 }, "counted pool");
Check(rc.Entry(KeyIndex("Cop")) is { Listed: true, Pool: 0 } && rc.Entry(KeyIndex("Faction_3")) is { Pool: 0 }, "off and 0 pools");
rc = Respawn(" crepe = INF : Gangbanger + Crepe Heavy + Gangbanger ; ");
RespawnFaction? rf = rc.Entry(KeyIndex("Crepe"));
Check(respawnErrors.Count == 0 && rf != null && rf.Units.SequenceEqual(new[] { "Gangbanger", "Crepe Heavy", "Gangbanger" }), "units keep order, repeats and inner spaces");
rc = Respawn("Cooldown=45;Size=4;Crepe=inf;Crepe.Size=6;crepe.cooldown=10;Blahd=5");
Check(respawnErrors.Count == 0, "settings parse: " + string.Join(" | ", respawnErrors));
Check(rc.For(KeyIndex("Crepe")) is { Cooldown: 10, Size: 6, Free: 2 }, "per-faction overrides apply over the defaults");
Check(rc.For(KeyIndex("Blahd")) is { Cooldown: 45, Size: 4 }, "level defaults apply to listed factions");
Check(rc.For(KeyIndex("Cop")) is { Cooldown: 45, Size: 4 } && rc.Entry(KeyIndex("Cop")) == null, "level defaults apply to unlisted factions");
rc = Respawn("Crepe.Size=2;Size=5");
Check(rc.For(KeyIndex("Crepe")).Size == 2 && rc.Entry(KeyIndex("Crepe")) is { Listed: false }, "an override alone doesn't list the faction, and beats a later default");
rc = Respawn("Crepe=10:Gangbanger", "Crepe=inf:Soldier+Cop");
Check(rc.Entry(KeyIndex("Crepe")) is { Pool: RespawnRules.Unlimited } e1 && e1.Units.SequenceEqual(new[] { "Soldier", "Cop" }), "a later entry replaces the pool and units");
rc = Respawn("Max=30;Max=1;Free=0;PerTurf=10;Cooldown=600");
Check(respawnErrors.Count == 0 && rc.Defaults is { Max: 1, Free: 0, PerTurf: 10, Cooldown: 600 }, "range edges are allowed and later defaults win");
Equal(rc.Defaults.Target(2), 1, "Max 1 caps the target");
foreach ((string entry, string want) in new[]
         {
             ("Crepe", "expected Faction=pool or Setting=number"),
             ("=5", "expected Faction=pool or Setting=number"),
             ("Nobody=inf", "unknown faction or setting \"Nobody\""),
             ("Nobody.Size=2", "unknown faction \"Nobody\""),
             (".Size=2", "unknown faction \"\""),
             ("Crepe.Speed=2", "unknown setting \"Speed\" (Free, PerTurf, Max, Cooldown or Size)"),
             ("Size=0", "Size must be 1 to 6"),
             ("Size=7", "Size must be 1 to 6"),
             ("Cooldown=9", "Cooldown must be 10 to 600"),
             ("Free=-1", "Free must be 0 to 30"),
             ("Max=x", "Max must be 1 to 30"),
             ("Crepe.PerTurf=11", "PerTurf must be 0 to 10"),
             ("Crepe=1000", "pool must be inf, off or 0 to 999"),
             ("Crepe=-3", "pool must be inf, off or 0 to 999"),
             ("Crepe=lots", "pool must be inf, off or 0 to 999"),
             ("Crepe=:Gangbanger", "pool must be inf, off or 0 to 999"),
             ("Crepe=inf:Gangbanger++Cop", "empty unit name"),
             ("Crepe=inf:", "empty unit name"),
             ("Crepe=inf:" + new string('x', 65), "unit names are at most 64 characters"),
             ("Crepe=inf:" + string.Join("+", Enumerable.Repeat("Cop", 13)), "at most 12 units"),
         })
{
    rc = Respawn(entry);
    Check(respawnErrors.Count == 1 && respawnErrors[0].EndsWith(": " + want, StringComparison.Ordinal), $"respawn entry '{entry}' is refused with '{want}': [{string.Join(" | ", respawnErrors)}]");
    Check(rc.Factions.Values.All(f => !f.Listed), $"refused entry '{entry}' lists nothing");
}
rc = Respawn("Crepe=inf:" + new string('x', 64) + ";Size=9;Blahd=3");
Check(respawnErrors.Count == 1 && rc.Entry(KeyIndex("Crepe")) is { Listed: true } && rc.Entry(KeyIndex("Blahd")) is { Pool: 3 } && rc.Defaults.Size == 3, "a bad entry is skipped and the rest still apply");
rc = Respawn("Crepe=inf:" + string.Join("+", Enumerable.Repeat("Cop", 12)));
Check(respawnErrors.Count == 0 && rc.Entry(KeyIndex("Crepe"))!.Units.Count == 12, "12 units are allowed");
rc = Respawn("Crepe=inf;Crepe.Size=9");
Check(respawnErrors.Count == 1 && rc.For(KeyIndex("Crepe")).Size == 3, "a bad override is skipped");

// Faction war rule strings: [RCK]TurfWar::, [RCK]ControlPoints:: and [RCK]Command::.
var warErrors = new List<string>();
void WarError(string text, string error) => warErrors.Add(error);
TurfWarSettings TurfWarOf(string body) { warErrors.Clear(); return TurfWarRules.Parse(new[] { body }, WarError); }
ControlPointSettings PointsOf(string body) { warErrors.Clear(); return ControlPointRules.Parse(new[] { body }, WarError); }
CommandSettings CommandOf(string body) { warErrors.Clear(); return CommandRules.Parse(new[] { body }, KeyIndex, WarError); }

TurfWarSettings tw = TurfWarOf("");
Check(!tw.Listed && tw.Expand == 45 && tw.Racket == 1 && tw.Reach == 30 && tw.Party == 3 && tw.Reopen == 3 && warErrors.Count == 0, "TurfWar defaults");
tw = TurfWarOf("Expand=5");
Check(tw.Listed && tw.Expand == TurfWarRules.MinExpand && warErrors.Count == 0, "Expand under 15 counts as 15");
tw = TurfWarOf("expand=0;RACKET=0");
Check(tw.Listed && tw.Expand == 0 && tw.Racket == 0 && warErrors.Count == 0, "Expand=0 and Racket=0, names case-insensitive");
tw = TurfWarOf("Party=9;Reach=50");
Check(warErrors.Count == 1 && warErrors[0] == "Party must be 1 to 6" && tw.Party == 3 && tw.Reach == 50, "Party=9 is out of range and the rest applies");
tw = TurfWarOf("Speed=3");
Check(warErrors.Count == 1 && warErrors[0].StartsWith("unknown setting \"speed\"") && tw.Listed, "an unknown TurfWar setting is refused");
tw = TurfWarOf("Expand");
Check(warErrors.Count == 1 && warErrors[0] == "expected Name=Value" && !tw.Listed, "an entry without = is refused");
tw = TurfWarOf("Reach=20;Reach=40");
Check(tw.Reach == 40, "later entries win");
tw = TurfWarOf("Reach=-5");
Check(warErrors.Count == 1 && tw.Reach == 30, "a negative number is refused");
tw = TurfWarOf("Reopen=0");
Check(tw.Reopen == 0 && warErrors.Count == 0, "Reopen=0 keeps ruined rackets ruined");
tw = TurfWarOf("Reopen=12");
Check(warErrors.Count == 1 && warErrors[0] == "Reopen must be 0 to 9" && tw.Reopen == 3, "Reopen=12 is out of range");

ControlPointSettings cp = PointsOf("");
Check(!cp.Listed && cp.Base == 1 && cp.PerTurf == 2 && cp.PerCommon == 1 && cp.Capture == 10 && cp.Kill == 1 && cp.Max == 200 && cp.Interval == 10 && cp.Start == 30, "ControlPoints defaults");
Equal(cp.Income(3, 2), 1 + 2 * 3 + 1 * 2, "income for 3 turfs and 2 rackets");
Equal(cp.Income(-1, 0), 1, "income never goes under Base");
cp = PointsOf("Start=500;Max=100");
Check(cp.Listed && cp.Max == 100 && cp.Start == 100 && warErrors.Count == 0, "Start is capped at Max");
cp = PointsOf("Interval=2;Max=5;PerTurf=3");
Check(warErrors.Count == 2 && cp.Interval == 10 && cp.Max == 200 && cp.PerTurf == 3, "Interval and Max ranges are enforced");

CommandSettings cmd = CommandOf("");
Check(!cmd.Listed && cmd.Faction == -1 && cmd.Units.Count == 0 && cmd.Cost == 10 && cmd.Size == 3 && cmd.Cap == 12 && cmd.Refund == 50, "Command defaults");
cmd = CommandOf("Faction=Crepe;Units=Gangbanger+Crepe Heavy:25;Cost=15");
Check(warErrors.Count == 0 && cmd.Listed && cmd.Faction == KeyIndex("Crepe") && cmd.Cost == 15, "Command faction and cost");
Check(cmd.Units.Count == 2 && cmd.Units[0].Name == "Gangbanger" && cmd.Units[0].Cost == -1 && cmd.Units[1].Name == "Crepe Heavy" && cmd.Units[1].Cost == 25, "Command units with a cost");
Check(cmd.CostOf(cmd.Units[0]) == 15 && cmd.CostOf(cmd.Units[1]) == 25, "a unit's own cost beats Cost");
Equal(string.Join("+", cmd.Units), "Gangbanger+Crepe Heavy:25", "units print back");
cmd = CommandOf("Units=A:B+Thug:0+Boss:7");
Check(warErrors.Count == 0 && cmd.Units.Count == 3 && cmd.Units[0].Name == "A:B" && cmd.Units[0].Cost == -1 && cmd.Units[1].Cost == 0 && cmd.Units[2].Name == "Boss" && cmd.Units[2].Cost == 7, "a colon without digits stays in the name");
cmd = CommandOf("Faction=3");
Check(warErrors.Count == 0 && cmd.Faction == KeyIndex("Faction_3"), "a faction by number");
cmd = CommandOf("Faction=Nobody;Size=4");
Check(warErrors.Count == 1 && warErrors[0] == "unknown faction \"Nobody\"" && cmd.Faction == -1 && cmd.Size == 4, "an unknown faction is refused");
cmd = CommandOf("Units=" + string.Join("+", Enumerable.Repeat("Cop", 13)));
Check(warErrors.Count == 1 && warErrors[0] == "at most 12 units" && cmd.Units.Count == 0, "13 units are refused");
cmd = CommandOf("Units=" + string.Join("+", Enumerable.Repeat("Cop", 12)));
Check(warErrors.Count == 0 && cmd.Units.Count == 12, "12 units are allowed");
cmd = CommandOf("Units=" + new string('x', 65));
Check(warErrors.Count == 1 && warErrors[0] == "unit names are at most 64 characters", "a 65-character unit name is refused");
cmd = CommandOf("Units=Cop:999");
Check(warErrors.Count == 1 && warErrors[0].Contains("a unit's cost is 0-500"), "a unit cost over 500 is refused");
cmd = CommandOf("Units=Cop++Thief");
Check(warErrors.Count == 1 && warErrors[0] == "empty unit name", "an empty unit name is refused");
cmd = CommandOf("Units=Cop;Units=Thief+Hacker");
Check(cmd.Units.Count == 2 && cmd.Units[0].Name == "Thief", "a later Units entry replaces the list");
cmd = CommandOf("Cost=abc;Cap=30;Refund=100;Morale=5");
Check(warErrors.Count == 3 && cmd.Cost == 10 && cmd.Cap == 12 && cmd.Refund == 100 && warErrors[2].StartsWith("unknown setting \"morale\""), "bad Command settings are refused");

// Level boundaries: one tracker decides when a level ends and when the next one has loaded.
{
    var gcA = new object();
    var gcB = new object();
    var lt = new RCK.LevelTracker();
    Equal(lt.Observe(gcA, 1, true), RCK.LevelStep.Ended | RCK.LevelStep.Loaded, "the first loaded look ends and loads at once");
    int id = lt.Id, stamp = lt.Stamp;
    Check(id == 1 && !lt.Loading && lt.Level == 1, "the first level has id 1 and level 1");
    Equal(lt.Observe(gcA, 1, true), RCK.LevelStep.None, "a repeat look finds nothing new");
    Equal(lt.Observe(gcA, 1, false), RCK.LevelStep.Ended, "a load beginning ends the level");
    Check(lt.Loading && lt.Id == id + 1 && lt.Stamp == stamp + 1, "a load beginning gives a new id and stamp");
    Equal(lt.Observe(gcA, 1, false), RCK.LevelStep.None, "a repeat look while loading finds nothing new");
    Equal(lt.Observe(gcA, 2, false), RCK.LevelStep.None, "a level number changing mid-load is the same load");
    id = lt.Id;
    stamp = lt.Stamp;
    Equal(lt.Observe(gcA, 2, true), RCK.LevelStep.Loaded, "the load finishing loads the level");
    Check(lt.Id == id && lt.Stamp == stamp + 1 && !lt.Loading && lt.Level == 2, "the id holds from end through load, the stamp moves at both");
    Equal(lt.Observe(gcA, 2, false), RCK.LevelStep.Ended, "restarting the same level ends it");
    Equal(lt.Observe(gcA, 2, true), RCK.LevelStep.Loaded, "and loads it again");
    Check(lt.Id == id + 1, "a same-level restart gets a new id");
    Equal(lt.Observe(gcB, 2, true), RCK.LevelStep.Ended | RCK.LevelStep.Loaded, "a new controller ends and loads at once");
    Equal(lt.Observe(gcB, 3, true), RCK.LevelStep.Ended | RCK.LevelStep.Loaded, "a level change seen without a load ends and loads at once");
    Check(lt.Id == id + 3 && lt.Level == 3, "each unseen change gets its own id");
}

// The generated spec agrees with the code.
string root = args.Length > 0 ? args[0] : FindRoot();
string json = Path.Combine(root, "docs", "ccu-interface.json");
if (File.Exists(json))
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(json));
    JsonElement factions = doc.RootElement.GetProperty("extensions").GetProperty("factions");
    string[] precedence = factions.GetProperty("precedence").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
    string[] expected = strongestFirst.Where(g => g != FactionGrade.None).Select(g => g.ToString()).ToArray();
    Check(precedence.SequenceEqual(expected), $"spec precedence [{string.Join(", ", precedence)}] matches the code [{string.Join(", ", expected)}]");
    int grades = 0;
    foreach (JsonProperty grade in factions.GetProperty("grades").EnumerateObject())
    {
        grades++;
        if (!FactionRules.TryParseGrade(grade.Name, out FactionGrade g)) { Check(false, $"spec grade suffix {grade.Name} parses"); continue; }
        Equal(FactionRules.RelOf(g), grade.Value.GetString(), $"spec grade {grade.Name} relationship");
    }
    Check(factions.GetProperty("grades").TryGetProperty("Territorial", out _), "spec has the Territorial grade");
    Check(factions.TryGetProperty("territorial", out JsonElement text) && (text.GetString() ?? "").Length > 0, "spec explains Territorial");
    Check(factions.TryGetProperty("private_access", out JsonElement access) && (access.GetString() ?? "").Contains("_Member"), "spec explains private access");
    Check(doc.RootElement.GetProperty("extensions").TryGetProperty("party_peace", out JsonElement peace) && (peace.GetString() ?? "").Contains("employer"), "spec explains party peace");
    Check(grades >= 6, "spec lists at least 6 grades");

    // Role traits: one Calls_Backup, Leader, Reinforcement and Vengeful per key, none of them a grade (they never set a
    // relationship at pair setup) and none cancelling anything, plus the single defector trait.
    string[] keyNames = factions.GetProperty("keys").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
    string[] roles = factions.GetProperty("roles").EnumerateObject().Select(p => p.Name).ToArray();
    Check(roles.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(new[] { "Calls_Backup", "Leader", "Reinforcement", "Vengeful" }), $"spec roles are Calls_Backup, Leader, Reinforcement and Vengeful: [{string.Join(", ", roles)}]");
    foreach (string role in roles)
        Check(!factions.GetProperty("grades").TryGetProperty(role, out _) && !FactionRules.TryParseGrade(role, out _), $"role {role} is not a grade");
    JsonElement traits = doc.RootElement.GetProperty("traits");
    int roleTraits = 0;
    foreach (JsonProperty t in traits.EnumerateObject())
    {
        if (!t.Value.TryGetProperty("faction", out JsonElement f) || f.ValueKind != JsonValueKind.Object || !f.TryGetProperty("role", out JsonElement role)) continue;
        roleTraits++;
        string key = f.GetProperty("key").GetString() ?? "";
        Equal(t.Name, key + "_" + role.GetString(), $"role trait {t.Name} is named <key>_<role>");
        Check(keyNames.Contains(key), $"role trait {t.Name} has a known key");
        Check(t.Value.GetProperty("unlock").GetProperty("Cancellations").GetArrayLength() == 0, $"role trait {t.Name} cancels nothing");
    }
    Equal(roleTraits, keyNames.Length * roles.Length, "one trait per key and role");
    string defector = factions.GetProperty("defector").GetProperty("trait").GetString() ?? "";
    Check(traits.TryGetProperty(defector, out JsonElement dt) && dt.TryGetProperty("defector", out JsonElement dflag) && dflag.GetBoolean(), $"spec has the defector trait {defector}");
    foreach (string section in new[] { "vengeful", "leader" })
        Check(factions.TryGetProperty(section, out JsonElement rule) && (rule.GetString() ?? "").Contains("_" + char.ToUpperInvariant(section[0]) + section.Substring(1)), $"spec explains {section}");
    Check((factions.GetProperty("defector").GetProperty("rules").GetString() ?? "").Contains(defector), "spec explains the defector");

    JsonElement recruit = factions.GetProperty("recruit");
    Equal(recruit.GetProperty("matrix").GetProperty("prefix").GetString(), "[RCK]FactionRecruit::", "spec recruit prefix");
    foreach (JsonElement p in recruit.GetProperty("policies").EnumerateArray())
        Check(RecruitMatrix.TryParsePolicy(p.GetString(), out _), $"spec recruit policy {p.GetString()} parses");
    foreach (string section in new[] { "traits", "mutators" })
        foreach (JsonProperty entry in recruit.GetProperty(section).EnumerateObject())
            Check(RecruitMatrix.TryParsePolicy(entry.Value.GetString(), out _), $"spec recruit {section} {entry.Name} has a known policy");

    JsonElement ext = doc.RootElement.GetProperty("extensions");
    if (ext.TryGetProperty("disguises", out JsonElement disguises))
    {
        Equal(disguises.GetProperty("mutator").GetString(), DisguiseRules.Mutator, "spec disguise mutator");
        Equal(disguises.GetProperty("prefix").GetString(), DisguiseRules.Prefix, "spec disguise prefix");
        var specDefaults = disguises.GetProperty("defaults").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        Check(specDefaults.Count == DisguiseRules.Defaults.Length && DisguiseRules.Defaults.All(d => specDefaults.GetValueOrDefault(d.Key) == d.Value),
            "spec disguise defaults match the code");
        foreach (KeyValuePair<string, string> d in DisguiseRules.Defaults)
            Check(keyNames.Contains(d.Value), $"disguise default {d.Key} names a faction key ({d.Value})");
        Check(doc.RootElement.GetProperty("mutators").TryGetProperty(DisguiseRules.Mutator, out _), "spec lists the disguise mutator");
    }
    else Check(false, "spec has extensions.disguises");
    Check(ext.TryGetProperty("faction_names", out JsonElement fnames) && fnames.GetProperty("prefix").GetString() == FactionNameRules.Prefix, "spec faction-name prefix");

    if (ext.TryGetProperty("quests", out JsonElement quests))
    {
        Equal(quests.GetProperty("prefix").GetString(), QuestRules.Prefix, "spec quest prefix");
        Check(quests.GetProperty("keys").EnumerateArray().Select(e => e.GetString()).SequenceEqual(QuestRules.Keys), "spec quest keys match the code");
        Check(quests.GetProperty("placeholders").EnumerateObject().Select(p => p.Name).OrderBy(x => x).SequenceEqual(QuestRules.Placeholders.OrderBy(x => x)), "spec quest placeholders match the code");
        foreach (JsonProperty type in quests.GetProperty("types").EnumerateObject())
            Check(QuestRules.TypeNames.ContainsKey(type.Name), $"spec quest type {type.Name} is known");
        Equal(quests.GetProperty("types").EnumerateObject().Count(), Enum.GetValues<QuestType>().Length, "spec lists every quest type");
        foreach (JsonProperty target in quests.GetProperty("targets").EnumerateObject())
        {
            string sample = target.Name.Replace("<faction>", "Blahd").Replace("Name[@owner]", "Generator@Rival").Replace("#id", "#3");
            Check(QuestRules.TryParseTarget(sample, ResolveFaction, out _, out string? terr), $"spec quest target {target.Name} parses ({terr})");
        }
        foreach (JsonProperty reward in quests.GetProperty("rewards").EnumerateObject())
            Check(QuestRules.TryParseRewards(reward.Name.Replace("$N", "$5").Replace("Item:Name", "Item:Banana"), new List<QuestReward>(), out _), $"spec quest reward {reward.Name} parses");
        Equal(quests.GetProperty("max_text").GetInt32(), QuestRules.MaxText, "spec quest text limit");
        Equal(quests.GetProperty("gate_switch").GetProperty("name").GetString(), QuestRules.GateSwitch, "spec quest gate switch");
        JsonElement radiant = quests.GetProperty("radiant");
        Equal(radiant.GetProperty("mutator").GetString(), QuestRules.RadiantMutator, "spec radiant mutator");
        Equal(radiant.GetProperty("trait").GetString(), QuestRules.RadiantGiverTrait, "spec radiant trait");
        Check(doc.RootElement.GetProperty("mutators").TryGetProperty(QuestRules.RadiantMutator, out _), "spec lists the radiant mutator");
        var questTraits = new List<string> { QuestRules.RadiantGiverTrait, QuestRules.NoQuestsTrait };
        questTraits.AddRange(QuestRules.Labels.Select(l => QuestRules.TargetTraitPrefix + l));
        foreach (string trait in questTraits)
            Check(traits.TryGetProperty(trait, out JsonElement qt) && qt.TryGetProperty("quests", out _) && quests.GetProperty("traits").TryGetProperty(trait, out _), $"spec has quest trait {trait}");
        Check(quests.GetProperty("target_labels").EnumerateArray().Select(e => e.GetString()).SequenceEqual(QuestRules.Labels), "spec quest target labels");
    }
    else Check(false, "spec has extensions.quests");

    if (ext.TryGetProperty("goals", out JsonElement goalsExt))
    {
        string[] listed = goalsExt.GetProperty("goals").EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
        string[] wander = doc.RootElement.GetProperty("goals").EnumerateObject().Select(p => p.Name)
            .Where(n => n.StartsWith("Random Patrol") || n.StartsWith("Wander Between")).OrderBy(x => x).ToArray();
        Check(listed.Length == 7 && listed.SequenceEqual(wander), $"spec explains every patrol/wander goal: [{string.Join(", ", listed)}]");
    }
    else Check(false, "spec has extensions.goals");

    if (ext.TryGetProperty("gates", out JsonElement gates))
    {
        string[] conds = gates.GetProperty("conditions").EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
        Check(conds.SequenceEqual(LevelGateRules.BuiltIn.Concat(LevelGateRules.Registered).OrderBy(x => x)), $"spec gate conditions match the code: [{string.Join(", ", conds)}]");
        Check(gates.GetProperty("keys").EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "Type", "Label", "Switch", "Logic", "Count", "Open" }), "spec gate keys");
        Check(gates.GetProperty("prefixes").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "[CCU]LevelGate::", "[RCK]LevelGate::" }), "spec gate prefixes");
        foreach (JsonElement ex in gates.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            int cut = s.IndexOf("::", StringComparison.Ordinal);
            LevelGateRule? exRule = cut > 0 ? Gate(s.Substring(cut + 2)) : null;
            Check(exRule != null && exRule.Warnings.Count == 0 && exRule.Conditions.All(c => LevelGateRules.IsKnown(c.Key)), $"spec gate example {s} parses cleanly");
        }
    }
    else Check(false, "spec has extensions.gates");

    // Faction war: turf capture, raids, backup and the broker.
    JsonElement specMutators = doc.RootElement.GetProperty("mutators");
    if (ext.TryGetProperty("turf", out JsonElement turf))
    {
        string turfMutator = turf.GetProperty("mutator").GetString() ?? "";
        Equal(turfMutator, "RCK_Turf_Capture", "spec turf mutator");
        Check(specMutators.TryGetProperty(turfMutator, out _), "spec lists the turf mutator");
        Equal(turf.GetProperty("gate_switch").GetProperty("name").GetString(), "TurfTaken", "spec turf gate switch");
        Check(LevelGateRules.Registered.Contains("TurfTaken") && gates.GetProperty("conditions").TryGetProperty("TurfTaken", out _), "TurfTaken is a registered gate condition");
    }
    else Check(false, "spec has extensions.turf");
    if (ext.TryGetProperty("raids", out JsonElement raids))
    {
        Equal(raids.GetProperty("mutator").GetString(), RaidRules.Mutator, "spec raid mutator");
        Equal(raids.GetProperty("prefix").GetString(), RaidRules.Prefix, "spec raid prefix");
        Check(specMutators.TryGetProperty(RaidRules.Mutator, out _), "spec lists the raid mutator");
        JsonElement raidDefaults = raids.GetProperty("defaults");
        Equal(raidDefaults.GetProperty("size").GetInt32(), RaidRules.DefaultSize, "spec raid size");
        Equal(raidDefaults.GetProperty("max_size").GetInt32(), RaidRules.MaxSize, "spec raid max size");
        Equal(raidDefaults.GetProperty("max_auto").GetInt32(), RaidRules.MaxAuto, "spec raid auto cap");
        int[] first = raidDefaults.GetProperty("first_seconds").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        int[] gap = raidDefaults.GetProperty("gap_seconds").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Check(first.SequenceEqual(new[] { RaidRules.FirstMin, RaidRules.FirstMax }) && gap.SequenceEqual(new[] { RaidRules.GapMin, RaidRules.GapMax }), "spec raid timings match the code");
        foreach (JsonElement ex in raids.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            var raidErrors = new List<string>();
            List<RaidEntry> parsed = s.StartsWith(RaidRules.Prefix, StringComparison.Ordinal)
                ? RaidRules.Parse(new[] { s.Substring(RaidRules.Prefix.Length) }, KeyIndex, (x, err) => raidErrors.Add(err))
                : new List<RaidEntry>();
            Check(parsed.Count > 0 && raidErrors.Count == 0, $"spec raid example {s} parses cleanly");
        }
    }
    else Check(false, "spec has extensions.raids");
    if (ext.TryGetProperty("backup", out JsonElement backup))
    {
        Equal(backup.GetProperty("role").GetString(), "Calls_Backup", "spec backup role");
        Check(roles.Contains("Calls_Backup"), "the backup role is a faction role");
        JsonElement limits = backup.GetProperty("limits");
        Check(limits.GetProperty("squad").GetInt32() == 3 && limits.GetProperty("calls_per_level").GetInt32() == 2 && limits.GetProperty("cooldown_seconds").GetInt32() == 30, "spec backup limits");
    }
    else Check(false, "spec has extensions.backup");
    if (ext.TryGetProperty("respawn", out JsonElement respawn))
    {
        Equal(respawn.GetProperty("mutator").GetString(), RespawnRules.Mutator, "spec respawn mutator");
        Equal(respawn.GetProperty("prefix").GetString(), RespawnRules.Prefix, "spec respawn prefix");
        Equal(respawn.GetProperty("role").GetString(), RespawnRules.Role, "spec respawn role");
        Check(roles.Contains(RespawnRules.Role), "the respawn role is a faction role");
        Check(specMutators.TryGetProperty(RespawnRules.Mutator, out _), "spec lists the respawn mutator");
        foreach (string field in new[] { "rules", "format", "units" })
            Check((respawn.GetProperty(field).GetString() ?? "").Length > 100, $"spec explains respawn {field}");
        JsonElement rdefs = respawn.GetProperty("defaults");
        var defaultsNow = new RespawnSettings();
        Check(rdefs.GetProperty("Free").GetInt32() == defaultsNow.Free && rdefs.GetProperty("PerTurf").GetInt32() == defaultsNow.PerTurf
              && rdefs.GetProperty("Max").GetInt32() == defaultsNow.Max && rdefs.GetProperty("Cooldown").GetInt32() == defaultsNow.Cooldown
              && rdefs.GetProperty("Size").GetInt32() == defaultsNow.Size && rdefs.GetProperty("pool").GetString() == "inf", "spec respawn defaults match the code");
        JsonElement rranges = respawn.GetProperty("ranges");
        foreach (KeyValuePair<string, (string Name, int Min, int Max)> range in RespawnRules.Ranges)
        {
            int[] span = rranges.GetProperty(range.Value.Name).EnumerateArray().Select(e => e.GetInt32()).ToArray();
            Check(span.SequenceEqual(new[] { range.Value.Min, range.Value.Max }), $"spec respawn range {range.Value.Name} matches the code");
        }
        Check(rranges.GetProperty("pool").EnumerateArray().Select(e => e.GetInt32()).SequenceEqual(new[] { 0, RespawnRules.MaxPool }), "spec respawn pool range");
        Equal(rranges.EnumerateObject().Count(), RespawnRules.Ranges.Count + 1, "spec lists every respawn range");
        JsonElement rlimits = respawn.GetProperty("limits");
        Equal(rlimits.GetProperty("max_respawned_alive").GetInt32(), RespawnRules.MaxLiving, "spec respawn living cap");
        Equal(rlimits.GetProperty("max_units_per_entry").GetInt32(), RespawnRules.MaxUnits, "spec respawn unit cap");
        foreach (JsonElement ex in respawn.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            RespawnConfig parsed = s.StartsWith(RespawnRules.Prefix, StringComparison.Ordinal)
                ? Respawn(s.Substring(RespawnRules.Prefix.Length))
                : new RespawnConfig();
            Check(parsed.Factions.Values.Any(f => f.Listed) && respawnErrors.Count == 0, $"spec respawn example {s} parses cleanly");
        }
        int reinforcement = 0;
        foreach (JsonProperty t in traits.EnumerateObject())
            if (t.Name.EndsWith("_" + RespawnRules.Role, StringComparison.Ordinal) && t.Value.TryGetProperty("faction", out JsonElement rfac)
                && rfac.GetProperty("role").GetString() == RespawnRules.Role) reinforcement++;
        Equal(reinforcement, keyNames.Length, "one Reinforcement trait per faction key");
    }
    else Check(false, "spec has extensions.respawn");
    if (ext.TryGetProperty("broker", out JsonElement broker))
    {
        string brokerTrait = broker.GetProperty("trait").GetString() ?? "";
        Check(traits.TryGetProperty(brokerTrait, out JsonElement bt) && bt.TryGetProperty("broker", out JsonElement bflag) && bflag.GetBoolean(), $"spec has the broker trait {brokerTrait}");
        Check(broker.GetProperty("buttons").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "Broker a truce", "Frame a faction", "Change first faction", "Change second faction" }), "spec broker buttons");
    }
    else Check(false, "spec has extensions.broker");

    // Expansion, control points, the commander, the war panel and the medic.
    if (ext.TryGetProperty("expansion", out JsonElement expansion))
    {
        Equal(expansion.GetProperty("mutator").GetString(), TurfWarRules.Mutator, "spec expansion mutator");
        Equal(expansion.GetProperty("prefix").GetString(), TurfWarRules.Prefix, "spec expansion prefix");
        Check(specMutators.TryGetProperty(TurfWarRules.Mutator, out JsonElement em) && em.GetProperty("extension").GetBoolean(), "spec lists the expansion mutator");
        var d = new TurfWarSettings();
        JsonElement edefs = expansion.GetProperty("defaults");
        Check(edefs.GetProperty("Expand").GetInt32() == d.Expand && edefs.GetProperty("Racket").GetInt32() == d.Racket
              && edefs.GetProperty("Reach").GetInt32() == d.Reach && edefs.GetProperty("Party").GetInt32() == d.Party
              && edefs.GetProperty("Reopen").GetInt32() == d.Reopen, "spec expansion defaults match the code");
        Equal(expansion.GetProperty("limits").GetProperty("min_expand_seconds").GetInt32(), TurfWarRules.MinExpand, "spec expansion minimum");
        CheckRanges(expansion.GetProperty("ranges"), TurfWarRules.Ranges, "expansion");
        Check((expansion.GetProperty("rackets").GetString() ?? "").Length > 200, "spec explains rackets");
        foreach (JsonElement ex in expansion.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            TurfWarSettings parsed = s.StartsWith(TurfWarRules.Prefix, StringComparison.Ordinal) ? TurfWarOf(s.Substring(TurfWarRules.Prefix.Length)) : new TurfWarSettings();
            Check(parsed.Listed && warErrors.Count == 0, $"spec expansion example {s} parses cleanly");
        }
    }
    else Check(false, "spec has extensions.expansion");
    if (ext.TryGetProperty("control_points", out JsonElement points))
    {
        Equal(points.GetProperty("prefix").GetString(), ControlPointRules.Prefix, "spec control point prefix");
        var d = new ControlPointSettings();
        JsonElement pdefs = points.GetProperty("defaults");
        Check(pdefs.GetProperty("Base").GetInt32() == d.Base && pdefs.GetProperty("PerTurf").GetInt32() == d.PerTurf && pdefs.GetProperty("PerCommon").GetInt32() == d.PerCommon
              && pdefs.GetProperty("Capture").GetInt32() == d.Capture && pdefs.GetProperty("Kill").GetInt32() == d.Kill && pdefs.GetProperty("Max").GetInt32() == d.Max
              && pdefs.GetProperty("Interval").GetInt32() == d.Interval && pdefs.GetProperty("Start").GetInt32() == d.Start, "spec control point defaults match the code");
        CheckRanges(points.GetProperty("ranges"), ControlPointRules.Ranges, "control point");
        foreach (JsonElement ex in points.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            ControlPointSettings parsed = s.StartsWith(ControlPointRules.Prefix, StringComparison.Ordinal) ? PointsOf(s.Substring(ControlPointRules.Prefix.Length)) : new ControlPointSettings();
            Check(parsed.Listed && warErrors.Count == 0, $"spec control point example {s} parses cleanly");
        }
    }
    else Check(false, "spec has extensions.control_points");
    if (ext.TryGetProperty("command", out JsonElement command))
    {
        Equal(command.GetProperty("mutator").GetString(), CommandRules.Mutator, "spec commander mutator");
        Equal(command.GetProperty("prefix").GetString(), CommandRules.Prefix, "spec commander prefix");
        Check(specMutators.TryGetProperty(CommandRules.Mutator, out JsonElement cm) && cm.GetProperty("extension").GetBoolean(), "spec lists the commander mutator");
        var d = new CommandSettings();
        JsonElement cdefs = command.GetProperty("defaults");
        Check(cdefs.GetProperty("Cost").GetInt32() == d.Cost && cdefs.GetProperty("Size").GetInt32() == d.Size && cdefs.GetProperty("Cap").GetInt32() == d.Cap
              && cdefs.GetProperty("Refund").GetInt32() == d.Refund, "spec commander defaults match the code");
        CheckRanges(command.GetProperty("ranges"), CommandRules.Ranges, "commander");
        JsonElement climits = command.GetProperty("limits");
        Check(climits.GetProperty("max_units").GetInt32() == CommandRules.MaxUnits && climits.GetProperty("max_unit_name").GetInt32() == CommandRules.MaxUnitName, "spec commander limits match the code");
        Check(command.GetProperty("orders").EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "Recruit", "Capture", "Defend", "Recall", "Move", "Disband", "Declare war" }), "spec commander orders");
        Check(command.GetProperty("config").TryGetProperty("Map.CommandConsoleKey", out _), "spec commander key");
        foreach (JsonElement ex in command.GetProperty("examples").EnumerateArray())
        {
            string s = ex.GetString() ?? "";
            CommandSettings parsed = s.StartsWith(CommandRules.Prefix, StringComparison.Ordinal) ? CommandOf(s.Substring(CommandRules.Prefix.Length)) : new CommandSettings();
            Check(parsed.Listed && warErrors.Count == 0, $"spec commander example {s} parses cleanly");
        }
    }
    else Check(false, "spec has extensions.command");
    if (ext.TryGetProperty("war_panel", out JsonElement panel))
    {
        string panelMutator = panel.GetProperty("mutator").GetString() ?? "";
        Equal(panelMutator, "RCK_War_Panel", "spec war panel mutator");
        Check(specMutators.TryGetProperty(panelMutator, out JsonElement pm) && pm.GetProperty("extension").GetBoolean(), "spec lists the war panel mutator");
        Check(panel.GetProperty("config").TryGetProperty("Map.WarPanelKey", out _), "spec war panel key");
    }
    else Check(false, "spec has extensions.war_panel");
    if (ext.TryGetProperty("medic", out JsonElement medic))
    {
        string medicTrait = medic.GetProperty("trait").GetString() ?? "";
        Equal(medicTrait, "RCK_Faction_Medic", "spec medic trait");
        Check(traits.TryGetProperty(medicTrait, out JsonElement mt) && mt.TryGetProperty("medic", out JsonElement mflag) && mflag.GetBoolean()
              && mt.GetProperty("extension").GetBoolean() && mt.GetProperty("unlock").GetProperty("Cancellations").GetArrayLength() == 0, $"spec has the medic trait {medicTrait}");
        JsonElement mlimits = medic.GetProperty("limits");
        Check(mlimits.GetProperty("range_tiles").GetInt32() == 5 && mlimits.GetProperty("cooldown_seconds").GetInt32() == 5 && mlimits.GetProperty("min_heal").GetInt32() == 8, "spec medic limits");
    }
    else Check(false, "spec has extensions.medic");
    if (ext.TryGetProperty("racketeer", out JsonElement racketeer))
    {
        string racketTrait = racketeer.GetProperty("trait").GetString() ?? "";
        Equal(racketTrait, "RCK_Faction_Racketeer", "spec racketeer trait");
        Check(traits.TryGetProperty(racketTrait, out JsonElement rt) && rt.TryGetProperty("racketeer", out JsonElement rflag) && rflag.GetBoolean()
              && rt.GetProperty("extension").GetBoolean() && rt.GetProperty("unlock").GetProperty("Cancellations").GetArrayLength() == 0, $"spec has the racketeer trait {racketTrait}");
        Check(racketeer.GetProperty("host_only").GetBoolean(), "spec racketeer is host only");
    }
    else Check(false, "spec has extensions.racketeer");
}
else Check(false, "found " + json);

void CheckRanges(JsonElement spec, Dictionary<string, (string Name, int Min, int Max)> code, string what)
{
    foreach ((string Name, int Min, int Max) r in code.Values)
    {
        int[] span = spec.TryGetProperty(r.Name, out JsonElement e) ? e.EnumerateArray().Select(x => x.GetInt32()).ToArray() : Array.Empty<int>();
        Check(span.SequenceEqual(new[] { r.Min, r.Max }), $"spec {what} range {r.Name} matches the code");
    }
    Equal(spec.EnumerateObject().Count(), code.Count, $"spec lists every {what} range");
}

Console.WriteLine(failures == 0 ? $"FactionTests: {checks} checks passed" : $"FactionTests: {failures} of {checks} checks FAILED");
return failures == 0 ? 0 : 1;

static string FindRoot()
{
    for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "docs", "ccu-interface.json"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}
