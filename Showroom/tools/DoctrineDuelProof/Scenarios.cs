using System.Text;
using Doctrine;
using ShowroomDoctrine.Duel;
using ShowroomPlenum;
using static H;

static class Scenarios
{
    static string Hex(ulong v) => v.ToString("x");

    public static void Wire()
    {
        Section("wire");
        Check(DuelProtocol.Normalize(" ab12cd ") == "AB12CD" && DuelProtocol.Normalize("ab/cd") is null && DuelProtocol.Normalize("a#bcd") is null && DuelProtocol.Normalize("+++++") is null && DuelProtocol.Normalize("abc") is null, "room codes are normalised and hostile ones refused");
        var t = new DuelTopics("TEST01");
        Check(t.TryParse(t.Commit(2, Seat.B), out var p) && p.Kind == DuelKind.Commit && p.Round == 2 && p.Seat == Seat.B, "a commit topic reads back");
        Check(t.TryParse(t.Chunk(1, 3), out p) && p.Kind == DuelKind.Chunk && p.Round == 1 && p.Index == 3, "a chunk topic reads back");
        Check(!t.TryParse("pzc/anything", out _) && !t.TryParse(DuelProtocol.Root + "OTHER1/room", out _) && !t.TryParse(t.Root + "commit/x/A", out _) && !t.TryParse(t.Root + "commit/1/C", out _), "pzc/, another room and malformed tails are refused");
        Check(t.Filter == "plenum/v1/doctrine/TEST01/#", "the one filter is under the room's root");
        var big = new string('x', 5000);
        var cut = DuelWire.Cut(big, 1024);
        Check(cut.Count == 5 && string.Concat(cut) == big && DuelWire.Cut("abc", 1024).Count == 1, "a packed result is cut and joined back");
        Check(DuelWire.ReadCommit(Encoding.UTF8.GetBytes("{\"v\":1,\"c\":\"AAAA\"}")) is null && DuelWire.ReadRoom(Encoding.UTF8.GetBytes("garbage")) is null && DuelWire.ReadReveal(Encoding.UTF8.GetBytes("{}")) is null, "damaged messages read as null, never throw");
        var bomb = DuelWire.Gzip(new byte[DuelWire.MaxInflated + 1000]);
        Check(DuelWire.Gunzip(bomb) is null, "an inflate bomb is refused (" + bomb.Length + " bytes would expand past the ceiling)");
    }

    // ---- a full real duel -----------------------------------------------------------------------------------------------------
    public static async Task RealDuel()
    {
        Section("a full 3-round duel on the real game (Pleroma army, Tracer battle), two in-process clients");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rig = await Rig.Make(DuelGames.Real, seed: 11);
        // the same seed gives the same offers on both seats, and they equal a fresh client's
        var ua = rig.A.LocalSeat!.UnitOffer; var ub = rig.B.LocalSeat!.UnitOffer;
        var fresh = DuelGames.Real(3).NewDuelSeat(rig.A.Seed, Seat.B).UnitOffer;
        Check(rig.A.Seed == rig.B.Seed && ua.Packs.SequenceEqual(ub.Packs) && ua.Budget == ub.Budget && ub.Packs.SequenceEqual(fresh.Packs), $"same seed, same offers on both seats and on a fresh client ({ua.Packs.Count} packs)");
        var seeds = new HashSet<string>();
        foreach (int s in new[] { 1, 2, 3, 4, 5, 6 }) seeds.Add(string.Join(";", DuelGames.Real(3).NewDuelSeat(s, Seat.A).UnitOffer.Packs.Select(x => x.Kind + "x" + x.Count)));
        Check(seeds.Count > 1, "offers do depend on the seed (null: they would all be equal), " + seeds.Count + " distinct in 6");
        await rig.PlayAll();
        Check(rig.A.Phase == DuelPhase.Ended && rig.B.Phase == DuelPhase.Ended, "both seats end after the last Continue");
        rig.VerifyAgree("real duel");
        rig.VerifyWire("real duel");
        Note($"largest result of the duel: {rig.A.MaxPackedChars} base64 chars (chunk limit {24000}); final: {rig.A.End?.Text} / {rig.B.End?.Text}; {sw.Elapsed.TotalSeconds:F1} s");
        await rig.A.LeaveAsync(); await rig.B.LeaveAsync();
        Check(!rig.Broker.RetainedTopics.Any(x => x.StartsWith(rig.Root)), "after both leave nothing of the room is retained on the relay");
    }

