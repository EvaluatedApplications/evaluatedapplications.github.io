using System.Text.RegularExpressions;

namespace CouncilAudit;

public enum DecodeStatus
{
    /// <summary>Principal published (round, Gross minus Net) AND a standard term at a quoted rate reproduces the interest to the penny.</summary>
    Decodes,
    /// <summary>Principal NOT published (interest-only line); at least one round-million principal and a 364-366 day term at a quoted rate reproduces the figure to the penny. Reported, but the principal itself is a search result, not a published fact.</summary>
    DecodesPrincipalUnpublished,
    /// <summary>No (principal, quoted rate, standard term) reproduces the figure under Act/365.</summary>
    DoesNotDecode,
    /// <summary>Not a borrowing/lending payment, or decoding is not meaningful (broker fee, re3 waste PFI instalment, credit line).</summary>
    NotApplicable,
}

public enum CounterpartyClass { InterAuthority, Pwlb, Bank, Broker, OwnedCompany, Redacted, Other, OwnCouncil }

/// <param name="Principal">Published principal for a split line; for an interest-only line only set when the search finds exactly one fit.</param>
/// <param name="RateBp">Annual rate in basis points, ONLY when exactly one (rate, term) fits; null when several fit (the Note lists them).</param>
/// <param name="Days">Term in days, only when exactly one fit.</param>
/// <param name="Candidates">How many plausible (principal, rate, term) triples reproduce the figure. Rate x days is a hyperbola, so more than one is normal.</param>
public sealed record LoanDecode(DecodeStatus Status, string Kind, decimal? Principal, int? RateBp, int? Days, int Candidates, string Note);

/// <summary>
/// Session 24: the Session 23 loan-interest decoding (rate x days, Act/365, see <see cref="LoanInterest"/>) lifted out
/// of the CLI's console report into a pure function, so the debt ledger can carry it as columns on every row.
/// </summary>
public static class LoanDecoder
{
    public static CounterpartyClass Classify(string council, string counterparty)
    {
        string c = counterparty ?? "";
        if (Regex.IsMatch(c, "REDACT", RegexOptions.IgnoreCase)) return CounterpartyClass.Redacted;
        // Session 53 (checklist C11): a payee that is the paying council's own name (Camden's file lists "LONDON BOROUGH OF CAMDEN" under interest payable) is not another authority
        if (SelfPayment.Core(c).Length > 0 && SelfPayment.Core(c) == SelfPayment.Core(council ?? "")) return CounterpartyClass.OwnCouncil;
        if (Regex.IsMatch(c, "Public Works Loan|PWLB", RegexOptions.IgnoreCase)) return CounterpartyClass.Pwlb;
        if (Regex.IsMatch(c, "Tradition|TPICAP|ICAP|Imperial Treasury|Link Treasury|Broking|Broker", RegexOptions.IgnoreCase)) return CounterpartyClass.Broker;
        if (Regex.IsMatch(c, @"\b(Barclays|Deutsche|Goldman|NatWest|Lloyds|HSBC|Santander)\b|\bBank\b", RegexOptions.IgnoreCase)) return CounterpartyClass.Bank;
        if (Regex.IsMatch(c, @"Berry Brook|Loddon Homes|Wokingham Housing|Brighter Futures for Children", RegexOptions.IgnoreCase)) return CounterpartyClass.OwnedCompany;
        if (Regex.IsMatch(c, @"\b(Council|Borough|County|Authority|District)\b", RegexOptions.IgnoreCase)) return CounterpartyClass.InterAuthority;
        return CounterpartyClass.Other;
    }

    /// <summary>Wokingham's debt-shaped lines to Reading Borough Council are its share of the re3 Waste PFI contract (Session 22), not a loan.</summary>
    public static bool IsRe3WastePfi(string council, string counterparty) =>
        council.StartsWith("Wokingham", StringComparison.OrdinalIgnoreCase)
        && Regex.IsMatch(counterparty ?? "", "Reading Borough Council", RegexOptions.IgnoreCase);

