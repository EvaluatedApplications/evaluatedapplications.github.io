namespace Showroom.Services;

/// <summary>
/// The only per-council wording on the Council Spending pages: PROSE that cannot be derived from the export (the name of a council's amount column, why a
/// council's numbering reads the way it does, a change of threshold, a count taken from reading the files by hand). Everything else is built from the
/// council's measured facts (CouncilFacts) and needs no entry here, so a new council needs none unless it has prose of this kind.
/// An entry only changes WHAT IS SAID; whether a check can run, and for which years, still comes from the facts.
/// <c>CannotCheck</c> is an ordered template for the page-top list: the tokens {A} {number} {budget} {redaction} expand to the derived line, anything else is a literal line.
/// </summary>
public sealed record CouncilOverride(
    string? ALine = null,                       // replaces the derived "invoice-amount check cannot run" line (names the amount column)
    string? NotAvailableA = null,               // replaces the per-year "not available" line of the invoice-amount block
    bool? DByNumbering = null,                  // forces the wording of the shared-number block ("finds nothing by construction" vs "cannot run") where the derived reading is a judgement call
    string[]? CannotCheck = null,               // ordered template for the page-top list (default {A} {number} {budget} {redaction})
    Func<string, string?>? YearNote = null,     // financial year -> caption, or null
    Func<string, string?>? NumberingNote = null);  // financial year with Schedule D rows -> caption above the block

public static class CouncilOverrides
{
    public static CouncilOverride? For(string slug) => Map.TryGetValue(slug, out var o) ? o : null;

    static readonly Dictionary<string, CouncilOverride> Map = new()
    {
        // Durham's number is a ledger reference with a line counter in it ("4560613-00584"), one per row, so the measured facts read it like Leeds's per-file counter. The page has always said of
        // Durham that every number sits on one payee and one date; that wording is kept.
        ["durham"] = new(DByNumbering: true),

        ["hertfordshire"] = new(
            ALine: "The invoice-amount check cannot run: the file has one amount column (Net Amount), so there is no stated invoice or gross amount to compare the payments with.",
            YearNote: y => string.CompareOrdinal(y, "2025-26") >= 0
                ? "From April 2025 this council's file lists payments over £500; before that it listed payments over £250. Counts and totals from this year are not comparable with earlier years."
                : y == "2024-25"
                    ? "This council's file lists payments over £250 up to March 2025; from April 2025 it lists payments over £500. Counts and totals are not comparable with later years."
                    : "This council's file lists payments over £250; from April 2025 it lists payments over £500. Counts and totals are not comparable with later years."),

        ["stockport"] = new(
            ALine: "The invoice-amount check cannot run: the file has one amount column (Gross equals Net, no VAT split), so there is no stated invoice amount to compare the payments with.",
            NotAvailableA: "Not available for this council: its published file has one amount column (Gross equals Net, no VAT split), so there is no stated invoice amount to compare the payments with, and this rule cannot run. That is a limit of the file, not a clean result.",
            CannotCheck: new[]
            {
                "{A}", "{number}",
                "In 66 of the 115 monthly files the only date is the invoice date, with no payment date. A repeated-payment group there is the same payee, amount and description under the same invoice date, which is not the same day of payment, so same-payment-day readings are limited for this council.",
                "{budget}",
                "About a third of the rows (31.6%) have a payee the council redacts or labels \"*Exclude\" (one label standing for unrelated payments). Those lines are left out of every check and only counted. The checks say nothing about them.",
            },
            YearNote: y => string.CompareOrdinal(y, "2025-26") >= 0
                ? "From April 2025 this council's file gives a transaction number and, from then, a payment date as well as an invoice date, and, by the council's own account, lists all spend (monthly rows step up from about 22,900 to about 36,000). Counts and totals from this year are not comparable with earlier years."
                : "This council's file gives no transaction number before April 2025, and in many months only an invoice date, not a payment date. Counts and totals here are not comparable with later years."),

        ["york"] = new(
            ALine: "The invoice-amount check cannot run: the file has one amount column (Net Amount), so there is no stated invoice amount to compare the payments with.",
            CannotCheck: new[]
            {
                "{A}",
                "The shared-transaction-number check reads the numbering, not the payments, in two stretches. From 2011/12 to 2017/18 the transaction number is a payment-run reference shared by unrelated payees paid on the same day, and in 2024/25 each number beginning \"202425CRCR\" is shared by two lines of different payees. The groups it finds there are a result of how the numbers are given, not a sign of any payment going to two payees, and no test that keys on the number can be read for those years. In 2018/19 to 2023/24 and 2025/26 every number is one line and the check finds nothing.",
                "Counts are not comparable across years: the file has 37,522 rows in 2011/12, 88,575 in 2012/13 (with almost the same total), 164,221 in 2013/14, then 139,000 to 146,000 until a drop in December 2016, and 56,000 to 68,000 a year since. The thresholds in the file names also change (over GBP 500, all payments, over GBP 250). From 2011/12 to 2014/15 the single amount column does not say whether it includes VAT.",
                "The share of rows whose payee the council redacts varies from 0.9% to 26.0% by year (17.8% in 2012/13, 3.4% in 2025/26). Those lines are left out of every check and only counted. The checks say nothing about them.",
                "{budget}",
            },
            YearNote: _ => "Row counts are not comparable across this council's years: the file's size, layout and threshold change from year to year (see the notes at the top of this page).",
            NumberingNote: y => y == "2024-25"
                ? "Read this block as a fact about the numbering, not about the payments. In this year's file the numbers beginning \"202425CRCR\" are each used by exactly two lines of different payees, so a group here is two unrelated lines that were given the same number. It is not one payment going to two payees, and no group below is an exception on that ground."
                : "Read this block as a fact about the numbering, not about the payments. In this year's file the transaction number is a payment-run reference, shared by unrelated payees paid on the same day, so a group here is a payment run, not one payment going to several payees. No group below is an exception on that ground."),
    };
}
