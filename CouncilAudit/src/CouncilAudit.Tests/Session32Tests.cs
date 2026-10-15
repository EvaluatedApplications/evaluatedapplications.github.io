using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 32. AliasScreen over real graded NEAREST proposals (export/supplier_alias_grades.csv). Every name below is a real
/// payee string with the human verdict it received.
/// </summary>
public class Session32Tests
{
    private static readonly string[] Leeds = { "LEEDS CITY COUNCIL" };
    private static readonly string[] Rbwm = { "ROYAL BOROUGH OF WINDSOR AND MAIDENHEAD", "RBWM", "WINDSOR AND MAIDENHEAD COUNCIL" };
    private static readonly string[] Bf = { "BRIGHTER FUTURES FOR CHILDREN" };

    [Theory]
    [InlineData("Leeds City Council")]
    [InlineData("LEEDS CITY COUNCIL")]
    public void Exact_Is_Contained(string name) =>
        Assert.Equal(AliasScreen.Tier.Contained, AliasScreen.Screen(Leeds, true, name).Tier);

    [Theory]
    [InlineData("Leic City Council")]            // NEAREST 0.474, rejected (Leicester)
    [InlineData("Hull City Council")]            // NEAREST 0.453, rejected
    public void OtherCouncil_Is_NoOverlap(string name) =>
        Assert.Equal(AliasScreen.Tier.NoOverlap, AliasScreen.Screen(Leeds, true, name).Tier);

    [Theory]
    [InlineData("Leeds City College")]           // rejected: a different body sharing two words
    [InlineData("Leeds City Fc")]
    public void SharedWordsButNoCouncilWord_Is_Not_Contained(string name) =>
        Assert.NotEqual(AliasScreen.Tier.Contained, AliasScreen.Screen(Leeds, true, name).Tier);

    [Theory]
    [InlineData("Leeds Council Lh")]             // probable: a person decides
    [InlineData("Leeds City Council Library & Information")]
    public void DepartmentLabels_Go_To_A_Person(string name) =>
        Assert.Equal(AliasScreen.Tier.Partial, AliasScreen.Screen(Leeds, true, name).Tier);

    [Fact]
    public void Typo_In_Source_Is_Tolerated() =>
        Assert.NotEqual(AliasScreen.Tier.NoOverlap, AliasScreen.Screen(Rbwm, true, "Royal Borough of Windsor and Maindenhead").Tier);

    [Fact]
    public void Ampersand_Spelling_Is_Contained() =>
        Assert.Equal(AliasScreen.Tier.Contained, AliasScreen.Screen(Rbwm, true, "ROYAL BOROUGH OF WINDSOR & MAIDENHEAD").Tier);

    [Fact]
    public void WordGluedToSuffix_Is_Not_NoOverlap() =>
        Assert.NotEqual(AliasScreen.Tier.NoOverlap, AliasScreen.Screen(Bf, false, "Brighter FuturesFC").Tier);

    [Fact]
    public void FuturesForChildren_Alone_Is_Not_Brighter_Futures_Contained() =>
        Assert.NotEqual(AliasScreen.Tier.Contained, AliasScreen.Screen(Bf, false, "FUTURES FOR CHILDREN LTD").Tier);
}
