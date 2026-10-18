using System.Text;
using System.Text.Json;

namespace ShowroomPlenum;

public enum SessionPhase { Idle, Connecting, Lobby, Unreachable, Room }
public enum RoomRole { None, Pending, Host, Guest, Spectator }

/// <summary>What the host publishes (retained) about its room. The seed is chosen by the host and is all a client needs, with the
/// orders, to rebuild the match.</summary>
public sealed record RoomSetup(string Code, string Host, string HostId, string? Guest, string? GuestId, ulong Seed, string Status, bool Private, long Ts, int Ver)
{
    public int Seats => GuestId is null ? 1 : 2;
    public string? IdOf(int seat) => seat == 0 ? HostId : GuestId;
    public int SeatOf(string id) => id == HostId ? 0 : (GuestId is not null && id == GuestId ? 1 : -1);
}

/// <summary>Message bodies: small JSON objects written and read by hand (no reflection, so the trimmed build is safe).</summary>
public static class PlenumJson
{
    public const int RulesVersion = 1;

    private static byte[] Obj(Action<Utf8JsonWriter> a)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) { w.WriteStartObject(); a(w); w.WriteEndObject(); }
        return ms.ToArray();
    }

    public static byte[] Setup(RoomSetup s) => Obj(w =>
    {
        w.WriteNumber("v", s.Ver);
        w.WriteString("code", s.Code);
        w.WriteString("host", s.Host);
        w.WriteString("hostId", s.HostId);
        if (s.Guest is null) w.WriteNull("guest"); else w.WriteString("guest", s.Guest);
        if (s.GuestId is null) w.WriteNull("guestId"); else w.WriteString("guestId", s.GuestId);
        w.WriteNumber("seats", s.Seats);
        w.WriteString("seed", s.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteString("status", s.Status);
        w.WriteBoolean("private", s.Private);
        w.WriteNumber("ts", s.Ts);
    });

    public static RoomSetup? ParseSetup(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > 2048) return null;
        try
        {
            using var d = JsonDocument.Parse(payload);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string? code = Str(r, "code"), host = Str(r, "host"), hostId = Str(r, "hostId"), seed = Str(r, "seed"), status = Str(r, "status");
            if (code is null || host is null || hostId is null || seed is null || status is null) return null;
            if (!ulong.TryParse(seed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sd)) return null;
            long ts = r.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0;
            int ver = r.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            bool priv = r.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True;
            return new RoomSetup(code, Clean(host), hostId, Str(r, "guest") is { } g ? Clean(g) : null, Str(r, "guestId"), sd, status, priv, ts, ver);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
    }

    private static string? Str(JsonElement r, string name)
        => r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    public static string Clean(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim()) { if (!char.IsControl(ch)) sb.Append(ch); if (sb.Length >= 16) break; }
        return sb.ToString().Trim();
    }

    // events on room/{code}/ev. Every event carries t (kind) and id (the sender's client id).
    public static byte[] Join(string id, string nick) => Obj(w => { w.WriteString("t", "join"); w.WriteString("id", id); w.WriteString("nick", nick); });
    public static byte[] Order(string id, DuelOrder o) => Obj(w =>
    {
        w.WriteString("t", "order"); w.WriteString("id", id); w.WriteNumber("seat", o.Seat); w.WriteNumber("round", o.Round);
        w.WriteStartArray("plays"); foreach (var p in o.Plays) w.WriteStringValue(p); w.WriteEndArray();
        w.WriteString("prev", o.Prev);
    });
    public static byte[] Hash(string id, int seat, int round, string h) => Obj(w => { w.WriteString("t", "hash"); w.WriteString("id", id); w.WriteNumber("seat", seat); w.WriteNumber("round", round); w.WriteString("h", h); });
    public static byte[] Sync(string id, int from) => Obj(w => { w.WriteString("t", "sync"); w.WriteString("id", id); w.WriteNumber("from", from); });
    public static byte[] Simple(string t, string id) => Obj(w => { w.WriteString("t", t); w.WriteString("id", id); });
    public static byte[] Forfeit(string id, int winner) => Obj(w => { w.WriteString("t", "forfeit"); w.WriteString("id", id); w.WriteNumber("winner", winner); });

    public sealed record Ev(string T, string Id, JsonElement Root, JsonDocument Doc)
    {
        public int Int(string name, int dflt = -1) => Root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : dflt;
        public string? Text(string name) => Root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        public string[]? Plays()
        {
            if (!Root.TryGetProperty("plays", out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() > 64) return null;
            var l = new List<string>();
            foreach (var x in a.EnumerateArray()) { if (x.ValueKind != JsonValueKind.String) return null; l.Add(x.GetString() ?? ""); }
            return l.ToArray();
        }
    }

    public static Ev? ParseEvent(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > 4096) return null;
        try
        {
            var d = JsonDocument.Parse(payload);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { d.Dispose(); return null; }
            string? t = Str(r, "t"), id = Str(r, "id");
            if (t is null || id is null) { d.Dispose(); return null; }
            return new Ev(t, id, r, d);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>The lobby and the room around a duel: who is connected, which rooms exist, who sits where, the clocks and the message
/// traffic. It is SESSION state, not game state, and it is listed honestly as such: nothing here is a Pleroma store. The game itself
/// is DuelMatch (every client runs one from the same seed and orders).
///
/// TRUST: the broker is public and anonymous, so seat claims are not authenticated. A client id is just a random label; the session
/// ignores orders whose label is not the one seated in that seat, which stops accidents, not a determined cheat. It is a casual
/// game: anyone who knows the room can see it.
///
/// Threading: built for one thread (Blazor WebAssembly, or a proof that drives it in order); there are no locks.</summary>
public sealed class PlenumSession : IAsyncDisposable
{
    public const int HeartbeatMs = 30_000, StaleMs = 90_000, TurnMs = 90_000, ForfeitWaitMs = 60_000, StuckMs = 120_000;
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0 O 1 I

    private readonly IPlenumRelayFactory _factory;
    private readonly IPlenumClock _clock;
    private IPlenumRelay? _relay;
    private int _gen;
    private readonly Dictionary<string, RoomSetup> _rooms = new();
    private readonly Dictionary<(int Seat, int Round), string> _reported = new();
    private readonly Dictionary<int, DuelOrder> _mine = new();
    private int _hashesPublished;
    private long _beatMs, _roundStartMs;
    private long? _oppGoneMs;
    private bool _joinSent, _finishedCleared;
    private int _staleCount = -1;
    private int _forfeitSeat = -1;
    private bool _haltSent;

    public PlenumSession(IPlenumRelayFactory factory, IPlenumClock clock, string? nick = null)
    {
        _factory = factory;
        _clock = clock;
        MyId = "p" + NewToken(8);
        Nick = nick ?? "";
    }

    public event Action? Changed;
    private void Raise() { try { Changed?.Invoke(); } catch { } }

    public string MyId { get; }
    private string _nick = "";
    public string Nick { get => _nick; set => _nick = PlenumJson.Clean(value ?? ""); }

    public SessionPhase Phase { get; private set; }
    /// <summary>A plain sentence for the visitor when something went wrong (not a verdict, just what happened).</summary>
    public string? Problem { get; private set; }
    public bool LinkLost { get; private set; }

    // room
    public string Code { get; private set; } = "";
    public RoomSetup? Setup { get; private set; }
    public RoomRole Role { get; private set; }
    public int Seat => Role == RoomRole.Host ? 0 : Role == RoomRole.Guest ? 1 : -1;
    public DuelMatch? Match { get; private set; }
    public bool OpponentGone => _oppGoneMs is not null;
    public bool IsPlayer => Role is RoomRole.Host or RoomRole.Guest;
    public string OpponentNick => Role == RoomRole.Host ? (Setup?.Guest ?? "") : Role == RoomRole.Guest ? (Setup?.Host ?? "") : "";
    public string? OpponentId => Role == RoomRole.Host ? Setup?.GuestId : Role == RoomRole.Guest ? Setup?.HostId : null;

    // ------------------------------------------------------------------------------------------------ clocks

    public int GoneSeconds => _oppGoneMs is null ? 0 : (int)Math.Max(0, (_clock.NowMs - _oppGoneMs.Value) / 1000);
    public bool CanClaimForfeit => IsPlayer && Match is { Over: false } && _oppGoneMs is not null && _clock.NowMs - _oppGoneMs.Value >= ForfeitWaitMs && Match.HaltReason is null;
    public int ForfeitSecondsLeft => _oppGoneMs is null ? 0 : (int)Math.Max(0, (ForfeitWaitMs - (_clock.NowMs - _oppGoneMs.Value) + 999) / 1000);
    public bool MyOrderIn => Match is not null && IsPlayer && Match.HasOrder(Seat, Match.Round);
    public bool OpponentOrderIn => Match is not null && IsPlayer && Match.HasOrder(1 - Seat, Match.Round);
    /// <summary>Seconds left on my turn clock, or -1 when it does not apply (I already submitted, or no match is running).</summary>
    public int TurnSecondsLeft
    {
        get
        {
            if (!IsPlayer || Match is not { Over: false } || Match.HaltReason is not null || MyOrderIn) return -1;
            return (int)Math.Max(0, (TurnMs - (_clock.NowMs - _roundStartMs) + 999) / 1000);
        }
    }
    /// <summary>True when I have submitted and the other side has not for longer than the stuck limit.</summary>
    public bool OpponentSeemsStuck => IsPlayer && Match is { Over: false } && MyOrderIn && !OpponentOrderIn && !OpponentGone && _clock.NowMs - _roundStartMs >= StuckMs;

    // ------------------------------------------------------------------------------------------------ lobby

    /// <summary>Rooms worth listing: public, same rules version, heartbeat no older than 90 s. Newest first.</summary>
    public IReadOnlyList<RoomSetup> Rooms
    {
        get
        {
            long unix = _clock.UnixMs;
            return _rooms.Values.Where(r => !r.Private && r.Ver == PlenumJson.RulesVersion && unix - r.Ts <= StaleMs).OrderByDescending(r => r.Ts).ToList();
        }
    }

    public static string NewToken(int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(CodeAlphabet.ToLowerInvariant()[Random.Shared.Next(CodeAlphabet.Length)]);
        return sb.ToString();
    }

    public static string NewCode()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 6; i++) sb.Append(CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>Upper-case the text, drop spaces and dashes, and return null unless it is a well-formed code.</summary>
    public static string? NormalizeCode(string? text)
    {
        if (text is null) return null;
        var sb = new StringBuilder();
        foreach (var ch in text.ToUpperInvariant()) if (ch != ' ' && ch != '-') sb.Append(ch);
        var s = sb.ToString();
        return s.Length == 6 && s.All(c => CodeAlphabet.Contains(c)) ? s : null;
    }

    private async Task CloseRelayAsync()
    {
        _gen++;
        var r = _relay;
        _relay = null;
        if (r is not null) { try { await r.DisconnectAsync(); } catch { } try { await r.DisposeAsync(); } catch { } }
    }

    private IPlenumRelay NewRelay(string id)
    {
        var r = _factory.Create(id);
        int gen = ++_gen;
        r.OnMessage += m => { if (gen == _gen) HandleMessage(m); };
        r.OnDisconnect += _ => { if (gen == _gen) { LinkLost = true; Raise(); } };
        r.OnReconnected += () => { if (gen == _gen) _ = HandleReconnectedAsync(); };
        _relay = r;
        return r;
    }

    private void ResetRoom()
    {
        Code = ""; Setup = null; Role = RoomRole.None; Match = null; _oppGoneMs = null; _joinSent = false; _finishedCleared = false;
        _reported.Clear(); _mine.Clear(); _hashesPublished = 0; LinkLost = false;
    }

    /// <summary>Connect to the relay and listen for rooms. If the relay cannot be reached the phase is Unreachable (solo still works).</summary>
    public async Task OpenLobbyAsync()
    {
        await CloseRelayAsync();
        ResetRoom();
        _rooms.Clear();
        Phase = SessionPhase.Connecting; Problem = null; Raise();
        var relay = NewRelay(MyId + "-l");
        try
        {
            await relay.ConnectAsync(null);
            Phase = SessionPhase.Lobby;                         // before subscribing: the retained listings arrive during the subscribe
            await relay.SubscribeAsync(PlenumTopics.LobbyFilter);
        }
        catch (Exception)
        {
            Phase = SessionPhase.Unreachable;
            Problem = "Can't reach the lobby. Solo still works.";
            await CloseRelayAsync();
        }
        Raise();
    }

    private void Fail(string problem) { Problem = problem; Raise(); }

    public async Task<bool> CreateRoomAsync(bool isPrivate, ulong? seed = null)
    {
        if (Phase != SessionPhase.Lobby) return false;
        if (Nick.Length == 0) { Fail("Choose a nickname first."); return false; }
        string code = NewCode();
        for (int i = 0; i < 20 && _rooms.ContainsKey(code); i++) code = NewCode();
        await CloseRelayAsync();
        ResetRoom();
        Problem = null;
        Code = code;
        var setup = new RoomSetup(code, Nick, MyId, null, null, seed ?? (ulong)Random.Shared.NextInt64(), "open", isPrivate, _clock.UnixMs, PlenumJson.RulesVersion);
        string topic = SetupTopic(setup);
        var relay = NewRelay(MyId);
        try
        {
            // the will clears the room's retained setup, so a host that vanishes takes its listing with it
            await relay.ConnectAsync(new RelayWill(topic, Array.Empty<byte>(), true, 1));
            await relay.SubscribeAsync(PlenumTopics.Events(code));
            Setup = setup; Role = RoomRole.Host; Phase = SessionPhase.Room;
            await PublishSetupAsync();
        }
        catch (Exception)
        {
            await CloseRelayAsync();
            Phase = SessionPhase.Unreachable; Problem = "Can't reach the lobby. Solo still works.";
        }
        Raise();
        return Phase == SessionPhase.Room;
    }

    private static string SetupTopic(RoomSetup s) => s.Private ? PlenumTopics.Private(s.Code) : PlenumTopics.Lobby(s.Code);

    private async Task PublishSetupAsync()
    {
        if (Setup is null || _relay is null) return;
        Setup = Setup with { Ts = _clock.UnixMs };
        _beatMs = _clock.NowMs;
        await _relay.PublishAsync(SetupTopic(Setup), PlenumJson.Setup(Setup), true, 1);
    }

    /// <summary>Join (or watch) a room by code. Returns false with Problem set if there is no such room.</summary>
    public async Task<bool> JoinAsync(string codeText)
    {
        if (Phase != SessionPhase.Lobby) return false;
        if (Nick.Length == 0) { Fail("Choose a nickname first."); return false; }
        var code = NormalizeCode(codeText);
        if (code is null) { Fail("A room code is 6 letters and digits, like K7Q2MX."); return false; }
        await CloseRelayAsync();
        ResetRoom();
        Problem = null;
        Code = code;
        _joinSent = true;                                       // I came to take the free seat if there is one
        Phase = SessionPhase.Connecting; Raise();
        var relay = NewRelay(MyId);
        try
        {
            await relay.ConnectAsync(new RelayWill(PlenumTopics.Events(code), PlenumJson.Simple("gone", MyId), false, 0));
            Phase = SessionPhase.Room; Role = RoomRole.Pending;
            await relay.SubscribeAsync(PlenumTopics.Events(code));
            await relay.SubscribeAsync(PlenumTopics.Lobby(code));
            await relay.SubscribeAsync(PlenumTopics.Private(code));
        }
        catch (Exception)
        {
            await CloseRelayAsync();
            ResetRoom();
            Phase = SessionPhase.Unreachable; Problem = "Can't reach the lobby. Solo still works.";
            Raise();
            return false;
        }
        if (!await WaitFor(() => Setup is not null, 3000))
        {
            await OpenLobbyAsync();
            Problem = "There is no open room with that code.";
            Raise();
            return false;
        }
        if (Setup!.HostId != MyId && Setup.GuestId is null && Setup.Ver == PlenumJson.RulesVersion)
        {
            await relay.PublishAsync(PlenumTopics.Events(code), PlenumJson.Join(MyId, Nick), false, 1);
            await WaitFor(() => Role != RoomRole.Pending, 3000);
        }
        else RecomputeRole();
        if (Role == RoomRole.Pending)
        {
            await OpenLobbyAsync();
            Problem = "The host did not answer. Try again.";
            Raise();
            return false;
        }
        if (Setup!.Ver != PlenumJson.RulesVersion)
        {
            await OpenLobbyAsync();
            Problem = "That room uses another version of the game.";
            Raise();
            return false;
        }
        if (Role == RoomRole.Spectator) await SendSyncAsync(1);
        Raise();
        return true;
    }

    private static async Task<bool> WaitFor(Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds >= ms) return false;
            await Task.Delay(15);
        }
        return true;
    }

    private async Task SayGoodbyeAsync()
    {
        if (Phase == SessionPhase.Room && _relay is not null)
        {
            try
            {
                if (Role == RoomRole.Host && Setup is not null)
                {
                    await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Simple("bye", MyId), false, 1);
                    await _relay.PublishAsync(SetupTopic(Setup), Array.Empty<byte>(), true, 1);
                }
                else if (IsPlayer) await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Simple("bye", MyId), false, 1);
            }
            catch { }
        }
    }

    /// <summary>Leave the room (the other side hears a goodbye, a host's listing is cleared) and go back to the lobby list.</summary>
    public async Task LeaveAsync()
    {
        await SayGoodbyeAsync();
        await OpenLobbyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await SayGoodbyeAsync();
        await CloseRelayAsync();
        Phase = SessionPhase.Idle;
    }

    // -------------------------------------------------------------------------------------------- incoming

    private void HandleMessage(RelayMessage m)
    {
        try
        {
            if (Phase == SessionPhase.Lobby) HandleLobby(m);
            else if (Phase == SessionPhase.Room)
            {
                if (m.Topic == PlenumTopics.Events(Code)) HandleEvent(m);
                else if (m.Topic == PlenumTopics.Lobby(Code) || m.Topic == PlenumTopics.Private(Code)) HandleSetup(m);
            }
        }
        catch (Exception) { /* a malformed message from a public broker must never take the page down */ }
    }

    private void HandleLobby(RelayMessage m)
    {
        const string prefix = PlenumTopics.Root + "lobby/";
        if (!m.Topic.StartsWith(prefix, StringComparison.Ordinal)) return;
        string code = m.Topic[prefix.Length..];
        if (m.Payload.Length == 0) { _rooms.Remove(code); Raise(); return; }
        var s = PlenumJson.ParseSetup(m.Payload);
        if (s is null || s.Code != code) return;
        _rooms[code] = s;
        Raise();
    }

    private void HandleSetup(RelayMessage m)
    {
        if (Role == RoomRole.Host) return;                      // the host is the author of the setup
        if (m.Payload.Length == 0)
        {
            if (Setup is not null && _oppGoneMs is null && Match is { Over: false }) _oppGoneMs = _clock.NowMs;   // the host's listing was cleared
            Raise();
            return;
        }
        var s = PlenumJson.ParseSetup(m.Payload);
        if (s is null || s.Code != Code) return;
        if (Setup is not null && s.HostId != Setup.HostId) return;
        if (Setup is not null && Match is not null && s.Seed != Setup.Seed) return;
        Setup = s;
        if (Role != RoomRole.Host && _oppGoneMs is not null && OpponentId == s.HostId) _oppGoneMs = null;   // the host is back
        RecomputeRole();
        Raise();
    }

    private void RecomputeRole()
    {
        if (Setup is null) return;
        var old = Role;
        if (Setup.HostId == MyId) Role = RoomRole.Host;
        else if (Setup.GuestId == MyId) Role = RoomRole.Guest;
        else if (_joinSent && Setup.GuestId is null) Role = RoomRole.Pending;
        else Role = RoomRole.Spectator;
        EnsureMatch();
        if (old != Role) Raise();
    }

    private void EnsureMatch()
    {
        if (Match is not null || Setup is null || Setup.GuestId is null) return;
        Match = new DuelMatch(Setup.Seed);
        _roundStartMs = _clock.NowMs;
    }

    private void HandleEvent(RelayMessage m)
    {
        var ev = PlenumJson.ParseEvent(m.Payload);
        if (ev is null) return;
        using (ev.Doc)
        {
            if (ev.Id == MyId || Setup is null) return;
            int seat = Setup.SeatOf(ev.Id);
            if (OpponentId is { } opp && ev.Id == opp && ev.T is not ("gone" or "bye")) _oppGoneMs = null;     // any sign of life
            switch (ev.T)
            {
                case "join": OnJoin(ev); break;
                case "order": OnOrder(ev, seat); break;
                case "hash": OnHash(ev, seat); break;
                case "sync": _ = ReplayAsync(ev.Int("from", 1)); break;
                case "bye": OnLeft(ev.Id, true); break;
                case "gone": OnLeft(ev.Id, false); break;
                case "forfeit": OnForfeit(ev, seat); break;
                case "halt": OnHalt(seat); break;
            }
        }
    }

    private void OnJoin(PlenumJson.Ev ev)
    {
        if (Role != RoomRole.Host || Setup is null) return;
        string nick = PlenumJson.Clean(ev.Text("nick") ?? "Guest");
        if (nick.Length == 0) nick = "Guest";
        if (Setup.GuestId is null)
        {
            Setup = Setup with { Guest = nick, GuestId = ev.Id, Status = "playing" };
            EnsureMatch();
        }
        else if (Setup.GuestId != ev.Id) return;                // the seat is taken: the visitor will see a full room and watch
        _oppGoneMs = null;
        _ = PublishSetupAsync();
        Raise();
    }

    private void OnOrder(PlenumJson.Ev ev, int seat)
    {
        if (Match is null) return;
        if (!PlenumFaults.TrustAnySeat && (seat < 0 || seat != ev.Int("seat"))) return;     // only the seated client may order for that seat
        int sseat = PlenumFaults.TrustAnySeat ? ev.Int("seat") : seat;
        var plays = ev.Plays();
        string? prev = ev.Text("prev");
        if (plays is null || prev is null) return;
        var res = Match.Submit(new DuelOrder(sseat, ev.Int("round"), plays, prev));
        AfterSubmit(res);
    }

    private void OnHash(PlenumJson.Ev ev, int seat)
    {
        if (seat < 0 || seat != ev.Int("seat")) return;
        int round = ev.Int("round");
        string? h = ev.Text("h");
        if (round < 1 || h is null || h.Length > 32) return;
        _reported[(seat, round)] = h;
        CheckHash(seat, round);
    }

    private void CheckHash(int seat, int round)
    {
        if (Match is null || PlenumFaults.SkipHashCompare) return;
        if (Match.HaltReason is not null || round > Match.ResolvedRounds) return;   // compared when the round resolves here
        if (_reported.TryGetValue((seat, round), out var h) && h != Match.HashAfter(round))
        {
            Match.Halt($"Out of sync after round {round}: this device and the other one no longer agree about the cards. The match is stopped.");
            NoteHalt();
            Raise();
        }
    }

    private void OnLeft(string id, bool deliberate)
    {
        if (Setup is null) return;
        int seat = Setup.SeatOf(id);
        if (seat < 0 || !IsPlayer || id != OpponentId) return;
        if (Match is null)
        {
            if (Role == RoomRole.Host && seat == 1)
            {
                Setup = Setup with { Guest = null, GuestId = null, Status = "open" };
                _ = PublishSetupAsync();
            }
            Raise();
            return;
        }
        if (Match.Over) return;
        _oppGoneMs = _clock.NowMs - (deliberate ? ForfeitWaitMs : 0);
        Raise();
    }

    private void OnHalt(int seat)
    {
        if (Match is null || seat < 0 || Match.HaltReason is not null) return;
        _haltSent = true;                                       // the other device already stopped; no need to say it again
        Match.Halt("Out of sync: the other device stopped the match after round " + Match.ResolvedRounds + ". Its cards and yours no longer agree.");
        Problem = Match.HaltReason;
        Raise();
    }

    /// <summary>If this device has halted the match, tell the other side once, so both stop together.</summary>
    private void NoteHalt()
    {
        if (Match?.HaltReason is null) return;
        Problem = Match.HaltReason;
        if (_haltSent || _relay is null || !IsPlayer) return;
        _haltSent = true;
        _ = _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Simple("halt", MyId), false, 1);
    }

    private void OnForfeit(PlenumJson.Ev ev, int seat)
    {
        if (Match is null || seat < 0 || ev.Int("winner") != seat) return;
        Match.Forfeit(seat, "The other side left the match.");
        Raise();
    }

    private void AfterSubmit(SubmitResult res)
    {
        if (Match is null) return;
        NoteHalt();
        if (res == SubmitResult.Resolved || Match.ResolvedRounds > _hashesPublished) AfterResolve();
        Raise();
    }

    private void AfterResolve()
    {
        if (Match is null) return;
        _roundStartMs = _clock.NowMs;
        while (_hashesPublished < Match.ResolvedRounds)
        {
            int r = ++_hashesPublished;
            if (IsPlayer && _relay is not null)
                _ = _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Hash(MyId, Seat, r, Match.HashAfter(r)), false, 1);
            for (int s = 0; s < 2; s++) CheckHash(s, r);
        }
        if (Match.Over && Role == RoomRole.Host && !_finishedCleared && Setup is not null)
        {
            _finishedCleared = true;                            // a finished room leaves the list
            if (_relay is not null) _ = _relay.PublishAsync(SetupTopic(Setup), Array.Empty<byte>(), true, 1);
        }
    }

    // --------------------------------------------------------------------------------------------- outgoing

    /// <summary>Submit my order for the current round (plays are tokens, see Play). Returns false if there is nothing to submit.</summary>
    public async Task<bool> SubmitAsync(IReadOnlyList<string> plays)
    {
        if (Match is null || _relay is null) return false;
        if (!IsPlayer && !PlenumFaults.SpectatorsMaySubmit) return false;
        int seat = Role == RoomRole.Guest ? 1 : 0;
        int round = Match.Round;
        if (Match.Over || Match.HaltReason is not null || Match.HasOrder(seat, round)) return false;
        var order = new DuelOrder(seat, round, plays.ToArray(), Match.Hash());
        var res = Match.Submit(order);
        if (res == SubmitResult.Invalid) return false;
        _mine[round] = order;
        await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Order(MyId, order), false, 1);
        AfterSubmit(res);
        return true;
    }

    private async Task SendSyncAsync(int from)
    {
        if (_relay is null) return;
        await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Sync(MyId, from), false, 1);
    }

    /// <summary>Answer a sync request: send my own orders from that round on, and my round hashes. Orders are idempotent on arrival.</summary>
    private async Task ReplayAsync(int from)
    {
        if (!IsPlayer || _relay is null || Match is null) return;
        from = Math.Max(1, from);
        int sent = 0;
        foreach (var kv in _mine.OrderBy(k => k.Key))
        {
            if (kv.Key < from) continue;
            if (++sent > 200) break;
            await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Order(MyId, kv.Value), false, 1);
        }
        if (Match.Over && _forfeitSeat == Seat) await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Forfeit(MyId, Seat), false, 1);
        for (int r = Math.Max(1, from); r <= Match.ResolvedRounds && r <= from + 200; r++)
            await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Hash(MyId, Seat, r, Match.HashAfter(r)), false, 1);
    }

    public async Task ClaimForfeitAsync()
    {
        if (!CanClaimForfeit || Match is null || _relay is null) return;
        _forfeitSeat = Seat;
        Match.Forfeit(Seat, "The other side left the match, so it is awarded here.");
        await _relay.PublishAsync(PlenumTopics.Events(Code), PlenumJson.Forfeit(MyId, Seat), false, 1);
        AfterResolve();
        Raise();
    }

    private async Task HandleReconnectedAsync()
    {
        LinkLost = false;
        try
        {
            if (Phase == SessionPhase.Room && _relay is not null)
            {
                if (Role == RoomRole.Host) await PublishSetupAsync();            // the will cleared it when the link dropped
                await SendSyncAsync(Match?.Round ?? 1);                          // ask for what I missed; also tells the other side I am back
            }
        }
        catch { }
        Raise();
    }

    /// <summary>The tab came back to the foreground: ask the other side to replay anything missed while this one was away.</summary>
    public async Task OnVisibleAsync()
    {
        if (Phase != SessionPhase.Room || Match is null) return;
        try { await SendSyncAsync(Match.Round); } catch { }
    }

    /// <summary>Called a few times a second by the page: the host's heartbeat, the stale-entry sweep, and my turn clock.</summary>
    public async Task TickAsync()
    {
        long now = _clock.NowMs;
        if (Phase == SessionPhase.Lobby)
        {
            int n = Rooms.Count;
            if (n != _staleCount) { _staleCount = n; Raise(); }
            return;
        }
        if (Phase != SessionPhase.Room) return;
        if (Role == RoomRole.Host && Setup is not null && !_finishedCleared && now - _beatMs >= HeartbeatMs)
            await PublishSetupAsync();
        if (IsPlayer && Match is { Over: false } && Match.HaltReason is null && !MyOrderIn && now - _roundStartMs >= TurnMs)
            await SubmitAsync(Array.Empty<string>());                            // out of time: the unsubmitted plays are forfeited
    }
}
