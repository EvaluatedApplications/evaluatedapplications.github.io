namespace Showroom.Services.PrismTab;

/// <summary>One hourly candle of the bundled tape (unix seconds, open, high, low, close).</summary>
public sealed record Candle(long T, double O, double H, double L, double C);

/// <summary>
/// The Forecaster written as text. Each hourly close-to-close move becomes a word (up, down, flat), and the model is shown
/// the last 16 words (the window is the visitor's setting) and asked for the next one, scored by next-token logits over " up" and " down":
///
///     aapl hourly moves: up up down flat up ... then:   ->  " up" / " down"
///
/// The edges match the old Forecaster's flat bucket: a move of at least +0.003 percent is up, below -0.003 percent is
/// down, anything between is flat. Flat targets are left out of scoring and training (about 0.7 percent of the hours, and
/// there is no one-token label for them); flat words still appear inside the context line.
/// The prompt format is the one chosen on the dev set in the 2026-10-18 probe (format F3).
/// The tape is split in time: the first 80 percent is the training region, the latest 20 percent is held out and is
/// the only place scored items come from; a scored hour's whole prompt lies inside the held-out part.
/// </summary>
public sealed class ForecasterTask : ITextTask
{
    public const int DefaultWindow = 16, MinWindow = 2, MaxWindow = 40;
    public const double FlatEdge = 0.00003;
    static readonly string[] LabelTexts = { " up", " down" };

    /// <summary>How many hours of history the prompt shows (the visitor sets it).</summary>
    public int Window { get; }
    readonly int[] _dir;       // 1 up, -1 down, 0 flat; _dir[j] is the move from candle j-1 to candle j (_dir[0] = 0)
    readonly int _cut;         // first held-out index
    readonly int[] _heldOut;   // scoreable targets (non-flat) whose full prompt lies in the held-out part
    readonly int[] _trainable; // scoreable-for-training targets in the training region

    public IReadOnlyList<string> Labels => LabelTexts;
    public string BaselineName => "always guessing the commoner direction";

    public IReadOnlyList<Candle> Candles { get; }
    public int Cut => _cut;
    public int HeldOutCount => _heldOut.Length;

    public ForecasterTask(IReadOnlyList<Candle> candles, int window = DefaultWindow)
    {
        Window = Math.Clamp(window, MinWindow, MaxWindow);
        Candles = candles;
        int n = candles.Count;
        if (n < 400) throw new ArgumentException("The tape is too short to split.", nameof(candles));
        _dir = new int[n];
        for (int j = 1; j < n; j++)
        {
            double r = candles[j].C / candles[j - 1].C - 1;
            _dir[j] = r >= FlatEdge ? 1 : r < -FlatEdge ? -1 : 0;
        }
        _cut = (int)(n * 0.8);
        // 32 hours of margin after the cut (as in the probe) keeps every scored prompt and target wholly in the held-out part.
        int margin = Math.Max(32, Window);
        _heldOut = Enumerable.Range(_cut + margin, n - _cut - margin).Where(t => _dir[t] != 0).ToArray();
        _trainable = Enumerable.Range(64, _cut - 64).Where(t => _dir[t] != 0).ToArray();
    }

    public int Direction(int index) => _dir[index];

    /// <summary>The words of the 16 hours before <paramref name="target"/>, oldest first.</summary>
    public string[] Words(int target)
    {
        var w = new string[Window];
        for (int k = 0; k < Window; k++)
        {
            int d = _dir[target - Window + k];
            w[k] = d > 0 ? "up" : d < 0 ? "down" : "flat";
        }
        return w;
    }

    public string Prompt(int target) => "aapl hourly moves: " + string.Join(" ", Words(target)) + " then:";

    public TaskItem ItemFor(int target)
    {
        var good = new bool[2];
        good[_dir[target] > 0 ? 0 : 1] = true;
        return new TaskItem(Prompt(target), new[] { true, true }, good);
    }

    /// <summary>Every held-out target index (non-flat), for the "guess the next hour" picker.</summary>
    public IReadOnlyList<int> HeldOut => _heldOut;

    public TaskItem[] MakeEvalSet(int n)
    {
        // Evenly spread over the held-out part, so the set covers the whole held-out stretch, not one week of it.
        n = Math.Min(n, _heldOut.Length);
        var items = new TaskItem[n];
        for (int i = 0; i < n; i++) items[i] = ItemFor(_heldOut[(int)((long)i * _heldOut.Length / n)]);
        return items;
    }

    public TaskItem NextTrainItem(Random r) => ItemFor(_trainable[r.Next(_trainable.Length)]);

    /// <summary>The target index behind an eval/train prompt is not kept on the item; tests use this to check the split.</summary>
    public int[] EvalTargets(int n)
    {
        n = Math.Min(n, _heldOut.Length);
        var t = new int[n];
        for (int i = 0; i < n; i++) t[i] = _heldOut[(int)((long)i * _heldOut.Length / n)];
        return t;
    }

    public int[] TrainTargets => _trainable;

    public double Baseline(IReadOnlyList<TaskItem> items)
    {
        if (items.Count == 0) return 0;
        int up = items.Count(i => i.Good[0]);
        return Math.Max(up, items.Count - up) / (double)items.Count;
    }
}
