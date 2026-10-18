using Phasor;

namespace ShowroomPlenum;

public enum Zone { Deck, Hand, Board, Discard, Engine }

/// <summary>What one action did: the real call, a plain sentence, and the cells of each zone that changed.</summary>
public sealed record OpResult(string Title, string Call, string Says, IReadOnlyList<(Zone Zone, int[] Cells)> Changed, bool Refused = false)
{
    public int[] CellsOf(Zone z) { foreach (var c in Changed) if (c.Zone == z) return c.Cells; return Array.Empty<int>(); }
}

/// <summary>The Plenum, level 0. ALL game state lives in Pleroma stores (Deck, Hand, Board, Discard, Engine, Tempo) and changes
/// only through Pleroma operations (Add, Subtract, Tick, RelabelFlags, Bind, Restrict, SampleKey and the reads). What is NOT in a
/// store: the match seed (a constant of the level, like a map id), the catalogue of card names and tags (static data), and the
/// request itself (which card the player chose, the number of ticks to apply). Draw n uses the stream (seed, n) where n is read
/// from the Tempo store, so no generator state is kept anywhere.</summary>
public sealed class PlenumGame
{
    public const int GoalFire = 8;
    public const int PlaysPerTurn = 3;
    public const int StartHand = 6;
    /// <summary>The first deal: its opening hand holds Ember and Spark, so the Fuse recipe in the tutorial text is on offer (tools/PlenumProof checks it).</summary>
    public const ulong DefaultSeed = 13;
    public static readonly uint FlipperMask = Tags.Mask(Tags.Fire);

    private const ulong KPlays = 0, KTurn = 1, KDraws = 2, KGoal = 3;

    public ulong Seed { get; }
    public Pleroma Deck { get; }
    public Pleroma Hand { get; }
    public Pleroma Board { get; }
    public Pleroma Discard { get; }
    public Pleroma Engine { get; }
    public Pleroma Tempo { get; }

    public PlenumGame(ulong seed)
    {
        Seed = seed;
        Deck = new Pleroma(PlenumShapes.Zone());
        Hand = new Pleroma(PlenumShapes.Zone());
        Board = new Pleroma(PlenumShapes.Zone());
        Discard = new Pleroma(PlenumShapes.Zone());
        Engine = new Pleroma(PlenumShapes.Engine());
        Tempo = new Pleroma(PlenumShapes.Tempo());

        foreach (var u in PlenumCards.Units) Deck.Add(Item(u), 3);
        // Opening board: eight cards, none of them Fire.
        Board.Add(Item(PlenumCards.ByKey(8)!), 3);
        Board.Add(Item(PlenumCards.ByKey(6)!), 3);
        Board.Add(Item(PlenumCards.ByKey(11)!), 2);
        // Engine: five cards on a six-tick cycle (the Time field is the charge position).
        Engine.Add(new HologramItem(1, PlenumCards.ByKey(1)!.Tags, 0, 0), 1);
        Engine.Add(new HologramItem(2, PlenumCards.ByKey(2)!.Tags, 0, 2), 1);
        Engine.Add(new HologramItem(8, PlenumCards.ByKey(8)!.Tags, 0, 3), 1);
        Engine.Add(new HologramItem(6, PlenumCards.ByKey(6)!.Tags, 0, 4), 1);
        Engine.Add(new HologramItem(1, PlenumCards.ByKey(1)!.Tags, 0, 4), 1);

        Tempo.Add(KPlays, PlaysPerTurn);
        Tempo.Add(KTurn, 1);
        for (int i = 0; i < StartHand; i++) DrawCore();
    }

    public static HologramItem Item(UnitCard u) => new(u.Key, u.Tags);

    public Pleroma Of(Zone z) => z switch
    {
        Zone.Deck => Deck, Zone.Hand => Hand, Zone.Board => Board, Zone.Discard => Discard, _ => Engine,
    };

    // ----------------------------------------------------------------------------------------------- reads

    private long TempoCount(ulong key)
    {
        if (!Tempo.TryKeys(out var keys)) return 0;
        foreach (var (k, c) in keys) if (k == key) return c;
        return 0;
    }

    public int PlaysLeft => (int)TempoCount(KPlays);
    public int Turn => (int)TempoCount(KTurn);
    public int DrawsMade => (int)TempoCount(KDraws);
    public bool GoalReached => TempoCount(KGoal) > 0;

    /// <summary>The hand as (key, copies), only while it holds at most 8 kinds. False = the read refused.</summary>
    public bool TryHand(out IReadOnlyList<(ulong Key, long Count)> kinds) => Hand.TryKeys(out kinds);

