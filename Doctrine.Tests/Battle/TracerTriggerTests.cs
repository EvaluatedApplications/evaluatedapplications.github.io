namespace Doctrine.Tests;

/// <summary>
/// The loss trigger (CONTRACT.md b, vocabulary version 2): a doctrine split at its first trigger token, the opening orders before it, the "then" orders after
/// it, taking over for good when the side has lost N quarters of the soldiers it brought. Each behaviour is a function of the engine under test so the
/// mutation proofs can run the SAME check on an engine whose trigger has been broken (<see cref="Battle.OrderProbe"/>) and watch it go red.
/// </summary>
public class TracerTriggerTests
{
    static CohortRead C(int key, int count, int slot) => Sample.Cohort(key, count, slot);
    static FieldOrder O(int group, FieldOrderKind kind) => new(group, kind, 1);

    /// <summary>40 Spearmen hold the front line and fall back once <paramref name="quarters"/> quarters are lost; 120 Spearmen advance on them.</summary>
    static BattleSetup Siege(int quarters, int seed = 3, FieldOrderKind then = FieldOrderKind.FallBack)
        => new("open-field", seed, 90,
            new BattleSide(Side.Player, [C(0, 40, 0)], [O(0, FieldOrderKind.Hold)], new LossTrigger(quarters, [O(0, then)])),
            new BattleSide(Side.Enemy, [C(0, 120, 0)], [O(0, FieldOrderKind.Advance)]));

