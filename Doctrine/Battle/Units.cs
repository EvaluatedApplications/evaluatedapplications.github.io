using System.Numerics;
using Tracer.Abstractions;
using Tracer.Models;

namespace Doctrine.Battle;

/// <summary>
/// Knobs of the Tracer battle section. The defaults are the game. Each switch exists because a Tracer service has to EARN its place:
/// turning it off must visibly change a battle (the tests flip each one and watch the difference).
/// </summary>
public sealed record TracerBattleOptions
{
    /// <summary>Most agents on the field, both sides together. A phone-feasible cap; cohort strength is soldiers, agents are a sample.</summary>
    public int MaxAgents { get; init; } = 300;
    /// <summary>Most agents one cohort shows.</summary>
    public int MaxAgentsPerCohort { get; init; } = 24;
    /// <summary>Hits need a clear line across the baked mesh (walls and a fallen gate block sight and shots).</summary>
    public bool LineOfSight { get; init; } = true;
    /// <summary>Cohorts only know where the enemy is when a fog-of-war grid says one of their agents is visible.</summary>
    public bool FogOfWar { get; init; } = true;
    /// <summary>Flank, Fall back and Guard read the influence map (territory) to choose where to go.</summary>
    public bool Influence { get; init; } = true;
    /// <summary>The Falling Gate arena drops its gate through the dynamic-obstacle service.</summary>
    public bool DynamicObstacles { get; init; } = true;
}

/// <summary>An agent of the battle: Tracer's <see cref="GameAgent"/> plus the integer facts the deterministic resolver needs.</summary>
internal sealed record UnitAgent(
    int Id, Vector2 Position, float AttackRange, int Cohort, int DamageFp, int TakenPct, Trait Traits)
    : GameAgent(Id, Position, Position, Radius: 0.35f, MaxSpeed: 0f, GoalChanged: false, Attack: DamageFp, Defense: TakenPct,
                Health: 1f, MaxHealth: 1f, AttackSpeed: 1f, AttackRange: AttackRange, MaxAttacksPerTick: 1);

/// <summary>What a kind of soldier does on the field, in plain numbers (traits decide; the roster owns the traits).</summary>
public static class UnitRules
{
    /// <summary>Damage is counted in 1/600 of a soldier: one soldier at power 100 deals 100 units a tick (a sixth of a soldier).</summary>
    public const int Fp = 600;
    /// <summary>The largest single hit, in 1/600 soldiers (keeps a hit exact in a float and every sum inside a long).</summary>
    public const int MaxHit = 8_000_000;

    public static float Speed(Trait t)
    {
        float s = 2.0f;
        if ((t & Trait.Swift) != 0) s *= 1.5f;
        if ((t & Trait.Heavy) != 0) s *= 0.7f;
        if ((t & Trait.Machine) != 0) s *= 0.8f;
        return s;
    }

    public static float Range(Trait t) => (t & Trait.Machine) != 0 ? 10f : (t & Trait.Ranged) != 0 ? 7f : 2.2f;

    /// <summary>Percent of base damage this kind deals per soldier.</summary>
    public static int DamagePct(Trait t)
    {
        int p = 100;
        if ((t & Trait.Ranged) != 0) p = p * 70 / 100;
        if ((t & Trait.Machine) != 0) p = p * 230 / 100;
        if ((t & Trait.Cursed) != 0) p = p * 115 / 100;
        if ((t & Trait.Beast) != 0) p = p * 110 / 100;
        return p;
    }

    /// <summary>Percent of incoming damage this kind takes.</summary>
    public static int TakenPct(Trait t)
    {
        int p = 100;
        if ((t & Trait.Shielded) != 0) p = p * 75 / 100;
        if ((t & Trait.Heavy) != 0) p = p * 90 / 100;
        return p;
    }

    /// <summary>One hit, in 1/600 soldiers. Cover cuts it by a third; the holy hurt the cursed half again.</summary>
    public static int Hit(int attackerDamageFp, Trait attacker, int targetTakenPct, Trait target, bool targetInCover)
    {
        long d = (long)attackerDamageFp * targetTakenPct / 100;
        if (targetInCover) d = d * 2 / 3;
        if ((attacker & Trait.Holy) != 0 && (target & Trait.Cursed) != 0) d = d * 3 / 2;
        return (int)Math.Clamp(d, 1L, MaxHit);
    }
}

/// <summary>
/// Decorates one of Tracer's behaviours so it only competes for the agents it is meant for. Tracer's NavigatorService scores EVERY
/// behaviour for EVERY agent (there is no per-group binding), and a built-in such as <c>PatrolBehavior</c> scores a constant, so the
/// gate is what makes "this cohort holds, that one advances" expressible with the real built-ins.
/// </summary>
internal sealed class GatedBehavior(INavigationBehavior inner, Func<int, bool> member, float boost) : INavigationBehavior
{
    public string Name => inner.Name;
    public float Score(AgentDto agent, NavigationContext ctx) => member(agent.Id) ? inner.Score(agent, ctx) + boost : 0f;
    public BehaviorResult Evaluate(AgentDto agent, NavigationContext ctx) => inner.Evaluate(agent, ctx);
}

/// <summary>
/// Team-aware flee. Tracer's <c>FleeBehavior</c> runs from the nearest AGENT of any side, so a cohort that fell back would run from its own
/// neighbours; this one runs to the retreat point the field decided from what the cohort knows of the enemy (a Tracer gap, see Tracer/todo).
/// </summary>
internal sealed class RetreatBehavior(Func<int, Vector2?> retreatPoint) : INavigationBehavior
{
    public string Name => "Retreat";
    public float Score(AgentDto agent, NavigationContext ctx) => retreatPoint(agent.Id) is null ? 0f : 5f;
    public BehaviorResult Evaluate(AgentDto agent, NavigationContext ctx)
        => new(retreatPoint(agent.Id) ?? agent.Position, Priority: 2f, ForceRepath: true);
}
