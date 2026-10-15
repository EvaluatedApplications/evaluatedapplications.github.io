using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 45: Stockport Metropolitan Borough Council (NextCouncils.Stockport) and its "*Exclude" payee label.
/// Every case is a real file name, header or payee from the 116 files downloaded from the council's S3 bucket (data.gov.uk "All spend").
/// </summary>
public class Session45Tests
{
    [Theory]
    [InlineData("Spend+over+_500+Feb+17.csv", "2017-02")]
    [InlineData("Over+_500+spend+April+2018.csv", "2018-04")]
    [InlineData("Over+_500+Spend+May+18.csv", "2018-05")]
    [InlineData("Over+_500+spend+Dec18.csv", "2018-12")]                       // no separator between month and year
    [InlineData("Over+_500+spend+Sept18.csv", "2018-09")]                      // "Sept"
    [InlineData("Over+_500+spend+July+18+.csv", "2018-07")]                    // trailing +
    [InlineData("Spend+over+_500+-+November+2019.xlsx", "2019-11")]
    [InlineData("Expenditure+over+_500+-+June+21.csv", "2021-06")]
    [InlineData("Over+500+Spend+November+2022.csv", "2022-11")]                // no pound sign
    [InlineData("Over+_500+spend+-+July+2022.csv", "2022-07")]
    [InlineData("All+Spend+April+2025.csv", "2025-04")]
    [InlineData("All+Spend+August+2026.csv", "2026-08")]
    [InlineData("notes.csv", "")]
    public void StockportTagIsTheMonthOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.StockportTag(name));

    [Fact]
    public void StockportHeadersOfTheLayoutsBecomeOneSet()
    {
        // 2017: "Date" only
        Assert.Equal(new[] { "Merchant Category", "Supplier Name", "Service", "Summary of Purpose of Expenditure", "Date", "Net Amount" },
            NextFixups.CanonHeader(NextFixups.StockportHeader(new[] { "Merchant Category", "Supplier Name", "Service", "Summary of purpose of expeniture", "Date", "Net Amount" }), NextFixups.StockportCanon));
        // 2020 to 2024: invoice_date is the only date, so it is "Date"
        Assert.Equal(new[] { "Merchant Category", "Supplier Name", "Service", "Summary of Purpose of Expenditure", "Date", "Net Amount" },
            NextFixups.CanonHeader(NextFixups.StockportHeader(new[] { "Merchant Category", "Supplier", "Service", "Summary of Purpose of Expenditure", "invoice_date", "net_amount" }), NextFixups.StockportCanon));
        // 2018-19: both dates; paid_date is the payment date, the invoice date is kept under its own name
        var both = NextFixups.CanonHeader(NextFixups.StockportHeader(new[] { "Merchant Category", "Supplier", "Service", "Summary of purpose of expenditure", "invoice_date", "paid_date", " net_amount " }), NextFixups.StockportCanon);
        Assert.Equal(new[] { "Merchant Category", "Supplier Name", "Service", "Summary of Purpose of Expenditure", "Invoice Date", "Date", "Net Amount" }, both);
        // April 2025 on: transaction_id and a Directorate
        var n = NextFixups.CanonHeader(NextFixups.StockportHeader(new[] { "transaction_id", "Merchant Category", "Supplier", "Directorate", "Service Area", "Summary of Purpose of Expenditure", "paid_date", "net_amount" }), NextFixups.StockportCanon);
        Assert.Equal(new[] { "Transaction Number", "Merchant Category", "Supplier Name", "Directorate", "Service", "Summary of Purpose of Expenditure", "Date", "Net Amount" }, n);
        // two 2019 files name the Service column "Services to People"; one 2018 file calls the category column "Merchant ID"
        Assert.Equal("Service", NextFixups.StockportCanon("Services to People"));
        Assert.Equal("Merchant Category", NextFixups.StockportCanon("Merchant ID"));
    }

    [Fact]
    public void StockportFileWithNoDateColumnGetsAnEmptyDateColumn()
    {
        var table = new List<string[]>
        {
            new[] { "Merchant Category", "Supplier", "Service", "Summary of Purpose of Expenditure", "net_amount" },
            new[] { "Social Care Services", "Ramos Healthcare", "Services to People", "Care Payments", "1288" },
            new[] { "Social Care Services", "Ramos Healthcare", "Services to People", "Care Payments" },   // a short row
        };
        var fixedTable = NextFixups.StockportTable(table);
        Assert.Equal(3, fixedTable.Count);
        Assert.Equal("Date", fixedTable[0][5]);
        Assert.All(fixedTable, r => Assert.Equal(6, r.Length));
        Assert.Equal("", fixedTable[1][5]);
        Assert.Equal("1288", fixedTable[1][4]);
        Assert.Equal("", fixedTable[2][4]);
        // a file that has a date column is returned as it is
        var dated = new List<string[]> { new[] { "Supplier", "invoice_date", "net_amount" }, new[] { "A", "01/01/2020", "1" } };
        Assert.Same(dated, NextFixups.StockportTable(dated));
    }

    [Theory]
    [InlineData("23/10/2024", "23/10/2024")]
    [InlineData("3/4/2019", "03/04/2019")]
    [InlineData("03/04/19", "03/04/2019")]
    [InlineData("", "")]
    public void StockportDateIsWrittenInFull(string input, string expected) => Assert.Equal(expected, NextFixups.StockportDate(input));

    [Fact]
    public void StockportProfileHasOneFilePerMonthFromFebruary2017()
    {
        Assert.Equal(115, NextCouncils.Stockport.Years.Count);           // 2017-02 to 2026-08
        Assert.Equal("2017-02", NextCouncils.Stockport.Years[0].Tag);
        Assert.Equal("2026-08", NextCouncils.Stockport.Years[^1].Tag);
        Assert.Equal("stockport", NextCouncils.SlugByName["Stockport Metropolitan Borough Council"]);
        Assert.Contains(NextCouncils.Stockport, NextCouncils.All);
    }

    [Theory]
    [InlineData("*Exclude", true)]                  // 131 lines in October 2025
    [InlineData("EXCLUDE", true)]                   // the same label as a supplier key
    [InlineData("*Exclude - VAT only", true)]       // 32 lines
    [InlineData("EXCLUDE VAT ONLY", true)]
    [InlineData("  *exclude  ", true)]
    [InlineData("Excluded Ltd", false)]
    [InlineData("Exclude Holdings Limited", false)]
    [InlineData("Lyreco UK Ltd", false)]
    [InlineData("", false)]
    public void ExcludeLabelIsAPlaceholderPayee(string name, bool expected) => Assert.Equal(expected, AuditEngine.IsRedactedSupplier(name));
}
