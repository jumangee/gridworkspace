<#
.SYNOPSIS
    Runs the offline checks for the pure-data and pure-geometry parts of the editor.

.DESCRIPTION
    `verify-compile.ps1` proves the code is callable. It does not prove that the port slots
    come out contiguous, that the input stack clears the output stack, that a dot sits where
    its bar's edge is, or that a wire route arrives at its target moving right. All of those
    are decidable without the game, and all of them are invisible in a compile.

    This gate compiles `tests/OfflineChecks.cs` together with the model layer and the two
    geometry types, links nothing from `Sandbox.*`, and runs the result. A failure is a
    sentence naming the property that broke.

    What it deliberately cannot check: anything drawn, anything read from the game, the three
    pointer modes, and the menu. Those need a person in the game — see the acceptance list in
    `docs/ROADMAP.md`.

.PARAMETER Bin
    Game Bin64 folder holding the math assemblies.

.PARAMETER Out
    Where to write the throwaway assembly. Defaults to a temp folder.

.EXAMPLE
    pwsh -File tools/offline-check.ps1
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319',
    [string] $Out
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repo 'src\VGS.Mod\Data\Scripts\VGS'
$checks = Join-Path $repo 'tests\OfflineChecks.cs'

if (-not $Out) { $Out = Join-Path ([System.IO.Path]::GetTempPath()) ('vgs-offline-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }
if (-not (Test-Path $checks)) { throw "Offline checks not found: $checks" }

# Only the sources that have no game references. Adding VgsEditorScreen here would drag in
# Sandbox.ModAPI and defeat the point of the gate: it must run on a machine that has the game
# installed but does not start it.
$sources = @(
    (Join-Path $sourceRoot 'Core\VgsConstants.cs'),
    (Join-Path $sourceRoot 'Model\Node.cs'),
    (Join-Path $sourceRoot 'Model\NodeStateFormat.cs'),
    (Join-Path $sourceRoot 'Model\GridGraph.cs'),
    (Join-Path $sourceRoot 'Model\BlockPortCatalog.cs'),
    (Join-Path $sourceRoot 'Model\ToolbarPlanner.cs'),
    (Join-Path $sourceRoot 'Ui\PortGeometry.cs'),
    (Join-Path $sourceRoot 'Ui\EdgeRouting.cs'),
    $checks
)

foreach ($s in $sources) {
    if (-not (Test-Path $s)) { throw "Source not found: $s" }
}

# VRageMath is the only game assembly the model is allowed to use (ADR-0003), plus the
# netstandard facade the game's own assemblies are built against.
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
if (-not $csc) { throw 'No Roslyn csc.dll found. Install a .NET SDK or point the script at one.' }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'dotnet host not found.' }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$outExe = Join-Path $Out 'VGS.offline.exe'
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

# The game assemblies are plain .NET Framework libraries; copy them next to the exe so the
# runtime can resolve VRageMath without a probing path.
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

$stdout = if (Test-Path $logOut) { Get-Content $logOut -Raw } else { '' }
$stderr = if (Test-Path $logErr) { Get-Content $logErr -Raw } else { '' }
$combined = ("$stdout`n$stderr").Trim()

$errors = @()
if ($combined) { $errors = @($combined -split "`r?`n" | Where-Object { $_ -match ':\s*error\s+CS\d+' }) }

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host "OFFLINE BUILD FAILED — $($errors.Count) error(s)" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

if (-not (Test-Path $outExe)) {
    Write-Host 'OFFLINE BUILD FAILED — compiler produced no executable' -ForegroundColor Red
    Write-Host $combined
    exit 1
}

Write-Host ''

# The checks are run through Start-Process with file redirection rather than `& $exe`.
# Capturing a child's stdout through a pipe needs a named pipe, and a confined environment
# (the DSH sandbox, some CI runners) denies that with "Access to the path
# '\\.\pipe\LOCAL\dotnet_...' is denied" — a failure of the harness, not of the checks, and one
# that reads like a broken gate. File redirection works everywhere and keeps the exit code.
$runOut = Join-Path $Out 'run.out.txt'
$runErr = Join-Path $Out 'run.err.txt'
$proc = Start-Process -FilePath $outExe -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $runOut -RedirectStandardError $runErr

if (Test-Path $runOut) { Get-Content $runOut -Raw }
$runError = if (Test-Path $runErr) { Get-Content $runErr -Raw } else { '' }
if ($runError -and $runError.Trim()) {
    Write-Host $runError -ForegroundColor Yellow
}

$code = $proc.ExitCode
Write-Host ''
if ($code -ne 0) { exit $code }
exit 0
