using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 50: the profile text of the original six councils, audited with the same path as the other 22 (the profileaudit command).
/// These tests pin the corrected wording: the stale figures found by that audit must not come back.
/// </summary>
public class Session50Tests
{
    private static string Text(SupportedCouncil c) => string.Join("\n", new[] { c.HowToFindTheFile, c.VerificationNote }.Concat(c.KnownQuirks));

    [Fact]
    public void ReadingProfileCarriesTheFullBackCatalogueCountsNotTheFiveMonthSample()
    {
        var t = Text(SupportedCouncils.Reading);
        Assert.DoesNotContain("found ZERO multi-payee", t);
        Assert.DoesNotContain("only one sample opened so far", t);
        // Session 52: the unnumbered payments are now published (audit E1)
        foreach (var f in new[] { "508,453", "1,227", "507,226", "28,925", "96,444", "2,621" }) Assert.Contains(f, t);
    }

    [Fact]
    public void RbwmMappedRowsAddUp()
    {
        var t = Text(SupportedCouncils.Rbwm);
        Assert.DoesNotContain("65,735", t);
        Assert.Contains("70,349", t); // Session 52: 28 unnumbered lines kept, 56 more left out as already published
        Assert.Equal(70_349 - 1_703, 64_101 + 4_545);   // mapped less dropped = retained plus redacted
        Assert.Equal(70_754, 70_349 + 405);             // raw records = mapped + non-payment lines
    }

    [Fact]
    public void BracknellScheduleDAndRedactionFiguresAgreeWithinTheProfile()
    {
        var t = Text(SupportedCouncils.BracknellForest);
        Assert.DoesNotContain("29-group", t);
        Assert.Contains("519 groups", t);
        Assert.Equal(161_336 - 32_074, 129_262);
        Assert.Contains("129,262", t);
    }

    [Fact]
    public void ReadingRedactionRateIsNoLongerQuotedAsTheSampleFigure()
    {
        foreach (var c in new[] { SupportedCouncils.BracknellForest, SupportedCouncils.WestBerkshire })
            Assert.DoesNotContain("0.03%", Text(c));
        Assert.Equal(0.22, Math.Round(100.0 * 1_118 / 505_832, 2));
    }

    [Fact]
    public void WokinghamAndMertonStateTheirCurrentCounts()
    {
        var w = Text(SupportedCouncils.Wokingham);
        foreach (var f in new[] { "212,951", "7,853", "12,177", "39,279", "420 transactions" }) Assert.Contains(f, w);
        var m = Text(SupportedCouncils.Merton);
        Assert.DoesNotContain("as first run", m);
        foreach (var f in new[] { "376,383", "84,621", "291,762", "15,844", "59,191", "6,502" }) Assert.Contains(f, m);
    }
}
