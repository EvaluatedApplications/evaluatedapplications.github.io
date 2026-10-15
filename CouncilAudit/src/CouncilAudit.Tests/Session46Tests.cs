using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 46: City of York Council (NextCouncils.York). Every case is a real file name, header or date from the 15 annual CSVs on
/// data.yorkopendata.org (data.gov.uk "All Payments to Suppliers").
/// </summary>
public class Session46Tests
{
    [Theory]
    [InlineData("over500payments2011.csv", "FY2011-12")]
    [InlineData("over500payments2012.csv", "FY2012-13")]
    [InlineData("allpayments2013.csv", "FY2013-14")]
    [InlineData("allpayments2017.csv", "FY2017-18")]
    [InlineData("over250payments2018.csv", "FY2018-19")]
    [InlineData("over250payments2025.csv", "FY2025-26")]
    [InlineData("notes.csv", "")]
    public void YorkTagIsTheFinancialYearTheFileNameStartsIn(string name, string expected) => Assert.Equal(expected, NextFixups.YorkTag(name));

    [Fact]
    public void YorkHeadersOfTheFourLayoutsBecomeOneSet()
    {
        // 2011/12 to 2014/15
        Assert.Equal(new[] { "Organisation Name", "Directorate", "Expense Category", "ExpenditureCode", "Date", "Transaction Number", "Net Amount", "Supplier Name" },
            NextFixups.CanonHeader(new[] { "BodyName", "OrganisationUnit", "ExpenseCategory", "ExpenditureCode", "Date", "TransactionNumber", "Amount", "SupplierName" }, NextFixups.YorkCanon));
        // 2015/16 to 2017/18
        Assert.Equal(new[] { "Organisation Name", "Directorate", "Department", "Expense Category", "Supplier Name", "SupplierID", "Date", "Transaction Number", "Net Amount" },
            NextFixups.CanonHeader(new[] { "OrganisationName", "Directorate", "Department", "ExpenseCategory", "Supplier_Beneficiary", "SupplierID", "PaymentDate", "TransactionReference", "NetAmount_ExcVAT" }, NextFixups.YorkCanon));
        // 2018/19: GL_Date stays a separate column; the payment date is Payment_Date
        var g = NextFixups.CanonHeader(new[] { "Organisation_Name", "Directorate", "Department", "Service_Plan", "Creditor_Name", "GL_Date", "Payment_Date", "Transaction_No", "Net_Amount", "Subjective_Detail" }, NextFixups.YorkCanon);
        Assert.Equal("GL Date", g[5]);
        Assert.Equal("Date", g[6]);
        Assert.Equal("Transaction Number", g[7]);
        Assert.Equal("Expense Category", g[9]);
    }

    [Fact]
    public void YorkTableCanonsOnlyTheHeaderRow()
    {
        var t = new List<string[]> { new[] { "BodyName", "Creditor_Name", "Amount" }, new[] { "City of York Council", "Amount", "12" } };
        var r = NextFixups.YorkTable(t);
        Assert.Equal(new[] { "Organisation Name", "Supplier Name", "Net Amount" }, r[0]);
        Assert.Equal(new[] { "City of York Council", "Amount", "12" }, r[1]);
        Assert.Equal(new[] { "BodyName", "Creditor_Name", "Amount" }, t[0]);   // the input is not changed
    }

    [Theory]
    [InlineData("04/04/2011", "04/04/2011")]
    [InlineData("25/04/2024 00:00", "25/04/2024")]
    [InlineData("7/2/2025", "07/02/2025")]
    [InlineData("not a date", "not a date")]
    public void YorkDatesAreWrittenInFull(string given, string expected) => Assert.Equal(expected, NextFixups.YorkDate(given));

    [Fact]
    public void YorkProfileHasFifteenFinancialYears()
    {
        Assert.Equal(15, NextCouncils.York.Years.Count);
        Assert.Equal("FY2011-12", NextCouncils.York.Years[0].Tag);
        Assert.Equal("FY2025-26", NextCouncils.York.Years[^1].Tag);
        Assert.Equal("york", NextCouncils.SlugByName["City of York Council"]);
    }
}
