using System.Text.RegularExpressions;
using Blazier;
using Bunit;
using HoloKernel;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Showroom.Services.PrismTab;

// UI proof for the Prism tool: renders the REAL page (Showroom.Pages.Prism, from the built Showroom.dll) under bUnit against the
// shipped checkpoint and drives the tabs the way a visitor would. See the csproj header. Exit code 0 = every check passed.
string webData = args.Length > 0 ? args[0] : @"C:\Users\dongy\website-data";
string wwwroot = args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\wwwroot"));
int failures = 0;
void Check(string name, bool ok, string detail = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}"); if (!ok) failures++; }
async Task<bool> Until(Func<bool> cond, int seconds, string what)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < seconds) { try { if (cond()) return true; } catch { } await Task.Delay(100); }
    Console.WriteLine($"  (timed out after {seconds}s waiting for: {what})");
    return false;
}

using var ctx = new BunitContext();
ctx.JSInterop.Mode = JSRuntimeMode.Loose;
ctx.JSInterop.Setup<string>("prismAudio.getPref", _ => true).SetResult("1");   // a saved preference: sound ON, volume 1
var handler = new FileHandler(webData, wwwroot);
var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/tools/") };
ctx.Services.AddSingleton(http);
ctx.Services.AddSingleton<SessionHost>();
ctx.Services.AddSingleton<PrismLoader>();
ctx.Services.AddSingleton<IBuildTime>(new LiveTime());
var nav = ctx.Services.GetRequiredService<NavigationManager>();
var loader = ctx.Services.GetRequiredService<PrismLoader>();
var sessions = ctx.Services.GetRequiredService<SessionHost>();

nav.NavigateTo("http://localhost/prism");
var page = ctx.Render<Showroom.Pages.Prism>();
Check("the page opens on the Chat tab and downloads the model once", await Until(() => page.FindAll(".pt-tab").Count == 1, 90, "model load"));
Check("Chat is the active tab", page.Find(".pt-tab h2").TextContent.Contains("Chat with Prism") && page.Find("a.pt-tab-btn.on").TextContent == "Chat");
Check("the Chat panel is ready (stats and input)", await Until(() => page.FindAll(".or-input").Count == 1, 60, "chat ready"));
var shared = loader.Loaded!.Session;
string fp0 = TaskStats.Fingerprint(shared);
var fetchesAfterChat = handler.BrainFetches;
Check("the checkpoint was fetched once", fetchesAfterChat == 1, $"{fetchesAfterChat}");

async Task ClickAsync(string selector, int skip = 0) { var el = page.FindAll(selector)[skip]; await Task.Run(() => el.Click()); }
bool Has(string selector) => page.FindAll(selector).Count > 0;
string Text(string selector) => page.Find(selector).TextContent;

// ---- tab links: Stories, Map ----
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#stories']");
Check("clicking Stories switches the tab (address and panel)", await Until(() => Has(".ns-panel"), 30, "stories panel") && nav.Uri.EndsWith("prism#stories"), nav.Uri);
Check("Stories did not fetch the checkpoint again", handler.BrainFetches == 1);
Check("only the active tab is rendered (no chat input on Stories)", !Has(".or-input"));
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#map']");
Check("the Map tab renders its form", await Until(() => Has(".cg-form"), 30, "map form"));
{
    page.Find(".cg-input").Input("The cat sat on the");
    await ClickAsync(".cg-form .go");
    Check("the Map draws a path for a prompt", await Until(() => Has(".cg-answer") && Has(".cg-svg"), 60, "map result"), Has(".cg-answer") ? Text(".cg-answer-word") : "");
}

