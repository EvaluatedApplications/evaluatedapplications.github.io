// CouncilDbBuilder: offline step that turns a supported council's hosted raw files into compact
// NORMALISED per-year CSV + one precomputed-exceptions CSV, gzipped, shipped under
// Showroom/wwwroot/data/councils/<council>/. The raw files stay hosted and credited as the
// auditable source (Showroom/wwwroot/data/wokingham/) -- this tool's output is a derived,
// browser-BulkLoad-ready copy, never a replacement for the raw files.
//
// Schema (one shared shape across every council, so cross-council SQL/NEAREST queries just work
// once a second council is onboarded): council, year, transid, paydate, net, gross, vattype,
// servicearea, costcentre, description, supplierkey, supplier.
// "costcentre" is always blank for Wokingham -- the published file has no such column (ServiceArea
// is the closest thing); carried as its own field so the shared schema doesn't have to change the
// day a council that DOES publish one is added.

using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using CouncilAudit;

static string RepoRoot([CallerFilePath] string here = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));

var showroomData = Path.Combine(RepoRoot(), "Showroom", "wwwroot", "data");
var rawDir = Path.Combine(showroomData, "wokingham");
var outDir = Path.Combine(showroomData, "councils", "wokingham");
Directory.CreateDirectory(outDir);

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

var years = new (string Tag, string File, SourceFormat Format)[]
{
    ("FY2020-21", "wokingham-2020-21.csv", SourceFormat.Csv),
    ("FY2021-22", "wokingham-2021-22.csv", SourceFormat.Csv),
    ("FY2022-23", "wokingham-2022-23.csv", SourceFormat.Csv),
    ("FY2023-24", "wokingham-2023-24.xlsx", SourceFormat.Xlsx),
    ("FY2024-25", "wokingham-2024-25.csv", SourceFormat.Csv),
    ("FY2025-26", "wokingham-2025-26.csv", SourceFormat.Csv),
};

const string Council = "wokingham";
var allRows = new List<SpendRow>();
var warnings = new List<string>();
var perYearRows = new Dictionary<string, List<SpendRow>>();

Console.WriteLine("[CouncilDbBuilder] parsing raw hosted files...");
foreach (var y in years)
{
    var bytes = File.ReadAllBytes(Path.Combine(rawDir, y.File));
    var table = y.Format == SourceFormat.Xlsx ? XlsxReader.Parse(bytes) : CsvReader.Parse(bytes, Encoding.GetEncoding(1252));
    var rows = AuditEngine.MapRows(table, SupportedCouncils.Wokingham.Mapping, y.Tag, warnings);
    perYearRows[y.Tag] = rows;
    allRows.AddRange(rows);
    Console.WriteLine($"  {y.Tag}: {rows.Count:N0} rows");
}

// ---- one normalised CSV per council per year ----
long totalNormBytes = 0, totalGzBytes = 0;
foreach (var y in years)
{
    var sb = new StringBuilder();
    sb.AppendLine("council,year,transid,paydate,net,gross,vattype,servicearea,costcentre,description,supplierkey,supplier");
    foreach (var r in perYearRows[y.Tag])
    {
        sb.AppendLine(string.Join(",",
            Council, y.Tag, Csv(r.TransactionId), Csv(r.PayDateRaw ?? ""),
            Num(r.Net), Num(r.Gross), Csv(r.VatType ?? ""), Csv(r.ServiceArea ?? ""), "",
            Csv(r.Description ?? ""), Csv(NormSupplier(r.Supplier)), Csv(r.Supplier)));
    }
    var path = Path.Combine(outDir, $"{y.Tag}.norm.csv");
    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    long normSize = new FileInfo(path).Length;
    WriteGzipSidecarAndDeletePlain(path);
    long gzSize = new FileInfo(path + ".gz").Length;
    totalNormBytes += normSize; totalGzBytes += gzSize;
    Console.WriteLine($"[normalised] {y.Tag}: {normSize:N0} bytes ({gzSize:N0} gzipped)");
}

// ---- precomputed exceptions, all 5 tests, one file (spans years by nature -- Schedule B/D group
// across the whole council, not within one year) ----
var result = AuditEngine.Run(allRows, SupportedCouncils.Wokingham.IdScope, SupportedCouncils.Wokingham.GrossMeaning, warnings);
var handVerified = new HashSet<string> { "3815610", "3809081", "7032959" };

