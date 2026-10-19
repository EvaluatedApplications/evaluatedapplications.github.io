namespace Doctrine.Tests;

// Phase 4a: what a HOST (the page) needs from the skeleton. Closed-round visibility, the draft report, the orders report, progress, the
// arena layout capability, and the one rule about who judges a token. Every test here was watched fail against a mutant (see the report).

/// <summary>Wraps any player and records what the pipeline told it, in order.</summary>
sealed class RecordingPlayer(IPlayerInput inner) : IPlayerInput
{
    public List<string> Log { get; } = [];
    public List<DraftReport> Reports { get; } = [];
    public List<(RoundRecord Record, ArmyReadModel After)> Closed { get; } = [];
    public List<GeneralOutput> Ghosts { get; } = [];
    public List<ArmyReadModel> ChooseArmies { get; } = [];
    public Func<RoundRecord, CancellationToken, ValueTask>? OnClosed { get; set; }

    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) { Log.Add($"draft {round}"); return inner.DraftAsync(army, round, ct); }
    public ValueTask DraftedAsync(DraftReport report, CancellationToken ct) { Log.Add("drafted"); Reports.Add(report); return ValueTask.CompletedTask; }
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
    { Log.Add($"choose {round}"); Ghosts.Add(ghost); ChooseArmies.Add(army); return inner.ChooseAsync(army, ghost, round, ct); }
    public async ValueTask RoundClosedAsync(RoundRecord record, ArmyReadModel after, CancellationToken ct)
    {
        Log.Add($"closed {record.Round}"); Closed.Add((record, after));
        if (OnClosed is not null) await OnClosed(record, ct);
    }
}

/// <summary>Records every observer call.</summary>
sealed class RecordingObserver : IRoundObserver
{
    public List<string> Stages { get; } = [];
    public List<(int Tick, int Limit)> Ticks { get; } = [];
    public int Started, Finished;
    public Action<int, int>? OnTick { get; set; }
    public void StageEntered(string stage, int round) => Stages.Add($"{round}:{stage}");
    public void BattleStarted(BattleSetup setup) => Started++;
    public void BattleTick(int tick, int limit) { Ticks.Add((tick, limit)); OnTick?.Invoke(tick, limit); }
    public void BattleFinished(BattleResult result) => Finished++;
}

public class ClosedRoundAndDraftTests
{
    static DoctrineBuilder Builder(IPlayerInput p, int rounds = 5) => DoctrineBuilder.New().WithPlayer(p).WithRules(new DoctrineDomain { MaxRounds = rounds });

    [Fact]
    public async Task The_player_hears_every_closed_round_whole_inside_one_run_of_the_game()
    {
        var p = new RecordingPlayer(new ScriptedPlayer(1));
        var r = await Builder(p).Build().PlayAsync(21);
        Assert.Equal(5, p.Closed.Count);
        for (int i = 0; i < 5; i++)
        {
            var (rec, after) = p.Closed[i];
            Assert.Same(r.Rounds[i], rec);                         // the very record the game keeps: replay, spoils, result and all
            Assert.NotNull(rec.Replay); Assert.NotNull(rec.Spoils); Assert.NotNull(rec.Result);
            Assert.Equal(rec.Round + 1, after.Round);              // the army as the next round finds it: the round has been ended
            Assert.Equal(i == 4, rec.Final);                       // only the last round of the game is final
        }
        // order: each round is drafted, reported, chosen, then closed, and round i closes before round i+1 is drafted
        Assert.Equal(["draft 0", "drafted", "choose 0", "closed 0", "draft 1"], p.Log.Take(5));
    }

    [Fact]
    public async Task Hooks_with_defaults_change_nothing_and_a_listening_player_plays_the_same_game()
    {
        var plain = await Builder(new ScriptedPlayer(1)).Build().PlayAsync(21);
        var heard = await Builder(new RecordingPlayer(new ScriptedPlayer(1))).Build().PlayAsync(21);
        Assert.Equal(plain.Digest(), heard.Digest());
    }

    [Fact]
    public async Task The_draft_report_reaches_the_player_after_the_army_applied_the_picks()
    {
        var p = new RecordingPlayer(new ScriptedPlayer(1));
        var r = await Builder(p, 3).Build().PlayAsync(9);
        Assert.Equal(3, p.Reports.Count);
        Assert.All(p.Reports, rep => Assert.Equal(rep.Accepted.Sum(a => ArmyRules.Default.DraftPrice(a.Kind) * a.Count), rep.GoldSpent));
        Assert.Contains(p.Reports, rep => rep.Accepted.Count > 0);

        // a refused pick comes back WITH its reason: a player that asks for an impossible kind is told so by the army
        var greedy = new RecordingPlayer(new GreedyPlayer());
        await Builder(greedy, 1).Build().PlayAsync(9);
        var report = Assert.Single(greedy.Reports);
        Assert.Contains(report.Refused, x => x.Pick.Kind == 99 && x.Reason.Length > 0);
        Assert.Contains(report.Refused, x => x.Pick.Kind == 0 && x.Pick.Count == 1_000_000 && x.Reason.Length > 0);
    }

