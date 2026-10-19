namespace Doctrine.General;

/// <summary>
/// What a card (a doctrine token) IS, as small integers, so a taste learned from some cards carries to cards never seen: the family, the order type
/// (family and kind), an argument key (the trait it touches, the kind it merges, the line it steers, the trigger level), and the id itself.
/// Computed once from the vocabulary table, never typed per token.
/// </summary>
internal static class CardFeatures
{
    public const int Families = 4, Types = 13, Args = 32, Reps = 3;
    static readonly int[] Fam, Typ, Arg;

    static CardFeatures()
    {
        int n = DoctrineVocabulary.Size;
        Fam = new int[n]; Typ = new int[n]; Arg = new int[n];
        for (int t = 0; t < n; t++)
        {
            var o = DoctrineVocabulary.Decode(t);
            Fam[t] = (int)o.Family;
            switch (o.Family)
            {
                case OrderFamily.Army:
                    Typ[t] = o.Kind;                                        // 0..5
                    var k = (ArmyOrderKind)o.Kind;
                    Arg[t] = k switch
                    {
                        ArmyOrderKind.Flip or ArmyOrderKind.Aura or ArmyOrderKind.Cull => 1 + o.A,   // 1..8: a trait
                        ArmyOrderKind.Merge => 9 + o.A,                                              // 9..16: a kind
                        ArmyOrderKind.Rotate => 16 + o.A,                                            // 17..18
                        _ => 19,                                                                     // Age
                    };
                    break;
                case OrderFamily.Field: Typ[t] = 6 + o.Kind; Arg[t] = 20 + o.A; break;               // 6..11; lines 20..22
                case OrderFamily.Trigger: Typ[t] = 12; Arg[t] = 23 + o.A - 1; break;                 // 23..25
            }
        }
    }

    public static int Family(int t) => Fam[t];
    public static int Type(int t) => Typ[t];
    public static int Arg_(int t) => Arg[t];
}

/// <summary>
/// The taste half of the lookup: it answers "which of THESE cards would this player take, and in what order" from how often cards with the same
/// FEATURES were taken when they were on offer. Per slot it keeps integer counts (picked, available) for the family, the order type, the argument,
/// whether the card repeats last round's picks, and the id; the chance a card is taken at a slot is built from the coarse to the fine (each level
/// smoothed towards the one above), the type and argument and repeat evidence multiplied as odds, so a player who likes "Hold on any line" or "anything
/// on the front line" is learned from a few rounds and carries to cards never offered before. A separate fill record says how many slots the player
/// usually fills; before any example it fills none, so a new general says nothing.
/// </summary>
internal sealed class CardTable
{
    const int Slots = DoctrineVocabulary.MaxSlots, Tokens = 64, Alpha = 4, AlphaId = 4, Window = 64;
    // [slot][value] picked / available weights, one pair of arrays per feature level
    readonly int[][] _pFam = New(Families), _aFam = New(Families), _pTyp = New(CardFeatures.Types), _aTyp = New(CardFeatures.Types),
                     _pArg = New(CardFeatures.Args), _aArg = New(CardFeatures.Args), _pRep = New(CardFeatures.Reps), _aRep = New(CardFeatures.Reps),
                     _pId = New(Tokens), _aId = New(Tokens);
    readonly int[] _pAll = new int[Slots], _aAll = new int[Slots], _filled = new int[Slots], _empty = new int[Slots];
    const int Families = CardFeatures.Families;
    int _examples;
    readonly bool _history;

    public CardTable(bool history) { _history = history; }

    static int[][] New(int values) { var a = new int[Slots][]; for (int k = 0; k < Slots; k++) a[k] = new int[values]; return a; }

    /// <summary>0 = not among last round's picks, 1 = same order type as one of them, 2 = the very card. Always 0 when history is off.</summary>
    public static int Rep(int token, ReadOnlySpan<int> prev, bool history)
    {
        if (!history) return 0;
        int r = 0;
        foreach (var p in prev) { if (p == 0) continue; if (p == token) return 2; if (CardFeatures.Type(p) == CardFeatures.Type(token)) r = 1; }
        return r;
    }