    public static async Task Chunked()
    {
        Section("a result over the chunk limit travels in pieces");
        var rig = await Rig.Make(tuneA: o => o.MaxChunkChars = 100);
        await rig.PlayAll();
        Check(rig.A.LastChunks > 1, $"the result went in {rig.A.LastChunks} pieces");
        Check(rig.Mon.Log.Any(l => l.Topic.Contains("/result/0/1")), "chunk topics were used");
        rig.VerifyAgree("chunked"); rig.VerifyWire("chunked");
    }

    public static async Task Timeout()
    {
        Section("timeout: auto-seal on time, host seals for an absent seat");
        var rig = await Rig.Make();
        long deadline = rig.Open + 45_000;
        await rig.Seal(rig.A);
        rig.Clock.Advance(30_000); await rig.TickSettle();
        Check(rig.B.Phase == DuelPhase.Sealing && rig.AutoCalls.Count == 0, "before the deadline nothing is sealed for B");
        Check(rig.B.RemainingMs == 15_000 && rig.A.RemainingMs == 15_000, $"the countdown reads 15 s (B {rig.B.RemainingMs} ms)");
        rig.Clock.Advance(14_999); await rig.TickSettle();
        Check(rig.B.Phase == DuelPhase.Sealing && rig.AutoCalls.Count == 0, "one millisecond early: still nothing");
        rig.Clock.Advance(1); await rig.TickSettle();
        Check(rig.AutoCalls.Count == 1 && rig.AutoCalls[0] == ("B", false), "at the deadline B is sealed by the auto picker");
        var cb = rig.Mon.Log.FirstOrDefault(l => l.Topic.EndsWith("/commit/0/B"));
        Check(cb.Topic is not null && cb.Unix == deadline && Encoding.UTF8.GetString(cb.Payload).Contains("\"auto\":true"), "B's auto commitment went out exactly at the deadline, flagged auto");
        Check(rig.A.Phase == DuelPhase.Result && rig.B.Phase == DuelPhase.Result, "the round closed on both seats");
        Check(rig.B.Outcome!.YouAutoSealed && rig.A.Outcome!.ThemAutoSealed && rig.A.IdleThem == 1, "both seats see who the timer sealed for (idle streak 1)");
        rig.VerifyWire("timeout");

        // an absent seat: the host seals for it after the grace, three times, then it is idle
        var r2 = await Rig.Make();
        var bl = r2.FB.Made[0].Loop;
        r2.Broker.Drop(bl); await r2.Settle();
        Check(r2.A.Opponent == OpponentState.Offline, "B's last will tells A that B is offline");
        for (int r = 0; r < 3; r++)
        {
            long dl = r2.Clock.UnixMs + 45_000;
            await r2.Seal(r2.A); await r2.Settle();
            r2.Clock.UnixMs = dl; r2.Clock.NowMs += 45_000; r2.Clock.Advance(3_999); await r2.TickSettle();
            Check(r2.A.Phase == DuelPhase.Sealed, $"round {r + 1}: grace not over, the host has not sealed for B");
            r2.Clock.Advance(1); await r2.TickSettle();
            Check(r2.A.Phase == DuelPhase.Result && r2.AutoCalls.Count(c => c.ForOpponent) == r + 1, $"round {r + 1}: after the grace the host sealed for B");
            Check(r2.A.Outcome!.Notes.Any(n => n.Contains("host sealed")) && r2.A.Outcome.ThemAutoSealed, $"round {r + 1}: the host says so in plain words");
            await r2.A.ContinueAsync();
            if (r < 2) { r2.Clock.Advance(60_000); await r2.TickSettle(); }
        }
        Check(r2.A.IdleThem == 3, "three auto-seals in a row mark B idle");
        // B comes back: everything is retained, it catches up three rounds
        await bl.ReconnectAsync(); await r2.Settle();
        Check(r2.B.History.Count == 3 && r2.B.Phase == DuelPhase.Result && r2.B.Errors == 0, $"B resynced from retained state and caught up ({r2.B.History.Count} rounds)");
        await r2.B.ContinueAsync(); await r2.A.ContinueAsync(); await r2.Settle();
        r2.VerifyAgree("catch-up");
        Check(r2.A.Opponent == OpponentState.Idle || r2.A.Phase == DuelPhase.Ended, "A sees the idle streak (3) in the result");
        Check(r2.B.History.All(h => h.YouAutoSealed), "B's own view says the host sealed for it each round");
    }

