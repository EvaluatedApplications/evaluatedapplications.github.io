namespace Doctrine.Tests;

/// <summary>
/// The comeback muster (fifth round, part C; Doctrine\todo\difficulty-2026-10-19.md): when spoils are paid, half of the army's shortfall against the strength the next enemy is raised at
/// is added as gold. A pure function of the tables and the domain, so the contract case takes the function and the mutants are other functions.
/// </summary>
public class ComebackTests
{
    [Fact]
    public void The_next_enemys_expected_wealth_is_read_from_the_levy_tables_and_the_domains_gold()
    {
        // 24 soldiers drawn from the levy weights [6,2,3,3,1,1,2,1] at the draft prices [2,3,2,2,2,3,3,5]: 24 x 46 / 19 = 58 (rounded), plus the enemy's gold
        Assert.Equal(58, Roster.ExpectedEnemyWealth(0));
        Assert.Equal(62, Roster.ExpectedEnemyWealth(4));
    }

    /// <summary>The muster is half of the shortfall (rounded up), zero at or above parity, and never lifts an army above parity.</summary>
    public static string? MusterIsHalfTheShortfallAndNeverAboveParity(Func<long, int, int> muster)
    {
        for (int gold = 0; gold <= 40; gold += 4)
            for (long wealth = 0; wealth <= 140; wealth++)
            {
                long parity = Roster.ExpectedEnemyWealth(gold);
                int m = muster(wealth, gold);
                long deficit = Math.Max(0, parity - wealth);
                if (m != (deficit + 1) / 2) return $"wealth {wealth}, enemy gold {gold}: shortfall {deficit}, muster {m}, expected {(deficit + 1) / 2}";
                if (wealth <= parity && wealth + m > parity) return $"wealth {wealth}, enemy gold {gold}: the muster lifts the army above parity ({wealth + m} against {parity})";
                if (wealth >= parity && m != 0) return $"wealth {wealth} is at or above parity but got {m}";
            }
        return null;
    }

    [Fact] public void The_muster_is_half_the_shortfall_and_never_above_parity() => Assert.Null(MusterIsHalfTheShortfallAndNeverAboveParity(Roster.Muster));

    // mutants of the function: each must make the check go red
    [Fact] public void A_muster_that_pays_nothing_is_caught() => Assert.NotNull(MusterIsHalfTheShortfallAndNeverAboveParity((w, g) => 0));
    [Fact] public void A_muster_that_pays_the_whole_shortfall_is_caught() => Assert.NotNull(MusterIsHalfTheShortfallAndNeverAboveParity((w, g) => (int)Math.Max(0, Roster.ExpectedEnemyWealth(g) - w)));
    [Fact] public void A_muster_that_pays_the_surplus_too_is_caught() => Assert.NotNull(MusterIsHalfTheShortfallAndNeverAboveParity((w, g) => (int)Math.Abs(Roster.ExpectedEnemyWealth(g) - w) / 2));
    [Fact] public void A_muster_that_pays_twice_the_shortfall_is_caught() => Assert.NotNull(MusterIsHalfTheShortfallAndNeverAboveParity((w, g) => (int)Math.Max(0, Roster.ExpectedEnemyWealth(g) - w) * 2));

    static async Task<GameResult> Play(DoctrineDomain rules, bool real, int seed)
    {
        var b = DoctrineBuilder.New().WithPlayer(new ScriptedPlayer(1)).WithRules(rules);
        if (real) b.WithDefaults(r => r.WithArmy(new PleromaArmyService()).WithTracer(TracerWorlds.Scratch()).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!)).WithGeneral(new TinyHoloGeneral()));
        return await b.Build().PlayAsync(seed);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task In_a_game_the_muster_is_part_of_the_gold_and_only_an_army_below_parity_gets_one(bool real)
    {
        int paid = 0;
        foreach (int seed in new[] { 1234, 1235, 1236 })
        {
            var on = await Play(new DoctrineDomain { MaxRounds = 8, Comeback = true }, real, seed);
            var off = await Play(new DoctrineDomain { MaxRounds = 8 }, real, seed);
            Assert.All(off.Rounds, r => Assert.Equal(0, r.Spoils.Muster));
            foreach (var r in on.Rounds)
            {
                Assert.InRange(r.Spoils.Muster, 0, r.Spoils.Gold);                                    // included in the gold, never on top of it
                if (r.Round == 7) Assert.Equal(0, r.Spoils.Muster);                                    // the last round has no next enemy
                if (r.Spoils.Muster > 0) paid++;
            }
        }
        Assert.True(paid > 0, "three games of a scripted player never fell behind the next enemy: the muster was never exercised");
    }

    [Fact]
    public async Task The_comeback_is_a_switch_and_off_it_changes_nothing()
    {
        // Off (the Comeback default of the rules as they stand when the switch was added) a game is the game it was: the digest does not depend on a muster that was not paid.
        var a = await Play(new DoctrineDomain { MaxRounds = 8, Comeback = false }, false, 1234);
        var b = await Play(new DoctrineDomain { MaxRounds = 8 }, false, 1234);
        Assert.Equal(a.Digest(), b.Digest());
        var on = await Play(new DoctrineDomain { MaxRounds = 8, Comeback = true }, false, 1234);
        Assert.NotEqual(a.Digest(), on.Digest());
    }

    [Fact]
    public async Task Flat_spoils_pay_the_same_for_a_win_and_a_loss_and_no_veterancy()
    {
        var flat = await Play(new DoctrineDomain { MaxRounds = 8, FlatSpoils = true }, false, 1234);
        Assert.All(flat.Rounds, r => { Assert.Equal(0, r.Spoils.Experience); Assert.Equal(r.Result.DefenderLosses.Sum(l => l.Lost) / 2 + 4, r.Spoils.Gold); });
    }
}
