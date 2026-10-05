using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 22: unit tests for West Berkshire's two real fixups (council #5 onboarding) -
/// the leading-blank-header-row skip (3 of 29 real files had this) and the synthetic
/// per-row TransactionId (this council publishes no reference column at all). Each test
/// uses the smallest synthetic table that exercises the real shape, not a real file.
/// </summary>
public class WestBerkshireFixupsTests
{
    [Fact]
    public void SkipLeadingBlankHeaderRow_OneBlankRow_RemovesItAndKeepsRealHeaderFirst()
    {
        var table = new List<string[]>
        {
            new[] { "", "", "", "", "", "" },
            new[] { "Service", "Expenditure category", "Narrative", "Date", "Net amount", "Supplier name" },
            new[] { "Adult Social Care", "Private Contractors", "Care", "45400", "1000.00", "Acme Care Ltd" },
        };

        var result = WestBerkshireFixups.SkipLeadingBlankHeaderRow(table);

        Assert.Equal(2, result.Count);
        Assert.Equal("Service", result[0][0]);
        Assert.Equal("Acme Care Ltd", result[1][5]);
    }

    [Fact]
    public void SkipLeadingBlankHeaderRow_NoBlankRow_IsNoOp()
    {
        var table = new List<string[]>
        {
            new[] { "Service", "Expenditure category", "Narrative", "Date", "Net amount", "Supplier name" },
            new[] { "Adult Social Care", "Private Contractors", "Care", "45400", "1000.00", "Acme Care Ltd" },
        };

        var result = WestBerkshireFixups.SkipLeadingBlankHeaderRow(table);

        Assert.Equal(2, result.Count);
        Assert.Same(table[0], result[0]); // genuinely unchanged, not a copy that happens to match
    }

    [Fact]
    public void SkipLeadingBlankHeaderRow_EmptyTable_ReturnsEmpty()
    {
        var result = WestBerkshireFixups.SkipLeadingBlankHeaderRow(new List<string[]>());
        Assert.Empty(result);
    }

    [Fact]
    public void AddSyntheticRowId_AppendsDistinctRowRefPerDataRow()
    {
        var table = new List<string[]>
        {
            new[] { "Service", "Supplier name" },
            new[] { "Adult Social Care", "Acme Care Ltd" },
            new[] { "Adult Social Care", "Acme Care Ltd" }, // same supplier/service, must still get a distinct ref
        };

        var result = WestBerkshireFixups.AddSyntheticRowId(table);

        Assert.Equal("RowRef", result[0][2]);
        Assert.NotEqual(result[1][2], result[2][2]); // the whole point: no two rows ever collide
    }

    [Fact]
    public void AddSyntheticRowId_EmptyTable_ReturnsEmpty()
    {
        var result = WestBerkshireFixups.AddSyntheticRowId(new List<string[]>());
        Assert.Empty(result);
    }
}
