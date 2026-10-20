using System.Security.Cryptography;
using Doctrine;
using ShowroomPlenum;

namespace ShowroomDoctrine.Duel;

// THE DOCTRINE DUEL SERVICE (UI-free): the state machine of one seat of a two-seat duel over the Plenum relay (MQTT over a WebSocket), topics ONLY under
// plenum/v1/doctrine/<code>/. Design: Doctrine\todo\duel-design-2026-10-19.md; library seam: Doctrine\CONTRACT.md "The opponent seam and the duel".
//
// HOW IT WORKS. Every message that arrives is only RECORDED (Absorb: parse, keep the first of each (kind, round, seat), ignore the rest). A single serialized pump then
// RECONCILES: it looks at what is known and does whatever step is now possible (commit, reveal, judge, fight, apply, open the next round). Because progress is a function of
// what is known, a duplicate does nothing, an early message waits for its turn, and a reconnect needs only the retained messages to be replayed. Nothing blocks a thread.
//
// ROLES. Seat A (the host) is also the AUTHORITY: it holds the DuelGame, judges the reveals, fights each battle ONCE and publishes the result. Seat B applies that result
// to its own locally built seat and checks the digest of ITS OWN army. Both seats hold a local DuelSeat the page plays on.
//
// TIME. The host opens a round with an absolute deadline (its Unix clock); a guest maps it to its own clock using the difference seen on the live message that opened the
// round. At the deadline each seat seals itself with the auto picker. The host waits GraceMs more for the other seat, then seals for it (a commitment and a reveal made by
// the host, flagged). Nothing here sleeps: the page calls Tick() (or RunTickerAsync) and the clock (IPlenumClock) is a service so a proof can drive time by hand.

public sealed class DoctrineDuel : IDoctrineDuel
{
    sealed class MyRound
    {
        public DuelPicks Picks = null!;
        public byte[] Canon = null!, Nonce = null!, Commit = null!;
        public bool Auto, ByHost, CommitSent, RevealSent;
    }
    sealed record Judged(DuelPicks Picks, bool Forfeit, string Why, bool Auto);

    readonly DuelOptions _o;
    readonly object _k = new();
    readonly List<string> _notices = [];

    // ---- identity, connection
    DuelTopics? _t;
    IPlenumRelay? _relay;
    string _cid = "", _code = "", _myName = "", _oppName = "";
    Seat _me;
    bool _joined;                         // Create/Join has put this seat in a room
    bool _online;
    bool _needRepublish;
    long _resyncStart, _resyncDeadline;
    string? _staleGoTopic;

    // ---- knowledge (under _k): first of each (kind, round, seat) wins
    RoomMsg? _room; long _skew; long _roomMs;
    readonly Dictionary<string, JoinMsg> _joins = new(); readonly List<string> _joinOrder = [];
    readonly bool[] _ready = new bool[2];
    readonly PresenceMsg?[] _presence = new PresenceMsg?[2]; readonly long[] _presenceMs = new long[2];
    readonly Dictionary<(int, Seat), CommitMsg> _commits = new();
    readonly Dictionary<(int, Seat), RevealMsg> _reveals = new();
    readonly Dictionary<int, List<ResultHeader>> _resHdr = new(); readonly Dictionary<(int, int), string> _resChunks = new();
    readonly HashSet<ResultHeader> _badHdr = [];
    readonly HashSet<(int, Seat)> _next = new();
    EndMsg? _endMsg; readonly bool[] _rematch = new bool[2]; string? _rematchGo;
    readonly Dictionary<string, byte[]> _outbox = new();

    // ---- the match
    DuelPhase _phase = DuelPhase.Idle;
    int _round, _seed, _rounds, _timerSec, _rev;
    string _roomState = "lobby"; long _openMs, _deadlineAuth, _deadlineLocal;
    string _guestCid = "";
    DoctrineGame? _game; DuelSeat? _seat; DuelGame? _duel; DuelSeat? _shadow;
    readonly Dictionary<int, MyRound> _mine = new(), _hostOpp = new();
    readonly Dictionary<(int, Seat), Judged> _judged = new();
    readonly Dictionary<int, long> _bothAt = new(), _resultAt = new();
    readonly Dictionary<int, List<string>> _roundNotes = new();
    readonly Dictionary<(int, Seat), DuelPicks> _pickHist = new();
    readonly Dictionary<int, DuelRoundResult> _results = new();
    readonly HashSet<int> _played = [], _applied = [];
    readonly List<DuelRoundOutcome> _history = [];
    DuelEnd? _end;
    bool _autoBusy;
    int _errors;
    long _lastSecond = -2;

    // ---- the pump
    int _running; volatile bool _dirty; volatile bool _changed; Task _pump = Task.CompletedTask;

    public DoctrineDuel(DuelOptions options)
    {
        _o = options ?? throw new ArgumentNullException(nameof(options));
        if (_o.GameFactory is null || _o.Relays is null) throw new ArgumentException("GameFactory and Relays are required", nameof(options));
    }

    long Mono => _o.Clock.NowMs;
    long Unix => _o.Clock.UnixMs;
    Seat Opp => _me.Other();

    // ================================================================================================ the surface

    public event Action? Changed;

    public DuelPhase Phase => _phase;
    public Seat? Me => _joined ? _me : null;
    public bool IsHost => _joined && _me == Seat.A;
    public string Code => _code;
    public string MyName => _myName;
    public string OpponentName => _oppName;
    public int Seed => _seed;
    public int Round => _round;
    public int Rounds => _rounds;
    public int TimerSec => _timerSec;
    public bool Online => _online;
    public bool MyReady { get { lock (_k) return _joined && _ready[(int)_me]; } }
    public bool OpponentReady { get { lock (_k) return _joined && _ready[(int)Opp]; } }
    public DuelSeat? LocalSeat => _seat;
    public DuelRoundOutcome? Outcome => _history.Count == 0 ? null : _history[^1];
    public IReadOnlyList<DuelRoundOutcome> History => _history;
    public DuelEnd? End => _end;
    public IReadOnlyList<string> Notices { get { lock (_k) return _notices.ToArray(); } }
    public int Errors => _errors;
    public int IdleYou => Outcome?.IdleYou ?? 0;
    public int IdleThem => Outcome?.IdleThem ?? 0;
    public bool OpponentContinued { get { lock (_k) return _joined && _next.Contains((_round, Opp)); } }