// ---- sound: the Chat tab calls the voice for every generated token; leaving and coming back recreates it ----
{
    int PrefReads() => ctx.JSInterop.Invocations["prismAudio.getPref"].Count();
    int Partials() => ctx.JSInterop.Invocations.Where(i => i.Identifier == "prismAudio.partials").Count();
    await ClickAsync("a.pt-tab-btn[href='/tools/prism/#chat']");
    Check("back on Chat for the sound check", await Until(() => Has(".or-input"), 30, "chat"));
    int prefsBefore = PrefReads();
    int unlocksBefore = ctx.JSInterop.Invocations.Where(i => i.Identifier == "prismAudio.unlock").Count();
    page.Find(".or-input").Input("hello there");
    await ClickAsync(".or-input-row .go");
    Check("Send resumes the AudioContext (unlock is called from the click)", ctx.JSInterop.Invocations.Where(i => i.Identifier == "prismAudio.unlock").Count() > unlocksBefore);
    Check("the reply finishes", await Until(() => Has(".or-turn.prism") && !Has(".or-turn.pending"), 240, "reply"));
    int tones = Partials();
    Console.WriteLine($"  tones played for the reply: {tones}");
    Check("every generated token plays a tone (the voice is called per token)", tones >= 3, $"{tones} tones");
    var firstTone = ctx.JSInterop.Invocations.First(i => i.Identifier == "prismAudio.partials");
    Check("a tone carries real partials (frequencies, amplitudes, phases)", firstTone.Arguments.Count >= 5 && ((double[])firstTone.Arguments[0]!).Length >= 8 && ((double[])firstTone.Arguments[0]!).All(f => f > 0));
    await ClickAsync("a.pt-tab-btn[href='/tools/prism/#map']");
    await Until(() => Has(".cg-form"), 30, "map");
    Check("leaving Chat stops the notes (stopAll)", ctx.JSInterop.Invocations.Any(i => i.Identifier == "prismAudio.stopAll"));
    await ClickAsync("a.pt-tab-btn[href='/tools/prism/#chat']");
    Check("coming back to Chat creates the voice again (the saved preference is read again)", await Until(() => Has(".or-input") && PrefReads() > prefsBefore, 30, "chat re-entered"), $"{PrefReads() - prefsBefore} new reads");
    int before2 = Partials();
    page.Find(".or-input").Input("tell me more");
    await ClickAsync(".or-input-row .go");
    await Until(() => Has(".or-turn.pending"), 30, "second reply started");
    Check("the re-created voice plays too", await Until(() => Partials() > before2 + 2, 120, "tones after re-entry"), $"{Partials() - before2} new tones");
    await Until(() => !Has(".or-turn.pending"), 240, "second reply done");
}

// ---- Creature: ONE act. Run plays and trains in the same ticks; Pause, Stop, Reset ----
const string Lab = "section[aria-labelledby^='tl-train']";
string[] banned = { "chance", "baseline", "beats", "verdict", "learns", "experiment" };
bool PageSaysNothingScientific() { var txt = page.Find(".pt-tab").TextContent; return banned.All(b => !txt.Contains(b, StringComparison.OrdinalIgnoreCase)); }
AngleSharp.Dom.IElement Knob(string label) => page.FindAll(Lab + " .tl-knobs label").First(l => l.TextContent.Contains(label)).QuerySelector("input,select")!;
int Steps() => int.Parse(Text(Lab + " .tl-stats .stat .n").Replace(",", ""));
string RunLabel() => Text(Lab + " .tl-row .go");
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#creature']");
Check("the Creature tab shows its prompt, world and lab", await Until(() => Has(".tl-prompt") && Has(".cr-grid") && Has(Lab), 30, "creature tab"));
Check("one short line, and the shared model is not changed", Text(".pt-tab .lede").Length < 200 && Text(Lab).Contains("The shared model is not changed"));
Check("every knob: weights, learning rate, examples per step, seed, exploration, speed", new[] { "Weights", "Learning rate", "Examples per step", "Seed", "Exploration", "Speed" }.All(k => page.FindAll(Lab + " .tl-knobs label").Any(l => l.TextContent.Contains(k))));
Check("there is no separate Train / Forage / Steps: one Run, Stop and Reset", RunLabel() == "Run" && !Text(Lab).Contains("Steps\n") && page.FindAll(".cr-controls").Count == 0 && page.FindAll("button").Count(b => b.TextContent.Trim() is "Train" or "Let it forage") == 0);
Check("defaults: fresh weights, learning rate 0.001, 1 example, seed 42, exploration 0.1",
    Knob("Weights").GetAttribute("value") == "fresh" && Knob("Learning rate").GetAttribute("value") == "0.001" && Knob("Examples per step").GetAttribute("value") == "1"
    && Knob("Seed").GetAttribute("value") == "42" && Knob("Exploration").GetAttribute("value") == "0.1");
