using System.Text.Json;

namespace Showroom.Services;

/// <summary>
/// What the builder MEASURED from one council's own export (CouncilWebBuilder writes it into the council's profile as "facts"); the page builds every
/// "cannot be checked" statement from these, so a new council needs no page edit. See the FACTS comment in CouncilWebBuilder/Program.cs for each definition.
/// </summary>
public sealed record CouncilFacts(
    long Rows,
    bool NoA,                       // no stated invoice amount to compare with (no Schedule A row, Gross equals Net on every line, no VAT amount)
    bool NoD,                       // no Schedule D row anywhere
    bool NoNumber,                  // no month publishes a transaction number
    string? NumberedFirst,          // numbers published from this month on and none before it ("2025-04"), else null
    string? NumberedLast,
    int NumberedMonths,
    bool CounterIds,                // the number is a per-file counter that restarts every month
    bool DByNumbering,              // real numbers, none shared by two payees or two dates
    IReadOnlyList<string> DYears,   // financial years with a Schedule D row
    bool NoBudget,                  // no declared-spend unit has a published Revenue Outturn: the comparison cannot run
    long Redacted,
    long Pooled)
{
    public bool Partial => NumberedFirst is not null;

    /// <summary>No facts known (a profile without them): every statement stays silent rather than guess.</summary>
    public static readonly CouncilFacts None = new(0, false, false, false, null, null, 0, false, false, Array.Empty<string>(), false, 0, 0);

    // Filled when profiles.json is read (all councils at once); the council page awaits that before it draws anything that asks.
    static readonly Dictionary<string, CouncilFacts> Known = new();
    public static CouncilFacts Of(string slug) => Known.TryGetValue(slug, out var f) ? f : None;
    public static void Set(string slug, CouncilFacts f) => Known[slug] = f;

    public static CouncilFacts FromJson(JsonElement e)
    {
        bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        long L(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        string? S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var years = new List<string>();
        if (e.TryGetProperty("dYears", out var d) && d.ValueKind == JsonValueKind.Array) foreach (var y in d.EnumerateArray()) years.Add(y.GetString() ?? "");
        return new CouncilFacts(L("rows"), B("noA"), B("noD"), B("noNumber"), S("numberedFirst"), S("numberedLast"), (int)L("numberedMonths"),
            B("counterIds"), B("dByNumbering"), years, B("noBudget"), L("redacted"), L("pooled"));
    }
}
