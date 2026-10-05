using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 43: Surrey County Council (NextCouncils.Surrey) and the extract-timestamp blind spot in the exact-repeat test.
/// Every case is a real file name or header from the 47 files downloaded from surreyi.gov.uk.
/// </summary>
public class Session43Tests
{
    [Theory]
    [InlineData("SAP_Spend_Data_Q1_2017-2018.csv", "2017-04_06")]
    [InlineData("SAP_Spend_Data_Q4_2017-2018.csv", "2018-01_03")]       // Q4 is January to March of the NEXT calendar year
    [InlineData("SAP_Spend_Data_Q4_2022-23.csv", "2023-01_03")]         // two-digit second year
    [InlineData("ERP_Spend_Q1_2023-2024_.csv", "2023-04_06")]           // trailing underscore
    [InlineData("ERP_Spend_Q3_2025-2026.csv", "2025-10_12")]
    [InlineData("ERP_Spend_Q1_2026-2027.csv", "2026-04_06")]
    [InlineData("SAP_Spend_Data_Q4_2016-2017.csv", "")]                 // before 2017-18: not used
    [InlineData("SAP_Spend_Data_Q3_2014-2015.csv", "")]
    [InlineData("notes.csv", "")]
    public void SurreyTagIsTheQuarterOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.SurreyTag(name));

    [Fact]
    public void SurreyHeadersOfTheLayoutsBecomeOneSet()
    {
        var old = new[] { "extractdate", "publisheruri", "publisherlabel", "servicetypeuri", "servicetypelabel", "Date Spend Occured", "Department Incurring Spend", "Beneficiary Name",
            "Summary of Purpose of Spend", " Gross Amount ", "Merchant Category", "Text", "Document Header Text" };
        var o = NextFixups.CanonHeader(old, NextFixups.SurreyCanon);
        Assert.Equal("Extract Date", o[0]);
        Assert.Equal("Date Spend Occurred", o[5]);
        Assert.Equal("Gross Amount", o[9]);
        var lower = new[] { "Date spend occurred", "Department incurring spend", "Beneficiary name", "Summary of purpose of spend", "Gross Amount", "Merchant category" };
        Assert.Equal(new[] { "Date Spend Occurred", "Department Incurring Spend", "Beneficiary Name", "Summary of Purpose of Spend", "Gross Amount", "Merchant Category" },
            NextFixups.CanonHeader(lower, NextFixups.SurreyCanon));
        Assert.Equal("Department Incurring Spend", NextFixups.SurreyCanon("LA Department Incurring Spend"));
        Assert.Equal("Date Spend Occurred", NextFixups.SurreyCanon("Date Spend Incurred"));
    }

    [Fact]
    public void SurreyProfileHasOneFilePerQuarterFromApril2017()
    {
        Assert.Equal(37, NextCouncils.Surrey.Years.Count);               // 2017-04_06 to 2026-04_06
        Assert.Equal("2017-04_06", NextCouncils.Surrey.Years[0].Tag);
        Assert.Equal("2026-04_06", NextCouncils.Surrey.Years[^1].Tag);
        Assert.Equal("surrey", NextCouncils.SlugByName["Surrey County Council"]);
    }

    [Theory]
    [InlineData("Redacted Personal Data", true)]          // the plain form
    [InlineData("REDACTED PERSONAL DATA", true)]
    [InlineData("Redated Personal Data", true)]            // Surrey, 1,722 lines
    [InlineData("Redcated Personal Data", true)]           // Surrey, 532
    [InlineData("Dedacted Personal Data", true)]           // Surrey, 12
    [InlineData("REDACETED PERSONAL DATA", true)]          // Wirral, 7
    [InlineData("REEDACTED PERSONAL DATA", true)]          // Wirral, 1
    [InlineData("Null", true)]                             // Surrey writes a missing name out, 99 lines
    [InlineData("NULL ", true)]
    [InlineData("RED11538ACTED", true)]                    // Merton's spliced placeholder, still caught
    [InlineData("REDACTIVE EVENTS LTD", false)]            // a real events company in 22 of the 24 councils' files, not a withheld name
    [InlineData("Redactive Publishing Limited", false)]
    [InlineData("Redactive Media Sales (Royal College)", false)]
    [InlineData("Identity Redacted", true)]                // Hertfordshire's wording, six capitalisations
    [InlineData("Idenity Redacted", true)]
    [InlineData("Nullarbor Holdings Ltd", false)]          // only the whole word
    [InlineData("Personnel Data Services Ltd", false)]     // "personnel data" is not "personal data"
    [InlineData("Teachers Pensions", false)]
    [InlineData("", false)]
    public void ARedactionPlaceholderIsRecognisedInItsMisspellings(string payee, bool expected) => Assert.Equal(expected, AuditEngine.IsRedactedSupplier(payee));

    // ---- Hertfordshire County Council (quarterly then monthly CSVs, a real transaction number) ----
    [Theory]
    [InlineData("supplier-payments-over-250-april-to-june-2022.csv", "2022-04_06")]
    [InlineData("supplier-payments-over-250-january-to-march-2023.csv", "2023-01_03")]
    [InlineData("supplier-payments-over-250-october-to-december-2024.csv", "2024-10_12")]
    [InlineData("supplier-payments-over-250-january-2025.csv", "2025-01")]
    [InlineData("supplier-payments-over-250-march-2025.csv", "2025-03")]
    [InlineData("supplier-payments-over-500-april-2025.csv", "2025-04")]        // the threshold changed from 250 to 500 pounds here
    [InlineData("supplier-payments-over-500-november-2025.csv", "2025-11")]
    [InlineData("supplier-payments-over-500-december.csv", "2025-12")]          // no year in the name: the month after November 2025
    [InlineData("supplier-payments-over-500-august-2026.csv", "2026-08")]
    [InlineData("purchasing-card-spend-january-march-2026.csv", "")]            // another dataset on the same page
    [InlineData("business-mileage-2026-27.xlsx", "")]
    public void HertfordshireTagIsTheMonthOrQuarterOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.HertsTag(name));

    [Theory]
    [InlineData("20/06/2022", "20/06/2022")]
    [InlineData("10/3/2025", "10/03/2025")]              // d/M/yyyy written in full: 10 March, never 3 October
    [InlineData("5/3/2025", "05/03/2025")]
    [InlineData("Payment Date", "Payment Date")]         // a stray header line published as data is left as it is
    public void HertfordshireDatesAreWrittenInFull(string given, string expected) => Assert.Equal(expected, NextFixups.HertsDate(given));

    [Fact]
    public void HertfordshireProfileHasElevenQuartersThenTwentyMonths()
    {
        Assert.Equal(31, NextCouncils.Hertfordshire.Years.Count);
        Assert.Equal("2022-04_06", NextCouncils.Hertfordshire.Years[0].Tag);
        Assert.Equal("2024-10_12", NextCouncils.Hertfordshire.Years[10].Tag);
        Assert.Equal("2025-01", NextCouncils.Hertfordshire.Years[11].Tag);
        Assert.Equal("2026-08", NextCouncils.Hertfordshire.Years[^1].Tag);
        Assert.Equal("hertfordshire", NextCouncils.SlugByName["Hertfordshire County Council"]);
    }

    // ---- Essex County Council (workbooks, one sheet a month, pooled payee labels) ----
    [Theory]
    [InlineData("April 2025", null)]                     // a month sheet: tag by the dates it holds
    [InlineData("September 2026", null)]
    [InlineData("Field Descriptions", "")]               // not spending
    [InlineData("Contracts Effective 310322", "")]       // the contract-register workbooks on the same page
    [InlineData("32_LGTC-Report-March-2022.xls", "")]
    public void EssexOnlyMonthSheetsAreSpending(string unit, string? expected) => Assert.Equal(expected, NextFixups.EssexTag(unit));

    [Theory]
    [InlineData("27/04/2020", "27/04/2020")]
    [InlineData("23 March 2020", "23/03/2020")]
    [InlineData("16th May 2023", "16/05/2023")]          // ordinal suffix, as published in the May 2023 and March 2023 sheets
    [InlineData("1st June 2023", "01/06/2023")]
    [InlineData("3 June 2023", "03/06/2023")]
    [InlineData("not a date", "not a date")]
    public void EssexDatesWrittenAsTextAreMadeStandard(string given, string expected) => Assert.Equal(expected, NextFixups.EssexDate(given));

    [Fact]
    public void EssexHeaderCellsLoseTheirStraySpacesAndAsterisk()
    {
        Assert.Equal("Spend Description", NextFixups.EssexCanon("Spend Description "));
        Assert.Equal("Merchant Group", NextFixups.EssexCanon("Merchant Group*"));
        Assert.Equal("Name", NextFixups.EssexCanon(" Name"));
    }

    [Theory]
    [InlineData("FOSTER CARE PAYMENT", true)]
    [InlineData("ecc employee", true)]                   // case-insensitive
    [InlineData("ECC Employee ", true)]
    [InlineData("PAYMENT TO INDIVIDIUAL", true)]         // the council's own spelling mistake, 490 lines
    [InlineData("ONE OFF UNDER £10K", true)]
    [InlineData("DIRECT PAYMENTS", true)]
    [InlineData("DIRECT PAYMENT SERVICES LTD", false)]   // exact match only, nothing fuzzy
    [InlineData("FOSTER CARE PAYMENTS LTD", false)]
    [InlineData("ESSEX CARES LTD", false)]
    [InlineData("", false)]
    public void EssexPooledLabelsAreMatchedExactly(string name, bool expected) => Assert.Equal(expected, NextFixups.IsEssexPooledLabel(name));

    [Fact]
    public void EssexTableRewritesOnlyThePooledNameCellsUnderTheHeader()
    {
        var table = new List<string[]>
        {
            new[] { "", "Accounts Payable", "276150552.01996833" },          // the summary block above the header: never touched
            new[] { "", "FOSTER CARE PAYMENT", "1" },                       // a pooled label sitting in a summary row is NOT a data row
            new[] { "Date", "Name", "Value" },
            new[] { "25/04/2025", "FOSTER CARE PAYMENT", "273.77" },
            new[] { "25/04/2025", "NETWORK RAIL", "4877572.75" },
            new[] { "25/04/2025", "Direct Payment", "318.86" },
        };
        var o = NextFixups.EssexTable(table);
        Assert.Equal(table.Count, o.Count);
        Assert.Equal("FOSTER CARE PAYMENT", o[1][1]);
        Assert.Equal("Redacted (pooled label): FOSTER CARE PAYMENT", o[3][1]);
        Assert.Equal("NETWORK RAIL", o[4][1]);
        Assert.Equal("Redacted (pooled label): Direct Payment", o[5][1]);
        Assert.Equal("273.77", o[3][2]);                                    // no amount or date cell changes
        Assert.Equal("FOSTER CARE PAYMENT", table[3][1]);                   // the input is not modified
    }

    [Fact]
    public void EssexProfileHasOneFilePerMonthFromApril2019ToAugust2026()
    {
        Assert.Equal(89, NextCouncils.Essex.Years.Count);
        Assert.Equal("2019-04", NextCouncils.Essex.Years[0].Tag);
        Assert.Equal("2026-08", NextCouncils.Essex.Years[^1].Tag);
        Assert.Equal("essex", NextCouncils.SlugByName["Essex County Council"]);
    }

    [Fact]
    public void AnExtractTimestampIsNotPartOfTheRepeatKeyButARealColumnIs()
    {
        // Surrey 2018-04_06: every row carries its own extract timestamp, which made 54,248 rows all unique
        Assert.Equal("Text=Rates Apr 18", FileDuplication.OtherForRepeat("Extract Date=20180724135050.0726188+01:00|Text=Rates Apr 18"));
        Assert.Equal("Text=Rates Apr 18", FileDuplication.OtherForRepeat("Text=Rates Apr 18|Extract Date=20180724135050.12866+01:00"));
        Assert.Equal("", FileDuplication.OtherForRepeat("Extract Date=12/04/2018 15:50"));
        // nothing else changes
        Assert.Equal("Text=a|Document Header Text=b", FileDuplication.OtherForRepeat("Text=a|Document Header Text=b"));
        Assert.Null(FileDuplication.OtherForRepeat(null));
    }

    [Fact]
    public void TwoRowsThatDifferOnlyInExtractTimestampAreExactRepeats()
    {
        SpendRow Row(string stamp) => new("2018-04_06", "(no number published) 1", "Gerald Eve", "01/04/2018", null, "Rates", "", 296m, 296m, null, "", 1,
            null, null, null, "Extract Date=" + stamp + "|Text=Rates Apr 18");
        var stats = FileDuplication.Scan(new[] { Row("20180724135050.0726188+01:00"), Row("20180724135050.5229837+01:00") });
        Assert.Equal(1, stats.Single().RepeatRows);
    }
}
