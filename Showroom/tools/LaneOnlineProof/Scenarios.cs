using System.Text;
using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using static H;

static partial class Scenarios
{
    static bool Has(string? s, string part) => s is not null && s.Contains(part, StringComparison.OrdinalIgnoreCase);
    static bool SameHashes(LaneOnline a, LaneOnline b) => a.Match is not null && b.Match is not null && a.Match.PauseHashes.Count > 0 && a.Match.PauseHashes.SequenceEqual(b.Match.PauseHashes);
    static string Hashes(LaneOnline a) => a.Match is null ? "-" : string.Join(" ", a.Match.PauseHashes.Select(h => h.Tick + ":" + LaneMatch.ShortHash(h.Hash)));

    // 1. ---- the wire ------------------------------------------------------------------------------------------------------------

    public static void Wire()
    {
        Section("wire: topics, codes, plans, commitments, names, damaged messages");
        var t = new LaneTopics("ABC123");
        var all = new (string Topic, LaneTopics.Kind Kind, int Gen, int Pause, LaneRole Role)[]
        {
            (t.Room, LaneTopics.Kind.Room, 0, 0, LaneRole.Host), (t.Join("llabc"), LaneTopics.Kind.Join, 0, 0, LaneRole.Guest),
            (t.Presence(LaneRole.Host), LaneTopics.Kind.Presence, 0, 0, LaneRole.Host), (t.Presence(LaneRole.Guest), LaneTopics.Kind.Presence, 0, 0, LaneRole.Guest),
            (t.Ready(2, LaneRole.Guest), LaneTopics.Kind.Ready, 2, 0, LaneRole.Guest), (t.Hash(1, 7, LaneRole.Host), LaneTopics.Kind.Hash, 1, 7, LaneRole.Host),
            (t.Commit(0, 12, LaneRole.Guest), LaneTopics.Kind.Commit, 0, 12, LaneRole.Guest), (t.Reveal(3, 40, LaneRole.Host), LaneTopics.Kind.Reveal, 3, 40, LaneRole.Host),
            (t.Fin(1, LaneRole.Guest), LaneTopics.Kind.Fin, 1, 0, LaneRole.Guest), (t.End(0, LaneRole.Host), LaneTopics.Kind.End, 0, 0, LaneRole.Host),
            (t.Rematch(4, LaneRole.Guest), LaneTopics.Kind.Rematch, 4, 0, LaneRole.Guest),
        };
        Check(all.All(a => a.Topic.StartsWith("plenum/v1/doctrine-lane/room/ABC123/")) && LaneTopics.Lobby("ABC123") == "plenum/v1/doctrine-lane/lobby/ABC123" && LaneOnlineProtocol.LobbyFilter == "plenum/v1/doctrine-lane/lobby/+" && t.Filter == "plenum/v1/doctrine-lane/room/ABC123/#",
            "every topic of a room is under plenum/v1/doctrine-lane/room/<code>/ and the lobby entry is under plenum/v1/doctrine-lane/lobby/<code>");
        Check(all.All(a => t.TryParse(a.Topic, out var p) && p.Kind == a.Kind && p.Gen == a.Gen && p.Pause == a.Pause && (a.Kind is LaneTopics.Kind.Room or LaneTopics.Kind.Join || p.Role == a.Role)), "every topic reads back as the same kind, game, pause and role");
        string[] foreign =
        [
            "plenum/v1/doctrine/ABC123/room", "plenum/v1/doctrine-lane/room/OTHER9/room", "pzc/x/room", "plenum/v1/lobby/ABC123", "plenum/v1/doctrine-lane/room/ABC123/g1/commit/x/host",
            "plenum/v1/doctrine-lane/room/ABC123/g100/commit/1/host", "plenum/v1/doctrine-lane/room/ABC123/g1/commit/1000/host", "plenum/v1/doctrine-lane/room/ABC123/g1/commit/1/mallory",
            "plenum/v1/doctrine-lane/room/ABC123/join/" + new string('x', 41), "plenum/v1/doctrine-lane/room/ABC123/room/extra", "plenum/v1/doctrine-lane/room/ABC123/g1/commit/1", "plenum/v1/doctrine-lane/room/ABC123/",
        ];
        Check(foreign.All(f => !t.TryParse(f, out _)), "another room, another root, a pause out of range, a made-up role and a malformed tail are not read");

        Check(LaneOnlineProtocol.Normalize(" ab-c 123 ") == "ABC123" && LaneOnlineProtocol.Normalize("abcd") == "ABCD" && LaneOnlineProtocol.Normalize("a/b1c") is null && LaneOnlineProtocol.Normalize("ab") is null
              && LaneOnlineProtocol.Normalize("x#y1") is null && LaneOnlineProtocol.Normalize("ABCDEFGHIJKLM") is null && LaneOnlineProtocol.Normalize("ab+cd") is null && LaneOnlineProtocol.Normalize(null) is null,
            "a typed code is trimmed and upper-cased; a wildcard, a slash, a short or a long one is not a code");
        var rnd = new Random(1);
        var codes = Enumerable.Range(0, 200).Select(_ => LaneOnlineProtocol.NewCode(rnd.Next)).ToList();
        Check(codes.All(c => c.Length == 6 && LaneOnlineProtocol.ValidCode(c) && c.All(ch => "ABCDEFGHJKMNPQRSTUVWXYZ23456789".Contains(ch))) && codes.Distinct().Count() > 150, "a new code is six characters without the look-alikes (0 O 1 I L), and they differ");

        Check(LaneNames.Clean("  Lord   Aldric  ") == "Lord Aldric" && LaneNames.Clean("") is null && LaneNames.Clean("!!!") is null && LaneNames.Clean(null) is null && LaneNames.Clean(new string('a', 80))!.Length == 24, "a name is cleaned: spaces folded, nothing left means no name, 24 characters at most");
        var dirty = LaneNames.Clean("<b>Bob</b> \u0007x/y\\z; DROP")!;
        Check(!dirty.Any(c => "<>/\\;\u0007".Contains(c)), "markup, slashes and control characters never survive in a name: " + dirty);
        Check(Enumerable.Range(0, 50).Select(_ => LaneNames.Random(rnd.Next)).All(n => n.StartsWith("Lord ") && LaneNames.Clean(n) == n), "a made-up name is 'Lord ...' and is already clean");

        var plan = new LanePlan([new PackBuy(0, 3), new PackBuy(2, 7)], [new LaneOrderChoice(1, FieldOrderKind.Hold), new LaneOrderChoice(2, FieldOrderKind.Hunt)]);
        string enc = LanePlanCodec.Encode(plan);
        Check(enc == "0.3,2.7;1.1,2.5" && LanePlanCodec.TryDecode(enc, 3, out var back, out _) && back.SameAs(plan), "a plan encodes to a short string and reads back the same: " + enc);
        Check(LanePlanCodec.Encode(LanePlan.Empty) == ";" && LanePlanCodec.TryDecode(";", 3, out var empty, out _) && empty.IsEmpty, "an empty plan is a single ';'");
        string[] bad = ["9.0;", "0.99;", ";0.2", ";0.1,0.4", ";0.9", "a.b;", "0.1", "0.1;;", ";-1.1", "-1.0;", ";0.1,1.1,2.1,3.1", new string('x', 3000), string.Join(",", Enumerable.Repeat("0.0", 65)) + ";"];
        Check(bad.All(b => !LanePlanCodec.TryDecode(b, 3, out _, out _)), "a lane or kind out of range, Flank, two orders for one lane, a missing separator, junk and 65 packs are all refused");
        Check(LanePlanCodec.TryDecode(string.Join(",", Enumerable.Repeat("0.0", 64)) + ";", 3, out _, out _), "64 packs are still allowed");

        var nonce = new byte[16]; new Random(3).NextBytes(nonce);
        var c = LaneOnlineProtocol.Commit("ABC123", 0, 4, LaneRole.Host, nonce, enc);
        Check(c.Length == 32 && LaneOnlineProtocol.Verify(c, "ABC123", 0, 4, LaneRole.Host, nonce, enc), "a commitment is 32 bytes and verifies with the plan and nonce it was made from");
        var other = (byte[])nonce.Clone(); other[0] ^= 1;
        Check(!LaneOnlineProtocol.Verify(c, "ABC123", 0, 4, LaneRole.Host, nonce, enc + "0.0,") && !LaneOnlineProtocol.Verify(c, "ABC123", 0, 4, LaneRole.Host, other, enc) && !LaneOnlineProtocol.Verify(c, "ABC123", 0, 5, LaneRole.Host, nonce, enc)
              && !LaneOnlineProtocol.Verify(c, "ABC123", 0, 4, LaneRole.Guest, nonce, enc) && !LaneOnlineProtocol.Verify(c, "ABC123", 1, 4, LaneRole.Host, nonce, enc) && !LaneOnlineProtocol.Verify(c, "OTHER1", 0, 4, LaneRole.Host, nonce, enc)
              && !LaneOnlineProtocol.Verify(new byte[5], "ABC123", 0, 4, LaneRole.Host, nonce, enc),
            "a changed plan, nonce, pause, role, game or room (or a short commitment) does not verify, so a commitment cannot be replayed into another pause");
        Check(!c.SequenceEqual(LaneOnlineProtocol.Commit("ABC123", 0, 4, LaneRole.Host, other, enc)) && !Encoding.UTF8.GetString(c).Contains("0.3"), "the nonce changes the commitment and the plan is not readable from it");

        var setup = Rig.Setup;
        var room = new LaneRoomMsg(3, "v", 77, setup.Lanes, setup.LaneCols, setup.PhaseTicks, setup.TickCap, setup.BaseHp, setup.MaxUnitsPerSide, "lobby", 0, "Lord Ada", "", "", 0, 5);
        var rr = LaneWire.ReadRoom(LaneWire.Room(room));
        Check(rr == room && rr.ToSetup() == setup, "the page's setup passes the room bounds and rebuilds the same setup on the other side");
        var def = LaneMatchOptions.DefaultSetup;
        Check(LaneWire.ReadRoom(LaneWire.Room(room with { Lanes = def.Lanes, LaneCols = def.LaneCols, PhaseTicks = def.PhaseTicks, TickCap = def.TickCap, BaseHp = def.BaseHp, MaxUnits = def.MaxUnitsPerSide })) is not null, "the default setup (the page's own) is inside the bounds a guest accepts");
        Check(new[] { room with { Lanes = 9 }, room with { LaneCols = 500 }, room with { MaxUnits = 100000 }, room with { State = "weird" }, room with { Gen = 99 }, room with { PhaseTicks = 5 }, room with { BaseHp = 0 }, room with { GuestCid = new string('c', 50) } }
            .All(r => LaneWire.ReadRoom(LaneWire.Room(r)) is null), "a room asking for 9 lanes, a huge map or army, an odd state, a game number out of range or a zero base is not read (a host cannot make a guest's browser build something the page never builds)");

        Check(LaneWire.ReadRoom(Encoding.UTF8.GetBytes("{not json")) is null && LaneWire.ReadRoom(Encoding.UTF8.GetBytes("{\"v\":2}")) is null && LaneWire.ReadRoom(new byte[LaneWire.MaxPayload + 1]) is null && LaneWire.ReadRoom([]) is null
              && LaneWire.ReadCommit(LaneWire.Commit(new LaneCommitMsg(new byte[32])))!.C.Length == 32 && LaneWire.ReadCommit(Encoding.UTF8.GetBytes("{\"v\":1,\"c\":\"AAAA\"}")) is null
              && LaneWire.ReadReveal(Encoding.UTF8.GetBytes("{\"v\":1,\"n\":\"AAAAAAAAAAA=\"}")) is null && LaneWire.ReadReveal(Encoding.UTF8.GetBytes("{\"v\":1,\"p\":\"x\",\"n\":\"AA==\"}")) is null
              && LaneWire.ReadEnd(Encoding.UTF8.GetBytes("{\"v\":1,\"reason\":\"win\",\"by\":\"host\"}")) is null && LaneWire.ReadHash(LaneWire.Hash(new LaneHashMsg(""))) is null
              && LaneWire.ReadJoin(LaneWire.Join(new LaneJoinMsg(new string('x', 50), "n", "v", 1))) is null && LaneWire.ReadLobby(LaneWire.Lobby(new LaneLobbyMsg("a", "n", "v", 1))) is null,
            "damaged json, a wrong version, an oversized payload, a short commitment, a reveal without a nonce, an unknown end reason, an empty hash, a long id and a bad code are all refused, never thrown");
        var rev = LaneWire.ReadReveal(LaneWire.Reveal(new LaneRevealMsg(enc, nonce, true)))!;
        Check(rev.Plan == enc && rev.Nonce.SequenceEqual(nonce) && rev.Missed && !LaneWire.ReadReveal(LaneWire.Reveal(new LaneRevealMsg(enc, nonce, false)))!.Missed, "a reveal carries the plan, the nonce and the 'you were too late' flag");
        var en = LaneWire.ReadEnd(LaneWire.End(new LaneEndMsg("left", LaneRole.Guest, LaneRole.Host)))!;
        Check(en.By == LaneRole.Guest && en.Winner == LaneRole.Host && LaneWire.ReadEnd(LaneWire.End(new LaneEndMsg("desync", LaneRole.Host, null)))!.Winner is null, "an end message names who ended it and who stayed");

        Check(LaneOnlineProtocol.SideOf(LaneRole.Host, 0) == Side.Player && LaneOnlineProtocol.SideOf(LaneRole.Guest, 0) == Side.Enemy && LaneOnlineProtocol.SideOf(LaneRole.Host, 1) == Side.Enemy && LaneOnlineProtocol.SideOf(LaneRole.Guest, 1) == Side.Player,
            "the host starts on the left base and the two swap sides at every rematch");
        Check(LaneOnlineProtocol.GameTag.StartsWith("lane1-") && LaneOnlineProtocol.Root == "plenum/v1/doctrine-lane/" && !LaneOnlineProtocol.Root.StartsWith("pzc/"), "the build tag carries the protocol number; the root is plenum/v1/doctrine-lane/ and never pzc/");
    }

