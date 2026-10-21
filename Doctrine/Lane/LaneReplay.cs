using System.Text;
using Tracer.Integration;

namespace Doctrine.Lane;

/// <summary>The plays applied at one pause (the tick is a multiple of the phase length).</summary>
public sealed record LanePlayBatch(int Tick, IReadOnlyList<LanePlay> Plays);

/// <summary>The state hash at a pause (tick 0 is the first pause; the last entry may be the tick the match ended on).</summary>
public readonly record struct LanePause(int Tick, ulong Hash);

/// <summary>
/// A whole match as PLAYS + SEED: the setup, the seed, every play batch with its tick, and the hash at each pause. Because the sim is deterministic this is enough to
/// rebuild every snapshot, and it is far smaller than recording snapshots (see <see cref="LaneReplayCodec"/> and the cost table in Lane\LANE.md). Playing it back
/// (<see cref="LaneReplayPlayer"/>) re-simulates and checks every pause hash, so a tampered or drifted replay is caught at the first pause that differs.
/// </summary>
public sealed record LaneReplay(int Version, LaneSetup Setup, int Seed, IReadOnlyList<LanePlayBatch> Batches, IReadOnlyList<LanePause> Pauses);

public static class LaneReplayCodec
{
    static readonly byte[] Magic = "LRP"u8.ToArray();
    public const int CurrentVersion = 1;

    public static byte[] Encode(LaneReplay r)
    {
        var w = new List<byte>();
        foreach (var b in Magic) w.Add(b);
        Var(w, r.Version); Var(w, r.Seed);
        var s = r.Setup;
        Var(w, s.Lanes); Var(w, s.LaneCols); Var(w, s.PhaseTicks); Var(w, s.TickCap); Var(w, s.BaseHp); Var(w, s.MaxUnitsPerSide);
        Var(w, r.Batches.Count);
        foreach (var batch in r.Batches)
        {
            Var(w, batch.Tick); Var(w, batch.Plays.Count);
            foreach (var p in batch.Plays)
                switch (p)
                {
                    case SpawnPlay sp: w.Add(0); w.Add((byte)sp.Side); Var(w, sp.Lane); Var(w, sp.Kind); Var(w, sp.Count); break;
                    case OrderPlay op: w.Add(1); w.Add((byte)op.Side); Var(w, op.Lane); Var(w, (int)op.Order); break;
                    default: throw new ArgumentException("unknown play type");
                }
        }
        Var(w, r.Pauses.Count);
        foreach (var p in r.Pauses) { Var(w, p.Tick); for (int i = 0; i < 8; i++) w.Add((byte)(p.Hash >> (8 * i))); }
        return w.ToArray();
    }

    /// <summary>Decodes a replay. Never throws: damaged data returns false with a reason.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out LaneReplay? replay, out string error)
    {
        replay = null; error = "";
        try
        {
            var d = data.ToArray(); int pos = 0;
            byte Byte() => pos < d.Length ? d[pos++] : throw new FormatException("truncated");
            int Rd()
            {
                uint z = 0; int shift = 0;
                while (true)
                {
                    byte b = Byte(); z |= (uint)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7; if (shift > 28) throw new FormatException("bad varint");
                }
                return (int)(z >> 1) ^ -(int)(z & 1);
            }
            int Count() { int n = Rd(); if (n < 0 || n > d.Length - pos) throw new FormatException("bad count"); return n; }
            foreach (var m in Magic) if (Byte() != m) { error = "not a lane replay"; return false; }
            int version = Rd();
            if (version != CurrentVersion) { error = $"unknown lane replay version {version}"; return false; }
            int seed = Rd();
            var setup = new LaneSetup { Lanes = Rd(), LaneCols = Rd(), PhaseTicks = Rd(), TickCap = Rd(), BaseHp = Rd(), MaxUnitsPerSide = Rd() };
            setup.Validate();
            int nb = Count(); var batches = new List<LanePlayBatch>(nb);
            for (int i = 0; i < nb; i++)
            {
                int tick = Rd(), np = Count(); var plays = new List<LanePlay>(np);
                for (int j = 0; j < np; j++)
                {
                    byte t = Byte();
                    if (t == 0) plays.Add(new SpawnPlay((Side)Byte(), Rd(), Rd(), Rd()));
                    else if (t == 1) plays.Add(new OrderPlay((Side)Byte(), Rd(), (FieldOrderKind)Rd()));
                    else { error = $"unknown play type {t}"; return false; }
                }
                batches.Add(new(tick, plays));
            }
            int npz = Count(); var pauses = new List<LanePause>(npz);
            for (int i = 0; i < npz; i++)
            {
                int tick = Rd(); ulong h = 0;
                for (int k = 0; k < 8; k++) h |= (ulong)Byte() << (8 * k);
                pauses.Add(new(tick, h));
            }
            if (pos != d.Length) { error = "trailing bytes"; return false; }
            replay = new(version, setup, seed, batches, pauses);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            error = e.Message; replay = null; return false;
        }
    }

    static void Var(List<byte> w, int v)
    {
        uint z = (uint)((v << 1) ^ (v >> 31));
        while (z >= 0x80) { w.Add((byte)(z | 0x80)); z >>= 7; }
        w.Add((byte)z);
    }
}

/// <summary>The verdict of a playback: every pause hash matched, or the first pause that did not.</summary>
public sealed record LaneReplayCheck(bool Ok, int FirstBadPause, string Why, LaneBattle Final);

/// <summary>Plays a <see cref="LaneReplay"/> back by re-simulating it, checking the state hash at every pause.</summary>
public static class LaneReplayPlayer
{
    /// <param name="onPause">Called at every pause (and at the end) with the snapshot, for a renderer that wants the match back.</param>
    public static async Task<LaneReplayCheck> PlayAsync(LaneReplay r, TracerContext? tracer = null, Action<LaneSnapshot>? onPause = null, CancellationToken ct = default)
    {
        var b = await LaneBattle.CreateAsync(r.Setup, r.Seed, tracer, ct);
        if (r.Pauses.Count == 0 || r.Pauses[0].Tick != 0 || r.Pauses[0].Hash != b.StateHash()) return new(false, 0, "the match does not start from the recorded state", b);
        onPause?.Invoke(b.Snapshot());
        for (int i = 1; i < r.Pauses.Count; i++)
        {
            foreach (var batch in r.Batches) if (batch.Tick == b.Tick) b.ApplyPlays(batch.Plays);
            var step = await b.StepAsync(int.MaxValue, ct);
            if (step.Tick != r.Pauses[i].Tick) return new(false, i, $"pause {i}: tick {step.Tick}, recorded {r.Pauses[i].Tick}", b);
            if (step.Hash != r.Pauses[i].Hash) return new(false, i, $"pause {i}: state hash differs at tick {step.Tick}", b);
            onPause?.Invoke(b.Snapshot());
        }
        return new(true, -1, "", b);
    }
}
