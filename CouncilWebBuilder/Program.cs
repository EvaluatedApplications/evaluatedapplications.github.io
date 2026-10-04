using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CouncilAudit;
using Showroom.Services;   // CouncilCodes: the plain-words table the page uses (compiled in from Showroom/Services/CouncilCodes.cs)

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
// The councils the page ships are DISCOVERED, never listed here: a council is shipped when web_export/index.csv has a row for it (the row's first column is
// its slug, the name of its web_export folder) AND the engine has a profile whose name matches that slug. Nothing to add per council: no slug table, no page edit.
// A slug with no profile, or a profile with no row, is printed and left out until both exist; it is never given a half-built page.
// A profile matches a slug when the slug is the name's words run together ("Bracknell Forest Council" holds "bracknellforest", "City of York Council" holds "york")
// or the initials of its capitalised words ("Royal Borough of Windsor and Maidenhead" is "rbwm"). Two matches for one slug stop the run.
var indexLines = File.ReadAllLines(Path.Combine(webExport, "index.csv"), Encoding.UTF8).Where(l => l.Length > 0).ToList();
var indexHeader = indexLines[0];
var indexRows = indexLines.Skip(1).Select(l => l.Split(',')).Where(r => Directory.Exists(Path.Combine(webExport, r[0]))).ToList();
var slugOf = new Dictionary<string, string>();                      // profile name -> slug
var rowOf = new Dictionary<string, string[]>();                     // slug -> its index.csv row
// A council the scanner is still working on has a row and a stub profile ("Profile text pending the first run"): it is HELD until the profile is written, so a half-onboarded
// council is never published by a rebuild. COUNCIL_HOLD (comma-separated slugs) holds one by hand.
var held = (Environment.GetEnvironmentVariable("COUNCIL_HOLD") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
// The scanner's own error-category checklist holds a council that fails a category it cannot fix: export/checklist_holds.csv (Council,Category,Reason,Effect). Effect "withhold" (also the
// default when the column is empty) keeps the council off the page exactly like COUNCIL_HOLD; any other effect ("note") only prints. This is the file `CouncilAudit.Cli checklist holdenv`
// reads, so the two agree by construction (the CLI prints the same slugs, comma separated, for COUNCIL_HOLD).
{
    string holdsPath = Path.Combine(export, "checklist_holds.csv");
    if (File.Exists(holdsPath))
        foreach (var h in ParseCsv(File.ReadAllText(holdsPath, Encoding.UTF8)).Skip(1))
        {
            if (h.Count < 3 || h[0].Length == 0) continue;
            string effect = h.Count > 3 && h[3].Length > 0 ? h[3] : "withhold";
            Console.WriteLine($"CHECKLIST HOLD [{h[0]} {h[1]}] effect {effect}: {h[2]}");
            if (effect == "withhold") held.Add(h[0]);
        }
}
var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // slug and full name of every council with an index row that is not shipped: its rows are also dropped from the cross files
foreach (var row in indexRows)
{
    var hits = SupportedCouncils.Everyone.Where(c => NameMatchesSlug(c.Name, row[0])).ToList();
    if (hits.Count == 0) { Console.WriteLine($"NOT SHIPPED (no profile in the engine matches the slug): {row[0]}"); heldKeys.Add(row[0]); continue; }
    if (hits.Count > 1) throw new InvalidOperationException($"slug '{row[0]}' matches {hits.Count} profiles: {string.Join("; ", hits.Select(h => h.Name))}");
    if (held.Contains(row[0]) || hits[0].VerificationNote.StartsWith("Profile text pending", StringComparison.OrdinalIgnoreCase))
    { Console.WriteLine($"HELD (profile text not written yet, COUNCIL_HOLD, or export/checklist_holds.csv): {row[0]}"); heldKeys.Add(row[0]); heldKeys.Add(hits[0].Name); continue; }
    if (slugOf.ContainsKey(hits[0].Name)) throw new InvalidOperationException($"profile '{hits[0].Name}' matches two slugs");
    slugOf[hits[0].Name] = row[0]; rowOf[row[0]] = row;
}
// The page lists councils in the order the engine took them on, so a new council lands at the end with no ordering to keep.
var published = SupportedCouncils.Everyone.Where(c => slugOf.ContainsKey(c.Name)).ToList();
foreach (var c in SupportedCouncils.Everyone.Where(c => !slugOf.ContainsKey(c.Name))) Console.WriteLine($"NOT SHIPPED (no web_export row or folder): {c.Name}");

long rawTotal = 0, gzTotal = 0; int files = 0, txSlices = 0;

// ---------------------------------------------------------------------------------------------------------------------------------------
// FACTS, measured from the export itself (written into each council's profile as "facts"; the page turns them into the "What cannot be checked" list,
// the per-year "not available" lines and the check notices, so none of that is typed per council). One pass over every transaction slice and every
// exception file of a full build; a "profiles" run reuses the facts the last full build wrote into profiles.json.gz.
//   noA          no stated invoice amount to compare with: no Schedule A row anywhere, and no slice row whose Gross differs from Net or carries a VAT amount
//   noD          no Schedule D row anywhere (the shared-transaction-number check found nothing)
//   noNumber     no month publishes a transaction number (every row carries the scanner's "(no number published) N" placeholder)
//   numbered*    numbers published from one month on and none before it (placeholders in every earlier month): first and last numbered month and their count
//   counterIds   the number is a per-file counter: in 80% or more of the months the smallest trailing number among the ids is 1 and the trailing numbers are nearly all different
//                (it restarts every month: Leeds "2406-00001", Birmingham/West Berkshire/Sheffield synthetic row numbers; Durham's "4219343-RES-12-2023-324" repeats its line numbers, so it is not one). Such a number is not a reference one transaction can share, which is how the engine's own notes call it blind
//   dByNumbering noD with real numbers published (not none, not partial, not counters): every number sits on one payee and one date, a result of how the numbers are given
//   dYears       financial years with at least one Schedule D row
//   noBudget     every declared-spend unit of the council has no published Revenue Outturn (export/budget_units.csv), or it has none: the comparison cannot run
//   redacted/pooled  rows whose payee is redacted (the engine's own test) / is a pooled label ("Redacted (pooled label): ...")
// ---------------------------------------------------------------------------------------------------------------------------------------
// Every machine code the data carries into a page line (classification, pattern reading, debt-sink flag, label check, loan-rebuild status), collected as the files are written and
// checked at the end against Showroom's CouncilCodes table: a code with no plain-words entry FAILS the run (exit 1), so a new engine code can never reach the page unmapped.
var seenCodes = new HashSet<(string Vocab, string Value)>();
var acc = new Dictionary<string, FactsAcc>();
FactsAcc Acc(string slug) { if (!acc.TryGetValue(slug, out var a)) acc[slug] = a = new FactsAcc(); return a; }
var budgetNote = new Dictionary<string, (int Units, int NoOutturn)>();
{
    var bu = Path.Combine(export, "budget_units.csv");
    if (File.Exists(bu))
    {
        var recs = ParseCsv(File.ReadAllText(bu, Encoding.UTF8));
        int cC = recs[0].IndexOf("Council"), cN = recs[0].IndexOf("Note");
        foreach (var r in recs.Skip(1))
        {
            if (r.Count <= Math.Max(cC, cN)) continue;
            budgetNote.TryGetValue(r[cC], out var t);
            budgetNote[r[cC]] = (t.Units + 1, t.NoOutturn + (r[cN].StartsWith("no published Revenue Outturn", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
        }
    }
}

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
    var facts = Acc(slug);
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
            seenCodes.Add((CouncilCodes.VocabClassification, At(r, cC))); seenCodes.Add((CouncilCodes.VocabExplainedBy, At(r, cEB)));
            if (sched == "A") facts.A++; else if (sched == "D") { facts.D++; facts.DYears.Add(FinancialYear(month)); }
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
// The page's council list, count and names come from this file: the export's own columns, then the profile's full Name and a Short name, one row per SHIPPED council in the engine's order.
if (!profilesOnly)
    WriteRaw("index.csv", new UTF8Encoding(false).GetBytes(indexHeader + ",Name,Short\n" +
        string.Join("", published.Select(c => string.Join(",", rowOf[Slug(c.Name)]) + "," + Q(c.Name) + "," + Q(ShortName(c.Name)) + "\n"))));
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
        var bytes = File.ReadAllBytes(f);
        ScanSlice(Acc(slug), n[..Math.Min(7, n.Length)], bytes);
        WriteGz($"{slug}/{n}", bytes);
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
                          "budget_units", "budget_test", "budget_test_stage2", "budget_test_stage2b", "budget_test_stage2c", "budget_test_stage2d", "budget_test_pooled", "budget_test_pooled_all", "budget_test_pooled_all2",
                          "budget_test_pooled_all3", "crossref_alias_flows", "supplier_alias_grades", "debt_ledger", "debt_sink", "payment_misfits",
                          "check_rules" })   // check_rules: the engine's registry of every row filter each check applies (CheckRules.cs); the page shows it as one disclosure per check
{
    var bytes = WithoutHeldCouncils(n, File.ReadAllBytes(Path.Combine(export, n + ".csv")));
    if (n == "budget_units") bytes = WithFrozenColumn(bytes);
    WriteGz($"cross/{n}.csv", bytes);
}
if (!profilesOnly)
    foreach (var (file, column, vocab) in new[] { ("debt_sink", "Flags", CouncilCodes.VocabDebtFlag), ("payment_misfits", "LabelCheck", CouncilCodes.VocabLabelCheck), ("debt_ledger", "DecodeStatus", CouncilCodes.VocabDecodeStatus),
                                                                  ("debt_ledger", "Class", CouncilCodes.VocabLedgerClass) })
    {
        var recs = ParseCsv(File.ReadAllText(Path.Combine(export, file + ".csv"), Encoding.UTF8));
        int col = recs[0].IndexOf(column);
        if (col < 0) throw new InvalidDataException($"{file}.csv has no {column} column");
        foreach (var r in recs.Skip(1)) if (r.Count > col) seenCodes.Add((vocab, r[col]));
    }

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
// A public page is not the working notebook (2026-10-04, after an independent audit). Three more kinds of text never reach it:
//  (1) FIRST PERSON ("I read all 77 workbooks", "computed by the site, not by me"): the notes are the scanner's, never a person's voice;
//  (2) INTERNAL working notes ("need a human click-through to re-confirm before being shown live", "Recorded here as the caveat for anyone reading the top-20 export, not as a fix",
//      "A real, not-yet-confirmed lead"): they talk about the work or lean toward a conclusion, and say nothing a reader can use;
//  (3) STATISTICAL JARGON of the verification protocol ("seeded", "n=30", "Wilson 95% CI [88.6%, 100.0%]", "(seed 20263001)"): the plain facts around it (how many groups were
//      hand-checked and all confirmed) stay, the jargon goes.
// Every sentence or clause dropped is collected and printed at the end (HELD LINES) so the profile's owner can reword the source; the profile text itself is never edited here.
var FirstPerson = new System.Text.RegularExpressions.Regex(@"\b(?:I|I'm|I've|I'd|I'll|me|my|myself|mine)\b");   // case-sensitive on purpose
var WorkingNote = new System.Text.RegularExpressions.Regex(@"human click-through|not-yet-confirmed|\bnot as a fix\b|Recorded here as|top-?\d+ export|shown live to a visitor|\bseed \d+\b|\bWilson\b|\b95% ?CI\b|\bCI \[", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
var heldLines = new List<string>();
bool IsPrivate(string s) => InternalSentence.IsMatch(s) || FirstPerson.IsMatch(s) || WorkingNote.IsMatch(s);
string StripJargon(string s)
{
    // markdown the notes were written in ("**30/30 confirmed**", "`Unclear`") is not shown as symbols
    s = s.Replace("**", "");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"`([^`]*)`", "$1");
    // the verification protocol's own words: the plain facts stay ("hand-checks 30/30 in each class"), the jargon goes
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\([^()]*Wilson[^()]*\)", "");   // a bracket that holds only the interval goes whole
    s = System.Text.RegularExpressions.Regex.Replace(s, @",?\s*(?:95% )?Wilson (?:95% )?CI\s*\[[^\]]*\](?:\s*for n=\d+)?", "");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(?:seeded\s+)?(?:random\s+)?(?:sample\s+)?n=\d+,?\s+", "");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\bseeded\s+", "");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(seed \d+\)", "");
    // a trailing working-note clause: "..., and need a human click-through to re-confirm before being shown live to a visitor"
    s = System.Text.RegularExpressions.Regex.Replace(s, @",?\s*(?:and )?needs? a human click-through[^.]*(?=\.)", "");
    return s;
}
string? Clean(string slugName, string text)
{
    text = StripJargon(text);
    // a parenthetical that only points at the working files, the engine's own names, a person's voice or the verification protocol goes; the sentence around it stays
    var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\([^()]*\)", m =>
    {
        if (IsPrivate(m.Value) || m.Value.Contains("the engine")) { heldLines.Add($"[{slugName}] (clause in brackets) {m.Value.Trim()}"); return ""; }
        return m.Value;
    });
    var kept = new List<string>();
    foreach (var sn in Sentence.Split(stripped))
    {
        if (!IsPrivate(sn)) { kept.Add(sn); continue; }
        // a sentence that points at the working files goes whole (as it always did); one that is only first person, a working note or protocol jargon keeps the clauses
        // that are not ("Unclear 14,225 groups ...; I could not class two"), and the others are held
        if (InternalSentence.IsMatch(sn)) { heldLines.Add($"[{slugName}] {sn.Trim()}"); continue; }
        var clauses = sn.Split("; ");
        var ok = clauses.Where(c => !IsPrivate(c)).ToList();
        foreach (var c in clauses.Where(IsPrivate)) heldLines.Add($"[{slugName}] {c.Trim()}");
        if (ok.Count > 0 && ok.Count < clauses.Length)
        {
            var joined = string.Join("; ", ok).TrimEnd();
            kept.Add(joined.EndsWith('.') || joined.EndsWith(')') || joined.EndsWith('"') ? joined : joined + ".");
        }
    }
    if (kept.Count == 0) { Console.WriteLine($"HELD BACK [{slugName}]: {text[..Math.Min(130, text.Length)]}"); return null; }
    var o = Reword(string.Join(" ", kept));
    o = System.Text.RegularExpressions.Regex.Replace(o, @"[;:,]\s*$", ".");
    if (o != text) Console.WriteLine($"REWORDED [{slugName}] ({kept.Count}/{Sentence.Split(text).Length} sentences kept): {o[..Math.Min(170, o.Length)]}");
    return o;
}
var cleanQuirks = published.ToDictionary(c => c.Name, c => c.KnownQuirks.Select(q => Clean(c.Name, q)).Where(q => q is not null).Cast<string>().ToArray());
var cleanVerif = published.ToDictionary(c => c.Name, c => Clean(c.Name + " verification", c.VerificationNote) ?? "");
var cleanHowTo = published.ToDictionary(c => c.Name, c => Clean(c.Name + " how to find the file", c.HowToFindTheFile) ?? "");
Console.WriteLine($"=== HELD LINES (dropped from the public text, for review; {heldLines.Count}) ===");
foreach (var h in heldLines.Distinct()) Console.WriteLine("HELD: " + h);

