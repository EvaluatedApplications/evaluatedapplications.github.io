using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 51: "N of M (P%)" statements in profile text must be arithmetically right (profileaudit present-but-wrong check).</summary>
public class Session51Tests
{
    private static readonly System.Text.RegularExpressions.Regex OfPct = new(@"(\d{1,3}(?:,\d{3})*|\d+) of (\d{1,3}(?:,\d{3})*|\d+)[^.;()]{0,40}\((?:about |~)?(\d+(?:\.\d+)?)%\)");

    [Fact]
    public void KirkleesRedactionPercentageIsRoundedRight()
    {
        var k = SupportedCouncils.Everyone.Single(x => x.Name.Contains("Kirklees"));
        var t = string.Join("\n", new[] { k.HowToFindTheFile, k.VerificationNote }.Concat(k.KnownQuirks));
        Assert.Contains("177,385 of 516,440 rows (34.3%)", t);
        Assert.DoesNotContain("(34.4%)", t);
        Assert.Equal(34.3, Math.Round(100.0 * 177_385 / 516_440, 1));
    }

    [Fact]
    public void EveryOfPercentStatementInEveryProfileIsArithmeticallyRight()
    {
        int seen = 0;
        foreach (var c in SupportedCouncils.Everyone)
        {
            var t = string.Join("\n", new[] { c.HowToFindTheFile, c.VerificationNote }.Concat(c.KnownQuirks));
            foreach (System.Text.RegularExpressions.Match m in OfPct.Matches(t))
            {
                double a = double.Parse(m.Groups[1].Value.Replace(",", "")), b = double.Parse(m.Groups[2].Value.Replace(",", "")), p = double.Parse(m.Groups[3].Value);
                int dp = m.Groups[3].Value.Contains('.') ? m.Groups[3].Value.Length - m.Groups[3].Value.IndexOf('.') - 1 : 0;
                Assert.True(a <= b, c.Name + ": " + m.Value);
                Assert.True(Math.Abs(100.0 * a / b - p) <= 0.5 * Math.Pow(10, -dp) + 1e-9, c.Name + ": " + m.Value);
                seen++;
            }
        }
        Assert.True(seen > 5, "seen " + seen);
    }
}


