using Doctrine;
using Doctrine.Lane;
using ShowroomLane;

/// <summary>
/// Proof of the playable lane match (Showroom\Services\Doctrine\Lane, compiled into this project): phase timing, plays only at the pause, both sides' plays at one tick, the
/// placeholder economy, the bot, the picture between ticks and the packet for the canvas. UI-free: time is passed in as exact seconds (0.125 s steps are exact in binary, so the
/// tick counts below are exact). Each group takes a <see cref="LaneMatchProbe"/> so the mutants can break ONE thing of the service and turn the group red.
/// </summary>
static class MatchProof
{
    const double Step = 0.125;   // 1.25 ticks at 1x, 2.5 at 2x, 5 at 4x: all exact

    static Task<LaneMatch> New(int seed, LaneMatchProbe probe, ILaneSeat? player = null, ILaneSeat? enemy = null, int maxTicks = 6)
        => LaneMatch.CreateAsync(new LaneMatchOptions { Seed = seed, Player = player, Enemy = enemy, Probe = probe, MaxTicksPerAdvance = maxTicks }, Rig.Ctx());

    /// <summary>Advances in exact steps until the stage is not <paramref name="stage"/> any more (at most <paramref name="cap"/> steps); returns the steps taken.</summary>
    static async Task<int> Until(LaneMatch m, MatchStage stage, int cap = 5000)
    {
        int n = 0;
        while (m.Stage == stage && n < cap) { await m.AdvanceAsync(Step); n++; }
        return n;
    }

    sealed class FixedSeat(LanePlan plan) : ILaneSeat
    {
        public bool IsHuman => false;
        public ValueTask<LanePlan> PlanAsync(LaneSeatView view, CancellationToken ct) => ValueTask.FromResult(plan);
    }

    // 1. ---- phase timing ----------------------------------------------------------------------------------------------------

    public static async Task Timing(LaneMatchProbe probe)
    {
        H.Section("timing: a run is 30 s of game time at 10 ticks a second, a pause is 30 real seconds, speed touches the run only");
        var m = await New(5, probe);
        H.Check(m.Stage == MatchStage.Pause && m.Tick == 0 && m.PauseLeft == 30 && m.PauseHashes.Count == 1, "a match opens in a pause at tick 0 with 30 s on the clock and its first hash logged");
        for (int i = 0; i < 239; i++) await m.AdvanceAsync(Step);   // 29.875 s
        H.Check(m.Stage == MatchStage.Pause, "after 29.875 s the pause is still open (the clock has not run out)");
        await m.AdvanceAsync(Step);
        H.Check(m.Stage == MatchStage.Run && m.Tick == 0, "at 30 s the pause ends by its clock and the run starts at the same tick (no tick passed in the pause)");

        // the run: 240 steps of 0.125 s at 1x = 300 ticks exactly
        for (int i = 0; i < 239; i++) await m.AdvanceAsync(Step);
        H.Check(m.Stage == MatchStage.Run && m.Tick == 298, $"1x: after 29.875 s of run the battle is at tick 298 and still running (it is at {m.Tick})");
        await m.AdvanceAsync(Step);
        H.Check(m.Stage == MatchStage.Pause && m.Tick == LaneRules.PhaseTicks && m.Battle.AtPause, "1x: after 30 s of run the match stands paused at tick 300, on the phase boundary");
        H.Check(m.PauseHashes.Count == 2 && m.PauseHashes[1].Tick == 300 && m.PauseHashes[1].Hash == m.Battle.StateHash(), "the hash logged at the pause is the state hash of that tick");

        // 4x: the same 300 ticks in 7.5 s; and the pause is still 30 real seconds long at 4x
        var f = await New(5, probe);
        H.Check(f.SetSpeed(4) && !f.SetSpeed(3) && !f.SetSpeed(0) && f.Speed == 4, "speed is 1, 2 or 4 and nothing else");
        for (int i = 0; i < 239; i++) await f.AdvanceAsync(Step);
        H.Check(f.Stage == MatchStage.Pause, "4x: the pause is still 30 real seconds long (speed does not shorten it)");
        await f.AdvanceAsync(Step);
        H.Check(f.Stage == MatchStage.Run, "4x: the pause ends at 30 s");
        int steps = await Until(f, MatchStage.Run);
        H.Check(steps == 60 && f.Tick == 300, $"4x: the run takes 7.5 s (60 steps) to reach tick 300 (took {steps} steps, tick {f.Tick})");
        var h = await New(5, probe); h.SetSpeed(2); await h.AdvanceAsync(30.0);
        int s2 = await Until(h, MatchStage.Run);
        H.Check(s2 == 120 && h.Tick == 300, $"2x: the run takes 15 s (120 steps) to reach tick 300 (took {s2})");

        // a slow device: the clock asks for more ticks than one call may run; the match plays slower and does not carry a backlog
        var s = await New(5, probe); await s.AdvanceAsync(30.0);
        var r = await s.AdvanceAsync(10.0);
        H.Check(r.Ticks == 6 && s.Behind && s.Tick == 6, $"a call that asks for 100 ticks runs the 6 it may, and says it is behind (ran {r.Ticks})");
        var r2 = await s.AdvanceAsync(0.2);
        H.Check(!s.Behind && r2.Ticks <= 3, $"and then plays on at the clock's pace, with no backlog to catch up (ran {r2.Ticks})");

        // bots only: no human to wait for, so a pause does not hold the match up
        var b = await New(6, probe, new LaneBot(probe), new LaneBot(probe));
        await b.AdvanceAsync(0);
        H.Check(!b.AnyHuman && b.Stage == MatchStage.Run, "with only bots at the table the pause ends at once");
    }

