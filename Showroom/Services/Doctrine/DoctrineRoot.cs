using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// THE COMPOSITION ROOT of the Doctrine page: the one place that names which sections the game is made of (Pleroma army, Tracer battle,
// tiny HoloFormer general), through the skeleton's `WithDefaults` hook. No Blazor here, so the wasm probe and the proofs compile this very file.
// The page's own hands (IPlayerInput) and rules are the caller's, so they sit OUTSIDE the root, as the section guide says.

/// <summary>Watches a battle without changing it: the page shows progress from these three events. Everything is passed straight through.</summary>
public sealed class BattleObserver
{
    public event Action<BattleSetup>? Started;
    public event Action<int>? Ticked;
    public event Action<BattleResult>? Finished;
    internal void OnStarted(BattleSetup s) => Started?.Invoke(s);
    internal void OnTicked(int tick) => Ticked?.Invoke(tick);
    internal void OnFinished(BattleResult r) => Finished?.Invoke(r);
}

sealed class ObservedEngine(IBattleEngine inner, BattleObserver observer) : IBattleEngine
{
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct)
    {
        var session = await inner.StartAsync(setup, ct);
        observer.OnStarted(setup);
        return new ObservedSession(session, observer);
    }
}

sealed class ObservedSession(IBattleSession inner, BattleObserver observer) : IBattleSession
{
    public int Tick => inner.Tick;
    public BattleStatus Status => inner.Status;
    public ValueTask FieldOrdersAsync(CancellationToken ct) => inner.FieldOrdersAsync(ct);
    public ValueTask MoveAsync(CancellationToken ct) => inner.MoveAsync(ct);
    public ValueTask EngageAsync(CancellationToken ct) => inner.EngageAsync(ct);
    public ValueTask ApplyLossesAsync(CancellationToken ct) => inner.ApplyLossesAsync(ct);
    public ReplayFrame Snapshot() => inner.Snapshot();
    public ReplayFrame Record() { var f = inner.Record(); observer.OnTicked(inner.Tick); return f; }
    public BattleResult Finish(bool tickLimitReached) { var r = inner.Finish(tickLimitReached); observer.OnFinished(r); return r; }
}

/// <summary>
/// The sections of a Doctrine game and the handles the page needs from them. The general lives HERE, not in a game, so it keeps what it has
/// learned across new games in one page visit (a refresh forgets it: it has no saved form).
/// </summary>
public sealed class DoctrineSections
{
    readonly Func<Tracer.PathfindingWorld> _world;

    public DoctrineSections(IGeneral? general = null, Func<Tracer.PathfindingWorld>? world = null)
    {
        General = general ?? new global::Doctrine.General.TinyHoloGeneral();
        _world = world ?? (() => new Tracer.PathfindingWorld());
    }

    /// <summary>The player's general: empty at the start, taught only by this player's own doctrines and results.</summary>
    public IGeneral General { get; }
    public BattleObserver Observer { get; } = new();
    /// <summary>The Tracer engine of the game most recently built (set when <see cref="Compose"/>'s builder builds). Its LayoutOf draws the arena.</summary>
    public TracerBattleEngine? Engine { get; private set; }
    public ArenaLayout? LayoutOf(string arenaId) => Engine?.LayoutOf(arenaId);

    /// <summary>
    /// Names the real sections once (the root), then the caller's own hands and rules. An explicit With* by a caller (a proof swapping a
    /// seam) would still beat the root; here only the player and the rules are explicit.
    /// </summary>
    public DoctrineBuilder Compose(DoctrineBuilder b, IPlayerInput player, DoctrineDomain rules) => b
        .WithDefaults(root => root
            .WithArmy(new global::Doctrine.Army.PleromaArmyService())
            .WithTracer(_world())                                              // Tracer's own context object, held in ours
            .WithBattle(ctx =>
            {
                Engine = new TracerBattleEngine(ctx.Tracer!);
                return new ObservedEngine(Engine, Observer);
            })
            .WithGeneral(General))
        .WithPlayer(player)
        .WithRules(rules);

    public DoctrineGame Build(IPlayerInput player, DoctrineDomain rules) => Compose(DoctrineBuilder.New(), player, rules).Build();
}
