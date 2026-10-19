using System.Text;

namespace Doctrine;

// The duel's byte format helpers: zig-zag varints like the replay codec, but the reader is STRICT (an over-long varint is refused), so a value has exactly one
// encoding and decode(encode(x)) == x AND encode(decode(b)) == b. That is what lets a commitment be checked against bytes that crossed a wire.

internal sealed class ByteWriter
{
    readonly List<byte> _b = new();
    public void Byte(byte v) => _b.Add(v);
    public void Raw(ReadOnlySpan<byte> v) { foreach (var x in v) _b.Add(x); }
    public void Var(int v) { uint z = (uint)((v << 1) ^ (v >> 31)); while (z >= 0x80) { _b.Add((byte)(z | 0x80)); z >>= 7; } _b.Add((byte)z); }
    public void VarL(long v) { ulong z = (ulong)((v << 1) ^ (v >> 63)); while (z >= 0x80) { _b.Add((byte)(z | 0x80)); z >>= 7; } _b.Add((byte)z); }
    public void U64(ulong v) { for (int i = 0; i < 8; i++) _b.Add((byte)(v >> (8 * i))); }
    public void Bytes(ReadOnlySpan<byte> v) { Var(v.Length); Raw(v); }
    public void Str(string s) => Bytes(Encoding.UTF8.GetBytes(s));
    public void Bool(bool v) => _b.Add(v ? (byte)1 : (byte)0);
    public byte[] ToArray() => _b.ToArray();
}

internal sealed class ByteReader(byte[] d)
{
    int _p;
    public bool AtEnd => _p == d.Length;
    public int Left => d.Length - _p;
    public byte Byte() => _p < d.Length ? d[_p++] : throw new FormatException("truncated");
    public bool Bool() { byte b = Byte(); return b <= 1 ? b == 1 : throw new FormatException("bad flag"); }
    public int Var()
    {
        uint z = 0; int shift = 0;
        while (true)
        {
            byte b = Byte();
            if (shift == 28 && (b & 0xF0) != 0) throw new FormatException("bad varint");
            z |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) { if (shift > 0 && b == 0) throw new FormatException("over-long varint"); break; }
            shift += 7;
            if (shift > 28) throw new FormatException("bad varint");
        }
        return (int)(z >> 1) ^ -(int)(z & 1);
    }
    public long VarL()
    {
        ulong z = 0; int shift = 0;
        while (true)
        {
            byte b = Byte();
            if (shift == 63 && (b & 0xFE) != 0) throw new FormatException("bad varint");
            z |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) { if (shift > 0 && b == 0) throw new FormatException("over-long varint"); break; }
            shift += 7;
            if (shift > 63) throw new FormatException("bad varint");
        }
        return (long)(z >> 1) ^ -(long)(z & 1);
    }
    public ulong U64() { ulong v = 0; for (int i = 0; i < 8; i++) v |= (ulong)Byte() << (8 * i); return v; }
    /// <summary>A count that cannot exceed the bytes left (a damaged length cannot allocate gigabytes).</summary>
    public int Count() { int n = Var(); if (n < 0 || n > Left) throw new FormatException("bad count"); return n; }
    public byte[] Bytes() { int n = Count(); var r = new byte[n]; Array.Copy(d, _p, r, 0, n); _p += n; return r; }
    public string Str() => Encoding.UTF8.GetString(Bytes());
}