    public bool HandReadable => Hand.TryKeys(out _);

    /// <summary>Cards in the hand, whether or not the kinds can be read (an exact total).</summary>
    public long HandTotal => Hand.Total();

    /// <summary>Per tag, how many hand cards carry it (a read that works even when the kinds cannot be read).</summary>
    public long[] HandTagCounts() => TagCounts(Hand);

    public static long[] TagCounts(Pleroma store)
    {
        var res = new long[8];
        for (int b = 0; b < 8; b++) res[b] = store.CountWhere(HasTag(b));
        return res;
    }

    public static FlagPredicate HasTag(int bit) => FlagPredicate.Match(PlenumShapes.FlagBits, Tags.Mask(bit), Tags.Mask(bit));

    /// <summary>The Board by kind: every key with a nonzero count (dense read, never refuses).</summary>
    public List<(ulong Key, long Count)> BoardKinds() => Kinds(Board);

    public static List<(ulong Key, long Count)> Kinds(Pleroma store)
    {
        var res = new List<(ulong, long)>();
        var counts = store.Counts();
        for (int k = 0; k < counts.Length; k++) if (counts[k] != 0) res.Add(((ulong)k, counts[k]));
        return res;
    }

    /// <summary>The Board by tag pattern: every flag word with a nonzero count.</summary>
    public List<(uint Word, long Count)> BoardPatterns()
    {
        var res = new List<(uint, long)>();
        var f = Board.Flags();
        for (int w = 0; w < f.Length; w++) if (f[w] != 0) res.Add(((uint)w, f[w]));
        return res;
    }

    /// <summary>The exact draw shares: each deck kind with its copies and the deck total.</summary>
    public (List<(ulong Key, long Count)> Kinds, long Total) DrawShares() => (Kinds(Deck), Deck.Total());

    /// <summary>The Engine's time ring: cards at each of the six positions (a read of the Time field).</summary>
    public long[] EngineRing() => Engine.Times();

    /// <summary>Harvest now: the cards at position 0 of the cycle, one read of the Time field. A read, not a counter. (A split by
    /// tag would need the Flags x Time joint slice, 1,536 more cells per store; this slice does not carry it.)</summary>
    public long Harvest() => Engine.Times()[0];

    // ------------------------------------------------------------------------------------------- actions

    private static Pleroma Pack(HologramItem item, long n)
    {
        var p = new Pleroma(PlenumShapes.Zone());
        p.Add(item, n);
        return p;
    }

    private void Move(Pleroma from, Pleroma to, HologramItem item, long n)
    {
        var pack = Pack(item, n);
        from.Subtract(pack);
        to.Add(pack);
    }

    private ulong[][] Snap(Zone[] zones)
    {
        var s = new ulong[zones.Length][];
        for (int i = 0; i < zones.Length; i++) s[i] = Of(zones[i]).RawCells.ToArray();
        return s;
    }

    private OpResult Finish(string title, string call, string says, Zone[] zones, ulong[][] before, bool refused = false)
    {
        var changed = new List<(Zone, int[])>();
        for (int i = 0; i < zones.Length; i++)
        {
            var now = Of(zones[i]).RawCells;
            var idx = new List<int>();
            for (int c = 0; c < now.Length; c++) if (now[c] != before[i][c]) idx.Add(c);
            if (idx.Count > 0) changed.Add((zones[i], idx.ToArray()));
        }
        return new OpResult(title, call, says, changed, refused);
    }

    private OpResult Refuse(string title, string call, string says) => new(title, call, says, Array.Empty<(Zone, int[])>(), true);

    private ulong DrawCore()
    {
        var rng = new PlenumRng(Seed, (ulong)DrawsMade);
        ulong key = Deck.SampleKey(rng);
        Move(Deck, Hand, Item(PlenumCards.ByKey(key)!), 1);
        Tempo.Add(KDraws, 1);
        return key;
    }

    /// <summary>Draw one card: Deck.SampleKey(rng), then a move (Subtract from the Deck, Add to the Hand). Free.</summary>
    public OpResult Draw()
    {
        const string call = "Deck.SampleKey(rng)";
        if (Deck.Total() <= 0) return Refuse("Draw", call, "The Deck is empty.");
        var zones = new[] { Zone.Deck, Zone.Hand };
        var before = Snap(zones);
        long total = Deck.Total();
        var shares = Kinds(Deck);
        ulong key = DrawCore();
        long copies = 0;
        foreach (var (k, c) in shares) if (k == key) copies = c;
        return Finish("Draw", call,
            $"Draw = Deck.SampleKey(rng): one exact integer draw below {total}. It landed on {PlenumCards.NameOf(key)}, which held {copies} of the {total}. Then a move: Subtract from the Deck, Add to the Hand.",
            zones, before);
    }

