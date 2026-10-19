using Doctrine;
using EvalApp;

namespace ShowroomDoctrine;

// One visitor's game of Doctrine, as the page sees it. The game itself is the skeleton's EvalApp round pipeline over the real sections
// (DoctrineSections). The page is the player's hands (IPlayerInput): when the pipeline asks for a draft or a doctrine it parks here, the page
// shows the screen, and the visitor's tap completes the question. No game rule is decided in this file.
//
// THE WHOLE GAME IS ONE RUN of the Round pipeline, with the game's real MaxRounds. Each closed round arrives through
// IPlayerInput.RoundClosedAsync (the record, the army as the next round finds it, and Final: whether the round ended the game), where the hands
// park on the Watch screen until the visitor taps Next. The end of the game is the pipeline's answer (RoundRecord.Final); no rule is copied.
// Progress comes from the skeleton's IRoundObserver, the arena's drawing from DoctrineContext.LayoutOf. A cancelled battle (or a visitor leaving)
// cancels the run's token; the army is put back from the code taken when the round began and the run starts again from that round.

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
    DoctrineDomain _rules = new();
    CancellationTokenSource? _life, _round;
    TaskCompletionSource<IReadOnlyList<DraftPick>>? _draftTcs;
    TaskCompletionSource<PlayerChoice>? _chooseTcs;
    TaskCompletionSource<bool>? _next;
    readonly List<RoundRecord> _records = new();
    IArmy? _army;                    // the visitor's army, the one object the whole run carries
    RoundState? _restart;            // the run's state as the CURRENT round began (where a cancelled round starts again)
    string _roundCode = "";          // the army as the current round began
    string _closedCode = "";         // the army as the closed round left it (valid while parked on the Watch screen)
    bool _parked;                    // the hands are waiting on the Watch screen
    long _lastNotify;
    readonly Watcher _watcher;

    public DoctrineSession(DoctrineSections? sections = null, Func<DoctrineSections>? makeSections = null)
    {
        _makeSections = makeSections;
        Sections = sections ?? makeSections?.Invoke() ?? new DoctrineSections();
        _watcher = new Watcher(this);
    }
    readonly Func<DoctrineSections>? _makeSections;

    public void Dispose()
    {
        try { _life?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>The sections and the general. They outlive a game so the general keeps what this visitor taught it.</summary>
    public DoctrineSections Sections { get; private set; }
    public event Action? Changed;
    /// <summary>A tick of a battle was recorded (called on the pipeline's thread, quickly). The page does not use it; a proof can.</summary>
    public event Action<int>? Ticked;

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
    public int BattleTickLimit { get; private set; } = BattleSetup.DefaultTickLimit;
    /// <summary>A plain sentence for the page: a cancelled battle, a refused pick. Cleared by the next action.</summary>
    public string Notice { get; private set; } = "";
    public string Error { get; private set; } = "";
    public GameResult? Result { get; private set; }
    public IReadOnlyList<RoundRecord> Records => _records;
    /// <summary>Doctrines the visitor has set (each one taught the general once; each battle result taught it once more).</summary>
    public int ChoicesMade { get; private set; }
    public bool InGame => Stage is not (DoctrineStage.Start or DoctrineStage.Over);
    public IArmyService? Armies => _game?.Context.Armies;

    /// <summary>The name of an arena of the game most recently started (its engine's own catalogue), or the id when there is none.</summary>
    public string ArenaName(string arenaId) => _game?.Context.Battle.Arenas.FirstOrDefault(a => a.Id == arenaId)?.Name ?? arenaId;

    // ---- starting a game ----

    /// <summary>Begin a game. <paramref name="armyCode"/> (an army code from an earlier game) replaces the opening levy; empty means a fresh army.</summary>
    public bool NewGame(int seed, int rounds, string? armyCode, out string error)
    {
        error = "";
        if (InGame) { error = "A game is already in progress. Leave it first."; return false; }
        Notice = ""; Error = ""; Result = null; Last = null; Ghost = null; Draft = null; Army = null; BattleTick = 0; ChoicesMade = 0;
        rounds = Math.Clamp(rounds, 1, 20);
        _rules = new DoctrineDomain { MaxRounds = rounds };
        BattleTickLimit = _rules.TickLimit;
        _game = Sections.Build(new Hands(this), _rules, _watcher);
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
        _records.Clear(); _parked = false;
        _army = army; _roundCode = army.ToCode(); _closedCode = _roundCode;
        _restart = new RoundState(0, seed, army);
        _life = new CancellationTokenSource();
        Stage = DoctrineStage.Working; Status = "Raising the army";
        Notify();
        _ = RunAsync(_life.Token);
        return true;
    }

    // ---- the visitor's answers ----

    public void SubmitDraft(IReadOnlyList<DraftPick> picks)
    {
        if (Stage != DoctrineStage.Draft || _draftTcs is null) return;
        Notice = "";
        _draftTcs.TrySetResult(picks.Where(p => p.Count > 0).ToList());
    }

    /// <summary>
    /// The army's own verdict on picks BEFORE the visitor confirms them (the live "this would cost" hint), asked of a COPY of the army read from
    /// its round-start code, so the real one is untouched. The pipeline's own report (DraftedAsync) only exists after the confirmation.
    /// </summary>
    public async ValueTask<DraftPreview?> PreviewDraftAsync(IReadOnlyList<DraftPick> picks)
    {
        if (Stage != DoctrineStage.Draft || Armies is null) return null;
        if (!Armies.TryParse(_roundCode, out var clone, out _) || clone is null) return null;
        var report = await clone.DraftAsync(new DraftRequest(RoundIndex, Seed, picks.Where(p => p.Count > 0).ToList()), CancellationToken.None);
        return new DraftPreview(report, clone.Read());
    }

    /// <summary>Set the doctrine. <paramref name="overrode"/> is true when it differs from the general's ghost; it goes through to the general's learn call.</summary>
    public void SubmitChoice(DoctrineSet doctrine, bool overrode)
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

    /// <summary>Leave the game here. From a draft or doctrine screen the army goes back to how it stood when the round began; from the watch screen it is the army after that round.</summary>
    public void Leave()
    {
        if (!InGame) return;
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
        Sections = _makeSections?.Invoke() ?? new DoctrineSections();
        Ghost = null; ChoicesMade = 0;
        Notify();
        return true;
    }

    // ---- the run ----

    async Task RunAsync(CancellationToken life)
    {
        try
        {
            while (true)
            {
                _round = CancellationTokenSource.CreateLinkedTokenSource(life);
                BattleTick = 0; Ghost = null; Draft = null;
                PipelineResult<RoundState>? run = null;
                try { run = await _game!.Round.RunAsync(_restart!, _round.Token); }
                catch (OperationCanceledException) { }
                if (life.IsCancellationRequested) { Finish(_parked ? _closedCode : _roundCode); return; }
                if (run is null || (run is PipelineResult<RoundState>.Failure cf && (_round.IsCancellationRequested || cf.Exception is OperationCanceledException)))
                {
                    // A cancelled battle: put the army back as it stood when the round began and ask for the draft again.
                    if (!_game!.Context.Armies.TryParse(_roundCode, out var back, out var why) || back is null) throw new InvalidOperationException("could not restore the army: " + why);
                    _army = back; _parked = false;
                    _restart = _restart! with { Player = back };
                    _records.RemoveRange(_restart.Records.Count, _records.Count - _restart.Records.Count);
                    Notice = "Battle cancelled. The round starts again from your draft.";
                    continue;
                }
                if (run is PipelineResult<RoundState>.Failure f) throw f.Exception;
                // The pipeline ran to its own end (the last round's Final): the army is the one the closed round left.
                Finish(_closedCode);
                return;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = "The game stopped: " + e.Message;
            Finish(_parked ? _closedCode : _roundCode);
        }
    }

    void Finish(string armyCode)
    {
        _parked = false;
        Result = new GameResult(Seed, _records.ToList(), armyCode);
        Stage = DoctrineStage.Over; Status = "";
        Notify();
    }

    void Notify() => Changed?.Invoke();

    // ---- progress (from the skeleton's observer: no section is wrapped) ----

    sealed class Watcher(DoctrineSession s) : IRoundObserver
    {
        public void StageEntered(string stage, int round)
        {
            if (stage == Stages.GeneralImitate) { s.Status = "Your general is learning from this doctrine"; s.Notify(); }
            else if (stage == Stages.GeneralOutcome) { s.Status = "Your general is learning from the result"; s.Notify(); }
        }
        public void BattleStarted(BattleSetup setup) { s.BattleTick = 0; s.BattleTickLimit = setup.TickLimit; s.Status = "Simulating the battle on this device"; s.Notify(); }
        public void BattleTick(int tick, int tickLimit)
        {
            s.BattleTick = tick; s.BattleTickLimit = tickLimit;
            s.Ticked?.Invoke(tick);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if ((now - s._lastNotify) * 1000 / System.Diagnostics.Stopwatch.Frequency >= 60) { s._lastNotify = now; s.Notify(); }
        }
        public void BattleFinished(BattleResult result) { s.Status = "Settling losses and spoils"; s.Notify(); }
    }

    // ---- the page as the player's hands ----

    sealed class Hands(DoctrineSession s) : IPlayerInput
    {
        public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
        {
            s.RoundIndex = round; s.BattleTick = 0; s.Ghost = null;
            s.Army = army; s.Draft = new DraftView(army, round); s.Stage = DoctrineStage.Draft; s.Status = "";
            var tcs = s._draftTcs = new TaskCompletionSource<IReadOnlyList<DraftPick>>(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Notify();
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var picks = await tcs.Task;
            s.Stage = DoctrineStage.Working; s.Status = "Placing your picks and raising the enemy"; s.Notify();
            return picks;
        }

        public ValueTask DraftedAsync(DraftReport report, CancellationToken ct)
        {
            // The army's own verdict on what the visitor confirmed: say plainly what it would not take.
            if (report.Refused.Count > 0)
                s.Notice = "The army did not take part of your draft. " + string.Join(" ", report.Refused.Select(r => $"{DoctrineWords.KindName(r.Pick.Kind)} x{r.Pick.Count}: {DoctrineWords.Refusal(r.Reason)}"));
            return ValueTask.CompletedTask;
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

        public async ValueTask RoundClosedAsync(RoundRecord rec, ArmyReadModel after, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            s._records.Add(rec);
            s._closedCode = s._army!.ToCode();
            var first = rec.Replay.Frames.Count > 0 ? rec.Replay.Frames[0] : null;
            int mine = first?.Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength) ?? 0;
            int theirs = first?.Blobs.Where(b => b.Side == Side.Enemy).Sum(b => b.Strength) ?? 0;
            s.Last = new RoundView(rec, after, s._game!.Context.LayoutOf(rec.ArenaId), mine, theirs, rec.Final);
            s.Army = after;
            s.Stage = DoctrineStage.Watch; s.Status = ""; s._parked = true;
            var tcs = s._next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Notify();
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;                       // a Leave (or a cancel) cancels the token and ends the wait with a cancellation
            s._parked = false;
            s.Notice = "";
            s._roundCode = s._closedCode;
            if (!rec.Final) s._restart = new RoundState(rec.Round + 1, s.Seed, s._army, History: s._records.ToList(),
                LastOutcome: rec.Result.Outcome, LastPlayerWon: rec.Result.AttackerWon);
        }
    }
}
