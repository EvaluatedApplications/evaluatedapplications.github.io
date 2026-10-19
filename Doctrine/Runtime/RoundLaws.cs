using Doctrine.Army;

namespace Doctrine;

/// <summary>
/// The laws of a round that do not depend on who plays it: how an offer binds a pick, how a choice is cleaned, what a field pays, how a battle is set up.
/// The single-player stages and the duel call THESE, so there is one definition of each (the duel is not a copy of the pipeline).
/// </summary>
internal static class RoundLaws
{
    /// <summary>The reason an army-less refusal carries: the pick is not one of the offered packs (or the pack was already bought this round).</summary>
    public const string NotOffered = "not on offer";

    /// <summary>THE OFFER BINDS THE PLAYER: a pick that is not exactly an offered pack (kind and count), or a pack bought twice, never reaches the army. Returns what goes on to the army and what is refused.</summary>
    public static (IReadOnlyList<DraftPick> Picks, List<(DraftPick Pick, string Reason)> NotOffered) BindDraft(UnitOffer offer, IReadOnlyList<DraftPick> picks, OfferRules rules)
    {
        var notOffered = new List<(DraftPick Pick, string Reason)>();
        if (offer.Active && rules.Enforce)
        {
            var left = offer.Packs.ToList();
            var ok = new List<DraftPick>();
            foreach (var p in picks)
            {
                int i = left.FindIndex(k => k.Kind == p.Kind && k.Count == p.Count);
                if (i < 0) notOffered.Add((p, NotOffered)); else { left.RemoveAt(i); ok.Add(p); }
            }
            picks = ok;
        }
        return (picks, notOffered);
    }

    /// <summary>
    /// WHO JUDGES A TOKEN (CONTRACT.md): a token is removed and counted in <paramref name="dropped"/> only when it is not in the vocabulary, or is a FIELD or TRIGGER order the
    /// <paramref name="legal"/> list (the start mask) does not allow; one that is not on offer, or picked twice, is removed and counted in <paramref name="refused"/>. An ARMY order goes
    /// on whole: the army judges it, in slot order, against the army as the earlier tokens left it.
    /// </summary>
    public static DoctrineSet BindChoice(DoctrineSet choice, LegalMask legal, OrderOffer offer, OfferRules rules, ref int dropped, ref int refused)
    {
        int drop = dropped, refuse = refused;
        var picked = new HashSet<int>();
        bool binding = offer.Active && rules.Enforce;
        int Keep(int t)
        {
            if (t == 0) return 0;
            if (DoctrineVocabulary.IsValid(t) && binding && (!offer.Allows(t) || !picked.Add(t))) { refuse++; return 0; }
            if (!DoctrineVocabulary.IsValid(t) || (DoctrineVocabulary.Decode(t).Family is OrderFamily.Field or OrderFamily.Trigger && !legal.Allows(t))) { drop++; return 0; }
            return t;
        }
        var clean = new DoctrineSet(Keep(choice.S0), Keep(choice.S1), Keep(choice.S2), Keep(choice.S3));
        dropped = drop; refused = refuse;
        return clean;
    }

    /// <summary>What a field pays before the comeback muster: kills / 2 plus 6 gold and a veterancy step for a win, 2 gold for anything else (the flat null: kills / 2 + 4).</summary>
    public static Spoils BaseSpoils(DoctrineDomain d, bool won, int killed)
        => d.FlatSpoils ? new Spoils(killed / 2 + 4, 0) : new Spoils(killed / 2 + (won ? 6 : 2), won ? 1 : 0);

    /// <summary>The setup of a battle: two army reads, two doctrines, the arena's seed and (in a siege arena) the besieger. The attacker is the first army.</summary>
    public static BattleSetup Setup(DoctrineDomain d, ArenaInfo arena, int seed, int round, Side? besieger,
                                    ArmyReadModel attacker, DoctrineSet attackerDoctrine, ArmyReadModel defender, DoctrineSet defenderDoctrine)
        => new(arena.Id, (int)GameRng.Mix(seed, 4000 + round), d.TickLimit,
            new BattleSide(Side.Player, attacker.Cohorts, DoctrineVocabulary.FieldOrders(attackerDoctrine), DoctrineVocabulary.Trigger(attackerDoctrine)),
            new BattleSide(Side.Enemy, defender.Cohorts, DoctrineVocabulary.FieldOrders(defenderDoctrine), DoctrineVocabulary.Trigger(defenderDoctrine)), besieger);

    /// <summary>The free turn of the ranks (<see cref="IOpponent.FormationAsync"/>): 0 does nothing; 1 and 2 are the army's own Rotate. False when the number is not 0..2 (nothing is applied).</summary>
    public static async ValueTask<bool> RotateAsync(IArmy army, int formation, CancellationToken ct)
    {
        if (formation == 0) return true;
        if (formation is < 0 or > DoctrineVocabulary.Groups - 1) return false;
        await army.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, formation)), ct);
        return true;
    }
}
