using System.Numerics;
using Doctrine;
using Doctrine.Battle;
using Doctrine.Lane;
using Tracer;
using Tracer.Models;

static class Scenarios
{
    // 1. ---- determinism ---------------------------------------------------------------------------------------------------

    public static async Task Determinism(LaneProbe probe)
    {
        H.Section("determinism: same setup, seed and ordered plays give the same hash at every pause");
        var setup = new LaneSetup { BaseHp = 500, Probe = probe };   // bases that hold out, so the standard match runs past eight pauses
        var plan = Rig.StandardPlan(3);
        var a = await Rig.Run(setup, 7, plan, 8);
        var b = await Rig.Run(setup, 7, plan, 8);
        H.Check(a.Replay.Pauses.Count >= 8, $"the standard match reaches at least 8 pauses (it has {a.Replay.Pauses.Count})");
        H.Check(Rig.SamePauses(a, b), "twice in a row: the hash is equal at every pause");
        H.Check(Rig.SameUnits(a.Snapshot(), b.Snapshot()), "twice in a row: the final snapshot is equal body for body");
        var both = await Task.WhenAll(Rig.Run(setup, 7, plan, 8), Rig.Run(setup, 7, plan, 8));
        H.Check(Rig.SamePauses(a, both[0]) && Rig.SamePauses(a, both[1]), "two clients running at the same time give the same hash at every pause");
        // the two players' plays at one pause commute: the relay may deliver them in either order
        List<LanePlay[]> Flip(bool enemyFirst) => plan.Select(batch => (enemyFirst ? batch.OrderBy(pl => PlaySide(pl) == Side.Enemy ? 0 : 1) : batch.OrderBy(pl => PlaySide(pl) == Side.Player ? 0 : 1)).ToArray()).ToList();
        var pf = await Rig.Run(setup, 7, Flip(false), 8); var ef = await Rig.Run(setup, 7, Flip(true), 8);
        H.Check(Rig.SamePauses(pf, ef), "the two sides' plays at a pause give the same match in either order of arrival (same hash at every pause)");
        var other = await Rig.Run(setup, 8, plan, 8);
        H.Check(!Rig.SamePauses(a, other), "a different seed is a different match (the seed matters)");
        var fewer = await Rig.Run(setup, 7, plan.Take(2).ToList(), 8);
        H.Check(!Rig.SamePauses(a, fewer), "dropping the last play batch is a different match (the plays matter)");
        H.Note($"standard match: {a.Replay.Pauses.Count} pauses, ended {a.Status} at tick {a.Tick}, {a.Stats.Strikes} strikes, {a.Stats.Deaths} deaths");
    }

    static Side PlaySide(LanePlay p) => p is SpawnPlay s ? s.Side : ((OrderPlay)p).Side;

    // 2. ---- phase-split equivalence ----------------------------------------------------------------------------------------

    public static async Task PhaseSplit(LaneProbe probe)
    {
        H.Section("phase-split equivalence: phases, odd chunks and one long run are the same match");
        var setup = new LaneSetup { Probe = probe };
        var plan = new List<LanePlay[]> { Rig.StandardPlan(3)[0] };   // plays at tick 0 only, then none
        const int phases = 5;
        var byPhase = await Rig.Run(setup, 11, plan, phases, Mode.Phases);
        var byChunk = await Rig.Run(setup, 11, plan, phases, Mode.Chunks);
        var longRun = await Rig.New(setup, 11);
        longRun.ApplyPlays(plan[0]);
        await longRun.RunToAsync(phases * setup.PhaseTicks);
        var pauses = byPhase.Replay.Pauses;
        H.Check(pauses.Count == phases + 1 && pauses.Select((p, i) => p.Tick == i * setup.PhaseTicks).All(x => x), $"every pause is a whole phase ({setup.PhaseTicks} ticks) after the last: ticks {string.Join(",", pauses.Select(p => p.Tick))}");
        H.Check(byPhase.Tick == phases * setup.PhaseTicks && byChunk.Tick == byPhase.Tick && longRun.Tick == byPhase.Tick, "all three runs end on the same tick");
        H.Check(Rig.SamePauses(byPhase, byChunk), "stepping in odd chunks gives the hash of the phase run at every pause");
        H.Check(Rig.SamePauses(byPhase, longRun), "one long run gives the hash of the phase run at every pause it passes");
        H.Check(byPhase.StateHash() == longRun.StateHash() && Rig.SameUnits(byPhase.Snapshot(), longRun.Snapshot()), "and the same state at the end, body for body");
        H.Check(byPhase.Stats.Strikes > 0, "the fight is not vacuous (strikes landed)");
        // StepAsync stops at the pause whatever the budget
        var s = await Rig.New(setup, 3);
        var r1 = await s.StepAsync(int.MaxValue);
        H.Check(r1.TicksRun == setup.PhaseTicks && r1.AtPause, "Step with no budget runs exactly one phase and stops on the pause");
        var r2 = await s.StepAsync(40);
        H.Check(r2.TicksRun == 40 && !r2.AtPause, "Step with a budget below the pause stops short of it");
        var r3 = await s.StepAsync(int.MaxValue);
        H.Check(r3.TicksRun == setup.PhaseTicks - 40 && r3.AtPause, "the next Step finishes the phase and lands on the pause");
    }

