# Doctrine: batched Showroom publish checklist (2026-10-19)

Author: showroom-owner. NOT run. Nothing below has been executed. The publish is AOT (~5 min, heavy): it needs a
Prism training pause, and the user does the pause. Batch it with the other pending Showroom work (Prism tabs, Helix,
Plenum are also built but unpublished and unseen in a browser).

## A. Hard blockers (do not publish until all are true)

1. **Phasor 2.20.0 published, then the site Phasor counts.** The site data already states the post-MergeKeys counts
   (40 operations, 8 of 40). Those must not go live before Phasor 2.20.0 is on NuGet. `check-site`/`versions.json` only
   stamp versions NuGet's flatcontainer index lists. Doctrine itself pins Phasor 2.19.1 and does not need 2.20.0, but a
   website push that carries the new counts does.
2. **Tracer/EvalApp PATCH release.** Doctrine pins the PUBLISHED Tracer 2.0.3 (and EvalApp 4.4.1). A fan-out race fixed in
   EvalApp/Tracer SOURCE is not in those packages yet. After the coordinator releases the PATCHes: bump
   `Doctrine.csproj`, `Showroom.csproj` and the `*UiProof`/`PrismTabProof` csprojs together (Doctrine is the NuGet pin,
   its owner's file; versions via `dotnet add package`), then rebuild and re-run every gate in B. Until then a
   published Doctrine battle runs on the unfixed race. Ask the coordinator which Tracer/EvalApp versions carry the fix.
3. **Editor review.** `editorial-owner` reviews the public text before it goes live: the Doctrine page copy
   (`Pages/DoctrinePage.razor` lede, "How it works", "About the general"), `scripts/finish-shells.mjs` meta line, and the
   web hand-off copy in `todo/doctrine-web-handoff.md` (tools.json line, games.json card, `games/doctrine.json`).
   Relay its questions to the user; apply its edits (Showroom text is mine, `web/` text is website-owner's).
4. **Web data pasted.** website-owner applies the three entries from `todo/doctrine-web-handoff.md` and rebuilds
   `site-next` (`node web/scripts/run.mjs --versions=committed`). A tool shell with no `tools.json` entry would be absent
   from the gallery; a `games.json` entry with no `tools.json` entry fails `data.mjs`.

## B. Pre-publish gates (all must be green on the final pins)

Last run 2026-10-19 on the CURRENT pins (Tracer 2.0.3): re-run after the PATCH bump.

```powershell
cd C:\Users\dongy\AboutUs
dotnet test Doctrine.Tests\Doctrine.Tests.csproj -c Release            # 352 passed, 0 failed (2026-10-19)
cd Showroom
dotnet build Showroom.csproj -c Release                                # 0 warnings, 0 errors (2026-10-19)
cd tools\DoctrineUiProof; dotnet run -c Release                        # ALL PASS (154 checks) (2026-10-19)
cd ..\PlenumProof;        dotnet run -c Release                        # ALL PASS (201 checks) (2026-10-19)
```

Build Showroom Release BEFORE `DoctrineUiProof`/`PlenumUiProof`/`PrismTabUiProof` (they load its output). Optional
same-batch proofs: `PlenumUiProof`, `PlenumDuelProof` (not `-- live`), `PrismTabProof`, `PrismTabUiProof`, `HelixProof`.

## C. Publish, in this order

User pauses Prism training first. Then:

```powershell
# 1. Showroom publish (AOT; shells, finish-shells.mjs, dist/, runtime copied to website-data\_framework)
cd C:\Users\dongy\AboutUs\Showroom
powershell -File scripts\publish-site.ps1

# 2. Optional shell sanity without a browser (exact scripts in Showroom CLAUDE.md "Verify a build")
node scripts\shell-check.mjs <dist> <website-data>
```

3. **website-data FIRST**: commit and push `C:\Users\dongy\website-data` (new hashed runtime files in `_framework/`).
   Old runtime files stay so the live site keeps booting during the gap.
4. **Then AboutUs**: commit and push `C:\Users\dongy\AboutUs` (Showroom `dist/`, the pasted `web/data` entries, the
   rebuilt `site-next/`). CI never builds Showroom, so `dist/` must come from step 1 in the SAME commit as the source change.
   Heed any "LF will be replaced by CRLF" warning under `dist/` or website-data (`.gitattributes` is load-bearing).
5. **A push is not a published site**: fetch the live URL `/tools/doctrine/` and `/games/doctrine/` and confirm the
   Pages deploy finished before moving on.
6. **Only after step 5 is live**: `powershell -File scripts\publish-site.ps1 -Prune` (deletes old runtime files in
   website-data), then push website-data again.

Coordinator commits and pushes (owners never commit). Prism checkpoint refresh is a separate data-only push and is NOT
part of this batch.

## D. After it is live (the user's part, I cannot do it)

Nothing here has been seen in a browser or on a phone. Try it: open `https://evaluatedapplications.github.io/tools/doctrine/`
(or locally `scripts\link-data.ps1` once, then `dotnet run` in `C:\Users\dongy\AboutUs\Showroom`, tool at `/doctrine`).
Look at: boot to the start screen, one full game of 3 rounds, the replay scrubber, Cancel mid-battle, army code
copy/paste, and the arena on a real phone. Re-measure ms/round and the runtime size with the post-fix build and only
then consider any figure on the site.
