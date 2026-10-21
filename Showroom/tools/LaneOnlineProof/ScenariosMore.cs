using System.Text;
using System.Text.Json;
using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using static H;

static partial class Scenarios
{
    static async Task<IReadOnlyList<(int Tick, ulong Hash)>> OfflineReplay(Pair p, LaneSetup setup, int seed)
    {
        var m = await LaneMatch.CreateAsync(new LaneMatchOptions { Seed = seed, Setup = setup, Player = new ScriptedSeat(p.Plans[Side.Player]), Enemy = new ScriptedSeat(p.Plans[Side.Enemy]) }, Rig.Ctx());
        await m.PlayToEndAsync();
        return m.PauseHashes.ToList();
    }

    // 5. ---- a whole match -------------------------------------------------------------------------------------------------------

    public static async Task FullMatch(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("a whole match on two clients: identical hashes at every pause, the same as an offline replay of the same plays");
        var p = new Pair(pa, pb);
        var err = await p.StartAsync();
        Check(err is null, "joined: " + err);
        await p.PlayOutAsync();
        var A = p.A; var B = p.B;
        Check(A.Phase == OnlinePhase.Ended && B.Phase == OnlinePhase.Ended && A.End?.Kind == OnlineEndKind.Played && B.End?.Kind == OnlineEndKind.Played, $"both played the match out ({A.End?.Kind}, {B.End?.Kind}): {A.End?.Text}");
        Check(SameHashes(A, B) && A.Match!.PauseHashes.Count >= 5, $"the two clients hold the same state hash at every one of their {A.Match?.PauseHashes.Count} pauses: A {Hashes(A)} | B {Hashes(B)}");
        Check(A.Match!.Tick == B.Match!.Tick && A.Match.Status == B.Match.Status && A.Match.Battle.StateHash() == B.Match.Battle.StateHash(), "and end on the same tick with the same result and the same final state");
        var offline = await OfflineReplay(p, Rig.Setup, 7);
        Check(offline.SequenceEqual(A.Match.PauseHashes), "a plain offline match fed the same plays gives the very same hashes (the online layer adds nothing to the battle)");
        Check(A.Errors == 0 && B.Errors == 0 && !A.Notices.Any(n => Has(n, "Stopped")) && !B.Notices.Any(n => Has(n, "Stopped")), "no faults and no stop");
        var order = p.Mon.RevealsAfterCommits(p.Code);
        Check(order.Ok, "every reveal of the match went out after both commitments: " + order.Why);
        int pauses = A.Match.PauseHashes.Count - 1;
        int commits = p.Mon.Log.Count(l => l.Topic.Contains("/commit/")), reveals = p.Mon.Log.Count(l => l.Topic.Contains("/reveal/"));
        Check(commits == 2 * pauses && reveals == 2 * pauses, $"two commitments and two reveals for each of the {pauses} pauses (a duplicate is not sent): {commits}, {reveals}");
        Check(p.Mon.Log.Count(l => l.Topic.Contains("/fin/")) == 2, "each client published its final number once");
        Check(p.Mon.Log.Where(l => l.Topic.Contains("/commit/") || l.Topic.Contains("/reveal/") || l.Topic.Contains("/hash/")).All(l => l.Payload.Length < 400), "a commitment, a reveal and a hash are each under 400 bytes (only plays cross the relay, never the battle)");
        Check(p.Mon.Log.Max(l => l.Payload.Length) < 1024, "the largest message of the whole match is under 1 KB: " + p.Mon.Log.Max(l => l.Payload.Length));
        await p.DisposeAsync();

        // a decisive match: one side buys, the other only presses Ready. The result reads right from both seats (the guest sits on the other base).
        foreach (bool hostBuys in new[] { true, false })
        {
            var d = new Pair(pa, pb, o => o.Setup = new LaneSetup { TickCap = 40 * LaneRules.PhaseTicks });
            d.LazyA = !hostBuys; d.LazyB = hostBuys;
            await d.StartAsync();
            await d.PlayOutAsync(600);
            var win = hostBuys ? d.A : d.B; var lose = hostBuys ? d.B : d.A;
            Check(win.End?.Kind == OnlineEndKind.Played && lose.End?.Kind == OnlineEndKind.Played && win.End?.YouWon == true && lose.End?.YouWon == false && Has(win.End.Text, "You win") && Has(lose.End.Text, "You lose") && SameHashes(d.A, d.B),
                $"{(hostBuys ? "the host" : "the guest")} wins the match they played and the other reads 'You lose' (the result is read from each seat): {d.A.End?.Text} | {d.B.End?.Text}");
            await d.DisposeAsync();
        }
    }

