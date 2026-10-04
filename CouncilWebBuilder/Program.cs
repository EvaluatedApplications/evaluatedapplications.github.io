using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CouncilAudit;

// Prepares Showroom/wwwroot/data/council-web from the virtual-customer's web_export. See the csproj comment.
string webExport = Environment.GetEnvironmentVariable("COUNCIL_WEB_EXPORT") ?? @"C:\Users\dongy\VirtualCustomer\web_export";
string export = Environment.GetEnvironmentVariable("COUNCIL_EXPORT_DIR") ?? @"C:\Users\dongy\VirtualCustomer\export";
string outDir = args.Length > 0 ? args[0] : @"C:\Users\dongy\AboutUs\Showroom\wwwroot\data\council-web";

if (args.Length > 1 && args[1] == "names") { foreach (var c in SupportedCouncils.Everyone) Console.WriteLine(c.Name + " | " + c.Years.Count + " | " + c.KnownQuirks.Count); return; }
if (args.Length > 1 && args[1] == "props")
{
    foreach (var p in typeof(SupportedCouncil).GetProperties()) Console.WriteLine($"{p.Name} : {p.PropertyType}");
    return;
}

Directory.CreateDirectory(outDir);
long rawTotal = 0, gzTotal = 0; int files = 0;

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

// index.csv and each months.csv stay plain text (tiny, read first). Exception slices are gzipped.
WriteRaw("index.csv", File.ReadAllBytes(Path.Combine(webExport, "index.csv")));
foreach (var slug in SupportedCouncils.Everyone.Select(c => Slug(c.Name)))
{
    var dir = Path.Combine(webExport, slug);
    WriteRaw($"{slug}/months.csv", File.ReadAllBytes(Path.Combine(dir, "months.csv")));
    foreach (var f in Directory.GetFiles(dir, "*.exceptions*.csv"))
        WriteGz($"{slug}/{Path.GetFileName(f)}", File.ReadAllBytes(f));
}

// The small cross-council check files and the declared-spend files (all read whole, each well under 400 KB raw).
foreach (var n in new[] { "transaction_twins", "cross_file_repeats", "file_duplication", "within_txn_repeats", "budget_reconciliation",
                          "budget_units", "budget_test", "budget_test_stage2", "budget_test_stage2b", "budget_test_pooled", "budget_test_pooled_all",
                          "crossref_alias_flows", "supplier_alias_grades", "debt_ledger", "debt_sink", "payment_misfits" })
    WriteGz($"cross/{n}.csv", File.ReadAllBytes(Path.Combine(export, n + ".csv")));

// Profile entries that are the virtual-customer's own working notes (session numbers, hand-over tips, checklist references) are held
// back from the public page and listed here so the owner of the profiles can reword them.
var Internal = new System.Text.RegularExpressions.Regex(@"owner's tip|Session \d+[:,]|Session \d+ CORRECTION|FIRST PASS|plain bot fetch", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
foreach (var c in SupportedCouncils.Everyone) foreach (var q in c.KnownQuirks.Where(q => Internal.IsMatch(q))) Console.WriteLine($"HELD BACK [{c.Name}]: {q[..Math.Min(150, q.Length)]}");

// Profiles: the councils' own text, as the virtual-customer wrote it.
var profiles = SupportedCouncils.Everyone.Select(c => new Dictionary<string, object?>
{
    ["slug"] = Slug(c.Name),
    ["name"] = c.Name,
    ["page"] = c.TransparencyPageUrl,
    ["howTo"] = c.HowToFindTheFile,
    ["quirks"] = c.KnownQuirks.Where(q => !Internal.IsMatch(q)).ToArray(),
    ["heldBack"] = c.KnownQuirks.Count(q => Internal.IsMatch(q)),
    ["lastChecked"] = c.LastChecked,
    ["verification"] = c.VerificationNote,
    ["foi"] = c.FoiContactEmail,
    ["idScope"] = c.IdScope.ToString(),
    ["years"] = c.Years.Select(y => new { tag = y.Tag, format = y.Format.ToString() }).ToArray(),
}).ToList();
var json = JsonSerializer.SerializeToUtf8Bytes(profiles, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
WriteGz("profiles.json", json);
WriteRaw("PREREG_BUDGET_TEST.txt", File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(webExport)!, "PREREG_BUDGET_TEST.md")));
WriteRaw("cross/sources.csv", File.ReadAllBytes(@"C:\Users\dongy\VirtualCustomer\inbox\published\sources.csv"));

Console.WriteLine($"{files} files, {rawTotal / 1048576.0:F1} MB raw, {gzTotal / 1048576.0:F1} MB as shipped -> {outDir}");

static string Slug(string name)
{
    var n = name.ToLowerInvariant();
    foreach (var (key, slug) in new[] { ("windsor", "rbwm"), ("bracknell", "bracknellforest"), ("west berkshire", "westberkshire"), ("wokingham", "wokingham"),
        ("merton", "merton"), ("reading", "reading"), ("birmingham", "birmingham"), ("leeds", "leeds"), ("sheffield", "sheffield"),
        ("bradford", "bradford"), ("liverpool", "liverpool"), ("bristol", "bristol") })
        if (n.Contains(key)) return slug;
    throw new InvalidOperationException("no slug for " + name);
}