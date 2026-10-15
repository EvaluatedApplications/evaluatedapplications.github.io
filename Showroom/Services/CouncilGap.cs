using System.Globalization;
using static Showroom.Services.CouncilTerms;

namespace Showroom.Services;

/// <summary>
/// One council-year of the declared-spend comparison, read from the virtual-customer's budget_units.csv (the file the pre-registered test
/// reads, so every figure here is the test's own). Declared = K3 (Revenue Outturn running expenses + Housing Revenue Account + Capital
/// Outturn + the pass-through payments seen in the file; employee pay is NOT in it). File = F, the sum of every published Net amount.
/// Flagged = T_B: the second copies of lines listed under a second transaction number (T_A) plus the repeated-payment groups. The gap is Declared - File.
/// </summary>
public sealed class GapYear
{
    public string Council = "", Year = "", Pool = "", Note = "";
    public bool Eligible; public int Months;
    public decimal File, K1, K2, K3, TA, TB;
    public decimal Declared => K3;
    public decimal Gap => K3 - File;
    public decimal Leads => TB - TA;
    public decimal Flagged => TB;
    public bool Worked => Pool == "exploratory";

    /// <summary>All years of one council (or every council when slug is null), newest first.</summary>
    public static List<GapYear> Build(Tab units, string? slug)
    {
        var list = new List<GapYear>();
        foreach (var r in units.Rows)
        {
            string c = units.G(r, "Council");
            if (slug is not null && c != slug) continue;
            list.Add(new GapYear
            {
                Council = c, Year = units.G(r, "Year"), Pool = units.G(r, "Pool"), Note = units.G(r, "Note"),
                Eligible = units.B(r, "Eligible"), Months = units.I(r, "MonthsCovered"),
                File = units.M(r, "F"), K1 = units.M(r, "K1"), K2 = units.M(r, "K2"), K3 = units.M(r, "K3"), TA = units.M(r, "TA"), TB = units.M(r, "TB"),
            });
        }
        list.Sort((a, b) => { int k = string.CompareOrdinal(b.Year, a.Year); return k != 0 ? k : string.CompareOrdinal(a.Council, b.Council); });
        return list;
    }

    /// <summary>Why a year is not compared: the file must cover twelve months and the government must have published a figure for the year.</summary>
    public string WhyNot => Months < 12
        ? $"the file covers {Months} of 12 months"
        : K1 == 0m ? "no government figure for this year is held here" : "not eligible";

    public static string Times(decimal r) => r >= 10m ? Math.Round(r, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture) : r.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The one plain sentence for a year. The same rule for every council; never a cause.</summary>
    public string Reading()
    {
        decimal gap = Gap, fl = Flagged;
        if (fl <= 0m) return "No payments were flagged for this year, so there is nothing to set against the declared spend.";
        string flagged = $"Flagged repeat payments ({Gbp(fl)}: {Gbp(TA)} second copies of lines listed under a second transaction number, {Gbp(Leads)} repeated-payment groups)";
        if (gap > 0m && fl <= gap)
            return $"{flagged} would fit inside the {Gbp(gap)} of declared spend not itemised in the file {Times(gap / fl)} times over, so the budget figures cannot rule them out.";
        if (gap > 0m)
            return $"{flagged} are larger than the {Gbp(gap)} of declared spend not itemised in the file ({Times(fl / gap)} times its size), so they would not all fit inside it. This compares sizes on different bases; it does not show what the payments were.";
        return $"The file itemises {Gbp(-gap)} more than the declared spend, so no declared spend is left un-itemised for the flagged repeat payments ({Gbp(fl)}) to fit inside, "
             + "and the budget figures give no room to treat them as extra spending on top of the declared total. The file is gross of VAT and includes loan movements and credits, so the excess has other possible sources; it does not show that any payment was made twice.";
    }
}

/// <summary>The council-level summary of a list of compared years: the same counts and the same sentence rule for every council.</summary>
public sealed class GapSummary
{
    public int N, Below, Above, Fits, Larger;
    public decimal SumGap, SumFlaggedBelow, SumTABelow, SumLeadsBelow, SumFlaggedAbove;
    public decimal MinFit, MaxFit, MedFit;
    public string First = "", Last = "";

    public static GapSummary Of(List<GapYear> years)
    {
        var s = new GapSummary();
        var fits = new List<decimal>();
        foreach (var y in years)
        {
            if (!y.Eligible) continue;
            s.N++;
            if (s.First == "" || string.CompareOrdinal(y.Year, s.First) < 0) s.First = y.Year;
            if (string.CompareOrdinal(y.Year, s.Last) > 0) s.Last = y.Year;
            if (y.Gap > 0m)
            {
                s.Below++; s.SumGap += y.Gap; s.SumFlaggedBelow += y.Flagged; s.SumTABelow += y.TA; s.SumLeadsBelow += y.Leads;
                if (y.Flagged > 0m && y.Flagged <= y.Gap) { s.Fits++; fits.Add(y.Gap / y.Flagged); }
                else if (y.Flagged > y.Gap) s.Larger++;
            }
            else { s.Above++; s.SumFlaggedAbove += y.Flagged; }
        }
        // insertion sort: at most a few dozen values, and it avoids a generic sort over decimals in the browser
        for (int i = 1; i < fits.Count; i++) { var v = fits[i]; int j = i - 1; while (j >= 0 && fits[j] > v) { fits[j + 1] = fits[j]; j--; } fits[j + 1] = v; }
        if (fits.Count > 0) { s.MinFit = fits[0]; s.MaxFit = fits[^1]; s.MedFit = fits.Count % 2 == 1 ? fits[fits.Count / 2] : (fits[fits.Count / 2 - 1] + fits[fits.Count / 2]) / 2m; }
        return s;
    }

    /// <summary>The sentences for one council's page.</summary>
    public List<string> Sentences()
    {
        var l = new List<string>();
        if (N == 0) { l.Add("No year of this council can be compared yet: the comparison needs a file that covers twelve months and a published government figure for the same year."); return l; }
        l.Add($"{N} year{(N == 1 ? "" : "s")} can be compared ({(First == Last ? First : First + " to " + Last)}). In {Fits} of {N} the flagged repeat payments fit inside the declared spend not itemised in the file, so the budget figures cannot rule them out.");
        if (Below > 0)
            l.Add($"The file itemises less than the declared spend in {Below} of {N} years, leaving {Gbp(SumGap)} of declared spend not itemised in all. Flagged repeat payments in those years total {Gbp(SumFlaggedBelow)} ({Gbp(SumTABelow)} second copies, {Gbp(SumLeadsBelow)} repeated-payment groups)"
                + (Fits > 0 ? $"; where they fit, they fit {GapYear.Times(MinFit)} to {GapYear.Times(MaxFit)} times over ({GapYear.Times(MedFit)} in the median year)" : "")
                + (Larger > 0 ? $"; in {Larger} year{(Larger == 1 ? "" : "s")} they are larger than the not-itemised amount" : "") + ".");
        if (Above > 0)
            l.Add($"The file itemises more than the declared spend in {Above} of {N} years. There is then no declared spend left un-itemised for flagged payments to fit inside; the flagged repeat payments in those years total {Gbp(SumFlaggedAbove)}.");
        return l;
    }
}
