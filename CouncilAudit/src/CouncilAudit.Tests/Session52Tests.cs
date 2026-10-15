using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 52: the fixes for the independent audit of 2026-10-04, each tested on the real case the audit named
/// (PREREG_S52_AUDIT_FIXES.md).
/// </summary>
public class Session52Tests
{
    // ---- Fix 1: no silent drop in the column mapping ----

    private static readonly ColumnMapping ReadingLike = new("Voucher Number", "Supplier Name", "Payment Date", "Purpose", "Service Area",
        null, "Amount", null, null);

    private static List<string[]> ReadingAugust2025() => new()
    {
        new[] { "Purchasing Organisation", "Payment Date", "Voucher Number", "Supplier Name", "Service Area", "Amount", "Purpose" },
        new[] { "RBC Legal Entity", "12/08/2025", "", "Gateley PLC", "Capital", "3704464.99", "CHAPS" },   // real: blank Voucher Number
        new[] { "RBC Legal Entity", "14/08/2025", "4801234", "Some Supplier Ltd", "Adult Care", "1200.00", "Care" },
        new[] { "", "", "", "", "", "45000000.00", "" },                                                    // an amount-only footer
        new[] { "", "", "", "", "", "", "" },                                                               // a blank line
        new[] { "RBC Legal Entity", "15/08/2025", "", "HMRC", "Payroll + Payments A/c", "504.00", "AWT" },
    };

    [Fact]
    public void ARowWithNoNumberButAPayee_IsKept_WithItsOwnPlaceholder()
    {
        var drops = new List<DroppedRow>();
        var rows = AuditEngine.MapRows(ReadingAugust2025(), ReadingLike, "2025-08", new List<string>(), drops);
        Assert.Equal(3, rows.Count);
        var gateley = Assert.Single(rows, r => r.Supplier == "Gateley PLC");
        Assert.Equal(3704464.99m, gateley.Net);
        Assert.Equal("(no number published) 0", gateley.TransactionId);   // Session 54: the placeholder the page labels
        Assert.Equal("(no number published) 4", rows.Single(r => r.Supplier == "HMRC").TransactionId);
        Assert.Equal("", FileDuplication.IdForRepeat(gateley.TransactionId)); // never treated as a real number
    }

    [Fact]
    public void EveryRecordTheMappingLeavesOut_IsLoggedWithItsReason()
    {
        var drops = new List<DroppedRow>();
        var rows = AuditEngine.MapRows(ReadingAugust2025(), ReadingLike, "2025-08", new List<string>(), drops);
        Assert.Equal(5, rows.Count + drops.Count); // 5 data rows in, every one accounted for
        Assert.Contains(drops, d => d.Reason == "blank row" && d.RowIndexInSource == 3);
        var footer = Assert.Single(drops, d => d.Reason.StartsWith("no number, no payee and no payment date"));
        Assert.Equal(45000000.00m, footer.Net);
    }

    [Fact]
    public void TheOldBehaviour_IsOnlyAvailableAsAMeasuredSwitch_AndStillLogs()
    {
        bool keep = AuditEngine.KeepUnnumberedPayments;
        try
        {
            AuditEngine.KeepUnnumberedPayments = false;
            var drops = new List<DroppedRow>();
            var rows = AuditEngine.MapRows(ReadingAugust2025(), ReadingLike, "2025-08", new List<string>(), drops);
            Assert.Single(rows);
            Assert.Equal(2, drops.Count(d => d.Reason.StartsWith("UNNUMBERED PAYMENT")));
        }
        finally { AuditEngine.KeepUnnumberedPayments = keep; }
        Assert.True(AuditEngine.KeepUnnumberedPayments);
    }

    [Fact]
    public void SyntheticRowNumber_GoesInTheRowRefColumn_AndABlankRowStaysBlank()
    {
        var t = new List<string[]>
        {
            new[] { "Service", "Expenditure category", "Narrative", "Date", "Net amount", "Supplier name" },
            new[] { "Adults", "Care", "x", "01/02/2026", "100.00", "A Ltd" },
            new[] { "", "", "" },                                      // short blank row (West Berkshire 2026-02 has 577 of them)
            new[] { "Adults", "Care", "y", "02/02/2026", "50.00" },     // short row with data: number must not land under the payee
            new[] { "Adults", "Care", "z", "03/02/2026", "75.00", "B Ltd", "extra" },
        };
        var o = WestBerkshireFixups.AddSyntheticRowId(t);
        Assert.Equal("RowRef", o[0][6]);
        Assert.Equal("(no number published) 1", o[1][6]);   // Session 53: the scanner's row number carries the unnumbered placeholder
        Assert.True(o[2].All(string.IsNullOrWhiteSpace));
        Assert.Equal("", o[3][5]);
        Assert.Equal("(no number published) 3", o[3][6]);
        Assert.Equal("(no number published) 4", o[4][6]);
        Assert.Equal("extra", o[4][7]);
    }

    [Fact]
    public void RbwmRepublishedLine_WithNoNumber_IsRecognisedAsAlreadyPublished()
    {
        SpendRow R(string id) => new("2025-05", id, "TAPI CARPETS & FLOORS LIMITED", "03/04/2025", null, "Other Expenses", "Adult Social Care", 737.27m, 737.27m, null, null, 1);
        var seen = new HashSet<(string, string, decimal, string?, string?)>();
        RbwmFixups.DropAlreadyPublished(new[] { R("(no number) 12") }, seen);
        var (kept, dropped) = RbwmFixups.DropAlreadyPublished(new[] { R("(no number) 40") }, seen);
        Assert.Empty(kept);
        Assert.Equal(1, dropped);
    }

