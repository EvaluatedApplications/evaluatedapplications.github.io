using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 26 rules, each from a hand-checked real case:
///  1. Per-line VAT uplift for groups whose lines carry different VAT Type labels (Wokingham: 971 mixed-label
///     groups in the Unreconciled set, 724 reconcile to the penny line by line; EDF Energy, Rossendales,
///     Bristow &amp; Sutor, Caterlink).
///  2. Multi-payee transactions are tested once as a whole (Wokingham: 10 academy payment-run transactions,
///     230 per-supplier rows).
///  3. Standing monthly payments: Reading's Brighter Futures for Children block of 7 fixed amounts paid 3x in
///     Feb 2024 with Dec and Jan empty and a March refund (hand-checked month by month).
/// </summary>
public class Session26Tests
{
    private static SpendRow Row(
        string trans, string supplier, decimal net, decimal gross,
        string? vatType = null, string? desc = "x", string? payDate = "01/04/2024",
        string source = "FY1", int idx = 0, string? invoiceType = null) =>
        new(source, trans, supplier, payDate, AuditEngine.ParseDate(payDate), desc, "Area",
            net, gross, null, vatType, idx, null, null, invoiceType);

    // ---- 1. per-line VAT ----

    [Fact]
    public void MixedVatLabels_ReconcileLineByLine_AreNotFlagged()
    {
        // 1000 at 20% + 500 at 5% = 1200 + 525 = 1725.00 stated; the first label alone (STD) would expect 1800.
        var rows = new List<SpendRow>
        {
            Row("T1", "EDF ENERGY", 1000m, 1725m, "STD  - VAT Purchases Standard Rate", idx: 0),
            Row("T1", "EDF ENERGY", 500m, 1725m, "RRTE - VAT Purchases Reduced Rate", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void Wokingham_Trans3583877_ReadingRe3WasteDebtCharges_ReconcilesPerLine_NotADebtCharge()
    {
        // Real Wokingham FY2020-21 trans 3583877 (Reading Borough Council, "TPP - Waste Collection", Cost Centre Area
        // "Debt Charges"): 838,465.48 Standard + (277,289.92) Zero-rated, stated Gross 728,868.66.
        // 838,465.48 x 1.2 = 1,006,158.58; less 277,289.92 = 728,868.66 exactly. Before Session 26 the first line's
        // label alone left a 167,693.10 gap that the debt-charge shape then wrongly "explained".
        var rows = new List<SpendRow>
        {
            Row("3583877", "READING BOROUGH COUNCIL", 838465.48m, 728868.66m, "STD  - VAT Purchases Standard Rate", desc: "TPP - Waste Collection", idx: 0),
            Row("3583877", "READING BOROUGH COUNCIL", -277289.92m, 728868.66m, "ZERO - VAT Purchases Zero Rated", desc: "TPP - Waste Collection", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void MixedVatLabels_ThatDoNotReconcileEvenPerLine_StayUnreconciled()
    {
        var rows = new List<SpendRow>
        {
            Row("T2", "EDF ENERGY", 1000m, 1750m, "STD  - VAT Purchases Standard Rate", idx: 0),
            Row("T2", "EDF ENERGY", 500m, 1750m, "RRTE - VAT Purchases Reduced Rate", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
        Assert.Equal(1725m, a.ExpectedGross); // the per-line reading (1725) is closer to 1750 than the whole-at-first-label reading (1800)
    }

    [Fact]
    public void Wokingham_Trans3765708_NetworkHealthcare_WholeNetAtTheFirstLabelStillReconciles()
    {
        // Real Wokingham FY2022-23 trans 3765708: Standard 1,003.38 + Exempt 214.62, stated Gross 1,461.60 =
        // 1,218.00 x 1.2. Per line it would be 1,418.68; the council applied Standard to the whole. The group must
        // stay reconciled (it was before the per-line rule, which must not regress it).
        var rows = new List<SpendRow>
        {
            Row("3765708", "NETWORK HEALTHCARE", 1003.38m, 1461.60m, "STD  - VAT Purchases Standard Rate", desc: "TPP - WBC Funded Care", idx: 0),
            Row("3765708", "NETWORK HEALTHCARE", 214.62m, 1461.60m, "EXEM - VAT Purchases Exempt", desc: "TPP - WBC Funded Care", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void SingleLabelGroup_StillUsesTheFirstLabel()
    {
        var rows = new List<SpendRow>
        {
            Row("T3", "ACME", 1000m, 1500m, "STD  - VAT Purchases Standard Rate"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(1200m, a.ExpectedGross);
    }

    // ---- 1b. VAT-inclusive double listing ----

    [Fact]
    public void Wokingham_Trans3557249_Reds10_InvoicePublishedTwice_WithVat_IsDoubleListing()
    {
        // Real FY2020-21 trans 3557249: lines 3,147.16 and 558,320.02, each twice, Standard rate, stated Gross
        // 673,760.62 = (3,147.16 + 558,320.02) x 1.2. Gross / Net = 0.6, not 0.5, because of the VAT.
        string std = "STD  - VAT Purchases Standard Rate";
        var rows = new List<SpendRow>
        {
            Row("3557249", "REDS10 (UK) LIMITED", 3147.16m, 673760.62m, std, desc: "Construction", idx: 0),
            Row("3557249", "REDS10 (UK) LIMITED", 558320.02m, 673760.62m, std, desc: "Construction", idx: 1),
            Row("3557249", "REDS10 (UK) LIMITED", 3147.16m, 673760.62m, std, desc: "Construction", idx: 2),
            Row("3557249", "REDS10 (UK) LIMITED", 558320.02m, 673760.62m, std, desc: "Construction", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.DoubleListing, a.Classification);
    }

    [Fact]
    public void RepeatedLines_WhoseVatUpliftedCopyDoesNotMatchTheGross_AreNotDoubleListing()
    {
        string std = "STD  - VAT Purchases Standard Rate";
        var rows = new List<SpendRow>
        {
            Row("V1", "ACME", 100m, 700m, std, desc: "a", idx: 0),
            Row("V1", "ACME", 200m, 700m, std, desc: "b", idx: 1),
            Row("V1", "ACME", 100m, 700m, std, desc: "a", idx: 2),
            Row("V1", "ACME", 200m, 700m, std, desc: "b", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(ScheduleAClassification.Unreconciled, a.Classification);
    }

    // ---- 2. multi-payee transaction ----

    [Fact]
    public void MultiPayeeTransaction_WithOneRepeatedGross_ProducesOneTransactionRow_NotOnePerSupplier()
    {
        var rows = new List<SpendRow>
        {
            Row("P1", "SCHOOL A", 100m, 500m, "EXEM", idx: 0),
            Row("P1", "SCHOOL B", 150m, 500m, "EXEM", idx: 1),
            Row("P1", "SCHOOL C", 50m, 500m, "EXEM", idx: 2),
            Row("P1", "SCHOOL C", 25m, 500m, "EXEM", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        var a = Assert.Single(result.ScheduleA);
        Assert.Equal(325m, a.Net);
        Assert.Equal(500m, a.Gross);
        Assert.Equal(4, a.LineCount);
        Assert.Contains("multi-payee", a.ClassificationDetail);
    }

    [Fact]
    public void MultiPayeeTransaction_WhoseLinesSumToTheGross_IsNotReportedAtAll()
    {
        var rows = new List<SpendRow>
        {
            Row("P2", "SCHOOL A", 100m, 300m, "EXEM", idx: 0),
            Row("P2", "SCHOOL B", 150m, 300m, "EXEM", idx: 1),
            Row("P2", "SCHOOL C", 50m, 300m, "EXEM", idx: 2),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Empty(result.ScheduleA);
    }

    [Fact]
    public void TwoPayeeTransaction_StillUsesThePerSupplierPass()
    {
        var rows = new List<SpendRow>
        {
            Row("P3", "SCHOOL A", 100m, 500m, "EXEM", idx: 0),
            Row("P3", "SCHOOL B", 150m, 500m, "EXEM", idx: 1),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.RepeatedInvoiceTotal);
        Assert.Equal(2, result.ScheduleA.Count);
    }

    // ---- 3. standing monthly payments ----

    private static List<SpendRow> MonthlySeries(string supplier, decimal amount, params (int year, int month, int count)[] months)
    {
        var rows = new List<SpendRow>();
        int n = 0;
        foreach (var (y, m, c) in months)
            for (int i = 0; i < c; i++)
            {
                n++;
                rows.Add(Row("S" + n, supplier, amount, amount, payDate: $"05/{m:00}/{y}", idx: n, invoiceType: "RBC Standard Invoice"));
            }
        return rows;
    }

    [Fact]
    public void StandingPayment_TripledAfterTwoEmptyMonths_IsACatchUp()
    {
        // Jan-Dec 2023 once a month except Nov (2) and Dec/Jan missing, Feb 2024 = 3: payments equal months.
        var rows = MonthlySeries("BFFC", 777991m,
            (2023, 1, 1), (2023, 2, 1), (2023, 3, 1), (2023, 4, 1), (2023, 5, 1), (2023, 6, 1),
            (2023, 7, 1), (2023, 8, 1), (2023, 9, 1), (2023, 10, 1),
            (2024, 1, 0), (2024, 2, 3));
        // Nov and Dec 2023 are absent: Feb carries Nov, Dec/Jan catch-up (3 for the 3 empty months Nov, Dec, Jan... Feb itself is 1)
        // 10 payments Jan-Oct + 3 in Feb = 13 payments over Jan 2023 .. Feb 2024 = 14 months -> not a surplus.
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.StandingScheduleCatchUp, b.Classification);
        Assert.Contains("standing payment", b.ClassificationDetail);
    }

    [Fact]
    public void Reading_ThamesValleyPolice_NewRateStartsWithBackdatedInstalments_IsACatchUp()
    {
        // Real Reading pattern: the 2023-24 amount (1,504,952.70) runs Apr 2023 - Jan 2024; the 2024-25 amount
        // (1,564,574.70) first appears 4 Jul 2024 THREE times (April, May, June) plus 17 Jul, then monthly to Jan 2025.
        var rows = new List<SpendRow>();
        int n = 0;
        foreach (var (y, m) in new[] { (2023, 4), (2023, 5), (2023, 6), (2023, 7), (2023, 8), (2023, 9), (2023, 10), (2023, 11), (2023, 12), (2024, 1) })
            rows.Add(Row("O" + ++n, "TVP", 1504952.70m, 1504952.70m, payDate: $"17/{m:00}/{y}", idx: n, invoiceType: "STANDARD"));
        for (int i = 0; i < 3; i++)
            rows.Add(Row("N" + ++n, "TVP", 1564574.70m, 1564574.70m, payDate: "04/07/2024", idx: n, invoiceType: "STANDARD"));
        rows.Add(Row("N" + ++n, "TVP", 1564574.70m, 1564574.70m, payDate: "17/07/2024", idx: n, invoiceType: "STANDARD"));
        foreach (var (y, m) in new[] { (2024, 8), (2024, 9), (2024, 10), (2024, 11), (2024, 12), (2025, 1) })
            rows.Add(Row("N" + ++n, "TVP", 1564574.70m, 1564574.70m, payDate: $"17/{m:00}/{y}", idx: n, invoiceType: "STANDARD"));
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = result.ScheduleB.Single(g => g.Net == 1564574.70m);
        Assert.Equal(3, b.Members.Count);
        Assert.Equal(ScheduleBClassification.StandingScheduleCatchUp, b.Classification);
        Assert.Contains("empty months since this supplier's previous amount ended", b.ClassificationDetail);
    }

    [Fact]
    public void StandingPayment_TripledWithNoGapAndNothingReturned_IsASurplus()
    {
        var rows = MonthlySeries("BFFC", 777991m,
            (2023, 1, 1), (2023, 2, 1), (2023, 3, 1), (2023, 4, 1), (2023, 5, 1), (2023, 6, 1),
            (2023, 7, 1), (2023, 8, 1), (2023, 9, 1), (2023, 10, 1), (2023, 11, 1), (2023, 12, 1),
            (2024, 1, 3));
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.StandingScheduleSurplus, b.Classification);
        Assert.Contains("surplus of 2", b.ClassificationDetail);
    }

    [Fact]
    public void StandingPayment_SurplusLaterRefundedByTheSameAmount_IsACatchUp()
    {
        var rows = MonthlySeries("BFFC", 777991m,
            (2023, 1, 1), (2023, 2, 1), (2023, 3, 1), (2023, 4, 1), (2023, 5, 1), (2023, 6, 1),
            (2023, 7, 1), (2023, 8, 1), (2023, 9, 1), (2023, 10, 1), (2023, 11, 1), (2023, 12, 1),
            (2024, 1, 2));
        // The second January payment is repaid in February under the council's own refund Invoice Type.
        rows.Add(Row("R1", "BFFC", 777991m, 777991m, payDate: "20/02/2024", idx: 99, invoiceType: "RBC Refunds Manual Entry"));
        rows.Add(Row("X1", "BFFC", 777991m, 777991m, payDate: "21/02/2024", idx: 100, invoiceType: "RBC Standard Invoice"));
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = result.ScheduleB.Single(g => g.Members[0].PayDateRaw == "05/01/2024");
        Assert.Equal(ScheduleBClassification.StandingScheduleCatchUp, b.Classification);
    }

    // ---- 3b. spreadsheet-mangled transaction ids (Merton 2.02504E+11 shared by 59 unrelated rows) ----

    [Fact]
    public void LossyTransactionIds_AreNotGroupedTogether_AndTheirRepeatsAreStillFound()
    {
        var rows = new List<SpendRow>
        {
            Row("2.02504E+11", "SUPPLIER A", 700m, 700m, payDate: "01/05/2025", idx: 1),
            Row("2.02504E+11", "SUPPLIER A", 700m, 700m, payDate: "01/05/2025", idx: 2),
            Row("2.02504E+11", "SUPPLIER B", 9m, 9m, payDate: "01/05/2025", idx: 3),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.SupplierFurnished, GrossMeaning.PerLineAmount);
        Assert.Contains(result.Warnings, w => w.Contains("scientific notation"));
        var b = Assert.Single(result.ScheduleB);          // the two identical SUPPLIER A payments are now two transactions
        Assert.Equal(2, b.Members.Count);
        Assert.All(b.Members, m => Assert.Contains("~row", m.TransactionId));
    }

    [Theory]
    [InlineData("2.02408E+11", true)]
    [InlineData("2.50001E+13", true)]
    [InlineData("1E+11", true)]
    [InlineData("30455071", false)]
    [InlineData("INV-4941", false)]
    [InlineData("J0002270", false)]
    public void LossyTransactionId_Detection(string id, bool expected) =>
        Assert.Equal(expected, AuditEngine.IsLossyTransactionId(id));

    // ---- 4. standing payment recorded under the refund Invoice Type (Reading -> Brighter Futures, 2024-03 on) ----

    [Fact]
    public void RefundTypedAmount_RecurringMonthly_IsAStandingPayment_NotAnUnmatchedRefund()
    {
        var rows = new List<SpendRow>();
        for (int m = 5; m <= 10; m++)
            rows.Add(Row("R" + m, "BFFC", 4343592m, 4343592m, payDate: $"06/{m:00}/2024", idx: m, invoiceType: "RBC Refunds Manual Entry"));
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Equal(6, result.ScheduleR.Count);
        Assert.All(result.ScheduleR, r => Assert.Equal(CreditMatchClassification.StandingPaymentUnderRefundType, r.Classification));
    }

    // ---- 5 (Session 27). a credit settled inside its own payment (Reading Payment Number) ----

    private static SpendRow PayRow(string trans, decimal gross, string payNo, int idx, string? invoiceType = null) =>
        Row(trans, "ALLPAY NET LTD", gross, gross, payDate: "14/10/2020", idx: idx, invoiceType: invoiceType) with
        { OtherColumns = "Purchasing Organisation=RBC Legal Entity|Payment Number=" + payNo + "|Service Area=LD" };

    [Fact]
    public void Credit_InAPaymentWhosePositiveLinesCoverIt_IsNettedInSamePayment()
    {
        // Real shape (Reading, ALLPAY NET LTD, 2020-Q3): Purchased Care credits sit in the payment that also carries the
        // invoices; here a 1,240.74 credit in a payment with 3,000 of charges (different amounts, so no 1:1 match).
        var rows = new List<SpendRow>
        {
            PayRow("V1", 3000m, "226975", 0), PayRow("V2", -1240.74m, "226975", 1, "CREDIT"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.SupplierFurnished, GrossMeaning.PerLineAmount);
        var r = Assert.Single(result.ScheduleR);
        Assert.Equal(CreditMatchClassification.NettedInSamePayment, r.Classification);
    }

    [Fact]
    public void Credit_LargerThanItsPayment_OrWithoutAPaymentNumber_StaysUnmatched()
    {
        var rows = new List<SpendRow>
        {
            PayRow("V1", 500m, "1", 0), PayRow("V2", -1240.74m, "1", 1, "CREDIT"),   // payment nets negative
            PayRow("V3", -75.10m, "2", 2, "CREDIT"),                                // payment is all credit
            Row("V4", "ALLPAY NET LTD", -33.33m, -33.33m, payDate: "14/10/2020", idx: 3, invoiceType: "CREDIT"), // no payment number
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.SupplierFurnished, GrossMeaning.PerLineAmount);
        Assert.Equal(3, result.ScheduleR.Count);
        Assert.All(result.ScheduleR, r => Assert.Equal(CreditMatchClassification.Unmatched, r.Classification));
    }

    // ---- 6 (Session 27). refund-typed payment in a file with no negative line at all (Reading from 2024-03) ----

    [Fact]
    public void PositiveRefundTyped_InAFileWithNoNegativeLines_IsARefundChannelPayment_UnlessLarge()
    {
        // Real shapes (Reading 2025-11 / 2024-10): Sundry Supplier 3,657.55 on the Balance Sheet service area, a planning
        // fee refund of 520.20, and a 25,000 grant-like payment; plus one of 4,343,592 (Brighter Futures) which stays a lead.
        var rows = new List<SpendRow>
        {
            Row("A1", "SUNDRY SUPPLIER", 3657.55m, 3657.55m, payDate: "28/11/2025", idx: 0, invoiceType: "RBC Refunds Manual Entry"),
            Row("A2", "PORTALPLANQUEST", 520.20m, 520.20m, payDate: "18/10/2025", idx: 1, invoiceType: "RBC Refunds Manual Entry"),
            Row("A3", "BFFC", 4343592m, 4343592m, payDate: "01/10/2025", idx: 2, invoiceType: "RBC Refunds Manual Entry"),
            Row("A4", "KEYLINE", 543.80m, 543.80m, payDate: "01/10/2025", idx: 3, invoiceType: "RBC Standard Invoice"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Equal(CreditMatchClassification.RefundChannelPayment, result.ScheduleR.Single(r => r.TransactionId == "A1").Classification);
        Assert.Equal(CreditMatchClassification.RefundChannelPayment, result.ScheduleR.Single(r => r.TransactionId == "A2").Classification);
        Assert.Equal(CreditMatchClassification.Unmatched, result.ScheduleR.Single(r => r.TransactionId == "A3").Classification);
    }

    [Fact]
    public void PositiveRefundTyped_InAFileThatDoesHaveNegativeLines_StaysUnmatched()
    {
        var rows = new List<SpendRow>
        {
            Row("A1", "SUNDRY SUPPLIER", 3657.55m, 3657.55m, payDate: "28/11/2025", idx: 0, invoiceType: "RBC Refunds Manual Entry"),
            Row("A5", "SOMEONE", -12m, -12m, payDate: "28/11/2025", idx: 1, invoiceType: "CREDIT"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.Equal(CreditMatchClassification.Unmatched, result.ScheduleR.Single(r => r.TransactionId == "A1").Classification);
    }

    [Fact]
    public void NegativeCreditNote_RecurringMonthly_StaysAsOrdinaryMatching()
    {
        var rows = new List<SpendRow>();
        for (int m = 5; m <= 10; m++)
            rows.Add(Row("C" + m, "PAYCOLL", -312.56m, -312.56m, payDate: $"06/{m:00}/2022", idx: m, invoiceType: "CREDIT"));
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        Assert.All(result.ScheduleR, r => Assert.Equal(CreditMatchClassification.Unmatched, r.Classification));
    }

    [Fact]
    public void RefundTypedAmount_OneOff_StaysUnmatched()
    {
        var rows = new List<SpendRow>
        {
            Row("R1", "BFFC", 20888456m, 20888456m, payDate: "04/12/2024", invoiceType: "RBC Refunds Manual Entry"),
            Row("R2", "BFFC", 4343592m, 4343592m, payDate: "06/05/2024", idx: 1, invoiceType: "RBC Refunds Manual Entry"),
            Row("R3", "BFFC", 4343592m, 4343592m, payDate: "06/06/2024", idx: 2, invoiceType: "RBC Refunds Manual Entry"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var big = result.ScheduleR.Single(r => r.Amount == 20888456m);
        Assert.Equal(CreditMatchClassification.Unmatched, big.Classification);
        // Two months is not a series: stays Unmatched too.
        Assert.All(result.ScheduleR.Where(r => r.Amount == 4343592m), r => Assert.Equal(CreditMatchClassification.Unmatched, r.Classification));
    }

    [Fact]
    public void RepeatedPayment_WithoutAStandingSeries_StaysUnclear()
    {
        var rows = new List<SpendRow>
        {
            Row("T20", "SOME CARE LTD", 500m, 500m, payDate: "05/12/2021"),
            Row("T21", "SOME CARE LTD", 500m, 500m, payDate: "05/12/2021"),
            Row("T22", "SOME CARE LTD", 500m, 500m, payDate: "05/01/2022"),
        };
        var result = AuditEngine.Run(rows, TransactionIdScope.CouncilWideUnique, GrossMeaning.PerLineAmount);
        var b = Assert.Single(result.ScheduleB);
        Assert.Equal(ScheduleBClassification.Unclear, b.Classification);
    }
}
