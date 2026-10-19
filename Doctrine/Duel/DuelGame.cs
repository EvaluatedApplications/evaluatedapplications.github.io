using Doctrine.Battle;

namespace Doctrine;

/// <summary>
/// The AUTHORITY of a duel: the device that simulates. It holds both seats (two <see cref="DuelSeat"/>, raised from the same seed so they start equal), takes each seat's revealed
/// <see cref="DuelPicks"/>, applies them, fights the battle ONCE with the game's own battle pipeline, and returns one <see cref="DuelRoundResult"/> that both seats read (each through
/// <see cref="DuelRoundResult.ViewFor"/>). It owns no clock and no connection: the host verifies commitments, decides when to give up on a seat, and moves bytes.
/// Created by <see cref="DoctrineGame.NewDuel(int)"/>.
/// </summary>
public sealed class DuelGame
{
    /// <summary>Auto-seals in a row after which a seat is marked idle.</summary>
    public const int IdleAfter = 3;

    readonly DoctrineGame _game;
    readonly DuelProbe _probe;
    readonly DuelSeat _a, _b;
    readonly DuelOpponent _boxA = new(), _boxB = new();
    readonly int[] _idle = new int[2];
    int _fieldsA, _fieldsB;
    ulong _lastDigest;

    internal DuelGame(DoctrineGame game, int seed, DuelProbe probe, int startRound = 0)
    {
        _game = game; _probe = probe; Seed = seed; Round = startRound;
        _a = new DuelSeat(game.Context.Armies, game.Context.Offers, game.Rules, seed, Seat.A, probe, startRound);
        _b = new DuelSeat(game.Context.Armies, game.Context.Offers, game.Rules, seed, Seat.B, probe, startRound);
    }

    public int Seed { get; }
    public int Rounds => _game.Rules.MaxRounds;
    /// <summary>The round to be played next (0-based); equal to <see cref="Rounds"/> when the duel is over.</summary>
    public int Round { get; private set; }
    public bool Finished => Round >= Rounds;

    public DuelSeat SeatOf(Seat seat) => seat == Seat.A ? _a : _b;

    /// <summary>The standing now. The power is read from the armies as they stand, so after the last round it is the end-of-duel tie-break.</summary>
    public DuelScore Score => new(_fieldsA, _fieldsB, DuelScore.PowerOf(_a.Read()), DuelScore.PowerOf(_b.Read()));

    // ---- who besieges, what ground ------------------------------------------------------------------------------------------

    public ArenaInfo ArenaFor(int round) => _game.ArenaFor(Seed, round);

    /// <summary>The seat that besieges in <paramref name="round"/>, or null when its ground is an open field (or sieges are off): seat A in even rounds, seat B in odd ones.</summary>
    public Seat? BesiegerOf(int round) => BesiegerSide(round) is { } s ? SeatExtensions.Of(s) : null;

    Side? BesiegerSide(int round)
        => RoundRules.BesiegerFor(_game.Rules, ArenaFor(round), _probe.HasFlag(DuelProbe.SiegeParityWrong) ? round + 1 : round);

    // ---- the hand-in (a reveal arrives) ------------------------------------------------------------------------------------

    /// <summary>The box holding a seat's revealed picks; also an <see cref="IOpponent"/>, so a single-player pipeline could take it as its enemy.</summary>
    public DuelOpponent OpponentFor(Seat seat) => seat == Seat.A ? _boxA : _boxB;

    /// <summary>Hand in a seat's verified reveal for <paramref name="round"/>. False (ignored) for a round already played or out of range, or when that seat's picks for the round are already in.</summary>
    public bool HandIn(Seat seat, int round, DuelPicks picks) => round >= Round && round < Rounds && OpponentFor(seat).HandIn(round, picks);

    /// <summary>True when both seats' picks for the next round are in.</summary>
    public bool Ready => !Finished && _boxA.Has(Round) && _boxB.Has(Round);

    /// <summary>Play the next round as soon as both seats' picks are handed in (waits, without blocking a thread, for the one that is missing).</summary>
    public async ValueTask<DuelRoundResult> PlayRoundAsync(CancellationToken ct = default)
    {
        if (Finished) throw new InvalidOperationException("the duel is over");
        int round = Round;
        var pa = await _boxA.PicksAsync(round, ct);
        var pb = await _boxB.PicksAsync(round, ct);
        return await PlayAsync(round, pa, pb, ct);
    }

