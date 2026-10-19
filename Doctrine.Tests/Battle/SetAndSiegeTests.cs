namespace Doctrine.Tests;

/// <summary>
/// The fifth round of Doctrine\todo\difficulty-2026-10-19.md, part B: SET (a cohort that has held its ground unhit for <see cref="UnitRules.SetTicks"/> ticks strikes harder until it
/// is hit or moves), SIEGE ARENAS (the besieger that has not been in an exchange by the arena's clock loses its stance), and the roles that swap every round. Each behaviour is a
/// function of the engine under test so the mutation proofs run the SAME check on a broken engine and watch it go red.
/// </summary>
public class SetAndSiegeTests
{
    static CohortRead C(int key, int count, int slot) => Sample.Cohort(key, count, slot);
    static FieldOrder O(int group, FieldOrderKind kind) => new(group, kind, 1);

    // ---- pure facts from the tables ----

    [Fact]
    public void The_set_time_and_bonus_are_read_from_the_tables()
    {
        Assert.Equal(6, UnitRules.SetTicks);                                                    // Fp / BaseDamage: the time one soldier needs to kill another
        Assert.Equal(32, UnitRules.ReachPct(Trait.Machine));                                    // the best reach edge in the tables
        Assert.Equal(32, UnitRules.SetBonusPct(Trait.None));                                    // a pike line gets the full edge
        Assert.Equal(32, UnitRules.SetBonusPct(Trait.Heavy | Trait.Shielded));                  // Shieldwall
        Assert.Equal(12, UnitRules.SetBonusPct(Trait.Ranged));                                  // Archers: 32 - 20
        Assert.Equal(12, UnitRules.SetBonusPct(Trait.Holy | Trait.Ranged));                     // Priests
        Assert.Equal(0, UnitRules.SetBonusPct(Trait.Machine | Trait.Ranged | Trait.Heavy));     // Catapults already have it
        for (int k = 0; k < Roster.Size; k++) Assert.InRange(UnitRules.SetBonusPct(Roster.TraitsOf(k)), 0, 32);
    }

    [Fact]
    public void The_siege_clock_of_an_arena_is_the_walk_of_the_slowest_kind_between_the_front_lines()
    {
        var arenas = ArenaCatalogue.Build().ToDictionary(a => a.Info.Id);
        Assert.Equal(14, arenas["chokepoint"].SiegeClockTicks);     // ceil(0.4 x 48 / 1.4)
        Assert.Equal(16, arenas["falling-gate"].SiegeClockTicks);   // ceil(0.4 x 56 / 1.4)
        Assert.Equal(0, arenas["open-field"].SiegeClockTicks);
        Assert.Equal(0, arenas["ridge"].SiegeClockTicks);
        Assert.Equal(2, arenas.Values.Count(a => a.Info.IsSiege));  // two of the four are siege arenas, the rest are open field
        Assert.Equal(14, arenas["chokepoint"].Info.SiegeClock);
        Assert.False(new ArenaInfo("x", "x", 10, 10).IsSiege);
    }

    [Fact]
    public void The_roles_swap_every_round()
    {
        Assert.Equal([Side.Player, Side.Enemy, Side.Player, Side.Enemy, Side.Player, Side.Enemy, Side.Player, Side.Enemy], Enumerable.Range(0, 8).Select(r => SiegeRules.BesiegerOf(r)));
        Assert.All(Enumerable.Range(0, 8), r => Assert.Equal(Side.Player, SiegeRules.BesiegerOf(r, swap: false)));
    }

    // ---- stepping a battle by hand, with the session's counters ----

    sealed record Step(int Tick, int SetDelta, bool Hit0, int SetNow);

    static async Task<(TracerBattleSession S, List<Step> Steps, List<FieldOrderKind[]> Orders)> Play(TracerBattleEngine e, BattleSetup setup, int ticks)
    {
        var s = (TracerBattleSession)await e.StartAsync(setup, default);
        var steps = new List<Step>(); var orders = new List<FieldOrderKind[]>();
        for (int t = 0; t < ticks && s.Status == BattleStatus.Running; t++)
        {
            int before = s.Stats.SetCohortTicks[0];
            await s.FieldOrdersAsync(default);
            orders.Add([s.Agents().First(a => a.Blob == 0 && a.Alive).Order, s.Agents().First(a => a.Blob == 1 && a.Alive).Order]);
            await s.MoveAsync(default); await s.EngageAsync(default); await s.ApplyLossesAsync(default);
            var f = s.Record();
            steps.Add(new Step(t, s.Stats.SetCohortTicks[0] - before, f.Events.Any(ev => ev.Target == 0), s.Stats.SetCohortTicks[0]));
        }
        return (s, steps, orders);
    }

