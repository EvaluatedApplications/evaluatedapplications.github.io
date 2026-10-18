using System.Text;
using Phasor;
using ShowroomPlenum;
using static H;

static class Scenarios
{
    static long Fire(PlenumGame g) => g.Board.CountWhere(PlenumGame.HasTag(Tags.Fire));
    static string[] P(params string[] t) => t;

    // ======================================================================================================== lobby

    public static async Task Lobby()
    {
        Section("lobby: codes, listing, join, spectate, stale, private, leave, drop, unreachable");
        var codes = Enumerable.Range(0, 2000).Select(_ => PlenumSession.NewCode()).ToList();
        Check(codes.All(c => c.Length == 6 && c.All(ch => PlenumSession.CodeAlphabet.Contains(ch)) && c.IndexOfAny(new[] { '0', 'O', '1', 'I' }) < 0), "2000 codes: 6 characters, none of 0 O 1 I");
        Check(PlenumSession.NormalizeCode(" k7q-2mx ") == "K7Q2MX" && PlenumSession.NormalizeCode("K7Q2M0") is null && PlenumSession.NormalizeCode("ABC") is null && PlenumSession.NormalizeCode(null) is null, "typed codes are upper-cased and spaces dropped; bad ones are refused");

        var room = new Room();
        room.Factory = new LoopbackRelayFactory(room.Broker);
        var host = new PlenumSession(room.Factory, room.Clock, "Ann");
        await host.OpenLobbyAsync();
        Check(host.Phase == SessionPhase.Lobby, "the lobby opens over the relay");
        Check(await host.CreateRoomAsync(false), "a public room is created");
        var noNick = new PlenumSession(room.Factory, room.Clock, "");
        await noNick.OpenLobbyAsync();
        Check(!await noNick.CreateRoomAsync(false) && noNick.Problem == "Choose a nickname first.", "no nickname, no room");
        string code = host.Code;
        var raw = room.Broker.RetainedText(PlenumTopics.Lobby(code)) ?? "";
        Check(room.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(code)), "the room is a retained message on plenum/v1/lobby/{code}");
        Check(raw.Contains("\"host\":\"Ann\"") && raw.Contains("\"seats\":1") && raw.Contains("\"status\":\"open\"") && raw.Contains("\"v\":1") && raw.Contains("\"private\":false"), "it carries host nickname, seats 1, status open, version  [" + raw + "]");

        var watcher = new PlenumSession(room.Factory, room.Clock, "Wy");
        await watcher.OpenLobbyAsync();
        H.Check(watcher.Rooms.Count == 1 && watcher.Rooms[0].Code == code && watcher.Rooms[0].Host == "Ann" && watcher.Rooms[0].Seats == 1, "another visitor's list shows the room (retained message)");

        var guest = new PlenumSession(room.Factory, room.Clock, "Bo");
        await guest.OpenLobbyAsync();
        H.Check(await guest.JoinAsync(" " + code.ToLowerInvariant() + " "), "join by typing the code (lower case, spaces)");
        H.Check(guest.Role == RoomRole.Guest && guest.Seat == 1 && host.Role == RoomRole.Host && host.Setup!.Seats == 2 && host.Match is not null && guest.Match is not null, "host seat 0, guest seat 1, both have the match");
        raw = room.Broker.RetainedText(PlenumTopics.Lobby(code)) ?? "";
        H.Check(raw.Contains("\"seats\":2") && raw.Contains("\"status\":\"playing\"") && raw.Contains("\"guest\":\"Bo\""), "the retained listing now says seats 2, playing");
        H.Check(watcher.Rooms.Count == 1 && watcher.Rooms[0].Status == "playing" && watcher.Rooms[0].Seats == 2, "the watcher's list updates live");

        var third = new PlenumSession(room.Factory, room.Clock, "Cy");
        await third.OpenLobbyAsync();
        H.Check(await third.JoinAsync(code) && third.Role == RoomRole.Spectator && third.Seat == -1 && !third.IsPlayer, "a third visitor becomes a spectator");

