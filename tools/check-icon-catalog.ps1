<#
.SYNOPSIS
    Checks that docs/07-icon-catalog.md still describes the icons the mod actually ships.

.DESCRIPTION
    The catalogue is the answer to "which icon is this button, and where is it drawn". It is a
    document, so it drifts: an icon gets added to the manifest and not to the table, a table row
    survives the icon it described, a preview file goes missing, an icon stops being drawn but
    keeps its texture.

    None of that breaks the build, and none of it is visible in a compile — the mod just ships a
    texture nobody references, or the documentation names a control that no longer exists. This
    gate makes the set comparison mechanical, so the only thing left for a human is the wording.

    What it checks, in both directions:

      * every id in tools/icon-atlas/icons.txt has a row in the catalogue, in the same order;
      * every catalogue row names an id that is in the manifest — no orphan rows;
      * every row links the preview docs/icons/<Id>.png, and that file exists;
      * every row has a non-empty description and a non-empty "where used" cell;
      * every id is drawn somewhere: it appears as `VgsIcon.<Id>` in the mod's sources, outside
        the generated table (which mentions every icon by construction and therefore proves
        nothing).

    What it deliberately does not check: the prose. A gate that judged wording would be a gate
    nobody could satisfy; the reviewer reads the sentences.

.PARAMETER Catalogue
    Path to the catalogue. Defaults to docs/07-icon-catalog.md.

.PARAMETER Manifest
    Path to the icon manifest. Defaults to tools/icon-atlas/icons.txt.

.PARAMETER SourcePath
    Mod source root. Defaults to src/GridWorkspace.Mod/Data/Scripts/VGS.

.EXAMPLE
    pwsh -File tools/check-icon-catalog.ps1
#>
[CmdletBinding()]
param(
    [string] $Catalogue,
    [string] $Manifest,
    [string] $SourcePath
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Catalogue) { $Catalogue = Join-Path $repo 'docs\07-icon-catalog.md' }
if (-not $Manifest) { $Manifest = Join-Path $repo 'tools\icon-atlas\icons.txt' }
if (-not $SourcePath) { $SourcePath = Join-Path $repo 'src\GridWorkspace.Mod\Data\Scripts\VGS' }

foreach ($path in @($Catalogue, $Manifest)) {
    if (-not (Test-Path $path)) { Write-Host "ICON CATALOG FAILED — missing $path" -ForegroundColor Red; exit 1 }
}
if (-not (Test-Path $SourcePath)) {
    Write-Host "ICON CATALOG FAILED — missing source root $SourcePath" -ForegroundColor Red
    exit 1
}

$problems = [System.Collections.Generic.List[string]]::new()

# ---- the manifest: the definition of the icon set ----
$manifestIds = [System.Collections.Generic.List[string]]::new()
foreach ($line in (Get-Content $Manifest)) {
    $trimmed = $line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
    $manifestIds.Add(($trimmed -split '\s+')[0])
}
if ($manifestIds.Count -eq 0) {
    Write-Host 'ICON CATALOG FAILED — the manifest lists no icons' -ForegroundColor Red
    exit 1
}

# ---- the catalogue table ----
$catalogueText = Get-Content $Catalogue -Raw
$lines = Get-Content $Catalogue

# The four columns are the contract of the table. A catalogue that dropped one of them is not
# a catalogue any more, and every row check below would silently pass on a shorter table.
$headerIndex = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\|\s*Изображение\s*\|' -and
        $lines[$i] -match 'Описание' -and
        $lines[$i] -match 'Идентификатор' -and
        $lines[$i] -match 'Где используется') {
        $headerIndex = $i
        break
    }
}
if ($headerIndex -lt 0) {
    $problems.Add('the catalogue has no table with the columns "Изображение | Описание | Идентификатор | Где используется"')
}

$catalogueIds = [System.Collections.Generic.List[string]]::new()
$rowNumbers = [System.Collections.Generic.List[int]]::new()

