# Lazy Blazor study: scaffold-first pages, lazy islands and worker runtimes

Date: 2026-10-08. Author: website-owner (research task, no site changes). Working package id for the candidate: `EvaluatedApplications.Blazier` ("the lazier Blazor"; the coordinator checked that NuGet has no package of that name).

Nothing in `site/`, `Showroom/` or `website-data` was touched. Everything measured here was run against the live site (read-only GETs) or against throwaway builds of a COPY of Showroom in the session scratchpad. The small scripts that produced the numbers are in `docs/lazy-blazor-spikes/` (listed at the end). Every figure below says how it was obtained. Anything I did not run is marked "not measured".

## 0. Headline

1. **The load problem is one file.** On the live site, `/tools/*` puts 11.3 MB on the wire (gzip, Pages does not serve Brotli) before any tool can run. 8.64 MB of that (77%) is `dotnet.native.wasm`, the AOT-compiled 31 MB module. The assemblies, ICU and the Prism checkpoint together are about 2.6 MB.
2. **The csproj premise "AOT is load-bearing" no longer reproduces.** A copy of Showroom built with `RunAOTCompilation=false` (trimmed, interpreter, same package pins: .NET 10.0.11, Prism 1.3.3, AlgFormer 2.20.1) boots and runs Prism chat, Nano Stories, the Cartographer, the Creature and the council page load with identical outputs. Wire size falls from 11.3 MB to 4.3 MB. Phone-profile time to the tool gallery falls from about 29 s to about 14 s. The price is compute speed: 1.3x to 5x slower on the loops I could measure, still small in absolute terms. This is the biggest single win and it needs no new package.
3. **Scaffold-first islands work today on stock Blazor.** A static page, then `Blazor.start()` on intent, then `Blazor.rootComponents.add(...)` of a component registered with `RegisterForJavaScript`, mounted the real, unmodified Prism and Analyst pages. A visitor who never presses Start downloads 0.07 MB. Real pages can also be prerendered at build time with `HtmlRenderer` (the real Creature page rendered to 35 KB of HTML in a console app).
4. **Lazy assemblies work with the interpreter, and gain little.** Marking HoloDb, Tracer, Prose and MQTTnet as `BlazorWebAssemblyLazyLoad` saved 0.39 MB gzip (7%) on the Prism island and loaded in 84 ms locally when needed. Under AOT they would save nothing that matters, because the AOT'd native code of every assembly lives inside `dotnet.native.wasm` regardless.
5. **A worker runtime with an awaitable, progress-reporting, cancellable, streaming API works and is cheap.** Measured locally: 0.5 to 0.8 s to start a second .NET runtime in a dedicated worker (HTTP-cached files), 32 MB initial linear memory, 0.34 ms per call round trip, 0 long tasks and an 11 ms worst timer gap on the main thread while a 300 ms job ran, 23 ms to cancel a cooperative job, no way to cancel a job that never yields. Two real traps were found and are in section 6.
6. **Recommendation: GO, scoped small.** Do Phase 0 (a build setting, not a package) now. Build `EvaluatedApplications.Blazier` as a thin package of three parts: build-time scaffold and island tooling, a tiny on-intent loader, and a typed worker bridge. Adopt nothing from the third-party worker libraries as a dependency (reasons in section 5), but borrow their lessons. Full recommendation in section 8.

## 1. Where load time goes today (measured)

Method: `docs/lazy-blazor-spikes/measure-live.mjs` drives headless Edge over the DevTools protocol with the cache disabled, records `encodedDataLength` per request (real bytes on the wire) and paint/ready times. "Phone profile" is the profile Showroom's own perf scripts use: 4096 kbit/s down, 1024 up, 100 ms RTT, CPU 6x slowdown. I also served a rebuilt AOT copy of the current source through a local gzip server shaped like Pages and got 29.4 s vs 28.3 s live for the gallery, so the local harness reproduces the live figure within the noise.

Bytes on the wire, live `https://evaluatedapplications.github.io/tools/` (gzip confirmed from `Content-Encoding` on every framework file; `Cache-Control: max-age=600`):

| Group | Requests | Wire MB | Share |
|---|---|---|---|
| `dotnet.native.wasm` (AOT, 31.15 MB raw) | 1 | 8.64 | 77% |
| BCL and Microsoft assemblies (.wasm Webcil) | 50 | 1.39 | 12% |
| App and EA assemblies (Showroom, AlgFormer, HoloDb, Tracer, EvalApp, Prose, MQTTnet, HoloKernel, Prism, Phasor) | 10 | 0.83 | 7% |
| ICU (EFIGS loaded) | 1 | 0.19 | 2% |
| Loader and runtime JS | 4 | 0.14 | 1% |
| CSS and html | 8 | 0.07 | 1% |
| **Gallery `/tools/` total** | 74 | **11.26** | |
| Prism adds the checkpoint (`oracle-brain.bin.gz` 0.96 MB, already gzip, served as is) and 4 text files | +5 | +0.96 | |
| **`/tools/prism` total** | | **12.25** | |

