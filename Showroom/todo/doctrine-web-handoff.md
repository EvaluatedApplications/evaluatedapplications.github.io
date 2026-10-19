# Doctrine: web hand-off for website-owner (2026-10-19)

Author: showroom-owner. NOT applied. website-owner pastes these into `web/data/` (I never edit `web/`).
Nothing here is published, and Doctrine has not been seen in a browser or on a phone.

## 0. Proof re-run (done 2026-10-19, before writing this)

Doctrine commits since my last proof run fixed a defender-side formation bias, a non-mirrored arena
navmesh and unit id order, so battles differ from the earlier run. Re-run result:

- `dotnet build Showroom.csproj -c Release`: 0 warnings, 0 errors.
- `tools/DoctrineUiProof`: **ALL PASS (154 checks)**, still green on the changed battles.
- `Doctrine.Tests`: 352 passed, 0 failed.
- `tools/PlenumProof`: ALL PASS (201 checks).

## 1. Unverified claims (keep out of the copy, or keep the wording below exactly)

- **Phone**: never tested on a device. The copy may say only the layout fact ("one column, 44 px tap targets",
  asserted by the bUnit/CSS rules in `DoctrineUiProof`), never "works on phones" or "tested".
- **Browser**: never opened in a browser. No "try it" claim beyond what the page does; no speed claim.
- **wasm numbers** (AOT under Node, 2026-10-18: ~105-130 ms/round, longest step 19 ms, runtime probe +10.8 MB raw /
  +3.0 MB gzip) were measured BEFORE the hook change AND before the defender-bias/navmesh/id-order fixes. Do not put
  any timing or size figure on the site until re-measured. The copy below has none.
- **The general**: the claim is only "learns how you set a doctrine from your own choices, in your browser, with
  nothing pre-trained". No claim that it beats a table, no strength claim, no number except the on-screen
  "match record N of 1000" (never "confidence", probability or accuracy).
- Phasor 2.19.1 is what Doctrine pins; its Merge order is a move (read, subtract, add). Do not describe a
  MergeKeys-based merge.

## 2. `web/data/tools.json` entry (paste after the plenum entry)

```json
    {
      "id": "doctrine", "name": "Doctrine", "slug": "/tools/doctrine/", "cat": "foundation", "poweredBy": ["phasor", "tracer", "algformer", "evalapp"],
      "line": "Raise a general that learns how you command. Draft an army, give it up to four orders, and watch the battle run on a navigation mesh, with nothing pre-trained and nothing uploaded."
    },
```

(`cat`: Plenum uses `foundation`; if website-owner wants the AlgFormer colour instead, `algformer` is the only other
sensible value. Check that `poweredBy` ids `tracer` and `evalapp` exist in `products.json`; `data.mjs` will say.)

## 3. `web/data/games.json` entry (append to `games`, after plenum)

```json
    {
      "id": "doctrine",
      "name": "Doctrine",
      "slug": "/games/doctrine/",
      "play": "/tools/doctrine/",
      "tool": "doctrine",
      "cat": "foundation",
      "poweredBy": ["phasor", "tracer", "algformer", "evalapp"],
      "modes": ["Solo"],
      "sizzle": "Raise a general that learns how you command.",
      "line": "An army game where you set the orders and a small general watches how you do it. Draft your cohorts, give up to four orders, and watch the battle run on a navigation mesh. The general suggests your next doctrine from your own choices.",
      "teaches": "Each order is one operation on the army's store, and the list of orders you may give is read from that store, so you see why an order is greyed out.",
      "powers": "The army is a set of Pleroma stores from Phasor, the battle runs on a Tracer navigation mesh, the rounds are EvalApp pipelines, and the general is a habit table with a tiny HoloFormer from AlgFormer.",
      "phone": "Laid out for a phone screen: one column, 44 px tap targets.",
      "content": "games/doctrine.json"
    }
```

## 4. `web/data/games/doctrine.json` (full draft, same shape as `games/plenum.json`)

Order of sections, `terms` shape and `closing` copied from plenum.json. `**bold**` and backtick code spans are the
inline markup the plenum file already uses. Code spans below are the names on the page, not API promises.

