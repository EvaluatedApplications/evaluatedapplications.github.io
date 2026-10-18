using EvalApp;

namespace Doctrine;

// The round pipeline's steps. Each is a ContextSideEffectStep: it receives the GlobalContext (services + pipeline refs), the
// DomainContext (rules) and the task data (RoundState), and returns the next RoundState. A step only calls a seam; it never
// names a stub or a package type, so replacing a section changes no line here.

abstract class RoundStage : ContextSideEffectStep<DoctrineContext, DoctrineDomain, RoundState> { }

/// <summary>Opponent behaviour for the headless game and the training ground: scripted, seeded, round-scaled. Not a section.</summary>
static class ScriptedOpponent
{
    public static IReadOnlyList<DraftPick> Picks(int round, int seed, int gold)
    {
        var rng = new GameRng(GameRng.Mix(seed, 1000 + round));
        var picks = new List<DraftPick>();
        for (int i = 0; i < 3 && gold > 0; i++)
        {
            int kind = rng.Next(Roster.Size);
            int n = Math.Max(1, (i < 2 ? gold / 2 : gold) / Roster.PriceOf(kind));
            if (n * Roster.PriceOf(kind) > gold) continue;
            picks.Add(new(kind, n)); gold -= n * Roster.PriceOf(kind);
        }
        return picks;
    }

    public static Doctrine DoctrineFor(int round, int seed, LegalMask legal)
    {
        var rng = new GameRng(GameRng.Mix(seed, 2000 + round));
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows).ToList();
        var tokens = new List<int>();
        for (int i = 0; i < 3 && pool.Count > 0; i++) tokens.Add(pool[rng.Next(pool.Count)]);
        return Doctrine.Of(tokens.ToArray());
    }
}

sealed class DraftStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var picks = await g.Player.DraftAsync(s.Player.Read(), s.Round, ct);
        await s.Player.DraftAsync(new DraftRequest(s.Round, s.Seed, picks), ct);
        var enemy = g.Armies.Create(Side.Enemy, (int)GameRng.Mix(s.Seed, s.Round));
        int gold = d.EnemyBaseGold + d.EnemyGoldPerRound * s.Round;
        await enemy.DraftAsync(new DraftRequest(s.Round, s.Seed, ScriptedOpponent.Picks(s.Round, s.Seed, gold)), ct);
        return s with { Enemy = enemy };
    }
}

sealed class SuggestStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var input = GeneralSummary.Build(s.Player.Read(), s.Enemy!.Read(), s.LastOutcome, s.LastPlayerWon);
        var ghost = await g.General.SuggestAsync(input, ct);
        return s with { Input = input, Ghost = ghost };
    }
}

sealed class ChooseStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var choice = await g.Player.ChooseAsync(s.Player.Read(), s.Ghost!, s.Round, ct);
        // The legal mask is a rule: a token the army does not allow is dropped here and counted, never applied.
        var legal = s.Input!.Legal;
        int dropped = 0;
        int Keep(int t) { if (t != 0 && !legal.Allows(t)) { dropped++; return 0; } return t; }
        var c = choice.Doctrine;
        var clean = new Doctrine(Keep(c.S0), Keep(c.S1), Keep(c.S2), Keep(c.S3));
        return s with { Choice = choice with { Doctrine = clean }, IllegalDropped = dropped };
    }
}

sealed class ImitateStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await g.General.LearnAsync(new GeneralExample(s.Input!, s.Choice!.Doctrine, s.Choice.Overrode), ct);
        return s;
    }
}

sealed class OrdersStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await s.Player.ApplyArmyOrdersAsync(s.Choice!.Doctrine, ct);
        var enemyDoctrine = ScriptedOpponent.DoctrineFor(s.Round, s.Seed, s.Enemy!.Read().Legal);
        await s.Enemy.ApplyArmyOrdersAsync(enemyDoctrine, ct);
        return s with { EnemyDoctrine = enemyDoctrine };
    }
}

sealed class SetupStage : RoundStage
{
    protected override ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var arenas = g.Battle.Arenas;
        var arena = arenas[(int)(GameRng.Mix(s.Seed, 3000 + s.Round) % (ulong)arenas.Count)];
        var setup = new BattleSetup(arena.Id, (int)GameRng.Mix(s.Seed, 4000 + s.Round), d.TickLimit,
            new BattleSide(Side.Player, s.Player.Read().Cohorts, DoctrineVocabulary.FieldOrders(s.Choice!.Doctrine)),
            new BattleSide(Side.Enemy, s.Enemy!.Read().Cohorts, DoctrineVocabulary.FieldOrders(s.EnemyDoctrine)));
        return ValueTask.FromResult(s with { Setup = setup });
    }
}

sealed class RunBattleStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var run = await g.BattleRun.RunAsync(new BattleState(s.Setup!), ct);   // ct flows down
        if (run is PipelineResult<BattleState>.Failure f) throw f.Exception;
        var b = run.GetData();
        return s with { Result = b.Result, Replay = b.Replay };
    }
}

sealed class LossesStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await s.Player.ApplyLossesAsync(s.Result!.AttackerLosses, ct);
        await s.Enemy!.ApplyLossesAsync(s.Result.DefenderLosses, ct);
        return s;
    }
}

sealed class SpoilsStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        bool won = s.Result!.AttackerWon;
        int killed = s.Result.DefenderLosses.Sum(l => l.Lost);
        var spoils = new Spoils(killed / 2 + (won ? 6 : 2), won ? 1 : 0);
        await s.Player.ApplySpoilsAsync(spoils, ct);
        return s with { Spoils = spoils };
    }
}

sealed class OutcomeStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await g.General.LearnOutcomeAsync(new GeneralOutcome(s.Input!, s.Choice!.Doctrine, s.Result!.Outcome, s.Result.AttackerWon), ct);
        return s;
    }
}

sealed class CloseStage : RoundStage
{
    protected override async ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var record = new RoundRecord(s.Round, s.Choice!.Doctrine, s.Ghost!.Doctrine, s.Ghost.ConfidencePermille, s.Choice.Overrode,
            s.IllegalDropped, s.Setup!.ArenaId, s.Result!, s.Replay!, s.Spoils!, s.Player.Read().Digest(), s.Enemy!.Read().Digest());
        await s.Player.EndRoundAsync(ct);
        var history = new List<RoundRecord>(s.Records) { record };
        var after = s.Player.Read();
        bool done = s.Round + 1 >= d.MaxRounds || (after.Soldiers == 0 && after.Gold < 1);   // an army with no soldiers and no gold cannot go on
        return s with { History = history, Done = done };
    }
}

sealed class AdvanceStage : RoundStage
{
    protected override ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
        => ValueTask.FromResult(s.Advance());
}
