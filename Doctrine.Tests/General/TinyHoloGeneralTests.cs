using EvalApp;

namespace Doctrine.Tests;

/// <summary>The real general's own tests, beyond the shared contract (which TinyHoloGeneralContract runs in all three modes).</summary>
public class TinyHoloGeneralTests
{
    static GeneralInput In(Situation s) => new(s.S[0], s.S, s.Legal);

    [Theory]
    [InlineData(GeneralMode.Hybrid)] [InlineData(GeneralMode.TableOnly)] [InlineData(GeneralMode.NetOnly)]
    public async Task A_new_general_says_nothing_in_every_mode(GeneralMode mode)
        => Assert.Null(await GeneralChecks.StartsEmpty(GeneralChecks.Real(mode)));

    [Fact]
    public async Task The_net_alone_cannot_get_a_forbidden_token_out()
        => Assert.Null(await GeneralChecks.NetStaysInsideTheMask(GeneralChecks.Real(GeneralMode.NetOnly)));

    [Theory]
    [InlineData(GeneralMode.Hybrid)] [InlineData(GeneralMode.TableOnly)] [InlineData(GeneralMode.NetOnly)]
    public async Task Two_generals_taught_alike_answer_alike_down_to_the_confidence(GeneralMode mode)
        => Assert.Null(await GeneralChecks.SameTeachingSameAnswers(GeneralChecks.Real(mode)));

    [Fact]
    public async Task Players_with_different_habits_get_generals_that_disagree()
        => Assert.Null(await GeneralChecks.PlayersStayApart(GeneralChecks.Real(GeneralMode.Hybrid)));

    [Fact]
    public async Task A_habitual_player_is_learned_from_sixty_of_their_own_choices()
        => Assert.Null(await GeneralChecks.HabitIsLearned(GeneralChecks.Real(GeneralMode.Hybrid)));

    [Theory]
    [InlineData(GeneralMode.Hybrid)] [InlineData(GeneralMode.TableOnly)]
    public async Task Lost_battles_erode_a_choice_and_won_ones_restore_it(GeneralMode mode)
        => Assert.Null(await GeneralChecks.LostBattlesErodeAndWinsRestore(GeneralChecks.Real(mode)));

    /// <summary>
    /// THE PROBE, pinned (AlgFormer\todo\general-probe-2026-10-18.md): with fixed seeds the numbers EXP-GEN-1/2 reported keep their order.
    /// The hybrid beats the skeleton's table stub by a wide margin and Null 1 (uniform over legal choices) by orders of magnitude; the net alone
    /// learns (far above Null 1) but stays below the lookup. If someone makes the net better than the lookup this goes red on purpose: update
    /// the probe note and the claim the page may make, then this test.
    /// </summary>
    [Fact]
    public async Task The_probes_ranking_holds_on_fixed_seeds()
    {
        double stub = 0, hybrid = 0, table = 0, net = 0; int n = 0;
        foreach (var style in new[] { 0, 1 }) foreach (var seed in new[] { 3, 4 })
        {
            var gStub = new TableLookupGeneral(); var gHyb = new TinyHoloGeneral(GeneralChecks.Fast()); var gTab = new TinyHoloGeneral(GeneralChecks.Fast(GeneralMode.TableOnly));
            var gNet = new TinyHoloGeneral(new GeneralOptions { Mode = GeneralMode.NetOnly, PassesPerLearn = 96, PassCap = 4800 });
            foreach (var g in new IGeneral[] { gStub, gHyb, gTab, gNet }) await GeneralChecks.Teach(g, style, seed, 60);
            stub += await GeneralChecks.Exact(gStub, style, seed); hybrid += await GeneralChecks.Exact(gHyb, style, seed);
            table += await GeneralChecks.Exact(gTab, style, seed); net += await GeneralChecks.Exact(gNet, style, seed); n++;
        }
        stub /= n; hybrid /= n; table /= n; net /= n;
        Assert.True(hybrid > stub + 0.15, $"hybrid {hybrid:F3} vs the skeleton's exact-key table {stub:F3}");
        Assert.True(Math.Abs(hybrid - table) < 0.05, $"the hybrid should track the lookup: hybrid {hybrid:F3}, lookup {table:F3}");
        Assert.True(net > 0.05, $"the net alone should learn far above the uniform null (about 0.0002): {net:F3}");
        Assert.True(net < table, $"the probe found the net alone below the lookup: net {net:F3}, lookup {table:F3}");
    }