    public long RemainingMs => _phase is DuelPhase.Sealing or DuelPhase.Sealed or DuelPhase.Resolving ? Math.Max(0, _deadlineLocal - Unix) : -1;

    public OpponentState Opponent
    {
        get
        {
            if (!_joined) return OpponentState.Absent;
            if (IsHost && _guestCid.Length == 0) return OpponentState.Absent;
            lock (_k)
            {
                if (_presence[(int)Opp] is { On: false }) return OpponentState.Offline;
                if (IdleThem >= DuelGame.IdleAfter) return OpponentState.Idle;
                if (_phase is DuelPhase.Sealing or DuelPhase.Sealed or DuelPhase.Resolving && _commits.ContainsKey((_round, Opp))) return OpponentState.Sealed;
                return OpponentState.Choosing;
            }
        }
    }

    // ================================================================================================ creating, joining, leaving

    public async Task CreateRoomAsync(string name, int rounds, int timerSec)
    {
        if (_phase != DuelPhase.Idle) throw new InvalidOperationException("already in a room");
        if (!DuelProtocol.RoundChoices.Contains(rounds)) throw new ArgumentOutOfRangeException(nameof(rounds), "rounds must be 3, 5 or 8");
        if (timerSec is < 10 or > 600) throw new ArgumentOutOfRangeException(nameof(timerSec), "the timer must be 10 to 600 seconds");
        string? code = null;
        for (int attempt = 0; attempt < 5 && code is null; attempt++)
        {
            var tryCode = DuelProtocol.Normalize(_o.CodeSource?.Invoke()) ?? DuelProtocol.NewCode(_o.Below);
            _me = Seat.A;
            await ConnectAsync(tryCode, announce: false);
            if (_o.CreateProbeMs > 0 && await WaitRealAsync(() => { lock (_k) return _room is not null; }, _o.CreateProbeMs))
            {
                await DropConnectionAsync(clear: false);        // someone else's room already holds this code
                ResetState();
                if (_o.CodeSource is not null) throw new InvalidOperationException("the chosen room code is taken");
                continue;
            }
            code = tryCode;
        }
        if (code is null) throw new IOException("could not find a free room code");
        await Announce();
        await StartHostAsync(name, rounds, timerSec);
    }

    async Task StartHostAsync(string name, int rounds, int timerSec)
    {
        _me = Seat.A; _joined = true; _myName = Clean(name, "Host");
        _rounds = rounds; _timerSec = timerSec; _seed = _o.SeedSource?.Invoke() ?? RandomNumberGenerator.GetInt32(1, int.MaxValue);
        _game = _o.GameFactory(rounds); _duel = _game.NewDuel(_seed); _seat = _game.NewDuelSeat(_seed, Seat.A);
        _phase = DuelPhase.Lobby; _roomState = "lobby";
        await PublishRoomAsync();
        Notice($"Room {_code} is open. Share the code; the second seat joins with it.");
        Raise();
    }

    public async Task<string?> JoinAsync(string code, string name)
    {
        if (_phase != DuelPhase.Idle) throw new InvalidOperationException("already in a room");
        var c = DuelProtocol.Normalize(code);
        if (c is null) return "That is not a room code (4 to 12 letters or digits).";
        _me = Seat.B;
        await ConnectAsync(c, announce: false);
        if (!await WaitRealAsync(() => { lock (_k) return _room is not null; }, _o.JoinWaitMs))
        {
            await DropConnectionAsync(clear: true); ResetState();
            return "No room with that code was found. Check the code, and that the host is still there.";
        }
        RoomMsg room; lock (_k) room = _room!;
        string? refuse = null;
        if (room.Ver != _o.GameTag) refuse = "This room runs another build of the game, so the two seats would not agree. Reload the page and try again.";
        else if (room.State != "lobby") refuse = "That duel has already started.";
        else if (room.GuestCid.Length > 0 && room.GuestCid != _cid) refuse = "That room already has two seats.";
        else if (!DuelProtocol.RoundChoices.Contains(room.Rounds)) refuse = "That room uses a number of rounds this page does not play.";
        if (refuse is not null) { await DropConnectionAsync(clear: true); ResetState(); return refuse; }

        _joined = true; _myName = Clean(name, "Guest"); _oppName = Clean(room.Host, "Host");
        _rounds = room.Rounds; _timerSec = room.TimerSec; _seed = room.Seed;
        _game = _o.GameFactory(_rounds); _seat = _game.NewDuelSeat(_seed, Seat.B);
        _phase = DuelPhase.Lobby;
        await Announce();
        await Send(_t!.Join(_cid), DuelWire.Join(new JoinMsg(_cid, _myName)));
        Notice($"Joined room {_code}. Waiting for the host to seat you.");
        Kick();
        return null;
    }

    public async Task SetReadyAsync(bool ready = true)
    {
        if (_phase != DuelPhase.Lobby || _t is null) return;
        await Send(_t.Ready(_me), DuelWire.Flag("ready", ready));
        Kick();
    }

    public async Task LeaveAsync()
    {
        try
        {
            if (_t is not null && _relay is not null && _joined)
            {
                bool finished = _end is { Kind: DuelEndKind.Final } || (_phase == DuelPhase.Result && (Outcome?.Final ?? false));
                if (_phase != DuelPhase.Idle && !finished && _end is null)
                {
                    var (fa, fb) = Standing();
                    try { await Send(_t.End, DuelWire.End(new EndMsg(IsHost ? "hostLeft" : "guestLeft", _me, fa, fb, IsHost ? "The host left the duel." : "The other seat left the duel."))); } catch { }
                }
                await DropConnectionAsync(clear: true);
            }
            else await DropConnectionAsync(clear: false);
        }
        finally { ResetState(); Raise(); }
    }