    public static async Task Tamper()
    {
        Section("a tampered reveal is refused and forfeited");
        (string Name, Func<int, byte[], byte[], (byte[], byte[])> Tweak)[] variants =
        [
            ("changed pick", (r, c, n) =>
            {
                DuelPicks.TryParse(c, out var p, out _);
                return (new DuelPicks(p!.Units, DoctrineSet.Of(p.Doctrine.S0 == 0 ? 5 : 0, 0, 0, 0), p.Formation, p.Rerolls).Canonical(), n);
            }),
            ("changed nonce", (r, c, n) => { var m = (byte[])n.Clone(); m[0] ^= 1; return (c, m); }),
            ("unreadable picks", (r, c, n) => (Encoding.ASCII.GetBytes("junkjunk"), n)),
        ];
        foreach (var (name, tweak) in variants)
        {
            var rig = await Rig.Make(tuneB: o => o.RevealTamper = tweak);
            await rig.SealBoth();
            Check(rig.A.Phase == DuelPhase.Result && rig.B.Phase == DuelPhase.Result, $"{name}: the round still closes");
            Check(rig.A.Outcome!.ThemForfeited && rig.B.Outcome!.YouForfeited && !rig.A.Outcome.YouForfeited, $"{name}: seat B forfeits, seat A does not");
            Check(rig.A.Outcome.Notes.Any(n => n.Contains("levy only")) && rig.B.Outcome.Notes.Count > 0, $"{name}: the reason is said on both seats: " + rig.A.Outcome.Notes.FirstOrDefault());
            Check(rig.A.Outcome.You.Opponent.Doctrine.IsEmpty, $"{name}: the forfeited seat fought with an empty doctrine");
            Check(rig.A.Outcome.Result.Digest() == rig.B.Outcome.Result.Digest(), $"{name}: both seats still hold the same result");
            Check(!rig.B.Outcome.Check.Ok, $"{name}: B's own check shows the host did not fight the army it built");
            rig.VerifyWire($"tamper {name}");
        }
    }

    public static async Task Duplicates()
    {
        Section("duplicate, redelivered, malformed and out-of-order messages");
        var clean = await Rig.Make(seed: 9); await clean.PlayAll();
        var dup = await Rig.Make(seed: 9, wrapA: w => w.Dup = true, wrapB: w => w.Dup = true);
        // junk: malformed on every kind, a poisoned result header for a future round, another room's topic, a stale message
        var rogue = dup.Broker.NewClient("junk"); await rogue.ConnectAsync();
        async Task J(string suffix, string body) => await rogue.PublishAsync(dup.Root + suffix, Encoding.UTF8.GetBytes(body), false, 1);
        await J("commit/1/A", "xx"); await J("reveal/1/B", "{}"); await J("room", "{\"v\":1}"); await J("next/1/A", "[]"); await J("zzz", "{\"v\":1}");
        await J("result/1", "{\"v\":1,\"round\":1,\"n\":1,\"dig\":\"123\",\"d\":\"!!!\"}");
        await rogue.PublishAsync(dup.Root + "../x", Encoding.UTF8.GetBytes("{}"), false, 1);
        for (int r = 0; r < 3; r++)
        {
            await dup.SealBoth();
            await dup.Redeliver();
            await dup.ContinueBoth();
            await dup.Redeliver();
        }
        await dup.Redeliver();
        dup.VerifyAgree("duplicates");
        Check(Rig.DigestLine(dup.A) == Rig.DigestLine(clean.A) && dup.A.End?.Score == clean.A.End?.Score, "the duplicated run has exactly the recordings and score of the clean run");
        Check(dup.A.History.Count == 3 && dup.B.History.Count == 3, "each round was applied once, though every message arrived twice and again from retained state");
        Check(dup.Mon.Count(dup.Root, "result") > 3, "(the duplicates really were on the wire)");

        // out of order: B's commitment is held back until after its reveal, so A sees the reveal first
        var ooo = await Rig.Make(seed: 9, wrapB: w => w.HoldCommit = true);
        await ooo.PlayAll();
        var cb = ooo.Mon.Log.First(l => l.Topic.EndsWith("/commit/0/B")).Index; var rb = ooo.Mon.Log.First(l => l.Topic.EndsWith("/reveal/0/B")).Index;
        Check(rb < cb, "(B's reveal really reached the wire before its commitment)");
        ooo.VerifyAgree("out of order");
        Check(Rig.DigestLine(ooo.A) == Rig.DigestLine(clean.A), "an early reveal waits for its commitment and the recordings are unchanged");
    }