    // 2. ---- plays only at the pause, both sides together --------------------------------------------------------------------

    public static async Task PlaysAtPause(LaneMatchProbe probe)
    {
        H.Section("plays: staged only at a pause, ended early by Ready, applied by both sides at one tick");
        var m = await New(7, probe);
        var seat = m.Human!;
        H.Check(seat.Buy(1, 0, out _), "at the pause a pack can be staged");
        H.Check(seat.Order(1, FieldOrderKind.Hold, out _), "and an order");
        H.Check(m.Ready() && m.Stage == MatchStage.Pause, "Ready is taken at the pause (the pause ends on the next advance)");
        await m.AdvanceAsync(0);
        H.Check(m.Stage == MatchStage.Run && m.PauseLeft > 29, "Ready ended the pause early, with the clock still near 30 s");
        H.Check(m.Battle.RosterCount(Side.Player, 1, 0) == LaneEconomy.PackSize && m.Battle.OrderOf(Side.Player, 1) == FieldOrderKind.Hold, "the staged pack and order were applied (a pack of 4 spearmen stands in lane 2, on Hold)");
        H.Check(m.LastResults.All(x => x.Applied), "every play was accepted");
        H.Check(m.Battle.Bodies(Side.Enemy) > 0, "the bot played at the same pause (the enemy has bodies on the board)");
        var batches = m.Battle.Replay.Batches;
        H.Check(batches.Count == 1 && batches[0].Tick == 0 && batches[0].Plays.Any(p => p is SpawnPlay { Side: Side.Player }) && batches[0].Plays.Any(p => p is SpawnPlay { Side: Side.Enemy }),
            $"both sides' plays were applied in ONE batch at tick 0 (batches: {batches.Count})");

        // in the run: staging is refused, Ready is refused, and nothing is applied until the next pause
        H.Check(!seat.Buy(0, 0, out var why) && why.Length > 0, "in the run a pack cannot be staged, and the refusal says why");
        H.Check(!seat.Order(0, FieldOrderKind.Hunt, out _) && !m.Ready(), "in the run an order cannot be staged and Ready does nothing");
        int before = m.Battle.Replay.Batches.Count;
        await Until(m, MatchStage.Run);
        H.Check(m.Stage == MatchStage.Pause && m.Battle.Replay.Batches.Count == before, "the whole run applied no play");
        // the next pause: batches land on pause ticks only
        H.Check(seat.Buy(2, 3, out _), "at the next pause staging works again");
        await m.AdvanceAsync(31.0);
        H.Check(m.Battle.Replay.Batches.All(x => x.Tick % LaneRules.PhaseTicks == 0), "every batch of the match sits on a pause tick");
        H.Check(seat.Packs.Count == 0 && seat.Orders.Count == 0, "the seat starts the next pause with nothing staged");
        // the match is a plain list of plays: a refusal for a bad lane
        var m2 = await New(7, probe);
        H.Check(!m2.Human!.Buy(9, 0, out _) && !m2.Human.Buy(0, 99, out _) && !m2.Human.Order(0, FieldOrderKind.Flank, out _), "an unknown lane, an unknown kind and Flank are refused");
    }

