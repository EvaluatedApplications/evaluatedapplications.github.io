using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// The battle chronicle: the account of one recorded battle, in the Count's voice (todo\doctrine-story-from-mechanics.md). A pure function of the
// recording, the doctrine, the arena's name and the kind names: the same replay always gives the same log. NOTHING here simulates anything and
// no line is flavour. Every line is read from the recording and carries a CITE, the exact thing in the recording that backs it, so a checker
// can look the line up again (tools\DoctrineUiProof does, with its own code). A line the recording cannot back is not written.

public enum ChronicleKind { Start, Contact, Gate, Flank, Holy, Blow, Run, Break, Stand, Lull, End }

public enum CiteKind
{
    /// <summary>Frame 0: A = your soldiers, B = the enemy's.</summary>
    Start,
    /// <summary>A strike in the frame at <c>Tick</c>: A = source blob, B = target blob, Value = the damage the event records.</summary>
    Event,
    /// <summary>The blob A has strength 0 in the frame at <c>Tick</c> and had more in the frame before.</summary>
    Break,
    /// <summary>The gate bit is set in the frame at <c>Tick</c> and was not in the frame before.</summary>
    Gate,
    /// <summary>The blob A was struck in every frame from tick B to <c>Tick</c> (Value ticks running, Value2 = the damage those strikes record).</summary>
    Run,
    /// <summary>No strike is recorded in any frame from tick A to tick B.</summary>
    Span,
    /// <summary>In the frame at <c>Tick</c> the blob A is the only one of its side still standing (Value = its strength) and two or more stood the frame before.</summary>
    Stand,
    /// <summary>The last frame: A = your soldiers, B = the enemy's, Value = the recorded outcome.</summary>
    End,
}

/// <summary>What backs one line. Which fields mean what depends on <see cref="CiteKind"/>.</summary>
public readonly record struct ChronicleCite(CiteKind Kind, int Tick, int A = 0, int B = 0, int Value = 0, int Value2 = 0);

public sealed record ChronicleLine(int Tick, ChronicleKind Kind, string Text, ChronicleCite Cite)
{
    /// <summary>The line as the page shows it: "Tick 12. The Riders ...".</summary>
    public string Full => $"Tick {Tick}. {Text}";
}

/// <summary>The summary under the log. The numbers are the recording's own; <see cref="Lines"/> is the same said in words.</summary>
public sealed record ChronicleWhy(
    int? TurnTick, int TurnYouLost, int TurnEnemyLost,
    int? FirstBreakTick, int? FirstBreakBlob,
    int? FirstLineTick, Side? FirstLineSide, int? FirstLineSlot,
    string? Hint, IReadOnlyList<string> Lines);

public sealed record Chronicle(IReadOnlyList<ChronicleLine> Lines, ChronicleWhy Why)
{
    public static readonly Chronicle Empty = new([], new ChronicleWhy(null, 0, 0, null, null, null, null, null, null, []));
}

public static class BattleChronicle
{
    /// <summary>A cohort that has moved this many arena units across the field (y) from where it began, and strikes from beside its target, struck from the side.</summary>
    public const int FlankUnits = 8;
    /// <summary>The same cohort struck in this many ticks running is a run.</summary>
    public const int RunTicks = 4;
    /// <summary>This many ticks running with no strike after first contact is a lull.</summary>
    public const int LullTicks = 3;
    /// <summary>A blow is heavy when it is at least this many soldiers AND a quarter of what the target had before the tick.</summary>
    public const int HeavyMin = 5;
    public const int MaxHeavy = 2, MaxRuns = 3, MaxLulls = 3, MaxHoly = 2;