// compareamount/groupkey/groupsize/oppositesign power the Showroom page's evidence/highlighting
// panel (see CouncilSpending.razor's BuildTestsFromExceptionsRows) -- compareamount is the "other"
// figure in a two-value comparison (test 1/2 only), groupkey links sibling rows within the SAME
// test so the page can show them aligned (test 3/4/5), groupsize is that group's row count, and
// oppositesign flags a test-2 row whose published net/gross have opposite signs (likely a
// credit/refund line worth checking before treating it as unreconciled).
var excSb = new StringBuilder();
excSb.AppendLine("council,testlabel,type,sourcetag,transid,supplier,paydate,amount,servicearea,description,handverified,compareamount,groupkey,groupsize,oppositesign");
void WriteExc(string testLabel, string type, string sourceTag, string transId, string supplier,
    string? payDate, decimal amount, string? serviceArea, string? description,
    decimal? compareAmount, string groupKey, int groupSize, bool oppositeSign) =>
    excSb.AppendLine(string.Join(",", Council, Csv(testLabel), type, Csv(sourceTag), Csv(transId),
        Csv(supplier), Csv(payDate ?? ""), Num(amount), Csv(serviceArea ?? ""), Csv(description ?? ""),
        handVerified.Contains(transId).ToString(),
        compareAmount is { } ca ? Num(ca) : "", Csv(groupKey), groupSize.ToString(CultureInfo.InvariantCulture),
        oppositeSign.ToString()));

const string T1 = "Exception test 1 - payments above the stated invoice amount";
const string T2 = "Exception test 2 - invoice amounts not covered by published payments";
const string T3 = "Exception test 3 - same supplier, amount, description and date under different transaction numbers";
const string T4 = "Exception test 4 - one transaction number covering several payees or pay dates";
const string T5 = "Exception test 5 - the same line listed more than once in the published file";

int t1n = 0, t2n = 0, t3n = 0, t4n = 0, t5n = 0;
foreach (var r in result.ScheduleA)
{
    bool paymentsExceed = r.Gross < r.ExpectedGross;
    bool oppositeSign = !paymentsExceed && r.Net != 0 && r.Gross != 0 && r.Net == -r.Gross;
    WriteExc(paymentsExceed ? T1 : T2, paymentsExceed ? "Discrepancy" : "Unreconciled",
        r.SourceTag, r.TransactionId, r.Supplier, r.PayDate, r.Gross, r.ServiceArea, r.Description,
        r.ExpectedGross, "", 1, oppositeSign);
    if (paymentsExceed) t1n++; else t2n++;
}
int g3Id = 0;
foreach (var g in result.ScheduleB)
{
    g3Id++;
    var key = $"g3-{g3Id}";
    int n = g.Members.Count;
    foreach (var m in g.Members)
    { WriteExc(T3, "Anomaly", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw, m.Net, m.ServiceArea, m.Description, null, key, n, false); t3n++; }
}
foreach (var g in result.ScheduleD)
{
    var key = $"g4-{g.SourceTag}-{g.TransactionId}";
    int n = g.Members.Count;
    foreach (var m in g.Members)
    { WriteExc(T4, "Discrepancy", m.SourceTag, m.TransactionId, m.Supplier, m.PayDateRaw, m.Gross, m.ServiceArea, m.Description, null, key, n, false); t4n++; }
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
            m.Gross, m.ServiceArea, m.Description, null, key, n, false);
        t5n++;
    }
}

var excPath = Path.Combine(outDir, "exceptions.csv");
File.WriteAllText(excPath, excSb.ToString(), new UTF8Encoding(false));
long excNorm = new FileInfo(excPath).Length;
WriteGzipSidecarAndDeletePlain(excPath);
long excGz = new FileInfo(excPath + ".gz").Length;
totalNormBytes += excNorm; totalGzBytes += excGz;
Console.WriteLine($"[exceptions] {t1n + t2n + t3n + t4n + t5n:N0} total ({t1n} T1 Discrepancy, {t2n} T2 Unreconciled, " +
    $"{t3n} T3 Anomaly, {t4n} T4 Discrepancy, {t5n} T5 Discrepancy): {excNorm:N0} bytes ({excGz:N0} gzipped)");

// ---- manifest.json: what the page fetches to know what's available to load ----
var manifestPath = Path.Combine(showroomData, "councils", "manifest.json");
var sbM = new StringBuilder();
sbM.AppendLine("{");
sbM.AppendLine("  \"councils\": [");
sbM.AppendLine("    {");
sbM.AppendLine($"      \"key\": \"{Council}\",");
sbM.AppendLine($"      \"name\": \"{SupportedCouncils.Wokingham.Name}\",");
sbM.AppendLine("      \"years\": [");
for (int i = 0; i < years.Length; i++)
{
    var y = years[i];
    var rows = perYearRows[y.Tag].Count;
    sbM.AppendLine($"        {{ \"tag\": \"{y.Tag}\", \"file\": \"{y.Tag}.norm.csv.gz\", \"rows\": {rows} }}{(i < years.Length - 1 ? "," : "")}");
}
sbM.AppendLine("      ],");
sbM.AppendLine("      \"exceptionsFile\": \"exceptions.csv.gz\",");
sbM.AppendLine($"      \"totalRows\": {allRows.Count}");
sbM.AppendLine("    }");
sbM.AppendLine("  ]");
sbM.AppendLine("}");
File.WriteAllText(manifestPath, sbM.ToString(), new UTF8Encoding(false));

Console.WriteLine($"[CouncilDbBuilder] TOTAL: {totalNormBytes:N0} bytes normalised ({totalGzBytes:N0} gzipped) across {years.Length} year files + 1 exceptions file.");
Console.WriteLine($"[CouncilDbBuilder] wrote manifest: {manifestPath}");
