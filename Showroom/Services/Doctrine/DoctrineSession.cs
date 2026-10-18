using System.Diagnostics;
using Doctrine;
using EvalApp;
using Plan = Doctrine.Doctrine;

namespace ShowroomDoctrine;

// One visitor's game of Doctrine, as the page sees it. The game itself is the skeleton's EvalApp round pipeline over the real sections
// (DoctrineSections.Compose). The page is the player's hands (IPlayerInput): when the pipeline asks for a draft or a doctrine it parks here,
// the page shows the screen, and the visitor's tap completes the question. No game rule is decided in this file.
//
// ONE ROUND PER PIPELINE RUN. The game is built with MaxRounds = 1, so each run of the Round pipeline plays exactly one round and hands back its
// RoundRecord (the replay, the spoils); the session then feeds RoundState.Advance() into the next run, which is what the pipeline does itself
// when it recurses. The only rule copied from the skeleton is when a game ends (CloseStage: the round limit, or no soldiers and no gold);
// it is marked below and listed as an amendment request (a round observer on IPlayerInput) in the hand-off.

public enum DoctrineStage { Start, Draft, Doctrine, Working, Watch, Over }

/// <summary>The draft screen's question: the army as it stands and the round.</summary>
public sealed record DraftView(ArmyReadModel Army, int Round);

/// <summary>What the army itself says to a set of picks (its own report), and the army it would have after them.</summary>
public sealed record DraftPreview(DraftReport Report, ArmyReadModel After);

/// <summary>What a finished round leaves for the watch screen.</summary>
public sealed record RoundView(RoundRecord Record, ArmyReadModel ArmyAfter, global::Doctrine.Battle.ArenaLayout? Layout, int YourSoldiers, int EnemySoldiers, bool Last);

public sealed class DoctrineSession : IDisposable
{
    public const int DefaultRounds = 5;

    DoctrineGame? _game;
    DoctrineDomain _rules = new() { MaxRounds = 1 };
    CancellationTokenSource? _life, _round;
    TaskCompletionSource<IReadOnlyList<DraftPick>>? _draftTcs;
    TaskCompletionSource<PlayerChoice>? _chooseTcs;
    TaskCompletionSource<bool>? _next;
    RoundState? _state;
    string _startCode = "";
    long _lastNotify;

    public DoctrineSession(DoctrineSections? sections = null)
    {
        Sections = sections ?? new DoctrineSections();
        Sections.Observer.Started += OnBattleStarted;
        Sections.Observer.Ticked += OnBattleTick;
        Sections.Observer.Finished += OnBattleFinished;
    }

