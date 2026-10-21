using System.Security.Cryptography;
using Doctrine;
using Doctrine.Lane;
using ShowroomPlenum;

namespace ShowroomLane;

// THE ONLINE LANE BATTLE (UI-free): one player's end of a two-player room over the relay (MQTT over a WebSocket), topics ONLY under plenum/v1/doctrine-lane/.
//
// THE IDEA. The lane battle is deterministic: the same setup, seed and ordered plays give a bit-identical state, and the two players' plays at one pause commute. So the host proposes
// the match number and BOTH players run the same LaneMatch on their own device. All that crosses the relay is each player's PLAYS at each pause, never the battle itself.
//   1. A pause opens on both devices. Each publishes the state hash of that pause. A hash that differs stops the match (no silent continue).
//   2. Each player COMMITS to their plays (a SHA-256 of the plan and a nonce), either by pressing Ready or when the pause clock runs out.
//   3. When both commitments are in, each REVEALS (plan and nonce). Each side checks the other's reveal against its commitment; a reveal that does not match is refused (empty plays).
//   4. Each side applies both plans in ONE call at the pause tick, in the library's order (host's side first), and the run starts. Nothing about timing enters the battle.
//   5. If a player has not committed by the pause deadline plus a grace, the other publishes a reveal flagged "missed": both read the one flag and use empty plays for the late player,
//      who (on return) reads the same flag and drops their own plays. Three in a row, or a player gone for good, ends the match in favour of the one who stayed.
// Every message that arrives is only RECORDED (Absorb; the first of each kind, pause and role wins). Progress is a function of what is known, so a duplicate does nothing, an early
// message waits for its turn, and a reconnect needs only the retained messages. The page calls AdvanceAsync (each frame and from a 4 Hz ticker); nothing here sleeps or owns a thread.

public sealed class LaneOnline : IAsyncDisposable
{
    readonly record struct Stamped<T>(T M, long At);

    readonly LaneOnlineOptions _o;
    readonly object _k = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly List<string> _notices = [];

    // ---- identity and connection
    OnlinePhase _phase = OnlinePhase.Idle;
    LaneRole _role;
    bool _joined, _online, _needRepublish, _changed;
    string _code = "", _cid = "", _myName = "", _oppName = "", _guestCid = "";
    LaneTopics? _t;
    IPlenumRelay? _relay;
    LaneLobbyBeacon? _beacon;
    long _lastBeat, _noMissUntil, _lastAdv;
    bool _anySeen;
    int _rev, _errors;

    // ---- what is known (under _k): the first of each kind, game, pause and role wins
    LaneRoomMsg? _room; bool _roomLive; long _roomRecvUnix;
    readonly Dictionary<string, LaneJoinMsg> _joins = new(); readonly List<string> _joinOrder = [];
    readonly Stamped<LanePresenceMsg>?[] _presence = new Stamped<LanePresenceMsg>?[2];
    readonly Dictionary<(int, LaneRole), Stamped<LaneReadyMsg>> _ready = new();
    readonly Dictionary<(int, int, LaneRole), Stamped<LaneHashMsg>> _hashes = new();
    readonly Dictionary<(int, int, LaneRole), Stamped<LaneCommitMsg>> _commits = new();
    readonly Dictionary<(int, int, LaneRole), Stamped<LaneRevealMsg>> _reveals = new();
    readonly Dictionary<(int, LaneRole), LaneFinMsg> _fins = new();
    readonly Dictionary<(int, LaneRole), LaneEndMsg> _ends = new();
    readonly HashSet<(int, LaneRole)> _rematch = new();
    readonly Dictionary<string, byte[]> _outbox = new();

    // ---- the game
    int _gen, _seed;
    LaneSetup _setup = LaneMatchOptions.DefaultSetup;
    LaneMatch? _m;
    RemoteSeat? _remote;
    bool _startSet; long _startMono;
    OnlineEnd? _end;
    bool _finSent, _finChecked;
    int _missStreak;

    // ---- the pause being handled
    int _pn = -1;
    long _pArrive, _pDeadline;
    bool _pCommitted, _pRevealed, _pDeclared, _pDiscard, _readyPressed;

    public LaneOnline(LaneOnlineOptions options)
    {
        _o = options ?? throw new ArgumentNullException(nameof(options));
        if (_o.Relays is null) throw new ArgumentException("Relays is required", nameof(options));
    }

    long Mono => _o.Clock.NowMs;
    long Unix => _o.Clock.UnixMs;
    LaneRole Opp => LaneOnlineProtocol.Other(_role);
    string OppName => _oppName.Length > 0 ? _oppName : "The other player";

    // ================================================================================================ the surface

    public event Action? Changed;

    public OnlinePhase Phase => _phase;
    public bool IsHost => _joined && _role == LaneRole.Host;
    public string Code => _code;
    public string MyName => _myName;
    public string OpponentName => _oppName;
    public int Seed => _seed;
    public int Generation => _gen;
    /// <summary>Your own connection to the relay (false while it is down and being retried).</summary>
    public bool Online => _online;
    /// <summary>The match, once this player has built it and its first hash is out (null before, and after a rematch until the next one is built).</summary>
    public LaneMatch? Match => _phase is OnlinePhase.Starting or OnlinePhase.Playing or OnlinePhase.Ended ? _m : null;
    public Side LocalSide => LaneOnlineProtocol.SideOf(_role, _gen);
    public OnlineEnd? End => _end;
    public IReadOnlyList<string> Notices { get { lock (_k) return _notices.ToArray(); } }
    public int Errors => _errors;
    /// <summary>How many pauses in a row ended because the other player's plays did not come (or yours did not, by their account).</summary>
    public int MissedInARow => _missStreak;
    public bool MyCommitted => _pCommitted;
    /// <summary>True while the pause on screen is the one being handled and your plays are locked in.</summary>
    public bool PauseOpen => _m is { Stage: MatchStage.Pause } m && _pn == m.Phase;
    public bool OpponentCommitted { get { lock (_k) return PauseOpen && _commits.ContainsKey((_gen, _pn, Opp)); } }
    public bool MyRematch { get { lock (_k) return _rematch.Contains((_gen, _role)); } }
    public bool OpponentRematch { get { lock (_k) return _rematch.Contains((_gen, Opp)); } }
    /// <summary>Milliseconds to the agreed start, or -1 when it is not set yet.</summary>
    public long StartInMs => _startSet ? Math.Max(0, _startMono - Mono) : -1;
    /// <summary>The setup of the room (the host proposed it).</summary>
    public LaneSetup Setup => _setup;

