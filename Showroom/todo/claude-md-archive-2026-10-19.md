# CLAUDE.md overflow archived 2026-10-19

- **Deep links / boot shell (cutover 2026-10-15)**: the generated site owns `/tools/`, so `dist` has NO root `index.html` (`finish-shells.mjs` deletes the raw template and
  FAILS the publish if a generated root shell appears; `publish-site.ps1` asserts `dist/app/index.html`). Boot shell = `Pages/BootShell.razor` -> `/tools/app/` (`noindex`).
  `/tools/council-spending/{slug}` has no file: Pages 404 -> site `404.html` (`APP_DEEP_LINKS`) -> `/tools/app/?/council-spending/<slug>` -> restore script in
  `wwwroot/index.html` (`replaceState`, `__eaBounced`). A new parameterised route with no shell = add its prefix to `APP_DEEP_LINKS` in the SAME change (website-owner's file).

**Chat/Stories constants** (re-measure on every re-mint): ceilings `MaxReplyStepsConst = 160` / `MaxStorySteps = 448` (ctx=512 K=3); the caps in force come from the LOADED model's `Context` via `HoloKernel.ContextBudget`. **THE WINDOW RULE: prompt + reply must stay under `Context`, or every step past it re-Primes the whole window** (~2.9 s PER STEP at 512/L33). Stories slide (`StoryRollTarget = 400`, `ParagraphChars = 130`). Checks: `chat-turns.mjs`, `story-times.mjs`. Full text: `todo/claude-md-archive-2026-10-18.md`.


- **Redirect stubs**: `/tools/{creature,forecaster,stories,cartographer}/` (to Prism) and `/tools/dashboard/` (to `/umbrarium/`, the superseding demo) are written by `finish-shells.mjs` (`STUBS`; `publish-site.ps1` asserts them in dist) as `noindex` meta-refresh + script pages (Prism ones keep query and hash; no hash = that tab).


## Doctrine wasm measurement (moved from CLAUDE.md, 2026-10-19)
MEASURED (AOT wasm under Node, 2026-10-18, BEFORE the hook change): digest equal to desktop, ~105-130 ms/round, longest step 19 ms, probe bundle +10.8 MB raw / +3.0 MB gzip.
