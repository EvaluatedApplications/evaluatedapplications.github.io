using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShowroomPlenum;

// UI proof for The Plenum. See the csproj header.
string showroom = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\.."));
int failures = 0, checks = 0;
void Check(string name, bool ok, string detail = "")
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    if (!ok) failures++;
}
async Task<bool> Until(Func<bool> cond, int ms, string what)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms) { try { if (cond()) return true; } catch { } await Task.Delay(40); }
    Console.WriteLine($"  (timed out after {ms} ms waiting for: {what})");
    return false;
}

var clock = new FakeClock();
BunitContext NewCtx(bool prerender, IPlenumRelayFactory? relays = null)
{
    var c = new BunitContext();
    c.JSInterop.Mode = JSRuntimeMode.Loose;
    c.Services.AddSingleton<IBuildTime>(new Time(prerender));
    c.Services.AddSingleton<IPlenumClock>(clock);
    c.Services.AddSingleton<IPlenumRelayFactory>(relays ?? new LoopbackRelayFactory(new LoopbackBroker()));
    return c;
}

// ---------------------------------------------------------------------------------------------- the static shell
using (var sctx = NewCtx(true))
{
    var shell = sctx.Render<Showroom.Pages.Plenum>();
    Check("shell: the heading and lede are real text", shell.Markup.Contains("The Plenum") && shell.Find(".lede").TextContent.Contains("one array of exact whole numbers"));
    Check("shell: the start button is there and the game is not", shell.FindAll("[data-blazier-go]").Count == 1 && shell.FindAll(".pl-hand").Count == 0);
}

// ------------------------------------------------------------------------------------------------ the live page
using var ctx = NewCtx(false);
var page = ctx.Render<Showroom.Pages.Plenum>();
bool Has(string sel) => page.FindAll(sel).Count > 0;
string Text(string sel) => page.Find(sel).TextContent.Trim();
async Task Tap(string sel, int skip = 0) { var el = page.FindAll(sel)[skip]; await page.InvokeAsync(() => el.Click()); }
async Task TapCard(string name)
{
    var idx = page.FindAll(".pl-card").ToList().FindIndex(e => e.QuerySelector(".nm")!.TextContent == name);
    if (idx < 0) throw new Exception("card not in hand: " + name);
    await Tap(".pl-card", idx);
}
async Task TapOp(string name)
{
    var idx = page.FindAll(".pl-opbtn").ToList().FindIndex(e => e.QuerySelector(".nm")!.TextContent == name);
    await Tap(".pl-opbtn", idx);
}
async Task TapTag(string name)
{
    var idx = page.FindAll(".pl-tag").ToList().FindIndex(e => e.TextContent == name);
    await Tap(".pl-tag", idx);
}

Check("renders: status, hand, operation cards, board, array, engine", Has(".pl-status") && Has(".pl-hand") && Has(".pl-ops") && Has(".pl-board") && Has(".pl-array") && Has(".pl-engine"));
Check("three operation cards only", page.FindAll(".pl-opbtn").Count == 3);
Check("the first deal's hand holds Ember and Spark", page.FindAll(".pl-card .nm").Any(e => e.TextContent == "Ember") && page.FindAll(".pl-card .nm").Any(e => e.TextContent == "Spark"));
Check("the Board opens with 8 cards and no Fire", Text(".pl-board h2").Contains("8 cards") && Text(".pl-chips li:nth-child(3)").Contains("Fire on the Board: 0 of 8"));
Check("the array view draws 286 cells, none changed yet", page.FindAll(".pl-cell").Count == 286 && page.FindAll(".pl-cell.changed").Count == 0);
Check("the page asked the browser to report tab visibility", ctx.JSInterop.Invocations.Any(i => i.Identifier == "plenumWatch"));

