using Doctrine.Army;
using Phasor;

namespace Doctrine;

// THE CAMPAIGN STATE AS PLEROMA STORES, the way the army is held (Army\PleromaArmy.cs). Nothing is kept in a host field except the store handles.
//   _player, _neutral, _enemy   the provinces by holder: item = province (Key = lane x 12 + index, or a capital), traits = Flags (feature in bits 0..3,
//                               arena index in bits 4..6), count = the province's YIELD. Taking a province is two Adds: -yield from the loser's store, +yield to the winner's.
//   _standards                  the count of key 0 (a complete one-key band, like the army's purse: a restore is MapCounts, a clamp the Pleroma applies).
//   _fields                     fields won: the count of key 0.
//   _ring                       battles won on a RING of five places (the veterancy idea): Tick(1) per win, a wrap to 0 is every fifth win.
//   _clock                      the epoch: the count of key 0.
// Reads are Pleroma reads: TryItems (the provinces), Total (gold = the sum of the held yields), CountWhere (a held feature), TryItems Time (the ring).
// The host decides WHICH operation to run (which lane's next province, who held it) and reads the numbers it needs; it never adds up a count.
// Phasor gaps met: none. A per-lane "next province" is a read of the stores by key order (keys are lane x 12 + index), not an operation of its own.

internal sealed class CampaignStore
{
    public const int MaxStandards = 3, RestoreEvery = 5;
    const int KeyCycle = 105;                       // 3 x 5 x 7 divides 2^61 - 2; at least 2K + 1 = 81 for K = 40

    static HologramShape MapShape => new() { KeyCapacity = 40, FlagBits = 7, CompleteKeys = true, KeyCycle = KeyCycle };
    static HologramShape OneKey => new() { KeyCapacity = 1 };
    static HologramShape RingShape => new() { KeyCapacity = 1, TimePeriod = RestoreEvery };

    static readonly Feature[] Features = [Feature.Barracks, Feature.Stables, Feature.Shrine, Feature.Granary];
    static readonly FlagPredicate[] HasFeature = Features.Select(f => FlagPredicate.Compile(7, x => (x & (uint)f) != 0u)).ToArray();

    readonly Pleroma _player = new(MapShape), _neutral = new(MapShape), _enemy = new(MapShape);
    Pleroma _standards = new(PleromaArmyLayout.PurseShape);
    readonly Pleroma _fields = new(OneKey), _ring = new(RingShape), _clock = new(OneKey);
    readonly CampaignProbe _probe;

    public CampaignStore(IReadOnlyList<ProvinceSpec> map, CampaignProbe probe)
    {
        _probe = probe;
        foreach (var s in map) Put(s);
        _standards.Add(0UL, MaxStandards);
        _ring.Add(new HologramItem(0), 1);
    }

    static HologramItem ItemOf(int key, Feature f, int arena) => new((ulong)key, (uint)f | ((uint)(arena & 7) << 4));
    Pleroma StoreOf(Holder h) => h switch { Holder.Player => _player, Holder.Enemy => _enemy, _ => _neutral };

    void Put(ProvinceSpec s) => StoreOf(s.Owner).Add(ItemOf(s.Key, s.Feature, s.ArenaIndex), s.Yield);

    /// <summary>A refill (endless): one more province appears, held by the enemy.</summary>
    public void Append(ProvinceSpec s) => Put(s);

    // ---- reads ----

    public IReadOnlyList<Province> Provinces()
    {
        var list = new List<Province>();
        foreach (var h in new[] { Holder.Player, Holder.Neutral, Holder.Enemy })
        {
            if (!StoreOf(h).TryItems(out var items)) throw new InvalidOperationException("the map store cannot be read (more provinces than the key capacity)");
            foreach (var (it, count) in items)
            {
                if (count <= 0) continue;
                int key = (int)it.Key;
                bool cap = MapKeys.IsCapital(key);
                list.Add(new Province(key, cap ? -1 : MapKeys.LaneOf(key), cap ? -1 : MapKeys.IndexOf(key), h, (Feature)(it.Flags & 15u), (int)((it.Flags >> 4) & 7u), (int)count, cap));
            }
        }
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    /// <summary>The features of the provinces the player holds NOW (a CountWhere on the player's store per feature).</summary>
    public Feature Unlocks()
    {
        var f = Feature.None;
        for (int i = 0; i < Features.Length; i++)
            if (_player.CountWhere(HasFeature[i]) > 0) f |= Features[i];
        return f;
    }

    /// <summary>The gold the held provinces pay at epoch close: the Total of the player's store (every count is a yield).</summary>
    public int Gold() => (int)Math.Clamp(_player.Total(), 0, int.MaxValue);

    public int Standards => (int)Math.Clamp(_standards.Total(), 0, int.MaxValue);
    public int FieldsWon => (int)Math.Clamp(_fields.Total(), 0, int.MaxValue);
    public int Epoch => (int)Math.Clamp(_clock.Total(), 0, int.MaxValue);

    /// <summary>The place on the ring of five (0 = a restore was just earned or nothing has been won): battles won mod 5.</summary>
    public int RingPosition => _ring.TryItems(out var it) && it.Count > 0 ? it[0].Item.Time : 0;

    // ---- writes (Pleroma operations only) ----

    /// <summary>Move a province to <paramref name="to"/>: Add(-yield) to the loser's store, Add(+yield) to the winner's.</summary>
    public void Transfer(Province p, Holder to)
    {
        var item = ItemOf(p.Key, p.Feature, p.ArenaIndex);
        StoreOf(p.Owner).Add(item, -p.Yield);
        StoreOf(to).Add(item, p.Yield);
    }

    /// <summary>A lost battle costs one standard (the probe <c>LossCostsNothing</c> breaks that). Conceding never calls this.</summary>
    public void LoseStandard() { if (!_probe.HasFlag(CampaignProbe.LossCostsNothing)) _standards.Add(0UL, -1); }

    /// <summary>A field won: fields +1, the ring ticks, and on a wrap to 0 (every fifth win) one standard is restored, clamped at <see cref="MaxStandards"/> by MapCounts.</summary>
    public bool WinField()
    {
        _fields.Add(0UL, 1);
        _ring.Tick(1);
        int before = Standards;
        bool wrapped = _probe.HasFlag(CampaignProbe.RestoreEveryFourth) ? FieldsWon % 4 == 0 : RingPosition == 0;
        if (wrapped)
            // a map over the whole band: only a standard that is held (count > 0) is restored, so the empty places of the band stay empty
            _standards = _probe.HasFlag(CampaignProbe.RestoreIgnoresMax) ? _standards.MapCounts(c => c <= 0 ? c : c + 1) : _standards.MapCounts(c => c <= 0 ? c : Math.Min(MaxStandards, c + 1));
        return Standards > before;
    }

    public void EndEpoch() => _clock.Add(0UL, 1);
}