Check("no verdict, baseline or chance wording on the Creature tab", PageSaysNothingScientific());
Knob("Speed").Input("10");
Knob("Examples per step").Change("2");
{
    await ClickAsync(Lab + " .tl-row .go");
    Check("Run starts: the button becomes Pause, the trainer lock is held, weights are locked", await Until(() => RunLabel() == "Pause" && loader.TrainingActive && Has(".tl-svg"), 120, "run") && Knob("Weights").HasAttribute("disabled"));
    Check("the creature acts and the same ticks train: steps climb", await Until(() => Has(".tl-svg") && Steps() >= 4, 180, "steps climbing"), Has(".tl-svg") ? Text(Lab + " .tl-stats .stat .n") : "");
    Check("the creature's decisions and trail show on the grid", await Until(() => Has(".tl-scores") && Has(".trail"), 60, "animation"), Has(".tl-scores") ? Text(".tl-scores") : "");
    Check("the readout is a live number, not a verdict", Regex.IsMatch(Text(Lab + " .tl-stats .stat:nth-of-type(2) .n"), @"^(\d+%|…)$") && PageSaysNothingScientific());
    Check("the shared model is unchanged mid-run", TaskStats.Fingerprint(shared) == fp0);
    await ClickAsync(Lab + " .tl-row .go");   // Pause
    Check("Pause frees the lock and offers Resume", await Until(() => !loader.TrainingActive && RunLabel() == "Resume", 60, "pause"));
    int atPause = Steps(); await Task.Delay(1500);
    Check("while paused nothing trains", Steps() == atPause);
    await ClickAsync(Lab + " .tl-row .go");   // Resume
    Check("Resume carries on from the same copy", await Until(() => Steps() > atPause + 1, 120, "resume"));
    await ClickAsync(Lab + " .tl-row .ghost");   // Stop
    Check("Stop ends the run and puts the creature back at the egg", await Until(() => !loader.TrainingActive && !Has(".trail"), 60, "stop"));
    Check("after Stop the trained copy is kept (Resume available)", RunLabel() == "Resume");
    Check("the shared model is unchanged after a run of fresh-weights ticks", TaskStats.Fingerprint(shared) == fp0);
}
{
    await ClickAsync(Lab + " .tl-row .ghost", 1);   // Reset
    Check("Reset drops the copy and unlocks the weights choice", await Until(() => !Has(".tl-svg") && RunLabel() == "Run", 30, "reset") && !Knob("Weights").HasAttribute("disabled"));
    Knob("Weights").Change("trained");
    Check("Prism's trained weights: learning rate moves to its default and the seed is hidden", await Until(() => !page.FindAll(Lab + " .tl-knobs label").Any(l => l.TextContent.Contains("Seed")), 10, "trained knobs") && Knob("Learning rate").GetAttribute("value") == "0.0003");
    Knob("Speed").Input("10");
    await ClickAsync(Lab + " .tl-row .go");
    Check("a run from Prism's trained weights plays and trains too", await Until(() => Has(".tl-svg") && Steps() >= 3, 180, "trained run"));
    await ClickAsync(Lab + " .tl-row .ghost");   // Stop
    await Until(() => !loader.TrainingActive, 60, "trained stop");
    Check("the shared model is unchanged after training a copy of it", TaskStats.Fingerprint(shared) == fp0);
}
// ---- leaving a tab mid-run stops the trainer ----
{
    await ClickAsync(Lab + " .tl-row .go");
    await Until(() => loader.TrainingActive, 30, "trainer running");
    await ClickAsync("a.pt-tab-btn[href='/tools/prism/#chat']");
    Check("leaving the tab mid-run releases the lock", await Until(() => !loader.TrainingActive, 60, "lock released after tab leave"));
    Check("back on Chat, nothing of the lab is rendered", await Until(() => Has(".or-input") && !Has(".tl-panel"), 30, "chat again"));
    Check("the shared model is unchanged after leaving mid-run", TaskStats.Fingerprint(shared) == fp0);
}