    /// <summary>Hand in both seats' picks and play the next round (what a proof or a test with both reveals in hand calls).</summary>
    public ValueTask<DuelRoundResult> RunRoundAsync(DuelPicks a, DuelPicks b, CancellationToken ct = default)
    {
        HandIn(Seat.A, Round, a); HandIn(Seat.B, Round, b);
        return PlayRoundAsync(ct);
    }

    async ValueTask<DuelRoundResult> PlayAsync(int round, DuelPicks pa, DuelPicks pb, CancellationToken ct)
    {
        var d = _game.Rules; var ctx = _game.Context;
        var sa = await _a.ApplyPicksAsync(pa, ct);
        var sb = await _b.ApplyPicksAsync(pb, ct);

        var arena = RoundRules.ArenaFor(ctx.Battle.Arenas, Seed, round);
        var setup = RoundLaws.Setup(d, arena, Seed, round, BesiegerSide(round), _a.Read(), sa.Doctrine, _b.Read(), sb.Doctrine);
        var run = await ctx.BattleRun.RunAsync(new BattleState(setup), ct);
        var fight = PipelineFailure.DataOrThrow(run, $"the duel battle of round {round} (seed {Seed}, arena {setup.ArenaId})");
        var result = fight.Result!; var replay = fight.Replay!;

        bool aWon = result.Outcome == BattleStatus.AttackerWon, bWon = result.Outcome == BattleStatus.DefenderWon;
        var spoilsA = RoundLaws.BaseSpoils(d, aWon, result.DefenderLosses.Sum(l => l.Lost));
        var spoilsB = RoundLaws.BaseSpoils(d, bWon, result.AttackerLosses.Sum(l => l.Lost));
        ulong closeA = await _a.SettleAsync(result.AttackerLosses, spoilsA, ct);
        ulong closeB = await _b.SettleAsync(result.DefenderLosses, spoilsB, ct);
        if (aWon) _fieldsA++; else if (bWon) _fieldsB++;
        await _a.EndRoundAsync(ct); await _b.EndRoundAsync(ct);
        Round = round + 1;

        var res = new DuelRoundResult(round, replay, sa with { Spoils = spoilsA, DigestAtClose = closeA }, sb with { Spoils = spoilsB, DigestAtClose = closeB }, Finished, Score);
        _lastDigest = res.Digest();
        return res;
    }

    /// <summary>The digest of the last round's result (0 before the first).</summary>
    public ulong LastDigest => _lastDigest;

    // ---- idle accounting (host-side policy: the host seals for a seat that did not, and says so here) ---------------------------

    /// <summary>The host sealed for this seat because its time ran out. Counts a streak.</summary>
    public void MarkAutoSealed(Seat seat) => _idle[(int)seat]++;
    /// <summary>The seat sealed for itself: the streak ends.</summary>
    public void MarkSealed(Seat seat) => _idle[(int)seat] = 0;
    /// <summary>Auto-seals in a row so far.</summary>
    public int IdleStreak(Seat seat) => _idle[(int)seat];
    /// <summary>True once <see cref="IdleAfter"/> auto-seals have come in a row.</summary>
    public bool IsIdle(Seat seat) => _idle[(int)seat] >= IdleAfter;

    /// <summary>The viewer's reading of a result (see <see cref="DuelRoundResult.ViewFor"/>); the authority's probe is honoured in tests.</summary>
    public DuelSeatView ViewOf(DuelRoundResult result, Seat viewer) => result.ViewFor(viewer, _probe);
}

public sealed partial class DoctrineGame
{
    /// <summary>
    /// The authority of a duel of <see cref="DoctrineDomain.MaxRounds"/> rounds from <paramref name="seed"/> (both seats must build their game with the same rules and the same seed).
    /// The single-player pipeline is untouched: a duel is a second way to play the same sections.
    /// </summary>
    public DuelGame NewDuel(int seed) => new(this, seed, DuelProbe.None);

    /// <summary>Test and harness hook: a fresh duel that opens at <paramref name="startRound"/> (the ground, the besieger and the offers are those of that round; the armies are the opening ones).</summary>
    internal DuelGame NewDuel(int seed, DuelProbe probe, int startRound = 0) => new(this, seed, probe, startRound);

    /// <summary>A client's own seat: its army built locally from the same seed, the same offers, the same rules (see <see cref="DuelSeat"/>).</summary>
    public DuelSeat NewDuelSeat(int seed, Seat seat) => new(Context.Armies, Context.Offers, Rules, seed, seat);
}