    private OpResult? NeedsPlay(string title, string call)
    {
        if (PlaysLeft <= 0) return Refuse(title, call, "No plays left this turn. End the turn.");
        return null;
    }

    /// <summary>Play a unit card from the hand: all copies of that kind move to the Board (Subtract + Add). Costs one play.</summary>
    public OpResult PlayUnit(ulong key)
    {
        const string call = "Hand.Subtract(pack); Board.Add(pack)";
        if (!HandReadable) return Refuse("Play", call, "The hand cannot be read right now, so no card can be chosen. Tidy it first.");
        if (NeedsPlay("Play", call) is { } r) return r;
        var u = PlenumCards.ByKey(key);
        long n = u is null ? 0 : Hand.Count(key);
        if (u is null || n <= 0) return Refuse("Play", call, "That card is not in the hand.");
        var zones = new[] { Zone.Hand, Zone.Board };
        var before = Snap(zones);
        Move(Hand, Board, Item(u), n);
        Tempo.Remove(new HologramItem(KPlays), 1);
        return Finish("Play " + u.Name, call,
            $"A move is a Subtract and an Add: {n} {u.Name} left the Hand and arrived on the Board with tags {Tags.List(u.Tags)}.", zones, before);
    }

    /// <summary>Tally: count the Board cards that carry a tag, with one CountWhere read. Costs one play. The Goal is met when
    /// the Fire count it reads is at least 8.</summary>
    public OpResult Tally(int bit)
    {
        string call = $"Board.CountWhere(FlagPredicate.Match(8, {Tags.Bits(Tags.Mask(bit))}, {Tags.Bits(Tags.Mask(bit))}))";
        if (NeedsPlay("Tally", call) is { } r) return r;
        var zones = new[] { Zone.Board };
        var before = Snap(zones);
        long n = Board.CountWhere(HasTag(bit));
        Tempo.Remove(new HologramItem(KPlays), 1);
        string goal = "";
        if (bit == Tags.Fire && n >= GoalFire && !GoalReached)
        {
            Tempo.Add(KGoal, 1);
            goal = " That meets the Goal.";
        }
        return Finish("Tally " + Tags.Names[bit], call,
            $"Tally reads the Board in one pass without changing it: {n} {(n == 1 ? "card carries" : "cards carry")} {Tags.Names[bit]}.{goal}", zones, before);
    }

    /// <summary>Flip: XOR one tag on every Board card at once. Costs one play. Twice is the identity.</summary>
    public OpResult Flip(int bit)
    {
        uint mask = Tags.Mask(bit);
        string call = $"Board.RelabelFlags(null, {Tags.Bits(mask)})";
        if (NeedsPlay("Flip", call) is { } r) return r;
        var zones = new[] { Zone.Board };
        var before = Snap(zones);
        long had = Board.CountWhere(HasTag(bit));
        long total = Board.Total();
        Board.RelabelFlags(null, mask);
        long now = Board.CountWhere(HasTag(bit));
        Tempo.Remove(new HologramItem(KPlays), 1);
        string name = Tags.Names[bit];
        return Finish("Flip " + name, call,
            $"Flip = RelabelFlags(null, {Tags.Bits(mask)}): {(had == 1 ? "the 1 card that was" : $"the {had} cards that were")} {name} {(had == 1 ? "is" : "are")} not {name} now, and the other {total - had} {(total - had == 1 ? "is" : "are")}. Board {name}: {had} then {now}. Twice undoes it.",
            zones, before);
    }

    /// <summary>Duel only: Flip aimed at the OPPONENT'S Board. The play is spent here, the RelabelFlags runs on the target's Board.</summary>
    public OpResult FlipOther(PlenumGame target, int bit)
    {
        uint mask = Tags.Mask(bit);
        string call = $"Opponent.Board.RelabelFlags(null, {Tags.Bits(mask)})";
        if (NeedsPlay("Flip", call) is { } r) return r;
        long had = target.Board.CountWhere(HasTag(bit));
        long total = target.Board.Total();
        target.Board.RelabelFlags(null, mask);
        long now = target.Board.CountWhere(HasTag(bit));
        Tempo.Remove(new HologramItem(KPlays), 1);
        string name = Tags.Names[bit];
        // the cells that changed belong to the other side's Board, so no cell of this side's stores is reported
        return new OpResult("Flip " + name + " on their Board", call,
            $"Flip = RelabelFlags(null, {Tags.Bits(mask)}) on THEIR Board: their {name} count went from {had} to {now} of their {total} cards. Twice undoes it.",
            Array.Empty<(Zone, int[])>());
    }

