using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 54: the fixes for the re-audit of 2026-10-05 (PREREG_S54_REAUDIT.md), each on the real case the audit named.
/// </summary>
public class Session54Tests
{
    // ---- N4: a row is redacted if ANY column carries a marker (Bradford 2026-08, 229 childcare-voucher rows) ----

    static readonly string[] BradfordAug2026Header = { "Organisation Code", "Service Code", "Service Label", "Expenditure Category", "Expenditure Category Description",
        "Payment Date", "Transaction Number", "Net Amount £", "Supplier Number", "Irrecoverable VAT", "SupplierName", "Supplier Name", "Industry Key" };

    static List<string[]> BradfordAugust2026() => new()
    {
        BradfordAug2026Header,
        new[] { "http://x/bradford", "1007", "Older People Keighley - Homecare", "5840", "Home Support", "2026-08-06", "110761023", "1097.04", "108519", "0.00", "06 CARE LIMITED", "06 CARE LIMITED", "" },
        new[] { "http://x/bradford", "", "3 & 4 year olds nef", "6010", "Childcare Vouch Sala", "2026-08-18", "110779554", "4857.00", "111626", "0.00", "REDACTED PERSONAL DATA", "TESTFIRST TESTLAST", "" },
    };

    [Fact]
    public void BradfordAugust2026_TheRowRedactedInOnePayeeColumn_ShowsNoNameInAnyColumn()
    {
        var table = CityFixups.BradfordPrepare(BradfordAugust2026());
        RowRedaction.ClearWithheld();
        var rows = AuditEngine.MapRows(table, SupportedCouncils.Bradford.Mapping, "2026-08", new List<string>());
        Assert.Equal(2, rows.Count);
        var red = rows.Single(r => r.TransactionId == "110779554");
        Assert.True(AuditEngine.IsRedactedSupplier(red.Supplier));
        Assert.DoesNotContain("TESTFIRST", red.Supplier);
        Assert.DoesNotContain("TESTLAST", red.OtherColumns ?? "", StringComparison.OrdinalIgnoreCase);   // the other name column is withheld too
        Assert.Equal("06 CARE LIMITED", rows.Single(r => r.TransactionId == "110761023").Supplier);  // a named row is untouched
        // and the engine now counts it as redacted (it counted 0)
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Contains(res.Warnings, w => w.StartsWith("1 rows carry a redacted"));
        // only a hash of the withheld name is kept
        var csv = string.Join("\n", RowRedaction.WithheldCsv());
        Assert.Contains(RowRedaction.NameHash("testfirst  testlast"), csv);
        Assert.DoesNotContain("TESTLAST", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMarkerInThePurposeOrIdColumn_DoesNotWithholdANamedCompanyPayee_ByDefault_ButTheAnyColumnScopeDoes()
    {
        // Kirklees: school taxi firms with "Purpose of Spend = REDACTED PERSONAL DATA"; Wakefield: "Supplier ID = Redacted" on every row (see RowRedaction.AnyColumn).
        var header = new[] { "Transaction Number", "Supplier Name", "Payment Date", "Description", "Amount" };
        var row = new[] { "T1", "Acme Taxis Ltd", "01/08/2026", "REDACTED client transport", "10.00" };
        Assert.Null(RowRedaction.MarkerIn(header, row, 1, anyColumn: false));
        Assert.NotNull(RowRedaction.MarkerIn(header, row, 1, anyColumn: true));
        var withheld = new List<string>();
        var out2 = RowRedaction.Withhold(header, row, 1, RowRedaction.Label(header, row, 1), withheld);
        Assert.StartsWith("REDACTED", out2[1]);          // the any-column scope replaces the payee
        Assert.Contains("Acme Taxis Ltd", withheld);
        var table = new List<string[]> { header, row };
        var map = new ColumnMapping("Transaction Number", "Supplier Name", "Payment Date", "Description", null, null, "Amount", null, null);
        Assert.Equal("Acme Taxis Ltd", Assert.Single(AuditEngine.MapRows(table, map, "t", new List<string>())).Supplier);   // default scope: untouched
    }

    [Fact]
    public void ARowWithNoMarker_IsNeverTouched()
    {
        var header = new[] { "Transaction Number", "Supplier Name", "Payment Date", "Description", "Amount" };
        var table = new List<string[]> { header, new[] { "T1", "Redactive Media Group", "01/08/2026", "Advertising", "10.00" } };   // a real supplier, not a marker
        var map = new ColumnMapping("Transaction Number", "Supplier Name", "Payment Date", "Description", null, null, "Amount", null, null);
        Assert.Equal("Redactive Media Group", Assert.Single(AuditEngine.MapRows(table, map, "t", new List<string>())).Supplier);
    }

    [Theory]
    [InlineData("Supplier Name", true)]
    [InlineData("SupplierName", true)]
    [InlineData("Supplier Name (alt)", true)]
    [InlineData("Beneficiary Name", true)]
    [InlineData("Payee", true)]
    [InlineData("VCSE Supplier", false)]       // Calderdale: a Yes/No flag, not a payee (the second full run withheld the value "Y" on 26 rows)
    [InlineData("Supplier", true)]
    [InlineData("Supplier (Beneficiary)", true)]
    [InlineData("SupplierID", false)]          // City of York and Calderdale: an identifier, not a name (the first full run withheld 2,051 York ids as names)
    [InlineData("Beneficiary ID", false)]
    [InlineData("Organisation Name", false)]   // the payer
    [InlineData("Supplier Number", false)]
    [InlineData("Supplier Invoice No", false)]
    [InlineData("Supplier Type", false)]
    [InlineData("Payment Date", false)]
    [InlineData("Net Amount", false)]
    public void NameKeyedHeaders(string header, bool expected) => Assert.Equal(expected, RowRedaction.IsNameHeader(header));

    [Fact]
    public void C14Rule_CatchesTheN4Pattern_AndPassesItsCleanTwin()
    {
        var h = new[] { "Year", "TransactionId", "SupplierKey", "SupplierName", "OtherColumns" };
        var bad = new[] { "2026-08", "110779554", "TESTFIRST TESTLAST", "TESTFIRST TESTLAST", "Supplier Number=111626|Supplier Name (alt)=REDACTED PERSONAL DATA" };
        var clean = new[] { "2026-08", "110779554", "REDACTED PERSONAL DATA", "REDACTED PERSONAL DATA", "Supplier Number=111626|Supplier Name (alt)=REDACTED PERSONAL DATA" };
        var leak = new[] { "2026-08", "110779554", "REDACTED PERSONAL DATA", "REDACTED PERSONAL DATA", "Supplier Number=111626|Supplier Name (alt)=A PERSON" };
        Assert.NotEmpty(RowRedaction.Violations(h, bad));
        Assert.Empty(RowRedaction.Violations(h, clean));
        Assert.NotEmpty(RowRedaction.Violations(h, leak));                 // the name in the OTHER column of a redacted payee row
        // a marker in a column that is not name-keyed (a purpose) leaves a named payee alone under the default scope
        Assert.Empty(RowRedaction.Violations(new[] { "SupplierName", "Purpose" }, new[] { "OAKWELL (UK) LTD", "REDACTED PERSONAL DATA" }));
        Assert.Empty(RowRedaction.Violations(h, new[] { "2026-08", "1", "ACME", "ACME", "Supplier Number=5" }));   // no marker anywhere: nothing to say
    }

    [Theory]
    [InlineData("REDACTED PERSONAL DATA", true)]
    [InlineData("RED11538ACTED", true)]
    [InlineData("REDACETED PERSONAL DATA", true)]
    [InlineData("Redated Personal Data", true)]
    [InlineData("Redactive Events Ltd", false)]
    [InlineData("Payment of personal data protection fee to the Information Commissioner for the year", false)]
    [InlineData("", false)]
    public void MarkerCells(string cell, bool expected) => Assert.Equal(expected, RowRedaction.IsMarkerCell(cell));

    // ---- N2: an n-squared run that stays Unreconciled shows the gap on the de-duplicated lines ----

    static SpendRow Line(string trans, string payee, string desc, decimal net, decimal gross, string date = "13/04/2023") =>
        new("S", trans, payee, date, AuditEngine.ParseDate(date), desc, "SEN", net, gross, null, null, 0, null, "High needs", null);

    [Fact]
    public void NSquaredRunWithAGap_ReportsTheGapOnTheDeduplicatedLines()
    {
        // Payee A: 2 lines printed twice each (4 rows), payee B: 3 lines printed three times (9 rows). Distinct lines 100.50 + 200.25 + 10 + 20 + 30 = 360.75.
        var rows = new List<SpendRow>();
        foreach (var d in new[] { ("a1", 100.50m), ("a2", 200.25m) }) for (int i = 0; i < 2; i++) rows.Add(Line("T9", "Academy A", d.Item1, d.Item2, 360.75m + 18550m));
        foreach (var d in new[] { ("b1", 10m), ("b2", 20m), ("b3", 30m) }) for (int i = 0; i < 3; i++) rows.Add(Line("T9", "Academy B", d.Item1, d.Item2, 360.75m + 18550m));
        rows.Add(Line("T9", "Academy C", "c1", 0.01m, 360.75m + 18550m));   // third payee so the run is multi-payee; net 0.01 keeps the arithmetic simple
        var res = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.Equal(360.76m, a.Net);                         // the distinct lines, not the 14-row total
        Assert.True(Math.Abs(a.Difference - 18549.99m) < 0.011m);
        Assert.Equal(14, a.LineCount);                        // the row count is still the published rows
        Assert.Contains("de-duplicated", a.ClassificationDetail);
    }

    // ---- N3: the placeholder the page labels ----

    [Theory]
    [InlineData("(no number) 12", "(no number published) 12")]
    [InlineData("(no number: CHAPS) 912", "(no number published) 912 CHAPS")]
    [InlineData("(no number: P CARD) 4", "(no number published) 4 P CARD")]
    [InlineData("(no number published) 7", "(no number published) 7")]
    [InlineData("4801234", "4801234")]
    public void PlaceholderIds(string id, string expected) => Assert.Equal(expected, AuditEngine.NormalisePlaceholderId(id));
}
