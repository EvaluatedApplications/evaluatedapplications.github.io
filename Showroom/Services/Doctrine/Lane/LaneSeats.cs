using Doctrine;
using Doctrine.Battle;
using Doctrine.Lane;

namespace ShowroomLane;

/// <summary>One pack bought: <see cref="LaneEconomy.PackSize"/> bodies of <see cref="Kind"/> for lane <see cref="Lane"/>.</summary>
public sealed record PackBuy(int Lane, int Kind);

/// <summary>One order for one lane (Advance, Hold, FallBack, Hunt or Guard).</summary>
public sealed record LaneOrderChoice(int Lane, FieldOrderKind Order);

/// <summary>What a seat wants to do at a pause: the packs, then the orders. The match prices it against the purse and applies both seats' plans together.</summary>
public sealed record LanePlan(IReadOnlyList<PackBuy> Packs, IReadOnlyList<LaneOrderChoice> Orders)
{
    public static LanePlan Empty { get; } = new(Array.Empty<PackBuy>(), Array.Empty<LaneOrderChoice>());
    public bool IsEmpty => Packs.Count == 0 && Orders.Count == 0;
    public bool SameAs(LanePlan other) => Packs.SequenceEqual(other.Packs) && Orders.SequenceEqual(other.Orders);
}

/// <summary>Everything a seat may look at when it plans: the whole board (both sides, nothing hidden), its own orders and purse.</summary>
public sealed record LaneSeatView(
    Side Side, int Seed, int Phase, int Tick, LaneSnapshot Field, IReadOnlyList<FieldOrderKind> Orders, int Balance, int OwnBodies, int BodyCap, int Lanes);

/// <summary>
/// A seat at the table: whoever plans one side's plays at a pause. The match asks it once per pause, at the moment the pause ends, and applies the answer. A human (the page) and a
/// bot implement it today; the general can take a seat later by implementing it.
/// </summary>
public interface ILaneSeat
{
    /// <summary>True when the pause must wait for this seat (its clock and its Ready button). A seat that is not human never holds the pause up.</summary>
    bool IsHuman { get; }
    ValueTask<LanePlan> PlanAsync(LaneSeatView view, CancellationToken ct);
}

/// <summary>
/// The human seat: the page stages packs and orders during a pause, the match takes them at the pause's end. Staging is refused outside a pause, over the purse, and for an
/// unknown lane or kind. One order per lane (a new one replaces the old).
/// </summary>
public sealed class ManualSeat : ILaneSeat
{
    readonly LaneEconomy _econ;
    readonly int _lanes;
    readonly LaneMatchProbe _probe;
    readonly List<PackBuy> _packs = new();
    readonly Dictionary<int, FieldOrderKind> _orders = new();
    internal Func<bool> Open { get; set; } = () => true;
    bool _frozen;

    internal ManualSeat(LaneEconomy econ, int lanes, LaneMatchProbe probe) { _econ = econ; _lanes = lanes; _probe = probe; }

    public bool IsHuman => true;
    public IReadOnlyList<PackBuy> Packs => _packs;
    public IReadOnlyDictionary<int, FieldOrderKind> Orders => _orders;
    /// <summary>The purse less what is staged.</summary>
    public int Remaining => _econ.Balance - _packs.Sum(p => LaneEconomy.Cost(p.Kind));
    /// <summary>True once the plays are locked in (online: you pressed Ready, so the plan you committed to cannot change). Clear at the next pause.</summary>
    public bool Frozen => _frozen;
    /// <summary>Locks the plan as it stands: further staging, taking back and orders are refused until the next pause.</summary>
    public void Freeze() => _frozen = true;

    /// <summary>What is staged right now, as a plan.</summary>
    public LanePlan CurrentPlan() => new(_packs.ToArray(), _orders.OrderBy(o => o.Key).Select(o => new LaneOrderChoice(o.Key, o.Value)).ToArray());

    bool Allowed => !_frozen && (_probe.HasFlag(LaneMatchProbe.PlaysAnyTime) || Open());

    /// <summary>Stages one pack. False with the reason in plain words when it is refused.</summary>
    public bool Buy(int lane, int kind, out string why)
    {
        why = "";
        if (_frozen) { why = "You are ready. Your plays are locked in."; return false; }
        if (!Allowed) { why = "Plays can only be made at the pause."; return false; }
        if ((uint)lane >= (uint)_lanes) { why = "No such lane."; return false; }
        if ((uint)kind >= (uint)Roster.Size) { why = "No such soldiers."; return false; }
        if (LaneEconomy.Cost(kind) > Remaining && !_probe.HasFlag(LaneMatchProbe.OverBudgetAccepted)) { why = "Not enough points left."; return false; }
        _packs.Add(new PackBuy(lane, kind));
        return true;
    }

    /// <summary>Takes one staged pack back out (the points come back).</summary>
    public bool RemovePack(int index)
    {
        if (!Allowed || (uint)index >= (uint)_packs.Count) return false;
        _packs.RemoveAt(index);
        return true;
    }

