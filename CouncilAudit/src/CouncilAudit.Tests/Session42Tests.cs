using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 42: Wokingham payments to private social care providers (PREREG_S42_SOCIALCARE.md). Real numbers from the published
/// files are the test cases.
/// </summary>
public class Session42Tests
{
    private static SocialCare.CareLine L(string txn, string sup, string pay, string? inv, decimal net, string vat = "EXEM", string type = "Home care") =>
        new("FY2022-23", txn, sup, DateOnly.Parse(pay), inv is null ? null : DateOnly.Parse(inv), net, vat, type, "OP Domiciliary Care");

    [Theory]
    // Barchester Healthcare, 14 Jun 2024: 5,314.29 = 1,200 a week for May's 31 days; 4,428.57 = 1,000 a week.
    [InlineData("5314.29", "2024-06-14", 1200, 31)]
    [InlineData("4428.57", "2024-06-14", 1000, 31)]
    // Barchester residential 2024-25: one client at 480 a week, billed for March (31) and June (30).
    [InlineData("2125.71", "2024-04-09", 480, 31)]
    [InlineData("2057.14", "2024-07-15", 480, 30)]
    public void WeeklyRate_DecodesSeventhsPricedInvoices_UnderThePreviousMonthPrior(string amount, string pay, int weekly, int days)
    {
        var r = SocialCare.WeeklyRate(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(pay));
        Assert.NotNull(r);
        Assert.Equal(weekly, r!.Value.Weekly);
        Assert.Equal(days, r.Value.Days);
    }

    [Fact]
    public void WeeklyRate_WholePoundsAndNonSevenths_AreNotDecoded()
    {
        Assert.Null(SocialCare.WeeklyRate(43500.00m, new DateOnly(2024, 4, 4)));   // Resiaction: whole pounds, period unreadable
        Assert.Null(SocialCare.WeeklyRate(31664.33m, new DateOnly(2025, 7, 1)));   // Jasmine Gardens: a daily rate x days, not sevenths
        Assert.False(SocialCare.IsSevenths(31664.33m));
        Assert.True(SocialCare.IsSevenths(51428.57m));                             // 12,000 a week x 30 days
        // 51,428.57 paid 2 Nov 2023 is September's invoice: the previous-month prior (October, 31 days) does not give a whole pound.
        Assert.Null(SocialCare.WeeklyRate(51428.57m, new DateOnly(2023, 11, 2)));
    }

    [Fact]
    public void CompanyDates_FlagOnlyWhatTheDatesShow()
    {
        // Network Healthcare Ltd: paid to 31 Jul 2025; the only company of that name (dormant) was dissolved 24 May 2022.
        Assert.Equal(SocialCare.CompanyDateFlag.PaidAfterDissolution,
            SocialCare.CheckCompanyDates(new DateOnly(2020, 5, 28), new DateOnly(2025, 7, 31), new DateOnly(2013, 12, 4), new DateOnly(2022, 5, 24)));
        // A later company re-using a name (Jasmine Care Limited, 2025) against payments in 2020-21: the date test fires, the match is wrong.
        Assert.Equal(SocialCare.CompanyDateFlag.PaidBeforeIncorporation,
            SocialCare.CheckCompanyDates(new DateOnly(2020, 4, 2), new DateOnly(2021, 9, 14), new DateOnly(2025, 5, 5), null));
        Assert.Equal(SocialCare.CompanyDateFlag.None,
            SocialCare.CheckCompanyDates(new DateOnly(2020, 5, 4), new DateOnly(2021, 4, 15), new DateOnly(2005, 1, 10), new DateOnly(2022, 1, 4))); // Sevilles Limited
    }

