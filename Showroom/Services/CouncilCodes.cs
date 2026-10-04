namespace Showroom.Services;

/// <summary>
/// Every machine code the scanner's export can carry into a page line, with the plain words that replace it. Nothing on the Council Spending pages prints a raw
/// code: a value with no entry here is shown as <see cref="Unlisted"/>, and the two offline tools that ship the data (CouncilWebBuilder, CouncilTermsCheck) scan
/// the real files with <see cref="Audit"/> and FAIL on any code that is missing from this table, so a new engine code cannot reach the public page unmapped.
/// Plain C# with no dependencies, so both tools compile this file directly.
/// </summary>
public static class CouncilCodes
{
    /// <summary>Shown for a code that has no entry (the build tools fail before this can reach a visitor).</summary>
    public const string Unlisted = "Other (not described here)";

    public const string VocabClassification = "Classification";
    public const string VocabExplainedBy = "ExplainedBy";
    public const string VocabDebtFlag = "DebtSinkFlag";
    public const string VocabLabelCheck = "LabelCheck";
    public const string VocabDecodeStatus = "DecodeStatus";

    static readonly Dictionary<string, string> Classes = new(StringComparer.Ordinal)
    {
        [""] = "No classification",
        ["Unreconciled"] = "Unreconciled",
        ["Unclear"] = "Unclear: no invoice number to check",
        ["Unmatched"] = "No matching charge found",
        ["DoubleListing"] = "Double listing",
        ["DebtCharge"] = "Debt charge",
        ["EarlyPaymentProgramme"] = "Early payment programme",
        ["VatRoundingNoise"] = "VAT rounding noise",
        ["NegativeNetSignFlip"] = "Net sign flip",
        ["LikelyDuplicate"] = "Same invoice number on every line",
        ["LikelyRecurring"] = "Different invoice numbers, same amount",
        ["MatchedOffsettingCharge"] = "Matched offsetting charge",
        ["StandingScheduleCatchUp"] = "Standing payment, caught up",
        ["StandingScheduleSurplus"] = "Standing payment, more payments than months",
        ["StandingPaymentUnderRefundType"] = "Standing payment under refund label",
        ["ReversedSameDay"] = "Copies cancelled the same day",
        ["MigrationControlLabel"] = "Council's own migration label",
        ["NSquaredListing"] = "Publication quirk: rows printed n times",
    };

    static readonly Dictionary<string, string> Readings = new(StringComparer.Ordinal)
    {
        ["RecurringBatchRate"] = "Pattern reading: recurring batch rate",
        ["CadenceCatchUp"] = "Pattern reading: monthly payments, caught up",
        ["NSquaredRows"] = "Publication quirk: rows printed n times",
    };

    static readonly Dictionary<string, string> LabelChecks = new(StringComparer.Ordinal)
    {
        ["label seen in other months"] = "The description of that month's largest row also appears in other months.",
        ["LABEL SEEN ONLY IN THIS MONTH"] = "The description of that month's largest row appears in no other month.",
    };

    static readonly Dictionary<string, string> DecodeStatuses = new(StringComparer.Ordinal)
    {
        ["Decodes"] = "Rebuilt",
        ["DecodesPrincipalUnpublished"] = "Rebuilt as interest on an unpublished loan",
        ["DoesNotDecode"] = "Not rebuilt",
        ["NotApplicable"] = "Not applicable",
    };

    /// <summary>Plain words for a classification, or <see cref="Unlisted"/>.</summary>
    public static string Class(string code) => Classes.TryGetValue(code ?? "", out var s) ? s : Unlisted;
    /// <summary>Plain words for a pattern reading, or <see cref="Unlisted"/>.</summary>
    public static string Reading(string code) => Readings.TryGetValue(code ?? "", out var s) ? s : Unlisted;
    /// <summary>The sentence for a payment-misfit "label check" value.</summary>
    public static string LabelCheck(string code) => LabelChecks.TryGetValue(code ?? "", out var s) ? s : Unlisted;
    /// <summary>Plain words for a loan-interest rebuild status.</summary>
    public static string DecodeStatus(string code) => DecodeStatuses.TryGetValue(code ?? "", out var s) ? s : Unlisted;

    /// <summary>One flag token of the debt-sink export ("NoReturnFlow", "RisingFullYears3") as plain words; null when the token is not known.</summary>
    public static string? DebtFlag(string token)
    {
        switch (token)
        {
            case "NoReturnFlow": return "nothing is credited back in this file";
            case "ReturnUnder2pct": return "less than 2% of the amount paid out is credited back in this file";
            case "DebtLabelled": return "some descriptions use words such as loan, advance, debt or interest";
        }
        const string rising = "RisingFullYears";
        if (token.StartsWith(rising, StringComparison.Ordinal) && int.TryParse(token.AsSpan(rising.Length), out var n) && n >= 2)
            return $"the yearly total is higher than the year before in {n} full calendar years in a row";
        return null;
    }

    /// <summary>A space-separated flag cell as one sentence of plain words, without the final full stop ("" when there are no flags); an unknown token becomes <see cref="Unlisted"/>.</summary>
    public static string DebtFlags(string cell)
    {
        var parts = new List<string>();
        foreach (var t in cell.Split(' ', StringSplitOptions.RemoveEmptyEntries)) parts.Add(DebtFlag(t) ?? Unlisted);
        return string.Join("; ", parts);
    }

    /// <summary>Every (vocabulary, value) the data carries that this table does not describe. Empty means every code is mapped.</summary>
    public static List<string> Audit(IEnumerable<(string Vocab, string Value)> seen)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (vocab, value) in seen)
        {
            bool ok = vocab switch
            {
                VocabClassification => Classes.ContainsKey(value),
                VocabExplainedBy => value.Length == 0 || Readings.ContainsKey(value),
                VocabDebtFlag => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => DebtFlag(t) is not null),
                VocabLabelCheck => LabelChecks.ContainsKey(value),
                VocabDecodeStatus => value.Length == 0 || DecodeStatuses.ContainsKey(value),
                _ => false,
            };
            if (!ok) missing.Add($"{vocab}: \"{value}\"");
        }
        return missing.ToList();
    }
}
