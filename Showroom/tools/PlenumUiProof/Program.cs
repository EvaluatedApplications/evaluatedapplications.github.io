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
BunitContext NewCtx(bool prerender)
{
    var c = new BunitContext();
    c.JSInterop.Mode = JSRuntimeMode.Loose;
    c.Services.AddSingleton<IBuildTime>(new Time(prerender));
    c.Services.AddSingleton<IPlenumClock>(clock);
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
}

Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;

sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }
sealed class FakeClock : IPlenumClock { public long NowMs { get; set; } = 1_000_000; }
