namespace Doctrine.Tests;

/// <summary>
/// The REPLAY contract (CONTRACT.md section e): the recording a battle leaves must play back without recomputation, survive the
/// binary codec bit for bit, and agree with the result it came with. Run on any <see cref="BattleReplay"/> a battle engine produced.
/// </summary>
public static class ReplayContract
{
    public static IReadOnlyList<ContractCase<BattleReplay>> Cases { get; } =
    [
        new("version-and-header", r =>
        {
            Expect.Equal(BattleReplay.CurrentVersion, r.Version, "replay version");
            Expect.True(r.ArenaId.Length > 0 && r.TickLimit > 0, "arena id and tick limit are recorded");
            return Task.CompletedTask;
        }),
        new("frames-are-in-tick-order-from-zero", r =>
        {
            Expect.True(r.Frames.Count >= 1 && r.Frames[0].Tick == 0, "frame 0 is the start");
            for (int i = 0; i < r.Frames.Count; i++) Expect.Equal(i, r.Frames[i].Tick, $"frame {i} tick");
            Expect.Equal(r.Result.Ticks + 1, r.Frames.Count, "frames = ticks + 1");
            Expect.True(r.Result.Ticks <= r.TickLimit, "never past the tick limit");
            return Task.CompletedTask;
        }),
        new("positions-stay-on-the-quantised-grid-and-in-bounds", r =>
        {
            foreach (var f in r.Frames)
                foreach (var b in f.Blobs)
                    Expect.True(b.X >= 0 && b.Y >= 0 && b.X <= 4096 * BattleReplay.Quantum && b.Y <= 4096 * BattleReplay.Quantum, $"blob {b.Id} is out of range at tick {f.Tick}");
            return Task.CompletedTask;
        }),
        new("world-flags-use-only-defined-bits-and-never-clear", r =>
        {
            // The recording carries the arena's changing state as a bitset per frame. Only defined bits may be used, and a bit set by an
            // event of the battle (the gate falling) stays set for the rest of the recording; the start frame is the arena as it began.
            int seen = 0;
            foreach (var f in r.Frames)
            {
                Expect.True(f.WorldFlags >= 0 && (f.WorldFlags & ~WorldFlag.Defined) == 0, $"frame {f.Tick} sets a bit that is not defined ({f.WorldFlags:x})");
                Expect.True((seen & ~f.WorldFlags) == 0, $"frame {f.Tick} clears a world flag that an earlier frame set ({seen:x} -> {f.WorldFlags:x})");
                seen |= f.WorldFlags;
            }
            Expect.Equal(0, r.Frames[0].WorldFlags, "the start frame shows the arena as it began");
            return Task.CompletedTask;
        }),
        new("final-strengths-agree-with-the-result", r =>
        {
            foreach (var (side, losses) in new[] { (Side.Player, r.Result.AttackerLosses), (Side.Enemy, r.Result.DefenderLosses) })
                Expect.Equal(Sample.Standing(r.Frames[0], side) - Sample.Standing(r.Frames[^1], side), losses.Sum(l => l.Lost), $"{side} losses vs the recording");
            Expect.True(r.Result.Outcome != BattleStatus.Running, "a recorded battle is finished");
            return Task.CompletedTask;
        }),
        new("codec-round-trips-bit-for-bit", r =>
        {
            var bytes = ReplayCodec.Encode(r);
            Expect.True(ReplayCodec.TryDecode(bytes, out var back, out var err) && back is not null, $"decode failed: {err}");
            Expect.Equal(ReplayCodec.Digest(r), ReplayCodec.Digest(back!), "decoded replay differs");
            Expect.True(ReplayCodec.Encode(back!).AsSpan().SequenceEqual(bytes), "re-encoding a decoded replay changes the bytes");
            Expect.True(ReplayCodec.Encode(r).AsSpan().SequenceEqual(bytes), "encoding is not deterministic");
            return Task.CompletedTask;
        }),
        new("recording-is-small", r =>
        {
            var n = ReplayCodec.Encode(r).Length;
            Expect.True(n <= 64 * 1024, $"a recording of {r.Frames.Count} frames took {n} bytes; the ladder shelf needs small recordings");
            return Task.CompletedTask;
        }),
        new("damaged-recordings-do-not-throw", r =>
        {
            var bytes = ReplayCodec.Encode(r);
            for (int cut = 0; cut < bytes.Length; cut += Math.Max(1, bytes.Length / 40))
            {
                var ok = ReplayCodec.TryDecode(bytes.AsSpan(0, cut), out var x, out var why);
                Expect.True(!ok && x is null && why.Length > 0, $"a recording cut at {cut} bytes must be refused with a reason");
            }
            var flipped = (byte[])bytes.Clone(); flipped[0] ^= 0x55;
            Expect.True(!ReplayCodec.TryDecode(flipped, out _, out _), "a wrong header is refused");
            return Task.CompletedTask;
        }),
        new("player-yields-every-frame-without-recomputing", r =>
        {
            var p = new ReplayPlayer(r);
            Expect.Equal(r.Frames.Count, p.Length, "player length");
            Expect.True(p.Frames.SequenceEqual(r.Frames), "player frames are the recorded frames, in order");
            Expect.True(ReferenceEquals(p.FrameAt(3), r.Frames[Math.Min(3, r.Frames.Count - 1)]), "FrameAt returns the recorded frame itself");
            return Task.CompletedTask;
        }),
    ];
}