if ($headerIndex -ge 0) {
    # Rows follow the header and its separator until the first line that is not a table row.
    for ($i = $headerIndex + 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (-not $line.TrimStart().StartsWith('|')) { break }
        if ($line -match '^\|\s*-{3,}') { continue }   # the header separator

        $cells = $line.Trim().Trim('|') -split '\|'
        if ($cells.Count -lt 4) {
            $problems.Add("line $($i + 1): the row has $($cells.Count) cell(s), expected 4")
            continue
        }

        $image = $cells[0].Trim()
        $description = $cells[1].Trim()
        $identifier = $cells[2].Trim()
        $usage = $cells[3].Trim()

        $idMatch = [regex]::Match($identifier, '^`VgsIcon\.(\w+)`$')
        if (-not $idMatch.Success) {
            $problems.Add("line $($i + 1): the identifier cell must be exactly ``VgsIcon.<Id>``, found '$identifier'")
            continue
        }

        $id = $idMatch.Groups[1].Value
        $catalogueIds.Add($id)
        $rowNumbers.Add($i + 1)

        if ([string]::IsNullOrWhiteSpace($description)) {
            $problems.Add("line $($i + 1): '$id' has an empty description")
        }
        if ([string]::IsNullOrWhiteSpace($usage)) {
            $problems.Add("line $($i + 1): '$id' does not say where it is used")
        }

        $expectedImage = "icons/$id.png"
        if ($image -notmatch [regex]::Escape($expectedImage)) {
            $problems.Add("line $($i + 1): '$id' must show ![...]($expectedImage)")
        }
        elseif (-not (Test-Path (Join-Path $repo "docs\$($expectedImage.Replace('/', '\'))"))) {
            $problems.Add("line $($i + 1): docs/$expectedImage is missing — run tools/build-icon-atlas.ps1")
        }
    }
}

# ---- set comparison, both directions ----
foreach ($id in $manifestIds) {
    if (-not $catalogueIds.Contains($id)) {
        $problems.Add("'$id' is in tools/icon-atlas/icons.txt but has no row in the catalogue")
    }
}
foreach ($id in $catalogueIds) {
    if (-not $manifestIds.Contains($id)) {
        $problems.Add("the catalogue describes '$id', which is not in tools/icon-atlas/icons.txt")
    }
}

# Order matters: the table is meant to read in the atlas order, so a row appended at the end
# for an icon that belongs in the middle is a sign the table was edited without looking at it.
if ($catalogueIds.Count -eq $manifestIds.Count) {
    for ($i = 0; $i -lt $manifestIds.Count; $i++) {
        if ($catalogueIds[$i] -ne $manifestIds[$i]) {
            $problems.Add("row $($rowNumbers[$i]) is '$($catalogueIds[$i])' but the manifest has " +
                          "'$($manifestIds[$i])' at that position — the table must follow icons.txt order")
        }
    }
}

# ---- every icon must be drawn somewhere ----
#
# The generated table is excluded: it mentions every icon in its switch statements by
# construction, so counting it would make this check pass on a set of icons nothing draws.
$codeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$codeFiles = @(Get-ChildItem -Path $SourcePath -Recurse -Filter '*.cs' -File |
               Where-Object { $_.Name -ne 'VgsIconAtlas.g.cs' })
foreach ($file in $codeFiles) {
    foreach ($match in [regex]::Matches((Get-Content $file.FullName -Raw), 'VgsIcon\.(\w+)')) {
        [void]$codeIds.Add($match.Groups[1].Value)
    }
}

foreach ($id in $manifestIds) {
    if (-not $codeIds.Contains($id)) {
        $problems.Add("'$id' is shipped but never drawn: no VgsIcon.$id outside the generated table")
    }
}
foreach ($id in $codeIds) {
    if (-not $manifestIds.Contains($id)) {
        $problems.Add("the code draws VgsIcon.$id, which the manifest does not build")
    }
}

# ---- verdict ----
if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host "ICON CATALOG FAILED — $($problems.Count) problem(s)" -ForegroundColor Red
    foreach ($problem in $problems) { Write-Host "  $problem" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'The catalogue is docs/07-icon-catalog.md; the set it describes is tools/icon-atlas/icons.txt.'
    exit 1
}

Write-Host "ICON CATALOG OK — $($manifestIds.Count) icon(s) listed, previewed and drawn"
exit 0
