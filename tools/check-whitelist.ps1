<#
.SYNOPSIS
    Checks a compiled mod assembly against the Space Engineers mod-script IL whitelist.

.DESCRIPTION
    A mod ships only .cs sources; the game compiles them itself and then rejects every type
    and member that is not on its IL whitelist. A local type check (tools/verify-compile.ps1)
    does NOT catch that: the sources compile fine against the game DLLs and only fail at
    world load with

        Error: The type or member 'X' is prohibited

    which costs a full game launch to discover. This script reproduces the whitelist decision
    offline, so the gate is seconds instead of minutes.

    THE WHITELIST IS READ FROM THE GAME, NOT TRANSCRIBED. It is built in code, in
    MySpaceGameDefaultIlChecker (SpaceEngineers.Game.dll), through
    IMyWhitelistBatch.AllowTypes / AllowNamespaceOfTypes / AllowMembers, each call tagged
    with a MyWhitelistTarget (ModApi, Ingame or Both). This script walks that IL, resolves
    every ldtoken to the type it names, and keeps only the entries whose target admits mods.
    A hand-written table was tried first and was wrong in a way that cost a game launch:
    it treated a namespace allow as a *prefix* rule, so `Sandbox.Game` covered
    `Sandbox.Game.Screens.Helpers.MyToolbar`. It does not. AllowNamespaceOfTypes admits the
    exact namespace and nothing below it, which is why MyModScreen (Sandbox.ModAPI) is
    usable and MyToolbar (Sandbox.Game.Screens.Helpers) is not, although both live in
    Sandbox.Game.dll.

    Two rules decide everything, and both are easy to get wrong:

      * A namespace allow is EXACT. `VRage.Game.ModAPI` does not cover
        `VRage.Game.ModAPI.Ingame`; each has to be allowed on its own.

      * The allow is scoped by target, not by assembly. `Ingame` entries are for the
        Programmable Block and are invisible to a mod, even when the same type is also
        listed for ModApi.

.PARAMETER Assembly
    Compiled mod assembly to inspect. Defaults to the newest output of verify-compile.ps1.

.PARAMETER Bin
    Game Bin64 folder, used to read the whitelist and to resolve type references.

.PARAMETER Quiet
    Only print the verdict line.

.PARAMETER Dump
    Print the decoded whitelist (namespaces, types, members) and exit. Useful after a game
    update, to see what changed.

.PARAMETER SelfTest
    Check the gate itself against cases whose answer is known, then exit. Run this after a
    game update: it proves the decoder still reads the whitelist the way the game means it.

.EXAMPLE
    pwsh -File tools/verify-compile.ps1; pwsh -File tools/check-whitelist.ps1

.EXAMPLE
    pwsh -File tools/check-whitelist.ps1 -Dump
