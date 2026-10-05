namespace CouncilAudit;

/// <summary>One (annual rate in basis points, term in days) pair that reproduces a published interest figure to the penny.</summary>
public sealed record LoanTermRate(int RateBp, int Days);

/// <summary>
/// Session 23: the published payment files never state a loan's term or rate, only what was paid. But
/// short-dated inter-authority loans are priced Actual/365 on a round principal, so a payment of
/// principal+interest where the principal is round (Gross-Net) leaves the interest as a number that can be
/// REPRODUCED TO THE PENNY by (rate in whole basis points) x (term in whole days). Match chance by luck is
/// small (about 0.1%-1% per payment on a 1bp x 1day grid, depending on principal; measured by
/// <see cref="NullHitRate"/>), so a match is evidence the interest really is Act/365 on that principal. It
/// does NOT identify the rate: rate x days is a hyperbola, so several (rate, term) pairs fit one figure
/// (e.g. 1.65% x 365 days = 3.30% x 182.5 days). <see cref="Solve"/> returns all of them; the caller narrows
/// to market-standard terms with <see cref="IsStandardTerm"/>.
/// </summary>
public static class LoanInterest
{
    public static decimal Interest(decimal principal, int rateBp, int days, int basis = 365) =>
        Math.Round(principal * rateBp / 10000m * days / basis, 2, MidpointRounding.AwayFromZero);

    /// <summary>Same as <see cref="Interest"/> but cut (not rounded) to the penny. Session 24: Wokingham to Buckinghamshire 26/03/2021 (5m, 1,232.87) fits 15bp x 60d only if truncated (rounded is 1,232.88).</summary>
    public static decimal InterestTruncated(decimal principal, int rateBp, int days, int basis = 365) =>
        Math.Truncate(principal * rateBp / 10000m * days / basis * 100m) / 100m;

    /// <summary>Every (rateBp, days) with Interest(...) == interest exactly (to the penny).</summary>
    public static List<LoanTermRate> Solve(decimal principal, decimal interest,
        int minBp = 1, int maxBp = 800, int minDays = 1, int maxDays = 400, int basis = 365, bool truncate = false)
    {
        var result = new List<LoanTermRate>();
        if (principal <= 0m || interest <= 0m) return result;
        for (int bp = minBp; bp <= maxBp; bp++)
        {
            decimal perDay = principal * bp / 10000m / basis;
            // days is approximately interest / perDay; test the integer neighbours only (exact, not a scan of 400).
            int approx = (int)Math.Round(interest / perDay);
            for (int d = Math.Max(minDays, approx - 1); d <= Math.Min(maxDays, approx + 1); d++)
                if ((truncate ? InterestTruncated(principal, bp, d, basis) : Interest(principal, bp, d, basis)) == interest) result.Add(new LoanTermRate(bp, d));
        }
        return result;
    }

    /// <summary>Terms a treasury desk would actually quote: 1-14 days, whole weeks to 52, and whole calendar
    /// months to 12 (28-31, 59-62, 89-92, 120-123, 150-153, 181-184, 212-215, 242-245, 273-276, 303-306,
    /// 334-337, 365-366 days).</summary>
    public static bool IsStandardTerm(int days)
    {
        if (days >= 1 && days <= 14) return true;
        if (days % 7 == 0 && days <= 364) return true;
        if (days >= 360 && days <= 366) return true; // a year, allowing for weekend/bank-holiday date roll (Devon: 363)
        for (int m = 1; m <= 11; m++)
            if (days >= m * 30 - 2 && days <= m * 31) return true; // m calendar months
        return false;
    }

    /// <summary>A rate a dealer would quote: a whole multiple of 5bp (0.05%), or any whole bp at or below 25bp
    /// (near-zero-rate money-market quotes are given to the basis point).</summary>
    public static bool IsQuotedRate(int rateBp) => rateBp % 5 == 0 || rateBp <= 25;

    /// <summary>Standard term AND quoted rate: the filter used to read a rate out of a penny-exact match.</summary>
    public static bool IsPlausible(LoanTermRate c) => IsStandardTerm(c.Days) && IsQuotedRate(c.RateBp);

    /// <summary>Null baseline for <see cref="Solve"/>: the share of RANDOM interest figures (uniform between
    /// 0.01% and 5% of principal, rounded to the penny, seeded) that still reproduce under the same grid:
    /// any (rate, term); standard term; standard term AND quoted rate. If real payments hit far more often
    /// than this, the Act/365-on-round-principal reading is not luck.</summary>
    public static (double anyTerm, double standardTerm, double plausible) NullHitRate(decimal principal, int draws = 5000, int seed = 12345)
    {
        var rng = new Random(seed);
        int any = 0, std = 0, pl = 0;
        for (int i = 0; i < draws; i++)
        {
            decimal fraction = 0.0001m + (decimal)rng.NextDouble() * 0.0499m;
            decimal interest = Math.Round(principal * fraction, 2);
            var s = Solve(principal, interest);
            if (s.Count > 0) any++;
            if (s.Any(x => IsStandardTerm(x.Days))) std++;
            if (s.Any(IsPlausible)) pl++;
        }
        return ((double)any / draws, (double)std / draws, (double)pl / draws);
    }
}
