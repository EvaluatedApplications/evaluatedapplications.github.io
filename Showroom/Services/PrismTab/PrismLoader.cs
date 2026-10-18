using System.Text;
using HoloKernel;
using PrismFormer;

namespace Showroom.Services.PrismTab;

/// <summary>The one loaded Prism checkpoint and the facts every tab reads off it.</summary>
public sealed record PrismModel(HoloSession Session, SubwordVocab Vocab, ModelStats Stats, long? Rounds, int? TrainedStackK, int? TrainedIterWarm);

/// <summary>One narrated line of the model download (a real fetch each).</summary>
public sealed class PrismLoadStep { public string Label = ""; public string Status = "fetching…"; public string Cls = ""; }

/// <summary>
/// Loads Prism's checkpoint ONCE per page load and hands the same <see cref="PrismModel"/> to every tab. Chat, Stories, Map,
/// Creature and Forecaster all read the shared <see cref="SessionHost"/> session under <see cref="PrismCheckpoint.SessionKey"/>;
/// this class only adds the sidecar files (vocabulary, rounds, K, ramp) and the narrated boot log. Nothing is fetched until
/// <see cref="EnsureAsync"/> is called, which the Prism page does when a tab is first shown.
///
/// It also owns the one-trainer-at-a-time lock: a single-threaded tab must never run two training loops.
/// </summary>
public sealed class PrismLoader
{
    readonly HttpClient _http;
    readonly SessionHost _sessions;
    Task<PrismModel>? _load;
    object? _trainer;

    public PrismLoader(HttpClient http, SessionHost sessions) { _http = http; _sessions = sessions; }

    public List<PrismLoadStep> Steps { get; } = new();
    public event Action? Changed;
    public PrismModel? Loaded { get; private set; }

    PrismLoadStep Begin(string label) { var s = new PrismLoadStep { Label = label }; Steps.Add(s); Changed?.Invoke(); return s; }
    void Finish(PrismLoadStep s, string status, string cls = "core") { s.Status = status; s.Cls = cls; Changed?.Invoke(); }

    public Task<PrismModel> EnsureAsync()
    {
        if (_load is { IsFaulted: false }) return _load;
        return _load = LoadAsync();
    }

    async Task<PrismModel> LoadAsync()
    {
        var s = Begin(DataUrl.For("prism/oracle-vocab.txt"));
        var vocabBytes = await _http.GetByteArrayAsync(DataUrl.For("prism/oracle-vocab.txt"));
        var merges = Encoding.UTF8.GetString(vocabBytes).Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        var vocab = new SubwordVocab(merges);   // the checkpoint's OWN vocabulary, not the studio's default table
        Finish(s, $"{vocabBytes.Length:N0} B → {vocab.Size:N0}-symbol vocab");

        // The sidecars are tiny and are needed up front: the SessionHost factory runs once per key, so K and the ramp must be known.
        long? rounds = null; int? stackK = null, iterWarm = null;
        s = Begin(DataUrl.For("prism/oracle-rounds.txt"));
        try { rounds = long.Parse((await _http.GetStringAsync(DataUrl.For("prism/oracle-rounds.txt"))).Trim()); Finish(s, $"{rounds:N0} rounds trained"); }
        catch { Finish(s, "not shipped (optional)", "fail"); }
        s = Begin(DataUrl.For("prism/oracle-stackk.txt"));
        try { stackK = int.Parse((await _http.GetStringAsync(DataUrl.For("prism/oracle-stackk.txt"))).Trim()); Finish(s, $"K={stackK}"); }
        catch { Finish(s, "not shipped (optional)", "fail"); }
        s = Begin(DataUrl.For("prism/oracle-iterwarm.txt"));
        try { iterWarm = int.Parse((await _http.GetStringAsync(DataUrl.For("prism/oracle-iterwarm.txt"))).Trim()); Finish(s, $"{iterWarm:N0}-round ramp"); }
        catch { Finish(s, "not shipped (optional)", "fail"); }

        int k = PrismCheckpoint.ResolveK(stackK);

        // SessionHost dedupes by MODEL key: whichever tool (or the Analyst's novelty scan) loaded "prism" first paid the download.
        bool reused = _sessions.IsLoaded(PrismCheckpoint.SessionKey);
        var session = await _sessions.GetOrCreateAsync(PrismCheckpoint.SessionKey, async () =>
        {
            var sb = Begin(DataUrl.For("prism/oracle-brain.bin.gz"));
            var (bytes, gzLen) = await CheckpointFetch.FetchAndDecompressGzipAsync(_http, DataUrl.For("prism/oracle-brain.bin.gz"));
            var peek = HoloFormer.Deserialize(bytes);   // layers + params for the log and the alpha guard; FromCheckpoint needs K/alpha up front
            Finish(sb, $"{gzLen:N0} B gzip → {bytes.Length:N0} B → {peek.ParamCount:N0} params deserialized");
            return HoloSession.FromCheckpoint(bytes, k, PrismCheckpoint.ReconstructAlpha(peek.Layers, rounds, iterWarm));
        });
        if (reused) Finish(Begin(DataUrl.For("prism/oracle-brain.bin.gz")), "reused from earlier this visit (no re-download)");

        Loaded = new PrismModel(session, vocab, session.Stats(), rounds, stackK, iterWarm);
        Changed?.Invoke();
        return Loaded;
    }

    /// <summary>True if <paramref name="owner"/> now holds the trainer lock; false if another trainer is running.</summary>
    public bool TryBeginTraining(object owner)
    {
        if (_trainer is not null && !ReferenceEquals(_trainer, owner)) return false;
        _trainer = owner;
        return true;
    }

    public void EndTraining(object owner) { if (ReferenceEquals(_trainer, owner)) _trainer = null; }

    public bool TrainingActive => _trainer is not null;
}
