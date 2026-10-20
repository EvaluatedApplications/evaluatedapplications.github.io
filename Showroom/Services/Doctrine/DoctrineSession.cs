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

/// <summary>The draft screen's question: the army as it stands, the round and the companies the muster offers.</summary>
public sealed record DraftView(ArmyReadModel Army, int Round, UnitOffer Offer);

/// <summary>The council is on the table and the game asks whether to pay for another: the host as it stands (after the draft and any earlier reroll) and the orders offered.</summary>
public sealed record RerollAsk(ArmyReadModel Army, OrderOffer Offer, int Round);

/// <summary>One of the three formations of the host: how it stands after <see cref="Turns"/> turns of Reform the ranks (0 = as it stands). <see cref="Host"/> is null when the army would not take the order.</summary>
public sealed record Formation(int Turns, int Token, ArmyReadModel? Host);

/// <summary>What the army itself says to a set of picks (its own report), and the army it would have after them.</summary>
public sealed record DraftPreview(DraftReport Report, ArmyReadModel After);

/// <summary>What a finished round leaves for the watch screen.</summary>
public sealed record RoundView(RoundRecord Record, ArmyReadModel ArmyAfter, global::Doctrine.Battle.ArenaLayout? Layout, int YourSoldiers, int EnemySoldiers, bool Last)
{
    /// <summary>The battle's own set-up as the pipeline handed it to the battle (both sides' cohorts with the traits they fought with), or null when none was seen.</summary>
    public BattleSetup? Setup { get; init; }

    /// <summary>The siege clock of the arena the round was fought on, in ticks, from the battle's own catalogue (0 for an open ground, or when not known).</summary>
    public int SiegeClock { get; init; }

    /// <summary>The traits the cohort of this side and kind fought with, from the set-up; null when the set-up is not known.</summary>
    public Trait? TraitsOf(Side side, int key)
    {
        if (Setup is null) return null;
        foreach (var bs in new[] { Setup.Attacker, Setup.Defender })
            if (bs.Side == side) foreach (var c in bs.Cohorts) if (c.Key == key) return c.Traits;
        return null;
    }
}

public sealed class DoctrineSession : IDisposable
{
    public const int DefaultRounds = 5;

    /// <summary>
    /// The rules of a game on this page: the game's own (<see cref="OfferRules.Default"/>: six companies, eight orders, up to four of them), with the unlock
    /// ladder as the host's POOLS. <paramref name="stepOf"/> gives the step a round is on; with it, a round offers only the kinds and the orders that step has
    /// opened (<see cref="DoctrineUnlocks"/>), so the ladder widens what can be offered instead of hiding cards. Without it the pools are the game's whole
    /// roster and vocabulary. A proof builds its headless games with the same rules, so a page game and a headless game are compared on equal terms.
    /// </summary>
    public static DoctrineDomain GameRules(int rounds, Func<int, int>? stepOf = null)
    {
        var offers = OfferRules.Default;
        if (stepOf is not null)
            offers = offers with
            {
                KindPool = (round, kind) => DoctrineUnlocks.KindOpen(kind, stepOf(round)),
                OrderPool = (round, token) => DoctrineUnlocks.Allows(token, stepOf(round)),
            };
        return new DoctrineDomain { MaxRounds = rounds, Offers = offers };
    }

    DoctrineGame? _game;
    DoctrineDomain _rules = new();
    readonly DoctrineProgress? _progress;
    CancellationTokenSource? _life, _round;
    TaskCompletionSource<IReadOnlyList<DraftPick>>? _draftTcs;
    TaskCompletionSource<PlayerChoice>? _chooseTcs;
    TaskCompletionSource<bool>? _rerollTcs;
    TaskCompletionSource<bool>? _next;
    readonly List<RoundRecord> _records = new();
    IArmy? _army;                    // the visitor's army, the one object the whole run carries
    RoundState? _restart;            // the run's state as the CURRENT round began (where a cancelled round starts again)
    string _roundCode = "";          // the army as the current round began
    string _closedCode = "";         // the army as the closed round left it (valid while parked on the Watch screen)
    bool _parked;                    // the hands are waiting on the Watch screen
    BattleSetup? _setup;             // the set-up of the battle most recently started (the chronicle reads the traits the cohorts fought with)
    long _lastNotify;
    readonly Watcher _watcher;

