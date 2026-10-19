using EvalApp;

namespace Doctrine;

abstract class BattleStage_ : ContextSideEffectStep<DoctrineContext, DoctrineDomain, BattleState> { }

sealed class BeginStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var session = await g.Battle.StartAsync(s.Setup, ct);
        g.Observer?.BattleStarted(s.Setup);
        // Frame 0 = the start. A battle already decided at the start (a side with no soldiers) runs no tick.
        return s with { Session = session, Frames = new List<ReplayFrame> { session.Snapshot() },
                        Done = session.Status != BattleStatus.Running || s.Setup.TickLimit <= 0 };
    }
}

/// <summary>Loops the tick pipeline (held in the context) until the battle ends or the tick limit stops it. A loop in a step is the
/// documented way to run a pipeline many times; recursion would hit the pipeline depth guard at 256 ticks.</summary>
sealed class TicksStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        while (!s.Done)
        {
            var r = await g.BattleTick.RunAsync(s, ct);
            // Rethrown WITH its own stack; a failure with no exception becomes an error that names the battle (see PipelineFailure).
            s = PipelineFailure.DataOrThrow(r, $"tick {s.Session?.Tick} of the battle in arena {s.Setup.ArenaId} (seed {s.Setup.Seed})");
        }
        return s;
    }
}

sealed class FinishStage : BattleStage_
{
    protected override ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var result = s.Session!.Finish(s.TickLimitReached);
        g.Observer?.BattleFinished(result);
        // Written in the lowest version that can carry it: a battle with a besieger is version 3, every other battle is still version 2.
        var replay = new BattleReplay(BattleReplay.VersionFor(s.Setup.Besieger), s.Setup.ArenaId, s.Setup.Seed, s.Setup.TickLimit, s.Frames!, result, s.Setup.Besieger);
        return ValueTask.FromResult(s with { Result = result, Replay = replay });
    }
}

sealed class FieldOrdersStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    { await s.Session!.FieldOrdersAsync(ct); return s; }
}

sealed class MoveStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    { await s.Session!.MoveAsync(ct); return s; }
}

sealed class EngageStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    { await s.Session!.EngageAsync(ct); return s; }
}

sealed class ApplyLossesBattleStage : BattleStage_
{
    protected override async ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    { await s.Session!.ApplyLossesAsync(ct); return s; }
}

sealed class RecordStage : BattleStage_
{
    protected override ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    { s.Frames!.Add(s.Session!.Record()); g.Observer?.BattleTick(s.Session.Tick, s.Setup.TickLimit); return ValueTask.FromResult(s); }
}

sealed class JudgeStage : BattleStage_
{
    protected override ValueTask<BattleState> ExecuteAsync(BattleState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        if (s.Session!.Status != BattleStatus.Running) return ValueTask.FromResult(s with { Done = true });
        if (s.Session.Tick >= s.Setup.TickLimit) return ValueTask.FromResult(s with { Done = true, TickLimitReached = true });
        return ValueTask.FromResult(s);
    }
}