    sealed class GreedyPlayer : IPlayerInput
    {
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<DraftPick>>([new DraftPick(99, 1), new DraftPick(0, 1_000_000)]);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => ValueTask.FromResult(new PlayerChoice(DoctrineSet.Empty, true));
    }

    [Fact]
    public async Task A_host_can_stop_the_game_from_inside_a_closed_round_and_the_run_throws_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var p = new RecordingPlayer(new ScriptedPlayer(1)) { OnClosed = (rec, _) => { if (rec.Round == 1) cts.Cancel(); return ValueTask.CompletedTask; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Builder(p, 8).Build().PlayAsync(4, cts.Token));
        Assert.Equal(2, p.Closed.Count);                            // rounds 0 and 1 were closed and delivered; nothing after
    }

    [Fact]
    public async Task The_orders_report_is_in_the_record_and_adds_up_to_the_army_orders_the_player_asked_for()
    {
        var r = await Builder(new ScriptedPlayer(1), 8).Build().PlayAsync(21);
        Assert.All(r.Rounds, x =>
        {
            Assert.NotNull(x.Orders);
            Assert.Equal(DoctrineVocabulary.ArmyOrders(x.PlayerDoctrine).Length, x.Orders!.Applied + x.Orders.Skipped);
        });
        Assert.Contains(r.Rounds, x => x.Orders!.Applied > 0);
    }
}

public class ProgressObserverTests
{
    [Fact]
    public async Task An_observer_hears_every_stage_in_order_and_each_battle_tick_of_the_limit()
    {
        var obs = new RecordingObserver();
        var rules = new DoctrineDomain { MaxRounds = 3, TickLimit = 7 };
        var game = DoctrineBuilder.New().WithObserver(obs).WithRules(rules).Build();
        var r = await game.PlayAsync(12);
        string[] oneRound = [Stages.ArmyDraft, Stages.GeneralSuggest, Stages.PlayerChoose, Stages.GeneralImitate, Stages.ArmyOrders, Stages.BattleSetup,
            Stages.BattleRun, Stages.ArmyLosses, Stages.ArmySpoils, Stages.GeneralOutcome, Stages.RoundClose];
        var expected = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            expected.AddRange(oneRound.Select(s => $"{i}:{s}"));
            if (i < 2) expected.Add($"{i}:{Stages.RoundAdvance}");
        }
        Assert.Equal(expected, obs.Stages);
        Assert.Equal(3, obs.Started); Assert.Equal(3, obs.Finished);
        Assert.Equal(r.Rounds.Sum(x => x.Result.Ticks), obs.Ticks.Count);
        Assert.All(obs.Ticks, t => Assert.Equal(7, t.Limit));
        Assert.All(obs.Ticks, t => Assert.InRange(t.Tick, 1, 7));
        Assert.Equal(Enumerable.Range(1, r.Rounds[0].Result.Ticks), obs.Ticks.Take(r.Rounds[0].Result.Ticks).Select(t => t.Tick));
    }

    [Fact]
    public async Task Watching_changes_nothing_and_a_game_without_an_observer_pays_nothing()
    {
        var plain = await DoctrineBuilder.New().Build().PlayAsync(33);
        var watched = await DoctrineBuilder.New().WithObserver(new RecordingObserver()).Build().PlayAsync(33);
        Assert.Equal(plain.Digest(), watched.Digest());
        Assert.Null(DoctrineBuilder.New().Build().Context.Observer);
    }

    [Fact]
    public async Task A_host_can_cancel_a_battle_from_a_tick_and_the_run_throws_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var obs = new RecordingObserver { OnTick = (tick, _) => { if (tick == 3) cts.Cancel(); } };
        var game = DoctrineBuilder.New().WithObserver(obs).Build();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await game.PlayAsync(5, cts.Token));
        Assert.Equal(3, obs.Ticks.Count);                           // no tick after the cancel
    }

    [Fact]
    public async Task The_observer_can_be_set_on_the_context_after_the_build()
    {
        var game = DoctrineBuilder.New().WithRules(new DoctrineDomain { MaxRounds = 1 }).Build();
        var obs = new RecordingObserver();
        game.Context.Observer = obs;
        await game.PlayAsync(1);
        Assert.NotEmpty(obs.Stages);
    }
}

