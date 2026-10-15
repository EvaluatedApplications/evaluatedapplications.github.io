using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 47: Calderdale Metropolitan Borough Council (NextCouncils.Calderdale). Every case is a real file name, header or cell from the
/// 77 monthly CSVs on dataworks.calderdale.gov.uk (data.gov.uk "Payments to suppliers 2020-21" to "2026-27").
/// </summary>
public class Session47Tests
{
    [Theory]
    [InlineData("April 2020.csv", "2020-04")]
    [InlineData("December 2020.csv", "2020-12")]
    [InlineData("September 2023.csv", "2023-09")]
    [InlineData("August 2026.csv", "2026-08")]
    [InlineData("readme.txt", "")]
    public void CalderdaleTagIsTheMonthOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.CalderdaleTag(name));

    [Fact]
    public void CalderdaleHeadersOfTheTwoLayoutsBecomeOneSet()
    {
        // 70 files start with "Body"; the seven files from February 2026 do not
        var withBody = NextFixups.CanonHeader(new[] { "Body", "Organisation Name", "Date", "Internal Ref Number", "Net Amount", "Irrecoverable VAT", "Supplier(Beneficiary) Name",
            "Supplier ID", "Expense Area", "Procurement Classification: Council's Procurement Category Name", "Definition", "Purpose of Spend (Summary)", "Voluntary Community and Social Enterprise Supplier" }, NextFixups.CalderdaleCanon);
        Assert.Equal(new[] { "Body", "Organisation Name", "Date", "Transaction Number", "Net Amount", "Irrecoverable VAT", "Supplier Name",
            "Supplier ID", "Expense Area", "Procurement Category", "Definition", "Purpose of Spend", "VCSE Supplier" }, withBody);
        var without = NextFixups.CanonHeader(new[] { "Organisation Name", "Date", "Internal Ref Number", "Net Amount", "Supplier(Beneficiary) Name" }, NextFixups.CalderdaleCanon);
        Assert.Equal(new[] { "Organisation Name", "Date", "Transaction Number", "Net Amount", "Supplier Name" }, without);
    }

    [Fact]
    public void CalderdaleTableCansOnlyTheHeaderRow()
    {
        var t = new List<string[]> { new[] { "Date", "Supplier(Beneficiary) Name", "Net Amount" }, new[] { "02/04/2026", "Supplier(Beneficiary) Name", "52500" } };
        var r = NextFixups.CalderdaleTable(t);
        Assert.Equal(new[] { "Date", "Supplier Name", "Net Amount" }, r[0]);
        Assert.Equal(new[] { "02/04/2026", "Supplier(Beneficiary) Name", "52500" }, r[1]);
    }

    [Theory]
    [InlineData("02/04/2026", "02/04/2026")]
    [InlineData("2/4/2026", "02/04/2026")]
    [InlineData("27/04/2022", "27/04/2022")]
    public void CalderdaleDatesAreWrittenInFull(string given, string expected) => Assert.Equal(expected, NextFixups.CalderdaleDate(given));

    [Fact]
    public void CalderdaleProfileHasSeventySevenMonths()
    {
        Assert.Equal(77, NextCouncils.Calderdale.Years.Count);
        Assert.Equal("2020-04", NextCouncils.Calderdale.Years[0].Tag);
        Assert.Equal("2026-08", NextCouncils.Calderdale.Years[^1].Tag);
        Assert.Equal("calderdale", NextCouncils.SlugByName["Calderdale Metropolitan Borough Council"]);
    }

    [Theory]
    [InlineData("REDACTED PERSONAL DATA", true)]
    [InlineData("REDACTIVE PUBLISHING LIMITED", false)]
    [InlineData("PERSONAL CARE CONSULTANTS LTD", false)]
    public void CalderdalePlaceholderIsTheOneSpellingAndRealNamesStay(string payee, bool redacted) => Assert.Equal(redacted, AuditEngine.IsRedactedSupplier(payee));
}
