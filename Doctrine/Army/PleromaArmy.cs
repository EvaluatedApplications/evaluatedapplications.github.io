using System.Text;
using Phasor;

namespace Doctrine.Army;

// ONE ARMY as Pleroma stores. Nothing is held in a host field except the Side and the store handles.
//   _army   the cohorts: one item per kind (Key), traits (Flags), slot (Arrangement), veterancy (Time), soldiers (count).
//   _purse  gold: the count of key 0.
//   _clock  the round: the count of key 0.
//   _flips  the flip record: one flag word per flip; BitCounts parity is the army's flip mask.
//   _auras  active auras: key = trait bit, count = percent.
// Every change is a Pleroma operation: Add (a negative count is a removal), Tick, RelabelFlags, RelabelArrangements, Clear.
// Every read is a Pleroma read: TryItems, TryKeys, Total, BitCounts, CountWhere, InnerProduct over a keys-only projection.
// The host decides WHICH operation to run and reads the numbers it needs; it never adds or multiplies a count itself.

internal sealed class PleromaArmy : IArmy
{
    readonly Pleroma _army, _purse, _clock, _flips, _auras;

    PleromaArmy(Side side)
    {
        Side = side;
        _army = new Pleroma(PleromaArmyLayout.ArmyShape);
        _purse = new Pleroma(PleromaArmyLayout.PurseShape);
        _clock = new Pleroma(PleromaArmyLayout.ClockShape);
        _flips = new Pleroma(PleromaArmyLayout.FlipShape);
        _auras = new Pleroma(PleromaArmyLayout.AuraShape);
    }

    public Side Side { get; }

    /// <summary>The army's own store (tests compare it cell for cell with a rebuilt model).</summary>
    internal Pleroma Army => _army;

    // ---- construction ----

    /// <summary>A fresh army: the starting levy is <see cref="PleromaArmyLayout.StartSoldiers"/> draws of the Pleroma's own SampleKey on the game's SplitMix64.</summary>
    internal static PleromaArmy Fresh(Side side, int seed)
    {
        var army = new PleromaArmy(side);
        var levy = new Pleroma(PleromaArmyLayout.KeysShape);
        for (int k = 0; k < DoctrineVocabulary.Kinds; k++) levy.Add((ulong)k, PleromaArmyLayout.LevyWeight[k]);
        var rng = new GameRandom(new GameRng(GameRng.Mix(seed, (int)side)));
        for (int i = 0; i < PleromaArmyLayout.StartSoldiers; i++)
        {
            int kind = (int)levy.SampleKey(rng);
            army._army.Add(army.ItemForNewCohort(kind), 1);
        }
        army._purse.Add(0UL, PleromaArmyLayout.StartGold);
        return army;
    }

    /// <summary>Rebuilds an army from the values an army code carries. Every store is filled by Add, so it equals the original cell for cell.</summary>
    internal static PleromaArmy Rebuild(Side side, int round, int gold, int flipMask, IReadOnlyList<(int Bit, int Percent)> auras,
        IReadOnlyList<(int Kind, int Soldiers, int Slot, int Veterancy)> cohorts)
    {
        var a = new PleromaArmy(side);
        if (gold > 0) a._purse.Add(0UL, gold);
        if (round > 0) a._clock.Add(0UL, round);
        for (int j = 0; j < DoctrineVocabulary.Traits; j++)
            if (((flipMask >> j) & 1) != 0) a._flips.Add(new HologramItem(0, 1u << j), 1);
        foreach (var (bit, pct) in auras) a._auras.Add(new HologramItem((ulong)bit), pct);
        foreach (var c in cohorts)
            a._army.Add(new HologramItem((ulong)c.Kind, (uint)Roster.TraitsOf(c.Kind) ^ (uint)flipMask,
                PleromaArmyLayout.SlotArrangement[c.Slot], c.Veterancy), c.Soldiers);
        return a;
    }

    // ---- reads (no mutation) ----

    /// <summary>The cohorts as Pleroma items, sorted by kind. False when the army cannot be read (more distinct kinds than the command span).</summary>
    bool TryCohorts(out IReadOnlyList<(HologramItem Item, long Count)> items) => _army.TryItems(out items);

