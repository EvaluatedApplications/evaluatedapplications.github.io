using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

public class CorrectionPairsTests
{
    [Fact]
    public void RbwmHmrcCorrection_IsASingleDigitDifference_WithPlus50Residual()
    {
        // RBWM 2026-03 transaction 9997557, four published lines.
        var lines = new[] { -1491449.25m, 1491449.25m, -1491449.25m, 1491499.25m };
        var hit = CorrectionPairs.Find(lines);
        Assert.NotNull(hit);
        Assert.Equal(50.00m, hit!.Value.residual);
        Assert.Equal(3, hit.Value.slip.PositionFromRight); // tens of pounds
        Assert.Equal('4', hit.Value.slip.From);
        Assert.Equal('9', hit.Value.slip.To);
        Assert.Equal(50.00m, Math.Abs(hit.Value.slip.Difference));
    }

    [Fact]
    public void DifferentLengthsOrTwoDigits_AreNotASlip()
    {
        Assert.Null(CorrectionPairs.SingleDigitSlip(1000.00m, 10000.00m));
        Assert.Null(CorrectionPairs.SingleDigitSlip(1234.56m, 1294.57m));
        Assert.Null(CorrectionPairs.SingleDigitSlip(500m, 500m));
    }

    [Fact]
    public void SameSignLines_AreNotAReversalPair() =>
        Assert.Null(CorrectionPairs.Find(new[] { 1491449.25m, 1491499.25m }));
}
