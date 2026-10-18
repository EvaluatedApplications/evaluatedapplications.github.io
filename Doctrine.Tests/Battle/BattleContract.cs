using EvalApp;

namespace Doctrine.Tests;

public static class Sample
{
    public static CohortRead Cohort(int key, int count, int slot, Trait? traits = null, int power = 100)
        => new(key, count, traits ?? Roster.TraitsOf(key), slot, 0, power);

    public static BattleSetup Setup(string arena, int seed, int tickLimit, int attackers = 24, int defenders = 24, bool orders = true)
        => new(arena, seed, tickLimit,
            new BattleSide(Side.Player, [Cohort(0, attackers, 0), Cohort(3, attackers / 2, 1), Cohort(2, attackers / 3 + 1, 2)],
                orders ? [new FieldOrder(0, FieldOrderKind.Advance, 1), new FieldOrder(1, FieldOrderKind.Hold, 1), new FieldOrder(2, FieldOrderKind.Flank, 1)] : []),
            new BattleSide(Side.Enemy, [Cohort(1, defenders, 0), Cohort(4, defenders / 2, 1), Cohort(6, defenders / 3 + 1, 2)],
                orders ? [new FieldOrder(0, FieldOrderKind.Advance, 1), new FieldOrder(1, FieldOrderKind.Hunt, 1)] : []));

    /// <summary>Run a battle through the game's own battle pipeline, with <paramref name="engine"/> as the battle section.</summary>
    public static async Task<BattleState> RunAsync(IBattleEngine engine, BattleSetup setup)
    {
        var game = DoctrineBuilder.New().WithBattle(engine).Build();
        var r = await game.Battle.RunAsync(new BattleState(setup));
        if (r is PipelineResult<BattleState>.Failure f) throw f.Exception;
        return r.GetData();
    }

    public static int Standing(ReplayFrame f, Side s) => f.Blobs.Where(b => b.Side == s).Sum(b => b.Strength);
}

