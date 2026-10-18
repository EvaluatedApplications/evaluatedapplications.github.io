using System.Globalization;

namespace Doctrine.Army;

/// <summary>
/// The army section (seam a) on Pleroma stores. Replaces <see cref="StubArmyService"/> in the builder: <c>DoctrineBuilder.New().WithArmy(new PleromaArmyService())</c>.
/// All state is in the Pleroma stores of <see cref="PleromaArmy"/>. The army code is a short text (format "DA2"); it carries the whole state.
/// </summary>
public sealed class PleromaArmyService : IArmyService
{
    public IArmy Create(Side side, int seed) => PleromaArmy.Fresh(side, seed);

    /// <summary>
    /// Army code, format <c>DA2.side.round.gold.flipMask.aura;aura.kind:soldiers:slot:veterancy;kind:...</c>, every field decimal.
    /// The code is canonical (kinds and auras sorted), so TryParse(ToCode()) re-encodes to the same text. TryParse never throws.
    /// </summary>
    public bool TryParse(string code, out IArmy? army, out string error)
    {
        army = null; error = "";
        try
        {
            if (string.IsNullOrEmpty(code)) { error = "empty code"; return false; }
            if (code.Length > PleromaArmyLayout.MaxCode) { error = "code too long"; return false; }
            var parts = code.Split('.');
            if (parts.Length != 7 || parts[0] != "DA2") { error = "not an army code"; return false; }
            if (!Num(parts[1], 0, 1, out int side)) { error = "bad side"; return false; }
            if (!Num(parts[2], 0, int.MaxValue, out int round)) { error = "bad round"; return false; }
            if (!Num(parts[3], 0, int.MaxValue, out int gold)) { error = "bad gold"; return false; }
            if (!Num(parts[4], 0, (1 << DoctrineVocabulary.Traits) - 1, out int flipMask)) { error = "bad flip mask"; return false; }

            var auras = new List<(int Bit, int Percent)>();
            var seenBit = new bool[DoctrineVocabulary.Traits];
            if (parts[5].Length > 0)
                foreach (var a in parts[5].Split(';'))
                {
                    var f = a.Split(':');
                    if (f.Length != 2 || !Num(f[0], 0, DoctrineVocabulary.Traits - 1, out int bit) || !Num(f[1], 1, PleromaArmyLayout.AuraCap, out int pct) || seenBit[bit])
                    { error = "bad aura"; return false; }
                    seenBit[bit] = true;
                    auras.Add((bit, pct));
                }

            var cohorts = new List<(int Kind, int Soldiers, int Slot, int Veterancy)>();
            var seenKind = new bool[Roster.Size];
            if (parts[6].Length > 0)
                foreach (var c in parts[6].Split(';'))
                {
                    var f = c.Split(':');
                    if (f.Length != 4 || !Num(f[0], 0, Roster.Size - 1, out int kind) || !Num(f[1], 1, int.MaxValue, out int n)
                        || !Num(f[2], 0, DoctrineVocabulary.Groups - 1, out int slot) || !Num(f[3], 0, PleromaArmyLayout.Period - 1, out int vet) || seenKind[kind])
                    { error = "bad cohort"; return false; }
                    seenKind[kind] = true;
                    cohorts.Add((kind, n, slot, vet));
                }

            army = PleromaArmy.Rebuild((Side)side, round, gold, flipMask, auras, cohorts);
            return true;
        }
        catch (Exception e)
        {
            army = null; error = e.GetType().Name;
            return false;
        }
    }

    /// <summary>Decimal digits only (no sign, no space), in [lo, hi].</summary>
    static bool Num(string s, int lo, int hi, out int value)
    {
        value = 0;
        if (s.Length == 0 || s.Length > 10) return false;
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
        return value >= lo && value <= hi;
    }
}
