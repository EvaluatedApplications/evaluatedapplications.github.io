using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 22: pins the debt ledger's evidence gate (its first real run was swamped by
/// "Debtors"/"Deferred Debt"/"Student Loan"/"Stock Loaned" noise) and the Cost-Centre-Area evidence path
/// (Wokingham's real Reading "Debt Charges" row carries the words in CostCentreArea, not Description).</summary>
public class DebtLedgerTests
{
    private static SpendRow Row(string desc, string? costCentre, decimal gross, string supplier = "Some Council", string trans = "1") =>
        new("FY", trans, supplier, "01/01/2021", null, desc, "Area", gross, gross, null, null, 0, null, costCentre);

    [Theory]
    [InlineData("Agency Staff Debtors")]
    [InlineData("ASC Deferred Debt")]
    [InlineData("Student Loan Repayments")]
    [InlineData("Stock Loaned Operational Resource")]
    public void NoiseDescriptions_AreNotDebtEvidence(string desc) =>
        Assert.False(DebtLedger.HasDebtEvidence(new[] { Row(desc, null, 1000m) }));

    [Theory]
    [InlineData("Interest Payments", null)]
    [InlineData("TPP - Waste Collection", "Debt Charges")]
    [InlineData("Loan repayment", null)]
    [InlineData("PWLB instalment", null)]
    public void RealDebtWording_IsEvidence_InEitherField(string desc, string? cc) =>
        Assert.True(DebtLedger.HasDebtEvidence(new[] { Row(desc, cc, 1000m) }));

    [Fact]
    public void Scan_DecomposesRoundPrincipalPlusInterest_AndReportsImpliedRate()
    {
        var rows = new[] { Row("TPP - Waste Collection", "Debt Charges", 728868.66m, "Reading Borough Council", "3583877") };
        var e = Assert.Single(DebtLedger.Scan("Wokingham", rows, GrossMeaning.RepeatedInvoiceTotal));
        Assert.True(e.Decomposed);
        Assert.Equal(700000m, e.Principal);
        Assert.Equal(28868.66m, e.Interest);
        Assert.Equal(4.1241m, e.ImpliedRatePercent);
    }

    [Fact]
    public void Scan_CarriesNetClassAndDecodeColumns()
    {
        // Wokingham -> Wandsworth style line: gross = principal + interest, net = interest.
        var row = new SpendRow("FY", "7", "Wandsworth Borough Council", "10/01/2023", null, "Interest Payments", "Area",
            55150.68m, 10055150.68m, null, null, 0, null, "Debt Charges");
        var e = Assert.Single(DebtLedger.Scan("Wokingham Borough Council", new[] { row }, GrossMeaning.RepeatedInvoiceTotal));
        Assert.Equal(55150.68m, e.Net);
        Assert.Equal(CounterpartyClass.InterAuthority, e.Class);
        Assert.NotNull(e.Decode);
    }

    [Fact]
    public void FlagCrossCouncilCounterparties_MatchesNormalisedCouncilNames()
    {
        var ledger = new[] { new DebtLedgerEntry("Wokingham Borough Council", "FY2020-21", "9", "Reading Borough Council", null, 1m, false, 0m, 0m, null, "Debt Charges") };
        var flags = DebtLedger.FlagCrossCouncilCounterparties(ledger, new[] { "Reading Borough Council" });
        Assert.Single(flags);
    }
}
