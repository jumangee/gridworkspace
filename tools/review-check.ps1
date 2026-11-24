<#
.SYNOPSIS
    Static review gate for the VGS mod sources.

.DESCRIPTION
    A reviewer's job is judgement, but a surprising amount of it is mechanical: is the
    layering intact, is the language version respected, is there an unguarded call into the
    game loop, did a string literal escape the text table. Those checks are cheap, repeatable
    and easy to forget, so they belong in a script that runs before every review instead of
    in a human's head.

    This script reports findings with a severity and never edits anything:

      ERROR    a rule the architecture document forbids; the change cannot be accepted
      WARNING  a defect risk that a reviewer must either justify or see fixed
      INFO     an observation worth reading, not necessarily actionable

    Exit code is 1 when any ERROR is found, so it can gate a change on its own.

.PARAMETER SourcePath
    Mod source root. Defaults to src\GridWorkspace.Mod\Data\Scripts\VGS.

.PARAMETER Rules
    Only run the given rule ids, e.g. -Rules R001,R004.

.PARAMETER Quiet
    Print only the summary and the findings, without the rule legend.

.EXAMPLE
    pwsh -File tools/review-check.ps1

.EXAMPLE
    pwsh -File tools/review-check.ps1 -Rules R001,R004
#>
[CmdletBinding()]
param(
    [string] $SourcePath,
    [string[]] $Rules,
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path $repo 'src\GridWorkspace.Mod\Data\Scripts\VGS' }
if (-not (Test-Path $SourcePath)) { throw "Source path not found: $SourcePath" }

$files = @(Get-ChildItem -Path $SourcePath -Recurse -Filter '*.cs' -File)
if ($files.Count -eq 0) { throw "No .cs files under $SourcePath" }

$findings = [System.Collections.Generic.List[object]]::new()

function Should-Run([string] $RuleId) {
    if (-not $Rules -or $Rules.Count -eq 0) { return $true }
    return $Rules -contains $RuleId
}

