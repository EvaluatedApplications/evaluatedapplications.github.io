using System.Text;

namespace ShowroomPlenum;

// The message relay the Plenum duel talks over. The shipped transport is MqttWsRelay (an MQTT 3.1.1 client over a WebSocket, the
// same broker Prism's swarm uses); LoopbackRelay is the in-memory broker the proofs use. Nothing here touches game state.

/// <summary>One message delivered by the relay. Retained is true when the broker sent it because of a new subscription.</summary>
public sealed record RelayMessage(string Topic, byte[] Payload, bool Retained = false, int Qos = 0)
{
    public string Text => Encoding.UTF8.GetString(Payload);
}

/// <summary>The message the broker publishes for a client that vanishes without saying goodbye.</summary>
public sealed record RelayWill(string Topic, byte[] Payload, bool Retain = false, int Qos = 0);

public enum RelayState { Disconnected, Connecting, Connected }

public interface IPlenumRelay : IAsyncDisposable
{
    string ClientId { get; }
    RelayState State { get; }
    /// <summary>A message arrived on a subscribed filter.</summary>
    event Action<RelayMessage>? OnMessage;
    /// <summary>The connection was lost (not raised for a deliberate DisconnectAsync). The argument is a short reason.</summary>
    event Action<string>? OnDisconnect;
    /// <summary>The connection came back after a loss, with every earlier subscription restored. Not raised for the first connect.</summary>
    event Action? OnReconnected;
    /// <summary>Connect once (throws if the relay cannot be reached). A will, if given, is fixed for the life of the relay.</summary>
    Task ConnectAsync(RelayWill? will = null, CancellationToken ct = default);
    Task SubscribeAsync(string filter);
    Task UnsubscribeAsync(string filter);
    /// <summary>Qos 0 = at most once; Qos 1 = kept and resent after a reconnect until the broker acknowledges it.</summary>
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain = false, int qos = 0);
    /// <summary>A clean goodbye: the broker does not send the will.</summary>
    Task DisconnectAsync();
}

public interface IPlenumRelayFactory
{
    IPlenumRelay Create(string clientId);
}

/// <summary>Topic names and filter matching. EVERY Plenum topic lives under <see cref="Root"/>; the relay refuses anything else, so a
/// Plenum client can never publish or subscribe into the other traffic on the same broker.</summary>
public static class PlenumTopics
{
    public const string Root = "plenum/v1/";

    public static string Lobby(string code) => Root + "lobby/" + code;
    public const string LobbyFilter = Root + "lobby/+";
    public static string Private(string code) => Root + "priv/" + code;
    public static string Events(string code) => Root + "room/" + code + "/ev";

    /// <summary>Throws unless the topic or filter starts with the literal Plenum root (so no wildcard can reach outside it).</summary>
    public static void RequireNamespace(string topicOrFilter, string root = Root)
    {
        if (topicOrFilter is null || !topicOrFilter.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException("topic outside the Plenum namespace: " + topicOrFilter);
    }

    /// <summary>MQTT 3.1.1 filter rules: '+' is a whole level, '#' is the whole last level.</summary>
    public static bool ValidFilter(string f)
    {
        if (string.IsNullOrEmpty(f) || f.Length > 65535) return false;
        var lv = f.Split('/');
        for (int i = 0; i < lv.Length; i++)
        {
            var l = lv[i];
            if (l.Contains('#')) { if (l != "#" || i != lv.Length - 1) return false; }
            else if (l.Contains('+') && l != "+") return false;
        }
        return true;
    }

    public static bool ValidTopicName(string t) => !string.IsNullOrEmpty(t) && !t.Contains('#') && !t.Contains('+') && t.Length <= 65535;

    /// <summary>Does a subscription filter match a published topic (MQTT 3.1.1, including that '#' matches the parent level and a
    /// topic starting with '$' is not matched by a leading wildcard).</summary>
    public static bool Matches(string filter, string topic)
    {
        var f = filter.Split('/');
        var t = topic.Split('/');
        if (t[0].StartsWith('$') && (f[0] == "+" || f[0] == "#")) return false;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] == "#") return true;                 // matches this level and everything below, and the parent itself
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }
}