    // 6. ---- the two games disagree ----------------------------------------------------------------------------------------------

    public static async Task HashMismatch(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("a hash that differs stops the match, with a clear message, and nothing runs on");
        // the very start: one client builds a battle whose first hash differs
        var s = new Pair(pa, pb | LaneOnlineProbe.LyingHash);
        var err = await s.StartAsync();
        await s.Until(() => s.A.Phase == OnlinePhase.Ended || s.A.Phase == OnlinePhase.Playing, 250, 40);
        await s.Step(250);
        Check(s.A.Phase == OnlinePhase.Ended && s.B.Phase == OnlinePhase.Ended && s.A.End?.Kind == OnlineEndKind.OutOfSync && s.B.End?.Kind == OnlineEndKind.OutOfSync, $"two games that do not agree from the start end at the start: {s.A.Phase}/{s.A.End?.Kind}, {s.B.Phase}/{s.B.End?.Kind}");
        Check(Has(s.A.End?.Text, "no longer agree") && Has(s.A.End?.Text, "stopped") && s.A.Match is { Tick: 0, Stage: MatchStage.Pause }, "the message says the games disagree and that the match was stopped; no tick was ever run");
        await s.DisposeAsync();

        // in the middle: one client leaves the other's plays out of its battle
        var d = new Pair(pa, pb | LaneOnlineProbe.DropsRemotePlays);
        await d.StartAsync();
        await d.PlayUntil(() => d.A.Phase == OnlinePhase.Ended || d.B.Phase == OnlinePhase.Ended || (d.A.Match?.Phase ?? 0) >= 4 && (d.B.Match?.Phase ?? 0) >= 4, 60);
        d.AutoA = d.AutoB = false;
        await d.Step(250);
        Check(d.A.End?.Kind == OnlineEndKind.OutOfSync && d.B.End?.Kind == OnlineEndKind.OutOfSync, $"a battle that drifts is caught at the next pause and both stop: {d.A.End?.Kind}, {d.B.End?.Kind}");
        Check(d.A.Match!.Phase == 1 && d.A.Match.Stage == MatchStage.Pause && d.B.Match!.Stage == MatchStage.Pause, $"neither client ran on: both stand at the pause where they stopped (phase {d.A.Match.Phase})");
        Check(Has(d.A.End?.Text, "no longer agree") && d.A.Notices.Any(n => Has(n, "no longer agree")), "and the player is told in words, not left to guess");
        await d.DisposeAsync();
    }

    // 7. ---- duplicates, out of order, forged repeats ------------------------------------------------------------------------------

