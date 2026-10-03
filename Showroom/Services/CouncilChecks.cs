using System.Globalization;
using CouncilAudit;
using EvalApp;

namespace Showroom.Services;

// =====================================================================================================
// Browser-side computed checks for the Council Spending Scanner (Session 25/26 sync).
//
// Two checks run in the visitor's own browser over what they have loaded, each by one stated neutral rule
// applied identically to every council:
//   * PUBLICATION FAULTS (per published file): (a) do the file's own pay dates fall inside the period its
//     name says, and (b) did any payment line already appear in another file of the same council.
//   * CORRECTION PAIRS (per transaction number that carries several lines): CorrectionPairs.Find, the
//     vendored engine's own public, pure rule, over each group's published Net amounts.
//
// Both run as ONE compiled EvalApp pipeline (Eval.App) rather than as imperative loops in the page: the data
// record is immutable, each check is a named step, and the page only reads the finished record. In WASM
// (single thread) the pipeline simply runs its steps in order, which is the right behaviour there. It is built
// once per page load and re-run each time the visitor loads more data.
//
// Everything below is data in, data out: no file system, no network, no HoloDb. Rows are referred to by the
// page's own row id (the id column of the in-browser `spend` table) so the page can show the source rows with
// one SELECT, and this file never has to hold a second copy of a row's text.
// =====================================================================================================

/// <summary>What one loaded file contributes to the publication-fault check. Built while the file is parsed.</summary>
public readonly record struct KeyInfo(int Id, double Net);

public sealed record FileFacts(
    string Council, string Tag, int Order, int Rows, int DatedRows, int InsideRows, double OutsideNetAbs,
    IReadOnlyDictionary<int, int> RowsByPaidMonth,        // month index (year*12 + month-1) -> rows
    IReadOnlyList<int> OutsideIds,                        // up to IdCap spend-row ids dated outside the named period
    IReadOnlyDictionary<long, KeyInfo> LineKeys);         // hashed (transaction, pay date, supplier key, net) -> first row id + net

/// <summary>Order is the file's position in its council's own file list; "already published in an EARLIER file"
/// means a lower Order, the same direction the engine's own RBWM rule drops lines in, so each repeated line is counted once.</summary>
public sealed record FileFault(
    string Council, string Tag, int Rows, int DatedRows, int InsideRows, double InsideShare, bool SingleMonth,
    double OutsideNetAbs, string? PeriodLabel, int PeakMonth, int PeakMonthRows,
    int OverlapRows, double OverlapNetAbs, IReadOnlyList<(string OtherTag, int Rows)> OverlapWith,
    IReadOnlyList<int> OutsideIds, IReadOnlyList<int> OverlapIds);

/// <summary>One group of lines under a single transaction number (a Schedule D group) for the correction-pair rule.</summary>
public sealed record GroupLines(string Council, string SourceTag, string TransactionId, string Supplier,
    IReadOnlyList<decimal> Nets);

/// <summary>Both lines of one reversal-and-correction pair. "Negative" is the line published with a minus sign.</summary>
public sealed record CorrectionFinding(string Council, string SourceTag, string TransactionId, string Supplier,
    decimal NegativeLine, decimal PositiveLine, int DigitPositionFromRight, char NegativeDigit, char PositiveDigit,
    decimal Residual, int Lines);

/// <summary>One item of the per-file ForEach: the file, plus every file (read-only) so overlap can be measured.</summary>
public sealed record FileJob(FileFacts File, IReadOnlyList<FileFacts> All, FileFault? Fault = null);

/// <summary>One item of the per-group ForEach.</summary>
public sealed record GroupJob(GroupLines Group, CorrectionFinding? Found = null);

public sealed record ChecksJob(
    IReadOnlyList<FileFacts> Files,
    IReadOnlyList<GroupLines> Groups,
    IReadOnlyList<FileFault>? Faults = null,
    IReadOnlyList<CorrectionFinding>? Corrections = null,
    int GroupsExamined = 0);

public static class CouncilChecks
{
    public const int IdCap = 200;            // source-row ids kept per fact, so the page can show them
    public const decimal MinAmount = 1000m;  // CorrectionPairs.Find: both lines at least this large
    public const int MaxLines = 8;           // CorrectionPairs.Find: only a SMALL group, in which everything else cancels

    static ICompiledPipeline<ChecksJob>? _pipeline;

