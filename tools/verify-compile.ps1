<#
.SYNOPSIS
    Type-checks the mod sources against the real Space Engineers assemblies.

.DESCRIPTION
    A Space Engineers mod is not a normal compiled assembly: the game ships only .cs
    sources inside Data/Scripts/... and compiles them itself at load time. That means a
    typo, a renamed member or a wrong overload is discovered only when the player starts
    the game — a slow and noisy feedback loop.

    This script closes that loop locally. It runs the same Roslyn compiler family the game
    uses (the game ships Microsoft.CodeAnalysis 2.9) against the exact assemblies the mod
    will be compiled against, and reports errors in the terminal. It is a *type check*,
    not a build: no DLL is shipped to the game, and the produced assembly is a throwaway.

    Constraints it enforces on purpose:
      * `-nostdlib+` with .NET Framework 4.x reference assemblies, because the game runs
        on .NET Framework and mixing CoreCLR references silently changes overload
        resolution;
      * `-langversion` pinned to the C# version the game's compiler accepts, so nobody
        accidentally commits syntax the in-game compiler rejects;
      * unsafe code disabled unless explicitly allowed, since mods cannot ship native code.

.PARAMETER Bin
    Game Bin64 folder holding the reference assemblies.

.PARAMETER SourcePath
    Folder (or file) with .cs sources. Defaults to the mod's Data/Scripts/VGS.

.PARAMETER LangVersion
    C# language version. The in-game compiler is Roslyn 2.9, which tops out at C# 7.3.

.PARAMETER FrameworkDir
    .NET Framework reference assemblies. Defaults to the installed v4.0.30319 folder.

.PARAMETER Out
    Where to write the throwaway assembly. Defaults to a temp folder.

.EXAMPLE
    pwsh -File tools/verify-compile.ps1
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $SourcePath,
    [string] $LangVersion = '7.3',
    [string] $FrameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319',
    [string] $Out
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path $repo 'src\VGS.Mod\Data\Scripts\VGS' }
if (-not $Out) { $Out = Join-Path ([System.IO.Path]::GetTempPath()) ('vgs-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }

if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }
if (-not (Test-Path $SourcePath)) { throw "Source path not found: $SourcePath" }

# Assemblies the game makes available to mods. Kept explicit rather than globbing Bin64,
# because referencing everything (e.g. XmlSerializers, EOS) hides real mistakes and slows
# the compiler down for no benefit.
$gameRefs = @(
    'VRage.dll', 'VRage.Math.dll', 'VRage.Library.dll', 'VRage.Game.dll',
    'VRage.Input.dll', 'VRage.Audio.dll', 'VRage.Render.dll', 'VRage.Render11.dll',
    'VRage.Scripting.dll', 'VRage.UserInterface.dll', 'VRage.Network.dll',
    'Sandbox.Common.dll', 'Sandbox.Graphics.dll', 'Sandbox.Game.dll',
    'SpaceEngineers.ObjectBuilders.dll', 'SpaceEngineers.Game.dll',
    # The game assemblies are built against netstandard, so the facade must be present or
    # every type derived from object reports a bogus "type is defined in an assembly that
    # is not referenced" error.
    'netstandard.dll'
)

$frameworkRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll')

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

$sources = @()
if (Test-Path $SourcePath -PathType Container) {
    $sources = Get-ChildItem -Path $SourcePath -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }
} else {
    $sources = @((Resolve-Path $SourcePath).Path)
}
if (-not $sources -or $sources.Count -eq 0) { throw "No .cs files under $SourcePath" }

# The game's compiler is Roslyn 2.9; use the closest available local Roslyn.
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
$outDll = Join-Path $Out 'VGS.verify.dll'
$logOut = Join-Path $Out 'csc.out.txt'
$logErr = Join-Path $Out 'csc.err.txt'
$rspFile = Join-Path $Out 'csc.rsp'

# A response file avoids Windows command-line length limits and quoting bugs once the
# mod grows past a few dozen files.
$rsp = @()
$rsp += '-nologo'
$rsp += '-target:library'
$rsp += '-nostdlib+'
$rsp += "-langversion:$LangVersion"
$rsp += '-unsafe-'
$rsp += '-warn:4'
$rsp += '-nowarn:1591'          # missing XML doc comments are not a defect here
$rsp += "-out:`"$outDll`""
$rsp += $refArgs
$rsp += ($sources | ForEach-Object { "`"$_`"" })
Set-Content -Path $rspFile -Value $rsp -Encoding utf8

# The sandbox forbids inheriting stdio pipes into child .NET processes, so redirect to
# files and read them back. DOTNET_CLI_HOME is relocated so the CLI does not try to write
# to the user profile.
$cliHome = Join-Path $Out 'clihome'
New-Item -ItemType Directory -Force -Path $cliHome | Out-Null
$childEnv = @{
    'DOTNET_CLI_HOME'               = $cliHome
    'DOTNET_CLI_TELEMETRY_OPTOUT'   = '1'
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' = '1'
    'DOTNET_NOLOGO'                 = '1'
    'DOTNET_CLI_UI_LANGUAGE'        = 'en'
}

$argLine = "exec `"$csc`" `"@$rspFile`""
$proc = Start-Process -FilePath $dotnet -ArgumentList $argLine -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $logOut -RedirectStandardError $logErr -Environment $childEnv

$stdout = if (Test-Path $logOut) { Get-Content $logOut -Raw } else { '' }
$stderr = if (Test-Path $logErr) { Get-Content $logErr -Raw } else { '' }
$combined = ("$stdout`n$stderr").Trim()

$errors = @()
$warnings = @()
if ($combined) {
    $errors = @($combined -split "`r?`n" | Where-Object { $_ -match ':\s*error\s+CS\d+' })
    $warnings = @($combined -split "`r?`n" | Where-Object { $_ -match ':\s*warning\s+CS\d+' })
}

Write-Host ''
if ($errors.Count -gt 0) {
    Write-Host "COMPILE FAILED — $($errors.Count) error(s), $($warnings.Count) warning(s)" -ForegroundColor Red
    Write-Host ''
    $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    if ($warnings.Count -gt 0) {
        Write-Host ''
        $warnings | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    }
    Write-Host ''
    Write-Host "Full compiler output: $logOut"
    exit 1
}

if ($combined -and $combined -notmatch '^\s*$' -and $combined -notmatch ':\s*warning\s+CS') {
    # Something other than diagnostics (e.g. a host error) — surface it rather than
    # claiming success.
    Write-Host 'Compiler produced unexpected output:' -ForegroundColor Yellow
    Write-Host $combined
    Write-Host ''
}

if (-not (Test-Path $outDll)) {
    Write-Host 'COMPILE FAILED — compiler produced no assembly' -ForegroundColor Red
    Write-Host $combined
    exit 1
}

Write-Host "COMPILE OK — $($sources.Count) file(s), $($warnings.Count) warning(s)" -ForegroundColor Green
if ($warnings.Count -gt 0) {
    $warnings | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
exit 0
