<#
.SYNOPSIS
    Migrates the deployed mod folder name and the worlds that reference the old one.

.DESCRIPTION
    The mod used to deploy into %APPDATA%\SpaceEngineers\Mods\VGS and now deploys into
    ...\Mods\GridWorkspace. The folder name is the mod's identity in a save:
    MyObjectBuilder_Checkpoint+ModItem builds the mod path as MyFileSystem.ModsPath + Name,
    where Name is the <Name> element inside the save's <ModItem>, and metadata.mod does not
    participate.

    So renaming the folder alone breaks every existing world twice:

      1. the world looks for Mods\VGS and does not find the mod — it loads without it;
      2. with the mod unloaded, its storage GUIDs are not claimed by any
         MyObjectBuilder_ModStorageComponentDefinition, and MyModStorageComponent.Serialize
         drops those keys on the next save ("Not saving ModStorageComponent GUID ... not
         claimed"). The card layout and the visibility flags are gone for good.

    This script does both halves, in the only order that is safe:

      1. renames Mods\VGS to Mods\GridWorkspace (skipped when already done);
      2. rewrites <Name>VGS</Name> to <Name>GridWorkspace</Name> in every save's Mods block,
         in Sandbox.sbc and Sandbox_config.sbc, keeping a timestamped backup of each file.

    It is idempotent: a second run finds nothing to do. Use -WhatIf to see the plan without
    touching anything.

    Verified against the game's assemblies: dump-se-il.ps1 -Assembly VRage.Game.dll
    -Type 'VRage.Game.MyObjectBuilder_Checkpoint+ModItem' -Method 'GetPath' resolves the mod
    path as MyFileSystem.ModsPath + Name. See docs/adr/0034-naming-grid-workspace.md.

.PARAMETER ModsRoot
    Space Engineers mods folder. Defaults to %APPDATA%\SpaceEngineers\Mods.

.PARAMETER SavesRoot
    Space Engineers saves folder. Defaults to %APPDATA%\SpaceEngineers\Saves.

.PARAMETER OldName
    Folder / <Name> value the mod used before the rename. Defaults to VGS.

.PARAMETER NewName
    Folder / <Name> value the mod uses now. Defaults to GridWorkspace.

.PARAMETER Force
    Migrate even when SpaceEngineers.exe appears to be running.

.EXAMPLE
    pwsh -File tools/migrate-mod-folder.ps1 -WhatIf

.EXAMPLE
    pwsh -File tools/migrate-mod-folder.ps1
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string] $ModsRoot = (Join-Path $env:APPDATA 'SpaceEngineers\Mods'),
    [string] $SavesRoot = (Join-Path $env:APPDATA 'SpaceEngineers\Saves'),
    [string] $OldName = 'VGS',
    [string] $NewName = 'GridWorkspace',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

if ($OldName -eq $NewName) { throw "OldName and NewName are both '$OldName'; nothing to migrate." }

# The game must not be running: it holds the save and would rewrite it on exit, undoing the fix.
$running = Get-Process -Name 'SpaceEngineers' -ErrorAction SilentlyContinue
if ($running -and -not $Force) {
    throw "SpaceEngineers.exe is running (PID $($running.Id -join ', ')). Close the game first, or pass -Force if you know it is only at the main menu."
}

Write-Host "Migrating '$OldName' -> '$NewName'"
Write-Host "Mods  : $ModsRoot"
Write-Host "Saves : $SavesRoot"
Write-Host ''

# --- 1. The deployed folder -------------------------------------------------
#
# The folder name and the save's <Name> must agree, so the folder is moved first: a world
# rewritten before the folder exists would fail to load on the next start.

$oldFolder = Join-Path $ModsRoot $OldName
$newFolder = Join-Path $ModsRoot $NewName

if (Test-Path $oldFolder) {
    if (Test-Path $newFolder) {
        throw "Both '$oldFolder' and '$newFolder' exist. Refusing to guess: delete or rename one of them by hand, then run this script again."
    }
    if ($PSCmdlet.ShouldProcess($oldFolder, "Rename to $newFolder")) {
        Move-Item -LiteralPath $oldFolder -Destination $newFolder -ErrorAction Stop
        Write-Host "Renamed folder: $oldFolder -> $newFolder" -ForegroundColor Green
    }
} elseif (Test-Path $newFolder) {
    Write-Host "Folder already renamed: $newFolder" -ForegroundColor DarkGray
} else {
    Write-Warning "Neither '$oldFolder' nor '$newFolder' exists. Run tools\deploy-mod.ps1 first; only the saves will be checked."
}

