// CouncilDbBuilder: offline step that turns a supported council's published files into compact
// NORMALISED per-year/month CSV + one precomputed-exceptions CSV per council, gzipped, shipped
// under Showroom/wwwroot/data/councils/<council>/, plus a small set of cross-council CSVs shipped under
// Showroom/wwwroot/data/councils/cross/. The raw files stay hosted and credited as the auditable source
// (Showroom/wwwroot/data/<council>/, where hosted) or are linked at the council's own page; this tool's
// output is a derived, browser-BulkLoad-ready copy, never a replacement for them.
//
// Schema (one shared shape across every council, so cross-council SQL/NEAREST queries just work): council,
// year, transid, paydate, net, gross, vattype, servicearea, costcentre, description, supplierkey, supplier.
// "costcentre" is blank for any council whose published file has no such column.
//
// Exceptions schema (one shared shape; the engine's own classification and per-row explanation are carried so
// the page can label and explain Schedule A/B rows, not just count them). Columns, in order:
// council,testlabel,type,sourcetag,transid,supplier,paydate,amount,servicearea,description,
// compareamount,groupkey,groupsize,oppositesign,classification,classificationdetail,linecount,net.
// "net" (appended 2026-10-03, Session 26 sync) is the line's published Net amount; the page needs it for the
// reversal-and-correction check, which is defined on Net (test 4 rows carry the repeated invoice Gross in "amount").
// A reader that stops at column 16 keeps working.
//
// TWO SOURCES, one engine. Wokingham and Reading are read from the raw files hosted beside the page. West
// Berkshire and RBWM have no raw copy hosted here; they are read from the per-file export CSVs the
// virtual-customer agent's CLI writes (VirtualCustomer\export\<slug>\<tag>.transactions.csv: every published
// column, after that council's own structural fixups and, for RBWM, after lines already published in an earlier
// file are dropped). Both go through the same vendored AuditEngine.Run. Checked 2026-10-03: re-running this engine
// over those export rows reproduces the exports' own exceptions.csv row-for-row (West Berkshire, RBWM and Wokingham,
// every schedule and classification). Set COUNCIL_EXPORT_DIR to read the exports from somewhere else.
//
// CROSS-COUNCIL files (cross/*.csv.gz) are straight copies of the CLI's computed outputs (alias-aware flows, the
// human grade table behind them, the loan-decode ledger, debt sink, payment misfits, file duplication, within-
// transaction repeats), plus cross/flow-rows.csv.gz: the published payment lines behind every accepted/probable
// flow row, read from the exports, so a visitor can see the source rows without loading six councils.
//
// Multi-council: each council also gets a "hasScheduleA" flag in manifest.json -- Reading, West Berkshire and RBWM
// publish one signed amount column with no separate net/gross/VAT split, so Schedule A (amount mismatch) is
// structurally inapplicable, not a clean/zero result; the page says so plainly instead of rendering an empty panel.

using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using CouncilAudit;

static string RepoRoot([CallerFilePath] string here = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));

var showroomData = Path.Combine(RepoRoot(), "Showroom", "wwwroot", "data");
var exportDir = Environment.GetEnvironmentVariable("COUNCIL_EXPORT_DIR") ?? @"C:\Users\dongy\VirtualCustomer\export";

static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
    ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
