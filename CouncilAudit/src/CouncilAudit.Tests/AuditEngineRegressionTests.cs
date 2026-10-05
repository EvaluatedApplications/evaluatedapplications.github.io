using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Regression suite: every case here is either (a) hand-verified against a real council
/// file in an earlier session (see FEEDBACK.md / COUNCIL_AUDIT_PRODUCT_PLAN.md for the
/// citation), or (b) a known false-positive trap the engine must NOT flag. A rule change
/// that breaks any of these fails the build - that is the point. Precision on this
/// labelled set is reported in the write-up, not just "tests pass."
///
/// Fixtures build <see cref="SpendRow"/> directly (bypassing CSV parsing) so each case
/// tests exactly one engine decision, deterministically, with no file I/O.
/// </summary>
public class AuditEngineRegressionTests
{
    private static SpendRow Row(
        string trans, string supplier, decimal net, decimal gross,
        string? vatType = null, decimal? vatAmount = null, string? desc = "x",
        string? payDate = "01/04/2024", string source = "FY1", int idx = 0,
        string? invoiceNo = null, string? costCentreArea = null, string? invoiceType = null) =>
        new(source, trans, supplier, payDate, AuditEngine.ParseDate(payDate), desc, "Area",
            net, gross, vatAmount, vatType, idx, invoiceNo, costCentreArea, invoiceType);

