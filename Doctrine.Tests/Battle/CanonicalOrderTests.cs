namespace Doctrine.Tests;

/// <summary>
/// Fifth round, coordinator's finding (showroom-owner): the same draft in a different PICK ORDER gave the same army digest and a different battle, because cohorts were
/// enumerated in add order (a kind re-added after a wipe-out stood in its line by add order; two cohorts of one line swapped places in frame 0). The army now reads its
/// cohorts by kind and the battle takes them by kind, then rank, whatever order it is handed.
/// </summary>
public class CanonicalOrderTests
{
    static CohortRead C(int key, int count, int slot) => Sample.Cohort(key, count, slot);

    /// <summary>The same cohorts handed to the battle in two orders (two of one line) give the same recording.</summary>
    public static async Task<string?> TheBattleDoesNotDependOnTheOrderCohortsAreListed(TracerBattleEngine e)
    {
        CohortRead[] a = [C(0, 40, 0), C(2, 30, 0), C(3, 25, 1), C(6, 20, 2)];
        var b = a.Reverse().ToArray();
        FieldOrder[] ord = [new(0, FieldOrderKind.Advance, 1), new(1, FieldOrderKind.Hold, 1), new(2, FieldOrderKind.Flank, 1)];
        foreach (var arena in new[] { "open-field", "ridge" })
        {
            var x = await Sample.RunAsync(e, new BattleSetup(arena, 7, 80, new BattleSide(Side.Player, a, ord), new BattleSide(Side.Enemy, a, ord)));
            var y = await Sample.RunAsync(e, new BattleSetup(arena, 7, 80, new BattleSide(Side.Player, b, ord), new BattleSide(Side.Enemy, a.Skip(1).Concat(a.Take(1)).ToArray(), ord)));
            if (ReplayCodec.Digest(x.Replay!) != ReplayCodec.Digest(y.Replay!)) return $"{arena}: the same cohorts listed in another order fought another battle";
            if (!ReplayCodec.Encode(x.Replay!).AsSpan().SequenceEqual(ReplayCodec.Encode(y.Replay!))) return $"{arena}: the recordings differ byte for byte";
        }
        return null;
    }

    [Fact] public async Task The_battle_does_not_depend_on_the_order_cohorts_are_listed() => Assert.Null(await TheBattleDoesNotDependOnTheOrderCohortsAreListed(TracerWorlds.Engine()));
    [Fact] public async Task A_battle_that_takes_cohorts_in_the_order_given_is_caught() => Assert.NotNull(await TheBattleDoesNotDependOnTheOrderCohortsAreListed(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.CohortsInGivenOrder })));

    [Fact]
    public async Task One_army_reached_by_different_pick_orders_reads_the_same_and_fights_the_same_battle()
    {
        var svc = new PleromaArmyService();
        async Task<IArmy> Reach(int[] order)
        {
            var army = svc.Create(Side.Player, 11);
            await army.ApplySpoilsAsync(new Spoils(300, 0), default);
            // wipe out three kinds, then buy them back in the given order
            var read = army.Read();
            await army.ApplyLossesAsync(read.Cohorts.Where(c => c.Key is 0 or 2 or 3).Select(c => new CohortLoss(c.Key, c.Slot, c.Count)).ToList(), default);
            await army.DraftAsync(new DraftRequest(0, 1, order.Select(k => new DraftPick(k, 5 + k)).ToList()), default);
            return army;
        }
        var a = await Reach([0, 2, 3]); var b = await Reach([3, 0, 2]); var c = await Reach([2, 3, 0]);
        Assert.Equal(a.Read().Digest(), b.Read().Digest());
        Assert.Equal(a.Read().Cohorts, b.Read().Cohorts);               // the same list in the same order
        Assert.Equal(a.Read().Cohorts, c.Read().Cohorts);
        Assert.Equal(a.Read().Cohorts.Select(x => x.Key).OrderBy(k => k), a.Read().Cohorts.Select(x => x.Key));
        var e = TracerWorlds.Engine();
        var enemy = svc.Create(Side.Enemy, 11);
        ulong Digest(IArmy army) => ReplayCodec.Digest(Sample.RunAsync(e, new BattleSetup("ridge", 5, 80,
            new BattleSide(Side.Player, army.Read().Cohorts, [new(0, FieldOrderKind.Advance, 1), new(1, FieldOrderKind.Advance, 1), new(2, FieldOrderKind.Advance, 1)]),
            new BattleSide(Side.Enemy, enemy.Read().Cohorts, [new(0, FieldOrderKind.Advance, 1), new(1, FieldOrderKind.Advance, 1), new(2, FieldOrderKind.Advance, 1)]))).GetAwaiter().GetResult().Replay!);
        Assert.Equal(Digest(a), Digest(b)); Assert.Equal(Digest(a), Digest(c));
    }
}
