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

// ---- Creature: the knobs, Train, Stop, Reset, the Weights choice ----
const string Lab = "section[aria-labelledby^='tl-train']";
string[] banned = { "chance", "baseline", "beats", "verdict", "learns", "experiment" };
bool PageSaysNothingScientific() { var txt = page.Find(".pt-tab").TextContent; return banned.All(b => !txt.Contains(b, StringComparison.OrdinalIgnoreCase)); }
void SetKnob(int index, string value) => page.FindAll(Lab + " .tl-knobs input")[index].Change(value);   // 0 learning rate, 1 steps, 2 examples per step, 3 seed
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#creature']");
Check("the Creature tab shows its prompt, lab and world", await Until(() => Has(".tl-prompt") && Has(".cr-grid") && Has(Lab), 30, "creature tab"));
Check("the Creature tab has one short line and says the shared model is not changed", Text(".pt-tab .lede").Length < 200 && Text(Lab).Contains("The shared model is not changed"));
Check("every knob is there: weights, learning rate, steps, examples per step, seed", page.FindAll(Lab + " .tl-knobs select").Count == 1 && page.FindAll(Lab + " .tl-knobs input").Count == 4);
Check("defaults: fresh weights, learning rate 0.001, 200 steps, 1 example, seed 42",
    page.Find(Lab + " .tl-knobs select").GetAttribute("value") == "fresh" && page.FindAll(Lab + " .tl-knobs input")[0].GetAttribute("value") == "0.001"
    && page.FindAll(Lab + " .tl-knobs input")[1].GetAttribute("value") == "200" && page.FindAll(Lab + " .tl-knobs input")[2].GetAttribute("value") == "1" && page.FindAll(Lab + " .tl-knobs input")[3].GetAttribute("value") == "42");
Check("no verdict, baseline or chance wording on the Creature tab", PageSaysNothingScientific());
Console.WriteLine("  prompt shown: " + Text(".tl-prompt").Trim());
{
    SetKnob(1, "1000");   // a long run, to Stop it
    await ClickAsync(Lab + " .go");
    Check("Train starts: stats and chart appear, trainer lock held, foraging locked", await Until(() => Has(".tl-svg") && loader.TrainingActive, 120, "training started") && page.Find(".cr-controls .go").HasAttribute("disabled"));
    Check("training advances", await Until(() => Regex.IsMatch(Text(".tl-stats .stat .n"), @"^[1-9]"), 120, "first steps"), Text(".tl-stats .stat .n"));
    Check("a progress bar shows the run", Has(Lab + " progress"));
    await ClickAsync(Lab + " .ghost");   // Stop
    Check("Stop ends the run and frees the trainer lock", await Until(() => !loader.TrainingActive, 60, "stop"));
    Check("the shared model is unchanged after training a fresh model", TaskStats.Fingerprint(shared) == fp0);
    Check("the weights choice is locked while a copy exists", page.Find(Lab + " .tl-knobs select").HasAttribute("disabled"));
}
{
    SetKnob(1, "25"); SetKnob(2, "2");   // 25 steps of 2 examples
    await ClickAsync(Lab + " .go");
    await Until(() => loader.TrainingActive, 30, "run started");
    Check("a short run ends by itself and charts its scores", await Until(() => !loader.TrainingActive && page.FindAll(".tl-dot").Count >= 2, 600, "run finished"), $"{page.FindAll(".tl-dot").Count} points, {Text(".tl-stats .stat .n")} steps");
    Console.WriteLine("  score now: " + Text(".tl-stats .stat:nth-of-type(2) .n") + ", last loss " + Text(".tl-stats .stat:nth-of-type(3) .n"));
    Check("still no verdict or baseline wording after a run", PageSaysNothingScientific());
    Check("the shared model is unchanged after a full run", TaskStats.Fingerprint(shared) == fp0);
}
{
    await ClickAsync(Lab + " .ghost", 1);   // Reset
    Check("Reset drops the copy and unlocks the weights choice", await Until(() => !Has(".tl-svg"), 30, "reset") && !page.Find(Lab + " .tl-knobs select").HasAttribute("disabled"));
    page.Find(Lab + " .tl-knobs select").Change("trained");
    Check("choosing Prism's trained weights moves the learning rate to its default and hides the seed", await Until(() => page.FindAll(Lab + " .tl-knobs input").Count == 3, 10, "trained knobs") && page.FindAll(Lab + " .tl-knobs input")[0].GetAttribute("value") == "0.0003");
    SetKnob(1, "10");
    await ClickAsync(Lab + " .go");
    await Until(() => loader.TrainingActive, 30, "trained run started");
    Check("a run from Prism's trained weights also ends", await Until(() => !loader.TrainingActive && Has(".tl-svg"), 600, "trained run"));
    Check("the shared model is unchanged after training a copy of it", TaskStats.Fingerprint(shared) == fp0);
}
{
    await ClickAsync(".cr-controls .go");
    Check("the creature forages and shows the model's scores", await Until(() => Has(".tl-scores"), 60, "forage step"), Has(".tl-scores") ? Text(".tl-scores") : "");
    Check("the world says it is using your copy", Text("#cr-world-title ~ .tl-copy").Contains("your copy"));
    await ClickAsync(".cr-controls .go");   // Pause
    Check("Pause stops the forage and frees the lock", await Until(() => !loader.TrainingActive, 30, "pause"));
    await ClickAsync(Lab + " .ghost", 1);   // Reset
    Check("after Reset the world uses the shared model again", await Until(() => Text("#cr-world-title ~ .tl-copy").Contains("the shared model"), 30, "world back"));
}
// ---- leaving a tab mid-training stops the trainer ----
{
    SetKnob(1, "1000");
    await ClickAsync(Lab + " .go");
    await Until(() => loader.TrainingActive, 30, "trainer running");
    await ClickAsync("a.pt-tab-btn[href='/tools/prism/#chat']");
    Check("leaving the tab disposes the trainer (lock released)", await Until(() => !loader.TrainingActive, 60, "lock released after tab leave"));
    Check("back on Chat, nothing of the lab is rendered", await Until(() => Has(".or-input") && !Has(".tl-panel"), 30, "chat again"));
    Check("the shared model is unchanged after leaving mid-run", TaskStats.Fingerprint(shared) == fp0);
}

