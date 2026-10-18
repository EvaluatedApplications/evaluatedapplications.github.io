namespace Doctrine;

// THE SEAMS. Each is a small interface over the plain records in Records.cs. CONTRACT.md states the invariants;
// the contract test base classes in Doctrine.Tests enforce them against any implementation, stub or real.
// All methods are async-shaped (ValueTask) because the real sections await Tracer and the browser must be able to yield;
// none may block a thread, touch the filesystem, or use reflection-based serialisation.

// ---- (a) ARMY: owner phasor-owner ----------------------------------------------------------------------------------

/// <summary>Creates armies and reads army codes. The real one keeps ALL game state in Pleroma stores.</summary>
public interface IArmyService
{
    /// <summary>A fresh army for a side. Same (side, seed) gives an identical army.</summary>
    IArmy Create(Side side, int seed);
    /// <summary>Parse an army code (see <see cref="IArmy.ToCode"/>). Never throws: bad input returns false with a reason.</summary>
    bool TryParse(string code, out IArmy? army, out string error);
}

public interface IArmy
{
    Side Side { get; }
    /// <summary>Reads: cohorts, gold, and the legal doctrine list for the coming round. Pure; no side effects.</summary>
    ArmyReadModel Read();
    /// <summary>
    /// Scoring read: a single non-negative number summarising army worth (used for the ladder and for training weights). Its scale and formula
    /// are the section's own, so compare only scores from the same section; CONTRACT.md (a) states what must hold of any scale.
    /// </summary>
    int Score();
    /// <summary>Buy cohorts. Refusals (unknown kind, count of zero or less, gold) come back in the report with a reason; a refused pick changes nothing. Deterministic: no chance in a draft.</summary>
    ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct);
    /// <summary>
    /// Apply the army-order tokens of a doctrine in slot order. Legality is judged PER TOKEN, against the army as the earlier tokens left it
    /// (not once before the doctrine). Illegal tokens are skipped and counted, never thrown.
    /// </summary>
    ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(DoctrineSet doctrine, CancellationToken ct);
    /// <summary>Subtract battle losses. Counts never go below zero; returns the soldiers actually removed.</summary>
    ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct);
    ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct);
    /// <summary>Close the round: advance the army's own clock (auras wear off). Round number increases by one.</summary>
    ValueTask EndRoundAsync(CancellationToken ct);
    /// <summary>The army as a short code (a few hundred bytes, URL-safe text). Parse(ToCode()) reads back identically.</summary>
    string ToCode();
}

// ---- (b) BATTLE: owner tracer-owner --------------------------------------------------------------------------------

public interface IBattleEngine
{
    /// <summary>The arena (navmesh) catalogue. At least one entry; ids are unique and stable.</summary>
    IReadOnlyList<ArenaInfo> Arenas { get; }
    /// <summary>Start a battle. Same setup gives the same battle on this device. Never throws for a valid setup.</summary>
    ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct);
}

/// <summary>
/// One battle in progress. The pipeline calls the four tick stages in order, then <see cref="Record"/>, once per tick,
/// until <see cref="Status"/> leaves Running or the tick limit is reached. The session owns all simulation state.
/// </summary>
public interface IBattleSession
{
    /// <summary>Completed ticks so far.</summary>
    int Tick { get; }
    /// <summary>Running until one side has no soldiers (AttackerWon, DefenderWon, or Draw if both fall in the same tick).</summary>
    BattleStatus Status { get; }
    ValueTask FieldOrdersAsync(CancellationToken ct);   // stage Battle.FieldOrders: each group's order picks a behaviour and goal
    ValueTask MoveAsync(CancellationToken ct);          // stage Battle.Move: path and move one tick
    ValueTask EngageAsync(CancellationToken ct);        // stage Battle.Engage: who strikes whom (pending damage, not yet applied)
    ValueTask ApplyLossesAsync(CancellationToken ct);   // stage Battle.ApplyLosses: pending damage becomes soldiers lost
    /// <summary>The current state as a frame, without advancing (used for frame 0).</summary>
    ReplayFrame Snapshot();
    /// <summary>Close the tick: increases <see cref="Tick"/> by one and returns the frame for the new tick (events of this tick included).</summary>
    ReplayFrame Record();
    /// <summary>The result. <paramref name="tickLimitReached"/> true means the limit stopped a still-running battle (stronger side wins, equal is a Draw).</summary>
    BattleResult Finish(bool tickLimitReached);
}

