using System.IO.Compression;
using System.Net.Http;

namespace HoloKernel;

/// <summary>
/// Fetches a gzip-precompressed static asset and decompresses it client-side.
///
/// GitHub Pages (Showroom's static host) serves files byte-for-byte with no content-negotiation
/// and no on-the-fly compression for binary assets. Blazor's own <c>_framework/</c> files get
/// <c>.br</c>/<c>.gz</c> precompression automatically from <c>dotnet publish</c>, but a raw data
/// file dropped straight into <c>wwwroot/data/</c> (Prism's checkpoint) never picks that up — it
/// ships at its full raw size on every fetch. Shipping ONE pre-gzipped copy of the file
/// (<c>&lt;name&gt;.gz</c>, ordinary gzip at rest, produced by any dumb <c>GZipStream</c> one-liner,
/// no dependency on the Blazor build pipeline) and decompressing it here with the BCL's own
/// <see cref="GZipStream"/> gets the download-size win without any JS interop or browser-API
/// surface to maintain. <see cref="GZipStream"/> already works inside a Blazor WebAssembly runtime
/// (the WASM runtime pack ships a WASM-compiled zlib), so this is plain, ordinary .NET — not a
/// WASM-specific workaround, and not the browser-native <c>DecompressionStream</c> API either;
/// deliberately avoided that path since this needs zero JS to reach.
///
/// Five real call sites share this (Prism, Nano Stories, The Cartographer, Analyst's novelty scan,
/// Prose's "score with Prism" — each an independent lazy load of the same checkpoint file, since
/// whichever tool a visitor opens first is the one that actually pays the fetch) — same reasoning
/// HoloKernel already centralises <see cref="AlphaRamp"/> for (and, since 2026-09-05,
/// <c>Prism.Inference.Gate</c>): one real behaviour, not several copies that can silently drift
/// apart. Also the one choke point <see cref="CheckpointF32.Unpack"/> hooks into, so every one of
/// those five callers transparently accepts either checkpoint storage format with no page edits.
/// </summary>
public static class CheckpointFetch
{
    /// <summary>
    /// Fetches <paramref name="gzUrl"/> and returns the decompressed bytes plus the compressed byte
    /// count actually sent over the wire (for boot-log narration). Throws on any HTTP or gzip
    /// failure — callers should treat this exactly like a plain <c>GetByteArrayAsync</c> call: no
    /// silent fallback to an uncompressed sibling is attempted here, a failure should surface loudly
    /// to whatever caller-level error handling already exists (both current call sites already wrap
    /// their whole load sequence in a try/catch that reports a real, visible error rather than
    /// leaving the tool silently broken).
    /// </summary>
    public static async Task<(byte[] Bytes, int CompressedLength)> FetchAndDecompressGzipAsync(HttpClient http, string gzUrl)
    {
        var compressed = await http.GetByteArrayAsync(gzUrl);
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(compressed.Length * 3);   // rough headroom guess; MemoryStream grows past it fine either way
        await gzip.CopyToAsync(output);
        // Single choke point every checkpoint consumer shares (Prism, Nano Stories, The
        // Cartographer, Analyst's novelty scan, Prose's "score with Prism") — transparently accepts
        // both today's plain f64 buffer and a packed f32 one (see CheckpointF32), so callers never
        // need to know or care which format shipped. A legacy f64 buffer passes through byte-for-byte
        // unchanged; only a "PF32"-prefixed buffer is rewritten. Bytes.Length (used in every caller's
        // boot-log narration) is therefore always the real f64 byte count either way.
        return (CheckpointF32.Unpack(output.ToArray()), compressed.Length);
    }
}
