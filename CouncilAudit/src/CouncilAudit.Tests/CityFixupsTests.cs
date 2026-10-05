using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 30: pins the facts found on the first two big cities' real files (Leeds, Birmingham). Every row below
/// is a real published row.</summary>
public class CityFixupsTests
{
    private static readonly string[] LeedsOldHeader = new[] { "Organisation Code", "Organisation Label", "Effective Date", "Organisational Unit",
        "Service Division Label", "Category Internal Name", "Purpose", "Detailed Expenditure Code", "Payment Date", "Transaction Number",
        "Irrecoverable VAT Amount", "Amount", "Capital Or Revenue", "Benificiary Name", "Procurement Card" };

    private static readonly string[] LeedsRow = new[] { "Http://opendatacommunities.org/id/metropolitan-district-council/leeds", "Leeds City Council",
        "30/04/2022", "Adults and Health", "Services For Older People Under 65 P/Dis", "Agency Payments", "Sheltered Accommodation", "569",
        "27/04/2022", "2204-00001", "0.00", "5400.00", "R", "A & V TRANSITIONAL HOMES", "No" };

    [Fact]
    public void LeedsHeader_RenamesTheMisspeltPayeeColumn_AndLeavesDataCellsAndCorrectFilesAlone()
    {
        var old = new List<string[]> { LeedsOldHeader, LeedsRow };
        var fixedTable = CityFixups.LeedsHeader(old);
        Assert.Equal("Beneficiary Name", fixedTable[0][13]);
        Assert.Same(LeedsRow, fixedTable[1]);
        Assert.Equal("Benificiary Name", old[0][13]); // the input table is not mutated

        var modern = LeedsOldHeader.ToArray();
        modern[13] = "Beneficiary Name";
        var already = new List<string[]> { modern, LeedsRow };
        Assert.Same(already, CityFixups.LeedsHeader(already));
    }

    [Fact]
    public void LeedsMapping_ReadsTheRealRow_AfterTheHeaderFixup()
    {
        var warnings = new List<string>();
        var rows = AuditEngine.MapRows(CityFixups.LeedsHeader(new List<string[]> { LeedsOldHeader, LeedsRow }), SupportedCouncils.Leeds.Mapping, "2022-04", warnings);
        Assert.Empty(warnings);
        var r = Assert.Single(rows);
        Assert.Equal("2204-00001", r.TransactionId);
        Assert.Equal("A & V TRANSITIONAL HOMES", r.Supplier);
        Assert.Equal(5400.00m, r.Gross);
        Assert.Equal(5400.00m, r.Net); // no VAT split is published, so net equals the one amount
        Assert.Equal(new DateOnly(2022, 4, 27), r.PayDate);
        Assert.Equal("Sheltered Accommodation", r.Description);
        Assert.Contains("Capital Or Revenue=R", r.OtherColumns);
        Assert.Contains("Irrecoverable VAT Amount=0.00", r.OtherColumns); // kept, not silently dropped
    }

    [Fact]
    public void LeedsMapping_WithoutTheFixup_FindsNoSupplierColumn_WhichIsWhyTheFixupExists()
    {
        var warnings = new List<string>();
        AuditEngine.MapRows(new List<string[]> { LeedsOldHeader, LeedsRow }, SupportedCouncils.Leeds.Mapping, "2018-04", warnings);
        Assert.Contains(warnings, w => w.Contains("supplier column"));
    }

    private static readonly string[] BirminghamHeader = { "date", "department", "beneficiary", "summary", "amount", "merchant_category", "supplier_number" };

