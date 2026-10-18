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
       Old = 'bool ok = legal.Allows(token);'; New = 'bool ok = true;'; Append = ''; Expect = 'the palette holds all 53 orders and each is enabled exactly when' },
    @{ Id = 'M2'; Name = 'the general is wired so that it never records an example'; File = 'Services\Doctrine\DoctrineRoot.cs';
       Old = '.WithGeneral(General))'; New = '.WithGeneral(new ForgetfulMutant(General)))'; Append = $forgetful; Expect = 'the general had something to suggest after seeing the visitor' },
    @{ Id = 'M3'; Name = 'the scrubber ignores the frames'' WorldFlags (the gate never falls)'; File = 'Services\Doctrine\ReplayView.cs';
       Old = 'public bool GateFallen(int tick) => (At(tick).WorldFlags & WorldFlag.GateFallen) != 0;'; New = 'public bool GateFallen(int tick) => false;'; Append = ''; Expect = 'on the frame the flag is set the gate' },
    @{ Id = 'M4'; Name = 'Accept reports the suggestion as overridden'; File = 'Pages\DoctrinePage.razor';
       Old = '_s!.SubmitChoice(ghost.Doctrine, false)'; New = '_s!.SubmitChoice(ghost.Doctrine, true)'; Append = ''; Expect = 'the accepted ghost is the doctrine that fought, recorded as NOT overridden' },
    @{ Id = 'M5'; Name = 'a cancelled round does not put the army back'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 's = s with { Player = back };'; New = 's = s with { };'; Append = ''; Expect = 'the army is back exactly as it stood at the start of the round' },
    @{ Id = 'M6'; Name = 'the end screen hands back the army from before the last round'; File = 'Services\Doctrine\DoctrineSession.cs';
       Old = 'if (!carryOn || last) { Finish(s.Player.ToCode()); return; }'; New = 'if (!carryOn || last) { Finish(_startCode); return; }'; Append = ''; Expect = 'it parses back to an army with the same read' },
    @{ Id = 'M7'; Name = 'the scrubber shows the frame after the one asked for'; File = 'Services\Doctrine\ReplayView.cs';
       Old = 'Replay.Frames[Math.Clamp(tick, 0, Last)]'; New = 'Replay.Frames[Math.Clamp(tick + 1, 0, Last)]'; Append = ''; Expect = 'the tick before the fall shows the gate standing' }
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