    public OpponentLink Opponent
    {
        get
        {
            if (!_joined) return OpponentLink.Absent;
            if (IsHost && _guestCid.Length == 0) return OpponentLink.Absent;
            var (away, since) = OppAway();
            if (!away) return OpponentLink.Here;
            return Mono - since >= _o.RejoinGraceMs ? OpponentLink.Gone : OpponentLink.Away;
        }
    }

    /// <summary>One plain line for the tray: who is waiting for whom, and the state of the connection.</summary>
    public string StatusLine
    {
        get
        {
            if (!_joined) return "";
            if (!_online) return "Connection lost. Trying to get it back. Nothing is lost while you wait.";
            if (_phase == OnlinePhase.Waiting) return "Waiting for a second player.";
            if (_phase == OnlinePhase.Joining) return "Waiting for the host to seat you.";
            if (_phase == OnlinePhase.Building) return "Building the lanes. " + OppName + " is building them too.";
            if (_phase == OnlinePhase.Starting) return StartInMs >= 0 ? $"Both of you are ready. Starting in {(int)Math.Ceiling(StartInMs / 1000.0)}." : "Both of you are ready. Waiting for the start.";
            if (_phase == OnlinePhase.Ended) return _end?.Text ?? "";
            if (Opponent == OpponentLink.Away) return OppName + " lost their connection. Waiting for them to come back.";
            if (Opponent == OpponentLink.Gone) return OppName + " has been gone too long.";
            var m = _m;
            if (m is null) return "";
            if (m.Stage == MatchStage.Pause)
            {
                if (_pCommitted && !OpponentCommitted) return "You are ready. Waiting for " + OppName + ".";
                if (!_pCommitted && OpponentCommitted) return OppName + " is ready.";
                return OppName + " is choosing.";
            }
            if (m.Stage == MatchStage.Run && m.Speed > 1) return "Catching up with " + OppName + ".";
            return "Battle against " + OppName + ".";
        }
    }

    // ================================================================================================ creating, joining, leaving

    /// <summary>Host: open a room and list it. Throws if the relay cannot be reached.</summary>
    public async Task CreateRoomAsync(string? name)
    {
        await _gate.WaitAsync();
        try
        {
            if (_phase != OnlinePhase.Idle) throw new InvalidOperationException("already in a room");
            string? code = null;
            for (int attempt = 0; attempt < 5 && code is null; attempt++)
            {
                var tryCode = LaneOnlineProtocol.Normalize(_o.CodeSource?.Invoke()) ?? LaneOnlineProtocol.NewCode(_o.Below);
                _role = LaneRole.Host;
                await ConnectAsync(tryCode);
                if (_o.CreateProbeMs > 0 && await WaitRealAsync(() => { lock (_k) return _anySeen; }, _o.CreateProbeMs))
                {
                    await DropConnectionAsync(clear: false);      // something of someone else's already sits under this code
                    ResetState();
                    if (_o.CodeSource is not null) throw new InvalidOperationException("the chosen room code is taken");
                    continue;
                }
                code = tryCode;
            }
            if (code is null) throw new IOException("could not find a free room code");
            _myName = LaneNames.Clean(name) ?? LaneNames.Random(_o.Below);
            _joined = true; _gen = 0; _setup = _o.Setup;
            _seed = _o.SeedSource?.Invoke() ?? RandomNumberGenerator.GetInt32(1, 1_000_000_000);
            _phase = OnlinePhase.Waiting;
            await Send(_t!.Presence(_role), PresenceBytes());
            await PublishRoomAsync("lobby");
            await StartBeaconAsync();
            Notice("Room " + _code + " is open. Share the code, or wait: it is also listed under Open rooms.");
            _lastBeat = Mono;
            Raise();
        }
        finally { _gate.Release(); }
    }

    async Task StartBeaconAsync()
    {
        _beacon = new LaneLobbyBeacon(_o.Relays, _o.Clock, _code, _myName, _o.GameTag);
        try { await _beacon.StartAsync(); }
        catch { _beacon = null; Notice("The room is open but could not be listed. Share the code with the other player."); }
    }