    /// <summary>Stages an order for a lane; the same order again takes it back.</summary>
    public bool Order(int lane, FieldOrderKind order, out string why)
    {
        why = "";
        if (_frozen) { why = "You are ready. Your plays are locked in."; return false; }
        if (!Allowed) { why = "Plays can only be made at the pause."; return false; }
        if ((uint)lane >= (uint)_lanes) { why = "No such lane."; return false; }
        if (order is not (FieldOrderKind.Advance or FieldOrderKind.Hold or FieldOrderKind.FallBack or FieldOrderKind.Hunt or FieldOrderKind.Guard)) { why = "That order is not used in a lane."; return false; }
        if (_orders.TryGetValue(lane, out var cur) && cur == order) _orders.Remove(lane); else _orders[lane] = order;
        return true;
    }

    /// <summary>Forgets everything staged (a new pause starts clean).</summary>
    public void Clear() { _packs.Clear(); _orders.Clear(); _frozen = false; }

    public ValueTask<LanePlan> PlanAsync(LaneSeatView view, CancellationToken ct) => ValueTask.FromResult(CurrentPlan());
}

/// <summary>
/// The opponent: deterministic from the match seed (same seed and board, same plan). It spends its whole purse at each pause on packs, putting each where the other side is
/// strongest and leaning toward soldiers that counter what stands there, then sets one order per lane from how the lane is going. Nothing clever.
/// </summary>
public sealed class LaneBot : ILaneSeat
{
    readonly LaneMatchProbe _probe;
    public LaneBot() { }
    internal LaneBot(LaneMatchProbe probe) { _probe = probe; }

    public bool IsHuman => false;

    public ValueTask<LanePlan> PlanAsync(LaneSeatView v, CancellationToken ct)
    {
        ulong stream = _probe.HasFlag(LaneMatchProbe.BotUnseeded) ? (ulong)Random.Shared.NextInt64()
            : GameRng.Mix(_probe.HasFlag(LaneMatchProbe.BotIgnoresSeed) ? 0 : v.Seed * 31 + (int)v.Side, v.Phase) ^ 0xB07B07UL;
        var rng = new GameRng(stream);
        int lanes = v.Lanes;
        var own = new int[lanes]; var foe = new int[lanes]; var foeClass = new int[lanes, UnitRules.KindClassCount];
        foreach (var u in v.Field.Units)
        {
            if (u.Side == v.Side) own[u.Lane]++;
            else { foe[u.Lane]++; foeClass[u.Lane, (int)UnitRules.ClassOf(Roster.TraitsOf(u.Kind))]++; }
        }

        var packs = new List<PackBuy>();
        int purse = v.Balance, room = v.BodyCap - v.OwnBodies;
        while (purse >= LaneEconomy.CheapestPack && room >= LaneEconomy.PackSize)
        {
            // the lane that needs it most: where the other side is ahead, a little noise to break ties
            int lane = 0; double best = double.MinValue;
            for (int l = 0; l < lanes; l++)
            {
                double score = foe[l] * 2.0 - own[l] + (rng.NextU64() % 200) / 100.0;
                if (score > best) { best = score; lane = l; }
            }
            // soldiers that counter the commonest class standing there (two draws in three), otherwise any the purse allows
            var affordable = new List<int>();
            for (int k = 0; k < Roster.Size; k++) if (LaneEconomy.Cost(k) <= purse) affordable.Add(k);
            if (affordable.Count == 0) break;
            var pool = affordable;
            if (foe[lane] > 0 && rng.NextU64() % 3 != 0)
            {
                int top = 0;
                for (int c = 1; c < UnitRules.KindClassCount; c++) if (foeClass[lane, c] > foeClass[lane, top]) top = c;
                int counter = (top + UnitRules.KindClassCount - 1) % UnitRules.KindClassCount;   // the class that counters `top` is the one before it in the ring
                var counters = affordable.Where(k => (int)UnitRules.ClassOf(Roster.TraitsOf(k)) == counter).ToList();
                if (counters.Count > 0) pool = counters;
            }
            int kind = pool[(int)(rng.NextU64() % (ulong)pool.Count)];
            packs.Add(new PackBuy(lane, kind));
            purse -= LaneEconomy.Cost(kind); room -= LaneEconomy.PackSize; own[lane] += LaneEconomy.PackSize;
        }

        var orders = new List<LaneOrderChoice>();
        for (int l = 0; l < lanes; l++)
        {
            var want = foe[l] == 0 ? FieldOrderKind.Advance
                     : own[l] * 2 < foe[l] ? FieldOrderKind.Hold
                     : own[l] > foe[l] * 2 ? FieldOrderKind.Hunt
                     : FieldOrderKind.Advance;
            if (want != v.Orders[l]) orders.Add(new LaneOrderChoice(l, want));
        }
        return ValueTask.FromResult(new LanePlan(packs, orders));
    }
}
