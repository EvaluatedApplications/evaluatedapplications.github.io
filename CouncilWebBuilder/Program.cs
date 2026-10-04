using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CouncilAudit;

// Prepares council-web (the Council Spending Scanner data) straight into the website-data repo (C:\Users\dongy\website-data\council-web,
// served at /website-data/council-web by that repo's own GitHub Pages), from the virtual-customer's web_export. See the csproj comment.
// Override the target with the first argument or COUNCIL_WEB_OUT. After a run: commit and push website-data; nothing here touches the site repo.
string webExport = Environment.GetEnvironmentVariable("COUNCIL_WEB_EXPORT") ?? @"C:\Users\dongy\VirtualCustomer\web_export";
string export = Environment.GetEnvironmentVariable("COUNCIL_EXPORT_DIR") ?? @"C:\Users\dongy\VirtualCustomer\export";
string outDir = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("COUNCIL_WEB_OUT") ?? @"C:\Users\dongy\website-data\council-web";

if (args.Length > 1 && args[1] == "names") { foreach (var c in SupportedCouncils.Everyone) Console.WriteLine(c.Name + " | " + c.Years.Count + " | " + c.KnownQuirks.Count); return 0; }
if (args.Length > 1 && args[1] == "props")
{
    foreach (var p in typeof(SupportedCouncil).GetProperties()) Console.WriteLine($"{p.Name} : {p.PropertyType}");
    return 0;
}

Directory.CreateDirectory(outDir);
long rawTotal = 0, gzTotal = 0; int files = 0, txSlices = 0;

void WriteGz(string name, byte[] data)
{
    var path = Path.Combine(outDir, name + ".gz");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var fs = File.Create(path);
    using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize)) gz.Write(data);
    rawTotal += data.Length; gzTotal += new FileInfo(path).Length; files++;
}
void WriteRaw(string name, byte[] data)
{
    var path = Path.Combine(outDir, name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, data);
    rawTotal += data.Length; gzTotal += data.Length; files++;
}

// "profiles" as the second argument rewrites only profiles.json.gz (and prints what was reworded or held back).
bool profilesOnly = args.Length > 1 && args[1] == "profiles";

// index.csv and each months.csv stay plain text (tiny, read first). Exception slices are gzipped.
if (!profilesOnly) WriteRaw("index.csv", File.ReadAllBytes(Path.Combine(webExport, "index.csv")));
foreach (var slug in profilesOnly ? Array.Empty<string>() : SupportedCouncils.Everyone.Select(c => Slug(c.Name)))
{
    var dir = Path.Combine(webExport, slug);
    WriteRaw($"{slug}/months.csv", File.ReadAllBytes(Path.Combine(dir, "months.csv")));
    foreach (var f in Directory.GetFiles(dir, "*.exceptions*.csv"))
        WriteGz($"{slug}/{Path.GetFileName(f)}", File.ReadAllBytes(f));
    // Transaction slices ("See the source rows"): one month per file, further parts when a month holds more than 12,000 rows. Fetched only
    // when a visitor asks for the source rows of one flagged item, never in bulk.
    foreach (var f in Directory.GetFiles(dir, "*.csv"))
    {
        var n = Path.GetFileName(f);
        if (n == "months.csv" || n.Contains(".exceptions")) continue;
        WriteGz($"{slug}/{n}", File.ReadAllBytes(f));
        txSlices++;
    }
}

// The small cross-council check files and the declared-spend files (all read whole, each well under 400 KB raw).
foreach (var n in profilesOnly ? Array.Empty<string>() : new[] { "transaction_twins", "cross_file_repeats", "file_duplication", "within_txn_repeats", "budget_reconciliation",
                          "budget_units", "budget_test", "budget_test_stage2", "budget_test_stage2b", "budget_test_stage2c", "budget_test_pooled", "budget_test_pooled_all", "budget_test_pooled_all2",
                          "crossref_alias_flows", "supplier_alias_grades", "debt_ledger", "debt_sink", "payment_misfits" })
    WriteGz($"cross/{n}.csv", File.ReadAllBytes(Path.Combine(export, n + ".csv")));