// ---- (c) GENERAL: owner algformer-owner ----------------------------------------------------------------------------

/// <summary>The player's general. Starts EMPTY (nothing pre-trained ships) and learns only from its own player.</summary>
public interface IGeneral
{
    /// <summary>Examples learned so far (imitation plus outcome). A new general has 0.</summary>
    int ExamplesSeen { get; }
    /// <summary>Suggest a doctrine for this situation. Every non-End token is allowed by <see cref="GeneralInput.Legal"/>. Deterministic for a fixed state.</summary>
    ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct);
    /// <summary>Imitation: the player set (or accepted) this doctrine.</summary>
    ValueTask LearnAsync(GeneralExample example, CancellationToken ct);
    /// <summary>Outcome: this choice was followed by this result.</summary>
    ValueTask LearnOutcomeAsync(GeneralOutcome outcome, CancellationToken ct);
}

// ---- (f) PLAYER INPUT: the human (the page) or a script (tests) ---------------------------------------------------

public sealed record PlayerChoice(DoctrineSet Doctrine, bool Overrode);

/// <summary>The player's hands. The page implements it with real input; tests use a scripted one.</summary>
public interface IPlayerInput
{
    /// <summary>The cohorts to buy this round (may be empty).</summary>
    ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct);
    /// <summary>
    /// The army's own verdict on the picks just returned (accepted, refused with a reason, gold spent). Called by the draft stage after the
    /// army applied them, before the enemy is raised. Optional: the default does nothing.
    /// </summary>
    ValueTask DraftedAsync(DraftReport report, CancellationToken ct) => ValueTask.CompletedTask;
    /// <summary>Choose the doctrine, seeing the general's ghost suggestion. Accept it as is (Overrode = false) or replace it.</summary>
    ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct);
    /// <summary>
    /// A round has closed. <paramref name="record"/> is the whole record (doctrine, orders report, replay, result, spoils, and
    /// <see cref="RoundRecord.Final"/>: whether this round ended the game); <paramref name="after"/> is the player's army as the next round
    /// finds it (losses, spoils and the end of the round applied). Called by the close stage once per round, before the next round starts,
    /// so a host can show a round as it happens inside ONE run of the game. Optional: the default does nothing.
    /// </summary>
    ValueTask RoundClosedAsync(RoundRecord record, ArmyReadModel after, CancellationToken ct) => ValueTask.CompletedTask;
}

// ---- (g) OPTIONAL CAPABILITIES AND HOST HOOKS ------------------------------------------------------------------------

/// <summary>
/// An optional capability of a battle engine: it can say how to DRAW an arena (the recording carries cohorts, never terrain). A host asks
/// <see cref="DoctrineContext.LayoutOf"/>, which uses it when the engine has it, so no concrete engine type is named. The Tracer section has it.
/// </summary>
public interface IArenaLayouts : IBattleEngine
{
    /// <summary>The drawable layout of an arena, or null for an id this engine does not have.</summary>
    global::Doctrine.Battle.ArenaLayout? LayoutOf(string arenaId);
}

/// <summary>
/// An optional watcher of the pipeline, installed with <c>WithObserver</c> or <see cref="DoctrineContext.Observer"/>. Every method is a no-op by
/// default and takes only integers, constant stage names and records the pipeline already holds, so an observer allocates nothing and a game
/// with none pays one null check per call. Calls come in pipeline order, must be quick, and must not throw.
/// </summary>
public interface IRoundObserver
{
    /// <summary>A round-pipeline stage is starting (<paramref name="stage"/> is one of the <see cref="Stages"/> constants, <c>Army.Draft</c> to <c>Round.Advance</c>).</summary>
    void StageEntered(string stage, int round) { }
    /// <summary>The battle engine has started the battle (arena baked, frame 0 taken).</summary>
    void BattleStarted(BattleSetup setup) { }
    /// <summary>Tick <paramref name="tick"/> of at most <paramref name="tickLimit"/> has just been recorded.</summary>
    void BattleTick(int tick, int tickLimit) { }
    /// <summary>The battle is over (a win or the tick limit).</summary>
    void BattleFinished(BattleResult result) { }
}