// Facts per council (see the FACTS comment above). A "profiles" run measures nothing: it carries over what the last full build wrote.
var oldFacts = new Dictionary<string, JsonElement>();
if (profilesOnly)
{
    var oldPath = Path.Combine(outDir, "profiles.json.gz");
    if (File.Exists(oldPath))
    {
        using var gz = new GZipStream(File.OpenRead(oldPath), CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        foreach (var e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("slug", out var s) && e.TryGetProperty("facts", out var f)) oldFacts[s.GetString()!] = f.Clone();
    }
}
object? FactsOf(string slug)
{
    if (profilesOnly)
    {
        if (!oldFacts.TryGetValue(slug, out var kept)) throw new InvalidOperationException($"{slug}: no facts in the last profiles.json.gz; run a full build first (not the profiles-only run)");
        return kept;
    }
    var a = Acc(slug);
    budgetNote.TryGetValue(slug, out var bn);
    return a.ToFacts(bn.Units == 0 || bn.NoOutturn == bn.Units);
}

// Profiles: the councils' own text.
var profiles = published.Select(c => new Dictionary<string, object?>
{
    ["slug"] = Slug(c.Name),
    ["name"] = c.Name,
    ["short"] = ShortName(c.Name),
    ["facts"] = FactsOf(Slug(c.Name)),
    ["page"] = c.TransparencyPageUrl,
    ["howTo"] = cleanHowTo[c.Name],
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

// A code the page has no plain words for fails the run: nothing may print a raw engine code ("DebtLabelled", "NoReturnFlow"). Add the code to Showroom/Services/CouncilCodes.cs.
if (!profilesOnly)
{
    var unmapped = CouncilCodes.Audit(seenCodes);
    if (unmapped.Count > 0)
    {
        foreach (var u in unmapped) Console.WriteLine("UNMAPPED CODE: " + u);
        Console.WriteLine($"FAILED: {unmapped.Count} machine code(s) in the data have no plain-words entry in Showroom/Services/CouncilCodes.cs. Map them, then run again.");
        return 1;
    }
    Console.WriteLine($"CODES: all {seenCodes.Count} classification, reading, flag, label and status values in the data are mapped to plain words.");
}

// GitHub refuses any file over 100 MB and warns over 50 MB; the owner's bar is 50 MB. Report the largest and fail loudly above it.
var biggest = new DirectoryInfo(outDir).EnumerateFiles("*", SearchOption.AllDirectories).OrderByDescending(f => f.Length).First();
Console.WriteLine($"{files} files ({txSlices} transaction slices), {rawTotal / 1048576.0:F1} MB raw, {gzTotal / 1048576.0:F1} MB as shipped -> {outDir}");
Console.WriteLine($"largest file shipped: {biggest.FullName} {biggest.Length / 1048576.0:F2} MB");
if (biggest.Length > 50L * 1048576) { Console.WriteLine("A FILE IS OVER 50 MB"); return 1; }
return 0;

// The cross-council files are written by the scanner for every council it has taken on, including one still being onboarded (held above). A row of such a council must not be
// published before the council is, so rows whose Council / Payer / PayerSlug column names a held council are dropped; a file with no such row is shipped byte for byte.
byte[] WithoutHeldCouncils(string name, byte[] data)
{
    if (heldKeys.Count == 0) return data;
    string text = new UTF8Encoding(false).GetString(data);
    int pos = 0;
    string? Record()   // the next physical record, raw (a quoted field may hold a line break)
    {
        if (pos >= text.Length) return null;
        int start = pos; bool q = false;
        while (pos < text.Length) { char c = text[pos++]; if (c == '"') q = !q; else if (c == '\n' && !q) break; }
        return text[start..pos];
    }
    string FieldAt(string rec, int col)
    {
        var sb = new StringBuilder(); int f = 0; bool q = false;
        for (int i = 0; i < rec.Length; i++)
        {
            char c = rec[i];
            if (c == '"') { if (q && i + 1 < rec.Length && rec[i + 1] == '"') { if (f == col) sb.Append('"'); i++; } else q = !q; }
            else if (c == ',' && !q) { if (f == col) return sb.ToString(); f++; }
            else if ((c == '\r' || c == '\n') && !q) break;
            else if (f == col) sb.Append(c);
        }
        return f == col ? sb.ToString() : "";
    }
    var header = Record(); if (header is null) return data;
    var names = ParseCsv(header)[0];
    int col = names.FindIndex(x => x is "Council" or "Payer" or "PayerSlug");
    if (col < 0) return data;
    var kept = new StringBuilder(header); int dropped = 0;
    for (var r = Record(); r is not null; r = Record())
        if (heldKeys.Contains(FieldAt(r, col).Trim())) dropped++; else kept.Append(r);
    if (dropped == 0) return data;
    Console.WriteLine($"HELD ROWS DROPPED from cross/{name}.csv: {dropped}");
    return new UTF8Encoding(false).GetBytes(kept.ToString());
}
// budget_units.csv gains one column, FrozenEligible: whether the council-year was eligible in the freeze file of its own stage (export/budget_units_<stage>.csv, committed before that stage's statistic).
// The live file can hold a year that became eligible after its group was frozen (Wirral 2022-23: a missing month was added later); the pre-registered statistic does not include it, and the page says so.
// "" when no freeze file holds the council-year. The page reads columns by name, so nothing else about the file changes.
byte[] WithFrozenColumn(byte[] data)
{
    var frozen = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);   // stage -> "council|year" -> Eligible as frozen
    foreach (var stage in new[] { "stage1", "stage2", "stage2b", "stage2c", "stage2d" })
    {
        var p = Path.Combine(export, $"budget_units_{stage}.csv");
        if (!File.Exists(p)) continue;
        var t = ParseCsv(File.ReadAllText(p, Encoding.UTF8));
        int c = t[0].IndexOf("Council"), y = t[0].IndexOf("Year"), e = t[0].IndexOf("Eligible");
        if (c < 0 || y < 0 || e < 0) throw new InvalidDataException($"{p}: no Council, Year or Eligible column");
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in t.Skip(1)) if (r.Count > Math.Max(c, Math.Max(y, e))) d[r[c] + "|" + r[y]] = r[e];
        frozen[stage] = d;
    }
    var rows = ParseCsv(new UTF8Encoding(false).GetString(data));
    int cC = rows[0].IndexOf("Council"), cY = rows[0].IndexOf("Year"), cP = rows[0].IndexOf("Pool");
    if (cC < 0 || cY < 0 || cP < 0) throw new InvalidDataException("budget_units.csv: no Council, Year or Pool column");
    var sb = new StringBuilder();
    sb.Append(string.Join(",", rows[0].Select(Q))).Append(",FrozenEligible\n");
    int late = 0;
    foreach (var r in rows.Skip(1))
    {
        string f = "";
        if (r.Count > Math.Max(cC, Math.Max(cY, cP)))
        {
            string stage = r[cP] is "exploratory" or "confirmatory" ? "stage1" : r[cP];   // Stage 1's freeze file holds both of its pools
            if (frozen.TryGetValue(stage, out var d) && d.TryGetValue(r[cC] + "|" + r[cY], out var el)) f = el;
        }
        int cE = rows[0].IndexOf("Eligible");
        if (f == "False" && cE >= 0 && r.Count > cE && r[cE] == "True") { late++; Console.WriteLine($"ELIGIBLE AFTER ITS FREEZE: {r[cC]} {r[cY]} ({r[cP]})"); }
        sb.Append(string.Join(",", r.Select(Q))).Append(',').Append(f).Append('\n');
    }
    Console.WriteLine($"BUDGET UNITS: {rows.Count - 1} council-years, {late} eligible now that were not eligible when their group was frozen.");
    return new UTF8Encoding(false).GetBytes(sb.ToString());
}
string Slug(string name) => slugOf.TryGetValue(name, out var s) ? s : throw new InvalidOperationException("no slug for " + name);

// "Bracknell Forest Council" holds "bracknellforest"; "Royal Borough of Windsor and Maidenhead" is "rbwm" (the initials of its capitalised words).
static bool NameMatchesSlug(string name, string slug)
{
    string run = new string(name.Where(char.IsLetter).ToArray()).ToLowerInvariant();
    if (run.Contains(slug)) return true;
    string initials = new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsUpper(w[0])).Select(w => char.ToLowerInvariant(w[0])).ToArray());
    return initials == slug;
}