#>
[CmdletBinding()]
param(
    [string] $Assembly,
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [switch] $Quiet,
    [switch] $Dump,
    [switch] $SelfTest
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

# The whitelist names types from many assemblies; they must all be loadable before ldtoken
# tokens can be resolved.
$preload = @(
    'VRage.dll', 'VRage.Math.dll', 'VRage.Library.dll', 'VRage.Game.dll',
    'VRage.Input.dll', 'VRage.Audio.dll', 'VRage.Render.dll', 'VRage.Render11.dll',
    'VRage.Scripting.dll', 'VRage.UserInterface.dll', 'VRage.Network.dll',
    'Sandbox.Common.dll', 'Sandbox.Graphics.dll', 'Sandbox.Game.dll',
    'SpaceEngineers.ObjectBuilders.dll', 'SpaceEngineers.Game.dll'
)
foreach ($d in $preload) {
    $p = Join-Path $Bin $d
    if (Test-Path $p) { try { [void][System.Reflection.Assembly]::LoadFrom($p) } catch { } }
}

# ---------------------------------------------------------------------------------------
# Opcode operand sizes, from the runtime. A hand-written table that misses one opcode stops
# the walk silently at that byte and under-reports the whitelist — which is exactly how this
# project once concluded that engine GUI types were allowed when they are not.
# ---------------------------------------------------------------------------------------
$opSize = @{}
foreach ($f in [System.Reflection.Emit.OpCodes].GetFields()) {
    if ($f.FieldType -ne [System.Reflection.Emit.OpCode]) { continue }
    $op = $f.GetValue($null)
    $size = switch ($op.OperandType.ToString()) {
        'InlineNone' { 0 }
        'ShortInlineI' { 1 } 'ShortInlineVar' { 1 } 'ShortInlineBrTarget' { 1 }
        'InlineVar' { 2 }
        'InlineI' { 4 } 'InlineBrTarget' { 4 } 'InlineMethod' { 4 } 'InlineField' { 4 }
        'InlineType' { 4 } 'InlineString' { 4 } 'InlineSig' { 4 } 'InlineTok' { 4 }
        'ShortInlineR' { 4 }
        'InlineI8' { 8 } 'InlineR' { 8 }
        'InlineSwitch' { -2 }
        default { -1 }
    }
    $v = [int]$op.Value
    if ($v -lt 0) { $v += 0x10000 }   # OpCode.Value is Int16; two-byte opcodes read negative
    $opSize[$v] = $size
}

# ---------------------------------------------------------------------------------------
# Decode the whitelist. MyWhitelistTarget: 0 None, 1 ModApi, 2 Ingame, 3 Both. Only ModApi
# and Both admit a mod script.
# ---------------------------------------------------------------------------------------
$checkerAsm = [System.Reflection.Assembly]::LoadFrom((Join-Path $Bin 'SpaceEngineers.Game.dll'))
$checker = $checkerAsm.GetType('SpaceEngineers.Game.MySpaceGameDefaultIlChecker')
if (-not $checker) { throw 'MySpaceGameDefaultIlChecker not found in SpaceEngineers.Game.dll' }

$checkerFlags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'

$AllowedNamespaces = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$AllowedTypes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$AllowedMembers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Test-ModTarget([int] $Target) { return ($Target -eq 1 -or $Target -eq 3) }

foreach ($m in $checker.GetMethods($checkerFlags)) {
    $body = $m.GetMethodBody(); if (-not $body) { continue }
    $il = $body.GetILAsByteArray(); if (-not $il) { continue }
    $module = $m.Module

    # Arguments to the Allow* calls are built inline on the stack, so the operands of the
    # current call are simply the tokens seen since the previous call.
    $pending = [System.Collections.Generic.List[object]]::new()

    # The MyWhitelistTarget is the integer pushed *before* the array length, i.e. two integer
    # constants back from `newarr`. Tracking "the last ldc.i4 seen" is wrong: the array length
    # and the element indices are ldc.i4 too, and reading one of those as the target silently
    # drops whole namespaces from the mod set (VRageMath among them).
    $prevInt = -1
    $lastInt = -1
    $currentTarget = -1

    $i = 0
    while ($i -lt $il.Length) {
        $op = [int]$il[$i]; $key = $op; $prefix = 1
        if ($op -eq 0xFE) {
            if ($i + 1 -ge $il.Length) { break }
            $key = 0xFE00 -bor [int]$il[$i + 1]; $prefix = 2
        }
        if (-not $opSize.ContainsKey($key)) { break }
        $size = $opSize[$key]
        if ($size -eq -2) { $size = 4 + 4 * [BitConverter]::ToInt32($il, $i + $prefix) }

        # Integer constants: ldc.i4.0..8 (0x16..0x1E), ldc.i4.s (0x1F), ldc.i4 (0x20).
        if ($key -ge 0x16 -and $key -le 0x1E) {
            $prevInt = $lastInt
            $lastInt = $key - 0x16
        } elseif ($key -eq 0x1F) {
            $prevInt = $lastInt
            $lastInt = [int][sbyte]$il[$i + $prefix]
        } elseif ($key -eq 0x20) {
            $prevInt = $lastInt
            $lastInt = [BitConverter]::ToInt32($il, $i + $prefix)
        } elseif ($key -eq 0x8D) {          # newarr: the target precedes the length
            $currentTarget = $prevInt
        }

        if ($size -eq 4) {
            $token = [BitConverter]::ToInt32($il, $i + $prefix)
            switch ($key) {
                0xD0 {
                    try { $mem = $module.ResolveMember($token); if ($mem -is [Type]) { $pending.Add($mem) } } catch { }
                }
                0x72 {
                    try { $s = $module.ResolveString($token); if ($s) { $pending.Add($s) } } catch { }
                }
                0x6F {
                    try {
                        $mi = $module.ResolveMethod($token)
                        if ($mi) {
                            $target = $currentTarget
                            switch ($mi.Name) {
                                'AllowTypes' {
                                    if (Test-ModTarget $target) {
                                        foreach ($x in $pending) { if ($x -is [Type]) { [void]$AllowedTypes.Add($x.FullName) } }
                                    }
                                    $pending.Clear()
                                }
                                'AllowNamespaceOfTypes' {
                                    if (Test-ModTarget $target) {
                                        foreach ($x in $pending) {
                                            if ($x -is [Type] -and $x.Namespace) { [void]$AllowedNamespaces.Add($x.Namespace) }
                                        }
                                    }
                                    $pending.Clear()
                                }
                                'AllowMembers' {
                                    if (Test-ModTarget $target) {
                                        for ($k = 0; $k + 1 -lt $pending.Count; $k += 2) {
                                            $tt = $pending[$k]; $nm = $pending[$k + 1]
                                            if ($tt -is [Type] -and $nm -is [string]) {
                                                [void]$AllowedMembers.Add($tt.FullName + '::' + $nm)
                                            }
                                        }
                                    }
                                    $pending.Clear()
                                }
                            }
                        }
                    } catch { }
                }
            }
        }
        $i += $prefix + $size
    }
}

if ($AllowedNamespaces.Count -eq 0) { throw 'Decoded an empty whitelist; the checker type changed shape.' }

# ---------------------------------------------------------------------------------------
# Self-test. A gate that silently becomes permissive is worse than no gate: this project
# shipped a mod that compiled, passed this very script, and then failed to load because the
# script's hand-written table treated a namespace allow as a prefix rule. These cases pin
# the two rules that were wrong. If a game update changes them, this fails loudly instead of
# approving a broken mod.
# ---------------------------------------------------------------------------------------
function Invoke-SelfTest {
    $cases = @(
        @{ Name = 'VRage.Game.ModAPI.IMyToolbarProvider'; Asm = 'VRage.Game'; Want = $true
           Why = 'the toolbar provider is the whitelisted route to toolbars' }
        @{ Name = 'VRage.Game.ModAPI.IMyToolbar'; Asm = 'VRage.Game'; Want = $true
           Why = 'the toolbar mirror interface is whitelisted' }
        @{ Name = 'VRage.Game.Entity.MyEntity'; Asm = 'VRage.Game'; Want = $true
           Why = 'components hang off MyEntity' }
        @{ Name = 'Sandbox.ModAPI.MyAPIGateway'; Asm = 'Sandbox.Common'; Want = $true
           Why = 'the API gateway must stay usable' }
        @{ Name = 'Sandbox.Game.Screens.Helpers.MyToolbar'; Asm = 'Sandbox.Game'; Want = $false
           Why = 'the exact type that made the mod fail to load' }
        @{ Name = 'Sandbox.Game.Screens.Helpers.IMyToolbarOwner'; Asm = 'Sandbox.Game'; Want = $false
           Why = 'a namespace allow is exact, so a sub-namespace is not covered' }
        @{ Name = 'Sandbox.Game.Entities.Cube.MyTerminalBlock'; Asm = 'Sandbox.Game'; Want = $false
           Why = 'Sandbox.Game.Entities is allowed but Sandbox.Game.Entities.Cube is not' }
        @{ Name = 'Sandbox.Graphics.GUI.MyGuiScreenBase'; Asm = 'Sandbox.Graphics'; Want = $false
           Why = 'Sandbox.Graphics is absent from the whitelist entirely' }
        @{ Name = 'VRage.Game.ModAPI.Ingame.IMyCubeBlock'; Asm = 'VRage.Game'; Want = $true
           Why = 'VRage.Game.ModAPI.Ingame is allowed on its own' }
    )

    $failed = 0
    foreach ($c in $cases) {
        $got = Test-AllowedTypeName $c.Asm $c.Name
        if ($got -ne $c.Want) {
            $failed++
            Write-Host ("  MISMATCH  {0}  expected {1}, got {2} — {3}" -f $c.Name, $c.Want, $got, $c.Why) -ForegroundColor Red
        }
    }

    if ($failed -gt 0) {
        Write-Host ''
        Write-Host "WHITELIST SELF-TEST FAILED — $failed case(s). The gate no longer matches the game." -ForegroundColor Red
        exit 1
    }

    Write-Host "WHITELIST SELF-TEST OK — $($cases.Count) cases, both rules intact" -ForegroundColor Green
}

if ($Dump) {
    Write-Host "MOD-API whitelist: $($AllowedNamespaces.Count) namespace(s), $($AllowedTypes.Count) type(s), $($AllowedMembers.Count) member(s)"
    Write-Host ''
    Write-Host '=== namespaces (exact) ==='
    $AllowedNamespaces | Sort-Object | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host '=== types ==='
    $AllowedTypes | Sort-Object | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host '=== members ==='
    $AllowedMembers | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 0
}

# ---------------------------------------------------------------------------------------
# Check one type name against the decoded whitelist.
# ---------------------------------------------------------------------------------------

# A nested type is allowed when it is listed itself, or when its declaring type is allowed.
function Get-WhitelistCandidates([string] $FullName) {
    $names = [System.Collections.Generic.List[string]]::new()
    $names.Add($FullName)
    $cut = $FullName.LastIndexOf('+')
    while ($cut -gt 0) {
        $FullName = $FullName.Substring(0, $cut)
        $names.Add($FullName)
        $cut = $FullName.LastIndexOf('+')
    }
    return $names
}

function Get-NamespaceOf([string] $FullName) {
    $plus = $FullName.LastIndexOf('+')
    $head = if ($plus -ge 0) { $FullName.Substring(0, $plus) } else { $FullName }
    $dot = $head.LastIndexOf('.')
    if ($dot -lt 0) { return '' }
    return $head.Substring(0, $dot)
}

# The .NET Framework is not policed by the game's checker: a mod is compiled against the
# framework the game runs on, and only references into the game's own assemblies (and the
# handful of packages the game itself allows) are filtered. Detected by assembly name,
# because this gate runs on CoreCLR where `List<T>` resolves to System.Private.CoreLib even
# though the mod's IL names mscorlib.
function Test-FrameworkAssembly([string] $AssemblyName) {
    if (-not $AssemblyName) { return $false }
    if ($AssemblyName -eq 'mscorlib' -or $AssemblyName -eq 'netstandard') { return $true }
    if ($AssemblyName -like 'System*') { return $true }
    if ($AssemblyName -eq 'Microsoft.CSharp') { return $true }
    return $false
}

$PrimitiveMap = @{
    'System.Boolean' = 'bool'; 'System.Byte' = 'byte'; 'System.Char' = 'char'
    'System.Decimal' = 'decimal'; 'System.Double' = 'double'; 'System.Single' = 'float'
    'System.Int32' = 'int'; 'System.Int64' = 'long'; 'System.Object' = 'object'
    'System.SByte' = 'sbyte'; 'System.Int16' = 'short'; 'System.String' = 'string'
    'System.UInt32' = 'uint'; 'System.UInt64' = 'ulong'; 'System.UInt16' = 'ushort'
    'System.Void' = 'void'
}

function Test-AllowedTypeName([string] $AssemblyName, [string] $FullName) {
    if ($PrimitiveMap.ContainsKey($FullName)) { return $true }
    if (Test-FrameworkAssembly $AssemblyName) { return $true }
    if ($FullName -eq $null -or $FullName -eq '') { return $true }

    foreach ($candidate in (Get-WhitelistCandidates $FullName)) {
        if ($AllowedTypes.Contains($candidate)) { return $true }
        $ns = Get-NamespaceOf $candidate
        if ($ns -and $AllowedNamespaces.Contains($ns)) { return $true }
    }
    return $false
}

# A member may be allowed on its own (AllowMembers) even when its type is not.
function Test-AllowedMember([string] $AssemblyName, [string] $DeclaringFullName, [string] $MemberName) {
    if (Test-FrameworkAssembly $AssemblyName) { return $true }
    if (Test-AllowedTypeName $AssemblyName $DeclaringFullName) { return $true }
    foreach ($candidate in (Get-WhitelistCandidates $DeclaringFullName)) {
        if ($AllowedMembers.Contains($candidate + '::' + $MemberName)) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------------------

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if (-not $Assembly) {
    $newest = Get-ChildItem ([System.IO.Path]::GetTempPath()) -Filter 'vgs-verify-*' -Directory -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw 'No verify-compile output found. Run tools/verify-compile.ps1 first, or pass -Assembly.' }
    $Assembly = Join-Path $newest.FullName 'VGS.verify.dll'
}
if (-not (Test-Path $Assembly)) { throw "Assembly not found: $Assembly" }

$mod = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $Assembly).Path)
$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'

