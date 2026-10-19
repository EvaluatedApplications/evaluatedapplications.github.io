using Doctrine;
using Doctrine.Army;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// The plain words of the Doctrine page: names for tokens, traits, slots and refusals, in the voice of a lord who speaks of his host. No game rule lives
// here, only how a rule is said. NO NUMBER IS TYPED IN A SENTENCE: every figure a line states (the years of the ring, the power a year adds, the cap of a
// banner, the quarters of a trigger, the places of a reform) is read from the game's own tables when the line is made, so the text follows a rule that
// changes. The proof scans this file for a digit inside a string, and checks each stated figure against the rule it names.

/// <summary>An army order tried on a copy of the host as it stands: the host before and after, as the army itself reads them.</summary>
public sealed record OrderTrial(ArmyReadModel Before, ArmyReadModel After);

public static class DoctrineWords
{
    /// <summary>How a trait is said as an adjective (the cohort's Details, an order's name). Index = trait bit.</summary>
    public static readonly string[] Traits = ["heavily armoured", "fleet", "shoots from afar", "blessed", "cursed", "beast", "siege-built", "shielded"];
    /// <summary>The folk who carry a trait (a banner is raised for them).</summary>
    public static readonly string[] TraitFolk = ["the heavily armoured", "the fleet", "the far-shooters", "the blessed", "the cursed", "the beasts", "the siege engines", "the shielded"];
    /// <summary>What a trait is, as a thing a cohort has or has not ("had heavy armour", "with a blessing").</summary>
    public static readonly string[] TraitMark = ["heavy armour", "fleetness", "the skill of shooting from afar", "a blessing", "a curse", "a beast's nature", "siege-craft", "a shield"];

    public static readonly string[] Slots = ["Front", "Middle", "Back"];
    /// <summary>The six orders for a line, by <see cref="FieldOrderKind"/>: Advance, Hold, Flank, Fall back, Guard, Hunt.</summary>
    public static readonly string[] FieldOrders = ["Press on", "Hold the line", "Ride the flank", "Fall back", "Guard the host", "Run them down"];
    public static readonly string[] FieldSentences =
    [
        "Marches on the nearest enemy cohort.",
        "Stands its ground and keeps watch.",
        "Rides round the side to reach the enemy.",
        "Pulls back toward your own edge of the ground.",
        "Stays close to a friendly cohort.",
        "Goes for the weakest enemy cohort it knows of.",
    ];

