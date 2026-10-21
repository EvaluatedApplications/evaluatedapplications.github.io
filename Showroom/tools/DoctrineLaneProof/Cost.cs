using System.Diagnostics;
using Doctrine;
using Doctrine.Lane;

/// <summary>The cost table and the replay sizes (`dotnet run -c Release -- cost`), the double-fall search (`find-draw`) and a scratch trace (`debug`).</summary>
static class Cost
{
    static readonly int[] Kinds = [0, 3, 2, 1];   // Spearmen, Archers, Riders, Shieldwall: one of each class
    static readonly double Freq = Stopwatch.Frequency / 1000.0;

    /// <summary>Tops each side up to <paramref name="perSide"/> live bodies, spread over the lanes and the four kinds.</summary>
    static List<LanePlay> Refill(LaneBattle b, int perSide)
    {
        var plays = new List<LanePlay>();
        int lanes = b.Setup.Lanes;
        foreach (var side in new[] { Side.Player, Side.Enemy })
        {
            int need = perSide - b.Bodies(side);
            for (int i = 0; i < need;)
            {
                int chunk = Math.Min(need - i, 4);
                plays.Add(new SpawnPlay(side, (i / 4) % lanes, Kinds[(i / 4 + (side == Side.Enemy ? 1 : 0)) % Kinds.Length], chunk));
                i += chunk;
            }
        }
        return plays;
    }

    record Row(int Phase, int LiveStart, int LiveEnd, double MsPerTick, double ThinkMs, double MoveMs, double EngageMs, double ResolveMs, double OtherMs, long Paths, long Strikes);

    static async Task<(List<Row> Rows, LaneBattle Battle)> Steady(int total, int phases, bool refill, int seed = 5)
    {
        var b = await Rig.New(new LaneSetup { BaseHp = 100000, TickCap = 1_000_000, MaxUnitsPerSide = Math.Max(120, total / 2) }, seed);   // bases that do not fall, so the match is the same length at every size
        var rows = new List<Row>();
        for (int ph = 0; ph < phases; ph++)
        {
            if (ph == 0 || refill) { var plays = Refill(b, total / 2); if (plays.Count > 0) b.ApplyPlays(plays); }
            var s0 = b.Stats; long t0 = s0.ThinkTicks, m0 = s0.MoveTicks, e0 = s0.EngageTicks, r0 = s0.ResolveTicks, p0 = s0.PathRequests, k0 = s0.Strikes;
            int live0 = b.LiveBodies;
            var sw = Stopwatch.StartNew();
            var r = await b.StepAsync();
            double ms = sw.Elapsed.TotalMilliseconds;
            double th = (s0.ThinkTicks - t0) / Freq, mv = (s0.MoveTicks - m0) / Freq, en = (s0.EngageTicks - e0) / Freq, rs = (s0.ResolveTicks - r0) / Freq;
            rows.Add(new Row(ph, live0, b.LiveBodies, ms / r.TicksRun, th / r.TicksRun, mv / r.TicksRun, en / r.TicksRun, rs / r.TicksRun, (ms - th - mv - en - rs) / r.TicksRun, s0.PathRequests - p0, s0.Strikes - k0));
        }
        return (rows, b);
    }

