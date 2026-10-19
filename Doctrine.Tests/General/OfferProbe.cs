using System.Text;

namespace Doctrine.Tests;

// =====================================================================================================================================================
// EXP-OFFER-1  [LOCKED 2026-10-19, written BEFORE the first run]  The general under the roguelike offers.
//
// Question. Each round the player is offered 8 order cards (Core\Offers.cs, the real SeededOffers with the game's rules) and takes up to 4, in order.
// Does the general rank the offered cards the way the player would take them, and do (a) the tiny net and (b) last round's picks as input add anything
// over the feature table?
//
// Runner  : this file, OfferProbe.Run (gated by the environment variable DOCTRINE_PROBE=1; output file in DOCTRINE_PROBE_OUT). Fixed seeds, no tuning.
// Stream  : per seed a 60-round stream of (summary, legal list, offer) from GeneralSim.Make and SeededOffers; the player is a scripted style choosing
//           from the offer by preference, with 10% rounds of a random 1..4 distinct cards (the existing Sim's noise).
// Styles  : 0 biggest army orders (Merge>Aura>Cull>Flip>Rotate>Age, up to 4)      1 field orders on the front line (line 0 first, then the rest, up to 4)
//           2 prefers Reform the ranks (Rotates, Age, Advance orders, up to 3)     3 random (1..4 distinct offered cards)
//           4 streak (cards of the same order type as last round's first pick, then biggest army; up to 4: needs HISTORY)
//           5 situation (after a win or at the start: Advance/Hunt/Flank first, after a loss: Hold/Guard/FallBack first, then army; up to 3)
// Arms    : T taste table alone (TableOnly)   H taste table + history   N hybrid (table + net)   NH hybrid + history   O net alone
//           F frequency table with NO features (most-taken exact cards, as many as the player usually takes)   R random-offer null (3 random offered cards)
// Metrics : per round, BEFORE learning that round: EXACT = the suggestion equals the player's picks, same cards in the same order;
//           SET = same cards, any order; RECALL = share of the player's picks the suggestion contained. Averaged over the first N rounds (N = 10, 20, 40, 60),
//           over 16 seeds. Paired differences are taken per seed.
// Search width : 6 styles x 4 horizons x 7 arms, ONE pass, 16 seeds, NO constant tuned after seeing a number (alpha, margin, learning rate, passes are the
//           ones in the code as written before this run).
// Null (empirical): arm R, same streams.  Null (analytic): a size-m random guess from n=8 offered cards equals m picks exactly with 1/(8!/(8-m)!) and as
//           a set with 1/C(8,m); the mean over the stream's true m is computed from the stream and compared with arm R. They must agree within 2x else D.
// Outcomes:
//   TABLE   PASS  taste (T) beats F on SET at N=20 on styles 0,1,2 by a paired mean > 0.10 each, and beats R by > 5x on SET at N=40 on styles 0,1,2.
//           B NULL  otherwise: record it, the table's feature idea does not pay.   (style 3 is the control: T must sit within 2 SE of R on SET.)
//   NET     PASS  N beats T on EXACT averaged over styles 0..5 at N=40 by > 0.03 with the paired 95% interval (1.96 SE) above 0, and no style loses > 0.02.
//           B NULL  |difference| inside 2 SE: ship the table alone.   Worse than T by more than 2 SE: ship the table alone, say it hurts.
//           (Style 5 is the net's home ground: report it apart.)
//   HISTORY PASS  H beats T on EXACT at N=40 on style 4 by > 0.03 (paired 1.96 SE interval above 0) and loses < 0.01 on styles 0,1,2,3,5 on average.
//           otherwise the history input stays OFF.
//   D INVALID  arm R disagrees with the analytic null by more than 2x, or any arm saw a different stream than another (stream hashes), or the
//           suggestion left the offer in any round.
// Pre-run notes (from reading the runner): exact for a 4-pick player from 8 cards is rare by construction, so SET and RECALL carry the story and EXACT
//           is the hard bar; the stream is the same for every arm; a general never sees the round it is being scored on.
// =====================================================================================================================================================
public sealed class OfferProbe
{
    public const int Rounds = 60, Seeds = 16;
    static readonly int[] Horizons = [10, 20, 40, 60];
    public static readonly string[] Styles = ["biggest-army", "front-line-field", "reform-ranks", "random", "streak", "situation"];
    public static readonly string[] Arms = ["T", "H", "N", "NH", "O", "F", "R"];

