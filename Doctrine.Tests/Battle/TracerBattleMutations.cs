using System.Collections.Immutable;
using Tracer;
using Tracer.Abstractions;
using Tracer.Integration;
using Tracer.Models;

namespace Doctrine.Tests;

// MUTATION PROOFS for the Tracer battle section. A test nobody has watched fail is only an assertion, so each of these breaks the real
// engine in ONE way and requires the named case or check to go red. (The shared MutationProofs break the stub; these break THIS section.)

// MutantOver and FrameMutantEngine (promoted from here) live in Mutation\Mutants.cs, shared by every section.

/// <summary>An engagement service whose resolution never lands a hit (the engine is told to use the one its Tracer context carries).</summary>
sealed class SilentEngagement(IEngagementService inner) : IEngagementService
{
    public ValueTask<EngagementResult<T>> ResolveAsync<T>(IReadOnlyList<T> a, IReadOnlyList<T> d, float r, CancellationToken ct) where T : GameAgent => inner.ResolveAsync(a, d, r, ct);
    public ValueTask<EngagementResult<T>> ResolveAsync<T>(IReadOnlyList<T> a, IReadOnlyList<T> d, float r, Func<T, T, float, IGameMath, IEnumerable<EngagementOutcome>> f, CancellationToken ct) where T : GameAgent => inner.ResolveAsync(a, d, r, f, ct);
    public ValueTask<ImmutableArray<EngagementOutcome>> ResolveOutcomesAsync<T>(IReadOnlyList<T> a, IReadOnlyList<T> d, float r, CancellationToken ct) where T : GameAgent => ValueTask.FromResult(ImmutableArray<EngagementOutcome>.Empty);
    public ValueTask<ImmutableArray<EngagementOutcome>> ResolveOutcomesAsync<T>(IReadOnlyList<T> a, IReadOnlyList<T> d, float r, Func<T, T, float, IGameMath, IEnumerable<EngagementOutcome>> f, CancellationToken ct) where T : GameAgent => ValueTask.FromResult(ImmutableArray<EngagementOutcome>.Empty);
}

public class TracerBattleMutationProofs
{
    static void Caught(IReadOnlyList<ContractFailure> failures, string expectedCase)
        => Assert.True(failures.Any(f => f.Case == expectedCase), $"expected '{expectedCase}' to fail; failures were: [{string.Join(" | ", failures)}]");

    static Task<IReadOnlyList<ContractFailure>> Battle(Func<IBattleEngine> create) => ContractRunner.RunAllAsync(BattleContract.Cases, create);

    static TracerBattleEngine With(TracerBattleOptions? o = null, Func<PathfindingWorld, IEngagementService>? engagement = null)
    {
        var world = TracerWorlds.Scratch();
        return new TracerBattleEngine(new TracerContext(world, engagement?.Invoke(world)), o);
    }

    // ---- baselines: the clean Tracer section holds every case ----
    [Fact] public async Task Clean_tracer_section_passes_the_battle_contract() => Assert.Empty(await Battle(() => TracerWorlds.Engine()));

    [Fact]
    public async Task Clean_tracer_recordings_pass_the_replay_contract()
    {
        var e = TracerWorlds.Engine();
        foreach (var (name, setup) in ReplayContractTests.Fights(e))
        {
            var replay = (await Sample.RunAsync(e, setup)).Replay!;
            Assert.Empty(await ContractRunner.RunAllAsync(ReplayContract.Cases, () => replay));
        }
    }