function Get-Relative([string] $full) {
    return $full.Replace($SourcePath.TrimEnd('\') + '\', '')
}

# Removes string literals and the trailing comment from a line, leaving only the code.
#
# The syntax rules below (R002 async, R003 language version) are about what the compiler sees, and
# a compiler never sees the inside of a string. Without this, a perfectly ordinary sentence in a
# user-facing string trips a rule: `"Jump failed: the player is not in the world yet"` matched
# R003's `is not` pattern (C# 9) and failed the gate on text that cannot be C# at all. Suppressing
# the rule there would have been the wrong fix — the gate, not the sentence, was wrong.
#
# Verbatim strings are not handled because this project has none; a `@"` literal would leave its
# content in place and the rule would simply behave as it did before.
function Get-CodeOnly([string] $line) {
    if (-not $line) { return '' }

    $sb = [System.Text.StringBuilder]::new($line.Length)
    $inString = $false
    $i = 0
    while ($i -lt $line.Length) {
        $ch = $line[$i]
        if ($inString) {
            if ($ch -eq '\' -and $i + 1 -lt $line.Length) { $i += 2; continue }
            if ($ch -eq '"') { $inString = $false }
            $i++
            continue
        }
        if ($ch -eq '"') { $inString = $true; $i++; continue }
        if ($ch -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') { break }
        [void]$sb.Append($ch)
        $i++
    }
    return $sb.ToString()
}

# A rule that cannot be silenced gets ignored, and an ignored gate protects nothing. Any line
# may carry `// review:allow R0NN <reason>`; the same line is then excluded from that rule.
# The reason is required — a bare suppression is itself a finding.
$suppressions = @{}
foreach ($f in $files) {
    $rel = Get-Relative $f.FullName
    $lines = @(Get-Content $f.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '//\s*review:allow\s+(R\d{3})(?:\s+(.+))?$') {
            $rule = $Matches[1]
            $reason = $Matches[2]
            $key = "$rel|$($i + 1)|$rule"
            $suppressions[$key] = $reason
            if ([string]::IsNullOrWhiteSpace($reason)) {
                $findings.Add([pscustomobject]@{
                    Rule     = 'R000'
                    Severity = 'WARNING'
                    File     = $rel
                    Line     = $i + 1
                    Message  = "Suppression of $rule has no reason. State why the rule does not apply."
                })
            }
        }
    }
}

function Test-Suppressed {
    param([string] $Rule, [string] $File, [int] $Line)
    return $suppressions.ContainsKey("$File|$Line|$Rule")
}

function Add-Finding {
    param(
        [string] $Rule,
        [string] $Severity,
        [string] $File,
        [int] $Line,
        [string] $Message
    )
    if (Test-Suppressed -Rule $Rule -File $File -Line $Line) { return }
    $findings.Add([pscustomobject]@{
        Rule     = $Rule
        Severity = $Severity
        File     = $File
        Line     = $Line
        Message  = $Message
    })
}

# ---------------------------------------------------------------------------------------
# R001 — layering: the model layer must not reach into the game world.
#
# Vgs.Model is the only part of the project that can be reasoned about without launching the
# game, and that property only holds while it has no world access. VRageMath is allowed and
# deliberately so: it is a pure value-type math library (Vector2, Vector3I) with no entity
# access, and reimplementing vectors to satisfy a purity rule would be worse.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R001') {
    $forbidden = @(
        'Sandbox\.ModAPI', 'Sandbox\.Game', 'Sandbox\.Graphics',
        'VRage\.Game\.ModAPI', 'VRage\.ModAPI', 'VRage\.Game\.Components',
        'VRage\.Game\.Entities', 'VRage\.Render'
    )
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        if ($rel -notlike 'Model\*') { continue }

        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($ns in $forbidden) {
                if ($lines[$i] -match "using\s+$ns") {
                    Add-Finding 'R001' 'ERROR' $rel ($i + 1) `
                        "Vgs.Model must not reference '$ns'. Move the game access into Vgs.Game and pass plain data across the boundary."
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R002 — no async/await or Task.
#
# System.Threading.Tasks is outside the script whitelist, so the in-game compiler rejects it.
# Catching it here turns a game-launch round trip into a one-second check.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R002') {
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $l = Get-CodeOnly $lines[$i]
            if (-not $l) { continue }
            if ($l -match '\basync\b' -or $l -match '\bawait\b' -or
                $l -match 'System\.Threading\.Tasks' -or $l -match '\bTask<') {
                Add-Finding 'R002' 'ERROR' $rel ($i + 1) `
                    'async/await and Task are outside the Space Engineers script whitelist. Use a frame-driven state machine instead.'
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R003 — language version.
#
# The in-game compiler is Roslyn 2.9, i.e. C# 7.3. Anything newer compiles locally only if
# the local toolchain is newer, which is exactly the kind of false green that reaches players.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R003') {
    $patterns = @(
        @{ Re = '\?\?=';                      What = 'null-coalescing assignment (C# 8)' },
        @{ Re = '\bis\s+not\s';               What = 'pattern `is not` (C# 9)' },
        @{ Re = '^\s*(public|internal|private|protected)?\s*record\s'; What = 'record types (C# 9)' },
        @{ Re = '\binit\s*;';                 What = 'init-only setters (C# 9)' },
        @{ Re = 'using\s+var\s';              What = 'using declarations (C# 8)' },
        @{ Re = '\[\s*\.\.';                  What = 'range/index operators (C# 8)' },
        @{ Re = '^\s*#nullable\s';            What = 'nullable reference types (C# 8)' },
        @{ Re = '\bswitch\s*$';               What = 'possible switch expression (C# 8) — verify' }
    )
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $l = Get-CodeOnly $lines[$i]
            if (-not $l) { continue }
            foreach ($p in $patterns) {
                if ($l -match $p.Re) {
                    Add-Finding 'R003' 'ERROR' $rel ($i + 1) `
                        "Uses $($p.What); the in-game compiler accepts C# 7.3 only."
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R004 — public types must be documented.
#
# The architecture requires every public type to state its role. A public type without a doc
# comment is a design statement nobody wrote down.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R004') {
    $declRe = '^\s*(public|internal)\s+(?:(?:sealed|abstract|static|partial)\s+)*(class|interface|enum|struct)\s+(\w+)'
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch $declRe) { continue }
            $kind = $Matches[2]
            $name = $Matches[3]

            # Look back past attributes and blank lines for an XML doc comment.
            $documented = $false
            for ($j = $i - 1; $j -ge 0 -and $j -ge $i - 6; $j--) {
                $prev = $lines[$j].Trim()
                if ($prev -eq '') { continue }
                if ($prev.StartsWith('///')) { $documented = $true; break }
                if ($prev.StartsWith('[')) { continue }
                break
            }

            if (-not $documented) {
                Add-Finding 'R004' 'WARNING' $rel ($i + 1) `
                    "$kind '$name' has no XML doc comment. State its role in one sentence."
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R005 — logging must go through VgsLog.
#
# Direct MyLog calls bypass the [VGS] prefix, the throttling and the verbose switch, so a
# bug report can no longer be reduced to a grep.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R005') {
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        if ($rel -eq 'Core\VgsLog.cs') { continue }
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*//') { continue }
            if ($lines[$i] -match 'MyLog\.Default') {
                Add-Finding 'R005' 'ERROR' $rel ($i + 1) `
                    'Direct MyLog usage. Call VgsLog instead so the line carries the [VGS] prefix and throttling.'
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R006 — user-facing strings belong in VgsText.
#
# A Cyrillic literal outside the text table is a string that will never be translated and
# that nobody can find when the wording changes.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R006') {
    # The text tables have their own files since Step 6.3: `Core\VgsText.cs` holds the facade and
    # the language-independent marks, and `Core\VgsStrings.cs` holds one key → value table per
    # language (ADR-0033). `Core\VgsConstants.cs` is skipped too, because VgsText lived there
    # before the split and a stray literal left behind in it is still a literal in a table's file
    # rather than a string in the wrong place.
    $textTables = @(
        'Core\VgsText.cs',
        'Core\VgsConstants.cs',
        'Core\VgsStrings.cs'
    )
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        if ($textTables -contains $rel) { continue }
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $l = $lines[$i]
            if ($l -match '^\s*//') { continue }
            if ($l -match '"[^"]*[\u0400-\u04FF][^"]*"') {
                Add-Finding 'R006' 'WARNING' $rel ($i + 1) `
                    'Cyrillic string literal outside the text tables. Move it to Core\VgsStringsRussian.cs and its English counterpart (ADR-0033).'
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R007 — no empty catch blocks.
#
# A swallowed exception in a mod is invisible: the feature silently does nothing and the log
# says nothing. Every catch must log, or carry a comment naming the reason it is safe.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R007') {
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch 'catch\s*(\([^)]*\))?\s*\{\s*\}') { continue }
            Add-Finding 'R007' 'WARNING' $rel ($i + 1) `
                'Empty catch block. Log through VgsLog, or add a comment stating why silence is correct.'
        }
    }
}

# ---------------------------------------------------------------------------------------
# R008 — game-facing entry points must be guarded.
#
# Update/Draw/HandleInput and terminal-control callbacks are called by the game. An exception
# escaping them breaks the session, not just the mod. This is a heuristic: it checks that a
# try block appears near the start of the method body, so it can produce a false negative on
# a long method — the reviewer still reads those.
#
# The name list must include the On-prefixed forms this project actually overrides. A rule
# that only knows MyGuiScreenBase's raw names matches nothing here and silently protects an
# empty set — the same failure mode Q-005 describes for gates that check nothing.
#
# The try detection anchors on the statement, not on the word: matching bare `\btry\b` also
# accepts `Console.WriteLine("no try here")` and `// no try block`, which makes the rule pass
# a method that has no guard at all (Q-008). Both styles used here — `try` on its own line and
# `try { ... }` — are covered by requiring the brace.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R008') {
    $entryRe = '^\s*(public|protected)\s+override\s+[\w<>\[\],\. ]+\s+((?:On)?(?:Update|Draw|HandleInput|Opened|Closed|Cancelling|LayoutChanged|RecreateControls|UnloadData|UpdateAfterSimulation|BeforeStart|Init))\s*\('
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch $entryRe) { continue }
            $method = $Matches[2]

            $hasTry = $false
            $limit = [Math]::Min($i + 30, $lines.Count - 1)
            for ($j = $i; $j -le $limit; $j++) {
                $line = $lines[$j]
                if ($line -notmatch '^\s*//' -and $line -match '(^\s*try\b|\btry\s*\{)') { $hasTry = $true; break }
                # Stop at the end of the method's opening brace block if it closes early.
                if ($j -gt $i -and $line -match '^\s*\}\s*$') { break }
            }

            if (-not $hasTry) {
                Add-Finding 'R008' 'WARNING' $rel ($i + 1) `
                    "Game-facing override '$method' has no try block near its start. An exception here breaks the session."
            }
        }
    }
}

# ---------------------------------------------------------------------------------------
# R009 — mutable public state on shared types.
#
# A public mutable field on a type the UI or the evaluator shares is a race waiting to happen
# and hides where the value is changed. Const/readonly fields are fine.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R009') {
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $l = $lines[$i]
            if ($l -match '^\s*//') { continue }
            if ($l -notmatch '^\s*public\s+(?!const\b)(?!readonly\b)(?!static\s+readonly\b)(?!class\b)(?!interface\b)(?!enum\b)(?!struct\b)(?!event\b)(?!delegate\b)(?:static\s+)?[\w<>\[\],\.]+\s+(\w+)\s*[;=]') { continue }
            Add-Finding 'R009' 'INFO' $rel ($i + 1) `
                "Public mutable field '$($Matches[1])'. Prefer a property, or make it readonly, so writes are findable."
        }
    }
}

# ---------------------------------------------------------------------------------------
# R010 — allocation in draw paths.
#
# Draw() runs every frame. String concatenation, interpolation and LINQ inside it are the
# most common cause of a mod that stutters only on big grids.
# ---------------------------------------------------------------------------------------
if (Should-Run 'R010') {
    foreach ($f in $files) {
        $rel = Get-Relative $f.FullName
        if ($rel -notlike 'Ui\*') { continue }
        $lines = @(Get-Content $f.FullName)
        $depth = 0
        $inDraw = $false
        $drawDepth = 0
        $sawBrace = $false

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $l = $lines[$i]

            if ($l -match '^\s*(private|public|protected|internal)\s+[\w<>\[\]\. ]+\s+(Draw\w*)\s*\(') {
                $inDraw = $true
                $drawDepth = $depth
                $sawBrace = $false
            }

            if ($inDraw -and $sawBrace -and $l -notmatch '^\s*//') {
                if ($l -match '\$"' -or $l -match '"\s*\+\s*\w' -or $l -match '\+\s*"') {
                    Add-Finding 'R010' 'INFO' $rel ($i + 1) `
                        'Possible string allocation inside a Draw* method. Cache the formatted text and rebuild only when it changes.'
                }
                if ($l -match '\.Where\(|\.Select\(|\.OrderBy\(|\.ToList\(') {
                    Add-Finding 'R010' 'WARNING' $rel ($i + 1) `
                        'LINQ inside a Draw* method. Draw runs every frame; use a precomputed list.'
                }
            }

            $open = ([regex]::Matches($l, '\{')).Count
            $close = ([regex]::Matches($l, '\}')).Count
            $depth += $open - $close

            # The body starts only after the opening brace, which Allman style puts on the
            # following line. Without this, the declaration line itself would immediately
            # satisfy the "depth returned" test and the body would never be inspected.
            if ($inDraw -and -not $sawBrace -and $depth -gt $drawDepth) { $sawBrace = $true }
            if ($inDraw -and $sawBrace -and $depth -le $drawDepth) { $inDraw = $false }
        }
    }
}

# ---------------------------------------------------------------------------------------

$errors = @($findings | Where-Object { $_.Severity -eq 'ERROR' })
$warnings = @($findings | Where-Object { $_.Severity -eq 'WARNING' })
$infos = @($findings | Where-Object { $_.Severity -eq 'INFO' })

if (-not $Quiet) {
    Write-Host ''
    Write-Host 'VGS review gate' -ForegroundColor Cyan
    Write-Host "  source: $SourcePath"
    Write-Host "  files : $($files.Count)"
    Write-Host '  rules : R001 layering · R002 async · R003 C# 7.3 · R004 docs · R005 logging'
    Write-Host '          R006 text table · R007 empty catch · R008 guarded entry points'
    Write-Host '          R009 public mutability · R010 draw-path allocation'
    Write-Host ''
}

function Write-Group {
    param([string] $Title, [object[]] $Items, [string] $Color)
    if ($Items.Count -eq 0) { return }
    Write-Host "$Title ($($Items.Count))" -ForegroundColor $Color
    foreach ($x in ($Items | Sort-Object File, Line)) {
        Write-Host ("  {0}:{1}  [{2}] {3}" -f $x.File, $x.Line, $x.Rule, $x.Message) -ForegroundColor $Color
    }
    Write-Host ''
}

Write-Group 'ERROR' $errors 'Red'
Write-Group 'WARNING' $warnings 'Yellow'
Write-Group 'INFO' $infos 'DarkGray'

if ($errors.Count -eq 0 -and $warnings.Count -eq 0 -and $infos.Count -eq 0) {
    Write-Host 'REVIEW OK — no findings.' -ForegroundColor Green
} elseif ($errors.Count -eq 0) {
    Write-Host "REVIEW OK with notes — 0 error(s), $($warnings.Count) warning(s), $($infos.Count) info." -ForegroundColor Green
} else {
    Write-Host "REVIEW FAILED — $($errors.Count) error(s), $($warnings.Count) warning(s), $($infos.Count) info." -ForegroundColor Red
}

if ($errors.Count -gt 0) { exit 1 }
exit 0

