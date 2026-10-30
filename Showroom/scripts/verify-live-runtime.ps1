# Checks the LIVE runtime against the boot manifest the live shell will use. Read-only (GET only).
# Reads the dotnet.<hash>.js named in the live /tools/<tool>/ import map, pulls the boot config embedded in it
# (between /*json-start*/ and /*json-end*/), then downloads every listed resource from /website-data/_framework/ on the
# live site and compares SHA-256 with the manifest (the two js modules carry no hash in the manifest: reported as "nohash").
# Usage: powershell -File verify-live-runtime.ps1 [-Tool prism] [-Site https://evaluatedapplications.github.io]
param([string]$Tool = 'prism', [string]$Site = 'https://evaluatedapplications.github.io')
Add-Type -AssemblyName System.Net.Http
$hd = New-Object System.Net.Http.HttpClientHandler
$hd.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip
$http = New-Object System.Net.Http.HttpClient($hd)
$http.Timeout = [TimeSpan]::FromMinutes(5)
$shell = $http.GetStringAsync("$Site/tools/$Tool/").Result
$loader = [regex]::Match($shell, '"\./_framework/dotnet\.js": "\./_framework/([^"]+)"').Groups[1].Value
"loader: $loader"
$js = $http.GetStringAsync("$Site/website-data/_framework/$loader").Result
$a = $js.IndexOf('/*json-start*/') + 14
$b = $js.IndexOf('/*json-end*/')
$cfg = $js.Substring($a, $b - $a) | ConvertFrom-Json
$sha = [Security.Cryptography.SHA256]::Create()
$bad = 0; $n = 0
foreach ($k in 'coreAssembly', 'assembly', 'jsModuleNative', 'jsModuleRuntime', 'wasmNative', 'icu') {
  foreach ($r in @($cfg.resources.$k)) {
    if (-not $r.name) { continue }
    $n++
    $resp = $http.GetAsync("$Site/website-data/_framework/$($r.name)", [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
    if ([int]$resp.StatusCode -ne 200) { $bad++; "$([int]$resp.StatusCode) $($r.name)"; continue }
    $st = $resp.Content.ReadAsStreamAsync().Result
    $h = 'sha256-' + [Convert]::ToBase64String($sha.ComputeHash($st))
    $st.Dispose(); $resp.Dispose()
    if (-not $r.hash) { "nohash $($r.name) $h" } elseif ($h -ne $r.hash) { $bad++; "HASH MISMATCH $($r.name)" }
  }
}
"checked $n resources, bad $bad"
