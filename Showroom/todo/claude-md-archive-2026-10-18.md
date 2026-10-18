# Showroom CLAUDE.md overflow, moved out 2026-10-18 (Plenum slice 2)

Verbatim from CLAUDE.md before the move. CLAUDE.md keeps a short summary and points here.

## Council Spending Scanner: `Pages/CouncilSpending.razor` (`/council-spending`, `/council-spending/{slug}`)
Built on the virtual-customer's PHONE-SIZED export (`VirtualCustomer\web_export`, SPEC_FOR_SHOWROOM.md items 9-30). 28 councils (counted
from `index.csv`), no HoloDb/engine in the browser. Public audience, mobile-first, not editorial: audit terms, plain prose + counts + GBP,
OGL credit, never a cause. Its static shell carries no council count or name (they need `index.csv`); the live hub reads them.
- **A new council needs NO page edit.** After the virtual-customer's `phoneexport <slug>` (and every `export`): run the builder
  (`AboutUs\CouncilWebBuilder`, `dotnet run -c Release --project CouncilWebBuilder`, ~3 min; 2nd arg `profiles` rewrites only
  profiles.json.gz; fails above 50 MB/file), commit + push website-data only. It DISCOVERS councils: an `index.csv` row (slug = folder)
  + the engine profile whose name matches (`NameMatchesSlug`); order = the engine's. A council is HELD (rows also dropped from
  `cross/*.csv`) when its profile `VerificationNote` starts "Profile text pending", it is in env `COUNCIL_HOLD`, or
  `VirtualCustomer\export\checklist_holds.csv` lists it `withhold`. **CHECKLIST GATE**: the builder REFUSES (exit 2, writes nothing) unless
  `export/checklist_results.csv` exists, every cell is PASS or HELD, and the file is newer than every file in `web_export` and `export`
  (run `CouncilAudit.Cli checklist all` first, in `C:\Users\dongy\VirtualCustomer`).
- **`index.csv`** = export columns + `Name,Short`: list, order, count, names (`CouncilWebData.Councils` via `IndexAsync`; nothing draws before `_catalogReady`;
  `CouncilTerms.Words(n)` writes the count). The builder MEASURES per-council facts into `profiles.json`; `CouncilFacts` + `CouncilTerms` build every statement from
  them (say plainly where a check cannot run, never an empty list). `Services/CouncilOverrides.cs` holds ONLY prose that cannot be derived; `ListForEveryCouncil = true`.
- **Audit rules, regression, load-by-year**: no raw engine code is ever printed (`Services/CouncilCodes.cs`; `CouncilWebBuilder` and `CouncilTermsCheck` FAIL on an unmapped code); no
  "fault"/"published twice"/"lead" wording; say where a check cannot run; the letter quotes facts, never a class or cause. Regression: `dotnet run -c Release --project
  CouncilTermsCheck -- <council-web> out.txt [slugs]` against `CouncilTermsCheck/golden.txt` (explain every diff). `BudgetTestPanel`'s stage numbers are TYPED: recompute when a stage
  moves. Full text of each rule: archive file.
- **Data, code, perf**: layout, `CouncilWebData`/`CouncilLoad`/`CouncilMonth`/`CouncilGap`/`FoiTray` notes, the perf scripts (`scripts/council-load-perf.mjs`, `council-perf.mjs`,
  `gap-perf.mjs`, `rules-check.mjs`) and baselines are in `todo/claude-md-archive-2026-10-17.md`. LINQ over decimals is slow in WASM.
- Gotchas: `--` in csproj XML comments breaks load; BudgetPanel's `<text>` trick fails in code blocks; CSS is one scoped file using
  `.cs ::deep`; lists with stateful children need `@key`.



## Chat/Stories constants (verbatim)

**Chat/Stories constants** (re-measure on every re-mint): `MaxReplyStepsConst = 160` / `MaxStorySteps = 448` are CEILINGS pinned on ctx=512 K=3; the caps in force come from the LOADED
model's `Context` via `HoloKernel.ContextBudget` (`ReplyCap`, `StoryCap`, `PromptBudget` = Ctx - cap, `KeepTail`). **THE WINDOW RULE: prompt + reply must stay under `Context`, or every
step past it re-Primes the whole window** (~2.9 s PER STEP at 512/L33; ~32 ms at ctx=64 L9). Stories SLIDES (`StoryRollTarget = 400`; at `cache.Filled == Context` re-Prime on
`ContextBudget.RollTail`, then O(1) `StepToken`); `ParagraphChars = 130`; ~1 story in 5 ends early (DegenerateTail cut): the model's doing. Chat uses the tagged wire format (`user: Q\nprism: A` + STOP), Stories plain text. Checks: `chat-turns.mjs <dist> <website-data> 9 6`, `story-times.mjs`.
