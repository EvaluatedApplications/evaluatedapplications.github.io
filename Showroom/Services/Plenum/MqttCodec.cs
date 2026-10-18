using System.Text;

namespace ShowroomPlenum;

// MQTT 3.1.1 packets: the subset the Plenum relay needs, encoded and decoded byte for byte (OASIS MQTT 3.1.1, sections 2 and 3).
// Pure functions over bytes: no sockets, so tools/PlenumProof can check every encoding against hand-computed packets.

public abstract record MqttPacket;
public sealed record MqttConnect(string ClientId, ushort KeepAlive, bool CleanSession, RelayWill? Will = null, string? User = null, string? Pass = null) : MqttPacket;
public sealed record MqttConnAck(bool SessionPresent, byte ReturnCode) : MqttPacket;
public sealed record MqttPublish(string Topic, byte[] Payload, int Qos, bool Retain, bool Dup, ushort PacketId) : MqttPacket;
public sealed record MqttPubAck(ushort PacketId) : MqttPacket;
public sealed record MqttSubscribe(ushort PacketId, IReadOnlyList<(string Filter, int Qos)> Topics) : MqttPacket;
public sealed record MqttSubAck(ushort PacketId, byte[] Codes) : MqttPacket;
public sealed record MqttUnsubscribe(ushort PacketId, IReadOnlyList<string> Filters) : MqttPacket;
public sealed record MqttUnsubAck(ushort PacketId) : MqttPacket;
public sealed record MqttPingReq : MqttPacket;
public sealed record MqttPingResp : MqttPacket;
public sealed record MqttDisconnect : MqttPacket;

public sealed class MqttProtocolException : Exception
{
    public MqttProtocolException(string m) : base(m) { }
}

public static class MqttCodec
{
    public const int MaxRemainingLength = 268_435_455;

    /// <summary>The variable-length "remaining length" (section 2.2.3): 7 bits per byte, low group first, high bit = more follows.
    /// 127 -> 7F; 128 -> 80 01; 16383 -> FF 7F; 16384 -> 80 80 01; 268435455 -> FF FF FF 7F.</summary>
    public static byte[] EncodeRemainingLength(int len)
    {
        if (len < 0 || len > MaxRemainingLength) throw new MqttProtocolException("remaining length out of range: " + len);
        var b = new List<byte>(4);
        do
        {
            int d = len % 128;
            len /= 128;
            if (len > 0) d |= 0x80;
            b.Add((byte)d);
        } while (len > 0);
        return b.ToArray();
    }

    private static void PutU16(List<byte> o, int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }

    private static void PutString(List<byte> o, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        if (b.Length > 65535) throw new MqttProtocolException("string too long");
        PutU16(o, b.Length);
        o.AddRange(b);
    }

    private static void PutBinary(List<byte> o, byte[] b)
    {
        if (b.Length > 65535) throw new MqttProtocolException("binary too long");
        PutU16(o, b.Length);
        o.AddRange(b);
    }

    private static byte[] Frame(int firstByte, List<byte> body)
    {
        var rl = EncodeRemainingLength(body.Count);
        var res = new byte[1 + rl.Length + body.Count];
        res[0] = (byte)firstByte;
        rl.CopyTo(res, 1);
        body.CopyTo(res, 1 + rl.Length);
        return res;
    }

    public static byte[] Encode(MqttPacket p)
    {
        var o = new List<byte>();
        switch (p)
        {
            case MqttConnect c:
            {
                PutString(o, "MQTT");
                o.Add(4);                                         // protocol level 4 = 3.1.1
                int flags = 0;
                if (c.CleanSession) flags |= 0x02;
                if (c.Will is { } w) { flags |= 0x04 | (w.Qos << 3) | (w.Retain ? 0x20 : 0); }
                if (c.User is not null) flags |= 0x80;
                if (c.Pass is not null) flags |= 0x40;
                o.Add((byte)flags);
                PutU16(o, c.KeepAlive);
                PutString(o, c.ClientId);
                if (c.Will is { } w2) { PutString(o, w2.Topic); PutBinary(o, w2.Payload); }
                if (c.User is not null) PutString(o, c.User);
                if (c.Pass is not null) PutString(o, c.Pass);
                return Frame(0x10, o);
            }
            case MqttConnAck a:
                return new byte[] { 0x20, 0x02, (byte)(a.SessionPresent ? 1 : 0), a.ReturnCode };
            case MqttPublish pub:
            {
                if (pub.Qos is < 0 or > 2) throw new MqttProtocolException("bad qos");
                PutString(o, pub.Topic);
                if (pub.Qos > 0) PutU16(o, pub.PacketId);
                o.AddRange(pub.Payload);
                return Frame(0x30 | (pub.Dup ? 0x08 : 0) | (pub.Qos << 1) | (pub.Retain ? 0x01 : 0), o);
            }
            case MqttPubAck k:
                return new byte[] { 0x40, 0x02, (byte)(k.PacketId >> 8), (byte)k.PacketId };
            case MqttSubscribe s:
            {
                PutU16(o, s.PacketId);
                foreach (var (f, q) in s.Topics) { PutString(o, f); o.Add((byte)q); }
                return Frame(0x82, o);                            // flags 0b0010 are mandatory
            }
            case MqttSubAck sa:
            {
                PutU16(o, sa.PacketId);
                o.AddRange(sa.Codes);
                return Frame(0x90, o);
            }
            case MqttUnsubscribe u:
            {
                PutU16(o, u.PacketId);
                foreach (var f in u.Filters) PutString(o, f);
                return Frame(0xA2, o);
            }
            case MqttUnsubAck ua:
                return new byte[] { 0xB0, 0x02, (byte)(ua.PacketId >> 8), (byte)ua.PacketId };
            case MqttPingReq: return new byte[] { 0xC0, 0x00 };
            case MqttPingResp: return new byte[] { 0xD0, 0x00 };
            case MqttDisconnect: return new byte[] { 0xE0, 0x00 };
        }
        throw new MqttProtocolException("cannot encode " + p.GetType().Name);
    }

