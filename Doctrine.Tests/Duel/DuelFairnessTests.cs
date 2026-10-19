using Xunit.Abstractions;

namespace Doctrine.Tests;

// The duel's fairness cells. Pre-registration: Doctrine\todo\duel-seam-2026-10-19.md, item 3 (locked before the first run). Seeds 1..40 and both parities of round, fixed.
// Cell 1: identical picks for both seats, seat A's pooled score within three sigma of one half. Cell 2: the picks swapped between the seats, d = A's score in the first
// assignment + A's score in the second - 1 has mean 0 when no seat is favoured. Null 1: seat A +15 percent power must be flagged. Null 2: a rerun reproduces every digest.

public class DuelFairnessTests(ITestOutputHelper output)
{
    public const int Seeds = 40;
    static readonly int[] Parities = [0, 1];

    public sealed record MirrorCell(int Battles, double AScore, int AWins, int BWins, int Draws, IReadOnlyList<ulong> Digests)
    {
        public double Band => 3 * 0.5 / Math.Sqrt(Battles);
        public bool InBand => Math.Abs(AScore - 0.5) <= Band + 1e-9;
    }

    public sealed record SwapCell(int Pairs, double MeanD, double Z, IReadOnlyList<ulong> Digests);

    static double Score(DuelRoundResult r) => r.Winner switch { Seat.A => 1, Seat.B => 0, _ => 0.5 };

    /// <summary>Runs <paramref name="jobs"/> on at most four workers, each worker with its own game (and so its own engine).</summary>
    static async Task<List<T>> Fan<T>(Func<DoctrineGame> newGame, IReadOnlyList<(int Seed, int Parity)> jobs, Func<DoctrineGame, int, int, Task<T>> run)
    {
        var results = new T[jobs.Count];
        var workers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            var game = newGame();
            for (int i = w; i < jobs.Count; i += 4) results[i] = await run(game, jobs[i].Seed, jobs[i].Parity);
        })).ToList();
        await Task.WhenAll(workers);
        return results.ToList();
    }

    static List<(int, int)> AllJobs() => Enumerable.Range(1, Seeds).SelectMany(s => Parities.Select(p => (s, p))).ToList();

    public static async Task<MirrorCell> Mirrored(Func<IBattleEngine, IBattleEngine>? wrap = null)
    {
        var rows = await Fan(() => DuelKit.Game("stub", "tracer", 2, wrap), AllJobs(), async (game, seed, parity) =>
        {
            var p = await new DuelKit.Session(game, seed, mirrored: true, DuelProbe.None, parity).NextAsync();
            Assert.Equal(p.PicksA, p.PicksB);                                           // the instrument: both seats really did pick the same
            Assert.Equal(p.Result.A.DigestAfterOrders, p.Result.B.DigestAfterOrders);
            return p.Result;
        });
        double score = rows.Average(Score);
        return new MirrorCell(rows.Count, score, rows.Count(r => r.Winner == Seat.A), rows.Count(r => r.Winner == Seat.B), rows.Count(r => r.Winner is null), rows.Select(r => r.Digest()).ToList());
    }

    public static async Task<SwapCell> Swapped()
    {
        var rows = await Fan(() => DuelKit.Game("stub", "tracer", 2), AllJobs(), async (game, seed, parity) =>
        {
            var x = await new DuelKit.Session(game, seed, mirrored: false, DuelProbe.None, parity).NextAsync();
            var y = await new DuelKit.Session(game, seed, mirrored: false, DuelProbe.None, parity, swapped: true).NextAsync();
            Assert.Equal(x.PicksA, y.PicksB); Assert.Equal(x.PicksB, y.PicksA);          // the instrument: the same picks, swapped between the seats
            return (D: Score(x.Result) + Score(y.Result) - 1, Differ: !x.PicksA.Equals(x.PicksB), Digests: new[] { x.Result.Digest(), y.Result.Digest() });
        });
        double mean = rows.Average(r => r.D), sd = Math.Sqrt(rows.Sum(r => (r.D - mean) * (r.D - mean)) / (rows.Count - 1));
        double z = sd < 1e-12 ? (Math.Abs(mean) < 1e-12 ? 0 : Math.Sign(mean) * double.PositiveInfinity) : mean / (sd / Math.Sqrt(rows.Count));
        Assert.True(rows.Count(r => r.Differ) >= rows.Count * 9 / 10, "the instrument: P and Q must be different picks in nearly every pair, or the cell swaps nothing");
        return new SwapCell(rows.Count, mean, z, rows.SelectMany(r => r.Digests).ToList());
    }

    static string Format(MirrorCell c) => $"mirrored picks, {c.Battles} battles (seeds 1..{Seeds}, rounds 0 and 1): seat A score {c.AScore:0.000} (band +/-{c.Band:0.000})  A{c.AWins}/B{c.BWins}/={c.Draws}  {(c.InBand ? "ok" : "OUT")}";
    static string Format(SwapCell c) => $"swapped picks, {c.Pairs} pairs: mean d {c.MeanD:+0.000;-0.000}  z {c.Z:+0.00;-0.00} (band +/-3)  {(Math.Abs(c.Z) <= 3 ? "ok" : "OUT")}";

    static void Dump(string name, string text)
    {
        var path = Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_OUT");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, $"== {name}{Environment.NewLine}{text}{Environment.NewLine}");
    }

    static readonly Lazy<Task<MirrorCell>> Real = new(() => Mirrored());

    [Fact]
    public async Task Cell_1_identical_picks_leave_neither_seat_favoured()
    {
        var c = await Real.Value;
        output.WriteLine(Format(c)); Dump("duel mirrored", Format(c));
        Assert.True(c.InBand, "mirrored picks favour a seat: " + Format(c));
    }

    [Fact]
    public async Task Cell_2_the_same_picks_swapped_between_the_seats_do_no_better_in_one_seat()
    {
        var c = await Swapped();
        output.WriteLine(Format(c)); Dump("duel swapped", Format(c));
        Assert.True(Math.Abs(c.Z) <= 3, "swapping the picks between the seats moves the result: " + Format(c));
    }

    // NULL 1: seat A carries +15 percent power in every cohort. The cell must be flagged and must name seat A.
    [Fact]
    public async Task Null_one_a_boosted_seat_is_flagged_and_named()
    {
        var c = await Mirrored(e => new AttackerBoostEngine(e, 15));
        output.WriteLine(Format(c)); Dump("duel null1 seat A +15", Format(c));
        Assert.False(c.InBand, "the instrument did not notice a +15 percent seat: " + Format(c));
        Assert.True(c.AScore > 0.5, "the flagged cell must favour the boosted seat A");
    }

    // NULL 2: a fresh engine on the same seeds reproduces every round digest.
    [Fact]
    public async Task Null_two_a_rerun_on_a_fresh_engine_reproduces_every_round_digest()
    {
        var a = await Real.Value; var b = await Mirrored();
        Assert.Equal(a.Digests, b.Digests);
        Assert.Equal(a.AScore, b.AScore);
    }
}
