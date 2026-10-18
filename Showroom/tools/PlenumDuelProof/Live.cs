using ShowroomPlenum;
using static H;

/// <summary>ONE live check against the public relay, in a throwaway namespace plenum/v1/test-{random}/ (never lobby/ or room/ topics).
/// About a dozen messages. Run it once; if it cannot connect it prints the exact error and stops (no loop).</summary>
static class Live
{
    public static async Task<int> Run(string url)
    {
        string rnd = Guid.NewGuid().ToString("N")[..8];
        string ns = $"plenum/v1/test-{rnd}/";
        Console.WriteLine($"live relay check: {url}  namespace {ns}");
        int published = 0;
        var gotA = new List<RelayMessage>();
        MqttWsRelay Make(string id, bool exposeTransport, out Func<WebSocketMqttTransport?> tr)
        {
            WebSocketMqttTransport? last = null;
            tr = () => last;
            return new MqttWsRelay(id, new MqttRelayOptions
            {
                Url = url, AutoReconnect = false, KeepAliveSeconds = 30,
                TransportFactory = () => last = new WebSocketMqttTransport(new Uri(url)),
            });
        }

        var a = Make("pl-a-" + rnd, false, out _);
        a.OnMessage += m => { lock (gotA) gotA.Add(m); };
        var b = Make("pl-b-" + rnd, true, out var btr);
        int bLost = 0; b.OnDisconnect += _ => bLost++;
        try
        {
            await a.ConnectAsync();
            await b.ConnectAsync(new RelayWill(ns + "will", Ascii("B-dropped"), false, 1));
        }
        catch (Exception e)
        {
            Console.WriteLine($"CANNOT CONNECT: {e.GetType().FullName}: {e.Message}" + (e.InnerException is null ? "" : $" / inner {e.InnerException.GetType().FullName}: {e.InnerException.Message}"));
            return 2;
        }
        Check(a.State == RelayState.Connected && b.State == RelayState.Connected, "two clients connected over wss with the mqtt subprotocol");

        await a.SubscribeAsync(ns + "+");
        Check(true, "subscribe acknowledged (SUBACK)");

        await b.PublishAsync(ns + "ret", Ascii("R1"), true, 1); published++;
        Check(await Until(() => { lock (gotA) return gotA.Any(m => m.Topic == ns + "ret" && m.Text == "R1" && !m.Retained); }, 6000), "a live Qos 1 retained publish reaches the subscriber (retain flag clear on a live delivery)");
        await b.PublishAsync(ns + "live0", Ascii("L0"), false, 0); published++;
        await b.PublishAsync(ns + "live1", Ascii("L1"), false, 1); published++;
        Check(await Until(() => { lock (gotA) return gotA.Any(m => m.Topic == ns + "live0") && gotA.Any(m => m.Topic == ns + "live1"); }, 6000), "Qos 0 and Qos 1 live messages arrive");
        Check(await Until(() => b.Unacked == 0, 6000), "the broker acknowledged the Qos 1 publishes (PUBACK, none left unacked)");

        // a later subscriber gets the retained message, flagged
        var c = Make("pl-c-" + rnd, false, out _);
        var gotC = new List<RelayMessage>(); c.OnMessage += m => { lock (gotC) gotC.Add(m); };
        await c.ConnectAsync();
        await c.SubscribeAsync(ns + "ret");
        Check(await Until(() => { lock (gotC) return gotC.Any(m => m.Text == "R1" && m.Retained); }, 6000), "a late subscriber receives the retained message with the retain flag set");

        // last will: drop B's socket without a DISCONNECT
        await btr()!.DisposeAsync();
        Check(await Until(() => { lock (gotA) return gotA.Any(m => m.Topic == ns + "will" && m.Text == "B-dropped"); }, 15000), "an abrupt drop makes the broker publish the will");
        Check(await Until(() => bLost == 1, 5000), "and the dropped client noticed");

        // cleanup: clear the retained message, prove it is gone
        await a.PublishAsync(ns + "ret", ReadOnlyMemory<byte>.Empty, true, 1); published++;
        Check(await Until(() => { lock (gotA) return gotA.Any(m => m.Topic == ns + "ret" && m.Payload.Length == 0); }, 6000), "the clearing publish is delivered");
        var d = Make("pl-d-" + rnd, false, out _);
        var gotD = new List<RelayMessage>(); d.OnMessage += m => { lock (gotD) gotD.Add(m); };
        await d.ConnectAsync();
        await d.SubscribeAsync(ns + "+");
        await Task.Delay(1500);
        int leftover; lock (gotD) leftover = gotD.Count;
        Check(leftover == 0, "after cleanup a new subscriber finds nothing retained under the namespace  [" + leftover + " found]");

        foreach (var r in new[] { a, c, d }) await r.DisposeAsync();
        await b.DisposeAsync();
        int recvA; lock (gotA) recvA = gotA.Count;
        Console.WriteLine($"live relay messages: {published} published by this check (+1 will), {recvA} received by the main subscriber, {leftover} retained left behind");
        return Failures == 0 ? 0 : 1;
    }
}
