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

    /// <summary>An Age costs gold in proportion to the power it adds (<see cref="Roster.AgeWeight"/>), and is legal only while the army can pay.</summary>
    public bool PriceAge { get; init; } = true;

    /// <summary>An Aura is rented for the round it lasts: gold in proportion to the power it adds, spread over the rounds a body would have served.</summary>
    public bool PriceAura { get; init; } = true;

    /// <summary>The worth of a soldier includes the free fire a longer reach gets (<see cref="Battle.UnitRules.ReachPct"/>); it prices Flip, Age and Aura.</summary>
    public bool PriceReach { get; init; } = true;

    /// <summary>The purse pays <see cref="Roster.Interest"/> when a round closes (gold / 7).</summary>
    public bool BankInterest { get; init; } = true;

    /// <summary>
    /// OFF in the shipped game (the draft prices are the game's feel and the user's to change): a draft costs the soldier's worth instead of
    /// <see cref="Roster.PriceOf"/>. The scripted enemy still sizes its picks with the typed prices, so this is a measurement switch until the enemy reads prices too.
    /// </summary>
    public bool DerivedDraftPrices { get; init; }

    /// <summary>What one soldier of <paramref name="kind"/> costs to draft under these rules.</summary>
    public int DraftPrice(int kind) => DerivedDraftPrices ? Roster.TraitPrice(Roster.TraitsOf(kind), PriceReach) : Roster.PriceOf(kind);
}
