# Doctrine multiplayer: the Duel (design v0, coordinator, 2026-10-19)

User priority (2026-10-19): "Prioritize getting a multiplayer version of the game setup." Restrained so it stays possible and
fair (see design-queue items 10 and 10b). v0 = a two-seat DUEL of N rounds over the existing relay; no map, no spectators, no
ranking. Everything else in the single-player game (offers, set and siege, chronicle, the general) carries over.

## Rules

- Two seats, A (creates the room) and B (joins by a short code). Rounds 3, 5 or 8, chosen by A. Same seed, same starting levy and
  gold for both seats (a mirror), the same arena each round (arena rule from the seed), siege roles by round parity: seat A
  besieges in even rounds, seat B in odd rounds (so each seat besieges equally often).
- Each round, both seats get the SAME offers from the shared seed (unit packs scaled to each seat's own purse; the same order
  cards, masked to each seat's own legal list). Picks are independent: both seats may take the same card.
- SEALED, simultaneous: each seat builds its muster, council and formation, then seals. Commit then reveal (below). When both are
  revealed the battle runs ONCE on the authority's device (seat A), the recording goes to seat B, both watch the same replay and
  the same chronicle, spoils and the next round follow. Winner after the last round: more fields won; a tie goes to the side that
  holds more power at the end (derived from UnitRules.Rating, no typed rule).
- TIMER per turn (default 45 s, host-selectable presets): at the deadline a seat that has not sealed is sealed by the game's
  starter advice (or its general's pick if it has one); three auto-seals in a row mark the seat idle (shown to the other seat).
  No timer in a beginner's first game against the scripted enemy is unchanged; in a duel the timer is on.
- Casual trust, said plainly on screen: one device simulates; the other plays back the recording. Each seat verifies the digest
  of ITS OWN army from the authority's result against its own local build; the outcome itself is the authority's word. A room
  code is readable by anyone who has it (the relay is shared and public).

## Protocol (relay topics only under plenum/v1/, NEVER pzc/; reuse MqttWsRelay, MqttCodec, PlenumRelay patterns)

Topic root: `plenum/v1/doctrine/<code>/`. Messages are small JSON or the existing token format, idempotent and keyed by
(round, seat, kind) so a duplicate does nothing:
- `room` (retained, by A): seed, rounds, timer, besieger parity rule, version, state (lobby, round n phase). The source of truth for
  a reconnecting client.
- `join` (B), `ready`/`start`.
- `commit/<round>/<seat>`: SHA-256 of (picks canonical form + nonce). Commit FIRST, so neither seat can adapt to the other.
- `reveal/<round>/<seat>`: picks (unit packs, tokens in slot order, formation) + nonce. A reveal that does not match its commit is
  refused and the round is forfeited by that seat.
- `result/<round>` (retained, by A): army digests of both seats after the orders, the replay (ReplayCodec bytes, gzip, base64;
  chunked if over the broker limit), the RoundRecord facts the UI shows (winner, losses, spoils, besieger, clock fell).
- `presence` (retained last-will): online/idle per seat; MQTT keepalive 30 s, reconnect with backoff, resubscribe, fetch retained.
- `end`: final score. Leaving mid-round forfeits the round after the timer.

## Doctrine library seam (tracer-owner)

No network code in Doctrine. The authority runs ONE Game with two seats: today the enemy side is `ScriptedOpponent`; make the
enemy side an `IOpponent`-style seam (draft picks, council picks, formation) whose scripted implementation reproduces the old
game byte for byte (digest pins unchanged with the scripted opponent) and whose duel implementation hands in the revealed picks.
Offers are generated for BOTH seats from the shared seed (stream per (seed, round, seat-neutral tag, reroll)); the commit/reveal
canonical form of a seat's picks is defined in Doctrine (a pure function `DuelPicks.Canonical(...)`, `DuelPicks.Commit(...)`) so
both clients and the proof use ONE definition. Rerolls: a seat's reroll draws from the shared offer stream with its own index
(the same reroll gives the same cards to both seats, fair) and is revealed with the picks.
The round result for the viewing seat is a mirror (the viewer is always 'you'): `RoundRecord` from a seat's point of view.

## Showroom (showroom-owner), after the seam

`DoctrineDuel` service on the relay (reuse the Plenum client and lobby code; the Plenum pages stay), lobby (create, share a code
or link, join, seat names), the existing HUD in duel mode: the opponent panel shows the opponent's army by line (kinds and counts,
as the briefing does) and their state (choosing, sealed, idle, offline), a countdown from an absolute deadline, 'waiting for the
other seat', the shared replay and chronicle, rematch. Phone compatible. `DoctrineDuelProof` console project with a fake relay
and two in-process pages (like `PlenumDuelProof`): full duel to the end, a timeout auto-seal, a commit/reveal mismatch, a
duplicate message, a dropped connection and a resync from retained state, an authority swap is OUT of v0 (the authority leaving
ends the match with a plain message).

## Not in v0

Map and lanes, spectators, ranking, chat, a general standing in for a closed tab, saving anything.

## Acceptance

Two browser pages play a full 3-round duel through the relay (proof with a fake relay; one live run against the public relay,
once), the same seed gives the same offers on both seats, a seat that never seals is auto-sealed on time, a tampered reveal is
refused, a reconnect resumes the round, and the result both seats see is the same recording.