// ------------------------------------------------------------------------------- scripted three plays reach the Goal
await TapOp("Fuse");
Check("Fuse arms: the hint asks for the first card", Text(".pl-hand .hint").Contains("Fuse: tap the first card"));
await TapCard("Ember");
Check("the first card is marked", Has(".pl-card.picked") && Text(".pl-hand .hint").Contains("Ember chosen"));
await TapCard("Spark");
Check("Fuse made Wisp on the Board (call and sentence shown)", Text(".pl-did .pl-call").Contains("a.Bind(b)") && Text(".pl-did .pl-says").Contains("Wisp"), Text(".pl-did .pl-says"));
Check("play 1 of 3 used", Text(".pl-chips li:nth-child(2)").Contains("Plays left 2"));
await TapOp("Flip");   // Fire is the default tag
Check("Flip shows the real call and that twice undoes it", Text(".pl-did .pl-call").Contains("Board.RelabelFlags(null, 0b00000001)") && Text(".pl-did .pl-says").Contains("Twice undoes it"), Text(".pl-did .pl-says"));
Check("Flip highlights changed cells in the array", page.FindAll(".pl-cell.changed").Count > 0, page.FindAll(".pl-cell.changed").Count + " cells");
await TapOp("Tally");
Check("Tally reads 8 Fire", Text(".pl-did .pl-says").Contains("8 cards carry Fire"), Text(".pl-did .pl-says"));
Check("the Goal is reached in three plays", Text(".pl-chips li:nth-child(3)") == "Goal reached" && Text(".pl-chips li:nth-child(2)").Contains("Plays left 0"));
await TapOp("Flip");
Check("with no plays left a card is refused with a plain sentence", Text(".pl-did .pl-says").Contains("No plays left"));

// ----------------------------------------------------------------------------------------------- the Flipper's turn
await Tap(".pl-btn.strong");
Check("End turn: the Flipper uses the same verb on Fire", Text(".pl-did .pl-says").Contains("The Flipper used the same verb") && Text(".pl-chips li:nth-child(1)").Contains("Turn 2"));
Check("plays refilled", Text(".pl-chips li:nth-child(2)").Contains("Plays left 3"));

// ------------------------------------------------------------------------------------------ the unreadable hand
{
    int guard = 0;
    while (!Has(".pl-unread") && guard++ < 40) await Tap(".pl-turn .pl-btn", 0);   // Draw
    Check("drawing past 8 kinds makes the hand unreadable", Has(".pl-unread"), $"{guard} draws");
    var t = Text(".pl-unread");
    Check("the page explains it: a hand can only be read with 8 kinds or fewer", t.Contains("A hand can only be read with 8 kinds or fewer"));
    Check("unreadable hand: face-down cards and exact tag counts, no card names", Has(".pl-back") && page.FindAll(".pl-unread .pl-chips li").Count == 8 && !Has(".pl-card"));
    await TapOp("Fuse");
    Check("an operation that needs the hand says why it cannot run", Text(".pl-did .pl-says").Contains("cannot be read"));
    await Tap(".pl-unread .pl-btn");
    Check("putting the hand back makes it readable again", !Has(".pl-unread") && Text(".pl-hand h2").Contains("0 cards"));
}

// ----------------------------------------------------------------------------------------------------- array view
{
    await Tap(".pl-zone", 4);   // Engine
    Check("Engine store shows 291 cells", page.FindAll(".pl-cell").Count == 291);
    await Tap(".pl-zone", 0);   // Board
    await Tap(".pl-cell", 5);
    Check("tapping a cell reads its exact number", Text(".pl-readout").Contains("Cell 5 of 286:"), Text(".pl-readout"));
    Check("the cells are not buttons (none of 286 needs a 44px target) and carry no title tooltip", page.FindAll(".pl-cell").All(c => c.TagName == "SPAN" && c.GetAttribute("title") is null));
}

