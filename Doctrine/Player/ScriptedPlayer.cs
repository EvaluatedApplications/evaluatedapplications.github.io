namespace Doctrine.Player;

// Seam (f): the player's hands. This scripted player drives headless runs and tests. The page (showroom-owner) implements IPlayerInput
// with real taps; nothing else changes.

/// <summary>A seeded, deterministic player: drafts with its gold, and either accepts the ghost or sets a doctrine by hand.</summary>
public sealed class ScriptedPlayer(int seed, int acceptGhostPercent = 40) : IPlayerInput
{
    readonly GameRng _rng = new(GameRng.Mix(seed, 7));

    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
    {
        var picks = new List<DraftPick>();
        int gold = army.Gold;
        for (int i = 0; i < 2 && gold > 0; i++)
        {
            int kind = _rng.Next(Roster.Size);
            int n = Math.Max(1, (i == 0 ? gold / 2 : gold) / Roster.PriceOf(kind));
            if (n * Roster.PriceOf(kind) > gold) continue;
            picks.Add(new(kind, n)); gold -= n * Roster.PriceOf(kind);
        }
        return ValueTask.FromResult<IReadOnlyList<DraftPick>>(picks);
    }

    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
    {
        if (!ghost.Doctrine.IsEmpty && _rng.Next(100) < acceptGhostPercent)
            return ValueTask.FromResult(new PlayerChoice(ghost.Doctrine, false));
        var legal = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(army.Legal.Allows).ToList();
        var tokens = new List<int>();
        for (int i = 0; i < 3 && legal.Count > 0; i++) tokens.Add(legal[_rng.Next(legal.Count)]);
        var d = DoctrineSet.Of(tokens.ToArray());
        return ValueTask.FromResult(new PlayerChoice(d, d != ghost.Doctrine));
    }
}
