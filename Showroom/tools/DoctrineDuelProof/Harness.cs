using Doctrine;
using ShowroomDoctrine.Duel;
using ShowroomPlenum;

static class H
{
    public static int Failures, Checks, BatchFails;
    public static string FirstRed = "";
    public static bool Quiet;

    public static void Check(bool ok, string what)
    {
        Checks++;
        if (!ok)
        {
            BatchFails++;
            if (BatchFails == 1) FirstRed = what;
            if (!Quiet) { Failures++; Console.WriteLine("  FAIL: " + what); }
        }
    }

    public static void Section(string s) { if (!Quiet) Console.WriteLine("== " + s); }
    public static void Note(string s) { if (!Quiet) Console.WriteLine("  " + s); }

    public static async Task<bool> Until(Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }
}

sealed class TestClock : IPlenumClock
{
    public long NowMs { get; set; } = 5_000_000;
    public long UnixMs { get; set; } = 1_760_000_000_000;
    public void Advance(long ms) { NowMs += ms; UnixMs += ms; }
}

/// <summary>Wraps a relay so a proof can repeat or reorder what a seat sends. Everything else passes through.</summary>
sealed class WrapRelay : IPlenumRelay
{
    public readonly LoopbackRelay Loop;
    public bool Dup, HoldCommit;
    readonly List<(string Topic, byte[] Payload, bool Retain, int Qos)> _held = [];
    public WrapRelay(LoopbackRelay inner)
    {
        Loop = inner;
        inner.OnMessage += m => OnMessage?.Invoke(m);
        inner.OnDisconnect += r => OnDisconnect?.Invoke(r);
        inner.OnReconnected += () => OnReconnected?.Invoke();
    }
    public string ClientId => Loop.ClientId;
    public RelayState State => Loop.State;
    public event Action<RelayMessage>? OnMessage;
    public event Action<string>? OnDisconnect;
    public event Action? OnReconnected;
    public Task ConnectAsync(RelayWill? will = null, CancellationToken ct = default) => Loop.ConnectAsync(will, ct);
    public Task SubscribeAsync(string filter) => Loop.SubscribeAsync(filter);
    public Task UnsubscribeAsync(string filter) => Loop.UnsubscribeAsync(filter);
    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain = false, int qos = 0)
    {
        if (HoldCommit && topic.Contains("/commit/")) { _held.Add((topic, payload.ToArray(), retain, qos)); return; }
        await Loop.PublishAsync(topic, payload, retain, qos);
        if (Dup) await Loop.PublishAsync(topic, payload, retain, qos);
        if (HoldCommit && topic.Contains("/reveal/"))
        {
            foreach (var h in _held.ToArray()) await Loop.PublishAsync(h.Topic, h.Payload, h.Retain, h.Qos);
            _held.Clear();
        }
    }
    public Task DisconnectAsync() => Loop.DisconnectAsync();
    public ValueTask DisposeAsync() => Loop.DisposeAsync();
}

sealed class WrapFactory(LoopbackBroker broker, Action<WrapRelay>? tune = null) : IPlenumRelayFactory
{
    public readonly List<WrapRelay> Made = [];
    public IPlenumRelay Create(string clientId) { var r = new WrapRelay(broker.NewClient(clientId)); tune?.Invoke(r); Made.Add(r); return r; }
    public WrapRelay Current => Made[^1];
}

/// <summary>A passive observer on the in-memory broker: sees every message every seat publishes, in order, with the clock reading at the moment it was sent.</summary>
sealed class Monitor
{
    public readonly List<(int Index, string Topic, byte[] Payload, long Unix)> Log = [];
    public int Clears;
    public Monitor(LoopbackBroker b, TestClock clock)
    {
        var relay = b.NewClient("monitor");
        relay.OnMessage += m =>
        {
            if (m.Payload.Length == 0) { Clears++; return; }
            Log.Add((Log.Count, m.Topic, m.Payload, clock.UnixMs));
        };
        relay.ConnectAsync().GetAwaiter().GetResult();
        relay.SubscribeAsync("plenum/v1/#").GetAwaiter().GetResult();
    }

    public IEnumerable<string> TopicsOutside(string code) => Log.Select(l => l.Topic).Where(t => !t.StartsWith(DuelProtocol.Root + code + "/", StringComparison.Ordinal)).Distinct();

    /// <summary>For every round: the first reveal message comes after BOTH commitments (by position on the wire).</summary>
    public (bool Ok, string Why) RevealsAfterCommits(string code)
    {
        string root = DuelProtocol.Root + code + "/";
        var firstCommit = new Dictionary<(string, string), int>(); var firstReveal = new Dictionary<string, int>();
        foreach (var l in Log)
        {
            if (!l.Topic.StartsWith(root, StringComparison.Ordinal)) continue;
            var parts = l.Topic[root.Length..].Split('/');
            if (parts.Length == 3 && parts[0] == "commit") firstCommit.TryAdd((parts[1], parts[2]), l.Index);
            if (parts.Length == 3 && parts[0] == "reveal") { firstReveal.TryAdd(parts[1], l.Index); }
        }
        foreach (var (r, at) in firstReveal)
        {
            if (!firstCommit.TryGetValue((r, "A"), out int ca) || !firstCommit.TryGetValue((r, "B"), out int cb)) return (false, $"round {r}: a reveal with a commitment missing");
            if (at < Math.Max(ca, cb)) return (false, $"round {r}: a reveal (message {at}) went out before both commitments (messages {ca} and {cb})");
        }
        return (true, "");
    }

    public int Count(string root, string kind) => Log.Count(l => l.Topic.StartsWith(root + kind + "/", StringComparison.Ordinal) || l.Topic == root + kind);
}