    // ---- SlotConfidence: the per-slot match record -------------------------------------------------------------------

    [Theory]
    [InlineData(GeneralMode.Hybrid)] [InlineData(GeneralMode.TableOnly)] [InlineData(GeneralMode.NetOnly)]
    public async Task An_empty_general_reports_four_zeros(GeneralMode mode)
    {
        var o = await GeneralChecks.Real(mode)().SuggestAsync(new GeneralInput(0, new int[14], LegalMask.All), default);
        Assert.Equal(new SlotPermille(0, 0, 0, 0), o.SlotConfidence);
        Assert.Equal(0, o.ConfidencePermille);
    }

    [Theory]
    [InlineData(GeneralMode.Hybrid, 0, 3)] [InlineData(GeneralMode.TableOnly, 1, 4)] [InlineData(GeneralMode.NetOnly, 0, 5)] [InlineData(GeneralMode.Hybrid, 2, 6, true)]
    public async Task The_slot_figure_equals_a_brute_force_recomputation_from_the_example_stream(GeneralMode mode, int style, int seed, bool random = false)
        => Assert.Null(await GeneralChecks.SlotRecordMatchesBruteForce(GeneralChecks.Real(mode), style, seed, 150, random));

    [Fact]
    public async Task A_habitual_player_reads_high_within_a_few_examples_and_a_random_player_does_not()
    {
        foreach (var style in new[] { 0, 1 })
        {
            var h = (await GeneralChecks.SlotFiguresAfter(GeneralChecks.Real(GeneralMode.Hybrid)(), style, 11, 12, false))!.Value;
            Assert.True(new[] { h.S0, h.S1, h.S2 }.Max() >= 400, $"style {style}: after 12 habitual examples the slot figures were {h}");
            var h30 = (await GeneralChecks.SlotFiguresAfter(GeneralChecks.Real(GeneralMode.Hybrid)(), style, 11, 30, false))!.Value;
            Assert.True(new[] { h30.S0, h30.S1, h30.S2 }.Count(x => x >= 500) >= 2, $"style {style}: after 30 habitual examples the slot figures were {h30}");
        }
        foreach (var seed in new[] { 11, 12 })
        {
            var r = (await GeneralChecks.SlotFiguresAfter(GeneralChecks.Real(GeneralMode.Hybrid)(), 0, seed, 40, true))!.Value;
            Assert.True(new[] { r.S0, r.S1, r.S2, r.S3 }.Max() < 300, $"a random player read {r}");
        }
    }

    [Fact]
    public async Task The_slot_figure_is_deterministic_in_range_and_a_new_general_starts_at_zero()
    {
        var a = await GeneralChecks.SlotFiguresAfter(GeneralChecks.Real(GeneralMode.Hybrid)(), 1, 21, 30, false);
        var b = await GeneralChecks.SlotFiguresAfter(GeneralChecks.Real(GeneralMode.Hybrid)(), 1, 21, 30, false);
        Assert.Equal(a, b);
        Assert.True(a!.Value.InRange);
        Assert.Null(await GeneralChecks.NewGeneralStartsAtZero(GeneralChecks.Real(GeneralMode.TableOnly)));
    }

    [Fact]
    public async Task Slot_figures_are_already_nonzero_after_eight_rounds_of_a_habit()
    {
        // the first rounds of a short game: the slot figures move although the whole-doctrine rate needs exact matches
        var g = GeneralChecks.Real(GeneralMode.TableOnly)();
        var (st, _, cl) = GeneralSim.Draw(31, 0, 8);
        for (int i = 0; i < 8; i++) await g.LearnAsync(new GeneralExample(In(st[i]), cl[0][i], false), default);
        var o = await g.SuggestAsync(In(st[0]), default);
        Assert.True(o.SlotConfidence!.Value.S0 > 0 || o.SlotConfidence.Value.S1 > 0, $"{o.SlotConfidence}");
    }