    public static string Slot(int slot) => (uint)slot < (uint)Slots.Length ? Slots[slot] : "Off the line";
    public static string KindName(int kind) => (uint)kind < (uint)Roster.Size ? Roster.NameOf(kind) : "Unknown";
    static string Places(int n) => n == 1 ? "place" : "places";
    static string Years(int n) => n == 1 ? "year" : "years";
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    static readonly string[] Ordinals = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];
    static string Ordinal(int n) => (uint)n < (uint)Ordinals.Length ? Ordinals[n] : n + "th";

    /// <summary>How many of the host's quarters a trigger waits for, said as a fraction ("a quarter", "half", "three quarters"). The count is the token's own.</summary>
    public static string QuartersWords(int quarters) => quarters switch
    {
        1 => "a quarter",
        2 => "half",
        3 => "three quarters",
        4 => "all",
        _ => quarters + " quarters",
    };

    // ---- the rule values the sentences state, each READ from the game ----

    static int? _auraCap;
    /// <summary>
    /// The most a banner can add, in percent. The army does not publish it, so it is read by trying a banner on a throwaway host of far more soldiers than any
    /// cap (the cohort's power rises by the banner's whole bonus). Null when the army cannot be tried, and then no cap is stated.
    /// </summary>
    public static int? AuraCap
    {
        get
        {
            if (_auraCap is { } have) return have > 0 ? have : null;
            _auraCap = ProbeAuraCap();
            return _auraCap > 0 ? _auraCap : null;
        }
    }

    static int ProbeAuraCap()
    {
        try
        {
            int kind = Enumerable.Range(0, Roster.Size).First(k => (Roster.TraitsOf(k) & Trait.Ranged) != 0);
            if (!new PleromaArmyService().TryParse($"DA2.0.0.9999.0..{kind}:9999:1:0", out var army, out _) || army is null) return 0;
            var run = army.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Aura, 2)), CancellationToken.None);
            if (!run.IsCompletedSuccessfully || run.Result.Applied != 1) return 0;
            return army.Read().Cohorts.Select(c => c.PowerPct - 100).DefaultIfEmpty(0).Max();
        }
        catch (Exception) { return 0; }
    }

    static bool PriceAge => ArmyRules.Default.PriceAge;
    static bool PriceAura => ArmyRules.Default.PriceAura;

    // ---- soldiers and cohorts ----

    /// <summary>
    /// What a kind of soldier is, in plain words. Every number is read from the battle's own tables (<see cref="UnitRules"/>), not typed, so the line
    /// cannot drift from what the soldiers do. No trait names: they are in the Details of a cohort, not here.
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
    public static string KindBlurbCap(int kind) => Cap(KindBlurb(kind));

    /// <summary>One cohort in plain words: "Spearmen, 10: basic fighters in the front line." A kind that is not open yet is not named (the caller says "other").</summary>
    public static string CohortSentence(int key, int count, int slot) => $"{KindName(key)}, {count}: {KindBlurb(key)} in the {Slot(slot).ToLowerInvariant()} line.";

    /// <summary>The traits a trait byte carries, in words ("heavily armoured, shielded"), or "none".</summary>
    public static string TraitList(Trait t)
    {
        var l = new List<string>();
        for (int b = 0; b < Traits.Length; b++) if (((int)t >> b & 1) != 0) l.Add(Traits[b]);
        return l.Count == 0 ? "none" : string.Join(", ", l);
    }

    /// <summary>A cohort's details line: its traits, its years of seasoning and its power.</summary>
    public static string CohortDetails(CohortRead c) => $"Traits: {TraitList(c.Traits)}. Seasoned {c.Veterancy} {Years(c.Veterancy)}. Power {c.PowerPct}%.";

    // ---- the orders ----

    /// <summary>The title of a kind of order for the whole host, with no trait or kind chosen (the unlock announcement names these).</summary>
    public static string ArmyTitle(ArmyOrderKind k) => k switch
    {
        ArmyOrderKind.Rotate => "Reform the ranks",
        ArmyOrderKind.Flip => "Reforge",
        ArmyOrderKind.Age => "Drill the host",
        ArmyOrderKind.Merge => "Swear into a kind",
        ArmyOrderKind.Aura => "Raise a banner",
        ArmyOrderKind.Cull => "Dismiss a trait",
        _ => "",
    };

    /// <summary>The title of the trigger orders as a group.</summary>
    public const string TriggerTitle = "Should the host fall";

    /// <summary>Short label for a chip or slot.</summary>
    public static string Label(int token)
    {
        if (token == 0) return "Empty";
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family == OrderFamily.Field) return $"{Slot(o.A)} line: {FieldOrders[o.Kind]}";
        if (o.Family == OrderFamily.Trigger) return $"Should {QuartersWords(o.A)} fall";
        return (ArmyOrderKind)o.Kind switch
        {
            ArmyOrderKind.Rotate => $"Reform the ranks: {o.A} {Places(o.A)}",
            ArmyOrderKind.Flip => $"Reforge: {Traits[o.A]}",
            ArmyOrderKind.Age => "Drill the host",
            ArmyOrderKind.Merge => $"Swear into the {KindName(o.A)}",
            ArmyOrderKind.Aura => $"Raise the banner of {TraitFolk[o.A]}",
            ArmyOrderKind.Cull => $"Dismiss {TraitFolk[o.A]}",
            _ => DoctrineVocabulary.Name(token),
        };
    }

    /// <summary>The one-line effect of an order on a card or chip: what it does to the host, with every figure read from the rules.</summary>
    public static string Effect(int token)
    {
        if (token == 0) return "No order in this slot.";
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family == OrderFamily.Field) return FieldSentences[o.Kind];
        if (o.Family == OrderFamily.Trigger) return $"Should {QuartersWords(o.A)} of the host fall, then:";
        return (ArmyOrderKind)o.Kind switch
        {
            ArmyOrderKind.Rotate => $"Every cohort steps {o.A} {Places(o.A)} along the line.",
            ArmyOrderKind.Flip => $"Cohorts that had {TraitMark[o.A]} shed it; the rest take it up.",
            ArmyOrderKind.Age => $"Every cohort gains a year of seasoning, and each year adds {Roster.VetStepPct}% power.",
            ArmyOrderKind.Merge => $"The {KindName((o.A + 1) % Roster.Size)} swear into the {KindName(o.A)}.",
            ArmyOrderKind.Aura => AuraCap is { } cap
                ? $"Cohorts with {TraitMark[o.A]} fight harder this round, up to {cap}%."
                : $"Cohorts with {TraitMark[o.A]} fight harder this round.",
            ArmyOrderKind.Cull => $"Dismisses the cohorts that carry {TraitMark[o.A]}.",
            _ => DoctrineVocabulary.Name(token),
        };
    }

    /// <summary>The whole of what an order does, as the army and the battle really treat it (one sentence, or two): shown under the slots and in the list of chosen orders.</summary>
    public static string Sentence(int token)
    {
        if (token == 0) return "No order in this slot.";
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family == OrderFamily.Field)
            return $"{Slot(o.A)} line, {FieldOrders[o.Kind].ToLowerInvariant()}: {Lower(FieldSentences[o.Kind])} A line with no order presses on.";
        if (o.Family == OrderFamily.Trigger)
        {
            string w = QuartersWords(o.A);
            return $"Should {w} of the host fall, then the orders after this one take over: orders before it open the battle, orders after it take over once your host has lost {w} of its soldiers, and a line they do not name presses on.";
        }
        return (ArmyOrderKind)o.Kind switch
        {
            ArmyOrderKind.Rotate => $"Reform the ranks: every cohort steps {o.A} {Places(o.A)} along the line (front to middle to back, then round to the front).",
            ArmyOrderKind.Flip => $"Reforge {Traits[o.A]}: every cohort that had {TraitMark[o.A]} sheds it, and every cohort that did not takes it up. The soldiers who gain power are paid for in gold, so the order cannot be given when you cannot pay. Giving it twice undoes the traits, not the gold.",
            ArmyOrderKind.Age => AgeRule(),
            ArmyOrderKind.Merge => $"Swear into the {KindName(o.A)}: all the {KindName((o.A + 1) % Roster.Size)} become {KindName(o.A)}, with the {KindName(o.A)}'s traits and place in the line.",
            ArmyOrderKind.Aura => AuraRule(o.A),
            ArmyOrderKind.Cull => $"Dismiss {TraitFolk[o.A]}: the cohorts that carry {TraitMark[o.A]} leave the host.",
            _ => DoctrineVocabulary.Name(token),
        };
    }

    static string AgeRule()
        => $"Drill the host: every cohort gains a year of seasoning, and each year adds {Roster.VetStepPct}% power. The {Ordinal(Roster.VetPeriod)} drill sends a cohort back to its first year."
           + (PriceAge ? " It costs gold in proportion to the power it adds." : "");

    static string AuraRule(int bit)
        => $"Raise the banner of {TraitFolk[bit]}: cohorts with {TraitMark[bit]} fight harder this round, by as many percent as you have soldiers with it"
           + (AuraCap is { } cap ? $", up to {cap}%" : "") + ". It ends with the round."
           + (PriceAura ? " The banner is rented for gold." : "");

    /// <summary>The rule of a kind of order for the whole host, stated once above its cards (no trait or kind chosen). Same figures as the cards.</summary>
    public static string Rule(ArmyOrderKind k) => k switch
    {
        ArmyOrderKind.Rotate => "Every cohort steps along the line: front to middle to back, then round to the front.",
        ArmyOrderKind.Flip => "Choose a trait. Every cohort that had it sheds it, and every cohort that did not takes it up. The soldiers who gain power are paid for in gold; giving it twice undoes the traits, not the gold.",
        ArmyOrderKind.Age => $"Every cohort gains a year of seasoning, and each year adds {Roster.VetStepPct}% power. The {Ordinal(Roster.VetPeriod)} drill sends a cohort back to its first year."
                             + (PriceAge ? " It costs gold in proportion to the power it adds." : ""),
        ArmyOrderKind.Merge => "The soldiers of the next kind swear into the kind you name and become it, with its traits and place in the line.",
        ArmyOrderKind.Aura => "Cohorts with the trait fight harder this round, by as many percent as you have soldiers with it"
                             + (AuraCap is { } cap ? $", up to {cap}%" : "") + ". It ends with the round."
                             + (PriceAura ? " The banner is rented for gold." : ""),
        ArmyOrderKind.Cull => "The cohorts that carry the trait leave the host.",
        _ => "",
    };

    /// <summary>The slot-order rule of the trigger orders, once, above their cards.</summary>
    public static string TriggerRule()
        => $"Orders before it open the battle; orders after it take over once your host has lost {QuartersWords(1)}, {QuartersWords(2)} or {QuartersWords(3)} of its soldiers, as the card says (a line they do not name presses on).";

    // ---- what an order would do, tried on a copy of the host ----

    /// <summary>
    /// What it costs now, in gold, or null when it costs nothing. A Flip is priced from the cohorts as they stand with the army's own price rule, so it is
    /// shown even when the order cannot be given; the others read the gold the trial took.
    /// </summary>
    public static int? CostNow(int token, ArmyReadModel host, OrderTrial? trial)
    {
        if (token == 0 || host is null) return null;
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family != OrderFamily.Army) return null;
        if ((ArmyOrderKind)o.Kind == ArmyOrderKind.Flip)
        {
            long sum = 0;
            foreach (var c in host.Cohorts) sum += (long)c.Count * Roster.RelabelCost(c.Traits, o.A, ArmyRules.Default.PriceReach);
            return sum > 0 ? (int)sum : null;
        }
        if (trial is null) return null;
        int spent = trial.Before.Gold - trial.After.Gold;
        return spent > 0 ? spent : null;
    }

    public static string CostWords(int gold, int purse) => gold <= purse ? $"Costs {gold} gold." : $"Costs {gold} gold; you have {purse}.";

    static string Describe(ArmyReadModel a, int slot, Func<int, string> nameOf)
    {
        var here = a.Cohorts.Where(x => x.Slot == slot && x.Count > 0).OrderBy(x => x.Key).Select(x => (Name: nameOf(x.Key), x.Count)).ToList();
        var parts = here.Where(p => p.Name.Length > 0).Select(p => $"{p.Count} {p.Name}").ToList();
        int others = here.Where(p => p.Name.Length == 0).Sum(p => p.Count);
        if (others > 0) parts.Add($"{others} more");
        if (parts.Count == 0) return "nobody";
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }

    /// <summary>
    /// What an order for the whole host would do, tried alone on a copy of the host as it stands now: a line read from the host before and after, or null for
    /// an order the host cannot try (an order for a line, a trigger, or one the host would not take). <paramref name="nameOf"/> names a kind, or "" when the
    /// kind is not open to this player yet.
    /// </summary>
    public static string? Preview(int token, OrderTrial? t, Func<int, string>? nameOf = null)
    {
        if (t is null || token == 0) return null;
        var o = DoctrineVocabulary.Decode(token);
        if (o.Family != OrderFamily.Army) return null;
        nameOf ??= KindName;
        var b = t.Before; var a = t.After;
        CohortRead? Was(int key) => b.Cohorts.FirstOrDefault(c => c.Key == key);
        switch ((ArmyOrderKind)o.Kind)
        {
            case ArmyOrderKind.Rotate:
            {
                string x = Describe(b, 0, nameOf), y = Describe(a, 0, nameOf);
                return x == y ? "Your front line stays as it is." : $"Your front line goes from {x} to {y}.";
            }
            case ArmyOrderKind.Flip:
            {
                int gain = 0, shed = 0;
                foreach (var c in a.Cohorts)
                {
                    if (Was(c.Key) is not { } w) continue;
                    bool had = ((int)w.Traits >> o.A & 1) != 0, has = ((int)c.Traits >> o.A & 1) != 0;
                    if (!had && has) gain += c.Count; else if (had && !has) shed += c.Count;
                }
                return $"{gain} {(gain == 1 ? "soldier takes" : "soldiers take")} it up and {shed} {(shed == 1 ? "sheds" : "shed")} it.";
            }
            case ArmyOrderKind.Age:
            {
                var up = new SortedDictionary<int, int>(); int back = 0;
                foreach (var c in a.Cohorts)
                {
                    if (Was(c.Key) is not { } w) continue;
                    int d = c.PowerPct - w.PowerPct;
                    if (d > 0) up[d] = up.GetValueOrDefault(d) + 1; else if (d < 0) back++;
                }
                var parts = up.Select(kv => $"{kv.Value} {(kv.Value == 1 ? "cohort grows" : "cohorts grow")} {kv.Key}% stronger").ToList();
                if (back > 0) parts.Add($"{back} {(back == 1 ? "cohort starts" : "cohorts start")} its ring again");
                return parts.Count == 0 ? "No cohort changes." : Cap(string.Join(", ", parts)) + ".";
            }
            case ArmyOrderKind.Merge:
            {
                int into = o.A, from = (o.A + 1) % Roster.Size;
                int before = Was(into)?.Count ?? 0, after = a.Cohorts.Where(c => c.Key == into).Sum(c => c.Count), moved = Was(from)?.Count ?? 0;
                string nInto = nameOf(into), nFrom = nameOf(from);
                if (nInto.Length == 0) nInto = "those soldiers";
                if (nFrom.Length == 0) nFrom = "the next kind";
                return $"{moved} {nFrom} swear in: the {nInto} go from {before} to {after}.";
            }
            case ArmyOrderKind.Aura:
            {
                int best = 0;
                foreach (var c in a.Cohorts)
                    if (Was(c.Key) is { } w && ((int)c.Traits >> o.A & 1) != 0) best = Math.Max(best, c.PowerPct - w.PowerPct);
                return best > 0 ? $"The cohorts that carry it fight {best}% harder this round." : "No cohort carries it.";
            }
            default:
                return null;
        }
    }

    public static string Orders(DoctrineSet d) => d.IsEmpty ? "No orders" : string.Join(", ", d.Tokens.Where(t => t != 0).Select(Label));

    /// <summary>The army's own refusal reason, said plainly.</summary>
    public static string Refusal(string reason) => reason switch
    {
        "gold" => "Too little gold for this pick.",
        "command span" => $"A host musters {Roster.Size} kinds at most, and this would be one more.",
        "bad pick" => "Not a kind in the roster, or a count of zero.",
        "army unreadable" => $"The host holds more kinds than the muster roll can list ({Roster.Size}), so it cannot be read.",
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