static string NormSupplier(string s) =>
    new string(s.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
static string G(string[] f, int i) => i >= 0 && i < f.Length ? f[i] : "";
static string? N(string[] f, int i) { var s = G(f, i); return s.Length == 0 ? null : s; }

// Writes <path>.gz and deletes the plain-text intermediate -- only the .gz ships in wwwroot (same
// pattern Prism's checkpoint uses), the plain file only ever existed so GZipStream had bytes to
// compress and so the byte-count printed to the console before this call is real, not estimated.
static void WriteGzipSidecarAndDeletePlain(string path)
{
    var bytes = File.ReadAllBytes(path);
    using (var fs = File.Create(path + ".gz"))
    using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        gz.Write(bytes, 0, bytes.Length);
    File.Delete(path);
}

// Rebuilds the engine's SpendRow list from one per-file export (every column the council published is in there).
static List<SpendRow> LoadExportTag(string path)
{
    var t = CsvReader.Parse(File.ReadAllBytes(path));
    var h = t[0];
    int Ix(string n) => Array.IndexOf(h, n);
    var rows = new List<SpendRow>();
    for (int j = 1; j < t.Count; j++)
    {
        var a = t[j]; if (a.Length < 12) continue;
        string raw = G(a, Ix("PayDate"));
        rows.Add(new SpendRow(G(a, Ix("Year")), G(a, Ix("TransactionId")), G(a, Ix("SupplierName")), raw, AuditEngine.ParseDate(raw),
            G(a, Ix("Description")), G(a, Ix("ServiceArea")),
            decimal.Parse(G(a, Ix("Net")), CultureInfo.InvariantCulture), decimal.Parse(G(a, Ix("Gross")), CultureInfo.InvariantCulture),
            N(a, Ix("VatAmount")) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : null, N(a, Ix("VatType")),
            int.TryParse(G(a, Ix("RowIndexInSource")), out var ri) ? ri : 0,
            N(a, Ix("SupplierInvoiceNumber")), N(a, Ix("CostCentreArea")), N(a, Ix("InvoiceType")), N(a, Ix("OtherColumns"))));
    }
    return rows;
}

const string T1 = "Exception test 1 - payments above the stated invoice amount";
const string T2 = "Exception test 2 - invoice amounts not covered by published payments";
const string T3 = "Exception test 3 - same supplier, amount, description and date under different transaction numbers";
const string T4 = "Exception test 4 - one transaction number covering several payees or pay dates";
const string T5 = "Exception test 5 - the same line listed more than once in the published file";
const string TR = "Exception test R - credit/refund rows vs. a matching same-supplier charge";

// One entry per council this builder ships. HasScheduleA: false means the council's own published
// shape has no separate invoice/payment amount to reconcile -- AuditEngine.Run still executes (Schedule A
// comes back empty by construction), but T1/T2 rows are deliberately NOT written to that council's
// exceptions file, so the Showroom page shows its own explanatory note instead of an empty panel.
// FromExport: read the per-file export CSVs instead of raw files (see the header).
var councils = new (string Key, SupportedCouncil Council, bool HasScheduleA, bool FromExport,
    (string Tag, string File, SourceFormat Format, string? EncodingOverride)[] Files)[]
{
    ("wokingham", SupportedCouncils.Wokingham, true, false, new (string, string, SourceFormat, string?)[]
    {
        ("FY2020-21", "wokingham-2020-21.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2021-22", "wokingham-2021-22.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2022-23", "wokingham-2022-23.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2023-24", "wokingham-2023-24.xlsx", SourceFormat.Xlsx, null),
        ("FY2024-25", "wokingham-2024-25.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2025-26", "wokingham-2025-26.csv", SourceFormat.Csv, "windows-1252"),
    }),
    // Full FY2020-21..FY2025-26 back-catalogue (63 of the council's 64 published periods --
    // reading_2021-05.xlsx is EXCLUDED, a council-side export fault: its header row doesn't
    // match its own data columns, the same corruption ReadingFixups.IsFileMappingUsable exists to catch).
    ("reading", SupportedCouncils.Reading, false, false, SupportedCouncils.Reading.Years
        .Where(y => y.Tag != "2021-05")
        .Select(y => (y.Tag, $"reading-{y.Tag}.{(y.Format == SourceFormat.Xlsx ? "xlsx" : "csv")}", y.Format, (string?)null))
        .ToArray()),
    // West Berkshire (no transaction reference published: the engine adds a synthetic per-row id, so
    // Schedules A, D and the same-line test are empty by construction) and RBWM (one amount column).
    ("westberkshire", SupportedCouncils.WestBerkshire, false, true, SupportedCouncils.WestBerkshire.Years
        .Select(y => (y.Tag, y.Tag + ".transactions.csv", y.Format, (string?)null)).ToArray()),
    ("rbwm", SupportedCouncils.Rbwm, false, true, SupportedCouncils.Rbwm.Years
        .Select(y => (y.Tag, y.Tag + ".transactions.csv", y.Format, (string?)null)).ToArray()),
};

var manifestEntries = new List<string>();

foreach (var cfg in councils)
{
    var rawDir = cfg.FromExport ? Path.Combine(exportDir, cfg.Key) : Path.Combine(showroomData, cfg.Key);
    var outDir = Path.Combine(showroomData, "councils", cfg.Key);
    Directory.CreateDirectory(outDir);

    var allRows = new List<SpendRow>();
    var warnings = new List<string>();
    var perTagRows = new Dictionary<string, List<SpendRow>>();
    var presentFiles = new List<(string Tag, string File, SourceFormat Format, string? EncodingOverride)>();

    Console.WriteLine($"[CouncilDbBuilder] {cfg.Key}: reading {(cfg.FromExport ? "export files" : "raw hosted files")}...");
    foreach (var y in cfg.Files)
    {
        var path = Path.Combine(rawDir, y.File);
        if (!File.Exists(path)) { Console.WriteLine($"  {y.Tag}: MISSING {path} -- skipped"); continue; }
        presentFiles.Add(y);
        List<SpendRow> rows;
        if (cfg.FromExport)
        {
            rows = LoadExportTag(path);
        }
        else
        {
            var bytes = File.ReadAllBytes(path);
            var table = y.Format == SourceFormat.Xlsx
                ? XlsxReader.Parse(bytes)
                : CsvReader.Parse(bytes, y.EncodingOverride is null ? null : Encoding.GetEncoding(y.EncodingOverride));
            // Reading-specific header/layout repairs (see CouncilAudit/ReadingFixups.cs) -- the
            // early .xlsx files have a split two-row header, and several files carry one-off
            // header typos/naming-convention drift the plain header lookup can't resolve alone.
            if (cfg.Key == "reading" && y.Format == SourceFormat.Xlsx)
                table = ReadingFixups.FixXlsxLayout(table);
            if (cfg.Key == "reading")
                ReadingFixups.ApplyHeaderFixups(table, ReadingFixups.HeaderFixups);
            int warnBefore = warnings.Count;
            rows = AuditEngine.MapRows(table, cfg.Council.Mapping, y.Tag, warnings);
            // Bad-file guard (ReadingFixups.IsFileMappingUsable): if some file's critical columns ever fail to
            // map, say so loudly rather than silently shipping blank/zero rows for that period.
            if (!ReadingFixups.IsFileMappingUsable(warnings, warnBefore))
                Console.WriteLine($"  {y.Tag}: WARNING -- {y.File} failed critical column mapping (see warning above); rows may be blank/zero for this period.");
        }
        perTagRows[y.Tag] = rows;
        allRows.AddRange(rows);
        Console.WriteLine($"  {y.Tag}: {rows.Count:N0} rows");
    }

    // ---- one normalised CSV per council per year/month ----
    long totalNormBytes = 0, totalGzBytes = 0;
    var gzByTag = new Dictionary<string, long>();   // compressed size of each period file, written to manifest.json so the page can say what a load will download
    foreach (var y in presentFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("council,year,transid,paydate,net,gross,vattype,servicearea,costcentre,description,supplierkey,supplier");
        foreach (var r in perTagRows[y.Tag])
        {
            sb.AppendLine(string.Join(",",
                cfg.Key, y.Tag, Csv(r.TransactionId), Csv(r.PayDateRaw ?? ""),
                Num(r.Net), Num(r.Gross), Csv(r.VatType ?? ""), Csv(r.ServiceArea ?? ""), Csv(r.CostCentreArea ?? ""),
                Csv(r.Description ?? ""), Csv(NormSupplier(r.Supplier)), Csv(r.Supplier)));
        }
        var path = Path.Combine(outDir, $"{y.Tag}.norm.csv");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        long normSize = new FileInfo(path).Length;
        WriteGzipSidecarAndDeletePlain(path);
        long gzSize = new FileInfo(path + ".gz").Length;
        gzByTag[y.Tag] = gzSize;
        totalNormBytes += normSize; totalGzBytes += gzSize;
        Console.WriteLine($"[normalised] {cfg.Key} {y.Tag}: {normSize:N0} bytes ({gzSize:N0} gzipped)");
    }

    // ---- precomputed exceptions, all applicable tests, one file per council (spans years/months
    // by nature -- Schedule B/D group across the whole council, not within one period) ----
    var result = AuditEngine.Run(allRows, cfg.Council.IdScope, cfg.Council.GrossMeaning, warnings);

    var excSb = new StringBuilder();
    excSb.AppendLine("council,testlabel,type,sourcetag,transid,supplier,paydate,amount,servicearea,description,compareamount,groupkey,groupsize,oppositesign,classification,classificationdetail,linecount,net");
    void WriteExc(string testLabel, string type, string sourceTag, string transId, string supplier,
        string? payDate, decimal amount, string? serviceArea, string? description,
        decimal? compareAmount, string groupKey, int groupSize, bool oppositeSign,
        string classification, string? classificationDetail, decimal netAmount, int lineCount = 1) =>
        excSb.AppendLine(string.Join(",", cfg.Key, Csv(testLabel), type, Csv(sourceTag), Csv(transId),
            Csv(supplier), Csv(payDate ?? ""), Num(amount), Csv(serviceArea ?? ""), Csv(description ?? ""),
            compareAmount is { } ca ? Num(ca) : "", Csv(groupKey), groupSize.ToString(CultureInfo.InvariantCulture),
            oppositeSign.ToString(), classification, Csv(classificationDetail ?? ""),
            lineCount.ToString(CultureInfo.InvariantCulture), Num(netAmount)));

    int t1n = 0, t2n = 0, t3n = 0, t4n = 0, t5n = 0;
    var classificationCounts = new Dictionary<string, int>();
    void Count(string cls) => classificationCounts[cls] = classificationCounts.GetValueOrDefault(cls) + 1;

    if (cfg.HasScheduleA)
    {
        foreach (var r in result.ScheduleA)
        {
            bool paymentsExceed = r.Gross < r.ExpectedGross;
            bool oppositeSign = !paymentsExceed && r.Net != 0 && r.Gross != 0 && r.Net == -r.Gross;
            // amount = the real sum of published PAYMENTS (r.Net); compareamount = the stated invoice figure (r.Gross).
            WriteExc(paymentsExceed ? T1 : T2, paymentsExceed ? "Discrepancy" : "Unreconciled",
                r.SourceTag, r.TransactionId, r.Supplier, r.PayDate, r.Net, r.ServiceArea, r.Description,
                r.Gross, "", 1, oppositeSign, r.Classification.ToString(), r.ClassificationDetail, r.Net, r.LineCount);
            if (paymentsExceed) t1n++; else t2n++;
            Count(r.Classification.ToString());
        }
    }
    else if (result.ScheduleA.Count > 0)
    {
        // Should not happen for a no-net/gross-split council (net==gross by construction, so every row reconciles
        // at diff==0) -- if it ever does, fail loudly rather than silently drop real exceptions.
        throw new InvalidOperationException(
            $"{cfg.Key}: HasScheduleA=false but AuditEngine.Run produced {result.ScheduleA.Count} Schedule A rows -- " +
            "investigate before shipping (either the mapping now publishes a real net/gross split, or this flag is wrong).");
    }

    int g3Id = 0;
    foreach (var g in result.ScheduleB)
    {
        g3Id++;
        var key = $"g3-{g3Id}";
        int n = g.Members.Count;
        foreach (var m in g.Members)
        {
            WriteExc(T3, "Anomaly", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw, m.Net, m.ServiceArea, m.Description,
                null, key, n, false, g.Classification.ToString(), g.ClassificationDetail, m.Net);
            t3n++;
        }
        Count("B:" + g.Classification);
    }
    foreach (var g in result.ScheduleD)
    {
        var key = $"g4-{g.SourceTag}-{g.TransactionId}";
        int n = g.Members.Count;
        foreach (var m in g.Members)
        { WriteExc(T4, "Discrepancy", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw, m.Gross, m.ServiceArea, m.Description, null, key, n, false, "", null, m.Net); t4n++; }
    }

    bool IsRedacted(string s) => s.Contains("REDACT", StringComparison.OrdinalIgnoreCase);
    var clean = allRows.Where(r => !IsRedacted(r.TransactionId) && !IsRedacted(r.Supplier)).ToList();
    var dupLineGroups = clean
        .GroupBy(r => (r.SourceTag, r.TransactionId, r.Supplier, r.Net, r.Gross, (r.Description ?? "").Trim(), r.PayDateRaw))
        .Where(g => g.Count() > 1);
    // One row per REPEATED LINE (not one aggregate per group) so the Showroom page can show the actual
    // repeated lines paired up via groupkey, same shape as test 3/4's group evidence.
    int g5Id = 0;
    foreach (var g in dupLineGroups)
    {
        g5Id++;
        var key = $"g5-{g5Id}";
        int n = g.Count();
        foreach (var m in g)
        {
            WriteExc(T5, "Discrepancy", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw,
                m.Gross, m.ServiceArea, m.Description, null, key, n, false, "", null, m.Net);
            t5n++;
        }
    }

    // Schedule R (credit/refund rows vs. a matching same-supplier charge) -- empty by construction for any council
    // that doesn't publish/map an Invoice Type column; written unconditionally.
    int trMatched = 0, trUnmatched = 0, trStanding = 0;
    foreach (var r in result.ScheduleR)
    {
        WriteExc(TR, "Unreconciled", r.SourceTag, r.TransactionId, r.Supplier, r.PayDate, r.Amount, "", r.Description,
            null, "", 1, false, r.Classification.ToString(), r.ClassificationDetail, r.Amount);
        if (r.Classification == CreditMatchClassification.MatchedOffsettingCharge) trMatched++;
        else if (r.Classification == CreditMatchClassification.StandingPaymentUnderRefundType) trStanding++;
        else trUnmatched++;
        Count("R:" + r.Classification);
    }
    if (result.ScheduleR.Count > 0)
        Console.WriteLine($"[schedule-r] {cfg.Key}: {result.ScheduleR.Count:N0} credit/refund rows ({trMatched} matched, {trUnmatched} unmatched, {trStanding} standing payment under refund type)");

    var excPath = Path.Combine(outDir, "exceptions.csv");
    File.WriteAllText(excPath, excSb.ToString(), new UTF8Encoding(false));
    long excNorm = new FileInfo(excPath).Length;
    WriteGzipSidecarAndDeletePlain(excPath);
    long excGz = new FileInfo(excPath + ".gz").Length;
    totalNormBytes += excNorm; totalGzBytes += excGz;
    Console.WriteLine($"[exceptions] {cfg.Key}: {t1n + t2n + t3n + t4n + t5n + result.ScheduleR.Count:N0} total " +
        $"({t1n} T1 Discrepancy, {t2n} T2 Unreconciled, {t3n} T3 Anomaly, {t4n} T4 Discrepancy, {t5n} T5 Discrepancy, " +
        $"{result.ScheduleR.Count} TR CreditMatch): {excNorm:N0} bytes ({excGz:N0} gzipped)");
    Console.WriteLine($"[classification] {cfg.Key}: " + string.Join(", ", classificationCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value:N0}")));
    Console.WriteLine($"[schedule-d] {cfg.Key}: {result.ScheduleD.Count:N0} groups, {t4n:N0} lines");
    foreach (var w in warnings) Console.WriteLine($"[warning] {cfg.Key}: {w}");

    // ---- this council's own manifest.json entry ----
    var sbM = new StringBuilder();
    sbM.AppendLine("    {");
    sbM.AppendLine($"      \"key\": \"{cfg.Key}\",");
    sbM.AppendLine($"      \"name\": \"{cfg.Council.Name}\",");
    sbM.AppendLine($"      \"hasScheduleA\": {(cfg.HasScheduleA ? "true" : "false")},");
    sbM.AppendLine("      \"years\": [");
    for (int i = 0; i < presentFiles.Count; i++)
    {
        var y = presentFiles[i];
        var rows = perTagRows[y.Tag].Count;
        sbM.AppendLine($"        {{ \"tag\": \"{y.Tag}\", \"file\": \"{y.Tag}.norm.csv.gz\", \"rows\": {rows}, \"gzBytes\": {gzByTag[y.Tag]} }}{(i < presentFiles.Count - 1 ? "," : "")}");
    }
    sbM.AppendLine("      ],");
    sbM.AppendLine("      \"exceptionsFile\": \"exceptions.csv.gz\",");
    sbM.AppendLine($"      \"exceptionsGzBytes\": {excGz},");
    sbM.AppendLine($"      \"totalRows\": {allRows.Count}");
    sbM.AppendLine("    }");
    manifestEntries.Add(sbM.ToString().TrimEnd());

    Console.WriteLine($"[CouncilDbBuilder] {cfg.Key} TOTAL: {totalNormBytes:N0} bytes normalised ({totalGzBytes:N0} gzipped) " +
        $"across {presentFiles.Count} period files + 1 exceptions file.");
}

// ---- manifest.json: what the page can load, across every council ----
var manifestPath = Path.Combine(showroomData, "councils", "manifest.json");
var manifest = "{\n  \"councils\": [\n" + string.Join(",\n", manifestEntries) + "\n  ]\n}\n";
File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
Console.WriteLine($"[CouncilDbBuilder] wrote manifest: {manifestPath}");

// ---- cross-council files: straight copies of the CLI's computed outputs ----
var crossDir = Path.Combine(showroomData, "councils", "cross");
Directory.CreateDirectory(crossDir);
foreach (var (src, dst) in new[]
{
    ("crossref_alias_flows.csv", "flows"), ("supplier_alias_grades.csv", "alias-grades"),
    ("debt_ledger.csv", "debt-ledger"), ("debt_sink.csv", "debt-sink"), ("payment_misfits.csv", "misfits"),
    ("file_duplication.csv", "file-duplication"), ("within_txn_repeats.csv", "within-txn"),
})
{
    var from = Path.Combine(exportDir, src);
    var to = Path.Combine(crossDir, dst + ".csv");
    File.WriteAllText(to, File.ReadAllText(from, Encoding.UTF8), new UTF8Encoding(false)); // UTF-8, no BOM
    long size = new FileInfo(to).Length;
    WriteGzipSidecarAndDeletePlain(to);
    Console.WriteLine($"[cross] {dst}: {size:N0} bytes ({new FileInfo(to + ".gz").Length:N0} gzipped)");
}

// ---- flow-rows: the published payment lines behind every accepted/probable flow row ----
{
    var flows = CsvReader.Parse(File.ReadAllBytes(Path.Combine(exportDir, "crossref_alias_flows.csv")));
    var fh = flows[0];
    int fi(string n) => Array.IndexOf(fh, n);
    var wanted = flows.Skip(1)
        .Where(f => G(f, fi("Verdict")) is "accept" or "probable")
        .Select(f => (Payer: G(f, fi("Payer")), Key: G(f, fi("SupplierKey")), Rows: int.Parse(G(f, fi("Rows"))),
                      Net: decimal.Parse(G(f, fi("Net")), CultureInfo.InvariantCulture))).ToList();
    var sb = new StringBuilder();
    sb.AppendLine("council,year,transid,paydate,net,supplier,description,supplierkey");
    int mismatches = 0;
    foreach (var payerGroup in wanted.GroupBy(w => w.Payer))
    {
        var keys = payerGroup.Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, (int Rows, decimal Net)>();
        foreach (var file in Directory.GetFiles(Path.Combine(exportDir, payerGroup.Key), "*.transactions.csv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var t = CsvReader.Parse(File.ReadAllBytes(file));
            var h = t[0];
            int Ix(string n) => Array.IndexOf(h, n);
            int iKey = Ix("SupplierKey");
            for (int j = 1; j < t.Count; j++)
            {
                var a = t[j]; if (a.Length < 12) continue;
                var key = G(a, iKey);
                if (!keys.Contains(key)) continue;
                var net = decimal.Parse(G(a, Ix("Net")), CultureInfo.InvariantCulture);
                found[key] = found.TryGetValue(key, out var cur) ? (cur.Rows + 1, cur.Net + net) : (1, net);
                sb.AppendLine(string.Join(",", payerGroup.Key, Csv(G(a, Ix("Year"))), Csv(G(a, Ix("TransactionId"))), Csv(G(a, Ix("PayDate"))),
                    Num(net), Csv(G(a, Ix("SupplierName"))), Csv(G(a, Ix("Description"))), Csv(key)));
            }
        }
        foreach (var w in payerGroup)
        {
            var got = found.GetValueOrDefault(w.Key);
            if (got.Rows != w.Rows || Math.Abs(got.Net - w.Net) >= 0.005m) // the flows file rounds Net to the penny
            {
                mismatches++;
                Console.WriteLine($"[flow-rows] MISMATCH {w.Payer} {w.Key}: flows file says {w.Rows} rows / {w.Net:N2}, export rows give {got.Rows} / {got.Net:N2}");
            }
        }
    }
    var frPath = Path.Combine(crossDir, "flow-rows.csv");
    File.WriteAllText(frPath, sb.ToString(), new UTF8Encoding(false));
    long frSize = new FileInfo(frPath).Length;
    WriteGzipSidecarAndDeletePlain(frPath);
    Console.WriteLine($"[cross] flow-rows: {frSize:N0} bytes ({new FileInfo(frPath + ".gz").Length:N0} gzipped), {wanted.Count} flow rows, {mismatches} count/net mismatches vs the flows file");
}