    static BattleSetup Hold(string arena, int seed, int ticks, Side? besieger = null, int a = 40, int d = 40)
        => new(arena, seed, ticks,
            new BattleSide(Side.Player, [C(0, a, 0)], [O(0, FieldOrderKind.Hold)]),
            new BattleSide(Side.Enemy, [C(0, d, 0)], [O(0, FieldOrderKind.Hold)]), besieger);

    // ---- SET ----

    /// <summary>Two lines that hold, far apart: each is set at the start of tick 6 (six still ticks) and stays set while nobody comes.</summary>
    public static async Task<string?> AHoldingLineBecomesSetAfterSixTicks(TracerBattleEngine e)
    {
        var (s, steps, _) = await Play(e, Hold("open-field", 3, 20), 20);
        if (s.Stats.SetFirstTick[0] != 6 || s.Stats.SetFirstTick[1] != 6) return $"both lines should be set at the start of tick 6, first set at {s.Stats.SetFirstTick[0]} and {s.Stats.SetFirstTick[1]}";
        if (steps.Take(6).Any(x => x.SetNow != 0)) return "a cohort was set before six still ticks";
        if (steps[^1].SetNow != 14) return $"nobody came, so the line should be set for ticks 6..19 (14 cohort-ticks), counted {steps[^1].SetNow}";
        return null;
    }

    /// <summary>A cohort that is moving is never still: an advancing line is not set while it walks; the line that holds is.</summary>
    public static async Task<string?> AMovingLineIsNeverSet(TracerBattleEngine e)
    {
        var setup = new BattleSetup("open-field", 3, 9, new BattleSide(Side.Player, [C(0, 40, 0)], [O(0, FieldOrderKind.Advance)]), new BattleSide(Side.Enemy, [C(0, 40, 0)], [O(0, FieldOrderKind.Hold)]));
        var (s, _, _) = await Play(e, setup, 9);
        if (s.Stats.SetFirstTick[0] != -1) return $"an advancing line was set at tick {s.Stats.SetFirstTick[0]}";
        if (s.Stats.SetFirstTick[1] != 6) return $"the holding line should be set at tick 6, was {s.Stats.SetFirstTick[1]}";
        return null;
    }

