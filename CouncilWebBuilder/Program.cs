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
// The councils the page ships: those the engine knows AND that have a phone export folder. A council the scanner has taken on but not yet exported
// is left out, and said so, until its web_export exists; it is never given a slug or a half-built page.
var published = SupportedCouncils.Everyone.Where(c => TrySlug(c.Name) is string s && Directory.Exists(Path.Combine(webExport, s))).ToList();
foreach (var c in SupportedCouncils.Everyone.Where(c => !published.Contains(c))) Console.WriteLine($"NOT SHIPPED (no web_export folder or no slug): {c.Name}");

long rawTotal = 0, gzTotal = 0; int files = 0, txSlices = 0;

long WriteGz(string name, byte[] data)
{
    var path = Path.Combine(outDir, name + ".gz");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using (var fs = File.Create(path))
    using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize)) gz.Write(data);
    long size = new FileInfo(path).Length;
    rawTotal += data.Length; gzTotal += size; files++;
    return size;
}

// ---------------------------------------------------------------------------------------------------------------------------------------
// Exception files. The page loads them by FINANCIAL YEAR (April to March, the year the councils' declared totals are given in), one fetch
// per year, or by single months when a visitor ticks months. Both are the same SLIM format, a "section" per month:
//     @2022-11                                  (marker line: the month of the file the rows came from)
//     Schedule,GroupId,TransactionId,SupplierName,Net,Difference,Detail,Classification,TransactionGross,ExplainedBy,ExplainedMeaning,Gross,Care
// Two columns were added on 2026-10-04 (the page finds columns by name, so an older slice still reads). "Gross" below is no longer dropped everywhere:
//   Gross  the line's own stated Gross, kept only where it differs from Net on a Schedule A row (the stated invoice amount the payments are
//          compared with) and on a Schedule B row read as NSquaredRows (the run's real value). Empty everywhere else.
//   Care   "1" on the first line of a Schedule B group of Wokingham whose every line sits in an adult or children's care cost centre (the
//          cost centre is not in the exception file, so it is looked up in the month's transaction slice by transaction number).
//     rows...
// Slim = the page's own reading of the engine's export with what it never shows taken out: Council, Year and SupplierKey are dropped, and Gross except as noted above;
// TransactionGross is left empty when it equals Net; Detail and ExplainedMeaning (the same sentence on every line of a group) stay on the first
// line of each group only, which is the only place the page reads them. Rows, groups, values and the text shown are unchanged (checked against
// the unslimmed files: see CLAUDE.md). A year bundle is cut into parts of whole months only if a part would pass PartCapRaw.
// ---------------------------------------------------------------------------------------------------------------------------------------
int PartCapRaw = (int.TryParse(Environment.GetEnvironmentVariable("COUNCIL_PART_CAP_MB"), out var capMb) ? capMb : 12) * 1024 * 1024;   // the env var is for testing a split
const int MonthCapRaw = 16 * 1024 * 1024;
const string SlimHeader = "Schedule,GroupId,TransactionId,SupplierName,Net,Difference,Detail,Classification,TransactionGross,ExplainedBy,ExplainedMeaning,Gross,Care";

// Wokingham's care cost centres (SPEC_FOR_SHOWROOM item 29): Nursing, Residential, Domiciliary, Supported Living, Day Care, Respite, Children's Homes
// Purchasing, Semi Independent, Independent Fostering Agency. A cost centre is care when its name holds one of these words.
string[] CareWords = { "nursing", "residential", "domiciliary", "supported living", "day care", "respite", "homes purchasing", "semi independent", "independent fostering agency" };
bool IsCareCentre(string cc) { var l = cc.ToLowerInvariant(); foreach (var w in CareWords) if (l.Contains(w)) return true; return false; }
long careGroups = 0, careGroupsChecked = 0;

// transaction number -> cost centre for one month of one council, from the month's transaction slice (all parts); empty when the slice has no such column
Dictionary<string, string> CostCentres(string dir, string month)
{
    var map = new Dictionary<string, string>();
    foreach (var f in Directory.GetFiles(dir, month + "*.csv"))
    {
        var n = Path.GetFileName(f);
        if (n.Contains(".exceptions") || n == "months.csv") continue;
        var rest = n[month.Length..];
        if (rest != ".csv" && !System.Text.RegularExpressions.Regex.IsMatch(rest, @"^\.\d+\.csv$")) continue;
        var recs = ParseCsv(File.ReadAllText(f, Encoding.UTF8));
        if (recs.Count == 0) continue;
        int cT = recs[0].FindIndex(x => x.Equals("TransactionId", StringComparison.OrdinalIgnoreCase)), cC = recs[0].FindIndex(x => x.Equals("CostCentreArea", StringComparison.OrdinalIgnoreCase));
        if (cT < 0 || cC < 0) continue;
        for (int k = 1; k < recs.Count; k++) if (recs[k].Count > Math.Max(cT, cC)) map[recs[k][cT]] = recs[k][cC];
    }
    return map;
}

