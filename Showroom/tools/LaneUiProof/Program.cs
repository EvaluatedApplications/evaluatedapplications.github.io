using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using Doctrine;
using Doctrine.Lane;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Pages;
using ShowroomLane;

// UI proof for the Doctrine page (the lane battle, route /doctrine). See the csproj header. The page is the REAL one, the match is the REAL service (Tracer navmesh, Pleroma roster); the canvas script is
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

BunitContext NewCtx(out BunitJSModuleInterop module, bool prerender = false, string? url = null)
{
    var c = new BunitContext();
    c.Services.AddSingleton<IBuildTime>(new Time(prerender));
    if (url is not null) c.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(url);
    c.JSInterop.Mode = JSRuntimeMode.Loose;
    module = c.JSInterop.SetupModule("http://localhost/doctrine/lane.js");
    module.Mode = JSRuntimeMode.Loose;
    module.Setup<string[]>("icons").SetResult(Enumerable.Range(0, 8).Select(i => "data:image/png;base64,AA==").ToArray());
    return c;
}

// ---------------------------------------------------------------------------------------------- the start screen
using (var c0 = NewCtx(out _))
{
    var p = c0.Render<LaneBattlePage>();
    Check("start: the heading, a match number box and the start button are there; no bots box, no development wording", Text(p, "h1") == "Doctrine" && p.FindAll("#ln-seed").Count == 1 && p.FindAll(".ln-check").Count == 0 && p.FindAll(".ln-start .ln-btn.strong").Count == 1 && !p.Markup.Contains("development", StringComparison.OrdinalIgnoreCase));
    string how = Text(p, ".ln-how");
    Check("start: How to play names the lane, the pack, the order, Ready, 30 s to choose, 30 s to watch and the goal", p.FindAll(".ln-how li").Count == 5 && how.Contains("Pick a lane") && how.Contains("Tap a pack") && how.Contains("Tap an order") && how.Contains("Ready") && how.Contains("30 seconds to choose") && how.Contains("30 seconds") && how.Contains("watch the battle for 30 seconds", StringComparison.OrdinalIgnoreCase) && how.Contains("Destroy the other castle"), how);
}