// ---- Forecaster ----
await ClickAsync("a.pt-tab-btn[href='/tools/prism/#forecaster']");
Check("the Forecaster tab loads the tape and says 'Data as of'", await Until(() => Has(".fc-source"), 60, "tape") && Text(".fc-source").Contains("Data as of"), Has(".fc-source") ? Text(".fc-src-note") : "");
Check("the Forecaster has the same wiring: weights, learning rate, steps, examples, seed, plus the history window", page.FindAll(Lab + " .tl-knobs input").Count == 5 && Has(Lab + " .tl-knobs select"));
Check("no verdict, baseline or chance wording on the Forecaster tab", PageSaysNothingScientific());
string promptBefore = Text(".tl-prompt").Trim();
Console.WriteLine("  prompt shown: " + promptBefore);
{
    var win = page.Find(Lab + " .tl-knobs input[min='2']");   // the history window input
    win.Change("6");
    Check("the history window changes what the model reads", await Until(() => Text(".tl-prompt").Trim().Split(' ').Length < promptBefore.Split(' ').Length, 20, "window applied"), Text(".tl-prompt").Trim());
    SetKnob(1, "10");
    await ClickAsync(Lab + " .go");
    await Until(() => loader.TrainingActive, 30, "forecaster training started");
    Check("the Forecaster trains and charts", await Until(() => !loader.TrainingActive && Has(".tl-svg"), 600, "forecaster run"));
    Console.WriteLine("  score now: " + Text(".tl-stats .stat:nth-of-type(2) .n"));
}
await ClickAsync("#fc-guess-title ~ .tl-row .go");
Check("Guess a random hour shows the model's call against the real candle", await Until(() => Has(".fc-flash") && Has(".fc-chart-svg"), 60, "guess"), Has(".fc-predict-label") ? Text(".fc-predict-label") : "");
Check("the shared model is unchanged after the Forecaster tab", TaskStats.Fingerprint(shared) == fp0);
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
