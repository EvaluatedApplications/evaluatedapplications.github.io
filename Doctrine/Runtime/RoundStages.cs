using Doctrine.Army;
using EvalApp;

namespace Doctrine;

// The round pipeline's steps. Each is a ContextSideEffectStep: it receives the GlobalContext (services + pipeline refs), the
// DomainContext (rules) and the task data (RoundState), and returns the next RoundState. A step only calls a seam; it never
// names a stub or a package type, so replacing a section changes no line here.

abstract class RoundStage(string name) : ContextSideEffectStep<DoctrineContext, DoctrineDomain, RoundState>
{
    // The observer hears every stage by its Stages name (an optional watcher: one null check when there is none).
    protected sealed override ValueTask<RoundState> ExecuteAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        g.Observer?.StageEntered(name, s.Round);
        return StepAsync(s, g, d, ct);
    }
    protected abstract ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct);
}

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
            int n = Math.Max(1, (i < 2 ? gold / 2 : gold) / ArmyRules.Default.DraftPrice(kind));
            if (n * ArmyRules.Default.DraftPrice(kind) > gold) continue;
            picks.Add(new(kind, n)); gold -= n * ArmyRules.Default.DraftPrice(kind);
        }
        return picks;
    }

    public static DoctrineSet DoctrineFor(int round, int seed, LegalMask legal)
    {
        var rng = new GameRng(GameRng.Mix(seed, 2000 + round));
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows).ToList();
        var tokens = new List<int>();
        for (int i = 0; i < 3 && pool.Count > 0; i++) tokens.Add(pool[rng.Next(pool.Count)]);
        return DoctrineSet.Of(tokens.ToArray());
    }
}

/// <summary>The round's pack offer, scaled to the purse as the round opens (CONTRACT.md, "The offers"). With the offers off it is the open market.</summary>
sealed class OfferUnitsStage() : RoundStage(Stages.OfferUnits)
{
    protected override ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        int gold = s.Player.Read().Gold;
        var offer = d.Offers.Enabled ? g.Offers.Units(s.Seed, s.Round, gold, d.Offers) : UnitOffer.Open(gold);
        return ValueTask.FromResult(s with { Units = offer });
    }
}

/// <summary>
/// The order offer, drawn from the legal list AS THE DRAFT LEFT IT (the draft changes gold, lines and kinds, so it changes what is legal). The player may
/// pay for a reroll while rerolls are left: the fee goes to the army (<see cref="IArmy.PayAsync"/>), the new offer is drawn from the reroll's own
/// stream and from the legal list as the payment left it. A reroll the purse cannot pay is refused, counted, and ends the asking.
/// </summary>
sealed class OfferOrdersStage() : RoundStage(Stages.OfferOrders)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var rules = d.Offers;
        if (!rules.Enabled) return s with { OrderOffer = OrderOffer.Open() };
        var army = s.Player.Read();
        var offer = g.Offers.Orders(s.Seed, s.Round, 0, army.Legal, rules);
        int rerolls = 0, refused = s.OfferRefused;
        while (rerolls < rules.MaxRerolls && await g.Player.RerollAsync(army, offer, s.Round, ct))
        {
            if (rules.RerollCost > 0 && !await s.Player.PayAsync(rules.RerollCost, ct)) { refused++; break; }
            rerolls++;
            army = s.Player.Read();
            offer = g.Offers.Orders(s.Seed, s.Round, rerolls, army.Legal, rules);
        }
        return s with { OrderOffer = offer, Rerolls = rerolls, OfferRefused = refused };
    }
}

sealed class DraftStage() : RoundStage(Stages.ArmyDraft)
{
    /// <summary>The reason an army-less refusal carries: the pick is not one of the offered packs (or the pack was already bought this round).</summary>
    public const string NotOffered = "not on offer";

    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var offer = s.Units ?? UnitOffer.Open(s.Player.Read().Gold);
        IReadOnlyList<DraftPick> picks = await g.Player.DraftAsync(s.Player.Read(), offer, s.Round, ct);
        // THE OFFER BINDS THE PLAYER: a pick that is not exactly an offered pack (kind and count), or a pack bought twice, never reaches the army; it comes
        // back in the report with its reason and is counted in the record. The army still judges the gold of what is left.
        var notOffered = new List<(DraftPick Pick, string Reason)>();
        if (offer.Active && d.Offers.Enforce)
        {
            var left = offer.Packs.ToList();
            var ok = new List<DraftPick>();
            foreach (var p in picks)
            {
                int i = left.FindIndex(k => k.Kind == p.Kind && k.Count == p.Count);
                if (i < 0) notOffered.Add((p, NotOffered)); else { left.RemoveAt(i); ok.Add(p); }
            }
            picks = ok;
        }
        var report = await s.Player.DraftAsync(new DraftRequest(s.Round, s.Seed, picks), ct);
        if (notOffered.Count > 0) report = report with { Refused = report.Refused.Concat(notOffered).ToList() };
        await g.Player.DraftedAsync(report, ct);                       // the army's own verdict goes back to the player's hands
        var enemy = g.Armies.Create(Side.Enemy, (int)GameRng.Mix(s.Seed, s.Round));
        int gold = d.EnemyBaseGold + d.EnemyGoldPerRound * s.Round;
        await enemy.DraftAsync(new DraftRequest(s.Round, s.Seed, ScriptedOpponent.Picks(s.Round, s.Seed, gold)), ct);
        return s with { Enemy = enemy, OfferRefused = s.OfferRefused + notOffered.Count };
    }
}

