using System.Diagnostics;
using EvalApp;

namespace Doctrine.Lane;

// The lane battle as an EvalApp application, the way the round code already is: one global context (holding the battle and the two pipelines by reference), one domain,
// two pipelines. "LaneTick" is one tick (five steps); "LanePhase" loops the tick pipeline (a loop in a step is the documented way to run a pipeline many times).

/// <summary>The stage names, as they appear in a pipeline report.</summary>
public static class LaneStages
{
    public const string Think = "Lane.Think";       // each body re-decides its goal from its lane's order (staggered)
    public const string Move = "Lane.Move";         // Tracer's multi-agent service paths the bodies that need it; every body steps along its path
    public const string Engage = "Lane.Engage";     // Tracer's engagement service (line of sight, cover, counters) says who strikes whom; bases are struck
    public const string Resolve = "Lane.Resolve";   // damage lands on every body at once; the dead leave the field and the Pleroma roster
    public const string Judge = "Lane.Judge";       // a fallen base ends the match; the hard cap is a draw
    public const string Phase = "Lane.Phase";       // loops the tick pipeline up to the next pause
}

internal sealed class LaneDomain : DomainContext { }

internal sealed class LaneContext : GlobalContext
{
    public LaneContext(LaneBattle battle) { Battle = battle; }
    public LaneBattle Battle { get; }
    public PipelineRef<LaneRun> Tick { get; } = new("LaneTick");
    public PipelineRef<LaneRun> Phase { get; } = new("LanePhase");
}

/// <summary>Task data of both pipelines: ticks still to run, and the stop flag the judge sets.</summary>
internal sealed record LaneRun(int Remaining, bool Stop = false);

internal abstract class LaneStage : ContextSideEffectStep<LaneContext, LaneDomain, LaneRun> { }

internal sealed class LaneThinkStage : LaneStage
{
    protected override ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct) { long t0 = Stopwatch.GetTimestamp(); g.Battle.ThinkStage(); g.Battle.Stats.ThinkTicks += Stopwatch.GetTimestamp() - t0; return ValueTask.FromResult(s); }
}

internal sealed class LaneMoveStage : LaneStage
{
    protected override async ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct) { long t0 = Stopwatch.GetTimestamp(); await g.Battle.MoveStage(ct); g.Battle.Stats.MoveTicks += Stopwatch.GetTimestamp() - t0; return s; }
}

internal sealed class LaneEngageStage : LaneStage
{
    protected override async ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct) { long t0 = Stopwatch.GetTimestamp(); await g.Battle.EngageStage(ct); g.Battle.Stats.EngageTicks += Stopwatch.GetTimestamp() - t0; return s; }
}

internal sealed class LaneResolveStage : LaneStage
{
    protected override ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct) { long t0 = Stopwatch.GetTimestamp(); g.Battle.ResolveStage(); g.Battle.Stats.ResolveTicks += Stopwatch.GetTimestamp() - t0; return ValueTask.FromResult(s); }
}

internal sealed class LaneJudgeStage : LaneStage
{
    protected override ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct) => ValueTask.FromResult(s with { Stop = g.Battle.JudgeStage() });
}

internal sealed class LanePhaseStage : LaneStage
{
    protected override async ValueTask<LaneRun> ExecuteAsync(LaneRun s, LaneContext g, LaneDomain d, CancellationToken ct)
    {
        while (s.Remaining > 0 && !s.Stop)
        {
            var r = await g.Tick.RunAsync(s, ct);
            s = PipelineFailure.DataOrThrow(r, $"tick {g.Battle.Tick} of the lane battle (seed {g.Battle.Seed})");
            s = s with { Remaining = s.Remaining - 1 };
        }
        return s;
    }
}
