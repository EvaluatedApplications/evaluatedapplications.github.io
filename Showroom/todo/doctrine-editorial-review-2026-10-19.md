# Doctrine public copy: editorial review (2026-10-19, overnight)

Reviewed: `todo/doctrine-web-handoff.md` (tools.json, games.json, games/doctrine.json) and `Pages/DoctrinePage.razor`.
Checked against: `Doctrine/Army/PleromaArmy.cs`, `Doctrine/General/TinyHoloGeneral.cs`, `Doctrine/Doctrine.csproj`,
`Doctrine/Army/PleromaArmyLayout.cs`, `Doctrine/Core/Vocabulary.cs`, `web/data/products.json`, `web/data/tools.json`.
Read-only; nothing applied. Owner for every fix: showroom-owner (page) and website-owner (the web/data pastes).

## Verified true (no change)
- General: slot goes to the net only when `_netHits[k] - _tableHits[k] >= Margin` (Margin = 2) and the net is trained; counts halve past 64 examples, so "over recent choices" holds (`TinyHoloGeneral.cs` L101, L182, `GeneralOptions.cs` L31).
- Merge is a move: read the count, `Add(-n)` on the source, `Add(+n)` on the target (`PleromaArmy.cs` L262-272). No MergeKeys anywhere in copy. Phasor pin 2.19.1 (`Doctrine.csproj`). No version or timing figure in the copy.
- Ring of nine (`Period = 9`), 3 line slots, up to four orders, six field orders, rounds 3/5/8 (default 5), Flip is an xor so twice undoes it.
- `tracer`, `evalapp`, `algformer`, `phasor` exist in `products.json`. No em or en dashes in either file (counted 0). No licence, "universal", "complete", verdict, chance or "plays well" wording. No roadmap.

## Findings

### 1. should-fix: "match record is how often" overstates the number
The figure is `1000 * hits / (counted + 2)`, a Laplace-style lower bound; five straight matches read 714 (`TinyHoloGeneral.cs` L65-71). "How often it matched" invites the reader to read 714 as a 71% hit rate, and a perfect run as 714, which is wrong. Places: page L92 (outro), L348 (`dc-conf`), handoff section 4 general paragraph 3.

Old: `how often its whole suggestion matched what you then chose, over your recent choices, out of 1000.`
New: `a score out of 1000 for how well its whole suggestion has matched what you then chose, over your recent choices. It starts low and climbs as the matches keep coming.`

Page L348, old: `Match record <b>@ghost.ConfidencePermille</b> of 1000: how often its whole suggestion matched what you then chose, over your recent choices.`
New: `Match record <b>@ghost.ConfidencePermille</b> of 1000: a score for how well its whole suggestion has matched what you then chose, over your recent choices. It starts low and climbs as the matches keep coming.`

Keep "match record N of 1000" as the label (still no "confidence").

### 2. should-fix: lede is not sizzle first
The `<p class="lede">` (L16-19) gives the promise in one sentence, then four sentences of package plumbing. The plumbing is already in "How it works".
Old: whole lede.
New: `Draft an army, give it up to four orders, and watch the battle run on a navigation mesh. Your general learns how you set orders from your own choices, in your browser, with nothing pre-trained, and suggests the next one. Nothing is uploaded.`
Move "The army is a set of Pleroma stores from the Phasor package, the battle runs on a Tracer navigation mesh, the rounds run as EvalApp pipelines, and the general is a habit table with a tiny HoloFormer from the AlgFormer package." to the top of the "How it works" section (it is the lead the section currently lacks). Same fix is already in the web page (`power` section), so the two stay in step.

### 3. should-fix: handoff claims Cull is not mentioned; the page mentions it
Handoff L142 says "Cull not mentioned (the page hides it...)". Page L400 prints `Cull is not available in this version of the army.` when no army allows it. "This version" reads as a roadmap hint.
Old (page L400): `Cull is not available in this version of the army.`
New: `Cull is not offered on this army.` (or delete the line; the order list is read from the army, so the absence already explains itself, and the handoff then stays true). Showroom CLAUDE.md says "one sentence" is shown, so either way make handoff L142 and the page agree.

### 4. polish: Merge term drops its direction
Handoff `terms` Merge: `Moves one kind of cohort into another.` The palette says "Merge a kind into the one before it" (kind k+1 moves into k).
New: `Moves one kind of cohort into the kind before it.`

### 5. polish: Aura term omits what it does
Handoff: `Counts the soldiers who carry a trait.` In code the count (capped) becomes the aura's worth and gives cohorts carrying the trait a power bonus (`Power(...)`, `PleromaArmy.cs` L255-257).
New: `Counts the soldiers who carry a trait and gives those cohorts a power bonus for the round.` ("for the round": `EndRoundAsync` clears auras.) Optional; skip if the bonus is not shown on screen.

### 6. polish: tools.json line is the long form; the card wants the promise
Old: `Draft an army, give it up to four orders, and watch the battle on a navigation mesh. Your general learns how you set orders from your own choices, in your browser, with nothing pre-trained.`
New: `Raise a general that learns how you command. Draft an army, give it up to four orders, and watch the battle run on a navigation mesh, with nothing pre-trained and nothing uploaded.`
(Matches the games.json sizzle; "from your own choices" stays in the page body.)

### 7. polish: title colon
`Doctrine: an army game with a general that learns from you` is fine and true. Leave it.

## Standing-rule scorecard
- Current state only / no roadmap: pass except finding 3 ("this version").
- Sizzle first: games.json and doctrine.json headline pass; page lede fails (finding 2).
- Claims true: pass except finding 1 (wording of the number).
- Never universal/complete, no verdict/chance/"plays well": pass.
- Describe the work not the people: pass.
- Licence wording: none present, pass.
- No em dashes: pass.
- Phone: only the layout fact, pass. wasm timings kept out, pass.

## Question for the user
None needed. (If you want the Cull line kept for transparency, finding 3 only needs the handoff sentence corrected.)
