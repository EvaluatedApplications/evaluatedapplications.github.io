<#
.SYNOPSIS
  Rebuilds Showroom/dist (the committed publish output the Pages deploy copies) and the Blazor runtime in the website-data repo.
.DESCRIPTION
  Showroom/dist holds only what is small and ours: index.html, css, blazor.webassembly.*.js, data/forecaster-history.json.
  The runtime (the 30 MB AOT wasm, assemblies, ICU data: everything else under _framework) goes to <DataRepo>/_framework, where
  index.html's loadBootResource hook fetches it (window.EA_FRAMEWORK_BASE). The precompressed .br/.gz copies are not copied:
  GitHub Pages never serves them, so they are dead weight.
  ORDER OF PUSHING MATTERS: push website-data FIRST (new hashed runtime files), then the site. Old runtime files are left in place so the
  live site keeps booting during the gap; run again with -Prune after the site push is live to delete runtime files this build
  does not use.
.PARAMETER Prune
  Delete _framework files in the data repo that the fresh publish does not contain.
#>
param([string]$DataRepo = 'C:\Users\dongy\website-data', [switch]$Prune)
$ErrorActionPreference = 'Stop'
$show = Split-Path $PSScriptRoot -Parent
$tmp = Join-Path $env:TEMP ('showroom-pub-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
dotnet publish (Join-Path $show 'Showroom.csproj') -c Release -o $tmp
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$pub = Join-Path $tmp 'wwwroot'
if (Test-Path (Join-Path $pub 'website-data')) { throw 'publish output carries website-data (the Release exclusion in Showroom.csproj is not working)' }

# 1. site side: dist = published wwwroot minus the runtime
$dist = Join-Path $show 'dist'
if (Test-Path $dist) { Get-ChildItem $dist -Force | Remove-Item -Recurse -Force }
robocopy $pub $dist /E /XD _framework /NFL /NDL /NJH /NJS /NP | Out-Null
New-Item -ItemType Directory -Force (Join-Path $dist '_framework') | Out-Null
Get-ChildItem (Join-Path $pub '_framework') -File -Filter 'blazor.webassembly.*' | Copy-Item -Destination (Join-Path $dist '_framework')

# 2. data side: the runtime, plain files only
$fw = Join-Path $DataRepo '_framework'
New-Item -ItemType Directory -Force $fw | Out-Null
$fresh = Get-ChildItem (Join-Path $pub '_framework') -File | Where-Object { $_.Name -notlike 'blazor.webassembly.*' -and $_.Extension -notin '.br', '.gz' }
$fresh | Copy-Item -Destination $fw -Force
if ($Prune) {
    $keep = $fresh.Name
    Get-ChildItem $fw -File | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Force
}
Remove-Item -Recurse -Force $tmp

function Sz($p) { $m = Get-ChildItem $p -Recurse -File | Measure-Object Length -Sum; '{0} files, {1:N1} MB' -f $m.Count, ($m.Sum / 1MB) }
Write-Host "site dist:               $(Sz $dist)"
Write-Host "website-data/_framework: $(Sz $fw)"
Write-Host 'Next: commit + push website-data FIRST, then the site (AboutUs). Then re-run with -Prune to drop old runtime files.'