    // ---- the world ------------------------------------------------------------------------------------------------------

    public sealed record Round(GeneralInput Input, int[] Offer, int[] Summary);

    public static Round[] Stream(int seed, int n)
    {
        var r = new GameRng(GameRng.Mix(seed, 9100));
        var rules = OfferRules.Default;
        var rounds = new Round[n];
        for (int i = 0; i < n; i++)
        {
            var st = GeneralSim.Make(r);
            var offer = SeededOffers.Instance.Orders(seed, i, 0, st.Legal, rules);
            rounds[i] = new Round(new GeneralInput(i, st.S, new LegalMask(st.Legal.Bits & offer.Mask.Bits)), [.. offer.Tokens], st.S);
        }
        return rounds;
    }

    static int ArmyBig(int t) => DoctrineVocabulary.Decode(t) is { Family: OrderFamily.Army } o ? o.Kind switch
    { (int)ArmyOrderKind.Merge => 5, (int)ArmyOrderKind.Aura => 4, (int)ArmyOrderKind.Cull => 3, (int)ArmyOrderKind.Flip => 2, (int)ArmyOrderKind.Rotate => 1, _ => 0 } : -1;
    static IEnumerable<int> BiggestArmy(IEnumerable<int> cards) => cards.Where(c => ArmyBig(c) >= 0).OrderByDescending(ArmyBig).ThenByDescending(c => c);
    static readonly int[] Bold = [(int)FieldOrderKind.Advance, (int)FieldOrderKind.Hunt, (int)FieldOrderKind.Flank];
    static readonly int[] Careful = [(int)FieldOrderKind.Hold, (int)FieldOrderKind.Guard, (int)FieldOrderKind.FallBack];

    public static int[] Choose(int style, Round rd, int[] prev, GameRng r)
    {
        var offer = rd.Offer;
        if (r.Next(100) < 10) return Random(offer, 1 + r.Next(4), r);      // the noise round
        IEnumerable<int> want;
        int take = 4;
        switch (style)
        {
            case 0: want = BiggestArmy(offer); break;
            case 1:
                want = offer.Where(c => DoctrineVocabulary.Decode(c).Family == OrderFamily.Field)
                            .OrderBy(c => DoctrineVocabulary.Decode(c).A == 0 ? 0 : 1).ThenBy(c => Array.IndexOf(Bold.Concat(Careful).ToArray(), DoctrineVocabulary.Decode(c).Kind)).ThenBy(c => c);
                break;
            case 2:
                take = 3;
                want = offer.Where(c => DoctrineVocabulary.Decode(c) is { Family: OrderFamily.Army, Kind: (int)ArmyOrderKind.Rotate }).OrderBy(c => c)
                    .Concat(offer.Where(c => DoctrineVocabulary.Decode(c) is { Family: OrderFamily.Army, Kind: (int)ArmyOrderKind.Age }))
                    .Concat(offer.Where(c => DoctrineVocabulary.Decode(c) is { Family: OrderFamily.Field, Kind: (int)FieldOrderKind.Advance }).OrderBy(c => c));
                break;
            case 3: return Random(offer, 1 + r.Next(4), r);
            case 4:
                {
                    var first = prev.Length > 0 && prev[0] != 0 ? prev[0] : -1;
                    var same = first < 0 ? Enumerable.Empty<int>() : offer.Where(c => c != first && Same(c, first)).OrderBy(c => c);
                    want = same.Concat(BiggestArmy(offer));
                    break;
                }
            default:
                {
                    take = 3;
                    int s13 = rd.Summary[13]; var order = (s13 == 0 || s13 == 4) ? Bold : Careful;
                    want = offer.Where(c => DoctrineVocabulary.Decode(c).Family == OrderFamily.Field && Array.IndexOf(order, DoctrineVocabulary.Decode(c).Kind) >= 0)
                                .OrderBy(c => Array.IndexOf(order, DoctrineVocabulary.Decode(c).Kind)).ThenBy(c => c)
                            .Concat(BiggestArmy(offer));
                    break;
                }
        }
        var picks = want.Distinct().Take(take).ToArray();
        return picks.Length > 0 ? picks : Random(offer, 1 + r.Next(4), r);
    }

    static bool Same(int a, int b)
    {
        var x = DoctrineVocabulary.Decode(a); var y = DoctrineVocabulary.Decode(b);
        return x.Family == y.Family && x.Kind == y.Kind;
    }

