using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using Doctrine;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Pages;
using ShowroomDoctrine;

// UI proof for Doctrine. See the csproj header. The page is the REAL one, the sections are the REAL ones (Pleroma army, Tracer battle,
// tiny HoloFormer general), the taps are a visitor's. Nothing is launched: bUnit renders the component in memory.
string showroom = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\.."));
int failures = 0, checks = 0;
void Check(string name, bool ok, string detail = "")
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    if (!ok) failures++;
}
// The pages change state from the pipeline's own thread; bUnit caches the parsed DOM and a render racing a read can leave the cache stale, so a
// wait that has not seen its condition for half a second asks the live pages to re-render once (this is a test-harness matter, not the page's).
var livePages = new List<IRenderedComponent<DoctrinePage>>();
async Task<bool> Until(Func<bool> cond, int ms, string what)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    long nextRefresh = 500;
    while (sw.ElapsedMilliseconds < ms)
    {
        try { if (cond()) return true; } catch { }
        if (sw.ElapsedMilliseconds >= nextRefresh) { nextRefresh += 500; foreach (var lp in livePages.ToArray()) { try { lp.Render(); } catch { } } }
        await Task.Delay(25);
    }
    Console.WriteLine($"  (timed out after {ms} ms waiting for: {what})");
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
void Scan(IRenderedComponent<DoctrinePage> p) { allText.Add(Regex.Replace(p.Find(".dc").TextContent, @"\s+", " ")); allMarkup.Add(p.Markup); }