/// <summary>The BATTLE contract (CONTRACT.md section b), checked through the game's own battle pipeline.</summary>
public static class BattleContract
{
    public static IReadOnlyList<ContractCase<IBattleEngine>> Cases { get; } =
    [
        new("arena-catalogue-is-usable", e =>
        {
            Expect.True(e.Arenas.Count >= 1, "at least one arena");
            Expect.Equal(e.Arenas.Count, e.Arenas.Select(a => a.Id).Distinct().Count(), "arena ids are unique");
            Expect.True(e.Arenas.All(a => a.Id.Length > 0 && a.Name.Length > 0 && a.Width > 0 && a.Height > 0), "every arena has an id, a name and a size");
            return Task.CompletedTask;
        }),
        new("every-arena-runs-a-battle", async e =>
        {
            foreach (var a in e.Arenas)
            {
                var b = await Sample.RunAsync(e, Sample.Setup(a.Id, 3, 60));
                Expect.True(b.Result is not null && b.Replay is not null, $"arena {a.Id} produced no result or replay");
            }
        }),
        new("battle-ends-with-a-decided-result-inside-the-limit", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 1, 40));
            Expect.True(b.Result!.Outcome != BattleStatus.Running, "a finished battle is never Running");
            Expect.True(b.Result.Ticks <= 40, $"ran {b.Result.Ticks} ticks past the limit of 40");
            Expect.Equal(b.Session!.Tick, b.Result.Ticks, "result ticks equal the session's completed ticks");
        }),
        new("tick-limit-decides-by-soldiers-left", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 1, 1, 60, 60));
            Expect.Equal(1, b.Result!.Ticks, "tick limit 1 means exactly one tick");
            var last = b.Replay!.Frames[^1];
            int a = Sample.Standing(last, Side.Player), d = Sample.Standing(last, Side.Enemy);
            var expect = a > d ? BattleStatus.AttackerWon : d > a ? BattleStatus.DefenderWon : BattleStatus.Draw;
            if (a > 0 && d > 0) Expect.Equal(expect, b.Result.Outcome, "when the limit stops a battle the side with more soldiers wins (equal is a Draw)");
        }),
        new("frames-are-consecutive-and-complete", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 2, 50));
            var fr = b.Replay!.Frames;
            Expect.Equal(b.Result!.Ticks + 1, fr.Count, "one frame per completed tick plus the start frame");
            for (int i = 0; i < fr.Count; i++) Expect.Equal(i, fr[i].Tick, $"frame {i} tick");
            Expect.Equal(0, fr[0].Events.Count, "the start frame has no events");
        }),
        new("blobs-are-stable-and-only-lose-soldiers", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 4, 60));
            var fr = b.Replay!.Frames;
            var ids = fr[0].Blobs.Select(x => x.Id).ToArray();
            Expect.Equal(ids.Length, ids.Distinct().Count(), "blob ids are unique");
            foreach (var f in fr)
            {
                Expect.True(f.Blobs.Select(x => x.Id).SequenceEqual(ids), $"frame {f.Tick} changed the set of blobs (a dead cohort stays with strength 0)");
                Expect.True(f.Blobs.All(x => x.Strength >= 0), $"frame {f.Tick} has a negative strength");
            }
            for (int i = 1; i < fr.Count; i++)
                for (int j = 0; j < ids.Length; j++)
                    Expect.True(fr[i].Blobs[j].Strength <= fr[i - 1].Blobs[j].Strength, $"blob {ids[j]} gained soldiers at tick {i}");
        }),
        new("blobs-match-the-setup", async e =>
        {
            var setup = Sample.Setup(e.Arenas[0].Id, 5, 20, 30, 18);
            var b = await Sample.RunAsync(e, setup);
            var f0 = b.Replay!.Frames[0];
            foreach (var side in new[] { setup.Attacker, setup.Defender })
            {
                var blobs = f0.Blobs.Where(x => x.Side == side.Side).OrderBy(x => x.Key).Select(x => (x.Key, x.Slot, x.Strength));
                var want = side.Cohorts.Where(c => c.Count > 0).OrderBy(c => c.Key).Select(c => (c.Key, c.Slot, c.Count));
                Expect.True(blobs.SequenceEqual(want), $"{side.Side}: start frame blobs must be the setup's cohorts (kind, slot, soldiers)");
            }
        }),
        new("losses-equal-what-the-replay-shows", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 6, 80));
            var fr = b.Replay!.Frames;
            foreach (var (side, losses) in new[] { (Side.Player, b.Result!.AttackerLosses), (Side.Enemy, b.Result.DefenderLosses) })
            {
                int start = Sample.Standing(fr[0], side), end = Sample.Standing(fr[^1], side);
                Expect.Equal(start - end, losses.Sum(l => l.Lost), $"{side}: losses in the result must equal soldiers gone in the replay");
                Expect.True(losses.All(l => l.Lost > 0), "only real losses are listed");
                foreach (var l in losses)
                {
                    var blob = fr[0].Blobs.First(x => x.Side == side && x.Key == l.Key && x.Slot == l.Slot);
                    Expect.True(l.Lost <= blob.Strength, "a cohort cannot lose more than it brought");
                }
            }
        }),
        new("events-are-between-enemies-and-deal-damage", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 7, 80));
            var side = b.Replay!.Frames[0].Blobs.ToDictionary(x => x.Id, x => x.Side);
            Expect.True(b.Replay.Frames.Any(f => f.Events.Count > 0), "two armies that fight produce at least one engagement");
            foreach (var f in b.Replay.Frames)
                foreach (var ev in f.Events)
                {
                    Expect.True(side.ContainsKey(ev.Source) && side.ContainsKey(ev.Target), "event names unknown blobs");
                    Expect.True(side[ev.Source] != side[ev.Target], "an engagement is between opposing sides");
                    Expect.True(ev.Damage > 0, "an event deals damage");
                }
        }),
        new("same-setup-same-recording", async e =>
        {
            var setup = Sample.Setup(e.Arenas[0].Id, 8, 60);
            var a = await Sample.RunAsync(e, setup); var b = await Sample.RunAsync(e, setup);
            Expect.Equal(ReplayCodec.Digest(a.Replay!), ReplayCodec.Digest(b.Replay!), "the same setup must give the same recording on this device");
        }),
        new("overwhelming-force-wins", async e =>
        {
            var b = await Sample.RunAsync(e, Sample.Setup(e.Arenas[0].Id, 9, 120, attackers: 300, defenders: 6));
            Expect.Equal(BattleStatus.AttackerWon, b.Result!.Outcome, "50 to 1 should win");
        }),
        new("an-empty-side-is-already-decided", async e =>
        {
            var setup = Sample.Setup(e.Arenas[0].Id, 10, 30) with { Defender = new BattleSide(Side.Enemy, [], []) };
            var b = await Sample.RunAsync(e, setup);
            Expect.Equal(BattleStatus.AttackerWon, b.Result!.Outcome, "a side with no soldiers has lost");
            Expect.Equal(0, b.Result.DefenderLosses.Count, "an empty side loses nothing");
        }),
    ];
}
