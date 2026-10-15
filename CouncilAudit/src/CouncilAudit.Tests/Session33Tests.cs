using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 33. Real case: Bracknell Forest, Queensmead School (SEN non-LEA provision): 12 or 13 payments of 27,223 on one date every
/// quarter for years. The rule explains a batch no larger than the rate's largest batch on another date and leaves larger ones open.
/// </summary>
public class Session33Tests
{
    private static List<SpendRow> Batch(string date, int n, decimal net, string supplier = "QUEENSMEAD SCHOOL")
    {
        var d = AuditEngine.ParseDate(date);
        return Enumerable.Range(0, n).Select(i => new SpendRow("Q" + date, date.Replace("/", "") + "-" + i, supplier, date, d, "SEN placement", "CYPL", net, net, null, null, 0, null, "High needs", null)).ToList();
    }

    private static List<SpendRow> Quarterly(params (string date, int n)[] batches) => batches.SelectMany(b => Batch(b.date, b.n, 27223m)).ToList();

    [Fact]
    public void ABatchNoLargerThanAnotherBatchOfTheSameRate_IsExplained()
    {
        // as Queensmead: 13, 13, 12, 12, 10 (the two largest batches match each other, so neither exceeds another)
        var rows = Quarterly(("01/03/2022", 13), ("08/03/2022", 13), ("01/09/2022", 12), ("01/12/2022", 10));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Equal(4, res.ScheduleB.Count);
        Assert.All(res.ScheduleB, b => { Assert.Equal(ScheduleBReading.RecurringBatchRate, b.Reading); Assert.Equal(ScheduleBClassification.Unclear, b.Classification); });
        Assert.Contains("no more than", res.ScheduleB[0].ReadingMeaning);
    }

    [Fact]
    public void TheOnlyLargestBatch_IsNotExplained_AndStaysOpen()
    {
        var rows = Quarterly(("01/03/2022", 13), ("01/06/2022", 12), ("01/09/2022", 12), ("01/12/2022", 10), ("01/03/2023", 20));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var big = res.ScheduleB.Single(b => b.Members.Count == 20);
        Assert.Equal(ScheduleBClassification.Unclear, big.Classification); Assert.Equal(ScheduleBReading.None, big.Reading);
        Assert.Equal(4, res.ScheduleB.Count(b => b.Reading == ScheduleBReading.RecurringBatchRate && b.Classification == ScheduleBClassification.Unclear));
    }

    private static SpendRow Tariff(string trans, string date, decimal net) =>
        new("T", trans, "DCLG", date, AuditEngine.ParseDate(date), "Business Rates Tariff", "Central Services", net, net, null, null, 0, null, "Tariff", null);

    private static List<SpendRow> MonthlyTariff()
    {
        var rows = new List<SpendRow>();
        int n = 0;
        foreach (var (y, m) in new[] { (2025, 4), (2025, 5), (2025, 6), (2025, 7), (2025, 8), (2025, 9), (2025, 10), (2025, 11), (2025, 12), (2026, 1) })
            rows.Add(Tariff("M" + n++, $"20/{m:00}/{y}", 2649992m + n));
        return rows;
    }

    [Fact]
    public void ThreePaymentsOfANewRate_AfterFourEmptyMonths_AreACadenceCatchUp()
    {
        var rows = MonthlyTariff();
        rows.Add(Tariff("N1", "19/06/2026", 4887566m)); rows.Add(Tariff("N2", "19/06/2026", 4887566m)); rows.Add(Tariff("N3", "30/06/2026", 4887566m));
        var b = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount).ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification); Assert.Equal(ScheduleBReading.CadenceCatchUp, b.Reading);
        Assert.Contains("3 payments", b.ReadingMeaning);
    }

    [Fact]
    public void TwoPaymentsOfTheSameRate_WithNoEmptyMonthBefore_StayUnclear()
    {
        var rows = MonthlyTariff();
        rows.Add(Tariff("N1", "20/02/2026", 3000000m)); rows.Add(Tariff("N2", "20/02/2026", 3000000m));
        var b = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount).ScheduleB);
        // February 2026 follows January 2026, which is paid, so no empty month can absorb the second payment
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification); Assert.Equal(ScheduleBReading.None, b.Reading);
    }

    [Fact]
    public void ThreeBatchDates_AreNotARate()
    {
        var rows = Quarterly(("01/03/2022", 13), ("01/06/2022", 12), ("01/09/2022", 12));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.All(res.ScheduleB, b => Assert.Equal(ScheduleBReading.None, b.Reading));
    }

    [Fact]
    public void FourDatesInOneMonth_AreNotARate()
    {
        var rows = Quarterly(("01/03/2022", 4), ("08/03/2022", 4), ("15/03/2022", 4), ("22/03/2022", 4));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.All(res.ScheduleB, b => Assert.Equal(ScheduleBReading.None, b.Reading));
    }

    [Fact]
    public void PairsOnFourDates_DoNotDefineARate()
    {
        var rows = Quarterly(("01/03/2022", 2), ("01/06/2022", 2), ("01/09/2022", 2), ("01/12/2022", 2));
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.All(res.ScheduleB, b => Assert.Equal(ScheduleBReading.None, b.Reading));
    }
}
