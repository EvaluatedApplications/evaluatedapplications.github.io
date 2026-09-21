using System.Globalization;
using Microsoft.JSInterop;

namespace HoloKernel;

/// <summary>
/// Prism's own voice, factored out of <c>Prism.razor</c> (2026-09-21) so a second tool (Nano
/// Stories) can share the EXACT sound and reveal cadence rather than re-derive it. This is the
/// whole reason the type exists: the premise of both tools is "the same model," and a visitor
/// moving between them should hear the same thing. A second hand-copy of this file is exactly the
/// drift that premise cannot survive — extend this type, never paste it.
///
/// Every JS call goes through the SAME <c>window.prismAudio</c> object <c>index.html</c> already
/// defines globally (kept under that name deliberately — it is genuinely the model's one voice,
/// not a Prism-page-local thing, and both consumers of this type share the one AudioContext it
/// creates lazily on first real use).
///
/// STANDING RULES, carried over unchanged from Prism — do not violate either even where it looks
/// like an improvement:
///   1. Never snap a partial's frequency to a musical scale. <see cref="PlayTone"/>'s pitch/timbre
///      come straight off the token's own phasor face; that mapping is the point, and quantising it
///      to notes was explicitly rejected once already (see PlayTone's own doc for the measured
///      reason: an earlier version DID impose a harmonic series by accident, and the fix was to
///      stop doing that, not to trade one imposed structure for another).
///   2. The tuning below (pitch window, partial spread, cadence, ARCADE CRUSH in the JS side) was
///      arrived at over many rounds of listening on Prism and is the voice, not a default — do not
///      retune it per consumer. A new tool gets the SAME voice, not its own.
/// </summary>
public sealed class TokenVoice : IDisposable
{
    readonly IJSRuntime _js;

    public TokenVoice(IJSRuntime js) => _js = js;

    public bool SoundOn { get; private set; }
    public double Volume { get; private set; } = 0.6;

    // "clamp the master gain low by default" — even a visitor dragging the slider to 100% only ever
    // reaches this genuinely quiet ceiling.
    const double VolumeCeiling = 0.32;

    // Note length ~1.4x the base beat, so each note is still sounding as the next one glides in.
    const int ToneMs = 300;

    // Sound on: one note per token, paced to a beat instead of as fast as compute allows. The wait is
    // measured from the previous note (see WaitForBeatAsync), so compute time is absorbed into the
    // beat rather than added on top; a step slower than the beat plays as soon as it's ready instead
    // of catching up. Sound OFF (the default) is not paced at all.
    const int NoteIntervalMs = 210;

    // SWAY: each beat is the base stretched or squeezed by a slow wander plus a little jitter, like a
    // player pushing and pulling the tempo rather than a metronome. A sine over ~16 notes (+-14%)
    // carries the phrase-level push and pull, and +-6% uniform jitter keeps neighbouring notes from
    // sounding quantised; every beat stays 0.8x-1.2x base.
    double _swayPhase;
    readonly Random _swayRng = new();

    System.Diagnostics.Stopwatch? _beat;
    long _nextBeat;

    const string SoundOnKey = "prism-sound-on", SoundVolKey = "prism-sound-volume";
    // Deliberately the SAME localStorage keys Prism has always used, not a per-tool copy: a visitor
    // who mutes the voice on one tool stays muted on the other, and turning the volume down carries
    // over too — consistent with "it's the same voice," not two independent preferences to manage.

    /// <summary>Restore the persisted mute/volume preference. Cheap and safe to call from a page's
    /// own <c>OnInitializedAsync</c> — reading localStorage is not itself a user gesture, so even a
    /// restored <c>SoundOn=true</c> may still need the AudioContext unlocked by a real click later
    /// (see <see cref="ToggleAsync"/>); this never blocks page load.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            SoundOn = await _js.InvokeAsync<string>("prismAudio.getPref", SoundOnKey, "0") == "1";
            var volPref = await _js.InvokeAsync<string>("prismAudio.getPref", SoundVolKey, "0.6");
            if (double.TryParse(volPref, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                Volume = Math.Clamp(v, 0, 1);
            if (SoundOn) await _js.InvokeVoidAsync("prismAudio.setVolume", Volume * VolumeCeiling);
        }
        catch { SoundOn = false; }
    }

    /// <summary>Flip mute. Call this from the toggle button's OWN click handler — that click IS the
    /// user gesture browsers require before audio can play; unlocking anywhere else (e.g. quietly
    /// from a restored preference) would just leave the context suspended.</summary>
    public async Task ToggleAsync()
    {
        SoundOn = !SoundOn;
        try
        {
            if (SoundOn)
            {
                await _js.InvokeVoidAsync("prismAudio.unlock");
                await _js.InvokeVoidAsync("prismAudio.setVolume", Volume * VolumeCeiling);
            }
            else
            {
                await _js.InvokeVoidAsync("prismAudio.stopAll");
            }
            await _js.InvokeVoidAsync("prismAudio.setPref", SoundOnKey, SoundOn ? "1" : "0");
        }
        catch { /* audio is decoration, never let it fail the page */ }
    }

    public async Task SetVolumeAsync(double v)
    {
        Volume = Math.Clamp(v, 0, 1);
        try
        {
            await _js.InvokeVoidAsync("prismAudio.setVolume", Volume * VolumeCeiling);
            await _js.InvokeVoidAsync("prismAudio.setPref", SoundVolKey, Volume.ToString(CultureInfo.InvariantCulture));
        }
        catch { /* audio is decoration, never let it fail the page */ }
    }