    [Fact]
    public void Learning_is_an_EvalApp_pipeline_one_remember_step_and_four_replay_slices()
    {
        var labels = Learning.Pipeline.Report().Nodes.Select(x => x.Label).ToList();
        Assert.Contains("General.Remember", labels);
        for (int i = 1; i <= 4; i++) Assert.Contains($"General.Replay.{i}", labels);
    }

    [Fact]
    public async Task A_cancelled_learn_stops_and_odd_examples_are_taken_in_stride()
    {
        var g = new TinyHoloGeneral(GeneralChecks.Fast());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(async () => await g.LearnAsync(new GeneralExample(In(GeneralSim.Draw(1, 0, 1).States[0]), DoctrineSet.Of(1), false), cts.Token));
        // values outside 0..15, a short and a long summary, tokens outside the vocabulary: learned (and counted), never thrown
        var odd = new[] { new GeneralInput(-5, [99, -3, 7], LegalMask.All), new GeneralInput(1000, Enumerable.Repeat(40, 30).ToArray(), LegalMask.None), new GeneralInput(0, [], LegalMask.All) };
        foreach (var i in odd)
        {
            await g.LearnAsync(new GeneralExample(i, new DoctrineSet(500, -3, 1, 2), true), default);
            await g.LearnOutcomeAsync(new GeneralOutcome(i, new DoctrineSet(500, -3, 1, 2), BattleStatus.Draw, false), default);
            Assert.True(i.Legal.AllowsAll((await g.SuggestAsync(i, default)).Doctrine));
        }
    }

    [Fact]
    public async Task The_real_general_plays_the_whole_game_beside_the_real_army_and_the_digest_is_deterministic()
    {
        static async Task<GameResult> Play(int seed)
        {
            var g = new TinyHoloGeneral(GeneralChecks.Fast());
            var game = DoctrineBuilder.New().WithArmy(new PleromaArmyService()).WithGeneral(g).WithPlayer(new ScriptedPlayer(3, acceptGhostPercent: 100)).Build();
            var r = await game.PlayAsync(seed);
            Assert.Equal(2 * r.Rounds.Count, g.ExamplesSeen);
            Assert.Equal(0, r.Rounds[0].GhostConfidence);
            Assert.All(r.Rounds, x => { Assert.InRange(x.GhostConfidence, 0, 1000); Assert.Equal(0, x.IllegalDropped); });
            return r;
        }
        var a = await Play(12); var b = await Play(12);
        Assert.Equal(a.Digest(), b.Digest());
        Assert.NotEqual(a.Digest(), (await Play(13)).Digest());
    }

    [Fact]
    public async Task On_one_thread_the_game_with_the_real_general_is_the_same_game()
    {
        var plain = await DoctrineBuilder.New().WithGeneral(new TinyHoloGeneral(GeneralChecks.Fast())).Build().PlayAsync(52);
        var threads = new HashSet<int>();
        var pinned = OnOneThread(async () =>
        {
            var g = DoctrineBuilder.New().WithSingleThread(true).WithGeneral(new ThreadNoting(new TinyHoloGeneral(GeneralChecks.Fast()), threads)).Build();
            return await g.PlayAsync(52);
        });
        Assert.Equal(plain.Digest(), pinned.Digest());
        Assert.Single(threads);
    }

    sealed class ThreadNoting(IGeneral i, HashSet<int> seen) : IGeneral
    {
        public int ExamplesSeen => i.ExamplesSeen;
        public ValueTask<GeneralOutput> SuggestAsync(GeneralInput x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.SuggestAsync(x, ct); }
        public ValueTask LearnAsync(GeneralExample x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.LearnAsync(x, ct); }
        public ValueTask LearnOutcomeAsync(GeneralOutcome x, CancellationToken ct) { seen.Add(Environment.CurrentManagedThreadId); return i.LearnOutcomeAsync(x, ct); }
    }

    static T OnOneThread<T>(Func<Task<T>> job)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() =>
        {
            var ctx = new Pump();
            SynchronizationContext.SetSynchronizationContext(ctx);
            var task = job();
            task.ContinueWith(x => { if (x.IsFaulted) tcs.SetException(x.Exception!.InnerExceptions); else tcs.SetResult(x.Result); ctx.Stop(); }, TaskScheduler.Default);
            ctx.Run();
        });
        t.Start();
        return tcs.Task.GetAwaiter().GetResult();
    }
    sealed class Pump : SynchronizationContext
    {
        readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _q = new();
        public override void Post(SendOrPostCallback d, object? s) => _q.Add((d, s));
        public void Stop() => _q.CompleteAdding();
        public void Run() { foreach (var (d, s) in _q.GetConsumingEnumerable()) d(s); }
    }
}