```json
{
  "title": "Doctrine: an army game with a general that learns from you",
  "description": "Draft an army, set up to four orders, and watch the battle on a navigation mesh. A small general learns how you set orders from your own choices, in your browser.",
  "eyebrow": "Game",
  "headline": "Raise a general that learns how you command.",
  "sub": "Draft your cohorts, give up to four orders, and watch the battle. The general learns from every doctrine you set, in your browser, with nothing pre-trained, and suggests the next one.",
  "playLabel": "Play Doctrine",
  "playNote": "One player, against armies the game raises. It runs in your browser tab. Laid out for a phone screen: one column, 44 px tap targets.",
  "sections": [
    {
      "id": "goal",
      "title": "How a game goes",
      "paragraphs": [
        "A game is 3, 5 or 8 rounds. You start with an army levied for you and some gold. Each round you **draft**, set a **doctrine**, and watch one battle against an army the game raises.",
        "The same seed raises the same enemies on the same ground, so you can replay a game. Leave the seed empty for a new one."
      ]
    },
    {
      "id": "orders",
      "title": "Orders",
      "paragraphs": [
        "A doctrine is up to four orders, and no orders is allowed: every line then advances. There are two kinds. **Army orders** change the army itself before the battle. **Field orders** go to a whole line during it. An order the army says is not legal right now is greyed out."
      ],
      "terms": [
        { "term": "Rotate the line", "text": "Moves every cohort to the next place in the line." },
        { "term": "Flip a trait", "text": "Cohorts that had the trait lose it and the others gain it, all at once. Twice undoes it." },
        { "term": "Age", "text": "Ticks the veterancy ring of every cohort one place." },
        { "term": "Merge", "text": "Moves one kind of cohort into the kind before it." },
        { "term": "Aura", "text": "Counts the soldiers who carry a trait." },
        { "term": "Field orders", "text": "Advance, Hold, Flank, Fall back, Guard and Hunt. A line with no order advances." }
      ]
    },
    {
      "id": "battle",
      "title": "The battle",
      "paragraphs": [
        "The ground is a baked Tracer navigation mesh. Each cohort is a group of agents that path, take cover and strike on it, and you watch the arena as it runs.",
        "A battle is simulated once, on your device, and recorded. The replay is a recording of that one battle: playing it back does not run it again, and you can scrub through it."
      ]
    },
    {
      "id": "general",
      "title": "The general",
      "paragraphs": [
        "Your general starts empty. It learns how you set a doctrine from your own choices, in your browser, with nothing pre-trained, so different players end up with different generals. It suggests and you decide.",
        "Every doctrine you set is an example for it, and so is each battle result. Inside it are a small habit table and a tiny HoloFormer from the [AlgFormer](/algformer/) package. For each of the four slots the table speaks, unless the HoloFormer has been right on that slot at least two more times than the table over your recent choices, in which case it takes the slot.",
        "The **match record** beside a suggestion is a score out of 1000 for how well its whole suggestion has matched what you then chose, over your recent choices. It starts low and climbs as the matches keep coming. Nothing is saved: refresh the page and the general is gone, though it carries what it has seen into your next game on the same page."
      ]
    },
    {
      "id": "code",
      "title": "Army codes",
      "paragraphs": [
        "At the end of a game you can copy an army code, a short piece of text that holds your army. Paste it on the start screen to play on with that army. It holds the army only: your general is not in it."
      ]
    },
    {
      "id": "power",
      "title": "What powers it",
      "paragraphs": [
        "Each cohort is an item in a Pleroma store from the [Phasor](/phasor/) package: its kind is the key, its traits are the flags, its place in the line is the arrangement, and its veterancy is a position on a ring of nine. An order is one operation on that store, and the list of orders you may give is read from the store.",
        "The battle runs on [Tracer](/tracer/), the rounds run as [EvalApp](/evalapp/) pipelines, and the general uses [AlgFormer](/algformer/). Nothing is uploaded: the whole game runs in your browser tab. Read more on [the Pleroma page](/phasor/pleroma/)."
      ]
    }
  ],
  "closing": {
    "title": "Ready when you are",
    "text": "Press play. The game loads once in your browser tab, and nothing needs installing."
  }
}
```

Copy checks done by me: no em dashes, no licence wording, no verdict/chance/"plays well", no "universal" or "complete",
no version or timing figure, no phone-tested claim, Cull is not in this copy (the page shows only the sentence "Cull is not offered on this army." while no army allows it).
The `/tracer/` and `/evalapp/` links assume those product slugs exist (they do in `products.json` per
website-owner's CLAUDE.md; `check-site` will confirm).

## 5. Also needed on the website side

- `Showroom/scripts/finish-shells.mjs` already has the `doctrine` meta entry (mine).
- `finish-shells.mjs` `APP_DEEP_LINKS` (website-owner's file) needs no change: `/doctrine` has no route parameter.
- Rebuild `site-next/` with `node web/scripts/run.mjs --versions=committed` after the paste (deploy guard `diff -r`).
