// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\CorrectionPairs.cs, re-synced 2026-10-03 (Session 25/26 sync): NEW (Session 24): reversal-and-correction single-digit pairs, public and pure.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>One single-digit difference between two published amounts of the same length.</summary>
public sealed record DigitSlip(int PositionFromRight, char From, char To, decimal Difference);

/// <summary>
/// Session 24: a computed check a visitor can see and judge for themselves. A "reversal and correction" booking
/// publishes the amount reversed and the amount re-posted; when the two differ by exactly ONE digit (a keying-slip
/// shape) the data shows it but cannot say which figure was right, or that either was a slip. This reports the
/// shape only: the residual left on the books, and whether the two figures differ in a single digit.
/// RBWM 2026-03, transaction 9997557: reversal 1,491,449.25, replacement 1,491,499.25, residual +50.00.
/// </summary>
public static class CorrectionPairs
{
    /// <summary>Two amounts (absolute values, 2dp) that have the same number of digits and differ in exactly one digit.</summary>
    public static DigitSlip? SingleDigitSlip(decimal a, decimal b)
    {
        string x = Digits(Math.Abs(a)), y = Digits(Math.Abs(b));
        if (x.Length != y.Length || x == y) return null;
        int diffAt = -1;
        for (int i = 0; i < x.Length; i++)
            if (x[i] != y[i])
            {
                if (diffAt >= 0) return null;
                diffAt = i;
            }
        return new DigitSlip(x.Length - 1 - diffAt, x[diffAt], y[diffAt], Math.Abs(b) - Math.Abs(a));
    }

    private static string Digits(decimal v) => Math.Round(v * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Looks inside one Schedule D group for a pair of lines of OPPOSITE sign whose absolute amounts differ in a single
    /// digit and which are both at least <paramref name="minAmount"/>. The group must have at most <paramref name="maxLines"/> lines and its whole residual must equal the slip (everything else cancels). Returns the first such pair and the residual.
    /// </summary>
    public static (decimal reversed, decimal replacement, DigitSlip slip, decimal residual)? Find(IReadOnlyList<decimal> lines, decimal minAmount = 1000m, int maxLines = 8)
    {
        // A big group (hundreds of lines) holds a one-digit pair by chance; only a SMALL group in which everything else
        // cancels, so the whole residual equals the slip, shows the correction shape.
        if (lines.Count > maxLines) return null;
        decimal residual = lines.Sum();
        for (int i = 0; i < lines.Count; i++)
            for (int j = 0; j < lines.Count; j++)
            {
                if (i == j || Math.Abs(lines[i]) < minAmount || Math.Abs(lines[j]) < minAmount) continue;
                if (Math.Sign(lines[i]) == Math.Sign(lines[j])) continue;
                var s = SingleDigitSlip(lines[i], lines[j]);
                if (s is not null && Math.Abs(residual) == Math.Abs(s.Difference)) return (lines[i], lines[j], s, residual);
            }
        return null;
    }
}
