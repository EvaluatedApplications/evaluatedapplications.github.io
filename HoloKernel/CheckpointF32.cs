namespace HoloKernel;

/// <summary>
/// The packed float32 checkpoint format — a CROSS-REPO CONTRACT with PrismStudio's own identical
/// implementation. Training is fp32 on the GPU, so every double a real checkpoint stores is a
/// widened float (measured: every value in the live checkpoint round-trips
/// <c>(double)(float)x == x</c>) — storing float32 on disk/over-the-wire is therefore lossless for
/// this data, and halves both the raw file and the gzip download.
///
/// Given the standard f64 <c>HoloFormer.Serialize()</c> buffer <c>s</c> (a 28-byte header of 7
/// int32, then n float64 values, then an 8-byte trailer; n = (s.Length - 36) / 8), the packed form
/// is: ASCII magic "PF32" (4 bytes) + int32 formatVersion=1 (little-endian) + the original 28-byte
/// header VERBATIM + n float32 little-endian values (same order) + the original 8-byte trailer
/// VERBATIM. Length = 4 + 4 + 28 + 4n + 8.
///
/// <see cref="Unpack"/> is the one direction this repo actually needs at runtime (a visitor's
/// browser only ever reads checkpoints) — it rebuilds the exact f64 buffer so the result can go
/// straight into <c>HoloFormer.Deserialize</c> exactly as before, unaware the format ever changed.
/// It also transparently passes through any buffer that ISN'T packed (legacy f64 files) — so this
/// loader accepts both today's f64 checkpoints and tomorrow's f32 ones with zero per-caller branching.
///
/// <see cref="Pack"/> exists only to build the packed form for verification (an oracle: pack a real
/// f64 checkpoint, unpack it, assert byte-identical) — the coordinator/studio side mints the actual
/// shipped f32 files, not this repo.
///
/// Deliberately plain byte-array loops only — no <c>unsafe</c>, no SIMD, no reflection, no
/// dependency on <c>BitConverter.IsLittleEndian</c> at runtime (explicit little-endian byte
/// read/write instead) — WASM-safe by construction, not by the host happening to be little-endian.
/// </summary>
public static class CheckpointF32
{
    private const byte MagicP = (byte)'P';
    private const byte MagicF = (byte)'F';
    private const byte Magic3 = (byte)'3';
    private const byte Magic2 = (byte)'2';

    private const int MagicLen = 4;
    private const int VersionLen = 4;
    private const int PrefixLen = MagicLen + VersionLen; // 8
    private const int HeaderLen = 28; // 7 int32
    private const int TrailerLen = 8;
    private const int FixedLen = HeaderLen + TrailerLen; // 36, matches "n = (s.Length - 36) / 8"

    public const int FormatVersion = 1;

    /// <summary>True if <paramref name="buffer"/> starts with the "PF32" magic (a packed f32
    /// checkpoint). Does NOT validate <see cref="FormatVersion"/> or overall length — see
    /// <see cref="Unpack"/> for the checked path.</summary>
    public static bool IsPacked(byte[] buffer) =>
        buffer is { Length: >= MagicLen } &&
        buffer[0] == MagicP && buffer[1] == MagicF && buffer[2] == Magic3 && buffer[3] == Magic2;

