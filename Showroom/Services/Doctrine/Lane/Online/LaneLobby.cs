using System.Security.Cryptography;
using ShowroomPlenum;

namespace ShowroomLane;

// THE LOBBY of the online lane battle. A host's open room is ONE retained message on plenum/v1/doctrine-lane/lobby/<code> (the host's name, the build, a time), repeated every
// few seconds while the room waits and cleared when a guest is seated, when the host leaves, or by the broker's last will if the host's connection dies. A browser of the list reads
// those retained messages; an entry that has not been heard from for TtlMs is dropped. The list is public: everyone who connects can read it. It never holds a match number.

[Flags]
internal enum LaneLobbyProbe
{
    None = 0,
    /// <summary>Entries never expire.</summary>
    NoExpiry = 1,
    /// <summary>A room of another build is listed.</summary>
    ShowsOtherBuilds = 2,
    /// <summary>A message whose code differs from its topic is believed.</summary>
    CodeNotChecked = 4,
}

/// <summary>One room in the list. <see cref="AgeSeconds"/> is how long since the host was last heard from.</summary>
public sealed record LaneLobbyEntry(string Code, string Host, int AgeSeconds);

public sealed class LaneLobbyOptions
{
    public IPlenumRelayFactory Relays { get; set; } = null!;
    public IPlenumClock Clock { get; set; } = new StopwatchPlenumClock();
    public string GameTag { get; set; } = LaneOnlineProtocol.GameTag;
    /// <summary>An entry not heard from for this long is not shown. The host repeats its entry every 20 s, so this is three missed beats and a little.</summary>
    public int TtlMs { get; set; } = 75_000;
    public int MaxRooms { get; set; } = 30;
    internal LaneLobbyProbe Probe { get; set; }
}

/// <summary>The list of open rooms (the "Open rooms" screen). Connects, listens, expires; it never publishes.</summary>
public sealed class LaneLobby : IAsyncDisposable
{
    sealed class Entry { public string Host = ""; public string Ver = ""; public long LastSeenUnix; }

    readonly LaneLobbyOptions _o;
    readonly object _k = new();
    readonly Dictionary<string, Entry> _e = new();
    IPlenumRelay? _relay;
    int _lastSig;

    public LaneLobby(LaneLobbyOptions options) { _o = options ?? throw new ArgumentNullException(nameof(options)); if (_o.Relays is null) throw new ArgumentException("Relays is required", nameof(options)); }

    public event Action? Changed;
    /// <summary>True while the connection to the relay is up.</summary>
    public bool Online { get; private set; }
    /// <summary>Set when the relay could not be reached (plain words).</summary>
    public string Problem { get; private set; } = "";

    public async Task StartAsync()
    {
        if (_relay is not null) return;
        var relay = _o.Relays.Create("lb" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant());
        _relay = relay;
        relay.OnMessage += OnMessage;
        relay.OnDisconnect += _ => { Online = false; Raise(); };
        relay.OnReconnected += () => { Online = true; Raise(); };
        try
        {
            await relay.ConnectAsync();
            Online = true; Problem = "";
            await relay.SubscribeAsync(LaneOnlineProtocol.LobbyFilter);
        }
        catch (Exception e)
        {
            Online = false; Problem = "Could not reach the message relay (" + e.Message + ").";
            try { await relay.DisposeAsync(); } catch { }
            _relay = null;
        }
        Raise();
    }

    void OnMessage(RelayMessage m)
    {
        if (!m.Topic.StartsWith(LaneOnlineProtocol.LobbyRoot, StringComparison.Ordinal)) return;
        var code = m.Topic[LaneOnlineProtocol.LobbyRoot.Length..];
        if (!LaneOnlineProtocol.ValidCode(code)) return;
        bool changed = false;
        lock (_k)
        {
            if (m.Payload.Length == 0) { changed = _e.Remove(code); }
            else
            {
                var msg = LaneWire.ReadLobby(m.Payload);
                if (msg is null || (msg.Code != code && !_o.Probe.HasFlag(LaneLobbyProbe.CodeNotChecked))) return;
                long now = _o.Clock.UnixMs;
                // a retained entry is as old as its time says (a host that died leaves nothing newer); a live one is fresh now. A time in the future (a fast clock) counts as now.
                long seen = m.Retained ? (msg.Ts > now ? now : msg.Ts) : now;
                if (!_e.TryGetValue(code, out var en)) _e[code] = en = new Entry();
                en.Host = msg.Host; en.Ver = msg.Ver; en.LastSeenUnix = Math.Max(en.LastSeenUnix, seen);
                changed = true;
            }
        }
        if (changed) Raise();
    }

