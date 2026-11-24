<#
.SYNOPSIS
    Builds the icon textures, their SBC registration and the generated C# table.

.DESCRIPTION
    The editor draws six Font Awesome icons (ADR-0019, ADR-0020). A Space Engineers mod cannot
    ship a library and the game compiles only .cs files, so the icons are baked into textures at
    build time: SVG -> flattened contours -> anti-aliased 256 px mask -> lossless PNG with a
    straight alpha channel. The mod then knows nothing but a material name and a texture size.

    One texture per icon, not one shared atlas. A shared atlas mixes neighbouring icons together
    as soon as the GPU samples a mip level — at the sizes this editor draws, the transparent
    margin between two cells is a fraction of a texel, and a box filter reaches across it. With
    one texture per icon the sampler clamps at the texture edge, where the margin is transparent
    by construction. Measured, with the numbers in ADR-0019; the clamp that makes it structural is
    verified in the game's IL there too (§4a).

    This script is the whole build. It compiles `tools/icon-atlas/*.cs` with the same Roslyn the
    mod is type-checked with, runs it, and lets it write the artefacts into the mod:

      src/GridWorkspace.Mod/Textures/VGS/Icons/<Id>.png     one texture per icon
      src/GridWorkspace.Mod/Data/GuiTextures.sbc            the SubtypeName -> texture registration
      src/GridWorkspace.Mod/Data/Scripts/VGS/Ui/VgsIconAtlas.g.cs
                                                  the VgsIcon enum, material names, sizes
      docs/icons/<Id>.png                         the catalogue preview (dark background, so the
                                                  light ink is visible in a Markdown viewer)

    All of them are checked in, so a clean checkout builds the mod without running this. Run it
    only when `tools/icon-atlas/icons.txt` or one of the SVGs next to it changed.

    Adding an icon: drop the .svg into `tools/icon-atlas/icons/`, add one line to `icons.txt`,
    add a row to `docs/07-icon-catalog.md`, run this script. No mod code changes beyond the one
    place that draws it.

.PARAMETER Verify
    Rebuild into a scratch folder and compare with what is checked in, without touching the
    repository. Exits 1 when the checked-in textures, SBC or generated table no longer match the
    sources — which is what happens when someone edits `icons.txt` and forgets to rebuild.
    Runs as part of `offline-check.ps1`, so it cannot be forgotten.

.EXAMPLE
    pwsh -File tools/build-icon-atlas.ps1

.EXAMPLE
    pwsh -File tools/build-icon-atlas.ps1 -Verify