    public static async Task Dropped()
    {
        Section("a dropped connection and a resync from retained state");
        // B drops with a half-built round, the host seals for it, B returns and replays what the host did in its name
        var rig = await Rig.Make();
        var seatBefore = rig.B.LocalSeat!;
        var offer = seatBefore.UnitOffer;
        await seatBefore.DraftAsync(offer.Packs.Where(p => p.Affordable).Take(1).Select(UnitOffer.Pick).ToList());
        rig.Broker.Drop(rig.FB.Made[0].Loop); await rig.Settle();
        Check(!rig.B.Online && rig.A.Opponent == OpponentState.Offline, "the drop is noticed on both seats");
        await rig.Seal(rig.A); await rig.Settle();
        rig.Clock.Advance(45_000 + 4_000); await rig.TickSettle();
        Check(rig.A.Phase == DuelPhase.Result, "the host went on without B");
        await rig.FB.Made[0].Loop.ReconnectAsync(); await rig.Settle();
        Check(rig.B.Online && rig.B.Phase == DuelPhase.Result && rig.B.History.Count == 1, "B is back, resynced from retained commitments, reveals and result");
        Note($"seat replaced {!ReferenceEquals(rig.B.LocalSeat, seatBefore)}, check {rig.B.History[0].Check}, errors {rig.B.Errors}, notices: {string.Join(" | ", rig.B.Notices)}");
        Check(!ReferenceEquals(rig.B.LocalSeat, seatBefore) && rig.B.History[0].Check.Ok, "its half-drafted seat was rebuilt from the host's picks and its digest matches");
        Check(rig.A.Opponent == OpponentState.Choosing || rig.A.Opponent == OpponentState.Sealed, "A sees B online again");
        Check(rig.A.History[0].Result.Digest() == rig.B.History[0].Result.Digest(), "the same recording on both");

        // A drops while B has not yet sealed: A misses B's commitment live and gets it from retained state
        var r2 = await Rig.Make();
        var al = r2.FA.Made[0].Loop;
        r2.Broker.Drop(al); await r2.Settle();
        Check(r2.B.Opponent == OpponentState.Offline && !r2.A.Online, "B sees the host go offline");
        await r2.Seal(r2.B); await r2.Settle();
        await al.ReconnectAsync(); await r2.Settle();
        Check(r2.B.Opponent != OpponentState.Offline && r2.A.Online, "A is back and republished its presence");
        await r2.Seal(r2.A); await r2.Settle();
        Check(r2.A.Phase == DuelPhase.Result && r2.B.Phase == DuelPhase.Result, "the round completes after the host's resync");
        await r2.ContinueBoth(); await r2.PlayAllFrom(1);
        r2.VerifyAgree("host resync"); r2.VerifyWire("host resync", revealRule: true);
    }

    public static async Task HostLeaves()
    {
        Section("the authority leaving ends the match plainly");
        var rig = await Rig.Make();
        await rig.Seal(rig.B);
        await rig.A.LeaveAsync(); await rig.Settle();
        Check(rig.B.Phase == DuelPhase.Ended && rig.B.End?.Kind == DuelEndKind.HostLeft && rig.B.End.Text.Contains("host"), "B ends with a plain message: " + rig.B.End?.Text);
        Check(rig.A.Phase == DuelPhase.Idle, "A is out");
        await rig.B.LeaveAsync();
        Check(!rig.Broker.RetainedTopics.Any(x => x.StartsWith(rig.Root)), "nothing of the room stays on the relay");

        var r2 = await Rig.Make();
        r2.Broker.Drop(r2.FA.Made[0].Loop); await r2.Settle();
        r2.Clock.Advance(29_999); await r2.TickSettle();
        Check(r2.B.Phase == DuelPhase.Sealing, "a host that drops is waited for (29.999 s)");
        r2.Clock.Advance(1); await r2.TickSettle();
        Check(r2.B.Phase == DuelPhase.Ended && r2.B.End?.Kind == DuelEndKind.HostLeft, "after the grace B ends it: " + r2.B.End?.Text);

        var r3 = await Rig.Make();
        await r3.B.LeaveAsync(); await r3.Settle();
        Check(r3.A.Phase == DuelPhase.Ended && r3.A.End?.Kind == DuelEndKind.GuestLeft, "the guest leaving ends it for the host: " + r3.A.End?.Text);
    }

