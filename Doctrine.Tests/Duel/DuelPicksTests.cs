using System.Security.Cryptography;

namespace Doctrine.Tests;

/// <summary>
/// The canonical form and the commitment of a seat's picks (Doctrine\todo\duel-seam-2026-10-19.md, items 4 and the pre-registered mutants). One definition serves both clients and the proof.
/// </summary>
public class DuelPicksTests
{
    static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b);
    static byte[] Nonce(int fill = 7) => Enumerable.Range(0, 16).Select(i => (byte)(fill + i)).ToArray();
    static DuelPicks Sample(int variant = 0) => new([new DraftPick(3, 5 + variant), new DraftPick(1, 2)], DoctrineSet.Of(4, 10), 1, 2);

    // ---- SHA-256: the managed one is the platform's, bit for bit ------------------------------------------------------------------

    [Theory]
    [InlineData("", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("abc", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248D6A61D20638B8E5C026930C3E6039A33CE45964FF2167F6ECEDD419DB06C1")]
    public void Sha256_gives_the_published_vectors(string text, string expected)
        => Assert.Equal(expected, Hex(Sha256.Hash(System.Text.Encoding.ASCII.GetBytes(text))));

    [Fact]
    public void Sha256_of_a_million_a_is_the_published_vector()
        => Assert.Equal("CDC76E5C9914FB9281A1C7E284D73E67F1809A48A497200E046D39CCC7112CD0", Hex(Sha256.Hash(new byte[1_000_000].Select(_ => (byte)'a').ToArray())));

    [Fact]
    public void Sha256_equals_the_platforms_for_every_length_near_the_block_edges_and_for_random_inputs()
    {
        var rng = new Random(20261019);
        foreach (int len in Enumerable.Range(0, 200).Concat([255, 256, 257, 1000, 4097]))
        {
            var data = new byte[len]; rng.NextBytes(data);
            Assert.Equal(Hex(SHA256.HashData(data)), Hex(Sha256.Hash(data)));
        }
        for (int i = 0; i < 3000; i++)
        {
            var data = new byte[rng.Next(0, 300)]; rng.NextBytes(data);
            Assert.Equal(SHA256.HashData(data), Sha256.Hash(data));
        }
    }

    // ---- the canonical form --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_canonical_form_is_pinned_byte_for_byte()
        // "DU1", 2 packs, (kind 1, count 2), (kind 3, count 5), tokens 4 and 10, formation 1, rerolls 2: zig-zag varints
        => Assert.Equal("445531040204060A081400000204", Hex(Sample().Canonical()));

    [Fact]
    public void The_order_the_packs_were_listed_in_changes_nothing()
    {
        var a = DuelPicks.Canonical([new(3, 5), new(1, 2), new(3, 1)], DoctrineSet.Of(4), 0, 0);
        var b = DuelPicks.Canonical([new(1, 2), new(3, 1), new(3, 5)], DoctrineSet.Of(4), 0, 0);
        Assert.Equal(a, b);
        Assert.Equal(new DuelPicks([new(3, 5), new(1, 2)], DoctrineSet.Of(4)), new DuelPicks([new(1, 2), new(3, 5)], DoctrineSet.Of(4)));
    }

    [Fact]
    public void The_slot_order_of_the_tokens_matters()
        => Assert.NotEqual(DuelPicks.Canonical([], DoctrineSet.Of(4, 10), 0, 0), DuelPicks.Canonical([], DoctrineSet.Of(10, 4), 0, 0));

    [Fact]
    public void Every_field_changes_the_form()
    {
        var baseForm = Sample().Canonical();
        Assert.NotEqual(baseForm, Sample(1).Canonical());
        Assert.NotEqual(baseForm, new DuelPicks(Sample().Units, DoctrineSet.Of(4, 10), 2, 2).Canonical());
        Assert.NotEqual(baseForm, new DuelPicks(Sample().Units, DoctrineSet.Of(4, 10), 1, 1).Canonical());
        Assert.NotEqual(baseForm, new DuelPicks(Sample().Units, DoctrineSet.Of(4, 11), 1, 2).Canonical());
    }

    [Fact]
    public void Parse_and_canonical_are_each_others_inverse_for_random_picks_including_nonsense()
    {
        var rng = new Random(11);
        for (int i = 0; i < 800; i++)
        {
            var units = Enumerable.Range(0, rng.Next(0, 9)).Select(_ => new DraftPick(rng.Next(-3, 12), rng.Next(-2, 200000))).ToList();
            var doc = new DoctrineSet(rng.Next(-5, 70), rng.Next(0, 60), rng.Next(0, 60), rng.Next(0, 60));
            var p = new DuelPicks(units, doc, rng.Next(-1, 5), rng.Next(-1, 5));
            var bytes = p.Canonical();
            Assert.True(DuelPicks.TryParse(bytes, out var back, out var why), why);
            Assert.Equal(p, back);
            Assert.Equal(bytes, back!.Canonical());
        }
    }

    [Fact]
    public void Anything_that_is_not_exactly_a_canonical_form_is_refused_with_a_reason()
    {
        var good = Sample().Canonical();
        Assert.True(DuelPicks.TryParse(good, out _, out _));
        void Refuses(byte[] bytes, string because) { Assert.False(DuelPicks.TryParse(bytes, out var p, out var why), because); Assert.Null(p); Assert.NotEmpty(why); }
        Refuses([.. good, 0], "trailing byte");
        Refuses(good[..^1], "truncated");
        Refuses([], "empty");
        Refuses([(byte)'X', .. good[1..]], "wrong magic");
        Refuses(DuelPicksTestsBytes.Replace(good, 3, [0x84, 0x00]), "an over-long number (two bytes for 2)");
        // packs out of order: (3,5) then (1,2)
        Refuses([.. good[..3], 0x04, 0x06, 0x0A, 0x02, 0x04, .. good[8..]], "packs out of order");
        Refuses([.. good[..3], 0xFE, 0xFF, 0xFF, 0xFF, 0x0F], "a pack count that cannot fit the bytes");
        Refuses([.. good[..3], 0x82, 0x01, .. Enumerable.Repeat((byte)2, 130), .. good[8..]], "more packs than the limit");
    }

    [Fact]
    public void Damaged_forms_never_throw()
    {
        var rng = new Random(5);
        var good = Sample().Canonical();
        for (int i = 0; i < 3000; i++)
        {
            var bytes = (byte[])good.Clone();
            for (int k = rng.Next(1, 4); k > 0; k--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
            if (rng.Next(3) == 0) Array.Resize(ref bytes, rng.Next(0, bytes.Length + 3));
            if (DuelPicks.TryParse(bytes, out var p, out _)) Assert.Equal(bytes, p!.Canonical());   // accepted only if it is a canonical form
        }
    }

    // ---- the commitment ------------------------------------------------------------------------------------------------------------

    /// <summary>All the checks of the commitment, run with a probe. Null when every one holds.</summary>
    static string? CommitChecks(DuelProbe probe)
    {
        var c = Sample().Canonical(); var n = Nonce();
        byte[] Commit(byte[] canon, byte[] nonce, int round, Seat seat) => DuelPicks.Commit(canon, nonce, round, seat, probe);
        bool Verify(byte[] commit, byte[] canon, byte[] nonce, int round, Seat seat) => DuelPicks.Verify(commit, canon, nonce, round, seat, probe);

        var h = Commit(c, n, 2, Seat.A);
        if (h.Length != 32) return "a commitment is 32 bytes";
        if (!Verify(h, c, n, 2, Seat.A)) return "an honest reveal does not verify";
        if (!h.SequenceEqual(Commit(c, n, 2, Seat.A))) return "a commitment is not a function of its inputs";
        if (h.SequenceEqual(Commit(c, Nonce(9), 2, Seat.A))) return "another nonce gave the same commitment: the nonce does not hide anything";
        if (h.SequenceEqual(Commit(c, n, 3, Seat.A))) return "another round gave the same commitment";
        if (h.SequenceEqual(Commit(c, n, 2, Seat.B))) return "another seat gave the same commitment: a commitment can be copied into the other seat's slot";
        if (h.SequenceEqual(Commit(Sample(1).Canonical(), n, 2, Seat.A))) return "other picks gave the same commitment";
        if (Verify(h, Sample(1).Canonical(), n, 2, Seat.A)) return "a tampered reveal (other picks) verified";
        if (Verify(h, c, Nonce(9), 2, Seat.A)) return "a reveal with another nonce verified";
        if (Verify(h, c, n, 3, Seat.A)) return "a reveal for another round verified";
        if (Verify(h, c, n, 2, Seat.B)) return "a reveal from the other seat verified: the commitment was copied";
        var bad = (byte[])h.Clone(); bad[31] ^= 1;
        if (Verify(bad, c, n, 2, Seat.A)) return "a one-bit-wrong commitment verified";
        if (Verify(h[..31], c, n, 2, Seat.A)) return "a short commitment verified";
        return null;
    }

    [Fact] public void The_commitment_holds_every_check() => Assert.Null(CommitChecks(DuelProbe.None));
    [Fact] public void A_commitment_that_ignores_the_nonce_is_caught() => Assert.Contains("nonce", CommitChecks(DuelProbe.CommitIgnoresNonce));
    [Fact] public void A_commitment_that_ignores_the_seat_is_caught() => Assert.Contains("seat", CommitChecks(DuelProbe.CommitIgnoresSeat));
    [Fact] public void A_verify_that_accepts_a_tampered_reveal_is_caught() => Assert.Contains("tampered", CommitChecks(DuelProbe.VerifyAcceptsTampered));

    [Fact]
    public void A_nonce_under_sixteen_bytes_is_refused_and_verify_says_false_without_throwing()
    {
        var c = Sample().Canonical();
        Assert.Throws<ArgumentException>(() => DuelPicks.Commit(c, new byte[15], 0, Seat.A));
        Assert.Throws<ArgumentException>(() => DuelPicks.Commit(c, [], 0, Seat.A));
        Assert.False(DuelPicks.Verify(new byte[32], c, new byte[15], 0, Seat.A));
    }

    [Fact]
    public void The_commitment_hides_the_picks()
    {
        var c = Sample().Canonical();
        // not the hash of the picks alone, and the picks do not appear in it
        Assert.NotEqual(Hex(SHA256.HashData(c)), Hex(DuelPicks.Commit(c, Nonce(), 0, Seat.A)));
        // a dictionary attack: the other seat knows the whole space of picks of a small round but not the nonce; hashing every candidate with a guessed nonce finds none
        var secret = DuelPicks.Commit(c, Nonce(100), 4, Seat.B);
        int hits = 0;
        for (int kind = 0; kind < Roster.Size; kind++)
            for (int count = 1; count <= 40; count++)
                for (int tok = 0; tok < DoctrineVocabulary.Size; tok++)
                {
                    var guess = DuelPicks.Canonical([new(kind, count)], DoctrineSet.Of(tok), 0, 0);
                    if (DuelPicks.Verify(secret, guess, new byte[16], 4, Seat.B) || DuelPicks.Verify(secret, guess, Nonce(), 4, Seat.B)) hits++;
                }
        Assert.Equal(0, hits);
        // the same picks under many nonces: unrelated commitments, about half the bits differ (128 +/- 11.3 per pair; 80..176 is far outside chance either way)
        var rng = new Random(3); var seen = new HashSet<string>();
        byte[]? prev = null;
        for (int i = 0; i < 60; i++)
        {
            var nonce = new byte[16]; rng.NextBytes(nonce);
            var h = DuelPicks.Commit(c, nonce, 0, Seat.A);
            Assert.True(seen.Add(Hex(h)));
            if (prev is not null)
            {
                int bits = h.Zip(prev).Sum(p => System.Numerics.BitOperations.PopCount((uint)(p.First ^ p.Second)));
                Assert.InRange(bits, 80, 176);
            }
            prev = h;
        }
    }

    [Fact]
    public void A_reveal_travels_as_bytes_and_is_checked_then_parsed()
    {
        // what a host does: receive (canonical, nonce) for a round and a seat, verify against the commit received earlier, parse
        var picks = Sample(); var nonce = Nonce(42);
        var commit = DuelPicks.Commit(picks.Canonical(), nonce, 1, Seat.B);
        var wireCanonical = (byte[])picks.Canonical().Clone();
        Assert.True(DuelPicks.Verify(commit, wireCanonical, nonce, 1, Seat.B));
        Assert.True(DuelPicks.TryParse(wireCanonical, out var parsed, out _));
        Assert.Equal(picks, parsed);
        wireCanonical[5] ^= 0x02;                                  // one bit of the reveal changed on the way
        Assert.False(DuelPicks.Verify(commit, wireCanonical, nonce, 1, Seat.B));
    }
}

static class DuelPicksTestsBytes
{
    /// <summary>The bytes with the byte at <paramref name="at"/> replaced by <paramref name="with"/> (which may be longer).</summary>
    public static byte[] Replace(byte[] b, int at, byte[] with) => [.. b[..at], .. with, .. b[(at + 1)..]];
}
