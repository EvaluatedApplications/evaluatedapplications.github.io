using System.Diagnostics;
using EvalApp;

namespace Doctrine.Tests;

/// <summary>The whole game, headless: draft, doctrine, battle, spoils, next round, through the EvalApp pipelines.</summary>
public class EndToEndTests
{
    static DoctrineGame Game(int playerSeed = 1, int rounds = 8) =>
        DoctrineBuilder.New().WithPlayer(new ScriptedPlayer(playerSeed)).WithRules(new DoctrineDomain { MaxRounds = rounds }).Build();

    [Fact]
    public async Task A_seeded_game_plays_every_round_and_hands_back_its_history()
    {
        var r = await Game().PlayAsync(1234);
        Assert.Equal(8, r.Rounds.Count);
        Assert.Equal(Enumerable.Range(0, 8), r.Rounds.Select(x => x.Round));
        Assert.All(r.Rounds, x => Assert.NotEqual(BattleStatus.Running, x.Result.Outcome));
        Assert.All(r.Rounds, x => Assert.Equal(x.Result.Ticks + 1, x.Replay.Frames.Count));
        Assert.Contains(r.Rounds, x => x.Result.AttackerWon);        // the scripted opponent is beatable and not trivial
        Assert.Contains(r.Rounds, x => !x.Result.AttackerWon);
    }

    [Fact]
    public async Task Same_seed_same_game_down_to_every_replay()
    {
        var a = await Game().PlayAsync(77); var b = await Game().PlayAsync(77);
        Assert.Equal(a.Digest(), b.Digest());
        Assert.Equal(a.PlayerArmyCode, b.PlayerArmyCode);
        Assert.True(a.Rounds.Zip(b.Rounds).All(p => ReplayCodec.Encode(p.First.Replay).AsSpan().SequenceEqual(ReplayCodec.Encode(p.Second.Replay))));
    }

    [Fact]
    public async Task Different_seeds_play_different_games()
    {
        var digests = new HashSet<ulong>();
        foreach (var seed in new[] { 1, 2, 3, 4, 5 }) digests.Add((await Game().PlayAsync(seed)).Digest());
        Assert.Equal(5, digests.Count);
    }

    [Fact]
    public async Task Every_round_leaves_a_replay_that_satisfies_the_replay_contract()
    {
        foreach (var round in (await Game().PlayAsync(9)).Rounds)
            foreach (var c in ReplayContract.Cases)
                Assert.True(null == await c.RunAsync(round.Replay), $"round {round.Round}: {c.Name}");
    }

    [Fact]
    public async Task The_legal_mask_is_never_broken_and_the_ending_army_is_a_valid_code()
    {
        var game = Game();
        var r = await game.PlayAsync(5);
        Assert.All(r.Rounds, x => Assert.Equal(0, x.IllegalDropped));
        Assert.True(game.Context.Armies.TryParse(r.PlayerArmyCode, out var back, out var err), err);
        Assert.Equal(r.PlayerArmyCode, back!.ToCode());
    }

    [Fact]
    public async Task The_general_learns_from_every_round_twice()
    {
        var game = Game();
        var r = await game.PlayAsync(3);
        Assert.Equal(2 * r.Rounds.Count, game.Context.General.ExamplesSeen);   // imitation before the battle, outcome after
    }

    [Fact]
    public async Task A_trained_general_is_offered_as_the_ghost()
    {
        var game = Game(playerSeed: 4);
        var r = await game.PlayAsync(6);
        Assert.Equal(0, r.Rounds[0].GhostConfidence);               // empty at the start: nothing pre-trained
        Assert.All(r.Rounds, x => Assert.InRange(x.GhostConfidence, 0, 1000));
    }

    [Fact]
    public async Task The_wall_of_the_tick_limit_holds()
    {
        var game = DoctrineBuilder.New().WithRules(new DoctrineDomain { MaxRounds = 3, TickLimit = 5 }).Build();
        var r = await game.PlayAsync(2);
        Assert.All(r.Rounds, x => Assert.True(x.Result.Ticks <= 5));
    }

    [Fact]
    public void Round_battle_and_tick_pipelines_expose_every_stage_slot()
    {
        var game = Game();
        var names = new HashSet<string>(game.Round.Report().Nodes.Concat(game.Battle.Report().Nodes).Concat(game.Tick.Report().Nodes).Select(n => n.Label));
        foreach (var f in typeof(Stages).GetFields().Where(f => f.IsLiteral)) Assert.Contains((string)f.GetRawConstantValue()!, names);
    }

    [Fact]
    public async Task A_round_really_feeds_the_next_through_one_pipeline_and_deep_games_work()
    {
        var weak = new DoctrineDomain { MaxRounds = 150, EnemyBaseGold = 0, EnemyGoldPerRound = 0 };
        var r = await DoctrineBuilder.New().WithRules(weak).Build().PlayAsync(8);
        Assert.Equal(150, r.Rounds.Count);
    }

    [Fact]
    public void Rules_that_would_nest_past_the_pipeline_depth_guard_are_refused_up_front()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DoctrineBuilder.New().WithRules(new DoctrineDomain { MaxRounds = 300 }));

    [Fact]
    public async Task A_cancelled_game_stops()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(async () => await Game().PlayAsync(1, cts.Token));
    }

    [Fact]
    public async Task A_broken_general_is_contained_by_the_round_pipeline_not_trusted()
    {
        // The unmasked mutant suggests tokens the army forbids; the pipeline drops and COUNTS them, so the harm is visible, never silent.
        var game = DoctrineBuilder.New().WithGeneral(new MutantGeneral(GeneralMut.IgnoresMask)).WithPlayer(new ScriptedPlayer(3, acceptGhostPercent: 100)).WithRules(new DoctrineDomain { MaxRounds = 12 }).Build();
        var r = await game.PlayAsync(3);
        Assert.True(r.Rounds.Sum(x => x.IllegalDropped) > 0, "the broken general should have been caught offering a forbidden token");
    }

}