# --- 2. The worlds ----------------------------------------------------------
#
# Both files carry the mod list: Sandbox.sbc is the world, Sandbox_config.sbc is its settings
# copy. A mod present in one and missing from the other produces a mismatch on load.

if (-not (Test-Path $SavesRoot)) { throw "Saves folder not found: $SavesRoot" }

# Backup\ holds the game's own historical snapshots of a world. They are not loaded as the
# current save, so rewriting them would only rewrite history — and a restore from a backup
# would be expected to give back what was there. Left untouched on purpose.
$saveFiles = @(Get-ChildItem $SavesRoot -Recurse -File -Filter '*.sbc' |
    Where-Object { $_.Name -in @('Sandbox.sbc', 'Sandbox_config.sbc') } |
    Where-Object { $_.FullName -notmatch '[\\/]Backup[\\/]' })

if ($saveFiles.Count -eq 0) { throw "No Sandbox.sbc / Sandbox_config.sbc under $SavesRoot" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$changed = 0
$planned = 0
$skipped = 0

foreach ($file in $saveFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)

    # Only the <Mods> block is touched: the same <Name> element exists elsewhere in a save
    # (factions, players, grids), and rewriting those would corrupt the world.
    $modsMatch = [regex]::Match($text, '(?s)<Mods>.*?</Mods>')
    if (-not $modsMatch.Success) { $skipped++; continue }

    $modsBlock = $modsMatch.Value
    $oldItem = '<Name>' + $OldName + '</Name>'
    if (-not $modsBlock.Contains($oldItem)) { $skipped++; continue }

    $newBlock = [regex]::Replace($modsBlock, '(?s)<ModItem\b[^>]*>.*?</ModItem>', {
        param($item)
        if (-not $item.Value.Contains($oldItem)) { return $item.Value }
        $rewritten = $item.Value.Replace($oldItem, '<Name>' + $NewName + '</Name>')
        $rewritten = $rewritten.Replace('FriendlyName="' + $OldName + '"', 'FriendlyName="' + $NewName + '"')
        return $rewritten
    })

    if ($newBlock -eq $modsBlock) { $skipped++; continue }

    $relative = $file.FullName.Substring($SavesRoot.Length).TrimStart('\', '/')
    if (-not $PSCmdlet.ShouldProcess($relative, "Rewrite mod entry $OldName -> $NewName")) {
        # WhatIf / declined: count it so the summary does not claim there was nothing to do.
        $planned++
        continue
    }

    # Backup first: a wrong rewrite of a save is not something to discover later.
    $backup = $file.FullName + '.bak-' + $OldName + '-' + $stamp
    Copy-Item -LiteralPath $file.FullName -Destination $backup -ErrorAction Stop

    $newText = $text.Replace($modsBlock, $newBlock)
    [System.IO.File]::WriteAllText($file.FullName, $newText)

    # Verify by reading back, not by trusting the write.
    $check = [System.IO.File]::ReadAllText($file.FullName)
    $checkBlock = [regex]::Match($check, '(?s)<Mods>.*?</Mods>').Value
    if ($checkBlock.Contains($oldItem) -or -not $checkBlock.Contains('<Name>' + $NewName + '</Name>')) {
        throw "Verification failed for $($file.FullName). The original is at $backup; restore it and report this."
    }

    Write-Host "Rewrote: $relative" -ForegroundColor Green
    Write-Host "  backup: $backup" -ForegroundColor DarkGray
    $changed++
}

Write-Host ''
Write-Host "Saves rewritten: $changed, planned (WhatIf/declined): $planned, already clean or without the mod: $skipped"

if ($changed -eq 0 -and $planned -eq 0 -and -not (Test-Path $oldFolder)) {
    Write-Host 'Nothing to migrate: the folder and every save already use the new name.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Next: start the game and load an existing world. The mod must load and the card'
Write-Host 'layout must be where you left it. If a world still reports a missing mod, its'
Write-Host 'Sandbox.sbc was not covered by this script — check the <Mods> block by hand.'
