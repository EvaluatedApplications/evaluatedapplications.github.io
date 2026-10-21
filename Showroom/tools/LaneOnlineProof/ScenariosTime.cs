using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using static H;

static partial class Scenarios
{
    static bool FlaggedReveal(Pair p, int n, LaneRole role)
        => p.Mon.Log.Where(l => l.Topic.EndsWith("/reveal/" + n + "/" + LaneOnlineProtocol.RoleName(role))).Select(l => LaneWire.ReadReveal(l.Payload)).Any(r => r is { Missed: true });

    static bool AnyReveal(Pair p, int n, LaneRole role) => p.Mon.Log.Any(l => l.Topic.EndsWith("/reveal/" + n + "/" + LaneOnlineProtocol.RoleName(role)));

    /// <summary>The ms left of the host's pause clock after it has looked at the clock now.</summary>
    static async Task<long> LeftMs(Pair p) { await p.Step(0, true, false); return (long)Math.Round(p.A.Match!.PauseLeft * 1000); }

    // 11. ---- the clock: grace, a missed pause, three in a row, a slow arrival, a reveal that never comes -----------------------------

    public static async Task Timeouts(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("timeouts: the pause clock, the grace, a missed pause (empty plays), three in a row, a late arrival");

        // ---- the other player answers inside the grace: nothing is missed
        var g = new Pair(pa, pb);
        await g.StartAsync();
        g.AutoA = true; g.StageOnly(g.B);                              // the guest has chosen but is silent (its device is not answering yet)
        long left = await LeftMs(g);
        Check(left is > 28_000 and <= 30_000 && g.A.Match!.Stage == MatchStage.Pause, $"the pause clock reads about 30 s at the start ({left} ms)");
        await g.Step((int)left + 4000, true, false);                 // 4 s past the deadline, the guest silent
        Check(g.A.Match!.Stage == MatchStage.Pause && g.A.MyCommitted && !AnyReveal(g, 0, LaneRole.Host), "4 s past the deadline the host has committed and is still waiting: nothing is missed yet");
        await g.Step(0, false, true);                                // the guest answers now (its clock has run out, so its chosen plays go)
        await g.Step(100);
        Check(g.A.Match!.Stage == MatchStage.Run && g.B.Match!.Stage == MatchStage.Run && g.A.MissedInARow == 0 && !FlaggedReveal(g, 0, LaneRole.Host) && g.A.Match.Battle.Bodies(Side.Enemy) > 0,
            "a commitment that arrives inside the grace counts: both reveal, nothing is flagged, and the guest's plays are in the host's battle");
        await g.DisposeAsync();

        // ---- the other player's plays do not arrive: after the grace the pause is declared missed
        var m = new Pair(pa, pb);
        m.FactoryB.Tune = r => r.Mute = t => t.Contains("/commit/0/") || t.Contains("/reveal/0/");   // the guest's answers for the first pause are lost
        await m.StartAsync();
        m.AutoA = m.AutoB = true;
        long t0 = m.Clock.NowMs; long deadline = t0 + await LeftMs(m);
        long declared = -1;
        for (int i = 0; i < 120 && declared < 0; i++)
        {
            await m.Step(500);
            if (m.A.Match!.Stage == MatchStage.Run) declared = m.Clock.NowMs;
        }
        long late = declared - deadline;
        Check(declared > 0 && late >= 6000 && late <= 7600, $"the host declares the pause missed after the deadline plus the grace of 6 s (it did, {late} ms after the deadline)");
        Check(FlaggedReveal(m, 0, LaneRole.Host) && m.A.MissedInARow == 1 && m.A.Match!.Battle.Bodies(Side.Enemy) == 0 && m.A.Match.Battle.Bodies(Side.Player) > 0 && m.A.Notices.Any(n => Has(n, "did not answer in time")),
            "the host's reveal says 'you were too late', the guest's plays for that pause are empty, the host's own are in, and the host is told in words");
        await m.Step(250);
        Check(Has(m.B.Notices.LastOrDefault(n => Has(n, "too late")), "came too late") && m.B.Match!.Stage == MatchStage.Run && m.B.Match.Battle.Bodies(Side.Player) > 0 && m.B.Match.Battle.Bodies(Side.Enemy) == 0,
            "the late player reads the same flag, drops the plays they had locked in, is told so, and plays on with the same board");
        await m.PlayUntil(() => m.A.Phase == OnlinePhase.Ended || (m.A.Match?.Phase ?? 0) >= 2 && m.A.PauseOpen && m.B.PauseOpen, 100);
        Check(m.A.Phase == OnlinePhase.Playing && m.B.Phase == OnlinePhase.Playing && SameHashes(m.A, m.B) && m.A.Match!.PauseHashes.Count >= 3, "both are at the next pause with the same hashes (a missed pause does not split the games)");
        await m.Step(250);
        Check(m.A.MissedInARow == 0 && m.A.Match!.Battle.Bodies(Side.Enemy) > 0 || m.A.Match!.Phase < 2, "after a pause that goes through, the count of missed pauses starts again at 0");
        await m.DisposeAsync();

        // ---- three missed pauses in a row end the match for the player who stayed
        var s = new Pair(pa, pb);
        s.FactoryB.Tune = r => r.Mute = t => t.Contains("/commit/") || t.Contains("/reveal/");
        await s.StartAsync();
        await s.PlayUntil(() => s.A.Phase == OnlinePhase.Ended, 700);
        Check(s.A.End is { Kind: OnlineEndKind.OpponentGone, YouWon: true } && s.A.MissedInARow >= 3 && Has(s.A.End.Text, "missed 3 pauses in a row"), $"three missed pauses in a row end the match for the player who stayed, in words: {s.A.End?.Kind} {s.A.End?.Text}");
        await s.Step(250);
        Check(s.B.End is { Kind: OnlineEndKind.YouWereAway, YouWon: false } && Has(s.B.End.Text, "away too long"), "and the other is told they were away too long and the match went to the host: " + s.B.End?.Text);
        await s.DisposeAsync();

        // ---- a player who arrives late at the pause (a slow device still running the battle) is not penalised
        var a = new Pair(pa, pb);
        a.FactoryB.Tune = r => r.Mute = t => t.Contains("/hash/0/");   // the guest's arrival at the first pause is not heard
        await a.StartAsync();
        a.AutoA = true; a.StageOnly(a.B);
        long l2 = await LeftMs(a);
        await a.Step((int)l2 + 10_000, true, false);
        Check(a.A.Match!.Stage == MatchStage.Pause && !AnyReveal(a, 0, LaneRole.Host) && a.A.MissedInARow == 0, "10 s past the deadline with the guest not yet arrived: the host still waits (arrival is waited for)");
        await a.Step(0, false, true); await a.Step(100);
        Check(a.A.Match!.Stage == MatchStage.Run && a.A.MissedInARow == 0 && !FlaggedReveal(a, 0, LaneRole.Host) && a.A.Match.Battle.Bodies(Side.Enemy) > 0, "when they arrive and answer their plays count: nothing was missed");
        await a.DisposeAsync();

        // ---- committed but never revealed: the match ends for the one who stayed
        var r = new Pair(pa, pb);
        r.FactoryB.Tune = x => x.Mute = t => t.Contains("/reveal/");
        await r.StartAsync();
        r.AutoA = r.AutoB = true;
        await r.Step(250); await r.Step(250);
        Check(r.A.Match!.Stage == MatchStage.Pause && r.A.Phase == OnlinePhase.Playing, "both committed and one reveal is missing: the pause stays open");
        await r.Step(30_000);
        Check(r.A.Phase == OnlinePhase.Playing, "30 s later the host is still waiting for the reveal");
        for (int i = 0; i < 60 && r.A.Phase != OnlinePhase.Ended; i++) await r.Step(2000);
        Check(r.A.End is { Kind: OnlineEndKind.OpponentGone, YouWon: true } && Has(r.A.End.Text, "never showed"), $"about 20 s after the grace the host ends the match: {r.A.End?.Text}");
        await r.Step(250);
        Check(r.B.End is { Kind: OnlineEndKind.YouWereAway }, "the other is told");
        await r.DisposeAsync();
    }

