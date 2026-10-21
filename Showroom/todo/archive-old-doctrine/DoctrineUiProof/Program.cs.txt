using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using Doctrine;
using Doctrine.Battle;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Pages;
using ShowroomDoctrine;

// UI proof for Doctrine. See the csproj header. The page is the REAL one, the sections are the REAL ones (Pleroma army, Tracer battle,
// tiny HoloFormer general), the taps are a visitor's. Nothing is launched: bUnit renders the component in memory.
string showroom = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\.."));
int failures = 0, checks = 0;
const int RecordWindowHand = 64;          // written out by hand: the window the match record is counted over, as the words state it (probed on a real general below)
void Check(string name, bool ok, string detail = "")
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    if (!ok) failures++;
}
// The pages change state from the pipeline's own thread; bUnit caches the parsed DOM and a render racing a read can leave the cache stale, so a
// wait that has not seen its condition for half a second asks the live pages to re-render once (this is a test-harness matter, not the page's).
var livePages = new List<IRenderedComponent<DoctrinePage>>();
bool reported = false;
async Task<bool> Until(Func<bool> cond, int ms, string what)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    long nextRefresh = 500;
    while (sw.ElapsedMilliseconds < ms)
    {
        try { if (cond()) return true; } catch { }
        if (sw.ElapsedMilliseconds >= nextRefresh) { nextRefresh += 500; foreach (var lp in livePages.ToArray()) { try { lp.Render(); } catch (ObjectDisposedException) { } catch (Exception rex) { Console.WriteLine("    (render failed: " + rex.GetType().Name + ": " + rex.Message + " :: " + rex.StackTrace?.Split('\n')[0] + ")"); } } }
        await Task.Delay(25);
    }
    Console.WriteLine($"  (timed out after {ms} ms waiting for: {what})");
    foreach (var lp in livePages.ToArray()) { try { var ss = lp.Instance.Session; if (ss is { InGame: true }) Console.WriteLine($"    page stage {ss.Stage}, records {ss.Records.Count}, status '{ss.Status}', error '{ss.Error}'; dom has result {lp.FindAll(".dc-result").Count}; dom text: " + Regex.Replace(lp.Find(".dc").TextContent, @"\s+", " ").Substring(0, 400)); } catch (Exception ex) { Console.WriteLine("    (could not read a page: " + ex.GetType().Name + ")"); } }
    return false;
}

BunitContext NewCtx(bool prerender)
{
    var c = new BunitContext();
    c.JSInterop.Mode = JSRuntimeMode.Loose;
    c.Services.AddSingleton<IBuildTime>(new Time(prerender));
    c.Services.AddSingleton<IDoctrineSettings>(new DoctrineSettings { AutoPlay = false });
    return c;
}

var allText = new List<string>();   // every screen's visible text, scanned for wording at the end
var allMarkup = new List<string>();
var playText = new List<string>();   // the same screens WITHOUT the "How it is built" expander: the words a player plays by
void Scan(IRenderedComponent<DoctrinePage> p)
{
    allText.Add(Regex.Replace(p.Find(".dc").TextContent, @"\s+", " ")); allMarkup.Add(p.Markup);
    var clone = (AngleSharp.Dom.IElement)p.Find(".dc").Clone(true);
    foreach (var d in clone.QuerySelectorAll("details.dc-built").ToList()) d.Remove();
    playText.Add(Regex.Replace(clone.TextContent, @"\s+", " "));
}
// --shots <dir>: write the page markup of the named states (the screenshot script wraps them in the real stylesheets)
string shotsDir = Array.IndexOf(args, "--shots") is var si and >= 0 && si + 1 < args.Length ? args[si + 1] : "";
void Shot(string name, IRenderedComponent<DoctrinePage> p) { if (shotsDir.Length == 0) return; Directory.CreateDirectory(shotsDir); File.WriteAllText(Path.Combine(shotsDir, name + ".html"), p.Markup); }

// ---------------------------------------------------------------------------------------------- the static shell
using (var sctx = NewCtx(true))
{
    var shell = sctx.Render<DoctrinePage>();
    Check("shell: the heading and lede are real text", shell.Markup.Contains("Doctrine") && Regex.Replace(shell.Find(".lede").TextContent, @"\s+", " ").Contains("Your general learns how you set orders from your own choices, in your browser, with nothing pre-trained") && shell.Find(".lede").TextContent.Contains("Nothing is uploaded") && !shell.Find(".lede").TextContent.Contains("Pleroma"));
    Check("shell: the start button is there and the game is not", shell.FindAll("[data-blazier-go]").Count == 1 && shell.FindAll("#dc-start").Count == 0 && shell.FindAll("#dc-new").Count == 0);
}

// ---------------------------------------------------------------------------------------------- the script (a visitor's habits)
// The SAME script drives the page by taps and drives a headless game as an IPlayerInput, so the two games can be compared digest for digest.
var script = new ScriptHands([ScriptHands.How.Manual, ScriptHands.How.Ignore, ScriptHands.How.Accept]);

// A seed whose first battle is on the falling-gate arena AND runs past the tick the gate falls (so the recording carries the gate's flag). Found
// headless, with the script, on the same composition root.
int gateSeed = -1;
for (int sd = 1; sd < 400 && gateSeed < 0; sd++)
{
    var arenas = new DoctrineSections().Build(script, DoctrineSession.GameRules(1));
    var arena = arenas.Context.Battle.Arenas[(int)(GameRng.Mix(sd, 3000) % (ulong)arenas.Context.Battle.Arenas.Count)];
    if (arena.Id != "falling-gate") continue;
    var r = await new DoctrineSections().Build(script, DoctrineSession.GameRules(1)).PlayAsync(sd);
    if (r.Rounds[0].Replay.Frames.Any(f => f.WorldFlags != 0)) gateSeed = sd;
}
Check("setup: a seed was found whose first battle is on the falling-gate arena and sees the gate fall", gateSeed > 0, "seed " + gateSeed);

// ---------------------------------------------------------------------------------------------- game A: three rounds through the real page
using var ctx = NewCtx(false);
var page = ctx.Render<DoctrinePage>();
livePages.Add(page);
DoctrineSession S = page.Instance.Session!;
bool Has(IRenderedComponent<DoctrinePage> p, string sel) => p.FindAll(sel).Count > 0;
string Text(IRenderedComponent<DoctrinePage> p, string sel) => p.Find(sel).TextContent.Trim();
async Task Tap(IRenderedComponent<DoctrinePage> p, string sel, int skip = 0)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        var all = p.FindAll(sel);
        if (skip >= all.Count) { Check($"tap target missing: {sel} #{skip}", false); return; }   // a missing control is a red check, not a crash
        var el = all[skip];
        try { await p.InvokeAsync(() => el.Click()); return; }
        catch (Bunit.Rendering.UnknownEventHandlerIdException) { p.Render(); }              // the known bUnit stale-DOM race: look again and tap again
    }
}
async Task TapChip(IRenderedComponent<DoctrinePage> p, int token)
{
    await Tap(p, $"[data-token='{token}']");        // the council's cards are all in the page while the council is on the table
}
// The game asks whether to pay for another council whenever the purse could: the scripted visitor keeps the council it was dealt.
async Task PassAsk(IRenderedComponent<DoctrinePage> p, int ms = 30000)
{
    await Until(() => Has(p, ".dc-editor") || Has(p, "#dc-keep"), ms, "the council (a question or the choice)");
    if (Has(p, "#dc-keep") && !Has(p, ".dc-editor"))
    {
        await Tap(p, "#dc-keep");
        await Until(() => Has(p, ".dc-editor"), ms, "the council after keeping it");
    }
}
async Task TapPack(IRenderedComponent<DoctrinePage> p, int index)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try { await p.InvokeAsync(() => p.FindAll(".dc-pack")[index].Click()); return; }
        catch (Bunit.Rendering.UnknownEventHandlerIdException) { p.Render(); }
    }
}
// The grounds, written out by hand: the catalogue's order, the names, the clocks and the draw (seed, round) -> ground, so the page's briefing is compared with something that is not the game's own answer.
string[] arenaIdsHand = ["open-field", "chokepoint", "ridge", "falling-gate"];
string ArenaIdHand(int seed, int round) => arenaIdsHand[(int)(GameRng.Mix(seed, 3000 + round) % 4)];
Side? BesiegerHand(string id, int round) => id is "chokepoint" or "falling-gate" ? (round % 2 == 0 ? Side.Player : Side.Enemy) : null;
string ArenaNameHand(string id) => id switch { "open-field" => "Open Field", "chokepoint" => "The Chokepoint", "ridge" => "Ridge Road", _ => "The Falling Gate" };
int ClockHand(string id) => id switch { "chokepoint" => 14, "falling-gate" => 16, _ => 0 };
string GroundHand(string id, int round) => BesiegerHand(id, round) is { } by
    ? $"This round: {ArenaNameHand(id)}, a siege. {(by == Side.Player ? "You besiege" : "The enemy besieges")}; the clock is {ClockHand(id)} ticks."
    : $"This round: {ArenaNameHand(id)}, an open ground with no siege clock.";
var groundProblems = new List<string>(); int groundChecked = 0, limitChecked = 0, limitStopped = 0;
void GroundSeen(IRenderedComponent<DoctrinePage> p, string where)
{
    var s0 = p.Instance.Session!;
    string id = ArenaIdHand(s0.Seed, s0.RoundIndex), want = GroundHand(id, s0.RoundIndex);
    groundChecked++;
    string got = Has(p, ".dc-groundline") ? Text(p, ".dc-groundline") : "(no line)";
    if (got != want || s0.Ground is not { } g || g.Info.Id != id || g.Besieger != BesiegerHand(id, s0.RoundIndex) || g.SiegeClock != ClockHand(id)) groundProblems.Add($"{where} seed {s0.Seed} round {s0.RoundIndex}: page says '{got}', hand says '{want}'");
}
// After a round has closed: a battle the tick limit stopped (both sides stood at the last frame) says so on the result screen, and that POWER left, not the count of soldiers, gave the field; any other battle says nothing of it.
void LimitAfter(IRenderedComponent<DoctrinePage> p, int round)
{
    var rec = p.Instance.Session!.Records[round];
    var lastF = rec.Replay.Frames[^1];
    bool stopped = lastF.Blobs.Any(b => b.Side == Side.Player && b.Strength > 0) && lastF.Blobs.Any(b => b.Side == Side.Enemy && b.Strength > 0);
    var got = p.FindAll(".dc-recap-limit").Select(e => e.TextContent.Trim()).ToList();
    string want = rec.Result.Outcome switch
    {
        BattleStatus.AttackerWon => "The battle stopped at its tick limit, and the field went to you: the greater power left decides it, not the number of soldiers.",
        BattleStatus.DefenderWon => "The battle stopped at its tick limit, and the field went to the enemy: the greater power left decides it, not the number of soldiers.",
        BattleStatus.Draw => "The battle stopped at its tick limit with the power left on the two sides equal.",
        _ => "The battle stopped at its tick limit.",
    };
    limitChecked++;
    if (stopped) { limitStopped++; if (!got.SequenceEqual([want])) groundProblems.Add($"round {round}: the tick limit stopped it ({rec.Result.Outcome}) but the result screen says [{string.Join(" / ", got)}], wanted [{want}]"); }
    else if (got.Count != 0) groundProblems.Add($"round {round}: a battle that ended before the limit says it stopped at it");
}
// After a round has closed (the result screen at tick 0): the record's ground and besieger are what the briefing said, and the result screen says the siege in the record's own words, from the recording's header and bit.
void GroundAfter(IRenderedComponent<DoctrinePage> p, int round)
{
    var s0 = p.Instance.Session!;
    if (round >= s0.Records.Count) { groundProblems.Add("no record for round " + round); return; }
    var rec = s0.Records[round];
    string id = ArenaIdHand(s0.Seed, round);
    var by = BesiegerHand(id, round);
    groundChecked++;
    if (rec.ArenaId != id || rec.Replay.Besieger != by || rec.Replay.Version != (by is null ? 2 : 3)) groundProblems.Add($"round {round}: the battle was on {rec.ArenaId} with besieger {rec.Replay.Besieger} (v{rec.Replay.Version}), the hand says {id} and {by}");
    var lines = p.FindAll(".dc-recap-siege").Select(e => e.TextContent.Trim()).ToList();
    var chips = p.FindAll(".dc-siegechip");
    if (by is null) { if (lines.Count != 0 || chips.Count != 0) groundProblems.Add($"round {round}: an open ground shows siege lines or a chip"); return; }
    string who = by == Side.Player ? "you" : "the enemy";
    var want = new List<string> { $"{ArenaNameHand(id)} is a siege, and {(by == Side.Player ? "you besieged" : "the enemy besieged")} it: the clock was {ClockHand(id)} ticks." };
    int fellAt = rec.Replay.Frames.Where(f => (f.WorldFlags & 2) != 0).Select(f => f.Tick).DefaultIfEmpty(-1).First();
    bool traded = rec.Replay.Frames.Any(f => f.Tick <= ClockHand(id) && f.Events.Any(e => f.Blobs.Any(b => (b.Id == e.Source || b.Id == e.Target) && b.Side == by)));
    if (fellAt >= 0) want.Add($"The clock fell at tick {fellAt}: {who} had traded no blow by then, lost the stance, and every company of {(by == Side.Player ? "yours" : "theirs")} advanced.");
    else if (rec.Result.Ticks > ClockHand(id) && traded) want.Add($"{(by == Side.Player ? "You" : "The enemy")} traded a blow before the clock ran out, and kept the stance.");
    string chipWant = (by == Side.Player ? "You besiege" : "The enemy besieges") + $": the clock is {ClockHand(id)} ticks";
    if (!lines.SequenceEqual(want) || chips.Count != 1 || Regex.Replace(chips[0].TextContent, @"\s+", " ").Trim() != chipWant) groundProblems.Add($"round {round}: recap '{string.Join(" / ", lines)}' chip '{(chips.Count == 1 ? chips[0].TextContent.Trim() : chips.Count.ToString())}', wanted '{string.Join(" / ", want)}' and '{chipWant}'");
}
async Task DoDraft(IRenderedComponent<DoctrinePage> p, int round, bool pass = true)
{
    await Until(() => Has(p, ".dc-draft"), 20000, "draft screen");
    var sess = p.Instance.Session!;
    GroundSeen(p, "draft");
    if (!Has(p, ".dc-musternote") || Text(p, ".dc-musternote") != DoctrineWords.MusterNote) groundProblems.Add("the muster note differs");
    foreach (int i in ScriptHands.PackIndices(sess.Draft!.Offer, sess.Draft.Army.Gold)) await TapPack(p, i);   // a visitor taps the companies the script wants
    Scan(p);
    await Tap(p, "#dc-confirm");
    if (pass) await PassAsk(p, 40000);
}
var roundTimes = new List<double>();
async Task DoChoose(IRenderedComponent<DoctrinePage> p, ScriptHands sh, int round)
{
    await PassAsk(p, 40000);
    await Until(() => Has(p, ".dc-editor"), 20000, "doctrine screen");
    GroundSeen(p, "council");
    var st = sh.Decide(round, p.Instance.Session!.Army!, p.Instance.Session!.Ghost!, p.Instance.Session!.OrdersOffered!);
    Console.WriteLine($"  (hands, round {round}: {st.How} {string.Join(" ", st.Tokens.Select(DoctrineVocabulary.Name))}; army {p.Instance.Session!.Army!.Soldiers} soldiers, {p.Instance.Session!.Army!.Gold} gold)");
    Scan(p);
    switch (st.How)
    {
        case ScriptHands.How.Accept: await Tap(p, "#dc-accept"); return;
        case ScriptHands.How.Edit:
            await Tap(p, "#dc-edit");
            await Tap(p, ".dc-slot", st.ClearSlot);
            foreach (var t in st.Tokens) await TapChip(p, t);
            break;
        case ScriptHands.How.Ignore:
            await Tap(p, "#dc-ignore");
            foreach (var t in st.Tokens) await TapChip(p, t);
            break;
        default:
            foreach (var t in st.Tokens) await TapChip(p, t);
            break;
    }
    await Tap(p, "#dc-fight");
}

// ============================================================================================== THE WAR (the overmap, campaign mode)
// Everything below is checked against HAND-WRITTEN knowledge of the rules (the road names, the feature table, which kinds a feature opens, the lane rule, who takes what in
// a race, the standards, the ordinals), not against the page's own words, and the map and the cards are compared with fresh reads of the library (Provinces(), BriefingAsync()).
bool warOnly = args.Contains("--war");
string[] RoadsHand = ["left road", "middle road", "right road"];
string[] OrdHand = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];
string FeatHand(Feature f) => f switch { Feature.Barracks => "Barracks", Feature.Stables => "Stables", Feature.Shrine => "Shrine", Feature.Granary => "Granary", _ => "Open country" };
int[] KindIdsHand(Feature f) => f switch { Feature.Barracks => [0, 1], Feature.Stables => [2, 6], Feature.Shrine => [4], _ => [] };
string OwnerWordHand(Holder h) => h switch { Holder.Player => "Yours", Holder.Enemy => "The enemy's", _ => "Nobody's" };
string JoinHand(IReadOnlyList<string> l) => l.Count == 0 ? "none" : l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1];
string ProvMidHand(Province p) => p.IsCapital ? (p.Key == 36 ? "your capital" : "the enemy capital") : ProvHand(p);
string ProvHand(Province p) => p.IsCapital ? (p.Key == 36 ? "Your capital" : "The enemy capital") : $"{FeatHand(p.Feature)}, {OrdHand[p.Index + 1]} from your capital on the {RoadsHand[p.Lane]}";
Province? PlayerTargetHand(IReadOnlyList<Province> ps, int lane)
{
    foreach (var p in ps.Where(x => !x.IsCapital && x.Lane == lane).OrderBy(x => x.Index)) if (p.Owner != Holder.Player) return p;
    var ec = ps.FirstOrDefault(x => x.Key == 37);
    return ec is { Owner: not Holder.Player } ? ec : null;
}
Province? EnemyTargetHand(IReadOnlyList<Province> ps, int lane)
{
    foreach (var p in ps.Where(x => !x.IsCapital && x.Lane == lane).OrderByDescending(x => x.Index)) if (p.Owner != Holder.Enemy) return p;
    var pc = ps.First(x => x.Key == 36);
    return pc.Owner != Holder.Enemy ? pc : null;
}
Feature HeldHand(IReadOnlyList<Province> ps) { var h = Feature.None; foreach (var p in ps) if (!p.IsCapital && p.Owner == Holder.Player) h |= p.Feature; return h; }
List<int> PoolHand(Feature held)
{
    var set = new SortedSet<int> { 3, 5, 7 };
    foreach (var f in new[] { Feature.Barracks, Feature.Stables, Feature.Shrine }) if (held.HasFlag(f)) foreach (var k in KindIdsHand(f)) set.Add(k);
    return set.ToList();
}
string NormText(AngleSharp.Dom.IElement e) => Regex.Replace(e.TextContent, @"\s+", " ").Trim();
int LaneOf(int seed, int epoch) => ScriptedLanes.Lane(seed, epoch);          // the seeded rule of the scripted enemy, which the proof needs to KNOW to plan its picks (the page never asks)

// seeds, found by the schedule alone (no battle is played to find them)
int warSeed = -1, sweepSeed = -1, sweepLane = -1, tripleSeed = -1, endlessSeed = -1, endlessLane = -1, quietSeed = 7;
for (int sd = 1; sd < 5000 && (warSeed < 0 || sweepSeed < 0 || tripleSeed < 0 || endlessSeed < 0); sd++)
{
    int l0 = LaneOf(sd, 0), l1 = LaneOf(sd, 1), l2 = LaneOf(sd, 2);
    if (warSeed < 0 && l2 != l0 && l1 != l2) warSeed = sd;                                                   // push the lane the enemy picks at epoch 2 (a race), fight epoch 1 elsewhere, hold back at epoch 2
    var distinct = new HashSet<int> { l0, l1, l2 };
    if (sweepSeed < 0 && distinct.Count == 2) { sweepSeed = sd; sweepLane = Enumerable.Range(0, 3).First(x => !distinct.Contains(x)); }    // three races up one lane that the enemy never picks
    if (tripleSeed < 0 && distinct.Count == 1) tripleSeed = sd;                                              // the enemy picks one lane three times running
    if (endlessSeed < 0) { var two = new HashSet<int> { l0, l1 }; endlessSeed = sd; endlessLane = Enumerable.Range(0, 3).First(x => !two.Contains(x)); }
}
Check("war: seeds were found from the schedule alone (a mixed war, a sweep, a triple, an endless run)", warSeed > 0 && sweepSeed > 0 && tripleSeed > 0 && endlessSeed > 0, $"{warSeed} {sweepSeed}/{sweepLane} {tripleSeed} {endlessSeed}/{endlessLane}");

// ---- the start screen: the war section beside the fixed games
Check("war: the start screen offers the war and the endless run beside the 3, 5 and 8 round games (radio buttons, a start button, the seed field shared); the fixed options are still three",
    page.FindAll(".dc-opt").Count == 3 && page.FindAll(".dc-mode[role=radio]").Count == 2 && Has(page, "#dc-mode-war") && Has(page, "#dc-mode-endless") && Has(page, "#dc-campaign") && Has(page, "#dc-seed")
    && Text(page, "#dc-mode-war") == "The war" && Text(page, "#dc-mode-endless") == "Endless" && Text(page, "#dc-campaign") == "Start the war");
await Tap(page, "#dc-mode-endless");
Check("war: choosing Endless says so and relabels the start button", Text(page, "#dc-mode-endless").Length > 0 && page.Find("#dc-mode-endless").GetAttribute("aria-checked") == "true" && Text(page, "#dc-campaign") == "Start the endless war" && Text(page, ".dc-modehint").Contains("no capital"));
await Tap(page, "#dc-mode-war");
Check("war: the war's start text states the epoch limit the session passes to the game and the map's shape from the library", Text(page, ".dc-modehint").Contains($"{DoctrineSession.WarEpochs} epochs") && DoctrineSession.WarEpochs == 24 && Text(page, "#dc-war").Contains("three roads of three provinces"));
Scan(page);

// ---- the stylesheet: the colours of the map, the pips and the road column
{
    string wcss = File.ReadAllText(Path.Combine(showroom, "Pages", "DoctrinePage.razor.css"));
    Check("war: map colours (stylesheet): yours is the accent, the enemy's the bad colour, nobody's the faint ink, the target a dashed warn outline",
        Regex.IsMatch(wcss, @"\.dc-p-player \.dc-box \{[^}]*fill: color-mix\(in srgb, var\(--accent\)[^}]*stroke: var\(--accent\)") && Regex.IsMatch(wcss, @"\.dc-p-enemy \.dc-box \{[^}]*var\(--bad\)[^}]*stroke: var\(--bad\)")
        && Regex.IsMatch(wcss, @"\.dc-p-neutral \.dc-box \{[^}]*stroke: var\(--ink-faint\)") && Regex.IsMatch(wcss, @"\.dc-target \{[^}]*stroke: var\(--warn\)[^}]*stroke-dasharray"));
    Check("war: layout (css): the map has no fixed width, the road cards take the general's column on a wide screen and a sheet on a phone (at most 62% of the stage), the standards are pips",
        Regex.IsMatch(wcss, @"\.dc-mapsvg \{[^}]*width: 100%;[^}]*height: auto") && Regex.IsMatch(wcss, @"min-width: 900px\) \{[\s\S]*\.dc-lanepanel \{ grid-column: 3; grid-row: 1 / -1; \}") && Regex.IsMatch(wcss, @"\.dc-lanepanel \{ max-height: 62%; \}")
        && Regex.IsMatch(wcss, @"\.dc-pips \.pip\.on \{[^}]*background: var\(--accent\)") && Regex.IsMatch(wcss, @"\.dc-push \{ width: 100%;"));
}

// ---- the number of provinces the drawing shows, and what is checked at every state of the map
async Task CheckMap(IRenderedComponent<DoctrinePage> p, string tag)
{
    var s0 = p.Instance.Session!; var c = s0.Campaign!;
    var ps = c.Provinces(); var brief = await c.BriefingAsync();
    int hi = Math.Max(2, ps.Where(x => !x.IsCapital).Max(x => x.Index)), lo = Math.Max(0, hi - 3);
    var shown = ps.Where(x => x.IsCapital || (x.Index >= lo && x.Index <= hi)).ToList();
    var targets = Enumerable.Range(0, 3).Select(l => PlayerTargetHand(ps, l)).Where(t => t is not null).Select(t => t!.Key).ToHashSet();
    var problems = new List<string>();
    if (p.FindAll("svg.dc-mapsvg g.dc-prov").Count != shown.Count) problems.Add($"{p.FindAll("svg.dc-mapsvg g.dc-prov").Count} drawn, {shown.Count} to draw");
    foreach (var pr in shown)
    {
        var gs = p.FindAll($"g.dc-prov[data-key='{pr.Key}']");
        if (gs.Count != 1) { problems.Add($"province {pr.Key}: {gs.Count} drawn"); continue; }
        var g = gs[0]; var arena = s0.Grounds![pr.ArenaIndex % s0.Grounds.Count];
        string own = pr.Owner switch { Holder.Player => "player", Holder.Enemy => "enemy", _ => "neutral" };
        string cls = pr.Owner switch { Holder.Player => "dc-p-player", Holder.Enemy => "dc-p-enemy", _ => "dc-p-neutral" };
        string txt = NormText(g);
        if (g.GetAttribute("data-owner") != own || !g.ClassList.Contains(cls) || g.ClassList.Count(k => k.StartsWith("dc-p-")) != 1) problems.Add($"province {pr.Key}: owner/colour {g.GetAttribute("data-owner")} {string.Join(" ", g.ClassList)} for {own}");
        if (g.GetAttribute("data-feature") != pr.Feature.ToString() || g.GetAttribute("data-yield") != pr.Yield.ToString() || g.GetAttribute("data-lane") != pr.Lane.ToString() || g.GetAttribute("data-index") != pr.Index.ToString()) problems.Add($"province {pr.Key}: feature/yield/place");
        if (g.GetAttribute("data-arena") != arena.Id || g.GetAttribute("data-siege") != (arena.SiegeClock > 0 ? "true" : "false") || !txt.Contains(arena.Name)) problems.Add($"province {pr.Key}: arena {g.GetAttribute("data-arena")} vs {arena.Id}");
        if (!txt.Contains($"Pays {pr.Yield} gold") || !txt.Contains(OwnerWordHand(pr.Owner))) problems.Add($"province {pr.Key}: yield or holder words");
        if (!pr.IsCapital && (!txt.Contains(FeatHand(pr.Feature)) || (arena.SiegeClock > 0) != txt.Contains("Siege") || (arena.SiegeClock == 0 && !txt.Contains("Open ground")))) problems.Add($"province {pr.Key}: feature or siege mark");
        if (!pr.IsCapital && (g.QuerySelectorAll(".dc-ic").Length != (pr.Feature == Feature.None ? 0 : 1) || (arena.SiegeClock > 0) != (g.QuerySelectorAll(".dc-siegemark").Length == 1))) problems.Add($"province {pr.Key}: icon or siege shape");
        if (pr.IsCapital && !txt.Contains(pr.Key == 36 ? "Your capital" : "The enemy capital")) problems.Add($"capital {pr.Key}: name");
        if (g.GetAttribute("data-target") != (targets.Contains(pr.Key) ? "true" : "false") || (g.QuerySelectorAll(".dc-target").Length == 1) != targets.Contains(pr.Key)) problems.Add($"province {pr.Key}: target mark");
    }
    if (s0.Endless && p.FindAll("g.dc-prov[data-key='37']").Count != 0) problems.Add("an endless map draws an enemy capital");
    Check($"war: the map shown is the library's Provinces() ({tag}): every province in view once, its owner (and the colour class that goes with it), feature, yield, place, arena (name and siege mark) and the dashed target on exactly the provinces a push would assault",
        problems.Count == 0, string.Join(" | ", problems.Take(3)));
    var tk = brief.Lanes.Where(l => l.Target is not null).Select(l => l.Target!.Key).ToHashSet();
    Check($"war: the targets the briefing names are the first province outward that the player does not hold, or the enemy capital ({tag})", tk.SetEquals(targets));
    int sum = ps.Where(x => x.Owner == Holder.Player).Sum(x => x.Yield);
    string standing = NormText(p.Find("#dc-standards")), held = NormText(p.Find("#dc-held"));
    var heldNames = new[] { Feature.Barracks, Feature.Stables, Feature.Shrine, Feature.Granary }.Where(f => HeldHand(ps).HasFlag(f)).Select(FeatHand).ToList();
    var pool = PoolHand(HeldHand(ps));
    bool heldOk = (heldNames.Count == 0 ? held.Contains("You hold no feature.") : held.Contains("You hold: " + JoinHand(heldNames) + ".")) && held.Contains("The muster draws from " + JoinHand(pool.Select(Roster.NameOf).ToList()))
        && (HeldHand(ps).HasFlag(Feature.Shrine) == held.Contains("Raise a banner"));
    Check($"war: standards, score and income ({tag}): the pips are the standards the game keeps ({c.Standards} of three), the income line is the held provinces' yields added by hand, the chips show the epoch, gold and score",
        s0.MaxStandards == 3 && p.FindAll("#dc-standards .pip").Count == 3 && p.FindAll("#dc-standards .pip.on").Count == c.Standards && p.FindAll(".dc-c-standards .pip.on").Count == c.Standards
        && NormText(p.Find("#dc-income")).Contains($"Your provinces pay {sum} gold at the end of an epoch.") && NormText(p.Find("#dc-income")).Contains($"Score {brief.Score}:") && brief.Score == c.FieldsWon + ps.Count(x => !x.IsCapital && x.Owner == Holder.Player)
        && Text(p, ".dc-c-epoch").StartsWith($"Epoch {c.Epoch + 1}") && Text(p, ".dc-c-gold") == $"Gold {c.Army.Read().Gold}" && Text(p, ".dc-c-score") == $"Score {c.Score}", $"{standing} / {sum}");
    Check($"war: an unlock ({tag}): the pool the muster draws from is the three base kinds plus the kinds of the features held NOW (written out by hand), the unlocks held are the held provinces' features, and the page says both",
        brief.OfferKinds.SequenceEqual(pool) && brief.HeldUnlocks == HeldHand(ps) && c.Unlocks == HeldHand(ps) && heldOk, $"offer {string.Join(",", brief.OfferKinds)} hand {string.Join(",", pool)}; '{held}'");
    Check($"war: nothing of the enemy's pick is on the map screen before the pick ({tag}): no picked lane, no sealed pick revealed", !Regex.IsMatch(NormText(p.Find(".dc-hud")), @"\b(enemy picked|picked the|enemy chose|will march)\b") && p.FindAll("[data-enemylane], [data-enemypick]").Count == 0);
}

async Task CheckCards(IRenderedComponent<DoctrinePage> p, string tag)
{
    var s0 = p.Instance.Session!; var c = s0.Campaign!;
    var ps = c.Provinces(); var brief = await c.BriefingAsync(); int epoch = c.Epoch;
    var held = HeldHand(ps); var pool = PoolHand(held);
    var problems = new List<string>();
    for (int l = 0; l < 3; l++)
    {
        var cards = p.FindAll($".dc-lanecard[data-lane='{l}']");
        if (cards.Count != 1) { problems.Add($"lane {l}: {cards.Count} cards"); continue; }
        var card = cards[0]; string txt = NormText(card); var lb = brief.Lanes[l]; var t = PlayerTargetHand(ps, l);
        var btn = p.FindAll($"#dc-lane-{l}");
        if (!txt.Contains(char.ToUpperInvariant(RoadsHand[l][0]) + RoadsHand[l][1..])) problems.Add($"lane {l}: road name");
        if (t is null) { if (lb.Target is not null || btn.Count != 1 || !btn[0].HasAttribute("disabled")) problems.Add($"lane {l}: nothing to assault but a push is offered"); continue; }
        var arena = s0.Grounds![t.ArenaIndex % s0.Grounds.Count];
        if (lb.Target?.Key != t.Key || card.GetAttribute("data-target") != t.Key.ToString()) problems.Add($"lane {l}: target {lb.Target?.Key} vs {t.Key}");
        string who = t.Owner == Holder.Enemy ? "held by the enemy" : "held by no one";
        if (!txt.Contains("You would assault " + ProvMidHand(t) + ", " + who + ".")) problems.Add($"lane {l}: province words");
        // the ground: the arena of the province, who besieges (the parity swap on the epoch, written out by hand), the clock
        var by = arena.SiegeClock > 0 ? (epoch % 2 == 0 ? Side.Player : Side.Enemy) : (Side?)null;
        string ground = arena.SiegeClock == 0 ? $"{arena.Name}: an open ground." : $"{arena.Name}: a siege, and {(by == Side.Player ? "you besiege" : "the enemy besieges")} it (the clock is {arena.SiegeClock} ticks).";
        if (lb.Arena?.Info.Id != arena.Id || lb.Arena?.Besieger != by || !txt.Contains(ground)) problems.Add($"lane {l}: ground '{ground}' vs briefing {lb.Arena?.Info.Id} {lb.Arena?.Besieger}");
        // the enemy host: soldiers and one line to a rank, from the cohorts the briefing reports
        var en = lb.Enemy!;
        int soldiers = en.Cohorts.Sum(x => x.Count);
        var lines = en.Cohorts.GroupBy(x => x.Slot).OrderBy(g => g.Key).Select(g => $"{new[] { "Front", "Middle", "Back" }[g.Key]} line: " + string.Join(", ", g.Select(x => $"{x.Count} {Roster.NameOf(x.Key)}"))).ToList();
        if (en.Soldiers != soldiers || !txt.Contains($"The enemy host there: {soldiers} {(soldiers == 1 ? "soldier" : "soldiers")}.") || !card.QuerySelectorAll(".dc-el li").Select(NormText).SequenceEqual(lines)) problems.Add($"lane {l}: enemy host [{string.Join(" / ", lines)}] vs [{string.Join(" / ", card.QuerySelectorAll(".dc-el li").Select(NormText))}]");
        // what holding it opens
        var fresh = KindIdsHand(t.Feature).Where(k => !pool.Contains(k)).ToList();
        string opens = t.IsCapital ? "" : t.Feature == Feature.Granary ? $"Holding it opens no new soldiers; it pays {t.Yield} gold." : t.Feature == Feature.None ? $"Holding it opens nothing; it pays {t.Yield} gold."
            : fresh.Count == 0 && !(t.Feature == Feature.Shrine && !held.HasFlag(Feature.Shrine)) ? $"Holding it opens nothing new; it pays {t.Yield} gold."
            : "Holding it opens " + string.Join(" and ", new[] { fresh.Count > 0 ? JoinHand(fresh.Select(Roster.NameOf).ToList()) + " to the muster" : "", t.Feature == Feature.Shrine && !held.HasFlag(Feature.Shrine) ? "the order Raise a banner to the council" : "" }.Where(x => x.Length > 0)) + $" while you hold it, and pays {t.Yield} gold.";
        if (!lb.NewKinds.SequenceEqual(fresh) || lb.Unlocks != (t.IsCapital ? Feature.None : t.Feature) && !t.IsCapital || lb.EndsWar != t.IsCapital || (opens.Length > 0 && !txt.Contains(opens)) || (opens.Length == 0 && card.QuerySelectorAll(".dc-lu").Length != 0)) problems.Add($"lane {l}: unlocks '{opens}'");
        if (t.IsCapital != txt.Contains("Taking it ends the war.") || card.GetAttribute("data-endswar") != (t.IsCapital ? "true" : "false")) problems.Add($"lane {l}: ends-the-war mark");
        if (btn.Count != 1 || btn[0].HasAttribute("disabled") || !NormText(btn[0]).StartsWith("Push the ")) problems.Add($"lane {l}: push button");
    }
    Check($"war: the lane cards equal the briefing ({tag}): for each road the province a push would assault, who holds it, its arena (with who besieges and the clock, the parity swap written out by hand), the enemy host by rank, what holding it opens (against the pool written out by hand) and the ends-the-war mark",
        problems.Count == 0, string.Join(" | ", problems.Take(3)));
    var hold = p.FindAll("#dc-holdback"); string holdText = NormText(p.Find(".dc-holdcard"));
    Check($"war: holding back ({tag}): the card says it costs ground and never a standard, forces no battle, and does not mention a standard being lost", hold.Count == 1 && NormText(hold[0]) == "Hold back" && holdText.Contains("fight no battle") && holdText.Contains("costs ground, never a standard") && !holdText.Contains("and a standard") && !Regex.IsMatch(holdText, @"costs? (you )?a standard"), holdText);
    Check($"war: the sealed pick is explained on the map screen ({tag}); the map comes first (the Roads sheet is shut on entry) and the dock offers Pick a road", NormText(p.Find(".dc-lanepanel")).Contains("Your pick is sealed. The enemy picks its lane without seeing yours") && !p.Find("#dc-lanes").ClassList.Contains("open") && p.Find("#dc-open-lanes").GetAttribute("aria-expanded") == "false" && Text(p, "#dc-roads-btn") == "Pick a road" && p.Find("#dc-roads-btn").GetAttribute("aria-controls") == "dc-lanes");
    Scan(p);
}

// what an epoch did, against the hand-written rules of the war
async Task CheckEpoch(IRenderedComponent<DoctrinePage> p, IReadOnlyList<Province> before, Feature heldBefore, int standardsBefore, LanePick pick, string tag)
{
    var s0 = p.Instance.Session!; var r = s0.LastEpoch!; var c = s0.Campaign!; var ps = c.Provinces();
    var problems = new List<string>();
    int enemyLane = LaneOf(s0.Seed, r.Epoch);
    var kind = pick.Concede ? EpochKind.Conceded : enemyLane == pick.Lane ? EpochKind.Battle : EpochKind.Race;
    if (r.Kind != kind || r.EnemyLane != enemyLane || r.PlayerLane != pick.Lane || r.Conceded != pick.Concede || r.StandardsBefore != standardsBefore) problems.Add($"kind {r.Kind} vs {kind}, enemy lane {r.EnemyLane} vs {enemyLane}, standards before {r.StandardsBefore} vs {standardsBefore}");
    var pt = pick.Concede ? null : PlayerTargetHand(before, pick.Lane); var et = EnemyTargetHand(before, enemyLane);
    var want = new List<(int Key, Holder To)>();
    if (kind == EpochKind.Battle)
    {
        var o = r.Battle!.Result.Outcome;
        if (o == BattleStatus.AttackerWon) want.Add((pt!.Key, Holder.Player)); else if (o == BattleStatus.DefenderWon && et is not null) want.Add((et.Key, Holder.Enemy));
    }
    else { if (pt is not null) want.Add((pt.Key, Holder.Player)); if (et is not null) want.Add((et.Key, Holder.Enemy)); }
    if (!r.Changes.Select(x => (x.Key, x.To)).OrderBy(x => x.Key).SequenceEqual(want.OrderBy(x => x.Key))) problems.Add($"changes [{string.Join(",", r.Changes.Select(x => x.Key + ">" + x.To))}] vs [{string.Join(",", want.Select(x => x.Key + ">" + x.To))}]");
    int lostStd = kind == EpochKind.Battle && r.Battle!.Result.Outcome == BattleStatus.DefenderWon ? 1 : 0;
    if (r.StandardsAfter != standardsBefore - lostStd || r.Restored) problems.Add($"standards {r.StandardsBefore} -> {r.StandardsAfter} (lost {lostStd})");
    if (kind != EpochKind.Battle && r.StandardsAfter != r.StandardsBefore) problems.Add("a race or a hold-back changed the standards");
    string all = NormText(p.Find("#dc-epoch")); var lis = p.FindAll("#dc-epoch .dc-epochlines li").Select(NormText).ToList();
    if (!NormText(p.Find("#dc-epoch h2")).StartsWith($"Epoch {r.Epoch + 1}: ")) problems.Add("heading");
    if (kind == EpochKind.Battle && !NormText(p.Find("#dc-epoch h2")).Contains($"a battle on the {RoadsHand[pick.Lane]}, {r.Battle!.Result.Outcome switch { BattleStatus.AttackerWon => "won", BattleStatus.DefenderWon => "lost", _ => "a draw" }}")) problems.Add("battle heading");
    if (kind == EpochKind.Race && !(NormText(p.Find("#dc-epoch h2")).EndsWith("a race") && all.Contains($"You pushed the {RoadsHand[pick.Lane]}; the enemy picked the {RoadsHand[enemyLane]}.") && all.Contains("each took ground unopposed"))) problems.Add("race words");
    if (kind == EpochKind.Conceded && !(NormText(p.Find("#dc-epoch h2")).EndsWith("you held back") && all.Contains($"You held back; the enemy picked the {RoadsHand[enemyLane]} and marched there unopposed."))) problems.Add("hold-back words");
    if (kind == EpochKind.Battle && !all.Contains($"You pushed the {RoadsHand[pick.Lane]} and the enemy picked it too, so the two hosts met there.")) problems.Add("battle words");
    foreach (var (key, to) in want)
    {
        var after = ps.First(x => x.Key == key); var was = before.First(x => x.Key == key);
        string line = to == Holder.Player ? $"You took {ProvMidHand(after)}." : was.Owner == Holder.Player ? $"The enemy took {ProvMidHand(after)}, which was yours." : $"The enemy took {ProvMidHand(after)}.";
        if (!lis.Contains(line)) problems.Add($"line missing: {line}");
    }
    if (want.Count == 0 && !lis.Contains("No ground changed hands.")) problems.Add("no-change line");
    string std = lostStd == 1 ? $"You lost a standard: {r.StandardsAfter} left." : $"No standard was lost: {r.StandardsAfter} fly.";
    if (!lis.Contains(std)) problems.Add("standards line: " + std);
    int income = ps.Where(x => x.Owner == Holder.Player).Sum(x => x.Yield);
    if (r.Outcome == CampaignOutcome.Running && (r.GoldPaid != income || !lis.Contains($"Your provinces paid {income} gold."))) problems.Add($"gold paid {r.GoldPaid} vs income {income}");
    var heldAfter = HeldHand(ps); var kb = PoolHand(heldBefore); var ka = PoolHand(heldAfter);
    var gained = ka.Except(kb).ToList(); var lost = kb.Except(ka).ToList();
    bool bannerUp = heldAfter.HasFlag(Feature.Shrine) && !heldBefore.HasFlag(Feature.Shrine), bannerDown = heldBefore.HasFlag(Feature.Shrine) && !heldAfter.HasFlag(Feature.Shrine);
    bool sawGain = lis.Any(x => x.StartsWith("From the next muster you may be offered ")), sawLoss = lis.Any(x => x.StartsWith("No longer offered, with the ground that gave them: "));
    if (sawGain != (gained.Count > 0 || bannerUp) || sawLoss != (lost.Count > 0 || bannerDown)) problems.Add($"unlock lines gain {sawGain}/{gained.Count} loss {sawLoss}/{lost.Count}");
    if (gained.Count > 0 && !all.Contains("From the next muster you may be offered " + JoinHand(gained.Select(Roster.NameOf).ToList()))) problems.Add("gained kinds not named");
    if (lost.Count > 0 && !all.Contains("No longer offered, with the ground that gave them: " + JoinHand(lost.Select(Roster.NameOf).ToList()))) problems.Add("lost kinds not named");
    int score = c.FieldsWon + ps.Count(x => !x.IsCapital && x.Owner == Holder.Player);
    if (r.Score != score || !lis.Contains($"Score: {score}.")) problems.Add($"score {r.Score} vs {score}");
    if (p.FindAll(".dc-c-standards .pip.on").Count != c.Standards) problems.Add("pips in the top bar");
    Check($"war: the summary of epoch {r.Epoch + 1} ({tag}) is what the rules say: the kind (battle, race or hold-back from the two picks), the revealed enemy lane, who took which province (the lane rule written out by hand), the standards (a loss costs one, a race or a hold-back none), the gold paid (the held provinces' yields added by hand), what opened and closed (the pool by hand) and the score",
        problems.Count == 0, string.Join(" | ", problems.Take(4)));
    Scan(p);
}

// the playable steps
async Task WaitMap(IRenderedComponent<DoctrinePage> p, string what)
    => await Until(() => p.Instance.Session!.Stage == DoctrineStage.Map && Has(p, ".dc-overmap") && Has(p, ".dc-lanecard"), 40000, what);
async Task<bool> PickAndSummarise(IRenderedComponent<DoctrinePage> p, int lane, bool hold, string tag)
{
    var s0 = p.Instance.Session!; var c = s0.Campaign!;
    var before = c.Provinces(); var heldBefore = HeldHand(before); int stdBefore = c.Standards;
    await Tap(p, hold ? "#dc-holdback" : $"#dc-lane-{lane}");
    bool ok = await Until(() => s0.Stage == DoctrineStage.Epoch && Has(p, "#dc-epoch"), 40000, "the epoch summary of " + tag);
    if (ok) await CheckEpoch(p, before, heldBefore, stdBefore, new LanePick(lane, hold), tag);
    return ok;
}
async Task ContinueToMap(IRenderedComponent<DoctrinePage> p)
{
    var s0 = p.Instance.Session!;
    Check("war: the summary's button goes on (back to the map, or to the end of the war when it is over)", Has(p, "#dc-epoch-next") && Text(p, "#dc-epoch-next") == (s0.Campaign!.Over ? "See the end of the war" : "Back to the map"));
    await Tap(p, "#dc-epoch-next");
    if (!s0.Campaign!.Over) await WaitMap(p, "the map after the summary"); else await Until(() => s0.Stage == DoctrineStage.Over && Has(p, ".dc-warend"), 20000, "the end of the war");
}
var warScript = new ScriptHands([ScriptHands.How.Manual, ScriptHands.How.Ignore]);
async Task<bool> PlayBattle(IRenderedComponent<DoctrinePage> p, int lane, string tag)
{
    var s0 = p.Instance.Session!; var c = s0.Campaign!;
    var before = c.Provinces(); var heldBefore = HeldHand(before); int stdBefore = c.Standards; var brief = s0.Briefing!; var lb = brief.Lanes[lane]; int epoch = brief.Epoch;
    var hand = PlayerTargetHand(before, lane)!; var armsHand = s0.Grounds![hand.ArenaIndex % s0.Grounds.Count];
    await Tap(p, $"#dc-lane-{lane}");
    if (!await Until(() => Has(p, ".dc-draft"), 40000, "the draft of the battle " + tag)) return false;
    // the battle is the card: the ground the top bar names, the session's ground, the pool the muster draws from
    bool groundOk = s0.Ground?.Info.Id == armsHand.Id && s0.Ground?.Besieger == lb.Arena?.Besieger && Has(p, ".dc-groundline") && Text(p, ".dc-groundline").Contains(armsHand.Name);
    bool poolOk = s0.Draft!.Offer.Packs.Count > 0 && s0.Draft.Offer.Packs.All(pk => brief.OfferKinds.Contains(pk.Kind)) && s0.Draft.Offer.Packs.All(pk => PoolHand(HeldHand(before)).Contains(pk.Kind));
    Check($"war: a battle epoch ({tag}) is fought on the card's ground, and the muster offers only kinds of the pool the card's briefing named (the pool written out by hand)", groundOk && poolOk, $"ground {s0.Ground?.Info.Id} vs {armsHand.Id}; packs {string.Join(",", s0.Draft.Offer.Packs.Select(x => x.Kind))} pool {string.Join(",", brief.OfferKinds)}");
    Check($"war: in a battle epoch ({tag}) the page keeps the whole round: the muster note, the Epoch chip, no slot count below the war's four, the ladder's news card never appears", Text(p, ".dc-musternote") == DoctrineWords.MusterNote && Text(p, ".dc-c-epoch").StartsWith($"Epoch {epoch + 1}") && !Has(p, ".dc-unlock") && p.FindAll(".dc-slots li").Count == 4);
    Scan(p);
    foreach (int i in ScriptHands.PackIndices(s0.Draft.Offer, s0.Draft.Army.Gold)) await TapPack(p, i);
    await Tap(p, "#dc-confirm");
    await PassAsk(p, 40000);
    await Until(() => Has(p, ".dc-editor"), 30000, "the doctrine of the battle " + tag);
    var st = warScript.Decide(epoch, s0.Army!, s0.Ghost!, s0.OrdersOffered!);
    Check($"war: the council of a battle epoch ({tag}) has orders on the table and the Raise a banner order only when a Shrine is held (the pool's order rule by hand)", s0.OrdersOffered!.Tokens.Count > 0
        && s0.OrdersOffered.Tokens.Any(t => DoctrineVocabulary.Decode(t) is { Family: OrderFamily.Army } o && (ArmyOrderKind)o.Kind == ArmyOrderKind.Aura) is var aura && (!aura || HeldHand(before).HasFlag(Feature.Shrine)));
    Scan(p);
    switch (st.How)
    {
        case ScriptHands.How.Accept: await Tap(p, "#dc-accept"); break;
        case ScriptHands.How.Ignore: await Tap(p, "#dc-ignore"); foreach (var t in st.Tokens) await TapChip(p, t); await Tap(p, "#dc-fight"); break;
        default: foreach (var t in st.Tokens) await TapChip(p, t); await Tap(p, "#dc-fight"); break;
    }
    if (!await Until(() => Has(p, ".dc-result"), 90000, "the result of the battle " + tag)) return false;
    var rec = s0.Records[^1];
    Check($"war: the battle's record is the card's ({tag}): the arena, who besieged, and the enemy's soldiers at the first frame are the briefing's", rec.ArenaId == armsHand.Id && rec.Replay.Besieger == lb.Arena?.Besieger && s0.Last!.EnemySoldiers == lb.Enemy!.Soldiers && Text(p, ".dc-result h2").StartsWith($"Epoch {rec.Round + 1}:") && rec.Round == epoch && Text(p, "#dc-next") == "Continue",
        $"enemy {s0.Last!.EnemySoldiers} vs {lb.Enemy!.Soldiers}; besieger {rec.Replay.Besieger} vs {lb.Arena?.Besieger}");
    Scan(p);
    await Tap(p, "#dc-next");
    bool ok = await Until(() => s0.Stage == DoctrineStage.Epoch && Has(p, "#dc-epoch"), 40000, "the epoch summary of " + tag);
    if (ok) await CheckEpoch(p, before, heldBefore, stdBefore, new LanePick(lane, false), tag);
    return ok;
}

// ---------------------------------------------------------------- scenario 1: a mixed war (a race, a battle, a hold-back that loses ground, a battle)
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    await wp.InvokeAsync(() => wp.Find("#dc-seed").Input(warSeed.ToString()));
    await Tap(wp, "#dc-campaign");
    bool up = await Until(() => WS.Stage == DoctrineStage.Map && Has(wp, ".dc-lanecard"), 60000, "the map of the war");
    Check("war: the campaign begins on the map with three road cards and the hold-back card, in the HUD", up && WS.IsCampaign && wp.FindAll(".dc-lanecard").Count == 4 && Has(wp, ".dc-hud") && wp.Find(".dc-hud").GetAttribute("data-stage") == "map" && !WS.Endless && Text(wp, ".dc-c-epoch") == $"Epoch 1 of {DoctrineSession.WarEpochs}");
    Check("war: the game seed is the field's, so the same seed draws the same map", WS.Seed == warSeed && WS.Campaign!.Seed == warSeed);
    await CheckMap(wp, "epoch 1 start"); await CheckCards(wp, "epoch 1");
    await Tap(wp, "#dc-roads-btn");
    bool opened = wp.Find("#dc-lanes").ClassList.Contains("open") && wp.Find("#dc-roads-btn").GetAttribute("aria-expanded") == "true" && wp.Find("#dc-open-lanes").GetAttribute("aria-expanded") == "true";
    Shot("war-map-sheet", wp);
    await Tap(wp, "#dc-roads-btn");
    Check("war: the dock button opens the road cards as a sheet and shuts them again", opened && !wp.Find("#dc-lanes").ClassList.Contains("open"));
    Shot("war-map", wp);
    // epoch 0: a race up the lane the enemy picks at epoch 2
    int xl = LaneOf(warSeed, 2);
    var p0 = WS.Campaign!.Provinces();
    Check("war: the first push is a race (the enemy's lane at epoch 1 differs), planned from the schedule", LaneOf(warSeed, 0) != xl);
    if (await PickAndSummarise(wp, xl, false, "race"))
    {
        Shot("war-epoch", wp);
        var mid = WS.Campaign!.Provinces().First(q => q.Lane == xl && q.Index == 1);
        Check("war: a race gave the player the middle province of the road he pushed (a feature that opens soldiers), and the held unlocks say so", mid.Owner == Holder.Player && mid.Feature is Feature.Barracks or Feature.Stables or Feature.Shrine && WS.Campaign.Unlocks.HasFlag(mid.Feature));
        await ContinueToMap(wp);
        await CheckMap(wp, "after the race"); await CheckCards(wp, "epoch 2");
        var poolNow = WS.Briefing!.OfferKinds;
        Check("war: an unlock appears in the offer after a hold: the pool of the next epoch has the kinds the held province opens", KindIdsHand(mid.Feature).All(poolNow.Contains) && poolNow.Count > 3);
        // epoch 1: a battle on the enemy's lane, which differs from the one just held
        int b = LaneOf(warSeed, 1);
        if (await PlayBattle(wp, b, "epoch 2"))
        {
            await ContinueToMap(wp);
            await CheckMap(wp, "after the battle"); await CheckCards(wp, "epoch 3");
            int stdMid = WS.Campaign!.Standards;
            // epoch 2: the enemy marches on the lane whose middle province we hold; we hold back (no battle): ground is lost, no standard
            var held2 = WS.Campaign.Provinces().First(q => q.Lane == xl && q.Index == 1);
            Check("war: before the hold-back the player still holds the middle province of that road", held2.Owner == Holder.Player);
            bool lostTheOne = false;
            if (await PickAndSummarise(wp, 0, true, "hold back"))
            {
                var r = WS.LastEpoch!;
                var after = WS.Campaign.Provinces().First(q => q.Lane == xl && q.Index == 1);
                lostTheOne = after.Owner == Holder.Enemy;
                Check("war: holding back cost ground and no standard: the enemy took the province it marched on (the middle province we held) and the standards are what they were", r.Kind == EpochKind.Conceded && r.EnemyLane == xl && lostTheOne && r.StandardsBefore == stdMid && r.StandardsAfter == stdMid && WS.Campaign.Standards == stdMid);
                await ContinueToMap(wp);
                await CheckMap(wp, "after the hold-back"); await CheckCards(wp, "epoch 4");
                var poolAfter = WS.Briefing!.OfferKinds;
                var handPool = PoolHand(HeldHand(WS.Campaign.Provinces()));
                Check("war: a lost province's unlock leaves the offer: the pool of the next epoch is the base kinds plus the kinds of what is held now, and none of the kinds the lost province opened remain", poolAfter.SequenceEqual(handPool) && (HeldHand(WS.Campaign.Provinces()).HasFlag(mid.Feature) || KindIdsHand(mid.Feature).All(k => !poolAfter.Contains(k))));
                int d = LaneOf(warSeed, 3);
                if (await PlayBattle(wp, d, "epoch 4"))
                {
                    Shot("war-epoch-battle", wp);
                    await ContinueToMap(wp);
                    await CheckMap(wp, "after the second battle"); await CheckCards(wp, "epoch 5");
                }
            }
        }
    }
    // leaving the war from the map: the end screen is the visitor's
    if (WS.Stage == DoctrineStage.Map)
    {
        await Tap(wp, "#dc-leave");
        await Until(() => WS.Stage == DoctrineStage.Over && Has(wp, ".dc-warend"), 20000, "left the war");
        var e = WS.End!;
        Check("war: the end screen of a war the visitor left says so, reports the score as fields won plus provinces held (hand count), lists every epoch played and offers the way back",
            e.Left && e.Outcome == CampaignOutcome.Running && Text(wp, "#dc-endhead") == "You left the war." && e.Score == e.FieldsWon + e.ProvincesHeld && e.ProvincesHeld == WS.Campaign!.Provinces().Count(q => !q.IsCapital && q.Owner == Holder.Player)
            && Text(wp, "#dc-score").StartsWith($"Score {e.Score}: {e.FieldsWon} ") && wp.FindAll(".dc-warend .dc-rounds-list li").Count == e.Epochs && e.Epochs == WS.Epochs.Count && Has(wp, "#dc-fresh") && !Has(wp, "#dc-again"));
        Scan(wp); Shot("war-end", wp);
        await Tap(wp, "#dc-fresh");
        Check("war: from the end of a war the way back is the start screen, where the fixed games are as they were", WS.Stage == DoctrineStage.Start && !WS.IsCampaign && wp.FindAll(".dc-opt").Count == 3);
    }
    livePages.Remove(wp);
}

// ---------------------------------------------------------------- scenario 2: three races up one road take the enemy capital (WarWon)
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    await wp.InvokeAsync(() => wp.Find("#dc-seed").Input(sweepSeed.ToString()));
    await Tap(wp, "#dc-campaign");
    await WaitMap(wp, "the map of the sweep");
    for (int e = 0; e < 3 && WS.Stage == DoctrineStage.Map; e++)
    {
        await CheckMap(wp, "sweep " + e); await CheckCards(wp, "sweep " + e);
        if (e == 2)
        {
            var card = WS.Briefing!.Lanes[sweepLane];
            Check("war: the last card of the sweep names the enemy capital and says taking it ends the war", card.EndsWar && card.Target is { IsCapital: true } && NormText(wp.Find($".dc-lanecard[data-lane='{sweepLane}']")).Contains("the enemy capital") && NormText(wp.Find($".dc-lanecard[data-lane='{sweepLane}']")).Contains("Taking it ends the war."));
            Shot("war-map-last", wp);
        }
        if (!await PickAndSummarise(wp, sweepLane, false, "sweep " + e)) break;
        if (e < 2) Check("war: a race changes no standard", WS.LastEpoch!.StandardsAfter == WS.LastEpoch.StandardsBefore && WS.Campaign!.Standards == WS.MaxStandards);
        await ContinueToMap(wp);
    }
    Check("war: three races up the road the enemy never picks take its capital: the war is won, and the end screen says so", WS.Stage == DoctrineStage.Over && WS.End?.Outcome == CampaignOutcome.WarWon && Text(wp, "#dc-endhead") == "The war is won: the enemy capital is yours." && WS.End.Epochs == 3 && !WS.End.Left);
    Check("war: the end screen lists the three races and the score is the provinces held (no field was fought)", wp.FindAll(".dc-warend .dc-rounds-list li").Count == 3 && wp.FindAll(".dc-warend .dc-rounds-list li").All(li => NormText(li).Contains(": a race, you on the ")) && WS.End!.FieldsWon == 0 && WS.End.Score == WS.End.ProvincesHeld);
    Scan(wp);
    livePages.Remove(wp);
}

// ---------------------------------------------------------------- scenario 3: the enemy marches up one road three times; holding back each time loses the capital but never a standard (WarLost)
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    await wp.InvokeAsync(() => wp.Find("#dc-seed").Input(tripleSeed.ToString()));
    await Tap(wp, "#dc-campaign");
    await WaitMap(wp, "the map of the retreat");
    for (int e = 0; e < 3 && WS.Stage == DoctrineStage.Map; e++)
    {
        await CheckMap(wp, "retreat " + e);
        if (!await PickAndSummarise(wp, 0, true, "retreat " + e)) break;
        Check($"war: concede costs no standard (hold-back {e + 1} of three): the pips are all lit and the epoch says no standard was lost", WS.Campaign!.Standards == WS.MaxStandards && p_pips(wp) == WS.MaxStandards);
        await ContinueToMap(wp);
    }
    Check("war: holding back three times while the enemy marches up one road loses the capital: the war is lost with every standard still flying", WS.Stage == DoctrineStage.Over && WS.End?.Outcome == CampaignOutcome.WarLost && Text(wp, "#dc-endhead") == "The war is lost: your capital has fallen." && WS.End.Standards == WS.MaxStandards);
    Scan(wp);
    livePages.Remove(wp);
    int p_pips(IRenderedComponent<DoctrinePage> q) => q.FindAll(".dc-c-standards .pip.on").Count;
}

// ---------------------------------------------------------------- scenario 4: both capitals fall in one epoch (WarDrawn)
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    int a = LaneOf(tripleSeed, 0), mine = (a + 1) % 3;
    await wp.InvokeAsync(() => wp.Find("#dc-seed").Input(tripleSeed.ToString()));
    await Tap(wp, "#dc-campaign");
    await WaitMap(wp, "the map of the draw");
    for (int e = 0; e < 3 && WS.Stage == DoctrineStage.Map; e++)
    {
        if (!await PickAndSummarise(wp, mine, false, "draw " + e)) break;
        await ContinueToMap(wp);
    }
    Check("war: both capitals taken in one epoch is a draw, said as such", WS.Stage == DoctrineStage.Over && WS.End?.Outcome == CampaignOutcome.WarDrawn && Text(wp, "#dc-endhead") == "A draw: both capitals fell in the same epoch.");
    livePages.Remove(wp);
}

// ---------------------------------------------------------------- scenario 5: the epochs run out (EpochsUp), set short through the session
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    WS.NewCampaign(quietSeed, false, out var whyNot, maxEpochs: 2);
    await WaitMap(wp, "the map of the short war");
    Check("war: a short war shows its epoch limit in the chip and goal", whyNot.Length == 0 && Text(wp, ".dc-c-epoch") == "Epoch 1 of 2" && Text(wp, ".dc-goal").Contains("within 2 epochs"));
    for (int e = 0; e < 2 && WS.Stage == DoctrineStage.Map; e++)
    {
        if (!await PickAndSummarise(wp, 0, true, "short " + e)) break;
        await ContinueToMap(wp);
    }
    Check("war: when the epochs run out the war is over and the end screen says so", WS.Stage == DoctrineStage.Over && WS.End?.Outcome == CampaignOutcome.EpochsUp && Text(wp, "#dc-endhead") == "The war is over: the epochs have run out." && WS.End.Epochs == 2);
    foreach (var o in Enum.GetValues<CampaignOutcome>())
    {
        string h = DoctrineCampaignWords.EndHeading(o, false);
        Check($"war: the end heading for {o} is its own sentence", h.Length > 0 && (o == CampaignOutcome.Running || (o switch { CampaignOutcome.WarWon => h.Contains("won"), CampaignOutcome.WarLost => h.Contains("capital has fallen"), CampaignOutcome.WarDrawn => h.StartsWith("A draw"), CampaignOutcome.OutOfStandards => h.Contains("no standards left"), _ => h.Contains("epochs have run out") })));
    }
    livePages.Remove(wp);
}

// ---------------------------------------------------------------- scenario 6: endless: a cleared road refills, there is no enemy capital, the score counts fields plus provinces
{
    using var wctx = NewCtx(false);
    var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
    var WS = wp.Instance.Session!;
    await Tap(wp, "#dc-mode-endless");
    await wp.InvokeAsync(() => wp.Find("#dc-seed").Input(endlessSeed.ToString()));
    await Tap(wp, "#dc-campaign");
    await WaitMap(wp, "the map of the endless run");
    Check("war: an endless run says so: no 'of' in the epoch chip, the goal is the score, and the map has no enemy capital", WS.Endless && Text(wp, ".dc-c-epoch") == "Epoch 1" && Text(wp, ".dc-goal").Contains("Score is fields won plus provinces held") && !Has(wp, "g.dc-prov[data-key='37']"));
    await CheckMap(wp, "endless start"); await CheckCards(wp, "endless start");
    for (int e = 0; e < 2 && WS.Stage == DoctrineStage.Map; e++)
    {
        if (!await PickAndSummarise(wp, endlessLane, false, "endless " + e)) break;
        await ContinueToMap(wp);
    }
    var ps = WS.Campaign!.Provinces();
    var line = ps.Where(q => !q.IsCapital && q.Lane == endlessLane).OrderBy(q => q.Index).ToList();
    Check("war: endless refill: a road the player has cleared grows a fresh enemy province beyond it, and the map draws it", line.Count == 4 && line[..3].All(q => q.Owner == Holder.Player) && line[3].Owner == Holder.Enemy && Has(wp, $"g.dc-prov[data-key='{line[3].Key}'][data-owner='enemy']"));
    await CheckMap(wp, "endless after the refill"); await CheckCards(wp, "endless after the refill");
    var next = WS.Briefing!.Lanes[endlessLane];
    Check("war: endless refill: the road's card now assaults the fresh province (the fourth from the capital), not a capital", next.Target?.Key == line[3].Key && !next.EndsWar && next.Target.Index == 3 && NormText(wp.Find($".dc-lanecard[data-lane='{endlessLane}']")).Contains("fourth from your capital"));
    Shot("war-map-endless", wp);
    await Tap(wp, "#dc-leave");
    await Until(() => WS.Stage == DoctrineStage.Over && Has(wp, ".dc-warend"), 20000, "left the endless run");
    var en = WS.End!;
    Check("war: the endless run's score is fields won plus provinces held, said so", en.Endless && en.Score == en.FieldsWon + en.ProvincesHeld && en.ProvincesHeld == WS.Campaign.Provinces().Count(q => !q.IsCapital && q.Owner == Holder.Player) && Text(wp, "#dc-score").Contains("(the score of an endless run)") && Text(wp, "#dc-score").StartsWith($"Score {en.Score}: "));
    Scan(wp);
    livePages.Remove(wp);
}

// ---------------------------------------------------------------- scenario 7: a battle the player wins takes the province and costs nothing
{
    int wonSeed = -1;
    for (int sd = 1; sd <= 60 && wonSeed < 0; sd++)
    {
        using var wctx = NewCtx(false);
        var wp = wctx.Render<DoctrinePage>(); livePages.Add(wp);
        var WS = wp.Instance.Session!;
        WS.NewCampaign(sd, false, out _);
        await WaitMap(wp, "the map of a battle hunt");
        int lane = LaneOf(sd, 0);
        var target = PlayerTargetHand(WS.Campaign!.Provinces(), lane)!;
        if (await PlayBattle(wp, lane, "hunt " + sd) && WS.LastEpoch!.Battle!.Result.Outcome == BattleStatus.AttackerWon)
        {
            wonSeed = sd;
            var r = WS.LastEpoch!; var ps = WS.Campaign.Provinces();
            Check("war: a won battle takes the province it was fought for, loses no standard, counts a field won, and the summary says it won", ps.First(q => q.Key == target.Key).Owner == Holder.Player && r.StandardsAfter == r.StandardsBefore && WS.Campaign.FieldsWon == 1
                && NormText(wp.Find("#dc-epoch h2")).EndsWith("won") && r.Score == 1 + ps.Count(q => !q.IsCapital && q.Owner == Holder.Player));
            await ContinueToMap(wp);
            await CheckMap(wp, "after a won battle"); await CheckCards(wp, "after a won battle");
        }
        livePages.Remove(wp);
    }
    Console.WriteLine($"  (a won battle was found at seed {wonSeed})");
    Check("war: a battle the scripted visitor wins was found and checked", wonSeed > 0);
}
if (warOnly)
{
    Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks, war only)" : $"{failures} FAILED of {checks} (war only)");
    return failures == 0 ? 0 : 1;
}

// start screen
Check("start: rounds options (three 44px buttons), seed field, new game, army code field and load button", page.FindAll(".dc-opt").Count == 3 && Has(page, "#dc-seed") && Has(page, "#dc-new") && Has(page, "#dc-code") && Has(page, "#dc-load"));
Check("start: the Doctrine stage is Start and no game runs", S.Stage == DoctrineStage.Start && !S.InGame);
Scan(page);
await page.InvokeAsync(() => page.Find("#dc-code").Input("DA2.not.a.code"));
await Tap(page, "#dc-load");
Check("start: a bad army code is refused in plain words and no game starts", Text(page, ".dc-loaderr").StartsWith("That is not an army code") && S.Stage == DoctrineStage.Start, Text(page, ".dc-loaderr"));
await page.InvokeAsync(() => page.Find("#dc-code").Input(new global::Doctrine.Army.PleromaArmyService().Create(Side.Enemy, 5).ToCode()));
await Tap(page, "#dc-load");
Check("start: an ENEMY army code is refused with its own sentence", Text(page, ".dc-loaderr").Contains("enemy army code"), Text(page, ".dc-loaderr"));
await page.InvokeAsync(() => page.Find("#dc-code").Input(""));
await page.InvokeAsync(() => page.Find("#dc-seed").Input("12x"));
await Tap(page, "#dc-new");
Check("start: a seed that is not digits is refused", Text(page, ".dc-loaderr").Contains("whole number") && S.Stage == DoctrineStage.Start);

Check("show everything: on a fresh page the control is offered and nothing is opened yet", Has(page, "#dc-showall") && page.Instance.Progress.Step == 0 && !page.Instance.Progress.ShowAll);
await Tap(page, "#dc-showall");
Check("show everything: one tap opens every order, slot and kind at once, and the control goes", page.Instance.Progress.ShowAll && page.Instance.Progress.Step == DoctrineUnlocks.LastStep && !Has(page, "#dc-showall"));
await Tap(page, ".dc-opt", 0);   // 3 rounds
Check("start: the 3-rounds option is the one pressed (radio, aria-checked)", page.FindAll(".dc-opt")[0].GetAttribute("aria-checked") == "true" && page.FindAll(".dc-opt")[1].GetAttribute("aria-checked") == "false");
await page.InvokeAsync(() => page.Find("#dc-seed").Input(gateSeed.ToString()));
var swGame = System.Diagnostics.Stopwatch.StartNew();
await Tap(page, "#dc-new");
Check("start: the game starts with that seed and 3 rounds", S.InGame && S.Seed == gateSeed && S.TotalRounds == 3);

// ---- round 0: the muster (the offered companies), a subset bought, the budget, the question about a reroll, the council, no ghost yet
await Until(() => Has(page, ".dc-draft"), 20000, "round 0 draft");
Check("draft: the top bar says Round 1 of 3 and the gold; the general panel counts its examples", Text(page, ".dc-status").Contains("Round 1 of 3") && Text(page, ".dc-status").Contains("Gold ") && Text(page, ".dc-examples").Contains("General: 0 examples seen"), Text(page, ".dc-status") + " | " + Text(page, ".dc-examples"));
ulong digestBeforeRound0 = S.Draft!.Army.Digest();
int gold0 = S.Draft!.Army.Gold;
var offer0 = S.Draft!.Offer;
string[] lineHandP = ["Front", "Middle", "Back"];
var poolRulesA = OfferRules.Default;       // the game's own defaults, NOT the page's rules (so a rule the page changes is seen): Show everything was pressed, so the page's pools let everything through
{
    var cardsP = page.FindAll(".dc-pack");
    Check("muster: the cards are exactly the session's offer, in its order: the kind, the count and the price the game drew (nothing added, nothing missing)",
        offer0.Active && offer0.Packs.Count > 0 && cardsP.Count == offer0.Packs.Count
        && cardsP.Select((c, i) => c.GetAttribute("data-kind") == offer0.Packs[i].Kind.ToString() && c.GetAttribute("data-count") == offer0.Packs[i].Count.ToString()
            && c.QuerySelector(".nm")!.TextContent.Trim() == $"{offer0.Packs[i].Count} {Roster.NameOf(offer0.Packs[i].Kind)}").All(x => x),
        string.Join(", ", offer0.Packs.Select(p => $"{p.Count} {Roster.NameOf(p.Kind)} for {p.Cost}")));
    Check("muster: the offer is the game's own for this seed, round and purse (the offer source asked again here): at most the pack count, each a distinct kind, priced count x price, none dearer than the purse",
        offer0.Packs.SequenceEqual(SeededOffers.Instance.Units(gateSeed, 0, gold0, poolRulesA).Packs) && offer0.Packs.Count <= OfferRules.Default.PackCount
        && offer0.Packs.Select(p => p.Kind).Distinct().Count() == offer0.Packs.Count && offer0.Packs.All(p => p.Cost == p.Count * Roster.PriceOf(p.Kind) && p.Cost <= gold0));
    Check("muster: each card says what the soldiers are and what the company costs ('Costs N gold.')",
        cardsP.Select((c, i) => c.QuerySelector(".fx")!.TextContent.Trim() == DoctrineWords.KindBlurbCap(offer0.Packs[i].Kind) && c.QuerySelector(".cost")!.TextContent.Trim() == $"Costs {offer0.Packs[i].Cost} gold.").All(x => x));
}
// buy a subset: the dearest companies first, as many as the purse pays
var byCost = Enumerable.Range(0, offer0.Packs.Count).OrderByDescending(i => offer0.Packs[i].Cost).ThenBy(i => i).ToList();
var bought = new List<int>(); int left0 = gold0;
foreach (int i in byCost) if (offer0.Packs[i].Cost <= left0) { await TapPack(page, i); bought.Add(i); left0 -= offer0.Packs[i].Cost; }
int total0 = gold0 - left0;
Check("muster: tapping buys a company whole: the cards bought are pressed, the others are not, and the total is the sum of the prices of those bought",
    bought.Count >= 1 && bought.Count < offer0.Packs.Count && Enumerable.Range(0, offer0.Packs.Count).All(i => (page.FindAll(".dc-pack")[i].GetAttribute("aria-pressed") == "true") == bought.Contains(i) && page.FindAll(".dc-pack")[i].ClassList.Contains("on") == bought.Contains(i))
    && Text(page, ".dc-sum") == $"This draft costs {total0} of {gold0} gold.", Text(page, ".dc-sum"));
var unaff = Enumerable.Range(0, offer0.Packs.Count).Where(i => !bought.Contains(i) && offer0.Packs[i].Cost > left0).ToList();
Check("muster: a company the purse can no longer pay for is greyed, disabled, and says 'Costs N gold; you have G.' (G is what is left)",
    unaff.Count > 0 && unaff.All(i => page.FindAll(".dc-pack")[i].HasAttribute("disabled") && page.FindAll(".dc-pack")[i].ClassList.Contains("off") && page.FindAll(".dc-pack")[i].QuerySelector(".cost")!.TextContent.Trim() == $"Costs {offer0.Packs[i].Cost} gold; you have {left0}."),
    string.Join(" | ", unaff.Select(i => page.FindAll(".dc-pack")[i].QuerySelector(".cost")!.TextContent.Trim())));
if (unaff.Count > 0) await TapPack(page, unaff[0]);              // bUnit fires a click on a disabled button; the page guards it
Check("muster: tapping a company that cannot be paid buys nothing (the total and the cards bought stay as they were)", Text(page, ".dc-sum") == $"This draft costs {total0} of {gold0} gold." && page.FindAll(".dc-pack.on").Count == bought.Count);
// the line each company would join is the army's own balance rule, asked here of an independent army of the same seed
{
    var svcI = new global::Doctrine.Army.PleromaArmyService();
    async Task<int> RankWith(IEnumerable<int> idx, int kind)
    {
        var a = svcI.Create(Side.Player, gateSeed);
        await a.DraftAsync(new DraftRequest(0, gateSeed, idx.OrderBy(i => i).Select(i => UnitOffer.Pick(offer0.Packs[i])).ToList()), CancellationToken.None);
        return a.Read().Cohorts.FirstOrDefault(c => c.Key == kind)?.Slot ?? -1;
    }
    await Until(() => Enumerable.Range(0, offer0.Packs.Count).Where(i => !unaff.Contains(i)).All(i => page.FindAll(".dc-pack")[i].QuerySelector(".rk, .why") is not null), 5000, "the lines the companies would join");
    var wrong = new List<string>(); int checkedRanks = 0;
    for (int i = 0; i < offer0.Packs.Count; i++)
    {
        if (unaff.Contains(i)) { if (page.FindAll(".dc-pack")[i].QuerySelector(".rk") is not null) wrong.Add($"pack {i} cannot be bought but names a line"); continue; }
        bool on = bought.Contains(i);
        int want = await RankWith(on ? bought : bought.Append(i), offer0.Packs[i].Kind);
        string? text = page.FindAll(".dc-pack")[i].QuerySelector(".rk")?.TextContent.Trim();
        if (want < 0) continue;
        checkedRanks++;
        if (text != $"{(on ? "Joins" : "Would join")} the {lineHandP[want].ToLowerInvariant()} line.") wrong.Add($"pack {i} ({Roster.NameOf(offer0.Packs[i].Kind)}, {(on ? "bought" : "not bought")}): page says '{text}', the army puts it in the {lineHandP[want]} line");
    }
    Check("muster: every company names the line it would join (or 'Joins' once bought), equal to what the army itself does when the same companies are drafted on an independent army", checkedRanks >= 2 && wrong.Count == 0, string.Join(" | ", wrong.Take(3)) + $" ({checkedRanks} checked)");
}
Shot("m1-muster-bought", page); Shot("m0-muster", page);
// taking a company back
await TapPack(page, bought[0]);
Check("muster: tapping a bought company takes it back, and the total drops by its price", Text(page, ".dc-sum") == $"This draft costs {total0 - offer0.Packs[bought[0]].Cost} of {gold0} gold." && page.FindAll(".dc-pack")[bought[0]].GetAttribute("aria-pressed") == "false");
Check("draft: the real army was untouched by the preview", S.Draft!.Army.Digest() == digestBeforeRound0 && S.Draft.Army.Gold == gold0);
while (page.FindAll(".dc-pack.on").Count > 0) await page.InvokeAsync(() => page.FindAll(".dc-pack.on")[0].Click());
Check("muster: a draft of nothing costs nothing and the button then says the army is kept as it is", Text(page, ".dc-sum") == $"This draft costs 0 of {gold0} gold." && Text(page, "#dc-confirm") == "Keep the army as it is");
await DoDraft(page, 0, pass: false);
// the question about a reroll
{
    bool asked = await Until(() => S.Reroll is not null && Has(page, "#dc-keep"), 20000, "the question about a reroll");
    var ask0 = S.Reroll;
    Check("ask: after the draft the game asks whether to pay for another council: the dock offers 'Reroll for N gold (K left)' and 'Keep these', N and K the offer's own, and the general has not read the council yet",
        asked && ask0 is not null && Text(page, "#dc-reroll") == $"Reroll for {ask0.Offer.RerollCost} gold ({ask0.Offer.RerollsLeft} left)" && ask0.Offer.RerollCost == OfferRules.Default.RerollCost && ask0.Offer.RerollsLeft == OfferRules.Default.MaxRerolls
        && S.Ghost is null && Has(page, ".dc-asking") && !Has(page, ".dc-editor") && Text(page, ".dc-rerollnote").Contains($"{ask0.Offer.RerollCost} gold"), asked ? Text(page, ".dc-rerollnote") : "no question");
    Check("ask: the cards wait for the answer (every card is disabled while the question stands, and the dock shows no slots, only the question's buttons)", page.FindAll(".dc-chip").Count > 0 && page.FindAll(".dc-chip").All(c => c.HasAttribute("disabled")) && page.FindAll(".dc-dock .dc-slot").Count == 0 && page.FindAll(".dc-dock .dc-dockbtns .dc-btn").Count == 3);
    Scan(page);
    Shot("r0-ask", page);
    await Tap(page, "#dc-keep");
}
await Until(() => Has(page, ".dc-editor"), 20000, "round 0 doctrine screen");
Check("doctrine: round 0, the general has seen nothing: no ghost, a plain sentence instead, confidence 0", !Has(page, ".dc-ghost") && Has(page, ".dc-noghost") && S.Ghost!.ConfidencePermille == 0 && S.Ghost.Doctrine.IsEmpty && S.Sections.General.ExamplesSeen == 0, Has(page, ".dc-noghost") ? Text(page, ".dc-noghost") : "no sentence");
Scan(page);
Shot("r0-council", page);

// the council: the cards are exactly the offer, and only an offered, legal order can be put in a slot
{
    var legal = S.Army!.Legal;
    var offerO = S.OrdersOffered!;
    var cards = page.FindAll(".dc-chip");
    var tokens = cards.Select(c => int.Parse(c.GetAttribute("data-token")!)).ToList();
    int legalCount = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Count(legal.Allows);
    Check("council: the cards are exactly the offer the game drew: its tokens, sorted by id, none twice, End never, as many as the offer size allows",
        offerO.Active && tokens.SequenceEqual(offerO.Tokens) && tokens.SequenceEqual(tokens.OrderBy(t => t)) && tokens.Distinct().Count() == tokens.Count && !tokens.Contains(0) && tokens.Count == Math.Min(poolRulesA.OrderOfferSize, legalCount),
        $"{tokens.Count} cards, offer {offerO.Tokens.Count}, {legalCount} legal");
    Check("council: the offer is the game's own for this seed, round, reroll and legal list (the offer source asked again here)", offerO.Tokens.SequenceEqual(SeededOffers.Instance.Orders(gateSeed, 0, 0, legal, poolRulesA).Tokens) && offerO.Rerolls == 0);
    int FieldN(IEnumerable<int> ts) => ts.Count(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field);
    int LinesN(IEnumerable<int> ts) => ts.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).Select(t => DoctrineVocabulary.Decode(t).A).Distinct().Count();
    var legalTokens = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows).ToList();
    Check("council: the game's guarantees show on the table: at least three orders for a line touching at least two lines, and at least one order for the whole host (as far as the legal list allows)",
        FieldN(tokens) >= Math.Min(poolRulesA.MinFieldOrders, FieldN(legalTokens)) && LinesN(tokens) >= Math.Min(poolRulesA.MinFieldLines, LinesN(legalTokens))
        && tokens.Count(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army) >= Math.Min(poolRulesA.MinArmyOrders, legalTokens.Count(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army)));
    Check("council: every card is an order the army allows now, and is enabled", cards.All(c => !c.HasAttribute("disabled") && legal.Allows(int.Parse(c.GetAttribute("data-token")!))));
    var cardsOf = page.FindAll(".dc-chip");
    Check("council (cards): every card has its label and its one-line effect; a card for Reform the ranks shows what the front line becomes; a card with a cost says it",
        cardsOf.All(c => c.QuerySelector(".lb") is not null && c.QuerySelector(".lb")!.TextContent.Trim() == DoctrineWords.Label(int.Parse(c.GetAttribute("data-token")!)) && c.QuerySelector(".fx")!.TextContent.Trim() == DoctrineWords.Effect(int.Parse(c.GetAttribute("data-token")!)))
        && cardsOf.Where(c => c.QuerySelector(".lb")!.TextContent.Trim().StartsWith("Reform the ranks")).All(c => c.QuerySelector(".pv")!.TextContent.StartsWith("Your front line "))
        && cardsOf.Where(c => c.QuerySelector(".lb")!.TextContent.Trim().StartsWith("Reforge:")).All(c => (c.QuerySelector(".cost") is null) == (S.Army!.Cohorts.All(k => Roster.RelabelCost(k.Traits, Array.IndexOf(DoctrineWords.Traits, c.QuerySelector(".lb")!.TextContent.Trim()[9..]), true) == 0))));
    int outside = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => legal.Allows(t) && !offerO.Tokens.Contains(t));
    bool noCard = !Has(page, $"[data-token='{outside}']");
    await page.InvokeAsync(() => page.Instance.PlaceOrder(outside)); page.Render();
    Check("council: a legal order that is NOT on the table has no card, and asking the page to place it puts nothing in a slot (it cannot be chosen)", noCard && page.FindAll(".dc-slot.empty").Count == 4, $"token {outside} ({DoctrineVocabulary.Name(outside)})");
    int illegal = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => !legal.Allows(t));
    await page.InvokeAsync(() => page.Instance.PlaceOrder(illegal)); page.Render();
    Check("council: an order the army says is illegal cannot be placed either", page.FindAll(".dc-slot.empty").Count == 4);
    await TapChip(page, tokens[0]);
    Check("council: tapping a card puts its order in the first empty slot, the card is marked with its slot, and its sentence is listed", page.FindAll(".dc-slot.empty").Count == 3 && Has(page, ".dc-says li") && page.Find($"[data-token='{tokens[0]}']").ClassList.Contains("on") && page.Find($"[data-token='{tokens[0]}'] .mk").TextContent.Trim() == "In slot 1");
    await Tap(page, ".dc-slot", 0);
    Check("council: tapping a filled slot clears it, and the card is free again", page.FindAll(".dc-slot.empty").Count == 4 && !page.Find($"[data-token='{tokens[0]}']").ClassList.Contains("on"));
    await TapChip(page, tokens[0]); await TapChip(page, tokens[0]);
    Check("council: tapping a placed card again takes it out (each order once)", page.FindAll(".dc-slot.empty").Count == 4);
}
await DoChoose(page, script, 0);
Check("battle: while the round works the page says so, with a Cancel button (44px)", await Until(() => Has(page, "#dc-working") || Has(page, ".dc-result"), 20000, "working or result"));
Scan(page);

// ---- round 0 result: the replay, the scrubber, the falling gate
Check("watch: the closed round arrives through RoundClosedAsync and the page parks on its result", await Until(() => Has(page, ".dc-result") || S.Stage == DoctrineStage.Over, 60000, "round 0 result") && Has(page, ".dc-result") && S.Stage == DoctrineStage.Watch);
roundTimes.Add(swGame.Elapsed.TotalMilliseconds);
GroundAfter(page, 0); LimitAfter(page, 0);
{
    var rec0 = S.Last!.Record;
    Scan(page);
    Check("watch: the first battle is on the falling-gate arena (the seed was chosen for it)", rec0.ArenaId == "falling-gate" && Text(page, ".dc-result h2").Contains("The Falling Gate"), rec0.ArenaId);
    Check("watch: the arena is drawn from the layout: an SVG with wall, cover and gate blocks", Has(page, "svg.dc-arena") && Has(page, ".dc-ground.wall") && Has(page, ".dc-ground.cover") && Has(page, ".dc-ground.gate"));
    int frame0Blobs = rec0.Replay.Frames[0].Blobs.Count;
    Check("watch: one blob per cohort of both sides at tick 0", page.FindAll("g.dc-blob").Count == frame0Blobs && Text(page, ".dc-tick") == "0", $"{frame0Blobs} blobs");
    Check("watch: the tick counter and the strengths of both sides are shown", Text(page, ".dc-replay .dc-chips").Contains($"of {rec0.Replay.Frames.Count - 1}") && Text(page, ".dc-replay .dc-chips li.mine").Contains($"Your soldiers {rec0.Replay.Frames[0].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength)}"));
    Check("watch: controls are play, pause, step, restart, speed and a scrubber (all 44px)", page.FindAll(".dc-controls .dc-btn").Count == 5 && Has(page, "input.dc-range[type=range]"));
    Check("watch: with autoplay off the recording waits at tick 0 for the visitor", Text(page, ".dc-tick") == "0" && Text(page, ".dc-play") == "Play");
    int lastTick = rec0.Replay.Frames.Count - 1;
    Check("watch: the scrubber spans the whole recording", page.Find(".dc-range").GetAttribute("max") == lastTick.ToString());
    // the gate
    int? fell = rec0.Replay.Frames.Where(f => (f.WorldFlags & WorldFlag.GateFallen) != 0).Select(f => (int?)f.Tick).FirstOrDefault();
    Check("watch: the recording itself carries the gate falling (WorldFlags bit 0 set from some frame on)", fell is > 0, "gate falls at tick " + fell);
    int before = fell!.Value - 1;
    await page.InvokeAsync(() => page.Find(".dc-range").Input(before.ToString()));
    Check("watch: the tick before the fall shows the gate standing (no fallen block)", Text(page, ".dc-tick") == before.ToString() && !Has(page, ".dc-ground.gate.fallen") && Has(page, ".dc-ground.gate") && Text(page, ".dc-gatechip") == "The gate is standing");
    await page.InvokeAsync(() => page.Find(".dc-range").Input(fell.Value.ToString()));
    Check("watch: on the frame the flag is set the gate's picture swaps to fallen", Has(page, ".dc-ground.gate.fallen") && Text(page, ".dc-gatechip") == "The gate has fallen");
    await page.InvokeAsync(() => page.Find(".dc-range").Input(lastTick.ToString()));
    Check("watch: the gate stays fallen to the end (a bit never clears)", Has(page, ".dc-ground.gate.fallen") && Text(page, ".dc-tick") == lastTick.ToString());
    int endMine = rec0.Replay.Frames[^1].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength);
    Check("watch: the last frame's strengths match the recording", Text(page, ".dc-replay .dc-chips li.mine").Contains($"Your soldiers {endMine}"));
    await Tap(page, ".dc-restart");
    Check("watch: Start returns to tick 0 and the gate stands again", Text(page, ".dc-tick") == "0" && !Has(page, ".dc-ground.gate.fallen"));
    await Tap(page, ".dc-fwd");
    Check("watch: Next steps one tick", Text(page, ".dc-tick") == "1");
    await Tap(page, ".dc-speed");
    Check("watch: the speed button cycles 1x to 2x", Text(page, ".dc-speed") == "2x");
    await Tap(page, ".dc-play");
    Check("watch: Play starts the recording and ticks advance by themselves", await Until(() => int.Parse(Text(page, ".dc-tick")) >= 3, 5000, "play advances") && Text(page, ".dc-play") is "Pause" or "Play");
    await Tap(page, ".dc-play");
    string frozen = Text(page, ".dc-tick");
    await Task.Delay(500);
    Check("watch: Pause holds the tick", Text(page, ".dc-tick") == frozen, frozen);
    Check("watch: losses and spoils are listed, with the doctrine set and the general's (empty) suggestion left out", Text(page, ".dc-spoils").Contains("Spoils:") && Text(page, ".dc-spoils").Contains("You brought") && Text(page, ".dc-spoils").Contains("Your doctrine:"));
    // The panel names the army the round LEFT (a host wiped out has no list, only the header), read against the closed round's own army.
    Check("watch: the army after the round is shown", Regex.Replace(Text(page, ".dc-armypanel h2"), @"\s+", " ").Contains($"{S.Last!.ArmyAfter.Soldiers} soldiers, {S.Last.ArmyAfter.Gold} gold") && (S.Last.ArmyAfter.Soldiers == 0 || Has(page, ".dc-army")), Text(page, ".dc-armypanel h2"));

    // ---- the chronicle beside the field (live with the replay, a tapped line jumps the replay, the full account at the end)
    {
        var cv = S.Last!;
        string arenaNm = S.ArenaName(rec0.ArenaId);
        var expected = BattleChronicle.Build(rec0.Replay, rec0.PlayerDoctrine, arenaNm, DoctrineWords.KindName, cv.TraitsOf, cv.SiegeClock);
        var kids = page.Find(".dc-stage").Children.Select(c => string.Join(" ", c.ClassList)).ToList();
        Check("chronicle: the docked panel is in the stage after the field and the general, with a sheet button for a phone", kids.Count == 4 && kids[0].Contains("dc-armypanel") && kids[1].Contains("dc-field") && kids[2].Contains("dc-generalpanel") && kids[3].Contains("dc-chroniclepanel") && Has(page, "#dc-open-chronicle") && page.Find("#dc-chronicle").ClassList.Contains("dc-sheet"), string.Join(" | ", kids));
        Check("chronicle: the battle's set-up was seen (the player is the attacker) and the log reads each cohort's own traits from it", cv.Setup is { } su && su.Attacker.Side == Side.Player && su.Defender.Side == Side.Enemy && su.ArenaId == rec0.ArenaId
            && su.Attacker.Cohorts.All(c => cv.TraitsOf(Side.Player, c.Key) == c.Traits) && su.Defender.Cohorts.All(c => cv.TraitsOf(Side.Enemy, c.Key) == c.Traits));
        Check("chronicle: the page's log is the chronicle function applied to the recording (same lines, same why)", expected.Lines.Count >= 4 && ChronicleCheck.Verify(expected, rec0.Replay, DoctrineWords.KindName, cv.TraitsOf, cv.SiegeClock).Count == 0 && ChronicleCheck.VerifyWhy(expected, rec0.Replay, rec0.PlayerDoctrine, DoctrineWords.KindName).Count == 0,
            string.Join(" | ", ChronicleCheck.Verify(expected, rec0.Replay, DoctrineWords.KindName, cv.TraitsOf, cv.SiegeClock).Concat(ChronicleCheck.VerifyWhy(expected, rec0.Replay, rec0.PlayerDoctrine, DoctrineWords.KindName)).Take(3)));

        int[] LogTicks() => page.FindAll(".dc-logline[data-tick]").Select(e => int.Parse(e.GetAttribute("data-tick")!)).ToArray();
        string[] LogTexts() => page.FindAll(".dc-logline[data-tick] .x").Select(e => e.TextContent.Trim()).ToArray();
        async Task ScrubTo(int t) { await page.InvokeAsync(() => page.Find(".dc-range").Input(t.ToString())); await Until(() => Text(page, ".dc-tick") == t.ToString(), 3000, $"scrub to {t}"); }

        await ScrubTo(0);
        Check("chronicle: at tick 0 the log holds only the opening lines (the opening and, in a siege, the siege) and says it fills in as the battle plays; no Why yet",
            await Until(() => LogTicks().Length == expected.Lines.Count(l => l.Tick <= 0), 3000, "log at 0") && Has(page, ".dc-logempty") && !Has(page, ".dc-why") && LogTexts().OrderBy(x => x).SequenceEqual(expected.Lines.Where(l => l.Tick <= 0).Select(l => l.Text).OrderBy(x => x)));
        int contactTick = expected.Lines.First(l => l.Kind == ChronicleKind.Contact).Tick;
        int midTick = expected.Lines.Where(l => l.Kind == ChronicleKind.Break).Select(l => (int?)l.Tick).FirstOrDefault() ?? contactTick;
        await ScrubTo(midTick);
        var want = expected.Lines.Where(l => l.Tick <= midTick).ToList();
        Check("chronicle: scrubbing to a tick shows exactly the lines up to it, and the lines of that tick are marked now",
            await Until(() => LogTicks().Length == want.Count, 3000, "log at mid") && LogTexts().OrderBy(x => x).SequenceEqual(want.Select(l => l.Text).OrderBy(x => x))
            && page.FindAll(".dc-logline.now").Count == want.Count(l => l.Tick == midTick) && !Has(page, ".dc-why"), $"tick {midTick}, {want.Count} lines");
        Check("chronicle: the live box is the column-reverse one, newest line first in the markup (so it sits at the bottom and stays pinned there)", Has(page, "ol.dc-log.live") && LogTicks().First() == want.Max(l => l.Tick));
        Shot("c1-watch-mid", page);
        if (expected.Lines.FirstOrDefault(l => l.Kind == ChronicleKind.Gate) is { } gl)
        {
            await ScrubTo(gl.Tick - 1);
            bool noGateBefore = await Until(() => !Has(page, ".dc-logline.k-gate"), 3000, "no gate line before");
            await ScrubTo(gl.Tick);
            Check("chronicle: the gate line appears on the very tick the recording's flag is set, and not before", noGateBefore && await Until(() => Has(page, ".dc-logline.k-gate"), 3000, "gate line") && page.Find(".dc-logline.k-gate").GetAttribute("data-tick") == gl.Tick.ToString() && gl.Tick == fell, $"gate at {gl.Tick}, flag at {fell}");
        }
        else Check("chronicle: the gate line is there when the recording's flag is", fell is null);
        await ScrubTo(lastTick);
        Check("chronicle: at the last tick the whole account is there with the Why under it, and the Why lines are the function's",
            await Until(() => LogTicks().Length == expected.Lines.Count && Has(page, ".dc-why"), 3000, "full account") && page.FindAll(".dc-why-list li").Select(e => e.TextContent.Trim()).SequenceEqual(expected.Why.Lines));
        Scan(page);
        Shot("c2-watch-end", page);
        // a tapped line jumps the replay
        await Tap(page, $".dc-logline[data-tick='{contactTick}'] .dc-logbtn");
        Check("chronicle: tapping a line jumps the replay to that tick (and the log follows it back)", await Until(() => Text(page, ".dc-tick") == contactTick.ToString(), 3000, "jump") && await Until(() => LogTicks().Length == expected.Lines.Count(l => l.Tick <= contactTick), 3000, "log follows") && !Has(page, ".dc-why"), Text(page, ".dc-tick"));
        // the whole account on request, with the lines still to come marked
        await ScrubTo(0);
        await Tap(page, "#dc-log-all");
        Check("chronicle: Show the whole account lists every line in time order, marks the ones to come, and shows the Why",
            await Until(() => LogTicks().Length == expected.Lines.Count, 3000, "all lines") && LogTicks().SequenceEqual(expected.Lines.Select(l => l.Tick)) && page.FindAll(".dc-logline.later").Count == expected.Lines.Count(l => l.Tick > 0) && Has(page, ".dc-why") && Has(page, "ol.dc-log.all") && page.Find("#dc-log-all").GetAttribute("aria-pressed") == "true");
        Scan(page);
        await Tap(page, "#dc-log-all");
        Check("chronicle: and back to only what has happened", await Until(() => LogTicks().Length == expected.Lines.Count(l => l.Tick <= 0) && Has(page, "ol.dc-log.live"), 3000, "back to live"));
        // the phone's sheet button
        await ScrubTo(lastTick);
        await Tap(page, "#dc-open-chronicle");
        Shot("c3-watch-sheet", page);
        Check("chronicle: on a phone the Chronicle button pulls the chronicle sheet up (the General button steps aside on the watch screen to make room)", page.Find("#dc-chronicle").ClassList.Contains("open") && page.Find("#dc-open-chronicle").GetAttribute("aria-expanded") == "true" && !Has(page, "#dc-open-general"));
        await Tap(page, "#dc-open-chronicle");
        Check("chronicle: tapping the button again puts it away", !page.Find("#dc-chronicle").ClassList.Contains("open"));
        Console.WriteLine($"  (chronicle of the gate battle: {expected.Lines.Count} lines: {string.Join(", ", expected.Lines.GroupBy(l => l.Kind).Select(g => g.Key + " " + g.Count()))})");
        foreach (var l in expected.Lines) Console.WriteLine("    " + l.Full);
        foreach (var w in expected.Why.Lines) Console.WriteLine("    Why: " + w);
    }
}
Console.WriteLine($"  (round 0: doctrine {S.Records[0].PlayerDoctrine}, outcome {S.Records[0].Result.Outcome}, lost {S.Records[0].Result.AttackerLosses.Sum(l => l.Lost)}, army after {S.Last!.ArmyAfter.Soldiers} soldiers, gold {S.Last.ArmyAfter.Gold})");
Check("round 0: the general has learned twice (the doctrine, then the result)", S.Sections.General.ExamplesSeen == 2, S.Sections.General.ExamplesSeen.ToString());
Check("round 0: the general panel follows (2 examples seen)", Text(page, ".dc-examples").Contains("General: 2 examples seen"));
await Tap(page, "#dc-next");

// ---- round 1: the ghost after two examples; the visitor ignores it
await DoDraft(page, 1);
await Until(() => Has(page, ".dc-editor"), 30000, "round 1 doctrine screen");
var ghost1 = S.Ghost!;
Console.WriteLine($"  (round 1 ghost: {(ghost1.Doctrine.IsEmpty ? "none" : ghost1.Doctrine.ToString())}, confidence {ghost1.ConfidencePermille}, examples {S.Sections.General.ExamplesSeen})");
Check("ghost: after 2 examples the confidence is within 0..1000 and every suggested token is legal", ghost1.ConfidencePermille is >= 0 and <= 1000 && S.Army!.Legal.AllowsAll(ghost1.Doctrine));
Check("ghost: the general had something to suggest after seeing the visitor's first doctrine", !ghost1.Doctrine.IsEmpty);
if (!ghost1.Doctrine.IsEmpty)
{
    Check("ghost: the suggestion is shown with its match record as a glow (--glow 0..1) and as a number out of 1000", Has(page, ".dc-ghost") && Regex.IsMatch(page.Find(".dc-ghost").GetAttribute("style")!, @"--glow:(0|1|0\.\d+)") && Text(page, ".dc-conf").Contains($"Match record {ghost1.ConfidencePermille} of 1000"), page.Find(".dc-ghost").GetAttribute("style")!);
    Check("ghost: Use, Edit and Mine are three 44px buttons", Has(page, "#dc-accept") && Has(page, "#dc-edit") && Has(page, "#dc-ignore"));
    Scan(page);
    Shot("a2-doctrine-ghost", page);
}
await DoChoose(page, script, 1);
await Until(() => Has(page, ".dc-result"), 60000, "round 1 result");
Scan(page);
GroundAfter(page, 1); LimitAfter(page, 1);
Check("round 1: the visitor's doctrine was recorded as differing from the ghost (Overrode)", S.Records.Count == 2 && S.Records[1].Overrode == (S.Records[1].PlayerDoctrine != S.Records[1].Ghost), $"overrode={S.Records[1].Overrode}");
Check("round 1: the general has now learned 4 times", S.Sections.General.ExamplesSeen == 4, S.Sections.General.ExamplesSeen.ToString());
await Tap(page, "#dc-next");

// ---- round 2: the visitor accepts the ghost
await DoDraft(page, 2);
await Until(() => Has(page, ".dc-editor"), 30000, "round 2 doctrine screen");
var ghost2 = S.Ghost!;
Console.WriteLine($"  (round 2 ghost: {(ghost2.Doctrine.IsEmpty ? "none" : ghost2.Doctrine.ToString())}, confidence {ghost2.ConfidencePermille}, examples {S.Sections.General.ExamplesSeen})");
// Under the offers the general is bound to the council: a habit it learned on orders that are not on this round's table is removed, so it may have nothing to suggest.
Check("ghost: after 4 examples the general's pick, if it has one, is within the council and the army's legal list, its confidence within 0..1000; if not, the page says it has no pick from this council",
    ghost2.ConfidencePermille is >= 0 and <= 1000 && (ghost2.Doctrine.IsEmpty ? Has(page, ".dc-noghost") : S.Army!.Legal.AllowsAll(ghost2.Doctrine) && ghost2.Doctrine.Tokens.All(S.OrdersOffered!.Allows)), $"confidence {ghost2.ConfidencePermille}");
int examplesBeforeAccept = S.Sections.General.ExamplesSeen;
await DoChoose(page, script, 2);
await Until(() => Has(page, ".dc-result") || S.Stage == DoctrineStage.Over, 60000, "round 2 result");
Scan(page);
GroundAfter(page, 2); LimitAfter(page, 2);
Check("accept (round 3, when the general had a pick): the accepted pick is the doctrine that fought, recorded as NOT overridden",
    S.Records.Count == 3 && (ghost2.Doctrine.IsEmpty || (S.Records[2].PlayerDoctrine == ghost2.Doctrine && !S.Records[2].Overrode && S.Records[2].GhostConfidence == ghost2.ConfidencePermille)));
Check("accept: accepting is a training example too (the general learned from this round twice more)", S.Sections.General.ExamplesSeen == examplesBeforeAccept + 2, $"{examplesBeforeAccept} -> {S.Sections.General.ExamplesSeen}");
Check("accept (round 3, when the general had a pick): the spoils screen says the suggestion was taken as it stood", ghost2.Doctrine.IsEmpty || Text(page, ".dc-spoils").Contains("You took it as it stood."));
Check("last round: the next button says it is the end", Text(page, "#dc-next") == "See the end of the game");
Check("final: the pipeline marks only the last round Final, and the page takes its end from that (the button says so)", S.Records.Count == 3 && !S.Records[0].Final && !S.Records[1].Final && S.Records[2].Final && S.Last!.Last);
var lastArmyAfter = S.Last!.ArmyAfter;
await Tap(page, "#dc-next");

// ---- end screen, army code round trip, reload
await Until(() => Has(page, ".dc-end"), 20000, "end screen");
Scan(page);
var result = S.Result!;
Check("end: three rounds played, a summary line with won/lost/drawn and the seed", Text(page, ".dc-end .dc-says").Contains("3 rounds played") && Text(page, ".dc-end .dc-says").Contains($"Game seed {gateSeed}"), Text(page, ".dc-end .dc-says"));
Check("end: every round is listed with its outcome and doctrine", page.FindAll(".dc-rounds-list li").Count == 3);
Check("end: the army code is shown and is the final army's", Text(page, "#dc-armycode") == result.PlayerArmyCode && result.PlayerArmyCode.StartsWith("DA2."), result.PlayerArmyCode);
{
    var svc = new global::Doctrine.Army.PleromaArmyService();
    Check("code: it parses back to an army with the same read, score and code", svc.TryParse(result.PlayerArmyCode, out var back, out var err) && back!.Read().Digest() == lastArmyAfter.Digest() && back.ToCode() == result.PlayerArmyCode, err);
    Check("code: at most 600 bytes, URL-safe characters", result.PlayerArmyCode.Length <= 600 && Regex.IsMatch(result.PlayerArmyCode, @"^[A-Za-z0-9\-_.~:;,+=]+$"));
}
await Tap(page, "#dc-copy");
Check("end: Copy sends the code through the page's copyText hook", ctx.JSInterop.Invocations.Any(i => i.Identifier == "copyText" && (i.Arguments[0] as string) == result.PlayerArmyCode) && Has(page, ".dc-end .hint[role=status]"));
Check("end: the end screen says plainly the code holds the army only and a refresh forgets the general", Text(page, ".dc-end").Contains("It holds the army only") && Text(page, ".dc-end").Contains("a refresh forgets the general"));

// determinism: the page's game equals the headless game with the same hands
{
    var headless = await new DoctrineSections().Build(script, DoctrineSession.GameRules(3)).PlayAsync(gateSeed);
    if (headless.Digest() != result.Digest())
        for (int i = 0; i < Math.Min(headless.Rounds.Count, result.Rounds.Count); i++)
        {
            var pr = result.Rounds[i]; var hr = headless.Rounds[i];
            Console.WriteLine($"    round {i}: page doctrine '{pr.PlayerDoctrine}' ghost '{pr.Ghost}' conf {pr.GhostConfidence} overrode {pr.Overrode} dropped {pr.IllegalDropped} replay {ReplayCodec.Digest(pr.Replay):x} army {pr.PlayerDigest:x} enemy {pr.EnemyDigest:x} spoils {pr.Spoils.Gold}/{pr.Spoils.Experience}");
            if (ReplayCodec.Digest(pr.Replay) != ReplayCodec.Digest(hr.Replay))
            {
                var pf = pr.Replay.Frames; var hf = hr.Replay.Frames;
                Console.WriteLine($"    round {i}: arena page {pr.ArenaId} head {hr.ArenaId}; frames page {pf.Count} head {hf.Count}; ticks {pr.Result.Ticks} vs {hr.Result.Ticks}; outcome {pr.Result.Outcome} vs {hr.Result.Outcome}; losses {pr.Result.AttackerLosses.Sum(l => l.Lost)}/{pr.Result.DefenderLosses.Sum(l => l.Lost)} vs {hr.Result.AttackerLosses.Sum(l => l.Lost)}/{hr.Result.DefenderLosses.Sum(l => l.Lost)}");
                for (int k = 0; k < Math.Min(pf.Count, hf.Count); k++)
                {
                    string a = string.Join(";", pf[k].Blobs.Select(b => $"{b.Id}:{b.X},{b.Y},{b.Strength}")) + "|" + string.Join(";", pf[k].Events.Select(e => $"{e.Source}>{e.Target}={e.Damage}")) + "|" + pf[k].WorldFlags;
                    string h = string.Join(";", hf[k].Blobs.Select(b => $"{b.Id}:{b.X},{b.Y},{b.Strength}")) + "|" + string.Join(";", hf[k].Events.Select(e => $"{e.Source}>{e.Target}={e.Damage}")) + "|" + hf[k].WorldFlags;
                    if (a != h) { Console.WriteLine($"    first difference at frame {k} (tick {pf[k].Tick}):\n      page {a}\n      head {h}"); break; }
                }
            }
            Console.WriteLine($"    round {i}: head doctrine '{hr.PlayerDoctrine}' ghost '{hr.Ghost}' conf {hr.GhostConfidence} overrode {hr.Overrode} dropped {hr.IllegalDropped} replay {ReplayCodec.Digest(hr.Replay):x} army {hr.PlayerDigest:x} enemy {hr.EnemyDigest:x} spoils {hr.Spoils.Gold}/{hr.Spoils.Experience}");
        }
    Check("determinism: the page game and the headless game with the same hands have the SAME digest", headless.Digest() == result.Digest(), $"{result.Digest():x16} vs {headless.Digest():x16}");
    {
        // INFORMATION, not a check: the same game with the picks of each draft handed over in the opposite order.
        var reversed = await new DoctrineSections().Build(new ReversedPicks(script), DoctrineSession.GameRules(3)).PlayAsync(gateSeed);
        var which = Enumerable.Range(0, Math.Min(reversed.Rounds.Count, headless.Rounds.Count)).Where(i => ReplayCodec.Digest(reversed.Rounds[i].Replay) != ReplayCodec.Digest(headless.Rounds[i].Replay)).ToList();
        Console.WriteLine($"  (information: the same game with each draft's picks in the opposite order: digest {(reversed.Digest() == headless.Digest() ? "equal" : "DIFFERENT")}, recordings that differ: [{string.Join(",", which)}], army digests of those rounds equal: {which.All(i => reversed.Rounds[i].PlayerDigest == headless.Rounds[i].PlayerDigest)})");
    }
    Check("determinism: every round's recording is byte-identical",headless.Rounds.Count == 3 && headless.Rounds.Zip(result.Rounds).All(p => ReplayCodec.Encode(p.First.Replay).AsSpan().SequenceEqual(ReplayCodec.Encode(p.Second.Replay))));
    Check("determinism: the final army code is the same", headless.PlayerArmyCode == result.PlayerArmyCode);
}

// reload from the code
await Tap(page, "#dc-fresh");
Check("reload: back to the start screen keeps the general's examples and offers a new, empty one", Has(page, "#dc-start") && Has(page, "#dc-resetgeneral") && Text(page, "[aria-label='Your general']").Contains($"{S.Sections.General.ExamplesSeen} examples"));
await page.InvokeAsync(() => page.Find("#dc-code").Input(result.PlayerArmyCode));
await Tap(page, "#dc-load");
await Until(() => Has(page, ".dc-draft"), 20000, "draft after load");
Check("reload: pasting the code starts a game on that very army (same read, round and gold)", S.Draft is not null && S.Draft.Army.Digest() == lastArmyAfter.Digest(), $"{S.Draft?.Army.Digest():x16} vs {lastArmyAfter.Digest():x16}");
Check("reload: the general kept what it learned (ghost not reset)", S.Sections.General.ExamplesSeen == examplesBeforeAccept + 2);
await Tap(page, "#dc-leave");
await Until(() => Has(page, ".dc-end"), 20000, "left the game");
Check("leave: leaving at the draft screen hands back the army as it stood (the same code)", S.Result!.PlayerArmyCode == result.PlayerArmyCode && S.Result.Rounds.Count == 0);
await Tap(page, "#dc-fresh");
await Tap(page, "#dc-resetgeneral");
Check("general: starting with a new, empty general forgets the examples", S.Sections.General.ExamplesSeen == 0 && !Has(page, "#dc-resetgeneral"));

// ---------------------------------------------------------------------------------------------- game B: cancel mid-battle, then the Edit path
using var ctxB = NewCtx(false);
var pageB = ctxB.Render<DoctrinePage>();
livePages.Add(pageB);
DoctrineSession SB = pageB.Instance.Session!;
var scriptB = new ScriptHands([ScriptHands.How.Manual, ScriptHands.How.Edit]);
await pageB.InvokeAsync(() => pageB.Find("#dc-seed").Input("7"));
await Tap(pageB, "#dc-showall");
    await Tap(pageB, ".dc-opt", 0);
await Tap(pageB, "#dc-new");
await Until(() => Has(pageB, ".dc-draft"), 20000, "B draft");
ulong startDigestB = SB.Draft!.Army.Digest();
using var gate = new ManualResetEventSlim(false);
bool frozeAtTick2 = false;
Action<int> hold = t => { if (t == 2 && !frozeAtTick2) { frozeAtTick2 = true; gate.Wait(30000); } };
SB.Ticked += hold;
await DoDraft(pageB, 0);
await DoChoose(pageB, scriptB, 0);
Check("cancel: the battle reaches tick 2 and the page shows the progress and a Cancel button", await Until(() => frozeAtTick2 && Has(pageB, "#dc-cancel") && Has(pageB, ".dc-progress"), 30000, "battle at tick 2") && SB.Stage == DoctrineStage.Working, $"tick {SB.BattleTick}");
Scan(pageB);
Check("cancel: the progress text names the tick and says the battle runs on this device", Text(pageB, ".dc-working").Contains("Tick 2 of up to 120") && Text(pageB, ".dc-working").Contains("on this device") || Text(pageB, ".dc-working").Contains("Simulating the battle on this device"));
await Tap(pageB, "#dc-cancel");
gate.Set();
Check("cancel: the round stops and the draft screen comes back", await Until(() => SB.Stage == DoctrineStage.Draft && Has(pageB, ".dc-draft"), 30000, "back to draft after cancel"));
SB.Ticked -= hold;
Check("cancel: the page says the battle was cancelled", Text(pageB, ".dc-notice").Contains("Battle cancelled"), pageB.Find(".dc-notice").TextContent);
Check("cancel: the army is back exactly as it stood at the start of the round (same read)", SB.Draft!.Army.Digest() == startDigestB);
Check("cancel: no round was recorded", SB.Records.Count == 0);
await DoDraft(pageB, 0);
await DoChoose(pageB, scriptB, 0);
Check("cancel: playing the round again completes it", await Until(() => Has(pageB, ".dc-result"), 60000, "B round result") && SB.Records.Count == 1);
await Tap(pageB, "#dc-next");
await DoDraft(pageB, 1);
await Until(() => Has(pageB, ".dc-editor"), 30000, "B round 1 doctrine");
bool ghostB = !SB.Ghost!.Doctrine.IsEmpty;
Console.WriteLine($"  (B round 1 ghost: {(ghostB ? SB.Ghost.Doctrine.ToString() : "none")}, confidence {SB.Ghost.ConfidencePermille})");
var ghostBDoctrine = SB.Ghost.Doctrine;
await DoChoose(pageB, scriptB, 1);
await Until(() => Has(pageB, ".dc-result") || SB.Stage == DoctrineStage.Over, 60000, "B round 1 result");
Check("edit (round 2 of game B, when the general had a pick): Edit loads the pick into the slots; changing one order records an override", SB.Records.Count == 2 && (!ghostB || (SB.Records[1].Overrode && SB.Records[1].PlayerDoctrine != ghostBDoctrine && SB.Records[1].Ghost == ghostBDoctrine)));
Scan(pageB);
Check("leave: from the result screen the visitor can leave; the end screen then carries the two rounds played", Has(pageB, "#dc-leave"));
await Tap(pageB, "#dc-leave");
await Until(() => Has(pageB, ".dc-end"), 20000, "B end");
Check("leave: two rounds listed and the code is the army after round 2", pageB.FindAll(".dc-rounds-list li").Count == 2 && SB.Result!.PlayerArmyCode.StartsWith("DA2."));

// ---------------------------------------------------------------------------------------------- autoplay (the app's default) on a real recording
{
    var recB = SB.Last!.Record;
    using var ctxP = new BunitContext();
    ctxP.JSInterop.Mode = JSRuntimeMode.Loose;
    ctxP.Services.AddSingleton<IDoctrineSettings>(new DoctrineSettings { AutoPlay = true, TickMs = 15 });
    var arenaP = ctxP.Render<Showroom.Components.DoctrineArena>(ps => ps.Add(p => p.Replay, recB.Replay).Add(p => p.Layout, SB.Last!.Layout));
    Check("autoplay: with autoplay on, the recording plays by itself to its last tick and then stops", await Until(() => arenaP.Instance.Tick == recB.Replay.Frames.Count - 1, 20000, "autoplay to the end") && await Until(() => !arenaP.Instance.Playing, 3000, "autoplay stopped"), $"tick {arenaP.Instance.Tick} of {recB.Replay.Frames.Count - 1}");
    Check("autoplay: at the end the button offers Play again", arenaP.Find(".dc-play").TextContent.Trim() == "Play");
    await arenaP.InvokeAsync(() => arenaP.Instance.Seek(0));
    Check("autoplay: a jump to tick 0 shows tick 0", arenaP.Find(".dc-tick").TextContent.Trim() == "0");
}

// ---------------------------------------------------------------------------------------------- game O: skipped orders, and what the game refuses outside the offer (the hooks' facts)
{
    // A seed whose round-0 council holds two orders for the whole host of which the second is skipped when it follows the first (found headless, with the
    // script's own draft, on the same sections and the same offer source).
    (int Seed, int A, int B)? skipPair = null;
    for (int sd = 1; sd < 800 && skipPair is null; sd++)
    {
        var svc0 = new global::Doctrine.Army.PleromaArmyService();
        var army0 = svc0.Create(Side.Player, sd);
        int g = army0.Read().Gold;
        await army0.DraftAsync(new DraftRequest(0, sd, ScriptHands.PickList(SeededOffers.Instance.Units(sd, 0, g, poolRulesA), g)), CancellationToken.None);
        var rd = army0.Read();
        var oo = SeededOffers.Instance.Orders(sd, 0, 0, rd.Legal, poolRulesA);
        var armyOrders = oo.Tokens.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Army).ToList();
        foreach (int a in armyOrders)
            foreach (int b in armyOrders)
            {
                if (a == b || skipPair is not null) continue;
                svc0.TryParse(army0.ToCode(), out var cl, out _);
                var r = cl!.ApplyArmyOrdersAsync(DoctrineSet.Of(a, b), CancellationToken.None).AsTask().Result;
                if (r.Applied == 1 && r.Skipped == 1) skipPair = (sd, a, b);
            }
    }
    Check("setup: a seed was found whose round-0 council holds two orders for the whole host of which the second is skipped after the first", skipPair is not null, skipPair is { } sp ? $"seed {sp.Seed}: {DoctrineVocabulary.Name(sp.A)} then {DoctrineVocabulary.Name(sp.B)}" : "none");
    int skipSeed = skipPair?.Seed ?? 11;
    using var ctxO = NewCtx(false);
    var pageO = ctxO.Render<DoctrinePage>();
    livePages.Add(pageO);
    DoctrineSession SO = pageO.Instance.Session!;
    await pageO.InvokeAsync(() => pageO.Find("#dc-seed").Input(skipSeed.ToString()));
    await Tap(pageO, "#dc-showall");
    await Tap(pageO, ".dc-opt", 0);
    await Tap(pageO, "#dc-new");
    await DoDraft(pageO, 0);
    if (skipPair is { } pair)
    {
        await TapChip(pageO, pair.A);
        await TapChip(pageO, pair.B);
        await Tap(pageO, "#dc-fight");
        await Until(() => Has(pageO, ".dc-result") || SO.Stage == DoctrineStage.Over, 60000, "O result");
        Scan(pageO);
        var recO = SO.Records.FirstOrDefault();
        Check("orders: the record carries the army's own count (one applied, one skipped: the first order leaves the host unable to take the second)", recO?.Orders is { Applied: 1, Skipped: 1 } && recO.OfferRefused == 0, recO?.Orders?.ToString() ?? "none");
        Check("orders: the watch screen says it in plain words", Has(pageO, ".dc-applied") && Text(pageO, ".dc-applied") == "1 of your 2 orders for the whole host was applied. The host skipped 1 that was not allowed when its turn came.", Has(pageO, ".dc-applied") ? Text(pageO, ".dc-applied") : "missing");
        Check("offers: a round that went by the book says nothing about refusals", !Has(pageO, ".dc-offerrefused") && !Has(pageO, ".dc-rerolled"));
        await Tap(pageO, "#dc-leave");
        await Until(() => Has(pageO, ".dc-end"), 20000, "O end");
        var codeO = SO.Result!.PlayerArmyCode;
        Check("leave: from the watch screen the end screen carries the army AFTER the closed round and the round played", SO.Result.Rounds.Count == 1 && new global::Doctrine.Army.PleromaArmyService().TryParse(codeO, out var armyO, out _) && armyO!.Read().Digest() == SO.Last!.ArmyAfter.Digest());
    }
}

// ---------------------------------------------------------------------------------------------- game X: what the game refuses when a pick is outside the offer (the page cannot make one, so the session is handed one)
{
    using var ctxX = NewCtx(false);
    var pageX = ctxX.Render<DoctrinePage>();
    livePages.Add(pageX);
    DoctrineSession SX = pageX.Instance.Session!;
    await pageX.InvokeAsync(() => pageX.Find("#dc-seed").Input("11"));
    await Tap(pageX, "#dc-showall");
    await Tap(pageX, ".dc-opt", 0);
    await Tap(pageX, "#dc-new");
    await Until(() => Has(pageX, ".dc-draft"), 20000, "X draft");
    var offerX = SX.Draft!.Offer;
    int goldX = SX.Draft.Army.Gold;
    var bogus = new DraftPick(offerX.Packs.Count > 0 ? (offerX.Packs[0].Kind + 1) % Roster.Size : 0, 1);      // a pick that is no pack of the offer (a different count of a kind, or a kind not offered)
    while (offerX.PackFor(bogus) is not null) bogus = bogus with { Count = bogus.Count + 1 };
    var okPack = offerX.Packs.First(p => p.Cost <= goldX);
    await pageX.InvokeAsync(() => SX.SubmitDraft([bogus, UnitOffer.Pick(okPack), UnitOffer.Pick(okPack)]));      // not offered, then the pack once, then the same pack again
    await Until(() => SX.Stage == DoctrineStage.Doctrine || Has(pageX, "#dc-keep"), 20000, "X ask");
    string note = Has(pageX, ".dc-notice") ? Text(pageX, ".dc-notice") : "no notice";
    Check("refusals (draft): a pick that is no offered pack, and an offered pack bought twice, are refused by the game and said in plain words after the confirmation",
        note.StartsWith("The army did not take part of your draft.") && note.Contains($"{DoctrineWords.KindName(bogus.Kind)} x{bogus.Count}: That company is not on offer, or it was already bought this round.") && note.Split("That company is not on offer").Length == 3, note);
    await PassAsk(pageX);
    var legalX = SX.Army!.Legal; var offerOX = SX.OrdersOffered!;
    int outsideX = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => legalX.Allows(t) && !offerOX.Tokens.Contains(t));
    var field = offerOX.Tokens.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).Take(2).ToList();
    await pageX.InvokeAsync(() => SX.SubmitChoice(new DoctrineSet(outsideX, field[0], field[0], field[1]), true));      // not on the table, a card, the same card again, another card (two refused here, two in the draft)
    await Until(() => Has(pageX, ".dc-result") || SX.Stage == DoctrineStage.Over, 60000, "X result");
    var recX = SX.Records[0];
    Check("refusals (orders): a token that is not on the table and one given twice are removed by the game and counted: the doctrine that fought is the two real ones, in their slots",
        recX.PlayerDoctrine == new DoctrineSet(0, field[0], 0, field[1]) && recX.OfferRefused == 4 && recX.IllegalDropped == 0, $"{recX.PlayerDoctrine} refused {recX.OfferRefused} dropped {recX.IllegalDropped}");
    Scan(pageX);
    Check("refusals: the watch screen says plainly how many requests outside the offer were refused, the record's own count", Has(pageX, ".dc-offerrefused") && Text(pageX, ".dc-offerrefused") == DoctrineWords.OfferRefused(recX.OfferRefused) && recX.OfferRefused == 4, Has(pageX, ".dc-offerrefused") ? Text(pageX, ".dc-offerrefused") : "missing");
    await Tap(pageX, "#dc-leave");
    await Until(() => Has(pageX, ".dc-end"), 20000, "X end");
}
// ---------------------------------------------------------------------------------------------- the per-slot match record, and its fallback
foreach (var withSlots in new[] { true, false })
{
    var slotValues = new SlotPermille(900, 300, 650, 0);
    using var ctxS = NewCtx(false);
    ctxS.Services.AddSingleton(new DoctrineSectionsSource(() => new DoctrineSections(new SlotLens(new global::Doctrine.General.TinyHoloGeneral(), withSlots ? slotValues : null))));
    var pageS = ctxS.Render<DoctrinePage>();
    livePages.Add(pageS);
    DoctrineSession SS = pageS.Instance.Session!;
    await pageS.InvokeAsync(() => pageS.Find("#dc-seed").Input("5"));
    await Tap(pageS, "#dc-showall");
    await Tap(pageS, ".dc-opt", 0);
    await Tap(pageS, "#dc-new");
    await DoDraft(pageS, 0);
    await Until(() => Has(pageS, ".dc-editor"), 20000, "S doctrine");
    string tag = withSlots ? "per-slot" : "fallback";
    Scan(pageS);
    var gs = SS.Ghost!;
    var li = pageS.FindAll(".dc-ghostslot");
    Check($"match record ({tag}): the ghost shows one line per order it suggests, and the whole figure under it", li.Count == gs.Doctrine.Used && li.Count >= 2 && Text(pageS, ".dc-conf").StartsWith($"Match record {gs.ConfidencePermille} of 1000 for the whole pick: over about the last {RecordWindowHand} rounds, the share of rounds in which all of it came out exactly as the doctrine you then set."), Text(pageS, ".dc-conf"));
    if (withSlots)
    {
        bool each = true;
        for (int k = 0; k < li.Count; k++)
        {
            int slot = int.Parse(li[k].GetAttribute("data-slot")!);
            each &= (li[k].GetAttribute("style") ?? "").Trim().TrimEnd(';') == "--glow:" + (slotValues[slot] / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) && li[k].QuerySelector(".dc-slotrec")!.TextContent.Trim() == $"Match record {slotValues[slot]} of 1000 on this slot";
        }
        Check("match record (per-slot): each order's glow and its figure come from the general's own value for THAT slot", each, string.Join(" | ", li.Select(e => e.GetAttribute("style") + " " + e.TextContent.Trim())));
        Check("match record (per-slot): the panel's glow is the average of the slots shown", Regex.IsMatch(page_style(pageS), @"--glow:0\.6\d*\b"), page_style(pageS));
    }
    else
    {
        Check("match record (fallback): with no per-slot reading the orders carry no figure of their own and the glow is the whole figure", pageS.FindAll(".dc-slotrec").Count == 0 && li.All(e => !e.HasAttribute("style")) && page_style(pageS).Contains("--glow:" + (gs.ConfidencePermille / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), page_style(pageS));
    }
    await Tap(pageS, "#dc-leave");
    await Until(() => Has(pageS, ".dc-end"), 20000, "S end");
}
string page_style(IRenderedComponent<DoctrinePage> p) => p.Find(".dc-ghost").GetAttribute("style") ?? "";

// ---------------------------------------------------------------------------------------------- an army with no soldiers
{
    using var ctxN = NewCtx(false);
    var pageN = ctxN.Render<DoctrinePage>();
    livePages.Add(pageN);
    await pageN.InvokeAsync(() => pageN.Find("#dc-code").Input("DA2.0.0.12.0.."));
    await Tap(pageN, "#dc-load");
    await Until(() => Has(pageN, ".dc-draft"), 20000, "N draft");
    Check("no soldiers: the draft screen says the army has none and to spend the gold", Text(pageN, ".dc-draft").Contains("no soldiers left") && Text(pageN, ".dc-draft").Contains("Spend your gold on cohorts: a battle needs soldiers."));
    Scan(pageN);
    await Tap(pageN, "#dc-confirm");
    await PassAsk(pageN);
    await Until(() => Has(pageN, ".dc-editor"), 20000, "N doctrine");
    Check("no soldiers: nothing is legal, so the council holds no card and the page says why", pageN.FindAll(".dc-chip").Count == 0 && pageN.FindAll(".dc-form:not([disabled])").Count == 1 && Text(pageN, ".dc-tray").Contains("no order can be given"));
    Scan(pageN);
    await Tap(pageN, "#dc-leave");
    Check("no soldiers: leaving still hands back a valid army code", await Until(() => Has(pageN, ".dc-end"), 20000, "N end") && Text(pageN, "#dc-armycode").StartsWith("DA2."));
}

// ---------------------------------------------------------------------------------------------- game U: a first game, nothing opened by hand
// The schedule, from the rounds option (written out here by hand, not read back from the table under test).
Check("unlock schedule: 8 rounds open a step a round, the last orders at round 5", Enumerable.Range(0, 8).Select(r => DoctrineUnlocks.StepForRound(r, 8)).SequenceEqual([0, 1, 2, 3, 4, 4, 4, 4]));
Check("unlock schedule: 5 rounds fold the last two steps into the first round of the last third (round 4)", Enumerable.Range(0, 5).Select(r => DoctrineUnlocks.StepForRound(r, 5)).SequenceEqual([0, 1, 2, 4, 4]));
Check("unlock schedule: 3 rounds open everything in the last round", Enumerable.Range(0, 3).Select(r => DoctrineUnlocks.StepForRound(r, 3)).SequenceEqual([0, 1, 4]));
Check("unlock steps: slots 2, 3, 4, 4, 4 and the kinds open 2, 4, 6, 7, 8", Enumerable.Range(0, 5).Select(DoctrineUnlocks.Slots).SequenceEqual([2, 3, 4, 4, 4]) && Enumerable.Range(0, 5).Select(s => Enumerable.Range(0, 8).Count(k => DoctrineUnlocks.KindOpen(k, s))).SequenceEqual([2, 4, 6, 7, 8]));

HashSet<int> ExpectedChips(int step)
{
    var set = new HashSet<int>();
    int fieldKinds = 2 * (Math.Min(step, 2) + 1);                      // Advance, Hold | + Flank, Fall back | + Guard, Hunt
    for (int g = 0; g < 3; g++) for (int f = 0; f < fieldKinds; f++) set.Add(DoctrineVocabulary.Field((FieldOrderKind)f, g));
    if (step >= 2) for (int q = 1; q <= 3; q++) set.Add(DoctrineVocabulary.WhenLost(q));          // the three loss triggers open with Guard and Hunt (four slots)
    if (step >= 3) { set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1)); set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2)); set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Age)); }
    if (step >= 4)
    {
        for (int b = 0; b < 8; b++) { set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Flip, b)); set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Aura, b)); }
        for (int k = 0; k < 8; k++) set.Add(DoctrineVocabulary.Army(ArmyOrderKind.Merge, k));
    }
    return set;
}
bool IsCull(int t) => DoctrineVocabulary.Decode(t) is { Family: OrderFamily.Army } o && (ArmyOrderKind)o.Kind == ArmyOrderKind.Cull;
string GeneralFree(string s) => Regex.IsMatch(s, "general", RegexOptions.IgnoreCase) ? "mentions the general" : "";

// THE LADDER IS THE HOST'S POOL (hand-written expectations: orders by ExpectedChips, kinds by roster key)
{
    var rules5 = DoctrineSession.GameRules(5, rr => DoctrineUnlocks.StepForRound(rr, 5)).Offers;
    int[] kindStepH = [0, 1, 1, 0, 2, 2, 3, 4], stepsH = [0, 1, 2, 4, 4];
    bool orderPool = rules5.OrderPool is not null && rules5.KindPool is not null, kindPool = orderPool, widening = true;
    int prevO = 0, prevK = 0;
    for (int rr = 0; rr < 5 && orderPool; rr++)
    {
        var open = ExpectedChips(stepsH[rr]);
        for (int t = 1; t < DoctrineVocabulary.Size; t++) if (!IsCull(t)) orderPool &= rules5.OrderPool!(rr, t) == open.Contains(t);
        for (int k = 0; k < Roster.Size; k++) kindPool &= rules5.KindPool!(rr, k) == (kindStepH[k] <= stepsH[rr]);
        int po = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Count(t => rules5.OrderPool!(rr, t)), pk = Enumerable.Range(0, Roster.Size).Count(k => rules5.KindPool!(rr, k));
        widening &= po >= prevO && pk >= prevK; prevO = po; prevK = pk;
    }
    Check("pools: a round's order pool is exactly the orders its step opens and its kind pool the kinds it opens (the unlock ladder as the host's pools)", orderPool && kindPool);
    Check("pools: the pools only widen from round to round", widening);
    Check("pools: without a ladder the pools are unset (the game's whole roster and vocabulary)", DoctrineSession.GameRules(3).Offers.OrderPool is null && DoctrineSession.GameRules(3).Offers.KindPool is null && DoctrineSession.GameRules(3).Offers.Enabled && DoctrineSession.GameRules(3).Offers.Enforce);
    // an offer drawn under the pool holds nothing the step has not opened (round one: 2 kinds, only Press on and Hold the line)
    bool narrowOrders = true, narrowKinds = true;
    for (int sd = 1; sd <= 60; sd++)
    {
        narrowOrders &= SeededOffers.Instance.Orders(sd, 0, 0, LegalMask.All, rules5).Tokens.All(t => ExpectedChips(0).Contains(t));
        narrowKinds &= SeededOffers.Instance.Units(sd, 0, 40, rules5).Packs.All(p => p.Kind is 0 or 3);
    }
    Check("pools: an offer drawn in round one holds only the orders and kinds round one has opened (sixty seeds)", narrowOrders && narrowKinds);
}

{
    using var ctxU = NewCtx(false);
    var pageU = ctxU.Render<DoctrinePage>();
    livePages.Add(pageU);
    DoctrineSession SU = pageU.Instance.Session!;
    var PU = pageU.Instance.Progress;
    await pageU.InvokeAsync(() => pageU.Find("#dc-seed").Input("3"));
    await Tap(pageU, ".dc-opt", 1);   // 5 rounds
    await Tap(pageU, "#dc-new");
    var stagesSeen = new HashSet<string>();
    int[] stepOf5 = [0, 1, 2, 4, 4];
    string[][] newLines =
    [
        [],
        ["New orders: Ride the flank, Fall back.", "New slot: 3.", "New soldiers: Shieldwall, Riders."],
        ["New orders: Guard the host, Run them down, Should the host fall.", "New slot: 4.", "New soldiers: Priests, Wights."],
        ["New orders: Reform the ranks, Drill the host, Reforge, Swear into a kind, Raise a banner.", "New soldiers: Wolves, Catapults."],
        [],
    ];
    int[] kindsOpen = [2, 4, 6, 8, 8], slotsOpen = [2, 3, 4, 4, 4];
    string[] kindNames0 = ["Spearmen", "Archers"];

    for (int r = 0; r < 5; r++)
    {
        string tag = $"U round {r + 1}";
        int step = stepOf5[r];
        await Until(() => SU.Stage == DoctrineStage.Draft && SU.RoundIndex == r && Has(pageU, ".dc-draft"), 40000, tag + " draft");
        Scan(pageU);
        stagesSeen.Add(pageU.Find(".dc-hud").GetAttribute("data-stage")!);
        if (r == 0)
        {
            var hud = pageU.Find(".dc-hud");
            var kids = hud.Children.Select(c => c.ClassList.FirstOrDefault(x => x is "dc-top" or "dc-stage" or "dc-dock") ?? "?").ToList();
            Check("HUD: one persistent scene: a top bar, a stage and a dock, in that order", pageU.FindAll(".dc-hud").Count == 1 && kids.SequenceEqual(["dc-top", "dc-stage", "dc-dock"]), string.Join(",", kids));
            var stageKids = pageU.Find(".dc-stage").Children.Select(c => string.Join(" ", c.ClassList)).ToList();
            Check("HUD: the stage holds the army panel, the field and the general panel, in that order, and the draft opens as a drawer in the same stage", stageKids.Count >= 4 && stageKids[0].Contains("dc-armypanel") && stageKids[1].Contains("dc-field") && stageKids[2].Contains("dc-generalpanel") && stageKids[3].Contains("dc-draft"), string.Join(" | ", stageKids));
            Check("HUD: no tabs, no tab roles and no links inside the scene", pageU.FindAll(".dc-hud [role=tab], .dc-hud [role=tablist], .dc-hud .dc-tab, .dc-hud a[href], .dc-hud nav").Count == 0);
            Check("HUD: the top bar carries the round, the gold, a one-line goal and the thing to do now", Text(pageU, ".dc-status").Contains("Round 1 of 5") && Text(pageU, ".dc-status").Contains("Gold ") && Text(pageU, ".dc-goal").StartsWith("Goal:") && Text(pageU, ".dc-todo").Length > 10, Text(pageU, ".dc-todo"));
            Check("HUD: the army list is on the left panel, the field in the centre", Has(pageU, ".dc-armypanel .dc-army") && Has(pageU, ".dc-field .dc-line"));

            // ---- the army panel in the first game: plain words, only the open kinds named, the rest under one collapsed line
            {
                var army = SU.Army!;
                var openC = army.Cohorts.Where(c => DoctrineUnlocks.KindOpen(c.Key, 0)).ToList();
                var otherC = army.Cohorts.Where(c => !DoctrineUnlocks.KindOpen(c.Key, 0)).ToList();
                var rows = pageU.FindAll(".dc-armypanel > .dc-cohortlist.dc-army > li");
                Check("army panel (round 1): one row for each open kind the army holds, and no other kind is named in the list", rows.Count == openC.Count && rows.All(r => r.QuerySelector(".nm")!.TextContent.Trim() is "Spearmen" or "Archers"), string.Join(",", rows.Select(r => r.QuerySelector(".nm")!.TextContent.Trim())));
                Check("army panel (round 1): each row is one plain sentence in the form 'Spearmen, 10: basic fighters in the front line.'", rows.Zip(openC).All(p => Regex.Replace(p.First.QuerySelector(".plain")!.TextContent, @"\s+", " ").Trim() == DoctrineWords.CohortSentence(p.Second.Key, p.Second.Count, p.Second.Slot)),
                    string.Join(" | ", rows.Select(r => Regex.Replace(r.QuerySelector(".plain")!.TextContent, @"\s+", " ").Trim())));
                Check("army panel (round 1): the sentences carry no trait name, no 'veterancy', no 'power' and no 'None'", rows.All(r => !Regex.IsMatch(r.QuerySelector(".plain")!.TextContent, @"Heavy|Swift|Ranged|Holy|Cursed|Beast|Machine|Shielded|[Vv]eterancy|[Pp]ower|None")));
                Check("army panel (round 1): traits, veterancy and power sit in a Details expander of each cohort, closed", rows.Zip(openC).All(p => p.First.QuerySelector("details.dc-det") is { } d && !d.HasAttribute("open") && d.QuerySelector("summary")!.TextContent.Trim() == "Details"
                    && Regex.Replace(d.QuerySelector(".meta")!.TextContent, @"\s+", " ").Trim() == $"Traits: {DoctrineWords.TraitList(p.Second.Traits)}. Seasoned {p.Second.Veterancy} {(p.Second.Veterancy == 1 ? "year" : "years")}. Power {p.Second.PowerPct}%."));
                Check("army panel (round 1): the same Details say what being dug in is worth to that cohort, from the battle's table (written out by hand: 32% for a plain line, 12% for far shooters, nothing for the longest reach)",
                    rows.Count > 0 && rows.Zip(openC).All(p => p.First.QuerySelector("details.dc-det .dc-dugin") is { } e && e.TextContent.Trim() == (ChronicleCheck.HandBonus(p.Second.Traits) > 0 ? $"Dug in, it strikes {ChronicleCheck.HandBonus(p.Second.Traits)}% harder." : "Dug in, it gains nothing: its reach is already the longest.")));
                var oth = pageU.FindAll(".dc-armypanel details.dc-other");
                Check("army panel (round 1): the kinds not open yet wait under one collapsed 'Other soldiers: N' line (N is their soldiers), expandable to the same plain rows", otherC.Count == 0 ? oth.Count == 0
                    : oth.Count == 1 && !oth[0].HasAttribute("open") && oth[0].QuerySelector("summary")!.TextContent.Trim() == $"Other soldiers: {otherC.Sum(c => c.Count)}" && oth[0].QuerySelectorAll(".dc-cohortlist > li").Length == otherC.Count
                    && !pageU.Find(".dc-armypanel > .dc-cohortlist").TextContent.Contains("Other"), $"{otherC.Count} locked cohorts, {otherC.Sum(c => c.Count)} soldiers");
                Console.WriteLine($"  (round 1 levy of seed 3: {string.Join(", ", army.Cohorts.Select(c => c.Count + " " + DoctrineWords.KindName(c.Key)))}; {openC.Sum(c => c.Count)} soldiers of open kinds, {otherC.Sum(c => c.Count)} of the others; {army.Gold} gold)");
                // ---- the field before the first battle: your three lines, back to front from the left
                var lanes = pageU.FindAll(".dc-field .dc-lines .dc-lane");
                Check("field (before a battle): your three lines are drawn, back to front, with the enemy's place on the right", lanes.Count == 3 && lanes.Select(l => l.GetAttribute("data-slot")).SequenceEqual(["2", "1", "0"]) && Has(pageU, ".dc-field .dc-foe")
                    && lanes[0].QuerySelector("b")!.TextContent.Trim() == "Back line" && lanes[2].QuerySelector("b")!.TextContent.Trim() == "Front line");
                var discs = pageU.FindAll(".dc-field .dc-disc");
                Check("field (before a battle): every cohort is a disc with its count (they add up to the army), open kinds named and the others called 'Other'", discs.Count == army.Cohorts.Count && discs.Sum(d => int.Parse(d.QuerySelector("b")!.TextContent.Trim())) == army.Soldiers
                    && discs.All(d => d.QuerySelector("i")!.TextContent.Trim() is "Spearmen" or "Archers" or "Other"));
            }
        }
        // the unlock announcement
        if (newLines[r].Length == 0) Check($"{tag}: nothing new opens, so no announcement", !Has(pageU, ".dc-unlock"));
        else
        {
            string card = Regex.Replace(Text(pageU, ".dc-unlock"), @"\s+", " ");
            var lines = pageU.FindAll(".dc-unlock-line").Select(e => e.TextContent.Trim()).ToList();
            Check($"{tag}: the unlock is announced in one line per kind of thing ({string.Join(" / ", newLines[r])})", lines.SequenceEqual(newLines[r]), string.Join(" / ", lines));
            var fx = pageU.FindAll(".dc-unlock-fx li").Select(e => e.TextContent.Trim()).ToList();
            var named = newLines[r][0].Split(':')[1].Trim().TrimEnd('.').Split(", ");
            Check($"{tag}: each new order shows a one-line plain effect before the player confirms", fx.Count == named.Length && fx.Zip(named).All(p => p.First.StartsWith(p.Second + ": ") && p.First.Length > p.Second.Length + 10), string.Join(" | ", fx));
            Check($"{tag}: the announcement never says the general and has a confirm button", GeneralFree(card) == "" && Has(pageU, "#dc-unlock-ok"));
            if (r == 1) Shot("u2-unlock", pageU);
            await Tap(pageU, "#dc-unlock-ok");
            Check($"{tag}: confirming the announcement removes it", !Has(pageU, ".dc-unlock"));
        }
        // THE LADDER WIDENS THE POOL: a round offers only the kinds its step has opened (the table is written out here by hand: kinds by roster key)
        int[] kindStepHand = [0, 1, 1, 0, 2, 2, 3, 4];
        var offerKinds = SU.Draft!.Offer.Packs.Select(p => p.Kind).ToList();
        var kindRows = offerKinds.Select(k => Roster.NameOf(k)).ToList();
        Check($"{tag}: the muster offers only kinds the step has opened (a pool of {kindsOpen[r]} kinds), each once, and never more than the pool or the pack count",
            offerKinds.Count > 0 && offerKinds.All(k => kindStepHand[k] <= step) && offerKinds.Distinct().Count() == offerKinds.Count && offerKinds.Count <= Math.Min(kindsOpen[r], OfferRules.Default.PackCount), string.Join(",", kindRows));
        Check($"{tag}: the cards on the page are the session's offer, and the offer is the game's own for this seed, round and purse under the ladder's pool",
            pageU.FindAll(".dc-pack").Select(e => (int.Parse(e.GetAttribute("data-kind")!), int.Parse(e.GetAttribute("data-count")!))).SequenceEqual(SU.Draft!.Offer.Packs.Select(p => (p.Kind, p.Count)))
            && SU.Draft.Offer.Packs.SequenceEqual(SeededOffers.Instance.Units(3, r, SU.Army!.Gold, DoctrineSession.GameRules(5, rr => DoctrineUnlocks.StepForRound(rr, 5)).Offers).Packs));
        Check($"{tag}: the dock shows exactly the open slots ({slotsOpen[r]})", pageU.FindAll(".dc-dock .dc-slot").Count == slotsOpen[r]);
        if (r == 0)
        {
            Check("round 1: the first muster offers only two kinds of soldier, Spearmen and Archers (never more than one company of each)", offerKinds.All(k => k is 0 or 3) && offerKinds.Count <= kindNames0.Length);
            Check("round 1: the locked kinds are said to arrive later, with a Show everything control", Has(pageU, ".dc-lockedhint") && Has(pageU, "#dc-showall"));
            Shot("u1-draft", pageU);
            // sheets: tap and drag, never hover
            Check("sheets: only the draft drawer starts open", pageU.Find(".dc-draft").ClassList.Contains("open") && !pageU.Find("#dc-army").ClassList.Contains("open") && !pageU.Find("#dc-general").ClassList.Contains("open"));
            await Tap(pageU, "#dc-open-army");
            Check("sheets: tapping Army pulls the army sheet up (and closes the draft drawer)", pageU.Find("#dc-army").ClassList.Contains("open") && !pageU.Find(".dc-draft").ClassList.Contains("open") && pageU.Find("#dc-open-army").GetAttribute("aria-expanded") == "true");
            await Tap(pageU, "#dc-open-army");
            Check("sheets: tapping Army again puts it away", !pageU.Find("#dc-army").ClassList.Contains("open"));
            await pageU.InvokeAsync(async () => { var b = pageU.Find("#dc-open-general"); await b.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientY = 500 }); await b.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientY = 380 }); });
            Check("sheets: dragging up on General pulls the general sheet up", pageU.Find("#dc-general").ClassList.Contains("open"));
            await pageU.InvokeAsync(async () => { var g = pageU.Find("#dc-general .dc-grip"); await g.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientY = 300 }); await g.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientY = 420 }); });
            Check("sheets: dragging the handle down puts it away", !pageU.Find("#dc-general").ClassList.Contains("open"));
            await Tap(pageU, "#dc-draftbtn");
            Check("sheets: the Draft button in the dock brings the drawer back", pageU.Find(".dc-draft").ClassList.Contains("open"));
        }
        // starter advice, draft
        Check($"{tag}: the advice button says 'Use starter advice' and never the general", Text(pageU, ".dc-draft #dc-advice-btn") == "Use starter advice");
        await Tap(pageU, ".dc-draft #dc-advice-btn");
        {
            string adv = Text(pageU, ".dc-advice");
            int gold = SU.Army!.Gold;
            var boughtU = pageU.FindAll(".dc-pack.on").Select(e => (Kind: int.Parse(e.GetAttribute("data-kind")!), Count: int.Parse(e.GetAttribute("data-count")!))).ToList();
            int spent = boughtU.Sum(b => b.Count * Roster.PriceOf(b.Kind));
            Check($"{tag}: starter advice is labelled as the game's advice, never as the general, and buys only offered companies within the gold", adv.StartsWith("Game advice:") && GeneralFree(adv) == "" && spent <= gold && (gold < 2 || spent > 0)
                && boughtU.All(b => SU.Draft!.Offer.Packs.Any(p => p.Kind == b.Kind && p.Count == b.Count)), $"{adv} (spent {spent} of {gold})");
            Check($"{tag}: the advice line states what it did: the gold it spends is the gold it fills", adv.Contains($"{spent} of your {gold} gold") || (spent == 0 && adv.Contains("nothing to buy")));
        }
        await Tap(pageU, "#dc-confirm");
        // the council
        await PassAsk(pageU, 40000);
        await Until(() => SU.Stage == DoctrineStage.Doctrine && Has(pageU, ".dc-editor"), 40000, tag + " doctrine");
        Scan(pageU);
        stagesSeen.Add(pageU.Find(".dc-hud").GetAttribute("data-stage")!);
        {
            var legal = SU.Army!.Legal;
            var chips = pageU.FindAll(".dc-chip").Select(c => int.Parse(c.GetAttribute("data-token")!)).ToList();
            var exp = ExpectedChips(step);
            var poolHere = exp.Where(legal.Allows).ToList();
            Check($"{tag}: the council offers only orders step {step} has opened ({exp.Count} in the pool), as many as the pool and the offer size allow, none twice",
                chips.Count > 0 && chips.All(t => exp.Contains(t)) && chips.Distinct().Count() == chips.Count && chips.Count == Math.Min(OfferRules.Default.OrderOfferSize, poolHere.Count), $"{chips.Count} cards of a pool of {poolHere.Count}; outside the pool: {string.Join(",", chips.Where(t => !exp.Contains(t)).Take(5))}");
            Check($"{tag}: the cards on the page are the session's offer, and the offer is the game's own for this seed, round and legal list under the ladder's pool",
                chips.SequenceEqual(SU.OrdersOffered!.Tokens) && chips.SequenceEqual(SeededOffers.Instance.Orders(3, r, 0, legal, DoctrineSession.GameRules(5, rr => DoctrineUnlocks.StepForRound(rr, 5)).Offers).Tokens));
            bool lawful = pageU.FindAll(".dc-chip").All(c => !c.HasAttribute("disabled") && legal.Allows(int.Parse(c.GetAttribute("data-token")!)));
            Check($"{tag}: every card is an order the army allows now (the pool never makes an illegal order legal)", lawful);
            Check($"{tag}: the dock shows {slotsOpen[r]} slots in the doctrine step", pageU.FindAll(".dc-dock .dc-slot").Count == slotsOpen[r]);
            if (r == 0)
            {
                Shot("u1-doctrine", pageU);
                Check("field (council on the table): the three lines are still in the field's markup and the formations picker shows them", Has(pageU, ".dc-tray") && pageU.FindAll(".dc-field .dc-lines .dc-lane").Count == 3 && pageU.FindAll(".dc-field .dc-disc").Count == SU.Army!.Cohorts.Count && pageU.FindAll(".dc-form").Count == 3);
                var fieldChips = pageU.FindAll(".dc-chip").Where(c => DoctrineVocabulary.Decode(int.Parse(c.GetAttribute("data-token")!)).Family == OrderFamily.Field).ToList();
                Check("orders (round 1): only orders for a line are on the table (Press on, Hold the line), each with its plain effect under its label", fieldChips.Count == chips.Count && fieldChips.Count >= 3 && fieldChips.All(c => c.QuerySelector(".fx")!.TextContent.Trim() == DoctrineWords.FieldSentences[DoctrineVocabulary.Decode(int.Parse(c.GetAttribute("data-token")!)).Kind] && c.QuerySelector(".lb")!.TextContent.Trim() == DoctrineWords.Label(int.Parse(c.GetAttribute("data-token")!)))
                    && fieldChips.All(c => DoctrineVocabulary.Decode(int.Parse(c.GetAttribute("data-token")!)).Kind is (int)FieldOrderKind.Advance or (int)FieldOrderKind.Hold), $"{fieldChips.Count} cards");
            }
            // more orders than slots are refused: fill every slot, then try one more
            var openLegal = chips.Take(slotsOpen[r] + 1).ToList();
            if (openLegal.Count > slotsOpen[r])
            {
                foreach (var t in openLegal) await TapChip(pageU, t);
                Check($"{tag}: a doctrine never holds more orders than the slots that are open", pageU.FindAll(".dc-dock .dc-slot:not(.empty)").Count == slotsOpen[r] && !pageU.Find($"[data-token='{openLegal[^1]}']").ClassList.Contains("on"), $"{pageU.FindAll(".dc-dock .dc-slot:not(.empty)").Count} filled");
                await Tap(pageU, "#dc-clear");
                Check($"{tag}: Clear empties the slots", pageU.FindAll(".dc-dock .dc-slot.empty").Count == slotsOpen[r]);
            }
        }
        // starter advice, doctrine
        Check($"{tag}: the council's advice button is the same 'Use starter advice'", Text(pageU, ".dc-tray #dc-advice-btn") == "Use starter advice");
        await Tap(pageU, ".dc-tray #dc-advice-btn");
        {
            string adv = Text(pageU, ".dc-advice");
            var legal = SU.Army!.Legal;
            var tableU = SU.OrdersOffered!.Tokens;
            int[] want = [DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), DoctrineVocabulary.Field(FieldOrderKind.Hold, 2), DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Army(ArmyOrderKind.Age)];
            var rest = tableU.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field && !want.Contains(t));
            var expect = want.Concat(rest).Where(t => tableU.Contains(t) && ExpectedChips(step).Contains(t) && legal.Allows(t)).Take(slotsOpen[r]).Select(DoctrineWords.Label).ToList();
            var filled = pageU.FindAll(".dc-dock .dc-slot:not(.empty) .t").Select(e => e.TextContent.Trim()).ToList();
            Check($"{tag}: starter advice is labelled as the game's advice, never as the general, and fills only open, legal orders", adv.StartsWith("Game advice:") && GeneralFree(adv) == "" && filled.SequenceEqual(expect) && expect.Count >= 1, $"{adv} | {string.Join(",", filled)} vs {string.Join(",", expect)}");
            if (r == 0) Shot("u1-doctrine-advice", pageU);
        }
        await Tap(pageU, "#dc-fight");
        await Until(() => (SU.Stage == DoctrineStage.Watch || SU.Stage == DoctrineStage.Over) && Has(pageU, ".dc-recap"), 60000, tag + " result");
        Scan(pageU);
        stagesSeen.Add(pageU.Find(".dc-hud").GetAttribute("data-stage")!);
        {
            var rec = SU.Records[^1];
            int lost = rec.Result.AttackerLosses.Sum(l => l.Lost), theirLost = rec.Result.DefenderLosses.Sum(l => l.Lost);
            var what = pageU.FindAll(".dc-recap-what").Select(e => e.TextContent.Trim()).ToList();
            var tries = pageU.FindAll(".dc-recap-try").Select(e => e.TextContent.Trim()).ToList();
            string arena = SU.ArenaName(rec.ArenaId);
            Check($"{tag}: the recap has two lines on what happened, with the arena and the ticks from the record", what.Count == 2 && what[0].Contains(arena) && what[0].Contains($"{rec.Result.Ticks} tick"), string.Join(" | ", what));
            Check($"{tag}: the recap's numbers are the record's own (soldiers lost, brought)", what.Count == 2 && what[1] == $"You lost {lost} of {SU.Last!.YourSoldiers} soldiers; the enemy lost {theirLost} of {SU.Last.EnemySoldiers}.", what.Count > 1 ? what[1] : "");
            bool tryOk = tries.Count <= 1 && (tries.Count == 0 || tries[0].StartsWith("Try next: "));
            if (tries.Count == 1)
            {
                var t0 = tries[0];
                if (Regex.Match(t0, @"skipped (\d+) of your orders") is { Success: true } m1) tryOk &= rec.Orders is { } o1 && o1.Skipped == int.Parse(m1.Groups[1].Value);
                if (Regex.Match(t0, @"lost the most \((\d+)\)") is { Success: true } m2) tryOk &= rec.Result.AttackerLosses.Max(l => l.Lost) == int.Parse(m2.Groups[1].Value);
                if (Regex.Match(t0, @"you have (\d+) gold") is { Success: true } m3) tryOk &= SU.Last!.ArmyAfter.Gold == int.Parse(m3.Groups[1].Value);
                if (t0.Contains("no orders")) tryOk &= rec.PlayerDoctrine.IsEmpty;
            }
            Check($"{tag}: at most one thing to try next, and what it says is true of the record", tryOk && !(rec.Final && tries.Count > 0), string.Join(" | ", tries));
            Check($"{tag}: the recap never mentions the general or a timer", GeneralFree(string.Join(" ", what.Concat(tries))) == "");
            if (r == 0) Shot("u1-watch", pageU);
        }
        if (r == 2) Check("unlock: after round 3 the progress holds step 2 until the last orders", PU.Step == 2);
        await Tap(pageU, "#dc-next");
    }
    Check("HUD: draft, doctrine, watch (and the end of a round) were all states of the same scene", stagesSeen.IsSupersetOf(["draft", "doctrine", "watch"]), string.Join(",", stagesSeen));
    await Until(() => Has(pageU, ".dc-end"), 20000, "U end");
    Check("unlock: the page remembers the last step after the game", PU.Step == DoctrineUnlocks.LastStep);
    await Tap(pageU, "#dc-fresh");
    await Tap(pageU, ".dc-opt", 0);
    await Tap(pageU, "#dc-new");
    await Until(() => SU.Stage == DoctrineStage.Draft && Has(pageU, ".dc-draft"), 30000, "U2 draft");
    Check("unlock: a restarted game keeps what was unlocked (the whole pool, 4 slots, no announcement in round 1)", SU.Draft!.Offer.Packs.SequenceEqual(SeededOffers.Instance.Units(SU.Seed, 0, SU.Army!.Gold, DoctrineSession.GameRules(3, _ => DoctrineUnlocks.LastStep).Offers).Packs)
        && pageU.FindAll(".dc-dock .dc-slot").Count == 4 && !Has(pageU, ".dc-unlock") && !Has(pageU, "#dc-showall"));
    await Tap(pageU, "#dc-leave");
    await Until(() => Has(pageU, ".dc-end"), 20000, "U2 end");
}

// ---------------------------------------------------------------------------------------------- the offers: the reroll (cost, limit, the purse), the picks, the slot order, the three formations, the general's pick of the council
int rot1 = DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 1), rot2 = DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 2);
// Seeds found headless, on the same offer source and with the script's own draft: one whose round-0 council holds BOTH turns of Reform the ranks, one that holds neither.
int rotSeed = -1, noRotSeed = -1;
for (int sd = 1; sd < 4000 && (rotSeed < 0 || noRotSeed < 0); sd++)
{
    var svcS = new global::Doctrine.Army.PleromaArmyService();
    var armyS = svcS.Create(Side.Player, sd);
    int gS = armyS.Read().Gold;
    await armyS.DraftAsync(new DraftRequest(0, sd, ScriptHands.PickList(SeededOffers.Instance.Units(sd, 0, gS, poolRulesA), gS)), CancellationToken.None);
    var tk = SeededOffers.Instance.Orders(sd, 0, 0, armyS.Read().Legal, poolRulesA).Tokens;
    if (rotSeed < 0 && tk.Contains(rot1) && tk.Contains(rot2) && tk.Count(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field) >= 3) rotSeed = sd;
    if (noRotSeed < 0 && !tk.Contains(rot1) && !tk.Contains(rot2)) noRotSeed = sd;
}
Check("setup: a seed was found whose round-0 council holds both turns of Reform the ranks, and one that holds neither", rotSeed > 0 && noRotSeed > 0, $"seeds {rotSeed} and {noRotSeed}");

// ---- game R1: the reroll costs gold, draws again from its own stream, stops at the limit, and is not asked when the purse cannot pay
{
    using var ctxR = NewCtx(false);
    var pageR = ctxR.Render<DoctrinePage>();
    livePages.Add(pageR);
    DoctrineSession SR = pageR.Instance.Session!;
    await pageR.InvokeAsync(() => pageR.Find("#dc-seed").Input("21"));
    await Tap(pageR, "#dc-showall");
    await Tap(pageR, ".dc-opt", 0);
    await Tap(pageR, "#dc-new");
    await DoDraft(pageR, 0, pass: false);
    bool ask1 = await Until(() => SR.Reroll is not null && Has(pageR, "#dc-reroll"), 20000, "R ask 1");
    var rulesR = poolRulesA;
    int gold1 = SR.Army!.Gold; var offA = SR.OrdersOffered!;
    Check("reroll: the first question shows the price and the rerolls left, the offer's own (the game's rules: one gold, two rerolls)", ask1 && SR.Reroll!.Offer.RerollCost == rulesR.RerollCost && SR.Reroll.Offer.RerollsLeft == rulesR.MaxRerolls && Text(pageR, "#dc-reroll") == $"Reroll for {rulesR.RerollCost} gold ({rulesR.MaxRerolls} left)");
    await Tap(pageR, "#dc-reroll");
    bool ask2 = await Until(() => SR.Reroll is not null && SR.Reroll.Offer.Rerolls == 1 && Has(pageR, "#dc-reroll") && Text(pageR, "#dc-reroll").Contains("(1 left)"), 20000, "R ask 2");
    var offB = SR.OrdersOffered!;
    Check("reroll: paying draws the offer again: the purse is down by exactly the price, the council is the game's own for reroll 1 on the legal list the payment left, and the cards show it",
        ask2 && SR.Army!.Gold == gold1 - rulesR.RerollCost && offB.Rerolls == 1 && offB.RerollsLeft == rulesR.MaxRerolls - 1
        && offB.Tokens.SequenceEqual(SeededOffers.Instance.Orders(21, 0, 1, SR.Army.Legal, rulesR).Tokens) && pageR.FindAll(".dc-chip").Select(c => int.Parse(c.GetAttribute("data-token")!)).SequenceEqual(offB.Tokens),
        $"gold {gold1} -> {SR.Army?.Gold}");
    Check("reroll: the new council is a different draw (its own stream), not the old one again", !offB.Tokens.SequenceEqual(offA.Tokens));
    Check("reroll: the status line shows the gold the fee left", Text(pageR, ".dc-status").Contains($"Gold {SR.Army!.Gold}"));
    await Tap(pageR, "#dc-reroll");
    bool through = await Until(() => Has(pageR, ".dc-editor"), 20000, "R choose");
    var offC = SR.OrdersOffered!;
    Check("reroll: the limit holds: after two rerolls there is no third question, the council is chosen from, and the purse is down by two prices",
        through && !Has(pageR, "#dc-reroll") && !Has(pageR, "#dc-keep") && offC.Rerolls == rulesR.MaxRerolls && offC.RerollsLeft == 0 && SR.Army!.Gold == gold1 - 2 * rulesR.RerollCost
        && offC.Tokens.SequenceEqual(SeededOffers.Instance.Orders(21, 0, 2, SR.Army.Legal, rulesR).Tokens), $"rerolls {offC.Rerolls}, gold {SR.Army?.Gold}");
    await Tap(pageR, "#dc-fight");
    await Until(() => Has(pageR, ".dc-result") || SR.Stage == DoctrineStage.Over, 60000, "R result");
    Check("reroll: the record counts the rerolls and the watch screen says so; nothing was refused", SR.Records.Count == 1 && SR.Records[0].Rerolls == 2 && SR.Records[0].OfferRefused == 0 && Has(pageR, ".dc-rerolled") && Text(pageR, ".dc-rerolled") == "You paid for 2 rerolls of the council." && !Has(pageR, ".dc-offerrefused"), Has(pageR, ".dc-rerolled") ? Text(pageR, ".dc-rerolled") : "missing");
    await Tap(pageR, "#dc-leave");
    await Until(() => Has(pageR, ".dc-end"), 20000, "R end");
}
// ---- game P: a purse that cannot pay is never asked: no gold, then gold for exactly one reroll
foreach (var (code, rerollsPaid) in new[] { ("DA2.0.0.0.0..0:5:0:0", 0), ("DA2.0.0.1.0..0:5:0:0", 1) })
{
    using var ctxP2 = NewCtx(false);
    var pageP = ctxP2.Render<DoctrinePage>();
    livePages.Add(pageP);
    DoctrineSession SP = pageP.Instance.Session!;
    await pageP.InvokeAsync(() => pageP.Find("#dc-code").Input(code));
    await Tap(pageP, "#dc-load");
    await Until(() => Has(pageP, ".dc-draft"), 20000, "P draft " + code);
    if (rerollsPaid == 0) Check("muster: a purse of nothing is offered nothing, and the page says so in one plain sentence", SP.Draft!.Offer.Packs.Count == 0 && Has(pageP, ".dc-nopacks") && Text(pageP, ".dc-nopacks") == DoctrineWords.NoCompany && pageP.FindAll(".dc-pack").Count == 0);
    await Tap(pageP, "#dc-confirm");
    if (rerollsPaid == 1)
    {
        bool asked = await Until(() => SP.Reroll is not null && Has(pageP, "#dc-reroll"), 20000, "P ask");
        await Tap(pageP, "#dc-reroll");
        await Until(() => Has(pageP, ".dc-editor"), 20000, "P choose");
        Check("reroll: with gold for exactly one reroll the question comes once; after the payment the purse is empty and the council is chosen from at once", asked && SP.OrdersOffered!.Rerolls == 1 && SP.Army!.Gold == 0 && !Has(pageP, "#dc-reroll") && !Has(pageP, "#dc-keep"));
    }
    else
    {
        bool straight = await Until(() => Has(pageP, ".dc-editor"), 20000, "P choose");
        Check("reroll: a purse that cannot pay the price is never asked: straight to the council, no reroll, no refusal", straight && !Has(pageP, "#dc-reroll") && !Has(pageP, "#dc-keep") && SP.OrdersOffered!.Rerolls == 0 && SP.OrdersOffered.Tokens.Count > 0 && SP.Army!.Gold == 0);
    }
    await Tap(pageP, "#dc-fight");
    await Until(() => Has(pageP, ".dc-result") || SP.Stage == DoctrineStage.Over, 60000, "P result");
    Check($"reroll: the closed round's record agrees ({rerollsPaid} paid, none refused)", SP.Records.Count == 1 && SP.Records[0].Rerolls == rerollsPaid && SP.Records[0].OfferRefused == 0);
    await Tap(pageP, "#dc-leave");
    await Until(() => Has(pageP, ".dc-end"), 20000, "P end");
}

// ---- game R2: up to four orders and no more, the slot order, the three formations against the army's own rotations, the general's pick marked on the cards
{
    using var ctxR2 = NewCtx(false);
    ctxR2.Services.AddSingleton(new DoctrineSectionsSource(() => new DoctrineSections(new OutsideLens(new global::Doctrine.General.TinyHoloGeneral()))));
    var pageR2 = ctxR2.Render<DoctrinePage>();
    livePages.Add(pageR2);
    DoctrineSession S2 = pageR2.Instance.Session!;
    await pageR2.InvokeAsync(() => pageR2.Find("#dc-seed").Input(rotSeed.ToString()));
    await Tap(pageR2, "#dc-showall");
    await Tap(pageR2, ".dc-opt", 0);
    await Tap(pageR2, "#dc-new");
    await DoDraft(pageR2, 0);
    await Until(() => Has(pageR2, ".dc-editor"), 20000, "R2 council");
    var offer2 = S2.OrdersOffered!;
    Check("setup: the seed's council holds both turns of Reform the ranks, as found", offer2.Tokens.Contains(rot1) && offer2.Tokens.Contains(rot2) && offer2.Rerolls == 0);
    // the general's pick of the council: marked on the cards it would take
    {
        var gh = S2.Ghost!.Doctrine;
        var marked = pageR2.FindAll(".dc-chip.gen").Select(c => int.Parse(c.GetAttribute("data-token")!)).OrderBy(x => x).ToList();
        Check("general: its pick is read from the council: every token it suggests is on the table (what it offered outside the offer was removed by the game and counted)", gh.Used >= 2 && gh.Tokens.Where(t => t != 0).All(t => offer2.Tokens.Contains(t)) && S2.Army!.Legal.AllowsAll(gh));
        Check("general: the cards it would take are marked 'Your general's pick', exactly those and no other, and each is one of the cards on the table",
            marked.SequenceEqual(gh.Tokens.Where(t => t != 0).OrderBy(x => x)) && marked.All(t => offer2.Tokens.Contains(t)) && pageR2.FindAll(".dc-chip.gen .gb").All(g => g.TextContent.Trim() == "Your general's pick") && marked.Count == pageR2.FindAll(".dc-chip.gen .gb").Count, string.Join(",", marked));
        Check("general: the panel names it 'Your general's pick of the council' with Use it, Edit and Mine, and says the cards are marked", Text(pageR2, ".dc-ghost h3") == "Your general's pick of the council" && Has(pageR2, "#dc-accept") && Has(pageR2, "#dc-edit") && Has(pageR2, "#dc-ignore") && Has(pageR2, ".dc-ghostmark"));
        Shot("r2-council-ghost", pageR2);
        await Tap(pageR2, "#dc-ignore");
        Check("general: Mine clears the marks and the slots (the choice is yours)", pageR2.FindAll(".dc-chip.gen").Count == 0 && pageR2.FindAll(".dc-dock .dc-slot.empty").Count == 4);
    }
    var fieldTokens = offer2.Tokens.Where(t => DoctrineVocabulary.Decode(t).Family == OrderFamily.Field).ToList();
    var plain = offer2.Tokens.Where(t => t != rot1 && t != rot2).ToList();
    // up to four and no more
    await Tap(pageR2, "#dc-clear");
    foreach (var t in plain.Take(5)) await TapChip(pageR2, t);
    Check("picks: up to four orders go in the four slots, in the order tapped, and a fifth is not taken (its card stays unpressed, no slot changes)",
        pageR2.FindAll(".dc-dock .dc-slot:not(.empty) .t").Select(e => e.TextContent.Trim()).SequenceEqual(plain.Take(4).Select(DoctrineWords.Label)) && !pageR2.Find($"[data-token='{plain[4]}']").ClassList.Contains("on"));
    await pageR2.InvokeAsync(() => pageR2.Instance.PlaceOrder(plain[4])); pageR2.Render();
    Check("picks: asking the page to place a fifth does nothing either (the slots are the guard, not the button)", pageR2.FindAll(".dc-dock .dc-slot:not(.empty)").Count == 4 && !pageR2.Find($"[data-token='{plain[4]}']").ClassList.Contains("on"));
    // the slot order
    string[] Slots() => pageR2.FindAll(".dc-dock .dc-slot .t").Select(e => e.TextContent.Trim()).ToArray();
    string[] Lab(params int[] ts) => ts.Select(DoctrineWords.Label).ToArray();
    int a0 = plain[0], a1 = plain[1], a2 = plain[2], a3 = plain[3];
    Check("order: the first slot has no 'earlier' arrow and the last no 'later' arrow", pageR2.Find("li[data-slot='0'] .dc-left").HasAttribute("disabled") && pageR2.Find("li[data-slot='3'] .dc-right").HasAttribute("disabled") && !pageR2.Find("li[data-slot='1'] .dc-left").HasAttribute("disabled"));
    await Tap(pageR2, "li[data-slot='0'] .dc-right");
    Check("order: the 'later' arrow of slot 1 swaps it with slot 2", Slots().SequenceEqual(Lab(a1, a0, a2, a3)), string.Join(" | ", Slots()));
    await Tap(pageR2, "li[data-slot='3'] .dc-left");
    Check("order: the 'earlier' arrow of slot 4 swaps it with slot 3", Slots().SequenceEqual(Lab(a1, a0, a3, a2)), string.Join(" | ", Slots()));
    await pageR2.InvokeAsync(async () => { await pageR2.Find("li[data-slot='0']").TriggerEventAsync("ondragstart", new DragEventArgs()); await pageR2.Find("li[data-slot='3']").TriggerEventAsync("ondrop", new DragEventArgs()); });
    Check("order: dragging slot 1 onto slot 4 moves it there and the slots between shift up one", Slots().SequenceEqual(Lab(a0, a3, a2, a1)), string.Join(" | ", Slots()));
    await pageR2.InvokeAsync(() => pageR2.Instance.MoveSlot(0, 9)); pageR2.Render();
    Check("order: a move to a slot that does not exist changes nothing", Slots().SequenceEqual(Lab(a0, a3, a2, a1)));
    Check("order: the words under the slots follow the slot order", pageR2.FindAll(".dc-says li").Select(e => e.TextContent.Trim()).SequenceEqual(new[] { a0, a3, a2, a1 }.Select(DoctrineWords.Sentence)));
    // the three formations
    await Tap(pageR2, "#dc-clear");
    var forms = pageR2.FindAll(".dc-form");
    var svcF = new global::Doctrine.Army.PleromaArmyService();
    var armyF = svcF.Create(Side.Player, rotSeed);
    int gF = armyF.Read().Gold;
    await armyF.DraftAsync(new DraftRequest(0, rotSeed, ScriptHands.PickList(SeededOffers.Instance.Units(rotSeed, 0, gF, poolRulesA), gF)), CancellationToken.None);
    var hostF = armyF.Read();
    Check("formations: the independent army (same seed, same draft) is the page's army", hostF.Digest() == S2.Army!.Digest());
    string Say(IEnumerable<(int Key, int Count)> items)
    {
        var l = items.OrderBy(i => i.Key).Select(i => $"{i.Count} {Roster.NameOf(i.Key)}").ToList();
        return l.Count == 0 ? "nobody" : l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1];
    }
    bool armyRotates = true; var formWrong = new List<string>();
    for (int turns = 0; turns < 3; turns++)
    {
        // by hand: a turn moves every cohort one place along the line; and the army's own order agrees
        var byHand = hostF.Cohorts.Where(c => c.Count > 0).Select(c => (c.Key, c.Count, Slot: (c.Slot + turns) % 3)).ToList();
        svcF.TryParse(armyF.ToCode(), out var cl, out _);
        if (turns > 0) await cl!.ApplyArmyOrdersAsync(DoctrineSet.Of(DoctrineVocabulary.Army(ArmyOrderKind.Rotate, turns)), CancellationToken.None);
        var real = cl!.Read().Cohorts.Where(c => c.Count > 0).Select(c => (c.Key, c.Count, c.Slot)).ToList();
        armyRotates &= real.OrderBy(x => x.Key).SequenceEqual(byHand.OrderBy(x => x.Key));
        var card = forms.FirstOrDefault(f => f.GetAttribute("data-turns") == turns.ToString());
        if (card is null) { formWrong.Add($"no card for {turns} turns"); continue; }
        for (int slot = 0; slot < 3; slot++)
        {
            string want = Say(byHand.Where(c => c.Slot == slot).Select(c => (c.Key, c.Count)));
            string got = card.QuerySelector($".fl[data-slot='{slot}'] .who")?.TextContent.Trim() ?? "(missing)";
            if (got != want) formWrong.Add($"{turns} turns, {lineHandP[slot]} line: page '{got}', army '{want}'");
        }
    }
    Check("formations: the army's own Reform the ranks moves every cohort one place along the line per turn (by hand), so the three formations are the real ones", armyRotates);
    Check("formations: the three formation cards (as they stand, one turn, two turns) show who would stand in the front, middle and back line, equal to the army's real rotations", forms.Count == 3 && formWrong.Count == 0, string.Join(" | ", formWrong.Take(3)));
    Check("formations: the cards are named as they stand and by the order that brings them about (Reform the ranks: 1 place / 2 places)", forms.Select(f => f.QuerySelector(".ft")!.TextContent.Trim()).SequenceEqual(["As they stand", DoctrineWords.Label(rot1), DoctrineWords.Label(rot2)]));
    Check("formations: with no order for any line, every line says it presses on", forms.All(f => f.QuerySelectorAll(".fl .by").All(b => b.TextContent.Trim() == "No order for this line: it presses on.")));
    // which order each line's order would reach
    var fo = fieldTokens.First(); var foo = DoctrineVocabulary.Decode(fo);
    await TapChip(pageR2, fo);
    {
        var all = pageR2.FindAll(".dc-form");
        bool reach = true;
        foreach (var f in all)
            for (int slot = 0; slot < 3; slot++)
                reach &= f.QuerySelector($".fl[data-slot='{slot}'] .by")!.TextContent.Trim() == (slot == foo.A ? $"Your order reaches them: {ChronicleCheck.HandNames[foo.Kind]}." : "No order for this line: it presses on.");
        Check("formations: an order for a line placed in a slot shows in every formation on that line only ('Your order reaches them: ...'), the other lines still press on", reach, DoctrineWords.Label(fo));
    }
    // choosing a formation puts its order in a slot
    Check("formations: as they stand is the one chosen while no turn of the ranks is in a slot", pageR2.Find(".dc-form[data-turns='0']").GetAttribute("aria-checked") == "true" && pageR2.Find(".dc-form[data-turns='1']").GetAttribute("aria-checked") == "false");
    await Tap(pageR2, ".dc-form[data-turns='1']");
    Check("formations: choosing one turn puts Reform the ranks (1 place) in the first empty slot, and that formation is the one chosen", Slots().SequenceEqual(Lab(fo, rot1, 0, 0)) && pageR2.Find(".dc-form[data-turns='1']").GetAttribute("aria-checked") == "true" && pageR2.Find(".dc-form[data-turns='0']").GetAttribute("aria-checked") == "false", string.Join(" | ", Slots()));
    await Tap(pageR2, ".dc-form[data-turns='2']");
    Check("formations: choosing two turns replaces the one turn already there (one turn of the ranks at a time)", Slots().SequenceEqual(Lab(fo, rot2, 0, 0)) && pageR2.Find(".dc-form[data-turns='2']").GetAttribute("aria-checked") == "true", string.Join(" | ", Slots()));
    await Tap(pageR2, ".dc-form[data-turns='0']");
    Check("formations: choosing as they stand takes the turn out again", Slots().SequenceEqual(Lab(fo, 0, 0, 0)) && pageR2.Find(".dc-form[data-turns='0']").GetAttribute("aria-checked") == "true", string.Join(" | ", Slots()));
    foreach (var t in plain.Where(t => t != fo).Take(3)) await TapChip(pageR2, t);
    await Tap(pageR2, ".dc-form[data-turns='1']");
    Check("formations: with every slot full a formation is not put anywhere, and the page says to take an order out", pageR2.FindAll(".dc-dock .dc-slot:not(.empty)").Count == 4 && !Slots().Contains(DoctrineWords.Label(rot1)) && Has(pageR2, ".dc-formnote") && Text(pageR2, ".dc-formnote").StartsWith("Every slot is full."));
    Shot("r2-formations", pageR2);
    // the end to end: the formation previewed is the formation the battle is set up with
    await Tap(pageR2, "#dc-clear");
    await TapChip(pageR2, fo);
    await Tap(pageR2, ".dc-form[data-turns='1']");
    var preview1 = S2.Formations()[1].Host!;
    await Tap(pageR2, "#dc-fight");
    await Until(() => Has(pageR2, ".dc-result") || S2.Stage == DoctrineStage.Over, 60000, "R2 result");
    var rec2 = S2.Records[0];
    var setup2 = S2.Last!.Setup!;
    Check("formations: the doctrine that fought is exactly the slots (the order for the line, then one turn of the ranks), none refused", rec2.PlayerDoctrine == DoctrineSet.Of(fo, rot1) && rec2.OfferRefused == 0 && rec2.Orders is { Applied: 1, Skipped: 0 }, $"{rec2.PlayerDoctrine} {rec2.Orders}");
    Check("formations: the battle was set up with the host in the formation the picker previewed (each cohort in the line the preview gave it)",
        setup2.Attacker.Cohorts.Where(c => c.Count > 0).Select(c => (c.Key, c.Slot)).OrderBy(x => x.Key).SequenceEqual(preview1.Cohorts.Where(c => c.Count > 0).Select(c => (c.Key, c.Slot)).OrderBy(x => x.Key)));
    Check("general: the game removed what its suggestion offered outside the council and counted it (the general is bound to the table)", rec2.IllegalDropped >= 1, rec2.IllegalDropped.ToString());
    await Tap(pageR2, "#dc-leave");
    await Until(() => Has(pageR2, ".dc-end"), 20000, "R2 end");
}
// ---- game A2: Use it and Edit on the general's pick of the council (a general that always has a pick, within the table)
{
    using var ctxA2 = NewCtx(false);
    ctxA2.Services.AddSingleton(new DoctrineSectionsSource(() => new DoctrineSections(new OutsideLens(new global::Doctrine.General.TinyHoloGeneral()))));
    var pageA2 = ctxA2.Render<DoctrinePage>();
    livePages.Add(pageA2);
    DoctrineSession SA = pageA2.Instance.Session!;
    await pageA2.InvokeAsync(() => pageA2.Find("#dc-seed").Input("4"));
    await Tap(pageA2, "#dc-showall");
    await Tap(pageA2, ".dc-opt", 0);
    await Tap(pageA2, "#dc-new");
    await DoDraft(pageA2, 0);
    await Until(() => Has(pageA2, ".dc-editor") && SA.Ghost is not null, 20000, "A2 council");
    var gA = SA.Ghost!.Doctrine;
    Check("accept: Use it is enabled when every order of the general's pick is on the table", gA.Used >= 2 && !pageA2.Find("#dc-accept").HasAttribute("disabled") && gA.Tokens.All(SA.OrdersOffered!.Allows));
    await Tap(pageA2, "#dc-accept");
    await Until(() => Has(pageA2, ".dc-result") || SA.Stage == DoctrineStage.Over, 60000, "A2 result");
    Check("accept: Use it fights the general's pick as it stands, recorded as NOT overridden, with the general's own match record; both the doctrine and the result teach it",
        SA.Records.Count == 1 && SA.Records[0].PlayerDoctrine == gA && !SA.Records[0].Overrode && SA.Records[0].GhostConfidence == 640 && SA.Sections.General.ExamplesSeen == 2);
    Check("accept: the spoils screen says the pick was taken as it stood", Text(pageA2, ".dc-spoils").Contains("You took it as it stood."));
    await Tap(pageA2, "#dc-next");
    await DoDraft(pageA2, 1);
    await Until(() => Has(pageA2, ".dc-editor") && SA.Ghost is not null, 30000, "A2 council 2");
    var gB = SA.Ghost!.Doctrine;
    var gTokens = gB.Tokens.Where(t => t != 0).ToList();
    int repl = SA.OrdersOffered!.Tokens.First(t => !gTokens.Contains(t) && SA.Army!.Legal.Allows(t));
    await Tap(pageA2, "#dc-edit");
    Check("edit: Edit loads the pick into the slots, in its order, and hides the pick's panel", pageA2.FindAll(".dc-dock .dc-slot:not(.empty) .t").Select(e => e.TextContent.Trim()).SequenceEqual(gTokens.Select(DoctrineWords.Label)) && !Has(pageA2, "#dc-accept"));
    await Tap(pageA2, ".dc-slot", gTokens.Count - 1);
    await TapChip(pageA2, repl);
    await Tap(pageA2, "#dc-fight");
    await Until(() => Has(pageA2, ".dc-result") || SA.Stage == DoctrineStage.Over, 60000, "A2 result 2");
    var recA = SA.Records[1];
    Check("edit: changing one order of the pick fights the changed doctrine and records an override against the general's pick", recA.Overrode && recA.Ghost == gB && recA.PlayerDoctrine == DoctrineSet.Of([.. gTokens.Take(gTokens.Count - 1), repl]) && recA.OfferRefused == 0, $"{recA.PlayerDoctrine} vs {gB}");
    await Tap(pageA2, "#dc-leave");
    await Until(() => Has(pageA2, ".dc-end"), 20000, "A2 end");
}
// ---- game F: a council with no turn of the ranks: the formations that need it are shown, greyed, and cannot be chosen
{
    using var ctxF = NewCtx(false);
    var pageF = ctxF.Render<DoctrinePage>();
    livePages.Add(pageF);
    DoctrineSession SF = pageF.Instance.Session!;
    await pageF.InvokeAsync(() => pageF.Find("#dc-seed").Input(noRotSeed.ToString()));
    await Tap(pageF, "#dc-showall");
    await Tap(pageF, ".dc-opt", 0);
    await Tap(pageF, "#dc-new");
    await DoDraft(pageF, 0);
    await Until(() => Has(pageF, ".dc-editor"), 20000, "F council");
    var tokF = SF.OrdersOffered!.Tokens;
    Check("formations: when the council holds no turn of the ranks the cards for one and two turns are greyed and say so; as they stand is still there",
        !tokF.Contains(rot1) && !tokF.Contains(rot2) && pageF.FindAll(".dc-form").Count == 3
        && pageF.Find(".dc-form[data-turns='1']").HasAttribute("disabled") && pageF.Find(".dc-form[data-turns='2']").HasAttribute("disabled") && !pageF.Find(".dc-form[data-turns='0']").HasAttribute("disabled")
        && pageF.Find(".dc-form[data-turns='1'] .fn").TextContent.Trim() == "Not on the council this round.");
    await Tap(pageF, ".dc-form[data-turns='1']");
    await pageF.InvokeAsync(() => pageF.Instance.PickFormation(2)); pageF.Render();
    Check("formations: a turn of the ranks that is not on the table cannot be put in a slot, by the card or by the page", pageF.FindAll(".dc-dock .dc-slot.empty").Count == 4);
    await Tap(pageF, "#dc-leave");
    await Until(() => Has(pageF, ".dc-end"), 20000, "F end");
}

// ---------------------------------------------------------------------------------------------- the chronicle (no page: the function against recordings)
{
    Func<int, string> kn = DoctrineWords.KindName;
    string Sig(Chronicle c) => string.Join("\n", c.Lines.Select(l => $"{l.Tick}|{l.Kind}|{l.Text}|{l.Cite}")) + "\n" + string.Join("\n", c.Why.Lines);

    // ---- synthetic frames: every line is written out by hand here, from the frames below, not read back from the function
    // Six cohorts. Yours: 0 Riders (front), 1 Priests (middle), 2 Archers (back). Theirs: 3 Spearmen (front), 4 Wights (middle), 5 Archers (back). Positions are arena units.
    const int Qm = BattleReplay.Quantum;
    var kinds = new[] { (Side.Player, 2, 0), (Side.Player, 4, 1), (Side.Player, 3, 2), (Side.Enemy, 0, 0), (Side.Enemy, 5, 1), (Side.Enemy, 3, 2) };
    var startAt = new[] { (10, 10), (6, 20), (2, 30), (30, 10), (34, 20), (38, 30) };
    // strength after each tick 0..14, per cohort
    int[][] str =
    [
        [10, 10, 10, 10, 10, 9, 8, 8, 8, 8, 8, 8, 8, 8, 8],   // 0 Riders: hit at ticks 5 and 6
        [8, 8, 8, 8, 8, 8, 8, 8, 7, 7, 7, 7, 7, 7, 7],        // 1 Priests: hit at tick 8
        [6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 0, 0],        // 2 Archers: broken at tick 13
        [10, 10, 10, 10, 8, 5, 2, 0, 0, 0, 0, 0, 0, 0, 0],    // 3 Spearmen: struck at ticks 4, 5, 6, 7
        [8, 8, 8, 8, 8, 8, 8, 8, 2, 2, 2, 2, 0, 0, 0],        // 4 Wights: struck at 8, broken at 12
        [6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 4, 0],        // 5 Archers: struck at 13, broken at 14
    ];
    var evAt = new Dictionary<int, (int S, int T, int D)[]>
    {
        [4] = [(0, 3, 2)], [5] = [(0, 3, 3), (3, 0, 1)], [6] = [(0, 3, 3), (3, 0, 1)], [7] = [(0, 3, 2)],
        [8] = [(1, 4, 6), (4, 1, 1)], [12] = [(1, 4, 2)], [13] = [(0, 5, 2), (5, 2, 6)], [14] = [(0, 5, 4)],
    };
    ReplayFrame SynFrame(int t, Func<int, int, (int X, int Y)>? place = null, int[][]? s = null, Dictionary<int, (int S, int T, int D)[]>? ev = null, int flags = 0)
    {
        s ??= str; ev ??= evAt;
        var blobs = new List<ReplayBlob>();
        for (int i = 0; i < 6; i++)
        {
            var (x, y) = place?.Invoke(t, i) ?? startAt[i];
            blobs.Add(new ReplayBlob(i, kinds[i].Item1, kinds[i].Item2, kinds[i].Item3, x * Qm, y * Qm, s[i][t]));
        }
        return new ReplayFrame(t, blobs, ev.TryGetValue(t, out var e) ? e.Select(x => new ReplayEvent(x.S, x.T, x.D)).ToList() : [], flags);
    }
    (int, int) Place(int t, int i) => i == 1 && t >= 8 ? (28, 31) : startAt[i];     // the Priests cross 11 units of the field, to stand beside the Wights, at tick 8
    var synFrames = Enumerable.Range(0, 15).Select(t => SynFrame(t, Place, flags: t >= 6 ? WorldFlag.GateFallen : 0)).ToList();
    var synResult = new BattleResult(BattleStatus.AttackerWon, 14, [new CohortLoss(2, 0, 2), new CohortLoss(4, 1, 1), new CohortLoss(3, 2, 6)], [new CohortLoss(0, 0, 10), new CohortLoss(5, 1, 8), new CohortLoss(3, 2, 6)]);
    var syn = new BattleReplay(2, "test-ground", 1, 120, synFrames, synResult);
    var synDoctrine = DoctrineSet.Of(DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Field(FieldOrderKind.Hold, 2));
    Func<Side, int, Trait?> roster = (s, k) => Roster.TraitsOf(k);
    var cs = BattleChronicle.Build(syn, synDoctrine, "Test Ground", kn, roster);
    var want = new (int Tick, ChronicleKind Kind, string Text)[]
    {
        (0, ChronicleKind.Start, "Test Ground. 24 of your soldiers face the enemy's 24."),
        (4, ChronicleKind.Contact, "First contact. Your Riders strike the enemy Spearmen, and the enemy has not struck yet."),
        (6, ChronicleKind.Gate, "The gate has fallen."),
        (6, ChronicleKind.DugIn, "Your Priests are dug in: six ticks unhit and in place. They strike 12% harder until they are hit or move."),
        (6, ChronicleKind.DugIn, "The enemy Wights are dug in: six ticks unhit and in place. They strike 32% harder until they are hit or move."),
        (7, ChronicleKind.Run, "The enemy Spearmen have been struck 4 ticks running, 10 in all."),
        (7, ChronicleKind.Break, "The enemy Spearmen break. The first to fall. The enemy Front line is gone, the first line to fall."),
        (8, ChronicleKind.Flank, "Your Priests strike the enemy Wights from the side, 11 units across the field from where they began."),
        (8, ChronicleKind.Holy, "Your Priests strike the enemy Wights, and holy blows hurt the cursed half again as hard."),
        (8, ChronicleKind.Blow, "The heaviest blow of the battle: your Priests strike the enemy Wights for 6."),
        (9, ChronicleKind.Lull, "A lull: no blow lands from tick 9 to tick 11."),
        (12, ChronicleKind.Break, "The enemy Wights break. The enemy Middle line is gone."),
        (12, ChronicleKind.Stand, "Only the enemy Archers still stand on their side, 6 strong."),
        (13, ChronicleKind.Blow, "A heavy blow: the enemy Archers strike your Archers for 6."),
        (13, ChronicleKind.Break, "Your Archers break. Your Back line is gone."),
        (14, ChronicleKind.Break, "The enemy Archers break. The enemy Back line is gone."),
        (14, ChronicleKind.End, "The field is yours. 15 of your soldiers stand; the enemy has 0."),
    };
    Check("chronicle (synthetic frames): the log is exactly the hand-written account: first contact, the gate, the first cohort of each side to dig in (the Priests and the Wights: unhit and in place from tick 0 to 6), a run, the first break and the first line gone, a flank, holy against cursed, the heaviest blow, a lull, the final stand, the end",
        cs.Lines.Select(l => (l.Tick, l.Kind, l.Text)).SequenceEqual(want), string.Join(" | ", cs.Lines.Select(l => $"{l.Tick} {l.Kind} {l.Text}").Except(want.Select(w => $"{w.Tick} {w.Kind} {w.Text}")).Take(3)));
    Check("chronicle (synthetic frames): a line reads as the Count says it ('Tick 12. ...')", cs.Lines.Any(l => l.Full == "Tick 4. First contact. Your Riders strike the enemy Spearmen, and the enemy has not struck yet."));
    Check("chronicle (synthetic frames): the Why is hand-written too: the turning point (largest swing in soldiers), the first line to fall, and one hint tied to the Flank order given",
        cs.Why.Lines.SequenceEqual(["Turning point: tick 8, when the enemy lost 6 soldiers and you lost 1.", "The first line to fall: the enemy Front line, at tick 7.", "Your order to ride the flank on the Middle line reached the enemy: your Priests struck from the side at tick 8."])
        && cs.Why is { TurnTick: 8, TurnYouLost: 1, TurnEnemyLost: 6, FirstBreakTick: 7, FirstBreakBlob: 3, FirstLineTick: 7, FirstLineSide: Side.Enemy, FirstLineSlot: 0 }, string.Join(" | ", cs.Why.Lines));
    var problems = ChronicleCheck.Verify(cs, syn, kn, roster);
    Check("chronicle (synthetic frames): the independent checker finds every line backed by a real frame event, break, flag, run, span or stand", problems.Count == 0, string.Join(" | ", problems.Take(3)));
    var whyProblems = ChronicleCheck.VerifyWhy(cs, syn, synDoctrine, kn);
    Check("chronicle (synthetic frames): the independent checker finds the Why numbers equal to the record", whyProblems.Count == 0, string.Join(" | ", whyProblems.Take(3)));
    Check("chronicle (synthetic frames): no holy line without the cohorts' own traits (a flip may have changed them), and none when the traits say no",
        !BattleChronicle.Build(syn, synDoctrine, "Test Ground", kn, null).Lines.Any(l => l.Kind == ChronicleKind.Holy)
        && !BattleChronicle.Build(syn, synDoctrine, "Test Ground", kn, (s, k) => Trait.None).Lines.Any(l => l.Kind == ChronicleKind.Holy));
    Check("chronicle (synthetic frames): the holy factor in the line is the battle's own hit rule (a holy hit on a cursed target is 3/2 of a plain one)",
        UnitRules.Hit(600_000, Trait.Holy, 100, Trait.Cursed, false) * 2 == UnitRules.Hit(600_000, Trait.None, 100, Trait.Cursed, false) * 3 && BattleChronicle.HolyWords() == "half again as hard");
    Check("chronicle (determinism): the same recording, doctrine and names give the same log twice, and the same after the recording is encoded and decoded", Sig(cs) == Sig(BattleChronicle.Build(syn, synDoctrine, "Test Ground", kn, roster))
        && ReplayCodec.TryDecode(ReplayCodec.Encode(syn), out var syn2, out _) && Sig(cs) == Sig(BattleChronicle.Build(syn2!, synDoctrine, "Test Ground", kn, roster)));

    // variants of the opening: the enemy strikes first; both strike at once; no strike at all
    ReplayFrame Ev(int t, params (int S, int T, int D)[] e) => SynFrame(t, null, ev: new() { [t] = e });
    var enemyFirst = new BattleReplay(2, "x", 1, 120, [Ev(0), Ev(1, (5, 2, 1))], new BattleResult(BattleStatus.Running, 1, [], []));
    var cEnemy = BattleChronicle.Build(enemyFirst, DoctrineSet.Empty, "X", kn);
    Check("chronicle (opening): when only the enemy has struck, it says so and names the enemy's strike", cEnemy.Lines.Any(l => l.Kind == ChronicleKind.Contact && l.Text == "First contact. The enemy Archers strike your Archers, and you have not struck yet."), string.Join(" | ", cEnemy.Lines.Select(l => l.Text)));
    var both = new BattleReplay(2, "x", 1, 120, [Ev(0), Ev(1, (0, 3, 1), (3, 0, 1), (5, 2, 1))], new BattleResult(BattleStatus.Running, 1, [], []));
    var cBoth = BattleChronicle.Build(both, DoctrineSet.Empty, "X", kn);
    Check("chronicle (opening): when both sides strike in the first tick it says both did, in the same tick", cBoth.Lines.Any(l => l.Kind == ChronicleKind.Contact && l.Text == "First contact. Both sides strike in the same tick: your Riders on the enemy Spearmen, the enemy Spearmen on your Riders."));
    var quiet = new BattleReplay(2, "x", 1, 120, [Ev(0), Ev(1), Ev(2)], new BattleResult(BattleStatus.Draw, 2, [], []));
    var cQuiet = BattleChronicle.Build(quiet, DoctrineSet.Empty, "X", kn);
    Check("chronicle (opening): a battle with no strike has no contact, break, blow or lull line, says nobody fell, and the end does not claim anyone fell",
        cQuiet.Lines.Select(l => l.Kind).SequenceEqual([ChronicleKind.Start, ChronicleKind.End]) && cQuiet.Why.Lines.SequenceEqual(["No soldier fell on either side.", "No cohort broke."]) && cQuiet.Lines[^1].Text.StartsWith("Neither side held the field.") && cQuiet.Why.Hint is null,
        string.Join(" | ", cQuiet.Lines.Select(l => l.Text)));
    Check("chronicle (empty recording): no frames, no lines, no error", BattleChronicle.Build(new BattleReplay(2, "x", 1, 120, [], new BattleResult(BattleStatus.Running, 0, [], [])), DoctrineSet.Empty, "X").Lines.Count == 0);

    // ---- mutants of the log: each must be found by the checker
    {
        var sw = cs.Lines.First(l => l.Kind == ChronicleKind.Contact);
        string Swapped(string t) => t.Replace("Your ", "\u0001").Replace("your ", "\u0002").Replace("The enemy ", "Your ").Replace("the enemy ", "your ").Replace("\u0001", "The enemy ").Replace("\u0002", "the enemy ");
        Chronicle With(Func<ChronicleLine, ChronicleLine> f, Func<ChronicleLine, bool> pick) { var first = cs.Lines.First(pick); return cs with { Lines = cs.Lines.Select(l => ReferenceEquals(l, first) ? f(l) : l).ToList() }; }
        var swapSide = With(l => l with { Text = Swapped(l.Text) }, l => l.Kind == ChronicleKind.Contact);
        Check("mutant: a swapped side (your and the enemy exchanged in a line) is found", ChronicleCheck.Verify(swapSide, syn, kn, roster).Count > 0, swapSide.Lines.First(l => l.Kind == ChronicleKind.Contact).Text);
        var swapBreak = With(l => l with { Text = Swapped(l.Text) }, l => l.Kind == ChronicleKind.Break);
        Check("mutant: a swapped side in a break line is found", ChronicleCheck.Verify(swapBreak, syn, kn, roster).Count > 0);
        var wrongTick = With(l => l with { Tick = l.Tick + 1 }, l => l.Kind == ChronicleKind.Flank);
        Check("mutant: a line moved to the wrong tick is found", ChronicleCheck.Verify(wrongTick, syn, kn, roster).Count > 0);
        var wrongTick2 = With(l => l with { Tick = l.Tick + 1, Cite = l.Cite with { Tick = l.Cite.Tick + 1 } }, l => l.Kind == ChronicleKind.Break);
        Check("mutant: a line and its cite both moved one tick (a break that did not happen then) is found", ChronicleCheck.Verify(wrongTick2, syn, kn, roster).Count > 0);
        var fakeBreak = cs with { Lines = cs.Lines.Concat([new ChronicleLine(13, ChronicleKind.Break, "Your Priests break.", new ChronicleCite(CiteKind.Break, 13, 1))]).OrderBy(l => l.Tick).ToList() };
        Check("mutant: a fabricated break (a cohort that stood) is found", ChronicleCheck.Verify(fakeBreak, syn, kn, roster).Count > 0);
        var fakeFlank = cs with { Lines = cs.Lines.Concat([new ChronicleLine(5, ChronicleKind.Flank, "Your Riders strike the enemy Spearmen from the side, 9 units across the field from where they began.", new ChronicleCite(CiteKind.Event, 5, 0, 3, 3))]).OrderBy(l => l.Tick).ToList() };
        Check("mutant: a flank the positions do not show is found", ChronicleCheck.Verify(fakeFlank, syn, kn, roster).Count > 0);
        var fakeBlow = cs with { Lines = cs.Lines.Concat([new ChronicleLine(5, ChronicleKind.Blow, "A heavy blow: your Riders strike the enemy Spearmen for 3.", new ChronicleCite(CiteKind.Event, 5, 0, 3, 3))]).OrderBy(l => l.Tick).ToList() };
        Check("mutant: a heavy blow that was not heavy is found", ChronicleCheck.Verify(fakeBlow, syn, kn, roster).Count > 0);
        var fakeHoly = cs with { Lines = cs.Lines.Concat([new ChronicleLine(5, ChronicleKind.Holy, "Your Riders strike the enemy Spearmen, and holy blows hurt the cursed half again as hard.", new ChronicleCite(CiteKind.Event, 5, 0, 3, 3))]).OrderBy(l => l.Tick).ToList() };
        Check("mutant: a holy line where the traits are not holy against cursed is found", ChronicleCheck.Verify(fakeHoly, syn, kn, roster).Count > 0);
        var fakeGate = cs with { Lines = cs.Lines.Select(l => l.Kind == ChronicleKind.Gate ? l with { Tick = 5, Cite = l.Cite with { Tick = 5 } } : l).OrderBy(l => l.Tick).ToList() };
        Check("mutant: the gate falling a tick early is found", ChronicleCheck.Verify(fakeGate, syn, kn, roster).Count > 0);
        var fakeLull = cs with { Lines = cs.Lines.Select(l => l.Kind == ChronicleKind.Lull ? l with { Text = "A lull: no blow lands from tick 8 to tick 11.", Tick = 8, Cite = new ChronicleCite(CiteKind.Span, 8, 8, 11) } : l).OrderBy(l => l.Tick).ToList() };
        Check("mutant: a lull that covers a tick with a strike is found", ChronicleCheck.Verify(fakeLull, syn, kn, roster).Count > 0);
        var fakeEnd = cs with { Lines = cs.Lines.Select(l => l.Kind == ChronicleKind.End ? l with { Text = "The enemy holds the field. 0 of theirs stand; you have 15." } : l).ToList() };
        Check("mutant: an end that names the wrong winner is found", ChronicleCheck.Verify(fakeEnd, syn, kn, roster).Count > 0);
        var fakeWhy = cs with { Why = cs.Why with { TurnTick = 13 } };
        Check("mutant: a turning point at the wrong tick is found", ChronicleCheck.VerifyWhy(fakeWhy, syn, synDoctrine, kn).Count > 0);
        var fakeWhy2 = cs with { Why = cs.Why with { Lines = cs.Why.Lines.Select(w => w.Replace("lost 6 soldiers", "lost 7 soldiers")).ToList() } };
        Check("mutant: a Why line with the wrong number is found", ChronicleCheck.VerifyWhy(fakeWhy2, syn, synDoctrine, kn).Count > 0);
        var fakeHint = cs with { Why = cs.Why with { Hint = "Your Back line broke first while ordered to hold the line. Try another order for it.", Lines = [.. cs.Why.Lines.Take(2), "Your Back line broke first while ordered to hold the line. Try another order for it."] } };
        Check("mutant: a hint about a line that did not break first is found", ChronicleCheck.VerifyWhy(fakeHint, syn, synDoctrine, kn).Count > 0);
    }

    // ---- recorded battles: real games on the real sections, several seeds, two rounds each, with orders that move cohorts around
    var tally = new Dictionary<ChronicleKind, int>();
    int battles = 0, linesTotal = 0, bad = 0, whyBad = 0, detBad = 0, lossBad = 0;
    var firstProblems = new List<string>();
    var hintsSeen = new List<string>();
    for (int seed = 1; seed <= 8; seed++)
    {
        var tap = new SetupTap();
        var gm = new DoctrineSections().Build(new VarietyHands(seed), new DoctrineDomain { MaxRounds = 2, Offers = OfferRules.Off }, tap);
        var game = await gm.PlayAsync(seed);
        for (int rd = 0; rd < game.Rounds.Count; rd++)
        {
            var rec = game.Rounds[rd];
            var setup = tap.Setups[rd];
            int clk = gm.Context.Battle.Arenas.First(a => a.Id == rec.ArenaId).SiegeClock;
            Func<Side, int, Trait?> traitsOf = (s, k) => (s == Side.Player ? setup.Attacker : setup.Defender).Cohorts.Where(c => c.Key == k).Select(c => (Trait?)c.Traits).FirstOrDefault();
            var c = BattleChronicle.Build(rec.Replay, rec.PlayerDoctrine, "Arena", kn, traitsOf, clk);
            battles++; linesTotal += c.Lines.Count;
            foreach (var l in c.Lines) tally[l.Kind] = tally.GetValueOrDefault(l.Kind) + 1;
            if (c.Why.Hint is not null) hintsSeen.Add(c.Why.Hint);
            var pr = ChronicleCheck.Verify(c, rec.Replay, kn, traitsOf, clk); if (pr.Count > 0) { bad++; firstProblems.AddRange(pr.Take(2).Select(x => $"seed {seed} round {rd}: {x}")); }
            var wp = ChronicleCheck.VerifyWhy(c, rec.Replay, rec.PlayerDoctrine, kn); if (wp.Count > 0) { whyBad++; firstProblems.AddRange(wp.Take(2).Select(x => $"seed {seed} round {rd} why: {x}")); }
            if (Sig(c) != Sig(BattleChronicle.Build(rec.Replay, rec.PlayerDoctrine, "Arena", kn, traitsOf, clk))
                || !ReplayCodec.TryDecode(ReplayCodec.Encode(rec.Replay), out var back, out _) || Sig(c) != Sig(BattleChronicle.Build(back!, rec.PlayerDoctrine, "Arena", kn, traitsOf, clk))) detBad++;
            // the record's own losses equal what the frames show (so a Why built from the frames is built from the record)
            int youDrop = rec.Replay.Frames[0].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength) - rec.Replay.Frames[^1].Blobs.Where(b => b.Side == Side.Player).Sum(b => b.Strength);
            int enDrop = rec.Replay.Frames[0].Blobs.Where(b => b.Side == Side.Enemy).Sum(b => b.Strength) - rec.Replay.Frames[^1].Blobs.Where(b => b.Side == Side.Enemy).Sum(b => b.Strength);
            if (youDrop != rec.Result.AttackerLosses.Sum(x => x.Lost) || enDrop != rec.Result.DefenderLosses.Sum(x => x.Lost)) lossBad++;
        }
    }
    Check($"chronicle (recorded battles): every line of {battles} real battles ({linesTotal} lines) is backed by a real frame event, break, flag, run, span or stand, as the independent checker finds", battles >= 12 && bad == 0, string.Join(" | ", firstProblems.Take(3)));
    Check("chronicle (recorded battles): the Why numbers (turning point, first break, first line gone) equal the record in every battle", whyBad == 0, string.Join(" | ", firstProblems.Where(x => x.Contains("why")).Take(3)));
    Check("chronicle (recorded battles): the frames' totals equal the record's losses, so the Why is the record's own", lossBad == 0, $"{lossBad} differ");
    Check("chronicle (recorded battles): the same recording gives the same log twice and after an encode/decode, in every battle", detBad == 0, $"{detBad} differ");
    Check("chronicle (recorded battles): every battle has an opening, a first contact and an end", tally.GetValueOrDefault(ChronicleKind.Start) == battles && tally.GetValueOrDefault(ChronicleKind.End) == battles && tally.GetValueOrDefault(ChronicleKind.Contact) == battles, string.Join(", ", tally.Select(kv => kv.Key + " " + kv.Value)));
    var handLower = ChronicleCheck.HandNames.Select(x => x.ToLowerInvariant()).ToArray();
    Check("chronicle (order names): the hint uses the new names (a lowercase 'ordered to press on', 'your order to ride the flank'), never an old one (Advance, Hold, Flank, Hunt)",
        cs.Why.Hint is { } sh && sh.Contains("order to ride the flank") && !Regex.IsMatch(string.Join(" ", hintsSeen.Append(sh)), @"\b(Advance|Hold|Flank|Hunt|Guard)\b") && hintsSeen.All(h => handLower.Any(n => h.Contains("ordered to " + n + ".") || h.Contains("order to " + n + " on the "))),
        string.Join(" | ", hintsSeen.Take(3)));
    Console.WriteLine($"  (chronicle over {battles} recorded battles: {string.Join(", ", tally.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value))})");

    // the log of the gate battle is checked in the page block above; here, the mutants against a recorded battle
    {
        var tap = new SetupTap();
        var game = await new DoctrineSections().Build(new VarietyHands(3), new DoctrineDomain { MaxRounds = 1, Offers = OfferRules.Off }, tap).PlayAsync(3);
        var rec = game.Rounds[0]; var setup = tap.Setups[0];
        Func<Side, int, Trait?> traitsOf = (s, k) => (s == Side.Player ? setup.Attacker : setup.Defender).Cohorts.Where(c => c.Key == k).Select(c => (Trait?)c.Traits).FirstOrDefault();
        var c = BattleChronicle.Build(rec.Replay, rec.PlayerDoctrine, "Arena", kn, traitsOf);
        var inv = c.Lines.First(l => l.Cite.Kind == CiteKind.Event);
        string Sw(string t) => t.Replace("Your ", "\u0001").Replace("your ", "\u0002").Replace("The enemy ", "Your ").Replace("the enemy ", "your ").Replace("\u0001", "The enemy ").Replace("\u0002", "the enemy ");
        var m1 = c with { Lines = c.Lines.Select(l => l == inv ? l with { Text = Sw(l.Text) } : l).ToList() };
        var m2 = c with { Lines = c.Lines.Select(l => l == inv ? l with { Tick = l.Tick + 1 } : l).ToList() };
        var standing = rec.Replay.Frames[^1].Blobs.Concat(rec.Replay.Frames[0].Blobs).First(b => rec.Replay.Frames[^1].Blobs.Any(x => x.Id == b.Id && x.Strength > 0));
        var m3 = c with { Lines = c.Lines.Concat([new ChronicleLine(rec.Replay.Frames[^1].Tick, ChronicleKind.Break, "A cohort breaks.", new ChronicleCite(CiteKind.Break, rec.Replay.Frames[^1].Tick, standing.Id))]).ToList() };
        Check("mutant (recorded battle): the checker is clean on the real log and finds a swapped side, a wrong tick and a fabricated break in it",
            ChronicleCheck.Verify(c, rec.Replay, kn, traitsOf).Count == 0 && ChronicleCheck.Verify(m1, rec.Replay, kn, traitsOf).Count > 0 && ChronicleCheck.Verify(m2, rec.Replay, kn, traitsOf).Count > 0 && ChronicleCheck.Verify(m3, rec.Replay, kn, traitsOf).Count > 0);
    }

    // ---- a siege, synthetic: every line written out by hand. Nine frames; nobody strikes until tick 7; the clock bit is set from tick 5.
    {
        int[][] Flat(int n) => [Enumerable.Repeat(10, n).ToArray(), Enumerable.Repeat(8, n).ToArray(), Enumerable.Repeat(6, n).ToArray(), Enumerable.Repeat(10, n).ToArray(), Enumerable.Repeat(8, n).ToArray(), Enumerable.Repeat(6, n).ToArray()];
        int[][] sg = Flat(9);
        sg[0][8] = 9; sg[3][7] = 8; sg[3][8] = 8;                                               // the Spearmen lose two at tick 7, the Riders one at tick 8
        var sgEv = new Dictionary<int, (int S, int T, int D)[]> { [7] = [(0, 3, 2)], [8] = [(3, 0, 1)] };
        BattleReplay Siege(Side? by, Dictionary<int, (int S, int T, int D)[]> ev, int bitFrom = 5, int version = 3, BattleStatus outcome = BattleStatus.AttackerWon)
            => new(version, "test-siege", 1, 120, Enumerable.Range(0, 9).Select(t => SynFrame(t, null, sg, ev, bitFrom >= 0 && t >= bitFrom ? WorldFlag.ClockFell : 0)).ToList(), new BattleResult(outcome, 8, [], []), by);
        const string limitSentence = " The battle stopped at its tick limit, where the field goes to the side with the greater power left, not to the side with more soldiers.";

        var sP = Siege(Side.Player, sgEv);
        var csP = BattleChronicle.Build(sP, DoctrineSet.Empty, "Test Ground", kn, null, 14);
        var wantP = new (int Tick, ChronicleKind Kind, string Text)[]
        {
            (0, ChronicleKind.Start, "Test Ground. 24 of your soldiers face the enemy's 24."),
            (0, ChronicleKind.Siege, "A siege. You besiege, and the siege clock is 14 ticks: if you have traded no blow by then, you lose your stance and every company of yours must advance."),
            (5, ChronicleKind.Clock, "The siege clock has run out. You had traded no blow, so you lose your stance: every company of yours now advances."),
            (7, ChronicleKind.Contact, "First contact. Your Riders strike the enemy Spearmen, and the enemy has not struck yet."),
            (8, ChronicleKind.End, "The field is yours. 23 of your soldiers stand; the enemy has 22." + limitSentence),
        };
        Check("siege (synthetic frames): you besiege on a ground whose clock is 14 ticks and have traded no blow when the bit is set at tick 5: the log is exactly the hand-written account (the siege at the opening, the clock at tick 5, first contact, the end said with the tick-limit rule)",
            csP.Lines.Select(l => (l.Tick, l.Kind, l.Text)).SequenceEqual(wantP), string.Join(" | ", csP.Lines.Select(l => $"{l.Tick} {l.Kind} {l.Text}")));
        Check("siege (synthetic frames): the independent checker finds the siege line and the clock line backed by the recording's header and bit", ChronicleCheck.Verify(csP, sP, kn, null, 14).Count == 0, string.Join(" | ", ChronicleCheck.Verify(csP, sP, kn, null, 14).Take(3)));
        var sE = Siege(Side.Enemy, sgEv);
        var csE = BattleChronicle.Build(sE, DoctrineSet.Empty, "Test Ground", kn, null, 16);
        Check("siege (synthetic frames): when the enemy besieges, the opening and the clock say so, in the enemy's person and with 'You may wait'",
            csE.Lines.Any(l => l.Kind == ChronicleKind.Siege && l.Text == "A siege. The enemy besieges, and the siege clock is 16 ticks: if it has traded no blow by then, it loses its stance and every company of it must advance. You may wait.")
            && csE.Lines.Any(l => l.Kind == ChronicleKind.Clock && l.Tick == 5 && l.Text == "The siege clock has run out. The enemy had traded no blow, so it loses its stance: every company of theirs now advances.")
            && ChronicleCheck.Verify(csE, sE, kn, null, 16).Count == 0);
        var csNoClock = BattleChronicle.Build(sP, DoctrineSet.Empty, "Test Ground", kn, null, 0);
        Check("siege (synthetic frames): when the arena's clock is not known the opening still names the besieger and states no figure", csNoClock.Lines.Any(l => l.Kind == ChronicleKind.Siege && l.Text == "A siege. You besiege.") && ChronicleCheck.Verify(csNoClock, sP, kn, null, 0).Count == 0);
        var sTraded = Siege(Side.Player, new() { [2] = [(0, 3, 1)], [7] = [(0, 3, 2)], [8] = [(3, 0, 1)] });
        var csTraded = BattleChronicle.Build(sTraded, DoctrineSet.Empty, "Test Ground", kn, null, 14);
        Check("siege (synthetic frames): no clock line when the besieger had traded a blow before the bit (the bit alone is not enough: the line must be backed by the frames)",
            csTraded.Lines.All(l => l.Kind != ChronicleKind.Clock) && csTraded.Lines.Any(l => l.Kind == ChronicleKind.Siege) && ChronicleCheck.Verify(csTraded, sTraded, kn, null, 14).Count == 0);
        var sNone = Siege(null, sgEv, 5, 2);
        var csNone = BattleChronicle.Build(sNone, DoctrineSet.Empty, "Test Ground", kn, null, 14);
        Check("siege (synthetic frames): an open ground (no besieger in the header) has no siege line and no clock line, whatever the bit says", csNone.Lines.All(l => l.Kind != ChronicleKind.Siege && l.Kind != ChronicleKind.Clock) && ChronicleCheck.Verify(csNone, sNone, kn, null, 14).Count == 0);
        Check("siege (determinism): the same recording gives the same log twice and after an encode/decode (a version 3 recording)", ReplayCodec.TryDecode(ReplayCodec.Encode(sP), out var sP2, out _) && sP2!.Version == 3 && sP2.Besieger == Side.Player && Sig(csP) == Sig(BattleChronicle.Build(sP2, DoctrineSet.Empty, "Test Ground", kn, null, 14)));
        // the recap on the result screen, from the same recordings: the siege and the clock in the recording's own words, and what the tick limit did
        {
            RoundView View(BattleReplay r, int clock) => new(new RoundRecord(0, DoctrineSet.Empty, DoctrineSet.Empty, 0, false, 0, r.ArenaId, r.Result, r, new Spoils(0, 0), 0UL, 0UL, null, true), null!, null, 24, 24, true) { SiegeClock = clock };
            var recapP = DoctrineGuide.Of(View(sP, 14), "Test Ground");
            Check("recap (synthetic siege): you besiege, the clock fell at the frame the bit is set, and the tick limit stopped the battle: the lines are written out by hand",
                recapP.Siege.SequenceEqual(["Test Ground is a siege, and you besieged it: the clock was 14 ticks.", "The clock fell at tick 5: you had traded no blow by then, lost the stance, and every company of yours advanced."])
                && recapP.Limit == "The battle stopped at its tick limit, and the field went to you: the greater power left decides it, not the number of soldiers.", string.Join(" | ", recapP.Siege));
            var recapE = DoctrineGuide.Of(View(sE, 16), "Test Ground");
            Check("recap (synthetic siege): the enemy besieges and its clock fell: said in the enemy's person",
                recapE.Siege.SequenceEqual(["Test Ground is a siege, and the enemy besieged it: the clock was 16 ticks.", "The clock fell at tick 5: the enemy had traded no blow by then, lost the stance, and every company of theirs advanced."]));
            // a long battle where the besieger traded a blow in time: it kept the stance; one that ended before the clock says nothing of the stance
            int[][] sgLong = Flat(17);
            var evLong = new Dictionary<int, (int S, int T, int D)[]> { [2] = [(0, 3, 1)], [9] = [(3, 0, 1)] };
            var longR = new BattleReplay(3, "test-siege", 1, 120, Enumerable.Range(0, 17).Select(t => SynFrame(t, null, sgLong, evLong, 0)).ToList(), new BattleResult(BattleStatus.DefenderWon, 16, [], []), Side.Player);
            var recapL = DoctrineGuide.Of(View(longR, 14), "Test Ground");
            Check("recap (synthetic siege): a besieger that traded a blow before the clock kept its stance (said), and the tick limit gave the field to the enemy (said)",
                recapL.Siege.SequenceEqual(["Test Ground is a siege, and you besieged it: the clock was 14 ticks.", "You traded a blow before the clock ran out, and kept the stance."])
                && recapL.Limit == "The battle stopped at its tick limit, and the field went to the enemy: the greater power left decides it, not the number of soldiers.");
            var recapShort = DoctrineGuide.Of(View(Siege(Side.Player, sgEv, -1), 14), "Test Ground");
            Check("recap (synthetic siege): a battle that ended before the clock says nothing of the stance (neither line is backed)", recapShort.Siege.Count == 1 && recapShort.Siege[0].StartsWith("Test Ground is a siege"));
            Check("recap (synthetic siege): an open ground has no siege line; a battle that one side lost whole has no tick-limit line; equal power at the limit is said as equal",
                DoctrineGuide.Of(View(Siege(null, sgEv, 5, 2), 0), "Test Ground").Siege.Count == 0 && DoctrineGuide.Of(View(syn, 0), "Test Ground").Limit is null
                && DoctrineGuide.Of(View(Siege(null, sgEv, -1, 2, BattleStatus.Draw), 0), "Test Ground").Limit == "The battle stopped at its tick limit with the power left on the two sides equal.");
        }
        // mutants of the siege log: each must be found by the checker
        {
            Chronicle Edit(Chronicle c0, ChronicleKind k, Func<ChronicleLine, ChronicleLine> f) => c0 with { Lines = c0.Lines.Select(l => l.Kind == k ? f(l) : l).ToList() };
            var swappedSide = Edit(csP, ChronicleKind.Siege, l => l with { Text = l.Text.Replace("You besiege", "The enemy besieges"), Cite = l.Cite with { A = 1 } });
            Check("mutant: a siege line that names the other side as besieger is found", ChronicleCheck.Verify(swappedSide, sP, kn, null, 14).Count > 0);
            var wrongClock = Edit(csP, ChronicleKind.Siege, l => l with { Text = l.Text.Replace("14 ticks", "15 ticks"), Cite = l.Cite with { Value = 15 } });
            Check("mutant: a siege line with another clock than the arena's is found", ChronicleCheck.Verify(wrongClock, sP, kn, null, 14).Count > 0);
            var earlyClock = Edit(csP, ChronicleKind.Clock, l => l with { Tick = 4, Cite = l.Cite with { Tick = 4 } });
            Check("mutant: the clock falling a tick before the bit is found", ChronicleCheck.Verify(earlyClock, sP, kn, null, 14).Count > 0);
            var fakeClock = csTraded with { Lines = csTraded.Lines.Concat([csP.Lines.First(l => l.Kind == ChronicleKind.Clock)]).OrderBy(l => l.Tick).ToList() };
            Check("mutant: a clock line for a besieger that had traded a blow is found", ChronicleCheck.Verify(fakeClock, sTraded, kn, null, 14).Count > 0);
            var noClock = csP with { Lines = csP.Lines.Where(l => l.Kind != ChronicleKind.Clock).ToList() };
            Check("mutant: a missing clock line (the bit is set, the besieger had not struck) is found", ChronicleCheck.Verify(noClock, sP, kn, null, 14).Count > 0);
            var noSiege = csP with { Lines = csP.Lines.Where(l => l.Kind != ChronicleKind.Siege).ToList() };
            Check("mutant: a missing siege line (the header names a besieger) is found", ChronicleCheck.Verify(noSiege, sP, kn, null, 14).Count > 0);
            var otherSideClock = Edit(csE, ChronicleKind.Clock, l => l with { Text = l.Text.Replace("The enemy had", "You had") });
            Check("mutant: a clock line that names the wrong side is found", ChronicleCheck.Verify(otherSideClock, sE, kn, null, 16).Count > 0);
            var softEnd = Edit(csP, ChronicleKind.End, l => l with { Text = l.Text.Replace("the greater power left", "more soldiers left") });
            Check("mutant: an end that says the tick limit goes to more soldiers is found", ChronicleCheck.Verify(softEnd, sP, kn, null, 14).Count > 0);
        }
    }

    // ---- dug in, synthetic: eleven frames, no strike at all. Yours: 0 Riders step 3 units at tick 2; 1 Priests step 35/16 of a unit at tick 3 (too close to the melee reach to tell); 2 Archers stay.
    // Theirs: 3 Spearmen step 3 units at tick 5; 4 Wights and 5 Archers stay.
    {
        int[][] st = [Enumerable.Repeat(10, 11).ToArray(), Enumerable.Repeat(8, 11).ToArray(), Enumerable.Repeat(6, 11).ToArray(), Enumerable.Repeat(10, 11).ToArray(), Enumerable.Repeat(8, 11).ToArray(), Enumerable.Repeat(6, 11).ToArray()];
        ReplayFrame Dug(int t, Func<int, int, (int X, int Y)> q) => new(t, Enumerable.Range(0, 6).Select(i => { var (qx, qy) = q(t, i); return new ReplayBlob(i, kinds[i].Item1, kinds[i].Item2, kinds[i].Item3, qx, qy, st[i][t]); }).ToList(), [], 0);
        (int, int) Q(int t, int i)
        {
            int x = startAt[i].Item1 * Qm, y = startAt[i].Item2 * Qm;
            if (i == 0 && t >= 2) x += 48;
            if (i == 1 && t >= 3) x += 35;
            if (i == 3 && t >= 5) x += 48;
            return (x, y);
        }
        BattleReplay Quiet(Func<int, int, (int X, int Y)> q) => new(2, "test-quiet", 1, 120, Enumerable.Range(0, 11).Select(t => Dug(t, q)).ToList(), new BattleResult(BattleStatus.Draw, 10, [], []));
        var qd = Quiet(Q);
        var csD = BattleChronicle.Build(qd, DoctrineSet.Empty, "Test Ground", kn, roster);
        var wantD = new (int Tick, ChronicleKind Kind, string Text)[]
        {
            (0, ChronicleKind.Start, "Test Ground. 24 of your soldiers face the enemy's 24."),
            (6, ChronicleKind.DugIn, "Your Archers are dug in: six ticks unhit and in place. They strike 12% harder until they are hit or move."),
            (6, ChronicleKind.DugIn, "The enemy Wights are dug in: six ticks unhit and in place. They strike 32% harder until they are hit or move."),
            (10, ChronicleKind.End, "Neither side held the field. You have 24; the enemy has 24. The battle stopped at its tick limit with the power left on the two sides equal."),
        };
        Check("dug in (synthetic frames): the first cohort of each side to be provably dug in has its line: the Riders that stepped three units are not yet, the Priests (a step of 35/16 of a unit, too close to the reach to tell) are not provable, so it is the Archers at tick 6; theirs are the Wights (the Spearmen stepped); a battle that ends with both sides standing says the power left was equal",
            csD.Lines.Select(l => (l.Tick, l.Kind, l.Text)).SequenceEqual(wantD), string.Join(" | ", csD.Lines.Select(l => $"{l.Tick} {l.Kind} {l.Text}")));
        Check("dug in (synthetic frames): the independent checker finds both lines backed (six frames without a strike, in place, alive) and the first of their side", ChronicleCheck.Verify(csD, qd, kn, roster).Count == 0, string.Join(" | ", ChronicleCheck.Verify(csD, qd, kn, roster).Take(3)));
        Check("dug in (synthetic frames): without the cohorts' own traits no dug-in line is written (the bonus is the cohort's own, a flip may have changed it)", BattleChronicle.Build(qd, DoctrineSet.Empty, "Test Ground", kn, null).Lines.All(l => l.Kind != ChronicleKind.DugIn));
        var csM = BattleChronicle.Build(qd, DoctrineSet.Empty, "Test Ground", kn, (s, k) => Roster.TraitsOf(7));
        Check("dug in (synthetic frames): a cohort with the longest reach gains nothing from it and the line says so, in the engine's table (a Catapult's bonus is nothing)",
            csM.Lines.Where(l => l.Kind == ChronicleKind.DugIn).Select(l => l.Text).SequenceEqual(["Your Archers are dug in: six ticks unhit and in place. Their reach is already the longest, so it gains them nothing.", "The enemy Wights are dug in: six ticks unhit and in place. Their reach is already the longest, so it gains them nothing."])
            && UnitRules.SetBonusPct(Roster.TraitsOf(7)) == 0 && ChronicleCheck.Verify(csM, qd, kn, (s, k) => Roster.TraitsOf(7)).Count == 0);
        Check("dug in (hand-written bonuses): the bonus in a line is the battle's own (pike line 32, far shooters 12, a Catapult none), the set time is six ticks",
            UnitRules.SetTicks == 6 && UnitRules.SetBonusPct(Trait.None) == 32 && UnitRules.SetBonusPct(Trait.Ranged) == 12 && UnitRules.SetBonusPct(Trait.Holy | Trait.Ranged) == 12 && Enumerable.Range(0, 8).All(k => UnitRules.SetBonusPct(Roster.TraitsOf(k)) == ChronicleCheck.HandBonus(Roster.TraitsOf(k))));
        // a cohort that steps CLEARLY within the reach stays still, and the same cohort stepping clearly beyond it starts again (the two sides of the margin)
        {
            (int, int) QIn(int t, int i) => i == 1 && t >= 3 ? (startAt[1].Item1 * Qm + 30, startAt[1].Item2 * Qm) : (startAt[i].Item1 * Qm, startAt[i].Item2 * Qm);       // 30/16 of a unit: clearly inside
            var inside = BattleChronicle.Build(Quiet(QIn), DoctrineSet.Empty, "Test Ground", kn, roster);
            var dl = inside.Lines.Where(l => l.Kind == ChronicleKind.DugIn).ToList();
            Check("dug in (synthetic frames): a step clearly inside the melee reach does not start the count again (the Riders, first by id, are dug in at tick 6)", dl.Count == 2 && dl[0].Tick == 6 && dl[0].Text.StartsWith("Your Riders are dug in"), string.Join(" | ", dl.Select(l => l.Text)));
        }
        // mutants: each must be found by the checker
        {
            Chronicle Edit(Chronicle c0, Func<ChronicleLine, bool> pick, Func<ChronicleLine, ChronicleLine> f) { var first = c0.Lines.First(pick); return c0 with { Lines = c0.Lines.Select(l => ReferenceEquals(l, first) ? f(l) : l).ToList() }; }
            var early = Edit(csD, l => l.Kind == ChronicleKind.DugIn, l => l with { Tick = 5, Cite = l.Cite with { Tick = 5 } });
            Check("mutant: a dug-in line one tick early is found", ChronicleCheck.Verify(early, qd, kn, roster).Count > 0);
            var ambiguous = Edit(csD, l => l.Kind == ChronicleKind.DugIn, l => l with { Text = "Your Priests are dug in: six ticks unhit and in place. They strike 12% harder until they are hit or move.", Cite = l.Cite with { A = 1 } });
            Check("mutant: a dug-in line for a cohort whose step is too close to the reach to tell is found", ChronicleCheck.Verify(ambiguous, qd, kn, roster).Count > 0);
            var stepped = Edit(csD, l => l.Kind == ChronicleKind.DugIn, l => l with { Text = "Your Riders are dug in: six ticks unhit and in place. They strike 32% harder until they are hit or move.", Cite = l.Cite with { A = 0, Value2 = 32 } });
            Check("mutant: a dug-in line for a cohort that stepped three units at tick 2 (the count started again) is found", ChronicleCheck.Verify(stepped, qd, kn, roster).Count > 0);
            var bonus = Edit(csD, l => l.Kind == ChronicleKind.DugIn, l => l with { Text = l.Text.Replace("12%", "32%"), Cite = l.Cite with { Value2 = 32 } });
            Check("mutant: a dug-in line with the wrong bonus for the cohort's traits is found", ChronicleCheck.Verify(bonus, qd, kn, roster).Count > 0);
            var twice = csD with { Lines = csD.Lines.Concat([csD.Lines.Last(l => l.Kind == ChronicleKind.DugIn) with { Cite = new ChronicleCite(CiteKind.Set, 6, 5, 0, 6, 12), Text = "The enemy Archers are dug in: six ticks unhit and in place. They strike 12% harder until they are hit or move." }]).OrderBy(l => l.Tick).ToList() };
            Check("mutant: a second dug-in line for a side (not the first of its side) is found", ChronicleCheck.Verify(twice, qd, kn, roster).Count > 0);
            var missing = csD with { Lines = csD.Lines.Where(l => l.Kind != ChronicleKind.DugIn).ToList() };
            Check("mutant: no dug-in line where a cohort is provably dug in is found", ChronicleCheck.Verify(missing, qd, kn, roster).Count > 0);
        }
    }

    // ---- sieges and dug-in lines in REAL battles, against the engine's own counters. The same set-up is run again by hand to read the session's Stats
    // (SetFirstTick: the tick at whose start a cohort of the side was first set; ClockFiredAtTick: the tick at whose start the clock fell).
    {
        string FrameSig(ReplayFrame f) => string.Join(";", f.Blobs.Select(b => $"{b.Id}:{b.X},{b.Y},{b.Strength}")) + "|" + string.Join(";", f.Events.Select(e => $"{e.Source}>{e.Target}={e.Damage}")) + "|" + f.WorldFlags;
        int sieges = 0, fell = 0, kept = 0, youBesiege = 0, enemyBesieges = 0, dugLines = 0, dugAtEngineTick = 0, dugLater = 0, dugEarlier = 0, omitted = 0, replayBad = 0, headerBad = 0, clockBad = 0, verifyBad = 0, handSieges = 0, handFell = 0;
        var probs = new List<string>();

        // the set-up run by hand on the game's own engine: its frames, its result and the session's counters
        async Task<(List<ReplayFrame> Frames, BattleResult Result, TracerBattleStats Stats)> ByHand(DoctrineGame gm, BattleSetup setup)
        {
            var sess = (TracerBattleSession)await gm.Context.Battle.StartAsync(setup, default);
            var frames = new List<ReplayFrame> { sess.Snapshot() };
            while (sess.Status == BattleStatus.Running && sess.Tick < setup.TickLimit) { await sess.FieldOrdersAsync(default); await sess.MoveAsync(default); await sess.EngageAsync(default); await sess.ApplyLossesAsync(default); frames.Add(sess.Record()); }
            return (frames, sess.Finish(sess.Tick >= setup.TickLimit), sess.Stats);
        }
        // one siege battle judged: the chronicle checked, its clock line and dug-in lines against the engine's counters
        void Judge(string label, BattleReplay replay, BattleSetup setup, DoctrineSet doctrine, ArenaInfo info, TracerBattleStats stats, bool hand)
        {
            Func<Side, int, Trait?> traitsOf = (s, k) => (s == Side.Player ? setup.Attacker : setup.Defender).Cohorts.Where(c => c.Key == k).Select(c => (Trait?)c.Traits).FirstOrDefault();
            var c = BattleChronicle.Build(replay, doctrine, info.Name, kn, traitsOf, info.SiegeClock);
            var pr = ChronicleCheck.Verify(c, replay, kn, traitsOf, info.SiegeClock); if (pr.Count > 0) { verifyBad++; probs.AddRange(pr.Take(2).Select(x => $"{label}: {x}")); }
            var wp = ChronicleCheck.VerifyWhy(c, replay, doctrine, kn); if (wp.Count > 0) { verifyBad++; probs.AddRange(wp.Take(2).Select(x => $"{label} why: {x}")); }
            bool hdr = replay.Version == 3 && replay.Besieger is not null && setup.Besieger == replay.Besieger && info.SiegeClock > 0 && c.Lines.Count(l => l.Kind == ChronicleKind.Siege) == 1;
            if (!hdr) { headerBad++; probs.Add($"{label}: header {replay.Version} {replay.Besieger}"); }
            var sieger = replay.Besieger!.Value;
            if (hand) handSieges++; else { sieges++; if (sieger == Side.Player) youBesiege++; else enemyBesieges++; }
            var clockLine = c.Lines.FirstOrDefault(l => l.Kind == ChronicleKind.Clock);
            int fired = stats.ClockFiredAtTick;
            if (fired >= 0 ? clockLine is null || clockLine.Tick != fired + 1 : clockLine is not null) { clockBad++; probs.Add($"{label}: engine clock fired at {fired}, chronicle {clockLine?.Tick.ToString() ?? "no line"}"); }
            bool tradedInTime = replay.Frames.Any(f => f.Tick <= info.SiegeClock && f.Events.Any(e => f.Blobs.Any(b => (b.Id == e.Source || b.Id == e.Target) && b.Side == sieger)));
            if (fired >= 0) { if (hand) handFell++; else fell++; if (tradedInTime) { clockBad++; probs.Add($"{label}: the clock fell although the besieger had traded"); } }
            else if (tradedInTime) { if (!hand) kept++; }
            else if (replay.Result.Ticks > info.SiegeClock) { clockBad++; probs.Add($"{label}: the clock did not fall and the besieger had not traded"); }       // a battle over before the clock is neither
            for (int role = 0; role < 2; role++)
            {
                var side = role == 0 ? Side.Player : Side.Enemy;
                var dl = c.Lines.FirstOrDefault(l => l.Kind == ChronicleKind.DugIn && replay.Frames[0].Blobs.First(b => b.Id == l.Cite.A).Side == side);
                int eng = stats.SetFirstTick[role];
                if (dl is null) { if (eng >= 0) omitted++; continue; }
                dugLines++;
                if (dl.Tick == eng) dugAtEngineTick++; else if (dl.Tick > eng) dugLater++; else { dugEarlier++; probs.Add($"{label}: {side} dug-in line at {dl.Tick}, the engine first set one at {eng}"); }
            }
        }

        // (1) games on the real sections: the player holds every line in the rounds it besieges, the variety orders in the others
        for (int seed = 1; seed <= 80 && sieges < 14; seed++)
        {
            var tapS = new SetupTap();
            var gm = new DoctrineSections().Build(new SiegeHands(seed), new DoctrineDomain { MaxRounds = 2, Offers = OfferRules.Off }, tapS);
            if (!Enumerable.Range(0, 2).Any(r => gm.ArenaFor(seed, r).IsSiege)) continue;
            var game = await gm.PlayAsync(seed);
            for (int rd = 0; rd < game.Rounds.Count; rd++)
            {
                var info = gm.ArenaFor(seed, rd);
                if (!info.IsSiege) continue;
                var rec = game.Rounds[rd]; var setup = tapS.Setups[rd];
                string label = $"seed {seed} round {rd}";
                if (rec.Replay.Besieger != (rd % 2 == 0 ? Side.Player : Side.Enemy)) { headerBad++; probs.Add($"{label}: besieger {rec.Replay.Besieger}"); }
                var (frames, _, stats) = await ByHand(gm, setup);
                if (frames.Count != rec.Replay.Frames.Count || !frames.Zip(rec.Replay.Frames).All(p => FrameSig(p.First) == FrameSig(p.Second))) { replayBad++; probs.Add($"{label}: the hand-run battle is not the recording"); continue; }
                Judge(label, rec.Replay, setup, rec.PlayerDoctrine, info, stats, false);
            }
        }

        // (2) hand-set sieges on the same engine (cohorts and orders written out here): both sides hold, or the besieger advances; each arena, each besieger
        {
            CohortRead Co(int key, int n, int slot) => new(key, n, Roster.TraitsOf(key), slot, 0, 100);
            FieldOrder Fo(int group, FieldOrderKind kind) => new(group, kind, 1);
            var gm = new DoctrineSections().Build(new SiegeHands(1), new DoctrineDomain { MaxRounds = 1, Offers = OfferRules.Off });
            foreach (var arenaId in new[] { "chokepoint", "falling-gate" })
                foreach (var by in new[] { Side.Player, Side.Enemy })
                    foreach (var advance in new[] { false, true })
                    {
                        var info = gm.Context.Battle.Arenas.First(a => a.Id == arenaId);
                        FieldOrder[] orders(bool besieger) => besieger && advance ? [Fo(0, FieldOrderKind.Advance), Fo(1, FieldOrderKind.Advance), Fo(2, FieldOrderKind.Advance)] : [Fo(0, FieldOrderKind.Hold), Fo(1, FieldOrderKind.Hold), Fo(2, FieldOrderKind.Hold)];
                        var setup = new BattleSetup(arenaId, 7, 60,
                            new BattleSide(Side.Player, [Co(0, 24, 0), Co(3, 16, 1), Co(4, 12, 2)], orders(by == Side.Player)),
                            new BattleSide(Side.Enemy, [Co(1, 20, 0), Co(3, 16, 1), Co(5, 14, 2)], orders(by == Side.Enemy)), by);
                        var (frames, byHandResult, stats) = await ByHand(gm, setup);
                        var replay = new BattleReplay(BattleReplay.VersionFor(setup.Besieger), arenaId, setup.Seed, setup.TickLimit, frames, byHandResult, setup.Besieger);
                        Judge($"hand {arenaId} {by} {(advance ? "besieger advances" : "both hold")}", replay, setup, DoctrineSet.Empty, info, stats, true);
                    }
        }
        Console.WriteLine($"  (real sieges: {sieges} battles, you besiege in {youBesiege}, the enemy in {enemyBesieges}; the clock fell in {fell} and the besieger traded in time in {kept}; hand-set sieges {handSieges}, the clock fell in {handFell}; dug-in lines {dugLines}: at the engine's own first tick {dugAtEngineTick}, later {dugLater}, earlier {dugEarlier}; sides with a set cohort but no line {omitted})");
        Check($"siege (recorded battles): {sieges} real siege battles (both besiegers) and {handSieges} hand-set ones are read right: version 3, the besieger by the game's roles rule and the same in the set-up, one siege line each, and the independent checker finds every line (siege, clock, dug in, Why) backed", sieges >= 8 && youBesiege >= 3 && enemyBesieges >= 3 && handSieges == 8 && headerBad == 0 && verifyBad == 0, string.Join(" | ", probs.Take(3)));
        Check("siege (engine): running the same set-up again by hand gives the very frames of the recording, so the session's own counters are the recording's", replayBad == 0, string.Join(" | ", probs.Where(x => x.Contains("hand-run")).Take(2)));
        Check($"siege (engine): the clock line is on the frame after the tick at whose start the engine's clock fell (ClockFiredAtTick + 1), there is none when it did not fall, and it falls exactly when the besieger had traded no blow by it; both happened ({fell + handFell} fell, {kept} kept the stance)", clockBad == 0 && fell + handFell >= 3 && kept >= 1, string.Join(" | ", probs.Where(x => x.Contains("clock")).Take(2)));
        Check($"dug in (engine): no dug-in line is ever earlier than the engine's first set tick for its side, and {dugAtEngineTick} of {dugLines} are exactly at it (the rest name a later cohort because an earlier one was not provable)", dugEarlier == 0 && dugLines >= 6 && dugAtEngineTick * 4 >= dugLines * 3, string.Join(" | ", probs.Where(x => x.Contains("dug-in")).Take(2)));
    }
    // ---- the chips of the replay, on a component of their own: the siege and the fallen clock, from the recording's header and bit
    {
        using var ctxA = NewCtx(false);
        var sg9 = Enumerable.Range(0, 9).Select(t => SynFrame(t, null, null, new(), t >= 5 ? WorldFlag.ClockFell : 0)).ToList();
        var rp = new BattleReplay(3, "test-siege", 1, 120, sg9, new BattleResult(BattleStatus.Draw, 8, [], []), Side.Enemy);
        var arenaC = ctxA.Render<Showroom.Components.DoctrineArena>(ps => ps.Add(p => p.Replay, rp).Add(p => p.SiegeClock, 14));
        string Chip() => Regex.Replace(arenaC.Find(".dc-siegechip").TextContent, @"\s+", " ").Trim();
        string before = Chip(); bool beforeFell = arenaC.Find(".dc-siegechip").ClassList.Contains("fallen");
        await arenaC.InvokeAsync(() => arenaC.Instance.Seek(4)); string at4 = Chip();
        await arenaC.InvokeAsync(() => arenaC.Instance.Seek(5)); string at5 = Chip(); bool fell5 = arenaC.Find(".dc-siegechip").ClassList.Contains("fallen");
        await arenaC.InvokeAsync(() => arenaC.Instance.Seek(8)); string at8 = Chip();
        Check("replay chips: a recording that names the enemy as besieger shows who besieges and the clock until the frame the bit is set, then that the clock has fallen and the enemy has lost its stance, to the end (a bit never clears)",
            before == "The enemy besieges: the clock is 14 ticks" && !beforeFell && at4 == before && at5 == "The clock has fallen: the enemy has lost its stance" && fell5 && at8 == at5, $"{before} | {at4} | {at5} | {at8}");
        var open9 = new BattleReplay(2, "test-open", 1, 120, sg9, new BattleResult(BattleStatus.Draw, 8, [], []));
        var arenaO = ctxA.Render<Showroom.Components.DoctrineArena>(ps => ps.Add(p => p.Replay, open9));
        Check("replay chips: an open ground (no besieger in the header) shows no siege chip, whatever the bit says", arenaO.FindAll(".dc-siegechip").Count == 0);
        Check("replay chips: the text alternative of the arena says the siege too", arenaC.Find("svg.dc-arena").GetAttribute("aria-label")!.EndsWith("The clock has fallen: the enemy has lost its stance."));
    }
}

// ---------------------------------------------------------------------------------------------- wording
{
    string all = string.Join("\n", allText);
    Check("wording: no em dash anywhere in the page", !all.Contains('—') && !allMarkup.Any(m => m.Contains('—')));
    foreach (var bad in new[] { "universal", "complete", "open source", "free to use", "MIT", "chance", "luck", "lucky", "guaranteed", "unbeatable", "plays well", "outlearn", "outperform", "smarter", "pre-trained model", "probability", "accuracy", "accurate", "Confidence", "Restrict", "phone-tested", "tested on" })
        Check($"wording: no '{bad}'", !Regex.IsMatch(all, @"\b" + Regex.Escape(bad) + @"\b", RegexOptions.IgnoreCase));
    Check("wording: the general is described only as the probe allows (learns from your own choices, nothing pre-trained, different players different generals)",
        all.Contains("learns how you set a doctrine from your own choices, in your browser, with nothing pre-trained") && all.Contains("different players end up with different generals"));
    Check("wording: nothing says the net beats a table or quotes a hit rate", !Regex.IsMatch(all, @"beats a (lookup|table)|outlearns|3 (times )?in 10|4 in 10|5 in 10", RegexOptions.IgnoreCase));
    Check("wording: battles are said to be simulated once on this device and the replay to be a recording", all.Contains("simulated once, on your device") && all.Contains("The replay is a recording"));
    Check("wording: no persistence is claimed or invented (a refresh forgets the general)", all.Contains("Nothing is saved: refresh the page and the general is gone") && !all.Contains("saved in your browser"));
    // the editorial fixes: each new phrase is there, each old one is gone
    // The general as TinyHoloGeneral now is (Hybrid by default, history off, margin from its options): two tables rank the cards on the council, the net takes a slot at the margin
    Check("wording A: the top of How it works names the general as two small tables and a tiny HoloFormer from the AlgFormer package (the page runs the Hybrid general); no habit table 'with' a net, no tiny AlgFormer model",
        new global::Doctrine.General.GeneralOptions().Mode == global::Doctrine.General.GeneralMode.Hybrid && all.Contains("and the general is two small tables and a tiny HoloFormer from the AlgFormer package.") && !all.Contains("a habit table with a tiny HoloFormer") && !all.Contains("tiny AlgFormer model"));
    Check("wording A: About says how it chooses, as the code does: it ranks the cards on the council; the habit table (the situation) and the taste table (sort of order, line, trait or kind, the very card) rank them; the table that has guessed better speaks per slot; the HoloFormer takes a slot at the margin; last round is not read",
        all.Contains("Each round it reads the cards on the council and ranks them, first, second, third and fourth. Two small tables do the ranking. The habit table remembers which order you put in each slot in a situation like this one:")
        && all.Contains("The taste table remembers what kind of card you take from a council: the sort of order, the line, trait or kind it touches, and the very card, so a liking for one kind of card carries to cards it has not met.")
        && all.Contains("It does not read what you took last round.")
        && all.Contains("A tiny HoloFormer from the AlgFormer package learns beside them from the same examples. For each slot the table that has guessed your picks better speaks, and the HoloFormer takes the slot only once it has been right on that slot at least two more times than that table, over your recent rounds.")
        && !all.Contains("whichever of the two") && !all.Contains("guessed your choices better") && !all.Contains("Inside it are a small habit table"));
    Check("wording A: the margin in the words is the general's own (Margin 2 reads 'two more times'; Margin 1 and 3 read 'one more time' and 'three more times'), the history sentence follows the option, and a table-only or net-only general says so",
        new global::Doctrine.General.GeneralOptions().Margin == 2
        && string.Join(" ", DoctrineWords.GeneralHow(new global::Doctrine.General.GeneralOptions { Margin = 3 })).Contains("at least three more times than that table")
        && string.Join(" ", DoctrineWords.GeneralHow(new global::Doctrine.General.GeneralOptions { Margin = 1 })).Contains("at least one more time than that table")
        && string.Join(" ", DoctrineWords.GeneralHow(new global::Doctrine.General.GeneralOptions { History = true })).Contains("also reads whether a card repeats what you took last round")
        && !string.Join(" ", DoctrineWords.GeneralHow(new global::Doctrine.General.GeneralOptions { Mode = global::Doctrine.General.GeneralMode.TableOnly })).Contains("HoloFormer")
        && !string.Join(" ", DoctrineWords.GeneralHow(new global::Doctrine.General.GeneralOptions { Mode = global::Doctrine.General.GeneralMode.NetOnly })).Contains("table")
        && DoctrineWords.GeneralBuilt(new global::Doctrine.General.GeneralOptions { Mode = global::Doctrine.General.GeneralMode.TableOnly }) == "two small tables");
    Check("wording B: it is a match record, said as what it is, in the two senses the code has: beside a card, how often the card ranked in a slot was the card taken there; under the pick, the share of rounds in which all of it came out as set; over about the last 64 rounds; no confidence, probability or accuracy",
        all.Contains("Match record") && all.Contains($"The match record is a score out of 1000. Beside each card: over about the last {RecordWindowHand} rounds, how often the card it ranked in that slot was the card you took in that slot (a round where neither of you named a card for the slot is not counted). For the whole pick it reads: over about the last {RecordWindowHand} rounds, the share of rounds in which all of it came out exactly as the doctrine you then set. A short record reads a little under that share, so it starts low and climbs as the picks keep matching.")
        && all.Contains($"of 1000 for the whole pick: over about the last {RecordWindowHand} rounds, the share of rounds in which all of it came out exactly as the doctrine you then set.")
        && all.Contains("It ranked the cards on this council from what you have taken from councils before, and fills the slots in that order.")
        && !all.Contains("a score for how well") && !Regex.IsMatch(all, "confiden", RegexOptions.IgnoreCase));
    Check("wording C: the working screen says it runs on the device in small steps", all.Contains("This runs on your device, in small steps.") && !all.Contains("so the page stays usable") && !all.Contains("one small step at a time"));
    Check("wording D: the examples count says what it counts, in the chip and on the start and end screens",
        Regex.Matches(all, Regex.Escape("(each doctrine you set and each battle result)")).Count >= 3 && all.Contains("examples seen (each doctrine you set and each battle result)") && all.Contains("since you opened this page"));
    Check("wording E: the empty-army sentence tells what to do and why", all.Contains("Spend your gold on cohorts: a battle needs soldiers.") && !all.Contains("starts lost"));
    Check("wording F: Cull never appears (the army allows none, so the council never holds one): no card, no sentence, no operation name", !all.Contains("Cull") && !all.Contains("No order to dismiss soldiers") && !all.Contains("this version") && !allMarkup.Any(m => m.Contains("Cull a trait") || m.Contains("Dismiss a trait") || m.Contains("Dismiss ")));
    Check("wording G: the offers are said in the lord's voice: the muster of companies and the council of orders, a pick is a whole company, a reroll has a price and a limit", all.Contains("The muster") && all.Contains("The council") && all.Contains("each whole or not at all") && all.Contains("The three formations") && Regex.IsMatch(all, @"Reroll for \d+ gold \(\d+ left\)") && all.Contains("Keep these") && all.Contains("Your general's pick of the council"));
    Check("wording G: no word of the offers says draw as luck: the offers are the game's own draw from the game seed, never 'random', 'lucky' or 'roguelike'", !Regex.IsMatch(all, @"\b(random|randomly|roguelike|loot|gacha)\b", RegexOptions.IgnoreCase));
    Check("wording H: the muster says it is dealt once a round and only the council can be drawn again (the game offers no second draw of companies)",
        DoctrineWords.MusterNote == "The muster is dealt once a round and cannot be drawn again. Only the council can be, for gold." && all.Contains(DoctrineWords.MusterNote)
        && typeof(UnitOffer).GetProperties().All(p => !p.Name.Contains("Reroll")) && typeof(OrderOffer).GetProperties().Any(p => p.Name == "RerollCost"));

    // ---- the match record's window: probed on a real general (a table-only one: the counting is the same code). A player who never changes: the whole-pick figure climbs until the window passes its size, then the counts are halved and it falls.
    {
        var gp = new global::Doctrine.General.TinyHoloGeneral(new global::Doctrine.General.GeneralOptions { Mode = global::Doctrine.General.GeneralMode.TableOnly });
        var sum = new int[GeneralSummary.Length];
        var picks = DoctrineSet.Of(DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), DoctrineVocabulary.Field(FieldOrderKind.Hold, 2));
        var input = new GeneralInput(0, sum, LegalMask.All);
        int prevConf = -1, fellAt = -1;
        for (int n = 1; n <= 100 && fellAt < 0; n++)
        {
            await gp.LearnAsync(new GeneralExample(input, picks, false), default);
            int conf = (await gp.SuggestAsync(input, default)).ConfidencePermille;
            if (prevConf >= 0 && conf < prevConf) fellAt = n;
            prevConf = conf;
        }
        Check($"match record: the window the words state ('about the last {RecordWindowHand} rounds') is the general's own: its counts are halved when the window passes the figure (probed: the whole-pick figure first falls at example {fellAt}), and DoctrineWords states that figure",
            fellAt == RecordWindowHand && DoctrineWords.RecordWindow == RecordWindowHand && DoctrineWords.RecordScale == 1000, $"first fall at example {fellAt}");
    }

    // ---- dug in, the siege and the tick limit, said once in the battle's own figures
    {
        const string dugRule = "Dug in. A line that holds its ground for six ticks without being hit is dug in: it strikes harder until it is hit or moves. A line that already shoots from afar gains less, and the longest reach on the field gains nothing.";
        Check("dug in (words): the rule is said once with the battle's set time (six ticks) and read from it, on the page under 'About the field'; the Hold card's sentence carries it, no other order's does",
            DoctrineWords.DugInRule() == dugRule && UnitRules.SetTicks == 6 && all.Contains(dugRule) && all.Contains("About the field")
            && DoctrineWords.Sentence(DoctrineVocabulary.Field(FieldOrderKind.Hold, 1)).Contains("Unhit for six ticks, it is dug in and strikes harder until it is hit or moves.")
            && Enumerable.Range(0, 6).Where(k => k != (int)FieldOrderKind.Hold).All(k => !DoctrineWords.Sentence(DoctrineVocabulary.Field((FieldOrderKind)k, 0)).Contains("dug in")));
        Check("dug in (words): what it is worth to a cohort is the battle's own bonus, per traits: 32% for a plain line, 12% for far shooters, nothing for the longest reach",
            DoctrineWords.DugInFor(Trait.None) == "Dug in, it strikes 32% harder." && DoctrineWords.DugInFor(Trait.Ranged) == "Dug in, it strikes 12% harder."
            && DoctrineWords.DugInFor(Roster.TraitsOf(7)) == "Dug in, it gains nothing: its reach is already the longest." && Enumerable.Range(0, 8).All(k => DoctrineWords.DugInFor(Roster.TraitsOf(k)).Contains($"{ChronicleCheck.HandBonus(Roster.TraitsOf(k))}%") || ChronicleCheck.HandBonus(Roster.TraitsOf(k)) == 0));
        Check("siege (words): who besieges in which round is the game's own roles rule: you in the first, third, fifth and seventh rounds of eight, the enemy in the rest (five rounds: first, third and fifth; three: first and third)",
            DoctrineWords.YourSiegeRounds(8) == "first, third, fifth and seventh" && DoctrineWords.YourSiegeRounds(5) == "first, third and fifth" && DoctrineWords.YourSiegeRounds(3) == "first and third"
            && Enumerable.Range(0, 8).All(r => SiegeRules.BesiegerOf(r) == (r % 2 == 0 ? Side.Player : Side.Enemy)));
        const string siegeRule = "Some grounds are sieges: a wall stands between the armies and one side besieges. The besieger carries a siege clock. A besieger that has traded no blow when the clock runs out loses its stance, and every company of it must advance; the other side may wait. The sides swap every round: in a game of eight rounds you besiege in the first, third, fifth and seventh rounds and the enemy in the rest. The open grounds have no clock.";
        Check("siege (words): the rule is on the page under 'About the field' without a ground's name (so the static shell can carry it), and after a game has been started it adds the grounds of the battle's own catalogue with their clocks",
            DoctrineWords.SiegeRule(8) == siegeRule && all.Contains(siegeRule)
            && all.Contains("The sieges are The Chokepoint (a clock of 14 ticks) and The Falling Gate (a clock of 16 ticks); the open grounds are Open Field and Ridge Road."));
        Check("tick limit (words): the rule is that the greater power left takes the field, not the side with more soldiers, and equal power is a draw; it is on the page and nowhere does the page say the count of soldiers decides it",
            DoctrineWords.TickLimitRule() == "If the tick limit stops a battle, the field goes to the side with the greater power left, each company's soldiers weighed by its power, how hard it strikes, how far it reaches and how little damage it takes, not to the side with more soldiers; equal power is a draw." && all.Contains(DoctrineWords.TickLimitRule())
            && !Regex.IsMatch(all, @"more soldiers (left )?wins|the side with more soldiers (left )?wins"));
        // the rule itself, read from the battle: Rating counts power, so a side with fewer soldiers can hold more of it
        Check("tick limit (the rule): the battle's own Rating is soldiers x power x damage x reach / taken, so fewer Catapults outweigh more Spearmen (what the words say)",
            UnitRules.Rating(10, 100, Roster.TraitsOf(7)) > UnitRules.Rating(11, 100, Roster.TraitsOf(0)) && new TracerBattleOptions().LimitByPower);
    }
}

// ---------------------------------------------------------------------------------------------- the orders in a lord's words, against the rules
{
    var svc = new global::Doctrine.Army.PleromaArmyService();
    // A host written out by hand: 5 Spearmen in front, 4 Archers in the middle, 3 Shieldwall at the back, gold to spare, no seasoning.
    const string hostCode = "DA2.0.0.500.0..0:5:0:0;1:3:2:0;3:4:1:0";
    OrderTrial? TryOn(int token, string code = hostCode)
    {
        svc.TryParse(code, out var a, out _);
        var before = a!.Read();
        var r = a.ApplyArmyOrdersAsync(DoctrineSet.Of(token), CancellationToken.None).Result;
        return r.Applied == 1 ? new OrderTrial(before, a.Read()) : null;
    }
    int Tk(ArmyOrderKind k, int a = 0) => DoctrineVocabulary.Army(k, a);
    string[] lineHand = ["Front", "Middle", "Back"];
    string[] traitHand = ["heavily armoured", "fleet", "shoots from afar", "blessed", "cursed", "beast", "siege-built", "shielded"];
    string[] folkHand = ["the heavily armoured", "the fleet", "the far-shooters", "the blessed", "the cursed", "the beasts", "the siege engines", "the shielded"];
    string[] kindHand = ["Spearmen", "Shieldwall", "Riders", "Archers", "Priests", "Wights", "Wolves", "Catapults"];

    // ---- the names
    var wrongNames = new List<string>();
    void Want(int token, string text) { if (DoctrineWords.Label(token) != text) wrongNames.Add($"{DoctrineVocabulary.Name(token)}: '{DoctrineWords.Label(token)}' not '{text}'"); }
    for (int g = 0; g < 3; g++) for (int f = 0; f < 6; f++) Want(DoctrineVocabulary.Field((FieldOrderKind)f, g), $"{lineHand[g]} line: {ChronicleCheck.HandNames[f]}");
    Want(Tk(ArmyOrderKind.Rotate, 1), "Reform the ranks: 1 place"); Want(Tk(ArmyOrderKind.Rotate, 2), "Reform the ranks: 2 places");
    for (int b = 0; b < 8; b++) { Want(Tk(ArmyOrderKind.Flip, b), "Reforge: " + traitHand[b]); Want(Tk(ArmyOrderKind.Aura, b), "Raise the banner of " + folkHand[b]); Want(Tk(ArmyOrderKind.Merge, b), "Swear into the " + kindHand[b]); }
    Want(Tk(ArmyOrderKind.Age), "Drill the host");
    Want(DoctrineVocabulary.WhenLost(1), "Should a quarter fall"); Want(DoctrineVocabulary.WhenLost(2), "Should half fall"); Want(DoctrineVocabulary.WhenLost(3), "Should three quarters fall");
    Check("wording (orders): every order has its lord's name: Press on, Hold the line, Ride the flank, Fall back, Guard the host, Run them down; Reform the ranks, Reforge, Drill the host, Swear into the, Raise the banner of, Should a quarter / half / three quarters fall",
        wrongNames.Count == 0, string.Join(" | ", wrongNames.Take(3)));
    Check("wording (orders): the kind names stay (Spearmen, Shieldwall, Riders, Archers, Priests, Wights, Wolves, Catapults)", Enumerable.Range(0, 8).All(k => Roster.NameOf(k) == kindHand[k] && DoctrineWords.KindName(k) == kindHand[k]));
    Check("wording (orders): the traits are plain adjectives (heavily armoured, fleet, shoots from afar, blessed, cursed, beast, siege-built, shielded)", Enumerable.Range(0, 8).All(b => DoctrineWords.Traits[b] == traitHand[b]) && DoctrineWords.TraitList(Trait.Heavy | Trait.Shielded) == "heavily armoured, shielded");

    // ---- Reform the ranks: the direction in the sentence is the army's
    bool rotOk = true;
    for (int n = 1; n <= 2; n++)
    {
        var t = TryOn(Tk(ArmyOrderKind.Rotate, n))!;
        foreach (var c in t.Before.Cohorts) rotOk &= t.After.Cohorts.First(x => x.Key == c.Key).Slot == (c.Slot + n) % 3;
    }
    Check("rotate: the sentence says front to middle to back, then round to the front, and the army moves every cohort exactly so (by 1 and by 2 places)",
        rotOk && DoctrineWords.Effect(Tk(ArmyOrderKind.Rotate, 1)) == "Every cohort steps 1 place along the line." && DoctrineWords.Effect(Tk(ArmyOrderKind.Rotate, 2)) == "Every cohort steps 2 places along the line."
        && DoctrineWords.Sentence(Tk(ArmyOrderKind.Rotate, 1)).Contains("(front to middle to back, then round to the front)"));

    // ---- previews: a hand-built host, each line written out by hand
    string P(int token, string? note = null) => DoctrineWords.Preview(token, TryOn(token)) ?? "(none)";
    var prevWant = new (int Token, string Text)[]
    {
        (Tk(ArmyOrderKind.Rotate, 1), "Your front line goes from 5 Spearmen to 3 Shieldwall."),
        (Tk(ArmyOrderKind.Rotate, 2), "Your front line goes from 5 Spearmen to 4 Archers."),
        (Tk(ArmyOrderKind.Flip, 2), "8 soldiers take it up and 4 shed it."),
        (Tk(ArmyOrderKind.Age), $"3 cohorts grow {Roster.VetStepPct}% stronger."),
        (Tk(ArmyOrderKind.Merge, 0), "3 Shieldwall swear in: the Spearmen go from 5 to 8."),
        (Tk(ArmyOrderKind.Aura, 7), "The cohorts that carry it fight 3% harder this round."),
    };
    Check("preview: 'Your front line goes from X to Y' and the other lines are computed from the army's own before and after, equal to the hand-written lines on a hand-built host",
        prevWant.All(w => P(w.Token) == w.Text), string.Join(" | ", prevWant.Where(w => P(w.Token) != w.Text).Select(w => P(w.Token))));
    Check("preview: an order for a line, a trigger, or one the host would not take has no preview line (nothing is invented)",
        DoctrineWords.Preview(DoctrineVocabulary.Field(FieldOrderKind.Hold, 0), TryOn(Tk(ArmyOrderKind.Age))) is null && DoctrineWords.Preview(DoctrineVocabulary.WhenLost(1), TryOn(Tk(ArmyOrderKind.Age))) is null
        && DoctrineWords.Preview(Tk(ArmyOrderKind.Aura, 3), TryOn(Tk(ArmyOrderKind.Aura, 3))) is null && TryOn(Tk(ArmyOrderKind.Aura, 3)) is null);

    // ---- Reforge: the cost is the army's, twice undoes the traits, not the gold
    {
        bool costOk = true; var bad = new List<string>();
        for (int tk = 1; tk < DoctrineVocabulary.Size; tk++)
        {
            if (DoctrineVocabulary.Decode(tk).Family != OrderFamily.Army) continue;
            var t = TryOn(tk); if (t is null) continue;
            int spent = t.Before.Gold - t.After.Gold;
            int? shown = DoctrineWords.CostNow(tk, t.Before, t);
            if (shown != (spent > 0 ? spent : null)) { costOk = false; bad.Add($"{DoctrineVocabulary.Name(tk)}: shown {shown}, took {spent}"); }
        }
        Check("reforge: the cost the card shows is the gold the army really takes for the order (every order for the whole host, on a hand-built host)", costOk, string.Join(" | ", bad.Take(3)));
        int costly = Enumerable.Range(0, 8).First(b => TryOn(Tk(ArmyOrderKind.Flip, b)) is { } o && o.Before.Gold > o.After.Gold);   // a trait that costs gold to take up on this host
        svc.TryParse(hostCode, out var twice, out _);
        var r0 = twice!.Read();
        twice.ApplyArmyOrdersAsync(DoctrineSet.Of(Tk(ArmyOrderKind.Flip, costly), Tk(ArmyOrderKind.Flip, costly)), CancellationToken.None).AsTask().Wait();
        var r2 = twice.Read();
        var once = TryOn(Tk(ArmyOrderKind.Flip, costly))!;
        Check("reforge: given twice the traits are back as they were and the gold is not (the sentence says so)",
            r2.Cohorts.Select(c => (c.Key, c.Traits)).SequenceEqual(r0.Cohorts.Select(c => (c.Key, c.Traits))) && once.Before.Gold - once.After.Gold > 0 && r0.Gold - r2.Gold == once.Before.Gold - once.After.Gold
            && DoctrineWords.Sentence(Tk(ArmyOrderKind.Flip, costly)).Contains("Giving it twice undoes the traits, not the gold."), $"flip bit {costly}: gold {r0.Gold} -> {r2.Gold}");
        // the card of an order the purse cannot pay still says what it costs, and what is in the purse
        Check("reforge: a card the purse cannot pay says the cost and the purse", DoctrineWords.CostWords(9, 6) == "Costs 9 gold; you have 6." && DoctrineWords.CostWords(4, 6) == "Costs 4 gold.");
    }

    // ---- Drill the host: the figures are the rule's
    {
        string[] ordHand = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];
        string age = DoctrineWords.Sentence(Tk(ArmyOrderKind.Age)), ageFx = DoctrineWords.Effect(Tk(ArmyOrderKind.Age));
        var t = TryOn(Tk(ArmyOrderKind.Age))!;
        svc.TryParse(hostCode, out var ring, out _);
        for (int i = 0; i < Roster.VetPeriod; i++) ring!.ApplyArmyOrdersAsync(DoctrineSet.Of(Tk(ArmyOrderKind.Age)), CancellationToken.None).AsTask().Wait();
        var rr = ring!.Read();
        var stated = Regex.Matches(age + " " + ageFx, @"\d+").Select(m => int.Parse(m.Value)).Distinct().ToList();
        Check("age: a year adds the power the sentence states (read from the rule), every cohort moves by it, and after the ring's whole length the cohorts are back at their first year",
            t.After.Cohorts.All(c => c.PowerPct - t.Before.Cohorts.First(x => x.Key == c.Key).PowerPct == Roster.VetStepPct) && rr.Cohorts.All(c => c.Veterancy == 0)
            && age.Contains($"each year adds {Roster.VetStepPct}% power") && age.Contains($"The {ordHand[Roster.VetPeriod]} drill sends a cohort back to its first year.") && ageFx.Contains($"{Roster.VetStepPct}% power")
            && stated.SequenceEqual([Roster.VetStepPct]), $"numbers in the text: {string.Join(",", stated)}");
    }

    // ---- Raise the banner: as many percent as carry it, up to the cap
    {
        int? capHand = null;
        if (svc.TryParse("DA2.0.0.5000.0..4:300:2:0", out var big, out _)) { big!.ApplyArmyOrdersAsync(DoctrineSet.Of(Tk(ArmyOrderKind.Aura, 3)), CancellationToken.None).AsTask().Wait(); capHand = big.Read().Cohorts[0].PowerPct - 100; }
        var small = TryOn(Tk(ArmyOrderKind.Aura, 3), "DA2.0.0.500.0..4:17:1:0");
        string aura = DoctrineWords.Sentence(Tk(ArmyOrderKind.Aura, 3)), auraFx = DoctrineWords.Effect(Tk(ArmyOrderKind.Aura, 3));
        var stated = Regex.Matches(aura + " " + auraFx, @"\d+").Select(m => int.Parse(m.Value)).Distinct().ToList();
        Check("banner: the cap in the text is the army's own (a 300-soldier host's bonus), and a bonus under the cap is as many percent as soldiers carry it (17 carry it: 17 percent)",
            capHand is > 0 && aura.Contains($"up to {capHand}%") && auraFx.Contains($"up to {capHand}%") && stated.SequenceEqual([capHand!.Value]) && DoctrineWords.AuraCap == capHand
            && small is not null && small.After.Cohorts[0].PowerPct - small.Before.Cohorts[0].PowerPct == 17 && aura.Contains("by as many percent as you have soldiers with it") && aura.Contains("It ends with the round."), $"cap {capHand}, text numbers {string.Join(",", stated)}");
    }

    // ---- Swear into: what the sentence says the army does
    {
        var t = TryOn(Tk(ArmyOrderKind.Merge, 0))!;
        var into = t.After.Cohorts.Single(c => c.Key == 0);
        Check("swear: the soldiers of the next kind become the kind named, with its traits and place in the line, and their cohort is no more",
            into.Count == 8 && into.Slot == 0 && into.Traits == Trait.None && t.After.Cohorts.All(c => c.Key != 1)
            && DoctrineWords.Sentence(Tk(ArmyOrderKind.Merge, 0)) == "Swear into the Spearmen: all the Shieldwall become Spearmen, with the Spearmen's traits and place in the line."
            && DoctrineWords.Effect(Tk(ArmyOrderKind.Merge, 0)) == "The Shieldwall swear into the Spearmen.");
    }

    // ---- Should a quarter fall: the slot-order rule is the vocabulary's
    {
        int advTk = DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), holdTk = DoctrineVocabulary.Field(FieldOrderKind.Hold, 1);
        var set = DoctrineSet.Of(advTk, DoctrineVocabulary.WhenLost(2), holdTk);
        var opening = DoctrineVocabulary.FieldOrders(set); var trig = DoctrineVocabulary.Trigger(set);
        string rule = DoctrineWords.TriggerRule(), sen = DoctrineWords.Sentence(DoctrineVocabulary.WhenLost(2));
        Check("trigger: orders before the trigger open the battle and orders after it take over once the stated share of the host has fallen (the vocabulary's own split), and the rule is said in one plain sentence",
            opening.Length == 1 && opening[0].Kind == FieldOrderKind.Advance && trig is { Quarters: 2 } && trig.Then.Count == 1 && trig.Then[0].Kind == FieldOrderKind.Hold && trig.Then[0].Group == 1
            && Enumerable.Range(1, 3).All(q => DoctrineVocabulary.Trigger(DoctrineSet.Of(DoctrineVocabulary.WhenLost(q)))!.Quarters == q)
            && rule == "Orders before it open the battle; orders after it take over once your host has lost a quarter, half or three quarters of its soldiers, as the card says (a line they do not name presses on)."
            && sen == "Should half of the host fall, then the orders after this one take over: orders before it open the battle, orders after it take over once your host has lost half of its soldiers, and a line they do not name presses on."
            && DoctrineWords.Effect(DoctrineVocabulary.WhenLost(1)) == "Should a quarter of the host fall, then:" && DoctrineWords.Effect(DoctrineVocabulary.WhenLost(3)) == "Should three quarters of the host fall, then:", sen);
    }

    // ---- no figure is typed inside a sentence
    {
        string src = Path.Combine(showroom, "Services", "Doctrine");
        var typed = new List<string>();
        foreach (var f in new[] { "DoctrineWords.cs", "DoctrineProgress.cs", "DoctrineGuide.cs", "BattleChronicle.cs", "CampaignWords.cs" })
        {
            string code = Regex.Replace(File.ReadAllText(Path.Combine(src, f)), @"//[^\n]*", "");
            foreach (var lit in Lex.StringTexts(code))
            {
                if (lit.StartsWith("DA2.")) continue;                                  // the throwaway host the banner's cap is read from
                if (Regex.IsMatch(lit, @"\d")) typed.Add(f + ": " + lit);
            }
        }
        Check("wording (numbers): no figure is typed inside a sentence of the words, the unlock announcement or the guide (every number is read from the rules)", typed.Count == 0, string.Join(" | ", typed.Take(3)));
    }

    // ---- the offers are ON on this page (the draft and the council show them, so nothing the visitor does is refused unseen)
    {
        var rulesOn = DoctrineSession.GameRules(3);
        string sessionSrc = File.ReadAllText(Path.Combine(showroom, "Services", "Doctrine", "DoctrineSession.cs"));
        // free-choice hands (what the page used to be) are refused by the game under these rules: the page cannot be one
        var free = await new DoctrineSections().Build(new FreeHands(), DoctrineSession.GameRules(1)).PlayAsync(gateSeed);
        var scripted = await new DoctrineSections().Build(new ScriptHands([ScriptHands.How.Manual]), DoctrineSession.GameRules(1)).PlayAsync(gateSeed);
        Console.WriteLine($"  (offers ON, seed {gateSeed}: free-choice hands: {free.Rounds[0].OfferRefused} refused; the offer-bound hands the page is: {scripted.Rounds[0].OfferRefused} refused)");
        Check("offers: the page's rules are the game's own with the offers ON and enforced (no Off), the pools only when the ladder gives them; hands that ignore the offer are refused by the game, hands bound to it are not",
            rulesOn.Offers.Enabled && rulesOn.Offers.Enforce && rulesOn.MaxRounds == 3 && !sessionSrc.Contains("OfferRules.Off") && free.Rounds[0].OfferRefused > 0 && scripted.Rounds[0].OfferRefused == 0);
    }

    // ---- the words a player plays by carry no builder's term; the technical paragraph is behind an expander
    {
        string play = string.Join("\n", playText);
        var banned = new (string Name, string Pattern)[]
        {
            ("Pleroma", @"Pleroma"), ("store", @"\bstores?\b"), ("operation", @"\boperations?\b"), ("relabel", @"[Rr]elabel"), ("arrangement", @"\barrangements?\b"), ("flag", @"\bflags?\b"),
            ("veterancy", @"[Vv]eterancy"), ("legal", @"\blegal\b"), ("command span", @"command span"),
            ("an old order name", @"\b(Rotate|Flip|Aura|Merge|Cull|Advance|Hunt|Flank)\b|\bHold\b(?! the line| back)|\bGuard\b(?! the host)"),
        };
        foreach (var (name, pattern) in banned)
        {
            var m = Regex.Match(play, pattern);
            Check($"wording (play UI): no '{name}' term in the words a player plays by", !m.Success, m.Success ? "..." + play.Substring(Math.Max(0, m.Index - 50), Math.Min(play.Length - Math.Max(0, m.Index - 50), 110)) + "..." : "");
        }
        string all = string.Join("\n", allText);
        var built = allMarkup.Select(mk => Regex.Match(mk, "<details class=\"dc-about dc-built\"[^>]*>(.*?)</details>", RegexOptions.Singleline)).FirstOrDefault(x => x.Success);
        Check("wording (play UI): the technical paragraph stays, behind a closed expander labelled 'How it is built' (Pleroma, stores, operations are named only there)",
            built is not null && Regex.IsMatch(built.Groups[1].Value, @"<summary[^>]*>How it is built</summary>") && Regex.IsMatch(built.Groups[1].Value, "Pleroma") && Regex.IsMatch(built.Groups[1].Value, "operation")
            && !Regex.IsMatch(built.Value.Substring(0, built.Value.IndexOf('>')), @"\sopen\b") && all.Contains("How it is built"));
    }
}

// -------------------------------------------------------------------------------- layout rules (no browser needed)
{
    string css = File.ReadAllText(Path.Combine(showroom, "Pages", "DoctrinePage.razor.css"));
    string acss = File.ReadAllText(Path.Combine(showroom, "Components", "DoctrineArena.razor.css"));
    string ccss = File.ReadAllText(Path.Combine(showroom, "Components", "DoctrineChronicle.razor.css"));
    string both = css + "\n" + acss + "\n" + ccss;
    Check("chronicle css: lines and the toggle get the 44px minimum, the live box is column-reverse (newest at the bottom, pinned), and it fills the docked panel on a wide screen",
        Regex.IsMatch(ccss, @"\.dc-logbtn, \.dc-logtoggle \{[^}]*min-height: 44px;[^}]*min-width: 44px;") && Regex.IsMatch(ccss, @"\.dc-log\.live \{[^}]*flex-direction: column-reverse") && Regex.IsMatch(ccss, @"min-width: 900px\) \{[\s\S]*\.dc-log \{[^}]*flex: 1 1 auto"));
    Check("chronicle layout: on a wide screen it takes the general's column on the watch screen (the general panel steps aside), and on a phone it is a sheet like the others",
        Regex.IsMatch(css, @"\.dc-generalpanel, \.dc-chroniclepanel \{[^}]*grid-column: 3;[^}]*grid-row: 1 / -1") && Regex.IsMatch(css, @"\.dc-hud\[data-stage=watch\] \.dc-generalpanel \{ display: none; \}") && Regex.IsMatch(css, @"\.dc-sheet, \.dc-tray \{ position: absolute;"));
    Check("council layout: on a wide screen the muster takes the field's column in full (both rows, the army list stays beside it) and the council takes the army's and the field's columns (the formations list who stands where), scrolling inside; the council is always there on a wide screen and a sheet that can be put away on a phone; the formations are three columns",
        Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-draft \{[^}]*grid-column: 2;[^}]*grid-row: 1 / -1") && Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-tray, \.dc-council:not\(\.open\) \{[^}]*display: flex;[^}]*grid-column: 1 / 3;[^}]*grid-row: 1 / -1")
        && Regex.IsMatch(css, @"\.dc-council:not\(\.open\) \{ display: none; \}") && Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-formgrid \{[^}]*repeat\(3, minmax\(0, 1fr\)\)") && !Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-tray \{[^}]*align-self: end"));
    Check("offers layout: the packs and the formations are one column on a phone and two and three on a wide screen; the slots with their arrows take two columns in portrait on a phone",
        Regex.IsMatch(css, @"\.dc-packs \{[^}]*grid-template-columns: minmax\(0, 1fr\)") && Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-packs \{ grid-template-columns: repeat\(2, minmax\(0, 1fr\)\)") && Regex.IsMatch(css, @"\.dc-formgrid \{[^}]*grid-template-columns: minmax\(0, 1fr\)")
        && Regex.IsMatch(css, @"max-width: 699px\) \{\s*\.dc-slots \{[^}]*repeat\(2, minmax\(0, 1fr\)\)"));
    {
        string rl = css.Replace("\r\n", "\n");
        int firstHas = rl.IndexOf(".dc-hud:has("), lastHas = rl.LastIndexOf(".dc-hud:has(");
        int mediaStart = firstHas < 0 ? -1 : rl.LastIndexOf("@media", firstHas), mediaEnd = mediaStart < 0 ? -1 : rl.IndexOf("\n}", mediaStart);
        Check("council layout (phone): with a sheet, the muster or the council open the top bar gives up its goal and to-do line, the recap gives way to the chronicle, the slots are put away in the draft and take one row (arrows away) while a sheet or the council is open, and the Council button goes while the council is open, so the cards get the height; every one of these rules is inside the one below-700px query (the desktop is untouched)",
            Regex.IsMatch(rl, @"@media \(max-width: 699px\) \{\n\s*\.dc-hud\[data-stage=draft\] \.dc-slots \{ display: none; \}\n\s*\.dc-hud:has\(\.dc-sheet\.open, \.dc-tray\.open\) \.dc-goal, \.dc-hud:has\(\.dc-sheet\.open, \.dc-tray\.open\) \.dc-todo \{ display: none; \}\n\s*\.dc-hud:has\(\.dc-chroniclepanel\.open\) \.dc-recap \{ display: none; \}")
            && Regex.IsMatch(rl, @"\.dc-hud:has\(\.dc-sheet\.open, \.dc-tray\.open\) \.dc-slots \{ grid-auto-flow: column; grid-template-columns: none;") && Regex.IsMatch(rl, @"#dc-orders \{ display: none; \}")
            && mediaStart >= 0 && rl.Substring(mediaStart).StartsWith("@media (max-width: 699px) {") && lastHas < mediaEnd);
    }
    Check("offers layout: the arrows and the formation and pack cards keep the 44px rule (the page rule covers every button) and the cards wrap long words instead of widening", Regex.IsMatch(css, @"\.dc-pack \{[^}]*overflow-wrap: anywhere") && Regex.IsMatch(css, @"\.dc-form \{[^}]*overflow-wrap: anywhere") && Regex.IsMatch(css, @"\.dc-mv \.dc-btn \{[^}]*padding: 0"));
    string index = File.ReadAllText(Path.Combine(showroom, "wwwroot", "index.html"));
    Check("viewport meta: width=device-width, initial-scale=1", Regex.IsMatch(index, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1(\\.0)?\""));
    Check("one rule gives every page button and summary a 44px minimum", Regex.IsMatch(css, @"\.dc button, \.dc summary \{[^}]*min-height: 44px;[^}]*min-width: 44px;"));
    Check("the replay's buttons and scrubber get the same 44px minimum", Regex.IsMatch(acss, @"\.dc-replay button, \.dc-replay input\[type=range\] \{[^}]*min-height: 44px;[^}]*min-width: 44px;"));
    Check("touch-action: manipulation on tap targets", css.Contains("touch-action: manipulation") && acss.Contains("touch-action: manipulation"));
    var fixedWidths = Regex.Matches(both, @"(?<![-\w])width\s*:\s*(\d+)px").Select(m => int.Parse(m.Groups[1].Value)).ToList();
    Check("no fixed width above 360px", fixedWidths.All(w => w <= 360), "fixed px widths: " + string.Join(",", fixedWidths));
    Check("no min-width in px above 360", Regex.Matches(both, @"(?<!\()min-width\s*:\s*(\d+)px").All(m => int.Parse(m.Groups[1].Value) <= 360));
    Check("media queries present (wide screens, narrow phones, landscape, reduced motion)", css.Contains("@media (min-width: 700px)") && css.Contains("@media (min-width: 900px)") && css.Contains("@media (max-width: 359px)") && css.Contains("orientation: landscape") && css.Contains("prefers-reduced-motion") && acss.Contains("prefers-reduced-motion"));
    Check("16px side gutters with safe-area insets", css.Contains("max(16px, env(safe-area-inset-left))") && css.Contains("max(16px, env(safe-area-inset-right))") && css.Contains("safe-area-inset-bottom"));
    Check("no hover-only rules", !both.Contains(":hover"));
    Check("HUD desktop: army, field and general columns on a wide screen, the dock across the bottom", Regex.IsMatch(css, @"@media \(min-width: 900px\) \{[\s\S]*\.dc-stage \{[^}]*grid-template-columns: minmax\(220px, 280px\) minmax\(0, 1fr\) minmax\(220px, 300px\)") && Regex.IsMatch(css, @"min-width: 900px\) \{[\s\S]*\.dc-sheetbtns \{ display: none; \}"));
    Check("HUD: the scene is as high as the screen below the site bar (dvh), so the field, dock and GO need no page scroll", Regex.IsMatch(css, @"\.dc-hud \{[^}]*calc\(100dvh - 5rem\)"));
    Check("phone: the army, general and draft panels are sheets that wait below the stage and rise when opened", Regex.IsMatch(css, @"\.dc-sheet \{[^}]*transform: translateY\(105%\);[^}]*visibility: hidden") && Regex.IsMatch(css, @"\.dc-sheet\.open \{[^}]*visibility: visible"));
    Check("phone: the dock keeps the bottom safe-area inset", Regex.IsMatch(css, @"\.dc-dock \{[^}]*safe-area-inset-bottom"));
    Check("phone: the handles take a drag (touch-action: none on the grip and the sheet buttons)", Regex.IsMatch(css, @"\.dc-grip \{[^}]*touch-action: none") && Regex.IsMatch(css, @"\.dc-sheetbtn \{[^}]*touch-action: none"));
    Check("phone: dock slots and dock buttons are 56px and 48px tall, at least 44px", Regex.IsMatch(css, @"\.dc-slot \{[^}]*min-height: 56px") && Regex.IsMatch(css, @"\.dc-dockbtns \.dc-btn \{[^}]*min-height: 48px"));
    Check("phone: a phone on its side puts the top bar and dock in a column beside the field", Regex.IsMatch(css, @"orientation: landscape\) and \(max-height: 500px\) and \(max-width: 899px\) \{[\s\S]*\.dc-hud \{ display: grid"));
    Check("no timer anywhere in the game's page", !Regex.IsMatch(File.ReadAllText(Path.Combine(showroom, "Pages", "DoctrinePage.razor")) + string.Join(" ", allText), @"\b(timer|countdown|time limit|seconds left)\b", RegexOptions.IgnoreCase));
    Check("the page cannot scroll sideways (overflow-x: clip on .room), and the arena scales to its box", css.Contains("overflow-x: clip") && Regex.IsMatch(acss, @"\.dc-arena \{[^}]*width: 100%;[^}]*height: auto"));
    Check("the base grids are the phone grids (two chip columns, two slot columns, three radio options)", Regex.IsMatch(css, @"\.dc-chipgrid \{[^}]*repeat\(2, minmax\(0, 1fr\)\)") && Regex.IsMatch(css, @"\.dc-slots \{[^}]*grid-auto-columns: minmax\(0, 1fr\)") &&Regex.IsMatch(css, @"\.dc-opts \{[^}]*repeat\(3, minmax\(0, 1fr\)\)"));
    Check("inputs are 16px with a 48px minimum (the phone does not zoom on focus)", Regex.IsMatch(css, @"\.dc-input \{[^}]*min-height: 48px;[^}]*font-size: 16px;"));
    Check("the animated blob moves by custom properties and a CSS transition that reduced motion switches off", Regex.IsMatch(acss, @"\.dc-blob \{[^}]*transform: translate\(calc\(var\(--x\) \* 1px\), calc\(var\(--y\) \* 1px\)\);[^}]*transition: transform") && Regex.IsMatch(acss, @"prefers-reduced-motion: reduce\) \{\s*\.dc-blob \{ transition: none; \}"));
    foreach (var (nm, p) in new[] { ("game A", page), ("game B", pageB) })
    {
        Check($"every handler in the rendered page is on a button, summary, input or textarea ({nm})", p.FindAll("[onclick], [onmouseover], [oncontextmenu]").All(e => e.TagName is "BUTTON" or "SUMMARY"));
        Check($"no title attributes (no hover tooltips) ({nm})", p.FindAll("[title]").Count == 0);
        Check($"no fixed-px inline styles in the markup ({nm})", !Regex.IsMatch(p.Markup, @"style=""[^""]*\d+px"));
    }
    Check("no title attribute or px inline style at any screen scanned", allMarkup.All(m => !Regex.IsMatch(m, @"\stitle=""") && !Regex.IsMatch(m, @"style=""[^""]*\d+px")));
    Check("the SVG arena has a text alternative", allMarkup.Any(m => Regex.IsMatch(m, @"<svg[^>]*role=""img""[^>]*aria-label=""Arena at tick")));
}

Check($"ground (briefing): in every round of every page game ({groundChecked} screens and results) the top bar named the ground the battle was then fought on and who besieged it (against a hand-written draw, names and clocks), the game's own answer agreed, the muster said it is dealt once, the record's ground and besieger were the briefing's, and the result screen said the siege and the clock in the recording's own words",
    groundChecked >= 14 && groundProblems.Count == 0, string.Join(" | ", groundProblems.Take(3)));

// --dump <file>: write the visible text of every screen the proof reached (for the coordinator to show the user)
if (args.Length >= 2 && args[0] == "--dump") File.WriteAllText(args[1], string.Join("\n\n==== next screen ====\n\n", allText));
Console.WriteLine($"  (game A took {roundTimes.FirstOrDefault():F0} ms to the first result on this machine, desktop, not a phone)");
Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;

sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }

/// <summary>The text of every string literal in some C# code (the parts outside the braces of an interpolated string; a literal inside an interpolation is its own entry).</summary>
static class Lex
{
    public static List<string> StringTexts(string code)
    {
        var all = new List<string>();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '\'') { i++; while (i < code.Length && code[i] != '\'') i += code[i] == '\\' ? 2 : 1; i++; }
            else if (c == '"') { all.Add(Str(code, ref i, false, all)); }
            else if (c == '$' && i + 1 < code.Length && code[i + 1] == '"') { i++; all.Add(Str(code, ref i, true, all)); }
            else i++;
        }
        return all;
    }

    // i is on the opening quote; returns the text outside any braces and leaves i after the closing quote.
    static string Str(string code, ref int i, bool interpolated, List<string> all)
    {
        i++;
        var sb = new System.Text.StringBuilder();
        while (i < code.Length && code[i] != '"')
        {
            if (code[i] == '\\') { sb.Append(code[i]); i += 2; continue; }
            if (interpolated && code[i] == '{')
            {
                if (i + 1 < code.Length && code[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                int depth = 1; i++;
                while (i < code.Length && depth > 0)
                {
                    char e = code[i];
                    if (e == '{') { depth++; i++; }
                    else if (e == '}') { depth--; i++; }
                    else if (e == '"') all.Add(Str(code, ref i, false, all));
                    else if (e == '$' && i + 1 < code.Length && code[i + 1] == '"') { i++; all.Add(Str(code, ref i, true, all)); }
                    else i++;
                }
                continue;
            }
            sb.Append(code[i]); i++;
        }
        i++;
        return sb.ToString();
    }
}

/// <summary>Keeps the set-up of every battle a game starts (the set-up carries both sides' cohorts with the traits they fought with).</summary>
sealed class SetupTap : IRoundObserver
{
    public List<BattleSetup> Setups { get; } = new();
    public void BattleStarted(BattleSetup setup) => Setups.Add(setup);
}

/// <summary>A visitor who drafts a mix of kinds (priests, wights, wolves, riders among them) and gives orders that move cohorts around (flank, guard, hunt, fall back), varying with the seed.</summary>
sealed class VarietyHands(int variant) : IPlayerInput
{
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
    {
        int gold = army.Gold; var picks = new List<DraftPick>();
        int[] order = variant % 2 == 0 ? [4, 6, 2, 5, 3, 0] : [6, 4, 5, 1, 2, 0];
        foreach (var k in order) { int n = Math.Min(gold / Roster.PriceOf(k), 2); if (n > 0) { picks.Add(new DraftPick(k, n)); gold -= n * Roster.PriceOf(k); } }
        return ValueTask.FromResult<IReadOnlyList<DraftPick>>(picks);
    }

    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
    {
        int[] want = ((variant + round) % 4) switch
        {
            0 => new[] { DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), DoctrineVocabulary.Field(FieldOrderKind.Hunt, 2) },
            1 => new[] { DoctrineVocabulary.Field(FieldOrderKind.Flank, 0), DoctrineVocabulary.Field(FieldOrderKind.Hold, 2), DoctrineVocabulary.Field(FieldOrderKind.Guard, 1) },
            2 => new[] { DoctrineVocabulary.Field(FieldOrderKind.FallBack, 0), DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Army(ArmyOrderKind.Age) },
            _ => [],
        };
        var legal = want.Where(army.Legal.Allows).Take(DoctrineVocabulary.MaxSlots).ToArray();
        return ValueTask.FromResult(new PlayerChoice(DoctrineSet.Of(legal), false));
    }
}

/// <summary>
/// A visitor who drafts like <see cref="VarietyHands"/>; in an even round (the player besieges, when the ground is a siege) holds all three lines so that the siege clock can fall,
/// in an odd round (the enemy besieges) gives the variety orders.
/// </summary>
sealed class SiegeHands(int variant) : IPlayerInput
{
    readonly VarietyHands _v = new(variant);
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => _v.DraftAsync(army, round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
        => round % 2 != 0 ? _v.ChooseAsync(army, ghost, round, ct)
            : ValueTask.FromResult(new PlayerChoice(DoctrineSet.Of(Enumerable.Range(0, 3).Select(g => DoctrineVocabulary.Field(FieldOrderKind.Hold, g)).Where(army.Legal.Allows).ToArray()), false));
}

/// <summary>
/// The independent check of a chronicle: it looks every line's cite up again in the recording, with its own code, and checks what the text says of it
/// (the side and the kind it names, the numbers, the tick). It returns the problems; none means every line is backed.
/// </summary>
static class ChronicleCheck
{
    const int Q = BattleReplay.Quantum;
    static readonly string[] SlotNames = ["Front", "Middle", "Back"];
    /// <summary>The names of the six orders for a line, written out here by hand (not read from the words under test).</summary>
    public static readonly string[] HandNames = ["Press on", "Hold the line", "Ride the flank", "Fall back", "Guard the host", "Run them down"];
    static string Nm(Func<int, string> kn, ReplayBlob b) => (b.Side == Side.Player ? "your " : "the enemy ") + kn(b.Key);
    static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
    static ReplayBlob? B(ReplayFrame f, int id) { foreach (var b in f.Blobs) if (b.Id == id) return b; return null; }
    static int Sum(ReplayFrame f, Side s) => f.Blobs.Where(b => b.Side == s).Sum(b => b.Strength);
    static int Standing(ReplayFrame f, Side s) => f.Blobs.Count(b => b.Side == s && b.Strength > 0);
    static bool GroupDown(ReplayFrame f, Side s, int slot) => f.Blobs.Where(b => b.Side == s && b.Slot == slot).All(b => b.Strength <= 0);
    static bool AnyGroupDown(ReplayFrame f) => f.Blobs.Any(b => GroupDown(f, b.Side, b.Slot));

    /// <summary>The blobs that broke (strength above zero before, zero now) between frame i-1 and i, lowest id first.</summary>
    static List<ReplayBlob> Broke(BattleReplay r, int i) => r.Frames[i].Blobs.Where(b => b.Strength <= 0 && B(r.Frames[i - 1], b.Id) is { Strength: > 0 }).OrderBy(b => b.Id).ToList();

    const int ClockBit = 2;                 // WorldFlag.ClockFell, written out by hand
    static readonly string[] SetWords = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight"];

    /// <summary>The bonus a dug-in cohort of these traits gets, written out by hand from the battle's tables: a Machine's reach is the longest (nothing to gain), a far shooter gains the difference, the rest gain the longest edge.</summary>
    public static int HandBonus(Trait t) => (t & Trait.Machine) != 0 ? 0 : (t & Trait.Ranged) != 0 ? 12 : 32;

    /// <summary>
    /// Is cohort <paramref name="id"/> PROVABLY dug in for the first time at frame <paramref name="at"/>? Written apart from the chronicle: a reference frame, six frames since it without a strike on the cohort
    /// and within a melee reach (35.2 sixteenths) of it, alive throughout; a distance within 1.5 of the reach makes the cohort unprovable until a strike resets it.
    /// </summary>
    public static bool DugInAt(BattleReplay r, int id, int at)
    {
        var F = r.Frames;
        if (at < 6 || at >= F.Count || B(F[0], id) is not { } b0) return false;
        int start = 0; bool unknown = false; int rx = b0.X, ry = b0.Y;
        for (int j = 1; j <= at; j++)
        {
            if (B(F[j], id) is not { Strength: > 0 } bj) return false;
            if (F[j].Events.Any(e => e.Target == id)) { start = j; rx = bj.X; ry = bj.Y; unknown = false; continue; }
            if (unknown) continue;
            double d = Math.Sqrt(Math.Pow(bj.X - rx, 2) + Math.Pow(bj.Y - ry, 2));
            if (d > 36.7) { start = j; rx = bj.X; ry = bj.Y; }
            else if (d >= 33.7) unknown = true;
        }
        return !unknown && at - start == 6;
    }

    public static List<string> Verify(Chronicle c, BattleReplay r, Func<int, string> kn, Func<Side, int, Trait?>? traits, int siegeClock = 0)
    {
        var bad = new List<string>();
        var F = r.Frames;
        var idx = new Dictionary<int, int>();
        for (int k = 0; k < F.Count; k++) idx[F[k].Tick] = k;
        int lastTick = int.MinValue;
        foreach (var l in c.Lines)
        {
            string w = $"[{l.Kind} at tick {l.Tick}]";
            if (l.Tick < lastTick) bad.Add(w + " out of time order");
            lastTick = l.Tick;
            var ck = l.Cite;
            if (ck.Tick != l.Tick) { bad.Add($"{w} its cite is for tick {ck.Tick}"); continue; }
            if (!idx.TryGetValue(l.Tick, out int i)) { bad.Add(w + " no such frame"); continue; }
            var f = F[i]; var p = i > 0 ? F[i - 1] : null;
            string text = l.Text, low = text.ToLowerInvariant();
            bool kindOk = l.Kind switch
            {
                ChronicleKind.Start => ck.Kind == CiteKind.Start,
                ChronicleKind.Siege => ck.Kind == CiteKind.Siege,
                ChronicleKind.Clock => ck.Kind == CiteKind.Clock,
                ChronicleKind.DugIn => ck.Kind == CiteKind.Set,
                ChronicleKind.Contact or ChronicleKind.Flank or ChronicleKind.Holy or ChronicleKind.Blow => ck.Kind == CiteKind.Event,
                ChronicleKind.Gate => ck.Kind == CiteKind.Gate,
                ChronicleKind.Run => ck.Kind == CiteKind.Run,
                ChronicleKind.Break => ck.Kind == CiteKind.Break,
                ChronicleKind.Stand => ck.Kind == CiteKind.Stand,
                ChronicleKind.Lull => ck.Kind == CiteKind.Span,
                ChronicleKind.End => ck.Kind == CiteKind.End,
                _ => false,
            };
            if (!kindOk) { bad.Add($"{w} the cite is of the wrong kind ({ck.Kind})"); continue; }
            switch (ck.Kind)
            {
                case CiteKind.Start:
                    if (i != 0 || ck.A != Sum(f, Side.Player) || ck.B != Sum(f, Side.Enemy) || !text.Contains($"{ck.A} of your soldiers") || !text.Contains($"enemy's {ck.B}.")) bad.Add(w + " the opening does not match frame 0");
                    break;
                case CiteKind.Event:
                {
                    var s = B(f, ck.A); var t = B(f, ck.B);
                    if (!f.Events.Any(e => e.Source == ck.A && e.Target == ck.B && e.Damage == ck.Value) || s is null || t is null) { bad.Add($"{w} no such strike ({ck.A} on {ck.B} for {ck.Value}) in the frame"); break; }
                    var sb = s.Value; var tb = t.Value;
                    if (!low.Contains(Nm(kn, sb).ToLowerInvariant()) || !low.Contains(Nm(kn, tb).ToLowerInvariant())) bad.Add($"{w} the text does not name {Nm(kn, sb)} and {Nm(kn, tb)}");
                    switch (l.Kind)
                    {
                        case ChronicleKind.Contact:
                        {
                            bool you = f.Events.Any(e => B(f, e.Source) is { Side: Side.Player }), they = f.Events.Any(e => B(f, e.Source) is { Side: Side.Enemy });
                            if (F.First(x => x.Events.Count > 0).Tick != l.Tick || !text.StartsWith("First contact. ")) bad.Add(w + " not the first tick with a strike");
                            else if (text.Contains("Both sides")) { if (!(you && they) || !text.Contains($": {Nm(kn, sb)} on {Nm(kn, tb)}")) bad.Add(w + " 'both sides' but the frame does not show both striking"); }
                            else if (text.Contains("the enemy has not struck yet")) { if (!(you && !they && sb.Side == Side.Player) || !text.StartsWith($"First contact. {Cap(Nm(kn, sb))} strike {Nm(kn, tb)},")) bad.Add(w + " says the enemy has not struck but the frame shows it did, or names the wrong striker"); }
                            else if (text.Contains("you have not struck yet")) { if (!(they && !you && sb.Side == Side.Enemy) || !text.StartsWith($"First contact. {Cap(Nm(kn, sb))} strike {Nm(kn, tb)},")) bad.Add(w + " says you have not struck but the frame shows you did, or names the wrong striker"); }
                            else bad.Add(w + " says neither who struck first nor that both did");
                            break;
                        }
                        case ChronicleKind.Flank:
                        {
                            var s0 = B(F[0], ck.A);
                            int across = s0 is null ? 0 : Math.Abs(sb.Y - s0.Value.Y), dx = Math.Abs(sb.X - tb.X), dy = Math.Abs(sb.Y - tb.Y);
                            if (s0 is null || across < 8 * Q || dy < dx || !text.Contains($"from the side, {across / Q} units across the field from where they began.")) bad.Add($"{w} the positions do not show a flank (across {across / (double)Q}, beside {dy >= dx})");
                            break;
                        }
                        case ChronicleKind.Holy:
                        {
                            var ts = traits?.Invoke(sb.Side, sb.Key); var tt = traits?.Invoke(tb.Side, tb.Key);
                            if (ts is null || tt is null || (ts.Value & Trait.Holy) == 0 || (tt.Value & Trait.Cursed) == 0) bad.Add(w + " the cohorts' traits are not holy against cursed");
                            else if (!text.Contains("holy blows hurt the cursed half again as hard")) bad.Add(w + " wrong factor in words");
                            break;
                        }
                        case ChronicleKind.Blow:
                        {
                            int before = p is null ? 0 : B(p, ck.B)?.Strength ?? 0;
                            if (ck.Value < 5 || ck.Value * 4 < before || !text.Contains($" for {ck.Value}.") || !(text.StartsWith("The heaviest blow of the battle: ") || text.StartsWith("A heavy blow: "))) bad.Add($"{w} not a heavy blow ({ck.Value} on a cohort of {before})");
                            break;
                        }
                    }
                    break;
                }
                case CiteKind.Break:
                {
                    var b = B(f, ck.A);
                    if (b is null || b.Value.Strength != 0 || p is null || B(p, ck.A) is not { Strength: > 0 }) { bad.Add(w + " that cohort did not break at this tick"); break; }
                    var bb = b.Value;
                    if (!text.StartsWith(Cap(Nm(kn, bb)) + " break.")) bad.Add(w + " names the wrong cohort or side");
                    var broke = Broke(r, i);
                    bool earlierBreak = false; for (int j = 1; j < i; j++) if (Broke(r, j).Count > 0) earlierBreak = true;
                    if (text.Contains("The first to fall.") != (!earlierBreak && broke[0].Id == bb.Id)) bad.Add(w + " 'the first to fall' is wrong");
                    bool gone = GroupDown(f, bb.Side, bb.Slot);
                    string lineText = $"{Cap(bb.Side == Side.Player ? "your" : "the enemy")} {SlotNames[bb.Slot]} line is gone";
                    if (text.Contains(" line is gone") != gone || (gone && !text.Contains(lineText))) bad.Add(w + " the line-gone sentence is wrong");
                    if (gone)
                    {
                        bool anotherGoneNow = broke.TakeWhile(x => x.Id != bb.Id).Any(x => GroupDown(f, x.Side, x.Slot));
                        bool firstLine = !AnyGroupDown(p) && !anotherGoneNow;
                        if (text.Contains(", the first line to fall.") != firstLine) bad.Add(w + " 'the first line to fall' is wrong");
                    }
                    break;
                }
                case CiteKind.Siege:
                {
                    string who = r.Besieger == Side.Player ? "You besiege" : "The enemy besieges";
                    string want = siegeClock > 0
                        ? $"A siege. {who}, and the siege clock is {siegeClock} ticks: " + (r.Besieger == Side.Player
                            ? "if you have traded no blow by then, you lose your stance and every company of yours must advance."
                            : "if it has traded no blow by then, it loses its stance and every company of it must advance. You may wait.")
                        : $"A siege. {who}.";
                    if (i != 0 || r.Besieger is not { } bsg || ck.A != (int)bsg || ck.Value != siegeClock || text != want) bad.Add(w + " the siege line does not match the recording's besieger and the arena's clock");
                    break;
                }
                case CiteKind.Clock:
                {
                    bool ok = r.Besieger is { } bsc && ck.A == (int)bsc && (f.WorldFlags & ClockBit) != 0 && p is not null && (p.WorldFlags & ClockBit) == 0;
                    if (ok)
                        for (int j = 1; j < i && ok; j++)
                            foreach (var e in F[j].Events)
                                if (B(F[j], e.Source) is { } s1 && s1.Side == r.Besieger || B(F[j], e.Target) is { } t1 && t1.Side == r.Besieger) { ok = false; break; }
                    string want = r.Besieger == Side.Player
                        ? "The siege clock has run out. You had traded no blow, so you lose your stance: every company of yours now advances."
                        : "The siege clock has run out. The enemy had traded no blow, so it loses its stance: every company of theirs now advances.";
                    if (!ok || text != want) bad.Add(w + " the clock bit is not first set in this frame, or the besieger had traded a blow before it, or the text differs");
                    break;
                }
                case CiteKind.Set:
                {
                    var b = B(f, ck.A); var tr = b is null ? null : traits?.Invoke(b.Value.Side, b.Value.Key);
                    if (b is null || tr is null || ck.Value != 6 || ck.Value2 != HandBonus(tr.Value) || !DugInAt(r, ck.A, i)) { bad.Add(w + " that cohort is not provably dug in for the first time at this tick, or the bonus is not its traits'"); break; }
                    // the first of its side: no cohort of the side before it by tick, and none of lower id on the same tick
                    bool earlier = false;
                    for (int j = 6; j <= i; j++) foreach (var o in F[j].Blobs.Where(x => x.Side == b.Value.Side && x.Id != ck.A && (j < i || x.Id < ck.A))) if (DugInAt(r, o.Id, j)) earlier = true;
                    string edge = ck.Value2 > 0 ? $"They strike {ck.Value2}% harder until they are hit or move." : "Their reach is already the longest, so it gains them nothing.";
                    if (earlier || text != $"{Cap(Nm(kn, b.Value))} are dug in: {SetWords[ck.Value]} ticks unhit and in place. {edge}") bad.Add(w + " not the first cohort of its side to dig in, or the text differs");
                    break;
                }
                case CiteKind.Gate:
                    if ((f.WorldFlags & WorldFlag.GateFallen) == 0 || (p is not null && (p.WorldFlags & WorldFlag.GateFallen) != 0) || text != "The gate has fallen.") bad.Add(w + " the recording's gate flag is not first set in this frame");
                    break;
                case CiteKind.Run:
                {
                    var t = B(f, ck.A);
                    int len = ck.Tick - ck.B + 1, dmg = 0; bool ok = t is not null && len == ck.Value && len >= 4;
                    for (int tk = ck.B; ok && tk <= ck.Tick; tk++)
                    {
                        if (!idx.TryGetValue(tk, out int ji)) { ok = false; break; }
                        var hits = F[ji].Events.Where(e => e.Target == ck.A).ToList();
                        if (hits.Count == 0) ok = false;
                        dmg += hits.Sum(e => e.Damage);
                    }
                    if (ok && idx.TryGetValue(ck.B - 1, out int pj) && F[pj].Events.Any(e => e.Target == ck.A)) ok = false;
                    if (!ok || dmg != ck.Value2 || !text.StartsWith(Cap(Nm(kn, t!.Value)) + $" have been struck {ck.Value} ticks running, {ck.Value2} in all.")) bad.Add($"{w} the run is not in the frames (from tick {ck.B}, {ck.Value} ticks, {dmg} damage)");
                    break;
                }
                case CiteKind.Span:
                {
                    bool ok = ck.B - ck.A + 1 >= 3 && F.Any(x => x.Tick < ck.A && x.Events.Count > 0);
                    for (int tk = ck.A; ok && tk <= ck.B; tk++) if (!idx.TryGetValue(tk, out int ji) || F[ji].Events.Count > 0) ok = false;
                    if (!ok || !text.Contains($"from tick {ck.A} to tick {ck.B}.")) bad.Add($"{w} a strike lands inside the span or it is too short");
                    break;
                }
                case CiteKind.Stand:
                {
                    var b = B(f, ck.A);
                    if (b is null || b.Value.Strength != ck.Value || ck.Value <= 0 || Standing(f, b.Value.Side) != 1 || p is null || Standing(p, b.Value.Side) < 2 || i == F.Count - 1
                        || !text.Contains(Nm(kn, b.Value)) || !text.Contains($"{ck.Value} strong")) bad.Add(w + " that cohort is not the last of its side at this tick");
                    break;
                }
                case CiteKind.End:
                {
                    int me = Sum(f, Side.Player), they = Sum(f, Side.Enemy);
                    string start = r.Result.Outcome switch { BattleStatus.AttackerWon => "The field is yours.", BattleStatus.DefenderWon => "The enemy holds the field.", BattleStatus.Draw => me > 0 && they > 0 ? "Neither side held the field." : "Both sides fall together.", _ => "The battle did not finish." };
                    // both sides still standing at the last frame means the tick limit stopped it: the field goes to the greater POWER left (not the greater count), or it is a draw on equal power
                    string limit = me > 0 && they > 0 ? (r.Result.Outcome == BattleStatus.Draw ? " The battle stopped at its tick limit with the power left on the two sides equal." : " The battle stopped at its tick limit, where the field goes to the side with the greater power left, not to the side with more soldiers.") : "";
                    bool winnerOk = limit.Length > 0 || !(r.Result.Outcome == BattleStatus.AttackerWon && !(me > they)) && !(r.Result.Outcome == BattleStatus.DefenderWon && !(they > me));
                    if (i != F.Count - 1 || ck.Value != (int)r.Result.Outcome || ck.A != me || ck.B != they || !text.StartsWith(start) || !text.Contains($"{me}") || !text.Contains($"{they}")
                        || (limit.Length > 0 ? !text.EndsWith(limit) : text.Contains("tick limit")) || !winnerOk) bad.Add(w + " the end does not match the last frame and the recorded outcome");
                    break;
                }
            }
        }
        if (c.Lines.Count(x => x.Kind == ChronicleKind.Start) != 1 || c.Lines.Count(x => x.Kind == ChronicleKind.End) != 1 || c.Lines.Count(x => x.Kind == ChronicleKind.Contact) > 1 || c.Lines.Count(x => x.Kind == ChronicleKind.Gate) > 1) bad.Add("a line kind appears the wrong number of times");
        if (c.Lines.Count(x => x.Kind == ChronicleKind.Siege) != (r.Besieger is null || F.Count == 0 ? 0 : 1) || c.Lines.Count(x => x.Kind == ChronicleKind.Clock) > 1) bad.Add("the siege line or the clock line appears the wrong number of times");
        if (c.Lines.Where(x => x.Kind == ChronicleKind.DugIn).GroupBy(x => B(F[idx[x.Tick]], x.Cite.A)?.Side).Any(g => g.Count() > 1) || (traits is null && c.Lines.Any(x => x.Kind == ChronicleKind.DugIn))) bad.Add("a side has two dug-in lines, or a dug-in line is written without the cohorts' traits");
        // every siege recording whose bit is set while the besieger had traded no blow before it must have its clock line
        if (r.Besieger is { } bsAll && F.Count > 1)
        {
            int firstBit = Enumerable.Range(1, F.Count - 1).Where(j => (F[j].WorldFlags & ClockBit) != 0 && (F[j - 1].WorldFlags & ClockBit) == 0).DefaultIfEmpty(-1).First();
            bool traded = firstBit > 0 && Enumerable.Range(1, firstBit - 1).Any(j => F[j].Events.Any(e => B(F[j], e.Source) is { } a && a.Side == bsAll || B(F[j], e.Target) is { } d && d.Side == bsAll));
            if (firstBit > 0 && !traded && c.Lines.All(x => x.Kind != ChronicleKind.Clock)) bad.Add("the clock fell in the recording but no line says so");
            if (firstBit < 0 && c.Lines.Any(x => x.Kind == ChronicleKind.Clock)) bad.Add("a clock line without the bit in the recording");
        }
        // every cohort provably dug in, the first of its side, has its line (when the traits are known)
        if (traits is not null && F.Count > 6)
            foreach (var side in new[] { Side.Player, Side.Enemy })
            {
                int firstAt = -1, firstId = -1;
                for (int j = 6; j < F.Count && firstAt < 0; j++) foreach (var o in F[j].Blobs.Where(x => x.Side == side).OrderBy(x => x.Id)) if (DugInAt(r, o.Id, j)) { firstAt = j; firstId = o.Id; break; }
                bool has = c.Lines.Any(x => x.Kind == ChronicleKind.DugIn && x.Cite.A == firstId && x.Tick == F[Math.Max(0, firstAt)].Tick);
                if (firstAt >= 0 && traits(side, B(F[firstAt], firstId)!.Value.Key) is not null && !has) bad.Add($"{side} has a cohort provably dug in at tick {F[firstAt].Tick} but no line says so");
            }
        if (F.Any(x => x.Events.Count > 0) && c.Lines.All(x => x.Kind != ChronicleKind.Contact)) bad.Add("there are strikes in the recording but no first contact");
        if (F.Any(x => (x.WorldFlags & WorldFlag.GateFallen) != 0) && F.Count > 1 && (F[0].WorldFlags & WorldFlag.GateFallen) == 0 && c.Lines.All(x => x.Kind != ChronicleKind.Gate)) bad.Add("the gate fell in the recording but no line says so");
        return bad;
    }

    /// <summary>The Why against the record: recomputed here from the frames, and the hint against the doctrine and the log.</summary>
    public static List<string> VerifyWhy(Chronicle c, BattleReplay r, DoctrineSet doctrine, Func<int, string> kn)
    {
        var bad = new List<string>();
        var F = r.Frames; var y = c.Why;
        int tTick = -1, tSwing = 0, tYou = 0, tEn = 0, sumYou = 0, sumEn = 0;
        int? fbTick = null, fbBlob = null, flTick = null, flSlot = null; Side? flSide = null;
        for (int i = 1; i < F.Count; i++)
        {
            int you = Math.Max(0, Sum(F[i - 1], Side.Player) - Sum(F[i], Side.Player)), en = Math.Max(0, Sum(F[i - 1], Side.Enemy) - Sum(F[i], Side.Enemy));
            sumYou += you; sumEn += en;
            int sw = en - you;
            if (you + en > 0 && (tTick < 0 || Math.Abs(sw) > Math.Abs(tSwing))) { tTick = F[i].Tick; tSwing = sw; tYou = you; tEn = en; }
            var broke = Broke(r, i);
            if (fbTick is null && broke.Count > 0) { fbTick = F[i].Tick; fbBlob = broke[0].Id; }
            if (flTick is null) foreach (var b in broke) if (GroupDown(F[i], b.Side, b.Slot)) { flTick = F[i].Tick; flSide = b.Side; flSlot = b.Slot; break; }
        }
        if (sumYou != r.Result.AttackerLosses.Sum(x => x.Lost) || sumEn != r.Result.DefenderLosses.Sum(x => x.Lost)) bad.Add("the frames' losses are not the record's losses");
        if ((tTick < 0 ? (int?)null : tTick) != y.TurnTick || (tTick >= 0 && (y.TurnYouLost != tYou || y.TurnEnemyLost != tEn))) bad.Add($"turning point is {y.TurnTick} ({y.TurnYouLost}/{y.TurnEnemyLost}), the frames say {tTick} ({tYou}/{tEn})");
        if (y.FirstBreakTick != fbTick || y.FirstBreakBlob != fbBlob) bad.Add("first break differs from the frames");
        if (y.FirstLineTick != flTick || y.FirstLineSide != flSide || y.FirstLineSlot != flSlot) bad.Add("first line gone differs from the frames");
        string l0 = tTick < 0 ? "No soldier fell on either side."
            : tSwing > 0 ? $"Turning point: tick {tTick}, when the enemy lost {tEn} {(tEn == 1 ? "soldier" : "soldiers")} and you lost {tYou}."
            : tSwing < 0 ? $"Turning point: tick {tTick}, when you lost {tYou} {(tYou == 1 ? "soldier" : "soldiers")} and the enemy lost {tEn}."
            : $"Turning point: tick {tTick}, when each side lost {tYou} {(tYou == 1 ? "soldier" : "soldiers")}.";
        string l1 = flTick is not null ? $"The first line to fall: {(flSide == Side.Player ? "your" : "the enemy")} {SlotNames[flSlot!.Value]} line, at tick {flTick}."
            : fbTick is not null ? $"No line fell whole. The first cohort to break was {Nm(kn, B(F.First(x => x.Tick == fbTick), fbBlob!.Value)!.Value)} ({SlotNames[B(F.First(x => x.Tick == fbTick), fbBlob.Value)!.Value.Slot]} line) at tick {fbTick}."
            : "No cohort broke.";
        if (y.Lines.Count < 2 || y.Lines[0] != l0 || y.Lines[1] != l1) bad.Add($"the Why lines are not the record's: '{(y.Lines.Count > 0 ? y.Lines[0] : "")}' / '{(y.Lines.Count > 1 ? y.Lines[1] : "")}'");
        if (y.Lines.Count != 2 + (y.Hint is null ? 0 : 1) || (y.Hint is not null && y.Lines[^1] != y.Hint)) bad.Add("the Why holds more than two lines and one hint");
        if (y.Hint is not null)
        {
            var orders = new List<(int Group, FieldOrderKind Kind)>();
            for (int s = 0; s < 4; s++) if (DoctrineVocabulary.Decode(doctrine[s]) is { Family: OrderFamily.Field } o) orders.Add((o.A, (FieldOrderKind)o.Kind));
            bool ok = false;
            if (y.Hint.Contains("broke first while ordered to"))
            {
                var fb = fbTick is null ? null : B(F.First(x => x.Tick == fbTick), fbBlob!.Value);
                ok = fb is { Side: Side.Player } && r.Result.Outcome != BattleStatus.AttackerWon && orders.Any(o => o.Group == fb.Value.Slot)
                    && y.Hint == $"Your {SlotNames[fb.Value.Slot]} line broke first while ordered to {HandNames[(int)orders.Last(o => o.Group == fb.Value.Slot).Kind].ToLowerInvariant()}. Try another order for it.";
            }
            else if (y.Hint.StartsWith("Your order to ride the flank on the "))
            {
                foreach (var o in orders.Where(o => o.Kind == FieldOrderKind.Flank))
                    foreach (var l in c.Lines.Where(l => l.Kind == ChronicleKind.Flank && B(F[idxOf(F, l.Tick)], l.Cite.A) is { Side: Side.Player } sb && sb.Slot == o.Group))
                        if (y.Hint.Contains($"on the {SlotNames[o.Group]} line") && y.Hint.EndsWith($"struck from the side at tick {l.Tick}.")) ok = true;
            }
            if (!ok) bad.Add("the hint is not backed by the doctrine and the record: " + y.Hint);
        }
        return bad;
    }

    static int idxOf(IReadOnlyList<ReplayFrame> F, int tick) { for (int i = 0; i < F.Count; i++) if (F[i].Tick == tick) return i; return 0; }
}

/// <summary>A general for the per-slot match record: it asks the real general, then (so a suggestion exists on the very first round) makes up one
/// from the first legal orders if the real one has none, and attaches the given per-slot reading (or none). Used only to drive the page's display.</summary>
sealed class SlotLens(IGeneral inner, SlotPermille? slots) : IGeneral
{
    public int ExamplesSeen => inner.ExamplesSeen;
    public async ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var o = await inner.SuggestAsync(input, ct);
        var d = o.Doctrine.IsEmpty ? DoctrineSet.Of(Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(input.Legal.Allows).Take(3).ToArray()) : o.Doctrine;
        return new GeneralOutput(d, 640, slots);
    }
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct) => inner.LearnAsync(e, ct);
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => inner.LearnOutcomeAsync(o, ct);
}

/// <summary>
/// A general whose suggestion holds two orders it is allowed (the first two of its mask, which the game has already narrowed to the council) and one it is
/// not (the first token the mask refuses). The game must remove the one it is not allowed and count it, so the suggestion the visitor sees is within the council.
/// </summary>
sealed class OutsideLens(IGeneral inner) : IGeneral
{
    public int ExamplesSeen => inner.ExamplesSeen;
    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput input, CancellationToken ct)
    {
        var allowed = Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(input.Legal.Allows).Take(2).ToList();
        int outside = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(x => !input.Legal.Allows(x));
        return ValueTask.FromResult(new GeneralOutput(DoctrineSet.Of([.. allowed, outside]), 640));
    }
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct) => inner.LearnAsync(e, ct);
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => inner.LearnOutcomeAsync(o, ct);
}

/// <summary>Hands that ignore the offers (what the page was before the muster and the council): a free draft and a free choice. The game refuses them.</summary>
sealed class FreeHands : IPlayerInput
{
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<DraftPick>>([new DraftPick(0, 997)]);
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct) => DraftAsync(army, round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
        => ValueTask.FromResult(new PlayerChoice(DoctrineSet.Of(Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(army.Legal.Allows).Take(3).ToArray()), true));
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct) => ChooseAsync(army, ghost, round, ct);
}

/// <summary>The same hands, handing each draft's picks over in the opposite order (an information run, see the determinism block).</summary>
sealed class ReversedPicks(IPlayerInput inner) : IPlayerInput
{
    public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct) => (await inner.DraftAsync(army, round, ct)).Reverse().ToList();
    public async ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct) => (await inner.DraftAsync(army, offer, round, ct)).Reverse().ToList();
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct) => inner.ChooseAsync(army, ghost, round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct) => inner.ChooseAsync(army, ghost, offer, round, ct);
}

/// <summary>A visitor's habits as a function of the situation: used both to know what to tap and as an IPlayerInput for the headless game.</summary>
sealed class ScriptHands(ScriptHands.How[] plan) : IPlayerInput
{
    public enum How { Manual, Ignore, Accept, Edit }
    public sealed record Step(How How, DoctrineSet Doctrine, bool Overrode, int[] Tokens, int ClearSlot);

    /// <summary>
    /// Which companies of the muster this visitor buys: the first two, in the order the muster lists them, that the purse pays for while keeping a small
    /// reserve (a visitor who wants to be able to pay for a reroll). THE PICKS GO IN THE MUSTER'S ORDER, as the page hands them over: the army places a new
    /// kind by the balance rule and by pick order (the same draft in another order has the same army digest and may fight a different battle; see the
    /// information line in the determinism block and the finding in CLAUDE.md).
    /// </summary>
    public const int Reserve = 3;
    public static List<int> PackIndices(UnitOffer offer, int gold)
    {
        var l = new List<int>(); int left = gold;
        for (int i = 0; i < offer.Packs.Count && l.Count < 2; i++)
            if (offer.Packs[i].Affordable && offer.Packs[i].Cost <= left - Reserve) { l.Add(i); left -= offer.Packs[i].Cost; }
        return l;
    }
    public static List<DraftPick> PickList(UnitOffer offer, int gold) => PackIndices(offer, gold).Select(i => UnitOffer.Pick(offer.Packs[i])).ToList();

    static int[] Preferred(int round) => round switch
    {
        0 => [DoctrineVocabulary.Field(FieldOrderKind.Hold, 0), DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Field(FieldOrderKind.Advance, 2)],
        1 => [DoctrineVocabulary.Army(ArmyOrderKind.Flip, 1), DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), DoctrineVocabulary.Field(FieldOrderKind.Hunt, 2), DoctrineVocabulary.Army(ArmyOrderKind.Age)],
        _ => [DoctrineVocabulary.Field(FieldOrderKind.Guard, 1), DoctrineVocabulary.Army(ArmyOrderKind.Aura, 2), DoctrineVocabulary.Field(FieldOrderKind.Hold, 0), DoctrineVocabulary.Army(ArmyOrderKind.Age)],
    };

    /// <summary>What the visitor does with the council: only orders on the table that the army allows, in the preferred order of the round.</summary>
    public Step Decide(int round, ArmyReadModel army, GeneralOutput ghost, OrderOffer offer)
    {
        var how = round < plan.Length ? plan[round] : How.Manual;
        bool hasGhost = !ghost.Doctrine.IsEmpty;
        var legal = army.Legal;
        var table = offer.Tokens.Where(legal.Allows).ToList();                                   // the council, as the army allows it
        if (how == How.Accept && hasGhost) return new(How.Accept, ghost.Doctrine, false, [], -1);
        if (table.Count == 0) return new(How.Manual, DoctrineSet.Empty, !ghost.Doctrine.IsEmpty, [], -1);   // a host wiped out has nothing to order
        if (how == How.Edit && hasGhost)
        {
            var g = ghost.Doctrine.Tokens.Where(t => t != 0).ToList();
            int n = g.Count;
            var keep = g.Take(n - 1).ToList();
            var cand = Preferred(99).Where(table.Contains).Concat(table).Where(t => !keep.Contains(t) && t != g[n - 1]).ToList();
            if (cand.Count == 0) return new(How.Accept, ghost.Doctrine, false, [], -1);
            int rep = cand[0];
            var d = DoctrineSet.Of([.. keep, rep]);
            return new(How.Edit, d, d != ghost.Doctrine, [rep], n - 1);
        }
        var toks = Preferred(round).Where(table.Contains).Distinct().Take(3).ToList();
        if (toks.Count == 0) toks.Add(table[0]);
        var dm = DoctrineSet.Of(toks.ToArray());
        return new(hasGhost && how == How.Ignore ? How.Ignore : How.Manual, dm, dm != ghost.Doctrine, toks.ToArray(), -1);
    }

    // the old free-choice hands: nothing is on offer to them (the game under the offers refuses a free pick; this player makes none)
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<DraftPick>>(new List<DraftPick>());
    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, UnitOffer offer, int round, CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<DraftPick>>(PickList(offer, army.Gold));

    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
        => ChooseAsync(army, ghost, OrderOffer.Open(), round, ct);
    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, OrderOffer offer, int round, CancellationToken ct)
    {
        var s = Decide(round, army, ghost, offer);
        return ValueTask.FromResult(new PlayerChoice(s.Doctrine, s.Overrode));
    }
}
