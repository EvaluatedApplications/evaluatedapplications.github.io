using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 31. Real cases: Sheffield 6 August 2017, South Yorkshire Police, seven lines of 1,762,744.00 (and Sheffield City Region
/// Combined Authority, seven of 1,985,333.00) described "AP MIGRATION CONTROL ACCOUNT" on cost centre BALANCE SHEET, part of 931 lines
/// on the day Sheffield converted its payables ledger; Birmingham July 2024, business unit "Migration Control".
/// </summary>
public class Session31Tests
{
    private static SpendRow Row(string trans, decimal net, string desc, string? costCentre, string supplier = "SOUTH YORKSHIRE POLICE", string payDate = "06/08/2017") =>
        new("2017-08", trans, supplier, payDate, AuditEngine.ParseDate(payDate), desc, "CORPORATE", net, net, null, null, 0, null, costCentre, null);

    [Fact]
    public void SevenEqualLines_AllLabelledMigrationControl_AreLabelledNotUnclear()
    {
        var rows = Enumerable.Range(0, 7).Select(i => Row("T" + i, 1762744.00m, "AP MIGRATION CONTROL ACCOUNT", "BALANCE SHEET")).ToList();
        var b = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount).ScheduleB);
        Assert.Equal(ScheduleBClassification.MigrationControlLabel, b.Classification);
        Assert.Contains("AP MIGRATION CONTROL ACCOUNT", b.ClassificationDetail);
        Assert.Contains("does not say whether cash left", b.ClassificationDetail);
    }

    [Fact]
    public void LabelInTheCostCentre_Counts_AsBirminghamUsesIt()
    {
        var rows = Enumerable.Range(0, 2).Select(i => Row("T" + i, 381487.42m, "Accounts payable PO Accrual - GRNI", "Migration Control", "CPC CIVILS")).ToList();
        var b = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount).ScheduleB);
        Assert.Equal(ScheduleBClassification.MigrationControlLabel, b.Classification);
    }

    [Fact]
    public void OneLineWithoutTheLabel_KeepsTheGroupUnclear()
    {
        var rows = new List<SpendRow>
        {
            // the group key includes the description, so a mixed group is one whose label sits in the cost centre
            Row("T1", 5000m, "Accounts payable", "Migration Control"),
            Row("T2", 5000m, "Accounts payable", "Migration Control"),
            Row("T3", 5000m, "Accounts payable", "Place"),
        };
        var b = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount).ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }

    [Fact]
    public void DebtCollectionAgencyCategory_IsNotBorrowing_ButADebtManagementExpenseStillIs()
    {
        var collect = new SpendRow("2017-01", "1", "UKSEARCH LIMITED", "12/01/2017", AuditEngine.ParseDate("12/01/2017"), "FEE EXPENSES", "RESOURCES", 370.16m, 370.16m, null, null, 0,
            null, "ICAM SHEFFIELD CITY COUNCIL", null, "Category Description=DEBT COLLECTION AGENCIES");
        Assert.False(DebtLedger.HasDebtEvidence(new[] { collect }));
        var mgmt = collect with { Description = "DEBT MANAGEMENT EXPENSES", OtherColumns = "Object Code=8780006" };
        Assert.True(DebtLedger.HasDebtEvidence(new[] { mgmt }));
        var bailiff = collect with { Description = "Debt Management - Bailiffs, Tracing", OtherColumns = null };
        Assert.False(DebtLedger.HasDebtEvidence(new[] { bailiff }));   // Liverpool: collection of money owed TO the council
        var pwlb = collect with { Description = "Asset Management Restatement Account", OtherColumns = "Expense Type=PWLB" };
        Assert.True(DebtLedger.HasDebtEvidence(new[] { pwlb }));        // Liverpool: real PWLB lines stay
    }

    [Fact]
    public void HeaderNormaliser_RenamesTheFiveSheffieldLayouts_AndTrimsCells()
    {
        var table = new List<string[]>
        {
            new[] { "Body Name", "Organisational Unit", "Business Unit No.", "Business Unit Description", "Expense Code", "Expense Code Description", "GL Date", "Document No.", "Invoice No.", "Invoiced Value", "Supplier Name", "Address Book No.", "Proclass Details Level 1", "Thomson Classification Description" },
            new[] { "SHEFFIELD CITY COUNCIL        ", "RESOURCES    ", "1", "SCC BALANCE SHEET   ", "1780", "ERROR SUSPENSE  ", "12/04/2017", "4185023", "30254", "3,792.80", "REDACTED PERSONAL DATA", "352082", "", "ADOPTION & FOSTERING   " },
        };
        var t = CityFixups.SheffieldHeader(table);
        Assert.Equal(new[] { "Body Name", "Portfolio", "Organisation Code", "Org Code Description", "Object Code", "Object Code Description", "Payment Date", "Document No.", "Invoice No", "Value", "Supplier", "Supplier No", "Category", "Category Description" }, t[0]);
        Assert.Equal("SHEFFIELD CITY COUNCIL", t[1][0]);
        Assert.Equal("ADOPTION & FOSTERING", t[1][13]);
        Assert.Equal("3,792.80", t[1][9]);
    }

    [Theory]
    [InlineData("06/08/2024 00:00", 2024, 8, 6)]   // Liverpool CSV: day 6, month 8 (the US reading would be June 8)
    [InlineData("28/10/2024 00:00", 2024, 10, 28)]
    [InlineData("20250313", 2025, 3, 13)]          // Bradford 2025 files
    [InlineData("2021-04-13", 2021, 4, 13)]        // Bradford quarterlies
    [InlineData("13/03/2025", 2025, 3, 13)]
    public void ParseDate_KnowsTheNewCityFormats(string s, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), AuditEngine.ParseDate(s));
    }

    [Fact]
    public void BradfordHeader_FourLayoutsGetOneStandardSet()
    {
        string[] annual = { "Service Code", "Service Label", "Expenditure code", "Expenditure category", "Payment date", "Transaction Number", "Net Amount £", "Supplier Number", "Supplier Name" };
        string[] y2025 = { "Organisation Code", "Service Code", "Service Label", "Expenditure Code", "Expenditure Category", "Payment Date", "Transaction Number", "Net Amount £", "Supplier Number", "Irrecoverable VAT", "SupplierName" };
        string[] y2026 = { "Organisation Code", "Service Code", "Service Label", "Expenditure Category", "Expenditure Category Description", "Payment Date", "Transaction Number", "Net Amount £", "Supplier Number", "Irrecoverable VAT", "SupplierName", "Supplier Name", "Industry Key" };
        var a = CityFixups.BradfordHeader(new List<string[]> { annual })[0];
        Assert.Equal(new[] { "Service Code", "Service Label", "Expenditure Code", "Expenditure Description", "Payment Date", "Transaction Number", "Net Amount £", "Supplier Number", "Supplier Name" }, a);
        var b = CityFixups.BradfordHeader(new List<string[]> { y2025 })[0];
        Assert.Contains("Expenditure Description", b); Assert.Contains("Expenditure Code", b); Assert.Contains("Supplier Name", b);
        var c = CityFixups.BradfordHeader(new List<string[]> { y2026 })[0];
        Assert.Equal("Expenditure Code", c[3]); Assert.Equal("Expenditure Description", c[4]);
        Assert.Equal("Supplier Name (alt)", c[10]); Assert.Equal("Supplier Name", c[11]);
    }

    [Fact]
    public void BradfordPrepare_WhichNameColumnHoldsNamesDecidesTheStandardName()
    {
        // 2026-06: names in "SupplierName", "Supplier Name" empty; 2026-08: both filled
        var june = new List<string[]> { new[] { "Net Amount £", "Supplier Number", "SupplierName", "Supplier Name" }, new[] { "822.78", "108519", "06 CARE LIMITED", "" } };
        var t = CityFixups.BradfordPrepare(june);
        Assert.Equal("Supplier Name", t[0][2]); Assert.Equal("Supplier Name (alt)", t[0][3]);
        Assert.Equal("06 CARE LIMITED", t[1][2]);
    }

    [Fact]
    public void BradfordPrepare_AnEmptyNameWithANumber_BecomesTheNumberLabel_NotAnotherFilesName()
    {
        var apr = new List<string[]>
        {
            new[] { "Net Amount £", "Supplier Number", "Supplier Name" },
            new[] { "-338829.19", "N01651", "" },
            new[] { "10.00", "", "" },
            new[] { "20.00", "N01652", "NAMED LTD" },
        };
        var t = CityFixups.BradfordPrepare(apr);
        Assert.Equal("(no name published) N01651", t[1][2]);
        Assert.Equal("", t[2][2]);
        Assert.Equal("NAMED LTD", t[3][2]);
    }

    [Fact]
    public void HeaderNormaliser_OldCertifiedDateAndMisspeltColumns()
    {
        var t = CityFixups.SheffieldHeader(new List<string[]> { new[] { "Body Name", "Portfolio", "Objective Code", "Objective Code Description", "Certified Date", "Supplier Reference", "Value", "Supplier", "Category & Description", "Suplier Number" } });
        Assert.Equal(new[] { "Body Name", "Portfolio", "Object Code", "Object Code Description", "Payment Date", "Invoice No", "Value", "Supplier", "Category Description", "Supplier No" }, t[0]);
    }
}