public class ArenaLayoutCapabilityTests
{
    [Fact]
    public void The_tracer_engine_has_the_capability_and_the_context_asks_it_without_naming_the_type()
    {
        var game = DoctrineBuilder.New().WithDefaults(b => b.WithTracer(TracerWorlds.Scratch()).WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!))).Build();
        Assert.IsAssignableFrom<IArenaLayouts>(game.Context.Battle);
        foreach (var info in game.Context.Battle.Arenas)
        {
            var l = game.Context.LayoutOf(info.Id);
            Assert.NotNull(l);
            Assert.Equal(info.Id, l!.Id);
            Assert.Equal(info.Width, l.Cells[0].Length * l.CellSize);      // cells are in arena units of CellSize
            Assert.Equal(info.Height, l.Cells.Count * l.CellSize);
        }
        Assert.Null(game.Context.LayoutOf("nowhere"));
        var gate = game.Context.LayoutOf("falling-gate")!;
        Assert.Contains(gate.Cells, row => row.Contains('G'));
        Assert.True(gate.GateFallsAtTick > 0);
    }

    [Fact]
    public void An_engine_without_the_capability_answers_null_and_a_wrapper_hides_it_by_design()
    {
        Assert.Null(DoctrineBuilder.New().Build().Context.LayoutOf("open-field"));       // the stub draws nothing
        var inner = new TracerBattleEngine(TracerWorlds.Context());
        var wrapped = DoctrineBuilder.New().WithBattle(new Plain(inner)).Build();
        Assert.Null(wrapped.Context.LayoutOf("falling-gate"));                           // a plain IBattleEngine wrapper is not an IArenaLayouts
    }

    sealed class Plain(IBattleEngine i) : IBattleEngine
    {
        public IReadOnlyList<ArenaInfo> Arenas => i.Arenas;
        public ValueTask<IBattleSession> StartAsync(BattleSetup s, CancellationToken ct) => i.StartAsync(s, ct);
    }
}

/// <summary>
/// WHO JUDGES A TOKEN, end to end through the real pipeline (CONTRACT.md): the START mask binds the General; the PLAYER's army orders go to
/// the army whole and are judged there per token in slot order; the pipeline itself removes only ids outside the vocabulary and field orders
/// the start mask forbids.
/// </summary>
public class WhoJudgesATokenTests
{
    public static TheoryData<string> Armies => new("stub", "pleroma");
    static IArmyService Army(string which) => which == "stub" ? new StubArmyService() : new PleromaArmyService();

    /// <summary>A section whose PLAYER army starts with plenty of gold: a Flip is paid for, and relabelling a whole levy is dear.</summary>
    sealed class RichPlayerArmy(IArmyService inner) : IArmyService
    {
        public IArmy Create(Side side, int seed)
        {
            var a = inner.Create(side, seed);
            if (side == Side.Player) a.ApplySpoilsAsync(new Spoils(2000, 0), default).AsTask().GetAwaiter().GetResult();
            return a;
        }
        public bool TryParse(string code, out IArmy? army, out string error) => inner.TryParse(code, out army, out error);
    }

    /// <summary>Plays one round with a doctrine built from the army the player sees when it chooses; null from <paramref name="make"/> = setup not met.</summary>
    sealed class DoctrinePlayer(Func<ArmyReadModel, DoctrineSet?> make) : IPlayerInput
    {
        public bool SetupMet;
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<DraftPick>>(army.Gold >= 3 ? [new DraftPick(0, 1), new DraftPick(1, 1)] : []);
        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
        {
            var d = make(army); SetupMet = d is not null;
            return ValueTask.FromResult(new PlayerChoice(d ?? DoctrineSet.Empty, true));
        }
    }

    static async Task<RoundRecord?> OneRound(IArmyService army, Func<ArmyReadModel, DoctrineSet?> make)
    {
        for (int seed = 1; seed <= 60; seed++)
        {
            var p = new DoctrinePlayer(make);
            var r = await DoctrineBuilder.New().WithArmy(army).WithPlayer(p).WithRules(new DoctrineDomain { MaxRounds = 1 }).Build().PlayAsync(seed);
            if (p.SetupMet) return r.Rounds[0];
        }
        return null;
    }

    static int ForeignTrait(ArmyReadModel a)
    {
        int held = a.Cohorts.Aggregate(0, (m, c) => m | (int)c.Traits);
        return Enumerable.Range(0, DoctrineVocabulary.Traits).FirstOrDefault(t => (held >> t & 1) == 0, -1);
    }