    static async Task<(TracerBattleSession S, List<FieldOrderKind> Orders, List<int> Standing)> Play(TracerBattleEngine e, BattleSetup setup, int ticks)
    {
        var s = (TracerBattleSession)await e.StartAsync(setup, default);
        var orders = new List<FieldOrderKind>(); var standing = new List<int> { setup.Attacker.Cohorts.Sum(c => c.Count) };
        for (int t = 0; t < ticks && s.Status == BattleStatus.Running; t++)
        {
            await s.FieldOrdersAsync(default);
            orders.Add(s.Agents().First(a => a.Blob == 0 && a.Alive).Order);          // the attacker's order as it stands for this tick
            await s.MoveAsync(default); await s.EngageAsync(default); await s.ApplyLossesAsync(default);
            var f = s.Record();
            standing.Add(f.Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength));
        }
        return (s, orders, standing);
    }

    /// <summary>The trigger fires on the side's own losses: the first tick that starts with a quarter lost, not before, and only that side's cohorts change.</summary>
    public static async Task<string?> FiresOnItsOwnLosses(TracerBattleEngine e)
    {
        var (s, orders, standing) = await Play(e, Siege(1), 90);
        int at = s.Stats.TriggerFiredAtTick[0];
        if (at < 0) return "a side that lost a quarter never fired its trigger";
        if (at == 0) return "the trigger fired at the first tick, before anything was lost";
        if (standing[at] > 30) return $"fired with {40 - standing[at]} soldiers lost of 40 (a quarter is 10): too soon";
        if (standing[at - 1] <= 30) return $"a quarter was already lost at the start of tick {at - 1} but the trigger waited";
        if (s.Stats.TriggerFiredAtTick[1] != -1) return "the side without a trigger reports one";
        for (int t = 0; t < orders.Count; t++)
        {
            var want = t < at ? FieldOrderKind.Hold : FieldOrderKind.FallBack;
            if (orders[t] != want) return $"tick {t}: the cohort's order is {orders[t]}, expected {want} (trigger at {at})";
        }
        return null;
    }

    /// <summary>A later level waits for more losses: three quarters fires no earlier than one quarter, and fires at all in a rout.</summary>
    public static async Task<string?> LevelsWaitForMoreLosses(TracerBattleEngine e)
    {
        var at = new int[4];
        for (int q = 1; q <= 3; q++)
        {
            // Then = Advance changes nothing for a side that already advances, so all three battles are the same battle and only the check differs.
            var setup = new BattleSetup("open-field", 5, 100,
                new BattleSide(Side.Player, [C(0, 40, 0)], [O(0, FieldOrderKind.Advance)], new LossTrigger(q, [O(0, FieldOrderKind.Advance)])),
                new BattleSide(Side.Enemy, [C(0, 90, 0)], [O(0, FieldOrderKind.Advance)]));
            var (s, _, _) = await Play(e, setup, 100);
            at[q] = s.Stats.TriggerFiredAtTick[0];
        }
        if (at[1] < 0 || at[2] < 0 || at[3] < 0) return $"a rout (40 against 90) did not fire every level: {at[1]}, {at[2]}, {at[3]}";
        if (!(at[1] <= at[2] && at[2] <= at[3] && at[1] < at[3])) return $"the levels did not fire in order of the losses they wait for: quarters 1, 2, 3 at ticks {at[1]}, {at[2]}, {at[3]}";
        return null;
    }

    /// <summary>With nothing lost the trigger never fires and the opening orders hold all battle.</summary>
    public static async Task<string?> StaysQuietWithoutLosses(TracerBattleEngine e)
    {
        var setup = new BattleSetup("open-field", 3, 30,
            new BattleSide(Side.Player, [C(0, 50, 0)], [O(0, FieldOrderKind.Hold)], new LossTrigger(1, [O(0, FieldOrderKind.Advance)])),
            new BattleSide(Side.Enemy, [C(0, 40, 0)], [O(0, FieldOrderKind.Hold)], new LossTrigger(1, [O(0, FieldOrderKind.Advance)])));
        var (s, orders, _) = await Play(e, setup, 30);
        if (s.Stats.TriggerFiredAtTick.Any(t => t != -1)) return $"a trigger fired in a battle where nobody fought: {string.Join(",", s.Stats.TriggerFiredAtTick)}";
        if (orders.Any(o => o != FieldOrderKind.Hold)) return "an order changed in a battle where nobody fought";
        return null;
    }

    [Fact] public async Task The_trigger_fires_on_the_sides_own_losses_and_swaps_the_orders() => Assert.Null(await FiresOnItsOwnLosses(TracerWorlds.Engine()));
    [Fact] public async Task A_later_level_waits_for_more_losses() => Assert.Null(await LevelsWaitForMoreLosses(TracerWorlds.Engine()));
    [Fact] public async Task With_nothing_lost_the_trigger_stays_quiet() => Assert.Null(await StaysQuietWithoutLosses(TracerWorlds.Engine()));

    [Fact]
    public async Task A_trigger_that_never_fires_changes_nothing_so_a_doctrine_without_one_plays_as_before()
    {
        // The contingency is the ONLY thing the trigger adds: with it disabled the recording is the one of a side that has none.
        var plain = Siege(1) with { Attacker = Siege(1).Attacker with { Trigger = null } };
        var withInert = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.TriggerNever }), Siege(1))).Replay!);
        var without = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(), plain)).Replay!);
        Assert.Equal(without, withInert);
        var (s, orders, _) = await Play(TracerWorlds.Engine(), plain, 90);
        Assert.All(orders, o => Assert.Equal(FieldOrderKind.Hold, o));
        Assert.Equal(-1, s.Stats.TriggerFiredAtTick[0]);
    }
    [Fact]
    public async Task Falling_back_after_the_trigger_changes_the_battle()
    {
        var e = TracerWorlds.Engine();
        var held = ReplayCodec.Digest((await Sample.RunAsync(e, Siege(1, then: FieldOrderKind.Hold))).Replay!);
        var fled = ReplayCodec.Digest((await Sample.RunAsync(TracerWorlds.Engine(), Siege(1))).Replay!);
        Assert.NotEqual(held, fled);
    }

    [Fact]
    public async Task A_group_not_named_after_the_trigger_advances()
    {
        // two groups hold; only group 0 is named after the trigger (Hold), so group 1 is released to advance when it fires
        var setup = new BattleSetup("open-field", 3, 90,
            new BattleSide(Side.Player, [C(0, 30, 0), C(3, 20, 1)], [O(0, FieldOrderKind.Hold), O(1, FieldOrderKind.Hold)], new LossTrigger(1, [O(0, FieldOrderKind.Hold)])),
            new BattleSide(Side.Enemy, [C(0, 120, 0)], [O(0, FieldOrderKind.Advance)]));
        var s = (TracerBattleSession)await TracerWorlds.Engine().StartAsync(setup, default);
        int fired = -1;
        for (int t = 0; t < 90 && s.Status == BattleStatus.Running && fired < 0; t++)
        {
            await s.FieldOrdersAsync(default); await s.MoveAsync(default); await s.EngageAsync(default); await s.ApplyLossesAsync(default); s.Record();
            fired = s.Stats.TriggerFiredAtTick[0];
        }
        Assert.True(fired > 0, "the trigger should fire in this siege");
        Assert.All(s.Agents().Where(a => a.Blob == 0 && a.Alive), a => Assert.Equal(FieldOrderKind.Hold, a.Order));          // named after the trigger: still holding
        Assert.All(s.Agents().Where(a => a.Blob == 1 && a.Alive), a => Assert.Equal(FieldOrderKind.Advance, a.Order));       // not named: released
    }

    // ---- the vocabulary side of the same rule ----

    [Fact]
    public void A_doctrine_is_read_as_two_lists_split_at_its_first_trigger_token()
    {
        int F(FieldOrderKind k, int g) => DoctrineVocabulary.Field(k, g);
        var d = DoctrineSet.Of(F(FieldOrderKind.Hold, 0), F(FieldOrderKind.Hold, 1), DoctrineVocabulary.WhenLost(2), F(FieldOrderKind.Advance, 1));
        Assert.Equal([new FieldOrder(0, FieldOrderKind.Hold, 1), new FieldOrder(1, FieldOrderKind.Hold, 1)], DoctrineVocabulary.FieldOrders(d));
        var t = DoctrineVocabulary.Trigger(d)!;
        Assert.Equal(2, t.Quarters);
        Assert.Equal([new FieldOrder(1, FieldOrderKind.Advance, 1)], t.Then);
        // no trigger: one list, the last word per group wins, exactly as before
        var plain = DoctrineSet.Of(F(FieldOrderKind.Hold, 0), F(FieldOrderKind.Flank, 0), F(FieldOrderKind.Guard, 2));
        Assert.Null(DoctrineVocabulary.Trigger(plain));
        Assert.Equal([new FieldOrder(0, FieldOrderKind.Flank, 1), new FieldOrder(2, FieldOrderKind.Guard, 1)], DoctrineVocabulary.FieldOrders(plain));
        // a trigger first: nothing opens, everything is "then"; a second trigger token is ignored
        var first = DoctrineSet.Of(DoctrineVocabulary.WhenLost(3), F(FieldOrderKind.FallBack, 0), DoctrineVocabulary.WhenLost(1));
        Assert.Empty(DoctrineVocabulary.FieldOrders(first));
        Assert.Equal(3, DoctrineVocabulary.Trigger(first)!.Quarters);
        Assert.Equal([new FieldOrder(0, FieldOrderKind.FallBack, 1)], DoctrineVocabulary.Trigger(first)!.Then);
        // a trigger last: the whole doctrine opens, nothing is "then" (every group is released)
        var last = DoctrineSet.Of(F(FieldOrderKind.Hold, 0), DoctrineVocabulary.WhenLost(1));
        Assert.Equal([new FieldOrder(0, FieldOrderKind.Hold, 1)], DoctrineVocabulary.FieldOrders(last));
        Assert.Empty(DoctrineVocabulary.Trigger(last)!.Then);
    }

    [Fact]
    public void The_vocabulary_grew_without_moving_a_token()
    {
        // ids are wire data: 0..53 are version 1 and stay where they were; 54..56 are the triggers
        Assert.Equal(57, DoctrineVocabulary.Size);
        Assert.True(DoctrineVocabulary.Size <= 64);
        Assert.Equal(1, DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1));
        Assert.Equal(3, DoctrineVocabulary.Army(ArmyOrderKind.Flip, 0));
        Assert.Equal(11, DoctrineVocabulary.Army(ArmyOrderKind.Age));
        Assert.Equal(12, DoctrineVocabulary.Army(ArmyOrderKind.Merge, 0));
        Assert.Equal(20, DoctrineVocabulary.Army(ArmyOrderKind.Aura, 0));
        Assert.Equal(28, DoctrineVocabulary.Army(ArmyOrderKind.Cull, 0));
        Assert.Equal(36, DoctrineVocabulary.Field(FieldOrderKind.Advance, 0));
        Assert.Equal(53, DoctrineVocabulary.Field(FieldOrderKind.Hunt, 2));
        Assert.Equal(54, DoctrineVocabulary.WhenLost(1));
        Assert.Equal(55, DoctrineVocabulary.WhenLost(2));
        Assert.Equal(56, DoctrineVocabulary.WhenLost(3));
        Assert.Equal("WhenLost50", DoctrineVocabulary.Name(55));
        Assert.Equal(OrderFamily.Trigger, DoctrineVocabulary.Decode(54).Family);
        Assert.Equal(DoctrineVocabulary.TriggerKind, DoctrineVocabulary.Decode(54).Kind);
    }

    [Theory]
    [InlineData("stub")]
    [InlineData("pleroma")]
    public async Task The_triggers_are_legal_exactly_when_a_line_has_a_cohort(string which)
    {
        IArmyService svc = which == "stub" ? new StubArmyService() : new PleromaArmyService();
        var a = svc.Create(Side.Player, 5);
        for (int q = 1; q <= 3; q++) Assert.True(a.Read().Legal.Allows(DoctrineVocabulary.WhenLost(q)), "a fresh army has lines");
        // the army ignores the token (it belongs to the battle, like a field order)
        var before = a.Read().Digest();
        var rep = await a.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.WhenLost(1)), default);
        Assert.Equal(new ArmyOrdersReport(0, 0), rep);
        Assert.Equal(before, a.Read().Digest());
        // no soldiers, no line, no contingency
        var all = a.Read().Cohorts.Select(c => new CohortLoss(c.Key, c.Slot, c.Count)).ToList();
        await a.ApplyLossesAsync(all, default);
        Assert.Equal(0, a.Read().Soldiers);
        for (int q = 1; q <= 3; q++) Assert.False(a.Read().Legal.Allows(DoctrineVocabulary.WhenLost(q)));
    }
}
