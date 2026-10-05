using System.Globalization;

namespace CouncilAudit;

/// <summary>
/// Session 42 (owner direction: payments to private social care providers, Wokingham only). Pure functions behind the
/// `socialcare` command; every rule here was measured in PREREG_S42_SOCIALCARE.md against a null before it was coded, and the
/// tests use the real Wokingham numbers it was found on.
/// </summary>
public static class SocialCare
{
    /// <summary>One published payment line in the care population.</summary>
    public sealed record CareLine(string Year, string TransactionId, string Supplier, DateOnly PayDate, DateOnly? InvoiceDate,
        decimal Net, string VatType, string CareType, string CostCentre);

    // ------------------------------------------------------------------ weekly rates
    /// <summary>Sevenths signature (Amendment 2): a care invoice priced as a weekly rate times days / 7 is a whole number of
    /// sevenths of a pound, to the penny. Whole-pound amounts are excluded (their period cannot be read). Under uniform pence the
    /// chance is 6/99; Wokingham's nursing lines show 53.9% and residential 34.6%, home care 3.6% (hourly, not weekly).</summary>
    public static bool IsSevenths(decimal amount)
    {
        amount = Math.Round(amount, 2);
        if (amount == Math.Truncate(amount)) return false;
        decimal k = amount * 7m;
        return Math.Abs(k - Math.Round(k)) <= 0.035m;
    }

    /// <summary>The weekly rate a sevenths-priced line implies when its period is the calendar month BEFORE the pay month
    /// (invoices in arrears; that prior gave a whole-pound rate for 74% of Wokingham's sevenths nursing lines against 23% for the
    /// pay month itself). Null when the line is not sevenths-priced or the rate under that prior is not a whole pound.</summary>
    public static (int Weekly, int Days)? WeeklyRate(decimal amount, DateOnly payDate)
    {
        if (!IsSevenths(amount)) return null;
        var prev = payDate.AddMonths(-1);
        int days = DateTime.DaysInMonth(prev.Year, prev.Month);
        decimal k = Math.Round(Math.Round(amount, 2) * 7m);
        decimal w = k / days;
        if (w != Math.Truncate(w) || w <= 0) return null;
        return ((int)w, days);
    }

    // ------------------------------------------------------------------ Companies House dates
    public enum CompanyDateFlag { None, PaidBeforeIncorporation, PaidAfterDissolution }

    /// <summary>Compares a payee's first and last payment with the incorporation and dissolution dates of the company its
    /// published name was matched to. A flag says only that the DATES do not fit the matched company; the first innocent reading
    /// is always a name match to the wrong company (a later company re-using the name, or a dormant shell whose name a group
    /// trades under). In Wokingham the rate of such hits was no higher for care than for non-care suppliers (N5 failed).</summary>
    public static CompanyDateFlag CheckCompanyDates(DateOnly firstPay, DateOnly lastPay, DateOnly? incorporated, DateOnly? dissolved)
    {
        if (incorporated is { } inc && firstPay < inc) return CompanyDateFlag.PaidBeforeIncorporation;
        if (dissolved is { } dis && lastPay > dis) return CompanyDateFlag.PaidAfterDissolution;
        return CompanyDateFlag.None;
    }

    // ------------------------------------------------------------------ same invoice date, same amount, different pay dates
    public sealed record InvoiceDateRepeat(string Supplier, decimal Net, DateOnly InvoiceDate, IReadOnlyList<CareLine> Lines)
    {
        public int Copies => Lines.Count;
        public decimal ValueBeyondFirst => Net * (Lines.Count - 1);
        public bool VatCodesDiffer => Lines.Select(l => l.VatType).Distinct(StringComparer.Ordinal).Count() > 1;
    }

    /// <summary>Lines of one supplier with the same Net and the same invoice (transaction) date under two or more transaction
    /// numbers, paid on two or more different pay dates. A same-day batch of equal per-client invoices is NOT this shape (it has
    /// one pay date). Measured on all of Wokingham's lines of 10,000 or more (post hoc, from the Bridges Home Care case): 208
    /// groups in all, 25 in care; with round thousands only, 7 groups in all and 1 in care. Innocent readings: termly or block
    /// invoices raised on one date for several periods; two separate payments (a grant and an on-account payment) entered with the
    /// same date.</summary>
    public static List<InvoiceDateRepeat> FindInvoiceDateRepeats(IEnumerable<CareLine> lines, decimal minNet, bool roundThousandsOnly)
    {
        var res = new List<InvoiceDateRepeat>();
        foreach (var g in lines.Where(l => l.InvoiceDate is not null && l.Net >= minNet)
                     .GroupBy(l => (l.Supplier, l.Net, l.InvoiceDate!.Value)))
        {
            var perTxn = g.GroupBy(l => (l.Year, l.TransactionId)).Select(t => t.First()).OrderBy(l => l.PayDate).ToList();
            if (perTxn.Count < 2 || perTxn.Select(l => l.PayDate).Distinct().Count() < 2) continue;
            if (roundThousandsOnly && g.Key.Net % 1000m != 0m) continue;
            res.Add(new InvoiceDateRepeat(g.Key.Supplier, g.Key.Net, g.Key.Item3, perTxn));
        }
        return res.OrderByDescending(r => r.ValueBeyondFirst).ToList();
    }

