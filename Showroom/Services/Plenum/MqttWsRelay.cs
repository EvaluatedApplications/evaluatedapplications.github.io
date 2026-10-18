using System.Diagnostics;
using System.Net.WebSockets;

namespace ShowroomPlenum;

/// <summary>Where the relay is. ONE setting: window.EA_PLENUM_RELAY at the top of wwwroot/index.html, read once at boot (Program.cs).</summary>
public static class PlenumRelayConfig
{
    public const string DefaultUrl = "wss://79-72-78-90.sslip.io/";
    public static string Url { get; private set; } = DefaultUrl;
    public static void Configure(string? url) { if (!string.IsNullOrWhiteSpace(url)) Url = url.Trim(); }
}

/// <summary>A byte pipe the MQTT client talks over: a WebSocket in the browser, a scripted fake in a proof.</summary>
public interface IMqttTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    /// <summary>Bytes received into the buffer; 0 means the other side closed.</summary>
    Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);
}

/// <summary>MQTT over a binary WebSocket with the "mqtt" subprotocol (the broker's secure listener). ClientWebSocket works in Blazor
/// WebAssembly; it is given nothing but the subprotocol because the browser does not allow other options.</summary>
public sealed class WebSocketMqttTransport : IMqttTransport
{
    private readonly Uri _url;
    private ClientWebSocket? _ws;
    public WebSocketMqttTransport(Uri url) { _url = url; }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.AddSubProtocol("mqtt");
        await _ws.ConnectAsync(_url, ct).ConfigureAwait(false);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _ws!.SendAsync(data, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var r = await _ws!.ReceiveAsync(buffer, ct).ConfigureAwait(false);
        return r.MessageType == WebSocketMessageType.Close ? 0 : r.Count;
    }

