# Substitution harness for ADR-0042.
#
# The ADR's verification table claims that nine specific defects each make the offline gate fail.
# That claim is only worth anything if it was measured, so this script performs each substitution
# on a scratch copy of the source, runs the gate, and prints the number of failures and which
# checks fired. The repository is left untouched: every file is restored from a byte copy.
#
# Run: pwsh -File tools/substitution-adr0042.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'src\GridWorkspace.Mod\Data\Scripts\VGS'
$constants = Join-Path $src 'Core\VgsConstants.cs'
$geometry = Join-Path $src 'Ui\PortGeometry.cs'
$widget = Join-Path $src 'Model\StateWidget.cs'
$graph = Join-Path $src 'Model\GridGraph.cs'
$catalog = Join-Path $src 'Model\BlockPortCatalog.cs'
$node = Join-Path $src 'Model\Node.cs'

# Only the files a substitution touches are backed up, and only those are restored. The gate's own
# sources (`tests/OfflineChecks.cs`) are never patched, so a copy of them would be dead weight —
# and on this machine writing that file from PowerShell is refused, which is what the first run of
# this script discovered the hard way.
$backups = @{}
foreach ($file in @($constants, $geometry, $widget, $graph, $catalog, $node)) {
    $backups[$file] = [System.IO.File]::ReadAllBytes($file)
}

function Restore-All {
    foreach ($file in $backups.Keys) {
        [System.IO.File]::WriteAllBytes($file, $backups[$file])
    }
}

function Patch($file, $old, $new, $label) {
    $text = [System.IO.File]::ReadAllText($file)

    # The anchors below are written with plain newlines, and the sources use CRLF. Comparing them
    # without normalising is how this script first reported "anchor not found" for text that is
    # plainly in the file: `Contains` compares bytes, not lines.
    $crlf = $text.Contains("`r`n")
    if ($crlf) {
        $old = $old.Replace("`r`n", "`n").Replace("`n", "`r`n")
        $new = $new.Replace("`r`n", "`n").Replace("`n", "`r`n")
    }

    if (-not $text.Contains($old)) { throw "substitution '$label': anchor not found" }
    [System.IO.File]::WriteAllText($file, $text.Replace($old, $new))
}

function Measure-Gate($label) {
    $output = & (Join-Path $repo 'tools\offline-check.ps1') 2>&1 | Out-String
    $failed = $output -match 'OFFLINE FAILED — (\d+) of (\d+)'
    $failures = @()
    foreach ($line in ($output -split "`n")) {
        if ($line -match '^\s*FAIL: (.+)$') { $failures += $Matches[1].Trim() }
    }

    if ($output -match 'OFFLINE OK') {
        Write-Host ("  {0,-52} STILL PASSES  <-- the check does not catch it" -f $label)
        return
    }

    if ($output -match 'OFFLINE BUILD FAILED') {
        Write-Host ("  {0,-52} BUILD FAILED" -f $label)
        return
    }

    Write-Host ("  {0,-52} {1} failure(s)" -f $label, $failures.Count)
    foreach ($failure in $failures) { Write-Host ("      - " + $failure) }
}

Write-Host 'Substitutions against ADR-0042 (each must fail the gate):'
Write-Host ''

# 1. the fill grows downwards instead of upwards
try {
    Patch $geometry @'
            fillTopLeft = new Vector2(frameTopLeft.X + insetX,
                                      frameTopLeft.Y + frameSize.Y - insetY - filled);
'@ @'
            fillTopLeft = new Vector2(frameTopLeft.X + insetX, frameTopLeft.Y + insetY);
'@ 'fill grows from the top'
    Measure-Gate 'fill grows from the top'
}
finally { Restore-All }

# 2. no inset between the frame and the fill
try {
    Patch $constants 'public const float WidgetInsetPixels = 2f;' `
                     'public const float WidgetInsetPixels = 0f;' 'WidgetInsetPixels 2 -> 0'
    Measure-Gate 'WidgetInsetPixels 2 -> 0'
}
finally { Restore-All }

# 3. the column exists only where the switch does
try {
    Patch $geometry @'
            return HasPowerSwitch(node) || HasStateWidget(node);
'@ @'
            return HasPowerSwitch(node);
'@ 'HasStateColumn = switch only'
    Measure-Gate 'HasStateColumn = switch only'
}
finally { Restore-All }

# 4. the cargo container loses the padding the requirement named
try {
    Patch $constants 'public const float WidgetPaddingFillWidth = 0.15f;' `
                     'public const float WidgetPaddingFillWidth = 0.05f;' 'WidgetPaddingFillWidth 0.15 -> 0.05'
    Measure-Gate 'WidgetPaddingFillWidth 0.15 -> 0.05'
}
finally { Restore-All }

