<#
.SYNOPSIS
    Disassembles selected methods of a Space Engineers assembly into readable IL.

.DESCRIPTION
    The mod may only call what the game's assemblies actually expose, and the interesting
    parts of the 1.211 mod-screen API (MyModScreen, MyScreenSpace, MyScreenSpaceSprite,
    MyScreenSpaceText) are undocumented. Their *behaviour* - the coordinate space, the
    order of operations in a draw call - can only be established by reading the code.

    This script loads an assembly read-only (no game code is executed) and prints the IL of
    the requested methods with every metadata token resolved to a name, so a layout bug can
    be traced to the exact call that produces it.

    It complements dump-se-api.ps1: that one prints the contract, this one prints the body.

.PARAMETER Bin
    Folder containing the game assemblies.

.PARAMETER Assembly
    File name of the assembly to disassemble.

.PARAMETER Type
    Full type name, e.g. 'Sandbox.ModAPI.MyScreenSpace'.

.PARAMETER Method
    Method name filter (wildcards allowed). Default '*' = every method.

.PARAMETER Fields
    Also print the type's fields (they often carry the coordinate constants).

.PARAMETER Out
    Optional output file. Defaults to stdout.

.EXAMPLE
    pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyScreenSpace' -Fields

.EXAMPLE
    pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyScreenSpaceSprite' -Method 'Draw'
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $Assembly = 'Sandbox.Game.dll',
    [Parameter(Mandatory = $true)][string] $Type,
    [string] $Method = '*',
    [switch] $Fields,
    [string] $Out
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Bin)) { throw "Game bin folder not found: $Bin" }

$resolver = [System.ResolveEventHandler] {
    param($s, $e)
    $n = ($e.Name -split ',')[0]
    $p = Join-Path $Bin "$n.dll"
    if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $Bin $Assembly))
$t = $asm.GetType($Type)
if (-not $t) { throw "Type '$Type' not found in $Assembly" }

# Operand size per opcode, from the runtime, so the walk cannot lose its place.
$opSize = @{}
$opNames = @{}
foreach ($f in [System.Reflection.Emit.OpCodes].GetFields()) {
    if ($f.FieldType -ne [System.Reflection.Emit.OpCode]) { continue }
    $op = $f.GetValue($null)
    $size = switch ($op.OperandType.ToString()) {
        'InlineNone' { 0 }
        'ShortInlineI' { 1 }
        'ShortInlineVar' { 1 }
        'ShortInlineBrTarget' { 1 }
        'InlineVar' { 2 }
        'InlineI' { 4 }
        'InlineBrTarget' { 4 }
        'InlineMethod' { 4 }
        'InlineField' { 4 }
        'InlineType' { 4 }
        'InlineString' { 4 }
        'InlineSig' { 4 }
        'InlineTok' { 4 }
        'ShortInlineR' { 4 }
        'InlineI8' { 8 }
        'InlineR' { 8 }
        'InlineSwitch' { -2 }
        default { -1 }
    }
    $v = [int]$op.Value
    if ($v -lt 0) { $v += 0x10000 }
    $opSize[$v] = $size
    $opNames[$v] = $op.Name
}

$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
$sb = [System.Text.StringBuilder]::new()
function W([string] $s) { [void]$sb.AppendLine($s) }

W "# IL dump: $($t.FullName)"
W "- assembly: $($t.Assembly.GetName().Name)"
W ""

if ($Fields) {
    W '## Fields'
    W '```csharp'
    foreach ($f in $t.GetFields($flags)) {
        $mods = @()
        if ($f.IsStatic) { $mods += 'static' }
        if ($f.IsLiteral) { $mods += 'const' }
        elseif ($f.IsInitOnly) { $mods += 'readonly' }
        $lit = ''
        if ($f.IsLiteral) { $lit = ' = ' + $f.GetRawConstantValue() }
        W ("$($mods -join ' ') $($f.FieldType.Name) $($f.Name)$lit")
    }
    W '```'
    W ''
}

