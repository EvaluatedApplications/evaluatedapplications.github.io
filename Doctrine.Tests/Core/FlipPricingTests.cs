namespace Doctrine.Tests;

/// <summary>
/// A Flip is no longer free. Measured before the change (Doctrine\todo\difficulty-2026-10-19.md, baseline): one token "Flip Machine" on a levy took the
/// win rate from 0.526 to 0.906 on the same 120 games (Spearmen with Catapult damage and range at a Spearman's price). A relabel now costs the
/// price difference of the trait sets (the battle's own stat tables decide what a trait set is worth).
/// </summary>
public class FlipPricingTests
{
    static int Ceil2(int dmg, int taken) => (2 * dmg + taken - 1) / taken;
    static int Ceil2Reach(int dmg, int taken, int reachPct) => (int)((2L * dmg * (100 + reachPct) + (long)taken * 100 - 1) / ((long)taken * 100));

    [Fact]
    public void A_trait_set_is_worth_ceil_of_twice_damage_over_taken_from_the_battle_tables()
    {
        // WITHOUT reach, pinned to the literal worth of each kind as it stood before reach was priced (Spearmen 2, Shieldwall 3, Riders 2, Archers 2,
        // Priests 2, Wights 3, Wolves 3, Catapults 4; a Spearman flipped to Machine 5)
        int[] plain = [2, 3, 2, 2, 2, 3, 3, 4];
        // WITH reach (the shipped rule): only the Catapults move (a Machine fires from 10 units, 32 percent free fire): 5, and that Machine 7
        int[] withReach = [2, 3, 2, 2, 2, 3, 3, 5];
        for (int k = 0; k < Roster.Size; k++)
        {
            var t = Roster.TraitsOf(k);
            Assert.Equal(plain[k], Roster.TraitPrice(t, reach: false));
            Assert.Equal(withReach[k], Roster.TraitPrice(t));
            Assert.Equal(Ceil2(Battle.UnitRules.DamagePct(t), Battle.UnitRules.TakenPct(t)), Roster.TraitPrice(t, reach: false));
            Assert.Equal(Ceil2Reach(Battle.UnitRules.DamagePct(t), Battle.UnitRules.TakenPct(t), Battle.UnitRules.ReachPct(t)), Roster.TraitPrice(t));
        }
        Assert.Equal(5, Roster.TraitPrice(Trait.Machine, reach: false));   // a plain soldier with Catapult damage is worth 5 without reach
        Assert.Equal(7, Roster.TraitPrice(Trait.Machine));                 // and 7 with it
    }

    [Fact]
    public void Reach_is_the_free_fire_of_a_longer_weapon_derived_from_the_stat_tables()
    {
        Assert.Equal(0, Battle.UnitRules.ReachPct(Trait.None));
        Assert.Equal(0, Battle.UnitRules.ReachPct(Trait.Swift | Trait.Heavy | Trait.Holy));
        Assert.Equal(20, Battle.UnitRules.ReachPct(Trait.Ranged));                        // (7 - 2.2) / (2 x 2.0) = 1.2 ticks, 1.2 x 100 / 600 = 20 percent
        Assert.Equal(32, Battle.UnitRules.ReachPct(Trait.Machine));                       // (10 - 2.2) / 4 = 1.95 ticks: 32 percent (floored)
        Assert.Equal(32, Battle.UnitRules.ReachPct(Trait.Machine | Trait.Ranged));        // the longer weapon counts, not both
        // it follows the constants: a unit that reaches no further than melee gets nothing, and more reach never gets less
        for (int t = 0; t < 256; t++)
        {
            var tr = (Trait)t;
            Assert.True(Battle.UnitRules.ReachPct(tr) >= 0);
            if (Battle.UnitRules.Range(tr) <= Battle.UnitRules.MeleeRange) Assert.Equal(0, Battle.UnitRules.ReachPct(tr));
        }
    }

    [Fact]
    public void An_age_and_an_aura_cost_the_power_they_add_times_the_worth_and_a_wrap_is_free()
    {
        // one step of the ring is VetStepPct power points on a soldier worth W gold: W x 5 gold-percent; 24 Spearmen (worth 2) x 5 = 240 -> 3 gold
        Assert.Equal(2 * Roster.VetStepPct, Roster.AgeWeight(Trait.None, 0, true));
        Assert.Equal(3, Roster.GoldFor(24 * Roster.AgeWeight(Trait.None, 0, true)));
        Assert.Equal(0, Roster.AgeWeight(Trait.None, Roster.VetPeriod - 1, true));        // the wrap back to 0 loses power: free
        // an aura of 20 percent on Ranged (worth 2) adds 20 points: W x 20 per soldier, rented over the rounds left
        Assert.Equal(2 * 20, Roster.AuraWeight(Trait.Ranged, 0, 0, 1 << 2, 20, true));
        Assert.Equal(0, Roster.AuraWeight(Trait.None, 0, 0, 1 << 2, 20, true));           // a cohort without the trait gains nothing, so pays nothing
        Assert.Equal(0, Roster.AuraWeight(Trait.Ranged, 1 << 2, 20, 1 << 2, 20, true));   // the same aura again adds nothing
        Assert.Equal(Roster.CampaignRounds, Roster.AuraRentalDivisor(0));
        Assert.Equal(1, Roster.AuraRentalDivisor(Roster.CampaignRounds - 1));
        Assert.Equal(1, Roster.AuraRentalDivisor(99));                                    // never zero
        Assert.Equal(0, Roster.GoldFor(0)); Assert.Equal(1, Roster.GoldFor(1)); Assert.Equal(1, Roster.GoldFor(100)); Assert.Equal(2, Roster.GoldFor(101));
        Assert.Equal(1, Roster.GoldFor(101, 2));                                          // 101 / 200 rounds up to 1
    }

    [Fact]
    public void Interest_is_gold_over_the_campaign_less_one_floored()
    {
        Assert.Equal(0, Roster.Interest(0)); Assert.Equal(0, Roster.Interest(6)); Assert.Equal(1, Roster.Interest(7)); Assert.Equal(2, Roster.Interest(14));
        Assert.Equal(0, Roster.Interest(-5));
        // the derivation: banking one round at the start of a campaign breaks even in soldier-rounds, (1 + 1/7) x 7 = 8 = the campaign length
        Assert.Equal(8 * 7, (7 + 1) * (Roster.CampaignRounds - 1));
        Assert.Equal(7, Roster.CampaignRounds - 1);
    }

    [Fact]
    public void A_relabel_costs_the_difference_when_the_soldier_gets_better_and_nothing_when_worse()
    {
        Assert.Equal(5, Roster.RelabelCost(Trait.None, 6));                                              // Spearman -> Machine: 7 - 2 (with reach)
        Assert.Equal(3, Roster.RelabelCost(Trait.None, 6, reach: false));                                // 5 - 2 before reach was priced
        Assert.Equal(0, Roster.RelabelCost(Trait.Machine | Trait.Ranged | Trait.Heavy, 6));              // Catapult loses Machine: cheaper, free
        for (int t = 0; t < 256; t++)
            for (int b = 0; b < DoctrineVocabulary.Traits; b++)
            {
                int c = Roster.RelabelCost((Trait)t, b);
                Assert.True(c >= 0, "a relabel never pays the army");
                Assert.Equal(Math.Max(0, Roster.TraitPrice((Trait)(t ^ (1 << b))) - Roster.TraitPrice((Trait)t)), c);
                Assert.True(Roster.RelabelCost((Trait)t, b, reach: false) >= 0);
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