    public static async Task Run(string[] args)
    {
        Console.WriteLine($"native, {Environment.ProcessorCount} logical cores, .NET {Environment.Version}; {LaneRules.TicksPerSecond} ticks/s, phase = {LaneRules.PhaseTicks} ticks ({LaneRules.PhaseSeconds} s); all ms are wall clock");
        // warm up the JIT and Tracer on a throwaway match at the largest size
        await Steady(240, 2, true);

        // 0. the fixed cost of the pipeline itself
        var empty = await Rig.New(new LaneSetup(), 1);
        var sw0 = Stopwatch.StartNew(); await empty.StepAsync(); double emptyMs = sw0.Elapsed.TotalMilliseconds / LaneRules.PhaseTicks;
        Console.WriteLine($"\nempty field (no bodies): {emptyMs:F4} ms/tick (the EvalApp tick pipeline alone, 5 steps)");

        // 1. the first tick of a match (cold services) and the spawn burst
        var cold = await Rig.New(new LaneSetup(), 2);
        cold.ApplyPlays(Refill(cold, 60));
        var swc = Stopwatch.StartNew(); await cold.StepAsync(1); double firstTick = swc.Elapsed.TotalMilliseconds;
        Console.WriteLine($"first tick after a 120-body spawn (all path at once): {firstTick:F1} ms");

        // 1b. the spawn burst on a warm process: the first ticks after k bodies appear at once at a pause
        Console.WriteLine("\nSPAWN BURST (warm): ms of each of the first ticks after k bodies are spawned at one pause, then the mean of the next 20");
        foreach (int k in new[] { 20, 60, 120, 240 })
        {
            var sb = await Rig.New(new LaneSetup { MaxUnitsPerSide = 240 }, 3);
            var burst = new List<LanePlay>();
            for (int i = 0; i < k; i += 4) burst.Add(new SpawnPlay(i % 8 < 4 ? Side.Player : Side.Enemy, (i / 8) % 3, Kinds[(i / 4) % 4], Math.Min(4, k - i)));
            sb.ApplyPlays(burst);
            var tt = new List<double>();
            for (int t = 0; t < 24; t++) { var s1 = Stopwatch.StartNew(); await sb.StepAsync(1); tt.Add(s1.Elapsed.TotalMilliseconds); }
            var st = sb.Stats;
            Console.WriteLine($"  k={k,-4} ticks 1-4: {string.Join(", ", tt.Take(4).Select(x => x.ToString("F1")))} ms; next 20 mean {tt.Skip(4).Average():F2} ms; paths requested {sb.Stats.PathRequests}; stage ms over 24 ticks: think {st.ThinkTicks / Freq:F1} move {st.MoveTicks / Freq:F1} engage {st.EngageTicks / Freq:F1} resolve {st.ResolveTicks / Freq:F1}");
        }

        // 2. march (no contact yet), then the whole match with a refill at every pause
        Console.WriteLine("\nTABLE A. steady battle: both sides refilled to N/2 live bodies at every pause, 3 lanes, 8 phases (ms are per tick, averaged over a phase)");
        Console.WriteLine("  N    live(start-end)   ms/tick   think   move  engage resolve  other   paths/phase strikes/phase");
        var summary = new List<(int N, double March, double Fight, double Mean, double Max, double MeanLive)>();
        foreach (int n in new[] { 20, 60, 120, 240, 480, 960 })
        {
            var (rows, _) = await Steady(n, 8, true);
            foreach (var r in rows)
                Console.WriteLine($"  {n,-4} ph{r.Phase} {r.LiveStart,4}-{r.LiveEnd,-4}      {r.MsPerTick,7:F3} {r.ThinkMs,7:F3} {r.MoveMs,6:F3} {r.EngageMs,7:F3} {r.ResolveMs,7:F3} {r.OtherMs,6:F3}   {r.Paths,8} {r.Strikes,8}");
            var post = rows.Skip(1).ToList();
            summary.Add((n, rows[0].MsPerTick, post.Max(r => r.MsPerTick), post.Average(r => r.MsPerTick), rows.Max(r => r.MsPerTick), rows.Average(r => (r.LiveStart + r.LiveEnd) / 2.0)));
        }
        Console.WriteLine("\nSUMMARY (ms per tick; phase = x300)");
        Console.WriteLine("  N     avg live   phase0 (march+spawn)   later phases mean   later phases worst   ms per phase (mean)   x2 wasm   x4 wasm   budget per tick at 10/s = 100 ms");
        foreach (var s in summary)
            Console.WriteLine($"  {s.N,-5} {s.MeanLive,7:F0}   {s.March,12:F3}          {s.Mean,12:F3}       {s.Fight,12:F3}      {s.Mean * LaneRules.PhaseTicks,12:F0}      {s.Mean * 2,7:F2}   {s.Mean * 4,7:F2}");
        double Slope(int i, int j) => Math.Log(summary[j].Mean / summary[i].Mean) / Math.Log(summary[j].MeanLive / summary[i].MeanLive);
        Console.WriteLine($"  log-log slope of mean ms/tick against average live bodies (1.0 = linear, 2.0 = quadratic): 20..240 {Slope(0, 3):F2}, 240..960 {Slope(3, 5):F2}, 60..960 {Slope(1, 5):F2}");

        // 3. the worst single tick and the spread, at 240
        Console.WriteLine("\nTABLE B. per-tick spread at N=240 (steady, refilled, 4 phases, one tick per call)");
        var b = await Rig.New(new LaneSetup { BaseHp = 100000, TickCap = 1_000_000 }, 5);
        var times = new List<double>();
        for (int ph = 0; ph < 4; ph++)
        {
            b.ApplyPlays(Refill(b, 120));
            for (int t = 0; t < LaneRules.PhaseTicks; t++) { var sw = Stopwatch.StartNew(); await b.StepAsync(1); times.Add(sw.Elapsed.TotalMilliseconds); }
        }
        times.Sort();
        Console.WriteLine($"  ticks {times.Count}: mean {times.Average():F3} ms, median {times[times.Count / 2]:F3}, p99 {times[(int)(times.Count * 0.99)]:F3}, max {times[^1]:F3} ms");

        // 4. replay sizes
        Console.WriteLine("\nREPLAY SIZES (a 240-body steady match, 8 phases)");
        var (_, big) = await Steady(240, 8, true);
        int plays = LaneReplayCodec.Encode(big.Replay).Length;
        var snaps = new List<LaneSnapshot>();
        var again = await Rig.New(big.Setup, big.Seed);
        long perTick = 0, perPause = 0; int ticksCounted = 0;
        for (int ph = 0; ph < 8; ph++)
        {
            foreach (var batch in big.Replay.Batches.Where(x => x.Tick == again.Tick)) again.ApplyPlays(batch.Plays);
            for (int t = 0; t < LaneRules.PhaseTicks; t++) { await again.StepAsync(1); perTick += EncodeSnapshot(again.Snapshot()).Length; ticksCounted++; }
            perPause += EncodeSnapshot(again.Snapshot()).Length;
        }
        Console.WriteLine($"  plays + seed: {plays:N0} bytes for the whole match (re-simulated on playback, hash-checked at every pause)");
        Console.WriteLine($"  snapshots at every pause only: {perPause:N0} bytes (too sparse to animate: 30 s between frames)");
        Console.WriteLine($"  snapshots at every tick: {perTick:N0} bytes over {ticksCounted} ticks ({perTick / (double)plays:N0}x larger than plays + seed)");
    }

