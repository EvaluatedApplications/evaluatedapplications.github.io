using Doctrine.Army;
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
            int n = Math.Max(1, (i == 0 ? gold / 2 : gold) / ArmyRules.Default.DraftPrice(kind));
            if (n * ArmyRules.Default.DraftPrice(kind) > gold) continue;
            picks.Add(new(kind, n)); gold -= n * ArmyRules.Default.DraftPrice(kind);
        }
        return ValueTask.FromResult<IReadOnlyList<DraftPick>>(picks);
    }

    /// <summary>With an active offer: up to two packs the remaining gold can pay for, at random. With the open market: the old free draft (same stream, same picks).</summary>
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct)
    {
        if (!offer.Active) return DraftAsync(army, round, ct);
        var picks = new List<DraftPick>();
        var left = offer.Packs.ToList();
        int gold = army.Gold;
        for (int i = 0; i < 2; i++)
        {
            var can = left.Where(p => p.Cost <= gold).ToList();
            if (can.Count == 0) break;
            var pack = can[_rng.Next(can.Count)];
            picks.Add(UnitOffer.Pick(pack)); gold -= pack.Cost; left.Remove(pack);
        }
        return ValueTask.FromResult<IReadOnlyList<DraftPick>>(picks);
    }

    /// <summary>With an active offer: accept the ghost, else up to three distinct offered tokens at random. With the open market: the old free choice.</summary>
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
    {
        if (!offer.Active) return ChooseAsync(army, ghost, round, ct);
        if (!ghost.Doctrine.IsEmpty && _rng.Next(100) < acceptGhostPercent)
            return ValueTask.FromResult(new PlayerChoice(ghost.Doctrine, false));
        var pool = offer.Tokens.Where(army.Legal.Allows).ToList();
        var tokens = new List<int>();
        for (int i = 0; i < 3 && pool.Count > 0; i++) { int t = pool[_rng.Next(pool.Count)]; tokens.Add(t); pool.Remove(t); }
        var d = DoctrineSet.Of(tokens.ToArray());
        return ValueTask.FromResult(new PlayerChoice(d, d != ghost.Doctrine));
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