/// <summary>
/// MUTATION PROOFS for the general's own checks, on top of the shared ones in MutationProofs. Each mutant breaks ONE thing in the real
/// class (a fault seam compiled into it, internal, set only here); the named check must go red, and the clean class must pass the same check.
/// </summary>
public class TinyHoloGeneralMutationProofs
{
    static Task<string?> Mask(GeneralFault f) => GeneralChecks.NetStaysInsideTheMask(GeneralChecks.Broken(GeneralMode.NetOnly, f));

    [Fact] public async Task Clean_general_passes_every_own_check()
    {
        Assert.Null(await GeneralChecks.StartsEmpty(GeneralChecks.Real(GeneralMode.Hybrid)));
        Assert.Null(await GeneralChecks.NetStaysInsideTheMask(GeneralChecks.Real(GeneralMode.NetOnly)));
        Assert.Null(await GeneralChecks.SameTeachingSameAnswers(GeneralChecks.Real(GeneralMode.NetOnly)));
        Assert.Null(await GeneralChecks.PlayersStayApart(GeneralChecks.Real(GeneralMode.Hybrid)));
        Assert.Null(await GeneralChecks.HabitIsLearned(GeneralChecks.Real(GeneralMode.Hybrid)));
        Assert.Null(await GeneralChecks.LostBattlesErodeAndWinsRestore(GeneralChecks.Real(GeneralMode.Hybrid)));
    }

    [Fact] public async Task Slot_figures_read_from_the_neighbouring_slot_are_caught()
        => Assert.NotNull(await GeneralChecks.SlotRecordMatchesBruteForce(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.SlotsSwapped), 0, 3, 60));

    [Fact] public async Task A_slot_record_that_a_new_general_inherits_is_caught()
        => Assert.NotNull(await GeneralChecks.NewGeneralStartsAtZero(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.SlotsNotReset)));

    [Fact] public async Task A_slot_figure_above_the_measured_hit_rate_is_caught()
        => Assert.NotNull(await GeneralChecks.SlotRecordMatchesBruteForce(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.SlotsOptimistic), 0, 3, 60));

    [Fact] public async Task Clean_general_passes_the_slot_record_checks()
    {
        Assert.Null(await GeneralChecks.SlotRecordMatchesBruteForce(GeneralChecks.Real(GeneralMode.TableOnly), 0, 3, 60));
        Assert.Null(await GeneralChecks.NewGeneralStartsAtZero(GeneralChecks.Real(GeneralMode.TableOnly)));
    }

    [Fact] public async Task A_net_whose_output_is_not_masked_is_caught()
        => Assert.NotNull(await Mask(GeneralFault.NetOutputUnmasked));

    [Fact] public async Task A_net_trusted_before_it_has_learned_anything_is_caught()
        => Assert.NotNull(await GeneralChecks.StartsEmpty(GeneralChecks.Broken(GeneralMode.Hybrid, GeneralFault.NetAlwaysTrusted)));

    [Fact] public async Task A_general_that_ignores_battle_results_is_caught()
        => Assert.NotNull(await GeneralChecks.LostBattlesErodeAndWinsRestore(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.OutcomeIgnored)));

    [Fact] public async Task Generals_that_share_one_lookup_cannot_tell_players_apart_and_are_caught()
        => Assert.NotNull(await GeneralChecks.PlayersStayApart(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.SharedState)));

    [Fact] public async Task A_net_seeded_differently_each_time_is_caught_by_the_same_teaching_check()
        => Assert.NotNull(await GeneralChecks.SameTeachingSameAnswers(GeneralChecks.Broken(GeneralMode.NetOnly, GeneralFault.NetUnseeded)));

    [Fact] public async Task A_lookup_that_always_reads_the_same_feature_does_not_learn_the_habit_and_is_caught()
        => Assert.NotNull(await GeneralChecks.HabitIsLearned(GeneralChecks.Broken(GeneralMode.TableOnly, GeneralFault.FirstFeatureOnly)));
}
