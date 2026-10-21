using System.Text.RegularExpressions;
using Bunit;
using Doctrine;
using Doctrine.Lane;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Pages;
using ShowroomLane;

// UI proof for the lane battle's development page. See the csproj header. The page is the REAL one, the match is the REAL service (Tracer navmesh, Pleroma roster); the canvas script is
// a loose stand-in, so this proves the page logic (start, stage, tap, pause, run, hashes, words), not what the canvas looks like.
string showroom = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\.."));
int failures = 0, checks = 0;
void Check(string name, bool ok, string detail = "")
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    if (!ok) failures++;
}
string Text(IRenderedComponent<LaneBattlePage> p, string sel) => Regex.Replace(p.Find(sel).TextContent, @"\s+", " ").Trim();

async Task<bool> Until(Func<bool> cond, int ms)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms) { try { if (cond()) return true; } catch { } await Task.Delay(20); }
    return false;
}

BunitContext NewCtx(out BunitJSModuleInterop module)
{
    var c = new BunitContext();
    c.JSInterop.Mode = JSRuntimeMode.Loose;
    module = c.JSInterop.SetupModule("http://localhost/lane-dev/lane.js");
    module.Mode = JSRuntimeMode.Loose;
    module.Setup<string[]>("icons").SetResult(Enumerable.Range(0, 8).Select(i => "data:image/png;base64,AA==").ToArray());
    return c;
}

// ---------------------------------------------------------------------------------------------- the start screen
using (var c0 = NewCtx(out _))
{
    var p = c0.Render<LaneBattlePage>();
    Check("start: the heading, a seed box, the bots box and the start button are there", p.Markup.Contains("Lane battle") && p.FindAll("#ln-seed").Count == 1 && p.FindAll(".ln-check input").Count == 1 && p.FindAll(".ln-start .ln-btn.strong").Count == 1);
}

