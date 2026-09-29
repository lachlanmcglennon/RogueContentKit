// Offline tests for RCK's pure faction rules: grade precedence, parsing and the FactionRel matrix, plus a check that
// docs\ccu-interface.json (the modder-facing spec) lists the same grades and precedence the code uses.
// Usage: dotnet run --project tools\FactionTests [-- <repo root>]. Exits 1 on any failure.
using System.Text.Json;
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
    Check(grades >= 6, "spec lists at least 6 grades");

    JsonElement recruit = factions.GetProperty("recruit");
    Equal(recruit.GetProperty("matrix").GetProperty("prefix").GetString(), "[RCK]FactionRecruit::", "spec recruit prefix");
    foreach (JsonElement p in recruit.GetProperty("policies").EnumerateArray())
        Check(RecruitMatrix.TryParsePolicy(p.GetString(), out _), $"spec recruit policy {p.GetString()} parses");
    foreach (string section in new[] { "traits", "mutators" })
        foreach (JsonProperty entry in recruit.GetProperty(section).EnumerateObject())
            Check(RecruitMatrix.TryParsePolicy(entry.Value.GetString(), out _), $"spec recruit {section} {entry.Name} has a known policy");
}
else Check(false, "found " + json);

Console.WriteLine(failures == 0 ? $"FactionTests: {checks} checks passed" : $"FactionTests: {failures} of {checks} checks FAILED");
return failures == 0 ? 0 : 1;

static string FindRoot()
{
    for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "docs", "ccu-interface.json"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}