    /// <summary>The rooms waiting for a guest now, newest first. Expired entries and rooms of another build are left out (and the expired ones are forgotten).</summary>
    public IReadOnlyList<LaneLobbyEntry> Rooms
    {
        get
        {
            long now = _o.Clock.UnixMs;
            lock (_k)
            {
                if (!_o.Probe.HasFlag(LaneLobbyProbe.NoExpiry))
                    foreach (var code in _e.Where(x => now - x.Value.LastSeenUnix > _o.TtlMs).Select(x => x.Key).ToList()) _e.Remove(code);
                return _e.Where(x => x.Value.Ver == _o.GameTag || _o.Probe.HasFlag(LaneLobbyProbe.ShowsOtherBuilds))
                    .OrderByDescending(x => x.Value.LastSeenUnix).ThenBy(x => x.Key, StringComparer.Ordinal).Take(_o.MaxRooms)
                    .Select(x => new LaneLobbyEntry(x.Key, x.Value.Host, (int)Math.Max(0, (now - x.Value.LastSeenUnix) / 1000))).ToList();
            }
        }
    }

    /// <summary>Call about once a second: drops expired entries and says Changed when the list or an age (in whole seconds) moved.</summary>
    public void Tick()
    {
        var rooms = Rooms;
        int sig = 17;
        foreach (var r in rooms) sig = HashCode.Combine(sig, r.Code, r.Host, r.AgeSeconds / 5);
        sig = HashCode.Combine(sig, rooms.Count);
        if (sig != _lastSig) { _lastSig = sig; Raise(); }
    }

    void Raise() { try { Changed?.Invoke(); } catch { } }

    public async ValueTask DisposeAsync()
    {
        var relay = _relay; _relay = null; Online = false;
        if (relay is null) return;
        try { await relay.DisconnectAsync(); } catch { }
        try { await relay.DisposeAsync(); } catch { }
    }
}

/// <summary>The host's side of the lobby: publishes the room's one entry, repeats it, and clears it. Own connection with a last will that clears the entry if the host's browser dies.</summary>
public sealed class LaneLobbyBeacon : IAsyncDisposable
{
    readonly IPlenumRelayFactory _relays;
    readonly IPlenumClock _clock;
    readonly string _code, _host, _ver;
    IPlenumRelay? _relay;
    long _lastBeatMono;

    public LaneLobbyBeacon(IPlenumRelayFactory relays, IPlenumClock clock, string code, string host, string ver) { _relays = relays; _clock = clock; _code = code; _host = host; _ver = ver; }

    public const int BeatMs = 20_000;
    public bool Active => _relay is not null;

    public async Task StartAsync()
    {
        if (_relay is not null) return;
        var relay = _relays.Create("lbb" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant());
        _relay = relay;
        try
        {
            await relay.ConnectAsync(new RelayWill(LaneTopics.Lobby(_code), Array.Empty<byte>(), true, 1));
            await PublishAsync();
        }
        catch { try { await relay.DisposeAsync(); } catch { } _relay = null; throw; }
    }

    async Task PublishAsync()
    {
        var r = _relay; if (r is null) return;
        _lastBeatMono = _clock.NowMs;
        await r.PublishAsync(LaneTopics.Lobby(_code), LaneWire.Lobby(new LaneLobbyMsg(_code, _host, _ver, _clock.UnixMs)), true, 1);
    }

    /// <summary>Repeats the entry when a beat is due (the session calls this from its ticker).</summary>
    public async Task BeatAsync()
    {
        if (_relay is null || _clock.NowMs - _lastBeatMono < BeatMs) return;
        try { await PublishAsync(); } catch { }
    }

    /// <summary>Clears the entry and closes the connection (a clean close does not fire the will, so the clear is explicit).</summary>
    public async Task StopAsync()
    {
        var r = _relay; _relay = null;
        if (r is null) return;
        try { if (r.State == RelayState.Connected) await r.PublishAsync(LaneTopics.Lobby(_code), ReadOnlyMemory<byte>.Empty, true, 1); } catch { }
        try { await r.DisconnectAsync(); } catch { }
        try { await r.DisposeAsync(); } catch { }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
