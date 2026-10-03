// CouncilDbBuilder: offline step that turns a supported council's hosted raw files into compact
// NORMALISED per-year/month CSV + one precomputed-exceptions CSV per council, gzipped, shipped
// under Showroom/wwwroot/data/councils/<council>/. The raw files stay hosted and credited as the
// auditable source (Showroom/wwwroot/data/<council>/) -- this tool's output is a derived,
// browser-BulkLoad-ready copy, never a replacement for the raw files.
//
// Schema (one shared shape across every council, so cross-council SQL/NEAREST queries just work
// once a second council is onboarded): council, year, transid, paydate, net, gross, vattype,
// servicearea, costcentre, description, supplierkey, supplier.
// "costcentre" is blank for any council whose published file has no such column (ServiceArea is
// the closest thing) -- carried as its own field so the shared schema doesn't change per council.
//
// Exceptions schema (one shared shape, extended 2026-10-03 to carry the engine's own
// classification so the Showroom page can label, not just count, Schedule A/B rows):
// council,testlabel,type,sourcetag,transid,supplier,paydate,amount,servicearea,description,
// handverified,compareamount,groupkey,groupsize,oppositesign,classification,classificationdetail.
//
// Multi-council (2026-10-03, Reading added alongside Wokingham): each council also gets a
// "hasScheduleA" flag in manifest.json -- Reading publishes one signed Amount column with no
// separate net/gross/VAT split, so AuditEngine's Schedule A (amount mismatch) is structurally
// inapplicable, not a clean/zero result, and the Showroom page uses this flag to say so plainly
// instead of rendering an empty "None found" Schedule A panel.

using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using CouncilAudit;

static string RepoRoot([CallerFilePath] string here = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));

var showroomData = Path.Combine(RepoRoot(), "Showroom", "wwwroot", "data");

static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n')
    ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