    /// <summary>
    /// Teach one choice (<paramref name="weight"/> 2 for a choice the player made or a battle won). The cards on offer are <paramref name="offer"/>; the
    /// picks are the player's tokens in slot order (End dropped, none twice). A lost battle (weight -1) takes picked weight back, never below zero.
    /// </summary>
    public void Add(ReadOnlySpan<int> offer, ReadOnlySpan<int> picks, ReadOnlySpan<int> prev, int weight)
    {
        if (weight > 0 && ++_examples >= Window) Halve();
        int n = 0; foreach (var p in picks) if (p != 0) n++;
        for (int k = 0; k < Slots; k++)
        {
            if (k < n) _filled[k] = Math.Max(0, _filled[k] + weight);
            else if (k == n) { if (weight > 0) _empty[k] += weight; }
            if (k >= n) continue;
            int pick = picks[k];
            if (weight > 0)
            {
                foreach (var c in offer)
                {
                    if (c == 0 || Before(picks, k, c)) continue;
                    Bump(_aAll, _aFam, _aTyp, _aArg, _aRep, _aId, k, c, Rep(c, prev, _history), weight);
                }
            }
            Bump(_pAll, _pFam, _pTyp, _pArg, _pRep, _pId, k, pick, Rep(pick, prev, _history), weight);
        }
    }

    static bool Before(ReadOnlySpan<int> picks, int k, int c) { for (int i = 0; i < k; i++) if (picks[i] == c) return true; return false; }

    void Bump(int[] all, int[][] fam, int[][] typ, int[][] arg, int[][] rep, int[][] id, int k, int c, int r, int w)
    {
        all[k] = Math.Max(0, all[k] + w);
        ref int f = ref fam[k][CardFeatures.Family(c)]; f = Math.Max(0, f + w);
        ref int t = ref typ[k][CardFeatures.Type(c)]; t = Math.Max(0, t + w);
        ref int a = ref arg[k][CardFeatures.Arg_(c)]; a = Math.Max(0, a + w);
        ref int rp = ref rep[k][r]; rp = Math.Max(0, rp + w);
        ref int i = ref id[k][c]; i = Math.Max(0, i + w);
    }

    void Halve()
    {
        _examples /= 2;
        foreach (var set in new[] { _pFam, _aFam, _pTyp, _aTyp, _pArg, _aArg, _pRep, _aRep, _pId, _aId })
            foreach (var row in set) for (int i = 0; i < row.Length; i++) row[i] /= 2;
        for (int k = 0; k < Slots; k++) { _pAll[k] /= 2; _aAll[k] /= 2; _filled[k] /= 2; _empty[k] /= 2; }
    }

    /// <summary>How many leading slots the player usually fills: slot k counts while it was filled more (by weight) than it was the first empty slot.</summary>
    public int Fill()
    {
        int n = 0;
        while (n < Slots && _filled[n] > _empty[n]) n++;
        return n;
    }

    static double Smooth(int p, int a, double prior, int alpha) => (Math.Min(p, a) + alpha * prior) / (a + alpha);   // picked can never exceed available, whatever the examples
    static double Odds(double r) => r / (1.0 - r);

    /// <summary>The chance, from the counts, that the player takes <paramref name="card"/> at slot <paramref name="k"/> when it is still on the table. Plain arithmetic (no log, no exp): the same on every device.</summary>
    public double Chance(int k, int card, int rep)
    {
        double r0 = (_pAll[k] + 1.0) / (_aAll[k] + 8.0);
        double fam = Smooth(_pFam[k][CardFeatures.Family(card)], _aFam[k][CardFeatures.Family(card)], r0, Alpha);
        double typ = Smooth(_pTyp[k][CardFeatures.Type(card)], _aTyp[k][CardFeatures.Type(card)], fam, Alpha);
        double arg = Smooth(_pArg[k][CardFeatures.Arg_(card)], _aArg[k][CardFeatures.Arg_(card)], fam, Alpha);
        double o = Odds(fam) * (Odds(typ) / Odds(fam)) * (Odds(arg) / Odds(fam));
        if (_history) o *= Odds(Smooth(_pRep[k][rep], _aRep[k][rep], r0, Alpha)) / Odds(r0);
        double prior = o / (1.0 + o);
        return Smooth(_pId[k][card], _aId[k][card], prior, AlphaId);
    }

    /// <summary>The ranked cards: for each filled slot the offered, not yet used card with the highest chance (ties: the lowest id). End (0) in the slots the player does not fill.</summary>
    public void Predict(ReadOnlySpan<int> offer, ReadOnlySpan<int> prev, Span<int> tokens)
    {
        tokens.Clear();
        int fill = Fill();
        for (int k = 0; k < fill && k < tokens.Length; k++)
        {
            int best = 0; double bv = double.NegativeInfinity;
            foreach (var c in offer)
            {
                if (c == 0 || Used(tokens, k, c)) continue;
                double v = Chance(k, c, Rep(c, prev, _history));
                if (v > bv) { bv = v; best = c; }
            }
            tokens[k] = best;
            if (best == 0) return;
        }
    }

    static bool Used(Span<int> tokens, int k, int c) { for (int i = 0; i < k; i++) if (tokens[i] == c) return true; return false; }
}