        long ts0 = host.Setup!.Ts;
        room.Clock.Advance(30_000);
        await host.TickAsync();
        H.Check(host.Setup!.Ts == ts0 + 30_000 && PlenumJson.ParseSetup(Encoding.UTF8.GetBytes(room.Broker.RetainedText(PlenumTopics.Lobby(code))!))!.Ts == ts0 + 30_000, "the host republishes the heartbeat every 30 s");
        room.Clock.Advance(29_000);
        await host.TickAsync();
        H.Check(host.Setup!.Ts == ts0 + 30_000, "and not before");
        room.Clock.Advance(59_000);      // 88 s since the last heartbeat
        H.Check(watcher.Rooms.Count == 1, "an entry 88 s old is still listed");
        room.Clock.Advance(3_000);       // 91 s
        H.Check(watcher.Rooms.Count == 0, "an entry older than 90 s is hidden");
        await host.TickAsync();
        H.Check(watcher.Rooms.Count == 1, "a fresh heartbeat brings it back");

        // garbage and other versions never break the list
        var rogue = room.Broker.NewClient("rogue");
        await rogue.ConnectAsync();
        await rogue.PublishAsync(PlenumTopics.Lobby("ZZZZZZ"), Encoding.UTF8.GetBytes("not json"), true);
        await rogue.PublishAsync(PlenumTopics.Lobby("YYYYYY"), Encoding.UTF8.GetBytes("{\"v\":2,\"code\":\"YYYYYY\",\"host\":\"X\",\"hostId\":\"x\",\"seed\":\"1\",\"status\":\"open\",\"ts\":" + room.Clock.UnixMs + "}"), true);
        await rogue.PublishAsync(PlenumTopics.Lobby("WWWWWW"), Encoding.UTF8.GetBytes("{\"v\":1,\"code\":\"OTHER1\",\"host\":\"X\",\"hostId\":\"x\",\"seed\":\"1\",\"status\":\"open\",\"ts\":" + room.Clock.UnixMs + "}"), true);
        var late = new PlenumSession(room.Factory, room.Clock, "Late");
        await late.OpenLobbyAsync();
        H.Check(late.Rooms.Count == 1 && late.Rooms[0].Code == code, "garbage, another rules version and a code/topic mismatch are not listed");
        await rogue.PublishAsync(PlenumTopics.Lobby("ZZZZZZ"), Array.Empty<byte>(), true);
        await rogue.PublishAsync(PlenumTopics.Lobby("YYYYYY"), Array.Empty<byte>(), true);
        await rogue.PublishAsync(PlenumTopics.Lobby("WWWWWW"), Array.Empty<byte>(), true);

        // namespace guard on the in-memory broker too
        bool g1 = false, g2 = false;
        try { await rogue.PublishAsync("pzc/swarm", Encoding.UTF8.GetBytes("x")); } catch (ArgumentException) { g1 = true; }
        try { await rogue.SubscribeAsync("#"); } catch (ArgumentException) { g2 = true; }
        H.Check(g1 && g2, "nothing outside plenum/v1/ can be published or subscribed");
        H.Check(room.Broker.RetainedTopics.All(t => t.StartsWith("plenum/v1/")), "every retained topic is under plenum/v1/");

