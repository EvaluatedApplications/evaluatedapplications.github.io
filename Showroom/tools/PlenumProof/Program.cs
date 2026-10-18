using Phasor;
using ShowroomPlenum;

// Proof for The Plenum (logic). See the csproj header. Every expectation below is a brute-force model (plain dictionaries of
// (key, tags, time) -> count) rebuilt into a fresh Pleroma and compared cell for cell with the game's store via Equals.

int failures = 0, checks = 0;
long[] KnownAnswers = { 170264799, 329584401, 542789403 };
void Check(bool ok, string what)
{
    checks++;
    if (!ok) { failures++; Console.WriteLine("  FAIL: " + what); }
}
void Section(string s) => Console.WriteLine("== " + s);

// ----------------------------------------------------------------------------------------------- the model
Zone[] AllZones = { Zone.Deck, Zone.Hand, Zone.Board, Zone.Discard, Zone.Engine };

bool SameAsModel(PlenumGame g, Model m, out string why)
{
    foreach (var z in new[] { Zone.Deck, Zone.Hand, Zone.Board, Zone.Discard, Zone.Engine })
        if (!m.Rebuild(z).Equals(g.Of(z))) { why = z + " differs from the brute-force rebuild"; return false; }
    if (g.PlaysLeft != m.Plays) { why = $"plays {g.PlaysLeft} vs {m.Plays}"; return false; }
    if (g.Turn != m.Turn) { why = $"turn {g.Turn} vs {m.Turn}"; return false; }
    if (g.DrawsMade != m.Draws) { why = $"draws {g.DrawsMade} vs {m.Draws}"; return false; }
    why = "";
    return true;
}

// A game whose hand holds both wanted cards (search a seed, deterministic).
ulong SeedWith(params ulong[] keys)
{
    for (ulong s = 1; s < 5000; s++)
    {
        var g = new PlenumGame(s);
        g.TryHand(out var h);
        if (keys.All(k => h.Any(x => x.Key == k))) return s;
    }
    throw new Exception("no seed");
}

// ---------------------------------------------------------------------------- 1. shapes and the hand limit
Section("shapes");
{
    var g = new PlenumGame(7);
    Check(g.Deck.Total() + g.Hand.Total() == 36, "36 cards between Deck and Hand at the start");
    Check(g.Board.Total() == 8, "opening Board has 8 cards");
    Check(g.HandTotal == PlenumGame.StartHand, "opening hand size");
    Check(g.HandReadable, "opening hand readable");
    Console.WriteLine($"  cells per zone: {g.Deck.CellCount}, engine {g.Engine.CellCount}, tempo {g.Tempo.CellCount}");

    // 8 kinds read, 9 refuse
    var h = new Pleroma(PlenumShapes.Zone());
    for (ulong k = 1; k <= 8; k++) h.Add(PlenumGame.Item(PlenumCards.ByKey(k)!), 1 + (long)k % 3);
    Check(h.TryKeys(out var eight) && eight.Count == 8, "8 kinds read");
    h.Add(PlenumGame.Item(PlenumCards.ByKey(9)!), 1);
    Check(!h.TryKeys(out _), "9 kinds refuse (TryKeys)");
    Check(h.Total() > 0 && PlenumGame.TagCounts(h).Sum() > 0, "unreadable hand still has an exact total and tag counts");
    var h2 = new Pleroma(PlenumShapes.Zone());
    h2.Add(PlenumGame.Item(PlenumCards.ByKey(1)!), 30);
    h2.Add(PlenumGame.Item(PlenumCards.ByKey(2)!), 30);
    Check(h2.TryKeys(out var many) && many.Count == 2 && many[0].Count == 30, "many copies of few kinds read");
    // all twelve on the board (dense read)
    var b = new Pleroma(PlenumShapes.Zone());
    foreach (var u in PlenumCards.Units) b.Add(PlenumGame.Item(u), 2);
    Check(PlenumGame.Kinds(b).Count == 12, "Counts() reads 12 kinds on a zone");
}

