<#
.SYNOPSIS
    Dumps the public API surface of Space Engineers assemblies as Markdown.

.DESCRIPTION
    The Space Engineers "SDK" is not distributed as a documented contract: mods compile
    against the game's own assemblies (VRage*.dll, Sandbox.*.dll, SpaceEngineers.*.dll).
    The only trustworthy source of truth for what a mod may call is the metadata in those
    assemblies. This script loads them (read-only) and prints the public members of the
    requested types, so architecture decisions can be based on verified facts instead of
    guesses, forum posts or outdated wikis.

    It works on a *copy* of nothing: it only reads. It never executes game code.

.PARAMETER Bin
    Path to the game's Bin64 (or Bin64_Profile) folder containing the assemblies.

.PARAMETER Types
    Full type names to dump, e.g. 'Sandbox.Game.Gui.MyTerminalControlFactory'.
    Wildcards are allowed on the simple name, e.g. 'Sandbox.Game.Gui.MyGuiScreen*'.

.PARAMETER Out
    Output markdown file. Defaults to stdout.

.PARAMETER IncludeNonPublic
    Also dump non-public members. Useful to understand internal behaviour, but remember
    a mod cannot legally call them (and they break between game versions).

.EXAMPLE
    pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.Graphics.Gui.MyGuiScreenBase'

.EXAMPLE
    pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.*' -Out docs/01-research/api-dump-sandbox.md
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [Parameter(Mandatory = $true)][string[]] $Types,
    [string] $Out,
    [switch] $IncludeNonPublic
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }

# Assemblies a mod may reference. Loaded lazily by the resolver below, but we preload the
# core ones so type lookup is cheap and dependency resolution order is stable.
$preload = @(
    'VRage.Math.dll', 'VRage.Library.dll', 'VRage.dll', 'VRage.Game.dll',
    'VRage.Input.dll', 'VRage.Audio.dll', 'VRage.Render.dll', 'VRage.Render11.dll',
    'VRage.Scripting.dll', 'VRage.UserInterface.dll', 'VRage.Network.dll',
    'Sandbox.Common.dll', 'Sandbox.Graphics.dll', 'Sandbox.Game.dll',
    'SpaceEngineers.ObjectBuilders.dll', 'SpaceEngineers.Game.dll'
)

