using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 23: pins the three structural facts found on the real RBWM files and the republication
/// de-duplication (the June 2025 file holding June 2024 payments).</summary>
public class RbwmFixupsTests
{
    private static string[] Header => new[] { "Organisation Name", "Organisation code", "Effective Date", "Directorate",
        "Service Category Label", "Service", "Supplier (Beneficiary name)", "Local Supplier(beneficiary) internal reference ",
        "Payment Date", "Transaction number", "Net Amount", "Invoice Date", "Irrecoverable VAT", "Purpose of spend", "Procurement Classification" };

    [Fact]
    public void SkipToHeader_DropsTitleAndBlankRows_AndIsANoOpWhenHeaderIsFirst()
    {
        var withPreamble = new List<string[]>
        {
            new[] { "", "", "", "Supplier Payments where charge to specific cost centre is >= 100 for June 2025" },
            new[] { "", "", "" },
            Header,
            new[] { "Windsor and Maidenhead" },
        };
        var fixedTable = RbwmFixups.SkipToHeader(withPreamble);
        Assert.Equal(2, fixedTable.Count);
        Assert.Equal("Organisation Name", fixedTable[0][0]);
        var already = new List<string[]> { Header };
        Assert.Same(already, RbwmFixups.SkipToHeader(already));
    }

    [Fact]
    public void HeaderFixups_MapTheAug2025Names_BackToTheMajorityNames()
    {
        var t = new List<string[]> { new[] { "Organisation Name", "Directorate(T)", "Sercop", "Sercop(T)", "Supplier", "Local supplier(internal ID)", "Purpose of spend(T)" } };
        RbwmFixups.ApplyHeaderFixups(t);
        Assert.Equal(new[] { "Organisation Name", "Directorate", "Service Category Label", "Service", "Supplier (Beneficiary name)",
            "Local Supplier(beneficiary) internal reference", "Purpose of spend" }, t[0]);
    }

    [Fact]
    public void AlignIrrecoverableVat_InsertsTheMissingCell_WhenRowsLackIt()
    {
        // 2025-06 shape: the data row has no VAT cell, so "Software" sits under "Irrecoverable VAT".
        var shifted = new List<string[]>
        {
            Header,
            new[] { "W&M", "E0305", "30/06/2025", "Resources", "102", "Central Services", "8x8 UK Limited", "122239", "10/06/2024", "20258271", "3,815.94", "01/06/2024", "Software", "270000" },
            new[] { "W&M", "E0305", "30/06/2025", "Place", "110", "Planning", "A N Other", "114450", "25/06/2024", "20259492", "220", "21/06/2024", "Building Maintenance", "390000" },
        };
        var aligned = RbwmFixups.AlignIrrecoverableVat(shifted);
        Assert.Equal("", aligned[1][12]);
        Assert.Equal("Software", aligned[1][13]);
        Assert.Equal("270000", aligned[1][14]);
    }

    [Fact]
    public void AlignIrrecoverableVat_LeavesAlignedFilesAlone_WhetherNotApplicableTextOrBlank()
    {
        var na = new List<string[]> { Header, new[] { "W&M", "E0305", "30/04/2024", "Resources", "102", "Svc", "S", "1", "09/04/2024", "20253942", "3,792.90", "01/04/2024", "Not applicable", "Software", "270000" } };
        Assert.Same(na, RbwmFixups.AlignIrrecoverableVat(na));
        var blank = new List<string[]> { Header, new[] { "W&M", "E0305", "30/09/2025", "Resources", "102", "Svc", "S", "1", "30/09/2025", "9973747", "500", "01/09/2025", "", "Other Expenses", "" } };
        Assert.Same(blank, RbwmFixups.AlignIrrecoverableVat(blank));
    }

    private static SpendRow Row(string trans, string supplier, decimal net, string date, string desc) =>
        new("f", trans, supplier, date, null, desc, "Svc", net, net, null, null, 0);

    [Fact]
    public void DropAlreadyPublished_DropsRowsSeenInEarlierFiles_ButKeepsRepeatsInsideOneFile()
    {
        var seen = new HashSet<(string, string, decimal, string?, string?)>();
        var first = new[] { Row("1", "A", 10m, "10/06/2024", "Software") };
        var (kept1, dropped1) = RbwmFixups.DropAlreadyPublished(first, seen);
        Assert.Single(kept1); Assert.Equal(0, dropped1);

        // the "June 2025" file republishes that payment, and also holds two genuine identical lines of its own
        var second = new[]
        {
            Row("1", "A", 10m, "10/06/2024", "Software"),
            Row("2", "B", 5m, "10/06/2025", "Fees"),
            Row("2", "B", 5m, "10/06/2025", "Fees"),
        };
        var (kept2, dropped2) = RbwmFixups.DropAlreadyPublished(second, seen);
        Assert.Equal(1, dropped2);
        Assert.Equal(2, kept2.Count);
    }
}