    [Fact]
    public void InvoiceDateRepeats_FindBridgesHomeCare_AndIgnoreASameDayBatch()
    {
        var lines = new List<SocialCare.CareLine>
        {
            // Bridges Home Care Ltd: 50,000.00 twice, one invoice date (18 Aug 2022), paid 1 and 14 Sep 2022, VAT codes OSCP and ZERO.
            L("3745899", "Bridges Home Care Ltd", "2022-09-01", "2022-08-18", 50000.00m, "OSCP - Outside Scope of VAT"),
            L("3751760", "Bridges Home Care Ltd", "2022-09-14", "2022-08-18", 50000.00m, "ZERO - VAT Purchases Zero Rated"),
            L("3755028", "Bridges Home Care Ltd", "2022-09-28", "2022-08-18", 826.40m, "OSCP - Outside Scope of VAT"),
            // Barchester: six residents at one rate, one invoice date AND one pay date: a batch, not this shape.
            L("3908725", "Barchester Healthcare", "2024-06-14", "2024-06-14", 15314.29m), L("3908726", "Barchester Healthcare", "2024-06-14", "2024-06-14", 15314.29m),
        };
        var all = SocialCare.FindInvoiceDateRepeats(lines, 10000m, false);
        var r = Assert.Single(all);
        Assert.Equal("Bridges Home Care Ltd", r.Supplier);
        Assert.Equal(2, r.Copies);
        Assert.Equal(50000m, r.ValueBeyondFirst);
        Assert.True(r.VatCodesDiffer);
        Assert.Single(SocialCare.FindInvoiceDateRepeats(lines, 10000m, true));
    }

    [Fact]
    public void NewProviders_UseTheFirstTwelveMonthsFromTheFirstPayment()
    {
        var lines = new List<SocialCare.CareLine>
        {
            L("1", "Old Provider", "2020-05-01", null, 900000m),
            L("2", "Resiaction Staffordshire LTD", "2023-05-16", null, 300000m, type: "Children residential"),
            L("3", "Resiaction Staffordshire LTD", "2024-02-20", null, 448500m, type: "Children residential"),
            L("4", "Resiaction Staffordshire LTD", "2025-06-19", null, 43500m, type: "Children residential"), // after the 12 months
            L("5", "Small New", "2023-01-01", null, 100000m),
        };
        var p = Assert.Single(SocialCare.FindNewProviders(lines, new DateOnly(2021, 4, 1), 250000m));
        Assert.Equal("Resiaction Staffordshire LTD", p.Supplier);
        Assert.Equal(748500m, p.FirstTwelveMonths);
        Assert.Equal(792000m, p.Total);
    }

    [Fact]
    public void CareType_FollowsThePreRegisteredPopulation()
    {
        Assert.Equal("Nursing", SocialCare.CareType("Adult Social Care & Health", "TPP - WBC Funded Care", "OP Nursing"));
        Assert.Equal("Home care", SocialCare.CareType("Adult Social Care & Health", "TPP - WBC Funded Care", "OP Domiciliary Care"));
        Assert.Equal("Supported living", SocialCare.CareType("Adult Social Care & Health", "Individual Service Fund", "LD Shared Lives"));
        Assert.Equal("Children residential", SocialCare.CareType("Children´s Services", "TPP - Other Establishments", "Children´s Homes Purchasing"));
        Assert.Equal("Fostering agency", SocialCare.CareType("Children's Services", "TPP - Other Establishments", "UASC IFA"));
        Assert.Equal("Care leavers / 18+", SocialCare.CareType("Children's Services", "TPP - Other Establishments", "UASC 18+"));
        Assert.Equal("Semi-independent", SocialCare.CareType("Children's Services", "TPP - Other Establishments", "UASC Semi"));
        Assert.Equal("Staffing (children)", SocialCare.CareType("Children's Services", "Agency Staff", "Disabled Childrens - Staffing Team"));
        // Amendment 1: a building bought for a care cost centre is not care spending.
        Assert.Null(SocialCare.CareType("Adult Social Care & Health", "Acquisition of Buildings", "OP Nursing"));
        Assert.Null(SocialCare.CareType("Children's Services", "TPP - Other Establishments", "Independent and Non-Maintained Special Schools"));
        Assert.True(SocialCare.IsCouncilOwned("Optalis Ltd– Hollies Care Home"));
        Assert.True(SocialCare.IsPublicBody("NHS Berkshire West CCG"));
        Assert.False(SocialCare.IsPublicBody("Barchester Healthcare"));
    }

    [Fact]
    public void Hhi_IsOnPercentShares()
    {
        Assert.Equal(10000d, SocialCare.Hhi(new[] { 5m }), 6);
        Assert.Equal(5000d, SocialCare.Hhi(new[] { 1m, 1m }), 6);
    }
}
