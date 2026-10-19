# Doctrine: the story is the mechanics (design, coordinator-owned, 2026-10-19)

Decision (user, 2026-10-19): the game is mine to design inside his constraints (our stack; every mechanic through Pleroma, Tracer,
EvalApp, AlgFormer), and the ethic is FromSoftware's: the mechanics ARE the story. Dark Souls does not explain dying and the
world resetting with a cutscene; the whole fiction exists to make that rule make sense. Doctrine does the same with its own rules.

## The rule for writing anything in the game

1. Never invent a mechanic to fit a story. Find the story inside a rule that already exists.
2. A line of lore must be TRUE of the code. Every line below carries the rule it explains and where it lives. If the rule changes,
   the line changes or goes.
3. No exposition. Story lives where the player already looks: order names, refusal messages, the cohort blurbs, the arena names,
   the general's panel. A refusal states the rule plainly first, then speaks in the voice.
4. The player pieces it together. No lore screen, no codex page, no wall of text.
5. Public text stays current-state, sizzle first, claims true. The one-line promise comes first; the voice never replaces it.
   No borrowed names or terms from other games.

## The premise

You hold the Count: the ledger of an army, eight names at most. You do not move soldiers. You speak orders, and an order reaches
everyone in the Count at once. A general is the part of the army that remembers how you speak.

## Mechanic to fiction (each row is checked against the code)

| Rule (where it lives) | The line it supports | Draft voice |
|---|---|---|
| The command span is 8 kinds; a ninth is refused (`DoctrineVocabulary.Kinds`, Army capacity) | To learn a new name you must forget one. | "A commander holds eight names. Dismiss one before you call a ninth." |
| An army order is ONE pass over the whole army (Pleroma operation) | An order is spoken once and reaches everyone. | Order blurbs: "Spoken once. Heard by all." |
| Veterancy is a ring of nine and wraps (`VeterancyPeriod = 9`, Age) | Nothing in the Count grows without end. | Age: "Veterans turn on a ring of nine. The ninth year returns them to the first." |
| Cull is not offered; a name leaves the Count only by the field | No order strikes a name out. | Where the page now says Cull is not offered: "The Count loses a name only on the field." |
| A battle is simulated once; the replay is a recording, not a second battle (determinism policy) | A battle is fought once; you watch the account of it. | Watch screen: "This is the account of the field. It is not fought again." |
| A defeat costs the fallen and still pays 2 gold; a win pays 6 and 1 experience (`SpoilsStage`: killed/2 + 6 or 2) | The Count loses names, not the campaign. | Result: "The field takes its names. The Count marches on." |
| A campaign is 3, 5 or 8 fields (rounds option) | A campaign has a fixed length. | "Three fields." "Five fields." "Eight fields." |
| The general starts empty, is taught only by you, is gone on refresh | A general is lent, not kept. Write the army down; raise the general again. | Start: "Your general is blank. It will be what you teach it." End: "The army can be written down. The general cannot." |
| Two voices: the habit table speaks first; the HoloFormer takes a slot only when it has been right at least two more times than the table (`Margin = 2`) | Habit speaks first. The mind speaks only when it has earned it. | Glow panel: "Habit says this." / "Mind says this." |
| The match record is 1000*hits/(counted+2), so it starts low and climbs | A general starts by doubting. | Panel: "It starts by doubting. Each match builds its record." |
| Arenas: open field, chokepoint, ridge, falling gate (a flag falls at a tick) | Places with one fact each. | "The Open Field", "The Neck", "The Ridge", "The Gate That Falls" (the gate falls at its appointed tick) |

Not used until the code backs it: signed counts as debts or curses, the async ladder as "found accounts", seasons on the time ring.

## Playstyles as schools (waiting for the measured style matrix)

If tracer-owner and algformer-owner show several valid styles, each becomes a named school of the Count with its own voice in the
general's panel (for example the one who keeps veterans, the one who banks, the one who wins fields). The names come from what
the style DOES in the measurements, not from a list we wrote first.

## Open (decisions for later, not built)

- If the game needs a long-run lever (infrastructure) so battle-focused play can pay off with skill, its story must come from the
  mechanic first (what it is, why it costs). The smallest game-logic addition goes to the user as a decision.
- Whether the general's amnesia on refresh should stay a rule or become a "general code" the player can keep. If it is kept, the
  line "the general cannot be written down" goes.

## How it gets into the page

Showroom owner applies strings only after tracer-owner's rebalance lands (same files, avoid conflicts). Sites: the refusal notice,
start screen, end screen, glow panel, cohort blurbs, arena names, order tooltips. DoctrineUiProof gets a check per line (present;
and for any line that states a rule, the number it relies on read from the code, not typed) and a mutant that breaks one.
