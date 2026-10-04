using System.Globalization;
using System.Text;

namespace Showroom.Services;

/// <summary>Where to look for the published lines behind one flagged item: a council, a month's transaction slice, the transaction numbers
/// to find there, and (for a group of one supplier's repeated lines) that supplier, to tell apart suppliers who reuse one number.</summary>
public sealed record SourceTarget(string Slug, string Month, IReadOnlyList<string> Tx, string? Supplier = null);

/// <summary>One published line of a month's transaction slice, as the council published it (the slim columns of web_export).</summary>
public sealed class SourceRow
{
    public string Year = "", Tx = "", PayDate = "", Service = "", Description = "", Supplier = "", CostCentre = "", InvoiceNo = "", InvoiceType = "", RowIndex = "";
    public decimal Net, Gross, Vat;
}

public sealed class SourceResult
{
    public List<SourceRow> Rows = new();
    /// <summary>How many lines of the month carry one of the transaction numbers (Rows holds at most the first <see cref="SourceScan.Cap"/> of them).</summary>
    public int Found;
    /// <summary>True when the supplier narrowed the match (otherwise every line carrying the number is listed).</summary>
    public bool NarrowedBySupplier;
}

/// <summary>One month's answer inside the "See the source rows" panel of an item.</summary>
public sealed class SourceBlock
{
    public SourceResult? Result;
    public string Summary = "";
    public int Shown = 15;
}

/// <summary>What the "See the source rows" panel of one item holds. It lives on the item (not in a component) so a long list draws no extra component per row.</summary>
public sealed class SourceState
{
    public bool Open, Busy, Loaded;
    public string? Error, Progress;
    public readonly List<SourceBlock> Blocks = new();

    /// <summary>Fetches each month's transaction file (cached by <see cref="CouncilWebData"/>) and finds the item's lines in it. <paramref name="redraw"/> is called when the panel's state changes while this runs.</summary>
    public async Task LoadAsync(CouncilWebData data, List<SourceTarget> targets, Action redraw)
    {
        if (Loaded || Busy) return;
        Busy = true; Error = null; Progress = "";
        redraw();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var co = new Cooperative(CancellationToken.None);
            if (targets.Count == 0)
                Blocks.Add(new SourceBlock { Summary = "This line has no readable pay date, so the month file it sits in is not known and its source rows cannot be looked up." });
            foreach (var t in targets)
            {
                var b = new SourceBlock();
                var months = await data.MonthsAsync(t.Slug, co);
                var m = months.FirstOrDefault(x => x.Month == t.Month);
                if (m is null)
                {
                    b.Summary = $"{CouncilTerms.MonthName(t.Month)} is not one of the months listed for this council, so there is no published month file to read these lines from.";
                }
                else
                {
                    Progress = m.Parts > 1 ? $"({m.Parts} parts)" : "";
                    redraw();
                    var parts = await data.TxSliceAsync(t.Slug, t.Month, m.Parts, co);
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    b.Result = await SourceScan.FindAsync(parts, t.Tx, t.Supplier, co);
                    await data.LogAsync($"source rows {t.Slug} {t.Month}", sw2.ElapsedMilliseconds, $"({b.Result.Found} found, longest slice {co.MaxSliceMs:F0}ms)");
                    string ids = string.Join(", ", t.Tx.Take(4)) + (t.Tx.Count > 4 ? $" and {t.Tx.Count - 4} more" : "");
                    b.Summary = b.Result.Found == 0
                        ? $"No line in {CouncilTerms.MonthName(t.Month)}'s published file carries transaction {ids}."
                        : $"{CouncilTerms.Num(b.Result.Found)} line(s) in {CouncilTerms.MonthName(t.Month)}'s published file carry transaction {ids}" + (b.Result.NarrowedBySupplier ? ", narrowed to the same supplier." : ".");
                }
                Blocks.Add(b);
            }
            Loaded = true;
            await data.LogAsync("source rows ready", sw.ElapsedMilliseconds);
        }
        catch (Exception ex) { Error = "Could not read the source rows: " + ex.Message; }
        finally { Busy = false; redraw(); }
    }
}

/// <summary>
/// Finds the published lines that carry given transaction numbers in one month's transaction slice. Like the exception scan it reads the
/// bytes in place, builds no string for a line that does not match, and hands the thread back every few hundred lines, so the biggest
/// month (about 5 MB raw across three parts) scans without freezing a phone.
/// </summary>
public static class SourceScan
{
    public const int Cap = 200;