    /// <summary>
    /// Build the chronicle. <paramref name="kindName"/> names a kind key (default: the roster). <paramref name="traitsOf"/> gives the traits a cohort
    /// really fought with (side, kind key), or null when they are not known; without it no holy line is written, because a flip can change a kind's traits.
    /// </summary>
    public static Chronicle Build(BattleReplay replay, DoctrineSet doctrine, string arenaName, Func<int, string>? kindName = null, Func<Side, int, Trait?>? traitsOf = null)
    {
        var F = replay.Frames;
        int n = F.Count;
        if (n == 0) return Chronicle.Empty;
        const int Q = BattleReplay.Quantum;
        kindName ??= DoctrineWords.KindName;

        var rows = new List<(int Rank, int Seq, ChronicleLine Line)>();
        int seq = 0;
        void Add(int tick, ChronicleKind kind, string text, ChronicleCite cite) => rows.Add(((int)kind, seq++, new ChronicleLine(tick, kind, text, cite)));

        static ReplayBlob? Find(ReplayFrame f, int id) { foreach (var b in f.Blobs) if (b.Id == id) return b; return null; }
        string Lab(ReplayBlob b) => (b.Side == Side.Player ? "your " : "the enemy ") + kindName(b.Key);
        static string Soldiers(int n) => n == 1 ? "soldier" : "soldiers";
        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
        static int Total(ReplayFrame f, Side side) { int t = 0; foreach (var b in f.Blobs) if (b.Side == side) t += b.Strength; return t; }
        static int Standing(ReplayFrame f, Side side) { int t = 0; foreach (var b in f.Blobs) if (b.Side == side && b.Strength > 0) t++; return t; }

        // ---- the start
        var f0 = F[0];
        Add(f0.Tick, ChronicleKind.Start, $"{arenaName}. {Total(f0, Side.Player)} of your soldiers face the enemy's {Total(f0, Side.Enemy)}.",
            new ChronicleCite(CiteKind.Start, f0.Tick, Total(f0, Side.Player), Total(f0, Side.Enemy)));

        bool contacted = false;
        int contactTick = 0, breaks = 0, linesGone = 0;
        int? firstBreakTick = null, firstBreakBlob = null, firstLineTick = null, firstLineSlot = null;
        Side? firstLineSide = null;
        var flanked = new HashSet<int>();
        var flankSlotsYou = new HashSet<int>();      // slots of your cohorts that struck from the side (for the hint)
        var flankTickOfSlot = new Dictionary<int, (int Tick, string Lab)>();
        var holyPairs = new HashSet<(int, int)>();
        int holyLines = 0;
        var blows = new List<(int Tick, int Src, int Tgt, int Dmg)>();
        var streakLen = new Dictionary<int, int>();
        var streakFirst = new Dictionary<int, int>();
        var streakDmg = new Dictionary<int, int>();
        var runDone = new HashSet<int>();
        int runLines = 0;
        var standDone = new HashSet<Side>();
        var lulls = new List<(int From, int To)>();
        int emptyFrom = -1, emptyTo = -1;
        int turnTick = -1, turnSwing = 0, turnYou = 0, turnEnemy = 0;

        for (int i = 1; i < n; i++)
        {
            var prev = F[i - 1];
            var cur = F[i];
            var evs = new List<(ReplayEvent E, ReplayBlob S, ReplayBlob T)>();
            foreach (var e in cur.Events)
                if (Find(cur, e.Source) is { } s && Find(cur, e.Target) is { } t) evs.Add((e, s, t));
            evs.Sort((a, b) => a.E.Source != b.E.Source ? a.E.Source.CompareTo(b.E.Source) : a.E.Target.CompareTo(b.E.Target));

            // turning point: the tick with the largest net swing in soldiers (the first one on a tie)
            int youLost = Math.Max(0, Total(prev, Side.Player) - Total(cur, Side.Player));
            int enemyLost = Math.Max(0, Total(prev, Side.Enemy) - Total(cur, Side.Enemy));
            int swing = enemyLost - youLost;
            if (youLost + enemyLost > 0 && (turnTick < 0 || Math.Abs(swing) > Math.Abs(turnSwing))) { turnSwing = swing; turnTick = cur.Tick; turnYou = youLost; turnEnemy = enemyLost; }

            // first contact, and who struck first
            if (!contacted && evs.Count > 0)
            {
                contacted = true; contactTick = cur.Tick;
                bool youStruck = evs.Any(x => x.S.Side == Side.Player), theyStruck = evs.Any(x => x.S.Side == Side.Enemy);
                if (youStruck && theyStruck)
                {
                    var mine = evs.First(x => x.S.Side == Side.Player);
                    var theirs = evs.First(x => x.S.Side == Side.Enemy);
                    Add(cur.Tick, ChronicleKind.Contact, $"First contact. Both sides strike in the same tick: {Lab(mine.S)} on {Lab(mine.T)}, {Lab(theirs.S)} on {Lab(theirs.T)}.",
                        new ChronicleCite(CiteKind.Event, cur.Tick, mine.E.Source, mine.E.Target, mine.E.Damage));
                }
                else
                {
                    var first = evs[0];
                    Add(cur.Tick, ChronicleKind.Contact,
                        $"First contact. {Cap(Lab(first.S))} strike {Lab(first.T)}, and {(youStruck ? "the enemy has" : "you have")} not struck yet." + (evs.Count > 1 ? $" {evs.Count} strikes land this tick." : ""),
                        new ChronicleCite(CiteKind.Event, cur.Tick, first.E.Source, first.E.Target, first.E.Damage));
                }
            }

            // the gate
            if ((cur.WorldFlags & WorldFlag.GateFallen) != 0 && (prev.WorldFlags & WorldFlag.GateFallen) == 0)
                Add(cur.Tick, ChronicleKind.Gate, "The gate has fallen.", new ChronicleCite(CiteKind.Gate, cur.Tick));

            foreach (var (e, s, t) in evs)
            {
                // a flank: judged from the positions alone
                if (!flanked.Contains(s.Id) && Find(f0, s.Id) is { } s0)
                {
                    int across = Math.Abs(s.Y - s0.Y), dx = Math.Abs(s.X - t.X), dy = Math.Abs(s.Y - t.Y);
                    if (across >= FlankUnits * Q && dy >= dx)
                    {
                        flanked.Add(s.Id);
                        Add(cur.Tick, ChronicleKind.Flank, $"{Cap(Lab(s))} strike {Lab(t)} from the side, {across / Q} units across the field from where they began.",
                            new ChronicleCite(CiteKind.Event, cur.Tick, e.Source, e.Target, e.Damage));
                        if (s.Side == Side.Player && flankSlotsYou.Add(s.Slot)) flankTickOfSlot[s.Slot] = (cur.Tick, Lab(s));
                    }
                }

                // holy against cursed: only where the cohorts' own traits are known, and the rule is the battle's own (UnitRules.Hit)
                if (traitsOf is not null && holyLines < MaxHoly && !holyPairs.Contains((e.Source, e.Target))
                    && traitsOf(s.Side, s.Key) is { } ts && traitsOf(t.Side, t.Key) is { } tt && (ts & Trait.Holy) != 0 && (tt & Trait.Cursed) != 0)
                {
                    holyPairs.Add((e.Source, e.Target)); holyLines++;
                    Add(cur.Tick, ChronicleKind.Holy, $"{Cap(Lab(s))} strike {Lab(t)}, and holy blows hurt the cursed {HolyWords()}.",
                        new ChronicleCite(CiteKind.Event, cur.Tick, e.Source, e.Target, e.Damage));
                }

                // candidates for a heavy blow
                var before = Find(prev, e.Target);
                if (before is { } bt && e.Damage >= HeavyMin && e.Damage * 4 >= bt.Strength) blows.Add((cur.Tick, e.Source, e.Target, e.Damage));
            }

            // a run of strikes on one cohort
            var hitNow = new Dictionary<int, int>();
            foreach (var (e, _, _) in evs) hitNow[e.Target] = hitNow.GetValueOrDefault(e.Target) + e.Damage;
            foreach (var id in streakLen.Keys.Where(k => !hitNow.ContainsKey(k)).ToList()) { streakLen.Remove(id); streakFirst.Remove(id); streakDmg.Remove(id); }
            foreach (var (id, dmg) in hitNow.OrderBy(k => k.Key))
            {
                if (streakLen.ContainsKey(id)) { streakLen[id]++; streakDmg[id] += dmg; }
                else { streakLen[id] = 1; streakFirst[id] = cur.Tick; streakDmg[id] = dmg; }
                if (streakLen[id] >= RunTicks && runLines < MaxRuns && runDone.Add(id) && Find(cur, id) is { } tb)
                {
                    runLines++;
                    Add(cur.Tick, ChronicleKind.Run, $"{Cap(Lab(tb))} have been struck {streakLen[id]} ticks running, {streakDmg[id]} in all.",
                        new ChronicleCite(CiteKind.Run, cur.Tick, id, streakFirst[id], streakLen[id], streakDmg[id]));
                }
            }

            // a cohort breaks; when it was the last of its line standing, the line is gone
            foreach (var b in cur.Blobs.OrderBy(b => b.Id))
            {
                if (Find(prev, b.Id) is not { } p || p.Strength <= 0 || b.Strength > 0) continue;
                bool lineGone = cur.Blobs.Where(x => x.Side == b.Side && x.Slot == b.Slot).All(x => x.Strength <= 0);
                string text = $"{Cap(Lab(b))} break.";
                if (breaks == 0) { text += " The first to fall."; firstBreakTick = cur.Tick; firstBreakBlob = b.Id; }
                breaks++;
                if (lineGone)
                {
                    text += $" {Cap(b.Side == Side.Player ? "your" : "the enemy")} {DoctrineWords.Slot(b.Slot)} line is gone" + (linesGone == 0 ? ", the first line to fall." : ".");
                    if (linesGone == 0) { firstLineTick = cur.Tick; firstLineSide = b.Side; firstLineSlot = b.Slot; }
                    linesGone++;
                }
                Add(cur.Tick, ChronicleKind.Break, text, new ChronicleCite(CiteKind.Break, cur.Tick, b.Id));
            }

            // the final stand: a side down to its last cohort while the battle goes on
            if (i < n - 1)
                foreach (var side in new[] { Side.Player, Side.Enemy })
                    if (!standDone.Contains(side) && Standing(cur, side) == 1 && Standing(prev, side) >= 2)
                    {
                        standDone.Add(side);
                        var last = cur.Blobs.First(x => x.Side == side && x.Strength > 0);
                        Add(cur.Tick, ChronicleKind.Stand, $"Only {Lab(last)} still stand on {(side == Side.Player ? "your" : "their")} side, {last.Strength} strong.",
                            new ChronicleCite(CiteKind.Stand, cur.Tick, last.Id, 0, last.Strength));
                    }

            // lulls (after first contact only)
            if (contacted && cur.Tick > contactTick)
            {
                if (cur.Events.Count == 0) { if (emptyFrom < 0) emptyFrom = cur.Tick; emptyTo = cur.Tick; }
                else if (emptyFrom >= 0) { lulls.Add((emptyFrom, emptyTo)); emptyFrom = -1; }
            }
        }
        if (emptyFrom >= 0) lulls.Add((emptyFrom, emptyTo));

        // the heaviest blows
        int heavy = 0;
        foreach (var bl in blows.OrderByDescending(b => b.Dmg).ThenBy(b => b.Tick).ThenBy(b => b.Src).ThenBy(b => b.Tgt).Take(MaxHeavy))
        {
            var fr = F.First(f => f.Tick == bl.Tick);
            var s = Find(fr, bl.Src)!.Value; var t = Find(fr, bl.Tgt)!.Value;
            Add(bl.Tick, ChronicleKind.Blow, $"{(heavy == 0 ? "The heaviest blow of the battle" : "A heavy blow")}: {Lab(s)} strike {Lab(t)} for {bl.Dmg}.",
                new ChronicleCite(CiteKind.Event, bl.Tick, bl.Src, bl.Tgt, bl.Dmg));
            heavy++;
        }

        foreach (var (from, to) in lulls.Where(l => l.To - l.From + 1 >= LullTicks).OrderByDescending(l => l.To - l.From).ThenBy(l => l.From).Take(MaxLulls))
            Add(from, ChronicleKind.Lull, $"A lull: no blow lands from tick {from} to tick {to}.", new ChronicleCite(CiteKind.Span, from, from, to));

        // the end
        var fl = F[n - 1];
        int mineEnd = Total(fl, Side.Player), theirsEnd = Total(fl, Side.Enemy);
        string endText = replay.Result.Outcome switch
        {
            BattleStatus.AttackerWon => $"The field is yours. {mineEnd} of your soldiers stand; the enemy has {theirsEnd}.",
            BattleStatus.DefenderWon => $"The enemy holds the field. {theirsEnd} of theirs stand; you have {mineEnd}.",
            BattleStatus.Draw => mineEnd > 0 && theirsEnd > 0 ? $"Neither side held the field. You have {mineEnd}; the enemy has {theirsEnd}."
                                                              : $"Both sides fall together. You have {mineEnd}; the enemy has {theirsEnd}.",
            _ => $"The battle did not finish. You have {mineEnd}; the enemy has {theirsEnd}.",
        };
        if (mineEnd > 0 && theirsEnd > 0) endText += " The battle stopped at its tick limit.";
        Add(fl.Tick, ChronicleKind.End, endText, new ChronicleCite(CiteKind.End, fl.Tick, mineEnd, theirsEnd, (int)replay.Result.Outcome));

        var lines = rows.OrderBy(r => r.Line.Tick).ThenBy(r => r.Rank).ThenBy(r => r.Seq).Select(r => r.Line).ToList();

        // ---- the Why
        var why = new List<string>();
        if (turnTick >= 0)
            why.Add(turnSwing > 0 ? $"Turning point: tick {turnTick}, when the enemy lost {turnEnemy} {Soldiers(turnEnemy)} and you lost {turnYou}."
                  : turnSwing < 0 ? $"Turning point: tick {turnTick}, when you lost {turnYou} {Soldiers(turnYou)} and the enemy lost {turnEnemy}."
                  : $"Turning point: tick {turnTick}, when each side lost {turnYou} {Soldiers(turnYou)}.");
        else why.Add("No soldier fell on either side.");

        if (firstLineTick is { } flt)
            why.Add($"The first line to fall: {(firstLineSide == Side.Player ? "your" : "the enemy")} {DoctrineWords.Slot(firstLineSlot!.Value)} line, at tick {flt}.");
        else if (firstBreakTick is { } fbt && Find(F.First(f => f.Tick == fbt), firstBreakBlob!.Value) is { } fb)
            why.Add($"No line fell whole. The first cohort to break was {Lab(fb)} ({DoctrineWords.Slot(fb.Slot)} line) at tick {fbt}.");
        else why.Add("No cohort broke.");

        string? hint = null;
        // With a loss trigger the orders that stood at the moment of a break may be the ones after it, which the recording does not say; so no hint is made then.
        var fieldOrders = DoctrineVocabulary.Trigger(doctrine) is null ? DoctrineVocabulary.FieldOrders(doctrine) : Array.Empty<FieldOrder>();
        if (firstBreakBlob is { } fbb && firstBreakTick is { } fbTick && Find(F.First(f => f.Tick == fbTick), fbb) is { Side: Side.Player } broke
            && replay.Result.Outcome != BattleStatus.AttackerWon && fieldOrders.FirstOrDefault(o => o.Group == broke.Slot) is { Weight: > 0 } given)
            hint = $"Your {DoctrineWords.Slot(broke.Slot)} line broke first while ordered to {DoctrineWords.FieldOrders[(int)given.Kind].ToLowerInvariant()}. Try another order for it.";
        else
            foreach (var o in fieldOrders)
                if (o.Kind == FieldOrderKind.Flank && flankTickOfSlot.TryGetValue(o.Group, out var fx))
                { hint = $"Your order to {DoctrineWords.FieldOrders[(int)FieldOrderKind.Flank].ToLowerInvariant()} on the {DoctrineWords.Slot(o.Group)} line reached the enemy: {fx.Lab} struck from the side at tick {fx.Tick}."; break; }
        if (hint is not null) why.Add(hint);

        return new Chronicle(lines, new ChronicleWhy(turnTick >= 0 ? turnTick : null, turnYou, turnEnemy, firstBreakTick, firstBreakBlob, firstLineTick, firstLineSide, firstLineSlot, hint, why));
    }

    /// <summary>How much harder a holy blow hits a cursed target, said in words and READ from the battle's own hit rule (UnitRules.Hit), not typed.</summary>
    public static string HolyWords()
    {
        const int probe = 1_000_000;
        int plain = UnitRules.Hit(probe, Trait.None, 100, Trait.Cursed, false), holy = UnitRules.Hit(probe, Trait.Holy, 100, Trait.Cursed, false);
        int pct = (int)((long)holy * 100 / plain);
        return pct == 150 ? "half again as hard" : $"{pct - 100}% harder";
    }
}
