using Xunit.Abstractions;

namespace Doctrine.Tests;

// The battle balance and fairness harness. Pre-registration: Doctrine\todo\balance-harness-2026-10-19.md (locked before the first run, with a
// follow-up pre-registration the same night). Mirrored armies (identical cohorts and orders on both sides) must come out near 50/50 on every arena
// over a declared seed set; a closer-matched pair of different armies must show no attacker/defender share beyond chance.

/// <summary>Setup-level mutant: the attacker gets extra power on every cohort. The harness must flag it (null 1).</summary>
sealed class AttackerBoostEngine(IBattleEngine inner, int bonusPct) : IBattleEngine
{
    public IReadOnlyList<ArenaInfo> Arenas => inner.Arenas;
    public ValueTask<IBattleSession> StartAsync(BattleSetup s, CancellationToken ct)
        => inner.StartAsync(s with { Attacker = s.Attacker with { Cohorts = s.Attacker.Cohorts.Select(c => c with { PowerPct = c.PowerPct + bonusPct }).ToList() } }, ct);
}

public sealed record BalanceCell(string Arena, string Army, double Score, double Margin, int AttackerWins, int DefenderWins, int Draws, IReadOnlyList<ulong> Digests, double MarginZ = 0)
{
    /// <summary>Sign test on the decisive battles: (A - D) / sqrt(A + D).</summary>
    public double Z => AttackerWins + DefenderWins == 0 ? 0 : (AttackerWins - DefenderWins) / Math.Sqrt(AttackerWins + DefenderWins);
}