    public static async Task Messages(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("duplicate, out-of-order and replayed messages change nothing");
        // every message sent twice
        var d = new Pair(pa, pb);
        d.FactoryA.Tune = r => r.Dup = true; d.FactoryB.Tune = r => r.Dup = true;
        await d.StartAsync();
        await d.PlayUntil(() => d.A.Phase == OnlinePhase.Ended || (d.A.Match?.Phase ?? 0) >= 3 && d.A.PauseOpen && d.B.PauseOpen, 200);
        Check(d.A.Phase == OnlinePhase.Playing && d.B.Phase == OnlinePhase.Playing && SameHashes(d.A, d.B) && d.A.Match!.PauseHashes.Count >= 4 && d.A.Errors == 0, $"with every message sent twice the match goes on as before and the hashes agree ({d.A.Match?.PauseHashes.Count} pauses)");
        await d.DisposeAsync();

        // a commitment that arrives after its own reveal (the guest's commitment is held back until its reveal is out)
        var o = new Pair(pa, pb);
        o.FactoryB.Tune = r => r.Hold = t => t.Contains("/commit/");
        await o.StartAsync();
        await o.PlayUntil(() => o.A.Phase == OnlinePhase.Ended || (o.A.Match?.Phase ?? 0) >= 3 && o.A.PauseOpen && o.B.PauseOpen, 200);
        int ci = o.Mon.Log.First(l => l.Topic.EndsWith("/commit/0/guest")).Index, ri = o.Mon.Log.First(l => l.Topic.EndsWith("/reveal/0/guest")).Index;
        Check(ri < ci, "the guest's reveal really reached the wire before its commitment");
        Check(o.A.Phase == OnlinePhase.Playing && o.B.Phase == OnlinePhase.Playing && SameHashes(o.A, o.B) && o.A.Match!.PauseHashes.Count >= 4 && o.A.End is null, "and a reveal that is early just waits for its commitment: the match goes on and the hashes agree");
        await o.DisposeAsync();

        // forged repeats: after the real commitment and reveal of the guest are on the wire, someone sends others for the same pause, an old game, a far game
        var f = new Pair(pa, pb);
        await f.StartAsync();
        var spy = f.Broker.NewClient("spy"); await spy.ConnectAsync();
        var t = new LaneTopics(f.Code);
        await f.Autopilot(f.A); await f.Autopilot(f.B);   // both commit; the guest has revealed; the host has not pumped yet
        var junk = new byte[32]; new Random(9).NextBytes(junk);
        await spy.PublishAsync(t.Commit(0, 0, LaneRole.Guest), LaneWire.Commit(new LaneCommitMsg(junk)), true, 1);
        await spy.PublishAsync(t.Reveal(0, 0, LaneRole.Guest), LaneWire.Reveal(new LaneRevealMsg("0.0,0.0,0.0;", new byte[16], false)), true, 1);
        await spy.PublishAsync(t.Hash(0, 0, LaneRole.Guest), LaneWire.Hash(new LaneHashMsg("deadbeefdeadbeef")), true, 1);
        await spy.PublishAsync(t.Ready(0, LaneRole.Guest), LaneWire.Ready(new LaneReadyMsg("deadbeefdeadbeef", 1)), true, 1);
        await spy.PublishAsync(t.Reveal(5, 0, LaneRole.Host), LaneWire.Reveal(new LaneRevealMsg("0.0;", new byte[16], true)), true, 1);
        await spy.PublishAsync(t.Hash(7, 3, LaneRole.Host), LaneWire.Hash(new LaneHashMsg("deadbeefdeadbeef")), true, 1);
        await spy.PublishAsync(t.Commit(0, 999, LaneRole.Host), LaneWire.Commit(new LaneCommitMsg(junk)), true, 1);
        await f.Step(100);
        Check(f.A.Match!.Stage == MatchStage.Run && f.B.Match!.Stage == MatchStage.Run && f.A.End is null && f.B.End is null, $"later copies of the guest's commitment, reveal, hash and ready for the same pause, a reveal of another game and a hash far ahead are ignored: the pause resolved as it should ({f.A.End?.Kind})");
        f.AutoA = f.AutoB = true;
        await f.PlayUntil(() => f.A.Phase == OnlinePhase.Ended || (f.A.Match?.Phase ?? 0) >= 2 && f.A.PauseOpen && f.B.PauseOpen, 100);
        Check(f.A.Phase == OnlinePhase.Playing && f.B.Phase == OnlinePhase.Playing && SameHashes(f.A, f.B) && f.A.Match!.PauseHashes.Count >= 3, "and the hashes still agree at the next pauses");

        // a replay of the whole first pause (the real messages again, as a reconnecting client would send them)
        int before = f.Mon.Log.Count;
        foreach (var l in f.Mon.Log.Take(before).Where(l => l.Topic.Contains("/g0/") && (l.Topic.Contains("/commit/0/") || l.Topic.Contains("/reveal/0/") || l.Topic.Contains("/hash/0/"))).ToList())
            await spy.PublishAsync(l.Topic, l.Payload, true, 1);
        await f.Step(100);
        Check(f.A.End is null && f.B.End is null && f.A.Errors == 0 && f.B.Errors == 0 && SameHashes(f.A, f.B), "replaying the first pause's messages as they were changes nothing");
        await f.DisposeAsync();
    }

    // 8. ---- a rematch -------------------------------------------------------------------------------------------------------------