    public static async Task Rematch()
    {
        Section("a rematch and the refusals");
        var rig = await Rig.Make(tuneA: o => { int n = 0; o.CodeSource = () => n++ == 0 ? "TEST01" : "TEST02"; o.SeedSource = () => 7 + n; });
        await rig.PlayAll();
        Check(rig.A.End?.Kind == DuelEndKind.Final, "the duel is over");
        await rig.A.RematchAsync(); await rig.Settle();
        Check(rig.A.Phase == DuelPhase.Ended, "one seat asking is not enough");
        await rig.B.RematchAsync();
        for (int i = 0; i < 20 && !(rig.A.Phase == DuelPhase.Lobby && rig.B.Phase == DuelPhase.Lobby); i++) { await Task.Delay(30); await rig.Settle(); }
        Check(rig.A.Code == "TEST02" && rig.B.Code == "TEST02" && rig.A.Phase == DuelPhase.Lobby && rig.B.Phase == DuelPhase.Lobby, $"both moved into a fresh room ({rig.A.Code}/{rig.B.Code})");
        Check(rig.A.History.Count == 0 && rig.B.History.Count == 0 && rig.A.Seed != 7, "fresh state and a new seed");
        await rig.B.SetReadyAsync(); await rig.A.SetReadyAsync(); await rig.Settle();
        Check(rig.A.Phase == DuelPhase.Sealing && rig.B.Phase == DuelPhase.Sealing, "the rematch starts");
        Check(!rig.Broker.RetainedTopics.Any(x => x.StartsWith(DuelProtocol.Root + "TEST01/")), "the old room left nothing behind");
        await rig.A.LeaveAsync(); await rig.B.LeaveAsync();

        var r2 = await Rig.Make(start: false);
        var c = new DoctrineDuel(new DuelOptions { GameFactory = Rig.Stub, Relays = new WrapFactory(r2.Broker), Clock = r2.Clock, JoinWaitMs = 100 });
        Check(await c.JoinAsync("TEST01", "Cy") is { } full && full.Contains("two seats"), "a third seat is refused");
        Check(await c.JoinAsync("NOPE99", "Cy") is { } none && none.Contains("No room"), "an unknown code is answered plainly");
        Check(await c.JoinAsync("a/b", "Cy") is { } bad && bad.Contains("not a room code"), "a malformed code is refused");
        var d = new DoctrineDuel(new DuelOptions { GameFactory = Rig.Stub, Relays = new WrapFactory(r2.Broker), Clock = r2.Clock, JoinWaitMs = 100, GameTag = "other" });
        Check(await d.JoinAsync("TEST01", "Di") is { } ver && ver.Contains("another build"), "another game build is refused");
    }

    /// <summary>The fast subset the mutants run against.</summary>
    public static async Task Core()
    {
        var rig = await Rig.Make(); await rig.PlayAll(); rig.VerifyAgree("core"); rig.VerifyWire("core");
        await Timeout1();
        await Tamper1();
        await DupCore();
    }
    static async Task Timeout1()
    {
        var rig = await Rig.Make();
        await rig.Seal(rig.A); rig.Clock.Advance(45_000); await rig.TickSettle();
        Check(rig.A.Phase == DuelPhase.Result && rig.B.Phase == DuelPhase.Result, "core timeout: the round closes at the deadline");
    }
    static async Task Tamper1()
    {
        var rig = await Rig.Make(tuneB: o => o.RevealTamper = (r, c, n) => { var m = (byte[])n.Clone(); m[0] ^= 1; return (c, m); });
        await rig.SealBoth();
        Check(rig.A.Outcome is { ThemForfeited: true }, "core tamper: refused and forfeited");
    }
    static async Task DupCore()
    {
        var rig = await Rig.Make(wrapA: w => w.Dup = true, wrapB: w => w.Dup = true);
        for (int r = 0; r < 3; r++) { await rig.SealBoth(); await rig.Redeliver(); await rig.ContinueBoth(); }
        rig.VerifyAgree("core dup");
    }
}
