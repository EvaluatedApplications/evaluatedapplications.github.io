namespace Doctrine.Tests;

/// <summary>
/// The third round of Doctrine\todo\difficulty-2026-10-19.md: the counter cycle between kind classes (read from the stat tables) and the tick limit that counts
/// power, not bodies. Each behaviour is a function of the engine under test so the mutation proofs run the SAME check on a broken engine and watch it go red.
/// </summary>
public class CounterAndLimitTests
{
    // ---- the class table and the cycle (pure, from the stat tables) ----

    [Fact]
    public void A_kinds_class_is_read_from_the_stat_tables()
    {
        // Spearmen Plain, Shieldwall Armoured (Heavy and Shielded), Riders Swift, Archers Ranged, Priests Ranged (Holy is not an edge), Wights Plain (Cursed is not an edge),
        // Wolves Swift, Catapults Ranged (the Machine's reach, 32, beats its Heavy, 11).
        KindClass[] want = [KindClass.Plain, KindClass.Armoured, KindClass.Swift, KindClass.Ranged, KindClass.Ranged, KindClass.Plain, KindClass.Swift, KindClass.Ranged];
        for (int k = 0; k < Roster.Size; k++) Assert.Equal(want[k], UnitRules.ClassOf(Roster.TraitsOf(k)));
        // the class follows the live traits: a Flip changes it (Spearmen made Machines are Ranged; Riders made Shielded stay Swift, 50 against 33)
        Assert.Equal(KindClass.Ranged, UnitRules.ClassOf(Trait.Machine));
        Assert.Equal(KindClass.Swift, UnitRules.ClassOf(Trait.Swift | Trait.Shielded));
        Assert.Equal(KindClass.Armoured, UnitRules.ClassOf(Trait.Shielded));
        Assert.Equal(KindClass.Plain, UnitRules.ClassOf(Trait.None));
    }

    [Fact]
    public void The_counter_cycle_is_a_ring_where_each_class_counters_one_and_is_countered_by_one()
    {
        Trait[] rep = [Trait.None, Trait.Swift, Trait.Ranged, Trait.Shielded];          // one trait set per class, in KindClass order
        for (int i = 0; i < 4; i++) Assert.Equal((KindClass)i, UnitRules.ClassOf(rep[i]));
        for (int a = 0; a < 4; a++)
        {
            Assert.False(UnitRules.Counters(rep[a], rep[a]), "no class counters itself");
            Assert.Equal(1, Enumerable.Range(0, 4).Count(b => UnitRules.Counters(rep[a], rep[b])));   // exactly one prey
            Assert.Equal(1, Enumerable.Range(0, 4).Count(b => UnitRules.Counters(rep[b], rep[a])));   // exactly one predator
        }
        Assert.True(UnitRules.Counters(Trait.None, Trait.Swift));         // a braced line breaks a charge
        Assert.True(UnitRules.Counters(Trait.Swift, Trait.Ranged));       // the fast close the gap
        Assert.True(UnitRules.Counters(Trait.Ranged, Trait.Shielded));    // the shielded are worn down at range
        Assert.True(UnitRules.Counters(Trait.Shielded, Trait.None));      // shields turn an unarmoured soldier's thrusts
        Assert.False(UnitRules.Counters(Trait.Swift, Trait.None));        // and not the other way round
    }

    [Fact]
    public void A_counter_is_the_hit_multiplier_the_holy_already_has()
    {
        Assert.Equal(UnitRules.CounterNum * 100 / UnitRules.CounterDen, UnitRules.Hit(100, Trait.None, 100, Trait.Swift, false, counters: true));
        Assert.Equal(100, UnitRules.Hit(100, Trait.None, 100, Trait.Swift, false, counters: false));                 // off: no counter
        Assert.Equal(100, UnitRules.Hit(100, Trait.Swift, 100, Trait.None, false, counters: true));                  // the countered side gets nothing back
        Assert.Equal(150, UnitRules.Hit(100, Trait.Holy, 100, Trait.Cursed, false));                                 // the holy rule is unchanged
        Assert.Equal(225, UnitRules.Hit(100, Trait.Holy | Trait.Ranged, 100, Trait.Cursed | Trait.Shielded, false, counters: true));   // Ranged counters Armoured and Holy hurts Cursed: they stack
        Assert.Equal(150, UnitRules.Hit(100, Trait.Swift, 100, Trait.Ranged, false, counters: true, counterReversed: false));
        Assert.Equal(100, UnitRules.Hit(100, Trait.Swift, 100, Trait.Ranged, false, counters: true, counterReversed: true));   // the mutant reads the cycle backwards
    }

