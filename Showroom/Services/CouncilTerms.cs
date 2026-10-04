using System.Globalization;

namespace Showroom.Services;

/// <summary>Plain-English wording and number formatting shared by the Council Spending pages. Audit terms only; every line states a fact
/// about the published numbers and never a cause.</summary>
public static class CouncilTerms
{
    // invariant formatting (comma thousands, dot decimals, English month names) is what UK readers expect and avoids loading culture data on first use
    static readonly CultureInfo GB = CultureInfo.InvariantCulture;

    public static string Gbp(decimal v)
    {
        var a = Math.Abs(v);
        string s = a >= 10_000_000m ? (a / 1_000_000m).ToString("#,##0.0", GB) + "m"
            : a >= 1_000_000m ? (a / 1_000_000m).ToString("#,##0.00", GB) + "m"
            : a >= 10_000m ? a.ToString("#,##0", GB)
            : a.ToString("#,##0.00", GB);
        return (v < 0 ? "-" : "") + "£" + s;
    }

    public static string Num(long n) => n.ToString("#,##0", GB);
    /// <summary>"1 line", "2 lines", "1,204 lines": the count with its noun in the right number.</summary>
    public static string Plural(long n, string one, string many) => Num(n) + " " + (n == 1 ? one : many);
    public static string NumLines(long n) => Plural(n, "line", "lines");
    public static string Pct(decimal fraction) => (fraction * 100m).ToString("0.##", GB) + "%";

    /// <summary>Pay dates arrive as ISO text, as dd-MMM-yy, or as a spreadsheet day-number (Reading's older files); show all as "4 May 2021".</summary>
    public static string Date(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "no date";
        return TryDate(s, out var d) ? d.ToString("d MMM yyyy", GB) : s;
    }