    public static async Task<SourceResult> FindAsync(List<byte[]> parts, IReadOnlyList<string> tx, string? supplier, Cooperative co)
    {
        var res = new SourceResult();
        if (tx.Count == 0) return res;
        // candidates grouped by byte length so most lines are rejected on length alone
        var byLen = new Dictionary<int, List<byte[]>>();
        foreach (var t in tx) { var b = Encoding.UTF8.GetBytes(t); if (!byLen.TryGetValue(b.Length, out var l)) byLen[b.Length] = l = new(); l.Add(b); }
        var sup = supplier is null ? null : Encoding.UTF8.GetBytes(supplier);
        var st = new int[32]; var ln = new int[32];
        var matched = new List<(SourceRow Row, bool SupplierMatch)>();
        int since = 0, matchedTotal = 0, supplierTotal = 0;
        foreach (var b in parts)
        {
            int pos = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            int nf = MonthScan.Fields(b, ref pos, st, ln);
            int cYear = Find(b, st, ln, nf, "Year"), cTx = Find(b, st, ln, nf, "TransactionId"), cDate = Find(b, st, ln, nf, "PayDate"), cNet = Find(b, st, ln, nf, "Net"),
                cGross = Find(b, st, ln, nf, "Gross"), cVat = Find(b, st, ln, nf, "VatAmount"), cSvc = Find(b, st, ln, nf, "ServiceArea"), cDesc = Find(b, st, ln, nf, "Description"),
                cSup = Find(b, st, ln, nf, "SupplierName"), cCc = Find(b, st, ln, nf, "CostCentreArea"), cInv = Find(b, st, ln, nf, "SupplierInvoiceNumber"),
                cTyp = Find(b, st, ln, nf, "InvoiceType"), cRow = Find(b, st, ln, nf, "RowIndexInSource");
            if (cTx < 0) throw new InvalidDataException("This month's file does not have the columns the page expects.");
            while (pos < b.Length)
            {
                nf = MonthScan.Fields(b, ref pos, st, ln);
                if (nf > cTx && byLen.TryGetValue(ln[cTx], out var cands))
                {
                    var span = b.AsSpan(st[cTx], ln[cTx]);
                    bool hit = false;
                    foreach (var c in cands) if (span.SequenceEqual(c)) { hit = true; break; }
                    if (hit)
                    {
                        bool supOk = sup is not null && cSup >= 0 && b.AsSpan(st[cSup], ln[cSup]).SequenceEqual(sup);
                        matchedTotal++; if (supOk) supplierTotal++;
                        if (matched.Count < Cap * 4)
                        {
                            string S(int c) => c < 0 || c >= nf || ln[c] <= 0 ? "" : Unquote(Encoding.UTF8.GetString(b, st[c], ln[c]));
                            matched.Add((new SourceRow
                            {
                                Year = S(cYear), Tx = S(cTx), PayDate = S(cDate), Net = D(S(cNet)), Gross = D(S(cGross)), Vat = D(S(cVat)), Service = S(cSvc),
                                Description = S(cDesc), Supplier = S(cSup), CostCentre = S(cCc), InvoiceNo = S(cInv), InvoiceType = S(cTyp), RowIndex = S(cRow),
                            }, supOk));
                        }
                    }
                }
                if (++since >= 256) { since = 0; if (co.Due) await co.YieldAsync(); }
            }
        }
        // a supplier narrows the list only when at least one line carries both the number and the supplier
        bool narrow = sup is not null && supplierTotal > 0 && supplierTotal < matchedTotal;
        var keep = narrow ? matched.Where(m => m.SupplierMatch).Select(m => m.Row) : matched.Select(m => m.Row);
        res.Rows = keep.Take(Cap).ToList();
        res.Found = narrow ? supplierTotal : matchedTotal;
        res.NarrowedBySupplier = narrow;
        return res;
    }

    static int Find(byte[] b, int[] st, int[] ln, int nf, string name)
    {
        for (int i = 0; i < nf; i++)
            if (ln[i] == name.Length && Encoding.ASCII.GetString(b, st[i], ln[i]).Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    static string Unquote(string t) => t.Contains("\"\"") ? t.Replace("\"\"", "\"") : t;
    static decimal D(string s) => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
