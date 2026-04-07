param(
  [string]$BaseUrl = 'http://localhost:5041'
)

$ErrorActionPreference = 'Continue'

function Write-Section([string]$title) {
  Write-Host ''
  Write-Host ('=' * 80)
  Write-Host $title
  Write-Host ('=' * 80)
}

function Try-Invoke([string]$url) {
  try {
    $res = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 20
    $contentType = ($res.Headers['Content-Type'] | Select-Object -First 1)
    $preview = ''
    if ($null -ne $res.Content) {
      $preview = [string]$res.Content
      if ($preview.Length -gt 300) { $preview = $preview.Substring(0, 300) + ' ...' }
    }

    [pscustomobject]@{
      Url = $url
      Ok = $true
      StatusCode = $res.StatusCode
      ContentType = $contentType
      Length = ($res.Content | Measure-Object -Character).Characters
      Preview = $preview
    }
  }
  catch {
    [pscustomobject]@{
      Url = $url
      Ok = $false
      StatusCode = $null
      ContentType = $null
      Length = $null
      Preview = $_.Exception.Message
    }
  }
}

function Print-Result($r) {
  if ($r.Ok) {
    Write-Host ("OK  {0}  {1}  len={2}  {3}" -f $r.StatusCode, $r.ContentType, $r.Length, $r.Url)
  } else {
    Write-Host ("ERR              {0}" -f $r.Url) -ForegroundColor Yellow
    Write-Host ("  {0}" -f $r.Preview) -ForegroundColor Yellow
  }
}

Write-Section "1) Process/Port check"
try {
  $port = ([uri]$BaseUrl).Port
} catch {
  $port = 5041
}

$netstat = (netstat -ano | Select-String (":$port\s"))
if ($netstat) {
  Write-Host "Listening lines:" 
  $netstat | ForEach-Object { Write-Host "  $($_.Line)" }

  $pids = @()
  foreach ($m in $netstat) {
    $parts = ($m.Line -split '\s+' | Where-Object { $_ -ne '' })
    if ($parts.Length -gt 0) {
      $procId = $parts[-1]
      if ($procId -match '^\d+$') { $pids += [int]$procId }
    }
  }
  $pids = $pids | Sort-Object -Unique

  if ($pids.Count -gt 0) {
    Write-Host "\nOwning processes:" 
    foreach ($procId in $pids) {
      try {
        $p = Get-Process -Id $procId -ErrorAction Stop
        $path = ''
        try { $path = $p.Path } catch { }
        Write-Host ("  PID={0} Name={1} Path={2}" -f $p.Id, $p.ProcessName, $path)
      } catch {
        Write-Host ("  PID={0} (process not found)" -f $procId)
      }
    }
  }
} else {
  Write-Host "No LISTENING entry found for port $port" -ForegroundColor Yellow
}

Write-Section "2) HTTP health checks"
$r1 = Try-Invoke "$BaseUrl/"
$r2 = Try-Invoke "$BaseUrl/api/ghn/provinces"
$r3 = Try-Invoke "$BaseUrl/api/ghn/districts?provinceId=202"
$r4 = Try-Invoke "$BaseUrl/api/ghn/wards?districtId=1450"
Print-Result $r1
Print-Result $r2
Print-Result $r3
Print-Result $r4

Write-Section "3) HTML sanity (server-side)"
$r5 = Try-Invoke "$BaseUrl/doi-tac"
Print-Result $r5
if ($r5.Ok) {
  $fullHtml = ''
  try {
    $fullHtml = (Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/doi-tac" -TimeoutSec 20).Content
  } catch {
    $fullHtml = ''
  }

  Write-Host "\nContains partner select IDs?" 
  $needles = @('partner_province','partner_district','partner_ward','/api/ghn/provinces')
  foreach ($n in $needles) {
    $has = $false
    try {
      $has = $fullHtml -like "*$n*"
    } catch { }
    $flag = 'NO'
    if ($has) { $flag = 'YES' }
    Write-Host ("  {0}: {1}" -f $n, $flag)
  }

  Write-Host "\nContains client-side dependencies?" 
  $deps = @(
    'jquery',
    'nice-select',
    'niceSelect',
    'assets/js/main.js',
    'assets/js/plugins',
    'assets/js/vendor'
  )
  foreach ($d in $deps) {
    $has = $false
    try {
      $has = $fullHtml -match [regex]::Escape($d)
    } catch { }
    $flag = 'NO'
    if ($has) { $flag = 'YES' }
    Write-Host ("  {0}: {1}" -f $d, $flag)
  }
}

Write-Section "4) Notes"
Write-Host "- Script only validates server-side endpoints + HTML." 
Write-Host "- If /api/ghn/* returns OK but dropdown still empty: likely client-side JS/niceSelect timing." 
Write-Host "- For client-side errors, open browser DevTools Console on /doi-tac?ghnDebug=1." 