    /// <summary>
    /// Pack a standard f64 <c>HoloFormer.Serialize()</c> buffer into the "PF32" format above.
    /// Throws <see cref="ArgumentException"/> if <paramref name="f64"/> isn't shaped like a real
    /// serialized checkpoint (too short, or its payload doesn't divide evenly into 8-byte doubles).
    /// </summary>
    public static byte[] Pack(byte[] f64)
    {
        ArgumentNullException.ThrowIfNull(f64);
        if (f64.Length < FixedLen || (f64.Length - FixedLen) % 8 != 0)
            throw new ArgumentException($"Not a valid f64 checkpoint buffer (length {f64.Length}).", nameof(f64));

        int n = (f64.Length - FixedLen) / 8;
        var packed = new byte[PrefixLen + HeaderLen + 4 * n + TrailerLen];

        int pos = 0;
        packed[pos++] = MagicP; packed[pos++] = MagicF; packed[pos++] = Magic3; packed[pos++] = Magic2;
        WriteInt32LE(packed, pos, FormatVersion); pos += VersionLen;

        Array.Copy(f64, 0, packed, pos, HeaderLen); pos += HeaderLen;

        for (int i = 0; i < n; i++)
        {
            double d = ReadDoubleLE(f64, HeaderLen + i * 8);
            WriteFloatLE(packed, pos, (float)d); pos += 4;
        }

        Array.Copy(f64, f64.Length - TrailerLen, packed, pos, TrailerLen);
        return packed;
    }

    /// <summary>
    /// Rebuild the exact f64 <c>HoloFormer.Serialize()</c> buffer from <paramref name="buffer"/>.
    /// If <paramref name="buffer"/> does NOT start with the "PF32" magic, it is returned UNCHANGED
    /// (a legacy f64 checkpoint — the common case until the coordinator ships the first f32 file).
    /// Throws <see cref="InvalidDataException"/> if the magic matches but
    /// <see cref="FormatVersion"/> doesn't (or the buffer is otherwise malformed) — a clear error,
    /// never a silent misread.
    /// </summary>
    public static byte[] Unpack(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!IsPacked(buffer)) return buffer;

        if (buffer.Length < PrefixLen + FixedLen)
            throw new InvalidDataException(
                $"PF32 checkpoint buffer too short ({buffer.Length} bytes) to contain a header+trailer.");

        int version = ReadInt32LE(buffer, MagicLen);
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"Unsupported PF32 checkpoint formatVersion {version} (this loader only knows {FormatVersion}).");

        int payloadLen = buffer.Length - PrefixLen - FixedLen;
        if (payloadLen % 4 != 0)
            throw new InvalidDataException(
                $"PF32 checkpoint payload ({payloadLen} bytes) is not a whole number of float32 values.");
        int n = payloadLen / 4;

        var f64 = new byte[HeaderLen + 8 * n + TrailerLen];

        Array.Copy(buffer, PrefixLen, f64, 0, HeaderLen);

        int srcPos = PrefixLen + HeaderLen;
        for (int i = 0; i < n; i++)
        {
            float f = ReadFloatLE(buffer, srcPos + i * 4);
            WriteDoubleLE(f64, HeaderLen + i * 8, f);
        }

        Array.Copy(buffer, buffer.Length - TrailerLen, f64, f64.Length - TrailerLen, TrailerLen);
        return f64;
    }

    // ---- explicit little-endian byte<->number helpers (no BitConverter.IsLittleEndian reliance) ----

    private static void WriteInt32LE(byte[] buf, int pos, int value)
    {
        buf[pos] = (byte)value;
        buf[pos + 1] = (byte)(value >> 8);
        buf[pos + 2] = (byte)(value >> 16);
        buf[pos + 3] = (byte)(value >> 24);
    }

    private static int ReadInt32LE(byte[] buf, int pos) =>
        buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24);

    private static void WriteFloatLE(byte[] buf, int pos, float value) =>
        WriteInt32LE(buf, pos, BitConverter.SingleToInt32Bits(value));

    private static float ReadFloatLE(byte[] buf, int pos) =>
        BitConverter.Int32BitsToSingle(ReadInt32LE(buf, pos));

    private static void WriteDoubleLE(byte[] buf, int pos, double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        for (int i = 0; i < 8; i++) buf[pos + i] = (byte)(bits >> (8 * i));
    }

    private static double ReadDoubleLE(byte[] buf, int pos)
    {
        long bits = 0;
        for (int i = 0; i < 8; i++) bits |= (long)buf[pos + i] << (8 * i);
        return BitConverter.Int64BitsToDouble(bits);
    }
}
