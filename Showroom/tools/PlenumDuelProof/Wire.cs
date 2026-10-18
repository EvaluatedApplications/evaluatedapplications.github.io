using System.Threading.Channels;
using ShowroomPlenum;
using static H;

/// <summary>A transport the proof drives by hand: it records every byte string the relay writes and returns whatever the proof injects.</summary>
sealed class ScriptedTransport : IMqttTransport
{
    public readonly List<byte[]> Sent = new();
    private readonly Channel<byte[]> _in = Channel.CreateUnbounded<byte[]>();
    private byte[]? _pending;
    private int _pos;
    public bool Disposed;

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) { lock (Sent) Sent.Add(data.ToArray()); return Task.CompletedTask; }
    public int SentCount { get { lock (Sent) return Sent.Count; } }
    public byte[] SentAt(int i) { lock (Sent) return Sent[i]; }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_pending is null)
        {
            try { _pending = await _in.Reader.ReadAsync(ct); _pos = 0; }
            catch (ChannelClosedException) { return 0; }
        }
        int n = Math.Min(buffer.Length, _pending.Length - _pos);
        _pending.AsMemory(_pos, n).CopyTo(buffer);
        _pos += n;
        if (_pos == _pending.Length) _pending = null;
        return n;
    }

    public void Inject(string hex) => _in.Writer.TryWrite(Bytes(hex));
    public void InjectRaw(byte[] b) => _in.Writer.TryWrite(b);
    public void Close() => _in.Writer.TryComplete();
    public ValueTask DisposeAsync() { Disposed = true; _in.Writer.TryComplete(); return ValueTask.CompletedTask; }
}

static class Wire
{
    static string Enc(MqttPacket p) => Hex(MqttCodec.Encode(p));

