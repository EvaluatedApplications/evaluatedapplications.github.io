// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\FileDuplication.cs, re-synced 2026-10-03 (Session 25/26 sync): NEW (Session 26): rows published twice inside one file, judged against neighbouring files.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// Session 26: rows published twice INSIDE one file.
///
/// Found by hand on Reading: reading_2024-10.csv lists 400 payments twice (every published column identical,
/// GBP 6.36m of Net) and reading_2024-12.csv lists 698 twice (GBP 4.20m); the 2024 files either side, and every other
/// 2024-26 month, have none. Schedule B needs two different transaction numbers and Schedule D needs two payees or
/// two dates, so a row repeated under its own voucher number was invisible to every other check.
///
/// Identical rows are NOT rare in every council's data (Wokingham 1-9% of a year's rows, Bracknell 5-12% of a
/// quarter's, Merton 3%: ordinary repeated lines). A file is therefore judged against its own council's
/// neighbouring files: it is flagged only when the repeat count is at least 50 rows, at least 2% of the file, and at
/// least three times the 75th-percentile repeat rate of up to five files either side (floored at 0.5%). (A first version used the median of three each side and flagged Bracknell 2020-DecFeb on the strength of two unusually clean 2021 files; the stricter baseline does not.)
/// </summary>
public static class FileDuplication
{
    public sealed record FileStat(string SourceTag, int Rows, int RepeatRows, decimal RepeatNetAbs,
        double Rate, double NeighbourMedianRate, bool Anomalous);

    public static List<FileStat> Scan(IReadOnlyList<SpendRow> all)
    {
        var order = new List<string>();
        var seen = new HashSet<string>();
        foreach (var r in all) if (seen.Add(r.SourceTag)) order.Add(r.SourceTag);
        var raw = new List<(string Tag, int Rows, int Repeats, decimal Value)>();
        foreach (var tag in order)
        {
            var rows = all.Where(r => r.SourceTag == tag).ToList();
            var keys = new HashSet<(string, string, decimal, decimal, string?, string?, string?, string?, string?, string?)>();
            int repeats = 0; decimal value = 0m;
            foreach (var r in rows)
            {
                var k = (r.TransactionId, r.Supplier, r.Net, r.Gross, r.PayDateRaw, r.Description, r.InvoiceType,
                         r.CostCentreArea, r.SupplierInvoiceNumber, r.OtherColumns);
                if (!keys.Add(k)) { repeats++; value += Math.Abs(r.Net); }
            }
            raw.Add((tag, rows.Count, repeats, value));
        }
        var result = new List<FileStat>();
        for (int i = 0; i < raw.Count; i++)
        {
            var neigh = new List<double>();
            for (int j = Math.Max(0, i - 5); j <= Math.Min(raw.Count - 1, i + 5); j++)
                if (j != i && raw[j].Rows > 0) neigh.Add((double)raw[j].Repeats / raw[j].Rows);
            neigh.Sort();
            double median = neigh.Count == 0 ? 0 : neigh[Math.Min(neigh.Count - 1, neigh.Count * 3 / 4)]; // 75th percentile of up to five files either side
            double rate = raw[i].Rows == 0 ? 0 : (double)raw[i].Repeats / raw[i].Rows;
            bool anomalous = raw[i].Repeats >= 50 && rate >= 0.02 && rate >= 3 * Math.Max(median, 0.005);
            result.Add(new FileStat(raw[i].Tag, raw[i].Rows, raw[i].Repeats, raw[i].Value, rate, median, anomalous));
        }
        return result;
    }
}