    // ---- Fix 2: the Schedule A difference is the VAT-adjusted gap ----

    private static SpendRow WokLine(string trans, string supplier, decimal net, decimal gross, string vat, string desc = "Fees") =>
        new("FY2022-23", trans, supplier, "09/11/2022", AuditEngine.ParseDate("09/11/2022"), desc, "Place", net, gross, null, vat, 0);

    [Fact]
    public void MatrixScm3763717_ShowsTheThirtySevenPenceGap_AsASmallGap()
    {
        var res = AuditEngine.Run(new[] { WokLine("3763717", "Matrix SCM Ltd", 105245.41m, 126294.86m, "STD") },
            TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(126294.49m, a.ExpectedGross);
        Assert.Equal(0.37m, a.Difference);              // was 21,049.45 (the VAT)
        Assert.Equal(ScheduleAClassification.SmallGap, a.Classification);
    }

    [Fact]
    public void AGapOfAPoundOrMore_StaysUnreconciled_AndTheDifferenceIsGrossMinusExpected()
    {
        var res = AuditEngine.Run(new[] { WokLine("3928781", "Holt School", 1000.00m, 1204.03m, "STD") },
            TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(res.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.Equal(4.03m, a.Difference);
    }

    [Fact]
    public void FivePenceOrLess_IsStillRoundingNoise()
    {
        var res = AuditEngine.Run(new[] { WokLine("3991866", "Education Boutique Ltd", 1000.00m, 1200.02m, "STD") },
            TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(ScheduleAClassification.VatRoundingNoise, Assert.Single(res.ScheduleA).Classification);
    }

    // ---- Fix 6: a redacted line under the same transaction number is stated, and no cause is guessed ----

    [Fact]
    public void ARedactedLineInAMultiPayeeRun_IsCountedInTheGap_AndNoCauseIsStated()
    {
        SpendRow L(string payee, string desc, decimal net) => new("FY2022-23", "3766535", payee, "11/11/2022", AuditEngine.ParseDate("11/11/2022"), desc, "SEN", net, 1000m, null, "EXEM", 0, null, "High needs", null);
        var rows = new List<SpendRow>();
        for (int i = 0; i < 2; i++) { rows.Add(L("Academy A", "a1", 100m)); rows.Add(L("Academy A", "a2", 200m)); }
        rows.Add(L("Academy B", "b1", 300m));
        rows.Add(L("Academy C", "c1", 50m));
        rows.Add(L("Redacted Personal Details", "Ukraine Response", 350m));
        var a = Assert.Single(AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal).ScheduleA);
        // distinct named lines 100 + 200 + 300 + 50 = 650; with the redacted 350 they are 1,000 = the stated Gross
        Assert.Contains("1 line whose payee is redacted (350.00)", a.ClassificationDetail);
        Assert.DoesNotContain("would be lines not published", a.ClassificationDetail);
        Assert.Equal(ScheduleAClassification.NSquaredListing, a.Classification);
    }

    [Fact]
    public void TheElevenNovemberGap_IsStatedWithTheRedactedLine()
    {
        var s = new NSquaredListing.Result(22, 3, 129, 35, 206361.06m, 950000m);
        string d = NSquaredListing.Describe(s, 225961.06m, 1, 350m);
        Assert.Contains("19250.00 above", d);
        Assert.Contains("the file does not show why", d);
        Assert.DoesNotContain("would be lines not published", d);
    }

    // ---- Fix 3: transaction twins ----

    private static DateTime D(string s) => DateTime.ParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void AnUndatedTransaction_IsNeverPaired()
    {
        // Cambian 3605427 (paid 06/05/2021) and 3644308 (no pay date): listed as "1 day apart" before
        var (pairs, undated) = TwinPairs.List(new DateTime?[] { D("06/05/2021"), null });
        Assert.Empty(pairs);
        Assert.Equal(1, undated);
    }

    [Fact]
    public void ARunOfFourOrMore_IsStillLeftOut_UntilABatchTestExists()
    {
        // Set aside in Session 52: listing The Loddon Foundation's same-day pair inside its monthly series also listed same-day
        // batches of identical purchases (640 -> 5,200 pairs). The stated rule stays "at most three such transactions".
        var dates = new DateTime?[] { D("22/09/2022"), D("21/10/2022"), D("22/11/2022"), D("22/11/2022"), D("21/12/2022") };
        Assert.Empty(TwinPairs.List(dates).pairs);
        Assert.Equal(29, TwinPairs.Cadence(dates)); // kept for the next step
    }

    [Fact]
    public void ATripleIsTwoExtraCopies_NotThree()
    {
        // Wokingham Cambian 3833138/39/40, GBP 54,336.67 each: three pairs, extra copies 108,673.34
        var dates = new DateTime?[] { D("04/03/2024"), D("04/03/2024"), D("04/03/2024") };
        var (pairs, _) = TwinPairs.List(dates);
        Assert.Equal(3, pairs.Count);
        var (group, size, credited) = TwinPairs.Groups(pairs, new[] { 54336.67m, 54336.67m, 54336.67m });
        Assert.All(group, g => Assert.Equal(0, g));
        Assert.All(size, s => Assert.Equal(3, s));
        Assert.Equal(108673.34m, credited.Sum());
    }

}