// ---------------------------------------------------------------------------------------------- a human match: pause, tap, ready, run, pause
using (var ctx = NewCtx(out var mod))
{
    var page = ctx.Render<LaneBattlePage>();
    page.Find("#ln-seed").Change("7");
    page.Find(".ln-start .ln-btn.strong").Click();
    bool started = await Until(() => page.FindAll(".ln-top").Count == 1, 60000);
    Check("a start builds the lanes and shows the match", started);
    if (!started) { Console.WriteLine("ABORT: " + (page.FindAll(".err").Count > 0 ? Text(page, ".err") : "no error shown")); return 1; }
    await Until(() => mod.Invocations.Any(i => i.Identifier == "init"), 5000);
    Check("the canvas script is loaded and started with the ground and the picture base", mod.Invocations.Any(i => i.Identifier == "init" && i.Arguments.Count == 4 && (i.Arguments[2] as string)!.Contains("\"rows\"") && (i.Arguments[3] as string)!.EndsWith("lane-dev/img/")));
    Check("the first pause: tag, clock of 30 s, ten points", Text(page, ".ln-tag") == "Pause" && Text(page, ".ln-time") == "0:30" && Text(page, ".ln-pts strong") == "10");
    Check("the pause shows three lane buttons, eight packs and five orders", page.FindAll(".ln-lane").Count == 3 && page.FindAll(".ln-pack").Count == 8 && page.FindAll(".ln-ord").Count == 5);
    Check("speed is for the run only (the three speed buttons are disabled in the pause)", page.FindAll(".ln-speed button").All(b => b.HasAttribute("disabled")));

    // tapping on the board (the script calls OnLaneTap) selects a lane
    await page.InvokeAsync(() => page.Instance.OnLaneTap(2));
    Check("a tap on the canvas selects lane 3 (the button is on)", page.FindAll(".ln-lane")[2].ClassList.Contains("on") && page.Markup.Contains("Add soldiers to lane 3"));
    page.FindAll(".ln-lane")[0].Click();
    page.FindAll(".ln-pack")[3].Click();   // archers
    page.FindAll(".ln-pack")[0].Click();   // spearmen
    page.FindAll(".ln-ord")[1].Click();    // hold
    Check("two packs and an order are staged and listed", page.FindAll(".ln-plan li").Count == 3 && Text(page, ".ln-plan").Contains("Archers") && Text(page, ".ln-plan").Contains("Hold"));
    int left = 10 - LaneEconomy.Cost(3) - LaneEconomy.Cost(0);
    Check("the points to spend went down by the two packs", Text(page, ".ln-pts strong") == left.ToString(), $"{Text(page, ".ln-pts strong")} vs {left}");
    Check("the script is told what is staged (ghosts and the order chip)", mod.Invocations.Where(i => i.Identifier == "setInfo").Any(i => (i.Arguments[0] as string)!.Contains("\"stagedPacks\":[{\"lane\":0,\"kind\":3") && (i.Arguments[0] as string)!.Contains("\"stagedOrders\":{\"0\":1}")));
    page.FindAll(".ln-x")[1 - 1].Click();
    Check("taking a pack back lifts it from the list and gives the points back", page.FindAll(".ln-plan li").Count == 2 && Text(page, ".ln-pts strong") == (10 - LaneEconomy.Cost(0)).ToString());
    page.FindAll(".ln-pack")[3].Click();

    // Ready ends the pause: the next Frame applies both sides' plays and starts the run
    page.Find(".ln-ctl .ln-btn.strong").Click();
    byte[]? first = await page.InvokeAsync(() => page.Instance.Frame(0.0));
    Check("Ready ends the pause at once; the first frame of the run hands the canvas a packet", first is { Length: > 32 } && page.FindAll(".ln-tag.run").Count == 1);
    var (hdr, bodies) = LanePack.Unpack(first!);
    Check("the packet holds the bodies of both sides (yours and theirs have arrived)", bodies.Any(b => b.Side == Side.Player) && bodies.Any(b => b.Side == Side.Enemy) && hdr[1] == (int)MatchStage.Run);
    Check("the run shows speed buttons and the clock to the next pause", page.FindAll(".ln-speed button:not([disabled])").Count == 3 && Text(page, ".ln-time") == "0:30");
    page.FindAll(".ln-speed button")[2].Click();
    Check("4x is taken", page.FindAll(".ln-speed button")[2].ClassList.Contains("on"));
    Check("your plays at work: the list names the soldiers sent and the order", Text(page, ".ln-side").Contains("Archers") && Text(page, ".ln-side").Contains("Hold") && Text(page, ".ln-side").Contains("sent"));

    // run to the next pause with 0.125 s frames at 4x: 5 ticks a frame, 60 frames
    int frames = 0; byte[]? last = null;
    while (frames < 200)
    {
        var pk = await page.InvokeAsync(() => page.Instance.Frame(0.125)); frames++;
        if (pk is not null) { last = pk; if (LanePack.Unpack(pk).Header[1] != (int)MatchStage.Run) break; }
    }
    bool paused = await Until(() => page.FindAll(".ln-tag.pause").Count == 1, 3000);
    Check("at 4x the run reaches the next pause in 7.5 s of real time (60 frames)", paused && frames == 60, $"frames {frames}");
    int expectPts = 10 - LaneEconomy.Cost(0) - LaneEconomy.Cost(3) + 10;
    Check("the second pause starts with the points not spent plus ten", Text(page, ".ln-pts strong") == expectPts.ToString(), $"{Text(page, ".ln-pts strong")} vs {expectPts}");
    string hashLine = Text(page, ".ln-hash .mono");
    Check("the hash line has the tick and a short hash for tick 0 and tick 300", Regex.IsMatch(hashLine, @"^0:[0-9a-f]{8}\s+300:[0-9a-f]{8}$"), hashLine);
    Check("a packet was returned for the pause tick (the canvas gets the final picture of the run)", last is { Length: > 32 });
    await page.Instance.DisposeAsync();
    Check("leaving the page tells the script to stop", mod.Invocations.Any(i => i.Identifier == "dispose"));
}