    static bool Find(IReadOnlyList<(HologramItem Item, long Count)> items, int kind, out (HologramItem Item, long Count) found)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].Item.Key == (ulong)kind) { found = items[i]; return true; }
        found = default;
        return false;
    }

    /// <summary>The army's flip mask: bit j is set when bit j has been flipped an odd number of times (BitCounts parity).</summary>
    int FlipMask()
    {
        var bits = _flips.BitCounts();
        int mask = 0;
        for (int j = 0; j < bits.Length && j < DoctrineVocabulary.Traits; j++)
            if ((bits[j] & 1) != 0) mask |= 1 << j;
        return mask;
    }

    /// <summary>The active auras (sorted by bit) and the strongest percent among them (the shared bonus, as in the stub).</summary>
    (IReadOnlyList<(int Bit, int Percent)> List, int Mask, int MaxPercent) Auras()
    {
        var list = new List<(int, int)>();
        int mask = 0, max = 0;
        if (_auras.TryKeys(out var keys))
            for (int i = 0; i < keys.Count; i++)
            {
                int bit = (int)keys[i].Key, pct = (int)keys[i].Count;
                list.Add((bit, pct));
                mask |= 1 << bit;
                max = Math.Max(max, pct);
            }
        return (list, mask, max);
    }

    /// <summary>Gold a Flip of <paramref name="bit"/> costs: the sum over the cohorts of soldiers x <see cref="Roster.RelabelCost"/> (read from the cohorts' live traits).</summary>
    static long FlipCost(IReadOnlyList<(HologramItem Item, long Count)> items, int bit)
    {
        long cost = 0;
        for (int i = 0; i < items.Count; i++) cost += items[i].Count * Roster.RelabelCost((Trait)(byte)items[i].Item.Flags, bit);
        return cost;
    }

    int Round() => (int)Math.Clamp(_clock.Total(), 0, int.MaxValue);
    int Gold() => (int)Math.Clamp(_purse.Total(), 0, int.MaxValue);

    /// <summary>Legal doctrine tokens, derived from Pleroma reads: what is readable, what has soldiers, what is open.</summary>
    LegalMask Legal()
    {
        var m = LegalMask.None;
        if (!TryCohorts(out var items)) return m;              // unreadable: End only
        if (_army.Total() == 0) return m;                      // no soldiers: nothing to order
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1)).With(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2));
        long gold = Gold();
        for (int b = 0; b < DoctrineVocabulary.Traits; b++)
            if (FlipCost(items, b) <= gold) m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b));   // a flip is legal while the army can pay for it
        m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Age));
        for (int k = 0; k < DoctrineVocabulary.Kinds; k++)
            if (Find(items, k, out _) && Find(items, (k + 1) % DoctrineVocabulary.Kinds, out _))
                m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
        var auras = Auras();
        for (int b = 0; b < DoctrineVocabulary.Traits; b++)
            if (((auras.Mask >> b) & 1) == 0 && _army.CountWhere(PleromaArmyLayout.HasTrait[b]) > 0)
                m = m.With(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b));
        // Cull is never legal on this shape: Restrict refuses on a store with item lines and order and time faces (Phasor CG1).
        var occupied = new bool[DoctrineVocabulary.Groups];
        for (int i = 0; i < items.Count; i++)
        {
            int slot = PleromaArmyLayout.SlotOf(items[i].Item.Arrangement);
            if (slot >= 0) occupied[slot] = true;
        }
        for (int g = 0; g < DoctrineVocabulary.Groups; g++)
            if (occupied[g])
                for (int f = 0; f <= (int)FieldOrderKind.Hunt; f++) m = m.With(DoctrineVocabulary.Field((FieldOrderKind)f, g));
        return m;
    }

    public ArmyReadModel Read()
    {
        var auras = Auras();
        var cohorts = new List<CohortRead>();
        if (TryCohorts(out var items))
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i].Item;
                int vet = it.Time, slot = PleromaArmyLayout.SlotOf(it.Arrangement);
                cohorts.Add(new CohortRead((int)it.Key, (int)items[i].Count, (Trait)(byte)it.Flags, slot, vet,
                    PleromaArmyLayout.Power(vet, it.Flags, auras.Mask, auras.MaxPercent)));
            }
        return new ArmyReadModel(Side, Round(), Gold(), cohorts, Legal());
    }

    public int Score()
    {
        // Sum of count x price x power over the cohorts, exact in the Pleroma: the army's keys-only projection
        // against a weight store (InnerProduct is the key pairing), plus 100 per gold so both sides share the percent scale.
        var auras = Auras();
        var weights = new long[PleromaArmyLayout.KeyCycle];
        if (TryCohorts(out var items))
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i].Item;
                weights[(int)it.Key] = Roster.PriceOf((int)it.Key) * PleromaArmyLayout.Power(it.Time, it.Flags, auras.Mask, auras.MaxPercent);
            }
        long units = Pleroma.InnerProduct(_army.Project(HologramFaces.None), Pleroma.FromCounts(PleromaArmyLayout.KeysShape, weights));
        return (int)Math.Clamp(units + 100L * Gold(), 0, int.MaxValue);
    }

    public string ToCode()
    {
        var sb = new StringBuilder("DA2.");
        sb.Append((int)Side).Append('.').Append(Round()).Append('.').Append(Gold()).Append('.').Append(FlipMask()).Append('.');
        var auras = Auras();
        for (int i = 0; i < auras.List.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(auras.List[i].Bit).Append(':').Append(auras.List[i].Percent);
        }
        sb.Append('.');
        if (TryCohorts(out var items))
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var it = items[i].Item;
                sb.Append((int)it.Key).Append(':').Append(items[i].Count).Append(':')
                  .Append(PleromaArmyLayout.SlotOf(it.Arrangement)).Append(':').Append(it.Time);
            }
        return sb.ToString();
    }

    // ---- writes (Pleroma operations only) ----

    /// <summary>A cohort item for a kind not yet in the army: its traits under the current flip mask, slot = kind mod 3 (the stub's seeding), veterancy 0.</summary>
    HologramItem ItemForNewCohort(int kind)
        => new((ulong)kind, (uint)Roster.TraitsOf(kind) ^ (uint)FlipMask(), PleromaArmyLayout.SlotArrangement[kind % DoctrineVocabulary.Groups], 0);

    public ValueTask<DraftReport> DraftAsync(DraftRequest request, CancellationToken ct)
    {
        var ok = new List<DraftPick>();
        var no = new List<(DraftPick Pick, string Reason)>();
        long spent = 0;
        foreach (var p in request.Picks)
        {
            if ((uint)p.Kind >= Roster.Size || p.Count <= 0) { no.Add((p, "bad pick")); continue; }
            if (!TryCohorts(out var items)) { no.Add((p, "army unreadable")); continue; }
            bool present = Find(items, p.Kind, out var have);
            if (!present && items.Count >= PleromaArmyLayout.CommandSpan) { no.Add((p, "command span")); continue; }
            long cost = (long)Roster.PriceOf(p.Kind) * p.Count;
            if (cost > Gold()) { no.Add((p, "gold")); continue; }
            _purse.Add(0UL, -cost);
            _army.Add(present ? have.Item : ItemForNewCohort(p.Kind), p.Count);
            spent += cost;
            ok.Add(p);
        }
        return ValueTask.FromResult(new DraftReport(ok, no, (int)spent));
    }

    public ValueTask<ArmyOrdersReport> ApplyArmyOrdersAsync(DoctrineSet doctrine, CancellationToken ct)
    {
        int applied = 0, skipped = 0;
        for (int slot = 0; slot < DoctrineVocabulary.MaxSlots; slot++)
        {
            int token = doctrine[slot];
            if (token == 0) continue;                                    // an empty slot
            if (!DoctrineVocabulary.IsValid(token)) { skipped++; continue; }
            var order = DoctrineVocabulary.Decode(token);
            if (order.Family != OrderFamily.Army) continue;             // field orders belong to the battle
            // The legal list is read fresh before each token, so an order that makes a later one impossible is seen.
            if (!Legal().Allows(token)) { skipped++; continue; }
            Perform(order);
            applied++;
        }
        return ValueTask.FromResult(new ArmyOrdersReport(applied, skipped));
    }

    void Perform(DoctrineOrder o)
    {
        switch ((ArmyOrderKind)o.Kind)
        {
            case ArmyOrderKind.Rotate:
                _army.RelabelArrangements(PleromaArmyLayout.RotationFactor(o.A), 0);
                break;
            case ArmyOrderKind.Flip:
                if (TryCohorts(out var flipItems)) _purse.Add(0UL, -FlipCost(flipItems, o.A));   // the legal mask already checked the army can pay
                _army.RelabelFlags(null, 1u << o.A);
                _flips.Add(new HologramItem(0, 1u << o.A), 1);
                break;
            case ArmyOrderKind.Age:
                _army.Tick(1);
                break;
            case ArmyOrderKind.Merge:
                MergeInto(o.A);
                break;
            case ArmyOrderKind.Aura:
                // An aura is worth the soldiers carrying its trait, capped. Legal only while it is not already active.
                _auras.Add(new HologramItem((ulong)o.A), Math.Min(PleromaArmyLayout.AuraCap, _army.CountWhere(PleromaArmyLayout.HasTrait[o.A])));
                break;
        }
    }

    /// <summary>
    /// Merge kind k+1 into kind k. Phasor has no operation that adds one key's counts onto another key (Bind multiplies counts), so
    /// this is a move: the count of k+1 is read from the store and removed with Add(-n), then added to k with Add(+n). Phasor gap AG1.
    /// </summary>
    void MergeInto(int k)
    {
        int from = (k + 1) % DoctrineVocabulary.Kinds;
        if (!TryCohorts(out var items) || !Find(items, k, out var into) || !Find(items, from, out var moving)) return;
        _army.Add(moving.Item, -moving.Count);
        _army.Add(into.Item, moving.Count);
    }

    public ValueTask<int> ApplyLossesAsync(IReadOnlyList<CohortLoss> losses, CancellationToken ct)
    {
        int removed = 0;
        for (int i = 0; i < losses.Count; i++)
        {
            var l = losses[i];
            if ((uint)l.Key >= Roster.Size || l.Lost <= 0) continue;
            if (!TryCohorts(out var items) || !Find(items, l.Key, out var have)) continue;
            long take = Math.Min(have.Count, l.Lost);
            if (take <= 0) continue;
            _army.Add(have.Item, -take);
            removed += (int)take;
        }
        return ValueTask.FromResult(removed);
    }

    public ValueTask ApplySpoilsAsync(Spoils spoils, CancellationToken ct)
    {
        if (spoils.Gold > 0) _purse.Add(0UL, spoils.Gold);
        if (spoils.Experience > 0) _army.Tick(spoils.Experience);
        return ValueTask.CompletedTask;
    }

    public ValueTask EndRoundAsync(CancellationToken ct)
    {
        _clock.Add(0UL, 1);
        _auras.Clear();
        return ValueTask.CompletedTask;
    }
}