    public ValueTask DisposeAsync()
    {
        try { _ws?.Abort(); } catch { }
        try { _ws?.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}

public sealed class MqttRelayOptions
{
    public string Url { get; set; } = PlenumRelayConfig.DefaultUrl;
    public int KeepAliveSeconds { get; set; } = 30;
    public int[] BackoffMs { get; set; } = { 500, 1000, 2000, 4000, 8000, 15000 };
    public bool AutoReconnect { get; set; } = true;
    public int ConnectTimeoutMs { get; set; } = 10_000;
    public int AckTimeoutMs { get; set; } = 10_000;
    /// <summary>Every topic and filter must start with this literal text (the broker also carries other traffic).</summary>
    public string RequiredPrefix { get; set; } = PlenumTopics.Root;
    public Func<IMqttTransport>? TransportFactory { get; set; }
}

public sealed class MqttWsRelayFactory : IPlenumRelayFactory
{
    public IPlenumRelay Create(string clientId) => new MqttWsRelay(clientId, new MqttRelayOptions { Url = PlenumRelayConfig.Url });
}

/// <summary>A small MQTT 3.1.1 client: CONNECT (clean session, keepalive, optional will), SUBSCRIBE/UNSUBSCRIBE, PUBLISH at Qos 0 and 1
/// with retain, PINGREQ on idle, and a reconnect loop with backoff that restores subscriptions and resends unacknowledged Qos 1
/// publishes with DUP set. No third-party MQTT package: the whole client is this file plus MqttCodec.cs, which keeps the
/// trimmed AOT build free of reflection-based libraries.</summary>
public sealed class MqttWsRelay : IPlenumRelay
{
    private readonly MqttRelayOptions _o;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<string> _subs = new();
    private readonly SortedDictionary<ushort, MqttPublish> _inflight = new();
    private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> _acks = new();
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private IMqttTransport? _tr;
    private CancellationTokenSource? _connCts;
    private RelayWill? _will;
    private int _gen;
    private volatile bool _closing;
    private ushort _nextId;
    private long _lastSendMs;
    private long _pingSentMs;
    private int _reconnecting;

    public MqttWsRelay(string clientId, MqttRelayOptions? options = null)
    {
        ClientId = clientId;
        _o = options ?? new MqttRelayOptions();
    }

    public string ClientId { get; }
    public RelayState State { get; private set; }
    public int Unacked { get { lock (_gate) return _inflight.Count; } }
    public event Action<RelayMessage>? OnMessage;
    public event Action<string>? OnDisconnect;
    public event Action? OnReconnected;

    private long Now => _sw.ElapsedMilliseconds;

    // ------------------------------------------------------------------------------------------------ connect

    public async Task ConnectAsync(RelayWill? will = null, CancellationToken ct = default)
    {
        if (State != RelayState.Disconnected) throw new InvalidOperationException("already connected");
        if (will is not null) PlenumTopics.RequireNamespace(will.Topic, _o.RequiredPrefix);
        _will = will;
        _closing = false;
        await ConnectCoreAsync(ct).ConfigureAwait(false);
    }

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        State = RelayState.Connecting;
        var tr = _o.TransportFactory?.Invoke() ?? new WebSocketMqttTransport(new Uri(_o.Url));
        var dec = new MqttStreamDecoder();
        var early = new List<MqttPacket>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_o.ConnectTimeoutMs);
            await tr.ConnectAsync(cts.Token).ConfigureAwait(false);
            var connect = new MqttConnect(ClientId, (ushort)_o.KeepAliveSeconds, true, _will);
            await tr.SendAsync(MqttCodec.Encode(connect), cts.Token).ConfigureAwait(false);
            var buf = new byte[4096];
            var got = new List<MqttPacket>();
            MqttConnAck? ack = null;
            while (ack is null)
            {
                int n = await tr.ReceiveAsync(buf, cts.Token).ConfigureAwait(false);
                if (n == 0) throw new IOException("the relay closed the connection before it answered");
                got.Clear();
                dec.Feed(buf.AsSpan(0, n), got);
                foreach (var p in got)
                {
                    if (ack is null && p is MqttConnAck a) ack = a;
                    else if (ack is not null) early.Add(p);
                }
            }
            if (ack.ReturnCode != 0) throw new IOException($"the relay refused the connection (code {ack.ReturnCode})");
        }
        catch
        {
            State = RelayState.Disconnected;
            await tr.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        int gen;
        lock (_gate)
        {
            gen = ++_gen;
            _tr = tr;
            _connCts?.Cancel();
            _connCts = new CancellationTokenSource();
            _lastSendMs = Now;
            _pingSentMs = 0;
            State = RelayState.Connected;
        }
        var token = _connCts!.Token;
        _ = ReceiveLoop(tr, gen, dec, early, token);
        _ = KeepAliveLoop(tr, gen, token);

        // restore: subscriptions first, then anything the broker never acknowledged
        string[] filters; MqttPublish[] pending;
        lock (_gate) { filters = _subs.ToArray(); pending = _inflight.Values.ToArray(); }
        if (filters.Length > 0)
            await SendPacketAsync(tr, gen, new MqttSubscribe(NextId(), filters.Select(f => (f, 1)).ToList())).ConfigureAwait(false);
        foreach (var p in pending) await SendPacketAsync(tr, gen, p with { Dup = true }).ConfigureAwait(false);
    }

    private ushort NextId()
    {
        lock (_gate)
        {
            for (int i = 0; i < 65535; i++)
            {
                _nextId = (ushort)(_nextId == 65535 ? 1 : _nextId + 1);
                if (!_inflight.ContainsKey(_nextId) && !_acks.ContainsKey(_nextId)) return _nextId;
            }
            throw new IOException("no free packet id");
        }
    }

    // ------------------------------------------------------------------------------------------------- send