// The name the page uses in headings and links: "Wokingham Borough Council" is "Wokingham", "City of York Council" is "York", "London Borough of Merton" is "Merton",
// "Royal Borough of Windsor and Maidenhead" is "RBWM". Only the words every council name carries are dropped; nothing is listed per council.
static string ShortName(string name)
{
    if (name.StartsWith("Royal Borough of ")) return new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsUpper(w[0])).Select(w => w[0]).ToArray());
    string s = name;
    foreach (var lead in new[] { "City of ", "London Borough of " }) if (s.StartsWith(lead)) s = s[lead.Length..];
    foreach (var tail in new[] { " Metropolitan District Council", " Metropolitan Borough Council", " Borough Council", " City Council", " County Council", " Council" })
        if (s.EndsWith(tail)) { s = s[..^tail.Length]; break; }
    return s;
}

// One byte-level pass over a transaction slice (columns found by header name; nothing is allocated per field but the payee and the id when needed).
static void ScanSlice(FactsAcc a, string month, byte[] d)
{
    int i = 0, n = d.Length;
    if (n >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF) i = 3;
    var st = new int[40]; var ln = new int[40]; var qd = new bool[40];
    int cId = -1, cNet = -1, cGr = -1, cVat = -1, cSup = -1;
    bool header = true;
    long rows = 0, ph = 0;
    if (a.CurMonth != month) a.FlushMonth();
    a.CurMonth = month;
    ReadOnlySpan<byte> placeholder = "(no number published)"u8;
    string Str(int f) { var sp = d.AsSpan(st[f], ln[f]); var s = Encoding.UTF8.GetString(sp); return qd[f] ? s.Replace("\"\"", "\"") : s; }
    while (i < n)
    {
        int f = 0; bool end = false;
        while (!end)
        {
            int s = i, len; bool q = false;
            if (i < n && d[i] == (byte)'"')
            {
                q = true; i++; s = i;
                while (i < n) { if (d[i] == (byte)'"') { if (i + 1 < n && d[i + 1] == (byte)'"') { i += 2; continue; } break; } i++; }
                len = i - s; if (i < n) i++;
            }
            else { while (i < n && d[i] != (byte)',' && d[i] != (byte)'\n' && d[i] != (byte)'\r') i++; len = i - s; }
            if (f < 40) { st[f] = s; ln[f] = len; qd[f] = q; }
            f++;
            if (i >= n) end = true;
            else if (d[i] == (byte)',') i++;
            else { if (d[i] == (byte)'\r') i++; if (i < n && d[i] == (byte)'\n') i++; end = true; }
        }
        if (f == 1 && ln[0] == 0) continue;
        if (header)
        {
            header = false;
            for (int k = 0; k < Math.Min(f, 40); k++)
                switch (Str(k)) { case "TransactionId": cId = k; break; case "Net": cNet = k; break; case "Gross": cGr = k; break; case "VatAmount": cVat = k; break; case "SupplierName": cSup = k; break; }
            if (cId < 0 || cNet < 0 || cGr < 0 || cVat < 0 || cSup < 0) throw new InvalidDataException("slice header lacks a column the facts need: " + month);
            continue;
        }
        if (f <= Math.Max(Math.Max(cId, cNet), Math.Max(Math.Max(cGr, cVat), cSup))) continue;
        rows++;
        var idSpan = d.AsSpan(st[cId], ln[cId]);
        bool isPh = idSpan.StartsWith(placeholder);
        if (isPh) ph++;
        else
        {
            int e = idSpan.Length, b = e;
            while (b > 0 && idSpan[b - 1] >= (byte)'0' && idSpan[b - 1] <= (byte)'9') b--;
            a.CurRows++;
            if (b < e && e - b <= 18 && long.TryParse(Encoding.UTF8.GetString(idSpan[b..e]), out var cnt)) { a.CurSet.Add(cnt); if (cnt < a.CurMin) a.CurMin = cnt; }
        }
        var netSpan = d.AsSpan(st[cNet], ln[cNet]); var grSpan = d.AsSpan(st[cGr], ln[cGr]);
        if (ln[cVat] > 0) a.Vat++;
        if (grSpan.Length > 0 && netSpan.Length > 0 && !grSpan.SequenceEqual(netSpan))
        {
            if (decimal.TryParse(Encoding.UTF8.GetString(grSpan), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g)
                && decimal.TryParse(Encoding.UTF8.GetString(netSpan), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nt)) { if (Math.Abs(g - nt) > 0.005m) a.GrossNe++; }
            else a.GrossNe++;
        }
        var sup = Str(cSup);
        if (CouncilAudit.AuditEngine.IsRedactedSupplier(sup)) { a.Redacted++; if (sup.Contains("(pooled label)", StringComparison.OrdinalIgnoreCase)) a.Pooled++; }
    }
    a.Rows += rows; a.Placeholders += ph;
    if (month != "undated")
    {
        a.MonthRows.TryGetValue(month, out var t);
        a.MonthRows[month] = (t.Rows + rows, t.Ph + ph);
    }
}