    /// <summary>A set line strikes harder: Archers that hold until Riders come in range deal more than the same Archers with Set off.</summary>
    public static async Task<string?> ASetLineStrikesHarder(TracerBattleEngine candidate)
    {
        var off = TracerWorlds.Engine(new TracerBattleOptions { Set = false });
        int lostOn = 0, lostOff = 0;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var setup = new BattleSetup("open-field", seed, 12,
                new BattleSide(Side.Enemy, [C(0, 60, 0)], [O(0, FieldOrderKind.Advance)]),            // the attacker slot: 60 Spearmen advance
                new BattleSide(Side.Player, [C(3, 60, 0)], [O(0, FieldOrderKind.Hold)]));              // 60 Archers hold, set from tick 6
            foreach (var (eng, add) in new[] { (candidate, true), (off, false) })
            {
                var st = await Sample.RunAsync(eng, setup);
                int lost = st.Result!.AttackerLosses.Sum(l => l.Lost);
                if (add) lostOn += lost; else lostOff += lost;
            }
        }
        if (lostOn <= lostOff) return $"Archers that held until the Spearmen came in range killed {lostOn} with Set and {lostOff} without: the bonus did nothing";
        return null;
    }

    /// <summary>A set line stops being set when it is hit: from the tick after the first hit on it, the count of set cohort-ticks stays flat for as long as it is under fire.</summary>
    public static async Task<string?> ASetLineIsSetNoMoreOnceHit(TracerBattleEngine e)
    {
        var setup = new BattleSetup("open-field", 3, 60,
            new BattleSide(Side.Player, [C(0, 40, 0)], [O(0, FieldOrderKind.Hold)]),
            new BattleSide(Side.Enemy, [C(0, 120, 0)], [O(0, FieldOrderKind.Advance)]));
        var (s, steps, _) = await Play(e, setup, 60);
        int first = steps.FindIndex(x => x.Hit0);
        if (first < 0) return "the holding line was never hit";
        if (s.Stats.SetFirstTick[0] < 0) return "the holding line was never set before the enemy came";
        // from the tick after the first hit on, the line is under fire every tick until it is gone: its set count must not grow
        var after = steps.Skip(first + 2).ToList();
        if (after.Count == 0) return "the battle ended at once";
        if (after.Any(x => x.SetDelta != 0 && x.Hit0)) return "a cohort under fire was still set";
        if (after.Count(x => x.Hit0) >= 3 && after.TakeWhile(x => x.Hit0).Any(x => x.SetDelta != 0)) return "the line stayed set after being hit";
        return null;
    }

    [Fact] public async Task A_holding_line_becomes_set_after_six_still_ticks() => Assert.Null(await AHoldingLineBecomesSetAfterSixTicks(TracerWorlds.Engine()));
    [Fact] public async Task A_moving_line_is_never_set() => Assert.Null(await AMovingLineIsNeverSet(TracerWorlds.Engine()));
    [Fact] public async Task A_set_line_strikes_harder_than_the_same_line_without_set() => Assert.Null(await ASetLineStrikesHarder(TracerWorlds.Engine()));
    [Fact] public async Task A_set_line_is_set_no_more_once_it_is_hit() => Assert.Null(await ASetLineIsSetNoMoreOnceHit(TracerWorlds.Engine()));

    // mutants: each break must make its check go red
    [Fact] public async Task A_set_bonus_that_is_ignored_is_caught() => Assert.NotNull(await ASetLineStrikesHarder(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.SetBonusIgnored })));
    [Fact] public async Task A_set_rule_that_is_switched_off_is_caught() => Assert.NotNull(await AHoldingLineBecomesSetAfterSixTicks(TracerWorlds.Engine(new TracerBattleOptions { Set = false })));
    [Fact] public async Task A_set_that_never_expires_is_caught() => Assert.NotNull(await ASetLineIsSetNoMoreOnceHit(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.SetNeverExpires })));

    [Fact]
    public async Task Set_off_plays_the_battle_it_did_before_for_lines_that_never_hold()
    {
        // No line holds, so nothing is ever set: Set on and Set off are the same recording (the rule changes only what a still line does).
        var setup = Sample.Setup("ridge", 5, 60) with { Attacker = Sample.Setup("ridge", 5, 60).Attacker with { Orders = [O(0, FieldOrderKind.Advance), O(1, FieldOrderKind.Advance), O(2, FieldOrderKind.Advance)] } };
        setup = setup with { Defender = setup.Defender with { Orders = [O(0, FieldOrderKind.Advance), O(1, FieldOrderKind.Advance), O(2, FieldOrderKind.Advance)] } };
        var on = await Sample.RunAsync(TracerWorlds.Engine(), setup);
        var off = await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { Set = false }), setup);
        Assert.Equal(ReplayCodec.Digest(off.Replay!), ReplayCodec.Digest(on.Replay!));
    }

    // ---- SIEGE ----

    /// <summary>Both sides hold on the chokepoint with the player besieging: the clock falls at its tick, the besieger advances from then on and the defender does not.</summary>
    public static async Task<string?> TheClockFallsOnABidingBesieger(TracerBattleEngine e)
    {
        var (s, _, orders) = await Play(e, Hold("chokepoint", 3, 30, Side.Player), 30);
        if (s.Stats.ClockFiredAtTick != 14) return $"the clock should fall at tick 14 (the chokepoint's), fell at {s.Stats.ClockFiredAtTick}";
        for (int t = 0; t < orders.Count; t++)
        {
            var wantBesieger = t < 14 ? FieldOrderKind.Hold : FieldOrderKind.Advance;
            if (orders[t][0] != wantBesieger) return $"tick {t}: the besieger's order is {orders[t][0]}, expected {wantBesieger}";
            if (orders[t][1] != FieldOrderKind.Hold) return $"tick {t}: the defender's order is {orders[t][1]}: the defender may bide";
        }
        return null;
    }

    /// <summary>The roles are real: with the ENEMY named besieger the clock is the enemy's and the player may bide.</summary>
    public static async Task<string?> TheClockBindsTheNamedBesiegerOnly(TracerBattleEngine e)
    {
        var (s, _, orders) = await Play(e, Hold("falling-gate", 3, 30, Side.Enemy), 30);
        if (s.Stats.ClockFiredAtTick != 16) return $"the falling gate's clock is 16, fell at {s.Stats.ClockFiredAtTick}";
        if (orders.Skip(16).Any(o => o[0] != FieldOrderKind.Hold)) return "the player (not the besieger) lost its stance";
        if (orders.Skip(16).Any(o => o[1] != FieldOrderKind.Advance)) return "the besieging enemy kept its stance after the clock";
        return null;
    }

    /// <summary>A besieger that fights in time never loses its stance: the clock only binds one that bides.</summary>
    public static async Task<string?> AnEngagedBesiegerKeepsItsStance(TracerBattleEngine e)
    {
        var setup = new BattleSetup("chokepoint", 3, 40,
            new BattleSide(Side.Player, [C(0, 40, 0)], [O(0, FieldOrderKind.Advance)]),
            new BattleSide(Side.Enemy, [C(0, 40, 0)], [O(0, FieldOrderKind.Hold)]), Side.Player);
        var (s, _, _) = await Play(e, setup, 40);
        return s.Stats.ClockFiredAtTick == -1 ? null : $"a besieger that advanced and fought lost its stance at tick {s.Stats.ClockFiredAtTick}";
    }

    [Fact] public async Task The_clock_falls_on_a_besieger_that_bides_and_not_on_the_defender() => Assert.Null(await TheClockFallsOnABidingBesieger(TracerWorlds.Engine()));
    [Fact] public async Task The_clock_binds_the_named_besieger_only() => Assert.Null(await TheClockBindsTheNamedBesiegerOnly(TracerWorlds.Engine()));
    [Fact] public async Task A_besieger_that_fights_in_time_keeps_its_stance() => Assert.Null(await AnEngagedBesiegerKeepsItsStance(TracerWorlds.Engine()));

    [Fact] public async Task A_siege_clock_that_is_off_is_caught() => Assert.NotNull(await TheClockFallsOnABidingBesieger(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.SiegeClockOff })));
    [Fact] public async Task A_siege_clock_that_is_switched_off_is_caught() => Assert.NotNull(await TheClockFallsOnABidingBesieger(TracerWorlds.Engine(new TracerBattleOptions { SiegeClock = false })));

    [Fact]
    public async Task An_open_field_has_no_clock_even_when_a_besieger_is_named()
    {
        var (s, _, orders) = await Play(TracerWorlds.Engine(), Hold("open-field", 3, 30, Side.Player), 30);
        Assert.Equal(-1, s.Stats.ClockFiredAtTick);
        Assert.All(orders, o => Assert.Equal(FieldOrderKind.Hold, o[0]));
    }

    [Fact]
    public async Task A_battle_with_a_besieger_is_recorded_in_version_three_with_the_clock_flag_from_the_frame_after_it_falls()
    {
        var st = await Sample.RunAsync(TracerWorlds.Engine(), Hold("chokepoint", 3, 30, Side.Player));
        var r = st.Replay!;
        Assert.Equal(3, r.Version); Assert.Equal(Side.Player, r.Besieger);
        int first = r.Frames.First(f => (f.WorldFlags & WorldFlag.ClockFell) != 0).Tick;
        Assert.Equal(15, first);                                                       // the clock fell at the start of tick 14, the frame of tick 15 is the first to show it
        Assert.All(r.Frames.Where(f => f.Tick >= 15), f => Assert.NotEqual(0, f.WorldFlags & WorldFlag.ClockFell));
        Assert.True(ReplayCodec.TryDecode(ReplayCodec.Encode(r), out var back, out var err), err);
        Assert.Equal(Side.Player, back!.Besieger); Assert.Equal(ReplayCodec.Digest(r), ReplayCodec.Digest(back));
        // a battle without a besieger is still version 2, and the flag never appears
        var open = (await Sample.RunAsync(TracerWorlds.Engine(), Hold("chokepoint", 3, 30))).Replay!;
        Assert.Equal(2, open.Version); Assert.Null(open.Besieger);
        Assert.All(open.Frames, f => Assert.Equal(0, f.WorldFlags & WorldFlag.ClockFell));
        // the clock changed the battle
        Assert.NotEqual(ReplayCodec.Digest(r with { Version = 2, Besieger = null }), ReplayCodec.Digest(open));
    }

    [Fact]
    public void A_version_three_recording_with_either_besieger_or_none_round_trips_and_an_unknown_besieger_is_refused()
    {
        foreach (Side? b in new Side?[] { null, Side.Player, Side.Enemy })
        {
            var r = new BattleReplay(3, "chokepoint", 7, 5,
                [new ReplayFrame(0, [new ReplayBlob(0, Side.Player, 0, 0, 32, 64, 9), new ReplayBlob(1, Side.Enemy, 1, 0, 900, 64, 9)], []),
                 new ReplayFrame(1, [new ReplayBlob(0, Side.Player, 0, 0, 40, 64, 9), new ReplayBlob(1, Side.Enemy, 1, 0, 890, 64, 8)], [new ReplayEvent(0, 1, 1)], WorldFlag.ClockFell)],
                new BattleResult(BattleStatus.AttackerWon, 1, [], [new CohortLoss(1, 0, 1)]), b);
            var bytes = ReplayCodec.Encode(r);
            Assert.True(ReplayCodec.TryDecode(bytes, out var back, out var err), err);
            Assert.Equal(b, back!.Besieger); Assert.Equal(3, back.Version);
            Assert.True(ReplayCodec.Encode(back).AsSpan().SequenceEqual(bytes));
            if (b is not null) Assert.NotEqual(ReplayCodec.Digest(r), ReplayCodec.Digest(r with { Besieger = b == Side.Player ? Side.Enemy : Side.Player }));
        }
        var bad = ReplayCodec.Encode(new BattleReplay(3, "x", 1, 1, [new ReplayFrame(0, [], [])], new BattleResult(BattleStatus.Draw, 0, [], []), Side.Player));
        // the besieger byte follows the tick limit: magic(3) version(1) arena(2 bytes: length + 'x') seed(1) limit(1) -> index 8
        bad[8] = 9;
        Assert.False(ReplayCodec.TryDecode(bad, out _, out var why)); Assert.Contains("besieger", why);
    }

    // ---- the roles swap in the pipeline ----

    /// <summary>A battle section that calls every arena a siege and remembers the setups it was handed (the stub has no siege arena).</summary>
    sealed class SiegeEverywhere(IBattleEngine inner) : IBattleEngine
    {
        public List<BattleSetup> Setups { get; } = [];
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas.Select(a => a with { SiegeClock = 14 }).ToList();
        public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) { Setups.Add(setup); return inner.StartAsync(setup, ct); }
    }

    /// <summary>Over eight rounds the player besieges in exactly half of them, alternating, and with the siege switched off nobody besieges.</summary>
    public static async Task<string?> TheRolesSwapEveryRound(DoctrineDomain rules)
    {
        var engine = new SiegeEverywhere(new StubBattleEngine());
        var game = DoctrineBuilder.New().WithBattle(engine).WithRules(rules).Build();
        await game.PlayAsync(1234);
        var seq = engine.Setups.Select(s => s.Besieger).ToList();
        if (seq.Count != 8) return $"expected 8 battles, saw {seq.Count}";
        if (seq.Any(b => b is null)) return "a siege arena was fought with no besieger named";
        if (seq.Count(b => b == Side.Player) != 4) return $"the player besieged in {seq.Count(b => b == Side.Player)} of 8 rounds, expected 4";
        for (int r = 0; r < 8; r++) if (seq[r] != ((r & 1) == 0 ? Side.Player : Side.Enemy)) return $"round {r}: the besieger is {seq[r]}, expected the roles to swap every round";
        return null;
    }

    [Fact] public async Task In_the_pipeline_the_roles_swap_every_round() => Assert.Null(await TheRolesSwapEveryRound(new DoctrineDomain { MaxRounds = 8 }));
    [Fact] public async Task Roles_that_never_swap_are_caught() => Assert.NotNull(await TheRolesSwapEveryRound(new DoctrineDomain { MaxRounds = 8, SwapSiegeRoles = false }));

    [Fact]
    public async Task With_the_siege_switched_off_no_battle_has_a_besieger()
    {
        var engine = new SiegeEverywhere(new StubBattleEngine());
        await DoctrineBuilder.New().WithBattle(engine).WithRules(new DoctrineDomain { MaxRounds = 8, Siege = false }).Build().PlayAsync(1234);
        Assert.All(engine.Setups, s => Assert.Null(s.Besieger));
    }

    [Fact]
    public async Task The_real_catalogue_names_a_besieger_only_in_the_two_siege_arenas()
    {
        var recorder = new RecordingEngine(TracerWorlds.Engine());
        var game = DoctrineBuilder.New().WithBattle(recorder).WithRules(new DoctrineDomain { MaxRounds = 8 }).Build();
        for (int seed = 1; seed <= 6; seed++) await game.PlayAsync(seed);
        Assert.NotEmpty(recorder.Setups);
        Assert.All(recorder.Setups, s => Assert.Equal(s.ArenaId is "chokepoint" or "falling-gate", s.Besieger is not null));
        Assert.Contains(recorder.Setups, s => s.Besieger == Side.Player); Assert.Contains(recorder.Setups, s => s.Besieger == Side.Enemy);
    }

    sealed class RecordingEngine(IBattleEngine inner) : IBattleEngine
    {
        public List<BattleSetup> Setups { get; } = [];
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
        public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) { Setups.Add(setup); return inner.StartAsync(setup, ct); }
    }
}