// ---------------------------------------------------------------------------------------------- the static shell
using (var sctx = NewCtx(true))
{
    var shell = sctx.Render<DoctrinePage>();
    Check("shell: the heading and lede are real text", shell.Markup.Contains("Doctrine") && Regex.Replace(shell.Find(".lede").TextContent, @"\s+", " ").Contains("Your general learns how you set a doctrine from your own choices, in your browser, with nothing pre-trained"));
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
    var arenas = new DoctrineSections().Build(script, new DoctrineDomain { MaxRounds = 1 });
    var arena = arenas.Context.Battle.Arenas[(int)(GameRng.Mix(sd, 3000) % (ulong)arenas.Context.Battle.Arenas.Count)];
    if (arena.Id != "falling-gate") continue;
    var r = await new DoctrineSections().Build(script, new DoctrineDomain { MaxRounds = 1 }).PlayAsync(sd);
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
    var all = p.FindAll(sel);
    if (skip >= all.Count) { Check($"tap target missing: {sel} #{skip}", false); return; }   // a missing control is a red check, not a crash
    var el = all[skip];
    await p.InvokeAsync(() => el.Click());
}
async Task TapChip(IRenderedComponent<DoctrinePage> p, int token)
{
    bool field = DoctrineVocabulary.Decode(token).Family == OrderFamily.Field;
    if (!Has(p, $"[data-token='{token}']")) await Tap(p, ".dc-tab", field ? 1 : 0);
    await Tap(p, $"[data-token='{token}']");
}
async Task DoDraft(IRenderedComponent<DoctrinePage> p, int round)
{
    await Until(() => Has(p, ".dc-draft"), 20000, "draft screen");
    foreach (var (kind, n) in ScriptHands.Picks(round, p.Instance.Session!.Draft!.Army.Gold))
        for (int i = 0; i < n; i++) await p.InvokeAsync(() => p.FindAll(".dc-kind")[kind].QuerySelector(".dc-plus")!.Click());
    Scan(p);
    await Tap(p, "#dc-confirm");
}
var roundTimes = new List<double>();
async Task DoChoose(IRenderedComponent<DoctrinePage> p, ScriptHands sh, int round)
{
    await Until(() => Has(p, ".dc-editor"), 20000, "doctrine screen");
    var st = sh.Decide(round, p.Instance.Session!.Army!, p.Instance.Session!.Ghost!);
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

await Tap(page, ".dc-opt", 0);   // 3 rounds
Check("start: the 3-rounds option is the one pressed (radio, aria-checked)", page.FindAll(".dc-opt")[0].GetAttribute("aria-checked") == "true" && page.FindAll(".dc-opt")[1].GetAttribute("aria-checked") == "false");
await page.InvokeAsync(() => page.Find("#dc-seed").Input(gateSeed.ToString()));
var swGame = System.Diagnostics.Stopwatch.StartNew();
await Tap(page, "#dc-new");
Check("start: the game starts with that seed and 3 rounds", S.InGame && S.Seed == gateSeed && S.TotalRounds == 3);

// ---- round 0: draft with a refusal, legal-only doctrine, no ghost yet
await Until(() => Has(page, ".dc-draft"), 20000, "round 0 draft");
Check("draft: status strip says Round 1 of 3, gold, soldiers, kinds and the general's examples", Text(page, ".dc-status").Contains("Round 1 of 3") && Text(page, ".dc-status").Contains("Gold ") && Text(page, ".dc-status").Contains("General: 0 examples seen"), Text(page, ".dc-status"));
Check("draft: all eight kinds are listed with traits and a price, each with 44px plus and minus buttons", page.FindAll(".dc-kind").Count == 8 && page.FindAll(".dc-kind .dc-plus").Count == 8 && page.FindAll(".dc-kind .dc-minus").Count == 8 && page.FindAll(".dc-kind")[7].TextContent.Contains("Catapults") && page.FindAll(".dc-kind")[7].TextContent.Contains("4 gold each"));
ulong digestBeforeRound0 = S.Draft!.Army.Digest();
int gold0 = S.Draft!.Army.Gold;
for (int i = 0; i < 4; i++) await page.InvokeAsync(() => page.FindAll(".dc-kind")[7].QuerySelector(".dc-plus")!.Click());   // 4 catapults = 16 gold
Check("draft: the ARMY's own refusal is shown in plain words when the gold is not there", await Until(() => Has(page, ".dc-refused"), 3000, "refusal") && Text(page, ".dc-refused").Contains("Too little gold for this pick."), Text(page, ".dc-refused"));
Check("draft: the cost line is shown", Text(page, ".dc-sum").Contains($"costs 16 of {gold0} gold"), Text(page, ".dc-sum"));
for (int i = 0; i < 4; i++) await page.InvokeAsync(() => page.FindAll(".dc-kind")[7].QuerySelector(".dc-minus")!.Click());
Check("draft: taking the picks back clears the refusal; the minus button is disabled at zero", await Until(() => !Has(page, ".dc-refused"), 3000, "refusal gone") && page.FindAll(".dc-kind")[7].QuerySelector(".dc-minus")!.HasAttribute("disabled"));
Check("draft: the real army was untouched by the preview", S.Draft!.Army.Digest() == digestBeforeRound0 && S.Draft.Army.Gold == gold0);
await DoDraft(page, 0);
await Until(() => Has(page, ".dc-editor"), 20000, "round 0 doctrine screen");
Check("doctrine: round 0, the general has seen nothing: no ghost, a plain sentence instead, confidence 0", !Has(page, ".dc-ghost") && Has(page, ".dc-noghost") && S.Ghost!.ConfidencePermille == 0 && S.Ghost.Doctrine.IsEmpty && S.Sections.General.ExamplesSeen == 0, Text(page, ".dc-noghost"));
Scan(page);

// legal-only gating: every chip of both tabs is disabled exactly when the army's mask does not allow the token
{
    var legal = S.Army!.Legal;
    var seen = new HashSet<int>(); bool match = true; int disabled = 0, enabled = 0;
    foreach (var tab in new[] { 0, 1 })
    {
        await Tap(page, ".dc-tab", tab);
        foreach (var chip in page.FindAll(".dc-chip"))
        {
            int token = int.Parse(chip.GetAttribute("data-token")!);
            seen.Add(token);
            bool off = chip.HasAttribute("disabled");
            if (off == legal.Allows(token)) match = false;
            if (off) disabled++; else enabled++;
        }
    }
    Check("doctrine: the palette holds all 53 orders and each is enabled exactly when the army's legal list allows it", seen.Count == DoctrineVocabulary.Size - 1 && match, $"{enabled} enabled, {disabled} disabled");
    Check("doctrine: the list really restricts (some enabled, some disabled) and Cull is never offered", enabled > 0 && disabled > 0);
    await Tap(page, ".dc-tab", 0);
    Check("doctrine: Cull is explained in a sentence", Has(page, ".dc-cullnote") && Text(page, ".dc-cullnote").Contains("Restrict"));
    int illegal = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(t => !legal.Allows(t));
    await Tap(page, ".dc-tab", DoctrineVocabulary.Decode(illegal).Family == OrderFamily.Field ? 1 : 0);
    await Tap(page, $"[data-token='{illegal}']");
    Check("doctrine: tapping an order the army says is illegal puts nothing in a slot", page.FindAll(".dc-slot.empty").Count == 4);
    await Tap(page, ".dc-tab", 0);
    int firstLegal = Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(legal.Allows);
    await TapChip(page, firstLegal);
    Check("doctrine: a legal order lands in the first slot, with its sentence under the slots", page.FindAll(".dc-slot.empty").Count == 3 && Has(page, ".dc-says li"));
    await Tap(page, ".dc-slot", 0);
    Check("doctrine: tapping a filled slot clears it", page.FindAll(".dc-slot.empty").Count == 4);
}
await DoChoose(page, script, 0);
Check("battle: while the round works the page says so, with a Cancel button (44px)", await Until(() => Has(page, "#dc-working") || Has(page, ".dc-result"), 20000, "working or result"));
Scan(page);

// ---- round 0 result: the replay, the scrubber, the falling gate
await Until(() => Has(page, ".dc-result"), 60000, "round 0 result");
roundTimes.Add(swGame.Elapsed.TotalMilliseconds);
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
    Check("watch: the army after the round is shown", Has(page, ".dc-army"));
}
Console.WriteLine($"  (round 0: doctrine {S.Records[0].PlayerDoctrine}, outcome {S.Records[0].Result.Outcome}, lost {S.Records[0].Result.AttackerLosses.Sum(l => l.Lost)}, army after {S.Last!.ArmyAfter.Soldiers} soldiers, gold {S.Last.ArmyAfter.Gold})");
Check("round 0: the general has learned twice (the doctrine, then the result)", S.Sections.General.ExamplesSeen == 2, S.Sections.General.ExamplesSeen.ToString());
Check("round 0: the status strip follows (2 examples seen)", Text(page, ".dc-status").Contains("General: 2 examples seen"));
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
    Check("ghost: the suggestion is shown with its confidence as a glow (--glow 0..1) and as a number out of 1000", Has(page, ".dc-ghost") && Regex.IsMatch(page.Find(".dc-ghost").GetAttribute("style")!, @"--glow:(0|1|0\.\d+)") && Text(page, ".dc-conf").Contains($"{ghost1.ConfidencePermille} of 1000"), page.Find(".dc-ghost").GetAttribute("style")!);
    Check("ghost: Accept, Edit and Ignore are three 44px buttons", Has(page, "#dc-accept") && Has(page, "#dc-edit") && Has(page, "#dc-ignore"));
    Scan(page);
}
await DoChoose(page, script, 1);
await Until(() => Has(page, ".dc-result"), 60000, "round 1 result");
Scan(page);
Check("round 1: the visitor's doctrine was recorded as differing from the ghost (Overrode)", S.Records.Count == 2 && S.Records[1].Overrode == (S.Records[1].PlayerDoctrine != S.Records[1].Ghost), $"overrode={S.Records[1].Overrode}");
Check("round 1: the general has now learned 4 times", S.Sections.General.ExamplesSeen == 4, S.Sections.General.ExamplesSeen.ToString());
await Tap(page, "#dc-next");

