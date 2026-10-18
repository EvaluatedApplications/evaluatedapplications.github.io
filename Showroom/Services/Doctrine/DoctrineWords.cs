using Doctrine;

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
            ArmyOrderKind.Flip => $"Flip the {Traits[o.A]} trait on every cohort: cohorts that had it lose it and the others gain it. Twice undoes it.",
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
