using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Pins <see cref="ConfidenceInterval.Wilson95"/> against known textbook values so a future
/// change to the formula can't silently drift. 10/10 and 0/10 are the two shapes this
/// project's own hand-check cycles hit most often (every opened case confirmed, or a trap
/// case correctly found to have zero real matches).
/// </summary>
public class ConfidenceIntervalTests
{
    [Fact]
    public void TenOfTen_KnownWilsonInterval_IsApproximately72to100Percent()
    {
        var (lower, upper) = ConfidenceInterval.Wilson95(10, 10);
        Assert.True(Math.Abs(lower - 0.7226) < 0.01, $"lower was {lower}");
        Assert.Equal(1.0, upper, 3);
    }

    [Fact]
    public void ZeroOfTen_KnownWilsonInterval_IsApproximately0to28Percent()
    {
        var (lower, upper) = ConfidenceInterval.Wilson95(0, 10);
        Assert.Equal(0.0, lower, 3);
        Assert.True(Math.Abs(upper - 0.2776) < 0.01, $"upper was {upper}");
    }

    [Fact]
    public void FortyOfForty_WiderSampleStillConfirmed_NarrowerThanTenOfTen()
    {
        // A bigger fully-confirmed sample should narrow the interval, not just repeat it -
        // this is the actual honesty check: a 25/25 "sample" (what earlier sessions printed
        // with no interval at all) looks identical to a 3/3 sample as a bare percentage, but
        // the two have very different Wilson lower bounds.
        var (lower10, _) = ConfidenceInterval.Wilson95(10, 10);
        var (lower40, _) = ConfidenceInterval.Wilson95(40, 40);
        Assert.True(lower40 > lower10, $"lower40={lower40} should exceed lower10={lower10}");
    }

    [Fact]
    public void ZeroTotal_ReturnsFullyUninformativeInterval()
    {
        var (lower, upper) = ConfidenceInterval.Wilson95(0, 0);
        Assert.Equal(0.0, lower);
        Assert.Equal(1.0, upper);
    }

    [Fact]
    public void SuccessesGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.Wilson95(5, 3));
    }
}