// ---------------------------------------------------------------------------------------------- bots on both sides: the hash line is the batch run's
using (var ctx = NewCtx(out var mod))
{
    var page = ctx.Render<LaneBattlePage>();
    page.Find("#ln-seed").Change("7");
    page.Find(".ln-check input").Change(true);
    page.Find(".ln-start .ln-btn.strong").Click();
    bool started = await Until(() => page.FindAll(".ln-top").Count == 1, 60000);
    Check("bots: the match starts and the pause shows bots only (no Ready button)", started && Text(page, ".ln-time") == "Bots only" && page.FindAll(".ln-ctl .ln-btn.strong").Count == 0);
    int guard = 0;
    while (guard++ < 5000)
    {
        var pk = await page.InvokeAsync(() => page.Instance.Frame(0.125));
        if (pk is not null) { var h = LanePack.Unpack(pk).Header; if (h[0] >= 900 && h[1] == (int)MatchStage.Pause) break; }
    }
    await Until(() => Regex.IsMatch(Text(page, ".ln-hash .mono"), @"900:"), 3000);    string line = Text(page, ".ln-hash .mono");
    var batch = await LaneMatch.CreateAsync(new LaneMatchOptions { Seed = 7, Player = new LaneBot(), Enemy = new LaneBot() });
    await batch.PlayToEndAsync(3);
    string want = string.Join(" ", batch.PauseHashes.Take(4).Select(h => h.Tick + ":" + LaneMatch.ShortHash(h.Hash)));
    Check("bots: the page's hash line equals the batch run's for seed 7 (four pauses)", Regex.Replace(line, @"\s+", " ").StartsWith(want), $"page '{line}' batch '{want}'");
    Check("the hash line tells the dev which command to compare with", Text(page, ".ln-hash").Contains("-- hashes 7"));
    await page.Instance.DisposeAsync();
}

// ---------------------------------------------------------------------------------------------- not on localhost: nothing but a notice
using (var ctx = NewCtx(out _))
{
    ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("https://evaluatedapplications.github.io/tools/lane/");
    var page = ctx.Render<LaneBattlePage>();
    Check("off localhost the page is a notice only (no start button, no canvas)", page.FindAll(".ln-start").Count == 0 && page.FindAll("canvas").Count == 0 && page.Markup.Contains("not available here"));
}

// ---------------------------------------------------------------------------------------------- source scans: words, phone rules, not listed
string Read(string rel) => File.ReadAllText(Path.Combine(showroom, rel));
var page_ = Read(@"Pages\LaneBattlePage.razor"); var css = Read(@"Pages\LaneBattlePage.razor.css");
var words = Read(@"Services\Doctrine\Lane\LaneView.cs"); var js = Read(@"wwwroot\lane-dev\lane.js");
var bannedTerms = new[] { "Pleroma", "Tracer", "EvalApp", "Phasor", "navmesh", "hologram", "holographic", "HoloFormer", "free to use", "open source", "MIT licen" };
string visible = Regex.Replace(page_, @"@code\s*\{.*", "", RegexOptions.Singleline) + words;
Check("the words a player reads carry no builder's term and no licence slip", bannedTerms.All(t => !visible.Contains(t, StringComparison.OrdinalIgnoreCase)), string.Join(",", bannedTerms.Where(t => visible.Contains(t, StringComparison.OrdinalIgnoreCase))));
var all = new[] { page_, css, words, js, Read(@"Services\Doctrine\Lane\LaneMatch.cs"), Read(@"Services\Doctrine\Lane\LaneSeats.cs"), Read(@"Services\Doctrine\Lane\LaneEconomy.cs"), Read(@"Services\Doctrine\Lane\LanePayload.cs"), Read(@"wwwroot\lane-dev\NOTICE.txt") };
Check("no em dash in any new text", all.All(t => !t.Contains('—') && !t.Contains('–')));
Check("the CSS keeps the 44px tap rule, the desktop breakpoint, and no fixed width over 360px", css.Contains("min-height: 44px") && css.Contains("min-width: 1000px") && !Regex.IsMatch(css, @"(?<![-\w])width:\s*(3[6-9]\d|[4-9]\d\d|\d{4,})px"));
Check("the page is unlisted: noindex, no BlazierPage shell, off localhost a notice", page_.Contains("noindex") && !page_.Contains("@attribute [BlazierPage") && page_.Contains("not available here"));
var cs = Read("Showroom.csproj");
Check("a Release publish leaves the lane-dev files out", cs.Contains(@"<Content Remove=""wwwroot\lane-dev\**"" />"));
string tools = Path.Combine(showroom, "..", "web", "data", "tools.json");
Check("the page is not in tools.json, the finish-shells list or the nav", (!File.Exists(tools) || !File.ReadAllText(tools).Contains("\"lane\"")) && !Read(@"scripts\finish-shells.mjs").Contains("'lane'") && !Read(@"Layout\MainLayout.razor").Contains("lane"));

Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;
