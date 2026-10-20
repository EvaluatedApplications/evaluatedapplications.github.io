using System.Text;
using ShowroomDoctrine.Duel;
using ShowroomPlenum;
using static H;

/// <summary>ONE live duel against the public relay: two in-process seats over two real MQTT connections, the real game, a unique room code, plenum/v1/doctrine/ only.
/// Round 1 is left to a 10 s timer on seat B (a live auto-seal); rounds 2 and 3 are sealed by hand. Cleans up, then proves the room left nothing retained.</summary>
static class Live
{
    public static async Task<int> Run(string url)
    {
        PlenumRelayConfig.Configure(url);
        string code = "ZT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = DuelProtocol.Root + code + "/";
        Console.WriteLine($"live relay duel: {url}  room {root}");
        var seen = new List<(string Topic, int Len, bool Retained)>();
        var spy = new MqttWsRelay("dd-spy-" + code, new MqttRelayOptions { Url = url, AutoReconnect = false });
        spy.OnMessage += m => { lock (seen) seen.Add((m.Topic, m.Payload.Length, m.Retained)); };
        try { await spy.ConnectAsync(); await spy.SubscribeAsync(root + "#"); }
        catch (Exception e) { Console.WriteLine($"CANNOT CONNECT: {e.GetType().FullName}: {e.Message}"); return 2; }

        DuelOptions Opts() => new() { GameFactory = ShowroomDoctrine.Duel.DuelGames.Real, Relays = new MqttWsRelayFactory(), CodeSource = () => code, CreateProbeMs = 700, JoinWaitMs = 8000, GraceMs = 3000 };
        var a = new DoctrineDuel(Opts()); var b = new DoctrineDuel(Opts());
        using var cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await a.CreateRoomAsync("Ann", 3, 10);
            Check(await b.JoinAsync(code, "Bo") is null, "the second seat joined over the real relay");
            _ = a.RunTickerAsync(cts.Token); _ = b.RunTickerAsync(cts.Token);
            await b.SetReadyAsync(); await a.SetReadyAsync();
            Check(await Until(() => a.Phase == DuelPhase.Sealing && b.Phase == DuelPhase.Sealing, 10000), "both seats reach round 1");
            async Task Seal(DoctrineDuel d) => await d.SealAsync(await DuelAutoPickers.Starter(d.LocalSeat!, d.Round, false, default));
            for (int r = 0; r < 3; r++)
            {
                await Seal(a);
                if (r > 0) await Seal(b);                        // round 1: B is left to the timer
                Check(await Until(() => a.Phase == DuelPhase.Result && b.Phase == DuelPhase.Result, 40000), $"round {r + 1} closed on both seats ({sw.Elapsed.TotalSeconds:F1} s)");
                if (r == 0) Check(b.Outcome?.YouAutoSealed == true && a.Outcome?.ThemAutoSealed == true, "the live timer sealed B's round 1");
                await a.ContinueAsync(); await b.ContinueAsync();
                if (r < 2) Check(await Until(() => a.Phase == DuelPhase.Sealing && b.Phase == DuelPhase.Sealing, 10000), $"round {r + 2} opened");
            }
            Check(await Until(() => a.Phase == DuelPhase.Ended && b.Phase == DuelPhase.Ended, 5000), "both seats show the end");
            bool same = a.History.Count == 3 && b.History.Count == 3;
            for (int i = 0; same && i < 3; i++) same &= a.History[i].Result.Digest() == b.History[i].Result.Digest() && a.History[i].Check.Ok && b.History[i].Check.Ok;
            Check(same, "identical recordings and passing checks on both seats");
            Check(a.End?.Score == b.End?.Score && a.Errors == 0 && b.Errors == 0, $"same final score ({a.End?.Score.FieldsA}-{a.End?.Score.FieldsB}), no faults");
            Console.WriteLine($"  result sizes: last {a.LastPackedChars} base64 chars in {a.LastChunks} piece(s); total {sw.Elapsed.TotalSeconds:F1} s");
        }
        finally
        {
            cts.Cancel();
            await a.LeaveAsync(); await b.LeaveAsync();
        }
        await Task.Delay(1500);
        // the retained leftovers (if any) are cleared by the spy so the shared relay keeps nothing of this run
        List<(string Topic, int Len, bool Retained)> snap; lock (seen) snap = seen.ToList();
        var topics = snap.Select(s => s.Topic).Distinct().ToList();
        Check(topics.All(t => t.StartsWith(root)), "every topic seen was under " + root);
        var late = new MqttWsRelay("dd-late-" + code, new MqttRelayOptions { Url = url, AutoReconnect = false });
        var left = new List<string>(); late.OnMessage += m => { if (m.Retained) lock (left) left.Add(m.Topic + "=" + m.Text); };
        await late.ConnectAsync(); await late.SubscribeAsync(root + "#"); await Task.Delay(1500);
        int leftover; lock (left) leftover = left.Count;
        Check(leftover == 0, $"after both seats left, a new subscriber finds nothing retained [{leftover} found: {string.Join(", ", left.Select(x => x[root.Length..]))}]");
        string[] stray; lock (left) stray = left.ToArray();
        foreach (var t in stray) await late.PublishAsync(t[..t.IndexOf('=')],ReadOnlyMemory<byte>.Empty, true, 1);
        Console.WriteLine($"  messages seen by the spy: {snap.Count} on {topics.Count} topics; largest payload {snap.Max(s => s.Len)} bytes");
        await spy.DisposeAsync(); await late.DisposeAsync();
        return Failures == 0 ? 0 : 1;
    }
}
