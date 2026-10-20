using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine.Duel;

/// <summary>
/// The game a duel is played on, built the way the single-player root builds it (Pleroma army, Tracer battle) but with no player, observer or general: a duel's hands are the
/// two seats. Both seats and the authority call this with the room's rounds, so they share the rules; the seed is the room's.
/// </summary>
public static class DuelGames
{
    public static DoctrineGame Real(int rounds)
        => DoctrineBuilder.New()
            .WithArmy(new global::Doctrine.Army.PleromaArmyService())
            .WithTracer(new Tracer.PathfindingWorld())
            .WithBattle(ctx => new TracerBattleEngine(ctx.Tracer!))
            .WithRules(new DoctrineDomain { MaxRounds = rounds })
            .Build();
}
