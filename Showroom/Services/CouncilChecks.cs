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
// MEASURED 2026-10-03 (MonoRepo EvalApp todo/evalapp-native-apps.md, "Field report 2"): on EvalApp 2.0.0 a ForEach
// stage on a single-thread host runs as ONE blocking loop (RunAsync returns an already-completed task, zero UI frames
// serviced), so the BROWSER path does not use the pipeline: it calls the same per-file and per-group rules through
// RunSlicedAsync, which yields to the browser between slices and can be cancelled. The pipeline (Pipeline()/RunAsync)
// stays for hosts that may block (CouncilDbBuilder).
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

    /// <summary>Builds one file's facts from its rows (row id, transaction, pay date, supplier key, net). Row by row,
    /// so a caller that must not hold the thread (the page, in the browser) can feed it a slice at a time.</summary>
    public sealed class FactsBuilder
    {
        readonly string _council, _tag; readonly int _order;
        readonly (int From, int To)? _period;
        int _n, _dated, _inside; double _outsideNet;
        readonly Dictionary<int, int> _byMonth = new();
        readonly List<int> _outside = new();
        readonly Dictionary<long, KeyInfo> _keys = new();

        public FactsBuilder(string council, string tag, int order)
        { _council = council; _tag = tag; _order = order; _period = PeriodOf(tag); }

        public void Add(int id, string transactionId, string payDate, string supplierKey, decimal net)
        {
            _n++;
            _keys.TryAdd(LineKey(transactionId, payDate, supplierKey, net), new KeyInfo(id, (double)Math.Abs(net)));
            var d = AuditEngine.ParseDate(payDate);
            if (d is null) return;
            _dated++;
            int m = MonthIndex(d.Value);
            _byMonth[m] = _byMonth.GetValueOrDefault(m) + 1;
            if (_period is { } p && m >= p.From && m <= p.To) _inside++;
            else if (_period is not null) { _outsideNet += (double)Math.Abs(net); if (_outside.Count < IdCap) _outside.Add(id); }
        }

        public FileFacts Build() =>
            // a file whose name is not a period (no way to say what it should contain) is not judged: treat every dated row as inside
            new(_council, _tag, _order, _n, _dated, _period is null ? _dated : _inside, _period is null ? 0 : _outsideNet, _byMonth, _outside, _keys);
    }

    /// <summary>Builds one file's facts from its rows in one go (the build-time tool and tests; the page uses <see cref="FactsBuilder"/>).</summary>
    public static FileFacts BuildFacts(string council, string tag, int order,
        IEnumerable<(int Id, string TransactionId, string PayDate, string SupplierKey, decimal Net)> rows)
    {
        var b = new FactsBuilder(council, tag, order);
        foreach (var r in rows) b.Add(r.Id, r.TransactionId, r.PayDate, r.SupplierKey, r.Net);
        return b.Build();
    }

    /// <summary>The same check as the pipeline's per-file step, but it hands the thread back between earlier files,
    /// so the browser stays responsive. The result is identical to <see cref="ComputeFault"/>.</summary>
    public static async Task<FileFault> ComputeFaultAsync(FileFacts f, IReadOnlyList<FileFacts> files, Cooperative co)
    {
        var overlap = new List<(string, int)>();
        var seen = new HashSet<long>();
        var ids = new List<int>();
        double overlapNet = 0;
        foreach (var o in files)
        {
            if (ReferenceEquals(o, f) || o.Council != f.Council || o.Order >= f.Order) continue;
            int shared = 0;
            foreach (var (k, info) in f.LineKeys)
                if (o.LineKeys.ContainsKey(k))
                {
                    shared++;
                    if (seen.Add(k)) { overlapNet += info.Net; if (ids.Count < IdCap) ids.Add(info.Id); }
                }
            if (shared > 0) overlap.Add((o.Tag, shared));
            await co.YieldIfDueAsync();
        }
        return FinishFault(f, seen.Count, overlapNet, overlap, ids);
    }

    /// <summary>Both checks over every loaded file and group, in time slices and cancellable. The browser path.</summary>
    public static async Task<ChecksJob> RunSlicedAsync(IReadOnlyList<FileFacts> files, IReadOnlyList<GroupLines> groups,
        Cooperative co, IWorkProgress? progress = null)
    {
        var faults = new List<FileFault>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            progress?.Report($"Checking file {i + 1} of {files.Count}", (double)i / Math.Max(1, files.Count));
            faults.Add(await ComputeFaultAsync(files[i], files, co));
        }
        var corrections = new List<CorrectionFinding>();
        int examined = 0, sinceCheck = 0;
        foreach (var g in groups)
        {
            if (g.Nets.Count > MaxLines) continue;
            examined++;
            if (FindCorrection(g) is { } hit) corrections.Add(hit);
            if (++sinceCheck >= 64) { sinceCheck = 0; await co.YieldIfDueAsync(); }
        }
        return new ChecksJob(files, groups, faults, corrections, examined);
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
        return FinishFault(f, seen.Count, overlapNet, overlap, ids);
    }

    static FileFault FinishFault(FileFacts f, int seenCount, double overlapNet, List<(string, int)> overlap, List<int> ids)
    {
        var period = PeriodOf(f.Tag);
        bool single = period is { } sp && sp.From == sp.To;
        double share = f.DatedRows == 0 ? 1.0 : (double)f.InsideRows / f.DatedRows;
        KeyValuePair<int, int> peak = f.RowsByPaidMonth.Count == 0 ? default : f.RowsByPaidMonth.MaxBy(kv => kv.Value);
        return new FileFault(f.Council, f.Tag, f.Rows, f.DatedRows, f.InsideRows, share, single, f.OutsideNetAbs,
            period is { } p ? (p.From == p.To ? MonthLabel(p.From) : $"{MonthLabel(p.From)} to {MonthLabel(p.To)}") : null,
            peak.Key, peak.Value, seenCount, overlapNet, overlap.OrderByDescending(x => x.Item2).ToList(), f.OutsideIds, ids);
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