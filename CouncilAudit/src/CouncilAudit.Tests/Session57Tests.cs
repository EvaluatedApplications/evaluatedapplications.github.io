using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>Session 57: no individual is named in public prose (checklist C20).</summary>
public class Session57Tests
{
    private static string Text(SupportedCouncil c) => string.Join("\n", new[] { c.HowToFindTheFile, c.VerificationNote }.Concat(c.KnownQuirks));

    [Theory]
    [InlineData("both to Alex & Sam Fictional, GBP 682.98")]
    [InlineData("Alex Fictional Electrical (3,314 rows)")]
    [InlineData("paid to Mrs Smith on 3 May")]
    [InlineData("J Smith Plumbing, 40 rows")]
    public void PlantedNamesAreFound(string text) => Assert.NotEmpty(IndividualNames.Find(text, new HashSet<string>()));

    [Theory]
    [InlineData("two private landlords, GBP 682.98")]
    [InlineData("a sole-trader electrician (3,314 rows)")]
    [InlineData("Dennis Eagle Ltd, four of GBP 228,920")]
    [InlineData("Reed Specialist Recruitment and Managed Water Services")]
    public void PatternDescriptionsAreClean(string text) => Assert.Empty(IndividualNames.Find(text, new HashSet<string>()));

    [Fact]
    public void AWithheldNameIsFoundByHash()
    {
        var wh = new HashSet<string> { RowRedaction.NameHash("Zebediah Plantwell") };
        Assert.NotEmpty(IndividualNames.Find("the payee Zebediah Plantwell was paid", wh));
        Assert.Empty(IndividualNames.Find("the payee Zebediah Plantwell was paid", new HashSet<string>()));
    }

    [Fact]
    public void NoProfileNamesAnIndividual()
    {
        foreach (var c in SupportedCouncils.Everyone) Assert.Empty(IndividualNames.Find(Text(c), new HashSet<string>()));
    }
}
