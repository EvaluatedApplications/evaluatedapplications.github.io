namespace Doctrine;

/// <summary>
/// SHA-256 in plain managed integer code (FIPS 180-4). The duel's commitment hashes with THIS, not with the platform's, so a commit means the same on every
/// device and in a trimmed browser build: nothing here is reflection, a platform call or a browser crypto path. It is held to the platform's SHA-256 by tests
/// (the standard vectors and thousands of random inputs). It is not used for anything secret except the commitment.
/// </summary>
internal static class Sha256
{
    static readonly uint[] K =
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    ];

    static uint Rotr(uint x, int n) => (x >> n) | (x << (32 - n));

    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
        int total = (data.Length + 9 + 63) / 64 * 64;
        var buf = new byte[total];
        data.CopyTo(buf);
        buf[data.Length] = 0x80;
        ulong bits = (ulong)data.Length * 8UL;
        for (int i = 0; i < 8; i++) buf[total - 1 - i] = (byte)(bits >> (8 * i));

        var w = new uint[64];
        for (int block = 0; block < total; block += 64)
        {
            for (int i = 0; i < 16; i++)
                w[i] = (uint)buf[block + 4 * i] << 24 | (uint)buf[block + 4 * i + 1] << 16 | (uint)buf[block + 4 * i + 2] << 8 | buf[block + 4 * i + 3];
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rotr(w[i - 15], 7) ^ Rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Rotr(w[i - 2], 17) ^ Rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = unchecked(w[i - 16] + s0 + w[i - 7] + s1);
            }
            uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
            for (int i = 0; i < 64; i++)
            {
                uint S1 = Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = unchecked(h + S1 + ch + K[i] + w[i]);
                uint S0 = Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint t2 = unchecked(S0 + maj);
                h = g; g = f; f = e; e = unchecked(d + t1); d = c; c = b; b = a; a = unchecked(t1 + t2);
            }
            unchecked { h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h; }
        }
        var outp = new byte[32];
        uint[] hs = [h0, h1, h2, h3, h4, h5, h6, h7];
        for (int i = 0; i < 8; i++) { outp[4 * i] = (byte)(hs[i] >> 24); outp[4 * i + 1] = (byte)(hs[i] >> 16); outp[4 * i + 2] = (byte)(hs[i] >> 8); outp[4 * i + 3] = (byte)hs[i]; }
        return outp;
    }
}