$resolver = [System.ResolveEventHandler] {
    param($sender, $e)
    $simple = ($e.Name -split ',')[0]
    $candidate = Join-Path $Bin "$simple.dll"
    if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

$assemblies = [System.Collections.Generic.List[System.Reflection.Assembly]]::new()
foreach ($dll in $preload) {
    $path = Join-Path $Bin $dll
    if (Test-Path $path) {
        try { $assemblies.Add([System.Reflection.Assembly]::LoadFrom($path)) }
        catch { Write-Warning "Failed to load $dll : $($_.Exception.Message)" }
    }
}

$flags = [System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'
if ($IncludeNonPublic) { $flags = $flags -bor [System.Reflection.BindingFlags]'NonPublic' }

function Format-TypeName([Type] $t) {
    if ($null -eq $t) { return '?' }
    if ($t.IsGenericType) {
        $name = $t.Name.Split('`')[0]
        $args = ($t.GetGenericArguments() | ForEach-Object { Format-TypeName $_ }) -join ', '
        return "$name<$args>"
    }
    if ($t.IsArray) { return "$(Format-TypeName $t.GetElementType())[]" }
    if ($t.IsByRef) { return "ref $(Format-TypeName $t.GetElementType())" }
    return $t.Name
}

function Format-Params([System.Reflection.ParameterInfo[]] $ps) {
    ($ps | ForEach-Object {
        $mod = ''
        if ($_.IsOut) { $mod = 'out ' } elseif ($_.ParameterType.IsByRef) { $mod = 'ref ' }
        $opt = ''
        if ($_.IsOptional) { $opt = ' = ' + ($(if ($null -eq $_.DefaultValue) { 'null' } else { $_.DefaultValue })) }
        "$mod$(Format-TypeName $_.ParameterType) $($_.Name)$opt"
    }) -join ', '
}

function Format-Modifiers([System.Reflection.MethodBase] $m) {
    $parts = @()
    if ($m.IsStatic) { $parts += 'static' }
    if ($m.IsAbstract) { $parts += 'abstract' }
    elseif ($m.IsVirtual -and -not $m.IsFinal) { $parts += 'virtual' }
    ($parts -join ' ')
}

$bt = [string][char]96   # backtick, used to build markdown code spans without quoting pain
function Code([string] $s) { $bt + $s + $bt }

$sb = [System.Text.StringBuilder]::new()
function W([string] $s) { [void]$sb.AppendLine($s) }

$allTypes = @()
foreach ($asm in $assemblies) {
    try { $allTypes += $asm.GetExportedTypes() } catch { }
}

$matched = [System.Collections.Generic.List[Type]]::new()
foreach ($pattern in $Types) {
    $found = $allTypes | Where-Object {
        $_.FullName -eq $pattern -or $_.FullName -like $pattern -or $_.Name -like $pattern
    }
    if (-not $found) { Write-Warning "No public type matched '$pattern' (is it internal, or in an assembly not preloaded?)" }
    foreach ($f in $found) { if (-not $matched.Contains($f)) { $matched.Add($f) } }
}

W "# Space Engineers API dump"
W ""
W "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  "
W "Source assemblies: ``$Bin``  "
W "Types: $($matched.Count)"
W ""

foreach ($t in ($matched | Sort-Object FullName)) {
    $kind = if ($t.IsInterface) { 'interface' } elseif ($t.IsEnum) { 'enum' } elseif ($t.IsValueType) { 'struct' } else { 'class' }
    $mods = @()
    if ($t.IsAbstract -and -not $t.IsInterface) { $mods += 'abstract' }
    if ($t.IsSealed -and -not $t.IsValueType) { $mods += 'sealed' }
    W ("## " + (Code $t.FullName))
    W ""
    W ("- assembly: " + (Code $t.Assembly.GetName().Name))
    W "- kind: $kind $(($mods -join ' '))"
    if ($t.BaseType) { W ("- base: " + (Code $t.BaseType.FullName)) }
    $ifaces = $t.GetInterfaces()
    if ($ifaces) {
        $ifaceText = ($ifaces | ForEach-Object { Code $_.FullName }) -join ', '
        W ("- interfaces: " + $ifaceText)
    }
    if ($t.IsEnum) {
        $enumText = ($t.GetEnumNames() | ForEach-Object { Code $_ }) -join ', '
        W ("- values: " + $enumText)
        W ""
        continue
    }
    W ""

    $ctors = $t.GetConstructors($flags)
    if ($ctors) {
        W "### Constructors"
        W '```csharp'
        foreach ($c in $ctors) { W "public $($t.Name)($(Format-Params $c.GetParameters()))" }
        W '```'
        W ""
    }

    $fields = $t.GetFields($flags) | Where-Object { -not $_.IsSpecialName }
    if ($fields) {
        W "### Fields"
        W '```csharp'
        foreach ($f in $fields) {
            $st = if ($f.IsStatic) { 'static ' } else { '' }
            $ro = if ($f.IsInitOnly) { 'readonly ' } else { '' }
            $lit = if ($f.IsLiteral) { " = $($f.GetRawConstantValue())" } else { '' }
            W "public $st$ro$(Format-TypeName $f.FieldType) $($f.Name)$lit"
        }
        W '```'
        W ""
    }

    $props = $t.GetProperties($flags)
    if ($props) {
        W "### Properties"
        W '```csharp'
        foreach ($p in $props) {
            $acc = @()
            if ($p.GetGetMethod($true)) { $acc += 'get;' }
            if ($p.GetSetMethod($true)) { $acc += 'set;' }
            $st = if (($p.GetGetMethod($true) ?? $p.GetSetMethod($true)).IsStatic) { 'static ' } else { '' }
            W "public $st$(Format-TypeName $p.PropertyType) $($p.Name) { $($acc -join ' ') }"
        }
        W '```'
        W ""
    }

    $events = $t.GetEvents($flags)
    if ($events) {
        W "### Events"
        W '```csharp'
        foreach ($e in $events) { W "public event $(Format-TypeName $e.EventHandlerType) $($e.Name)" }
        W '```'
        W ""
    }

    $methods = $t.GetMethods($flags) | Where-Object { -not $_.IsSpecialName }
    if ($methods) {
        W "### Methods"
        W '```csharp'
        foreach ($m in ($methods | Sort-Object Name)) {
            $gen = ''
            if ($m.IsGenericMethod) { $gen = '<' + (($m.GetGenericArguments() | ForEach-Object { $_.Name }) -join ', ') + '>' }
            $mod = Format-Modifiers $m
            if ($mod) { $mod += ' ' }
            W "public $mod$(Format-TypeName $m.ReturnType) $($m.Name)$gen($(Format-Params $m.GetParameters()))"
        }
        W '```'
        W ""
    }
}

$text = $sb.ToString()
if ($Out) {
    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Set-Content -Path $Out -Value $text -Encoding utf8
    Write-Host "Wrote $($matched.Count) types to $Out"
} else {
    $text
}