    public static async Task Run()
    {
        Section("MQTT encodings against hand-computed bytes");
        const string FilterHex = "70 6C 65 6E 75 6D 2F 76 31 2F 6C 6F 62 62 79 2F 2B";   // plenum/v1/lobby/+
        Check(Enc(new MqttConnect("ab", 30, true)) == "10 0E 00 04 4D 51 54 54 04 02 00 1E 00 02 61 62", "CONNECT: clean session, keepalive 30, client id ab");
        Check(Enc(new MqttConnect("ab", 30, true, new RelayWill("t/w", Ascii("x"), true, 1))) == "10 16 00 04 4D 51 54 54 04 2E 00 1E 00 02 61 62 00 03 74 2F 77 00 01 78", "CONNECT with a retained Qos 1 will (flags 2E)");
        Check(Enc(new MqttConnect("ab", 60, false, null, "u", "p")) == "10 14 00 04 4D 51 54 54 04 C0 00 3C 00 02 61 62 00 01 75 00 01 70", "CONNECT with user and password (flags C0, no clean session)");
        Check(Enc(new MqttPublish("a/b", Ascii("hi"), 0, false, false, 0)) == "30 07 00 03 61 2F 62 68 69", "PUBLISH Qos 0");
        Check(Enc(new MqttPublish("a", Ascii("z"), 1, true, true, 10)) == "3B 06 00 01 61 00 0A 7A", "PUBLISH Qos 1 + retain + DUP, packet id 10");
        Check(Enc(new MqttPublish("a", Ascii("z"), 1, false, false, 0x1234)) == "32 06 00 01 61 12 34 7A", "PUBLISH Qos 1, packet id 0x1234");
        Check(Enc(new MqttPublish("a", Array.Empty<byte>(), 1, true, false, 7)) == "33 05 00 01 61 00 07", "PUBLISH retained with an empty payload (clears a retained message)");
        Check(Enc(new MqttSubscribe(1, new[] { ("plenum/v1/lobby/+", 1) })) == "82 16 00 01 00 11 " + FilterHex + " 01", "SUBSCRIBE id 1, one filter at Qos 1");
        Check(Enc(new MqttSubscribe(2, new[] { ("a", 0), ("b/+", 1) })) == "82 0C 00 02 00 01 61 00 00 03 62 2F 2B 01", "SUBSCRIBE two filters");
        Check(Enc(new MqttUnsubscribe(2, new[] { "a" })) == "A2 05 00 02 00 01 61", "UNSUBSCRIBE");
        Check(Enc(new MqttPubAck(0x1234)) == "40 02 12 34", "PUBACK");
        Check(Enc(new MqttPingReq()) == "C0 00", "PINGREQ");
        Check(Enc(new MqttPingResp()) == "D0 00", "PINGRESP");
        Check(Enc(new MqttDisconnect()) == "E0 00", "DISCONNECT");
        Check(Enc(new MqttConnAck(false, 0)) == "20 02 00 00", "CONNACK accepted");
        Check(Enc(new MqttSubAck(1, new byte[] { 1 })) == "90 03 00 01 01", "SUBACK");
        Check(Enc(new MqttUnsubAck(2)) == "B0 02 00 02", "UNSUBACK");

        Section("remaining length at the edges");
        foreach (var (v, hex) in new[] { (0, "00"), (1, "01"), (127, "7F"), (128, "80 01"), (16383, "FF 7F"), (16384, "80 80 01"), (2097151, "FF FF 7F"), (2097152, "80 80 80 01"), (268435455, "FF FF FF 7F") })
            Check(Hex(MqttCodec.EncodeRemainingLength(v)) == hex, $"remaining length {v} -> {hex}");
        bool threw = false; try { MqttCodec.EncodeRemainingLength(268435456); } catch (MqttProtocolException) { threw = true; }
        Check(threw, "remaining length above 268435455 is refused");
        foreach (int rem in new[] { 127, 128, 16383, 16384 })
        {
            var pub = new MqttPublish("a", new byte[rem - 3], 0, false, false, 0);      // 2 (topic length) + 1 (topic) + payload
            var bytes = MqttCodec.Encode(pub);
            int lenBytes = rem < 128 ? 1 : rem < 16384 ? 2 : 3;
            Check(bytes.Length == 1 + lenBytes + rem && Hex(bytes[1..(1 + lenBytes)]) == Hex(MqttCodec.EncodeRemainingLength(rem)), $"PUBLISH with remaining length {rem} has a {lenBytes}-byte length");
            var dec = new MqttStreamDecoder(); var outp = new List<MqttPacket>();
            dec.Feed(bytes, outp);
            Check(outp.Count == 1 && outp[0] is MqttPublish p && p.Payload.Length == rem - 3 && p.Topic == "a" && dec.Buffered == 0, $"and it decodes back ({rem})");
        }

        Section("decoding: split and coalesced frames");
        {
            var a = MqttCodec.Encode(new MqttPublish("plenum/v1/x", Ascii("one"), 1, false, false, 5));
            var b = MqttCodec.Encode(new MqttPingResp());
            var c = MqttCodec.Encode(new MqttPublish("plenum/v1/y", new byte[300], 0, true, false, 0));
            var all = a.Concat(b).Concat(c).ToArray();
            var dec = new MqttStreamDecoder(); var o = new List<MqttPacket>();
            dec.Feed(all, o);
            Check(o.Count == 3 && o[0] is MqttPublish { Qos: 1, PacketId: 5 } && o[1] is MqttPingResp && o[2] is MqttPublish { Retain: true } pc && pc.Payload.Length == 300, "three packets in one chunk");
            bool allSplits = true;
            for (int cut = 1; cut < all.Length; cut++)
            {
                var d2 = new MqttStreamDecoder(); var o2 = new List<MqttPacket>();
                d2.Feed(all.AsSpan(0, cut), o2); d2.Feed(all.AsSpan(cut), o2);
                if (o2.Count != 3 || d2.Buffered != 0) { allSplits = false; break; }
            }
            Check(allSplits, "the same three packets split at EVERY byte boundary (including inside the length bytes)");
            var d3 = new MqttStreamDecoder(); var o3 = new List<MqttPacket>();
            foreach (var by in all) d3.Feed(new[] { by }, o3);
            Check(o3.Count == 3, "fed one byte at a time");
            bool bad = false; try { new MqttStreamDecoder().Feed(Bytes("30 FF FF FF FF 01"), new List<MqttPacket>()); } catch (MqttProtocolException) { bad = true; }
            Check(bad, "a 5-byte remaining length is a protocol error");
            bool q3 = false; try { new MqttStreamDecoder().Feed(Bytes("36 05 00 01 61 00 01"), new List<MqttPacket>()); } catch (MqttProtocolException) { q3 = true; }
            Check(q3, "Qos 3 is a protocol error");
            // round trips
            var rt = new List<MqttPacket>
            {
                new MqttConnect("cid", 45, true, new RelayWill("plenum/v1/w", Ascii("bye"), true, 1)),
                new MqttSubscribe(9, new[] { ("plenum/v1/+/x", 1), ("plenum/v1/y", 0) }),
                new MqttSubAck(9, new byte[] { 1, 0 }), new MqttUnsubscribe(3, new[] { "p/q", "r" }),
            };
            bool ok = true;
            foreach (var p in rt)
            {
                var d4 = new MqttStreamDecoder(); var o4 = new List<MqttPacket>();
                d4.Feed(MqttCodec.Encode(p), o4);
                ok &= o4.Count == 1 && Hex(MqttCodec.Encode(o4[0])) == Hex(MqttCodec.Encode(p));
            }
            Check(ok, "CONNECT, SUBSCRIBE, SUBACK, UNSUBSCRIBE decode and re-encode to the same bytes");
        }

        Section("topic filters");
        (string f, string t, bool want)[] cases =
        {
            ("a/b/c", "a/b/c", true), ("a/+/c", "a/b/c", true), ("a/+", "a/b/c", false), ("a/#", "a/b/c", true), ("a/#", "a", true), ("#", "a/b", true),
            ("+/b", "x/b", true), ("+/b", "$SYS/b", false), ("#", "$SYS/x", false), ("$SYS/#", "$SYS/x", true), ("a/b", "a/b/", false), ("a//b", "a//b", true),
            ("plenum/v1/lobby/+", "plenum/v1/lobby/K7Q2MX", true), ("plenum/v1/lobby/+", "plenum/v1/lobby/K7Q2MX/x", false), ("plenum/v1/lobby/+", "pzc/lobby/K7Q2MX", false),
            ("plenum/v1/room/K7Q2MX/ev", "plenum/v1/room/K7Q2MX/ev", true), ("plenum/v1/room/K7Q2MX/ev", "plenum/v1/room/ABCDEF/ev", false),
        };
        foreach (var (f, t, want) in cases) Check(PlenumTopics.Matches(f, t) == want, $"filter {f} vs topic {t} = {want}");
        Check(PlenumTopics.ValidFilter("a/+/b") && PlenumTopics.ValidFilter("#") && PlenumTopics.ValidFilter("a/#") && !PlenumTopics.ValidFilter("a/#/b") && !PlenumTopics.ValidFilter("a#") && !PlenumTopics.ValidFilter("a+/b") && !PlenumTopics.ValidFilter(""), "filter validity rules");

        Section("the relay client against a scripted transport");
        await RelayClient();
    }