    // 3. ---- plays only at a pause ----------------------------------------------------------------------------------------

    public static async Task PlaysAtPause(LaneProbe probe)
    {
        H.Section("no input is applied mid-phase");
        var setup = new LaneSetup { Probe = probe };
        var b = await Rig.New(setup, 5);
        H.Check(b.AtPause && b.Tick == 0, "a new match starts on a pause (tick 0)");
        b.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 5)]);
        await b.StepAsync(25);
        H.Check(!b.AtPause, "after 25 ticks the match is mid-phase");
        ulong before = b.StateHash(); int bodies = b.Bodies(Side.Player);
        bool threw = false;
        try { b.ApplyPlays([new SpawnPlay(Side.Player, 1, 0, 5), new OrderPlay(Side.Player, 0, FieldOrderKind.Hold)]); } catch (InvalidOperationException) { threw = true; }
        H.Check(threw, "a play mid-phase is refused (it throws)");
        H.Check(b.StateHash() == before && b.Bodies(Side.Player) == bodies, "and it changed nothing");
        await b.StepAsync();
        H.Check(b.AtPause, "stepping on lands on the pause again");
        var res = b.ApplyPlays([new SpawnPlay(Side.Player, 1, 0, 5)]);
        H.Check(res.Count == 1 && res[0].Applied && b.Bodies(Side.Player) == bodies + 5, "and a play at the pause is applied");
    }

    // 4. ---- plays do what they say -----------------------------------------------------------------------------------------

    public static async Task PlayEffects(LaneProbe probe)
    {
        H.Section("plays change the state as intended");
        var setup = new LaneSetup { Probe = probe };
        var b = await Rig.New(setup, 9);
        var layout = b.Layout;
        var res = b.ApplyPlays([new SpawnPlay(Side.Player, 1, 3, 12), new SpawnPlay(Side.Enemy, 2, 0, 5)]);
        H.Check(res.All(r => r.Applied) && res[0].Accepted == 12 && res[1].Accepted == 5, "a spawn accepts the bodies it was asked for");
        var snap = b.Snapshot();
        var mine = snap.Units.Where(u => u.Side == Side.Player).ToList(); var theirs = snap.Units.Where(u => u.Side == Side.Enemy).ToList();
        H.Check(mine.Count == 12 && theirs.Count == 5, "twelve player bodies and five enemy bodies stand on the field");
        H.Check(mine.All(u => u.Lane == 1 && u.Kind == 3) && theirs.All(u => u.Lane == 2 && u.Kind == 0), "each in the lane and of the kind asked for");
        H.Check(mine.All(u => u.X < layout.PlayerBaseEdgeX) && theirs.All(u => u.X > layout.EnemyBaseEdgeX), "at its own base (the player's behind its edge, the enemy's behind its)");
        H.Check(mine.All(u => Math.Abs(u.Y - layout.LaneCentreY[1]) < 5f) && theirs.All(u => Math.Abs(u.Y - layout.LaneCentreY[2]) < 5f), "level with the mouth of their lane");
        H.Check(snap.Units.All(u => u.Hp == UnitRules.Fp && u.MaxHp == UnitRules.Fp), "every body is one soldier at full strength");
        H.Check(snap.Units.All(u => OnGround(layout, u.X, u.Y)), "and on open ground");
        H.Check(b.RosterCount(Side.Player, 1, 3) == 12 && b.RosterTotal(Side.Player) == 12 && b.RosterCount(Side.Enemy, 2, 0) == 5 && b.RosterTotal(Side.Enemy) == 5,
            "the Pleroma roster holds the same bodies by side, lane and kind");

        var h0 = b.StateHash();
        var bad = b.ApplyPlays([
            new SpawnPlay(Side.Player, 3, 0, 1), new SpawnPlay(Side.Player, -1, 0, 1), new SpawnPlay(Side.Player, 0, 8, 1), new SpawnPlay(Side.Player, 0, -1, 1),
            new SpawnPlay(Side.Player, 0, 0, 0), new SpawnPlay(Side.Player, 0, 0, -4), new SpawnPlay((Side)7, 0, 0, 1),
            new OrderPlay(Side.Player, 0, FieldOrderKind.Flank), new OrderPlay(Side.Player, 9, FieldOrderKind.Hold)]);
        H.Check(bad.Count == 9 && bad.All(r => !r.Applied && r.Reason.Length > 0), "plays that mean nothing are refused, each with its reason");
        H.Check(b.StateHash() == h0, "and change nothing");

        // the cap on live bodies
        var capped = await Rig.New(new LaneSetup { MaxUnitsPerSide = 10, Probe = probe }, 9);
        var c1 = capped.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 15), new SpawnPlay(Side.Player, 0, 0, 1), new SpawnPlay(Side.Enemy, 0, 0, 10)]);
        H.Check(c1[0].Applied && c1[0].Accepted == 10 && c1[0].Reason.Contains("cap") && !c1[1].Applied && c1[2].Accepted == 10, "the body cap cuts a spawn short, refuses the next, and spares the other side");
        H.Check(capped.Bodies(Side.Player) == 10 && capped.RosterTotal(Side.Player) == 10, "and only ten bodies stand");

        // orders: Hold stops a lane at its line and leaves the other lanes exactly alone
        var spawn = new LanePlay[] { new SpawnPlay(Side.Player, 0, 0, 8), new SpawnPlay(Side.Player, 1, 0, 8), new SpawnPlay(Side.Player, 2, 0, 8) };
        var free = await Rig.New(setup, 9); free.ApplyPlays(spawn); await free.StepAsync(); await free.StepAsync();
        var held = await Rig.New(setup, 9); held.ApplyPlays([.. spawn, new OrderPlay(Side.Player, 1, FieldOrderKind.Hold)]); await held.StepAsync(); await held.StepAsync();
        double HoldX = layout.PlayerBaseEdgeX + (layout.EnemyBaseEdgeX - layout.PlayerBaseEdgeX) * LaneRules.HoldPct / 100.0;
        double MeanX(LaneBattle x, int lane) => x.Snapshot().Units.Where(u => u.Lane == lane).Average(u => u.X);
        H.Check(held.OrderOf(Side.Player, 1) == FieldOrderKind.Hold && held.OrderOf(Side.Player, 0) == FieldOrderKind.Advance, "the order is recorded for that lane only");
        H.Check(Math.Abs(MeanX(held, 1) - HoldX) < 6.0, $"a held lane stands at its hold line (mean x {MeanX(held, 1):F1}, line {HoldX:F1})");
        H.Check(MeanX(free, 1) > MeanX(held, 1) + 20.0, $"while the same lane left to advance is far past it (mean x {MeanX(free, 1):F1})");
        bool lanesAlike = true;
        foreach (int lane in new[] { 0, 2 })
            lanesAlike &= free.Snapshot().Units.Where(u => u.Lane == lane).Select(u => (u.Id, u.X, u.Y)).SequenceEqual(held.Snapshot().Units.Where(u => u.Lane == lane).Select(u => (u.Id, u.X, u.Y)));
        H.Check(lanesAlike, "the lanes that got no order are bit for bit what they were without the play");
        H.Check(held.Snapshot().Units.Where(u => u.Lane == 1).All(u => u.Order == FieldOrderKind.Hold), "the snapshot shows the order each body follows");

        // FallBack sends a lane home; Guard draws a lane together
        var fb = await Rig.New(setup, 9); fb.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 8)]); await fb.StepAsync();
        double far = fb.Snapshot().Units.Average(u => u.X);
        fb.ApplyPlays([new OrderPlay(Side.Player, 0, FieldOrderKind.FallBack)]); await fb.StepAsync(); await fb.StepAsync();
        H.Check(far > 40 && fb.Snapshot().Units.Average(u => u.X) < layout.PlayerBaseEdgeX + 2, $"a lane that falls back walks home (mean x {far:F1} then {fb.Snapshot().Units.Average(u => u.X):F1})");
        var gd = await Rig.New(setup, 9); gd.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 20), new OrderPlay(Side.Player, 0, FieldOrderKind.Guard)]);
        double Spread(LaneBattle x) { var u = x.Snapshot().Units; double cx = u.Average(q => q.X), cy = u.Average(q => q.Y); return u.Max(q => Math.Sqrt((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy))); }
        double spread0 = Spread(gd); await gd.StepAsync();
        H.Check(Spread(gd) < spread0 * 0.6 && gd.Snapshot().Units.Average(u => u.X) < layout.PlayerBaseEdgeX + 4, $"a guarding lane closes up where it stands (spread {spread0:F1} then {Spread(gd):F1})");
        var hunt = await Rig.New(setup, 9);
        var hr = hunt.ApplyPlays([new SpawnPlay(Side.Player, 0, 2, 3), new OrderPlay(Side.Player, 0, FieldOrderKind.Hunt)]);
        H.Check(hr.All(r => r.Applied) && hunt.OrderOf(Side.Player, 0) == FieldOrderKind.Hunt, "Hunt is accepted and recorded");
    }

    static bool OnGround(LaneLayout l, float x, float y)
    {
        int c = Math.Clamp((int)(x / l.Cell), 0, l.Rows[0].Length - 1), r = Math.Clamp((int)(y / l.Cell), 0, l.Rows.Count - 1);
        return l.Rows[r][c] != '#';
    }

    // 5. ---- survivors persist ------------------------------------------------------------------------------------------------

    public static async Task Persistence(LaneProbe probe)
    {
        H.Section("survivors persist across pauses");
        var setup = new LaneSetup { Probe = probe };
        // every one of the 24 (kind, lane) groups of a side is readable from the roster (a store reads back at most 8 items, so the roster is one store per side and lane)
        var all = await Rig.New(setup, 21);
        all.ApplyPlays([.. Enumerable.Range(0, 3).SelectMany(l => Enumerable.Range(0, Roster.Size).Select(k => (LanePlay)new SpawnPlay(Side.Player, l, k, 1 + k % 3)))]);
        H.Check(Enumerable.Range(0, 3).All(l => Enumerable.Range(0, Roster.Size).All(k => all.RosterCount(Side.Player, l, k) == 1 + k % 3)) && all.RosterTotal(Side.Player) == 3 * Enumerable.Range(0, Roster.Size).Sum(k => 1 + k % 3),
            "all 24 (kind, lane) groups of a side read back from the Pleroma roster with their counts");
        var b = await Rig.New(setup, 21);
        b.ApplyPlays([.. Rig.Spawns(Side.Player, 3, (0, 8), (3, 6)), .. Rig.Spawns(Side.Enemy, 3, (1, 8), (0, 6))]);
        var snaps = new List<LaneSnapshot> { b.Snapshot() };
        bool rosterOk = RosterAgrees(b);
        for (int i = 0; i < 6; i++) { await b.StepAsync(); snaps.Add(b.Snapshot()); rosterOk &= RosterAgrees(b); }
        bool ok = true, never = true, hpDown = true, still = true;
        for (int i = 1; i < snaps.Count; i++)
        {
            var prev = snaps[i - 1].Units.ToDictionary(u => u.Id);
            foreach (var u in snaps[i].Units)
            {
                if (!prev.TryGetValue(u.Id, out var p)) { never = false; continue; }
                ok &= p.Kind == u.Kind && p.Side == u.Side && p.Lane == u.Lane;
                hpDown &= u.Hp <= p.Hp;
                double d = Math.Sqrt((u.X - p.X) * (u.X - p.X) + (u.Y - p.Y) * (u.Y - p.Y));
                still &= d <= LaneRules.StepOf(Roster.TraitsOf(u.Kind)) * setup.PhaseTicks * 1.001 + 0.01;
            }
        }
        H.Check(never, "no body appears between pauses unless a play spawned it");
        H.Check(ok, "a body keeps its id, kind, side and lane from pause to pause");
        H.Check(hpDown, "hit points never go up");
        H.Check(still, "a body moves no farther in a phase than its pace allows (it is the same body, not a fresh one)");
        H.Check(snaps[1].Units.Count > 0 && snaps[3].Units.Count > 0, "bodies are still standing after three pauses");
        var ids0 = snaps[0].Units.Select(u => u.Id).ToHashSet();
        int survivors = snaps[^1].Units.Count(u => ids0.Contains(u.Id));
        H.Check(survivors > 0 && survivors < snaps[0].Units.Count, $"some of the original bodies survive to the end and some fell ({survivors} of {snaps[0].Units.Count}): the fight is real and so is the persistence");
        H.Check(rosterOk, "at every pause the Pleroma roster equals the bodies on the field, by side, lane and kind");
        // new bodies join the old ones
        var more = b.Status == LaneStatus.Running ? b.ApplyPlays([new SpawnPlay(Side.Player, 0, 2, 4)]) : [];
        if (more.Count > 0)
        {
            var old = snaps[^1].Units.Select(u => u.Id).ToHashSet(); var nowU = b.Snapshot().Units;
            H.Check(nowU.Count(u => !old.Contains(u.Id)) == 4 && nowU.Count(u => old.Contains(u.Id)) == old.Count, "a later spawn adds four bodies with new ids and leaves the survivors alone");
        }
    }

    static bool RosterAgrees(LaneBattle b)
    {
        var u = b.Snapshot().Units;
        foreach (var side in new[] { Side.Player, Side.Enemy })
        {
            if (b.RosterTotal(side) != u.Count(x => x.Side == side)) return false;
            for (int lane = 0; lane < b.Setup.Lanes; lane++)
                for (int kind = 0; kind < Roster.Size; kind++)
                    if (b.RosterCount(side, lane, kind) != u.Count(x => x.Side == side && x.Lane == lane && x.Kind == kind)) return false;   // -1 (unreadable) fails too
        }
        return true;
    }

    // 6. ---- win, draw, cap -----------------------------------------------------------------------------------------------------

    public static async Task WinDrawCap(LaneProbe probe)
    {
        H.Section("win, draw and the hard cap");
        var small = new LaneSetup { Lanes = 1, LaneCols = 24, BaseHp = 3, Probe = probe };
        var pw = await Rig.New(small, 4);
        pw.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 12)]);
        for (int i = 0; i < 12 && pw.Status == LaneStatus.Running; i++) await pw.StepAsync();
        H.Check(pw.Status == LaneStatus.PlayerWon && pw.EnemyBaseHp == 0 && pw.PlayerBaseHp == pw.BaseMaxHp, $"twelve spearmen alone take the enemy base: PlayerWon, enemy base 0, the player's untouched (status {pw.Status}, bases {pw.PlayerBaseHp}/{pw.EnemyBaseHp})");
        H.Check(pw.Snapshot().Status == LaneStatus.PlayerWon, "the snapshot says so too");
        int endTick = pw.Tick; ulong endHash = pw.StateHash();
        var after = await pw.StepAsync();
        var refused = pw.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 3)]);
        H.Check(after.TicksRun == 0 && pw.Tick == endTick && pw.StateHash() == endHash && !refused[0].Applied, "a finished match does not step and refuses plays");
        var last = pw.Replay.Pauses[^1];
        H.Check(last.Tick == endTick, "and the tick it ended on is a pause in the replay");

        var ew = await Rig.New(small, 4);
        ew.ApplyPlays([new SpawnPlay(Side.Enemy, 0, 0, 12)]);
        for (int i = 0; i < 12 && ew.Status == LaneStatus.Running; i++) await ew.StepAsync();
        H.Check(ew.Status == LaneStatus.EnemyWon && ew.PlayerBaseHp == 0 && ew.EnemyBaseHp == ew.BaseMaxHp, $"and the same for the enemy: EnemyWon (status {ew.Status})");

        // the hard cap: nobody fights, the match is a draw at exactly the cap tick, which is not a pause
        var capSetup = new LaneSetup { Lanes = 1, LaneCols = 24, TickCap = 2 * LaneRules.PhaseTicks + 50, Probe = probe };
        var cap = await Rig.New(capSetup, 4);
        cap.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 3), new SpawnPlay(Side.Enemy, 0, 0, 3), new OrderPlay(Side.Player, 0, FieldOrderKind.Hold), new OrderPlay(Side.Enemy, 0, FieldOrderKind.Hold)]);
        int steps = 0;
        while (cap.Status == LaneStatus.Running && steps++ < 6) await cap.StepAsync();
        H.Check(cap.Status == LaneStatus.Draw && cap.Tick == capSetup.TickCap && !cap.AtPause, $"a match still running at the cap is a draw on exactly the cap tick (status {cap.Status}, tick {cap.Tick} of {capSetup.TickCap})");
        H.Check(cap.PlayerBaseHp == cap.BaseMaxHp && cap.EnemyBaseHp == cap.BaseMaxHp, "with both bases standing");
        // both bases at once is a draw, not a win for either
        var (pk, el, ek, ec, pc) = DoubleFall;
        var dbl = await Rig.New(new LaneSetup { Lanes = 3, LaneCols = 24, BaseHp = 1, Probe = probe }, 4);
        dbl.ApplyPlays([new SpawnPlay(Side.Player, pc, pk, 20), new SpawnPlay(Side.Enemy, el, ek, ec)]);
        for (int i = 0; i < 12 && dbl.Status == LaneStatus.Running; i++) await dbl.StepAsync();
        H.Check(dbl.PlayerBaseHp == 0 && dbl.EnemyBaseHp == 0 && dbl.Status == LaneStatus.Draw, $"both bases falling on the same tick is a draw (bases {dbl.PlayerBaseHp}/{dbl.EnemyBaseHp}, status {dbl.Status}; if the sim changed, run `find-draw` and retype Scenarios.DoubleFall)");
    }

    /// <summary>A scenario found by `find-draw`: (player kind, enemy lane, enemy kind, enemy count, player lane). Both bases (one soldier-life each) fall on the same tick.</summary>
    public static (int PlayerKind, int EnemyLane, int EnemyKind, int EnemyCount, int PlayerLane) DoubleFall = (2, 2, 2, 6, 1);

    // 7. ---- the hash sees every difference ---------------------------------------------------------------------------------------

    public static async Task HashSensitivity(LaneProbe probe)
    {
        H.Section("the state hash differs when any single body or base hit point differs");
        var setup = new LaneSetup { Lanes = 2, LaneCols = 24, Probe = probe };
        var b = await Rig.Run(setup, 31, Rig.StandardPlan(2), 2);
        int n = b.BodyCountForTest;
        H.Check(n >= 20, $"a field with enough bodies to test ({n})");
        ulong h = b.StateHash();
        int hpMiss = 0, xMiss = 0, yMiss = 0, restoreMiss = 0;
        for (int i = 0; i < n; i++)
        {
            b.PokeBodyHp(i, -1); if (b.StateHash() == h) hpMiss++; b.PokeBodyHp(i, +1); if (b.StateHash() != h) restoreMiss++;
            b.PokeBodyX(i, 0.01f); if (b.StateHash() == h) xMiss++; b.PokeBodyX(i, -0.01f);
            b.PokeBodyY(i, 0.01f); if (b.StateHash() == h) yMiss++; b.PokeBodyY(i, -0.01f);
        }
        H.Check(hpMiss == 0, $"one hit point off on any one of {n} bodies changes the hash ({hpMiss} missed)");
        H.Check(xMiss == 0 && yMiss == 0, $"a hundredth of a unit on any one body changes the hash ({xMiss} missed in x, {yMiss} in y)");
        H.Check(restoreMiss == 0, "and putting it back restores the hash");
        b.PokeBaseHp(Side.Player, -1); bool p1 = b.StateHash() != h; b.PokeBaseHp(Side.Player, 1);
        b.PokeBaseHp(Side.Enemy, -1); bool p2 = b.StateHash() != h; b.PokeBaseHp(Side.Enemy, 1);
        H.Check(p1 && p2 && b.StateHash() == h, "one point off either base changes the hash");
        // one extra body, one body of another kind
        var x1 = await Rig.New(setup, 31); x1.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 10)]);
        var x2 = await Rig.New(setup, 31); x2.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 11)]);
        var x3 = await Rig.New(setup, 31); x3.ApplyPlays([new SpawnPlay(Side.Player, 0, 0, 9), new SpawnPlay(Side.Player, 0, 1, 1)]);
        H.Check(x1.StateHash() != x2.StateHash() && x1.StateHash() != x3.StateHash(), "one body more, or one body of another kind, changes the hash");
    }

    // 8. ---- the counter ring --------------------------------------------------------------------------------------------------

    static async Task<double> Margin(int a, int b, LaneProbe probe)
    {
        const int N = 15;
        double sum = 0; int cnt = 0;
        foreach (int seed in new[] { 3, 5 })
            foreach (bool aIsPlayer in new[] { true, false })
            {
                var m = await Rig.New(new LaneSetup { Lanes = 1, LaneCols = 24, Probe = probe }, seed);
                m.ApplyPlays([new SpawnPlay(aIsPlayer ? Side.Player : Side.Enemy, 0, a, N), new SpawnPlay(aIsPlayer ? Side.Enemy : Side.Player, 0, b, N)]);
                await m.RunToAsync(3 * LaneRules.PhaseTicks);
                int survA = m.Bodies(aIsPlayer ? Side.Player : Side.Enemy), survB = m.Bodies(aIsPlayer ? Side.Enemy : Side.Player);
                sum += (survA - survB) / (double)N; cnt++;
            }
        return sum / cnt;
    }

    public static async Task CounterRing(LaneProbe probe)
    {
        H.Section("the counter ring in the lane: Plain over Swift over Ranged over Armoured over Plain");
        foreach (var (a, b, name) in new[] { (0, 2, "Plain over Swift"), (2, 3, "Swift over Ranged"), (3, 1, "Ranged over Armoured"), (1, 0, "Armoured over Plain") })
        {
            double with = await Margin(a, b, probe), without = await Margin(a, b, probe | LaneProbe.CountersOff);
            H.Note($"{name}: surviving-share margin {with:F2} with the ring, {without:F2} without");
            // A margin is bounded by 1 (a clean sweep), so a side that already wins without the ring has little room: the ring must close at least a third of the gap to a sweep.
            H.Check(with >= 0.3 && with - without >= (1 - without) / 3, $"{name}: the countering side wins clearly and the ring closes a third of its gap to a clean sweep ({with:F2} against {without:F2} without)");
        }
    }

    // 9. ---- the ground --------------------------------------------------------------------------------------------------------------

    public static async Task Ground()
    {
        H.Section("the ground: lanes, walls, necks, mirror");
        foreach (int lanes in new[] { 1, 2, 3 })
        {
            var ctx = Rig.Ctx();
            var b = await LaneBattle.CreateAsync(new LaneSetup { Lanes = lanes, LaneCols = 40 }, 1, ctx);
            var l = b.Layout;
            H.Check(l.Rows.Count == lanes * 8 + (lanes - 1) && l.LaneCentreY.Count == lanes, $"{lanes} lane(s): {l.Rows.Count} rows, {l.LaneCentreY.Count} lane centres");
            H.Check(l.Rows.All(r => r.SequenceEqual(r.Reverse())), $"{lanes} lane(s): the map is its own mirror image");
            H.Check(l.Rows.All(r => r[..5].All(c => c == '.') && r[^5..].All(c => c == '.')), $"{lanes} lane(s): both base zones are open ground the whole height");
            if (lanes > 1) H.Check(Enumerable.Range(1, lanes - 1).All(k => l.Rows[k * 9 - 1][10..^10].All(c => c == '#')), $"{lanes} lanes: a solid wall row stands between the lanes");
            H.Check(l.Rows.Any(r => r.Contains('#')) && l.Rows.Any(r => r.Contains(',')), $"{lanes} lane(s): there is rock and there is cover");
            H.Check(b.Mesh.TriangleCount > 100, $"{lanes} lane(s): the ground is baked to a navmesh ({b.Mesh.TriangleCount} triangles)");
            // walls stop sight, open ground does not
            var los = ctx.World.CreateLineOfSightService();
            float mid = l.Width / 2f - 6f;
            if (lanes > 1) H.Check(!los.HasLineOfSight(b.Mesh, new Vector2(mid, l.LaneCentreY[0]), new Vector2(mid, l.LaneCentreY[1])), $"{lanes} lanes: the wall between two lanes blocks sight");
            H.Check(los.HasLineOfSight(b.Mesh, new Vector2(30f, l.LaneCentreY[0]), new Vector2(50f, l.LaneCentreY[0])), $"{lanes} lane(s): sight along open lane ground holds");
            // paths stay in the lane and the lane shapes differ
            var single = ctx.World.CreateSingleAgentService();
            var lengths = new List<double>();
            for (int k = 0; k < lanes; k++)
            {
                var from = new Vector2(l.PlayerBaseEdgeX - 1f, l.LaneCentreY[k]); var to = new Vector2(l.EnemyBaseEdgeX + 1f, l.LaneCentreY[k]);
                var path = await single.QueryPathAsync(b.Mesh, from, to);
                H.Check(path.Count >= 2, $"lane {k} of {lanes}: a path exists from the player's mouth to the enemy's");
                if (path.Count < 2) continue;
                double len = 0; for (int i = 1; i < path.Count; i++) len += Vector2.Distance(path[i - 1], path[i]);
                lengths.Add(len - Vector2.Distance(from, to));
                bool inBand = path.All(p => p.Y > k * 18f - 0.01f && p.Y < k * 18f + 16.01f);
                H.Check(inBand, $"lane {k} of {lanes}: the path never leaves its lane's walls");
            }
            if (lanes == 3) H.Check(lengths[0] > lengths[2] + 0.4, $"3 lanes: the neck lane (detour {lengths[0]:F1}) is longer than the double-neck lane (detour {lengths[2]:F1}): terrain matters");
        }
        bool bad = true;
        foreach (var s in new[] { new LaneSetup { Lanes = 0 }, new LaneSetup { Lanes = 4 }, new LaneSetup { LaneCols = 10 }, new LaneSetup { BaseHp = 0 } })
        {
            try { await Rig.New(s, 1); bad = false; } catch (ArgumentOutOfRangeException) { }
        }
        H.Check(bad, "0 or 4 lanes, lanes under 24 cells and a base with no hit points are refused");
    }

    // 10. ---- the replay: plays + seed ---------------------------------------------------------------------------------------------

    public static async Task Replay()
    {
        H.Section("the replay: plays + seed");
        var setup = new LaneSetup { BaseHp = 500 };
        var a = await Rig.Run(setup, 77, Rig.StandardPlan(3), 8);
        var replay = a.Replay;
        var bytes = LaneReplayCodec.Encode(replay);
        H.Check(LaneReplayCodec.TryDecode(bytes, out var back, out var err) && back is not null && LaneReplayCodec.Encode(back).AsSpan().SequenceEqual(bytes), $"the replay round-trips byte for byte ({bytes.Length} bytes) {err}");
        var snaps = new List<LaneSnapshot>();
        var check = await LaneReplayPlayer.PlayAsync(back!, Rig.Ctx(), s => snaps.Add(s));
        H.Check(check.Ok && check.Final.StateHash() == a.StateHash() && check.Final.Tick == a.Tick, $"playing it back re-simulates the same match, hash at every pause equal ({check.Why})");
        H.Check(snaps.Count == replay.Pauses.Count, "one snapshot per pause comes back for a renderer");
        // tampered: a flipped hash bit, a changed play, a cut
        var badHash = replay with { Pauses = replay.Pauses.Select((p, i) => i == 3 ? p with { Hash = p.Hash ^ 1UL } : p).ToList() };
        var r1 = await LaneReplayPlayer.PlayAsync(badHash, Rig.Ctx());
        H.Check(!r1.Ok && r1.FirstBadPause == 3, $"a flipped hash bit is caught at its pause (pause {r1.FirstBadPause})");
        var badPlay = replay with { Batches = replay.Batches.Select((bt, i) => i == 1 ? bt with { Plays = [.. bt.Plays.Skip(1)] } : bt).ToList() };
        var r2 = await LaneReplayPlayer.PlayAsync(badPlay, Rig.Ctx());
        H.Check(!r2.Ok, "a changed play is caught");
        H.Check(!LaneReplayCodec.TryDecode(bytes.AsSpan(0, bytes.Length - 3), out _, out _) && !LaneReplayCodec.TryDecode("XXX"u8, out _, out _), "damaged data is refused, never thrown");
        H.Check(bytes.Length < 600, $"the whole match is under 600 bytes ({bytes.Length})");
    }
}