Times (cold cache, one run each unless stated, so treat as plus or minus 20 to 30%; the machine drifts):

| Page | Unthrottled desktop | Phone profile |
|---|---|---|
| Static site home `/` or `evalapp.html` | 0.7 s complete, 0.04 MB | 1.2 to 1.3 s complete, 0.04 MB |
| `/tools/` gallery, first paint | 0.45 s (the boot terminal in `index.html`) | 0.84 s |
| `/tools/` gallery, interactive (`.card.tool` present) | 4.3 s | 28.3 s |
| `/tools/prism`, interactive (stats present) | 4.6 s | 34.5 s |

Reading the phone numbers: 11.26 MB at 4 Mbit/s is 22.5 s of pure transfer, so roughly 6 s is browser compile and boot on top. The "first paint" is the fake-but-honest boot terminal; the first paint of real content is the same moment as interactive.

Two findings the existing docs did not state:

- **The gallery page `/tools/` itself costs 11.3 MB.** `Home.razor` is a static list of cards, and the static site homepage already carries the same gallery. Anyone who lands on `/tools/` pays for a runtime to see a list of links.
- **Every repeat visit after 10 minutes revalidates about 66 files** (`max-age=600`). Fingerprinted names would allow a far longer lifetime, but Pages does not let us set headers. Not measured (it needs a conditional-request run); flagged as a Pages limit.

## 2. AOT versus interpreter (measured on a copy)

Copy of the current Showroom source built three ways with `dotnet publish -c Release`, trimmed in all cases:

| Build | `dotnet.native.wasm` raw | Whole `_framework` raw / gzip (my gzip -9) | Wire for `/tools/` gallery (served gzip) |
|---|---|---|---|
| AOT as shipped (`-O1`) | 31.15 MB (byte identical to live) | 39.6 MB / 11.6 MB | 11.37 MB |
| Interpreter (`RunAOTCompilation=false`) | 3.12 MB | 12.9 MB / 4.8 MB | 4.34 MB |
| Mixed: CoreLib and EA assemblies AOT, other assemblies forced to the interpreter | 19.2 MB | not measured | 8.35 MB |

The mixed build needed the internal `_AOT_InternalForceInterpretAssemblies` item; forcing CoreLib to the interpreter failed to link (undefined `aot_wrapper_*` symbols). It is unsupported territory and it gave the worst of both: phone gallery 28.0 s (no better than full AOT) with 8.35 MB. Dropped.

Does the interpreter build work? Yes, with the same pins. Run with the repo's own scripts against both builds (each AOT and interpreter result below is the same script, same data, same machine, back to back):

| Check | AOT | Interpreter |
|---|---|---|
| `boot-check.mjs` Prism load and one reply | pass | pass, 958 ms |
| `chat-turns.mjs` 9 turns of Prism chat (current ctx=64 checkpoint, 282,944 parameters) | 0.1 s per turn | 0.4 to 0.5 s per turn |
| `tool-check.mjs` Stories (3 varieties) and Cartographer | 0.3 s / 0.2 s | 0.4 s / 0.8 s |
| `council-load-perf.mjs` Wokingham 2024-25, Reading 2022-23 (inflate and scan, same results) | 29 ms and 18 ms | 141 ms and 74 ms |
| Creature live training, episodes in 20 s (2 runs each) | 4 and 5 | 2 and 2 |
| Gallery on phone profile, interactive | 29.4 s | 13.9 s |
| `/tools/prism` on phone profile, interactive | 32.2 s | 19.7, 28.0, 27.0 s (noisy) |

So the interpreter is 1.3x to 5x slower on compute and roughly half the time to interactive on a phone profile. The Prism page loses less than the gallery gains because unpacking the checkpoint is compute (AOT: +2.8 s over the gallery; interpreter: +6 to +14 s). That unpack is the first thing worth moving to a worker, or storing in a form that needs less work.

Not measured, so do not assume: Forecaster, Prose, Analyst SQL on a large file, the long council flows (York, Essex), and a larger checkpoint than the current 282,944-parameter one (the older ctx=512 model re-primed at 2.9 s per step under AOT; interpreter would be several times that). `Showroom.csproj` says AOT's speed benefit "was never measured"; this is the first measurement of it, and it should be repeated by showroom-owner on the real source before any change.

The csproj reason for AOT (`Prism.Inference.DecodePolicy:.cctor` hitting `NIY` in the interpreter) did not reproduce on .NET 10.0.11 with Prism 1.3.3. I did not investigate why (a fixed Prism, or a fixed runtime). It needs confirming by showroom-owner before the csproj comment is retired.

One further size lever seen in the file list, untested: MQTTnet (0.13 MB gzip) and System.Linq.Expressions (0.13 MB) ship to every visitor; a browser build of AlgFormer that does not reference MQTTnet would drop both. Invariant globalization (drops the 0.21 MB ICU file) is untested and may change formatting in the council text.

## 3. Scaffold-first pages and islands

