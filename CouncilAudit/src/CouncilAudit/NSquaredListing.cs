namespace CouncilAudit;

/// <summary>
/// Session 41: a publication defect found by hand in Wokingham's academy payment runs (Session 40). A multi-payee run prints each
/// payee's n distinct lines n times (The Circle Trust: 9 lines printed 81 times; The Keys Academy Trust 5 x 5; Maiden Erlegh 2 x 2),
/// the rows of a report that joins a payee's lines to themselves. The line total is then about 4.6 times the run, while the stated
/// Gross (repeated on every line) is the run's real value: in April 2023 the de-duplicated lines sum to the Gross to the penny.
///
/// Shape (see PREREG_S41_RULES.md): for EVERY payee, rows R = n^2 for an integer n and each distinct line appears a multiple of n
/// times; at least one payee has n >= 2. The de-duplicated sum counts each distinct line count/n times.
/// </summary>
public static class NSquaredListing
{
    public sealed record Result(int Payees, int PayeesRepeated, int Rows, int DistinctLines, decimal DistinctNet, decimal RowNet);

    private static (string, string, string, string, string, string, decimal) LineKey(SpendRow r) =>
        ((r.Description ?? "").Trim(), (r.ServiceArea ?? "").Trim(), (r.CostCentreArea ?? "").Trim(),
         r.PayDateRaw ?? "", r.Gross.ToString("0.00"), r.VatType ?? "", r.Net);

    /// <summary>The n-squared shape of a multi-payee transaction's lines, or null if any payee does not have it.</summary>
    public static Result? Analyse(IReadOnlyList<SpendRow> lines)
    {
        int payees = 0, repeated = 0, distinct = 0;
        decimal dedup = 0m;
        foreach (var p in lines.GroupBy(r => r.Supplier))
        {
            payees++;
            int rows = p.Count();
            int n = (int)Math.Round(Math.Sqrt(rows));
            if (n * n != rows) return null;
            if (n >= 2) repeated++;
            foreach (var k in p.GroupBy(LineKey))
            {
                int c = k.Count();
                if (c % n != 0) return null;
                int lines1 = c / n;
                distinct += lines1;
                dedup += lines1 * k.First().Net;
            }
        }
        if (repeated == 0) return null;
        return new Result(payees, repeated, lines.Count, distinct, dedup, lines.Sum(r => r.Net));
    }

    /// <summary>Session 54 (re-audit N2): the run's distinct lines: each payee's line printed c times, with n = sqrt(rows of the payee), is kept c/n times.
    /// Only meaningful when <see cref="Analyse"/> returned a result for the same lines.</summary>
    public static List<SpendRow> Deduplicate(IReadOnlyList<SpendRow> lines)
    {
        var kept = new List<SpendRow>();
        foreach (var p in lines.GroupBy(r => r.Supplier))
        {
            int n = (int)Math.Round(Math.Sqrt(p.Count()));
            foreach (var k in p.GroupBy(LineKey))
                kept.AddRange(k.Take(k.Count() / Math.Max(1, n)));
        }
        return kept;
    }

    public static string Describe(Result s, decimal gross) => Describe(s, gross, 0, 0m);

    /// <summary>True when the distinct lines, with or without the transaction's redacted lines, equal the stated Gross to the penny.</summary>
    public static bool Reconciles(Result s, decimal gross, decimal redactedNet) =>
        Math.Abs(s.DistinctNet - gross) <= 0.01m || (redactedNet != 0m && Math.Abs(s.DistinctNet + redactedNet - gross) <= 0.01m);

    /// <summary>Session 52: the sentence that says a transaction also carries lines whose payee is redacted (left out of every schedule).</summary>
    public static string RedactedNote(int lines, decimal net) => lines == 0 ? ""
        : $" The same transaction number also carries {lines} line{(lines == 1 ? "" : "s")} whose payee is redacted ({net:0.00}); the schedules leave {(lines == 1 ? "it" : "them")} out.";

    /// <summary>
    /// Session 52 (audit E11): the counts are of the NAMED lines, and the redacted lines of the same transaction are stated with their value, so the
    /// gap to the Gross is given both ways. No cause is stated for a gap: the file does not show one.
    /// </summary>
    public static string Describe(Result s, decimal gross, int redactedLines, decimal redactedNet)
    {
        string head = $"the run prints each payee's n distinct lines n times (n-squared rows; {s.PayeesRepeated} of {s.Payees} named payees have n of 2 or more): " +
            $"{s.Rows} named rows are {s.DistinctLines} distinct lines. The distinct lines sum to {s.DistinctNet:0.00} against the stated Gross {gross:0.00}";
        string red = redactedLines == 0 ? "" : $"; the transaction also carries {redactedLines} line{(redactedLines == 1 ? "" : "s")} whose payee is redacted ({redactedNet:0.00}), which the schedules leave out";
        if (Math.Abs(s.DistinctNet - gross) <= 0.01m)
            return head + ", to the penny" + red + ": the stated Gross is the run's value and the row total repeats lines, it is not extra money.";
        if (redactedLines > 0 && Math.Abs(s.DistinctNet + redactedNet - gross) <= 0.01m)
            return head + red + "; with the redacted lines the distinct lines equal the stated Gross to the penny: the stated Gross is the run's value and the row total repeats lines, it is not extra money.";
        string gap = redactedLines == 0
            ? $" (the Gross is {gross - s.DistinctNet:0.00} above them; the file does not show why)"
            : $"{red}; counting them, the Gross is {gross - s.DistinctNet - redactedNet:0.00} above the lines (the file does not show why)";
        return head + gap + $"; quote the run as the stated Gross, not the row total of {s.RowNet:0.00}.";
    }
}
