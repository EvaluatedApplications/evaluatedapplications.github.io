using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// THE COMPOSITION ROOT of the Doctrine page: the one place that names which sections the game is made of (Pleroma army, Tracer battle,
// tiny HoloFormer general), through the skeleton's `WithDefaults` hook. No Blazor here, so the wasm probe and the proofs compile this very file.
// The page's own hands (IPlayerInput), its watcher (IRoundObserver) and rules are the caller's, so they sit OUTSIDE the root, as the section
// guide says. No section is wrapped: the arena layouts and the battle progress come from the skeleton's own hooks.

/// <summary>
/// The sections of a Doctrine game. The general lives HERE, not in a game, so it keeps what it has
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

    /// <summary>
    /// Names the real sections once (the root), then the caller's own hands, watcher and rules. An explicit With* by a caller (a proof swapping
    /// a seam) would still beat the root; here only the player, the observer and the rules are explicit.
    /// </summary>
    public DoctrineBuilder Compose(DoctrineBuilder b, IPlayerInput player, DoctrineDomain rules, IRoundObserver? observer = null)
    {
        b = b
            .WithDefaults(root => root
                .WithArmy(new global::Doctrine.Army.PleromaArmyService())
                .WithTracer(_world())                                              // Tracer's own context object, held in ours
                .WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!))
                .WithGeneral(General))
            .WithPlayer(player)
            .WithRules(rules);
        return observer is null ? b : b.WithObserver(observer);
    }

    public DoctrineGame Build(IPlayerInput player, DoctrineDomain rules, IRoundObserver? observer = null)
        => Compose(DoctrineBuilder.New(), player, rules, observer).Build();
}
