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
          "wakefield", "coventry", "durham", "kirklees", "leicester", "cornwall", "nottingham", "wirral", "newcastle" };
    // durham, kirklees and newcastle: every transaction number sits against one payee (or one row), so the shared-number check is empty by construction in the profiles
    public static readonly HashSet<string> NoScheduleD = new() { "birmingham", "westberkshire", "sheffield", "leeds", "durham", "kirklees", "newcastle" };
    /// <summary>The members of NoScheduleD that do publish a number: the shared-number check is empty because of how the numbers are given, not because they are missing.</summary>
    public static readonly HashSet<string> NoDByNumbering = new() { "durham", "kirklees", "newcastle" };

    public const string DiscrepancyNote =
        "A discrepancy here is a fact about the published numbers, not proof of an error or wrongdoing. Councils publish corrections, instalments, and VAT treatments that can look like a mismatch until explained. If you intend to raise this with the council or its auditor, ask for the records and the reason, not for an admission.";

    public const string OglCredit =
        "Contains public sector information licensed under the Open Government Licence v3.0. Council payment data is published by each council under the Local Government Transparency Code; declared figures are the government's Revenue Outturn and Capital Outturn statistics.";
}