// -------------------------------------------------------------------------------------- 2. the draw and the rng
Section("draw and generator");
{
    var a = new PlenumRng(42, 0); var a2 = new PlenumRng(42, 0); var c = new PlenumRng(42, 1);
    bool same = true; for (int i = 0; i < 100; i++) same &= a.NextInt64(1000) == a2.NextInt64(1000);
    Check(same, "same (seed, stream) gives the same numbers");
    Check(new PlenumRng(42, 0).NextInt64(1000) != new PlenumRng(43, 0).NextInt64(1000) || new PlenumRng(42, 5).NextInt64(1000000) != new PlenumRng(43, 5).NextInt64(1000000), "seed matters");
    bool inRange = true; var r = new PlenumRng(1, 1); for (int i = 0; i < 5000; i++) { long x = r.NextInt64(33); inRange &= x >= 0 && x < 33; }
    Check(inRange, "NextInt64(33) stays in [0, 33)");
    // Known-answer vectors: pins the stream so every browser/runtime must give the same numbers
    var kv = new PlenumRng(1, 0);
    var got = new[] { kv.NextInt64(1_000_000_007), kv.NextInt64(1_000_000_007), kv.NextInt64(1_000_000_007) };
    Console.WriteLine("  known-answer (seed 1, stream 0, mod 1000000007): " + string.Join(", ", got));
    Check(got.SequenceEqual(KnownAnswers), "generator matches its pinned known answers");

    // Pleroma takes the Random subclass: exhaustive check of SampleKey against the brute cumulative pick
    var g = new PlenumGame(3);
    long total = g.Deck.Total();
    var counts = g.Deck.Counts();
    bool allOk = true;
    for (long pick = 0; pick < total; pick++)
    {
        ulong expect = 0; long rest = pick;
        for (int k = 0; k < counts.Length; k++) { if (rest < counts[k]) { expect = (ulong)k; break; } rest -= counts[k]; }
        ulong gotKey = g.Deck.SampleKey(new FixedRng(pick));
        allOk &= gotKey == expect;
    }
    Check(allOk, $"SampleKey lands on the brute cumulative pick for every r in [0, {total})");
    // the generator really is what the draw calls: two games with the same seed draw the same hand
    var g1 = new PlenumGame(99); var g2 = new PlenumGame(99);
    g1.TryHand(out var ha); g2.TryHand(out var hb);
    Check(ha.SequenceEqual(hb), "same seed deals the same opening hand");
    Check(!new PlenumGame(100).Hand.Equals(g1.Hand) || !new PlenumGame(101).Hand.Equals(g1.Hand), "another seed deals another hand");
    // the generator never touches Random.Shared or the clock: replay twice, stores equal
    var g3 = new PlenumGame(99); g3.Draw(); g3.Draw(); var g4 = new PlenumGame(99); g4.Draw(); g4.Draw();
    Check(g3.Hand.Equals(g4.Hand) && g3.Deck.Equals(g4.Deck), "replaying the same draws gives equal stores");
    // empirical spread of the first-draw key over many seeds follows the exact shares loosely (not asserted as a claim): every key reachable
    var seen = new HashSet<ulong>();
    for (ulong s = 1; s <= 400; s++) { var gg = new PlenumGame(s); gg.TryHand(out var hh); foreach (var x in hh) seen.Add(x.Key); }
    Check(seen.Count == 12, "all twelve cards are drawable");
}

// ------------------------------------------------------------------------------- 3. each card operation vs brute
// A "scenario" runs one operation on a fresh game and on the model; the comparison is the same for the real operation and for
// each mutant. Mutants must be caught.
(bool Ok, string Why) RunScenario(string name, Func<PlenumGame, ulong> seedPick, Action<PlenumGame> real, Action<Model, PlenumGame> expect)
{
    var g = new PlenumGame(seedPick(null!));
    var m = new Model(g);
    real(g);
    expect(m, g);
    var ok = SameAsModel(g, m, out var why);
    return (ok, why);
}

ulong seedEmberSpark = SeedWith(1, 2);
Console.WriteLine("  first seed with Ember and Spark in hand: " + seedEmberSpark);
ulong seedAny = 5;