    public static int[] Random(int[] offer, int n, GameRng r)
    {
        var pool = offer.ToList(); var l = new List<int>();
        while (l.Count < n && pool.Count > 0) { int i = r.Next(pool.Count); l.Add(pool[i]); pool.RemoveAt(i); }
        return [.. l];
    }

    // ---- the arms ---------------------------------------------------------------------------------------------------------

    /// <summary>An exact-card frequency table with no features: the most taken offered cards, as many as the player usually takes.</summary>
    sealed class FrequencyNull
    {
        readonly int[] _taken = new int[DoctrineVocabulary.Size]; int _rounds, _picks;
        public int[] Suggest(int[] offer)
        {
            if (_rounds == 0) return [];
            int k = (int)Math.Round(_picks / (double)_rounds);
            return offer.Where(c => _taken[c] > 0).OrderByDescending(c => _taken[c]).ThenBy(c => c).Take(k).ToArray();
        }
        public void Learn(int[] picks) { foreach (var p in picks) _taken[p]++; _picks += picks.Length; _rounds++; }
    }

    static TinyHoloGeneral Make(string arm)
    {
        var fast = new GeneralOptions { Mode = GeneralMode.Hybrid };   // the shipped numbers: passes, rate, margin
        return arm switch
        {
            "T" => new TinyHoloGeneral(fast with { Mode = GeneralMode.TableOnly }),
            "H" => new TinyHoloGeneral(fast with { Mode = GeneralMode.TableOnly, History = true }),
            "N" => new TinyHoloGeneral(fast),
            "NH" => new TinyHoloGeneral(fast with { History = true }),
            "O" => new TinyHoloGeneral(fast with { Mode = GeneralMode.NetOnly }),
            _ => throw new ArgumentException(arm),
        };
    }

    public readonly record struct Score(double Exact, double Set, double Recall);

    static Score Judge(int[] suggested, int[] picks)
    {
        bool set = suggested.Length == picks.Length && suggested.OrderBy(x => x).SequenceEqual(picks.OrderBy(x => x));
        return new Score(suggested.SequenceEqual(picks) ? 1 : 0, set ? 1 : 0, picks.Count(suggested.Contains) / (double)picks.Length);
    }

    /// <summary>Play one player (a style) against every arm on the same stream. Returns arm -> the score of each round, predicted before that round was learned.</summary>
    public static async Task<Dictionary<string, Score[]>> Play(int style, int seed, IEnumerable<string> arms, int rounds = Rounds, Action<string>? violation = null)
    {
        var stream = Stream(seed, rounds);
        var chooser = new GameRng(GameRng.Mix(seed, 9200 + style));
        var all = new int[rounds][]; var prev = Array.Empty<int>();
        for (int i = 0; i < rounds; i++) { all[i] = Choose(style, stream[i], prev, chooser); prev = all[i]; }      // the player never sees the general
        var result = new Dictionary<string, Score[]>();
        foreach (var arm in arms)
        {
            var sc = new Score[rounds];
            if (arm == "R")
            {
                var rr = new GameRng(GameRng.Mix(seed, 9300 + style));
                for (int i = 0; i < rounds; i++) sc[i] = Judge(Random(stream[i].Offer, 3, rr), all[i]);
            }
            else if (arm == "F")
            {
                var f = new FrequencyNull();
                for (int i = 0; i < rounds; i++) { sc[i] = Judge(f.Suggest(stream[i].Offer), all[i]); f.Learn(all[i]); }
            }
            else
            {
                var g = Make(arm);
                for (int i = 0; i < rounds; i++)
                {
                    var o = await g.SuggestAsync(stream[i].Input, default);
                    var sug = o.Doctrine.Tokens.Where(t => t != 0).ToArray();
                    if (sug.Any(t => !stream[i].Offer.Contains(t))) violation?.Invoke($"{arm} style {style} seed {seed} round {i}: {o.Doctrine} left the offer");
                    sc[i] = Judge(sug, all[i]);
                    await g.LearnAsync(new GeneralExample(stream[i].Input, DoctrineSet.Of(all[i]), false), default);
                }
            }
            result[arm] = sc;
        }
        return result;
    }