// ---- Forecaster: ONE act, a beat of guess, countdown, reveal, train ----
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#forecaster']");
Check("the Forecaster tab loads the tape and says 'Data as of'", await Until(() => Has(".fc-source"), 60, "tape") && Text(".fc-source").Contains("Data as of"), Has(".fc-source") ? Text(".fc-src-note") : "");
Check("the Forecaster has the same wiring plus the history window", new[] { "Weights", "Learning rate", "Examples per step", "Seed", "Speed" }.All(k => page.FindAll(Lab + " .tl-knobs label").Any(l => l.TextContent.Contains(k))) && page.FindAll(Lab + " .tl-knobs label").Any(l => l.TextContent.Contains("History window")) && RunLabel() == "Run");
Check("no verdict, baseline or chance wording on the Forecaster tab", PageSaysNothingScientific());
string promptBefore = Text(".tl-prompt").Trim();
{
    page.Find(Lab + " .tl-knobs input[min='2']").Change("6");
    Check("the history window changes what the model reads", await Until(() => Text(".tl-prompt").Trim().Split(' ').Length < promptBefore.Split(' ').Length, 20, "window applied"), Text(".tl-prompt").Trim());
    Knob("Speed").Input("10");
    await ClickAsync(Lab + " .tl-row .go");
    Check("Run plays beats: a guess with a countdown, then the real candle and a verdict flash", await Until(() => Has(".fc-predict-strip"), 120, "first beat") && await Until(() => Has(".fc-flash"), 120, "reveal"), Has(".fc-predict-label") ? Text(".fc-predict-label").Trim() : "");
    Check("the same ticks train: steps climb", await Until(() => Has(".tl-svg") && Steps() >= 3, 180, "steps"));
    await ClickAsync(Lab + " .tl-row .go");   // Pause
    Check("Pause frees the lock", await Until(() => !loader.TrainingActive, 60, "pause"));
    await ClickAsync(Lab + " .tl-row .ghost");   // Stop
    Check("Stop puts the tape back at the start", await Until(() => !Has(".fc-predict-strip"), 30, "tape reset"));
    Check("the shared model is unchanged after the Forecaster run", TaskStats.Fingerprint(shared) == fp0);
}
// ---- deep links ----
{
    using var ctx2 = new BunitContext();
    ctx2.JSInterop.Mode = JSRuntimeMode.Loose;
    ctx2.Services.AddSingleton(new HttpClient(new FileHandler(webData, wwwroot)) { BaseAddress = new Uri("http://localhost/tools/") });
    ctx2.Services.AddSingleton<SessionHost>(); ctx2.Services.AddSingleton<PrismLoader>(); ctx2.Services.AddSingleton<IBuildTime>(new LiveTime());
    ctx2.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/prism?tab=map");
    var p2 = ctx2.Render<Showroom.Pages.Prism>();
    Check("?tab=map opens the Map tab", await Until(() => p2.FindAll(".cg-form").Count == 1, 90, "map via query"));
}
{
    using var ctx3 = new BunitContext();
    ctx3.JSInterop.Mode = JSRuntimeMode.Loose;
    ctx3.Services.AddSingleton(new HttpClient(new FileHandler(webData, wwwroot)) { BaseAddress = new Uri("http://localhost/tools/") });
    ctx3.Services.AddSingleton<SessionHost>(); ctx3.Services.AddSingleton<PrismLoader>(); ctx3.Services.AddSingleton<IBuildTime>(new LiveTime());
    ctx3.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/prism#stories");
    var p3 = ctx3.Render<Showroom.Pages.Prism>();
    Check("#stories opens the Stories tab", await Until(() => p3.FindAll(".ns-panel").Count == 1, 90, "stories via hash"));
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

sealed class LiveTime : IBuildTime { public bool IsPrerender => false; }

// Serves the website-data checkout and Showroom/wwwroot to the page's HttpClient (no network, no server), and counts checkpoint fetches.
sealed class FileHandler : HttpMessageHandler
{
    readonly string _webData, _wwwroot;
    public int BrainFetches;
    public FileHandler(string webData, string wwwroot) { _webData = webData; _wwwroot = wwwroot; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var path = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath);
        string? file = path.StartsWith("/website-data/") ? Path.Combine(_webData, path["/website-data/".Length..].Replace('/', '\\'))
                     : path.StartsWith("/tools/data/") ? Path.Combine(_wwwroot, "data", path["/tools/data/".Length..].Replace('/', '\\')) : null;
        if (path.EndsWith("oracle-brain.bin.gz")) Interlocked.Increment(ref BrainFetches);
        if (file is null || !File.Exists(file)) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) });
    }
}
