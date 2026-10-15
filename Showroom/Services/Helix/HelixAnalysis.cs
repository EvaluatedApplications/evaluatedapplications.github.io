// This exact file runs in the browser (Pages/Helix.razor) and is shown to the visitor as the file a console app copies (it is embedded in
// the assembly as a resource, see Showroom.csproj). It uses only the published HoloGenome package and the base library. The page shows
// each region below (marked >>> name / <<< name) beside the result it produced, so the code on screen is the code that ran.
using System.Diagnostics;
using System.Globalization;
using HoloGenome;

namespace HelixDemo;

public enum BlockMode { Rows, Tiled, Segments }

/// <summary>Sequences (upper-case A/C/G/T), an optional measurement column, and one line for everything that was left out.</summary>
public sealed record Dataset(IReadOnlyList<string> Ids, IReadOnlyList<string> Sequences, IReadOnlyList<double>? Measurements, IReadOnlyList<string> Notes);

public sealed record TrackResult(double[] Phase, double[] FlatPhase, double Turns, double FlatTurns, double BasesPerTurn, int HologramReals, double Ms);

public sealed record RunOptions(BlockMode Mode, int BlockSize, int FitRows, int FitIterations, bool FitControl);

public sealed record StageResult(string Id, string Label, double Ms, ScoreGrade? Grade = null, PhasorPeriodModel? Model = null, string Note = "");

/// <summary>One step of the run; <see cref="Run"/> does the work when called, so a caller can paint "running" first and stop between steps.</summary>
public sealed record Stage(string Id, string Label, Func<StageResult> Run);

public static class HelixAnalysis
{
    public const int MaxRows = 5_000;            // rows read from the input
    public const int MaxLength = 1_000;          // bases per sequence
    public const int MinRows = 30;               // fewer rows cannot support a train and a held-out part
    public const int Resamples = 1_000;          // bootstrap resamples behind every interval
    public const int GradeSeed = 20261002;       // the package default, written out so it is visible
    public const double ControlShort = 7.0;      // off-period controls, bases per turn
    public const double ControlLong = 15.0;
    public const int ShuffleSeed = 7;
    public const double FitStartPeriod = 12.0;   // neutral start, neither the Olson value nor a control
    public const int MaxFitBases = 200_000;      // training bases the fit may use (its cost is bases x iterations)
    public const int HeldMax = 2_000;            // held-out rows graded after the fit

