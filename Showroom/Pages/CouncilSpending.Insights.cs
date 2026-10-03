using System.Globalization;
using System.Text;
using CouncilAudit;
using Microsoft.JSInterop;
using Showroom.Services;

namespace Showroom.Pages;

// =====================================================================================================
// Council Spending Scanner: the Session 25/26 checks (code-behind half of CouncilSpending.razor).
//
// Everything here is a plain reading of two kinds of data, never a judgement:
//   * files built offline by CouncilDbBuilder from the engine's own outputs (cross/*.csv.gz), fetched once and
//     only sorted/filtered here, and
//   * the exceptions and rows the visitor has loaded, run through the vendored engine's own rules in the
//     browser (Services/CouncilChecks.cs, one EvalApp pipeline).
// Each section states its rule in words on the page; the same rule runs for every council.
// =====================================================================================================
public partial class CouncilSpending
{
    // ------------------------------------------------------------------ hosted councils (data-driven)

    /// <summary>One council whose files are hosted beside the page. Everything council-specific the page shows is here.</summary>
    sealed class Hosted
    {
        public string Key = "";
        public SupportedCouncil Profile = null!;
        public string Short = "";
        public string PickTail = "";
        /// <summary>"6 financial years hosted here, April 2020 to March 2026." -- counts and span read from the periods, never typed.</summary>
        public string PickBlurb => $"{Periods.Count} {PeriodNoun}{(Periods.Count == 1 ? "" : "s")} hosted here, {Span}. {PickTail}";
        public string Span
        {
            get
            {
                var spans = Periods.Select(p => CouncilChecks.PeriodOf(p.Tag)).Where(x => x is not null).Select(x => x!.Value).ToList();
                if (spans.Count == 0) return "";
                static string Name(int m) => new DateTime(m / 12, m % 12 + 1, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                return $"{Name(spans.Min(x => x.From))} to {Name(spans.Max(x => x.To))}";
            }
        }
        public string RetrievedOn = "";
        public string PeriodNoun = "period";
        public bool HasScheduleA;
        public string? FoiEmail;
        public string FoiNote = "";
        public List<YearPick> Periods = new();
        public bool Grouped => Periods.Count > 8;
    }

    static string UrlFileName(string url)
    {
        try { return Uri.UnescapeDataString(new Uri(url).Segments[^1]); } catch { return url; }
    }

    static List<Hosted> BuildHosted()
    {
        var w = SupportedCouncils.Wokingham; var r = SupportedCouncils.Reading;
        var wb = SupportedCouncils.WestBerkshire; var rb = SupportedCouncils.Rbwm;
        return new()
        {
            new Hosted
            {
                Key = "wokingham", Profile = w, Short = "Wokingham", RetrievedOn = "2 October 2026", PeriodNoun = "financial year", HasScheduleA = true,
                PickTail = "Ready to scan — no download hunting required.",
                FoiEmail = "informationrequests@wokingham.gov.uk",
                // Verified by hand against the council's own page, checked 3 October 2026:
                // https://www.wokingham.gov.uk/foi/receiving-foi-requests-email
                FoiNote = "checked 3 October 2026 against the council's own FOI contact page",
                Periods = new()
                {
                    new() { Tag = "FY2020-21", FileName = "wokingham-2020-21.csv", Format = SourceFormat.Csv },
                    new() { Tag = "FY2021-22", FileName = "wokingham-2021-22.csv", Format = SourceFormat.Csv },
                    new() { Tag = "FY2022-23", FileName = "wokingham-2022-23.csv", Format = SourceFormat.Csv },
                    new() { Tag = "FY2023-24", FileName = "wokingham-2023-24.xlsx", Format = SourceFormat.Xlsx },
                    new() { Tag = "FY2024-25", FileName = "wokingham-2024-25.csv", Format = SourceFormat.Csv },
                    new() { Tag = "FY2025-26", FileName = "wokingham-2025-26.csv", Format = SourceFormat.Csv },
                },
            },
            new Hosted
            {
                Key = "reading", Profile = r, Short = "Reading", RetrievedOn = "3 October 2026", PeriodNoun = "period",
                PickTail = "The full back-catalogue. Reading publishes one signed Amount column with " +
                           "no separate invoice/payment split, so the payment-vs-invoice test doesn't apply — repeated-payment and multi-payee checks still run.",
                FoiEmail = "FOI.CRT@reading.gov.uk",
                // Verified by hand against the council's own page, checked 3 October 2026:
                // https://www.reading.gov.uk/contact-us/freedom-of-information-foi/freedom-of-information-act-procedure-for-dealing-with-requests/
                FoiNote = "checked 3 October 2026 against the council's own FOI contact page",
                // Driven off the vendored SupportedCouncils list so the page can't drift from CouncilDbBuilder's own file list: 63 periods
                // (2020-21 is 4 quarters, every later period a month). 2021-05 is excluded -- council-side export fault.
                Periods = r.Years.Where(y => y.Tag != "2021-05")
                    .Select(y => new YearPick { Tag = y.Tag, FileName = $"reading-{y.Tag}.{(y.Format == SourceFormat.Xlsx ? "xlsx" : "csv")}", Format = y.Format, Url = y.Url })
                    .ToList(),
            },
            new Hosted
            {
                Key = "westberkshire", Profile = wb, Short = "West Berkshire", RetrievedOn = "3 October 2026", PeriodNoun = "month",
                PickTail = "West Berkshire publishes no transaction or invoice reference at all, only one amount per row, so " +
                           "the invoice and shared-reference tests cannot run — repeated-payment and standing-payment checks do.",
                FoiEmail = wb.FoiContactEmail,
                // Profile note: verified via https://www.westberks.gov.uk/article/40935/Contact-details-for-Freedom-of-Information-Requests, checked 2026-10-03.
                FoiNote = "checked 3 October 2026 against the council's own FOI contact page",
                Periods = wb.Years.Select(y => new YearPick { Tag = y.Tag, FileName = UrlFileName(y.Url), Format = y.Format, Url = y.Url }).ToList(),
            },
            new Hosted
            {
                Key = "rbwm", Profile = rb, Short = "RBWM", RetrievedOn = "3 October 2026", PeriodNoun = "file",
                PickTail = "The Royal Borough publishes every payment of £100 or more, with one amount per row (no invoice/VAT split). " +
                           "Lines already published in an earlier file were removed before checking, so nothing is counted twice.",
                FoiEmail = null, // not verified (profile: FoiContactEmail null) -> the letter leaves a placeholder
                FoiNote = "",
                Periods = rb.Years.Select(y => new YearPick { Tag = y.Tag, FileName = UrlFileName(y.Url), Format = y.Format, Url = y.Url }).ToList(),
            },
        };
    }