// Profile text is the virtual-customer's own working notes. Before it goes on the public page: (1) the engine's internal names for the
// three rules become the page's own words, (2) sentences that point at the working files themselves (session numbers, checklist steps,
// source-file names, hand-over tips) are dropped, and a note with nothing left is held back. Every change is printed so the profile's
// owner can reword the source; the profile text itself is never edited here.
var Sentence = new System.Text.RegularExpressions.Regex(@"(?<=[.;:])\s+(?=[A-Z""(])");
var InternalSentence = new System.Text.RegularExpressions.Regex(@"owner's tip|Session \d+|FIRST PASS|plain bot fetch|\.cs\b|\.md\b|CityFixups|AuditEngine|ColumnMapping|MapRows|KnownQuirks|\bfoi/|scratch|inbox|Schedule R\b|hand-built|ONBOARDING|checklist|BASELINE|FEEDBACK|\bCLI\b|ParseDate|against the BASE|Fixups|this session|handopen|full-engine|MAD-r|XlsxReader", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
string Reword(string s)
{
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\((?:see |per )?[^()]*(?:ONBOARDING_CHECKLIST|checklist step|BASELINE|FEEDBACK)[^()]*\)", "");
    string Cap(System.Text.RegularExpressions.Match m, string t) => m.Index == 0 || (m.Index > 1 && s[m.Index - 1] == ' ' && ".:;".Contains(s[m.Index - 2])) ? char.ToUpper(t[0]) + t[1..] : t;
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedules A and D", m => Cap(m, "the invoice-amount and shared-transaction-number checks"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule A/B/D", m => Cap(m, "the invoice-amount, repeated-payment and shared-transaction-number checks"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule A( \(amount mismatch\))?", m => Cap(m, "the invoice-amount check"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule B", m => Cap(m, "the repeated-payment check"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule D", m => Cap(m, "the shared-transaction-number check"));
    // the preparation command and its step: "`prep kirklees` writes it dd/MM/yyyy" becomes "the scanner writes it dd/MM/yyyy"
    s = System.Text.RegularExpressions.Regex.Replace(s, @"`prep(?: [a-z]+)?`|\b(?:[Tt]he )?Prepare step\b", m => Cap(m, "the scanner"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(The|the|an|a) engine\b", m => m.Value[0] == 'T' ? "The scanner" : m.Value.StartsWith("an") ? "a scanner" : "the scanner");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\bengine\b", "scanner");
    return s;
}
string? Clean(string slugName, string text)
{
    // a parenthetical that only points at the working files or the engine's own names goes; the sentence around it stays
    var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\([^()]*\)", m => InternalSentence.IsMatch(m.Value) || m.Value.Contains("the engine") ? "" : m.Value);
    var kept = Sentence.Split(stripped).Where(x => !InternalSentence.IsMatch(x)).ToArray();
    if (kept.Length == 0) { Console.WriteLine($"HELD BACK [{slugName}]: {text[..Math.Min(130, text.Length)]}"); return null; }
    var o = Reword(string.Join(" ", kept));
    o = System.Text.RegularExpressions.Regex.Replace(o, @"[;:,]\s*$", ".");
    if (o != text) Console.WriteLine($"REWORDED [{slugName}] ({kept.Length}/{Sentence.Split(text).Length} sentences kept): {o[..Math.Min(170, o.Length)]}");
    return o;
}
var cleanQuirks = SupportedCouncils.Everyone.ToDictionary(c => c.Name, c => c.KnownQuirks.Select(q => Clean(c.Name, q)).Where(q => q is not null).Cast<string>().ToArray());
var cleanVerif = SupportedCouncils.Everyone.ToDictionary(c => c.Name, c => Clean(c.Name + " verification", c.VerificationNote) ?? "");

// Profiles: the councils' own text.
var profiles = SupportedCouncils.Everyone.Select(c => new Dictionary<string, object?>
{
    ["slug"] = Slug(c.Name),
    ["name"] = c.Name,
    ["page"] = c.TransparencyPageUrl,
    ["howTo"] = c.HowToFindTheFile,
    ["quirks"] = cleanQuirks[c.Name],
    ["heldBack"] = c.KnownQuirks.Count - cleanQuirks[c.Name].Length,
    ["lastChecked"] = c.LastChecked,
    ["verification"] = cleanVerif[c.Name],
    ["foi"] = c.FoiContactEmail,
    ["idScope"] = c.IdScope.ToString(),
    ["years"] = c.Years.Select(y => new { tag = y.Tag, format = y.Format.ToString() }).ToArray(),
}).ToList();
var json = JsonSerializer.SerializeToUtf8Bytes(profiles, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
WriteGz("profiles.json", json);
WriteRaw("PREREG_BUDGET_TEST.txt", File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(webExport)!, "PREREG_BUDGET_TEST.md")));
WriteRaw("cross/sources.csv", File.ReadAllBytes(@"C:\Users\dongy\VirtualCustomer\inbox\published\sources.csv"));

// GitHub refuses any file over 100 MB and warns over 50 MB; the owner's bar is 50 MB. Report the largest and fail loudly above it.
var biggest = new DirectoryInfo(outDir).EnumerateFiles("*", SearchOption.AllDirectories).OrderByDescending(f => f.Length).First();
Console.WriteLine($"{files} files ({txSlices} transaction slices), {rawTotal / 1048576.0:F1} MB raw, {gzTotal / 1048576.0:F1} MB as shipped -> {outDir}");
Console.WriteLine($"largest file shipped: {biggest.FullName} {biggest.Length / 1048576.0:F2} MB");
if (biggest.Length > 50L * 1048576) { Console.WriteLine("A FILE IS OVER 50 MB"); return 1; }
return 0;

static string Slug(string name)
{
    var n = name.ToLowerInvariant();
    foreach (var (key, slug) in new[] { ("windsor", "rbwm"), ("bracknell", "bracknellforest"), ("west berkshire", "westberkshire"), ("wokingham", "wokingham"),
        ("merton", "merton"), ("reading", "reading"), ("birmingham", "birmingham"), ("leeds", "leeds"), ("sheffield", "sheffield"),
        ("bradford", "bradford"), ("liverpool", "liverpool"), ("bristol", "bristol"), ("wakefield", "wakefield"), ("coventry", "coventry"),
        ("durham", "durham"), ("kirklees", "kirklees"), ("leicester", "leicester"), ("cornwall", "cornwall"), ("nottingham", "nottingham"),
        ("wirral", "wirral"), ("newcastle", "newcastle") })
        if (n.Contains(key)) return slug;
    throw new InvalidOperationException("no slug for " + name);
}