// ----------------------------------------------------------------------------------------- engine, hidden tab, return
{
    string Line() => Text(".pl-engine .hint");
    clock.NowMs += 6000;   // three ticks of 2 s
    Check("the Engine ticks on its own timer: Engine.Tick(3)", await Until(() => Line().Contains("Engine.Tick(3)"), 3000, "timer tick"), Line());
    Check("harvest is shown as a read, not a counter", Text(".pl-engine .pl-says").Contains("Engine.Times()") && Text(".pl-engine .pl-says").Contains("not a counter"));
    await page.InvokeAsync(() => page.Instance.OnVisibility(true));      // tab hidden
    clock.NowMs += 8L * 3600 * 1000;                                     // 8 hours away
    await Task.Delay(700);
    Check("while hidden the Engine does not tick", Line().Contains("Engine.Tick(3)"), Line());
    await page.InvokeAsync(() => page.Instance.OnVisibility(false));     // back
    Check("on return the time away is one Tick(14400)", await Until(() => Line().Contains("Engine.Tick(14400)"), 3000, "catch-up"), Line());
}

// ------------------------------------------------------------------------------------- tap-to-open call notes, text
{
    var q = page.FindAll(".pl-q")[0];
    Check("a call note is closed until tapped", !Has(".pl-callnote"));
    await Tap(".pl-q", 0);
    Check("tapping the ? opens the real call (no hover needed)", Has(".pl-callnote") && Text(".pl-callnote").Contains("CountWhere"));
    string visible = page.Markup;
    Check("no em dash in the page text", !visible.Contains('—'));
    foreach (var bad in new[] { "universal", "complete", "open source", "free to use", "MIT", "chance", "luck", "lucky" })
        Check($"no '{bad}' wording", !Regex.IsMatch(page.Find(".plenum").TextContent, @"\b" + Regex.Escape(bad) + @"\b", RegexOptions.IgnoreCase));
}

// ------------------------------------------------------------------------------------------------ the duel, through the real page
var broker = new LoopbackBroker();
var relays = new LoopbackRelayFactory(broker);
using var ctxA = NewCtx(false, relays);
using var ctxB = NewCtx(false, relays);
using var ctxC = NewCtx(false, relays);
var A = ctxA.Render<Showroom.Pages.Plenum>();
var B = ctxB.Render<Showroom.Pages.Plenum>();
var C = ctxC.Render<Showroom.Pages.Plenum>();
bool Has2(IRenderedComponent<Showroom.Pages.Plenum> p, string sel) => p.FindAll(sel).Count > 0;
string Text2(IRenderedComponent<Showroom.Pages.Plenum> p, string sel) => p.Find(sel).TextContent.Trim();
async Task Tap2(IRenderedComponent<Showroom.Pages.Plenum> p, string sel, int skip = 0) { var el = p.FindAll(sel)[skip]; await p.InvokeAsync(() => el.Click()); }
async Task TapOp2(IRenderedComponent<Showroom.Pages.Plenum> p, string name)
{
    var idx = p.FindAll(".pl-opbtn").ToList().FindIndex(e => e.QuerySelector(".nm")!.TextContent == name);
    await Tap2(p, ".pl-opbtn", idx);
}
async Task SetNick(IRenderedComponent<Showroom.Pages.Plenum> p, string nick) => await p.InvokeAsync(() => p.Find("#pl-nick").Change(nick));
async Task OpenLobby(IRenderedComponent<Showroom.Pages.Plenum> p, string nick)
{
    await Tap2(p, ".pl-mode", 1);
    await Until(() => Has2(p, "#pl-nick"), 3000, "lobby");
    await SetNick(p, nick);
}

Check("the page opens on Solo with a Solo / Duel switch (two 44px buttons)", Has(".pl-modes") && page.FindAll(".pl-mode").Count == 2 && Text(".pl-mode.on") == "Solo");
await OpenLobby(A, "Ann");
Check("Duel opens the lobby: nickname field, create button, room code field", Has2(A, "#pl-nick") && Has2(A, "#pl-code") && Text2(A, ".pl-create .pl-btn.strong") == "Create a room" && Text2(A, ".pl-lobby").Contains("No open rooms right now"));
var nick = A.Find("#pl-nick");
var code = A.Find("#pl-code");
Check("mobile fields: nickname uses a name keyboard (autocapitalize words, 16 chars), the code field upper-cases and turns off autocorrect",
    nick.GetAttribute("autocapitalize") == "words" && nick.GetAttribute("maxlength") == "16" && nick.GetAttribute("inputmode") == "text" &&
    code.GetAttribute("autocapitalize") == "characters" && code.GetAttribute("autocorrect") == "off" && code.GetAttribute("spellcheck") == "false" && code.GetAttribute("autocomplete") == "off");