    /// <summary>
    /// Decodes one payment. <paramref name="gross"/> is the stated total; <paramref name="net"/> the interest figure
    /// (Wokingham: Net is the interest and Gross is principal plus interest; a single-figure line has Gross == Net).
    /// A split line needs Gross - Net to be a round thousand of at least 100k; anything else is read as interest-only.
    /// </summary>
    public static LoanDecode Decode(decimal gross, decimal net)
    {
        if (net <= 0m) return new LoanDecode(DecodeStatus.NotApplicable, "credit-or-zero", null, null, null, 0, "non-positive amount");
        decimal diff = gross - net;
        if (diff >= 100000m && diff % 1000m == 0m)
        {
            var c = LoanInterest.Solve(diff, net).Where(LoanInterest.IsPlausible).OrderBy(x => x.Days).ThenBy(x => x.RateBp).ToList();
            if (c.Count == 0)
            {
                // The figure may have been cut rather than rounded to the penny.
                var tr = LoanInterest.Solve(diff, net, truncate: true).Where(LoanInterest.IsPlausible).OrderBy(x => x.Days).ThenBy(x => x.RateBp).ToList();
                if (tr.Count > 0)
                    return new LoanDecode(DecodeStatus.Decodes, "split", diff, tr.Count == 1 ? tr[0].RateBp : null, tr.Count == 1 ? tr[0].Days : null, tr.Count, "penny TRUNCATED, not rounded; " + Alternatives(tr));
                var any = LoanInterest.Solve(diff, net, 1, 1000);
                string anyNote = any.Count == 0
                    ? "no (rate, term) at all reproduces it, rounded or cut"
                    : $"only non-standard terms fit ({any.Count}, e.g. {any[0].RateBp / 100m:0.00}%x{any[0].Days}d); not a term a dealer quotes";
                return new LoanDecode(DecodeStatus.DoesNotDecode, "split", diff, null, null, 0, "round principal published; no quoted-rate x standard-term Act/365 reproduction; " + anyNote);
            }
            return new LoanDecode(DecodeStatus.Decodes, "split", diff, c.Count == 1 ? c[0].RateBp : null, c.Count == 1 ? c[0].Days : null, c.Count, Alternatives(c));
        }
        var hits = new List<(decimal p, LoanTermRate t)>();
        for (decimal p = 1000000m; p <= 20000000m; p += 1000000m)
            foreach (var t in LoanInterest.Solve(p, net).Where(x => LoanInterest.IsPlausible(x) && x.Days >= 364 && x.Days <= 366))
                hits.Add((p, t));
        if (hits.Count == 0 && gross == net && net >= 1000000m)
        {
            // A single figure of a million or more is principal plus interest in one number (Bracknell 5,046,460.27 = 5m + 46,460.27).
            foreach (var basePound in new[] { 1000000m, 500000m })
            {
                decimal pr = Math.Floor(net / basePound) * basePound, intr = net - pr;
                if (pr < 1000000m || intr <= 0.01m || intr >= pr * 0.05m) continue;
                var c2 = LoanInterest.Solve(pr, intr).Where(LoanInterest.IsPlausible).OrderBy(x => x.Days).ThenBy(x => x.RateBp).ToList();
                if (c2.Count > 0)
                    return new LoanDecode(DecodeStatus.DecodesPrincipalUnpublished, "single-figure principal+interest", pr, c2.Count == 1 ? c2[0].RateBp : null, c2.Count == 1 ? c2[0].Days : null, c2.Count,
                        $"figure read as {pr:N0} + {intr:N2}; principal inferred, not published; " + Alternatives(c2));
            }
        }
        if (hits.Count == 0) return new LoanDecode(DecodeStatus.DoesNotDecode, "interest-only", null, null, null, 0, "principal unpublished; no 1m..20m (step 1m) x quoted rate x 364-366d reproduction");
        var first = hits.OrderBy(h => h.p).ThenBy(h => h.t.Days).First();
        return new LoanDecode(DecodeStatus.DecodesPrincipalUnpublished, "interest-only", hits.Count == 1 ? first.p : null, hits.Count == 1 ? first.t.RateBp : null, hits.Count == 1 ? first.t.Days : null, hits.Count,
            $"{hits.Count} fits, principal NOT published: " + string.Join("; ", hits.OrderBy(h => h.p).Take(4).Select(h => $"{h.p / 1000000m:0}m@{h.t.RateBp / 100m:0.00}%x{h.t.Days}d")) + (hits.Count > 4 ? "; ..." : ""));
    }

    private static string Alternatives(List<LoanTermRate> c) =>
        c.Count == 1 ? "unique" : $"{c.Count} fits (rate x term is a hyperbola): " + string.Join("; ", c.Take(6).Select(x => $"{x.RateBp / 100m:0.00}%x{x.Days}d")) + (c.Count > 6 ? "; ..." : "");

    public static LoanDecode ForEntry(string council, string counterparty, decimal gross, decimal net)
    {
        var cls = Classify(council, counterparty);
        if (IsRe3WastePfi(council, counterparty)) return new LoanDecode(DecodeStatus.NotApplicable, "re3-waste-pfi", null, null, null, 0, "re3 Waste PFI instalment, not a loan");
        if (cls is CounterpartyClass.Broker or CounterpartyClass.Other or CounterpartyClass.OwnCouncil)
            return new LoanDecode(DecodeStatus.NotApplicable, "fee-or-other", null, null, null, 0, "counterparty class " + cls + ": not a lender");
        return Decode(gross, net);
    }

    /// <summary>Null baseline for the interest-only branch: the share of random interest figures (uniform
    /// <paramref name="lo"/>..<paramref name="hi"/>, rounded to the penny, seeded) that still "decode" under the
    /// 1m..20m x quoted-rate x 364-366d search. If real interest-only lines decode far more often than this, it is not luck.</summary>
    public static double NullHitRateInterestOnly(decimal lo = 5000m, decimal hi = 600000m, int draws = 3000, int seed = 777)
    {
        var rng = new Random(seed);
        int hit = 0;
        for (int i = 0; i < draws; i++)
        {
            decimal amt = Math.Round(lo + (decimal)rng.NextDouble() * (hi - lo), 2);
            if (Decode(amt, amt).Status == DecodeStatus.DecodesPrincipalUnpublished) hit++;
        }
        return (double)hit / draws;
    }
}