Section("operations vs brute-force rebuild");
{
    // Draw
    var (ok, why) = RunScenario("draw", _ => seedAny, g => g.Draw(), (m, g) => m.Draw());
    Check(ok, "Draw: " + why);
    // Play a unit (first kind in the hand)
    ulong playKey = 0;
    (ok, why) = RunScenario("play", _ => seedAny, g => { g.TryHand(out var h); playKey = h[0].Key; g.PlayUnit(playKey); }, (m, g) => m.Play(playKey));
    Check(ok, "Play unit: " + why);
    // Flip each of the eight tags
    for (int bit = 0; bit < 8; bit++)
    {
        int b2 = bit;
        (ok, why) = RunScenario("flip", _ => seedAny, g => g.Flip(b2), (m, g) => m.Flip(b2));
        Check(ok, $"Flip {Tags.Names[bit]}: {why}");
    }
    // Flip twice = identity
    {
        var g = new PlenumGame(seedAny); var before = g.Board.Clone();
        g.Flip(0); Check(!before.Equals(g.Board), "Flip changes the Board"); g.Flip(0);
        Check(before.Equals(g.Board), "Flip twice restores the Board cell for cell");
    }
    // Tally: reads, never writes; equals the brute count
    for (int bit = 0; bit < 8; bit++)
    {
        var g = new PlenumGame(seedAny); var m = new Model(g); var boardBefore = g.Board.Clone();
        var res = g.Tally(bit);
        Check(boardBefore.Equals(g.Board), $"Tally {Tags.Names[bit]} leaves the Board unchanged");
        Check(res.Says.Contains($"{m.BoardWith(bit)} card"), $"Tally {Tags.Names[bit]} reads the brute count {m.BoardWith(bit)} (said: {res.Says})");
        m.Plays--;
        Check(SameAsModel(g, m, out var w2), "Tally state: " + w2);
    }
    // Fuse the recipes
    foreach (var (a, b) in new (ulong, ulong)[] { (1, 2), (4, 1), (6, 1), (8, 1), (11, 1) })
    {
        ulong sd = SeedWith(a, b);
        ulong ka = a, kb = b;
        (ok, why) = RunScenario("fuse", _ => sd, g => g.Fuse(ka, kb), (m, g) => m.Fuse(ka, kb));
        Check(ok, $"Fuse {a}+{b}: {why}");
    }
    {   // the catalogue recipes land on a catalogue card with the matching tags
        foreach (var (a, b, want) in new (ulong, ulong, ulong)[] { (1, 2, 3), (4, 1, 5), (6, 1, 7), (8, 1, 9), (11, 1, 12) })
        {
            var ua = PlenumCards.ByKey(a)!; var ub = PlenumCards.ByKey(b)!; var uw = PlenumCards.ByKey(want)!;
            Check(a + b == want && (ua.Tags ^ ub.Tags) == uw.Tags, $"recipe {ua.Name} + {ub.Name} = {uw.Name}");
        }
    }
    // Fuse two copies of one card
    {
        var g = new PlenumGame(1); bool found = false;
        for (ulong s = 1; s < 3000 && !found; s++) { g = new PlenumGame(s); g.TryHand(out var h); found = h.Any(x => x.Count >= 2); }
        g.TryHand(out var hh); ulong k = hh.First(x => x.Count >= 2).Key; var m = new Model(g);
        g.Fuse(k, k); m.Fuse(k, k);
        Check(SameAsModel(g, m, out var w), "Fuse a card with its own copy: " + w);
    }
    // Put the hand back
    (ok, why) = RunScenario("putback", _ => seedAny, g => g.PutBack(), (m, g) => m.PutBack());
    Check(ok, "Put back: " + why);
    // End turn
    (ok, why) = RunScenario("endturn", _ => seedAny, g => g.EndTurn(), (m, g) => m.EndTurn());
    Check(ok, "End turn: " + why);
    // Engine ticks, including a long absence in one call
    foreach (long n in new long[] { 1, 5, 6, 7, 600, 86400, 3_000_001 })
    {
        long nn = n;
        (ok, why) = RunScenario("tick", _ => seedAny, g => g.EngineTick(nn), (m, g) => m.Tick(nn));
        Check(ok, $"Engine.Tick({n}): {why}");
    }
    {
        var g1 = new PlenumGame(1); var g2 = new PlenumGame(1);
        g1.EngineTick(4); g1.EngineTick(9); g2.EngineTick(13);
        Check(g1.Engine.Equals(g2.Engine), "Tick(4) then Tick(9) equals Tick(13)");
        var g3 = new PlenumGame(1); var start = g3.Engine.Clone(); g3.EngineTick(6);
        Check(start.Equals(g3.Engine), "Tick(6) is a full turn of the cycle");
    }
    // Harvest is a read: position 0 count follows the brute model and nothing is incremented
    {
        var g = new PlenumGame(1); var m = new Model(g);
        for (int step = 0; step < 14; step++)
        {
            long total = g.Harvest();
            long exp = m.Z[(int)Zone.Engine].Where(e => e.Key.Time == 0).Sum(e => e.Value);
            Check(total == exp, $"harvest at step {step}: read {total}, brute {exp}");
            var ring = g.EngineRing();
            for (int t = 0; t < 6; t++)
                Check(ring[t] == m.Z[(int)Zone.Engine].Where(e => e.Key.Time == t).Sum(e => e.Value), $"ring slot {t} at step {step}");
            var before = g.Engine.Clone(); g.Harvest();
            Check(before.Equals(g.Engine), "reading the harvest changes nothing");
            g.EngineTick(1); m.Tick(1);
        }
    }
}

