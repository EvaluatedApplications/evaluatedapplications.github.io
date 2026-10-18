using EvalApp;
using Tracer.Integration;

namespace Doctrine;

/// <summary>
/// The compiled game: one EvalApp app, one domain, three pipelines wired through the context.
/// <c>Round</c> = draft, doctrine, battle, spoils, then the next round (recursion through a PipelineRef).
/// <c>Battle</c> = begin, tick until a win, loss or the tick limit, finish. <c>BattleTick</c> = one tick.
/// </summary>
public sealed class DoctrineGame
{
    public DoctrineContext Context { get; }
    public DoctrineDomain Rules { get; }
    public ICompiledPipeline<RoundState> Round => Context.Round;
    public ICompiledPipeline<BattleState> Battle => Context.BattleRun;
    public ICompiledPipeline<BattleState> Tick => Context.BattleTick;

    internal DoctrineGame(DoctrineContext ctx, DoctrineDomain rules, bool? singleThread = null)
    {
        Context = ctx; Rules = rules;
        // Left alone, EvalApp detects a one-thread host (a browser, one core) by itself; a pin is for tests and probes.
        var app = Eval.App("Doctrine").WithContext(ctx);
        if (singleThread is bool pin) app = app.WithSingleThread(pin);
        // The Tick pipeline is declared first only for readability; refs make order irrelevant.
        app
            .DefineDomain("Doctrine", rules)
                .DefineTask<BattleState>("BattleTick")
                    .AddStep<FieldOrdersStage>(Stages.BattleFieldOrders)
                    .AddStep<MoveStage>(Stages.BattleMove)
                    .AddStep<EngageStage>(Stages.BattleEngage)
                    .AddStep<ApplyLossesBattleStage>(Stages.BattleApplyLosses)
                    .AddStep<RecordStage>(Stages.BattleRecord)
                    .AddStep<JudgeStage>(Stages.BattleJudge)
                .Run(ctx.BattleTick)
                .DefineTask<BattleState>("Battle")
                    .AddStep<BeginStage>(Stages.BattleBegin)
                    .AddStep<TicksStage>(Stages.BattleTicks)
                    .AddStep<FinishStage>(Stages.BattleFinish)
                .Run(ctx.BattleRun)
                .DefineTask<RoundState>("Round")
                    .AddStep<DraftStage>(Stages.ArmyDraft)
                    .AddStep<SuggestStage>(Stages.GeneralSuggest)
                    .AddStep<ChooseStage>(Stages.PlayerChoose)
                    .AddStep<ImitateStage>(Stages.GeneralImitate)
                    .AddStep<OrdersStage>(Stages.ArmyOrders)
                    .AddStep<SetupStage>(Stages.BattleSetup)
                    .AddStep<RunBattleStage>(Stages.BattleRun)
                    .AddStep<LossesStage>(Stages.ArmyLosses)
                    .AddStep<SpoilsStage>(Stages.ArmySpoils)
                    .AddStep<OutcomeStage>(Stages.GeneralOutcome)
                    .AddStep<CloseStage>(Stages.RoundClose)
                    .If(s => !s.Done, then: b => b
                        .AddStep<AdvanceStage>(Stages.RoundAdvance)
                        .AddStep(Stages.RoundNext, ctx.Round))        // a round feeds the next: this pipeline, by reference
                .Run(ctx.Round)
            .Build();
    }

    /// <summary>A fresh opening record for a game.</summary>
    public RoundState NewGame(int seed) => new(0, seed, Context.Armies.Create(Side.Player, seed));

    /// <summary>Play the whole game headless: <paramref name="rounds"/> rounds from <paramref name="seed"/>. Deterministic for fixed seams.</summary>
    public async ValueTask<GameResult> PlayAsync(int seed, CancellationToken ct = default)
    {
        var run = await Context.Round.RunAsync(NewGame(seed), ct);
        if (run is PipelineResult<RoundState>.Failure f) throw f.Exception;
        var s = run.GetData();
        return new GameResult(seed, s.Records, s.Player.ToCode());
    }
}

