using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 29: Reading's FY2017-18 to FY2019-20 back-catalogue (an older finance-system export with different column
/// names and, in 2018-Q1, a title row above the header) and the pure rules of the declared-spend reconciliation.
/// Inputs are the smallest synthetic shapes copied from the real files (reading_2017-Q1.xlsx, reading_2018-Q1.xlsx).
/// </summary>
public class Session29Tests
{
    [Fact]
    public void OldReadingHeaders_MapToTheCurrentNames()
    {
        // header row of reading_2017-Q1.xlsx, with the trailing spaces reading_2018-Q1.xlsx carries
        var table = new List<string[]>
        {
            new[] { "Transaction Number ", "Supplier Name ", "Payment date", "Invoice Distribution Amount ", "Subjective Description", "Directorate" },
        };
        ReadingFixups.ApplyHeaderFixups(table, ReadingFixups.HeaderFixups);
        Assert.Equal("Voucher Number (Internal Classification)", table[0][0]);
        Assert.Equal("Payment Date", table[0][2]);
        Assert.Equal("Amount (£)", table[0][3]);
        Assert.Equal("Purpose", table[0][4]);
        Assert.Equal("Directorate", table[0][5]);
    }

    [Fact]
    public void TitleRowAboveTheHeader_IsSkipped()
    {
        // reading_2018-Q1.xlsx opens with "All Transaction Payments Over GBP500 - April to June 2018"
        var table = new List<string[]>
        {
            new[] { "All Transaction Payments Over £500 - April to June 2018", "", "" },
            new[] { "Transaction Number ", "Supplier Name ", "Payment date" },
            new[] { "4076803", "REED ", "43269.04" },
        };
        var fixedTable = ReadingFixups.FixXlsxLayout(table);
        Assert.Equal(2, fixedTable.Count);
        Assert.Equal("Supplier Name ", fixedTable[0][1]);
    }

    [Fact]
    public void FileWithoutATitleRow_IsUnchanged()
    {
        var table = new List<string[]>
        {
            new[] { "Purchasing Organisation", "Payment Date", "Voucher Number (Internal Classification)", "Supplier Name" },
            new[] { "Reading Borough Council", "03/08/2026", "408085", "SIGNWAY SUPPLIES" },
        };
        Assert.Equal(2, ReadingFixups.FixXlsxLayout(table).Count);
    }

    [Theory]
    [InlineData("2024-03-31", "2023-24")]
    [InlineData("2024-04-01", "2024-25")]
    [InlineData("2025-01-15", "2024-25")]
    [InlineData("1999-12-31", "1999-00")]
    public void FinancialYear_RunsAprilToMarch(string date, string expected) =>
        Assert.Equal(expected, BudgetRules.FinancialYear(DateTime.Parse(date)));

    [Theory]
    [InlineData("HM Revenue & Customs", "Payments to HMRC")]
    [InlineData("HMRC", "Payments to HMRC")]
    [InlineData("Berkshire Pension Fund", "Payments to pension funds")]
    [InlineData("Police and Crime Commissioner for Thames Valley", "Police precept and other payments to the police authority")]
    [InlineData("Royal Berkshire Fire & Rescue Service", "Payments to the fire authority")]
    [InlineData("Shinfield Parish Council", "Payments to parish councils")]
    [InlineData("Wokingham Town Council", "Payments to town councils")]
    public void ObservedPayees_AreNamedByRule(string supplier, string expectedClass) =>
        Assert.Equal(expectedClass, BudgetRules.ObservedClass(supplier));

    [Theory]
    [InlineData("Optalis Limited")]
    [InlineData("Fire Protection Services Ltd")] // 'FIRE' alone is not the fire authority
    [InlineData("Policeman's Charity Ltd")]       // 'POLICE' without commissioner/authority wording
    public void OrdinarySuppliers_AreNotObserved(string supplier) =>
        Assert.Null(BudgetRules.ObservedClass(supplier));
}