    // >>> parse
    /// <summary>FASTA (">id [measurement]" then the sequence lines) or one sequence per line ("SEQ" or "SEQ,measurement").</summary>
    public static Dataset Parse(string text)
    {
        var ids = new List<string>(); var seqs = new List<string>(); var ys = new List<double>(); var notes = new List<string>();
        int seen = 0, withY = 0, skipped = 0, ignored = 0;

        void Add(string id, string raw, double? y)
        {
            seen++;
            string s = raw.ToUpperInvariant();
            char bad = s.FirstOrDefault(c => Dna.Index(c) < 0);
            string? why = s.Length == 0 ? "empty" : s.Length > MaxLength ? $"longer than {MaxLength} bases" : bad != default ? $"base '{bad}' is not A, C, G or T" : null;
            if (why is not null) { if (++skipped <= 3) notes.Add($"record {seen} skipped: {why}"); return; }
            if (seqs.Count >= MaxRows) { ignored++; return; }
            ids.Add(id); seqs.Add(s);
            if (y is { } v) { ys.Add(v); withY++; }
        }

        string[] lines = text.Split('\n');
        if (text.TrimStart().StartsWith('>'))
        {
            string? id = null; double? y = null; var body = new System.Text.StringBuilder();
            foreach (string raw in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (raw[0] != '>') { body.Append(raw); continue; }
                if (id is not null) Add(id, body.ToString(), y);
                body.Clear();
                var tok = raw[1..].Split([' ', '\t', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries);
                id = tok.Length > 0 ? tok[0] : $"record{seen + 1}";
                y = tok.Length > 1 ? Number(tok[^1]) : null;      // the last word of the header, when it is a number
            }
            if (id is not null) Add(id, body.ToString(), y);
        }
        else
        {
            foreach (string raw in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var tok = raw.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
                Add($"row{seen + 1}", tok[0], tok.Length > 1 ? Number(tok[1]) : null);
            }
        }

        if (skipped > 3) notes.Add($"{skipped - 3} more records skipped");
        if (ignored > 0) notes.Add($"{ignored} records beyond the first {MaxRows} were ignored");
        IReadOnlyList<double>? measurements = null;
        if (withY == seqs.Count && seqs.Count > 0) measurements = ys;
        else if (withY > 0) notes.Add($"measurements on {withY} of {seqs.Count} rows: the measurement column was ignored");
        return new Dataset(ids, seqs, measurements, notes);
    }

    static double? Number(string t) => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    // <<< parse

    // >>> track
    static readonly GenomeEncoder OlsonEncoder = new(new GenomeEncoderOptions { Dim = 512, Twist = OlsonTwist.Instance });
    static readonly GenomeEncoder FlatEncoder = new(new GenomeEncoderOptions { Dim = 512, Twist = new ConstantTwist(ConstantTwist.BDnaDegrees) });

    public static TrackResult Track(string sequence)
    {
        long t = Stopwatch.GetTimestamp();
        double[] phase = OlsonEncoder.HelixPhases(sequence);        // cumulative helix angle at each base, radians
        double[] flat = FlatEncoder.HelixPhases(sequence);          // the same with one constant twist (34.3 degrees) at every step
        double[] hologram = OlsonEncoder.Encode(sequence);          // the fixed-width vector the readouts use
        double turns = phase[^1] / (2 * Math.PI), flatTurns = flat[^1] / (2 * Math.PI);
        double basesPerTurn = turns > 0 ? (sequence.Length - 1) / turns : double.NaN;
        return new(phase, flat, turns, flatTurns, basesPerTurn, hologram.Length, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }
    // <<< track

    // >>> blocks
    public static IReadOnlyList<int[]> Blocks(IReadOnlyList<string> seqs, BlockMode mode, int size) => mode switch
    {
        BlockMode.Tiled => ResampleBlocks.Tiled(seqs, size, seqs.Min(s => s.Length) - size),   // windows slid by `size` bases
        BlockMode.Segments => ResampleBlocks.Segments(seqs.Count, size),                        // consecutive runs of `size` rows
        _ => ResampleBlocks.Rows(seqs.Count),                                                   // every row independent
    };
    // <<< blocks

    // >>> helical
    static GradeOptions Options(IReadOnlyList<int[]> blocks) => new() { Blocks = blocks, Resamples = Resamples, Seed = GradeSeed };

    /// <summary>The zero-parameter helical score against the measurement. period null = the Olson period; nothing is fitted.</summary>
    public static StageResult Helical(string id, string label, IReadOnlyList<string> seqs, IReadOnlyList<double> y, double? period, IReadOnlyList<int[]> blocks)
    {
        long t = Stopwatch.GetTimestamp();
        ScoreGrade g = GenomeScorer.GradeHelical(seqs, y, period, Options(blocks));
        return new(id, label, Stopwatch.GetElapsedTime(t).TotalMilliseconds, g);
    }
    // <<< helical

    // >>> order-null
    /// <summary>Same score on dinucleotide-shuffled sequences: same step counts, order destroyed.</summary>
    public static StageResult OrderNull(string id, string label, IReadOnlyList<string> seqs, IReadOnlyList<double> y, IReadOnlyList<int[]> blocks)
    {
        long t = Stopwatch.GetTimestamp();
        var rng = new Random(ShuffleSeed);
        List<string> shuffled = seqs.Select(s => DinucleotideShuffle.Shuffle(s, rng)).ToList();
        ScoreGrade g = GenomeScorer.GradeHelical(shuffled, y, null, Options(blocks));
        return new(id, label, Stopwatch.GetElapsedTime(t).TotalMilliseconds, g);
    }
    // <<< order-null

    // >>> fit
    /// <summary>Fits the phasor model with the period free, starting at 12 bases per turn.</summary>
    public static StageResult Fit(string id, string label, IReadOnlyList<string> seqs, IReadOnlyList<double> y, int iterations)
    {
        long t = Stopwatch.GetTimestamp();
        var options = new PhasorFitOptions { Iterations = iterations, InitialPeriod = FitStartPeriod, LogEvery = Math.Max(1, iterations / 10) };
        PhasorPeriodModel model = PhasorPeriodModel.Fit(seqs, y, options);
        return new(id, label, Stopwatch.GetElapsedTime(t).TotalMilliseconds, Model: model, Note: $"{seqs.Count} rows, {seqs.Sum(s => s.Length)} bases, {iterations} iterations");
    }

    /// <summary>The fitted model's score on rows it was not fitted on.</summary>
    public static StageResult HeldOut(string id, string label, PhasorPeriodModel model, IReadOnlyList<string> seqs, IReadOnlyList<double> y, IReadOnlyList<int[]> blocks)
    {
        long t = Stopwatch.GetTimestamp();
        ScoreGrade g = ScoreGrader.Grade(model.Scores(seqs), y, Options(blocks));
        return new(id, label, Stopwatch.GetElapsedTime(t).TotalMilliseconds, g);
    }
    // <<< fit

    // >>> plan
    /// <summary>Training rows for the fit: the first rows, at most two thirds of the data, at most MaxFitBases bases.</summary>
    public static int TrainCount(IReadOnlyList<string> seqs, int wanted)
    {
        int cap = Math.Min(wanted, (2 * seqs.Count + 2) / 3), bases = 0, n = 0;
        while (n < cap && bases + seqs[n].Length <= MaxFitBases) bases += seqs[n++].Length;
        return n;
    }

    /// <summary>Every step of the run, in order. Nothing runs until the caller calls <see cref="Stage.Run"/>.</summary>
    public static IEnumerable<Stage> Plan(Dataset d, RunOptions o)
    {
        IReadOnlyList<string> seqs = d.Sequences;
        IReadOnlyList<double> y = d.Measurements ?? throw new ArgumentException("the input has no measurement column");
        if (seqs.Count < MinRows) throw new ArgumentException($"need at least {MinRows} rows with a measurement");
        IReadOnlyList<int[]> blocks = Blocks(seqs, o.Mode, o.BlockSize);

        yield return new("helical", $"Helical score at the Olson period, {HelicalScore.OlsonPeriodBasesPerTurn:F4} bases per turn", () => Helical("helical", "Olson period", seqs, y, null, blocks));
        yield return new("control7", "Control: the same score at 7 bases per turn", () => Helical("control7", "7 bases per turn", seqs, y, ControlShort, blocks));
        yield return new("control15", "Control: the same score at 15 bases per turn", () => Helical("control15", "15 bases per turn", seqs, y, ControlLong, blocks));
        yield return new("order-null", "Control: sequences shuffled, step counts kept", () => OrderNull("order-null", "Order destroyed", seqs, y, blocks));

        int train = TrainCount(seqs, o.FitRows), held = Math.Min(HeldMax, seqs.Count - train);
        var trainSeqs = seqs.Take(train).ToList(); var trainY = y.Take(train).ToList();
        var heldSeqs = seqs.Skip(train).Take(held).ToList(); var heldY = y.Skip(train).Take(held).ToList();
        PhasorPeriodModel? model = null;
        yield return new("fit", $"Fit the phasor model with a free period on {train} rows", () => { var r = Fit("fit", "Fitted period", trainSeqs, trainY, o.FitIterations); model = r.Model; return r; });
        yield return new("heldout", $"Score the fitted model on the next {held} rows", () => HeldOut("heldout", "Held-out rows", model!, heldSeqs, heldY, Blocks(heldSeqs, o.Mode, o.BlockSize)));
        if (o.FitControl)
        {
            var rng = new Random(ShuffleSeed);
            yield return new("fit-shuffled", "Control: fit again on shuffled sequences", () =>
                Fit("fit-shuffled", "Fitted period, order destroyed", trainSeqs.Select(s => DinucleotideShuffle.Shuffle(s, rng)).ToList(), trainY, o.FitIterations));
        }
    }
    // <<< plan
}