    // ---------------------------------------------------------------------------------
    // 1. Double listing vs itemisation (Wokingham 2020-21 pattern: 1,990 transactions
    //    where identical lines sum to exactly 2x the stated invoice).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void DoubleListing_TwoIdenticalLines_SummingToTwiceStatedInvoice_IsFlaggedAndLabelled()
    {
        var rows = new List<SpendRow>
        {
            Row("T1", "ACME LTD", net: 500m, gross: 500m, vatType: "EXEM", idx: 0),
            Row("T1", "ACME LTD", net: 500m, gross: 500m, vatType: "EXEM", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);

        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
        Assert.Equal(1000m, a.Net);
        Assert.Equal(500m, a.Gross);
    }

    [Fact]
    public void Itemisation_TwoLinesSummingToStatedInvoiceOnce_IsNotFlaggedAtAll()
    {
        // AquaCare-style itemised invoice: two genuinely different charges whose net
        // values happen to be equal, but the published Gross is the TRUE combined total
        // (1000), not one line's own value repeated - so net sum (1000) reconciles with
        // gross (1000) exactly once. This must never be confused with double listing.
        var rows = new List<SpendRow>
        {
            Row("T2", "AQUACARE SERVICES", net: 500m, gross: 1000m, vatType: "EXEM", desc: "Week 1 care", idx: 0),
            Row("T2", "AQUACARE SERVICES", net: 500m, gross: 1000m, vatType: "EXEM", desc: "Week 2 care", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void Itemisation_SameDescriptionSameAmount_ButGrossIsTrueTotal_IsNotFlagged()
    {
        // Lines ARE fully identical (same net/gross/description) but Gross already IS
        // the true combined total (not the single-line value) - net sum reconciles once.
        // Distinguishes "identical lines" from "double listing": identical lines alone
        // are not the signal, the 2x-of-the-STATED-invoice ratio is.
        var rows = new List<SpendRow>
        {
            Row("T3", "CARE PROVIDER LTD", net: 500m, gross: 1000m, vatType: "EXEM", idx: 0),
            Row("T3", "CARE PROVIDER LTD", net: 500m, gross: 1000m, vatType: "EXEM", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    // ---------------------------------------------------------------------------------
    // 1b. Generalised double listing (Session 14 - Net=2xGross census on Wokingham's
    //     remaining Unreconciled population): a whole ITEMISED (multi-line, genuinely
    //     different lines) invoice published twice, not just one line. Confirmed real
    //     on Wokingham FY2020-21 (trans 3556053, 3557512, 3561522, 3557414, 3557664 -
    //     23 rows total, 15 suppliers, 100% EXEM).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void GroupDoubleListing_TwoDistinctLines_RepeatedTwice_IsFlaggedAsDoubleListing()
    {
        // Real shape (trans 3557512, Reading & Wokingham Coaches, FY2020-21): an
        // itemised 2-line invoice (2,600 + 3,900 = 6,500 = stated Gross) published
        // AGAIN in full, making a 4-line group whose net sum is exactly 2x Gross.
        var rows = new List<SpendRow>
        {
            Row("T5", "READING & WOKINGHAM COACHES", net: 2600m, gross: 6500m, vatType: "EXEM", desc: "Home to School Transport", idx: 0),
            Row("T5", "READING & WOKINGHAM COACHES", net: 3900m, gross: 6500m, vatType: "EXEM", desc: "Home to School Transport", idx: 1),
            Row("T5", "READING & WOKINGHAM COACHES", net: 2600m, gross: 6500m, vatType: "EXEM", desc: "Home to School Transport", idx: 2),
            Row("T5", "READING & WOKINGHAM COACHES", net: 3900m, gross: 6500m, vatType: "EXEM", desc: "Home to School Transport", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);

        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
        Assert.Equal(13000m, a.Net);
        Assert.Equal(6500m, a.Gross);
    }

    [Fact]
    public void GroupDoubleListing_TwoDistinctLinesDifferentDescriptions_RepeatedTwice_IsFlaggedAsDoubleListing()
    {
        // Real shape (trans 3557664, Forest Care Limited, FY2020-21): the two distinct
        // lines even carry DIFFERENT descriptions, not just different amounts - the
        // grouping key is (Net, Gross, Description) together, confirmed still matches.
        var rows = new List<SpendRow>
        {
            Row("T6", "FOREST CARE LIMITED", net: 2520m, gross: 4896.64m, vatType: "EXEM", desc: "TPP - WBC Funded Care", idx: 0),
            Row("T6", "FOREST CARE LIMITED", net: 2376.64m, gross: 4896.64m, vatType: "EXEM", desc: "TPP - WBC Domicilary Care", idx: 1),
            Row("T6", "FOREST CARE LIMITED", net: 2376.64m, gross: 4896.64m, vatType: "EXEM", desc: "TPP - WBC Domicilary Care", idx: 2),
            Row("T6", "FOREST CARE LIMITED", net: 2520m, gross: 4896.64m, vatType: "EXEM", desc: "TPP - WBC Funded Care", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);

        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
    }

    [Fact]
    public void GroupDoubleListing_UnequalRepeatCounts_StaysUnreconciled()
    {
        // Guard: if the distinct lines do NOT all repeat the SAME number of times,
        // this is not the confirmed shape - must not be mislabelled as double listing.
        var rows = new List<SpendRow>
        {
            Row("T7", "GUARD CO", net: 2600m, gross: 6500m, vatType: "EXEM", desc: "A", idx: 0),
            Row("T7", "GUARD CO", net: 3900m, gross: 6500m, vatType: "EXEM", desc: "B", idx: 1),
            Row("T7", "GUARD CO", net: 2600m, gross: 6500m, vatType: "EXEM", desc: "A", idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);

        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    [Fact]
    public void GroupDoubleListing_RepeatedLinesNotSummingToStatedGross_StaysUnreconciled()
    {
        // Guard: lines repeat the same number of times (k=2) but the one-copy sum does
        // NOT reconcile the stated Gross - a coincidental repeat count, not this shape.
        var rows = new List<SpendRow>
        {
            Row("T8", "GUARD CO 2", net: 2600m, gross: 9999m, vatType: "EXEM", desc: "A", idx: 0),
            Row("T8", "GUARD CO 2", net: 3900m, gross: 9999m, vatType: "EXEM", desc: "B", idx: 1),
            Row("T8", "GUARD CO 2", net: 2600m, gross: 9999m, vatType: "EXEM", desc: "A", idx: 2),
            Row("T8", "GUARD CO 2", net: 3900m, gross: 9999m, vatType: "EXEM", desc: "B", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);

        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 2. Reverse-charge construction: a known false-positive trap (Session 4, ~296
    //    false mismatches on real Wokingham construction invoices up to £1.44m).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ReverseChargeConstruction_StandardRateWithNoUplift_IsNotFlagged()
    {
        var rows = new List<SpendRow>
        {
            Row("T4", "BUILD IT CONSTRUCTION LTD", net: 10000m, gross: 10000m, vatType: "STD", desc: "Construction works"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void StandardRate_GenuineVatUplift_StillReconciles()
    {
        // The reverse-charge dual check must not swallow the ordinary case: a real
        // STD invoice where gross DOES carry the 20% uplift.
        var rows = new List<SpendRow>
        {
            Row("T5", "OFFICE SUPPLIES LTD", net: 1000m, gross: 1200m, vatType: "STD"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void StandardRate_GenuineMismatch_IsStillCaught_NotSwallowedByReverseChargeCheck()
    {
        // Neither the uplifted (1200) nor the raw (1000) comparison matches a gross of
        // 1100 - this must still be flagged as Unreconciled, proving the dual check
        // removes the reverse-charge false positive without hiding real exceptions.
        var rows = new List<SpendRow>
        {
            Row("T6", "MYSTERY SUPPLIES LTD", net: 1000m, gross: 1100m, vatType: "STD"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 3. Care standard rates / recurring vs duplicate payments (checklist 4b, confirmed
    //    on real Reading data: [individual's name withheld] = recurring, [individual's name withheld]/Lynx Lettings = duplicate).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void RepeatedPayment_DifferentInvoiceNumbers_IsClassifiedAsRecurring_NotDuplicate()
    {
        var rows = new List<SpendRow>
        {
            Row("V1", "LANDLORD ONE", net: 682.98m, gross: 682.98m, desc: "Guaranteed Rent Scheme", payDate: "05/12/2021", invoiceNo: "264142"),
            Row("V2", "LANDLORD ONE", net: 682.98m, gross: 682.98m, desc: "Guaranteed Rent Scheme", payDate: "05/12/2021", invoiceNo: "264142"),
            Row("V3", "LANDLORD ONE", net: 682.98m, gross: 682.98m, desc: "Guaranteed Rent Scheme", payDate: "05/12/2021", invoiceNo: "264491"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.LikelyRecurring, b.Classification);
    }

    [Fact]
    public void RepeatedPayment_SameInvoiceNumberTwice_IsClassifiedAsLikelyDuplicate()
    {
        var rows = new List<SpendRow>
        {
            Row("V10", "LANDLORD TWO", net: 682.98m, gross: 682.98m, desc: "Guaranteed Rent Scheme", payDate: "05/12/2021", invoiceNo: "264133"),
            Row("V11", "LANDLORD TWO", net: 682.98m, gross: 682.98m, desc: "Guaranteed Rent Scheme", payDate: "05/12/2021", invoiceNo: "264133"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.LikelyDuplicate, b.Classification);
    }

    [Fact]
    public void RepeatedPayment_NoInvoiceNumberPublished_IsUnclear_AsPublishedNoVerdict()
    {
        var rows = new List<SpendRow>
        {
            Row("T20", "SOME CARE LTD", net: 500m, gross: 500m, desc: "Weekly placement", payDate: "05/12/2021"),
            Row("T21", "SOME CARE LTD", net: 500m, gross: 500m, desc: "Weekly placement", payDate: "05/12/2021"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 4. Redactions: never compared, excluded from every schedule (confirmed on real
    //    Merton and Wokingham data: shared placeholder text produces false matches).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void RedactedPayees_AreExcludedFromEverySchedule_AndNeverCompared()
    {
        var rows = new List<SpendRow>
        {
            Row("T30", "REDACTED PERSONAL DETAILS", net: 500m, gross: 500m, payDate: "01/01/2024"),
            Row("T31", "REDACTED PERSONAL DETAILS", net: 500m, gross: 500m, payDate: "01/01/2024"),
            Row("RED11538ACTED", "SOME CHILD PLACEMENT", net: 300m, gross: 400m, vatType: "STD"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
        Assert.Empty(result.ScheduleB);
        Assert.Empty(result.ScheduleD);
        Assert.Contains(result.Warnings, w => w.Contains("redacted", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------
    // 5. Merton-style supplier-furnished transaction ids: Schedule D is coincidence, not
    //    a finding, under SupplierFurnished scope (confirmed: trans 30970, two unrelated
    //    real suppliers).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void SupplierFurnishedTransactionId_MultiPayeeCollision_IsCaptionedAsCoincidence_NotAFinding()
    {
        var rows = new List<SpendRow>
        {
            Row("30970", "BIRKIN CLEANING SERVICES", net: 500m, gross: 500m, payDate: "01/01/2024"),
            Row("30970", "MORE HOUSE SCHOOL", net: 900m, gross: 900m, payDate: "02/01/2024"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.SupplierFurnished, GrossMeaning.PerLineAmount);
        Assert.Single(result.ScheduleD); // the rows genuinely exist, still reported...
        Assert.Contains(result.Warnings, w => w.Contains("coincidental", StringComparison.OrdinalIgnoreCase)
                                             && w.Contains("Do not present Schedule D as a finding", StringComparison.Ordinal));
    }

    [Fact]
    public void CouncilWideUniqueTransactionId_MultiPayee_IsAFindingWithNoCoincidenceCaveat()
    {
        var rows = new List<SpendRow>
        {
            Row("T40", "SUPPLIER A", net: 500m, gross: 500m, payDate: "01/01/2024"),
            Row("T40", "SUPPLIER B", net: 900m, gross: 900m, payDate: "01/01/2024"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Single(result.ScheduleD);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("coincidental", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------
    // 6. Reading-style signed amounts: negative lines are credits/corrections, never
    //    treated as an error, never merged with a positive line of the same reference.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void SignedAmounts_CreditLine_IsNotFlaggedAsAMismatch()
    {
        // No net/gross/VAT split at all (Reading's shape): Net defaults to Gross, so a
        // negative payment reconciles with itself trivially and must not appear in
        // Schedule A regardless of sign.
        var rows = new List<SpendRow>
        {
            Row("V100", "HEXAGON THEATRE LTD", net: -8626.95m, gross: -8626.95m, desc: "Credit note"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void SignedAmounts_PositiveAndNegativeSameReference_NetToTrueTotal_NotDoubleCounted()
    {
        // Two identical positive charges plus one negative credit under the same voucher,
        // netting to one real payment (confirmed on real Reading data, trans
        // 4657431/4657471/4657487) - Net defaults to Gross per row (no VAT split), and the
        // transaction's own true total is the sum; this must not be read as 2x anything.
        var rows = new List<SpendRow>
        {
            Row("V200", "TRAFFIC LEASING LTD", net: 8626.95m, gross: 8626.95m, idx: 0),
            Row("V200", "TRAFFIC LEASING LTD", net: 8626.95m, gross: 8626.95m, idx: 1),
            Row("V200", "TRAFFIC LEASING LTD", net: -8626.95m, gross: -8626.95m, idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Empty(result.ScheduleA); // sum nets to 8626.95 = PerLineAmount sum of gross; nothing to flag.
    }

    // ---------------------------------------------------------------------------------
    // 7. Debt-charge pattern: invoice = interest + exact round principal.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(1023.47)]
    [InlineData(523.10)]
    [InlineData(10034.99)]
    public void DebtChargeAmount_RoundPrincipalPlusInterest_IsDetected(decimal amount)
    {
        Assert.True(AuditEngine.TryDecomposeDebtCharge(amount, out var principal, out var interest));
        Assert.True(principal % 10m == 0m);
        Assert.True(interest > 0m);
        Assert.Equal(amount, principal + interest);
    }

    [Theory]
    [InlineData(1000.00)]   // exact round number - nothing to decompose, no charge present
    [InlineData(1050.00)]   // round + round "interest" - two fees, not interest
    [InlineData(12.34)]     // principal below the 100 floor at every base - too small to read as a debt
    public void DebtChargeAmount_NonChargeShapes_AreNotDetected(decimal amount)
    {
        Assert.False(AuditEngine.TryDecomposeDebtCharge(amount, out _, out _));
    }

    [Fact]
    public void DebtChargeAmount_MultiMillionInterAuthorityLoan_UsesTheTrueRoundPrincipal()
    {
        // Found by hand-checking real Wokingham data (Session 9): trans 3676987,
        // "Wandsworth Borough Council", VAT type NBUS (outside VAT scope - consistent
        // with inter-authority treasury lending, not a trade invoice), net 23,934.25,
        // gross 10,023,934.25. The true round number here is 10,000,000, not 10,023,000
        // (the 1000-base floor's own "round" number) - only recognised correctly once a
        // million-pound base is tried first. NOT independently confirmed against a loan
        // agreement or treasury record - this is a pattern match on amount shape, VAT
        // type and supplier (another local authority), not an opened source document;
        // flagged honestly as such in the write-up, not claimed as verified.
        Assert.True(AuditEngine.TryDecomposeDebtCharge(10_023_934.25m, out var principal, out var interest));
        Assert.Equal(10_000_000m, principal);
        Assert.Equal(23_934.25m, interest);
    }

    [Fact]
    public void DebtChargeAmount_RoundInterestOnARoundLoan_IsStillDetected()
    {
        // Found by hand-checking real Wokingham data (Session 9): trans 3669612,
        // Oxfordshire County Council, description "Interest Payments", gross
        // 5,082,500.00 = principal 5,000,000 + a CLEAN, ROUND 82,500 interest charge.
        // An earlier version of the detector wrongly rejected round-looking remainders
        // on the theory that "two round fees" isn't real interest - this real,
        // source-labelled example proved that wrong (opened and confirmed against the
        // raw FY2021-22 Wokingham CSV, row trans=3669612).
        Assert.True(AuditEngine.TryDecomposeDebtCharge(5_082_500.00m, out var principal, out var interest));
        Assert.Equal(5_000_000m, principal);
        Assert.Equal(82_500.00m, interest);
    }

    [Fact]
    public void ScheduleA_DebtChargeShapedMismatch_IsLabelledDebtCharge_NotGenericUnreconciled()
    {
        // A transaction whose Gross is a debt-charge shape (1000 principal + 23.47
        // interest) and whose net doesn't reconcile with it under either VAT path -
        // should be labelled DebtCharge, ranked below genuine Unreconciled rows.
        var rows = new List<SpendRow>
        {
            Row("T50", "DEBT RECOVERY SERVICES LTD", net: 900m, gross: 1023.47m, vatType: "EXEM",
                desc: "Debt Charges"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DebtCharge, a.Classification);
    }

    [Fact]
    public void ScheduleA_DebtChargeShapedMismatch_WithoutDebtEvidence_IsUnreconciled_NotDebtCharge()
    {
        // Session 21 fix (coordinator-reported bug): the SAME amount shape as the test
        // above (1000 principal + 23.47 interest), but with no "Debt Charges"/"Interest"/
        // "Loan" evidence anywhere in Description or CostCentreArea - must NOT be labelled
        // DebtCharge just because the number happens to decompose that way. This is the
        // general form of the real Optalis bug below.
        var rows = new List<SpendRow>
        {
            Row("T51", "SOME SUPPLIER LTD", net: 900m, gross: 1023.47m, vatType: "EXEM",
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 7b. Session 21 fix: real Optalis Limited rows the coordinator found mislabelled
    //     DebtCharge (Wokingham's own adult-social-care arm's-length company, "TPP -
    //     Inter Company"/"Inter Company Income" - never a lender). Opened against the
    //     raw FY2020-21/FY2021-22/FY2024-25 Wokingham CSVs (see export/wokingham/*).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Wokingham_OptalisSignFlip_3920573_IsNegativeNetSignFlip_NotDebtCharge()
    {
        // Raw file: net -952,162.00, gross 952,162.00, VAT type NBUS, CostCentreArea
        // "TPP - Inter Company". No debt evidence anywhere, and it's a pure sign flip -
        // must land on NegativeNetSignFlip, not DebtCharge (the amount shape happens to
        // decompose as 900,000 principal + 52,162 interest, which is why the old
        // evidence-free rule caught it first).
        var rows = new List<SpendRow>
        {
            Row("3920573", "OPTALIS", net: -952162m, gross: 952162m, vatType: "NBUS",
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    [Fact]
    public void Wokingham_OptalisSignFlip_3614883And3614885_AreNegativeNetSignFlip_NotDebtCharge()
    {
        // Raw file: each TransNo is 3 lines whose net sums to -468,163.00 against a
        // repeated stated gross of 468,163.00, VAT type EXEM, CostCentreArea
        // "TPP - Inter Company" - same sign-flip shape, two separate real TransNos.
        var rows3883 = new List<SpendRow>
        {
            Row("3614883", "OPTALIS", net: -152501m, gross: 468163m, vatType: "EXEM", idx: 0,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3614883", "OPTALIS", net: -118170m, gross: 468163m, vatType: "EXEM", idx: 1,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3614883", "OPTALIS", net: -197492m, gross: 468163m, vatType: "EXEM", idx: 2,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
        };
        var rows3885 = new List<SpendRow>
        {
            Row("3614885", "OPTALIS", net: -152501m, gross: 468163m, vatType: "EXEM", idx: 0,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3614885", "OPTALIS", net: -118170m, gross: 468163m, vatType: "EXEM", idx: 1,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3614885", "OPTALIS", net: -197492m, gross: 468163m, vatType: "EXEM", idx: 2,
                desc: "Fees", costCentreArea: "TPP - Inter Company"),
        };
        var result3883 = AuditEngine.Run(rows3883, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var result3885 = AuditEngine.Run(rows3885, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, Assert.Single(result3883.ScheduleA).Classification);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, Assert.Single(result3885.ScheduleA).Classification);
    }

    [Fact]
    public void Wokingham_OptalisDoubledLines_3558420_IsNotDebtCharge()
    {
        // Raw file: 14 lines, 7 distinct net amounts each published exactly twice
        // (183,400 / 1,434,881 / 148,225 / 5,775 / 1,081,150 / 254,919 / 819,413), mixed
        // VAT types (EXEM/STD), repeated stated gross 4,172,274.40, CostCentreArea
        // "TPP - Inter Company". This is a real DoubleListing-shaped anomaly the
        // mixed-VAT generalised check can't yet confirm (a separate, un-fixed gap, not
        // this session's bug) - the only requirement THIS fix pins is that it must NOT
        // be mislabelled DebtCharge (grossStated 4,172,274.40 happens to decompose as
        // 4,000,000 + 172,274.40, which is why the old evidence-free rule caught it).
        var rows = new List<SpendRow>
        {
            Row("3558420", "OPTALIS", net: 183400m, gross: 4172274.40m, vatType: "EXEM", idx: 0, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 183400m, gross: 4172274.40m, vatType: "EXEM", idx: 1, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 1434881m, gross: 4172274.40m, vatType: "EXEM", idx: 2, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 1434881m, gross: 4172274.40m, vatType: "EXEM", idx: 3, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 148225m, gross: 4172274.40m, vatType: "STD", idx: 4, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 148225m, gross: 4172274.40m, vatType: "STD", idx: 5, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 5775m, gross: 4172274.40m, vatType: "EXEM", idx: 6, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 5775m, gross: 4172274.40m, vatType: "EXEM", idx: 7, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 1081150m, gross: 4172274.40m, vatType: "EXEM", idx: 8, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 1081150m, gross: 4172274.40m, vatType: "EXEM", idx: 9, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 254919m, gross: 4172274.40m, vatType: "STD", idx: 10, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 254919m, gross: 4172274.40m, vatType: "STD", idx: 11, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 819413m, gross: 4172274.40m, vatType: "STD", idx: 12, desc: "Fees", costCentreArea: "TPP - Inter Company"),
            Row("3558420", "OPTALIS", net: 819413m, gross: 4172274.40m, vatType: "STD", idx: 13, desc: "Fees", costCentreArea: "TPP - Inter Company"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.NotEqual(ScheduleAClassification.DebtCharge, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 8. Ranking: unexplained exceptions rise to the top.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ScheduleA_RanksUnreconciledAboveDoubleListingAndDebtCharge()
    {
        var rows = new List<SpendRow>
        {
            // DoubleListing (2 identical lines, net sum = 2x stated gross)
            Row("D1", "DUP CO LTD", net: 200m, gross: 200m, vatType: "EXEM", idx: 0, source: "A"),
            Row("D1", "DUP CO LTD", net: 200m, gross: 200m, vatType: "EXEM", idx: 1, source: "A"),
            // DebtCharge (1000 + 23.47)
            Row("D2", "DEBT CO LTD", net: 900m, gross: 1023.47m, vatType: "EXEM", source: "A"),
            // genuine Unreconciled (doesn't fit either pattern - remainder too large under
            // either round base to read as debt-charge interest)
            Row("D3", "MYSTERY CO LTD", net: 1000m, gross: 1337.77m, vatType: "STD", source: "A"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(3, result.ScheduleA.Count);
        Assert.Equal(ScheduleAClassification.Unreconciled, result.ScheduleA[0].Classification);
        Assert.Equal("D3", result.ScheduleA[0].TransactionId);
    }

    // ---------------------------------------------------------------------------------
    // 9. Confirmed-unexplained real cases (opened and confirmed by hand; see
    //    COUNCIL_AUDIT_PRODUCT_PLAN.md / FEEDBACK.md for the citation). These stay
    //    Unreconciled on purpose - they are genuine exceptions, not engine bugs.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Wokingham_KnownCrossTransNoPair_StaysUnreconciled()
    {
        // Session 4: cross-TransNo pairs, same supplier+amount+day, non-recurring
        // category, 7/7 hand-opened and confirmed to exist as described. Modelled here
        // as a single transaction that fails to reconcile under both VAT paths, to
        // pin the "this stays a real exception" behaviour under regression.
        var rows = new List<SpendRow>
        {
            Row("T99", "GENUINE EXCEPTION LTD", net: 5000m, gross: 5800m, vatType: "STD"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    [Fact]
    public void Wokingham_KnownSignMismatchedPrepaymentClawback_IsNowLabelledNegativeNetSignFlip()
    {
        // Opened and confirmed against the raw FY2022-23 Wokingham CSV, trans 3679988:
        // Dimensions (UK) Ltd, "ASC Covid-19 Provider Prepayments", Net published as
        // NEGATIVE £37,000.00 while Gross is published as POSITIVE £37,000.00 on the
        // same line. Session 9/10 correctly left this Unreconciled because no rule
        // explained it THEN. Session 13's dedicated opposite-sign census found this
        // exact shape recurs 2,383 times (68.5% of what was Unreconciled going into that
        // cycle) across hundreds of suppliers, every VAT type, growing year over year -
        // repeatable enough to name and label (NegativeNetSignFlip), though still NOT
        // claimed to be "explained" (why Net's sign is inverted is not established).
        // Updated from "StaysUnreconciled" deliberately, by a human/this session deciding
        // so, exactly as Session 9's own comment on this row said reclassifying it would
        // require.
        var rows = new List<SpendRow>
        {
            Row("T3679988", "DIMENSIONS (UK) LTD", net: -37000m, gross: 37000m, vatType: "ZERO",
                desc: "ASC Covid-19 Provider Prepayments"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    [Fact]
    public void Wokingham_KnownDoubleListing_LandAcquisition_IsConfirmedAgainstRawFile()
    {
        // Opened and confirmed against the raw FY2020-21 Wokingham CSV, trans 3557488:
        // two byte-identical rows (same date, same net/gross £825,000.00, same supplier
        // "Attwells", same description "Acquisition of Land") 225 rows apart in the same
        // published file.
        var rows = new List<SpendRow>
        {
            Row("3557488", "ATTWELLS", net: 825000m, gross: 825000m, vatType: "NBUS", desc: "Acquisition of Land", idx: 0),
            Row("3557488", "ATTWELLS", net: 825000m, gross: 825000m, vatType: "NBUS", desc: "Acquisition of Land", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
    }

    [Fact]
    public void Wokingham_KnownDebtCharge_WandsworthLoanInterest_IsConfirmedAgainstRawFile()
    {
        // Opened and confirmed against the raw FY2021-22 Wokingham CSV, trans 3676987:
        // "Resources & Assets" / "Debt Charges" / "Interest Payments" to Wandsworth
        // Borough Council, net £23,934.25, gross £10,023,934.25 - the source data's own
        // category names confirm this is exactly the debt-charge pattern, not a
        // coincidental amount shape.
        var rows = new List<SpendRow>
        {
            Row("3676987", "WANDSWORTH BOROUGH COUNCIL", net: 23934.25m, gross: 10023934.25m, vatType: "NBUS",
                desc: "Debt Charges"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DebtCharge, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 10. Session 10 hand-check cycle widening: more confirmed real cases (construction-
    //     framework DoubleListing and inter-authority DebtCharge, opened against the raw
    //     Wokingham files this session - see FEEDBACK.md Session 10 for the full list of
    //     21/21 DoubleListing and 13/13 DebtCharge opened-and-confirmed this cycle).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Wokingham_KnownDoubleListing_BalfourBeattyHighwaysFramework_IsConfirmedAgainstRawFile()
    {
        // Opened and confirmed against the raw FY2020-21 Wokingham CSV, trans 3558511:
        // two identical lines of £2,930,482.32 net / £3,516,578.78 gross, "Highway
        // Delivery - Projects", 148 rows apart in the same published file. The largest of
        // five Balfour Beatty Group Ltd DoubleListing transactions opened this session
        // (3558511, 3556265, 3558414, 3558510, plus Volker Highways 3561755 and Wates
        // Construction 3659594 - the "large construction-framework cases" FEEDBACK.md
        // Session 9 named as the next cycle's starting point, now opened).
        var rows = new List<SpendRow>
        {
            Row("3558511", "BALFOUR BEATTY GROUP LTD", net: 2930482.32m, gross: 3516578.78m, vatType: "STD",
                desc: "Highway Delivery - Projects", idx: 0),
            Row("3558511", "BALFOUR BEATTY GROUP LTD", net: 2930482.32m, gross: 3516578.78m, vatType: "STD",
                desc: "Highway Delivery - Projects", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
    }

    [Fact]
    public void Wokingham_KnownDebtCharge_OxfordshireTwinSameDayLoans_AreBothConfirmedAgainstRawFile()
    {
        // Opened and confirmed against the raw FY2022-23 Wokingham CSV: two SEPARATE
        // transaction numbers (3685097, 3685094) on the same day, same supplier
        // (Oxfordshire County Council (not schools)), each its own £5,000,000 principal
        // plus a different small interest remainder - two genuine debt-charge invoices,
        // not one group double-counted (they have different TransNos and are never
        // grouped together by the engine).
        var rows97 = new List<SpendRow> { Row("3685097", "OXFORDSHIRE COUNTY COUNCIL (NOT SCHOOLS)", net: 5786.30m, gross: 5005786.30m, vatType: "NBUS", desc: "Debt Charges") };
        var rows94 = new List<SpendRow> { Row("3685094", "OXFORDSHIRE COUNTY COUNCIL (NOT SCHOOLS)", net: 5753.42m, gross: 5005753.42m, vatType: "NBUS", desc: "Debt Charges") };
        var result97 = AuditEngine.Run(rows97, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var result94 = AuditEngine.Run(rows94, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(ScheduleAClassification.DebtCharge, Assert.Single(result97.ScheduleA).Classification);
        Assert.Equal(ScheduleAClassification.DebtCharge, Assert.Single(result94.ScheduleA).Classification);
    }

    [Fact]
    public void Wokingham_KnownUnreconciled_OneDayAtATimeHomeCare_IsNowLabelledNegativeNetSignFlip()
    {
        // Opened and confirmed against the raw FY2025-26 Wokingham CSV: trans 7010929 and
        // 7020203, "One Day at a Time Home Care Ltd", "TPP - WBC Funded Care", each its
        // own single line with net published as NEGATIVE £10,000 and gross as POSITIVE
        // £10,000 - the same opposite-signed-pair shape as Session 9's Dimensions (UK)
        // Ltd finding. Session 10 confirmed it recurs across suppliers/years as a true
        // sample finding; Session 13's full census found it is in fact the single
        // largest shape in the whole Unreconciled population - now labelled
        // NegativeNetSignFlip, same reasoning as the Dimensions case above.
        var rows = new List<SpendRow>
        {
            Row("7010929", "ONE DAY AT A TIME HOME CARE LTD", net: -10000m, gross: 10000m, vatType: "NBUS",
                desc: "TPP - WBC Funded Care"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 11. Reading's SupplierInvoiceNumber field (Session 10, two findings in one cycle):
    //     (a) the column name originally used ("Supplier Invoice No") does not exist in
    //     any real Reading file - a genuine mapping bug, now fixed by leaving the field
    //     unmapped; (b) the TEMPTING fix (map it to "Payment Number" instead) is actively
    //     wrong, not just unhelpful: Payment Number is a BACS payment-run id covering many
    //     DIFFERENT vouchers/invoices, confirmed by hand against the raw 2026-02 file
    //     (MMCG (2) LTD, Payment Number 108150, 25 different Voucher Numbers, two
    //     different Cost Centres/Service Areas, one standardised £5,200 rate - a bundled
    //     recurring-rate run, not a duplicate invoice). This test pins BOTH halves: the
    //     real mapping leaves SupplierInvoiceNumber null, and the MMCG-shaped group stays
    //     Unclear, not LikelyDuplicate - so a future "helpful" remap to Payment Number
    //     fails here before it ships a false-positive label to a visitor.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Reading_RealColumnMapping_LeavesSupplierInvoiceNumberUnmapped()
    {
        var header = new[] { "Purchasing Organisation", "Payment Date", "Voucher Number (Internal Classification)",
            "Payment Number", "Supplier Name", "Service Area", "Amount (£)", "Purpose",
            "Supplier Type (Internal Classification)", "Invoice Type (Internal Classification)", "Cost Centre", "Directorate" };
        var row1 = new[] { "RBC Legal Entity", "01/12/2021", "4619430", "264133", "LANDLORD TWO",
            "Guaranteed Rent Scheme", "682.98", "Rents", "SUPPLIER", "STANDARD", "5872", "KAAA-Directorate" };
        var table = new List<string[]> { header, row1 };
        var warnings = new List<string>();
        var rows = AuditEngine.MapRows(table, SupportedCouncils.Reading.Mapping, "2021-12", warnings);

        Assert.All(rows, r => Assert.Null(r.SupplierInvoiceNumber));
    }

    [Fact]
    public void BACSPaymentRun_SharedAcrossDifferentVouchersAndCostCentres_MustNotBeMislabelledDuplicate()
    {
        // The MMCG (2) LTD shape, modelled directly (not through MapRows): 3 different
        // Voucher Numbers, one shared BACS Payment Number, same standardised rate, same
        // date, but genuinely different placements (different Cost Centre/Service Area
        // in the real data, which the engine doesn't even see - it only has SpendRow's
        // fields). With SupplierInvoiceNumber correctly left null (no safe field exists
        // for Reading), this must stay Unclear - never LikelyDuplicate - regardless of
        // what any single shared numeric field looks like.
        var rows = new List<SpendRow>
        {
            Row("345474", "MMCG (2) LTD", net: 5200m, gross: 5200m, desc: "Purchased Care", payDate: "25/02/2026"),
            Row("345587", "MMCG (2) LTD", net: 5200m, gross: 5200m, desc: "Purchased Care", payDate: "25/02/2026"),
            Row("345636", "MMCG (2) LTD", net: 5200m, gross: 5200m, desc: "Purchased Care", payDate: "25/02/2026"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 12. Session 12: the JUDGEMENT step (reading full context for a random sample, not
    //     just re-confirming existence/arithmetic) on Wokingham's Unreconciled sample
    //     found two new real, repeatable patterns - both confirmed against the raw
    //     FY2024-25/FY2021-22 CSVs, both now coded and pinned here.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void EarlyPaymentProgramme_NegativeDiscountFeeLines_IsLabelledNotUnreconciled()
    {
        // Opened and confirmed against the raw FY2024-25 Wokingham CSV, trans 3904409,
        // supplier "School Express": 18 lines, every one a NEGATIVE early-payment
        // discount fee (-£26.52, -£51.11, ... -£38.48), every one carrying the council's
        // own "Early payment programme" description, all under one repeated Gross
        // (£906.51). Confirmed this is a named, recurring scheme (3,730 raw rows across
        // six years, multiple real suppliers including Oxygen Finance Ltd, a known UK
        // council supply-chain-finance provider) - not an arithmetic mismatch, and not
        // reconcilable from this file alone (the underlying invoice amounts aren't
        // published), so it must be labelled, not left as undifferentiated Unreconciled.
        // Note: the real council data's own Description for these lines is the generic
        // "Fees" - the "Early payment programme" label lives in Cost Centre Area, a
        // separate column Session 12 added (see SpendRow.CostCentreArea) after a first
        // manual read of the raw columns got this backwards.
        var rows = new List<SpendRow>
        {
            Row("3904409", "SCHOOL EXPRESS", net: -26.52m, gross: 906.51m, vatType: "STD", desc: "Fees", costCentreArea: "Early payment programme", idx: 0),
            Row("3904409", "SCHOOL EXPRESS", net: -51.11m, gross: 906.51m, vatType: "STD", desc: "Fees", costCentreArea: "Early payment programme", idx: 1),
            Row("3904409", "SCHOOL EXPRESS", net: -45.31m, gross: 906.51m, vatType: "STD", desc: "Fees", costCentreArea: "Early payment programme", idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.EarlyPaymentProgramme, a.Classification);
    }

    [Fact]
    public void EarlyPaymentProgramme_RequiresAllLinesNegativeAndSameCostCentreArea_OtherwiseStaysUnreconciled()
    {
        // Guards against over-matching: a mixed-sign group, or a group whose Cost Centre
        // Area doesn't carry the exact council label (including a council with no
        // CostCentreArea mapped at all, left null by default), must not be swept into
        // this classification just because it has more than one line.
        var mixedSign = new List<SpendRow>
        {
            Row("T1", "SOME CO", net: -10m, gross: 100m, vatType: "STD", costCentreArea: "Early payment programme", idx: 0),
            Row("T1", "SOME CO", net: 50m, gross: 100m, vatType: "STD", costCentreArea: "Early payment programme", idx: 1),
        };
        var wrongCostCentre = new List<SpendRow>
        {
            Row("T2", "SOME CO", net: -10m, gross: 100m, vatType: "STD", costCentreArea: "Maintenance", idx: 0),
            Row("T2", "SOME CO", net: -20m, gross: 100m, vatType: "STD", costCentreArea: "Maintenance", idx: 1),
        };
        var noCostCentreMapped = new List<SpendRow>
        {
            Row("T3", "SOME CO", net: -10m, gross: 100m, vatType: "STD", idx: 0),
            Row("T3", "SOME CO", net: -20m, gross: 100m, vatType: "STD", idx: 1),
        };
        var result1 = AuditEngine.Run(mixedSign, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var result2 = AuditEngine.Run(wrongCostCentre, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var result3 = AuditEngine.Run(noCostCentreMapped, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(ScheduleAClassification.Unreconciled, Assert.Single(result1.ScheduleA).Classification);
        Assert.Equal(ScheduleAClassification.Unreconciled, Assert.Single(result2.ScheduleA).Classification);
        Assert.Equal(ScheduleAClassification.Unreconciled, Assert.Single(result3.ScheduleA).Classification);
    }

    [Fact]
    public void VatRoundingNoise_PenceLevelGap_IsLabelledNotUnreconciled()
    {
        // Opened and confirmed against the raw FY2025-26 Wokingham CSV, trans 3991866,
        // "Education Boutique Ltd": net 583.32, VAT type STD (20% uplift expected =
        // 699.98), stated gross 700.00 - a 2-pence gap, consistent with VAT rounded per
        // line then summed rather than once on the total.
        var rows = new List<SpendRow>
        {
            Row("3991866", "EDUCATION BOUTIQUE LTD", net: 583.32m, gross: 700.00m, vatType: "STD",
                desc: "TPP - Other Establishments"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.VatRoundingNoise, a.Classification);
    }

    [Fact]
    public void VatRoundingNoise_SecondRealExample_CarringtonWest_IsLabelledNotUnreconciled()
    {
        // Opened and confirmed against the raw FY2021-22 Wokingham CSV, trans 3626256,
        // "Carrington West Ltd": net 2447.52, VAT type STD (20% uplift expected =
        // 2937.02), stated gross 2937.00 - a 2-pence gap, the same magnitude as the
        // Education Boutique case above, independent evidence this is systematic
        // rounding noise rather than coincidence on a single row.
        var rows = new List<SpendRow>
        {
            Row("3626256", "CARRINGTON WEST LTD", net: 2447.52m, gross: 2937.00m, vatType: "STD", desc: "Construction"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.VatRoundingNoise, a.Classification);
    }

    [Fact]
    public void VatRoundingNoise_IsNarrow_LargerGapStaysUnreconciled()
    {
        // Guard against widening the tolerance too far: Holt School's trans 3928781 in
        // the same judgement sample was off by GBP 4.03 under its own published RRTE
        // (5%) rate and must stay Unreconciled, not be swept into rounding noise.
        var rows = new List<SpendRow>
        {
            Row("3928781", "HOLT SCHOOL", net: 1279.52m, gross: 1347.53m, vatType: "RRTE", desc: "Gas"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    // ---------------------------------------------------------------------------------
    // 13. Session 13: a dedicated judgement census of the opposite-sign shape (not just a
    //     sample) found this is a GENERAL pattern, not limited to the no-VAT-uplift case
    //     Session 9/10 saw (net exactly = -gross): flipping ONLY the sign of a negative
    //     Net reconciles the stated Gross exactly under the row's own VAT Type too. Found
    //     after the Showroom owner flagged a row with negative ExpectedGross against a
    //     positive Gross, which is exactly this shape viewed through a different field.
    //     2,383 of the 3,479 Wokingham rows that were still Unreconciled going into this
    //     cycle hit this rule (2,129 of the no-VAT-uplift sub-case, 254 more across STD/
    //     RRTE VAT types), 40/40 hand-checked and confirmed, 95% Wilson CI [91.2%, 100%].
    // ---------------------------------------------------------------------------------

    [Fact]
    public void NegativeNetSignFlip_NoVatUplift_ExactOppositeSign_IsLabelledNotUnreconciled()
    {
        // The no-uplift sub-case Session 9/10 already found (ZERO/EXEM/NBUS/OSCP): net is
        // exactly the negative of gross. Real example shape: Dimensions (UK) Ltd,
        // trans 3679988, net -37000.00, gross 37000.00, VAT type ZERO.
        var rows = new List<SpendRow>
        {
            Row("3679988", "DIMENSIONS (UK) LTD", net: -37000.00m, gross: 37000.00m, vatType: "ZERO", desc: "TPP - WBC Funded Care"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    [Fact]
    public void NegativeNetSignFlip_WithVatUplift_FlippedSignReconciles_IsLabelledNotUnreconciled()
    {
        // The broader case this session added: Net is negative, but a POSITIVE Net of the
        // same magnitude would reconcile the stated Gross exactly once the row's own VAT
        // Type uplift is applied - not just a raw sign flip. Real example shape: a payee that is
        // a private individual (name withheld), net -20000.00, gross 24000.00, VAT type STD
        // (20%): -(-20000)*1.20 = 24000 = stated gross, exactly.
        var rows = new List<SpendRow>
        {
            Row("3904579", "CONSULTANT ONE PSYCHOLOGY", net: -20000.00m, gross: 24000.00m, vatType: "STD", desc: "Services - Consultancy"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    [Fact]
    public void NegativeNetSignFlip_MultiLineGroup_SumsBeforeChecking_IsLabelledNotUnreconciled()
    {
        // Real example shape: Phoenix Healthcare - Warren Lodge, trans 3619062, 2 lines
        // summing to net -8970.11, gross 8970.11 (EXEM, no uplift) - the sign-flip check
        // runs on the GROUP's net sum, same as every other Schedule A rule, not per line.
        var rows = new List<SpendRow>
        {
            Row("3619062", "PHOENIX HEALTHCARE - WARREN LODGE", net: -4000.00m, gross: 8970.11m, vatType: "EXEM", desc: "TPP - WBC Funded Care", idx: 0),
            Row("3619062", "PHOENIX HEALTHCARE - WARREN LODGE", net: -4970.11m, gross: 8970.11m, vatType: "EXEM", desc: "TPP - WBC Funded Care", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.NegativeNetSignFlip, a.Classification);
    }

    [Fact]
    public void NegativeNetSignFlip_DoesNotOverMatch_GenuineGapAndZeroNetStayUnreconciled()
    {
        // Guards: (1) a negative Net whose magnitude does NOT reconcile the stated Gross
        // under either comparison is a real, different exception and must stay
        // Unreconciled, not be swept in just because Net happens to be negative;
        // (2) Net == 0 (a real, separately-named Session 13 next-cycle item - 166
        // Wokingham rows, no single dominant description/service-area trait found yet)
        // must not be caught by this rule either - flipping the sign of zero changes
        // nothing, so it can never "reconcile" by this mechanism, and must not be
        // accidentally matched by a sign check that forgot to require Net < 0.
        var genuineGap = new List<SpendRow>
        {
            Row("T1", "SOME CO", net: -1000.00m, gross: 50.00m, vatType: "STD", desc: "x"),
        };
        var zeroNet = new List<SpendRow>
        {
            Row("T2", "SOME CO", net: 0.00m, gross: 500.00m, vatType: "STD", desc: "x", idx: 0),
            Row("T2", "SOME CO", net: 0.00m, gross: 500.00m, vatType: "STD", desc: "x", idx: 1),
        };
        var result1 = AuditEngine.Run(genuineGap, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var result2 = AuditEngine.Run(zeroNet, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(ScheduleAClassification.Unreconciled, Assert.Single(result1.ScheduleA).Classification);
        Assert.Equal(ScheduleAClassification.Unreconciled, Assert.Single(result2.ScheduleA).Classification);
    }

    // ---------------------------------------------------------------------------------
    // 14. Session 14: Schedule R (credit/refund matching), built for Reading - the only
    //     onboarded council that publishes an explicit Invoice Type column. Confirmed
    //     real: "CREDIT" (2021-2023 files, always negative) and "RBC Refunds Manual
    //     Entry"/"RBC AR REFUNDS" (2024 onward, always POSITIVE - the council's own sign
    //     convention for a refund changed between the two eras, confirmed by hand on
    //     real files: 720/720 CREDIT rows negative in reading_2023-03.csv, 134/134 "RBC
    //     Refunds Manual Entry" rows positive in reading_2026-02.csv).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ScheduleR_CreditWithMatchingChargeSameSupplier_IsMatchedOffsettingCharge()
    {
        var rows = new List<SpendRow>
        {
            Row("V1", "EVERSHEDS LLP", net: 458.16m, gross: 458.16m, desc: "External Fees", payDate: "01/03/2023", invoiceType: "STANDARD", idx: 0),
            Row("V2", "EVERSHEDS LLP", net: -458.16m, gross: -458.16m, desc: "External Fees", payDate: "15/03/2023", invoiceType: "CREDIT", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.MatchedOffsettingCharge, r.Classification);
    }

    [Fact]
    public void ScheduleR_RefundWithNoMatchingChargeAnywhere_IsUnmatched()
    {
        // Real shape (newer era): "RBC Refunds Manual Entry" rows are POSITIVE, not
        // negative - the matching logic must compare by magnitude, not sign, and must
        // correctly report Unmatched when no same-supplier charge exists at all.
        var rows = new List<SpendRow>
        {
            // Session 27: 250,000 (not 1,800) because a small positive refund in a file with no negative line is now
            // a RefundChannelPayment; one of GBP 100,000 or more stays Unmatched (see Session26Tests section 6).
            Row("V3", "NORTHWOOD READING", net: 250000m, gross: 250000m, desc: "Preventative & Support Svces",
                payDate: "02/02/2026", invoiceType: "RBC Refunds Manual Entry"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.Unmatched, r.Classification);
    }

    [Fact]
    public void ScheduleR_MatchRequiresSameSupplier_DifferentSupplierSameAmountStaysUnmatched()
    {
        // Guard: a matching-magnitude charge for a DIFFERENT supplier must not count as
        // an offsetting charge, even though the amounts line up exactly.
        var rows = new List<SpendRow>
        {
            Row("V10", "SUPPLIER A", net: 1000m, gross: 1000m, payDate: "01/03/2023", invoiceType: "STANDARD", idx: 0),
            Row("V11", "SUPPLIER B", net: -1000m, gross: -1000m, payDate: "02/03/2023", invoiceType: "CREDIT", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.Unmatched, r.Classification);
    }

    [Fact]
    public void ScheduleR_CouncilWithNoInvoiceTypeMapped_IsStructurallyEmpty()
    {
        // Wokingham/Merton don't map InvoiceType at all - Schedule R must be empty, not
        // throw or fabricate a classification, same "not a bug" principle as Reading's
        // own empty Schedule A.
        var rows = new List<SpendRow>
        {
            Row("T1", "SOME CO", net: 500m, gross: 500m, vatType: "EXEM"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleR);
    }

    // ---------------------------------------------------------------------------------
    // 15. Session 15 widening: a structural probe over Schedule R's FULL Unmatched
    //     population (not a judgement sample) found 10 real credits whose magnitude
    //     equals the SUM of two same-supplier, same-VOUCHER charge lines - confirmed
    //     real by hand against reading_2023-03.csv (STRICTLY THEATRE CO LIMITED voucher
    //     4779485: credit -39,438.00 = charges 32,627.53 + 6,810.47, a box-office
    //     settlement voucher with many positive/negative lines, same shape as the
    //     already-documented Hexagon venue pattern). Scoped deliberately narrow to the
    //     credit's OWN voucher number (not any same-supplier charge anywhere in the
    //     window) because a frequently-paid supplier will turn up SOME coincidental
    //     pair summing near any given credit over a year by chance - the same-voucher
    //     restriction removes that risk entirely (the lines are definitionally part of
    //     one published transaction). The probe also found 115 CROSS-voucher
    //     "coincidental sum" candidates - deliberately NOT coded (see AuditEngine.cs
    //     comment at the Schedule R split-match block).
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ScheduleR_CreditEqualsSumOfTwoSameVoucherCharges_IsMatchedOffsettingCharge()
    {
        var rows = new List<SpendRow>
        {
            Row("4779485", "STRICTLY THEATRE CO LIMITED", net: 32627.53m, gross: 32627.53m, payDate: "13/03/2023", invoiceType: "STANDARD", idx: 0),
            Row("4779485", "STRICTLY THEATRE CO LIMITED", net: 6810.47m, gross: 6810.47m, payDate: "13/03/2023", invoiceType: "STANDARD", idx: 1),
            Row("4779485", "STRICTLY THEATRE CO LIMITED", net: -39438m, gross: -39438m, payDate: "13/03/2023", invoiceType: "CREDIT", idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.MatchedOffsettingCharge, r.Classification);
        Assert.Contains("SAME-VOUCHER", r.ClassificationDetail);
    }

    [Fact]
    public void ScheduleR_CreditEqualsSumOfTwoCrossVoucherCharges_StaysUnmatched()
    {
        // Guard against the exact "coincidence" risk this widening deliberately avoids:
        // the same two amounts summing correctly, but under a DIFFERENT voucher number
        // from the credit's own, must NOT count as a match - this is the 115/125
        // cross-voucher case the probe found and the engine deliberately does not code.
        var rows = new List<SpendRow>
        {
            Row("9000001", "SAME CORP LTD", net: 32627.53m, gross: 32627.53m, payDate: "13/03/2023", invoiceType: "STANDARD", idx: 0),
            Row("9000002", "SAME CORP LTD", net: 6810.47m, gross: 6810.47m, payDate: "14/03/2023", invoiceType: "STANDARD", idx: 1),
            Row("9000003", "SAME CORP LTD", net: -39438m, gross: -39438m, payDate: "15/03/2023", invoiceType: "CREDIT", idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.Unmatched, r.Classification);
    }
}