    // ---- the shared battle mutants, over the real engine ----
    [Fact] public async Task Tracer_frames_off_by_one_tick_are_caught() => Caught(await Battle(() => new MutantOver(TracerWorlds.Engine(), BattleMut.FrameTickOffByOne)), "frames-are-consecutive-and-complete");
    [Fact] public async Task Tracer_losses_reported_twice_are_caught() => Caught(await Battle(() => new MutantOver(TracerWorlds.Engine(), BattleMut.LossesReportedTwice)), "losses-equal-what-the-replay-shows");
    [Fact] public async Task Tracer_that_ignores_the_tick_limit_is_caught()
        // The sample fight of "battle-ends-with-a-decided-result-inside-the-limit" is decided before tick 40 on Tracer (the stub never decided it), so on this
        // section the limit is exercised by "tick-limit-decides-by-soldiers-left" instead.
        => Caught(await Battle(() => new MutantOver(TracerWorlds.Engine(), BattleMut.TickLimitIgnored)), "tick-limit-decides-by-soldiers-left");
    [Fact] public async Task Tracer_that_is_not_deterministic_is_caught() => Caught(await Battle(() => new MutantOver(TracerWorlds.Engine(), BattleMut.Nondeterministic)), "same-setup-same-recording");
    [Fact] public async Task Tracer_where_soldiers_reappear_is_caught() => Caught(await Battle(() => new MutantOver(TracerWorlds.Engine(), BattleMut.SoldiersReappear)), "blobs-are-stable-and-only-lose-soldiers");

    // ---- mutants of THIS section ----

    [Fact]
    public async Task An_engagement_service_that_never_hits_is_caught()
        => Caught(await Battle(() => With(engagement: w => new SilentEngagement(w.CreateEngagementService("silent")))), "events-are-between-enemies-and-deal-damage");

[Fact]
    public async Task Silent_engagement_leaves_a_crushing_attack_with_no_victory()
    {
        // The real section routs 20 defenders with 120 attackers (see TracerBattleTests); with no hit ever landing, nobody dies.
        var engine = With(engagement: w => new SilentEngagement(w.CreateEngagementService("silent")));
        var st = await Sample.RunAsync(engine, Sample.Setup("open-field", 3, 120, 120, 20));
        Assert.Empty(st.Result!.DefenderLosses);
        var real = await Sample.RunAsync(TracerWorlds.Engine(), Sample.Setup("open-field", 3, 120, 120, 20));
        Assert.NotEmpty(real.Result!.DefenderLosses);
    }

    [Fact]
    public async Task A_section_that_silently_skips_line_of_sight_is_caught()
        => Assert.NotNull(await TracerBattleTests.LineOfSightHoldsFire(With(new TracerBattleOptions { LineOfSight = false })));

    [Fact]
    public async Task A_section_that_silently_skips_fog_of_war_is_caught()
        => Assert.NotNull(await TracerBattleTests.FogChangesWhatIsKnown(With(new TracerBattleOptions { FogOfWar = false })));

    [Fact]
    public async Task A_section_that_silently_skips_the_influence_map_is_caught()
        => Assert.NotNull(await TracerBattleTests.InfluenceChoosesGround(With(new TracerBattleOptions { Influence = false })));

    [Fact]
    public async Task A_section_whose_gate_never_falls_is_caught()
        => Assert.NotNull(await TracerBattleTests.TheGateFalls(With(new TracerBattleOptions { DynamicObstacles = false })));

    [Fact]
    public async Task Recorded_positions_outside_the_arena_are_caught()
    {
        var bad = new FrameMutantEngine(TracerWorlds.Engine(), f => f with { Blobs = f.Blobs.Select(b => b with { X = b.X + 5000 * BattleReplay.Quantum }).ToList() });
        var replay = (await Sample.RunAsync(bad, Sample.Setup("open-field", 3, 30, 20, 20))).Replay!;
        Caught(await ContractRunner.RunAllAsync(ReplayContract.Cases, () => replay), "positions-stay-on-the-quantised-grid-and-in-bounds");
    }

    [Fact]
    public async Task A_recording_that_loses_its_last_frame_is_caught()
    {
        var replay = (await Sample.RunAsync(TracerWorlds.Engine(), Sample.Setup("ridge", 3, 40, 20, 20))).Replay!;
        var cut = replay with { Frames = replay.Frames.Take(replay.Frames.Count - 1).ToList() };
        Caught(await ContractRunner.RunAllAsync(ReplayContract.Cases, () => cut), "frames-are-in-tick-order-from-zero");
    }
}