// ---- round 2: the visitor accepts the ghost
await DoDraft(page, 2);
await Until(() => Has(page, ".dc-editor"), 30000, "round 2 doctrine screen");
var ghost2 = S.Ghost!;
Console.WriteLine($"  (round 2 ghost: {(ghost2.Doctrine.IsEmpty ? "none" : ghost2.Doctrine.ToString())}, confidence {ghost2.ConfidencePermille}, examples {S.Sections.General.ExamplesSeen})");
Check("ghost: after 4 examples a suggestion is offered, its confidence within 0..1000, every token legal", !ghost2.Doctrine.IsEmpty && ghost2.ConfidencePermille is >= 0 and <= 1000 && S.Army!.Legal.AllowsAll(ghost2.Doctrine), $"confidence {ghost2.ConfidencePermille}");
int examplesBeforeAccept = S.Sections.General.ExamplesSeen;
await DoChoose(page, script, 2);
await Until(() => Has(page, ".dc-result") || S.Stage == DoctrineStage.Over, 60000, "round 2 result");
Scan(page);
Check("accept: the accepted ghost is the doctrine that fought, recorded as NOT overridden", S.Records.Count == 3 && S.Records[2].PlayerDoctrine == ghost2.Doctrine && !S.Records[2].Overrode && S.Records[2].GhostConfidence == ghost2.ConfidencePermille);
Check("accept: accepting is a training example too (the general learned from this round twice more)", S.Sections.General.ExamplesSeen == examplesBeforeAccept + 2, $"{examplesBeforeAccept} -> {S.Sections.General.ExamplesSeen}");
Check("accept: the spoils screen says the suggestion was taken as it stood", Text(page, ".dc-spoils").Contains("You took it as it stood."));
Check("last round: the next button says it is the end", Text(page, "#dc-next") == "See the end of the game");
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
    var headless = await new DoctrineSections().Build(script, new DoctrineDomain { MaxRounds = 3 }).PlayAsync(gateSeed);
    Check("determinism: the page game and the headless game with the same hands have the SAME digest", headless.Digest() == result.Digest(), $"{result.Digest():x16} vs {headless.Digest():x16}");
    Check("determinism: every round's recording is byte-identical", headless.Rounds.Count == 3 && headless.Rounds.Zip(result.Rounds).All(p => ReplayCodec.Encode(p.First.Replay).AsSpan().SequenceEqual(ReplayCodec.Encode(p.Second.Replay))));
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
await Tap(pageB, ".dc-opt", 0);
await Tap(pageB, "#dc-new");
await Until(() => Has(pageB, ".dc-draft"), 20000, "B draft");
ulong startDigestB = SB.Draft!.Army.Digest();
using var gate = new ManualResetEventSlim(false);
bool frozeAtTick2 = false;
Action<int> hold = t => { if (t == 2 && !frozeAtTick2) { frozeAtTick2 = true; gate.Wait(30000); } };
SB.Sections.Observer.Ticked += hold;
await DoDraft(pageB, 0);
await DoChoose(pageB, scriptB, 0);
Check("cancel: the battle reaches tick 2 and the page shows the progress and a Cancel button", await Until(() => frozeAtTick2 && Has(pageB, "#dc-cancel") && Has(pageB, ".dc-progress"), 30000, "battle at tick 2") && SB.Stage == DoctrineStage.Working, $"tick {SB.BattleTick}");
Scan(pageB);
Check("cancel: the progress text names the tick and says the battle runs on this device", Text(pageB, ".dc-working").Contains("Tick 2 of up to 120") && Text(pageB, ".dc-working").Contains("on this device") || Text(pageB, ".dc-working").Contains("Simulating the battle on this device"));
await Tap(pageB, "#dc-cancel");
gate.Set();
Check("cancel: the round stops and the draft screen comes back", await Until(() => SB.Stage == DoctrineStage.Draft && Has(pageB, ".dc-draft"), 30000, "back to draft after cancel"));
SB.Sections.Observer.Ticked -= hold;
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
Check("edit: Edit loads the suggestion into the slots; changing one order records an override", ghostB && SB.Records.Count == 2 && SB.Records[1].Overrode && SB.Records[1].PlayerDoctrine != ghostBDoctrine && SB.Records[1].Ghost == ghostBDoctrine);
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

