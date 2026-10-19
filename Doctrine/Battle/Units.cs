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
    /// <summary>A class hits the class it counters half again as hard (<see cref="UnitRules.Counters"/>: Plain over Swift over Ranged over Armoured over Plain).</summary>
    public bool Counters { get; init; } = true;
    /// <summary>When the tick limit stops a running battle the side with the greater <see cref="UnitRules.Rating"/> wins (power left), not the side with more soldiers.</summary>
    public bool LimitByPower { get; init; } = true;
    /// <summary>Test hook of the balance harness: flips ONE ordering of the battle to see whether it carries a side lean. None = the game.</summary>
    internal OrderProbe Probe { get; init; }
}

/// <summary>Each switch reverses one ordering that the two sides do not share (see Doctrine\todo\balance-harness-2026-10-19.md). Default None.</summary>
[Flags]
internal enum OrderProbe
{
    None = 0,
    /// <summary>Hand the engagement service the defenders as its "attackers" list.</summary>
    EngagementSwap = 1,
    /// <summary>Build the cohort list defenders first (jitter streams and flank tie keep the attackers-first ordinal).</summary>
    SpawnReversed = 2,
    /// <summary>Build the navigator's free-agent list defenders first.</summary>
    NavigatorReversed = 4,
    /// <summary>Flip which role owns the right of way at an index (unit id parity).</summary>
    IdParityFlip = 8,
    /// <summary>Bake the arenas as before the mirror fix: every cell split along the same diagonal, so the mesh is not its own reflection.</summary>
    LegacyMesh = 16,
    /// <summary>Mutant of the loss trigger: it never fires.</summary>
    TriggerNever = 32,
    /// <summary>Mutant of the loss trigger: it fires at the first tick, whatever was lost.</summary>
    TriggerAtStart = 64,
    /// <summary>Mutant of the loss trigger: it reads the ENEMY's losses instead of the side's own.</summary>
    TriggerReadsEnemy = 128,
    /// <summary>Mutant of the counter cycle: each class hits the class that counters IT (the cycle read backwards).</summary>
    CounterReversed = 256,
    /// <summary>Mutant of the tick-limit rule: it counts soldiers even when LimitByPower is on.</summary>
    LimitCountsBodies = 512,
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

    /// <summary>The reach of a soldier with no weapon trait, in arena units.</summary>
    public const float MeleeRange = 2.2f;
    /// <summary>The pace of a soldier with no speed trait, in arena units per tick.</summary>
    public const float BaseSpeed = 2.0f;
    /// <summary>The damage of one soldier at power 100 per tick, in 1/600 soldiers (<see cref="Fp"/> / 6: a soldier falls to six ticks of one soldier's fire).</summary>
    public const int BaseDamage = 100;

    public static float Speed(Trait t)
    {
        float s = BaseSpeed;
        if ((t & Trait.Swift) != 0) s *= 1.5f;
        if ((t & Trait.Heavy) != 0) s *= 0.7f;
        if ((t & Trait.Machine) != 0) s *= 0.8f;
        return s;
    }

    public static float Range(Trait t) => (t & Trait.Machine) != 0 ? 10f : (t & Trait.Ranged) != 0 ? 7f : MeleeRange;

    /// <summary>
    /// What a longer reach is worth, in percent of a soldier's damage-over-durability: the free fire it gets while a melee unit closes on it.
    /// Both sides advance, so the gap closes at twice the base speed; the longer weapon fires free for (range - melee range) / (2 x base speed) ticks, and each
    /// tick of fire is <see cref="BaseDamage"/> of the <see cref="Fp"/> that kill a soldier. Archers and Priests (range 7) 20 percent, Machines (range 10) 32 percent.
    /// Integer arithmetic on tenths of a unit, so the figure is exact on every device.
    /// </summary>
    public static int ReachPct(Trait t)
    {
        long tenths = (long)MathF.Round(Range(t) * 10f) - (long)MathF.Round(MeleeRange * 10f);
        if (tenths <= 0) return 0;
        long closing = (long)MathF.Round(2f * BaseSpeed * 10f);          // tenths of a unit per tick, both sides advancing
        return (int)(tenths * BaseDamage * 100 / (closing * Fp));
    }

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