// ---------------------------------------------------------------------------------------------- a human match: pause, tap, ready, run, pause
using (var ctx = NewCtx(out var mod))
{
    var page = ctx.Render<LaneBattlePage>();
    page.Find("#ln-seed").Change("7");
    page.Find(".ln-start .ln-btn.strong").Click();
    bool started = await Until(() => page.FindAll(".ln-dock").Count == 1, 60000);
    Check("a start builds the lanes and shows the match", started);
    if (!started) { Console.WriteLine("ABORT: " + (page.FindAll(".err").Count > 0 ? Text(page, ".err") : "no error shown")); return 1; }
    await Until(() => mod.Invocations.Any(i => i.Identifier == "init"), 5000);
    Check("the canvas script is loaded and started with the ground and the picture base", mod.Invocations.Any(i => i.Identifier == "init" && i.Arguments.Count == 4 && (i.Arguments[2] as string)!.Contains("\"rows\"") && (i.Arguments[3] as string)!.EndsWith("doctrine/img/")));
    string LastInfo() => (string)mod.Invocations.Where(i => i.Identifier == "setInfo").Last().Arguments[0]!;
    Check("the first pause: tag, clock of 30 s, ten points", Text(page, ".ln-tag") == "Pause" && Text(page, ".ln-time") == "0:30" && Text(page, ".ln-pts strong") == "10");

    // ---- nothing outside the board and the bottom tray
    string[] outside = { ".ln-side", ".ln-lanes", ".ln-lane", ".ln-orders", ".ln-ord", ".ln-top", ".ln-bases", ".ln-plan" };
    Check("no side panel, no lane buttons, no order buttons and no top strip: the stage holds the board and the tray only", outside.All(s => page.FindAll(s).Count == 0) && string.Join(",", page.Find(".ln-stage").Children.Select(c => c.ClassList.First())) == "ln-board,ln-dock", string.Join(",", outside.Where(s => page.FindAll(s).Count > 0)));
    Check("every button of the live page is in the tray (the board is the canvas)", page.FindAll("button").Count > 0 && page.FindAll("button").All(b => b.Closest(".ln-dock") is not null));
    Check("the page holds only the heading, the stage and the credit while it plays", page.Find(".ln").Children.Select(c => c.ClassList.FirstOrDefault() ?? c.TagName.ToLowerInvariant()).SequenceEqual(new[] { "room-head", "ln-stage", "hint" }), string.Join(",", page.Find(".ln").Children.Select(c => c.ClassList.FirstOrDefault() ?? c.TagName)));

    // ---- no lane picked: a plain hint, the packs are there but cannot be used
    Check("no lane picked: the tray says Pick a lane and all eight packs are disabled", Text(page, ".ln-tip").Contains("Pick a lane") && page.FindAll(".ln-pack").Count == 8 && page.FindAll(".ln-pack").All(b => b.HasAttribute("disabled")));
    Check("each pack shows a picture, a name, a cost and its one plain line", page.FindAll(".ln-pack").All(b => b.QuerySelector(".ln-icon") is not null && b.QuerySelector(".ln-pname")!.TextContent.Length > 2 && b.QuerySelector(".ln-pcost")!.TextContent.Contains("points") && b.QuerySelector(".ln-pline")!.TextContent.Contains("strong against")));
    Check("speed is for the run only: no speed buttons in the pause", page.FindAll(".ln-speed").Count == 0);
    var info0 = LastInfo();
    Check("the script is told it is a human pause, the seconds left, and the five orders with the page's words", info0.Contains("\"human\":true") && info0.Contains("\"left\":30") && info0.Contains("\"selected\":-1") && info0.Contains("\"menu\":[{\"order\":0,\"name\":\"Advance\"},{\"order\":1,\"name\":\"Hold\"},{\"order\":3,\"name\":\"Fall back\"},{\"order\":5,\"name\":\"Hunt\"},{\"order\":4,\"name\":\"Guard\"}]"), info0);

    // ---- a tap on a lane (the script calls OnLaneTap) picks it; the packs wake up
    await page.InvokeAsync(() => page.Instance.OnLaneTap(2));
    int affordable = Enumerable.Range(0, 8).Count(k => LaneEconomy.Cost(k) <= 10);
    Check("a tap on lane 3 picks it: the tray names it and the packs the purse allows are enabled", Text(page, ".ln-tip").Contains("Lane 3") && page.FindAll(".ln-pack:not([disabled])").Count == affordable && affordable > 0 && LastInfo().Contains("\"selected\":2"), $"{page.FindAll(".ln-pack:not([disabled])").Count} vs {affordable}");
    await page.InvokeAsync(() => page.Instance.OnLaneTap(-1));
    Check("tapping the picked lane's number puts it down: the hint and the disabled packs come back", Text(page, ".ln-tip").Contains("Pick a lane") && page.FindAll(".ln-pack").All(b => b.HasAttribute("disabled")));

    // ---- packs go into the picked lane
    await page.InvokeAsync(() => page.Instance.OnLaneTap(0));
    page.FindAll(".ln-pack")[3].Click();   // archers, lane 1
    await page.InvokeAsync(() => page.Instance.OnLaneTap(2));
    page.FindAll(".ln-pack")[0].Click();   // spearmen, lane 3
    var info1 = LastInfo();
    Check("a pack is staged in the lane that was picked when it was tapped (archers in lane 1, spearmen in lane 3)", info1.Contains("\"stagedPacks\":[{\"lane\":0,\"kind\":3,\"count\":4},{\"lane\":2,\"kind\":0,\"count\":4}]"), info1);
    Check("the tray lists them by lane, each with a Take back", page.FindAll(".ln-staged .ln-x").Count == 2 && Text(page, ".ln-staged").Contains("Lane 1: Archers") && Text(page, ".ln-staged").Contains("Lane 3: Spearmen") && Text(page, ".ln-staged").Contains("Take back"));
    int left = 10 - LaneEconomy.Cost(3) - LaneEconomy.Cost(0);
    Check("the points left went down by the two packs", Text(page, ".ln-pts strong") == left.ToString(), $"{Text(page, ".ln-pts strong")} vs {left}");

    // ---- orders are set from the lane (the script calls OnOrderTap with the lane the chip sits on)
    await page.InvokeAsync(() => page.Instance.OnOrderTap(0, (int)FieldOrderKind.Hold));
    Check("a tap on the Hold chip of lane 1 sets that lane's order", LastInfo().Contains("\"stagedOrders\":{\"0\":1}"), LastInfo());
    await page.InvokeAsync(() => page.Instance.OnLaneTap(0));
    Check("the tray tells what the staged order does", Text(page, ".ln-tip").Contains("Hold") && Text(page, ".ln-tip").Contains(LaneWords.OrderLine(FieldOrderKind.Hold)));
    await page.InvokeAsync(() => page.Instance.OnOrderTap(0, (int)FieldOrderKind.Hold));
    Check("a second tap on the same chip clears the order", LastInfo().Contains("\"stagedOrders\":{}"), LastInfo());
    await page.InvokeAsync(() => page.Instance.OnOrderTap(0, (int)FieldOrderKind.Hunt));
    await page.InvokeAsync(() => page.Instance.OnOrderTap(0, (int)FieldOrderKind.Hold));
    Check("a different chip replaces the order of that lane (one order a lane)", LastInfo().Contains("\"stagedOrders\":{\"0\":1}"), LastInfo());
    await page.InvokeAsync(() => page.Instance.OnOrderTap(1, (int)FieldOrderKind.Flank));
    await page.InvokeAsync(() => page.Instance.OnOrderTap(9, (int)FieldOrderKind.Hold));
    Check("an order that is not used in a lane, or a lane that is not there, changes nothing", LastInfo().Contains("\"stagedOrders\":{\"0\":1}"), LastInfo());

    page.FindAll(".ln-staged .ln-x")[0].Click();
    Check("Take back lifts the pack from the tray and gives the points back", page.FindAll(".ln-staged .ln-x").Count == 1 && Text(page, ".ln-pts strong") == (10 - LaneEconomy.Cost(0)).ToString());
    page.FindAll(".ln-pack")[3].Click();   // lane 1 is picked again: archers back in lane 1
    Check("the pick is kept: archers go back into lane 1", LastInfo().Contains("{\"lane\":2,\"kind\":0,\"count\":4},{\"lane\":0,\"kind\":3,\"count\":4}"), LastInfo());
    // Ready ends the pause: the next Frame applies both sides' plays and starts the run
    page.Find(".ln-ctl .ln-btn.strong").Click();
    byte[]? first = await page.InvokeAsync(() => page.Instance.Frame(0.0));
    Check("Ready ends the pause at once; the first frame of the run hands the canvas a packet", first is { Length: > 32 } && page.FindAll(".ln-tag.run").Count == 1);
    var (hdr, bodies) = LanePack.Unpack(first!);
    Check("the packet holds the bodies of both sides (yours and theirs have arrived)", bodies.Any(b => b.Side == Side.Player) && bodies.Any(b => b.Side == Side.Enemy) && hdr[1] == (int)MatchStage.Run);
    Check("the run shows speed buttons and the clock to the next pause", page.FindAll(".ln-speed button:not([disabled])").Count == 3 && Text(page, ".ln-time") == "0:30");
    page.FindAll(".ln-speed button")[2].Click();
    Check("4x is taken", page.FindAll(".ln-speed button")[2].ClassList.Contains("on"));
    var runInfo = LastInfo();
    Check("your plays at work: the lanes are told what was sent where (shown on the board, compact) and the tray keeps to one line", runInfo.Contains("\"work\":{\"0\":[\"Archers x4 sent\"],\"2\":[\"Spearmen x4 sent\"]}") && runInfo.Contains("\"stage\":1") && page.FindAll(".ln-packs").Count == 0 && page.FindAll(".ln-tip").Count == 1, runInfo);
    Check("the order you set is now the lane's order, shown on the lane by the script", runInfo.Contains("\"orders\":[[1,0,0]"), runInfo);

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
    Check("a normal match shows no hash line (that is for the check mode only)", page.FindAll(".ln-hash").Count == 0);
    Check("a packet was returned for the pause tick (the canvas gets the final picture of the run)", last is { Length: > 32 });
    await page.Instance.DisposeAsync();
    Check("leaving the page tells the script to stop", mod.Invocations.Any(i => i.Identifier == "dispose"));
}