sealed class SuggestStage() : RoundStage(Stages.GeneralSuggest)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var input = GeneralSummary.Build(s.Player.Read(), s.Enemy!.Read(), s.LastOutcome, s.LastPlayerWon);
        // THE GENERAL'S ACTION SPACE IS "PICK UP TO FOUR OF THE OFFER": its mask is the offer AND the legal list. Only the mask changes; the net's input does not.
        if (s.OrderOffer is { Active: true } offer) input = input with { Legal = new LegalMask(input.Legal.Bits & offer.Mask.Bits) };
        var ghost = await g.General.SuggestAsync(input, ct);
        // THE GENERAL IS BOUND BY THE START MASK (CONTRACT.md, "Who judges a token"): a token it offers that the army does not allow at the
        // start of the round is removed here and counted, so the player never sees it and a broken general is visible, never silent.
        int dropped = 0;
        int Keep(int t) { if (t != 0 && !input.Legal.Allows(t)) { dropped++; return 0; } return t; }
        var gd = ghost.Doctrine;
        if (!input.Legal.AllowsAll(gd)) ghost = ghost with { Doctrine = new DoctrineSet(Keep(gd.S0), Keep(gd.S1), Keep(gd.S2), Keep(gd.S3)) };
        return s with { Input = input, Ghost = ghost, IllegalDropped = dropped };
    }
}

sealed class ChooseStage() : RoundStage(Stages.PlayerChoose)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var offer = s.OrderOffer ?? OrderOffer.Open();
        var choice = await g.Player.ChooseAsync(s.Player.Read(), s.Ghost!, offer, s.Round, ct);
        // WHO JUDGES A TOKEN (CONTRACT.md): the start mask binds the GENERAL (SuggestStage), not the player. Here a token is removed and
        // counted only when it is not in the vocabulary at all, or is a FIELD or TRIGGER order the start mask does not allow (the army never judges
        // field orders, and a group with no cohort has nobody to order). An ARMY order goes to the army whole: it judges each one, in slot
        // order, against the army as the earlier tokens left it, so a token illegal at the start may be legal by its turn. What the army
        // skips is counted in RoundRecord.Orders, and the player's doctrine keeps it (the record shows what was asked).
        var legal = s.Input!.Legal;
        int dropped = s.IllegalDropped;                                  // the general's, from the suggest stage
        // THE OFFER BINDS THE PLAYER: a token that is not on offer, or one picked twice, is removed and counted apart (OfferRefused); the pick is "up to four of the eight, each once".
        int refused = s.OfferRefused;
        var picked = new HashSet<int>();
        bool binding = offer.Active && d.Offers.Enforce;
        int Keep(int t)
        {
            if (t == 0) return 0;
            if (DoctrineVocabulary.IsValid(t) && binding && (!offer.Allows(t) || !picked.Add(t))) { refused++; return 0; }
            if (!DoctrineVocabulary.IsValid(t) ||(DoctrineVocabulary.Decode(t).Family is OrderFamily.Field or OrderFamily.Trigger && !legal.Allows(t))) { dropped++; return 0; }
            return t;
        }
        var c = choice.Doctrine;
        var clean = new DoctrineSet(Keep(c.S0), Keep(c.S1), Keep(c.S2), Keep(c.S3));
        return s with { Choice = choice with { Doctrine = clean }, IllegalDropped = dropped, OfferRefused = refused };
    }
}

sealed class ImitateStage() : RoundStage(Stages.GeneralImitate)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await g.General.LearnAsync(new GeneralExample(s.Input!, s.Choice!.Doctrine, s.Choice.Overrode), ct);
        return s;
    }
}

sealed class OrdersStage() : RoundStage(Stages.ArmyOrders)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var report = await s.Player.ApplyArmyOrdersAsync(s.Choice!.Doctrine, ct);
        var enemyDoctrine = ScriptedOpponent.DoctrineFor(s.Round, s.Seed, s.Enemy!.Read().Legal);
        await s.Enemy.ApplyArmyOrdersAsync(enemyDoctrine, ct);
        return s with { EnemyDoctrine = enemyDoctrine, Orders = report };
    }
}