string FinancialYear(string month)
{
    if (month.Length != 7 || !int.TryParse(month[..4], out var y) || !int.TryParse(month[5..], out var m)) return "undated";
    int s = m >= 4 ? y : y - 1;
    return $"{s}-{(s + 1) % 100:00}";
}

List<List<string>> ParseCsv(string t)
{
    var rows = new List<List<string>>(); var row = new List<string>(); var sb = new StringBuilder();
    int i = 0, n = t.Length; bool any = false;
    if (n > 0 && t[0] == '﻿') i = 1;
    while (i < n)
    {
        char c = t[i];
        if (c == '"')
        {
            i++; any = true;
            while (i < n) { if (t[i] == '"') { if (i + 1 < n && t[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; } sb.Append(t[i++]); }
        }
        else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); i++; any = true; }
        else if (c == '\r') i++;
        else if (c == '\n') { if (any || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); } row = new(); sb.Clear(); any = false; i++; }
        else { sb.Append(c); i++; any = true; }
    }
    if (any || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
    return rows;
}

string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

// The Schedule B groups of one month of Wokingham whose every line (each found in the transaction slice) sits in a care cost centre.
HashSet<long> CareGroupsOf(string dir, string month, List<string> files)
{
    var cc = CostCentres(dir, month);
    var found = new Dictionary<long, int>(); var care = new Dictionary<long, int>();
    foreach (var f in files)
    {
        var recs = ParseCsv(File.ReadAllText(f, Encoding.UTF8));
        if (recs.Count == 0) continue;
        var h = recs[0]; int Col(string n) => h.FindIndex(x => x.Equals(n, StringComparison.OrdinalIgnoreCase));
        int cS = Col("Schedule"), cG = Col("GroupId"), cT = Col("TransactionId");
        for (int k = 1; k < recs.Count; k++)
        {
            var r = recs[k];
            if (r.Count <= Math.Max(cS, Math.Max(cG, cT)) || r[cS] != "B" || !long.TryParse(r[cG], out var gid)) continue;
            if (!cc.TryGetValue(r[cT], out var centre)) continue;
            found[gid] = found.GetValueOrDefault(gid) + 1;
            if (IsCareCentre(centre)) care[gid] = care.GetValueOrDefault(gid) + 1;
        }
    }
    var o = new HashSet<long>();
    foreach (var (gid, n) in found) { careGroupsChecked++; if (care.GetValueOrDefault(gid) == n) { o.Add(gid); careGroups++; } }
    return o;
}

// One month's section in slim form, from the month's exception file(s) (a month the engine split in parts is read as one: group ids carry across parts).
(byte[] Data, int Rows) SlimMonth(string slug, string dir, string month)
{
    var files = Directory.GetFiles(dir, month + ".exceptions*.csv")
        .OrderBy(f => { var p = Path.GetFileName(f)[(month.Length + ".exceptions".Length)..]; return p == ".csv" ? 1 : int.Parse(p.Split('.')[1]); }).ToList();
    if (files.Count == 0) return (Array.Empty<byte>(), 0);
    var sb = new StringBuilder();
    sb.Append('@').Append(month).Append('\n').Append(SlimHeader).Append('\n');
    var seen = new HashSet<(char, long)>();
    var careSet = slug == "wokingham" ? CareGroupsOf(dir, month, files) : null;
    int rowsOut = 0;
    foreach (var f in files)
    {
        var recs = ParseCsv(File.ReadAllText(f, Encoding.UTF8));
        if (recs.Count == 0) continue;
        var h = recs[0]; int Col(string n) => h.FindIndex(x => x.Equals(n, StringComparison.OrdinalIgnoreCase));
        int cS = Col("Schedule"), cG = Col("GroupId"), cT = Col("TransactionId"), cN = Col("SupplierName"), cNet = Col("Net"), cD = Col("Difference"),
            cDet = Col("Detail"), cC = Col("Classification"), cTG = Col("TransactionGross"), cEB = Col("ExplainedBy"), cEM = Col("ExplainedMeaning"), cGr = Col("Gross");
        if (cS < 0 || cG < 0 || cT < 0 || cN < 0 || cNet < 0 || cD < 0 || cDet < 0 || cC < 0) throw new InvalidDataException($"{f}: columns missing");
        string At(List<string> r, int c) => c >= 0 && c < r.Count ? r[c] : "";
        for (int k = 1; k < recs.Count; k++)
        {
            var r = recs[k];
            if (r.Count < 12) continue;      // the page skips these too
            string sched = At(r, cS);
            bool hasGid = long.TryParse(At(r, cG), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var gid);
            bool first = !(hasGid && sched.Length > 0 && sched[0] != 'A') || seen.Add((sched[0], gid));
            string net = At(r, cNet), tg = At(r, cTG);
            // the stated Gross: kept on a Schedule A row when it differs from Net, and on an NSquaredRows group (the run's real value)
            string gr = At(r, cGr), keepGross = "";
            if (gr.Length > 0 && sched == "A" && !(decimal.TryParse(gr, System.Globalization.CultureInfo.InvariantCulture, out var ga) && decimal.TryParse(net, System.Globalization.CultureInfo.InvariantCulture, out var gn) && ga == gn)) keepGross = gr;
            else if (gr.Length > 0 && At(r, cEB) == "NSquaredRows") keepGross = gr;
            bool care = first && careSet is not null && sched == "B" && hasGid && careSet.Contains(gid);
            bool sameGross = tg.Length == 0 || (decimal.TryParse(tg, System.Globalization.CultureInfo.InvariantCulture, out var a) && decimal.TryParse(net, System.Globalization.CultureInfo.InvariantCulture, out var b) && a == b);
            sb.Append(Q(sched)).Append(',').Append(Q(At(r, cG))).Append(',').Append(Q(At(r, cT))).Append(',').Append(Q(At(r, cN))).Append(',').Append(Q(net)).Append(',')
              .Append(Q(At(r, cD))).Append(',').Append(first ? Q(At(r, cDet)) : "").Append(',').Append(Q(At(r, cC))).Append(',').Append(sameGross ? "" : Q(tg)).Append(',')
              .Append(Q(At(r, cEB))).Append(',').Append(first ? Q(At(r, cEM)) : "").Append(',').Append(Q(keepGross)).Append(',').Append(care ? "1" : "").Append('\n');
            rowsOut++;
        }
    }
    return rowsOut == 0 ? (Array.Empty<byte>(), 0) : (new UTF8Encoding(false).GetBytes(sb.ToString()), rowsOut);
}