    [Fact]
    public void Rating_is_worth_without_the_rounding()
    {
        for (int k = 0; k < Roster.Size; k++)
        {
            var t = Roster.TraitsOf(k);
            // one soldier at power 100: rating / 10^4 rounded up (the worth formula has the 100 for damage and taken inside) is the worth in gold of a body
            long r = UnitRules.Rating(1, 100, t);
            Assert.Equal(Roster.TraitPrice(t, true), (int)((2 * r + 9_999) / 10_000));
        }
        Assert.Equal(2 * UnitRules.Rating(30, 100, Trait.Ranged), UnitRules.Rating(60, 100, Trait.Ranged));
        Assert.True(UnitRules.Rating(40, 100, Trait.Heavy | Trait.Shielded) > UnitRules.Rating(50, 100, Trait.None));   // 40 Shieldwall outweigh 50 Spearmen
        Assert.Equal(0, UnitRules.Rating(0, 100, Trait.None));
    }

    // ---- in the battle: the cycle decides, and the tick limit counts power ----

    static CohortRead C(int key, int n) => Sample.Cohort(key, n, 0);
    static readonly FieldOrder[] Advance = [new(0, FieldOrderKind.Advance, 1)];

    /// <summary>The surviving share of kind a minus that of kind b when 60 of each fight on the open field, averaged over both orientations and three seeds.</summary>
    static async Task<double> Margin(TracerBattleEngine e, int a, int b)
    {
        double sum = 0; int n = 0;
        foreach (int seed in new[] { 3, 5, 7 })
            foreach (bool aAttacks in new[] { true, false })
            {
                var sideA = new BattleSide(aAttacks ? Side.Player : Side.Enemy, [C(a, 60)], Advance);
                var sideB = new BattleSide(aAttacks ? Side.Enemy : Side.Player, [C(b, 60)], Advance);
                var setup = new BattleSetup("open-field", seed, 100, aAttacks ? sideA : sideB, aAttacks ? sideB : sideA);
                var r = (await Sample.RunAsync(e, setup)).Result!;
                int lostA = (aAttacks ? r.AttackerLosses : r.DefenderLosses).Sum(l => l.Lost), lostB = (aAttacks ? r.DefenderLosses : r.AttackerLosses).Sum(l => l.Lost);
                sum += (lostB - lostA) / 60.0; n++;
            }
        return sum / n;
    }

    /// <summary>For every edge of the cycle the class that counters wins a clearer victory than it does with counters off: the cycle is what moved the result.</summary>
    public static async Task<string?> TheCycleDecidesTheEdges(TracerBattleEngine on)
    {
        var off = TracerWorlds.Engine(new TracerBattleOptions { Counters = false });
        // (counters, countered): Spearmen over Riders, Riders over Archers, Archers over Shieldwall, Shieldwall over Spearmen
        foreach (var (a, b, name) in new[] { (0, 2, "Plain over Swift"), (2, 3, "Swift over Ranged"), (3, 1, "Ranged over Armoured"), (1, 0, "Armoured over Plain") })
        {
            double with = await Margin(on, a, b), without = await Margin(off, a, b);
            if (with - without < 0.15) return $"{name}: the counter side's margin is {with:F2} with the cycle and {without:F2} without; the cycle should add at least .15";
        }
        return null;
    }

    [Fact] public async Task The_counter_cycle_decides_every_edge_of_the_ring() => Assert.Null(await TheCycleDecidesTheEdges(TracerWorlds.Engine(new TracerBattleOptions { Counters = true })));

