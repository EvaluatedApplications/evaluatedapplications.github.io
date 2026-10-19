namespace Doctrine.Tests;

/// <summary>
/// The economy switches (<see cref="ArmyRules"/>). The defaults are the game as it ships; every switch is a measurement tool of the second round of
/// Doctrine\todo\difficulty-2026-10-19.md and a mutant when turned off (SectionMutationProofs). Draft prices derived from worth ship OFF.
/// </summary>
public class EconomyRulesTests
{
    [Fact]
    public void The_defaults_are_the_game_age_and_aura_free_reach_and_interest_on()
    {
        var d = ArmyRules.Default;
        // Third round 2026-10-19 (coordinator): priced Age and Aura made a mass of one-gold Spearmen unbeatable, so they are OFF; the rules stay as switches.
        Assert.False(d.PriceAge); Assert.False(d.PriceAura); Assert.True(d.PriceReach); Assert.True(d.BankInterest);
        // and the draft prices are derived from worth (third round): a plain soldier costs 2, not the typed 1
        // (these asserts were swallowed by the comment through a literal backslash-n until the fifth round; they now run)
        Assert.True(d.DerivedDraftPrices); Assert.Equal(2, d.DraftPrice(0)); Assert.Equal(1, Roster.TypedPrice(0)); Assert.Equal(2, Roster.PriceOf(0));   // PriceOf is the game's price
    }

    [Fact]
    public void Derived_draft_prices_are_the_worth_of_the_kind_with_reach_and_a_plain_soldier_costs_two()
    {
        var on = new ArmyRules { DerivedDraftPrices = true };
        int[] expected = [2, 3, 2, 2, 2, 3, 3, 5];
        for (int k = 0; k < Roster.Size; k++)
        {
            Assert.Equal(expected[k], on.DraftPrice(k));
            Assert.Equal(Roster.TraitPrice(Roster.TraitsOf(k), true), on.DraftPrice(k));
        }
        Assert.Equal(Roster.TraitPrice(Roster.TraitsOf(7), false), (on with { PriceReach = false }).DraftPrice(7));   // 4 without reach
    }

    [Theory]
    [InlineData("stub")]
    [InlineData("pleroma")]
    public async Task An_army_charges_the_draft_price_its_rules_name(string which)
    {
        foreach (bool derived in new[] { false, true })
        {
            var rules = new ArmyRules { DerivedDraftPrices = derived };
            IArmyService svc = which == "stub" ? new StubArmyService(rules) : new PleromaArmyService(rules);
            for (int kind = 0; kind < Roster.Size; kind++)
            {
                var a = svc.Create(Side.Player, 5); await a.ApplySpoilsAsync(new Spoils(100, 0), default);
                int before = a.Read().Gold;
                var rep = await a.DraftAsync(new DraftRequest(0, 1, [new DraftPick(kind, 3)]), default);
                Assert.Equal(rules.DraftPrice(kind) * 3, rep.GoldSpent);
                Assert.Equal(before - rules.DraftPrice(kind) * 3, a.Read().Gold);
            }
        }
    }

    [Theory]
    [InlineData("stub")]
    [InlineData("pleroma")]
    public async Task The_switches_change_what_an_order_costs(string which)
    {
        // the same army, Age and Aura with their price switched off cost nothing; on, they cost; reach moves the price of a Flip to Machine
        IArmy Make(ArmyRules r)
        {
            IArmyService svc = which == "stub" ? new StubArmyService(r) : new PleromaArmyService(r);
            var a = svc.Create(Side.Player, 5); a.ApplySpoilsAsync(new Spoils(500, 0), default).AsTask().GetAwaiter().GetResult();
            return a;
        }
        async Task<int> Spent(ArmyRules r, int token) { var a = Make(r); int g = a.Read().Gold; await a.ApplyArmyOrdersAsync(DoctrineSet.Of(token), default); return g - a.Read().Gold; }
        int age = DoctrineVocabulary.Army(ArmyOrderKind.Age), flipMachine = DoctrineVocabulary.Army(ArmyOrderKind.Flip, 6);
        Assert.Equal(0, await Spent(ArmyRules.Default, age));                                   // the game: an Age is free
        Assert.True(await Spent(ArmyContract.PricedRules, age) > 0);                           // the rule ON: it costs
        Assert.Equal(0, await Spent(ArmyContract.PricedRules with { PriceAge = false }, age));
        Assert.True(await Spent(ArmyRules.Default, flipMachine) > await Spent(new ArmyRules { PriceReach = false }, flipMachine));   // a Machine fires from 10 units: dearer with reach
    }
}
