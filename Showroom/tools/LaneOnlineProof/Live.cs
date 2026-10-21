using System.Diagnostics;
using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using static H;

/// <summary>A clock that runs <c>factor</c> times faster than the wall clock (so a 30 s pause is a couple of seconds in the live smoke).</summary>
sealed class ScaledClock(double factor) : IPlenumClock
{
    readonly Stopwatch _sw = Stopwatch.StartNew();
    readonly long _unix0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long NowMs => (long)(_sw.ElapsedMilliseconds * factor);
    public long UnixMs => _unix0 + NowMs;
}

/// <summary>
/// ONE short online match against the public relay: two in-process players over two real MQTT connections, the real battle, a unique room code under plenum/v1/doctrine-lane/ only.
/// The clock runs 15 times faster than the wall clock (every timer of the match is scaled with it), the match is played to its third pause, and the run cleans up after itself and
/// then proves a new subscriber finds nothing of the room left on the relay. Never part of the default run.
/// </summary>
static class Live
{
    public static async Task<int> Run(string url)
    {
        PlenumRelayConfig.Configure(url);
        string code = "ZT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = LaneOnlineProtocol.RoomRoot + code + "/";
        const double F = 15;
        Console.WriteLine($"live online lane match: {url}  room {root}  (clock x{F})");
        var seen = new List<(string Topic, int Len, bool Retained)>();
        var spy = new MqttWsRelay("ll-spy-" + code, new MqttRelayOptions { Url = url, AutoReconnect = false });
        spy.OnMessage += m => { lock (seen) seen.Add((m.Topic, m.Payload.Length, m.Retained)); };
        try { await spy.ConnectAsync(); await spy.SubscribeAsync(root + "#"); await spy.SubscribeAsync(LaneTopics.Lobby(code)); }
        catch (Exception e) { Console.WriteLine($"CANNOT CONNECT: {e.GetType().FullName}: {e.Message}"); return 2; }

        var clock = new ScaledClock(F);
        LaneOnlineOptions Opts() => new()
        {
            Relays = new MqttWsRelayFactory(), Clock = clock, Tracer = () => Rig.Ctx(), Setup = Rig.Setup, CodeSource = () => code, CreateProbeMs = 900, JoinWaitMs = 8000, SeatWaitMs = 12000,
            GraceMs = (int)(6000 * F), ArrivalSlackMs = (int)(15_000 * F), RevealWaitMs = (int)(20_000 * F), RejoinGraceMs = (int)(45_000 * F), ResyncMs = (int)(3000 * F), HeartbeatMs = (int)(15_000 * F),
            StaleMs = (int)(60_000 * F), StartDelayMs = (int)(3000 * F), RunDtCap = 0.25 * F, TicksPerAdvance = 80, SeedSource = () => 7,
        };
        var a = new LaneOnline(Opts()); var b = new LaneOnline(Opts());
        var lobby = new LaneLobby(new LaneLobbyOptions { Relays = new MqttWsRelayFactory(), Clock = clock, TtlMs = (int)(75_000 * F) });
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var plans = 0;
        async Task Autopilot(LaneOnline s)
        {
            if (s.Phase != OnlinePhase.Playing || !s.PauseOpen || s.MyCommitted || s.Match is not { } m || m.Human is not { } h) return;
            int n = m.Phase, side = (int)m.LocalSide, lanes = m.Battle.Setup.Lanes;
            for (int k = 0; k < 8; k++) h.Buy((n + k + side) % lanes, (n + side + k) % 8, out _);
            if (await s.ReadyAsync()) plans++;
        }
        // one loop for each player, as each page has its own ticker (a player that is busy joining must not hold the other one up)
        Task Loop(LaneOnline s) => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { await s.AdvanceAsync(); await Autopilot(s); } catch (OperationCanceledException) { }
                await Task.Delay(30);
            }
        });
        var ticker = Task.WhenAll(Loop(a), Loop(b));
        try
        {
            await lobby.StartAsync();
            await a.CreateRoomAsync("Lord Live");
            Check(await Until(() => lobby.Rooms.Any(r => r.Code == code && r.Host == "Lord Live"), 5000), "the new room shows in the open-rooms list over the real relay");
            var joinErr = await b.JoinAsync(code.ToLowerInvariant(), "Lord Smoke");
            Check(joinErr is null, "the second player joined with the code over the real relay: " + joinErr);
            Check(await Until(() => lobby.Rooms.All(r => r.Code != code), 5000), "and the room left the list once seated");
            Check(await Until(() => a.Phase == OnlinePhase.Playing && b.Phase == OnlinePhase.Playing, 40000), $"both reach the battle ({sw.Elapsed.TotalSeconds:F1} s)");
            Check(await Until(() => a.Phase == OnlinePhase.Ended || b.Phase == OnlinePhase.Ended || ((a.Match?.Phase ?? 0) >= 3 && (b.Match?.Phase ?? 0) >= 3), 120000), $"both reach the fourth pause ({sw.Elapsed.TotalSeconds:F1} s)");
            Check(a.End is null && b.End is null && a.Errors == 0 && b.Errors == 0, $"no end, no faults ({a.End?.Kind} {a.End?.Text})");
            int n = Math.Min(a.Match?.PauseHashes.Count ?? 0, b.Match?.PauseHashes.Count ?? 0);
            Check(n >= 4 && a.Match!.PauseHashes.Take(n).SequenceEqual(b.Match!.PauseHashes.Take(n)), $"identical state hashes at the first {n} pauses");
            if (a.Match is not null) Console.WriteLine($"  hashes: {string.Join(" ", a.Match.PauseHashes.Take(n).Select(h => h.Tick + ":" + LaneMatch.ShortHash(h.Hash)))}  ({plans} commitments, {sw.Elapsed.TotalSeconds:F1} s)");
        }
        finally
        {
            cts.Cancel(); try { await ticker; } catch { }
            await b.LeaveAsync(); await a.LeaveAsync(); await lobby.DisposeAsync();
        }
        await Task.Delay(1500);
        List<(string Topic, int Len, bool Retained)> snap; lock (seen) snap = seen.ToList();
        var topics = snap.Select(s => s.Topic).Distinct().ToList();
        Check(topics.Count > 8 && topics.All(t => t.StartsWith(root) || t == LaneTopics.Lobby(code)), $"every topic seen was under {root} or this room's lobby entry ({topics.Count} topics)");
        var late = new MqttWsRelay("ll-late-" + code, new MqttRelayOptions { Url = url, AutoReconnect = false });
        var left = new List<string>(); late.OnMessage += m => { if (m.Retained) lock (left) left.Add(m.Topic + "=" + m.Text); };
        await late.ConnectAsync(); await late.SubscribeAsync(root + "#"); await late.SubscribeAsync(LaneTopics.Lobby(code)); await Task.Delay(1500);
        string[] stray; lock (left) stray = left.ToArray();
        Check(stray.Length == 0, $"after both left, a new subscriber finds nothing retained [{stray.Length} found: {string.Join(", ", stray.Select(x => x.Length > 90 ? x[..90] : x))}]");
        foreach (var t in stray) await late.PublishAsync(t[..t.IndexOf('=')], ReadOnlyMemory<byte>.Empty, true, 1);
        Console.WriteLine($"  messages seen by the spy: {snap.Count} on {topics.Count} topics; largest payload {(snap.Count == 0 ? 0 : snap.Max(s => s.Len))} bytes");
        await spy.DisposeAsync(); await late.DisposeAsync();
        return Failures == 0 ? 0 : 1;
    }

    static async Task<bool> Until(Func<bool> cond, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(20); }
        return cond();
    }
    /// <summary>`-- sweep`: lists what is retained under plenum/v1/doctrine-lane/ and clears only the leftovers of the live smoke (codes of the form ZT plus eight hex digits; a real room's code is six characters).</summary>
    public static async Task<int> Sweep(string url)
    {
        var relay = new MqttWsRelay("ll-sweep-" + Guid.NewGuid().ToString("N")[..6], new MqttRelayOptions { Url = url, AutoReconnect = false });
        var held = new List<string>();
        relay.OnMessage += m => { if (m.Retained && m.Payload.Length > 0) lock (held) held.Add(m.Topic); };
        await relay.ConnectAsync(); await relay.SubscribeAsync(LaneOnlineProtocol.Root + "#"); await Task.Delay(2500);
        string[] all; lock (held) all = held.Distinct().ToArray();
        var mine = all.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"/(room|lobby)/ZT[0-9A-F]{8}(/|$)")).ToArray();
        Console.WriteLine($"retained under {LaneOnlineProtocol.Root}: {all.Length} topics, {mine.Length} of them leftovers of the live smoke");
        foreach (var t in all.Except(mine).Take(10)) Console.WriteLine("  other: " + t);
        foreach (var t in mine) await relay.PublishAsync(t, ReadOnlyMemory<byte>.Empty, true, 1);
        await Task.Delay(500); await relay.DisconnectAsync(); await relay.DisposeAsync();
        return 0;
    }
}