        // leave
        await guest.LeaveAsync();
        H.Check(host.CanClaimForfeit && host.OpponentGone, "a guest who says goodbye can be forfeited at once");
        await host.LeaveAsync();
        H.Check(!room.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(code)) && watcher.Rooms.Count == 0, "the host leaving clears the retained listing and the list");

        // host dropped: the will clears the listing
        var r2 = await Room.Make(5);
        string c2 = r2.Host.Code;
        H.Check(r2.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(c2)), "second room listed");
        r2.Broker.Drop(r2.RelayOf(r2.Host));
        H.Check(!r2.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(c2)) && r2.Guest.OpponentGone, "a host that vanishes: last will clears the listing, the guest sees 'opponent left'");

        // private
        var r3 = new Room(); r3.Factory = new LoopbackRelayFactory(r3.Broker);
        var ph = new PlenumSession(r3.Factory, r3.Clock, "Pia"); await ph.OpenLobbyAsync(); await ph.CreateRoomAsync(true, 9);
        var pw = new PlenumSession(r3.Factory, r3.Clock, "Quin"); await pw.OpenLobbyAsync();
        H.Check(r3.Broker.RetainedTopics.Contains(PlenumTopics.Private(ph.Code)) && !r3.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(ph.Code)) && pw.Rooms.Count == 0, "a private room is not on the lobby topic and not listed");
        H.Check(await pw.JoinAsync(ph.Code) && pw.Role == RoomRole.Guest, "but joining it by code works");

        // unreachable and retry
        var r4 = new Room(); r4.Factory = new LoopbackRelayFactory(r4.Broker); r4.Broker.Reachable = false;
        var u = new PlenumSession(r4.Factory, r4.Clock, "Uma");
        await u.OpenLobbyAsync();
        H.Check(u.Phase == SessionPhase.Unreachable && u.Problem == "Can't reach the lobby. Solo still works.", "unreachable relay: the plain message");
        r4.Broker.Reachable = true;
        await u.OpenLobbyAsync();
        H.Check(u.Phase == SessionPhase.Lobby && u.Problem is null, "Retry reaches the lobby once the relay is back");
        var nj = new PlenumSession(r4.Factory, r4.Clock, "Nia"); await nj.OpenLobbyAsync();
        H.Check(!await nj.JoinAsync("ABCDEF") && nj.Problem == "There is no open room with that code." && nj.Phase == SessionPhase.Lobby, "a code with no room: plain sentence, back in the lobby");
        H.Check(!await nj.JoinAsync("AB1DEF") && nj.Problem!.StartsWith("A room code is 6"), "a malformed code is refused before any network call");
    }

    // ========================================================================================================= duel

    public static async Task Duel()
    {
        Section("duel: plays, scripted wins, draw, Flip on either board");
        Check(Play.TryParse("u7", out var pu) && pu.Kind == 'u' && pu.A == 7 && Play.TryParse("f3o", out var pf) && pf.Other && pf.A == 3 && Play.TryParse("x1,2", out var px) && px.B == 2 && Play.TryParse("p", out _) && Play.TryParse("t0", out _), "play tokens parse");
        Check(!Play.TryParse("t9", out _) && !Play.TryParse("f0x", out _) && !Play.TryParse("zz", out _) && !Play.TryParse("u99", out _) && !Play.TryParse("x1", out _) && !Play.TryParse("", out _) && !Play.TryParse(null, out _), "bad tokens are refused");

        // seat 0 wins: Flip own (all 8 cards become Fire), Tally reads 8
        {
            var r = await Room.Make(11);
            await r.Round(P("f0s", "t0"), P());
            Check(r.Host.Match!.End == DuelEnd.Seat0Wins && r.Guest.Match!.End == DuelEnd.Seat0Wins, "seat 0 wins with Flip then Tally; both clients agree");
            Check(Fire(r.Host.Match.Side[0]) == 8 && Fire(r.Guest.Match.Side[0]) == 8 && Fire(r.Guest.Match.Side[1]) == 0, "the mirror shows 8 Fire on seat 0's board and none on seat 1's, on both screens");
            Check(r.Agree(), "hashes agree");
            Check(!await r.Host.SubmitAsync(P()), "no more orders once the match is over");
        }
        // seat 1 wins
        {
            var r = await Room.Make(12);
            await r.Round(P(), P("f0s", "t0"));
            Check(r.Host.Match!.End == DuelEnd.Seat1Wins && r.Guest.Match!.End == DuelEnd.Seat1Wins && r.Agree(), "seat 1 wins the same way");
        }
        // draw
        {
            var r = await Room.Make(13);
            await r.Round(P("f0s", "t0"), P("f0s", "t0"));
            Check(r.Host.Match!.End == DuelEnd.Draw && r.Guest.Match!.End == DuelEnd.Draw && r.Agree(), "both cash the Goal in the same round: a draw");
        }
        // Flip aimed at the opponent's board: seat 0 gives seat 1 eight Fire, seat 1 only tallies
        {
            var r = await Room.Make(14);
            await r.Round(P("f0o"), P("t0"));
            Check(r.Host.Match!.End == DuelEnd.Seat1Wins && Fire(r.Host.Match.Side[1]) == 8 && Fire(r.Host.Match.Side[0]) == 0 && r.Agree(), "Flip on the OPPONENT's board changes that board only (seat 1 reads 8 Fire after seat 0's order)");
            Check(r.Host.Match.Log.Any(l => l.Seat == 0 && l.Token == "f0o" && l.Says.Contains("THEIR Board")), "and the log says it acted on their Board");
        }
        // apply order: seat 0's plays first. Seat 1 tallies BEFORE seat 0's flip would matter if the order were swapped
        {
            var r = await Room.Make(15);
            await r.Round(P("f0o"), P("f0s", "t0"));        // seat 0 flips seat 1 to 8 Fire, seat 1's own Flip turns it back: Tally reads 0
            Check(r.Host.Match!.End == DuelEnd.Running && r.Host.Match.Round == 2, "seat 0's Flip on seat 1 lands before seat 1's own plays: seat 1 reads 0 Fire, nobody wins");
            Check(Fire(r.Host.Match.Side[0]) == 8 && Fire(r.Host.Match.Side[1]) == 8, "then the Flipper acts on each board (both now Fire)");
            await r.Round(P("t0"), P("t0"));
            Check(r.Host.Match.End == DuelEnd.Draw && r.Agree(), "round 2: both Tally and both read 8: a draw");
        }

        Section("duel: order delivery (duplicate, late, out of order, conflicting, malformed)");
        {
            var r = await Room.Make(21);
            await r.Round(P("f0s"), P());
            string h1 = r.Guest.Match!.Hash();
            // the guest asks for a replay from round 1: the host re-sends its round-1 order and hash
            await r.RelayOf(r.Guest).PublishAsync(PlenumTopics.Events(r.Host.Code), PlenumJson.Sync(r.Guest.MyId, 1), false, 1);
            Check(r.Guest.Match.Hash() == h1 && r.Guest.Match.ResolvedRounds == 1 && r.Guest.Problem is null, "a duplicate of an already applied order changes nothing");
            Check(r.Agree(), "both clients still agree after the replay");

            var m = r.Host.Match!;
            var same = m.OrderFor(0, 1)!;
            Check(m.Submit(same) == SubmitResult.Duplicate, "re-submitting the same order is a duplicate");
            var changed = new DuelOrder(0, 1, P("t0"), same.Prev);
            Check(m.Submit(changed) == SubmitResult.Conflict && m.Hash() == h1, "a different order for the same (seat, round) is a conflict and is ignored: the first one stands");
            Check(m.Submit(new DuelOrder(0, 1, P("bogus"), "x")) == SubmitResult.Invalid && m.Submit(new DuelOrder(2, 1, P(), "x")) == SubmitResult.Invalid && m.Submit(new DuelOrder(0, 99, P(), "x")) == SubmitResult.Invalid, "malformed and far-future orders are invalid");
        }
        {
            // out of order: build the orders on an oracle, deliver them to a fresh match in a scrambled order
            var oracle = new DuelMatch(777);
            var orders = new List<DuelOrder>();
            var planned = new (string[] a, string[] b)[] { (P("f0s"), P()), (P("t1"), P("f1s")), (P(), P("t1")) };
            for (int i = 0; i < planned.Length; i++)
            {
                var o0 = new DuelOrder(0, i + 1, planned[i].a, oracle.Hash());
                var o1 = new DuelOrder(1, i + 1, planned[i].b, oracle.Hash());
                orders.Add(o0); orders.Add(o1);
                oracle.Submit(o0); oracle.Submit(o1);
            }
            var fresh = new DuelMatch(777);
            var res = new List<SubmitResult>();
            foreach (var idx in new[] { 5, 4, 3, 2, 1, 0 }) res.Add(fresh.Submit(orders[idx]));     // round 3 first, round 1 last
            Check(res.Take(5).All(x => x == SubmitResult.Stored) && res[5] == SubmitResult.Resolved, "orders for later rounds wait; the last missing order resolves all three rounds");
            Check(fresh.ResolvedRounds == 3 && fresh.Hash() == oracle.Hash() && Enumerable.Range(0, 4).All(i => fresh.HashAfter(i) == oracle.HashAfter(i)), "and the result is identical to in-order delivery, hash for hash");
        }

        Section("duel: seat claims");
        {
            var r = await Room.Make(31);
            var cy = r.NewSession("Cy"); await cy.OpenLobbyAsync(); await cy.JoinAsync(r.Host.Code);
            string h0 = r.Host.Match!.Hash();
            var imp = r.RelayOf(cy);
            string forged = "{\"t\":\"order\",\"id\":\"" + cy.MyId + "\",\"seat\":1,\"round\":1,\"plays\":[\"f0s\",\"t0\"],\"prev\":\"" + h0 + "\"}";
            await imp.PublishAsync(PlenumTopics.Events(r.Host.Code), Encoding.UTF8.GetBytes(forged), false, 1);
            string forged2 = "{\"t\":\"order\",\"id\":\"" + r.Guest.MyId + "\",\"seat\":0,\"round\":1,\"plays\":[\"f0s\",\"t0\"],\"prev\":\"" + h0 + "\"}";
            await r.RelayOf(r.Guest).PublishAsync(PlenumTopics.Events(r.Host.Code), Encoding.UTF8.GetBytes(forged2), false, 1);
            Check(!r.Host.Match.HasOrder(0, 1) && !r.Host.Match.HasOrder(1, 1) && !r.Guest.Match!.HasOrder(1, 1), "an order from a client that is not seated there, or claiming the other seat, is ignored");
            await r.Round(P(), P());
            Check(r.Host.Match.End == DuelEnd.Running && r.Host.Match.Round == 2 && r.Agree(), "the real orders then play out normally");
        }
        {
            var r = await Room.Make(41);
            await r.Round(P("t1"), P("t1"));
            await r.Round(P("t1"), P("t1"));
            var cy = r.NewSession("Cy"); await cy.OpenLobbyAsync();
            Check(await cy.JoinAsync(r.Host.Code) && cy.Role == RoomRole.Spectator, "a spectator joins mid-match");
            Check(cy.Match!.ResolvedRounds == 2 && cy.Match.Hash() == r.Host.Match!.Hash(), "and catches up from the replayed orders (same hash as the players)");
            int before = r.Broker.Published;
            bool submitted = await cy.SubmitAsync(P("f0s", "t0"));
            Check(!submitted && r.Broker.Published == before && !r.Host.Match.HasOrder(0, 3), "a spectator cannot submit (nothing is sent)");
            await r.Round(P("t1"), P("t1"));
            Check(cy.Match.ResolvedRounds == 3 && cy.Match.Hash() == r.Host.Match.Hash() && cy.Problem is null, "and follows the live match read-only");
        }

        Section("duel: drop, reconnect, resume");
        {
            var r = await Room.Make(51);
            await r.Round(P("t1"), P("t1"));
            r.Broker.Drop(r.RelayOf(r.Guest));
            Check(r.Host.OpponentGone && !r.Host.CanClaimForfeit, "guest drops: the host sees 'opponent left' (last will), no forfeit yet");
            await r.Host.SubmitAsync(P("t1"));                  // the guest never hears this
            await r.RelayOf(r.Guest).ReconnectAsync();
            Check(!r.Host.OpponentGone, "guest back: the host sees them again");
            Check(r.Guest.Match!.HasOrder(0, 2), "the host replayed the order the guest missed");
            await r.Guest.SubmitAsync(P("t1"));
            Check(r.Host.Match!.ResolvedRounds == 2 && r.Agree(), "round 2 resolves and both agree");
            // now the host drops
            r.Broker.Drop(r.RelayOf(r.Host));
            Check(r.Guest.OpponentGone && !r.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(r.Host.Code)), "host drops: listing cleared, guest sees 'opponent left'");
            await r.Guest.SubmitAsync(P("t1"));
            await r.RelayOf(r.Host).ReconnectAsync();
            Check(!r.Guest.OpponentGone && r.Broker.RetainedTopics.Contains(PlenumTopics.Lobby(r.Host.Code)), "host back within the minute: the setup is republished, the guest sees them again");
            Check(r.Host.Match!.HasOrder(1, 3), "the guest's order sent while the host was away reached it again");
            await r.Host.SubmitAsync(P("t1"));
            Check(r.Host.Match.ResolvedRounds == 3 && r.Agree(), "round 3 resolves after the host's resume");
        }
        {
            var r = await Room.Make(61);
            await r.Round(P("t1"), P("t1"));
            r.Broker.Drop(r.RelayOf(r.Guest));
            r.Clock.Advance(59_000);
            Check(!r.Host.CanClaimForfeit && r.Host.ForfeitSecondsLeft is 1 or 2, "59 s after the drop: no forfeit yet");
            r.Clock.Advance(2_000);
            Check(r.Host.CanClaimForfeit, "after 60 s the host may claim the win");
            await r.Host.ClaimForfeitAsync();
            Check(r.Host.Match!.End == DuelEnd.Seat0Wins && r.Host.Match.EndReason.Contains("left"), "forfeit: the match is awarded to the host");
            await r.RelayOf(r.Guest).ReconnectAsync();
            Check(r.Guest.Match!.End == DuelEnd.Seat0Wins, "a guest who comes back is told the match was awarded");
        }

        Section("duel: the turn clock");
        {
            var r = await Room.Make(63);
            await r.Host.SubmitAsync(P("t1"));
            Check(r.Host.TurnSecondsLeft == -1 && r.Guest.TurnSecondsLeft == 90, "the clock shows 90 s for the side that has not submitted and is off for the side that has");
            r.Clock.Advance(89_000);
            await r.Guest.TickAsync();
            Check(!r.Guest.MyOrderIn && r.Guest.TurnSecondsLeft == 1, "at 89 s nothing is forfeited");
            r.Clock.Advance(1_500);
            await r.Guest.TickAsync();
            await r.Host.TickAsync();
            Check(r.Host.Match!.ResolvedRounds == 1 && r.Host.Match.OrderFor(1, 1)!.Plays.Length == 0, "at 90 s the unsubmitted side submits an EMPTY order and the round resolves");
            Check(r.Host.Match.OrderFor(0, 1)!.Plays.SequenceEqual(P("t1")), "the plays already submitted are untouched");
            Check(r.Agree(), "both agree");
            await r.Host.SubmitAsync(P("t1"));
            r.Clock.Advance(121_000);
            Check(r.Host.OpponentSeemsStuck, "a connected opponent who never orders is reported as stuck after 2 minutes (no order can be made for them)");
        }

        Section("duel: integrity (hash mismatch halts)");
        {
            // (a) the guest's state is corrupted AFTER it signed its order and BEFORE the round resolves: the guest's own check of the host's order catches it and tells the host
            var r = await Room.Make(71);
            await r.Guest.SubmitAsync(P());
            var hand = r.Guest.Match!.Side[1].Hand;
            var cells = hand.RawCells.ToArray(); cells[3] ^= 1UL; hand.LoadCells(cells);
            await r.Host.SubmitAsync(P());
            Check(r.Host.Match!.HaltReason is not null && r.Guest.Match.HaltReason is not null, "one corrupted cell: BOTH clients halt");
            Check(r.Host.Problem is not null && r.Host.Problem.StartsWith("Out of sync") && r.Guest.Problem!.StartsWith("Out of sync") && r.Host.Match.End == DuelEnd.Halted, "with a plain 'out of sync' message, never silently continuing");
            Check(!await r.Host.SubmitAsync(P()), "a halted match accepts no more orders");
        }
        {
            // (c) the same corruption with the order check switched off: the end-of-round hash comparison alone stops both sides
            bool wasSkip = PlenumFaults.SkipPrevCheck;
            PlenumFaults.SkipPrevCheck = true;
            var r = await Room.Make(73);
            await r.Guest.SubmitAsync(P());
            var hand = r.Guest.Match!.Side[1].Hand;
            var cells = hand.RawCells.ToArray(); cells[3] ^= 1UL; hand.LoadCells(cells);
            await r.Host.SubmitAsync(P());
            PlenumFaults.SkipPrevCheck = wasSkip;
            Check(r.Host.Match!.HaltReason is not null && r.Guest.Match.HaltReason is not null && r.Host.Match.HaltReason.StartsWith("Out of sync after round 1"), "round hashes alone catch a corrupted cell: both clients halt after round 1");
        }        {
            // (b) corrupted BETWEEN rounds: the next order is built on a state the other side does not have
            var r = await Room.Make(72);
            await r.Round(P("t1"), P("t1"));
            var hand = r.Guest.Match!.Side[1].Hand;
            var cells = hand.RawCells.ToArray(); cells[3] ^= 1UL; hand.LoadCells(cells);
            await r.Round(P("t1"), P("t1"));
            Check(r.Host.Match!.HaltReason is not null && r.Host.Match.HaltReason.Contains("different state") && r.Guest.Match.HaltReason is not null && r.Guest.Match.HaltReason.Contains("different state"), "an order built on a different state halts the round on both sides, by name");
            Check(r.Host.Match.ResolvedRounds == 1, "and the bad round was not applied");
        }
    }

    // ============================================================================================ random matches

    static string RandomToken(Random rng, DuelMatch fork, int seat)
    {
        var g = fork.Side[seat];
        if (!g.TryHand(out var kinds)) return Play.PutBack;
        int k = rng.Next(10);
        int bit = rng.Next(3) == 0 ? rng.Next(8) : Tags.Fire;
        if (k < 3 && kinds.Count > 0) return Play.Unit(kinds[rng.Next(kinds.Count)].Key);
        if (k < 5) return Play.Tally(bit);
        if (k < 8) return Play.Flip(bit, rng.Next(2) == 0);
        if (kinds.Count > 0) return Play.Fuse(kinds[rng.Next(kinds.Count)].Key, kinds[rng.Next(kinds.Count)].Key);
        return Play.Tally(bit);
    }

    static string[] RandomOrder(Random rng, DuelMatch real, int seat)
    {
        var fork = real.Fork();
        var plays = new List<string>();
        int costed = 0;
        for (int tries = 0; tries < 8 && costed < PlenumGame.PlaysPerTurn; tries++)
        {
            string tok = RandomToken(rng, fork, seat);
            var res = fork.ApplyPlay(seat, tok);
            if (res.Refused) continue;
            plays.Add(tok);
            if (tok != Play.PutBack) costed++;
        }
        return plays.ToArray();
    }

    public static async Task Random20()
    {
        Section("20 random matches of random legal play over the loopback relay");
        int rounds = 0, wins0 = 0, wins1 = 0, draws = 0, open = 0;
        for (int i = 0; i < 20; i++)
        {
            var rng = new Random(9000 + i);
            var r = await Room.Make((ulong)(5000 + i * 7919));
            bool agree = true, clean = true;
            for (int round = 0; round < 12 && !r.Host.Match!.Over; round++)
            {
                await r.Host.SubmitAsync(RandomOrder(rng, r.Host.Match, 0));
                await r.Guest.SubmitAsync(RandomOrder(rng, r.Guest.Match!, 1));
                rounds++;
                agree &= r.Agree();
                clean &= r.Host.Problem is null && r.Guest.Problem is null && r.Host.Match.HaltReason is null;
            }
            Check(agree && clean && r.Host.Match!.End == r.Guest.Match!.End, $"match {i + 1}: hashes agree after every round ({r.Host.Match.ResolvedRounds} rounds, {r.Host.Match.End})");
            switch (r.Host.Match.End) { case DuelEnd.Seat0Wins: wins0++; break; case DuelEnd.Seat1Wins: wins1++; break; case DuelEnd.Draw: draws++; break; default: open++; break; }
        }
        Note($"{rounds} rounds played; seat 0 wins {wins0}, seat 1 wins {wins1}, draws {draws}, still running at 12 rounds {open}");
    }
}