    // ------------------------------------------------------------------ new providers taking large sums (S6)
    public sealed record NewProvider(string Supplier, DateOnly FirstPay, decimal FirstTwelveMonths, decimal Total);

    /// <summary>Suppliers whose first payment in the files is on or after <paramref name="notBefore"/> and whose first 12 months
    /// from that payment total at least <paramref name="threshold"/>. Wokingham care: 33 hits, 11.26 per GBP 100m against 7.73
    /// for non-care. Innocent readings: a new home or scheme, a rename (Multi-Care Reading Community Services became Happy at Home
    /// Community Care Services Ltd on 5 Oct 2023 at Companies House), a transferred contract, one high-cost child placement.</summary>
    public static List<NewProvider> FindNewProviders(IEnumerable<CareLine> lines, DateOnly notBefore, decimal threshold)
    {
        var res = new List<NewProvider>();
        foreach (var g in lines.GroupBy(l => l.Supplier))
        {
            var first = g.Min(l => l.PayDate);
            if (first < notBefore) continue;
            var end = first.AddMonths(12);
            decimal v12 = g.Where(l => l.PayDate < end).Sum(l => l.Net);
            if (v12 >= threshold) res.Add(new NewProvider(g.Key, first, v12, g.Sum(l => l.Net)));
        }
        return res.OrderByDescending(r => r.FirstTwelveMonths).ToList();
    }

    // ------------------------------------------------------------------ concentration
    /// <summary>Herfindahl-Hirschman index on percentage shares (0 to 10,000). Under 1,500 is usually read as unconcentrated.</summary>
    public static double Hhi(IEnumerable<decimal> supplierTotals)
    {
        var t = supplierTotals.Where(v => v > 0).ToList();
        decimal sum = t.Sum();
        if (sum <= 0) return 0;
        return t.Sum(v => Math.Pow((double)(100m * v / sum), 2));
    }

    // ------------------------------------------------------------------ population (PREREG_S42_SOCIALCARE.md, Amendment 1)
    private static readonly string[] AdultWords = { "Nursing", "Residential", "Domiciliary", "Domicilary", "Supported Living", "Shared Lives", "Day Care", "Respite", "Other Care" };

    /// <summary>Wokingham care type of a line from its service area, description and cost centre, or null if the line is not in
    /// the care population. Agency staff in adult or children's services is "Staffing"; otherwise the description must be a
    /// third-party payment ("TPP - ...") or an Individual Service Fund.</summary>
    public static string? CareType(string? serviceArea, string? description, string? costCentre)
    {
        string svc = serviceArea ?? "", desc = description ?? "", cc = costCentre ?? "";
        bool adult = svc.StartsWith("Adult", StringComparison.Ordinal), kids = svc.StartsWith("Children", StringComparison.Ordinal);
        if (desc == "Agency Staff" && (adult || kids)) return adult ? "Staffing (adult)" : "Staffing (children)";
        if (!(desc.StartsWith("TPP - ", StringComparison.Ordinal) || desc == "Individual Service Fund")) return null;
        string ccN = cc.Replace('´', '\'').Replace('’', '\'');
        if (!adult)
        {
            if (ccN.Contains("Children's Homes Purchasing") || ccN.Contains("Children's External Residential")) return "Children residential";
            if (ccN.Contains("Independent Fostering Agency") || ccN.Contains("UASC IFA")) return "Fostering agency";
            if (ccN.Contains("18+ Care Leavers") || ccN.Contains("UASC 18") || ccN.Contains("Staying Close")) return "Care leavers / 18+";
            if (ccN.Contains("Semi Independent") || ccN.Contains("UASC")) return "Semi-independent";
            return null;
        }
        foreach (var w in AdultWords)
        {
            if (!cc.Contains(w, StringComparison.Ordinal)) continue;
            return w switch
            {
                "Nursing" => "Nursing",
                "Residential" => "Residential",
                "Domiciliary" or "Domicilary" => "Home care",
                "Supported Living" or "Shared Lives" => "Supported living",
                _ => "Day / respite / other",
            };
        }
        if (desc == "TPP - WBC Domicilary Care (to be Recharged to Health)") return "Home care";
        if (desc == "TPP - Self Funded Care" || desc == "TPP - Continuing Care") return "Adult other";
        return null;
    }

    /// <summary>Council-owned companies are a comparison group, never the target (Optalis: Wokingham, and since 2017 RBWM).</summary>
    public static bool IsCouncilOwned(string supplier) => supplier.StartsWith("Optalis", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] PublicWords = { "NHS", "Council", "Borough", "Integrated Care", "Foundation Trust", "Health Authority", "CCG", "ICB" };
    public static bool IsPublicBody(string supplier) =>
        PublicWords.Any(w => System.Text.RegularExpressions.Regex.IsMatch(supplier, @"\b" + System.Text.RegularExpressions.Regex.Escape(w) + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public static string FinancialYear(DateOnly d)
    {
        int s = d.Month >= 4 ? d.Year : d.Year - 1;
        return "FY" + s.ToString(CultureInfo.InvariantCulture) + "-" + ((s + 1) % 100).ToString("00", CultureInfo.InvariantCulture);
    }
}
