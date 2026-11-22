<#
.SYNOPSIS
    Compiles and runs the icon rasterisation prototype.

.DESCRIPTION
    This is a throwaway harness, not part of the mod. It compiles the same C# 7.3 sources that
    the mod would carry (`IconGeometry.cs`, `IconRaster.cs`) against the game's own
    `VRage.Math.dll`, runs them, and writes one alpha mask per icon per pixel size into
    `tmp\icon-proto\out`. `preview.py` then turns those masks into contact sheets.

    The point is to answer, with pixels rather than a promise, whether a Font Awesome SVG can be
    drawn through the mod screen API at the editor's real sizes without a baked bitmap.
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319',
    [string] $Out
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proto = $PSScriptRoot

if (-not $Out) { $Out = Join-Path $repo 'tmp\icon-proto\bin' }
if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }

$sources = @(
    (Join-Path $proto 'IconGeometry.cs'),
    (Join-Path $proto 'IconRaster.cs'),
    (Join-Path $proto 'Program.cs')
)
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "Source not found: $s" } }

$gameRefs = @('VRage.dll', 'VRage.Math.dll', 'VRage.Library.dll', 'netstandard.dll')
$frameworkRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll')

$refArgs = @()
foreach ($r in $gameRefs) {
    $p = Join-Path $Bin $r
    if (-not (Test-Path $p)) { throw "Missing game assembly: $p" }
    $refArgs += "/r:`"$p`""
}
foreach ($r in $frameworkRefs) {
    $p = Join-Path $FrameworkDir $r
    if (-not (Test-Path $p)) { throw "Missing framework assembly: $p" }
    $refArgs += "/r:`"$p`""
}

$cscCandidates = @(
    'C:\Program Files\dotnet\sdk\9.0.321\Roslyn\bincore\csc.dll',
    'C:\Program Files\dotnet\sdk\8.0.308\Roslyn\bincore\csc.dll'
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'No Roslyn csc.dll found.' }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet host not found.' }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$outExe = Join-Path $Out 'IconProto.exe'
$logOut = Join-Path $Out 'csc.out.txt'
$logErr = Join-Path $Out 'csc.err.txt'
$rspFile = Join-Path $Out 'csc.rsp'

$rsp = @()
$rsp += '-nologo'
$rsp += '-target:exe'
$rsp += '-nostdlib+'
$rsp += '-langversion:7.3'
$rsp += '-unsafe-'
$rsp += '-warn:4'
$rsp += '-nowarn:1591'
$rsp += "-out:`"$outExe`""
$rsp += $refArgs
$rsp += ($sources | ForEach-Object { "`"$_`"" })
Set-Content -Path $rspFile -Value $rsp -Encoding utf8

foreach ($r in $gameRefs) { Copy-Item (Join-Path $Bin $r) $Out -Force }

$cliHome = Join-Path $Out 'clihome'
New-Item -ItemType Directory -Force -Path $cliHome | Out-Null
$childEnv = @{
    'DOTNET_CLI_HOME'                   = $cliHome
    'DOTNET_CLI_TELEMETRY_OPTOUT'       = '1'
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' = '1'
    'DOTNET_NOLOGO'                     = '1'
    'DOTNET_CLI_UI_LANGUAGE'            = 'en'
}

$proc = Start-Process -FilePath $dotnet -ArgumentList "exec `"$csc`" `"@$rspFile`"" -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $logOut -RedirectStandardError $logErr -Environment $childEnv

$combined = ((Get-Content $logOut -Raw) + "`n" + (Get-Content $logErr -Raw)).Trim()
$errors = @($combined -split "`r?`n" | Where-Object { $_ -match ':\s*error\s+CS\d+' })
if ($errors.Count -gt 0) {
    Write-Host "PROTO BUILD FAILED — $($errors.Count) error(s)" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

if (-not (Test-Path $outExe)) { Write-Host 'PROTO BUILD FAILED — no executable'; exit 1 }

# A child process whose stdio is captured through a pipe needs a named pipe, which a confined
# environment (the DSH sandbox) denies. Run it through a .cmd wrapper with file redirection
# instead — the same workaround offline-check.ps1 uses.
$runOut = Join-Path $Out 'run.out.txt'
$runErr = Join-Path $Out 'run.err.txt'
$runCode = Join-Path $Out 'run.code.txt'
$wrapperFile = Join-Path $Out 'run.cmd'
foreach ($stale in @($runCode, $runOut, $runErr)) { if (Test-Path $stale) { Remove-Item $stale -Force } }

$wrapperLines = @(
    '@echo off',
    ('"{0}" "{1}" "{2}" > "{3}" 2> "{4}"' -f $outExe, $repo, (Join-Path $repo 'tmp\icon-proto\out'), $runOut, $runErr),
    ('echo %ERRORLEVEL% > "{0}"' -f $runCode)
)
Set-Content -Path $wrapperFile -Value $wrapperLines -Encoding Oem

Start-Process -FilePath $wrapperFile -NoNewWindow -Wait

if (Test-Path $runOut) { Get-Content $runOut -Raw }
$err = if (Test-Path $runErr) { Get-Content $runErr -Raw } else { '' }
if ($err -and $err.Trim()) { Write-Host $err -ForegroundColor Yellow }

$codeText = if (Test-Path $runCode) { Get-Content $runCode -Raw } else { $null }
$code = -1
if (-not $codeText -or -not [int]::TryParse($codeText.Trim(), [ref]$code)) {
    Write-Host 'PROTO RUN INCONCLUSIVE — exit code unreadable' -ForegroundColor Red
    exit 1
}
exit $code
