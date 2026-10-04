using System.Globalization;
using System.Text;
using static Showroom.Services.CouncilTerms;

namespace Showroom.Services;

/// <summary>A parsed CSV with its header, so a column is found by name (the export files grew columns over time).</summary>
public sealed class Tab
{
    readonly Dictionary<string, int> _idx = new(StringComparer.OrdinalIgnoreCase);
    public List<string[]> Rows { get; }
    public Tab(List<string[]> table)
    {
        if (table.Count > 0) for (int i = 0; i < table[0].Length; i++) _idx[table[0][i]] = i;
        Rows = table.Count > 0 ? table.GetRange(1, table.Count - 1) : new();
    }
    public string G(string[] r, string col) => _idx.TryGetValue(col, out var i) && i < r.Length ? r[i] : "";
    public decimal M(string[] r, string col) => CouncilWebData.D(G(r, col));
    public int I(string[] r, string col) => int.TryParse(G(r, col), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public bool B(string[] r, string col) => G(r, col).Equals("True", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One displayed line of a check: a headline, a detail line, and the figure it is sorted by.</summary>
public sealed class CheckRow
{
    public decimal Sort; public string Line1 = "", Line2 = "";
    /// <summary>For "Add to request": a key that is the same every time this row is built, and the item itself, made only when ticked.</summary>
    public string? Key; public Func<FoiItem>? Foi;
    /// <summary>For "See the source rows": where the published lines behind this row are, worked out only when asked.</summary>
    public Func<List<SourceTarget>>? Src;
    /// <summary>The row's open "See the source rows" panel (null until tapped).</summary>
    public SourceState? Source;
    public CheckRow(decimal sort, string l1, string l2) { Sort = sort; Line1 = l1; Line2 = l2; }
}

/// <summary>What a check shows: its plain-English rule, its mandatory caption, the file it reads and how a row of it is worded.</summary>
public sealed class CheckDef
{
    public string Id = "", Title = "", Plain = "", Caption = "", File = "";
    /// <summary>The names this check carries in cross/check_rules.csv (the engine's registry of every row filter a check applies); shown as the check's "rules it applies" disclosure.</summary>
    public string[] RuleChecks = Array.Empty<string>();
    /// <summary>A rule the check's own file states on every row (the twins file writes it on each line); given the whole table, returns the distinct texts, shown beside the summary.</summary>
    public Func<Tab, List<string>>? RuleOf;
    /// <summary>Column holding the council this row belongs to; matched against the slug (or the full name when ByName).</summary>
    public string CouncilCol = "Council";
    public bool ByName;
    /// <summary>Turns the council's rows into display rows, and says in one sentence how many there are and what they add up to.</summary>
    public Func<Tab, List<string[]>, (List<CheckRow> Rows, string Summary)> Build = (_, _) => (new(), "");
    public bool Matches(Tab t, string[] r, string slug)
    {
        var v = t.G(r, CouncilCol);
        return ByName ? v.Equals(CouncilWebData.NameOf(slug), StringComparison.OrdinalIgnoreCase) : v.Equals(slug, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The council-level checks read from the small summary files. Written with plain loops and string keys rather than LINQ over decimals
/// and tuples on purpose: in the browser's WebAssembly runtime those generic shapes are not pre-compiled and run several times slower.
/// </summary>
public static class CouncilChecksWeb
{
    static string S(string slug) => CouncilWebData.ShortOf(slug);
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    static string S2(string fullName)
    {
        foreach (var c in CouncilWebData.Councils) if (c.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase)) return c.Short;
        return fullName;
    }
    static string Trim(string s, int n)
    {
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= n ? s : s[..n] + "...";
    }
    sealed class Pair { public string Payer = "", Entity = ""; public decimal Net; public int Rows; public List<string> Names = new(), Years = new(); }
    static void SortDesc(List<CheckRow> rows) => rows.Sort((a, b) => b.Sort.CompareTo(a.Sort));
    static FoiItem Item(string slug, string fact, params string[] lines) => new() { Slug = slug, Fact = fact, Lines = lines.ToList() };
    static string Stop(string s) => s.TrimEnd().TrimEnd('.');
    /// <summary>"TPP Contract |  | Adult Services" as "TPP Contract, Adult Services": the file's own column separators, without the empty ones.</summary>
    static string Clean(string s) => string.Join(", ", s.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0));
    /// <summary>"2018:5000000 2019:104470484" as "2018 £5.00m, 2019 £104,470,484": the file's own year series, in pounds.</summary>
    static string Series(string s)
    {
        var o = new List<string>();
        foreach (var p in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int c = p.IndexOf(':');
            o.Add(c > 0 && decimal.TryParse(p[(c + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? $"{p[..c]} {Gbp(v)}" : p);
        }
        return string.Join(", ", o);
    }
    /// <summary>Source targets for the lines that carry a date the scanner can place in a month; a row with no readable date has none.</summary>
    static List<SourceTarget> At(string slug, params (string Date, string Tx)[] at) =>
        at.Where(a => MonthKey(a.Date).Length > 0 && a.Tx.Length > 0).Select(a => new SourceTarget(slug, MonthKey(a.Date), new[] { a.Tx })).ToList();
    // ---- a council paying itself is not a payment to another council ----
    // Some payee labels are the paying council's own name in another spelling ("Leeds CC", "Surrey CC", "Merton"). They are left out of the lists that are about money going to
    // OTHER councils and companies, counted, and said so in the summary. A company a council owns ("Wokingham Housing") is not the council itself and stays.
    static readonly HashSet<string> Generic = new(StringComparer.Ordinal) { "city", "county", "borough", "metropolitan", "district", "council", "cc", "bc", "mbc", "mdc", "london", "of", "royal", "the", "and" };
    static string Core(string s)
    {
        var sb = new StringBuilder(); var w = new StringBuilder();
        void Flush() { if (w.Length > 0 && !Generic.Contains(w.ToString())) sb.Append(w); w.Clear(); }
        foreach (var ch in s.ToLowerInvariant()) { if (char.IsLetter(ch)) w.Append(ch); else Flush(); }
        Flush();
        return sb.ToString();
    }
    /// <summary>True when the payee label is the paying council itself, by its slug (its full name and its short name are both tried).</summary>
    public static bool IsSelf(string payerSlug, string entity)
    {
        if (payerSlug.Length == 0) return false;
        string e = Core(entity);
        return e.Length > 0 && (e == Core(CouncilWebData.NameOf(payerSlug)) || e == Core(CouncilWebData.ShortOf(payerSlug)));
    }
    static string SelfNote(int n, string noun, string eg) => n == 0 ? "" : $" {Plural(n, noun, noun + "s")} where the payee is the council's own name (for example \"{eg}\") {(n == 1 ? "is" : "are")} left out: a council paying itself is not a payment to another council.";

    static bool Many(Tab t, List<string[]> rows)
    {
        string? first = null;
        foreach (var r in rows) { var c = t.G(r, "Council"); if (first is null) first = c; else if (c != first) return true; }
        return false;
    }

    public static readonly CheckDef[] All =
    {
        new()
        {
            Id = "twins", Title = "Identical lines under two transaction numbers",
            Plain = "Two different transaction numbers with the same supplier and the same lines (amount and description). Each row is a pair; three identical transactions give three pairs but two extra copies, so the summary counts groups of identical transactions, not pairs. A pair that does not meet every condition of the rule below is not listed, so this is not a list of every identical pair. Sorted by the value of the second transaction.",
            Caption = "The file cannot show whether cash left twice, or whether one payment was published under two numbers. Bracknell Forest pairs dated in the future are forward schedules.",
            File = "cross/transaction_twins.csv", RuleChecks = new[] { "TwinPairs" },
            // the rule the engine writes on every row (TxnTwinScan.RuleText): every condition, including the two-line, GBP 10,000, one-payee, redacted, mangled-number and doubled-listing ones
            RuleOf = t => { var l = new List<string>(); foreach (var r in t.Rows) { var x = Cap(t.G(r, "Reading").Trim()); if (x.Length > 0 && !l.Contains(x)) l.Add(x); } return l; },
            Build = (t, rows) =>
            {
                bool many = Many(t, rows);
                var o = new List<CheckRow>(rows.Count); decimal extra = 0, extraAbs = 0; int bigGroups = 0;
                var groups = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in rows)
                {
                    decimal v = t.M(r, "TotalValue");
                    string slug = t.G(r, "Council"), ta = t.G(r, "TransactionA"), tb = t.G(r, "TransactionB");
                    // a group of identical transactions: GroupId is the file's own (an older file without it counts each pair as a group); each transaction after a group's first is one extra copy,
                    // credited once on the pair that reaches it, so the column sums to the value of the extra copies (a credit is negative)
                    string gid = t.G(r, "GroupId"); int gsize = Math.Max(2, t.I(r, "GroupSize"));
                    if (groups.Add(slug + "|" + (gid.Length > 0 ? gid : ta + "|" + tb)) && gsize > 2) bigGroups++;
                    decimal ec = t.M(r, "ExtraCopyValue"); extra += ec; extraAbs += Math.Abs(ec);
                    // DaysApart is -1 when a first pay date is missing from the file: it is not "1 day apart"
                    bool dated = t.I(r, "DaysApart") >= 0;
                    string apart = dated ? $"first pay dates {Math.Abs(t.I(r, "DaysApart"))} day(s) apart ({Date(t.G(r, "DateA"))} and {Date(t.G(r, "DateB"))})"
                                         : $"a first pay date is missing in the file ({Date(t.G(r, "DateA"))} and {Date(t.G(r, "DateB"))})";
                    o.Add(new CheckRow(Math.Abs(v), $"{t.G(r, "SupplierName")}: {Gbp(v)}",
                        $"{(many ? S(t.G(r, "Council")) + ". " : "")}Transactions {ta} and {tb}, {t.I(r, "Lines")} line(s), {apart}. {Cap(t.G(r, "Kind"))}.{(gsize > 2 ? $" One of {Num(gsize)} identical transactions, listed pair by pair." : "")}")
                    {
                        Key = $"twins|{slug}|{ta}|{tb}",
                        Foi = () => Item(slug, $"Transactions {ta} and {tb}, both for {t.G(r, "SupplierName")}, have the same {t.I(r, "Lines")} line(s) (amount and description). {(t.G(r, "DateA") == t.G(r, "DateB") ? $"Their first pay date is {Date(t.G(r, "DateA"))} for both." : dated ? $"Their first pay dates are {Date(t.G(r, "DateA"))} and {Date(t.G(r, "DateB"))}, {Math.Abs(t.I(r, "DaysApart"))} day(s) apart." : $"The file gives no first pay date for one or both ({Date(t.G(r, "DateA"))} and {Date(t.G(r, "DateB"))}).")} {Cap(Stop(t.G(r, "Kind")))}. The second transaction is {Gbp(v)}.",
                            $"transaction {ta}, pay date {Date(t.G(r, "DateA"))}, file {t.G(r, "YearA")}", $"transaction {tb}, pay date {Date(t.G(r, "DateB"))}, file {t.G(r, "YearB")}"),
                        Src = () => At(slug, (t.G(r, "DateA"), ta), (t.G(r, "DateB"), tb)),
                    });
                }
                SortDesc(o);
                return (o, $"{Plural(groups.Count, "group", "groups")} of identical transactions ({Plural(o.Count, "pair", "pairs")} listed{(bigGroups > 0 ? $"; {Num(bigGroups)} of the groups hold more than two transactions" : "")}). "
                    + $"Every transaction after a group's first is one extra copy: together the extra copies carry {Gbp(extra)} (a credit counts as negative), or {Gbp(extraAbs)} if a credit counts as its absolute value.");
            },
        },
        new()
        {
            Id = "crossfile", Title = "The same line published in two files",
            Plain = "A payment line is \"also in an earlier file\" when an earlier file of the same council holds a row with the same supplier, amount, payment date, description and service (letter case in the description and service is ignored). Only files with at least one such row are listed; the rate is shown with the row count, not only a flag.",
            Caption = "The file cannot show whether the payment was posted twice or published twice.",
            File = "cross/cross_file_repeats.csv", RuleChecks = new[] { "CrossFile" },
            Build = (t, rows) =>
            {
                bool many = Many(t, rows);
                var o = new List<CheckRow>(); int hits = 0; long rowsAlso = 0; decimal val = 0;
                foreach (var r in rows)
                {
                    int also = t.I(r, "RowsAlsoInEarlierFile");
                    if (also <= 0) continue;
                    hits++; rowsAlso += also; decimal v = t.M(r, "ValueAbsAlsoInEarlierFile"); val += v;
                    string earlier = t.G(r, "MainEarlierFiles"), slug = t.G(r, "Council"), file = t.G(r, "File");
                    o.Add(new CheckRow(v, $"{file}: {Num(also)} of {Num(t.I(r, "Rows"))} rows ({Pct(t.M(r, "Rate"))}) are also in an earlier file",
                        $"{(many ? S(t.G(r, "Council")) + ". " : "")}{Gbp(v)} of absolute value. Earlier files holding them: {(string.IsNullOrWhiteSpace(earlier) ? "not listed" : earlier)}. {(t.B(r, "Flagged") ? "Marked: above the usual rate of the neighbouring files." : "Not marked against the neighbouring files' rate.")}")
                    {
                        Key = $"crossfile|{slug}|{file}",
                        Foi = () => Item(slug, $"The file tagged {file} holds {Num(also)} of {Num(t.I(r, "Rows"))} rows ({Pct(t.M(r, "Rate"))}) that are also in an earlier file, with the same supplier, amount, payment date, description and service; {Gbp(v)} of absolute value. Earlier files holding them: {(string.IsNullOrWhiteSpace(earlier) ? "not listed" : earlier)}."),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(hits)} of {Num(rows.Count)} files hold at least one row already in an earlier file: {Num(rowsAlso)} rows, {Gbp(val)} of absolute value.");
            },
        },
        new()
        {
            Id = "filedup", Title = "Files that list the same row twice",
            Plain = "Does a file contain the same payment line more than once, compared with the months either side of it? Each file's repeat rate is set against its own council's neighbouring files, because repeat rates differ about tenfold between councils. Only flagged files are listed.",
            Caption = "The files do not say why a line is repeated.",
            File = "cross/file_duplication.csv", RuleChecks = new[] { "FileDuplication" },
            Build = (t, rows) =>
            {
                var o = new List<CheckRow>();
                foreach (var r in rows)
                {
                    if (!t.B(r, "Flagged")) continue;
                    decimal v = t.M(r, "RepeatNetAbs");
                    string slug = t.G(r, "Council"), file = t.G(r, "File");
                    o.Add(new CheckRow(v, $"{file}: {Num(t.I(r, "ExactRepeatRows"))} of {Num(t.I(r, "Rows"))} rows ({Pct(t.M(r, "Rate"))}) repeat a line already in the same file",
                        $"{S(t.G(r, "Council"))}. {Gbp(v)} of absolute Net. The usual rate in the neighbouring files is {Pct(t.M(r, "NeighbourMedianRate"))}.")
                    {
                        Key = $"filedup|{slug}|{file}",
                        Foi = () => Item(slug, $"The file tagged {file} contains {Num(t.I(r, "ExactRepeatRows"))} of {Num(t.I(r, "Rows"))} rows ({Pct(t.M(r, "Rate"))}) that repeat a line already in the same file, {Gbp(v)} of absolute Net. The usual rate in the neighbouring files is {Pct(t.M(r, "NeighbourMedianRate"))}."),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(o.Count)} of {Num(rows.Count)} files are flagged; every other file is within its own council's normal repeat rate.");
            },
        },
        new()
        {
            Id = "withintxn", Title = "The same line repeated inside one transaction",
            Plain = "One transaction number lists an identical line (same supplier, amount, description and pay date) at least twice, and the line's amount is at least £10,000 (in absolute value): a smaller repeated line is not listed. The same amount on different dates is a payment schedule, not a repeat, and is not listed either. "
                + "Wokingham is not run: its invoice-amount check already tests each transaction against the invoice total its file states. For every other council the files state no invoice total to compare against, so the lines are shown as published, sorted by the extra value.",
            Caption = "The file cannot show whether the repeated line was one payment or several.",
            File = "cross/within_txn_repeats.csv", RuleChecks = new[] { "WithinTransaction" },
            Build = (t, rows) =>
            {
                var o = new List<CheckRow>(rows.Count); decimal sum = 0;
                foreach (var r in rows)
                {
                    decimal extra = t.M(r, "ExtraValue"); sum += extra;
                    string slug = t.G(r, "Council"), tx = t.G(r, "TransactionId");
                    o.Add(new CheckRow(extra, $"{t.G(r, "SupplierName")}: {Gbp(t.M(r, "Net"))} listed {t.I(r, "Copies")} times in transaction {tx}",
                        $"{S(t.G(r, "Council"))}, {t.G(r, "Year")}. Pay date {Date(t.G(r, "PayDate"))}. Extra value {Gbp(extra)}. \"{Trim(t.G(r, "Description"), 90)}\". {Cap(t.G(r, "Reading"))}.")
                    {
                        Key = $"withintxn|{slug}|{t.G(r, "Year")}|{tx}|{t.G(r, "PayDate")}|{t.G(r, "Net")}",
                        Foi = () => Item(slug, $"Transaction {tx} ({t.G(r, "SupplierName")}), in the file tagged {t.G(r, "Year")}, lists the same line {t.I(r, "Copies")} times: {Gbp(t.M(r, "Net"))}, pay date {Date(t.G(r, "PayDate"))}, description \"{Trim(t.G(r, "Description"), 120)}\". The extra copies come to {Gbp(extra)}."),
                        Src = () => At(slug, (t.G(r, "PayDate"), tx)),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(o.Count)} transaction(s) carry a repeated line, {Gbp(sum)} of extra value in all.");
            },
        },
        new()
        {
            Id = "flows", Title = "Money paid between these councils and the companies they own",
            Plain = "How much each of these councils paid another one of them, or a company one of them owns, even when the other side is spelled differently on the payer's books. Every spelling is listed. A person graded each candidate spelling by hand; no similarity score separates right from wrong, so the table behind this is published in full. Only graded matches marked \"accept\" are counted. The name as published is shown beside the authority it was matched to, because the matching is a triage, not a proof: of 99 names the first screen called \"all distinctive words present\", one was a different body (Woking, for Wokingham).",
            Caption = "What the payer says it paid; never reconciled to the payee's income.",
            File = "cross/crossref_alias_flows.csv", CouncilCol = "Payer", RuleChecks = new[] { "AliasFlows" },
            Build = (t, rows) =>
            {
                // one line per payer and payee, with every spelling and period the accepted matches carry
                var byPair = new Dictionary<string, Pair>();
                foreach (var r in rows)
                {
                    if (t.G(r, "Verdict") != "accept") continue;
                    string payer = t.G(r, "Payer"), entity = t.G(r, "Entity"), key = payer + "\u0001" + entity;
                    if (!byPair.TryGetValue(key, out var e)) byPair[key] = e = new Pair { Payer = payer, Entity = entity };
                    e.Net += t.M(r, "Net"); e.Rows += t.I(r, "Rows");
                    string nm = t.G(r, "SupplierName"); if (!e.Names.Contains(nm)) e.Names.Add(nm);
                    foreach (var y in t.G(r, "Years").Split(' ', StringSplitOptions.RemoveEmptyEntries)) if (!e.Years.Contains(y)) e.Years.Add(y);
                }
                var o = new List<CheckRow>(); decimal sum = 0; int self = 0; string selfEg = "";
                foreach (var e in byPair.Values)
                {
                    if (IsSelf(e.Payer, e.Entity)) { self++; selfEg = e.Entity; continue; }
                    sum += e.Net;
                    var pe = e;
                    o.Add(new CheckRow(e.Net, $"{S(e.Payer)} paid {e.Entity}: {Gbp(e.Net)} in {Num(e.Rows)} rows",
                        $"{e.Names.Count} spelling(s) on the payer's books: {string.Join("; ", e.Names.GetRange(0, Math.Min(14, e.Names.Count)))}{(e.Names.Count > 14 ? "; and more" : "")}. Periods: {string.Join(", ", e.Years.GetRange(0, Math.Min(8, e.Years.Count)))}{(e.Years.Count > 8 ? ", and more" : "")}.")
                    {
                        Key = $"flows|{e.Payer}|{e.Entity}",
                        Foi = () => Item(pe.Payer, $"The published file shows {S(pe.Payer)} paying {pe.Entity} {Gbp(pe.Net)} in {Num(pe.Rows)} rows. The payee is spelled {pe.Names.Count} way(s) on the payer's books: {string.Join("; ", pe.Names.Take(14))}{(pe.Names.Count > 14 ? "; and more" : "")}. Periods: {string.Join(", ", pe.Years.Take(8))}{(pe.Years.Count > 8 ? ", and more" : "")}."),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(o.Count)} payer-and-payee pair(s) found, {Gbp(sum)} in all.{SelfNote(self, "pair", selfEg)}");
            },
        },
        new()
        {
            Id = "loandecode", Title = "Loan interest between councils: can the figure be rebuilt?",
            Plain = "For a payment between councils that looks like loan interest: can the figure be rebuilt from a round loan, a rate and a number of days? If yes, the rebuild is shown; if not, it is shown as not rebuilt. Loans from the Public Works Loan Board, banks or a council's own company are different instruments and are not listed.",
            Caption = "A payment that is not rebuilt is not evidence of anything; many loans are not round or not in the file.",
            File = "cross/debt_ledger.csv", ByName = true, RuleChecks = new[] { "DebtLedger" },
            Build = (t, rows) =>
            {
                var o = new List<CheckRow>(); int ia = 0, ok = 0, no = 0, self = 0; string selfEg = "";
                foreach (var r in rows)
                {
                    if (t.G(r, "Class") != "InterAuthority") continue;
                    if (IsSelf(CouncilWebData.SlugOfName(t.G(r, "Council")) ?? "", t.G(r, "Counterparty"))) { self++; selfEg = t.G(r, "Counterparty"); continue; }
                    ia++;
                    string status = t.G(r, "DecodeStatus"), how;
                    if (status == "Decodes") ok++; else if (status == "DoesNotDecode") no++;
                    if (status == "Decodes" && t.G(r, "DecodedRateBp") != "" && t.G(r, "DecodedDays") != "")
                        how = $"Rebuilt: a round loan of {Gbp(t.M(r, "DecodedPrincipal"))} at {(t.M(r, "DecodedRateBp") / 100m).ToString("0.##", CultureInfo.InvariantCulture)}% for {t.G(r, "DecodedDays")} days.";
                    else if (status == "Decodes") how = $"Rebuilt from a round loan of {Gbp(t.M(r, "DecodedPrincipal"))}, but more than one rate and number of days give the same figure: {t.G(r, "DecodeNote")}.";
                    else if (status == "DecodesPrincipalUnpublished") how = $"The figure can be rebuilt as interest, but the loan itself is not published: {t.G(r, "DecodeNote")}.";
                    else if (status == "DoesNotDecode") how = $"Not rebuilt from a round loan, a rate and a number of days ({t.G(r, "DecodeNote")}).";
                    else how = CouncilCodes.DecodeStatus(status) + ".";   // "Not applicable", or the unlisted text for a status the table does not describe (never the raw code)
                    decimal amt = t.M(r, "Amount");
                    string slug = CouncilWebData.SlugOfName(t.G(r, "Council")) ?? "", tx = t.G(r, "TransactionId"), howNow = how;
                    var row = new CheckRow(Math.Abs(amt), $"{t.G(r, "Counterparty")}: {Gbp(amt)} on {Date(t.G(r, "PayDate"))}", $"{S2(t.G(r, "Council"))} {t.G(r, "Year")}. {how}");
                    if (slug.Length > 0)
                    {
                        row.Key = $"loan|{slug}|{tx}|{t.G(r, "PayDate")}|{amt}";
                        // facts only: the payment, and (when the check could not rebuild it) that it did not rebuild; a rebuilt figure is an explanation, so it is not repeated here
                        row.Foi = () => Item(slug, $"The published file shows a payment of {Gbp(amt)} to {t.G(r, "Counterparty")} on {Date(t.G(r, "PayDate"))} (transaction {TxText(slug, tx)}, file tagged {t.G(r, "Year")})." +
                            (t.G(r, "DecodeStatus") == "DoesNotDecode" ? " The figure does not rebuild from a round loan, a rate and a number of days." : ""));
                        row.Src = () => At(slug, (t.G(r, "PayDate"), tx));
                    }
                    o.Add(row);
                }
                SortDesc(o);
                return (o, $"{Num(ia)} payment(s) between councils: {Num(ok)} rebuilt, {Num(no)} not rebuilt, the rest rebuilt as interest on an unpublished loan or not applicable.{SelfNote(self, "payment", selfEg)}");
            },
        },
        new()
        {
            Id = "debtsink", Title = "Money paid out to other councils and councils' companies, and what is credited back",
            Plain = "Each pair shows what one council's spending file shows going out to another council or a company it owns, over how many years, and what was credited back in the same file. The \"In this file\" line states in words what the scanner noticed about the pair's yearly totals and credits; it is a description of the numbers, not a conclusion. A payee that is the paying council's own name is not another council and is left out. Rows with no published payment date count in every total and are shown separately; they are left out only of the year-by-year figures and the month test.",
            Caption = "A spending file shows only the payer's side. Nothing coming back in this file does not mean a debt is unpaid; repayments and income are in other records.",
            File = "cross/debt_sink.csv", CouncilCol = "Payer", RuleChecks = new[] { "DebtSink" },
            Build = (t, rows) =>
            {
                var o = new List<CheckRow>(rows.Count); decimal sum = 0; int self = 0; string selfEg = "";
                foreach (var r in rows)
                {
                    string slug = t.G(r, "Payer"), entity = t.G(r, "Entity");
                    if (IsSelf(slug, entity)) { self++; selfEg = entity; continue; }
                    decimal out_ = t.M(r, "NetOut"); sum += out_;
                    // the engine's flag codes ("NoReturnFlow", "RisingFullYears3") are never printed: CouncilCodes turns each into words (and the build tools fail on a code it does not know)
                    string noted = CouncilCodes.DebtFlags(t.G(r, "Flags"));
                    // rows with no published pay date are in the totals but not in the year series (UndatedRows / UndatedNet, which the engine added in Session 52; an older file has neither column)
                    int undated = t.I(r, "UndatedRows"); decimal undatedNet = t.M(r, "UndatedNet");
                    string undatedLine = undated > 0 ? $" {Plural(undated, "row", "rows")} ({Gbp(undatedNet)}) {(undated == 1 ? "has" : "have")} no published payment date: {(undated == 1 ? "it is" : "they are")} in the totals but not in the by-year figures." : "";
                    o.Add(new CheckRow(out_, $"{S(t.G(r, "Payer"))} to {t.G(r, "Entity")}: {Gbp(out_)} out in {Num(t.I(r, "Rows"))} rows, {t.G(r, "FirstYear")} to {t.G(r, "LastYear")}",
                        $"Credited back in this file: {Num(t.I(r, "CreditRows"))} rows, {Gbp(t.M(r, "CreditNet"))}. By year: {t.G(r, "YearSeries")}.{undatedLine}{(noted.Length > 0 ? " In this file: " + noted + "." : "")}")
                    {
                        Key = $"debtsink|{slug}|{entity}",
                        Foi = () => Item(slug, $"The published file shows {S(slug)} paying {entity} {Gbp(out_)} in {Num(t.I(r, "Rows"))} rows, {t.G(r, "FirstYear")} to {t.G(r, "LastYear")}, with {Num(t.I(r, "CreditRows"))} credit rows ({Gbp(t.M(r, "CreditNet"))}) from {entity} in the same file. By year: {Series(t.G(r, "YearSeries"))}.{undatedLine}"),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(o.Count)} pair(s), {Gbp(sum)} out in all.{SelfNote(self, "pair", selfEg)}");
            },
        },
        new()
        {
            Id = "misfits", Title = "Months that do not fit the usual month",
            Plain = "For the same pairs as above: a month whose total is far from that pair's usual month. Each line says whether the council's own description of that month's largest row appears in any other month. A pair whose payee is the paying council's own name is left out.",
            Caption = "A spending file shows only the payer's side. Nothing coming back in this file does not mean a debt is unpaid; repayments and income are in other records.",
            File = "cross/payment_misfits.csv", CouncilCol = "Payer", RuleChecks = new[] { "DebtSink" },
            Build = (t, rows) =>
            {
                var o = new List<CheckRow>(rows.Count); int only = 0, self = 0; string selfEg = "";
                foreach (var r in rows)
                {
                    decimal v = t.M(r, "MonthNet");
                    string slug = t.G(r, "Payer"), entity = t.G(r, "Entity"), month = t.G(r, "Month");
                    if (IsSelf(slug, entity)) { self++; selfEg = entity; continue; }
                    string label = CouncilCodes.LabelCheck(t.G(r, "LabelCheck"));   // the engine's own wording in plain sentences; an unknown value is never printed raw
                    if (t.G(r, "LabelCheck").StartsWith("LABEL SEEN ONLY", StringComparison.OrdinalIgnoreCase)) only++;
                    o.Add(new CheckRow(v, $"{S(t.G(r, "Payer"))} to {t.G(r, "Entity")}, {MonthName(t.G(r, "Month"))}: {Gbp(v)}",
                        $"A usual month for this pair is {Gbp(t.M(r, "MedianMonthNet"))} ({t.M(r, "Ratio").ToString("0.#", CultureInfo.InvariantCulture)} times). {Num(t.I(r, "Rows"))} rows. {label} Largest row: {Trim(t.G(r, "TopDescription"), 90)}.")
                    {
                        Key = $"misfit|{slug}|{entity}|{month}",
                        Foi = () => Item(slug, $"In {MonthName(month)} the published file shows {S(slug)} paying {entity} {Gbp(v)} in {Num(t.I(r, "Rows"))} rows. A usual month for that pair is {Gbp(t.M(r, "MedianMonthNet"))}, so this month is {t.M(r, "Ratio").ToString("0.#", CultureInfo.InvariantCulture)} times it. {label} Largest row: {Trim(Clean(t.G(r, "TopDescription")), 120)}."),
                    });
                }
                SortDesc(o);
                return (o, $"{Num(o.Count)} month(s) flagged, {Num(only)} of them with a description that appears in no other month.{SelfNote(self, "month", selfEg)}");
            },
        },
    };
}