    // 2. ---- creating, finding and joining a room ---------------------------------------------------------------------------------

    public static async Task Rooms(LaneOnlineProbe pa, LaneOnlineProbe pb)
    {
        Section("rooms: create, the lobby list, join with a code, refusals, a stale room, leaving");
        var pair = new Pair(pa, pb);
        pair.Code = Rig.NextCode();
        pair.Mon = new Monitor(pair.Broker, pair.Clock);
        pair.A = new LaneOnline(pair.Opts(pair.FactoryA, pa)); pair.B = new LaneOnline(pair.Opts(pair.FactoryB, pb));
        var A = pair.A; var B = pair.B;
        var lobby = new LaneLobby(new LaneLobbyOptions { Relays = new GateFactory(pair.Broker), Clock = pair.Clock });
        int changes = 0; lobby.Changed += () => changes++;
        await lobby.StartAsync();
        Check(lobby.Online && lobby.Rooms.Count == 0, "the open-rooms list connects and starts empty");

        await A.CreateRoomAsync("  Lord   Ada  ");
        Check(A.Phase == OnlinePhase.Waiting && A.IsHost && A.Code == pair.Code && A.MyName == "Lord Ada" && A.Online, "a created room waits for a guest, with the code and the cleaned name");
        var listed = lobby.Rooms;
        Check(listed.Count == 1 && listed[0].Code == pair.Code && listed[0].Host == "Lord Ada" && listed[0].AgeSeconds == 0 && changes > 0, "the room shows in the open-rooms list with its host name and an age of 0");
        string? lobbyText = pair.Broker.RetainedText(LaneTopics.Lobby(pair.Code));
        Check(lobbyText is not null && !Has(lobbyText, "seed") && !Has(lobbyText, "gcid") && !Has(lobbyText, "plan") && pair.Broker.RetainedText(new LaneTopics(pair.Code).Room) is { } rt && Has(rt, "seed"), "the lobby entry carries no match number (the room itself does)");

        var nameless = new LaneOnline(Tune(pair.Opts(new GateFactory(pair.Broker), pb), x => x.CodeSource = () => "NAMELESS1"));
        await nameless.CreateRoomAsync(null);
        Check(nameless.MyName.StartsWith("Lord ") && lobby.Rooms.Any(r => r.Host == nameless.MyName), "no name typed: a made-up 'Lord ...' name is used and listed");
        await nameless.LeaveAsync();
        Check(lobby.Rooms.Count == 1, "a room that is left leaves the list at once");

        var takers = new LaneOnline(Tune(pair.Opts(new GateFactory(pair.Broker), pb), x => x.CodeSource = () => pair.Code));
        bool threw = false; try { await takers.CreateRoomAsync("Lord Imposter"); } catch (InvalidOperationException) { threw = true; }
        Check(threw && takers.Phase == OnlinePhase.Idle, "creating a room with a code that is already taken is refused (a code is never shared by two rooms)");

        var stranger = new LaneOnline(pair.Opts(new GateFactory(pair.Broker), pb));
        Check((await stranger.JoinAsync("a/b", "x")) is { } e1 && Has(e1, "not a room code") && stranger.Phase == OnlinePhase.Idle, "a code with a slash is not a code");
        Check((await stranger.JoinAsync("NOSUCH77", "x")) is { } e2 && Has(e2, "No room with that code") && stranger.Phase == OnlinePhase.Idle, "a code nobody opened says so in plain words");

        var other = new LaneOnline(Tune(pair.Opts(new GateFactory(pair.Broker), pb), x => x.GameTag = "lane1-other"));
        Check((await other.JoinAsync(pair.Code, "x")) is { } e3 && Has(e3, "another version"), "a player on another build of the game is refused with the reason");

        // ---- join with a code (the host answers from a loop, as its ticker would)
        var j = B.JoinAsync(" " + pair.Code.ToLowerInvariant().Insert(2, "-") + " ", "  Lord  Bo ");
        while (!j.IsCompleted) { await A.AdvanceAsync(); await Task.Delay(5); }
        var err = await j;
        Check(err is null, "joining with the code typed in lower case, with a dash and spaces works: " + err);
        await A.AdvanceAsync(); await B.AdvanceAsync();
        Check(A.OpponentName == "Lord Bo" && B.OpponentName == "Lord Ada" && A.Phase is OnlinePhase.Building or OnlinePhase.Starting && B.Phase is OnlinePhase.Building or OnlinePhase.Starting && !B.IsHost, "both see the other's name and both are building the match");
        Check(lobby.Rooms.Count == 0 && pair.Broker.RetainedText(LaneTopics.Lobby(pair.Code)) is null, "once a guest is seated the room leaves the list");
        Check(A.Notices.Any(n => Has(n, "Lord Bo joined")) && B.Notices.Any(n => Has(n, "seated")), "each is told what happened in plain words");

        var third = new LaneOnline(pair.Opts(new GateFactory(pair.Broker), pb));
        Check((await third.JoinAsync(pair.Code, "Lord Cy")) is { } e4 && Has(e4, "already has two players") && third.Phase == OnlinePhase.Idle, "a third player is told the room already has two players");

        // ---- a stale room: the host stopped answering long ago and its entry is still there
        var staleCode = Rig.NextCode();
        var stale = new LaneOnline(Tune(pair.Opts(new GateFactory(pair.Broker), pa), x => x.CodeSource = () => staleCode));
        await stale.CreateRoomAsync("Lord Gone");
        pair.Clock.Advance(120_000);
        var late = new LaneOnline(pair.Opts(new GateFactory(pair.Broker), pb));
        Check((await late.JoinAsync(staleCode, "Lord Late")) is { } e5 && Has(e5, "expired"), "a room whose host has been silent for two minutes is refused as expired");
        Check(lobby.Rooms.All(r => r.Code != staleCode), "and it is no longer in the list");

        // ---- the host dies without a goodbye: the broker's last will clears the list entry
        var crashCode = Rig.NextCode();
        var crashF = new GateFactory(pair.Broker);
        var crash = new LaneOnline(Tune(pair.Opts(crashF, pa), x => x.CodeSource = () => crashCode));
        await crash.CreateRoomAsync("Lord Crash");
        Check(lobby.Rooms.Any(r => r.Code == crashCode), "a new room is listed");
        pair.Broker.Drop(crashF.Made.First(r => r.ClientId.StartsWith("lbb")).Loop);
        Check(lobby.Rooms.All(r => r.Code != crashCode), "a host whose connection dies without a goodbye leaves the list through the broker's last will");

        await pair.DisposeAsync(); await stale.LeaveAsync(); await crash.LeaveAsync(); await lobby.DisposeAsync();
        Check(pair.Broker.RetainedTopics.Count == 0, "after everyone has left the relay holds nothing: " + string.Join(", ", pair.Broker.RetainedTopics.Take(4)));
    }

