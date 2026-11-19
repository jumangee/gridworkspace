<#
.SYNOPSIS
    Decodes the IL of Space Engineers' script-whitelist registration and lists the
    types/namespaces it allows.

.DESCRIPTION
    A Space Engineers mod ships only .cs source; the game compiles it itself and rejects any
    member that is not on an IL whitelist. That whitelist is built in code
    (SpaceEngineers.Game.MySpaceGameDefaultIlChecker.Allow*Namespaces), not in a data file,
    so the only way to know whether an API is usable from a mod is to read that code.

    This script walks the IL of those methods, collects every `ldtoken` (0xD0) and every
    `ldstr` (0x72) operand, resolves the tokens against the assembly metadata and prints
    the referenced types and namespaces. That answers questions such as "may a mod call
    Sandbox.ModAPI.MyModScreen?" without guessing or trusting a wiki page.

    It is a static analysis only: no game code is executed.

    The opcode table is read from the runtime ([System.Reflection.Emit.OpCodes]) rather than
    hand-written. A hand-written table that is missing one opcode stops the walk silently at
    that byte and under-reports the whitelist — which is exactly how this project concluded
    that engine GUI types were allowed when they are not.

.PARAMETER Bin
    Folder with SpaceEngineers.Game.dll.

.PARAMETER Filter
    Only report entries matching this substring (case-insensitive). Empty = everything.

.PARAMETER MethodFilter
    Only decode methods whose name matches (default: all Allow* methods of the checker).

.EXAMPLE
    pwsh -File tools/read-se-whitelist.ps1 -Filter 'MyModScreen'

.EXAMPLE
    # Full list, to diff after a game update
    pwsh -File tools/read-se-whitelist.ps1 -MethodFilter ''
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $Filter = '',
    [string] $MethodFilter = 'Allow'
)

$ErrorActionPreference = 'Stop'

$resolver = [System.ResolveEventHandler] {
    param($s, $e)
    $n = ($e.Name -split ',')[0]
    $p = Join-Path $Bin "$n.dll"
    if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $Bin 'SpaceEngineers.Game.dll'))
$checker = $asm.GetType('SpaceEngineers.Game.MySpaceGameDefaultIlChecker')
if (-not $checker) { throw 'MySpaceGameDefaultIlChecker not found in SpaceEngineers.Game.dll' }

$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
$methods = $checker.GetMethods($flags) | Where-Object { $_.Name -like "*$MethodFilter*" }
if (-not $methods) { throw "No methods matching '$MethodFilter' on $($checker.FullName)" }

# Operand size per opcode, taken from the runtime so the walk cannot lose its place.
# Keyed by the 16-bit opcode value: single-byte opcodes use their own value, two-byte
# opcodes use 0xFE00 -bor second byte. -2 marks `switch`, whose size depends on its count.
$opSize = @{}
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
    # OpCode.Value is Int16, so two-byte opcodes come back negative; normalise to 0xFE00+.
    $v = [int]$op.Value
    if ($v -lt 0) { $v += 0x10000 }
    $opSize[$v] = $size
}

$result = [System.Collections.Generic.List[string]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new()

foreach ($m in $methods) {
    $body = $m.GetMethodBody()
    if (-not $body) { continue }
    $il = $body.GetILAsByteArray()
    $module = $m.Module

    $i = 0
    while ($i -lt $il.Length) {
        $op = [int]$il[$i]
        $key = $op
        $prefix = 1
        if ($op -eq 0xFE) {
            if ($i + 1 -ge $il.Length) { break }
            $key = 0xFE00 -bor [int]$il[$i + 1]
            $prefix = 2
        }

        if (-not $opSize.ContainsKey($key)) {
            # Never guess a position: a wrong offset would silently truncate the report.
            $result.Add("!! undecodable opcode 0x{0:X} at offset {1} in {2}" -f $key, $i, $m.Name)
            break
        }

        $size = $opSize[$key]
        if ($size -eq -2) { $size = 4 + 4 * [BitConverter]::ToInt32($il, $i + $prefix) }

        if ($size -eq 4) {
            $token = [BitConverter]::ToInt32($il, $i + $prefix)
            $text = $null
            switch ($key) {
                0xD0 {
                    try {
                        $member = $module.ResolveMember($token)
                        if ($member -is [Type]) { $text = "TYPE  $($member.FullName)" }
                        elseif ($member) { $text = "TOKEN $($member.DeclaringType.FullName)::$($member.Name)" }
                    } catch { }
                }
                0x72 {
                    try {
                        $s = $module.ResolveString($token)
                        if ($s) { $text = "STR   $s" }
                    } catch { }
                }
                0x28 { try { $mi = $module.ResolveMethod($token); if ($mi) { $text = "CALL  $($mi.DeclaringType.Name).$($mi.Name)" } } catch { } }
                0x6F { try { $mi = $module.ResolveMethod($token); if ($mi) { $text = "CALLV $($mi.DeclaringType.Name).$($mi.Name)" } } catch { } }
            }
            if ($text -and $seen.Add($text)) { $result.Add($text) }
        }

        $i += $prefix + $size
    }
}

$shown = $result | Where-Object { -not $Filter -or $_ -match [regex]::Escape($Filter) } | Sort-Object -Unique

Write-Host "Decoded $($methods.Count) methods of $($checker.FullName)"
Write-Host "Entries: $($result.Count) total, $($shown.Count) matching '$Filter'"
Write-Host ''

$shown | ForEach-Object { $_ }
