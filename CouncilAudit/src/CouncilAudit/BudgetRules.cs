namespace CouncilAudit;

/// <summary>
/// Pure rules of the declared-spend reconciliation (PREREG_BUDGET_TEST.md), kept in the library so they have unit tests
/// and can run in the Showroom page: which financial year a payment date falls in, and which payees count as OBSERVED
/// pass-through or payroll-liability payments (named by payee, shown as their own line, never guessed).
/// </summary>
public static class BudgetRules
{
    /// <summary>English local-government financial year label for a date, e.g. 2024-03-31 gives "2023-24".</summary>
    public static string FinancialYear(DateTime d) =>
        d.Month >= 4 ? $"{d.Year}-{(d.Year + 1) % 100:00}" : $"{d.Year - 1}-{d.Year % 100:00}";

    /// <summary>
    /// The class of observed pass-through or payroll-liability payee, or null. Fixed in the pre-registration before any
    /// residual was computed. These payments sit in a payments file but are neither revenue running expenses nor
    /// capital spend, so they are named and added to the comparator rather than left in the unexplained remainder.
    /// </summary>
    public static string? ObservedClass(string supplierName)
    {
        string n = (supplierName ?? "").ToUpperInvariant();
        if (n.Contains("REVENUE & CUSTOMS") || n.Contains("REVENUE AND CUSTOMS") || n.Contains("HMRC") || n.Contains("H M REVENUE")) return "Payments to HMRC";
        if (n.Contains("PENSION")) return "Payments to pension funds";
        if (n.Contains("POLICE") && (n.Contains("COMMISSIONER") || n.Contains("AUTHORITY") || n.Contains("THAMES VALLEY") || n.Contains("CRIME"))) return "Police precept and other payments to the police authority";
        if (n.Contains("FIRE") && (n.Contains("RESCUE") || n.Contains("AUTHORITY"))) return "Payments to the fire authority";
        if (n.Contains("PARISH COUNCIL")) return "Payments to parish councils";
        if (n.Contains("TOWN COUNCIL")) return "Payments to town councils";
        return null;
    }

    /// <summary>
    /// POST HOC context classes, added in Session 29 AFTER the Stage 1 residuals were seen (Merton's file carries GBP 70m a
    /// year to the Greater London Authority and about GBP 10m each to schools; Reading's 2019-20 file carries GBP 93m
    /// into money-market funds). They are shown beside the pre-registered lines so a reader can see where the file's money
    /// goes, and are never part of the pre-registered statistic. Checked in order; a payee already named by
    /// <see cref="ObservedClass"/> is not repeated here.
    /// </summary>
    public static string? ContextClass(string supplierName)
    {
        string n = (supplierName ?? "").ToUpperInvariant();
        if (n.Contains("REDACT")) return "Payee withheld by the council (redacted rows with an amount)";
        if (n.Contains("GREATER LONDON AUTHORITY")) return "Greater London Authority (precept paid over)";
        if (n.Contains("LIQUIDITY FUND") || n.Contains("MANAGED STERLING") || n.Contains("FEDERATED INVESTORS") || n.Contains("PUBLIC WORKS LOAN"))
            return "Treasury: investment funds and loan accounts (balance-sheet movements)";
        if (n.Contains("SCHOOL") || n.Contains("ACADEMY") || n.Contains("COLLEGE")) return "Schools, academies and colleges";
        if (n.Contains("NHS") || n.Contains("INTEGRATED CARE") || n.Contains(" ICB")) return "NHS bodies";
        if (n.Contains("COUNCIL") || n.Contains("BOROUGH OF") || n.Contains("CITY OF ") || n.Contains("METROPOLITAN")) return "Other local authorities (and the council's own name)";
        return null;
    }
}