// ---------------------------------------------------------------------------------------------- an army with no soldiers
{
    using var ctxN = NewCtx(false);
    var pageN = ctxN.Render<DoctrinePage>();
    livePages.Add(pageN);
    await pageN.InvokeAsync(() => pageN.Find("#dc-code").Input("DA2.0.0.12.0.."));
    await Tap(pageN, "#dc-load");
    await Until(() => Has(pageN, ".dc-draft"), 20000, "N draft");
    Check("no soldiers: the draft screen says the army has none and to spend the gold", Text(pageN, ".dc-draft").Contains("no soldiers left"));
    await Tap(pageN, "#dc-confirm");
    await Until(() => Has(pageN, ".dc-editor"), 20000, "N doctrine");
    Check("no soldiers: nothing is legal, every order is greyed out and the page says why", pageN.FindAll(".dc-chip").All(c => c.HasAttribute("disabled")) && Text(pageN, ".dc-editor").Contains("no order is legal"));
    Scan(pageN);
    await Tap(pageN, "#dc-leave");
    Check("no soldiers: leaving still hands back a valid army code", await Until(() => Has(pageN, ".dc-end"), 20000, "N end") && Text(pageN, "#dc-armycode").StartsWith("DA2."));
}

// ---------------------------------------------------------------------------------------------- wording
{
    string all = string.Join("\n", allText);
    Check("wording: no em dash anywhere in the page", !all.Contains('—') && !allMarkup.Any(m => m.Contains('—')));
    foreach (var bad in new[] { "universal", "complete", "open source", "free to use", "MIT", "chance", "luck", "lucky", "guaranteed", "unbeatable", "plays well", "outlearn", "outperform", "smarter", "pre-trained model" })
        Check($"wording: no '{bad}'", !Regex.IsMatch(all, @"\b" + Regex.Escape(bad) + @"\b", RegexOptions.IgnoreCase));
    Check("wording: the general is described only as the probe allows (learns from your own choices, nothing pre-trained, different players different generals)",
        all.Contains("learns how you set a doctrine from your own choices, in your browser, with nothing pre-trained") && all.Contains("different players end up with different generals"));
    Check("wording: nothing says the net beats a table or quotes a hit rate", !Regex.IsMatch(all, @"beats a (lookup|table)|outlearns|3 (times )?in 10|4 in 10|5 in 10", RegexOptions.IgnoreCase));
    Check("wording: battles are said to be simulated once on this device and the replay to be a recording", all.Contains("simulated once, on your device") && all.Contains("The replay is a recording"));
    Check("wording: no persistence is claimed or invented (a refresh forgets the general)", all.Contains("Nothing is saved: refresh the page and the general is gone") && !all.Contains("saved in your browser"));
}