    private async Task SendPacketAsync(IMqttTransport tr, int gen, MqttPacket p)
    {
        var bytes = MqttCodec.Encode(p);
        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (gen != _gen) return;
                await tr.SendAsync(bytes, CancellationToken.None).ConfigureAwait(false);
                _lastSendMs = Now;
            }
            finally { _sendLock.Release(); }
        }
        catch (Exception e) { HandleLost(tr, gen, e.Message); }
    }

    private Task SendNowAsync(MqttPacket p)
    {
        IMqttTransport? tr; int gen;
        lock (_gate) { tr = _tr; gen = _gen; }
        return tr is null ? Task.CompletedTask : SendPacketAsync(tr, gen, p);
    }

    public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain = false, int qos = 0)
    {
        PlenumTopics.RequireNamespace(topic, _o.RequiredPrefix);
        if (!PlenumTopics.ValidTopicName(topic)) throw new ArgumentException("bad topic name " + topic);
        if (qos is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(qos));
        if (qos == 0)
        {
            if (State != RelayState.Connected) return Task.CompletedTask;       // at most once: nothing to keep
            return SendNowAsync(new MqttPublish(topic, payload.ToArray(), 0, retain, false, 0));
        }
        MqttPublish pub;
        lock (_gate)
        {
            pub = new MqttPublish(topic, payload.ToArray(), 1, retain, false, NextId());
            _inflight[pub.PacketId] = pub;                                      // kept until PUBACK, resent after a reconnect
        }
        return State == RelayState.Connected ? SendNowAsync(pub) : Task.CompletedTask;
    }

    public async Task SubscribeAsync(string filter)
    {
        PlenumTopics.RequireNamespace(filter, _o.RequiredPrefix);
        if (!PlenumTopics.ValidFilter(filter)) throw new ArgumentException("bad filter " + filter);
        lock (_gate) _subs.Add(filter);
        if (State != RelayState.Connected) return;
        var (id, tcs) = NewAck();
        await SendNowAsync(new MqttSubscribe(id, new[] { (filter, 1) })).ConfigureAwait(false);
        var codes = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(_o.AckTimeoutMs)).ConfigureAwait(false);
        if (codes.Length > 0 && codes[0] == 0x80) throw new IOException("the relay refused the subscription " + filter);
    }

    public async Task UnsubscribeAsync(string filter)
    {
        lock (_gate) _subs.Remove(filter);
        if (State != RelayState.Connected) return;
        var (id, tcs) = NewAck();
        await SendNowAsync(new MqttUnsubscribe(id, new[] { filter })).ConfigureAwait(false);
        await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(_o.AckTimeoutMs)).ConfigureAwait(false);
    }

    private (ushort, TaskCompletionSource<byte[]>) NewAck()
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ushort id = NextId();
        lock (_gate) _acks[id] = tcs;
        return (id, tcs);
    }

    // ----------------------------------------------------------------------------------------------- receive

    private async Task ReceiveLoop(IMqttTransport tr, int gen, MqttStreamDecoder dec, List<MqttPacket> early, CancellationToken ct)
    {
        try
        {
            foreach (var p in early) await HandleAsync(tr, gen, p).ConfigureAwait(false);
            var buf = new byte[4096];
            var list = new List<MqttPacket>();
            while (!ct.IsCancellationRequested)
            {
                int n = await tr.ReceiveAsync(buf, ct).ConfigureAwait(false);
                if (n == 0) { HandleLost(tr, gen, "the relay closed the connection"); return; }
                list.Clear();
                dec.Feed(buf.AsSpan(0, n), list);
                foreach (var p in list) await HandleAsync(tr, gen, p).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }      // we closed it ourselves
        catch (Exception e) { HandleLost(tr, gen, e.Message); }                      // anything else (an aborted socket included) is a lost link
    }

    private async Task HandleAsync(IMqttTransport tr, int gen, MqttPacket p)
    {
        _pingSentMs = 0;                                                        // any traffic shows the link is alive
        switch (p)
        {
            case MqttPublish pub:
                if (pub.Qos == 1) await SendPacketAsync(tr, gen, new MqttPubAck(pub.PacketId)).ConfigureAwait(false);
                if (pub.Qos == 2) return;                                       // never requested
                try { OnMessage?.Invoke(new RelayMessage(pub.Topic, pub.Payload, pub.Retain, pub.Qos)); } catch { }
                break;
            case MqttPubAck ack:
                lock (_gate) _inflight.Remove(ack.PacketId);
                break;
            case MqttSubAck sa:
                Complete(sa.PacketId, sa.Codes);
                break;
            case MqttUnsubAck ua:
                Complete(ua.PacketId, Array.Empty<byte>());
                break;
        }
    }

    private void Complete(ushort id, byte[] codes)
    {
        TaskCompletionSource<byte[]>? t;
        lock (_gate) { _acks.Remove(id, out t); }
        t?.TrySetResult(codes);
    }

    private async Task KeepAliveLoop(IMqttTransport tr, int gen, CancellationToken ct)
    {
        int half = Math.Max(10, _o.KeepAliveSeconds * 500);
        try
        {
            while (true)
            {
                await Task.Delay(half, ct).ConfigureAwait(false);
                if (gen != _gen || _closing) return;
                long now = Now;
                if (_pingSentMs != 0 && now - _pingSentMs > _o.KeepAliveSeconds * 1000L) { HandleLost(tr, gen, "keepalive timeout"); return; }
                if (_pingSentMs == 0 && now - _lastSendMs >= half)
                {
                    _pingSentMs = now;
                    await SendPacketAsync(tr, gen, new MqttPingReq()).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ---------------------------------------------------------------------------------------- loss and close

    private void HandleLost(IMqttTransport tr, int gen, string reason)
    {
        List<TaskCompletionSource<byte[]>> pend;
        lock (_gate)
        {
            if (gen != _gen || !ReferenceEquals(_tr, tr)) return;
            _tr = null;
            _gen++;                                                             // stale loops of this connection stop acting
            _connCts?.Cancel();
            State = RelayState.Disconnected;
            pend = _acks.Values.ToList();
            _acks.Clear();
        }
        foreach (var t in pend) t.TrySetException(new IOException("connection lost"));
        _ = tr.DisposeAsync().AsTask();
        if (_closing) return;
        try { OnDisconnect?.Invoke(reason); } catch { }
        if (_o.AutoReconnect) _ = ReconnectLoop();
    }

    private async Task ReconnectLoop()
    {
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1) return;
        int attempt = 0;
        try
        {
            while (!_closing)
            {
                int ms = _o.BackoffMs[Math.Min(attempt, _o.BackoffMs.Length - 1)];
                ms += Random.Shared.Next(0, ms / 5 + 1);
                await Task.Delay(ms).ConfigureAwait(false);
                if (_closing) return;
                try
                {
                    await ConnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
                    Volatile.Write(ref _reconnecting, 0);                       // a loss from here on may start a new loop
                    try { OnReconnected?.Invoke(); } catch { }
                    return;
                }
                catch { attempt++; }
            }
        }
        finally { Volatile.Write(ref _reconnecting, 0); }
    }

    public async Task DisconnectAsync()
    {
        _closing = true;
        IMqttTransport? tr; int gen;
        List<TaskCompletionSource<byte[]>> pend;
        lock (_gate)
        {
            tr = _tr; gen = _gen; _tr = null; _gen++;
            _connCts?.Cancel();
            State = RelayState.Disconnected;
            pend = _acks.Values.ToList(); _acks.Clear();
        }
        foreach (var t in pend) t.TrySetException(new IOException("closed"));
        if (tr is null) return;
        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try { using var c = new CancellationTokenSource(1500); await tr.SendAsync(MqttCodec.Encode(new MqttDisconnect()), c.Token).ConfigureAwait(false); }
            finally { _sendLock.Release(); }
        }
        catch { }
        await tr.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