    int NextBeatMs()
    {
        _swayPhase += 2 * Math.PI / 16.0;
        var factor = 1.0 + 0.14 * Math.Sin(_swayPhase) + 0.06 * (_swayRng.NextDouble() * 2 - 1);
        return (int)Math.Round(NoteIntervalMs * factor);
    }

    /// <summary>Start a fresh pacing beat for a new generation (one call, one story/reply) so cadence
    /// timing never carries over from an earlier run — call once before the first token.</summary>
    public void ResetBeat() { _beat = System.Diagnostics.Stopwatch.StartNew(); _nextBeat = 0; }

    /// <summary>Wait for the next swayed beat, then reserve the beat after it. Only meaningful while
    /// <see cref="SoundOn"/> — callers should guard the call with that check themselves so an
    /// unpaced (sound-off) run never even reads the stopwatch.</summary>
    public async Task WaitForBeatAsync()
    {
        if (_beat is null) ResetBeat();
        var wait = _nextBeat - _beat!.ElapsedMilliseconds;
        if (wait > 0) await Task.Delay((int)wait);
        _nextBeat = Math.Max(_nextBeat, _beat.ElapsedMilliseconds) + NextBeatMs();
    }

    // Genuinely live streaming: one real forward-pass step per token, revealed immediately, no
    // artificial pacer unless sound is on. The render is BATCHED (not the compute) so a long,
    // sound-off run doesn't re-diff the DOM on every single token: repaint on the stopping/final
    // token, every token while sound is on (so text lands on its note), or every RenderBatch tokens
    // otherwise.
    public const int RenderBatch = 4;

    public static bool ShouldRepaint(int revealedCount, bool stopping, bool isLastStep, bool soundOn) =>
        stopping || isLastStep || soundOn || revealedCount % RenderBatch == 0;

    /// <summary>
    /// Play the tone for one just-revealed token, straight from that token's own phasor face — no
    /// forward pass, deterministic, the same face every time this id is ever emitted on this
    /// checkpoint. A face is <c>Dim</c> doubles interleaved as (cos, sin) pairs, i.e. a literal
    /// Fourier series: NOTE (base pitch) is the magnitude-weighted circular mean of the LEARNED TAIL
    /// components, so a token's pitch moves as the model trains; FLAVOUR (timbre) is one partial per
    /// FROZEN codec component, fixed for the life of the model. See <c>Prism.razor</c>'s history
    /// comments for the full tuning log (mid-bass -> robot -> 80s arcade robot) — none of it is
    /// re-derived here, only carried forward.
    ///
    /// Fire-and-forget: a synth glitch (or JS interop being torn down mid-navigation) must never fail
    /// a generation or add latency to the loop that triggered it.
    /// </summary>
    public void PlayTone(HoloSession session, int tokenId)
    {
        if (!SoundOn) return;
        try
        {
            var face = session.Model.Face(tokenId);
            var comps = face.Length / 2;
            var codec = Math.Min(session.Model.FrozenPrefix / 2, comps);
            if (codec < 1 || codec >= comps) return;   // needs both a codec band and a learned tail to play

            var f0 = BaseFrequency(face, codec, comps);
            var n = codec;
            var freqs = new double[n];
            var amps = new double[n];
            var phases = new double[n];
            for (var c = 0; c < n; c++)
            {
                var re = face[2 * c];
                var im = face[2 * c + 1];
                var phase = Math.Atan2(im, re);                               // -pi..pi, the component itself
                amps[c] = Math.Sqrt(re * re + im * im);                       // the component's own magnitude
                phases[c] = phase;
                // Frequency from the same phase, spread logarithmically so equal phase differences are
                // equal musical intervals — NOT quantised to any scale, see the standing rule above.
                freqs[c] = f0 * Math.Pow(2, (phase + Math.PI) / (2 * Math.PI) * PartialSpread);
            }
            _ = _js.InvokeVoidAsync("prismAudio.partials", freqs, amps, phases, ToneMs, 1.0);
        }
        catch { /* audio is decoration, never let a synth glitch touch generation */ }
    }

    // 80s ARCADE ROBOT pitch window (C1-C3, 2 octaves) — the tuned voice, not a default. See
    // Prism.razor's own history comments for the full round-by-round tuning log; not re-litigated
    // here, only carried forward unchanged.
    const double PitchLowHz = 32.0, PitchHighHz = 128.0;

    // How many octaves the codec partials spread above the base frequency. Tuned alongside the pitch
    // window above so the top partial doesn't ring as a buzz over a low note — see PlayTone's own doc.
    const double PartialSpread = 3.5;

    static double BaseFrequency(double[] face, int fromComp, int toComp)
    {
        double sumCos = 0, sumSin = 0;
        var end = Math.Min(toComp, face.Length / 2);
        if (end <= fromComp) return PitchLowHz;
        for (var c = fromComp; c < end; c++) { sumCos += face[2 * c]; sumSin += face[2 * c + 1]; }
        var theta = Math.Atan2(sumSin, sumCos);                       // -pi..pi
        var octaves = Math.Log2(PitchHighHz / PitchLowHz);            // 2 octaves
        return PitchLowHz * Math.Pow(2, (theta + Math.PI) / (2 * Math.PI) * octaves);
    }

    /// <summary>Navigating away mid-generation must not leave a note ringing behind in this
    /// single-page app. Does not touch the sound-on/volume preference, only currently-sounding
    /// notes.</summary>
    public void Dispose()
    {
        try { _ = _js.InvokeVoidAsync("prismAudio.stopAll"); } catch { /* JS runtime may already be torn down */ }
    }
}