### What was built and measured

Spike: a copy of Showroom whose `Program.cs` registers real pages as JavaScript components (`builder.RootComponents.RegisterForJavaScript<Showroom.Pages.Prism>("prism")`) instead of mounting `App` on `#app`. The page `p-prism.html` is plain HTML with a heading and a slot. Its script loads `blazor.webassembly.js` with `autostart="false"` only on intent, calls `Blazor.start({ loadBootResource })` (the existing framework redirect to `website-data` works unchanged), then `Blazor.rootComponents.add(slot, 'prism', {})`. Result:

- Scaffold alone, phone profile: 0.07 MB, complete at 0.8 s. A visitor who never presses Start costs 0.07 MB instead of 11.3 MB.
- Auto-start on load, phone profile: runtime started at about 10.5 s, Prism mounted at about 15.5 s, stats visible at 23.5 to 25.8 s, versus 19.7 to 28.0 s for the ordinary routed page on the same interpreter build. Islands do not add measurable cost once started; the run to run noise is larger than any difference.
- The unmodified `Prism.razor` and `Analyst.razor` pages mounted and worked as islands. No page edits were needed.

### Build-time prerender

A console app referencing the Showroom project rendered real pages with `HtmlRenderer`: the Creature page produced 35,015 characters of HTML (full grid, controls, lede) in one call. Prism and Stories rendered their header, crumb and lede, then the page's own `OnInitialized` tried the network and emitted the "Couldn't load the checkpoint" error paragraph into the static HTML. So prerendering existing pages as they are gives a correct-looking shell only when the page does no I/O at construction. The fix is small: a `BuildTime` service (or a `RenderMode` flag) that the pages check before fetching, so the prerender emits the shell plus a skeleton. Scoped CSS attributes (`b-xxxx`) come through, so `Showroom.styles.css` applies to the static HTML as is.

### What breaks and how it is handled

| Concern | Effect | Handling |
|---|---|---|
| Routing | The `Router` and the SPA 404 bounce go away; each tool becomes a real file (`/tools/prism/index.html`). Deep links get indexable HTML and a real first paint. | Generate one static file per route. Keep `404.html` only for unknown paths. |
| Runtime readiness | `Blazor.start()` resolves slightly before dynamic root components are enabled: the first `rootComponents.add` threw "Dynamic root components have not been enabled" and succeeded on the second try, 50 ms later (observed 2 attempts every run). | Loader retries `add` on that error. A cleaner fix is an explicit C# "ready" callback after `Build()`. |
| Startup code that touches lazy types | `AddSingleton<ContentDbHost>()` has a HoloDb-typed field, so with HoloDb lazy the host failed type load at startup and nothing mounted. | Composition root must not reference lazy-assembly types. Register such services lazily. |
| `SessionHost` shared state (Prism model reused by Analyst novelty scan, Creature, Forecaster) | It is a singleton in one page load. Full page navigations between static tool pages throw it away, so the model is re-fetched (HTTP cache) and re-deserialized per page. | Two options: keep `/tools/*` as one SPA scope behind a scaffold (state kept, navigation client side), or accept reload cost and make deserialization cheap. Not measured; recommend the first for tool-to-tool moves and static scaffolds only for entry. |
| Layout (`MainLayout`) | Nav and footer are Blazor-rendered today. | Scaffold supplies the shared chrome statically (it is the same markup the static site uses). Cohesion gain: one chrome. |
| Shared page CSS | Unchanged. | Link `Showroom.styles.css` and `css/*` from the scaffold head. |
| Two sources of truth for words | If the scaffold text is written separately from the page, they drift. | The scaffold is generated from the component's own markup by the prerender step, never hand written. |
| `window.EA_DATA_BASE` and similar globals | Set by inline script in `index.html`. | The scaffold template keeps the same script block. |

Avoided download for a visitor who never uses a tool: 11.3 MB today (gallery), 12.3 MB (Prism), against 0.07 MB for the scaffold. For a visitor who does use a tool, scaffolding does not reduce bytes; it moves the real-content paint from 28 s to under 1 s on the phone profile, and gives the user a visible, honest "Start" or progress state.

## 4. Lazy loading

- **Assemblies**: `BlazorWebAssemblyLazyLoad` plus `LazyAssemblyLoader.LoadAssembliesAsync` worked with the interpreter build and the trimmer: Analyst mounted after loading `HoloDb.wasm,Tracer.wasm,Prose.wasm,MQTTnet.wasm` in 84 ms (local). Savings on the Prism island: 5.30 MB to 4.91 MB (0.39 MB gzip, 7%). The Microsoft docs say nothing about AOT here; my reading, not tested: under AOT the compiled code of a lazy assembly is still inside `dotnet.native.wasm`, so lazy loading would save only the IL bytes. The docs also warn that core runtime assemblies must not be lazy.
- **What is worth making lazy**: the Showroom assembly itself is 0.92 MB raw because every tool page is in it. Splitting each tool into its own Razor class library and loading it with its tool would be the real lazy-assembly gain (est. 0.2 to 0.3 MB gzip per page, from the ratio of page sizes; not built). Beyond that, lazy loading data is already how the council and Prism data work (fetched on demand from `website-data`).
- **Data**: unchanged and fine. The checkpoint is 0.96 MB; council slices are fetched only on Load.
- **AOT versus interpreter** is the dominant lever (section 2), far above lazy assemblies.

