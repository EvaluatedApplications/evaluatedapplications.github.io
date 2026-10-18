using Phasor;
using ShowroomPlenum;

sealed class Model
{
    public readonly Dictionary<(ulong Key, uint Tags, int Time), long>[] Z = new Dictionary<(ulong, uint, int), long>[5];
    public int Plays, Turn, Draws;
    public ulong Seed;
    public Model(PlenumGame g)
    {
        Seed = g.Seed;
        for (int i = 0; i < 5; i++) Z[i] = new();
        // Independent construction of the opening: the deck is 3 of each card minus what the game says is in the hand.
        foreach (var u in PlenumCards.Units) Add(Zone.Deck, u.Key, u.Tags, 0, 3);
        Add(Zone.Board, 8, PlenumCards.ByKey(8)!.Tags, 0, 3);
        Add(Zone.Board, 6, PlenumCards.ByKey(6)!.Tags, 0, 3);
        Add(Zone.Board, 11, PlenumCards.ByKey(11)!.Tags, 0, 2);
        Add(Zone.Engine, 1, PlenumCards.ByKey(1)!.Tags, 0, 1);
        Add(Zone.Engine, 2, PlenumCards.ByKey(2)!.Tags, 2, 1);
        Add(Zone.Engine, 8, PlenumCards.ByKey(8)!.Tags, 3, 1);
        Add(Zone.Engine, 6, PlenumCards.ByKey(6)!.Tags, 4, 1);
        Add(Zone.Engine, 1, PlenumCards.ByKey(1)!.Tags, 4, 1);
        g.TryHand(out var hand);
        foreach (var (k, c) in hand) { var u = PlenumCards.ByKey(k)!; Add(Zone.Hand, k, u.Tags, 0, c); Add(Zone.Deck, k, u.Tags, 0, -c); }
        Plays = PlenumGame.PlaysPerTurn; Turn = 1; Draws = PlenumGame.StartHand;
    }
    public void Add(Zone z, ulong key, uint tags, int time, long n)
    {
        var d = Z[(int)z];
        d.TryGetValue((key, tags, time), out var c);
        c += n;
        if (c == 0) d.Remove((key, tags, time)); else d[(key, tags, time)] = c;
    }
    public long Count(Zone z, ulong key) => Z[(int)z].Where(e => e.Key.Key == key).Sum(e => e.Value);
    public long Total(Zone z) => Z[(int)z].Values.Sum();

    public ulong DrawKey()
    {
        // brute cumulative pick over keys ascending using the same stream the game documents
        var rng = new PlenumRng(Seed, (ulong)Draws);
        long total = Total(Zone.Deck);
        long pick = rng.NextInt64(total);
        foreach (var k in Z[(int)Zone.Deck].Keys.Select(e => e.Key).Distinct().OrderBy(x => x))
        {
            long c = Count(Zone.Deck, k);
            if (pick < c) return k;
            pick -= c;
        }
        throw new Exception("unreachable");
    }
    public void Draw()
    {
        ulong k = DrawKey();
        var u = PlenumCards.ByKey(k)!;
        Add(Zone.Deck, k, u.Tags, 0, -1); Add(Zone.Hand, k, u.Tags, 0, 1); Draws++;
    }
    public void Play(ulong key)
    {
        var u = PlenumCards.ByKey(key)!;
        long n = Count(Zone.Hand, key);
        Add(Zone.Hand, key, u.Tags, 0, -n); Add(Zone.Board, key, u.Tags, 0, n); Plays--;
    }
    public void XorBoard(uint mask)
    {
        var old = Z[(int)Zone.Board].ToArray();
        Z[(int)Zone.Board].Clear();
        foreach (var (k, c) in old) Add(Zone.Board, k.Key, k.Tags ^ mask, 0, c);
    }
    public void Flip(int bit) { XorBoard(1u << bit); Plays--; }
    public void Fuse(ulong a, ulong b)
    {
        var ua = PlenumCards.ByKey(a)!; var ub = PlenumCards.ByKey(b)!;
        Add(Zone.Hand, a, ua.Tags, 0, -1); Add(Zone.Hand, b, ub.Tags, 0, -1);
        Add(Zone.Discard, a, ua.Tags, 0, 1); Add(Zone.Discard, b, ub.Tags, 0, 1);
        Add(Zone.Board, (a + b) % PlenumShapes.KeyCycle, ua.Tags ^ ub.Tags, 0, 1);
        Plays--;
    }
    public void PutBack()
    {
        foreach (var (k, c) in Z[(int)Zone.Hand].ToArray()) { Add(Zone.Hand, k.Key, k.Tags, 0, -c); Add(Zone.Deck, k.Key, k.Tags, 0, c); }
    }
    public void EndTurn()
    {
        XorBoard(PlenumGame.FlipperMask);
        Plays = PlenumGame.PlaysPerTurn; Turn++;
        if (Total(Zone.Deck) > 0) Draw();
    }
    public void Tick(long n)
    {
        var old = Z[(int)Zone.Engine].ToArray();
        Z[(int)Zone.Engine].Clear();
        foreach (var (k, c) in old) Add(Zone.Engine, k.Key, k.Tags, (int)((k.Time + n) % PlenumShapes.EngineTicks), c);
    }
    public long BoardWith(int bit) => Z[(int)Zone.Board].Where(e => (e.Key.Tags >> bit & 1) != 0).Sum(e => e.Value);

    public Pleroma Rebuild(Zone z)
    {
        var p = new Pleroma(z == Zone.Engine ? PlenumShapes.Engine() : PlenumShapes.Zone());
        foreach (var (k, c) in Z[(int)z]) p.Add(new HologramItem(k.Key, k.Tags, 0, k.Time), c);
        return p;
    }
}


sealed class FixedRng : Random
{
    private readonly long _r;
    public FixedRng(long r) : base(0) { _r = r; }
    public override long NextInt64(long maxValue) => _r < maxValue ? _r : throw new InvalidOperationException("pick not below total");
}

static partial class Known { }