    // 12. ---- connections that drop, come back, or end ---------------------------------------------------------------------------------

    public static async Task Disconnects(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("disconnect and rejoin: a short drop is waited out, a long one ends the match, a goodbye ends it at once");

        // ---- a drop of a few seconds, with the guest's own answer made while offline
        var d = new Pair(pa, pb);
        await d.StartAsync();
        d.AutoA = d.AutoB = true;
        await d.PlayUntil(() => d.A.Match?.Stage == MatchStage.Run && d.B.Match?.Stage == MatchStage.Run, 40);
        d.AutoB = false;
        var relayB = d.RelayOf(false);
        d.Broker.Drop(relayB.Loop);
        Check(!d.B.Online && Has(d.B.StatusLine, "Connection lost"), "the guest's own screen says the connection is lost");
        await d.Step(30_000);
        Check(d.A.Opponent == OpponentLink.Away && Has(d.A.StatusLine, "lost their connection") && d.A.Match!.Stage == MatchStage.Pause && d.B.Match!.Stage == MatchStage.Pause, "the host sees the guest away (the broker's last will) and both stand at the next pause");
        d.AutoB = true; await d.Step(250);
        Check(d.B.MyCommitted && !d.Mon.Log.Any(l => l.Topic.EndsWith("/commit/1/guest")), "the guest locks in while offline: the message is held, not on the relay");
        await d.Step(5000);
        Check(d.A.Opponent == OpponentLink.Away && d.A.Phase == OnlinePhase.Playing && d.A.MissedInARow == 0, "5 s later the host is still waiting: nothing is missed, nothing is ended");
        await relayB.Loop.ReconnectAsync();
        await d.Step(500);
        Check(d.B.Online && d.A.Opponent == OpponentLink.Here && d.Mon.Log.Any(l => l.Topic.EndsWith("/commit/1/guest")), "the guest is back within the grace: what it said while away is said again, and the host sees it here");
        await d.PlayOutAsync();
        Check(d.A.End?.Kind == OnlineEndKind.Played && d.B.End?.Kind == OnlineEndKind.Played && SameHashes(d.A, d.B) && d.A.MissedInARow == 0 && d.A.Errors == 0 && d.B.Errors == 0, $"the match goes on to its end with identical hashes ({d.A.End?.Kind}, {d.B.End?.Kind})");
        await d.DisposeAsync();

        // ---- a drop that lasts: the player who stayed is awarded the match
        var g = new Pair(pa, pb);
        await g.StartAsync();
        g.AutoA = g.AutoB = true;
        await g.PlayUntil(() => g.A.Match?.Stage == MatchStage.Run && g.B.Match?.Stage == MatchStage.Run, 40);
        g.AutoB = false;
        var rb = g.RelayOf(false);
        g.Broker.Drop(rb.Loop);
        for (int i = 0; i < 30 && g.A.Phase != OnlinePhase.Ended; i++) await g.Step(5000);
        Check(g.A.End is { Kind: OnlineEndKind.OpponentGone, YouWon: true } && Has(g.A.End.Text, "gone too long") && g.A.Opponent is OpponentLink.Gone, $"a guest who stays away past the grace loses the match to the host, in words: {g.A.End?.Kind} {g.A.End?.Text}");
        await rb.Loop.ReconnectAsync(); await g.Step(500);
        Check(g.B.End is { Kind: OnlineEndKind.YouWereAway, YouWon: false } && Has(g.B.End.Text, "away too long"), "and when they come back they are told what happened: " + g.B.End?.Text);
        await g.DisposeAsync();

        // ---- a goodbye
        var l = new Pair(pa, pb);
        await l.StartAsync();
        l.AutoA = l.AutoB = true;
        await l.PlayUntil(() => (l.A.Match?.Phase ?? 0) >= 1, 40);
        await l.B.LeaveAsync();
        Check(l.B.Phase == OnlinePhase.Idle && !l.B.Online, "leaving closes the leaver's side");
        await l.Step(250, true, false);
        Check(l.A.End is { Kind: OnlineEndKind.OpponentLeft, YouWon: true } && Has(l.A.End.Text, "left the match") && l.A.Phase == OnlinePhase.Ended, $"the other is told at once and awarded the match: {l.A.End?.Text}");
        await l.A.LeaveAsync();
        Check(l.Broker.RetainedTopics.Count == 0, "when the second player leaves too nothing is left on the relay: " + string.Join(", ", l.Broker.RetainedTopics.Take(3)));

        // ---- the guest leaves before the match began: the host's room opens again, and a new guest can take the seat
        var e = new Pair(pa, pb);
        await e.CreateAndJoinAsync();
        await e.Step(250);
        var lobby = new LaneLobby(new LaneLobbyOptions { Relays = new GateFactory(e.Broker), Clock = e.Clock });
        await lobby.StartAsync();
        Check(lobby.Rooms.Count == 0, "while two are seated the room is not listed");
        await e.B.LeaveAsync();
        await e.Step(250, true, false);
        Check(e.A.Phase == OnlinePhase.Waiting && lobby.Rooms.Count == 1 && lobby.Rooms[0].Code == e.Code && Has(e.A.Notices.Last(), "open again"), "the guest leaving before the start reopens the host's room, listed again");
        e.B = new LaneOnline(e.Opts(e.FactoryB, pb));
        var j = e.B.JoinAsync(e.Code, "Lord Cy");
        while (!j.IsCompleted) { await e.A.AdvanceAsync(); await Task.Delay(5); }
        Check(await j is null, "a new guest joins the reopened room");
        await e.Until(() => e.A.Phase == OnlinePhase.Playing && e.B.Phase == OnlinePhase.Playing, 500, 80);
        Check(e.A.Phase == OnlinePhase.Playing && e.B.Phase == OnlinePhase.Playing && SameHashes(e.A, e.B) && e.A.LocalSide == Side.Player && e.A.OpponentName == "Lord Cy", "and the two play from a clean start with the same hashes, the host on the same side as before");
        await e.DisposeAsync(); await lobby.DisposeAsync();

        // ---- the host leaves before the match began
        var h = new Pair(pa, pb);
        await h.CreateAndJoinAsync();
        await h.Step(250);
        await h.A.LeaveAsync();
        await h.Step(250, false, true);
        Check(h.B.End is { Kind: OnlineEndKind.OpponentLeft } && Has(h.B.End.Text, "host left before the match started") && h.B.Phase == OnlinePhase.Ended, $"a host who leaves before the start: the guest is told, plainly: {h.B.End?.Text}");
        await h.B.LeaveAsync();
        Check(h.Broker.RetainedTopics.Count == 0, "and the relay is clean");
    }
}