    public static async Task Rematch(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("rematch: both ask, the sides swap, the old game is cleared, an old game's messages are not read");
        int seeds = 6;
        var p = new Pair(pa, pb, o => o.SeedSource = () => ++seeds);
        await p.StartAsync();
        await p.PlayOutAsync();
        var A = p.A; var B = p.B;
        Check(A.End?.Kind == OnlineEndKind.Played && B.End?.Kind == OnlineEndKind.Played && A.CanRematch && B.CanRematch, "after a played-out match a rematch can be asked for");
        int seed0 = A.Seed;
        await A.RematchAsync(); await p.Step(250);
        Check(A.MyRematch && B.OpponentRematch && !B.MyRematch && A.Generation == 0 && A.Phase == OnlinePhase.Ended && B.Phase == OnlinePhase.Ended, "one side asking is only an offer: the other sees it, nothing starts");
        await B.RematchAsync();
        await p.Until(() => A.Phase == OnlinePhase.Playing && B.Phase == OnlinePhase.Playing, 500, 80);
        Check(A.Phase == OnlinePhase.Playing && B.Phase == OnlinePhase.Playing && A.Generation == 1 && B.Generation == 1, $"both asking starts the next game in the same room ({A.Phase}, {B.Phase})");
        Check(A.LocalSide == Side.Enemy && B.LocalSide == Side.Player && A.Match!.LocalSide == Side.Enemy && B.Match!.LocalSide == Side.Player, "the two swap sides for the rematch");
        Check(A.Seed != seed0 && A.Seed == B.Seed && A.Match!.Tick == 0 && A.Match.PauseHashes.Count >= 1 && A.Match.PauseHashes[0].Hash == B.Match!.PauseHashes[0].Hash && A.End is null, "with a new match number, built the same on both, and the old result gone");
        Check(!p.Broker.RetainedTopics.Any(tp => tp.Contains("/g0/")), "the first game's messages are cleared from the relay: " + string.Join(", ", p.Broker.RetainedTopics.Where(tp => tp.Contains("/g0/")).Take(3)));

        // an old game's messages, sent now that game 1 is on and before its pause is answered
        var spy = p.Broker.NewClient("spy"); await spy.ConnectAsync();
        var t = new LaneTopics(p.Code);
        var junk = new byte[32]; new Random(5).NextBytes(junk);
        await spy.PublishAsync(t.Commit(0, 0, LaneRole.Host), LaneWire.Commit(new LaneCommitMsg(junk)), true, 1);
        await spy.PublishAsync(t.Hash(0, 0, LaneRole.Host), LaneWire.Hash(new LaneHashMsg("deadbeefdeadbeef")), true, 1);
        await spy.PublishAsync(t.Reveal(0, 0, LaneRole.Host), LaneWire.Reveal(new LaneRevealMsg("0.0;", new byte[16], false)), true, 1);
        await p.Step(100);
        p.AutoA = p.AutoB = true;
        await p.PlayUntil(() => A.Phase == OnlinePhase.Ended || (A.Match?.Phase ?? 0) >= 2 && A.PauseOpen && B.PauseOpen, 100);
        Check(A.Phase == OnlinePhase.Playing && B.Phase == OnlinePhase.Playing && A.End is null && B.End is null && SameHashes(A, B) && A.Match!.PauseHashes.Count >= 3, $"messages of game 0 do nothing to game 1: it plays on with equal hashes ({A.End?.Kind}, {B.End?.Kind})");
        await p.DisposeAsync();
    }

    // 9. ---- every topic, every message ---------------------------------------------------------------------------------------------

    public static async Task Topics(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("topic discipline: everything under plenum/v1/doctrine-lane/, nothing left behind");
        var p = new Pair(pa, pb);
        await p.StartAsync();
        p.AutoA = p.AutoB = true;
        await p.PlayUntil(() => (p.A.Match?.Phase ?? 0) >= 2, 100);
        await p.B.LeaveAsync(); await p.Step(250);
        await p.A.LeaveAsync();
        var topics = p.Mon.Topics.ToList();
        Check(topics.Count > 10 && !p.Mon.TopicsOutsideRoot.Any(), "every topic any client published is under plenum/v1/doctrine-lane/: " + string.Join(", ", p.Mon.TopicsOutsideRoot.Take(3)));
        Check(topics.All(tp => tp.StartsWith(LaneOnlineProtocol.RoomRoot + p.Code + "/") || tp == LaneTopics.Lobby(p.Code)), "and each is this room's, or this room's one lobby entry");
        Check(topics.All(tp => !tp.StartsWith("pzc/") && !tp.StartsWith("plenum/v1/doctrine/") && !tp.StartsWith("plenum/v1/lobby/") && !tp.StartsWith("plenum/v1/priv/")), "none is in the Swarm's namespace, the Plenum card game's or the old duel's");
        Check(topics.Any(tp => tp.Contains("/presence/")) && topics.Any(tp => tp.Contains("/hash/")) && topics.Any(tp => tp.Contains("/commit/")) && topics.Any(tp => tp.Contains("/reveal/")) && topics.Any(tp => tp.StartsWith(LaneOnlineProtocol.LobbyRoot)), "the run really used presence, hash, commit, reveal and the lobby entry (the check above is not empty)");
        Check(p.Broker.RetainedTopics.Count == 0, "after both left nothing is retained on the relay: " + string.Join(", ", p.Broker.RetainedTopics.Take(4)));
    }

    // 10. ---- the mirrored view ------------------------------------------------------------------------------------------------------