sealed class FactsAcc
{
    public long A, D, Rows, Placeholders, GrossNe, Vat, Redacted, Pooled;
    public SortedSet<string> DYears = new(StringComparer.Ordinal);
    public SortedDictionary<string, (long Rows, long Ph)> MonthRows = new(StringComparer.Ordinal);
    // the per-file counter test, one month at a time (slices arrive month by month): the smallest trailing number is 1 and the trailing numbers are nearly all different
    public string CurMonth = ""; public long CurMin = long.MaxValue, CurRows; public HashSet<long> CurSet = new();
    public int CounterMonths, CountedMonths;
    public void FlushMonth()
    {
        if (CurMonth != "" && CurMonth != "undated" && CurRows >= 20)
        {
            CountedMonths++;
            if (CurMin <= 1 && CurSet.Count * 10 >= CurRows * 6) CounterMonths++;
        }
        CurMonth = ""; CurMin = long.MaxValue; CurRows = 0; CurSet = new();
    }

    public Dictionary<string, object?> ToFacts(bool noBudget)
    {
        var numbered = MonthRows.Where(m => m.Value.Rows > 0 && m.Value.Ph * 2 <= m.Value.Rows).Select(m => m.Key).ToList();
        var holders = MonthRows.Where(m => m.Value.Rows > 0 && m.Value.Ph * 2 > m.Value.Rows).Select(m => m.Key).ToList();
        bool noNumber = Rows > 0 && numbered.Count == 0;
        // numbers from one month on and none before it: every placeholder month precedes every numbered month
        bool partial = numbered.Count > 0 && holders.Count > 0 && string.CompareOrdinal(holders[^1], numbered[0]) < 0;
        bool noA = Rows > 0 && A == 0 && GrossNe == 0 && Vat == 0;
        bool noD = Rows > 0 && D == 0;
        FlushMonth();
        bool counterIds = CountedMonths > 0 && CounterMonths * 10 >= CountedMonths * 8;
        return new Dictionary<string, object?>
        {
            ["rows"] = Rows, ["noA"] = noA, ["noD"] = noD, ["noNumber"] = noNumber,
            ["numberedFirst"] = partial ? numbered[0] : null, ["numberedLast"] = partial ? numbered[^1] : null, ["numberedMonths"] = partial ? numbered.Count : 0,
            ["counterIds"] = counterIds,
            ["dByNumbering"] = noD && !noNumber && !partial && !counterIds,
            ["dYears"] = DYears.ToArray(),
            ["noBudget"] = noBudget, ["redacted"] = Redacted, ["pooled"] = Pooled,
        };
    }
}