/// <summary>Attacker share of a (different-armies) pair played both ways: X = the fixed army, Y = the scaled one.</summary>
public sealed record SwapRow(string Arena, int Battles, double AttackerScore, double XScore);

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

    /// <summary>
    /// One row of the table per (arena, composition); one engine per arena (a reused engine gives the recording a fresh one gives:
    /// TracerBattleTests), arenas in parallel. <paramref name="only"/> restricts the table to named (arena, composition) cells.
    /// </summary>
    public static async Task<IReadOnlyList<BalanceCell>> MirrorTable(Func<IBattleEngine> engine, int seeds = Seeds, string[]? arenas = null, int seedFrom = 1,
                                                                    IReadOnlyList<(string Arena, string Army)>? only = null)
    {
        var chosen = (arenas ?? Arenas).Where(a => only is null || only.Any(o => o.Arena == a));
        var perArena = chosen.Select(arena => Task.Run(async () =>
        {
            var e = engine();
            var cells = new List<BalanceCell>();
            foreach (var m in Mirrors)
            {
                if (only is not null && !only.Contains((arena, m.Name))) continue;
                int aw = 0, dw = 0, dr = 0; var digests = new List<ulong>(); var margins = new List<double>();
                for (int seed = seedFrom; seed < seedFrom + seeds; seed++)
                {
                    var setup = Mirrored(arena, seed, m);
                    var (r, dig, a, d) = await Fight(() => e, setup);
                    digests.Add(dig);
                    if (r.Outcome == BattleStatus.AttackerWon) aw++; else if (r.Outcome == BattleStatus.DefenderWon) dw++; else dr++;
                    margins.Add((a - d) / (double)setup.Attacker.Cohorts.Sum(c => c.Count));
                }
                double mean = margins.Average(), sd = Math.Sqrt(margins.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, margins.Count - 1));
                double mz = sd < 1e-12 ? 0 : mean / (sd / Math.Sqrt(margins.Count));
                cells.Add(new BalanceCell(arena, m.Name, (aw + 0.5 * dr) / seeds, mean, aw, dw, dr, digests, mz));
            }
            return cells;
        })).ToList();
        return (await Task.WhenAll(perArena)).SelectMany(c => c).ToList();
    }

    public static bool InBand(BalanceCell c) => Math.Abs(c.Score - 0.5) <= ScoreBand + 1e-9 && Math.Abs(c.Margin) <= MarginBand;

    // ---- the side-swap check -----------------------------------------------------------------------------------------------------
    // The first pair (Archers+Spearmen against Riders+Wolves) was not close: the same army won every time, so "same army wins both ways" could
    // not show a side effect. The replacement plays a MATCHED pair both ways and asks for the attacker's share: with the armies matched the army
    // difference cancels and the share measures the side alone. Matched-ness is asserted too, so the check cannot pass vacuously.

    /// <summary>Y's size relative to its base roster (Riders 50, Wolves 40). Chosen by the scan registered in the note; verified on the test seeds.</summary>
    public const double PairScale = 1.22;

    /// <summary>
    /// The second orientation gets its own jitter draws. The jitter stream is keyed by the cohort's place in the attackers-first order, so with one seed
    /// for both orientations the army that attacks would carry the same "lucky" draws both ways and the two battles of a seed would not be independent
    /// (the 3-sigma band assumes they are). Found when 12 seeds gave falling-gate 20/24 attacker wins and 100 seeds gave 0.525.
    /// </summary>
    const int SecondOrientationSeedOffset = 100_000;

    static readonly CohortRead[] X = [C(3, 60, 0), C(0, 40, 1)];
    public static CohortRead[] Y(double f) => [C(2, (int)Math.Round(50 * f), 0), C(6, (int)Math.Round(40 * f), 1)];
    static readonly FieldOrder[] Adv = [O(0, FieldOrderKind.Advance), O(1, FieldOrderKind.Advance)];

    /// <summary>Plays X attacks Y and Y attacks X for each seed on one arena.</summary>
    public static async Task<SwapRow> SwapShare(Func<IBattleEngine> engine, string arena, double f, int seedFrom = 1, int seeds = Seeds)
    {
        var e = engine(); var y = Y(f);
        double att = 0, xs = 0; int n = 0;
        for (int seed = seedFrom; seed < seedFrom + seeds; seed++)
        {
            var xa = await Fight(() => e, new BattleSetup(arena, seed, TickLimit, new BattleSide(Side.Player, X, Adv), new BattleSide(Side.Enemy, y, Adv)));
            var ya = await Fight(() => e, new BattleSetup(arena, seed + SecondOrientationSeedOffset, TickLimit, new BattleSide(Side.Player, y, Adv), new BattleSide(Side.Enemy, X, Adv)));
            att += Score(xa.R.Outcome, true) + Score(ya.R.Outcome, true);
            xs += Score(xa.R.Outcome, true) + Score(ya.R.Outcome, false);   // X attacks in the first, defends in the second
            n += 2;
        }
        return new SwapRow(arena, n, att / n, xs / n);
    }

    static double Score(BattleStatus s, bool forAttacker) => s == BattleStatus.Draw ? 0.5 : (s == BattleStatus.AttackerWon) == forAttacker ? 1 : 0;

    public static async Task<IReadOnlyList<SwapRow>> SwapTable(Func<IBattleEngine> engine, double f, int seedFrom = 1, int seeds = Seeds)
        => await Task.WhenAll(Arenas.Select(a => Task.Run(() => SwapShare(engine, a, f, seedFrom, seeds))));

    /// <summary>The verdict on a swap table: pooled and per-arena attacker share within three sigma of one half, and the pair matched (X share 0.25..0.75).</summary>
    public static (bool Ok, string Text) SwapVerdict(IReadOnlyList<SwapRow> rows)
    {
        int n = rows.Sum(r => r.Battles);
        double att = rows.Sum(r => r.AttackerScore * r.Battles) / n, xs = rows.Sum(r => r.XScore * r.Battles) / n;
        double poolBand = 1.5 / Math.Sqrt(n);
        var lines = rows.Select(r => $"{r.Arena,-13} attacker share {r.AttackerScore:0.000} (band +/-{1.5 / Math.Sqrt(r.Battles):0.000}) X share {r.XScore:0.000} over {r.Battles}").ToList();
        lines.Add($"pooled        attacker share {att:0.000} (band +/-{poolBand:0.000}) X share {xs:0.000} (matched needs 0.25..0.75) over {n}");
        bool ok = Math.Abs(att - 0.5) <= poolBand && xs is >= 0.25 and <= 0.75 && rows.All(r => Math.Abs(r.AttackerScore - 0.5) <= 1.5 / Math.Sqrt(r.Battles));
        return (ok, string.Join(Environment.NewLine, lines));
    }

    public static string Format(IEnumerable<BalanceCell> cells)
        => string.Join(Environment.NewLine, cells.Select(c =>
            $"{c.Arena,-13} {c.Army,-6} score {c.Score:0.000}  margin {c.Margin:+0.000;-0.000} (z {c.MarginZ:+0.0;-0.0})  A{c.AttackerWins}/D{c.DefenderWins}/={c.Draws} (z {c.Z:+0.0;-0.0})  {(InBand(c) ? "ok" : "OUT")}"));
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
    public async Task A_matched_pair_played_both_ways_shows_no_attacker_or_defender_share()
    {
        var rows = await BalanceHarness.SwapTable(Fresh, BalanceHarness.PairScale);
        var (ok, text) = BalanceHarness.SwapVerdict(rows);
        output.WriteLine(text); Dump("swap", text);
        Assert.True(ok, "the sides are not even on a matched pair (or the pair is not matched):" + Environment.NewLine + text);
    }

    // The swap check's own mutant: the attacker gets +15 percent power on every cohort. The check must go red, for the share and not for the match.
    [Fact]
    public async Task The_swap_check_flags_an_attacker_boosted_engine()
    {
        var rows = await BalanceHarness.SwapTable(() => new AttackerBoostEngine(Fresh(), 15), BalanceHarness.PairScale);
        var (ok, text) = BalanceHarness.SwapVerdict(rows);
        output.WriteLine(text); Dump("swap-null-attacker+15", text);
        Assert.False(ok, "the swap check did not notice a +15 percent attacker:" + Environment.NewLine + text);
        double att = rows.Sum(r => r.AttackerScore * r.Battles) / rows.Sum(r => r.Battles);
        Assert.True(att > 0.5, "the flagged share must favour the boosted attacker");
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

    // ---- the ground must be its own mirror image -------------------------------------------------------------------------------------
    // Found by the large-N sweep (note, 2026-10-19): with one diagonal in every cell the baked mesh was not symmetric, and mirrored armies leaned to
    // the attacker 43 to 8 on the ridge. The 12-seed table cannot see that lean (most battles end in draws), this check sees its cause in no time.

    static bool MeshIsItsOwnReflection(Arena a)
    {
        var (v, tris) = a.Geometry();
        string P(float x, float y) => $"{x:0.###},{y:0.###}";
        string Key(IEnumerable<string> corners) => string.Join("|", corners.OrderBy(c => c, StringComparer.Ordinal));
        var plain = tris.Select(t => Key(new[] { t.A, t.B, t.C }.Select(i => P(v[i].X, v[i].Y)))).ToHashSet();
        var mirrored = tris.Select(t => Key(new[] { t.A, t.B, t.C }.Select(i => P(a.Width - v[i].X, v[i].Y)))).ToHashSet();
        return plain.SetEquals(mirrored);
    }

    [Fact]
    public void Every_arena_mesh_is_its_own_mirror_image()
    {
        foreach (var a in ArenaCatalogue.Build()) Assert.True(MeshIsItsOwnReflection(a), $"{a.Info.Id}: the baked triangles are not symmetric about the arena's middle");
    }

    [Fact]
    public void The_mesh_check_flags_the_legacy_single_diagonal_mesh()
    {
        var legacy = ArenaCatalogue.Build(legacyMesh: true);
        Assert.All(legacy, a => Assert.False(MeshIsItsOwnReflection(a), $"{a.Info.Id}: the legacy mesh passed the symmetry check"));
    }

    [Fact]
    public async Task The_engine_fights_on_the_symmetric_mesh_unless_asked_for_the_legacy_one()
    {
        var m = BalanceHarness.Mirrors[0];
        var setup = new BattleSetup("ridge", 1001, BalanceHarness.TickLimit, new BattleSide(Side.Player, m.Cohorts, m.Orders), new BattleSide(Side.Enemy, m.Cohorts, m.Orders));
        var real = await Sample.RunAsync(TracerWorlds.Engine(), setup);
        var legacy = await Sample.RunAsync(TracerWorlds.Engine(new TracerBattleOptions { Probe = OrderProbe.LegacyMesh }), setup);
        Assert.NotEqual(ReplayCodec.Digest(real.Replay!), ReplayCodec.Digest(legacy.Replay!));
    }
    // ---- the large-N sweep (off in the default run) ----------------------------------------------------------------------------------
    // DOCTRINE_BALANCE_SWEEP = comma list of: base | null (seed blocks 1001..1200 / 1201..1400), optionally ":" + probe letters
    // (E engagement swap, S spawn reversed, N navigator reversed, I id parity flip, U legacy unmirrored mesh), e.g. "base,null,base:E,base:U";
    // "rerun" repeats base twice and compares digests; "scan" runs the matched-pair scan (f = 1.0..3.0 step 0.1 on seeds 5001..5008).
    // DOCTRINE_BALANCE_SEEDS overrides the 200. Results go to the test output and to DOCTRINE_BALANCE_OUT.

    static readonly (string Arena, string Army)[] Leaners = [("open-field", "melee"), ("ridge", "mixed"), ("falling-gate", "mixed"), ("chokepoint", "melee")];

    static OrderProbe ProbeOf(string letters)
    {
        var p = OrderProbe.None;
        foreach (char ch in letters)
            p |= ch switch { 'E' => OrderProbe.EngagementSwap, 'S' => OrderProbe.SpawnReversed, 'N' => OrderProbe.NavigatorReversed, 'I' => OrderProbe.IdParityFlip, 'U' => OrderProbe.LegacyMesh, _ => throw new ArgumentException("probe letter " + ch) };
        return p;
    }

    static Func<IBattleEngine> EngineWith(OrderProbe p) => () => TracerWorlds.Engine(new TracerBattleOptions { Probe = p });

    [Fact]
    public async Task Sweep_large_n_leans_and_probes()
    {
        var plan = Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_SWEEP");
        if (string.IsNullOrWhiteSpace(plan)) return;   // inert unless asked for
        int seeds = int.TryParse(Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_SEEDS"), out var s) ? s : 200;
        foreach (var token in plan.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string text;
            if (token == "scan")
            {
                var lines = new List<string>();
                var sc = (Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_SCAN") ?? "1.0,3.0,0.1").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                for (int k = 0; sc[0] + sc[2] * k <= sc[1] + 1e-9; k++)
                {
                    double f = sc[0] + sc[2] * k;
                    var rows = await BalanceHarness.SwapTable(Fresh, f, 5001, 8);
                    int n = rows.Sum(r => r.Battles);
                    lines.Add($"f {f:0.00}  X share {rows.Sum(r => r.XScore * r.Battles) / n:0.000}  attacker share {rows.Sum(r => r.AttackerScore * r.Battles) / n:0.000}  ({n} battles)");
                }
                text = string.Join(Environment.NewLine, lines);
            }
            else if (token.StartsWith("diag"))
            {
                var probe = token.Contains(':') ? ProbeOf(token.Split(':')[1]) : OrderProbe.None;
                var lines = new List<string>();
                foreach (var (arena, army) in Leaners)
                {
                    var e = EngineWith(probe)(); var m = BalanceHarness.Mirrors.First(x => x.Name == army);
                    for (int seed = 1001; seed <= 1006; seed++)
                    {
                        var st = await Sample.RunAsync(e, new BattleSetup(arena, seed, BalanceHarness.TickLimit, new BattleSide(Side.Player, m.Cohorts, m.Orders), new BattleSide(Side.Enemy, m.Cohorts, m.Orders)));
                        lines.Add($"{arena,-13} {army,-6} seed {seed} {st.Result!.Outcome,-12} ticks {st.Result.Ticks,3}  lost A {st.Result.AttackerLosses.Sum(l => l.Lost),3} D {st.Result.DefenderLosses.Sum(l => l.Lost),3}");
                    }
                }
                text = string.Join(Environment.NewLine, lines);
            }
            else if (token.StartsWith("swapb"))
            {
                // swapb<N>: the matched pair with the attacker given +N percent power, to read the check's sensitivity.
                int bonus = int.Parse(token[5..]);
                int sw = int.TryParse(Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_SWAPSEEDS"), out var q) ? q : 100;
                var rows = await BalanceHarness.SwapTable(() => new AttackerBoostEngine(Fresh(), bonus), BalanceHarness.PairScale, 2001, sw);
                text = string.Join(Environment.NewLine, rows.Select(r => $"{r.Arena,-13} attacker share {r.AttackerScore:0.000} (z {(r.AttackerScore - 0.5) / (0.5 / Math.Sqrt(r.Battles)):+0.0;-0.0})  over {r.Battles}"));
            }
            else if (token.StartsWith("swap"))
            {
                // swap[null][:letters]: the matched pair both ways, seeds 2001.. (swap) or 2101.. (swapnull), DOCTRINE_BALANCE_SWAPSEEDS (default 100) seeds.
                var parts = token.Split(':');
                int sw = int.TryParse(Environment.GetEnvironmentVariable("DOCTRINE_BALANCE_SWAPSEEDS"), out var q) ? q : 100;
                var rows = await BalanceHarness.SwapTable(EngineWith(parts.Length > 1 ? ProbeOf(parts[1]) : OrderProbe.None), BalanceHarness.PairScale, parts[0] == "swapnull" ? 2101 : 2001, sw);
                text = string.Join(Environment.NewLine, rows.Select(r => $"{r.Arena,-13} attacker share {r.AttackerScore:0.000} (z {(r.AttackerScore - 0.5) / (0.5 / Math.Sqrt(r.Battles)):+0.0;-0.0})  X share {r.XScore:0.000}  over {r.Battles}"));
            }
            else if (token == "rerun")
            {
                var a = await BalanceHarness.MirrorTable(Fresh, seeds, null, 1001, Leaners);
                var b = await BalanceHarness.MirrorTable(Fresh, seeds, null, 1001, Leaners);
                bool same = a.Count == b.Count && a.Zip(b).All(p => p.First.Digests.SequenceEqual(p.Second.Digests));
                text = same ? "identical digests battle by battle" : "DIFFERENT DIGESTS (outcome D)";
                Assert.True(same, text);
            }
            else
            {
                var parts = token.Split(':');
                int from = parts[0] == "null" ? 1201 : 1001;
                var probe = parts.Length > 1 ? ProbeOf(parts[1]) : OrderProbe.None;
                var cells = await BalanceHarness.MirrorTable(EngineWith(probe), seeds, null, from, Leaners);
                text = BalanceHarness.Format(cells);
            }
            output.WriteLine("== " + token + Environment.NewLine + text); Dump("sweep " + token, text);
        }
    }
}