    public DoctrineSession(DoctrineSections? sections = null, Func<DoctrineSections>? makeSections = null, DoctrineProgress? progress = null)
    {
        _makeSections = makeSections;
        _progress = progress;
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
    /// <summary>The companies the muster offered this round (as the draft screen was handed them), or null outside a draft.</summary>
    public UnitOffer? UnitsOffered => Draft?.Offer;
    /// <summary>The orders on the council's table (the offer the game drew, and the one it will bind the choice to), or null before it is drawn.</summary>
    public OrderOffer? OrdersOffered { get; private set; }
    /// <summary>Set while the game asks whether to pay for another council; null otherwise. The general has not seen the council yet then.</summary>
    public RerollAsk? Reroll { get; private set; }
    /// <summary>Counts every screen the hands park on (a draft, a reroll question, a doctrine), so the page can tell a new question from the same one.</summary>
    public int Turn { get; private set; }
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

    /// <summary>The siege clock of an arena of the game most recently started, in ticks (0: an open ground, or none started yet).</summary>
    public int SiegeClockOf(string arenaId) => _game?.Context.Battle.Arenas.FirstOrDefault(a => a.Id == arenaId)?.SiegeClock ?? 0;

    /// <summary>The grounds of the game most recently started (its engine's own catalogue), or null before the first game: the grounds exist only in a built game.</summary>
    public IReadOnlyList<ArenaInfo>? Grounds => _game?.Context.Battle.Arenas;

    /// <summary>
    /// The ground of the round now being played and who besieges it, asked of the game itself before the draft (<c>DoctrineGame.BriefingAsync</c>: the same rules the setup stage
    /// uses, so it cannot disagree with the battle that follows). Null before the first round, or if the game could not say.
    /// </summary>
    public ArenaBrief? Ground { get; private set; }

    async ValueTask<ArenaBrief?> GroundOfAsync(int round, CancellationToken ct)
    {
        if (_game is null) return null;
        try { return (await _game.BriefingAsync(Seed, round, 0, ct)).Arena; }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { return null; }
    }

    // ---- starting a game ----

    /// <summary>Begin a game. <paramref name="armyCode"/> (an army code from an earlier game) replaces the opening levy; empty means a fresh army.</summary>
    public bool NewGame(int seed, int rounds, string? armyCode, out string error)
    {
        error = "";
        if (InGame) { error = "A game is already in progress. Leave it first."; return false; }
        Notice = ""; Error = ""; Result = null; Last = null; Ghost = null; Draft = null; Army = null; BattleTick = 0; ChoicesMade = 0;
        OrdersOffered = null; Reroll = null;
        rounds = Math.Clamp(rounds, 1, 20);
        // The step a round is on: the ladder's own schedule, never below what this visitor has already opened (a restarted game keeps it, Show everything opens all).
        _rules = GameRules(rounds, round => Math.Max(_progress?.Step ?? 0, DoctrineUnlocks.StepForRound(round, rounds)));
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

    readonly Dictionary<int, OrderTrial?> _trials = new();
    readonly Dictionary<int, Formation> _forms = new();

    /// <summary>
    /// The three formations of the host as it stands now, each read from the army itself: as it stands, and after one and after two turns of Reform the
    /// ranks, tried on COPIES read from the host's own code (the real army is untouched). Only while the council is on the table.
    /// </summary>
    public IReadOnlyList<Formation> Formations()
    {
        var list = new List<Formation>();
        for (int turns = 0; turns < DoctrineVocabulary.Groups; turns++)
        {
            if (_forms.TryGetValue(turns, out var have)) { list.Add(have); continue; }
            int token = turns == 0 ? 0 : DoctrineVocabulary.Army(ArmyOrderKind.Rotate, turns);
            ArmyReadModel? host = null;
            if (Stage == DoctrineStage.Doctrine && _army is not null && Armies is not null)
            {
                try
                {
                    if (Armies.TryParse(_army.ToCode(), out var copy, out _) && copy is not null)
                    {
                        if (turns == 0) host = copy.Read();
                        else
                        {
                            var run = copy.ApplyArmyOrdersAsync(DoctrineSet.Of(token), CancellationToken.None);
                            if (run.IsCompletedSuccessfully && run.Result.Applied == 1) host = copy.Read();
                        }
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException) { host = null; }
            }
            var f = new Formation(turns, token, host);
            if (host is not null) _forms[turns] = f;
            list.Add(f);
        }
        return list;
    }

    /// <summary>Pay for another council (the question the game is asking). The game takes the fee and draws again.</summary>
    public void RerollCouncil()
    {
        if (Stage != DoctrineStage.Doctrine || _rerollTcs is null) return;
        _rerollTcs.TrySetResult(true);
    }

    /// <summary>Keep the council as it is (the answer to the same question). The general then reads it.</summary>
    public void KeepCouncil()
    {
        if (Stage != DoctrineStage.Doctrine || _rerollTcs is null) return;
        _rerollTcs.TrySetResult(false);
    }

    /// <summary>
    /// What an order for the whole host would do, tried ALONE on a copy of the host as it stands now (the copy is read from the host's own code, so the real
    /// army is untouched), asked of the army itself: the host before and after as it reads them. Null for an order the host would not take, or one that is
    /// not an order for the whole host. Kept for the doctrine screen, so the tray asks each order once.
    /// </summary>
    public OrderTrial? Trial(int token)
    {
        if (Stage != DoctrineStage.Doctrine || _army is null || Armies is null || token == 0 || !DoctrineVocabulary.IsValid(token)) return null;
        if (DoctrineVocabulary.Decode(token).Family != OrderFamily.Army) return null;
        if (_trials.TryGetValue(token, out var have)) return have;
        OrderTrial? trial = null;
        try
        {
            if (Armies.TryParse(_army.ToCode(), out var copy, out _) && copy is not null)
            {
                var before = copy.Read();
                var run = copy.ApplyArmyOrdersAsync(DoctrineSet.Of(token), CancellationToken.None);
                if (run.IsCompletedSuccessfully && run.Result.Applied == 1) trial = new OrderTrial(before, copy.Read());
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { trial = null; }
        _trials[token] = trial;
        return trial;
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
        public void BattleStarted(BattleSetup setup) { s._setup = setup; s.BattleTick = 0; s.BattleTickLimit = setup.TickLimit; s.Status = "Simulating the battle on this device"; s.Notify(); }
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
        // The free draft of the old game: the muster is the open market. The pipeline of this page always calls the overload with the offer.
        public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => DraftAsync(army, UnitOffer.Open(army.Gold), round, ct);

        public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct)
        {
            s.RoundIndex = round; s.BattleTick = 0; s.Ghost = null; s.OrdersOffered = null; s.Reroll = null;
            s.Ground = await s.GroundOfAsync(round, ct);
            s.Army = army; s.Draft = new DraftView(army, round, offer); s.Stage = DoctrineStage.Draft; s.Status = ""; s.Turn++;
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

        /// <summary>
        /// The game asks whether to pay for another council. When the purse cannot pay (or no reroll is left) the answer is no without a question. Otherwise
        /// the hands park on the council with the question; the general has not read the council yet.
        /// </summary>
        public async ValueTask<bool> RerollAsync(ArmyReadModel army, OrderOffer offer, int round, CancellationToken ct)
        {
            s.Army = army; s.OrdersOffered = offer;
            if (!offer.Active || offer.RerollsLeft <= 0 || army.Gold < offer.RerollCost) return false;
            s._trials.Clear(); s._forms.Clear();
            s.Ghost = null; s.Reroll = new RerollAsk(army, offer, round); s.Stage = DoctrineStage.Doctrine; s.Status = ""; s.Turn++;
            var tcs = s._rerollTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            s.Notify();
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            bool again;
            try { again = await tcs.Task; }
            finally { s._rerollTcs = null; s.Reroll = null; }
            s.Stage = DoctrineStage.Working; s.Status = again ? "Drawing a new council" : "Your general is reading the council"; s.Notify();
            return again;
        }

        public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
            => ChooseAsync(army, ghost, s.OrdersOffered ?? OrderOffer.Open(), round, ct);

        public async ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
        {
            s._trials.Clear(); s._forms.Clear();
            s.OrdersOffered = offer; s.Reroll = null;
            s.Army = army; s.Ghost = ghost; s.Stage = DoctrineStage.Doctrine; s.Status = ""; s.Turn++;
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
            s.Last = s.Last with { Setup = s._setup, SiegeClock = s.SiegeClockOf(rec.ArenaId) };
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