    private sealed class Rd
    {
        private readonly byte[] _b; private readonly int _end;
        public int Pos;
        public Rd(byte[] b, int off, int len) { _b = b; Pos = off; _end = off + len; }
        public int Left => _end - Pos;
        public byte Byte() { if (Pos >= _end) throw new MqttProtocolException("short packet"); return _b[Pos++]; }
        public ushort U16() { if (Pos + 2 > _end) throw new MqttProtocolException("short packet"); ushort v = (ushort)(_b[Pos] << 8 | _b[Pos + 1]); Pos += 2; return v; }
        public string Str() { int n = U16(); if (Pos + n > _end) throw new MqttProtocolException("short string"); var s = Encoding.UTF8.GetString(_b, Pos, n); Pos += n; return s; }
        public byte[] Bin() { int n = U16(); if (Pos + n > _end) throw new MqttProtocolException("short binary"); var r = new byte[n]; Array.Copy(_b, Pos, r, 0, n); Pos += n; return r; }
        public byte[] Rest() { var r = new byte[Left]; Array.Copy(_b, Pos, r, 0, r.Length); Pos = _end; return r; }
    }

    /// <summary>Decode one packet whose fixed header and remaining length have already been split off.</summary>
    internal static MqttPacket Decode(int first, byte[] src, int off, int len)
    {
        int type = first >> 4, flags = first & 0x0F;
        var r = new Rd(src, off, len);
        switch (type)
        {
            case 1:
            {
                if (r.Str() != "MQTT") throw new MqttProtocolException("not MQTT");
                if (r.Byte() != 4) throw new MqttProtocolException("not 3.1.1");
                int cf = r.Byte();
                ushort ka = r.U16();
                string id = r.Str();
                RelayWill? will = null;
                if ((cf & 0x04) != 0) { string wt = r.Str(); var wp = r.Bin(); will = new RelayWill(wt, wp, (cf & 0x20) != 0, (cf >> 3) & 3); }
                string? user = (cf & 0x80) != 0 ? r.Str() : null;
                string? pass = (cf & 0x40) != 0 ? r.Str() : null;
                return new MqttConnect(id, ka, (cf & 0x02) != 0, will, user, pass);
            }
            case 2:
            {
                if (len != 2) throw new MqttProtocolException("bad CONNACK");
                byte f = r.Byte(); byte code = r.Byte();
                return new MqttConnAck((f & 1) != 0, code);
            }
            case 3:
            {
                int qos = (flags >> 1) & 3;
                if (qos == 3) throw new MqttProtocolException("bad qos 3");
                string topic = r.Str();
                ushort id = qos > 0 ? r.U16() : (ushort)0;
                return new MqttPublish(topic, r.Rest(), qos, (flags & 1) != 0, (flags & 8) != 0, id);
            }
            case 4: return new MqttPubAck(r.U16());
            case 8:
            {
                ushort id = r.U16();
                var list = new List<(string, int)>();
                while (r.Left > 0) { string f = r.Str(); list.Add((f, r.Byte())); }
                return new MqttSubscribe(id, list);
            }
            case 9:
            {
                ushort id = r.U16();
                return new MqttSubAck(id, r.Rest());
            }
            case 10:
            {
                ushort id = r.U16();
                var list = new List<string>();
                while (r.Left > 0) list.Add(r.Str());
                return new MqttUnsubscribe(id, list);
            }
            case 11: return new MqttUnsubAck(r.U16());
            case 12: return new MqttPingReq();
            case 13: return new MqttPingResp();
            case 14: return new MqttDisconnect();
        }
        throw new MqttProtocolException("unsupported packet type " + type);
    }
}
/// <summary>Turns a byte stream into packets. A WebSocket message may hold half a packet or several; feed bytes as they arrive.</summary>
public sealed class MqttStreamDecoder
{
    private byte[] _buf = new byte[1024];
    private int _len;

    public int Buffered => _len;

    public void Feed(ReadOnlySpan<byte> data, List<MqttPacket> output)
    {
        if (_len + data.Length > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + data.Length));
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
        int start = 0;
        while (true)
        {
            int avail = _len - start;
            if (avail < 2) break;
            int mult = 1, rem = 0, i = 1;
            bool complete = false;
            while (true)
            {
                if (start + i >= _len) break;                    // length bytes not all here yet
                int d = _buf[start + i];
                rem += (d & 0x7F) * mult;
                i++;
                if ((d & 0x80) == 0) { complete = true; break; }
                mult *= 128;
                if (i > 4) throw new MqttProtocolException("remaining length longer than 4 bytes");
            }
            if (!complete) break;
            int total = i + rem;
            if (avail < total) break;
            output.Add(MqttCodec.Decode(_buf[start], _buf, start + i, rem));
            start += total;
        }
        if (start > 0)
        {
            Buffer.BlockCopy(_buf, start, _buf, 0, _len - start);
            _len -= start;
        }
    }
}
