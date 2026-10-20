namespace Doctrine.Tests;

// THE OVERMAP, slice 1. Pre-registered in Doctrine\todo\overmap-slice1-2026-10-20.md. Every rule is a function Check(probe) that returns null when the rule holds and a
// reason when it does not; the real test asserts null with no probe, and a mutant test turns ONE probe on and asserts the reason is NOT null (a test nobody has watched fail
// is an assertion). Battles are forced through ForcedEngine so a rule is tested against a known win or loss; two tests play real battles.

/// <summary>A battle engine whose battles are decided at the start by the rig's script (empty script: the player wins). Losses: a quarter of the attacker, half of the defender.</summary>
sealed class ForcedEngine(IBattleEngine inner, CampaignRig rig, bool force) : IBattleEngine
{
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct)
    {
        rig.Setups.Add(setup);
        if (!force) return await inner.StartAsync(setup, ct);
        var outcome = rig.Script.Count > 0 ? rig.Script.Dequeue() : BattleStatus.AttackerWon;
        return new ForcedSession(await inner.StartAsync(setup, ct), outcome, setup);
    }
}

sealed class ForcedSession(IBattleSession s, BattleStatus outcome, BattleSetup setup) : IBattleSession
{
    public int Tick => s.Tick;
    public BattleStatus Status => outcome;
    public ValueTask FieldOrdersAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask MoveAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask EngageAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask ApplyLossesAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ReplayFrame Snapshot() => s.Snapshot();
    public ReplayFrame Record() => s.Record();
    public BattleResult Finish(bool tickLimitReached) => new(outcome, s.Tick,
        setup.Attacker.Cohorts.Select(c => new CohortLoss(c.Key, c.Slot, c.Count / 4)).ToList(),
        setup.Defender.Cohorts.Select(c => new CohortLoss(c.Key, c.Slot, c.Count / 2)).ToList());
}

/// <summary>The enemy's lane by a fixed list (epoch mod length), so a test knows where the enemy goes.</summary>
sealed class FixedLanes(params int[] lanes) : ILaneOpponent
{
    public List<LaneTurn> Asked { get; } = [];
    public ValueTask<int> PickAsync(LaneTurn turn, CancellationToken ct) { Asked.Add(turn); return ValueTask.FromResult(lanes[turn.Epoch % lanes.Length]); }
}

