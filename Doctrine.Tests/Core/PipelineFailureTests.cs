using EvalApp;

namespace Doctrine.Tests;

/// <summary>
/// A failed pipeline run must be reported as a failure, with what was running. EXP-AP-1 (AlgFormer\todo\autopilot-selfplay-2026-10-19.md) hit a
/// <c>Failure</c> with a null <c>Exception</c> inside <c>RunBattleStage</c>: <c>throw f.Exception</c> raised a NullReferenceException that hid it.
/// </summary>
public class PipelineFailureTests
{
    /// <summary>A battle pipeline that fails without an exception (the shape of the race seen in a concurrent host).</summary>
    sealed class FailsWithoutException : IStepMiddleware<BattleState>
    {
        public ValueTask<PipelineResult<BattleState>> ExecuteAsync(BattleState data, string stepName,
            Func<BattleState, CancellationToken, ValueTask<PipelineResult<BattleState>>> next, CancellationToken ct = default)
            => ValueTask.FromResult<PipelineResult<BattleState>>(new PipelineResult<BattleState>.Failure(data, null!, "the engine fell over"));
    }

    [Fact]
    public void A_failure_with_no_exception_becomes_an_error_that_says_what_was_running()
    {
        var f = new PipelineResult<int>.Failure(1, null!, "the engine fell over");
        var e = PipelineFailure.ToException(f, "the battle of round 3");
        var ioe = Assert.IsType<InvalidOperationException>(e);
        Assert.Contains("the battle of round 3", ioe.Message);
        Assert.Contains("the engine fell over", ioe.Message);
        // no message either: the error still names what was running
        var bare = Assert.IsType<InvalidOperationException>(PipelineFailure.ToException(new PipelineResult<int>.Failure(1, null!), "tick 4"));
        Assert.Contains("tick 4", bare.Message);
        Assert.Contains("no message", bare.Message);
    }

    [Fact]
    public void A_real_exception_is_kept_as_it_is()
    {
        var boom = new ArgumentException("boom");
        Assert.Same(boom, PipelineFailure.ToException(new PipelineResult<int>.Failure(1, boom), "anything"));
        var thrown = Assert.Throws<ArgumentException>(() => PipelineFailure.DataOrThrow<int>(new PipelineResult<int>.Failure(1, boom), "anything"));
        Assert.Same(boom, thrown);
        Assert.Equal(7, PipelineFailure.DataOrThrow<int>(new PipelineResult<int>.Success(7), "anything"));
    }

    [Fact]
    public async Task RunBattleStage_reports_a_battle_that_failed_without_an_exception()
    {
        var ctx = new DoctrineContext(new StubArmyService(), new StubBattleEngine(), new TableLookupGeneral(), new ScriptedPlayer(1));
        Eval.App("PipelineFailureTests").WithContext(ctx)
            .DefineDomain("Doctrine", new DoctrineDomain())
                .DefineTask<BattleState>("Battle").WithMiddleware(new FailsWithoutException()).AddStep("Never", (BattleState b) => b).Run(ctx.BattleRun)
                .DefineTask<RoundState>("Round").AddStep<RunBattleStage>(Stages.BattleRun).Run(ctx.Round)
            .Build();

        var state = new RoundState(3, 77, new StubArmyService().Create(Side.Player, 1), Setup: Sample.Setup("ridge", 5, 10));
        var run = await ctx.Round.RunAsync(state);

        var failure = Assert.IsType<PipelineResult<RoundState>.Failure>(run);
        var e = Assert.IsType<InvalidOperationException>(failure.Exception);     // not a NullReferenceException
        Assert.Contains("round 3", e.Message);
        Assert.Contains("seed 77", e.Message);
        Assert.Contains("ridge", e.Message);
        Assert.Contains("the engine fell over", e.Message);
    }
}
