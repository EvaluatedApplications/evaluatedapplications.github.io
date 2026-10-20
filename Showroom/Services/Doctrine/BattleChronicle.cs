using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

// The battle chronicle: the account of one recorded battle, in the Count's voice (todo\doctrine-story-from-mechanics.md). A pure function of the
// recording, the doctrine, the arena's name and the kind names: the same replay always gives the same log. NOTHING here simulates anything and
// no line is flavour. Every line is read from the recording and carries a CITE, the exact thing in the recording that backs it, so a checker
// can look the line up again (tools\DoctrineUiProof does, with its own code). A line the recording cannot back is not written.

public enum ChronicleKind { Start, Siege, Contact, Gate, Clock, Flank, Holy, DugIn, Blow, Run, Break, Stand, Lull, End }

public enum CiteKind
{
    /// <summary>Frame 0: A = your soldiers, B = the enemy's.</summary>
    Start,
    /// <summary>Frame 0 of a siege: A = the side the recording's header names as besieger (0 yours, 1 the enemy's), Value = the arena's siege clock in ticks (0 when the caller did not know it).</summary>
    Siege,
    /// <summary>The clock-fell bit is set in the frame at <c>Tick</c> and was not in the frame before; A = the besieger's side as the header names it. No frame before this one has a blow dealt or taken by that side.</summary>
    Clock,
    /// <summary>The blob A is DUG IN in the frame at <c>Tick</c>: Value ticks running (the battle's set time) it was struck in no frame and stood within a melee reach of where its stillness began; Value2 = the bonus percent its traits give.</summary>
    Set,
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
    /// How far, in 1/16 of an arena unit, a cohort's recorded place may differ from the battle's exact one in a comparison with the melee reach: each place is rounded to
    /// the nearest 1/16 in x and y, so two of them differ from the truth by at most 1.42 of these units. A distance within this margin of the reach is NOT PROVABLE from the
    /// frames and the cohort is not called dug in from there on (until a blow resets it).
    /// </summary>
    public const double DugInMargin = 1.5;

    sealed class Still(int x, int y)
    {
        public int Count, RX = x, RY = y;
        public bool Known = true, Set;
        public void Reset(ReplayBlob b) { Count = 0; RX = b.X; RY = b.Y; Known = true; Set = false; }
    }

    /// <summary>
    /// Build the chronicle. <paramref name="kindName"/> names a kind key (default: the roster). <paramref name="traitsOf"/> gives the traits a cohort
    /// really fought with (side, kind key), or null when they are not known; without it no holy line is written, because a flip can change a kind's traits,
    /// and no dug-in line, because the bonus is the cohort's own. <paramref name="siegeClock"/> is the arena's siege clock in ticks (0 when not known); the
    /// recording names the besieger (version 3) but not the clock.
    /// </summary>
    public static Chronicle Build(BattleReplay replay, DoctrineSet doctrine, string arenaName, Func<int, string>? kindName = null, Func<Side, int, Trait?>? traitsOf = null, int siegeClock = 0)
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

        // ---- the siege: who besieges is in the recording's header; the clock is the arena's, handed in by the caller
        Side? besieger = replay.Besieger;
        if (besieger is { } bs0)
        {
            string head = bs0 == Side.Player ? "A siege. You besiege" : "A siege. The enemy besieges";
            string said = siegeClock > 0
                ? head + $", and the siege clock is {siegeClock} ticks: " + (bs0 == Side.Player
                    ? "if you have traded no blow by then, you lose your stance and every company of yours must advance."
                    : "if it has traded no blow by then, it loses its stance and every company of it must advance. You may wait.")
                : head + ".";
            Add(f0.Tick, ChronicleKind.Siege, said, new ChronicleCite(CiteKind.Siege, f0.Tick, (int)bs0, 0, siegeClock));
        }
        bool besiegerTraded = false;
        var still = new Dictionary<int, Still>();
        foreach (var b in f0.Blobs) still[b.Id] = new Still(b.X, b.Y);
        var dug = new (int Tick, ReplayBlob Blob)?[2];     // the first cohort of each side to be dug in, by side

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

            // the siege clock: the bit is first set in this frame, and no frame before it holds a blow dealt or taken by the besieger (else the line is not written)
            if (besieger is { } sg && (cur.WorldFlags & WorldFlag.ClockFell) != 0 && (prev.WorldFlags & WorldFlag.ClockFell) == 0 && !besiegerTraded)
                Add(cur.Tick, ChronicleKind.Clock, sg == Side.Player
                    ? "The siege clock has run out. You had traded no blow, so you lose your stance: every company of yours now advances."
                    : "The siege clock has run out. The enemy had traded no blow, so it loses its stance: every company of theirs now advances.",
                    new ChronicleCite(CiteKind.Clock, cur.Tick, (int)sg));

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

            // dug in: the battle's own rule (UnitRules.SetTicks still ticks: struck in none, and within a melee reach of where the stillness began), followed on the recorded places.
            // A strike on the cohort, or a clear move of more than the reach, starts the count again from where it stands; a distance too close to the reach to tell is not provable.
            var struck = new HashSet<int>();
            foreach (var e in cur.Events) struck.Add(e.Target);
            double reach = UnitRules.MeleeRange * Q;
            foreach (var b in cur.Blobs.OrderBy(x => x.Id))
            {
                if (!still.TryGetValue(b.Id, out var st)) continue;
                if (b.Strength <= 0) { st.Set = false; st.Known = false; continue; }
                if (struck.Contains(b.Id)) { st.Reset(b); continue; }
                if (!st.Known) continue;
                double dist = Math.Sqrt((double)(b.X - st.RX) * (b.X - st.RX) + (double)(b.Y - st.RY) * (b.Y - st.RY));
                if (dist > reach + DugInMargin) st.Reset(b);
                else if (dist < reach - DugInMargin)
                {
                    st.Count++;
                    if (st.Count >= UnitRules.SetTicks && !st.Set) { st.Set = true; if (dug[(int)b.Side] is null) dug[(int)b.Side] = (cur.Tick, b); }
                }
                else { st.Known = false; st.Set = false; }
            }

            // the besieger has traded a blow once a frame holds one dealt or taken by its side
            if (besieger is { } sb)
                foreach (var (_, s, t) in evs) if (s.Side == sb || t.Side == sb) besiegerTraded = true;
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

        // dug in: the first cohort of each side to be so, where its traits (and so its bonus) are known
        if (traitsOf is not null)
            foreach (var side in new[] { Side.Player, Side.Enemy })
                if (dug[(int)side] is { } dg && traitsOf(side, dg.Blob.Key) is { } tr)
                {
                    int bonus = UnitRules.SetBonusPct(tr);
                    string edge = bonus > 0 ? $"They strike {bonus}% harder until they are hit or move." : "Their reach is already the longest, so it gains them nothing.";
                    Add(dg.Tick, ChronicleKind.DugIn, $"{Cap(Lab(dg.Blob))} are dug in: {DoctrineWords.Cardinal(UnitRules.SetTicks)} ticks unhit and in place. {edge}",
                        new ChronicleCite(CiteKind.Set, dg.Tick, dg.Blob.Id, 0, UnitRules.SetTicks, bonus));
                }

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
        if (mineEnd > 0 && theirsEnd > 0)
            endText += replay.Result.Outcome == BattleStatus.Draw ? " The battle stopped at its tick limit with the power left on the two sides equal."
                                                                  : " The battle stopped at its tick limit, where the field goes to the side with the greater power left, not to the side with more soldiers.";
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