    public async ValueTask DisposeAsync() => await LeaveAsync();

    // ================================================================================================ connection

    async Task ConnectAsync(string code, bool announce = true)
    {
        _t = new DuelTopics(code); _code = code;
        _cid = "dd" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var relay = _o.Relays.Create(_cid);
        _relay = relay;
        relay.OnMessage += OnRelayMessage; relay.OnDisconnect += OnRelayDisconnect; relay.OnReconnected += OnRelayReconnected;
        var will = new RelayWill(_t.Presence(_me), DuelWire.Presence(new PresenceMsg(false, _cid, 0)), true, 1);
        try
        {
            await relay.ConnectAsync(will);
            _online = true;
            await relay.SubscribeAsync(_t.Filter);
            if (announce) await Announce();
        }
        catch { await DropConnectionAsync(clear: false); ResetState(); throw; }
    }

    Task Announce() => Send(_t!.Presence(_me), DuelWire.Presence(new PresenceMsg(true, _cid, Unix)));

    /// <summary>Close the connection. With <paramref name="clear"/>, first empty every retained message this seat put on the relay (the room leaves nothing behind but what is named in keep).</summary>
    async Task DropConnectionAsync(bool clear)
    {
        var relay = _relay; if (relay is null) return;
        string? presenceTopic = _t?.Presence(_me);
        try
        {
            if (clear && relay.State == RelayState.Connected)
            {
                string[] topics; lock (_k) topics = _outbox.Keys.ToArray();
                foreach (var tp in topics) { try { await relay.PublishAsync(tp, ReadOnlyMemory<byte>.Empty, true, 1); } catch { } }
            }
            try { await relay.DisconnectAsync(); } catch { }
            if (clear && presenceTopic is not null) await ClearPresenceAsync(presenceTopic);
        }
        finally
        {
            relay.OnMessage -= OnRelayMessage; relay.OnDisconnect -= OnRelayDisconnect; relay.OnReconnected -= OnRelayReconnected;
            try { await relay.DisposeAsync(); } catch { }
            _relay = null; _online = false;
        }
    }

    /// <summary>The socket is dropped right after the goodbye, and a broker can read that as a lost link and publish the last will over the cleared presence. So once the first
    /// connection is gone, a second short one (no will) clears the presence topic again.</summary>
    async Task ClearPresenceAsync(string topic)
    {
        IPlenumRelay? r = null;
        try
        {
            await Task.Delay(400);
            r = _o.Relays.Create(_cid + "x");
            await r.ConnectAsync();
            await r.PublishAsync(topic, ReadOnlyMemory<byte>.Empty, true, 1);
            await Task.Delay(150);
            await r.DisconnectAsync();
        }
        catch { }
        finally { if (r is not null) { try { await r.DisposeAsync(); } catch { } } }
    }

    void ResetState()
    {
        lock (_k)
        {
            _t = null; _code = ""; _cid = ""; _joined = false; _online = false; _needRepublish = false; _resyncDeadline = 0;
            _myName = ""; _oppName = ""; _room = null; _skew = 0; _roomMs = 0;
            _joins.Clear(); _joinOrder.Clear(); Array.Clear(_ready); Array.Clear(_presence); Array.Clear(_presenceMs);
            _commits.Clear(); _reveals.Clear(); _resHdr.Clear(); _resChunks.Clear(); _next.Clear(); _endMsg = null; Array.Clear(_rematch); _rematchGo = null; _outbox.Clear();
            _phase = DuelPhase.Idle; _round = _seed = _rounds = _timerSec = _rev = 0; _roomState = "lobby"; _openMs = _deadlineAuth = _deadlineLocal = 0; _guestCid = "";
            _game = null; _seat = null; _duel = null; _shadow = null;
            _mine.Clear(); _hostOpp.Clear(); _judged.Clear(); _bothAt.Clear(); _resultAt.Clear(); _roundNotes.Clear(); _pickHist.Clear(); _results.Clear();
            _played.Clear(); _applied.Clear(); _badHdr.Clear(); _history.Clear(); _end = null; _autoBusy = false; _lastSecond = -2;
        }
    }

    void OnRelayDisconnect(string reason) { _online = false; Notice("The connection to the relay was lost; trying to get it back."); Raise(); }
    void OnRelayReconnected() { _online = true; _resyncStart = Mono; _resyncDeadline = _resyncStart + _o.ResyncWaitMs; _needRepublish = true; Notice("Back on the relay; catching up."); Kick(); }

    void OnRelayMessage(RelayMessage m)
    {
        try { Absorb(m.Topic, m.Payload, m.Retained); } catch { }
        Kick();
    }

    static string Clean(string? s, string fallback) { var c = DuelWire.Clean(s ?? ""); return c.Length == 0 ? fallback : c; }