static string NormSupplier(string s) =>
    new string(s.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

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

const string T1 = "Exception test 1 - payments above the stated invoice amount";
const string T2 = "Exception test 2 - invoice amounts not covered by published payments";
const string T3 = "Exception test 3 - same supplier, amount, description and date under different transaction numbers";
const string T4 = "Exception test 4 - one transaction number covering several payees or pay dates";
const string T5 = "Exception test 5 - the same line listed more than once in the published file";
const string TR = "Exception test R - credit/refund rows vs. a matching same-supplier charge";

// One entry per council this builder ships. HasScheduleA: false means the council's own published
// shape has no separate invoice/payment amount to reconcile (Reading: one signed Amount column) --
// AuditEngine.Run still executes (Schedule A comes back empty by construction, net==gross always
// since no VAT info is published), but T1/T2 rows are deliberately NOT written to that council's
// exceptions file, so the Showroom page shows its own explanatory note instead of an empty panel.
var councils = new (string Key, SupportedCouncil Council, bool HasScheduleA,
    (string Tag, string File, SourceFormat Format, string? EncodingOverride)[] Files)[]
{
    ("wokingham", SupportedCouncils.Wokingham, true, new (string, string, SourceFormat, string?)[]
    {
        ("FY2020-21", "wokingham-2020-21.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2021-22", "wokingham-2021-22.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2022-23", "wokingham-2022-23.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2023-24", "wokingham-2023-24.xlsx", SourceFormat.Xlsx, null),
        ("FY2024-25", "wokingham-2024-25.csv", SourceFormat.Csv, "windows-1252"),
        ("FY2025-26", "wokingham-2025-26.csv", SourceFormat.Csv, "windows-1252"),
    }),
    ("reading", SupportedCouncils.Reading, false, new (string, string, SourceFormat, string?)[]
    {
        ("2021-12", "reading-2021-12.csv", SourceFormat.Csv, null), // UTF-8 (with BOM) -- auto-detected
        ("2022-04", "reading-2022-04.csv", SourceFormat.Csv, null), // windows-1252 -- auto-detected
        ("2023-03", "reading-2023-03.csv", SourceFormat.Csv, null),
        ("2024-06", "reading-2024-06.csv", SourceFormat.Csv, null),
        ("2026-02", "reading-2026-02.csv", SourceFormat.Csv, null),
    }),
};

var manifestEntries = new List<string>();

foreach (var cfg in councils)
{
    var rawDir = Path.Combine(showroomData, cfg.Key);
    var outDir = Path.Combine(showroomData, "councils", cfg.Key);
    Directory.CreateDirectory(outDir);

    var allRows = new List<SpendRow>();
    var warnings = new List<string>();
    var perTagRows = new Dictionary<string, List<SpendRow>>();

    Console.WriteLine($"[CouncilDbBuilder] {cfg.Key}: parsing raw hosted files...");
    foreach (var y in cfg.Files)
    {
        var bytes = File.ReadAllBytes(Path.Combine(rawDir, y.File));
        var table = y.Format == SourceFormat.Xlsx
            ? XlsxReader.Parse(bytes)
            : CsvReader.Parse(bytes, y.EncodingOverride is null ? null : Encoding.GetEncoding(y.EncodingOverride));
        var rows = AuditEngine.MapRows(table, cfg.Council.Mapping, y.Tag, warnings);
        perTagRows[y.Tag] = rows;
        allRows.AddRange(rows);
        Console.WriteLine($"  {y.Tag}: {rows.Count:N0} rows");
    }

    // ---- one normalised CSV per council per year/month ----
    long totalNormBytes = 0, totalGzBytes = 0;
    foreach (var y in cfg.Files)
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
        totalNormBytes += normSize; totalGzBytes += gzSize;
        Console.WriteLine($"[normalised] {cfg.Key} {y.Tag}: {normSize:N0} bytes ({gzSize:N0} gzipped)");
    }

    // ---- precomputed exceptions, all applicable tests, one file per council (spans years/months
    // by nature -- Schedule B/D group across the whole council, not within one period) ----
    var result = AuditEngine.Run(allRows, cfg.Council.IdScope, cfg.Council.GrossMeaning, warnings);
    var handVerified = new HashSet<string> { "3815610", "3809081", "7032959" };

    // compareamount/groupkey/groupsize/oppositesign power the Showroom page's evidence/highlighting
    // panel; classification/classificationdetail (added 2026-10-03) carry AuditEngine's own
    // ScheduleAClassification/ScheduleBClassification label + per-row explanation, so the page can
    // surface a neutral label (ranked below the genuinely unexplained rows) instead of one
    // undifferentiated pile; linecount (added 2026-10-03) carries ScheduleARow.LineCount so the
    // FOI-letter sentence generator can say "one payment of GBPX" vs "N payments totalling GBPX".
    // Blank classification/detail, linecount=1 for T4/T5, which have no such concept.
    var excSb = new StringBuilder();
    excSb.AppendLine("council,testlabel,type,sourcetag,transid,supplier,paydate,amount,servicearea,description,handverified,compareamount,groupkey,groupsize,oppositesign,classification,classificationdetail,linecount");
    void WriteExc(string testLabel, string type, string sourceTag, string transId, string supplier,
        string? payDate, decimal amount, string? serviceArea, string? description,
        decimal? compareAmount, string groupKey, int groupSize, bool oppositeSign,
        string classification, string? classificationDetail, int lineCount = 1) =>
        excSb.AppendLine(string.Join(",", cfg.Key, Csv(testLabel), type, Csv(sourceTag), Csv(transId),
            Csv(supplier), Csv(payDate ?? ""), Num(amount), Csv(serviceArea ?? ""), Csv(description ?? ""),
            handVerified.Contains(transId).ToString(),
            compareAmount is { } ca ? Num(ca) : "", Csv(groupKey), groupSize.ToString(CultureInfo.InvariantCulture),
            oppositeSign.ToString(), classification, Csv(classificationDetail ?? ""),
            lineCount.ToString(CultureInfo.InvariantCulture)));

    int t1n = 0, t2n = 0, t3n = 0, t4n = 0, t5n = 0;
    var classificationCounts = new Dictionary<string, int>();
    void Count(string cls) => classificationCounts[cls] = classificationCounts.GetValueOrDefault(cls) + 1;

    if (cfg.HasScheduleA)
    {
        foreach (var r in result.ScheduleA)
        {
            bool paymentsExceed = r.Gross < r.ExpectedGross;
            bool oppositeSign = !paymentsExceed && r.Net != 0 && r.Gross != 0 && r.Net == -r.Gross;
            // FIX (2026-10-03, caught building the Showroom FOI-letter sentence generator): amount/
            // compareamount used to be written as (r.Gross, r.ExpectedGross) -- backwards. r.Net is
            // the real sum of published PAYMENTS; r.Gross is the real stated invoice figure. See the
            // matching fix + comment in CouncilSpending.razor's own-file BuildTests.
            WriteExc(paymentsExceed ? T1 : T2, paymentsExceed ? "Discrepancy" : "Unreconciled",
                r.SourceTag, r.TransactionId, r.Supplier, r.PayDate, r.Net, r.ServiceArea, r.Description,
                r.Gross, "", 1, oppositeSign, r.Classification.ToString(), r.ClassificationDetail, r.LineCount);
            if (paymentsExceed) t1n++; else t2n++;
            Count(r.Classification.ToString());
        }
    }
    else if (result.ScheduleA.Count > 0)
    {
        // Should not happen for a no-net/gross-split council (net==gross by construction, so
        // every row reconciles at diff==0 and AuditEngine.Run never adds it) -- if it ever does,
        // fail loudly rather than silently drop real exceptions for a council marked HasScheduleA=false.
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
                null, key, n, false, g.Classification.ToString(), g.ClassificationDetail);
            t3n++;
        }
        Count("B:" + g.Classification);
    }
    foreach (var g in result.ScheduleD)
    {
        var key = $"g4-{g.SourceTag}-{g.TransactionId}";
        int n = g.Members.Count;
        foreach (var m in g.Members)
        { WriteExc(T4, "Discrepancy", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw, m.Gross, m.ServiceArea, m.Description, null, key, n, false, "", null); t4n++; }
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
                m.Gross, m.ServiceArea, m.Description, null, key, n, false, "", null);
            t5n++;
        }
    }

    // Schedule R (credit/refund rows vs. a matching same-supplier charge, Session 14) -- empty by
    // construction for any council (like Wokingham) that doesn't publish/map an Invoice Type
    // column; written unconditionally, same "not a bug" principle as Reading's own empty Schedule A.
    int trMatched = 0, trUnmatched = 0;
    foreach (var r in result.ScheduleR)
    {
        WriteExc(TR, "Unreconciled", r.SourceTag, r.TransactionId, r.Supplier, r.PayDate, r.Amount, "", r.Description,
            null, "", 1, false, r.Classification.ToString(), r.ClassificationDetail);
        if (r.Classification == CreditMatchClassification.MatchedOffsettingCharge) trMatched++; else trUnmatched++;
        Count("R:" + r.Classification);
    }
    if (result.ScheduleR.Count > 0)
        Console.WriteLine($"[schedule-r] {cfg.Key}: {result.ScheduleR.Count:N0} credit/refund rows ({trMatched} matched, {trUnmatched} unmatched)");

    var excPath = Path.Combine(outDir, "exceptions.csv");
    File.WriteAllText(excPath, excSb.ToString(), new UTF8Encoding(false));
    long excNorm = new FileInfo(excPath).Length;
    WriteGzipSidecarAndDeletePlain(excPath);
    long excGz = new FileInfo(excPath + ".gz").Length;
    totalNormBytes += excNorm; totalGzBytes += excGz;
    Console.WriteLine($"[exceptions] {cfg.Key}: {t1n + t2n + t3n + t4n + t5n + trMatched + trUnmatched:N0} total " +
        $"({t1n} T1 Discrepancy, {t2n} T2 Unreconciled, {t3n} T3 Anomaly, {t4n} T4 Discrepancy, {t5n} T5 Discrepancy, " +
        $"{trMatched + trUnmatched} TR CreditMatch): {excNorm:N0} bytes ({excGz:N0} gzipped)");
    Console.WriteLine($"[classification] {cfg.Key}: " + string.Join(", ", classificationCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value:N0}")));
    int unreconciledCount = classificationCounts.GetValueOrDefault(ScheduleAClassification.Unreconciled.ToString());
    Console.WriteLine($"[classification] {cfg.Key}: Unreconciled (Schedule A) = {unreconciledCount:N0}");

    // ---- this council's own manifest.json entry ----
    var sbM = new StringBuilder();
    sbM.AppendLine("    {");
    sbM.AppendLine($"      \"key\": \"{cfg.Key}\",");
    sbM.AppendLine($"      \"name\": \"{cfg.Council.Name}\",");
    sbM.AppendLine($"      \"hasScheduleA\": {(cfg.HasScheduleA ? "true" : "false")},");
    sbM.AppendLine("      \"years\": [");
    for (int i = 0; i < cfg.Files.Length; i++)
    {
        var y = cfg.Files[i];
        var rows = perTagRows[y.Tag].Count;
        sbM.AppendLine($"        {{ \"tag\": \"{y.Tag}\", \"file\": \"{y.Tag}.norm.csv.gz\", \"rows\": {rows} }}{(i < cfg.Files.Length - 1 ? "," : "")}");
    }
    sbM.AppendLine("      ],");
    sbM.AppendLine("      \"exceptionsFile\": \"exceptions.csv.gz\",");
    sbM.AppendLine($"      \"totalRows\": {allRows.Count}");
    sbM.AppendLine("    }");
    manifestEntries.Add(sbM.ToString().TrimEnd());

    Console.WriteLine($"[CouncilDbBuilder] {cfg.Key} TOTAL: {totalNormBytes:N0} bytes normalised ({totalGzBytes:N0} gzipped) " +
        $"across {cfg.Files.Length} period files + 1 exceptions file.");
}

// ---- manifest.json: what the page fetches to know what's available to load, across every council ----
var manifestPath = Path.Combine(showroomData, "councils", "manifest.json");
var manifest = "{\n  \"councils\": [\n" + string.Join(",\n", manifestEntries) + "\n  ]\n}\n";
File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
Console.WriteLine($"[CouncilDbBuilder] wrote manifest: {manifestPath}");