/// <summary>Watches the enemy army of a lane at the point where the player chooses (before the battle's orders).</summary>
sealed class WatchingPlayer(CampaignRig rig) : IPlayerInput
{
    readonly IPlayerInput _in = new ScriptedPlayer(1);
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => _in.DraftAsync(army, round, ct);
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct) => _in.DraftAsync(army, offer, round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => _in.ChooseAsync(army, ghost, round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
    {
        if (rig.Campaign is { } c) rig.AtChoose.Add(c.EnemyArmy(rig.FightLane));
        return _in.ChooseAsync(army, ghost, offer, round, ct);
    }
    public ValueTask<bool> RerollAsync(ArmyReadModel army, OrderOffer offer, int round, CancellationToken ct) => _in.RerollAsync(army, offer, round, ct);
}

sealed class CampaignRig
{
    public readonly Queue<BattleStatus> Script = new();
    public readonly List<BattleSetup> Setups = [];
    public readonly List<ArmyReadModel> AtChoose = [];
    public int FightLane;
    public CampaignGame? Campaign;
    public DoctrineGame Game { get; }

    public CampaignRig(OfferRules? offers = null, IBattleEngine? engine = null, bool forced = true)
    {
        IBattleEngine e = engine ?? new StubBattleEngine();
        e = new ForcedEngine(e, this, forced);
        Game = DoctrineBuilder.New().WithArmy(new PleromaArmyService()).WithBattle(e).WithPlayer(new WatchingPlayer(this))
            .WithRules(new DoctrineDomain { Offers = offers ?? OfferRules.Default }).Build();
    }

    public async Task<CampaignGame> Start(int seed, CampaignRules? rules = null, ILaneOpponent? lanes = null, CampaignProbe probe = CampaignProbe.None)
        => Campaign = await Game.NewCampaignAsync(seed, rules ?? new CampaignRules(), lanes, probe);

    public async Task<EpochResult> Play(int lane, bool concede = false, params BattleStatus[] outcomes)
    {
        foreach (var o in outcomes) Script.Enqueue(o);
        FightLane = lane;
        return await Campaign!.PlayEpochAsync(new LanePick(lane, concede));
    }
}

public class CampaignTests
{
    const BattleStatus W = BattleStatus.AttackerWon, L = BattleStatus.DefenderWon;
    static readonly int[] BaseKinds = [3, 5, 7];

    // an independent statement of what each feature opens (the test does not call UnlockPools)
    static int[] KindsExpected(Feature held)
    {
        var s = new SortedSet<int>(BaseKinds);
        if (held.HasFlag(Feature.Barracks)) { s.Add(0); s.Add(1); }
        if (held.HasFlag(Feature.Stables)) { s.Add(2); s.Add(6); }
        if (held.HasFlag(Feature.Shrine)) s.Add(4);
        return s.ToArray();
    }
    static Feature HeldBy(IEnumerable<Province> provs) => provs.Where(p => p.Owner == Holder.Player).Aggregate(Feature.None, (a, p) => a | p.Feature);
    static long Wealth(ArmyReadModel a) => a.Gold + a.Cohorts.Sum(c => (long)c.Count * Roster.PriceOf(c.Key));
    static string Sig(IEnumerable<Province> ps) => string.Join(";", ps.Select(p => $"{p.Key}:{p.Owner}:{p.Feature}:{p.ArenaIndex}:{p.Yield}"));

    // ================= the map =================

    static async Task<string?> DeterminismError(CampaignProbe probe)
    {
        foreach (int seed in new[] { 1, 7, 99 })
        {
            var a = await new CampaignRig().Start(seed, probe: probe); var b = await new CampaignRig().Start(seed, probe: probe);
            if (Sig(a.Provinces()) != Sig(b.Provinces())) return $"seed {seed}: two campaigns of one seed drew different maps";
        }
        return null;
    }
    [Fact] public async Task The_map_is_a_pure_function_of_the_seed() => Assert.Null(await DeterminismError(CampaignProbe.None));
    [Fact] public async Task MUTANT_a_map_that_is_not_deterministic_is_caught() => Assert.NotNull(await DeterminismError(CampaignProbe.MapNotDeterministic));

    [Fact]
    public async Task Different_seeds_draw_different_maps_and_the_shape_is_two_capitals_and_three_lanes_of_three()
    {
        var sigs = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++)
        {
            var c = await new CampaignRig().Start(seed);
            var ps = c.Provinces();
            sigs.Add(Sig(ps));
            Assert.Equal(11, ps.Count);
            Assert.Equal(2, ps.Count(p => p.IsCapital));
            for (int lane = 0; lane < 3; lane++)
            {
                var line = ps.Where(p => !p.IsCapital && p.Lane == lane).OrderBy(p => p.Index).ToList();
                Assert.Equal(new[] { 0, 1, 2 }, line.Select(p => p.Index));
                Assert.Equal(new[] { Holder.Player, Holder.Neutral, Holder.Enemy }, line.Select(p => p.Owner));
                Assert.All(line, p => Assert.InRange(p.ArenaIndex, 0, 2));        // the stub has three arenas
                Assert.All(ps, p => Assert.True(p.Yield > 0));
            }
            // each middle province is worth fighting for: Barracks, Stables, Shrine, one per lane
            var mids = ps.Where(p => !p.IsCapital && p.Index == 1).Select(p => p.Feature).OrderBy(f => f).ToList();
            Assert.Equal(new[] { Feature.Barracks, Feature.Stables, Feature.Shrine }, mids);
        }
        Assert.True(sigs.Count > 8, "twenty seeds should draw many different maps");
    }

    static string? SymmetryError(CampaignProbe probe)
    {
        var rules = new CampaignRules();
        for (int seed = 0; seed < 60; seed++)
        {
            var m = MapGen.Initial(seed, 4, rules, false, probe);
            for (int lane = 0; lane < 3; lane++)
            {
                var home = m.Single(s => s.Key == MapKeys.Key(lane, 0)); var far = m.Single(s => s.Key == MapKeys.Key(lane, 2));
                // a seat swap maps province i to province 2-i and Player to Enemy: the two ends must be the same ground
                if (home.ArenaIndex != far.ArenaIndex || home.Feature != far.Feature || home.Yield != far.Yield) return $"seed {seed} lane {lane}: the ends of the lane differ";
                if (home.Owner != Holder.Player || far.Owner != Holder.Enemy) return $"seed {seed} lane {lane}: the ends are not held by opposite sides";
            }
            var pc = m.Single(s => s.Key == MapKeys.PlayerCapital); var ec = m.Single(s => s.Key == MapKeys.EnemyCapital);
            if (pc.ArenaIndex != ec.ArenaIndex || pc.Yield != ec.Yield) return $"seed {seed}: the capitals differ";
        }
        return null;
    }
    [Fact] public void The_map_is_symmetric_under_a_seat_swap() => Assert.Null(SymmetryError(CampaignProbe.None));
    [Fact] public void MUTANT_a_map_whose_far_end_differs_is_caught() => Assert.NotNull(SymmetryError(CampaignProbe.MapNotSymmetric));

    // ================= the sealed pick =================

    static async Task<string?> SealedPickError(CampaignProbe probe)
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            var seen = new List<int>();
            for (int pick = 0; pick < 3; pick++)
            {
                var rig = new CampaignRig(); var c = await rig.Start(seed, probe: probe);
                var r = await rig.Play(pick);
                seen.Add(r.EnemyLane);
                if (r.EnemyLane != ScriptedLanes.Lane(seed, 0) && probe == CampaignProbe.None) return "the enemy lane is not the seeded one";
            }
            if (seen.Distinct().Count() != 1) return $"seed {seed}: the enemy's lane followed the player's pick ({string.Join(",", seen)})";
        }
        return null;
    }
    [Fact] public async Task The_enemys_lane_does_not_depend_on_the_players_pick() => Assert.Null(await SealedPickError(CampaignProbe.None));
    [Fact] public async Task MUTANT_an_enemy_that_reads_the_sealed_pick_is_caught() => Assert.NotNull(await SealedPickError(CampaignProbe.EnemyPeeks));

    [Fact]
    public async Task The_lane_opponent_is_asked_once_per_epoch_with_only_the_seed_and_the_epoch()
    {
        var rig = new CampaignRig(); var lanes = new FixedLanes(0, 1, 2);
        await rig.Start(5, lanes: lanes);
        await rig.Play(0); await rig.Play(0, concede: true); await rig.Play(2);
        Assert.Equal(new[] { new LaneTurn(5, 0), new LaneTurn(5, 1), new LaneTurn(5, 2) }, lanes.Asked);
    }

    // ================= the epoch: battle, race, concede =================

    [Fact]
    public async Task The_same_lane_is_a_battle_and_the_winner_takes_the_next_province()
    {
        var rig = new CampaignRig(); var c = await rig.Start(3, lanes: new FixedLanes(1));
        var before = c.Provinces();
        var r = await rig.Play(1);
        Assert.Equal(EpochKind.Battle, r.Kind);
        Assert.NotNull(r.Battle);
        Assert.Single(rig.Setups);
        var taken = Assert.Single(r.Changes);
        Assert.Equal(MapKeys.Key(1, 1), taken.Key); Assert.Equal(Holder.Neutral, taken.From); Assert.Equal(Holder.Player, taken.To);
        Assert.Equal(5, r.Score);                                      // one field won plus four provinces held (three homes and the one just taken)
        Assert.Equal(1, c.FieldsWon);
        // the fight was against the lane's own army, on the target's own ground
        Assert.Equal(c.Provinces().Single(p => p.Key == MapKeys.Key(1, 1)).ArenaIndex, rig.Game.Context.Battle.Arenas.ToList().FindIndex(a => a.Id == rig.Setups[0].ArenaId));
    }

    [Fact]
    public async Task Different_lanes_are_a_race_both_advance_and_no_battle_is_fought()
    {
        var rig = new CampaignRig(); var c = await rig.Start(3, lanes: new FixedLanes(2));
        var r = await rig.Play(0);
        Assert.Equal(EpochKind.Race, r.Kind);
        Assert.Null(r.Battle); Assert.Empty(rig.Setups);
        Assert.Equal(2, r.Changes.Count);
        Assert.Contains(r.Changes, x => x.Key == MapKeys.Key(0, 1) && x.To == Holder.Player);
        Assert.Contains(r.Changes, x => x.Key == MapKeys.Key(2, 1) && x.To == Holder.Enemy);
        Assert.Equal(3, c.Standards);                                  // a race costs nothing
    }

    static async Task<string?> ConcedeError(CampaignProbe probe)
    {
        var rig = new CampaignRig(); var c = await rig.Start(3, lanes: new FixedLanes(0), probe: probe);
        var r = await rig.Play(0, concede: true);
        if (r.Kind != EpochKind.Conceded || r.Battle is not null || rig.Setups.Count != 0) return "a concession fought a battle";
        if (r.Changes.Count != 1 || r.Changes[0].To != Holder.Enemy || r.Changes[0].Key != MapKeys.Key(0, 1)) return "the enemy did not take the ground it came for";
        if (c.Standards != 3 || r.StandardsAfter != 3) return $"conceding cost a standard ({c.Standards} left)";
        if (c.FieldsWon != 0) return "conceding won a field";
        return null;
    }
    [Fact] public async Task Conceding_a_lane_costs_ground_and_never_a_standard() => Assert.Null(await ConcedeError(CampaignProbe.None));
    [Fact] public async Task MUTANT_a_concession_that_costs_a_standard_is_caught() => Assert.NotNull(await ConcedeError(CampaignProbe.ConcedeCostsStandard));

    [Fact]
    public async Task A_conceded_lane_the_enemy_did_not_come_to_loses_nothing()
    {
        var rig = new CampaignRig(); var c = await rig.Start(3, lanes: new FixedLanes(1));
        var r = await rig.Play(0, concede: true);                      // the enemy goes to lane 1: it takes ground there (an unopposed advance), the player pushed nowhere
        Assert.Equal(EpochKind.Conceded, r.Kind);
        Assert.Equal(new[] { MapKeys.Key(1, 1) }, r.Changes.Select(x => x.Key));
        Assert.Equal(3, c.Standards);
    }

    [Fact]
    public async Task A_refusal_is_data_and_changes_nothing()
    {
        var rig = new CampaignRig(); var c = await rig.Start(3, lanes: new FixedLanes(0));
        var bad = await c.PlayEpochAsync(new LanePick(7));
        Assert.Equal(EpochKind.Refused, bad.Kind); Assert.NotNull(bad.Refused); Assert.Equal(0, c.Epoch);
        await rig.Play(0, false, W, W, W);                              // fixed lane 0: p1
        await rig.Play(0); await rig.Play(0);                           // p2, then the capital: the war is won
        Assert.Equal(CampaignOutcome.WarWon, c.Outcome);
        var after = await c.PlayEpochAsync(new LanePick(0));
        Assert.Equal(EpochKind.Refused, after.Kind);
    }

    // ================= standards =================

    static string? StandardsError(CampaignProbe probe)
    {
        var rules = new CampaignRules();
        for (int seed = 0; seed < 40; seed++)
        {
            var store = new CampaignStore(MapGen.Initial(seed, 4, rules, false), probe);
            var rng = new Random(seed);
            int std = 3, wins = 0;                                      // the model, written from the rule: a loss costs one; every fifth win restores one, at most three
            for (int step = 0; step < 120 && std > 1; step++)
            {
                bool win = rng.Next(100) < 70;
                if (win) { store.WinField(); wins++; if (wins % 5 == 0) std = Math.Min(3, std + 1); }
                else { store.LoseStandard(); std--; }
                if (store.Standards != std) return $"seed {seed} step {step} ({(win ? "win" : "loss")}): the store holds {store.Standards} standards, the rule says {std}";
                if (store.FieldsWon != wins) return $"seed {seed} step {step}: fields won {store.FieldsWon}, expected {wins}";
            }
        }
        return null;
    }
    [Fact] public void Standards_follow_the_rule_a_loss_costs_one_every_fifth_win_restores_one_at_most_three() => Assert.Null(StandardsError(CampaignProbe.None));
    [Fact] public void MUTANT_a_restore_every_fourth_win_is_caught() => Assert.NotNull(StandardsError(CampaignProbe.RestoreEveryFourth));
    [Fact] public void MUTANT_a_restore_past_the_maximum_is_caught() => Assert.NotNull(StandardsError(CampaignProbe.RestoreIgnoresMax));
    [Fact] public void MUTANT_a_loss_that_costs_nothing_is_caught() => Assert.NotNull(StandardsError(CampaignProbe.LossCostsNothing));

    [Fact]
    public async Task A_lost_battle_costs_one_standard_through_the_pipeline_and_the_fifth_win_restores_it_and_zero_ends_the_run()
    {
        var rig = new CampaignRig(); var c = await rig.Start(11, new CampaignRules { Endless = true }, new FixedLanes(0));
        var r = await rig.Play(0, false, L);
        Assert.Equal(2, c.Standards); Assert.Equal(1, r.StandardsBefore - r.StandardsAfter);
        Assert.Equal(CampaignOutcome.Running, r.Outcome);
        for (int i = 0; i < 4; i++) { r = await rig.Play(0, false, W); Assert.Equal(2, c.Standards); Assert.False(r.Restored); }
        r = await rig.Play(0, false, W);                                // the fifth field won
        Assert.True(r.Restored); Assert.Equal(3, c.Standards);
        for (int i = 0; i < 5; i++) await rig.Play(0, false, W);        // ten won: no more than three
        Assert.Equal(3, c.Standards);
        // three losses end the run, in different lanes so no capital falls
        var rig2 = new CampaignRig(); var c2 = await rig2.Start(11, new CampaignRules(), new FixedLanes(0, 1, 2));
        await rig2.Play(0, false, L); await rig2.Play(1, false, L);
        var last = await rig2.Play(2, false, L);
        Assert.Equal(0, c2.Standards); Assert.Equal(CampaignOutcome.OutOfStandards, last.Outcome); Assert.True(c2.Over);
        Assert.Equal(EpochKind.Refused, (await c2.PlayEpochAsync(new LanePick(0))).Kind);
    }

    [Fact]
    public async Task A_drawn_battle_moves_nothing_and_costs_nothing()
    {
        var rig = new CampaignRig(); var c = await rig.Start(4, lanes: new FixedLanes(0));
        var before = Sig(c.Provinces());
        var r = await rig.Play(0, false, BattleStatus.Draw);
        Assert.Empty(r.Changes); Assert.Equal(3, c.Standards); Assert.Equal(0, c.FieldsWon); Assert.Equal(before, Sig(c.Provinces()));
    }

    // ================= the war ends at a capital =================

    [Fact]
    public async Task Taking_the_enemy_capital_wins_the_war_and_losing_ours_loses_it()
    {
        var rig = new CampaignRig(); var c = await rig.Start(21, lanes: new FixedLanes(0));
        await rig.Play(0, false, W); await rig.Play(0, false, W);       // p1, p2
        var brief = await c.BriefingAsync();
        Assert.True(brief.Lanes[0].EndsWar); Assert.True(brief.Lanes[0].Target!.IsCapital);
        var r = await rig.Play(0, false, W);
        Assert.Equal(CampaignOutcome.WarWon, r.Outcome);
        Assert.Equal(MapKeys.EnemyCapital, r.Changes.Single().Key);

        var rig2 = new CampaignRig(); var c2 = await rig2.Start(21, lanes: new FixedLanes(0));
        await rig2.Play(0, false, L); await rig2.Play(0, false, L);     // the enemy takes p1, then p0
        var lost = await rig2.Play(0, false, L);                        // the capital: the third standard goes too, the capital decides the war
        Assert.Equal(CampaignOutcome.WarLost, lost.Outcome);
    }

    // ================= hold: gold =================

    [Fact]
    public async Task A_held_province_pays_its_yield_at_epoch_close()
    {
        var rig = new CampaignRig(); var c = await rig.Start(8, lanes: new FixedLanes(0));
        for (int e = 0; e < 4; e++)
        {
            long g0 = c.Army.Read().Gold;
            var r = await rig.Play(e % 2, concede: true);                // a pass: no battle, so the gold moves only by interest and the yield
            if (c.Over) { Assert.Equal(0, r.GoldPaid); break; }           // a finished war pays nothing
            long expected = c.Provinces().Where(p => p.Owner == Holder.Player).Sum(p => (long)p.Yield);   // capitals pay too
            Assert.True(expected > 0);
            Assert.Equal(expected, r.GoldPaid);
            Assert.Equal(g0 + Roster.Interest(g0) + expected, c.Army.Read().Gold);
        }
    }

    // ================= unlock on hold =================

    static async Task<string?> UnlockError(CampaignProbe probe)
    {
        var rig = new CampaignRig(); var c = await rig.Start(14, lanes: new FixedLanes(0), probe: probe);
        string? Check(CampaignBriefing b, string at)
        {
            var want = KindsExpected(HeldBy(c.Provinces()));
            return b.OfferKinds.SequenceEqual(want) ? null : $"{at}: the offer draws from [{string.Join(",", b.OfferKinds)}], holding the provinces it should draw from [{string.Join(",", want)}]";
        }
        if (Check(await c.BriefingAsync(), "start") is { } e0) return e0;
        var mid = c.Provinces().Single(p => p.Key == MapKeys.Key(0, 1)).Feature;
        await rig.Play(0, false, W);                                    // take the lane's middle province
        if (Check(await c.BriefingAsync(), "after taking it") is { } e1) return e1;
        if (!(await c.BriefingAsync()).HeldUnlocks.HasFlag(mid)) return "the taken feature is not held";
        var fight = await rig.Play(0, false, L);                        // the offer of THIS epoch still has the unlock (the epoch opened holding it)
        var offered = fight.Battle!.UnitOffer!.Packs.Select(p => p.Kind).ToList();
        if (offered.Any(k => !KindsExpected(mid).Contains(k))) return "the epoch that lost the province offered a kind from outside its pool";
        if (c.Provinces().Single(p => p.Key == MapKeys.Key(0, 1)).Owner != Holder.Enemy) return "the province was not lost";
        if (Check(await c.BriefingAsync(), "after losing it") is { } e2) return e2;
        // the fight after the loss offers nothing from the lost feature; soldiers raised before stay in the army
        var next = await rig.Play(0, false, W);
        var kinds = next.Battle!.UnitOffer!.Packs.Select(p => p.Kind);
        if (kinds.Any(k => !BaseKinds.Contains(k))) return "a kind of the lost province was offered after the loss";
        return null;
    }
    [Fact] public async Task A_held_feature_opens_its_kinds_and_losing_the_province_takes_them_from_the_next_offer() => Assert.Null(await UnlockError(CampaignProbe.None));
    [Fact] public async Task MUTANT_an_unlock_kept_after_the_province_is_lost_is_caught() => Assert.NotNull(await UnlockError(CampaignProbe.UnlockKeptAfterLoss));

    [Fact]
    public async Task Every_feature_opens_what_it_says_and_the_aura_order_needs_a_shrine()
    {
        Assert.Equal(BaseKinds, UnlockPools.Kinds(Feature.None));
        Assert.Equal(KindsExpected(Feature.Barracks), UnlockPools.Kinds(Feature.Barracks));
        Assert.Equal(KindsExpected(Feature.Stables), UnlockPools.Kinds(Feature.Stables));
        Assert.Equal(KindsExpected(Feature.Shrine), UnlockPools.Kinds(Feature.Shrine));
        Assert.Equal(BaseKinds, UnlockPools.Kinds(Feature.Granary));        // a granary pays gold, it opens no kind
        Assert.Equal(KindsExpected(Feature.Barracks | Feature.Stables | Feature.Shrine), UnlockPools.Kinds(Feature.Barracks | Feature.Stables | Feature.Shrine));
        int aura = DoctrineVocabulary.Army(ArmyOrderKind.Aura, 0), flip = DoctrineVocabulary.Army(ArmyOrderKind.Flip, 0);
        Assert.False(UnlockPools.OrderOpen(Feature.None, aura)); Assert.False(UnlockPools.OrderOpen(Feature.Barracks | Feature.Stables | Feature.Granary, aura));
        Assert.True(UnlockPools.OrderOpen(Feature.Shrine, aura));
        Assert.True(UnlockPools.OrderOpen(Feature.None, flip));             // everything else is always in the pool
        // through the game: with nothing held, no offer in a fought battle has an Aura token or a kind outside the base kinds
        var rig = new CampaignRig(); var c = await rig.Start(33, lanes: new FixedLanes(0, 1));
        var r1 = await rig.Play(0, false, L); var r2 = await rig.Play(1, false, L);
        foreach (var r in new[] { r1, r2 })
        {
            Assert.All(r.Battle!.OrderOffer!.Tokens, t => Assert.False(DoctrineVocabulary.Decode(t) is { Family: OrderFamily.Army } o && o.Kind == (int)ArmyOrderKind.Aura));
            Assert.All(r.Battle.UnitOffer!.Packs, p => Assert.Contains(p.Kind, BaseKinds));
        }
    }

    [Fact]
    public async Task With_the_offers_off_the_campaign_is_the_open_market_and_the_unlocks_widen_nothing()
    {
        var rig = new CampaignRig(OfferRules.Off); var c = await rig.Start(33, lanes: new FixedLanes(0));
        var r = await rig.Play(0, false, W);
        Assert.False(r.Battle!.UnitOffer!.Active); Assert.False(r.Battle.OrderOffer!.Active);
        Assert.Equal(0, r.Battle.OfferRefused);
    }

    [Fact]
    public async Task A_campaign_battle_is_the_round_pipeline_unchanged_a_scenario_equal_to_a_round_plays_the_same_battle()
    {
        // The plumbing must not touch the fixed games: round 0 of a normal game and the same round run with a FieldScenario that names the same arena, besieger, enemy and offers
        // leave the same recording, to the digest. (The fixed-game digest pins in DigestPinTests are the other half of this proof.)
        const int seed = 77;
        DoctrineGame Fresh() => DoctrineBuilder.New().WithArmy(new PleromaArmyService()).WithBattle(TracerWorlds.Engine()).WithPlayer(new ScriptedPlayer(1))
            .WithRules(new DoctrineDomain { MaxRounds = 1 }).Build();
        var legacy = await Fresh().PlayAsync(seed);
        var g = Fresh();
        var arena = RoundRules.ArenaFor(g.Context.Battle.Arenas, seed, 0);
        var enemy = await RoundRules.RaiseEnemyAsync(g.Context.Armies, g.Context.Opponent, g.Rules, seed, 0, default);
        var scenario = new FieldScenario(arena.Id, RoundRules.BesiegerFor(g.Rules, arena, 0), 0, enemy, g.Rules.Offers);
        var run = await g.Context.Round.RunAsync(new RoundState(0, seed, g.NewGame(seed).Player, Field: scenario), default);
        var rec = PipelineFailure.DataOrThrow(run, "scenario").Records[0];
        Assert.Equal(ReplayCodec.Digest(legacy.Rounds[0].Replay), ReplayCodec.Digest(rec.Replay));
        Assert.Equal(legacy.Rounds[0].PlayerDigest, rec.PlayerDigest);
        Assert.Equal(legacy.Rounds[0].EnemyDigest, rec.EnemyDigest);
        Assert.Equal(legacy.Rounds[0].Spoils, rec.Spoils);
        Assert.True(legacy.Rounds[0].Final); Assert.False(rec.Final);       // a campaign battle is not the end of a game
    }

    // ================= the briefing equals the battle =================

    static async Task<string?> BriefingError(CampaignProbe probe, IBattleEngine? engine, int epochs)
    {
        var rig = new CampaignRig(engine: engine, forced: engine is null);
        var c = await rig.Start(5, new CampaignRules { Endless = true }, new FixedLanes(0, 1, 2, 2, 1, 0), probe);
        var spy = new List<CampaignBriefing>();
        for (int e = 0; e < epochs; e++)
        {
            var b = await c.BriefingAsync(); spy.Add(b);
            int lane = new[] { 0, 1, 2, 2, 1, 0 }[e % 6];
            var lb = b.Lanes[lane];
            rig.AtChoose.Clear();
            var r = await rig.Play(lane, false, e % 3 == 2 ? L : W);
            if (c.Over) break;
            if (r.Kind != EpochKind.Battle) return $"epoch {e}: expected a battle";
            var s = rig.Setups[^1];
            if (s.ArenaId != lb.Arena!.Info.Id) return $"epoch {e}: the briefing said arena {lb.Arena.Info.Id}, the battle was on {s.ArenaId}";
            if (s.Besieger != lb.Arena.Besieger) return $"epoch {e}: the briefing said besieger {lb.Arena.Besieger}, the battle {s.Besieger}";
            if (r.Battle!.Replay.Besieger != lb.Arena.Besieger) return $"epoch {e}: the recording's besieger differs from the briefing";
            var at = rig.AtChoose.Single();
            if (!at.Cohorts.SequenceEqual(lb.Enemy!.Cohorts) || at.Soldiers != lb.Enemy.Soldiers) return $"epoch {e}: the enemy at the choose point is not the enemy in the briefing";
            if (lb.Enemy.Gold != new CampaignRules { Endless = true }.EnemyBaseGold + 4 * lb.Target!.Index + 3 * e) return $"epoch {e}: the briefing's scheduled enemy gold is off the curve";
            if (s.Defender.Cohorts.Sum(x => x.Count) != lb.Enemy.Soldiers) return $"epoch {e}: the enemy brought {s.Defender.Cohorts.Sum(x => x.Count)} soldiers, the briefing said {lb.Enemy.Soldiers}";
        }
        return null;
    }
    [Fact] public async Task The_briefing_equals_what_the_battle_later_uses_forced_battles() => Assert.Null(await BriefingError(CampaignProbe.None, null, 9));
    [Fact] public async Task The_briefing_equals_what_the_battle_later_uses_real_battles_on_the_tracer_engine() => Assert.Null(await BriefingError(CampaignProbe.None, TracerWorlds.Engine(), 3));
    [Fact] public async Task MUTANT_a_briefing_that_names_the_wrong_arena_is_caught() => Assert.NotNull(await BriefingError(CampaignProbe.BriefingArenaWrong, null, 9));

    sealed class RecordingEngine(IBattleEngine inner) : IBattleEngine
    {
        public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
        public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct) => inner.StartAsync(setup, ct);
    }

    // ================= the enemy's losses persist =================

    static async Task<string?> PersistenceError(CampaignProbe probe)
    {
        var rig = new CampaignRig(); var c = await rig.Start(9, new CampaignRules { Endless = true }, new FixedLanes(0), probe);
        long w0 = -1; long lost0 = 0; var heal = new CampaignRules().EnemyHealGold;
        for (int battle = 0; battle < 2; battle++)
        {
            rig.AtChoose.Clear();
            var r = await rig.Play(0, false, W);
            long here = Wealth(rig.AtChoose.Single());
            if (battle > 0 && here > w0 - lost0 + heal + 4) return $"battle {battle}: the lane's army came back with wealth {here}; it had {w0}, lost {lost0}, and heals at most {heal}";
            w0 = here; lost0 = r.Battle!.Result.DefenderLosses.Sum(l => (long)l.Lost * Roster.PriceOf(l.Key));
            if (lost0 <= heal + 4) return $"battle {battle}: the fight took too little ({lost0}) to tell persistence from a fresh levy";
        }
        return null;
    }
    [Fact] public async Task A_beaten_enemy_army_returns_weaker_on_the_same_lane() => Assert.Null(await PersistenceError(CampaignProbe.None));
    [Fact] public async Task MUTANT_an_enemy_that_is_levied_afresh_each_epoch_is_caught() => Assert.NotNull(await PersistenceError(CampaignProbe.EnemyRecreated));

    [Fact]
    public async Task An_enemy_lane_nobody_fights_musters_back_only_up_to_its_schedule()
    {
        var rig = new CampaignRig(); var c = await rig.Start(9, lanes: new FixedLanes(0));
        long w = Wealth(c.EnemyArmy(2)); var rl = new CampaignRules();
        for (int e = 0; e < 6; e++) { await rig.Play(0, false, W); if (c.Over) break; }
        // lane 2 was never fought: it did not snowball (the schedule caps the heal), it stays near where it started
        long after = Wealth(c.EnemyArmy(2));
        Assert.True(after <= Math.Max(w, Roster.ExpectedEnemyWealth(rl.ScheduledGold(1, 6)) + rl.EnemyHealGold), $"lane 2 wealth {after} from {w}");
    }

    // ================= endless =================

    [Fact]
    public async Task Endless_refills_a_cleared_lane_has_no_enemy_capital_and_the_enemy_grows_each_epoch_without_a_cap()
    {
        var rules = new CampaignRules { Endless = true };
        var rig = new CampaignRig(); var c = await rig.Start(19, rules, new FixedLanes(0));
        Assert.DoesNotContain(c.Provinces(), p => p.Key == MapKeys.EnemyCapital);
        for (int e = 0; e < 8; e++)
        {
            var b = await c.BriefingAsync();
            var t = b.Lanes[0].Target!;
            Assert.False(b.Lanes[0].EndsWar); Assert.False(t.IsCapital);
            Assert.Equal(rules.EnemyBaseGold + rules.EnemyStepGold * t.Index + rules.EnemyEpochGold * e, b.Lanes[0].Enemy!.Gold);
            var r = await rig.Play(0, false, W);
            Assert.Equal(CampaignOutcome.Running, r.Outcome);
        }
        Assert.Equal(8, c.FieldsWon);
        var line = c.Provinces().Where(p => !p.IsCapital && p.Lane == 0).ToList();
        Assert.Equal(10, line.Count);                                  // the map scrolled: win k >= 2 leaves k + 2 provinces in the lane`r`n        Assert.Equal(9, line.Count(p => p.Owner == Holder.Player)); Assert.Equal(Holder.Enemy, line[^1].Owner);`r`n        // no cap: the schedule at epoch 1000 is still rising
        Assert.True(rules.ScheduledGold(2, 1000) > rules.ScheduledGold(2, 999));
        // a fixed war has no growth with the epoch
        var fixedRules = new CampaignRules();
        Assert.Equal(fixedRules.ScheduledGold(2, 0), fixedRules.ScheduledGold(2, 50));
    }

    // ================= whole campaigns with real battles (stub engine) =================

    static async Task<string> Run(int seed, bool endless)
    {
        var rig = new CampaignRig(forced: false);
        var c = await rig.Start(seed, new CampaignRules { Endless = endless, MaxEpochs = 20 });
        var rng = new GameRng(GameRng.Mix(seed, 5));
        while (!c.Over)
        {
            var b = await c.BriefingAsync();
            int lane = rng.Next(3);
            if (b.Lanes[lane].Target is null) { lane = (lane + 1) % 3; }
            await c.PlayEpochAsync(new LanePick(lane, rng.Next(10) == 0));
        }
        return $"{c.Outcome}|{c.Epoch}|{c.Score}|{c.Standards}|{c.FieldsWon}|{c.Army.ToCode()}|{Sig(c.Provinces())}";
    }

    [Fact]
    public async Task Whole_campaigns_with_real_battles_end_and_are_deterministic()
    {
        foreach (int seed in new[] { 1, 2, 3 })
            foreach (bool endless in new[] { false, true })
            {
                var a = await Run(seed, endless); var b = await Run(seed, endless);
                Assert.Equal(a, b);
                Assert.DoesNotContain("Running", a.Split('|')[0]);
            }
    }
}