// Writes one council's month files, year bundles, months.csv (its exception columns now describe the slim files) and years.csv.
void WriteExceptions(string slug, string dir)
{
    var lines = File.ReadAllLines(Path.Combine(dir, "months.csv")).Where(l => l.Length > 0).ToList();
    var outLines = new List<string> { lines[0] };
    var byYear = new SortedDictionary<string, List<(string Month, byte[] Data, int Rows)>>(StringComparer.Ordinal);
    var tot = new Dictionary<string, (int Months, long Tx, decimal Net)>();
    foreach (var line in lines.Skip(1))
    {
        var f = line.Split(',');           // plain numbers: no quoted fields in months.csv
        string month = f[0];
        var (data, rows) = SlimMonth(slug, dir, month);
        long gz = 0;
        if (rows > 0)
        {
            if (data.Length > MonthCapRaw) throw new InvalidOperationException($"{slug} {month}: slim month is {data.Length / 1048576.0:F1} MB raw, over the cap");
            gz = WriteGz($"{slug}/{month}.exceptions.csv", data);
        }
        if (int.TryParse(f[7], out var old) && old != rows) Console.WriteLine($"ROW COUNT [{slug} {month}]: months.csv says {old} flagged lines, the files hold {rows}");
        f[6] = rows > 0 ? "1" : "0"; f[7] = rows.ToString(); f[8] = data.Length.ToString(); f[9] = gz.ToString();
        outLines.Add(string.Join(",", f));
        string fy = FinancialYear(month);
        if (!byYear.TryGetValue(fy, out var list)) byYear[fy] = list = new();
        list.Add((month, data, rows));
        tot.TryGetValue(fy, out var t);
        tot[fy] = (t.Months + 1, t.Tx + long.Parse(f[2]), t.Net + decimal.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture));
    }
    WriteRaw($"{slug}/months.csv", new UTF8Encoding(false).GetBytes(string.Join("\n", outLines) + "\n"));

    var years = new List<string> { "Year,Months,Parts,TxRows,Net,ExceptionRows,ExceptionBytes,ExceptionGzipBytes" };
    foreach (var (fy, list) in byYear)
    {
        var parts = new List<MemoryStream> { new MemoryStream() };
        int rows = 0;
        foreach (var (_, data, r) in list)
        {
            if (r == 0) continue;
            if (parts[^1].Length > 0 && parts[^1].Length + data.Length > PartCapRaw) parts.Add(new MemoryStream());
            parts[^1].Write(data); rows += r;
        }
        long raw = 0, gz = 0; int np = 0;
        if (rows > 0)
            foreach (var (p, i) in parts.Select((p, i) => (p, i)))
            {
                gz += WriteGz($"{slug}/fy-{fy}.exceptions{(i == 0 ? "" : "." + (i + 1))}.csv", p.ToArray());
                raw += p.Length; np++;
            }
        var tt = tot[fy];
        years.Add($"{fy},{tt.Months},{np},{tt.Tx},{tt.Net.ToString(System.Globalization.CultureInfo.InvariantCulture)},{rows},{raw},{gz}");
    }
    WriteRaw($"{slug}/years.csv", new UTF8Encoding(false).GetBytes(string.Join("\n", years) + "\n"));
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
foreach (var slug in profilesOnly ? Array.Empty<string>() : published.Select(c => Slug(c.Name)))
{
    var dir = Path.Combine(webExport, slug);
    WriteExceptions(slug, dir);
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

// Wokingham's social care view (SPEC_FOR_SHOWROOM item 30; made by the scanner's "socialcare wokingham", Wokingham only). Four small files the page reads whole
// (providers 200 KB raw). companies_house_links.csv is a hand-kept input, and new_providers.csv repeats the "new large provider" column of providers.csv: neither ships.
if (!profilesOnly)
{
    foreach (var n in new[] { "concentration", "providers", "rates", "invoice_date_repeats" })
        WriteGz($"wokingham/socialcare/{n}.csv", File.ReadAllBytes(Path.Combine(export, "wokingham_socialcare", n + ".csv")));
    Console.WriteLine($"CARE COSTS: {careGroups} of {careGroupsChecked} Wokingham Schedule B groups (found in the transaction slices) sit wholly in a care cost centre");
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
var InternalSentence = new System.Text.RegularExpressions.Regex(@"owner's tip|Session \d+|FIRST PASS|plain bot fetch|\.cs\b|\.md\b|CityFixups|AuditEngine|ColumnMapping|MapRows|KnownQuirks|\bfoi/|scratch|inbox|Schedule R\b|hand-built|ONBOARDING|checklist|BASELINE|FEEDBACK|\bCLI\b|ParseDate|against the BASE|Fixups|this session|handopen|full-engine|MAD-r|XlsxReader|XlsReader|ExcelDataReader|FileDuplication|OtherForRepeat", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
string Reword(string s)
{
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\((?:see |per )?[^()]*(?:ONBOARDING_CHECKLIST|checklist step|BASELINE|FEEDBACK)[^()]*\)", "");
    string Cap(System.Text.RegularExpressions.Match m, string t) => m.Index == 0 || (m.Index > 1 && s[m.Index - 1] == ' ' && ".:;".Contains(s[m.Index - 2])) ? char.ToUpper(t[0]) + t[1..] : t;
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedules A and D", m => Cap(m, "the invoice-amount and shared-transaction-number checks"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule A/B/D", m => Cap(m, "the invoice-amount, repeated-payment and shared-transaction-number checks"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule A( \(amount mismatch\))?", m => Cap(m, "the invoice-amount check"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"Schedule B", m => Cap(m, "the repeated-payment check"));
    s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=check and )D(?= (?:are|is)\b)", "the shared-transaction-number check");   // "the invoice-amount check and D are empty"
    s = System.Text.RegularExpressions.Regex.Replace(s, @"`rawcheck`", m => Cap(m, "the raw-file check"));
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
var cleanQuirks = published.ToDictionary(c => c.Name, c => c.KnownQuirks.Select(q => Clean(c.Name, q)).Where(q => q is not null).Cast<string>().ToArray());
var cleanVerif = published.ToDictionary(c => c.Name, c => Clean(c.Name + " verification", c.VerificationNote) ?? "");

// Profiles: the councils' own text.
var profiles = published.Select(c => new Dictionary<string, object?>
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

static string Slug(string name) => TrySlug(name) ?? throw new InvalidOperationException("no slug for " + name);
static string? TrySlug(string name)
{
    var n = name.ToLowerInvariant();
    foreach (var (key, slug) in new[] { ("windsor", "rbwm"), ("bracknell", "bracknellforest"), ("west berkshire", "westberkshire"), ("wokingham", "wokingham"),
        ("merton", "merton"), ("reading", "reading"), ("birmingham", "birmingham"), ("leeds", "leeds"), ("sheffield", "sheffield"),
        ("bradford", "bradford"), ("liverpool", "liverpool"), ("bristol", "bristol"), ("wakefield", "wakefield"), ("coventry", "coventry"),
        ("durham", "durham"), ("kirklees", "kirklees"), ("leicester", "leicester"), ("cornwall", "cornwall"), ("nottingham", "nottingham"),
        ("wirral", "wirral"), ("newcastle", "newcastle"), ("surrey", "surrey"), ("essex", "essex"), ("hertfordshire", "hertfordshire"), ("stockport", "stockport"), ("york", "york") })
        if (n.Contains(key)) return slug;
    return null;
}