    // 3. ---- the placeholder economy -----------------------------------------------------------------------------------------

    public static async Task Economy(LaneMatchProbe probe)
    {
        H.Section("economy: ten points a pause, packs cost by kind, unspent points carry");
        var m = await New(8, probe);
        var seat = m.Human!;
        H.Check(m.Economy(Side.Player).Balance == LaneEconomy.PointsPerPause && m.Economy(Side.Enemy).Balance == LaneEconomy.PointsPerPause, "each side starts the first pause with the same ten points");
        H.Check(LaneEconomy.Cost(0) == Roster.PriceOf(0) && LaneEconomy.Cost(7) == Roster.PriceOf(7) && LaneEconomy.Cost(7) > LaneEconomy.Cost(0), "a pack costs the kind's price from the game's tables (catapults more than spearmen)");
        int bought = 0;
        while (seat.Buy(0, 7, out _) && bought < 50) bought++;
        H.Check(bought * LaneEconomy.Cost(7) <= LaneEconomy.PointsPerPause && seat.Remaining >= 0 && bought >= 1, $"packs can be staged only while the points last ({bought} catapult packs, {seat.Remaining} left)");
        H.Check(!seat.Buy(0, 7, out var why) && why.Length > 0, "the next one is refused and says why");
        seat.RemovePack(0);
        H.Check(seat.Remaining == LaneEconomy.PointsPerPause - (bought - 1) * LaneEconomy.Cost(7), "taking a pack back gives its points back");
        seat.Clear();

        // carry: spend nothing at the first pause, and the next pause starts with twenty
        await m.AdvanceAsync(31.0);
        await Until(m, MatchStage.Run);
        H.Check(m.Stage == MatchStage.Pause && m.Economy(Side.Player).Balance == 2 * LaneEconomy.PointsPerPause, $"points not spent carry to the next pause (balance {m.Economy(Side.Player).Balance})");
        seat.Buy(0, 0, out _);
        await m.AdvanceAsync(31.0);
        H.Check(m.Economy(Side.Player).Balance == 2 * LaneEconomy.PointsPerPause - LaneEconomy.Cost(0), "spending takes the cost of the pack off the purse");

        // a plan that costs more than the purse is trimmed by the match, whoever planned it
        var greedy = new LanePlan(Enumerable.Range(0, 20).Select(i => new PackBuy(i % 3, 0)).ToArray(), Array.Empty<LaneOrderChoice>());
        var g = await New(9, probe, new FixedSeat(greedy), new FixedSeat(LanePlan.Empty));
        await g.AdvanceAsync(0);
        int expect = LaneEconomy.PointsPerPause / LaneEconomy.Cost(0);
        H.Check(g.LastPlans[0].Packs.Count == expect && g.Battle.Bodies(Side.Player) == expect * LaneEconomy.PackSize, $"a plan of 20 packs is cut to the {expect} the purse buys (a seat other than the page's can plan too)");
        H.Check(g.Economy(Side.Player).Balance >= 0, "the purse never goes below zero");

        // the bot spends its purse
        var b = await New(10, probe);
        await b.AdvanceAsync(31.0);
        H.Check(b.Economy(Side.Enemy).Balance < LaneEconomy.CheapestPack, $"the bot spends its purse down to less than the cheapest pack (left {b.Economy(Side.Enemy).Balance})");
    }

