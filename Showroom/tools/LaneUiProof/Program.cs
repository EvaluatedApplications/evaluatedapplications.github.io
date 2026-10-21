using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using Doctrine;
using Doctrine.Lane;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Pages;
using ShowroomLane;
using ShowroomPlenum;

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
string TextOr(IRenderedComponent<LaneBattlePage> p, string sel) => p.FindAll(sel).FirstOrDefault() is { } e ? Regex.Replace(e.TextContent, @"\s+", " ").Trim() : "";
AngleSharp.Dom.IElement Btn(IRenderedComponent<LaneBattlePage> p, string text) => p.FindAll("button").First(b => Regex.Replace(b.TextContent, @"\s+", " ").Trim() == text);
bool HasBtn(IRenderedComponent<LaneBattlePage> p, string text) => p.FindAll("button").Any(b => Regex.Replace(b.TextContent, @"\s+", " ").Trim() == text);

async Task<bool> Until(Func<bool> cond, int ms)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms) { try { if (cond()) return true; } catch { } await Task.Delay(20); }
    return false;
}

BunitContext NewCtx(out BunitJSModuleInterop module, bool prerender = false, string? url = null, IPlenumRelayFactory? relays = null, IPlenumClock? clock = null)
{
    var c = new BunitContext();
    c.Services.AddSingleton<IBuildTime>(new Time(prerender));
    c.Services.AddSingleton<IPlenumRelayFactory>(relays ?? new LoopbackRelayFactory(new LoopbackBroker()));
    c.Services.AddSingleton<IPlenumClock>(clock ?? new FakeClock());
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

// ---------------------------------------------------------------------------------------------- online: the lobby screens, two visitors and a third
{
    var broker = new LoopbackBroker(); var relays = new LoopbackRelayFactory(broker); var clock = new FakeClock();
    using var ctxH = NewCtx(out var modH, relays: relays, clock: clock);
    using var ctxG = NewCtx(out _, relays: relays, clock: clock);
    using var ctxT = NewCtx(out var modT, relays: relays, clock: clock);
    var host = ctxH.Render<LaneBattlePage>(); var guest = ctxG.Render<LaneBattlePage>(); var third = ctxT.Render<LaneBattlePage>();

    // ---- the start screen: the computer, and three ways in to a game against a person
    Check("lobby: the start screen offers Play the computer and Play another person side by side", host.FindAll(".ln-modes > section").Count == 2 && host.FindAll(".ln-start").Count == 1 && host.FindAll(".ln-online").Count == 1 && host.FindAll(".ln-start .ln-btn.strong").Count == 1);
    Check("lobby: Create a room, Join with a code and Open rooms are there, with an optional name box and a code box", HasBtn(host, "Create a room") && HasBtn(host, "Join with a code") && HasBtn(host, "Open rooms") && host.FindAll("#ln-name").Count == 1 && host.FindAll("#ln-code").Count == 1 && host.Find("#ln-name").GetAttribute("maxlength") == "24");
    string pub = Text(host, ".ln-public");
    Check("lobby: the page says in plain words that the relay is public, that anyone with a code sees the room, that the list is visible to everyone, that there are no accounts and to use a made-up name",
        pub.Contains("public") && pub.Contains("Anyone who has a room's code can see that room") && pub.Contains("visible to everyone") && pub.Contains("no accounts") && pub.Contains("made-up name"), pub);
    Check("lobby: Join with a code waits for a code (disabled while the box is empty)", Btn(host, "Join with a code").HasAttribute("disabled"));
    host.Find("#ln-code").Input("a/b");
    Check("lobby: a typed code enables it", !Btn(host, "Join with a code").HasAttribute("disabled"));
    Btn(host, "Join with a code").Click();
    bool bad = await Until(() => host.FindAll(".ln-online .err").Count == 1, 5000);
    Check("lobby: a code that is not a code is refused on the start screen, in words, and the screen stays", bad && Text(host, ".ln-online .err").Contains("not a room code") && host.FindAll(".ln-online").Count == 1 && host.FindAll(".ln-room").Count == 0, bad ? Text(host, ".ln-online .err") : "no error");
    host.Find("#ln-code").Input("");
    Btn(host, "Open rooms").Click();
    await Until(() => host.FindAll(".ln-rooms").Count == 1, 5000);
    Check("lobby: Open rooms with nobody waiting says so", Text(host, ".ln-rooms").Contains("No open rooms right now"), Text(host, ".ln-rooms"));
    Btn(host, "Hide open rooms").Click();
    Check("lobby: the list can be hidden again", host.FindAll(".ln-rooms").Count == 0 && HasBtn(host, "Open rooms"));

    // ---- create a room
    host.Find("#ln-name").Input("  Lord  Test ");
    Btn(host, "Create a room").Click();
    bool made = await Until(() => host.FindAll(".ln-room").Count == 1 && host.FindAll(".ln-codeshow").Count == 1, 8000);
    Check("lobby: Create a room opens the waiting screen with a room code", made);
    if (!made) { Console.WriteLine("ABORT: " + (host.FindAll(".err").Count > 0 ? Text(host, ".err") : "no error shown")); return 1; }
    string code = Text(host, ".ln-codeshow");
    Check("lobby: the code is short (six letters or digits) and there is a Copy code button, Leave the room and a connection line", System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9]{6}$") && HasBtn(host, "Copy code") && HasBtn(host, "Leave the room") && Text(host, ".ln-room .ln-net").Contains("Connected"), code);
    Check("lobby: the waiting screen says it is waiting for a second player, that the room is also listed, and not to share the code widely", Text(host, ".ln-room").Contains("Waiting for a second player") && Text(host, ".ln-room").Contains("also listed under Open rooms") && Text(host, ".ln-room").Contains("share it only with the person you mean to play"));
    Check("lobby: the start screen is gone while in a room, and so is How to play", host.FindAll(".ln-modes").Count == 0 && host.FindAll(".ln-how").Count == 0 && host.FindAll("canvas").Count == 0);
    Btn(host, "Copy code").Click();
    Check("lobby: Copy code puts the code on the clipboard (the page asks the script) and says Copied", ctxH.JSInterop.Invocations.Any(i => i.Identifier == "copyText" && (i.Arguments[0] as string) == code) && HasBtn(host, "Copied"));
    string? entry = broker.RetainedText(LaneTopics.Lobby(code));
    Check("lobby: the room is listed on the relay under plenum/v1/doctrine-lane/lobby/ with the typed name cleaned, and the entry holds no match number", entry is not null && entry.Contains("Lord Test") && !entry.Contains("seed"), entry ?? "none");

    // ---- another visitor looks at the list
    Btn(third, "Open rooms").Click();
    bool listed = await Until(() => third.FindAll(".ln-rooms li").Count == 1, 8000);
    Check("lobby: the room shows in another visitor's Open rooms with the host's name, its age and a Join button", listed && TextOr(third, ".ln-rooms li .ln-rname") == "Lord Test" && TextOr(third, ".ln-rooms li .ln-rage") == "just now" && TextOr(third, ".ln-rooms li button") == "Join");
    clock.Advance(12_000);
    bool aged = await Until(() => TextOr(third, ".ln-rooms li .ln-rage").Contains("12 s ago") || TextOr(third, ".ln-rooms li .ln-rage") == "just now", 8000);
    Check("lobby: the list keeps itself up to date: the age moves on, and when the host repeats its entry it is fresh again (a room that is not repeated expires from the list)", aged, TextOr(third, ".ln-rooms li .ln-rage"));

    // ---- join from the list
    Btn(third, "Join").Click();
    bool seated = await Until(() => host.FindAll(".ln-dock").Count == 1 && third.FindAll(".ln-dock").Count == 1, 90000);
    Check("lobby: Join from the list seats the visitor; both screens go on to the battle", seated);
    if (!seated) { Console.WriteLine("ABORT: host " + Text(host, ".ln-room") + " | guest " + (third.FindAll(".ln-room").Count > 0 ? Text(third, ".ln-room") : third.FindAll(".err").Count > 0 ? Text(third, ".err") : "-")); return 1; }
    await Until(() => modH.Invocations.Any(i => i.Identifier == "init") && modT.Invocations.Any(i => i.Identifier == "init"), 8000);
    Check("lobby: the room left the list once it was full", broker.RetainedText(LaneTopics.Lobby(code)) is null);
    string hostSees = Text(host, ".ln-net .ln-who"), guestSees = Text(third, ".ln-net .ln-who");
    Check("lobby: each tray names the other player (the host typed a name, the guest got a made-up 'Lord ...' one) and shows the connection", guestSees == "Lord Test:" && hostSees.StartsWith("Lord ") && hostSees.EndsWith(":") && host.FindAll(".ln-net .ln-dot.on").Count == 1 && third.FindAll(".ln-net .ln-dot.on").Count == 1, hostSees + " / " + guestSees);
    bool counting = await Until(() => Text(host, ".ln-tag") == "Starting" && Text(third, ".ln-tag") == "Starting" && Text(host, ".ln-time") == "0:03" && Text(third, ".ln-time") == "0:03", 20000);
    Check("lobby: before the start both screens say Starting and count down the 3 s to the agreed start", counting, Text(host, ".ln-tag") + " " + Text(host, ".ln-time") + " | " + Text(third, ".ln-tag") + " " + Text(third, ".ln-time"));
    Check("lobby: no Ready button and no speed buttons before the start", !HasBtn(host, "Ready") && host.FindAll(".ln-speed").Count == 0);

    // ---- perspective: the script is given the ground turned for the guest only
    string layH = (string)modH.Invocations.First(i => i.Identifier == "init").Arguments[2]!, layG = (string)modT.Invocations.First(i => i.Identifier == "init").Arguments[2]!;
    Check("lobby: the host gets the ground as it is and the guest gets it turned (each sees their own base on their own side)", layH.Contains("\"mirror\":false") && layG.Contains("\"mirror\":true") && layH != layG);

    // ---- the start, then Ready means "I have committed"
    clock.Advance(3500);
    bool paused = await Until(() => Text(host, ".ln-tag") == "Pause" && Text(third, ".ln-tag") == "Pause", 15000);
    Check("lobby: at the agreed start both screens show the first pause with 30 s to choose", paused && Text(host, ".ln-time") == "0:30" && Text(host, ".ln-pts strong") == "10" && HasBtn(host, "Ready") && HasBtn(third, "Ready"));
    Check("lobby: no speed buttons in an online match (a pause has none, and the run will not either)", host.FindAll(".ln-speed").Count == 0 && third.FindAll(".ln-speed").Count == 0 && !HasBtn(host, "Play again"));
    await host.InvokeAsync(() => host.Instance.OnLaneTap(0));
    host.FindAll(".ln-pack:not([disabled])")[0].Click();
    Btn(host, "Ready").Click();
    bool waitingFirst = await Until(() => HasBtn(host, "Waiting for opponent"), 8000);
    Check("lobby: Ready on one side means 'I have committed': the button says Waiting for opponent and is locked", waitingFirst && Btn(host, "Waiting for opponent").HasAttribute("disabled"));
    bool otherSees = await Until(() => Text(third, ".ln-net").Contains("is ready"), 8000);
    Check("lobby: the connection line tells each side who they are waiting for, and the other side that they are ready", otherSees && Text(host, ".ln-net").Contains("You are ready. Waiting for"), Text(host, ".ln-net") + " | " + Text(third, ".ln-net"));
    Check("lobby: once ready the packs and Take back are locked", host.FindAll(".ln-pack").All(b => b.HasAttribute("disabled")) && host.FindAll(".ln-staged .ln-x").All(b => b.HasAttribute("disabled")));
    Btn(third, "Ready").Click();
    bool running = await Until(() => host.FindAll(".ln-tag.run").Count == 1 && third.FindAll(".ln-tag.run").Count == 1, 15000);
    Check("lobby: when both are ready the plays are shown together and the battle starts on both screens, with no speed buttons", running && host.FindAll(".ln-speed").Count == 0 && third.FindAll(".ln-speed").Count == 0);
    var pkH = await host.InvokeAsync(() => host.Instance.Frame(0.0)); var pkG = await third.InvokeAsync(() => third.Instance.Frame(0.0));
    if (pkH is null) pkH = await host.InvokeAsync(() => host.Instance.Frame(0.0));
    if (pkG is null) pkG = await third.InvokeAsync(() => third.Instance.Frame(0.0));
    bool pk = pkH is { Length: > 32 } && pkG is { Length: > 32 };
    var bodiesH = pk ? LanePack.Unpack(pkH!).Bodies : new(); var bodiesG = pk ? LanePack.Unpack(pkG!).Bodies : new();
    Check("lobby: the picture each screen gets is its own: the host's soldiers are side 0 (yours) on the host's screen and side 1 (theirs) on the guest's, the same bodies", pk && bodiesH.Count > 0 && bodiesH.Count(b => b.Side == Side.Player) == bodiesG.Count(b => b.Side == Side.Enemy) && bodiesH.Count(b => b.Side == Side.Enemy) == bodiesG.Count(b => b.Side == Side.Player), $"{bodiesH.Count} / {bodiesG.Count}");
    Check("lobby: in the run the tray keeps to the connection line, the clock and Leave (no packs, no Ready)", host.FindAll(".ln-packs").Count == 0 && !HasBtn(host, "Ready") && HasBtn(host, "Leave") && host.FindAll("button").All(b => b.Closest(".ln-dock") is not null));

    // ---- a third visitor with the code finds the room full
    guest.Find("#ln-code").Input(code.ToLowerInvariant());
    Btn(guest, "Join with a code").Click();
    bool full = await Until(() => guest.FindAll(".ln-online .err").Count == 1, 8000);
    Check("lobby: a visitor who joins with the code of a full room is told so in words and stays on the start screen", full && Text(guest, ".ln-online .err").Contains("already has two players") && guest.FindAll(".ln-modes").Count == 1);

    // ---- leaving a running match gives it to the other player; the first tap only asks
    Btn(host, "Leave").Click();
    Check("lobby: the first tap on Leave in a running match only asks (it would give the match away)", HasBtn(host, "Tap again to leave. The other player wins.") && host.FindAll(".ln-tag.run").Count == 1);
    Btn(host, "Tap again to leave. The other player wins.").Click();
    bool hostBack = await Until(() => host.FindAll(".ln-modes").Count == 1, 8000);
    Check("lobby: the second tap leaves: the host is back on the start screen", hostBack && host.FindAll(".ln-dock").Count == 0);
    bool told = await Until(() => third.FindAll(".ln-tag.over").Count == 1, 8000);
    Check("lobby: the other player is told at once, in words, that the opponent left and that the match is theirs", told && Text(third, ".ln-outcome").Contains("left the match") && Text(third, ".ln-outcome").Contains("The match is yours"), told ? Text(third, ".ln-outcome") : "");
    Check("lobby: after that match there is Leave, and no Rematch (the opponent is gone), no speed buttons", HasBtn(third, "Leave") && !HasBtn(third, "Rematch") && third.FindAll(".ln-speed").Count == 0);
    Btn(third, "Leave").Click();
    bool thirdBack = await Until(() => third.FindAll(".ln-modes").Count == 1, 8000);
    Check("lobby: Leave takes the other visitor back to the start screen too, and the relay holds nothing of the room", thirdBack && await Until(() => broker.RetainedTopics.Count == 0, 5000), string.Join(", ", broker.RetainedTopics.Take(3)));
    await host.Instance.DisposeAsync(); await guest.Instance.DisposeAsync(); await third.Instance.DisposeAsync();
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
var online = Directory.GetFiles(Path.Combine(showroom, @"Services\Doctrine\Lane\Online"), "*.cs").Select(File.ReadAllText).ToArray();
var all = new[] { page_, css, words, js, Read(@"Services\Doctrine\Lane\LaneMatch.cs"), Read(@"Services\Doctrine\Lane\LaneSeats.cs"), Read(@"Services\Doctrine\Lane\LaneEconomy.cs"), Read(@"Services\Doctrine\Lane\LanePayload.cs"), Read(@"wwwroot\doctrine\NOTICE.txt") }.Concat(online).ToArray();
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

sealed class FakeClock : IPlenumClock { public long NowMs { get; private set; } = 1_000_000; public long UnixMs { get; private set; } = 1_760_000_000_000; public void Advance(long ms) { NowMs += ms; UnixMs += ms; } }
sealed class Time : IBuildTime { public Time(bool p) { IsPrerender = p; } public bool IsPrerender { get; } }