    /// <summary>The analytic random null for a stream: mean over rounds of 1/(8!/(8-m)!) (exact) and 1/C(8,m) (set) for a size-3 guess when the player took m = 3, else 0.</summary>
    public static (double Exact, double Set) AnalyticRandom(int style, int seed, int rounds)
    {
        var stream = Stream(seed, rounds);
        var chooser = new GameRng(GameRng.Mix(seed, 9200 + style));
        var prev = Array.Empty<int>(); double ex = 0, st = 0;
        for (int i = 0; i < rounds; i++)
        {
            var p = Choose(style, stream[i], prev, chooser); prev = p;
            if (p.Length != 3) continue;
            int n = stream[i].Offer.Length;
            ex += 1.0 / (n * (n - 1.0) * (n - 2)); st += 6.0 / (n * (n - 1.0) * (n - 2));
        }
        return (ex / rounds, st / rounds);
    }

    // ---- the run ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Run()
    {
        if (Environment.GetEnvironmentVariable("DOCTRINE_PROBE") != "1") return;
        var arms = (Environment.GetEnvironmentVariable("DOCTRINE_PROBE_ARMS") ?? string.Join(",", Arms)).Split(',');
        var cells = new Dictionary<(string Arm, int Style, int Seed), Score[]>();
        var violations = new System.Collections.Concurrent.ConcurrentBag<string>();
        var jobs = Enumerable.Range(0, Styles.Length).SelectMany(s => Enumerable.Range(1, Seeds).Select(seed => (s, seed))).ToList();
        var gate = new object();
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (j, _) =>
        {
            var r = await Play(j.s, j.seed, arms, Rounds, violations.Add);
            lock (gate) foreach (var kv in r) cells[(kv.Key, j.s, j.seed)] = kv.Value;
        });

        var sb = new StringBuilder();
        double Mean(string arm, int style, int seed, int n, Func<Score, double> f) => cells[(arm, style, seed)].Take(n).Average(f);
        (double M, double Se) Stat(string arm, int style, int n, Func<Score, double> f, string? minus = null)
        {
            var v = Enumerable.Range(1, Seeds).Select(sd => Mean(arm, style, sd, n, f) - (minus is null ? 0 : Mean(minus, style, sd, n, f))).ToArray();
            double m = v.Average(), sd2 = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Length - 1));
            return (m, sd2 / Math.Sqrt(v.Length));
        }
        sb.AppendLine("arm\tstyle\tN\texact\tset\trecall\t(se exact)\t(se set)");
        foreach (var arm in arms) for (int s = 0; s < Styles.Length; s++) foreach (var n in Horizons)
        {
            var e = Stat(arm, s, n, x => x.Exact); var st = Stat(arm, s, n, x => x.Set); var rc = Stat(arm, s, n, x => x.Recall);
            sb.AppendLine($"{arm}\t{Styles[s]}\t{n}\t{e.M:F3}\t{st.M:F3}\t{rc.M:F3}\t{e.Se:F3}\t{st.Se:F3}");
        }
        sb.AppendLine("\npaired differences (A minus B), mean +- 1.96 se");
        void Diff(string a, string b, int n)
        {
            if (!arms.Contains(a) || !arms.Contains(b)) return;
            for (int s = 0; s < Styles.Length; s++)
            {
                var e = Stat(a, s, n, x => x.Exact, b); var st = Stat(a, s, n, x => x.Set, b);
                sb.AppendLine($"{a}-{b}\t{Styles[s]}\tN={n}\texact {e.M:+0.000;-0.000} +-{1.96 * e.Se:F3}\tset {st.M:+0.000;-0.000} +-{1.96 * st.Se:F3}");
            }
        }
        foreach (var n in new[] { 20, 40 }) { Diff("T", "F", n); Diff("T", "R", n); Diff("N", "T", n); Diff("O", "T", n); Diff("H", "T", n); Diff("NH", "N", n); }
        if (arms.Contains("R"))
            for (int s = 0; s < Styles.Length; s++)
            {
                var an = Enumerable.Range(1, Seeds).Select(sd => AnalyticRandom(s, sd, 60)).ToArray();
                var emp = Stat("R", s, 60, x => x.Exact).M; var empS = Stat("R", s, 60, x => x.Set).M;
                sb.AppendLine($"null check {Styles[s]}: analytic exact {an.Average(a => a.Exact):F5} set {an.Average(a => a.Set):F5}; empirical (size-3 guess) exact {emp:F5} set {empS:F5}");
            }
        sb.AppendLine($"violations: {violations.Count}");
        foreach (var v in violations.Take(10)) sb.AppendLine(v);
        File.WriteAllText(Environment.GetEnvironmentVariable("DOCTRINE_PROBE_OUT") ?? Path.Combine(Path.GetTempPath(), "offer-probe.tsv"), sb.ToString());
        Assert.Empty(violations);
    }
}
