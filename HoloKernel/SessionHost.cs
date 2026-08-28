namespace HoloKernel;

/// <summary>
/// Holds the loaded model(s) for the lifetime of a page load, so navigating between tools reuses
/// what is already in memory instead of rebuilding or re-downloading it.
///
/// EPHEMERAL BY DESIGN. There is no persistence here and none is wanted: a page reload drops
/// everything, including any refinement a visitor did. That is the intended behaviour, not a
/// limitation to engineer around — no local storage, no save-back, no cross-session sync. If a
/// "keep my progress" feature is ever actually wanted, it is a new decision, not something to
/// pre-build for.
///
/// Register once in the Blazor host:
/// <code>builder.Services.AddSingleton&lt;SessionHost&gt;();</code>
/// In Blazor WebAssembly a singleton's lifetime IS the page load, so the DI lifetime and the
/// intended ephemerality are the same thing — nothing extra to enforce.
///
/// The single job worth doing carefully is load de-duplication: concurrent callers await the SAME
/// load rather than each starting their own. Prism's checkpoint is ~2.9 MB, so a double fetch is a
/// real cost on a phone, and two tools racing on first navigation is an ordinary case, not an
/// exotic one.
/// </summary>
public sealed class SessionHost
{
    private readonly Dictionary<string, Lazy<Task<HoloSession>>> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Get the session for <paramref name="key"/>, creating it via <paramref name="factory"/> only
    /// the first time. Every later caller — including one that arrives while the first load is still
    /// in flight — awaits that same load.
    ///
    /// <paramref name="key"/> identifies a MODEL, not a tool. Tools that genuinely share a model
    /// (same checkpoint, same shape) should pass the same key and will share one instance.
    /// </summary>
    public Task<HoloSession> GetOrCreateAsync(string key, Func<Task<HoloSession>> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sessions.TryGetValue(key, out var lazy))
        {
            // ExecutionAndPublication: the factory runs at most once. The value is a Task, so
            // callers await rather than block — safe on a single-threaded WASM host, where blocking
            // on your own continuation is the one thing that must never happen.
            lazy = new Lazy<Task<HoloSession>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
            _sessions[key] = lazy;
        }

        return lazy.Value;
    }

    /// <summary>True once the model for <paramref name="key"/> has finished loading successfully.</summary>
    public bool IsLoaded(string key) =>
        _sessions.TryGetValue(key, out var lazy)
        && lazy.IsValueCreated
        && lazy.Value is { IsCompletedSuccessfully: true };

    /// <summary>
    /// Drop a session so the next request rebuilds it — what a "reset brain" control does.
    /// Discards any in-memory refinement, which is the whole point of such a control.
    /// </summary>
    public void Forget(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _sessions.Remove(key);
    }

    /// <summary>Drop everything. Equivalent to what a page reload does anyway.</summary>
    public void Clear() => _sessions.Clear();

    /// <summary>Keys currently held, loaded or still loading.</summary>
    public IReadOnlyCollection<string> Keys => _sessions.Keys.ToList();
}