    /// <summary>The factor of a counter, as a fraction: the holy against the cursed and a class against the class it counters both hit half again as hard (one shape, named once).</summary>
    public const int CounterNum = 3, CounterDen = 2;

    /// <summary>
    /// What a kind of soldier is GOOD AT, read from the stat tables above (never typed per kind): the largest of three edges over the plain soldier, each in percent.
    /// Swift = speed over the base pace, Ranged = the free fire of its reach (<see cref="ReachPct"/>), Armoured = the damage it does not take (100 / taken - 100).
    /// A kind with no positive edge is Plain. A kind with several (Catapults are Ranged and Heavy, Shieldwall is Heavy and Shielded) is the class of its largest edge,
    /// the first of Swift, Ranged, Armoured on a tie. Damage-only traits (Cursed, Beast, Holy) are not an edge in this sense: they change the numbers, not the manner of fighting.
    /// </summary>
    public static KindClass ClassOf(Trait t)
    {
        int swift = (int)MathF.Round(Speed(t) * 100f / BaseSpeed) - 100, ranged = ReachPct(t), armoured = 10000 / Math.Max(1, TakenPct(t)) - 100;
        KindClass best = KindClass.Plain; int top = 0;
        if (swift > top) { best = KindClass.Swift; top = swift; }
        if (ranged > top) { best = KindClass.Ranged; top = ranged; }
        if (armoured > top) { best = KindClass.Armoured; top = armoured; }
        return best;
    }

    /// <summary>
    /// The counter cycle: Plain counters Swift (a braced line breaks a charge), Swift counters Ranged (it closes the gap the shooter lives on),
    /// Ranged counters Armoured (the slow and the shielded are worn down at a distance), Armoured counters Plain (shields turn an unarmoured soldier's thrusts).
    /// Each class counters exactly the next one in <see cref="KindClass"/> order, round the ring: a real cycle with no class that is safe against all.
    /// </summary>
    public static bool Counters(Trait attacker, Trait target)
        => (int)ClassOf(target) == ((int)ClassOf(attacker) + 1) % KindClassCount;
    public const int KindClassCount = 4;

    /// <summary>
    /// What a cohort is worth on the field, on one scale for every kind: soldiers x power x damage x (100 + reach) / taken. It is the same quantity <c>Roster.TraitPrice</c>
    /// rounds up to gold (worth), without the rounding and with the cohort's veterancy and aura in. Used where a count of bodies would reward the cheapest body (the tick limit).
    /// </summary>
    public static long Rating(int soldiers, int powerPct, Trait t)
        => (long)soldiers * powerPct * DamagePct(t) * (100 + ReachPct(t)) / Math.Max(1, TakenPct(t));

    /// <summary>One hit, in 1/600 soldiers. Cover cuts it by a third; the holy hurt the cursed half again; with <paramref name="counters"/> a class hurts the class it counters half again.</summary>
    public static int Hit(int attackerDamageFp, Trait attacker, int targetTakenPct, Trait target, bool targetInCover, bool counters = false, bool counterReversed = false)
    {
        long d = (long)attackerDamageFp * targetTakenPct / 100;
        if (targetInCover) d = d * 2 / 3;
        if ((attacker & Trait.Holy) != 0 && (target & Trait.Cursed) != 0) d = d * CounterNum / CounterDen;
        if (counters && (counterReversed ? Counters(target, attacker) : Counters(attacker, target))) d = d * CounterNum / CounterDen;
        return (int)Math.Clamp(d, 1L, MaxHit);
    }
}

/// <summary>How a kind fights, from its stat table (see <see cref="UnitRules.ClassOf"/>). The order is the counter cycle.</summary>
public enum KindClass : byte { Plain = 0, Swift = 1, Ranged = 2, Armoured = 3 }

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