Check("only the nickname and the code take the keyboard (no other input in the page)", A.FindAll("input").Count == 2);
Check("the nickname was written to this browser only (localStorage call, guarded in index.html)", ctxA.JSInterop.Invocations.Any(i => i.Identifier == "plenumNickSet"));

await Tap2(A, ".pl-create .pl-btn.strong");
Check("Create a room: the host sits in a room with a 6-character code", await Until(() => Has2(A, ".pl-roombar"), 3000, "room") && System.Text.RegularExpressions.Regex.IsMatch(Text2(A, ".pl-roomline b"), @"^Room [A-HJ-NP-Z2-9]{6}$"), Text2(A, ".pl-roomline"));
string roomCode = Text2(A, ".pl-roomline b").Substring(5);
Check("the host waits: 'Waiting for an opponent', the code and the help", Text2(A, ".pl-roombar").Contains("Waiting for an opponent") && Text2(A, ".pl-help").Contains("same message relay as Prism's swarm") && Text2(A, ".pl-help").Contains("anyone who knows the room can see it"));
Check("the retained listing is on the relay", broker.RetainedTopics.Contains(PlenumTopics.Lobby(roomCode)));

await OpenLobby(B, "Bo");
Check("the second visitor's lobby lists the room with the host name and a Join button (a 56px row)", Has2(B, ".pl-room") && Text2(B, ".pl-room .who") == "Ann" && Text2(B, ".pl-room .go") == "Join" && Text2(B, ".pl-room .meta").Contains(roomCode));
await Tap2(B, ".pl-room");
Check("tapping the listed room seats the guest; both pages show the match",
    await Until(() => Has2(B, ".pl-draft") && Has2(A, ".pl-draft"), 4000, "both seated"));
Check("the opponent summary is always visible: name, Fire count, hand size, order state, last plays",
    Text2(A, ".pl-opp .pl-oppline").Contains("Bo") && Text2(A, ".pl-opp .pl-chips").Contains("Fire 0 of 8") && Text2(A, ".pl-opp .pl-chips").Contains("hand 6 cards") && Text2(A, ".pl-opp .pl-chips").Contains("no rounds yet") && Text2(A, ".pl-opp .pl-oppline").Contains("still choosing"));
Check("their Board is a collapsible, closed by default, whose summary is a tap target; the summary strip comes before it",
    Has2(A, "details.pl-oppboard") && A.Find("details.pl-oppboard").GetAttribute("open") is null && A.Find(".pl-opp").Children.ToList().FindIndex(e => e.ClassName == "pl-chips") < A.Find(".pl-opp").Children.ToList().FindIndex(e => e.TagName == "DETAILS"));
Check("no Draw, End turn, Engine or New deal in a duel", !A.Markup.Contains("Draw a card") && !Has2(A, ".pl-engine") && !A.Markup.Contains("New deal"));
Check("a turn clock is showing (90 s) and the status reads Round 1", Text2(A, ".pl-status .pl-chips").Contains("Round 1") && A.Markup.Contains("Turn clock 1:30"));

