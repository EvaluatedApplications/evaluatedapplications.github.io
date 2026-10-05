using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 24: pins the loan-interest decoding as a ledger column (real published figures).</summary>
public class LoanDecoderTests
{
    [Fact]
    public void RoundPrincipal_WithStandardTermInterest_Decodes()
    {
        // 5m at 1.65% x 365d = 82,500.00 exactly (also 3.30% x 182.5 is not whole days; 1.65%/365d is the unique quoted fit)
        decimal interest = LoanInterest.Interest(5000000m, 165, 365);
        var d = LoanDecoder.Decode(5000000m + interest, interest);
        Assert.Equal(DecodeStatus.Decodes, d.Status);
        Assert.Equal(5000000m, d.Principal);
        Assert.True(d.Candidates >= 1);
    }

    [Fact]
    public void TruncatedPenny_IsRecognisedAsTruncation()
    {
        // Wokingham -> Buckinghamshire, 26/03/2021: 5m, interest 1,232.87 (rounding the 15bp x 60d figure gives 1,232.88)
        var d = LoanDecoder.Decode(5001232.87m, 1232.87m);
        Assert.Equal(DecodeStatus.Decodes, d.Status);
        Assert.Contains("TRUNCATED", d.Note);
    }

    [Fact]
    public void NoFit_DoesNotDecode_AndSaysWhy()
    {
        // Wokingham -> Northamptonshire, 8m, interest 112,285.00: nothing reproduces it
        var d = LoanDecoder.Decode(8112285.00m, 112285.00m);
        Assert.Equal(DecodeStatus.DoesNotDecode, d.Status);
        Assert.Contains("no (rate, term) at all", d.Note);
    }

    [Fact]
    public void NonPositive_IsNotApplicable() =>
        Assert.Equal(DecodeStatus.NotApplicable, LoanDecoder.Decode(-100m, -100m).Status);

    [Theory]
    [InlineData("Wokingham Borough Council", "Public Works Loans Account", CounterpartyClass.Pwlb)]
    [InlineData("Wokingham Borough Council", "Wandsworth Borough Council", CounterpartyClass.InterAuthority)]
    [InlineData("Wokingham Borough Council", "Tradition (UK) Ltd", CounterpartyClass.Broker)]
    [InlineData("Wokingham Borough Council", "Barclays Bank PLC", CounterpartyClass.Bank)]
    [InlineData("Wokingham Borough Council", "WBC Loddon Homes Ltd", CounterpartyClass.OwnedCompany)]
    [InlineData("Bracknell Forest Council", "REDACT PERSONAL INFORMATION", CounterpartyClass.Redacted)]
    public void Classify_Counterparties(string council, string cp, CounterpartyClass expected) =>
        Assert.Equal(expected, LoanDecoder.Classify(council, cp));

    [Fact]
    public void Re3WastePfi_IsNotALoan() =>
        Assert.Equal(DecodeStatus.NotApplicable,
            LoanDecoder.ForEntry("Wokingham Borough Council", "Reading Borough Council", 728868.66m, 28868.66m).Status);
}