## 5. Prior art, licences and where Blazier differs

Checked this session (GitHub API and READMEs):

| Project | Licence | What it does | Gap against our goals |
|---|---|---|---|
| Microsoft Learn "Blazor with .NET on Web Workers" (`blazorwebworker` template, `WebWorkerClient`) | docs: CC-BY-4.0 (blazor-samples repo); template code Microsoft | Library with `CreateAsync`, `InvokeAsync<T>(string method, object[] args, timeoutMs, CancellationToken)`; methods are `[JSExport]` statics found by name; complex data as JSON strings. | Stringly typed calls, no progress, no streaming, cancellation token only abandons the wait. The template is not installed in SDK 10.0.400 here, so I read the documented API but did not run it. |
| SpawnDev.BlazorJS.WebWorkers (LostBeard) | MIT, last push 2026-07-22, one maintainer | Runs your Blazor app inside workers; call interface services across threads; shared workers; transferables; cooperative `CancellationToken` via message. Works on .NET 8 to 10, no special headers. | The worker is a whole Blazor app instance (not a lean runtime). Cancellation needs the callee to yield (its own README says `await Task.Delay(1)`). No `IProgress` or async-enumerable streaming in the README. Depends on SpawnDev.BlazorJS. Single-maintainer risk. |
| SpawnDev.SpawnJS, SpawnDev.ILGPU | licence file reads MIT (GitHub shows NOASSERTION; the SpawnJS repo's LICENSE.txt is titled "SpawnDev.ILGPU License (MIT)") | JSON-free interop; ILGPU kernels in the browser. | Interop layer and GPU, not scaffolding. Re-check licence terms before any dependency. |
| BlazorWorker (Tewr) | MIT, 483 stars, active 2026-09 | Process-like worker with a message bus; `BackgroundService` expression API and a low level string API. .NET 8 to 10. | Same missing pieces: no scaffold, no streaming, no pipeline-aware cancellation. |
| Uno, Avalonia browser | Apache-2.0, MIT | Load whole app runtimes with splash screens; Uno has bootstrapper options for mixed interpreter and AOT with profile-guided AOT. | From memory, not re-verified this session. They do not split the runtime from the page; no islands. |
| Blazor Web App (static SSR plus `InteractiveWebAssembly`) | Microsoft | Prerender plus hydrate. | Needs an ASP.NET server to prerender; no first party static-site generation. Pages is static. |
| Mixed mode AOT via `AOTProfilePath` / `_AOT_InternalForceInterpretAssemblies` | SDK internals | See section 2. | Internal switch failed to link when CoreLib was excluded; not a supported path. |

**Do existing libraries already cover it?** Partly. For "call a method in a worker" they do. Nothing covers (a) static scaffold plus on-intent islands for Blazor on a static host, (b) progress, streaming and pipeline-aware cancellation in one awaitable API, (c) the same code shape for the council worker, Prism and training tools, (d) EvalApp integration. The overlap on (b) is the reason to keep the worker bridge thin and to copy the good ideas (module worker, JSON-free marshalling, cooperative cancel) rather than depend on any of them. No dependency on a single-maintainer library for our core load path.

## 6. Worker runtimes made frictionless

### Measured (spike in `docs/lazy-blazor-spikes/worker/`)

A generic worker script (written once) boots `dotnet.js` at module top level, wires one `[JSImport] emit` and one `[JSExport] Call(id, method, argsJson)` (async) plus `Cancel(id)`, and a generic client class returns a Promise per call with `onProgress`, `onItem` and an `AbortSignal`. The app author writes only C#. Results, two builds: a minimal `wasmbrowser` app (7 MB raw `_framework`) and the full Showroom bundle used as a worker (worker boots the same files the page already cached):

| Metric | Minimal app | Showroom bundle as the worker |
|---|---|---|
| Cold start of the second runtime (files HTTP-cached, local) | 0.54 to 0.62 s | 0.80 s |
| Restart after `terminate()` (the hard cancel) | 0.14 to 0.20 s | 0.22 s |
| Initial linear memory of the worker runtime | 32 MB | 32 MB |
| Tiny call round trip (200 calls) | 0.34 to 0.51 ms | 0.35 ms |
| 300 ms prime job: progress messages / main-thread worst 10 ms timer gap / long tasks | 16 to 19 / 11 ms / 0 | 15 / 11 ms / 0 |
| Cancel latency of a job that yields every 12 ms | 23 ms | 24 ms |
| Cancel of a job that never yields | not possible: ran to its end (2.0 s) | same |
| Stream 2000 items (first item / total) | 0 to 4 ms / 209 to 565 ms | 0 ms / 208 ms |

Not measured: a cold network start of a worker (the 11 MB case is the page's first download, which a worker reusing the page's URLs does not repeat), phone memory, battery. CPU throttling in DevTools did not slow the worker, so the "6x" row for workers is not a phone figure.

### Traps found (both would bite any package that skips them)

1. **A handler set with `self.onmessage` before `dotnet.create()` is lost.** The runtime's glue owns `onmessage` while booting in a worker; my first worker hung forever with no error. Fix used: create the runtime at module top level, attach the listener afterwards with `addEventListener`, and have the page wait for a `ready` message before posting anything. The Microsoft docs' worker script has the same top-level shape.
2. **A canceled `Task` returned through a `[JSExport]` Promise never settled** in this runtime (no resolve, no reject, the job had stopped). Fix used: catch `OperationCanceledException` in the C# entry and return a sentinel the client turns into an `AbortError`. Also `Task.Delay(0)` returns an already-completed task and never reaches the event loop, so a cancel message is never read; use `Delay(1)` or a real yield.

Also: module workers ignore the page's import map, so the page must resolve the fingerprinted `dotnet.js` URL and pass it to the worker (done with a query string).

### Proposed shape (one design for three jobs)

- **Council audit worker** (prototype: `VirtualCustomer/src/CouncilAudit.Flow.Wasm/browser/s9/worker.js`, read only). It hand-writes message types `log/rows/progress/done/failed/error`, a CSV feed, retry, SHA-256 checks, abort detection (`ABORT_TEXT`), a 180 s silence watchdog, a per-council `MONO_GC_PARAMS=soft-heap-limit`, and a separate AOT 4 GiB bundle. Blazier replaces the message plumbing with typed calls and streams (`IAsyncEnumerable<Row[]>` batches), and surfaces runtime aborts as a typed exception with the last progress. It must therefore support two worker modes: **shared bundle** (cheap, the page's own assemblies, default) and **dedicated bundle** (its own `_framework`, AOT or 4 GiB, per job).
- **Prism inference**: the checkpoint unpack and generation move to a worker; the page receives tokens as a stream and can cancel at the next token (`StepToken` is a natural yield).
- **Training tools** (Creature, Forecaster): the trainer runs in the worker with progress, the page draws.

Cost of the second runtime: a few hundred ms and 32 MB initial (more as the heap grows), no second download in shared mode (the same URLs hit the HTTP cache; with `max-age=600` that cache can expire during a long session, which would re-fetch; not measured). On phones, memory per tab is the real budget (see the existing showcase notes on wasm32 limits).

## 7. EvalApp: making its parallel primitives web native (proposal)

Read: `EvalApp/CLAUDE.md`, `PACKAGE.md` section list, `Core/ExecutionHost.cs`. No EvalApp change is made or implied; this is a proposal for evalapp-owner.

### What EvalApp does today in the browser

`ExecutionHost.IsSingleThreaded` is true for `OperatingSystem.IsBrowser()` ("browser (no worker threads)"). In that mode `ForEach` never fans out (items run inline), `AddParallelGroup` runs branches sequentially, `ChunkedWork.For/Run` run inline, and a process-wide 12 ms slice clock calls `Task.Yield()` between nodes, items and branches so the page stays responsive. That is exactly the property that makes the worker bridge in section 6 work: an EvalApp pipeline running inside a worker yields on a budget, so a cancel message is read within one slice (my measurement: 23 ms), and `ExecutionHost.Yields` and `pipeline.Report()` give progress. The sync `ChunkedWork.For` cannot yield (CLAUDE.md says HoloDb's `HoloPar` gets the inline path), so a job built on it behaves like my non-yielding case and cannot be canceled except by `terminate()`.

### Three levels, in order of value and risk

1. **Run a whole pipeline in a worker** (no EvalApp change). The worker entry builds the same `Eval.App(...)` pipeline from the same assemblies and runs it; Blazier forwards a serializable input, progress (`OnItemCompleted`, `Report()` snapshots, `ExecutionHost.Yields`), results and cancellation (a `CancellationTokenSource` cancelled by the cancel message). This frees the UI thread and covers the council worker, Prism and training. It needs only Blazier plus the existing public EvalApp API.
2. **Fan a `ForEach` out across a pool of worker runtimes** (needs one EvalApp seam). Each worker is itself single threaded (it is a browser), so the fan-out is above `ExecutionHost`, not inside it.
3. **Chunk-level fan-out (`ChunkedWork`)** across workers. Hardest, and probably not worth it (below).

### The seam level 2 needs

Today a `ForEach` node fans out by scheduling its per-item pipeline on the thread pool (adaptive: it learns per-item cost and the process-wide dispatch cost D, then goes inline when items are cheap). Proposed seam, platform neutral, in EvalApp:

```csharp
// Proposal for evalapp-owner (names illustrative).
public interface IItemDispatcher
{
    // Run the ForEach item pipeline identified by `pipelineId` over serialized items, return serialized results in source order.
    ValueTask<ReadOnlyMemory<byte>[]> RunAsync(string pipelineId, ReadOnlyMemory<byte>[] items, ItemRunOptions options, CancellationToken ct);
    int Width { get; }            // concurrent runtimes available; 1 means do not offload
    long RoundTripCostNs { get; } // learned, feeds AdaptiveDispatch like D does
}
// ExecutionHost.UseDispatcher(IItemDispatcher?)   process-wide, null by default
// ForEach(..., remote: RemoteItems.Allowed)        opt-in per node; default stays local
```

What the seam must define, and what crosses the boundary:

| Question | Proposal |
|---|---|
| Pipeline definition | Does not travel. Lambdas cannot be serialized. Both runtimes contain the same assemblies, so the worker looks the item pipeline up by name from a registry of `DefineTask` names (`PipelineRef` already gives a named, late-bound reference). A node is offloadable only if its item pipeline is registered and closure free. |
| Items and results | Serialized. `T` for the item needs a codec (source-generated System.Text.Json by default, a binary codec hook for numeric rows). Results merge in source order as `ForEach` already does. |
| Data transfer | `postMessage` copies by structured clone; `ArrayBuffer`s can be transferred (moved, zero copy) but not shared. Round trip for a tiny call measured at 0.34 ms. Anything cheaper than a few tens of microseconds of work per item should stay local; the learned `RoundTripCostNs` makes the existing adaptive logic decide this. |
| SharedArrayBuffer | Not available on GitHub Pages (no COOP/COEP headers; we tried threads on 2026-08-28 and reverted). So no shared memory, no zero-copy sharing, no `Atomics`. The design must be copy or transfer only. If hosting later gains headers (the notes name Cloudflare), shared buffers become an optional fast path, not a requirement. |
| Shared static state | Not shared. Each runtime has its own statics, `ExecutionHost`, `AdaptiveDispatch`, `SessionHost`, model copies. Per-worker caches mean N copies (Prism checkpoint about 2 MB: fine; a council working set of 1 to 6 GiB: not). |
| `Gate` and `ExclusiveResource` | A Gate is a count of holders in one process; across runtimes it must be arbitrated by one owner. Keep gates on the main runtime and make a remote item request its permit by message, or forbid remote items inside a Gate. `ExclusiveResource` violations cannot be detected across runtimes; forbid remote nodes inside an exclusive scope (a plan-time check, like the existing `HasExclusive`). |
| Progress and `Report()` | Workers emit per item completion and per slice events; the main runtime merges them into `OnItemCompleted` and into the `ForEach` node's `FanOut`/`Calls` counters. `Report()` grows a remote-node row. |
| Cancellation | The token becomes a cancel message per in-flight batch; the worker's own slice yield reads it. A non-yielding item cannot be stopped; the pool then terminates and restarts that worker (0.14 to 0.22 s measured). |
| Failures | A worker abort (Mono GC abort, as seen in the council work) fails only that worker's batch with a typed error; the pool restarts it. `ForEachFailureMode` semantics apply per item as today. |
| Stays on the main thread | Anything touching the DOM, Blazor components, JS interop objects, `IJSRuntime`, UI state, gate arbitration, the `PipelineTrace` for the visible run, and any step with closures over page state. |

### Honest value

EvalApp's own findings say fan-out width was 1 in all 112 calls at Showroom shapes ("nothing to parallelise here anyway"). So the first win of web native EvalApp is **main-thread freedom (level 1)**, not speed. Level 2 pays only for coarse items (a council year, a ForEach of expensive sub-pipelines) and needs the pool to be sized lazily and conservatively: the 2026-08-28 threads regression was a pool sized from `navigator.hardwareConcurrency`. Default width 1, cap about 4, grown only when the learned round trip cost justifies it. Level 3 (`ChunkedWork` lambdas over shared arrays) would need the arrays copied per chunk; with cheap rows that costs more than it saves, so I would leave `ChunkedWork` local.

### Where the code belongs

Recommended split: **EvalApp stays platform neutral and gains only the small seam** (`IItemDispatcher`, `ExecutionHost.UseDispatcher`, the `remote` opt-in, the pipeline registry by name, plan-time checks for Gate and Exclusive). It would equally serve a process pool or a server later. **Blazier is the browser host package** that implements `IItemDispatcher` with a pool of worker runtimes, the codec glue, the worker script and the typed client. Nothing about browsers, `postMessage` or Blazor goes into EvalApp. Level 1 needs no EvalApp change at all and should ship first.

## 8. The proposed package: EvaluatedApplications.Blazier

### Scope (three parts, one package; split later only if consumers differ)

1. **Scaffold and islands**: an MSBuild target plus a small prerender tool. Per page: render the component (or a declared shell component) with `HtmlRenderer` to static HTML, wrap it in the site template, emit `<page>/index.html`. Pages mark expensive work with a `BuildTime` service so the prerender emits a shell, not an error.
2. **Loader** (under 5 KB of JS, no dependency): starts the runtime on intent (click, visibility via `IntersectionObserver`, idle prefetch, or immediately), retries `rootComponents.add` until dynamic roots are enabled, reports progress with the existing `loadBootResource` hook, supports a no-JS and a failure fallback.
3. **Worker bridge**: generic `worker.js` and client, a typed C# surface, a source generator for proxies, shared and dedicated bundle modes, runtime abort reporting. Later, the EvalApp dispatcher.

### How a page opts in

```xml
<!-- Showroom.csproj -->
<PackageReference Include="EvaluatedApplications.Blazier" Version="..." />
<PropertyGroup>
  <BlazierScaffold>true</BlazierScaffold>
</PropertyGroup>
```

```csharp
// Program.cs: components that may mount as islands, and the ready handshake
builder.UseBlazier(b => b
    .Island<Prism>("prism")            // RegisterForJavaScript + ready callback
    .Island<Analyst>("analyst", lazyAssemblies: ["HoloDb", "Tracer", "Prose"]));
```

```razor
@* Prism.razor *@
@attribute [BlazierPage("/tools/prism", Start = StartOn.Click)]   @* or Visible, Idle, Immediate *@
@inject IBuildTime Build
@if (Build.IsPrerender) { <Skeleton Lines="3" /> } else { ... real UI ... }
```

### Public API sketch

```csharp
namespace Blazier;

public enum StartOn { Click, Visible, Idle, Immediate }

public interface IBuildTime { bool IsPrerender { get; } }

// ---- worker bridge ----
[AttributeUsage(AttributeTargets.Interface)] public sealed class WorkerServiceAttribute : Attribute { }

[WorkerService]                                  // a source generator emits the client proxy and the worker dispatcher
public interface ICouncilJobs
{
    Task<AuditSummary> RunAsync(string slug, IProgress<AuditProgress> progress, CancellationToken ct);
    IAsyncEnumerable<FlaggedRow[]> FlaggedRowsAsync(string slug, CancellationToken ct);
}

public sealed class WorkerOptions
{
    public WorkerBundle Bundle { get; init; } = WorkerBundle.Shared;   // or Dedicated("/council-live/bundle/")
    public int? SoftHeapLimitMiB { get; init; }
    public TimeSpan SilenceWatchdog { get; init; } = TimeSpan.FromSeconds(180);
}

public interface IWorkerHost : IAsyncDisposable
{
    ValueTask<T> StartAsync<T>(WorkerOptions? options = null) where T : class;   // returns the generated proxy
    Task RestartAsync();                                                          // terminate + start: the hard cancel
}

public sealed class WorkerAbortException : Exception { public string LastStage { get; } public int LinearMiB { get; } }

// ---- EvalApp (phase 4; needs the EvalApp seam) ----
public static class BlazierEvalApp
{
    public static IWorkerHost UseWorkerPool(this IAppBuilder app, int maxWidth = 1);   // implements IItemDispatcher
}
```

Contract rules the generator enforces: arguments and results are serializable (System.Text.Json source generated, `byte[]` and `ReadOnlyMemory<byte>` go as transferables), `CancellationToken` maps to the cancel message plus the C# token in the worker, `IProgress<T>` maps to progress events, `IAsyncEnumerable<T>` maps to item events with backpressure by batch. Calls that never yield are reported at generation time when the service method is marked `[NoYield]`, and can be killed with `RestartAsync`.

### Build integration

- MSBuild props and targets in the package: `BlazierScaffold`, `BlazierIslands`, lazy assembly list translated into `BlazorWebAssemblyLazyLoad`, a post-publish step that runs the prerender tool and writes the static pages, and a check that fails the build if a lazy assembly type is referenced from startup code (the `ContentDbHost` trap).
- The publish script already splits the runtime into `website-data/_framework`; Blazier should keep working with the existing `EA_FRAMEWORK_BASE` redirect and pass the same base to workers.
- Interpreter by default (`RunAOTCompilation` off), with an opt in per dedicated worker bundle.

### What it needs from EvalApp

Nothing for phases 1 to 3. For phase 4: the dispatcher seam in section 7 (interface, `ExecutionHost.UseDispatcher`, the remote opt in and the pipeline-name registry). Route as a proposal to evalapp-owner, not an assumption.

### Phased build plan and expected gain

| Phase | Deliverable | Gain (measured where stated) |
|---|---|---|
| 0 (no package, a showroom-owner change) | Interpreter build, drop MQTTnet and Linq.Expressions from the browser graph, re-measure all 8 tools, refresh the csproj comment. | Wire 11.3 to 4.3 MB (measured); phone gallery 28 to 14 s (measured); about 2x slower compute (measured on 5 workloads). |
| 1 | Static `/tools/` gallery, scaffold generation for each tool route, loader with Start on intent. | Gallery and idle visitors: 11.3 MB to 0.07 MB (measured); real content paint under 1 s on the phone profile (measured for the scaffold). Tool users: same bytes, honest visible start. |
| 2 | Islands for all tools, `BuildTime` shell support, retry and ready handshake, per-tool lazy assemblies and a per-tool RCL split. | Lazy assemblies 0.39 MB (7%) measured on Prism; per-tool split est. 0.2 to 0.3 MB per page (estimate). |
| 3 | Worker bridge and generator; move council audit, Prism checkpoint unpack and generation, and training into workers. | Main-thread long tasks to about zero during jobs (measured on the spike); unpack of the checkpoint off the UI thread; second runtime about 0.5 to 0.8 s and 32 MB (measured locally). |
| 4 | EvalApp dispatcher (needs evalapp-owner), worker pool, `Report()` merge. | Only for coarse items; EvalApp's own data says width 1 at today's shapes, so expect no speedup yet. |

### Risks

- **Interpreter correctness and speed**: only 5 workloads were exercised; Forecaster, Prose, Analyst SQL and large checkpoints are unmeasured. A slow tool may need a dedicated AOT worker bundle (that is exactly what the worker bridge allows).
- **Runtime internals**: both worker traps above were silent hangs. The package needs its own browser test harness (the spike scripts are the start) and must pin the .NET SDK, runtime pack and package versions together (the 2026-09-06 outage was a skew).
- **SessionHost state loss** on static page navigation (section 3).
- **Pages limits**: no Brotli, no headers (so no SharedArrayBuffer), `max-age=600`; repeat visit behaviour unmeasured.
- **Memory**: each worker runtime is another heap; phones are tight.
- **Dependency on unsupported SDK internals** if mixed mode is ever pursued.
- **Naming and positioning**: the package must not be described as free or open source; it is covered by the commercial licence like the other EA packages. Third party libraries named above are MIT, which is their business and does not apply to ours.
- **Scope creep** into a general Blazor framework. Hold it to the three parts.

### Recommendation: GO

1. **Now, no package**: hand Phase 0 to showroom-owner as a measured proposal (interpreter build, graph trimming, re-run of the repo's own check scripts on the real source). It is most of the load win.
2. **Build `EvaluatedApplications.Blazier` for phases 1 to 3**, in that order, because each phase is shippable and measurable on its own, and the earlier ones need no EvalApp change.
3. **Adopt no third party worker library as a dependency**; read SpawnDev.BlazorJS.WebWorkers and Microsoft's template for ideas and compatibility, and match their module worker and transferable conventions.
4. **Phase 4 only after** evalapp-owner accepts the dispatcher seam and a real tool shows ForEach width above 1.
5. **No site change until a phase has a working package.** None was made.

No-go conditions, so this stays honest: if showroom-owner's re-measurement finds a tool that is unusable in the interpreter and cannot be moved to a dedicated AOT worker, Phase 0 narrows to partial; if the scaffold and loader cannot keep `SessionHost` behaviour acceptable, Blazier should keep `/tools/*` as one SPA scope and ship only the loader, the lazy assemblies and the worker bridge.

## 9. Flags for other owners (not acted on)

- **showroom-owner**: `Showroom.csproj` comments say AOT is load-bearing; this study's copy build shows it is not on 10.0.11 with the current pins. Verify and update the comment either way. `Pages/Analyst.razor` intro text reads "free, and it never uploads a thing" (seen when the page mounted in the spike): the commercial licence rule says never write "free"; editorial-owner should review that sentence.
- **evalapp-owner**: the dispatcher seam proposal (section 7). Also a runtime note: a canceled `Task` returned through a `[JSExport]` Promise did not settle in .NET 10.0.11 browser-wasm; anything in EvalApp that surfaces a canceled task to JS should convert it to a value.
- **virtual-customer** (read only here): the s9 worker's hand-written protocol is what Blazier would replace; no change asked.

## 10. Reproducing the numbers

Spike files in `docs/lazy-blazor-spikes/` (nothing here is part of the site):

- `measure-live.mjs`: wire bytes by group and paint/ready times for any URL (live or local) under an emulated network and CPU.
- `serve-gz.mjs`: one-origin static server with gzip level 9 and `max-age=600`, shaped like Pages.
- `debug-page.mjs`, `run-bench.mjs`: page console capture and the worker bench runner.
- `creature-rate.mjs`: Creature training episodes in a fixed time.
- `make-island.ps1`: turns a published Showroom `wwwroot` into on-intent island pages.
- `worker/`: `worker.js`, `worker-client.mjs`, `bench.html`, `dotnet/WorkerEntry.cs`.

The builds (AOT, interpreter, mixed, island, lazy, prerender console app, minimal worker app) were made from a copy of `Showroom/` and `HoloKernel/` in the session scratchpad with `dotnet publish -c Release -o <scratch>` and `-p:RunAOTCompilation=false`; they are not kept. To repeat, copy those two folders, apply the small edits described in sections 3, 4 and 6 (JS component registration, `BlazorWebAssemblyLazyLoad` items, `WorkerEntry.cs`, `AllowUnsafeBlocks`), publish, stage `_framework` plus the `prism` and `council-web` folders as a stand-in `website-data`, and run the scripts above.
