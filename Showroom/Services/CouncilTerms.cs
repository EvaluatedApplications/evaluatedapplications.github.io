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

    static readonly string[] Ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
    static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
    /// <summary>A count in words for running prose ("twenty-seven"); digits above ninety-nine. The council count is computed, never typed.</summary>
    public static string Words(int n) => n < 0 || n > 99 ? Num(n) : n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 == 0 ? "" : "-" + Ones[n % 10]);

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

    // ----------------------------------------------------------------------------------------------------------------------------------
    // What cannot be checked. Nothing below names a council: every statement is built from the FACTS the builder measured from the council's own export and wrote
    // into its profile (CouncilFacts: no stated invoice amount, no Schedule D row, no transaction number, numbers from one month on, per-file counters, no
    // declared-spend unit). The only per-council wording left is CouncilOverrides: prose that cannot be derived (a column's name, York's numbering, a threshold change).
    // ----------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The page-top "What cannot be checked" list is built for the councils the declared-spend test does not cover (no unit has a published Revenue Outturn): the
    /// shape every page has had since that list was added. A council the test does cover states the same invoice-amount and shared-number limits per year instead. Set true
    /// to print the derived list for every council (the older councils would then each gain a few lines that repeat what their year blocks already say).</summary>
    public const bool ListForEveryCouncil = false;

    /// <summary>True when this schedule cannot run for this council ("A": no stated invoice amount to compare; "D": the shared-transaction-number check found nothing). "Not available", never "clean".</summary>
    public static bool IsNa(string slug, string sch) => sch switch { "A" => CouncilFacts.Of(slug).NoA, "D" => CouncilFacts.Of(slug).NoD, _ => false };

    static bool DByNumbering(string slug) => CouncilOverrides.For(slug)?.DByNumbering ?? CouncilFacts.Of(slug).DByNumbering;

    /// <summary>Whether a financial year of a council whose numbers begin part-way through its history falls in the numbered stretch; true for every other council.</summary>
    public static bool HasNumber(string slug, string year)
    {
        var f = CouncilFacts.Of(slug);
        if (f.NoNumber) return false;
        if (!f.Partial) return true;
        return IsYear(year) && string.CompareOrdinal(year, FinancialYearOf(f.NumberedFirst!)) >= 0;
    }
    static bool IsYear(string y) => y.Length == 7 && y[4] == '-' && char.IsDigit(y[0]);
    /// <summary>The financial year (April to March) a month key belongs to: "2023-04" and "2024-03" are both "2023-24"; anything else is "undated".</summary>
    public static string FinancialYearOf(string month)
    {
        if (month.Length != 7 || !int.TryParse(month.AsSpan(0, 4), out var y) || !int.TryParse(month.AsSpan(5, 2), out var m)) return "undated";
        int s = m >= 4 ? y : y - 1;
        return $"{s}-{(s + 1) % 100:00}";
    }

    /// <summary>A caption for the shared-transaction-number block of a year in which the council's numbering, not its payments, produces the groups (the years with Schedule D rows, for a
    /// council whose prose says so); null otherwise.</summary>
    public static string? NumberingNote(string slug, string sch, string year)
    {
        if (sch != "D" || !CouncilFacts.Of(slug).DYears.Contains(year)) return null;
        return CouncilOverrides.For(slug)?.NumberingNote?.Invoke(year);
    }

    /// <summary>The "not available" line of a Schedule A or D block for a year; null when the schedule can run for this council (nothing to say beyond the groups).</summary>
    public static string? NotAvailable(string slug, string sch, string year)
    {
        if (!IsNa(slug, sch)) return null;
        var f = CouncilFacts.Of(slug);
        if (sch == "A" && CouncilOverrides.For(slug)?.NotAvailableA is { } a) return a;
        if (sch == "D" && f.Partial)
        {
            string from = MonthName(f.NumberedFirst!);
            return HasNumber(slug, year)
                ? $"Not available for most of this council's history, and nothing found here: its file gives a transaction number only from {from}. In the months that carry one, this rule found no number against more than one payee or pay date. That is a limit of the file, not a sign the rule was clean for earlier years."
                : $"Not available for this council: its published file has no transaction number before {from}, so there is no reference to group by and this rule cannot run. That is a limit of the file, not a clean result.";
        }
        return "Not available for this council: its published file "
            + (sch == "A" ? (f.NoNumber ? "has no transaction number, so one transaction's payments cannot be added up and set against a stated amount" : "has no stated invoice or gross amount to compare the payments with")
               : DByNumbering(slug) ? "gives every transaction number to one payee on one date, or to a single row" : "has no transaction reference to group by")
            + ", so this rule " + (sch == "D" && DByNumbering(slug) ? "finds nothing by construction" : "cannot run") + ". That is a limit of the file, not a clean result.";
    }

    /// <summary>The reason a cross-council check cannot run for this council, or null when it can. Said on the page instead of "no rows", which would read as a clean result.</summary>
    public static string? CheckCannotRun(string slug, string checkId) =>
        CouncilFacts.Of(slug).NoNumber && checkId is "twins" or "withintxn"
            ? "This check cannot run for this council: its file publishes no transaction number, and this check compares transaction numbers. That is a limit of the file, not a clean result."
            : null;

    /// <summary>Plain statements, shown at the top of a council's page, of what cannot be checked for it and why. Nothing here says anything about the payments themselves.</summary>
    public static List<string> CannotCheck(string slug)
    {
        var f = CouncilFacts.Of(slug);
        var l = new List<string>();
        if (!ListForEveryCouncil && !f.NoBudget) return l;
        var ov = CouncilOverrides.For(slug);
        foreach (var item in ov?.CannotCheck ?? new[] { "{A}", "{number}", "{budget}", "{redaction}" })
        {
            switch (item)
            {
                case "{A}": if (f.NoNumber) break; if (f.NoA) l.Add(ov?.ALine ?? ALine); break;
                case "{number}": AddNumberLines(slug, f, l); break;
                case "{budget}": if (f.NoBudget) l.Add(BudgetLine); break;
                case "{redaction}": if (f.Redacted > 0) l.Add(f.Pooled > 0 ? PooledLine : RedactedLine); break;
                default: l.Add(item); break;
            }
        }
        return l;
    }

    const string ALine = "The invoice-amount check cannot run: the file gives no VAT amount and Gross equals Net on every line, so there is no stated invoice amount to compare the payments with.";
    const string BudgetLine = "The declared-spend comparison cannot run: no government Revenue Outturn figure is held here for this council, so no year is set against the file total, and this council is in none of the groups of the pre-registered test.";
    const string RedactedLine = "Lines whose payee the council redacts are left out of every check and only counted. The checks say nothing about those lines.";
    const string PooledLine = "Lines whose payee is a pooled label (one label standing for many people, such as a foster care payment) are shown by the scanner as \"Redacted (pooled label): <label>\" and are left out of every check. The checks say nothing about those lines.";

    static void AddNumberLines(string slug, CouncilFacts f, List<string> l)
    {
        if (f.NoNumber)
        {
            l.Add("The invoice-amount check and the shared-transaction-number check cannot run: the file publishes no transaction number, so one transaction's payments cannot be added up or compared. They are listed under each year as \"not available\", not as clean.");
            l.Add("Two cross-council checks need a transaction number and cannot run either: transactions published twice under two numbers, and the same line repeated inside one transaction.");
            l.Add("The repeated-payment check is the only repeat test that can run, and it has no transaction number to tell two payments apart: a group is the same payee, amount and description on the same date.");
        }
        else if (f.Partial)
        {
            string from = MonthName(f.NumberedFirst!), to = MonthName(f.NumberedLast!);
            l.Add($"No transaction number is published before {from} (the {f.NumberedMonths} months from {from} to {to} carry one). For every earlier month the shared-transaction-number check cannot run, and neither can the two cross-council checks that need a number: transactions published twice under two numbers, and the same line repeated inside one transaction. They are listed as not available, not as clean. In the months with a number they run"
                + (f.NoD ? ", and the transaction-number check found no number against more than one payee or pay date." : "."));
        }
        else if (f.NoD && (CouncilOverrides.For(slug)?.DByNumbering ?? f.DByNumbering))
            l.Add("The shared-transaction-number check finds nothing by construction: the file gives each transaction number to one payee on one date. That is a result of how the numbers are given, not a sign the check was clean.");
        else if (f.NoD)
            l.Add("The shared-transaction-number check cannot run: the file's transaction number is a per-file counter that restarts every month, not a reference that one transaction can share. That is a limit of the file, not a clean result.");
    }

    /// <summary>A caption for one financial year of a council when its publication changes inside or at the edge of the year (prose, so a CouncilOverrides entry); null otherwise.</summary>
    public static string? YearNote(string slug, string year)
    {
        if (!IsYear(year)) return null;   // "undated" is also seven characters
        return CouncilOverrides.For(slug)?.YearNote?.Invoke(year);
    }

    public const string DiscrepancyNote =
        "A discrepancy here is a fact about the published numbers, not proof of an error or wrongdoing. Councils publish corrections, instalments, and VAT treatments that can look like a mismatch until explained. If you intend to raise this with the council or its auditor, ask for the records and the reason, not for an admission.";

    public const string OglCredit =
        "Contains public sector information licensed under the Open Government Licence v3.0. Council payment data is published by each council under the Local Government Transparency Code; declared figures are the government's Revenue Outturn and Capital Outturn statistics.";
}