/// <summary>An in-memory broker for proofs: retained messages, wildcard filters, a will fired on an abrupt drop, FIFO delivery
/// (deliveries made while another is being delivered are queued, so a handler can publish without re-entering).</summary>
public sealed class LoopbackBroker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RelayMessage> _retained = new();
    private readonly List<LoopbackRelay> _clients = new();
    private readonly Queue<(LoopbackRelay To, RelayMessage Msg)> _queue = new();
    private bool _pumping;

    /// <summary>Set false to make every connect fail, as an unreachable relay would.</summary>
    public bool Reachable { get; set; } = true;
    public int Published { get; private set; }

    public LoopbackRelay NewClient(string? id = null)
    {
        var c = new LoopbackRelay(this, id ?? "c" + (_clients.Count + 1));
        lock (_gate) _clients.Add(c);
        return c;
    }

    public IReadOnlyList<string> RetainedTopics { get { lock (_gate) return _retained.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(); } }
    public string? RetainedText(string topic) { lock (_gate) return _retained.TryGetValue(topic, out var m) ? m.Text : null; }

    internal void Publish(LoopbackRelay from, string topic, byte[] payload, bool retain, int qos)
    {
        PlenumTopics.RequireNamespace(topic);
        lock (_gate)
        {
            Published++;
            if (retain)
            {
                if (payload.Length == 0) _retained.Remove(topic);
                else _retained[topic] = new RelayMessage(topic, payload, true, qos);
            }
            foreach (var c in _clients)
                if (c.IsConnected && c.Matches(topic)) _queue.Enqueue((c, new RelayMessage(topic, payload, false, qos)));
        }
        Pump();
    }

    internal void Subscribed(LoopbackRelay c, string filter)
    {
        lock (_gate)
            foreach (var m in _retained.Values)
                if (PlenumTopics.Matches(filter, m.Topic)) _queue.Enqueue((c, m));
        Pump();
    }

    /// <summary>Drop a client abruptly (no goodbye): the will is published, then the client hears OnDisconnect.</summary>
    public void Drop(LoopbackRelay c, string reason = "dropped")
    {
        RelayWill? will;
        lock (_gate) { will = c.TakeWillForDrop(); }
        c.MarkLost();
        if (will is not null) Publish(c, will.Topic, will.Payload, will.Retain, will.Qos);
        c.RaiseDisconnect(reason);
    }

    private void Pump()
    {
        lock (_gate) { if (_pumping) return; _pumping = true; }
        try
        {
            while (true)
            {
                (LoopbackRelay To, RelayMessage Msg) item;
                lock (_gate) { if (_queue.Count == 0) return; item = _queue.Dequeue(); }
                if (item.To.IsConnected) item.To.Deliver(item.Msg);
            }
        }
        finally { lock (_gate) _pumping = false; }
    }
}

public sealed class LoopbackRelay : IPlenumRelay
{
    private readonly LoopbackBroker _broker;
    private readonly HashSet<string> _filters = new();
    private RelayWill? _will;
    private bool _everConnected;

    internal LoopbackRelay(LoopbackBroker b, string id) { _broker = b; ClientId = id; }

    public string ClientId { get; }
    public RelayState State { get; private set; }
    internal bool IsConnected => State == RelayState.Connected;
    public event Action<RelayMessage>? OnMessage;
    public event Action<string>? OnDisconnect;
    public event Action? OnReconnected;

    internal bool Matches(string topic) { lock (_filters) foreach (var f in _filters) if (PlenumTopics.Matches(f, topic)) return true; return false; }
    internal void Deliver(RelayMessage m) => OnMessage?.Invoke(m);
    internal RelayWill? TakeWillForDrop() => State == RelayState.Connected ? _will : null;
    internal void MarkLost() => State = RelayState.Disconnected;
    internal void RaiseDisconnect(string reason) => OnDisconnect?.Invoke(reason);

    public Task ConnectAsync(RelayWill? will = null, CancellationToken ct = default)
    {
        if (!_broker.Reachable) throw new IOException("loopback relay is unreachable");
        if (will is not null) { PlenumTopics.RequireNamespace(will.Topic); _will = will; }
        State = RelayState.Connected;
        bool again = _everConnected;
        _everConnected = true;
        string[] fs; lock (_filters) fs = _filters.ToArray();
        foreach (var f in fs) _broker.Subscribed(this, f);
        if (again) OnReconnected?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>What MqttWsRelay does by itself after a loss: connect again with the same will, subscriptions restored.</summary>
    public Task ReconnectAsync() => ConnectAsync(null);

    public Task SubscribeAsync(string filter)
    {
        PlenumTopics.RequireNamespace(filter);
        if (!PlenumTopics.ValidFilter(filter)) throw new ArgumentException("bad filter " + filter);
        bool isNew; lock (_filters) isNew = _filters.Add(filter);
        if (IsConnected) _broker.Subscribed(this, filter);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string filter) { lock (_filters) _filters.Remove(filter); return Task.CompletedTask; }

    public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain = false, int qos = 0)
    {
        if (!IsConnected) return Task.CompletedTask;     // a lost relay drops what it is asked to send (the real relay keeps Qos 1)
        _broker.Publish(this, topic, payload.ToArray(), retain, qos);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() { State = RelayState.Disconnected; _will = null; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { State = RelayState.Disconnected; _will = null; return ValueTask.CompletedTask; }
}

public sealed class LoopbackRelayFactory : IPlenumRelayFactory
{
    public LoopbackBroker Broker { get; }
    public LoopbackRelayFactory(LoopbackBroker b) { Broker = b; }
    public List<LoopbackRelay> Made { get; } = new();
    public IPlenumRelay Create(string clientId) { var r = Broker.NewClient(clientId); Made.Add(r); return r; }
}