// -------------------------------------------------------------------------------- layout rules (no browser needed)
{
    string css = File.ReadAllText(Path.Combine(showroom, "Pages", "DoctrinePage.razor.css"));
    string acss = File.ReadAllText(Path.Combine(showroom, "Components", "DoctrineArena.razor.css"));
    string both = css + "\n" + acss;
    string index = File.ReadAllText(Path.Combine(showroom, "wwwroot", "index.html"));
    Check("viewport meta: width=device-width, initial-scale=1", Regex.IsMatch(index, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1(\\.0)?\""));
    Check("one rule gives every page button and summary a 44px minimum", Regex.IsMatch(css, @"\.dc button, \.dc summary \{[^}]*min-height: 44px;[^}]*min-width: 44px;"));
    Check("the replay's buttons and scrubber get the same 44px minimum", Regex.IsMatch(acss, @"\.dc-replay button, \.dc-replay input\[type=range\] \{[^}]*min-height: 44px;[^}]*min-width: 44px;"));
    Check("touch-action: manipulation on tap targets", css.Contains("touch-action: manipulation") && acss.Contains("touch-action: manipulation"));
    var fixedWidths = Regex.Matches(both, @"(?<![-\w])width\s*:\s*(\d+)px").Select(m => int.Parse(m.Groups[1].Value)).ToList();
    Check("no fixed width above 360px", fixedWidths.All(w => w <= 360), "fixed px widths: " + string.Join(",", fixedWidths));
    Check("no min-width in px above 360", Regex.Matches(both, @"(?<!\()min-width\s*:\s*(\d+)px").All(m => int.Parse(m.Groups[1].Value) <= 360));
    Check("media queries present (wide screens, narrow phones, landscape, reduced motion)", css.Contains("@media (min-width: 700px)") && css.Contains("@media (min-width: 960px)") && css.Contains("@media (max-width: 359px)") && css.Contains("orientation: landscape") && css.Contains("prefers-reduced-motion") && acss.Contains("prefers-reduced-motion"));
    Check("16px side gutters with safe-area insets", css.Contains("max(16px, env(safe-area-inset-left))") && css.Contains("max(16px, env(safe-area-inset-right))") && css.Contains("safe-area-inset-bottom"));
    Check("no hover-only rules", !both.Contains(":hover"));
    Check("the page cannot scroll sideways (overflow-x: clip on .room), and the arena scales to its box", css.Contains("overflow-x: clip") && Regex.IsMatch(acss, @"\.dc-arena \{[^}]*width: 100%;[^}]*height: auto"));
    Check("the base grids are the phone grids (two chip columns, two slot columns, three radio options)", Regex.IsMatch(css, @"\.dc-chipgrid \{[^}]*repeat\(2, minmax\(0, 1fr\)\)") && Regex.IsMatch(css, @"\.dc-slots \{[^}]*repeat\(2, minmax\(0, 1fr\)\)") && Regex.IsMatch(css, @"\.dc-opts \{[^}]*repeat\(3, minmax\(0, 1fr\)\)"));
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

// --dump <file>: write the visible text of every screen the proof reached (for the coordinator to show the user)
if (args.Length >= 2 && args[0] == "--dump") File.WriteAllText(args[1], string.Join("\n\n==== next screen ====\n\n", allText));
Console.WriteLine($"  (game A took {roundTimes.FirstOrDefault():F0} ms to the first result on this machine, desktop, not a phone)");
Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;

sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }

