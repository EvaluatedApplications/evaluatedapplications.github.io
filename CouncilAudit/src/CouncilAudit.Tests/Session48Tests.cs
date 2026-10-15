using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 48: London Borough of Camden (NextCouncils.Camden). Every case is a real file name, header or payee from the one
/// Socrata export on opendata.camden.gov.uk (view 3ixw-qvb8), split by payment month.
/// </summary>
public class Session48Tests
{
    [Theory]
    [InlineData("Camden 2019-09.csv", "2019-09")]
    [InlineData("Camden 2023-12.csv", "2023-12")]
    [InlineData("Camden 2026-08.csv", "2026-08")]
    [InlineData("camden_spend.csv", "")]
    [InlineData("Camden 2026-13.csv", "")]
    public void CamdenTagIsTheMonthOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.CamdenTag(name));

    [Fact]
    public void CamdenHeadersBecomeTheStandardSet()
    {
        var h = NextFixups.CanonHeader(new[] { "Organisation Label", "Organisation URI", "Payment Date", "Payment Month", "Payment Year", "Financial Year", "Beneficiary Name",
            "Purpose", "Organisational Unit", "Amount GBP", "Irrecoverable VAT Amount GBP", "Latest Financial Year", "Latest Year", "Latest Month", "Unique Identifier" }, NextFixups.CamdenCanon);
        Assert.Equal(new[] { "Organisation Label", "Organisation URI", "Date", "Payment Month", "Payment Year", "Financial Year", "Supplier Name",
            "Purpose", "Organisational Unit", "Net Amount", "Irrecoverable VAT Amount GBP", "Latest Financial Year", "Latest Year", "Latest Month", "Transaction Number" }, h);
    }

    [Theory]
    [InlineData("04/08/2026", "04/08/2026")]
    [InlineData("20/08/2026", "20/08/2026")]
    [InlineData("7/9/2020", "07/09/2020")]
    public void CamdenDatesAreWrittenInFull(string given, string expected) => Assert.Equal(expected, NextFixups.CamdenDate(given));

    [Fact]
    public void CamdenProfileHasEightyFourMonths()
    {
        Assert.Equal(84, NextCouncils.Camden.Years.Count);
        Assert.Equal("2019-09", NextCouncils.Camden.Years[0].Tag);
        Assert.Equal("2026-08", NextCouncils.Camden.Years[^1].Tag);
        Assert.Equal("camden", NextCouncils.SlugByName["London Borough of Camden"]);
    }

    // PREREG C5 was REFUTED: the council marks 43 payee names with a trailing run of asterisks (for 19 the same name also appears without), and the
    // supplier key does NOT drop the marker, because the company-suffix strip needs the suffix at the end ("... LIMITED****" keeps LIMITED). Measured
    // effect on the schedules: 0 groups of the same payee, amount and date mix a starred and a plain spelling, so no Schedule B group is split. The key is
    // left as it is (a shared rule changes for no measured gain); this test records the behaviour so a later change is deliberate.
    [Fact]
    public void CamdenAsteriskMarkerKeepsItsOwnSupplierKey()
    {
        Assert.NotEqual(SupplierKey.Normalize("24HR AQUAFLOW SERVICES LIMITED"), SupplierKey.Normalize("24HR AQUAFLOW SERVICES LIMITED****"));
    }
    // Amendment 1: Camden's Purpose label "Other Debtor Entities and Individuals" (money owed TO the council: Cyclescheme, Capita Travel and Events) is not borrowing.
    [Fact]
    public void CamdenDebtorPurposeIsNotDebtEvidenceButRealLoanWordingStillIs()
    {
        var row = new SpendRow("2019-09", "sep-19-7613", "CAPITA TRAVEL AND EVENTS LTD", "26/09/2019", null, "Capita", "Area", 14184.67m, 14184.67m, null, null, 0, null, null, null,
            "Purpose=Other Debtor Entities and Individuals");
        Assert.False(DebtLedger.HasDebtEvidence(new[] { row }));
        Assert.True(DebtLedger.HasDebtEvidence(new[] { row with { OtherColumns = "Purpose=Loan repayment" } }));
    }

    [Fact]
    public void CamdenRedactionLabelsAreRedactedAndRedactiveIsNot()
    {
        Assert.True(AuditEngine.IsRedactedSupplier("xxxxREDACTEDxxxx"));
        Assert.True(AuditEngine.IsRedactedSupplier("REDACTED"));
        Assert.True(AuditEngine.IsRedactedSupplier("Redacted"));
        Assert.False(AuditEngine.IsRedactedSupplier("REDACTIVE EVENTS"));
    }
}
