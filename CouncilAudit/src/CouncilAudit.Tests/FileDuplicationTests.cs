using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 26: Reading 2024-10 (400 of 4,175 rows exact repeats, GBP 6.36m) and 2024-12 (698 of 4,137, GBP 4.20m)
/// against none in 2024-09, 2024-11, 2025-01 and every other 2024-26 month; and the control: councils whose every
/// file carries a steady few percent of ordinary repeated lines (Wokingham, Bracknell, Merton) must not be flagged.
/// </summary>
public class FileDuplicationTests
{
    private static List<SpendRow> File(string tag, int rows, int repeats, int startVoucher = 1)
    {
        var list = new List<SpendRow>();
        for (int i = 0; i < rows; i++)
            list.Add(new SpendRow(tag, (startVoucher + i).ToString(), "S" + (i % 40), "01/01/2024", null, "d", "a", 100m + i, 100m + i, null, null, i));
        for (int i = 0; i < repeats; i++)
            list.Add(new SpendRow(tag, (startVoucher + i).ToString(), "S" + (i % 40), "01/01/2024", null, "d", "a", 100m + i, 100m + i, null, null, rows + i));
        return list;
    }

    [Fact]
    public void AFileWithAHeavyRepeatRateBesideCleanFiles_IsFlagged()
    {
        var all = new List<SpendRow>();
        foreach (var t in new[] { "2024-08", "2024-09" }) all.AddRange(File(t, 1000, 0, 1));
        all.AddRange(File("2024-10", 1000, 100, 5000));
        foreach (var t in new[] { "2024-11", "2024-12" }) all.AddRange(File(t, 1000, 0, 9000 + t.Length * 1000));
        var stats = FileDuplication.Scan(all);
        var oct = stats.Single(s => s.SourceTag == "2024-10");
        Assert.True(oct.Anomalous);
        Assert.Equal(100, oct.RepeatRows);
        Assert.All(stats.Where(s => s.SourceTag != "2024-10"), s => Assert.False(s.Anomalous));
    }

    [Fact]
    public void SteadyOrdinaryRepeatsInEveryFile_AreNotFlagged()
    {
        var all = new List<SpendRow>();
        int v = 1;
        foreach (var t in new[] { "A", "B", "C", "D", "E", "F" })
        {
            all.AddRange(File(t, 1000, 60, v));
            v += 2000;
        }
        Assert.All(FileDuplication.Scan(all), s => Assert.False(s.Anomalous));
    }

    [Fact]
    public void ARepeatBelowTheAbsoluteFloor_IsNotFlagged()
    {
        var all = new List<SpendRow>();
        all.AddRange(File("X1", 1000, 0, 1));
        all.AddRange(File("X2", 1000, 30, 3000));   // 3% but only 30 rows
        all.AddRange(File("X3", 1000, 0, 6000));
        Assert.All(FileDuplication.Scan(all), s => Assert.False(s.Anomalous));
    }
}