    /// <summary>Guest: join a room by its code. Returns null on success, else the plain reason it failed.</summary>
    public async Task<string?> JoinAsync(string code, string? name)
    {
        await _gate.WaitAsync();
        try
        {
            if (_phase != OnlinePhase.Idle) throw new InvalidOperationException("already in a room");
            var c = LaneOnlineProtocol.Normalize(code);
            if (c is null) return "That is not a room code. A code is 4 to 12 letters or digits.";
            _role = LaneRole.Guest;
            try { await ConnectAsync(c); }
            catch { return "Could not reach the message relay. Check your connection and try again."; }
            bool shown = await WaitRealAsync(() => { lock (_k) return _room is not null && _presence[(int)LaneRole.Host] is not null; }, _o.JoinWaitMs);
            LaneRoomMsg? room; Stamped<LanePresenceMsg>? pres;
            lock (_k) { room = _room; pres = _presence[(int)LaneRole.Host]; }
            string? refuse = null;
            if (!shown || room is null || pres is null) refuse = room is not null ? "That room has expired. Its host is gone." : "No room with that code was found. Check the code, and that the host is still waiting.";
            else if (room.Ver != _o.GameTag) refuse = "This room runs another version of the game, so the two of you would not see the same battle. Reload the page and try again.";
            else if (!pres.Value.M.On || (Unix - pres.Value.M.Ts > _o.StaleMs && !_o.Probe.HasFlag(LaneOnlineProbe.StaleRoomAccepted))) refuse = "That room has expired. Its host is gone.";
            else if (room.State != "lobby" || (room.GuestCid.Length > 0 && room.GuestCid != _cid)) refuse = "That room already has two players.";
            if (refuse is not null) { await DropConnectionAsync(clear: true); ResetState(); return refuse; }

            _myName = LaneNames.Clean(name) ?? LaneNames.Random(_o.Below);
            _oppName = room!.Host; _gen = room.Gen; _seed = room.Seed; _setup = room.ToSetup();
            _joined = true; _phase = OnlinePhase.Joining;
            await Send(_t!.Presence(_role), PresenceBytes());
            await Send(_t.Join(_cid), LaneWire.Join(new LaneJoinMsg(_cid, _myName, _o.GameTag, Unix)));
            _lastBeat = Mono;
            Notice("Asked for a seat in room " + _code + ".");

            bool seated = await WaitRealAsync(() => { lock (_k) return _room is { } r && (r.GuestCid == _cid || (r.GuestCid.Length > 0 && r.GuestCid != _cid)); }, _o.SeatWaitMs);
            bool mine; lock (_k) mine = _room is { } r2 && r2.GuestCid == _cid;
            if (!mine)
            {
                string why = seated ? "That room already has two players." : "The host did not answer in time. They may have left.";
                await DropConnectionAsync(clear: true); ResetState();
                return why;
            }
            Raise();
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Leave: the other player is told plainly, your messages are cleared from the relay and the connection closes. Safe to call twice.</summary>
    public async Task LeaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_beacon is not null) { try { await _beacon.StopAsync(); } catch { } _beacon = null; }
            if (_t is not null && _relay is not null && _joined)
            {
                bool over = _phase is OnlinePhase.Ended or OnlinePhase.Idle;
                // when the other player has already gone (their goodbye is here, or their presence says off) there is nobody to tell and nothing to keep: the room is cleared
                bool oppGone; lock (_k) oppGone = _ends.ContainsKey((_gen, Opp)) || _presence[(int)Opp] is { M.On: false };
                bool someoneThere = !(IsHost && _guestCid.Length == 0) && !oppGone;
                if (!over && someoneThere)
                {
                    var opp = Opp;
                    try { await Send(_t.End(_gen, _role), LaneWire.End(new LaneEndMsg("left", _role, opp)), retain: true); } catch { }
                }
                await DropConnectionAsync(clear: true, keepEnd: !over && someoneThere);
            }
            else await DropConnectionAsync(clear: false);
        }
        finally { ResetState(); _gate.Release(); Raise(); }
    }

    public async ValueTask DisposeAsync() => await LeaveAsync();

    // ================================================================================================ connection

    async Task ConnectAsync(string code)
    {
        _t = new LaneTopics(code); _code = code;
        _cid = "ll" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var relay = _o.Relays.Create(_cid);
        _relay = relay;
        relay.OnMessage += OnRelayMessage; relay.OnDisconnect += OnRelayDisconnect; relay.OnReconnected += OnRelayReconnected;
        var will = new RelayWill(PresenceTopic(), LaneWire.Presence(new LanePresenceMsg(false, _cid, 0)), true, 1);
        try
        {
            await relay.ConnectAsync(will);
            _online = true;
            await relay.SubscribeAsync(_t.Filter);
        }
        catch { await DropConnectionAsync(clear: false); ResetState(); throw; }
    }

    string PresenceTopic() => _o.Probe.HasFlag(LaneOnlineProbe.WrongTopic) ? "plenum/v1/doctrine-" + _code + "/presence" : _t!.Presence(_role);
    byte[] PresenceBytes() => LaneWire.Presence(new LanePresenceMsg(true, _cid, Unix));

    /// <summary>Close the connection. With <paramref name="clear"/>, first empty every retained message this player put on the relay.</summary>
    async Task DropConnectionAsync(bool clear, bool keepEnd = false)
    {
        var relay = _relay; if (relay is null) return;
        string? presenceTopic = _t is null ? null : PresenceTopic();
        try
        {
            if (clear && relay.State == RelayState.Connected && _t is not null)
            {
                // a goodbye ("left") stays on the relay for a player who is offline right now; whoever leaves after reading an end message clears every end message of the room
                string[] topics; lock (_k) topics = _outbox.Keys.Where(k => !(keepEnd && k.Contains("/end/", StringComparison.Ordinal))).ToArray();
                foreach (var tp in topics) { try { await relay.PublishAsync(tp, ReadOnlyMemory<byte>.Empty, true, 1); } catch { } }
                if (!keepEnd)
                    for (int g = 0; g <= _gen; g++)
                        foreach (var r in new[] { LaneRole.Host, LaneRole.Guest })
                            { try { await relay.PublishAsync(_t.End(g, r), ReadOnlyMemory<byte>.Empty, true, 1); } catch { } }
            }
            try { await relay.DisconnectAsync(); } catch { }
            if (clear && presenceTopic is not null && _o.PresenceClearDelayMs > 0) await ClearPresenceAsync(presenceTopic);
        }
        finally
        {
            relay.OnMessage -= OnRelayMessage; relay.OnDisconnect -= OnRelayDisconnect; relay.OnReconnected -= OnRelayReconnected;
            try { await relay.DisposeAsync(); } catch { }
            _relay = null; _online = false;
        }
    }

    /// <summary>A broker can read the closed socket as a lost link and publish the last will over the cleared presence. So once the first connection is gone, a second short one (no will) clears the presence again.</summary>
    async Task ClearPresenceAsync(string topic)
    {
        IPlenumRelay? r = null;
        try
        {
            await Task.Delay(_o.PresenceClearDelayMs);
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
            _t = null; _code = ""; _cid = ""; _joined = false; _online = false; _needRepublish = false; _anySeen = false;
            _myName = ""; _oppName = ""; _guestCid = ""; _room = null; _roomLive = false; _rev = 0; _beacon = null;
            _joins.Clear(); _joinOrder.Clear(); Array.Clear(_presence);
            ClearGameKnowledge();
            _phase = OnlinePhase.Idle; _gen = _seed = 0; _end = null; _setup = LaneMatchOptions.DefaultSetup;
            _m = null; _remote = null; _startSet = false; _startMono = 0; _finSent = _finChecked = false; _missStreak = 0; _pn = -1; _outbox.Clear();
            _pCommitted = _pRevealed = _pDeclared = _pDiscard = _readyPressed = false;
        }
    }

    /// <summary>Forgets what was known about the games before <paramref name="newGen"/> (everything when it is large). Messages of the new game that arrived early are kept.</summary>
    void ClearGameKnowledge(int newGen = int.MaxValue)
    {
        foreach (var k in _ready.Keys.Where(k => k.Item1 < newGen).ToList()) _ready.Remove(k);
        foreach (var k in _hashes.Keys.Where(k => k.Item1 < newGen).ToList()) _hashes.Remove(k);
        foreach (var k in _commits.Keys.Where(k => k.Item1 < newGen).ToList()) _commits.Remove(k);
        foreach (var k in _reveals.Keys.Where(k => k.Item1 < newGen).ToList()) _reveals.Remove(k);
        foreach (var k in _fins.Keys.Where(k => k.Item1 < newGen).ToList()) _fins.Remove(k);
        foreach (var k in _ends.Keys.Where(k => k.Item1 < newGen).ToList()) _ends.Remove(k);
        _rematch.RemoveWhere(k => k.Item1 < newGen);
    }

    void OnRelayDisconnect(string reason) { _online = false; Notice("The connection to the relay was lost. Trying to get it back."); Touch(); }
    void OnRelayReconnected()
    {
        _online = true; _needRepublish = true;
        _noMissUntil = Mono + _o.ResyncMs;
        Notice("Back on the relay. Catching up."); Touch();
    }

    void OnRelayMessage(RelayMessage m)
    {
        try { Absorb(m.Topic, m.Payload, m.Retained, true); } catch { }
    }

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
        if (_o.Probe.HasFlag(LaneOnlineProbe.WrongTopic) && topic == t.Presence(_role)) topic = PresenceTopic();
        else if (!topic.StartsWith(t.Root, StringComparison.Ordinal)) throw new InvalidOperationException("topic outside the room: " + topic);
        if (retain) lock (_k) _outbox[topic] = payload;
        var relay = _relay;
        if (relay is not null)
        {
            try { await relay.PublishAsync(topic, payload, retain, 1); }
            catch { Notice("A message could not be sent just now. It will be sent again when the connection is back."); _needRepublish = true; }
        }
        Absorb(topic, payload, false, false);
    }

    async Task PublishRoomAsync(string state)
    {
        _rev++;
        var s = _setup;
        long startAt = state == "play" ? _startAtUnix : 0;
        var m = new LaneRoomMsg(_rev, _o.GameTag, _seed, s.Lanes, s.LaneCols, s.PhaseTicks, s.TickCap, s.BaseHp, s.MaxUnitsPerSide, state, _gen, _myName, _oppName, _guestCid, startAt, Unix);
        await Send(_t!.Room, LaneWire.Room(m));
    }
    long _startAtUnix;

    // ================================================================================================ recording what arrives

    void Absorb(string topic, byte[] payload, bool retained, bool fromRelay)
    {
        var t = _t;
        if (t is null || payload.Length == 0 || !t.TryParse(topic, out var p)) return;
        long now = Mono;
        lock (_k)
        {
            if (fromRelay) _anySeen = true;
            int g = _o.Probe.HasFlag(LaneOnlineProbe.GenIgnored) ? _gen : p.Gen;
            bool replace = _o.Probe.HasFlag(LaneOnlineProbe.NoIdempotence);
            switch (p.Kind)
            {
                case LaneTopics.Kind.Room:
                {
                    var m = LaneWire.ReadRoom(payload); if (m is null) return;
                    if (_room is null || m.Rev > _room.Rev)
                    {
                        _room = m; _roomLive = fromRelay && !retained;
                        if (_roomLive) _roomRecvUnix = Unix;
                    }
                    break;
                }
                case LaneTopics.Kind.Join:
                {
                    var m = LaneWire.ReadJoin(payload); if (m is null || m.Cid != p.Id) return;
                    if (_joins.TryAdd(m.Cid, m)) _joinOrder.Add(m.Cid); else _joins[m.Cid] = m;
                    break;
                }
                case LaneTopics.Kind.Presence: { var m = LaneWire.ReadPresence(payload); if (m is not null) _presence[(int)p.Role] = new(m, now); break; }
                case LaneTopics.Kind.Ready: { var m = LaneWire.ReadReady(payload); if (m is not null && (replace || !_ready.ContainsKey((g, p.Role)))) _ready[(g, p.Role)] = new(m, now); break; }
                case LaneTopics.Kind.Hash: { var m = LaneWire.ReadHash(payload); if (m is not null && (replace || !_hashes.ContainsKey((g, p.Pause, p.Role)))) _hashes[(g, p.Pause, p.Role)] = new(m, now); break; }
                case LaneTopics.Kind.Commit: { var m = LaneWire.ReadCommit(payload); if (m is not null && (replace || !_commits.ContainsKey((g, p.Pause, p.Role)))) _commits[(g, p.Pause, p.Role)] = new(m, now); break; }
                case LaneTopics.Kind.Reveal: { var m = LaneWire.ReadReveal(payload); if (m is not null && (replace || !_reveals.ContainsKey((g, p.Pause, p.Role)))) _reveals[(g, p.Pause, p.Role)] = new(m, now); break; }
                case LaneTopics.Kind.Fin: { var m = LaneWire.ReadFin(payload); if (m is not null) _fins.TryAdd((g, p.Role), m); break; }
                case LaneTopics.Kind.End: { var m = LaneWire.ReadEnd(payload); if (m is not null && m.By == p.Role) _ends.TryAdd((g, p.Role), m); break; }
                case LaneTopics.Kind.Rematch: { var f = LaneWire.ReadFlag(payload, "want"); if (f == true) _rematch.Add((g, p.Role)); break; }
            }
            _changed = true;
        }
    }

    Stamped<LaneCommitMsg>? CommitOf(int n, LaneRole r) { lock (_k) return _commits.TryGetValue((_gen, n, r), out var c) ? c : null; }
    Stamped<LaneRevealMsg>? RevealOf(int n, LaneRole r) { lock (_k) return _reveals.TryGetValue((_gen, n, r), out var c) ? c : null; }
    Stamped<LaneHashMsg>? HashOf(int n, LaneRole r) { lock (_k) return _hashes.TryGetValue((_gen, n, r), out var c) ? c : null; }
    Stamped<LaneReadyMsg>? ReadyOf(LaneRole r) { lock (_k) return _ready.TryGetValue((_gen, r), out var c) ? c : null; }

    // ================================================================================================ the pump

    void Raise() { _changed = false; try { Changed?.Invoke(); } catch { } }
    void Touch() => _changed = true;

    void Notice(string text)
    {
        lock (_k) { _notices.Add(text); if (_notices.Count > 40) _notices.RemoveAt(0); }
        _changed = true;
    }

    /// <summary>
    /// Lets the protocol act on what is known and moves the battle on by the time that has passed (at most <see cref="LaneOnlineOptions.RunDtCap"/> seconds a call). The page calls
    /// this every frame and from a 4 Hz ticker (a hidden tab still answers); calls never overlap.
    /// </summary>
    public async ValueTask<LaneAdvance> AdvanceAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long now = Mono;
            double dt = _lastAdv == 0 ? 0 : Math.Min(Math.Max(0, (now - _lastAdv) / 1000.0), _o.RunDtCap);
            _lastAdv = now;
            var before = _m is null ? (MatchStage?)null : _m.Stage;
            int ran = 0;
            await PumpAsync();
            var m = _m;
            if (m is not null && _phase == OnlinePhase.Playing && m.Stage == MatchStage.Run)
            {
                m.SetSpeed(OpponentAhead() ? LaneMatch.MaxSpeed : 1);
                var adv = await m.AdvanceAsync(dt, ct);
                ran = adv.Ticks;
                await PumpAsync();
            }
            if (_changed) Raise();
            var after = _m is null ? (MatchStage?)null : _m.Stage;
            return new LaneAdvance(ran, before != after);
        }
        finally { _gate.Release(); }
    }

    /// <summary>True when the other player has already reached a later pause than this player's battle (this player is behind and catches up at 4x until they meet).</summary>
    bool OpponentAhead()
    {
        var m = _m; if (m is null) return false;
        lock (_k) return _hashes.Keys.Any(k => k.Item1 == _gen && k.Item3 == Opp && k.Item2 > m.Phase);
    }

    async Task PumpAsync()
    {
        for (int i = 0; i < 64; i++)
        {
            bool did;
            try { did = await StepOnceAsync(); }
            catch (Exception e)
            {
                _errors++;
                Notice("Internal fault: " + e.GetType().Name + ": " + e.Message);
                break;
            }
            if (!did) break;
        }
    }

    async Task<bool> StepOnceAsync()
    {
        if (_phase == OnlinePhase.Idle || _t is null) return false;
        bool did = false;
        if (_needRepublish && _online) { _needRepublish = false; did |= await RepublishAsync(); }
        if (_online && Mono - _lastBeat >= _o.HeartbeatMs)
        {
            _lastBeat = Mono;
            try { await Send(_t.Presence(_role), PresenceBytes()); if (_beacon is not null) await _beacon.BeatAsync(); } catch { }
            did = true;
        }
        did |= await CheckEndAsync();
        switch (_phase)
        {
            case OnlinePhase.Waiting: did |= await HostSeatAsync(); break;
            case OnlinePhase.Joining: did |= GuestSeated(); break;
            case OnlinePhase.Building: did |= await BuildAsync(); break;
            case OnlinePhase.Starting: did |= await StartingAsync(); break;
            case OnlinePhase.Playing: did |= await PlayingAsync(); break;
            case OnlinePhase.Ended: did |= await EndedAsync(); break;
        }
        return did;
    }

    /// <summary>After a reconnect: say again everything this player said (idempotent), and that they are online.</summary>
    async Task<bool> RepublishAsync()
    {
        var relay = _relay; if (relay is null) return false;
        KeyValuePair<string, byte[]>[] all; lock (_k) all = _outbox.ToArray();
        await Send(_t!.Presence(_role), PresenceBytes());
        foreach (var (topic, payload) in all)
            if (topic != PresenceTopic()) { try { await relay.PublishAsync(topic, payload, true, 1); } catch { _needRepublish = true; } }
        return true;
    }

    // ================================================================================================ how the match ends, other than by being played out

    (bool Away, long Since) OppAway()
    {
        Stamped<LanePresenceMsg>? p; lock (_k) p = _presence[(int)Opp];
        if (p is null) return (false, 0);
        if (!p.Value.M.On) return (true, p.Value.At);
        long silent = Mono - p.Value.At;
        if (silent > 3L * _o.HeartbeatMs) return (true, p.Value.At + 3L * _o.HeartbeatMs);
        return (false, 0);
    }

    bool SettledOnline => _online && Mono >= _noMissUntil;

    async Task<bool> CheckEndAsync()
    {
        if (_phase is OnlinePhase.Idle or OnlinePhase.Ended) return false;
        // the other player said so
        LaneEndMsg? em; lock (_k) em = _ends.TryGetValue((_gen, Opp), out var e0) ? e0 : null;
        if (em is not null) return await OpponentEndedAsync(em);
        // the other player is gone for good
        if (_joined && !(IsHost && _guestCid.Length == 0) && SettledOnline && !_o.Probe.HasFlag(LaneOnlineProbe.NeverGone))
        {
            var (away, since) = OppAway();
            if (away && Mono - since >= _o.RejoinGraceMs)
            {
                if (_phase == OnlinePhase.Playing) { await EndByMeAsync("gone"); return true; }
                if (IsHost) { await ReopenRoomAsync(OppName + " left before the match started."); return true; }
                Finish(OnlineEndKind.OpponentGone, "The host left before the match started.", null); return true;
            }
        }
        return false;
    }

    async Task<bool> OpponentEndedAsync(LaneEndMsg em)
    {
        string who = OppName;
        if (em.Reason == "left")
        {
            if (_phase != OnlinePhase.Playing && IsHost) { await ReopenRoomAsync(who + " left before the match started."); return true; }
            if (_phase != OnlinePhase.Playing) { Finish(OnlineEndKind.OpponentLeft, "The host left before the match started.", null); return true; }
            Finish(OnlineEndKind.OpponentLeft, who + " left the match. The match is yours.", true);
            return true;
        }
        if (em.Reason == "desync") { Finish(OnlineEndKind.OutOfSync, DesyncText(), null); return true; }
        if (em.Reason == "refused") { Finish(OnlineEndKind.PlaysRefused, who + " stopped the match: the plays you showed did not match what you locked in earlier.", null); return true; }
        // gone / away / noreveal: the other player stayed and says this one did not
        Finish(OnlineEndKind.YouWereAway, who + " waited for you and ended the match. You were away too long, so the match went to them.", false);
        return true;
    }

    /// <summary>This player ends the match because the other is gone, was away for too many pauses, or never revealed. The match goes to the one who stayed.</summary>
    async Task EndByMeAsync(string reason)
    {
        try { await Send(_t!.End(_gen, _role), LaneWire.End(new LaneEndMsg(reason, _role, _role))); } catch { }
        string who = OppName;
        string text = reason switch
        {
            "away" => $"{who} missed {_missStreak} pauses in a row, so the match ends here. The match is yours.",
            "noreveal" => $"{who} locked in their plays and then never showed them, so the match ends here. The match is yours.",
            _ => $"{who} has been gone too long, so the match ends here. The match is yours.",
        };
        Finish(OnlineEndKind.OpponentGone, text, true);
    }

    string DesyncText() => "The two games no longer agree, so the match was stopped here instead of letting you play on different battles. Neither player is blamed. Start a new match to play again.";

    async Task DesyncAsync(string where)
    {
        try { await Send(_t!.End(_gen, _role), LaneWire.End(new LaneEndMsg("desync", _role, null))); } catch { }
        Notice("Stopped: the two games disagree " + where + ".");
        Finish(OnlineEndKind.OutOfSync, DesyncText(), null);
    }

    void Finish(OnlineEndKind kind, string text, bool? won)
    {
        _end = new OnlineEnd(kind, text, won);
        _phase = OnlinePhase.Ended;
        Notice(text);
        var b = _beacon; _beacon = null;
        if (b is not null) _ = b.StopAsync();
        Touch();
    }

    /// <summary>The host's room goes back to waiting after the second player left before the match began.</summary>
    async Task ReopenRoomAsync(string why)
    {
        await ClearOwnGameAsync(_gen + 2);
        lock (_k) { _joins.Clear(); _joinOrder.Clear(); _presence[(int)LaneRole.Guest] = null; }
        _m = null; _remote = null; _guestCid = ""; _oppName = ""; _startSet = false; _pn = -1; _gen += 2;   // two, so the host keeps the same side as before
        _seed = _o.SeedSource?.Invoke() ?? RandomNumberGenerator.GetInt32(1, 1_000_000_000);
        _phase = OnlinePhase.Waiting;
        await PublishRoomAsync("lobby");
        await StartBeaconAsync();
        Notice(why + " The room is open again.");
    }

    /// <summary>Clears, from the relay, the retained messages of the games before <paramref name="newGen"/>, and forgets what was known about them.</summary>
    async Task ClearOwnGameAsync(int newGen)
    {
        var relay = _relay;
        string[] stale;
        lock (_k)
        {
            string gRoot = _t is null ? "\0" : _t.Root + "g";
            stale = _outbox.Keys.Where(k => k.StartsWith(gRoot, StringComparison.Ordinal) && int.TryParse(k[gRoot.Length..k.IndexOf('/', gRoot.Length)], out int kg) && kg < newGen).ToArray();
            foreach (var s in stale) _outbox.Remove(s);
            ClearGameKnowledge(newGen);
        }
        if (relay is not null) foreach (var s in stale) { try { await relay.PublishAsync(s, ReadOnlyMemory<byte>.Empty, true, 1); } catch { } }
        _finSent = _finChecked = false; _missStreak = 0; _pCommitted = _pRevealed = _pDeclared = _pDiscard = _readyPressed = false;
    }

    // ================================================================================================ the lobby of a room: the host seats a guest, the guest is seated

    async Task<bool> HostSeatAsync()
    {
        if (_guestCid.Length > 0) return false;
        LaneJoinMsg? pick = null;
        lock (_k)
        {
            var gp = _presence[(int)LaneRole.Guest]?.M;
            foreach (var cid in _joinOrder)
            {
                var j = _joins[cid];
                // a join counts if it is current, is for this build, and its sender is the one that is online as the guest (a stale join of a guest who died is passed over)
                if (j.Ver != _o.GameTag || Unix - j.Ts > _o.StaleMs || gp is null || !gp.On || gp.Cid != j.Cid) continue;
                pick = j; break;
            }
        }
        if (pick is null) return false;
        _guestCid = pick.Cid; _oppName = pick.Name.Length > 0 ? pick.Name : "Guest";
        if (_beacon is not null) { var b = _beacon; _beacon = null; try { await b.StopAsync(); } catch { } }
        _phase = OnlinePhase.Building;
        await PublishRoomAsync("seated");
        Notice(_oppName + " joined. Building the lanes.");
        return true;
    }

    bool GuestSeated()
    {
        LaneRoomMsg? room; lock (_k) room = _room;
        if (room is null) return false;
        if (room.GuestCid.Length > 0 && room.GuestCid != _cid) { Finish(OnlineEndKind.Failed, "That room already has two players.", null); return true; }
        if (room.GuestCid == _cid && room.State is "seated" or "play")
        {
            _oppName = room.Host; _gen = room.Gen; _seed = room.Seed; _setup = room.ToSetup();
            _phase = OnlinePhase.Building;
            Notice("You are seated. Building the lanes.");
            return true;
        }
        return false;
    }

    // ================================================================================================ building the match and the agreed start

    async Task<bool> BuildAsync()
    {
        if (_m is not null) { _phase = OnlinePhase.Starting; return true; }
        var local = LaneOnlineProtocol.SideOf(_role, _gen);
        var remote = new RemoteSeat();
        var mo = new LaneMatchOptions
        {
            Seed = _seed, Setup = _setup, LocalSide = local, ExternalPause = true, MaxTicksPerAdvance = _o.TicksPerAdvance,
            Player = local == Side.Player ? null : remote, Enemy = local == Side.Enemy ? null : remote,
        };
        try { _m = await LaneMatch.CreateAsync(mo, _o.Tracer?.Invoke()); }
        catch (Exception e)
        {
            try { await Send(_t!.End(_gen, _role), LaneWire.End(new LaneEndMsg("desync", _role, null))); } catch { }
            Finish(OnlineEndKind.Failed, "Could not build the lanes: " + e.Message, null);
            return true;
        }
        _remote = remote;
        ulong h0 = _m.PauseHashes[0].Hash;
        if (_o.Probe.HasFlag(LaneOnlineProbe.LyingHash)) h0 ^= 1;
        await Send(_t!.Ready(_gen, _role), LaneWire.Ready(new LaneReadyMsg(LaneOnlineProtocol.HashHex(h0), Unix)));
        _phase = OnlinePhase.Starting;
        return true;
    }

    async Task<bool> StartingAsync()
    {
        var mine = ReadyOf(_role); var theirs = ReadyOf(Opp);
        if (mine is null) return false;
        if (theirs is not null && theirs.Value.M.Hash != mine.Value.M.Hash && !_o.Probe.HasFlag(LaneOnlineProbe.IgnoreHashMismatch))
        {
            await DesyncAsync("at the very start (the two copies of the game build different battles)");
            return true;
        }
        if (theirs is null) return false;
        if (IsHost)
        {
            if (!_startSet)
            {
                _startAtUnix = Unix + _o.StartDelayMs;
                _startMono = Mono + _o.StartDelayMs; _startSet = true;
                await PublishRoomAsync("play");
                return true;
            }
        }
        else if (!_startSet)
        {
            LaneRoomMsg? room; bool live; long recv; lock (_k) { room = _room; live = _roomLive; recv = _roomRecvUnix; }
            if (room is null || room.State != "play" || room.Gen != _gen) return false;
            // a live "play" message tells this device's clock apart from the host's by the time it took to arrive (a retained one is old: start at once)
            if (live) { long skew = room.HostNow - recv; _startMono = Mono + (room.StartAt - skew - Unix); }
            else _startMono = Mono;
            _startSet = true;
        }
        if (_startSet && Mono >= _startMono)
        {
            _phase = OnlinePhase.Playing;
            _lastAdv = Mono;
            Notice("The battle begins.");
            return true;
        }
        return false;
    }

    // ================================================================================================ playing: the pauses

    async Task<bool> PlayingAsync()
    {
        var m = _m!;
        if (await CompareHashesAsync()) return true;
        if (m.Stage == MatchStage.Over) return await OverAsync();
        if (m.Stage == MatchStage.Pause) return await PauseAsync();
        return false;
    }

    /// <summary>Both players' hash of the same pause must be the same number. A different one stops the match.</summary>
    async Task<bool> CompareHashesAsync()
    {
        if (_o.Probe.HasFlag(LaneOnlineProbe.IgnoreHashMismatch)) return false;
        int bad = -1;
        lock (_k)
        {
            foreach (var kv in _hashes)
            {
                if (kv.Key.Item1 != _gen || kv.Key.Item3 != LaneRole.Host) continue;
                if (_hashes.TryGetValue((_gen, kv.Key.Item2, LaneRole.Guest), out var g) && g.M.Hash != kv.Value.M.Hash) { bad = kv.Key.Item2; break; }
            }
        }
        if (bad < 0) return false;
        await DesyncAsync("at pause " + (bad + 1));
        return true;
    }

    async Task EnterPauseAsync()
    {
        var m = _m!;
        _pn = m.Phase;
        long now = Mono;
        _pArrive = _pn == 0 ? Math.Min(now, _startMono) : now;
        _pDeadline = _pArrive + _o.PauseMs;
        _pCommitted = _pRevealed = _pDeclared = _pDiscard = _readyPressed = false;
        ulong h = m.PauseHashes[^1].Hash;
        if (_o.Probe.HasFlag(LaneOnlineProbe.LyingHash)) h ^= 1;
        await Send(_t!.Hash(_gen, _pn, _role), LaneWire.Hash(new LaneHashMsg(LaneOnlineProtocol.HashHex(h))));
    }

    /// <summary>When the other player is expected to have committed by (before the grace): the later of this player's own deadline and a pause after their own arrival at it.</summary>
    long ExpectedOpponent(int n)
    {
        var h = HashOf(n, Opp);
        return h is { } hh ? Math.Max(_pDeadline, hh.At + _o.PauseMs) : _pDeadline + _o.ArrivalSlackMs;
    }

    async Task<bool> PauseAsync()
    {
        var m = _m!; bool did = false;
        if (_pn != m.Phase) { await EnterPauseAsync(); did = true; }
        int n = _pn; long now = Mono; var opp = Opp;
        m.SetPauseLeft((_pDeadline - now) / 1000.0);
        var oppReveal = RevealOf(n, opp);

        // a reveal that says "you were too late" voids this player's plays for the pause (and if both say it, the two lost sight of each other: stop)
        if (oppReveal is { M.Missed: true })
        {
            if (_pDeclared) { await DesyncAsync("at pause " + (n + 1) + " (each of you thought the other was gone)"); return true; }
            if (!_pDiscard && !_o.Probe.HasFlag(LaneOnlineProbe.FlagIgnored))
            {
                _pDiscard = true; did = true;
                Notice("Your plays for this pause came too late for " + OppName + ". They were not used.");
            }
        }

        // my commitment: Ready, or the clock
        if (!_pCommitted && !_pDiscard && (_readyPressed || now >= _pDeadline)) { await CommitAsync(n); did = true; }
        // my reveal, once both commitments are in
        if (_pCommitted && !_pRevealed && !_pDiscard && (CommitOf(n, opp) is not null || _o.Probe.HasFlag(LaneOnlineProbe.RevealBeforeCommits))) { await RevealAsync(n, false); did = true; }

        // the other player never committed: after the grace, say so in my reveal (the one message both sides read the decision from)
        if (_pCommitted && !_pRevealed && !_pDiscard && CommitOf(n, opp) is null && oppReveal is null && SettledOnline && !_o.Probe.HasFlag(LaneOnlineProbe.NeverDeclareMiss))
        {
            long due = ExpectedOpponent(n) + (_o.Probe.HasFlag(LaneOnlineProbe.NoGrace) ? 0 : _o.GraceMs);
            if (now >= due)
            {
                await RevealAsync(n, true);
                _pDeclared = true; _missStreak++; did = true;
                Notice(OppName + " did not answer in time. Their plays for this pause are empty. (" + _missStreak + " missed in a row)");
                if (_missStreak >= _o.MissLimit && !_o.Probe.HasFlag(LaneOnlineProbe.MissStreakIgnored)) { await EndByMeAsync("away"); return true; }
            }
        }

        // both committed but the other reveal never came (counted from the later of the two commitments and the time the other player would have called THIS player late, so a commitment that never reached them is answered by their flagged reveal first)
        var oc = CommitOf(n, opp); var mc = CommitOf(n, _role);
        if (_pRevealed && !_pDeclared && oc is not null && mc is not null && oppReveal is null && SettledOnline && now >= Math.Max(Math.Max(oc.Value.At, mc.Value.At), ExpectedOpponent(n) + _o.GraceMs) + _o.RevealWaitMs)
        { await EndByMeAsync("noreveal"); return true; }

        // resolve: apply both plans together
        LanePlan? oppPlan = null;
        if (_pDeclared) oppPlan = LanePlan.Empty;
        else if (oppReveal is { } rv && oc is { } cm)
        {
            oppPlan = ReadOpponentPlan(n, rv.M, cm.M, m.Battle.Setup.Lanes, out var refuse);
            if (refuse is not null) { await RefuseAsync(refuse); return true; }
        }
        if (oppPlan is null) return did;
        if (!(_pDiscard || (_pCommitted && _pRevealed))) return did;

        if (_pDiscard) m.Human!.Clear();
        if (_o.Probe.HasFlag(LaneOnlineProbe.DropsRemotePlays)) oppPlan = LanePlan.Empty;
        _remote!.Set(n, oppPlan);
        if (!_pDeclared && !_pDiscard) _missStreak = 0;
        _readyPressed = false;
        await m.EndPauseNowAsync();
        return true;
    }

    /// <summary>
    /// Checks the opponent's reveal against the commitment they made earlier, then reads the plan. A reveal that does not match, or cannot be read, is REFUSED and the match is stopped:
    /// the other player's own game used the plays they locked in, so no plan this side could apply would keep the two games the same.
    /// </summary>
    LanePlan ReadOpponentPlan(int n, LaneRevealMsg rv, LaneCommitMsg cm, int lanes, out string? refuse)
    {
        refuse = null;
        bool ok = _o.Probe.HasFlag(LaneOnlineProbe.AcceptTamperedReveal) || LaneOnlineProtocol.Verify(cm.C, _code, _gen, n, Opp, rv.Nonce, rv.Plan);
        if (!ok) { refuse = OppName + "'s plays did not match what they locked in earlier, so they were refused and the match was stopped. Nothing from that pause was applied."; return LanePlan.Empty; }
        if (!LanePlanCodec.TryDecode(rv.Plan, lanes, out var plan, out var why)) { refuse = OppName + "'s plays could not be read (" + why + "), so the match was stopped. Nothing from that pause was applied."; return LanePlan.Empty; }
        return plan;
    }

    async Task RefuseAsync(string text)
    {
        try { await Send(_t!.End(_gen, _role), LaneWire.End(new LaneEndMsg("refused", _role, null))); } catch { }
        Finish(OnlineEndKind.PlaysRefused, text, null);
    }

    async Task CommitAsync(int n)
    {
        var m = _m!; var h = m.Human!;
        h.Freeze();
        _myPlan = LanePlanCodec.Encode(h.CurrentPlan());
        _myNonce = RandomNumberGenerator.GetBytes(16);
        var c = LaneOnlineProtocol.Commit(_code, _gen, n, _role, _myNonce, _myPlan);
        _pCommitted = true;
        await Send(_t!.Commit(_gen, n, _role), LaneWire.Commit(new LaneCommitMsg(c)));
    }
    string _myPlan = ""; byte[] _myNonce = [];

    async Task RevealAsync(int n, bool missed)
    {
        var plan = _myPlan; var nonce = _myNonce;
        if (_o.RevealTamper is { } tamper) (plan, nonce) = tamper(n, plan, nonce);
        _pRevealed = true;
        await Send(_t!.Reveal(_gen, n, _role), LaneWire.Reveal(new LaneRevealMsg(plan, nonce, missed)));
    }

    /// <summary>Ready: lock the plays in now (the commitment goes out). False when it is not the time, or you were already locked in.</summary>
    public async Task<bool> ReadyAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_phase != OnlinePhase.Playing || _m is not { Stage: MatchStage.Pause } m || _pn != m.Phase || _pCommitted || _pDiscard) return false;
            _readyPressed = true;
            await CommitAsync(_pn);
            await PumpAsync();
            if (_changed) Raise();
            return true;
        }
        finally { _gate.Release(); }
    }

    // ================================================================================================ the end of a played match, and a rematch

    async Task<bool> OverAsync()
    {
        var m = _m!;
        bool did = false;
        if (!_finSent)
        {
            _finSent = true;
            ulong h = m.PauseHashes[^1].Hash;
            if (_o.Probe.HasFlag(LaneOnlineProbe.LyingHash)) h ^= 1;
            await Send(_t!.Fin(_gen, _role), LaneWire.Fin(new LaneFinMsg(m.Tick, (int)m.Status, LaneOnlineProtocol.HashHex(h))));
            did = true;
        }
        var st = m.Status;
        bool? won = st == LaneStatus.Draw ? null : (st == LaneStatus.PlayerWon) == (m.LocalSide == Side.Player);
        Finish(OnlineEndKind.Played, LaneWords.Outcome(st, m.LocalSide), won);
        return true | did;
    }

    async Task<bool> EndedAsync()
    {
        bool did = false;
        // the other player's final number must be mine too
        if (_end is { Kind: OnlineEndKind.Played } && !_finChecked && !_o.Probe.HasFlag(LaneOnlineProbe.IgnoreHashMismatch))
        {
            LaneFinMsg? of; lock (_k) of = _fins.TryGetValue((_gen, Opp), out var f) ? f : null;
            var m = _m;
            if (of is not null && m is not null)
            {
                _finChecked = true;
                if (of.Tick != m.Tick || of.Status != (int)m.Status || of.Hash != LaneOnlineProtocol.HashHex(m.PauseHashes[^1].Hash))
                { await DesyncAsync("at the end of the battle"); return true; }
            }
        }
        return did | await RematchStepAsync();
    }

    /// <summary>After a match played out (or stopped because the games disagreed): ask for another with the same opponent. When both ask, the host opens the next game in the same room and the two swap sides.</summary>
    public async Task RematchAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_phase != OnlinePhase.Ended || _end is not { Kind: OnlineEndKind.Played or OnlineEndKind.OutOfSync } || _t is null) return;
            await Send(_t.Rematch(_gen, _role), LaneWire.Flag("want", true));
            await PumpAsync();
            if (_changed) Raise();
        }
        finally { _gate.Release(); }
    }

    /// <summary>True when a rematch can be asked for: the match ended in a way that allows it and the other player is still in the room.</summary>
    public bool CanRematch => _phase == OnlinePhase.Ended && _end is { Kind: OnlineEndKind.Played or OnlineEndKind.OutOfSync } && Opponent is OpponentLink.Here or OpponentLink.Away;

    async Task<bool> RematchStepAsync()
    {
        if (_end is not { Kind: OnlineEndKind.Played or OnlineEndKind.OutOfSync } || _t is null) return false;
        if (IsHost)
        {
            bool both; lock (_k) both = _rematch.Contains((_gen, LaneRole.Host)) && _rematch.Contains((_gen, LaneRole.Guest));
            if (!both) return false;
            await ClearOwnGameAsync(_gen + 1);
            _gen++; _seed = _o.SeedSource?.Invoke() ?? RandomNumberGenerator.GetInt32(1, 1_000_000_000);
            NewGameState();
            await PublishRoomAsync("seated");
            Notice("Rematch. You swap sides. Building the lanes.");
            return true;
        }
        LaneRoomMsg? room; lock (_k) room = _room;
        if (room is not null && room.Gen > _gen && room.GuestCid == _cid && room.State is "seated" or "play")
        {
            await ClearOwnGameAsync(room.Gen);
            _gen = room.Gen; _seed = room.Seed; _setup = room.ToSetup();
            NewGameState();
            Notice("Rematch. You swap sides. Building the lanes.");
            return true;
        }
        return false;
    }

    void NewGameState()
    {
        _m = null; _remote = null; _end = null; _startSet = false; _startMono = 0; _pn = -1; _phase = OnlinePhase.Building; _finSent = _finChecked = false; _missStreak = 0;
        _pCommitted = _pRevealed = _pDeclared = _pDiscard = _readyPressed = false;
        Touch();
    }
}