$methods = @($t.GetMethods($flags) | Where-Object { $_.Name -like $Method })
$ctors = @($t.GetConstructors($flags) | Where-Object { $Method -eq '*' -or $_.Name -like $Method })
$methods = $methods + $ctors
if ($methods.Count -eq 0) { throw "No methods matching '$Method' on $Type" }

foreach ($m in $methods) {
    $ps = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
    W "## $($m.Name)($ps)"
    $body = $m.GetMethodBody()
    if (-not $body) { W '_(no body)_'; W ''; continue }

    $il = $body.GetILAsByteArray()
    $module = $m.Module
    W '```il'
    $i = 0
    while ($i -lt $il.Length) {
        $start = $i
        $op = [int]$il[$i]
        $key = $op
        $prefix = 1
        if ($op -eq 0xFE) {
            if ($i + 1 -ge $il.Length) { break }
            $key = 0xFE00 -bor [int]$il[$i + 1]
            $prefix = 2
        }
        if (-not $opSize.ContainsKey($key)) {
            W ("{0:X4}: !! undecodable 0x{1:X}" -f $start, $key)
            break
        }
        $size = $opSize[$key]
        if ($size -eq -2) { $size = 4 + 4 * [BitConverter]::ToInt32($il, $i + $prefix) }

        $operand = ''
        if ($size -eq 4) {
            $token = [BitConverter]::ToInt32($il, $i + $prefix)
            try {
                switch ($key) {
                    0x28 { $operand = 'call ' + $module.ResolveMethod($token).DeclaringType.FullName + '::' + $module.ResolveMethod($token).Name }
                    0x6F { $operand = 'callvirt ' + $module.ResolveMethod($token).DeclaringType.FullName + '::' + $module.ResolveMethod($token).Name }
                    0x73 { $operand = 'newobj ' + $module.ResolveMethod($token).DeclaringType.FullName }
                    0x7B { $operand = 'ldfld ' + $module.ResolveField($token).Name }
                    0x7C { $operand = 'ldflda ' + $module.ResolveField($token).Name }
                    0x7D { $operand = 'stfld ' + $module.ResolveField($token).Name }
                    0x7E { $operand = 'ldsfld ' + $module.ResolveField($token).Name }
                    0x80 { $operand = 'stsfld ' + $module.ResolveField($token).Name }
                    0x72 { $operand = 'ldstr "' + $module.ResolveString($token) + '"' }
                    0xD0 { $operand = 'ldtoken ' + $module.ResolveMember($token).Name }
                    0x8C { $operand = 'box ' + $module.ResolveType($token).FullName }
                    0xA5 { $operand = 'unbox.any ' + $module.ResolveType($token).FullName }
                    0x74 { $operand = 'castclass ' + $module.ResolveType($token).FullName }
                    0x75 { $operand = 'isinst ' + $module.ResolveType($token).FullName }
                    default { $operand = 'token 0x{0:X8}' -f $token }
                }
            } catch { $operand = 'token 0x{0:X8} (unresolved)' -f $token }
        } elseif ($size -eq 1) {
            $operand = [int]$il[$i + $prefix]
        } elseif ($size -eq 2) {
            $operand = [BitConverter]::ToInt16($il, $i + $prefix)
        } elseif ($size -eq 8) {
            $operand = [BitConverter]::ToDouble($il, $i + $prefix)
        } elseif ($size -eq 4 -and $key -eq 0x20) {
            $operand = [BitConverter]::ToInt32($il, $i + $prefix)
        }

        W ("{0:X4}: {1,-12} {2}" -f $start, $opNames[$key], $operand)
        $i += $prefix + $size
    }
    W '```'
    W ''
}

$text = $sb.ToString()
if ($Out) {
    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Set-Content -Path $Out -Value $text -Encoding utf8
    Write-Host "Wrote $($methods.Count) methods to $Out"
} else {
    $text
}
