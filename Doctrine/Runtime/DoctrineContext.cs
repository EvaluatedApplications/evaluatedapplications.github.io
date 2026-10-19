using EvalApp;
using Tracer.Integration;

namespace Doctrine;

/// <summary>
/// The shared game services, as the pipeline's GlobalContext (EvalApp snippet 4: the context is OUR object and holds every service).
/// It also holds the three pipelines by reference, so a stage can run another pipeline (the round runs a battle; the battle runs
/// a tick; the round runs the next round) without anything importing anything.
/// </summary>
public sealed class DoctrineContext : GlobalContext
{
    public DoctrineContext(IArmyService armies, IBattleEngine battle, IGeneral general, IPlayerInput player, TracerContext? tracer = null)
    {
        Armies = armies; Battle = battle; General = general; Player = player; Tracer = tracer;
    }

    public IArmyService Armies { get; }
    public IBattleEngine Battle { get; internal set; }
    public IGeneral General { get; }
    public IPlayerInput Player { get; }
    /// <summary>The Tracer extension's own context object, if one was supplied (it is a GlobalContext of its own, so it is HELD here, not installed).</summary>
    public TracerContext? Tracer { get; }

    /// <summary>Where the round's offers come from (default <see cref="SeededOffers"/>; a test may stand in another). Set before a run, not during one.</summary>
    public IOfferSource Offers { get; set; } = SeededOffers.Instance;

    /// <summary>An optional watcher of stages and battle progress (see <see cref="IRoundObserver"/>). Null means nobody is watching. A host may set it at any time between runs.</summary>
    public IRoundObserver? Observer { get; set; }

    /// <summary>
    /// How to draw an arena, if the battle engine can say (<see cref="IArenaLayouts"/>; the Tracer section can). Null for an engine without the
    /// capability or an unknown id. Nothing here names a concrete engine.
    /// </summary>
    public global::Doctrine.Battle.ArenaLayout? LayoutOf(string arenaId) => (Battle as IArenaLayouts)?.LayoutOf(arenaId);

    /// <summary>Round feeds the next round: its last step runs this ref.</summary>
    public PipelineRef<RoundState> Round { get; } = new("Round");
    /// <summary>One whole battle: begin, tick until it ends, finish.</summary>
    public PipelineRef<BattleState> BattleRun { get; } = new("Battle");
    /// <summary>One tick of a battle.</summary>
    public PipelineRef<BattleState> BattleTick { get; } = new("BattleTick");
}

/// <summary>The stage slots: one name per section step. A real section is invisible here; these names appear in reports and traces.</summary>
public static class Stages
{
    // Round pipeline
    public const string OfferUnits = "Offer.Units";         // core:    the round's pack offer, scaled to the purse
    public const string ArmyDraft = "Army.Draft";           // army:    draft cohorts (offered packs only), create the enemy
    public const string OfferOrders = "Offer.Orders";       // core:    the order offer from the legal list the draft left, paid rerolls
    public const string GeneralSuggest = "General.Suggest"; // general: ghost doctrine from the token summary
    public const string PlayerChoose = "Player.Choose";     // player:  accept or override, mask enforced
    public const string GeneralImitate = "General.Imitate"; // general: learn the player's choice
    public const string ArmyOrders = "Army.Orders";         // army:    apply the army-order tokens (both sides)
    public const string BattleSetup = "Battle.Setup";       // core:    two reads + two doctrines -> BattleSetup
    public const string BattleRun = "Battle.Run";           // battle:  runs the BattlePipeline
    public const string ArmyLosses = "Army.Losses";         // army:    apply losses (both sides)
    public const string ArmySpoils = "Army.Spoils";         // army:    apply spoils
    public const string GeneralOutcome = "General.Outcome"; // general: learn the result
    public const string RoundClose = "Round.Close";         // core:    record the round, end it, decide whether the game goes on
    public const string RoundAdvance = "Round.Advance";     // core:    build the next round's data
    public const string RoundNext = "Round.Next";           // core:    the next round (this pipeline, by reference)
    // Battle pipeline
    public const string BattleBegin = "Battle.Begin";
    public const string BattleTicks = "Battle.Ticks";       // loops the tick pipeline until a win, loss or the tick limit
    public const string BattleFinish = "Battle.Finish";
    // Tick pipeline
    public const string BattleFieldOrders = "Battle.FieldOrders";
    public const string BattleMove = "Battle.Move";
    public const string BattleEngage = "Battle.Engage";
    public const string BattleApplyLosses = "Battle.ApplyLosses";
    public const string BattleRecord = "Battle.Record";
    public const string BattleJudge = "Battle.Judge";
}
