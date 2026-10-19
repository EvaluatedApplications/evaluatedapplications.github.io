namespace Doctrine.Tests;

/// <summary>
/// A Flip is no longer free. Measured before the change (Doctrine\todo\difficulty-2026-10-19.md, baseline): one token "Flip Machine" on a levy took the
/// win rate from 0.526 to 0.906 on the same 120 games (Spearmen with Catapult damage and range at a Spearman's price). A relabel now costs the
/// price difference of the trait sets (the battle's own stat tables decide what a trait set is worth).
/// </summary>
public class FlipPricingTests
{
    static int Ceil2(int dmg, int taken) => (2 * dmg + taken - 1) / taken;

    [Fact]
    public void A_trait_set_is_worth_ceil_of_twice_damage_over_taken_from_the_battle_tables()
    {
        // pinned to the literal worth of each kind (Spearmen 2, Shieldwall 3, Riders 2, Archers 2, Priests 2, Wights 3, Wolves 3, Catapults 4)
        int[] worth = [2, 3, 2, 2, 2, 3, 3, 4];
        for (int k = 0; k < Roster.Size; k++)
        {
            var t = Roster.TraitsOf(k);
            Assert.Equal(worth[k], Roster.TraitPrice(t));
            Assert.Equal(Ceil2(Battle.UnitRules.DamagePct(t), Battle.UnitRules.TakenPct(t)), Roster.TraitPrice(t));
        }
        Assert.Equal(5, Roster.TraitPrice(Trait.Machine));   // a plain soldier with Catapult damage is worth 5
    }

    [Fact]
    public void A_relabel_costs_the_difference_when_the_soldier_gets_better_and_nothing_when_worse()
    {
        Assert.Equal(3, Roster.RelabelCost(Trait.None, 6));                                              // Spearman -> Machine: 5 - 2
        Assert.Equal(0, Roster.RelabelCost(Trait.Machine | Trait.Ranged | Trait.Heavy, 6));              // Catapult loses Machine: cheaper, free
        for (int t = 0; t < 256; t++)
            for (int b = 0; b < DoctrineVocabulary.Traits; b++)
            {
                int c = Roster.RelabelCost((Trait)t, b);
                Assert.True(c >= 0, "a relabel never pays the army");
                Assert.Equal(Math.Max(0, Roster.TraitPrice((Trait)(t ^ (1 << b))) - Roster.TraitPrice((Trait)t)), c);
            }
    }

    [Theory]
    [InlineData("stub")]
    [InlineData("pleroma")]
    public async Task The_free_levy_of_machines_is_closed_a_fresh_army_cannot_afford_it(string which)
    {
        IArmyService svc = which == "stub" ? new StubArmyService() : new PleromaArmyService();
        var flipMachine = DoctrineVocabulary.Army(ArmyOrderKind.Flip, 6);
        for (int seed = 1; seed <= 20; seed++)
        {
            var a = svc.Create(Side.Player, seed);
            var before = a.Read();
            Assert.False(before.Legal.Allows(flipMachine), $"seed {seed}: a levy of {before.Soldiers} soldiers and {before.Gold} gold cannot pay to make them all Machines");
            var rep = await a.ApplyArmyOrdersAsync(DoctrineSet.Of(flipMachine), default);
            Assert.Equal(new ArmyOrdersReport(0, 1), rep);
            Assert.Equal(before.Digest(), a.Read().Digest());
        }
    }
}
