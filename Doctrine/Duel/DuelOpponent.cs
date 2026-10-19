namespace Doctrine;

/// <summary>
/// The duel implementation of <see cref="IOpponent"/>: a seat's picks, handed in when its REVEAL arrives. Every question the game asks waits (asynchronously, no thread is
/// blocked) until <see cref="HandIn"/> has delivered that round's picks, then answers from them. A round is handed in once: a second hand-in of the same round is ignored,
/// so a duplicated message does nothing. Nothing here knows a network; the host calls <see cref="HandIn"/> from wherever the reveal came.
/// </summary>
public sealed class DuelOpponent : IOpponent
{
    readonly object _gate = new();
    readonly Dictionary<int, TaskCompletionSource<DuelPicks>> _rounds = new();

    TaskCompletionSource<DuelPicks> Slot(int round)
    {
        lock (_gate)
        {
            if (!_rounds.TryGetValue(round, out var tcs)) _rounds[round] = tcs = new TaskCompletionSource<DuelPicks>(TaskCreationOptions.RunContinuationsAsynchronously);
            return tcs;
        }
    }

    /// <summary>Deliver the picks of <paramref name="round"/>. True when they were taken; false when that round already has picks (the duplicate is ignored, the first stands).</summary>
    public bool HandIn(int round, DuelPicks picks) => Slot(round).TrySetResult(picks);

    /// <summary>True once the picks of <paramref name="round"/> have been handed in.</summary>
    public bool Has(int round) => Slot(round).Task.IsCompletedSuccessfully;

    /// <summary>The picks of <paramref name="round"/>, as soon as they are handed in.</summary>
    public Task<DuelPicks> PicksAsync(int round, CancellationToken ct = default) => Slot(round).Task.WaitAsync(ct);

    public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound round, CancellationToken ct) => (await PicksAsync(round.Round, ct)).Units;
    public async ValueTask<int> FormationAsync(OpponentRound round, CancellationToken ct) => (await PicksAsync(round.Round, ct)).Formation;
    public async ValueTask<DoctrineSet> DoctrineAsync(OpponentRound round, LegalMask legal, CancellationToken ct) => (await PicksAsync(round.Round, ct)).Doctrine;
}