    static async Task RelayClient()
    {
        const string PubHex = "70 6C 65 6E 75 6D 2F 76 31 2F 71";     // plenum/v1/q
        var queue = new Queue<ScriptedTransport>();
        var t1 = new ScriptedTransport(); var t2 = new ScriptedTransport();
        queue.Enqueue(t1); queue.Enqueue(t2);
        var relay = new MqttWsRelay("ab", new MqttRelayOptions { KeepAliveSeconds = 30, BackoffMs = new[] { 20 }, TransportFactory = () => queue.Dequeue() });
        var messages = new List<RelayMessage>();
        int disconnects = 0, reconnects = 0;
        relay.OnMessage += messages.Add; relay.OnDisconnect += _ => disconnects++; relay.OnReconnected += () => reconnects++;

        var conn = relay.ConnectAsync(new RelayWill("plenum/v1/w", Ascii("x"), true, 1));
        Check(await Until(() => t1.SentCount >= 1, 2000), "connect writes a packet");
        Check(Hex(t1.SentAt(0)) == "10 1E 00 04 4D 51 54 54 04 2E 00 1E 00 02 61 62 00 0B 70 6C 65 6E 75 6D 2F 76 31 2F 77 00 01 78", "first bytes on the wire are the CONNECT with the will");
        Check(!conn.IsCompleted, "connect waits for the CONNACK");
        t1.Inject("20 02 00 00");
        await conn;
        Check(relay.State == RelayState.Connected, "CONNACK 0 = connected");

        var sub = relay.SubscribeAsync("plenum/v1/lobby/+");
        Check(await Until(() => t1.SentCount >= 2, 2000) && Hex(t1.SentAt(1)) == "82 16 00 01 00 11 70 6C 65 6E 75 6D 2F 76 31 2F 6C 6F 62 62 79 2F 2B 01", "subscribe writes SUBSCRIBE id 1 at Qos 1");
        Check(!sub.IsCompleted, "subscribe waits for the SUBACK");
        t1.Inject("90 03 00 01 01");
        await sub;
        Check(true, "SUBACK completes the subscribe");

        await relay.PublishAsync("plenum/v1/q", Ascii("ok"), true, 1);
        Check(Hex(t1.SentAt(2)) == "33 11 00 0B " + PubHex + " 00 02 6F 6B" && relay.Unacked == 1, "publish Qos 1 retained: bytes exact, one unacknowledged");
        await relay.PublishAsync("plenum/v1/q", Ascii("hi"), false, 0);
        Check(Hex(t1.SentAt(3)) == "30 0F 00 0B " + PubHex + " 68 69" && relay.Unacked == 1, "publish Qos 0 is not kept");

        // incoming: a coalesced chunk (PUBLISH Qos 1 + PINGRESP) split inside the first packet
        var incoming = MqttCodec.Encode(new MqttPublish("plenum/v1/lobby/ABC234", Ascii("{}"), 1, true, false, 77)).Concat(MqttCodec.Encode(new MqttPingResp())).ToArray();
        t1.InjectRaw(incoming[..5]); t1.InjectRaw(incoming[5..]);
        Check(await Until(() => messages.Count == 1, 2000), "a split frame still delivers one message");
        Check(messages[0].Topic == "plenum/v1/lobby/ABC234" && messages[0].Text == "{}" && messages[0].Retained, "topic, payload and the retained flag arrive intact");
        Check(await Until(() => t1.SentCount >= 5, 2000) && Hex(t1.SentAt(4)) == "40 02 00 4D", "an incoming Qos 1 message is answered with PUBACK (same id 77 = 4D)");

        // PUBACK for ours (id 2) clears it
        // (the unacked publish is kept below to prove the resend, so first prove PUBACK on a second one)
        await relay.PublishAsync("plenum/v1/q", Ascii("two"), false, 1);
        Check(relay.Unacked == 2, "second Qos 1 publish is kept too");
        t1.Inject("40 02 00 03");
        Check(await Until(() => relay.Unacked == 1, 2000), "PUBACK for id 3 releases it (id 2 stays)");

        // drop the link: reconnect with backoff, same will, subscriptions restored, unacked resent with DUP
        t1.Close();
        Check(await Until(() => disconnects == 1, 2000), "a closed link raises OnDisconnect once");
        Check(await Until(() => t2.SentCount >= 1, 2000) && Hex(t2.SentAt(0)) == Hex(t1.SentAt(0)), "after the backoff it connects again with the same CONNECT (same will)");
        t2.Inject("20 02 00 00");
        Check(await Until(() => t2.SentCount >= 3 && reconnects == 1, 2000), "OnReconnected is raised after the restore");
        Check(Hex(t2.SentAt(1)).StartsWith("82 16") && Hex(t2.SentAt(1)).Contains("70 6C 65 6E 75 6D 2F 76 31 2F 6C 6F 62 62 79 2F 2B"), "the subscription is sent again");
        Check(Hex(t2.SentAt(2)) == "3B 11 00 0B " + PubHex + " 00 02 6F 6B", "the unacknowledged publish is resent with DUP set and the same id (2)");
        await relay.DisconnectAsync();
        Check(await Until(() => t2.SentCount >= 4, 2000) && Hex(t2.SentAt(3)) == "E0 00" && disconnects == 1, "a deliberate disconnect sends DISCONNECT and raises no OnDisconnect");

        // namespace guard
        bool g1 = false, g2 = false, g3 = false;
        var guard = new MqttWsRelay("g", new MqttRelayOptions { TransportFactory = () => new ScriptedTransport() });
        try { await guard.PublishAsync("pzc/swarm", Ascii("x")); } catch (ArgumentException) { g1 = true; }
        try { await guard.SubscribeAsync("#"); } catch (ArgumentException) { g2 = true; }
        try { await guard.ConnectAsync(new RelayWill("pzc/x", Ascii("x"))); } catch (ArgumentException) { g3 = true; }
        Check(g1 && g2 && g3, "publish to pzc/..., subscribe to '#', and a will outside plenum/v1/ are all refused before anything is sent");

        // keepalive: a PINGREQ when idle, a PINGRESP keeps the link, silence ends it
        {
            var k = new ScriptedTransport();
            var kr = new MqttWsRelay("k", new MqttRelayOptions { KeepAliveSeconds = 1, AutoReconnect = false, TransportFactory = () => k });
            int kd = 0; string kreason = ""; kr.OnDisconnect += r => { kd++; kreason = r; };
            var kc = kr.ConnectAsync(); await Until(() => k.SentCount >= 1, 2000); k.Inject("20 02 00 00"); await kc;
            Check(await Until(() => k.SentCount >= 2, 2500) && Hex(k.SentAt(1)) == "C0 00", "idle for half the keepalive: PINGREQ is sent");
            k.Inject("D0 00");
            Check(await Until(() => k.SentCount >= 3 && Hex(k.SentAt(2)) == "C0 00", 2500) && kd == 0, "after a PINGRESP it pings again and stays connected");
            // now say nothing
            Check(await Until(() => kd == 1, 4000) && kreason == "keepalive timeout", "no PINGRESP for a whole keepalive: the link is declared lost (keepalive timeout)");
            await kr.DisposeAsync();
        }

        // an unreachable relay: ConnectAsync throws and the state stays Disconnected
        {
            var dead = new MqttWsRelay("d", new MqttRelayOptions { TransportFactory = () => new RefusingTransport() });
            bool thrown = false; try { await dead.ConnectAsync(); } catch (IOException) { thrown = true; }
            Check(thrown && dead.State == RelayState.Disconnected, "an unreachable relay throws from ConnectAsync (the lobby shows its retry message)");
        }
    }

    sealed class RefusingTransport : IMqttTransport
    {
        public Task ConnectAsync(CancellationToken ct) => throw new IOException("refused");
        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => Task.CompletedTask;
        public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct) => Task.FromResult(0);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