    /// <summary>The compiled pipeline, built once per page load (rebuilding throws away EvalApp's tuner state).
    /// Two data-parallel ForEach stages, one item per file and one item per transaction group; each item's own
    /// step is a pure function of that item (plus, for the file check, the other files' line keys, shared and
    /// read-only), so there is no shared mutable state for the stage to protect.</summary>
    public static ICompiledPipeline<ChecksJob> Pipeline()
    {
        if (_pipeline is not null) return _pipeline;
        Eval.App("CouncilChecks")
            .DefineDomain("Checks")
                .DefineTask<ChecksJob>("RunChecks")
                    .ForEach<FileJob>(
                        select: job => job.Files.Select(f => new FileJob(f, job.Files)),
                        merge: (job, items) => job with { Faults = items.Select(i => i.Fault!).ToList() },
                        collectionName: "Files",
                        configure: sub => sub.AddStep("FileFault", (FileJob i) => i with { Fault = ComputeFault(i.File, i.All) }))
                    .ForEach<GroupJob>(
                        select: job => job.Groups.Where(g => g.Nets.Count <= MaxLines).Select(g => new GroupJob(g)),
                        merge: (job, items) => job with
                        {
                            Corrections = items.Where(i => i.Found is not null).Select(i => i.Found!).ToList(),
                            GroupsExamined = items.Count(),
                        },
                        collectionName: "Groups",
                        configure: sub => sub.AddStep("CorrectionPair", (GroupJob i) => i with { Found = FindCorrection(i.Group) }))
                .Run(out ICompiledPipeline<ChecksJob> pipeline)
            .Build();
        return _pipeline = pipeline;
    }

    public static async Task<ChecksJob> RunAsync(IReadOnlyList<FileFacts> files, IReadOnlyList<GroupLines> groups)
    {
        var result = await Pipeline().RunAsync(new ChecksJob(files, groups));
        return result switch
        {
            PipelineResult<ChecksJob>.Success s => s.Data,
            PipelineResult<ChecksJob>.Failure f => throw f.Exception,
            _ => new ChecksJob(files, groups, Array.Empty<FileFault>(), Array.Empty<CorrectionFinding>()),
        };
    }

    // ---------------------------------------------------------------- publication faults

    public static int MonthIndex(DateOnly d) => d.Year * 12 + (d.Month - 1);
    public static string MonthLabel(int m) => $"{m / 12}-{(m % 12) + 1:00}";

    /// <summary>The span of months (inclusive) a file's own name says it covers, or null if the name is not a period.
    /// Reads only the tag: FY2020-21 (Apr-Mar), 2023-24 (annual, Apr-Mar), 2020-Q1..Q4 (financial quarters; Q4 is
    /// Jan-Mar of the tag year), YYYY-MM (one month), CY2023 (calendar year), 2020-DecFeb (a named run of months).</summary>
    public static (int From, int To)? PeriodOf(string tag)
    {
        tag = tag.Trim();
        static int Ym(int y, int mo) => y * 12 + (mo - 1);
        if (tag.StartsWith("FY", StringComparison.OrdinalIgnoreCase) && tag.Length >= 9
            && int.TryParse(tag.AsSpan(2, 4), out var fy))
            return (Ym(fy, 4), Ym(fy + 1, 3));
        if (tag.StartsWith("CY", StringComparison.OrdinalIgnoreCase) && int.TryParse(tag.AsSpan(2), out var cy))
            return (Ym(cy, 1), Ym(cy, 12));
        if (tag.Length == 7 && tag[4] == '-' && int.TryParse(tag.AsSpan(0, 4), out var y1))
        {
            var rest = tag.Substring(5);
            if (int.TryParse(rest, out var n))
            {
                if (n >= 1 && n <= 12) return (Ym(y1, n), Ym(y1, n));                       // 2025-06
                if (n == (y1 + 1) % 100) return (Ym(y1, 4), Ym(y1 + 1, 3));              // 2023-24 (annual, Apr-Mar)
            }
            if (rest.Length == 2 && (rest[0] == 'Q' || rest[0] == 'q') && rest[1] >= '1' && rest[1] <= '4')
            {
                int q = rest[1] - '0';
                return q switch { 1 => (Ym(y1, 4), Ym(y1, 6)), 2 => (Ym(y1, 7), Ym(y1, 9)), 3 => (Ym(y1, 10), Ym(y1, 12)), _ => (Ym(y1, 1), Ym(y1, 3)) };
            }
        }
        if (tag.Length == 11 && tag[4] == '-' && int.TryParse(tag.AsSpan(0, 4), out var y2))
        {
            // 2020-DecFeb: the first three letters start the run, the last three end it, rolling into next year when earlier.
            int a = MonthNo(tag.Substring(5, 3)), b = MonthNo(tag.Substring(8, 3));
            if (a > 0 && b > 0) return (Ym(y2, a), Ym(b >= a ? y2 : y2 + 1, b));
        }
        return null;
    }

