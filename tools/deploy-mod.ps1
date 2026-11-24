<#
.SYNOPSIS
    Copies the mod into the Space Engineers local mod folder.

.DESCRIPTION
    Space Engineers does not load mods from the game installation. It reads them from
    %APPDATA%\SpaceEngineers\Mods\<folder>\, and only when the folder is enabled in the
    world's mod list. This script mirrors src\GridWorkspace.Mod into that folder so the game picks up
    the current sources on the next world load.

    It is a mirror, not a merge: files removed from src are removed from the deployed copy.
    That is intentional — a stale .cs left behind would still be compiled by the game and
    would produce confusing duplicate-type errors.

    The script refuses to run while SpaceEngineers.exe is running, because the game compiles
    the sources at load time and a half-copied folder produces an unhelpful compile error
    inside the game instead of here.

.PARAMETER Source
    Mod source folder. Defaults to src\GridWorkspace.Mod in this repository.

.PARAMETER ModsRoot
    Space Engineers mods folder. Defaults to %APPDATA%\SpaceEngineers\Mods.

.PARAMETER FolderName
    Deployed folder name. Defaults to GridWorkspace.

    The folder name IS the mod's identity in a save: MyObjectBuilder_Checkpoint+ModItem builds
    the mod path as MyFileSystem.ModsPath + Name (the <Name> element inside the save's <ModItem>),
    and metadata.mod does not participate. So the folder and the save's <Name> must agree.

    The mod was deployed as VGS before it was renamed. A world saved with
    <Name>VGS</Name> looks for Mods\VGS and will not find the mod under the new folder name;
    the unloaded mod also stops claiming its storage GUIDs, so MyModStorageComponent drops the
    layout and visibility flags on the next save. Migrate existing worlds first:

        pwsh -File tools/migrate-mod-folder.ps1 -WhatIf   # see what would change
        pwsh -File tools/migrate-mod-folder.ps1           # rename folder + rewrite worlds

    See docs/adr/0034-naming-grid-workspace.md.

    The display name in the mod list comes from metadata.mod (<Name>Grid Workspace</Name>), so
    the player sees the product name while the folder carries the technical one.

    Use another name only for a throwaway test deployment (e.g. GridWorkspace-dev).

.PARAMETER Force
    Deploy even when SpaceEngineers.exe appears to be running.

.EXAMPLE
    pwsh -File tools/deploy-mod.ps1

.EXAMPLE
    pwsh -File tools/deploy-mod.ps1 -FolderName GridWorkspace-dev -Force
#>
[CmdletBinding()]
param(
    [string] $Source,
    [string] $ModsRoot = (Join-Path $env:APPDATA 'SpaceEngineers\Mods'),
    [string] $FolderName = 'GridWorkspace',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $repo 'src\GridWorkspace.Mod' }

if (-not (Test-Path $Source)) { throw "Mod source not found: $Source" }
if (-not (Test-Path $ModsRoot)) { throw "Mods folder not found: $ModsRoot" }

# The game must not be running: it holds the compiled sources and would race the copy.
$running = Get-Process -Name 'SpaceEngineers' -ErrorAction SilentlyContinue
if ($running -and -not $Force) {
    throw "SpaceEngineers.exe is running (PID $($running.Id -join ', ')). Close the game, or pass -Force if you know the game is only at the main menu."
}

# Sanity check before copying: the game only compiles .cs under Data\Scripts.
$scriptRoot = Join-Path $Source 'Data\Scripts\VGS'
if (-not (Test-Path $scriptRoot)) {
    throw "Expected mod sources under Data\Scripts\VGS — not found in $Source"
}
$csCount = (Get-ChildItem $scriptRoot -Recurse -Filter '*.cs').Count
if ($csCount -eq 0) { throw "No .cs files under $scriptRoot" }

# EntityComponents.sbc claims the mod-storage GUIDs. Without it the game still loads the mod and
# the data still reads back during the session, but MyModStorageComponent.Serialize drops every
# unclaimed key on save — the player's layout disappears on the next world load. That failure is
# silent, so the deploy fails loudly instead.
$components = Join-Path $Source 'Data\EntityComponents.sbc'
if (-not (Test-Path $components)) {
    throw "Data\EntityComponents.sbc is missing from $Source. The mod's storage GUIDs would not be registered and saved layouts would be discarded on save. See docs/adr/0012-layout-lives-on-the-block.md."
}

# The GUIDs in the SBC and in VgsConstants must agree; a mismatch is the same silent data loss.
$constants = Join-Path $scriptRoot 'Core\VgsConstants.cs'
$sbcText = Get-Content $components -Raw
foreach ($line in @(Get-Content $constants)) {
    if ($line -match 'new Guid\("([0-9a-fA-F-]{36})"\)') {
        $guid = $Matches[1]
        if ($sbcText -notmatch [regex]::Escape($guid)) {
            throw "Storage GUID $guid is used in VgsConstants.cs but not registered in Data\EntityComponents.sbc. Saved data using it would be discarded on save."
        }
    }
}

$target = Join-Path $ModsRoot $FolderName
Write-Host "Source : $Source"
Write-Host "Target : $target"
Write-Host "Files  : $csCount .cs"

# Mirror: delete the deployed copy first so removed files do not linger.
if (Test-Path $target) {
    Remove-Item -Path $target -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $target | Out-Null

Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force

# metadata.mod is optional in current builds but harmless and helps identify the version in
# bug reports; warn if it is missing rather than failing.
if (-not (Test-Path (Join-Path $target 'metadata.mod'))) {
    Write-Warning 'metadata.mod is missing from the deployed mod; the game will still load it, but bug reports lose the version string.'
}

$deployed = (Get-ChildItem (Join-Path $target 'Data\Scripts\VGS') -Recurse -Filter '*.cs').Count
Write-Host ''
Write-Host "Deployed $deployed .cs file(s)." -ForegroundColor Green
Write-Host 'Next: enable the mod in the world''s mod list, load the world, then run tools\tail-mod-log.ps1'
