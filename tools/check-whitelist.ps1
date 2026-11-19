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

    Where the rules come from: the whitelist is built in code, in
    MySandboxGame.InitIlChecker() and MySpaceGameDefaultIlChecker.InitIlChecker(). The table
    below is transcribed from the generated listing of those two methods
    (https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist, "last
    checked for v1.211.017"). tools/read-se-whitelist.ps1 decodes the same registration from
    the installed DLLs; when the game updates, re-run it and re-diff this table.

    Two rules decide everything, and both are easy to get wrong:

      * An allow is scoped to the assembly that declares the type, not to the namespace name.
        `Sandbox.ModAPI.*` is allowed from Sandbox.Common.dll and from Sandbox.Game.dll, so
        `MyAPIGateway` (Sandbox.Common) and `MyModScreen` (Sandbox.Game) are both usable.

      * Sandbox.Graphics.dll has NO whitelist section at all. The engine GUI —
        MyGuiScreenBase, MyGuiManager, MyScreenManager, MyGuiControl* — is therefore
        unavailable to mods, no matter that its namespace looks plausible.

.PARAMETER Assembly
    Compiled mod assembly to inspect. Defaults to the newest output of verify-compile.ps1.

.PARAMETER Bin
    Game Bin64 folder, used to resolve type references to their declaring assembly.

.PARAMETER Quiet
    Only print the verdict line.

.EXAMPLE
    pwsh -File tools/verify-compile.ps1; pwsh -File tools/check-whitelist.ps1
#>
[CmdletBinding()]
param(
    [string] $Assembly,
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------
# The whitelist, transcribed from the game's own registration.
#
# Each entry maps a declaring assembly to the namespaces it allows wholesale and to the
# individual type prefixes it allows (`Foo.Bar` covers `Foo.Bar` and every member of it).
# An assembly that appears nowhere in this table allows nothing.
# ---------------------------------------------------------------------------------------
$Whitelist = @{
    'Sandbox.Common' = @{
        Namespaces = @(
            'Sandbox.ModAPI', 'Sandbox.ModAPI.Ingame',
            'Sandbox.ModAPI.Interfaces', 'Sandbox.ModAPI.Interfaces.Terminal'
        )
        Types = @()
    }
    'Sandbox.Game' = @{
        Namespaces = @(
            'Sandbox.Definitions', 'Sandbox.Definitions.GUI',
            'Sandbox.Game', 'Sandbox.Game.Components', 'Sandbox.Game.Entities',
            'Sandbox.Game.Entities.Character.Components', 'Sandbox.Game.EntityComponents',
            'Sandbox.Game.GameSystems.TextSurfaceScripts', 'Sandbox.Game.Lights',
            'Sandbox.Game.WorldEnvironment',
            'Sandbox.ModAPI', 'Sandbox.ModAPI.Contracts', 'Sandbox.ModAPI.Weapons'
        )
        Types = @(
            'Sandbox.Engine.Physics.MyPhysics', 'Sandbox.Engine.Physics.MyPhysicsHelper',
            'Sandbox.Engine.Utils.MySpectatorCameraController',
            'Sandbox.Game.Gui.MyHudBlockInfo', 'Sandbox.Game.Gui.TerminalActionExtensions',
            'Sandbox.Game.Localization.MySpaceTexts',
            'Sandbox.Game.Weapons.MyAmmoBase', 'Sandbox.Game.Weapons.MyGunBase',
            'Sandbox.Game.Weapons.MyToolBase',
            'VRage.Game.ModAPI.Ingame.MyPhysicalInventoryItemExtensions_ModAPI',
            'VRage.Input.MyInputExtensions'
        )
    }
    'SpaceEngineers.Game' = @{
        Namespaces = @(
            'SpaceEngineers.Game.Definitions.SafeZone',
            'SpaceEngineers.Game.ModAPI', 'SpaceEngineers.Game.ModAPI.Ingame'
        )
        Types = @()
    }
    'SpaceEngineers.ObjectBuilders' = @{
        Namespaces = @(
            'ObjectBuilders.SafeZone', 'Sandbox.Common.ObjectBuilders',
            'Sandbox.Common.ObjectBuilders.Definitions', 'SpaceEngineers.Game.ModAPI.Ingame'
        )
        Types = @('Sandbox.Game.MyExplosionFlags')
    }
    'VRage' = @{
        Namespaces = @('VRage.Utils', 'VRage.Voxels')
        Types = @(
            'VRage.DistantSound', 'VRage.IMyXmlSerializable', 'VRage.Input.MyJoystickAxesEnum',
            'VRage.IPacketData', 'VRage.MyAudioWave', 'VRage.MyBoneIndexWeight',
            'VRage.MyCubeInstanceData', 'VRage.MyDebugUtils', 'VRage.MyDecalTriangle_Data',
            'VRage.MyDeltaTransform', 'VRage.MyInstanceData', 'VRage.MyLanguagesEnum',
            'VRage.MyLocalizationPackage', 'VRage.MyMultipleEnabledEnum', 'VRage.MyOrientation',
            'VRage.MyPacket', 'VRage.MyPacketDataBitStreamBase', 'VRage.MyPositionAndOrientation',
            'VRage.MyResourceStateEnum', 'VRage.MySimpleProfiler', 'VRage.MySpectator',
            'VRage.MySpectatorCameraMovementEnum', 'VRage.MyStructDefault',
            'VRage.MyTexts', 'VRage.MyTriangle_BoneIndicesWeigths', 'VRage.MyTriangle_Colors',
            'VRage.MyTriangle_Coords', 'VRage.MyTriangle_Normals', 'VRage.MyTriangle_Vertices',
            'VRage.MyVertex_BoneIndicesWeights', 'VRage.MyVertexFormatVoxelSingleData',
            'VRage.MyXmlSerializerBase', 'VRage.MyXmlSerializerManager',
            'VRage.Network.IMyEventProxy', 'VRage.SerializableBoundingBoxD',
            'VRage.SerializableBounds', 'VRage.SerializableOrientedBoundingBoxD',
            'VRage.SerializableQuaternion', 'VRage.SerializableVector2',
            'VRage.SerializableVector2I', 'VRage.SerializableVector3', 'VRage.SerializableVector3D',
            'VRage.SerializableVector3I', 'VRage.SerializableVector3UByte',
            'VRage.StructDefaultAttribute', 'VRage.Sync.SyncDirection', 'VRage.Sync.SyncExtensions'
        )
    }
    'VRage.Game' = @{
        Namespaces = @(
            'Sandbox.Common.ObjectBuilders.Definitions',
            'VRage.Game', 'VRage.Game.Components', 'VRage.Game.Components.Interfaces',
            'VRage.Game.Definitions', 'VRage.Game.Definitions.Reputation',
            'VRage.Game.Definitions.SessionComponents', 'VRage.Game.Entity',
            'VRage.Game.Entity.UseObject', 'VRage.Game.GUI.TextPanel', 'VRage.Game.ModAPI',
            'VRage.Game.ModAPI.Ingame', 'VRage.Game.ModAPI.Ingame.Utilities',
            'VRage.Game.ModAPI.Interfaces', 'VRage.Game.ModAPI.Network',
            'VRage.Game.ObjectBuilders', 'VRage.Game.ObjectBuilders.Components',
            'VRage.Game.ObjectBuilders.ComponentSystem',
            'VRage.Game.ObjectBuilders.Definitions',
            'VRage.ModAPI', 'VRage.ObjectBuilders'
        )
        Types = @(
            'VRage.Game.Models.MyIntersectionResultLineTriangle',
            'VRage.Game.Models.MyIntersectionResultLineTriangleEx',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.MyGridPlacementSettings',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.MyObjectBuilder_CubeBuilderDefinition',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.MyPlacementSettings',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.SnapMode',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.VoxelPlacementMode',
            'VRage.Game.ObjectBuilders.Definitions.SessionComponents.VoxelPlacementSettings'
        )
    }
    'VRage.Input' = @{
        Namespaces = @('VRage.ModAPI')
        Types = @(
            'VRage.Input.IMyControllerControl', 'VRage.Input.MyControlStateType',
            'VRage.Input.MyControlType', 'VRage.Input.MyGuiControlTypeEnum',
            'VRage.Input.MyGuiInputDeviceEnum', 'VRage.Input.MyJoystickButtonsEnum',
            'VRage.Input.MyKeys', 'VRage.Input.MyMouseButtonsEnum', 'VRage.Input.MySharedButtonsEnum'
        )
    }
    'VRage.Library' = @{
        Namespaces = @(
            'System.Collections.Generic', 'VRage', 'VRage.Collections',
            'VRage.Library.Utils', 'VRage.Utils'
        )
        Types = @(
            'ParallelTasks.IWork', 'ParallelTasks.Task', 'ParallelTasks.WorkOptions',
            'System.ArrayExtensions', 'System.Text.StringBuilderExtensions_Format',
            'VRage.Library.Threading.SpinLock', 'VRage.Library.Threading.SpinLockRef',
            'VRage.MyFixedPoint', 'VRage.MyTuple', 'VRage.MyTupleComparer',
            'VRage.Serialization.SerializableDictionary'
        )
    }
    'VRage.Math' = @{
        Namespaces = @('VRageMath')
        Types = @()
    }
    'VRage.Render' = @{
        Namespaces = @('VRage.Game.ModAPI')
        Types = @(
            'VRageRender.Import.MyMeshDrawTechnique', 'VRageRender.Lights.MyGlareTypeEnum',
            'VRageRender.Messages.MyDecalPositionUpdate', 'VRageRender.Messages.MyHBAOData',
            'VRageRender.Messages.MyTextureChange', 'VRageRender.MyBillboard',
            'VRageRender.MyDecalBindingInfo', 'VRageRender.MyDecalFlags',
            'VRageRender.MyDecalRenderInfo', 'VRageRender.MyEnvironmentData',
            'VRageRender.MyEnvironmentLightData', 'VRageRender.MyLodTypeEnum',
            'VRageRender.MyPostprocessSettings', 'VRageRender.MyShadowsSettings',
            'VRageRender.MySSAOSettings', 'VRageRender.MyTriangleBillboard'
        )
    }
    'VRage.Scripting' = @{
        Namespaces = @('VRage.Scripting.MemorySafeTypes')
        Types = @()
    }
    'VRage.UserInterface' = @{
        # Not listed in the whitelist at all; kept explicit so a future allow is a visible edit.
        Namespaces = @()
        Types = @()
    }
    'mscorlib' = @{
        Namespaces = @(
            'System.Collections', 'System.Collections.Concurrent', 'System.Collections.Generic',
            'System.Globalization', 'System.Text'
        )
        Types = @(
            'System.Action', 'System.ArgumentException', 'System.ArgumentNullException',
            'System.Array', 'System.BitConverter', 'System.Buffer', 'System.Convert',
            'System.DateTime', 'System.Delegate', 'System.DivideByZeroException',
            'System.Enum', 'System.Environment', 'System.EventArgs', 'System.Exception',
            'System.FormatException', 'System.Func', 'System.Guid', 'System.IComparable',
            'System.IDisposable', 'System.IEquatable', 'System.InvalidCastException',
            'System.InvalidOperationException',
            'System.IO.BinaryReader', 'System.IO.BinaryWriter', 'System.IO.FileNotFoundException',
            'System.IO.Path', 'System.IO.Stream', 'System.IO.TextReader', 'System.IO.TextWriter',
            'System.Math', 'System.MidpointRounding', 'System.NotSupportedException',
            'System.Nullable', 'System.NullReferenceException', 'System.Object',
            'System.Random', 'System.Reflection.MemberInfo', 'System.RuntimeType',
            'System.String', 'System.StringComparer', 'System.StringComparison',
            'System.StringSplitOptions', 'System.Threading.AutoResetEvent',
            'System.Threading.Interlocked', 'System.Threading.ManualResetEvent',
            'System.Threading.Monitor', 'System.TimeSpan', 'System.Type', 'System.TypeCode',
            'System.ValueType', 'System.Version',
            # Primitives are whitelisted by C# keyword.
            'bool', 'byte', 'char', 'decimal', 'double', 'float', 'int', 'long',
            'object', 'sbyte', 'short', 'string', 'uint', 'ulong', 'ushort', 'void'
        )
    }
    'System' = @{
        Namespaces = @(
            'System.Collections.Concurrent', 'System.Collections.Generic',
            'System.Text.RegularExpressions', 'System.Timers'
        )
        Types = @(
            'System.ComponentModel.DefaultValueAttribute', 'System.ComponentModel.INotifyPropertyChanged',
            'System.ComponentModel.INotifyPropertyChanging',
            'System.ComponentModel.PropertyChangedEventArgs',
            'System.ComponentModel.PropertyChangedEventHandler',
            'System.ComponentModel.PropertyChangingEventArgs',
            'System.ComponentModel.PropertyChangingEventHandler',
            'System.Diagnostics.Stopwatch', 'System.Diagnostics.TraceEventType'
        )
    }
    'System.Core' = @{
        Namespaces = @('System.Collections.Generic', 'System.Linq')
        Types = @('System.Action', 'System.Func')
    }
    'System.Xml' = @{
        Namespaces = @()
        Types = @('System.Xml.Serialization.Xml')
    }
    'ProtoBuf.Net.Core' = @{
        Namespaces = @()
        Types = @('ProtoBuf.')
    }
}

# Assemblies the mod may reference at all. Anything else is a red flag on its own.
$KnownAssemblies = @($Whitelist.Keys)

# Assemblies that exist but have no whitelist section. Named explicitly because the error
# they cause ("is prohibited") does not say which assembly was at fault.
$AssembliesWithNoWhitelist = @('Sandbox.Graphics', 'VRage.Render11', 'VRage.Ansel', 'EmptyKeys.UserInterface')

# This script runs on modern .NET, where framework types report System.Private.CoreLib and
# friends. The game compiles mods against .NET Framework, where the same types live in
# mscorlib/System/System.Core/System.Xml, and the whitelist is keyed by those names. Mapping
# keeps the comparison honest instead of flagging every framework type as unlisted.
$FrameworkAssemblyMap = @{
    'System.Private.CoreLib' = 'mscorlib'
    'System.Runtime' = 'mscorlib'
    'System.Collections' = 'mscorlib'
    'System.Threading' = 'mscorlib'
    'System.Text.Encoding.Extensions' = 'mscorlib'
    'netstandard' = 'mscorlib'
    'System.Linq' = 'System.Core'
    'System.Xml' = 'System.Xml'
    'System.Xml.ReaderWriter' = 'System.Xml'
    'System.Text.RegularExpressions' = 'System'
    'System.ComponentModel' = 'System'
    'System.ComponentModel.Primitives' = 'System'
    'System.Diagnostics.Debug' = 'System'
    'System.Diagnostics.TraceSource' = 'System'
    'System' = 'System'
}

function Get-WhitelistAssemblyName([string] $AssemblyName) {
    if ($FrameworkAssemblyMap.ContainsKey($AssemblyName)) { return $FrameworkAssemblyMap[$AssemblyName] }
    return $AssemblyName
}

# `List`1` is not how the whitelist spells it; drop the arity suffix and generic arguments so
# a closed generic compares the same way as its definition.
function Get-WhitelistTypeName([Type] $Type) {
    $t = if ($Type.IsGenericType) { $Type.GetGenericTypeDefinition() } else { $Type }
    $name = $t.FullName
    if ($null -eq $name) { return $null }
    $tick = $name.IndexOf('`')
    if ($tick -ge 0) {
        $end = $name.IndexOf('[', $tick)
        $name = if ($end -ge 0) { $name.Substring(0, $tick) + $name.Substring($end) } else { $name.Substring(0, $tick) }
    }
    return $name
}

# The whitelist spells the primitives with C# keywords (`int.*`, `bool.*`), reflection spells
# them System.Int32/System.Boolean. Without this the gate reports every int as prohibited.
$PrimitiveMap = @{
    'System.Boolean' = 'bool'; 'System.Byte' = 'byte'; 'System.Char' = 'char'
    'System.Decimal' = 'decimal'; 'System.Double' = 'double'; 'System.Single' = 'float'
    'System.Int32' = 'int'; 'System.Int64' = 'long'; 'System.Object' = 'object'
    'System.SByte' = 'sbyte'; 'System.Int16' = 'short'; 'System.String' = 'string'
    'System.UInt32' = 'uint'; 'System.UInt64' = 'ulong'; 'System.UInt16' = 'ushort'
    'System.Void' = 'void'
}

function Get-WhitelistTypeName2([Type] $Type) {
    $name = Get-WhitelistTypeName $Type
    if ($null -ne $name -and $PrimitiveMap.ContainsKey($name)) { return $PrimitiveMap[$name] }
    return $name
}

function Test-Allowed([string] $AssemblyName, [string] $TypeName) {
    $rule = $Whitelist[$AssemblyName]
    if (-not $rule) { return $false }

    foreach ($ns in $rule.Namespaces) {
        if ($TypeName -eq $ns -or $TypeName.StartsWith($ns + '.', [StringComparison]::Ordinal)) { return $true }
    }
    foreach ($t in $rule.Types) {
        if ($TypeName -eq $t -or $TypeName.StartsWith($t + '.', [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------------------

if (-not $Assembly) {
    $newest = Get-ChildItem ([System.IO.Path]::GetTempPath()) -Filter 'vgs-verify-*' -Directory -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw 'No verify-compile output found. Run tools/verify-compile.ps1 first, or pass -Assembly.' }
    $Assembly = Join-Path $newest.FullName 'VGS.verify.dll'
}
if (-not (Test-Path $Assembly)) { throw "Assembly not found: $Assembly" }

$resolver = [System.ResolveEventHandler] {
    param($s, $e)
    $n = ($e.Name -split ',')[0]
    $p = Join-Path $Bin "$n.dll"
    if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

# Full opcode table straight from the runtime, so the IL walk cannot silently stop on an
# opcode the hand-written table forgot (that is how read-se-whitelist.ps1 under-reports).
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

$mod = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $Assembly).Path)
$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'

$found = [System.Collections.Generic.Dictionary[string, object]]::new()

function Add-Type-Ref([Type] $t) {
    if ($null -eq $t) { return }
    if ($t.IsGenericType) { foreach ($a in $t.GetGenericArguments()) { Add-Type-Ref $a } }
    if ($t.IsArray -or $t.IsByRef -or $t.IsPointer) { Add-Type-Ref $t.GetElementType(); return }
    if ($t.IsGenericParameter) { return }
    if ($t.IsNested -and $t.DeclaringType) { Add-Type-Ref $t.DeclaringType; return }

    $full = Get-WhitelistTypeName2 $t
    if ($null -eq $full) { return }
    $asmName = Get-WhitelistAssemblyName $t.Assembly.GetName().Name
    $key = "$asmName|$full"
    if ($found.ContainsKey($key)) { return }
    $found[$key] = [pscustomobject]@{ Assembly = $asmName; Type = $full }
}

foreach ($t in $mod.GetTypes()) {
    Add-Type-Ref $t.BaseType

    # Only the interfaces this type declares. GetInterfaces() also returns everything the
    # base type implements, which for an enum means IConvertible and friends — noise that
    # would drown the real findings.
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
                            elseif ($member) { Add-Type-Ref $member.DeclaringType }
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
$own = $mod.GetName().Name
$findings = [System.Collections.Generic.List[object]]::new()
foreach ($ref in $found.Values) {
    if ($ref.Assembly -eq $own) { continue }
    if (Test-Allowed $ref.Assembly $ref.Type) { continue }

    $reason = if ($AssembliesWithNoWhitelist -contains $ref.Assembly) {
        "assembly '$($ref.Assembly)' has no whitelist section at all"
    } elseif (-not $Whitelist.ContainsKey($ref.Assembly)) {
        "assembly '$($ref.Assembly)' is not whitelisted"
    } else {
        "not in the whitelist for '$($ref.Assembly)'"
    }
    $findings.Add([pscustomobject]@{ Assembly = $ref.Assembly; Type = $ref.Type; Reason = $reason })
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host 'VGS whitelist gate'
    Write-Host "  assembly : $Assembly"
    Write-Host "  refs     : $($found.Count) distinct type reference(s)"
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
exit 1
