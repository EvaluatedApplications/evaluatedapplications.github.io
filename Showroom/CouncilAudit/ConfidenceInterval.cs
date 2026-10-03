// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\ConfidenceInterval.cs, re-synced 2026-10-03 (Session 25/26 sync): verbatim upstream.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// Plain Wilson score interval for a hand-check precision figure: "x of n opened items
/// confirmed against source". A bare percentage from a small sample reads more confident
/// than it is - FEEDBACK.md Session 9/10's hand-checks were "first N by construction order",
/// not a random sample, and reported no interval at all. This gives any hand-check cycle a
/// way to size its sample and report an honest range instead of a point estimate, without
/// pretending a small opened-vs-confirmed count proves the whole population.
/// Not tied to any one schedule or council - used by the CLI's hand-check cycles for
/// Wokingham, Merton and Reading alike (see CouncilAudit.Cli/Program.cs RunWokingham/RunMerton).
/// </summary>
public static class ConfidenceInterval
{
    /// <summary>
    /// 95% Wilson score interval for `successes` out of `total` independent Bernoulli
    /// trials. Preferred over the naive normal-approximation ("Wald") interval here
    /// because it stays inside [0,1] and doesn't collapse to zero width at 0% or 100%
    /// observed - exactly what every hand-check sample in this project has hit so far
    /// (every opened row confirmed, or every opened row in a trap case correctly refused).
    /// Returns (0.0, 1.0) for total &lt;= 0 (nothing was sampled, so nothing is known).
    /// </summary>
    public static (double Lower, double Upper) Wilson95(int successes, int total)
    {
        if (total <= 0) return (0.0, 1.0);
        if (successes < 0 || successes > total)
            throw new ArgumentOutOfRangeException(nameof(successes), "successes must be between 0 and total.");

        const double z = 1.959963985; // 95% two-sided standard normal quantile
        double n = total;
        double phat = successes / n;
        double z2 = z * z;
        double denom = 1.0 + z2 / n;
        double center = phat + z2 / (2 * n);
        double margin = z * Math.Sqrt(phat * (1 - phat) / n + z2 / (4 * n * n));
        double lower = (center - margin) / denom;
        double upper = (center + margin) / denom;
        return (Math.Max(0.0, lower), Math.Min(1.0, upper));
    }
}