// seat 0 (Ann): Flip my Board, Tally, send. seat 1 (Bo): send an empty order.
await TapOp2(A, "Flip");
Check("Flip shows its effect before sending (the order list and the Board)", Text2(A, ".pl-plays").Contains("Flip Fire") && Text2(A, ".pl-status .pl-chips").Contains("Plays left 2"), Text2(A, ".pl-plays"));
await TapOp2(A, "Tally");
Check("Tally on the preview cashes the Goal in the preview", Text2(A, ".pl-status .pl-chips").Contains("Goal reached") && A.FindAll(".pl-plays li").Count == 2);
await Tap2(A, ".pl-draft .pl-btn", 0);   // Undo last play
Check("Undo removes the last play and the preview follows", A.FindAll(".pl-plays li").Count == 1 && Text2(A, ".pl-status .pl-chips").Contains("Plays left 2") && !Text2(A, ".pl-status .pl-chips").Contains("Goal reached"));
await TapOp2(A, "Tally");
await Tap2(A, ".pl-draft .pl-btn.strong");
Check("Send order: the page says it is waiting for the opponent", await Until(() => Has2(A, ".pl-draft") && Text2(A, ".pl-draft").Contains("Your order is in"), 3000, "sent"));
Check("the opponent's summary shows their state without their order", Text2(B, ".pl-opp .pl-oppline").Contains("Ann") && Text2(B, ".pl-opp .pl-oppline").Contains("order sent") && !B.Markup.Contains("Flip Fire on my"));
await Tap2(B, ".pl-draft .pl-btn.strong");
Check("both orders in: the round is applied and the match is over on BOTH pages",
    await Until(() => Has2(A, ".pl-result") && Has2(B, ".pl-result"), 4000, "result"));
Check("seat 0 sees 'You win', seat 1 sees the winner's name", Text2(A, ".pl-result .pl-says").StartsWith("You win.") && Text2(B, ".pl-result .pl-says").StartsWith("Ann wins."), Text2(B, ".pl-result .pl-says"));
Check("the room left the list when the match ended", !broker.RetainedTopics.Contains(PlenumTopics.Lobby(roomCode)));

// second match: join by typed code, a spectator, Flip aimed at their Board
await Tap2(A, ".pl-result .pl-btn.strong");
await Tap2(B, ".pl-result .pl-btn.strong");
Check("Back to the lobby returns both to the lobby", await Until(() => Has2(A, "#pl-nick") && Has2(B, "#pl-nick"), 4000, "back"));
Check("the nickname is still there", A.Find("#pl-nick").GetAttribute("value") == "Ann");
await Tap2(A, ".pl-create .pl-btn:not(.strong)");
Check("the Private switch is a button with aria-checked", A.Find(".pl-create .pl-btn[role=switch]").GetAttribute("aria-checked") == "true" && Text2(A, ".pl-create").Contains("Private: on"));
await Tap2(A, ".pl-create .pl-btn.strong");
await Until(() => Has2(A, ".pl-roombar"), 3000, "room 2");
string code2 = Text2(A, ".pl-roomline b").Substring(5);
Check("a private room says it is not listed and is not on the lobby topic", Text2(A, ".pl-roombar").Contains("not listed") && broker.RetainedTopics.Contains(PlenumTopics.Private(code2)) && !broker.RetainedTopics.Contains(PlenumTopics.Lobby(code2)));
Check("and the other visitor's list stays empty", !Has2(B, ".pl-room"));
await B.InvokeAsync(() => B.Find("#pl-code").Change(" " + code2.ToLowerInvariant()));
await Tap2(B, ".pl-codeline .pl-btn");
Check("typing the code (lower case) joins the private room", await Until(() => Has2(B, ".pl-draft") && Has2(A, ".pl-draft"), 4000, "joined by code"));
await OpenLobby(C, "Cy");
await B.InvokeAsync(() => { });
await C.InvokeAsync(() => C.Find("#pl-code").Change(code2));
await Tap2(C, ".pl-codeline .pl-btn");
Check("a third visitor watches: both players shown, no hand, no operation cards, no order buttons", await Until(() => Has2(C, ".pl-opp"), 4000, "spectator") && !Has2(C, ".pl-hand") && !Has2(C, ".pl-ops") && !Has2(C, ".pl-draft") && Text2(C, ".pl-opp").Contains("You are watching") && C.FindAll(".pl-oppline").Count == 2);
// Ann flips THEIR board (Bo's), Bo only tallies: Bo reads 8 Fire after Ann's order and wins
await Tap2(A, ".pl-aim .pl-tag", 1);
Check("the aim switch picks 'Flip their Board'", A.FindAll(".pl-aim .pl-tag")[1].GetAttribute("aria-checked") == "true");
await TapOp2(A, "Flip");
Check("the order list names the target and the call is Opponent.Board.RelabelFlags", Text2(A, ".pl-plays").Contains("on their Board") && Text2(A, ".pl-did .pl-call").Contains("Opponent.Board.RelabelFlags(null, 0b00000001)"), Text2(A, ".pl-plays"));
await Tap2(A, ".pl-draft .pl-btn.strong");
await TapOp2(B, "Tally");
await Tap2(B, ".pl-draft .pl-btn.strong");
Check("Flip on the other Board decided it: seat 1 wins, on all three pages", await Until(() => Has2(A, ".pl-result") && Has2(B, ".pl-result") && Has2(C, ".pl-result"), 4000, "result 2") && Text2(B, ".pl-result .pl-says").StartsWith("You win.") && Text2(C, ".pl-result .pl-says").StartsWith("Bo wins.") && Text2(A, ".pl-result .pl-says").StartsWith("Bo wins."));

