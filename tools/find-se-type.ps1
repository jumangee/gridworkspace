<#
.SYNOPSIS
    Finds which Space Engineers assembly defines a type, and lists matching type names.

.DESCRIPTION
    A mod's `using` directives are guesses until you know which assembly a type lives in.
    Several types sit in surprising places — `MyObjectBuilder_SessionComponent` is in
    VRage.Game, not VRage.Game.Components — and guessing wrong costs a compile round trip.

    This script loads every assembly in the game's bin folder and reports the assembly and
    namespace for each matching type. Read-only: no game code is executed.

.PARAMETER Bin
    Folder with the game assemblies.

.PARAMETER Name
    Type name or a mask with `*` wildcards. Matched against the simple name.

.PARAMETER Namespace
    Optional substring filter on the full name, to narrow noisy results.

.PARAMETER IncludeInternal
    Also report non-public types. Useful to confirm that something you saw in a stack trace
    is real but inaccessible.

.EXAMPLE
    pwsh -File tools/find-se-type.ps1 -Name 'IMyCockpit'

.EXAMPLE
    pwsh -File tools/find-se-type.ps1 -Name 'MyGui*' -Namespace 'Sandbox.Graphics.GUI'
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [Parameter(Mandatory = $true)][string] $Name,
    [string] $Namespace = '',
    [switch] $IncludeInternal
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

# Skip assemblies that cannot contain the types we care about, and the ones that throw on
# GetTypes() because of missing optional dependencies.
$skip = @(
    'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll',
    'EOSSDK.dll', 'EOSSDK-Shipping.dll', 'steam_api64.dll', 'GfnRuntimeSdk.dll',
    'AnselSDK64.dll', 'Optick.dll', 'Havok.dll', 'opus.dll', 'd3dcompiler_47.dll',
    'd3dcsx_47.dll', 'GFSDK_Aftermath_Lib.x64.dll'
)

$pattern = $Name
$results = [System.Collections.Generic.List[object]]::new()

foreach ($file in (Get-ChildItem -Path $Bin -Filter '*.dll' -File)) {
    if ($skip -contains $file.Name) { continue }

    try {
        $asm = [System.Reflection.Assembly]::LoadFrom($file.FullName)
    } catch { continue }

    $types = $null
    try {
        $types = $asm.GetTypes()
    } catch [System.Reflection.ReflectionTypeLoadException] {
        # Partially loadable: keep whatever resolved rather than dropping the assembly.
        $types = $_.Exception.Types | Where-Object { $null -ne $_ }
    } catch { continue }

    if (-not $types) { continue }

    foreach ($t in $types) {
        if ($null -eq $t) { continue }
        if (-not $IncludeInternal -and -not $t.IsPublic -and -not $t.IsNestedPublic) { continue }

        if ($t.Name -notlike $pattern) { continue }
        if ($Namespace -and $t.FullName -notlike "*$Namespace*") { continue }

        $results.Add([pscustomobject]@{
            Type      = $t.FullName
            Kind      = if ($t.IsInterface) { 'interface' } elseif ($t.IsEnum) { 'enum' } elseif ($t.IsValueType) { 'struct' } else { 'class' }
            Public    = $t.IsPublic -or $t.IsNestedPublic
            Assembly  = $asm.GetName().Name
        })
    }
}

if ($results.Count -eq 0) {
    Write-Host "No type matching '$pattern'" -NoNewline
    if ($Namespace) { Write-Host " in namespace containing '$Namespace'" -NoNewline }
    Write-Host '.'
    Write-Host ''
    Write-Host 'Try a wider mask, or pass -IncludeInternal if the type may be internal.'
    exit 0
}

Write-Host "Found $($results.Count) matching type(s):" -ForegroundColor Cyan
Write-Host ''
$results |
    Sort-Object Assembly, Type |
    Format-Table -AutoSize -Property Assembly, Kind, Public, Type |
    Out-String -Width 200 |
    ForEach-Object { $_ }