    public static async Task Mirror()
    {
        Section("perspective: each player sees their own base on their own side; the simulation is the same");
        LaneMatchOptions O(Side local) => new() { Seed = 5, Setup = new LaneSetup { TickCap = 40 * LaneRules.PhaseTicks }, Player = new LaneBot(), Enemy = new ScriptedSeat(new Dictionary<int, LanePlan> { [0] = new LanePlan([], [new LaneOrderChoice(1, FieldOrderKind.Hold)]) }), LocalSide = local };   // the enemy plays no soldiers (its base is hit) and holds lane 2
        var a = await LaneMatch.CreateAsync(O(Side.Player), Rig.Ctx()); var b = await LaneMatch.CreateAsync(O(Side.Enemy), Rig.Ctx());
        await a.PlayToEndAsync(12); await b.PlayToEndAsync(12);
        Check(a.PauseHashes.SequenceEqual(b.PauseHashes) && a.Tick == b.Tick, "the same match from either seat: identical simulation, only the view differs");
        float W = a.Battle.Layout.Width;
        var (ha, ba) = LanePack.Unpack(LanePack.Pack(a)); var (hb, bb) = LanePack.Unpack(LanePack.Pack(b));
        Check(ha[3] == hb[4] && ha[4] == hb[3] && (ha[3] < 1f || ha[4] < 1f), $"the base fractions swap: yours first for each seat ({ha[3]:0.00}/{ha[4]:0.00} and {hb[3]:0.00}/{hb[4]:0.00})");
        var mapB = bb.ToDictionary(x => x.Id);
        bool same = ba.Count == bb.Count && ba.Count > 10 && ba.All(x => mapB.TryGetValue(x.Id, out var y) && y.Side == (x.Side == Side.Player ? Side.Enemy : Side.Player) && Math.Abs(y.X - (W - x.X)) < 1e-3 && y.Y == x.Y && Math.Abs(y.FacingX + x.FacingX) < 1e-6 && y.FacingY == x.FacingY
            && y.Lane == x.Lane && y.Kind == x.Kind && y.HpFrac == x.HpFrac && y.Fighting == x.Fighting);
        Check(same, $"every body is where the mirror says: side swapped (0 is always you), x turned about the middle, facing turned, lane, kind and health the same ({ba.Count} bodies)");

        using var la = JsonDocument.Parse(LanePayload.Layout(a)); using var lb = JsonDocument.Parse(LanePayload.Layout(b));
        var ra = la.RootElement; var rb = lb.RootElement;
        var rowsA = ra.GetProperty("rows").EnumerateArray().Select(e => e.GetString()!).ToList(); var rowsB = rb.GetProperty("rows").EnumerateArray().Select(e => e.GetString()!).ToList();
        Check(!ra.GetProperty("mirror").GetBoolean() && rb.GetProperty("mirror").GetBoolean() && rowsA.Zip(rowsB).All(z => new string(z.First.Reverse().ToArray()) == z.Second), "the ground is turned for the guest: the rows read the other way round");
        Check(Math.Abs(rb.GetProperty("playerEdge").GetDouble() - (W - ra.GetProperty("enemyEdge").GetDouble())) < 1e-3 && Math.Abs(rb.GetProperty("enemyEdge").GetDouble() - (W - ra.GetProperty("playerEdge").GetDouble())) < 1e-3
              && ra.GetProperty("laneCentreY").ToString() == rb.GetProperty("laneCentreY").ToString(), "the two base edges trade places and the lanes keep their numbers and places");
        using var ia = JsonDocument.Parse(LanePayload.Info(a, -1)); using var ib = JsonDocument.Parse(LanePayload.Info(b, -1));
        var oa = ia.RootElement.GetProperty("orders"); var ob = ib.RootElement.GetProperty("orders");
        Check(oa[0].ToString() == ob[1].ToString() && oa[1].ToString() == ob[0].ToString() && oa[0].ToString() != oa[1].ToString(), "the orders list starts with yours for each seat");
        Check(LaneWords.Outcome(LaneStatus.PlayerWon, Side.Player).StartsWith("You win") && LaneWords.Outcome(LaneStatus.PlayerWon, Side.Enemy).StartsWith("You lose") && LaneWords.Outcome(LaneStatus.EnemyWon, Side.Enemy).StartsWith("You win")
              && LaneWords.Outcome(LaneStatus.Draw, Side.Enemy).StartsWith("A draw") && LaneWords.Outcome(LaneStatus.Running, Side.Enemy) == "", "the result is worded for the seat that reads it");
    }
}