    [Fact]
    public async Task The_cycle_is_the_game_and_off_it_is_a_different_battle()
    {
        // The default engine has the cycle on: the same setup with the option explicitly on is the same recording; explicitly off, a mixed fight (Archers meet Shieldwall) differs.
        var setup = Sample.Setup("open-field", 3, 60, 30, 30);
        var def = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(), setup)).Replay!);
        var on = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { Counters = true }), setup)).Replay!);
        var off = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { Counters = false }), setup)).Replay!);
        Assert.Equal(def, on);
        Assert.NotEqual(def, off);
        Assert.True(new TracerBattleOptions().LimitByPower && new TracerBattleOptions().Counters, "both rules are the game");
    }

    /// <summary>50 Spearmen and 40 Shieldwall hold the line for 10 ticks (nobody can reach anybody): by bodies the 50 win, by power the 40 do.</summary>
    static BattleSetup Standoff(int attackerKey, int attackers, int defenderKey, int defenders)
    {
        var hold = new FieldOrder[] { new(0, FieldOrderKind.Hold, 1) };
        return new BattleSetup("open-field", 11, 10,
            new BattleSide(Side.Player, [C(attackerKey, attackers)], hold), new BattleSide(Side.Enemy, [C(defenderKey, defenders)], hold));
    }

    /// <summary>The tick limit counts power left when the engine says so: the smaller force of heavier soldiers wins a standoff, and equal power is a draw.</summary>
    public static async Task<string?> TheLimitCountsPower(TracerBattleEngine e)
    {
        var st = await Sample.RunAsync(e, Standoff(0, 50, 1, 40));
        if (st.Result!.Ticks != 10) return $"the standoff should run to the limit, ran {st.Result.Ticks} ticks";
        if (st.Result.AttackerLosses.Count + st.Result.DefenderLosses.Count != 0) return "nobody should lose a soldier in a standoff";
        if (st.Result.Outcome != BattleStatus.DefenderWon) return $"50 Spearmen against 40 Shieldwall: power says the Shieldwall, got {st.Result.Outcome}";
        var even = await Sample.RunAsync(e, Standoff(0, 40, 0, 40));
        if (even.Result!.Outcome != BattleStatus.Draw) return $"equal power must be a Draw, got {even.Result.Outcome}";
        var more = await Sample.RunAsync(e, Standoff(0, 41, 0, 40));
        if (more.Result!.Outcome != BattleStatus.AttackerWon) return $"41 against 40 of one kind: the larger force wins, got {more.Result.Outcome}";
        return null;
    }

    [Fact] public async Task With_the_limit_by_power_a_standoff_goes_to_the_stronger_force() => Assert.Null(await TheLimitCountsPower(TracerWorlds.Engine(new TracerBattleOptions { LimitByPower = true })));

    [Fact]
    public async Task With_the_option_off_a_standoff_goes_to_the_larger_body_count_as_before()
    {
        var st = await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { LimitByPower = false }), Standoff(0, 50, 1, 40));
        Assert.Equal(BattleStatus.AttackerWon, st.Result!.Outcome);
        var game = await Sample.RunAsync(TracerWorlds.Engine(), Standoff(0, 50, 1, 40));   // the game: by power
        Assert.Equal(BattleStatus.DefenderWon, game.Result!.Outcome);
    }

    [Fact] public async Task A_limit_that_counts_bodies_while_the_option_is_on_is_caught() => Assert.NotNull(await TheLimitCountsPower(TracerWorlds.Engine(new TracerBattleOptions { LimitByPower = true, Probe = OrderProbe.LimitCountsBodies })));
    [Fact] public async Task A_limit_by_power_that_is_switched_off_is_caught() => Assert.NotNull(await TheLimitCountsPower(TracerWorlds.Engine(new TracerBattleOptions { LimitByPower = false })));
    [Fact] public async Task A_cycle_that_is_switched_off_is_caught() => Assert.NotNull(await TheCycleDecidesTheEdges(TracerWorlds.Engine(new TracerBattleOptions { Counters = false })));
    [Fact] public async Task A_cycle_read_backwards_is_caught() => Assert.NotNull(await TheCycleDecidesTheEdges(TracerWorlds.Engine(new TracerBattleOptions { Counters = true, Probe = OrderProbe.CounterReversed })));

    // ---- the enemy follows the price rule ----

    [Fact]
    public void The_scripted_enemys_picks_are_sized_with_the_price_the_rules_name()
    {
        foreach (int gold in new[] { 14, 18, 30, 60 })
            for (int seed = 1; seed <= 20; seed++)
            {
                var picks = ScriptedOpponent.Picks(seed % 8, seed, gold);
                long cost = picks.Sum(p => (long)ArmyRules.Default.DraftPrice(p.Kind) * p.Count);
                Assert.True(cost <= gold, $"seed {seed}, gold {gold}: the enemy's picks cost {cost}");
                Assert.True(picks.Count > 0, "an enemy with gold drafts something");
            }
    }
}