    [Fact]
    public void BirminghamMapping_UsesASyntheticRowRef_AndMapsTheSevenPublishedFields()
    {
        var table = new List<string[]>
        {
            BirminghamHeader,
            new[] { "2026-07-23", "City Ops Directorate", "Birmingham Highways Ltd", "PFI Contract Management Team BU", "1748559.22", "Other Purchased Services", "B144361" },
            new[] { "2026-07-22", "City Housing Directorate", "Chubb Fire & Security Limited", "Housing Management BU", "1200.0", "Accounts payable PO  Accrual  - GRNI", "B112029" },
        };
        var warnings = new List<string>();
        var rows = AuditEngine.MapRows(WestBerkshireFixups.AddSyntheticRowId(table), SupportedCouncils.Birmingham.Mapping, "2026-07", warnings);
        Assert.Empty(warnings);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "(no number published) 1", "(no number published) 2" }, rows.Select(r => r.TransactionId).ToArray());   // Session 53
        Assert.Equal(1748559.22m, rows[0].Gross);
        Assert.Equal(new DateOnly(2026, 7, 23), rows[0].PayDate);
        Assert.Equal("Other Purchased Services", rows[0].Description);
        Assert.Equal("City Ops Directorate", rows[0].ServiceArea);
        Assert.Equal("PFI Contract Management Team BU", rows[0].CostCentreArea);
        Assert.Contains("supplier_number=B144361", rows[0].OtherColumns);
        Assert.Equal("Accounts payable PO  Accrual  - GRNI", rows[1].Description);
    }

    [Fact]
    public void BirminghamRedactedBeneficiary_IsExcludedFromTheSchedulesAndCounted()
    {
        var table = new List<string[]>
        {
            BirminghamHeader,
            new[] { "2026-07-20", "Adult Social Care & Health Directorate", "REDACTED - PERSONAL DATA", "Adult Care Packages BU", "1689.24", "Adult Residential Care", "O120272" },
            new[] { "2026-07-20", "Adult Social Care & Health Directorate", "REDACTED - PERSONAL DATA", "Adult Care Packages BU", "1689.24", "Adult Residential Care", "O120273" },
        };
        var warnings = new List<string>();
        var rows = AuditEngine.MapRows(WestBerkshireFixups.AddSyntheticRowId(table), SupportedCouncils.Birmingham.Mapping, "2026-07", warnings);
        var result = AuditEngine.Run(rows, SupportedCouncils.Birmingham.IdScope, SupportedCouncils.Birmingham.GrossMeaning, warnings);
        Assert.Equal(0, result.RawRowCount);      // both rows withheld
        Assert.Empty(result.ScheduleB);           // two identical redacted rows are NOT a repeated payment
        Assert.Contains(result.Warnings, w => w.StartsWith("2 rows carry a redacted"));
    }

    [Theory]
    [InlineData("14-Sep-2018", 2018, 9, 14)] // Leeds 2018-09 (45,602 rows across 2018-02 and 2018-09 use this form)
    [InlineData("22-Jan-2018", 2018, 1, 22)] // Leeds 2018-02
    [InlineData("27/04/2022", 2022, 4, 27)]  // the form of every other Leeds file
    [InlineData("2026-07-23", 2026, 7, 23)]  // Birmingham
    public void EngineDateParser_ReadsEveryDateFormatTheCitiesPublish(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), AuditEngine.ParseDate(raw));
    }

    [Fact]
    public void LeedsGarbagePaymentDate_IsNotInvented()
    {
        // Leeds 2018-09, transaction 1809-20877 publishes the Payment Date "214" (one row).
        Assert.Null(AuditEngine.ParseDate("214"));
    }

    [Fact]
    public void CityLists_AreKeptApartFromTheSixCouncilBaseline()
    {
        Assert.Equal(6, SupportedCouncils.All.Count);
        Assert.Equal(SupportedCouncils.All.Count + SupportedCouncils.Cities.Count, SupportedCouncils.Everyone.Count);
        Assert.Equal(116, SupportedCouncils.Sheffield.Years.Count);   // 2017-01 .. 2026-08, every month present
        Assert.Equal(25, SupportedCouncils.Birmingham.Years.Count);   // 2024-07 .. 2026-07
        Assert.Contains(SupportedCouncils.Leeds.Years, y => y.Tag == "2020-03"); // the file Data Mill North names spending_2019_12.csv
        Assert.Equal(111, SupportedCouncils.Leeds.Years.Count);       // 111 monthly files, April 2017 to June 2026
    }
}