    /// <summary>Fuse two cards from the hand with Bind: keys add, tags XOR. The result goes to the Board, the two inputs to the
    /// Discard. Costs one play.</summary>
    public OpResult Fuse(ulong keyA, ulong keyB)
    {
        const string call = "a.Bind(b)";
        if (!HandReadable) return Refuse("Fuse", call, "The hand cannot be read right now, so no card can be chosen. Tidy it first.");
        if (NeedsPlay("Fuse", call) is { } r) return r;
        var a = PlenumCards.ByKey(keyA);
        var b = PlenumCards.ByKey(keyB);
        if (a is null || b is null) return Refuse("Fuse", call, "Choose two cards from the hand.");
        long need = keyA == keyB ? 2 : 1;
        if (Hand.Count(keyA) < need || Hand.Count(keyB) < need) return Refuse("Fuse", call, "The hand does not hold those cards.");
        var zones = new[] { Zone.Hand, Zone.Board, Zone.Discard };
        var before = Snap(zones);
        var pa = Pack(Item(a), 1);
        var pb = Pack(Item(b), 1);
        var inputs = Pack(Item(a), 1);
        inputs.Add(Item(b), 1);
        pa.Bind(pb);
        // read the one item the bind made, from the store itself
        var counts = pa.Counts();
        ulong key = 0;
        for (int k = 0; k < counts.Length; k++) if (counts[k] == 1) key = (ulong)k;
        var flags = pa.Flags();
        uint word = 0;
        for (int w = 0; w < flags.Length; w++) if (flags[w] == 1) word = (uint)w;
        Hand.Subtract(inputs);
        Discard.Add(inputs);
        Board.Add(pa);
        Tempo.Remove(new HologramItem(KPlays), 1);
        string made = PlenumCards.NameOf(key, word);
        return Finish("Fuse " + a.Name + " + " + b.Name, call,
            $"Fuse = Bind: keys add ({a.Key} + {b.Key} = {key}) and tags XOR ({Tags.Bits(a.Tags)} ^ {Tags.Bits(b.Tags)} = {Tags.Bits(word)}). That makes {made} ({Tags.List(word)}). It went to the Board; the two inputs went to the Discard.",
            zones, before);
    }

    /// <summary>Put the whole hand back in the Deck (the way out of a hand with more than 8 kinds): the hand is cloned, subtracted
    /// from itself and added to the Deck. Free. No card is lost.</summary>
    public OpResult PutBack()
    {
        const string call = "Deck.Add(hand); Hand.Subtract(hand)";
        var zones = new[] { Zone.Hand, Zone.Deck };
        var before = Snap(zones);
        var pack = Hand.Clone();
        long n = pack.Total();
        Hand.Subtract(pack);
        Deck.Add(pack);
        return Finish("Put the hand back", call,
            $"Put back = Subtract from the Hand and Add to the Deck: {n} {(n == 1 ? "card" : "cards")} returned. An empty hand reads again; a hand reads while it holds 8 kinds or fewer.",
            zones, before);
    }
    /// <summary>End the turn: the Flipper acts on the Board, plays refill, one card is drawn.</summary>
    public OpResult EndTurn()
    {
        string call = $"Board.RelabelFlags(null, {Tags.Bits(FlipperMask)})";
        var zones = new[] { Zone.Board, Zone.Deck, Zone.Hand };
        var before = Snap(zones);
        Board.RelabelFlags(null, FlipperMask);
        long left = PlaysLeft;
        if (left < PlaysPerTurn) Tempo.Add(KPlays, PlaysPerTurn - left);
        Tempo.Add(KTurn, 1);
        string drew = "";
        if (Deck.Total() > 0) drew = " It drew " + PlenumCards.NameOf(DrawCore()) + ".";
        return Finish("End turn", call,
            $"The Flipper used the same verb you have: RelabelFlags(null, {Tags.Bits(FlipperMask)}) on the Board. Every Fire card is not Fire now and every other card is.{drew}",
            zones, before);
    }

    /// <summary>Apply n ticks to the Engine: Tick(n). n is the number of ticks the clock says passed, applied in one call.</summary>
    public OpResult EngineTick(long n)
    {
        string call = $"Engine.Tick({n})";
        var zones = new[] { Zone.Engine };
        var before = Snap(zones);
        if (n > 0) Engine.Tick(n);
        return Finish("Engine", call, $"Engine.Tick({n}): every Engine card moved {n} steps along its cycle of {PlenumShapes.EngineTicks}.", zones, before);
    }
}
