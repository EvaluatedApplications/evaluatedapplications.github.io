# CLAUDE.md overflow archived 2026-10-19

- **Deep links / boot shell (cutover 2026-10-15)**: the generated site owns `/tools/`, so `dist` has NO root `index.html` (`finish-shells.mjs` deletes the raw template and
  FAILS the publish if a generated root shell appears; `publish-site.ps1` asserts `dist/app/index.html`). Boot shell = `Pages/BootShell.razor` -> `/tools/app/` (`noindex`).
  `/tools/council-spending/{slug}` has no file: Pages 404 -> site `404.html` (`APP_DEEP_LINKS`) -> `/tools/app/?/council-spending/<slug>` -> restore script in
  `wwwroot/index.html` (`replaceState`, `__eaBounced`). A new parameterised route with no shell = add its prefix to `APP_DEEP_LINKS` in the SAME change (website-owner's file).

**Chat/Stories constants** (re-measure on every re-mint): ceilings `MaxReplyStepsConst = 160` / `MaxStorySteps = 448` (ctx=512 K=3); the caps in force come from the LOADED model's `Context` via `HoloKernel.ContextBudget`. **THE WINDOW RULE: prompt + reply must stay under `Context`, or every step past it re-Primes the whole window** (~2.9 s PER STEP at 512/L33). Stories slide (`StoryRollTarget = 400`, `ParagraphChars = 130`). Checks: `chat-turns.mjs`, `story-times.mjs`. Full text: `todo/claude-md-archive-2026-10-18.md`.


- **Redirect stubs**: `/tools/{creature,forecaster,stories,cartographer}/` (to Prism) and `/tools/dashboard/` (to `/umbrarium/`, the superseding demo) are written by `finish-shells.mjs` (`STUBS`; `publish-site.ps1` asserts them in dist) as `noindex` meta-refresh + script pages (Prism ones keep query and hash; no hash = that tab).


## Doctrine wasm measurement (moved from CLAUDE.md, 2026-10-19)
MEASURED (AOT wasm under Node, 2026-10-18, BEFORE the hook change): digest equal to desktop, ~105-130 ms/round, longest step 19 ms, probe bundle +10.8 MB raw / +3.0 MB gzip.


## Doctrine details moved from CLAUDE.md (2026-10-19 pre-publish pass)
- **Hooks**: progress = the skeleton's `IRoundObserver` (`StageEntered`/`BattleTick`), the arena's drawing = `DoctrineContext.LayoutOf`, refusals after the confirm =
  `DraftedAsync`; the live "would cost" hint still drafts onto a COPY via `TryParse(code)` because the pipeline report only exists after the confirm. Cancel/Leave cancel the
  run's token; a cancelled round restores the army from its round-start code and restarts the run.
- **Copy rules**: the general "learns how you set a doctrine from your own choices, in your browser, with nothing pre-trained"; examples = each doctrine AND each battle result;
  never that the net beats a table or "plays well"; battles "simulated once on your device", the replay "is a recording"; no saved form; Cull HIDDEN (one sentence) while no army allows it.
- **Order names** (user 2026-10-19: lord-based, no builder's terms): Press on, Hold the line, Ride the flank, Fall back, Guard the host, Run them down; Reform the ranks, Reforge,
  Drill the host, Swear into the, Raise the banner of; Should a quarter / half / three quarters fall. `DoctrineSession.Trial` previews an order on a copy of the host; table in
  `todo/doctrine-story-from-mechanics.md`.
- **The general is masked to the council** by the game, so under offers it often has no pick (a habit on orders that are not on the table is dropped). FINDING (Doctrine army, not
  ours): the same draft with its picks in another order has the same army digest and a different battle.
- **Gotchas**: a bUnit DOM cache can go stale after a background-thread render (the proof re-renders on a stalled wait); a lambda parameter may reuse a local's name in this C#.
- **2026-10-19 pre-publish pass facts**: the proof reads `DoctrinePage.razor.css` from disk (so a mutant of the css is possible); the siege briefing needs `DoctrineGame.BriefingAsync`
  (committed in Doctrine eabe96d); the muster has no reroll (`UnitOffer` has no reroll member; the proof asserts it by reflection); `TinyHoloGeneral`'s class comment still says
  "TableOnly is the shipped default" while `GeneralOptions.Mode` defaults to Hybrid (code wins; flagged to the coordinator).