    // Day first, as the councils and the scanner read them ("03/04/2020" is 3 April, never March 4); the framework's invariant parser would read it month first.
    static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yy", "dd-MMM-yy", "dd-MMM-yyyy", "d MMM yyyy", "d-MMM-yy", "yyyyMMdd",
    };

    public static bool TryDate(string s, out DateTime d)
    {
        d = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            if (serial > 20000 && serial < 80000) { d = DateTime.FromOADate(serial); return true; }
            if (s.Length != 8) return false;   // a plain number that is not a day-number or yyyyMMdd is not a date
        }
        return DateTime.TryParseExact(s, DateFormats, GB, DateTimeStyles.None, out d);
    }

    /// <summary>"2024-03" to "March 2024"; anything else (such as "undated") is returned as is.</summary>
    public static string MonthName(string m) =>
        DateTime.TryParseExact(m, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMMM yyyy", GB) : m == "undated" ? "Rows with no readable date" : m;

    public static string Display(string c) => c switch
    {
        "DoubleListing" => "Double listing", "DebtCharge" => "Debt charge",
        "EarlyPaymentProgramme" => "Early payment programme", "VatRoundingNoise" => "VAT rounding noise",
        "NegativeNetSignFlip" => "Net sign flip", "LikelyDuplicate" => "Likely duplicate",
        "LikelyRecurring" => "Likely recurring", "MatchedOffsettingCharge" => "Matched offsetting charge",
        "StandingScheduleCatchUp" => "Standing payment, caught up", "StandingScheduleSurplus" => "Standing payment, surplus",
        "StandingPaymentUnderRefundType" => "Standing payment under refund label",
        "ReversedSameDay" => "Copies cancelled the same day", "MigrationControlLabel" => "Council's own migration label",
        "NSquaredListing" => "Publication quirk: rows printed n times",
        "" => "No classification", _ => c,
    };

    /// <summary>The pattern readings the engine attaches to a still-open group (ExplainedBy). A reading never removes a group from the open count and is never a verdict.</summary>
    public static string ReadingTag(string by) => by switch
    {
        "RecurringBatchRate" => "Pattern reading: recurring batch rate",
        "CadenceCatchUp" => "Pattern reading: monthly payments, caught up",
        "NSquaredRows" => "Publication quirk: rows printed n times",
        _ => "Pattern reading: " + by,
    };

    /// <summary>The caption of a group's reading. A group the council's report printed n times (NSquaredRows) gets the engine's plain words and then its own
    /// figures (the de-duplicated value, with the first letter capitalised); every other reading is the row's own sentence, or the standard one.</summary>
    public static string ReadingCaption(string by, string own)
    {
        if (by == "NSquaredRows")
        {
            string t = own.Length > 0 ? char.ToUpperInvariant(own[0]) + own[1..] : "";
            return ReadingMeaning(by) + (t.Length > 0 ? " " + t : "");
        }
        return own.Length > 0 ? own : ReadingMeaning(by);
    }

    /// <summary>Used when a row carries the flag but no text of its own; otherwise the row's own ExplainedMeaning is the caption.</summary>
    public static string ReadingMeaning(string by) => by switch
    {
        "RecurringBatchRate" => "Same amount paid in batches on many separate dates, as a per-head rate or a roll of placements is; this batch is no larger than the biggest that amount has formed before. A consistent reading, not a proof.",
        "CadenceCatchUp" => "This supplier's ledger line is paid about once a month in steady sums; this month's payments are no more than the months since the last one would account for. A consistent reading, not a proof.",
        "NSquaredRows" => "This payment run prints each payee's lines as many times as the payee has lines. The row total is not money; the stated invoice amount is the run.",
        _ => "",
    };

    /// <summary>Shown under a repeat group in a care cost centre (Wokingham), where a care home or agency bills each resident separately.</summary>
    public const string CareCaption =
        "Care homes and agencies bill each resident separately, so equal amounts on one day are normal. The published file has no client or period column, so it cannot tell two residents from one bill paid twice.";

    /// <summary>Unexplained classes lead; the explained ones follow, with the same-day and migration-label explanations below "Unclear".</summary>
    public static int Rank(string c) => c switch
    {
        "Unreconciled" => 0, "StandingScheduleSurplus" => 1, "Unclear" => 2, "Unmatched" => 3,
        "ReversedSameDay" => 10, "MigrationControlLabel" => 11, _ => 5,
    };
    public static bool IsUnexplained(string c) => c is "Unreconciled" or "Unclear" or "Unmatched" or "StandingScheduleSurplus" or "";

    public static string Meaning(string c) => c switch
    {
        "Unreconciled" => "No specific pattern was recognised: the published numbers simply do not add up under any of the rules. This is the actual target: ask the council for the invoice and the reason.",
        "DoubleListing" => "The same invoice line, or the whole itemised invoice, appears to have been published more than once (the lines sum to an exact whole-number multiple of the stated invoice, or of it once VAT is added), rather than several genuinely different charges.",
        "DebtCharge" => "The stated amount splits exactly into a round principal plus a non-round interest or charge remainder: consistent with a loan repayment or debt-recovery invoice, not an unexplained gap.",
        "EarlyPaymentProgramme" => "A batch of negative early-payment discount fees under the council's own named scheme (a supply-chain-finance arrangement): a known pattern, not an arithmetic mismatch.",
        "VatRoundingNoise" => "The gap is a few pence, consistent with VAT being rounded per line and then summed, rather than once on the total.",
        "NegativeNetSignFlip" => "The published net figure is the exact negative of the value needed to reconcile the stated invoice. Reported as a named, recurring shape, not an explanation: why the sign is inverted is not established.",
        "Unclear" => "No supplier invoice number was available to check, so the repeat is reported exactly as published, with no verdict either way. A group may carry a pattern reading beside it; the reading is a consistent fit, not a proof, and the group stays open.",
        "LikelyDuplicate" => "Every member of this group shares the same supplier invoice number as well as the same amount: the strongest signal of a genuine duplicate payment.",
        "LikelyRecurring" => "Members carry different supplier invoice numbers despite the identical amount: consistent with a standardised recurring rate, not a repeat.",
        "Unmatched" => "No same-supplier published charge of matching magnitude was found within 365 days. Not established to be an error; reported as published.",
        "MatchedOffsettingCharge" => "A same-supplier published charge of matching magnitude exists within 365 days: a plausible explanation exists in the data, not a claim that this credit offsets that charge.",
        "StandingScheduleCatchUp" => "This amount is paid about once a month, and this date carries more than one payment because earlier months have none. Counting payments against months, the extra payments are made good by empty months: consistent with a late payment caught up, paid ahead or repaid; not proof of any of them.",
        "StandingScheduleSurplus" => "This amount is paid about once a month, but there are more payments than months and nothing returned in the published data. More payments than months in the published data; the files do not say why. Still a lead, so it ranks with the unexplained rows.",
        "StandingPaymentUnderRefundType" => "No matching charge was found, but the same amount recurs every month under the council's own refund label. What the council means by the label is not stated in the file.",
        "ReversedSameDay" => "The same supplier is also shown with equal-and-opposite lines on the same date, which cancel the extra copies: the lines net to one payment. An explanation of a repeat, not a finding.",
        "NSquaredListing" => "Publication quirk, not extra money: the distinct lines add up to the stated amount to the penny. This payment run prints each payee's lines as many times as the payee has lines, so the row total is not money; the stated invoice amount is the run.",
        "MigrationControlLabel" => "Every line carries the council's own label \"AP MIGRATION CONTROL ACCOUNT\": a ledger-migration control account. The file does not say whether cash left under it. Not a finding.",
        _ => "",
    };

    /// <summary>"2024-03-05", "05-Mar-24" or a spreadsheet day-number as the file's month key "2024-03"; "" when the date cannot be read.</summary>
    public static string MonthKey(string s) => TryDate(s, out var d) ? d.ToString("yyyy-MM", GB) : "";

    public static string ScheduleTitle(string s) => s switch
    {
        "A" => "A payment that does not match its stated invoice amount",
        "B" => "The same supplier, amount, description and date more than once",
        "D" => "One transaction number covering several payees or pay dates",
        _ => "Schedule " + s,
    };

    public static string ScheduleDefinition(string s) => s switch
    {
        "A" => "The payments published for one transaction add up to more or less than the invoice amount the file states for it, once VAT is allowed for. A fact about the published figures, not a verdict.",
        "B" => "Lines that share a supplier, an amount, a description and a pay date. Some are explained by a recognised pattern (a standing monthly payment, an exact reversal the same day); the rest are listed as published.",
        "D" => "The same transaction number appears against more than one payee, or more than one pay date, in the published data. Where a council's own transaction numbers are supplier-furnished invoice numbers, two suppliers can simply have used the same number; this is not read as one payment going to two payees.",
        _ => "",
    };

    /// <summary>Schedules that cannot run for a council because its file has no VAT split (A) or no transaction reference (D). "Not available", never "clean".</summary>
    public static readonly HashSet<string> NoScheduleA = new()
        { "reading", "bracknellforest", "westberkshire", "rbwm", "birmingham", "leeds", "sheffield", "bradford", "liverpool", "bristol",
          "wakefield", "coventry", "durham", "kirklees", "leicester", "cornwall", "nottingham", "wirral", "newcastle", "surrey", "essex", "hertfordshire", "stockport", "york" };
    // durham, kirklees, newcastle and hertfordshire: every transaction number sits against one payee on one date, so the shared-number check is empty by construction in the profiles
    public static readonly HashSet<string> NoScheduleD = new() { "birmingham", "westberkshire", "sheffield", "leeds", "durham", "kirklees", "newcastle", "surrey", "essex", "hertfordshire", "stockport" };
    /// <summary>The members of NoScheduleD that do publish a number: the shared-number check is empty because of how the numbers are given, not because they are missing.</summary>
    public static readonly HashSet<string> NoDByNumbering = new() { "durham", "kirklees", "newcastle", "hertfordshire" };
    /// <summary>Councils whose file publishes no transaction number at all (the scanner gives each row a placeholder "(no number published) N"): every check that keys on a transaction number cannot run.</summary>
    public static readonly HashSet<string> NoTransactionNumber = new() { "surrey", "essex" };

    /// <summary>Stockport publishes a transaction number only from April 2025 (17 of its 115 monthly files, to August 2026): not in NoTransactionNumber, because the
    /// two number-keyed cross-council checks DO run on those months. Before then they cannot run, which the year caption and CannotCheck say.</summary>
    public static bool HasNumber(string slug, string year) =>
        slug == "stockport" ? year.Length == 7 && year[4] == '-' && string.CompareOrdinal(year, "2025-26") >= 0 : !NoTransactionNumber.Contains(slug);

    /// <summary>City of York: in 2011/12 to 2017/18 the transaction number is a payment-run reference shared by unrelated payees paid on the same day, and in 2024/25 each
    /// "202425CRCR" number is shared by two lines of different payees. In those years Schedule D reads the numbering, not the payments.</summary>
    public static bool YorkSharedNumbering(string slug, string year) =>
        slug == "york" && year.Length == 7 && year[4] == '-' && (string.CompareOrdinal(year, "2018-19") < 0 || year == "2024-25");

    /// <summary>A caption for the shared-transaction-number block when a council's numbering, not its payments, produces the groups; null otherwise.</summary>
    public static string? NumberingNote(string slug, string sch, string year)
    {
        if (sch != "D" || !YorkSharedNumbering(slug, year)) return null;
        return year == "2024-25"
            ? "Read this block as a fact about the numbering, not about the payments. In this year's file the numbers beginning \"202425CRCR\" are each used by exactly two lines of different payees, so a group here is two unrelated lines that were given the same number. It is not one payment going to two payees, and no group below is an exception on that ground."
            : "Read this block as a fact about the numbering, not about the payments. In this year's file the transaction number is a payment-run reference, shared by unrelated payees paid on the same day, so a group here is a payment run, not one payment going to several payees. No group below is an exception on that ground.";
    }

    /// <summary>The "not available" line of a Schedule A or D block for the one council whose answer depends on the year (Stockport); null for every other council.</summary>
    public static string? NotAvailable(string slug, string sch, string year)
    {
        if (slug != "stockport") return null;
        if (sch == "A")
            return "Not available for this council: its published file has one amount column (Gross equals Net, no VAT split), so there is no stated invoice amount to compare the payments with, and this rule cannot run. That is a limit of the file, not a clean result.";
        return HasNumber(slug, year)
            ? "Not available for most of this council's history, and nothing found here: its file gives a transaction number only from April 2025. In the months that carry one, this rule found no number against more than one payee or pay date. That is a limit of the file, not a sign the rule was clean for earlier years."
            : "Not available for this council: its published file has no transaction number before April 2025, so there is no reference to group by and this rule cannot run. That is a limit of the file, not a clean result.";
    }

    /// <summary>The reason a cross-council check cannot run for this council, or null when it can. Said on the page instead of "no rows", which would read as a clean result.</summary>
    public static string? CheckCannotRun(string slug, string checkId) =>
        NoTransactionNumber.Contains(slug) && checkId is "twins" or "withintxn"
            ? "This check cannot run for this council: its file publishes no transaction number, and this check compares transaction numbers. That is a limit of the file, not a clean result."
            : null;

    /// <summary>Plain statements, shown at the top of a council's page, of what cannot be checked for it and why. Nothing here says anything about the payments themselves.</summary>
    public static List<string> CannotCheck(string slug)
    {
        var l = new List<string>();
        if (NoTransactionNumber.Contains(slug))
        {
            l.Add("The invoice-amount check and the shared-transaction-number check cannot run: the file publishes no transaction number, so one transaction's payments cannot be added up or compared. They are listed under each year as \"not available\", not as clean.");
            l.Add("Two cross-council checks need a transaction number and cannot run either: transactions published twice under two numbers, and the same line repeated inside one transaction.");
            l.Add("The repeated-payment check is the only repeat test that can run, and it has no transaction number to tell two payments apart: a group is the same payee, amount and description on the same date.");
        }
        else if (slug == "hertfordshire")
        {
            l.Add("The invoice-amount check cannot run: the file has one amount column (Net Amount), so there is no stated invoice or gross amount to compare the payments with.");
            l.Add("The shared-transaction-number check finds nothing by construction: the file gives each transaction number to one payee on one date. That is a result of how the numbers are given, not a sign the check was clean.");
        }
        else if (slug == "stockport")
        {
            l.Add("The invoice-amount check cannot run: the file has one amount column (Gross equals Net, no VAT split), so there is no stated invoice amount to compare the payments with.");
            l.Add("No transaction number is published before April 2025 (the 17 months from April 2025 to August 2026 carry one). For every earlier month the shared-transaction-number check cannot run, and neither can the two cross-council checks that need a number: transactions published twice under two numbers, and the same line repeated inside one transaction. They are listed as not available, not as clean. In the months with a number they run, and the transaction-number check found no number against more than one payee or pay date.");
            l.Add("In 66 of the 115 monthly files the only date is the invoice date, with no payment date. A repeated-payment group there is the same payee, amount and description under the same invoice date, which is not the same day of payment, so same-payment-day readings are limited for this council.");
        }
        else if (slug == "york")
        {
            l.Add("The invoice-amount check cannot run: the file has one amount column (Net Amount), so there is no stated invoice amount to compare the payments with.");
            l.Add("The shared-transaction-number check reads the numbering, not the payments, in two stretches. From 2011/12 to 2017/18 the transaction number is a payment-run reference shared by unrelated payees paid on the same day, and in 2024/25 each number beginning \"202425CRCR\" is shared by two lines of different payees. The groups it finds there are a result of how the numbers are given, not a sign of any payment going to two payees, and no test that keys on the number can be read for those years. In 2018/19 to 2023/24 and 2025/26 every number is one line and the check finds nothing.");
            l.Add("Counts are not comparable across years: the file has 37,522 rows in 2011/12, 88,575 in 2012/13 (with almost the same total), 164,221 in 2013/14, then 139,000 to 146,000 until a drop in December 2016, and 56,000 to 68,000 a year since. The thresholds in the file names also change (over GBP 500, all payments, over GBP 250). From 2011/12 to 2014/15 the single amount column does not say whether it includes VAT.");
            l.Add("The share of rows whose payee the council redacts varies from 0.9% to 26.0% by year (17.8% in 2012/13, 3.4% in 2025/26). Those lines are left out of every check and only counted. The checks say nothing about them.");
        }
        if (slug is "surrey" or "essex" or "hertfordshire" or "stockport" or "york")
            l.Add("The declared-spend comparison cannot run: no government Revenue Outturn figure is held here for this council, so no year is set against the file total, and this council is in none of the groups of the pre-registered test.");
        if (slug == "stockport")
            l.Add("About a third of the rows (31.6%) have a payee the council redacts or labels \"*Exclude\" (one label standing for unrelated payments). Those lines are left out of every check and only counted. The checks say nothing about them.");
        if (slug is "surrey" or "essex" or "hertfordshire")
            l.Add(slug == "essex"
                ?"Lines whose payee is a pooled label (one label standing for many people, such as a foster care payment) are shown by the scanner as \"Redacted (pooled label): <label>\" and are left out of every check. The checks say nothing about those lines."
                : "Lines whose payee the council redacts are left out of every check and only counted. The checks say nothing about those lines.");
        return l;
    }

    /// <summary>A caption for one financial year of a council when its publication changes inside or at the edge of the year; null otherwise.</summary>
    public static string? YearNote(string slug, string year)
    {
        if (year.Length != 7 || year[4] != '-') return null;   // "undated" is also seven characters
        if (slug == "stockport")
            return string.CompareOrdinal(year, "2025-26") >= 0
                ? "From April 2025 this council's file gives a transaction number and, from then, a payment date as well as an invoice date, and, by the council's own account, lists all spend (monthly rows step up from about 22,900 to about 36,000). Counts and totals from this year are not comparable with earlier years."
                : "This council's file gives no transaction number before April 2025, and in many months only an invoice date, not a payment date. Counts and totals here are not comparable with later years.";
        if (slug == "york")
            return "Row counts are not comparable across this council's years: the file's size, layout and threshold change from year to year (see the notes at the top of this page).";
        if (slug != "hertfordshire") return null;
        return string.CompareOrdinal(year, "2025-26") >= 0
            ? "From April 2025 this council's file lists payments over £500; before that it listed payments over £250. Counts and totals from this year are not comparable with earlier years."
            : string.CompareOrdinal(year, "2024-25") == 0
                ? "This council's file lists payments over £250 up to March 2025; from April 2025 it lists payments over £500. Counts and totals are not comparable with later years."
                : "This council's file lists payments over £250; from April 2025 it lists payments over £500. Counts and totals are not comparable with later years.";
    }

    public const string DiscrepancyNote =
        "A discrepancy here is a fact about the published numbers, not proof of an error or wrongdoing. Councils publish corrections, instalments, and VAT treatments that can look like a mismatch until explained. If you intend to raise this with the council or its auditor, ask for the records and the reason, not for an admission.";

    public const string OglCredit =
        "Contains public sector information licensed under the Open Government Licence v3.0. Council payment data is published by each council under the Local Government Transparency Code; declared figures are the government's Revenue Outturn and Capital Outturn statistics.";
}
