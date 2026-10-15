<#
.SYNOPSIS
  Pulls a bigger real historical AAPL hourly-candle bundle for The Forecaster from Yahoo Finance's
  public chart JSON endpoint (query1.finance.yahoo.com/v8/finance/chart) and writes it to
  Showroom/wwwroot/data/forecaster-history.json.

.WHY THIS IS A SCRIPT, NOT A BROWSER FETCH
  Verified live (see Showroom/todo/forecaster-live-data-research.md): this Yahoo endpoint sends NO
  Access-Control-Allow-Origin header at all, so it can only ever be called BUILD/SERVER-side (a
  PowerShell/Node/CI HTTP client, not a visitor's browser fetch()). This script IS that build-side
  pull. Run it manually whenever you want to refresh the bundle, or wire it into a scheduled GitHub
  Actions workflow (propose the YAML to the coordinator - .github/workflows/ is outside this repo's
  Showroom/-only boundary, don't add the workflow file from here).

.WHAT CHANGED vs the old bundle
  Old: wwwroot/data/forecaster-sample.json, 450 rows (about 3 months), close price ONLY.
  New: wwwroot/data/forecaster-history.json, about 2 years of real hourly AAPL candles (interval=1h
  is Yahoo's own practical ceiling for range - verified live, range=2y returns about 3500 rows, a
  further range=5y/max collapses to a coarser interval), full OHLC (not close-only - enables a real
  candlestick chart instead of a close-price line). Null rows (market-data gaps, e.g. trading halts)
  are dropped. Forecaster.razor's tokenization only ever used Close, so this is additive, not a
  breaking schema change to the training logic.

.USAGE
  powershell -File Showroom\scripts\fetch-forecaster-history.ps1
#>

$ErrorActionPreference = 'Stop'

$Symbol = 'AAPL'
$Uri = 'https://query1.finance.yahoo.com/v8/finance/chart/' + $Symbol + '?interval=1h&range=2y'
$OutFile = Join-Path $PSScriptRoot '..\wwwroot\data\forecaster-history.json'

Write-Host ('Fetching ' + $Symbol + ' 1h/2y candles from ' + $Uri + ' ...')
$resp = Invoke-WebRequest -Uri $Uri -Headers @{ 'User-Agent' = 'Mozilla/5.0' } -UseBasicParsing -TimeoutSec 30
$json = $resp.Content | ConvertFrom-Json
$result = $json.chart.result[0]
if (-not $result) { throw 'Yahoo chart response had no result[0] - check the symbol/response shape.' }

$ts = $result.timestamp
$q = $result.indicators.quote[0]
if (-not $ts -or -not $q) { throw 'Missing timestamp/quote arrays in Yahoo response.' }

$dq = [char]34
$rows = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $ts.Count; $i++) {
    $o = $q.open[$i]; $h = $q.high[$i]; $l = $q.low[$i]; $c = $q.close[$i]
    if ($null -eq $o -or $null -eq $h -or $null -eq $l -or $null -eq $c) { continue }  # drop gap rows
    $t = [long]$ts[$i]
    $oR = [math]::Round([double]$o, 4)
    $hR = [math]::Round([double]$h, 4)
    $lR = [math]::Round([double]$l, 4)
    $cR = [math]::Round([double]$c, 4)
    $row = '{' + $dq + 't' + $dq + ':' + $t + ',' + $dq + 'o' + $dq + ':' + $oR + ',' + $dq + 'h' + $dq + ':' + $hR + ',' + $dq + 'l' + $dq + ':' + $lR + ',' + $dq + 'c' + $dq + ':' + $cR + '}'
    $rows.Add($row)
}

if ($rows.Count -lt 450) { throw ('Only got ' + $rows.Count + ' clean rows - fewer than the OLD bundle (450); refusing to overwrite. Check the endpoint/response.') }

$outJson = '[' + ($rows -join ',') + ']'
[System.IO.File]::WriteAllText($OutFile, $outJson, [System.Text.UTF8Encoding]::new($false))

Write-Host ('Wrote ' + $rows.Count + ' candles (' + $outJson.Length + ' bytes) to ' + $OutFile)
$mult = [math]::Round($rows.Count / 450.0, 1)
Write-Host ('Old bundle was 450 rows -> new bundle is ' + $mult + 'x bigger.')
