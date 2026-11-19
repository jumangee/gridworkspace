<#
.SYNOPSIS
    Prints the VGS mod's lines from the newest Space Engineers log.

.DESCRIPTION
    The game writes one log per launch to %APPDATA%\SpaceEngineers\SpaceEngineers_<date>_<time>.log.
    Every line this mod emits is prefixed with [VGS], so the fastest way to see what happened
    is to filter that prefix out of the newest file.

    Two modes:
      default  — only [VGS] lines, plus any line mentioning an exception in a Vgs file.
      -All     — the tail of the whole log, for when the mod produced nothing and the cause is
                 a Keen compile error or a missing dependency instead.

.PARAMETER Lines
    How many trailing lines to consider.

.PARAMETER All
    Show the whole tail instead of only this mod's lines.

.PARAMETER LogPath
    A specific log file. Defaults to the newest one.

.PARAMETER Follow
    Keep printing new lines as the game appends them. Ctrl+C to stop.

.EXAMPLE
    pwsh -File tools/tail-mod-log.ps1

.EXAMPLE
    pwsh -File tools/tail-mod-log.ps1 -All -Lines 300
#>
[CmdletBinding()]
param(
    [int] $Lines = 300,
    [switch] $All,
    [string] $LogPath,
    [switch] $Follow
)

$ErrorActionPreference = 'Stop'

if (-not $LogPath) {
    $logDir = Join-Path $env:APPDATA 'SpaceEngineers'
    if (-not (Test-Path $logDir)) { throw "Space Engineers log folder not found: $logDir" }

    $newest = Get-ChildItem -Path $logDir -Filter 'SpaceEngineers_*.log' -File -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending |
              Select-Object -First 1

    if (-not $newest) { throw "No SpaceEngineers_*.log in $logDir — has the game been launched?" }
    $LogPath = $newest.FullName
}

$log = Get-Item $LogPath
Write-Host "Log: $($log.FullName)"
Write-Host "Modified: $($log.LastWriteTime)   Size: $([math]::Round($log.Length / 1KB, 1)) KB"
Write-Host ''

function Show-Tail {
    param([string] $Path, [int] $Count, [switch] $Everything)

    $tail = Get-Content -Path $Path -Tail $Count -ErrorAction SilentlyContinue
    if (-not $tail) { return }

    if ($Everything) {
        $tail | ForEach-Object { $_ }
        return
    }

    # The mod's own lines, plus anything that looks like a failure involving mod code —
    # a compile error or an exception inside Vgs.* will not carry the [VGS] prefix.
    $tail | Where-Object {
        $_ -match '\[VGS\]' -or
        $_ -match 'Vgs[A-Za-z]*\.' -or
        ($_ -match '(?i)exception' -and $_ -match 'VGS')
    }
}

$found = Show-Tail -Path $LogPath -Count $Lines -Everything:$All

if (-not $found) {
    Write-Host "No [VGS] lines in the last $Lines lines of the log." -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Most likely causes:' -ForegroundColor Yellow
    Write-Host '  1. The mod is not enabled in the world (or the folder name does not match).'
    Write-Host '  2. The game failed to compile the sources — look for a Keen compile error:'
    Write-Host '       pwsh -File tools/tail-mod-log.ps1 -All -Lines 400'
    Write-Host '  3. The log is from a session started before the mod was deployed.'
    exit 0
}

$found | ForEach-Object { $_ }

if ($Follow) {
    Write-Host ''
    Write-Host 'Following… Ctrl+C to stop.' -ForegroundColor Cyan
    $position = (Get-Item $LogPath).Length
    while ($true) {
        Start-Sleep -Seconds 1
        $current = Get-Item $LogPath -ErrorAction SilentlyContinue
        if (-not $current) { continue }
        if ($current.Length -le $position) { continue }

        $stream = [System.IO.File]::Open($LogPath, 'Open', 'Read', 'ReadWrite')
        try {
            [void]$stream.Seek($position, 'Begin')
            $reader = New-Object System.IO.StreamReader($stream)
            $chunk = $reader.ReadToEnd()
            $position = $stream.Position
            $chunk -split "`r?`n" | Where-Object {
                $_ -match '\[VGS\]' -or $_ -match 'Vgs[A-Za-z]*\.'
            } | ForEach-Object { $_ }
        } finally {
            $stream.Dispose()
        }
    }
}