sealed class SetupStage() : RoundStage(Stages.BattleSetup)
{
    protected override ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var arenas = g.Battle.Arenas;
        var arena = arenas[(int)(GameRng.Mix(s.Seed, 3000 + s.Round) % (ulong)arenas.Count)];
        // A siege arena names a besieger, who carries the siege clock; the roles swap every round, so the player besieges in even rounds and the enemy in odd ones.
        Side? besieger = d.Siege && arena.IsSiege ? SiegeRules.BesiegerOf(s.Round, d.SwapSiegeRoles) : null;
        var setup = new BattleSetup(arena.Id, (int)GameRng.Mix(s.Seed, 4000 + s.Round), d.TickLimit,
            new BattleSide(Side.Player, s.Player.Read().Cohorts, DoctrineVocabulary.FieldOrders(s.Choice!.Doctrine), DoctrineVocabulary.Trigger(s.Choice.Doctrine)),
            new BattleSide(Side.Enemy, s.Enemy!.Read().Cohorts, DoctrineVocabulary.FieldOrders(s.EnemyDoctrine), DoctrineVocabulary.Trigger(s.EnemyDoctrine)), besieger);
        return ValueTask.FromResult(s with { Setup = setup });
    }
}

sealed class RunBattleStage() : RoundStage(Stages.BattleRun)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        var run = await g.BattleRun.RunAsync(new BattleState(s.Setup!), ct);   // ct flows down
        var b = PipelineFailure.DataOrThrow(run, $"the battle of round {s.Round} (seed {s.Seed}, arena {s.Setup!.ArenaId})");
        return s with { Result = b.Result, Replay = b.Replay };
    }
}

sealed class LossesStage() : RoundStage(Stages.ArmyLosses)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await s.Player.ApplyLossesAsync(s.Result!.AttackerLosses, ct);
        await s.Enemy!.ApplyLossesAsync(s.Result.DefenderLosses, ct);
        return s;
    }
}

sealed class SpoilsStage() : RoundStage(Stages.ArmySpoils)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        bool won = s.Result!.AttackerWon;
        int killed = s.Result.DefenderLosses.Sum(l => l.Lost);
        var spoils = d.FlatSpoils ? new Spoils(killed / 2 + 4, 0) : new Spoils(killed / 2 + (won ? 6 : 2), won ? 1 : 0);
        if (d.Comeback && s.Round + 1 < d.MaxRounds)
        {
            // THE MUSTER: half of what the army lacks against the strength the next enemy is raised at, paid in gold (it cannot lift an army above parity).
            var read = s.Player.Read();
            long wealth = read.Gold + spoils.Gold + read.Cohorts.Sum(c => (long)c.Count * Roster.PriceOf(c.Key));
            int muster = Roster.Muster(wealth, d.EnemyBaseGold + d.EnemyGoldPerRound * (s.Round + 1));
            if (muster > 0) spoils = spoils with { Gold = spoils.Gold + muster, Muster = muster };
        }
        await s.Player.ApplySpoilsAsync(spoils, ct);
        return s with { Spoils = spoils };
    }
}

sealed class OutcomeStage() : RoundStage(Stages.GeneralOutcome)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        await g.General.LearnOutcomeAsync(new GeneralOutcome(s.Input!, s.Choice!.Doctrine, s.Result!.Outcome, s.Result.AttackerWon), ct);
        return s;
    }
}

sealed class CloseStage() : RoundStage(Stages.RoundClose)
{
    protected override async ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
    {
        ulong playerDigest = s.Player.Read().Digest(), enemyDigest = s.Enemy!.Read().Digest();   // taken before the round ends, as ever
        await s.Player.EndRoundAsync(ct);
        var after = s.Player.Read();
        bool done = s.Round + 1 >= d.MaxRounds || (after.Soldiers == 0 && after.Gold < 1);   // an army with no soldiers and no gold cannot go on
        var record = new RoundRecord(s.Round, s.Choice!.Doctrine, s.Ghost!.Doctrine, s.Ghost.ConfidencePermille, s.Choice.Overrode,
            s.IllegalDropped, s.Setup!.ArenaId, s.Result!, s.Replay!, s.Spoils!, playerDigest, enemyDigest, s.Orders, done,
            s.Units, s.OrderOffer, s.Rerolls, s.OfferRefused);
        var history = new List<RoundRecord>(s.Records) { record };
        await g.Player.RoundClosedAsync(record, after, ct);          // the host sees the closed round, whole, before the next one starts
        return s with { History = history, Done = done };
    }
}

sealed class AdvanceStage() : RoundStage(Stages.RoundAdvance)
{
    protected override ValueTask<RoundState> StepAsync(RoundState s, DoctrineContext g, DoctrineDomain d, CancellationToken ct)
        => ValueTask.FromResult(s.Advance());
}