    /// <summary>The financial year (April to March) a file's name places it in, for grouping the period picker.</summary>
    static string FinancialYearOf(string tag)
    {
        if (CouncilChecks.PeriodOf(tag) is not { } p) return "Other";
        int year = p.From / 12, month = p.From % 12 + 1;
        int fy = month >= 4 ? year : year - 1;
        return $"FY{fy}-{(fy + 1) % 100:00}";
    }

    List<IGrouping<string, YearPick>> PeriodGroups(Hosted h) =>
        h.Periods.GroupBy(p => FinancialYearOf(p.Tag)).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

    bool GroupAllTicked(IEnumerable<YearPick> g) { var open = g.Where(p => !p.Loaded).ToList(); return open.Count > 0 && open.All(p => p.Checked); }
    void TickGroup(IEnumerable<YearPick> g, bool on) { foreach (var p in g.Where(p => !p.Loaded)) p.Checked = on; }
    void TickAll(Hosted h, bool on) => TickGroup(h.Periods, on);
    int TickedCount(Hosted h) => h.Periods.Count(p => p.Checked && !p.Loaded);

    // The six councils the files cover, keyed by the slug the export files use; names and links come straight from the vendored profiles.
    static readonly Dictionary<string, SupportedCouncil> Profiles = new()
    {
        ["wokingham"] = SupportedCouncils.Wokingham, ["merton"] = SupportedCouncils.Merton, ["reading"] = SupportedCouncils.Reading,
        ["bracknellforest"] = SupportedCouncils.BracknellForest, ["westberkshire"] = SupportedCouncils.WestBerkshire, ["rbwm"] = SupportedCouncils.Rbwm,
    };
    static readonly Dictionary<string, string> CouncilNames = Profiles.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

    /// <summary>The councils a section currently covers, with their transparency pages (for the licence credit line).</summary>
    List<SupportedCouncil> ScopeProfiles() => Profiles.Where(kv => InScope(kv.Key)).Select(kv => kv.Value).ToList();

    static string CouncilName(string slug) => CouncilNames.GetValueOrDefault(slug, slug);

    // ------------------------------------------------------------------ test labels (shared with the page's own tables)

    const string LabelT1 = "Exception test 1 - payments above the stated invoice amount";
    const string LabelT2 = "Exception test 2 - invoice amounts not covered by published payments";
    const string LabelT3 = "Exception test 3 - same supplier, amount, description and date under different transaction numbers";
    const string LabelT4 = "Exception test 4 - one transaction number covering several payees or pay dates";
    const string LabelT5 = "Exception test 5 - the same line listed more than once in the published file";
    const string LabelTR = "Exception test R - credit/refund rows vs. a matching same-supplier charge";

    static bool IsStanding(string classification) => classification.StartsWith("StandingSchedule", StringComparison.Ordinal);

    // ------------------------------------------------------------------ cross-council files (fetched once)

    sealed record FlowLine(string PayerSlug, string Entity, string Verdict, string SupplierKey, string SupplierName,
        int Rows, decimal Net, string Years, bool InOldExactPass)
    { public ExceptionItem? Item; public bool Open; }

    sealed record AliasGrade(string Entity, string PayerSlug, string SupplierKey, string SupplierName, double Score, string Verdict, string Note);

    sealed record LoanLine(string Council, string Year, string TransactionId, string Counterparty, string PayDate, decimal Amount, decimal Net,
        string Class, string Status, string Kind, decimal? Principal, int? RateBp, int? Days, int Candidates, string Note)
    { public ExceptionItem? Item; }

    sealed record SinkLine(string Payer, string Entity, int FirstYear, int LastYear, int YearsActive, int Rows, decimal NetOut, int CreditRows,
        decimal CreditNet, decimal SettleRatio, int RisingStreak, int DebtLabelledRows, decimal DebtLabelledNet, string YearSeries, string Flags)
    { public ExceptionItem? Item; }

    sealed record MisfitLine(string Payer, string Entity, string Month, decimal MonthNet, decimal MedianMonthNet, double Ratio, int Rows,
        string LabelCheck, string TopDescription)
    { public ExceptionItem? Item; }

    sealed record FileDupLine(string Council, string File, int Rows, int Repeats, decimal RepeatNet, double Rate, double NeighbourRate, bool Flagged)
    { public ExceptionItem? Item; }

    sealed record WithinLine(string Council, string Year, string TransactionId, string Supplier, string PayDate, decimal Net, string Description,
        int Copies, decimal ExtraValue, int DistinctLines, bool EveryLineRepeated, int OtherTransactions, string Pattern)
    { public ExceptionItem? Item; }

    sealed record FlowSrcRow(string Council, string Year, string TransactionId, string PayDate, decimal Net, string Supplier, string Description);

    bool _crossLoading, _crossLoaded;
    string? _crossError;
    bool _showAllSix;
    readonly HashSet<string> _openedCouncils = new();
    List<FlowLine> _flows = new();
    List<AliasGrade> _grades = new();
    List<LoanLine> _loans = new();
    List<SinkLine> _sinks = new();
    List<MisfitLine> _misfits = new();
    List<FileDupLine> _fileDups = new();
    List<WithinLine> _withins = new();
    Dictionary<(string, string), List<FlowSrcRow>>? _flowRows;
    bool _flowRowsLoading;