// ------------------------------------------------------------------------------------ 4. unreadable hand and Tidy
Section("hand limit");
{
    // keep drawing with no plays until the hand has 9 kinds
    PlenumGame? bad = null;
    for (ulong s = 1; s < 200 && bad is null; s++)
    {
        var g = new PlenumGame(s);
        for (int i = 0; i < 12 && g.HandReadable; i++) g.Draw();
        if (!g.HandReadable) bad = g;
    }
    Check(bad is not null, "a hand can be pushed past 8 kinds by drawing");
    if (bad is not null)
    {
        long total = bad.HandTotal; var tags = bad.HandTagCounts();
        Check(!bad.TryHand(out _), "the unreadable hand refuses TryKeys");
        Check(bad.PlayUnit(1).Refused && bad.Fuse(1, 2).Refused, "no card can be played from an unreadable hand");
        Check(total > 8 && tags.Sum() > 0, "an unreadable hand still has an exact total and tag counts");
        long deckBefore = bad.Deck.Total();
        bad.PutBack();
        Check(bad.HandReadable && bad.HandTotal == 0, "putting the hand back makes it readable (empty)");
        Check(bad.Deck.Total() == deckBefore + total, "every card returned to the Deck");
    }
}

// ----------------------------------------------------------------------------------------- 5. long random play
Section("random legal play vs the model (300 steps x 40 seeds)");
{
    int steps = 0, bad = 0;
    for (ulong s = 1; s <= 40; s++)
    {
        var g = new PlenumGame(s); var m = new Model(g); var rnd = new Random((int)s);
        for (int i = 0; i < 300; i++)
        {
            int pick = rnd.Next(7);
            if (pick == 0 && g.Deck.Total() > 0) { g.Draw(); m.Draw(); }
            else if (pick == 1 && g.HandReadable && g.PlaysLeft > 0)
            {
                g.TryHand(out var h); if (h.Count == 0) continue;
                var k = h[rnd.Next(h.Count)].Key; g.PlayUnit(k); m.Play(k);
            }
            else if (pick == 2 && g.PlaysLeft > 0) { int b = rnd.Next(8); g.Flip(b); m.Flip(b); }
            else if (pick == 3 && g.PlaysLeft > 0) { int b = rnd.Next(8); g.Tally(b); m.Plays--; }
            else if (pick == 4 && g.HandReadable && g.PlaysLeft > 0)
            {
                g.TryHand(out var h); var flat = h.SelectMany(x => Enumerable.Repeat(x.Key, (int)x.Count)).ToList();
                if (flat.Count < 2) continue;
                int i1 = rnd.Next(flat.Count), i2 = rnd.Next(flat.Count - 1); if (i2 >= i1) i2++;
                g.Fuse(flat[i1], flat[i2]); m.Fuse(flat[i1], flat[i2]);
            }
            else if (pick == 5) { g.EndTurn(); m.EndTurn(); }
            else if (pick == 6 && !g.HandReadable) { g.PutBack(); m.PutBack(); }
            else { long n = rnd.Next(1, 500); g.EngineTick(n); m.Tick(n); }
            steps++;
            if (!SameAsModel(g, m, out var why)) { bad++; Console.WriteLine($"  seed {s} step {i}: {why}"); break; }
        }
    }
    Check(bad == 0, $"{steps} random legal steps all equal the brute-force rebuild");
}

// ----------------------------------------------------------------------------------------- 6. the scripted win
Section("scripted three-play win");
{
    var g = new PlenumGame(seedEmberSpark);
    Check(!g.GoalReached, "Goal not reached at the start");
    Check(g.Tally(Tags.Fire).Says.Contains("0 cards carry Fire"), "the opening Board holds no Fire");
    // reset the play so the count below is exactly three plays: use a fresh game
    g = new PlenumGame(seedEmberSpark);
    int plays = 0;
    var f = g.Fuse(1, 2); plays++;                      // Ember + Spark = Wisp on the Board
    Check(!f.Refused && f.Says.Contains("Wisp"), "Fuse makes Wisp");
    var fl = g.Flip(Tags.Fire); plays++;                // the eight old cards turn Fire, Wisp turns off
    var t = g.Tally(Tags.Fire); plays++;
    Check(t.Says.Contains("8 cards carry Fire"), "Tally reads 8 Fire: " + t.Says);
    Check(g.GoalReached && plays == 3 && g.PlaysLeft == 0, "the Goal is reached in three plays");
}

