using System.Globalization;
using HoloDb;

namespace Showroom.Services;

/// <summary>A redacted payee or reference (replaced by a placeholder such as REDACTED PERSONAL DATA) is left out of every
/// exception test by the engine; the page counts them per council as files load so the exclusion is shown, not buried.
/// Same rule as the engine's own (it is private there): contains "REDACT", or the split form RED...ACTED.</summary>
public static class Redaction
{
    public static bool IsRedacted(string s) =>
        s.Contains("REDACT", StringComparison.OrdinalIgnoreCase)
        || (s.StartsWith("RED", StringComparison.OrdinalIgnoreCase) && s.EndsWith("ACTED", StringComparison.OrdinalIgnoreCase) && s.Length >= 8);
}

public sealed record LoadedPeriod(int Rows, int Redacted, FileFacts Facts);

/// <summary>
/// Reads one pre-normalised period file (written by CouncilDbBuilder), adds its rows to the in-browser HoloDb `spend`
/// table, takes its publication facts and feeds the supplier directory. Every step runs in time slices
/// (<see cref="Cooperative"/>), reports progress, and stays cancellable up to the point rows start going into the table.
/// </summary>
public static class PeriodLoader
{
    /// <summary>Rows per BulkLoad call. Measured on the desktop: 500-row calls cost the same in total as one call per file
    /// (987 ms vs 835 ms for Reading's 326,712 rows) and keep each call to ~5-20 ms, which is what a slice can afford.</summary>
    public const int Chunk = 500;

    // columns worth sharing one string instance across rows: council, year, paydate, vattype, servicearea, costcentre, supplierkey, supplier
    public const int NormInternMask = (1 << 0) | (1 << 1) | (1 << 3) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 10) | (1 << 11);

    static readonly string[] Cols =
        { "id", "council", "year", "transid", "paydate", "net", "gross", "vattype", "servicearea", "costcentre", "description", "supplierkey", "supplier" };

    /// <summary>Row ids that do not depend on load order: council, then period, then row in the file. The same id always
    /// means the same source row, so facts computed once (for example "these rows repeat an earlier file") stay valid
    /// whichever periods the visitor loads, in whatever order.</summary>
    public static long StableId(int councilIdx, int periodIdx, int row) =>
        ((long)councilIdx << 26) | ((long)periodIdx << 20) | (long)row;

    public static async Task<LoadedPeriod> LoadAsync(Database db, string councilKey, int councilIdx, string tag, int periodIdx, byte[] csv,
        Interner interner, SupplierIndex.Bucket? suppliers, Cooperative co, IWorkProgress progress)
    {
        var table = await SlicedCsv.ParseAsync(csv, co, interner, NormInternMask, progress, $"Reading {tag}");
        int m = table.Count - 1;
        if (m < 0) m = 0;
        var facts = new CouncilChecks.FactsBuilder(councilKey, tag, periodIdx);
        int redacted = 0;

        // From here rows go into the table; a cancel waits for the end of this file rather than leave half of it behind.
        co.Cancellable = false;
        try
        {
            for (int s = 1; s < table.Count; s += Chunk)
            {
                int n = Math.Min(Chunk, table.Count - s);
                var id = new long[n]; var council = new string[n]; var year = new string[n];
                var transid = new string[n]; var paydate = new string[n];
                var net = new double[n]; var gross = new double[n];
                var vattype = new string[n]; var servicearea = new string[n]; var costcentre = new string[n];
                var description = new string[n]; var supplierkey = new string[n]; var supplier = new string[n];
                for (int j = 0; j < n; j++)
                {
                    var f = table[s + j];
                    if (f.Length < 12) throw new InvalidDataException($"{tag}: row {s + j} has {f.Length} fields, expected 12.");
                    id[j] = StableId(councilIdx, periodIdx, s - 1 + j);
                    council[j] = f[0]; year[j] = f[1]; transid[j] = f[2]; paydate[j] = f[3];
                    net[j] = double.Parse(f[4], CultureInfo.InvariantCulture);
                    gross[j] = double.Parse(f[5], CultureInfo.InvariantCulture);
                    vattype[j] = f[6]; servicearea[j] = f[7]; costcentre[j] = f[8]; description[j] = f[9];
                    supplierkey[j] = f[10]; supplier[j] = f[11];
                    facts.Add((int)id[j], transid[j], paydate[j], supplierkey[j], Math.Round((decimal)net[j], 2));
                    if (Redaction.IsRedacted(transid[j]) || Redaction.IsRedacted(supplier[j])) redacted++;
                    suppliers?.Add(supplierkey[j], supplier[j], net[j]);
                }
                db.BulkLoad("spend", Cols,
                    new Array[] { id, council, year, transid, paydate, net, gross, vattype, servicearea, costcentre, description, supplierkey, supplier }, n);
                progress.Report($"Adding {tag} to the database", (double)(s - 1 + n) / Math.Max(1, m));
                await co.YieldIfDueAsync();
            }
        }
        finally { co.Cancellable = true; }
        return new LoadedPeriod(m, redacted, facts.Build());
    }
}