// ---------------------------------------------------------------------------------------------- bots on both sides: the hash line is the batch run's
using (var ctx = NewCtx(out var mod, url: "http://localhost/doctrine?seed=7&bots=1"))
{
    var page = ctx.Render<LaneBattlePage>();
    page.Find(".ln-start .ln-btn.strong").Click();
    bool started = await Until(() => page.FindAll(".ln-dock").Count == 1, 60000);
    Check("bots: the match starts and the pause shows bots only (no Ready button)", started && Text(page, ".ln-time") == "Bots only" && page.FindAll(".ln-ctl .ln-btn.strong").Count == 0 && page.FindAll(".ln-pack").Count == 0);
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
    await page.Instance.DisposeAsync();
}

// ---------------------------------------------------------------------------------------------- the static shell (build time): real text, a start panel, no match, no canvas
using (var ctx = NewCtx(out var mod, prerender: true))
{
    var page = ctx.Render<LaneBattlePage>();
    Check("shell: the intro and How to play are real text, a start panel stands in for the game, no canvas and no match", page.FindAll(".ln-how li").Count == 5 && page.FindAll(".tool-start").Count == 1 && page.FindAll(".ln-start").Count == 0 && page.FindAll("canvas").Count == 0 && Text(page, ".lede").Contains("Two castles, three lanes"));
    Check("shell: no script is loaded at build time", mod.Invocations.Count == 0);
}
using (var ctx = NewCtx(out _))
{
    var page = ctx.Render<LaneBattlePage>();
    Check("the match number box can hold a number and the hash line is hidden outside the check mode", page.FindAll(".ln-hash").Count == 0 && page.Find("#ln-seed").GetAttribute("type") == "number");
}
// ---------------------------------------------------------------------------------------------- source scans: words, phone rules, not listed
string Read(string rel) => File.ReadAllText(Path.Combine(showroom, rel));
var page_ = Read(@"Pages\LaneBattlePage.razor"); var css = Read(@"Pages\LaneBattlePage.razor.css");
var words = Read(@"Services\Doctrine\Lane\LaneView.cs"); var js = Read(@"wwwroot\doctrine\lane.js");
var bannedTerms = new[] { "Pleroma", "Tracer", "EvalApp", "Phasor", "navmesh", "hologram", "holographic", "HoloFormer", "free to use", "open source", "MIT licen", "development", "dev page", "localhost" };
string visible = Regex.Replace(page_, @"@code\s*\{.*", "", RegexOptions.Singleline) + words;
Check("the words a player reads carry no builder's term and no licence slip", bannedTerms.All(t => !visible.Contains(t, StringComparison.OrdinalIgnoreCase)), string.Join(",", bannedTerms.Where(t => visible.Contains(t, StringComparison.OrdinalIgnoreCase))));
var all = new[] { page_, css, words, js, Read(@"Services\Doctrine\Lane\LaneMatch.cs"), Read(@"Services\Doctrine\Lane\LaneSeats.cs"), Read(@"Services\Doctrine\Lane\LaneEconomy.cs"), Read(@"Services\Doctrine\Lane\LanePayload.cs"), Read(@"wwwroot\doctrine\NOTICE.txt") };
Check("no em dash in any new text", all.All(t => !t.Contains('—') && !t.Contains('–')));
Check("the CSS keeps the 44px tap rule, the wide-screen breakpoint, and no fixed width over 360px", css.Contains("min-height: 44px") && css.Contains("min-width: 720px") && !Regex.IsMatch(css, @"(?<![-\w])width:\s*(3[6-9]\d|[4-9]\d\d|\d{4,})px"));
Check("the source has no side panel or order buttons left (page and styles)", new[] { "ln-side", "ln-lanes", "ln-orders", "ln-ord", "ln-top", "ln-bases" }.All(s => !page_.Contains(s) && !css.Contains(s)));
Check("the order chips on the lane are at least 44 px tall and are hit-tested in the canvas script; the page has an OnOrderTap for them", js.Contains("const TAP = 44") && js.Contains("function menuBoxes") && js.Contains("function hitAt") && page_.Contains("public async Task OnOrderTap(int lane, int order)"));
Check("sound is made in the script with Web Audio: no sound file, no audio tag, the audio context is built in one place only (the gesture handler)", !Regex.IsMatch(js, @"\.(mp3|ogg|wav|m4a|flac)\b|new Audio\(|<audio") && Regex.Matches(js, @"new AC\(").Count == 1 && Directory.GetFiles(Path.Combine(showroom, "wwwroot"), "*.*", SearchOption.AllDirectories).All(f => !Regex.IsMatch(f, @"\.(mp3|ogg|wav|m4a|flac)$", RegexOptions.IgnoreCase)));
Check("the page is the published Doctrine tool: route /doctrine, a Blazier shell, indexable, no local-only gate", page_.StartsWith("@page \"/doctrine\"") && page_.Contains("[BlazierPage(\"doctrine\"") && page_.Contains("Static = true") && !page_.Contains("noindex") && !page_.Contains("not available here") && !page_.Contains("localhost"));
var cs = Read("Showroom.csproj");
Check("a Release publish carries the pictures and the script (no exclusion of wwwroot\\doctrine or lane-dev)", !cs.Contains(@"wwwroot\doctrine") && !cs.Contains("lane-dev"));
Check("the assets live in wwwroot\\doctrine with both licence notices; wwwroot\\lane-dev is gone", File.Exists(Path.Combine(showroom, @"wwwroot\doctrine\lane.js")) && File.Exists(Path.Combine(showroom, @"wwwroot\doctrine\NOTICE.txt")) && File.Exists(Path.Combine(showroom, @"wwwroot\doctrine\KENNEY-LICENSE.txt")) && Directory.GetFiles(Path.Combine(showroom, @"wwwroot\doctrine\img"), "*.png").Length == 7 && !Directory.Exists(Path.Combine(showroom, @"wwwroot\lane-dev")));
string shells = Read(@"scripts\finish-shells.mjs");
var dline = shells.Split('\n').Single(l => l.TrimStart().StartsWith("'doctrine':"));
Check("finish-shells describes the lane battle for Doctrine and does not noindex it", dline.Contains("lane battle") && !dline.Contains("noindex") && !dline.Contains("navigation mesh") && !shells.Contains("'lane'"));
string[] oldFiles = { @"Pages\DoctrinePage.razor", @"Pages\DoctrinePage.razor.css", @"Components\DoctrineArena.razor", @"Components\DoctrineChronicle.razor", @"Components\DoctrineArena.razor.css", @"Components\DoctrineChronicle.razor.css", @"Services\Doctrine\DoctrineWords.cs", @"Services\Doctrine\DoctrineRoot.cs", @"Services\Doctrine\DoctrineSession.cs" };
Check("the old Doctrine page, components, styles and services are out of the build", oldFiles.All(f => !File.Exists(Path.Combine(showroom, f))), string.Join(",", oldFiles.Where(f => File.Exists(Path.Combine(showroom, f)))));
Check("nothing of the old game is registered at start-up", !Read("Program.cs").Contains("ShowroomDoctrine") && !Read(@"Services\ShowroomPrerender.cs").Contains("ShowroomDoctrine"));
var oneRoute = Directory.GetFiles(Path.Combine(showroom, "Pages"), "*.razor").Where(f => File.ReadAllText(f).Contains("@page \"/doctrine\"")).Count();
Check("exactly one page answers /doctrine", oneRoute == 1);
Check("the nav does not mention the page by a second name", !Read(@"Layout\MainLayout.razor").Contains("lane"));

Console.WriteLine(failures == 0 ? $"ALL PASS ({checks} checks)" : $"{failures} FAILED of {checks}");
return failures == 0 ? 0 : 1;

sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }