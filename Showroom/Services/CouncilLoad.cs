using static Showroom.Services.CouncilTerms;

namespace Showroom.Services;

/// <summary>One financial year of one council as the picker lists it: its months (the ones the council's file has), and the year's bundle (years.csv).</summary>
public sealed class YearInfo
{
    public string Year = "";
    public List<MonthRow> Months = new();      // oldest first
    public YearRow? Row;                       // null only if years.csv has no line for the year
    public string Title => Year == "undated" ? "No readable date" : Year;
    public int ExceptionRows => Row?.ExceptionRows ?? Months.Sum(m => m.ExceptionRows);
    public long GzipBytes => Row?.ExceptionGzipBytes ?? Months.Sum(m => m.ExceptionGzipBytes);

    /// <summary>"12 months", or "11 months, May 2019 to March 2020" when the council's file does not cover the whole of the year.</summary>
    public string Coverage
    {
        get
        {
            if (Year == "undated") return Num(Months.Sum(m => m.TxRows)) + " lines";
            if (Months.Count == 12) return "12 months";
            return $"{Months.Count} month{(Months.Count == 1 ? "" : "s")}, {MonthName(Months[0].Month)} to {MonthName(Months[^1].Month)}";
        }
    }

    /// <summary>Every year of a council, newest first (the lines with no readable date last).</summary>
    public static List<YearInfo> Build(List<MonthRow> months, List<YearRow> years)
    {
        var map = new Dictionary<string, YearInfo>();
        foreach (var m in months.OrderBy(m => m.Month, StringComparer.Ordinal))
        {
            string fy = CouncilWebData.FinancialYearOf(m.Month);
            if (!map.TryGetValue(fy, out var yi)) map[fy] = yi = new YearInfo { Year = fy, Row = years.FirstOrDefault(r => r.Year == fy) };
            yi.Months.Add(m);
        }
        var list = map.Values.ToList();
        list.Sort((a, b) => a.Year == "undated" ? 1 : b.Year == "undated" ? -1 : string.CompareOrdinal(b.Year, a.Year));
        return list;
    }
}

/// <summary>What the visitor has loaded: a whole financial year (one bundle), or some months of one year. Its scan, its totals, and (for a whole year) the declared-spend row.</summary>
public sealed class LoadedUnit
{
    public string Key = "", Year = "";
    public bool Full;                          // every month the council has for the year (the declared total is a whole-year figure)
    public string Label = "", Coverage = "";
    public List<string> Months = new();
    public MonthScan Scan = null!;
    public int TxRows;
    public decimal Net;
    public GapYear? Gap;
    public string GapNote = "";
    public bool Open, Busy;                    // UI state: the block is expanded; its bytes are being read back in
    public int FlaggedLines => Scan.TotalLines;
    public int Groups { get { int n = 0; foreach (var b in Scan.Buckets) n += b.GroupCount; return n; } }
}