#>
[CmdletBinding()]
param(
    [switch] $Verify,
    [string] $FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319',
    [string] $Out
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$toolDir = Join-Path $repo 'tools\icon-atlas'

if (-not $Out) { $Out = Join-Path ([System.IO.Path]::GetTempPath()) ('vgs-atlas-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }

$sources = @(
    (Join-Path $toolDir 'Vec2.cs'),
    (Join-Path $toolDir 'IconPath.cs'),
    (Join-Path $toolDir 'IconRaster.cs'),
    (Join-Path $toolDir 'PngWriter.cs'),
    (Join-Path $toolDir 'AtlasBuilder.cs')
)
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "Source not found: $s" } }

# The tool is deliberately self-contained: no game assemblies, no Python, no package manager.
# That keeps the atlas buildable on any machine, which is the point of a build tool.
$frameworkRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll')
$refArgs = @()
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
$exe = Join-Path $Out 'IconAtlasBuilder.exe'
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
$rsp += "-out:`"$exe`""
$rsp += $refArgs
$rsp += ($sources | ForEach-Object { "`"$_`"" })
Set-Content -Path $rspFile -Value $rsp -Encoding utf8

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
    Write-Host "ATLAS BUILD FAILED — $($errors.Count) error(s)" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

if (-not (Test-Path $exe)) { Write-Host 'ATLAS BUILD FAILED — no executable'; exit 1 }

# A child process captured through a pipe needs a named pipe, which a confined environment
# denies; file redirection through a .cmd wrapper works everywhere (same workaround as
# offline-check.ps1).
$runOut = Join-Path $Out 'run.out.txt'
$runErr = Join-Path $Out 'run.err.txt'
$runCode = Join-Path $Out 'run.code.txt'
$wrapperFile = Join-Path $Out 'run.cmd'
foreach ($stale in @($runCode, $runOut, $runErr)) { if (Test-Path $stale) { Remove-Item $stale -Force } }

# -Verify builds into a scratch root and never writes to the repository.
$outRoot = ''
if ($Verify) {
    $outRoot = Join-Path $Out 'verify'
    if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
}
$wrapperLines = @(
    '@echo off',
    ('"{0}" "{1}" "{2}" > "{3}" 2> "{4}"' -f $exe, $repo, $outRoot, $runOut, $runErr),
    ('echo %ERRORLEVEL% > "{0}"' -f $runCode)
)
Set-Content -Path $wrapperFile -Value $wrapperLines -Encoding Oem

Start-Process -FilePath $wrapperFile -NoNewWindow -Wait

if (Test-Path $runOut) { Get-Content $runOut -Raw }
$runError = if (Test-Path $runErr) { Get-Content $runErr -Raw } else { '' }
if ($runError -and $runError.Trim()) { Write-Host $runError -ForegroundColor Yellow }

$codeText = if (Test-Path $runCode) { Get-Content $runCode -Raw } else { $null }
$code = -1
if (-not $codeText -or -not [int]::TryParse($codeText.Trim(), [ref]$code)) {
    Write-Host 'ATLAS BUILD INCONCLUSIVE — the builder ran but its exit code could not be read' -ForegroundColor Red
    exit 1
}

if ($code -ne 0) { exit $code }

if (-not $Verify) { exit 0 }

# ---- verify: compare the scratch build with the checked-in artefacts ----
#
# The texture list is derived from the manifest rather than hard-coded, so adding an icon does
# not require touching this script — and a texture that is checked in but no longer in the
# manifest is reported too.
$artefacts = @(
    'src\GridWorkspace.Mod\Data\GuiTextures.sbc',
    'src\GridWorkspace.Mod\Data\Scripts\VGS\Ui\VgsIconAtlas.g.cs'
)

$manifestPath = Join-Path $repo 'tools\icon-atlas\icons.txt'
foreach ($line in (Get-Content $manifestPath)) {
    $trimmed = $line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
    $id = ($trimmed -split '\s+')[0]
    $artefacts += "src\GridWorkspace.Mod\Textures\VGS\Icons\$id.png"
    # The catalogue previews are generated too, so they are compared like every other artefact:
    # a preview that drifted from its icon would show the reader a shape the mod no longer ships.
    $artefacts += "docs\icons\$id.png"
}

$drift = @()
foreach ($relative in $artefacts) {
    $fresh = Join-Path $outRoot $relative
    $checkedIn = Join-Path $repo $relative
    if (-not (Test-Path $fresh)) { $drift += "$relative — the build produced no such file"; continue }
    if (-not (Test-Path $checkedIn)) { $drift += "$relative — not checked in"; continue }

    # Text artefacts are compared with line endings normalised, binary ones byte for byte.
    #
    # The builder writes "\r\n" (the SBC and C# files it generates are Windows text), while the
    # repository stores every file with "\n". Comparing raw hashes would then make this gate
    # depend on the machine's `core.autocrlf` rather than on whether the artefacts match their
    # sources: the same checkout would pass on one machine and fail on another, and the honest
    # fix — "commit what the build produces" — is invisible in a diff for a binary and noisy for
    # a text file. Normalising here keeps the gate about content, which is what it is for.
    $binary = $relative.EndsWith('.png')
    if ($binary) {
        $a = (Get-FileHash $fresh -Algorithm SHA256).Hash
        $b = (Get-FileHash $checkedIn -Algorithm SHA256).Hash
        if ($a -ne $b) { $drift += "$relative — differs from a fresh build" }
        continue
    }

    $freshText = ((Get-Content $fresh -Raw) -replace "`r`n", "`n")
    $checkedText = ((Get-Content $checkedIn -Raw) -replace "`r`n", "`n")
    if ($freshText -ne $checkedText) { $drift += "$relative — differs from a fresh build" }
}

# An icon removed from the manifest leaves its texture behind, and the mod would keep shipping
# a file nothing references.
$textureDir = Join-Path $repo 'src\GridWorkspace.Mod\Textures\VGS\Icons'
$previewDir = Join-Path $repo 'docs\icons'
$expected = @{}
foreach ($line in (Get-Content $manifestPath)) {
    $trimmed = $line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
    $expected[(($trimmed -split '\s+')[0]) + '.png'] = $true
}

if (Test-Path $textureDir) {
    foreach ($file in (Get-ChildItem $textureDir -Filter *.png -File)) {
        if (-not $expected.ContainsKey($file.Name)) {
            $drift += "src\GridWorkspace.Mod\Textures\VGS\Icons\$($file.Name) — no longer listed in icons.txt"
        }
    }
}

# The catalogue previews are checked the same way: a preview of an icon nobody lists would show
# up in docs/07-icon-catalog.md as a picture of something the mod does not draw.
if (Test-Path $previewDir) {
    foreach ($file in (Get-ChildItem $previewDir -Filter *.png -File)) {
        if (-not $expected.ContainsKey($file.Name)) {
            $drift += "docs\icons\$($file.Name) — no longer listed in icons.txt"
        }
    }
}

if ($drift.Count -gt 0) {
    Write-Host ''
    Write-Host 'ICONS STALE — the checked-in artefacts do not match tools/icon-atlas' -ForegroundColor Red
    foreach ($d in $drift) { Write-Host "  $d" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Run: pwsh -File tools/build-icon-atlas.ps1'
    exit 1
}

Write-Host ''
Write-Host "ICONS UP TO DATE — $($artefacts.Count) artefact(s) match a fresh build" -ForegroundColor Green
exit 0
