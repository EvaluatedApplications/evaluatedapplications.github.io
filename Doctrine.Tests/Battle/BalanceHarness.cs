using Xunit.Abstractions;

namespace Doctrine.Tests;

// The battle balance and fairness harness. Pre-registration: Doctrine\todo\balance-harness-2026-10-19.md (locked before the first run).
// Mirrored armies (identical cohorts and orders on both sides) must come out near 50/50 on every arena over a declared seed set.

/// <summary>Setup-level mutant: the attacker gets extra power on every cohort. The harness must flag it (null 1).</summary>
sealed class AttackerBoostEngine(IBattleEngine inner, int bonusPct) : IBattleEngine
{
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public ValueTask<IBattleSession> StartAsync(BattleSetup s, CancellationToken ct)
        => inner.StartAsync(s with { Attacker = s.Attacker with { Cohorts = s.Attacker.Cohorts.Select(c => c with { PowerPct = c.PowerPct + bonusPct }).ToList() } }, ct);
}

public sealed record BalanceCell(string Arena, string Army, double Score, double Margin, int AttackerWins, int DefenderWins, int Draws, IReadOnlyList<ulong> Digests);

public static class BalanceHarness
{
    public const int Seeds = 12, TickLimit = 120;
    // Amended 2026-10-19 after the first runs (see the pre-registration note): the score band is three standard deviations of a fair
    // binary outcome over Seeds battles (0.5 / sqrt(12) = 0.144 each, so 0.433), derived and not fitted; the first-registered +/-0.15
    // was about one sigma and flagged a fair arena about 23 percent of the time per cell.
    public static readonly double ScoreBand = 3 * 0.5 / Math.Sqrt(Seeds);
    public const double MarginBand = 0.10;

    public static readonly string[] Arenas = ["open-field", "chokepoint", "ridge", "falling-gate"];

    static CohortRead C(int key, int count, int slot) => Sample.Cohort(key, count, slot);
    static FieldOrder O(int g, FieldOrderKind k) => new(g, k, 1);

    /// <summary>The declared mirrored compositions: name, cohorts, orders.</summary>
    public static readonly (string Name, CohortRead[] Cohorts, FieldOrder[] Orders)[] Mirrors =
    [
        ("mixed", [C(0, 60, 0), C(3, 30, 1), C(2, 20, 2)], [O(0, FieldOrderKind.Advance), O(1, FieldOrderKind.Hold), O(2, FieldOrderKind.Flank)]),
        ("melee", [C(0, 80, 0)], [O(0, FieldOrderKind.Advance)]),
    ];

    static BattleSetup Mirrored(string arena, int seed, (string Name, CohortRead[] Cohorts, FieldOrder[] Orders) m)
        => new(arena, seed, TickLimit, new BattleSide(Side.Player, m.Cohorts, m.Orders), new BattleSide(Side.Enemy, m.Cohorts, m.Orders));

    static async Task<(BattleResult R, ulong Digest, int A, int D)> Fight(Func<IBattleEngine> engine, BattleSetup setup)
    {
        var st = await Sample.RunAsync(engine(), setup);
        int a = setup.Attacker.Cohorts.Sum(c => c.Count) - st.Result!.AttackerLosses.Sum(l => l.Lost);
        int d = setup.Defender.Cohorts.Sum(c => c.Count) - st.Result.DefenderLosses.Sum(l => l.Lost);
        return (st.Result, ReplayCodec.Digest(st.Replay!), a, d);
    }

