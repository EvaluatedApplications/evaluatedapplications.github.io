using System.Text;

namespace Doctrine;

/// <summary>
/// The replay record format (seam e). A battle is simulated ONCE by one authority; every other device PLAYS BACK this
/// recording and never recomputes it. Hand-written binary (varints), so no reflection serialisation and no float on the wire.
/// Layout: "DRP" + version, arena, seed, tick limit, frames (tick, blobs, events), result.
/// </summary>
public static class ReplayCodec
{
    static readonly byte[] Magic = "DRP"u8.ToArray();

    public static byte[] Encode(BattleReplay r)
    {
        var w = new Writer();
        foreach (var b in Magic) w.Byte(b);
        w.Var(r.Version); w.Str(r.ArenaId); w.Var(r.Seed); w.Var(r.TickLimit);
        w.Var(r.Frames.Count);
        foreach (var f in r.Frames)
        {
            w.Var(f.Tick);
            w.Var(f.Blobs.Count);
            foreach (var b in f.Blobs) { w.Var(b.Id); w.Byte((byte)b.Side); w.Var(b.Key); w.Var(b.Slot); w.Var(b.X); w.Var(b.Y); w.Var(b.Strength); }
            w.Var(f.Events.Count);
            foreach (var e in f.Events) { w.Var(e.Source); w.Var(e.Target); w.Var(e.Damage); }
        }
        w.Byte((byte)r.Result.Outcome); w.Var(r.Result.Ticks);
        Losses(w, r.Result.AttackerLosses); Losses(w, r.Result.DefenderLosses);
        return w.ToArray();
    }

    static void Losses(Writer w, IReadOnlyList<CohortLoss> l)
    {
        w.Var(l.Count);
        foreach (var x in l) { w.Var(x.Key); w.Var(x.Slot); w.Var(x.Lost); }
    }

    /// <summary>Decode a recording. Never throws: damaged or unknown-version data returns false with a reason.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out BattleReplay? replay, out string error)
    {
        replay = null; error = "";
        try
        {
            var rd = new Reader(data.ToArray());
            foreach (var m in Magic) if (rd.Byte() != m) { error = "not a replay"; return false; }
            int version = rd.Var();
            if (version != BattleReplay.CurrentVersion) { error = $"unknown replay version {version}"; return false; }
            string arena = rd.Str(); int seed = rd.Var(); int limit = rd.Var();
            int n = rd.Count();
            var frames = new List<ReplayFrame>(n);
            for (int i = 0; i < n; i++)
            {
                int tick = rd.Var();
                int bc = rd.Count();
                var blobs = new List<ReplayBlob>(bc);
                for (int j = 0; j < bc; j++) blobs.Add(new(rd.Var(), (Side)rd.Byte(), rd.Var(), rd.Var(), rd.Var(), rd.Var(), rd.Var()));
                int ec = rd.Count();
                var ev = new List<ReplayEvent>(ec);
                for (int j = 0; j < ec; j++) ev.Add(new(rd.Var(), rd.Var(), rd.Var()));
                frames.Add(new(tick, blobs, ev));
            }
            var outcome = (BattleStatus)rd.Byte(); int ticks = rd.Var();
            var al = ReadLosses(rd); var dl = ReadLosses(rd);
            if (!rd.AtEnd) { error = "trailing bytes"; return false; }
            replay = new(version, arena, seed, limit, frames, new(outcome, ticks, al, dl));
            return true;
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            error = e.Message; replay = null; return false;
        }
    }

    static List<CohortLoss> ReadLosses(Reader rd)
    {
        int n = rd.Count(); var l = new List<CohortLoss>(n);
        for (int i = 0; i < n; i++) l.Add(new(rd.Var(), rd.Var(), rd.Var()));
        return l;
    }

    /// <summary>A stable hash of a recording (FNV over the integers): equal recordings, equal digest.</summary>
    public static ulong Digest(BattleReplay r)
    {
        var h = new Fnv();
        h.Add(r.Version); h.Add(r.Seed); h.Add(r.TickLimit);
        foreach (var c in r.ArenaId) h.Add(c);
        foreach (var f in r.Frames)
        {
            h.Add(f.Tick);
            foreach (var b in f.Blobs) { h.Add(b.Id); h.Add((int)b.Side); h.Add(b.Key); h.Add(b.Slot); h.Add(b.X); h.Add(b.Y); h.Add(b.Strength); }
            foreach (var e in f.Events) { h.Add(e.Source); h.Add(e.Target); h.Add(e.Damage); }
        }
        h.Add((int)r.Result.Outcome); h.Add(r.Result.Ticks);
        foreach (var l in r.Result.AttackerLosses) { h.Add(l.Key); h.Add(l.Slot); h.Add(l.Lost); }
        foreach (var l in r.Result.DefenderLosses) { h.Add(l.Key); h.Add(l.Slot); h.Add(l.Lost); }
        return h.Value;
    }

    sealed class Writer
    {
        readonly List<byte> _b = new();
        public void Byte(byte v) => _b.Add(v);
        /// <summary>Zig-zag varint (signed).</summary>
        public void Var(int v)
        {
            uint z = (uint)((v << 1) ^ (v >> 31));
            while (z >= 0x80) { _b.Add((byte)(z | 0x80)); z >>= 7; }
            _b.Add((byte)z);
        }
        public void Str(string s) { var u = Encoding.UTF8.GetBytes(s); Var(u.Length); _b.AddRange(u); }
        public byte[] ToArray() => _b.ToArray();
    }

    sealed class Reader(byte[] d)
    {
        int _p;
        public bool AtEnd => _p == d.Length;
        public byte Byte() => _p < d.Length ? d[_p++] : throw new FormatException("truncated");
        public int Var()
        {
            uint z = 0; int shift = 0;
            while (true)
            {
                byte b = Byte();
                z |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) throw new FormatException("bad varint");
            }
            return (int)(z >> 1) ^ -(int)(z & 1);
        }
        /// <summary>A count that cannot exceed the bytes left (a damaged length cannot allocate gigabytes).</summary>
        public int Count() { int n = Var(); if (n < 0 || n > d.Length - _p) throw new FormatException("bad count"); return n; }
        public string Str() { int n = Count(); var s = Encoding.UTF8.GetString(d, _p, n); _p += n; return s; }
    }
}

/// <summary>
/// Plays a recording back: frame by frame, nothing recomputed. The page animates between consecutive frames
/// (blobs share ids across frames); that is drawing, not simulation.
/// </summary>
public sealed class ReplayPlayer(BattleReplay replay)
{
    public BattleReplay Replay { get; } = replay;
    public int Length => Replay.Frames.Count;
    public ReplayFrame FrameAt(int index) => Replay.Frames[Math.Clamp(index, 0, Length - 1)];
    public IEnumerable<ReplayFrame> Frames => Replay.Frames;
}