/// <summary>Settings and services collected before the game compiles. Use the <c>With*</c> extensions; <c>Build()</c> fills any seam you left with its stub.</summary>
public sealed class DoctrineBuilder
{
    internal IArmyService? Armies; internal IBattleEngine? Battle; internal IGeneral? General; internal IPlayerInput? Player;
    internal Func<DoctrineContext, IBattleEngine>? BattleFactory;
    internal TracerContext? Tracer;
    internal DoctrineDomain Rules = new();
    internal bool? SingleThread;

    public static DoctrineBuilder New() => new();

    public DoctrineGame Build()
    {
        var armies = Armies ?? new global::Doctrine.Army.StubArmyService();
        var player = Player ?? new global::Doctrine.Player.ScriptedPlayer(1);
        var general = General ?? new global::Doctrine.General.TableLookupGeneral();
        var ctx = new DoctrineContext(armies, Battle ?? new global::Doctrine.Battle.StubBattleEngine(), general, player, Tracer);
        // A battle engine can need the finished context (it reads the Tracer context out of it), so a factory is resolved last.
        if (BattleFactory is not null) ctx.Battle = BattleFactory(ctx);
        return new DoctrineGame(ctx, Rules, SingleThread);
    }
}

/// <summary>Builder extensions in the published-extension style: <c>Doctrine.New().WithArmy(..).WithBattle(..).Build()</c>.</summary>
public static class DoctrineBuilderExtensions
{
    public static DoctrineBuilder WithArmy(this DoctrineBuilder b, IArmyService armies) { b.Armies = armies; return b; }
    public static DoctrineBuilder WithBattle(this DoctrineBuilder b, IBattleEngine engine) { b.Battle = engine; b.BattleFactory = null; return b; }
    /// <summary>Late-bound battle engine: the factory receives the context so it can read <see cref="DoctrineContext.Tracer"/>.</summary>
    public static DoctrineBuilder WithBattle(this DoctrineBuilder b, Func<DoctrineContext, IBattleEngine> factory) { b.BattleFactory = factory; return b; }
    public static DoctrineBuilder WithGeneral(this DoctrineBuilder b, IGeneral general) { b.General = general; return b; }
    public static DoctrineBuilder WithPlayer(this DoctrineBuilder b, IPlayerInput player) { b.Player = player; return b; }
    /// <summary>Rules for the game. MaxRounds is limited to 200: each round runs the next, and EvalApp stops a pipeline nested 256 deep.</summary>
    public static DoctrineBuilder WithRules(this DoctrineBuilder b, DoctrineDomain rules)
    {
        if (rules.MaxRounds is < 1 or > DoctrineDomain.MaxRoundsLimit) throw new ArgumentOutOfRangeException(nameof(rules), $"MaxRounds must be 1..{DoctrineDomain.MaxRoundsLimit}");
        if (rules.TickLimit < 1) throw new ArgumentOutOfRangeException(nameof(rules), "TickLimit must be at least 1");
        b.Rules = rules; return b;
    }
    /// <summary>Pin EvalApp to (or off) its one-thread mode for this game. Normally leave it alone: the host is detected.</summary>
    public static DoctrineBuilder WithSingleThread(this DoctrineBuilder b, bool pin) { b.SingleThread = pin; return b; }
    /// <summary>
    /// Compose with the Tracer package's own EvalApp extension. Tracer's <c>WithTracer(world)</c> installs its context as THE global
    /// context (one slot); here we build the SAME object it would build and hold it in ours, so nothing fights over the slot.
    /// </summary>
    public static DoctrineBuilder WithTracer(this DoctrineBuilder b, Tracer.PathfindingWorld world,
        Tracer.Abstractions.IEngagementService? engagement = null, Tracer.Abstractions.IAdaptivePathfindingService? adaptive = null,
        Tracer.Abstractions.ISimulationService? simulation = null)
    { b.Tracer = new TracerContext(world, engagement, adaptive, simulation); return b; }
}