    static async Task<bool> WaitRealAsync(Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (cond()) return true;
            if (sw.ElapsedMilliseconds >= ms) return false;
            await Task.Delay(15);
        }
    }

    // ================================================================================================ sending

    /// <summary>Publish a retained message of THIS room (QoS 1), remember it for a reconnect, and record it as known. Refuses any topic outside this room's root.</summary>
    async Task Send(string topic, byte[] payload, bool retain = true)
    {
        var t = _t ?? throw new InvalidOperationException("not in a room");
        if (DoctrineDuelFaults.WrongTopic && topic == t.Presence(_me)) topic = "plenum/v1/doctrine-" + t.Code + "/presence";
        else if (!topic.StartsWith(t.Root, StringComparison.Ordinal)) throw new InvalidOperationException("topic outside the room: " + topic);
        if (retain) lock (_k) _outbox[topic] = payload;
        var relay = _relay;
        if (relay is not null) await relay.PublishAsync(topic, payload, retain, 1);
        Absorb(topic, payload, false);
    }

    async Task PublishRoomAsync()
    {
        _rev++;
        var m = new RoomMsg(_rev, _o.GameTag, _seed, _rounds, _timerSec, _roomState, _round, _openMs, _deadlineAuth, Unix, _myName, _oppName, _guestCid);
        await Send(_t!.Room, DuelWire.Room(m));
    }

    // ================================================================================================ recording what arrives

    void Absorb(string topic, byte[] payload, bool retained)
    {
        var t = _t;
        if (t is null || payload.Length == 0 || !t.TryParse(topic, out var p)) return;
        lock (_k)
        {
            switch (p.Kind)
            {
                case DuelKind.Room:
                {
                    var m = DuelWire.ReadRoom(payload); if (m is null) return;
                    if (_room is null || m.Rev > _room.Rev)
                    {
                        _room = m;
                        if (!retained) _skew = m.Now - Unix;       // a retained room is old: its clock reading says nothing about the difference now
                    }
                    _roomMs = Mono;
                    break;
                }
                case DuelKind.Join:
                {
                    var m = DuelWire.ReadJoin(payload); if (m is null || m.Cid != p.Id) return;
                    if (_joins.TryAdd(m.Cid, m)) _joinOrder.Add(m.Cid); else _joins[m.Cid] = m;
                    break;
                }
                case DuelKind.Ready: { var f = DuelWire.ReadFlag(payload, "ready"); if (f is not null) _ready[(int)p.Seat] = f.Value; break; }
                case DuelKind.Presence: { var m = DuelWire.ReadPresence(payload); if (m is not null) { _presence[(int)p.Seat] = m; _presenceMs[(int)p.Seat] = Mono; } break; }
                case DuelKind.Commit:
                {
                    var m = DuelWire.ReadCommit(payload); if (m is null || (_rounds > 0 && p.Round >= _rounds)) return;
                    var key = (p.Round, p.Seat);
                    // The first stands; the one exception is the host's own commitment for an absent seat, which replaces a late one from that seat (the host's reveal goes with it).
                    if (!_commits.TryGetValue(key, out var old) || (m.ByHost && !old.ByHost) || DoctrineDuelFaults.NoIdempotence) _commits[key] = m;
                    break;
                }
                case DuelKind.Reveal:
                {
                    var m = DuelWire.ReadReveal(payload); if (m is null || (_rounds > 0 && p.Round >= _rounds)) return;
                    var key = (p.Round, p.Seat);
                    if (!_reveals.ContainsKey(key) || DoctrineDuelFaults.NoIdempotence) _reveals[key] = m;
                    break;
                }
                case DuelKind.Result:
                {
                    var h = DuelWire.ReadResultHeader(payload); if (h is null || h.Round != p.Round || (_rounds > 0 && p.Round >= _rounds)) return;
                    // Every distinct header is kept (at most 4 per round): one that does not check out against its own digest is skipped when the result is read, so a damaged or hostile
                    // header cannot hide the real one.
                    if (!_resHdr.TryGetValue(p.Round, out var list)) _resHdr[p.Round] = list = [];
                    if (list.Count < 4 && !list.Any(x => x.Digest == h.Digest && x.Chunks == h.Chunks && x.Length == h.Length && x.Inline == h.Inline)) list.Add(h);
                    break;
                }
                case DuelKind.Chunk: { var s = DuelWire.ReadChunk(payload, out int idx); if (s is not null && idx is >= 0 and < 4096) { _resChunks[(p.Round, idx)] = s; _badHdr.RemoveWhere(x => x.Round == p.Round); } break; }
                case DuelKind.Next: _next.Add((p.Round, p.Seat)); break;
                case DuelKind.End: { var m = DuelWire.ReadEnd(payload); if (m is not null) _endMsg ??= m; break; }
                case DuelKind.Rematch: { var f = DuelWire.ReadFlag(payload, "want"); if (f is not null) _rematch[(int)p.Seat] = f.Value; break; }
                case DuelKind.RematchGo: { var c = DuelWire.ReadRematchGo(payload); if (c is not null) _rematchGo ??= c; break; }
            }
        }
    }

    bool HasCommit(int r, Seat s) { lock (_k) return _commits.ContainsKey((r, s)); }
    bool BothCommits(int r) { lock (_k) return _commits.ContainsKey((r, Seat.A)) && _commits.ContainsKey((r, Seat.B)); }

    // ================================================================================================ the pump

    void Kick()
    {
        _dirty = true;
        if (Interlocked.CompareExchange(ref _running, 1, 0) == 0) _pump = PumpAsync();
    }

    async Task PumpAsync()
    {
        try
        {
            while (_dirty)
            {
                _dirty = false;
                try { while (await StepOnceAsync().ConfigureAwait(false)) { } }
                catch (Exception e)
                {
                    Interlocked.Increment(ref _errors);
                    Notice("Internal fault: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }
        finally { Volatile.Write(ref _running, 0); }
        if (_dirty) Kick();
        if (_changed) Raise();
    }

    /// <summary>Wait until the state machine has nothing left to do with what it knows (for a proof, and for a clean leave).</summary>
    public async Task SettleAsync()
    {
        for (int i = 0; i < 100000; i++)
        {
            var p = _pump;
            if (Volatile.Read(ref _running) == 0 && !_dirty) return;
            try { await p.ConfigureAwait(false); } catch { }
            await Task.Yield();
        }
    }

    void Raise() { _changed = false; try { Changed?.Invoke(); } catch { } }
    void Touch() => _changed = true;

    void Notice(string text)
    {
        lock (_k) { _notices.Add(text); if (_notices.Count > 40) _notices.RemoveAt(0); }
        _changed = true;
    }

    public void Tick()
    {
        Kick();
        long rem = RemainingMs, sec = rem >= 0 ? rem / 1000 : -1;
        if (sec != _lastSecond) { _lastSecond = sec; Raise(); }
    }

    public async Task RunTickerAsync(CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) { await Task.Delay(250, ct); Tick(); } }
        catch (OperationCanceledException) { }
    }

    // ================================================================================================ the state machine

    async Task<bool> StepOnceAsync()
    {
        if (_phase == DuelPhase.Idle || _t is null) return false;
        bool did = false;
        if (_needRepublish) { _needRepublish = false; did |= await RepublishAsync(); }
        did |= CheckEnd();
        if (_phase == DuelPhase.Ended) return did | await RematchStepAsync();
        long now = Unix;
        switch (_phase)
        {
            case DuelPhase.Lobby: did |= IsHost ? await LobbyHostAsync() : LobbyGuest(); break;
            case DuelPhase.Sealing or DuelPhase.Sealed or DuelPhase.Resolving: did |= await RoundStepAsync(now); break;
            case DuelPhase.Result: did |= await ResultStepAsync(now); break;
        }
        return did;
    }

    /// <summary>After a reconnect: say again everything this seat said (idempotent), and that it is online.</summary>
    async Task<bool> RepublishAsync()
    {
        var relay = _relay; if (relay is null) return false;
        KeyValuePair<string, byte[]>[] all; lock (_k) all = _outbox.ToArray();
        await Send(_t!.Presence(_me), DuelWire.Presence(new PresenceMsg(true, _cid, Unix)));
        foreach (var (topic, payload) in all) if (topic != _t.Presence(_me)) await relay.PublishAsync(topic, payload, true, 1);
        return true;
    }

    // ---- the end of a match: a message, a host that stayed away, a room that did not come back --------------------------------

    bool CheckEnd()
    {
        if (_phase is DuelPhase.Ended or DuelPhase.Idle) return false;
        bool finished = _phase == DuelPhase.Result && (Outcome?.Final ?? false);
        EndMsg? em; PresenceMsg? hostPresence; long hostSince; long roomMs;
        lock (_k) { em = _endMsg; hostPresence = _presence[(int)Seat.A]; hostSince = _presenceMs[(int)Seat.A]; roomMs = _roomMs; }
        if (!finished && em is not null && em.By != _me && em.Reason is "hostLeft" or "guestLeft")
        {
            Finish(em.Reason == "hostLeft" ? DuelEndKind.HostLeft : DuelEndKind.GuestLeft, em.Text.Length > 0 ? em.Text : (em.Reason == "hostLeft" ? "The host left the duel." : "The other seat left the duel."));
            return true;
        }
        if (!finished && !IsHost && hostPresence is { On: false } && Mono - hostSince >= _o.AuthorityGraceMs)
        {
            Finish(DuelEndKind.HostLeft, "The host is gone, and the duel cannot go on without them (they run the battles).");
            return true;
        }
        if (!finished && _resyncDeadline > 0 && Mono >= _resyncDeadline)
        {
            _resyncDeadline = 0;
            if (roomMs < _resyncStart) { Finish(DuelEndKind.RoomGone, "The room is gone: after the connection came back, nothing of it was left on the relay."); return true; }
        }
        return false;
    }

    (int, int) Standing() { var o = Outcome; return o is null ? (0, 0) : (o.Result.Score.FieldsA, o.Result.Score.FieldsB); }

    void Finish(DuelEndKind kind, string text)
    {
        var score = Outcome?.Result.Score ?? new DuelScore(0, 0, 0, 0);
        bool? won = null; DuelDecider? by = null;
        if (kind == DuelEndKind.Final) { by = score.DecidedBy; won = score.Winner is { } w ? w == _me : null; }
        _end = new DuelEnd(kind, text, score, won, by);
        _phase = DuelPhase.Ended; _deadlineLocal = 0;
        Notice(text); Touch();
    }

    string FinalText(DuelScore s)
    {
        int mine = s.Fields(_me), theirs = s.Fields(Opp);
        return s.Winner switch
        {
            null => $"The duel is drawn: {mine} fields each, and equal power in the field.",
            var w when s.DecidedBy == DuelDecider.Fields => w == _me ? $"You won the duel, {mine} fields to {theirs}." : $"You lost the duel, {mine} fields to {theirs}.",
            var w => w == _me ? $"Fields were level at {mine}, and you hold more power at the end, so the duel is yours." : $"Fields were level at {mine}, and the other seat holds more power at the end, so the duel is theirs.",
        };
    }

    // ---- lobby --------------------------------------------------------------------------------------------------------------

    async Task<bool> LobbyHostAsync()
    {
        bool did = false;
        string first; JoinMsg? j = null;
        lock (_k) { first = _joinOrder.Count > 0 ? _joinOrder[0] : ""; if (first.Length > 0) j = _joins[first]; }
        if (_guestCid.Length == 0 && j is not null)
        {
            _guestCid = j.Cid; _oppName = j.Name;
            await PublishRoomAsync(); did = true;
            Notice($"{_oppName} joined.");
            if (_staleGoTopic is not null && _relay is not null) { try { await _relay.PublishAsync(_staleGoTopic, ReadOnlyMemory<byte>.Empty, true, 1); } catch { } _staleGoTopic = null; }
        }
        else if (_guestCid.Length > 0 && j is not null && j.Cid == _guestCid && j.Name != _oppName) { _oppName = j.Name; await PublishRoomAsync(); did = true; }
        bool go; lock (_k) go = _ready[(int)Seat.A] && _ready[(int)Seat.B];
        if (_guestCid.Length > 0 && go)
        {
            _roomState = "play"; _round = 0; _openMs = Unix; _deadlineAuth = _openMs + _timerSec * 1000L;
            await PublishRoomAsync();
            BeginRound(0); did = true;
        }
        return did;
    }

    bool LobbyGuest()
    {
        RoomMsg? room; lock (_k) room = _room;
        if (room is null) return false;
        if (room.GuestCid.Length > 0 && room.GuestCid != _cid) { Finish(DuelEndKind.Refused, "That room already has two seats."); return true; }
        if (room.GuestCid == _cid && room.State == "play")
        {
            _oppName = Clean(room.Host, "Host");
            BeginRound(room.Round); return true;
        }
        if (room.GuestCid == _cid && _oppName != Clean(room.Host, "Host")) { _oppName = Clean(room.Host, "Host"); Touch(); }
        return false;
    }

    void BeginRound(int r)
    {
        _round = r; _phase = DuelPhase.Sealing;
        if (IsHost) _deadlineLocal = _deadlineAuth;
        else
        {
            RoomMsg? room; lock (_k) room = _room;
            _deadlineLocal = room is not null && room.Round == r ? room.Deadline - _skew : Unix;
        }
        Touch();
    }

    // ---- a round -----------------------------------------------------------------------------------------------------------

    public Task<bool> SealAsync(DuelPicks picks)
    {
        if (_phase != DuelPhase.Sealing || _autoBusy || _t is null) return Task.FromResult(false);
        int r = _round;
        lock (_k)
        {
            if (_mine.ContainsKey(r) || _commits.TryGetValue((r, _me), out var c) && c.ByHost) return Task.FromResult(false);
            _mine[r] = NewMine(r, picks, auto: false);
        }
        Kick();
        return Task.FromResult(true);
    }

    MyRound NewMine(int r, DuelPicks picks, bool auto, bool byHost = false, Seat? seat = null)
    {
        var canon = picks.Canonical();
        var nonce = RandomNumberGenerator.GetBytes(24);
        var commit = DuelPicks.Commit(canon, nonce, r, seat ?? _me);
        return new MyRound { Picks = picks, Canon = canon, Nonce = nonce, Commit = commit, Auto = auto, ByHost = byHost };
    }

    async Task<bool> RoundStepAsync(long now)
    {
        bool did = false; int r = _round;

        // 0. the result is already here (the guest's normal path; the host's own when it sealed for a seat that was away)
        if (!IsHost && !_applied.Contains(r) && ResultReady(r, out var hdr0, out var res0))
            return await ApplyResultAsync(r, hdr0!, res0!);

        // 1. the clock ran out on a seat that has not sealed
        bool have; lock (_k) have = _mine.ContainsKey(r) || (_commits.TryGetValue((r, _me), out var c0) && c0.ByHost);
        // (an offline seat does not seal itself: it cannot say so, and the host will seal for it; on return it seals itself only if the host has not)
        if (!have && !_autoBusy && _online && !DoctrineDuelFaults.IgnoreTimeout && now >= _deadlineLocal) { await AutoSealAsync(r); did = true; }

        // 2. my commitment goes out (unless the host already sealed for me)
        MyRound? my; lock (_k) _mine.TryGetValue(r, out my);
        if (my is not null && !my.CommitSent)
        {
            bool superseded; lock (_k) superseded = _commits.TryGetValue((r, _me), out var cc) && cc.ByHost;
            my.CommitSent = true;
            if (!superseded)
            {
                await Send(_t!.Commit(r, _me), DuelWire.Commit(new CommitMsg(my.Commit, my.Auto, false)));
                if (_phase == DuelPhase.Sealing) _phase = DuelPhase.Sealed;
                did = true;
            }
        }

        // 3. both commitments are in: reveal mine (never before)
        bool both = BothCommits(r);
        if (both && !_bothAt.ContainsKey(r)) { _bothAt[r] = now; did = true; }
        if (my is not null && my.CommitSent && !my.RevealSent && (both || DoctrineDuelFaults.RevealBeforeCommits) && CommitIsMine(r, my))
        {
            my.RevealSent = true;
            var (canon, nonce) = _o.RevealTamper?.Invoke(r, my.Canon, my.Nonce) ?? (my.Canon, my.Nonce);
            await Send(_t!.Reveal(r, _me), DuelWire.Reveal(new RevealMsg(canon, nonce, my.Auto, false)));
            did = true;
        }
        if (both && _phase is DuelPhase.Sealing or DuelPhase.Sealed) { _phase = DuelPhase.Resolving; did = true; }

        if (IsHost) did |= await HostRoundAsync(r, now);
        return did;
    }

    bool CommitIsMine(int r, MyRound my) { lock (_k) return _commits.TryGetValue((r, _me), out var c) && c.C.AsSpan().SequenceEqual(my.Commit); }

    async Task AutoSealAsync(int r)
    {
        _autoBusy = true;
        DuelPicks picks;
        try { picks = await _o.AutoPicker(_seat!, r, false, CancellationToken.None); }
        catch (Exception e)
        {
            Notice("The timer could not finish your round (" + e.Message + "); you are sealed with what you had.");
            try { picks = await _seat!.SealAsync(DoctrineSet.Empty); } catch { picks = DuelPicks.Empty; }
        }
        finally { _autoBusy = false; }
        lock (_k) _mine[r] = NewMine(r, picks, auto: true);
        Notice("Time ran out: your round was sealed for you.");
    }

    // ---- the authority --------------------------------------------------------------------------------------------------

    async Task<bool> HostRoundAsync(int r, long now)
    {
        bool did = false;
        var opp = Seat.B;

        // a. the other seat never committed: after the grace the host seals for it, as a commitment and a reveal of its own, flagged
        if (!HasCommit(r, opp) && !_hostOpp.ContainsKey(r) && !DoctrineDuelFaults.IgnoreTimeout && now >= _deadlineAuth + _o.GraceMs)
        {
            var shadow = await EnsureShadowAsync(r);
            DuelPicks picks;
            try { picks = await _o.AutoPicker(shadow, r, true, CancellationToken.None); }
            catch (Exception e) { Notice("The host could not seal for the other seat (" + e.Message + "); it fights with its levy."); picks = DuelPicks.Empty; }
            _hostOpp[r] = NewMine(r, picks, auto: true, byHost: true, seat: opp);
            Note(r, $"{(_oppName.Length > 0 ? _oppName : "The other seat")} was not there when time ran out, so the host sealed for them.");
            did = true;
        }
        if (_hostOpp.TryGetValue(r, out var h))
        {
            if (!h.CommitSent) { h.CommitSent = true; await Send(_t!.Commit(r, opp), DuelWire.Commit(new CommitMsg(h.Commit, true, true))); did = true; }
            if (BothCommits(r) && !h.RevealSent) { h.RevealSent = true; await Send(_t!.Reveal(r, opp), DuelWire.Reveal(new RevealMsg(h.Canon, h.Nonce, true, true))); did = true; }
        }

        if (!BothCommits(r)) return did;

        // b. judge the reveals that are in
        foreach (var s in new[] { Seat.A, Seat.B })
        {
            if (_judged.ContainsKey((r, s))) continue;
            RevealMsg? rv; CommitMsg? cm; lock (_k) { _reveals.TryGetValue((r, s), out rv); _commits.TryGetValue((r, s), out cm); }
            if (rv is null || cm is null) continue;
            Judge(r, s, cm, rv); did = true;
        }

        // c. a reveal that never came: forfeit it after the grace
        if (_bothAt.TryGetValue(r, out long at) && now >= at + _o.RevealGraceMs && !DoctrineDuelFaults.IgnoreTimeout)
            foreach (var s in new[] { Seat.A, Seat.B })
                if (!_judged.ContainsKey((r, s)))
                {
                    bool auto; lock (_k) auto = _commits.TryGetValue((r, s), out var cm) && (cm.Auto || cm.ByHost);
                    Forfeit(r, s, auto, $"{NameOf(s)}'s reveal never arrived, so they fight with their levy only."); did = true;
                }

        // d. both are judged: fight the battle once, publish the result, read it myself like any seat
        if (_judged.ContainsKey((r, Seat.A)) && _judged.ContainsKey((r, Seat.B)) && !_played.Contains(r) && _duel!.Ready)
        {
            _played.Add(r);
            var result = await _duel.PlayRoundAsync();
            _results[r] = result;
            var ja = _judged[(r, Seat.A)]; var jb = _judged[(r, Seat.B)];
            _pickHist[(r, Seat.A)] = ja.Picks; _pickHist[(r, Seat.B)] = jb.Picks;
            var packed = DuelWire.PackResult(result);
            var parts = DuelWire.Cut(packed, _o.MaxChunkChars);
            var hdr = new ResultHeader(r, parts.Count, packed.Length, result.Digest(),
                [_duel.IdleStreak(Seat.A), _duel.IdleStreak(Seat.B)], [ja.Auto, jb.Auto], [ja.Forfeit, jb.Forfeit],
                _roundNotes.TryGetValue(r, out var notes) ? notes.ToArray() : [], result.Final, parts.Count == 1 ? parts[0] : null);
            if (parts.Count > 1) for (int i = 0; i < parts.Count; i++) await Send(_t!.Chunk(r, i), DuelWire.ChunkBytes(i, parts[i]));
            await Send(_t!.Result(r), DuelWire.ResultHeaderBytes(hdr));
            LastPackedChars = packed.Length; LastChunks = parts.Count; MaxPackedChars = Math.Max(MaxPackedChars, packed.Length);
            if (result.Final)
            {
                _roomState = "end"; await PublishRoomAsync();
                await Send(_t.End, DuelWire.End(new EndMsg("final", Seat.A, result.Score.FieldsA, result.Score.FieldsB, "")));
            }
            _resultAt[r] = Mono;
            await ApplyResultAsync(r, hdr, result);
            if (_shadow is not null)
            {
                if (_shadow.Sealed is null) await _shadow.ApplyPicksAsync(jb.Picks);
                await _shadow.ApplyResultAsync(result);
            }
            did = true;
        }
        return did;
    }

    /// <summary>Size of the last result this host published (base64 characters of the gzipped bytes) and how many pieces it went in. For the proof's measurement.</summary>
    public int LastPackedChars { get; private set; }
    public int LastChunks { get; private set; }
    public int MaxPackedChars { get; private set; }

    string NameOf(Seat s) => s == _me ? "You" : (_oppName.Length > 0 ? _oppName : "The other seat");

    void Note(int r, string text)
    {
        if (!_roundNotes.TryGetValue(r, out var l)) _roundNotes[r] = l = [];
        if (!l.Contains(text)) l.Add(text);
    }

    void Judge(int r, Seat s, CommitMsg cm, RevealMsg rv)
    {
        bool verified = DoctrineDuelFaults.AcceptTamperedReveal || DuelPicks.Verify(cm.C, rv.P, rv.N, r, s);
        DuelPicks? picks = null; string why = "";
        if (!verified) why = $"{NameOf(s)}'s reveal did not match the commitment sealed earlier, so the round is forfeited by that seat and they fight with their levy only.";
        else if (!DuelPicks.TryParse(rv.P, out picks, out var err)) { why = $"{NameOf(s)}'s picks could not be read ({err}), so they fight with their levy only."; picks = null; }
        bool auto = cm.Auto || cm.ByHost;
        if (picks is null) { Forfeit(r, s, auto, why); return; }
        _judged[(r, s)] = new Judged(picks, false, "", auto);
        _duel!.HandIn(s, r, picks);
        if (auto) _duel.MarkAutoSealed(s); else _duel.MarkSealed(s);
    }

    void Forfeit(int r, Seat s, bool auto, string why)
    {
        _judged[(r, s)] = new Judged(DuelPicks.Empty, true, why, auto);
        _duel!.HandIn(s, r, DuelPicks.Empty);
        _duel.MarkAutoSealed(s);
        Note(r, why); Notice(why);
    }

    /// <summary>The host's own copy of the other seat: only built when the host has to seal for it (replaying the rounds already played).</summary>
    async Task<DuelSeat> EnsureShadowAsync(int r)
    {
        if (_shadow is not null && _shadow.Round == r) return _shadow;
        _shadow = await RebuildSeatAsync(Seat.B, r);
        return _shadow;
    }

    async Task<DuelSeat> RebuildSeatAsync(Seat who, int upTo)
    {
        var s = _game!.NewDuelSeat(_seed, who);
        for (int i = 0; i < upTo; i++)
        {
            await s.ApplyPicksAsync(_pickHist[(i, who)]);
            await s.ApplyResultAsync(_results[i]);
        }
        return s;
    }

    // ---- reading a result (every seat, the host included) ----------------------------------------------------------------------

    bool ResultReady(int r, out ResultHeader? hdr, out DuelRoundResult? res)
    {
        hdr = null; res = null;
        lock (_k)
        {
            if (!_resHdr.TryGetValue(r, out var candidates)) return false;
            foreach (var h in candidates.ToArray())
            {
                if (_badHdr.Contains(h)) continue;
                string text;
                if (h.Chunks == 1) text = h.Inline!;
                else
                {
                    var sb = new System.Text.StringBuilder(h.Length);
                    bool all = true;
                    for (int i = 0; i < h.Chunks && all; i++) { if (_resChunks.TryGetValue((r, i), out var piece)) sb.Append(piece); else all = false; }
                    if (!all) continue;                                          // the rest of its pieces has not arrived yet
                    text = sb.ToString();
                }
                var decoded = DuelWire.UnpackResult(text, out var err);
                if (decoded is null || decoded.Round != r || decoded.Digest() != h.Digest)
                {
                    _badHdr.Add(h);                                              // not read again unless a fresh piece of this round arrives
                    Notice("A result for round " + (r + 1) + " arrived damaged (" + (decoded is null ? err : "it does not match its digest") + "); it was ignored.");
                    continue;
                }
                hdr = h; res = decoded; _results[r] = decoded;
                return true;
            }
            return false;
        }
    }

    async Task<bool> ApplyResultAsync(int r, ResultHeader hdr, DuelRoundResult res)
    {
        var seat = _seat!;
        if (seat.Sealed is null)
        {
            // I never sealed this round (the host did it for me, or I was away): replay the picks the host revealed in my name
            RevealMsg? rv; CommitMsg? cm; lock (_k) { _reveals.TryGetValue((r, _me), out rv); _commits.TryGetValue((r, _me), out cm); }
            if (rv is null || cm is null) return false;                        // wait for them to arrive
            DuelPicks picks = DuelPicks.Empty;
            if (DuelPicks.Verify(cm.C, rv.P, rv.N, r, _me) && DuelPicks.TryParse(rv.P, out var parsed, out _)) picks = parsed!;
            if (seat.OrderOffer is null) await seat.ApplyPicksAsync(picks);
            else { seat = _seat = await RebuildSeatAsync(_me, r); await seat.ApplyPicksAsync(picks); }
            _pickHist[(r, _me)] = picks;
        }
        else
        {
            MyRound? my; lock (_k) _mine.TryGetValue(r, out my);
            _pickHist[(r, _me)] = my?.Picks ?? DuelPicks.Empty;
        }
        var check = await seat.ApplyResultAsync(res);
        var view = res.ViewFor(_me);
        int me = (int)_me, them = (int)Opp;
        _applied.Add(r);
        _history.Add(new DuelRoundOutcome(r, res, view, check, hdr.Notes, res.Final, hdr.Idle[me], hdr.Idle[them], hdr.Auto[me], hdr.Auto[them], hdr.Forfeit[me], hdr.Forfeit[them]));
        if (!check.Ok) Notice("The host's account of your army does not match the army you built (round " + (r + 1) + ").");
        _phase = DuelPhase.Result;
        _resultAt.TryAdd(r, Mono);
        Touch();
        return true;
    }

    // ---- watching, continuing, the next round -----------------------------------------------------------------------------------

    public async Task ContinueAsync()
    {
        if (_phase != DuelPhase.Result || _t is null) return;
        var o = Outcome!;
        await Send(_t.Next(o.Round, _me), DuelWire.Next());
        if (o.Final) { Finish(DuelEndKind.Final, FinalText(o.Result.Score)); Raise(); return; }
        Kick();
    }

    async Task<bool> ResultStepAsync(long now)
    {
        var o = Outcome; if (o is null) return false;
        int r = o.Round;
        if (DoctrineDuelFaults.NoIdempotence && !IsHost && ResultReady(r, out var h, out var res)) { await ApplyResultAsync(r, h!, res!); return true; }
        if (o.Final) return false;
        if (IsHost)
        {
            bool bothNext; lock (_k) bothNext = _next.Contains((r, Seat.A)) && _next.Contains((r, Seat.B));
            bool cap = _resultAt.TryGetValue(r, out long at) && Mono - at >= _o.WatchCapMs;
            if (!bothNext && !cap) return false;
            _round = r + 1; _roomState = "play"; _openMs = now; _deadlineAuth = now + _timerSec * 1000L;
            await PublishRoomAsync();
            BeginRound(r + 1);
            return true;
        }
        RoomMsg? room; lock (_k) room = _room;
        if (room is { State: "play" or "end" } && room.Round > r) { BeginRound(r + 1); return true; }
        return false;
    }

    // ---- a rematch ---------------------------------------------------------------------------------------------------------------

    public async Task RematchAsync()
    {
        if (_phase != DuelPhase.Ended || _end?.Kind != DuelEndKind.Final || _t is null) return;
        await Send(_t.Rematch(_me), DuelWire.Flag("want", true));
        Kick();
    }

    async Task<bool> RematchStepAsync()
    {
        if (_end?.Kind != DuelEndKind.Final || _t is null) return false;
        bool both; string? go; lock (_k) { both = _rematch[0] && _rematch[1]; go = _rematchGo; }
        if (IsHost && both && go is null)
        {
            var newCode = DuelProtocol.Normalize(_o.CodeSource?.Invoke()) ?? DuelProtocol.NewCode(_o.Below);
            string name = _myName; int rounds = _rounds, timer = _timerSec; var goTopic = _t.RematchGo;
            await Send(goTopic, DuelWire.RematchGo(newCode));
            await DropConnectionAsync(clear: true, keep: goTopic);
            ResetState();
            _me = Seat.A;
            await ConnectAsync(newCode);
            await StartHostAsync(name, rounds, timer);
            _staleGoTopic = goTopic;
            return true;
        }
        if (!IsHost && go is not null)
        {
            string name = _myName;
            await DropConnectionAsync(clear: true);
            ResetState();
            string? err = await JoinAsync(go, name);
            if (err is not null) Finish(DuelEndKind.Refused, "The rematch could not start: " + err);
            return true;
        }
        return false;
    }

    async Task DropConnectionAsync(bool clear, string keep)
    {
        if (keep.Length > 0) lock (_k) _outbox.Remove(keep);
        await DropConnectionAsync(clear);
    }
}
