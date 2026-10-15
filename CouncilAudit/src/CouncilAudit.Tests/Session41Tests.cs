using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 41. Real case: Wokingham's academy payment runs (11 Nov 2022, 3766535 and 3766545; April 2023, 3804026). A payee with n
/// distinct lines has every line printed n times (The Circle Trust 9 x 9 = 81 rows); the de-duplicated lines of April 2023 sum to
/// the stated Gross to the penny, so the row total is a publication defect, not extra money.
/// </summary>
public class Session41Tests
{
    private static SpendRow Line(string trans, string payee, string desc, decimal net, decimal gross, string date = "13/04/2023") =>
        new("S", trans, payee, date, AuditEngine.ParseDate(date), desc, "SEN", net, gross, null, null, 0, null, "High needs", null);

    /// <summary>Payee A: 2 distinct lines printed 2 times each (4 rows); payee B: 3 lines printed 3 times (9 rows); payee C: 1 line (1 row).</summary>
    private static List<SpendRow> Run(string trans, decimal gross, decimal bExtra = 0m)
    {
        var rows = new List<SpendRow>();
        foreach (var d in new[] { ("a1", 100.50m), ("a2", 200.25m) }) for (int i = 0; i < 2; i++) rows.Add(Line(trans, "Academy A", d.Item1, d.Item2, gross));
        foreach (var d in new[] { ("b1", 10m), ("b2", 20m), ("b3", 30m + bExtra) }) for (int i = 0; i < 3; i++) rows.Add(Line(trans, "Academy B", d.Item1, d.Item2, gross));
        rows.Add(Line(trans, "Academy C", "c1", 50m, gross));
        return rows;
    }

    private const decimal Distinct = 100.50m + 200.25m + 10m + 20m + 30m + 50m; // 410.75

    [Fact]
    public void NSquaredRun_WhoseDistinctLinesEqualTheGross_IsAPublicationDefect()
    {
        var res = AuditEngine.Run(Run("T1", Distinct), TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(ScheduleAClassification.NSquaredListing, a.Classification);
        Assert.Equal(14, a.LineCount);
        Assert.Contains("to the penny", a.ClassificationDetail);
        Assert.Contains("410.75", a.ClassificationDetail);
        Assert.True(a.Net > Distinct * 2); // the row total is inflated: 4 + 9 + 1 rows
    }

    [Fact]
    public void NSquaredShape_WithAGrossAboveTheDistinctLines_StaysUnreconciled_AndStatesTheGap()
    {
        var res = AuditEngine.Run(Run("T2", Distinct + 18550m), TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.Contains("n-squared", a.ClassificationDetail);
        Assert.Contains("18550.00 above", a.ClassificationDetail);
    }

    [Fact]
    public void AMultiPayeeRunWithoutTheShape_IsNotTouched()
    {
        var rows = Run("T3", Distinct);
        rows.Add(Line("T3", "Academy A", "a3", 1m, Distinct)); // payee A now has 5 rows: not a square
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.DoesNotContain("n-squared", a.ClassificationDetail);
    }

    [Fact]
    public void ARunWhereEveryPayeeHasOneLine_IsNotNSquared()
    {
        var rows = new List<SpendRow> { Line("T4", "A", "x", 10m, 99m), Line("T4", "B", "y", 20m, 99m), Line("T4", "C", "z", 30m, 99m) };
        var a = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal).ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.DoesNotContain("n-squared", a.ClassificationDetail);
    }

    [Fact]
    public void TwoCopiesOfAnNSquaredRun_FormAScheduleBGroup_FlaggedWithTheDedupedValue_AndStayCounted()
    {
        var rows = Run("T5", Distinct + 19000m);
        rows.AddRange(Run("T6", Distinct + 19000m));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var b = Assert.Single(res.ScheduleB);
        Assert.Equal(2, b.Members.Count);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification); // a flag, never a reason to leave the open count
        Assert.Equal(ScheduleBReading.NSquaredRows, b.Reading);
        Assert.Contains("410.75", b.ReadingMeaning);
        Assert.Contains("inflated row total", b.ReadingMeaning);
    }
}
