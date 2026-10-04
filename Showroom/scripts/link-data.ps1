# Local dev: make `dotnet run` serve the website-data repo at /tools/website-data/ (a directory junction, no copy).
# Production reads /website-data/ (the repo's own GitHub Pages site, same origin); wwwroot/appsettings.Development.json
# points dev at this junction. Run once per clone. The junction is gitignored and is NOT part of a Release publish (see Showroom.csproj).
param([string]$DataRepo = 'C:\Users\dongy\website-data')
$link = Join-Path $PSScriptRoot '..\wwwroot\website-data'
if (-not (Test-Path $DataRepo)) { throw "website-data repo not found at $DataRepo (clone EvaluatedApplications/website-data there, or pass -DataRepo)" }
if (Test-Path $link) { Write-Host "already linked: $link"; return }
New-Item -ItemType Junction -Path $link -Target $DataRepo | Out-Null
Write-Host "linked $link -> $DataRepo"