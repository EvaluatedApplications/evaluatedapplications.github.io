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
    @{ Id = 'M1'; Name = 'an illegal order is enabled in the palette'; File = 'Pages\DoctrinePage.razor';
       Old = 'bool ok = legal.Allows(token);'; New = 'bool ok = true;'; Append = ''; Expect = 'the palette holds every order but Cull' },
    @{ Id = 'M2'; Name = 'the general is wired so that it never records an example'; File = 'Services\Doctrine\DoctrineRoot.cs';
       Old = '.WithGeneral(General))'; New = '.WithGeneral(new ForgetfulMutant(General)))'; Append = $forgetful; Expect = 'the general had something to suggest after seeing the visitor' },
    @{ Id = 'M3'; Name = 'the scrubber ignores the frames'' WorldFlags (the gate never falls)'; File = 'Services\Doctrine\ReplayView.cs';
       Old = 'public bool GateFallen(int tick) => (At(tick).WorldFlags & WorldFlag.GateFallen) != 0;'; New = 'public bool GateFallen(int tick) => false;'; Append = ''; Expect = 'on the frame the flag is set the gate' },
    @{ Id = 'M4'; Name = 'Accept reports the suggestion as overridden'; File = 'Pages\DoctrinePage.razor';
       Old = '_s!.SubmitChoice(ghost.Doctrine, false)'; New = '_s!.SubmitChoice(ghost.Doctrine, true)'; Append = ''; Expect = 'the accepted ghost is the doctrine that fought, recorded as NOT overridden' },
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
       Old = 'if (report.Refused.Count > 0)'; New = 'if (report.Refused.Count > 99)'; Append = ''; Expect = 'draft: what the army would not take' },
    @{ Id = 'M14'; Name = 'the Cull chip comes back (greyed) in the palette'; File = 'Pages\DoctrinePage.razor';
       Old = '@if (Tokens(ArmyOrderKind.Cull).Any(legal.Allows))'; New = '@if (true)'; Append = ''; Expect = 'the palette holds every order but Cull' },
    @{ Id = 'M15'; Name = 'the unlock schedule is ignored (every step opens in round one)'; File = 'Services\Doctrine\DoctrineProgress.cs';
       Old = 'return round >= last ? LastStep : Math.Min(round, LastStep - 1);'; New = 'return LastStep;'; Append = ''; Expect = 'unlock schedule: 8 rounds open a step a round' },
    @{ Id = 'M16'; Name = 'every kind of soldier is open in the draft'; File = 'Services\Doctrine\DoctrineProgress.cs';
       Old = 'public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length && KindStep[kind] <= step;'; New = 'public static bool KindOpen(int kind, int step) => (uint)kind < (uint)KindStep.Length;'; Append = ''; Expect = 'the draft lists exactly the open kinds' },
    @{ Id = 'M17'; Name = 'the starter advice is labelled as the general'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = 'public const string AdvicePrefix = "Game advice:";'; New = 'public const string AdvicePrefix = "Your general suggests:";'; Append = ''; Expect = 'starter advice is labelled as the game' },
    @{ Id = 'M18'; Name = 'a locked order is still in the tray and tappable'; File = 'Pages\DoctrinePage.razor';
       Old = 'var open = tokens.Where(t => DoctrineUnlocks.Allows(t, Level)).ToList();'; New = 'var open = tokens.ToList();'; Append = ''; Expect = 'the tray shows exactly the orders open at step' },
    @{ Id = 'M19'; Name = 'a suggestion with a locked order can be used'; File = 'Pages\DoctrinePage.razor';
       Old = 'if (!InDoctrine || !DoctrineUnlocks.AllowsAll(ghost.Doctrine, Level)) return;'; New = 'if (!InDoctrine) return;'; Append = ''; Expect = 'tapping the disabled Use button submits nothing' },
    @{ Id = 'M20'; Name = 'the recap quotes the wrong losses'; File = 'Services\Doctrine\DoctrineGuide.cs';
       Old = '$"You lost {lost} of {v.YourSoldiers} soldiers;'; New = '$"You lost {theirLost} of {v.YourSoldiers} soldiers;'; Append = ''; Expect = 'the recap''s numbers are the record' },
    @{ Id = 'M21'; Name = 'starting a game forgets what was unlocked'; File = 'Pages\DoctrinePage.razor';
       Old = '_s!.NewGame(seed, _rounds, null, out var why);'; New = '_p = new DoctrineProgress(); _s!.NewGame(seed, _rounds, null, out var why);'; Append = ''; Expect = 'draft: all eight kinds are listed' },
    @{ Id = 'M22'; Name = 'the dock loses the bottom safe-area inset'; File = 'Pages\DoctrinePage.razor.css';
       Old = 'padding: 0.5rem 0 max(0.5rem, env(safe-area-inset-bottom, 0px));'; New = 'padding: 0.5rem 0;'; Append = ''; Expect = 'the dock keeps the bottom safe-area inset' },
    @{ Id = 'M23'; Name = 'the sheet handle no longer takes a drag'; File = 'Pages\DoctrinePage.razor.css';
       Old = 'background: none; border: 0; color: var(--ink-soft); padding: 0; touch-action: none; }'; New = 'background: none; border: 0; color: var(--ink-soft); padding: 0; }'; Append = ''; Expect = 'the handles take a drag' },
    @{ Id = 'M24'; Name = 'the field becomes a tab'; File = 'Pages\DoctrinePage.razor';
       Old = '<section class="dc-field" aria-label="The field">'; New = '<section class="dc-field dc-tab" aria-label="The field">'; Append = ''; Expect = 'no tabs, no tab roles' },
    @{ Id = 'M25'; Name = 'the chronicle writes a break for a cohort that still stands (strength 1)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'if (Find(prev, b.Id) is not { } p || p.Strength <= 0 || b.Strength > 0) continue;'; New = 'if (Find(prev, b.Id) is not { } p || p.Strength <= 0 || b.Strength > 1) continue;'; Append = ''; Expect = 'chronicle (recorded battles): every line' },
    @{ Id = 'M26'; Name = 'the chronicle says your where it means the enemy (sides swapped)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'string Lab(ReplayBlob b) => (b.Side == Side.Player ? "your " : "the enemy ") + kindName(b.Key);'; New = 'string Lab(ReplayBlob b) => (b.Side == Side.Player ? "the enemy " : "your ") + kindName(b.Key);'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' },
    @{ Id = 'M27'; Name = 'the chronicle puts the gate line one tick early'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'Add(cur.Tick, ChronicleKind.Gate, "The gate has fallen.", new ChronicleCite(CiteKind.Gate, cur.Tick));'; New = 'Add(prev.Tick, ChronicleKind.Gate, "The gate has fallen.", new ChronicleCite(CiteKind.Gate, prev.Tick));'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' },
    @{ Id = 'M28'; Name = 'the Why turns the swing the wrong way (who gained the turning point)'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = 'int swing = enemyLost - youLost;'; New = 'int swing = youLost - enemyLost;'; Append = ''; Expect = 'chronicle (synthetic frames): the Why is hand-written too' },
    @{ Id = 'M29'; Name = 'first contact names the wrong side as not having struck'; File = 'Services\Doctrine\BattleChronicle.cs';
       Old = '{(youStruck ? "the enemy has" : "you have")} not struck yet'; New = '{(youStruck ? "you have" : "the enemy has")} not struck yet'; Append = ''; Expect = 'chronicle (synthetic frames): the log is exactly' }
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
