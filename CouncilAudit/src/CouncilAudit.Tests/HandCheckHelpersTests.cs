using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 19: unit tests for HandCheckHelpers, which retires ConfirmedInRawDetailed (the
/// double-counting failure mode confirmed 3 times - Schedule D's trans 4378111 Session 18,
/// the Reading crossref hand-open Session 18, and the theoretical Merton gap BASELINE.md
/// Section 7 named). Every test builds the smallest synthetic raw table that exercises the
/// real shape, not a real council file, so these run without any file I/O - same discipline
/// as ReadingFixupsTests.cs.
/// </summary>
public class HandCheckHelpersTests
{
    private static readonly ColumnMapping Map = new(
        TransactionId: "TransId", Supplier: "Supplier", PayDate: null, Description: "Description",
        ServiceArea: null, Net: "Net", Gross: "Gross", VatAmount: null, VatType: null);

    private static string[] Row(string transId, string supplier, string net, string gross, string desc) =>
        new[] { transId, supplier, net, gross, desc };

    private static IReadOnlyList<string[]> Table(params string[][] dataRows)
    {
        var table = new List<string[]> { new[] { "TransId", "Supplier", "Net", "Gross", "Description" } };
        table.AddRange(dataRows);
        return table;
    }

    // -----------------------------------------------------------------------------
    // ConfirmedScheduleAExactLines
    // -----------------------------------------------------------------------------

    [Fact]
    public void ScheduleA_SingleLine_ExactMatch_Confirmed()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleA_DoubleListing_RepeatedInvoiceTotal_ComparesFirstRowGrossNotSum()
    {
        // Two identical lines, gross repeated on both (Wokingham's own convention) - the
        // stated Gross is the ONE invoice total, not double it.
        var table = Table(
            Row("T1", "Acme Ltd", "50.00", "50.00", "Fees"),
            Row("T1", "Acme Ltd", "50.00", "50.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 50.00m, 50.00m, 0m,
            null, null, "Fees", 2, ScheduleAClassification.DoubleListing, null, new[] { 0, 1 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleA_PerLineAmount_SumsGrossAcrossExactLines()
    {
        // Merton/Reading's convention: each line carries its OWN gross; the stated total
        // is the sum of every line's own gross, not one repeated value.
        var table = Table(
            Row("T1", "Acme Ltd", "30.00", "30.00", "Part A"),
            Row("T1", "Acme Ltd", "70.00", "70.00", "Part A"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Part A", 2, ScheduleAClassification.Unreconciled, null, new[] { 0, 1 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: false);

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleA_DoesNotDoubleCount_DecoyRowsSharingSameTransactionAndSupplierTextAreIgnored()
    {
        // The exact regression this file exists to prevent: an UNRELATED raw line shares
        // the same (TransactionId, Supplier) text as the real group (e.g. a different
        // transaction that happens to reuse a voucher number for a different supplier
        // relationship, or - the retired function's actual failure - a genuinely separate
        // same-numbered line that should NOT be summed into this group). Only the rows at
        // the group's own MemberRowIndexes may contribute to the sum; a decoy row at a
        // DIFFERENT index, even with identical TransactionId/Supplier text, must not be
        // picked up.
        var table = Table(
            Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"),      // row 0: the real member
            Row("T1", "Acme Ltd", "999.00", "999.00", "Decoy"));    // row 1: decoy, same key, NOT a member
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.Null(result); // confirmed using ONLY row 0 - the decoy at row 1 never entered the sum.
    }

    [Fact]
    public void ScheduleA_NetMismatch_ReportedDistinctlyFromGrossMismatch()
    {
        var table = Table(Row("T1", "Acme Ltd", "999.00", "100.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("net mismatch", result);
    }

    [Fact]
    public void ScheduleA_GrossMismatch_ReportedDistinctlyFromNetMismatch()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "999.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("gross mismatch", result);
    }

    [Fact]
    public void ScheduleA_DescriptionMismatch_ReportedWhenAmountsMatch()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Something else"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("description mismatch", result);
    }

    [Fact]
    public void ScheduleA_RowIndexOutOfRange_ReportedExplicitly()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 5 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("out of range", result);
    }

    [Fact]
    public void ScheduleA_EmptyMemberRowIndexes_ReportedExplicitly_NotSilentlyPassed()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1); // no MemberRowIndexes passed -> defaults to empty

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("no member row indexes", result);
    }

    [Fact]
    public void ScheduleA_WrongSupplierAtRecordedIndex_ReportedAsMismatchNotSilentMatch()
    {
        // Defends against a future bug where MemberRowIndexes drifts out of sync with the
        // SpendRow it was built from (e.g. a re-sort between MapRows and Run) - the exact
        // row at the recorded position must still say what the classified row claims.
        var table = Table(Row("T1", "Someone Else Ltd", "100.00", "100.00", "Fees"));
        var a = new ScheduleARow("Y1", "T1", "Acme Ltd", null, 100.00m, 100.00m, 100.00m, 0m,
            null, null, "Fees", 1, ScheduleAClassification.Unreconciled, null, new[] { 0 });

        var result = HandCheckHelpers.ConfirmedScheduleAExactLines(table, Map, a, repeatedInvoiceTotal: true);

        Assert.NotNull(result);
        Assert.Contains("supplier mismatch", result);
    }

    // -----------------------------------------------------------------------------
    // ConfirmedScheduleBMember
    // -----------------------------------------------------------------------------

    [Fact]
    public void ScheduleB_SingleLineMember_ExactMatch_Confirmed()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"));
        var member = new SpendRow("Y1", "T1", "Acme Ltd", null, null, "Fees", null,
            100.00m, 100.00m, null, null, RowIndexInSource: 0);

        var result = HandCheckHelpers.ConfirmedScheduleBMember(table, Map, member, new[] { 0 });

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleB_MultiLineTransaction_SumsNetButComparesGrossAgainstFirstLineOnly()
    {
        // A Schedule B member can represent a multi-line transaction (transAgg sums Net
        // across every line) whose Gross is always the FIRST published line's own value,
        // never a sum - regardless of GrossMeaning, per AuditEngine.Run's own convention.
        var table = Table(
            Row("T1", "Acme Ltd", "40.00", "100.00", "Fees"),   // first line: Gross is the reference value
            Row("T1", "Acme Ltd", "60.00", "100.00", "Fees"));  // second line: same Gross published again
        var member = new SpendRow("Y1", "T1", "Acme Ltd", null, null, "Fees", null,
            Net: 100.00m, Gross: 100.00m, null, null, RowIndexInSource: 0);

        var result = HandCheckHelpers.ConfirmedScheduleBMember(table, Map, member, new[] { 0, 1 });

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleB_DoesNotDoubleCount_DecoyRowSharingSameKeyIsIgnored()
    {
        var table = Table(
            Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"),      // row 0: the real member
            Row("T1", "Acme Ltd", "999.00", "999.00", "Decoy"));    // row 1: decoy, same key, NOT part of this member
        var member = new SpendRow("Y1", "T1", "Acme Ltd", null, null, "Fees", null,
            100.00m, 100.00m, null, null, RowIndexInSource: 0);

        var result = HandCheckHelpers.ConfirmedScheduleBMember(table, Map, member, new[] { 0 });

        Assert.Null(result);
    }

    [Fact]
    public void ScheduleB_NetMismatch_ReportedWhenSumAcrossExactLinesDisagrees()
    {
        var table = Table(
            Row("T1", "Acme Ltd", "40.00", "100.00", "Fees"),
            Row("T1", "Acme Ltd", "40.00", "100.00", "Fees")); // raw sum 80, not 100
        var member = new SpendRow("Y1", "T1", "Acme Ltd", null, null, "Fees", null,
            100.00m, 100.00m, null, null, RowIndexInSource: 0);

        var result = HandCheckHelpers.ConfirmedScheduleBMember(table, Map, member, new[] { 0, 1 });

        Assert.NotNull(result);
        Assert.Contains("net mismatch", result);
    }

    [Fact]
    public void ScheduleB_EmptyRowIndexes_ReportedExplicitly()
    {
        var table = Table(Row("T1", "Acme Ltd", "100.00", "100.00", "Fees"));
        var member = new SpendRow("Y1", "T1", "Acme Ltd", null, null, "Fees", null,
            100.00m, 100.00m, null, null, RowIndexInSource: 0);

        var result = HandCheckHelpers.ConfirmedScheduleBMember(table, Map, member, Array.Empty<int>());

        Assert.NotNull(result);
        Assert.Contains("no member row indexes", result);
    }
}
