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
/// Two real call sites share this (Prism.razor's own checkpoint load, and Analyst.razor's
/// independent lazy load for its novelty-scan feature — same checkpoint, same file, loaded from a
/// second place if Prism itself hasn't run yet) — same reasoning HoloKernel already centralises
/// <see cref="AlphaRamp"/>/<see cref="Decoding.Gate"/> for: one real behaviour, not two copies that
/// can silently drift apart.
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
        return (output.ToArray(), compressed.Length);
    }
}
