<#
.SYNOPSIS
    Builds a machine-readable block catalog (JSON) from a Space Engineers installation.

.DESCRIPTION
    Scans <GameData>\CubeBlocks\*.sbc, parses every <Definition> element inside
    <Definitions><CubeBlocks>, and emits one JSON object per block subtype.

    The result is a BUILD-TIME SNAPSHOT of the game data. It is intentionally a
    flat array keyed by subtypeId so that a C# mod / node-graph editor can load it
    with a single deserialize call and index it by subtype name.

    Design notes
    ------------
    * Read-only: nothing under -GameData is ever written to.
    * Idempotent: identical input produces byte-identical output. The script only
      rewrites -Out when the serialized content actually changed, and it never
      embeds timestamps or machine-specific paths in the JSON.
    * Tolerant: definitions may omit xsi:type, may use <SubtypeName> instead of
      <SubtypeId>, may write <Size x y z> as attributes OR as <X><Y><Z> children,
      and may carry a namespace. All of that is handled.
    * Deterministic ordering: output is sorted by subtypeId using an ordinal,
      case-insensitive comparer (SortedDictionary), not culture-aware sorting.

.PARAMETER GameData
    Path to the game's "Content\Data" folder (the folder that contains CubeBlocks\).

.PARAMETER Out
    Path of the JSON file to write. Parent directory is created if missing.

.EXAMPLE
    pwsh -File .\tools\build-block-catalog.ps1

.EXAMPLE
    pwsh -File .\tools\build-block-catalog.ps1 -GameData 'D:\Steam\steamapps\common\SpaceEngineers\Content\Data' -Out '.\data\block-catalog.json'

.NOTES
    Version: 1.0
    Author : VGS project