    /// <summary>A compact snapshot: tick, then per body id (delta), x and y in 1/16 units, hit points, facing as two sbytes, and side/lane/kind packed in one byte (varints).</summary>
    static byte[] EncodeSnapshot(LaneSnapshot s)
    {
        var w = new List<byte>();
        void Var(int v) { uint z = (uint)((v << 1) ^ (v >> 31)); while (z >= 0x80) { w.Add((byte)(z | 0x80)); z >>= 7; } w.Add((byte)z); }
        Var(s.Tick); Var(s.Units.Count); Var((int)Math.Min(int.MaxValue, s.PlayerBaseHp)); Var((int)Math.Min(int.MaxValue, s.EnemyBaseHp));
        int prev = 0;
        foreach (var u in s.Units)
        {
            Var(u.Id - prev); prev = u.Id;
            Var((int)MathF.Round(u.X * 16f)); Var((int)MathF.Round(u.Y * 16f)); Var(u.Hp);
            w.Add((byte)(sbyte)MathF.Round(u.FacingX * 127f)); w.Add((byte)(sbyte)MathF.Round(u.FacingY * 127f));
            w.Add((byte)(((int)u.Side << 7) | (u.Lane << 5) | u.Kind));
        }
        return w.ToArray();
    }

    /// <summary>Does either side lean? Mirror armies in one lane, many seeds; reports the mean survivors of each side and how often each side had more (ties excluded).</summary>
    public static async Task Lean(bool enemyFirst, int seeds)
    {
        foreach (int kind in new[] { 0, 3 })
        {
            double ps = 0, es = 0; int pw = 0, ew = 0, ties = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var b = await Rig.New(new LaneSetup { Lanes = 1, LaneCols = 24 }, seed);
                b.ApplyPlays(enemyFirst ? [new SpawnPlay(Side.Enemy, 0, kind, 15), new SpawnPlay(Side.Player, 0, kind, 15)] : [new SpawnPlay(Side.Player, 0, kind, 15), new SpawnPlay(Side.Enemy, 0, kind, 15)]);
                await b.RunToAsync(3 * LaneRules.PhaseTicks);
                int p = b.Bodies(Side.Player), e = b.Bodies(Side.Enemy);
                ps += p; es += e; if (p > e) pw++; else if (e > p) ew++; else ties++;
            }
            Console.WriteLine($"kind {kind} {Doctrine.Roster.NameOf(kind),-9}: mean survivors player {ps / seeds:F1}, enemy {es / seeds:F1}; more survivors: player {pw}, enemy {ew}, tie {ties} ({seeds} seeds, 15 v 15, one lane, {(enemyFirst ? "enemy" : "player")} spawns first)");
        }
    }

    public static async Task Debug()
    {
        {
            var m = await Rig.New(new LaneSetup(), 1);
            var plays = new List<LanePlay>();
            for (int lane = 0; lane < 3; lane++) for (int k = 0; k < 8; k++) plays.Add(new SpawnPlay(Side.Player, lane, k, 2 + k));
            m.ApplyPlays(plays);
            long total = 0; int unreadable = 0;
            for (int lane = 0; lane < 3; lane++) for (int k = 0; k < 8; k++) { long n = m.RosterCount(Side.Player, lane, k); if (n < 0) unreadable++; else total += n; }
            Console.WriteLine($"24 (kind, lane) items on one side: unreadable {unreadable}, read total {total}, Total() {m.RosterTotal(Side.Player)}");
        }
        var setup = new LaneSetup { Lanes = 3, LaneCols = 24, BaseHp = 1 };
        var b = await Rig.New(setup, 4);
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 20), new SpawnPlay(Side.Enemy, 1, 0, 10)]);
        foreach (int t in new[] { 100, 200, 300, 400, 470 })
        {
            await b.RunToAsync(t);
            var s = b.Snapshot();
            foreach (var side in new[] { Side.Player, Side.Enemy })
            {
                var u = s.Units.Where(x => x.Side == side).ToList();
                Console.WriteLine($"t={t} {side}: n={u.Count} x {u.Min(q => q.X):F1}..{u.Max(q => q.X):F1} y {u.Min(q => q.Y):F1}..{u.Max(q => q.Y):F1}");
            }
            Console.WriteLine($"   bases {b.PlayerBaseHp}/{b.EnemyBaseHp} paths {b.Stats.PathRequests} notfound {b.Stats.PathsNotFound} strikes {b.Stats.Strikes} bstrikes {b.Stats.BaseStrikes}");
        }
    }

    /// <summary>
    /// Looks for a pair of one-sided raids (different lanes) whose bases (one soldier-life each) fall on the SAME tick in a combined run. Each raid is timed alone to prune the
    /// search; the combined run decides (the sides share one pathing call, so a raid can arrive a tick off its solo time). The result is typed into Scenarios.DoubleFall.
    /// </summary>
    public static async Task FindDraw()
    {
        var setup = new LaneSetup { Lanes = 3, LaneCols = 24, BaseHp = 1 };
        async Task<int> FallTick(Side side, int lane, int kind, int count)
        {
            var b = await Rig.New(setup, 4);
            b.ApplyPlays([new SpawnPlay(side, lane, kind, count)]);
            for (int i = 0; i < 12 && b.Status == LaneStatus.Running; i++) await b.StepAsync();
            return b.Status == LaneStatus.Running ? -1 : b.Tick;
        }
        var player = new List<(int Kind, int Lane, int Tick)>();
        for (int k = 0; k < 8; k++) for (int lane = 0; lane < 3; lane++) player.Add((k, lane, await FallTick(Side.Player, lane, k, 20)));
        var enemy = new List<(int Kind, int Lane, int Count, int Tick)>();
        for (int k = 0; k < 8; k++) for (int lane = 0; lane < 3; lane++) for (int n = 6; n <= 30; n += 2) enemy.Add((k, lane, n, await FallTick(Side.Enemy, lane, k, n)));
        Console.WriteLine($"timed {player.Count} player raids and {enemy.Count} enemy raids");
        foreach (var p in player.Where(x => x.Tick > 0))
            foreach (var e in enemy.Where(x => x.Tick > 0 && Math.Abs(x.Tick - p.Tick) <= 4 && x.Lane != p.Lane))
            {
                var b = await Rig.New(setup, 4);
                b.ApplyPlays([new SpawnPlay(Side.Player, p.Lane, p.Kind, 20), new SpawnPlay(Side.Enemy, e.Lane, e.Kind, e.Count)]);
                for (int i = 0; i < 12 && b.Status == LaneStatus.Running; i++) await b.StepAsync();
                if (b.Status == LaneStatus.Draw) { Console.WriteLine($"player kind {p.Kind} lane {p.Lane} x20 vs enemy kind {e.Kind} lane {e.Lane} x{e.Count}: Draw at tick {b.Tick}"); return; }
            }
        Console.WriteLine("no pair found");
    }
}