// ----------------------------------------------------------------------------------------------- 7. mutants
Section("mutants (each must be caught)");
{
    int caught = 0, total = 0;
    void Mutant(string name, Func<bool> runsOk) { total++; bool ok = runsOk(); if (!ok) { caught++; Console.WriteLine($"  caught: {name}"); } else { failures++; Console.WriteLine($"  NOT CAUGHT: {name}"); } }

    // M1 Flip with the wrong mask (bit + 1)
    Mutant("Flip uses the wrong mask", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        g.Board.RelabelFlags(null, Tags.Mask(1)); g.Tempo.Remove(new HologramItem(0), 1); m.Flip(0);
        return SameAsModel(g, m, out _);
    });
    // M2 Flip that does not run (a no-op)
    Mutant("Flip does nothing", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        g.Tempo.Remove(new HologramItem(0), 1); m.Flip(0);
        return SameAsModel(g, m, out _);
    });
    // M3 Fuse with tags OR-ed instead of XOR-ed
    Mutant("Fuse ORs the tags", () =>
    {
        ulong sd = SeedWith(1, 2);
        var g = new PlenumGame(sd); var m = new Model(g);
        var ua = PlenumCards.ByKey(1)!; var ub = PlenumCards.ByKey(2)!;
        var inputs = new Pleroma(PlenumShapes.Zone()); inputs.Add(PlenumGame.Item(ua), 1); inputs.Add(PlenumGame.Item(ub), 1);
        g.Hand.Subtract(inputs); g.Discard.Add(inputs);
        g.Board.Add(new HologramItem(3, ua.Tags | ub.Tags), 1);
        g.Tempo.Remove(new HologramItem(0), 1); m.Fuse(1, 2);
        return SameAsModel(g, m, out _);
    });
    // M4 Fuse with keys XOR-ed instead of added (1 xor 2 = 3 happens to match; use 4+1 -> 5 and 6+1 -> 7: xor gives the same for disjoint bits, so use 3+1)
    Mutant("Fuse XORs the keys", () =>
    {
        ulong sd = SeedWith(3, 1);
        var g = new PlenumGame(sd); var m = new Model(g);
        var ua = PlenumCards.ByKey(3)!; var ub = PlenumCards.ByKey(1)!;
        var inputs = new Pleroma(PlenumShapes.Zone()); inputs.Add(PlenumGame.Item(ua), 1); inputs.Add(PlenumGame.Item(ub), 1);
        g.Hand.Subtract(inputs); g.Discard.Add(inputs);
        g.Board.Add(new HologramItem(3 ^ 1, ua.Tags ^ ub.Tags), 1);
        g.Tempo.Remove(new HologramItem(0), 1); m.Fuse(3, 1);
        return SameAsModel(g, m, out _);
    });
    // M5 Play moves the card but forgets to Subtract it from the Hand (a duplicate)
    Mutant("Play forgets to Subtract from the Hand", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        g.TryHand(out var h); var k = h[0].Key; var u = PlenumCards.ByKey(k)!;
        g.Board.Add(PlenumGame.Item(u), h[0].Count); g.Tempo.Remove(new HologramItem(0), 1); m.Play(k);
        return SameAsModel(g, m, out _);
    });
    // M6 Draw that skips the Deck Subtract (the card is copied, not moved)
    Mutant("Draw copies instead of moving", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        ulong k = m.DrawKey(); var u = PlenumCards.ByKey(k)!;
        g.Hand.Add(PlenumGame.Item(u), 1); g.Tempo.Add(2, 1); m.Draw();
        return SameAsModel(g, m, out _);
    });
    // M7 Engine ticks by one less than asked
    Mutant("Engine.Tick(n) ticks n-1", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        g.Engine.Tick(2); m.Tick(3);
        return SameAsModel(g, m, out _);
    });
    // M8 the Tally mutant: counts the wrong tag
    Mutant("Tally counts the wrong tag", () =>
    {
        var g = new PlenumGame(5); var m = new Model(g);
        // Gear/Tide/Hex opening: Cheap is carried by all 8, Fire by none; the mutant asks for Cheap while the model reads Fire
        long wrong = g.Board.CountWhere(PlenumGame.HasTag(Tags.Cheap));
        return wrong == m.BoardWith(Tags.Fire);
    });
    Check(caught == total, $"{caught} of {total} mutants caught");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"FAILED: {failures} of {checks} checks");
return failures == 0 ? 0 : 1;

// replay a game to the same state as `like` (same seed, same draws) so a destructive test can run on a fresh copy
PlenumGame ReplayTo(PlenumGame like)
{
    var g = new PlenumGame(like.Seed);
    while (g.DrawsMade < like.DrawsMade) g.Draw();
    return g;
}

partial class Program { }