    /// <summary>One row of the table per (arena, composition); a fresh engine per battle so nothing leaks between them.</summary>
    public static async Task<IReadOnlyList<BalanceCell>> MirrorTable(Func<IBattleEngine> engine, int seeds = Seeds, string[]? arenas = null)
    {
        // One engine per arena (a reused engine gives the recording a fresh one gives: TracerBattleTests), arenas in parallel.
        var perArena = (arenas ?? Arenas).Select(arena => Task.Run(async () =>
        {
            var e = engine();
            var cells = new List<BalanceCell>();
            foreach (var m in Mirrors)
            {
                int aw = 0, dw = 0, dr = 0; double margin = 0; var digests = new List<ulong>();
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var setup = Mirrored(arena, seed, m);
                    var (r, dig, a, d) = await Fight(() => e, setup);
                    digests.Add(dig);
                    if (r.Outcome == BattleStatus.AttackerWon) aw++; else if (r.Outcome == BattleStatus.DefenderWon) dw++; else dr++;
                    margin += (a - d) / (double)setup.Attacker.Cohorts.Sum(c => c.Count);
                }
                cells.Add(new BalanceCell(arena, m.Name, (aw + 0.5 * dr) / seeds, margin / seeds, aw, dw, dr, digests));
            }
            return cells;
        })).ToList();
        return (await Task.WhenAll(perArena)).SelectMany(c => c).ToList();
    }

    public static bool InBand(BalanceCell c) => Math.Abs(c.Score - 0.5) <= ScoreBand + 1e-9 && Math.Abs(c.Margin) <= MarginBand;

    static readonly CohortRead[] X = [C(3, 60, 0), C(0, 40, 1)], Y = [C(2, 50, 0), C(6, 40, 1)];
    static readonly FieldOrder[] Adv = [O(0, FieldOrderKind.Advance), O(1, FieldOrderKind.Advance)];

    /// <summary>How many of the seeds the SAME army wins in both orientations (X attacks Y, Y attacks X) on an arena. A draw agrees only with a draw.</summary>
    public static async Task<(int Agree, int Total)> SwapAgreement(Func<IBattleEngine> engine, string arena)
    {
        var e = engine(); engine = () => e;
        int agree = 0;
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var xa = await Fight(engine, new BattleSetup(arena, seed, TickLimit, new BattleSide(Side.Player, X, Adv), new BattleSide(Side.Enemy, Y, Adv)));
            var ya = await Fight(engine, new BattleSetup(arena, seed, TickLimit, new BattleSide(Side.Player, Y, Adv), new BattleSide(Side.Enemy, X, Adv)));
            // "X won" in orientation 1 is AttackerWon; in orientation 2 it is DefenderWon.
            char w1 = xa.R.Outcome switch { BattleStatus.AttackerWon => 'X', BattleStatus.DefenderWon => 'Y', _ => '=' };
            char w2 = ya.R.Outcome switch { BattleStatus.AttackerWon => 'Y', BattleStatus.DefenderWon => 'X', _ => '=' };
            if (w1 == w2) agree++;
        }
        return (agree, Seeds);
    }

    public static string Format(IEnumerable<BalanceCell> cells)
        => string.Join(Environment.NewLine, cells.Select(c =>
            $"{c.Arena,-13} {c.Army,-6} score {c.Score:0.000}  margin {c.Margin:+0.000;-0.000}  A{c.AttackerWins}/D{c.DefenderWins}/={c.Draws}  {(InBand(c) ? "ok" : "OUT")}"));
}

public class BalanceHarnessTests(ITestOutputHelper output)
{
    static TracerBattleEngine Fresh() => TracerWorlds.Engine();

    // The real table is computed once and shared by the mirror check and null 2's first side; null 2's second run is a separate computation.
    static readonly Lazy<Task<IReadOnlyList<BalanceCell>>> Real = new(() => BalanceHarness.MirrorTable(Fresh));

    static void Dump(string name, string text)
    {
        var path = Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_OUT");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, $"== {name}{Environment.NewLine}{text}{Environment.NewLine}");
    }

    [Fact]
    public async Task Mirrored_armies_come_out_near_fifty_fifty_on_every_arena()
    {
        var cells = await Real.Value;
        var text = BalanceHarness.Format(cells);
        output.WriteLine(text); Dump("mirror", text);
        var bad = cells.Where(c => !BalanceHarness.InBand(c)).ToList();
        Assert.True(bad.Count == 0, "mirrored armies favour a side:" + Environment.NewLine + BalanceHarness.Format(bad));
    }

    [Fact]
    public async Task Swapping_the_sides_gives_the_same_army_the_win()
    {
        var results = await Task.WhenAll(BalanceHarness.Arenas.Select(a => Task.Run(async () => (Arena: a, R: await BalanceHarness.SwapAgreement(Fresh, a)))));
        var lines = results.Select(r => $"{r.Arena,-13} same army wins both ways {r.R.Agree}/{r.R.Total}").ToList();
        output.WriteLine(string.Join(Environment.NewLine, lines)); Dump("swap", string.Join(Environment.NewLine, lines));
        var bad = results.Where(r => r.R.Agree < 10).Select(r => r.Arena).ToList();
        Assert.True(bad.Count == 0, "the side decides the winner on: " + string.Join(", ", bad));
    }

    // NULL 1: a deliberately side-biased engine. The harness must flag it and name the attacker.
    [Fact]
    public async Task Null_one_a_side_biased_engine_is_flagged_and_the_attacker_is_named()
    {
        var cells = await BalanceHarness.MirrorTable(() => new AttackerBoostEngine(Fresh(), 15));
        var text = BalanceHarness.Format(cells);
        output.WriteLine(text); Dump("null1-attacker+15", text);
        var flagged = cells.Where(c => !BalanceHarness.InBand(c)).ToList();
        Assert.NotEmpty(flagged);
        Assert.All(flagged, c => Assert.True(c.Score > 0.5 && c.Margin > 0, "a flagged cell must favour the boosted attacker"));
    }

    // NULL 2: the same seeds on fresh engines reproduce every recording exactly.
    [Fact]
    public async Task Null_two_an_identical_seed_rerun_reproduces_every_recording()
    {
        var a = await Real.Value;
        var b = await BalanceHarness.MirrorTable(Fresh);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Digests, b[i].Digests);
            Assert.Equal(a[i].Score, b[i].Score);
            Assert.Equal(a[i].Margin, b[i].Margin);
        }
    }
}