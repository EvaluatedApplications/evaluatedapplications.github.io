namespace Doctrine;

// THE OPPONENT SEAM (duel round, 2026-10-19; Doctrine\todo\duel-design-2026-10-19.md). The other side of a battle used to be a static class that
// the round pipeline called by name. It is now a seam: the pipeline asks an IOpponent for the three things a side decides before a battle (what to
// buy, how to stand, which orders to give). The SCRIPTED opponent is the old enemy, byte for byte; a duel hands in what a remote seat revealed.

/// <summary>What the opponent is asked about: the game seed, the round, and the gold its army was raised with (for the scripted enemy: the round's enemy gold).</summary>
public readonly record struct OpponentRound(int Seed, int Round, int Gold);

/// <summary>
/// The decisions of the other side, asked in this order each round: <see cref="DraftAsync"/> (right after its army is raised), <see cref="FormationAsync"/>
/// (once the packs are bought), <see cref="DoctrineAsync"/> (when the orders are given, with the legal list its army holds then). All three are async so
/// an answer may come from another device; none may block a thread. A returned doctrine is raw: the game still judges every token.
/// </summary>
public interface IOpponent
{
    /// <summary>The packs to buy. The game applies them in kind order, so the order of the list changes nothing.</summary>
    ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound round, CancellationToken ct);
    /// <summary>The free turn of the ranks after the draft: 0 leaves them, 1 or 2 is a Rotate by that many places (the army's own Rotate, not a new rule). Anything else is refused.</summary>
    ValueTask<int> FormationAsync(OpponentRound round, CancellationToken ct);
    /// <summary>The doctrine (up to four tokens, slot order) given with the army's legal list as it stands.</summary>
    ValueTask<DoctrineSet> DoctrineAsync(OpponentRound round, LegalMask legal, CancellationToken ct);
    /// <summary>
    /// The packs this opponent WILL buy in <paramref name="round"/>, if that is knowable ahead (the briefing shows the enemy before the player chooses). Null when it is not:
    /// a remote seat's picks are secret until revealed, so the briefing refuses such an opponent instead of showing a guess.
    /// </summary>
    IReadOnlyList<DraftPick>? Preview(OpponentRound round) => null;
}

/// <summary>
/// The enemy of the single-player game: seeded, round-scaled, and blind to the player. Its two rules are plain functions of (seed, round) so the
/// training ground and the briefing can read them without a game. The instance is the <see cref="IOpponent"/> the game uses unless told otherwise.
/// </summary>
public sealed class ScriptedOpponent : IOpponent
{
    public static ScriptedOpponent Instance { get; } = new();

    internal static IReadOnlyList<DraftPick> Picks(int round, int seed, int gold)
    {
        var rng = new GameRng(GameRng.Mix(seed, 1000 + round));
        var picks = new List<DraftPick>();
        for (int i = 0; i < 3 && gold > 0; i++)
        {
            int kind = rng.Next(Roster.Size);
            int n = Math.Max(1, (i < 2 ? gold / 2 : gold) / Army.ArmyRules.Default.DraftPrice(kind));
            if (n * Army.ArmyRules.Default.DraftPrice(kind) > gold) continue;
            picks.Add(new(kind, n)); gold -= n * Army.ArmyRules.Default.DraftPrice(kind);
        }
        return picks;
    }

    internal static DoctrineSet DoctrineFor(int round, int seed, LegalMask legal)
    {
        var rng = new GameRng(GameRng.Mix(seed, 2000 + round));
        var pool = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows).ToList();
        var tokens = new List<int>();
        for (int i = 0; i < 3 && pool.Count > 0; i++) tokens.Add(pool[rng.Next(pool.Count)]);
        return DoctrineSet.Of(tokens.ToArray());
    }

    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(Picks(r.Round, r.Seed, r.Gold));
    public ValueTask<int> FormationAsync(OpponentRound r, CancellationToken ct) => ValueTask.FromResult(0);
    public ValueTask<DoctrineSet> DoctrineAsync(OpponentRound r, LegalMask legal, CancellationToken ct) => ValueTask.FromResult(DoctrineFor(r.Round, r.Seed, legal));
    public IReadOnlyList<DraftPick>? Preview(OpponentRound r) => Picks(r.Round, r.Seed, r.Gold);
}
