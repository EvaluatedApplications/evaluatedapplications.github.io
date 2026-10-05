using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 23: pins the penny-exact Act/365 reading of inter-authority interest (every figure below is a
/// real published payment: Wokingham to Oxfordshire/Devon/Wandsworth, Bracknell Forest's redacted treasury lines)
/// and the new "no published column is dropped" (OtherColumns) and debt-evidence noise rules.</summary>
public class LoanInterestTests
{
    [Theory]
    [InlineData(5_000_000, 165, 365, 82500.00)]      // Oxfordshire, Wokingham trans 3669612 (11 Feb 2022)
    [InlineData(5_000_000, 165, 366, 82726.03)]      // Oxfordshire, trans 3592089 / 3597094 (leap-year anniversary)
    [InlineData(10_000_000, 70, 365, 70000.00)]      // Devon, trans 3673557: the "round" figure is exactly one year at 0.70%
    [InlineData(10_000_000, 70, 363, 69616.44)]      // Devon, trans 3772758 (2 Dec 2022)
    [InlineData(10_000_000, 55, 366, 55150.68)]      // Wandsworth, trans 3781131
    [InlineData(5_000_000, 470, 364, 234356.16)]     // Bracknell Forest, redacted 1 Oct 2025 and Blackburn 30 Oct 2025
    [InlineData(5_000_000, 475, 364, 236849.32)]     // Bracknell Forest, redacted 14 and 28 Oct 2025
    [InlineData(10_000_000, 485, 364, 483671.23)]    // Bracknell Forest, redacted 4 Nov 2025
    public void Interest_ReproducesPublishedFiguresToThePenny(int principal, int bp, int days, double expected) =>
        Assert.Equal((decimal)expected, LoanInterest.Interest(principal, bp, days));

    [Fact]
    public void Solve_FindsTheOneYearReading_AndOnlyExactPennyMatches()
    {
        var s = LoanInterest.Solve(5_000_000m, 82500.00m);
        Assert.Contains(new LoanTermRate(165, 365), s);
        Assert.All(s, c => Assert.Equal(82500.00m, LoanInterest.Interest(5_000_000m, c.RateBp, c.Days)));
        Assert.Contains(s, LoanInterest.IsPlausible);
    }

    [Fact]
    public void Solve_ReportsNoQuotedRateReading_ForAnUnroundedInterestFigure()
    {
        // 5,786.30 on 5m = 4224 bp-days: only 88x48, 96x44, 66x64 ... none a quoted rate (multiple of 5bp) at a short term
        Assert.DoesNotContain(LoanInterest.Solve(5_000_000m, 5786.30m), LoanInterest.IsPlausible);
    }

    [Fact]
    public void NullHitRate_IsSmall_SoAPennyExactMatchIsNotLuck()
    {
        var (any, std, plausible) = LoanInterest.NullHitRate(5_000_000m, draws: 2000, seed: 7);
        Assert.True(any < 0.02);
        Assert.True(plausible < 0.005);
        Assert.True(plausible <= std && std <= any);
    }

    [Theory]
    [InlineData(1, true)] [InlineData(14, true)] [InlineData(56, true)] [InlineData(91, true)]
    [InlineData(364, true)] [InlineData(365, true)] [InlineData(366, true)] [InlineData(363, true)] [InlineData(359, false)] [InlineData(200, false)]
    public void IsStandardTerm_AcceptsDealerTerms(int days, bool expected) =>
        Assert.Equal(expected, LoanInterest.IsStandardTerm(days));

    [Fact]
    public void OtherColumnsText_KeepsEveryUnmappedNonEmptyCell_AndSkipsMappedAndBlankHeaders()
    {
        var header = new[] { "Directorate", "Supplier", "Purpose of Expenditure", "Vendor Id", "Empty", "" };
        var row = new[] { "Adults", "Acme", "Older People Placements", "0019", "", "Application Service Provision" };
        var mapped = new HashSet<int> { 0, 1 };
        // a cell under a BLANK header is still published data (RBWM's classification names): kept as ColN
        Assert.Equal("Purpose of Expenditure=Older People Placements|Vendor Id=0019|Col6=Application Service Provision",
            AuditEngine.OtherColumnsText(header, row, mapped));
        Assert.Null(AuditEngine.OtherColumnsText(new[] { "A" }, new[] { "x" }, new HashSet<int> { 0 }));
    }

    [Fact]
    public void DebtEvidence_ReadsUnmappedColumnValues_ButNotHeadings()
    {
        var hidden = new SpendRow("FY", "1", "Lender", "01/01/2021", null, "Payment", "Area", 10m, 10m, null, null, 0, null, null, null,
            "Responsible Unit=Other Interest");
        Assert.True(DebtLedger.HasDebtEvidence(new[] { hidden }));
        var heading = hidden with { OtherColumns = "Service Division=Interest & Investment Income" };
        Assert.False(DebtLedger.HasDebtEvidence(new[] { heading }));
        var provision = hidden with { OtherColumns = "Purpose of Expenditure=Prov. for Bad & Doubtful Debts" };
        Assert.False(DebtLedger.HasDebtEvidence(new[] { provision }));
    }
}
