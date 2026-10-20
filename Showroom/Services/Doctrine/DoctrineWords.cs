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
            return $"{Slot(o.A)} line, {FieldOrders[o.Kind].ToLowerInvariant()}: {Lower(FieldSentences[o.Kind])} "
                   + ((FieldOrderKind)o.Kind == FieldOrderKind.Hold ? DugInShort() + " " : "") + "A line with no order presses on.";
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

    /// <summary>Who stands in one line of a host, said as "12 Spearmen and 6 Archers", or "nobody". A kind <paramref name="nameOf"/> does not name counts under "more".</summary>
    public static string Standing(ArmyReadModel host, int slot, Func<int, string>? nameOf = null) => Describe(host, slot, nameOf ?? KindName);

    // ---- the offers (the muster of companies and the council of orders) ----

    /// <summary>A company on offer: how many of which kind ("12 Spearmen").</summary>
    public static string PackName(UnitPack p) => $"{p.Count} {KindName(p.Kind)}";

    /// <summary>What a company costs, against what is left in the purse. A company already bought just says its price.</summary>
    public static string PackCost(UnitPack p, int left, bool bought) => bought ? CostWords(p.Cost, p.Cost) : CostWords(p.Cost, left);

    /// <summary>The line a company would stand in, as the army's own balance rule places it (read from a preview, never worked out here).</summary>
    public static string JoinWords(int slot, bool bought)
        => bought ? $"Joins the {Slot(slot).ToLowerInvariant()} line." : $"Would join the {Slot(slot).ToLowerInvariant()} line.";

    /// <summary>The muster has nothing for this purse.</summary>
    public const string NoCompany = "No company is offered: the purse cannot pay for even the smallest.";

    /// <summary>What a reroll of the council is, said with its price, the rerolls left and the purse.</summary>
    public static string RerollWords(OrderOffer o, int purse)
        => $"A reroll draws a new council for {o.RerollCost} gold. You have {o.RerollsLeft} {(o.RerollsLeft == 1 ? "reroll" : "rerolls")} left and {purse} gold.";

    /// <summary>The line a Reform the ranks configuration stands for, in the order of the picker.</summary>
    public static string FormationTitle(int turns, int token) => turns == 0 ? "As they stand" : Label(token);

    /// <summary>The order a line would get in the battle's opening: its field order, or the plain fact that a line with no order presses on.</summary>
    public static string LineOrderWords(FieldOrder? o)
        => o is { } f ? $"Your order reaches them: {FieldOrders[(int)f.Kind]}." : "No order for this line: it presses on.";

    /// <summary>Requests the game refused because they were outside the offer, said plainly (a pack or an order not offered, an order twice, a reroll that could not be paid).</summary>
    public static string OfferRefused(int n)
        => $"{n} {(n == 1 ? "request" : "requests")} outside the offer {(n == 1 ? "was" : "were")} refused: a company or an order that was not offered, an order given twice, or a reroll you could not pay.";

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
        "not on offer" => "That company is not on offer, or it was already bought this round.",
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

    // ---- small counts in words, and lists ----

    static readonly string[] Cardinals = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];
    /// <summary>A small count in words ("six"); beyond twelve, the figure.</summary>
    public static string Cardinal(int n) => (uint)n < (uint)Cardinals.Length ? Cardinals[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    static string MoreTimes(int n) => n == 1 ? "one more time" : Cardinal(n) + " more times";
    static string JoinAnd(IReadOnlyList<string> l) => l.Count == 0 ? "none" : l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1];

    // ---- the general: how it chooses and what its two records mean ----

    /// <summary>
    /// The general counts its record over about this many rounds: it halves every count when its window passes the figure. The library does not publish it (it is a
    /// private constant of TinyHoloGeneral), so the proof PROBES a real general and goes red if this drifts from what the general does.
    /// </summary>
    public const int RecordWindow = 64;
    /// <summary>The scale of the match record (the general reports permille: one thousand is every round).</summary>
    public const int RecordScale = 1000;

    /// <summary>What the general did with the council, said at the head of its panel.</summary>
    public const string GeneralRanked = "It ranked the cards on this council from what you have taken from councils before, and fills the slots in that order.";

    /// <summary>What the figure beside each card means: how often the card it ranked in that slot was the card you took there.</summary>
    public static string SlotRecordWords() => $"Beside each card: over about the last {RecordWindow} rounds, how often the card it ranked in that slot was the card you took in that slot (a round where neither of you named a card for the slot is not counted).";

    /// <summary>What the figure under the whole pick means: the share of rounds in which all of it came out exactly as the doctrine you set.</summary>
    public static string WholeRecordWords()
        => $"over about the last {RecordWindow} rounds, the share of rounds in which all of it came out exactly as the doctrine you then set. A short record reads a little under that share, so it starts low and climbs as the picks keep matching.";

    /// <summary>What the general is made of, for the top of How it works (read from its options: a table-only or a net-only general would say so).</summary>
    public static string GeneralBuilt(global::Doctrine.General.GeneralOptions o) => o.Mode switch
    {
        global::Doctrine.General.GeneralMode.TableOnly => "two small tables",
        global::Doctrine.General.GeneralMode.NetOnly => "a tiny HoloFormer from the AlgFormer package",
        _ => "two small tables and a tiny HoloFormer from the AlgFormer package",
    };

    /// <summary>How the general chooses (the rule of TinyHoloGeneral, with the margin read from its options).</summary>
    public static IReadOnlyList<string> GeneralHow(global::Doctrine.General.GeneralOptions o)
    {
        var l = new List<string>();
        if (o.Mode != global::Doctrine.General.GeneralMode.NetOnly)
            l.Add("Each round it reads the cards on the council and ranks them, first, second, third and fourth. Two small tables do the ranking. The habit table remembers which order you put in each slot in a situation like this one: the round, how many soldiers and kinds you hold, your gold, the traits your host carries, how big the enemy host is, and how the last battle went. The taste table remembers what kind of card you take from a council: the sort of order, the line, trait or kind it touches, and the very card, so a liking for one kind of card carries to cards it has not met. Orders that are not on the council are left out of the pick."
                + (o.History ? " The taste table also reads whether a card repeats what you took last round." : " It does not read what you took last round."));
        switch (o.Mode)
        {
            case global::Doctrine.General.GeneralMode.Hybrid:
                l.Add($"A tiny HoloFormer from the AlgFormer package learns beside them from the same examples. For each slot the table that has guessed your picks better speaks, and the HoloFormer takes the slot only once it has been right on that slot at least {MoreTimes(o.Margin)} than that table, over your recent rounds.");
                break;
            case global::Doctrine.General.GeneralMode.TableOnly:
                l.Add("For each slot the table that has guessed your picks better speaks.");
                break;
            default:
                l.Add("A tiny HoloFormer from the AlgFormer package learns from the same examples and speaks for every slot.");
                break;
        }
        return l;
    }

    // ---- the muster ----

    /// <summary>The game offers no second draw of the companies: only the council can be drawn again.</summary>
    public const string MusterNote = "The muster is dealt once a round and cannot be drawn again. Only the council can be, for gold.";

    // ---- the battle's own rules, said once (every figure read from them) ----

    /// <summary>The name of the state of a line that has held its ground.</summary>
    public const string DugInName = "Dug in";

    /// <summary>The rule of a line that holds its ground, from the battle's tables.</summary>
    public static string DugInRule()
        => $"{DugInName}. A line that holds its ground for {Cardinal(UnitRules.SetTicks)} ticks without being hit is dug in: it strikes harder until it is hit or moves. A line that already shoots from afar gains less, and the longest reach on the field gains nothing.";

    /// <summary>What being dug in is worth to a cohort with these traits, from the battle's own bonus.</summary>
    public static string DugInFor(Trait t)
    {
        int bonus = UnitRules.SetBonusPct(t);
        return bonus > 0 ? $"{DugInName}, it strikes {bonus}% harder." : $"{DugInName}, it gains nothing: its reach is already the longest.";
    }

    static string DugInShort() => $"Unhit for {Cardinal(UnitRules.SetTicks)} ticks, it is dug in and strikes harder until it is hit or moves.";

    /// <summary>"first, third, fifth and seventh": the rounds of a game of that many rounds in which you besiege (the game's own rule for who besieges).</summary>
    public static string YourSiegeRounds(int rounds)
    {
        var l = new List<string>();
        for (int r = 0; r < rounds; r++) if (SiegeRules.BesiegerOf(r) == Side.Player) l.Add(Ordinal(r + 1));
        return JoinAnd(l);
    }

    /// <summary>The siege rule, said once and without a name: who besieges, the clock, the stance, the swap. Safe in the static shell.</summary>
    public static string SiegeRule(int rounds)
        => $"Some grounds are sieges: a wall stands between the armies and one side besieges. The besieger carries a siege clock. A besieger that has traded no blow when the clock runs out loses its stance, and every company of it must advance; the other side may wait. The sides swap every round: in a game of {Cardinal(rounds)} rounds you besiege in the {YourSiegeRounds(rounds)} rounds and the enemy in the rest. The open grounds have no clock.";

    /// <summary>The grounds of the game, sieges with their clocks and the open ones, read from the battle's catalogue.</summary>
    public static string SiegeGrounds(IReadOnlyList<ArenaInfo> arenas)
        => $"The sieges are {SiegeList(arenas)}; the open grounds are {JoinAnd(arenas.Where(a => !a.IsSiege).Select(a => a.Name).ToList())}.";

    static string SiegeList(IReadOnlyList<ArenaInfo> arenas) => JoinAnd(arenas.Where(a => a.IsSiege).Select(a => $"{a.Name} (a clock of {a.SiegeClock} ticks)").ToList());

    /// <summary>The briefing line of a round, from the game's own answer about its ground (<c>DoctrineGame.BriefingAsync</c>): which ground, whether it is a siege, who besieges and the clock.</summary>
    public static string GroundBriefing(ArenaBrief a)
    {
        if (!a.IsSiege) return $"This round: {a.Info.Name}, an open ground with no siege clock.";
        return a.Besieger switch
        {
            Side.Player => $"This round: {a.Info.Name}, a siege. You besiege; the clock is {a.SiegeClock} ticks.",
            Side.Enemy => $"This round: {a.Info.Name}, a siege. The enemy besieges; the clock is {a.SiegeClock} ticks.",
            _ => $"This round: {a.Info.Name}, a siege ground that no side besieges.",
        };
    }

    /// <summary>The chip on the replay: who besieges, and the clock.</summary>
    public static string SiegeChip(Side besieger, int clock)
        => (besieger == Side.Player ? "You besiege" : "The enemy besieges") + (clock > 0 ? $": the clock is {clock} ticks" : "");

    /// <summary>The chip on the replay from the frame the clock has fallen.</summary>
    public static string ClockFellChip(Side besieger) => besieger == Side.Player ? "The clock has fallen: you have lost your stance" : "The clock has fallen: the enemy has lost its stance";

    /// <summary>The line of the recap that names the ground a siege and who besieged it.</summary>
    public static string SiegeRecap(string arenaName, Side besieger, int clock)
        => $"{arenaName} is a siege, and {(besieger == Side.Player ? "you besieged" : "the enemy besieged")} it" + (clock > 0 ? $": the clock was {clock} ticks." : ".");

    /// <summary>The recap line for a clock that fell: the frame the recording sets the bit.</summary>
    public static string ClockFellRecap(Side besieger, int tick)
        => $"The clock fell at tick {tick}: {(besieger == Side.Player ? "you" : "the enemy")} had traded no blow by then, lost the stance, and every company of {(besieger == Side.Player ? "yours" : "theirs")} advanced.";

    /// <summary>The recap line for a clock that did not fall because the besieger had traded a blow.</summary>
    public static string StanceKeptRecap(Side besieger)
        => $"{(besieger == Side.Player ? "You" : "The enemy")} traded a blow before the clock ran out, and kept the stance.";

    /// <summary>The recap line of a battle the tick limit stopped (both sides still stood): who the field went to and that power, not the count of soldiers, decides it.</summary>
    public static string LimitRecap(BattleStatus outcome) => outcome switch
    {
        BattleStatus.AttackerWon => "The battle stopped at its tick limit, and the field went to you: the greater power left decides it, not the number of soldiers.",
        BattleStatus.DefenderWon => "The battle stopped at its tick limit, and the field went to the enemy: the greater power left decides it, not the number of soldiers.",
        BattleStatus.Draw => "The battle stopped at its tick limit with the power left on the two sides equal.",
        _ => "The battle stopped at its tick limit.",
    };

    /// <summary>What the tick limit does, said once.</summary>
    public static string TickLimitRule()
        => "If the tick limit stops a battle, the field goes to the side with the greater power left, each company's soldiers weighed by its power, how hard it strikes, how far it reaches and how little damage it takes, not to the side with more soldiers; equal power is a draw.";
}