    public void Dispose()
    {
        Sections.Observer.Started -= OnBattleStarted;
        Sections.Observer.Ticked -= OnBattleTick;
        Sections.Observer.Finished -= OnBattleFinished;
        try { _life?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>The sections and the general. They outlive a game so the general keeps what this visitor taught it.</summary>
    public DoctrineSections Sections { get; private set; }
    public event Action? Changed;

    // ---- what the page reads ----
    public DoctrineStage Stage { get; private set; } = DoctrineStage.Start;
    public int Seed { get; private set; }
    public int TotalRounds { get; private set; } = DefaultRounds;
    /// <summary>Round number now being played, from 0.</summary>
    public int RoundIndex { get; private set; }
    public DraftView? Draft { get; private set; }
    public ArmyReadModel? Army { get; private set; }
    public GeneralOutput? Ghost { get; private set; }
    public RoundView? Last { get; private set; }
    public string Status { get; private set; } = "";
    public int BattleTick { get; private set; }
    public int BattleTickLimit => _rules.TickLimit;
    /// <summary>A plain sentence for the page: a cancelled battle, a refused code. Cleared by the next action.</summary>
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public GameResult? Result { get; private set; }
    public IReadOnlyList<RoundRecord> Records => _state?.Records ?? Array.Empty<RoundRecord>();
    /// <summary>Doctrines the visitor has set (each one taught the general once; each battle result taught it once more).</summary>
    public int ChoicesMade { get; private set; }
    public bool InGame => Stage is not (DoctrineStage.Start or DoctrineStage.Over);
    public IArmyService? Armies => _game?.Context.Armies;

    // ---- starting a game ----

    /// <summary>Begin a game. <paramref name="armyCode"/> (an army code from an earlier game) replaces the opening levy; empty means a fresh army.</summary>
    public bool NewGame(int seed, int rounds, string? armyCode, out string error)
    {
        error = "";
        if (InGame) { error = "A game is already in progress. Leave it first."; return false; }
        Notice = ""; Error = ""; Result = null; Last = null; Ghost = null; Draft = null; Army = null; BattleTick = 0; ChoicesMade = 0;
        rounds = Math.Clamp(rounds, 1, 20);
        _game = Sections.Build(new Hands(this), _rules);
        IArmy army;
        var code = (armyCode ?? "").Trim();
        if (code.Length > 0)
        {
            if (!_game.Context.Armies.TryParse(code, out var parsed, out var why) || parsed is null || parsed.Side != Side.Player)
            {
                error = parsed is not null && parsed.Side != Side.Player ? "That is an enemy army code, not a player's." : $"That is not an army code ({why}).";
                _game = null;
                return false;
            }
            army = parsed;
        }
        else army = _game.Context.Armies.Create(Side.Player, seed);
        Seed = seed; TotalRounds = rounds; RoundIndex = 0;
        _life = new CancellationTokenSource();
        _state = new RoundState(0, seed, army);
        Stage = DoctrineStage.Working; Status = "Raising the army";
        Notify();
        _ = RunAsync(_state, _life.Token);
        return true;
    }

    // ---- the visitor's answers ----

    public void SubmitDraft(IReadOnlyList<DraftPick> picks)
    {
        if (Stage != DoctrineStage.Draft || _draftTcs is null) return;
        Notice = "";
        _draftTcs.TrySetResult(picks.Where(p => p.Count > 0).ToList());
    }

    /// <summary>The army's own verdict on picks, asked of a COPY of the army (read from its code), so the real one is untouched until the visitor confirms.</summary>
    public async ValueTask<DraftPreview?> PreviewDraftAsync(IReadOnlyList<DraftPick> picks)
    {
        if (Stage != DoctrineStage.Draft || Armies is null) return null;
        if (!Armies.TryParse(_startCode, out var clone, out _) || clone is null) return null;
        var report = await clone.DraftAsync(new DraftRequest(RoundIndex, Seed, picks.Where(p => p.Count > 0).ToList()), CancellationToken.None);
        return new DraftPreview(report, clone.Read());
    }

    /// <summary>Set the doctrine. <paramref name="overrode"/> is true when it differs from the general's ghost; it goes through to the general's learn call.</summary>
    public void SubmitChoice(Plan doctrine, bool overrode)
    {
        if (Stage != DoctrineStage.Doctrine || _chooseTcs is null) return;
        ChoicesMade++;
        _chooseTcs.TrySetResult(new PlayerChoice(doctrine, overrode));
    }

    public void CancelBattle()
    {
        if (Stage != DoctrineStage.Working) return;
        try { _round?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void NextRound()
    {
        if (Stage != DoctrineStage.Watch) return;
        _next?.TrySetResult(true);
    }

    /// <summary>Leave the game here. From a draft or doctrine screen the army goes back to how it stood when the round began.</summary>
    public void Leave()
    {
        if (!InGame) return;
        if (Stage == DoctrineStage.Watch) { _next?.TrySetResult(false); return; }
        try { _life?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>From the end screen back to the start screen.</summary>
    public void BackToStart()
    {
        if (InGame) return;
        Stage = DoctrineStage.Start; Notice = ""; Error = "";
        Notify();
    }

    /// <summary>Start over with a new, empty general. Only between games.</summary>
    public bool ResetGeneral()
    {
        if (InGame) return false;
        Sections.Observer.Started -= OnBattleStarted; Sections.Observer.Ticked -= OnBattleTick; Sections.Observer.Finished -= OnBattleFinished;
        Sections = new DoctrineSections();
        Sections.Observer.Started += OnBattleStarted; Sections.Observer.Ticked += OnBattleTick; Sections.Observer.Finished += OnBattleFinished;
        Ghost = null; ChoicesMade = 0;
        Notify();
        return true;
    }

    // ---- the round loop ----

    async Task RunAsync(RoundState s, CancellationToken life)
    {
        try
        {
            while (true)
            {
                _state = s; RoundIndex = s.Round; _startCode = s.Player.ToCode();
                _round = CancellationTokenSource.CreateLinkedTokenSource(life);
                BattleTick = 0; Ghost = null; Draft = null;
                PipelineResult<RoundState>? run = null;
                try { run = await _game!.Round.RunAsync(s, _round.Token); }
                catch (OperationCanceledException) { }
                if (life.IsCancellationRequested) { Finish(_startCode); return; }
                if (run is null || (run is PipelineResult<RoundState>.Failure cf && (_round.IsCancellationRequested || cf.Exception is OperationCanceledException)))
                {
                    // A cancelled battle: put the army back as it stood when the round began and ask for the draft again.
                    if (!_game!.Context.Armies.TryParse(_startCode, out var back, out var why) || back is null) throw new InvalidOperationException("could not restore the army: " + why);
                    s = s with { Player = back };
                    Notice = "Battle cancelled. The round starts again from your draft.";
                    continue;
                }
                if (run is PipelineResult<RoundState>.Failure f) throw f.Exception;
                s = run.GetData();
                _state = s;
                var rec = s.Records[^1];
                var after = s.Player.Read();
                // THE END-OF-GAME RULE, copied from the skeleton's CloseStage (which cannot be asked, because the game runs one round per run):
                // the round limit, or an army with no soldiers and no gold.
                bool last = s.Records.Count >= TotalRounds || (after.Soldiers == 0 && after.Gold < 1);
                var first = rec.Replay.Frames.Count > 0 ? rec.Replay.Frames[0] : null;
                int mine = first?.Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength) ?? 0;
                int theirs = first?.Blobs.Where(b => b.Side == Side.Enemy).Sum(b => b.Strength) ?? 0;
                Last = new RoundView(rec, after, Sections.LayoutOf(rec.ArenaId), mine, theirs, last);
                Army = after;
                Stage = DoctrineStage.Watch; Status = "";
                Notify();
                _next = new TaskCompletionSource<bool>();
                bool carryOn = await _next.Task;
                if (!carryOn || last) { Finish(s.Player.ToCode()); return; }
                Notice = "";
                s = s.Advance();
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = "The game stopped: " + e.Message;
            Finish(_startCode);
        }
    }

    void Finish(string armyCode)
    {
        Result = new GameResult(Seed, Records.ToList(), armyCode);
        Stage = DoctrineStage.Over; Status = "";
        Notify();
    }

    // ---- battle progress (from the observer around the Tracer engine) ----

    void OnBattleStarted(BattleSetup setup) { BattleTick = 0; Status = "Simulating the battle on this device"; Notify(); }
    void OnBattleTick(int tick)
    {
        BattleTick = tick;
        long now = Stopwatch.GetTimestamp();
        if ((now - _lastNotify) * 1000 / Stopwatch.Frequency >= 60) { _lastNotify = now; Notify(); }
    }
    void OnBattleFinished(BattleResult r) { Status = "Settling losses and spoils; the general learns the result"; Notify(); }

    void Notify() => Changed?.Invoke();

    // ---- the page as the player's hands ----

    sealed class Hands(DoctrineSession s) : IPlayerInput
    {
        public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
        {
            s.Army = army; s.Draft = new DraftView(army, round); s.Stage = DoctrineStage.Draft; s.Status = "";
            var tcs = s._draftTcs = new TaskCompletionSource<IReadOnlyList<DraftPick>>(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Notify();
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var picks = await tcs.Task;
            s.Stage = DoctrineStage.Working; s.Status = "Placing your picks and raising the enemy"; s.Notify();
            return picks;
        }

        public async ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
        {
            s.Army = army; s.Ghost = ghost; s.Stage = DoctrineStage.Doctrine; s.Status = "";
            var tcs = s._chooseTcs = new TaskCompletionSource<PlayerChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Notify();
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var choice = await tcs.Task;
            s.Stage = DoctrineStage.Working; s.Status = "Your general is learning from this doctrine"; s.Notify();
            return choice;
        }
    }
}