// an unreachable relay
{
    var dead = new LoopbackBroker { Reachable = false };
    using var ctxD = NewCtx(false, new LoopbackRelayFactory(dead));
    var D = ctxD.Render<Showroom.Pages.Plenum>();
    await Tap2(D, ".pl-mode", 1);
    Check("unreachable relay: the plain message, a Retry button, and Solo still there", await Until(() => Has2(D, ".err"), 3000, "unreachable") && Text2(D, ".err") == "Can't reach the lobby. Solo still works." && Text2(D, ".pl-lobby .pl-btn") == "Retry" && Has2(D, ".pl-mode"));
    dead.Reachable = true;
    await Tap2(D, ".pl-lobby .pl-btn");
    Check("Retry reaches the lobby once the relay is back", await Until(() => Has2(D, "#pl-nick"), 3000, "retry"));
    await Tap2(D, ".pl-mode", 0);
    Check("Solo still plays after the lobby", Has2(D, ".pl-hand") && Has2(D, ".pl-engine") && !Has2(D, ".pl-lobby"));
}

// wording across the duel states (help text included)
{
    string all = A.Find(".plenum").TextContent + " " + B.Find(".plenum").TextContent + " " + C.Find(".plenum").TextContent;
    Check("no em dash anywhere in the duel pages", !A.Markup.Contains('—') && !B.Markup.Contains('—') && !C.Markup.Contains('—'));
    foreach (var bad in new[] { "universal", "complete", "open source", "free to use", "MIT", "chance", "luck", "lucky", "guaranteed", "unbeatable" })
        Check($"no '{bad}' wording in the duel pages", !Regex.IsMatch(all, @"\b" + Regex.Escape(bad) + @"\b", RegexOptions.IgnoreCase));
}
// -------------------------------------------------------------------------------- layout rules (no browser needed)
{
    string css = File.ReadAllText(Path.Combine(showroom, "Pages", "Plenum.razor.css"));
    string index = File.ReadAllText(Path.Combine(showroom, "wwwroot", "index.html"));
    Check("viewport meta: width=device-width, initial-scale=1", Regex.IsMatch(index, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1(\\.0)?\""));
    Check("one rule gives every button and summary a 44px minimum", Regex.IsMatch(css, @"\.plenum button, \.plenum summary \{[^}]*min-height: 44px;[^}]*min-width: 44px;"));
    Check("touch-action: manipulation on tap targets", css.Contains("touch-action: manipulation"));
    var fixedWidths = Regex.Matches(css, @"(?<![-\w])width\s*:\s*(\d+)px").Select(m => int.Parse(m.Groups[1].Value)).ToList();
    Check("no fixed width above 360px", fixedWidths.All(w => w <= 360), "fixed px widths: " + string.Join(",", fixedWidths));
    Check("no min-width in px above 360", Regex.Matches(css, @"(?<!\()min-width\s*:\s*(\d+)px").All(m => int.Parse(m.Groups[1].Value) <= 360));
    Check("media queries present (wide screens, narrow phones, landscape, reduced motion)", css.Contains("@media (min-width: 700px)") && css.Contains("@media (min-width: 960px)") && css.Contains("@media (max-width: 359px)") && css.Contains("orientation: landscape") && css.Contains("prefers-reduced-motion"));
    Check("16px side gutters with safe-area insets", css.Contains("max(16px, env(safe-area-inset-left))") && css.Contains("max(16px, env(safe-area-inset-right))") && css.Contains("safe-area-inset-bottom"));
    Check("no hover-only rules", !css.Contains(":hover"));
    Check("the page cannot scroll sideways (overflow-x: clip on .room)", css.Contains("overflow-x: clip"));
    Check("base grid is the phone grid (13 columns of cells, 2 columns of hand cards)", css.Contains("repeat(13, minmax(0, 1fr))") && Regex.IsMatch(css, @"\.pl-cards \{[^}]*repeat\(2, minmax\(0, 1fr\)\)"));
    Check("every interactive element in the rendered page is a button or summary (nothing relies on hover or right click)", page.FindAll("[onclick], [onmouseover], [oncontextmenu]").All(e => e.TagName is "BUTTON" or "SUMMARY" or "SPAN"));
    Check("no title attributes (no hover tooltips)", page.FindAll("[title]").Count == 0);
    Check("no fixed-px inline styles in the markup", !Regex.IsMatch(page.Markup, @"style=""[^""]*\d+px"));
    Check("inputs are 16px with a 48px minimum (the phone does not zoom on focus)", Regex.IsMatch(css, @"\.pl-input \{[^}]*min-height: 48px;[^}]*font-size: 16px;"));
    Check("lobby room rows are at least 56px and fill the width", Regex.IsMatch(css, @"\.pl-room \{[^}]*width: 100%;[^}]*min-height: 56px"));
    Check("the code field and its button share a row that cannot overflow (minmax(0, 1fr))", Regex.IsMatch(css, @"\.pl-codeline \{[^}]*grid-template-columns: minmax\(0, 1fr\) auto"));
    Check("the room list is one column on a phone", Regex.IsMatch(css, @"\.pl-roomlist \{[^}]*display: grid;") && !Regex.IsMatch(css, @"\.pl-roomlist \{[^}]*grid-template-columns"));
    Check("the opponent's Board collapsible has a 44px summary through the shared rule", css.Contains(".plenum button, .plenum summary {") && Regex.IsMatch(css, @"\.pl-help summary, \.pl-oppboard summary \{[^}]*display: flex"));
    Check("long names wrap instead of widening the page", css.Contains(".pl-room .who { font-weight: 700; overflow-wrap: anywhere; }") && css.Contains(".pl-roomline, .pl-oppline { margin: 0 0 0.4rem; overflow-wrap: anywhere; }"));
    Check("the duel's own media-free rules add no px width and no hover", !css.Contains(":hover"));
    Check("every duel button, link and field in the rendered duel pages is a button, summary, input or span (no hover or right-click handlers)", new[] { A, B, C }.All(p => p.FindAll("[onclick], [onmouseover], [oncontextmenu]").All(e => e.TagName is "BUTTON" or "SUMMARY" or "SPAN")));
    Check("no title attributes in the duel pages", new[] { A, B, C }.All(p => p.FindAll("[title]").Count == 0));
    Check("no fixed-px inline styles in the duel markup", new[] { A, B, C }.All(p => !Regex.IsMatch(p.Markup, @"style=""[^""]*\d+px")));
}

Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;

sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }
sealed class FakeClock : IPlenumClock { public long NowMs { get; set; } = 1_000_000; public long UnixMs { get; set; } = 1_760_000_000_000; }
