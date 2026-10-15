using HelixDemo;

namespace Showroom.Services;

/// <summary>Everything the Helix page shows: the loaded data, the options, the stages as they run and the visitor's own timings.
/// The page only renders this. All the science is HelixAnalysis (the embedded file); this class only sequences it and keeps state.</summary>
public sealed class HelixSession
{
    public static readonly int[] DemoSizes = { 300, 1000, 3000 };
    public static readonly int[] FitSizes = { 500, 1000, 2000, 4000 };
    public const int FitIterations = 5000;                  // the package default, shown on the page
    public const int MaxTextChars = 2_000_000;

    public Func<Task> Repaint = () => Task.CompletedTask;
    public Dataset? Data;
    public bool IsDemo;
    public string Label = "";
    public string PastedText = "";
    public int DemoRows = 1000;
    public double ParseMs;

    // track
    public int Row;
    public TrackResult? Track;

    // grading options
    public BlockMode Mode = BlockMode.Rows;
    public int BlockSize = 7;
    public int FitRows = 1000;
    public bool FitControl = true;

    // run state
    public bool Running, Cancelled;
    public string? Error;
    public IReadOnlyList<Stage> Stages = [];
    public readonly List<StageResult> Results = new();
    public Stage? Current;
    public double TotalMs;
    CancellationTokenSource? _cts;

    public bool HasMeasurement => Data?.Measurements is not null && Data.Sequences.Count >= HelixAnalysis.MinRows;
    public RunOptions Options => new(Mode, BlockSize, FitRows, FitIterations, FitControl);
    public StageResult? Result(string id) => Results.FirstOrDefault(r => r.Id == id);
    public bool Finished => !Running && Results.Count > 0 && Results.Count == Stages.Count;

    public void Fail(string message) { Clear(); Error = message; }

    public void LoadDemo()
    {
        var t = System.Diagnostics.Stopwatch.GetTimestamp();
        Load(HelixDemoData.Make(DemoRows), demo: true, $"Synthetic demo, {DemoRows:N0} rows");
        ParseMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
    }

    public void LoadText(string text, string label)
    {
        if (text.Length > MaxTextChars) { Clear(); Error = $"That is {text.Length:N0} characters; the limit is {MaxTextChars:N0}."; return; }
        var t = System.Diagnostics.Stopwatch.GetTimestamp();
        var d = HelixAnalysis.Parse(text);
        ParseMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        if (d.Sequences.Count == 0) { Clear(); Error = "No usable sequences were found. " + string.Join("; ", d.Notes); return; }
        Load(d, demo: false, label);
    }

    void Load(Dataset d, bool demo, string label)
    {
        Clear();
        Data = d; IsDemo = demo; Label = label;
        SelectRow(0);
    }

    void Clear()
    {
        _cts?.Cancel();
        Data = null; Track = null; Error = null; Stages = []; Results.Clear(); Current = null; Cancelled = false; Running = false;
    }

    public void SelectRow(int row)
    {
        if (Data is null) return;
        Row = Math.Clamp(row, 0, Data.Sequences.Count - 1);
        Track = HelixAnalysis.Track(Data.Sequences[Row]);
    }

    public void Cancel() => _cts?.Cancel();

    public async Task RunAsync()
    {
        if (Data is null || Running) return;
        Error = null; Cancelled = false; Results.Clear(); Current = null; TotalMs = 0;
        try
        {
            if (!HasMeasurement) throw new InvalidOperationException($"Grading needs a measurement on every row and at least {HelixAnalysis.MinRows} rows.");
            int minLen = Data.Sequences.Min(s => s.Length);
            if (Mode == BlockMode.Tiled && (BlockSize < 1 || BlockSize >= minLen)) throw new InvalidOperationException($"The window step must be between 1 and {minLen - 1} bases (the shortest sequence has {minLen}).");
            if (Mode == BlockMode.Segments && BlockSize < 1) throw new InvalidOperationException("A run of rows needs at least 1 row.");
            Stages = HelixAnalysis.Plan(Data, Options).ToList();     // nothing has run yet
        }
        catch (Exception e) { Error = e.Message; Stages = []; await Repaint(); return; }

        _cts = new CancellationTokenSource();
        Running = true;
        try
        {
            foreach (var stage in Stages)
            {
                if (_cts.IsCancellationRequested) { Cancelled = true; break; }
                Current = stage;
                await Repaint();                              // paint "running" before the synchronous stage starts
                var r = stage.Run();
                Results.Add(r); TotalMs += r.Ms;
            }
        }
        catch (OutOfMemoryException) { Error = "The browser ran out of memory. Try fewer rows."; }
        catch (Exception e) { Error = e.Message; }
        finally { Running = false; Current = null; }
        await Repaint();
    }
}
