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

    /// <summary>The words for a classification code (see CouncilCodes: a raw engine code is never printed; an unlisted one reads "Other (not described here)").</summary>
    public static string Display(string c) => CouncilCodes.Class(c);

    /// <summary>The pattern readings the engine attaches to a still-open group (ExplainedBy). A reading never removes a group from the open count and is never a verdict.</summary>
    public static string ReadingTag(string by) => CouncilCodes.Reading(by);

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
        "Unreconciled" => 0, "StandingScheduleSurplus" => 1, "Unclear" => 2, "Unmatched" => 3, "SmallGap" => 4,
        "ReversedSameDay" => 10, "MigrationControlLabel" => 11, _ => 5,
    };
    public static bool IsUnexplained(string c) => c is "Unreconciled" or "SmallGap" or "Unclear" or "Unmatched" or "StandingScheduleSurplus" or "";

    public static string Meaning(string c) => c switch
    {
        "Unreconciled" => "No specific pattern was recognised: the published numbers do not add up under any of the rules. The file does not say why.",
        "DoubleListing" => "The same invoice line, or the whole itemised invoice, appears to have been published more than once (the lines sum to an exact whole-number multiple of the stated invoice, or of it once VAT is added), rather than several genuinely different charges.",
        "DebtCharge" => "The stated amount splits exactly into a round principal plus a non-round interest or charge remainder: consistent with a loan repayment or debt-recovery invoice, not an unexplained gap.",
        "EarlyPaymentProgramme" => "A batch of negative early-payment discount fees under the council's own named scheme (a supply-chain-finance arrangement): a known pattern, not an arithmetic mismatch.",
        "VatRoundingNoise" => "The gap is a few pence (5p or less), consistent with VAT being rounded per line and then summed, rather than once on the total.",
        "SmallGap" => "The gap is more than 5p and under £1. It is too large to be read as VAT rounding and no pattern accounts for it. The file does not say why.",
        "NegativeNetSignFlip" => "The published net figure is the exact negative of the value needed to reconcile the stated invoice. Reported as a named, recurring shape, not an explanation: why the sign is inverted is not established.",
        "Unclear" => "No supplier invoice number was available to check, so the repeat is reported exactly as published, with no verdict either way. A group may carry a pattern reading beside it; the reading is a consistent fit, not a proof, and the group stays open.",
        "LikelyDuplicate" => "Every member of this group shares the same supplier invoice number as well as the same amount. The file does not show whether more than one payment was made.",
        "LikelyRecurring" => "Members carry different supplier invoice numbers despite the identical amount: consistent with a standardised recurring rate.",
        "Unmatched" => "No same-supplier published charge of matching magnitude was found within 365 days. Not established to be an error; reported as published.",
        "MatchedOffsettingCharge" => "A same-supplier published charge of matching magnitude exists within 365 days: a plausible explanation exists in the data, not a claim that this credit offsets that charge.",
        "StandingScheduleCatchUp" => "This amount is paid about once a month, and this date carries more than one payment because earlier months have none. Counting payments against months, the extra payments are made good by empty months: consistent with a late payment caught up, paid ahead or repaid; not proof of any of them.",
        "StandingScheduleSurplus" => "This amount is paid about once a month, but there are more payments than months and nothing returned in the published data. More payments than months in the published data; the files do not say why. It is listed with the unexplained rows because no pattern accounts for it.",
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
        "A" => "The invoice amount the file states for a transaction (its Gross) is compared with the Gross expected from the payments published for it: each line's Net with its VAT type applied (Standard 20%, Reduced 5%, any other label 0%). The gap shown is the stated Gross minus that expected Gross. A transaction is listed only when the gap is more than 1p; up to 5p is called VAT rounding, under £1 a small gap, £1 or more unreconciled. A fact about the published figures, not a verdict.",
        "B" => "Lines that share a supplier, an amount, a description and a pay date. Some are explained by a recognised pattern (a standing monthly payment, an exact reversal the same day); the rest are listed as published.",
        "D" => "The same transaction number appears against more than one payee, or more than one pay date, in the published data. Where a council's own transaction numbers are supplier-furnished invoice numbers, two suppliers can simply have used the same number; this is not read as one payment going to two payees.",
        _ => "",
    };

    // ----------------------------------------------------------------------------------------------------------------------------------
    // What cannot be checked. Nothing below names a council: every statement is built from the FACTS the builder measured from the council's own export and wrote
    // into its profile (CouncilFacts: no stated invoice amount, no Schedule D row, no transaction number, numbers from one month on, per-file counters, no
    // declared-spend unit). The only per-council wording left is CouncilOverrides: prose that cannot be derived (a column's name, York's numbering, a threshold change).
    // ----------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The page-top "What cannot be checked" list is shown on EVERY council's page (2026-10-04, after an independent audit: Leeds, Kirklees, Durham, Newcastle and the rest have
    /// real limits too, and a limit that is only stated per year is easy to miss). The older councils gain a few lines that repeat what their year blocks also say; that is intended.</summary>
    public const bool ListForEveryCouncil = true;

    /// <summary>True when this council's file publishes no transaction number: measured (every line carries the scanner's "(no number published)" placeholder), or stated in the council's
    /// profile because the scanner added a row number of its own (CouncilOverrides.NoPublishedNumber). A row number the scanner added is never called the file's transaction number.</summary>
    public static bool NoNumber(string slug) => CouncilFacts.Of(slug).NoNumber || (CouncilOverrides.For(slug)?.NoPublishedNumber ?? false);

    /// <summary>The scanner's own row number stands where the council publishes no number (set by CouncilOverrides), as opposed to a placeholder that says so.</summary>
    static bool ScannerNumbers(string slug) => !CouncilFacts.Of(slug).NoNumber && NoNumber(slug);

    /// <summary>A line's transaction number as a council would recognise it: the number the file gives, or (when the council publishes none) a plain statement that it gives none.
    /// The scanner's row numbers and placeholders are never shown as a transaction number.</summary>
    public static string TxText(string slug, string tx) =>
        NoNumber(slug) || IsPlaceholder(tx) ? "(the file gives no transaction number)" : tx;

    /// <summary>True when an id is the scanner's own placeholder ("(no number published) 2620", "(no number published) 18 CHAPS", or a "~row" id) and not a number the council published. A council
    /// can publish numbers for most lines and none for some (Reading, RBWM, Nottingham, Leicester, Wakefield), so this is read per line, not per council.</summary>
    public static bool IsPlaceholder(string tx) => tx.StartsWith("(no number", StringComparison.Ordinal) || tx.Contains("~row");

    /// <summary>The placeholder's own counting number ("(no number published) 2620" is 2620; "... 18 CHAPS" is "18 CHAPS"), for a line that says it is a placeholder.</summary>
    public static string PlaceholderNumber(string tx)
    {
        int c = tx.IndexOf(')');
        return (c >= 0 ? tx[(c + 1)..] : tx).Trim();
    }

    /// <summary>True when this schedule cannot run for this council ("A": no stated invoice amount to compare; "D": the shared-transaction-number check found nothing). "Not available", never "clean".</summary>
    public static bool IsNa(string slug, string sch) => sch switch { "A" => CouncilFacts.Of(slug).NoA, "D" => CouncilFacts.Of(slug).NoD, _ => false };

    static bool DByNumbering(string slug) => CouncilOverrides.For(slug)?.DByNumbering ?? CouncilFacts.Of(slug).DByNumbering;

    /// <summary>Whether a financial year of a council whose numbers begin part-way through its history falls in the numbered stretch; true for every other council.</summary>
    public static bool HasNumber(string slug, string year)
    {
        var f = CouncilFacts.Of(slug);
        if (NoNumber(slug)) return false;
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
            + (sch == "A" ? (NoNumber(slug) ? "has no transaction number, so one transaction's payments cannot be added up and set against a stated amount" : "has no stated invoice or gross amount to compare the payments with")
               : DByNumbering(slug) ? "gives every transaction number to one payee on one date, or to a single row" : "has no transaction reference to group by")
            + ", so this rule " + (sch == "D" && DByNumbering(slug) ? "finds nothing by construction" : "cannot run") + ". That is a limit of the file, not a clean result.";
    }

    /// <summary>The two cross-council checks that read transaction numbers (identical lines under two numbers; the same line repeated inside one number).</summary>
    static bool NeedsNumber(string checkId) => checkId is "twins" or "withintxn";

    /// <summary>The one council whose own file states an invoice total, so the engine's within-transaction scan skips it (WithinTxnScan: "Schedule A already tests this against the published invoice total").
    /// Said on the page instead of an empty list, which would read as a clean result.</summary>
    const string WithinTxnSkippedSlug = "wokingham";
    static bool WithinTxnNotRun(string slug, string checkId) => checkId == "withintxn" && slug == WithinTxnSkippedSlug;

    /// <summary>The reason a cross-council check cannot run for this council, or null when it can. Said on the page instead of "no rows", which would read as a clean result.
    /// A council with no published number, and one whose number is a count that restarts every month or is given to one line only, cannot say that two lines belong to one transaction.</summary>
    public static string? CheckCannotRun(string slug, string checkId)
    {
        if (!NeedsNumber(checkId)) return null;
        if (WithinTxnNotRun(slug, checkId))
            return "This check is not run for this council: its invoice-amount check already tests each transaction against the invoice total the file states, so a line repeated inside one transaction is looked for there. That is a choice of the rules, not a clean result of this check.";
        var f = CouncilFacts.Of(slug);
        if (NoNumber(slug))
            return "This check cannot run for this council: its file publishes no transaction number" + (ScannerNumbers(slug) ? " (the number shown against each line is a row number the scanner added)" : "") + ", and this check compares transaction numbers. That is a limit of the file, not a clean result.";
        if (f.CounterIds)
            return "This check cannot run for this council: each line carries a transaction number of its own, so the number cannot show that two lines belong to one transaction or that two numbers stand for one. That is a limit of the numbers, not a clean result.";
        return null;
    }

    /// <summary>A caption for a check that CAN run but only over part of a council's months (numbers from one month on), so a short or empty list is not read as covering every month; null otherwise.</summary>
    public static string? CheckNote(string slug, string checkId)
    {
        var f = CouncilFacts.Of(slug);
        if (!NeedsNumber(checkId) || !f.Partial || NoNumber(slug)) return null;
        return $"This check covers only the months from {MonthName(f.NumberedFirst!)}: the file gives no transaction number before then. An empty list for earlier months is not a clean result.";
    }

    /// <summary>For the all-councils version of a number-based check: which councils it cannot run for, or covers only in part. Needs the councils' facts (read with the profiles); "" when none are known.</summary>
    public static string CheckCoverage(string checkId, IEnumerable<(string Slug, string Short)> councils)
    {
        if (!NeedsNumber(checkId)) return "";
        var none = new List<string>(); var part = new List<string>();
        var notRun = new List<string>();
        foreach (var (slug, name) in councils)
        {
            if (WithinTxnNotRun(slug, checkId)) notRun.Add(name);
            else if (CheckCannotRun(slug, checkId) is not null) none.Add(name);
            else if (CouncilFacts.Of(slug).Partial) part.Add(name);
        }
        var sb = new System.Text.StringBuilder();
        if (notRun.Count > 0) sb.Append($"This check is not run for {string.Join(", ", notRun)}: the invoice-amount check already tests each transaction against the invoice total the file states. ");
        if (none.Count > 0) sb.Append($"This check cannot run for {string.Join(", ", none)}: the transaction number in their files cannot link lines (none is published, or it is a count used by one line). They are not covered, and no list here is a clean result for them. ");
        if (part.Count > 0) sb.Append($"It covers only the months with a transaction number for {string.Join(", ", part)}. ");
        return sb.ToString().Trim();
    }

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
                case "{A}": if (NoNumber(slug)) break; if (f.NoA) l.Add(ov?.ALine ?? ALine); break;
                case "{number}": AddNumberLines(slug, f, l); break;
                case "{budget}": if (f.NoBudget) l.Add(BudgetLine); break;
                case "{redaction}": if (f.Redacted > 0) l.Add(f.Pooled > 0 ? PooledLine : RedactedLine); break;
                default: l.Add(item); break;
            }
        }
        // limits every council's files share: stated on every page, so no council's list is empty
        l.Add(ThresholdLine);
        l.Add(PayerSideLine);
        return l;
    }

    const string CounterCrossLine = "Two cross-council checks need a transaction number that can link lines and cannot run either: identical lines under two transaction numbers, and the same line repeated inside one transaction. The repeated-payment check, which does not use the number, is the repeat test that applies.";
    const string ThresholdLine = "Only payments above the council's own publication threshold are in the files, so smaller payments are in none of the checks.";
    const string PayerSideLine = "The files show the payer's side only. They cannot show money received, repayments, or a correction made outside the file, so a payment with nothing coming back in the file is not shown to be unpaid.";

    const string ALine = "The invoice-amount check cannot run: the file gives no VAT amount and Gross equals Net on every line, so there is no stated invoice amount to compare the payments with.";
    const string BudgetLine = "The declared-spend comparison cannot run: no government Revenue Outturn figure is held here for this council, so no year is set against the file total, and this council is in none of the groups of the pre-registered test.";
    const string RedactedLine = "Lines whose payee the council redacts are left out of every check and only counted. The checks say nothing about those lines.";
    const string PooledLine = "Lines whose payee is a pooled label (one label standing for many people, such as a foster care payment) are shown by the scanner as \"Redacted (pooled label): <label>\" and are left out of every check. The checks say nothing about those lines.";

    static void AddNumberLines(string slug, CouncilFacts f, List<string> l)
    {
        if (NoNumber(slug))
        {
            string scanner = ScannerNumbers(slug) ? " (the number shown against each of its lines is a row number the scanner added, not one the council publishes)" : "";
            l.Add($"The invoice-amount check and the shared-transaction-number check cannot run: the file publishes no transaction number{scanner}, so one transaction's payments cannot be added up or compared. They are listed under each year as \"not available\", not as clean.");
            l.Add("Two cross-council checks need a transaction number and cannot run either: identical lines under two transaction numbers, and the same line repeated inside one transaction.");
            l.Add("The repeated-payment check is the only repeat test that can run, and it has no transaction number to tell two payments apart: a group is the same payee, amount and description on the same date.");
        }
        else if (f.Partial)
        {
            string from = MonthName(f.NumberedFirst!), to = MonthName(f.NumberedLast!);
            l.Add($"No transaction number is published before {from} (the {f.NumberedMonths} months from {from} to {to} carry one). For every earlier month the shared-transaction-number check cannot run, and neither can the two cross-council checks that need a number: identical lines under two transaction numbers, and the same line repeated inside one transaction. They are listed as not available, not as clean. In the months with a number they run"
                + (f.NoD ? ", and the transaction-number check found no number against more than one payee or pay date." : "."));
        }
        else if (f.NoD && (CouncilOverrides.For(slug)?.DByNumbering ?? f.DByNumbering))
        {
            l.Add("The shared-transaction-number check finds nothing by construction: the file gives each transaction number to one payee on one date. That is a result of how the numbers are given, not a sign the check was clean.");
            if (f.CounterIds) l.Add(CounterCrossLine);
        }
        else if (f.CounterIds)
        {
            l.Add("The shared-transaction-number check finds nothing by construction: the file gives each line its own transaction number (a count that restarts every month), so no number can be shared by two payees or two pay dates. That is a result of how the numbers are given, not a sign the check was clean.");
            l.Add(CounterCrossLine);
        }
        else if (f.NoD)
            l.Add("The shared-transaction-number check cannot run: the transaction numbers the scanner holds for this council restart every month, so they are not a reference that one transaction can share. That is a limit of the numbers held, not a clean result.");
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
