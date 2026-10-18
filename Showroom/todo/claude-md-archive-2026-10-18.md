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

## The Plenum: slice 1 and Duel detail (verbatim, moved 2026-10-18 for the Doctrine round)

The Plenum `/plenum` (L0 of `MonoRepo/Phasor/todo/pleroma-card-game-design.md`; slice 1 = Solo, slice 2 = lobby + two-player Duel; all proofs PASS; NOT published, UNSEEN IN A BROWSER OR ON A PHONE). ALL game state is
Pleroma stores (Deck, Hand, Board, Discard, Engine, Tempo), changed only by Pleroma calls. Core `Services/Plenum/*.cs` (namespace `ShowroomPlenum`, no Blazor deps, compiled into every proof): `PlenumShapes` (zone = KeyCapacity 8, FlagBits 8,
CompleteKeys, KeyCycle 31, ItemLines false = 286 cells), `PlenumCards` (12 units), `PlenumRng` (splitmix64; Pleroma calls only `NextInt64(total)`; `SeatSeed(match, seat)`), `PlenumGame` (Draw `SampleKey`, moves Subtract+Add, Tally `CountWhere`, Flip
`RelabelFlags`, `FlipOther` aims at the other Board, Fuse `Bind`, Flipper at `EndTurn`), `EngineTicker` (solo only). Measured: `Restrict` refuses on this shape; `CountWhereByTime` needs +1,536 cells; Tick without TimePeriod is a silent no-op.
**Duel** (rules as built): `DuelMatch` = two `PlenumGame` sides seeded from (match seed, seat); an order = up to 3 costed plays (+ free put-back) as tokens `u7 t0 f0s f0o x1,2 p` with `prev` = the LIVE state hash; when both orders for round r are in, seat 0's
plays then seat 1's, then if either side cashed the Goal (Tally on 8 Fire) it ends (both = draw), else the Flipper acts on each Board and each draws. Idempotent by (seat, round), first wins; future rounds wait (<= 8 ahead). State hash = FNV-1a over RawCells
of Deck/Hand/Board/Discard/Tempo of BOTH sides (no Engine in a duel); published after every round, mismatch or bad `prev` halts BOTH (`halt` event), never silently. NOT Pleroma (listed in code): lobby, room, pending orders, log, clocks, presence.
`PlenumSession` = lobby list (retained `plenum/v1/lobby/+`, stale > 90 s hidden, host heartbeat 30 s), create/join/spectate, private room (`plenum/v1/priv/{code}`), turn clock 90 s (sends an EMPTY order), 60 s forfeit, `sync` replay on reconnect.
ONE retained message per room = listing AND setup (it carries the seed); the host's last will clears it (a connection has one will). Events on `room/{code}/ev` (join/order/hash/sync/bye/gone/forfeit/halt). Seat claims are NOT authenticated.
Relay: `IPlenumRelay`; `LoopbackBroker` (proofs); `MqttCodec` + `MqttWsRelay` = our own MQTT 3.1.1 client over `ClientWebSocket` (no package; QoS 1 resend with DUP, keepalive, backoff, filters); every topic must start `plenum/v1/` (NEVER `pzc/`, Prism's swarm
shares the broker). URL = `window.EA_PLENUM_RELAY` (index.html) -> `PlenumRelayConfig`. Page: Solo/Duel switch; an order is built on a `DuelMatch.Fork()` preview; opponent summary strip always visible, their Board in a closed `<details>`; spectators read-only.
Phone first (`Plenum.razor.css`: 16px gutters, 44px targets, 16px inputs, one column). Known design notes: seat 0 applies first each round; the opening deal lets Flip+Tally win in round 1 for either seat. Needs `web/data/tools.json` (hand-off).
Gotchas: set `Phase = Lobby` BEFORE subscribing (retained listings arrive during SUBSCRIBE); a ReadOnlySpan cannot be captured by a local function (MqttCodec reads through a small class); `--` in a csproj comment breaks MSBuild.
## Prism tab: Creature and Forecaster detail (verbatim, moved 2026-10-18 for the Doctrine round)

  **Creature/Forecaster = the same model on other jobs, a TECH DEMO**: TEXT tasks scored by next-token logits over one-token labels (`Services/PrismTab/`; Creature = 5x5 map -> N/E/S/W with Tracer BFS as
  the hidden answer key; Forecaster = last 16 up/down/flat words -> " up"/" down"; formats in `MonoRepo/AlgFormer/todo/prism-tab-probe-2026-10-18.md`). Playing and training are ONE act
  (`PrismTaskLab` owns knobs, Run/Pause/Stop/Reset and the cooperative loop; the tab supplies the tick). `PrivateCopy` = a NEW session (K and alpha set by hand, they do not serialize); the shared
  session is only READ and proofs assert its bytes are unchanged. Readout = share of the last 50 calls right and NOTHING else (no baseline, chance line, verdict or "learns" wording). Forecaster data = stored
  `wwwroot/data/forecaster-history.json` ("Data as of", no live claim). `TokenVoice.Unlock()` (first in Send / Tell me a story) resumes the AudioContext. Knob list, sound history, measured scores: archive file.