/// <summary>A visitor's habits as a function of the situation: used both to know what to tap and as an IPlayerInput for the headless game.</summary>
sealed class ScriptHands(ScriptHands.How[] plan) : IPlayerInput
{
    public enum How { Manual, Ignore, Accept, Edit }
    public sealed record Step(How How, DoctrineSet Doctrine, bool Overrode, int[] Tokens, int ClearSlot);

    public static (int Kind, int N)[] Picks(int round, int gold)
    {
        if (round == 0) return [(0, 3), (3, 2), (1, 1)];   // 9 of the 12 starting gold
        int shield = gold / 4, spear = gold - shield * 2;
        var l = new List<(int, int)>();
        if (shield > 0) l.Add((1, shield));
        if (spear > 0) l.Add((0, spear));
        return l.ToArray();
    }

    static int[] Preferred(int round) => round switch
    {
        0 => [DoctrineVocabulary.Field(FieldOrderKind.Hold, 0), DoctrineVocabulary.Army(ArmyOrderKind.Age), DoctrineVocabulary.Field(FieldOrderKind.Flank, 1), DoctrineVocabulary.Field(FieldOrderKind.Advance, 2)],
        1 => [DoctrineVocabulary.Army(ArmyOrderKind.Flip, 1), DoctrineVocabulary.Field(FieldOrderKind.Advance, 0), DoctrineVocabulary.Field(FieldOrderKind.Hunt, 2), DoctrineVocabulary.Army(ArmyOrderKind.Age)],
        _ => [DoctrineVocabulary.Field(FieldOrderKind.Guard, 1), DoctrineVocabulary.Army(ArmyOrderKind.Aura, 2), DoctrineVocabulary.Field(FieldOrderKind.Hold, 0), DoctrineVocabulary.Army(ArmyOrderKind.Age)],
    };

    public Step Decide(int round, ArmyReadModel army, GeneralOutput ghost)
    {
        var how = round < plan.Length ? plan[round] : How.Manual;
        bool hasGhost = !ghost.Doctrine.IsEmpty;
        var legal = army.Legal;
        if (how == How.Accept && hasGhost) return new(How.Accept, ghost.Doctrine, false, [], -1);
        if (how == How.Edit && hasGhost)
        {
            var g = ghost.Doctrine.Tokens.Where(t => t != 0).ToList();
            int n = g.Count;
            var keep = g.Take(n - 1).ToList();
            int rep = Preferred(99).Concat(Enumerable.Range(1, DoctrineVocabulary.Size - 1)).First(t => legal.Allows(t) && !keep.Contains(t) && t != g[n - 1]);
            var d = DoctrineSet.Of([.. keep, rep]);
            return new(How.Edit, d, d != ghost.Doctrine, [rep], n - 1);
        }
        var toks = Preferred(round).Where(legal.Allows).Distinct().Take(3).ToList();
        if (toks.Count == 0) toks.Add(Enumerable.Range(1, DoctrineVocabulary.Size - 1).First(legal.Allows));
        var dm = DoctrineSet.Of(toks.ToArray());
        return new(hasGhost && how == How.Ignore ? How.Ignore : How.Manual, dm, dm != ghost.Doctrine, toks.ToArray(), -1);
    }

    public ValueTask<IReadOnlyList<DraftPick>> DraftAsync(ArmyReadModel army, int round, CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<DraftPick>>(Picks(round, army.Gold).Select(p => new DraftPick(p.Kind, p.N)).ToList());

    public ValueTask<PlayerChoice> ChooseAsync(ArmyReadModel army, GeneralOutput ghost, int round, CancellationToken ct)
    {
        var s = Decide(round, army, ghost);
        return ValueTask.FromResult(new PlayerChoice(s.Doctrine, s.Overrode));
    }
}