# 5. the hand does not start at twelve o'clock
try {
    Patch $geometry 'double angle = -Math.PI * 0.5 + turns * Math.PI * 2.0;' `
                    'double angle = turns * Math.PI * 2.0;' 'hand tip: angle without -pi/2'
    Measure-Gate 'hand tip: angle without -pi/2'
}
finally { Restore-All }

# 6. the hand sweeps an ellipse
try {
    Patch $geometry @'
            return new Vector2(center.X + (float)Math.Cos(angle) * reach * aspectX,
                               center.Y + (float)Math.Sin(angle) * reach);
'@ @'
            return new Vector2(center.X + (float)Math.Cos(angle) * reach,
                               center.Y + (float)Math.Sin(angle) * reach);
'@ 'hand tip: no aspect on X'
    Measure-Gate 'hand tip: no aspect on X'
}
finally { Restore-All }

# 7. a clock reads nothing
try {
    Patch $widget @'
                case StateWidgetKind.Clock: return 1;
'@ @'
                case StateWidgetKind.Clock: return 0;
'@ 'MetricCount(Clock) 1 -> 0'
    Measure-Gate 'MetricCount(Clock) 1 -> 0'
}
finally { Restore-All }

# 8. the graph writes a widget to a node that has none
try {
    Patch $graph @'
            if (node.Widget == StateWidgetKind.None) return false;
'@ '' 'ApplyWidget without the widget check'
    Measure-Gate 'ApplyWidget without the widget check'
}
finally { Restore-All }

# 9. the bolt takes its column from the bar without the bar knowing
try {
    Patch $geometry @'
                width = width - width * VgsConstants.WidgetBoltShare
                        - VgsConstants.WidgetBoltGap * aspectX;
'@ '' 'battery bar keeps the bolt column'
    Measure-Gate 'battery bar keeps the bolt column'
}
finally { Restore-All }

# 10. "off" is tested as `!= On`; `== Off` would let Unavailable and Unknown draw again
try {
    Patch $widget @'
            if (canBeEnabled && power != BlockPower.On) return false;
'@ @'
            if (canBeEnabled && power == BlockPower.Off) return false;
'@ 'ShowsWidget: != On -> == Off'
    Measure-Gate 'ShowsWidget: != On -> == Off'
}
finally { Restore-All }

# 11. the canBeEnabled guard is what keeps a cargo container from silencing itself forever
try {
    Patch $widget @'
            if (canBeEnabled && power != BlockPower.On) return false;
'@ @'
            if (power != BlockPower.On) return false;
'@ 'ShowsWidget: drop the canBeEnabled guard'
    Measure-Gate 'ShowsWidget: drop the canBeEnabled guard'
}
finally { Restore-All }

# 12. the gap under the switch: without it the switch lies on the widget (ADR-0042 §3a)
try {
    Patch $constants 'public const float WidgetTopGapPixels = 2f;' `
                     'public const float WidgetTopGapPixels = 0f;' 'WidgetTopGapPixels 2 -> 0'
    Measure-Gate 'WidgetTopGapPixels 2 -> 0'
}
finally { Restore-All }

# 13. the classifier back to "cargocontainer" only — the shape the defect had (ADR-0042 §7)
try {
    Patch $catalog 'if (s.Contains("container") || s.Contains("cargocontainer") || s.Contains("cargosmall"))' `
                   'if (s.Contains("cargocontainer") || s.Contains("cargosmall"))' `
                   'cargo: back to "cargocontainer" only'
    Measure-Gate 'cargo: back to "cargocontainer" only'
}
finally { Restore-All }

# 14. `None` must mean "ask the table", and the state must be sized by the resolved shape
try {
    Patch $node 'Widget = widget != StateWidgetKind.None' `
                'Widget = widget == StateWidgetKind.None' `
                'Node: None stops meaning "ask the table"'
    Measure-Gate 'Node: None stops meaning "ask the table"'
}
finally { Restore-All }

Restore-All
Write-Host ''
Write-Host 'Repository restored.'