# The mod's own types are never filtered: they are not part of the game's assembly set.
$own = $mod.GetName().Name

$found = [System.Collections.Generic.Dictionary[string, object]]::new()
$memberFindings = [System.Collections.Generic.Dictionary[string, string]]::new()

function Add-Type-Ref([Type] $t) {
    if ($null -eq $t) { return }
    if ($t.IsGenericType) { foreach ($a in $t.GetGenericArguments()) { Add-Type-Ref $a } }
    if ($t.IsArray -or $t.IsByRef -or $t.IsPointer) { Add-Type-Ref $t.GetElementType(); return }
    if ($t.IsGenericParameter) { return }
    if ($t.IsNested -and $t.DeclaringType) { Add-Type-Ref $t.DeclaringType; return }

    $full = $t.FullName
    if ($null -eq $full) { return }
    $tick = $full.IndexOf('`')
    if ($tick -ge 0) { $full = $full.Substring(0, $tick) }

    $asmName = $t.Assembly.GetName().Name
    $key = "$asmName|$full"
    if ($found.ContainsKey($key)) { return }
    $found[$key] = [pscustomobject]@{ Assembly = $asmName; Type = $full }
}

foreach ($t in $mod.GetTypes()) {
    Add-Type-Ref $t.BaseType

    # Only the interfaces this type declares. GetInterfaces() also returns everything the
    # base type implements, which for an enum means IConvertible and friends — noise.
    $inherited = if ($t.BaseType) { $t.BaseType.GetInterfaces() } else { @() }
    foreach ($i in $t.GetInterfaces()) {
        if ($inherited -notcontains $i) { Add-Type-Ref $i }
    }

    foreach ($m in $t.GetMembers($flags)) {
        $mi = $m -as [System.Reflection.MethodBase]
        if ($mi) {
            Add-Type-Ref $mi.DeclaringType
            $mb = $mi -as [System.Reflection.MethodInfo]
            if ($mb) { Add-Type-Ref $mb.ReturnType }
            foreach ($p in $mi.GetParameters()) { Add-Type-Ref $p.ParameterType }

            # Body tokens: catches calls, field reads and casts that no signature mentions.
            $body = $mi.GetMethodBody()
            if (-not $body) { continue }
            $il = $body.GetILAsByteArray()
            $module = $mi.Module
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
                if (-not $opSize.ContainsKey($key)) { break }
                $size = $opSize[$key]
                if ($size -eq -2) { $size = 4 + 4 * [BitConverter]::ToInt32($il, $i + $prefix) }
                if ($size -eq 4) {
                    $token = [BitConverter]::ToInt32($il, $i + $prefix)
                    if ($key -ne 0x72) {   # 0x72 is ldstr, a string token, not a member
                        try {
                            $member = $module.ResolveMember($token)
                            if ($member -is [Type]) { Add-Type-Ref $member }
                            elseif ($member) {
                                Add-Type-Ref $member.DeclaringType

                                # A member used on a type that is not allowed on its own may
                                # still be allowed by name (AllowMembers).
                                $mb2 = $member -as [System.Reflection.MethodBase]
                                $declaring = if ($mb2) { $mb2.DeclaringType } else { $null }
                                if ($declaring -and $declaring.Assembly.GetName().Name -ne $own) {
                                    $declFull = $declaring.FullName
                                    if ($declFull) {
                                        $tick2 = $declFull.IndexOf('`')
                                        if ($tick2 -ge 0) { $declFull = $declFull.Substring(0, $tick2) }
                                        if (-not (Test-AllowedMember $declaring.Assembly.GetName().Name $declFull $member.Name)) {
                                            $k = $declFull + '::' + $member.Name
                                            if (-not $memberFindings.ContainsKey($k)) {
                                                $memberFindings[$k] = "member '" + $member.Name + "' of '" + $declFull + "' is prohibited"
                                            }
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }
                }
                $i += $prefix + $size
            }
            continue
        }

        $fi = $m -as [System.Reflection.FieldInfo]
        if ($fi) { Add-Type-Ref $fi.FieldType; continue }
        $pi = $m -as [System.Reflection.PropertyInfo]
        if ($pi) { Add-Type-Ref $pi.PropertyType }
    }
}

# Only references into the game and the framework matter; the mod's own types never do.
$findings = [System.Collections.Generic.List[object]]::new()
foreach ($ref in $found.Values) {
    if ($ref.Assembly -eq $own) { continue }
    if (Test-AllowedTypeName $ref.Assembly $ref.Type) { continue }

    $ns = Get-NamespaceOf $ref.Type
    $reason = if ($AllowedNamespaces.Contains($ns)) {
        "namespace '$ns' is whitelisted but the type is not"
    } else {
        "namespace '$ns' is not whitelisted for mods"
    }
    $findings.Add([pscustomobject]@{ Assembly = $ref.Assembly; Type = $ref.Type; Reason = $reason })
}
foreach ($k in $memberFindings.Keys) {
    $findings.Add([pscustomobject]@{ Assembly = ''; Type = $k; Reason = $memberFindings[$k] })
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host 'VGS whitelist gate'
    Write-Host "  assembly  : $Assembly"
    Write-Host "  whitelist : $($AllowedNamespaces.Count) namespace(s), $($AllowedTypes.Count) type(s), $($AllowedMembers.Count) member(s), decoded from the game"
    Write-Host "  refs      : $($found.Count) distinct type reference(s)"
    Write-Host ''
}

if ($findings.Count -eq 0) {
    Write-Host 'WHITELIST OK — every referenced type is allowed for a mod script' -ForegroundColor Green
    exit 0
}

Write-Host "WHITELIST FAILED — $($findings.Count) prohibited reference(s)" -ForegroundColor Red
Write-Host ''
foreach ($f in ($findings | Sort-Object Assembly, Type)) {
    Write-Host ("  {0}  {1}" -f $f.Type, $f.Reason) -ForegroundColor Red
}
Write-Host ''
Write-Host 'A mod ships only .cs; the game compiles it and then rejects these at world load.'
Write-Host 'Move the work to a whitelisted API, or the mod will fail with "is prohibited".'
Write-Host 'Run this script with -Dump to see what the game actually allows.'
exit 1
