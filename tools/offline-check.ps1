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
$sourceRoot = Join-Path $repo 'src\GridWorkspace.Mod\Data\Scripts\VGS'
$checks = Join-Path $repo 'tests\OfflineChecks.cs'

if (-not $Out) { $Out = Join-Path ([System.IO.Path]::GetTempPath()) ('vgs-offline-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }
if (-not (Test-Path $checks)) { throw "Offline checks not found: $checks" }

# Only the sources that have no game references. Adding VgsEditorScreen here would drag in
# Sandbox.ModAPI and defeat the point of the gate: it must run on a machine that has the game
# installed but does not start it.
$sources = @(
    (Join-Path $sourceRoot 'Core\VgsConstants.cs'),
    # The string tables (ADR-0033): pure data plus a facade, no game references, which is what
    # lets the gate assert that both languages carry the same keys, that every value formats, and
    # that no property resolves to a key — the failure modes a key → value table actually has,
    # and all of them invisible in a compile.
    (Join-Path $sourceRoot 'Core\VgsLanguage.cs'),
    (Join-Path $sourceRoot 'Core\VgsStrings.cs'),
    (Join-Path $sourceRoot 'Core\VgsText.cs'),
    # One file per concept since Step 6.4: the model types that used to share Node.cs.
    (Join-Path $sourceRoot 'Model\Port.cs'),
    (Join-Path $sourceRoot 'Model\ToolbarLayout.cs'),
    (Join-Path $sourceRoot 'Model\NodeKind.cs'),
    (Join-Path $sourceRoot 'Model\GroupMemberActions.cs'),
    (Join-Path $sourceRoot 'Model\WatchTarget.cs'),
    (Join-Path $sourceRoot 'Model\Edge.cs'),
    (Join-Path $sourceRoot 'Model\Node.cs'),
    (Join-Path $sourceRoot 'Model\BlockState.cs'),
    (Join-Path $sourceRoot 'Model\BlockCategory.cs'),
    (Join-Path $sourceRoot 'Model\ControllerState.cs'),
    (Join-Path $sourceRoot 'Model\NodeStateFormat.cs'),
    (Join-Path $sourceRoot 'Model\GridViewFormat.cs'),
    (Join-Path $sourceRoot 'Model\BlockSearch.cs'),
    (Join-Path $sourceRoot 'Model\TextEditor.cs'),
    (Join-Path $sourceRoot 'Model\CellContents.cs'),
    (Join-Path $sourceRoot 'Model\GridGraph.cs'),
    (Join-Path $sourceRoot 'Model\BlockPortCatalog.cs'),
    (Join-Path $sourceRoot 'Model\ToolbarPlanner.cs'),
    (Join-Path $sourceRoot 'Ui\PortGeometry.cs'),
    (Join-Path $sourceRoot 'Ui\EdgeRouting.cs'),
    # The search field and the view state (Step 4): both were extracted from the screen so that
    # the arithmetic that breaks silently — where the caret lands, how far the text scrolls,
    # whether a write is due — is checked without the game. The field's one game dependency (font
    # measurement) is an injected delegate, and the view state touches no grid at all.
    (Join-Path $sourceRoot 'Ui\SearchField.cs'),
    (Join-Path $sourceRoot 'Ui\ViewState.cs'),
    (Join-Path $sourceRoot 'Ui\GridFrames.cs'),
    # Icon geometry (ADR-0019): placement and clipping are pure math, and the generated atlas
    # table is a value the drawing code multiplies by. Neither touches Sandbox.*.
    (Join-Path $sourceRoot 'Ui\IconPlacement.cs'),
    (Join-Path $sourceRoot 'Ui\VgsIconAtlas.g.cs'),
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

# The checks are run through a `cmd.exe` wrapper with file redirection rather than `& $exe`.
# Capturing a child's stdout through a pipe needs a named pipe, and a confined environment
# (the DSH sandbox, some CI runners) denies that with "Access to the path
# '\\.\pipe\LOCAL\dotnet_...' is denied" — a failure of the harness, not of the checks, and one
# that reads like a broken gate. File redirection works everywhere.
#
# The exit code is read from a file the wrapper writes, NOT from Start-Process -PassThru.
# Measured on PowerShell 7.7.0: with -RedirectStandardOutput (and with -NoNewWindow alone) the
# returned Process object reports HasExited = true but ExitCode = $null, even after
# WaitForExit() and Refresh(). `$proc.ExitCode` is then $null, `$null -ne 0` is true, and
# `exit $null` exits 0 — so a failing run printed "OFFLINE FAILED" and reported success to CI.
# A gate that cannot fail protects nothing (Q-005, Q-007, Q-009).
#
# The wrapper is a .cmd file rather than an argument line: cmd needs its own quoting for the
# three paths, and passing them through Start-Process -ArgumentList produces nested quotes that
# cmd rejects with "The filename, directory name, or volume label syntax is incorrect".
$runOut = Join-Path $Out 'run.out.txt'
$runErr = Join-Path $Out 'run.err.txt'
$runCode = Join-Path $Out 'run.code.txt'
$wrapperFile = Join-Path $Out 'run.cmd'
foreach ($stale in @($runCode, $runOut, $runErr)) {
    if (Test-Path $stale) { Remove-Item $stale -Force }
}

# `%ERRORLEVEL%` is expanded by cmd at run time, after the checks have finished. The space
# before `>` keeps cmd from reading the digit as a redirection target; Trim() drops it.
$wrapperLines = @(
    '@echo off',
    ('"{0}" > "{1}" 2> "{2}"' -f $outExe, $runOut, $runErr),
    ('echo %ERRORLEVEL% > "{0}"' -f $runCode)
)

# OEM, not ASCII or UTF-8: cmd.exe reads a .cmd in the console code page, and a temp folder
# whose path contains non-ASCII characters (a user name, typically) would otherwise be mangled
# into a path that does not exist. If even that fails, the missing code file below turns the
# run into INCONCLUSIVE instead of a silent pass.
Set-Content -Path $wrapperFile -Value $wrapperLines -Encoding Oem

Start-Process -FilePath $wrapperFile -NoNewWindow -Wait

if (Test-Path $runOut) { Get-Content $runOut -Raw }
$runError = if (Test-Path $runErr) { Get-Content $runErr -Raw } else { '' }
if ($runError -and $runError.Trim()) {
    Write-Host $runError -ForegroundColor Yellow
}

Write-Host ''

# A missing or unreadable code file is itself a failure: the checks were never observed to
# succeed, and reporting 0 would be the same defect this block replaced.
$code = -1
$codeText = if (Test-Path $runCode) { Get-Content $runCode -Raw } else { $null }
if (-not $codeText -or -not [int]::TryParse($codeText.Trim(), [ref]$code)) {
    Write-Host 'OFFLINE CHECK INCONCLUSIVE — the checks ran but their exit code could not be read' -ForegroundColor Red
    exit 1
}

if ($code -ne 0) { exit $code }

# ---- the icon textures must still match their sources (ADR-0019) ----
#
# The mod's C# table, the SBC registration and the .dds files are three separate artefacts that
# must agree with tools/icon-atlas/icons.txt. The checks above prove the table is self-consistent;
# only a rebuild proves the table still describes the pixels. Without this, editing icons.txt and
# forgetting to rebuild would ship a mod whose table points at a texture that is not there — the
# failure is silent, because the icon simply falls back to geometry.
Write-Host ''
Write-Host '=== icon artefacts match their sources (ADR-0019) ==='
$iconVerify = Join-Path $PSScriptRoot 'build-icon-atlas.ps1'
if (-not (Test-Path $iconVerify)) {
    Write-Host 'OFFLINE CHECK INCONCLUSIVE — build-icon-atlas.ps1 is missing' -ForegroundColor Red
    exit 1
}

& $iconVerify -Verify | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'OFFLINE FAILED — the checked-in icon artefacts do not match tools/icon-atlas' -ForegroundColor Red
    Write-Host 'Run: pwsh -File tools/build-icon-atlas.ps1'
    exit 1
}

# ---- and each .png must be a loadable file (ADR-0019) ----
#
# `-Verify` proves the table was generated from the current sources. It does not prove the file
# itself is well formed: a truncated write, a wrong colour type or a broken chunk would still hash
# consistently with itself. This walks every chunk and checks every CRC, which is arithmetic on
# bytes — no decoder, no Python, no game.
#
# The icons are PNG, not DDS. The DDS version was correct on disk (checked at every mip level by
# two independent decoders) and still drew as an opaque square in the game's sprite path; the same
# icons as PNG render correctly. That is recorded in ADR-0019 §2, and this gate enforces the format
# that was proven in the game rather than the one that looked right on paper.
Write-Host ''
Write-Host '=== icon textures are well formed (ADR-0019) ==='

$textureDir = Join-Path $repo 'src\GridWorkspace.Mod\Textures\VGS\Icons'
$tableFile = Join-Path $sourceRoot 'Ui\VgsIconAtlas.g.cs'
$tableText = Get-Content $tableFile -Raw

$declaredSize = [int]([regex]::Match($tableText, 'TextureSize = (\d+)').Groups[1].Value)
$declaredCount = [int]([regex]::Match($tableText, 'Count = (\d+)').Groups[1].Value)
$declaredNames = [regex]::Matches($tableText, 'return "(VGS_\w+)";') | ForEach-Object { $_.Groups[1].Value }

$textureProblems = @()

if ($declaredNames.Count -ne $declaredCount) {
    $textureProblems += "the table declares $declaredCount icon(s) but names $($declaredNames.Count)"
}

$sbcText = Get-Content (Join-Path $repo 'src\GridWorkspace.Mod\Data\GuiTextures.sbc') -Raw

# CRC-32 as PNG defines it: reflected polynomial 0xEDB88320, pre- and post-inverted.
# The arithmetic is done in uint64 and masked back to 32 bits: PowerShell widens to int32, which
# turns the top bit into a negative number and makes the cast back to uint32 fail.
function Get-PngCrc32([byte[]] $data, [int] $offset, [int] $count) {
    [uint64] $crc = 4294967295
    for ($i = 0; $i -lt $count; $i++) {
        $crc = $crc -bxor [uint64]$data[$offset + $i]
        for ($bit = 0; $bit -lt 8; $bit++) {
            if (($crc -band 1) -ne 0) {
                $crc = ([uint64]3988292384 -bxor ($crc -shr 1)) -band 4294967295
            }
            else {
                $crc = $crc -shr 1
            }
        }
    }
    return [uint32](($crc -bxor 4294967295) -band 4294967295)
}

foreach ($material in $declaredNames) {
    $id = $material.Substring(4)
    $relative = "Textures\VGS\Icons\$id.png"

    # The SBC must register this material, and point it at this file.
    if ($sbcText -notmatch [regex]::Escape("<SubtypeName>$material</SubtypeName>")) {
        $textureProblems += "$material is not registered in GuiTextures.sbc"
    }
    elseif ($sbcText -notmatch [regex]::Escape("<Path>$relative</Path>")) {
        $textureProblems += "$material is registered, but not against $relative"
    }

    $path = Join-Path $textureDir "$id.png"
    if (-not (Test-Path $path)) { $textureProblems += "$relative is missing"; continue }

    $bytes = [System.IO.File]::ReadAllBytes($path)

    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    if ($bytes.Length -lt 57) { $textureProblems += "$relative is too short to be a PNG"; continue }
    for ($i = 0; $i -lt 8; $i++) {
        if ($bytes[$i] -ne $signature[$i]) {
            $textureProblems += "$relative does not start with the PNG signature"
            break
        }
    }

    # Walk the chunk list. Every chunk is length(4) type(4) data(length) crc(4), big-endian, and
    # the CRCs are checked because a corrupted IDAT is the one defect a size check cannot see.
    $at = 8
    $sawHeader = $false
    $sawData = $false
    $sawEnd = $false
    $width = 0
    $height = 0

    while ($at + 8 -le $bytes.Length) {
        $length = [uint32](([uint32]$bytes[$at] -shl 24) -bor ([uint32]$bytes[$at + 1] -shl 16) -bor
                           ([uint32]$bytes[$at + 2] -shl 8) -bor [uint32]$bytes[$at + 3])
        $type = [System.Text.Encoding]::ASCII.GetString($bytes, $at + 4, 4)

        if ($at + 12 + $length -gt $bytes.Length) {
            $textureProblems += "$relative has a $type chunk that runs past the end of the file"
            break
        }

        $stored = [uint32](([uint32]$bytes[$at + 8 + $length] -shl 24) -bor
                           ([uint32]$bytes[$at + 9 + $length] -shl 16) -bor
                           ([uint32]$bytes[$at + 10 + $length] -shl 8) -bor
                           [uint32]$bytes[$at + 11 + $length])
        $actual = Get-PngCrc32 $bytes ($at + 4) (4 + $length)
        if ($stored -ne $actual) {
            $textureProblems += "$relative has a bad CRC on its $type chunk"
        }

        if ($type -eq 'IHDR') {
            $sawHeader = $true
            $width = [uint32](([uint32]$bytes[$at + 8] -shl 24) -bor ([uint32]$bytes[$at + 9] -shl 16) -bor
                              ([uint32]$bytes[$at + 10] -shl 8) -bor [uint32]$bytes[$at + 11])
            $height = [uint32](([uint32]$bytes[$at + 12] -shl 24) -bor ([uint32]$bytes[$at + 13] -shl 16) -bor
                               ([uint32]$bytes[$at + 14] -shl 8) -bor [uint32]$bytes[$at + 15])

            $bitDepth = $bytes[$at + 16]
            $colourType = $bytes[$at + 17]
            $interlace = $bytes[$at + 20]

            # Colour type 6 is 8-bit RGBA. Anything else either loses the alpha (type 2) or is
            # not a format the sprite path was proven to accept.
            if ($bitDepth -ne 8 -or $colourType -ne 6) {
                $textureProblems += "$relative is bit depth $bitDepth colour type $colourType, expected 8 / 6 (RGBA)"
            }
            if ($interlace -ne 0) {
                $textureProblems += "$relative is interlaced, which the encoder never writes"
            }
        }
        elseif ($type -eq 'IDAT') {
            $sawData = $true
        }
        elseif ($type -eq 'IEND') {
            $sawEnd = $true
            $at += 12 + $length
            break
        }

        $at += 12 + $length
    }

    if (-not $sawHeader) { $textureProblems += "$relative has no IHDR chunk" }
    if (-not $sawData) { $textureProblems += "$relative has no IDAT chunk" }
    if (-not $sawEnd) { $textureProblems += "$relative has no IEND chunk" }

    if ($width -ne $declaredSize -or $height -ne $declaredSize) {
        $textureProblems += "$relative is ${width}x${height}, the table says $declaredSize"
    }
}

# A texture nothing references is a file the mod ships for no reason.
if (Test-Path $textureDir) {
    foreach ($file in (Get-ChildItem $textureDir -Filter *.png -File)) {
        $material = 'VGS_' + [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        if ($declaredNames -notcontains $material) {
            $textureProblems += "Icons\$($file.Name) is shipped but no icon references it"
        }
    }

    # The DDS route was abandoned; a leftover .dds would be shipped and never loaded.
    foreach ($file in (Get-ChildItem $textureDir -Filter *.dds -File -ErrorAction SilentlyContinue)) {
        $textureProblems += "Icons\$($file.Name) is a DDS, but the icons ship as PNG (ADR-0019 §2)"
    }
}

if ($textureProblems.Count -gt 0) {
    Write-Host ''
    Write-Host 'OFFLINE FAILED — the icon textures are not usable as shipped' -ForegroundColor Red
    foreach ($problem in $textureProblems) { Write-Host "  $problem" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Run: pwsh -File tools/build-icon-atlas.ps1'
    exit 1
}

Write-Host "  $($declaredNames.Count) texture(s): 8-bit RGBA PNG, ${declaredSize}px, CRCs verified, registered and referenced"

# ---- and the catalogue must describe exactly the icons that are shipped (ADR-0020) ----
#
# `-Verify` proves the artefacts match their sources and the block above proves each PNG is
# loadable. Neither says anything about docs/07-icon-catalog.md, which is what a reader uses to
# find out which control draws which icon: an icon added to the manifest without a catalogue row,
# or a catalogue row left behind by a removed icon, is a document that lies about the mod. The
# check is a set comparison in both directions plus "every icon is drawn somewhere", and it runs
# here rather than by memory so that forgetting it is not an option.
Write-Host ''
Write-Host '=== icon catalogue matches the shipped icons (ADR-0020) ==='
$catalogGate = Join-Path $PSScriptRoot 'check-icon-catalog.ps1'
if (-not (Test-Path $catalogGate)) {
    Write-Host 'OFFLINE CHECK INCONCLUSIVE — check-icon-catalog.ps1 is missing' -ForegroundColor Red
    exit 1
}

& $catalogGate
if ($LASTEXITCODE -ne 0) {
    Write-Host 'OFFLINE FAILED — docs/07-icon-catalog.md does not match tools/icon-atlas/icons.txt' -ForegroundColor Red
    exit 1
}

exit 0