    static LaneOnlineOptions Tune(LaneOnlineOptions o, Action<LaneOnlineOptions> f) { f(o); return o; }

    // 3. ---- the lobby list on its own: expiry, retained leftovers, other builds, spoofs --------------------------------------------

    public static async Task LobbyList(LaneLobbyProbe probe)
    {
        Section("lobby list: expiry, repeats, leftovers, other builds, spoofs");
        var clock = new TestClock(); var broker = new LoopbackBroker();
        var lobby = new LaneLobby(new LaneLobbyOptions { Relays = new GateFactory(broker), Clock = clock, Probe = probe });
        int changes = 0; lobby.Changed += () => changes++;
        await lobby.StartAsync();
        var spy = broker.NewClient("spy"); await spy.ConnectAsync();
        var fac = new GateFactory(broker);
        var x = new LaneLobbyBeacon(fac, clock, "AAAA11", "Lord X", LaneOnlineProtocol.GameTag);
        await x.StartAsync();
        Check(lobby.Rooms.Count == 1 && lobby.Rooms[0].Code == "AAAA11" && lobby.Rooms[0].Host == "Lord X", "a room is listed when its host starts the beacon");
        clock.Advance(30_000); lobby.Tick();
        Check(lobby.Rooms.Count == 1 && lobby.Rooms[0].AgeSeconds == 30, "after 30 s it is still listed and its age shows 30 s");
        await x.BeatAsync();
        Check(lobby.Rooms.Count == 1 && lobby.Rooms[0].AgeSeconds == 0, "the host's repeat (every 20 s) makes it fresh again");
        clock.Advance(10_000); await x.BeatAsync();
        Check(lobby.Rooms[0].AgeSeconds == 10, "a repeat before its time is not sent");
        clock.Advance(80_000); lobby.Tick();
        Check(lobby.Rooms.Count == 0, "80 s without a repeat: the room has expired from the list (stale rooms are not shown)");
        await x.StopAsync();

        await x.StartAsync(); await x.StopAsync();
        Check(lobby.Rooms.Count == 0 && broker.RetainedText(LaneTopics.Lobby("AAAA11")) is null, "the host clears the entry when it stops");

        var y = new LaneLobbyBeacon(fac, clock, "BBBB22", "Lord Y", LaneOnlineProtocol.GameTag);
        await y.StartAsync();
        Check(lobby.Rooms.Count == 1, "another room is listed");
        broker.Drop(fac.Made.Last().Loop);
        Check(lobby.Rooms.Count == 0, "a host that dies leaves the list through the last will");

        // retained leftovers seen by a list that connects LATER
        long now = clock.UnixMs;
        async Task Put(string code, string host, long ts, string ver, string? topicCode = null)
            => await spy.PublishAsync(LaneTopics.Lobby(topicCode ?? code), LaneWire.Lobby(new LaneLobbyMsg(code, host, ver, ts)), true, 1);
        await Put("OLDONE", "Lord Old", now - 200_000, LaneOnlineProtocol.GameTag);
        await Put("FRESH1", "Lord Fresh", now - 10_000, LaneOnlineProtocol.GameTag);
        await Put("FUTURE", "Lord Future", now + 3_600_000, LaneOnlineProtocol.GameTag);
        await Put("OTHERB", "Lord Other", now, "lane9-other");
        await Put("SPOOFED", "Lord Spoof", now, LaneOnlineProtocol.GameTag, "REALONE");
        await spy.PublishAsync(LaneTopics.Lobby("BADJSON"), Encoding.UTF8.GetBytes("{oops"), true, 1);
        await spy.PublishAsync(LaneOnlineProtocol.LobbyRoot + "ab", LaneWire.Lobby(new LaneLobbyMsg("AB12CD", "Lord Short", LaneOnlineProtocol.GameTag, now)), true, 1);
        await Put("CTRL01", "Lord\u0007 Ctrl<b>", now, LaneOnlineProtocol.GameTag);
        var late = new LaneLobby(new LaneLobbyOptions { Relays = new GateFactory(broker), Clock = clock, Probe = probe });
        await late.StartAsync();
        var rooms = late.Rooms;
        Check(rooms.All(r => r.Code != "OLDONE"), "a leftover entry 200 s old is not listed by a list that connects later");
        Check(rooms.Any(r => r.Code == "FRESH1" && r.AgeSeconds == 10), "a retained entry 10 s old is listed with its age of 10 s");
        Check(rooms.Any(r => r.Code == "FUTURE" && r.AgeSeconds == 0), "an entry stamped in the future counts as just now (a fast clock cannot make a room last for ever)");
        Check(rooms.All(r => r.Code != "OTHERB"), "a room of another build is not listed");
        Check(rooms.All(r => r.Code != "REALONE" && r.Code != "SPOOFED"), "a message whose code differs from its topic is not believed");
        Check(rooms.All(r => r.Code != "BADJSON" && r.Code != "AB12CD") && rooms.All(r => !r.Host.Any(char.IsControl) && !r.Host.Contains('<')), "damaged json and a bad topic are ignored, and control characters and markup are removed from names");
        clock.Advance(80_000); late.Tick();
        Check(late.Rooms.Count == 0, "everything left over expires 75 s after it was first heard, even the one stamped in the future");

        for (int i = 0; i < 35; i++) { clock.Advance(1000); await Put("CAPP" + (10 + i), "Lord " + i, clock.UnixMs, LaneOnlineProtocol.GameTag); }
        Check(late.Rooms.Count == 30 && late.Rooms[0].Code == "CAPP44", "the list shows at most 30 rooms, the newest first");
        await lobby.DisposeAsync(); await late.DisposeAsync();
        Check(!lobby.Online, "closing the list closes its connection");
    }
}