/// <summary>"Replace one stub": wrap each seam, count the calls, and the game still plays identically.</summary>
public class ReplacementTests
{
    [Fact]
    public async Task Any_seam_can_be_swapped_without_the_others_noticing()
    {
        var plain = await DoctrineBuilder.New().Build().PlayAsync(41);
        var armies = new CountingArmies(new StubArmyService()); var engine = new CountingBattle(new StubBattleEngine()); var general = new CountingGeneral(new TableLookupGeneral());
        var wrapped = await DoctrineBuilder.New().WithArmy(armies).WithBattle(engine).WithGeneral(general).Build().PlayAsync(41);
        Assert.Equal(plain.Digest(), wrapped.Digest());
        Assert.True(armies.Calls > 0 && engine.Calls > 0 && general.Calls > 0);
    }

    sealed class CountingArmies(IArmyService i) : IArmyService
    {
        public int Calls;
        public IArmy Create(Side s, int seed) { Calls++; return i.Create(s, seed); }
        public bool TryParse(string c, out IArmy? a, out string e) { Calls++; return i.TryParse(c, out a, out e); }
    }
    sealed class CountingBattle(IBattleEngine i) : IBattleEngine
    {
        public int Calls;
        public IReadOnlyList<ArenaInfo> Arenas => i.Arenas;
        public ValueTask<IBattleSession> StartAsync(BattleSetup s, CancellationToken ct) { Calls++; return i.StartAsync(s, ct); }
    }
    sealed class CountingGeneral(IGeneral i) : IGeneral
    {
        public int Calls;
        public int ExamplesSeen => i.ExamplesSeen;
        public ValueTask<GeneralOutput> SuggestAsync(GeneralInput x, CancellationToken ct) { Calls++; return i.SuggestAsync(x, ct); }
        public ValueTask LearnAsync(GeneralExample x, CancellationToken ct) { Calls++; return i.LearnAsync(x, ct); }
        public ValueTask LearnOutcomeAsync(GeneralOutcome x, CancellationToken ct) { Calls++; return i.LearnOutcomeAsync(x, ct); }
    }
}

/// <summary>Composition with the Tracer package's own EvalApp context, and the one-thread host.</summary>
public class CompositionTests
{
    [Fact]
    public async Task The_tracer_context_is_held_in_ours_and_reaches_a_late_bound_battle_engine()
    {
        var world = new global::Tracer.PathfindingWorld();
        TracerSeen? seen = null;
        var game = DoctrineBuilder.New().WithTracer(world).WithBattle(ctx => { seen = new(ctx.Tracer?.World); return new StubBattleEngine(); }).Build();
        Assert.Same(world, game.Context.Tracer!.World);
        Assert.Same(world, seen!.World);
        Assert.Equal(8, (await game.PlayAsync(1)).Rounds.Count);
    }
    sealed record TracerSeen(global::Tracer.PathfindingWorld? World);

    [Fact]
    public async Task A_game_pinned_to_one_thread_plays_the_same_game_on_one_thread()
    {
        var plain = await DoctrineBuilder.New().Build().PlayAsync(52);
        var threads = new HashSet<int>();
        var pinned = OnOneThread(async () =>
        {
            var g = DoctrineBuilder.New().WithSingleThread(true).WithGeneral(new ThreadNoting(new TableLookupGeneral(), threads)).Build();
            return await g.PlayAsync(52);
        });
        Assert.Equal(plain.Digest(), pinned.Digest());
        Assert.Single(threads);   // every general call, across 8 rounds, ran on the pump's one thread
    }

    sealed class ThreadNoting(IGeneral i, HashSet<int> seen) : IGeneral
    {
        public int ExamplesSeen => i.ExamplesSeen;
        public ValueTask<GeneralOutput> SuggestAsync(GeneralInput x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.SuggestAsync(x, ct); }
        public ValueTask LearnAsync(GeneralExample x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.LearnAsync(x, ct); }
        public ValueTask LearnOutcomeAsync(GeneralOutcome x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.LearnOutcomeAsync(x, ct); }
    }

    /// <summary>Runs an async job with a message pump on ONE dedicated thread (what a browser gives you) and returns its result.</summary>
    static T OnOneThread<T>(Func<Task<T>> job) => OnOneThreadAsync(job).GetAwaiter().GetResult();
    static Task<T> OnOneThreadAsync<T>(Func<Task<T>> job)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() =>
        {
            var ctx = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            var task = job();
            task.ContinueWith(x => { if (x.IsFaulted) tcs.SetException(x.Exception!.InnerExceptions); else tcs.SetResult(x.Result); ctx.Stop(); }, TaskScheduler.Default);
            ctx.Run();
        });
        t.Start();
        return tcs.Task;
    }
    sealed class PumpContext : SynchronizationContext
    {
        readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _q = new();
        public override void Post(SendOrPostCallback d, object? s) => _q.Add((d, s));
        public void Stop() => _q.CompleteAdding();
        public void Run() { foreach (var (d, s) in _q.GetConsumingEnumerable()) d(s); }
    }
}
