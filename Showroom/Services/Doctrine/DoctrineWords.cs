using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// The plain words of the Doctrine page: names for tokens, traits, slots and refusals. No game rule lives here, only how a rule is said.

public static class DoctrineWords
{
    public static readonly string[] Traits = ["Heavy", "Swift", "Ranged", "Holy", "Cursed", "Beast", "Machine", "Shielded"];
    public static readonly string[] Slots = ["Front", "Middle", "Back"];
    public static readonly string[] FieldOrders = ["Advance", "Hold", "Flank", "Fall back", "Guard", "Hunt"];
    public static readonly string[] FieldSentences =
    [
        "Move on the nearest enemy cohort.",
        "Stay where you stand and keep watch.",
        "Go round the side to reach the enemy.",
        "Pull back toward your own edge of the ground.",
        "Stay close to an allied cohort.",
        "Go for the weakest enemy cohort you know about.",
    ];

    public static string Slot(int slot) => (uint)slot < (uint)Slots.Length ? Slots[slot] : "Off the line";
    public static string KindName(int kind) => (uint)kind < (uint)Roster.Size ? Roster.NameOf(kind) : "Unknown";

    /// <summary>
    /// What a kind of soldier is, in plain words. Every number is read from the battle's own tables (<see cref="UnitRules"/>), not typed, so the line
    /// cannot drift from what the soldiers do. No trait names: "Heavy" and "Shielded" are in the Details of a cohort, not here.
    /// </summary>
    public static string KindBlurb(int kind)
    {
        if ((uint)kind >= (uint)Roster.Size) return "soldiers";
        var t = Roster.TraitsOf(kind);
        int dmg = UnitRules.DamagePct(t), taken = UnitRules.TakenPct(t);
        string hits = dmg > 100 ? $"hit {dmg - 100}% harder" : dmg < 100 ? $"hit at {dmg}% strength" : "";
        string guard = taken < 100 ? $"take {100 - taken}% less damage" : "";
        string slowFast = UnitRules.Speed(t) > 2.0f ? "fast " : UnitRules.Speed(t) < 2.0f ? "slow " : "";
        string far = (t & Trait.Machine) != 0 ? "shoot from far away" : (t & Trait.Ranged) != 0 ? "shoot from a distance" : "";
        string holy = (t & Trait.Holy) != 0 ? $"hurt cursed soldiers {BattleChronicle.HolyWords()}" : "";
        string noun = (t & Trait.Machine) != 0 ? "siege engines" : (t & Trait.Beast) != 0 ? "beasts" : (t & Trait.Holy) != 0 ? "holy archers" : (t & Trait.Cursed) != 0 ? "cursed fighters"
            : (t & Trait.Ranged) != 0 ? "archers" : t == Trait.None ? "basic fighters" : "fighters";
        var parts = new List<string>();
        foreach (var p in new[] { far, hits, guard, holy }) if (p.Length > 0) parts.Add(p);
        string head = (slowFast + noun).Trim();
        if (noun == "archers" && far.Length > 0) parts.Remove(far);
        if (noun == "holy archers" && far.Length > 0) parts.Remove(far);
        return parts.Count == 0 ? head : head + " that " + string.Join(", ", parts);
    }

    /// <summary>The same, starting with a capital (for a list that gives the price after it).</summary>
    public static string KindBlurbCap(int kind) { var s = KindBlurb(kind); return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]; }

    /// <summary>One cohort in plain words: "Spearmen, 10: basic fighters in the front line." A kind that is not open yet is not named (the caller says "other").</summary>
    public static string CohortSentence(int key, int count, int slot) => $"{KindName(key)}, {count}: {KindBlurb(key)} in the {Slot(slot).ToLowerInvariant()} line.";

    /// <summary>The traits a trait byte carries, in words ("Heavy, Shielded"), or "None".</summary>
    public static string TraitList(Trait t)
    {
        var l = new List<string>();
        for (int b = 0; b < Traits.Length; b++) if (((int)t >> b & 1) != 0) l.Add(Traits[b]);
        return l.Count == 0 ? "None" : string.Join(", ", l);
    }

    /// <summary>Short label for a chip or slot.</summary>
    public static string Label(int token)
    {
        if (token == 0) return "Empty";
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family == OrderFamily.Field) return $"{Slot(o.A)}: {FieldOrders[o.Kind]}";
        return (ArmyOrderKind)o.Kind switch
        {
            ArmyOrderKind.Rotate => $"Rotate {o.A}",
            ArmyOrderKind.Flip => $"Flip {Traits[o.A]}",
            ArmyOrderKind.Age => "Age",
            ArmyOrderKind.Merge => $"Merge into {KindName(o.A)}",
            ArmyOrderKind.Aura => $"Aura {Traits[o.A]}",
            ArmyOrderKind.Cull => $"Cull {Traits[o.A]}",
            _ => DoctrineVocabulary.Name(token),
        };
    }

    /// <summary>One sentence of what the order does, as the army and the battle really treat it.</summary>
    public static string Sentence(int token)
    {
        if (token == 0) return "No order in this slot.";
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family == OrderFamily.Field)
            return $"{Slot(o.A)} line, {FieldOrders[o.Kind].ToLowerInvariant()}: {FieldSentences[o.Kind]} A line with no order advances.";
        return (ArmyOrderKind)o.Kind switch
        {
            ArmyOrderKind.Rotate => $"Move every cohort {o.A} place{(o.A == 1 ? "" : "s")} along the line (front to middle to back, then round to the front).",
            ArmyOrderKind.Flip => $"Flip the {Traits[o.A]} trait on every cohort: cohorts that had it lose it and the others gain it. It costs gold for the soldiers who gain power, so it is greyed out when you cannot pay. Twice undoes the traits, not the gold.",
            ArmyOrderKind.Age => "Every cohort moves one step along the veterancy ring of 9 steps. Each step adds 5 power, then the ring wraps to 0.",
            ArmyOrderKind.Merge => $"Move the soldiers of {KindName((o.A + 1) % Roster.Size)} into {KindName(o.A)}.",
            ArmyOrderKind.Aura => $"Cohorts carrying {Traits[o.A]} get a bonus this round, as many percent as soldiers carry it, up to 50. It wears off at the end of the round.",
            ArmyOrderKind.Cull => $"Dismiss the cohorts that carry {Traits[o.A]}.",
            _ => DoctrineVocabulary.Name(token),
        };
    }

    public static string Orders(DoctrineSet d) => d.IsEmpty ? "No orders" : string.Join(", ", d.Tokens.Where(t => t != 0).Select(Label));

    /// <summary>The army's own refusal reason, said plainly.</summary>
    public static string Refusal(string reason) => reason switch
    {
        "gold" => "Too little gold for this pick.",
        "command span" => $"The command span is full: an army can hold {Roster.Size} kinds.",
        "bad pick" => "Not a kind in the roster, or a count of zero.",
        "army unreadable" => "The army holds more kinds than a Pleroma store can list, so it cannot be read.",
        _ => reason,
    };

    public static string Outcome(BattleStatus s) => s switch
    {
        BattleStatus.AttackerWon => "Your side won the field.",
        BattleStatus.DefenderWon => "The enemy held the field.",
        BattleStatus.Draw => "Both sides fell together: a draw.",
        _ => "The battle did not finish.",
    };

    public static string OutcomeShort(BattleStatus s) => s switch
    {
        BattleStatus.AttackerWon => "Won",
        BattleStatus.DefenderWon => "Lost",
        BattleStatus.Draw => "Draw",
        _ => "Unfinished",
    };
}