    static int MonthNo(string abbr) => abbr.ToLowerInvariant() switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6,
        "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, "dec" => 12, _ => 0,
    };

    /// <summary>A stable 64-bit key for "the same payment line": transaction, pay date, supplier key, net.</summary>
    public static long LineKey(string transactionId, string payDate, string supplierKey, decimal net)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            void Mix(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 0x1F; h *= 1099511628211UL; }
            Mix(transactionId); Mix(payDate); Mix(supplierKey); Mix(net.ToString("0.00", CultureInfo.InvariantCulture));
            return (long)h;
        }
    }

    /// <summary>Builds one file's facts from its rows (page row id, transaction, pay date, supplier key, net).</summary>
    public static FileFacts BuildFacts(string council, string tag, int order,
        IEnumerable<(int Id, string TransactionId, string PayDate, string SupplierKey, decimal Net)> rows)
    {
        var period = PeriodOf(tag);
        int n = 0, dated = 0, inside = 0;
        double outsideNet = 0;
        var byMonth = new Dictionary<int, int>();
        var outside = new List<int>();
        var keys = new Dictionary<long, KeyInfo>();
        foreach (var r in rows)
        {
            n++;
            keys.TryAdd(LineKey(r.TransactionId, r.PayDate, r.SupplierKey, r.Net), new KeyInfo(r.Id, (double)Math.Abs(r.Net)));
            var d = AuditEngine.ParseDate(r.PayDate);
            if (d is null) continue;
            dated++;
            int m = MonthIndex(d.Value);
            byMonth[m] = byMonth.GetValueOrDefault(m) + 1;
            if (period is { } p && m >= p.From && m <= p.To) inside++;
            else if (period is not null) { outsideNet += (double)Math.Abs(r.Net); if (outside.Count < IdCap) outside.Add(r.Id); }
        }
        // a file whose name is not a period (no way to say what it should contain) is not judged: treat every dated row as inside
        return new FileFacts(council, tag, order, n, dated, period is null ? dated : inside, period is null ? 0 : outsideNet, byMonth, outside, keys);
    }

    static FileFault ComputeFault(FileFacts f, IReadOnlyList<FileFacts> files)
    {
        var overlap = new List<(string, int)>();
        var seen = new HashSet<long>();
        var ids = new List<int>();
        double overlapNet = 0;
        foreach (var o in files)
        {
            // only EARLIER files of the same council: a line is a repeat when it was already published before this file
            if (ReferenceEquals(o, f) || o.Council != f.Council || o.Order >= f.Order) continue;
            int shared = 0;
            foreach (var (k, info) in f.LineKeys)
                if (o.LineKeys.ContainsKey(k))
                {
                    shared++;
                    if (seen.Add(k)) { overlapNet += info.Net; if (ids.Count < IdCap) ids.Add(info.Id); }
                }
            if (shared > 0) overlap.Add((o.Tag, shared));
        }
        var period = PeriodOf(f.Tag);
        bool single = period is { } sp && sp.From == sp.To;
        double share = f.DatedRows == 0 ? 1.0 : (double)f.InsideRows / f.DatedRows;
        KeyValuePair<int, int> peak = f.RowsByPaidMonth.Count == 0 ? default : f.RowsByPaidMonth.MaxBy(kv => kv.Value);
        return new FileFault(f.Council, f.Tag, f.Rows, f.DatedRows, f.InsideRows, share, single, f.OutsideNetAbs,
            period is { } p ? (p.From == p.To ? MonthLabel(p.From) : $"{MonthLabel(p.From)} to {MonthLabel(p.To)}") : null,
            peak.Key, peak.Value, seen.Count, overlapNet, overlap.OrderByDescending(x => x.Item2).ToList(), f.OutsideIds, ids);
    }

    // ---------------------------------------------------------------- correction pairs

    static CorrectionFinding? FindCorrection(GroupLines g)
    {
        if (g.Nets.Count > MaxLines) return null;
        if (CorrectionPairs.Find(g.Nets, MinAmount, MaxLines) is not { } hit) return null;
        // the rule returns the pair in published order; present it as (negative line, positive line) so it reads the same every time
        decimal neg = Math.Min(hit.reversed, hit.replacement), pos = Math.Max(hit.reversed, hit.replacement);
        var slip = CorrectionPairs.SingleDigitSlip(neg, pos) ?? hit.slip;
        return new CorrectionFinding(g.Council, g.SourceTag, g.TransactionId, g.Supplier,
            neg, pos, slip.PositionFromRight, slip.From, slip.To, hit.residual, g.Nets.Count);
    }
}