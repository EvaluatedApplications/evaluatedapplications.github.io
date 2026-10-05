using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 30 rule from a hand-opened real case. Leeds, file 2017-06, 19 June 2017, payee "The Secretary Of State": the monthly
/// settlement (Business Rates Tariffs -5,660,668.00, Revenue Support Grant -5,851,504.00, NNDR Income 16,217,763.00) is posted as
/// 1706-21397..21399, again as 1706-21400..21402, and then once more with every sign reversed as 1706-21403..21405.
/// Two copies less one reversal is one settlement, so Schedule B must not present NNDR Income as paid twice.
/// </summary>
public class Session30Tests
{
    private static SpendRow Row(string trans, decimal net, string desc, string supplier = "The Secretary Of State", string payDate = "19/06/2017", string source = "2017-06") =>
        new(source, trans, supplier, payDate, AuditEngine.ParseDate(payDate), desc, "Strategic", net, net, null, null, 0, null, null, null);

    private static List<SpendRow> Leeds19June2017() => new()
    {
        Row("1706-21397", -5660668.00m, "Business Rates Tariffs"),
        Row("1706-21398", -5851504.00m, "Revenue Support Grant"),
        Row("1706-21399", 16217763.00m, "NNDR Income"),
        Row("1706-21400", -5660668.00m, "Business Rates Tariffs"),
        Row("1706-21401", -5851504.00m, "Revenue Support Grant"),
        Row("1706-21402", 16217763.00m, "NNDR Income"),
        Row("1706-21403", 5660668.00m, "Business Rates Tariffs"),
        Row("1706-21404", 5851504.00m, "Revenue Support Grant"),
        Row("1706-21405", -16217763.00m, "NNDR Income"),
    };

    [Fact]
    public void TwoCopiesAndOneSameDayReversal_IsReversedSameDay()
    {
        var result = AuditEngine.Run(Leeds19June2017(), TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var nndr = Assert.Single(result.ScheduleB, b => b.Net == 16217763.00m);
        Assert.Equal(ScheduleBClassification.ReversedSameDay, nndr.Classification);
        Assert.Contains("2 copies and 1 equal-and-opposite", nndr.ClassificationDetail);
        Assert.Contains("1 payment(s) net", nndr.ClassificationDetail);
    }

    [Fact]
    public void TheNegativeLinesThemselves_AreNeverGroupedAsRepeats()
    {
        // Business Rates Tariffs: -5,660,668.00 twice and +5,660,668.00 once. The two negative lines are a group of
        // credit-like members, which the engine never labels by the rule above.
        var result = AuditEngine.Run(Leeds19June2017(), TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        foreach (var b in result.ScheduleB.Where(b => b.Net < 0m))
            Assert.NotEqual(ScheduleBClassification.ReversedSameDay, b.Classification);
    }

    [Fact]
    public void ThreeCopiesAndOneReversal_StaysUnclear_TwoNetCopiesAreStillTwo()
    {
        var rows = new List<SpendRow>
        {
            Row("T1", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T2", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T3", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T4", -1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }

    [Fact]
    public void AReversalOnAnotherDay_IsNotACancellation()
    {
        var rows = new List<SpendRow>
        {
            Row("T1", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T2", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T3", -1000m, "Fees", "ACME LTD", "06/03/2024", "FY1"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }

    [Fact]
    public void AReversalWithADifferentDescription_IsNotACancellation()
    {
        var rows = new List<SpendRow>
        {
            Row("T1", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T2", 1000m, "Fees", "ACME LTD", "05/03/2024", "FY1"),
            Row("T3", -1000m, "Refund of deposit", "ACME LTD", "05/03/2024", "FY1"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }
}