    // 4. ---- same seed, same match; the bot is deterministic -------------------------------------------------------------------

    public static async Task Determinism(LaneMatchProbe probe)
    {
        H.Section("determinism: same seed, same match; the bot draws from the seed; real time and a batch run agree");
        const int phases = 5;
        async Task<List<ulong>> Batch(int seed)
        {
            var m = await New(seed, probe, new LaneBot(probe), new LaneBot(probe));
            await m.PlayToEndAsync(phases);
            return m.PauseHashes.Select(x => x.Hash).ToList();
        }
        var a = await Batch(7); var b = await Batch(7); var c = await Batch(8);
        H.Check(a.Count >= phases && a.SequenceEqual(b), $"the same seed gives the same hash at all {a.Count} pauses, twice in a row");
        H.Check(!a.SequenceEqual(c) && a.Skip(1).First() != c.Skip(1).First(), "another seed is another match (the hash differs from the first pause on)");

        // driven by the clock in small exact steps, the match is the one the batch run plays
        var rt = await New(7, probe, new LaneBot(probe), new LaneBot(probe));
        int guard = 0;
        while (rt.Stage != MatchStage.Over && rt.PauseHashes.Count < phases + 1 && guard++ < 100000) await rt.AdvanceAsync(Step);
        H.Check(rt.PauseHashes.Count >= phases + 1 && rt.PauseHashes.Take(phases + 1).Select(x => x.Hash).SequenceEqual(a.Take(phases + 1)), "driven by the clock (0.125 s steps) the match has the same hash at every pause as the batch run");
        var rt2 = await New(7, probe, new LaneBot(probe), new LaneBot(probe)); rt2.SetSpeed(4); guard = 0;
        while (rt2.Stage != MatchStage.Over && rt2.PauseHashes.Count < phases + 1 && guard++ < 100000) await rt2.AdvanceAsync(0.31);
        H.Check(rt2.PauseHashes.Take(phases + 1).Select(x => x.Hash).SequenceEqual(a.Take(phases + 1)), "and so does 4x speed with odd step sizes (the clock never enters the battle)");

        // the bot itself: same view and seed, same plan; another seed, another plan
        var m3 = await New(7, probe, new LaneBot(probe), new LaneBot(probe));
        await m3.PlayToEndAsync(2);
        var snap = m3.Battle.Snapshot();
        LaneSeatView View(int seed) => new(Side.Enemy, seed, m3.Phase, m3.Tick, snap, [FieldOrderKind.Advance, FieldOrderKind.Advance, FieldOrderKind.Advance], 14, m3.Battle.Bodies(Side.Enemy), 120, 3);
        var bot = new LaneBot(probe);
        var p1 = await bot.PlanAsync(View(7), default); var p2 = await bot.PlanAsync(View(7), default); var p3 = await bot.PlanAsync(View(99), default);
        H.Check(p1.SameAs(p2), "the bot gives the same plan for the same board and seed");
        H.Check(!p1.SameAs(p3), "and another plan for another seed");
        int cost = p1.Packs.Sum(p => LaneEconomy.Cost(p.Kind));
        H.Check(p1.Packs.Count > 0 && cost <= 14 && 14 - cost < LaneEconomy.CheapestPack, $"the bot spends its purse (a purse of 14 bought {p1.Packs.Count} packs for {cost})");
        H.Check(p1.Packs.All(p => p.Lane is >= 0 and < 3 && p.Kind is >= 0 and < 8) && p1.Orders.All(o => o.Lane is >= 0 and < 3 && o.Order != FieldOrderKind.Flank), "its packs and orders are all legal");
    }

    // 5. ---- the picture between ticks and the canvas packet -------------------------------------------------------------------

