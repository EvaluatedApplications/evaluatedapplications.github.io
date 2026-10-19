# Mutation proof for DoctrineUiProof: break the shipped source on purpose, build Showroom, run the proof, expect it to go RED for the right reason,
# restore. Usage (from any folder): powershell -File tools\DoctrineUiProof\mutants.ps1 [-Only M1,M3]
# Writes nothing outside the files it patches (each is restored in a finally block) and a log next to this script. Windows PowerShell 5.1.
param([string[]]$Only = @())
$ErrorActionPreference = 'Continue'   # a crashing proof writes to stderr; that must not end this script
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent        # ...\Showroom
$utf8 = New-Object System.Text.UTF8Encoding($false)
$forgetful = @'

sealed class ForgetfulMutant(IGeneral inner) : IGeneral
{
    public int ExamplesSeen => 0;
    public ValueTask<GeneralOutput> SuggestAsync(GeneralInput i, CancellationToken ct) => inner.SuggestAsync(i, ct);
    public ValueTask LearnAsync(GeneralExample e, CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask LearnOutcomeAsync(GeneralOutcome o, CancellationToken ct) => ValueTask.CompletedTask;
}
'@

$mutants = @(
    @{ Id = 'M2'; Name = 'the general is wired so that it never records an example'; File = 'Services\Doctrine\DoctrineRoot.cs';
       Old = '.WithGeneral(General))'; New = '.WithGeneral(new ForgetfulMutant(General)))'; Append = $forgetful; Expect = 'the general had something to suggest after seeing the visitor' },
    @{ Id = 'M3'; Name = 'the scrubber ignores the frames'' WorldFlags (the gate never falls)'; File = 'Services\Doctrine\ReplayView.cs';
       Old = 'public bool GateFallen(int tick) => (At(tick).WorldFlags & WorldFlag.GateFallen) != 0;'; New = 'public bool GateFallen(int tick) => false;'; Append = ''; Expect = 'on the frame the flag is set the gate' },
    @{ Id = 'M4'; Name = 'Accept reports the suggestion as overridden'; File = 'Pages\DoctrinePage.razor';
       Old = '_s!.SubmitChoice(ghost.Doctrine, false)'; New = '_s!.SubmitChoice(ghost.Doctrine, true)'; Append = ''; Expect = 'accept: Use it fights the general' },
    @{ Id = 'M5'; Name = 'a cancelled round does not put the army back'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = '_restart = _restart! with { Player = back };'; New = '_restart = _restart! with { };'; Append = ''; Expect = 'the army is back exactly as it stood at the start of the round' },
    @{ Id = 'M6'; Name = 'the end screen hands back the army from before the last round'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 's._closedCode = s._army!.ToCode();'; New = 's._closedCode = s._roundCode;'; Append = ''; Expect = 'it parses back to an army with the same read' },
    @{ Id = 'M7'; Name = 'the scrubber shows the frame after the one asked for'; File = 'Services\Doctrine\ReplayView.cs';
       Old = 'Replay.Frames[Math.Clamp(tick, 0, Last)]'; New = 'Replay.Frames[Math.Clamp(tick + 1, 0, Last)]'; Append = ''; Expect = 'the tick before the fall shows the gate standing' },
    @{ Id = 'M8'; Name = 'RoundClosedAsync is ignored (the page never parks on a closed round)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'public async ValueTask RoundClosedAsync(RoundRecord rec, ArmyReadModel after, CancellationToken ct)'; New = 'public ValueTask RoundClosedAsync(RoundRecord rec, ArmyReadModel after, CancellationToken ct) => ValueTask.CompletedTask; public async ValueTask RoundClosedUnused(RoundRecord rec, ArmyReadModel after, CancellationToken ct)'; Append = ''; Expect = 'the closed round arrives through RoundClosedAsync' },
    @{ Id = 'M9'; Name = 'RoundRecord.Final is ignored (the last round does not know it is the last)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'mine, theirs, rec.Final);'; New = 'mine, theirs, false);'; Append = ''; Expect = 'final: the pipeline marks only the last round Final' },
    @{ Id = 'M10'; Name = 'the per-slot glow ignores GeneralOutput.SlotConfidence'; File = 'Pages\DoctrinePage.razor';
       Old = 'style="--glow:@Glow(sr[sl])"'; New = 'style="--glow:@Glow(ghost.ConfidencePermille)"'; Append = ''; Expect = 'match record (per-slot): each order' },
    @{ Id = 'M11'; Name = 'RoundRecord.Orders is never shown (skipped orders go unsaid)'; File = 'Pages\DoctrinePage.razor';
       Old = 'rec.Orders is { Skipped: > 0 } ord'; New = 'rec.Orders is { Skipped: > 999 } ord'; Append = ''; Expect = 'orders: the watch screen says it in plain words' },
    @{ Id = 'M12'; Name = 'the arena layout from DoctrineContext.LayoutOf is not used'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 's._game!.Context.LayoutOf(rec.ArenaId)'; New = 'null'; Append = ''; Expect = 'watch: the arena is drawn from the layout' },
    @{ Id = 'M13'; Name = 'DraftedAsync drops the army''s verdict'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'if (report.Refused.Count > 0)'; New = 'if (report.Refused.Count > 99)'; Append = ''; Expect = 'refusals (draft)' },
    @{ Id = 'M15'; Name = 'the unlock schedule is ignored (every step opens in round one)'; File = 'Services\Doctrine\DoctrineProgress.cs';
       Old = 'return round >= last ? LastStep : Math.Min(round, LastStep - 1);'; New = 'return LastStep;'; Append = ''; Expect = 'unlock schedule: 8 rounds open a step a round' },
    @{ Id = 'M16'; Name = 'every kind of soldier is open in the draft'; File = 'Services\Doctrine\DoctrineProgress.cs';
       Old = 'public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length && KindStep[kind] <= step;'; New = 'public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length;'; Append = ''; Expect = 'the muster offers only kinds the step has opened' },
    @{ Id = 'M17'; Name = 'the starter advice is labelled as the general'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = 'public const string AdvicePrefix = "Game advice:";'; New = 'public const string AdvicePrefix = "Your general suggests:";'; Append = ''; Expect = 'starter advice is labelled as the game' },
    @{ Id = 'M20'; Name = 'the recap quotes the wrong losses'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = '$"You lost {lost} of {v.YourSoldiers} soldiers;'; New = '$"You lost {theirLost} of {v.YourSoldiers} soldiers;'; Append = ''; Expect = 'the recap''s numbers are the record' },
    @{ Id = 'M21'; Name = 'starting a game forgets what was unlocked'; File = 'Pages\DoctrinePage.razor';
       Old = '_s!.NewGame(seed, _rounds, null, out var why);'; New = '_p = new DoctrineProgress(); _s!.NewGame(seed, _rounds, null, out var why);'; Append = ''; Expect = 'unlock: a restarted game keeps what was unlocked' },
    @{ Id = 'M22'; Name = 'the dock loses the bottom safe-area inset'; File = 'Pages\DoctrinePage.razor.css';
       Old = 'padding: 0.5rem 0 max(0.5rem, env(safe-area-inset-bottom, 0px));'; New = 'padding: 0.5rem 0;'; Append = ''; Expect = 'the dock keeps the bottom safe-area inset' },
    @{ Id = 'M23'; Name = 'the sheet handle no longer takes a drag'; File = 'Pages\DoctrinePage.razor.css';
       Old = 'background: none; border: 0; color: var(--ink-soft); padding: 0; touch-action: none; }'; New = 'background: none; border: 0; color: var(--ink-soft); padding: 0; }'; Append = ''; Expect = 'the handles take a drag' },
    @{ Id = 'M24'; Name = 'the field becomes a tab'; File = 'Pages\DoctrinePage.razor';
       Old = '<section class="dc-field" aria-label="The field">'; New = '<section class="dc-field dc-tab" aria-label="The field">'; Append = ''; Expect = 'no tabs, no tab roles' },
    @{ Id = 'M25'; Name = 'the chronicle writes a break for a cohort that still stands (strength 1)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'if (Find(prev, b.Id) is not { } p || p.Strength <= 0 || b.Strength > 0) continue;'; New = 'if (Find(prev, b.Id) is not { } p || p.Strength <= 0 || b.Strength > 1) continue;'; Append = ''; Expect = 'chronicle (recorded battles): every line' },
    @{ Id = 'M30'; Name = 'a banner description with a TYPED number (the cap) in place of the rule''s'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = '$"Cohorts with {TraitMark[o.A]} fight harder this round, up to {cap}%."'; New = '$"Cohorts with {TraitMark[o.A]} fight harder this round, up to 60%."'; Append = ''; Expect = 'wording (numbers): no figure is typed' },
    @{ Id = 'M31'; Name = 'the drill sentence states a power per year that drifts from the rule'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = '$"Drill the host: every cohort gains a year of seasoning, and each year adds {Roster.VetStepPct}% power.'; New = '$"Drill the host: every cohort gains a year of seasoning, and each year adds {Roster.VetStepPct + 1}% power.'; Append = ''; Expect = 'age: a year adds the power the sentence states' },
    @{ Id = 'M32'; Name = 'an OLD order name is left in the chronicle hint'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'order to {DoctrineWords.FieldOrders[(int)FieldOrderKind.Flank].ToLowerInvariant()} on the'; New = 'order to Flank on the'; Append = ''; Expect = 'chronicle (synthetic frames): the Why is hand-written too' },
    @{ Id = 'M33'; Name = 'a technical term in the order tray (Reforge titled Relabel a trait)'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = 'ArmyOrderKind.Flip => "Reforge",'; New = 'ArmyOrderKind.Flip => "Relabel a trait",'; Append = ''; Expect = 'wording (play UI): no ''relabel'' term' },
    @{ Id = 'M34'; Name = 'the trigger rule loses the slot-order sentence'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = '=> $"Orders before it open the battle; orders after it take over once your host'; New = '=> $"Orders after it take over once your host'; Append = ''; Expect = 'trigger: orders before the trigger open the battle' },
    @{ Id = 'M35'; Name = 'the front-line preview reads the middle line after the order'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = 'y = Describe(a, 0, nameOf);'; New = 'y = Describe(a, 1, nameOf);'; Append = ''; Expect = 'preview: ''Your front line goes from X to Y''' },
    @{ Id = 'M37'; Name = 'the Reforge card shows the cost of one soldier, not of the host'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = 'sum += (long)c.Count * Roster.RelabelCost('; New = 'sum += (long)1 * Roster.RelabelCost('; Append = ''; Expect = 'reforge: the cost the card shows' },
    @{ Id = 'M38'; Name = 'the technical paragraph is left open on the page'; File = 'Pages\DoctrinePage.razor';
       Old = '<details class="dc-about dc-built">'; New = '<details class="dc-about dc-built" open>'; Append = ''; Expect = 'wording (play UI): the technical paragraph stays' },
    @{ Id = 'M39'; Name = 'the Pleroma store is named in the tray hint'; File = 'Pages\DoctrinePage.razor';
       Old = 'These orders are on the table this round.'; New = 'These orders are on the table in the Pleroma store this round.'; Append = ''; Expect = 'wording (play UI): no ''Pleroma'' term' },
    @{ Id = 'M26'; Name = 'the chronicle says your where it means the enemy (sides swapped)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'string Lab(ReplayBlob b) => (b.Side == Side.Player ? "your " : "the enemy ") + kindName(b.Key);'; New = 'string Lab(ReplayBlob b) => (b.Side == Side.Player ? "the enemy " : "your ") + kindName(b.Key);'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' },
    @{ Id = 'M27'; Name = 'the chronicle puts the gate line one tick early'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'Add(cur.Tick, ChronicleKind.Gate, "The gate has fallen.", new ChronicleCite(CiteKind.Gate, cur.Tick));'; New = 'Add(prev.Tick, ChronicleKind.Gate, "The gate has fallen.", new ChronicleCite(CiteKind.Gate, prev.Tick));'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' },
    @{ Id = 'M28'; Name = 'the Why turns the swing the wrong way (who gained the turning point)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'int swing = enemyLost - youLost;'; New = 'int swing = youLost - enemyLost;'; Append = ''; Expect = 'chronicle (synthetic frames): the Why is hand-written too' },
    @{ Id = 'M29'; Name = 'first contact names the wrong side as not having struck'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = '{(youStruck ? "the enemy has" : "you have")} not struck yet'; New = '{(youStruck ? "you have" : "the enemy has")} not struck yet'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' },
    @{ Id = 'M36'; Name = 'the page switches the game''s offers off again (the old free choice)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'return new DoctrineDomain { MaxRounds = rounds, Offers = offers };'; New = 'return new DoctrineDomain { MaxRounds = rounds, Offers = OfferRules.Off };'; Append = ''; Expect = 'muster: the cards are exactly the session' },
    @{ Id = 'N1'; Name = 'a card for an order that is NOT on the table (every legal order is a card) is tappable'; File = 'Pages\DoctrinePage.razor';
       Old = '@foreach (var tk in offer.Tokens)'; New = '@foreach (var tk in Enumerable.Range(1, DoctrineVocabulary.Size - 1).Where(legal.Allows))'; Append = ''; Expect = 'council: the cards are exactly the offer the game drew' },
    @{ Id = 'N2'; Name = 'placing an order does not check that it is on the table'; File = 'Pages\DoctrinePage.razor';
       Old = 'if (!offer.Allows(token) || !a.Legal.Allows(token) || !DoctrineUnlocks.Allows(token, Level)) return;'; New = 'if (!a.Legal.Allows(token) || !DoctrineUnlocks.Allows(token, Level)) return;'; Append = ''; Expect = 'council: a legal order that is NOT on the table' },
    @{ Id = 'N3'; Name = 'a fifth order overwrites the last slot (more than four allowed to be picked)'; File = 'Pages\DoctrinePage.razor';
       Old = 'for (int i = 0; i < SlotCount; i++) if (_slots[i] == 0) { _slots[i] = token; _lastAdded = token; _advice = ""; _formNote = ""; return; }'; New = 'for (int i = 0; i < SlotCount; i++) if (_slots[i] == 0) { _slots[i] = token; _lastAdded = token; _advice = ""; _formNote = ""; return; } _slots[SlotCount - 1] = token;'; Append = ''; Expect = 'picks: up to four orders go in the four slots' },
    @{ Id = 'N4'; Name = 'a reroll is free (the rules give it no price)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'return new DoctrineDomain { MaxRounds = rounds, Offers = offers };'; New = 'return new DoctrineDomain { MaxRounds = rounds, Offers = offers with { RerollCost = 0 } };'; Append = ''; Expect = 'reroll: paying draws the offer again' },
    @{ Id = 'N5'; Name = 'the reroll limit is gone (nine rerolls)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'return new DoctrineDomain { MaxRounds = rounds, Offers = offers };'; New = 'return new DoctrineDomain { MaxRounds = rounds, Offers = offers with { MaxRerolls = 9 } };'; Append = ''; Expect = 'reroll: the limit holds' },
    @{ Id = 'N6'; Name = 'the unlock ladder''s kind pool is ignored (every kind in round one)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'KindPool = (round, kind) => DoctrineUnlocks.KindOpen(kind, stepOf(round)),'; New = 'KindPool = null,'; Append = ''; Expect = 'its step opens and its kind pool the kinds it opens' },
    @{ Id = 'N7'; Name = 'the unlock ladder''s order pool is ignored (every order in round one)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'OrderPool = (round, token) => DoctrineUnlocks.Allows(token, stepOf(round)),'; New = 'OrderPool = null,'; Append = ''; Expect = 'its step opens and its kind pool the kinds it opens' },
    @{ Id = 'N8'; Name = 'the pool is frozen at round one (it never widens)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'KindPool = (round, kind) => DoctrineUnlocks.KindOpen(kind, stepOf(round)),'; New = 'KindPool = (round, kind) => DoctrineUnlocks.KindOpen(kind, stepOf(0)),'; Append = ''; Expect = 'its step opens and its kind pool the kinds it opens' },
    @{ Id = 'N9'; Name = 'the formation previews turn the host the wrong way (one turn shows two, two shows one)'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'int token = turns == 0 ? 0 : DoctrineVocabulary.Army(ArmyOrderKind.Rotate, turns);'; New = 'int token = turns == 0 ? 0 : DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 3 - turns);'; Append = ''; Expect = 'formations: the three formation cards' },
    @{ Id = 'N10'; Name = 'choosing a formation puts the wrong turn in the slot'; File = 'Pages\DoctrinePage.razor';
       Old = 'int token = turns == 0 ? 0 : DoctrineVocabulary.Army(ArmyOrderKind.Rotate, turns);'; New = 'int token = turns == 0 ? 0 : DoctrineVocabulary.Army(ArmyOrderKind.Rotate, 3 - turns);'; Append = ''; Expect = 'formations: choosing one turn puts Reform the ranks' },
    @{ Id = 'N11'; Name = 'a line''s order is shown against the wrong line in the formations'; File = 'Pages\DoctrinePage.razor';
       Old = 'foreach (var f in DoctrineVocabulary.FieldOrders(set)) if (f.Group == slot) return f;'; New = 'foreach (var f in DoctrineVocabulary.FieldOrders(set)) if (f.Group == (slot + 1) % 3) return f;'; Append = ''; Expect = 'formations: an order for a line placed in a slot shows' },
    @{ Id = 'N12'; Name = 'the formation chosen is counted wrongly (every turn counts one)'; File = 'Pages\DoctrinePage.razor';
       Old = 'if (o.Family == OrderFamily.Army && (ArmyOrderKind)o.Kind == ArmyOrderKind.Rotate) turns += o.A;'; New = 'if (o.Family == OrderFamily.Army && (ArmyOrderKind)o.Kind == ArmyOrderKind.Rotate) turns += 1;'; Append = ''; Expect = 'formations: choosing two turns replaces' },
    @{ Id = 'N13'; Name = 'the later arrow moves a slot the wrong way'; File = 'Pages\DoctrinePage.razor';
       Old = '@onclick="() => MoveSlot(slot, slot + 1)"'; New = '@onclick="() => MoveSlot(slot, slot - 1)"'; Append = ''; Expect = 'arrow of slot 1 swaps it with slot 2' },
    @{ Id = 'N14'; Name = 'dragging a slot onto another moves nothing'; File = 'Pages\DoctrinePage.razor';
       Old = 'private void DropOnSlot(int slot) { if (_dragFromSlot >= 0) MoveSlot(_dragFromSlot, slot); _dragFromSlot = -1; }'; New = 'private void DropOnSlot(int slot) { _dragFromSlot = -1; }'; Append = ''; Expect = 'order: dragging slot 1 onto slot 4' },
    @{ Id = 'N15'; Name = 'the general''s pick is not marked on the cards'; File = 'Pages\DoctrinePage.razor';
       Old = '@(gen ? "gen" : "")'; New = '@("")'; Append = ''; Expect = 'general: the cards it would take are marked' },
    @{ Id = 'N16'; Name = 'every card is marked as the general''s pick'; File = 'Pages\DoctrinePage.razor';
       Old = 'bool gen = picked.Tokens.Contains(token);'; New = 'bool gen = picked.Used > 0;'; Append = ''; Expect = 'general: the cards it would take are marked' },
    @{ Id = 'N17'; Name = 'Mine leaves the marks on'; File = 'Pages\DoctrinePage.razor';
       Old = 'private void IgnoreGhost() { if (!Choosing) return; _ghostHidden = true;'; New = 'private void IgnoreGhost() { if (!Choosing) return; _ghostHidden = false;'; Append = ''; Expect = 'general: Mine clears the marks' },
    @{ Id = 'N18'; Name = 'the draft total adds the soldiers, not the gold'; File = 'Pages\DoctrinePage.razor';
       Old = 'private int Cost() => Packs.Where((p, i) => i < _sel.Length && _sel[i]).Sum(p => p.Cost);'; New = 'private int Cost() => Packs.Where((p, i) => i < _sel.Length && _sel[i]).Sum(p => p.Count);'; Append = ''; Expect = 'muster: tapping buys a company whole' },
    @{ Id = 'N19'; Name = 'a company the purse cannot pay for can be bought'; File = 'Pages\DoctrinePage.razor';
       Old = 'if (!pack.Affordable || pack.Cost > _s.Draft.Army.Gold - Cost()) return;'; New = 'if (!pack.Affordable) return;'; Append = ''; Expect = 'muster: tapping a company that cannot be paid buys nothing' },
    @{ Id = 'N20'; Name = 'a company is shown with the wrong count'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = 'public static string PackName(UnitPack p) => $"{p.Count} {KindName(p.Kind)}";'; New = 'public static string PackName(UnitPack p) => $"{p.Count + 1} {KindName(p.Kind)}";'; Append = ''; Expect = 'muster: the cards are exactly the session' },
    @{ Id = 'N21'; Name = 'the line a company would join is read one line off'; File = 'Pages\DoctrinePage.razor';
       Old = 'rank[i] = c.Slot;'; New = 'rank[i] = (c.Slot + 1) % 3;'; Append = ''; Expect = 'muster: every company names the line it would join' },
    @{ Id = 'N22'; Name = 'the watch screen never says what the game refused outside the offer'; File = 'Pages\DoctrinePage.razor';
       Old = '@if (rec.OfferRefused > 0)'; New = '@if (rec.OfferRefused > 999)'; Append = ''; Expect = 'refusals: the watch screen says plainly' },
    @{ Id = 'N23'; Name = 'the not-on-offer refusal loses its plain words'; File = 'Services\Doctrine\DoctrineWords.cs';
       Old = '"not on offer" => "That company is not on offer, or it was already bought this round.",'; New = '"not on offer" => reason,'; Append = ''; Expect = 'refusals (draft)' },
    @{ Id = 'N24'; Name = 'the question about a reroll is never asked'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'if (!offer.Active || offer.RerollsLeft <= 0 || army.Gold < offer.RerollCost) return false;'; New = 'return false;'; Append = ''; Expect = 'ask: after the draft the game asks whether' },
    @{ Id = 'N25'; Name = 'Keep these pays for a reroll'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = '_rerollTcs.TrySetResult(false);'; New = '_rerollTcs.TrySetResult(true);'; Append = ''; Expect = 'council: the offer is the game' },
    @{ Id = 'N26'; Name = 'the reroll button shows the purse as the price'; File = 'Pages\DoctrinePage.razor';
       Old = 'Reroll for @ask.Offer.RerollCost gold (@ask.Offer.RerollsLeft left)'; New = 'Reroll for @ask.Army.Gold gold (@ask.Offer.RerollsLeft left)'; Append = ''; Expect = 'ask: after the draft the game asks whether' },
    @{ Id = 'N27'; Name = 'the starter advice buys companies the purse cannot pay'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = 'if (taken[i] || !p.Affordable || p.Cost > gold) continue;'; New = 'if (taken[i] || !p.Affordable) continue;'; Append = ''; Expect = 'starter advice is labelled as the game' },
    @{ Id = 'N28'; Name = 'the orders advice ignores the council (takes orders that are not on the table)'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = 'Where(t => t != 0 && offer.Allows(t) && DoctrineUnlocks.Allows(t, step) && army.Legal.Allows(t))'; New = 'Where(t => t != 0 && DoctrineUnlocks.Allows(t, step) && army.Legal.Allows(t))'; Append = ''; Expect = 'starter advice is labelled as the game' }
)

$log = Join-Path $PSScriptRoot 'mutants.log'
"Doctrine UI mutation proof, $(Get-Date -Format s)" | Out-File $log -Encoding utf8
function BuildShowroom { Push-Location $root; try { $o = dotnet build Showroom.csproj -c Release 2>&1 | Out-String; if ($o -notmatch 'Build succeeded') { throw "Showroom did not build:`n$o" } } finally { Pop-Location } }

foreach ($m in $mutants) {
    if ($Only.Count -gt 0 -and $Only -notcontains $m.Id) { continue }
    $path = Join-Path $root $m.File
    $orig = [IO.File]::ReadAllText($path)
    if (($orig.Split(@($m.Old), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "$($m.Id): the text to break is not in $($m.File) exactly once" }
    try {
        [IO.File]::WriteAllText($path, $orig.Replace($m.Old, $m.New) + $m.Append, $utf8)
        BuildShowroom
        Push-Location (Join-Path $root 'tools\DoctrineUiProof')
        try { $out = dotnet run -c Release 2>&1 | Out-String } finally { Pop-Location }
        $out | Out-File (Join-Path $PSScriptRoot ("mutants-" + $m.Id + ".out.txt")) -Encoding utf8
        $fails = @($out -split "`r?`n" | Where-Object { $_ -match '^FAIL' })
        $caught = $fails | Where-Object { $_ -like "*$($m.Expect)*" }
        $verdict = if ($caught) { 'CAUGHT' } elseif ($fails.Count -gt 0) { 'RED, BUT NOT FOR THE EXPECTED CHECK' } elseif ($out -match 'ALL PASS') { 'SURVIVED' } else { 'CRASHED (no FAIL line; see the exception)' }
        if ($verdict -like 'CRASHED*') { ($out -split "`r?`n" | Where-Object { $_ -match 'Unhandled|Exception' } | Select-Object -First 3) | ForEach-Object { Write-Host "    $_" } }
        $line = "$($m.Id) [$verdict] $($m.Name): $($fails.Count) failing check(s)"
        Write-Host $line; $line | Out-File $log -Append -Encoding utf8
        foreach ($f in $fails | Select-Object -First 6) { Write-Host "    $f"; "    $f" | Out-File $log -Append -Encoding utf8 }
    } finally {
        [IO.File]::WriteAllText($path, $orig, $utf8)
    }
}
BuildShowroom
Write-Host 'restored and rebuilt'