    /// <summary>Flip a trait nobody holds, then its aura: illegal at the start, legal by its turn.</summary>
    public static DoctrineSet? FlipThenAura(ArmyReadModel a)
    {
        int x = ForeignTrait(a); if (x < 0) return null;
        var aura = DoctrineVocabulary.Army(ArmyOrderKind.Aura, x);
        return a.Legal.Allows(aura) ? null : DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Flip, x), aura);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_token_illegal_at_the_start_but_legal_after_an_earlier_one_applies_reaches_the_army_and_applies(string which)
    {
        var rec = await OneRound(new RichPlayerArmy(Army(which)), FlipThenAura);
        Assert.NotNull(rec);
        Assert.Equal(new ArmyOrdersReport(2, 0), rec!.Orders);           // both applied: the aura was judged AFTER the flip
        Assert.Equal(0, rec.IllegalDropped);                             // the pipeline dropped nothing
        Assert.Equal(2, rec.PlayerDoctrine.Used);                        // and the record keeps what the player asked
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_token_legal_at_the_start_but_not_after_an_earlier_one_is_skipped_by_the_army_and_kept_in_the_record(string which)
    {
        var merge0 = DoctrineVocabulary.Army(ArmyOrderKind.Merge, 0);
        var rec = await OneRound(Army(which), a => a.Legal.Allows(merge0) ? DoctrineSet.Of(merge0, merge0) : null);
        Assert.NotNull(rec);
        Assert.Equal(new ArmyOrdersReport(1, 1), rec!.Orders);
        Assert.Equal(0, rec.IllegalDropped);
        Assert.Equal(2, rec.PlayerDoctrine.Used);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task A_token_that_is_never_legal_is_skipped_by_the_army_not_dropped_by_the_pipeline(string which)
    {
        var rec = await OneRound(Army(which), a => { int x = ForeignTrait(a); return x < 0 ? null : DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Aura, x)); });
        Assert.NotNull(rec);
        Assert.Equal(new ArmyOrdersReport(0, 1), rec!.Orders);
        Assert.Equal(0, rec.IllegalDropped);
    }

    [Theory, MemberData(nameof(Armies))]
    public async Task The_pipeline_still_removes_ids_outside_the_vocabulary(string which)
    {
        var rec = await OneRound(Army(which), a => DoctrineSet.Of(999, -4, DoctrineVocabulary.Army(ArmyOrderKind.Age)));
        Assert.NotNull(rec);
        Assert.Equal(2, rec!.IllegalDropped);
        Assert.Equal(DoctrineSet.Of(0, 0, DoctrineVocabulary.Army(ArmyOrderKind.Age)), rec.PlayerDoctrine);
        Assert.Equal(new ArmyOrdersReport(1, 0), rec.Orders);
    }

    [Fact]
    public async Task The_pipeline_still_removes_a_field_order_the_start_mask_forbids()
    {
        // (the stub army withholds the field orders of a group with no cohort; the Pleroma army's list never withholds one in these armies)
        var rec = await OneRound(new StubArmyService(), a =>
        {
            int g = Enumerable.Range(0, DoctrineVocabulary.Groups).FirstOrDefault(x => !a.Legal.Allows(DoctrineVocabulary.Field(FieldOrderKind.Hold, x)), -1);
            return g < 0 ? null : DoctrineSet.Of(DoctrineVocabulary.Field(FieldOrderKind.Hold, g), DoctrineVocabulary.Army(ArmyOrderKind.Age));
        });
        Assert.NotNull(rec);
        Assert.Equal(1, rec!.IllegalDropped);
        Assert.Equal(DoctrineSet.Of(0, DoctrineVocabulary.Army(ArmyOrderKind.Age)), rec.PlayerDoctrine);
    }
    [Theory, MemberData(nameof(Armies))]
    public async Task An_army_that_judges_legality_once_instead_of_per_token_is_caught_end_to_end(string which)
    {
        // the mutant: the army evaluates the list before the doctrine (ArmyMut.LegalityOnce); the pipeline-level case goes red on it
        var rec = await OneRound(new MutantArmyService(new RichPlayerArmy(Army(which)), ArmyMut.LegalityOnce), FlipThenAura);
        Assert.NotNull(rec);
        Assert.NotEqual(new ArmyOrdersReport(2, 0), rec!.Orders);
    }

    [Fact]
    public async Task The_general_is_bound_by_the_start_mask_the_pipeline_cleans_its_ghost_and_counts_what_it_removed()
    {
        // a general that ignores the mask offers a forbidden token every time; the player is never shown it, and the round counts it
        var seen = new RecordingPlayer(new ScriptedPlayer(3, acceptGhostPercent: 100));
        var game = DoctrineBuilder.New().WithGeneral(new MutantGeneral(GeneralMut.IgnoresMask)).WithPlayer(seen).WithRules(new DoctrineDomain { MaxRounds = 12 }).Build();
        var r = await game.PlayAsync(3);
        for (int i = 0; i < seen.Ghosts.Count; i++)
            Assert.True(seen.ChooseArmies[i].Legal.AllowsAll(seen.Ghosts[i].Doctrine), $"round {i}: the ghost shown to the player held a token the start mask forbids");
        Assert.True(r.Rounds.Sum(x => x.IllegalDropped) > 0, "the broken general should have been caught");
    }
}