#>
[CmdletBinding()]
param(
    [string]$GameData = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data',
    [string]$Out      = 'E:\Projects\!VGS\data\block-catalog.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 0. Constants / heuristics
# ---------------------------------------------------------------------------

# Namespace that carries xsi:type on <Definition> elements.
$xsiNs = 'http://www.w3.org/2001/XMLSchema-instance'

# Block "runtime types" (i.e. MyObjectBuilder_<X> without the prefix) that do
# NOT expose terminal controls in game. Everything else is assumed to have a
# terminal panel. This list is a heuristic: the SBC schema does not state it,
# the information lives in the Sandbox.Game class hierarchy.
# Keep it short and explicit - it is meant to be reviewed/edited by hand.
$NoTerminalTypes = @(
    'CubeBlock'           # armor, windows, catwalks, corridors, decorative hull
    'Conveyor'            # conveyor blocks / pipe junctions (no UI)
    'ConveyorConnector'   # conveyor tubes and pipe pieces
    'Passage'             # legacy hidden passage block
    'Ladder2'             # ladder segments
    'PistonTop'           # piston head half of the piston pair
    'EmissiveBlock'       # neon tubes / emissive decorative blocks
    'ExhaustBlock'        # exhaust pipes and caps
    'SpaceBall'           # space ball toy block
    'DebugSphere1'        # debug/test spheres (hidden, not buildable)
    'DebugSphere2'
    'DebugSphere3'
)

# ---------------------------------------------------------------------------
# 1. Helpers
# ---------------------------------------------------------------------------

# Returns the trimmed text of the first direct child element with the given
# local name (namespace-agnostic). Returns $null when the child is absent.
function Get-ChildText {
    param(
        [System.Xml.XmlElement]$Parent,
        [string]$Name
    )
    $node = $Parent.SelectSingleNode("./*[local-name()='$Name']")
    if ($null -eq $node) { return $null }
    return $node.InnerText.Trim()
}

# Parses a boolean element value; returns $Default when absent/blank/unparsable.
function Get-ChildBool {
    param(
        [System.Xml.XmlElement]$Parent,
        [string]$Name,
        [bool]$Default
    )
    $raw = Get-ChildText -Parent $Parent -Name $Name
    if ([string]::IsNullOrWhiteSpace($raw)) { return $Default }
    $parsed = $false
    if ([bool]::TryParse($raw, [ref]$parsed)) { return $parsed }
    switch ($raw.ToLowerInvariant()) {
        '1' { return $true }
        '0' { return $false }
        'yes' { return $true }
        'no'  { return $false }
    }
    return $Default
}

# Parses a numeric element value with invariant culture; $null when absent/bad.
function Get-ChildNumber {
    param(
        [System.Xml.XmlElement]$Parent,
        [string]$Name
    )
    $raw = Get-ChildText -Parent $Parent -Name $Name
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $d = 0.0
    if ([double]::TryParse($raw, [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$d)) {
        return $d
    }
    return $null
}

# Normalizes a definition class name into the runtime block type name:
#   MyObjectBuilder_CockpitDefinition -> MyObjectBuilder_Cockpit
#   MyObjectBuilder_CubeBlock         -> MyObjectBuilder_CubeBlock
#   MyObjectBuilder_Projector         -> MyObjectBuilder_Projector
function ConvertTo-BlockTypeId {
    param([string]$DefinitionClass, [string]$FallbackTypeId)

    $name = $DefinitionClass
    if ([string]::IsNullOrWhiteSpace($name)) { $name = $FallbackTypeId }
    if ([string]::IsNullOrWhiteSpace($name)) { return $null }

    $name = $name.Trim()
    if ($name.EndsWith('Definition', [System.StringComparison]::Ordinal) -and
        $name.Length -gt 'Definition'.Length) {
        $name = $name.Substring(0, $name.Length - 'Definition'.Length)
    }
    if (-not $name.StartsWith('MyObjectBuilder_', [System.StringComparison]::Ordinal)) {
        $name = 'MyObjectBuilder_' + $name
    }
    return $name
}

# Reads <Size x="1" y="2" z="3"/> or <Size><X>1</X>...</Size>; returns "1x2x3".
function Get-BlockSize {
    param([System.Xml.XmlElement]$Definition)

    $node = $Definition.SelectSingleNode("./*[local-name()='Size']")
    if ($null -eq $node) { return $null }

    $x = $node.GetAttribute('x')
    $y = $node.GetAttribute('y')
    $z = $node.GetAttribute('z')

    if ([string]::IsNullOrWhiteSpace($x) -and [string]::IsNullOrWhiteSpace($y) -and
        [string]::IsNullOrWhiteSpace($z)) {
        # Child-element form: <Size><X>1</X><Y>1</Y><Z>1</Z></Size>
        $x = Get-ChildText -Parent $node -Name 'X'
        $y = Get-ChildText -Parent $node -Name 'Y'
        $z = Get-ChildText -Parent $node -Name 'Z'
    }

    if ([string]::IsNullOrWhiteSpace($x) -or [string]::IsNullOrWhiteSpace($y) -or
        [string]::IsNullOrWhiteSpace($z)) {
        return $null
    }
    return ('{0}x{1}x{2}' -f $x.Trim(), $y.Trim(), $z.Trim())
}

# ---------------------------------------------------------------------------
# 2. Validate inputs
# ---------------------------------------------------------------------------

$GameData = [System.IO.Path]::GetFullPath($GameData)
$Out      = [System.IO.Path]::GetFullPath($Out)
$cubeDir  = Join-Path $GameData 'CubeBlocks'

if (-not (Test-Path -LiteralPath $GameData -PathType Container)) {
    throw "GameData folder not found: $GameData"
}
if (-not (Test-Path -LiteralPath $cubeDir -PathType Container)) {
    throw "CubeBlocks folder not found: $cubeDir (is -GameData pointing at Content\Data?)"
}

$sbcFiles = @(Get-ChildItem -LiteralPath $cubeDir -Filter '*.sbc' -File | Sort-Object Name)
if ($sbcFiles.Count -eq 0) {
    throw "No *.sbc files found in $cubeDir"
}

Write-Host ("[catalog] game data : {0}" -f $GameData)
Write-Host ("[catalog] sbc files : {0}" -f $sbcFiles.Count)

# ---------------------------------------------------------------------------
# 3. Parse every definition
# ---------------------------------------------------------------------------

# SortedDictionary gives us BOTH dedupe (by key) and deterministic ordinal
# ordering in one structure. Later files override earlier ones.
$bySubtype = [System.Collections.Generic.SortedDictionary[string, object]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

$totalDefinitions = 0        # <Definition> elements seen
$skippedEmptySubtype = 0     # definitions with no SubtypeId/SubtypeName
$skippedDetails = [System.Collections.Generic.List[string]]::new()
$duplicateCount = 0
$duplicateDetails = [System.Collections.Generic.List[string]]::new()
$failedFiles = [System.Collections.Generic.List[string]]::new()
$noXsiTypeCount = 0

foreach ($file in $sbcFiles) {

    $doc = [System.Xml.XmlDocument]::new()
    try {
        # Ignore DTDs; preserve whitespace off for speed.
        $doc.XmlResolver = $null
        $doc.Load($file.FullName)
    }
    catch {
        $failedFiles.Add(('{0}: {1}' -f $file.Name, $_.Exception.Message))
        Write-Warning ("[catalog] failed to parse {0}: {1}" -f $file.Name, $_.Exception.Message)
        continue
    }

    # Namespace-agnostic selection: some packs wrap CubeBlocks differently.
    $defs = $doc.SelectNodes("//*[local-name()='CubeBlocks']/*[local-name()='Definition']")
    if ($null -eq $defs -or $defs.Count -eq 0) { continue }

    foreach ($def in $defs) {
        $totalDefinitions++

        $idNode = $def.SelectSingleNode("./*[local-name()='Id']")
        if ($null -eq $idNode) {
            $skippedEmptySubtype++
            $skippedDetails.Add(('{0}: <Definition> without <Id>' -f $file.Name))
            continue
        }

        # SubtypeId is authoritative; SubtypeName is a tolerated alias.
        $subtype = Get-ChildText -Parent $idNode -Name 'SubtypeId'
        if ([string]::IsNullOrWhiteSpace($subtype)) {
            $subtype = Get-ChildText -Parent $idNode -Name 'SubtypeName'
        }
        if ([string]::IsNullOrWhiteSpace($subtype)) {
            # Legacy "default" definition of a type (e.g. Door, Passage, Ladder2).
            # These register under an empty subtype and are not addressable, so
            # they cannot live in a subtypeId-keyed catalog.
            $skippedEmptySubtype++
            $typeHint = Get-ChildText -Parent $idNode -Name 'TypeId'
            $skippedDetails.Add(('{0}: empty SubtypeId (TypeId={1})' -f $file.Name, $typeHint))
            continue
        }

        $rawTypeId = Get-ChildText -Parent $idNode -Name 'TypeId'
        $xsiType = $def.GetAttribute('type', $xsiNs)
        if ([string]::IsNullOrWhiteSpace($xsiType)) {
            $noXsiTypeCount++
        }
        $typeId = ConvertTo-BlockTypeId -DefinitionClass $xsiType -FallbackTypeId $rawTypeId
        $shortType = $typeId -replace '^MyObjectBuilder_', ''

        $displayName = Get-ChildText -Parent $def -Name 'DisplayName'
        if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = $subtype }

        # Category: BlockPairName (block family key) -> Group -> first Category.
        $category = Get-ChildText -Parent $def -Name 'BlockPairName'
        if ([string]::IsNullOrWhiteSpace($category)) { $category = Get-ChildText -Parent $def -Name 'Group' }
        if ([string]::IsNullOrWhiteSpace($category)) { $category = Get-ChildText -Parent $def -Name 'Category' }
        if ([string]::IsNullOrWhiteSpace($category)) { $category = $null }

        $entry = [ordered]@{
            subtypeId          = $subtype
            typeId             = $typeId
            displayName        = $displayName
            cubeSize           = Get-ChildText -Parent $def -Name 'CubeSize'
            blockSize          = Get-BlockSize -Definition $def
            public             = Get-ChildBool -Parent $def -Name 'Public'  -Default $true
            enabled            = Get-ChildBool -Parent $def -Name 'Enabled' -Default $true
            pcOnly             = Get-ChildBool -Parent $def -Name 'PCOnly'  -Default $false
            mass               = Get-ChildNumber -Parent $def -Name 'Mass'
            category           = $category
            hasTerminalControls = -not ($NoTerminalTypes -contains $shortType)
            isCockpitLike      = [bool]($typeId -match 'Cockpit')
        }

        if ($bySubtype.ContainsKey($subtype)) {
            $duplicateCount++
            $duplicateDetails.Add(('{0} (redefined in {1})' -f $subtype, $file.Name))
        }
        # Later definition wins (mirrors "last write wins" load semantics).
        $bySubtype[$subtype] = [pscustomobject]$entry
    }
}

# ---------------------------------------------------------------------------
# 4. Serialize
# ---------------------------------------------------------------------------

$catalog = @($bySubtype.Values)
$json = ConvertTo-Json -InputObject $catalog -Depth 4

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Write-Host ("[catalog] created folder {0}" -f $outDir)
}

# Idempotency: only touch the file when the payload changed.
$changed = $true
if (Test-Path -LiteralPath $Out) {
    $existing = Get-Content -LiteralPath $Out -Raw -Encoding utf8
    if ($existing -ceq $json) { $changed = $false }
}
if ($changed) {
    # UTF-8 without BOM keeps the file diff-friendly and .NET-friendly.
    [System.IO.File]::WriteAllText($Out, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host ("[catalog] wrote {0}" -f $Out)
}
else {
    Write-Host ("[catalog] unchanged {0}" -f $Out)
}

# ---------------------------------------------------------------------------
# 5. Verify + summary
# ---------------------------------------------------------------------------

$verify = Get-Content -LiteralPath $Out -Raw -Encoding utf8 | ConvertFrom-Json
if (@($verify).Count -ne $catalog.Count) {
    throw ("Verification failed: JSON has {0} entries, expected {1}" -f @($verify).Count, $catalog.Count)
}

$bySize = $catalog | Group-Object cubeSize | Sort-Object Name
$cockpits = @($catalog | Where-Object { $_.isCockpitLike })
$terminal = @($catalog | Where-Object { $_.hasTerminalControls })
$noTerminal = @($catalog | Where-Object { -not $_.hasTerminalControls })
$types = @($catalog | Group-Object typeId).Count

Write-Host ''
Write-Host '=== block catalog summary ==='
Write-Host ("definitions found        : {0}" -f $totalDefinitions)
Write-Host ("  skipped (empty subtype): {0}" -f $skippedEmptySubtype)
Write-Host ("  duplicate subtypeIds   : {0}" -f $duplicateCount)
Write-Host ("  definitions w/o xsi:type: {0}" -f $noXsiTypeCount)
Write-Host ("  files failed to parse  : {0}" -f $failedFiles.Count)
Write-Host ("catalog entries          : {0}" -f $catalog.Count)
Write-Host ("distinct typeIds         : {0}" -f $types)
foreach ($g in $bySize) {
    Write-Host ("  cubeSize {0,-6}       : {1}" -f $g.Name, $g.Count)
}
Write-Host ("cockpit-like             : {0}" -f $cockpits.Count)
Write-Host ("with terminal controls   : {0}" -f $terminal.Count)
Write-Host ("without terminal controls: {0}" -f $noTerminal.Count)
Write-Host ("public=false             : {0}" -f @($catalog | Where-Object { -not $_.public }).Count)
Write-Host ("JSON verified            : {0} entries parse OK" -f @($verify).Count)

if ($duplicateDetails.Count -gt 0) {
    Write-Host ''
    Write-Host '--- duplicate subtypeIds (later definition won) ---'
    $duplicateDetails | ForEach-Object { Write-Host ("  " + $_) }
}
if ($skippedDetails.Count -gt 0) {
    Write-Host ''
    Write-Host '--- skipped definitions ---'
    $skippedDetails | ForEach-Object { Write-Host ("  " + $_) }
}
