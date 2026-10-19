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
async Task DoDraft(IRenderedComponent<DoctrinePage> p, int round, bool pass = true)
{
    await Until(() => Has(p, ".dc-draft"), 20000, "draft screen");
    var sess = p.Instance.Session!;
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
Shot("m1-muster-bought", page);
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
        var expected = BattleChronicle.Build(rec0.Replay, rec0.PlayerDoctrine, arenaNm, DoctrineWords.KindName, cv.TraitsOf);
        var kids = page.Find(".dc-stage").Children.Select(c => string.Join(" ", c.ClassList)).ToList();
        Check("chronicle: the docked panel is in the stage after the field and the general, with a sheet button for a phone", kids.Count == 4 && kids[0].Contains("dc-armypanel") && kids[1].Contains("dc-field") && kids[2].Contains("dc-generalpanel") && kids[3].Contains("dc-chroniclepanel") && Has(page, "#dc-open-chronicle") && page.Find("#dc-chronicle").ClassList.Contains("dc-sheet"), string.Join(" | ", kids));
        Check("chronicle: the battle's set-up was seen (the player is the attacker) and the log reads each cohort's own traits from it", cv.Setup is { } su && su.Attacker.Side == Side.Player && su.Defender.Side == Side.Enemy && su.ArenaId == rec0.ArenaId
            && su.Attacker.Cohorts.All(c => cv.TraitsOf(Side.Player, c.Key) == c.Traits) && su.Defender.Cohorts.All(c => cv.TraitsOf(Side.Enemy, c.Key) == c.Traits));
        Check("chronicle: the page's log is the chronicle function applied to the recording (same lines, same why)", expected.Lines.Count >= 4 && ChronicleCheck.Verify(expected, rec0.Replay, DoctrineWords.KindName, cv.TraitsOf).Count == 0 && ChronicleCheck.VerifyWhy(expected, rec0.Replay, rec0.PlayerDoctrine, DoctrineWords.KindName).Count == 0,
            string.Join(" | ", ChronicleCheck.Verify(expected, rec0.Replay, DoctrineWords.KindName, cv.TraitsOf).Concat(ChronicleCheck.VerifyWhy(expected, rec0.Replay, rec0.PlayerDoctrine, DoctrineWords.KindName)).Take(3)));

        int[] LogTicks() => page.FindAll(".dc-logline[data-tick]").Select(e => int.Parse(e.GetAttribute("data-tick")!)).ToArray();
        string[] LogTexts() => page.FindAll(".dc-logline[data-tick] .x").Select(e => e.TextContent.Trim()).ToArray();
        async Task ScrubTo(int t) { await page.InvokeAsync(() => page.Find(".dc-range").Input(t.ToString())); await Until(() => Text(page, ".dc-tick") == t.ToString(), 3000, $"scrub to {t}"); }

        await ScrubTo(0);
        Check("chronicle: at tick 0 the log holds only the opening line and says it fills in as the battle plays; no Why yet",
            await Until(() => LogTicks().Length == expected.Lines.Count(l => l.Tick <= 0), 3000, "log at 0") && Has(page, ".dc-logempty") && !Has(page, ".dc-why") && LogTexts().SequenceEqual(expected.Lines.Where(l => l.Tick <= 0).Select(l => l.Text)));
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
        Check("chronicle: and back to only what has happened", await Until(() => LogTicks().Length == 1 && Has(page, "ol.dc-log.live"), 3000, "back to live"));
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
    Check("ghost: the suggestion is shown with its confidence as a glow (--glow 0..1) and as a number out of 1000", Has(page, ".dc-ghost") && Regex.IsMatch(page.Find(".dc-ghost").GetAttribute("style")!, @"--glow:(0|1|0\.\d+)") && Text(page, ".dc-conf").Contains($"Match record {ghost1.ConfidencePermille} of 1000"), page.Find(".dc-ghost").GetAttribute("style")!);
    Check("ghost: Use, Edit and Mine are three 44px buttons", Has(page, "#dc-accept") && Has(page, "#dc-edit") && Has(page, "#dc-ignore"));
    Scan(page);
    Shot("a2-doctrine-ghost", page);
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
// Under the offers the general is bound to the council: a habit it learned on orders that are not on this round's table is removed, so it may have nothing to suggest.
Check("ghost: after 4 examples the general's pick, if it has one, is within the council and the army's legal list, its confidence within 0..1000; if not, the page says it has no pick from this council",
    ghost2.ConfidencePermille is >= 0 and <= 1000 && (ghost2.Doctrine.IsEmpty ? Has(page, ".dc-noghost") : S.Army!.Legal.AllowsAll(ghost2.Doctrine) && ghost2.Doctrine.Tokens.All(S.OrdersOffered!.Allows)), $"confidence {ghost2.ConfidencePermille}");
int examplesBeforeAccept = S.Sections.General.ExamplesSeen;
await DoChoose(page, script, 2);
await Until(() => Has(page, ".dc-result") || S.Stage == DoctrineStage.Over, 60000, "round 2 result");
Scan(page);
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
    Check($"match record ({tag}): the ghost shows one line per order it suggests, and the whole figure under it", li.Count == gs.Doctrine.Used && li.Count >= 2 && Text(pageS, ".dc-conf").StartsWith($"Match record {gs.ConfidencePermille} of 1000: a score for how well its whole suggestion has matched what you then chose, over your recent choices."), Text(pageS, ".dc-conf"));
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
    Check("chronicle (synthetic frames): the log is exactly the hand-written account: first contact, the gate, a run, the first break and the first line gone, a flank, holy against cursed, the heaviest blow, a lull, the final stand, the end",
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
        var game = await new DoctrineSections().Build(new VarietyHands(seed), new DoctrineDomain { MaxRounds = 2, Offers = OfferRules.Off }, tap).PlayAsync(seed);
        for (int rd = 0; rd < game.Rounds.Count; rd++)
        {
            var rec = game.Rounds[rd];
            var setup = tap.Setups[rd];
            Func<Side, int, Trait?> traitsOf = (s, k) => (s == Side.Player ? setup.Attacker : setup.Defender).Cohorts.Where(c => c.Key == k).Select(c => (Trait?)c.Traits).FirstOrDefault();
            var c = BattleChronicle.Build(rec.Replay, rec.PlayerDoctrine, "Arena", kn, traitsOf);
            battles++; linesTotal += c.Lines.Count;
            foreach (var l in c.Lines) tally[l.Kind] = tally.GetValueOrDefault(l.Kind) + 1;
            if (c.Why.Hint is not null) hintsSeen.Add(c.Why.Hint);
            var pr = ChronicleCheck.Verify(c, rec.Replay, kn, traitsOf); if (pr.Count > 0) { bad++; firstProblems.AddRange(pr.Take(2).Select(x => $"seed {seed} round {rd}: {x}")); }
            var wp = ChronicleCheck.VerifyWhy(c, rec.Replay, rec.PlayerDoctrine, kn); if (wp.Count > 0) { whyBad++; firstProblems.AddRange(wp.Take(2).Select(x => $"seed {seed} round {rd} why: {x}")); }
            if (Sig(c) != Sig(BattleChronicle.Build(rec.Replay, rec.PlayerDoctrine, "Arena", kn, traitsOf))
                || !ReplayCodec.TryDecode(ReplayCodec.Encode(rec.Replay), out var back, out _) || Sig(c) != Sig(BattleChronicle.Build(back!, rec.PlayerDoctrine, "Arena", kn, traitsOf))) detBad++;
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
    Check("wording A: the top of How it works names the general as a habit table with a tiny HoloFormer from the AlgFormer package; no tiny AlgFormer model", all.Contains("and the general is a habit table with a tiny HoloFormer from the AlgFormer package.") && !all.Contains("tiny AlgFormer model"));
    Check("wording A: About states the rule the code has (table speaks; the HoloFormer takes a slot at two more right answers than the table, on recent choices)",
        all.Contains("Inside it are a small habit table and a tiny HoloFormer from the AlgFormer package. For each of the four slots the table speaks, unless the HoloFormer has been right on that slot at least two more times than the table over your recent choices, in which case it takes the slot.")
        && !all.Contains("whichever of the two") && !all.Contains("guessed your choices better"));
    Check("wording B: it is a match record, said as what it is; the word confidence is gone from every screen",
        all.Contains("Match record") && all.Contains("a score out of 1000 for how well its whole suggestion has matched what you then chose, over your recent choices. It starts low and climbs as the matches keep coming.") && all.Contains("of 1000: a score for how well its whole suggestion has matched what you then chose, over your recent choices.") && !all.Contains("how often") && !Regex.IsMatch(all, "confiden", RegexOptions.IgnoreCase));
    Check("wording C: the working screen says it runs on the device in small steps", all.Contains("This runs on your device, in small steps.") && !all.Contains("so the page stays usable") && !all.Contains("one small step at a time"));
    Check("wording D: the examples count says what it counts, in the chip and on the start and end screens",
        Regex.Matches(all, Regex.Escape("(each doctrine you set and each battle result)")).Count >= 3 && all.Contains("examples seen (each doctrine you set and each battle result)") && all.Contains("since you opened this page"));
    Check("wording E: the empty-army sentence tells what to do and why", all.Contains("Spend your gold on cohorts: a battle needs soldiers.") && !all.Contains("starts lost"));
    Check("wording F: Cull never appears (the army allows none, so the council never holds one): no card, no sentence, no operation name", !all.Contains("Cull") && !all.Contains("No order to dismiss soldiers") && !all.Contains("this version") && !allMarkup.Any(m => m.Contains("Cull a trait") || m.Contains("Dismiss a trait") || m.Contains("Dismiss ")));
    Check("wording G: the offers are said in the lord's voice: the muster of companies and the council of orders, a pick is a whole company, a reroll has a price and a limit", all.Contains("The muster") && all.Contains("The council") && all.Contains("each whole or not at all") && all.Contains("The three formations") && Regex.IsMatch(all, @"Reroll for \d+ gold \(\d+ left\)") && all.Contains("Keep these") && all.Contains("Your general's pick of the council"));
    Check("wording G: no word of the offers says draw as luck: the offers are the game's own draw from the game seed, never 'random', 'lucky' or 'roguelike'", !Regex.IsMatch(all, @"\b(random|randomly|roguelike|loot|gacha)\b", RegexOptions.IgnoreCase));
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
        foreach (var f in new[] { "DoctrineWords.cs", "DoctrineProgress.cs", "DoctrineGuide.cs" })
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
            ("an old order name", @"\b(Rotate|Flip|Aura|Merge|Cull|Advance|Hunt|Flank)\b|\bHold\b(?! the line)|\bGuard\b(?! the host)"),
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

    public static List<string> Verify(Chronicle c, BattleReplay r, Func<int, string> kn, Func<Side, int, Trait?>? traits)
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
                    if (i != F.Count - 1 || ck.Value != (int)r.Result.Outcome || ck.A != me || ck.B != they || !text.StartsWith(start) || !text.Contains($"{me}") || !text.Contains($"{they}")
                        || r.Result.Outcome == BattleStatus.AttackerWon && !(me > they) || r.Result.Outcome == BattleStatus.DefenderWon && !(they > me)) bad.Add(w + " the end does not match the last frame and the recorded outcome");
                    break;
                }
            }
        }
        if (c.Lines.Count(x => x.Kind == ChronicleKind.Start) != 1 || c.Lines.Count(x => x.Kind == ChronicleKind.End) != 1 || c.Lines.Count(x => x.Kind == ChronicleKind.Contact) > 1 || c.Lines.Count(x => x.Kind == ChronicleKind.Gate) > 1) bad.Add("a line kind appears the wrong number of times");
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
