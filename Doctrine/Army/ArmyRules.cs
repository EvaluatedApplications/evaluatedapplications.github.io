namespace Doctrine.Army;

/// <summary>
/// The economy rules of an army service. The defaults ARE the game as it ships; each switch exists so the style-balance harness can measure one
/// lever at a time (Doctrine\todo\difficulty-2026-10-19.md, second round) and so a mutant can break exactly one rule and watch the contract go red.
/// Both army sections (the Pleroma army and the reference stub) read the same record and price through the same <see cref="Roster"/> functions.
/// </summary>
public sealed record ArmyRules
{
    /// <summary>The game as it ships.</summary>
    public static ArmyRules Default { get; } = new();

    /// <summary>
    /// OFF in the game (third round, 2026-10-19: with it on, a mass of one-gold Spearmen became unbeatable). When ON an Age costs gold in proportion to the power
    /// it adds (<see cref="Roster.AgeWeight"/>) and is legal only while the army can pay. The rule, its contract case and its mutants are kept.
    /// </summary>
    public bool PriceAge { get; init; }

    /// <summary>OFF in the game (see <see cref="PriceAge"/>). When ON an Aura is rented for the round it lasts: gold in proportion to the power it adds, spread over the rounds a body would have served.</summary>
    public bool PriceAura { get; init; }

    /// <summary>The worth of a soldier includes the free fire a longer reach gets (<see cref="Battle.UnitRules.ReachPct"/>); it prices Flip, Age and Aura.</summary>
    public bool PriceReach { get; init; } = true;

    /// <summary>The purse pays <see cref="Roster.Interest"/> when a round closes (gold / 7).</summary>
    public bool BankInterest { get; init; } = true;

    /// <summary>
    /// ON in the game (third round 2026-10-19): a draft costs the soldier's worth (<see cref="Roster.TraitPrice"/>, read from the battle's stat tables) instead of the typed
    /// <see cref="Roster.TypedPrice"/> (Spearmen 1, Shieldwall 2, Riders 2, Archers 2, Priests 3, Wights 2, Wolves 2, Catapults 4). A plain soldier therefore costs 2: a body costs
    /// what it is worth. The scripted enemy and the scripted player size their picks with <see cref="Default"/>, so the enemy follows the same price rule.
    /// </summary>
    public bool DerivedDraftPrices { get; init; } = true;

    /// <summary>
    /// ON in the game: a new cohort joins the rank the balance rule picks (<see cref="Roster.BalancedLine"/>: the rank with the least of its class), so ranks are
    /// mixed on purpose. OFF is the old seeding, kind mod 3 (an accident of kind numbers: Spearmen, Archers and Wolves all in front), kept so old digests reproduce.
    /// </summary>
    public bool BalancedPlacement { get; init; } = true;

    /// <summary>The rank a new cohort of <paramref name="kind"/> with the live <paramref name="traits"/> joins, given the cohorts already standing.</summary>
    public int LineFor(int kind, Trait traits, IEnumerable<(Trait Traits, int Count, int Slot)> standing)
        => BalancedPlacement ? Roster.BalancedLine(traits, standing) : kind % DoctrineVocabulary.Groups;

    /// <summary>What one soldier of <paramref name="kind"/> costs to draft under these rules.</summary>
    public int DraftPrice(int kind) => DerivedDraftPrices ? Roster.TraitPrice(Roster.TraitsOf(kind), PriceReach) : Roster.TypedPrice(kind);
}