    // "show more" limits per section (rows shown), and open/closed state per section card
    readonly Dictionary<string, int> _limits = new();
    readonly Dictionary<string, bool> _open = new();
    const int FirstRows = 20, MoreRows = 50;
    int Limit(string id) => _limits.GetValueOrDefault(id, FirstRows);
    void More(string id) => _limits[id] = Limit(id) + MoreRows;
    bool IsOpen(string id) => _open.GetValueOrDefault(id, false);
    void Toggle(string id) => _open[id] = !IsOpen(id);
    readonly Dictionary<string, string> _filters = new();
    string Filter(string id) => _filters.GetValueOrDefault(id, "");
    void SetFilter(string id, string? v) { _filters[id] = v ?? ""; _limits.Remove(id); }

    static Dictionary<string, int> HeaderIndex(string[] header)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++) d[header[i].Trim().TrimStart('\uFEFF')] = i;
        return d;
    }
    static decimal Dec(string[] f, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < f.Length && decimal.TryParse(f[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    static decimal? DecN(string[] f, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < f.Length && decimal.TryParse(f[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    static int Int(string[] f, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < f.Length && int.TryParse(f[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    static double Dbl(string[] f, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < f.Length && double.TryParse(f[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
    static string Str(string[] f, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < f.Length ? f[i] : "";

    async Task<List<string[]>> FetchCsvAsync(string name, Cooperative? co = null)
    {
        co ??= new Cooperative(CancellationToken.None) { Cancellable = false };
        var bytes = await FetchGunzipAsync($"data/councils/cross/{name}.csv.gz", co);
        return await SlicedCsv.ParseAsync(bytes, co);   // written by CouncilDbBuilder as UTF-8, so the sliced reader applies
    }

    /// <summary>Fetches the seven small cross-council files (about 30 KB gzipped in all) once per page load.</summary>
    async Task EnsureCrossAsync()
    {
        if (_crossLoaded || _crossLoading) return;
        _crossLoading = true; _crossError = null; StateHasChanged();
        try
        {
            var co = new Cooperative(CancellationToken.None) { Cancellable = false };
            var flows = await FetchCsvAsync("flows", co); var h = HeaderIndex(flows[0]);
            _flows = flows.Skip(1).Where(f => f.Length >= 8).Select(f => new FlowLine(Str(f, h, "Payer"), Str(f, h, "Entity"), Str(f, h, "Verdict"),
                Str(f, h, "SupplierKey"), Str(f, h, "SupplierName"), Int(f, h, "Rows"), Dec(f, h, "Net"), Str(f, h, "Years"),
                Str(f, h, "InOldExactPass") == "True")).ToList();

            var grades = await FetchCsvAsync("alias-grades", co); h = HeaderIndex(grades[0]);
            _grades = grades.Skip(1).Where(f => f.Length >= 6).Select(f => new AliasGrade(Str(f, h, "Entity"), Str(f, h, "PayerSlug"), Str(f, h, "SupplierKey"),
                Str(f, h, "SupplierName"), Dbl(f, h, "NearestScore"), Str(f, h, "Verdict"), Str(f, h, "Note"))).ToList();

            var loans = await FetchCsvAsync("debt-ledger", co); h = HeaderIndex(loans[0]);
            _loans = loans.Skip(1).Where(f => f.Length >= 15 && Str(f, h, "Class") == "InterAuthority").Select(f => new LoanLine(Str(f, h, "Council"), Str(f, h, "Year"),
                Str(f, h, "TransactionId"), Str(f, h, "Counterparty"), Str(f, h, "PayDate"), Dec(f, h, "Amount"), Dec(f, h, "Net"), Str(f, h, "Class"),
                Str(f, h, "DecodeStatus"), Str(f, h, "DecodeKind"), DecN(f, h, "DecodedPrincipal"), DecN(f, h, "DecodedRateBp") is { } bp ? (int)bp : null,
                DecN(f, h, "DecodedDays") is { } dd ? (int)dd : null, Int(f, h, "DecodeCandidates"), Str(f, h, "DecodeNote"))).ToList();

            var sink = await FetchCsvAsync("debt-sink", co); h = HeaderIndex(sink[0]);
            _sinks = sink.Skip(1).Where(f => f.Length >= 14).Select(f => new SinkLine(Str(f, h, "Payer"), Str(f, h, "Entity"), Int(f, h, "FirstYear"), Int(f, h, "LastYear"),
                Int(f, h, "YearsActive"), Int(f, h, "Rows"), Dec(f, h, "NetOut"), Int(f, h, "CreditRows"), Dec(f, h, "CreditNet"), Dec(f, h, "SettleRatio"),
                Int(f, h, "RisingStreakYears"), Int(f, h, "DebtLabelledRows"), Dec(f, h, "DebtLabelledNet"), Str(f, h, "YearSeries"), Str(f, h, "Flags"))).ToList();

            var mis = await FetchCsvAsync("misfits", co); h = HeaderIndex(mis[0]);
            _misfits = mis.Skip(1).Where(f => f.Length >= 8).Select(f => new MisfitLine(Str(f, h, "Payer"), Str(f, h, "Entity"), Str(f, h, "Month"), Dec(f, h, "MonthNet"),
                Dec(f, h, "MedianMonthNet"), Dbl(f, h, "Ratio"), Int(f, h, "Rows"), Str(f, h, "LabelCheck"), Str(f, h, "TopDescription"))).ToList();

            var dup = await FetchCsvAsync("file-duplication", co); h = HeaderIndex(dup[0]);
            _fileDups = dup.Skip(1).Where(f => f.Length >= 8).Select(f => new FileDupLine(Str(f, h, "Council"), Str(f, h, "File"), Int(f, h, "Rows"), Int(f, h, "ExactRepeatRows"),
                Dec(f, h, "RepeatNetAbs"), Dbl(f, h, "Rate"), Dbl(f, h, "NeighbourMedianRate"), Str(f, h, "Flagged") == "True")).ToList();

            var wi = await FetchCsvAsync("within-txn", co); h = HeaderIndex(wi[0]);
            _withins = wi.Skip(1).Where(f => f.Length >= 12).Select(f => new WithinLine(Str(f, h, "Council"), Str(f, h, "Year"), Str(f, h, "TransactionId"), Str(f, h, "SupplierName"),
                Str(f, h, "PayDate"), Dec(f, h, "Net"), Str(f, h, "Description"), Int(f, h, "Copies"), Dec(f, h, "ExtraValue"), Int(f, h, "DistinctLinesInTxn"),
                Str(f, h, "EveryDistinctLineRepeatedSameK") == "True", Int(f, h, "OtherTransactionsSameSupplierAndNet"),
                // the export's own last column ("Reading") says whether the same supplier+amount turns up in other transactions
                Str(f, h, "Reading"))).OrderByDescending(x => x.ExtraValue).ToList();

            await BuildCrossItemsAsync(co);
            _crossLoaded = true;
        }
        catch (Exception ex) { _crossError = "Couldn't load the cross-council files: " + ex.Message; }
        finally { _crossLoading = false; StateHasChanged(); }
    }

    /// <summary>The published payment lines behind each flow row (fetched the first time a visitor asks to see them).</summary>
    async Task EnsureFlowRowsAsync()
    {
        if (_flowRows is not null || _flowRowsLoading) return;
        _flowRowsLoading = true; StateHasChanged();
        try
        {
            var t = await FetchCsvAsync("flow-rows"); var h = HeaderIndex(t[0]);
            var d = new Dictionary<(string, string), List<FlowSrcRow>>();
            foreach (var f in t.Skip(1))
            {
                if (f.Length < 8) continue;
                var key = (Str(f, h, "council"), Str(f, h, "supplierkey"));
                if (!d.TryGetValue(key, out var list)) d[key] = list = new();
                list.Add(new FlowSrcRow(key.Item1, Str(f, h, "year"), Str(f, h, "transid"), Str(f, h, "paydate"), Dec(f, h, "net"), Str(f, h, "supplier"), Str(f, h, "description")));
            }
            _flowRows = d;
        }
        catch (Exception ex) { _crossError = "Couldn't load the source rows: " + ex.Message; }
        finally { _flowRowsLoading = false; StateHasChanged(); }
    }

    async Task ToggleFlowRows(FlowLine f)
    {
        f.Open = !f.Open;
        if (f.Open) await EnsureFlowRowsAsync();
    }

    List<FlowSrcRow> FlowSource(FlowLine f) =>
        _flowRows is not null && _flowRows.TryGetValue((f.PayerSlug, f.SupplierKey), out var l) ? l : new();

    // ------------------------------------------------------------------ scope: which councils the cross-council sections cover

    // Default: the councils the visitor has opened this session. A switch widens it to all six councils the files cover.
    bool InScope(string slug) => _showAllSix || _openedCouncils.Contains(slug);
    string ScopeLine() => _showAllSix
        ? "all six councils the files cover"
        : string.Join(", ", _openedCouncils.OrderBy(x => x).Select(CouncilName));

    // ------------------------------------------------------------------ FOI addressee (one council per letter)

    string _foiTo = "";

    List<string> SelectedCouncilNames() =>
        _selected.Values.Select(i => i.Council).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    string FoiAddressee
    {
        get
        {
            if (_mode == Mode.OwnFile) return _councilDisplayName;
            var names = SelectedCouncilNames();
            if (_foiTo != "" && names.Contains(_foiTo)) return _foiTo;
            return names.FirstOrDefault() ?? _councilDisplayName;
        }
    }

    /// <summary>The verified FOI address for a council, or "" (the letter then leaves a placeholder rather than guess).</summary>
    string FoiEmailFor(string name)
    {
        var h = _hosted.FirstOrDefault(x => x.Profile.Name == name);
        if (h is not null) return h.FoiEmail ?? "";
        return SupportedCouncils.All.FirstOrDefault(c => c.Name == name)?.FoiContactEmail ?? "";
    }

    string FoiNoteFor(string name) =>
        _hosted.FirstOrDefault(x => x.Profile.Name == name)?.FoiNote is { Length: > 0 } n
            ? n : "as recorded in the tool's council profile";

    // Redacted rows (a payee or reference replaced by a placeholder such as REDACTED PERSONAL DATA) are excluded by the engine from
    // every exception test; the page counts them per council as the files load, so the exclusion is shown, not buried.
    // Same rule as AuditEngine's own (it is private there): contains "REDACT", or the split form RED...ACTED.
    static bool IsRedactedText(string s) => Redaction.IsRedacted(s);

    readonly Dictionary<string, (int Rows, int Redacted)> _redaction = new();

    // ------------------------------------------------------------------ FOI items for the new sections

    /// <summary>A selectable row in one of the new sections: it joins the same tray and FOI letter as the exception tests do.</summary>
    readonly Dictionary<string, ExceptionItem> _itemCache = new();

    ExceptionItem NewItem(string section, string sectionTitle, string sectionDefinition, ExceptionType type, string councilName, string sourceTag,
        string idPart, string supplier, string? payDate, decimal amount, string sentence, IEnumerable<string> lines, string request)
    {
        var item = new ExceptionItem
        {
            Custom = true, SectionId = section, SectionTitle = sectionTitle, SectionDefinition = sectionDefinition,
            TestLabel = section, Type = type, Council = councilName, SourceTag = sourceTag, TransactionId = idPart,
            Supplier = supplier, PayDate = payDate, Amount = amount, Sentence = sentence, Lines = lines.ToList(), Request = request,
        };
        // A row that is rebuilt (more files loaded) keeps its identity, so a ticked box stays ticked and the tray's own entry stays valid.
        if (_itemCache.TryGetValue(item.Key, out var old))
        {
            old.Amount = item.Amount; old.Sentence = item.Sentence; old.Lines = item.Lines; old.Request = item.Request; old.Supplier = item.Supplier;
            return old;
        }
        return _itemCache[item.Key] = item;
    }

    const string SecFlows = "flows", SecLoans = "loans", SecCorr = "corrections", SecPub = "pubfaults", SecDup = "filedup",
        SecWithin = "withintxn", SecSink = "sink", SecMisfit = "misfit", SecStanding = "standing";

    // In time slices: ~700 rows each get a sentence built from their values.
    async Task BuildCrossItemsAsync(Cooperative co)
    {
        foreach (var f in _flows.Where(f => f.Verdict == "accept"))
        {
            var payer = CouncilName(f.PayerSlug);
            f.Item = NewItem(SecFlows, "Payments between councils and their companies",
                "payments the published file records from this council to another public body or company, shown under one of that body's spellings",
                ExceptionType.Anomaly, payer, f.Years, $"flow:{f.PayerSlug}:{f.SupplierKey}", f.SupplierName, null, f.Net,
                $"{payer}'s published spending files record {f.Rows:N0} payment{(f.Rows == 1 ? "" : "s")} totalling {Money(f.Net)} to a payee published as \"{f.SupplierName}\" ({f.Entity}), in {f.Years.Replace(" ", ", ")}.",
                new[] { $"{payer}, payee \"{f.SupplierName}\", {f.Rows:N0} payments, {Money(f.Net)}, files {f.Years.Replace(" ", ", ")}" },
                "a copy of the payment records for these payments, the agreement or invoice basis for them, and the supplier record(s) under which this payee is held.");
            await co.YieldIfDueAsync();
        }
        foreach (var l in _loans.Where(l => l.Status != "NotApplicable"))
        {
            var cn = l.Council;
            string result = l.Status == "DoesNotDecode" ? "No principal, quoted rate and standard number of days were found that reproduce this figure."
                : l.Status == "DecodesPrincipalUnpublished" ? "A principal, rate and number of days that reproduce this figure were found, but the principal is not published in the file."
                : "A published round principal and a rate and number of days reproduce the interest figure.";
            l.Item = NewItem(SecLoans, "Loan interest between councils",
                "payments to another council that the published file labels as debt or interest, checked against a round loan, a rate and a number of days",
                ExceptionType.Anomaly, cn, l.Year, l.TransactionId + "|" + l.Counterparty, l.Counterparty, l.PayDate, l.Amount,
                $"Transaction {l.TransactionId} ({l.Year}) records a payment of {Money(l.Amount)} to {l.Counterparty} on {l.PayDate}, labelled in the published file as debt or interest. {result}",
                new[] { $"{cn}, {l.Year}, transaction {l.TransactionId}, {l.PayDate}, {Money(l.Amount)}, {l.Counterparty}" },
                "the loan agreement or deal record for this payment (principal, rate, start and end dates) and the calculation of the amount paid.");
            await co.YieldIfDueAsync();
        }
        foreach (var s in _sinks)
        {
            var payer = CouncilName(s.Payer);
            s.Item = NewItem(SecSink, "Money paid to another council or company over several years",
                "totals the paying council's own published file records to one payee over several years, with whatever flows back in the same file",
                ExceptionType.Anomaly, payer, $"{s.FirstYear}-{s.LastYear}", $"sink:{s.Payer}:{s.Entity}", s.Entity, null, s.NetOut,
                $"{payer}'s published files record {s.Rows:N0} payments to {s.Entity} totalling {Money(s.NetOut)} between {s.FirstYear} and {s.LastYear}, with {s.CreditRows:N0} credit row{(s.CreditRows == 1 ? "" : "s")} totalling {Money(Math.Abs(s.CreditNet))} in the same files.",
                new[] { $"{payer}, payee {s.Entity}, {s.FirstYear} to {s.LastYear}, yearly totals {s.YearSeries}" },
                "the agreement(s) under which these payments were made and the council's own record of amounts repaid or returned.");
            await co.YieldIfDueAsync();
        }
        foreach (var m in _misfits)
        {
            var payer = CouncilName(m.Payer);
            m.Item = NewItem(SecMisfit, "Months that differ from the usual month",
                "months in which a council's published payments to one payee are far above that payee's own usual month",
                ExceptionType.Anomaly, payer, m.Month, $"misfit:{m.Payer}:{m.Entity}:{m.Month}", m.Entity, m.Month, m.MonthNet,
                $"In {m.Month}, {payer}'s published files record {Money(m.MonthNet)} paid to {m.Entity} across {m.Rows:N0} row{(m.Rows == 1 ? "" : "s")}, against a usual month of {Money(m.MedianMonthNet)} ({m.Ratio:0.#} times). The largest row is labelled \"{m.TopDescription.Trim()}\".",
                new[] { $"{payer}, payee {m.Entity}, {m.Month}, {Money(m.MonthNet)} over {m.Rows:N0} rows" },
                "the records for the payments in that month and the reason the month's total differs from the usual month.");
            await co.YieldIfDueAsync();
        }
        foreach (var d in _fileDups.Where(d => d.Flagged))
        {
            var cn = CouncilName(d.Council);
            d.Item = NewItem(SecDup, "Files that list the same row twice",
                "a published file in which many rows appear twice with every published column identical",
                ExceptionType.Discrepancy, cn, d.File, $"filedup:{d.Council}:{d.File}", d.File, null, d.RepeatNet,
                $"The file {cn} published for {d.File} lists {d.Repeats:N0} of its {d.Rows:N0} rows a second time, with every published column identical ({Money(d.RepeatNet)} of Net).",
                new[] { $"{cn}, file {d.File}, {d.Repeats:N0} repeated rows of {d.Rows:N0}, {Money(d.RepeatNet)}" },
                "the source records for these rows and the reason they appear twice in the published file.");
            await co.YieldIfDueAsync();
        }
        foreach (var w in _withins)
        {
            var cn = CouncilName(w.Council);
            var date = PayDateText(w.PayDate);
            w.Item = NewItem(SecWithin, "The same line repeated inside one transaction",
                "a transaction number whose published lines include the same supplier, amount, description and pay date more than once",
                ExceptionType.Anomaly, cn, w.Year, $"{w.TransactionId}|{w.Supplier}|{w.Net}|{w.PayDate}", w.Supplier, date, w.ExtraValue,
                $"Transaction {w.TransactionId} ({w.Year}) lists the same line {w.Copies:N0} times: {w.Supplier}, {Money(w.Net)}, \"{w.Description.Trim()}\", paid {date}.",
                new[] { $"{cn}, {w.Year}, transaction {w.TransactionId}, {date}, {Money(w.Net)} x {w.Copies:N0}, {w.Supplier}" },
                "the invoice(s) and payment record(s) for this transaction and the reason the line appears " + w.Copies.ToString("N0") + " times.");
            await co.YieldIfDueAsync();
        }
    }

    /// <summary>The page's own date text: a raw published pay date (text, or an Excel serial number) as dd/MM/yyyy.</summary>
    static string PayDateText(string raw)
    {
        var d = AuditEngine.ParseDate(raw);
        return d is { } x ? x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : (string.IsNullOrWhiteSpace(raw) ? "no pay date published" : raw);
    }

    // ------------------------------------------------------------------ sections: scoped, sorted views (no judgement, just a rule + a sort)

    List<FlowLine> FlowsScoped(string verdict) => _flows.Where(f => f.Verdict == verdict && InScope(f.PayerSlug))
        .OrderByDescending(f => f.Net).ToList();

    /// <summary>Accepted flows grouped by the body they are paid to, largest first; each group lists its spellings (the point of the table).</summary>
    List<(string Entity, decimal Net, int Rows, int Spellings, int Payers, List<FlowLine> Lines)> FlowEntities() =>
        FlowsScoped("accept").GroupBy(f => f.Entity)
            .Select(g => (g.Key, g.Sum(x => x.Net), g.Sum(x => x.Rows), g.Select(x => x.SupplierName.ToLowerInvariant()).Distinct().Count(),
                g.Select(x => x.PayerSlug).Distinct().Count(), g.OrderByDescending(x => x.Net).ToList()))
            .OrderByDescending(x => x.Item2).ToList();

    string SourceRowsCsv(FlowLine f)
    {
        var sb = new StringBuilder("Council,Year,TransactionId,PayDate,Net,Supplier,Description\n");
        foreach (var r in FlowSource(f))
            sb.Append(string.Join(",", Csv(CouncilName(r.Council)), Csv(r.Year), Csv(r.TransactionId), Csv(PayDateText(r.PayDate)), r.Net.ToString(CultureInfo.InvariantCulture), Csv(r.Supplier), Csv(r.Description))).Append('\n');
        return sb.ToString();
    }

    async Task DownloadFlowRows(FlowLine f) =>
        await JS.InvokeVoidAsync("analystDownload", SafeFileName($"{f.PayerSlug}-{f.SupplierKey}") + ".csv", SourceRowsCsv(f), "text/csv");

    List<LoanLine> LoansScoped() => _loans.Where(l => InScope(SlugOfName(l.Council))).OrderByDescending(l => l.Amount).ToList();

    static string SlugOfName(string name) => CouncilNames.FirstOrDefault(kv => kv.Value == name).Key ?? name;

    List<SinkLine> SinksScoped() => _sinks.Where(s => InScope(s.Payer)).OrderByDescending(s => s.NetOut).ToList();
    List<MisfitLine> MisfitsScoped() => _misfits.Where(m => InScope(m.Payer)).OrderByDescending(m => m.MonthNet).ToList();
    List<FileDupLine> FileDupsScoped() => _fileDups.Where(d => InScope(d.Council)).ToList();
    List<WithinLine> WithinScoped() => _withins.Where(w => InScope(w.Council)).ToList();

    static string FlagWords(string flags) => string.Join("; ", flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f =>
        f == "NoReturnFlow" ? "no credit rows at all in the file"
        : f == "ReturnUnder2pct" ? "credits are under 2% of what was paid"
        : f == "DebtLabelled" ? "some rows are labelled as loan, debt, interest or recharge"
        : f.StartsWith("RisingFullYears") ? $"paid total rose in {f["RisingFullYears".Length..]} full years running"
        : f));

    // ------------------------------------------------------------------ standing payments (view over the loaded test-3 items)

    sealed class StandingGroup
    {
        public string Council = "", Supplier = "", Years = "", Detail = "", Classification = "", PayDate = "";
        public decimal Each, Total;
        public int Payments;
        public ExceptionItem Rep = null!;
        public List<ExceptionItem> Members = new();
    }

    List<StandingGroup>? _standingCache;

    /// <summary>The standing-payment view over the loaded test-3 groups. Built once per load by <see cref="RebuildStandingAsync"/>
    /// (in slices); a render only reads it.</summary>
    List<StandingGroup> StandingGroups() => _standingCache ?? NoStanding;

    async Task RebuildStandingAsync(Cooperative co)
    {
        var list = new List<StandingGroup>();
        if (_testsByLabel.TryGetValue(LabelT3, out var t3))
        {
            int k = 0;
            // t3.Groups already holds the rows of each GroupKey; a group's rows share one classification
            foreach (var m in t3.Groups.Values)
            {
                var first = m[0];
                if (!IsStanding(first.Classification)) { if ((++k & 255) == 0) await co.YieldIfDueAsync(); continue; }
                list.Add(new StandingGroup
                {
                    Council = first.Council, Supplier = first.Supplier, Classification = first.Classification, Detail = first.ClassificationDetail ?? "",
                    Years = string.Join(", ", m.Select(x => x.SourceTag).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                    PayDate = first.PayDate ?? "", Each = first.Amount ?? 0m, Total = m.Sum(TestRowValue), Payments = m.Count, Rep = first, Members = m,
                });
                if ((++k & 255) == 0) await co.YieldIfDueAsync();
            }
        }
        _standingCache = list.OrderByDescending(g => g.Total).ToList();
    }

    // ------------------------------------------------------------------ in-browser checks: publication faults and correction pairs (EvalApp pipeline)

    readonly List<FileFacts> _facts = new();
    ChecksJob? _checks;
    string? _checksError;
    bool _checksRunning;
    List<ExceptionItem> _pubItems = new();
    List<ExceptionItem> _corrItems = new();

    /// <summary>Publication faults and correction pairs over everything loaded, in time slices (see CouncilChecks.RunSlicedAsync
    /// for why this does not go through the EvalApp pipeline in the browser). A failure shows as a plain error line.</summary>
    async Task RunChecksAsync(Cooperative co)
    {
        _checksRunning = true; _checksError = null;
        try
        {
            var groups = new List<GroupLines>();
            if (_testsByLabel.TryGetValue(LabelT4, out var t4))
                foreach (var m0 in t4.Groups.Values)
                {
                    var m = m0.Where(x => x.Net is not null).ToList();
                    if (m.Count > 0)
                        groups.Add(new GroupLines(m[0].CouncilKey, m[0].SourceTag, m[0].TransactionId, m[0].Supplier, m.Select(x => x.Net!.Value).ToList()));
                    if (groups.Count % 256 == 0) await co.YieldIfDueAsync();
                }
            _checks = await CouncilChecks.RunSlicedAsync(_facts.ToList(), groups, co, _status);
            BuildCheckItems();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _checksError = "The in-browser checks could not run: " + ex.Message; }
        finally { _checksRunning = false; }
    }

    static string DigitPlace(int fromRightInPennies) => fromRightInPennies switch
    {
        0 => "pence", 1 => "tens of pence", 2 => "pounds", 3 => "tens of pounds", 4 => "hundreds of pounds", 5 => "thousands of pounds",
        6 => "tens of thousands of pounds", 7 => "hundreds of thousands of pounds", 8 => "millions of pounds", _ => "the position " + fromRightInPennies + " from the right",
    };

    /// <summary>The files marked by the section's stated rule: a single-month file with under 9 in 10 of its dated rows in the named month,
    /// or any file with lines already published in an earlier file of the same council.</summary>
    IEnumerable<FileFault> MarkedFaults() => (_checks?.Faults ?? Array.Empty<FileFault>()).Where(IsMarked);
    static bool IsMarked(FileFault f) => (f.SingleMonth && f.DatedRows > 0 && f.InsideShare < 0.9) || f.OverlapRows > 0;
    static bool OutsideMarked(FileFault f) => f.SingleMonth && f.DatedRows > 0 && f.InsideShare < 0.9;

    void BuildCheckItems()
    {
        _pubItems = new();
        foreach (var f in MarkedFaults())
        {
            var cn = CouncilName(f.Council);
            var parts = new List<string>();
            var lines = new List<string>();
            if (OutsideMarked(f))
            {
                parts.Add($"only {f.InsideRows:N0} of its {f.DatedRows:N0} dated rows have a pay date in {f.PeriodLabel}");
                lines.Add($"{cn}, file {f.Tag}: {f.DatedRows - f.InsideRows:N0} of {f.DatedRows:N0} dated rows are paid outside {f.PeriodLabel} ({Money((decimal)f.OutsideNetAbs)} of Net)");
            }
            if (f.OverlapRows > 0)
            {
                parts.Add($"{f.OverlapRows:N0} of its lines (same transaction number, pay date, payee and net amount) were already published in an earlier file ({string.Join(", ", f.OverlapWith.Select(o => o.OtherTag))})");
                lines.Add($"{cn}, file {f.Tag}: {f.OverlapRows:N0} lines also published in {string.Join(", ", f.OverlapWith.Select(o => o.OtherTag))} ({Money((decimal)f.OverlapNetAbs)} of Net)");
            }
            var item = NewItem(SecPub, "Files whose contents do not match their name, or overlap another file",
                "a published file named for one month that mostly holds payments dated in other months, or that repeats lines already published in an earlier file",
                ExceptionType.Discrepancy, cn, f.Tag, $"pub:{f.Council}:{f.Tag}", f.Tag, null, (decimal)(OutsideMarked(f) ? f.OutsideNetAbs : 0.0) + (decimal)f.OverlapNetAbs,
                $"The file {cn} published for {f.Tag} contains " + string.Join(", and ", parts) + ".", lines,
                "the council's own record of which period this file was extracted for, and of how the lines listed above come to appear as they do in the published files.");
            _pubItems.Add(item);
        }
        _pubKeyed = _pubItems.ToDictionary(i => i.TransactionId);

        _corrItems = new();
        foreach (var c in _checks?.Corrections ?? Array.Empty<CorrectionFinding>())
        {
            var cn = CouncilName(c.Council);
            var item = NewItem(SecCorr, "Reversal-and-correction pairs",
                "a transaction whose lines include one amount reversed and another posted that differ in a single digit",
                ExceptionType.Discrepancy, cn, c.SourceTag, c.TransactionId, c.Supplier, null, c.Residual,
                $"Transaction {c.TransactionId} ({c.SourceTag}) lists {c.Lines} lines for {c.Supplier}, including {Money(c.NegativeLine)} and {Money(c.PositiveLine)}, which differ in one digit ({DigitPlace(c.DigitPositionFromRight)}: {c.NegativeDigit} and {c.PositiveDigit}); the lines together leave {Money(c.Residual)}.",
                new[] { $"{cn}, {c.SourceTag}, transaction {c.TransactionId}, {c.Supplier}, {Money(c.NegativeLine)} and {Money(c.PositiveLine)}, residual {Money(c.Residual)}" },
                "the booking records for each line of this transaction and the reason for the reversal and the re-posting.");
            _corrItems.Add(item);
        }
    }

    Dictionary<string, ExceptionItem> _pubKeyed = new();
    ExceptionItem? PubItem(FileFault f) => _pubKeyed.GetValueOrDefault($"pub:{f.Council}:{f.Tag}");

    // Source rows behind a check, fetched from the in-browser database by the page's own row id.
    readonly Dictionary<string, List<(string Tag, string TransId, string PayDate, string Supplier, decimal Net, string Description)>> _srcRows = new();
    readonly HashSet<string> _srcOpen = new();

    async Task ToggleSource(string key, IReadOnlyList<int> ids)
    {
        if (!_srcOpen.Add(key)) { _srcOpen.Remove(key); return; }
        if (_srcRows.ContainsKey(key) || _db is null || ids.Count == 0) return;
        try
        {
            var list = new List<(string, string, string, string, decimal, string)>();
            foreach (var chunk in ids.Take(100).Chunk(50))
            {
                var r = _db.Execute($"SELECT year, transid, paydate, supplier, net, description FROM spend WHERE id IN ({string.Join(",", chunk)})");
                foreach (var row in r.Rows)
                    list.Add((S(row, "year"), S(row, "transid"), S(row, "paydate"), S(row, "supplier"),
                        row.TryGetValue("net", out var n) && n is not null ? Convert.ToDecimal(n, CultureInfo.InvariantCulture) : 0m, S(row, "description")));
            }
            _srcRows[key] = list.OrderBy(x => x.Item1, StringComparer.Ordinal).ThenBy(x => x.Item2, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) { _error = "Couldn't read those rows back: " + ex.Message; }
        await Task.CompletedTask;
    }

    static string S(IReadOnlyDictionary<string, object> row, string col) => row.TryGetValue(col, out var v) ? v?.ToString() ?? "" : "";

    /// <summary>The lines of one correction-pair transaction, taken from the loaded test-4 rows.</summary>
    List<ExceptionItem> CorrectionLines(CorrectionFinding c) =>
        _testsByLabel.TryGetValue(LabelT4, out var t4)
            ? t4.Items.Where(i => i.CouncilKey == c.Council && i.SourceTag == c.SourceTag && i.TransactionId == c.TransactionId).ToList() : new();

    // ------------------------------------------------------------------ summary table (every section, with a grand total)

    sealed record SummaryRow(string Id, string Title, string Count, string ValueMeaning, decimal? Value, string Note);

    List<SummaryRow> SummaryRows()
    {
        var rows = new List<SummaryRow>();
        // the six exception tests first (one row each, over everything loaded), then the checks of this file
        foreach (var t in _tests)
            rows.Add(new(t.Label, ShortTestLabel(t.Label) + " — " + t.Label[(t.Label.IndexOfAny(new[] { '—', '-' }) + 1)..].Trim(),
                $"{t.Items.Count:N0} exceptions", TestAmountLabel(t.Label), t.Total, ""));
        var st = StandingGroups();
        rows.Add(new("standing", "Standing payments paid late, then caught up", $"{st.Count:N0} groups ({st.Sum(g => g.Payments):N0} payments)",
            "value of the payments in those groups", st.Sum(g => g.Total), ""));
        if (_crossLoaded)
        {
            var fl = FlowsScoped("accept");
            rows.Add(new("flows", "Payments between councils and their companies", $"{fl.Count:N0} payee spellings, {FlowEntities().Count:N0} bodies",
                "paid by the payer, as published", fl.Sum(f => f.Net), ""));
            var ln = LoansScoped().Where(l => l.Status != "NotApplicable").ToList();
            rows.Add(new("loans", "Loan interest figures between councils", $"{ln.Count:N0} payments ({ln.Count(l => l.Status == "DoesNotDecode"):N0} not rebuilt)",
                "value of those payments", ln.Sum(l => l.Amount), ""));
        }
        var cr = _corrItems;
        rows.Add(new("corrections", "Reversal-and-correction pairs", $"{cr.Count:N0} of {(_checks?.GroupsExamined ?? 0):N0} small groups", "left on the books after the pair", cr.Sum(c => Math.Abs(c.Amount ?? 0m)), ""));
        var mf = MarkedFaults().ToList();
        rows.Add(new("pubfaults", "Files whose contents do not match their name, or overlap another file", $"{mf.Count:N0} of {(_checks?.Faults?.Count ?? 0):N0} files",
            "paid outside the named month, plus repeated lines", _pubItems.Sum(p => p.Amount ?? 0m), ""));
        if (_crossLoaded)
        {
            var fd = FileDupsScoped();
            rows.Add(new("filedup", "Files that list the same row twice", $"{fd.Count(d => d.Flagged):N0} of {fd.Count:N0} files", "Net value of the second copies", fd.Where(d => d.Flagged).Sum(d => d.RepeatNet), ""));
            var wi = WithinScoped();
            rows.Add(new("withintxn", "The same line repeated inside one transaction", $"{wi.Count:N0} lines", "value of the extra copies", wi.Sum(w => w.ExtraValue), ""));
            var sk = SinksScoped(); var ms = MisfitsScoped();
            rows.Add(new("sinkmisfit", "What grows, what comes back, what does not fit the usual month", $"{sk.Count:N0} payee pairs, {ms.Count:N0} months",
                "paid to those payees over the years (the unusual months are part of these totals)", sk.Sum(s => s.NetOut), ""));
        }
        return rows;
    }
}