    public static async Task Picture(LaneMatchProbe probe)
    {
        H.Section("picture: bodies drawn between the last two ticks; the packet for the canvas reads back");
        var m = await New(11, probe, new LaneBot(probe), new LaneBot(probe));
        await m.AdvanceAsync(0);                         // plays applied, run begins
        for (int i = 0; i < 5; i++) await m.AdvanceAsync(Step);   // 6.25 ticks: tick 6, a quarter of the way to the next
        H.Check(m.Tick == 6 && m.Alpha == 0.25, $"the run is at tick 6, a quarter of the way on (tick {m.Tick}, alpha {m.Alpha})");
        var draw = m.Bodies();
        int moved = 0, checkedBodies = 0; bool exact = true;
        var prev = m.Prev.Units.ToDictionary(u => u.Id);
        foreach (var d in draw)
        {
            if (!prev.TryGetValue(d.Id, out var pu)) continue;
            var cu = m.Curr.Units.First(u => u.Id == d.Id);
            checkedBodies++;
            if (pu.X != cu.X || pu.Y != cu.Y) moved++;
            float ex = pu.X + (cu.X - pu.X) * 0.25f, ey = pu.Y + (cu.Y - pu.Y) * 0.25f;
            if (Math.Abs(d.X - ex) > 1e-4f || Math.Abs(d.Y - ey) > 1e-4f) exact = false;
        }
        H.Check(checkedBodies > 10 && moved > 0, $"bodies are on the move at tick 6 (instrument: {moved} of {checkedBodies} moved)");
        H.Check(exact, "each body is drawn a quarter of the way from its place a tick ago to its place now");
        var at0 = new List<LaneBodyDraw>(); var at1 = new List<LaneBodyDraw>();
        LaneInterp.Between(m.Prev, m.Curr, 0, at0); LaneInterp.Between(m.Prev, m.Curr, 1, at1);
        H.Check(at0.Count > 0 && at0.All(d => !prev.TryGetValue(d.Id, out var pu) || (d.X == pu.X && d.Y == pu.Y)) && at1.Select(d => (d.Id, d.X, d.Y)).SequenceEqual(m.Curr.Units.Select(u => (u.Id, u.X, u.Y))), "alpha 0 is the earlier tick and alpha 1 is the later one");

        // the packet
        var bytes = LanePack.Pack(m);
        var (header, bodies) = LanePack.Unpack(bytes);
        H.Check(bytes.Length == 4 * (LanePack.HeaderFloats + LanePack.BodyFloats * draw.Count) && header[0] == m.Tick && header[1] == (int)MatchStage.Run && bodies.Count == draw.Count, "the packet has the header and ten floats a body");
        H.Check(bodies.Zip(draw).All(z => z.First.Id == z.Second.Id && z.First.X == z.Second.X && z.First.Y == z.Second.Y && z.First.Kind == z.Second.Kind && z.First.Side == z.Second.Side), "it reads back as the bodies that were drawn");
        H.Check(Math.Abs(header[3] - 1f) < 1e-6 && Math.Abs(header[4] - 1f) < 1e-6, "both bases are whole at the start (1.0 of full)");

        // the two JSON texts for the script parse and say what the ground says
        using var lay = System.Text.Json.JsonDocument.Parse(LanePayload.Layout(m));
        var root = lay.RootElement; var real = m.Battle.Layout;
        H.Check(root.GetProperty("rows").GetArrayLength() == real.Rows.Count && root.GetProperty("laneCentreY").GetArrayLength() == 3 && root.GetProperty("width").GetSingle() == real.Width && root.GetProperty("holdPct").GetInt32() == LaneRules.HoldPct,
            "the ground text parses and carries the map rows, the lane centres, the width and the hold share");
        using var info = System.Text.Json.JsonDocument.Parse(LanePayload.Info(m, 1));
        H.Check(info.RootElement.GetProperty("selected").GetInt32() == 1 && info.RootElement.GetProperty("stage").GetInt32() == (int)MatchStage.Run && info.RootElement.GetProperty("orders").GetArrayLength() == 2, "the information text parses (selected lane, stage, each side's orders)");
    }
}
